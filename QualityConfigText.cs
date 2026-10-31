using System;
using System.Collections.Generic;
using System.Text;

namespace DSPQualitySystem
{
    /// <summary>
    /// v0.15.0 (hotfix): the player-facing config layout, kept free of any BepInEx / Unity types so the
    /// text builder can be exercised by the offline test project.
    ///
    /// The cfg file is the player's only view of the mod, so it has to stay at exactly the 16 keys that
    /// are still exposed. BepInEx itself never removes an entry that a plugin stops binding: it keeps it
    /// in <c>orphanedEntries</c> and writes it back on the next save. That is why v0.15.0 shipped a
    /// migration that used <c>ConfigFile.Save()</c> and still ended up with the 16 new keys plus all 47
    /// legacy keys in one file. This class builds the finished file text from scratch instead.
    /// </summary>
    public static class QualityConfigText
    {
        public const string PluginName = "DSP Quality System";
        public const string PluginGuid = "dsp.qualitysystem";
        public const string SectionName = "quality";

        /// <summary>One player-facing key: canonical name, TOML-ish type label, default text and Chinese description.</summary>
        public sealed class CfgEntry
        {
            public readonly string Key;
            public readonly string TypeName;
            public readonly string DefaultText;
            public readonly string Description;

            public CfgEntry(string key, string typeName, string defaultText, string description)
            {
                Key = key;
                TypeName = typeName;
                DefaultText = defaultText;
                Description = description;
            }
        }

