using System;
using System.Reflection;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.11.1 (P4 sort): the backpack "sort" button must not merge building stacks that carry
    /// different quality points.
    ///
    /// Vanilla StorageComponent.Sort(bool) (asm_full.cs:81153-81236) first aggregates every grid
    /// into a SortedDictionary keyed by item proto index and does value.count += grids[i].count;
    /// value.inc += grids[i].inc; (the inc of a grid is the TOTAL points of that stack, see
    /// split_inc at asm_full.cs:81360). Any two stacks of the same building item therefore collapse
    /// into one grid and their points are added, which bypasses the AddItemStacked isolation prefix
    /// from v0.10.0.
    ///
    /// This class installs a Prefix on Sort(bool) that takes over only when
    /// QualityRules.ShouldIsolateStorage accepts the storage (player inventory, or a plain Default
    /// container when isolateStorageContainers is on), no grid has a filter, and at least one
    /// building stack would actually split into two quality groups. The regroup plan is computed by the pure
    /// QualityMath.PlanSortedLayout (same vanilla grouping/order/stack spreading, but building
    /// stacks only merge when the per-item point level matches). Everything else returns true so
    /// the vanilla body runs untouched (bit-identical for non-building items).
    ///
    /// Failure policy: the prefix snapshots the grids before calling Clear(); on any exception the
    /// snapshot is restored and the vanilla Sort is allowed to run on the restored state. Losing or
    /// duplicating items is never acceptable, so a partial custom sort is only kept after the whole
    /// write/notify sequence completed. A binding failure disables only this feature, warns once,
    /// and is reported by Describe()/StartupAudit.
    /// </summary>
    internal static class P4_IsolationSort
    {
        private static bool _armed;
        private static bool _warned;
        private static string _reason = "not applied yet";
        private static long _regrouped;
        private static int _debugLogged;

        public static bool Armed
        {
            get { return _armed; }
        }

        public static bool Apply(Harmony harmony)
        {
            try
            {
                if (harmony == null)
                {
                    _reason = "harmony instance is null";
                    WarnOnce("isolation.sort.apply", _reason);
                    return false;
                }
                if (QualityConfig.IsolateBuildingStacks == null || !QualityConfig.IsolateBuildingStacks.Value)
                {
                    _reason = "isolateBuildingStacks disabled by config";
                    WarnOnce("isolation.sort.apply", _reason);
                    return false;
                }
                if (!TryInstall(harmony))
                {
                    return false;
                }
                _armed = true;
                _reason = null;
                return true;
            }
            catch (Exception e)
            {
                _reason = "Apply failed: " + e.GetType().Name;
                WarnOnce("isolation.sort.apply", e);
                return false;
            }
        }

        private static bool TryInstall(Harmony harmony)
        {
            MethodInfo target = typeof(StorageComponent).GetMethod("Sort",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(bool) }, null);
            if (target == null)
            {
                _reason = "StorageComponent.Sort(bool) not found";
                WarnOnce("isolation.sort.install", _reason);
                return false;
            }
            if (target.ReturnType != typeof(void))
            {
                _reason = "StorageComponent.Sort(bool) does not return void";
                WarnOnce("isolation.sort.install", _reason);
                return false;
            }

            ParameterInfo[] parameters = target.GetParameters();
            if (parameters.Length != 1
                || parameters[0].ParameterType != typeof(bool)
                || parameters[0].Name != "raiseNotify")
            {
                _reason = "StorageComponent.Sort signature/parameter name changed: " + DescribeParameters(parameters);
                WarnOnce("isolation.sort.install", _reason);
                return false;
            }

            MethodInfo prefix = typeof(P4_IsolationSort).GetMethod("SortPrefix",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (prefix == null)
            {
                _reason = "SortPrefix missing";
                WarnOnce("isolation.sort.install", _reason);
                return false;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            if (Plugin.Log != null)
            {
                Plugin.Log.LogInfo("[QSystem][isolation] sort patch registered: StorageComponent.Sort(bool)"
                    + " prefix=SortPrefix (building stacks are regrouped by per-item quality points)");
            }
            return true;
        }

        /// <summary>
        /// Returns true in every uncertain situation so the vanilla Sort body runs. False is only
        /// returned after a complete custom layout was written (and, when requested, notified).
        /// </summary>
        private static bool SortPrefix(StorageComponent __instance, bool raiseNotify)
        {
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
                if (__instance.type != EStorageType.Default)
                {
                    return true;
                }

                StorageComponent.GRID[] grids = __instance.grids;
                if (grids == null || grids.Length == 0 || __instance.size <= 0)
                {
                    return true;
                }
                // StorageComponent.GRID has no "ordered" field (asm_full.cs:79407-79418; the GRID
                // with ordered belongs to DeliveryPackage at 78854). A non-zero filter means the
                // player configured a filter on that slot, so leave the whole sort to vanilla.
                for (int i = 0; i < grids.Length; i++)
                {
                    if (grids[i].filter != 0)
                    {
                        return true;
                    }
                }
                if (LDB.items == null)
                {
                    return true;
                }

                int capacity = grids.Length;
                int[] itemIds = new int[capacity];
                int[] counts = new int[capacity];
                int[] incs = new int[capacity];
                int[] stackSizes = new int[capacity];
                int[] protoIndexes = new int[capacity];
                bool[] isBuilding = new bool[capacity];
                bool anyBuilding = false;
                for (int i = 0; i < capacity; i++)
                {
                    itemIds[i] = grids[i].itemId;
                    counts[i] = grids[i].count;
                    incs[i] = grids[i].inc;
                    stackSizes[i] = grids[i].stackSize;
                    protoIndexes[i] = -1;
                    if (grids[i].itemId <= 0 || grids[i].count <= 0)
                    {
                        continue;
                    }
                    if (QualityData.IsBuildingItem(grids[i].itemId))
                    {
                        isBuilding[i] = true;
                        anyBuilding = true;
                    }
                    try
                    {
                        ItemProto proto = LDB.items.Select(grids[i].itemId);
                        if (proto != null)
                        {
                            protoIndexes[i] = proto.index;
                        }
                    }
                    catch (Exception)
                    {
                        // proto index is only the ordering hint; -1 falls back to 10000+itemId
                    }
                }
                if (!anyBuilding)
                {
                    // pure minerals/materials: vanilla Sort is already correct and cheaper
                    return true;
                }

                int[] outIds;
                int[] outCounts;
                int[] outIncs;
                int planCount = QualityMath.PlanSortedLayout(itemIds, counts, incs, stackSizes, isBuilding,
                    protoIndexes, out outIds, out outCounts, out outIncs);
                if (planCount <= 0)
                {
                    return true;
                }
                if (!HasDuplicateItemIds(outIds, outCounts, planCount))
                {
                    // one group per item id: vanilla Sort produces the same content, let it run
                    return true;
                }

                StorageComponent.GRID[] snapshot = (StorageComponent.GRID[])grids.Clone();
                bool cleared = false;
                try
                {
                    __instance.Clear();
                    cleared = true;
                    for (int i = 0; i < planCount; i++)
                    {
                        int id = outIds[i];
                        int count = outCounts[i];
                        if (id <= 0 || count <= 0)
                        {
                            continue;
                        }
                        grids[i].itemId = id;
                        grids[i].count = count;
                        grids[i].inc = outIncs[i];
                        grids[i].stackSize = StackSizeOf(id);
                    }
                    if (raiseNotify)
                    {
                        __instance.ResetOptimizationFlags();
                        __instance.NotifyStorageChange();
                    }
                    _regrouped++;
                    LogRegrouped(outIds, outCounts, planCount);
                    return false;
                }
                catch (Exception e)
                {
                    if (cleared)
                    {
                        RestoreSnapshot(__instance, snapshot);
                    }
                    QualityEnv.WarnOnce("isolation.sort", e);
                    return true;
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("isolation.sort.guard", e);
                return true;
            }
        }

        private static bool HasDuplicateItemIds(int[] outIds, int[] outCounts, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (outCounts[i] <= 0)
                {
                    continue;
                }
                for (int j = i + 1; j < count; j++)
                {
                    if (outCounts[j] > 0 && outIds[i] == outIds[j])
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static int StackSizeOf(int itemId)
        {
            try
            {
                int[] table = StorageComponent.itemStackCount;
                if (table != null && itemId > 0 && itemId < table.Length && table[itemId] > 0)
                {
                    return table[itemId];
                }
            }
            catch (Exception)
            {
            }
            return 1000;
        }

        private static void RestoreSnapshot(StorageComponent instance, StorageComponent.GRID[] snapshot)
        {
            try
            {
                StorageComponent.GRID[] grids = instance.grids;
                if (grids != null && snapshot != null)
                {
                    int n = grids.Length < snapshot.Length ? grids.Length : snapshot.Length;
                    Array.Copy(snapshot, 0, grids, 0, n);
                    instance.ResetOptimizationFlags();
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("isolation.sort.restore", e);
            }
        }

        private static void LogRegrouped(int[] outIds, int[] outCounts, int planCount)
        {
            try
            {
                if (QualityConfig.DebugProbes == null || !QualityConfig.DebugProbes.Value || Plugin.Log == null)
                {
                    return;
                }
                if (_debugLogged >= 20)
                {
                    return;
                }
                _debugLogged++;
                int buildingGroups = 0;
                int items = 0;
                for (int i = 0; i < planCount; i++)
                {
                    if (outIds[i] <= 0 || outCounts[i] <= 0)
                    {
                        continue;
                    }
                    items++;
                    if (QualityData.IsBuildingItem(outIds[i]))
                    {
                        buildingGroups++;
                    }
                }
                Plugin.Log.LogInfo("[QSystem][isolation] sort regrouped items=" + items
                    + " grids=" + planCount + " building-groups=" + buildingGroups);
            }
            catch (Exception)
            {
                // observation only
            }
        }

        public static string Describe()
        {
            if (QualityConfig.IsolateBuildingStacks != null && !QualityConfig.IsolateBuildingStacks.Value)
            {
                return "sort NOT ARMED (vanilla sort: isolateBuildingStacks disabled by config)";
            }
            if (!_armed)
            {
                return "sort NOT ARMED (vanilla sort, reason: " + (_reason ?? "not applied yet") + ")";
            }
            return "sort armed (StorageComponent.Sort(bool) prefix=SortPrefix, regrouped=" + _regrouped
                + ", storage containers=" + (QualityConfig.IsolateStorageContainers != null
                    && QualityConfig.IsolateStorageContainers.Value ? "on" : "off") + ")";
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
                    Plugin.Log.LogWarning("[QSystem][isolation] NOT ARMED (vanilla sort): " + key + ": " + detail);
                }
            }
            catch (Exception)
            {
                // logging must never break patching
            }
        }
    }
}
