using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysicsRangeExtender
{
    /// <summary>
    ///     Keeps landed vessels that are not the active vessel safe at long range, using stock mechanisms only:
    ///     <list type="number">
    ///         <item>
    ///             When the reference frame turns inertial, unpacked landed and splashed vessels are packed.
    ///             Stock <c>Vessel.GoOffRails</c> then refuses to unpack them until the frame rotates again.
    ///         </item>
    ///         <item>
    ///             A packed landed vessel is held (its landed and pre-launch unpack ranges set to 0, so stock
    ///             never unpacks it) until a ray finds a ground collider under it. Stock builds colliders only
    ///             within a few km of the active vessel; unpacked without one, the vessel falls through the
    ///             terrain. A vessel that had to wait is also seated on the ground by stock's
    ///             <c>CheckGroundCollision</c> when it unpacks. An unpacked landed vessel whose ground goes away
    ///             is packed and held. A landed vessel being switched to is left to stock's own ground check,
    ///             which waits for a collider before unpacking it.
    ///         </item>
    ///         <item>
    ///             A vessel that lands farther away than the landed physics range keeps the flying pack range
    ///             until it comes to rest, instead of being packed at first ground contact.
    ///         </item>
    ///     </list>
    ///     Owned by <see cref="PhysicsRangeExtender" />, which calls <see cref="Tick" /> twice a second and
    ///     <see cref="OnRangesApplied" /> each time it gives a vessel new ranges.
    /// </summary>
    internal class LandedVesselGuard
    {
        // Layer 15 holds terrain and KSC buildings.
        private const int GroundMask = 1 << 15;

        // The ray starts this far above the vessel, so that a vessel sitting slightly below the collider
        // surface still finds it, and ends this far below.
        private const float RayAbove = 1000f;
        private const float RayBelow = 1000f;

        private const float SettledSpeed = 0.3f;
        private const float SettledTime = 5f;
        private const float SettleTimeout = 60f;

        private readonly Func<VesselRanges> _profile;

        // Held vessels. The value is true once the vessel was in range and no ground was found under it.
        private readonly Dictionary<Vessel, bool> _held = new Dictionary<Vessel, bool>();
        private readonly Dictionary<Vessel, Settling> _settling = new Dictionary<Vessel, Settling>();
        private readonly List<Vessel> _scratch = new List<Vessel>();

        public LandedVesselGuard(Func<VesselRanges> profile)
        {
            _profile = profile;
        }

        /// <summary>Held vessels close enough to the active vessel to unpack if there were ground under them.</summary>
        public int WaitingForGround { get; private set; }

        public void Subscribe()
        {
            GameEvents.onRotatingFrameTransition.Add(OnRotatingFrameTransition);
            GameEvents.onVesselGoOnRails.Add(OnVesselGoOnRails);
            GameEvents.onVesselSituationChange.Add(OnVesselSituationChange);
            GameEvents.onVesselSwitching.Add(OnVesselSwitching);
        }

        public void Unsubscribe()
        {
            GameEvents.onRotatingFrameTransition.Remove(OnRotatingFrameTransition);
            GameEvents.onVesselGoOnRails.Remove(OnVesselGoOnRails);
            GameEvents.onVesselSituationChange.Remove(OnVesselSituationChange);
            GameEvents.onVesselSwitching.Remove(OnVesselSwitching);
        }

        /// <summary>
        ///     Forgets every vessel without touching its ranges. The caller gives all vessels new ranges right
        ///     after, or the scene is ending.
        /// </summary>
        public void Clear()
        {
            _held.Clear();
            _settling.Clear();
            WaitingForGround = 0;
        }

        /// <summary>Called after <paramref name="vessel" /> was given a fresh copy of the profile ranges.</summary>
        public void OnRangesApplied(Vessel vessel)
        {
            if (vessel == null) return;

            if (_held.ContainsKey(vessel))
            {
                if (IsHoldable(vessel))
                    SetUnpack(vessel, 0f);
                else
                    _held.Remove(vessel);
            }
            else if (IsHoldable(vessel))
            {
                Hold(vessel, "packed, ground not checked yet");
            }

            if (_settling.ContainsKey(vessel)) SetLandedPack(vessel, _profile().flying.pack);
        }

        public void Tick()
        {
            var active = FlightGlobals.ActiveVessel;
            if (active == null) return;

            PruneHeld();
            CheckGround(active);
            CheckSettling(active);
        }

        // Rule 1: inertial frame.
        private void OnRotatingFrameTransition(GameEvents.HostTargetAction<CelestialBody, bool> data)
        {
            if (!PreSettings.ModEnabled || data.target) return;

            var active = FlightGlobals.ActiveVessel;
            _scratch.Clear();
            _scratch.AddRange(FlightGlobals.VesselsLoaded);
            foreach (var vessel in _scratch)
            {
                if (vessel == null || vessel == active || vessel.packed || !vessel.LandedOrSplashed) continue;

                StopSettling(vessel, "reference frame is inertial");
                Log.Debug("Packing " + vessel.vesselName + ": reference frame is inertial");
                vessel.GoOnRails();
            }
        }

        // Fired before the vessel is packed. A landed vessel that packs is held until its ground is checked
        // again: after a vessel switch or time warp, stock would otherwise unpack it on the next frame.
        // This includes the active vessel: a switch packs every vessel before changing the active one. The
        // tick releases the active vessel, whose unpacking does not depend on its ranges anyway.
        private void OnVesselGoOnRails(Vessel vessel)
        {
            if (!PreSettings.ModEnabled || vessel == null) return;

            StopSettling(vessel, "packed");
            if (vessel.loaded && vessel.Landed && !vessel.Splashed)
                Hold(vessel, "packed, ground not checked yet");
        }

        // Fired after every vessel was packed for the switch, before the active vessel changes. Stock unpacks
        // the new active vessel two frames later whatever its ranges, and skips its ground check because
        // its saved terrain levels still match: switched to from far away, it would unpack before terrain
        // colliders exist under it and fall through. With these two flags stock checks for a collider and
        // retries every frame until one is there (see ksp-internals.md, "Unpacking a landed vessel").
        private void OnVesselSwitching(Vessel from, Vessel to)
        {
            if (!PreSettings.ModEnabled || to == null) return;
            if (!to.loaded || !to.packed || !to.Landed || to.Splashed) return;

            if (_held.ContainsKey(to)) Release(to, false, "switching to it");
            Seat(to);
            Log.Debug("Switching to " + to.vesselName + ": stock will wait for ground and seat it");
        }

        // Rule 3: a lander keeps the flying pack range until it is at rest.
        private void OnVesselSituationChange(GameEvents.HostedFromToAction<Vessel, Vessel.Situations> data)
        {
            if (!PreSettings.ModEnabled) return;
            var vessel = data.host;
            if (vessel == null) return;

            if (data.to != Vessel.Situations.LANDED && data.to != Vessel.Situations.SPLASHED)
            {
                StopSettling(vessel, "no longer landed");
                return;
            }

            var active = FlightGlobals.ActiveVessel;
            if (active == null || vessel == active || !vessel.loaded || vessel.packed) return;
            if (!FlightGlobals.RefFrameIsRotating) return;
            if (_settling.ContainsKey(vessel)) return;

            var distance = Vector3.Distance(vessel.transform.position, active.transform.position);
            var profile = _profile();
            if (distance <= profile.landed.unpack) return;

            _settling[vessel] = new Settling { Since = Time.time, CalmSince = -1f };
            SetLandedPack(vessel, profile.flying.pack);
            Log.Debug("Keeping " + vessel.vesselName + " unpacked until it is at rest (" + data.to + " at " +
                      (distance / 1000f).ToString("0.0") + " km)");
        }

        private void PruneHeld()
        {
            if (_held.Count == 0) return;

            _scratch.Clear();
            foreach (var vessel in _held.Keys)
                if (vessel == null || !vessel.loaded || vessel.state == Vessel.State.DEAD)
                    _scratch.Add(vessel);
            foreach (var vessel in _scratch) _held.Remove(vessel);
        }

        // Rule 2: no ground, no physics.
        private void CheckGround(Vessel active)
        {
            var waiting = 0;
            var rotating = FlightGlobals.RefFrameIsRotating;
            var profile = _profile();

            _scratch.Clear();
            _scratch.AddRange(FlightGlobals.VesselsLoaded);
            foreach (var vessel in _scratch)
            {
                if (vessel == null) continue;

                if (!IsHoldable(vessel))
                {
                    if (_held.ContainsKey(vessel))
                        Release(vessel, false, vessel == active ? "is the active vessel" : "no longer packed and landed");
                    if (rotating && IsUnpackedLanded(vessel, active)) PackIfNoGround(vessel, active);
                    continue;
                }

                // Stock does not unpack it anyway; holding it saves stock a GoOffRails call every frame.
                if (!rotating)
                {
                    Hold(vessel, "reference frame is inertial");
                    continue;
                }

                if (vessel.mainBody != active.mainBody)
                {
                    Hold(vessel, "not on the active vessel's body");
                    continue;
                }

                var ranges = profile.GetSituationRanges(vessel.situation);
                var distance = Vector3.Distance(vessel.transform.position, active.transform.position);
                if (distance > ranges.pack)
                {
                    // Out of range: no need to look. Hold it, so that its ground is checked when it comes back.
                    Hold(vessel, "out of range");
                    continue;
                }

                if (HasGround(vessel))
                {
                    if (_held.TryGetValue(vessel, out var waited))
                        Release(vessel, waited, "ground found at " + (distance / 1000f).ToString("0.0") + " km");
                    continue;
                }

                if (!_held.TryGetValue(vessel, out var alreadyWaiting) || !alreadyWaiting)
                {
                    if (!_held.ContainsKey(vessel)) SetUnpack(vessel, 0f);
                    _held[vessel] = true;
                    Log.Debug("Holding " + vessel.vesselName + ": no ground under it at " +
                              (distance / 1000f).ToString("0.0") + " km");
                }

                if (distance < ranges.unpack) waiting++;
            }

            WaitingForGround = waiting;
        }

        private void CheckSettling(Vessel active)
        {
            if (_settling.Count == 0) return;

            var rotating = FlightGlobals.RefFrameIsRotating;
            var now = Time.time;

            _scratch.Clear();
            _scratch.AddRange(_settling.Keys);
            foreach (var vessel in _scratch)
            {
                if (vessel == null || !vessel.loaded || vessel.state == Vessel.State.DEAD)
                {
                    _settling.Remove(vessel);
                    continue;
                }

                if (vessel == active || vessel.packed)
                {
                    StopSettling(vessel, vessel == active ? "is the active vessel" : "packed");
                    continue;
                }

                if (!rotating)
                {
                    StopSettling(vessel, "reference frame is inertial");
                    continue;
                }

                var settling = _settling[vessel];
                if (vessel.srfSpeed < SettledSpeed)
                {
                    if (settling.CalmSince < 0f) settling.CalmSince = now;
                    if (now - settling.CalmSince >= SettledTime)
                    {
                        StopSettling(vessel, "at rest");
                        continue;
                    }
                }
                else
                {
                    settling.CalmSince = -1f;
                }

                if (now - settling.Since >= SettleTimeout) StopSettling(vessel, "still moving after " + SettleTimeout + " s");
            }
        }

        // An unpacked landed vessel loses its ground when the active vessel moves away and the terrain detail
        // under it collapses (stock keeps colliders only within a few km of the active vessel, fewer on small
        // bodies or with the active vessel high up). Pack it before it falls through. It is then held like any
        // packed landed vessel, and seated when it is released, since it may have sunk before the tick saw it.
        private void PackIfNoGround(Vessel vessel, Vessel active)
        {
            if (vessel.mainBody != active.mainBody || HasGround(vessel)) return;

            var distance = Vector3.Distance(vessel.transform.position, active.transform.position);
            StopSettling(vessel, "no ground under it");
            Log.Debug("Packing " + vessel.vesselName + ": no ground under it at " + (distance / 1000f).ToString("0.0") +
                      " km");
            vessel.GoOnRails(); // OnVesselGoOnRails holds it.
            if (_held.ContainsKey(vessel)) _held[vessel] = true;
        }

        private static bool IsHoldable(Vessel vessel)
        {
            return vessel.loaded && vessel.packed && vessel.Landed && !vessel.Splashed &&
                   vessel.state != Vessel.State.DEAD && vessel != FlightGlobals.ActiveVessel;
        }

        private static bool IsUnpackedLanded(Vessel vessel, Vessel active)
        {
            return vessel.loaded && !vessel.packed && vessel.Landed && !vessel.Splashed &&
                   vessel.state != Vessel.State.DEAD && vessel != active;
        }

        /// <summary>True if a ray straight down through the vessel hits a terrain or KSC collider (layer 15).</summary>
        internal static bool HasGround(Vessel vessel)
        {
            Vector3 position = vessel.transform.position;
            Vector3 up = FlightGlobals.getUpAxis(vessel.mainBody, position);
            return Physics.Raycast(position + up * RayAbove, -up, RayAbove + RayBelow, GroundMask,
                QueryTriggerInteraction.Ignore);
        }

        private void Hold(Vessel vessel, string reason)
        {
            if (_held.ContainsKey(vessel)) return;

            _held[vessel] = false;
            SetUnpack(vessel, 0f);
            Log.Debug("Holding " + vessel.vesselName + ": " + reason);
        }

        private void Release(Vessel vessel, bool seat, string reason)
        {
            _held.Remove(vessel);
            SetUnpack(vessel, _profile().landed.unpack);
            if (seat) Seat(vessel);

            Log.Debug("Releasing " + vessel.vesselName + ": " + reason + (seat ? ", stock will seat it" : ""));
        }

        // With PQSminLevel == 0, GoOffRails keeps skipGroundPositioning as set here, runs CheckGroundCollision,
        // moves the vessel onto the collider, and stays packed if there is none. checkLanded writes the body's
        // levels back once the vessel is down.
        private static void Seat(Vessel vessel)
        {
            vessel.PQSminLevel = 0;
            vessel.skipGroundPositioning = false;
        }

        private void StopSettling(Vessel vessel, string reason)
        {
            if (!_settling.Remove(vessel)) return;

            if (vessel.loaded) SetLandedPack(vessel, _profile().landed.pack);
            Log.Debug("Normal pack range again for " + vessel.vesselName + ": " + reason);
        }

        private static void SetUnpack(Vessel vessel, float unpack)
        {
            vessel.vesselRanges.landed.unpack = unpack;
            vessel.vesselRanges.prelaunch.unpack = unpack;
        }

        private static void SetLandedPack(Vessel vessel, float pack)
        {
            vessel.vesselRanges.landed.pack = pack;
            vessel.vesselRanges.splashed.pack = pack;
        }

        private class Settling
        {
            public float CalmSince;
            public float Since;
        }
    }
}
