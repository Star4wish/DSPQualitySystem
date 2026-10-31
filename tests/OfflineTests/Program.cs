using System;
using System.Collections.Generic;
using System.Text;
using DSPQualitySystem;
using DSPQualitySystem.Save;

namespace DSPQualitySystem.OfflineTests
{
    /// <summary>
    /// Offline test suite: pool conservation, config clamps, JSON round trip, miner/upgrade rolls.
    /// Only pure code is linked, so this runs without the game (dotnet run / tests\run-tests.ps1).
    /// </summary>
    internal static partial class Program
    {
        private const int MaxLevel = 10;
        private static int _failures;
        private static int _checks;

        private static int Main()
        {
            Console.WriteLine("DSP Quality System - offline tests (no game required)");
            Console.WriteLine("-----------------------------------------------------");

            Check("pool conservation (1,000,000 random add/take ops)", CheckPoolConservation);
            Check("pool edge cases", CheckPoolEdgeCases);
            Check("config / rule clamps", CheckClamps);
            Check("JSON round trip (schema + tricky strings)", CheckJsonRoundTrip);
            Check("save guard (seed / gameName / schema version)", CheckSaveGuard);
            Check("save blob codec (append / read back, lengths 1..65536)", CheckSaveBlobRoundTrip);
            Check("save blob codec rejects (empty / truncated / bad length / bad magic)", CheckSaveBlobRejects);
            Check("save blob codec vanilla prefix (VFSAVE header + 200 KB payload)", CheckSaveBlobVanillaPrefix);
            Check("save blob codec double append (only the last trailer is at EOF)", CheckSaveBlobDoubleAppend);
            Check("save blob codec shared trailer (another mod appended after us)", CheckSaveBlobSharedTrailer);
            Check("save blob codec arguments (null / empty payload rejected)", CheckSaveBlobArguments);
            Check("miner roll distribution (2% and 8% per item)", CheckMinerRollDistribution);
            Check("upgrade roll (100% must upgrade, 6% distribution)", CheckUpgradeRoll);
            Check("fractionator input level (num3 recompute + clamp)", CheckFractionatorInputLevel);
            Check("fractionator pool round trip (produce / ship / rollback)", CheckFractionatorPoolRoundTrip);
            Check("P3 speed factor (monotonic / cap / zero-point untouched)", CheckSpeedFactor);
            Check("P3 station charge factor (curve / cap / zero-point untouched)", CheckStationChargeFactor);
            Check("P3-2a charge cap (curve / full-buffer skip / zero-point untouched)", CheckChargeCap);
            Check("P3 dismantle inc (config gate / clamp / zero points)", CheckDismantleInc);
            Check("P3-2e miner multi-roll (cap = floor(level/2), monotonic, 0/1 -> 0)", CheckMinerMultiRoll);
            Check("P3-2e extra upgrade (>6 double roll / 10 guaranteed +2 / 0 = old)", CheckExtraUpgrade);
            Check("P3-2e roll log (roll count pinned / logging cannot change RNG)", CheckRollLogNoRngChange);
            Check("P4 isolation merge (same inc / different inc / non-building / capacity)", CheckIsolationMerge);
            Check("P4 split_inc (vanilla distribution / degenerate inputs)", CheckSplitInc);
            Check("P4 sort basics (0 vs 4 point building stay in two grids)", CheckSortIsolationBasics);
            Check("P4 sort merge (same level merges / different level splits / stack overflow)", CheckSortIsolationMerge);
            Check("P4 sort non-building (vanilla merge + mixed backpack + determinism)", CheckSortNonBuildingVanilla);
            Check("P3 curve bonus table (2.5..100% table / curve vs linear factor)", CheckSpeedCurve);
            Check("P3 curve charge cap (curve table / linear mode regression)", CheckChargeCurve);
            Check("P5 consume priority (highest points per item / hand wins ties / best grid)", CheckConsumePriorityPick);
            Check("P4 containers gate (player / Default container / machine types)", CheckShouldIsolateStorage);
            Check("P4 containers merge (container splits by level / disabled = vanilla)", CheckContainerIsolation);
            Check("P4 containers sort (container 0/4/8 point stacks regrouped)", CheckContainerSortIsolation);
            Check("P6 halo math (ring profile / radius / pulse / quad)", CheckHaloMath);
            Check("P5 A-class factor (curve table / disabled vanilla / linear regression / monotonic)", CheckBonusFactorCurve);
            Check("P5 A-class feature matrix (lab / turret / power / launcher share the curve)", CheckBonusFeatureMatrix);
            Check("P5 sprayer chance (curve 0..100% / clamps / monotonic)", CheckSprayChance);
            Check("P5 sprayer bonus (+1 only / max level / chance 0 / never lowers)", CheckSprayBonus);
            Check("P5 sprayer events (deterministic 15% / stack above max / stateless)", CheckSprayBonusEvents);
            Check("P6 halo config (hex colour / marker style / pulse / spin)", CheckHaloConfig);
            Check("P4 storage grids (perPoint floor / max / base cap / degenerate)", CheckStorageGrids);
            Check("P4 storage total (never negative / reshape predicate)", CheckStorageTotalGrids);
            Check("C2 splitter rule pack (packed int round trip / clamps)", CheckSplitterRulePack);
            Check("C2 splitter rule match (geq N points / any item / no rule)", CheckSplitterRuleMatch);
            Check("C2 splitter route (strictest first / vanilla fallback order)", CheckSplitterRoute);
            Check("C2 splitter output index (physical port -> rotation slot)", CheckSplitterOutputIndex);
            Check("C2/C3 quality threshold cycle (next / prev / validate)", CheckQualityMinCycle);
            Check("C3 sorter pick (skip low quality / item filter / needs)", CheckSorterPick);
            Check("C2 filter UI text (min / item / header / sorter caption)", CheckQualityFilterUiText);
            Check("C2 filter trailer JSON (v0.16.1 nodes round trip / guard)", CheckQualityFilterTrailerJson);
            Check("C2 filter legacy trailer (v0.15 boolean node ignored)", CheckQualityFilterLegacyDoc);
            Check("C2 route vanilla order (no rules = output0..3 order)", CheckSplitterRouteVanillaOrder);
            Check("C2 route strictest first (min desc / equal min keeps port order)", CheckSplitterRouteStrictestFirst);
            Check("C2 route rule-only port (low quality falls back to the rule-less port)", CheckSplitterRouteRuleOnlyFallback);
            Check("C2 route non-output port (rule on an input port is ignored)", CheckSplitterRouteRulePortNotOutput);
            Check("C2 route saturated (all ports ruled / no match = stay on belt)", CheckSplitterRouteSaturated);
            Check("C3 sorter pick needs (care-needs list restricts the item)", CheckSorterPickNeeds);
            Check("C2/C3 threshold extremes (cycle wrap / validation bounds)", CheckQualityMinExtremes);
            Check("C2 cargo points (stack total inc -> per-item points)", CheckCargoPoints);
            Check("C2 splitter fallback (vanilla order / reserved ports excluded)", CheckSplitterFallback);
            Check("C2 splitter fallback rotation (output slot order / holes / inputs)",
                CheckSplitterFallbackRotation);
            Check("C2 route cargo points (low per-item cargo never enters a rule port)",
                CheckSplitterRouteCargoPoints);
            Check("C3 sorter cargo points (stack total inc -> per-item threshold)", CheckSorterCargoPoints);
            Check("C3 sorter per-item tiers (mixed stack order / min 0 regression)", CheckSorterPerItemTiers);
            Check("P5 item badges points (inc/count per-item derivation / clamps / monotonic)",
                CheckBadgePointsFromGrid);
            Check("P5 item badges visibility (feature switch / threshold clamp / degenerate values)",
                CheckBadgeVisibility);
            Check("P5 item badges style+tier (number/dot/both labels / four colour tiers)",
                CheckBadgeStylesAndTiers);
            Check("P5 cfg text (16 keys / legacy keys dropped / values + Chinese comments kept)",
                CheckCfgText);
            Check("P5 miner collector inc (transfer adds count*points / clamps / degenerate)",
                CheckMinerCollectorInc);
            Check("P5 miner collector roll bounds (R-3 half-points cap / chance bounds)",
                CheckMinerCollectorRollBounds);
            Check("P5 assembler pool delta (picks added back / 增产 extras credited / degenerate)",
                CheckProductDelta);
            Check("P5 instant-build per-item (inc/count conversion / clamps / degenerate)",
                CheckInstantBuildPerItem);
            Check("P5 receipt per-item (hundredths floor / cap / monotonic / degenerate)",
                CheckReceiptPerItem);
            Check("P5 receipt item match (station vs normal building / wildcard / mismatch)",
                CheckReceiptItemMatches);
            Check("P5 consume window union (paste / hand build / instant build)",
                CheckConsumeWindowUnion);
            Check("P5 instant-build dilution (count>1 average / station-type parity)",
                CheckInstantDilution);
            Check("P5 prebuild binding value (receipt wins / record fallback / clamp)",
                CheckPreferBindingValue);
            Check("P5 prebuild receipt scenario (backpack build / instant build / vanilla fallback)",
                CheckPrebuildReceiptScenario);
            Check("v0.18.0 upgrade curve chance (shared per-point curve replaces the linear 1.5%)",
                CheckUpgradeCurveChance);
            Check("v0.18.0 cycle upgrade curve (single/double roll shape + 10-point double upgrade)",
                CheckRollCycleLevelCurve);
            Check("v0.18.0 miner guaranteed rolls (two levels per point / 100% chance fast path)",
                CheckMinerGuaranteedRolls);
            Check("v0.18.0 deferred pay per-item (late ghost payment shares the instant-build rule)",
                CheckDeferredPayPerItem);
            Check("v0.19.0 recycler points-to-sprays (4 points = 1 spray use / tank room / carry math)",
                CheckRecyclerBottles);
            Check("v0.19.0 recycler harvest gate (per-item threshold / stack split / min clamp)",
                CheckRecyclerHarvestGate);
            Check("v0.19.0 recycler remainder (carry across harvests / per-entity accounts / stats)",
                CheckRecyclerAccumulation);
            Check("v0.19.0 recycler save node (write / parse round trip / normalise / garbage)",
                CheckRecyclerSaveRoundTrip);
            Check("v0.19.0 sorter filter (belt window decode / storage split_inc / bullets / sources / pick)",
                CheckSorterFilter);
            Check("v0.19.1 batch input level (round half up / clamps / reported 29 of 5 case)",
                CheckBatchInputLevel);

            Console.WriteLine("-----------------------------------------------------");
            if (_failures == 0)
            {
                Console.WriteLine("ALL PASS (" + _checks + " checks)");
                return 0;
            }

            Console.WriteLine("FAILURES: " + _failures + " (of " + _checks + " checks)");
            return 1;
        }

