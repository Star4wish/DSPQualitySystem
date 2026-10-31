using System;

namespace DSPQualitySystem.OfflineTests
{
    /// <summary>
    /// v0.16.0 (C-2 + C-3): pure math behind the splitter per-output-port quality filter and the
    /// sorter quality threshold. Covers rule packing, the "geq N points" match, the candidate
    /// ordering (strictest rule first, vanilla order for the rule-less fallback), the UI threshold
    /// cycle and the sorter window scan.
    ///
    /// Registration is NOT part of this file (P1 keeps the shared test projects untouched); the
    /// integration step appends these lines to tests\OfflineTests\Program.cs Main():
    ///   Check("C2 splitter rule pack (packed int round trip / clamps)", CheckSplitterRulePack);
    ///   Check("C2 splitter rule match (geq N points / any item / no rule)", CheckSplitterRuleMatch);
    ///   Check("C2 splitter route (strictest first / vanilla fallback order)", CheckSplitterRoute);
    ///   Check("C2 splitter output index (physical port -&gt; rotation slot)", CheckSplitterOutputIndex);
    ///   Check("C2/C3 quality threshold cycle (next / prev / validate)", CheckQualityMinCycle);
    ///   Check("C3 sorter pick (skip low quality / item filter / needs)", CheckSorterPick);
    /// </summary>
    internal static partial class Program
    {
        private static void CheckSplitterRulePack()
        {
            Assert(QualityMath.PackSplitterRule(0, 0) == QualityMath.SplitterRuleNone,
                "any item at any quality carries no rule");
            Assert(QualityMath.SplitterRuleIsEmpty(QualityMath.SplitterRuleNone), "zero packed value is empty");

            int magnet5 = QualityMath.PackSplitterRule(1001, 5);
            Assert(magnet5 != QualityMath.SplitterRuleNone, "magnet >= 5 packs to a live rule");
            Assert(QualityMath.SplitterRuleItem(magnet5) == 1001, "packed rule keeps the item id");
            Assert(QualityMath.SplitterRuleMin(magnet5) == 5, "packed rule keeps the minimum points");
            Assert(!QualityMath.SplitterRuleIsEmpty(magnet5), "live rule is not empty");

            int itemOnly = QualityMath.PackSplitterRule(1001, 0);
            Assert(itemOnly != QualityMath.SplitterRuleNone, "item-only rule is still a rule");
            Assert(QualityMath.SplitterRuleMin(itemOnly) == 0, "item-only rule has min 0");

            int anyItem5 = QualityMath.PackSplitterRule(0, 5);
            Assert(anyItem5 != QualityMath.SplitterRuleNone, "quality-only rule is still a rule");
            Assert(QualityMath.SplitterRuleItem(anyItem5) == 0, "quality-only rule has item 0");

            Assert(QualityMath.SplitterRuleMin(QualityMath.PackSplitterRule(1001, 11)) == 10,
                "minimum points clamp down to 10");
            Assert(QualityMath.SplitterRuleMin(QualityMath.PackSplitterRule(1001, -2)) == 0,
                "negative minimum points clamp to 0");
            Assert(QualityMath.PackSplitterRule(-1, 5) == QualityMath.SplitterRuleNone,
                "negative item id collapses to no rule");
            Assert(QualityMath.PackSplitterRule(QualityMath.SplitterRuleItemMask + 1, 5) == QualityMath.SplitterRuleNone,
                "item id above the 16 bit field collapses to no rule");

            int wide = QualityMath.PackSplitterRule(QualityMath.SplitterRuleItemMask, 10);
            Assert(QualityMath.SplitterRuleItem(wide) == QualityMath.SplitterRuleItemMask,
                "largest item id round trips");
            Assert(QualityMath.SplitterRuleMin(wide) == 10, "largest minimum points round trips");

            Assert(QualityMath.ClampQualityPoints(-3) == 0, "clamp -3 -> 0");
            Assert(QualityMath.ClampQualityPoints(0) == 0, "clamp 0 -> 0");
            Assert(QualityMath.ClampQualityPoints(5) == 5, "clamp 5 -> 5");
            Assert(QualityMath.ClampQualityPoints(10) == 10, "clamp 10 -> 10");
            Assert(QualityMath.ClampQualityPoints(42) == 10, "clamp 42 -> 10");
        }

