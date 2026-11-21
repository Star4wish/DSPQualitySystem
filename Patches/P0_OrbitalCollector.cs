using System;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.20.0 (issue 3): "an orbital collector (gas giant station) with quality points produces
    /// items without any points".
    ///
    /// Vanilla `StationComponent.UpdateCollection(PlanetFactory, float collectSpeedRate, int[]
    /// productRegister)` (decompiled StationComponent, method UpdateCollection) accumulates the
    /// mining progress and then only does
    ///
    ///     storage[i].count += num;
    ///     productRegister[storage[i].itemId] += num;
    ///
    /// - the storage slot's `inc` is never written, so every collected item carries 0 points even
    /// though the station building itself was built from a quality item. This is the exact same
    /// shape as the large-mining-machine bug fixed in v0.16.0 (`UpdateVeinCollection`), just on the
    /// gas-giant collector path.
    ///
    /// Fix: snapshot the slot counts before vanilla, and after vanilla add the rolled per-item
    /// quality for the transferred count to `storage[i].inc` (StationStore.inc is the TOTAL points
    /// of the slot, so the contribution is `added * perItem`, clamped to `count * 10` by
    /// QualityMath.StationIncAfterTransfer). The roll is the same R-3 miner roll keyed by the
    /// station entityId, so a 0-point collector still produces 0-point items and the configured
    /// miner chance/curve is reused unchanged.
    ///
    /// Every failure path is swallowed and logged once via QualityEnv.WarnOnce so a broken hook can
    /// never abort the station tick or the save load.
    /// </summary>
    [HarmonyPatch(typeof(StationComponent), "UpdateCollection",
        new[] { typeof(PlanetFactory), typeof(float), typeof(int[]) })]
    internal static class StationComponent_UpdateCollection_OutputInc
    {
        private const string Tag = "[QSystem][collector] ";
        private const int EffectiveLogEvery = 200;
        private const int DebugLogLimit = 20;

        private static long _effective;
        private static long _zeroRolls;
        private static long _noTransfer;
        private static int _debugLogged;
        private static bool _firstLogged;

        /// <summary>
        /// Per-thread scratch: StationComponent ticks can run on several worker threads, so the
        /// before-image must not be a plain static array. Grown once per thread and reused.
        /// </summary>
        [ThreadStatic] private static int[] _before;

        private static void Prefix(StationComponent __instance)
        {
            try
            {
                StationStore[] store = __instance.storage;
                if (store == null || store.Length == 0)
                {
                    return;
                }
                int[] before = _before;
                if (before == null || before.Length < store.Length)
                {
                    before = new int[store.Length];
                    _before = before;
                }
                for (int i = 0; i < store.Length; i++)
                {
                    before[i] = store[i].count;
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("collector.prefix", e);
            }
        }

        private static void Postfix(StationComponent __instance, PlanetFactory factory)
        {
            try
            {
                if (!QualityConfig.Enabled.Value || factory == null || factory.planet == null)
                {
                    return;
                }
                if (!__instance.isCollector)
                {
                    return;
                }

                StationStore[] store = __instance.storage;
                int[] before = _before;
                if (store == null || before == null || store.Length == 0)
                {
                    return;
                }

                int entityId = __instance.entityId;
                if (entityId <= 0)
                {
                    return;
                }

                int planetId = factory.planet.id;
                int slots = store.Length < before.Length ? store.Length : before.Length;
                for (int i = 0; i < slots; i++)
                {
                    int added = store[i].count - before[i];
                    if (added <= 0)
                    {
                        _noTransfer++;
                        continue;
                    }

                    int perItem = QualityData.MinerCollectorIncLevel(factory, entityId, added);
                    if (perItem <= 0)
                    {
                        _zeroRolls++;
                        if (!_firstLogged)
                        {
                            _firstLogged = true;
                            Info("orbital collector quality hook active (p=" + planetId + " e=" + entityId
                                + " slot=" + i + " added=" + added
                                + " perItem=0: building has no points or the roll missed)");
                        }
                        continue;
                    }

                    int beforeInc = store[i].inc;
                    store[i].inc = QualityMath.StationIncAfterTransfer(beforeInc, added, perItem,
                        store[i].count);
                    _effective++;

                    if (!_firstLogged)
                    {
                        _firstLogged = true;
                        Info("orbital collector quality path active: p=" + planetId + " e=" + entityId
                            + " slot=" + i + " added=" + added + " perItem=" + perItem
                            + " inc=" + beforeInc + "->" + store[i].inc);
                    }

                    if (_effective % EffectiveLogEvery == 0)
                    {
                        Info("orbital collector effective=" + _effective
                            + " zeroRolls=" + _zeroRolls + " noTransfer=" + _noTransfer);
                    }

                    if (QualityConfig.DevMode && _debugLogged < DebugLogLimit)
                    {
                        _debugLogged++;
                        Info("orbital collector transfer p=" + planetId + " e=" + entityId
                            + " slot=" + i + " added=" + added + " perItem=" + perItem
                            + " inc=" + store[i].inc + "/" + (store[i].count * QualityMath.IncPerItemMax));
                    }
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("collector.postfix", e);
            }
        }

        internal static string Describe()
        {
            return "orbital-collector quality hook (station storage inc, active=" + _effective
                + " zeroRolls=" + _zeroRolls + ")";
        }

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
