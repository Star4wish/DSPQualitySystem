using System;
using System.Threading;

namespace DSPQualitySystem
{
    /// <summary>
    /// Quality rules (spec section 2.3). R-1 (inherit min input level) lives in the assembler
    /// prefix; R-2 (building upgrade roll) is implemented here; R-3 (miner) arrived in M3.
    /// </summary>
    public static class QualityRules
    {
        [ThreadStatic]
        private static Random _rng;

        private static Random Rng
        {
            get
            {
                if (_rng == null)
                {
                    _rng = new Random(unchecked(Environment.TickCount * 31 + Thread.CurrentThread.ManagedThreadId));
                }
                return _rng;
            }
        }

        public static int ClampLevel(int level)
        {
            return QualityMath.ClampLevel(level, QualityConfig.MaxLevel);
        }

        /// <summary>
        /// R-1 + R-2: output level of one production cycle. The caller passes the inherited
        /// input level; a building with quality points may upgrade the output. v0.9.0 extension:
        /// 7/8/9 points roll a second upgrade, 10 points are a guaranteed +2 (both configurable,
        /// see extraUpgradeAbove / doubleUpgradeAt).
        /// </summary>
        public static int RollCycleQuality(int planetId, int entityId, int inputLevel)
        {
            return RollCycleQuality(planetId, entityId, inputLevel, true);
        }

        /// <summary>
        /// Same as the three argument overload; <paramref name="allowExtraUpgrade"/> = false keeps
        /// the pre-v0.9.0 single roll. The fractionator production hook passes false so the M4 rule
        /// stays exactly as it was (the user explicitly asked not to change the fractionator), while
        /// assembler style production buildings opt into the extra rolls.
        /// </summary>
        public static int RollCycleQuality(int planetId, int entityId, int inputLevel, bool allowExtraUpgrade)
        {
            byte buildingInc = QualityData.GetBuildingInc(planetId, entityId);
            int mode;
            bool baseHit;
            bool extraHit;
            int level = QualityMath.RollCycleLevelCurve(inputLevel, buildingInc, allowExtraUpgrade,
                QualityConfig.DoubleUpgradeAtClamped, QualityConfig.ExtraUpgradeAboveClamped,
                QualityConfig.MaxLevel, RollPercent, out mode, out baseHit, out extraHit);
            LogRoll(planetId, entityId, inputLevel, buildingInc, allowExtraUpgrade, mode, baseHit, extraHit, level);
            return level;
        }

        /// <summary>
        /// R-3 (spec section 3.5, v0.9.0 multi-roll; rebalanced by v0.18.0 issue 3): quality points of
        /// one mined item. The miner gets floor(buildingInc / 2) independent rolls and every roll now
        /// succeeds for certain (MinerChancePerPointValue = 100), so the rule the player asked for is
        /// "each two levels of the miner add exactly one quality point to the mined product":
        /// 2 points -> +1, 4 points -> +2, 10 points -> +5. The item cap still equals the roll count,
        /// and the multi-roll loop is exactly the v0.9.0 code - only the chance constant changed, so a
        /// future rebalance can dial it back without touching this loop.
        /// </summary>
        public static int RollMinerQuality(int planetId, int entityId)
        {
            byte buildingInc = QualityData.GetBuildingInc(planetId, entityId);
            if (buildingInc <= 0)
            {
                return 0;
            }

            int rolls = QualityMath.MinerRollsOf(buildingInc);
            if (rolls <= 0)
            {
                return 0;
            }

            float chance = QualityMath.MinerChance(buildingInc, QualityConfig.MinerChancePerPoint.Value);
            if (chance <= 0f)
            {
                return 0;
            }

            int inc = 0;
            for (int i = 0; i < rolls; i++)
            {
                if (chance >= 100f || RollPercent(chance))
                {
                    inc++;
                }
            }
            return inc > rolls ? rolls : inc;
        }

        private const int RollLogLimit = 20;
        private const int RollLogMaxKeys = 4096;

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, int> RollLogCounts =
            new System.Collections.Concurrent.ConcurrentDictionary<long, int>();

        /// <summary>Test hook: reseed the thread-local roller so offline tests are deterministic.</summary>
        internal static void ResetRandomForTests(int seed)
        {
            _rng = new Random(seed);
        }

