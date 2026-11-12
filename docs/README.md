# PRE analysis notes

Written 2026-11-12, against commit `2b83a71` (branch
`fix-situation-change-leak` = upstream master 1.21.0 + the
`onVesselSituationChange` leak fix) and KSP 1.12.5.3190.

| File | Content |
|------|---------|
| [analysis.md](analysis.md) | Every problem found, with file:line, how sure it is, and a suggested fix. Ends with a fix order and an in-game test plan. |
| [plan.md](plan.md) | **Start here to implement.** Step-by-step fix plan, decisions taken, per-step flight checks. |
| [ksp-internals.md](ksp-internals.md) | What stock KSP 1.12.5 really does in the places PRE touches, read from the IL of `Assembly-CSharp.dll`. |
| [tools/ilx.py](tools/ilx.py) | Helper to print a class or method from a `monodis` dump, with KSP's obfuscation noise removed. |
| [tools/csclean.py](tools/csclean.py) | Strips the same noise from `ilspycmd` C# output and can print single methods: `python3 docs/tools/csclean.py /tmp/Vessel.cs GoOffRails`. |
| [tools/ilspycmd](tools/ilspycmd) | Repo-local C# decompiler; install with [tools/install-ilspycmd](tools/install-ilspycmd). |
| [tools/unitypy-python](tools/unitypy-python) | Python with UnityPy for inspecting serialized Unity assets; install with [tools/install-unitypy](tools/install-unitypy). |

## Where things stand

- Branch `rework-1.12.5` holds these notes and steps 0 to 4 of `plan.md`,
  one commit per step: lifecycle fixes, near-clip and origin code removed,
  separate landed physics range, and `LandedVesselGuard` in place of the
  terrain extender and freeze logic. A fifth commit adds the space physics
  range and the three-field GUI (decision D6 in `plan.md`). Builds without
  warnings. The user flew the result on 2026-11-12 and reported it works
  (an earlier flight showed far unpacked vessels shaking, which led to
  D6); the step 4 check table was not reported item by item. Step 5's
  review ran on 2026-11-12 and its fixes are in (see `plan.md`, step 5);
  they build but are not flown. Next: fly the step 4 table plus the four
  checks added under step 5. The user chose to go on as if they pass.
  Step 6 (settings file in `PluginData`) is committed; the user saw the
  migration and the GUI save work. Step 7 (GUI) is done, builds, not
  flown, not committed. Step 9's spike (terrain under a far descending
  vessel, off by default) is implemented and builds, not flown. Step 8 is
  still to do. The findings below describe the code before these steps.
- Remotes: `origin` is the user's own GitHub repo, `upstream` is
  jrodrigv/PhysicsRangeExtender (push disabled).
- The source builds cleanly (no warnings) with `./build.sh`.
- The headline results:
  1. PRE's write to `FloatingOrigin.threshold` does nothing in 1.12.5. The
     terrain extender and the near-clip "flickering correction" were both
     designed around that write working.
  2. PRE overwrites the No Crash Damage and Unbreakable Joints cheats every
     frame, and can leave them stuck on for the whole session.
  3. Landed vessels that PRE "freezes" on a vessel switch are never released.
  4. A flight scene started around the Sun or Jool throws in `Start()` and
     PRE stays inactive for that scene.
  5. Even with the leak fixed, PRE restarts Kerbin's whole terrain at every
     flight scene start and at every atmospheric entry (about 2 s each with
     Parallax, from the earlier investigation).
- Not tested in game. Each finding in `analysis.md` says whether it is proven
  from code or still needs a flight to confirm.

## Environment

- KSP installs: Steam (`~/.local/share/Steam/steamapps/common/Kerbal Space
  Program`, stock) and `~/Games/KSP-kOS`, `KSP-Career`, `KSP-kOSIM-dev`. All
  are 1.12.5.3190 (Windows build under Proton) with the same
  `Assembly-CSharp.dll` (md5 `96b01ccc…`).
- Only `KSP-kOS` has PRE. Its DLL is the branch build (md5 `d40bd452…`, same
  as `build/PhysicsRangeExtender.dll`). Its `settings.cfg` has
  `ModEnabled = False`, and the last `KSP.log` (2026-11-11) has no
  `Updating ranges` line: **PRE was inactive in that session, so the leak fix
  has not been exercised in the latest log.**
- Earlier work on the freeze bug: `~/Games/KSP-kOS/bug-investigation.md`.
- `build/` is ignored through `.git/info/exclude`. It holds the original
  1.21.0 DLL and the branch build.

## Build

`./build.sh` from the repo root (`./build.sh install` also copies the DLL
into `$KSP_DIR`, default `~/Games/KSP-kOS`). The csproj is unusable here, see
B8 in `analysis.md`. The script runs the equivalent of this, from
`PhysicsRangeExtender/`:

