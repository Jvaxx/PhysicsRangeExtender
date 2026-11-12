# Fix plan

Written 2026-11-12 for commit `2b83a71`, KSP 1.12.5.3190. Read
[analysis.md](analysis.md) (what is wrong) and
[ksp-internals.md](ksp-internals.md) (what stock really does) first. Finding
numbers (A1, B4…) refer to `analysis.md`.

This plan is meant to be executed in a fresh session, one step at a time.

## 1. Decisions already taken (2026-11-12)

| # | Decision | Source |
|---|----------|--------|
| D1 | Replace the terrain extender state machine, the freeze list and the "unload landed vessels" logic with stock mechanisms. Do not fix the old code. | user |
| D2 | Add a separate, smaller physics (unpack) range for landed / splashed / prelaunch vessels. They still load at the full range. | user, on the condition below |
| D3 | Use cases to serve: kOS booster landing; seeing and switching to far vessels; rendezvous of two vessels with kOS running on both. | user |
| D4 | Remove every terrain (PQS) write and every `StartUpSphere()` call. They do not create ground under far vessels (see `ksp-internals.md`, "PQS"), and they cost about 2 s per call. | my proposal, approved by the user |
| D5 | Remove the near-clip "flickering correction". Proven to do nothing in external view and to override stock's IVA value. | my proposal, follows from the analysis |
| D6 | Add a separate physics range for sub-orbital, orbiting and escaping vessels (`SpacePhysicsRange`, default 20 km), both physics ranges editable in the GUI. Flying keeps the full range: stock `Vessel.CheckKill` destroys a packed vessel in more than 1 kPa of atmosphere beyond its pack range. | user, 2026-11-12, after far unpacked vessels shook in flight |

D2's condition was "only if kOS CPUs stay fully loaded". Checked in
`~/Projects/KOS-Improved/src/kOS/Module/kOSProcessor.cs:931-946`: the CPU
runs whenever `!vessel.HoldPhysics`, and stock only sets that lock on the
active vessel. A loaded, packed, non-active vessel keeps its CPU booted and
executing. What a packed vessel cannot do is move: throttle and steering have
no effect on rails. So a script that wants to **launch** a landed vessel that
is farther than the landed physics range from the active vessel will not lift
off. The range is a setting; setting it equal to `GlobalRange` restores the
old behaviour.

D4 has one consequence to know: with the active vessel far away, there is no
ground under a non-active vessel that comes down to land (stock builds
collider terrain only within a few km of the active vessel). The old PRE did
not solve this either; it only kept terrain alive that the active vessel had
already flown over. Solving it properly is step 9 (optional feature).

## 2. Ground rules for the implementing session

- Branch: create `rework-1.12.5` from `fix-situation-change-leak`. One commit
  per step below. Commit only if the user says so at the start; message
  format is in `~/.claude/CLAUDE.md`. Never push without being asked.
- Build after every step with `./build.sh` (created in step 0). Zero
  warnings is the bar.
- KSP cannot be driven from the terminal. Each step ends with a short flight
  checklist **for the user**. Do not claim a step works in game; say it
  builds and what remains to be flown.
- Install into the game (`./build.sh install`) only when the user is about to
  test. KSP must be closed. `~/Games/KSP-kOS` is the test install; its
  `settings.cfg` currently has `ModEnabled = False`.
- Before relying on a stock method, read it:
  `docs/tools/ilspycmd -r "$M" -t Vessel "$M/Assembly-CSharp.dll" > /tmp/Vessel.cs`
  then `python3 docs/tools/csclean.py /tmp/Vessel.cs GoOffRails`. Keep
  decompiled code out of the repo.
- Do not touch `Distribution/…/Plugins/PhysicsRangeExtender.dll` before
  step 8.
- `build/` is ignored (`.git/info/exclude`).

## 3. Target shape

```
PhysicsRangeExtender/
  PRESettings.cs          settings load/save/validate (PluginData)
  RangeProfile.cs         builds the VesselRanges for a vessel   (new)
  PhysicsRangeExtender.cs flight addon: lifecycle, events, apply/restore
  LandedVesselGuard.cs    keeps landed vessels safe              (new)
  Gui.cs                  window + toolbar button
  PRExtensions.cs         PreOn / PreOff for other mods
  TerrainExtender.cs      deleted in step 4
build.sh                  (new)
```

