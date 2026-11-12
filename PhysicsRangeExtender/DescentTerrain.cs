using System.Collections.Generic;
using UnityEngine;

namespace PhysicsRangeExtender
{
    /// <summary>
    ///     Experimental (setting <see cref="PreSettings.TerrainUnderDescendingVessels" />, off by default): builds
    ///     collider terrain under a far, non-active vessel that is coming down to land, by moving the terrain
    ///     subdivision target to it with <see cref="TerrainTargetMod" />.
    ///     <para>
    ///         Stock builds terrain, and colliders, only around one point, the active vessel. Moving that point
    ///         takes the active vessel's own detailed terrain away, so this only runs while the active vessel is
    ///         high enough to have no ground colliders anyway, and gives the point back as soon as that ends.
    ///     </para>
    ///     Owned by <see cref="PhysicsRangeExtender" />, which calls <see cref="Tick" /> after the guard tick.
    /// </summary>
    internal class DescentTerrain
    {
        // The active vessel must be this high above the ground (or sea) to start, and is given its terrain
        // back below the stop height. Kerbin's finest terrain exists within about 8 km of the target, height
        // included, and collapses at about twice that.
        private const double ActiveStartHeight = 20000;
        private const double ActiveStopHeight = 15000;

        // A vessel is picked below the start height while descending, and dropped if it climbs above the stop
        // height. Terrain needs about one sphere update per subdivision level (about 10) to reach colliders.
        private const double VesselStartHeight = 5000;
        private const double VesselStopHeight = 7000;
        private const double MinDescentSpeed = 1;

        private const float SettledSpeed = 0.3f;
        private const float SettledTime = 5f;
        private const float LandedTimeout = 60f;

        // After the target goes back, keep sampling frame times this long (the active vessel's terrain rebuilds).
        private const float AfterReleaseSampling = 5f;

        private readonly HashSet<PQS> _describedSpheres = new HashSet<PQS>();

        private TerrainTargetMod _mod;
        private Vessel _vessel;
        // Real time, for the measurements.
        private float _since;

        // Game time, for the landing rules.
        private float _landedSince = -1f;
        private float _calmSince = -1f;
        private float _groundAfter = -1f;
        private float _releasedAt = -1f;
        private bool _unsupportedLogged;

        /// <summary>The vessel terrain is currently built around, or null.</summary>
        public Vessel Vessel => _vessel;

        public void Tick(Vessel active)
        {
            if (!PreSettings.TerrainUnderDescendingVessels)
            {
                Stop("setting is off");
                Uninstall();
                return;
            }

            if (!TerrainTargetMod.Supported)
            {
                if (!_unsupportedLogged) Log.Error("Terrain under descending vessels: PQS fields not found, disabled");
                _unsupportedLogged = true;
                return;
            }

            if (_vessel != null)
            {
                var reason = StopReason(_vessel, active);
                if (reason != null) Stop(reason);
                else CheckGroundReady();
            }

            if (_vessel == null)
            {
                var candidate = PickCandidate(active);
                if (candidate != null) Start(candidate, active);
            }

            if (_vessel == null && _mod != null && _releasedAt >= 0f &&
                Time.realtimeSinceStartup - _releasedAt >= AfterReleaseSampling)
            {
                Log.Info("Terrain target: " + AfterReleaseSampling + " s after giving it back: " + _mod.TakeSample());
                Uninstall();
            }
        }

        /// <summary>Gives the terrain target back to stock and removes the mod. Safe to call at any time.</summary>
        public void Shutdown(string reason)
        {
            Stop(reason);
            Uninstall();
        }

