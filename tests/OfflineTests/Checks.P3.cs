using System;

namespace DSPQualitySystem.OfflineTests
{
    /// <summary>
    /// P3 additive-factor checks (stage 1 speed + stage 2a station charge). Split out of Program.cs
    /// so every test source file stays under the project convention of 500 lines; the harness is
    /// still one exe (partial Program class), and both use only the linked pure QualityMath.cs.
    /// </summary>
    internal static partial class Program
    {
        // ---------------------------------------------------------- P3 speed

        private static void CheckSpeedFactor()
        {
            Assert(QualityMath.SpeedFactor(0, 2f, 50f) == 1f, "0 points must keep vanilla speed exactly");
            Assert(QualityMath.SpeedFactor(-3, 2f, 50f) == 1f, "negative points must keep vanilla speed");
            AssertNear(QualityMath.SpeedFactor(10, 2f, 50f), 1.2f, 1e-5f, "10 points x 2% must be +20%");
            AssertNear(QualityMath.SpeedFactor(4, 2f, 50f), 1.08f, 1e-5f, "4 points x 2% must be +8%");

            // monotonic in points and always bounded by the configured cap
            float prev = 0f;
            for (int inc = 0; inc <= MaxLevel; inc++)
            {
                float f = QualityMath.SpeedFactor(inc, 2f, 50f);
                Assert(f >= 1f && f >= prev, "speed factor must be monotonic at inc=" + inc);
                Assert(f <= 1.5f + 1e-6f, "default cap must hold at inc=" + inc);
                prev = f;
            }

            AssertNear(QualityMath.SpeedFactor(100, 2f, 50f), 1.5f, 1e-5f, "total bonus must cap at +50%");
            AssertNear(QualityMath.SpeedFactor(10, 20f, 50f), 1.5f, 1e-5f, "cap must beat the linear bonus");
            AssertNear(QualityMath.SpeedFactor(10, 100f, 500f), 6f, 1e-5f, "max clamped config gives +500%");

            Assert(QualityMath.SpeedFactor(5, 0f, 50f) == 1f, "0 percent per point = vanilla speed");
            Assert(QualityMath.SpeedFactor(5, -1f, 50f) == 1f, "negative percent = vanilla speed");
            Assert(QualityMath.SpeedFactor(5, 2f, 0f) == 1f, "0 cap = vanilla speed");
            Assert(QualityMath.SpeedFactor(5, 2f, -10f) == 1f, "negative cap = vanilla speed");

            // sample the default config curve: every point adds exactly 2% until the cap
            for (int inc = 0; inc <= MaxLevel; inc++)
            {
                AssertNear(QualityMath.SpeedFactor(inc, 2f, 50f), 1f + inc * 0.02f, 1e-5f,
                    "default curve at inc=" + inc);
            }
        }

        // ---------------------------------------------------------- P3 station charge

        private static void CheckStationChargeFactor()
        {
            // The station charge bonus deliberately reuses the stage-1 speed curve (one shared
            // formula, no duplicated math), so this pins the same contract for the charge use.
            Assert(QualityMath.SpeedFactor(0, 2f, 50f) == 1f, "0 points must keep vanilla charge exactly");
            Assert(QualityMath.SpeedFactor(-3, 2f, 50f) == 1f, "negative points must keep vanilla charge");
            AssertNear(QualityMath.SpeedFactor(10, 2f, 50f), 1.2f, 1e-5f, "10 points x 2% must be +20% charge");
            AssertNear(QualityMath.SpeedFactor(4, 2f, 50f), 1.08f, 1e-5f, "4 points x 2% must be +8% charge");

            // monotonic in points and always bounded by the configured cap
            float prev = 0f;
            for (int inc = 0; inc <= MaxLevel; inc++)
            {
                float f = QualityMath.SpeedFactor(inc, 2f, 50f);
                Assert(f >= 1f && f >= prev, "charge factor must be monotonic at inc=" + inc);
                Assert(f <= 1.5f + 1e-6f, "default charge cap must hold at inc=" + inc);
                prev = f;
            }

            AssertNear(QualityMath.SpeedFactor(100, 2f, 50f), 1.5f, 1e-5f, "total charge bonus must cap at +50%");
            AssertNear(QualityMath.SpeedFactor(10, 100f, 500f), 6f, 1e-5f, "max clamped charge config gives +500%");
            Assert(QualityMath.SpeedFactor(5, 0f, 50f) == 1f, "0 percent per point = vanilla charge");
            Assert(QualityMath.SpeedFactor(5, 2f, 0f) == 1f, "0 cap = vanilla charge");
            Assert(!float.IsNaN(QualityMath.SpeedFactor(MaxLevel, 2f, 50f)), "charge factor must not be NaN");
        }

