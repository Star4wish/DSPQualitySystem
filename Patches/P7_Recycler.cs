using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.19.0 (task-2 B, design A): quality recycler implemented as the <b>inverse catalyst feed</b>
    /// of the vanilla spray coater (item 2313).
    /// <para>
    /// Design A (agreed with the lead after field test v1 produced no output):
    /// <list type="number">
    /// <item>The recycler destroys scrap cargo that carries quality points and feeds it through the
    /// coater's <b>spray-catalyst input port</b> (<c>incBeltId</c>), i.e. the same belt the vanilla
    /// body sucks 1141/1142/1143 from.</item>
    /// <item>Every 4 harvested points add one <b>spray use</b> to the tank (<c>incCount</c>). Nothing
    /// is ever put back on a belt, so the old "dropped Mk.III bottles have to survive the vanilla
    /// catalyst picker" problem disappears.</item>
    /// <item>The coater keeps spraying normally: the tank it fills is the same tank vanilla sprays
    /// items with, so a recycler is just a coater fed with scrap instead of catalysts.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Why a <b>postfix</b> and not a prefix takeover: the vanilla body has to keep running, otherwise
    /// the tank it accumulates would never spray and the machine would stop consuming anything. A
    /// postfix is safe here because we never place 1143 on the belt any more, so the vanilla catalyst
    /// picker (<c>path.TryPickItem(index-2, 5, incItemId, ...)</c>) can never eat our output.
    /// </para>
    /// <para>
    /// Vanilla anchor (verified with il.exe against the installed Assembly-CSharp.dll,
    /// <c>SpraycoaterComponent.InternalUpdate(CargoTraffic, AnimData[], int[])</c>):
    /// <code>
    /// 0x000E  if (incBeltId &lt;= 0) goto spray_branch        ; catalyst feed only when a belt is wired
    /// 0x001A  if (incCount + extraIncCount &gt;= incCapacity) goto end
    /// 0x0032  belt = beltPool[incBeltId]; path = GetCargoPath(belt.segPathId)
    /// 0x0189  path.TryPickItem(belt.segIndex + belt.segPivotOffset - 2, 5, incItemId, out stack, out inc)
    /// </code>
    /// <c>CargoTraffic.SpraycoaterGameTick</c> calls <c>InternalUpdate</c> for every coater on the
    /// planet on every tick (<c>spraycoaterPool[i].id == i</c>), so the postfix runs even when the
    /// machine is idle.
    /// </para>
    /// <para>
    /// The probe line is deliberately wide: field test v1 failed without leaving a single log line, so
    /// every state change of a coater (belt wired/unwired, brand, empty tank) and every 200th call
    /// prints <c>[QSystem][recycler][probe] ...</c> with the actual cargo seen at the feed slot.
    /// </para>
    /// </summary>
    internal static class P7_Recycler
    {
        internal const string Tag = "[QSystem][recycler]";

        /// <summary>Vanilla Mk.III spray item; the tank brand we create when the tank is empty.</summary>
        internal const int SprayItemMk3 = 1143;

        /// <summary>Catalyst items the vanilla body understands. They are never harvested: they are
        /// the sprayer's own fuel, and 1143 is also what we accumulate as spray uses.</summary>
        private static readonly int[] CatalystItems = { 1141, 1142, 1143 };

        /// <summary>Vanilla scans five belt slots forward from the feed slot.</summary>
        private const int ScanWindow = 5;

        /// <summary>Vanilla takes catalysts two slots in front of the belt pin.</summary>
        private const int BeltReadOffset = 2;

        /// <summary>Ticks between orphan-account sweeps (matches the mod's other prune cadence).</summary>
        private const int PruneEvery = 1200;

        /// <summary>Hard ceiling for unspent points per recycler (600 spray uses worth). A guard
        /// against a full tank silently eating the player's items forever.</summary>
        internal const int MaxAccountPoints = 600 * 4;

        // ---- tunables (wired from QualityConfig by Plugin.cs; safe defaults here) ----
        internal static bool RecyclerEnabledValue = true;
        internal static int MinPerItemValue = QualityRecyclerMath.DefaultMinPerItem;
        internal static int PointsPerSprayValue = QualityRecyclerMath.PointsPerSpray;

        private static bool _armed;
        private static string _reason = "not attempted";
        private static MethodInfo _target;

        private static long _harvested;
        private static long _items;
        private static long _pointsIn;
        private static long _spraysOut;
        private static long _lowSkipped;
        private static long _tankFull;
        private static long _probes;
        private static long _pruned;

        private static int _logged;
        private static int _tickCounter;
        private static int _currentPlanetId = -1;

        private static readonly Dictionary<int, int> ProbeCalls = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> ProbeSignature = new Dictionary<int, int>();

        internal static bool Armed
        {
            get { return _armed; }
        }

        /// <summary>Patch entry point, called by Plugin.cs.</summary>
        internal static void Apply(Harmony harmony)
        {
            _armed = false;
            _reason = "not attempted";
            try
            {
                if (harmony == null)
                {
                    _reason = "null harmony";
                    P5Common.WarnOnce("recycler.apply", "null harmony");
                    return;
                }

                MethodInfo target = ResolveTarget();
                if (target == null)
                {
                    _reason = "SpraycoaterComponent.InternalUpdate not found";
                    P5Common.WarnOnce("recycler.apply", "SpraycoaterComponent.InternalUpdate not found");
                    return;
                }

                bool instanceIsValueType = typeof(SpraycoaterComponent).IsValueType;
                string postfixName = instanceIsValueType ? "InternalUpdatePostfixValue" : "InternalUpdatePostfixRef";
                MethodInfo postfix = typeof(P7_Recycler).GetMethod(postfixName, BindingFlags.Static | BindingFlags.NonPublic);
                if (postfix == null)
                {
                    _reason = postfixName + " not found";
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                _target = target;
                _armed = true;
                _reason = "patched " + target.DeclaringType.Name + "." + target.Name;
                P5Common.Info(Tag + " " + Describe());
            }
            catch (Exception ex)
            {
                _armed = false;
                _reason = ex.GetType().Name + ": " + ex.Message;
                P5Common.WarnOnce("recycler.apply", ex.ToString());
            }
        }

        private static MethodInfo ResolveTarget()
        {
            try
            {
                MethodInfo[] methods = typeof(SpraycoaterComponent).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo candidate = methods[i];
                    if (candidate.Name != "InternalUpdate")
                    {
                        continue;
                    }

                    ParameterInfo[] args = candidate.GetParameters();
                    if (args.Length != 3)
                    {
                        continue;
                    }

                    if (args[0].ParameterType == typeof(CargoTraffic))
                    {
                        return candidate;
                    }
                }
            }
            catch (Exception ex)
            {
                P5Common.WarnOnce("recycler.resolve", ex.ToString());
            }

            return null;
        }

        // ---- Harmony entry points -------------------------------------------------------------

        private static void InternalUpdatePostfixValue(ref SpraycoaterComponent __instance, CargoTraffic __0)
        {
            try
            {
                After(ref __instance, __0);
            }
            catch (Exception ex)
            {
                P5Common.WarnOnce("recycler.tick", ex.ToString());
            }
        }

        private static void InternalUpdatePostfixRef(SpraycoaterComponent __instance, CargoTraffic __0)
        {
            SpraycoaterComponent copy = __instance;
            try
            {
                After(ref copy, __0);
            }
            catch (Exception ex)
            {
                P5Common.WarnOnce("recycler.tick", ex.ToString());
            }

            __instance = copy;
        }

        /// <summary>Recycler tick. Runs after the vanilla body has finished.</summary>
        private static void After(ref SpraycoaterComponent comp, CargoTraffic traffic)
        {
            if (!_armed || !RecyclerEnabledValue || traffic == null)
            {
                return;
            }

            if (comp.id <= 0 || comp.entityId <= 0)
            {
                return;
            }

            int planetId = ResolvePlanetId(traffic);
            if (planetId < 0)
            {
                return;
            }

            _currentPlanetId = planetId;

            // Orphan-account sweep (self-throttled by _tickCounter).
            Prune(traffic, planetId);

            // Harmony may hand us a struct copy depending on the runtime; always rebase on the pool
            // entry so the vanilla writes are not lost when we write the component back.
            Refresh(traffic, ref comp);

            int compId = comp.entityId;
            int room = QualityRecyclerMath.FreeRoom(comp.incCapacity, comp.incCount, comp.extraIncCount);

            if (comp.incBeltId <= 0)
            {
                Probe(ref comp, compId, room, "idle", "noIncBelt", default(Cargo), false, -1);
                return;
            }

            if (traffic.beltPool == null || comp.incBeltId >= traffic.beltPool.Length)
            {
                Probe(ref comp, compId, room, "idle", "badBelt", default(Cargo), false, -1);
                return;
            }

            var belt = traffic.beltPool[comp.incBeltId];
            if (belt.id != comp.incBeltId)
            {
                Probe(ref comp, compId, room, "idle", "noBelt", default(Cargo), false, -1);
                return;
            }

            CargoPath path = null;
            try
            {
                path = traffic.GetCargoPath(belt.segPathId);
            }
            catch (Exception ex)
            {
                P5Common.WarnOnce("recycler.path", ex.ToString());
            }

            if (path == null)
            {
                Probe(ref comp, compId, room, "idle", "noPath", default(Cargo), false, -1);
                return;
            }

            int inputIndex = belt.segIndex + belt.segPivotOffset - BeltReadOffset;
            if (inputIndex < 0)
            {
                inputIndex = 0;
            }

            Cargo cargo = default(Cargo);
            int cargoId;
            int offset;
            bool hasCargo = false;
            try
            {
                byte[] buffer = path.buffer;
                if (buffer != null && inputIndex < buffer.Length)
                {
                    hasCargo = path.GetCargoAtIndex(inputIndex, out cargo, out cargoId, out offset);
                }
            }
            catch (Exception ex)
            {
                P5Common.WarnOnce("recycler.read", ex.ToString());
            }

            if (!hasCargo)
            {
                Probe(ref comp, compId, room, "idle", "noCargo", default(Cargo), false, inputIndex);
                return;
            }

            if (cargo.item <= 0 || cargo.stack <= 0)
            {
                Probe(ref comp, compId, room, "idle", "emptyCargo", cargo, true, inputIndex);
                return;
            }

            if (IsCatalyst(cargo.item))
            {
                Probe(ref comp, compId, room, "sprayer", "catalyst", cargo, true, inputIndex);
                return;
            }

            int cargoPoints = QualityRecyclerMath.PointsFromCargoInc(cargo.inc);
            if (cargoPoints <= 0)
            {
                Probe(ref comp, compId, room, "sprayer", "noPoints", cargo, true, inputIndex);
                return;
            }

            int minPerItem = MinPerItemValue;
            if (!QualityRecyclerMath.ShouldHarvest(cargoPoints, cargo.stack, minPerItem))
            {
                P5Common.Effective(ref _lowSkipped, "recycler", " low-points item=" + cargo.item + " inc=" + cargoPoints + " stack=" + cargo.stack + " min=" + minPerItem);
                Probe(ref comp, compId, room, "sprayer", "lowPoints", cargo, true, inputIndex);
                return;
            }

            if (room <= 0)
            {
                P5Common.Effective(ref _tankFull, "recycler", " tank-full eid=" + compId + " tank=" + comp.incCount + "/" + comp.incCapacity);
                Probe(ref comp, compId, room, "sprayer", "tankFull", cargo, true, inputIndex);
                return;
            }

            RecyclerState state = QualityData.GetRecyclerState(planetId, compId);
            if (state.Points >= MaxAccountPoints)
            {
                Probe(ref comp, compId, room, "sprayer", "accountFull", cargo, true, inputIndex);
                return;
            }

            // Destroy one cargo (the vanilla picker removes the whole cargo group and reports its
            // accumulated points through the out inc byte).
            byte pickedStack;
            byte pickedInc;
            int picked;
            try
            {
                picked = path.TryPickItem(inputIndex, ScanWindow, cargo.item, out pickedStack, out pickedInc);
            }
            catch (Exception ex)
            {
                P5Common.WarnOnce("recycler.pick", ex.ToString());
                return;
            }

            if (picked <= 0)
            {
                Probe(ref comp, compId, room, "sprayer", "pickFailed", cargo, true, inputIndex);
                return;
            }

            int gained = QualityRecyclerMath.PointsFromCargoInc(pickedInc);
            if (gained <= 0)
            {
                // Should not happen (the cargo reported points), but never destroy for free.
                gained = cargoPoints * (pickedStack > 0 ? pickedStack : 1) / (cargo.stack > 0 ? cargo.stack : 1);
            }

            int account = QualityData.AddRecyclerPoints(planetId, compId, gained);
            // Make sure the planet has a PlanetQuality entry, otherwise the save writer may skip the
            // planet node and our carried points would be lost on save (QualitySave loops planets).
            try
            {
                QualityData.GetOrCreate(planetId);
            }
            catch (Exception)
            {
            }
            P5Common.Effective(ref _harvested, "recycler",
                " harvested eid=" + compId + " item=" + cargo.item + " stack=" + pickedStack + " points=+" + gained
                + " account=" + account + " tank=" + comp.incCount + "/" + comp.incCapacity);
            _items += pickedStack;
            _pointsIn += gained;

            int units = QualityRecyclerMath.SpraysThatFit(account, room);
            if (units > 0)
            {
                EnsureBrand(ref comp);
                comp.incCount += units;
                if (comp.incCapacity > 0 && comp.incCount > comp.incCapacity)
                {
                    comp.incCount = comp.incCapacity;
                }

                comp.extraIncCount = 0;
                QualityData.TakeRecyclerPoints(planetId, compId, units * PointsPerSprayValue);
                _spraysOut += units;
                int remainder = account - units * PointsPerSprayValue;
                if (remainder > 0)
                {
                    QualityData.SetRecyclerPoints(planetId, compId, remainder);
                }

                string line = Tag + " recycled eid=" + compId + " item=" + cargo.item + " stack=" + pickedStack
                    + " points=+" + gained + " sprays=+" + units + " tank=" + comp.incCount + "/" + comp.incCapacity
                    + " carried=" + (remainder > 0 ? remainder : 0);
                P5Common.Debug(ref _logged, line);
                Probe(ref comp, compId, QualityRecyclerMath.FreeRoom(comp.incCapacity, comp.incCount, comp.extraIncCount),
                    "harvest", "recycled", cargo, true, inputIndex);
            }
            else
            {
                Probe(ref comp, compId, room, "harvest", "queued", cargo, true, inputIndex);
            }

            Persist(traffic, comp);
        }

        private static int ResolvePlanetId(CargoTraffic traffic)
        {
            try
            {
                if (traffic.factory != null)
                {
                    int planetId = traffic.factory.planetId;
                    if (planetId >= 0)
                    {
                        return planetId;
                    }
                }
            }
            catch (Exception)
            {
            }

            bool viaEnv;
            return P5Common.ResolvePlanetId(out viaEnv);
        }

        /// <summary>Rebase the component on the pool entry (Harmony copy safety).</summary>
        private static void Refresh(CargoTraffic traffic, ref SpraycoaterComponent comp)
        {
            try
            {
                SpraycoaterComponent[] pool = traffic.spraycoaterPool;
                int id = comp.id;
                if (pool != null && id > 0 && id < pool.Length && pool[id].id == id)
                {
                    comp = pool[id];
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Write the component back (Harmony copy safety). Harmless when it is already the
        /// pool storage.</summary>
        private static void Persist(CargoTraffic traffic, SpraycoaterComponent comp)
        {
            try
            {
                SpraycoaterComponent[] pool = traffic.spraycoaterPool;
                int id = comp.id;
                if (pool != null && id > 0 && id < pool.Length && pool[id].id == id)
                {
                    pool[id] = comp;
                }
            }
            catch (Exception ex)
            {
                P5Common.WarnOnce("recycler.persist", ex.ToString());
            }
        }

        private static bool IsCatalyst(int itemId)
        {
            for (int i = 0; i < CatalystItems.Length; i++)
            {
                if (CatalystItems[i] == itemId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Give the tank the Mk.III brand when it is empty, so the spray branch can consume
        /// the uses we accumulate (incCount is brand-agnostic, but incAbility / incSprayTimes are not).</summary>
        private static void EnsureBrand(ref SpraycoaterComponent comp)
        {
            if (comp.incItemId == SprayItemMk3)
            {
                return;
            }

            int ability = 3;
            int sprayTimes = 1;
            try
            {
                ItemProto proto = LDB.items.Select(SprayItemMk3);
                if (proto != null)
                {
                    if (proto.Ability > 0)
                    {
                        ability = proto.Ability;
                    }

                    if (proto.HpMax > 0)
                    {
                        sprayTimes = proto.HpMax;
                    }
                }
            }
            catch (Exception ex)
            {
                P5Common.WarnOnce("recycler.proto", ex.ToString());
            }

            comp.incItemId = SprayItemMk3;
            comp.incAbility = ability;
            comp.incSprayTimes = sprayTimes;
            comp.incUsed = false;
        }

        private static void Prune(CargoTraffic traffic, int planetId)
        {
            _tickCounter++;
            if (_tickCounter % PruneEvery != 0)
            {
                return;
            }

            try
            {
                List<int> ids = QualityData.RecyclerEntityIds(planetId);
                for (int i = 0; i < ids.Count; i++)
                {
                    int entityId = ids[i];
                    if (entityId <= 0 || !EntityExists(traffic, entityId))
                    {
                        QualityData.ClearRecyclerState(planetId, entityId);
                        _pruned++;
                    }
                }

                if (ProbeCalls.Count > 4096)
                {
                    ProbeCalls.Clear();
                    ProbeSignature.Clear();
                }
            }
            catch (Exception ex)
            {
                P5Common.WarnOnce("recycler.prune", ex.ToString());
            }
        }

        private static bool EntityExists(CargoTraffic traffic, int entityId)
        {
            try
            {
                if (traffic.factory == null || traffic.factory.entityPool == null)
                {
                    return true;
                }

                if (entityId <= 0 || entityId >= traffic.factory.entityPool.Length)
                {
                    return false;
                }

                return traffic.factory.entityPool[entityId].id == entityId;
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>
        /// One-line state dump, printed when the coater state changes (belt wired, brand change,
        /// tank emptied) and every 200th call. Field test v1 gave no log at all, so this is the
        /// cheapest way to see what the machine actually sees.
        /// </summary>
        private static void Probe(ref SpraycoaterComponent comp, int compId, int room, string mode, string reason,
            Cargo cargo, bool hasCargo, int inputIndex)
        {
            try
            {
                int calls;
                ProbeCalls.TryGetValue(compId, out calls);
                calls++;
                ProbeCalls[compId] = calls;

                int signature = unchecked(comp.incBeltId * 397 ^ comp.cargoBeltId * 31 ^ comp.incItemId * 7
                    ^ (comp.incCount == 0 ? 1 : 0) ^ (comp.incCapacity == 0 ? 2 : 0));
                int last;
                bool changed = !ProbeSignature.TryGetValue(compId, out last) || last != signature;
                if (changed)
                {
                    ProbeSignature[compId] = signature;
                }

                bool periodic = calls % P5Common.EffectiveLogEvery == 0;
                if (!changed && !periodic)
                {
                    return;
                }

                var sb = new StringBuilder(192);
                sb.Append(Tag).Append("[probe] eid=").Append(compId)
                    .Append(" incBelt=").Append(comp.incBeltId)
                    .Append(" cargoBelt=").Append(comp.cargoBeltId)
                    .Append(" brand=").Append(comp.incItemId)
                    .Append(" tank=").Append(comp.incCount).Append('/').Append(comp.incCapacity)
                    .Append(" extra=").Append(comp.extraIncCount)
                    .Append(" room=").Append(room)
                    .Append(" inputIdx=").Append(inputIndex)
                    .Append(" cargo{");
                if (hasCargo)
                {
                    int perItem = cargo.stack > 0 ? cargo.inc / cargo.stack : 0;
                    sb.Append("item=").Append(cargo.item)
                        .Append(",stack=").Append(cargo.stack)
                        .Append(",inc=").Append(cargo.inc)
                        .Append(",per=").Append(perItem);
                }
                else
                {
                    sb.Append("none");
                }

                sb.Append("} mode=").Append(mode).Append(" reason=").Append(reason)
                    .Append(" calls=").Append(calls)
                    .Append(" account=").Append(_currentPlanetId >= 0 ? QualityData.GetRecyclerState(_currentPlanetId, compId).Points : 0);

                _probes++;
                if (changed)
                {
                    P5Common.Info(sb.ToString());
                }
                else
                {
                    P5Common.Debug(ref _logged, sb.ToString());
                }
            }
            catch (Exception ex)
            {
                P5Common.WarnOnce("recycler.probe", ex.ToString());
            }
        }

        /// <summary>Self check used by the startup audit / describe line.</summary>
        internal static string SelfCheck()
        {
            var missing = new List<string>();
            try
            {
                if (ResolveTarget() == null)
                {
                    missing.Add("InternalUpdate");
                }

                if (!HasMethod(typeof(CargoPath), "TryPickItem"))
                {
                    missing.Add("TryPickItem");
                }

                if (!HasMethod(typeof(CargoPath), "GetCargoAtIndex"))
                {
                    missing.Add("GetCargoAtIndex");
                }

                if (!HasMethod(typeof(CargoTraffic), "GetCargoPath"))
                {
                    missing.Add("GetCargoPath");
                }

                if (AccessTools.Field(typeof(CargoTraffic), "spraycoaterPool") == null)
                {
                    missing.Add("spraycoaterPool");
                }
            }
            catch (Exception ex)
            {
                return "error " + ex.GetType().Name;
            }

            return missing.Count == 0 ? "ok" : "missing:" + string.Join(",", missing.ToArray());
        }

        /// <summary>
        /// Overload-tolerant method probe: AccessTools.Method(type, name) throws
        /// AmbiguousMatchException as soon as a type has two overloads with that name
        /// (TryPickItem / GetCargoAtIndex both do), which used to turn the armed line into
        /// "selfCheck=error AmbiguousMatchException" even though the patch itself was fine.
        /// </summary>
        private static bool HasMethod(Type type, string name)
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == name)
                {
                    return true;
                }
            }
            return false;
        }

        internal static string Describe()
        {
            int planets;
            int entries;
            int carried;
            QualityData.RecyclerStats(out planets, out entries, out carried);
            return "recycler armed=" + _armed + " reason=" + _reason + " mode=inc-feed"
                + " selfCheck=" + SelfCheck()
                + " pointsPerSpray=" + PointsPerSprayValue + " minPerItem=" + MinPerItemValue
                + " accounts=" + entries + "@" + planets + " carried=" + carried
                + " harvested=" + _harvested + " items=" + _items + " pointsIn=" + _pointsIn
                + " spraysOut=" + _spraysOut + " lowSkipped=" + _lowSkipped + " tankFull=" + _tankFull
                + " pruned=" + _pruned + " probes=" + _probes;
        }
    }
}
