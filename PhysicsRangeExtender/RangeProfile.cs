using UnityEngine;

namespace PhysicsRangeExtender
{
    /// <summary>Builds the <see cref="VesselRanges" /> the mod gives to vessels.</summary>
    internal static class RangeProfile
    {
        /// <summary>
        ///     Every vessel loads within <paramref name="globalKm" />. Vessels in flight or in space also run
        ///     physics (unpack) within that range; landed, splashed and pre-launch vessels only within
        ///     <paramref name="landedKm" /> (at most <paramref name="globalKm" />), and stay packed beyond it.
        /// </summary>
        public static VesselRanges Build(float globalKm, float landedKm)
        {
            var r = globalKm * 1000f;
            var l = Mathf.Clamp(landedKm, 0f, globalKm) * 1000f;

            // VesselRanges.Situation(load, unload, pack, unpack)
            return new VesselRanges
            {
                flying = Free(r),
                subOrbital = Free(r),
                orbit = Free(r),
                escaping = Free(r),
                landed = Landed(r, l),
                splashed = Landed(r, l),
                prelaunch = Landed(r, l)
            };
        }

        private static VesselRanges.Situation Free(float r)
        {
            return new VesselRanges.Situation(r, r * 1.05f, r * 1.10f, r * 0.99f);
        }

        private static VesselRanges.Situation Landed(float r, float l)
        {
            return new VesselRanges.Situation(r, r * 1.05f, Mathf.Max(l * 1.10f, l + 50f), l);
        }
    }
}
