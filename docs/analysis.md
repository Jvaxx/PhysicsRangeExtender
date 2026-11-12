# Physics Range Extender: code analysis

Commit `e9aaa75`, KSP 1.12.5.3190. File paths are relative to
`PhysicsRangeExtender/`. Stock behaviour quoted here is documented in
[ksp-internals.md](ksp-internals.md).

## Status in 1.22.0

The findings below describe the 1.21.0 code. Where each one went (commits
on branch `rework-1.12.5`, released as 1.22.0):

| Finding | Status | Commit |
|---------|--------|--------|
| A1 cheats overwritten | removed with the terrain extender | `e06d605` |
| A2 frozen vessels never released | removed; guard rule 1 replaces it | `e06d605` |
| A3 dead floating-origin write | removed | `2a7f508`, `e06d605` |
| A4 zero `InitialPosition` | removed | `e06d605` |
| A5 focus switching | removed | `e06d605` |
| A6 state machine details | removed | `e06d605` |
| A7 exception around the Sun or Jool | fixed, then the call removed | `3c580e0`, `e06d605` |
| A8 terrain restarts | removed (no PQS writes left) | `e06d605` |
| A9 inertial-frame holes | fixed (guard rule 1) | `e06d605` |
| B1 enable / disable | fixed | `3c580e0` |
| B2 near-clip hack | removed | `2a7f508` |
| B3 per-event cost | fixed (per vessel) | `5274969` |
| B4 settings file | fixed (`PluginData`, validation, migration) | `8399a03` |
| B5 GUI | fixed | `3e8c38c`, `88c4ec8`, `209b0f3` |
| B6 `PRExtensions` not compiled | fixed | `4444d18`, `3c580e0` |
| B7 stale DLL | fixed (rebuilt for the release) | 1.22.0 release commit |
| B8 build setup | `build.sh` (build, install, release); csproj kept, Windows post-build script and `LocalDev` removed | `4444d18`, 1.22.0 release commit |
| B9 nulls in handlers | fixed | `3c580e0` |
| B10 one range for all | fixed (load, orbit and landed ranges) | `5274969`, `3e8c38c`, `88c4ec8` |
| Section 4 cleanup | mostly gone with the deleted code | |

Not covered by a finding but changed too: ground checks for switched-to and
unpacked landed vessels (`88c4ec8`), sub-orbital at full range (`88c4ec8`),
experimental terrain target for descending vessels (`1a5fa9a`, off by
default).

How sure each finding is:

- **Proven**: follows from PRE's source alone, or from PRE's source plus stock
  code that was read in the IL.
- **Likely**: the code path is clear, but the visible result depends on timing
  or on values that were not read (serialized scene values, script order).
- **Open**: a question, not a claim.

Nothing here was tested in game. Section 6 lists the flights that would
confirm the "likely" items.

**Update, same day, after the decompiler was installed.** Three points below
are now settled; details in `ksp-internals.md`, consequences in
[plan.md](plan.md):

- A3 / B2: the floating-origin threshold is 500 m (created in code, not
  serialized). PRE's near-clip formula therefore always gives 0.21, stock's
  own default. The "flickering correction" does nothing in external view and
  replaces stock's 0.1 in IVA. B2 is **proven**.
- A8: `visRadSeaLevelValue = 200` and `collapseSeaLevelValue = 200` do not
  create ground under far vessels. The first widens what is drawn; the second
  only makes already detailed terrain last longer. Terrain is built around
  the active vessel only.
- A9: stock itself refuses to unpack a landed vessel in the inertial frame,
  and can refuse to unpack one that has no collider under it. The plan uses
  both instead of PRE's own logic.

## 1. How the mod works (short)

Four classes, about 1000 lines.

- `PreSettings` (`PRESettings.cs`): loads `GameData/PhysicsRangeExtender/settings.cfg`
  at game start into statics (`GlobalRange` in km, `ModEnabled`,
  `CamFixMultiplier`, `TerrainExtenderEnabled`).
