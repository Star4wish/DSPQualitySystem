using System;

namespace DSPQualitySystem
{
    /// <summary>
    /// v0.16.0 (C-2 + C-3): pure routing math behind the per-output-port quality filter of the
    /// four-way splitter and behind the sorter quality threshold.
    ///
    /// Deliberately free of BepInEx/Unity/game types so tests\OfflineTests can link this source
    /// file verbatim and exercise exactly the code that ships in the plugin.
    ///
    /// Rule encoding (one packed int per splitter port, held in the mod side table only - never in
    /// the vanilla SplitterComponent fields, so a vanilla save/load cycle cannot be affected):
    ///
    ///     bits  0..15 : itemId (0 = any item)
    ///     bits 16..23 : minimum quality points (0..10)
    ///     packed == 0 : no rule on this port
    ///
    /// "Quality points" are the vanilla cargo/build quality levels (Cargo.inc, 0..10 in normal
    /// play). A rule matches when the cargo item is the rule item (or the rule item is 0) and the
    /// cargo quality points are greater than or equal to the rule minimum ("at least N points").
    /// </summary>
    public static partial class QualityMath
    {
        /// <summary>Number of physical ports of a four-way splitter (belt A..D).</summary>
        public const int SplitterPortCount = 4;

        /// <summary>Packed rule value that means "this port has no quality rule".</summary>
        public const int SplitterRuleNone = 0;

        /// <summary>Item id inside a packed rule that matches any item (quality-only rule).</summary>
        public const int SplitterRuleAnyItem = 0;

        /// <summary>Mask of the item id field of a packed rule.</summary>
        public const int SplitterRuleItemMask = 0xFFFF;

        /// <summary>Shift of the minimum-points field of a packed rule.</summary>
        public const int SplitterRuleMinShift = 16;

        /// <summary>Mask of the minimum-points field of a packed rule.</summary>
        public const int SplitterRuleMinMask = 0xFF;

        /// <summary>Highest quality point level used by the rules (vanilla cargo inc range).</summary>
        public const int QualityPointsMax = 10;

        /// <summary>Sorter threshold value that means "the sorter has no quality rule".</summary>
        public const int SorterMinNone = 0;

        /// <summary>
        /// v0.19.2 blueprint transport: magic value written at the TAIL of the vanilla
        /// BuildingParameters int[] array, so a blueprint carries the per-building splitter rules
        /// and sorter threshold next to the vanilla fields.
        ///
        /// Vanilla allocates that array from a fixed layout (and never appends to it), therefore the
        /// marker can never collide with a vanilla value. Old blueprints simply have no tail and fall
        /// back to the classic single-clipboard behaviour.
        /// </summary>
        public const int BlueprintBlockMagic = 0x51465042; // 'QFPB'

        /// <summary>Format version of a <see cref="BlueprintBlockMagic"/> tail block.</summary>
        public const int BlueprintBlockVersion = 1;

        /// <summary>Number of ints in one blueprint quality tail block.</summary>
        public const int BlueprintBlockLength = 9;

        /// <summary>Block kind: four-way splitter with per-port rules.</summary>
        public const int BlueprintBlockKindSplitter = 1;

        /// <summary>Block kind: sorter with a minimum-points threshold.</summary>
        public const int BlueprintBlockKindSorter = 2;

        /// <summary>
        /// Return <paramref name="parameters"/> with one quality tail block appended (the input array
        /// is never modified). <paramref name="paramCount"/> handling is left to the caller because
        /// vanilla uses it inconsistently.
        /// </summary>
        public static int[] AppendBlueprintBlock(int[] parameters, int kind, int protoId, int[] rules, int minPoints)
        {
            int oldLength = parameters == null ? 0 : parameters.Length;
            int[] next = new int[oldLength + BlueprintBlockLength];
            if (oldLength > 0)
            {
                Array.Copy(parameters, next, oldLength);
            }

            next[oldLength] = BlueprintBlockMagic;
            next[oldLength + 1] = BlueprintBlockVersion;
            next[oldLength + 2] = kind;
            next[oldLength + 3] = protoId;
            for (int port = 0; port < SplitterPortCount; port++)
            {
                next[oldLength + 4 + port] = rules != null && port < rules.Length ? rules[port] : SplitterRuleNone;
            }

            next[oldLength + 8] = minPoints;
            return next;
        }