Per frame, the reworked mod does nothing. Work happens on vessel events and
in one guard tick at 2 Hz.

## 4. Steps

### Step 0. Preparation (no behaviour change)

**Done 2026-11-12** (branch created, `build.sh`, csproj, docs committed and
pushed). Steps 1 to 4 are done too (one local commit each); next is step 5.

1. Create the branch.
2. Add `build.sh` at the repo root:
   - `./build.sh` compiles every `PhysicsRangeExtender/*.cs` plus
     `Properties/AssemblyInfo.cs` to `build/PhysicsRangeExtender.dll` with
     the `csc` line from `docs/README.md` (add `-warn:4`).
   - `KSP_DIR` environment variable, default `~/Games/KSP-kOS`.
   - `./build.sh install` copies the DLL to
     `$KSP_DIR/GameData/PhysicsRangeExtender/Plugins/` and prints the md5.
3. Add the two missing files to the csproj `<Compile>` list as they appear
   (`PRExtensions.cs` now; new files in later steps). Do not modernise the
   csproj: there is no .NET SDK here to test it.
4. Commit `docs/` (without `docs/tools/.ilspycmd` and `.unitypy-venv`, which
   `docs/tools/.gitignore` already excludes).

Check: `./build.sh` output has the same types and methods as the current
`build/PhysicsRangeExtender.dll`.

### Step 1. Lifecycle and robustness

Fixes B1, B9, part of A7, and the triple update loop. Small and certain.

`PhysicsRangeExtender.cs`:

1. `Start()`: always subscribe to events, whatever `ModEnabled` is. Move the
   enabled-only work into `ApplyAll()` and call it only when enabled.
   Wrap the terrain call so a body without PQS cannot abort `Start()`
   (the call itself goes away in step 4).
2. Every handler starts with `if (!PreSettings.ModEnabled) return;`
   (today only `ApplyRangesToVessels` does).
3. Null checks: `from` in the `onVesselSwitching` handler (stock passes null
   on the first activation), `FlightGlobals.ActiveVessel`,
   `data.host.mainBody?.pqsController`.
4. Keep one update method. Delete the duplicated bodies in `LateUpdate` and
   `FixedUpdate`; what remains runs in `FixedUpdate` until steps 2 and 4
   delete it.
5. Add `public static PhysicsRangeExtender Instance` (set in `Awake`, cleared
   in `OnDestroy`) and `public void SetEnabled(bool)`, which is what the GUI
   and `PRExtensions` call. `SetEnabled(true)` = save setting + `ApplyAll()`;
   `SetEnabled(false)` = save setting + `RestoreAll()`.
6. `RestoreAll()`: use
   `new VesselRanges(PhysicsGlobals.Instance.VesselRangesDefault)`, not
   `new VesselRanges()`.
7. Add `PreSettings.DebugLog` (default false) and a `Log.Debug(string)`
   helper with the single prefix `[PhysicsRangeExtender]`. Later steps log
   every guard decision through it. This is how in-game behaviour gets
   verified from `KSP.log`.

`PRExtensions.cs`: `PreOn` / `PreOff` call `Instance?.SetEnabled(...)`; when
there is no instance (not in flight) they only flip and save the setting.
Keep the `_wasEnabled` rule as is.

User check: toggle "Disable Mod" / "Enable Mod" mid-flight with a second
vessel at 5 km: it unloads on disable and reloads on enable, no exception in
`KSP.log`.

### Step 2. Remove what is proven dead (D5, part of A3)

1. Delete `UpdateNearClipPlane`, `_initialClippingPlane`,
   `LastFlickeringTime`, the "Flickering correction" message, and the
   `nearClipPlane` line in `RestoreStockRanges`.
2. Delete the `FloatingOrigin.fetch.threshold = …` line.
3. Remove `CamFixMultiplier` from the GUI. Keep reading it from an old
   settings file without failing (step 6 drops it from the saved file).

Evidence: `FloatingOrigin` is created by `AddComponent` in
`PSystemManager.SetupLocalSpace`, so `threshold = 500` and
`thresholdSqr = 250000`; the active vessel stays within about 500 m of the
origin, and PRE's formula `0.21 * (d / 4000)²` is always clamped to 0.21,
which is stock's own default (`FlightCamera.camera00DefaultNearClipPlane`).
In IVA stock wants 0.1 and PRE was forcing 0.21.

