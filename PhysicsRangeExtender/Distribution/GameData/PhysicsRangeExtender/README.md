# Physics Range Extender

Extends how far from the active vessel other vessels are loaded and run
physics in Kerbal Space Program 1.12.3 to 1.12.5. You can see and switch to
vessels far away, and let kOS fly two vessels at once (rendezvous, booster
landing).

Based on code written by BahamutoD for BDArmory and improved by jrodrigv.
Version 1.22 is a rework by Jvaxx.

## Ranges

Set in the toolbar window (flight and map view):

| Setting | Default | What it does |
|---------|--------:|--------------|
| Load range | 100 km | Every vessel within it is loaded. Flying and sub-orbital vessels also run physics within it. |
| Physics range in orbit | 20 km | Orbiting and escaping vessels run physics within it. |
| Physics range landed | 5 km | Landed, splashed and pre-launch vessels run physics within it. |

Beyond its physics range a loaded vessel stays packed ("on rails"). It is
visible, and a kOS CPU on it keeps running, but it cannot move: throttle and
steering have no effect. A script that must launch a landed vessel far from
the active one needs a larger landed physics range.

Physics far from the active vessel runs at low floating-point precision:
far unpacked vessels can shake. Keep the physics ranges as small as your
use allows.

## Landed vessels

KSP builds collider terrain only within a few km of the active vessel.
Without it, a landed vessel falls through the ground. So a landed vessel
stays packed until there is ground under it, and is packed again if its
ground goes away. A landed vessel you switch to waits for its ground before
it unpacks.

Known limit: with the active vessel far away, there is no ground under a
non-active vessel that comes down to land. The experimental setting
`TerrainUnderDescendingVessels` (below) addresses this.

## Settings file

`GameData/PhysicsRangeExtender/PluginData/settings.cfg`, created on first
start (values from an old `GameData/PhysicsRangeExtender/settings.cfg` are
taken over once). Besides the three ranges (`GlobalRange`,
`SpacePhysicsRange`, `LandedPhysicsRange`, in km) and `ModEnabled`:

- `DebugLog = True` writes every decision of the mod to `KSP.log`.
- `TerrainUnderDescendingVessels = True` (experimental, off by default):
  while the active vessel is more than 20 km above the ground, terrain is
  built under a far vessel descending below 5 km instead of under the
  active vessel, so the far vessel can land.

## For other mods

`PhysicsRangeExtender.PRExtensions.PreOff(modName)` and `PreOn(modName)`
turn the mod off and back on.
