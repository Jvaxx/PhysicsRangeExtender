# Stock KSP 1.12.5 behaviour that PRE depends on

Read from the IL of `KSP_x64_Data/Managed/Assembly-CSharp.dll`
(1.12.5.3190, md5 `96b01ccc61875ef725fc315c2333d7cd`). See `README.md` for how
to reproduce. Everything here was read directly unless marked otherwise.

Sections marked *(C#)* were added on the second pass with `ilspycmd` and
`tools/csclean.py`. The decompiled control flow is obfuscated (nested
`while/switch` blocks), so nesting was inferred in a few places; those are
marked **confirm**.

## Vessel ranges

- `VesselRanges.Situation(load, unload, pack, unpack)`, in that order.
- `new VesselRanges()` is hard-coded:

  | situation | load | unload | pack | unpack |
  |-----------|-----:|-------:|-----:|-------:|
  | prelaunch, landed, splashed, orbit, escaping | 2250 | 2500 | 350 | 200 |
  | flying | 2250 | 22500 | 25000 | 2000 |
  | subOrbital | 2250 | 15000 | 10000 | 200 |

- `Vessel.Awake` sets `vesselRanges = new VesselRanges(PhysicsGlobals.Instance.VesselRangesDefault)`
  (falls back to `new VesselRanges()` only if `PhysicsGlobals` is missing).
  `VesselRangesDefault` is loaded from `Physics.cfg`. So the true stock value
  is the `PhysicsGlobals` one, not the hard-coded constructor.
- `vesselRanges` is not reassigned anywhere else in stock (only read).
- `PQS_KSPBinder.GetPQSCityLoadRange` / `OnGetPOIRange` (KSC buildings,
  anomalies) read `PhysicsGlobals.VesselRangesDefault.flying` and
  `.subOrbital`, not per-vessel ranges.
- `Vessel.Update`, for every vessel that is not the active one:
  - loaded and distance > `unload` of its `situation`: `Unload()`
  - not loaded and distance < `load`: `Load()` (fires `onVesselLoaded`)
- `OrbitPhysicsManager.LateUpdate`, for every other vessel:
  - distance > `pack` of its `BestSituation`: `GoOnRails()`
  - distance < `unpack` of its `situation`, packed, no high warp:
    `GoOffRails()`
- The active vessel is not range-checked: `OrbitPhysicsManager.LateUpdate`
  calls `activeVessel.GoOffRails()` every frame while it is packed and
  `holdVesselUnpack` is off. `FlightGlobals.setActiveVessel` packs every
  vessel (if the target is packed), fires `onVesselSwitching(from, to)`,
  then calls `HoldVesselUnpack()` (one frame). The physics-hold counter
  `framesAtStartup` is set in `Vessel.Awake` only, so a vessel loaded long
  ago unpacks about two frames after a switch. With its saved PQS levels
  equal to the body's, its ground check is skipped (see below), so a far
  landed vessel switched to unpacks before any collider exists under it.
  *(C#, read 2026-11-12)*
- Distances are `Vector3.Distance` between the two `transform.position`
  (single precision).
- `Vessel.GoOffRails` returns unless `loaded`. Details in "Unpacking a
  landed vessel" below.
- `Vessel.CheckKill` *(C#)*: a packed, non-active, non-landed vessel in more
  than 1 kPa of atmosphere is destroyed when it is farther than the `pack`
  range of its `BestSituation` from the active vessel. A non-landed vessel
  whose altitude is below `terrainAltitude` "crashed through terrain" and
  explodes. So a vessel coming down where there is no collider is lost.
  `CheckKill` runs from `VesselPrecalculate.MainPhysics` for packed vessels
  too, so a packed vessel descending on an airless body also explodes at
  the surface.
- `Vessel.updateSituation` runs from `Vessel.LateUpdate` every frame for
  every vessel, packed or not: a sub-orbital vessel becomes FLYING as soon
  as its static pressure is above 0, before `CheckKill`'s 1 kPa.

## Unpacking a landed vessel *(C#)*

`Vessel.GoOffRails()`, in order:

1. returns if not `packed`, or not `loaded`;
2. returns if the physics hold has not expired (25 frames, 75 if landed),
   except for `ModuleGroundPart` vessels;
3. if `Landed && !Splashed`: when `PQSminLevel` and `PQSmaxLevel` are both
   non-zero, the situation is not PRELAUNCH, and both equal the body's
   current `pqsController.minLevel` / `maxLevel`, it sets
   `skipGroundPositioning = true`; when they differ it sets it to false.
   **When `PQSminLevel` or `PQSmaxLevel` is 0, or the vessel is PRELAUNCH,
   it leaves `skipGroundPositioning` as it is.** A single-part vessel gets
   `skipGroundPositioning = false` unless `skipGroundPositioningForDroppedPart`.
   Then, unless (`!vesselSpawning && skipGroundPositioning`), it calls
   `CheckGroundCollision()` and, if that returns false, sets
   `vesselSpawning = false` and **returns without unpacking**
   (confirmed in the C# on 2026-11-12);
4. sets `vesselSpawning = false`, removes the physics hold, calls
   `precalc.GoOffRails()` (empty in `VesselPrecalculate`), then
   **returns if `LandedOrSplashed && !FlightGlobals.RefFrameIsRotating`**:
   stock never unpacks a landed vessel in the inertial frame (confirmed);
5. otherwise unpacks and fires `onVesselGoOffRails`.

So to make stock seat a landed vessel at its next unpack, set both
`PQSminLevel = 0` and `skipGroundPositioning = false`: the first stops
step 3 from setting the second back to true. `checkLanded` writes the body
levels back once the vessel is unpacked and touching the ground.
`FlightGlobals.RefFrameIsRotating` is false on a body that does not rotate.

`Vessel.CheckGroundCollision()`: returns true at once if
`skipGroundPositioning`. Otherwise it raycasts down from above the vessel
with mask `0x8000 | LayerUtil.DefaultEquivalent` (layer 15 is terrain and
scenery). On a hit it moves the vessel up or down onto the surface (log line
`ground contact! - error. Moving Vessel up/down N m`), may align its
rotation, removes the physics hold and returns true. **With no hit it puts
the vessel back and returns false.**

`PQSminLevel` / `PQSmaxLevel` are public fields, written in
`Vessel.checkLanded` and `Vessel.Initialize` (current body levels) and
loaded from the save. `skipGroundPositioning` and `vesselSpawning` are public
fields too.

In the normal case the saved levels equal the current ones, so the ground
check is skipped and a landed vessel unpacks even with no collider under it.
That is why far landed vessels fall through the ground with long ranges.

`OrbitPhysicsManager.setRotatingFrame(bool)`: adds or removes the body's
rotation velocity on every **unpacked** part, then fires
`GameEvents.onRotatingFrameTransition(HostTargetAction<CelestialBody, bool>)`
(`target` is the new rotating state). It does not pack landed vessels.

`Vessel.CheckGroundCollision()` moves the vessel 5000 m up before its
raycast, so the ray does not hit the vessel's own parts, then puts it back.

`Vessel.GoOnRails()`: returns if already packed; fires `onVesselGoOnRails`
**before** packing (in the handler `packed` is still false); packs every
part; sets the orbit driver to IDLE for a landed or splashed vessel.

`Vessel.BestSituation` returns `lastSituation` when that is FLYING, else
`situation`. `updateSituation` sets `lastSituation = situation` right after
firing `onVesselSituationChange`, so in practice `BestSituation` is the
current situation: a vessel that lands far away gets the landed pack range
at its first ground contact.

The KSPCommunityFixes installed in `KSP-kOS` patches none of these methods except a
postfix on `Vessel.GoOnRails` (`PackedPartsRotation`). No other installed
mod references `CheckGroundCollision`, `skipGroundPositioning` or
`onRotatingFrameTransition`.

## Physics hold and kOS *(C#)*

`Vessel.HoldPhysics` is `physicsHoldLock`. `AddPhysicsHoldLock()` is called
only for the **active** vessel (`Vessel.Initialize` with `setActiveVessel`,
and `Vessel.Load` when `Landed && isActiveVessel`). A non-active vessel never
has the lock.

kOS (`~/Projects/KOS-Improved/src/kOS/Module/kOSProcessor.cs`, `FixedUpdate`)
boots and runs its CPU whenever `!vessel.HoldPhysics`. It does not look at
`packed`. So kOS keeps running on any loaded vessel, packed or not. A packed
vessel cannot be moved by throttle or steering.

## Floating origin

- `FloatingOrigin..ctor`: `threshold = 500`, `velForContinuous = 5000`.
  The component is created in code (`AddComponent<FloatingOrigin>()` in
  `PSystemManager.SetupLocalSpace`; no serialized instance exists in the
  level or shared asset files), so these are the real values:
  `thresholdSqr = 250000`.
- `FloatingOrigin.Awake`: `thresholdSqr = threshold * threshold`.
  **This is the only write to `thresholdSqr` and the only read of
  `threshold` in the whole assembly.**
- `FloatingOrigin.FixedUpdate` shifts the origin when
  `ActiveVessel.transform.position.sqrMagnitude > thresholdSqr`, or every
  frame in continuous mode (ship velocity above `velForContinuous`, or
  on-rails warp of a non-landed vessel).
- Consequence: assigning `FloatingOrigin.fetch.threshold` after `Awake` has no
  effect at all.

## Reference frame (rotating vs inertial)

- `OrbitPhysicsManager.checkReferenceFrame`: for a rotating body, compares
  `FlightGlobals.getAltitudeAtPos(ActiveVessel position, body)` with
  `body.inverseRotThresholdAltitude`. Below: rotating frame
  (`setRotatingFrame(true)`). Above: inertial frame.
- `CelestialBody..ctor` default is 15000. `SetupConstants` raises it to
  `atmosphereDepth` when `clampInverseRotThreshold` is set and the atmosphere
  is deeper.
- Actual values in this install (Kopernicus body logs,
  `~/Games/KSP-kOS/Logs/Kopernicus`): 100000 m for every stock body except one
  at 220000 m.

## Active vessel switch

`FlightGlobals.setActiveVessel(v, force, clearDeadVessels)`:

- returns false if `v` is null or already active;
- sets `FlightGlobals.ready = false` and fires `OnFlightGlobalsReady(false)`;
- if there was an active vessel: `MakeInactive()`, then, for a loaded target,
  **`GoOnRails()` on every vessel in `FlightGlobals.Vessels`**;
- fires `onVesselSwitching(previousActive, v)`. **`previousActive` is null on
  the first activation of a scene** (the code path that skips the block above
  jumps straight to this `Fire`);
- then sets `activeVessel` and fires `onVesselChange(v)`.

So each switch packs everything, and each vessel that unpacks afterwards fires
`onVesselGoOffRails`.

## Events

- `EventData<…>.Fire` wraps each handler in try/catch and logs
  `Exception handling event {0} in class {1}:` plus the exception. A throwing
  handler does not stop the others.
- `onVesselCreate` is fired from `Vessel.Initialize` and `ProtoVessel.Load`;
  `onVesselLoaded` from `Vessel.Load`; `onVesselGoOffRails` from
  `Vessel.GoOffRails`; `onVesselSituationChange` from
  `Vessel.updateSituation`.

## Vessel helpers

- `Vessel.SetWorldVelocity(v)`: does nothing when packed; otherwise sets
  `rb.velocity` (and `servoRb.velocity`) of every part to `v`.
- `Vessel.SetPosition(p)`: moves `vesselTransform` and every part to match.
  The position is a world position in the current floating-origin frame.

## PQS (terrain)

- `PQS.StartUpSphere()` is `StartSphere(true)`. With `force = true` and a game
  scene loaded, an already started sphere is torn down and rebuilt.
- In `StartSphere`:
  `visRadDelta = (visRadAltitudeValue - visRadSeaLevelValue) / visRadAltitudeMax`
  and the same shape for `collapseDelta`.
- In `UpdateVisual`:
  `visRad = clamp(visRadSeaLevelValue + visibleAltitude * visRadDelta,
  visRadAltitudeValue, visRadSeaLevelValue)` and
  `visibleRadius = horizonDistance * sin(horizonAngle) * visRad`.
- `maxQuadLenghtsPerFrame`: constructor default is 0.03. Used in
  `UpdateVisual` to cap the subdivision level at high angular speed.
- `detailAltitudeMax`, `detailSeaLevelQuads`, `detailAltitudeQuads`: written
  only in `PQS.Reset`, **never read** in `Assembly-CSharp`.
- `PQS.Reset` defaults: `minLevel 3`, `maxLevel 10`, `minDetailDistance 7`,
  `visRadSeaLevelValue 4`, `visRadAltitudeValue 1.5`,
  `visRadAltitudeMax 100000`, `collapseSeaLevelValue 2`,
  `collapseAltitudeValue 16`, `collapseAltitudeMax 10000000`. Per-body
  values are serialized (not read).

What decides where detailed terrain exists *(C#)*:

- There is one subdivision centre: `PQS.target`. `PQ` uses
  `sphereRoot.relativeTargetPositionNormalized` and `targetHeight` only.
  `secondaryTarget` only feeds `visibleAltitude`.
- `PQSMod_CelestialBodyTransform.OnPreUpdate` calls
  `sphere.SetTarget(FlightGlobals.fetch.activeVessel.transform)` on every
  update (its guard compares a `Vessel` with a `Transform`, so it is always
  true). Setting the target from outside does not stick.
- A quad of level `L` subdivides when
  `gcDist < radius * minDetailDistance / 2^L * subdivisionThreshold`
  (table built once in `PQS.Start`), with
  `gcDist = arc distance * 1.3 + |targetHeight| - angularinterval`.
  With the defaults on Kerbin (600 km), the finest quads exist within about
  8 km of the target, less when the target is high.
- Children collapse when `gcDist > subdivisionThresholds[L] * collapseThreshold`,
  where `collapseThreshold` is `collapseSeaLevelValue + targetAltitude *
  collapseDelta`, raised to `collapseSeaLevelValue` if below it, **else**
  lowered to `collapseAltitudeValue` if above it.
- `PQSMod_QuadMeshColliders.OnQuadBuilt` adds a collider only to quads with
  `subdivision >= maxLevel - |maxLevelOffset|`.

Moving the target, for step 9 *(C#, read 2026-11-12)*:

- `PQS.UpdateSphere` (the update coroutine, every frame unless
  `frameTimeDelta > 0`): `Mod_OnPreUpdate()` (every mod's `OnPreUpdate`, in
  array order), then `UpdateVisual()`, then, if active and `target` is set,
  `UpdateQuads()` and `Mod_OnUpdateFinished()`. A mod's `OnPreUpdate` is the
  last point before the target is read. An exception there ends the
  coroutine (terrain stops updating).
- `SetupMods` collects `PQSMod` components from the sphere's **child**
  GameObjects (recursively), sorts them by `order`, drops disabled ones, and
  calls `OnSetup` on every mod. It runs at sphere start and reset, and via
  `ResetModList` when `Mod_OnUpdateFinished` meets a null (destroyed) mod.
  The result is the private `PQSMod[] mods` with `int modCount`. Kopernicus
  adds `PQSMod_CelestialBodyTransform` with `order = 10`.
- `PQS.SetTarget` and `SetSecondaryTarget` also set every child sphere
  (ocean). `PSystemManager.SpawnBody` sets both target and secondary target
  to the flight camera; nothing in flight changes the secondary target
  afterwards. `UpdateVisual` uses `visibleAltitude = max(target altitude,
  secondary altitude)` for `visibleRadius` (which quads are shown, measured
  from the target) and stock's sphere (de)activation uses `visibleAltitude`.
  So moving only `target` keeps those tied to the camera.
- `UpdateVisual` derives everything else from `target.position`: the
  relative position, `targetHeight` (target altitude minus the terrain
  function under it), the collapse factor, and `angularTargetSpeed`, which
  caps subdivision (`maxLevelAtCurrentTgtSpeed`) when the target moves fast.
  A jump of the target caps it for one update only. Above
  `maxDetailDistance * radius` subdivision is disabled.
- `PQ.UpdateSubdivision` subdivides one level per update (children are not
  recursed in the same pass), within `maxFrameTime` per update, and
  collapses a subdivided quad (and destroys the colliders below it) at once
  when `gcDist > collapseThresholds[level]`.
- `PQSMod_QuadMeshColliders` takes its minimum level
  (`maxLevel - |maxLevelOffset|`) in `OnSetup`.
- `PQS.Update` rebuilds the whole sphere ("PQS …: Restarted") if
  `isThinking` is still set, i.e. `UpdateQuads` threw.
- `PQSMod_CelestialBodyTransform.forceRebuildOnTargetChange` is written
  (stock `Reset`, Kopernicus) but never read.
- `PQSMod_CelestialBodyTransform.OnPreUpdate` resets the sphere when
  `FlightGlobals.currentMainBody` is another body.
- Kopernicus patches `PQS.UpdateVisual` (skips the terrain height lookup
  while the target is above `maxDetailDistance * radius`), `UpdateQuadsInit`,
  `StartSphere` and `ResetAndWait`. Parallax Continued places scatter from
  `FlightGlobals.ActiveVessel`'s position and patches PQS quad destruction
  and `PQ.SetVisible` / `SetInvisible`. Neither touches the target.
- `Vessel.UpdatePosVel`: `radarAltitude` is `heightFromTerrain` (the
  vessel's own ray) when that hit something, else `altitude` minus the
  terrain function (`PQSAltitude()`), or `altitude` over the sea.

What PRE's values therefore do:

- `visRadSeaLevelValue = 200` multiplies `visibleRadius` (which quads are
  shown), not the subdivision distance. It creates no colliders.
- `collapseSeaLevelValue = 200` is above `collapseAltitudeValue`, so the
  clamp turns the collapse factor into the constant `collapseAltitudeValue`
  (16 by default) instead of 2 near the ground. Detailed terrain that the
  target has already been over survives about 8 times farther. It does not
  create detail where the target has not been.
- That is why the old terrain extender switched the active vessel to each
  far landed vessel: it was the only way to move `PQS.target`.

## Camera

- `FlightCamera.OnCameraChange` *(C#)* sets `nearClipPlane` of "Camera 00"
  to `camera00IVANearClipPlane` when the new mode is `IVA`, else to
  `camera00DefaultNearClipPlane`. Both are private fields with initialisers:
  **0.1** and **0.21**.

## Culture

- `HighLogic.Awake` sets `CultureInfo.CurrentCulture`. `float.Parse` on the
  main thread is therefore not locale-dependent in KSP. (Which culture it sets
  was not decoded.)

## IL quirk

KSP's assembly carries junk instructions: `ldc.i4.N` + `switch (IL_x)`,
`dup` + `pop`, and `ldc.i4.1; brtrue; ldtoken; pop`. `tools/ilx.py` strips
them. Offsets in its output are the real ones, so gaps are expected.