User check: in IVA and external view, with a second vessel on the ground
nearby, nothing looks different from before (no new flicker).

### Step 3. Range model (D2, B3, B10)

New `RangeProfile.cs`:

```csharp
static VesselRanges Build(float globalKm, float landedKm)
// R = globalKm * 1000, L = min(landedKm, globalKm) * 1000
// flying, subOrbital, orbit, escaping: Situation(R, 1.05 R, 1.10 R, 0.99 R)   (unchanged)
// landed, splashed, prelaunch:         Situation(R, 1.05 R, max(1.10 L, L + 50), L)
```

`Situation` argument order is (load, unload, pack, unpack).

`PhysicsRangeExtender.cs`:

1. Replace `_baseRanges`, `_globalSituation` (statics) with one instance
   field built by `RangeProfile.Build`.
2. `ApplyTo(Vessel v)`: `v.vesselRanges = new VesselRanges(profile)`, then
   let the guard adjust it (step 4).
3. Events: `onVesselCreate(v)`, `onVesselLoaded(v)`, `onVesselGoOffRails(v)`
   call `ApplyTo(v)` for that vessel only. `onVesselSwitching` no longer
   applies ranges. `ApplyAll()` (Start, enable, GUI apply) loops once over
   `FlightGlobals.Vessels`.
4. Delete `VesselOrbitingWhileUpdatingRangeFromUi` and the `updatingFromUi`
   parameter: the GUI applies to all vessels. (The old rule was undone by the
   next vessel event anyway.)
5. Do not write `PhysicsGlobals.Instance.VesselRangesDefault`: it also sets
   the KSC building load range and applies outside flight.

`PRESettings.cs`: add `LandedPhysicsRange` (km, float, default 5). Step 6
does the file work; here a missing value just falls back to the default.

Still in place after this step: the old freeze / unload / terrain code. It
keeps working on the new ranges until step 4 removes it.

User checks:
- Two vessels in orbit 20 km apart: both loaded and unpacked, kOS terminal of
  the far one responds (`PRINT SHIP:UNPACKED.` gives True).
- A landed vessel 10 km from the active one: `SHIP:LOADED` True,
  `SHIP:UNPACKED` False, its kOS program keeps printing.

### Step 4. Landed vessels: stock mechanisms instead of PRE's (D1, D4)

This is the core step. It removes A1, A2, A4, A5, A6, A8, A9 by deleting
their code.

**Read first** (cleaned decompile): all of `Vessel.GoOffRails`,
`Vessel.CheckGroundCollision`, `Vessel.GoOnRails`, `Vessel.checkLanded`,
`OrbitPhysicsManager.LateUpdate`, `OrbitPhysicsManager.setRotatingFrame`.
The summary in `ksp-internals.md` ("Unpacking a landed vessel") was read from
obfuscated control flow; confirm the three facts marked there before coding.
Also check KSPCommunityFixes for patches on these methods
(`docs/tools/ilspycmd -l c ~/Games/KSP-kOS/GameData/KSPCommunityFixes/Plugins/KSPCommunityFixes.dll`
and look for `GoOffRails`, `CheckGroundCollision`).

**Delete:**

- `TerrainExtender.cs` entirely (state machine, `UpdateSphere`,
  `Activate/DeactivateNoCrashDamage`, `MakingVesselPartsIndestructible`,
  messages, static list). Remove it from the csproj.
- In `PhysicsRangeExtender.cs`: `VesselToFreeze`, `CheckIfFreezeIsNeeded`,
  `FreezeLandedVesselWhenSwitching`, `NewVesselIsLoaded`,
  `AvoidReferenceFrameChangeIssues`, `ShouldLandedVesselsBeLoaded`,
  `UnloadLandedVessels`, `_unloadDueToReferenceFrameApplied`,
  `SituationChangeFixes`, `RefreshPqsWhenApproaching`, `_isSuborbital`, the
  `onVesselSituationChange` subscription, and the remaining update method.
- `TerrainExtenderEnabled` stops having any effect (step 6 drops it from the
  file).

After this there is no write to `CheatOptions`, no `ForceSetActiveVessel`,
no `SetPosition`, no `SetWorldVelocity`, no PQS access anywhere in the mod.
Grep for those names to confirm.

**Add `LandedVesselGuard.cs`** (plain class owned by the addon, not a second
`KSPAddon`). Three rules:

1. **Inertial frame.** Subscribe to `GameEvents.onRotatingFrameTransition`
   (`HostTargetAction<CelestialBody, bool>`, `target == false` means
   inertial). On a change to inertial: for every loaded, unpacked,
   non-active vessel with `LandedOrSplashed`, call `GoOnRails()`. Stock
   `GoOffRails` then refuses to unpack them until the frame rotates again
   (`LandedOrSplashed && !FlightGlobals.RefFrameIsRotating` returns early).
   No range changes involved.

2. **No ground, no physics.** Tick every 0.5 s. For each loaded, packed,
   non-active vessel with `Landed && !Splashed` that is inside its landed
   unpack range: raycast straight down from above the vessel for a ground
   collider.
   - Mask: start with layer 15 only (`1 << 15`, terrain and KSC buildings).
   - No hit: hold the vessel. Holding = set
     `vesselRanges.landed.unpack = 0` and `prelaunch.unpack = 0` on **that
     vessel's own** `VesselRanges` copy. Stock then never calls
     `GoOffRails` for it.
   - Hit: release. Restore the profile's unpack value, and ask stock to seat
     the vessel on the ground at its next unpack: `skipGroundPositioning =
     false` and `PQSminLevel = 0` make `GoOffRails` run
     `CheckGroundCollision()`, which repositions the vessel on the collider
     (verify the exact flags when reading `GoOffRails`).
   - Keep held vessels in a `HashSet<Vessel>`; drop destroyed and unloaded
     ones each tick; release all in `RestoreAll()` and `OnDestroy`.
   - `ApplyTo(v)` from step 3 must re-apply the hold if `v` is in the set.
   - Log hold and release through `Log.Debug`.

3. **Let a lander settle before packing it.** When a non-active unpacked
   vessel becomes LANDED farther than `L` from the active vessel, stock
   would pack it at first ground contact. Keep it unpacked until it is at
   rest: on `onVesselSituationChange` to LANDED / SPLASHED for an unpacked
   non-active vessel, give that vessel `landed.pack = splashed.pack =
   1.10 R` and remember it; in the tick, once `srfSpeed < 0.3` for 5 s (or
   after 60 s at most), restore the profile values. Skip this rule while the
   frame is inertial (rule 1 wins).

Not covered, by design: the active vessel (stock handles it), splashed
vessels in rule 2 (they float on buoyancy, not on colliders), vessels beyond
`L` (they stay packed anyway).

User checks (set `DebugLog = True`):

| Flight | Expect |
|--------|--------|
| Rover parked 3 km from the pad, launch a rocket straight up | rover unpacked at start; `KSP.log` shows it packed once at "Reference Frame: Inertial"; switch back after landing: it drives |
| Start a flight on a vessel 10 km from a landed base, drive or fly towards it | base stays packed (hold logged) until ground exists, then one release line and stock's `ground contact` line; base is not thrown, sunk or lifted |
| Booster separates, active vessel follows the booster down, upper stage coasts within range | upper stage stays loaded and unpacked, its kOS CPU keeps running |
| Alt+F12: turn on No Crash Damage | stays on (A1 gone) |
| Flight scene in Jool or solar orbit, second vessel at 10 km | loaded; no exception at scene start (A7 gone) |
| Any scene load and any re-entry | no `Calling StartUpSphere()` in the log, no 2 s hitch (A8 gone) |
| Flag planted 3 km away | no focus switch (A5 gone) |

**Done 2026-11-12, not flown yet.** Stock code read and recorded in
`ksp-internals.md`. Where the code differs from the text above:

- Rule 2 holds by default. Stock checks the unpack range every frame and
  the guard only twice a second, so a vessel entering its range would be
  unpacked before the guard looked. Every packed landed vessel is held from
  the moment it loads or packs (vessel switch, warp, rule 1) until a ray
  within its pack range finds ground. Out of range or in the inertial
  frame it stays held, which also spares stock a `GoOffRails` call per
  frame. The active vessel is released at the next tick.
- Stock is asked to seat a vessel (`PQSminLevel = 0`,
  `skipGroundPositioning = false`) only if the guard found no ground under
  it at least once, not on every release. Both flags are needed: see
  `ksp-internals.md`.
- `VesselsWaitingForGround` (held vessels inside their unpack range) is
  what step 7's GUI line should show; the raw held count includes every far
  landed vessel.