        // ---------------------------------------------------------- P3 dismantle refund

        private static void CheckDismantleInc()
        {
            // Config gate: giveDismantleInc=false must reproduce the vanilla hard-coded inc=0.
            Assert(QualityMath.DismantleIncFrom(4, MaxLevel, false) == 0, "gave=false must return vanilla inc 0");
            Assert(QualityMath.DismantleIncFrom(MaxLevel, MaxLevel, false) == 0, "gave=false must ignore points");

            // No stored points -> vanilla inc 0 (building placed before M2 or never upgraded).
            Assert(QualityMath.DismantleIncFrom(0, MaxLevel, true) == 0, "0 points must return vanilla inc 0");
            Assert(QualityMath.DismantleIncFrom(-3, MaxLevel, true) == 0, "negative points must return vanilla inc 0");

            // Normal passthrough and kIncLevelMax clamp.
            Assert(QualityMath.DismantleIncFrom(4, MaxLevel, true) == 4, "4 points must refund inc 4");
            Assert(QualityMath.DismantleIncFrom(MaxLevel, MaxLevel, true) == MaxLevel, "MaxLevel points must refund MaxLevel");
            Assert(QualityMath.DismantleIncFrom(MaxLevel + 3, MaxLevel, true) == MaxLevel, "points above kIncLevelMax must clamp");
            Assert(QualityMath.DismantleIncFrom(9, 4, true) == 4, "clamp must follow the given kIncLevelMax");

            // Defensive: a bogus negative table max must never produce a negative inc.
            Assert(QualityMath.DismantleIncFrom(5, -1, true) >= 0, "negative kIncLevelMax must not return negative inc");
            Assert(QualityMath.DismantleIncFrom(5, 0, true) == 0, "0 kIncLevelMax must return inc 0");
        }

        // ---------------------------------------------------------- v0.9.0 miner multi-roll

        private static void CheckMinerMultiRoll()
        {
            // User spec 2026-10-25: rolls = floor(points / 2); every success adds one point.
            for (int inc = 0; inc <= MaxLevel; inc++)
            {
                int expected = inc / 2;
                Assert(QualityMath.MinerRollsOf(inc) == expected, "rolls must be floor(level/2) at inc=" + inc);
                Assert(QualityMath.MinerMaxInc(inc) == expected, "cap must be floor(level/2) at inc=" + inc);
            }
            Assert(QualityMath.MinerRollsOf(-3) == 0, "negative points must give 0 rolls");

            var rng = new Random(20251025);
            double previousAverage = -1.0;
            for (int inc = 0; inc <= MaxLevel; inc++)
            {
                int rolls = QualityMath.MinerRollsOf(inc);
                int cap = QualityMath.MinerMaxInc(inc);
                float chance = QualityMath.MinerChance(inc, 2f);
                int trials = 200000;
                long total = 0;
                int aboveCap = 0;
                for (int t = 0; t < trials; t++)
                {
                    int points = 0;
                    for (int r = 0; r < rolls; r++)
                    {
                        if (QualityMath.RollPercent(chance, rng))
                        {
                            points++;
                        }
                    }
                    if (points > cap)
                    {
                        aboveCap++;
                    }
                    total += points;
                }
                double average = total / (double)trials;
                Assert(aboveCap == 0, "mined item points must stay within the cap at inc=" + inc);
                Assert(average >= previousAverage, "expected points must be monotonic at inc=" + inc);
                previousAverage = average;
                if (inc <= 1)
                {
                    Assert(average == 0.0, "levels 0/1 must never emit a quality point");
                }
                // Theoretical mean of the multi-roll: rolls * chance / 100.
                AssertNear((float)average, rolls * chance / 100f, 0.02f, "multi-roll mean at inc=" + inc);
            }
        }

        // ---------------------------------------------------------- v0.9.0 extra upgrade