        private static void Check(string name, Action body)
        {
            _checks++;
            try
            {
                body();
                Console.WriteLine("[TEST] " + name + " ... PASS");
            }
            catch (Exception e)
            {
                _failures++;
                Console.WriteLine("[TEST] " + name + " ... FAIL: " + e.Message);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception(message);
            }
        }

        private static void AssertNear(float actual, float expected, float tolerance, string message)
        {
            if (Math.Abs(actual - expected) > tolerance)
            {
                throw new Exception(message + " expected " + expected + " got " + actual);
            }
        }

        // ------------------------------------------------------------------ pools

        private static void CheckPoolConservation()
        {
            var rng = new Random(1234567);
            int poolInc = 0;
            int poolCount = 0;
            long totalAdded = 0;
            long totalTaken = 0;

            for (int i = 0; i < 1000000; i++)
            {
                if (poolCount == 0 || rng.Next(100) < 60)
                {
                    int count = rng.Next(1, 101);
                    int perItem = rng.Next(0, MaxLevel + 1);
                    int inc = perItem * count; // AddPool takes the total points, not the per-item level
                    int newInc;
                    int newCount;
                    QualityMath.AddPool(poolInc, poolCount, inc, count, out newInc, out newCount);
                    poolInc = newInc;
                    poolCount = newCount;
                    totalAdded += inc;
                }
                else
                {
                    int newInc;
                    int newCount;
                    int perItem = QualityMath.TakeOne(poolInc, poolCount, MaxLevel, out newInc, out newCount);
                    Assert(perItem >= 0 && perItem <= MaxLevel, "perItem out of range at op " + i + ": " + perItem);
                    Assert(newCount == poolCount - 1, "take must consume exactly one product at op " + i);
                    poolInc = newInc;
                    poolCount = newCount;
                    totalTaken += perItem;
                }

                Assert(poolInc >= 0, "negative poolInc at op " + i);
                Assert(poolCount >= 0, "negative poolCount at op " + i);
                Assert(poolInc <= (long)poolCount * MaxLevel, "poolInc above max possible quality sum at op " + i);
            }

            Assert(totalAdded - totalTaken == poolInc,
                "quality points lost: added=" + totalAdded + " taken=" + totalTaken + " remaining=" + poolInc);
            Assert(poolCount >= 0, "negative final poolCount");
        }

