using System;
using System.Collections.Generic;
using DSPQualitySystem.Save;

namespace DSPQualitySystem.OfflineTests
{
    /// <summary>
    /// v0.16.1 (C-2 / C-3): UI captions, MOD trailer nodes and the routing edge cases that the
    /// shorter checks in Checks.SplitterFilter.cs do not cover. Everything here is pure code, so it
    /// runs without the game.
    ///
    /// Registered from Program.cs Main():
    ///   Check("C2 filter UI text (min / item / header / sorter caption)", CheckQualityFilterUiText);
    ///   Check("C2 filter trailer JSON (v0.16.1 nodes round trip / guard)", CheckQualityFilterTrailerJson);
    ///   Check("C2 filter legacy trailer (v0.15 boolean node ignored)", CheckQualityFilterLegacyDoc);
    ///   Check("C2 route vanilla order (no rules = output0..3 order)", CheckSplitterRouteVanillaOrder);
    ///   Check("C2 route strictest first (min desc / equal min keeps port order)",
    ///       CheckSplitterRouteStrictestFirst);
    ///   Check("C2 route rule-only port (low quality falls back to the rule-less port)",
    ///       CheckSplitterRouteRuleOnlyFallback);
    ///   Check("C2 route non-output port (rule on an input port is ignored)", CheckSplitterRouteRulePortNotOutput);
    ///   Check("C2 route saturated (all ports ruled / no match = stay on belt)", CheckSplitterRouteSaturated);
    ///   Check("C3 sorter pick needs (care-needs list restricts the item)", CheckSorterPickNeeds);
    ///   Check("C2/C3 threshold extremes (cycle wrap / validation bounds)", CheckQualityMinExtremes);
    /// </summary>
    internal static partial class Program
    {
        private static void CheckQualityFilterUiText()
        {
            Assert(QualityMath.SplitterRuleMinText(0) == "不限", "threshold 0 renders as unrestricted");
            Assert(QualityMath.SplitterRuleMinText(1) == "≥1", "threshold 1 renders geq 1");
            Assert(QualityMath.SplitterRuleMinText(10) == "≥10", "threshold 10 renders geq 10");
            Assert(QualityMath.SplitterRuleMinText(-4) == "不限", "negative threshold renders unrestricted");
            Assert(QualityMath.SplitterRuleMinText(99) == "≥10", "above-max threshold renders geq 10");

            Assert(QualityMath.SplitterRuleItemText(0, "Magnet") == "任意物品", "item 0 renders any-item");
            Assert(QualityMath.SplitterRuleItemText(1001, "Magnet") == "Magnet", "known item keeps its name");
            Assert(QualityMath.SplitterRuleItemText(1001, null) == "#1001", "unknown name falls back to #id");
            Assert(QualityMath.SplitterRuleItemText(1001, "") == "#1001", "empty name falls back to #id");

            string folded = QualityMath.SplitterPanelHeaderText(false, 2);
            Assert(folded.Contains("▸"), "folded header carries the collapsed marker");
            Assert(folded.Contains("2/4"), "folded header shows the rule count");
            string expanded = QualityMath.SplitterPanelHeaderText(true, 0);
            Assert(expanded.Contains("▾"), "expanded header carries the open marker");
            Assert(QualityMath.SplitterPanelHeaderText(true, 9).Contains("4/4"), "header clamps the count");

            Assert(QualityMath.SorterFilterText("Magnet", 0) == "Magnet", "sorter caption keeps the name at 0");
            Assert(QualityMath.SorterFilterText("Magnet", 5) == "Magnet  品质≥5", "sorter caption appends the threshold");
            Assert(QualityMath.SorterFilterText(null, 3) == "任意物品  品质≥3", "sorter caption handles no item");
        }