        private static void CheckExtraUpgrade()
        {
            // Default thresholds: 0..6 = old single roll, 7/8/9 = one extra roll, 10 = forced +2.
            int[] expectedModes = { 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 2 };
            for (int inc = 0; inc <= MaxLevel; inc++)
            {
                Assert(QualityMath.ExtraUpgradeMode(inc, 10, 6) == expectedModes[inc],
                    "default extra-upgrade mode at inc=" + inc);
                Assert(QualityMath.ExtraUpgradeMode(inc, 0, 0) == 0,
                    "config 0/0 must keep the v0.8.0 behaviour at inc=" + inc);
            }
            Assert(QualityMath.ExtraUpgradeMode(-2, 10, 6) == 0, "negative points must not upgrade");
            Assert(QualityMath.ExtraUpgradeMode(5, 0, 4) == 1, "extra rule alone still applies above its threshold");
            Assert(QualityMath.ExtraUpgradeMode(4, 2, 0) == 2, "double rule alone still applies at its threshold");

            // 10 points = deterministic +2: 1,000,000 samples per input level, always min(input+2, cap).
            int mismatches = 0;
            for (int input = 0; input <= MaxLevel; input++)
            {
                int expected = input + 2 > MaxLevel ? MaxLevel : input + 2;
                for (int t = 0; t < 1000000; t++)
                {
                    if (QualityMath.ApplyCycleUpgrade(input, 2, false, false, MaxLevel) != expected)
                    {
                        mismatches++;
                    }
                }
            }
            Assert(mismatches == 0, "10-point buildings must always upgrade +2 (clamped)");

            // 7/8/9: upper bound is +2, but the second roll can fail (not a guaranteed +2).
            var rng = new Random(20251025);
            for (int inc = 7; inc <= 9; inc++)
            {
                int mode = QualityMath.ExtraUpgradeMode(inc, 10, 6);
                float chance = QualityMath.UpgradeChance(inc, 1.5f, 60f);
                int trials = 200000;
                int aboveTwo = 0;
                int belowTwo = 0;
                for (int t = 0; t < trials; t++)
                {
                    bool baseHit = QualityMath.RollPercent(chance, rng);
                    bool extraHit = QualityMath.RollPercent(chance, rng);
                    int got = QualityMath.ApplyCycleUpgrade(3, mode, baseHit, extraHit, MaxLevel);
                    if (got > 5)
                    {
                        aboveTwo++;
                    }
                    if (got < 5)
                    {
                        belowTwo++;
                    }
                }
                Assert(aboveTwo == 0, "7/8/9 must never gain more than +2 at inc=" + inc);
                Assert(belowTwo > 0, "7/8/9 must stay probabilistic, not a guaranteed +2 at inc=" + inc);
            }

            // Mode semantics on a fixed input level: 0 = single roll, 1 = up to +2, 2 = forced +2.
            Assert(QualityMath.ApplyCycleUpgrade(4, 0, true, true, MaxLevel) == 5, "mode 0 ignores the extra roll");
            Assert(QualityMath.ApplyCycleUpgrade(4, 1, true, false, MaxLevel) == 5, "mode 1 single hit = +1");
            Assert(QualityMath.ApplyCycleUpgrade(4, 1, true, true, MaxLevel) == 6, "mode 1 double hit = +2");
            Assert(QualityMath.ApplyCycleUpgrade(4, 2, false, false, MaxLevel) == 6, "mode 2 is deterministic +2");
            Assert(QualityMath.ApplyCycleUpgrade(9, 2, false, false, MaxLevel) == MaxLevel, "guaranteed +2 clamps at max");
            Assert(QualityMath.ApplyCycleUpgrade(9, 1, true, true, MaxLevel) == MaxLevel, "mode 1 clamps at max");
            Assert(QualityMath.ApplyCycleUpgrade(3, 0, false, false, 2) == 2, "input level above max is clamped");
            Assert(QualityMath.ApplyCycleUpgrade(-1, 0, false, false, MaxLevel) == 0, "negative input level becomes 0");
            Assert(QualityMath.ApplyCycleUpgrade(5, 0, false, false, MaxLevel) == 5, "no points / no roll keeps the input");
        }

        // ------------------------------------------------- P3-2a charge cap (v0.11.0)