        private static void CheckSplitterRuleMatch()
        {
            int magnet5 = QualityMath.PackSplitterRule(1001, 5);
            Assert(QualityMath.SplitterRuleMatches(magnet5, 1001, 5), "equal quality satisfies geq N");
            Assert(QualityMath.SplitterRuleMatches(magnet5, 1001, 7), "higher quality satisfies geq N");
            Assert(QualityMath.SplitterRuleMatches(magnet5, 1001, 10), "max quality satisfies geq N");
            Assert(!QualityMath.SplitterRuleMatches(magnet5, 1001, 4), "one point below the minimum fails");
            Assert(!QualityMath.SplitterRuleMatches(magnet5, 1001, 0), "zero quality fails a geq 5 rule");
            Assert(!QualityMath.SplitterRuleMatches(magnet5, 1101, 9), "another item fails an item rule");
            Assert(!QualityMath.SplitterRuleMatches(magnet5, 1001, -1), "negative quality never matches");
            Assert(!QualityMath.SplitterRuleMatches(QualityMath.SplitterRuleNone, 1001, 9),
                "no rule never matches");

            int anyItem5 = QualityMath.PackSplitterRule(0, 5);
            Assert(QualityMath.SplitterRuleMatches(anyItem5, 1001, 5), "any-item rule matches item 1001");
            Assert(QualityMath.SplitterRuleMatches(anyItem5, 2002, 10), "any-item rule matches item 2002");
            Assert(!QualityMath.SplitterRuleMatches(anyItem5, 2002, 4), "any-item rule still needs the quality");

            int itemOnly = QualityMath.PackSplitterRule(1001, 0);
            Assert(QualityMath.SplitterRuleMatches(itemOnly, 1001, 0), "item-only rule accepts quality 0");
            Assert(QualityMath.SplitterRuleMatches(itemOnly, 1001, 10), "item-only rule accepts any quality");
            Assert(!QualityMath.SplitterRuleMatches(itemOnly, 1101, 5), "item-only rule rejects other items");
        }

        private static void CheckSplitterRoute()
        {
            // Four connected outputs, physical ports 0..3 map to belts 11..14.
            int[] belts = new int[] { 11, 12, 13, 14 };
            int[] outputs = new int[] { 11, 12, 13, 14 };
            int[] rules = new int[4];
            int[] candidates = new int[4];

            int count = QualityMath.PlanSplitterRoute(1001, 5, rules, belts, outputs, candidates);
            Assert(count == 4, "no rules: every output port is a candidate");
            Assert(candidates[0] == 0 && candidates[1] == 1 && candidates[2] == 2 && candidates[3] == 3,
                "no rules: candidates follow the vanilla output order");

            rules[0] = QualityMath.PackSplitterRule(1001, 5);
            count = QualityMath.PlanSplitterRoute(1001, 7, rules, belts, outputs, candidates);
            Assert(count == 4 && candidates[0] == 0, "matching rule port is tried first");
            count = QualityMath.PlanSplitterRoute(1001, 3, rules, belts, outputs, candidates);
            Assert(count == 3, "low quality skips the rule port");
            Assert(candidates[0] == 1 && candidates[1] == 2 && candidates[2] == 3,
                "low quality falls back to the rule-less ports in vanilla order");
            count = QualityMath.PlanSplitterRoute(1101, 9, rules, belts, outputs, candidates);
            Assert(count == 3 && candidates[0] == 1, "another item falls back to the rule-less ports");
            count = QualityMath.PlanSplitterRoute(1001, 5, rules, belts, outputs, candidates);
            Assert(count == 4 && candidates[0] == 0, "exactly N points satisfies the rule (inclusive geq)");

            // Two tiers of the same item: the stricter rule owns the better cargo.
            rules[1] = QualityMath.PackSplitterRule(1001, 2);
            count = QualityMath.PlanSplitterRoute(1001, 3, rules, belts, outputs, candidates);
            Assert(count == 3 && candidates[0] == 1, "middle quality goes to the geq 2 port");
            count = QualityMath.PlanSplitterRoute(1001, 7, rules, belts, outputs, candidates);
            Assert(count == 4 && candidates[0] == 0 && candidates[1] == 1,
                "best quality goes to the strictest port first, then the looser one");
            count = QualityMath.PlanSplitterRoute(1001, 1, rules, belts, outputs, candidates);
            Assert(count == 2 && candidates[0] == 2 && candidates[1] == 3,
                "below every tier falls back to the rule-less ports only");

            // A rule on a later port is still tried before the rule-less ports.
            rules[2] = QualityMath.PackSplitterRule(1001, 5);
            count = QualityMath.PlanSplitterRoute(1001, 5, rules, belts, outputs, candidates);
            Assert(count == 4 && candidates[0] == 0 && candidates[1] == 2,
                "equal minimum: ascending port order is preserved");

            // Ties keep ascending port order; rule-less ports keep the vanilla order.
            rules[3] = QualityMath.PackSplitterRule(1001, 5);
            count = QualityMath.PlanSplitterRoute(1001, 5, rules, belts, outputs, candidates);
            Assert(count == 4, "four matching ports are all candidates");
            Assert(candidates[0] == 0 && candidates[1] == 2 && candidates[2] == 3,
                "geq 5 ports keep ascending port order and outrank the looser geq 2 port");
            Assert(candidates[3] == 1, "the looser geq 2 port is tried last");

            // All four ports reserved for another item: nothing may take this cargo.
            for (int i = 0; i < 4; i++)
            {
                rules[i] = QualityMath.PackSplitterRule(1101, 1);
            }

            count = QualityMath.PlanSplitterRoute(1001, 9, rules, belts, outputs, candidates);
            Assert(count == 0, "every port reserved for another item leaves no candidate (cargo waits)");

            // A port that is currently an input must never become a destination.
            int[] rulesPort0 = new int[4];
            rulesPort0[0] = QualityMath.PackSplitterRule(1001, 5);
            int[] outputsNoPort0 = new int[] { 12, 13, 14, 0 };
            count = QualityMath.PlanSplitterRoute(1001, 9, rulesPort0, belts, outputsNoPort0, candidates);
            Assert(count == 3, "a rule port that is currently an input is skipped");
            Assert(candidates[0] == 1 && candidates[1] == 2 && candidates[2] == 3,
                "skipped rule port is not replaced by anything else");

            // Item 0 rule (quality only) can own any item at the port.
            int[] anyRules = new int[4];
            anyRules[3] = QualityMath.PackSplitterRule(0, 6);
            count = QualityMath.PlanSplitterRoute(2002, 6, anyRules, belts, outputs, candidates);
            Assert(count == 4 && candidates[0] == 3, "any-item quality rule owns its port for any item");

            // Degenerate input.
            Assert(QualityMath.PlanSplitterRoute(1001, 5, null, belts, outputs, candidates) == 0,
                "null rules gives no candidate");
            Assert(QualityMath.PlanSplitterRoute(1001, 5, rules, belts, outputs, null) == 0,
                "null candidate buffer gives no candidate");
        }

