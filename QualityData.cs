using System.Collections.Generic;

namespace DSPQualitySystem
{
    /// <summary>
    /// Per-planet quality storage (spec section 2.1). Arrays are indexed by entityId and
    /// grow together with PlanetFactory.entityPool. Quality levels are 0..kIncLevelMax.
    /// </summary>
    public class PlanetQuality
    {
        public int planetId;

        /// <summary>Quality points baked into a placed building (0..10).</summary>
        public byte[] buildingInc = new byte[0];

        /// <summary>Accumulated quality points waiting to be attached to products.</summary>
        public int[] poolInc = new int[0];

        /// <summary>Number of products those pool points belong to.</summary>
        public int[] poolCount = new int[0];

        /// <summary>Quality of the material set currently loaded into a machine (0..10, -1 = unset).</summary>
        public short[] inputLevel = new short[0];

        /// <summary>Quality predicted for a batch that may start this tick (-1 = none).</summary>
        public short[] pendingLevel = new short[0];

        /// <summary>AssemblerComponent.replicating snapshot taken before the current tick (0/1).</summary>
        public byte[] wasReplicating = new byte[0];

        /// <summary>Snapshot of AssemblerComponent.produced, 4 slots per entity (entityId*4+slot).</summary>
        public int[] prevProduced = new int[0];

        /// <summary>
        /// v0.16.0: product items taken out of a machine since the previous assembler tick.
        /// PlanetFactory.PickFrom decrements AssemblerComponent.produced, so the net delta of that
        /// array is "produced - picked"; counting the picks separately restores the real production
        /// amount and keeps the quality pool from starving (see QualityMath.ProducedDelta).
        /// </summary>
        public int[] pickedSinceTick = new int[0];

        /// <summary>
        /// v0.13.0 (C-1): vanilla grid count of a storage container before the quality bonus was
        /// applied (0 = the container has never been seen by the storage-grid rule, or it is not a
        /// storage container at all). Never persisted as a structure field, only used in memory to
        /// restore the vanilla shape before the save writer and to recompute the bonus.
        /// </summary>
        public int[] storageBase = new int[0];

        public int Capacity
        {
            get { return buildingInc.Length; }
        }

        public void EnsureCapacity(int capacity)
        {
            if (capacity <= buildingInc.Length)
            {
                return;
            }

            int newCapacity = buildingInc.Length < 64 ? 64 : buildingInc.Length * 2;
            if (newCapacity < capacity)
            {
                newCapacity = capacity;
            }

            int previousLevelLength = inputLevel == null ? 0 : inputLevel.Length;
            buildingInc = Grow(buildingInc, newCapacity);
            poolInc = Grow(poolInc, newCapacity);
            poolCount = Grow(poolCount, newCapacity);
            inputLevel = Grow(inputLevel, newCapacity);
            // -1: "no batch observed yet"; 0 is a valid quality level.
            for (int i = previousLevelLength; i < inputLevel.Length; i++)
            {
                inputLevel[i] = -1;
            }

            int previousPendingLength = pendingLevel == null ? 0 : pendingLevel.Length;
            pendingLevel = Grow(pendingLevel, newCapacity);
            for (int i = previousPendingLength; i < pendingLevel.Length; i++)
            {
                pendingLevel[i] = -1;
            }
            wasReplicating = Grow(wasReplicating, newCapacity);
            storageBase = Grow(storageBase, newCapacity);
            pickedSinceTick = Grow(pickedSinceTick, newCapacity);

            int previousProducedLength = prevProduced == null ? 0 : prevProduced.Length;
            prevProduced = Grow(prevProduced, newCapacity * QualityData.ProducedSlots);
            // -1 means "not observed yet": the first postfix call records a baseline instead
            // of counting a save-loaded produced[] buffer as fresh production.
            for (int i = previousProducedLength; i < prevProduced.Length; i++)
            {
                prevProduced[i] = -1;
            }
        }