        private static void CheckChargeCap()
        {
            // 0 points / no-op config / invalid inputs return the "leave vanilla" sentinel 0.
            Assert(QualityMath.ChargeCapFrom(1200, 100, 8000, 0, 2f, 50f) == 0, "0 points = vanilla charge");
            Assert(QualityMath.ChargeCapFrom(1200, 100, 8000, 4, 0f, 50f) == 0, "0 percent per point = no change");
            Assert(QualityMath.ChargeCapFrom(1200, 100, 8000, 4, 2f, 0f) == 0, "0 cap = no change");
            Assert(QualityMath.ChargeCapFrom(0, 100, 8000, 4, 2f, 50f) == 0, "zero energyPerTick = no change");
            Assert(QualityMath.ChargeCapFrom(-5, 100, 8000, 4, 2f, 50f) == 0, "negative energyPerTick = no change");
            Assert(QualityMath.ChargeCapFrom(1200, 100, 0, 4, 2f, 50f) == 0, "zero energyMax = no change");

            // A full (or one unit from full) buffer must stay vanilla even at 10 points.
            Assert(QualityMath.ChargeCapFrom(1200, 8000, 8000, 10, 2f, 50f) == 0, "full buffer is skipped");
            Assert(QualityMath.ChargeCapFrom(1200, 7999, 8000, 10, 2f, 50f) == 0, "1 unit from full is skipped");
            Assert(QualityMath.ChargeCapFrom(1200, 7998, 8000, 4, 2f, 50f) > 1200, "below the skip edge the cap rises");

            // Default curve: 4 points x 2% = +8%, 10 points = +20%.
            Assert(QualityMath.ChargeCapFrom(1200, 100, 8000, 4, 2f, 50f) == 1296, "4 points x 2%: 1200 -> 1296");
            Assert(QualityMath.ChargeCapFrom(1200, 100, 8000, 10, 2f, 50f) == 1440, "10 points x 2%: 1200 -> 1440");
            Assert(QualityMath.ChargeCapFrom(1200, 100, 8000, 10, 20f, 50f) == 1800, "cap beats the linear bonus (+50%)");
            Assert(QualityMath.ChargeCapFrom(1200, 100, 8000, 10, 100f, 500f) == 7200, "clamped config allows +500%");

            // 1 point: value must equal the same SpeedFactor-based arithmetic (float rounding safe).
            float f1 = QualityMath.SpeedFactor(1, 2f, 50f);
            Assert(QualityMath.ChargeCapFrom(1200, 100, 8000, 1, 2f, 50f) == (long)(1200 * (double)f1),
                "1 point uses the shared SpeedFactor curve");

            // Monotonic and never below the vanilla value when the cap does change.
            long prev = 0;
            for (int inc = 0; inc <= MaxLevel; inc++)
            {
                long got = QualityMath.ChargeCapFrom(1000, 100, 8000, inc, 2f, 50f);
                if (got != 0)
                {
                    Assert(got >= 1000, "raised cap must grow at inc=" + inc);
                    Assert(got >= prev, "raised cap must be monotonic at inc=" + inc);
                    prev = got;
                }
            }

            // Long arithmetic: big caps scale linearly and cannot overflow toward negative.
            Assert(QualityMath.ChargeCapFrom(1000000, 1, 5000000, 10, 2f, 50f) == 1200000, "large cap scales linearly");
            Assert(QualityMath.ChargeCapFrom(long.MaxValue / 4, 1, long.MaxValue, 10, 2f, 50f) > 0,
                "huge cap stays positive");
        }
        // v0.11.1: the [QSystem][roll] debug line lives in QualityRules (game layer). This test pins
        // the pure roll layer it wraps: the number of rolls is a function of the mode and the pooled
        // level is bit-identical to the v0.11.0 formula for the same roller stream, so observation
        // can never change the RNG sequence or the produced levels.
        internal static void CheckRollLogNoRngChange()
        {
            float pct = 1.5f;
            float maxChance = 60f;
            int doubleAt = 10;
            int extraAbove = 6;
            int maxLevel = MaxLevel;

            // 1) Roll-count contract: mode 0 -> 1 roll, mode 1 -> 2 rolls, mode 2 -> 0 rolls.
            for (int bld = 0; bld <= MaxLevel; bld++)
            {
                for (int allow = 0; allow <= 1; allow++)
                {
                    int rolls = 0;
                    int mode;
                    bool baseHit;
                    bool extraHit;
                    int got = QualityMath.RollCycleLevel(5, bld, allow == 1, pct, maxChance,
                        doubleAt, extraAbove, maxLevel, p => { rolls++; return true; },
                        out mode, out baseHit, out extraHit);
                    int expectedRolls = bld <= 0 ? 0 : (mode >= 2 ? 0 : (mode >= 1 ? 2 : 1));
                    Assert(rolls == expectedRolls,
                        "roll count must follow the mode (bld=" + bld + ", allow=" + allow + ", rolls=" + rolls + ")");
                    Assert(got >= 5 && got <= maxLevel, "pooled level must stay in range (bld=" + bld + ")");
                    if (allow == 0)
                    {
                        Assert(mode == 0, "fractionator path must never use the extra upgrade rules");
                        Assert(rolls <= 1, "fractionator path must only use the single base roll");
                    }
                }
            }

            // 2) Bit-identical pooled sequence against an independent v0.11.0 formula copy.
            int[] blds = { 0, 2, 4, 7, 10 };
            int[] ins = { 0, 3, 9, 10 };
            for (int pass = 0; pass < 2; pass++)
            {
                bool allowExtra = pass == 1;
                for (int bi = 0; bi < blds.Length; bi++)
                {
                    for (int li = 0; li < ins.Length; li++)
                    {
                        Random rngA = new Random(424242);
                        Random rngB = new Random(424242);
                        for (int rep = 0; rep < 40; rep++)
                        {
                            int modeA;
                            bool hitA;
                            bool extraA;
                            int pure = QualityMath.RollCycleLevel(ins[li], blds[bi], allowExtra, pct, maxChance,
                                doubleAt, extraAbove, maxLevel,
                                p => p >= 100f || rngA.Next(10000) < (int)(p * 100f + 0.5f),
                                out modeA, out hitA, out extraA);
                            int modeB;
                            bool hitB;
                            bool extraB;
                            int legacy = LegacyRollCycleLevel(ins[li], blds[bi], allowExtra, pct, maxChance,
                                doubleAt, extraAbove, maxLevel,
                                p => p >= 100f || rngB.Next(10000) < (int)(p * 100f + 0.5f),
                                out modeB, out hitB, out extraB);
                            Assert(pure == legacy, "pure layer must match the v0.11.0 formula (bld=" + blds[bi] + ", in=" + ins[li] + ")");
                            Assert(modeA == modeB && hitA == hitB && extraA == extraB, "mode and hit flags must match");
                        }
                    }
                }
            }

            // 3) A re-seeded roller stream reproduces the same levels, i.e. nothing besides the
            // documented rolls consumes randomness.
            Random seed1 = new Random(777001);
            int[] first = new int[300];
            for (int i = 0; i < first.Length; i++)
            {
                int mode;
                bool hit;
                bool extra;
                first[i] = QualityMath.RollCycleLevel(i % 11, (i % 5) * 2, true, pct, maxChance,
                    doubleAt, extraAbove, maxLevel,
                    p => p >= 100f || seed1.Next(10000) < (int)(p * 100f + 0.5f),
                    out mode, out hit, out extra);
            }
            Random seed2 = new Random(777001);
            for (int i = 0; i < first.Length; i++)
            {
                int mode;
                bool hit;
                bool extra;
                int second = QualityMath.RollCycleLevel(i % 11, (i % 5) * 2, true, pct, maxChance,
                    doubleAt, extraAbove, maxLevel,
                    p => p >= 100f || seed2.Next(10000) < (int)(p * 100f + 0.5f),
                    out mode, out hit, out extra);
                Assert(first[i] == second, "seeded sequence must be reproducible (i=" + i + ")");
            }
        }

