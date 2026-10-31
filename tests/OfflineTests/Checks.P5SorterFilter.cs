using System;

namespace DSPQualitySystem.OfflineTests
{
    /// <summary>
    /// v0.19.0 (task-5): pure helpers behind the sorter quality threshold fix.
    ///
    /// Covers the vanilla belt window decoder (CargoPath.TryPickItem: marker byte &gt;= 246,
    /// i += 250 - buffer[i], cargo id packed as (b1-1) + (b2-1)*100 + (b3-1)*10000
    /// + (b4-1)*1000000, one-byte advance after a marker), the vanilla storage split_inc formula
    /// (StorageComponent.split_inc, points conserved when one item leaves a mixed stack), the
    /// per-bullet points of EjectorComponent/SiloComponent and the source classification used by
    /// P5_SorterFilter.Describe(). Registration lives in Program.cs Main().
    /// </summary>
    internal static partial class Program
    {
        private static void CheckSorterFilter()
        {
            CheckSorterFilterWindow();
            CheckSorterFilterStorageSplit();
            CheckSorterFilterBullets();
            CheckSorterFilterSources();
            CheckSorterFilterPick();
            CheckSorterFilterBlueprintBlock();
        }

        /// <summary>Writes the vanilla 4-byte cargo id payload that follows a marker byte.</summary>
        private static void PutCargoId(byte[] buffer, int markerIndex, int cargoId)
        {
            int rest = cargoId;
            buffer[markerIndex + 1] = (byte)(rest % 100 + 1);
            rest /= 100;
            buffer[markerIndex + 2] = (byte)(rest % 100 + 1);
            rest /= 100;
            buffer[markerIndex + 3] = (byte)(rest % 100 + 1);
            rest /= 100;
            buffer[markerIndex + 4] = (byte)(rest % 100 + 1);
        }

        private static void CheckSorterFilterWindow()
        {
            int[] ids = new int[8];
            int[] markers = new int[8];
            Assert(QualityMath.DecodeBeltWindow(null, 0, 0, 5, ids, markers) == 0,
                "null buffer must decode to zero cargos");

            byte[] buffer = new byte[64];
            Assert(QualityMath.DecodeBeltWindow(buffer, 64, 0, 5, ids, markers) == 0,
                "empty window must decode to zero cargos");

            // One cargo whose marker sits exactly at the window start (250 - 250 = 0 advance).
            buffer[0] = 250;
            PutCargoId(buffer, 0, 12345);
            int count = QualityMath.DecodeBeltWindow(buffer, 64, 0, 5, ids, markers);
            Assert(count == 1, "single marker must decode one cargo, got " + count);
            Assert(ids[0] == 12345, "cargo id round trip, got " + ids[0]);
            Assert(markers[0] == 0, "marker index must be the vanilla marker position, got " + markers[0]);

            // Distance marker: 246 means the marker is 4 bytes further ahead.
            Array.Clear(buffer, 0, buffer.Length);
            buffer[0] = 246;
            PutCargoId(buffer, 4, 7);
            count = QualityMath.DecodeBeltWindow(buffer, 64, 0, 5, ids, markers);
            Assert(count == 1 && ids[0] == 7 && markers[0] == 4,
                "distance marker must land on index 4 with cargo id 7, got count=" + count
                + " id=" + (count > 0 ? ids[0] : -1) + " marker=" + (count > 0 ? markers[0] : -1));

            // Two cargos inside one long window, decoded in belt order.
            Array.Clear(buffer, 0, buffer.Length);
            buffer[0] = 250;
            PutCargoId(buffer, 0, 101);
            buffer[5] = 250;
            PutCargoId(buffer, 5, 202);
            count = QualityMath.DecodeBeltWindow(buffer, 64, 0, 10, ids, markers);
            Assert(count == 2, "two markers must decode two cargos, got " + count);
            Assert(ids[0] == 101 && ids[1] == 202, "cargos keep belt order");
            Assert(markers[0] == 0 && markers[1] == 5, "marker indexes follow belt order");

            // The same window shifted by five bytes only sees the second cargo.
            count = QualityMath.DecodeBeltWindow(buffer, 64, 5, 5, ids, markers);
            Assert(count == 1 && ids[0] == 202 && markers[0] == 5,
                "shifted window must only see the second cargo, got " + count);

            // Index clamps: negative -> 0, past the end -> bufferLength - 1, end clamps to bufferLength.
            count = QualityMath.DecodeBeltWindow(buffer, 10, -3, 5, ids, markers);
            Assert(count == 1 && ids[0] == 101, "negative index must clamp to 0, got " + count);
            count = QualityMath.DecodeBeltWindow(buffer, 64, 99, 5, ids, markers);
            Assert(count == 0, "index past the buffer length must clamp to an empty byte, got " + count);
            // Vanilla clamps the start to bufferLength - 1, so a marker there is decoded even though
            // its four payload bytes sit past the end: the decoder must mirror that quirk.
            count = QualityMath.DecodeBeltWindow(buffer, 6, 99, 5, ids, markers);
            Assert(count == 1 && ids[0] == 202 && markers[0] == 5,
                "clamped index on a marker still decodes like vanilla, got " + count);
            Assert(QualityMath.DecodeBeltWindow(buffer, 64, 0, 0, ids, markers) == 0,
                "zero length window must decode nothing");
            Assert(QualityMath.DecodeBeltWindow(buffer, 64, 0, 5, new int[0], new int[0]) == 0,
                "zero capacity destination must not overflow");
        }

