using System;

namespace DSPQualitySystem
{
    /// <summary>
    /// v0.13.0 (task-17 / C-1): pure helpers behind the "quality grid" rule for storage
    /// containers. A storage container (small chest = 30 grids, large chest = 100 grids, ...)
    /// gains extra usable grids while its building quality points are above zero:
    ///
    ///     extra = floor(qualityPoints * gridsPerPoint), clamped to [0, maxExtra]
    ///     extra is additionally clamped to the vanilla base grid count (never more than +100%).
    ///
    /// Kept free of Unity/game types so tests\OfflineTests links this very file.
    /// </summary>
    public static partial class QualityMath
    {
        /// <summary>
        /// Number of extra storage grids unlocked by <paramref name="qualityPoints"/> points.
        /// Returns 0 (never a negative value) for any disabled / degenerate input, which makes the
        /// patched code path a strict no-op. <paramref name="baseGrids"/> guards against a config
        /// that would more than double a container; pass 0 only when the base size is unknown.
        /// </summary>
        public static int StorageExtraGrids(int qualityPoints, float gridsPerPoint, int maxExtra, int baseGrids)
        {
            if (qualityPoints <= 0 || maxExtra <= 0 || baseGrids <= 0)
            {
                return 0;
            }
            if (float.IsNaN(gridsPerPoint) || float.IsInfinity(gridsPerPoint) || gridsPerPoint <= 0f)
            {
                return 0;
            }
            double raw = qualityPoints * (double)gridsPerPoint;
            if (raw >= maxExtra)
            {
                return maxExtra > baseGrids ? baseGrids : maxExtra;
            }
            int extra = (int)Math.Floor(raw);
            if (extra < 0)
            {
                return 0;
            }
            if (extra > maxExtra)
            {
                extra = maxExtra;
            }
            if (extra > baseGrids)
            {
                extra = baseGrids;
            }
            return extra;
        }

        /// <summary>Total usable grids of a container: vanilla base size plus the quality bonus.</summary>
        public static int StorageTotalGrids(int baseGrids, int extraGrids)
        {
            if (baseGrids <= 0)
            {
                return baseGrids < 0 ? 0 : baseGrids;
            }
            if (extraGrids <= 0)
            {
                return baseGrids;
            }
            return baseGrids + extraGrids;
        }

        /// <summary>
        /// True when a container must be re-shaped: the total grid count implied by the current
        /// quality points differs from the grid count currently allocated. Pure so the runtime
        /// repair loop and the offline tests share one definition of "out of sync".
        /// </summary>
        public static bool StorageGridsOutOfSync(int baseGrids, int currentGrids, int qualityPoints,
            float gridsPerPoint, int maxExtra)
        {
            if (baseGrids <= 0)
            {
                return false;
            }
            int want = StorageTotalGrids(baseGrids, StorageExtraGrids(qualityPoints, gridsPerPoint, maxExtra, baseGrids));
            return want != currentGrids;
        }
    }
}
