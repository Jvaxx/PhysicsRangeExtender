using System;
using System.Reflection;
using UnityEngine;

namespace PhysicsRangeExtender
{
    /// <summary>
    ///     A terrain mod that moves the subdivision target of a body's sphere (<see cref="PQS.target" />) to a
    ///     transform of our choice. Stock <c>PQSMod_CelestialBodyTransform.OnPreUpdate</c> sets the target to the
    ///     active vessel on every sphere update; this mod runs after it (highest <see cref="PQSMod.order" />), before
    ///     <c>PQS.UpdateVisual</c> and <c>UpdateQuads</c> read it. Terrain, and its colliders, then build around
    ///     <see cref="Target" /> instead of the active vessel. <see cref="PQS.secondaryTarget" /> (the flight camera)
    ///     is left alone, so the visible radius and stock's sphere activation still follow the camera.
    /// </summary>
    /// <remarks>
    ///     PQS collects its mods only in <c>SetupMods</c>, which runs at sphere start or reset and calls
    ///     <c>OnSetup</c> on every mod. To avoid that, <see cref="Install" /> adds the mod to the private
    ///     <c>mods</c> array directly. The mod also lives on a child GameObject of the sphere, where a later
    ///     <c>SetupMods</c> finds it on its own. <see cref="Uninstall" /> takes it out of the array before
    ///     destroying it: a destroyed mod left in the array makes PQS rebuild its mod list.
    /// </remarks>
    internal class TerrainTargetMod : PQSMod
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo ModsField = typeof(PQS).GetField("mods", Private);
        private static readonly FieldInfo ModCountField = typeof(PQS).GetField("modCount", Private);

        /// <summary>The transform terrain builds around, or null to leave stock's target in place.</summary>
        public Transform Target;

        private bool _failed;

        /// <summary>Frame-time statistics over the sphere updates since the last <see cref="TakeSample" />.</summary>
        private FrameStats _stats;

        /// <summary>False if this KSP version does not have the private PQS fields the mod needs.</summary>
        public static bool Supported => ModsField != null && ModCountField != null &&
                                        ModsField.FieldType == typeof(PQSMod[]) &&
                                        ModCountField.FieldType == typeof(int);

        public static TerrainTargetMod Install(PQS sphere)
        {
            var go = new GameObject("PhysicsRangeExtender_TerrainTarget");
            go.transform.SetParent(sphere.transform, false);
            var mod = go.AddComponent<TerrainTargetMod>();
            mod.sphere = sphere;
            mod.order = int.MaxValue;
            mod.modEnabled = true;
            mod.requirements = PQS.ModiferRequirements.Default;

            var mods = (PQSMod[]) ModsField.GetValue(sphere) ?? new PQSMod[0];
            if (Array.IndexOf(mods, mod) < 0)
            {
                var grown = new PQSMod[mods.Length + 1];
                Array.Copy(mods, grown, mods.Length);
                grown[mods.Length] = mod; // Highest order: last, as SetupMods would sort it.
                ModsField.SetValue(sphere, grown);
                ModCountField.SetValue(sphere, grown.Length);
            }

            return mod;
        }

        public void Uninstall()
        {
            Target = null;
            if (sphere != null)
            {
                var mods = (PQSMod[]) ModsField.GetValue(sphere);
                var index = mods == null ? -1 : Array.IndexOf(mods, this);
                if (index >= 0)
                {
                    var shrunk = new PQSMod[mods.Length - 1];
                    Array.Copy(mods, 0, shrunk, 0, index);
                    Array.Copy(mods, index + 1, shrunk, index, mods.Length - index - 1);
                    ModsField.SetValue(sphere, shrunk);
                    ModCountField.SetValue(sphere, shrunk.Length);
                }
            }

            Destroy(gameObject);
        }

        /// <summary>Returns the frame-time statistics since the last call and starts a new sample.</summary>
        public FrameStats TakeSample()
        {
            var stats = _stats;
            _stats = default(FrameStats);
            return stats;
        }

        public override void OnPreUpdate()
        {
            // Runs inside the sphere's update coroutine: an exception here would stop the terrain updating.
            try
            {
                _stats.Add(Time.unscaledDeltaTime);
                if (Target != null && !_failed) sphere.SetTarget(Target);
            }
            catch (Exception e)
            {
                _failed = true;
                Log.Error("Terrain target failed, back to stock: " + e);
            }
        }

        internal struct FrameStats
        {
            public int Frames;
            public int SlowFrames;
            public float Sum;
            public float Worst;

            public void Add(float seconds)
            {
                Frames++;
                Sum += seconds;
                if (seconds > Worst) Worst = seconds;
                if (seconds > 0.05f) SlowFrames++;
            }

            public override string ToString()
            {
                if (Frames == 0) return "no sphere updates";
                return Frames + " updates, mean " + (Sum / Frames * 1000f).ToString("0.0") + " ms, worst " +
                       (Worst * 1000f).ToString("0.0") + " ms, " + SlowFrames + " over 50 ms";
            }
        }
    }
}
