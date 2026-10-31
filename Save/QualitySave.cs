using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;

namespace DSPQualitySystem.Save
{
    /// <summary>
    /// Quality persistence.
    /// Since v0.11.11 the data travels INSIDE the player's own .dsv: a trailer
    /// [int32 length][UTF-8 JSON][8 B magic] is appended at the end of GameData.Export and read back
    /// at the end of GameData.Import (see <see cref="QualityBlobCodec"/>). The vanilla loader stops
    /// reading where the vanilla data ends, so the trailer is invisible to it - deleting the mod
    /// leaves a perfectly loadable save, and copying/backing up/rotating a save carries the quality
    /// data with it. No sidecar files are written anymore.
    /// The old <c>BepInEx\config\DSPQualitySystem\saves\&lt;slot&gt;.json</c> files are still read once
    /// (so existing saves keep their points) and deleted as soon as the .dsv itself carries a
    /// trailer. Data is always loaded/saved even when quality.enabled is false so that toggling the
    /// mod does not destroy quality points; gameplay patches stay behind the Enabled check.
    /// Only non-zero entries are written (spec 4.2).
    /// </summary>
    public static class QualitySave
    {
        public const int SchemaVersion = SaveGuard.SchemaVersion;

        private const string ModDirectory = "DSPQualitySystem";
        private const string LegacyExtension = ".json";
        private const string AutoSavePrefix = "_autosave";
        private const string AutoSaveTmpName = "_autosave_tmp";

        /// <summary>v0.16.1: one-time notice when a pre-v0.16.1 splitter flag node is ignored.</summary>
        private static bool _legacySplitterNodeLogged;

        /// <summary>
        /// Where v0.11.10 and older kept the sidecar JSON. Read-only since v0.11.11: it is the source
        /// of the one-time migration and the target of the cleanup, never a write target again.
        /// </summary>
        public static string LegacySaveDirectory
        {
            get { return Path.Combine(Paths.ConfigPath, ModDirectory, "saves"); }
        }

        /// <summary>
        /// True when the save that is being loaded carried a valid embedded trailer. The load hook
        /// uses it to decide between "the .dsv is authoritative, drop the sidecar" and "old save, use
        /// the sidecar once".
        /// </summary>
        public static bool EmbeddedLoaded { get; private set; }

        /// <summary>
        /// True when the last export really appended a trailer. The save hook only cleans up the
        /// legacy file in that case, otherwise the .dsv would not have become self-contained.
        /// </summary>
        public static bool LastEmbeddedWriteOk { get; private set; }

        /// <summary>
        /// Clear the per save/load-cycle flags. Called at the start of both embedded entry points so a
        /// failed export/import can never leak "we already have data" into the next cycle.
        /// </summary>
        public static void ResetCycleFlags()
        {
            EmbeddedLoaded = false;
            LastEmbeddedWriteOk = false;
        }

        /// <summary>Legacy sidecar path for a save slot (migration source / cleanup target only).</summary>
        public static string GetLegacyPath(string saveName)
        {
            string safe = SanitizeFileName(saveName);
            if (safe.Length == 0)
            {
                safe = "unknown_save";
            }
            return Path.Combine(LegacySaveDirectory, safe + LegacyExtension);
        }