- `PhysicsRangeExtender` (flight scene): gives every vessel the same ranges
  for every situation: load `R`, unload `1.05 R`, pack `1.10 R`, unpack
  `0.99 R`. Reapplies them on vessel create / load / switch / off-rails.
  Also: near-clip-plane hack against flickering, "unload landed vessels"
  when the active vessel nears the inertial-frame altitude, "freeze landed
  vessels" on some switches, and a terrain restart on re-entry.
- `TerrainExtender` (flight scene): raises the terrain detail radius of the
  current body, and runs a state machine on landed vessels that load far
  away: switch focus to the vessel, lift it, lower it until it lands, switch
  back.
- `Gui`: toolbar button and a small IMGUI window.
- `PRExtensions`: static `PreOn` / `PreOff` for other mods.

## 2. High impact

### A1. Cheat options are overwritten every frame, and can stick on (proven)

`PhysicsRangeExtender.cs:168-175`, `TerrainExtender.cs:261-285`.

`FreezeLandedVesselWhenSwitching()` runs from `Update`, `LateUpdate` and
`FixedUpdate`. Whenever `VesselToFreeze` is empty (the normal case) it calls
`DeactivateNoCrashDamage()`, which writes the saved values `_crashDamage` and
`_joints` into `CheatOptions.NoCrashDamage` and `UnbreakableJoints`. Both
saved values are statics that start as `false`.

Results:

1. With PRE enabled, the Alt+F12 cheats No Crash Damage and Unbreakable
   Joints are switched back off within one frame.
2. The terrain extender calls `ActivateNoCrashDamage()` when it starts
   working (`TerrainExtender.cs:67`), and this loop cancels it at once. The
   protection it is meant to give does not exist. Only the
   `crashTolerance >= 1000` bump (A6) remains.
3. `ActivateNoCrashDamage()` saves the *current* values each time. If it is
   called while the cheats are already on (two landed-to-orbital switches
   without the freeze list emptying in between), it saves `true`. From then
   on PRE forces both cheats **on**, every frame, in every flight scene,
   until KSP is restarted.

Fix: keep one "PRE wants protection" counter or flag, save the user's values
once when it goes from 0 to 1, restore once when it returns to 0, and never
write the cheats outside those two moments.

### A2. Frozen landed vessels are never released (proven from source; result likely)

`PhysicsRangeExtender.cs:98-107`, `168-175`.

On `onVesselSwitching(from, to)`, if `from.Landed` and `to` is sub-orbital or
higher, `from` **and every loaded landed or splashed vessel** go into
`VesselToFreeze`. Each frame, three times, every vessel in the list gets
`SetWorldVelocity(0)`. Entries leave the list only when the vessel is
destroyed or unloaded (beyond `1.05 R`).

So after switching from a landed booster to the upper stage and back, the
booster's velocity is zeroed on every physics step: it should not be able to
drive or lift off until it is unloaded. No Crash Damage and Unbreakable
Joints are also meant to stay on for as long as the list is not empty.

The terrain extender triggers this by itself: its switch back to the original
vessel (`TerrainExtender.cs:162`) is a switch from a landed vessel, so if the
original vessel is sub-orbital the freeze applies to everything landed.

Fix: release a vessel when it becomes the active vessel, when the active
vessel is landed again, and after a timeout. Better: remove the mechanism and
rely on keeping landed vessels packed (see section 5).

### A3. The floating-origin write is dead code; the terrain extender depends on it (dead write proven; effect likely)

`PhysicsRangeExtender.cs:255`, `TerrainExtender.cs:109,123,128`,
`PhysicsRangeExtender.cs:86`.

`UpdateRanges()` sets `FloatingOrigin.fetch.threshold = (R * 1200)^2`. In
1.12.5 `threshold` is read only in `FloatingOrigin.Awake`, to compute
`thresholdSqr`. Writing it later changes nothing. (The value is also already
squared, which fits an older KSP where the field was compared directly.)

