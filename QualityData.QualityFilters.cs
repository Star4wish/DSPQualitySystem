using System;
using System.Collections.Generic;

namespace DSPQualitySystem
{
    /// <summary>
    /// v0.16.0 (C-2 + C-3) side tables: per-output-port quality filter rules for four-way
    /// splitters and the quality threshold of sorters.
    ///
    /// WHY THESE TABLES ARE NOT IN PlanetQuality (yet): task delivery is split in phases (P1 is a
    /// pure "new files only" drop while another agent edits the shared tree), so the two tables
    /// live in their own per-planet container and are grown/cleared through the hooks below. The
    /// public accessor surface is the one the patch code will call, so nothing outside this file
    /// needs to change if the tables later move into PlanetQuality.
    ///
    /// STATE MODEL
    ///   splitter: one packed int per entity per physical port (entityId * 4 + port). Packed as
    ///             itemId (bits 0..15, 0 = any item) | minimum points (bits 16..23), 0 = no rule.
    ///   sorter  : one byte per entity, the minimum quality points a cargo must reach before the
    ///             sorter may pick it (0 = the sorter behaves exactly like vanilla). The item is
    ///             NOT duplicated here: the vanilla InserterComponent.filter already stores the
    ///             item id, is persisted by the game and is displayed by the vanilla UI.
    ///
    /// THREADING
    ///   Writes (SetSplitterRule / SetSorterMin / Clear*) are expected from the Unity main thread
    ///   (UI clicks, save loading). Reads from the async splitter path go through
    ///   FiltersForRead(planetId), which is cheap and lock free:
    ///     - a single int (filterRuleTotal) short-circuits every call while no rule exists;
    ///     - the per-thread cache is validated by a global epoch, so a main-thread write is picked
    ///       up on the next call without touching the dictionary.
    ///   This is the same lock-free read pattern the existing QualityData.Get hot path uses; rule
    ///   writes are single int/byte stores, so a worker either sees the old or the new value.
    ///
    /// PERSISTENCE
    ///   Nothing here is serialized by itself. The save writer emits only the non-empty entries as
    ///   the per-planet JSON nodes "splittersV2" and "sorters" (see the design doc, section 2.3).
    ///   A v0.15 save has none of those nodes, and its legacy "splitters" boolean node is ignored,
    ///   so both directions stay compatible. SchemaVersion stays 1.
    ///
    /// INTEGRATION INTO THE SHARED TREE (P2) - exactly these edits inside QualityData.cs:
    ///   1. REQUIRED  ClearAll()                  (QualityData.cs:185) : call ClearAllFilters();
    ///   2. REQUIRED  RemovePlanet(int planetId)  (QualityData.cs:179) : call ClearFiltersForPlanet(planetId);
    ///   3. REQUIRED  ClearEntity(int,int)        (QualityData.cs:505) : call ClearFilterEntity(planetId, entityId);
    ///   4. OPTIONAL  EnsureCapacity(PlanetQuality,int) (QualityData.cs:170): only if the Lead
    ///      prefers the historical layout, move splitterRules/sorterMin into PlanetQuality here and
    ///      drop the dictionary. Correctness does not depend on it; the accessor names stay the same.
    /// </summary>
    public static partial class QualityData
    {
        /// <summary>Entity id guard, identical to QualityData.MaxEntityId (duplicated so this file
        /// stays self contained for the isolated P1 compile check).</summary>
        internal const int FilterMaxEntityId = 1 << 20;

        /// <summary>Per-planet rule storage. Grows on demand; entity ids are dense and small.</summary>
        internal sealed class PlanetFilterTable
        {
            /// <summary>Packed splitter rules, entityId * 4 + port.</summary>
            internal int[] splitterRules = new int[0];

            /// <summary>Sorter minimum quality points, one byte per entity.</summary>
            internal byte[] sorterMin = new byte[0];

            internal int splitterRuleCount;
            internal int sorterRuleCount;
            internal int capacity;