        /// <summary>
        /// Append the current in-memory quality data to the save stream. Called from the
        /// GameData.Export postfix, i.e. before the vanilla code back-fills the file size, which is
        /// what keeps the save self-consistent for the vanilla loader.
        /// Returns the number of planets written (0 when nothing could be written).
        /// </summary>
        public static int SaveEmbedded(GameData data, BinaryWriter writer)
        {
            ResetCycleFlags();
            if (data == null || writer == null)
            {
                return 0;
            }

            try
            {
                int planetCount;
                int buildingCount;
                int poolCount;
                int storageCount;
                int splitterCount;
                int sorterCount;
                string json = BuildDocument(data, out planetCount, out buildingCount, out poolCount,
                    out storageCount, out splitterCount, out sorterCount);
                byte[] payload = Encoding.UTF8.GetBytes(json);

                // Always write, even when nothing is recorded: the empty trailer is the "this save
                // has been saved by the mod" marker that stops us from re-reading a stale sidecar
                // later. It costs 12 bytes + a ~40 byte header.
                QualityBlobCodec.Append(writer, payload);
                LastEmbeddedWriteOk = true;

                if (Plugin.Log != null)
                {
                    Plugin.Log.LogInfo("[QSystem][save] embedded quality trailer written ('" + SafeGameName(data)
                        + "'): planets=" + planetCount + " buildings=" + buildingCount + " pools=" + poolCount
                        + " storages=" + storageCount + " splitters=" + splitterCount + " sorters=" + sorterCount
                        + " bytes=" + payload.Length);
                }
                return planetCount;
            }
            catch (Exception e)
            {
                // Never let the mod break a vanilla save: worst case the save simply has no trailer.
                QualityEnv.WarnOnce("save.write", e);
                return 0;
            }
        }

        /// <summary>
        /// Replace the in-memory quality data with the trailer found in the save stream. Called from
        /// the GameData.Import postfix, i.e. after the vanilla data has been restored.
        /// Returns the number of applied entries, 0 when a trailer existed but was unusable, and -1
        /// when the save has no trailer at all (an old save: the caller may then migrate the sidecar).
        /// </summary>
        public static int ReadEmbedded(GameData data, BinaryReader reader)
        {
            ResetCycleFlags();
            QualityData.ClearAll();
            if (data == null || reader == null)
            {
                return -1;
            }

            try
            {
                byte[] payload;
                string reason;
                if (!QualityBlobCodec.TryRead(reader, out payload, out reason))
                {
                    if (Plugin.Log != null)
                    {
                        Plugin.Log.LogInfo("[QSystem][save] no embedded quality trailer ('" + SafeGameName(data)
                            + "'): " + reason + ", vanilla save");
                    }
                    return -1;
                }

                var root = JsonLite.Parse(Encoding.UTF8.GetString(payload)) as Dictionary<string, object>;
                if (root == null)
                {
                    WarnEmbeddedIgnored(data, "root is not an object");
                    return 0;
                }

                // The trailer is inside the same file as the game name and seed, so this check is not
                // about cross-save contamination (a copied .dsv carries both halves) but about a
                // damaged/hand-edited save: applying points to the wrong galaxy would corrupt it.
                string guardError = SaveGuard.ValidateDocument(root, SafeGameName(data), SeedOf(data));
                if (guardError != null)
                {
                    WarnEmbeddedIgnored(data, guardError);
                    return 0;
                }

                int planetCount;
                int buildingCount;
                int poolCount;
                int storageCount;
                int splitterCount;
                int sorterCount;
                int applied = ApplyDocument(root, data, out planetCount, out buildingCount, out poolCount,
                    out storageCount, out splitterCount, out sorterCount);
                if (applied < 0)
                {
                    WarnEmbeddedIgnored(data, "missing planets object");
                    return 0;
                }

                EmbeddedLoaded = true;
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogInfo("[QSystem][save] embedded quality trailer used ('" + SafeGameName(data)
                        + "'): planets=" + planetCount + " entries=" + applied
                        + " buildings=" + buildingCount + " pools=" + poolCount
                        + " storages=" + storageCount + " splitters=" + splitterCount + " sorters=" + sorterCount
                        + " bytes=" + payload.Length);
                }
                return applied;
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("save.read", e);
                return 0;
            }
        }

