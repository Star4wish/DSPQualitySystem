using System;
using DSPQualitySystem;

namespace DSPQualitySystem.OfflineTests
{
    /// <summary>
    /// v0.20.0 offline checks for <see cref="QualityMath.TakeStackAbove"/>.
    ///
    /// The helper is the pure core of two live fixes:
    ///  * issue 2 - OrbitalRing drains `produced[]` into its own shared/elevator storage, so the
    ///    points of a whole product stack have to leave the DSPQS pool in one call;
    ///  * issue 3 - orbital collectors add a whole transfer of items to one station slot per tick.
    /// It must conserve points (taken + remaining == added), never hand out more than
    /// Cargo.kIncLevelMax per item, and, for the OrbitalRing smelters that already add a flat
    /// 4 points/item themselves, keep low quality in the pool instead of consuming it.
    /// </summary>
    internal static partial class Program
    {
        private static void CheckStackTake()
        {
            const int max = 10;
            int inc;
            int count;
            int points;

            // Uniform pool: 60 points over 10 items = 6 per item. Taking 4 leaves 36 / 6.
            points = QualityMath.TakeStackAbove(60, 10, 4, 0, max, out inc, out count);
            Assert(points == 24, "stack take 4 of 60/10 -> 24 points, got " + points);
            Assert(inc == 36 && count == 6, "stack take leaves 36/6, got " + inc + "/" + count);

            // Degenerate inputs never move the pool.
            points = QualityMath.TakeStackAbove(0, 0, 5, 0, max, out inc, out count);
            Assert(points == 0 && inc == 0 && count == 0, "empty pool take is a no-op");
            points = QualityMath.TakeStackAbove(50, 5, 0, 0, max, out inc, out count);
            Assert(points == 0 && inc == 50 && count == 5, "count 0 take is a no-op");
            points = QualityMath.TakeStackAbove(-7, 3, 2, 0, max, out inc, out count);
            Assert(points == 0 && inc == 0 && count == 3, "negative pool is clamped to 0");

            // Asking for more products than the pool holds is clamped to the pool size.
            points = QualityMath.TakeStackAbove(30, 3, 99, 0, max, out inc, out count);
            Assert(points == 30 && inc == 0 && count == 0, "count above poolCount is clamped");

            // A single item can never exceed maxLevel even if the pool average does.
            points = QualityMath.TakeStackAbove(100, 1, 1, 0, max, out inc, out count);
            Assert(points == max && inc == 90 && count == 0,
                "one item is clamped to maxLevel, got " + points + " inc=" + inc + " count=" + count);

            // Smelter floor: a floor below the average (6) takes normally.
            points = QualityMath.TakeStackAbove(60, 10, 5, 4, max, out inc, out count);
            Assert(points == 30 && inc == 30 && count == 5,
                "floor 4 < average 6 takes 5 items, got " + points + " inc=" + inc + " count=" + count);

            // A floor equal to the average (6) refuses: only quality strictly above the flat 4
            // (or any other floor) is handed out.
            points = QualityMath.TakeStackAbove(60, 10, 5, 6, max, out inc, out count);
            Assert(points == 0 && inc == 60 && count == 10, "floor equal to the average keeps the pool");

            // Floor above the average also keeps the pool.
            points = QualityMath.TakeStackAbove(60, 10, 5, 9, max, out inc, out count);
            Assert(points == 0 && inc == 60 && count == 10, "floor above the average keeps the pool");

            // Floor 3 passes the 6 average and takes normally too.
            points = QualityMath.TakeStackAbove(60, 10, 5, 3, max, out inc, out count);
            Assert(points == 30 && inc == 30 && count == 5, "floor 3 < average 6 takes normally");

            // Conservation sweep: random pools/stacks/floors, taken + remaining must equal added,
            // and the remaining count must account for exactly the taken items.
            var rng = new Random(20201121);
            for (int i = 0; i < 200000; i++)
            {
                int poolCount = rng.Next(0, 400);
                int poolInc = rng.Next(0, poolCount * max + 1);
                int requested = rng.Next(0, 60);
                int floor = rng.Next(0, max + 1);

                points = QualityMath.TakeStackAbove(poolInc, poolCount, requested, floor, max,
                    out inc, out count);
                int taken = poolCount - count;

                Assert(points >= 0, "negative points at op " + i);
                Assert(inc >= 0 && count >= 0, "negative remainder at op " + i);
                Assert(points + inc == poolInc, "points not conserved at op " + i
                    + ": taken=" + points + " remaining=" + inc + " added=" + poolInc);
                Assert(taken <= requested, "took more items than requested at op " + i);
                Assert(points <= taken * max, "more points than items*max at op " + i);
                if (floor > 0 && taken > 0)
                {
                    Assert(points / taken > floor, "took items at or below the floor at op " + i);
                }
            }
        }
    }
}