        private static void CheckSorterFilterStorageSplit()
        {
            int restInc;
            int restCount;

            // 30 points over 3 items, take one: 10 points leave, the rest stays.
            int taken = QualityMath.SplitStorageInc(30, 3, 1, out restInc, out restCount);
            Assert(taken == 10 && restInc == 20 && restCount == 2,
                "30/3 take 1 must leave 20/2, got " + taken + "/" + restInc + "/" + restCount);

            // The integer remainder cannot be split: it stays with the remaining stack.
            taken = QualityMath.SplitStorageInc(31, 3, 1, out restInc, out restCount);
            Assert(taken == 10 && restInc == 21 && restCount == 2,
                "31/3 take 1 must leave 21/2, got " + taken + "/" + restInc + "/" + restCount);

            taken = QualityMath.SplitStorageInc(1, 2, 1, out restInc, out restCount);
            Assert(taken == 0 && restInc == 1 && restCount == 1,
                "1/2 take 1 must leave the single point behind, got " + taken + "/" + restInc + "/" + restCount);

            // Degenerate and clamped inputs.
            Assert(QualityMath.SplitStorageInc(0, 0, 1, out restInc, out restCount) == 0
                && restInc == 0 && restCount == 0, "empty grid must not take anything");
            taken = QualityMath.SplitStorageInc(5, 2, 3, out restInc, out restCount);
            Assert(taken == 5 && restInc == 0 && restCount == 0,
                "over-sized take must consume the whole grid, got " + taken + "/" + restInc + "/" + restCount);
            taken = QualityMath.SplitStorageInc(40, 4, 0, out restInc, out restCount);
            Assert(taken == 0 && restInc == 40 && restCount == 4,
                "take zero must leave the grid untouched, got " + taken + "/" + restInc + "/" + restCount);

            // Conservation: whatever leaves plus whatever stays must equal the grid total.
            int[] incs = { 0, 1, 2, 9, 30, 31, 100, 101 };
            int[] counts = { 1, 2, 3, 4, 5 };
            for (int i = 0; i < incs.Length; i++)
            {
                for (int c = 0; c < counts.Length; c++)
                {
                    int inc = incs[i];
                    int itemCount = counts[c];
                    for (int take = 1; take <= itemCount; take++)
                    {
                        taken = QualityMath.SplitStorageInc(inc, itemCount, take, out restInc, out restCount);
                        Assert(taken + restInc == inc && restCount == itemCount - take,
                            "points must be conserved for inc=" + inc + " count=" + itemCount + " take=" + take);
                        Assert(taken >= 0 && restInc >= 0, "split must never produce negative points");
                    }
                }
            }
        }

