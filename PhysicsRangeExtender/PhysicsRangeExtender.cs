using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PhysicsRangeExtender
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class PhysicsRangeExtender : MonoBehaviour
    {
        private static VesselRanges _baseRanges;
        private static VesselRanges.Situation _globalSituation;
        private static bool _unloadDueToReferenceFrameApplied;

        private bool _isSuborbital;

        /// <summary>The flight-scene instance, or null outside the flight scene.</summary>
        public static PhysicsRangeExtender Instance { get; private set; }

        public List<Vessel> VesselToFreeze { get; set; } = new List<Vessel>();

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            // Subscribe whatever ModEnabled is: the mod can be enabled later in this scene.
            // Every handler checks ModEnabled itself.
            GameEvents.onVesselCreate.Add(ApplyPhysRange);
            GameEvents.onVesselLoaded.Add(ApplyPhysRangeOnLoad);
            GameEvents.onVesselSwitching.Add(ApplyPhysRange);
            GameEvents.onVesselGoOffRails.Add(ApplyPhysRange);
            GameEvents.onVesselSituationChange.Add(SituationChangeFixes);

            if (PreSettings.ModEnabled) ApplyAll();
        }

        private void OnDestroy()
        {
            GameEvents.onVesselCreate.Remove(ApplyPhysRange);
            GameEvents.onVesselLoaded.Remove(ApplyPhysRangeOnLoad);
            GameEvents.onVesselSwitching.Remove(ApplyPhysRange);
            GameEvents.onVesselGoOffRails.Remove(ApplyPhysRange);
            GameEvents.onVesselSituationChange.Remove(SituationChangeFixes);

            if (Instance == this) Instance = null;
        }

        /// <summary>
        ///     Turns the mod on or off for the rest of this scene and saves the setting. Called by the GUI
        ///     and by <see cref="PRExtensions" />.
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            if (PreSettings.ModEnabled == enabled) return;

            Log.Info(enabled ? "Enabling" : "Disabling");
            PreSettings.ModEnabled = enabled;
            if (enabled)
                ApplyAll();
            else
                RestoreAll();
            PreSettings.SaveConfig();
        }

        /// <summary>Applies the mod's settings to the current scene. Only call while the mod is enabled.</summary>
        private void ApplyAll()
        {
            try
            {
                TerrainExtender.UpdateSphere();
            }
            catch (Exception e)
            {
                Log.Error("Failed to update the terrain sphere: " + e);
            }

            UpdateRanges();
        }

        /// <summary>Gives every vessel stock ranges back and drops what the mod holds.</summary>
        private void RestoreAll()
        {
            if (VesselToFreeze.Count > 0)
            {
                VesselToFreeze.Clear();
                TerrainExtender.DeactivateNoCrashDamage();
            }

            try
            {
                var stock = PhysicsGlobals.Instance != null
                    ? PhysicsGlobals.Instance.VesselRangesDefault
                    : new VesselRanges();
                var vesselsCount = FlightGlobals.Vessels.Count;
                for (var i = 0; i < vesselsCount; i++)
                    FlightGlobals.Vessels[i].vesselRanges = new VesselRanges(stock);
            }
            catch (Exception e)
            {
                Log.Error("Failed to restore stock ranges: " + e);
            }
        }

        private void SituationChangeFixes(GameEvents.HostedFromToAction<Vessel, Vessel.Situations> data)
        {
            if (!PreSettings.ModEnabled) return;
            RefreshPqsWhenApproaching(data);
        }


        private void RefreshPqsWhenApproaching(GameEvents.HostedFromToAction<Vessel, Vessel.Situations> data)
        {
            var curVessel = data.host;
            if (curVessel == null || curVessel.mainBody == null) return;
            if (!curVessel.mainBody.isHomeWorld || !curVessel.isActiveVessel) return;

            if (data.from == Vessel.Situations.FLYING && data.to == Vessel.Situations.SUB_ORBITAL)
            {
                _isSuborbital = true;
            }
            else if (_isSuborbital && data.to == Vessel.Situations.FLYING)
            {
                _isSuborbital = false;
                var pqs = curVessel.mainBody.pqsController;
                if (pqs == null) return;
                Log.Info("Calling StartUpSphere() to prevent missing PQ tiles");
                pqs.StartUpSphere();
            }
        }

        private void ApplyPhysRangeOnLoad(Vessel data)
        {
            if (!PreSettings.ModEnabled) return;
            NewVesselIsLoaded(data);
            ApplyRangesToVessels();
        }


        private void NewVesselIsLoaded(Vessel vessel)
        {
            var active = FlightGlobals.ActiveVessel;
            if (vessel != null && active != null && !vessel.isActiveVessel && vessel.Landed && vessel.vesselType != VesselType.Debris && Vector3.Distance(vessel.CoM, active.CoM) > 2500)
                if (TerrainExtender.VesselsLandedToLoad.All(x => x.Vessel != vessel))
                    TerrainExtender.VesselsLandedToLoad.Add(new TerrainExtender.VesselLandedState
                    {
                        Vessel = vessel,
                        InitialAltitude = vessel.altitude,
                        InitialPosition = vessel.GetWorldPos3D(),
                        LandedState = TerrainExtender.LandedVesselsStates.NotFocused
                    });
        }


        private void ApplyPhysRange(Vessel data0, Vessel data1)
        {
            if (!PreSettings.ModEnabled) return;
            CheckIfFreezeIsNeeded(data0, data1);
            ApplyRangesToVessels();
        }

        private void CheckIfFreezeIsNeeded(Vessel from, Vessel to)
        {
            // Stock passes from == null on the first activation of a scene.
            if (from == null || to == null) return;

            if (from.Landed && to.situation >= Vessel.Situations.SUB_ORBITAL)
            {
                TerrainExtender.ActivateNoCrashDamage();
                from.SetWorldVelocity(Vector3d.zero);
                VesselToFreeze.Add(from);
                VesselToFreeze.AddRange(FlightGlobals.VesselsLoaded.Where(x => x.LandedOrSplashed));
            }
        }

        private void ApplyPhysRange(Vessel data)
        {
            if (!PreSettings.ModEnabled) return;
            ApplyRangesToVessels();
        }


        private void FixedUpdate()
        {
            if (!PreSettings.ModEnabled) return;
            AvoidReferenceFrameChangeIssues();
            FreezeLandedVesselWhenSwitching();
        }

        private void FreezeLandedVesselWhenSwitching()
        {
            VesselToFreeze.RemoveAll(x => x == null);
            VesselToFreeze.RemoveAll(x => !x.loaded);

            if (VesselToFreeze.Count == 0) TerrainExtender.DeactivateNoCrashDamage();
            VesselToFreeze.ForEach(x => x?.SetWorldVelocity(Vector3d.zero));
        }

        private void AvoidReferenceFrameChangeIssues()
        {
            if (!ShouldLandedVesselsBeLoaded())
            {
                if (!_unloadDueToReferenceFrameApplied)
                {
                    UnloadLandedVessels();
                    _unloadDueToReferenceFrameApplied = true;
                }
            }
            else if (_unloadDueToReferenceFrameApplied)
            {
                UpdateRanges();
                _unloadDueToReferenceFrameApplied = false;
            }
        }

        /// <summary>
        ///     This method will avoid landed vessels to be destroyed due to changes on the referencial frame (inertial vs
        ///     rotation) when the active vessel is going suborbital
        /// </summary>
        /// <returns> if landed vessel should be loaded</returns>
        private static bool ShouldLandedVesselsBeLoaded()
        {
            var safetyMargin = 0.90f;

            if (FlightGlobals.ActiveVessel == null ||
                FlightGlobals.ActiveVessel.LandedOrSplashed ||
                FlightGlobals.ActiveVessel.orbit == null ||
                FlightGlobals.ActiveVessel.orbit.referenceBody == null)
                return true;

            var altitudeAtPos =
                (double)FlightGlobals.getAltitudeAtPos(FlightGlobals.ActiveVessel.transform.position,
                    FlightGlobals.ActiveVessel.orbit.referenceBody);

            if (altitudeAtPos / FlightGlobals.ActiveVessel.orbit.referenceBody.inverseRotThresholdAltitude >
                safetyMargin)
                return false;
            return true;
        }

        /// <summary>
        ///     This method will reduce the load/unload distances using a closer range to avoid issues.
        /// </summary>
        private void UnloadLandedVessels()
        {
            var vesselsCount = FlightGlobals.VesselsLoaded.Count;
            ScreenMessages.PostScreenMessage(
                "[PhysicsRangeExtender] Unloading landed vessels during active orbital fly.", 3f,
                ScreenMessageStyle.UPPER_CENTER);
            for (var i = 0; i < vesselsCount; i++)
                if (FlightGlobals.VesselsLoaded[i].LandedOrSplashed)
                {
                    var safeSituation = new VesselRanges.Situation(
                        FlightGlobals.ActiveVessel.orbit.referenceBody.inverseRotThresholdAltitude * 0.90f,
                        FlightGlobals.ActiveVessel.orbit.referenceBody.inverseRotThresholdAltitude * 0.95f,
                        FlightGlobals.ActiveVessel.orbit.referenceBody.inverseRotThresholdAltitude * 1.10f,
                        FlightGlobals.ActiveVessel.orbit.referenceBody.inverseRotThresholdAltitude * 0.99f);

                    var newRanges = new VesselRanges
                    {
                        escaping = _globalSituation,
                        flying = _globalSituation,
                        landed = safeSituation,
                        orbit = _globalSituation,
                        prelaunch = safeSituation,
                        splashed = safeSituation,
                        subOrbital = _globalSituation
                    };

                    FlightGlobals.VesselsLoaded[i].vesselRanges = newRanges;
                }
        }

        public static void UpdateRanges(bool updatingFromUi = false)
        {
            Log.Info("Updating ranges");

            if (updatingFromUi) TerrainExtender.UpdateSphere();

            _globalSituation = new VesselRanges.Situation(
                PreSettings.GlobalRange * 1000,
                PreSettings.GlobalRange * 1000 * 1.05f,
                PreSettings.GlobalRange * 1000 * 1.10f,
                PreSettings.GlobalRange * 1000 * 0.99f);

            _baseRanges = new VesselRanges
            {
                escaping = _globalSituation,
                flying = _globalSituation,
                landed = _globalSituation,
                orbit = _globalSituation,
                prelaunch = _globalSituation,
                splashed = _globalSituation,
                subOrbital = _globalSituation
            };
            ApplyRangesToVessels(updatingFromUi);
        }

        private static void ApplyRangesToVessels(bool updatingFromUi = false)
        {
            if (!PreSettings.ModEnabled) return;
            try
            {
                var vesselsCount = FlightGlobals.Vessels.Count;

                for (var i = 0; i < vesselsCount; i++)
                {
                    // check to avoid landed vessels to be destroyed when the active vessel is sub-orbital
                    if (FlightGlobals.Vessels[i].LandedOrSplashed && !ShouldLandedVesselsBeLoaded()) continue;
                    //
                    if (VesselOrbitingWhileUpdatingRangeFromUi(updatingFromUi, FlightGlobals.Vessels[i])) continue;

                    FlightGlobals.Vessels[i].vesselRanges = new VesselRanges(_baseRanges);
                }
            }
            catch (Exception e)
            {
                Log.Error("Failed to apply ranges: " + e);
            }
        }

        /// <summary>
        ///     This method will avoid de-orbiting unloaded vessels when a user is extending the range using the UI and orbiting
        ///     vessels are getting loaded.
        /// </summary>
        /// <param name="updatingFromUi"></param>
        /// <param name="vessel"></param>
        /// <returns></returns>
        private static bool VesselOrbitingWhileUpdatingRangeFromUi(bool updatingFromUi, Vessel vessel)
        {
            return !vessel.isActiveVessel && updatingFromUi && !vessel.LandedOrSplashed;
        }
    }
}
