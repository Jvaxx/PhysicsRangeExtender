#!/usr/bin/env bash
# Build PhysicsRangeExtender.dll against a local KSP install with Mono's csc.
#
#   ./build.sh           compile to build/PhysicsRangeExtender.dll
#   ./build.sh install   compile, then copy the DLL into the KSP install
#   ./build.sh release   compile, copy the DLL into Distribution/, then write
#                        build/PhysicsRangeExtender-<version>.zip and .ckan
#
# KSP_DIR selects the install (default: ~/Games/KSP-kOS). Close KSP before
# installing. RELEASE_REPO (default: the GitHub repo of the origin remote)
# sets the download URL written into the .ckan file: the zip must be
# attached to that repo's release tagged <version>.
set -euo pipefail

repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
ksp_dir=${KSP_DIR:-$HOME/Games/KSP-kOS}
managed="$ksp_dir/KSP_x64_Data/Managed"
src="$repo/PhysicsRangeExtender"
out="$repo/build/PhysicsRangeExtender.dll"

case "${1:-}" in
    ''|install|release) ;;
    *) echo "usage: $0 [install|release]" >&2; exit 2 ;;
esac

if [[ ! -f "$managed/Assembly-CSharp.dll" ]]; then
    echo "No KSP assemblies in $managed (set KSP_DIR)." >&2
    exit 1
fi
if ! command -v csc >/dev/null 2>&1; then
    echo 'csc not found (install mono).' >&2
    exit 1
fi

refs=()
for name in Assembly-CSharp KSPAssets UnityEngine UnityEngine.CoreModule \
    UnityEngine.IMGUIModule UnityEngine.PhysicsModule UnityEngine.UI \
    UnityEngine.TextRenderingModule UnityEngine.AnimationModule \
    UnityEngine.AssetBundleModule UnityEngine.ImageConversionModule \
    UnityEngine.TextCoreModule UnityEngine.UIElementsModule \
    mscorlib System System.Core; do
    refs+=("-r:$managed/$name.dll")
done

mkdir -p -- "$repo/build"
csc -nologo -nostdlib -noconfig -target:library -optimize+ -warn:4 \
    -out:"$out" "${refs[@]}" \
    "$src"/*.cs "$src/Properties/AssemblyInfo.cs"
echo "built $out"
md5sum "$out"

if [[ ${1:-} == install ]]; then
    dest="$ksp_dir/GameData/PhysicsRangeExtender/Plugins"
    if [[ ! -d "$dest" ]]; then
        echo "PhysicsRangeExtender is not installed in $ksp_dir." >&2
        exit 1
    fi
    install -m 644 "$out" "$dest/PhysicsRangeExtender.dll"
    echo "installed to $dest"
fi

if [[ ${1:-} == release ]]; then
    dist="$src/Distribution"
    mod="$dist/GameData/PhysicsRangeExtender"
    version=$(sed -n 's/^\[assembly: AssemblyVersion("\([0-9.]*\)")\]$/\1/p' \
        "$src/Properties/AssemblyInfo.cs")
    [[ -n $version ]] || { echo "No AssemblyVersion found." >&2; exit 1; }

    # The version must agree everywhere players and CKAN read it.
    avc=$(python3 "$repo/tools/ckan.py" avc-version "$mod")
    [[ $avc == "$version" ]] || { echo ".version says $avc, AssemblyInfo $version." >&2; exit 1; }
    head -1 "$mod/ChangeLog.txt" | grep -qx "= v$version" ||
        { echo "ChangeLog.txt does not start with '= v$version'." >&2; exit 1; }

    install -m 644 "$out" "$mod/Plugins/PhysicsRangeExtender.dll"

    zipfile="$repo/build/PhysicsRangeExtender-$version.zip"
    rm -f -- "$zipfile"
    (cd "$dist" && zip -X -q -r "$zipfile" GameData/PhysicsRangeExtender)
    echo "packed $zipfile"
    unzip -l "$zipfile"

    slug=${RELEASE_REPO:-$(git -C "$repo" remote get-url origin |
        sed -E 's#^(https://github.com/|git@github.com:)##; s#\.git$##')}
    ckanfile="$repo/build/PhysicsRangeExtender-$version.ckan"
    python3 "$repo/tools/ckan.py" ckan "$zipfile" "$version" "$slug" "$mod" > "$ckanfile"
    echo "wrote $ckanfile"
fi
