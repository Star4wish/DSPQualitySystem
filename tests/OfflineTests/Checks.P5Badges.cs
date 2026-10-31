using System;

namespace DSPQualitySystem.OfflineTests
{
    /// <summary>
    /// v0.14.0 (P6 / 物品品质角标): pure functions behind the item icon badges — the per-item point
    /// derivation (inc / count), the visibility gate (feature switch + threshold), the text styles
    /// (number / dot / both) and the four colour tiers. All logic lives in QualityMath.Badges.cs so
    /// it can be covered without the game assemblies.
    /// </summary>
    internal static partial class Program
    {
        private static void CheckBadgePointsFromGrid()
        {
            // Per-item points = grid.inc / grid.count (inc is the whole-cell total).
            Assert(QualityMath.BadgePointsFromGrid(500, 100) == 5, "500 inc over 100 items is 5 points");
            Assert(QualityMath.BadgePointsFromGrid(1000, 100) == 10, "1000 inc over 100 items is 10 points");
            Assert(QualityMath.BadgePointsFromGrid(600, 100) == 6, "600 inc over 100 items is 6 points");
            Assert(QualityMath.BadgePointsFromGrid(100, 1) == 10, "a single item with 100 inc is 10 points");
            Assert(QualityMath.BadgePointsFromGrid(155, 100) == 1, "integer division drops the remainder");
            Assert(QualityMath.BadgePointsFromGrid(150, 10) == QualityMath.BadgeMaxPoints,
                "a per-item value above 10 clamps to the display maximum");
            Assert(QualityMath.BadgePointsFromGrid(99, 100) == 0, "less than one point per item shows nothing");

            // Out-of-range input never produces a badge.
            Assert(QualityMath.BadgePointsFromGrid(0, 100) == 0, "no inc means no badge");
            Assert(QualityMath.BadgePointsFromGrid(500, 0) == 0, "an empty cell has no badge");
            Assert(QualityMath.BadgePointsFromGrid(500, -3) == 0, "negative count is treated as empty");
            Assert(QualityMath.BadgePointsFromGrid(-500, 100) == 0, "negative inc is treated as empty");

            // Above the vanilla maximum the value clamps to 10 (display only).
            Assert(QualityMath.BadgePointsFromGrid(1200, 100) == QualityMath.BadgeMaxPoints,
                "values above the vanilla maximum clamp to 10");
            Assert(QualityMath.BadgeMaxPoints == 10, "the display maximum mirrors Cargo.kIncLevelMax");

            // Monotonic in inc and antitone in count.
            int previous = -1;
            for (int inc = 100; inc <= 1000; inc += 100)
            {
                int points = QualityMath.BadgePointsFromGrid(inc, 100);
                Assert(points >= previous, "more inc never lowers the per-item points");
                previous = points;
            }
            Assert(QualityMath.BadgePointsFromGrid(900, 300) < QualityMath.BadgePointsFromGrid(900, 100),
                "the same total over more items is worth fewer points");
        }

        private static void CheckBadgeVisibility()
        {
            // Feature switch: off means no badge at all.
            Assert(!QualityMath.BadgeVisible(7, false, 1), "the feature switch hides every badge");
            for (int points = 1; points <= QualityMath.BadgeMaxPoints; points++)
            {
                Assert(QualityMath.BadgeVisible(points, true, 1),
                    "any real point value >= 1 is visible with the default threshold");
            }

            // Threshold: clamped to 1..10, everything below stays hidden.
            Assert(QualityMath.BadgeMinPointsClamped(0) == 1, "minPoints clamps up to 1");
            Assert(QualityMath.BadgeMinPointsClamped(-5) == 1, "negative minPoints clamps up to 1");
            Assert(QualityMath.BadgeMinPointsClamped(3) == 3, "a valid minPoints is kept");
            Assert(QualityMath.BadgeMinPointsClamped(11) == QualityMath.BadgeMaxPoints,
                "minPoints above the maximum clamps down to 10");
            for (int points = 1; points < 5; points++)
            {
                Assert(!QualityMath.BadgeVisible(points, true, 5), "below the threshold is hidden");
            }
            Assert(QualityMath.BadgeVisible(5, true, 5), "reaching the threshold is visible");
            Assert(QualityMath.BadgeVisible(9, true, 5), "above the threshold is visible");

            // Degenerate values never show up.
            Assert(!QualityMath.BadgeVisible(0, true, 1), "zero points never gets a badge");
            Assert(!QualityMath.BadgeVisible(-1, true, 1), "negative points never gets a badge");
            Assert(!QualityMath.BadgeVisible(QualityMath.BadgeMaxPoints + 1, true, 1),
                "values above 10 are rejected instead of clamping silently");
        }