Two parts of PRE assume that the origin no longer moves inside `1.2 R`:

- **Terrain extender.** `InitialPosition` is the vessel's world position
  taken at load time (`GetWorldPos3D()`). The state machine then calls
  `SetPosition(InitialPosition + up * size)` on every physics step for more
  than 2 s. World positions are relative to the floating origin. The
  extender itself calls `ForceSetActiveVessel(farVessel)`, after which stock
  recentres the origin on that vessel (it is by definition more than 2.5 km
  away). The saved position then points somewhere else, by roughly the
  distance between the two vessels. Expected result: the landed vessel is
  moved far from where it stood. This matches the "landed vessels thrown
  around / destroyed on load" reputation of the mod, but needs a flight to
  confirm.
- **Near-clip hack** (B2): its formula uses the active vessel's distance
  from the origin, which stock keeps small.

Fix: store latitude / longitude / altitude (or a position relative to the
body) and convert at use time. Remove the threshold write.

### A4. Vessels found at scene start get `InitialPosition = (0,0,0)` (proven from source; trigger likely)

`TerrainExtender.cs:181-201`.

`InitialFetch()` adds landed vessels that are already loaded at the first
physics step of the scene, but does not set `InitialPosition`. The default is
the world origin, which is next to the active vessel. Those vessels would be
moved to the active vessel's position plus their own size upward, then
lowered onto it.

It only triggers if another landed vessel is already loaded at the first
`FixedUpdate` where `FlightGlobals.ready` is true. Whether that happens
depends on load order (other vessels load from `Vessel.Update`). Unlike
`NewVesselIsLoaded`, this path has no 2.5 km minimum distance, and accepts
vessels that are merely within 100 m of the terrain.

### A5. The terrain extender works by switching the active vessel (proven)

`TerrainExtender.cs:84-103`, `160-164`.

For each far landed vessel (or cluster within 2.5 km), PRE calls
`FlightGlobals.ForceSetActiveVessel` on it, waits 2 s, then switches back.
In stock, each switch:

- sets `FlightGlobals.ready = false` and puts **every** vessel on rails;
- fires `onVesselSwitching` and `onVesselChange`, which every mod listens to
  (kOS, RemoteTech, StageRecovery, Trajectories in the KSP-kOS install);
- takes control input away from the vessel being flown, mid-flight.

PRE also leaves IVA (`TerrainExtender.cs:89-93`) and never returns to it. If
the original vessel is destroyed meanwhile, `_tvel` is null,
`ForceSetActiveVessel(null)` is refused, and the player stays on the landed
vessel.

Only `VesselType.Debris` is excluded (`PhysicsRangeExtender.cs:80`). Flags,
EVA kerbals and deployed science parts are landed vessels too, so a flag 3 km
away is enough to trigger a focus switch.

### A6. State machine details (proven from source)

`TerrainExtender.cs:72-156`.

- `if (!SortaLanded(currentVessel)) continue;` skips a vessel that is more
  than 100 m above the terrain. The lift height is `vesselSize.magnitude`
  (the bounding-box diagonal). For a vessel larger than about 100 m, or one
  standing on a building, the entry is skipped forever, is never removed, and
  `_loading` stays true. If the vessel falls back under 100 m, the state
  machine resumes and lifts it again.
- The descent speed is `gravity direction * 20 * Time.fixedDeltaTime`, set as
  a velocity, not added: 0.4 m/s at 50 Hz. A 30 m vessel takes over a minute
  to come down. The `* fixedDeltaTime` looks like an acceleration formula
  used as a speed.
- `MakingVesselPartsIndestructible` raises `crashTolerance` to at least 1000
  and never restores it (it stays until the vessel is reloaded).
- `ShowMessageTerrainStatus` posts a new 3 s screen message every frame while
  the list is not empty.