        private static T[] Grow<T>(T[] source, int length)
        {
            var target = new T[length];
            if (source != null && source.Length > 0)
            {
                System.Array.Copy(source, target, source.Length);
            }
            return target;
        }
    }

    /// <summary>
    /// Global registry of per-planet quality data. M0 keeps the data structures and the
    /// pool arithmetic; the patch sites that feed them arrive in M1/M2.
    /// </summary>
    public static partial class QualityData
    {
        public const int ProducedSlots = 4;

        /// <summary>
        /// v0.12.1 hardening: sanity ceiling for an entity id handed in by a component. A real
        /// factory entity pool stays far below this, but a dummy/pooled component can carry a
        /// sentinel id (e.g. int.MaxValue); without this guard <c>entityId + 1</c> overflows,
        /// EnsureCapacity silently returns and the array write throws IndexOutOfRangeException
        /// into the assembler hot path (caught, but the tick loses its quality bookkeeping).
        /// </summary>
        public const int MaxEntityId = 1 << 20;

        public static readonly Dictionary<int, PlanetQuality> planets = new Dictionary<int, PlanetQuality>();
        public static readonly Dictionary<int, byte> prebuildInc = new Dictionary<int, byte>();

        // Hot path cache: patches resolve the planet once per factory tick thousands of times.
        private static int _cachedPlanetId = int.MinValue;
        private static PlanetQuality _cached;

        private static void InvalidateCache()
        {
            _cachedPlanetId = int.MinValue;
            _cached = null;
        }

        public static PlanetQuality Get(int planetId)
        {
            if (_cachedPlanetId == planetId)
            {
                return _cached;
            }

            PlanetQuality quality;
            planets.TryGetValue(planetId, out quality);
            _cachedPlanetId = planetId;
            _cached = quality;
            return quality;
        }

        public static PlanetQuality GetOrCreate(int planetId)
        {
            PlanetQuality quality;
            if (planetId == _cachedPlanetId && _cached != null)
            {
                return _cached;
            }
            if (!planets.TryGetValue(planetId, out quality))
            {
                quality = new PlanetQuality();
                quality.planetId = planetId;
                planets[planetId] = quality;
            }
            _cachedPlanetId = planetId;
            _cached = quality;
            return quality;
        }

        public static void EnsureCapacity(PlanetQuality quality, int capacity)
        {
            if (quality == null)
            {
                return;
            }
            quality.EnsureCapacity(capacity);
        }

        public static void RemovePlanet(int planetId)
        {
            planets.Remove(planetId);
            // v0.16.1 (C-2 / C-3): the filter side tables live next to the planet data.
            ClearFiltersForPlanet(planetId);
            InvalidateCache();
        }

        public static void ClearAll()
        {
            planets.Clear();
            prebuildInc.Clear();
            ClearAllFilters();
            InvalidateCache();
        }

        public static byte GetBuildingInc(int planetId, int entityId)
        {
            PlanetQuality quality = Get(planetId);
            if (quality == null || entityId < 0 || entityId >= quality.buildingInc.Length)
            {
                return 0;
            }
            return quality.buildingInc[entityId];
        }

        public static void SetBuildingInc(int planetId, int entityId, byte inc)
        {
            if (entityId < 0 || entityId > MaxEntityId)
            {
                return;
            }
            PlanetQuality quality = GetOrCreate(planetId);
            quality.EnsureCapacity(entityId + 1);
            quality.buildingInc[entityId] = inc > Cargo.kIncLevelMax ? (byte)Cargo.kIncLevelMax : inc;
            // v0.13.0 (C-1): let the storage-grid rule re-shape a container whose points changed.
            System.Action<int, int> observer = BuildingIncChanged;
            if (observer != null)
            {
                observer(planetId, entityId);
            }
        }

        public static short GetInputLevel(int planetId, int entityId)
        {
            PlanetQuality quality = Get(planetId);
            if (quality == null || entityId < 0 || entityId >= quality.inputLevel.Length)
            {
                return -1;
            }
            return quality.inputLevel[entityId];
        }

