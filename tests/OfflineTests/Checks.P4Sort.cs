using System;

namespace DSPQualitySystem.OfflineTests
{
    internal static partial class Program
    {
        // ---------------------------------------------------------------------------------------
        // v0.11.1 (P4 sort): the backpack "sort" action must keep building stacks with different
        // per-item quality points in separate grids. QualityMath.PlanSortedLayout mirrors the
        // vanilla StorageComponent.Sort grouping/ordering/stack spreading, so these checks assert
        // the exact layout the prefix will write. Totals must always be conserved.
        // ---------------------------------------------------------------------------------------

        internal static void CheckSortIsolationBasics()
        {
            int[] ids;
            int[] counts;
            int[] incs;

            // The reported bug: one 0-point and one 4-point stack of the same building item.
            int plan = Plan(
                new[] { 2104, 2104 },
                new[] { 1, 1 },
                new[] { 0, 4 },
                new[] { 1, 1 },
                new[] { true, true },
                new[] { 51, 51 },
                out ids, out counts, out incs);
            Assert(plan == 2, "0-point and 4-point building stacks must occupy two grids (got " + plan + ")");
            Assert(ids[0] == 2104 && counts[0] == 1 && incs[0] == 0, "first grid keeps the 0-point stack");
            Assert(ids[1] == 2104 && counts[1] == 1 && incs[1] == 4, "second grid keeps the 4-point stack");

            // A single building stack plus empty grids stays exactly one grid.
            plan = Plan(
                new[] { 2104, 0, 0 },
                new[] { 5, 0, 0 },
                new[] { 20, 0, 0 },
                new[] { 10, 0, 0 },
                new[] { true, false, false },
                new[] { 51, -1, -1 },
                out ids, out counts, out incs);
            Assert(plan == 1, "one building stack stays in one grid");
            Assert(counts[0] == 5 && incs[0] == 20, "count and total points are preserved");

            // Level ordering is ascending: 0-point grid first, 4-point grid second, 10-point last.
            plan = Plan(
                new[] { 2104, 2104, 2104 },
                new[] { 1, 1, 1 },
                new[] { 40, 0, 4 },
                new[] { 1, 1, 1 },
                new[] { true, true, true },
                new[] { 51, 51, 51 },
                out ids, out counts, out incs);
            Assert(plan == 3, "three distinct levels stay in three grids");
            Assert(incs[0] == 0 && incs[1] == 4 && incs[2] == 40, "levels are ordered ascending");
        }

        internal static void CheckSortIsolationMerge()
        {
            int[] ids;
            int[] counts;
            int[] incs;
            // 0-point x3 and 4-point x2: two groups, counts/points kept apart.
            int plan = Plan(
                new[] { 2104, 2104, 2104, 2104, 2104 },
                new[] { 1, 1, 1, 1, 1 },
                new[] { 0, 0, 0, 4, 4 },
                new[] { 10, 10, 10, 10, 10 },
                new[] { true, true, true, true, true },
                new[] { 51, 51, 51, 51, 51 },
                out ids, out counts, out incs);
            Assert(plan == 2, "0/4 split stays in two groups");
            Assert(counts[0] == 3 && incs[0] == 0, "0-point group keeps 3 items / 0 points");
            Assert(counts[1] == 2 && incs[1] == 8, "4-point group keeps 2 items / 8 points");

            // Same level scattered over two grids merges into one grid.
            plan = Plan(
                new[] { 2104, 2104 },
                new[] { 1, 1 },
                new[] { 4, 4 },
                new[] { 10, 10 },
                new[] { true, true },
                new[] { 51, 51 },
                out ids, out counts, out incs);
            Assert(plan == 1, "same per-item level merges");
            Assert(counts[0] == 2 && incs[0] == 8, "merged grid keeps count 2 / 8 points");

            // Fractional levels compare exactly (10 items 30 points = 3/item vs 3 items 9 points).
            // Stack size 20 so the merged 13-item group still fits one grid; the spill path is
            // covered by the overflow case below.
            plan = Plan(
                new[] { 2104, 2104 },
                new[] { 10, 3 },
                new[] { 30, 9 },
                new[] { 20, 20 },
                new[] { true, true },
                new[] { 51, 51 },
                out ids, out counts, out incs);
            Assert(plan == 1, "3 points/item groups merge when they fit one grid");
            Assert(counts[0] == 13 && incs[0] == 39, "merged group keeps 13 items / 39 points");

            // Overflow across the grid stack size is distributed with the vanilla split_inc.
            plan = Plan(
                new[] { 2104, 2104, 2104 },
                new[] { 1, 1, 1 },
                new[] { 4, 4, 4 },
                new[] { 2, 2, 2 },
                new[] { true, true, true },
                new[] { 51, 51, 51 },
                out ids, out counts, out incs);
            Assert(plan == 2, "3 items over stackSize 2 spill into two grids");
            Assert(counts[0] == 2 && incs[0] == 8, "full grid takes 2 items / 8 points");
            Assert(counts[1] == 1 && incs[1] == 4, "remaining grid takes 1 item / 4 points");
        }