        private static void CheckPoolEdgeCases()
        {
            int newInc;
            int newCount;

            // taking from an empty pool is a no-op
            int perItem = QualityMath.TakeOne(0, 0, MaxLevel, out newInc, out newCount);
            Assert(perItem == 0 && newInc == 0 && newCount == 0, "empty pool take must be a no-op");

            // adding zero products is a no-op even when points are given
            QualityMath.AddPool(5, 2, 100, 0, out newInc, out newCount);
            Assert(newInc == 5 && newCount == 2, "zero-count add must be a no-op");

            // negative points are clamped to zero
            QualityMath.AddPool(0, 0, -7, 3, out newInc, out newCount);
            Assert(newInc == 0 && newCount == 3, "negative inc must clamp to 0");

            // 4 items worth 10 points total -> 2,2,3,3 points (integer division keeps every point)
            QualityMath.AddPool(0, 0, 10, 4, out newInc, out newCount);
            int taken = 0;
            for (int i = 0; i < 4; i++)
            {
                taken += QualityMath.TakeOne(newInc, newCount, MaxLevel, out newInc, out newCount);
            }
            Assert(taken == 10, "4 items of 10/4 points should give 10 points in total, got " + taken);
            Assert(newInc == 0 && newCount == 0, "integer division must hand out every point for 10/4");
            Assert(newCount == 0, "final pool count must be zero");
        }

