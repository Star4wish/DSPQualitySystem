using System;
using DSPQualitySystem;

namespace DSPQualitySystem.OfflineTests
{
    internal static partial class Program
    {
        /// <summary>
        /// v0.10.0 (P4): the merge predicate that decides whether a player-backpack grid may take
        /// an incoming building stack. Vanilla inc arguments are TOTAL points of the stack, so the
        /// quality level is inc/count. Covers the 4 required classes: same level merges, different
        /// level does not, non-building items / non-player storages stay vanilla, empty grids accept
        /// any level. Also models the capacity accounting of the isolation add loop to prove the
        /// pure helpers never lose or duplicate items at a grid boundary.
        /// </summary>
        private static void CheckIsolationMerge()
        {
            // Different item id never merges (vanilla rule kept in front of the quality rule).
            Assert(!QualityMath.SharesStack(1001, 1002, 10, 40, 5, 20, true, true),
                "different item id must not merge");

            // Non-building items always merge, even with different levels (vanilla behaviour).
            Assert(QualityMath.SharesStack(1001, 1001, 10, 0, 5, 20, false, true),
                "non-building items must merge like vanilla");
            // Non-player storages always merge, even for building items.
            Assert(QualityMath.SharesStack(1001, 1001, 10, 0, 5, 20, true, false),
                "non-player storages must merge like vanilla");

            // Building item in the player inventory: same level merges, different level does not.
            Assert(QualityMath.SharesStack(1001, 1001, 10, 40, 5, 20, true, true),
                "same level (4 pts/item in both stacks) must merge");
            // Explicit anti-fragmentation cases: foundations/belts/sorters are CanBuild items whose
            // level is normally 0. Two zero-point stacks must still merge, 0 vs 1 must not.
            Assert(QualityMath.SharesStack(1001, 1001, 10, 0, 5, 0, true, true),
                "two zero-point stacks (belts/sorters/foundations) must merge");
            Assert(!QualityMath.SharesStack(1001, 1001, 10, 0, 5, 5, true, true), "0 vs 1 point must not merge");
            Assert(!QualityMath.SharesStack(1001, 1001, 10, 0, 5, 20, true, true), "0 vs 4 points must not merge");
            Assert(!QualityMath.SharesStack(1001, 1001, 10, 40, 5, 0, true, true), "4 vs 0 points must not merge");
            Assert(!QualityMath.SharesStack(1001, 1001, 10, 40, 5, 10, true, true), "4 vs 2 points must not merge");
            // Cross multiplication keeps the comparison exact even for fractional splits:
            // 10 points over 3 items and 100 points over 30 items are the same level.
            Assert(QualityMath.SharesStack(1001, 1001, 3, 10, 30, 100, true, true),
                "equal level must merge even when inc/count is fractional");
            Assert(!QualityMath.SharesStack(1001, 1001, 3, 10, 3, 9, true, true),
                "fractional level 10/3 vs 3/1 must not merge");
            // An empty grid accepts whatever level arrives (fresh stack).
            Assert(QualityMath.SharesStack(1001, 1001, 0, 0, 7, 28, true, true), "empty grid must accept any level");

            // Capacity model: grid0 is a full 50-count 0-point stack, add 30 items with 4 points each
            // (inc = 120 total). The isolation rule must open a second grid, leave the first untouched
            // and lose nothing.
            var grids = new[]
            {
                new GridModel { ItemId = 1001, Count = 50, Inc = 0, StackSize = 50 },
                new GridModel { ItemId = 0, Count = 0, Inc = 0, StackSize = 0 }
            };
            int added = AddModel(grids, 1001, 30, 120);
            Assert(added == 30, "model: all 30 items must be placed, got " + added);
            Assert(grids[0].Count == 50 && grids[0].Inc == 0, "model: full 0-point grid must stay untouched");
            Assert(grids[1].ItemId == 1001 && grids[1].Count == 30 && grids[1].Inc == 120,
                "model: second grid must hold 30 items / 120 total points (4 per item)");
            // Same-level top-up on the second grid merges into it.
            int added2 = AddModel(grids, 1001, 10, 40);
            Assert(added2 == 10 && grids[1].Count == 40 && grids[1].Inc == 160,
                "model: same-level top-up must merge (40 items / 160 total points)");
            // A full grid must not swallow the incoming stack.
            grids[1].Count = 50;
            grids[1].Inc = 200;
            int added3 = AddModel(grids, 1001, 10, 40);
            Assert(added3 == 0, "model: full grid must not swallow items, got " + added3);
        }

