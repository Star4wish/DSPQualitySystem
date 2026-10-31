using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Configuration;

namespace DSPQualitySystem
{
    /// <summary>
    /// v0.15.0: fixed-value stand-in for a ConfigEntry that is no longer exposed to players. Keeping
    /// the same <c>.Value</c> shape means every existing gate (Enabled.Value, GiveDismantleInc.Value,
    /// ...) keeps compiling unchanged, while the value itself is an internal constant near the top of
    /// <see cref="QualityConfig"/>. A constant is kept for every removed key so the "switch it off"
    /// capability can be re-opened later without touching the call sites.
    /// </summary>
    public sealed class QualityFixedBool
    {
        private readonly bool _value;

        public QualityFixedBool(bool value)
        {
            _value = value;
        }

        public bool Value
        {
            get { return _value; }
        }
    }

    /// <summary>v0.15.0: fixed int stand-in for a removed ConfigEntry (see QualityFixedBool).</summary>
    public sealed class QualityFixedInt
    {
        private readonly int _value;

        public QualityFixedInt(int value)
        {
            _value = value;
        }

        public int Value
        {
            get { return _value; }
        }
    }

    /// <summary>v0.15.0: fixed float stand-in for a removed ConfigEntry (see QualityFixedBool).</summary>
    public sealed class QualityFixedFloat
    {
        private readonly float _value;

        public QualityFixedFloat(float value)
        {
            _value = value;
        }

        public float Value
        {
            get { return _value; }
        }
    }

    /// <summary>v0.15.0: fixed string stand-in for a removed ConfigEntry (see QualityFixedBool).</summary>
    public sealed class QualityFixedString
    {
        private readonly string _value;

        public QualityFixedString(string value)
        {
            _value = value;
        }

        public string Value
        {
            get { return _value; }
        }
    }

    /// <summary>
    /// v0.15.0: developer mode switch. The debug probes and debug hotkeys are no longer a player
    /// config key; they are enabled only when the environment variable DSPQS_DEV equals "1".
    /// </summary>
    public sealed class QualityDevSwitch
    {
        public const string EnvVar = "DSPQS_DEV";

