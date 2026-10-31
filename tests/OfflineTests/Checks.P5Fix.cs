using System;

namespace DSPQualitySystem.OfflineTests
{
    /// <summary>
    /// v0.16.0 pure helpers: Bug 3 (assembler pool accounting with picks) and Bug 2
    /// (instant-build per-item point conversion).
    /// </summary>
    internal static partial class Program
    {
        private static void CheckProductDelta()
        {
            // Plain production, no picks.
            Assert(QualityMath.ProducedDelta(5, 0) == 5, "delta: 5 produced, no picks -> 5");
            Assert(QualityMath.ProducedDelta(1, 0) == 1, "delta: 1 produced -> 1");
            Assert(QualityMath.ProducedDelta(0, 0) == 0, "delta: nothing produced -> 0");

            // Picks cancel production in the net produced[] change; adding them back recovers it.
            Assert(QualityMath.ProducedDelta(0, 1) == 1, "delta: +1 picked, net 0 -> 1 (extra product)");
            Assert(QualityMath.ProducedDelta(0, 10) == 10, "delta: 10 picks masked 10 produced -> 10");
            Assert(QualityMath.ProducedDelta(-1, 1) == 0, "delta: net -1 + 1 pick -> 0");
            Assert(QualityMath.ProducedDelta(-3, 3) == 0, "delta: fully masked, no net production -> 0");
            Assert(QualityMath.ProducedDelta(2, 1) == 3, "delta: 2 net + 1 pick -> 3");
            Assert(QualityMath.ProducedDelta(-5, 2) == 0, "delta: picks cannot make a negative net positive");

            // Degenerate values stay safe.
            Assert(QualityMath.ProducedDelta(0, -1) == 0, "delta: negative picks ignored");
            Assert(QualityMath.ProducedDelta(-100, 0) == 0, "delta: negative net clamps to 0");
            Assert(QualityMath.ProducedDelta(int.MaxValue, int.MaxValue) == int.MaxValue,
                "delta: overflow stays at int.MaxValue");

            // The 增产 case: one extra item per 10 cycles, one pick per tick.
            Assert(QualityMath.ProducedDelta(0, 1) == 1 && QualityMath.ProducedDelta(0, 0) == 0,
                "delta: only the tick that saw the pick holds credit");
        }

        private static void CheckInstantBuildPerItem()
        {
            // 4 items x 4 points = 16 total points -> 4 points per item.
            Assert(QualityMath.InstantBuildPerItem(16, 4) == 4, "instant: 16/4 -> 4");
            Assert(QualityMath.InstantBuildPerItem(40, 10) == 4, "instant: 40/10 -> 4");
            Assert(QualityMath.InstantBuildPerItem(10, 1) == 10, "instant: single item keeps 10");
            Assert(QualityMath.InstantBuildPerItem(7, 2) == 3, "instant: 7/2 truncates to 3");

            // Mixed stacks: total / count is the per-item average (floor).
            Assert(QualityMath.InstantBuildPerItem(9 + 3 + 0 + 10, 4) == 5, "instant: mixed stack average");
            Assert(QualityMath.InstantBuildPerItem(3, 4) == 0, "instant: average below 1 -> 0");

            // Clamps and degenerate values.
            Assert(QualityMath.InstantBuildPerItem(999, 2) == QualityMath.IncPerItemMax,
                "instant: per-item clamps to 10");
            Assert(QualityMath.InstantBuildPerItem(0, 5) == 0, "instant: zero points -> 0");
            Assert(QualityMath.InstantBuildPerItem(-5, 5) == 0, "instant: negative points -> 0");
            Assert(QualityMath.InstantBuildPerItem(20, 0) == 0, "instant: zero count -> 0");
            Assert(QualityMath.InstantBuildPerItem(20, -3) == 0, "instant: negative count -> 0");
            Assert(QualityMath.InstantBuildPerItem(20, 2) <= QualityMath.IncPerItemMax,
                "instant: never above the item cap");
        }
    }
}