        // ------------------------------------------------------------------ clamps

        private static void CheckClamps()
        {
            Assert(QualityMath.ClampLevel(-3, MaxLevel) == 0, "level below zero clamps to 0");
            Assert(QualityMath.ClampLevel(7, MaxLevel) == 7, "level in range is kept");
            Assert(QualityMath.ClampLevel(11, MaxLevel) == MaxLevel, "level above max clamps to max");
            Assert(QualityMath.ClampLevel(5, 0) == 0, "maxLevel 0 forces level 0");
            Assert(QualityMath.ClampLevel(-1, -5) == 0, "negative maxLevel still yields 0");

            Assert(QualityMath.ClampPercent(-1f) == 0f, "percent below zero clamps to 0");
            Assert(QualityMath.ClampPercent(50f) == 50f, "percent in range is kept");
            Assert(QualityMath.ClampPercent(150f) == 100f, "percent above 100 clamps to 100");

            Assert(QualityMath.UpgradeChance(0, 1.5f, 60f) == 0f, "no building points -> no upgrade chance");
            AssertNear(QualityMath.UpgradeChance(4, 1.5f, 60f), 6f, 1e-4f, "4 points * 1.5%/point = 6%");
            AssertNear(QualityMath.UpgradeChance(4, 1000f, 100f), 100f, 1e-4f, "test config must allow a 100% roll");
            AssertNear(QualityMath.UpgradeChance(4, 1000f, 60f), 60f, 1e-4f, "maxUpgradeChance caps the roll");
            Assert(QualityMath.UpgradeChance(4, 1.5f, 0f) == 0f, "maxUpgradeChance 0 disables upgrades");
            Assert(QualityMath.UpgradeChance(4, -2f, 60f) == 0f, "negative chance per point is ignored");

            Assert(QualityMath.MinerChance(0, 2f) == 0f, "no miner points -> no quality");
            AssertNear(QualityMath.MinerChance(4, 2f), 8f, 1e-4f, "4 points * 2%/point = 8%");
            AssertNear(QualityMath.MinerChance(4, 1000f), 100f, 1e-4f, "miner chance clamps at 100%");
            AssertNear(QualityMath.MinerChance(4, 60f), 100f, 1e-4f, "4 * 60%/point clamps at 100%");

            var rng = new Random(99);
            Assert(!QualityMath.RollPercent(0f, rng), "0% roll must always fail");
            Assert(QualityMath.RollPercent(100f, rng), "100% roll must always succeed");
            Assert(!QualityMath.RollPercent(-5f, rng), "negative roll must fail");
            Assert(QualityMath.RollPercent(150f, rng), "roll above 100% must succeed");
        }