        public bool Value
        {
            get
            {
                try
                {
                    return string.Equals(Environment.GetEnvironmentVariable(EnvVar), "1",
                        StringComparison.Ordinal);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }
    }

    /// <summary>
    /// Config entries. Section: [quality].
    ///
    /// v0.15.0: the player-facing config was cut down to 16 keys (isolation, the seven quality-bonus
    /// switches and the visualisation block). Every other balance value moved to the internal
    /// constants in this file, with exactly the default the old cfg key had, so the running mod
    /// behaves the same. The debug probes moved to the DSPQS_DEV environment variable.
    /// </summary>
    public static class QualityConfig
    {
        public const string Section = "quality";

        // ---------------------------------------------------------------- internal constants
        // v0.15.0: former cfg keys, frozen at their shipped defaults. Edit here (not in the cfg file)
        // to rebalance. The *Value constants also document the "switch it off" capability.
        public const bool EnabledValue = true;
        public const int IncPerPointValue = 100;
        public const int PowerPerPointValue = 250;
        /// <summary>v0.18.0 (issue 2): kept for the legacy linear roll path / offline A-B tests; the live
        /// building upgrade roll now uses the shared per-point curve (QualityMath.UpgradeCurveChance).</summary>
        public const float BuildingChancePerPointValue = 1.5f;
        /// <summary>v0.18.0 (issue 3): a miner now adds one guaranteed product point per two building
        /// levels (2 -> +1, 4 -> +2, 10 -> +5). Implemented by raising the per-roll chance to 100% so the
        /// existing multi-roll loop in QualityRules.RollMinerQuality hits every time; lower this value to
        /// restore probabilistic miner output.</summary>
        public const float MinerChancePerPointValue = 100.0f;
        public const int MaxUpgradeChanceValue = 60;
        public const int MaxQualityValue = 10;
        public const bool GiveDismantleIncValue = true;
        public const int DebugInjectPointsValue = 4;
        public const string SpeedBonusModeValue = "curve";
        public const float SpeedPerPointValue = 2.0f;
        public const float MaxSpeedBonusValue = 50f;
        public const float ChargePerPointValue = 2.0f;
        public const float MaxChargeBonusValue = 50f;
        public const int ExtraUpgradeAboveValue = 6;
        public const int DoubleUpgradeAtValue = 10;
        public const bool PreferHighQualityConsumeValue = true;
        public const string TenPointHaloColorValue = "FFD24A";
        public const float TenPointHaloLiftValue = 1.2f;
        public const int TenPointHaloMaxValue = 64;
        public const bool TenPointHaloValue = true;
        /// <summary>
        /// v0.19.2: retired on purpose. The extra-grids-per-quality storage rule is gone - it was the
        /// cause of a vanilla UIStorageGrid._OnUpdate IndexOutOfRange while a chest with quality items
        /// was open, and it duplicated nothing the player asked for. The constant stays (false) so old
        /// configs and the MOD trailer reader keep compiling; nothing expands a container any more.
        /// </summary>
        public const bool StorageQualityGridsValue = false;
        public const float StorageQualityGridsPerPointValue = 0.5f;
        public const int StorageQualityGridsMaxValue = 20;
        /// <summary>v0.16.1 (C-2): per-output-port splitter quality filter (was splitterQualityPriority).</summary>
        public const bool SplitterQualityFilterValue = true;
        /// <summary>v0.16.1 (C-3): sorter (inserter) quality threshold filter.</summary>
        public const bool SorterQualityFilterValue = true;
        /// <summary>v0.18.0 (issue 1b): power node buildings (tesla tower / wireless power tower /
        /// substation) scale their connect distance and cover radius with the shared per-point curve,
        /// exactly like the power generation bonus. Internal constant: the published cfg keeps its 16 keys.</summary>
        public const bool PowerRangeBonusValue = true;

        /// <summary>True when a building's quality points are refunded on dismantle (was giveDismantleInc).</summary>
        public static readonly QualityFixedBool GiveDismantleInc = new QualityFixedBool(GiveDismantleIncValue);

        /// <summary>True when building/paste consumption prefers the highest quality item (was preferHighQualityConsume).</summary>
        public static readonly QualityFixedBool PreferHighQualityConsume =
            new QualityFixedBool(PreferHighQualityConsumeValue);

        /// <summary>True when a max-quality building is marked with the golden ring (was tenPointHalo).</summary>
        public static readonly QualityFixedBool TenPointHalo = new QualityFixedBool(TenPointHaloValue);

        /// <summary>Fixed marker colour (was tenPointHaloColor).</summary>
        public static readonly QualityFixedString TenPointHaloColor = new QualityFixedString(TenPointHaloColorValue);

        /// <summary>Fixed ring lift in metres (was tenPointHaloLift).</summary>
        public static readonly QualityFixedFloat TenPointHaloLift = new QualityFixedFloat(TenPointHaloLiftValue);

        /// <summary>Fixed per-frame ring budget (was tenPointHaloMax).</summary>
        public static readonly QualityFixedInt TenPointHaloMax = new QualityFixedInt(TenPointHaloMaxValue);

        /// <summary>True when containers may gain extra grids from quality points (was storageQualityGrids).</summary>
        public static readonly QualityFixedBool StorageQualityGrids = new QualityFixedBool(StorageQualityGridsValue);

        /// <summary>Fixed extra grids per point (was storageQualityGridsPerPoint).</summary>
        public static readonly QualityFixedFloat StorageQualityGridsPerPoint =
            new QualityFixedFloat(StorageQualityGridsPerPointValue);

        /// <summary>Fixed extra-grid cap (was storageQualityGridsMax).</summary>
        public static readonly QualityFixedInt StorageQualityGridsMax = new QualityFixedInt(StorageQualityGridsMaxValue);

        /// <summary>True when the per-output-port splitter quality filter may be registered.</summary>
        public static readonly QualityFixedBool SplitterQualityFilter =
            new QualityFixedBool(SplitterQualityFilterValue);

        /// <summary>True when the sorter (inserter) quality threshold filter may be registered.</summary>
        public static readonly QualityFixedBool SorterQualityFilter =
            new QualityFixedBool(SorterQualityFilterValue);

        /// <summary>True when power nodes scale their connect distance / cover radius with quality.</summary>
        public static readonly QualityFixedBool PowerRangeBonus =
            new QualityFixedBool(PowerRangeBonusValue);

        /// <summary>Master switch (was the enabled cfg key). Always true since v0.15.0.</summary>
        public static readonly QualityFixedBool Enabled = new QualityFixedBool(EnabledValue);
        public static readonly QualityFixedInt IncPerPoint = new QualityFixedInt(IncPerPointValue);
        public static readonly QualityFixedInt PowerPerPoint = new QualityFixedInt(PowerPerPointValue);
        public static readonly QualityFixedFloat BuildingChancePerPoint =
            new QualityFixedFloat(BuildingChancePerPointValue);
        public static readonly QualityFixedFloat MinerChancePerPoint =
            new QualityFixedFloat(MinerChancePerPointValue);
        public static readonly QualityFixedInt MaxUpgradeChance = new QualityFixedInt(MaxUpgradeChanceValue);
        public static readonly QualityFixedInt MaxQuality = new QualityFixedInt(MaxQualityValue);
        public static readonly QualityFixedInt DebugInjectPoints = new QualityFixedInt(DebugInjectPointsValue);
        public static readonly QualityFixedString SpeedBonusMode = new QualityFixedString(SpeedBonusModeValue);
        public static readonly QualityFixedFloat SpeedPerPoint = new QualityFixedFloat(SpeedPerPointValue);
        public static readonly QualityFixedFloat MaxSpeedBonus = new QualityFixedFloat(MaxSpeedBonusValue);
        public static readonly QualityFixedFloat ChargePerPoint = new QualityFixedFloat(ChargePerPointValue);
        public static readonly QualityFixedFloat MaxChargeBonus = new QualityFixedFloat(MaxChargeBonusValue);
        public static readonly QualityFixedInt ExtraUpgradeAbove = new QualityFixedInt(ExtraUpgradeAboveValue);
        public static readonly QualityFixedInt DoubleUpgradeAt = new QualityFixedInt(DoubleUpgradeAtValue);

        /// <summary>
        /// v0.15.0: former debugProbes cfg key. True only when the DSPQS_DEV environment variable is
        /// "1"; it gates the debug hotkeys (F5..F12) and the per-feature debug log lines.
        /// </summary>
        public static readonly QualityDevSwitch DebugProbes = new QualityDevSwitch();

        // ---------------------------------------------------------------- player-facing entries
        // Order matches the published config layout: isolation, bonus switches, visualisation.
        public static ConfigEntry<bool> IsolateBuildingStacks;
        public static ConfigEntry<bool> IsolateStorageContainers;

        public static ConfigEntry<bool> SpeedBonus;
        public static ConfigEntry<bool> ChargeBonus;
        // v0.12.0 (task-16) "A class" five: one independent switch per facility bonus.
        public static ConfigEntry<bool> LabSpeedBonus;
        public static ConfigEntry<bool> TurretFireRateBonus;
        public static ConfigEntry<bool> PowerGenBonus;
        public static ConfigEntry<bool> LauncherSpeedBonus;
        public static ConfigEntry<bool> SprayerIncBonus;

        // v0.14.0 (P6): per-item quality badges on item icons. Pure display: nothing is saved.
        public static ConfigEntry<bool> ItemQualityBadges;
        public static ConfigEntry<string> ItemQualityBadgeStyle;
        public static ConfigEntry<int> ItemQualityBadgeMinPoints;

        // v0.11.9 (task-19) / v0.12.2: max-quality visual marker.
        public static ConfigEntry<string> TenPointMarkerStyle;
        public static ConfigEntry<float> TenPointHaloSize;
        public static ConfigEntry<bool> TenPointHaloPulse;
        public static ConfigEntry<float> TenPointHaloSpin;

        public static void Init(ConfigFile cfg)
        {
            // v0.15.0 hotfix: the Chinese descriptions live in QualityConfigText so the live entries and
            // the file text builder (which rewrites the cfg when it still contains legacy keys) can never
            // drift apart. Defaults stay literal here and match QualityConfigText.PlayerEntries[*].DefaultText.
            // ---- isolation ----
            IsolateBuildingStacks = cfg.Bind(Section, "isolateBuildingStacks", true,
                QualityConfigText.DescriptionOf("isolateBuildingStacks"));
            IsolateStorageContainers = cfg.Bind(Section, "isolateStorageContainers", true,
                QualityConfigText.DescriptionOf("isolateStorageContainers"));

            // ---- quality bonus switches ----
            SpeedBonus = cfg.Bind(Section, "speedBonus", true,
                QualityConfigText.DescriptionOf("speedBonus"));
            ChargeBonus = cfg.Bind(Section, "chargeBonus", true,
                QualityConfigText.DescriptionOf("chargeBonus"));
            LabSpeedBonus = cfg.Bind(Section, "labSpeedBonus", true,
                QualityConfigText.DescriptionOf("labSpeedBonus"));
            TurretFireRateBonus = cfg.Bind(Section, "turretFireRateBonus", true,
                QualityConfigText.DescriptionOf("turretFireRateBonus"));
            PowerGenBonus = cfg.Bind(Section, "powerGenBonus", true,
                QualityConfigText.DescriptionOf("powerGenBonus"));
            LauncherSpeedBonus = cfg.Bind(Section, "launcherSpeedBonus", true,
                QualityConfigText.DescriptionOf("launcherSpeedBonus"));
            SprayerIncBonus = cfg.Bind(Section, "sprayerIncBonus", true,
                QualityConfigText.DescriptionOf("sprayerIncBonus"));

            // ---- visualisation ----
            ItemQualityBadges = cfg.Bind(Section, "itemQualityBadges", true,
                QualityConfigText.DescriptionOf("itemQualityBadges"));
            ItemQualityBadgeStyle = cfg.Bind(Section, "itemQualityBadgeStyle", "number",
                QualityConfigText.DescriptionOf("itemQualityBadgeStyle"));
            ItemQualityBadgeMinPoints = cfg.Bind(Section, "itemQualityBadgeMinPoints", 1,
                QualityConfigText.DescriptionOf("itemQualityBadgeMinPoints"));
            TenPointMarkerStyle = cfg.Bind(Section, "tenPointMarkerStyle", "auto",
                QualityConfigText.DescriptionOf("tenPointMarkerStyle"));
            TenPointHaloSize = cfg.Bind(Section, "tenPointHaloSize", 0.3f,
                QualityConfigText.DescriptionOf("tenPointHaloSize"));
            TenPointHaloPulse = cfg.Bind(Section, "tenPointHaloPulse", true,
                QualityConfigText.DescriptionOf("tenPointHaloPulse"));
            TenPointHaloSpin = cfg.Bind(Section, "tenPointHaloSpin", 1f,
                QualityConfigText.DescriptionOf("tenPointHaloSpin"));

            MigrateCfgLayout(cfg);
        }

        /// <summary>
        /// v0.15.0 hotfix: rebuild the player config file so it contains exactly the 16 exposed keys.
        ///
        /// BepInEx keeps entries that a plugin no longer binds in <c>orphanedEntries</c> and writes them
        /// back on every save, so a player upgrading from v0.14.0 (47 keys) previously ended up with a
        /// 63-key file, and <c>ConfigFile.Save()</c> could never shrink it. Instead:
        ///   1. parse every <c>key = value</c> line of the file,
        ///   2. self-heal when that key set differs from the 16 player keys (missing / extra / legacy),
        ///   3. copy the player's current values into the live entries (typed parse, default on failure),
        ///   4. write the whole file ourselves from <see cref="QualityConfigText.BuildCfgText"/>,
        ///   5. drop BepInEx's in-memory orphan list so a later save cannot resurrect the legacy keys.
        /// Values are never changed, only the file structure/comments. Runs once: an already clean file
        /// returns immediately, so the mtime never jitters on later starts.
        /// </summary>
        private static void MigrateCfgLayout(ConfigFile cfg)
        {
            try
            {
                if (cfg == null)
                {
                    return;
                }

                string path = cfg.ConfigFilePath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    // Fresh install: BepInEx has just created the file from the current 16 Bind calls.
                    return;
                }

                string text = File.ReadAllText(path, Encoding.UTF8);
                Dictionary<string, string> parsed = QualityConfigText.ParseKeyValues(text);
                if (QualityConfigText.IsPlayerKeySet(parsed.Keys))
                {
                    return;
                }

                int dropped = QualityConfigText.CountLegacyKeys(parsed.Keys);

                // Step 3: keep the player's values (BepInEx already loaded them for the 16 bound keys, but
                // parse ourselves so a duplicated/merged file cannot leak a stale value). Setting the
                // entries must not trigger BepInEx's own Save() - that would write the orphans back.
                bool saveOnSet = cfg.SaveOnConfigSet;
                cfg.SaveOnConfigSet = false;
                try
                {
                    for (int i = 0; i < QualityConfigText.PlayerEntries.Length; i++)
                    {
                        QualityConfigText.CfgEntry def = QualityConfigText.PlayerEntries[i];
                        ConfigEntryBase entry = FindEntry(def.Key);
                        string raw = null;
                        if (entry != null && parsed.TryGetValue(def.Key, out raw))
                        {
                            ApplyParsedValue(entry, raw);
                        }
                    }
                }
                finally
                {
                    cfg.SaveOnConfigSet = saveOnSet;
                }

                // Step 4: build and write the cleaned file (UTF-8 without BOM).
                List<KeyValuePair<string, string>> values = new List<KeyValuePair<string, string>>();
                for (int i = 0; i < QualityConfigText.PlayerEntries.Length; i++)
                {
                    QualityConfigText.CfgEntry def = QualityConfigText.PlayerEntries[i];
                    ConfigEntryBase entry = FindEntry(def.Key);
                    values.Add(new KeyValuePair<string, string>(
                        def.Key,
                        entry == null ? def.DefaultText : FormatEntryValue(entry)));
                }

                File.WriteAllText(path, QualityConfigText.BuildCfgText(Plugin.VERSION, values),
                    new UTF8Encoding(false));

                // Step 5: forget the legacy entries inside BepInEx as well.
                ClearOrphanedEntries(cfg);

                Plugin.Log.LogInfo("[QSystem][config] rewrote cfg with cleaned Chinese layout ("
                    + QualityConfigText.PlayerEntries.Length + " keys, dropped " + dropped + " legacy keys)");
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("config.chinese", e);
            }
        }

        private static ConfigEntryBase FindEntry(string key)
        {
            switch (key)
            {
                case "isolateBuildingStacks": return IsolateBuildingStacks;
                case "isolateStorageContainers": return IsolateStorageContainers;
                case "speedBonus": return SpeedBonus;
                case "chargeBonus": return ChargeBonus;
                case "labSpeedBonus": return LabSpeedBonus;
                case "turretFireRateBonus": return TurretFireRateBonus;
                case "powerGenBonus": return PowerGenBonus;
                case "launcherSpeedBonus": return LauncherSpeedBonus;
                case "sprayerIncBonus": return SprayerIncBonus;
                case "itemQualityBadges": return ItemQualityBadges;
                case "itemQualityBadgeStyle": return ItemQualityBadgeStyle;
                case "itemQualityBadgeMinPoints": return ItemQualityBadgeMinPoints;
                case "tenPointMarkerStyle": return TenPointMarkerStyle;
                case "tenPointHaloSize": return TenPointHaloSize;
                case "tenPointHaloPulse": return TenPointHaloPulse;
                case "tenPointHaloSpin": return TenPointHaloSpin;
                default: return null;
            }
        }

        private static void ApplyParsedValue(ConfigEntryBase entry, string raw)
        {
            object current = entry.BoxedValue;
            if (current is bool)
            {
                bool v;
                if (bool.TryParse(raw, out v) && v != (bool)current)
                {
                    entry.BoxedValue = v;
                }

                return;
            }

            if (current is int)
            {
                int v;
                if (int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out v) && v != (int)current)
                {
                    entry.BoxedValue = v;
                }

                return;
            }

            if (current is float)
            {
                float v;
                if (float.TryParse(raw, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out v)
                    && !float.IsNaN(v) && !float.IsInfinity(v) && v != (float)current)
                {
                    entry.BoxedValue = v;
                }

                return;
            }

            if (current is string)
            {
                string v = raw == null ? string.Empty : raw.Trim();
                if (v.Length > 0 && !string.Equals(v, (string)current, StringComparison.Ordinal))
                {
                    entry.BoxedValue = v;
                }
            }
        }