        /// <summary>
        /// Read a quality tail block from the array vanilla wrote. Returns false for every array that
        /// does not end in a valid block (old blueprints, non splitter/sorter buildings, garbage).
        /// </summary>
        public static bool TryReadBlueprintBlock(int[] parameters, out int kind, out int protoId, out int[] rules,
            out int minPoints)
        {
            kind = 0;
            protoId = 0;
            rules = null;
            minPoints = SorterMinNone;

            if (parameters == null || parameters.Length < BlueprintBlockLength)
            {
                return false;
            }

            int start = parameters.Length - BlueprintBlockLength;
            if (parameters[start] != BlueprintBlockMagic || parameters[start + 1] != BlueprintBlockVersion)
            {
                return false;
            }

            kind = parameters[start + 2];
            if (kind != BlueprintBlockKindSplitter && kind != BlueprintBlockKindSorter)
            {
                return false;
            }

            protoId = parameters[start + 3];
            if (protoId <= 0)
            {
                return false;
            }

            rules = new int[SplitterPortCount];
            for (int port = 0; port < SplitterPortCount; port++)
            {
                rules[port] = parameters[start + 4 + port];
            }

            minPoints = parameters[start + 8];
            if (minPoints < 0 || minPoints > QualityPointsMax)
            {
                minPoints = SorterMinNone;
            }

            return true;
        }

        /// <summary>Clamp an arbitrary quality-point value into the supported 0..10 range.</summary>
        public static int ClampQualityPoints(int points)
        {
            if (points <= 0)
            {
                return 0;
            }

            return points >= QualityPointsMax ? QualityPointsMax : points;
        }

        /// <summary>
        /// Pack a (itemId, minimum points) pair into the single int stored in the side table.
        /// An out-of-range item id collapses to "no rule", because a rule that can never match an
        /// item would silently swallow cargo at that port.
        /// </summary>
        public static int PackSplitterRule(int itemId, int minPoints)
        {
            if (itemId < 0 || itemId > SplitterRuleItemMask)
            {
                return SplitterRuleNone;
            }

            int min = ClampQualityPoints(minPoints);
            int item = itemId & SplitterRuleItemMask;
            if (item == 0 && min == 0)
            {
                // "any item at any quality" carries no information: treat it as no rule.
                return SplitterRuleNone;
            }

            return item | (min << SplitterRuleMinShift);
        }

        /// <summary>Item id of a packed rule (0 = any item).</summary>
        public static int SplitterRuleItem(int packed)
        {
            return packed & SplitterRuleItemMask;
        }

        /// <summary>Minimum quality points of a packed rule (0..10).</summary>
        public static int SplitterRuleMin(int packed)
        {
            return (packed >> SplitterRuleMinShift) & SplitterRuleMinMask;
        }

        /// <summary>True when the packed value carries no rule at all.</summary>
        public static bool SplitterRuleIsEmpty(int packed)
        {
            return packed == SplitterRuleNone;
        }

        /// <summary>
        /// True when a rule accepts this cargo: the item matches (rule item 0 matches anything) and
        /// the cargo quality points are at least the rule minimum ("geq N", equal counts).
        /// </summary>
        public static bool SplitterRuleMatches(int packed, int itemId, int incPoints)
        {
            if (packed == SplitterRuleNone)
            {
                return false;
            }

            if (incPoints < 0)
            {
                return false;
            }

            int ruleItem = SplitterRuleItem(packed);
            if (ruleItem != SplitterRuleAnyItem && ruleItem != itemId)
            {
                return false;
            }

            return incPoints >= SplitterRuleMin(packed);
        }

        /// <summary>Cycle a quality threshold upwards for the UI: 0 -> 1 -> ... -> 10 -> 0.</summary>
        public static int NextQualityMin(int minPoints)
        {
            int min = ClampQualityPoints(minPoints);
            return min >= QualityPointsMax ? 0 : min + 1;
        }

