using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.18.0 (issue 4): a white ghost pre-build gets the quality of the materials that are actually
    /// paid for it, even when those materials did not exist at placement time.
    ///
    /// Why the old code lost the points: `BuildTool_Click.CreatePrebuilds` may place a semi transparent
    /// pre-build without consuming anything (materials missing), so no consumption receipt exists at
    /// that moment and `QualityData.prebuildInc` stays empty. Later the construction module pays for it
    /// in `ConstructionModuleComponent.PlaceItems`, which calls
    /// `StorageComponent.TakeTailItems(ref itemId, ref count, ref inc, false)` and throws the `inc`
    /// away, so the finished entity used to be bound at 0 points.
    ///
    /// The fix observes that payment instead of hijacking it:
    ///   1. `PlaceItems` prefix/postfix opens a short "deferred payment" window,
    ///   2. `StorageComponent.TakeTailItems` postfix records each payment's
    ///      (itemId, count, inc) while the window is open,
    ///   3. `PlanetFactory.NotifyPrebuildChange(prebuildId, 3)` is the vanilla step that follows every
    ///      payment, so the prefix drains the recorded payments into a per-prebuild accumulator and,
    ///      as soon as `prebuildPool[prebuildId].itemRequired` reaches 0, binds
    ///      floor(points / items) as the pre-build's points.
    /// The existing `P1_BuildCapture.AddEntityData_Bind` postfix then moves those points onto the real
    /// entity when vanilla finally promotes the ghost, so the whole chain stays in the v0.17.0 shape.
    ///
    /// The window is deliberately read-only for the consume-priority layer (see
    /// <see cref="PayWindowActive"/>): vanilla asserts that a single TakeTailItems call pays the full
    /// remaining stack, so nothing may swap in a different grid while PlaceItems is running.
    /// </summary>
    internal static class P1_DeferredPay
    {
        private sealed class Receipt
        {
            public int ItemId;
            public int Count;
            public int Inc;
        }

        private static bool _armed;
        private static string _reason;
        private static bool _inPlaceItems;

        /// <summary>Payments recorded since the previous NotifyPrebuildChange(…, 3).</summary>
        private static readonly List<Receipt> Pending = new List<Receipt>();

        /// <summary>Accumulated points / items per prebuild (a base may pay a ghost over several frames).</summary>
        private static readonly Dictionary<int, int> Points = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> Items = new Dictionary<int, int>();

        private static long _bound;
        private static long _payments;
        private static int _logged;

        /// <summary>True while vanilla PlaceItems is paying ghosts; the consume layer must not interfere.</summary>
        internal static bool PayWindowActive
        {
            get { return _armed && _inPlaceItems; }
        }

        internal static bool Armed
        {
            get { return _armed; }
        }

        internal static void Apply(Harmony harmony)
        {
            try
            {
                if (QualityConfig.Enabled == null || !QualityConfig.Enabled.Value)
                {
                    Disable("quality system disabled");
                    return;
                }

                MethodInfo placeItems = typeof(ConstructionModuleComponent).GetMethod("PlaceItems",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { typeof(PlanetFactory), typeof(Player) }, null);
                MethodInfo takeTail = typeof(StorageComponent).GetMethod("TakeTailItems",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { typeof(int).MakeByRefType(), typeof(int).MakeByRefType(),
                        typeof(int).MakeByRefType(), typeof(bool) }, null);
                MethodInfo notify = typeof(PlanetFactory).GetMethod("NotifyPrebuildChange",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { typeof(int), typeof(int) }, null);
                MethodInfo remove = typeof(PlanetFactory).GetMethod("RemovePrebuildWithComponents",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { typeof(int) }, null);

                string missing = placeItems == null ? "ConstructionModuleComponent.PlaceItems"
                    : takeTail == null ? "StorageComponent.TakeTailItems(ref,ref,ref,bool)"
                    : notify == null ? "PlanetFactory.NotifyPrebuildChange(int,int)"
                    : remove == null ? "PlanetFactory.RemovePrebuildWithComponents(int)"
                    : null;
                if (missing != null)
                {
                    Disable(missing + " not found");
                    return;
                }

                harmony.Patch(placeItems,
                    prefix: new HarmonyMethod(typeof(P1_DeferredPay).GetMethod("PlaceItems_Prefix",
                        BindingFlags.NonPublic | BindingFlags.Static)),
                    postfix: new HarmonyMethod(typeof(P1_DeferredPay).GetMethod("PlaceItems_Postfix",
                        BindingFlags.NonPublic | BindingFlags.Static)));
                harmony.Patch(takeTail,
                    postfix: new HarmonyMethod(typeof(P1_DeferredPay).GetMethod("TakeTailItems_Postfix",
                        BindingFlags.NonPublic | BindingFlags.Static)));
                harmony.Patch(notify,
                    prefix: new HarmonyMethod(typeof(P1_DeferredPay).GetMethod("NotifyPrebuildChange_Prefix",
                        BindingFlags.NonPublic | BindingFlags.Static)));
                harmony.Patch(remove,
                    postfix: new HarmonyMethod(typeof(P1_DeferredPay).GetMethod("RemovePrebuild_Postfix",
                        BindingFlags.NonPublic | BindingFlags.Static)));

                _armed = true;
                P5Common.Info("[QSystem][deferred] armed: white ghost pre-builds inherit the points of the"
                    + " materials paid by ConstructionModuleComponent.PlaceItems");
            }
            catch (Exception e)
            {
                Disable("install failed: " + e.Message);
                P5Common.WarnOnce("deferred.install", e.ToString());
            }
        }

        private static void PlaceItems_Prefix()
        {
            if (!_armed)
            {
                return;
            }
            _inPlaceItems = true;
            Pending.Clear();
        }

        private static void PlaceItems_Postfix()
        {
            if (!_armed)
            {
                return;
            }
            _inPlaceItems = false;
            Pending.Clear();
        }

        /// <summary>
        /// v0.18.0: `TakeTailItems` returns the total points of the consumed items in its `inc` out
        /// parameter. We only read the values vanilla just wrote, never change them.
        /// </summary>
        private static void TakeTailItems_Postfix(int itemId, ref int count, ref int inc)
        {
            // itemId = item actually taken, count = items taken, inc = the total points vanilla put in
            // that out parameter and then threw away. We only observe, never modify.
            if (!_armed || !_inPlaceItems)
            {
                return;
            }
            try
            {
                if (count <= 0)
                {
                    return;
                }
                Pending.Add(new Receipt { ItemId = itemId, Count = count, Inc = inc });
                _payments++;
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("deferred.payment", e.ToString());
            }
        }

        /// <summary>
        /// v0.18.0: change == 3 is exactly the "PlaceItems paid a pre-build" notification (1 = placed,
        /// 2 = upgraded, 4 = reconstructed, -1 = removed). Draining here keeps every payment attached to
        /// the prebuild it was paid for, even when one PlaceItems call pays several ghosts in a row.
        /// </summary>
        private static void NotifyPrebuildChange_Prefix(PlanetFactory __instance, int prebuildId, int change)
        {
            if (!_armed || !_inPlaceItems || change != 3 || prebuildId <= 0)
            {
                return;
            }
            try
            {
                int points = 0;
                int items = 0;
                for (int i = 0; i < Pending.Count; i++)
                {
                    points += Pending[i].Inc;
                    items += Pending[i].Count;
                }
                Pending.Clear();
                if (items <= 0)
                {
                    return;
                }
                Accumulate(prebuildId, points, items);

                if (!IsFullyPaid(__instance, prebuildId))
                {
                    // A defence base may pay a ghost over several frames; keep the accumulator and bind
                    // when the last payment lands (the next PlaceItems call for the same prebuild).
                    return;
                }

                Bind(__instance, prebuildId);
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("deferred.notify", e.ToString());
            }
        }

        private static void Accumulate(int prebuildId, int points, int items)
        {
            if (points <= 0)
            {
                return;
            }
            int oldPoints;
            Points.TryGetValue(prebuildId, out oldPoints);
            Points[prebuildId] = oldPoints + points;
            int oldItems;
            Items.TryGetValue(prebuildId, out oldItems);
            Items[prebuildId] = oldItems + items;
        }

        private static bool IsFullyPaid(PlanetFactory factory, int prebuildId)
        {
            PrebuildData[] pool = factory.prebuildPool;
            if (pool == null || prebuildId <= 0 || prebuildId >= pool.Length)
            {
                return false;
            }
            PrebuildData prebuild = pool[prebuildId];
            return prebuild.id == prebuildId && prebuild.itemRequired == 0;
        }

        private static void Bind(PlanetFactory factory, int prebuildId)
        {
            int points;
            int items;
            Points.TryGetValue(prebuildId, out points);
            Items.TryGetValue(prebuildId, out items);
            Points.Remove(prebuildId);
            Items.Remove(prebuildId);

            int perItem = QualityMath.DeferredPayPerItem(points, items);
            if (perItem <= 0)
            {
                return;
            }
            QualityData.SetPrebuildInc(prebuildId, perItem);
            P5Common.Debug(ref _logged, "[QSystem][deferred] prebuild=" + prebuildId + " paid points="
                + points + " items=" + items + " perItem=" + perItem);
            P5Common.Effective(ref _bound, "deferred",
                " (payments=" + _payments + " bound=" + _bound + ")");
        }

        private static void RemovePrebuild_Postfix(int __0)
        {
            if (!_armed || __0 <= 0)
            {
                return;
            }
            try
            {
                Points.Remove(__0);
                Items.Remove(__0);
            }
            catch (Exception)
            {
            }
        }

        internal static void ResetState()
        {
            try
            {
                _inPlaceItems = false;
                Pending.Clear();
                Points.Clear();
                Items.Clear();
                _bound = 0;
                _payments = 0;
                _logged = 0;
            }
            catch (Exception)
            {
            }
        }

        private static void Disable(string reason)
        {
            _armed = false;
            _reason = reason;
            P5Common.Info("[QSystem][deferred] NOT ARMED (ghost pre-builds stay at 0 points): " + reason);
        }

        internal static string Describe()
        {
            return "deferredPay armed=" + _armed + " reason=" + (_reason ?? "-")
                + " payments=" + _payments + " bound=" + _bound;
        }
    }
}