        /// <summary>
        /// v0.11.3 (P4 containers): the pure gate that decides which storages keep building stacks
        /// of different quality in separate grids. The player backpack always isolates; other
        /// storages isolate only for a plain EStorageType.Default container while
        /// isolateStorageContainers is on. Fuel/ammo/bomb/fighter (non-Default) types never do.
        /// </summary>
        internal static void CheckShouldIsolateStorage()
        {
            Assert(QualityMath.ShouldIsolateStorage(true, 0, 50, false),
                "player inventory must isolate regardless of isolateStorageContainers");
            Assert(QualityMath.ShouldIsolateStorage(false, 0, 30, true),
                "Default storage container must isolate when isolateStorageContainers is on");
            Assert(!QualityMath.ShouldIsolateStorage(false, 0, 30, false),
                "Default storage container must stay vanilla when isolateStorageContainers is off");
            Assert(!QualityMath.ShouldIsolateStorage(false, 1, 30, true),
                "non-Default storage types (Fuel/Ammo/Bomb/Fighter/Filtered) must never isolate");
            Assert(!QualityMath.ShouldIsolateStorage(false, 1, 30, false),
                "non-Default storage types must stay vanilla with the switch off too");
            Assert(!QualityMath.ShouldIsolateStorage(false, 0, 0, true),
                "a storage with no grids cannot isolate");
            Assert(!QualityMath.ShouldIsolateStorage(true, 0, 0, true),
                "the player inventory always has grids; size 0 stays non-isolating defensively");
        }

        /// <summary>
        /// v0.11.3 (P4 containers): the isolation add model inside a plain storage container. An
        /// empty container receives a 0-point building stack and a 4-point one: it must open two
        /// grids. With isolateStorageContainers off (isolate=false) the two stacks merge into one
        /// grid and the points are added, exactly like v0.11.2 outside the backpack.
        /// </summary>
        internal static void CheckContainerIsolation()
        {
            var container = new[]
            {
                new GridModel { ItemId = 0, Count = 0, Inc = 0, StackSize = 0 },
                new GridModel { ItemId = 0, Count = 0, Inc = 0, StackSize = 0 },
                new GridModel { ItemId = 0, Count = 0, Inc = 0, StackSize = 0 }
            };
            int a1 = AddModel(container, 2104, 1, 0);
            int a2 = AddModel(container, 2104, 1, 4);
            Assert(a1 == 1 && a2 == 1, "container: both building items must be accepted");
            Assert(container[0].Count == 1 && container[0].Inc == 0,
                "container: the 0-point stack must stay in its own grid");
            Assert(container[1].Count == 1 && container[1].Inc == 4,
                "container: the 4-point stack must open a second grid");
            int a3 = AddModel(container, 2104, 1, 0);
            Assert(a3 == 1 && container[0].Count == 2 && container[0].Inc == 0,
                "container: same-level 0-point top-up must merge into the 0-point grid");

            // isolateStorageContainers off: v0.11.2 semantics outside the backpack (vanilla merge).
            var vanilla = new[]
            {
                new GridModel { ItemId = 0, Count = 0, Inc = 0, StackSize = 0 },
                new GridModel { ItemId = 0, Count = 0, Inc = 0, StackSize = 0 }
            };
            AddModel(vanilla, 2104, 1, 0, false);
            AddModel(vanilla, 2104, 1, 4, false);
            Assert(vanilla[0].Count == 2 && vanilla[0].Inc == 4 && vanilla[1].ItemId == 0,
                "container off: the two stacks must merge into one grid with points added");
        }
        /// <summary>
        /// v0.10.0 (P4): split_inc equivalent (asm_full.cs:170203-170216). It returns the TOTAL inc
        /// delta for the filled part and mutates the remaining count/points, exactly like vanilla.
        /// </summary>
        private static void CheckSplitInc()
        {
            int n = 10;
            int m = 20;
            int perItem = QualityMath.SplitInc(ref n, ref m, 4);
            Assert(perItem == 8, "SplitInc(10,20,4) expected 8 total inc delta, got " + perItem);
            Assert(n == 6, "SplitInc leftover count expected 6, got " + n);
            Assert(m == 12, "SplitInc leftover points expected 12, got " + m);

            // Remainder branch: perItem=2, remainder goes negative after the count shrink.
            int n2 = 3;
            int m2 = 7;
            int per2 = QualityMath.SplitInc(ref n2, ref m2, 1);
            Assert(per2 == 2, "SplitInc(3,7,1) expected 2, got " + per2);
            Assert(n2 == 2, "SplitInc(3,7,1) leftover count expected 2, got " + n2);
            Assert(m2 == 5, "SplitInc(3,7,1) leftover points expected 5, got " + m2);

            // Degenerate inputs must be inert.
            int n3 = 0;
            int m3 = 5;
            Assert(QualityMath.SplitInc(ref n3, ref m3, 0) == 0, "SplitInc with n=0 must return 0");
            int n4 = -2;
            int m4 = 5;
            Assert(QualityMath.SplitInc(ref n4, ref m4, 1) == 0, "SplitInc with negative n must return 0");
            int n5 = 5;
            int m5 = 5;
            Assert(QualityMath.SplitInc(ref n5, ref m5, 0) == 0, "SplitInc with p=0 must return 0");

            // Full split of one level: 8 items / 8 points (1 per item) into 8 free slots.
            int n6 = 8;
            int m6 = 8;
            int per6 = QualityMath.SplitInc(ref n6, ref m6, 8);
            Assert(per6 == 8, "SplitInc(8,8,8) expected 8 total inc delta, got " + per6);
            Assert(n6 == 0 && m6 == 0, "SplitInc(8,8,8) must consume all items and points");
        }

