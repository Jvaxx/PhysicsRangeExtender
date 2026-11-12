using System;
using UnityEngine;

namespace PhysicsRangeExtender
{
    [KSPAddon(KSPAddon.Startup.Instantly, false)]
    public class PreSettings : MonoBehaviour
    {
        public static string SettingsConfigUrl = "GameData/PhysicsRangeExtender/settings.cfg";
        public static int GlobalRange { get; set; }

        public static float CamFixMultiplier { get; set; }

        public static bool ConfigLoaded { get; set; } = false;
        public static bool ModEnabled { get; set; }
        public static bool TerrainExtenderEnabled { get; set; }

        /// <summary>Write every decision of the mod to KSP.log (see <see cref="Log.Debug" />).</summary>
        public static bool DebugLog { get; set; }

        void Awake()
        {
            LoadConfig();
            ConfigLoaded = true;
        }

        public static void LoadConfig()
        {
            try
            {
                Log.Info("Loading settings.cfg");

                ConfigNode fileNode = ConfigNode.Load(SettingsConfigUrl);
                if (!fileNode.HasNode("PreSettings")) return;

                ConfigNode settings = fileNode.GetNode("PreSettings");

                // Optional, read first so that a bad value below cannot hide it.
                var debugLog = false;
                settings.TryGetValue("DebugLog", ref debugLog);
                DebugLog = debugLog;

                // No longer used; kept only so that it is written back unchanged until the file format changes.
                var camFixMultiplier = 1f;
                settings.TryGetValue("CamFixMultiplier", ref camFixMultiplier);
                CamFixMultiplier = camFixMultiplier;

                GlobalRange = int.Parse(settings.GetValue("GlobalRange"));
                ModEnabled = bool.Parse(settings.GetValue("ModEnabled"));
                TerrainExtenderEnabled = bool.Parse(settings.GetValue("TerrainExtenderEnabled"));
            }
            catch (Exception ex)
            {
                Log.Error("Failed to load settings config: " + ex.Message);
            }
        }

        public static void SaveConfig()
        {
            try
            {
                Log.Info("Saving settings.cfg");
                ConfigNode fileNode = ConfigNode.Load(SettingsConfigUrl);
                if (!fileNode.HasNode("PreSettings")) return;
                ConfigNode settings = fileNode.GetNode("PreSettings");

                settings.SetValue("GlobalRange", GlobalRange);
                settings.SetValue("CamFixMultiplier", CamFixMultiplier);
                settings.SetValue("ModEnabled", ModEnabled);
                settings.SetValue("TerrainExtenderEnabled", TerrainExtenderEnabled);
                settings.SetValue("DebugLog", DebugLog, true);
                fileNode.Save(SettingsConfigUrl);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to save settings config: " + ex.Message); throw;
            }
        }


    }
}
