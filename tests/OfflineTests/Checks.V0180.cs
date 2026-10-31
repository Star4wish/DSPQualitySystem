using System;

namespace DSPQualitySystem.OfflineTests
{
    internal static partial class Program
    {
        // ------------------------------------------------------------------ v0.18.0 issue 2

        /// <summary>
        /// v0.18.0 (issue 2): the manufacturing upgrade chance is no longer the linear
        /// points * 1.5% formula; it reuses the shared per-point bonus curve, so a building with
        /// N points rolls the same percentage that N points give to speed / charge / power.
        /// </summary>
        private static void CheckUpgradeCurveChance()
        {
            AssertNear(QualityMath.UpgradeCurveChance(0), 0f, 1e-4f, "curve chance: 0 points -> 0%");
            AssertNear(QualityMath.UpgradeCurveChance(-3), 0f, 1e-4f, "curve chance: negative points -> 0%");
            AssertNear(QualityMath.UpgradeCurveChance(1), 2.5f, 1e-4f, "curve chance: 1 point -> 2.5%");
            AssertNear(QualityMath.UpgradeCurveChance(2), 5f, 1e-4f, "curve chance: 2 points -> 5%");
            AssertNear(QualityMath.UpgradeCurveChance(3), 7.5f, 1e-4f, "curve chance: 3 points -> 7.5%");
            AssertNear(QualityMath.UpgradeCurveChance(4), 10f, 1e-4f, "curve chance: 4 points -> 10%");
            AssertNear(QualityMath.UpgradeCurveChance(5), 15f, 1e-4f, "curve chance: 5 points -> 15%");
            AssertNear(QualityMath.UpgradeCurveChance(6), 20f, 1e-4f, "curve chance: 6 points -> 20%");
            AssertNear(QualityMath.UpgradeCurveChance(7), 30f, 1e-4f, "curve chance: 7 points -> 30%");
            AssertNear(QualityMath.UpgradeCurveChance(8), 40f, 1e-4f, "curve chance: 8 points -> 40%");
            AssertNear(QualityMath.UpgradeCurveChance(9), 60f, 1e-4f, "curve chance: 9 points -> 60%");
            AssertNear(QualityMath.UpgradeCurveChance(10), 100f, 1e-4f, "curve chance: 10 points -> 100%");
            AssertNear(QualityMath.UpgradeCurveChance(11), 100f, 1e-4f, "curve chance: above 10 points stays 100%");

            // monotone, never above 100, and the curve is never below the old linear formula
            float last = -1f;
            for (int points = 0; points <= 12; points++)
            {
                float chance = QualityMath.UpgradeCurveChance(points);
                Assert(chance >= last, "curve chance: monotone at " + points + " points");
                Assert(chance >= 0f && chance <= 100f, "curve chance: inside 0..100 at " + points);
                int linear = (int)(points * 1.5f);
                Assert(chance >= linear - 1e-4f,
                    "curve chance (" + chance + "%) must not be below the old linear " + linear + "% at " + points);
                last = chance;
            }

            // the legacy linear helper is untouched (A/B tests and the offline distribution check
            // still feed it literals).
            AssertNear(QualityMath.UpgradeChance(4, 1.5f, 60f), 6f, 1e-4f,
                "legacy linear UpgradeChance must stay 6% at 4 points");
        }

