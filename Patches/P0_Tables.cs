using System;
using System.Globalization;
using System.Text;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// M0 core: replace the four vanilla linear tables so that one "inc" level equals
    /// one quality point. Values are written element by element (the arrays are shared
    /// with other systems, so the references must stay untouched).
    /// </summary>
    public static class P0_Tables
    {
        public const int LevelCount = 11;

        private static bool _vanillaCaptured;
        private static int[] _vanillaInc;
        private static double[] _vanillaIncMilli;
        private static int[] _vanillaPower;
        private static double[] _vanillaPowerRatio;

        public static bool Apply(string reason)
        {
            return Apply(reason, true);
        }

        public static bool Apply(string reason, bool logArrays)
        {
            var log = Plugin.Log;
            if (log == null)
            {
                return false;
            }

            CaptureVanilla(log);

            if (!QualityConfig.Enabled.Value)
            {
                RestoreVanilla(log, reason, logArrays);
                return false;
            }

            if (!CheckArrays(log, reason))
            {
                return false;
            }

            int perPoint = QualityConfig.IncPerPoint.Value;
            int powerPerPoint = QualityConfig.PowerPerPoint.Value;
            if (perPoint < 0)
            {
                perPoint = 0;
            }
            if (powerPerPoint < 0)
            {
                powerPerPoint = 0;
            }

            int max = QualityConfig.MaxLevel;
            int limit = max + 1;
            if (limit > Cargo.incTable.Length)
            {
                limit = Cargo.incTable.Length;
            }
            if (limit > Cargo.powerTable.Length)
            {
                limit = Cargo.powerTable.Length;
            }

            for (int i = 0; i < limit; i++)
            {
                Cargo.incTable[i] = perPoint * i;
                Cargo.incTableMilli[i] = perPoint * 0.001 * i;
                Cargo.powerTable[i] = powerPerPoint * i;
                Cargo.powerTableRatio[i] = 1.0 + (powerPerPoint * i) / 1000.0;
            }

            log.LogInfo("[QSystem][tables] applied (" + reason + "): perPoint=" + perPoint
                + " powerPerPoint=" + powerPerPoint + " levels=0.." + (limit - 1));
            if (logArrays)
            {
                LogArrays(log);
            }
            return true;
        }

        /// <summary>
        /// Keep a copy of the vanilla tables before the first write so that enabled=false can
        /// restore exact vanilla numbers at runtime (T5 regression item 9).
        /// </summary>
        private static void CaptureVanilla(BepInEx.Logging.ManualLogSource log)
        {
            if (_vanillaCaptured)
            {
                return;
            }
            if (!CheckArrays(log, "capture-vanilla"))
            {
                return;
            }

            _vanillaInc = (int[])Cargo.incTable.Clone();
            _vanillaIncMilli = (double[])Cargo.incTableMilli.Clone();
            _vanillaPower = (int[])Cargo.powerTable.Clone();
            _vanillaPowerRatio = (double[])Cargo.powerTableRatio.Clone();
            _vanillaCaptured = true;
            log.LogInfo("[QSystem][tables] vanilla tables cached (inc=" + Format(_vanillaInc) + ")");
        }

        private static void RestoreVanilla(BepInEx.Logging.ManualLogSource log, string reason, bool logArrays)
        {
            if (!_vanillaCaptured)
            {
                log.LogWarning("[QSystem][tables] skipped (" + reason + "): enabled=false but no vanilla cache");
                return;
            }
            if (!CheckArrays(log, reason))
            {
                return;
            }

            CopyBack(Cargo.incTable, _vanillaInc);
            CopyBack(Cargo.incTableMilli, _vanillaIncMilli);
            CopyBack(Cargo.powerTable, _vanillaPower);
            CopyBack(Cargo.powerTableRatio, _vanillaPowerRatio);
            log.LogInfo("[QSystem][tables] disabled (" + reason + "): vanilla tables restored");

            if (logArrays)
            {
                LogArrays(log);
            }
        }

        private static void CopyBack(int[] target, int[] source)
        {
            int length = target.Length < source.Length ? target.Length : source.Length;
            for (int i = 0; i < length; i++)
            {
                target[i] = source[i];
            }
        }

        private static void CopyBack(double[] target, double[] source)
        {
            int length = target.Length < source.Length ? target.Length : source.Length;
            for (int i = 0; i < length; i++)
            {
                target[i] = source[i];
            }
        }

        private static bool CheckArrays(BepInEx.Logging.ManualLogSource log, string reason)
        {
            if (Cargo.incTable == null || Cargo.incTableMilli == null
                || Cargo.powerTable == null || Cargo.powerTableRatio == null)
            {
                log.LogError("[QSystem][tables] FAILED (" + reason + "): a Cargo table is null");
                return false;
            }

            if (Cargo.incTable.Length < LevelCount || Cargo.incTableMilli.Length < LevelCount
                || Cargo.powerTable.Length < LevelCount || Cargo.powerTableRatio.Length < LevelCount)
            {
                log.LogError("[QSystem][tables] FAILED (" + reason + "): table length too small, lengths="
                    + Cargo.incTable.Length + "/" + Cargo.incTableMilli.Length + "/"
                    + Cargo.powerTable.Length + "/" + Cargo.powerTableRatio.Length);
                return false;
            }
            return true;
        }

        public static void LogArrays(BepInEx.Logging.ManualLogSource log)
        {
            if (log == null || Cargo.incTable == null)
            {
                return;
            }

            log.LogInfo("[QSystem][tables] incTable=" + Format(Cargo.incTable));
            log.LogInfo("[QSystem][tables] incTableMilli=" + Format(Cargo.incTableMilli));
            log.LogInfo("[QSystem][tables] powerTable=" + Format(Cargo.powerTable));
            log.LogInfo("[QSystem][tables] powerTableRatio=" + Format(Cargo.powerTableRatio));
        }

        private static string Format(int[] values)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append(values[i].ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string Format(double[] values)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append(values[i].ToString("0.###", CultureInfo.InvariantCulture));
            }
            sb.Append(']');
            return sb.ToString();
        }
    }

    /// <summary>
    /// Re-apply after the game HUD is created, in case another mod or the game itself
    /// restored the vanilla tables. In M0 this is mostly a safety net before a save loads.
    /// </summary>
    [HarmonyPatch(typeof(UIGame), "_OnCreate")]
    internal static class UIGame_OnCreate_Patch
    {
        private static void Postfix()
        {
            try
            {
                P0_Tables.Apply("UIGame._OnCreate", false);
            }
            catch (Exception e)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning("[QSystem][tables] UIGame._OnCreate re-apply failed: " + e.Message);
                }
            }
        }
    }
}
