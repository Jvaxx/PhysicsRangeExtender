using System;
using UnityEngine;

namespace PhysicsRangeExtender
{
    /// <summary>
    ///     Flight-scene addon: gives every vessel the ranges built by <see cref="RangeProfile" />, and runs the
    ///     <see cref="LandedVesselGuard" />. Nothing runs per frame; work happens on vessel events and in the
    ///     guard tick.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class PhysicsRangeExtender : MonoBehaviour
    {
        private const float GuardTickInterval = 0.5f;

        /// <summary>The ranges given to every vessel; built from the settings by <see cref="ApplyRanges" />.</summary>
        private VesselRanges _profile;

        private LandedVesselGuard _guard;

        /// <summary>The flight-scene instance, or null outside the flight scene.</summary>
        public static PhysicsRangeExtender Instance { get; private set; }

        /// <summary>Landed vessels in range that stay packed because there is no ground under them yet.</summary>
        public int VesselsWaitingForGround => _guard?.WaitingForGround ?? 0;

        private VesselRanges Profile =>
            _profile ?? (_profile = RangeProfile.Build(PreSettings.GlobalRange, PreSettings.LandedPhysicsRange));

        private void Awake()
        {
            Instance = this;
            _guard = new LandedVesselGuard(() => Profile);
        }

        private void Start()
        {
            // Subscribe whatever ModEnabled is: the mod can be enabled later in this scene.
            // Every handler checks ModEnabled itself.
            GameEvents.onVesselCreate.Add(OnVesselCreate);
            GameEvents.onVesselLoaded.Add(OnVesselLoaded);
            GameEvents.onVesselGoOffRails.Add(OnVesselGoOffRails);
            _guard.Subscribe();
            InvokeRepeating(nameof(GuardTick), GuardTickInterval, GuardTickInterval);

            if (PreSettings.ModEnabled) ApplyAll();
        }

        private void OnDestroy()
        {
            CancelInvoke(nameof(GuardTick));
            GameEvents.onVesselCreate.Remove(OnVesselCreate);
            GameEvents.onVesselLoaded.Remove(OnVesselLoaded);
            GameEvents.onVesselGoOffRails.Remove(OnVesselGoOffRails);
            _guard.Unsubscribe();
            _guard.Clear();

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

        /// <summary>Gives the range profile to one vessel, adjusted by the guard.</summary>
        private void ApplyTo(Vessel vessel)
        {
            if (vessel == null) return;

            try
            {
                vessel.vesselRanges = new VesselRanges(Profile);
                _guard.OnRangesApplied(vessel);
            }
            catch (Exception e)
            {
                Log.Error("Failed to apply ranges to " + vessel.vesselName + ": " + e);
            }
        }

        /// <summary>Gives every vessel stock ranges back and drops what the guard holds.</summary>
        private void RestoreAll()
        {
            _guard.Clear();

            try
            {
                var stock = PhysicsGlobals.Instance != null
                    ? PhysicsGlobals.Instance.VesselRangesDefault
                    : new VesselRanges();
                var vessels = FlightGlobals.Vessels;
                for (var i = 0; i < vessels.Count; i++)
                    vessels[i].vesselRanges = new VesselRanges(stock);
            }
            catch (Exception e)
            {
                Log.Error("Failed to restore stock ranges: " + e);
            }
        }

        private void GuardTick()
        {
            if (!PreSettings.ModEnabled || !FlightGlobals.ready) return;

            try
            {
                _guard.Tick();
            }
            catch (Exception e)
            {
                Log.Error("Landed vessel guard failed: " + e);
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
            ApplyTo(vessel);
        }

        private void OnVesselGoOffRails(Vessel vessel)
        {
            if (!PreSettings.ModEnabled) return;
            ApplyTo(vessel);
        }
    }
}