            internal void EnsureCapacity(int entityId)
            {
                int target = entityId + 1;
                if (target <= capacity)
                {
                    return;
                }

                int next = capacity < 64 ? 64 : capacity;
                while (next < target)
                {
                    next *= 2;
                }

                if (next > FilterMaxEntityId)
                {
                    next = FilterMaxEntityId;
                }

                if (next <= capacity)
                {
                    return;
                }

                int[] rules = new int[next * QualityMath.SplitterPortCount];
                byte[] mins = new byte[next];
                if (splitterRules.Length > 0)
                {
                    Array.Copy(splitterRules, rules, splitterRules.Length);
                }

                if (sorterMin.Length > 0)
                {
                    Array.Copy(sorterMin, mins, sorterMin.Length);
                }

                splitterRules = rules;
                sorterMin = mins;
                capacity = next;
            }

            /// <summary>Packed rule of one physical port, 0 when absent/out of range.</summary>
            internal int GetSplitterRule(int entityId, int port)
            {
                int index = entityId * QualityMath.SplitterPortCount + port;
                if (entityId < 0 || port < 0 || port >= QualityMath.SplitterPortCount || index >= splitterRules.Length)
                {
                    return QualityMath.SplitterRuleNone;
                }

                return splitterRules[index];
            }

            /// <summary>True when any of the four ports of this splitter carries a rule.</summary>
            internal bool HasAnySplitterRule(int entityId)
            {
                if (entityId < 0)
                {
                    return false;
                }

                int index = entityId * QualityMath.SplitterPortCount;
                if (index + QualityMath.SplitterPortCount > splitterRules.Length)
                {
                    return false;
                }

                for (int port = 0; port < QualityMath.SplitterPortCount; port++)
                {
                    if (splitterRules[index + port] != QualityMath.SplitterRuleNone)
                    {
                        return true;
                    }
                }

                return false;
            }

            /// <summary>Sorter minimum quality points of one sorter, 0 when absent/out of range.</summary>
            internal int GetSorterMin(int entityId)
            {
                if (entityId < 0 || entityId >= sorterMin.Length)
                {
                    return QualityMath.SorterMinNone;
                }

                return sorterMin[entityId];
            }

            internal int EntityCapacity
            {
                get { return capacity; }
            }
        }

        private static readonly Dictionary<int, PlanetFilterTable> filterTables = new Dictionary<int, PlanetFilterTable>();

        /// <summary>Total number of live rules (splitter ports + sorters) across all planets.</summary>
        private static int filterRuleTotal;

        /// <summary>Mutation counter used to invalidate the per-thread read cache.</summary>
        private static int filterEpoch;

        [ThreadStatic] private static PlanetFilterTable cachedFilterTable;
        [ThreadStatic] private static int cachedFilterPlanetId;
        [ThreadStatic] private static int cachedFilterEpoch;

        // ------------------------------------------------------------------ read path (hot)

        /// <summary>
        /// Rule table for read-only use from any thread (main thread UI and the async splitter
        /// tick). Returns null while no rule exists anywhere or when this planet has none.
        /// </summary>
        internal static PlanetFilterTable FiltersForRead(int planetId)
        {
            if (filterRuleTotal <= 0)
            {
                return null;
            }

            if (cachedFilterTable != null && cachedFilterPlanetId == planetId && cachedFilterEpoch == filterEpoch)
            {
                return cachedFilterTable;
            }

            PlanetFilterTable table;
            filterTables.TryGetValue(planetId, out table);

            cachedFilterPlanetId = planetId;
            cachedFilterTable = table;
            cachedFilterEpoch = filterEpoch;
            return table;
        }

        /// <summary>True when at least one splitter port or sorter rule exists anywhere.</summary>
        internal static bool HasAnyFilterRules
        {
            get { return filterRuleTotal > 0; }
        }

        /// <summary>Total number of live rules, for diagnostics.</summary>
        internal static int TotalFilterRuleCount
        {
            get { return filterRuleTotal; }
        }

        // ------------------------------------------------------------------ splitter rules

        /// <summary>Packed rule of a splitter port, 0 when the port has no rule.</summary>
        internal static int GetSplitterRule(int planetId, int entityId, int port)
        {
            PlanetFilterTable table;
            if (!filterTables.TryGetValue(planetId, out table) || table == null)
            {
                return QualityMath.SplitterRuleNone;
            }

            return table.GetSplitterRule(entityId, port);
        }

