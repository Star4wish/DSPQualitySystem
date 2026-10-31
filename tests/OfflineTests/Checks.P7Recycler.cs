using System;
using System.Collections.Generic;
using System.Text;
using DSPQualitySystem;
using DSPQualitySystem.Save;

namespace DSPQualitySystem.OfflineTests
{
    /// <summary>
    /// v0.19.0 (task-2 B, design A) offline checks for the quality recycler. The belt plumbing lives in
    /// Patches\P7_Recycler.cs (game types) and is asserted at runtime by its own Describe/SelfCheck,
    /// while everything decidable in isolation is here: the 4-points-to-one-spray-use conversion, the
    /// tank room clamp, the carried remainder (0..3 points), the per-item harvest gate, and the save
    /// node round trip (including the obsolete design-B "o" field, which must be ignored).
    /// </summary>
    internal static partial class Program
    {
        // ------------------------------------------------------------------ conversion math

        private static void CheckRecyclerBottles()
        {
            Assert(QualityRecyclerMath.PointsPerSpray == 4, "4 quality points must buy one spray use");
            Assert(QualityRecyclerMath.DefaultMinPerItem == 1,
                "the default harvest threshold accepts any cargo that carries at least one point");
            Assert(QualityRecyclerMath.DefaultCapacity == 600,
                "the vanilla spray tank capacity is 600 spray uses");

            Assert(QualityRecyclerMath.PointsFromCargoInc(9) == 9, "positive inc is the point count");
            Assert(QualityRecyclerMath.PointsFromCargoInc(0) == 0, "an unsprayed cargo carries no points");
            Assert(QualityRecyclerMath.PointsFromCargoInc(-7) == 0, "negative inc is treated as none");

            int remainder;
            Assert(QualityRecyclerMath.SpraysFromPoints(4, out remainder) == 1 && remainder == 0,
                "4 points = 1 spray, nothing carried");
            Assert(QualityRecyclerMath.SpraysFromPoints(7, out remainder) == 1 && remainder == 3,
                "7 points = 1 spray + 3 carried");
            Assert(QualityRecyclerMath.SpraysFromPoints(3, out remainder) == 0 && remainder == 3,
                "3 points = 0 sprays + 3 carried");
            Assert(QualityRecyclerMath.SpraysFromPoints(0, out remainder) == 0 && remainder == 0,
                "0 points = nothing");
            Assert(QualityRecyclerMath.SpraysFromPoints(-9, out remainder) == 0 && remainder == 0,
                "negative points must be ignored");
            Assert(QualityRecyclerMath.SpraysFromPoints(10, out remainder) == 2 && remainder == 2,
                "a fully painted item (10 points) = 2 sprays + 2 carried");
            Assert(QualityRecyclerMath.SpraysFromPoints(60, out remainder) == 15 && remainder == 0,
                "a full Mk.III-worth (60 points) = 15 sprays");
            Assert(QualityRecyclerMath.SpraysFromPoints(1, out remainder) == 0 && remainder == 1,
                "1 point = 0 sprays + 1 carried");

            // Carrying across harvests: sprays * 4 + remainder must equal the points paid in.
            int sprays = QualityRecyclerMath.SpraysFromPoints(11, out remainder);
            Assert(sprays * QualityRecyclerMath.PointsPerSpray + remainder == 11,
                "sprays * 4 + remainder accounts for every harvested point");

            Assert(QualityRecyclerMath.MergePoints(3, 1) == 4, "carry + 1 fresh point = 4");
            Assert(QualityRecyclerMath.MergePoints(0, 9) == 9, "empty account + 9 points");
            Assert(QualityRecyclerMath.MergePoints(-5, 2) == 2, "a corrupt negative account restarts at 2");
            Assert(QualityRecyclerMath.MergePoints(3, -4) == 3, "fresh negative points never subtract");
            Assert(QualityRecyclerMath.MergePoints(1 << 20, 50) == 1 << 20,
                "overflow guard clamps the account");

            Assert(QualityRecyclerMath.FreeRoom(600, 600, 0) == 0, "a full tank has no room");
            Assert(QualityRecyclerMath.FreeRoom(600, 599, 0) == 1, "one free spray");
            Assert(QualityRecyclerMath.FreeRoom(600, 598, -5) == 2,
                "a negative extra count must not shrink the room");
            Assert(QualityRecyclerMath.FreeRoom(0, 10, 10) == 580,
                "an impossible capacity falls back to 600");

            Assert(QualityRecyclerMath.SpraysThatFit(10, 2) == 2, "the tank room caps the sprays");
            Assert(QualityRecyclerMath.SpraysThatFit(10, 10) == 2, "points cap the sprays");
            Assert(QualityRecyclerMath.SpraysThatFit(2, 5) == 0, "less than 4 points fits no spray");
            Assert(QualityRecyclerMath.SpraysThatFit(10, 0) == 0, "a full tank takes nothing");
        }

