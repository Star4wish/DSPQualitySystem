using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace DSPQualitySystem
{
    /// <summary>
    /// M0 (T1) entry point: load config, rewrite the four Cargo tables, patch Harmony,
    /// then wait for LDB and print the startup self-check + table values.
    /// </summary>
    [BepInPlugin(GUID, "DSP Quality System", VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "dsp.qualitysystem";
        public const string VERSION = "0.19.2";

        internal static ManualLogSource Log;

        private bool _startupReported;
        private int _pollTicks;

        private void Awake()
        {
            Log = Logger;

            try
            {
                QualityConfig.Init(Config);
                Log.LogInfo("[QSystem] === DSP Quality System v" + VERSION + " ===");
                LogBuildInfo();
                LogConfig();

                // v0.9.0 rule announcement: one grep-able line so a live log proves which
                // upgrade rules this build runs (multi-roll miner + high-point extra upgrade).
                Log.LogInfo("[QSystem] rule: miner rolls = floor(points/2) (multi-point ore, cap = half);"
                    + " extra upgrade above=" + QualityConfig.ExtraUpgradeAboveClamped
                    + " guaranteed +2 at=" + QualityConfig.DoubleUpgradeAtClamped);
                // v0.15.0: the table rewrite is unconditional now (the old `enabled` key became an
                // internal constant, so there is no runtime toggle to listen to any more).
                Patches.P0_Tables.Apply("Awake");
            }
            catch (Exception e)
            {
                Log.LogError("[QSystem] Awake (config/tables) failed: " + e);
            }

            try
            {
                var harmony = new Harmony(GUID);
                harmony.PatchAll(typeof(Plugin).Assembly);
                int patched = 0;
                var names = new System.Text.StringBuilder();
                foreach (MethodBase method in harmony.GetPatchedMethods())
                {
                    patched++;
                    if (names.Length > 0)
                    {
                        names.Append(", ");
                    }
                    names.Append(method.DeclaringType == null ? "?" : method.DeclaringType.Name)
                        .Append('.').Append(method.Name);
                }
                Log.LogInfo("[QSystem] Harmony PatchAll OK, harmony=" + typeof(Harmony).Assembly.GetName().Version
                    + " patchedMethods=" + patched);
                Log.LogInfo("[QSystem] patched: " + names);

                // P3 stage 1: register the speed Prefixes manually so a binding failure on the
                // older runtime HarmonyX can be isolated instead of aborting the whole PatchAll.
                Patches.P3_Speed.Apply(harmony);
                Log.LogInfo("[QSystem][speed] " + Patches.P3_Speed.Describe());

                // P3 stage 2a (v0.11.0): charge-cap postfix on StationComponent.SetPCState (the old
                // power prefix was retired); same per-target isolation, shapes validated first.
                Patches.P3_StationCharge.Apply(harmony);
                Log.LogInfo("[QSystem][charge] " + Patches.P3_StationCharge.Describe());

                // P3 stage 2d: dismantled buildings refund their quality points as inc on the
                // returned item (manual registration keeps the count above unchanged).
                Patches.P3_Dismantle.Apply(harmony);
                Log.LogInfo("[QSystem][dismantle] " + Patches.P3_Dismantle.Describe());

                // P4: keep building items with different quality points in separate backpack grids
                // (manual registration; the vanilla merge is the fallback on any failure).
                Patches.P4_Isolation.Apply(harmony);
                Log.LogInfo("[QSystem][isolation] " + Patches.P4_Isolation.Describe());

                // v0.11.5: generic preference - hand build and blueprint paste consume the highest
                // quality building item first; the placed building inherits that item's points.
                // (v0.15.0 removed the separate blueprint-carried-quality hook that used to run
                // before this one.)
                Patches.P4_ConsumePriority.Apply(harmony);
                Log.LogInfo("[QSystem][consume] " + Patches.P4_ConsumePriority.Describe());

                // v0.18.0 (issue 4): a white ghost pre-build that was placed before its materials
                // existed now inherits the points of the items the construction module pays for it
                // later (vanilla drops that TakeTailItems inc). Registered right after the consume
                // layer, which it guards with PayWindowActive.
                Patches.P1_DeferredPay.Apply(harmony);
                Log.LogInfo("[QSystem][deferred] " + Patches.P1_DeferredPay.Describe());

                // v0.12.0 (task-16): the five "A class" facility bonuses. Every switch is independent
                // and a disabled switch means the patch is never registered at all, so the vanilla
                // code path stays untouched (byte-identical) instead of merely multiplying by 1.
                Patches.P5_LabSpeed.Apply(harmony);
                Log.LogInfo("[QSystem][lab] " + Patches.P5_LabSpeed.Describe());
                Patches.P5_TurretFireRate.Apply(harmony);
                Log.LogInfo("[QSystem][turret] " + Patches.P5_TurretFireRate.Describe());
                Patches.P5_PowerGen.Apply(harmony);
                Log.LogInfo("[QSystem][powergen] " + Patches.P5_PowerGen.Describe());
                // v0.18.0 (issue 1b): power node buildings (tesla / wireless tower / substation) scale
                // their connect distance and cover radius with the same per-point curve.
                Patches.P5_PowerRange.Apply(harmony);
                Log.LogInfo("[QSystem][powerrange] " + Patches.P5_PowerRange.Describe());
                Patches.P5_Launcher.Apply(harmony);
                Log.LogInfo("[QSystem][launcher] " + Patches.P5_Launcher.Describe());
                Patches.P5_Sprayer.Apply(harmony);
                Log.LogInfo("[QSystem][sprayer] " + Patches.P5_Sprayer.Describe());

                // v0.13.0 (task-17 / C-1): storage containers gain extra usable grids from the
                // building's quality points. The extra grids only exist in memory; a save-shave
                // prefix keeps the vanilla container shape in the .dsv. Default off.
                Patches.P4_StorageGrids.Apply(harmony);
                Log.LogInfo("[QSystem][storage] " + Patches.P4_StorageGrids.Describe());

                // v0.16.1 (C-2): per-output-port splitter quality filter. The rules are edited from
                // the filter area of the vanilla splitter window; with no rule the distribution loop
                // is the untouched vanilla one.
                Patches.P5_SplitterFilter.Apply(harmony);
                Log.LogInfo("[QSystem][splitterfilter] " + Patches.P5_SplitterFilter.Describe());
                // v0.17.0 (C-2 UI / C-3 UI): the per-port rule editor lives in the footer of the
                // vanilla item picker (opened by a splitter filter circle or a sorter filter button),
                // so nothing covers the splitter window any more.
                Patches.P5_QualityFilterPicker.Apply(harmony);
                Log.LogInfo("[QSystem][filterpicker] " + Patches.P5_QualityFilterPicker.Describe());

                // v0.16.1 (C-3): the same "item + minimum quality points" filter applied to sorters
                // (item part stays the vanilla InserterComponent.filter, only the threshold is ours).
                Patches.P5_SorterFilter.Apply(harmony);
                Log.LogInfo("[QSystem][sorterfilter] " + Patches.P5_SorterFilter.Describe());
                // v0.16.1 (C-3 UI): filter-button caption shows the threshold (Lead hotfix: same).
                Patches.P5_SorterFilterUi.Apply(harmony);
                Log.LogInfo("[QSystem][sorterfilter] " + Patches.P5_SorterFilterUi.Describe());

                // v0.19.2: keep the splitter per-port rules and the sorter minimum points inside
                // vanilla's "copy building settings" clipboard, so Shift-copy (and the panel copy
                // button) carries them to the new building.
                Patches.P6_FilterCopy.Apply(harmony);
                Log.LogInfo("[QSystem][filtercopy] " + Patches.P6_FilterCopy.Describe());

                // v0.14.0 (P6): per-item quality badges on item icons (player backpack, opened
                // storage containers, held-item tip). Pure display: nothing is saved; false = the
                // patch is not registered and no badge object is ever created.
                Patches.P6_ItemQualityBadges.Apply(harmony);
                Log.LogInfo("[QSystem][badges] " + Patches.P6_ItemQualityBadges.Describe());

                // (v0.19.0 tried a P6_DescPanel that scaled the vanilla building panel texts; it was
                // rolled back - the panel got visually messy and AssemblerUI already covers it.)

                // v0.19.0: one spray item = one spray use for all three proliferator marks
                // (vanilla Mk.I/II/III = 12/24/60 uses per item, read live from ItemProto.HpMax).
                // Old saves are folded on load so a legacy tank value never explodes.
                Patches.P6_SprayCount.Apply(harmony);
                Log.LogInfo("[QSystem][spraycount] " + Patches.P6_SprayCount.Describe());

                // (v0.19.1 tried to scale the number of spray uses one proliferator carries by the
                // quality points of that item; rolled back - it never matched what players see in
                // the tank and the flat "1 item = 1 spray" behaviour is the one that was asked for.)

                // v0.19.0 (quality recycler, design A): items carrying quality points are fed into
                // the proliferator input of a spray coater, are consumed there and their points are
                // poured into the tank as spray uses (4 points = 3 uses of a 3-point Mk.III item).
                // Vanilla spraying is untouched: harvested ticks simply skip the vanilla refill.
                Patches.P7_Recycler.Apply(harmony);
                Log.LogInfo("[QSystem][recycler] " + Patches.P7_Recycler.Describe());

                // v0.16.0 (Bug 2): keep the quality of the items consumed by the Shift+click
                // instant-build path (vanilla discards the TakeTailItems inc there).
                Patches.InstantBuildCapture.Apply(harmony);
                Log.LogInfo("[QSystem][build] " + Patches.InstantBuildCapture.Describe());

                // v0.11.9 (task-19): golden ring above max-quality buildings. Purely a HUD object
                // owned by the plugin; no building material or renderer is modified.
                // (v0.15.0 deleted the screen-space outline overlay that used to be installed here.)
                Patches.P5_TenPointHalo.Install(gameObject);
                Log.LogInfo("[QSystem][halo] " + Patches.P5_TenPointHalo.Describe());
            }
            catch (Exception e)
            {
                Log.LogError("[QSystem] Harmony PatchAll failed: " + e);
            }
        }

        private void Update()
        {
            try
            {
                if (!_startupReported)
                {
                    PollStartup();
                }
                HandleHotkeys();

                // v0.11.9 (task-19): refresh the golden ring overlay for this frame.
                // (v0.15.0 removed the outline tick.)
                Patches.P5_TenPointHalo.Tick();

                // v0.13.0: container re-shaping pump (dirty queue + incremental sweep + collapse).
                // v0.16.1: the splitter/sorter quality filter needs no per-frame tick (the rules are
                // read from the side table inside the patches and the windows refresh themselves).
                StorageGrids.Tick();
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("plugin.update", e);
            }
        }

        private void PollStartup()
        {
            _pollTicks++;
            if (_pollTicks % 30 != 0)
            {
                return;
            }

            try
            {
                if (LDB.items == null)
                {
                    return;
                }

                var protoArray = LDB.items.dataArray;
                if (protoArray == null || protoArray.Length < 100)
                {
                    return;
                }

                _startupReported = true;
                Log.LogInfo("[QSystem] LDB ready (items=" + protoArray.Length + "), running startup report.");
                Patches.P0_Tables.Apply("LDB ready");
                Diag.StartupAudit.Run();
                Log.LogInfo("[QSystem] === startup report complete ===");
            }
            catch (Exception e)
            {
                _startupReported = true;
                Log.LogError("[QSystem] startup report failed: " + e);
            }
        }

        /// <summary>
        /// Debug probes (spec section 6.2): F5 assemblers, F7 pick ring, F8 tables, F9 building inc,
        /// F10/F11 quality injection, F12 miner stats, F6 aimed miner.
        ///
        /// v0.15.0: these hotkeys are developer tools and no longer exist in the player config; they
        /// only run when the DSPQS_DEV environment variable is "1" (see QualityConfig.DevMode).
        /// </summary>
        private void HandleHotkeys()
        {
            if (!QualityConfig.DevMode)
            {
                return;
            }

            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F5))
            {
                Diag.Probes.DumpAssemblers();
            }
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F7))
            {
                Diag.Probes.DumpPicks();
            }
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F8))
            {
                Diag.Probes.DumpTables();
            }
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F9))
            {
                Diag.Probes.DumpBuildingInc();
            }
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F10))
            {
                Diag.Probes.InjectHandItemQuality();
            }
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F11))
            {
                Diag.Probes.InjectAimedBuildingQuality();
            }
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F12))
            {
                Diag.Probes.DumpMinerStats();
            }
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F6))
            {
                Diag.Probes.DumpAimedMiner();
            }
        }

        private void LogBuildInfo()
        {
            try
            {
                string location = Assembly.GetExecutingAssembly().Location;
                string stamp = "-";
                if (!string.IsNullOrEmpty(location) && File.Exists(location))
                {
                    stamp = File.GetLastWriteTime(location).ToString("yyyy-MM-dd HH:mm:ss");
                }

                Log.LogInfo("[QSystem] build: dll=" + location);
                Log.LogInfo("[QSystem] build: mtime=" + stamp + " assembly=" + typeof(Plugin).Assembly.GetName().Version);
                Log.LogInfo("[QSystem] env: game=" + UnityEngine.Application.version + " unity=" + UnityEngine.Application.unityVersion);
            }
            catch (Exception e)
            {
                Log.LogWarning("[QSystem] build info failed: " + e.Message);
            }
        }

        /// <summary>
        /// v0.15.0: only the 16 player-facing keys are printed, in the same groups as the cfg file
        /// (isolation / bonus switches / visualisation). The balance values that became internal
        /// constants are summarised on one extra line so a log still proves which numbers run.
        /// </summary>
        private void LogConfig()
        {
            Log.LogInfo("[QSystem] cfg isolateBuildingStacks=" + QualityConfig.IsolateBuildingStacks.Value
                + " isolateStorageContainers=" + QualityConfig.IsolateStorageContainers.Value
                + " | speedBonus=" + QualityConfig.SpeedBonus.Value
                + " chargeBonus=" + QualityConfig.ChargeBonus.Value
                + " labSpeedBonus=" + QualityConfig.LabSpeedBonus.Value
                + " turretFireRateBonus=" + QualityConfig.TurretFireRateBonus.Value
                + " powerGenBonus=" + QualityConfig.PowerGenBonus.Value
                + " launcherSpeedBonus=" + QualityConfig.LauncherSpeedBonus.Value
                + " sprayerIncBonus=" + QualityConfig.SprayerIncBonus.Value
                + " | itemQualityBadges=" + QualityConfig.ItemQualityBadges.Value
                + " itemQualityBadgeStyle=" + QualityConfig.ItemQualityBadgeStyleNormalized
                + " itemQualityBadgeMinPoints=" + QualityConfig.ItemQualityBadgeMinPointsClamped
                + " tenPointMarkerStyle=" + QualityConfig.MarkerStyleName
                + " tenPointHaloSize=" + QualityConfig.TenPointHaloSizeClamped.ToString("F2")
                + " tenPointHaloPulse=" + QualityConfig.HaloPulseEnabled
                + " tenPointHaloSpin=" + QualityConfig.HaloSpinClamped.ToString("F1"));

            // v0.15.0: former cfg keys that are now internal constants (kept off the player surface).
            Log.LogInfo("[QSystem] cfg builtin=(internal constants, no longer user-editable) enabled="
                + QualityConfig.EnabledValue
                + " incPerPoint=" + QualityConfig.IncPerPointValue
                + " powerPerPoint=" + QualityConfig.PowerPerPointValue
                + " buildingChancePerPoint=" + QualityConfig.BuildingChancePerPointValue.ToString("F2")
                + " minerChancePerPoint=" + QualityConfig.MinerChancePerPointValue.ToString("F2")
                + " maxUpgradeChance=" + QualityConfig.MaxUpgradeChanceValue
                + " maxQuality=" + QualityConfig.MaxQualityValue
                + " giveDismantleInc=" + QualityConfig.GiveDismantleIncValue
                + " speedPerPoint=" + QualityConfig.SpeedPerPointValue.ToString("F2")
                + " maxSpeedBonus=" + QualityConfig.MaxSpeedBonusValue.ToString("F2")
                + " chargePerPoint=" + QualityConfig.ChargePerPointValue.ToString("F2")
                + " maxChargeBonus=" + QualityConfig.MaxChargeBonusValue.ToString("F2")
                + " speedBonusMode=" + QualityConfig.SpeedBonusModeValue
                + " extraUpgradeAbove=" + QualityConfig.ExtraUpgradeAboveValue
                + " doubleUpgradeAt=" + QualityConfig.DoubleUpgradeAtValue
                + " preferHighQualityConsume=" + QualityConfig.PreferHighQualityConsumeValue
                + " tenPointHaloColor=" + QualityConfig.TenPointHaloColorValue
                + " tenPointHaloLift=" + QualityConfig.TenPointHaloLiftValue.ToString("F2")
                + " tenPointHaloMax=" + QualityConfig.TenPointHaloMaxValue
                + " storageQualityGrids=" + QualityConfig.StorageQualityGridsValue
                + " storageQualityGridsPerPoint=" + QualityConfig.StorageQualityGridsPerPointValue.ToString("F2")
                + " storageQualityGridsMax=" + QualityConfig.StorageQualityGridsMaxValue
                + " splitterQualityFilter=" + QualityConfig.SplitterQualityFilterValue
                + " sorterQualityFilter=" + QualityConfig.SorterQualityFilterValue
                + " debugProbes=" + QualityConfig.DevMode + " (env DSPQS_DEV=1 enables debug hotkeys/probes)");
        }
    }
}
