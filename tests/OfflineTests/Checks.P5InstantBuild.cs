using System;

namespace DSPQualitySystem.OfflineTests
{
    /// <summary>
    /// v0.17.0 pure helpers for the two field regressions:
    ///  - Bug 1 "大型采矿机 placed from the backpack comes out 0 points": the normal hand-build path
    ///    now binds the points of the item that was actually consumed (shared receipt).
    ///  - Bug 2 "Shift+click instant build loses quality": the instant-build frame consumes the
    ///    highest quality matching grid and binds its points through the same receipt.
    ///
    /// All assertions below are pure math / matching rules, so they run without the game.
    /// </summary>
    internal static partial class Program
    {
        /// <summary>Receipt hundredths -> whole per-item points (floor + clamp + degenerate).</summary>
        private static void CheckReceiptPerItem()
        {
            Assert(QualityMath.ReceiptPerItem(0) == 0, "receipt: 0 hundredths -> 0 points");
            Assert(QualityMath.ReceiptPerItem(-150) == 0, "receipt: negative hundredths -> 0 points");
            Assert(QualityMath.ReceiptPerItem(99) == 0, "receipt: 0.99 points floors to 0");
            Assert(QualityMath.ReceiptPerItem(100) == 1, "receipt: 1.00 points -> 1");
            Assert(QualityMath.ReceiptPerItem(450) == 4, "receipt: 4.50 points floors to 4");
            Assert(QualityMath.ReceiptPerItem(1000) == QualityMath.IncPerItemMax,
                "receipt: a perfect item keeps 10 points");
            Assert(QualityMath.ReceiptPerItem(1200) == QualityMath.IncPerItemMax,
                "receipt: above the cap clamps to 10");

            int last = -1;
            for (int per100 = 0; per100 <= 1500; per100 += 25)
            {
                int value = QualityMath.ReceiptPerItem(per100);
                Assert(value >= 0 && value <= QualityMath.IncPerItemMax,
                    "receipt: value out of range at " + per100 + ": " + value);
                Assert(value >= last, "receipt: not monotonic at " + per100);
                last = value;
            }
        }

        /// <summary>
        /// A receipt matches the prebuild proto id; a wildcard (0) matches anything. This is the only
        /// gate between the consumption and the binding, and it is deliberately item-agnostic: a
        /// station-type building (大型采矿机, item 2316) and a normal building (小型采矿机, item 2301)
        /// run through exactly the same rule.
        /// </summary>
        private static void CheckReceiptItemMatches()
        {
            Assert(QualityMath.ReceiptItemMatches(2316, 2316), "receipt match: station item matches itself");
            Assert(QualityMath.ReceiptItemMatches(2301, 2301), "receipt match: normal item matches itself");
            Assert(!QualityMath.ReceiptItemMatches(2301, 2316),
                "receipt match: a different building item is rejected");
            Assert(QualityMath.ReceiptItemMatches(0, 2316), "receipt match: unknown receipt item is a wildcard");
            Assert(QualityMath.ReceiptItemMatches(2316, 0), "receipt match: unknown expected item is a wildcard");
            Assert(QualityMath.ReceiptItemMatches(0, 0), "receipt match: 0/0 wildcard");
            Assert(QualityMath.ReceiptItemMatches(-5, 2316), "receipt match: negative id behaves as unknown");
        }

        /// <summary>The three consume windows (paste / hand build / instant build) share one predicate.</summary>
        private static void CheckConsumeWindowUnion()
        {
            Assert(!QualityMath.ConsumeWindowActive(false, false, false),
                "window: no window outside a consume context");
            Assert(QualityMath.ConsumeWindowActive(true, false, false), "window: blueprint paste opens it");
            Assert(QualityMath.ConsumeWindowActive(false, true, false), "window: hand build opens it");
            Assert(QualityMath.ConsumeWindowActive(false, false, true),
                "window: instant build opens it (v0.17.0 fix)");
            Assert(QualityMath.ConsumeWindowActive(true, true, true), "window: union of all three");
        }