        internal static void CheckSortNonBuildingVanilla()
        {
            int[] ids;
            int[] counts;
            int[] incs;

            // Non-building items merge by item id exactly like vanilla (inc totals add up).
            int plan = Plan(
                new[] { 1001, 1001, 1001 },
                new[] { 5, 10, 20 },
                new[] { 1, 2, 3 },
                new[] { 100, 100, 100 },
                new[] { false, false, false },
                new[] { 7, 7, 7 },
                out ids, out counts, out incs);
            Assert(plan == 1, "non-building stacks of one item merge into one grid");
            Assert(counts[0] == 35 && incs[0] == 6, "non-building total count/points are conserved");

            // Empty player backpack: nothing to write.
            plan = Plan(
                new[] { 0, 0 },
                new[] { 0, 0 },
                new[] { 0, 0 },
                new[] { 0, 0 },
                new[] { false, false },
                new[] { -1, -1 },
                out ids, out counts, out incs);
            Assert(plan == 0, "empty backpack plans zero grids");

            // Mixed backpack: minerals merge, building levels stay split, ordered by proto index.
            plan = Plan(
                new[] { 2104, 1001, 2104, 1001 },
                new[] { 1, 5, 1, 5 },
                new[] { 4, 1, 0, 1 },
                new[] { 10, 100, 10, 100 },
                new[] { true, false, true, false },
                new[] { 51, 7, 51, 7 },
                out ids, out counts, out incs);
            Assert(plan == 3, "mixed backpack yields 3 grids (1 mineral + 2 building levels)");
            Assert(ids[0] == 1001 && counts[0] == 10 && incs[0] == 2, "mineral group comes first by proto index");
            Assert(ids[1] == 2104 && incs[1] == 0 && ids[2] == 2104 && incs[2] == 4, "building levels stay split");

            // Determinism: identical input yields identical output.
            int planA = Plan(new[] { 2104, 2104 }, new[] { 1, 1 }, new[] { 4, 0 }, new[] { 5, 5 },
                new[] { true, true }, new[] { 51, 51 }, out ids, out counts, out incs);
            int[] firstIds = (int[])ids.Clone();
            int[] firstCounts = (int[])counts.Clone();
            int[] firstIncs = (int[])incs.Clone();
            int planB = Plan(new[] { 2104, 2104 }, new[] { 1, 1 }, new[] { 4, 0 }, new[] { 5, 5 },
                new[] { true, true }, new[] { 51, 51 }, out ids, out counts, out incs);
            Assert(planA == planB && planB == 2, "same input plans the same grid count");
            for (int i = 0; i < planB; i++)
            {
                Assert(firstIds[i] == ids[i] && firstCounts[i] == counts[i] && firstIncs[i] == incs[i],
                    "same input plans the same layout at grid " + i);
            }
        }

        /// <summary>
        /// v0.11.3 (P4 containers): the same regroup plan must cover plain storage containers, not
        /// only the player backpack. A container holding 1x0pt + 1x4pt + 2x16pt (8 pts/item) must
        /// sort into three groups, level ascending, totals conserved - which is exactly the plan the
        /// Sort prefix writes for a Default container once isolateStorageContainers is on.
        /// </summary>
        internal static void CheckContainerSortIsolation()
        {
            int plan = Plan(
                new[] { 2104, 2104, 2104 },
                new[] { 1, 1, 2 },
                new[] { 0, 4, 16 },
                new[] { 20, 20, 20 },
                new[] { true, true, true },
                new[] { 0, 0, 0 },
                out int[] ids, out int[] counts, out int[] incs);
            Assert(plan == 3, "container sort: 0/4/8 points per item must produce 3 groups, got " + plan);
            Assert(ids[0] == 2104 && counts[0] == 1 && incs[0] == 0, "container sort: first group must be the 0-point stack");
            Assert(ids[1] == 2104 && counts[1] == 1 && incs[1] == 4, "container sort: second group must be the 4-point stack");
            Assert(ids[2] == 2104 && counts[2] == 2 && incs[2] == 16, "container sort: third group must be the 2x8-point stack");
            Assert(counts[0] + counts[1] + counts[2] == 4, "container sort: counts must be conserved");
            Assert(incs[0] + incs[1] + incs[2] == 20, "container sort: points must be conserved");
        }
        private static int Plan(int[] itemIds, int[] counts, int[] incs, int[] stackSizes,
            bool[] isBuilding, int[] protoIndexes,
            out int[] outIds, out int[] outCounts, out int[] outIncs)
        {
            return QualityMath.PlanSortedLayout(itemIds, counts, incs, stackSizes, isBuilding, protoIndexes,
                out outIds, out outCounts, out outIncs);
        }
    }
}
