using System;

namespace DSPQualitySystem.OfflineTests
{
    internal static partial class Program
    {
        /// <summary>
        /// v0.16.0 Bug 1 (large miner = station-type vein collector): the storage slot keeps the
        /// TOTAL quality points of the stack, so a transfer adds `count * perItem`, clamped to
        /// `count * IncPerItemMax`. Degenerate input must never lower the existing value.
        /// </summary>
        private static void CheckMinerCollectorInc()
        {
            // basic accumulation: new slot (0 points), 3 items with 1 point each.
            Assert(QualityMath.StationIncAfterTransfer(0, 3, 1, 3) == 3,
                "collector transfer: 0 + 3 items * 1 point = 3 points");
            // existing points are kept and extended.
            Assert(QualityMath.StationIncAfterTransfer(5, 2, 1, 10) == 7,
                "collector transfer: 5 existing + 2 items * 1 point = 7 points");
            Assert(QualityMath.StationIncAfterTransfer(4, 3, 2, 10) == 10,
                "collector transfer: 4 existing + 3 items * 2 points = 10 points");
            // one item can carry at most IncPerItemMax (10) points.
            Assert(QualityMath.StationIncAfterTransfer(0, 1, 99, 1) == QualityMath.IncPerItemMax,
                "collector transfer: per-item points are clamped to 10");
            // total points can never exceed count * 10 (whole stack at max quality).
            Assert(QualityMath.StationIncAfterTransfer(18, 2, 1, 2) == 20,
                "collector transfer: total points are clamped to count * 10");
            Assert(QualityMath.StationIncAfterTransfer(25, 10, 10, 3) == 30,
                "collector transfer: overfull slot clamps to count * 10");
            // count 0 is degenerate - the previous value wins.
            Assert(QualityMath.StationIncAfterTransfer(7, 2, 3, 0) == 7,
                "collector transfer: totalCount 0 keeps the previous points");
            // no transfer / no roll: unchanged and never negative.
            Assert(QualityMath.StationIncAfterTransfer(6, 0, 3, 10) == 6,
                "collector transfer: zero items adds nothing");
            Assert(QualityMath.StationIncAfterTransfer(6, -2, 3, 10) == 6,
                "collector transfer: negative items adds nothing");
            Assert(QualityMath.StationIncAfterTransfer(6, 2, 0, 10) == 6,
                "collector transfer: per-item 0 (roll missed) adds nothing");
            Assert(QualityMath.StationIncAfterTransfer(-5, 2, 3, 10) == 6,
                "collector transfer: negative current points are treated as 0");
            Assert(QualityMath.StationIncAfterTransfer(0, 1, 1, 1) == 1,
                "collector transfer: single full-quality item");
            // monotone in the added count.
            int last = -1;
            for (int count = 0; count <= 10; count++)
            {
                int inc = QualityMath.StationIncAfterTransfer(0, count, 1, 10);
                Assert(inc >= last, "collector transfer: points are monotone in the item count (" + count + ")");
                last = inc;
            }
            // a 0-point building (per-item 0) never gains points over repeated transfers.
            int zero = 0;
            for (int i = 0; i < 5; i++)
            {
                zero = QualityMath.StationIncAfterTransfer(zero, 10, 0, 10);
            }
            Assert(zero == 0, "collector transfer: a 0-point building keeps producing 0-point ore");
        }

        /// <summary>
        /// v0.16.0 Bug 1: the collector uses the same R-3 roll as the small miner, i.e. the per-item
        /// cap is MinerRollsOf(buildingInc) (floor(points / 2)), never more. The transfer therefore
        /// adds at most `count * MinerMaxInc(points)` points and never exceeds `count * 10`.
        /// </summary>
        private static void CheckMinerCollectorRollBounds()
        {
            for (int points = 0; points <= QualityMath.IncPerItemMax; points++)
            {
                int maxInc = QualityMath.MinerMaxInc(points);
                Assert(maxInc == points / 2,
                    "collector roll: max ore points == floor(building points / 2) for " + points);
                Assert(maxInc >= 0 && maxInc <= QualityMath.IncPerItemMax,
                    "collector roll: max ore points stay inside 0..10 for " + points);

                int inc = QualityMath.StationIncAfterTransfer(0, 10, maxInc, 10);
                int expected = 10 * maxInc;
                Assert(inc == expected,
                    "collector roll: 10 items at the roll cap of " + points + " points = " + expected);

                float chance = QualityMath.MinerChance(points, 2f);
                if (points <= 0)
                {
                    Assert(chance <= 0f, "collector roll: 0 building points give no roll chance");
                }
                else
                {
                    Assert(chance > 0f && chance <= 100f,
                        "collector roll: roll chance stays inside (0, 100] for " + points);
                }
            }

            // 10 points -> 5 rolls x up to 2% (curve-independent constant 2%/point) => 5 points max,
            // which is the documented "max ore level never above half the miner points" rule.
            Assert(QualityMath.MinerMaxInc(10) == 5,
                "collector roll: a 10-point vein collector caps ore at 5 points");
            Assert(QualityMath.MinerMaxInc(1) == 0,
                "collector roll: 1 building point gives no roll");
        }
    }
}