        private Vessel PickCandidate(Vessel active)
        {
            if (active.LandedOrSplashed || !FlightGlobals.RefFrameIsRotating) return null;

            var body = active.mainBody;
            var sphere = body.pqsController;
            if (sphere == null || !sphere.isActive) return null;
            if (HeightAboveSurface(active) < ActiveStartHeight) return null;

            Vessel best = null;
            var bestHeight = double.MaxValue;
            var loaded = FlightGlobals.VesselsLoaded;
            for (var i = 0; i < loaded.Count; i++)
            {
                var vessel = loaded[i];
                if (vessel == null || vessel == active || vessel.packed || vessel.state == Vessel.State.DEAD) continue;
                if (vessel.mainBody != body) continue;
                if (vessel.situation != Vessel.Situations.FLYING &&
                    vessel.situation != Vessel.Situations.SUB_ORBITAL) continue;
                if (vessel.verticalSpeed > -MinDescentSpeed) continue;

                var terrain = TerrainAltitude(vessel);
                if (body.ocean && terrain < 0) continue; // It will splash down: no colliders needed.

                var height = vessel.altitude - terrain;
                if (height > VesselStartHeight || height >= bestHeight) continue;

                best = vessel;
                bestHeight = height;
            }

            return best;
        }

        private void Start(Vessel vessel, Vessel active)
        {
            var sphere = vessel.mainBody.pqsController;

            // The terrain around the active vessel collapses once the target moves: pack any landed vessel
            // still running physics there (possible only with a landed physics range above the active
            // vessel's height). The guard holds it and releases it when there is ground again.
            PackLandedVessels(vessel, "before the terrain target moves away");

            if (_mod != null && _mod.sphere != sphere) Uninstall();
            if (_mod == null) _mod = TerrainTargetMod.Install(sphere);
            else _mod.TakeSample(); // Drop the after-release sample of the previous vessel.

            _mod.Target = vessel.transform;
            _vessel = vessel;
            _since = Time.realtimeSinceStartup;
            _landedSince = -1f;
            _calmSince = -1f;
            _groundAfter = -1f;
            _releasedAt = -1f;

            DescribeOnce(sphere);
            Log.Info("Terrain target moved to " + vessel.vesselName + ": " +
                     (vessel.altitude - TerrainAltitude(vessel)).ToString("0") + " m above ground, " +
                     (Vector3.Distance(vessel.transform.position, active.transform.position) / 1000f).ToString("0.0") +
                     " km from " + active.vesselName + " (" + HeightAboveSurface(active).ToString("0") + " m up)");
        }

        /// <summary>Why terrain should no longer follow <paramref name="vessel" />, or null to keep it.</summary>
        private string StopReason(Vessel vessel, Vessel active)
        {
            if (vessel == null || !vessel.loaded || vessel.state == Vessel.State.DEAD) return "vessel gone";
            if (vessel == active) return "it is the active vessel";
            if (vessel.packed) return "it is packed";
            if (vessel.mainBody != active.mainBody || _mod == null || _mod.sphere != vessel.mainBody.pqsController)
                return "not on the active vessel's body";
            if (!_mod.sphere.isActive) return "the terrain sphere was switched off (active vessel too high)";
            if (active.LandedOrSplashed || HeightAboveSurface(active) < ActiveStopHeight)
                return "active vessel is below " + (ActiveStopHeight / 1000).ToString("0") + " km";
            if (vessel.Splashed) return "it splashed down";

            if (!vessel.Landed)
            {
                _landedSince = -1f;
                _calmSince = -1f;
                if (vessel.altitude - TerrainAltitude(vessel) > VesselStopHeight) return "it climbed";
                return null;
            }

            // Landed: stock packs a landed vessel only in the rotating frame, and the guard settles it.
            if (!FlightGlobals.RefFrameIsRotating) return "reference frame is inertial";

            var now = Time.time; // Game time, like the guard's settling.
            if (_landedSince < 0f) _landedSince = now;
            if (vessel.srfSpeed < SettledSpeed)
            {
                if (_calmSince < 0f) _calmSince = now;
                if (now - _calmSince >= SettledTime) return "landed and at rest";
            }
            else
            {
                _calmSince = -1f;
            }

            return now - _landedSince >= LandedTimeout ? "landed " + LandedTimeout + " s ago" : null;
        }

        private void CheckGroundReady()
        {
            if (_groundAfter >= 0f || !LandedVesselGuard.HasGround(_vessel)) return;

            _groundAfter = Time.realtimeSinceStartup - _since;
            Log.Info("Terrain target: ground collider under " + _vessel.vesselName + " after " +
                     _groundAfter.ToString("0.00") + " s, " +
                     (_vessel.altitude - TerrainAltitude(_vessel)).ToString("0") + " m above it; so far " +
                     _mod.TakeSample());
        }