        // Order matches the published layout: isolation, bonus switches, visualisation.
        public static readonly CfgEntry[] PlayerEntries =
        {
            new CfgEntry("isolateBuildingStacks", "Boolean", "true",
                "【DSP 品质系统 v0.15.0】本文件只开放 16 个玩家开关，其余平衡参数已内置；"
                + "调试探针需环境变量 DSPQS_DEV=1。"
                + "｜隔离-背包建筑分格：true = 玩家背包里的同类建筑按每件品质点数分格存放"
                + "（0 点与 10 点不会合并成一格）；false = 原版合并行为。非建筑物品始终按原版合并。"),
            new CfgEntry("isolateStorageContainers", "Boolean", "true",
                "隔离-储物容器分格：true = 在 isolateBuildingStacks=true 的前提下，储物仓、"
                + "战场分析基站等通用储物容器里的建筑物品也按每件品质点数分格存放；"
                + "false = 只隔离玩家背包。机甲燃料/弹药/炸弹/战斗机格、过滤容器、"
                + "物流塔内部、储液罐永远不隔离。代价：同一物品可能占更多格位。"),

            new CfgEntry("speedBonus", "Boolean", "true",
                "品质加成-制造与采集速度：true = 建筑品质点数提高制造台/采矿机的工作速度"
                + "（内置分段曲线，10 点 = 2.00×）；false = 原版速度。"),
            new CfgEntry("chargeBonus", "Boolean", "true",
                "品质加成-物流塔充电上限：true = 建筑品质点数提高物流塔每 tick 可充入的电量上限"
                + "（内置分段曲线，10 点 = 2.00×，总电量上限不变、单位时间取电上升）；"
                + "false = 原版充电速度。"),
            new CfgEntry("labSpeedBonus", "Boolean", "true",
                "品质加成-研究站研究速度：true = 建筑品质点数提高研究站的研究吞吐"
                + "（内置分段曲线，10 点 = 2.00×）；false = 原版研究速度。"),
            new CfgEntry("turretFireRateBonus", "Boolean", "true",
                "品质加成-防御塔射速：true = 建筑品质点数提高防御塔开火速度"
                + "（高斯/激光/加农/等离子/导弹/近程等离子，内置分段曲线，10 点 = 2.00×）；"
                + "false = 原版射速。伤害、射程、弹药消耗不变。"),
            new CfgEntry("powerGenBonus", "Boolean", "true",
                "品质加成-发电机功率：true = 建筑品质点数提高发电机每 tick 发电功率"
                + "（风力/光伏/地热/火力/射线接收站，内置分段曲线，10 点 = 2.00×）；"
                + "false = 原版功率。火力发电机按比例多烧燃料、射线接收站按比例多产。"),
            new CfgEntry("launcherSpeedBonus", "Boolean", "true",
                "品质加成-弹射器与发射井：true = 建筑品质点数提高电磁轨道弹射器和垂直发射井的"
                + "发射速度（内置分段曲线，10 点 = 2.00×）；false = 原版发射速度。"),
            new CfgEntry("sprayerIncBonus", "Boolean", "true",
                "品质加成-喷涂机额外品质点：true = 喷涂机按自身建筑品质点数，在喷到的物品上"
                + "额外 +1 品质点（同一曲线当概率：0 点 0%、5 点 15%、10 点 100%，已达 10 点上限不加）；"
                + "false = 原版喷涂结果。"),

            new CfgEntry("itemQualityBadges", "Boolean", "true",
                "可视化-物品品质角标：true = 在物品图标角上显示每件品质点数（0–10，按 总点数/堆叠数"
                + " 向下取整），覆盖玩家背包、打开的储物仓面板、手持物品提示；纯显示、不写存档；"
                + "false = 不注册补丁、不创建任何角标（原版等价）。"),
            new CfgEntry("itemQualityBadgeStyle", "String", "number",
                "可视化-角标样式：number = 只显示数字（1–10）；dot = 只显示一个按档位着色的圆点；"
                + "both = 圆点 + 数字。无法识别时按 number 处理。"),
            new CfgEntry("itemQualityBadgeMinPoints", "Int32", "1",
                "可视化-角标显示阈值：每件品质点数达到该值才显示角标（自动夹到 1–10）。"
                + "默认 1 = 有品质点就显示；设 10 = 只标出满品物品。"),
            new CfgEntry("tenPointMarkerStyle", "String", "auto",
                "可视化-满品建筑标记样式：auto = 画金色光环（默认）；halo = 只画金色光环；"
                + "none = 不画任何标记。屏幕描边功能已删除，旧值 outline/both 及无法识别的值"
                + "都会按 auto（画光环）处理。"),
            new CfgEntry("tenPointHaloSize", "Single", "0.3",
                "可视化-光环大小倍率：光环半径 = clamp(max(占地X,占地Z) × 0.42, 1.0, 3.2) × 本值，"
                + "使用时夹到 0.3–3.0。默认 0.3（比首版 1.0 小 70%，不会比建筑本身还大）；"
                + "想要更醒目可调到 1.0 左右。"),
            new CfgEntry("tenPointHaloPulse", "Boolean", "true",
                "可视化-光环呼吸：true = 光环做 alpha 呼吸（约 2 秒一轮，0.6–1.0）；"
                + "false = 恒定亮度 0.85，长时间看不刺眼。"),
            new CfgEntry("tenPointHaloSpin", "Single", "1",
                "可视化-光环自转速度：倍率制，1 = 基础约 20°/秒，0 = 不自转，2 = 两倍；"
                + "使用时夹到 0–5。")
        };

        /// <summary>Canonical key list, in published order.</summary>
        public static readonly string[] PlayerKeys = BuildKeyList();

        private static string[] BuildKeyList()
        {
            string[] keys = new string[PlayerEntries.Length];
            for (int i = 0; i < PlayerEntries.Length; i++)
            {
                keys[i] = PlayerEntries[i].Key;
            }

            return keys;
        }

        /// <summary>Description text for one player key (empty when unknown).</summary>
        public static string DescriptionOf(string key)
        {
            CfgEntry entry = Find(key);
            return entry == null ? string.Empty : entry.Description;
        }

