using UnityEngine;

namespace PhysicsRangeExtender
{
    /// <summary>Builds the <see cref="VesselRanges" /> the mod gives to vessels.</summary>
    internal static class RangeProfile
    {
        /// <summary>The profile for the current settings.</summary>
        public static VesselRanges FromSettings()
        {
            return Build(PreSettings.GlobalRange, PreSettings.LandedPhysicsRange, PreSettings.SpacePhysicsRange);
        }

        /// <summary>
        ///     Every vessel loads within <paramref name="globalKm" />. Flying vessels also run physics (unpack)
        ///     within that range. Landed, splashed and pre-launch vessels run physics only within
        ///     <paramref name="landedKm" />, and sub-orbital, orbiting and escaping vessels only within
        ///     <paramref name="spaceKm" />; both are capped at <paramref name="globalKm" />. Beyond them the
        ///     vessels stay loaded and packed.
        /// </summary>
        /// <remarks>
        ///     Flying cannot get a shorter range: stock <c>Vessel.CheckKill</c> destroys a packed vessel in more
        ///     than 1 kPa of atmosphere when it is beyond the pack range of its situation. A sub-orbital vessel
        ///     coming down becomes FLYING at the edge of the atmosphere, before that pressure, and gets the full
        ///     range back.
        /// </remarks>
        public static VesselRanges Build(float globalKm, float landedKm, float spaceKm)
        {
            var r = globalKm * 1000f;
            var landed = PhysicsWithin(r, landedKm * 1000f);
            var space = PhysicsWithin(r, spaceKm * 1000f);

            // VesselRanges.Situation(load, unload, pack, unpack)
            return new VesselRanges
            {
                flying = new VesselRanges.Situation(r, r * 1.05f, r * 1.10f, r * 0.99f),
                subOrbital = space,
                orbit = new VesselRanges.Situation(space),
                escaping = new VesselRanges.Situation(space),
                landed = landed,
                splashed = new VesselRanges.Situation(landed),
                prelaunch = new VesselRanges.Situation(landed)
            };
        }

        /// <summary>Loads within <paramref name="r" />, runs physics within <paramref name="p" /> (at most r).</summary>
        private static VesselRanges.Situation PhysicsWithin(float r, float p)
        {
            p = Mathf.Clamp(p, 0f, r);
            return new VesselRanges.Situation(r, r * 1.05f, Mathf.Max(p * 1.10f, p + 50f), p);
        }
    }
}
