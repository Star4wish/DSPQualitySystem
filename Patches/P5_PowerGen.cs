using System;
using System.Reflection;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.12.0 (task-16 A3): power generator output scales with building quality points. The five
    /// EnergyCap_* methods return the per-tick capacity into the transient `capacityCurrentTick`
    /// field (never serialized), so the postfix raises both the local copy and the returned value.
    /// Fuel generators burn proportionally more fuel and the gamma receiver produces proportionally
    /// more, which are documented double-benefit behaviours rather than bugs.
    /// </summary>
    internal static class P5_PowerGen
    {
        private sealed class CapTarget
        {
            public string Name;
            public Type[] Parameters;
        }

        private static readonly CapTarget[] Targets =
        {
            new CapTarget { Name = "EnergyCap_Fuel", Parameters = Type.EmptyTypes },
            new CapTarget { Name = "EnergyCap_Wind", Parameters = new[] { typeof(float) } },
            new CapTarget { Name = "EnergyCap_PV", Parameters = new[] { typeof(float), typeof(float), typeof(float), typeof(float) } },
            new CapTarget { Name = "EnergyCap_Gamma", Parameters = new[] { typeof(float) } },
            new CapTarget { Name = "EnergyCap_GTH", Parameters = Type.EmptyTypes }
        };

        private static bool _armed;
        private static string _reason;
        private static int _patched;
        private static long _hits;
        private static long _viaEnv;
        private static long _fallback;
        private static int _logged;

        internal static bool Armed
        {
            get { return _armed; }
        }

        internal static void Apply(Harmony harmony)
        {
            try
            {
                if (QualityConfig.PowerGenBonus == null || !QualityConfig.PowerGenBonus.Value)
                {
                    Disable("powerGenBonus disabled by config");
                    return;
                }
                MethodInfo postfix = typeof(P5_PowerGen).GetMethod("Postfix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (postfix == null)
                {
                    Disable("postfix method missing");
                    return;
                }
                foreach (CapTarget target in Targets)
                {
                    MethodInfo method = typeof(PowerGeneratorComponent).GetMethod(target.Name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                        target.Parameters, null);
                    if (method == null)
                    {
                        P5Common.WarnOnce("powergen." + target.Name,
                            "PowerGeneratorComponent." + target.Name + " not found");
                        continue;
                    }
                    try
                    {
                        harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                        _patched++;
                    }
                    catch (Exception e)
                    {
                        P5Common.WarnOnce("powergen." + target.Name + ".install", e.ToString());
                    }
                }
                if (_patched <= 0)
                {
                    Disable("no PowerGeneratorComponent.EnergyCap_* method could be patched");
                    return;
                }
                _armed = true;
                P5Common.Info("[QSystem][powergen] patch registered: PowerGeneratorComponent.EnergyCap_* "
                    + "postfix=Postfix (capacity *= output factor) for " + _patched + "/" + Targets.Length + " methods");
                P5Common.Info("[QSystem][powergen] armed: generator output bonus (powerGenBonus=True)");
            }
            catch (Exception e)
            {
                Disable(e.ToString());
            }
        }

        private static void Disable(string reason)
        {
            _reason = reason;
            _armed = false;
            P5Common.WarnOnce("powergen.install", reason);
            P5Common.Info("[QSystem][powergen] NOT ARMED (vanilla output): " + reason);
        }

        private static void Postfix(ref PowerGeneratorComponent __instance, ref long __result)
        {
            if (!_armed || __result <= 0)
            {
                return;
            }
            try
            {
                if (!P5Common.Enabled || QualityConfig.PowerGenBonus == null || !QualityConfig.PowerGenBonus.Value)
                {
                    return;
                }
                bool viaEnv;
                int planetId = P5Common.ResolvePlanetId(out viaEnv);
                float factor = QualityData.PowerGenFactor(planetId, __instance.entityId);
                if (factor <= 1f)
                {
                    return;
                }
                long raised = (long)(__result * (double)factor);
                if (raised <= __result)
                {
                    return;
                }
                __result = raised;
                __instance.capacityCurrentTick = raised;
                if (viaEnv)
                {
                    _viaEnv++;
                }
                else
                {
                    _fallback++;
                }
                P5Common.Debug(ref _logged, "[QSystem][powergen] output bonus active (planet=" + planetId
                    + " entity=" + __instance.entityId + " factor=" + factor.ToString("F2")
                    + " capacity=" + raised + ")");
                P5Common.Effective(ref _hits, "powergen",
                    " (methods=" + _patched + " viaEnv=" + _viaEnv + " fallback=" + _fallback + ")");
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("powergen.postfix", e.ToString());
            }
        }

        internal static string Describe()
        {
            try
            {
                if (QualityConfig.PowerGenBonus == null || !QualityConfig.PowerGenBonus.Value)
                {
                    return "powergen output NOT ARMED (vanilla, reason: powerGenBonus disabled)";
                }
                if (!_armed)
                {
                    return "powergen output NOT ARMED (vanilla, reason: " + (_reason ?? "not applied yet") + ")";
                }
                return "powergen output armed (capacity *= quality curve; methods=" + _patched + "/"
                    + Targets.Length + ", hits=" + _hits + ")";
            }
            catch (Exception)
            {
                return "powergen output state unavailable";
            }
        }
    }
}