        /// <summary>
        /// Read the pre-v0.11.11 sidecar JSON for this slot (one-time migration for saves that were
        /// written by an older build). Logs the candidates it tried, because autosaves were rotated
        /// on disk while their JSON kept the original slot name.
        /// </summary>
        public static int LoadLegacy(string saveName)
        {
            QualityData.ClearAll();

            string primary = GetLegacyPath(saveName);
            var candidates = new List<string>();
            candidates.Add(primary);
            if (IsAutoSaveName(saveName))
            {
                // The game rotates _autosave_tmp.dsv into _autosave_0..3, but the old scheme had
                // already written the JSON under the name that existed at save time, so an autosave
                // slot may only have a _autosave_tmp.json to its name.
                string fallback = GetLegacyPath(AutoSaveTmpName);
                if (!string.Equals(fallback, primary, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(fallback);
                }
            }

            string path = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (File.Exists(candidates[i]))
                {
                    path = candidates[i];
                    break;
                }
            }

            if (path == null)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogInfo("[QSystem][save] no quality data for '" + saveName + "' ("
                        + string.Join(", ", candidates.ToArray()) + "), starting fresh");
                }
                return 0;
            }

            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                var root = JsonLite.Parse(text) as Dictionary<string, object>;
                if (root == null)
                {
                    Warn(saveName, "root is not an object", path);
                    return 0;
                }

                // Guard against cross-save contamination: the file is keyed by slot name, but a
                // renamed/copied .dsv (or a new game reusing a slot) can still point at another
                // save's quality data. Applying foreign points would silently corrupt the save,
                // so a mismatch discards the file instead (points are harmless to lose).
                // The expected name follows the file we actually opened: a _autosave_N slot served by
                // _autosave_tmp.json holds the internal name, not the rotated slot name.
                string expectedName = string.Equals(path, primary, StringComparison.OrdinalIgnoreCase)
                    ? GetExpectedGameName(saveName)
                    : GetExpectedGameName(AutoSaveTmpName);
                string guardError = SaveGuard.ValidateDocument(root, expectedName, CurrentSeed());
                if (guardError != null)
                {
                    if (Plugin.Log != null)
                    {
                        Plugin.Log.LogWarning("[QSystem][save] " + guardError + " (slot='" + saveName + "', "
                            + path + ") -> ignored, starting fresh");
                    }
                    return 0;
                }

                int planetCount;
                int buildingCount;
                int poolCount;
                int storageCount;
                int splitterCount;
                int sorterCount;
                int applied = ApplyDocument(root, CurrentData(), out planetCount, out buildingCount,
                    out poolCount, out storageCount, out splitterCount, out sorterCount);
                if (applied < 0)
                {
                    Warn(saveName, "missing planets object", path);
                    return 0;
                }

                if (Plugin.Log != null)
                {
                    Plugin.Log.LogInfo("[QSystem][save] loaded legacy sidecar '" + path + "': entries=" + applied);
                    Plugin.Log.LogInfo("[QSystem][save] legacy sidecar used for '" + saveName + "' (" + path
                        + "): migrated in memory, this .dsv gets its own trailer on the next save");
                }
                return applied;
            }
            catch (Exception e)
            {
                Warn(saveName, e.Message, path);
                return 0;
            }
        }

        /// <summary>
        /// Best-effort removal of the legacy sidecar of a slot, now that the .dsv carries the data.
        /// Missing files are silent; a failure only warns, because a leftover JSON is cosmetic.
        /// </summary>
        public static void DeleteLegacy(string saveName)
        {
            try
            {
                DeleteLegacyFile(GetLegacyPath(saveName));
                if (IsAutoSaveName(saveName))
                {
                    // Autosave rotation renames the .dsv, so the leftover sidecar can sit under the
                    // temporary name as well; both are stale once a trailer exists.
                    string fallback = GetLegacyPath(AutoSaveTmpName);
                    if (!string.Equals(fallback, GetLegacyPath(saveName), StringComparison.OrdinalIgnoreCase))
                    {
                        DeleteLegacyFile(fallback);
                    }
                }

                // Once the last sidecar is gone the whole "saves" folder is dead weight. Removing it
                // is worthwhile on its own: users read "no side files anymore" literally and an empty
                // BepInEx\config\DSPQualitySystem\saves directory still looks like one.
                RemoveLegacyDirectoryIfEmpty();
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("save.cleanup", e);
            }
        }

        /// <summary>
        /// Best-effort removal of the legacy sidecar directory once nothing is left in it. Failures
        /// and a non-empty folder are both fine (other save slots may still need their JSON), so this
        /// never throws and never logs a warning.
        /// </summary>
        private static void RemoveLegacyDirectoryIfEmpty()
        {
            try
            {
                string dir = LegacySaveDirectory;
                if (!Directory.Exists(dir))
                {
                    return;
                }
                if (Directory.GetFileSystemEntries(dir).Length != 0)
                {
                    return;
                }
                Directory.Delete(dir);
            }
            catch (Exception)
            {
                // A leftover empty folder cannot lose any data; keep the save path quiet.
            }
        }

        private static void DeleteLegacyFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return;
                }
                File.Delete(path);
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogInfo("[QSystem][save] removed legacy sidecar " + path);
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("save.cleanup", e);
            }
        }

        /// <summary>
        /// Serialize the in-memory data with the same schema the sidecar used, so a .dsv written by
        /// this build can also be migrated by an older build if the user ever rolls back.
        /// </summary>
        private static string BuildDocument(GameData data, out int planetCount, out int buildingCount,
            out int poolCount, out int storageCount, out int splitterCount, out int sorterCount)
        {
            var sb = new StringBuilder(8192);
            sb.Append("{\"version\":").Append(SchemaVersion).Append(",\"gameName\":");
            JsonLite.WriteString(sb, SafeGameName(data));
            sb.Append(",\"seed\":").Append(SeedOf(data));
            sb.Append(",\"planets\":{");

            planetCount = 0;
            buildingCount = 0;
            poolCount = 0;
            storageCount = 0;
            splitterCount = 0;
            sorterCount = 0;
            bool firstPlanet = true;
            foreach (KeyValuePair<int, PlanetQuality> pair in QualityData.planets)
            {
                PlanetQuality quality = pair.Value;
                if (quality == null)
                {
                    continue;
                }

                if (!firstPlanet)
                {
                    sb.Append(',');
                }
                firstPlanet = false;
                planetCount++;

                sb.Append('"').Append(pair.Key.ToString(CultureInfo.InvariantCulture)).Append("\":{\"buildings\":[");
                bool first = true;
                if (quality.buildingInc != null)
                {
                    for (int entityId = 0; entityId < quality.buildingInc.Length; entityId++)
                    {
                        int inc = quality.buildingInc[entityId];
                        if (inc == 0)
                        {
                            continue;
                        }
                        if (!first)
                        {
                            sb.Append(',');
                        }
                        first = false;
                        sb.Append("{\"e\":").Append(entityId).Append(",\"i\":").Append(inc).Append('}');
                        buildingCount++;
                    }
                }

                sb.Append("],\"pools\":[");
                first = true;
                if (quality.poolInc != null && quality.poolCount != null)
                {
                    int length = Math.Min(quality.poolInc.Length, quality.poolCount.Length);
                    for (int entityId = 0; entityId < length; entityId++)
                    {
                        int poolIncValue = quality.poolInc[entityId];
                        int countValue = quality.poolCount[entityId];
                        if (poolIncValue == 0 && countValue == 0)
                        {
                            continue;
                        }
                        if (!first)
                        {
                            sb.Append(',');
                        }
                        first = false;
                        sb.Append("{\"e\":").Append(entityId)
                            .Append(",\"pi\":").Append(poolIncValue)
                            .Append(",\"pc\":").Append(countValue).Append('}');
                        poolCount++;
                    }
                }

                // v0.19.1 (issue: quality dropped by one level after a save/load cycle): the batch
                // that an assembler read its ingredients at ("inputLevel") is what the next cycle
                // derives its product quality from. It used to live in memory only, so the first
                // batch produced after a load inherited 0 points instead of the real ingredient
                // level. It is a per-entity integer, so it rides in the MOD trailer as "inputs".
                sb.Append("],\"inputs\":[");
                first = true;
                if (quality.inputLevel != null)
                {
                    for (int entityId = 0; entityId < quality.inputLevel.Length; entityId++)
                    {
                        int level = quality.inputLevel[entityId];
                        if (level < 0)
                        {
                            continue;
                        }
                        if (!first)
                        {
                            sb.Append(',');
                        }
                        first = false;
                        sb.Append("{\"e\":").Append(entityId).Append(",\"l\":").Append(level).Append('}');
                    }
                }

                // v0.13.0 (task-17 / C-1): containers whose extra grids could not be emptied back
                // into the vanilla grids for this save. "b" is the vanilla grid count, "cells" the
                // items that live in the extra grids (grid index + full vanilla grid fields). A
                // vanilla client never sees this node: it is inside the MOD trailer only.
                sb.Append("],\"storages\":[");
                first = true;
                List<StorageGrids.OverflowRecord> stored = StorageGrids.OverflowForPlanet(pair.Key);
                if (stored != null)
                {
                    for (int i = 0; i < stored.Count; i++)
                    {
                        StorageGrids.OverflowRecord record = stored[i];
                        if (record.cells == null || record.cells.Count == 0)
                        {
                            continue;
                        }
                        if (!first)
                        {
                            sb.Append(',');
                        }
                        first = false;
                        sb.Append("{\"e\":").Append(record.entityId)
                            .Append(",\"b\":").Append(record.baseGrids)
                            .Append(",\"cells\":[");
                        for (int c = 0; c < record.cells.Count; c++)
                        {
                            if (c > 0)
                            {
                                sb.Append(',');
                            }
                            StorageGrids.OverflowCell cell = record.cells[c];
                            sb.Append("{\"g\":").Append(cell.grid)
                                .Append(",\"i\":").Append(cell.itemId)
                                .Append(",\"f\":").Append(cell.filter)
                                .Append(",\"c\":").Append(cell.count)
                                .Append(",\"s\":").Append(cell.stackSize)
                                .Append(",\"q\":").Append(cell.inc).Append('}');
                        }
                        sb.Append("]}");
                        storageCount++;
                    }
                }

                // v0.16.1 (C-2 / C-3): per-port splitter quality rules and per-sorter quality
                // thresholds. These live in the MOD trailer only; the vanilla splitter/sorter
                // rotation is never modified, so a vanilla client simply ignores this node.
                QualityData.PlanetFilterTable filters = QualityData.FiltersForSave(pair.Key);
                sb.Append("],\"splittersV2\":[");
                first = true;
                if (filters != null && filters.splitterRuleCount > 0)
                {
                    int entityLimit = filters.capacity;
                    for (int entityId = 0; entityId < entityLimit; entityId++)
                    {
                        int baseIndex = entityId * QualityMath.SplitterPortCount;
                        if (baseIndex + QualityMath.SplitterPortCount > filters.splitterRules.Length)
                        {
                            break;
                        }
                        bool any = false;
                        for (int port = 0; port < QualityMath.SplitterPortCount; port++)
                        {
                            if (!QualityMath.SplitterRuleIsEmpty(filters.splitterRules[baseIndex + port]))
                            {
                                any = true;
                                break;
                            }
                        }
                        if (!any)
                        {
                            continue;
                        }
                        if (!first)
                        {
                            sb.Append(',');
                        }
                        first = false;
                        sb.Append("{\"e\":").Append(entityId).Append(",\"o\":[");
                        bool firstPort = true;
                        for (int port = 0; port < QualityMath.SplitterPortCount; port++)
                        {
                            int packed = filters.splitterRules[baseIndex + port];
                            if (QualityMath.SplitterRuleIsEmpty(packed))
                            {
                                continue;
                            }
                            if (!firstPort)
                            {
                                sb.Append(',');
                            }
                            firstPort = false;
                            sb.Append("{\"p\":").Append(port)
                                .Append(",\"i\":").Append(QualityMath.SplitterRuleItem(packed))
                                .Append(",\"m\":").Append(QualityMath.SplitterRuleMin(packed)).Append('}');
                            splitterCount++;
                        }
                        sb.Append("]}");
                    }
                }

                sb.Append("],\"sorters\":[");
                first = true;
                if (filters != null && filters.sorterRuleCount > 0)
                {
                    for (int entityId = 0; entityId < filters.sorterMin.Length; entityId++)
                    {
                        int min = filters.sorterMin[entityId];
                        if (min <= 0)
                        {
                            continue;
                        }
                        if (!first)
                        {
                            sb.Append(',');
                        }
                        first = false;
                        sb.Append("{\"e\":").Append(entityId).Append(",\"m\":").Append(min).Append('}');
                        sorterCount++;
                    }
                }
                sb.Append(']');
                // v0.19.0 (quality recycler): point remainder + pending bottles of every coater that
                // works as a recycler. Writes nothing when the planet has no recycler state, so the
                // document shape stays identical for saves that never used the feature.
                QualityData.AppendRecyclersJson(sb, pair.Key);
                sb.Append('}');
            }

            // The container snapshot belongs to this one save; the postfix restores the live
            // container shapes independently, so it is safe to drop the items copy here.
            StorageGrids.ClearSaveSnapshot();
            sb.Append("}}");
            return sb.ToString();
        }

        /// <summary>
        /// Apply a parsed quality document to the in-memory data. Returns the number of applied
        /// entries, or -1 when the document has no usable planets object.
        /// </summary>
        private static int ApplyDocument(Dictionary<string, object> root, GameData data,
            out int planetCount, out int buildingCount, out int poolCount,
            out int storageCount, out int splitterCount, out int sorterCount)
        {
            planetCount = 0;
            buildingCount = 0;
            poolCount = 0;
            storageCount = 0;
            splitterCount = 0;
            sorterCount = 0;

            var planets = GetDict(root, "planets");
            if (planets == null)
            {
                return -1;
            }

            int applied = 0;
            foreach (KeyValuePair<string, object> pair in planets)
            {
                int planetId;
                if (!int.TryParse(pair.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out planetId)
                    || planetId < 0)
                {
                    continue;
                }

                EnsurePlanetCapacity(data, planetId);
                var planet = pair.Value as Dictionary<string, object>;
                if (planet == null)
                {
                    continue;
                }
                planetCount++;

                var buildings = GetList(planet, "buildings");
                if (buildings != null)
                {
                    foreach (object item in buildings)
                    {
                        var entry = item as Dictionary<string, object>;
                        if (entry == null)
                        {
                            continue;
                        }
                        int entityId = GetInt(entry, "e");
                        int inc = GetInt(entry, "i");
                        if (entityId < 0 || inc <= 0)
                        {
                            continue;
                        }
                        QualityData.SetBuildingInc(planetId, entityId, (byte)Math.Min(inc, 255));
                        buildingCount++;
                        applied++;
                    }
                }

                var pools = GetList(planet, "pools");
                if (pools != null)
                {
                    foreach (object item in pools)
                    {
                        var entry = item as Dictionary<string, object>;
                        if (entry == null)
                        {
                            continue;
                        }
                        int entityId = GetInt(entry, "e");
                        if (entityId < 0)
                        {
                            continue;
                        }
                        QualityData.SetPool(planetId, entityId, GetInt(entry, "pi"), GetInt(entry, "pc"));
                        poolCount++;
                        applied++;
                    }
                }

                // v0.19.1: restore the ingredient batch level each assembler read its inputs at,
                // so products made right after a load keep the correct inherited quality.
                var inputs = GetList(planet, "inputs");
                if (inputs != null)
                {
                    foreach (object item in inputs)
                    {
                        var entry = item as Dictionary<string, object>;
                        if (entry == null)
                        {
                            continue;
                        }
                        int entityId = GetInt(entry, "e");
                        int level = GetInt(entry, "l");
                        if (entityId <= 0 || level < 0)
                        {
                            continue;
                        }
                        QualityData.SetInputLevel(planetId, entityId, (short)Math.Min(level, 10));
                        applied++;
                    }
                }

                // v0.13.0 (C-1): containers that kept items in their extra grids for this save.
                var storages = GetList(planet, "storages");
                if (storages != null)
                {
                    foreach (object item in storages)
                    {
                        var entry = item as Dictionary<string, object>;
                        if (entry == null)
                        {
                            continue;
                        }
                        int entityId = GetInt(entry, "e");
                        int baseGrids = GetInt(entry, "b");
                        if (entityId <= 0 || baseGrids <= 0)
                        {
                            continue;
                        }
                        List<StorageGrids.OverflowCell> cells = null;
                        var cellList = GetList(entry, "cells");
                        if (cellList != null)
                        {
                            cells = new List<StorageGrids.OverflowCell>(cellList.Count);
                            foreach (object cellItem in cellList)
                            {
                                var cellEntry = cellItem as Dictionary<string, object>;
                                if (cellEntry == null)
                                {
                                    continue;
                                }
                                var cell = new StorageGrids.OverflowCell();
                                cell.grid = GetInt(cellEntry, "g");
                                cell.itemId = GetInt(cellEntry, "i");
                                cell.filter = GetInt(cellEntry, "f");
                                cell.count = GetInt(cellEntry, "c");
                                cell.stackSize = GetInt(cellEntry, "s");
                                cell.inc = GetInt(cellEntry, "q");
                                if (cell.grid < 0 || cell.itemId <= 0 || cell.count <= 0)
                                {
                                    continue;
                                }
                                cells.Add(cell);
                            }
                        }
                        StorageGrids.ApplyLoadedStorage(planetId, entityId, baseGrids, cells);
                        storageCount++;
                        applied++;
                    }
                }

                // v0.16.1 (C-2 / C-3): per-port splitter rules and per-sorter thresholds.
                var splittersV2 = GetList(planet, "splittersV2");
                if (splittersV2 != null)
                {
                    foreach (object item in splittersV2)
                    {
                        var entry = item as Dictionary<string, object>;
                        if (entry == null)
                        {
                            continue;
                        }
                        int entityId = GetInt(entry, "e");
                        if (entityId <= 0)
                        {
                            continue;
                        }
                        var outs = GetList(entry, "o");
                        if (outs == null)
                        {
                            continue;
                        }
                        foreach (object portItem in outs)
                        {
                            var portEntry = portItem as Dictionary<string, object>;
                            if (portEntry == null)
                            {
                                continue;
                            }
                            int port = GetInt(portEntry, "p");
                            int itemId = GetInt(portEntry, "i");
                            int min = GetInt(portEntry, "m");
                            int packed = QualityMath.PackSplitterRule(itemId, min);
                            if (QualityMath.SplitterRuleIsEmpty(packed))
                            {
                                continue;
                            }
                            QualityData.SetSplitterRule(planetId, entityId, port, packed);
                            splitterCount++;
                            applied++;
                        }
                    }
                }

                var sorters = GetList(planet, "sorters");
                if (sorters != null)
                {
                    foreach (object item in sorters)
                    {
                        var entry = item as Dictionary<string, object>;
                        if (entry == null)
                        {
                            continue;
                        }
                        int entityId = GetInt(entry, "e");
                        int min = GetInt(entry, "m");
                        if (entityId <= 0 || min <= 0 || !QualityMath.ValidateSorterMin(min))
                        {
                            continue;
                        }
                        QualityData.SetSorterMin(planetId, entityId, min);
                        sorterCount++;
                        applied++;
                    }
                }

                // v0.19.0 (quality recycler): restore the per-planet point remainder and the bottles
                // still waiting for a free belt slot. Unknown/absent node = nothing to do, and any
                // value above the threshold is folded into bottles by the reader itself.
                applied += QualityData.ApplyRecyclersJson(planetId, planet);

                // v0.13.0 / v0.15.x trailers carried a boolean "quality first" splitter flag. That
                // semantics was replaced in v0.16.1 by per-port rules, so the node is intentionally
                // ignored (the splitter falls back to vanilla behaviour until the player sets a rule).
                if (!_legacySplitterNodeLogged)
                {
                    var legacy = GetList(planet, "splitters");
                    if (legacy != null && legacy.Count > 0)
                    {
                        _legacySplitterNodeLogged = true;
                        QualityEnv.LogInfo("[QSystem][save] legacy 'splitters' node ignored (pre-v0.16.1 "
                            + "quality-priority flag): set per-port rules in the splitter window");
                    }
                }
            }

            // The splitter flag may have been restored before the belt/splitter pools finished
            // importing; a full sweep also re-shapes every container from the restored points.
            StorageGrids.RequestFullSweep();
            return applied;
        }

        /// <summary>
        /// Size the per-entity arrays from the planet factory when it already exists. Import may run
        /// before the factories are populated, in which case QualityData grows the arrays on demand
        /// (SetBuildingInc calls EnsureCapacity(entityId + 1)), so this is a fast path, not a
        /// correctness requirement.
        /// </summary>
        private static void EnsurePlanetCapacity(GameData data, int planetId)
        {
            try
            {
                if (data == null || data.factories == null || data.galaxy == null)
                {
                    return;
                }
                // GameData.factories is indexed by PlanetData.factoryIndex, not by planet id.
                PlanetData planet = data.galaxy.PlanetById(planetId);
                if (planet == null)
                {
                    return;
                }
                int factoryIndex = planet.factoryIndex;
                if (factoryIndex < 0 || factoryIndex >= data.factories.Length)
                {
                    return;
                }
                PlanetFactory factory = data.factories[factoryIndex];
                if (factory != null && factory.entityPool != null)
                {
                    QualityData.EnsureCapacity(planetId, factory.entityPool.Length);
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("save.capacity", e);
            }
        }

        private static string GetExpectedGameName(string saveName)
        {
            // Manual saves pass the save slot name itself; fall back to the in-game name when the
            // hook got an empty name (e.g. some autosave paths).
            return string.IsNullOrEmpty(saveName) ? CurrentGameName() : saveName;
        }

        private static bool IsAutoSaveName(string saveName)
        {
            return !string.IsNullOrEmpty(saveName)
                && saveName.StartsWith(AutoSavePrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static GameData CurrentData()
        {
            try
            {
                return GameMain.data;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Galaxy seed used as the identity check. 0 means "not available" (menu, no game data) and
        /// disables the seed check, matching SaveGuard.ValidateSeed.
        /// </summary>
        private static int SeedOf(GameData data)
        {
            try
            {
                if (data == null || data.gameDesc == null)
                {
                    return 0;
                }
                return data.gameDesc.galaxySeed;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static int CurrentSeed()
        {
            return SeedOf(CurrentData());
        }

        private static string CurrentGameName()
        {
            try
            {
                return GameMain.data != null ? GameMain.data.gameName : "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static string SafeGameName(GameData data)
        {
            try
            {
                return data != null && data.gameName != null ? data.gameName : "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }

            var sb = new StringBuilder(name.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool bad = c == '/' || c == '\\' || c == ':' || c == '*' || c == '?' || c == '"'
                    || c == '<' || c == '>' || c == '|';
                for (int j = 0; !bad && j < invalid.Length; j++)
                {
                    bad = c == invalid[j];
                }
                sb.Append(bad ? '_' : c);
            }
            return sb.ToString().Trim();
        }

        private static void WarnEmbeddedIgnored(GameData data, string message)
        {
            if (Plugin.Log != null)
            {
                Plugin.Log.LogWarning("[QSystem][save] embedded quality trailer ('" + SafeGameName(data)
                    + "') -> ignored: " + message);
            }
        }

        private static void Warn(string saveName, string message, string path)
        {
            if (Plugin.Log != null)
            {
                Plugin.Log.LogWarning("[QSystem][save] '" + saveName + "' (" + path + "): " + message);
            }
        }

        private static Dictionary<string, object> GetDict(Dictionary<string, object> source, string key)
        {
            object value;
            if (source != null && source.TryGetValue(key, out value))
            {
                return value as Dictionary<string, object>;
            }
            return null;
        }

        private static List<object> GetList(Dictionary<string, object> source, string key)
        {
            object value;
            if (source != null && source.TryGetValue(key, out value))
            {
                return value as List<object>;
            }
            return null;
        }

        private static int GetInt(Dictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                return 0;
            }
            if (value is long)
            {
                return (int)(long)value;
            }
            if (value is double)
            {
                return (int)Math.Round((double)value);
            }
            if (value is string)
            {
                int parsed;
                if (int.TryParse((string)value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed;
                }
            }
            return 0;
        }
    }
}
