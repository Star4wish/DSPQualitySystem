using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DSPQualitySystem
{
    /// <summary>
    /// v0.19.0 (task-2 B, design A): pure decision math for the quality recycler.
    /// <para>
    /// The recycler is a vanilla spray coater driven in reverse: scrap cargo is fed through the
    /// <b>spray-catalyst input port</b> (the same belt the vanilla body sucks 1141/1142/1143 from),
    /// the items carrying quality points are destroyed, and their points are converted into spray
    /// uses inside the tank: every <see cref="PointsPerSpray"/> points add one spray use to
    /// <c>incCount</c>. The coater keeps spraying the normal item belt below it, so the two roles
    /// never fight over the same belt and nothing is ever put back on a belt.
    /// </para>
    /// <para>
    /// Everything in this file is game-free on purpose: the offline test harness links it directly
    /// (see tests\OfflineTests\Checks.P7Recycler.cs) while Patches\P7_Recycler.cs only owns the
    /// belt/inventory plumbing.
    /// </para>
    /// </summary>
    public static class QualityRecyclerMath
    {
        /// <summary>Quality points needed for one spray use. 4 keeps the 1:4 ratio of the old
        /// "4 points = one Mk.III" rule: a sprayed item carries at most 10 points, so a fully
        /// painted item refunds two and a half sprays.</summary>
        public const int PointsPerSpray = 4;

        /// <summary>Minimum per-item points a cargo must carry before the recycler destroys it.
        /// 1 means "any cargo that actually carries points can be recycled": a single Mk.I point
        /// is enough. There is no tier-up arbitrage to guard against because the exchange is
        /// lossy in every direction (4 points buy 1 spray use, and one spray use is worth at most
        /// 3 points), so the threshold only exists to keep unsprayed cargo flowing untouched.
        /// Raise it (to 3 for "Mk.III painted items only") if a stricter gate is ever wanted.</summary>
        public const int DefaultMinPerItem = 1;

        /// <summary>The vanilla tank holds 600 spray uses; only used when the component reports an
        /// impossible capacity.</summary>
        public const int DefaultCapacity = 600;

        /// <summary>Points carried by one belt cargo (negative values are treated as no points).</summary>
        public static int PointsFromCargoInc(int cargoInc)
        {
            return cargoInc > 0 ? cargoInc : 0;
        }

        /// <summary>Split <paramref name="points"/> into whole spray uses plus the carried remainder.</summary>
        public static int SpraysFromPoints(int points, out int remainder)
        {
            if (points <= 0)
            {
                remainder = 0;
                return 0;
            }

            remainder = points % PointsPerSpray;
            return points / PointsPerSpray;
        }

        /// <summary>Add freshly harvested points to an account; both sides are treated as 0 when
        /// non-positive so corrupt values can never create negative points.</summary>
        public static int MergePoints(int accountPoints, int harvestedPoints)
        {
            int account = accountPoints > 0 ? accountPoints : 0;
            int fresh = harvestedPoints > 0 ? harvestedPoints : 0;
            int total = account + fresh;
            // Guard against int overflow on a corrupt save (16 bits of points are plenty).
            return total < 0 || total > 1 << 20 ? (1 << 20) : total;
        }

        /// <summary>True when a cargo of <paramref name="stack"/> items carrying
        /// <paramref name="totalInc"/> points (the vanilla cargo-wide total) should be destroyed.
        /// The threshold is per item, matching every other quality threshold in the mod.</summary>
        public static bool ShouldHarvest(int totalInc, int stack, int minPerItem)
        {
            if (totalInc <= 0 || stack <= 0)
            {
                return false;
            }

            int min = minPerItem < 0 ? 0 : minPerItem;
            return QualityMath.PerItemPoints(totalInc, stack) >= min;
        }

        /// <summary>Free spray uses in the tank. extaIncCount can go temporarily negative in the
        /// vanilla body, so it is clamped to 0 before it can inflate the room.</summary>
        public static int FreeRoom(int capacity, int incCount, int extraIncCount)
        {
            int cap = capacity > 0 ? capacity : DefaultCapacity;
            int used = (incCount > 0 ? incCount : 0) + (extraIncCount > 0 ? extraIncCount : 0);
            int room = cap - used;
            return room > 0 ? room : 0;
        }

        /// <summary>How many of <paramref name="points"/> can be spent right now: at most
        /// <paramref name="freeRoom"/> sprays, at least 0.</summary>
        public static int SpraysThatFit(int points, int freeRoom)
        {
            int remainder;
            int sprays = SpraysFromPoints(points, out remainder);
            int room = freeRoom > 0 ? freeRoom : 0;
            return sprays < room ? sprays : room;
        }
    }

    /// <summary>Per-recycler persisted bookkeeping: quality points harvested but not yet converted
    /// into a spray use (normally 0..3, larger only while the tank is full).</summary>
    public struct RecyclerState
    {
        public int Points;
    }

    public static partial class QualityData
    {
        private static readonly Dictionary<int, Dictionary<int, RecyclerState>> RecyclersByPlanet =
            new Dictionary<int, Dictionary<int, RecyclerState>>();

        private static Dictionary<int, RecyclerState> GetOrCreateRecyclerMap(int planetId)
        {
            Dictionary<int, RecyclerState> map;
            if (!RecyclersByPlanet.TryGetValue(planetId, out map) || map == null)
            {
                map = new Dictionary<int, RecyclerState>();
                RecyclersByPlanet[planetId] = map;
            }

            return map;
        }

        /// <summary>Credit <paramref name="points"/> harvested from one cargo. Returns the account
        /// total after the merge (the caller spends it on spray uses and writes back the rest).</summary>
        public static int AddRecyclerPoints(int planetId, int entityId, int points)
        {
            if (planetId < 0 || entityId <= 0 || points <= 0)
            {
                return 0;
            }

            Dictionary<int, RecyclerState> map = GetOrCreateRecyclerMap(planetId);
            RecyclerState state;
            map.TryGetValue(entityId, out state);
            state.Points = QualityRecyclerMath.MergePoints(state.Points, points);
            map[entityId] = state;
            return state.Points;
        }

        /// <summary>Spend <paramref name="take"/> carried points on a spray use. Returns how many
        /// were actually spent (never more than the account holds).</summary>
        public static int TakeRecyclerPoints(int planetId, int entityId, int take)
        {
            if (planetId < 0 || entityId <= 0 || take <= 0)
            {
                return 0;
            }

            Dictionary<int, RecyclerState> map;
            if (!RecyclersByPlanet.TryGetValue(planetId, out map) || map == null)
            {
                return 0;
            }

            RecyclerState state;
            if (!map.TryGetValue(entityId, out state))
            {
                return 0;
            }

            int spent = take < state.Points ? take : state.Points;
            if (spent <= 0)
            {
                return 0;
            }

            state.Points -= spent;
            if (state.Points <= 0)
            {
                map.Remove(entityId);
                if (map.Count == 0)
                {
                    RecyclersByPlanet.Remove(planetId);
                }
            }
            else
            {
                map[entityId] = state;
            }

            return spent;
        }

        /// <summary>Write the account back verbatim (used when points could not be spent yet).</summary>
        public static void SetRecyclerPoints(int planetId, int entityId, int points)
        {
            if (planetId < 0 || entityId <= 0)
            {
                return;
            }

            if (points <= 0)
            {
                ClearRecyclerState(planetId, entityId);
                return;
            }

            Dictionary<int, RecyclerState> map = GetOrCreateRecyclerMap(planetId);
            RecyclerState state;
            map.TryGetValue(entityId, out state);
            state.Points = points;
            map[entityId] = state;
        }

        public static RecyclerState GetRecyclerState(int planetId, int entityId)
        {
            Dictionary<int, RecyclerState> map;
            if (!RecyclersByPlanet.TryGetValue(planetId, out map) || map == null)
            {
                return default(RecyclerState);
            }

            RecyclerState state;
            return map.TryGetValue(entityId, out state) ? state : default(RecyclerState);
        }

        /// <summary>Forget everything about one recycler (demolished / no longer a recycler).</summary>
        public static void ClearRecyclerState(int planetId, int entityId)
        {
            Dictionary<int, RecyclerState> map;
            if (!RecyclersByPlanet.TryGetValue(planetId, out map) || map == null)
            {
                return;
            }

            map.Remove(entityId);
            if (map.Count == 0)
            {
                RecyclersByPlanet.Remove(planetId);
            }
        }

        /// <summary>Lazy entity-existence probe helper for the tick path.</summary>
        public static bool HasRecyclerState(int planetId, int entityId)
        {
            Dictionary<int, RecyclerState> map;
            return RecyclersByPlanet.TryGetValue(planetId, out map)
                && map != null
                && map.ContainsKey(entityId);
        }

        /// <summary>Snapshot of the entity ids that still carry state on one planet.</summary>
        public static List<int> RecyclerEntityIds(int planetId)
        {
            Dictionary<int, RecyclerState> map;
            if (!RecyclersByPlanet.TryGetValue(planetId, out map) || map == null)
            {
                return new List<int>();
            }

            return new List<int>(map.Keys);
        }

        /// <summary>Planet ids that carry recycler state. Used by the save writer when it has to
        /// enumerate planets that own no PlanetQuality entry yet (recycler on a fresh planet).</summary>
        public static List<int> PlanetIdsWithRecyclers()
        {
            return new List<int>(RecyclersByPlanet.Keys);
        }

        public static void ClearRecyclerPlanet(int planetId)
        {
            RecyclersByPlanet.Remove(planetId);
        }

        public static void ClearAllRecyclers()
        {
            RecyclersByPlanet.Clear();
        }

        /// <summary>Totals for the startup / describe logs.</summary>
        public static void RecyclerStats(out int planetCount, out int entryCount, out int carriedPoints)
        {
            planetCount = RecyclersByPlanet.Count;
            entryCount = 0;
            carriedPoints = 0;
            foreach (KeyValuePair<int, Dictionary<int, RecyclerState>> planet in RecyclersByPlanet)
            {
                if (planet.Value == null)
                {
                    continue;
                }

                foreach (KeyValuePair<int, RecyclerState> entry in planet.Value)
                {
                    entryCount++;
                    if (entry.Value.Points > 0)
                    {
                        carriedPoints += entry.Value.Points;
                    }
                }
            }
        }

        /// <summary>
        /// Append the recycler node of one planet, or nothing when the planet has no state. The
        /// caller has just written the previous node of the planet object, so the text starts with a
        /// comma: <c>,"recyclers":[{"e":12,"p":3}]</c>. Only the carried points (<c>p</c>) exist in
        /// design A; the obsolete <c>o</c> field of older saves is never written again.
        /// </summary>
        public static int AppendRecyclersJson(StringBuilder sb, int planetId)
        {
            Dictionary<int, RecyclerState> map;
            if (sb == null || !RecyclersByPlanet.TryGetValue(planetId, out map) || map == null || map.Count == 0)
            {
                return 0;
            }

            var ids = new List<int>(map.Keys);
            ids.Sort();
            int written = 0;
            sb.Append(",\"recyclers\":[");
            for (int i = 0; i < ids.Count; i++)
            {
                RecyclerState state;
                if (!map.TryGetValue(ids[i], out state))
                {
                    continue;
                }

                if (state.Points <= 0)
                {
                    continue;
                }

                if (written > 0)
                {
                    sb.Append(',');
                }

                sb.Append("{\"e\":").Append(ids[i].ToString(CultureInfo.InvariantCulture))
                    .Append(",\"p\":").Append(state.Points.ToString(CultureInfo.InvariantCulture)).Append('}');
                written++;
            }

            sb.Append(']');
            if (written == 0)
            {
                // Nothing usable: undo the opening so the document stays valid.
                int cut = sb.Length - ",\"recyclers\":[]".Length;
                if (cut >= 0)
                {
                    sb.Length = cut;
                }
            }

            return written;
        }

        /// <summary>
        /// Restore the recycler node of one planet from an already parsed save document. Always
        /// clears the planet first so a loaded save never mixes with leftovers from the previous one.
        /// The obsolete <c>o</c> (queued bottles) field of older saves is parsed and ignored.
        /// </summary>
        public static int ApplyRecyclersJson(int planetId, Dictionary<string, object> planetNode)
        {
            if (planetId < 0)
            {
                return 0;
            }

            ClearRecyclerPlanet(planetId);
            if (planetNode == null)
            {
                return 0;
            }

            object node;
            if (!planetNode.TryGetValue("recyclers", out node) || node == null)
            {
                return 0;
            }

            var list = node as List<object>;
            if (list == null)
            {
                return 0;
            }

            Dictionary<int, RecyclerState> map = GetOrCreateRecyclerMap(planetId);
            int applied = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var entry = list[i] as Dictionary<string, object>;
                if (entry == null)
                {
                    continue;
                }

                int entityId = ReadRecyclerInt(entry, "e");
                int points = ReadRecyclerInt(entry, "p");
                if (entityId <= 0 || points <= 0)
                {
                    continue;
                }

                map[entityId] = new RecyclerState { Points = points };
                applied++;
            }

            if (map.Count == 0)
            {
                RecyclersByPlanet.Remove(planetId);
            }

            return applied;
        }

        private static int ReadRecyclerInt(Dictionary<string, object> entry, string key)
        {
            object value;
            if (!entry.TryGetValue(key, out value) || value == null)
            {
                return 0;
            }

            if (value is int)
            {
                return (int)value;
            }

            if (value is long)
            {
                return (int)(long)value;
            }

            if (value is double)
            {
                return (int)(double)value;
            }

            string text = value as string;
            int parsed;
            if (text != null && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }

            return 0;
        }
    }
}