        // ------------------------------------------------------------------ harvest gate

        private static void CheckRecyclerHarvestGate()
        {
            // With the shipped default (1) every cargo that really carries points is recyclable.
            Assert(QualityRecyclerMath.ShouldHarvest(1, 1, QualityRecyclerMath.DefaultMinPerItem),
                "a single Mk.I point is recyclable with the shipped threshold");
            Assert(QualityRecyclerMath.ShouldHarvest(2, 1, QualityRecyclerMath.DefaultMinPerItem),
                "a Mk.II item (2 points) is recyclable with the shipped threshold");
            Assert(QualityRecyclerMath.ShouldHarvest(11, 4, QualityRecyclerMath.DefaultMinPerItem),
                "2 points/item is above the shipped threshold");
            Assert(!QualityRecyclerMath.ShouldHarvest(0, 1, QualityRecyclerMath.DefaultMinPerItem),
                "unsprayed cargo still flows through untouched");

            Assert(QualityRecyclerMath.ShouldHarvest(9, 3, 3), "3 points/item must be harvested");
            Assert(QualityRecyclerMath.ShouldHarvest(10, 1, 3), "10 points on one item is harvested");
            Assert(QualityRecyclerMath.ShouldHarvest(6, 2, 3), "6 points over 2 items = 3/item");
            Assert(QualityRecyclerMath.ShouldHarvest(12, 4, 3), "12 points over 4 items = 3/item");
            Assert(!QualityRecyclerMath.ShouldHarvest(11, 4, 3), "11 points over 4 items = 2/item, kept");
            Assert(!QualityRecyclerMath.ShouldHarvest(5, 2, 3), "5 points over 2 items = 2/item, kept");
            Assert(!QualityRecyclerMath.ShouldHarvest(2, 1, 3), "2 points/item (Mk.II) passes through");
            Assert(!QualityRecyclerMath.ShouldHarvest(0, 1, 3), "no points, nothing to harvest");
            Assert(!QualityRecyclerMath.ShouldHarvest(4, 0, 3), "degenerate stack is ignored");
            Assert(!QualityRecyclerMath.ShouldHarvest(-4, 1, 3), "negative inc is ignored");
            Assert(!QualityRecyclerMath.ShouldHarvest(4, -1, 3), "negative stack is ignored");
            Assert(QualityRecyclerMath.ShouldHarvest(1, 1, 0), "min 0 harvests anything with points");
            Assert(QualityRecyclerMath.ShouldHarvest(1, 1, -2), "negative min clamps to 0");

            // The gate must agree with the mod-wide per-item definition (floor of inc / stack).
            Assert(QualityRecyclerMath.ShouldHarvest(12, 4, 3) == (QualityMath.PerItemPoints(12, 4) >= 3),
                "gate matches QualityMath.PerItemPoints");
            Assert(QualityRecyclerMath.ShouldHarvest(11, 4, 3) == (QualityMath.PerItemPoints(11, 4) >= 3),
                "gate matches QualityMath.PerItemPoints for a floor case");

            // A whole fully painted 4-stack (40 points on 10-point items) pays 10 sprays.
            int remainder;
            Assert(QualityRecyclerMath.SpraysFromPoints(40, out remainder) == 10 && remainder == 0,
                "4 fully painted items = 10 spray uses");
        }