        // ------------------------------------------------------------------ json

        private static void CheckJsonRoundTrip()
        {
            string[] tricky =
            {
                "plain",
                "沙盒 save 01",
                "quote\" backslash\\ newline\n tab\t",
                "control\u0001 and \u001f",
                "emoji \ud83d\ude00 surrogate pair"
            };

            foreach (string value in tricky)
            {
                var sb = new StringBuilder();
                JsonLite.WriteString(sb, value);
                object parsed = JsonLite.Parse(sb.ToString());
                Assert(parsed is string, "string literal must parse back to a string");
                Assert((string)parsed == value, "string round trip failed for: " + value.Replace("\n", "\\n"));
            }

            // Escaped input produced by hand must parse too.
            Assert((string)JsonLite.Parse("\"a\\u4e2d\\/b\"") == "a中/b", "unicode escape and \\/ must parse");

            // Full save document using the spec 4.2 shape.
            string gameName = "沙盒 \"A\"";
            var doc = new StringBuilder();
            doc.Append("{\"version\":1,\"gameName\":");
            JsonLite.WriteString(doc, gameName);
            doc.Append(",\"planets\":{\"103\":{\"buildings\":[{\"e\":54,\"i\":4},{\"e\":7,\"i\":0}],");
            doc.Append("\"pools\":[{\"e\":54,\"pi\":12,\"pc\":3}]}}}");

            var root = (Dictionary<string, object>)JsonLite.Parse(doc.ToString());
            Assert((long)root["version"] == 1L, "version must parse as integer 1");
            Assert((string)root["gameName"] == gameName, "gameName must survive the round trip");
            var planets = (Dictionary<string, object>)root["planets"];
            var planet = (Dictionary<string, object>)planets["103"];
            var buildings = (List<object>)planet["buildings"];
            Assert(buildings.Count == 2, "two buildings expected");
            var first = (Dictionary<string, object>)buildings[0];
            Assert((long)first["e"] == 54L && (long)first["i"] == 4L, "building e/i values must round trip");
            var pools = (List<object>)planet["pools"];
            var pool = (Dictionary<string, object>)pools[0];
            Assert((long)pool["pi"] == 12L && (long)pool["pc"] == 3L, "pool pi/pc values must round trip");

            // Scalars and containers.
            Assert((long)JsonLite.Parse("-5") == -5L, "negative integers must parse");
            Assert(Math.Abs((double)JsonLite.Parse("1.5") - 1.5) < 1e-9, "doubles must parse");
            Assert((bool)JsonLite.Parse("true") == true, "true must parse");
            Assert(JsonLite.Parse("null") == null, "null must parse");
            Assert(((Dictionary<string, object>)JsonLite.Parse("{}")).Count == 0, "empty object must parse");
            Assert(((List<object>)JsonLite.Parse("[]")).Count == 0, "empty array must parse");

            bool threw = false;
            try
            {
                JsonLite.Parse("{\"a\":1} trailing");
            }
            catch (FormatException)
            {
                threw = true;
            }
            Assert(threw, "trailing garbage must be rejected");

            threw = false;
            try
            {
                JsonLite.Parse("\"unterminated");
            }
            catch (FormatException)
            {
                threw = true;
            }
            Assert(threw, "unterminated string must be rejected");
        }