        private static void CheckQualityFilterTrailerJson()
        {
            string doc = "{\"version\":1,\"gameName\":\"沙盒\",\"seed\":424242,\"planets\":{"
                + "\"3\":{\"buildings\":[],\"pools\":[],\"storages\":[],"
                + "\"splittersV2\":[{\"e\":42,\"o\":[{\"p\":1,\"i\":1001,\"m\":5},{\"p\":3,\"i\":0,\"m\":7}]}],"
                + "\"sorters\":[{\"e\":77,\"m\":4}]}}}";

            var root = (Dictionary<string, object>)JsonLite.Parse(doc);
            Assert(SaveGuard.ValidateDocument(root, "沙盒", 424242) == null,
                "v0.16.1 trailer with the new nodes still validates");

            var planets = (Dictionary<string, object>)root["planets"];
            var planet = (Dictionary<string, object>)planets["3"];
            var splitters = (List<object>)planet["splittersV2"];
            Assert(splitters != null && splitters.Count == 1, "splittersV2 node parses as one entry");
            var splitter = (Dictionary<string, object>)splitters[0];
            Assert(Convert.ToInt32(splitter["e"]) == 42, "splitter entity id round trips");
            var ports = (List<object>)splitter["o"];
            Assert(ports != null && ports.Count == 2, "two port rules round trip");
            var port0 = (Dictionary<string, object>)ports[0];
            Assert(Convert.ToInt32(port0["p"]) == 1, "port index round trips");
            Assert(Convert.ToInt32(port0["i"]) == 1001, "rule item id round trips");
            Assert(Convert.ToInt32(port0["m"]) == 5, "rule minimum points round trip");
            var port1 = (Dictionary<string, object>)ports[1];
            Assert(Convert.ToInt32(port1["i"]) == 0, "any-item rule keeps item 0");
            Assert(Convert.ToInt32(port1["m"]) == 7, "any-item rule keeps the minimum");

            var sorters = (List<object>)planet["sorters"];
            Assert(sorters != null && sorters.Count == 1, "sorters node parses as one entry");
            var sorter = (Dictionary<string, object>)sorters[0];
            Assert(Convert.ToInt32(sorter["e"]) == 77, "sorter entity id round trips");
            Assert(Convert.ToInt32(sorter["m"]) == 4, "sorter threshold round trips");

            int packed = QualityMath.PackSplitterRule(
                Convert.ToInt32(port0["i"]), Convert.ToInt32(port0["m"]));
            Assert(QualityMath.SplitterRuleMatches(packed, 1001, 5), "parsed rule matches at the threshold");
            Assert(!QualityMath.SplitterRuleMatches(packed, 1001, 4), "parsed rule rejects below the threshold");
        }

        private static void CheckQualityFilterLegacyDoc()
        {
            string doc = "{\"version\":1,\"gameName\":\"沙盒\",\"seed\":424242,\"planets\":{"
                + "\"3\":{\"buildings\":[],\"pools\":[],\"splitters\":[{\"e\":5},{\"e\":9}]}}}";
            var root = (Dictionary<string, object>)JsonLite.Parse(doc);
            Assert(SaveGuard.ValidateDocument(root, "沙盒", 424242) == null,
                "pre-v0.16.1 trailer still validates");
            var planets = (Dictionary<string, object>)root["planets"];
            var planet = (Dictionary<string, object>)planets["3"];
            var legacy = (List<object>)planet["splitters"];
            Assert(legacy != null && legacy.Count == 2, "legacy boolean splitter node is readable");
            Assert(!planet.ContainsKey("splittersV2"), "no per-port rules in the legacy document");
            Assert(!planet.ContainsKey("sorters"), "no sorter thresholds in the legacy document");
        }

        private static void CheckSplitterRouteVanillaOrder()
        {
            // Physical ports A..D = belts 11..14; outputs are wired in a different order than the
            // port order, which is exactly why the fallback must follow output0..3 and not beltA..D.
            int[] portBelts = { 11, 12, 13, 14 };
            int[] outputBelts = { 13, 11, 14, 0 };
            int[] rules = { 0, 0, 0, 0 };
            int[] candidates = new int[4];
            int count = QualityMath.PlanSplitterRoute(1001, 5, rules, portBelts, outputBelts, candidates);
            Assert(count == 3, "three wired outputs produce three candidates");
            Assert(candidates[0] == 2, "vanilla order starts with output0 -> port C");
            Assert(candidates[1] == 0, "then output1 -> port A");
            Assert(candidates[2] == 3, "then output2 -> port D");

            Assert(QualityMath.PlanSplitterRoute(1001, 5, null, portBelts, outputBelts, candidates) == 0,
                "null rules mean no route (vanilla path)");
            Assert(QualityMath.PlanSplitterRoute(1001, 5, rules, portBelts, outputBelts, null) == 0,
                "null candidate buffer means no route");
        }

