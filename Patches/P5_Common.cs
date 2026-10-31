using System;
using System.Collections.Generic;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.12.0 (task-16): shared plumbing for the five "A class" facility bonuses (research lab,
    /// turret fire rate, generator output, ejector/silo launch rate, sprayer bonus point).
    /// Every patch is registered manually and is a no-op unless its own config switch is on.
    /// </summary>
    internal static class P5Common
    {
        internal const int DebugLogLimit = 20;
        internal const int EffectiveLogEvery = 200;

        private static readonly HashSet<string> Warned = new HashSet<string>();

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
                    Plugin.Log.LogWarning("[QSystem][p5] " + key + " not armed, vanilla: " + detail);
                }
            }
            catch (Exception)
            {
            }
        }

        internal static bool Enabled
        {
            get
            {
                try
                {
                    return QualityConfig.Enabled != null && QualityConfig.Enabled.Value;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        internal static bool DebugOn
        {
            get
            {
                try
                {
                    return QualityConfig.DebugProbes != null && QualityConfig.DebugProbes.Value;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        internal static void Debug(ref int logged, string line)
        {
            try
            {
                if (!DebugOn || logged >= DebugLogLimit)
                {
                    return;
                }
                logged++;
                Info(line);
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
                if (n % EffectiveLogEvery != 0)
                {
                    return;
                }
                Info("[QSystem][" + feature + "] effective=" + n + (suffix ?? string.Empty));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Planet id resolution for components without a planetId field: the serial GameTick scope
        /// (viaEnv=true) first, then the last-seen/local planet fallback (viaEnv=false).
        /// </summary>
        internal static int ResolvePlanetId(out bool viaEnv)
        {
            int ambient = QualityEnv.AmbientPlanetId;
            if (ambient >= 0)
            {
                viaEnv = true;
                return ambient;
            }
            viaEnv = false;
            return QualityEnv.CurrentPlanetId;
        }
    }
}