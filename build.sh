#!/usr/bin/env bash
# Build PhysicsRangeExtender.dll against a local KSP install with Mono's csc.
#
#   ./build.sh           compile to build/PhysicsRangeExtender.dll
#   ./build.sh install   compile, then copy the DLL into the KSP install
#
# KSP_DIR selects the install (default: ~/Games/KSP-kOS). Close KSP before
# installing.
set -euo pipefail

repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
ksp_dir=${KSP_DIR:-$HOME/Games/KSP-kOS}
managed="$ksp_dir/KSP_x64_Data/Managed"
src="$repo/PhysicsRangeExtender"
out="$repo/build/PhysicsRangeExtender.dll"

case "${1:-}" in
    ''|install) ;;
    *) echo "usage: $0 [install]" >&2; exit 2 ;;
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