        public static void SetInputLevel(int planetId, int entityId, short level)
        {
            if (entityId < 0 || entityId > MaxEntityId)
            {
                return;
            }
            PlanetQuality quality = GetOrCreate(planetId);
            quality.EnsureCapacity(entityId + 1);
            quality.inputLevel[entityId] = level;
        }

        /// <summary>Pool write: productCount items of quality inc contribute inc*productCount points.</summary>
        public static void AddPool(int planetId, int entityId, int incPoints, int productCount)
        {
            if (entityId < 0 || entityId > MaxEntityId || productCount <= 0)
            {
                return;
            }
            PlanetQuality quality = GetOrCreate(planetId);
            quality.EnsureCapacity(entityId + 1);
            int newInc;
            int newCount;
            QualityMath.AddPool(quality.poolInc[entityId], quality.poolCount[entityId], incPoints, productCount,
                out newInc, out newCount);
            quality.poolInc[entityId] = newInc;
            quality.poolCount[entityId] = newCount;
        }

        /// <summary>Pool read: hand out the average quality of the pool and consume one product.</summary>
        public static int TakeOneProductInc(int planetId, int entityId)
        {
            PlanetQuality quality = Get(planetId);
            if (quality == null || entityId < 0 || entityId >= quality.poolCount.Length)
            {
                return 0;
            }

            int newInc;
            int newCount;
            int perItem = QualityMath.TakeOne(quality.poolInc[entityId], quality.poolCount[entityId],
                Cargo.kIncLevelMax, out newInc, out newCount);
            quality.poolInc[entityId] = newInc;
            quality.poolCount[entityId] = newCount;
            return perItem;
        }

        public static int GetPoolInc(int planetId, int entityId)
        {
            PlanetQuality quality = Get(planetId);
            if (quality == null || entityId < 0 || entityId >= quality.poolInc.Length)
            {
                return 0;
            }
            return quality.poolInc[entityId];
        }

        public static int GetPoolCount(int planetId, int entityId)
        {
            PlanetQuality quality = Get(planetId);
            if (quality == null || entityId < 0 || entityId >= quality.poolCount.Length)
            {
                return 0;
            }
            return quality.poolCount[entityId];
        }

        /// <summary>Snapshot of AssemblerComponent.produced used by the M1 delta accounting.</summary>
        public static int GetPrevProduced(int planetId, int entityId, int slot)
        {
            PlanetQuality quality = Get(planetId);
            if (quality == null || entityId < 0 || slot < 0 || slot >= ProducedSlots)
            {
                return -1;
            }
            int index = entityId * ProducedSlots + slot;
            if (index >= quality.prevProduced.Length)
            {
                return -1;
            }
            return quality.prevProduced[index];
        }

        public static void SetPrevProduced(int planetId, int entityId, int slot, int value)
        {
            if (entityId < 0 || entityId > MaxEntityId || slot < 0 || slot >= ProducedSlots)
            {
                return;
            }
            PlanetQuality quality = GetOrCreate(planetId);
            quality.EnsureCapacity(entityId + 1);
            int index = entityId * ProducedSlots + slot;
            if (index < quality.prevProduced.Length)
            {
                quality.prevProduced[index] = value;
            }
        }

        /// <summary>
        /// v0.16.0: remember that one product item was just taken from this machine. The assembler
        /// postfix adds these back to the produced[] delta so production is counted even when picks
        /// and production happen between the same two ticks (the 增产 extra products are the case
        /// where that used to zero out the quality pool).
        /// </summary>
        public static void NoteProductPicked(int planetId, int entityId)
        {
            if (entityId < 0 || entityId > MaxEntityId)
            {
                return;
            }
            PlanetQuality quality = GetOrCreate(planetId);
            quality.EnsureCapacity(entityId + 1);
            if (entityId < quality.pickedSinceTick.Length && quality.pickedSinceTick[entityId] < int.MaxValue)
            {
                quality.pickedSinceTick[entityId]++;
            }
        }

