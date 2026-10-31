using System;
using System.Globalization;
using UnityEngine;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.11.5: consumption side of the "prefer the highest quality building item" patch. Hand path
    /// swaps a better backpack stack into the hand; backpack path picks the best grid itself. Both are
    /// gated on the consume windows and degrade to vanilla on any uncertainty.
    /// </summary>
    internal static partial class P4_ConsumePriority
    {
        // ---------------- hand path ----------------

        private static void HandPrefix(Player __instance, out HandState __state)
        {
            __state = new HandState();
            try
            {
                if (__instance == null)
                {
                    return;
                }
                __state.itemId = __instance.inhandItemId;
                __state.count = __instance.inhandItemCount;
                if (!_armed || !InConsumeWindow || !PreferEnabled)
                {
                    return;
                }
                int itemId = __state.itemId;
                int count = __state.count;
                if (itemId <= 0 || count <= 0)
                {
                    return;
                }
                StorageComponent package = __instance.package;
                if (package == null || package.grids == null)
                {
                    return;
                }
                int best = BestGrid(package, itemId);
                if (best < 0)
                {
                    return;
                }
                StorageComponent.GRID grid = package.grids[best];
                if (!QualityMath.ShouldSwapHand(__instance.inhandItemInc, count, grid.inc, grid.count))
                {
                    return;
                }
                int handPer100 = PerHundredths(__instance.inhandItemInc, count);
                int gridPer100 = PerHundredths(grid.inc, grid.count);
                int gridCount = grid.count;
                _swapping++;
                try
                {
                    __instance.SetHandItems(itemId, gridCount, best);
                }
                finally
                {
                    if (_swapping > 0)
                    {
                        _swapping--;
                    }
                }
                _swapped++;
                LogDebug("[QSystem][consume] pick item=" + itemId
                    + " planet=" + QualityEnv.CurrentPlanetId
                    + " hand=" + FormatPer100(handPer100) + "x" + count
                    + " -> grid=" + best + " " + FormatPer100(gridPer100) + "x" + gridCount
                    + " reason=higher");
            }
            catch (Exception e)
            {
                P5_ConsumeLog.WarnOnce("consume.handswap", e.ToString());
            }
        }

        private static void HandPostfix(Player __instance, int useCnt, int useInc, int __result,
            HandState __state)
        {
            try
            {
                if (!_armed || !InConsumeWindow || _swapping > 0 || __state.itemId <= 0)
                {
                    return;
                }
                int used = __result;
                if (used <= 0 && __state.count > 0 && __instance != null
                    && __instance.inhandItemCount == 0)
                {
                    used = __state.count > useCnt ? useCnt : __state.count;
                }
                if (used <= 0)
                {
                    return;
                }
                Note(__state.itemId, PerHundredths(useInc, used), SourceHand, -1);
            }
            catch (Exception)
            {
            }
        }

        // ---------------- backpack path ----------------

        private static bool TakeBestGridPrefix(StorageComponent __instance, ref int itemId,
            ref int count, out int inc, bool useBan)
        {
            inc = 0;
            if (!_armed || !InConsumeWindow || !PreferEnabled || useBan || __instance == null)
            {
                return true;
            }
            // v0.18.0 (issue 4): while ConstructionModuleComponent.PlaceItems pays a white ghost
            // pre-build, vanilla asserts that one call covers the full remaining itemRequired stack.
            // Swapping in another grid or taking a partial stack here would break that assert, so the
            // consume layer steps aside for the short window and only observes the payment.
            if (P1_DeferredPay.PayWindowActive)
            {
                return true;
            }
            try
            {
                if (itemId <= 0 || count <= 0)
                {
                    return true;
                }
                StorageComponent.GRID[] grids = __instance.grids;
                if (grids == null)
                {
                    return true;
                }
                int size = __instance.size;
                if (size > grids.Length)
                {
                    size = grids.Length;
                }
                int[] itemIds = new int[size];
                int[] counts = new int[size];
                int[] incs = new int[size];
                for (int i = 0; i < size; i++)
                {
                    itemIds[i] = grids[i].itemId;
                    counts[i] = grids[i].count;
                    incs[i] = grids[i].inc;
                }
                int best = QualityMath.ChooseBestMatchGrid(itemIds, counts, incs, itemId);
                if (best < 0)
                {
                    return true;
                }
                int need = count;
                int taken;
                int takenInc;
                if (grids[best].count > need)
                {
                    int gridCount = grids[best].count;
                    int gridInc = grids[best].inc;
                    takenInc = QualityMath.SplitInc(ref gridCount, ref gridInc, need);
                    grids[best].count = gridCount;
                    grids[best].inc = gridInc;
                    taken = need;
                }
                else
                {
                    taken = grids[best].count;
                    takenInc = grids[best].inc;
                    grids[best].itemId = grids[best].filter;
                    grids[best].count = 0;
                    grids[best].inc = 0;
                    if (grids[best].filter == 0)
                    {
                        grids[best].stackSize = 0;
                    }
                }
                if (taken <= 0)
                {
                    return true;
                }
                count = taken;
                inc = takenInc;
                __instance.lastFullItem = -1;
                __instance.NotifyStorageChange();
                int per100 = PerHundredths(takenInc, taken);
                Note(itemId, per100, SourceBackpack, best);
                _bestGridPicks++;
                LogDebug("[QSystem][consume] pick item=" + itemId
                    + " planet=" + QualityEnv.CurrentPlanetId
                    + " -> grid=" + best + " " + FormatPer100(per100) + "x" + taken
                    + " reason=highest");
                return false;
            }
            catch (Exception e)
            {
                P5_ConsumeLog.WarnOnce("consume.takebest", e.ToString());
                return true;
            }
        }

        private static int BestGrid(StorageComponent package, int itemId)
        {
            StorageComponent.GRID[] grids = package.grids;
            if (grids == null)
            {
                return -1;
            }
            int size = package.size;
            if (size > grids.Length)
            {
                size = grids.Length;
            }
            if (size <= 0)
            {
                return -1;
            }
            int[] itemIds = new int[size];
            int[] counts = new int[size];
            int[] incs = new int[size];
            for (int i = 0; i < size; i++)
            {
                itemIds[i] = grids[i].itemId;
                counts[i] = grids[i].count;
                incs[i] = grids[i].inc;
            }
            return QualityMath.ChooseBestMatchGrid(itemIds, counts, incs, itemId);
        }

        // ---------------- receipt ----------------

        private static void Note(int itemId, int perItemHundredths, int source, int grid)
        {
            try
            {
                if (itemId <= 0 || !InConsumeWindow || _swapping > 0)
                {
                    return;
                }
                if (perItemHundredths < 0)
                {
                    perItemHundredths = 0;
                }
                else if (perItemHundredths > Cargo.kIncLevelMax * 100)
                {
                    perItemHundredths = Cargo.kIncLevelMax * 100;
                }
                _valid = true;
                _itemId = itemId;
                _perItemHundredths = perItemHundredths;
                _source = source;
                _grid = grid;
                _frame = SafeFrame();
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// v0.17.0: consume the recorded receipt for the item that actually paid for a building.
        /// The receipt is produced by the same-frame `TakeBestGridPrefix`/`HandPostfix` note, so it
        /// describes the highest-quality item the MOD chose to spend. Returns false when there is no
        /// fresh receipt or it cannot belong to `expectedItemId`; the receipt is single-use.
        ///
        /// Callers: `P1_BuildCapture.AddPrebuildData_ClearStale.Postfix` (normal hand build and
        /// blueprint paste) and `P1_InstantBuild` (Shift+click instant build via BuildFinally).
        /// </summary>
        internal static bool TryTakeReceipt(int expectedItemId, out int perItem)
        {
            perItem = 0;
            if (!_armed || !PreferEnabled || !_valid)
            {
                return false;
            }
            try
            {
                if (!InConsumeWindow)
                {
                    _valid = false;
                    return false;
                }
                if (_frame != SafeFrame())
                {
                    _valid = false;
                    return false;
                }
                if (!QualityMath.ReceiptItemMatches(_itemId, expectedItemId))
                {
                    _valid = false;
                    return false;
                }
                int per100 = _perItemHundredths;
                int source = _source;
                int grid = _grid;
                int itemId = _itemId;
                _valid = false;
                int value = QualityMath.ReceiptPerItem(per100);
                if (value <= 0)
                {
                    return false;
                }
                perItem = value;
                _captured++;
                LogDebug("[QSystem][consume] receipt item=" + itemId
                    + " perItem=" + value + " expected=" + expectedItemId
                    + " source=" + (source == SourceBackpack ? "backpack grid=" + grid : "hand")
                    + " window=" + DescribeWindow());
                return true;
            }
            catch (Exception e)
            {
                P5_ConsumeLog.WarnOnce("consume.receipt", e.ToString());
                return false;
            }
        }

        /// <summary>
        /// v0.15.0/v0.17.0: a freshly created prebuild inherits the points of the building item that
        /// was just consumed for it. v0.17.0 also covers the normal hand-build window: the backpack
        /// branch of `BuildTool_Click.CreatePrebuilds` (asm_full.cs:155445-155448) never calls
        /// `Player.UseHandItems`, so before this change only the `BuildQualityStash` path bound points
        /// and backpack-built buildings (e.g. the 大型采矿机) stayed at 0 points.
        /// No blueprint settlement logic is involved: the marker path was removed in v0.15.0.
        /// </summary>
        internal static void CapturePasteConsumed(PrebuildData prebuild, int prebuildId)
        {
            if (!_armed || !PreferEnabled || !InConsumeWindow)
            {
                return;
            }
            try
            {
                int perItem;
                if (!TryTakeReceipt(prebuild.protoId, out perItem))
                {
                    return;
                }
                QualityData.SetPrebuildInc(prebuildId, perItem);
                LogDebug("[QSystem][build] prebuild binds consumed item inc=" + perItem
                    + " item=" + prebuild.protoId
                    + " prebuild=" + prebuildId
                    + " planet=" + QualityEnv.CurrentPlanetId
                    + " window=" + DescribeWindow());
                EffectiveLog(" (swapped=" + _swapped + " bestGrid=" + _bestGridPicks
                    + " captured=" + _captured + ")");
            }
            catch (Exception e)
            {
                P5_ConsumeLog.WarnOnce("consume.capture", e.ToString());
            }
        }

        private static string DescribeWindow()
        {
            if (_inPaste > 0)
            {
                return "paste";
            }
            if (_inHandBuild > 0)
            {
                return "hand-build";
            }
            if (InstantWindowOpen)
            {
                return "instant-build";
            }
            return "none";
        }

        private static void LogDebug(string line)
        {
            try
            {
                if (QualityConfig.DebugProbes == null || !QualityConfig.DebugProbes.Value)
                {
                    return;
                }
                if (_debugLogs >= DebugLogLimit)
                {
                    return;
                }
                _debugLogs++;
                P5_ConsumeLog.Info(line);
            }
            catch (Exception)
            {
            }
        }

        private static void EffectiveLog(string suffix)
        {
            try
            {
                long n = ++_effective;
                if (n % EffectiveLogEvery != 0)
                {
                    return;
                }
                P5_ConsumeLog.Info("[QSystem][consume] effective=" + n + (suffix ?? string.Empty));
            }
            catch (Exception)
            {
            }
        }

        private static int SafeFrame()
        {
            try
            {
                return Time.frameCount;
            }
            catch (Exception)
            {
                return int.MinValue;
            }
        }

        private static int PerHundredths(int inc, int count)
        {
            if (count <= 0 || inc <= 0)
            {
                return 0;
            }
            long value = (long)inc * 100L / count;
            if (value < 0)
            {
                return 0;
            }
            if (value > Cargo.kIncLevelMax * 100)
            {
                return Cargo.kIncLevelMax * 100;
            }
            return (int)value;
        }

        private static string FormatPer100(int per100)
        {
            try
            {
                return ((double)per100 / 100.0).ToString("0.##", CultureInfo.InvariantCulture) + "pts";
            }
            catch (Exception)
            {
                return per100 + "pts/100";
            }
        }
    }
}