        private static string FormatEntryValue(ConfigEntryBase entry)
        {
            object v = entry.BoxedValue;
            if (v is bool)
            {
                return (bool)v ? "true" : "false";
            }

            if (v is int)
            {
                return ((int)v).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (v is float)
            {
                float f = (float)v;
                if (float.IsNaN(f) || float.IsInfinity(f))
                {
                    f = 0f;
                }

                return f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            }

            string s = v as string;
            return s ?? string.Empty;
        }

        private static void ClearOrphanedEntries(ConfigFile cfg)
        {
            try
            {
                const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
                System.Collections.IDictionary orphans = null;
                System.Reflection.PropertyInfo prop = typeof(ConfigFile).GetProperty("OrphanedEntries", Flags);
                if (prop != null)
                {
                    orphans = prop.GetValue(cfg, null) as System.Collections.IDictionary;
                }

                if (orphans == null)
                {
                    System.Reflection.FieldInfo field = typeof(ConfigFile).GetField("orphanedEntries", Flags);
                    if (field != null)
                    {
                        orphans = field.GetValue(cfg) as System.Collections.IDictionary;
                    }
                }

                if (orphans != null)
                {
                    orphans.Clear();
                }
            }
            catch (Exception)
            {
                // Best effort: if the internal field is renamed, a later BepInEx save may re-add the
                // legacy keys, but the file we just wrote is still correct for this session.
            }
        }

        /// <summary>v0.15.0: true when the DSPQS_DEV environment variable is "1".</summary>
        public static bool DevMode
        {
            get { return DebugProbes.Value; }
        }

        /// <summary>Clamp helper used by all chance rolls (T2+).</summary>
        public static int ClampChance(float chance)
        {
            int max = MaxUpgradeChance.Value;
            if (max < 0) max = 0;
            if (max > 100) max = 100;
            int v = (int)chance;
            if (v < 0) return 0;
            if (v > max) return max;
            return v;
        }

        /// <summary>P3 speed bonus per building quality point, clamped to 0..100 percent.</summary>
        public static float SpeedPerPointClamped
        {
            get
            {
                float v = SpeedPerPoint == null ? 2.0f : SpeedPerPoint.Value;
                if (v < 0f) return 0f;
                if (v > 100f) return 100f;
                return v;
            }
        }

        /// <summary>P3 total speed bonus cap, clamped to 0..500 percent.</summary>
        public static float MaxSpeedBonusClamped
        {
            get
            {
                float v = MaxSpeedBonus == null ? 50f : MaxSpeedBonus.Value;
                if (v < 0f) return 0f;
                if (v > 500f) return 500f;
                return v;
            }
        }

        /// <summary>
        /// v0.15.0: the linear/curve selector moved to an internal constant ("curve"), so the shared
        /// per-point curve is always used. Kept as a property so the P3 code path is unchanged.
        /// </summary>
        public static bool SpeedBonusIsCurve
        {
            get
            {
                return SpeedBonusMode == null
                    || !string.Equals(SpeedBonusMode.Value, "linear", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>P3 station charge bonus per building quality point, clamped to 0..100 percent.</summary>
        public static float ChargePerPointClamped
        {
            get
            {
                float v = ChargePerPoint == null ? 2.0f : ChargePerPoint.Value;
                if (v < 0f) return 0f;
                if (v > 100f) return 100f;
                return v;
            }
        }

        /// <summary>P3 total station charge bonus cap, clamped to 0..500 percent.</summary>
        public static float MaxChargeBonusClamped
        {
            get
            {
                float v = MaxChargeBonus == null ? 50f : MaxChargeBonus.Value;
                if (v < 0f) return 0f;
                if (v > 500f) return 500f;
                return v;
            }
        }

        /// <summary>v0.9.0 extra-upgrade threshold (buildingInc must EXCEED it), clamped to 0..10.</summary>
        public static int ExtraUpgradeAboveClamped
        {
            get
            {
                int v = ExtraUpgradeAbove == null ? 6 : ExtraUpgradeAbove.Value;
                if (v < 0) return 0;
                if (v > 10) return 10;
                return v;
            }
        }

        /// <summary>v0.9.0 deterministic +2 threshold, clamped to 0..10.</summary>
        public static int DoubleUpgradeAtClamped
        {
            get
            {
                int v = DoubleUpgradeAt == null ? 10 : DoubleUpgradeAt.Value;
                if (v < 0) return 0;
                if (v > 10) return 10;
                return v;
            }
        }

        /// <summary>v0.11.0 debug injection points (F10/F11), clamped to 0..MaxLevel (fixed 4 since v0.15.0).</summary>
        public static int DebugInjectPointsClamped
        {
            get
            {
                int v = DebugInjectPoints == null ? 4 : DebugInjectPoints.Value;
                if (v < 0) return 0;
                int max = MaxLevel;
                if (v > max) return max;
                return v;
            }
        }

        /// <summary>Halo lift above the building top in metres, clamped to 0..6 (fixed 1.2 since v0.15.0).</summary>
        public static float TenPointHaloLiftClamped
        {
            get
            {
                float v = TenPointHaloLift == null ? 1.2f : TenPointHaloLift.Value;
                if (v < 0f) return 0f;
                if (v > 6f) return 6f;
                return v;
            }
        }

        /// <summary>Halo size multiplier, clamped to 0.3..3.0 (player-facing since v0.15.0).</summary>
        public static float TenPointHaloSizeClamped
        {
            get
            {
                float v = TenPointHaloSize == null ? 0.3f : TenPointHaloSize.Value;
                if (v < 0.3f) return 0.3f;
                if (v > 3.0f) return 3.0f;
                return v;
            }
        }

        /// <summary>Halo target budget, clamped to 0..256 (fixed 64 since v0.15.0).</summary>
        public static int TenPointHaloMaxClamped
        {
            get
            {
                int v = TenPointHaloMax == null ? 64 : TenPointHaloMax.Value;
                if (v < 0) return 0;
                if (v > 256) return 256;
                return v;
            }
        }

        /// <summary>
        /// v0.15.0: marker style narrowed to auto / halo / none. "outline" and "both" are no longer
        /// supported and are normalised to auto (the golden ring is the only marker).
        /// </summary>
        public static string MarkerStyleName
        {
            get
            {
                string s = TenPointMarkerStyle == null ? null : TenPointMarkerStyle.Value;
                if (string.IsNullOrEmpty(s)) return "auto";
                s = s.Trim().ToLowerInvariant();
                if (s == "halo" || s == "none")
                {
                    return s;
                }

                return "auto";
            }
        }

        /// <summary>
        /// Resolved "draw the golden ring" switch (v0.15.0): the ring is drawn for auto/halo and
        /// hidden only for none. Kept for the halo tick gate.
        /// </summary>
        public static bool HaloArmed
        {
            get
            {
                bool haloOn;
                QualityMath.ResolveMarkerStyle(MarkerStyleName, out haloOn);
                return haloOn && TenPointHalo != null && TenPointHalo.Value;
            }
        }

        /// <summary>Marker colour string (fixed FFD24A since v0.15.0; the hex parser stays for tooling).</summary>
        public static string HaloColorHex
        {
            get
            {
                string s = TenPointHaloColor == null ? null : TenPointHaloColor.Value;
                return string.IsNullOrEmpty(s) ? QualityMath.HaloDefaultColorHex : s;
            }
        }

        /// <summary>Whether the ring breathes (alpha animation).</summary>
        public static bool HaloPulseEnabled
        {
            get { return TenPointHaloPulse == null || TenPointHaloPulse.Value; }
        }

        /// <summary>Ring spin speed multiplier, clamped to 0..5.</summary>
        public static float HaloSpinClamped
        {
            get
            {
                float v = TenPointHaloSpin == null ? 1f : TenPointHaloSpin.Value;
                if (float.IsNaN(v) || v < 0f) return 0f;
                if (v > 5f) return 5f;
                return v;
            }
        }

        /// <summary>
        /// v0.13.0 (C-1): true when the storage-grid rule is switched on. Both the master switch and
        /// the (now internal) storageQualityGrids constant gate it.
        /// </summary>
        public static bool StorageGridsEnabled
        {
            get
            {
                return Enabled != null && Enabled.Value
                    && StorageQualityGrids != null && StorageQualityGrids.Value;
            }
        }

        /// <summary>v0.13.0 (C-1): extra grids per quality point, clamped to 0..2 (fixed 0.5 since v0.15.0).</summary>
        public static float StorageGridsPerPointClamped
        {
            get
            {
                float v = StorageQualityGridsPerPoint == null ? 0.5f : StorageQualityGridsPerPoint.Value;
                if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f) return 0f;
                if (v > 2f) return 2f;
                return v;
            }
        }

        /// <summary>v0.13.0 (C-1): hard cap on extra grids per container, clamped to 0..200 (fixed 20).</summary>
        public static int StorageGridsMaxClamped
        {
            get
            {
                int v = StorageQualityGridsMax == null ? 20 : StorageQualityGridsMax.Value;
                if (v < 0) return 0;
                if (v > 200) return 200;
                return v;
            }
        }

        /// <summary>
        /// v0.16.1 (C-2): true when the per-output-port splitter quality filter may be registered.
        /// The master switch and the (now internal) splitterQualityFilter constant gate it.
        /// </summary>
        public static bool SplitterQualityFilterEnabled
        {
            get
            {
                return Enabled != null && Enabled.Value
                    && SplitterQualityFilter != null && SplitterQualityFilter.Value;
            }
        }

        /// <summary>
        /// v0.16.1 (C-3): true when the sorter (inserter) quality threshold filter may be registered.
        /// The master switch and the (now internal) sorterQualityFilter constant gate it.
        /// </summary>
        public static bool SorterQualityFilterEnabled
        {
            get
            {
                return Enabled != null && Enabled.Value
                    && SorterQualityFilter != null && SorterQualityFilter.Value;
            }
        }

        /// <summary>
        /// v0.14.0 (P6): true when the item-quality-badge patch may be registered. The master
        /// switch also gates it (Enabled is always true since v0.15.0).
        /// </summary>
        public static bool ItemQualityBadgesEnabled
        {
            get
            {
                return Enabled != null && Enabled.Value
                    && ItemQualityBadges != null && ItemQualityBadges.Value;
            }
        }

        /// <summary>v0.14.0 (P6): per-item point threshold, clamped to 1..Cargo.kIncLevelMax.</summary>
        public static int ItemQualityBadgeMinPointsClamped
        {
            get
            {
                int v = ItemQualityBadgeMinPoints == null ? 1 : ItemQualityBadgeMinPoints.Value;
                if (v < 1) return 1;
                if (v > Cargo.kIncLevelMax) return Cargo.kIncLevelMax;
                return v;
            }
        }

        /// <summary>v0.14.0 (P6): normalized badge style ('number' / 'dot' / 'both').</summary>
        public static string ItemQualityBadgeStyleNormalized
        {
            get
            {
                return QualityMath.BadgeStyleNormalized(
                    ItemQualityBadgeStyle == null ? null : ItemQualityBadgeStyle.Value);
            }
        }

        public static int MaxLevel
        {
            get
            {
                int m = MaxQuality.Value;
                if (m < 0) return 0;
                if (m > Cargo.kIncLevelMax) return Cargo.kIncLevelMax;
                return m;
            }
        }
    }
}