        private static void CheckSplitterOutputIndex()
        {
            int[] outputs = new int[] { 11, 12, 13, 14 };
            Assert(QualityMath.FindOutputIndex(outputs, 11) == 0, "belt A is output slot 0");
            Assert(QualityMath.FindOutputIndex(outputs, 13) == 2, "belt C is output slot 2");
            Assert(QualityMath.FindOutputIndex(outputs, 14) == 3, "belt D is output slot 3");
            Assert(QualityMath.FindOutputIndex(outputs, 99) == -1, "unknown belt is not an output");
            Assert(QualityMath.FindOutputIndex(outputs, 0) == -1, "empty belt is not an output");
            Assert(QualityMath.FindOutputIndex(null, 11) == -1, "null rotation has no outputs");

            int[] holes = new int[] { 0, 12, 0, 14 };
            Assert(QualityMath.FindOutputIndex(holes, 12) == 1, "hole at output 0 keeps later slots");
            Assert(QualityMath.FindOutputIndex(holes, 14) == 3, "hole at output 2 keeps later slots");
            Assert(QualityMath.FindOutputIndex(holes, 0) == -1, "an empty slot is never matched");
        }

        private static void CheckQualityMinCycle()
        {
            Assert(QualityMath.NextQualityMin(0) == 1, "next: 0 -> 1");
            Assert(QualityMath.NextQualityMin(5) == 6, "next: 5 -> 6");
            Assert(QualityMath.NextQualityMin(9) == 10, "next: 9 -> 10");
            Assert(QualityMath.NextQualityMin(10) == 0, "next: 10 wraps to 0 (no limit)");
            Assert(QualityMath.NextQualityMin(-4) == 1, "next clamps negative input first");
            Assert(QualityMath.NextQualityMin(11) == 0, "next clamps above-max input first");

            Assert(QualityMath.PrevQualityMin(0) == 10, "prev: 0 wraps to 10");
            Assert(QualityMath.PrevQualityMin(1) == 0, "prev: 1 -> 0");
            Assert(QualityMath.PrevQualityMin(10) == 9, "prev: 10 -> 9");
            Assert(QualityMath.PrevQualityMin(5) == 4, "prev: 5 -> 4");
            Assert(QualityMath.PrevQualityMin(-2) == 10, "prev clamps negative input first");
            Assert(QualityMath.PrevQualityMin(12) == 9, "prev clamps above-max input first");

            Assert(QualityMath.ValidateSorterMin(0), "sorter min 0 is valid");
            Assert(QualityMath.ValidateSorterMin(10), "sorter min 10 is valid");
            Assert(QualityMath.ValidateSorterMin(5), "sorter min 5 is valid");
            Assert(!QualityMath.ValidateSorterMin(-1), "sorter min -1 is invalid");
            Assert(!QualityMath.ValidateSorterMin(11), "sorter min 11 is invalid");
        }

