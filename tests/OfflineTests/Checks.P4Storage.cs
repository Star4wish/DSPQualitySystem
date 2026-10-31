using System;

namespace DSPQualitySystem.OfflineTests
{
    /// <summary>
    /// v0.13.0 (task-17 / C-1): pure math behind the storage "quality grid" rule - how many extra
    /// usable grids a storage container unlocks from its building quality points, and the bounds
    /// that guarantee the rule can never shrink or corrupt a vanilla container.
    /// </summary>
    internal static partial class Program
    {
        private static void CheckStorageGrids()
        {
            // Default config: 0.5 grids per point, at most 20 extra, on a 30-grid small chest.
            Assert(QualityMath.StorageExtraGrids(10, 0.5f, 20, 30) == 5, "10 points on a small chest add 5 grids");
            Assert(QualityMath.StorageExtraGrids(10, 0.5f, 20, 100) == 5, "same bonus on a large chest is 5 grids");
            Assert(QualityMath.StorageExtraGrids(3, 0.5f, 20, 30) == 1, "fractional bonus floors (1.5 -> 1)");
            Assert(QualityMath.StorageExtraGrids(1, 0.49f, 20, 30) == 0, "a sub-grid bonus floors to zero");
            Assert(QualityMath.StorageExtraGrids(1, 1f, 20, 30) == 1, "one point with 1 grid/point adds one grid");
            Assert(QualityMath.StorageExtraGrids(20, 1f, 20, 30) == 20, "20 points reach the configured maximum");

            // Degenerate input must be a strict no-op, never a negative grid count.
            Assert(QualityMath.StorageExtraGrids(0, 0.5f, 20, 30) == 0, "0 points add nothing");
            Assert(QualityMath.StorageExtraGrids(-5, 0.5f, 20, 30) == 0, "negative points add nothing");
            Assert(QualityMath.StorageExtraGrids(10, 0f, 20, 30) == 0, "perPoint 0 disables the bonus");
            Assert(QualityMath.StorageExtraGrids(10, -0.5f, 20, 30) == 0, "negative perPoint adds nothing");
            Assert(QualityMath.StorageExtraGrids(10, float.NaN, 20, 30) == 0, "NaN perPoint adds nothing");
            Assert(QualityMath.StorageExtraGrids(10, float.PositiveInfinity, 20, 30) == 0, "infinite perPoint adds nothing");
            Assert(QualityMath.StorageExtraGrids(10, 0.5f, 0, 30) == 0, "maxExtra 0 disables the bonus");
            Assert(QualityMath.StorageExtraGrids(10, 0.5f, -3, 30) == 0, "negative maxExtra adds nothing");
            Assert(QualityMath.StorageExtraGrids(10, 0.5f, 20, 0) == 0, "unknown base size adds nothing");

            // Two independent ceilings: the configured maximum and the base grid count (+100% cap).
            Assert(QualityMath.StorageExtraGrids(100, 0.5f, 20, 100) == 20, "the configured maximum caps a huge bonus");
            Assert(QualityMath.StorageExtraGrids(100, 0.5f, 200, 30) == 30, "the bonus never exceeds the base grid count");
            Assert(QualityMath.StorageExtraGrids(60, 0.5f, 20, 30) == 20, "raw >= maximum still returns the maximum");
            Assert(QualityMath.StorageExtraGrids(100, 1f, 50, 30) == 30, "raw >= maximum is clamped by the base grid count");

            // Monotonic: more points never mean fewer grids, and the value never goes negative.
            int previous = 0;
            for (int points = 0; points <= 64; points++)
            {
                int extra = QualityMath.StorageExtraGrids(points, 0.5f, 20, 100);
                Assert(extra >= 0, "extra grids are never negative at " + points + " points");
                Assert(extra >= previous, "extra grids are monotonic at " + points + " points");
                Assert(extra <= 20 && extra <= 100, "extra grids stay inside both ceilings at " + points + " points");
                previous = extra;
            }
        }

        private static void CheckStorageTotalGrids()
        {
            Assert(QualityMath.StorageTotalGrids(30, 5) == 35, "30 base + 5 extra = 35 grids");
            Assert(QualityMath.StorageTotalGrids(30, 0) == 30, "zero extra keeps the vanilla size");
            Assert(QualityMath.StorageTotalGrids(30, -4) == 30, "negative extra never shrinks a container");
            Assert(QualityMath.StorageTotalGrids(0, 5) == 0, "unknown base size stays 0");
            Assert(QualityMath.StorageTotalGrids(-7, 5) == 0, "negative base size clamps to 0");

            // The runtime repair loop uses this single definition of "out of sync".
            Assert(!QualityMath.StorageGridsOutOfSync(30, 35, 10, 0.5f, 20), "35 grids at 10 points need no reshape");
            Assert(QualityMath.StorageGridsOutOfSync(30, 30, 10, 0.5f, 20), "30 grids at 10 points must grow");
            Assert(QualityMath.StorageGridsOutOfSync(30, 36, 10, 0.5f, 20), "36 grids at 10 points must shrink back");
            Assert(!QualityMath.StorageGridsOutOfSync(30, 30, 0, 0.5f, 20), "0 points on a vanilla container is in sync");
            Assert(!QualityMath.StorageGridsOutOfSync(0, 35, 10, 0.5f, 20), "unknown base size is never out of sync");
        }
    }
}