        private static void CheckSaveGuard()
        {
            const string doc = "{\"version\":1,\"gameName\":\"沙盒\",\"seed\":424242,"
                + "\"planets\":{\"103\":{\"buildings\":[{\"e\":54,\"i\":4}],\"pools\":[{\"e\":54,\"pi\":7,\"pc\":2}]}}}";
            var root = (Dictionary<string, object>)JsonLite.Parse(doc);

            // Matching identity is accepted; the document itself is untouched.
            Assert(SaveGuard.ValidateDocument(root, "沙盒", 424242) == null,
                "matching gameName + seed must be accepted");

            // A mismatch makes QualitySave.Load discard the whole file (returns 0, memory stays
            // empty) instead of applying foreign points; the validator is the only thing that can fail.
            string seedError = SaveGuard.ValidateDocument(root, "沙盒", 424243);
            Assert(seedError != null && seedError.IndexOf("seed mismatch", StringComparison.Ordinal) >= 0,
                "seed mismatch must be rejected, got: " + (seedError ?? "<null>"));
            Assert(!seedError.Contains("Exception"), "rejection must be a plain reason, not an exception message");

            string nameError = SaveGuard.ValidateDocument(root, "another slot", 424242);
            Assert(nameError != null && nameError.IndexOf("gameName mismatch", StringComparison.Ordinal) >= 0,
                "gameName mismatch must be rejected, got: " + (nameError ?? "<null>"));

            // Legacy file without seed: seed 0 is tolerated, so old saves keep working.
            var legacy = (Dictionary<string, object>)JsonLite.Parse(
                "{\"version\":1,\"gameName\":\"沙盒\",\"planets\":{}}");
            Assert(SaveGuard.ValidateDocument(legacy, "沙盒", 12345) == null,
                "missing seed must be tolerated for legacy files");

            // No game data yet (seed 0) disables the seed check too.
            Assert(SaveGuard.ValidateDocument(root, "沙盒", 0) == null,
                "unknown current seed must disable the seed check");

            // Bad schema version / null root are rejected with a plain reason (Load catches nothing).
            var badVersion = (Dictionary<string, object>)JsonLite.Parse(
                "{\"version\":2,\"gameName\":\"沙盒\",\"seed\":1,\"planets\":{}}");
            string versionError = SaveGuard.ValidateDocument(badVersion, "沙盒", 1);
            Assert(versionError != null && versionError.IndexOf("unsupported schema version 2", StringComparison.Ordinal) >= 0,
                "unknown schema version must be rejected, got: " + (versionError ?? "<null>"));
            Assert(SaveGuard.ValidateDocument(null, "沙盒", 1) != null, "null root must be rejected");

            // Accepted document still carries the full payload (round trip of a matching save).
            var planets = (Dictionary<string, object>)root["planets"];
            var planet = (Dictionary<string, object>)planets["103"];
            var buildings = (List<object>)planet["buildings"];
            var building = (Dictionary<string, object>)buildings[0];
            Assert((long)building["e"] == 54L && (long)building["i"] == 4L,
                "accepted save must keep building data");
            var pools = (List<object>)planet["pools"];
            var pool = (Dictionary<string, object>)pools[0];
            Assert((long)pool["pi"] == 7L && (long)pool["pc"] == 2L, "accepted save must keep pool data");
        }

