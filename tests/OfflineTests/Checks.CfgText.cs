using System;
using System.Collections.Generic;
using System.Text;

namespace DSPQualitySystem.OfflineTests
{
    internal static partial class Program
    {
        /// <summary>
        /// v0.15.0 hotfix: the cfg file must end up as exactly the 16 player keys, with Chinese comments and
        /// the player's values preserved. Exercises QualityConfigText (pure: no BepInEx), which is what the
        /// plugin uses to rewrite an upgraded 63-key file on startup.
        /// </summary>
        private static void CheckCfgText()
        {
            // A v0.14.0-era file: English comments, 47 legacy keys, plus the 16 new keys BepInEx appended.
            // A few representative rows of each kind are enough to prove the key-set detection/cleanup.
            string legacy = string.Join("\r\n", new[]
            {
                "## Settings file was created by plugin DSP Quality System v0.14.0",
                "## Plugin GUID: dsp.qualitysystem",
                "",
                "[quality]",
                "",
                "## enabled: master switch (legacy key, no longer exposed)",
                "# Setting type: Boolean",
                "# Default value: true",
                "enabled = true",
                "",
                "## debugProbes: verbose logging (legacy key, moved to DSPQS_DEV)",
                "# Setting type: Boolean",
                "# Default value: false",
                "debugProbes = false",
                "",
                "## tenPointHaloColor: ring colour (legacy key, now internal constant)",
                "# Setting type: String",
                "# Default value: FFD24A",
                "tenPointHaloColor = FFD24A",
                "",
                "## 隔离-背包建筑分格",
                "# Setting type: Boolean",
                "# Default value: true",
                "isolateBuildingStacks = false",
                "",
                "## 可视化-角标样式",
                "# Setting type: String",
                "# Default value: number",
                "itemQualityBadgeStyle = dot",
                "",
                "## 可视化-光环自转速度",
                "# Setting type: Single",
                "# Default value: 1",
                "tenPointHaloSpin = 2",
                ""
            });

            // --- parse ---
            Dictionary<string, string> parsed = QualityConfigText.ParseKeyValues(legacy);
            Assert(parsed.ContainsKey("enabled"), "cfg parse: legacy key 'enabled' must be parsed");
            Assert(parsed.ContainsKey("debugProbes"), "cfg parse: legacy key 'debugProbes' must be parsed");
            Assert(parsed.ContainsKey("tenPointHaloColor"), "cfg parse: legacy key 'tenPointHaloColor' must be parsed");
            Assert(parsed.ContainsKey("isolateBuildingStacks"), "cfg parse: player key must be parsed");
            Assert(string.Equals(parsed["isolateBuildingStacks"], "false", StringComparison.Ordinal),
                "cfg parse: value text must be kept verbatim");
            Assert(!parsed.ContainsKey("[quality]"), "cfg parse: section header is not a key");
            Assert(!parsed.ContainsKey("Setting type"), "cfg parse: comment lines are not keys");
            Assert(parsed.Count == 6, "cfg parse: expected 6 real keys, got " + parsed.Count);

            // --- self-heal trigger ---
            Assert(!QualityConfigText.IsPlayerKeySet(parsed.Keys),
                "cfg gate: a legacy 47-key-style file must be rewritten");
            Assert(QualityConfigText.CountLegacyKeys(parsed.Keys) == 3,
                "cfg gate: 3 legacy keys expected, got " + QualityConfigText.CountLegacyKeys(parsed.Keys));
            Assert(QualityConfigText.IsPlayerKeySet(QualityConfigText.PlayerKeys),
                "cfg gate: the canonical 16 keys are already clean");
            Assert(QualityConfigText.PlayerKeys.Length == 16, "cfg gate: exactly 16 player keys expected");
            List<string> extra = new List<string>(QualityConfigText.PlayerKeys);
            extra.Add("enabled");
            Assert(!QualityConfigText.IsPlayerKeySet(extra), "cfg gate: one extra key means rewrite");
            List<string> missing = new List<string>(QualityConfigText.PlayerKeys);
            missing.RemoveAt(0);
            Assert(!QualityConfigText.IsPlayerKeySet(missing), "cfg gate: one missing key means rewrite");
            List<string> upper = new List<string>();
            foreach (string k in QualityConfigText.PlayerKeys)
            {
                upper.Add(k.ToUpperInvariant());
            }

            Assert(QualityConfigText.IsPlayerKeySet(upper), "cfg gate: key names compare case-insensitively");

            // --- rebuild with the player's values ---
            List<KeyValuePair<string, string>> values = new List<KeyValuePair<string, string>>();
            foreach (string key in QualityConfigText.PlayerKeys)
            {
                string v;
                if (parsed.TryGetValue(key, out v))
                {
                    values.Add(new KeyValuePair<string, string>(key, v));
                }
            }

            string clean = QualityConfigText.BuildCfgText("0.15.0", values);

            // header + single section
            Assert(clean.Contains("v0.15.0"), "cfg text: header must carry the current version");
            Assert(clean.Contains("## Plugin GUID: dsp.qualitysystem"), "cfg text: plugin guid header expected");
            Assert(CountOccurrences(clean, "[quality]") == 1, "cfg text: exactly one [quality] section");
            Assert(clean.Contains("\r\n"), "cfg text: CRLF line endings expected");
            Assert(clean.EndsWith("\r\n", StringComparison.Ordinal), "cfg text: file ends with a newline");

            // exactly the 16 key lines, no legacy key can sneak back in
            List<string> keyLines = new List<string>();
            string[] lines = clean.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == '[')
                {
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq > 0)
                {
                    keyLines.Add(line.Substring(0, eq).Trim());
                }
            }