        /// <summary>
        /// v0.18.0 (issue 2): the roll structure is unchanged - mode 0 is one roll, mode 1 is a
        /// second roll at the same chance (7/8/9 points), mode 2 is the deterministic +2 at 10
        /// points - only the chance that feeds the roll now comes from the curve.
        /// </summary>
        private static void CheckRollCycleLevelCurve()
        {
            // 10 points: +2 without ever calling the random source.
            int rolls = 0;
            Func<float, bool> neverRolls = _ => { rolls++; return false; };
            int mode;
            bool baseHit;
            bool extraHit;
            int level = QualityMath.RollCycleLevelCurve(0, 10, true, 10, 6, MaxLevel, neverRolls,
                out mode, out baseHit, out extraHit);
            Assert(level == 2, "curve roll: 10 points must upgrade +2, got " + level);
            Assert(mode == 2, "curve roll: 10 points must use mode 2, got " + mode);
            Assert(rolls == 0, "curve roll: 10 points must not roll the dice, got " + rolls + " rolls");

            // 4 points: mode 0, one roll, chance 10%.
            float seen = -1f;
            Func<float, bool> missOne = chance => { seen = chance; return false; };
            level = QualityMath.RollCycleLevelCurve(0, 4, true, 10, 6, MaxLevel, missOne,
                out mode, out baseHit, out extraHit);
            Assert(level == 0, "curve roll: 4 points with a miss must stay at level 0");
            Assert(mode == 0, "curve roll: 4 points must use mode 0, got " + mode);
            AssertNear(seen, 10f, 1e-4f, "curve roll: 4 points roll at 10%");
            Assert(!baseHit && !extraHit, "curve roll: a miss must clear both hit flags");

            Func<float, bool> hitOne = chance => { seen = chance; return true; };
            level = QualityMath.RollCycleLevelCurve(0, 4, true, 10, 6, MaxLevel, hitOne,
                out mode, out baseHit, out extraHit);
            Assert(level == 1, "curve roll: 4 points with a hit must be +1, got " + level);
            Assert(baseHit && !extraHit, "curve roll: mode 0 uses a single roll");

            // 8 points: mode 1, two rolls at 40%; both hit -> +2, one hit -> +1.
            int calls = 0;
            Func<float, bool> bothHit = chance => { seen = chance; calls++; return true; };
            level = QualityMath.RollCycleLevelCurve(0, 8, true, 10, 6, MaxLevel, bothHit,
                out mode, out baseHit, out extraHit);
            Assert(mode == 1, "curve roll: 8 points must use mode 1, got " + mode);
            AssertNear(seen, 40f, 1e-4f, "curve roll: 8 points roll at 40%");
            Assert(level == 2 && calls == 2, "curve roll: 8 points double hit = +2 (" + level + ", " + calls + " rolls)");

            calls = 0;
            Func<float, bool> firstOnly = chance => { calls++; return calls == 1; };
            level = QualityMath.RollCycleLevelCurve(0, 8, true, 10, 6, MaxLevel, firstOnly,
                out mode, out baseHit, out extraHit);
            Assert(level == 1 && baseHit && !extraHit, "curve roll: base hit only = +1");

            calls = 0;
            Func<float, bool> secondOnly = chance => { calls++; return calls == 2; };
            level = QualityMath.RollCycleLevelCurve(0, 8, true, 10, 6, MaxLevel, secondOnly,
                out mode, out baseHit, out extraHit);
            Assert(level == 1 && !baseHit && extraHit, "curve roll: extra hit only = +1");

            // 7 points: 30%, 9 points: 60% - the curve values feed the same double roll.
            seen = -1f;
            QualityMath.RollCycleLevelCurve(0, 7, true, 10, 6, MaxLevel, chance => { seen = chance; return false; },
                out mode, out baseHit, out extraHit);
            AssertNear(seen, 30f, 1e-4f, "curve roll: 7 points roll at 30%");
            seen = -1f;
            QualityMath.RollCycleLevelCurve(0, 9, true, 10, 6, MaxLevel, chance => { seen = chance; return false; },
                out mode, out baseHit, out extraHit);
            AssertNear(seen, 60f, 1e-4f, "curve roll: 9 points roll at 60%");

            // 6 points: still below the extra-roll threshold (mode 0).
            QualityMath.RollCycleLevelCurve(0, 6, true, 10, 6, MaxLevel, _ => false,
                out mode, out baseHit, out extraHit);
            Assert(mode == 0, "curve roll: 6 points must stay a single roll, got mode " + mode);

            // fractionators pass allowExtraUpgrade=false: 9 points -> one roll only.
            calls = 0;
            level = QualityMath.RollCycleLevelCurve(0, 9, false, 10, 6, MaxLevel, _ => { calls++; return true; },
                out mode, out baseHit, out extraHit);
            Assert(mode == 0 && calls == 1 && level == 1,
                "curve roll: fractionator keeps the single roll (mode " + mode + ", " + calls + " rolls)");

            // level clamping: a 10-point roll from level 9 stops at MaxLevel.
            level = QualityMath.RollCycleLevelCurve(9, 10, true, 10, 6, MaxLevel, _ => false,
                out mode, out baseHit, out extraHit);
            Assert(level == MaxLevel, "curve roll: +2 clamps at max level, got " + level);

            // equal input levels produce the same result as the legacy entry point when the chance
            // itself is equal (same rollPercent call order).
            int legacyMode, curveMode;
            bool legacyBase, legacyExtra, curveBase, curveExtra;
            int legacyLevel = QualityMath.RollCycleLevel(3, 6, true, 1.5f, 60f, 10, 6, MaxLevel,
                _ => true, out legacyMode, out legacyBase, out legacyExtra);
            int curveLevel = QualityMath.RollCycleLevelCurve(3, 6, true, 10, 6, MaxLevel,
                _ => true, out curveMode, out curveBase, out curveExtra);
            Assert(legacyLevel == curveLevel && legacyMode == curveMode &&
                   legacyBase == curveBase && legacyExtra == curveExtra,
                "curve roll: identical roll shape at 6 points (legacy " + legacyLevel + " vs curve " + curveLevel + ")");
        }

