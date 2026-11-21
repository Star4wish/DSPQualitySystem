using System;
using System.Reflection;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.20.0 (issue 1): "building through the battlefield analysis base crashes with
    /// System.IndexOutOfRangeException at ConstructionModuleComponent.PreLaunchDrone".
    ///
    /// Root symptom: the base keeps a `ConstructTarget[] buildTargets` cache of the pre-builds it may
    /// send construction drones to. `PreLaunchDrone` trusts that cache and reads
    /// `factory.prebuildPool[buildTargets[0].objectId]` with no bounds check (decompiled
    /// ConstructionModuleComponent.PreLaunchDrone: the only array index in the throttled branch is
    /// that one, and the vanilla stack trace points exactly at it). The cache is only pruned through
    /// `ConstructionSystem.ExcuteDeferredTargetChange`/`RemoveBuildTargetFromModules`, which is
    /// skipped for pre-builds that were already launched or whose owning base/module was not reached
    /// by the deferred list; after a prebuild pool flush (PlanetFactory.FlushPools shrinks the pool
    /// to 256 when the planet unloads with `prebuildCount == 0`) or after any other stale entry, the
    /// first cached target can point outside the array and the whole game frame throws.
    ///
    /// This guard does not change normal behaviour: a prefix validates both caches and drops only the
    /// entries that cannot be used any more (id outside the pool, recycled slot, destroyed prebuild,
    /// still unpaid ghost, or a repair target whose construct stat / entity / combat stat is gone).
    /// Surviving build groups are compacted upwards so `buildTargets[0]` is valid again, and
    /// `buildTargetTotalCount`/`repairTargetTotalCount` are recomputed so the vanilla
    /// `SearchBuildTargets` gate refills the list on the next tick. With a healthy cache the prefix
    /// only walks 120 numbers and changes nothing.
    ///
    /// Every failure path is swallowed (WarnOnce) - a broken guard must never abort a build tick.
    /// </summary>
    internal static class P1_ConstructionGuard
    {
        private const string Tag = "[QSystem][construction] ";
        private const int DebugLogLimit = 8;
        private const int EffectiveLogEvery = 200;

        private static bool _armed;
        private static string _reason;
        private static long _prunedBuild;
        private static long _prunedRepair;
        private static int _debugLogs;

        internal static void Apply(Harmony harmony)
        {
            try
            {
                if (harmony == null)
                {
                    Disable("no harmony instance");
                    return;
                }
                if (!P5Common.Enabled)
                {
                    Disable("quality system disabled");
                    return;
                }

                MethodInfo target = typeof(ConstructionModuleComponent).GetMethod("PreLaunchDrone",
                    BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { typeof(PlanetFactory), typeof(Player) }, null);
                MethodInfo prefix = typeof(P1_ConstructionGuard).GetMethod("PreLaunchDronePrefix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (target == null || prefix == null)
                {
                    Disable("ConstructionModuleComponent.PreLaunchDrone(PlanetFactory,Player) not found");
                    return;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(prefix)
                {
                    priority = Priority.High
                });
                _armed = true;
                P5Common.Info(Tag + "armed: validates/prunes stale build+repair drone targets before "
                    + "PreLaunchDrone (vanilla has no bounds check on prebuildPool[buildTargets[0].objectId])");
            }
            catch (Exception e)
            {
                Disable("install failed: " + e.Message);
                QualityEnv.WarnOnce("construction.install", e);
            }
        }

        /// <summary>
        /// Returns false only when the vanilla body cannot run safely at all (a freed factory); then
        /// there is nothing to launch drones for anyway.
        /// </summary>
        private static bool PreLaunchDronePrefix(ConstructionModuleComponent __instance, PlanetFactory factory)
        {
            if (!_armed || __instance == null)
            {
                return true;
            }

            try
            {
                if (factory == null || factory.prebuildPool == null || factory.entityPool == null)
                {
                    return false;
                }

                int planetId = factory.planet == null ? -1 : factory.planet.id;
                SanitizeBuildTargets(__instance, factory, planetId);
                SanitizeRepairTargets(__instance, factory, planetId);
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("construction.guard", e);
            }

            return true;
        }

        private static void SanitizeBuildTargets(ConstructionModuleComponent module, PlanetFactory factory,
            int planetId)
        {
            ConstructTarget[] targets = module.buildTargets;
            if (targets == null || targets.Length < 4)
            {
                return;
            }

            PrebuildData[] pool = factory.prebuildPool;
            int groups = targets.Length / 4;
            int writeGroup = 0;
            int pruned = 0;

            for (int group = 0; group < groups; group++)
            {
                int b = group << 2;
                bool hasAny = false;
                bool usable = true;

                for (int slot = 0; slot < 4; slot++)
                {
                    int id = targets[b + slot].objectId;
                    if (id == 0)
                    {
                        continue;
                    }
                    hasAny = true;
                    if (!IsUsablePrebuild(pool, id))
                    {
                        usable = false;
                        break;
                    }
                }

                // Vanilla always writes the group's first slot (InsertBuildTarget); a group whose
                // head is empty but whose other slots are not can never be scheduled and is stale.
                if (hasAny && targets[b].objectId == 0)
                {
                    usable = false;
                }

                if (!hasAny)
                {
                    // Empty group: groups are kept compact, so this is the end of the list.
                    break;
                }

                if (!usable)
                {
                    pruned++;
                    if (pruned <= DebugLogLimit)
                    {
                        _debugLogs++;
                        P5Common.Info(Tag + "dropped stale build target group=" + group
                            + " ids=" + targets[b].objectId + "/" + targets[b + 1].objectId + "/"
                            + targets[b + 2].objectId + "/" + targets[b + 3].objectId
                            + " pool=" + pool.Length + " planet=" + planetId);
                    }
                    ClearGroup(targets, b);
                    continue;
                }

                if (writeGroup != group)
                {
                    Array.Copy(targets, b, targets, writeGroup << 2, 4);
                    ClearGroup(targets, b);
                }
                writeGroup++;
            }

            if (pruned > 0)
            {
                module.buildTargetTotalCount = CountNonZero(targets);
                _prunedBuild += pruned;
                if (_prunedBuild <= DebugLogLimit || _prunedBuild % EffectiveLogEvery == 0)
                {
                    P5Common.Info(Tag + "build targets pruned=" + pruned + " total=" + _prunedBuild
                        + " remaining=" + module.buildTargetTotalCount + " planet=" + planetId);
                }
            }
        }

        private static void SanitizeRepairTargets(ConstructionModuleComponent module, PlanetFactory factory,
            int planetId)
        {
            ConstructTargetExtra[] targets = module.repairTargets;
            if (targets == null || targets.Length == 0)
            {
                return;
            }

            ConstructStat[] stats = factory.constructionSystem == null
                || factory.constructionSystem.constructStats == null
                ? null : factory.constructionSystem.constructStats.buffer;
            CombatStat[] combat = factory.skillSystem == null
                || factory.skillSystem.combatStats == null
                ? null : factory.skillSystem.combatStats.buffer;
            EntityData[] entities = factory.entityPool;

            if (stats == null || combat == null || entities == null)
            {
                // Vanilla would dereference these unconditionally; with no repair system there is
                // nothing to repair, so hand it an empty list instead of letting it throw.
                Array.Clear(targets, 0, targets.Length);
                module.repairTargetTotalCount = 0;
                return;
            }

            int write = 0;
            int pruned = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                int id = targets[i].objectId;
                if (id == 0)
                {
                    break;
                }
                if (!IsUsableRepairTarget(stats, combat, entities, id))
                {
                    pruned++;
                    if (pruned <= DebugLogLimit)
                    {
                        _debugLogs++;
                        P5Common.Info(Tag + "dropped stale repair target id=" + id
                            + " stats=" + stats.Length + " entities=" + entities.Length
                            + " planet=" + planetId);
                    }
                    continue;
                }
                if (write != i)
                {
                    targets[write] = targets[i];
                }
                write++;
            }

            for (int i = write; i < targets.Length; i++)
            {
                targets[i].objectId = 0;
                targets[i].value = 0f;
                targets[i].value2 = 0f;
            }

            if (pruned > 0)
            {
                module.repairTargetTotalCount = write;
                _prunedRepair += pruned;
                if (_prunedRepair <= DebugLogLimit || _prunedRepair % EffectiveLogEvery == 0)
                {
                    P5Common.Info(Tag + "repair targets pruned=" + pruned + " total=" + _prunedRepair
                        + " remaining=" + write + " planet=" + planetId);
                }
            }
        }

        /// <summary>
        /// A build target is only usable when the pool slot still holds that exact prebuild, it is a
        /// live (not destroyed) ghost and it is fully paid (`itemRequired == 0`) - the same three
        /// conditions vanilla `SearchBuildTargets` uses before inserting a target.
        /// </summary>
        private static bool IsUsablePrebuild(PrebuildData[] pool, int id)
        {
            if (pool == null || id <= 0 || id >= pool.Length)
            {
                return false;
            }
            PrebuildData prebuild = pool[id];
            return prebuild.id == id && !prebuild.isDestroyed && prebuild.itemRequired == 0;
        }

        private static bool IsUsableRepairTarget(ConstructStat[] stats, CombatStat[] combat,
            EntityData[] entities, int id)
        {
            if (id <= 0 || id >= stats.Length)
            {
                return false;
            }
            ConstructStat stat = stats[id];
            if (stat.id != id)
            {
                return false;
            }
            int entityId = stat.entityId;
            if (entityId <= 0 || entityId >= entities.Length)
            {
                return false;
            }
            EntityData entity = entities[entityId];
            if (entity.id != entityId)
            {
                return false;
            }
            int combatStatId = entity.combatStatId;
            return combatStatId >= 0 && combatStatId < combat.Length;
        }

        private static void ClearGroup(ConstructTarget[] targets, int baseIndex)
        {
            for (int i = 0; i < 4; i++)
            {
                targets[baseIndex + i].objectId = 0;
                targets[baseIndex + i].value = 0f;
            }
        }

        private static int CountNonZero(ConstructTarget[] targets)
        {
            int count = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i].objectId != 0)
                {
                    count++;
                }
            }
            return count;
        }

        internal static string Describe()
        {
            return "construction guard armed=" + _armed + " reason=" + (_reason ?? "-")
                + " prunedBuild=" + _prunedBuild + " prunedRepair=" + _prunedRepair;
        }

        internal static void ResetState()
        {
            _prunedBuild = 0;
            _prunedRepair = 0;
            _debugLogs = 0;
        }

        private static void Disable(string reason)
        {
            _armed = false;
            _reason = reason;
            P5Common.Info(Tag + "NOT ARMED (vanilla PreLaunchDrone keeps its unchecked target cache): "
                + reason);
        }
    }
}
