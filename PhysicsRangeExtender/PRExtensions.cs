namespace PhysicsRangeExtender
{
    /// <summary>Lets other mods turn PRE off and back on.</summary>
    public static class PRExtensions
    {
        public static bool _wasEnabled = false;

        /// <summary>Turns PRE back on, only if an earlier <see cref="PreOff" /> turned it off.</summary>
        public static void PreOn(string _modName)
        {
            if (!PreSettings.ModEnabled && _wasEnabled)
            {
                _wasEnabled = false;
                Log.Info("Being turned on by " + _modName);
                SetModEnabled(true);
            }
        }

        public static void PreOff(string _modName)
        {
            if (PreSettings.ModEnabled)
            {
                _wasEnabled = true;
                Log.Info("Being turned off by " + _modName);
                SetModEnabled(false);
            }
        }

        /// <summary>
        ///     The player turned the mod on or off. A later <see cref="PreOn" /> no longer overrides that
        ///     choice.
        /// </summary>
        internal static void SetByUser(bool enabled)
        {
            _wasEnabled = false;
            SetModEnabled(enabled);
        }

        /// <summary>
        ///     Enables or disables the mod in the flight scene, or, outside it, only changes and saves the
        ///     setting (the next flight scene reads it).
        /// </summary>
        internal static void SetModEnabled(bool enabled)
        {
            var instance = PhysicsRangeExtender.Instance;
            if (instance != null)
            {
                instance.SetEnabled(enabled);
            }
            else if (PreSettings.ModEnabled != enabled)
            {
                PreSettings.ModEnabled = enabled;
                PreSettings.SaveConfig();
            }
        }
    }
}
