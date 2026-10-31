using System;
using System.Reflection;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.12.0 (task-16 A1): research lab research speed scales with the building's quality points.
    /// The transient `power` argument of LabComponent.InternalUpdateResearch is multiplied before the
    /// vanilla code turns it into hash progress, so nothing persistent is touched.
    /// </summary>
    internal static class P5_LabSpeed
    {
        private static bool _armed;
        private static string _reason;
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
                if (QualityConfig.LabSpeedBonus == null || !QualityConfig.LabSpeedBonus.Value)
                {
                    Disable("labSpeedBonus disabled by config");
                    return;
                }
                Type[] ps =
                {
                    typeof(float), typeof(float), typeof(int[]), typeof(TechState).MakeByRefType(),
                    typeof(int).MakeByRefType(), typeof(long).MakeByRefType(), typeof(long).MakeByRefType()
                };
                MethodInfo target = typeof(LabComponent).GetMethod("InternalUpdateResearch",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, ps, null);
                if (target == null)
                {
                    Disable("LabComponent.InternalUpdateResearch(float,float,int[],...) not found");
                    return;
                }
                MethodInfo prefix = typeof(P5_LabSpeed).GetMethod("Prefix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (prefix == null)
                {
                    Disable("prefix method missing");
                    return;
                }
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                _armed = true;
                P5Common.Info("[QSystem][lab] patch registered: LabComponent.InternalUpdateResearch "
                    + "prefix=Prefix (power *= quality curve factor)");
                P5Common.Info("[QSystem][lab] armed: research lab research speed bonus (labSpeedBonus=True)");
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
            P5Common.WarnOnce("lab.install", reason);
            P5Common.Info("[QSystem][lab] NOT ARMED (vanilla research speed): " + reason);
        }

        private static void Prefix(ref LabComponent __instance, ref float power)
        {
            if (!_armed || power < 0.1f)
            {
                return;
            }
            try
            {
                if (!P5Common.Enabled || QualityConfig.LabSpeedBonus == null || !QualityConfig.LabSpeedBonus.Value)
                {
                    return;
                }
                bool viaEnv;
                int planetId = P5Common.ResolvePlanetId(out viaEnv);
                float factor = QualityData.LabSpeedFactor(planetId, __instance.entityId);
                if (factor <= 1f)
                {
                    return;
                }
                power *= factor;
                if (viaEnv)
                {
                    _viaEnv++;
                }
                else
                {
                    _fallback++;
                }
                P5Common.Debug(ref _logged, "[QSystem][lab] research speed bonus active (planet=" + planetId
                    + " entity=" + __instance.entityId + " factor=" + factor.ToString("F2")
                    + " viaEnv=" + (viaEnv ? "true" : "false") + ")");
                P5Common.Effective(ref _hits, "lab", " (viaEnv=" + _viaEnv + " fallback=" + _fallback + ")");
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("lab.prefix", e.ToString());
            }
        }

        internal static string Describe()
        {
            try
            {
                if (QualityConfig.LabSpeedBonus == null || !QualityConfig.LabSpeedBonus.Value)
                {
                    return "lab speed NOT ARMED (vanilla research speed, reason: labSpeedBonus disabled)";
                }
                if (!_armed)
                {
                    return "lab speed NOT ARMED (vanilla research speed, reason: "
                        + (_reason ?? "not applied yet") + ")";
                }
                return "lab speed armed (research speed *= quality curve; hits=" + _hits
                    + " viaEnv=" + _viaEnv + " fallback=" + _fallback + ")";
            }
            catch (Exception)
            {
                return "lab speed state unavailable";
            }
        }
    }
}