        private static void CheckBadgeStylesAndTiers()
        {
            // Style normalization: unknown/empty falls back to 'number', case and spaces ignored.
            Assert(QualityMath.BadgeStyleNormalized(null) == QualityMath.BadgeStyleNumber,
                "null style falls back to number");
            Assert(QualityMath.BadgeStyleNormalized("") == QualityMath.BadgeStyleNumber,
                "empty style falls back to number");
            Assert(QualityMath.BadgeStyleNormalized("DOT") == QualityMath.BadgeStyleDot,
                "style matching is case-insensitive");
            Assert(QualityMath.BadgeStyleNormalized(" Both ") == QualityMath.BadgeStyleBoth,
                "style matching trims spaces");
            Assert(QualityMath.BadgeStyleNormalized("sparkles") == QualityMath.BadgeStyleNumber,
                "an unknown style falls back to number");

            // Labels: number carries the value, dot carries the glyph, both carries glyph + value.
            Assert(QualityMath.BadgeLabelText(7, "number") == "7", "number style prints the value");
            Assert(QualityMath.BadgeLabelText(10, "number") == "10", "two digit values print both digits");
            Assert(QualityMath.BadgeLabelText(7, "dot") == QualityMath.BadgeDotGlyph,
                "dot style prints only the glyph");
            Assert(QualityMath.BadgeLabelText(7, "both") == QualityMath.BadgeDotGlyph + "7",
                "both style prints glyph + value");
            Assert(QualityMath.BadgeLabelText(0, "number") == string.Empty, "zero points has no label");
            Assert(QualityMath.BadgeLabelText(11, "number") == string.Empty, "out-of-range points has no label");
            for (int points = 1; points <= QualityMath.BadgeMaxPoints; points++)
            {
                Assert(QualityMath.BadgeLabelText(points, "number").Length > 0,
                    "every valid point value has a number label");
            }

            // Colour tiers: 1..2 / 3..4 / 5..7 / 8..10, monotonic and never zero inside range.
            Assert(QualityMath.BadgeTier(0) == 0, "no points means no tier");
            Assert(QualityMath.BadgeTier(1) == 1 && QualityMath.BadgeTier(2) == 1, "1-2 points share tier 1");
            Assert(QualityMath.BadgeTier(3) == 2 && QualityMath.BadgeTier(4) == 2, "3-4 points share tier 2");
            Assert(QualityMath.BadgeTier(5) == 3 && QualityMath.BadgeTier(7) == 3, "5-7 points share tier 3");
            Assert(QualityMath.BadgeTier(8) == 4 && QualityMath.BadgeTier(10) == 4, "8-10 points share tier 4");
            int lastTier = 0;
            int lastColor = -1;
            for (int points = 1; points <= QualityMath.BadgeMaxPoints; points++)
            {
                int tier = QualityMath.BadgeTier(points);
                Assert(tier >= lastTier, "the tier never decreases as points grow");
                int rgb = QualityMath.BadgeColorRgb(points);
                Assert(rgb != 0, "every visible tier has a colour");
                if (tier != lastTier)
                {
                    Assert(rgb != lastColor, "each new tier uses a distinct colour");
                }
                lastTier = tier;
                lastColor = rgb;
            }
            Assert(QualityMath.BadgeColorRgb(0) == 0, "no points means no colour");
            Assert(QualityMath.BadgeColorRgb(11) == 0, "out-of-range points means no colour");
            Assert(QualityMath.BadgeColorRgb(1) == 0x93A0AE, "tier 1 keeps its documented grey-blue");
            Assert(QualityMath.BadgeColorRgb(9) == 0xC79A2A, "tier 4 keeps its documented gold");
        }
    }
}
