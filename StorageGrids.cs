using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DSPQualitySystem
{
    /// <summary>
    /// v0.13.0 (task-17 / C-1): runtime half of the "quality grid" rule for storage containers.
    ///
    /// A plain storage container (EStorageType.Default, never the player inventory) gains
    /// <c>floor(qualityPoints * storageQualityGridsPerPoint)</c> extra usable grids. The extra grids
    /// only ever exist in memory: before the game serialises the factories the shapes are shaved
    /// back to the vanilla size (see <see cref="BeginExportShave"/>), so the .dsv always carries the
    /// untouched vanilla structure. Items that sit in the extra grids are moved back into free
    /// vanilla grids when possible; whatever does not fit is persisted in the existing MOD trailer
    /// (a JSON blob appended after the vanilla payload, ignored by a vanilla client).
    /// </summary>
    internal static class StorageGrids
    {
        internal struct OverflowCell
        {
            public int grid;
            public int itemId;
            public int filter;
            public int count;
            public int stackSize;
            public int inc;
        }

        internal sealed class OverflowRecord
        {
            public int planetId;
            public int entityId;
            public int baseGrids;
            public List<OverflowCell> cells = new List<OverflowCell>();
        }

        private sealed class ShavedState
        {
            public StorageComponent storage;
            public int originalSize;
            public int originalBans;
        }

        /// <summary>A trailer entry whose container was not instantiated yet at import time.</summary>
        private sealed class PendingLoad
        {
            public int planetId;
            public int entityId;
            public int baseGrids;
            public List<OverflowCell> cells;
        }

        private const int DirtyBudget = 128;
        private const int SweepBudget = 2048;
        private const int CollapseBudget = 8;
        private const int CellCeiling = 8192;

        private static readonly ConcurrentQueue<long> dirty = new ConcurrentQueue<long>();
        private static readonly HashSet<long> expanded = new HashSet<long>();
        private static readonly List<ShavedState> shaved = new List<ShavedState>();
        private static readonly List<OverflowRecord> overflow = new List<OverflowRecord>();
        private static readonly List<PendingLoad> pendingLoads = new List<PendingLoad>();

        private static bool armed;
        private static int sweepPlanet;
        private static int sweepEntity;
        private static bool sweepRequested;
        private static int collapseTick;
        private static int expandedLogs;
        private static int shaveLogs;
        private static int sweepContainers;
        private static int sweepExpanded;
        private static int unsupportedLogs;

        /// <summary>True once <see cref="Install"/> registered the export hook (cfg allowed it).</summary>
        internal static bool Armed
        {
            get { return armed; }
        }

        internal static int ExpandedCount
        {
            get { return expanded.Count; }
        }

        internal static bool Enabled
        {
            get { return QualityConfig.StorageGridsEnabled; }
        }

        /// <summary>Snapshot of the containers whose extra grids were shaved for the current save.</summary>
        internal static List<OverflowRecord> SaveSnapshot
        {
            get { return overflow; }
        }

        internal static void Install()
        {
            armed = true;
            QualityData.BuildingIncChanged = OnBuildingIncChanged;
            RequestFullSweep();
            QualityEnv.LogInfo("[QSystem][storage] rule armed: perPoint="
                + QualityConfig.StorageGridsPerPointClamped.ToString("0.###")
                + " maxExtra=" + QualityConfig.StorageGridsMaxClamped);
        }

        internal static void ResetRuntime()
        {
            long dropped;
            while (dirty.TryDequeue(out dropped))
            {
                // drain: the queued keys describe the previous game world
            }
            expanded.Clear();
            shaved.Clear();
            overflow.Clear();
            pendingLoads.Clear();
            sweepRequested = false;
            collapseTick = 0;
        }

        private static void OnBuildingIncChanged(int planetId, int entityId)
        {
            if (!armed || entityId <= 0)
            {
                return;
            }
            Enqueue(planetId, entityId);
        }

        private static long Key(int planetId, int entityId)
        {
            return ((long)planetId << 32) | (uint)entityId;
        }

        private static void Enqueue(int planetId, int entityId)
        {
            dirty.Enqueue(Key(planetId, entityId));
        }

        internal static void RequestFullSweep()
        {
            sweepRequested = true;
            sweepPlanet = 0;
            sweepEntity = 1;
        }

        /// <summary>Main-thread pump: drains changed containers, the startup sweep and collapse work.</summary>
        internal static void Tick()
        {
            if (!armed)
            {
                return;
            }
            try
            {
                for (int i = 0; i < DirtyBudget; i++)
                {
                    long key;
                    if (!dirty.TryDequeue(out key))
                    {
                        break;
                    }
                    int planetId = (int)(key >> 32);
                    int entityId = (int)(key & 0xFFFFFFFF);
                    Apply(planetId, entityId);
                }

                FlushPendingLoads();

                if (sweepRequested)
                {
                    SweepStep();
                }
                collapseTick++;
                if (collapseTick % 30 == 0)
                {
                    CollapseStep();
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("storage.tick", e);
            }
        }

        private static void SweepStep()
        {
            GameData data = GameMain.data;
            if (data == null || data.factories == null)
            {
                return;
            }
            int budget = SweepBudget;
            while (budget > 0)
            {
                if (sweepPlanet >= data.factories.Length)
                {
                    sweepRequested = false;
                    sweepPlanet = 0;
                    sweepEntity = 1;
                    if (QualityConfig.DebugProbes.Value)
                    {
                        QualityEnv.LogInfo("[QSystem][storage] sweep done: containers=" + sweepContainers
                            + " expandedInSweep=" + sweepExpanded + " expandedTotal=" + expanded.Count
                            + " (perPoint=" + QualityConfig.StorageGridsPerPointClamped.ToString("0.###")
                            + " maxExtra=" + QualityConfig.StorageGridsMaxClamped + ")");
                    }
                    sweepContainers = 0;
                    sweepExpanded = 0;
                    return;
                }
                PlanetFactory factory = data.factories[sweepPlanet];
                if (factory == null || factory.entityPool == null || factory.planet == null)
                {
                    sweepPlanet++;
                    sweepEntity = 1;
                    continue;
                }
                // sweepPlanet is an index into GameData.factories (= PlanetData.factoryIndex),
                // never a planet id. All side tables are keyed by the real planet id.
                int sweptPlanetId = factory.planet.id;
                int end = factory.entityPool.Length;
                if (factory.entityCursor > 0 && factory.entityCursor < end)
                {
                    end = factory.entityCursor;
                }
                while (sweepEntity < end && budget > 0)
                {
                    EntityData entity = factory.entityPool[sweepEntity];
                    if (entity.storageId > 0)
                    {
                        sweepContainers++;
                        if (QualityConfig.DebugProbes.Value && sweepContainers <= 8)
                        {
                            StorageComponent sc = StorageOf(factory, sweepEntity);
                            QualityEnv.LogInfo("[QSystem][storage] container p=" + sweptPlanetId
                                + " e=" + sweepEntity
                                + " storageId=" + entity.storageId + " size=" + (sc == null ? -1 : sc.size)
                                + " type=" + (sc == null ? "-" : sc.type.ToString())
                                + " points=" + QualityData.GetBuildingInc(sweptPlanetId, sweepEntity)
                                + " supported=" + (sc != null && IsSupportedContainer(sc)));
                        }
                        int beforeExpand = expanded.Count;
                        Apply(sweptPlanetId, sweepEntity);
                        if (expanded.Count > beforeExpand)
                        {
                            sweepExpanded++;
                        }
                    }
                    sweepEntity++;
                    budget--;
                }
                if (sweepEntity >= end)
                {
                    sweepPlanet++;
                    sweepEntity = 1;
                    // Drop side-table entries of containers that vanished while the rule was off.
                    PrunePlanet(sweptPlanetId, factory);
                }
            }
        }

        private static void PrunePlanet(int planetId, PlanetFactory factory)
        {
            PlanetQuality quality = QualityData.Get(planetId);
            if (quality == null || quality.storageBase == null)
            {
                return;
            }
            int[] bases = quality.storageBase;
            for (int i = 1; i < bases.Length; i++)
            {
                if (bases[i] <= 0)
                {
                    continue;
                }
                bool alive = factory.entityPool != null && i < factory.entityPool.Length
                    && factory.entityPool[i].storageId > 0;
                if (!alive)
                {
                    QualityData.ClearStorageBase(planetId, i);
                    expanded.Remove(Key(planetId, i));
                }
            }
        }

        /// <summary>Re-shape one container to the grid count implied by its quality points.</summary>
        private static void Apply(int planetId, int entityId)
        {
            PlanetFactory factory = FactoryOf(planetId);
            if (factory == null)
            {
                return;
            }
            StorageComponent storage = StorageOf(factory, entityId);
            if (storage == null || !IsSupportedContainer(storage))
            {
                if (storage != null && QualityConfig.DebugProbes.Value && unsupportedLogs < 5)
                {
                    unsupportedLogs++;
                    QualityEnv.LogInfo("[QSystem][storage] skip unsupported container p=" + planetId
                        + " e=" + entityId + " type=" + storage.type + " size=" + storage.size
                        + " entityId=" + storage.entityId);
                }
                return;
            }
            int baseGrids = QualityData.GetStorageBase(planetId, entityId);
            if (baseGrids <= 0)
            {
                baseGrids = storage.size;
                if (baseGrids <= 0)
                {
                    return;
                }
                QualityData.SetStorageBase(planetId, entityId, baseGrids);
            }
            if (storage.size < baseGrids)
            {
                // Vanilla shrank/imported the container under us: trust the live size again.
                baseGrids = storage.size;
                QualityData.SetStorageBase(planetId, entityId, baseGrids);
            }

            int points = QualityData.GetBuildingInc(planetId, entityId);
            int extra = Enabled
                ? QualityMath.StorageExtraGrids(points, QualityConfig.StorageGridsPerPointClamped,
                    QualityConfig.StorageGridsMaxClamped, baseGrids)
                : 0;
            int want = baseGrids + extra;
            long key = Key(planetId, entityId);

            if (want > storage.size)
            {
                storage.SetSize(want);
                expanded.Add(key);
                LogExpanded(planetId, entityId, baseGrids, extra, storage.size, points);
            }
            else if (want < storage.size)
            {
                if (!OverflowEmpty(storage, want, storage.size))
                {
                    // Keep the extra room while items still live in it; retried by CollapseStep.
                    return;
                }
                if (storage.bans > want)
                {
                    storage.SetBans(want);
                }
                storage.SetSize(want);
                if (want <= baseGrids)
                {
                    expanded.Remove(key);
                }
            }
        }

        private static void LogExpanded(int planetId, int entityId, int baseGrids, int extra, int total, int points)
        {
            if (!QualityConfig.DebugProbes.Value)
            {
                return;
            }
            if (expandedLogs >= 20)
            {
                return;
            }
            expandedLogs++;
            QualityEnv.LogInfo("[QSystem][storage] expanded p=" + planetId + " e=" + entityId
                + " points=" + points + " base=" + baseGrids + " extra=" + extra + " total=" + total);
        }

        private static void CollapseStep()
        {
            if (expanded.Count == 0)
            {
                return;
            }
            var keys = new List<long>(expanded);
            int done = 0;
            for (int i = 0; i < keys.Count && done < CollapseBudget; i++)
            {
                long key = keys[i];
                int planetId = (int)(key >> 32);
                int entityId = (int)(key & 0xFFFFFFFF);
                done++;
                if (CanCollapse(planetId, entityId))
                {
                    Apply(planetId, entityId);
                }
            }
        }

        private static bool CanCollapse(int planetId, int entityId)
        {
            PlanetFactory factory = FactoryOf(planetId);
            if (factory == null)
            {
                expanded.Remove(Key(planetId, entityId));
                return false;
            }
            StorageComponent storage = StorageOf(factory, entityId);
            if (storage == null)
            {
                expanded.Remove(Key(planetId, entityId));
                return false;
            }
            int baseGrids = QualityData.GetStorageBase(planetId, entityId);
            if (baseGrids <= 0 || storage.size <= baseGrids)
            {
                expanded.Remove(Key(planetId, entityId));
                return false;
            }
            return OverflowEmpty(storage, baseGrids, storage.size);
        }

        private static bool OverflowEmpty(StorageComponent storage, int from, int size)
        {
            StorageComponent.GRID[] grids = storage.grids;
            if (grids == null)
            {
                return true;
            }
            if (size > grids.Length)
            {
                size = grids.Length;
            }
            for (int i = from; i < size; i++)
            {
                if (grids[i].itemId > 0 || grids[i].count > 0)
                {
                    return false;
                }
            }
            return true;
        }

        internal static bool IsSupportedContainer(StorageComponent storage)
        {
            if (storage == null || storage.isPlayerInventory)
            {
                return false;
            }
            return QualityMath.ShouldIsolateStorage(false, (int)storage.type, storage.size, true);
        }

        internal static PlanetFactory FactoryOf(int planetId)
        {
            GameData data = GameMain.data;
            if (data == null || data.factories == null || data.galaxy == null)
            {
                return null;
            }
            // GameData.factories is indexed by PlanetData.factoryIndex, not by planet id
            // (asm_full.cs:192576 `stars[i].planets`, 186111 `public int factoryIndex;`,
            // 77079 `galaxy.PlanetById(planetId).factoryIndex`). Indexing it with a planet id
            // silently picks another planet (or null) - the C-1 sweep regression of 2026-10-28.
            PlanetData planet = data.galaxy.PlanetById(planetId);
            if (planet == null)
            {
                return null;
            }
            int factoryIndex = planet.factoryIndex;
            if (factoryIndex < 0 || factoryIndex >= data.factories.Length)
            {
                return null;
            }
            return data.factories[factoryIndex];
        }

        internal static StorageComponent StorageOf(PlanetFactory factory, int entityId)
        {
            if (factory == null || factory.entityPool == null)
            {
                return null;
            }
            if (entityId <= 0 || entityId >= factory.entityPool.Length)
            {
                return null;
            }
            int storageId = factory.entityPool[entityId].storageId;
            if (storageId <= 0)
            {
                return null;
            }
            FactoryStorage factoryStorage = factory.factoryStorage;
            if (factoryStorage == null || factoryStorage.storagePool == null
                || storageId >= factoryStorage.storagePool.Length)
            {
                return null;
            }
            StorageComponent storage = factoryStorage.storagePool[storageId];
            if (storage == null || storage.id != storageId || storage.entityId != entityId)
            {
                return null;
            }
            return storage;
        }

        /// <summary>
        /// GameData.Export prefix: put every expanded container back to its vanilla shape and stash
        /// the contents of the extra grids so the trailer writer can persist them.
        /// </summary>
        internal static void BeginExportShave(GameData data)
        {
            if (!armed)
            {
                return;
            }
            try
            {
                shaved.Clear();
                overflow.Clear();
                if (expanded.Count == 0 || data == null || data.factories == null)
                {
                    return;
                }
                int cells = 0;
                foreach (long key in new List<long>(expanded))
                {
                    int planetId = (int)(key >> 32);
                    int entityId = (int)(key & 0xFFFFFFFF);
                    PlanetFactory factory = FactoryOf(planetId);
                    if (factory == null)
                    {
                        continue;
                    }
                    StorageComponent storage = StorageOf(factory, entityId);
                    if (storage == null)
                    {
                        continue;
                    }
                    int baseGrids = QualityData.GetStorageBase(planetId, entityId);
                    if (baseGrids <= 0 || storage.size <= baseGrids)
                    {
                        continue;
                    }
                    CompactIntoBase(storage, baseGrids);
                    var record = new OverflowRecord();
                    record.planetId = planetId;
                    record.entityId = entityId;
                    record.baseGrids = baseGrids;
                    StorageComponent.GRID[] grids = storage.grids;
                    int limit = storage.size < grids.Length ? storage.size : grids.Length;
                    for (int i = baseGrids; i < limit && cells < CellCeiling; i++)
                    {
                        if (grids[i].itemId > 0 || grids[i].count > 0)
                        {
                            var cell = new OverflowCell();
                            cell.grid = i;
                            cell.itemId = grids[i].itemId;
                            cell.filter = grids[i].filter;
                            cell.count = grids[i].count;
                            cell.stackSize = grids[i].stackSize;
                            cell.inc = grids[i].inc;
                            record.cells.Add(cell);
                            cells++;
                        }
                    }
                    shaved.Add(new ShavedState
                    {
                        storage = storage,
                        originalSize = storage.size,
                        originalBans = storage.bans
                    });
                    // bans counts disabled grids at the tail (player slider / battle base): it must
                    // never exceed the size written into the save, otherwise a vanilla client would
                    // import a negative usable-grid count.
                    if (storage.bans > baseGrids)
                    {
                        storage.bans = baseGrids;
                    }
                    storage.size = baseGrids;
                    if (record.cells.Count > 0)
                    {
                        overflow.Add(record);
                    }
                }
                if (shaved.Count > 0 && shaveLogs < 20)
                {
                    shaveLogs++;
                    QualityEnv.LogInfo("[QSystem][storage] save shave: containers=" + shaved.Count
                        + " overflowCells=" + cells
                        + (cells > 0 ? " (kept in the MOD trailer)" : string.Empty));
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("storage.shave", e);
                EndExportShave();
            }
        }

        /// <summary>GameData.Export postfix/finalizer: restore the live shape of every shaved container.</summary>
        internal static void EndExportShave()
        {
            if (shaved.Count == 0)
            {
                return;
            }
            try
            {
                for (int i = 0; i < shaved.Count; i++)
                {
                    ShavedState state = shaved[i];
                    if (state != null && state.storage != null)
                    {
                        state.storage.size = state.originalSize;
                        state.storage.bans = state.originalBans;
                    }
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("storage.restore", e);
            }
            finally
            {
                shaved.Clear();
            }
        }

        private static void CompactIntoBase(StorageComponent storage, int baseGrids)
        {
            StorageComponent.GRID[] grids = storage.grids;
            if (grids == null || baseGrids <= 0 || baseGrids > grids.Length)
            {
                return;
            }
            int limit = storage.size < grids.Length ? storage.size : grids.Length;
            // Target only the grids the player can actually use (bans disables the tail).
            int usable = baseGrids - storage.bans;
            if (usable > baseGrids)
            {
                usable = baseGrids;
            }
            for (int j = baseGrids; j < limit; j++)
            {
                if (grids[j].itemId <= 0 && grids[j].count <= 0)
                {
                    continue;
                }
                int target = -1;
                for (int i = 0; i < usable; i++)
                {
                    if (grids[i].itemId <= 0 && grids[i].count <= 0)
                    {
                        target = i;
                        break;
                    }
                }
                if (target < 0)
                {
                    return;
                }
                grids[target] = grids[j];
                grids[j].itemId = 0;
                grids[j].filter = 0;
                grids[j].count = 0;
                grids[j].stackSize = 0;
                grids[j].inc = 0;
            }
        }

        /// <summary>Called by the trailer writer for each planet node (may return an empty list).</summary>
        internal static List<OverflowRecord> OverflowForPlanet(int planetId)
        {
            List<OverflowRecord> result = null;
            for (int i = 0; i < overflow.Count; i++)
            {
                if (overflow[i].planetId != planetId)
                {
                    continue;
                }
                if (result == null)
                {
                    result = new List<OverflowRecord>();
                }
                result.Add(overflow[i]);
            }
            return result;
        }

        /// <summary>Clear the save snapshot once the trailer has been written (or the save aborted).</summary>
        internal static void ClearSaveSnapshot()
        {
            overflow.Clear();
        }

        /// <summary>
        /// Called from the trailer reader: restore one container entry (base size + overflow cells).
        /// Runs during GameData.Import; the factory of a non-home planet may not be instantiated
        /// yet, in which case the entry is parked and replayed by <see cref="FlushPendingLoads"/>.
        /// </summary>
        internal static void ApplyLoadedStorage(int planetId, int entityId, int baseGrids, List<OverflowCell> cells)
        {
            if (!armed)
            {
                if (cells != null && cells.Count > 0)
                {
                    QualityEnv.WarnOnce("storage.load.notarmed",
                        "[QSystem][storage] trailer has overflow cells but the feature is NOT ARMED;"
                        + " those cells stay inside the save blob only (vanilla grids keep their items)");
                }
                return;
            }
            if (entityId <= 0 || baseGrids <= 0)
            {
                return;
            }
            try
            {
                QualityData.SetStorageBase(planetId, entityId, baseGrids);
                PlanetFactory factory = FactoryOf(planetId);
                StorageComponent storage = factory == null ? null : StorageOf(factory, entityId);
                if (storage == null)
                {
                    if (pendingLoads.Count < 4096)
                    {
                        var pending = new PendingLoad();
                        pending.planetId = planetId;
                        pending.entityId = entityId;
                        pending.baseGrids = baseGrids;
                        pending.cells = cells;
                        pendingLoads.Add(pending);
                    }
                    return;
                }
                RestoreInto(planetId, entityId, baseGrids, storage, cells);
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("storage.load", e);
            }
        }

        /// <summary>Retry the trailer entries whose factory has been instantiated since the import.</summary>
        private static void FlushPendingLoads()
        {
            if (pendingLoads.Count == 0)
            {
                return;
            }
            int budget = 16;
            for (int i = 0; i < pendingLoads.Count && budget > 0; i++)
            {
                PendingLoad pending = pendingLoads[i];
                PlanetFactory factory = FactoryOf(pending.planetId);
                StorageComponent storage = factory == null ? null : StorageOf(factory, pending.entityId);
                if (storage == null)
                {
                    continue;
                }
                pendingLoads.RemoveAt(i);
                i--;
                budget--;
                RestoreInto(pending.planetId, pending.entityId, pending.baseGrids, storage, pending.cells);
            }
        }

        private static void RestoreInto(int planetId, int entityId, int baseGrids,
            StorageComponent storage, List<OverflowCell> cells)
        {
            if (storage == null || !IsSupportedContainer(storage))
            {
                return;
            }
            if (storage.size != baseGrids)
            {
                storage.SetSize(baseGrids);
            }
            int points = QualityData.GetBuildingInc(planetId, entityId);
            int extra = QualityMath.StorageExtraGrids(points, QualityConfig.StorageGridsPerPointClamped,
                QualityConfig.StorageGridsMaxClamped, baseGrids);
            if (extra > 0 && storage.size < baseGrids + extra)
            {
                storage.SetSize(baseGrids + extra);
            }
            if (storage.size > baseGrids)
            {
                expanded.Add(Key(planetId, entityId));
            }
            if (cells == null || cells.Count == 0)
            {
                return;
            }
            StorageComponent.GRID[] grids = storage.grids;
            int skipped = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                OverflowCell cell = cells[i];
                if (cell.grid < 0 || cell.grid >= storage.size || cell.grid >= grids.Length)
                {
                    skipped++;
                    continue;
                }
                grids[cell.grid].itemId = cell.itemId;
                grids[cell.grid].filter = cell.filter;
                grids[cell.grid].count = cell.count;
                grids[cell.grid].stackSize = cell.stackSize;
                grids[cell.grid].inc = cell.inc;
            }
            storage.NotifyStorageChange();
            if (skipped > 0)
            {
                QualityEnv.WarnOnce("storage.load.cells",
                    "[QSystem][storage] " + skipped + " overflow cell(s) did not fit after load"
                    + " (quality points dropped or cfg changed); p=" + planetId + " e=" + entityId);
            }
            QualityEnv.LogInfo("[QSystem][storage] restored p=" + planetId + " e=" + entityId
                + " base=" + baseGrids + " cells=" + (cells.Count - skipped));
        }

        /// <summary>One-line status for Diag\StartupAudit.</summary>
        internal static string Describe()
        {
            if (!armed)
            {
                return "storage grids: NOT ARMED (storageQualityGrids=false)";
            }
            if (!QualityConfig.StorageGridsEnabled)
            {
                return "storage grids: armed, feature off (collapsing " + expanded.Count + " containers)";
            }
            return "storage grids: armed, perPoint=" + QualityConfig.StorageGridsPerPointClamped.ToString("0.###")
                + " maxExtra=" + QualityConfig.StorageGridsMaxClamped
                + " expanded=" + expanded.Count;
        }
    }
}