        /// <summary>Reads and clears the pick counter for one machine (called once per tick).</summary>
        public static int ConsumeProductPicked(int planetId, int entityId)
        {
            PlanetQuality quality = Get(planetId);
            if (quality == null || entityId < 0 || entityId >= quality.pickedSinceTick.Length)
            {
                return 0;
            }
            int picks = quality.pickedSinceTick[entityId];
            quality.pickedSinceTick[entityId] = 0;
            return picks;
        }

        public static short GetPendingLevel(int planetId, int entityId)
        {
            PlanetQuality quality = Get(planetId);
            if (quality == null || entityId < 0 || entityId >= quality.pendingLevel.Length)
            {
                return -1;
            }
            return quality.pendingLevel[entityId];
        }

        public static void SetPendingLevel(int planetId, int entityId, short level)
        {
            if (entityId < 0 || entityId > MaxEntityId)
            {
                return;
            }
            PlanetQuality quality = GetOrCreate(planetId);
            quality.EnsureCapacity(entityId + 1);
            quality.pendingLevel[entityId] = level;
        }

        public static byte GetWasReplicating(int planetId, int entityId)
        {
            PlanetQuality quality = Get(planetId);
            if (quality == null || entityId < 0 || entityId >= quality.wasReplicating.Length)
            {
                return 0;
            }
            return quality.wasReplicating[entityId];
        }

        public static void SetWasReplicating(int planetId, int entityId, byte value)
        {
            if (entityId < 0 || entityId > MaxEntityId)
            {
                return;
            }
            PlanetQuality quality = GetOrCreate(planetId);
            quality.EnsureCapacity(entityId + 1);
            quality.wasReplicating[entityId] = value;
        }

        /// <summary>
        /// M2: remember the quality points of the hand item that paid for prebuildId. The value
        /// survives until PlanetFactory.AddEntityDataWithComponents turns the prebuild into an
        /// entity (or until the player cancels the prebuild).
        /// </summary>
        public static void SetPrebuildInc(int prebuildId, int perItem)
        {
            if (prebuildId <= 0)
            {
                return;
            }
            if (perItem <= 0)
            {
                prebuildInc.Remove(prebuildId);
                return;
            }
            if (perItem > Cargo.kIncLevelMax)
            {
                perItem = Cargo.kIncLevelMax;
            }
            prebuildInc[prebuildId] = (byte)perItem;
        }

        /// <summary>M2: read and consume the pending points of prebuildId (0 when absent).</summary>
        public static int TakePrebuildInc(int prebuildId)
        {
            if (prebuildId <= 0)
            {
                return 0;
            }
            byte inc;
            if (!prebuildInc.TryGetValue(prebuildId, out inc))
            {
                return 0;
            }
            prebuildInc.Remove(prebuildId);
            return inc;
        }

        /// <summary>M2: drop pending points when a prebuild is removed before it becomes an entity.</summary>
        public static void ClearPrebuild(int prebuildId)
        {
            if (prebuildId > 0)
            {
                prebuildInc.Remove(prebuildId);
            }
        }

        /// <summary>Save/load: grow the arrays of a planet to hold at least capacity entities.</summary>
        public static void EnsureCapacity(int planetId, int capacity)
        {
            if (capacity <= 0)
            {
                return;
            }
            PlanetQuality quality = GetOrCreate(planetId);
            quality.EnsureCapacity(capacity);
        }

        /// <summary>Save/load: restore an exact pool snapshot (spec section 4.2).</summary>
        public static void SetPool(int planetId, int entityId, int poolInc, int poolCount)
        {
            if (entityId < 0 || entityId > MaxEntityId)
            {
                return;
            }
            PlanetQuality quality = GetOrCreate(planetId);
            quality.EnsureCapacity(entityId + 1);
            quality.poolInc[entityId] = poolInc < 0 ? 0 : poolInc;
            quality.poolCount[entityId] = poolCount < 0 ? 0 : poolCount;
        }

