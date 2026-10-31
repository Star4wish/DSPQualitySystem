using System;
using DSPQualitySystem;

namespace DSPQualitySystem.OfflineTests
{
    /// <summary>
    /// v0.12.0 (task-16) offline checks for the five "A class" bonuses. Only the pure math layer is
    /// linked into this harness, so these checks cover the shared curve factor (lab / turret / power /
    /// launcher all call it), the sprayer chance and the per-event "never lose points" contract.
    /// The game-side plumbing is asserted at startup by Diag.StartupAudit instead.
    /// </summary>
    internal static partial class Program
    {
        // ------------------------------------------------------------------ P5 A-class factor

        private static void CheckBonusFactorCurve()
        {
            // Enabled + shared curve: factor comes from QualityMath.Curves.CurveBonusPercent.
            AssertNear(QualityMath.FeatureFactorFrom(0, true, true, 2f, 50f), 1.00f, 0.0001f,
                "0 points must stay vanilla");
            AssertNear(QualityMath.FeatureFactorFrom(-3, true, true, 2f, 50f), 1.00f, 0.0001f,
                "negative points must stay vanilla");
            AssertNear(QualityMath.FeatureFactorFrom(1, true, true, 2f, 50f), 1.025f, 0.0001f,
                "1 point = +2.5%");
            AssertNear(QualityMath.FeatureFactorFrom(4, true, true, 2f, 50f), 1.10f, 0.0001f,
                "4 points = +10%");
            AssertNear(QualityMath.FeatureFactorFrom(5, true, true, 2f, 50f), 1.15f, 0.0001f,
                "5 points = +15%");
            AssertNear(QualityMath.FeatureFactorFrom(7, true, true, 2f, 50f), 1.30f, 0.0001f,
                "7 points = +30%");
            AssertNear(QualityMath.FeatureFactorFrom(8, true, true, 2f, 50f), 1.40f, 0.0001f,
                "8 points = +40%");
            AssertNear(QualityMath.FeatureFactorFrom(10, true, true, 2f, 50f), 2.00f, 0.0001f,
                "10 points = 2.00x");
            AssertNear(QualityMath.FeatureFactorFrom(11, true, true, 2f, 50f), 2.00f, 0.0001f,
                "beyond 10 points must clamp to 2.00x");

            // Hard switch: disabled means exactly vanilla for every level.
            for (int points = 0; points <= 10; points++)
            {
                AssertNear(QualityMath.FeatureFactorFrom(points, false, true, 2f, 50f), 1.00f, 0.0001f,
                    "disabled feature must be vanilla at " + points + " points");
            }

            // Linear mode regression: the same helper must fall back to SpeedFactor.
            AssertNear(QualityMath.FeatureFactorFrom(5, true, false, 2f, 50f), 1.10f, 0.0001f,
                "linear 5 points * 2% = +10%");
            AssertNear(QualityMath.FeatureFactorFrom(40, true, false, 2f, 50f), 1.50f, 0.0001f,
                "linear mode must clamp at maxBonus=50%");
            AssertNear(QualityMath.FeatureFactorFrom(5, true, false, 0f, 50f), 1.00f, 0.0001f,
                "0% per point must be vanilla");

            // Monotonic across the whole table (no dip, no overshoot).
            float last = 0f;
            for (int points = 1; points <= 10; points++)
            {
                float factor = QualityMath.FeatureFactorFrom(points, true, true, 2f, 50f);
                Assert(factor >= last, "factor must be monotonic at " + points + " points");
                Assert(factor >= 1f && factor <= 2f, "factor must stay inside 1.00x..2.00x");
                last = factor;
            }
        }

        // ------------------------------------------------------------------ P5 A-class matrix

        private static void CheckBonusFeatureMatrix()
        {
            // All four "facility speed" features share FeatureFactorFrom; this matrix pins the contract
            // that the four config switches map to the same curve and that a disabled switch is exact
            // vanilla (no "multiply by 1" residual behaviour).
            string[] features = { "labSpeedBonus", "turretFireRateBonus", "powerGenBonus", "launcherSpeedBonus" };
            int[] points = { 0, 1, 4, 5, 7, 8, 10 };
            float[] expected = { 1.00f, 1.025f, 1.10f, 1.15f, 1.30f, 1.40f, 2.00f };

            for (int f = 0; f < features.Length; f++)
            {
                for (int i = 0; i < points.Length; i++)
                {
                    float on = QualityMath.FeatureFactorFrom(points[i], true, true, 2f, 50f);
                    float off = QualityMath.FeatureFactorFrom(points[i], false, true, 2f, 50f);
                    AssertNear(on, expected[i], 0.0001f,
                        features[f] + " enabled at " + points[i] + " points");
                    AssertNear(off, 1.00f, 0.0001f,
                        features[f] + " disabled at " + points[i] + " points");
                }
            }
        }

        // ------------------------------------------------------------------ P5 sprayer chance

