using System;

namespace DSPQualitySystem
{
    /// <summary>
    /// v0.14.0 (P6 / 物品品质角标): pure functions that decide whether an item icon gets a
    /// quality badge, what text it shows and which colour tier it uses.
    ///
    /// The point value of a cell is the per-item value = grid.inc / grid.count (0..10). A badge is
    /// only shown when the value is >= 0 (actually 1..10) and reaches the configured threshold.
    /// All functions here are game-independent so they can be covered by the offline test harness.
    /// </summary>
    public static partial class QualityMath
    {
        /// <summary>Highest per-item point value that can be displayed (Cargo.kIncLevelMax).</summary>
        public const int BadgeMaxPoints = 10;

        /// <summary>Default visibility threshold (itemQualityBadgeMinPoints).</summary>
        public const int BadgeDefaultMinPoints = 1;

        /// <summary>itemQualityBadgeStyle values.</summary>
        public const string BadgeStyleNumber = "number";
        public const string BadgeStyleDot = "dot";
        public const string BadgeStyleBoth = "both";

        /// <summary>Glyph used by the 'dot' and 'both' styles (U+25CF BLACK CIRCLE).</summary>
        public const string BadgeDotGlyph = "\u25CF";

        /// <summary>Number of colour tiers (1..4); 0 means "no badge".</summary>
        public const int BadgeTierCount = 4;

        /// <summary>
        /// Per-item quality points of one storage grid cell: inc is the whole-cell total, so the
        /// per-item value is inc / count. Returns 0 for empty cells and negative input.
        /// </summary>
        public static int BadgePointsFromGrid(int totalInc, int count)
        {
            if (count <= 0 || totalInc <= 0) return 0;
            int points = totalInc / count;
            if (points < 0) return 0;
            if (points > BadgeMaxPoints) return BadgeMaxPoints;
            return points;
        }

        /// <summary>Clamp the configured minimum to 1..BadgeMaxPoints.</summary>
        public static int BadgeMinPointsClamped(int value)
        {
            if (value < 1) return 1;
            if (value > BadgeMaxPoints) return BadgeMaxPoints;
            return value;
        }

        /// <summary>Normalize itemQualityBadgeStyle; unknown/empty values fall back to 'number'.</summary>
        public static string BadgeStyleNormalized(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return BadgeStyleNumber;
            string v = raw.Trim();
            if (string.Equals(v, BadgeStyleDot, StringComparison.OrdinalIgnoreCase)) return BadgeStyleDot;
            if (string.Equals(v, BadgeStyleBoth, StringComparison.OrdinalIgnoreCase)) return BadgeStyleBoth;
            return BadgeStyleNumber;
        }

        /// <summary>
        /// True when a badge must be shown: the feature is on, the per-item value carries a real
        /// quality point, and it reaches the configured threshold.
        /// </summary>
        public static bool BadgeVisible(int points, bool featureEnabled, int minPoints)
        {
            if (!featureEnabled) return false;
            if (points <= 0 || points > BadgeMaxPoints) return false;
            return points >= BadgeMinPointsClamped(minPoints);
        }

        /// <summary>
        /// Badge caption. 'number' -> "7", 'dot' -> "●", 'both' -> "●7".
        /// Returns an empty string for values outside 1..BadgeMaxPoints.
        /// </summary>
        public static string BadgeLabelText(int points, string style)
        {
            if (points <= 0 || points > BadgeMaxPoints) return string.Empty;
            string s = BadgeStyleNormalized(style);
            if (string.Equals(s, BadgeStyleDot, StringComparison.Ordinal)) return BadgeDotGlyph;
            if (string.Equals(s, BadgeStyleBoth, StringComparison.Ordinal))
            {
                return BadgeDotGlyph + points.ToString();
            }
            return points.ToString();
        }

        /// <summary>
        /// Colour tier: 1 = 1..2 points (grey-blue), 2 = 3..4 (cyan), 3 = 5..7 (purple),
        /// 4 = 8..10 (gold). 0 when there is no badge.
        /// </summary>
        public static int BadgeTier(int points)
        {
            if (points <= 0 || points > BadgeMaxPoints) return 0;
            if (points <= 2) return 1;
            if (points <= 4) return 2;
            if (points <= 7) return 3;
            return 4;
        }

        /// <summary>Badge colour as 0xRRGGBB for the given per-item points; 0 = no badge.</summary>
        public static int BadgeColorRgb(int points)
        {
            switch (BadgeTier(points))
            {
                case 1: return 0x93A0AE;
                case 2: return 0x2F86AE;
                case 3: return 0x7A5BB0;
                case 4: return 0xC79A2A;
                default: return 0;
            }
        }
    }
}
