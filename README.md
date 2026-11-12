# Physics Range Extender

KSP 1.12 mod that extends the load and physics range of vessels. What it
does and its settings for players: [the mod's README](PhysicsRangeExtender/Distribution/GameData/PhysicsRangeExtender/README.md).
Changes: [ChangeLog.txt](PhysicsRangeExtender/Distribution/GameData/PhysicsRangeExtender/ChangeLog.txt).

This is a fork of [jrodrigv/PhysicsRangeExtender](https://github.com/jrodrigv/PhysicsRangeExtender)
(1.21.0), reworked for KSP 1.12.3 to 1.12.5 from version 1.22.0.

## Install

- CKAN: download `PhysicsRangeExtender-<version>.ckan` from the
  [latest release](https://github.com/Jvaxx/PhysicsRangeExtender/releases/latest)
  and install it from the command line. CKAN does not upgrade an installed
  module from a local `.ckan` file ("already up to date"), so remove the
  installed version first; `PluginData/settings.cfg` is kept:

  ```sh
  ckan remove PhysicsRangeExtender
  ckan install -c PhysicsRangeExtender-<version>.ckan
  ```

  CKAN downloads the zip from the release. In the CKAN window, remove
  Physics Range Extender, apply, then use *File > Install from .ckan…*.
- By hand: unzip `PhysicsRangeExtender-<version>.zip` into the KSP folder,
  so that `GameData/PhysicsRangeExtender` is replaced.

## Build

Needs Mono (`csc`) and a KSP 1.12 install for the reference assemblies.

```sh
./build.sh            # build/PhysicsRangeExtender.dll
./build.sh install    # also copy the DLL into $KSP_DIR (default ~/Games/KSP-kOS)
./build.sh release    # also update Distribution/, then build/PhysicsRangeExtender-<version>.zip
                      # and its .ckan file (download URL = this repo's release for the tag)
```

The version comes from `PhysicsRangeExtender/Properties/AssemblyInfo.cs`;
keep `PhysicsRangeExtender.version` and `ChangeLog.txt` in step.

`docs/` holds the analysis behind the 1.22 rework, notes on stock KSP
internals, and tools to read the game's code.
