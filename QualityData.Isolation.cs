using System;

namespace DSPQualitySystem
{
    /// <summary>
    /// v0.10.0 (P4): backpack building-quality isolation. Player inventory merges items purely by
    /// itemId (StorageComponent.AddItemStacked, asm_full.cs:80012-80122), so a 0-point and a
    /// 4-point building would share one grid and their points would be added together. This part
    /// re-implements that single add path with one extra condition for eligible storages: only
    /// stacks of a building item with the same per-item level (inc/count) may merge, different
    /// point values occupy separate grids. v0.11.3 extends the eligible set from the player
    /// inventory to plain Default storage containers when isolateStorageContainers is on
    /// (QualityRules.ShouldIsolateStorage owns that decision). Non-building items and non-eligible
    /// storages never reach this code (the prefix returns early and the vanilla body runs unchanged).
    ///
    /// Split from QualityData.cs to keep every file under the project convention of 500 lines;
    /// the storage class itself is partial.
    /// </summary>
    public static partial class QualityData
    {
        private static int _isolationDebugCount;

        /// <summary>
        /// True when the item proto is a placeable building. The original game uses
        /// ItemProto.CanBuild for the same purpose (build tool asm_full.cs:163657, build bar
        /// asm_full.cs:291427). LDB not ready / unknown id returns false so the caller falls back
        /// to vanilla merging.
        /// </summary>
        public static bool IsBuildingItem(int itemId)
        {
            if (itemId <= 0)
            {
                return false;
            }
            try
            {
                var items = LDB.items;
                if (items == null)
                {
                    return false;
                }
                ItemProto proto = items.Select(itemId);
                return proto != null && proto.CanBuild;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Number of buildable item protos (startup audit self check; 0 when LDB is not ready).</summary>
        public static int CountBuildingItems()
        {
            try
            {
                var items = LDB.items;
                ItemProto[] arr = items == null ? null : items.dataArray;
                if (arr == null)
                {
                    return 0;
                }
                int count = 0;
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i] != null && arr[i].CanBuild)
                    {
                        count++;
                    }
                }
                return count;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// v0.10.0 isolation-aware replacement for StorageComponent.AddItemStacked(int,int,int,out int).
        /// Mirrors the vanilla two-pass loop (asm_full.cs:80041-80113) and only changes the merge
        /// condition: a grid with a different inc is skipped instead of merged. Same inc still merges
        /// up to stackSize, partial fills still distribute points with SplitInc, leftover items are
        /// returned through remainInc exactly like vanilla (the caller then tries the delivery
        /// package / hand / trash, so nothing is lost).
        /// </summary>
        public static int AddIsolatedStacked(StorageComponent storage, int itemId, int count, int inc,
            out int remainInc)
        {
            remainInc = inc;
            if (storage == null || itemId <= 0 || count <= 0)
            {
                return 0;
            }

            StorageComponent.GRID[] grids = storage.grids;
            if (grids == null)
            {
                return 0;
            }

            int size = storage.size;
            if (size > grids.Length)
            {
                size = grids.Length;
            }
            if (size <= 0)
            {
                return 0;
            }

            int remaining = count;
            int remainingInc = inc;
            int stackSize = 0;
            int skipped = 0;

            // Pass 1: merge into existing stacks of the same item and the same per-item level.
            for (int i = 0; i < size && remaining > 0; i++)
            {
                if (grids[i].itemId != itemId)
                {
                    continue;
                }
                if (!QualityMath.SharesStack(itemId, grids[i].itemId, grids[i].count, grids[i].inc,
                        count, inc, true, true))
                {
                    skipped++;
                    continue;
                }
                if (!MergeIntoGrid(ref grids[i], ref stackSize, ref remaining, ref remainingInc))
                {
                    skipped++;
                }
            }

            // Pass 2: create/use free grids (vanilla second loop; the merge condition is the same).
            if (remaining > 0)
            {
                for (int j = 0; j < size && remaining > 0; j++)
                {
                    if (grids[j].itemId == 0)
                    {
                        if (stackSize == 0)
                        {
                            stackSize = StackSizeOf(itemId);
                        }
                        grids[j].itemId = itemId;
                        if (grids[j].filter == 0)
                        {
                            grids[j].stackSize = stackSize;
                        }
                    }
                    if (grids[j].itemId != itemId)
                    {
                        continue;
                    }
                    if (!QualityMath.SharesStack(itemId, grids[j].itemId, grids[j].count, grids[j].inc,
                            count, inc, true, true))
                    {
                        skipped++;
                        continue;
                    }
                    if (!MergeIntoGrid(ref grids[j], ref stackSize, ref remaining, ref remainingInc))
                    {
                        skipped++;
                    }
                }
            }

            int added = count - remaining;
            if (added > 0)
            {
                storage.searchStart = 0;
                storage.lastEmptyItem = -1;
                storage.NotifyStorageChange();
                LogIsolationSplit(itemId, inc, skipped, ContainerLabel(storage));
            }
            remainInc = remainingInc;
            return added;
        }

        /// <summary>
        /// One vanilla-style fill of a single grid. Returns false when the grid cannot take anything
        /// (full grid), so the caller can count it as a skipped merge site and move on.
        /// </summary>
        private static bool MergeIntoGrid(ref StorageComponent.GRID grid, ref int stackSize,
            ref int remaining, ref int remainingInc)
        {
            if (stackSize == 0)
            {
                stackSize = grid.stackSize;
            }
            int room = stackSize - grid.count;
            if (room <= 0)
            {
                return false;
            }
            if (remaining <= room)
            {
                grid.count += remaining;
                grid.inc += remainingInc;
                remaining = 0;
                remainingInc = 0;
                return true;
            }
            grid.count = stackSize;
            grid.inc += QualityMath.SplitInc(ref remaining, ref remainingInc, room);
            return true;
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
                // fall through to the vanilla default
            }
            return 1000;
        }

        private static string ContainerLabel(StorageComponent storage)
        {
            try
            {
                return storage.isPlayerInventory ? "player" : storage.type.ToString();
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        private static void LogIsolationSplit(int itemId, int inc, int skippedGrids, string container)
        {
            if (skippedGrids <= 0 || _isolationDebugCount >= 20)
            {
                return;
            }
            try
            {
                if (!QualityConfig.DebugProbes.Value || Plugin.Log == null)
                {
                    return;
                }
                _isolationDebugCount++;
                Plugin.Log.LogInfo("[QSystem][isolation] split item=" + itemId
                    + " inc=" + inc + " grids=" + skippedGrids + " container=" + container);
            }
            catch (Exception)
            {
                // never let probe logging break the inventory add
            }
        }
    }
}