        /// <summary>True when this splitter has at least one port rule.</summary>
        internal static bool EntityHasSplitterRule(int planetId, int entityId)
        {
            PlanetFilterTable table;
            if (!filterTables.TryGetValue(planetId, out table) || table == null)
            {
                return false;
            }

            return table.HasAnySplitterRule(entityId);
        }

        /// <summary>Number of splitter port rules on a planet (0 stays on the fast path).</summary>
        internal static int SplitterRuleCount(int planetId)
        {
            PlanetFilterTable table;
            if (!filterTables.TryGetValue(planetId, out table) || table == null)
            {
                return 0;
            }

            return table.splitterRuleCount;
        }

        /// <summary>
        /// Write one splitter port rule (packed value; 0 clears the port). Main thread only.
        /// Values are sanitized here as the last line of defense: an out-of-range entity id is
        /// ignored, and the packed value is rebuilt so a caller cannot smuggle in garbage bits.
        /// </summary>
        internal static void SetSplitterRule(int planetId, int entityId, int port, int packed)
        {
            if (entityId < 0 || entityId >= FilterMaxEntityId)
            {
                return;
            }

            if (port < 0 || port >= QualityMath.SplitterPortCount)
            {
                return;
            }

            int sanitized = QualityMath.PackSplitterRule(
                QualityMath.SplitterRuleItem(packed), QualityMath.SplitterRuleMin(packed));

            PlanetFilterTable table = GetOrCreateFilterTable(planetId);
            table.EnsureCapacity(entityId);
            int index = entityId * QualityMath.SplitterPortCount + port;
            int old = table.splitterRules[index];
            if (old == sanitized)
            {
                return;
            }

            table.splitterRules[index] = sanitized;
            if (old == QualityMath.SplitterRuleNone && sanitized != QualityMath.SplitterRuleNone)
            {
                table.splitterRuleCount++;
                filterRuleTotal++;
            }
            else if (old != QualityMath.SplitterRuleNone && sanitized == QualityMath.SplitterRuleNone)
            {
                table.splitterRuleCount--;
                filterRuleTotal--;
            }

            TouchFilters();
            DropFilterTableIfEmpty(planetId, table);
        }

        /// <summary>Clear all four port rules of one splitter. Main thread only.</summary>
        internal static void ClearSplitterRules(int planetId, int entityId)
        {
            PlanetFilterTable table;
            if (!filterTables.TryGetValue(planetId, out table) || table == null)
            {
                return;
            }

            if (entityId < 0 || (entityId * QualityMath.SplitterPortCount + QualityMath.SplitterPortCount) > table.splitterRules.Length)
            {
                return;
            }

            int index = entityId * QualityMath.SplitterPortCount;
            for (int port = 0; port < QualityMath.SplitterPortCount; port++)
            {
                if (table.splitterRules[index + port] != QualityMath.SplitterRuleNone)
                {
                    table.splitterRules[index + port] = QualityMath.SplitterRuleNone;
                    table.splitterRuleCount--;
                    filterRuleTotal--;
                }
            }

            TouchFilters();
            DropFilterTableIfEmpty(planetId, table);
        }

        // ------------------------------------------------------------------ sorter rules

        /// <summary>Minimum quality points a sorter accepts (0 = vanilla behaviour).</summary>
        internal static int GetSorterMin(int planetId, int entityId)
        {
            PlanetFilterTable table;
            if (!filterTables.TryGetValue(planetId, out table) || table == null)
            {
                return QualityMath.SorterMinNone;
            }

            return table.GetSorterMin(entityId);
        }

        /// <summary>Number of sorters with a quality threshold on a planet.</summary>
        internal static int SorterRuleCount(int planetId)
        {
            PlanetFilterTable table;
            if (!filterTables.TryGetValue(planetId, out table) || table == null)
            {
                return 0;
            }

            return table.sorterRuleCount;
        }