- `Log.cs` holds the log helper from step 1.

### Step 5. Review pass before touching settings and GUI

Stop and run `/code-review` on the branch diff (steps 1 to 4), fix findings,
and have the user fly the step 4 table. Steps 6 to 8 are mechanical; they
should start from a core that is known to work.

### Step 6. Settings file (B4)

`PRESettings.cs`:

1. Path: `KSPUtil.ApplicationRootPath` +
   `GameData/PhysicsRangeExtender/PluginData/settings.cfg`. `PluginData` is
   not loaded as game config, so ModuleManager's cache is no longer
   invalidated by a save. Create the directory if missing.
2. Load order: new file; else the old `GameData/PhysicsRangeExtender/settings.cfg`
   (migration: read it, write the new file, leave the old one alone); else
   defaults. Each value is read with `TryParse` and its own default, so one
   bad value does not zero the rest.
3. Values: `GlobalRange` (int km, clamp 3 to 2000, default 100),
   `LandedPhysicsRange` (float km, clamp 0.2 to `GlobalRange`, default 5),
   `SpacePhysicsRange` (float km, clamp 0.2 to `GlobalRange`, default 20),
   `ModEnabled` (default true), `DebugLog` (default false). Stop writing
   `CamFixMultiplier` and `TerrainExtenderEnabled`.
4. `SaveConfig` builds the node from scratch (works when the file or node is
   missing), logs on failure, does not rethrow.
5. Use `CultureInfo.InvariantCulture` for every parse and format, in
   `Gui.cs` too.
6. Remove `settings.cfg` from `Distribution/` and from the csproj
   `<None>` list. Defaults live in code.

User check: delete the new file, start KSP: values come from the old file;
change the range in the GUI, restart: ModuleManager does not rebuild its
cache (no "cache invalid" in the ModuleManager log) and the value is kept.

### Step 7. GUI (B5)

`Gui.cs`:

1. Toolbar button: create it from `GameEvents.onGUIApplicationLauncherReady`
   (and directly if `ApplicationLauncher.Ready`), for
   `AppScenes.FLIGHT | AppScenes.MAPVIEW`; null-check
   `ApplicationLauncher.Instance` in `OnDestroy`.
2. `GuiEnabled` becomes an instance field (the window starts closed in each
   flight, matching the button).
3. Input: while the mouse is over the window, set an
   `InputLockManager` control lock (`ControlTypes.ALLBUTCAMERAS`, id
   `PRE_GUI`) and remove it otherwise and in `OnDestroy`. This stops digits
   typed in the fields from firing action groups, and clicks from reaching
   the scene.
4. Fields: "Range (km)", "Landed physics range (km)", buttons "Apply" and
   "Enable / Disable". Invalid input: keep the old value and show the valid
   interval in the window. Add one status line: number of vessels held by
   the guard.
5. Create the `GUIStyle` objects once; window id from `GetInstanceID()`;
   size the label rectangles to their text.

User check: type `12` in the range field during flight: action groups 1 and
2 do not fire; window follows the toolbar button state across scene changes.

**Partly done early (D6):** items 3, 4 (three fields: load range, physics
range in space, physics range landed; all-or-nothing validation), and 5
(styles cached, `GUILayout` window with `GetInstanceID()`). Still to do:
items 1 and 2.

### Step 8. Version and packaging (B6, B7, B8)

1. `AssemblyInfo.cs`: version 1.22.0; add
   `[assembly: KSPAssembly("PhysicsRangeExtender", 1, 22)]`.
2. `ChangeLog.txt`: 1.22.0 entry listing the behaviour changes (landed
   physics range, landed vessels stay packed without ground or in the
   inertial frame, terrain extender and flickering correction removed,
   settings moved to `PluginData`, leak fixes).
3. `PhysicsRangeExtender.version`: 1.22.0, KSP 1.12.3 to 1.12.5. **Ask the
   user** which URLs to put (upstream's would announce an update that does
   not exist there).
4. `README.md` (root and `Distribution/`): what the mod does now, the two
   ranges, the kOS note from D2, the limitation from D4.
5. Rebuild and replace `Distribution/…/Plugins/PhysicsRangeExtender.dll`.
6. Remove `LocalDev/*.txt` and the post-build script from the csproj only if
   the user wants to diverge that far from upstream; otherwise leave them.
