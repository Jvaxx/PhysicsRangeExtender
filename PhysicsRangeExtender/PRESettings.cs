using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace PhysicsRangeExtender
{
    /// <summary>
    ///     The mod's settings, loaded once at game start and saved when the player or another mod changes them.
    ///     The file lives in <c>PluginData</c>, which KSP does not load as game config, so saving it does not
    ///     invalidate ModuleManager's cache. Defaults live in code; the mod does not ship a settings file.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, false)]
    public class PreSettings : MonoBehaviour
    {
        public const int MinGlobalRange = 3;
        public const int MaxGlobalRange = 2000;
        public const int DefaultGlobalRange = 100;
        public const float MinPhysicsRange = 0.2f;
        public const float DefaultLandedPhysicsRange = 5f;
        public const float DefaultSpacePhysicsRange = 20f;

        private const string NodeName = "PreSettings";

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>
        ///     Distance in km within which every vessel loads. Flying and sub-orbital vessels also run physics
        ///     (unpack) within it: stock destroys a packed vessel flying in atmosphere beyond its pack range.
        /// </summary>
        public static int GlobalRange { get; set; } = DefaultGlobalRange;

        /// <summary>
        ///     Distance in km within which landed, splashed and pre-launch vessels run physics. They still
        ///     load within <see cref="GlobalRange" />, packed.
        /// </summary>
        public static float LandedPhysicsRange { get; set; } = DefaultLandedPhysicsRange;

        /// <summary>
        ///     Distance in km within which orbiting and escaping vessels run physics. They still
        ///     load within <see cref="GlobalRange" />, packed. Physics far from the active vessel runs at low
        ///     floating-point precision, which makes vessels shake.
        /// </summary>
        public static float SpacePhysicsRange { get; set; } = DefaultSpacePhysicsRange;

        public static bool ConfigLoaded { get; set; }
        public static bool ModEnabled { get; set; } = true;

        /// <summary>Write every decision of the mod to KSP.log (see <see cref="Log.Debug" />).</summary>
        public static bool DebugLog { get; set; }

        /// <summary>The settings file: <c>GameData/PhysicsRangeExtender/PluginData/settings.cfg</c>.</summary>
        public static string SettingsPath =>
            Path.Combine(ModDirectory, Path.Combine("PluginData", "settings.cfg"));

        /// <summary>Where versions up to 1.21 kept the settings. Read once to migrate, never written.</summary>
        private static string OldSettingsPath => Path.Combine(ModDirectory, "settings.cfg");

        private static string ModDirectory =>
            Path.Combine(KSPUtil.ApplicationRootPath, Path.Combine("GameData", "PhysicsRangeExtender"));

        private void Awake()
        {
            LoadConfig();
            ConfigLoaded = true;
        }

        /// <summary>
        ///     Loads the settings file; else migrates the pre-1.22 file to it, leaving the old one in place;
        ///     else keeps the defaults. A missing or invalid value keeps its default without affecting the others.
        /// </summary>
        public static void LoadConfig()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    Log.Info("Loading " + SettingsPath);
                    Read(ConfigNode.Load(SettingsPath));
                }
                else if (File.Exists(OldSettingsPath))
                {
                    Log.Info("Migrating settings from " + OldSettingsPath + " to " + SettingsPath);
                    Read(ConfigNode.Load(OldSettingsPath));
                    SaveConfig();
                }
                else
                {
                    Log.Info("No settings file, using defaults");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Failed to load settings: " + ex);
            }

            Log.Info("Settings: GlobalRange = " + GlobalRange + ", LandedPhysicsRange = " +
                     LandedPhysicsRange.ToString(Invariant) + ", SpacePhysicsRange = " +
                     SpacePhysicsRange.ToString(Invariant) + ", ModEnabled = " + ModEnabled + ", DebugLog = " +
                     DebugLog);
        }

        /// <summary>Writes every setting to <see cref="SettingsPath" />. Logs a failure; never throws.</summary>
        public static void SaveConfig()
        {
            try
            {
                var root = new ConfigNode();
                var settings = root.AddNode(NodeName);
                settings.AddValue("GlobalRange", GlobalRange.ToString(Invariant));
                settings.AddValue("LandedPhysicsRange", LandedPhysicsRange.ToString(Invariant));
                settings.AddValue("SpacePhysicsRange", SpacePhysicsRange.ToString(Invariant));
                settings.AddValue("ModEnabled", ModEnabled.ToString());
                settings.AddValue("DebugLog", DebugLog.ToString());

                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                root.Save(SettingsPath);
                Log.Info("Saved " + SettingsPath);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to save settings: " + ex);
            }
        }

        private static void Read(ConfigNode file)
        {
            var settings = file?.GetNode(NodeName);
            if (settings == null)
            {
                Log.Warning("No " + NodeName + " node in the settings file, using defaults");
                return;
            }

            GlobalRange = Mathf.Clamp(ReadInt(settings, "GlobalRange", DefaultGlobalRange), MinGlobalRange,
                MaxGlobalRange);
            LandedPhysicsRange = ClampPhysicsRange(ReadFloat(settings, "LandedPhysicsRange", DefaultLandedPhysicsRange));
            SpacePhysicsRange = ClampPhysicsRange(ReadFloat(settings, "SpacePhysicsRange", DefaultSpacePhysicsRange));
            ModEnabled = ReadBool(settings, "ModEnabled", true);
            DebugLog = ReadBool(settings, "DebugLog", false);
        }

        /// <summary>Clamps a physics range to its valid interval, which depends on <see cref="GlobalRange" />.</summary>
        private static float ClampPhysicsRange(float km)
        {
            return Mathf.Clamp(km, MinPhysicsRange, GlobalRange);
        }

        private static int ReadInt(ConfigNode node, string name, int fallback)
        {
            var text = node.GetValue(name);
            if (text == null) return fallback;
            if (int.TryParse(text.Trim(), NumberStyles.Integer, Invariant, out var value)) return value;

            Log.Warning("Invalid " + name + " = '" + text + "', using " + fallback);
            return fallback;
        }

        private static float ReadFloat(ConfigNode node, string name, float fallback)
        {
            var text = node.GetValue(name);
            if (text == null) return fallback;
            if (float.TryParse(text.Trim(), NumberStyles.Float, Invariant, out var value) &&
                !float.IsNaN(value) && !float.IsInfinity(value))
                return value;

            Log.Warning("Invalid " + name + " = '" + text + "', using " + fallback.ToString(Invariant));
            return fallback;
        }

        private static bool ReadBool(ConfigNode node, string name, bool fallback)
        {
            var text = node.GetValue(name);
            if (text == null) return fallback;
            if (bool.TryParse(text.Trim(), out var value)) return value;

            Log.Warning("Invalid " + name + " = '" + text + "', using " + fallback);
            return fallback;
        }
    }
}