        // Independent copy of the v0.11.0 RollCycleQuality body (no logging), used to prove the
        // refactored pure layer returns bit-identical results.
        private static int LegacyRollCycleLevel(int inputLevel, int buildingInc, bool allowExtraUpgrade,
            float chancePerPoint, float maxChance, int doubleUpgradeAt, int extraAbove, int maxLevel,
            Func<float, bool> rollPercent, out int mode, out bool baseRollHit, out bool extraRollHit)
        {
            int level = inputLevel < 0 ? 0 : inputLevel;
            if (level > maxLevel)
            {
                level = maxLevel;
            }
            mode = 0;
            baseRollHit = false;
            extraRollHit = false;
            if (level < maxLevel && buildingInc > 0)
            {
                mode = allowExtraUpgrade
                    ? QualityMath.ExtraUpgradeMode(buildingInc, doubleUpgradeAt, extraAbove)
                    : 0;
                if (mode < 2)
                {
                    float chance = QualityMath.UpgradeChance(buildingInc, chancePerPoint, maxChance);
                    baseRollHit = chance > 0f && rollPercent(chance);
                    if (mode >= 1)
                    {
                        extraRollHit = chance > 0f && rollPercent(chance);
                    }
                }
                level = QualityMath.ApplyCycleUpgrade(level, mode, baseRollHit, extraRollHit, maxLevel);
            }
            if (level > maxLevel)
            {
                level = maxLevel;
            }
            if (level < 0)
            {
                level = 0;
            }
            return level;
        }
        // ---------------------------------------------------------- P3-2e curve (v0.11.2 A)