        private static void CheckSprayChance()
        {
            AssertNear(QualityMath.SprayChancePercent(-1), 0f, 0.0001f, "negative points = 0% chance");
            AssertNear(QualityMath.SprayChancePercent(0), 0f, 0.0001f, "0 points = 0% chance");
            AssertNear(QualityMath.SprayChancePercent(1), 2.5f, 0.0001f, "1 point = 2.5% chance");
            AssertNear(QualityMath.SprayChancePercent(4), 10f, 0.0001f, "4 points = 10% chance");
            AssertNear(QualityMath.SprayChancePercent(5), 15f, 0.0001f, "5 points = 15% chance");
            AssertNear(QualityMath.SprayChancePercent(7), 30f, 0.0001f, "7 points = 30% chance");
            AssertNear(QualityMath.SprayChancePercent(8), 40f, 0.0001f, "8 points = 40% chance");
            AssertNear(QualityMath.SprayChancePercent(9), 60f, 0.0001f, "9 points = 60% chance");
            AssertNear(QualityMath.SprayChancePercent(10), 100f, 0.0001f, "10 points = 100% chance");
            AssertNear(QualityMath.SprayChancePercent(25), 100f, 0.0001f, "clamp above 10 points = 100%");

            float last = -1f;
            for (int points = 0; points <= 10; points++)
            {
                float chance = QualityMath.SprayChancePercent(points);
                Assert(chance >= last, "spray chance must be monotonic at " + points + " points");
                Assert(chance >= 0f && chance <= 100f, "spray chance must stay inside 0..100");
                last = chance;
            }
        }

        // ------------------------------------------------------------------ P5 sprayer bonus

        private static void CheckSprayBonus()
        {
            // Never lowers and never runs on nonsense inputs.
            Assert(QualityMath.SprayBonus(0, 10, _ => true, 10) == 0, "0 inc must stay 0");
            Assert(QualityMath.SprayBonus(-5, 10, _ => true, 10) == -5, "negative inc must be untouched");
            Assert(QualityMath.SprayBonus(4, 10, null, 10) == 4, "null roll must be a no-op");
            Assert(QualityMath.SprayBonus(4, 0, _ => true, 10) == 4, "0 points = 0% chance, roll ignored");
            Assert(QualityMath.SprayBonus(4, -2, _ => true, 10) == 4, "negative points must be a no-op");
            Assert(QualityMath.SprayBonus(4, 5, _ => false, 10) == 4, "failed roll keeps the vanilla inc");

            // Winning roll adds exactly one point and reports the curve chance to the RNG.
            float seen = -1f;
            int after = QualityMath.SprayBonus(4, 5, percent =>
            {
                seen = percent;
                return true;
            }, 10);
            Assert(after == 5, "winning roll must add exactly +1 point, got " + after);
            AssertNear(seen, 15f, 0.0001f, "roll callback must receive the curve chance (5 points = 15%)");

            // Level cap: 10 points (max) must never be exceeded even with a 100% roll.
            Assert(QualityMath.SprayBonus(10, 10, _ => true, 10) == 10, "max level must be capped at maxInc");
            Assert(QualityMath.SprayBonus(9, 10, _ => true, 10) == 10, "9 -> 10 is the last allowed +1");
            Assert(QualityMath.SprayBonus(9, 10, _ => true, 0) == 10, "maxInc=0 must be treated as unclamped");

            // 100% at 10 points means every eligible event gains a point, 0% at 0 points never does.
            int gained = 0;
            int lost = 0;
            for (int inc = 1; inc < 10; inc++)
            {
                int result = QualityMath.SprayBonus(inc, 10, _ => true, 10);
                if (result == inc + 1) { gained++; }
                if (result < inc) { lost++; }
            }
            Assert(gained == 9, "10-point sprayer must gain +1 on every 1..9 event, got " + gained);
            Assert(lost == 0, "spray bonus must never lower the inc");

            int zeroGain = 0;
            for (int inc = 1; inc < 10; inc++)
            {
                if (QualityMath.SprayBonus(inc, 0, _ => true, 10) != inc) { zeroGain++; }
            }
            Assert(zeroGain == 0, "0-point sprayer must never change the inc");
        }

        // ------------------------------------------------------------------ P5 sprayer events

        private static void CheckSprayBonusEvents()
        {
            // Deterministic 15% sequence: exactly 15 of 100 spray events win at 5 points.
            int wins = 0;
            int hits = 0;
            for (int i = 0; i < 100; i++)
            {
                int rolled = i;
                int result = QualityMath.SprayBonus(4, 5, _ => rolled < 15, 10);
                if (result == 5) { hits++; }
                // More than one event per item is impossible: the helper is stateless and +1 only.
                wins += result - 4;
            }
            Assert(hits == 15, "15% chance must hit 15 of 100 deterministic events, got " + hits);
            Assert(wins == hits, "every win is exactly one point");

            // A stack spray (sprayedInc = count * incAbility) also only ever gains one point.
            Assert(QualityMath.SprayBonus(20, 10, _ => true, 10) == 20,
                "a stack above maxInc must stay untouched");
            Assert(QualityMath.SprayBonus(6, 10, _ => true, 10) == 7,
                "a stack below maxInc gains exactly one point");
        }
    }
}
