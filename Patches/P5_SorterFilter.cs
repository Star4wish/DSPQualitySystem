using System;
using System.Reflection;
using HarmonyLib;
using DSPQualitySystem;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.19.0 (task-5): quality threshold for sorter (分拣器) pick-ups, applied to every source the
    /// vanilla pick funnel can produce.
    ///
    /// WHY THIS SHAPE (root cause of the v0.18.x "threshold has no effect" bug)
    ///   The old version only prefixed <c>CargoTraffic.TryPickItem</c>, i.e. it could only ever see
    ///   belt sources, and it re-derived the belt window from
    ///   <c>belt.pivotOnPath + offset - 2</c> instead of from the exact arguments vanilla uses.
    ///   Every other source (storage box, assembler output mouth, station, ejector, silo, lab,
    ///   power generator fuel) is dispatched by <c>PlanetFactory.PickFrom(uint,int,int,int[],out,out)</c>
    ///   and never reached the belt-only patch, which is why low-quality items were still taken.
    ///
    ///   The IL-verified vanilla chain for a sorter tick is
    ///     InserterComponent.InternalUpdate
    ///       -> PlanetFactory.PickFrom(uint ioTargetTypedId, int offset, int filter, int[] needs,
    ///                                  out byte stack, out byte inc)      (one call per attempt)
    ///       -> case Belt     : CargoTraffic.TryPickItem -> CargoPath.TryPickItem(index, 5, ...)
    ///       -> case Assembler: produced[]/products[] inside entityMutexs lock (P0_PickFrom overrides it)
    ///       -> case Storage  : StorageComponent.TakeTailItems (tail to head grid scan, split_inc)
    ///       -> case Station  : StationComponent.TakeItem
    ///       -> case Lab      : lab produced[]/products[]
    ///       -> case Ejector/Silo: TakeOneBulletUnsafe
    ///       -> case PowerGen : PickFuelFrom
    ///
    ///   This patch therefore hooks BOTH levels:
    ///     1. <c>CargoPath.TryPickItem</c> (with and without <c>int[] needs</c>) - the belt source.
    ///        Here the window index/length are already the exact vanilla values, so the decoder
    ///        cannot drift from vanilla again (the old "count=0" log lines were simply empty
    ///        windows: pathLength is bufferLength and the geometry arithmetic was identical, so a
    ///        non-empty window was never missed - what was missing was every non-belt source).
    ///     2. <c>PlanetFactory.PickFrom(uint,...)</c> with <c>Priority.High</c> - every non-belt
    ///        source. Belt is delegated to (1). Storage is picked here from the first grid that
    ///        satisfies the threshold (mirroring TakeTailItems + split_inc, so <c>inc</c> keeps the
    ///        vanilla "total points of the taken stack" meaning). Assembler output is pre-checked
    ///        against the quality product pool without consuming a point and then left to
    ///        <c>P0_PickFrom</c> (which runs after this prefix and attaches the pooled point).
    ///        Ejector/Silo bullets are pre-checked with bulletInc/bulletCount.
    ///
    ///   Sources that cannot be filtered without changing unrelated vanilla behaviour are skipped
    ///   and counted in Describe(): station (TakeItem selection order / keepMode bookkeeping cannot
    ///   be reproduced safely here), lab (lab products carry no quality pool, so their points are
    ///   always 0 - blocking would silently stop research output), power generator fuel (fuel has
    ///   no quality points; blocking it would stop power plants).
    ///
    /// COMPATIBILITY
    ///   * P0_PickFrom prefixes the same PickFrom(uint) method for the assembler branch. This patch
    ///     uses Priority.High so it is evaluated first; a "true" return keeps P0's prefix running,
    ///     a "false" return (threshold not met) blocks the take before P0 consumes a pooled point.
    ///   * P4_ConsumePriority prefixes <c>StorageComponent.TakeTailItems(ref,int,ref,int,bool)</c>
    ///     (the player consumption windows) and is not touched: sorter picks are implemented here
    ///     with a read-only grid scan, no TakeTailItems call is re-entered.
    ///   * P1_DeferredPay / P1_InstantBuild only act inside player build windows.
    ///
    /// The <c>[ThreadStatic] _ctxMin</c> contract from v0.18.x is kept: it is armed by the
    /// InserterComponent tick prefixes and cleared by the tick postfixes, so every hook below only
    /// filters while a sorter is actually ticking.
    /// </summary>
    internal static class P5_SorterFilter
    {
        internal const string Tag = "[QSystem][sorterfilter] ";

        /// <summary>Vanilla belt pick window length in buffer bytes (CargoTraffic.TryPickItem).</summary>
        internal const int WindowLength = 5;

        /// <summary>Slots decoded per belt window (the window is 5 bytes; 8 is a safe over-estimate).</summary>
        internal const int WindowSlots = 8;

        internal const int MaxDebugLogs = 20;
        internal const int MaxDiagLogs = 60;
        internal const int AlwaysDiagLogs = 8;
        internal const int SourceCount = 8;

        internal const int SrcBelt = 0;
        internal const int SrcStorage = 1;
        internal const int SrcAssembler = 2;
        internal const int SrcEjector = 3;
        internal const int SrcSilo = 4;
        internal const int SrcStation = 5;
        internal const int SrcLab = 6;
        internal const int SrcPowerGen = 7;

        private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private static bool _armed;
        private static string _reason = "not applied";
        private static int _tickPatched;
        private static int _beltPatched;
        private static int _pickFromPatched;

        private static long _picked;
        private static long _blocked;
        private static long _skipped;
        private static long _scanned;
        private static long _tickCalls;
        private static long _tickMinHits;
        private static long _pathCalls;
        private static long _pathMin0;
        private static long _pickCalls;
        private static long _pickMin0;
        private static int _diagLogs;
        private static int _debugLogs;
        private static int _storageProbeLogs;
        private const int StorageProbeLimit = 40;
        private static int _insertProbeLogs;
        private const int InsertProbeLimit = 40;
        private static int _pickProbeLogs;
        private const int PickProbeLimit = 40;
        private static int _watchItem;
        private static int _watchInc;
        private static int _stackProbeLogs;
        private static int _holdProbeLogs;
        private const int HoldProbeLimit = 40;
        private static int _returnProbeLogs;
        private const int ReturnProbeLimit = 40;
        private static readonly int[] _debugBySource = new int[SourceCount];
        private static readonly long[] _pickedBySource = new long[SourceCount];
        private static readonly long[] _blockedBySource = new long[SourceCount];
        private static readonly long[] _skippedBySource = new long[SourceCount];

        /// <summary>Threshold armed by the sorter tick prefix; 0 = no filtering for this call stack.</summary>
        [ThreadStatic] private static int _ctxMin;

        [ThreadStatic] private static int _ctxEntityId;

        [ThreadStatic] private static int _ctxPlanetId;

        [ThreadStatic] private static int[] _slotCargoIds;

        [ThreadStatic] private static int[] _slotPositions;

        [ThreadStatic] private static int[] _slotItems;

        [ThreadStatic] private static int[] _slotIncs;

        [ThreadStatic] private static int[] _slotStacks;

        [ThreadStatic] private static int[] _slotRawIncs;

        internal static bool Armed
        {
            get { return _armed; }
        }

        internal static string Reason
        {
            get { return _reason ?? "not applied"; }
        }

        // ------------------------------------------------------------------ install

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
            {
                Disable("no harmony instance");
                return;
            }

            try
            {
                if (!QualityConfig.SorterQualityFilterEnabled)
                {
                    Disable("sorterQualityFilter disabled by config");
                    return;
                }

                _tickPatched = 0;
                _tickPatched += PatchTick(harmony, "InternalUpdate",
                    new[] { typeof(PlanetFactory), typeof(int[][]), typeof(AnimData[]), typeof(float) });
                _tickPatched += PatchTick(harmony, "InternalUpdate_Bidirectional",
                    new[] { typeof(PlanetFactory), typeof(int[][]), typeof(AnimData[]), typeof(float), typeof(bool) });
                _tickPatched += PatchTick(harmony, "InternalUpdateNoAnim",
                    new[] { typeof(PlanetFactory), typeof(int[][]), typeof(float) });

                _beltPatched = PatchBelt(harmony);
                _pickFromPatched = PatchPickFrom(harmony);
                PatchInsertProbe(harmony);

                // All three anchors are mandatory: a partially armed install would advertise a
                // threshold that the unpatched vanilla path then silently ignores.
                if (_tickPatched != 3)
                {
                    Disable("only " + _tickPatched + "/3 InserterComponent tick methods could be patched");
                    return;
                }

                if (_beltPatched != 2)
                {
                    Disable("only " + _beltPatched + "/2 CargoPath.TryPickItem overloads could be patched");
                    return;
                }

                if (_pickFromPatched != 1)
                {
                    Disable("PlanetFactory.PickFrom(uint,...) could not be patched (pickFrom="
                        + _pickFromPatched + "/1)");
                    return;
                }

                _armed = true;
                _reason = null;
                P5Common.Info(Tag + "armed: sorter quality threshold is enforced on "
                    + "tick=" + _tickPatched + "/3 belt=" + _beltPatched + "/2 pickFrom=" + _pickFromPatched + "/1");
            }
            catch (Exception e)
            {
                Disable(e.ToString());
            }
        }

        /// <summary>
        /// v0.19.2 diagnostic: logs what actually reaches a belt after an inserter pick. Belt -&gt; belt
        /// keeps the points, so a storage -&gt; belt loss can be split between "our pick returned 0" and
        /// "the insertion dropped it" by comparing this line with the storage-probe / pick line.
        /// </summary>
        private static void PatchInsertProbe(Harmony harmony)
        {
            try
            {
                if (!P5Common.DebugOn)
                {
                    return;
                }

                MethodInfo insert = AccessTools.Method(typeof(CargoTraffic), "TryInsertItem",
                    new[] { typeof(int), typeof(int), typeof(int), typeof(byte), typeof(byte) });
                if (insert != null)
                {
                    harmony.Patch(insert, postfix: new HarmonyMethod(typeof(P5_SorterFilter).GetMethod(
                        nameof(InsertProbePostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                }

                // Stacking sorters never reach TryInsertItem: they funnel through
                // CargoPath.TryInsertItemWithStackIncreasement -> InsertItemDirect, so watch the
                // cargo-level entry as well (same one-shot watch, no extra noise).
                MethodInfo direct = AccessTools.Method(typeof(CargoPath), "InsertItemDirect",
                    new[] { typeof(int), typeof(int), typeof(byte), typeof(byte) });
                if (direct != null)
                {
                    harmony.Patch(direct, postfix: new HarmonyMethod(typeof(P5_SorterFilter).GetMethod(
                        nameof(CargoDirectPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                }

                // Entry point of the belt path: the inserter hands over its whole hand (itemCount /
                // itemInc, both by ref). Logging it tells us whether the points were already gone
                // when the inserter handed the goods over, or whether the cargo math dropped them.
                MethodInfo stackInsert = AccessTools.Method(typeof(CargoPath), "TryInsertItemWithStackIncreasement",
                    new[] { typeof(int), typeof(int), typeof(int), typeof(int).MakeByRefType(), typeof(int).MakeByRefType() });
                if (stackInsert != null)
                {
                    harmony.Patch(stackInsert,
                        prefix: new HarmonyMethod(typeof(P5_SorterFilter).GetMethod(
                            nameof(StackInsertPrefix), BindingFlags.Static | BindingFlags.NonPublic)),
                        postfix: new HarmonyMethod(typeof(P5_SorterFilter).GetMethod(
                            nameof(StackInsertPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                }

                // Container-level stack insert (stackOutput > 1 hands go through this one, see
                // InserterComponent.InternalUpdate). Probing it shows the count/inc the hand really
                // carries right before the cargo math runs.
                MethodInfo beltStackInsert = AccessTools.Method(typeof(CargoTraffic),
                    "TryInsertItemToBeltWithStackIncreasement", new[]
                    {
                        typeof(int), typeof(int), typeof(int), typeof(int), typeof(int).MakeByRefType(),
                        typeof(int).MakeByRefType(),
                    });
                if (beltStackInsert != null)
                {
                    harmony.Patch(beltStackInsert, prefix: new HarmonyMethod(typeof(P5_SorterFilter).GetMethod(
                        nameof(BeltStackInsertPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                }

                // The real hand-over point (task-7 B1): InserterComponent computes
                // num6 = itemInc / itemCount * (itemCount / stackCount) and calls InsertInto with it.
                // Watching the uint overload tells us whether the points were already 0 in the hand.
                MethodInfo into = AccessTools.Method(typeof(PlanetFactory), "InsertInto", new[]
                {
                    typeof(uint), typeof(int), typeof(int), typeof(byte), typeof(byte),
                    typeof(byte).MakeByRefType(),
                });
                if (into != null)
                {
                    harmony.Patch(into, postfix: new HarmonyMethod(typeof(P5_SorterFilter).GetMethod(
                        nameof(InsertIntoPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                }
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("sorterfilter.insertprobe", e.Message);
            }
        }

        /// <summary>
        /// Postfix of the cargo-level stack insert: <paramref name="itemCount"/> / <paramref name="itemInc"/>
        /// are the values the call left behind. When they still hold the hand, the cargo math never
        /// consumed it (the points were dropped somewhere else); when they are zero, the belt really
        /// took everything this call could.
        /// </summary>
        private static void StackInsertPostfix(CargoPath __instance, int index, int itemId, ref int itemCount,
            ref int itemInc)
        {
            try
            {
                if (!P5Common.DebugOn) { return; }
                if (_watchItem <= 0 || itemId != _watchItem)
                {
                    return;
                }

                if (_stackProbeLogs >= InsertProbeLimit)
                {
                    return;
                }

                _stackProbeLogs++;
                P5Common.Info("stack-insert2b item=" + itemId + " remainingCount=" + itemCount
                    + " remainingInc=" + itemInc + " expected=" + _watchInc + " eid=" + _ctxEntityId
                    + " tick=" + Environment.TickCount);
            }
            catch (Exception)
            {
            }
        }

        private static void StackInsertPrefix(int itemId, int maxStack, ref int itemCount, ref int itemInc)
        {
            try
            {
                if (!P5Common.DebugOn) { return; }
                if (_watchItem <= 0 || itemId != _watchItem)
                {
                    return;
                }

                if (_stackProbeLogs >= InsertProbeLimit)
                {
                    return;
                }

                _stackProbeLogs++;
                P5Common.Info("stack-insert item=" + itemId + " max=" + maxStack
                    + " count=" + itemCount + " inc=" + itemInc + " expected=" + _watchInc
                    + " frame=" + Environment.TickCount);
            }
            catch (Exception)
            {
            }
        }

        private static void InsertProbePostfix(int itemId, byte itemCount, byte itemInc)
        {
            try
            {
                if (!P5Common.DebugOn) { return; }
                // Only the pick we just handed back is interesting: a factory-wide dump of every
                // belt insert says nothing (and the budget is eaten within a second).
                if (_watchItem <= 0 || itemId != _watchItem)
                {
                    return;
                }

                int expected = _watchInc;
                if (_insertProbeLogs >= InsertProbeLimit)
                {
                    return;
                }

                _insertProbeLogs++;
                P5Common.Info("belt-insert item=" + itemId + " count=" + itemCount + " inc=" + itemInc
                    + " expected=" + expected + " tick=" + Environment.TickCount);
            }
            catch (Exception)
            {
            }
        }

        private static void CargoDirectPostfix(int itemId, byte stack, byte inc)
        {
            try
            {
                if (!P5Common.DebugOn) { return; }
                if (_watchItem <= 0 || itemId != _watchItem)
                {
                    return;
                }

                int expected = _watchInc;
                if (_insertProbeLogs >= InsertProbeLimit)
                {
                    return;
                }

                _insertProbeLogs++;
                P5Common.Info("cargo-insert item=" + itemId + " stack=" + stack + " inc=" + inc
                    + " expected=" + expected + " tick=" + Environment.TickCount);
            }
            catch (Exception)
            {
            }
        }

        private static void BeltStackInsertPrefix(int beltId, int offset, int itemId, int maxStack,
            ref int itemCount, ref int itemInc)
        {
            try
            {
                if (!P5Common.DebugOn) { return; }
                if (_watchItem <= 0 || itemId != _watchItem || _stackProbeLogs >= InsertProbeLimit)
                {
                    return;
                }

                _stackProbeLogs++;
                P5Common.Info("stack-insert2 belt=" + beltId + " off=" + offset + " item=" + itemId
                    + " max=" + maxStack + " count=" + itemCount + " inc=" + itemInc
                    + " expected=" + _watchInc + " tick=" + Environment.TickCount);
            }
            catch (Exception)
            {
            }
        }

        private static void InsertIntoPostfix(int itemId, byte itemCount, byte itemInc, byte remainInc)
        {
            try
            {
                if (!P5Common.DebugOn) { return; }
                if (_watchItem <= 0 || itemId != _watchItem || _insertProbeLogs >= InsertProbeLimit)
                {
                    return;
                }

                _insertProbeLogs++;
                P5Common.Info("insert-into item=" + itemId + " count=" + itemCount + " inc=" + itemInc
                    + " remain=" + remainInc + " expected=" + _watchInc + " tick=" + Environment.TickCount);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>One line per successful pick: ties the hand-off chain together (task-7 B1).</summary>
        private static void LogPickReturn(string source, int item, int stack, int inc, int result)
        {
            try
            {
                if (!P5Common.DebugOn) { return; }
                if (_returnProbeLogs >= ReturnProbeLimit)
                {
                    return;
                }

                if (_watchItem > 0 && _watchItem != item)
                {
                    return;
                }

                _returnProbeLogs++;
                P5Common.Info("pick-return src=" + source + " item=" + item + " stack=" + stack + " inc=" + inc
                    + " result=" + result + " eid=" + _ctxEntityId + " tick=" + Environment.TickCount);
            }
            catch (Exception)
            {
            }
        }

        private static int PatchTick(Harmony harmony, string name, Type[] args)
        {
            try
            {
                MethodInfo tick = FindMethod(typeof(InserterComponent), name, args);
                if (tick == null)
                {
                    WarnOnce("tick." + name, typeof(InserterComponent).Name + "." + name + " not found");
                    return 0;
                }

                MethodInfo prefix = FindStatic("TickPrefix");
                MethodInfo postfix = FindStatic("TickPostfix");
                if (prefix == null || postfix == null)
                {
                    WarnOnce("tick.handlers", "TickPrefix/TickPostfix not found");
                    return 0;
                }

                harmony.Patch(tick, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                return 1;
            }
            catch (Exception e)
            {
                WarnOnce("tick." + name, e.ToString());
                return 0;
            }
        }

        private static int PatchBelt(Harmony harmony)
        {
            int patched = 0;
            try
            {
                Type[] plainArgs =
                {
                    typeof(int), typeof(int), typeof(int), typeof(byte).MakeByRefType(), typeof(byte).MakeByRefType()
                };
                Type[] needsArgs =
                {
                    typeof(int), typeof(int), typeof(int), typeof(int[]), typeof(byte).MakeByRefType(),
                    typeof(byte).MakeByRefType()
                };

                MethodInfo plain = FindMethod(typeof(CargoPath), "TryPickItem", plainArgs);
                MethodInfo needs = FindMethod(typeof(CargoPath), "TryPickItem", needsArgs);
                MethodInfo plainPrefix = FindStatic("PathPickPrefix");
                MethodInfo needsPrefix = FindStatic("PathPickNeedsPrefix");

                if (plain == null || needs == null || plainPrefix == null || needsPrefix == null)
                {
                    WarnOnce("belt.targets", "CargoPath.TryPickItem overloads not found");
                    return 0;
                }

                harmony.Patch(plain, prefix: new HarmonyMethod(plainPrefix) { priority = Priority.High });
                harmony.Patch(needs, prefix: new HarmonyMethod(needsPrefix) { priority = Priority.High });
                patched = 2;
            }
            catch (Exception e)
            {
                WarnOnce("belt.apply", e.ToString());
            }

            return patched;
        }

        private static int PatchPickFrom(Harmony harmony)
        {
            try
            {
                Type[] args =
                {
                    typeof(uint), typeof(int), typeof(int), typeof(int[]), typeof(byte).MakeByRefType(),
                    typeof(byte).MakeByRefType()
                };
                MethodInfo pick = FindMethod(typeof(PlanetFactory), "PickFrom", args);
                MethodInfo prefix = FindStatic("PickFromPrefix");
                if (pick == null || prefix == null)
                {
                    WarnOnce("pickfrom.targets", "PlanetFactory.PickFrom(uint,...) not found");
                    return 0;
                }

                // High priority: this prefix must run before P0_PickFrom so that a blocked take
                // never consumes a pooled quality point on the assembler branch.
                harmony.Patch(pick, prefix: new HarmonyMethod(prefix) { priority = Priority.High });
                return 1;
            }
            catch (Exception e)
            {
                WarnOnce("pickfrom.apply", e.ToString());
                return 0;
            }
        }

        private static void Disable(string reason)
        {
            _armed = false;
            _reason = reason;
            WarnOnce("install", "NOT ARMED (vanilla sorter picks): " + reason);
        }

        private static MethodInfo FindMethod(Type type, string name, Type[] args)
        {
            return type.GetMethod(name, AnyInstance | BindingFlags.DeclaredOnly, null, args, null)
                ?? type.GetMethod(name, AnyInstance, null, args, null);
        }

        private static MethodInfo FindStatic(string name)
        {
            return typeof(P5_SorterFilter).GetMethod(name, AnyStatic);
        }

        // ------------------------------------------------------------------ sorter tick context

        private static void TickPrefix(ref InserterComponent __instance, PlanetFactory factory)
        {
            _ctxMin = 0;
            _ctxEntityId = 0;
            _ctxPlanetId = 0;

            if (!_armed || factory == null)
            {
                return;
            }

            try
            {
                int entityId = __instance.entityId;
                PlanetData planet = factory.planet;
                if (entityId <= 0 || planet == null)
                {
                    return;
                }

                int planetId = planet.id;
                _ctxEntityId = entityId;
                _ctxPlanetId = planetId;
                _tickCalls++;

                int min = QualityData.GetSorterMin(planetId, entityId);
                if (min <= 0)
                {
                    return;
                }

                _ctxMin = min;
                _tickMinHits++;
                if (_tickMinHits <= 3)
                {
                    Diag("tick e=" + entityId + " p=" + planetId + " min=" + min);
                }
            }
            catch (Exception)
            {
                _ctxMin = 0;
            }
        }

        private static void TickPostfix(ref InserterComponent __instance)
        {
            // Hand-off probe (task-7 B1): the watch item is the one our storage pick just handed
            // back. Printing the hand right after the tick shows whether the inserter kept the points
            // (itemInc) or whether they were already gone before InsertInto saw them.
            try
            {
                if (P5Common.DebugOn && _watchItem > 0 && __instance.itemId == _watchItem && _holdProbeLogs < HoldProbeLimit)
                {
                    _holdProbeLogs++;
                    P5Common.Info("inserter-hold eid=" + __instance.entityId + " item=" + __instance.itemId
                        + " stackCount=" + __instance.stackCount
                        + " itemCount=" + __instance.itemCount + " itemInc=" + __instance.itemInc
                        + " stage=" + __instance.stage + " expected=" + _watchInc);
                }
            }
            catch (Exception)
            {
            }

            _ctxMin = 0;
            _ctxEntityId = 0;
            _ctxPlanetId = 0;
        }

        // ------------------------------------------------------------------ belt source

        private static bool PathPickPrefix(CargoPath __instance, int index, int length, int filter,
            out byte stack, out byte inc, ref int __result)
        {
            return FilterBelt(__instance, index, length, filter, null, out stack, out inc, ref __result);
        }

        private static bool PathPickNeedsPrefix(CargoPath __instance, int index, int length, int filter,
            int[] needs, out byte stack, out byte inc, ref int __result)
        {
            return FilterBelt(__instance, index, length, filter, needs, out stack, out inc, ref __result);
        }

        /// <summary>
        /// Belt source. <paramref name="index"/> and <paramref name="length"/> are the exact vanilla
        /// arguments (CargoTraffic passes <c>belt.pivotOnPath + offset - 2</c> and 5), so the decoded
        /// window is byte-for-byte the window vanilla would have used.
        /// </summary>
        private static bool FilterBelt(CargoPath path, int index, int length, int filter, int[] needs,
            out byte stack, out byte inc, ref int result)
        {
            stack = 1;
            inc = 0;

            int min = _ctxMin;
            if (min <= 0)
            {
                _pathMin0++;
                return true;
            }

            _pathCalls++;
            try
            {
                if (path == null)
                {
                    return true;
                }

                byte[] buffer = path.buffer;
                CargoContainer container = path.cargoContainer;
                if (buffer == null || container == null || container.cargoPool == null)
                {
                    return true;
                }

                int bufferLength = path.pathLength;
                if (bufferLength <= 0)
                {
                    return true;
                }

                EnsureSlots();
                int count;
                int valid = 0;
                string seen = null;

                lock (buffer)
                {
                    count = QualityMath.DecodeBeltWindow(buffer, bufferLength, index, length,
                        _slotCargoIds, _slotPositions);
                    for (int i = 0; i < count; i++)
                    {
                        int cargoId = _slotCargoIds[i];
                        if (cargoId <= 0 || cargoId >= container.cargoPool.Length)
                        {
                            continue;
                        }

                        Cargo cargo = container.cargoPool[cargoId];
                        if (cargo.item <= 0 || cargo.stack <= 0)
                        {
                            continue;
                        }

                        int points = QualityMath.PerItemPoints(cargo.inc, cargo.stack);
                        _slotItems[valid] = cargo.item;
                        _slotIncs[valid] = points;
                        _slotStacks[valid] = cargo.stack;
                        _slotRawIncs[valid] = cargo.inc;
                        _slotPositions[valid] = _slotPositions[i];
                        _slotCargoIds[valid] = cargoId;
                        valid++;

                        if (P5Common.DebugOn && valid <= WindowSlots)
                        {
                            seen = (seen == null ? string.Empty : seen + ",")
                                + cargo.item + "x" + cargo.stack + "@" + cargo.inc + "(pt=" + points + ")";
                        }
                    }

                    _scanned += count;

                    if (valid <= 0)
                    {
                        // Vanilla would also find nothing in this window; keep it vanilla so the belt
                        // keeps moving (returning false here would just cost the sorter a retry).
                        return true;
                    }

                    int chosen = QualityMath.PlanSorterPick(_slotItems, _slotIncs, valid, filter, min, needs);
                    if (chosen < 0)
                    {
                        _blocked++;
                        _blockedBySource[SrcBelt]++;
                        result = 0;
                        DebugBySource(SrcBelt, "src=belt min=" + min + " idx=" + index + " len=" + bufferLength
                            + " cand=" + valid + " [" + (seen ?? "?") + "] -> blocked");
                        return false;
                    }

                    Cargo picked = container.cargoPool[_slotCargoIds[chosen]];
                    // RemoveCargoAtIndexDirect(markerIndex) is exactly the vanilla removal: it decodes
                    // the cargo id at marker+1..+4, clears the 10 cargo bytes at marker-4 and grows
                    // updateLen. The marker index we decoded is that same index.
                    path.RemoveCargoAtIndexDirect(_slotPositions[chosen]);
                    stack = (byte)_slotStacks[chosen];
                    inc = (byte)_slotRawIncs[chosen];
                    result = picked.item;
                }

                _picked++;
                _pickedBySource[SrcBelt]++;
                _watchItem = result;
                _watchInc = inc;
                LogPickReturn("belt", result, stack, inc, result);
                DebugBySource(SrcBelt, "src=belt min=" + min + " idx=" + index + " cand=" + valid
                    + " [" + (seen ?? "?") + "] -> pick item=" + result + " stack=" + stack + " inc=" + inc);
                return false;
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("sorterfilter.belt", e);
                stack = 1;
                inc = 0;
                return true;
            }
        }

        // ------------------------------------------------------------------ non-belt sources

        private static bool PickFromPrefix(PlanetFactory __instance, uint ioTargetTypedId, int filter,
            int[] needs, out byte stack, out byte inc, ref int __result)
        {
            stack = 1;
            inc = 0;

            int min = _ctxMin;
            if (min <= 0 || __instance == null)
            {
                _pickMin0++;
                return true;
            }

            _pickCalls++;
            try
            {
                int typeCode = (int)(ioTargetTypedId & 0xFF000000u);
                int id = (int)(ioTargetTypedId & 0xFFFFFFu);
                EFactoryIOTargetType type = (EFactoryIOTargetType)typeCode;

                switch (type)
                {
                    case EFactoryIOTargetType.Belt:
                        // Handled in FilterBelt with the exact vanilla window arguments.
                        return true;

                    case EFactoryIOTargetType.Storage:
                        return FilterStorage(__instance, id, filter, needs, min, out stack, out inc, ref __result);

                    case EFactoryIOTargetType.Assembler:
                        return FilterAssembler(__instance, id, min, out stack, out inc, ref __result);

                    case EFactoryIOTargetType.Ejector:
                        return FilterBullets(__instance, id, filter, needs, min, false, out stack, out inc, ref __result);

                    case EFactoryIOTargetType.Silo:
                        return FilterBullets(__instance, id, filter, needs, min, true, out stack, out inc, ref __result);

                    case EFactoryIOTargetType.Station:
                        _skipped++;
                        _skippedBySource[SrcStation]++;
                        {
                            if (_skippedBySource[SrcStation] <= 3)
                            {
                                Diag("source station skipped (vanilla TakeItem order cannot be reproduced), min=" + min);
                            }
                        }
                        return true;

                    case EFactoryIOTargetType.Lab:
                        _skipped++;
                        _skippedBySource[SrcLab]++;
                        {
                            if (_skippedBySource[SrcLab] <= 3)
                            {
                                Diag("source lab skipped (lab products carry no quality points), min=" + min);
                            }
                        }
                        return true;

                    case EFactoryIOTargetType.PowerGen:
                        _skipped++;
                        _skippedBySource[SrcPowerGen]++;
                        {
                            if (_skippedBySource[SrcPowerGen] <= 3)
                            {
                                Diag("source powergen fuel skipped (fuel has no quality points), min=" + min);
                            }
                        }
                        return true;

                    default:
                        return true;
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("sorterfilter.pickfrom", e);
                stack = 1;
                inc = 0;
                return true;
            }
        }

        /// <summary>
        /// Storage source. Replays the vanilla PickFrom(uint) storage branch - walk the
        /// topStorage -> previousStorage chain, lock each storage entity mutex, honour the
        /// lastEmptyItem cache and fall through the chain when a container has nothing - but only
        /// accepts a grid whose per-item points reach the threshold. When no container qualifies the
        /// take is blocked (result 0), which is the whole point of the filter: vanilla would hand
        /// out a below-threshold item here.
        /// </summary>
        private static bool FilterStorage(PlanetFactory factory, int storageId, int filter, int[] needs, int min,
            out byte stack, out byte inc, ref int result)
        {
            stack = 1;
            inc = 0;
            result = 0;

            FactoryStorage storagePool = factory.factoryStorage;
            if (storagePool == null || storagePool.storagePool == null || storageId <= 0)
            {
                return true;
            }

            // Vanilla: int storageId = entityPool[entityId].storageId; factoryStorage.storagePool[
            // storageId]. The id handed over here is the *entity* id, not the storage component
            // index - indexing the pool with it (v0.19.2) found an unrelated, empty container and
            // reported "grids=0".
            EntityData[] storageEntityPool = factory.entityPool;
            if (storageEntityPool == null || storageId >= storageEntityPool.Length)
            {
                return true;
            }

            int storageIndex = storageEntityPool[storageId].storageId;
            if (storageIndex <= 0 || storageIndex >= storagePool.storagePool.Length)
            {
                return true;
            }

            StorageComponent root = storagePool.storagePool[storageIndex];
            if (root == null)
            {
                return true;
            }

            Mutex[] mutexes = factory.entityMutexs;
            if (mutexes == null)
            {
                return true;
            }

            bool scannedAny = false;
            StorageComponent current = root.topStorage;
            while (current != null)
            {
                int entityId = current.entityId;
                if (entityId <= 0 || entityId >= mutexes.Length || mutexes[entityId] == null)
                {
                    return true;
                }

                bool took = false;
                lock (mutexes[entityId])
                {
                    // v0.19.2: always scan while a quality threshold is active. Vanilla's
                    // lastEmptyItem cache means "this container has nothing for this item filter",
                    // which is exactly what a below-threshold block looks like - honouring it made
                    // the sorter stop for good after one blocked attempt (lowering the threshold
                    // did nothing) and made a quality-only filter (filter == 0) never engage.
                    scannedAny = true;
                    int itemId;
                    int points;
                    int scanned;
                    string seen;
                    if (TryPickStorageComponent(factory, current, filter, needs, min,
                            out itemId, out points, out scanned, out seen))
                    {
                        stack = 1;
                        inc = (byte)Math.Min(points, 255);
                        result = itemId;
                        current.lastEmptyItem = -1;
                        took = true;
                        _picked++;
                        _pickedBySource[SrcStorage]++;
                        // Vanilla signals the container change after TakeTailItems; we edit the grid
                        // ourselves, so without this the open storage window kept showing the item
                        // that had already been moved to a belt until the next manual refresh.
                        current.NotifyStorageChange();
                        // v0.19.2 paired probe: remember what we handed back so the belt-insert
                        // postfix can print the very same pick on the other side of the funnel.
                        _watchItem = itemId;
                        _watchInc = inc;
                        LogPickReturn("storage", itemId, stack, inc, result);
                        if (P5Common.DebugOn && _pickProbeLogs < PickProbeLimit)
                        {
                            _pickProbeLogs++;
                            P5Common.Info("storage-pick storage=" + current.id + " item=" + itemId
                                + " pt=" + points + " inc=" + inc + " min=" + min
                                + " grids=" + scanned);
                        }

                        DebugBySource(SrcStorage, "src=storage storage=" + current.id + " min=" + min
                            + " grids=" + scanned + " [" + (seen ?? "none") + "] -> pick item=" + itemId
                            + " pt=" + points + " inc=" + inc);
                    }
                    else
                    {
                        // Deliberately do NOT poison lastEmptyItem here: the very next tick can
                        // qualify as soon as the threshold or the filter changes.
                        DebugBySource(SrcStorage, "src=storage storage=" + current.id + " min=" + min
                            + " grids=" + scanned + " [" + (seen ?? "none") + "] -> skip");
                    }

                    if (took)
                    {
                        return false;
                    }

                    if (current == root)
                    {
                        break;
                    }

                    current = current.previousStorage;
                }
            }

            if (!scannedAny)
            {
                // Every container was cached empty: vanilla would return 0 as well, so leave the
                // decision (and its counter-visible "take failed") to the vanilla code.
                return true;
            }

            _blocked++;
            _blockedBySource[SrcStorage]++;
            DebugBySource(SrcStorage, "src=storage storage=" + root.id + " min=" + min
                + " -> blocked (no grid reached " + min + " points)");
            return false;
        }

        /// <summary>
        /// One storage container: scans tail-to-head for a grid matching the item filter / care-needs
        /// whose per-item points reach the threshold, then takes exactly one item off that grid with
        /// the vanilla split_inc points math. Returns false - without touching anything - when no grid
        /// qualifies, so the caller can fall through to the next container exactly like vanilla.
        /// </summary>
        private static bool TryPickStorageComponent(PlanetFactory factory, StorageComponent storage, int filter,
            int[] needs, int min, out int itemId, out int points, out int scannedGrids, out string seen)
        {
            itemId = 0;
            points = 0;
            scannedGrids = 0;
            seen = null;

            StorageComponent.GRID[] grids = storage.grids;
            if (grids == null || grids.Length <= 0)
            {
                return false;
            }

            // Vanilla TakeTailItems: the last `bans` grids are reserved when the storage belongs to a
            // battle base, otherwise the whole array is available.
            bool useBan = false;
            EntityData[] entityPool = factory.entityPool;
            if (entityPool != null && storage.entityId > 0 && storage.entityId < entityPool.Length)
            {
                useBan = entityPool[storage.entityId].battleBaseId > 0;
            }

            int startIndex = useBan ? storage.size - storage.bans - 1 : storage.size - 1;
            if (startIndex >= grids.Length)
            {
                startIndex = grids.Length - 1;
            }

            // v0.19.2 diagnostic: a blocked report of "grids=0" says nothing about why - the item
            // filter, the care-needs list and the storage window all take part. Only sorters that
            // actually carry a quality threshold are dumped (a busy sorter on an empty chest would
            // otherwise swallow the whole log budget), and the dump is capped at 40 lines.
            if (min > 0 && _storageProbeLogs < StorageProbeLimit)
            {
                int nonEmpty = 0;
                int itemMatch = 0;
                int needMatch = 0;
                string sample = "none";
                for (int i = 0; i < grids.Length; i++)
                {
                    if (grids[i].itemId <= 0 || grids[i].count <= 0)
                    {
                        continue;
                    }

                    nonEmpty++;
                    if (filter == 0 || grids[i].itemId == filter)
                    {
                        itemMatch++;
                    }

                    if (NeedsMatch(needs, grids[i].itemId))
                    {
                        needMatch++;
                    }

                    if (nonEmpty <= 6)
                    {
                        sample = (sample == "none" ? string.Empty : sample + ",") + "#" + i + ":" + grids[i].itemId
                            + "x" + grids[i].count + "(inc=" + grids[i].inc + ",pt="
                            + QualityMath.PerItemPoints(grids[i].inc, grids[i].count) + ")";
                    }
                }

                // Only containers that actually hold something are interesting: a sorter idling on
                // an empty chest says nothing about the item/needs mismatch and used to eat the budget.
                if (nonEmpty > 0 && P5Common.DebugOn)
                {
                    _storageProbeLogs++;
                    P5Common.Info("storage-probe id=" + storage.id + " eid=" + storage.entityId
                        + " size=" + storage.size + " grids=" + grids.Length + " bans=" + storage.bans
                        + " useBan=" + (useBan ? "1" : "0") + " start=" + startIndex + " filter=" + filter
                        + " needs=" + (needs == null ? -1 : needs.Length) + " min=" + min + " nonEmpty=" + nonEmpty
                        + " itemMatch=" + itemMatch + " needMatch=" + needMatch + " [" + sample + "]");
                }
            }

            int found = -1;
            for (int i = startIndex; i >= 0; i--)
            {
                int gridItem = grids[i].itemId;
                int count = grids[i].count;
                if (gridItem <= 0 || count <= 0)
                {
                    continue;
                }

                if (filter != 0 && gridItem != filter)
                {
                    continue;
                }

                if (!NeedsMatch(needs, gridItem))
                {
                    continue;
                }

                scannedGrids++;
                int gridPoints = QualityMath.PerItemPoints(grids[i].inc, count);
                if (P5Common.DebugOn && scannedGrids <= 8)
                {
                    seen = (seen == null ? string.Empty : seen + ",") + gridItem + "x" + count
                        + "(pt=" + gridPoints + ")";
                }

                if (gridPoints >= min)
                {
                    found = i;
                    points = gridPoints;
                    break;
                }
            }

            if (found < 0)
            {
                return false;
            }

            int takenInc = QualityMath.SplitStorageInc(grids[found].inc, grids[found].count, 1,
                out int restInc, out int restCount);
            itemId = grids[found].itemId;
            grids[found].count = restCount;
            grids[found].inc = restInc;
            if (grids[found].count <= 0)
            {
                grids[found].itemId = 0;
                grids[found].inc = 0;
            }

            return true;
        }

        /// <summary>
        /// Assembler output mouth. The next product's quality points are peeked from the pooled
        /// product points (QualityData.GetPoolInc/GetPoolCount + the pure QualityMath.TakeOne, i.e.
        /// exactly what P0_PickFrom would hand out) so a below-threshold take can be blocked without
        /// consuming a point; otherwise P0_PickFrom runs as usual.
        /// </summary>
        private static bool FilterAssembler(PlanetFactory factory, int assemblerId, int min,
            out byte stack, out byte inc, ref int result)
        {
            stack = 1;
            inc = 0;

            FactorySystem system = factory.factorySystem;
            if (system == null || system.assemblerPool == null
                || assemblerId <= 0 || assemblerId >= system.assemblerPool.Length)
            {
                return true;
            }

            AssemblerComponent assembler = system.assemblerPool[assemblerId];
            if (assembler.id <= 0)
            {
                return true;
            }

            int[] produced = assembler.produced;
            if (produced == null)
            {
                return true;
            }

            bool hasProduct = false;
            for (int i = 0; i < produced.Length; i++)
            {
                if (produced[i] > 0)
                {
                    hasProduct = true;
                    break;
                }
            }

            if (!hasProduct)
            {
                return true;
            }

            int entityId = assembler.entityId;
            int planetId = factory.planetId;
            int poolInc = QualityData.GetPoolInc(planetId, entityId);
            int poolCount = QualityData.GetPoolCount(planetId, entityId);
            int perItem = QualityMath.TakeOne(poolInc, poolCount, Cargo.kIncLevelMax, out int _, out int _);

            int chosen = perItem >= min ? 1 : -1;
            if (chosen < 0)
            {
                _blocked++;
                _blockedBySource[SrcAssembler]++;
                result = 0;
                DebugBySource(SrcAssembler, "src=assembler e=" + entityId + " min=" + min
                    + " pool=" + poolInc + "/" + poolCount + " pt=" + perItem + " -> blocked");
                return false;
            }

            _pickedBySource[SrcAssembler]++;
            _picked++;
            DebugBySource(SrcAssembler, "src=assembler e=" + entityId + " min=" + min
                + " pool=" + poolInc + "/" + poolCount + " pt=" + perItem + " -> allow (P0_PickFrom takes it)");

            // Let P0_PickFrom run: it consumes one pooled point and returns the item with that inc.
            return true;
        }

        /// <summary>
        /// Ejector / silo bullet stock. bulletInc is the total points of all loaded bullets, so the
        /// per-bullet points are bulletInc / bulletCount (IL-verified TakeOneBulletUnsafe).
        /// </summary>
        private static bool FilterBullets(PlanetFactory factory, int poolId, int filter, int[] needs, int min,
            bool silo, out byte stack, out byte inc, ref int result)
        {
            stack = 1;
            inc = 0;

            FactorySystem system = factory.factorySystem;
            if (system == null)
            {
                return true;
            }

            int bulletId;
            int bulletCount;
            int bulletInc;
            int entityId;

            if (silo)
            {
                if (system.siloPool == null || poolId <= 0 || poolId >= system.siloPool.Length)
                {
                    return true;
                }

                SiloComponent siloComponent = system.siloPool[poolId];
                if (siloComponent.id <= 0)
                {
                    return true;
                }

                bulletId = siloComponent.bulletId;
                bulletCount = siloComponent.bulletCount;
                bulletInc = siloComponent.bulletInc;
                entityId = siloComponent.entityId;
            }
            else
            {
                if (system.ejectorPool == null || poolId <= 0 || poolId >= system.ejectorPool.Length)
                {
                    return true;
                }

                EjectorComponent ejector = system.ejectorPool[poolId];
                if (ejector.id < 0)
                {
                    return true;
                }

                bulletId = ejector.bulletId;
                bulletCount = ejector.bulletCount;
                bulletInc = ejector.bulletInc;
                entityId = ejector.entityId;
            }

            int minCount = silo ? 1 : 5; // vanilla: bulletCount > 5 (ejector) / > 1 (silo)
            if (bulletId <= 0 || bulletCount <= minCount)
            {
                return true;
            }

            if (filter != 0 && filter != bulletId)
            {
                return true;
            }

            if (!NeedsMatch(needs, bulletId))
            {
                return true;
            }

            int perItem = QualityMath.PerBulletInc(bulletInc, bulletCount);
            int source = silo ? SrcSilo : SrcEjector;
            if (perItem < min)
            {
                _blocked++;
                _blockedBySource[source]++;
                result = 0;
                DebugBySource(source, "src=" + (silo ? "silo" : "ejector") + " e=" + entityId + " min=" + min
                    + " bullets=" + bulletCount + " inc=" + bulletInc + " pt=" + perItem + " -> blocked");
                return false;
            }

            _pickedBySource[source]++;
            DebugBySource(source, "src=" + (silo ? "silo" : "ejector") + " e=" + entityId + " min=" + min
                + " bullets=" + bulletCount + " inc=" + bulletInc + " pt=" + perItem + " -> allow");
            return true;
        }

        private static bool NeedsMatch(int[] needs, int itemId)
        {
            if (needs == null)
            {
                return true;
            }

            int count = needs.Length < 6 ? needs.Length : 6;
            for (int i = 0; i < count; i++)
            {
                if (needs[i] == itemId)
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ state / diagnostics

        internal static void ResetState()
        {
            _picked = 0;
            _blocked = 0;
            _skipped = 0;
            _scanned = 0;
            _tickCalls = 0;
            _tickMinHits = 0;
            _pathCalls = 0;
            _pathMin0 = 0;
            _pickCalls = 0;
            _pickMin0 = 0;
            _diagLogs = 0;
            _debugLogs = 0;
            Array.Clear(_debugBySource, 0, _debugBySource.Length);
            Array.Clear(_pickedBySource, 0, _pickedBySource.Length);
            Array.Clear(_blockedBySource, 0, _blockedBySource.Length);
            Array.Clear(_skippedBySource, 0, _skippedBySource.Length);
        }

        internal static string Describe()
        {
            if (!_armed)
            {
                return "NOT ARMED (vanilla sorter picks, reason: " + Reason + ")";
            }

            return "armed (picked=" + _picked + " blocked=" + _blocked + " skipped=" + _skipped
                + " ticks=" + _tickCalls + " tickMin=" + _tickMinHits
                + " pathCalls=" + _pathCalls + " pathMin0=" + _pathMin0
                + " pickCalls=" + _pickCalls + " pickMin0=" + _pickMin0
                + " scanned=" + _scanned + " bySource=[" + SourceCounters() + "])";
        }

        private static string SourceCounters()
        {
            string text = string.Empty;
            for (int i = 0; i < SourceCount; i++)
            {
                if (_pickedBySource[i] == 0 && _blockedBySource[i] == 0 && _skippedBySource[i] == 0)
                {
                    continue;
                }

                if (text.Length > 0)
                {
                    text += " ";
                }

                text += QualityMath.SorterSourceName(i) + ":" + _pickedBySource[i] + "/" + _blockedBySource[i]
                    + "/" + _skippedBySource[i];
            }

            return text.Length == 0 ? "none" : text;
        }

        private static void EnsureSlots()
        {
            if (_slotCargoIds == null)
            {
                _slotCargoIds = new int[WindowSlots];
                _slotPositions = new int[WindowSlots];
                _slotItems = new int[WindowSlots];
                _slotIncs = new int[WindowSlots];
                _slotStacks = new int[WindowSlots];
                _slotRawIncs = new int[WindowSlots];
            }
        }

        private static void Diag(string line)
        {
            if (_diagLogs >= MaxDiagLogs)
            {
                return;
            }

            if (_diagLogs >= AlwaysDiagLogs && !P5Common.DebugOn)
            {
                return;
            }

            _diagLogs++;
            P5Common.Info(Tag + line);
        }

        private static void DebugBySource(int source, string line)
        {
            if (!P5Common.DebugOn || source < 0 || source >= SourceCount)
            {
                return;
            }

            if (_debugBySource[source] >= MaxDebugLogs || _debugLogs >= MaxDebugLogs * 2)
            {
                return;
            }

            _debugBySource[source]++;
            _debugLogs++;
            P5Common.Info(Tag + line);
        }

        private static void WarnOnce(string key, string detail)
        {
            QualityEnv.WarnOnce(Tag + key, detail);
        }
    }
}
