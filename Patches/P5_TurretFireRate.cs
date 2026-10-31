using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.12.0 (task-16 A2): turret fire rate scales with the turret's building quality points.
    /// The transient `power` argument of every TurretComponent.Shoot_* method is multiplied before
    /// the vanilla rotates-per-frame maths, so more shots are fired without touching any field.
    /// Only the standard turret component is covered; the dark-fog turrets are out of scope.
    /// </summary>
    internal static class P5_TurretFireRate
    {
        private static readonly string[] MethodNames =
        {
            "Shoot_Gauss", "Shoot_Laser", "Shoot_Cannon", "Shoot_Plasma", "Shoot_Missile", "Shoot_LocalPlasma"
        };

        private static bool _armed;
        private static string _reason;
        private static int _patched;
        private static long _hits;
        private static int _logged;

        internal static bool Armed
        {
            get { return _armed; }
        }

        internal static void Apply(Harmony harmony)
        {
            try
            {
                if (QualityConfig.TurretFireRateBonus == null || !QualityConfig.TurretFireRateBonus.Value)
                {
                    Disable("turretFireRateBonus disabled by config");
                    return;
                }
                MethodInfo prefix = typeof(P5_TurretFireRate).GetMethod("Prefix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (prefix == null)
                {
                    Disable("prefix method missing");
                    return;
                }
                var simple = new[]
                {
                    typeof(PlanetFactory), typeof(PrefabDesc), typeof(int[]), typeof(float), typeof(long),
                    typeof(CombatUpgradeData).MakeByRefType()
                };
                var withEnemy = new[]
                {
                    typeof(PlanetFactory), typeof(EnemyData[]), typeof(PrefabDesc), typeof(int[]),
                    typeof(float), typeof(long), typeof(CombatUpgradeData).MakeByRefType()
                };
                foreach (string name in MethodNames)
                {
                    Type[] parameters = name == "Shoot_Plasma" || name == "Shoot_Missile" ? withEnemy : simple;
                    MethodInfo target = typeof(TurretComponent).GetMethod(name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                        parameters, null);
                    if (target == null)
                    {
                        P5Common.WarnOnce("turret." + name, "TurretComponent." + name + " not found");
                        continue;
                    }
                    try
                    {
                        harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                        _patched++;
                    }
                    catch (Exception e)
                    {
                        P5Common.WarnOnce("turret." + name + ".install", e.ToString());
                    }
                }
                if (_patched <= 0)
                {
                    Disable("no TurretComponent.Shoot_* method could be patched");
                    return;
                }
                _armed = true;
                P5Common.Info("[QSystem][turret] patch registered: TurretComponent.Shoot_* prefix=Prefix "
                    + "(power *= fire-rate factor) for " + _patched + "/" + MethodNames.Length + " methods");
                P5Common.Info("[QSystem][turret] armed: turret fire rate bonus (turretFireRateBonus=True)");
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
            P5Common.WarnOnce("turret.install", reason);
            P5Common.Info("[QSystem][turret] NOT ARMED (vanilla fire rate): " + reason);
        }

        private static void Prefix(ref TurretComponent __instance, PlanetFactory factory, ref float power)
        {
            if (!_armed || power < 0.1f)
            {
                return;
            }
            try
            {
                if (!P5Common.Enabled || QualityConfig.TurretFireRateBonus == null
                    || !QualityConfig.TurretFireRateBonus.Value)
                {
                    return;
                }
                int planetId = factory == null ? -1 : factory.planetId;
                float factor = QualityData.TurretFireFactor(planetId, __instance.entityId);
                if (factor <= 1f)
                {
                    return;
                }
                power *= factor;
                P5Common.Debug(ref _logged, "[QSystem][turret] fire rate bonus active (planet=" + planetId
                    + " entity=" + __instance.entityId + " factor=" + factor.ToString("F2") + ")");
                P5Common.Effective(ref _hits, "turret", " (methods=" + _patched + ")");
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("turret.prefix", e.ToString());
            }
        }

        internal static string Describe()
        {
            try
            {
                if (QualityConfig.TurretFireRateBonus == null || !QualityConfig.TurretFireRateBonus.Value)
                {
                    return "turret fire rate NOT ARMED (vanilla, reason: turretFireRateBonus disabled)";
                }
                if (!_armed)
                {
                    return "turret fire rate NOT ARMED (vanilla, reason: "
                        + (_reason ?? "not applied yet") + ")";
                }
                return "turret fire rate armed (power *= quality curve; methods=" + _patched
                    + "/" + MethodNames.Length + ", hits=" + _hits + ")";
            }
            catch (Exception)
            {
                return "turret fire rate state unavailable";
            }
        }
    }
}