        // ------------------------------------------------------------------ rolls

        private static void CheckMinerRollDistribution()
        {
            var rng = new Random(777);
            float chance8 = QualityMath.MinerChance(4, 2f);
            float chance2 = QualityMath.MinerChance(1, 2f);
            AssertNear(chance8, 8f, 1e-4f, "4 point miner = 8% per item");
            AssertNear(chance2, 2f, 1e-4f, "1 point miner = 2% per item");

            const int trials = 1000000;
            int hits8 = 0;
            int hits2 = 0;
            for (int i = 0; i < trials; i++)
            {
                if (QualityMath.RollPercent(chance8, rng))
                {
                    hits8++;
                }
                if (QualityMath.RollPercent(chance2, rng))
                {
                    hits2++;
                }
            }

            double rate8 = hits8 * 100.0 / trials;
            double rate2 = hits2 * 100.0 / trials;
            Assert(rate8 > 7.7 && rate8 < 8.3, "8% miner roll rate out of tolerance: " + rate8);
            Assert(rate2 > 1.7 && rate2 < 2.3, "2% miner roll rate out of tolerance: " + rate2);
            Console.WriteLine("       miner rates: 8% -> " + rate8.ToString("F3") + "%, 2% -> " + rate2.ToString("F3") + "%");

            float guaranteed = QualityMath.MinerChance(4, 1000f);
            for (int i = 0; i < 10000; i++)
            {
                Assert(QualityMath.RollPercent(guaranteed, rng), "100% miner chance must always emit a point");
            }
        }

        private static void CheckUpgradeRoll()
        {
            var rng = new Random(2024);
            float chance = QualityMath.UpgradeChance(4, 1000f, 100f);
            AssertNear(chance, 100f, 1e-4f, "test config must give a 100% upgrade roll");

            int level = 0;
            int cycles = 0;
            while (level < MaxLevel && cycles < 100)
            {
                if (QualityMath.RollPercent(chance, rng))
                {
                    level++;
                }
                cycles++;
            }
            Assert(level == MaxLevel, "100% upgrade must reach max level, stopped at " + level);
            Assert(cycles <= MaxLevel, "100% upgrade needed too many cycles: " + cycles);

            float capped = QualityMath.UpgradeChance(4, 1000f, 60f);
            AssertNear(capped, 60f, 1e-4f, "maxUpgradeChance must cap the upgrade roll");
            float six = QualityMath.UpgradeChance(4, 1.5f, 60f);
            AssertNear(six, 6f, 1e-4f, "default 4 point building = 6% per cycle");

            const int trials = 1000000;
            int upgrades = 0;
            for (int i = 0; i < trials; i++)
            {
                if (QualityMath.RollPercent(six, rng))
                {
                    upgrades++;
                }
            }
            double rate = upgrades * 100.0 / trials;
            Assert(rate > 5.7 && rate < 6.3, "6% upgrade roll rate out of tolerance: " + rate);
            Console.WriteLine("       upgrade rate at 6%: " + rate.ToString("F3") + "%");
        }

        // ---------------------------------------------------------- fractionator