        /// <summary>Offline mirror of the isolation add loop, using only the shipped pure helpers.</summary>
        private static int AddModel(GridModel[] grids, int itemId, int count, int inc, bool isolate = true)
        {
            int remaining = count;
            int remainingInc = inc;
            int stackSize = 0;
            int size = grids.Length;
            for (int i = 0; i < size && remaining > 0; i++)
            {
                if (grids[i].ItemId != itemId)
                {
                    continue;
                }
                if (!QualityMath.SharesStack(itemId, grids[i].ItemId, grids[i].Count, grids[i].Inc,
                        count, inc, true, isolate))
                {
                    continue;
                }
                FillModel(ref grids[i], ref stackSize, ref remaining, ref remainingInc);
            }
            for (int j = 0; j < size && remaining > 0; j++)
            {
                if (grids[j].ItemId == 0 && grids[j].Count == 0)
                {
                    if (stackSize == 0)
                    {
                        stackSize = 50;
                    }
                    grids[j].ItemId = itemId;
                    grids[j].StackSize = stackSize;
                }
                if (grids[j].ItemId != itemId)
                {
                    continue;
                }
                if (!QualityMath.SharesStack(itemId, grids[j].ItemId, grids[j].Count, grids[j].Inc,
                        count, inc, true, isolate))
                {
                    continue;
                }
                FillModel(ref grids[j], ref stackSize, ref remaining, ref remainingInc);
            }
            return count - remaining;
        }

        private static void FillModel(ref GridModel grid, ref int stackSize, ref int remaining, ref int remainingInc)
        {
            if (stackSize == 0)
            {
                stackSize = grid.StackSize > 0 ? grid.StackSize : 50;
            }
            int room = stackSize - grid.Count;
            if (room <= 0)
            {
                return;
            }
            if (remaining <= room)
            {
                grid.Count += remaining;
                grid.Inc += remainingInc;
                remaining = 0;
                remainingInc = 0;
                return;
            }
            grid.Count = stackSize;
            grid.Inc += QualityMath.SplitInc(ref remaining, ref remainingInc, room);
        }

        private struct GridModel
        {
            public int ItemId;
            public int Count;
            public int Inc;
            public int StackSize;
        }
    }
}
