using System;

namespace DSPQualitySystem
{
    /// <summary>
    /// Pure arithmetic behind the quality rules. Deliberately free of BepInEx/Unity/game types so
    /// the offline test suite (tests\OfflineTests) can link the same source file and exercise the
    /// exact code that ships in the plugin.
    /// </summary>
    public static partial class QualityMath
    {
        /// <summary>Resolutions per percent for the roll (0.01% steps).</summary>
        public const int RollResolution = 100;

        /// <summary>Pool write (spec section 2.2): productCount items of quality incPoints contribute incPoints*productCount points.</summary>
        public static void AddPool(int poolInc, int poolCount, int incPoints, int productCount,
            out int newPoolInc, out int newPoolCount)
        {
            if (productCount <= 0)
            {
                newPoolInc = poolInc;
                newPoolCount = poolCount;
                return;
            }
            if (incPoints < 0)
            {
                incPoints = 0;
            }
            newPoolInc = poolInc + incPoints;
            newPoolCount = poolCount + productCount;
        }

        /// <summary>
        /// Pool read (spec section 2.2): hand out floor(poolInc/poolCount) for one product and pay it
        /// back from the pool. The integer remainder stays in the pool, so points are conserved.
        /// </summary>
        public static int TakeOne(int poolInc, int poolCount, int maxLevel,
            out int newPoolInc, out int newPoolCount)
        {
            if (poolCount <= 0)
            {
                newPoolInc = poolInc;
                newPoolCount = poolCount;
                return 0;
            }
            int perItem = poolInc / poolCount;
            if (perItem < 0)
            {
                perItem = 0;
            }
            else if (perItem > maxLevel)
            {
                perItem = maxLevel;
            }
            newPoolInc = poolInc - perItem;
            newPoolCount = poolCount - 1;
            return perItem;
        }

        public static int ClampLevel(int level, int maxLevel)
        {
            if (maxLevel < 0)
            {
                maxLevel = 0;
            }
            if (level < 0)
            {
                return 0;
            }
            return level > maxLevel ? maxLevel : level;
        }

        public static float ClampPercent(float percent)
        {
            if (percent < 0f)
            {
                return 0f;
            }
            return percent > 100f ? 100f : percent;
        }

        /// <summary>R-2: upgrade chance in percent for one production cycle.</summary>
        public static float UpgradeChance(int buildingInc, float chancePerPoint, float maxChance)
        {
            if (buildingInc <= 0 || chancePerPoint <= 0f)
            {
                return 0f;
            }
            float limit = ClampPercent(maxChance);
            float chance = buildingInc * chancePerPoint;
            return chance > limit ? limit : chance;
        }

        /// <summary>R-3: chance in percent that one mined item carries one quality point.</summary>
        public static float MinerChance(int buildingInc, float chancePerPoint)
        {
            if (buildingInc <= 0 || chancePerPoint <= 0f)
            {
                return 0f;
            }
            return ClampPercent(buildingInc * chancePerPoint);
        }

        /// <summary>
        /// v0.19.1: quality level of one production batch, derived from the ingredients the machine
        /// actually served for it (vanilla AssemblerComponent: incServed[]/served[] per slot).
        /// Vanilla truncates (inc / served); we round half up so a 5.8 average still counts as 6 -
        /// a belt that mixes a few low-level items no longer drags a whole batch one level down.
        /// </summary>
        public static int BatchInputLevel(int incServed, int served, int maxLevel)
        {
            if (served <= 0)
            {
                return 0;
            }
            if (incServed < 0)
            {
                incServed = 0;
            }
            int level = (incServed + (served >> 1)) / served;
            if (level > maxLevel)
            {
                level = maxLevel;
            }
            return level < 0 ? 0 : level;
        }

        /// <summary>R-3 (v0.9.0 multi-roll, user spec 2026-10-25): number of independent quality rolls one
        /// mined item gets. floor(buildingInc / 2): 2 points -> 1 roll, 4 -> 2, 10 -> 5; 0/1 points
        /// give no roll at all. Each successful roll adds one point, so the item cap equals the
        /// roll count ("max ore level never above half the miner points, rounded down").
        /// </summary>
        public static int MinerRollsOf(int buildingInc)
        {
            if (buildingInc <= 0)
            {
                return 0;
            }
            return buildingInc / 2;
        }