        private static void CheckSplitterRouteStrictestFirst()
        {
            int[] portBelts = { 11, 12, 13, 14 };
            int[] outputBelts = { 11, 12, 13, 14 };
            int[] rules = new int[4];
            rules[1] = QualityMath.PackSplitterRule(1001, 5);
            rules[3] = QualityMath.PackSplitterRule(1001, 2);
            int[] candidates = new int[4];

            int count = QualityMath.PlanSplitterRoute(1001, 9, rules, portBelts, outputBelts, candidates);
            Assert(count == 4, "both rule ports plus the two rule-less ports are candidates");
            Assert(candidates[0] == 1, "the strictest minimum (5) is tried first");
            Assert(candidates[1] == 3, "then the looser minimum (2)");
            Assert(candidates[2] == 0 && candidates[3] == 2, "rule-less ports follow in vanilla order");

            // Equal minima keep the ascending port order, which keeps the routing deterministic.
            rules[1] = QualityMath.PackSplitterRule(1001, 3);
            rules[3] = QualityMath.PackSplitterRule(1001, 3);
            count = QualityMath.PlanSplitterRoute(1001, 3, rules, portBelts, outputBelts, candidates);
            Assert(count == 4, "equal minima both qualify and the rule-less ports stay available");
            Assert(candidates[0] == 1 && candidates[1] == 3, "equal minima keep the port order");
        }

        private static void CheckSplitterRouteRuleOnlyFallback()
        {
            int[] portBelts = { 11, 12, 13, 14 };
            int[] outputBelts = { 13, 11, 12, 0 };
            int[] rules = new int[4];
            // Port D (13, output0) wants item 1001 at quality >= 8.
            rules[2] = QualityMath.PackSplitterRule(1001, 8);
            int[] candidates = new int[4];

            int count = QualityMath.PlanSplitterRoute(1001, 9, rules, portBelts, outputBelts, candidates);
            Assert(count == 3, "matching rule port plus two rule-less outputs");
            Assert(candidates[0] == 2, "the rule port is tried first");
            Assert(candidates[0] != candidates[1], "the same port is never offered twice");

            count = QualityMath.PlanSplitterRoute(1001, 7, rules, portBelts, outputBelts, candidates);
            Assert(count == 2, "below the minimum the rule port leaves the candidate list");
            Assert(candidates[0] == 0, "output0 (port A) becomes the vanilla fallback");
            Assert(candidates[1] == 1, "then output1 (port B)");

            count = QualityMath.PlanSplitterRoute(2002, 9, rules, portBelts, outputBelts, candidates);
            Assert(count == 2, "another item falls back to the rule-less outputs");

            // A rule-less port that is not wired never appears.
            outputBelts[3] = 0;
            rules[2] = 0;
            count = QualityMath.PlanSplitterRoute(2002, 9, rules, portBelts, outputBelts, candidates);
            Assert(count == 3, "unwired output3 never becomes a candidate");
        }

        private static void CheckSplitterRouteRulePortNotOutput()
        {
            int[] portBelts = { 11, 12, 13, 14 };
            // Port B (12) is an input here, so a rule on it must be ignored completely.
            int[] outputBelts = { 11, 13, 14, 0 };
            int[] rules = new int[4];
            rules[1] = QualityMath.PackSplitterRule(1001, 1);
            int[] candidates = new int[4];

            int count = QualityMath.PlanSplitterRoute(1001, 9, rules, portBelts, outputBelts, candidates);
            Assert(count == 3, "only the three outputs are candidates");
            for (int i = 0; i < count; i++)
            {
                Assert(candidates[i] != 1, "the rule on the input port is never routed to");
            }

            rules[1] = 0;
            count = QualityMath.PlanSplitterRoute(1001, 9, rules, portBelts, outputBelts, candidates);
            Assert(count == 3, "without rules the same three outputs are used");
        }