        /// <summary>
        /// count &gt; 1 dilution: a multi-item instant build divides the consumed total points over the
        /// consumed item count. The helper is item-agnostic, so the station-type 大型采矿机 uses the
        /// same rule as a normal building.
        /// </summary>
        private static void CheckInstantDilution()
        {
            // 大型采矿机 case: one building item carrying 4 points.
            Assert(QualityMath.InstantBuildPerItem(4, 1) == 4, "instant: single 4-point item -> 4");
            // count > 1 dilutes the total points (e.g. a recipe requiring several items).
            Assert(QualityMath.InstantBuildPerItem(16, 4) == 4, "instant: 16 points over 4 items -> 4 each");
            Assert(QualityMath.InstantBuildPerItem(12, 3) == 4, "instant: 12 points over 3 items -> 4 each");
            Assert(QualityMath.InstantBuildPerItem(8, 4) == 2, "instant: 8 points over 4 items -> 2 each");
            Assert(QualityMath.InstantBuildPerItem(11, 3) == 3, "instant: 11/3 floors to 3");
            Assert(QualityMath.InstantBuildPerItem(3, 4) == 0, "instant: average below one point -> 0");
            // Clamp / degenerate.
            Assert(QualityMath.InstantBuildPerItem(999, 2) == QualityMath.IncPerItemMax,
                "instant: per-item clamps to 10");
            Assert(QualityMath.InstantBuildPerItem(0, 1) == 0, "instant: 0-point item stays 0");
            Assert(QualityMath.InstantBuildPerItem(20, 0) == 0, "instant: zero count -> 0");

            // The same helper for both building families means the station-type building cannot take a
            // different code path just because its prefab is a vein collector.
            int station = QualityMath.InstantBuildPerItem(4, 1);
            int normal = QualityMath.InstantBuildPerItem(4, 1);
            Assert(station == normal, "instant: station-type and normal building use the same conversion");
        }

        /// <summary>
        /// Which value a freshly created prebuild receives: the receipt of the highest-quality
        /// consumption wins, the raw take record is the fallback, 0 means "vanilla".
        /// </summary>
        private static void CheckPreferBindingValue()
        {
            Assert(QualityMath.PreferBindingValue(4, 0) == 4, "bind: receipt only -> 4");
            Assert(QualityMath.PreferBindingValue(0, 5) == 5, "bind: record fallback -> 5");
            Assert(QualityMath.PreferBindingValue(4, 5) == 4, "bind: receipt wins over record");
            Assert(QualityMath.PreferBindingValue(0, 0) == 0, "bind: nothing recorded -> vanilla 0");
            Assert(QualityMath.PreferBindingValue(-3, 0) == 0, "bind: negative receipt is ignored");
            Assert(QualityMath.PreferBindingValue(99, 0) == QualityMath.IncPerItemMax,
                "bind: clamped to the item cap");
            Assert(QualityMath.PreferBindingValue(0, 99) == QualityMath.IncPerItemMax,
                "bind: fallback clamped to the item cap");
        }

        /// <summary>
        /// End-to-end decision table for the prebuild receipt: backpack build (Bug 1), instant build
        /// (Bug 2) and the degraded vanilla case.
        /// </summary>
        private static void CheckPrebuildReceiptScenario()
        {
            // Bug 1: backpack branch of CreatePrebuilds consumed a 4-point item -> receipt 4/100.
            int backpackBuilt = QualityMath.PreferBindingValue(
                QualityMath.ReceiptPerItem(400), 0);
            Assert(backpackBuilt == 4, "scenario: backpack-built building inherits 4 points");

            // Bug 2: instant build consumed the highest quality grid -> receipt 4/100, no raw record.
            int instantBuilt = QualityMath.PreferBindingValue(
                QualityMath.ReceiptPerItem(400), QualityMath.InstantBuildPerItem(0, 1));
            Assert(instantBuilt == 4, "scenario: instant-built building inherits 4 points");

            // Fallback: receipt unavailable (P4 not armed), raw take carried 4 points over one item.
            int fallback = QualityMath.PreferBindingValue(0, QualityMath.InstantBuildPerItem(4, 1));
            Assert(fallback == 4, "scenario: raw take record still binds when no receipt exists");

            // Degradation: 0-point consumption (or no capture at all) keeps vanilla 0 points.
            Assert(QualityMath.PreferBindingValue(0, QualityMath.InstantBuildPerItem(0, 1)) == 0,
                "scenario: 0-point input builds a 0-point building");
            Assert(QualityMath.PreferBindingValue(
                    QualityMath.ReceiptPerItem(0), 0) == 0,
                "scenario: empty receipt stays vanilla");
        }
    }
}
