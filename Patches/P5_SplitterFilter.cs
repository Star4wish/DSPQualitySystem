using System;
using System.Reflection;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.16.1 (C-2, task-18 rework): per-output-port quality filter for the four-way splitter.
    ///
    /// WHY THE v0.13 BEHAVIOUR WAS REPLACED
    ///   v0.13.0 only permuted SplitterComponent.input0..3 so that the vanilla first-fit loop spent
    ///   the highest-quality cargo first. Players found that unusable: it cannot say "quality cargo
    ///   leaves through this port, plain cargo through that one". The vanilla splitter has exactly
    ///   one global outFilter (SplitterComponent.outFilter, written by
    ///   UISplitterWindow.OnItemPickerReturn) and no per-port filtering at all, so the only way to
    ///   get the requested behaviour is to take over the allocation step.
    ///
    /// WHAT THIS PATCH DOES (v0.17.1)
    ///   Prefix on CargoTraffic.UpdateSplitter(ref SplitterComponent) (asm 82854) and its parallel
    ///   twin UpdateSplitterAsync(ref SplitterComponent) (asm 83208). Whenever the splitter carries
    ///   at least one per-port rule the whole allocation step is ours: same gather order, same
    ///   InsertCargoAtHeadDirect / InputAlternate / OutputAlternate calls, same "output0 has to be
    ///   connected" precondition, but the destination is chosen by QualityMath.PlanSplitterRoute
    ///   (ports whose rule accepts the cargo, strictest minimum first) with a fallback walk over
    ///   the rule-less output ports only.
    ///
    ///   v0.17.1 CRITICAL FIX: the vanilla cargo inc is the TOTAL power pool of a whole stack
    ///   (merging stacks does cargoPool[id].inc += inc, Assembly-CSharp.cs:28583) while rules and
    ///   the item badge are per-item. v0.17.0 compared the raw total against the per-item minimum,
    ///   so a 2-piece stack with 1 stray point (badge shows 0) passed a ">= 1 point" rule and the
    ///   player saw plain cargo leak through a quality port. Every comparison now goes through
    ///   QualityMath.PerItemPoints(cargo.inc, cargo.stack).
    ///
    ///   v0.17.1 also removed the last way vanilla could feed a reserved port: cargo that no rule
    ///   claims walks only the rule-less output ports (QualityMath.PlanSplitterFallback), never the
    ///   vanilla global outFilter first-fit. The vanilla outFilter is still written by the vanilla
    ///   item picker for its own icon display, but it no longer routes anything while rules exist.
    ///
    /// WHEN IT STEPS ASIDE (returns true -> vanilla runs untouched)
    ///   - the splitter has no rule at all (the overwhelmingly common case: zero overhead, the
    ///     result is byte-for-byte the vanilla tick);
    ///   - anything throws -> vanilla runs and one warning is logged.
    /// Everything else (input priority, pending priority presets, a storage on top) is handled here:
    /// pending presets are applied with the vanilla SplitterComponent.CheckPriorityPreset() first, and
    /// the storage round-trip branch of a topId splitter is not reimplemented, so cargo that cannot
    /// leave through an output stays on the belt instead of being pushed into the top storage
    /// (documented limitation; a standard floor splitter has topId == 0).
    ///
    /// THREADING
    ///   UpdateSplitterAsync runs on worker threads (FactorySplitterGameTick_Parallel, asm 259097),
    ///   so every scratch buffer below is [ThreadStatic] and the side table read goes through
    ///   QualityData.QualityFilters' lock-free path. No Unity API is touched.
    /// </summary>
    internal static class P5_SplitterFilter
    {
        internal const string Tag = "[QSystem][splitterfilter] ";
        private const int MaxDebugLogs = 20;

        private static bool _armed;
        private static bool _warned;
        private static string _reason = "not applied";
        private static int _patched;
        private static int _routed;
        private static int _debugLogs;

        // v0.17.1 diagnostic counters (DEV only): prove which branch handles a splitter on a live
        // save, and count every time the vanilla storage round-trip branch of a topId splitter is
        // skipped (documented limitation).
        private const int MaxDiagLogs = 80;
        private static int _diagLogs;
        private static int _routeLogs;
        private static int _fallbackLogs;
        private static int _blockedLogs;
        private static int _skipPreset;
        private static int _skipNoRule;
        private static int _takeovers;
        private static int _fallbacks;
        private static int _unrouted;
        private static int _noOps;
        private static int _topStorageSkips;

        [ThreadStatic] private static int[] _portRules;
        [ThreadStatic] private static int[] _portBelts;
        [ThreadStatic] private static int[] _outBelts;
        [ThreadStatic] private static int[] _candidates;
        [ThreadStatic] private static CargoPath[] _inPaths;
        [ThreadStatic] private static int[] _inCargo;
        [ThreadStatic] private static int[] _inIndex;

        private static int[] PortRules { get { return _portRules ?? (_portRules = new int[QualityMath.SplitterPortCount]); } }
        private static int[] PortBelts { get { return _portBelts ?? (_portBelts = new int[QualityMath.SplitterPortCount]); } }
        private static int[] OutBelts { get { return _outBelts ?? (_outBelts = new int[QualityMath.SplitterPortCount]); } }
        private static int[] Candidates { get { return _candidates ?? (_candidates = new int[QualityMath.SplitterPortCount]); } }
        private static CargoPath[] InPaths { get { return _inPaths ?? (_inPaths = new CargoPath[QualityMath.SplitterPortCount]); } }
        private static int[] InCargo { get { return _inCargo ?? (_inCargo = new int[QualityMath.SplitterPortCount]); } }
        private static int[] InIndex { get { return _inIndex ?? (_inIndex = new int[QualityMath.SplitterPortCount]); } }

        internal static bool Armed { get { return _armed; } }
        internal static string Reason { get { return _reason; } }

        internal static string Describe()
        {
            if (!_armed)
            {
                return "NOT ARMED (" + _reason + ")";
            }

            return "armed (methods=" + _patched + ", routed=" + _routed + ", took=" + _takeovers
                + ", fallback=" + _fallbacks + ", unrouted=" + _unrouted + ", noop=" + _noOps
                + ", skip[presetApplied=" + _skipPreset + ",noRule=" + _skipNoRule
                + ",topStorage=" + _topStorageSkips + "])";
        }

        private static void Diag(string message)
        {
            if (!QualityConfig.DevMode || _diagLogs >= MaxDiagLogs)
            {
                return;
            }

            _diagLogs++;
            QualityEnv.LogInfo(Tag + message);
        }

        private static string SpState(ref SplitterComponent sp)
        {
            return "e=" + sp.entityId + " inp=" + sp.input0 + "," + sp.input1 + "," + sp.input2 + "," + sp.input3
                + " out=" + sp.output0 + "," + sp.output1 + "," + sp.output2 + "," + sp.output3
                + " belts=" + sp.beltA + "," + sp.beltB + "," + sp.beltC + "," + sp.beltD
                + " outFilter=" + sp.outFilter + " outPri=" + sp.outPriority
                + " inPri=" + sp.inPriority + " presets=" + sp.prioritySlotPresets + " top=" + sp.topId;
        }

        internal static void ResetState()
        {
            _warned = false;
        }

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
            {
                _reason = "harmony null";
                return;
            }

            if (!QualityConfig.SplitterQualityFilterEnabled)
            {
                _reason = "feature off";
                QualityEnv.LogInfo(Tag + "NOT ARMED (feature off)");
                return;
            }

            try
            {
                MethodInfo prefix = typeof(P5_SplitterFilter).GetMethod(
                    "UpdatePrefix", BindingFlags.Static | BindingFlags.NonPublic);
                if (prefix == null)
                {
                    _reason = "prefix method missing";
                    QualityEnv.WarnOnce("splitterfilter.apply",
                        Tag + "NOT ARMED (prefix method missing): splitter quality filter stays vanilla");
                    return;
                }

                int installed = 0;
                installed += PatchOne(harmony, prefix, "UpdateSplitter");
                installed += PatchOne(harmony, prefix, "UpdateSplitterAsync");
                _patched = installed;
                if (installed <= 0)
                {
                    _reason = "anchor not found";
                    QualityEnv.WarnOnce("splitterfilter.apply",
                        Tag + "NOT ARMED (CargoTraffic.UpdateSplitter anchor not found): splitter quality filter stays vanilla");
                    return;
                }

                _armed = true;
                _reason = "armed";
                QualityEnv.LogInfo(Tag + "patch registered: CargoTraffic.UpdateSplitter"
                    + (installed > 1 ? " + UpdateSplitterAsync" : string.Empty)
                    + " [per-port quality filter]");
            }
            catch (Exception e)
            {
                _armed = false;
                _reason = "apply failed";
                QualityEnv.WarnOnce("splitterfilter.apply", e);
            }
        }

        private static int PatchOne(Harmony harmony, MethodInfo prefix, string name)
        {
            try
            {
                MethodInfo target = typeof(CargoTraffic).GetMethod(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new[] { typeof(SplitterComponent).MakeByRefType() },
                    null);
                if (target == null)
                {
                    return 0;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                return 1;
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("splitterfilter.patch." + name, e);
                return 0;
            }
        }

        /// <summary>
        /// Prefix (v0.17.1): returns false - so the vanilla UpdateSplitter never runs - whenever the
        /// splitter carries at least one per-port rule. Vanilla keeps full ownership only when there
        /// is no rule at all, or when anything in here throws.
        /// </summary>
        private static bool UpdatePrefix(CargoTraffic __instance, ref SplitterComponent __0)
        {
            try
            {
                if (__instance == null)
                {
                    return true;
                }

                SplitterComponent sp = __0;
                if (sp.id <= 0 || sp.entityId <= 0)
                {
                    return true;
                }

                PlanetData planet = __instance.planet;
                if (planet == null)
                {
                    return true;
                }

                int planetId = planet.id;
                if (!QualityData.EntityHasSplitterRule(planetId, sp.entityId))
                {
                    _skipNoRule++;
                    return true;
                }

                // A pending vanilla priority preset has to be materialised exactly like vanilla does
                // at the top of UpdateSplitter, otherwise the input/output rotation read below would
                // be stale. (The vanilla presets only reorder slots; routing still obeys the rules.)
                if (sp.prioritySlotPresets != 0)
                {
                    _skipPreset++;
                    sp.CheckPriorityPreset();
                    Diag("apply-preset " + SpState(ref sp));
                }

                Route(__instance, planetId, ref sp);
                _takeovers++;
                __0 = sp;
                return false;
            }
            catch (Exception e)
            {
                WarnOnce("splitterfilter.update", e);
                return true;
            }
        }

        /// <summary>
        /// Reimplements the vanilla allocation loop with rule-aware destination selection. Never hands
        /// control back to vanilla: a cargo either moves through a port its rule approves, moves
        /// through a rule-less output port (vanilla first-fit order), or stays on its belt.
        /// </summary>
        private static void Route(CargoTraffic traffic, int planetId, ref SplitterComponent sp)
        {
            // Vanilla preconditions: the vanilla loop does not run at all without input0 (and, for a
            // plain floor splitter, without output0). Replicating them keeps "when is this splitter
            // idle" byte-for-byte identical to vanilla.
            if (sp.topId == 0)
            {
                if (sp.input0 == 0 || sp.output0 == 0)
                {
                    _noOps++;
                    return;
                }
            }
            else
            {
                // The vanilla storage round-trip branch (topId != 0) is not reimplemented: cargo that
                // cannot leave through an output stays on the belt instead of being pushed into the
                // top storage. Documented limitation - a standard floor splitter has topId == 0.
                _topStorageSkips++;
                if (sp.input0 == 0 && sp.output0 == 0)
                {
                    _noOps++;
                    return;
                }
            }

            int[] rules = PortRules;
            int[] belts = PortBelts;
            int[] outs = OutBelts;
            int[] candidates = Candidates;
            CargoPath[] paths = InPaths;
            int[] cargos = InCargo;
            int[] indexes = InIndex;

            belts[0] = sp.beltA;
            belts[1] = sp.beltB;
            belts[2] = sp.beltC;
            belts[3] = sp.beltD;
            outs[0] = sp.output0;
            outs[1] = sp.output1;
            outs[2] = sp.output2;
            outs[3] = sp.output3;

            for (int port = 0; port < QualityMath.SplitterPortCount; port++)
            {
                rules[port] = QualityData.GetSplitterRule(planetId, sp.entityId, port);
            }

            // Same gather order as vanilla: input0..input3, rear cargo only.
            int count = 0;
            count = Gather(traffic, sp.input0, 0, paths, cargos, indexes, count);
            count = Gather(traffic, sp.input1, 1, paths, cargos, indexes, count);
            count = Gather(traffic, sp.input2, 2, paths, cargos, indexes, count);
            count = Gather(traffic, sp.input3, 3, paths, cargos, indexes, count);
            if (count <= 0)
            {
                _noOps++;
                return;
            }

            CargoContainer container = traffic.container;
            if (container == null || container.cargoPool == null)
            {
                _noOps++;
                return;
            }

            if (QualityConfig.DevMode && _diagLogs < MaxDiagLogs && _routeLogs < 6)
            {
                _routeLogs++;
                string gathered = string.Empty;
                for (int i = 0; i < count; i++)
                {
                    int cid = cargos[i];
                    if (cid > 0 && cid < container.cargoPool.Length)
                    {
                        Cargo c = container.cargoPool[cid];
                        gathered += " [" + c.item + "x" + c.stack + "@" + c.inc
                            + "/" + QualityMath.PerItemPoints(c.inc, c.stack) + "pt]";
                    }
                }

                Diag("route " + SpState(ref sp) + " rules=" + rules[0] + "," + rules[1] + ","
                    + rules[2] + "," + rules[3] + " cargos=" + count + ":" + gathered);
            }

            int routed = 0;
            for (int i = 0; i < count; i++)
            {
                int cargoId = cargos[i];
                if (cargoId <= 0 || cargoId >= container.cargoPool.Length)
                {
                    continue;
                }

                Cargo cargo = container.cargoPool[cargoId];
                int points = QualityMath.PerItemPoints(cargo.inc, cargo.stack);
                int plan = QualityMath.PlanSplitterRoute(cargo.item, points, rules, belts, outs, candidates);
                bool moved = false;
                if (plan > 0)
                {
                    // A rule claims this cargo: it may only leave through one of those reserved
                    // ports. When they are all full the cargo waits (quality cargo back-pressure)
                    // instead of spilling into a plain port.
                    for (int k = 0; k < plan; k++)
                    {
                        if (TryMoveToSlot(traffic, ref sp, candidates[k], belts, outs, paths[i], indexes[i]))
                        {
                            moved = true;
                            break;
                        }
                    }

                    if (!moved)
                    {
                        _unrouted++;
                        if (_blockedLogs < 20)
                        {
                            _blockedLogs++;
                            Diag("blocked e=" + sp.entityId + " item=" + cargo.item + " inc=" + cargo.inc
                                + " stack=" + cargo.stack + " pts=" + points + " plan=" + plan);
                        }
                    }
                }
                else
                {
                    // No rule claims this cargo: walk the rule-less output ports in vanilla first-fit
                    // order. Reserved ports are excluded, so the vanilla global outFilter (which the
                    // vanilla item picker still writes for its own icon) can never feed one of them.
                    int fallback = QualityMath.PlanSplitterFallback(rules, belts, outs, candidates);
                    _fallbacks++;
                    for (int k = 0; k < fallback; k++)
                    {
                        if (TryMoveToSlot(traffic, ref sp, candidates[k], belts, outs, paths[i], indexes[i]))
                        {
                            moved = true;
                            break;
                        }
                    }

                    if (!moved)
                    {
                        _unrouted++;
                    }

                    if (_fallbackLogs < 20)
                    {
                        _fallbackLogs++;
                        Diag("fallback e=" + sp.entityId + " item=" + cargo.item + " inc=" + cargo.inc
                            + " stack=" + cargo.stack + " pts=" + points + " outFilter=" + sp.outFilter
                            + " plainPorts=" + fallback + " moved=" + moved);
                    }
                }

                if (moved)
                {
                    routed++;
                }
            }

            if (routed > 0)
            {
                _routed += routed;
                if (QualityConfig.DevMode && _debugLogs < MaxDebugLogs)
                {
                    _debugLogs++;
                    QualityEnv.LogInfo(Tag + "routed p=" + planetId + " e=" + sp.entityId
                        + " cargos=" + count + " moved=" + routed
                        + " rules=" + rules[0] + "," + rules[1] + "," + rules[2] + "," + rules[3]);
                }
            }
        }

        /// <summary>A physical port a rule approved for this cargo, identified by belt id.</summary>
        private static bool TryMoveToSlot(CargoTraffic traffic, ref SplitterComponent sp,
            int port, int[] belts, int[] outs, CargoPath inPath, int inputIndex)
        {
            if (port < 0 || port >= QualityMath.SplitterPortCount)
            {
                return false;
            }

            int beltId = belts[port];
            int slot = QualityMath.FindOutputIndex(outs, beltId);
            if (slot < 0)
            {
                return false;
            }

            return TryMoveVanilla(traffic, ref sp, beltId, slot, inPath, inputIndex);
        }

        private static bool TryMoveVanilla(CargoTraffic traffic, ref SplitterComponent sp,
            int outputBelt, int slot, CargoPath inPath, int inputIndex)
        {
            if (inPath == null || outputBelt == 0)
            {
                return false;
            }

            CargoPath outPath = PathOf(traffic, outputBelt, sp);
            if (outPath == null || outPath.pathLength <= 10)
            {
                return false;
            }

            int blank = outPath.TestBlankAtHead();
            if (blank < 0)
            {
                return false;
            }

            int picked = inPath.TryPickCargoAtEnd();
            if (picked < 0)
            {
                return false;
            }

            outPath.InsertCargoAtHeadDirect(picked, blank);
            sp.InputAlternate(inputIndex);
            sp.OutputAlternate(slot);
            return true;
        }

        private static int Gather(CargoTraffic traffic, int beltId, int slot,
            CargoPath[] paths, int[] cargos, int[] indexes, int count)
        {
            if (beltId == 0 || count >= QualityMath.SplitterPortCount)
            {
                return count;
            }

            CargoPath path = PathOf(traffic, beltId, default(SplitterComponent));
            if (path == null)
            {
                return count;
            }

            int cargoId = path.GetCargoIdAtRear();
            if (cargoId <= 0)
            {
                return count;
            }

            paths[count] = path;
            cargos[count] = cargoId;
            indexes[count] = slot;
            return count + 1;
        }

        /// <summary>
        /// Safe GetCargoPath wrapper: the vanilla guard in CargoTraffic.GetCargoPath is written as
        /// "id &lt;= 0 &amp;&amp; id &gt;= pathCursor" (an &amp;&amp; typo), so callers must validate the
        /// ids themselves before using it.
        /// </summary>
        private static CargoPath PathOf(CargoTraffic traffic, int beltId, SplitterComponent sp)
        {
            return PathOf(traffic, beltId, sp, null);
        }

        private static CargoPath PathOf(CargoTraffic traffic, int beltId, SplitterComponent sp, CargoPath known)
        {
            if (known != null)
            {
                return known;
            }

            if (traffic == null || traffic.beltPool == null || beltId <= 0 || beltId >= traffic.beltPool.Length)
            {
                return null;
            }

            BeltComponent belt = traffic.beltPool[beltId];
            if (belt.id != beltId || belt.segPathId <= 0)
            {
                return null;
            }

            if (traffic.pathPool == null || belt.segPathId >= traffic.pathPool.Length)
            {
                return null;
            }

            return traffic.GetCargoPath(belt.segPathId);
        }

        private static void WarnOnce(string key, Exception e)
        {
            if (_warned)
            {
                return;
            }

            _warned = true;
            QualityEnv.WarnOnce(key, e);
        }
    }
}