        private static void CheckSpeedCurve()
        {
            double[] expected = { 0.0, 2.5, 5.0, 7.5, 10.0, 15.0, 20.0, 30.0, 40.0, 60.0, 100.0 };
            for (int i = 0; i < expected.Length; i++)
            {
                AssertNear((float)QualityMath.SpeedBonusFrom(i), (float)expected[i], 1e-6f,
                    "curve bonus percent at " + i + " points");
            }
            AssertNear((float)QualityMath.SpeedBonusFrom(-1), 0f, 1e-6f, "negative points give no bonus");
            AssertNear((float)QualityMath.SpeedBonusFrom(11), 100f, 1e-6f, "above the table is clamped to +100%");
            AssertNear((float)QualityMath.SpeedBonusFrom(99), 100f, 1e-6f, "far above the table stays at +100%");

            // monotonic, and the two modes agree only at 0 points
            double previous = -1.0;
            for (int i = 0; i <= QualityMath.CurveMaxPoints; i++)
            {
                double value = QualityMath.SpeedBonusFrom(i);
                Assert(value >= previous, "curve must be monotonic at " + i);
                previous = value;
            }
            Assert(QualityMath.SpeedFactorFor(0, true, 2f, 50f) == 1f, "curve 0 points = vanilla exactly");
            Assert(QualityMath.SpeedFactorFor(0, false, 2f, 50f) == 1f, "linear 0 points = vanilla exactly");
            AssertNear(QualityMath.SpeedFactorFor(10, true, 2f, 50f), 2.0f, 1e-5f, "curve 10 points = x2.00");
            AssertNear(QualityMath.SpeedFactorFor(4, true, 2f, 50f), 1.10f, 1e-5f, "curve 4 points = +10%");
            AssertNear(QualityMath.SpeedFactorFor(10, false, 2f, 50f), 1.2f, 1e-5f, "linear mode stays legacy");
            AssertNear(QualityMath.SpeedFactorFor(4, false, 2f, 50f), 1.08f, 1e-5f, "linear 4 points = +8%");
        }

        private static void CheckChargeCurve()
        {
            Assert(QualityMath.ChargeCapFrom(1200, 100, 8000, 0, true, 2f, 50f) == 0,
                "curve 0 points = vanilla charge");
            Assert(QualityMath.ChargeCapFrom(1200, 100, 8000, 10, true, 2f, 50f) == 2400,
                "curve 10 points doubles the cap (x2.00)");
            Assert(QualityMath.ChargeCapFrom(1200, 100, 8000, 4, true, 2f, 50f) == 1320,
                "curve 4 points raises 1200 -> 1320 (+10%)");
            Assert(QualityMath.ChargeCapFrom(1200, 100, 8000, 10, false, 2f, 50f) == 1440,
                "linear mode keeps the v0.11.0 result (+20%)");
            Assert(QualityMath.ChargeCapFrom(1200, 8000, 8000, 10, true, 2f, 50f) == 0,
                "curve mode still skips a full buffer");
            Assert(QualityMath.ChargeCapFrom(1200, 7998, 8000, 10, true, 2f, 50f) > 1200,
                "curve mode raises the cap below the skip edge");
            Assert(QualityMath.ChargeCapFrom(0, 100, 8000, 10, true, 2f, 50f) == 0,
                "curve mode keeps zero energyPerTick untouched");
        }
    }
}