        private static void CheckSorterPick()
        {
            int[] items = new int[] { 1001, 1001, 1001 };
            int[] incs = new int[] { 3, 7, 5 };
            Assert(QualityMath.PlanSorterPick(items, incs, 3, 1001, 5, null) == 1,
                "the first cargo reaching the threshold wins (skip the low quality head)");
            Assert(QualityMath.PlanSorterPick(items, incs, 3, 1001, 0, null) == 0,
                "threshold 0 reproduces the vanilla first-match behaviour");
            Assert(QualityMath.PlanSorterPick(items, incs, 3, 1001, 8, null) == -1,
                "no cargo reaches a threshold above every cargo");
            Assert(QualityMath.PlanSorterPick(items, incs, 3, 1101, 0, null) == -1,
                "item filter mismatches never match");
            Assert(QualityMath.PlanSorterPick(items, incs, 3, 0, 6, null) == 1,
                "filter 0 with a threshold picks the first cargo reaching it");

            int[] mixedItems = new int[] { 1001, 1101 };
            int[] mixedIncs = new int[] { 9, 9 };
            int[] needs = new int[] { 1101 };
            Assert(QualityMath.PlanSorterPick(mixedItems, mixedIncs, 2, 0, 0, needs) == 1,
                "care-needs filter is applied on top of the item filter");
            Assert(QualityMath.PlanSorterPick(mixedItems, new int[] { 9, 1 }, 2, 0, 5, needs) == -1,
                "care-needs item below the threshold does not match");

            int[] withEmpty = new int[] { 0, 1001 };
            int[] emptyIncs = new int[] { 9, 9 };
            Assert(QualityMath.PlanSorterPick(withEmpty, emptyIncs, 2, 0, 0, null) == 1,
                "empty cargo slots are skipped");

            Assert(QualityMath.PlanSorterPick(items, incs, 0, 1001, 0, null) == -1, "empty window finds nothing");
            Assert(QualityMath.PlanSorterPick(null, incs, 3, 1001, 0, null) == -1, "null items find nothing");
            Assert(QualityMath.PlanSorterPick(items, null, 3, 1001, 0, null) == -1, "null incs find nothing");
            Assert(QualityMath.PlanSorterPick(items, incs, 99, 1001, 0, null) == 0,
                "count larger than the arrays is clamped to the array length");
        }

        // ---------------------------------------------------------------- v0.17.1 live-bug guards
        // The two live reports were "splitter lets plain cargo into a filtered port" and "sorter
        // quality filter does nothing". Both came from comparing the vanilla cargo inc - the TOTAL
        // point pool of the whole STACK - against the per-item minimum. These checks pin the
        // conversion, the rule-less fallback and the block-on-low-quality behaviour.

        private static void CheckCargoPoints()
        {
            Assert(QualityMath.PerItemPoints(0, 1) == 0, "0 total points is 0 points per item");
            Assert(QualityMath.PerItemPoints(8, 1) == 8, "a single item carries the whole pool");
            Assert(QualityMath.PerItemPoints(4, 4) == 1, "4 points over 4 items is 1 point per item");
            Assert(QualityMath.PerItemPoints(16, 4) == 4, "16 points over 4 items is 4 points per item");
            Assert(QualityMath.PerItemPoints(1, 2) == 0, "1 stray point over 2 items floors to 0");
            Assert(QualityMath.PerItemPoints(3, 2) == 1, "3 points over 2 items floors to 1");
            Assert(QualityMath.PerItemPoints(23, 100) == 0, "23 points over 100 items floors to 0");
            Assert(QualityMath.PerItemPoints(2, 0) == 0, "an empty stack has no points per item");
            Assert(QualityMath.PerItemPoints(2, -1) == 0, "a negative stack has no points per item");
        }