```sh
M=~/Games/KSP-kOS/KSP_x64_Data/Managed
csc -nologo -nostdlib -noconfig -target:library -optimize+ \
  -out:../build/PhysicsRangeExtender.dll \
  $(for d in Assembly-CSharp KSPAssets UnityEngine UnityEngine.CoreModule \
    UnityEngine.IMGUIModule UnityEngine.PhysicsModule UnityEngine.UI \
    UnityEngine.TextRenderingModule UnityEngine.AnimationModule \
    UnityEngine.AssetBundleModule UnityEngine.ImageConversionModule \
    UnityEngine.TextCoreModule UnityEngine.UIElementsModule \
    mscorlib System System.Core; do echo -r:$M/$d.dll; done) \
  -warn:4 *.cs Properties/AssemblyInfo.cs
```

## Reading stock KSP code

`ilspycmd` is now available locally through `docs/tools/ilspycmd`. The installer
pins version **11.2.0.10050** from the official NuGet `ilspycmd.linux-x64`
package. This Linux x64 build is native: no .NET SDK or runtime is required.
The downloaded binary is Git-ignored; the launcher and installer can be tracked.
No system packages or PATH settings are changed.

From the repository root:

```sh
# Install/reinstall (requires curl, unzip, and network access):
docs/tools/install-ilspycmd

docs/tools/ilspycmd --version
M=~/Games/KSP-kOS/KSP_x64_Data/Managed
# Read one class as C# (use a fully qualified type name if namespaced):
docs/tools/ilspycmd -r "$M" -t FloatingOrigin "$M/Assembly-CSharp.dll"
docs/tools/ilspycmd -r "$M" -t Vessel "$M/Assembly-CSharp.dll" > /tmp/Vessel.cs
# Decompile PRE into a project, one source file per type:
docs/tools/ilspycmd -r "$M" -p -o /tmp/pre-csharp build/PhysicsRangeExtender.dll
```

The launcher disables automatic update checks and works from any directory when
invoked by its path. Run `docs/tools/ilspycmd --help` for more options.
Decompilation reconstructs readable C#, not the original comments or all local
variable names. Obfuscated names may remain; consult IL for ambiguous results.
Keep decompiled game source outside the repository.

The original analysis used IL via `monodis` and `ikdasm`, which remain useful:

```sh
M=~/Games/KSP-kOS/KSP_x64_Data/Managed
monodis --output=/tmp/asm.il $M/Assembly-CSharp.dll        # 111 MB, ~9 s
python3 docs/tools/ilx.py /tmp/asm.il FloatingOrigin        # list members
python3 docs/tools/ilx.py /tmp/asm.il Vessel '^GoOffRails$' # one method
# PRE's own DLLs need the reference path, or monodis segfaults:
MONO_PATH=$M monodis --output=/tmp/pre.il build/PhysicsRangeExtender.dll
```

Do not copy the dump into the repo (size, and it is Squad's code).

## Reading Unity assets with UnityPy

**UnityPy 1.25.4** is installed in the Git-ignored
`docs/tools/.unitypy-venv/` environment. It reads serialized assets, scenes,
and prefabs, rather than decompiling C# code. System Python is unchanged.
The installer uses `uv` and Python 3.13 (downloaded by uv if needed).

```sh
# Install/reinstall (requires uv and network access):
docs/tools/install-unitypy

# Run a Python script with UnityPy available, without activating the environment:
docs/tools/unitypy-python my_asset_script.py
# Or use an interactive Python session:
docs/tools/unitypy-python
```

Read-only example, listing object types and GameObject names in KSP's first scene:

```sh
docs/tools/unitypy-python - <<'PY'
from collections import Counter
from pathlib import Path
import UnityPy

scene = Path.home() / 'Games/KSP-kOS/KSP_x64_Data/level0'
env = UnityPy.load(str(scene))
print(Counter(obj.type.name for obj in env.objects))
for obj in env.objects:
    if obj.type.name == 'GameObject':
        print(obj.path_id, obj.parse_as_dict().get('m_Name'))
PY
```

Load individual files rather than the entire game directory to limit memory use.
Do not save changes over game assets or commit extracted game data here.

Several values PRE depends on are serialized in scenes and prefabs, not in code:
`FloatingOrigin.threshold`, `FlightCamera.camera00DefaultNearClipPlane`, and each
body's `PQS.visRadSeaLevelValue` / `collapseSeaLevelValue`. Reading custom
`MonoBehaviour` fields may require type trees that a shipped asset omits.
UnityPy's optional `TypeTreeGeneratorAPI` support can generate these from game
assemblies, but is not included in this installation. Installing UnityPy alone
does not guarantee these fields are readable. A temporary `Debug.Log` in PRE
would also get their runtime values in one flight.
