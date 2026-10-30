using System;

namespace DSPQualitySystem.Patches
{
    /// <summary>v0.11.5: tiny log helper shared by the consume-priority patch (keeps files small).</summary>
    internal static class P5_ConsumeLog
    {
        private static readonly System.Collections.Generic.HashSet<string> Warned =
            new System.Collections.Generic.HashSet<string>();

        internal static void Info(string line)
        {
            try
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogInfo(line);
                }
            }
            catch (Exception)
            {
            }
        }

        internal static void Warn(string line)
        {
            try
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning(line);
                }
            }
            catch (Exception)
            {
            }
        }

        internal static void WarnOnce(string key, string detail)
        {
            try
            {
                if (!Warned.Add(key))
                {
                    return;
                }
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning("[QSystem][consume] " + key + " failed: " + detail);
                }
            }
            catch (Exception)
            {
            }
        }

        internal static void Effective(ref long hits, string feature, string suffix)
        {
            try
            {
                long n = ++hits;
                if (n % 200 != 0)
                {
                    return;
                }
                Info("[QSystem][" + feature + "] effective=" + n + (suffix ?? string.Empty));
            }
            catch (Exception)
            {
            }
        }
    }
}