- `VesselsLandedToLoad` is static: entries survive scene changes until the
  extender runs again and prunes destroyed vessels.
- `InitialAltitude` is stored and never used.

### A7. `Start()` throws when the main body has no terrain (proven)

`PhysicsRangeExtender.cs:28`, `TerrainExtender.cs:28-41`.

`UpdateSphere()` dereferences `FlightGlobals.currentMainBody.pqsController`
with no null check. The Sun and Jool have no PQS. A flight scene that starts
there throws in `PhysicsRangeExtender.Start()` before `UpdateRanges()` and
before the event subscriptions: PRE does nothing in that scene (no extended
range for a rendezvous in solar or Jool orbit). Commit `b54bf3d` fixed the
same null in `TerrainExtender.FixedUpdate` only.

`UpdateSphere()` is also called when `TerrainExtenderEnabled = False`: that
setting does not turn the terrain changes off.

### A8. Terrain settings: permanent, heavy, and partly without effect (proven)

`TerrainExtender.cs:28-41`.

- `StartUpSphere()` is a forced full restart of the body's terrain. PRE calls
  it at every flight scene start and on every "Apply". With Kopernicus and
  Parallax that was measured at about 2 s per call
  (`~/Games/KSP-kOS/bug-investigation.md`).
- It is also called at every atmospheric entry on the home world
  (`PhysicsRangeExtender.cs:44-59`): after going sub-orbital, the next
  SUB_ORBITAL to FLYING change restarts the terrain. **With the leak fixed
  this is one call instead of dozens, but the hitch at 70 km on the way down
  is still there by design.** The handler has no `ModEnabled` check. Whether
  the "missing PQ tiles" it works around still occur in 1.12.5, or were
  caused by PRE's own settings, is open.
- `visRadSeaLevelValue = 200` and `collapseSeaLevelValue = 200` multiply the
  radius in which terrain is subdivided near the ground. This is the actual
  "terrain extender" (colliders under far vessels), and probably the main
  performance and memory cost of the mod. The stock per-body values were not
  read (serialized).
- `detailAltitudeMax`, `detailSeaLevelQuads`, `detailAltitudeQuads` are never
  read by stock code. `maxQuadLenghtsPerFrame = 0.03` is the stock default.
  Four of the eight assignments do nothing.
- The body's PQS object lives for the whole session. The changes are never
  undone: not by "Disable Mod", not when leaving flight (the space centre
  uses the same Kerbin terrain).
- Only the body at scene start is changed. After an SOI change in the same
  scene the new body keeps stock terrain settings.

### A9. The inertial-frame protection for landed vessels has holes (proven from source)

`PhysicsRangeExtender.cs:177-250`, `278-299`.

Stock switches to the inertial frame above `inverseRotThresholdAltitude`
(100 km on stock bodies here). An unpacked landed vessel then sits on terrain
that moves under it. PRE's defence: above 90 % of that altitude, give landed
vessels shorter ranges.

- `UnloadLandedVessels()` runs once, at the crossing, and only for vessels
  **loaded at that moment**. `ApplyRangesToVessels()` then *skips* landed
  vessels instead of giving them the safe ranges, so unloaded landed vessels
  keep the full range. With `GlobalRange` above 100 km they load and unpack
  while the frame is inertial.
- The "safe" ranges are load 0.90, unload 0.95, pack 1.10, unpack 0.99 times
  the threshold altitude, whatever `GlobalRange` is. A landed vessel on high
  ground, less than 95 km below the active vessel, stays loaded and unpacked
  (unpack 99 km is above load 90 km) through the frame change at 100 km.
- Nothing covers the more common case: an orbit below 90 km. Everything
  landed within `R` loads and unpacks while the active vessel passes at
  orbital speed.
- `_unloadDueToReferenceFrameApplied` is static and carries over between
  scenes.

## 3. Medium impact

### B1. Enabling and disabling are not symmetric (proven)