        /// <summary>Entry for one player key (null when unknown).</summary>
        public static CfgEntry Find(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            for (int i = 0; i < PlayerEntries.Length; i++)
            {
                if (string.Equals(PlayerEntries[i].Key, key, StringComparison.Ordinal))
                {
                    return PlayerEntries[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Parse every <c>key = value</c> line of a cfg file. Comment lines (<c>## …</c>, <c># …</c>),
        /// section headers (<c>[quality]</c>) and blank lines are ignored. The first occurrence of a key
        /// wins so a duplicated block cannot change the value we keep. Keys keep the canonical case used
        /// in the file.
        /// </summary>
        public static Dictionary<string, string> ParseKeyValues(string text)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(text))
            {
                return result;
            }

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';' || line[0] == '[')
                {
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, eq).Trim();
                if (key.Length == 0 || !IsIdentifier(key))
                {
                    continue;
                }

                string value = line.Substring(eq + 1).Trim();
                if (!result.ContainsKey(key))
                {
                    result.Add(key, value);
                }
            }

            return result;
        }

        private static bool IsIdentifier(string key)
        {
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (i > 0 && c >= '0' && c <= '9')
                    || c == '_';
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// True when the parsed key set is exactly the 16 player keys (count, membership and names match;
        /// order is irrelevant). Used as the self-heal trigger: any legacy key or missing key rewrites the file.
        /// </summary>
        public static bool IsPlayerKeySet(IEnumerable<string> keys)
        {
            if (keys == null)
            {
                return false;
            }

            int count = 0;
            foreach (string key in keys)
            {
                count++;
                if (FindIgnoreCase(key) == null)
                {
                    return false;
                }
            }

            return count == PlayerEntries.Length;
        }

        private static CfgEntry FindIgnoreCase(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            for (int i = 0; i < PlayerEntries.Length; i++)
            {
                if (string.Equals(PlayerEntries[i].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return PlayerEntries[i];
                }
            }

            return null;
        }

        /// <summary>How many parsed keys are legacy (not part of the 16 player keys).</summary>
        public static int CountLegacyKeys(IEnumerable<string> keys)
        {
            int legacy = 0;
            if (keys == null)
            {
                return 0;
            }

            foreach (string key in keys)
            {
                if (FindIgnoreCase(key) == null)
                {
                    legacy++;
                }
            }

            return legacy;
        }

        /// <summary>
        /// Build the complete cfg file text: BepInEx header lines, one <c>[quality]</c> section and the 16
        /// keys in published order, each with its Chinese description, type label, default value and the
        /// current value. Values missing from <paramref name="values"/> fall back to the table default.
        /// </summary>
        public static string BuildCfgText(string version, IList<KeyValuePair<string, string>> values)
        {
            Dictionary<string, string> byKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (values != null)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    KeyValuePair<string, string> pair = values[i];
                    if (!string.IsNullOrEmpty(pair.Key))
                    {
                        byKey[pair.Key] = pair.Value ?? string.Empty;
                    }
                }
            }

            StringBuilder sb = new StringBuilder(4096);
            sb.Append("## Settings file was created by plugin ").Append(PluginName).Append(" v")
                .Append(string.IsNullOrEmpty(version) ? "0.0.0" : version).Append("\r\n");
            sb.Append("## Plugin GUID: ").Append(PluginGuid).Append("\r\n");
            sb.Append("\r\n");
            sb.Append('[').Append(SectionName).Append(']').Append("\r\n");
            sb.Append("\r\n");

            for (int i = 0; i < PlayerEntries.Length; i++)
            {
                CfgEntry entry = PlayerEntries[i];
                string value;
                if (!byKey.TryGetValue(entry.Key, out value) || value == null)
                {
                    value = entry.DefaultText;
                }

                sb.Append("## ").Append(entry.Description).Append("\r\n");
                sb.Append("# Setting type: ").Append(entry.TypeName).Append("\r\n");
                sb.Append("# Default value: ").Append(entry.DefaultText).Append("\r\n");
                sb.Append(entry.Key).Append(" = ").Append(value).Append("\r\n");
                sb.Append("\r\n");
            }

            return sb.ToString();
        }
    }
}