        /// <summary>R-3: upper bound of points one mined item can carry (== MinerRollsOf).</summary>
        public static int MinerMaxInc(int buildingInc)
        {
            return MinerRollsOf(buildingInc);
        }

        /// <summary>
        /// Maximum quality points one item can carry (mirrors Cargo.kIncLevelMax = 10). Kept as a
        /// local constant so this file stays game-independent and linkable into the offline tests.
        /// </summary>
        public const int IncPerItemMax = 10;

        /// <summary>
        /// v0.16.0 (Bug 1 "big miner loses quality"): total quality points of one station storage
        /// slot after a vein-collector transfer.
        ///
        /// Vanilla `StationComponent.UpdateVeinCollection` (asm_full.cs:77689-77722) moves
        /// `minerPool[minerId].productCount` into `station.storage[0]` with `count += num2` only -
        /// it never writes `inc`, and the vein collector's miner has `insertTarget == 0`
        /// (asm_full.cs:208341-208346 only calls SetMinerInsertTarget when the miner is NOT attached
        /// to a station), so the mined items never pass through `factory.InsertInto` and the
        /// P0_Miner output hook never runs for them. This helper computes the slot's new total inc:
        /// `currentInc + addCount * perItem`, clamped to `totalCount * IncPerItemMax` because a
        /// stack of N items can never hold more than N * 10 points. Degenerate input never lowers
        /// the previous value and never returns a negative number.
        /// </summary>
        public static int StationIncAfterTransfer(int currentInc, int addCount, int perItem, int totalCount)
        {
            int inc = currentInc > 0 ? currentInc : 0;
            if (addCount <= 0 || perItem <= 0 || totalCount <= 0)
            {
                return inc;
            }
            if (perItem > IncPerItemMax)
            {
                perItem = IncPerItemMax;
            }
            long cap = (long)totalCount * IncPerItemMax;
            long total = inc + (long)addCount * perItem;
            if (total > cap)
            {
                total = cap;
            }
            if (total > int.MaxValue)
            {
                total = int.MaxValue;
            }
            return (int)total;
        }