        private static void CheckSplitterFallback()
        {
            int[] belts = new int[] { 11, 12, 13, 14 };
            int[] outputs = new int[] { 11, 12, 13, 14 };
            int[] rules = new int[4];
            int[] candidates = new int[4];

            int count = QualityMath.PlanSplitterFallback(rules, belts, outputs, candidates);
            Assert(count == 4, "no rules: every output port is a fallback destination");
            Assert(candidates[0] == 0 && candidates[1] == 1 && candidates[2] == 2 && candidates[3] == 3,
                "no rules: fallback keeps the vanilla output0..3 order");

            // THE live bug: the vanilla outFilter may point at port 0 while port 0 is reserved by a
            // rule. The fallback must never offer that port, so vanilla first-fit cannot feed it.
            rules[0] = QualityMath.PackSplitterRule(1001, 4);
            count = QualityMath.PlanSplitterFallback(rules, belts, outputs, candidates);
            Assert(count == 3, "a reserved port leaves the fallback");
            Assert(candidates[0] == 1 && candidates[1] == 2 && candidates[2] == 3,
                "the reserved port 0 is skipped in favour of the rule-less ports");

            rules[1] = QualityMath.PackSplitterRule(0, 1);
            count = QualityMath.PlanSplitterFallback(rules, belts, outputs, candidates);
            Assert(count == 2 && candidates[0] == 2 && candidates[1] == 3,
                "two reserved ports leave only the two rule-less ports");

            for (int i = 0; i < 4; i++)
            {
                rules[i] = QualityMath.PackSplitterRule(1101, 1);
            }

            count = QualityMath.PlanSplitterFallback(rules, belts, outputs, candidates);
            Assert(count == 0, "a fully reserved splitter offers no fallback (cargo waits on the belt)");

            Assert(QualityMath.PlanSplitterFallback(null, belts, outputs, candidates) == 0,
                "null rules gives no fallback");
            Assert(QualityMath.PlanSplitterFallback(rules, belts, outputs, null) == 0,
                "null candidate buffer gives no fallback");
        }

        private static void CheckSplitterFallbackRotation()
        {
            int[] belts = new int[] { 11, 12, 13, 14 };
            int[] rules = new int[4];
            int[] candidates = new int[4];

            // The vanilla rotation can put the physical ports in any order; the fallback follows the
            // rotation (output0..3), not the port order, so "first fit" stays vanilla-shaped.
            int[] rotated = new int[] { 13, 11, 14, 12 };
            rules[0] = QualityMath.PackSplitterRule(1001, 4);
            int count = QualityMath.PlanSplitterFallback(rules, belts, rotated, candidates);
            Assert(count == 3, "rotated outputs: one reserved port is dropped");
            Assert(candidates[0] == 2 && candidates[1] == 3 && candidates[2] == 1,
                "fallback follows output slot order, skipping the reserved port 0");

            // Empty output slots are holes, never destinations.
            int[] holes = new int[] { 0, 12, 0, 14 };
            int[] noRules = new int[4];
            count = QualityMath.PlanSplitterFallback(noRules, belts, holes, candidates);
            Assert(count == 2 && candidates[0] == 1 && candidates[1] == 3, "empty output slots are skipped");

            // A rule on a port that currently feeds an INPUT must not resurrect that belt.
            int[] rulesOnInput = new int[4];
            rulesOnInput[2] = QualityMath.PackSplitterRule(1001, 4);
            int[] outputsNoPort2 = new int[] { 11, 12, 0, 14 };
            count = QualityMath.PlanSplitterFallback(rulesOnInput, belts, outputsNoPort2, candidates);
            Assert(count == 3 && candidates[0] == 0 && candidates[1] == 1 && candidates[2] == 3,
                "rule port that is currently an input is not a destination");
        }