        private static void CheckFractionatorInputLevel()
        {
            Assert(QualityMath.FractionatorInputLevel(0, 0, MaxLevel) == 0, "empty input must give level 0");
            Assert(QualityMath.FractionatorInputLevel(100, 10, MaxLevel) == 10, "10 items x 10 inc = level 10");
            Assert(QualityMath.FractionatorInputLevel(10, 3, MaxLevel) == 3, "a single item carries its own level");
            Assert(QualityMath.FractionatorInputLevel(100, 10, 4) == 4, "clamp to the configured max level");
            Assert(QualityMath.FractionatorInputLevel(150, 10, MaxLevel) == MaxLevel, "clamp to MaxLevel");
            Assert(QualityMath.FractionatorInputLevel(9, 10, MaxLevel) == 0, "mixed stack floors the average");
            Assert(QualityMath.FractionatorInputLevel(-5, 10, MaxLevel) == 0, "negative inc -> 0");
            Assert(QualityMath.FractionatorInputLevel(5, -1, MaxLevel) == 0, "negative count -> 0");
            Assert(QualityMath.FractionatorInputLevel(5, 0, MaxLevel) == 0, "zero count -> 0");
            Assert(QualityMath.FractionatorInputLevel(0, 10, MaxLevel) == 0, "zero inc -> 0");
            Assert(QualityMath.FractionatorInputLevel(5, 5, 0) == 0, "maxLevel 0 clamps everything to 0");
        }

        private static void CheckFractionatorPoolRoundTrip()
        {
            var rng = new Random(777);

            // 1) one successful cycle per shipped item: the shipped inc must equal the level exactly
            for (int i = 0; i < 100000; i++)
            {
                int level = QualityMath.FractionatorInputLevel(rng.Next(0, MaxLevel + 1) * 10, 10, MaxLevel);
                int poolInc, poolCount, newInc, newCount;
                QualityMath.AddPool(0, 0, level, 1, out poolInc, out poolCount);
                int shipped = QualityMath.TakeOne(poolInc, poolCount, MaxLevel, out newInc, out newCount);
                Assert(shipped == level, "shipped " + shipped + " != produced level " + level);
                Assert(newInc == 0 && newCount == 0, "a single-item pool must drain completely");
            }

            // 2) failed belt insert: take + rollback must restore the pool exactly (atomic ship)
            for (int i = 0; i < 100000; i++)
            {
                int level = rng.Next(0, MaxLevel + 1);
                int poolInc, poolCount, newInc, newCount;
                QualityMath.AddPool(0, 0, level, 1, out poolInc, out poolCount);
                int taken = QualityMath.TakeOne(poolInc, poolCount, MaxLevel, out newInc, out newCount);
                QualityMath.AddPool(newInc, newCount, taken, 1, out poolInc, out poolCount);
                Assert(poolInc == level && poolCount == 1,
                    "rollback must restore inc=" + level + " count=1, got inc=" + poolInc + " count=" + poolCount);
            }

            // 3) mixed backlog: conservation across random produce/ship steps
            int mInc = 0;
            int mCount = 0;
            long added = 0;
            long takenTotal = 0;
            for (int i = 0; i < 200000; i++)
            {
                int level = QualityMath.FractionatorInputLevel(rng.Next(0, (MaxLevel + 1) * 10), rng.Next(1, 11), MaxLevel);
                int newInc;
                int newCount;
                QualityMath.AddPool(mInc, mCount, level, 1, out newInc, out newCount);
                mInc = newInc;
                mCount = newCount;
                added += level;

                if (mCount > 0 && rng.Next(100) < 70)
                {
                    int shipped = QualityMath.TakeOne(mInc, mCount, MaxLevel, out newInc, out newCount);
                    Assert(shipped >= 0 && shipped <= MaxLevel, "shipped inc out of range at op " + i + ": " + shipped);
                    mInc = newInc;
                    mCount = newCount;
                    takenTotal += shipped;
                }

                Assert(mInc >= 0 && mCount >= 0, "negative pool at op " + i);
                Assert(mInc <= (long)mCount * MaxLevel, "pool above the max possible sum at op " + i);
            }
            Assert(added - takenTotal == mInc,
                "quality points lost: added=" + added + " taken=" + takenTotal + " remaining=" + mInc);
        }

    }
}