        /// <summary>
        /// v0.16.0 (Bug 3 "增产 extra products carry 0 points"): real production amount for one
        /// assembler tick from the net change of `produced[]` plus the product picks that happened
        /// since the previous tick.
        ///
        /// `PlanetFactory.PickFrom` (asm_full.cs:217966-218019) decrements `produced[]` whenever a
        /// player/inserter takes a product, and that happens between two assembler ticks, so the raw
        /// delta measures `produced_total_gain - picked_total`. Adding the picks back recovers the
        /// produced count. The 增产 (productive) extra products arrive one item per 10 cycles and are
        /// cancelled by the steady pick stream, which is why only they were credited with 0 points.
        ///
        /// Degenerate input yields 0: never negative, never consumes an unbounded value.
        /// </summary>
        public static int ProducedDelta(int netProduced, int picks)
        {
            long total = netProduced;
            if (picks > 0)
            {
                total += picks;
            }
            if (total <= 0)
            {
                return 0;
            }
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        /// <summary>
        /// v0.16.0 (Bug 2 "Shift+click instant build loses quality"): per-item quality points of the
        /// items consumed by one instant-build action, derived from the total points returned by
        /// `StorageComponent.TakeTailItems(ref int, ref int, out int inc, bool)` (asm_full.cs:80771).
        /// Degenerate input yields 0; the result is clamped to 0..IncPerItemMax.
        /// </summary>
        public static int InstantBuildPerItem(int totalInc, int count)
        {
            if (count <= 0 || totalInc <= 0)
            {
                return 0;
            }
            int perItem = totalInc / count;
            return ClampLevel(perItem, IncPerItemMax);
        }

        /// <summary>
        /// v0.18.0 (issue 4): per-item points of a white ghost (pre-build) that was placed before its
        /// materials existed and only got paid later by `ConstructionModuleComponent.PlaceItems`.
        /// That method pays the whole remaining `itemRequired` stack of one prebuild in a single
        /// `TakeTailItems` call, so the building inherits the points of exactly the items that were
        /// consumed: floor(totalInc / totalCount), clamped to 0..IncPerItemMax. Same formula as the
        /// instant-build path, kept as its own name so the rule and its tests read clearly.
        /// </summary>
        public static int DeferredPayPerItem(int totalInc, int totalCount)
        {
            return InstantBuildPerItem(totalInc, totalCount);
        }

        /// <summary>
        /// v0.17.0: per-item points carried by one building item, derived from a consumption receipt
        /// recorded as hundredths of a point. Floors to a whole point and clamps to 0..IncPerItemMax.
        /// Degenerate input yields 0.
        /// </summary>
        public static int ReceiptPerItem(int perItemHundredths)
        {
            if (perItemHundredths <= 0)
            {
                return 0;
            }
            return ClampLevel(perItemHundredths / 100, IncPerItemMax);
        }

        /// <summary>
        /// v0.17.0: does a recorded consumption receipt belong to the item that a build is paying
        /// for? Unknown ids (0, or vanilla's "any matching item" wildcard) match anything, mirroring
        /// the vanilla `TakeTailItems(ref int itemId, ...)` contract.
        /// </summary>
        public static bool ReceiptItemMatches(int receiptItemId, int expectedItemId)
        {
            if (receiptItemId <= 0 || expectedItemId <= 0)
            {
                return true;
            }
            return receiptItemId == expectedItemId;
        }

        /// <summary>
        /// v0.17.0: shared consume-window union. Hand build (BuildTool_Click.CreatePrebuilds),
        /// blueprint paste (BuildTool_BlueprintPaste.CreatePrebuilds) and the instant-build tick
        /// (PlayerAction_Inspect.GameTick) all need the same "highest quality grid first"
        /// preference, so the predicate is one pure function instead of three ad hoc checks.
        /// </summary>
        public static bool ConsumeWindowActive(bool inPaste, bool inHandBuild, bool instantWindow)
        {
            return inPaste || inHandBuild || instantWindow;
        }

        /// <summary>
        /// v0.17.0: per-item value a freshly created prebuild receives. A receipt from the
        /// highest-quality consumption path wins; the raw `TakeTailItems` record is the fallback.
        /// </summary>
        public static int PreferBindingValue(int receiptPerItem, int recordPerItem)
        {
            if (receiptPerItem > 0)
            {
                return ClampLevel(receiptPerItem, IncPerItemMax);
            }
            if (recordPerItem > 0)
            {
                return ClampLevel(recordPerItem, IncPerItemMax);
            }
            return 0;
        }

        /// <summary>
        /// R-2 extension (v0.9.0, user spec 2026-10-25): how the extra-upgrade rules treat a
        /// production building. 0 = base upgrade roll only (v0.8.0 behaviour), 1 = base roll plus
        /// one extra same-chance roll (max +2), 2 = guaranteed +2 with no probability roll.
        /// A threshold of 0 disables that rule; when both are 0 every building maps to mode 0.
        /// </summary>
        public static int ExtraUpgradeMode(int buildingInc, int doubleAt, int extraAbove)
        {
            if (buildingInc <= 0)
            {
                return 0;
            }
            if (doubleAt > 0 && buildingInc >= doubleAt)
            {
                return 2;
            }
            if (extraAbove > 0 && buildingInc > extraAbove)
            {
                return 1;
            }
            return 0;
        }

        /// <summary>
        /// R-2 extension: apply the cycle upgrade result. Pure so the offline tests can hammer it.
        /// mode 2 is deterministic (+2, skipping rolls); mode 1 adds the extra roll on top of the
        /// base hit; mode 0 reproduces the old single-roll behaviour. Always clamped to [0, maxLevel].
        /// </summary>
        public static int ApplyCycleUpgrade(int inputLevel, int mode, bool baseRollHit, bool extraRollHit, int maxLevel)
        {
            int level = inputLevel < 0 ? 0 : inputLevel;
            if (maxLevel < 0)
            {
                maxLevel = 0;
            }
            if (level > maxLevel)
            {
                level = maxLevel;
            }
            if (level >= maxLevel)
            {
                return level;
            }
            if (mode >= 2)
            {
                level += 2;
            }
            else
            {
                if (baseRollHit)
                {
                    level++;
                }
                if (mode >= 1 && level < maxLevel && extraRollHit)
                {
                    level++;
                }
            }
            if (level > maxLevel)
            {
                level = maxLevel;
            }
            if (level < 0)
            {
                level = 0;
            }
            return level;
        }

        /// <summary>
        /// v0.11.1: complete R-1 + R-2 cycle roll. Pure so the offline tests can pin the RNG
        /// contract while QualityRules adds the read-only [QSystem][roll] debug line. The order and
        /// number of rollPercent calls is exactly the v0.11.0 sequence (mode 0 -> one base roll,
        /// mode 1 -> base + one extra roll, mode 2 -> no probability roll), so adding logging can
        /// never shift the random stream. The two out flags are diagnostics only; they never feed
        /// back into the return value beyond what the vanilla formula already did.
        /// </summary>
        public static int RollCycleLevel(int inputLevel, int buildingInc, bool allowExtraUpgrade,
            float chancePerPoint, float maxChance, int doubleUpgradeAt, int extraAbove, int maxLevel,
            Func<float, bool> rollPercent, out int mode, out bool baseRollHit, out bool extraRollHit)
        {
            float chance = buildingInc > 0 ? UpgradeChance(buildingInc, chancePerPoint, maxChance) : 0f;
            return RollCycleLevelCore(inputLevel, buildingInc, allowExtraUpgrade, chance, doubleUpgradeAt,
                extraAbove, maxLevel, rollPercent, out mode, out baseRollHit, out extraRollHit);
        }

        /// <summary>
        /// v0.18.0 (issue 2): identical cycle roll, but the per-roll chance comes from the shared
        /// per-point curve (<see cref="UpgradeCurveChance"/>) instead of the linear
        /// chancePerPoint/maxChance pair. The roll count and order are byte-for-byte the same
        /// sequence as <see cref="RollCycleLevel"/> (mode 0 = one base roll, mode 1 = base + one extra
        /// roll, mode 2 = deterministic +2 with no probability roll).
        /// </summary>
        public static int RollCycleLevelCurve(int inputLevel, int buildingInc, bool allowExtraUpgrade,
            int doubleUpgradeAt, int extraAbove, int maxLevel,
            Func<float, bool> rollPercent, out int mode, out bool baseRollHit, out bool extraRollHit)
        {
            float chance = UpgradeCurveChance(buildingInc);
            return RollCycleLevelCore(inputLevel, buildingInc, allowExtraUpgrade, chance, doubleUpgradeAt,
                extraAbove, maxLevel, rollPercent, out mode, out baseRollHit, out extraRollHit);
        }

        private static int RollCycleLevelCore(int inputLevel, int buildingInc, bool allowExtraUpgrade,
            float chance, int doubleUpgradeAt, int extraAbove, int maxLevel,
            Func<float, bool> rollPercent, out int mode, out bool baseRollHit, out bool extraRollHit)
        {
            int level = inputLevel < 0 ? 0 : inputLevel;
            if (maxLevel < 0)
            {
                maxLevel = 0;
            }
            if (level > maxLevel)
            {
                level = maxLevel;
            }

            mode = 0;
            baseRollHit = false;
            extraRollHit = false;
            if (level < maxLevel && buildingInc > 0)
            {
                mode = allowExtraUpgrade ? ExtraUpgradeMode(buildingInc, doubleUpgradeAt, extraAbove) : 0;
                if (mode < 2)
                {
                    baseRollHit = chance > 0f && rollPercent != null && rollPercent(chance);
                    if (mode >= 1)
                    {
                        extraRollHit = chance > 0f && rollPercent != null && rollPercent(chance);
                    }
                }
                level = ApplyCycleUpgrade(level, mode, baseRollHit, extraRollHit, maxLevel);
            }

            if (level > maxLevel)
            {
                level = maxLevel;
            }
            if (level < 0)
            {
                level = 0;
            }
            return level;
        }
        /// <summary>
        /// P2 fractionator (spec 3.11): the per-item input inc level the vanilla success roll uses.
        /// Vanilla computes it as num3 = fluidInputInc > 0 &amp;&amp; fluidInputCount > 0
        /// ? fluidInputInc / fluidInputCount : 0 (asm_full.cs:53727) and passes min(num3, 10)
        /// into Cargo.accTableMilli. The transpiler hook recomputes it from the component fields
        /// instead of reading the vanilla local (whose stack slot is an implementation detail),
        /// hence this shared pure function. maxLevel clamps the result into the quality tables.
        /// </summary>
        public static int FractionatorInputLevel(int fluidInputInc, int fluidInputCount, int maxLevel)
        {
            if (fluidInputInc <= 0 || fluidInputCount <= 0)
            {
                return 0;
            }
            return ClampLevel(fluidInputInc / fluidInputCount, maxLevel);
        }

        /// <summary>
        /// P3 stage 1 (spec 8.D): building quality points -> work speed multiplier.
        /// Linear and deterministic: factor = 1 + min(inc * percentPerPoint, maxBonusPercent) / 100.
        /// inc = 0 (or anything that cannot give a bonus) returns exactly 1f so the caller can
        /// leave the vanilla value untouched and behave bit-identically to vanilla.
        /// </summary>
        public static float SpeedFactor(int buildingInc, float percentPerPoint, float maxBonusPercent)
        {
            if (buildingInc <= 0 || percentPerPoint <= 0f || maxBonusPercent <= 0f)
            {
                return 1f;
            }
            float bonus = buildingInc * percentPerPoint;
            if (bonus > maxBonusPercent)
            {
                bonus = maxBonusPercent;
            }
            return 1f + bonus / 100f;
        }

        /// <summary>
        /// P3 stage 2a (v0.11.0): raise the logistics-station charge cap for a building level.
        /// Vanilla SetPCState derives energyPerTick from the prefab work power every tick
        /// (asm_full.cs:75035-75051), so scaling the derived value is transient by construction.
        /// Returns 0 when nothing must change: full/near-full buffer, invalid values, zero points,
        /// or a factor that would not raise the value (so callers can leave vanilla untouched and
        /// stay bit-identical). The factor comes from <see cref="SpeedFactor"/> (same curve).
        /// </summary>
        public static long ChargeCapFrom(long energyPerTick, long energy, long energyMax,
            int buildingInc, float percentPerPoint, float maxBonusPercent)
        {
            return ChargeCapFrom(energyPerTick, energy, energyMax, buildingInc, false,
                percentPerPoint, maxBonusPercent);
        }

        /// <summary>v0.11.2: same as the legacy overload plus the curve flag (see <see cref="SpeedFactorFor"/>).</summary>
        public static long ChargeCapFrom(long energyPerTick, long energy, long energyMax,
            int buildingInc, bool curve, float percentPerPoint, float maxBonusPercent)
        {
            if (energyPerTick <= 0 || energyMax <= 0 || energy >= energyMax - 1)
            {
                return 0;
            }
            float factor = SpeedFactorFor(buildingInc, curve, percentPerPoint, maxBonusPercent);
            if (factor <= 1f || float.IsNaN(factor) || float.IsInfinity(factor))
            {
                return 0;
            }
            long raised = (long)(energyPerTick * (double)factor);
            if (raised <= energyPerTick)
            {
                return 0;
            }
            return raised;
        }

        /// <summary>
        /// P3 stage 2d (build quality refund on dismantle): number of quality points to stamp on
        /// the item returned to the inventory. Pure and clamp-only: the caller owns config gates
        /// and storage lookups. A missing/zero contribution returns 0 so the vanilla hard-coded
        /// inc=0 is reproduced bit-for-bit.
        /// </summary>
        public static int DismantleIncFrom(int buildingInc, int kIncLevelMax, bool giveDismantleInc)
        {
            if (!giveDismantleInc || buildingInc <= 0)
            {
                return 0;
            }
            return ClampLevel(buildingInc, kIncLevelMax);
        }

        /// <summary>
        /// v0.10.0 (P4 backpack isolation): may two stacks of the same item share one grid?
        /// Vanilla StorageComponent.AddItemStacked merges purely by itemId, so a 0-point and a
        /// 4-point building end up in the same grid and their points are added together. For
        /// building items in the player inventory we additionally require the same per-item point
        /// level. Note the vanilla convention: the inc parameter is the TOTAL points of the stack
        /// (see split_inc, asm_full.cs:170203-170216, and UseHandItems' useInc/used), so the level
        /// is inc/count and the comparison uses cross multiplication to stay integer exact.
        /// Non-building items and non-player storages keep vanilla behaviour (always true, so the
        /// old behaviour is bit-identical); an empty grid may take any level.
        /// </summary>
        public static bool SharesStack(int itemId, int gridItemId, int gridCount, int gridInc,
            int incomingCount, int incomingInc, bool isBuilding, bool isPlayerInventory)
        {
            if (gridItemId != itemId)
            {
                return false;
            }
            if (!isBuilding || !isPlayerInventory)
            {
                return true;
            }
            if (gridCount <= 0 || incomingCount <= 0)
            {
                return true;
            }
            return (long)gridInc * incomingCount == (long)incomingInc * gridCount;
        }

        /// <summary>
        /// v0.11.3 (P4 containers): may this storage keep building stacks of different quality in
        /// separate grids? The player inventory has always opted in (the v0.10.0 scope). Other
        /// containers opt in only when isolateStorageContainers is on and the storage is a plain
        /// EStorageType.Default container (Default = 0, asm_full.cs:79397); fuel/ammo/bomb/fighter
        /// and the Filtered/DeliveryFiltered types are slot-filtered storages and are never
        /// isolated. Pure so the gate can be asserted offline; the mecha ownership exclusions live
        /// in QualityRules.ShouldIsolateStorage because they need live game objects.
        /// </summary>
        public static bool ShouldIsolateStorage(bool isPlayerInventory, int storageType, int storageSize,
            bool containersEnabled)
        {
            if (storageSize <= 0)
            {
                return false;
            }
            if (isPlayerInventory)
            {
                return true;
            }
            if (!containersEnabled)
            {
                return false;
            }
            return storageType == 0;
        }

        /// <summary>
        /// v0.10.0 (P4): verbatim equivalent of the vanilla StorageComponent.split_inc helper
        /// (asm_full.cs:170203-170216). Distributes m quality points over n items when p items are
        /// placed into a grid: returns the per-item points stored in that grid and updates the
        /// remaining item count / point total. Kept pure so the isolation add path is testable
        /// offline. The n&lt;=0 / p&lt;=0 guards are defensive: vanilla callers never hit them.
        /// </summary>
        public static int SplitInc(ref int n, ref int m, int p)
        {
            if (n <= 0 || p <= 0)
            {
                return 0;
            }
            int perItem = m / n;
            int remainder = m - perItem * n;
            n -= p;
            remainder -= n;
            perItem = remainder > 0 ? perItem * p + remainder : perItem * p;
            m -= perItem;
            return perItem;
        }

        /// <summary>
        /// v0.11.1 (P4 sort): plan the backpack layout that vanilla StorageComponent.Sort would
        /// produce, except that building stacks with a different per-item quality level stay in
        /// separate groups. Mirrors asm_full.cs:81153-81236: groups are ordered by item proto index
        /// (pass protoIndexes; -1 falls back to 10000+itemId like vanilla), each group is then spread
        /// over grids of itemStackCount-sized stacks with split_inc distributing the total points.
        /// Pure int arrays only so the offline tests can assert the layout. Returns the number of
        /// grids written (never more than the input capacity; the item/point total is conserved).
        /// </summary>
        public static int PlanSortedLayout(int[] itemIds, int[] counts, int[] incs, int[] stackSizes,
            bool[] isBuilding, int[] protoIndexes,
            out int[] outIds, out int[] outCounts, out int[] outIncs)
        {
            int capacity = itemIds == null ? 0 : itemIds.Length;
            outIds = new int[capacity];
            outCounts = new int[capacity];
            outIncs = new int[capacity];
            if (capacity == 0 || counts == null || incs == null || stackSizes == null || isBuilding == null)
            {
                return 0;
            }

            int[] groupIds = new int[capacity];
            int[] groupCounts = new int[capacity];
            long[] groupIncs = new long[capacity];
            int[] groupStack = new int[capacity];
            int[] groupProto = new int[capacity];
            int[] groupFirst = new int[capacity];
            int groupTotal = 0;

            for (int i = 0; i < capacity; i++)
            {
                int id = itemIds[i];
                int count = counts[i];
                int inc = incs[i];
                if (id <= 0 || count <= 0)
                {
                    continue;
                }

                bool building = isBuilding[i];
                int found = -1;
                for (int g = 0; g < groupTotal; g++)
                {
                    if (groupIds[g] != id)
                    {
                        continue;
                    }
                    if (!building)
                    {
                        found = g;
                        break;
                    }
                    if (SharesStack(id, groupIds[g], groupCounts[g], (int)groupIncs[g], count, inc, true, true))
                    {
                        found = g;
                        break;
                    }
                }

                if (found < 0)
                {
                    found = groupTotal;
                    groupTotal++;
                    groupIds[found] = id;
                    groupCounts[found] = 0;
                    groupIncs[found] = 0;
                    groupStack[found] = stackSizes[i] > 0 ? stackSizes[i] : 1000;
                    int proto = protoIndexes != null && i < protoIndexes.Length ? protoIndexes[i] : -1;
                    groupProto[found] = proto >= 0 ? proto : 10000 + id;
                    groupFirst[found] = i;
                }

                groupCounts[found] += count;
                groupIncs[found] += inc;
            }

            int[] order = new int[groupTotal];
            for (int i = 0; i < groupTotal; i++)
            {
                order[i] = i;
            }
            for (int a = 1; a < groupTotal; a++)
            {
                int key = order[a];
                int pos = a - 1;
                while (pos >= 0 && CompareSortedGroups(key, order[pos], groupCounts, groupIncs, groupProto, groupFirst) < 0)
                {
                    order[pos + 1] = order[pos];
                    pos--;
                }
                order[pos + 1] = key;
            }

            int written = 0;
            for (int i = 0; i < groupTotal && written < capacity; i++)
            {
                int g = order[i];
                int id = groupIds[g];
                int n = groupCounts[g];
                long total = groupIncs[g];
                int m = total > int.MaxValue ? int.MaxValue : (total < 0 ? 0 : (int)total);
                int stack = groupStack[g] <= 0 ? 1000 : groupStack[g];
                while (n > 0 && written < capacity)
                {
                    outIds[written] = id;
                    if (n <= stack)
                    {
                        outCounts[written] = n;
                        outIncs[written] = m;
                        n = 0;
                        m = 0;
                    }
                    else
                    {
                        outIncs[written] = SplitInc(ref n, ref m, stack);
                        outCounts[written] = stack;
                    }
                    written++;
                }
            }
            return written;
        }

        /// <summary>Layout ordering: proto index ascending, then per-item quality level ascending.</summary>
        private static int CompareSortedGroups(int a, int b, int[] groupCounts, long[] groupIncs,
            int[] groupProto, int[] groupFirst)
        {
            if (groupProto[a] != groupProto[b])
            {
                return groupProto[a] < groupProto[b] ? -1 : 1;
            }
            int countA = groupCounts[a];
            int countB = groupCounts[b];
            if (countA > 0 && countB > 0)
            {
                long levelA = groupIncs[a] * countB;
                long levelB = groupIncs[b] * countA;
                if (levelA != levelB)
                {
                    return levelA < levelB ? -1 : 1;
                }
            }
            if (groupFirst[a] != groupFirst[b])
            {
                return groupFirst[a] < groupFirst[b] ? -1 : 1;
            }
            return 0;
        }
        /// <summary>Roll a percentage with 0.01% resolution. rng must not be null.</summary>
        public static bool RollPercent(float percent, Random rng)
        {
            if (percent <= 0f)
            {
                return false;
            }
            if (percent >= 100f)
            {
                return true;
            }
            if (rng == null)
            {
                return false;
            }
            int threshold = (int)(percent * RollResolution + 0.5f);
            if (threshold > 10000)
            {
                threshold = 10000;
            }
            return rng.Next(10000) < threshold;
        }
    }
}