- `Start()` returns early when the mod is disabled
  (`PhysicsRangeExtender.cs:25-26`, `TerrainExtender.cs:270-271`). "Enable
  Mod" later in the same scene applies ranges once, with no event
  subscriptions, so new or reloaded vessels get stock ranges until the next
  scene.
- "Disable Mod" calls `RestoreStockRanges()`, which uses `new VesselRanges()`
  (hard-coded numbers) rather than `PhysicsGlobals.Instance.VesselRangesDefault`
  (what stock really uses, from `Physics.cfg`). It does not undo the terrain
  settings, the cheats, the freeze list or `crashTolerance`.
- When disabled mid-scene, `CheckIfFreezeIsNeeded`, `NewVesselIsLoaded` and
  `SituationChangeFixes` still run (only `ApplyRangesToVessels` checks
  `ModEnabled`). `CheckIfFreezeIsNeeded` turns the cheats on, and the loop
  that would turn them off is gated by `ModEnabled`: they stay on.
- `UpdateRanges(true)` from the UI skips other non-landed vessels on purpose
  (changelog 1.5.0), but the next vessel event applies the ranges to them
  anyway.

### B2. Near-clip-plane hack (proven it overrides stock; usefulness open)

`PhysicsRangeExtender.cs:123-148`.

- Runs three times per frame. In the `else` branch it writes
  `nearClipPlane = 0.21` every time, so it overrides the values stock sets on
  camera mode change (`camera00DefaultNearClipPlane`,
  `camera00IVANearClipPlane`) and anything another mod sets.
- The formula is `0.21 * (distance of active vessel from origin / 4000)^2`,
  clamped to 0.21 to 10.5. Since the origin threshold write is dead (A3),
  that distance stays within stock's threshold. If the scene value is 4000 m
  or less, the result is always 0.21 and the hack does nothing except show
  "Flickering correction is active" every 60 s.
- The condition is "more than one loaded vessel and at least one within 100 m
  of the ground", which is true right after the first staging on the pad.
  That is the message seen in the freeze investigation.
- No null checks on `FlightGlobals.ActiveVessel` or `FlightCamera.fetch`.
- `FlightGlobals.VesselsLoaded.Count(TerrainExtender.SortaLanded)` allocates
  on each call.
- Written for the two-camera setup of KSP 1.4. Whether it helps with the
  single flight camera of 1.9+ is open.

### B3. Ranges are re-applied to every vessel on every event (proven)

`PhysicsRangeExtender.cs:278-299`.

Each `onVesselCreate`, `onVesselLoaded`, `onVesselSwitching` and
`onVesselGoOffRails` loops over all vessels and allocates a new
`VesselRanges` (8 objects) for each. A vessel switch packs everything, and
each unpack fires the event again: N vessels cost N² allocations.
`ShouldLandedVesselsBeLoaded()` is recomputed for each landed vessel inside
the loop.

Simpler: set the ranges of the one vessel the event is about. Or set
`PhysicsGlobals.Instance.VesselRangesDefault` once (every new `Vessel` copies
it in `Awake`) and update existing vessels once. Note that KSC building load
range also reads that default.

### B4. Settings file (proven, except the ModuleManager point)

`PRESettings.cs`.

- The file is a `.cfg` inside `GameData`, so the game loads it as a config
  (`Config(PreSettings) PhysicsRangeExtender/settings/PreSettings` in
  `KSP.log`). ModuleManager hashes all configs, so each "Apply", "Enable" or
  "Disable" should invalidate its cache and make the next start slow. (Known
  ModuleManager behaviour, not re-checked here.) Standard fix: move it to
  `GameData/PhysicsRangeExtender/PluginData/`.
- A CKAN reinstall or upgrade overwrites the user's settings (the file ships
  in the package).
- The path is relative to the working directory. Use
  `KSPUtil.ApplicationRootPath`.