            Assert(keyLines.Count == 16, "cfg text: expected 16 key lines, got " + keyLines.Count);
            Dictionary<string, string> rebuilt = QualityConfigText.ParseKeyValues(clean);
            Assert(QualityConfigText.IsPlayerKeySet(rebuilt.Keys), "cfg text: rebuilt key set must be the 16 player keys");
            Assert(!rebuilt.ContainsKey("enabled"), "cfg text: legacy 'enabled' must be dropped");
            Assert(!rebuilt.ContainsKey("debugProbes"), "cfg text: legacy 'debugProbes' must be dropped");
            Assert(!rebuilt.ContainsKey("tenPointHaloColor"), "cfg text: legacy 'tenPointHaloColor' must be dropped");

            // values preserved: changed bool / changed string / changed float
            Assert(string.Equals(rebuilt["isolateBuildingStacks"], "false", StringComparison.Ordinal),
                "cfg text: changed bool value must be preserved");
            Assert(string.Equals(rebuilt["itemQualityBadgeStyle"], "dot", StringComparison.Ordinal),
                "cfg text: changed string value must be preserved");
            Assert(string.Equals(rebuilt["tenPointHaloSpin"], "2", StringComparison.Ordinal),
                "cfg text: changed float value must be preserved");
            // untouched keys keep their defaults
            Assert(string.Equals(rebuilt["speedBonus"], "true", StringComparison.Ordinal),
                "cfg text: untouched bool keeps its default");
            Assert(string.Equals(rebuilt["itemQualityBadgeMinPoints"], "1", StringComparison.Ordinal),
                "cfg text: untouched int keeps its default");
            Assert(string.Equals(rebuilt["tenPointHaloSize"], "0.3", StringComparison.Ordinal),
                "cfg text: untouched float keeps its default");

            // Chinese comments are written for the player
            Assert(clean.Contains("隔离-背包建筑分格"), "cfg text: Chinese description for a Bool key expected");
            Assert(clean.Contains("可视化-光环自转速度"), "cfg text: Chinese description for a float key expected");
            Assert(clean.Contains("DSPQS_DEV=1"), "cfg text: the first key description must mention DSPQS_DEV");
            // v0.15.0 layout polish: one concise preamble, Chinese "｜" separator, no ASCII rule lines
            Assert(clean.Contains("｜隔离-背包建筑分格"), "cfg text: preamble must be joined to the first key with the ｜ separator");
            Assert(!clean.Contains("==="), "cfg text: no ASCII === separator may appear in the comments");
            Assert(!clean.Contains("---"), "cfg text: no ASCII --- separator may appear in the comments");
            Assert(CountOccurrences(clean, "本文件只开放 16 个玩家开关") == 1,
                "cfg text: the preamble must appear exactly once (not glued onto every key)");
            Assert(clean.Contains("# Setting type: Boolean"), "cfg text: BepInEx-style type line expected");
            Assert(clean.Contains("# Default value: true"), "cfg text: BepInEx-style default line expected");

            // building from no values at all must fall back to the table defaults
            string defaults = QualityConfigText.BuildCfgText("0.15.0", null);
            Dictionary<string, string> fromDefaults = QualityConfigText.ParseKeyValues(defaults);
            Assert(QualityConfigText.IsPlayerKeySet(fromDefaults.Keys), "cfg text: null values still yield the 16 keys");
            Assert(string.Equals(fromDefaults["itemQualityBadgeStyle"], "number", StringComparison.Ordinal),
                "cfg text: null values fall back to the table default");
            Assert(string.Equals(fromDefaults["tenPointHaloSpin"], "1", StringComparison.Ordinal),
                "cfg text: null values fall back to the float default");

            // duplicate key: the first row wins, so a merged file cannot resurrect a stale value
            Dictionary<string, string> dup = QualityConfigText.ParseKeyValues(
                "enabled = true\r\nenabled = false\r\n[quality]\r\n");
            Assert(string.Equals(dup["enabled"], "true", StringComparison.Ordinal),
                "cfg parse: first duplicate value wins");
        }

        private static int CountOccurrences(string text, string needle)
        {
            int count = 0;
            int at = 0;
            while (true)
            {
                int found = text.IndexOf(needle, at, StringComparison.Ordinal);
                if (found < 0)
                {
                    return count;
                }

                count++;
                at = found + needle.Length;
            }
        }
    }
}
