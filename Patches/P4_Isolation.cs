using System;
using System.Reflection;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.10.0 (P4), v0.11.3 containers: building-quality isolation.
    ///
    /// Vanilla StorageComponent.AddItemStacked (asm_full.cs:80012-80122) merges purely by itemId,
    /// so two building stacks with different quality points (grids[].inc) end up in one grid and
    /// their points are added together. This patch installs a Prefix on that single method; only
    /// when QualityRules.ShouldIsolateStorage accepts the storage (the player inventory, or a
    /// plain Default storage container when isolateStorageContainers is on) AND the item is a
    /// building (ItemProto.CanBuild) does it run an isolation-aware copy of the add loop
    /// (QualityData.AddIsolatedStacked), where stacks with a different inc are kept in separate
    /// grids. Every other case returns true and the vanilla body runs unchanged, so behaviour is
    /// bit-identical outside the eligible storages.
    ///
    /// Registration is manual (harmony.Patch) like the other P3/P4 hooks: a binding failure is
    /// isolated to this feature instead of aborting PatchAll, and the startup count stays clean.
    /// On any failure the feature is fully disabled (never half-armed) and warns once; the vanilla
    /// merge is the fallback.
    /// </summary>
    internal static class P4_Isolation
    {
        private static bool _armed;
        private static bool _warned;
        private static string _reason = "not applied yet";
        private static long _splits;
        private static int _buildingItems;

        public static bool Armed
        {
            get { return _armed; }
        }

        public static void Apply(Harmony harmony)
        {
            try
            {
                if (harmony == null)
                {
                    _reason = "harmony instance is null";
                    WarnOnce("isolation.apply", _reason);
                    return;
                }
                if (QualityConfig.IsolateBuildingStacks == null || !QualityConfig.IsolateBuildingStacks.Value)
                {
                    _reason = "isolateBuildingStacks disabled by config";
                    if (Plugin.Log != null)
                    {
                        Plugin.Log.LogInfo("[QSystem][isolation] NOT ARMED (vanilla merge): " + _reason);
                    }
                    return;
                }
                _buildingItems = QualityData.CountBuildingItems();
                if (TryInstall(harmony))
                {
                    _armed = true;
                    _reason = null;
                    P4_IsolationSort.Apply(harmony);
                    if (Plugin.Log != null)
                    {
                        Plugin.Log.LogInfo("[QSystem][isolation] armed (building items=" + _buildingItems
                            + ", patch points=" + (P4_IsolationSort.Armed ? 2 : 1)
                            + ", storage containers=" + StorageContainersState()
                            + " [AddItemStacked: " + IsolateScopeLabel()
                            + "; sort " + (P4_IsolationSort.Armed ? IsolateScopeLabel() : "off") + "])");
                    }
                }
            }
            catch (Exception e)
            {
                _reason = "Apply failed: " + e.GetType().Name;
                WarnOnce("isolation.apply", e);
            }
        }

        private static bool TryInstall(Harmony harmony)
        {
            MethodInfo target = typeof(StorageComponent).GetMethod("AddItemStacked",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(int), typeof(int), typeof(int), typeof(int).MakeByRefType() }, null);
            if (target == null)
            {
                _reason = "StorageComponent.AddItemStacked(int,int,int,out int) not found";
                WarnOnce("isolation.install", _reason);
                return false;
            }

            ParameterInfo[] parameters = target.GetParameters();
            if (parameters.Length != 4
                || parameters[0].Name != "itemId"
                || parameters[1].Name != "count"
                || parameters[2].Name != "inc"
                || !parameters[3].IsOut
                || parameters[3].Name != "remainInc")
            {
                _reason = "AddItemStacked signature/parameter names changed: " + DescribeParameters(parameters);
                WarnOnce("isolation.install", _reason);
                return false;
            }

            MethodInfo prefix = typeof(P4_Isolation).GetMethod("AddItemStackedPrefix",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (prefix == null)
            {
                _reason = "AddItemStackedPrefix missing";
                WarnOnce("isolation.install", _reason);
                return false;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            if (Plugin.Log != null)
            {
                Plugin.Log.LogInfo("[QSystem][isolation] patch registered: StorageComponent.AddItemStacked"
                    + " prefix=AddItemStackedPrefix (inc-aware merge for building items: player inventory + Default storage containers)");
            }
            return true;
        }

        /// <summary>
        /// Returns false (vanilla body skipped) only after the isolation copy has run; return true
        /// means "let the original AddItemStacked do exactly what it always did". Hard rule from the
        /// spec: any exception, missing precondition or unreadable state returns true so the vanilla
        /// body runs; the prefix never invents a result on failure (that could duplicate or drop
        /// items). The one exception is the add path itself, which already completed inside
        /// AddIsolatedStacked before returning false.
        /// </summary>
        private static bool AddItemStackedPrefix(StorageComponent __instance, int itemId, int count, int inc,
            out int remainInc, ref int __result)
        {
            remainInc = inc;
            try
            {
                if (!_armed)
                {
                    return true;
                }
                if (!QualityRules.ShouldIsolateStorage(__instance))
                {
                    return true;
                }
                if (QualityConfig.Enabled == null || !QualityConfig.Enabled.Value)
                {
                    return true;
                }
                if (QualityConfig.IsolateBuildingStacks == null || !QualityConfig.IsolateBuildingStacks.Value)
                {
                    return true;
                }
                if (itemId <= 0 || count <= 0)
                {
                    return true;
                }
                StorageComponent.GRID[] grids = __instance.grids;
                if (grids == null || grids.Length == 0 || __instance.size <= 0)
                {
                    return true;
                }
                if (!QualityData.IsBuildingItem(itemId))
                {
                    return true;
                }

                int isolatedResult = QualityData.AddIsolatedStacked(__instance, itemId, count, inc, out remainInc);
                __result = isolatedResult;
                _splits++;
                return false;
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("isolation.add", e);
                remainInc = inc;
                return true;
            }
        }

        private static string StorageContainersState()
        {
            return QualityConfig.IsolateStorageContainers != null
                && QualityConfig.IsolateStorageContainers.Value
                ? "on"
                : "off";
        }

        private static string IsolateScopeLabel()
        {
            return StorageContainersState() == "on" ? "player + Default containers" : "player only";
        }

        public static string Describe()
        {
            if (QualityConfig.IsolateBuildingStacks != null && !QualityConfig.IsolateBuildingStacks.Value)
            {
                return "isolation NOT ARMED (vanilla merge: isolateBuildingStacks disabled by config)";
            }
            if (!_armed)
            {
                return "isolation NOT ARMED (vanilla merge, reason: " + (_reason ?? "not applied yet") + ")";
            }
            return "isolation armed (building items=" + _buildingItems + ", patch points="
                + (P4_IsolationSort.Armed ? 2 : 1) + ", opens=" + _splits
                + ", storage containers=" + StorageContainersState()
                + " [AddItemStacked: inc-aware merge for building items (" + IsolateScopeLabel() + "); "
                + P4_IsolationSort.Describe() + "])";
        }

        private static string DescribeParameters(ParameterInfo[] parameters)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (sb.Length > 0)
                {
                    sb.Append(',');
                }
                sb.Append(parameters[i].ParameterType.Name);
                sb.Append(' ').Append(parameters[i].Name);
            }
            return sb.ToString();
        }

        private static void WarnOnce(string key, object detail)
        {
            if (_warned)
            {
                return;
            }
            _warned = true;
            try
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning("[QSystem][isolation] NOT ARMED (vanilla merge): " + key + ": " + detail);
                }
            }
            catch (Exception)
            {
                // logging must never break patching
            }
        }
    }
}