        private static void CheckSorterFilterBullets()
        {
            Assert(QualityMath.PerBulletInc(30, 3) == 10, "30 points over 3 bullets is 10 per bullet");
            Assert(QualityMath.PerBulletInc(29, 3) == 9, "29 points over 3 bullets floors to 9");
            Assert(QualityMath.PerBulletInc(2, 3) == 0, "fewer points than bullets floors to 0");
            Assert(QualityMath.PerBulletInc(0, 3) == 0, "no points means no quality");
            Assert(QualityMath.PerBulletInc(5, 0) == 0, "empty stock means no quality");
            Assert(QualityMath.PerBulletInc(100, 3) == QualityMath.QualityPointsMax,
                "per-bullet points clamp to the vanilla max level");
        }

        private static void CheckSorterFilterSources()
        {
            Assert(QualityMath.SorterSourceName(0) == "belt", "source 0 is the belt");
            Assert(QualityMath.SorterSourceName(1) == "storage", "source 1 is the storage");
            Assert(QualityMath.SorterSourceName(2) == "assembler", "source 2 is the assembler");
            Assert(QualityMath.SorterSourceName(3) == "ejector", "source 3 is the ejector");
            Assert(QualityMath.SorterSourceName(4) == "silo", "source 4 is the silo");
            Assert(QualityMath.SorterSourceName(5) == "station", "source 5 is the station");
            Assert(QualityMath.SorterSourceName(6) == "lab", "source 6 is the lab");
            Assert(QualityMath.SorterSourceName(7) == "powergen", "source 7 is the power generator");
            Assert(QualityMath.SorterSourceName(8) == "other" && QualityMath.SorterSourceName(-1) == "other",
                "unknown source indexes fall back to other");
        }

        private static void CheckSorterFilterPick()
        {
            int[] items = { 1001, 1001, 1001 };
            int[] incs = { 0, 5, 10 };

            Assert(QualityMath.PlanSorterPick(items, incs, 3, 1001, 10, null) == 2,
                "threshold 10 must pick the 10-point cargo, not the first match");
            Assert(QualityMath.PlanSorterPick(items, incs, 3, 1001, 5, null) == 1,
                "threshold 5 must pick the first cargo that reaches 5");
            Assert(QualityMath.PlanSorterPick(items, incs, 3, 1001, 0, null) == 0,
                "threshold 0 keeps the vanilla first match");
            Assert(QualityMath.PlanSorterPick(items, incs, 3, 2000, 0, null) == -1,
                "a mismatching item filter rejects every cargo");
            Assert(QualityMath.PlanSorterPick(items, incs, 3, 1001, 11, null) == 2,
                "a threshold above the max level clamps to 10 and picks the 10-point cargo");
            Assert(QualityMath.PlanSorterPick(items, incs, 3, 1001, 5, new[] { 1001, 0, 0, 0, 0, 0 }) == 1,
                "the care-needs list is applied in addition to the item filter");
            Assert(QualityMath.PlanSorterPick(items, incs, 3, 1001, 5, new[] { 2000, 0, 0, 0, 0, 0 }) == -1,
                "an absent care-need item rejects every cargo");
            Assert(QualityMath.PlanSorterPick(items, incs, 0, 1001, 0, null) == -1,
                "an empty window yields no pick");
        }