        /// <summary>
        /// v0.11.1: read-only observation of the R-1/R-2 cycle roll, enabled by debugProbes.
        /// Throttled to the first 20 calls per (planet, entity) so it can be left on in a live save.
        /// This method must never roll, mutate the level or throw: the pure roll already happened in
        /// QualityMath.RollCycleLevel and the values here are only the ones it used/returned.
        /// </summary>
        private static void LogRoll(int planetId, int entityId, int inputLevel, int buildingInc,
            bool allowExtraUpgrade, int mode, bool baseHit, bool extraHit, int pooled)
        {
            try
            {
                if (QualityConfig.DebugProbes == null || !QualityConfig.DebugProbes.Value || Plugin.Log == null)
                {
                    return;
                }

                long key = ((long)planetId << 32) ^ (uint)entityId;
                int seen;
                RollLogCounts.TryGetValue(key, out seen);
                if (seen >= RollLogLimit)
                {
                    return;
                }
                RollLogCounts[key] = seen + 1;
                if (RollLogCounts.Count > RollLogMaxKeys)
                {
                    RollLogCounts.Clear();
                }

                Plugin.Log.LogInfo("[QSystem][roll] eid=" + entityId
                    + " in=" + inputLevel
                    + " bld=" + buildingInc
                    + " mode=" + mode
                    + " base=" + (baseHit ? 1 : 0)
                    + " extra=" + (extraHit ? 1 : 0)
                    + " pooled=" + pooled
                    + " allowExtra=" + (allowExtraUpgrade ? "true" : "false"));
            }
            catch (Exception)
            {
                // observation only: never disturb the production path
            }
        }
        private static readonly System.Collections.Generic.HashSet<int> ExcludedStorageIds =
            new System.Collections.Generic.HashSet<int>();
        private static bool _mechaStoragesScanned;
        private static bool _mechaStorageWarningLogged;

        /// <summary>
        /// v0.11.3 (P4 containers): decide whether a storage keeps building stacks of different
        /// quality in separate grids. The player inventory always passes (the v0.10.0 scope).
        /// Another container passes only when isolateStorageContainers is on, the type is a plain
        /// EStorageType.Default container, it has no filtered grids and it is not one of the
        /// mecha's built-in storages (reactor/warp/ammo/bomb/fighter: their grids are slot-filtered
        /// fuel/ammo/fighter slots where fragmenting by quality would waste capacity). Never
        /// throws: any failure returns false, so the caller falls back to the vanilla merge/sort.
        /// </summary>
        public static bool ShouldIsolateStorage(StorageComponent storage)
        {
            try
            {
                if (storage == null)
                {
                    return false;
                }
                if (storage.isPlayerInventory)
                {
                    return QualityMath.ShouldIsolateStorage(true, (int)storage.type, storage.size, false);
                }
                bool containers = QualityConfig.IsolateStorageContainers != null
                    && QualityConfig.IsolateStorageContainers.Value;
                if (!QualityMath.ShouldIsolateStorage(false, (int)storage.type, storage.size, containers))
                {
                    return false;
                }
                EnsureMechaStoragesExcluded();
                if (ExcludedStorageIds.Contains(storage.id))
                {
                    return false;
                }
                StorageComponent.GRID[] grids = storage.grids;
                if (grids == null || grids.Length == 0)
                {
                    return false;
                }
                int count = storage.size <= grids.Length ? storage.size : grids.Length;
                for (int i = 0; i < count; i++)
                {
                    if (grids[i].filter != 0)
                    {
                        // a filtered container keeps the vanilla merge (the player configured slots)
                        return false;
                    }
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Lazily collect the mecha storages once the main player exists (retries after loading).</summary>
        private static void EnsureMechaStoragesExcluded()
        {
            if (_mechaStoragesScanned)
            {
                return;
            }
            try
            {
                Player player = GameMain.mainPlayer;
                Mecha mecha = player == null ? null : player.mecha;
                if (mecha == null)
                {
                    // main menu / loading: nothing to exclude yet, try again on the next call
                    return;
                }
                _mechaStoragesScanned = true;
                AddExcludedStorage(mecha.reactorStorage);
                AddExcludedStorage(mecha.warpStorage);
                AddExcludedStorage(mecha.ammoStorage);
                AddExcludedStorage(mecha.bombStorage);
                AddExcludedStorage(mecha.fighterStorage);
            }
            catch (Exception e)
            {
                if (!_mechaStorageWarningLogged)
                {
                    _mechaStorageWarningLogged = true;
                    QualityEnv.WarnOnce("isolation.mecha", e);
                }
            }
        }

        private static void AddExcludedStorage(StorageComponent storage)
        {
            if (storage != null && storage.id > 0)
            {
                ExcludedStorageIds.Add(storage.id);
            }
        }
        /// <summary>Roll a percentage chance with 0.01% resolution.</summary>
        public static bool RollPercent(float percent)
        {
            return QualityMath.RollPercent(percent, Rng);
        }
    }
}