        private void Stop(string reason)
        {
            if (_vessel == null && (_mod == null || _mod.Target == null)) return;

            var vessel = _vessel;
            _vessel = null;

            // Terrain collapses under the vessel within one sphere update once the target goes back. Pack the
            // landed vessels first (the vessel and any neighbour); the guard then holds them until there is
            // ground again.
            PackLandedVessels(null, "before its terrain goes away");
            if (vessel != null && vessel.loaded && !vessel.packed && !vessel.LandedOrSplashed &&
                vessel != FlightGlobals.ActiveVessel && vessel.state != Vessel.State.DEAD)
                Log.Warning(vessel.vesselName + " is still in flight and loses the terrain under it");

            var name = vessel != null ? vessel.vesselName : "vessel";
            var ground = _groundAfter >= 0f ? "ground after " + _groundAfter.ToString("0.00") + " s" : "no ground found";
            var sample = _mod != null ? _mod.TakeSample().ToString() : "no sample";
            Log.Info("Terrain target back to the active vessel (" + name + ": " + reason + "; " + ground + "; " +
                     sample + ")");

            if (_mod != null) _mod.Target = null;
            _releasedAt = Time.realtimeSinceStartup;
        }

        /// <summary>Packs every loaded, unpacked, landed (not splashed) vessel except the active one and
        /// <paramref name="except" />.</summary>
        private static void PackLandedVessels(Vessel except, string reason)
        {
            var active = FlightGlobals.ActiveVessel;
            var loaded = new List<Vessel>(FlightGlobals.VesselsLoaded);
            foreach (var vessel in loaded)
            {
                if (vessel == null || vessel == active || vessel == except || vessel.packed || !vessel.Landed ||
                    vessel.Splashed || vessel.state == Vessel.State.DEAD)
                    continue;

                Log.Info("Packing " + vessel.vesselName + " " + reason);
                vessel.GoOnRails();
            }
        }

        private void Uninstall()
        {
            if (_mod == null) return;
            _mod.Uninstall();
            _mod = null;
            _releasedAt = -1f;
        }

        private void DescribeOnce(PQS sphere)
        {
            if (!_describedSpheres.Add(sphere)) return;

            var level = sphere.maxLevel - 1;
            var threshold = sphere.subdivisionThresholds != null && level >= 0 &&
                            level < sphere.subdivisionThresholds.Length
                ? sphere.subdivisionThresholds[level].ToString("0")
                : "?";
            var colliders = sphere.GetComponentsInChildren<PQSMod_QuadMeshColliders>(true);
            var colliderOffset = colliders.Length > 0 ? colliders[0].maxLevelOffset.ToString() : "none";
            var transform = sphere.GetComponentsInChildren<PQSMod_CelestialBodyTransform>(true);
            var deactivate = transform.Length > 0 ? transform[0].deactivateAltitude.ToString("0") : "?";

            Log.Info("Terrain of " + sphere.name + ": radius " + sphere.radius.ToString("0") + " m, levels " +
                     sphere.minLevel + "-" + sphere.maxLevel + ", subdivision threshold at level " + level + ": " +
                     threshold + " m, collapse factor " + sphere.collapseSeaLevelValue + " to " +
                     sphere.collapseAltitudeValue + ", collider level offset " + colliderOffset +
                     ", deactivate altitude " + deactivate + " m, frameTimeDelta " + sphere.frameTimeDelta +
                     ", maxFrameTime " + sphere.maxFrameTime);
        }

        /// <summary>Terrain altitude under the vessel from the terrain function (no collider needed).</summary>
        private static double TerrainAltitude(Vessel vessel)
        {
            var body = vessel.mainBody;
            if (body.pqsController == null) return 0;
            return body.pqsController.GetSurfaceHeight(body.GetRelSurfaceNVector(vessel.latitude, vessel.longitude)) -
                   body.Radius;
        }

        /// <summary>Height above the terrain, or above the sea where the terrain is below it.</summary>
        private static double HeightAboveSurface(Vessel vessel)
        {
            var terrain = TerrainAltitude(vessel);
            if (vessel.mainBody.ocean && terrain < 0) terrain = 0;
            return vessel.altitude - terrain;
        }
    }
}
