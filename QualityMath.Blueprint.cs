using System;

namespace DSPQualitySystem
{
    /// <summary>
    /// Pure helpers for the "prefer the highest quality building item" consumption feature (v0.11.5).
    /// The building item that actually pays for a building decides its quality.
    ///
    /// v0.15.0: the blueprint-carried-quality experiment was removed, so the blueprint marker helper
    /// (<c>ShouldApplyBlueprintInc</c>) is gone. Only the generic consumption helpers below remain; they
    /// are game-independent and fully offline testable.
    /// </summary>
    public static partial class QualityMath
    {
        /// <summary>
        /// Points per item of a grid/stack; 0 for an empty or invalid stack.
        ///
        /// v0.17.1: this is also the one true conversion between a vanilla cargo/storage inc (the
        /// TOTAL point pool of the whole stack: merging stacks does inc += inc,
        /// Assembly-CSharp.cs:28583) and the per-item points every quality rule in this mod talks
        /// about. Belt rules must compare PerItemPoints(cargo.inc, cargo.stack) against their
        /// minimum, never the raw total - otherwise a cheap multi-piece stack carrying a few stray
        /// points passes any threshold.
        /// </summary>
        public static int PerItemPoints(int totalInc, int count)
        {
            if (count <= 0 || totalInc <= 0)
            {
                return 0;
            }
            return totalInc / count;
        }

        /// <summary>
        /// Index of the best matching grid: highest exact points per item (fractional levels are
        /// compared by cross-multiplication, not by truncated per-item points), ties -> lowest index.
        /// Returns -1 when nothing matches or the arrays are unusable.
        /// </summary>
        public static int ChooseBestMatchGrid(int[] itemIds, int[] counts, int[] incs, int itemId)
        {
            if (itemIds == null || counts == null || incs == null || itemId <= 0)
            {
                return -1;
            }
            int length = itemIds.Length;
            if (counts.Length < length)
            {
                length = counts.Length;
            }
            if (incs.Length < length)
            {
                length = incs.Length;
            }
            int best = -1;
            int bestInc = 0;
            int bestCount = 0;
            for (int i = 0; i < length; i++)
            {
                if (itemIds[i] != itemId || counts[i] <= 0)
                {
                    continue;
                }
                int inc = incs[i] < 0 ? 0 : incs[i];
                if (best < 0)
                {
                    best = i;
                    bestInc = inc;
                    bestCount = counts[i];
                    continue;
                }
                long candidate = (long)inc * bestCount;
                long current = (long)bestInc * counts[i];
                if (candidate > current)
                {
                    best = i;
                    bestInc = inc;
                    bestCount = counts[i];
                }
            }
            return best;
        }

        /// <summary>True when the backpack grid has strictly higher points per item than the hand stack.</summary>
        public static bool ShouldSwapHand(int handInc, int handCount, int bestInc, int bestCount)
        {
            if (handCount <= 0 || bestCount <= 0)
            {
                return false;
            }
            long hand = (long)(handInc < 0 ? 0 : handInc) * bestCount;
            long best = (long)(bestInc < 0 ? 0 : bestInc) * handCount;
            return best > hand;
        }
    }
}