        /// <summary>
        /// v0.19.2 (task-6): the tail block that carries splitter rules / sorter threshold inside a
        /// blueprint's vanilla BuildingParameters int[] array.
        /// </summary>
        private static void CheckSorterFilterBlueprintBlock()
        {
            int[] rules = { 1, 0, 2, 3 };

            // Appending must not touch the vanilla prefix and must keep both arrays independent.
            int[] vanilla = { 11, 22, 33 };
            int[] appended = QualityMath.AppendBlueprintBlock(vanilla, QualityMath.BlueprintBlockKindSplitter, 2314,
                rules, 7);
            Assert(appended.Length == vanilla.Length + QualityMath.BlueprintBlockLength,
                "append must grow the array by exactly one block, got " + appended.Length);
            Assert(appended[0] == 11 && appended[1] == 22 && appended[2] == 33,
                "the vanilla prefix must be preserved");
            Assert(vanilla.Length == 3 && vanilla[0] == 11,
                "append must not modify the caller array");

            Assert(QualityMath.TryReadBlueprintBlock(appended, out int kind, out int protoId, out int[] readRules,
                    out int min)
                && kind == QualityMath.BlueprintBlockKindSplitter && protoId == 2314 && min == 7,
                "round trip must recover kind/proto/min, got kind=" + kind + " proto=" + protoId + " min=" + min);
            Assert(readRules != null && readRules[0] == 1 && readRules[1] == 0 && readRules[2] == 2 && readRules[3] == 3,
                "round trip must recover all four splitter rules");

            // A null / empty vanilla array still gets a block on its own.
            int[] fromNull = QualityMath.AppendBlueprintBlock(null, QualityMath.BlueprintBlockKindSorter, 2315, null, 4);
            Assert(fromNull.Length == QualityMath.BlueprintBlockLength,
                "appending to a null array yields just the block, got " + fromNull.Length);
            Assert(QualityMath.TryReadBlueprintBlock(fromNull, out kind, out protoId, out readRules, out min)
                && kind == QualityMath.BlueprintBlockKindSorter && protoId == 2315 && min == 4,
                "sorter block round trip");
            Assert(readRules != null && readRules[0] == 0 && readRules[3] == 0,
                "missing rules default to SplitterRuleNone");

            // Old blueprints / foreign arrays / garbage must be rejected, never half-read.
            Assert(!QualityMath.TryReadBlueprintBlock(null, out _, out _, out _, out _),
                "null array has no block");
            Assert(!QualityMath.TryReadBlueprintBlock(new int[0], out _, out _, out _, out _),
                "empty array has no block");
            Assert(!QualityMath.TryReadBlueprintBlock(vanilla, out _, out _, out _, out _),
                "vanilla-only array has no block");
            int[] truncated = { QualityMath.BlueprintBlockMagic, QualityMath.BlueprintBlockVersion };
            Assert(!QualityMath.TryReadBlueprintBlock(truncated, out _, out _, out _, out _),
                "a short array is rejected even if it starts with the magic");
            int[] wrongVersion = QualityMath.AppendBlueprintBlock(vanilla, QualityMath.BlueprintBlockKindSplitter, 2314,
                rules, 7);
            wrongVersion[wrongVersion.Length - QualityMath.BlueprintBlockLength + 1] = 99;
            Assert(!QualityMath.TryReadBlueprintBlock(wrongVersion, out _, out _, out _, out _),
                "an unknown block version is rejected");
            int[] wrongKind = QualityMath.AppendBlueprintBlock(vanilla, QualityMath.BlueprintBlockKindSplitter, 2314,
                rules, 7);
            wrongKind[wrongKind.Length - QualityMath.BlueprintBlockLength + 2] = 7;
            Assert(!QualityMath.TryReadBlueprintBlock(wrongKind, out _, out _, out _, out _),
                "an unknown block kind is rejected");
            int[] noProto = QualityMath.AppendBlueprintBlock(vanilla, QualityMath.BlueprintBlockKindSorter, 0, rules, 7);
            Assert(!QualityMath.TryReadBlueprintBlock(noProto, out _, out _, out _, out _),
                "a block without a proto id is rejected");

            // A block buried in the middle is not a tail block: only the last nine ints count.
            int[] middle = QualityMath.AppendBlueprintBlock(vanilla, QualityMath.BlueprintBlockKindSorter, 2315, rules, 4);
            int[] shifted = new int[middle.Length + 1];
            Array.Copy(middle, shifted, middle.Length);
            Assert(!QualityMath.TryReadBlueprintBlock(shifted, out _, out _, out _, out _),
                "a block that is not at the array tail is ignored");

            // An out-of-range threshold is normalised instead of being applied blindly.
            int[] badMin = QualityMath.AppendBlueprintBlock(vanilla, QualityMath.BlueprintBlockKindSorter, 2315, rules,
                42);
            Assert(QualityMath.TryReadBlueprintBlock(badMin, out _, out _, out _, out min) && min == QualityMath.SorterMinNone,
                "an out-of-range threshold falls back to SorterMinNone, got " + min);
        }
    }
}