        private static void CheckSplitterRouteSaturated()
        {
            int[] portBelts = { 11, 12, 13, 14 };
            int[] outputBelts = { 11, 12, 13, 14 };
            int[] rules = new int[4];
            for (int port = 0; port < 4; port++)
            {
                rules[port] = QualityMath.PackSplitterRule(1000 + port, 1);
            }

            int[] candidates = new int[4];
            // Every output carries a rule, so there is no vanilla fallback at all.
            int count = QualityMath.PlanSplitterRoute(5555, 10, rules, portBelts, outputBelts, candidates);
            Assert(count == 0, "no rule matches and no fallback port exists");

            count = QualityMath.PlanSplitterRoute(1002, 10, rules, portBelts, outputBelts, candidates);
            Assert(count == 1 && candidates[0] == 2, "only the matching ruled port is offered");

            count = QualityMath.PlanSplitterRoute(1002, 0, rules, portBelts, outputBelts, candidates);
            Assert(count == 0, "the threshold is still enforced when every port is ruled");
        }

        private static void CheckSorterPickNeeds()
        {
            int[] items = { 1001, 1002, 1003, 1002 };
            int[] incs = { 0, 0, 0, 0 };
            int[] needs = { 1002 };

            Assert(QualityMath.PlanSorterPick(items, incs, 4, 0, 0, needs) == 1,
                "the first cargo whose item is in the needs list wins");
            Assert(QualityMath.PlanSorterPick(items, incs, 4, 1002, 0, needs) == 1,
                "item filter and needs list combine");
            Assert(QualityMath.PlanSorterPick(items, incs, 4, 1002, 0, null) == 1,
                "without needs the item filter alone still picks it");
            Assert(QualityMath.PlanSorterPick(items, incs, 2, 1002, 0, null) == 1,
                "the scan is limited to the requested window length");

            int[] hot = { 1002, 1002 };
            int[] hotInc = { 2, 9 };
            Assert(QualityMath.PlanSorterPick(hot, hotInc, 2, 1002, 5, null) == 1,
                "the low-quality cargo is skipped even when the item matches");
            Assert(QualityMath.PlanSorterPick(hot, hotInc, 2, 1002, 10, null) == -1,
                "nothing qualifies at the maximum threshold");
            Assert(QualityMath.PlanSorterPick(null, hotInc, 2, 1002, 0, null) == -1,
                "null item array is degenerate");
            Assert(QualityMath.PlanSorterPick(hot, hotInc, 0, 1002, 0, null) == -1,
                "empty window is degenerate");
        }

        private static void CheckQualityMinExtremes()
        {
            Assert(QualityMath.NextQualityMin(0) == 1, "cycle 0 -> 1");
            Assert(QualityMath.NextQualityMin(9) == 10, "cycle 9 -> 10");
            Assert(QualityMath.NextQualityMin(10) == 0, "cycle 10 wraps to 0");
            Assert(QualityMath.NextQualityMin(-3) == 1, "negative cycles from 0");
            Assert(QualityMath.PrevQualityMin(0) == 10, "cycle 0 wraps down to 10");
            Assert(QualityMath.PrevQualityMin(1) == 0, "cycle 1 -> 0");
            Assert(QualityMath.PrevQualityMin(10) == 9, "cycle 10 -> 9");
            Assert(QualityMath.PrevQualityMin(-3) == 10, "negative clamps to 0 then wraps down to 10");

            Assert(QualityMath.ValidateSorterMin(0), "threshold 0 is valid");
            Assert(QualityMath.ValidateSorterMin(10), "threshold 10 is valid");
            Assert(!QualityMath.ValidateSorterMin(11), "threshold 11 is invalid");
            Assert(!QualityMath.ValidateSorterMin(-1), "threshold -1 is invalid");
        }
    }
}
