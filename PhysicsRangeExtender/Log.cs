namespace PhysicsRangeExtender
{
    /// <summary>
    ///     Log lines with one common prefix. <see cref="Debug" /> lines are written only when
    ///     <see cref="PreSettings.DebugLog" /> is set; they record every decision the mod takes,
    ///     so its in-game behaviour can be checked from KSP.log.
    /// </summary>
    internal static class Log
    {
        private const string Prefix = "[PhysicsRangeExtender] ";

        public static void Info(string message)
        {
            UnityEngine.Debug.Log(Prefix + message);
        }

        public static void Warning(string message)
        {
            UnityEngine.Debug.LogWarning(Prefix + message);
        }

        public static void Error(string message)
        {
            UnityEngine.Debug.LogError(Prefix + message);
        }

        public static void Debug(string message)
        {
            if (PreSettings.DebugLog) UnityEngine.Debug.Log(Prefix + "[debug] " + message);
        }
    }
}