        // ------------------------------------------------------------------ v0.18.0 issue 3

        /// <summary>
        /// v0.18.0 (issue 3): miner rolls are deterministic - floor(points / 2) points per item,
        /// i.e. a 2-point miner adds 1, a 4-point miner adds 2. The chance constant is 100, so the
        /// pre-existing "chance >= 100f" fast path in RollMinerQuality awards every roll.
        /// </summary>
        private static void CheckMinerGuaranteedRolls()
        {
            // QualityConfig.cs is BepInEx-bound and not linked into this harness, so the shipping
            // constant is mirrored here: MinerChancePerPointValue = 100f (two levels per point).
            const float minerChancePerPoint = 100f;
            AssertNear(QualityMath.MinerChance(1, minerChancePerPoint), 100f, 1e-4f,
                "miner chance per point 100: a 1-point miner rolls at 100% (but has no roll)");

            for (int points = 0; points <= MaxLevel; points++)
            {
                Assert(QualityMath.MinerRollsOf(points) == points / 2,
                    "miner rolls: floor(points / 2) at " + points + " points");
                float chance = QualityMath.MinerChance(points, minerChancePerPoint);
                if (points <= 0)
                {
                    Assert(chance <= 0f, "miner chance: 0 points -> no roll");
                }
                else
                {
                    AssertNear(chance, 100f, 1e-4f, "miner chance: " + points + " points -> 100%");
                }
            }

            Assert(QualityMath.MinerRollsOf(2) == 1, "miner rolls: 2 points = 1 point per item");
            Assert(QualityMath.MinerRollsOf(4) == 2, "miner rolls: 4 points = 2 points per item");
            Assert(QualityMath.MinerRollsOf(10) == 5, "miner rolls: 10 points = 5 points per item");
            Assert(QualityMath.MinerRollsOf(1) == 0, "miner rolls: 1 point stays at 0 (no half point)");

            var rng = new Random(4242);
            for (int i = 0; i < 1000; i++)
            {
                Assert(QualityMath.RollPercent(100f, rng), "miner chance: 100% must always hit");
            }

            // a 100% roll at every point level is exactly the "guaranteed per two levels" rule; the
            // collected ore therefore never exceeds MinerMaxInc(points) and never drops below it.
            Assert(QualityMath.MinerMaxInc(10) == 5, "miner rolls: 10-point miner caps ore at 5 points");
        }

        // ------------------------------------------------------------------ v0.18.0 issue 4

        /// <summary>
        /// v0.18.0 (issue 4): the deferred pre-build payment path converts the consumed items back
        /// to per-item points with the same arithmetic the Shift instant-build path uses, so a ghost
        /// that is paid later inherits the points of the items that actually paid for it.
        /// </summary>
        private static void CheckDeferredPayPerItem()
        {
            Assert(QualityMath.DeferredPayPerItem(16, 4) == 4, "deferred pay: 16 points over 4 items -> 4 each");
            Assert(QualityMath.DeferredPayPerItem(7, 2) == 3, "deferred pay: 7 points over 2 items floors to 3");
            Assert(QualityMath.DeferredPayPerItem(0, 5) == 0, "deferred pay: no points -> 0");
            Assert(QualityMath.DeferredPayPerItem(10, 1) == 10, "deferred pay: a single 10-point item keeps 10");
            Assert(QualityMath.DeferredPayPerItem(999, 2) == QualityMath.IncPerItemMax,
                "deferred pay: per-item points are clamped to 10");
            Assert(QualityMath.DeferredPayPerItem(-5, 3) == 0, "deferred pay: negative points -> 0");
            Assert(QualityMath.DeferredPayPerItem(20, 0) == 0, "deferred pay: zero count -> 0");
            Assert(QualityMath.DeferredPayPerItem(20, -2) == 0, "deferred pay: negative count -> 0");
            Assert(QualityMath.DeferredPayPerItem(12, 3) == 4, "deferred pay: 12 over 3 items -> 4 each");
            Assert(QualityMath.DeferredPayPerItem(3, 4) == 0, "deferred pay: average below one point -> 0");

            // identical to the instant-build conversion (one shared rule for both late payments).
            for (int points = 0; points <= 40; points++)
            {
                for (int count = 0; count <= 5; count++)
                {
                    Assert(QualityMath.DeferredPayPerItem(points, count) ==
                           QualityMath.InstantBuildPerItem(points, count),
                        "deferred pay: must match the instant-build conversion at " + points + "/" + count);
                }
            }
        }
    }
}