7. Update `docs/analysis.md`: mark each finding fixed, removed or still
   open, with the commit.

### Step 9. Optional feature: ground under a far descending vessel

Not a bug fix. Only after steps 0 to 8 are flown and accepted.

Need (D3, "kOS booster landing" with the player on the upper stage): collider
terrain where a non-active vessel is about to land.

What is known (`ksp-internals.md`, "PQS"): terrain subdivides around one
point only, `PQS.target`, which `PQSMod_CelestialBodyTransform.OnPreUpdate`
resets to the active vessel on every update. Colliders exist only on the
finest quads, a few km around that point.

Spike to run before any design:

1. Add a `PQSMod` of our own to the body's sphere with a higher `order` than
   `PQSMod_CelestialBodyTransform`, whose `OnPreUpdate` calls
   `sphere.SetTarget(otherVessel.transform)`. Check that terrain then builds
   under `otherVessel` and what the active vessel sees (it loses its own
   detail while retargeted).
2. Rule to try: retarget only while the active vessel is above ~20 km radar
   altitude and a loaded, unpacked, non-active vessel is below ~5 km and
   descending. Return the target as soon as either condition ends.
3. Measure the cost of each retarget with Parallax and Kopernicus installed
   (the earlier investigation saw about 2 s for a full sphere restart; a
   retarget is not a restart, but scatter mods may react).
4. Decide with the user whether it is worth shipping, behind a setting that
   is off by default.

## 5. Risks and how the plan handles them

| Risk | Handling |
|------|----------|
| The stock ground check (`PQSminLevel = 0`, `skipGroundPositioning = false`) does not behave as read from obfuscated code | Step 4 starts by reading the full methods. Fallback: skip the "ask stock to seat it" part; the hold alone already prevents falling through. |
| Raycast on layer 15 misses something a vessel can stand on (another vessel, a mod's static) and the vessel is held forever | Held vessels are logged and counted in the GUI. The active vessel is never held, so switching to it always frees it. If it shows up in testing, widen the mask to stock's (`0x8000 | LayerUtil.DefaultEquivalent`) and ignore the vessel's own parts. |
| Packing landed vessels on the frame change disturbs mods that react to `onVesselGoOnRails` | It is the same event stock fires on every vessel switch. Logged. |
| A booster is packed in mid-bounce | Rule 3 of the guard. |
| A kOS script expects to launch a far landed vessel | Documented (D2). `LandedPhysicsRange = GlobalRange` restores it, with the guard's ground rule still applying. |
| Removing the terrain writes brings back "missing PQ tiles" on re-entry | Unknown whether that bug exists without PRE's own terrain settings. Step 4's last-but-one check covers it; if tiles are missing, reintroduce only the re-entry restart, once, behind `ModEnabled`. |
| No automated tests, everything depends on user flights | `DebugLog` lines for every decision, a check table per step, and a review pass (step 5) before the mechanical steps. |

## 6. What each finding becomes

| Finding | Step | How |
|---------|------|-----|
| A1 cheats overwritten | 4 | code deleted |
| A2 frozen vessels | 4 | code deleted; guard rule 1 replaces its purpose |
| A3 dead origin write, stale positions | 2, 4 | line deleted; state machine deleted |
| A4 zero `InitialPosition` | 4 | code deleted |
| A5 focus switching | 4 | code deleted |
| A6 state machine details | 4 | code deleted |
| A7 Sun / Jool exception | 1, 4 | guarded, then the call is deleted |
| A8 terrain restarts | 4 | all PQS access deleted (D4) |
| A9 inertial-frame holes | 4 | guard rule 1 |
| B1 enable / disable | 1 | `SetEnabled`, handlers gated, proper restore |
| B2 near clip | 2 | deleted |
| B3 per-event cost | 3 | apply to one vessel |
| B4 settings file | 6 | `PluginData`, validation, migration |
| B5 GUI | 7 | |
| B6 `PRExtensions` not compiled | 0, 1 | csproj, `SetEnabled` |
| B7 stale DLL | 8 | rebuilt |
| B8 build setup | 0, 8 | `build.sh`; csproj kept minimal |
| B9 nulls in handlers | 1 | |
| B10 one range for all | 3 | landed physics range |
| Section 4 cleanup | along the way | most of it disappears with the deleted code |
