using System;

namespace DSPQualitySystem.OfflineTests
{
    internal static partial class Program
    {
        /// <summary>
        /// v0.15.0: this file used to also cover the deleted blueprint-carried-quality feature
        /// (parameter round trip + blueprint quality gates). Only the generic "prefer the highest
        /// quality building item" consumption helpers remain, so the file was narrowed to them.
        /// </summary>
        private static void CheckConsumePriorityPick()
        {
            // PerItemPoints: integer division, 0 for degenerate stacks.
            Assert(QualityMath.PerItemPoints(500, 100) == 5, "PerItemPoints(500,100) should be 5");
            Assert(QualityMath.PerItemPoints(1000, 100) == 10, "PerItemPoints(1000,100) should be 10");
            Assert(QualityMath.PerItemPoints(99, 100) == 0, "PerItemPoints(99,100) should truncate to 0");
            Assert(QualityMath.PerItemPoints(0, 10) == 0, "empty grid should be 0 points");
            Assert(QualityMath.PerItemPoints(-50, 10) == 0, "negative inc should be 0 points");
            Assert(QualityMath.PerItemPoints(50, 0) == 0, "zero count should be 0 points");
            Assert(QualityMath.PerItemPoints(50, -3) == 0, "negative count should be 0 points");

            // ChooseBestMatchGrid: highest points-per-item wins, item id must match.
            int[] itemIds = { 1001, 1002, 1001, 1001 };
            int[] counts = { 10, 5, 10, 2 };
            int[] incs = { 50, 0, 90, 10 };
            Assert(QualityMath.ChooseBestMatchGrid(itemIds, counts, incs, 1001) == 2,
                "the 9.0/item grid should beat the 5.0/item grid");
            Assert(QualityMath.ChooseBestMatchGrid(itemIds, counts, incs, 1002) == 1,
                "the only matching grid should win even with 0 points");
            Assert(QualityMath.ChooseBestMatchGrid(itemIds, counts, incs, 9999) == -1,
                "an unknown item id should return -1");

            int[] tieIds = { 7, 7 };
            int[] tieCounts = { 10, 10 };
            int[] tieIncs = { 60, 60 };
            Assert(QualityMath.ChooseBestMatchGrid(tieIds, tieCounts, tieIncs, 7) == 0,
                "equal points per item should keep the lowest index");

            int[] cmpIds = { 8, 8 };
            int[] cmpCounts = { 3, 7 };
            int[] cmpIncs = { 9, 20 };
            Assert(QualityMath.ChooseBestMatchGrid(cmpIds, cmpCounts, cmpIncs, 8) == 0,
                "cross multiplication should compare 3.0/item against 2.857/item");

            int[] zeroIds = { 9, 9 };
            int[] zeroCounts = { 0, 4 };
            int[] zeroIncs = { 1000, 8 };
            Assert(QualityMath.ChooseBestMatchGrid(zeroIds, zeroCounts, zeroIncs, 9) == 1,
                "a zero-count grid must be ignored");

            Assert(QualityMath.ChooseBestMatchGrid(null, counts, incs, 1001) == -1,
                "a null item id array should return -1");
            Assert(QualityMath.ChooseBestMatchGrid(itemIds, counts, incs, 0) == -1,
                "a non-positive item id should return -1");

            // ShouldSwapHand: strictly better points per item in the backpack triggers the swap.
            Assert(QualityMath.ShouldSwapHand(0, 1, 30, 3), "0/1 vs 30/3 should swap");
            Assert(QualityMath.ShouldSwapHand(50, 10, 60, 10), "5.0 vs 6.0 points/item should swap");
            Assert(!QualityMath.ShouldSwapHand(60, 10, 60, 10), "equal quality must not swap");
            Assert(!QualityMath.ShouldSwapHand(70, 10, 60, 10), "a better hand stack must not swap");
            Assert(!QualityMath.ShouldSwapHand(50, 0, 60, 10), "an empty hand must not swap");
            Assert(!QualityMath.ShouldSwapHand(50, 10, 60, 0), "an empty target grid must not swap");
        }
    }
}