        private static void CheckSplitterRouteCargoPoints()
        {
            int[] belts = new int[] { 11, 12, 13, 14 };
            int[] outputs = new int[] { 11, 12, 13, 14 };
            int[] rules = new int[4];
            int[] candidates = new int[4];

            // Port 2 is reserved for "copper ore >= 4 points".
            rules[2] = QualityMath.PackSplitterRule(1002, 4);

            // A 4-piece stack carrying 4 total points shows 1 point per item in the UI. The raw total
            // would satisfy the rule, the per-item value must not - this is exactly the live leak.
            Assert(QualityMath.SplitterRuleMatches(rules[2], 1002, 4),
                "raw total 4 would have matched the old (wrong) comparison");
            int stackedPoints = QualityMath.PerItemPoints(4, 4);
            Assert(!QualityMath.SplitterRuleMatches(rules[2], 1002, stackedPoints),
                "a 4-piece stack with 1 point per item must not match a >= 4 rule");
            int count = QualityMath.PlanSplitterRoute(1002, stackedPoints, rules, belts, outputs, candidates);
            Assert(count == 3, "stacked low quality cargo cannot enter the reserved port");
            Assert(candidates[0] == 0 && candidates[1] == 1 && candidates[2] == 3,
                "stacked low quality cargo falls back to the rule-less ports");

            // Two 4-point items stacked with 8 total points still show 4 points per item.
            int goodPoints = QualityMath.PerItemPoints(8, 2);
            Assert(goodPoints == 4, "8 points over 2 items is 4 points per item");
            count = QualityMath.PlanSplitterRoute(1002, goodPoints, rules, belts, outputs, candidates);
            Assert(count == 4 && candidates[0] == 2, "genuine quality cargo goes to the reserved port first");

            // A single item with 8 points passes even the strictest rule.
            count = QualityMath.PlanSplitterRoute(1002, QualityMath.PerItemPoints(8, 1), rules, belts,
                outputs, candidates);
            Assert(count == 4 && candidates[0] == 2, "a single high quality item is routed to the rule port");

            // min 0 (item-only rule) keeps accepting everything of the item, whatever the stack.
            int[] itemOnly = new int[4];
            itemOnly[1] = QualityMath.PackSplitterRule(1002, 0);
            count = QualityMath.PlanSplitterRoute(1002, QualityMath.PerItemPoints(2, 2), itemOnly, belts,
                outputs, candidates);
            Assert(count == 4 && candidates[0] == 1, "item-only rule accepts a low quality stack");
        }

        private static void CheckSorterCargoPoints()
        {
            int[] items = new int[] { 1002, 1002, 1002 };
            // Realistic stack totals (inc is the pool of the whole stack): 4 items with 4 points show
            // 1 point each, 2 items with 4 points show 2 each, a single item with 4 points shows 4.
            int[] points = new int[]
            {
                QualityMath.PerItemPoints(4, 4),
                QualityMath.PerItemPoints(4, 2),
                QualityMath.PerItemPoints(4, 1)
            };

            Assert(points[0] == 1 && points[1] == 2 && points[2] == 4, "stack totals convert per item");
            Assert(QualityMath.PlanSorterPick(items, points, 3, 1002, 4, null) == 2,
                "only the cargo with 4 points per item reaches a >= 4 threshold");
            Assert(QualityMath.PlanSorterPick(items, points, 3, 1002, 2, null) == 1,
                "the first cargo reaching >= 2 per-item points is picked");
            Assert(QualityMath.PlanSorterPick(items, points, 3, 1002, 5, null) == -1,
                "nothing reaches >= 5: the sorter must idle instead of grabbing a cheap stack");
            Assert(QualityMath.PlanSorterPick(items, points, 3, 1002, 0, null) == 0,
                "threshold 0 keeps the vanilla first-match behaviour");
        }

        private static void CheckSorterPerItemTiers()
        {
            int[] items = new int[] { 1002, 1002, 1002 };
            int[] points = new int[]
            {
                QualityMath.PerItemPoints(1, 2),   // 1 point over 2 items = 0 points per item
                QualityMath.PerItemPoints(3, 4),   // 3 points over 4 items = 0 points per item
                QualityMath.PerItemPoints(8, 1)    // 8 points per item
            };

            Assert(QualityMath.PlanSorterPick(items, points, 3, 1002, 4, null) == 2,
                "per-item tiers: only the high quality item qualifies");
            Assert(QualityMath.PlanSorterPick(items, points, 3, 1002, 1, null) == 2,
                "per-item tiers: a >= 1 threshold still skips both 0-point stacks");
            Assert(QualityMath.PlanSorterPick(items, points, 3, 0, 4, null) == 2,
                "filter 0 with a per-item threshold picks the qualifying cargo");
            Assert(QualityMath.PlanSorterPick(items, points, 3, 1002, 9, null) == -1,
                "above the best cargo: the sorter idles");
            Assert(QualityMath.PlanSorterPick(items, points, 3, 0, 4, new int[] { 1002 }) == 2,
                "care-needs filter is applied on top of the per-item threshold");
        }
    }
}