        /// <summary>Cycle a quality threshold downwards for the UI: 0 -> 10 -> 9 -> ... -> 1 -> 0.</summary>
        public static int PrevQualityMin(int minPoints)
        {
            int min = ClampQualityPoints(minPoints);
            return min <= 0 ? QualityPointsMax : min - 1;
        }

        /// <summary>True when a sorter threshold is inside the supported 0..10 range.</summary>
        public static bool ValidateSorterMin(int minPoints)
        {
            return minPoints >= 0 && minPoints <= QualityPointsMax;
        }

        /// <summary>
        /// Index of a belt id inside the vanilla output rotation (output0..output3), or -1 when the
        /// belt is not one of the current outputs. The physical port identity lives in beltA..D, so
        /// this is the only reliable way to map "physical port" to "current output slot".
        /// </summary>
        public static int FindOutputIndex(int[] outputBelts, int beltId)
        {
            if (outputBelts == null || beltId == 0)
            {
                return -1;
            }

            int count = outputBelts.Length < SplitterPortCount ? outputBelts.Length : SplitterPortCount;
            for (int i = 0; i < count; i++)
            {
                if (outputBelts[i] == beltId)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Plan where a single cargo should go. Writes the candidate physical ports into
        /// <paramref name="candidates"/> (length &gt;= SplitterPortCount) and returns how many were
        /// written, in priority order:
        ///
        ///   1. output ports whose rule accepts this cargo, strictest minimum first ("geq 5" wins
        ///      over "geq 2"), ties broken by ascending port number;
        ///   2. output ports that carry no rule at all, in vanilla output rotation order
        ///      (output0..3). This is also the fallback for cargo a rule does not accept, and it
        ///      reproduces vanilla first-fit behaviour when no rule is configured anywhere.
        ///
        /// A port that has a rule never appears in group 2, so low-quality cargo can never fill a
        /// port the player reserved for a higher quality tier. When every output port has a rule
        /// and none accepts the cargo, the result is empty and the caller must leave the cargo in
        /// place (vanilla "blocked" behaviour) instead of destroying it.
        /// </summary>
        /// <param name="cargoItem">Item id of the cargo being routed.</param>
        /// <param name="cargoInc">
        /// Quality points of ONE item of the cargo, i.e. QualityMath.PerItemPoints(cargo.inc,
        /// cargo.stack) - never the raw Cargo.inc stack total.
        /// </param>
        /// <param name="portRules">Packed rules for physical ports 0..3 (index = port).</param>
        /// <param name="portBelts">Belt id of physical ports 0..3 (SplitterComponent.GetSlotBelt).</param>
        /// <param name="outputBelts">Belt ids currently in output0..output3 order (0 = empty slot).</param>
        /// <param name="candidates">Destination buffer, at least SplitterPortCount entries.</param>
        public static int PlanSplitterRoute(int cargoItem, int cargoInc,
            int[] portRules, int[] portBelts, int[] outputBelts, int[] candidates)
        {
            if (candidates == null || portRules == null || portBelts == null || outputBelts == null)
            {
                return 0;
            }

            int count = 0;

            // Group 1: rule ports that accept this cargo, ordered by strictest minimum, then port.
            int portLimit = portRules.Length < SplitterPortCount ? portRules.Length : SplitterPortCount;
            int beltLimit = portBelts.Length < SplitterPortCount ? portBelts.Length : SplitterPortCount;
            if (beltLimit < portLimit)
            {
                portLimit = beltLimit;
            }

            for (int port = 0; port < portLimit; port++)
            {
                int rule = portRules[port];
                if (rule == SplitterRuleNone)
                {
                    continue;
                }

                if (!PortIsOutput(portBelts, outputBelts, port))
                {
                    continue;
                }

                if (!SplitterRuleMatches(rule, cargoItem, cargoInc))
                {
                    continue;
                }

                candidates[count++] = port;
            }

            // Stable insertion sort by descending minimum points: ascending port order is preserved
            // for equal minima, which keeps the routing deterministic.
            for (int i = 1; i < count; i++)
            {
                int key = candidates[i];
                int keyMin = SplitterRuleMin(portRules[key]);
                int j = i - 1;
                while (j >= 0 && SplitterRuleMin(portRules[candidates[j]]) < keyMin)
                {
                    candidates[j + 1] = candidates[j];
                    j--;
                }

                candidates[j + 1] = key;
            }

            // Group 2: rule-less output ports in vanilla output order (output0..3).
            for (int slot = 0; slot < SplitterPortCount; slot++)
            {
                int belt = outputBelts[slot];
                if (belt == 0)
                {
                    continue;
                }

                int port = PortOfBelt(portBelts, belt, portLimit);
                if (port < 0)
                {
                    continue;
                }

                if (portRules[port] != SplitterRuleNone)
                {
                    continue;
                }

                candidates[count++] = port;
            }

            return count;
        }

        /// <summary>
        /// v0.17.1: destinations for cargo that no quality rule claims (the "vanilla fallback" of a
        /// splitter that has at least one rule). Writes physical ports into
        /// <paramref name="candidates"/> and returns how many were written.
        ///
        /// The order is the vanilla first-fit order (output0, output1, output2, output3), but a
        /// physical port that carries a rule is NEVER written, no matter what the vanilla
        /// splitter component's outFilter says. That is the whole point of the fallback: once a
        /// port is reserved for a quality tier, vanilla's item filter (a single global outFilter,
        /// still written by the vanilla item picker for its own icon display) must not be able to
        /// push low-quality cargo into it. When every output port is reserved the result is empty
        /// and the caller leaves the cargo on the belt.
        /// </summary>
        /// <param name="portRules">Packed rules for physical ports 0..3 (index = port).</param>
        /// <param name="portBelts">Belt id of physical ports 0..3 (SplitterComponent.GetSlotBelt).</param>
        /// <param name="outputBelts">Belt ids currently in output0..output3 order (0 = empty slot).</param>
        /// <param name="candidates">Destination buffer, at least SplitterPortCount entries.</param>
        public static int PlanSplitterFallback(int[] portRules, int[] portBelts, int[] outputBelts, int[] candidates)
        {
            if (candidates == null || portRules == null || portBelts == null || outputBelts == null)
            {
                return 0;
            }

            int count = 0;
            int portLimit = portRules.Length < SplitterPortCount ? portRules.Length : SplitterPortCount;
            int beltLimit = portBelts.Length < SplitterPortCount ? portBelts.Length : SplitterPortCount;
            if (beltLimit < portLimit)
            {
                portLimit = beltLimit;
            }

            int slotLimit = outputBelts.Length < SplitterPortCount ? outputBelts.Length : SplitterPortCount;
            for (int slot = 0; slot < slotLimit; slot++)
            {
                int belt = outputBelts[slot];
                if (belt == 0)
                {
                    continue;
                }

                int port = PortOfBelt(portBelts, belt, portLimit);
                if (port < 0)
                {
                    continue;
                }

                if (portRules[port] != SplitterRuleNone)
                {
                    // Reserved by a quality rule: vanilla first-fit must never feed it.
                    continue;
                }

                candidates[count++] = port;
            }

            return count;
        }

        /// <summary>
        /// sorter pick plan: index (0..count-1) of the first cargo in the belt window that matches
        /// the sorter item filter and reaches the quality threshold, or -1 when no cargo qualifies.
        /// A threshold of 0 reproduces the vanilla rule (first cargo with the matching item id).
        /// <paramref name="incs"/> must hold the quality points of ONE item of each cargo - use
        /// QualityMath.PerItemPoints(cargo.inc, cargo.stack) - not the raw Cargo.inc stack total.
        /// <paramref name="needs"/> may be null; when present it is the care-needs item list and is
        /// applied in addition to the item filter.
        /// </summary>
        public static int PlanSorterPick(int[] items, int[] incs, int count, int filter, int minPoints, int[] needs)
        {
            if (items == null || incs == null || count <= 0)
            {
                return -1;
            }

            int n = items.Length < incs.Length ? items.Length : incs.Length;
            if (n > count)
            {
                n = count;
            }

            int min = ClampQualityPoints(minPoints);
            for (int i = 0; i < n; i++)
            {
                int item = items[i];
                if (item <= 0)
                {
                    continue;
                }

                if (filter != 0 && item != filter)
                {
                    continue;
                }

                if (incs[i] < min)
                {
                    continue;
                }

                if (needs != null && !ContainsItem(needs, item))
                {
                    continue;
                }

                return i;
            }

            return -1;
        }

        /// <summary>
        /// v0.16.1 UI: label of the threshold button of a splitter port row. 0 renders as "不限"
        /// (no quality constraint), otherwise "≥N".
        /// </summary>
        public static string SplitterRuleMinText(int minPoints)
        {
            int min = ClampQualityPoints(minPoints);
            return min <= 0 ? "不限" : "≥" + min;
        }

        /// <summary>
        /// v0.16.1 UI: item button label; itemId 0 means "any item". The name is supplied by the
        /// caller (LDB lookup) so this stays a pure function.
        /// </summary>
        public static string SplitterRuleItemText(int itemId, string itemName)
        {
            if (itemId <= 0)
            {
                return "任意物品";
            }

            if (itemName == null || itemName.Length == 0)
            {
                return "#" + itemId;
            }

            return itemName;
        }

        /// <summary>
        /// v0.16.1 UI: folded/unfolded header caption of the splitter port rule panel plus the number
        /// of ports that currently carry a rule.
        /// </summary>
        public static string SplitterPanelHeaderText(bool expanded, int ruleCount)
        {
            int count = ruleCount;
            if (count < 0)
            {
                count = 0;
            }

            if (count > SplitterPortCount)
            {
                count = SplitterPortCount;
            }

            return (expanded ? "品质过滤 ▾" : "品质过滤 ▸") + "（" + count + "/" + SplitterPortCount + " 口有规则）";
        }

        /// <summary>
        /// v0.16.1 UI: caption shown in the sorter window, e.g. "磁铁  品质≥5". A threshold of 0
        /// keeps just the item name so the vanilla wording survives when the feature is not used.
        /// </summary>
        public static string SorterFilterText(string itemName, int minPoints)
        {
            string name = itemName == null || itemName.Length == 0 ? "任意物品" : itemName;
            int min = ClampQualityPoints(minPoints);
            return min <= 0 ? name : name + "  品质≥" + min;
        }

        // ------------------------------------------------------------------ v0.19.0 sorter pick helpers

        /// <summary>
        /// v0.19.0: transcription of the vanilla belt window decoder used by
        /// <c>CargoPath.TryPickItem(int index, int length, int filter, out byte stack, out byte inc)</c>.
        /// IL-verified against the 2026-10-19 Assembly-CSharp.dll:
        ///   * the index is clamped to [0, bufferLength - 1] and the window end to bufferLength;
        ///   * a byte below 246 is a distance byte and just moves one step forward;
        ///   * a byte &gt;= 246 is a cargo marker: <c>i += 250 - buffer[i]</c> lands on the marker and
        ///     the cargo id is packed as (b[i+1]-1) + (b[i+2]-1)*100 + (b[i+3]-1)*10000
        ///     + (b[i+4]-1)*1000000;
        ///   * after handling a marker the vanilla loop advances one byte (i++) and keeps scanning.
        /// The caller maps the ids to <c>CargoContainer.cargoPool</c> entries. Keeping this a pure
        /// function lets the offline tests replay a synthetic belt buffer byte for byte.
        /// </summary>
        /// <param name="buffer">CargoPath.buffer (marker/distance bytes).</param>
        /// <param name="bufferLength">CargoPath.bufferLength (the vanilla pathLength property returns exactly this).</param>
        /// <param name="index">First buffer byte of the pick window (vanilla: pivotOnPath + offset - 2).</param>
        /// <param name="length">Window length in bytes (vanilla belt pick: 5).</param>
        /// <param name="cargoIds">Destination buffer for the decoded cargo ids.</param>
        /// <param name="markerIndexes">Destination buffer for the buffer index of each marker.</param>
        /// <returns>Number of cargos decoded into the destination buffers.</returns>
        public static int DecodeBeltWindow(byte[] buffer, int bufferLength, int index, int length,
            int[] cargoIds, int[] markerIndexes)
        {
            if (buffer == null || cargoIds == null || markerIndexes == null || bufferLength <= 0)
            {
                return 0;
            }

            if (index < 0)
            {
                index = 0;
            }
            else if (index >= bufferLength)
            {
                index = bufferLength - 1;
            }

            int end = index + length;
            if (end > bufferLength)
            {
                end = bufferLength;
            }

            int capacity = cargoIds.Length < markerIndexes.Length ? cargoIds.Length : markerIndexes.Length;
            int count = 0;
            int i = index;
            while (i < end && count < capacity)
            {
                if (i < 0 || i >= buffer.Length)
                {
                    break;
                }

                if (buffer[i] < 246)
                {
                    i++;
                    continue;
                }

                i += 250 - buffer[i];
                if (i + 4 >= buffer.Length)
                {
                    break;
                }

                int cargoId = buffer[i + 1] - 1
                    + (buffer[i + 2] - 1) * 100
                    + (buffer[i + 3] - 1) * 10000
                    + (buffer[i + 4] - 1) * 1000000;
                cargoIds[count] = cargoId;
                markerIndexes[count] = i;
                count++;
                i++;
            }

            return count;
        }

        /// <summary>
        /// v0.19.0: transcription of <c>StorageComponent.split_inc(ref int count, ref int inc, int takeCount)</c>
        /// (IL-verified): the points handed to the caller are <c>perItem * takeCount</c> plus the
        /// integer remainder that the remaining stack can no longer carry, so points are conserved
        /// when one item of a mixed stack is taken. Returns the points taken and reports the state
        /// the grid must be left in.
        /// </summary>
        public static int SplitStorageInc(int inc, int count, int takeCount, out int restInc, out int restCount)
        {
            restInc = 0;
            restCount = 0;
            if (count <= 0)
            {
                return 0;
            }

            if (takeCount <= 0)
            {
                restInc = inc;
                restCount = count;
                return 0;
            }

            if (takeCount > count)
            {
                takeCount = count;
            }

            int perItem = inc / count;
            int remainder = inc - perItem * count;
            int newCount = count - takeCount;
            int leftover = remainder - newCount;
            int taken = perItem * takeCount + (leftover > 0 ? leftover : 0);
            restInc = inc - taken;
            restCount = newCount;
            return taken;
        }

        /// <summary>
        /// v0.19.0: per-item points of the bullet stock of an ejector / silo, matching
        /// <c>EjectorComponent.TakeOneBulletUnsafe</c> / <c>SiloComponent.TakeOneBulletUnsafe</c>
        /// (IL-verified: <c>inc = (byte)(bulletInc / bulletCount)</c>, then bulletInc -= inc).
        /// </summary>
        public static int PerBulletInc(int bulletInc, int bulletCount)
        {
            if (bulletCount <= 0 || bulletInc <= 0)
            {
                return 0;
            }

            int perItem = bulletInc / bulletCount;
            return perItem > QualityPointsMax ? QualityPointsMax : perItem;
        }

        /// <summary>v0.19.0: stable display name of a sorter pick source (diagnostics / Describe).</summary>
        public static string SorterSourceName(int sourceIndex)
        {
            switch (sourceIndex)
            {
                case 0:
                    return "belt";
                case 1:
                    return "storage";
                case 2:
                    return "assembler";
                case 3:
                    return "ejector";
                case 4:
                    return "silo";
                case 5:
                    return "station";
                case 6:
                    return "lab";
                case 7:
                    return "powergen";
                default:
                    return "other";
            }
        }

        /// <summary>True when the physical port currently feeds one of the splitter outputs.</summary>
        private static bool PortIsOutput(int[] portBelts, int[] outputBelts, int port)
        {
            int belt = portBelts[port];
            if (belt == 0)
            {
                return false;
            }

            return FindOutputIndex(outputBelts, belt) >= 0;
        }

        /// <summary>Physical port 0..3 that owns this belt id, or -1.</summary>
        private static int PortOfBelt(int[] portBelts, int beltId, int portLimit)
        {
            for (int port = 0; port < portLimit; port++)
            {
                if (portBelts[port] == beltId)
                {
                    return port;
                }
            }

            return -1;
        }

        private static bool ContainsItem(int[] items, int itemId)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == itemId)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