        /// <summary>Write the sorter quality threshold (0 clears it). Main thread only.</summary>
        internal static void SetSorterMin(int planetId, int entityId, int minPoints)
        {
            if (entityId < 0 || entityId >= FilterMaxEntityId)
            {
                return;
            }

            int sanitized = QualityMath.ClampQualityPoints(minPoints);

            PlanetFilterTable table = GetOrCreateFilterTable(planetId);
            table.EnsureCapacity(entityId);
            int old = table.sorterMin[entityId];
            if (old == sanitized)
            {
                return;
            }

            table.sorterMin[entityId] = (byte)sanitized;
            if (old == QualityMath.SorterMinNone && sanitized != QualityMath.SorterMinNone)
            {
                table.sorterRuleCount++;
                filterRuleTotal++;
            }
            else if (old != QualityMath.SorterMinNone && sanitized == QualityMath.SorterMinNone)
            {
                table.sorterRuleCount--;
                filterRuleTotal--;
            }

            TouchFilters();
            DropFilterTableIfEmpty(planetId, table);
        }

        /// <summary>Clear the sorter quality threshold of one sorter. Main thread only.</summary>
        internal static void ClearSorterMin(int planetId, int entityId)
        {
            SetSorterMin(planetId, entityId, QualityMath.SorterMinNone);
        }

        // ------------------------------------------------------------------ lifecycle hooks

        /// <summary>
        /// Drop every rule of one entity. Called from QualityData.ClearEntity so a dismantled
        /// splitter/sorter does not leak a rule into a future entity that reuses the pool id.
        /// </summary>
        internal static void ClearFilterEntity(int planetId, int entityId)
        {
            ClearSplitterRules(planetId, entityId);
            ClearSorterMin(planetId, entityId);
        }

        /// <summary>Drop every rule of one planet. Called from QualityData.RemovePlanet.</summary>
        internal static void ClearFiltersForPlanet(int planetId)
        {
            PlanetFilterTable table;
            if (!filterTables.TryGetValue(planetId, out table) || table == null)
            {
                return;
            }

            filterRuleTotal -= table.splitterRuleCount;
            filterRuleTotal -= table.sorterRuleCount;
            if (filterRuleTotal < 0)
            {
                filterRuleTotal = 0;
            }

            filterTables.Remove(planetId);
            TouchFilters();
            if (cachedFilterPlanetId == planetId)
            {
                cachedFilterTable = null;
            }
        }

        /// <summary>Drop every rule of every planet. Called from QualityData.ClearAll (new game).</summary>
        internal static void ClearAllFilters()
        {
            filterTables.Clear();
            filterRuleTotal = 0;
            cachedFilterTable = null;
            cachedFilterPlanetId = 0;
            TouchFilters();
        }

        // ------------------------------------------------------------------ save writer support

        /// <summary>Rule table of one planet for the save writer, or null (main thread).</summary>
        internal static PlanetFilterTable FiltersForSave(int planetId)
        {
            PlanetFilterTable table;
            filterTables.TryGetValue(planetId, out table);
            return table;
        }

        /// <summary>
        /// Planet ids that carry at least one rule, so the save writer can emit planets that have
        /// filter rules but no other quality state. Main thread only.
        /// </summary>
        internal static List<int> PlanetIdsWithRules()
        {
            List<int> ids = new List<int>();
            foreach (KeyValuePair<int, PlanetFilterTable> pair in filterTables)
            {
                PlanetFilterTable table = pair.Value;
                if (table != null && (table.splitterRuleCount > 0 || table.sorterRuleCount > 0))
                {
                    ids.Add(pair.Key);
                }
            }

            ids.Sort();
            return ids;
        }

        // ------------------------------------------------------------------ internals

        private static PlanetFilterTable GetOrCreateFilterTable(int planetId)
        {
            PlanetFilterTable table;
            if (filterTables.TryGetValue(planetId, out table) && table != null)
            {
                return table;
            }

            table = new PlanetFilterTable();
            filterTables[planetId] = table;
            TouchFilters();
            return table;
        }

        /// <summary>Bump the cache epoch so worker-thread readers re-validate immediately.</summary>
        private static void TouchFilters()
        {
            filterEpoch++;
        }

        /// <summary>Release an empty table so a cleared planet costs no memory.</summary>
        private static void DropFilterTableIfEmpty(int planetId, PlanetFilterTable table)
        {
            if (table.splitterRuleCount == 0 && table.sorterRuleCount == 0)
            {
                filterTables.Remove(planetId);
                TouchFilters();
            }
        }
    }
}
