using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PhysicsRangeExtender
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class PhysicsRangeExtender : MonoBehaviour
    {
        private static bool _unloadDueToReferenceFrameApplied;

        private bool _isSuborbital;

        /// <summary>The ranges given to every vessel; built from the settings by <see cref="ApplyRanges" />.</summary>
        private VesselRanges _profile;

        private VesselRanges Profile =>
            _profile ?? (_profile = RangeProfile.Build(PreSettings.GlobalRange, PreSettings.LandedPhysicsRange));

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
            GameEvents.onVesselCreate.Add(OnVesselCreate);
            GameEvents.onVesselLoaded.Add(OnVesselLoaded);
            GameEvents.onVesselSwitching.Add(OnVesselSwitching);
            GameEvents.onVesselGoOffRails.Add(OnVesselGoOffRails);
            GameEvents.onVesselSituationChange.Add(SituationChangeFixes);

            if (PreSettings.ModEnabled) ApplyAll();
        }

        private void OnDestroy()
        {
            GameEvents.onVesselCreate.Remove(OnVesselCreate);
            GameEvents.onVesselLoaded.Remove(OnVesselLoaded);
            GameEvents.onVesselSwitching.Remove(OnVesselSwitching);
            GameEvents.onVesselGoOffRails.Remove(OnVesselGoOffRails);
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

        /// <summary>
        ///     Applies the current settings to the scene: every vessel gets the ranges built from them. Does
        ///     nothing while the mod is disabled.
        /// </summary>
        public void ApplyAll()
        {
            if (!PreSettings.ModEnabled) return;

            try
            {
                TerrainExtender.UpdateSphere();
            }
            catch (Exception e)
            {
                Log.Error("Failed to update the terrain sphere: " + e);
            }

            ApplyRanges();
        }

        /// <summary>Rebuilds the range profile from the settings and gives it to every vessel.</summary>
        private void ApplyRanges()
        {
            _profile = RangeProfile.Build(PreSettings.GlobalRange, PreSettings.LandedPhysicsRange);
            Log.Info("Applying ranges: " + PreSettings.GlobalRange + " km, landed physics " +
                     _profile.landed.unpack / 1000f + " km");

            var vessels = FlightGlobals.Vessels;
            for (var i = 0; i < vessels.Count; i++) ApplyTo(vessels[i]);
        }

        /// <summary>Gives the range profile to one vessel.</summary>
        private void ApplyTo(Vessel vessel)
        {
            if (vessel == null) return;

            // Landed vessels keep the shorter ranges UnloadLandedVessels gave them while the active vessel
            // is near the inertial frame.
            if (vessel.LandedOrSplashed && !ShouldLandedVesselsBeLoaded()) return;

            try
            {
                vessel.vesselRanges = new VesselRanges(Profile);
            }
            catch (Exception e)
            {
                Log.Error("Failed to apply ranges to " + vessel.vesselName + ": " + e);
            }
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

        private void OnVesselCreate(Vessel vessel)
        {
            if (!PreSettings.ModEnabled) return;
            ApplyTo(vessel);
        }

        private void OnVesselLoaded(Vessel vessel)
        {
            if (!PreSettings.ModEnabled) return;
            NewVesselIsLoaded(vessel);
            ApplyTo(vessel);
        }

        private void OnVesselGoOffRails(Vessel vessel)
        {
            if (!PreSettings.ModEnabled) return;
            ApplyTo(vessel);
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


        private void OnVesselSwitching(Vessel from, Vessel to)
        {
            if (!PreSettings.ModEnabled) return;
            CheckIfFreezeIsNeeded(from, to);
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
                ApplyRanges();
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

                    var newRanges = new VesselRanges(Profile)
                    {
                        landed = safeSituation,
                        prelaunch = safeSituation,
                        splashed = safeSituation
                    };

                    FlightGlobals.VesselsLoaded[i].vesselRanges = newRanges;
                }
        }
    }
}