- On a parse error the remaining values keep their defaults (`GlobalRange = 0`,
  `ModEnabled = false`) and `ConfigLoaded` is set to true anyway. If the
  `PreSettings` node is missing, `SaveConfig` returns silently and can never
  create it.
- No validation: `GlobalRange` 0 or negative is accepted from the UI and
  becomes every vessel's ranges.
- `SaveConfig` rethrows inside `OnGUI`.
- Culture: not an issue for `float.Parse` (stock sets the culture). `Gui.cs`
  still mixes invariant `ToString` with default `TryParse`.

### B5. GUI (proven)

`Gui.cs`.

- No input lock and no click-through protection. Typing digits in the range
  field also triggers action groups; clicks reach the scene behind the
  window.
- `ApplicationLauncher.Instance` is used in `Start()` without waiting for
  `onGUIApplicationLauncherReady`, and in `OnDestroy()` without a null check.
- The button is registered for `AppScenes.ALWAYS` though the addon only lives
  in flight.
- `GuiEnabled` is static: if the window was open when leaving flight, it is
  open in the next flight while the new button shows "off".
- Three `new GUIStyle` per `OnGUI` call; fixed window id 320; label rectangles
  60 px wide for longer texts.

### B6. `PRExtensions` is missing from the csproj and the release (proven)

`PRExtensions.cs` is not in the `<Compile>` list. The official 1.21.0 DLL has
no `PRExtensions` type (checked in its IL). The `csc` build used here does
include it, so the branch build has a public API the release lacks.

`PreOn` only works after a `PreOff` (`_wasEnabled`), and calls
`Gui.Fetch.Apply()`, which fails outside the flight scene.

### B7. The committed DLL is stale (proven)

`Distribution/GameData/PhysicsRangeExtender/Plugins/PhysicsRangeExtender.dll`
is byte-identical to the official 1.21.0 (md5 `f6d7ec1f…`). It has neither
the `Gui.OnDestroy` fix (`22d6553`) nor the leak fix (`e9aaa75`). Anyone
packaging `Distribution/` ships the old bugs. The `.version` file and
`AssemblyVersion` still say 1.21.0.

### B8. Build setup (proven)

The csproj uses absolute Windows paths for every reference
(`E:\REPOSITORIO\_LocalDev\KSPRefs\…`) and a `cmd` post-build script
(`set /p`, `xcopy`, `7za.exe`, `pdb2mdb`) that also copies a
`CLSInterfaces.dll` this project does not have. It cannot build on another
machine. `LocalDev/*.txt` are the original author's local paths.

Suggested: an SDK-style csproj with a `KSPRoot` property (or KSPBuildTools),
or simply a `build.sh` with the `csc` line from `README.md`, and stop
tracking the DLL.

### B9. Null handling in event handlers (proven the null exists; whether PRE is subscribed in time is likely)

- `onVesselSwitching` is fired with `from == null` on the first activation in
  a scene. `CheckIfFreezeIsNeeded` reads `from.Landed`
  (`PhysicsRangeExtender.cs:100`). Stock catches and logs the exception, but
  `ApplyRangesToVessels()` is skipped for that event.
- `NewVesselIsLoaded` reads `FlightGlobals.ActiveVessel.CoM` unchecked
  (`PhysicsRangeExtender.cs:80`).
- `RefreshPqsWhenApproaching` reads `curVessel.mainBody.pqsController`
  unchecked.

### B10. The range numbers themselves (design)

Unpack at `0.99 R` means every vessel within range runs full physics. Stock
keeps most vessels packed beyond 200 to 350 m even when loaded. At 100 km
from the origin single precision resolves about 8 mm, which is where the
shaking and phantom forces in the README come from. Separate settings for
"load" and "unpack", and per situation, would let users see far vessels
without simulating them.

## 4. Low impact / cleanup

- `Update`, `LateUpdate` and `FixedUpdate` all run the same three methods.
  Once per frame (or per physics step for the velocity work) is enough.
  `Update` lacks the `ConfigLoaded` check the other two have.