        // ------------------------------------------------------------------ carried remainder

        private static void CheckRecyclerAccumulation()
        {
            QualityData.ClearAllRecyclers();
            const int planet = 101;
            const int entity = 42;

            Assert(QualityData.AddRecyclerPoints(planet, entity, 3) == 3,
                "3 points are carried in the account");
            Assert(QualityData.GetRecyclerState(planet, entity).Points == 3,
                "the account keeps the carried points");
            Assert(QualityData.HasRecyclerState(planet, entity), "a carried point is real state");

            Assert(QualityData.AddRecyclerPoints(planet, entity, 1) == 4,
                "1 more point completes a spray use");
            Assert(QualityData.TakeRecyclerPoints(planet, entity, 4) == 4,
                "the completed spray use is paid out");
            Assert(!QualityData.HasRecyclerState(planet, entity),
                "an emptied account disappears (nothing to save)");

            Assert(QualityData.TakeRecyclerPoints(planet, entity, 4) == 0,
                "nothing is paid out of an empty account");

            QualityData.AddRecyclerPoints(planet, entity, 9);
            Assert(QualityData.TakeRecyclerPoints(planet, entity, 4) == 4,
                "one spray use is paid out of 9 points");
            Assert(QualityData.GetRecyclerState(planet, entity).Points == 5,
                "the 5 leftover points stay carried");

            QualityData.SetRecyclerPoints(planet, entity, 2);
            Assert(QualityData.GetRecyclerState(planet, entity).Points == 2,
                "a stored remainder can be written back verbatim");
            QualityData.SetRecyclerPoints(planet, entity, 0);
            Assert(!QualityData.HasRecyclerState(planet, entity), "zero points clear the account");

            // Invalid input never changes anything and never throws.
            Assert(QualityData.AddRecyclerPoints(planet, 0, 4) == 0, "entity 0 is ignored");
            Assert(QualityData.AddRecyclerPoints(-1, 5, 4) == 0, "negative planet id is ignored");
            Assert(QualityData.AddRecyclerPoints(planet, entity, 0) == 0, "0 points are ignored");
            Assert(QualityData.AddRecyclerPoints(planet, entity, -4) == 0, "negative points are ignored");
            Assert(QualityData.TakeRecyclerPoints(planet, entity, 0) == 0, "taking 0 is ignored");
            Assert(!QualityData.HasRecyclerState(planet, entity), "ignored calls leave no state");

            // Overflow guard is reachable through the account API too.
            Assert(QualityData.AddRecyclerPoints(planet, entity, 1 << 20) == 1 << 20,
                "the account is clamped instead of overflowing");
            QualityData.ClearAllRecyclers();

            // A second recycler on the same planet owns its own account.
            QualityData.AddRecyclerPoints(planet, 43, 6);
            QualityData.AddRecyclerPoints(planet, entity, 2);
            Assert(QualityData.GetRecyclerState(planet, 43).Points == 6,
                "per-entity accounts are independent");
            Assert(QualityData.GetRecyclerState(planet, entity).Points == 2,
                "the second account is unaffected");
            Assert(QualityData.RecyclerEntityIds(planet).Count == 2,
                "both accounts are listed for the prune sweep");
            Assert(QualityData.PlanetIdsWithRecyclers().Contains(planet),
                "the planet is enumerable for the save writer");

            QualityData.ClearRecyclerState(planet, entity);
            Assert(!QualityData.HasRecyclerState(planet, entity) && QualityData.RecyclerEntityIds(planet).Count == 1,
                "clearing one account keeps the other");

            int planets;
            int entries;
            int carried;
            QualityData.RecyclerStats(out planets, out entries, out carried);
            Assert(planets == 1 && entries == 1 && carried == 6,
                "stats report one planet / one account / six carried points");

            QualityData.ClearRecyclerPlanet(planet);
            Assert(QualityData.PlanetIdsWithRecyclers().Count == 0, "planet clear empties the table");
            QualityData.RecyclerStats(out planets, out entries, out carried);
            Assert(planets == 0 && entries == 0 && carried == 0, "stats are empty after the clear");
        }

