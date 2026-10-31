using System;
using System.Reflection;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// P3 stage 2a (v0.11.0): building quality raises the logistics-station ENERGY CHARGE CAP,
    /// implemented as a Postfix on <c>StationComponent.SetPCState(PowerConsumerComponent[])</c>
    /// (asm_full.cs:75035-75051) that scales both the derived per-tick request
    /// (<c>energyPerTick</c>) and the power-grid demand (<c>pcPool[pcId].requiredEnergy</c>).
    ///
    /// Why not the old power-prefix (retired here): <c>energy += (int)(energyPerTick * power)</c>
    /// (InternalTickLocal, asm:75192) means scaling the transient <c>power</c> argument and scaling
    /// <c>energyPerTick</c> produce the exact same product, so the old approach only "took more
    /// power while reporting the vanilla demand". Raising energyPerTick *and* requiredEnergy is the
    /// honest form: with grid supply the station really charges factor-times faster; with a
    /// shortage the grid distributes by its own ratio (no hidden over-draw bookkeeping).
    ///
    /// Save impact: <c>energy</c>/<c>energyPerTick</c>/<c>energyMax</c> (Export asm:77745-77747,
    /// Import :77881-77883) and <c>requiredEnergy</c> (Export :58851, Import :58874) are
    /// serialized, but every tick <c>SetPCState</c> recomputes energyPerTick and requiredEnergy.
    /// The Postfix value is therefore transient (one tick at most): if the game saves in the same
    /// frame, the load restores a slightly larger energyPerTick and the first tick charges at most
    /// one tick worth more energy before it is recomputed. No schema change, self-correcting.
    ///
    /// Registration is manual (like the other P3/P4 patches) and strictly validated before the
    /// patch is applied: target must exist, be an instance method returning void with exactly one
    /// <c>PowerConsumerComponent[] pcPool</c> parameter, and the postfix must have the exact
    /// <c>(ref StationComponent __instance, PowerConsumerComponent[] pcPool)</c> shape. On any
    /// mismatch the whole feature stays disabled (vanilla charge) and only one warning is logged.
    ///
    /// <c>chargeBonus=false</c> is a hard switch: the postfix returns before any counter/log work.
    /// The design statement: faster charge = more power per unit time, total energy unchanged,
    /// power draw per unit time higher -- never a "power saving" feature.
    /// </summary>
    internal static class P3_StationCharge
    {
        private const string TargetTypeName = "StationComponent";
        private const string TargetMethodName = "SetPCState";
        private const string PostfixMethodName = "StationChargeCapPostfix";
        private const string PoolParamName = "pcPool";
        private const int EffectiveLogEvery = 200;
        private const int DebugLogLimit = 20;

        private static bool _armed;
        private static bool _warned;
        private static string _reason;
        private static long _effective;
        private static int _debugLogged;

        /// <summary>True only after the postfix was validated and registered.</summary>
        internal static bool Armed
        {
            get { return _armed; }
        }

        /// <summary>
        /// Registers the charge-cap postfix. Called once from Plugin.Awake after PatchAll; the
        /// six-argument <c>Harmony.Patch</c> overload exists on both the build reference
        /// (HarmonyX 2.7.0) and the game runtime (0Harmony 2.5.5), so manual registration binds the
        /// same way the attribute patches do.
        /// </summary>
        internal static void Apply(Harmony harmony)
        {
            try
            {
                if (_armed)
                {
                    return;
                }
                if (harmony == null)
                {
                    _reason = "harmony instance missing";
                    return;
                }
                if (QualityConfig.ChargeBonus == null || !QualityConfig.ChargeBonus.Value)
                {
                    LogDisabled();
                    _reason = "chargeBonus disabled by config";
                    return;
                }
                TryInstall(harmony);
                if (!_armed && _reason == null)
                {
                    _reason = "charge cap postfix not installed (see warnings)";
                }
            }
            catch (Exception e)
            {
                _reason = "Apply failed: " + e.GetType().Name;
                WarnOnce("charge.apply", e.ToString());
            }
        }

        private static void TryInstall(Harmony harmony)
        {
            try
            {
                MethodInfo target = typeof(StationComponent).GetMethod(
                    TargetMethodName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { typeof(PowerConsumerComponent[]) }, null);
                MethodInfo postfix = typeof(P3_StationCharge).GetMethod(
                    PostfixMethodName, BindingFlags.NonPublic | BindingFlags.Static);
                if (target == null || postfix == null)
                {
                    WarnOnce("charge.install",
                        "reflection miss (target=" + (target != null) + ", postfix=" + (postfix != null) + ")");
                    return;
                }

                // 1) Target: instance method, returns void, exactly one PowerConsumerComponent[]
                //    parameter named pcPool (Harmony binds the postfix argument by that name).
                if (target.ReturnType != typeof(void))
                {
                    WarnOnce("charge.install",
                        "target return type is " + target.ReturnType.Name + ", expected Void");
                    return;
                }
                ParameterInfo[] targetParams = target.GetParameters();
                if (targetParams.Length != 1
                    || targetParams[0].ParameterType != typeof(PowerConsumerComponent[])
                    || targetParams[0].Name != PoolParamName)
                {
                    WarnOnce("charge.install",
                        "target signature mismatch (expected void " + TargetTypeName + "." + TargetMethodName
                        + "(PowerConsumerComponent[] " + PoolParamName + "))");
                    return;
                }

                // 2) Postfix: exact (ref StationComponent __instance, PowerConsumerComponent[] pcPool).
                ParameterInfo[] postfixParams = postfix.GetParameters();
                bool shapeOk = postfixParams.Length == 2
                    && postfixParams[0].Name == "__instance"
                    && postfixParams[0].ParameterType == typeof(StationComponent).MakeByRefType()
                    && postfixParams[1].Name == PoolParamName
                    && postfixParams[1].ParameterType == typeof(PowerConsumerComponent[]);
                if (!shapeOk)
                {
                    WarnOnce("charge.install",
                        "postfix signature mismatch (expected (ref StationComponent __instance, "
                        + "PowerConsumerComponent[] " + PoolParamName + "))");
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                _armed = true;
                LogInfo("[QSystem][charge] patch registered: " + TargetTypeName + "." + TargetMethodName
                    + " postfix=" + PostfixMethodName
                    + " (energyPerTick + pcPool.requiredEnergy; power path retired in v0.11.0)");
                LogInfo("[QSystem][charge] armed: station charge cap bonus (mode="
                    + (QualityConfig.SpeedBonusIsCurve ? "curve" : "linear")
                    + " chargePerPoint=" + QualityConfig.ChargePerPointClamped.ToString("F1")
                    + " maxChargeBonus=" + QualityConfig.MaxChargeBonusClamped.ToString("F1") + "%)");
            }
            catch (Exception e)
            {
                WarnOnce("charge.install", e.ToString());
            }
        }

        /// <summary>
        /// Raises <c>energyPerTick</c> and <c>requiredEnergy</c> by the quality factor. Every guard
        /// runs BEFORE the first write: invalid ids / out-of-range pcId / full buffer / zero
        /// request / zero factor leave the vanilla values untouched. If anything throws, nothing
        /// was written yet, so the vanilla charge keeps running.
        /// </summary>
        private static void StationChargeCapPostfix(ref StationComponent __instance,
            PowerConsumerComponent[] pcPool)
        {
            // Hard switch first: disabled means no counters, no debug lines, no logging at all.
            if (QualityConfig.ChargeBonus == null || !QualityConfig.ChargeBonus.Value)
            {
                return;
            }
            if (!_armed)
            {
                return;
            }
            if (QualityConfig.Enabled == null || !QualityConfig.Enabled.Value)
            {
                return;
            }
            try
            {
                if (pcPool == null)
                {
                    return;
                }
                int pcId = __instance.pcId;
                if (pcId < 0 || pcId >= pcPool.Length)
                {
                    return;
                }
                long energy = __instance.energy;
                long energyMax = __instance.energyMax;
                long energyPerTick = __instance.energyPerTick;
                // Full (or one unit from full) must stay vanilla: vanilla already falls back to
                // idleEnergyPerTick, and raising the demand of a full station would be a lie.
                if (energyMax <= 0 || energy >= energyMax - 1)
                {
                    return;
                }
                if (energyPerTick <= 0)
                {
                    return;
                }
                long requiredEnergy = pcPool[pcId].requiredEnergy;
                if (requiredEnergy <= 0)
                {
                    return;
                }

                int planetId = __instance.planetId;
                if (planetId < 0)
                {
                    return;
                }
                int entityId = __instance.entityId;
                if (entityId < 0)
                {
                    return;
                }
                int level = QualityData.GetBuildingInc(planetId, entityId);
                long newValue = QualityMath.ChargeCapFrom(
                    energyPerTick, energy, energyMax, level, QualityConfig.SpeedBonusIsCurve,
                    QualityConfig.ChargePerPointClamped, QualityConfig.MaxChargeBonusClamped);
                if (newValue <= 0 || newValue <= energyPerTick)
                {
                    // 0 points / factor <= 1 / anything that would not raise the cap: leave vanilla.
                    return;
                }

                // All validation and arithmetic is done; only now touch the two fields.
                __instance.energyPerTick = newValue;
                pcPool[pcId].requiredEnergy = newValue;

                float factor = (float)newValue / (float)energyPerTick;
                _effective++;
                LogDebug(entityId, level, factor, energyPerTick, newValue, requiredEnergy);
                if (_effective % EffectiveLogEvery == 0)
                {
                    LogEffective(_effective);
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("charge.cap", e);
            }
        }

        /// <summary>Visible state for the startup log / [Audit] block.</summary>
        internal static string Describe()
        {
            try
            {
                if (QualityConfig.ChargeBonus == null || !QualityConfig.ChargeBonus.Value)
                {
                    return "station charge cap bonus NOT ARMED (vanilla charge, "
                        + "reason: chargeBonus disabled by config)";
                }
                if (QualityConfig.Enabled == null || !QualityConfig.Enabled.Value)
                {
                    return "station charge cap bonus NOT ARMED (vanilla charge, "
                        + "reason: master enabled=false)";
                }
                if (!_armed)
                {
                    return "station charge cap bonus NOT ARMED (vanilla charge, reason: "
                        + (_reason ?? "not applied yet") + ")";
                }
                return "station charge cap bonus armed (mode="
                    + (QualityConfig.SpeedBonusIsCurve ? "curve" : "linear")
                    + " chargePerPoint=" + QualityConfig.ChargePerPointClamped.ToString("F1")
                    + " cap=" + QualityConfig.MaxChargeBonusClamped.ToString("F1") + "%"
                    + "; cap effective=" + _effective
                    + " env=none [postfix on " + TargetTypeName + "." + TargetMethodName
                    + ": energyPerTick & pcPool.requiredEnergy; power path retired in v0.11.0])";
            }
            catch (Exception)
            {
                return "station charge cap state unavailable";
            }
        }

        private static void LogDebug(int entityId, int level, float factor, long before, long after,
            long requiredBefore)
        {
            try
            {
                if (QualityConfig.DebugProbes == null || !QualityConfig.DebugProbes.Value
                    || _debugLogged >= DebugLogLimit)
                {
                    return;
                }
                _debugLogged++;
                var log = Plugin.Log;
                if (log != null)
                {
                    log.LogInfo("[QSystem][charge] cap eid=" + entityId + " level=" + level
                        + " factor=" + factor.ToString("F2") + "x energyPerTick=" + before
                        + "->" + after + " requiredEnergy=" + requiredBefore + "->" + after);
                }
            }
            catch (Exception)
            {
                // diagnostics must never break the game
            }
        }

        private static void LogEffective(long effective)
        {
            try
            {
                var log = Plugin.Log;
                if (log != null)
                {
                    log.LogInfo("[QSystem][charge] cap effective=" + effective
                        + " (postfix on " + TargetTypeName + "." + TargetMethodName + ")");
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
                    log.LogInfo("[QSystem][charge] station charge cap bonus disabled by config "
                        + "(vanilla charge)");
                }
            }
            catch (Exception)
            {
                // ignore
            }
        }

        private static void LogInfo(string message)
        {
            try
            {
                var log = Plugin.Log;
                if (log != null)
                {
                    log.LogInfo(message);
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
                if (_warned)
                {
                    return;
                }
                _warned = true;
                var log = Plugin.Log;
                if (log != null)
                {
                    log.LogWarning("[QSystem][charge] cap " + key + " not armed, vanilla charge: " + detail);
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