- `throw new ArgumentOutOfRangeException()` in `default:` branches that run
  inside `Update` / `FixedUpdate`.
- Unused: `LastFlickeringTime` as a public property, `InitialAltitude`,
  `_incrButtonWidth` layout leftovers, `using UniLinq`, a doubled `;`.
- No `[KSPAssembly]` attribute, so other mods cannot declare a dependency.
- Log prefixes differ (`[PhysicsRangeExtender]:` vs `[Physic Range Extender]`).
- `ChangeLog.txt` has no 1.19.0 header (the 1.9.1 entries sit under 1.20.0).

## 5. Suggested order of work

1. **Small, certain fixes.** A1 (cheats), A2 (release frozen vessels), A7
   (null check, respect `TerrainExtenderEnabled`), B1 (gate every handler on
   `ModEnabled`, subscribe regardless of the flag), B9 (null checks), B6 (add
   the file to the csproj), B4 (move settings to `PluginData`, validate the
   range). None of these changes what the mod is meant to do.
2. **Remove dead and harmful writes.** The `FloatingOrigin.threshold` line
   (A3), the four no-op PQS fields (A8), the unconditional
   `nearClipPlane = 0.21` (B2).
3. **Decide what to do with the terrain extender.** As written it cannot work
   in 1.12.5 (A3, A4) and its method is intrusive (A5). Options:
   - ship with `TerrainExtenderEnabled = False` and make that flag really
     disable everything terrain-related (cheapest, fits a kOS career where
     the point is seeing and switching to far vessels);
   - keep far landed vessels **packed**: large load range, stock-like pack
     and unpack range for landed / splashed / prelaunch. Packed vessels are
     placed on the surface by the game and need no colliders. This also
     removes the need for A2 and most of A9. It does not help a booster that
     is still flying and needs ground under it far from the active vessel;
   - rewrite it with body-relative positions and without focus switching.
     Stock `GoOffRails` already repositions landed vessels against the
     terrain (`CheckGroundCollision`); read that first, it may do most of
     the job once colliders exist.
4. **Terrain restarts** (A8): restart only when a value actually changes,
   restore the original values on disable and on scene exit, handle SOI
   changes, and test whether the re-entry restart is still needed.
5. **Build and packaging** (B7, B8), version bump, `.version` file.

## 6. In-game checks for the "likely" items

Use a sandbox save in `~/Games/KSP-kOS` with `ModEnabled = True`. Look at
`KSP.log` after each.

| # | Flight | Confirms | What to look for |
|---|--------|----------|------------------|
| 1 | Launch, pass 70 km, revert, repeat 3 times, stage | the leak fix | no burst of `Calling StartUpSphere()`; no `KSPCF … PhysicsRangeExtender … onVesselSituationChange` lines |
| 2 | Alt+F12, turn on No Crash Damage, close and reopen the menu | A1 | the box is off again |
| 3 | Land a rover 5 km from the pad, launch a rocket to above 70 km, switch to the rover with `]`, try to drive | A2 | rover does not move |
| 4 | Same rover, start a flight 10 km away and approach on the ground or in a plane | A3, A5 | focus jumps to the rover; where the rover ends up |
| 5 | Start a flight on a vessel with another landed vessel 50 m away | A4 | whether the neighbour is moved onto the active vessel |
| 6 | Load a vessel in Jool or solar orbit from the tracking station | A7 | `NullReferenceException` from `TerrainExtender.UpdateSphere` in the log; a second vessel at 10 km stays unloaded |
| 7 | Any flight scene load, and a re-entry | A8 | time between `Update Quads Init: Kerbin` lines around `Calling StartUpSphere()` |
| 8 | Add `Debug.Log` of `FloatingOrigin.fetch.thresholdSqr`, `FlightCamera.fetch.camera00DefaultNearClipPlane` and the body's `visRadSeaLevelValue` before PRE changes them | A3, A8, B2 | the stock numbers missing from these notes |
