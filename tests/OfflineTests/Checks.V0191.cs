using System;
using DSPQualitySystem;

namespace DSPQualitySystem.OfflineTests
{
    /// <summary>
    /// v0.19.1 offline checks for the batch input level of an assembler.
    ///
    /// User report: a 10 point assembler whose ingredients are 6 points produced a handful of
    /// 7 point products. Root cause: vanilla derives the batch level from incServed[]/served[]
    /// with a truncating integer division, so a slot that serves a few low level items next to
    /// the good ones drags the whole batch down one level (5.8 -> 5 -> 5 + 2 = 7).
    /// QualityMath.BatchInputLevel rounds half up instead; these checks pin that down together
    /// with the clamps that keep a hostile save from producing an out-of-range level.
    /// </summary>
    internal static partial class Program
    {
        private static void CheckBatchInputLevel()
        {
            const int max = 10;

            // Uniform ingredients: the level is exact, rounding never moves it.
            Assert(QualityMath.BatchInputLevel(0, 1, max) == 0, "batch(0/1)");
            Assert(QualityMath.BatchInputLevel(24, 4, max) == 6, "batch(24/4) = 6");
            Assert(QualityMath.BatchInputLevel(30, 5, max) == 6, "batch(30/5) = 6");
            Assert(QualityMath.BatchInputLevel(40, 4, max) == 10, "batch(40/4) = 10");

            // The reported case: 6 point ingredients that average 5.8 because two of them are
            // lower level. Truncation gives 5 (product 7), rounding gives 6 (product 8).
            Assert((29 / 5) == 5, "vanilla truncation of 29/5 is 5");
            Assert(QualityMath.BatchInputLevel(29, 5, max) == 6, "batch(29/5) = 6 (rounds up)");

            // Threshold is 0.5: exactly half still rounds up, just below half does not.
            Assert(QualityMath.BatchInputLevel(9, 4, max) == 2, "batch(9/4) = 2.25 -> 2");
            Assert(QualityMath.BatchInputLevel(10, 4, max) == 3, "batch(10/4) = 2.5 -> 3");
            Assert(QualityMath.BatchInputLevel(7, 2, max) == 4, "batch(7/2) = 3.5 -> 4");

            // Clamps: never above the item cap, never below zero, and a zero serve count is 0.
            Assert(QualityMath.BatchInputLevel(99, 1, max) == max, "batch clamps to max");
            Assert(QualityMath.BatchInputLevel(-5, 2, max) == 0, "negative inc clamps to 0");
            Assert(QualityMath.BatchInputLevel(50, 0, max) == 0, "zero served count is 0");
            Assert(QualityMath.BatchInputLevel(50, -3, max) == 0, "negative served count is 0");
            Assert(QualityMath.BatchInputLevel(3, 1, 0) == 0, "max level 0 wins over the average");

            // A real mixed slot: four items, one carrying 6, the rest unsprayed (6 total).
            Assert(QualityMath.BatchInputLevel(6, 4, max) == 2, "batch(6/4) = 1.5 -> 2");

            // Sanity: for every served count 1..40 and total 0..(served*10) the rounded result is
            // within one level of the truncating result and never outside 0..max.
            for (int served = 1; served <= 40; served++)
            {
                for (int inc = 0; inc <= served * max; inc++)
                {
                    int rounded = QualityMath.BatchInputLevel(inc, served, max);
                    int truncated = Math.Min(inc / served, max);
                    if (rounded < 0 || rounded > max)
                    {
                        Assert(false, "batch range violated at inc=" + inc + " served=" + served
                            + " -> " + rounded);
                    }
                    if (rounded < truncated || rounded > truncated + 1)
                    {
                        Assert(false, "batch rounding drifted at inc=" + inc + " served=" + served
                            + " -> " + rounded + " vs " + truncated);
                    }
                }
            }
        }
    }
}
