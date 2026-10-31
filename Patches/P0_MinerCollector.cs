using System;
using System.Reflection;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.16.0 (Bug 1 "big miner gets no quality bonus"): quality for the station-type vein
    /// collector (large mining machine, item 2316).
    ///
    /// Why the existing miner hook misses it (verified against _analysis/quality-system/asm_full.cs):
    ///  - `PlanetFactory.CreateEntityLogicComponents` creates the vein collector's miner with
    ///    `NewMinerComponent(entityId, desc)` (:208636) but only calls `SetMinerInsertTarget` when
    ///    the connected entity is not a station (:208341-208346), so `minerPool[minerId].insertTarget`
    ///    stays 0. `MinerComponent.InternalUpdate` therefore skips its output flush block
    ///    (`if (productCount > 0 && insertTarget > 0 && productId > 0)`, :57414) and never reaches
    ///    `factory.InsertInto(..., (byte)num17, 0, out remainInc)` (:57429) - the exact site the
    ///    P0_Miner transpiler patches. The small miner path is unaffected.
    ///  - The vein collector's items are instead drained by
    ///    `StationComponent.UpdateVeinCollection(PlanetFactory, int[])` (:77689-77722):
    ///    `storage[0].count += num2; minerPool[minerId].productCount -= num2;` - count only, the
    ///    storage slot's `inc` is never touched, so every mined item ends up with 0 quality points.
    ///
    /// Fix: after vanilla moves the items, add the rolled per-item quality for this transfer to
    /// `station.storage[0].inc`. `StationStore.inc` is an int holding the TOTAL quality points of
    /// the slot (asm_full.cs:78322-78328), so the transfer contributes `count * perItem`; the value
    /// is clamped to `count * IncPerItemMax` (10 points per item) by the pure helper in QualityMath.
    /// The roll is the same R-3 roll the small miner uses (QualityRules.RollMinerQuality, keyed by
    /// the station entityId = the building the player placed), so a 0-point building still produces
    /// 0-point ore and the existing rates/chance config values apply unchanged.
    ///
    /// Speed is NOT touched here: the station-owned miner is still driven through
    /// `minerPool[i].InternalUpdate(factory, veinPool, power, ...)` in FactorySystem.GameTick
    /// (:98920-98929) and in the parallel `_miner_parallel` path (:259207-259209), so the existing
    /// P3_Speed miner prefix (which multiplies the `power` argument) already applies to it.
    ///
    /// Degradation: every failure path is swallowed and logged once via QualityEnv.WarnOnce, so a
    /// broken hook can never abort the station tick or the save load.
    /// </summary>
    [HarmonyPatch(typeof(StationComponent), "UpdateVeinCollection",
        new[] { typeof(PlanetFactory), typeof(int[]) })]
    internal static class StationComponent_UpdateVeinCollection_OutputInc
    {
        private const string Tag = "[QSystem][miner] ";
        private const int EffectiveLogEvery = 200;
        private const int DebugLogLimit = 20;

        private static long _effective;
        private static long _zeroRolls;
        private static long _noTransfer;
        private static int _debugLogged;
        private static bool _firstLogged;

        private static void Prefix(StationComponent __instance, out int __state)
        {
            __state = 0;
            try
            {
                StationStore[] store = __instance.storage;
                if (store != null && store.Length > 0)
                {
                    __state = store[0].count;
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("miner.collector.prefix", e);
            }
        }

        private static void Postfix(StationComponent __instance, PlanetFactory factory, int __state)
        {
            try
            {
                if (factory == null || factory.planet == null)
                {
                    return;
                }

                StationStore[] store = __instance.storage;
                if (store == null || store.Length == 0)
                {
                    return;
                }

                int added = store[0].count - __state;
                if (added <= 0)
                {
                    _noTransfer++;
                    return;
                }

                int entityId = __instance.entityId;
                if (entityId <= 0)
                {
                    return;
                }

                int planetId = factory.planet.id;
                int perItem = QualityData.MinerCollectorIncLevel(factory, entityId, added);
                if (perItem <= 0)
                {
                    _zeroRolls++;
                    if (!_firstLogged)
                    {
                        _firstLogged = true;
                        Info("vein-collector quality hook active (p=" + planetId + " e=" + entityId
                            + " added=" + added + " perItem=0: building has no points or the roll missed)");
                    }
                    return;
                }

                int before = store[0].inc;
                store[0].inc = QualityMath.StationIncAfterTransfer(before, added, perItem, store[0].count);
                _effective++;

                if (!_firstLogged)
                {
                    _firstLogged = true;
                    Info("vein-collector quality path active: p=" + planetId + " e=" + entityId
                        + " added=" + added + " perItem=" + perItem
                        + " inc=" + before + "->" + store[0].inc);
                }

                if (_effective % EffectiveLogEvery == 0)
                {
                    Info("vein collector effective=" + _effective
                        + " zeroRolls=" + _zeroRolls
                        + " noTransfer=" + _noTransfer);
                }

                if (QualityConfig.DevMode && _debugLogged < DebugLogLimit)
                {
                    _debugLogged++;
                    Info("vein collector transfer p=" + planetId + " e=" + entityId
                        + " added=" + added + " perItem=" + perItem
                        + " inc=" + store[0].inc + "/" + (store[0].count * QualityMath.IncPerItemMax));
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("miner.collector", e);
            }
        }

        /// <summary>Reflection self-check text for Diag.StartupAudit (never throws).</summary>
        internal static string Describe()
        {
            return "vein-collector quality hook (station storage inc, active=" + _effective
                + " zeroRolls=" + _zeroRolls + ")";
        }

        /// <summary>Test/diagnostic hook: forget the counters of this session.</summary>
        internal static void ResetState()
        {
            _effective = 0;
            _zeroRolls = 0;
            _noTransfer = 0;
            _debugLogged = 0;
            _firstLogged = false;
        }

        private static void Info(string message)
        {
            if (Plugin.Log != null)
            {
                Plugin.Log.LogInfo(Tag + message);
            }
        }
    }
}
