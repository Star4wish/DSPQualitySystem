using System;
using System.Reflection;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.12.0 (task-16 A4): EM rail ejector and vertical launching silo launch rate scales with the
    /// building's quality points.
    ///
    /// Both components are patched through their transient <c>power</c> argument:
    /// * ejector  asm_full.cs:37815 InternalUpdate(float power, long tick, DysonSwarm, AstroData[],
    ///   AnimData[], int[]) turns power into the firing timer at :37864
    ///   <c>num2 = (int)(power * 10000f * (1f + accTableMilli[incLevel]) + 0.1f)</c>;
    /// * silo     asm_full.cs:61221 InternalUpdate(float power, DysonSphere, AnimData[], int[]) uses
    ///   the same formula at :61242.
    /// Nothing persistent is written: power is a per-tick local computed by FactorySystem, so the
    /// quality bonus never leaks into the save file. Both components carry their own
    /// <c>planetId</c> field (asm_full.cs:37575 / :61053), which is used instead of the ambient env
    /// resolution when it is valid.
    ///
    /// The switch is hard: <c>launcherSpeedBonus=false</c> means neither patch is registered, so the
    /// vanilla IL runs untouched.
    /// </summary>
    internal static class P5_Launcher
    {
        private static bool _armedEjector;
        private static bool _armedSilo;
        private static string _reason;
        private static long _hitsEjector;
        private static long _hitsSilo;
        private static int _logged;

        internal static bool Armed
        {
            get { return _armedEjector || _armedSilo; }
        }

        internal static void Apply(Harmony harmony)
        {
            try
            {
                if (QualityConfig.LauncherSpeedBonus == null || !QualityConfig.LauncherSpeedBonus.Value)
                {
                    Disable("launcherSpeedBonus disabled by config");
                    return;
                }
                if (harmony == null)
                {
                    Disable("harmony instance missing");
                    return;
                }

                TryApplyEjector(harmony);
                TryApplySilo(harmony);

                if (!_armedEjector && !_armedSilo)
                {
                    Disable("no launcher component could be patched");
                    return;
                }
                P5Common.Info("[QSystem][launcher] armed: ejector/silo launch rate bonus (launcherSpeedBonus=True,"
                    + " ejector=" + (_armedEjector ? "on" : "off")
                    + " silo=" + (_armedSilo ? "on" : "off") + ")");
            }
            catch (Exception e)
            {
                Disable(e.ToString());
            }
        }

        private static void TryApplyEjector(Harmony harmony)
        {
            try
            {
                MethodInfo target = typeof(EjectorComponent).GetMethod("InternalUpdate",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[]
                    {
                        typeof(float), typeof(long), typeof(DysonSwarm), typeof(AstroData[]),
                        typeof(AnimData[]), typeof(int[])
                    }, null);
                MethodInfo prefix = typeof(P5_Launcher).GetMethod("EjectorPrefix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (target == null || prefix == null)
                {
                    P5Common.WarnOnce("launcher.ejector.install",
                        "reflection miss (target=" + (target != null) + ", prefix=" + (prefix != null) + ")");
                    return;
                }
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                _armedEjector = true;
                P5Common.Info("[QSystem][launcher] patch registered: EjectorComponent.InternalUpdate"
                    + " prefix=EjectorPrefix (power *= launch-rate factor)");
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("launcher.ejector.install", e.ToString());
            }
        }

        private static void TryApplySilo(Harmony harmony)
        {
            try
            {
                MethodInfo target = typeof(SiloComponent).GetMethod("InternalUpdate",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { typeof(float), typeof(DysonSphere), typeof(AnimData[]), typeof(int[]) }, null);
                MethodInfo prefix = typeof(P5_Launcher).GetMethod("SiloPrefix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (target == null || prefix == null)
                {
                    P5Common.WarnOnce("launcher.silo.install",
                        "reflection miss (target=" + (target != null) + ", prefix=" + (prefix != null) + ")");
                    return;
                }
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                _armedSilo = true;
                P5Common.Info("[QSystem][launcher] patch registered: SiloComponent.InternalUpdate"
                    + " prefix=SiloPrefix (power *= launch-rate factor)");
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("launcher.silo.install", e.ToString());
            }
        }

        private static void Disable(string reason)
        {
            _reason = reason;
            _armedEjector = false;
            _armedSilo = false;
            P5Common.WarnOnce("launcher.install", reason);
            P5Common.Info("[QSystem][launcher] NOT ARMED (vanilla launch rate): " + reason);
        }

        private static void EjectorPrefix(ref EjectorComponent __instance, ref float power)
        {
            if (!_armedEjector || power < 0.1f)
            {
                return;
            }

            try
            {
                bool viaEnv;
                int planetId = ResolvePlanet(__instance.planetId, out viaEnv);
                float factor = QualityData.LauncherSpeedFactor(planetId, __instance.entityId);
                if (factor <= 1f)
                {
                    return;
                }
                power *= factor;
                P5Common.Debug(ref _logged, "[QSystem][launcher] ejector bonus active (planet=" + planetId
                    + " entity=" + __instance.entityId + " factor=" + factor.ToString("F2")
                    + " viaEnv=" + (viaEnv ? "true" : "false") + ")");
                P5Common.Effective(ref _hitsEjector, "launcher-ejector", "");
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("launcher.ejector.prefix", e.ToString());
            }
        }

        private static void SiloPrefix(ref SiloComponent __instance, ref float power)
        {
            if (!_armedSilo || power < 0.1f)
            {
                return;
            }

            try
            {
                bool viaEnv;
                int planetId = ResolvePlanet(__instance.planetId, out viaEnv);
                float factor = QualityData.LauncherSpeedFactor(planetId, __instance.entityId);
                if (factor <= 1f)
                {
                    return;
                }
                power *= factor;
                P5Common.Debug(ref _logged, "[QSystem][launcher] silo bonus active (planet=" + planetId
                    + " entity=" + __instance.entityId + " factor=" + factor.ToString("F2")
                    + " viaEnv=" + (viaEnv ? "true" : "false") + ")");
                P5Common.Effective(ref _hitsSilo, "launcher-silo", "");
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("launcher.silo.prefix", e.ToString());
            }
        }

        /// <summary>Prefer the component's own planetId; fall back to the ambient env when it is not usable.</summary>
        private static int ResolvePlanet(int componentPlanetId, out bool viaEnv)
        {
            if (componentPlanetId > 0)
            {
                viaEnv = false;
                return componentPlanetId;
            }
            return P5Common.ResolvePlanetId(out viaEnv);
        }

        internal static string Describe()
        {
            try
            {
                if (QualityConfig.LauncherSpeedBonus == null || !QualityConfig.LauncherSpeedBonus.Value)
                {
                    return "launcher speed NOT ARMED (vanilla launch rate, reason: launcherSpeedBonus disabled)";
                }
                if (!Armed)
                {
                    return "launcher speed NOT ARMED (vanilla launch rate, reason: "
                        + (_reason ?? "not applied yet") + ")";
                }
                return "launcher speed armed (ejector=" + (_armedEjector ? "on" : "off")
                    + " silo=" + (_armedSilo ? "on" : "off")
                    + "; power *= quality curve; hits ejector=" + _hitsEjector + " silo=" + _hitsSilo + ")";
            }
            catch (Exception)
            {
                return "launcher speed state unavailable";
            }
        }
    }
}
