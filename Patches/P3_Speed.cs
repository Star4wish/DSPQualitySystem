using System;
using System.Reflection;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// P3 stage 1 (v0.6.0): building quality speeds up manufacturing / mining by scaling the
    /// transient <c>power</c> argument of the per-tick update, i.e.
    /// <c>progress += (int)(power * speedOverride)</c> (assembler IL 0x0576) and the four
    /// <c>time += ... * power ...</c> increments of the miner.
    ///
    /// Why not touch the speed fields: <c>AssemblerComponent.speed/speedOverride/extraSpeed</c> and
    /// <c>MinerComponent.speed/speedDamper</c> are serialized save fields (Export/Import) and the
    /// assembler recomputes speedOverride from its recipe every cycle, so writing them would leak
    /// into saves and be overwritten anyway. <c>power</c> is a temporary local of each tick: static,
    /// zero save impact, exactly one read per progression formula.
    ///
    /// Registration is done manually in <see cref="Apply"/> instead of via attributes: if a target
    /// or prefix cannot be found on this game build, only that one target is skipped (and logged),
    /// the rest of PatchAll stays intact. Because the assembler method already has a first prefix
    /// from P0_Assembler (quality roll), the newer prefix is pinned to <see cref="Priority.Low"/>
    /// (= [HarmonyPriority(Priority.Low)]), i.e. it runs after the quality prefix; the two are
    /// independent (quality reads levels, this one only rescales power) but the order is explicit.
    ///
    /// <c>speedBonus=false</c> is a hard switch: both prefixes return before any counter/log work,
    /// so a live problem can be neutralised from the cfg without swapping the DLL.
    /// </summary>
    internal static class P3_Speed
    {
        private const float PowerGate = 0.1f;
        private const int EffectiveLogEvery = 200;
        private const int DebugLogLimit = 20;

        internal static bool ArmedAssembler;
        internal static bool ArmedMiner;

        // Best-effort diagnostics counters (tiny races between parallel worker threads are fine).
        private static long _assemblerEffective;
        private static long _assemblerViaEnv;
        private static long _assemblerFallback;
        private static long _minerEffective;
        private static long _minerViaEnv;
        private static long _minerFallback;
        private static int _assemblerDebugLogged;
        private static int _minerDebugLogged;
        private static string _reason;

        /// <summary>
        /// Registers both prefixes on the already-patched components. Called once from Plugin.Awake
        /// after PatchAll; on HarmonyX 2.5.5/2.7.0 the six-argument <c>Harmony.Patch</c> overload
        /// (MethodBase + five HarmonyMethod slots) exists on both the build reference and the game's
        /// 0Harmony.dll, so this binds the same way the attribute patches do.
        /// </summary>
        internal static void Apply(Harmony harmony)
        {
            try
            {
                if (harmony == null)
                {
                    _reason = "harmony instance missing";
                    return;
                }
                if (QualityConfig.SpeedBonus == null || !QualityConfig.SpeedBonus.Value)
                {
                    LogDisabled();
                    _reason = "speedBonus disabled by config";
                    return;
                }

                TryApplyAssembler(harmony);
                TryApplyMiner(harmony);

                if (!ArmedAssembler && !ArmedMiner)
                {
                    _reason = "patch registration failed (see warnings)";
                }
            }
            catch (Exception e)
            {
                _reason = "Apply failed: " + e.GetType().Name;
                WarnOnce("speed.apply", e.ToString());
            }
        }

        private static void TryApplyAssembler(Harmony harmony)
        {
            try
            {
                MethodInfo target = typeof(AssemblerComponent).GetMethod(
                    "InternalUpdate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { typeof(float), typeof(int[]), typeof(int[]) }, null);
                MethodInfo prefix = typeof(P3_Speed).GetMethod(
                    "AssemblerPrefix", BindingFlags.NonPublic | BindingFlags.Static);
                if (target == null || prefix == null)
                {
                    WarnOnce("speed.assembler.install",
                        "reflection miss (target=" + (target != null) + ", prefix=" + (prefix != null) + ")");
                    return;
                }
                harmony.Patch(target, prefix: new HarmonyMethod(prefix) { priority = Priority.Low });
                ArmedAssembler = true;
            }
            catch (Exception e)
            {
                WarnOnce("speed.assembler.install", e.ToString());
            }
        }

        private static void TryApplyMiner(Harmony harmony)
        {
            try
            {
                MethodInfo target = typeof(MinerComponent).GetMethod(
                    "InternalUpdate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[]
                    {
                        typeof(PlanetFactory), typeof(VeinData[]), typeof(float), typeof(float), typeof(float),
                        typeof(int[])
                    }, null);
                MethodInfo prefix = typeof(P3_Speed).GetMethod(
                    "MinerPrefix", BindingFlags.NonPublic | BindingFlags.Static);
                if (target == null || prefix == null)
                {
                    WarnOnce("speed.miner.install",
                        "reflection miss (target=" + (target != null) + ", prefix=" + (prefix != null) + ")");
                    return;
                }
                harmony.Patch(target, prefix: new HarmonyMethod(prefix) { priority = Priority.Low });
                ArmedMiner = true;
            }
            catch (Exception e)
            {
                WarnOnce("speed.miner.install", e.ToString());
            }
        }

        /// <summary>
        /// Runs after P0_Assembler's prefix (pinned with Priority.Low) and only rescales the
        /// transient power argument. Quality roll logic does not read power, so the two are
        /// order-independent, but the order is explicit rather than relying on defaults.
        /// </summary>
        [HarmonyPriority(Priority.Low)]
        private static void AssemblerPrefix(ref AssemblerComponent __instance, ref float power)
        {
            // Hard switch first: disabled means no counters, no debug lines, no logging at all.
            if (QualityConfig.SpeedBonus == null || !QualityConfig.SpeedBonus.Value)
            {
                return;
            }
            if (power < PowerGate)
            {
                return;
            }
            try
            {
                int planetId = QualityEnv.CurrentPlanetId;
                if (planetId <= 0)
                {
                    return;
                }
                int entityId = __instance.entityId;
                if (entityId < 0)
                {
                    return;
                }
                int level = QualityData.GetBuildingInc(planetId, entityId);
                float factor = QualityMath.SpeedFactorFor(
                    level, QualityConfig.SpeedBonusIsCurve,
                    QualityConfig.SpeedPerPointClamped, QualityConfig.MaxSpeedBonusClamped);
                if (factor <= 1f)
                {
                    return;
                }

                float before = power;
                power = before * factor;

                _assemblerEffective++;
                if (QualityEnv.AmbientPlanetId > 0)
                {
                    _assemblerViaEnv++;
                }
                else
                {
                    _assemblerFallback++;
                }
                LogDebug("assembler", ref _assemblerDebugLogged, entityId, level, factor, before, power);
                if (_assemblerEffective % EffectiveLogEvery == 0)
                {
                    LogEffective("assembler", _assemblerEffective, _assemblerViaEnv, _assemblerFallback);
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("speed.assembler", e);
            }
        }

        /// <summary>
        /// Miner counterpart. The factory argument is available, so the planet id never depends on
        /// the ambient scope here; the source is still counted to keep the two log lines comparable.
        /// </summary>
        [HarmonyPriority(Priority.Low)]
        private static void MinerPrefix(ref MinerComponent __instance, PlanetFactory factory, ref float power)
        {
            if (QualityConfig.SpeedBonus == null || !QualityConfig.SpeedBonus.Value)
            {
                return;
            }
            if (power < PowerGate)
            {
                return;
            }
            try
            {
                int planetId = factory != null ? factory.planetId : QualityEnv.CurrentPlanetId;
                if (planetId <= 0)
                {
                    return;
                }
                int entityId = __instance.entityId;
                if (entityId < 0)
                {
                    return;
                }
                int level = QualityData.GetBuildingInc(planetId, entityId);
                float factor = QualityMath.SpeedFactorFor(
                    level, QualityConfig.SpeedBonusIsCurve,
                    QualityConfig.SpeedPerPointClamped, QualityConfig.MaxSpeedBonusClamped);
                if (factor <= 1f)
                {
                    return;
                }

                float before = power;
                power = before * factor;

                _minerEffective++;
                if (QualityEnv.AmbientPlanetId > 0)
                {
                    _minerViaEnv++;
                }
                else
                {
                    _minerFallback++;
                }
                LogDebug("miner", ref _minerDebugLogged, entityId, level, factor, before, power);
                if (_minerEffective % EffectiveLogEvery == 0)
                {
                    LogEffective("miner", _minerEffective, _minerViaEnv, _minerFallback);
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("speed.miner", e);
            }
        }

        /// <summary>Visible state for the startup log / [Audit] block.</summary>
        internal static string Describe()
        {
            try
            {
                if (QualityConfig.SpeedBonus == null || !QualityConfig.SpeedBonus.Value)
                {
                    return "speed bonus NOT ARMED (vanilla speed, reason: speedBonus disabled by config)";
                }
                if (!ArmedAssembler && !ArmedMiner)
                {
                    return "speed bonus NOT ARMED (vanilla speed, reason: " +
                           (_reason ?? "not applied yet") + ")";
                }
                return "speed bonus armed (assembler=" + (ArmedAssembler ? "on" : "off") +
                       " miner=" + (ArmedMiner ? "on" : "off") +
                       " mode=" + (QualityConfig.SpeedBonusIsCurve ? "curve" : "linear") +
                       " speedPerPoint=" + QualityConfig.SpeedPerPointClamped.ToString("F1") +
                       " cap=" + QualityConfig.MaxSpeedBonusClamped.ToString("F1") + "%" +
                       "; assembler effective=" + _assemblerEffective +
                       " viaEnv=" + _assemblerViaEnv +
                       " fallback=" + _assemblerFallback +
                       ", miner effective=" + _minerEffective +
                       " viaEnv=" + _minerViaEnv +
                       " fallback=" + _minerFallback + ")";
            }
            catch (Exception)
            {
                return "speed bonus state unavailable";
            }
        }

        private static void LogDebug(string target, ref int logged, int entityId, int level, float factor,
            float before, float after)
        {
            try
            {
                if (QualityConfig.DebugProbes == null || !QualityConfig.DebugProbes.Value ||
                    logged >= DebugLogLimit)
                {
                    return;
                }
                logged++;
                var log = Plugin.Log;
                if (log != null)
                {
                    log.LogInfo("[QSystem][speed] " + target + " eid=" + entityId + " level=" + level +
                                " factor=" + factor.ToString("F2") + "x power=" + before.ToString("F2") +
                                "->" + after.ToString("F2"));
                }
            }
            catch (Exception)
            {
                // diagnostics must never break the game
            }
        }

        private static void LogEffective(string target, long effective, long viaEnv, long fallback)
        {
            try
            {
                var log = Plugin.Log;
                if (log != null)
                {
                    log.LogInfo("[QSystem][speed] " + target + " effective=" + effective +
                                " (viaEnv=" + viaEnv + " fallback=" + fallback + ")");
                }
            }
            catch (Exception)
            {
                // never let diagnostics break the game
            }
        }

        private static void LogDisabled()
        {
            try
            {
                var log = Plugin.Log;
                if (log != null)
                {
                    log.LogInfo("[QSystem][speed] speed bonus disabled by config (vanilla speed)");
                }
            }
            catch (Exception)
            {
                // ignore
            }
        }

        private static void WarnOnce(string key, string detail)
        {
            try
            {
                var log = Plugin.Log;
                if (log != null)
                {
                    log.LogWarning("[QSystem][speed] " + key + " not armed, vanilla speed: " + detail);
                }
                if (_reason == null)
                {
                    _reason = key;
                }
            }
            catch (Exception)
            {
                // ignore
            }
        }
    }
}
