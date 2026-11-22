using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.20.0 (issue 2): OrbitalRing's own production buildings (space dock / orbital hydroponics /
    /// ecological greenhouse and friends) moved their finished products into OrbitalRing's shared
    /// storage ("共享空间") or the equator-ring elevator storage without any quality points.
    ///
    /// Why it happens: OrbitalRing replaces `AssemblerComponent.InternalUpdate` for those buildings
    /// and drains `produced[]` itself:
    ///   * `OrbitalRingStorageCalculate.ShareStorageForOrbitalAssembler(PlanetFactory, FactorySystem)`
    ///     does `storageItem[item][0] += n` and only writes `storageItem[item][1]` (the total inc of
    ///     the stack) when `recipeType == Smelt` (`+= 4 * n` - a hard-coded flat value);
    ///   * `OrbitalAssembler.OrbitalAssemblerInternalUpdate` does the same for the elevator storage
    ///     (`elevatorStorage[k].count += n`, `inc += 4 * n` only for Smelt).
    /// Every other recipe (assemble, chemical, farm, ...) therefore deposits 0-point products even
    /// when the building/inputs carry quality points, while DSPQualitySystem's per-machine quality
    /// pool keeps accumulating and is never consumed.
    ///
    /// Fix (installed only when the OrbitalRing mod is present): observe both transfers. A prefix
    /// snapshots `produced[]` and the destination slot counts of this assembler; the postfix detects
    /// how many products actually moved and adds the pooled per-item quality for exactly that count
    /// through the same `inc` field the vanilla output path uses. For smelters OrbitalRing already
    /// adds 4 points per item, so only the pooled points above that flat 4 are added, keeping the
    /// total within 0..10 per item. Points are conserved: they are taken out of the DSPQS pool with
    /// `QualityData.TakeProductInc` and the integer remainder stays pooled for the next transfer.
    ///
    /// Everything is reflection-based on purpose: DSPQualitySystem must keep loading when
    /// OrbitalRing is not installed, and must not hard-reference ProjectOrbitalRing.dll. Any missing
    /// type/method just leaves the feature NOT ARMED and the game untouched.
    /// </summary>
    internal static class P8_OrbitalRingCompat
    {
        private const string Tag = "[QSystem][orbitalring] ";
        private const string AssemblyName = "ProjectOrbitalRing";
        private const string GuidGuard = "org.ProfessorCat305.OrbitalRing";
        private const int EffectiveLogEvery = 200;
        private const int DebugLogLimit = 20;

        private const int ModeSharedStorage = 0;
        private const int ModeElevatorStorage = 1;

        private static bool _armed;
        private static string _reason;

        // Cached reflection handles (resolved once when the mod is detected).
        private static PropertyInfo _managerInstance;
        private static MethodInfo _getRingData;
        private static PropertyInfo _ringsProperty;
        private static PropertyInfo _capacityProperty;
        private static MethodInfo _getPair;
        private static MethodInfo _getElevatorStorage;
        private static MethodInfo _isOneFull;
        private static FieldInfo _ringStorageField;
        private static FieldInfo _storageItemField;
        private static FieldInfo _pairElevatorField;
        private static FieldInfo _pairStationField;
        private static FieldInfo _pairTypeField;
        private static object _assemblerStationType;
        private static Type _storageCalcType;
        private static Type _ringStorageType;

        private static long _moves;
        private static long _points;
        private static long _zeroPool;
        private static long _missedMoves;
        private static long _sharedMoves;
        private static long _elevatorMoves;
        private static int _debugLogs;
        private static bool _firstLogged;
        private static bool _firstMissLogged;

        /// <summary>Per-thread call state: OrbitalRing ticks can run on several worker threads.</summary>
        [ThreadStatic] private static CallState _state;

        private sealed class CallState
        {
            public bool Active;
            public int Mode;
            public int PlanetId;
            public int EntityId;
            public int[] ProducedBefore = new int[0];
            public int ProducedLength;
            public readonly List<Entry> Entries = new List<Entry>();
        }

        private struct Entry
        {
            public int ItemId;
            public int CountBefore;

            /// <summary>Shared-storage dictionary (live at postfix time) and its value array.</summary>
            public Dictionary<int, int[]> Dict;
            public int[] SharedArray;

            /// <summary>Elevator-storage station array and the slot index inside it.</summary>
            public StationStore[] Store;
            public int StoreIndex;
        }

        internal static void Apply(Harmony harmony)
        {
            try
            {
                if (harmony == null)
                {
                    Disable("no harmony instance");
                    return;
                }
                if (!P5Common.Enabled)
                {
                    Disable("quality system disabled");
                    return;
                }
                if (!OrbitalRingPresent())
                {
                    Disable("OrbitalRing (ProjectOrbitalRing) is not installed");
                    return;
                }

                if (!ResolveReflection())
                {
                    return;
                }

                MethodInfo shared = FindStatic("ProjectOrbitalRing.Patches.Logic.OrbitalRing.OrbitalRingStorageCalculate",
                    "ShareStorageForOrbitalAssembler",
                    new[] { typeof(AssemblerComponent).MakeByRefType(), typeof(FactorySystem) });
                MethodInfo elevator = FindStatic("ProjectOrbitalRing.Patches.Logic.OrbitalRing.OrbitalAssembler",
                    "OrbitalAssemblerInternalUpdate",
                    new[] { typeof(AssemblerComponent).MakeByRefType(), typeof(int) });

                if (shared == null && elevator == null)
                {
                    Disable("OrbitalRing production transfer methods not found (unsupported version)");
                    return;
                }

                if (shared != null)
                {
                    harmony.Patch(shared,
                        prefix: new HarmonyMethod(typeof(P8_OrbitalRingCompat).GetMethod(
                            "SharedPrefix", BindingFlags.NonPublic | BindingFlags.Static)),
                        postfix: new HarmonyMethod(typeof(P8_OrbitalRingCompat).GetMethod(
                            "SharedPostfix", BindingFlags.NonPublic | BindingFlags.Static)));
                }
                if (elevator != null)
                {
                    harmony.Patch(elevator,
                        prefix: new HarmonyMethod(typeof(P8_OrbitalRingCompat).GetMethod(
                            "ElevatorPrefix", BindingFlags.NonPublic | BindingFlags.Static)),
                        postfix: new HarmonyMethod(typeof(P8_OrbitalRingCompat).GetMethod(
                            "ElevatorPostfix", BindingFlags.NonPublic | BindingFlags.Static)));
                }

                // OrbitalRing's shared-storage <-> station-storage sync truncates the slot's TOTAL inc
                // to short (`stationComponent.storage[i].inc += (short)…`), so a stock of tens of
                // thousands of points overflows into a negative value as soon as the player takes
                // items out of the shared storage. Both sites are a plain conv.i2 right before the
                // int add; widening them to conv.i4 keeps the arithmetic in int and the displayed
                // points stay correct.
                MethodInfo stationShare = null;
                if (_storageCalcType != null && _ringStorageType != null)
                {
                    stationShare = _storageCalcType.GetMethod("ShareStorageForOrbitalStation",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null,
                        new[] { typeof(StationComponent).MakeByRefType(), _ringStorageType.MakeByRefType() },
                        null);
                }
                if (stationShare != null)
                {
                    harmony.Patch(stationShare,
                        transpiler: new HarmonyMethod(typeof(P8_OrbitalRingCompat).GetMethod(
                            "StationShareTranspiler", BindingFlags.NonPublic | BindingFlags.Static)));
                }
                else
                {
                    P5Common.Info(Tag + "ShareStorageForOrbitalStation not found; the short-truncation"
                        + " fix for the station sync is skipped (large stocks may show negative points)");
                }

                _armed = true;
                P5Common.Info(Tag + "armed: quality points now follow OrbitalRing products into shared"
                    + " storage / elevator storage (shared=" + (shared != null)
                    + " elevator=" + (elevator != null)
                    + " stationShareConv=" + (stationShare != null) + ")");
            }
            catch (Exception e)
            {
                Disable("install failed: " + e.Message);
                QualityEnv.WarnOnce("orbitalring.install", e);
            }
        }

        private static bool OrbitalRingPresent()
        {
            try
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly == null)
                    {
                        continue;
                    }
                    AssemblyName name = assembly.GetName();
                    if (name != null && name.Name == AssemblyName)
                    {
                        return true;
                    }
                }
                // Fallback: resolves (or loads) the assembly only if it is available at all.
                return Type.GetType("ProjectOrbitalRing.ProjectOrbitalRing, " + AssemblyName) != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool ResolveReflection()
        {
            _managerInstance = null;
            _getRingData = null;
            _ringsProperty = null;
            _capacityProperty = null;
            _getPair = null;
            _getElevatorStorage = null;
            _isOneFull = null;
            _ringStorageField = null;
            _storageItemField = null;
            _pairElevatorField = null;
            _pairStationField = null;
            _pairTypeField = null;
            _assemblerStationType = null;

            Type manager = Type.GetType("ProjectOrbitalRing.Patches.Logic.OrbitalRing.OrbitalStationManager, " + AssemblyName);
            Type ringData = Type.GetType("ProjectOrbitalRing.Patches.Logic.OrbitalRing.PlanetOrbitalRingData, " + AssemblyName);
            Type ring = Type.GetType("ProjectOrbitalRing.Patches.Logic.OrbitalRing.EquatorRing, " + AssemblyName);
            Type ringStorage = Type.GetType("ProjectOrbitalRing.Patches.Logic.OrbitalRing.OrbitalRingStorage, " + AssemblyName);
            _storageCalcType = Type.GetType("ProjectOrbitalRing.Patches.Logic.OrbitalRing.OrbitalRingStorageCalculate, " + AssemblyName);
            _ringStorageType = ringStorage;

            if (manager == null || ringData == null || ring == null || ringStorage == null)
            {
                Disable("OrbitalRing ring data types not found (unsupported version)");
                return false;
            }

            _managerInstance = manager.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            _getRingData = manager.GetMethod("GetPlanetOrbitalRingData",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(int) }, null);
            _ringsProperty = ringData.GetProperty("Rings",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _capacityProperty = ring.GetProperty("Capacity",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _getPair = ring.GetMethod("GetPair",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(int) }, null);
            _getElevatorStorage = ring.GetMethod("GetElevatorStorage",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(int) }, null);
            _isOneFull = ring.GetMethod("IsOneFull",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                Type.EmptyTypes, null);
            _ringStorageField = ring.GetField("orbitalRingStorage",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _storageItemField = ringStorage.GetField("storageItem",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (_managerInstance == null || _getRingData == null || _ringsProperty == null
                || _capacityProperty == null || _getPair == null || _getElevatorStorage == null
                || _isOneFull == null || _ringStorageField == null || _storageItemField == null)
            {
                Disable("OrbitalRing ring data members missing (unsupported version)");
                return false;
            }

            if (_getPair.ReturnType == null)
            {
                Disable("OrbitalRing GetPair has no tuple return type");
                return false;
            }
            FieldInfo[] tupleFields = _getPair.ReturnType.GetFields(
                BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < tupleFields.Length; i++)
            {
                if (tupleFields[i].Name == "Item1")
                {
                    _pairElevatorField = tupleFields[i];
                }
                else if (tupleFields[i].Name == "Item2")
                {
                    _pairStationField = tupleFields[i];
                }
                else if (tupleFields[i].Name == "Item3")
                {
                    _pairTypeField = tupleFields[i];
                }
            }
            if (_pairElevatorField == null || _pairStationField == null || _pairTypeField == null)
            {
                Disable("OrbitalRing GetPair tuple layout unsupported");
                return false;
            }

            Type stationType = _pairTypeField.FieldType;
            if (stationType.IsEnum)
            {
                _assemblerStationType = Enum.Parse(stationType, "Assembler");
            }
            if (_assemblerStationType == null)
            {
                Disable("OrbitalRing StationType.Assembler not found");
                return false;
            }

            return true;
        }

        private static MethodInfo FindStatic(string typeName, string methodName, Type[] parameters)
        {
            try
            {
                Type type = Type.GetType(typeName + ", " + AssemblyName);
                if (type == null)
                {
                    return null;
                }
                return type.GetMethod(methodName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null,
                    parameters, null);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---------------------------------------------------------------- Harmony entries

        private static void SharedPrefix(ref AssemblerComponent __0, FactorySystem __1)
        {
            Begin(ref __0, __1 == null || __1.factory == null || __1.factory.planet == null
                ? -1 : __1.factory.planet.id, ModeSharedStorage);
        }

        private static void SharedPostfix(ref AssemblerComponent __0, FactorySystem __1)
        {
            End(ref __0);
        }

        private static void ElevatorPrefix(ref AssemblerComponent __0, int __1)
        {
            Begin(ref __0, __1, ModeElevatorStorage);
        }

        private static void ElevatorPostfix(ref AssemblerComponent __0, int __1)
        {
            End(ref __0);
        }

        /// <summary>
        /// Widen the two `(short)` truncations in OrbitalRing's
        /// `ShareStorageForOrbitalStation` (shared storage -> station storage inc) to int adds.
        /// Both are the only conv.i2 in that method; the surrounding code is
        /// `… storage[i].inc += (short)value;`, where conversion to int changes nothing for small
        /// values and prevents the sign flip for large stocks.
        /// </summary>
        private static IEnumerable<CodeInstruction> StationShareTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            int widened = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Conv_I2)
                {
                    instruction.opcode = OpCodes.Conv_I4;
                    widened++;
                }
                yield return instruction;
            }
            P5Common.Info(Tag + "ShareStorageForOrbitalStation: widened " + widened
                + " conv.i2 to conv.i4 (station inc truncation fix)");
        }

        // ---------------------------------------------------------------- transfer observation

        private static void Begin(ref AssemblerComponent assembler, int planetId, int mode)
        {
            CallState state = _state;
            if (state == null)
            {
                state = new CallState();
                _state = state;
            }
            state.Active = false;
            state.Mode = mode;
            state.PlanetId = planetId;
            state.EntityId = assembler.entityId;
            state.Entries.Clear();

            try
            {
                if (!_armed || planetId <= 0 || assembler.entityId <= 0)
                {
                    return;
                }
                RecipeExecuteData data = assembler.recipeExecuteData;
                int[] produced = assembler.produced;
                if (data == null || data.products == null || produced == null
                    || data.products.Length == 0 || produced.Length == 0)
                {
                    return;
                }

                // Cheap exit: nothing was produced since the previous tick.
                bool any = false;
                for (int i = 0; i < produced.Length; i++)
                {
                    if (produced[i] > 0)
                    {
                        any = true;
                        break;
                    }
                }
                if (!any)
                {
                    return;
                }

                int count = produced.Length;
                if (state.ProducedBefore.Length < count)
                {
                    state.ProducedBefore = new int[count];
                }
                Array.Copy(produced, state.ProducedBefore, count);
                state.ProducedLength = count;

                object ringDataObject = GetRingData(planetId);
                if (ringDataObject == null)
                {
                    return;
                }
                System.Collections.IList rings = _ringsProperty.GetValue(ringDataObject) as System.Collections.IList;
                if (rings == null || rings.Count == 0)
                {
                    return;
                }

                for (int r = 0; r < rings.Count; r++)
                {
                    object ring = rings[r];
                    if (ring == null)
                    {
                        continue;
                    }
                    if (mode == ModeSharedStorage)
                    {
                        CollectShared(ring, assembler.id, data.products, state.Entries);
                    }
                    else
                    {
                        CollectElevator(ring, assembler.id, data.products, state.Entries);
                    }
                }

                state.Active = state.Entries.Count > 0;
            }
            catch (Exception e)
            {
                state.Active = false;
                QualityEnv.WarnOnce("orbitalring.prefix", e);
            }
        }

        private static void CollectShared(object ring, int assemblerId, int[] products, List<Entry> entries)
        {
            if (!(bool)_isOneFull.Invoke(ring, null))
            {
                return;
            }
            object ringStorage = _ringStorageField.GetValue(ring);
            if (ringStorage == null)
            {
                return;
            }
            Dictionary<int, int[]> storageItem = _storageItemField.GetValue(ringStorage) as Dictionary<int, int[]>;
            if (storageItem == null)
            {
                return;
            }

            int capacity = (int)_capacityProperty.GetValue(ring, null);
            for (int position = 0; position < capacity; position++)
            {
                object pair = _getPair.Invoke(ring, new object[] { position });
                if (pair == null)
                {
                    continue;
                }
                if (!IsAssemblerPair(pair, assemblerId))
                {
                    continue;
                }

                int[] array;
                for (int i = 0; i < products.Length; i++)
                {
                    int itemId = products[i];
                    if (itemId <= 0)
                    {
                        continue;
                    }
                    if (storageItem.TryGetValue(itemId, out array) && array != null && array.Length >= 2)
                    {
                        entries.Add(new Entry
                        {
                            ItemId = itemId,
                            CountBefore = array[0],
                            Dict = storageItem,
                            SharedArray = array
                        });
                    }
                    else
                    {
                        // v0.20.1: the key is often absent before the transfer because the station
                        // sync removes it once the previous stack was pulled out (this is exactly the
                        // space-dock probe-ship case). Register a placeholder so the postfix can see
                        // the key OrbitalRing creates during the call and add its points.
                        entries.Add(new Entry
                        {
                            ItemId = itemId,
                            CountBefore = 0,
                            Dict = storageItem,
                            SharedArray = null
                        });
                    }
                }
            }
        }

        private static void CollectElevator(object ring, int assemblerId, int[] products, List<Entry> entries)
        {
            int capacity = (int)_capacityProperty.GetValue(ring, null);
            for (int position = 0; position < capacity; position++)
            {
                object pair = _getPair.Invoke(ring, new object[] { position });
                if (pair == null)
                {
                    continue;
                }
                if (!IsAssemblerPair(pair, assemblerId))
                {
                    continue;
                }
                int elevatorId = (int)_pairElevatorField.GetValue(pair);
                if (elevatorId == -1)
                {
                    continue;
                }

                StationStore[] storage = _getElevatorStorage.Invoke(ring, new object[] { position }) as StationStore[];
                if (storage == null)
                {
                    continue;
                }
                for (int k = 0; k < storage.Length; k++)
                {
                    int itemId = storage[k].itemId;
                    if (itemId <= 0 || Array.IndexOf(products, itemId) < 0)
                    {
                        continue;
                    }
                    entries.Add(new Entry
                    {
                        ItemId = itemId,
                        CountBefore = storage[k].count,
                        Store = storage,
                        StoreIndex = k
                    });
                }
            }
        }

        private static bool IsAssemblerPair(object pair, int assemblerId)
        {
            object stationId = _pairStationField.GetValue(pair);
            if (!(stationId is int) || (int)stationId != assemblerId)
            {
                return false;
            }
            object type = _pairTypeField.GetValue(pair);
            return type != null && type.Equals(_assemblerStationType);
        }

        private static object GetRingData(int planetId)
        {
            object manager = _managerInstance.GetValue(null, null);
            if (manager == null)
            {
                return null;
            }
            return _getRingData.Invoke(manager, new object[] { planetId });
        }

        private static void End(ref AssemblerComponent assembler)
        {
            CallState state = _state;
            if (state == null || !state.Active)
            {
                return;
            }
            state.Active = false;

            try
            {
                int[] produced = assembler.produced;
                RecipeExecuteData data = assembler.recipeExecuteData;
                if (produced == null || data == null || data.products == null)
                {
                    return;
                }
                int slots = produced.Length;
                if (slots > state.ProducedLength)
                {
                    slots = state.ProducedLength;
                }
                if (data.products.Length < slots)
                {
                    slots = data.products.Length;
                }

                int recipeType = (int)assembler.recipeType;
                bool smelt = recipeType == 1;

                for (int i = 0; i < slots; i++)
                {
                    int moved = state.ProducedBefore[i] - produced[i];
                    if (moved <= 0)
                    {
                        continue;
                    }
                    int product = data.products[i];
                    if (product <= 0)
                    {
                        continue;
                    }

                    int movedTotal = moved;
                    int matched = 0;
                    for (int e = 0; e < state.Entries.Count && moved > 0; e++)
                    {
                        Entry entry = state.Entries[e];
                        if (entry.ItemId != product)
                        {
                            continue;
                        }
                        int current = CurrentCount(ref entry);
                        int delta = current - entry.CountBefore;
                        if (delta <= 0)
                        {
                            continue;
                        }
                        int take = delta < moved ? delta : moved;
                        moved -= take;
                        matched += take;

                        // Smelters: OrbitalRing already added 4 points per item before this
                        // postfix runs, so only pooled quality above that flat 4 is consumed/added.
                        int points = QualityData.TakeProductIncAbove(state.PlanetId, state.EntityId,
                            take, smelt ? 4 : 0);
                        if (points <= 0)
                        {
                            _zeroPool++;
                            continue;
                        }

                        AddPoints(ref entry, points);
                        _moves++;
                        _points += points;
                        if (state.Mode == ModeSharedStorage)
                        {
                            _sharedMoves++;
                        }
                        else
                        {
                            _elevatorMoves++;
                        }

                        if (!_firstLogged)
                        {
                            _firstLogged = true;
                            Info("OrbitalRing product quality path active: mode=" + ModeName(state.Mode)
                                + " planet=" + state.PlanetId + " entity=" + state.EntityId
                                + " item=" + product + " moved=" + take + " points=" + points);
                        }
                        if (_moves % EffectiveLogEvery == 0)
                        {
                            Info("OrbitalRing transfers=" + _moves + " points=" + _points
                                + " zeroPool=" + _zeroPool + " missed=" + _missedMoves
                                + " shared=" + _sharedMoves + " elevator=" + _elevatorMoves);
                        }
                        if (QualityConfig.DevMode && _debugLogs < DebugLogLimit)
                        {
                            _debugLogs++;
                            Info("OrbitalRing transfer mode=" + ModeName(state.Mode) + " item=" + product
                                + " take=" + take + " points=" + points + " smelt=" + smelt);
                        }
                    }

                    if (matched < movedTotal)
                    {
                        // The assembler moved products but we did not find the destination slot
                        // (or the slot count did not grow). Log once per mode so a missing output
                        // path is visible instead of silently losing the points.
                        _missedMoves++;
                        if (!_firstMissLogged)
                        {
                            _firstMissLogged = true;
                            Info("OrbitalRing transfer destination NOT matched: mode="
                                + ModeName(state.Mode) + " planet=" + state.PlanetId
                                + " entity=" + state.EntityId + " item=" + product
                                + " moved=" + movedTotal + " matched=" + matched
                                + " entries=" + state.Entries.Count);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("orbitalring.postfix", e);
            }
        }

        private static string ModeName(int mode)
        {
            return mode == ModeSharedStorage ? "shared" : "elevator";
        }

        private static int CurrentCount(ref Entry entry)
        {
            try
            {
                if (entry.Dict != null)
                {
                    int[] array;
                    if (entry.Dict.TryGetValue(entry.ItemId, out array) && array != null && array.Length >= 2)
                    {
                        entry.SharedArray = array;
                        return array[0];
                    }
                    return entry.SharedArray == null ? 0 : entry.SharedArray[0];
                }
                if (entry.Store != null && entry.StoreIndex >= 0 && entry.StoreIndex < entry.Store.Length
                    && entry.Store[entry.StoreIndex].itemId == entry.ItemId)
                {
                    return entry.Store[entry.StoreIndex].count;
                }
            }
            catch (Exception)
            {
            }
            return 0;
        }

        private static void AddPoints(ref Entry entry, int points)
        {
            try
            {
                if (points <= 0)
                {
                    return;
                }
                if (entry.Dict != null)
                {
                    int[] array = entry.SharedArray;
                    if (array == null || array.Length < 2)
                    {
                        return;
                    }
                    long cap = (long)array[0] * QualityMath.IncPerItemMax;
                    long total = (long)array[1] + points;
                    array[1] = total > cap ? (int)cap : (int)total;
                    return;
                }
                if (entry.Store != null && entry.StoreIndex >= 0 && entry.StoreIndex < entry.Store.Length)
                {
                    int index = entry.StoreIndex;
                    long cap = (long)entry.Store[index].count * QualityMath.IncPerItemMax;
                    long total = (long)entry.Store[index].inc + points;
                    entry.Store[index].inc = total > cap ? (int)cap : (int)total;
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("orbitalring.addpoints", e);
            }
        }

        private static void Info(string message)
        {
            if (!QualityConfig.DevMode)
            {
                return;
            }
            if (Plugin.Log != null)
            {
                Plugin.Log.LogInfo(Tag + message);
            }
        }

        internal static string Describe()
        {
            return "OrbitalRing compat armed=" + _armed + " reason=" + (_reason ?? "-")
                + " transfers=" + _moves + " points=" + _points + " zeroPool=" + _zeroPool
                + " missed=" + _missedMoves + " shared=" + _sharedMoves + " elevator=" + _elevatorMoves;
        }

        internal static void ResetState()
        {
            _moves = 0;
            _points = 0;
            _zeroPool = 0;
            _debugLogs = 0;
            _firstLogged = false;
            _state = null;
        }

        private static void Disable(string reason)
        {
            _armed = false;
            _reason = reason;
            P5Common.Info(Tag + "NOT ARMED (OrbitalRing products keep OrbitalRing's own inc): " + reason);
        }
    }
}