        // ------------------------------------------------------------------ save node

        private static void CheckRecyclerSaveRoundTrip()
        {
            QualityData.ClearAllRecyclers();
            StringBuilder sb = new StringBuilder();
            Assert(QualityData.AppendRecyclersJson(sb, 7) == 0, "no state means no node");
            Assert(sb.Length == 0, "an empty planet writes nothing at all");

            QualityData.AddRecyclerPoints(7, 12, 3);
            QualityData.AddRecyclerPoints(7, 20, 7);
            sb = new StringBuilder("{\"buildings\":[]");
            int written = QualityData.AppendRecyclersJson(sb, 7);
            Assert(written == 2, "two recyclers are written");
            string text = sb.ToString();
            Assert(text.Contains(",\"recyclers\":["), "the node is comma-joined onto the planet object: " + text);
            Assert(text.Contains("{\"e\":12,\"p\":3}"), "entity 12 keeps 3 carried points: " + text);
            Assert(text.Contains("{\"e\":20,\"p\":7}"), "entity 20 keeps 7 carried points: " + text);
            Assert(sb.ToString().EndsWith("]"), "the node closes its array");
            Assert(!text.Contains("\"o\":"), "design A never writes the obsolete bottle backlog field");

            // Parse it back exactly like the save reader does (JsonLite builds the same object tree).
            string document = sb.ToString() + "}";
            var root = JsonLite.Parse(document) as Dictionary<string, object>;
            Assert(root != null, "the produced planet object must stay valid JSON: " + document);

            QualityData.ClearAllRecyclers();
            Assert(QualityData.ApplyRecyclersJson(7, root) == 2, "two accounts are restored");
            Assert(QualityData.GetRecyclerState(7, 12).Points == 3, "entity 12 keeps 3 carried points");
            Assert(QualityData.GetRecyclerState(7, 20).Points == 7, "entity 20 keeps 7 carried points");

            // Design-B saves carried an "o" bottle backlog; design A has no bottles on the belt, so the
            // field is parsed and ignored (the points are what matters).
            var legacy = JsonLite.Parse("{\"recyclers\":[{\"e\":31,\"p\":2,\"o\":5}]}")
                as Dictionary<string, object>;
            Assert(QualityData.ApplyRecyclersJson(9, legacy) == 1, "legacy entry is applied");
            Assert(QualityData.GetRecyclerState(9, 31).Points == 2,
                "the legacy point remainder survives, the bottle backlog is dropped");

            // A planet without the node must be cleared, so a load never mixes saves.
            QualityData.AddRecyclerPoints(8, 30, 5);
            var empty = JsonLite.Parse("{\"buildings\":[]}") as Dictionary<string, object>;
            Assert(QualityData.ApplyRecyclersJson(8, empty) == 0, "missing node applies nothing");
            Assert(!QualityData.HasRecyclerState(8, 30), "missing node clears the planet");

            // Garbage entries are skipped, not fatal.
            var broken = JsonLite.Parse("{\"recyclers\":[{\"e\":0,\"p\":4},{\"e\":-1,\"p\":4},\"nope\","
                + "{\"e\":32,\"p\":-3},{\"e\":33}]}") as Dictionary<string, object>;
            QualityData.ClearAllRecyclers();
            Assert(QualityData.ApplyRecyclersJson(10, broken) == 0, "garbage recycler entries are ignored");
            Assert(QualityData.PlanetIdsWithRecyclers().Count == 0, "garbage leaves no planet entry");

            Assert(QualityData.ApplyRecyclersJson(-1, null) == 0, "a null node is ignored");
            QualityData.ClearAllRecyclers();
        }
    }
}