        /// <summary>
        /// R-3 hook called from the MinerComponent.InternalUpdate transpiler: quality points
        /// written on one mined item (v0.9.0 multi-roll: 0..floor(buildingInc/2) points).
        /// Never throws into the miner hot path.
        /// </summary>
        public static int MinerOutputInc(PlanetFactory factory, int entityId)
        {
            try
            {
                if (!QualityConfig.Enabled.Value || factory == null || factory.planet == null)
                {
                    return 0;
                }
                int level = QualityRules.RollMinerQuality(factory.planet.id, entityId);
                Diag.Probes.RecordMinerCall(entityId, level);
                if (level > 0)
                {
                    Diag.Probes.RecordMinerHit(entityId, level);
                }
                return level;
            }
            catch (System.Exception e)
            {
                QualityEnv.WarnOnce("miner.roll", e);
                return 0;
            }
        }


        /// <summary>
        /// v0.16.0 (Bug 1 "big miner loses quality"): R-3 hook for the station-type vein collector
        /// (large mining machine, item 2316). Its owned miner keeps `insertTarget == 0` because
        /// `PlanetFactory.CreateEntityLogicComponents` only calls `SetMinerInsertTarget` when the
        /// connected entity is not a station (asm_full.cs:208341-208346), so the mined items never
        /// pass through `factory.InsertInto` and the P0_Miner transpiler never runs for them.
        /// `StationComponent.UpdateVeinCollection` drains `minerPool[minerId].productCount` into
        /// `station.storage[0]` instead (asm_full.cs:77689-77722). Return the per-item quality level
        /// for one such transfer; the caller multiplies it by the transferred count. Never throws
        /// into the station tick.
        /// </summary>
        public static int MinerCollectorIncLevel(PlanetFactory factory, int entityId, int transferCount)
        {
            try
            {
                if (!QualityConfig.Enabled.Value || factory == null || factory.planet == null
                    || entityId <= 0 || transferCount <= 0)
                {
                    return 0;
                }
                int level = QualityRules.RollMinerQuality(factory.planet.id, entityId);
                Diag.Probes.RecordMinerCall(entityId, level);
                if (level > 0)
                {
                    Diag.Probes.RecordMinerHit(entityId, level);
                }
                return level;
            }
            catch (System.Exception e)
            {
                QualityEnv.WarnOnce("miner.collector.roll", e);
                return 0;
            }
        }


        public static void ClearEntity(int planetId, int entityId)
        {
            PlanetQuality quality = Get(planetId);
            if (quality == null || entityId < 0)
            {
                return;
            }
            if (entityId < quality.inputLevel.Length)
            {
                quality.inputLevel[entityId] = -1;
            }
            if (entityId < quality.pendingLevel.Length)
            {
                quality.pendingLevel[entityId] = -1;
            }
            if (entityId < quality.wasReplicating.Length)
            {
                quality.wasReplicating[entityId] = 0;
            }
            if (entityId < quality.buildingInc.Length)
            {
                quality.buildingInc[entityId] = 0;
            }
            if (entityId < quality.storageBase.Length)
            {
                quality.storageBase[entityId] = 0;
            }
            // v0.16.1 (C-2 / C-3): drop this entity's splitter output rules / sorter threshold.
            ClearFilterEntity(planetId, entityId);
            if (entityId < quality.poolCount.Length)
            {
                quality.poolInc[entityId] = 0;
                quality.poolCount[entityId] = 0;
            }
            if (entityId < quality.pickedSinceTick.Length)
            {
                quality.pickedSinceTick[entityId] = 0;
            }
            if (entityId * ProducedSlots < quality.prevProduced.Length)
            {
                for (int slot = 0; slot < ProducedSlots; slot++)
                {
                    // -1: next owner of this entityId starts a fresh baseline.
                    quality.prevProduced[entityId * ProducedSlots + slot] = -1;
                }
            }
        }
    }
}