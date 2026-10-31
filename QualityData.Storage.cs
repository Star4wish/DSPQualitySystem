namespace DSPQualitySystem
{
    /// <summary>
    /// v0.13.0 side table for task-17 (storage quality grids). Kept in a separate partial file so
    /// the historical data layout stays untouched: the table lives in <see cref="PlanetQuality"/>
    /// next to the existing entity-indexed arrays and therefore grows and clears exactly like the
    /// rest of the per-planet state.
    ///
    /// v0.16.1: the old per-splitter "quality priority" flag was retired together with its patch;
    /// the splitter/sorter quality *filter* rules live in <c>QualityData.QualityFilters.cs</c>.
    /// </summary>
    public static partial class QualityData
    {
        /// <summary>
        /// v0.13.0 (C-1): optional observer invoked whenever a building's quality points change.
        /// The storage-grid rule uses it to re-shape the affected container without polling every
        /// entity of every planet. Always called on the thread that wrote the value; the observer
        /// must not assume the Unity main thread.
        /// </summary>
        public static System.Action<int, int> BuildingIncChanged;

        /// <summary>Vanilla grid count of a storage container (0 = unknown / not a container).</summary>
        public static int GetStorageBase(int planetId, int entityId)
        {
            PlanetQuality quality = Get(planetId);
            if (quality == null || entityId < 0 || entityId >= quality.storageBase.Length)
            {
                return 0;
            }
            return quality.storageBase[entityId];
        }

        public static void SetStorageBase(int planetId, int entityId, int baseGrids)
        {
            if (entityId < 0 || entityId > MaxEntityId)
            {
                return;
            }
            PlanetQuality quality = GetOrCreate(planetId);
            quality.EnsureCapacity(entityId + 1);
            quality.storageBase[entityId] = baseGrids < 0 ? 0 : baseGrids;
        }

        /// <summary>Forget the recorded vanilla size (container removed or rule disabled).</summary>
        public static void ClearStorageBase(int planetId, int entityId)
        {
            PlanetQuality quality = Get(planetId);
            if (quality == null || entityId < 0 || entityId >= quality.storageBase.Length)
            {
                return;
            }
            quality.storageBase[entityId] = 0;
        }

        /// <summary>True when at least one container of the planet has a recorded vanilla size.</summary>
        public static bool HasAnyStorageBase(int planetId)
        {
            PlanetQuality quality = Get(planetId);
            if (quality == null)
            {
                return false;
            }
            int[] bases = quality.storageBase;
            for (int i = 0; i < bases.Length; i++)
            {
                if (bases[i] > 0)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
