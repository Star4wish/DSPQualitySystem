using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// task-2 C: every spray item (improved spray / 增产剂 Mk.I/II/III = items 1141/1142/1143)
    /// sprays exactly ONE item instead of the vanilla 12 / 24 / 60.
    ///
    /// Where the number lives (verified against Assembly-CSharp.dll of game 0.10.35.29104 with
    /// il.exe / api.exe / fields.exe):
    ///  - <c>ItemProto.HpMax</c> (ItemProto.cs:39) is the "spray times" of a catalyst item. It is
    ///    part of <c>ProtoSignature_0_10_30_3100.CalculateSignature(ItemProtoSet)</c> (:26) and the
    ///    item info panel prints it directly (<c>ItemProto.cs:1812</c> "N 喷涂次数的后缀").
    ///  - <c>SpraycoaterComponent.incCount</c> is the tank content in *spray times*, while
    ///    <c>incSprayTimes</c> caches "spray times per item" of the last inserted item and
    ///    <c>incCapacity</c> (from <c>PrefabDesc.incCapacity</c>, 600 for the vanilla spray coater)
    ///    is the tank capacity in the same unit.
    ///  - Every insert path reads <c>ItemProto.HpMax</c> live and adds <c>itemCount * HpMax</c>:
    ///    <c>PlanetFactory.cs:12272-12286</c> / <c>:12330-12339</c>, plus the belt insert inside
    ///    <c>SpraycoaterComponent.InternalUpdate</c> (IL 0x014D <c>incSprayTimes = itemProto.HpMax</c>,
    ///    0x01E5 <c>incCount += incSprayTimes</c>). Taking spray items back out divides by it:
    ///    <c>CargoTraffic.cs:3716</c> <c>int num = spraycoaterComponent.incCount / itemProto.HpMax;</c>.
    ///    So a single field edit makes "1 item = 1 spray" everywhere (insert, take-out, tooltip, UI),
    ///    while the sprayer's 600-point tank capacity keeps its vanilla meaning.
    ///
    /// Why the hook is <c>ItemProto.Preload(int)</c>: it runs once per prototype right after the
    /// prototype table is deserialized from the game asset, i.e. before any save load, before the UI
    /// reads the tooltip and before <c>SpraycoaterComponent.Import</c>, and it needs no Plugin.cs
    /// wiring. LDB itself is a lazy static (<c>LDB.LoadTable&lt;T&gt;</c> just does
    /// <c>Resources.Load</c>), so there is no other stable load-time callback.
    ///
    /// Two compatibility guards:
    ///  - <b>Old saves.</b> A save written with the vanilla numbers stores <c>incCount</c> in vanilla
    ///    spray times (e.g. 3000 for 50 Mk.III). A postfix on <c>SpraycoaterComponent.Import</c>
    ///    converts it to the new unit (ceil(incCount / vanilla HpMax) items, same tank value within
    ///    one item) so a loaded coater neither gains uses nor dumps an explosive amount of spray
    ///    items when a sorter empties it.
    ///  - <b>Proto signature.</b> <c>ABN_ProtoData.CheckProto</c> re-hashes the item table on save /
    ///    game begin and raises the in-game "data abnormal" tip when HpMax was touched. The prefix
    ///    temporarily restores the vanilla numbers for the duration of that check (and the postfix
    ///    re-applies 1), so the hash still matches and the player never sees the warning.
    ///
    /// Everything is fail-open: any reflection miss or exception falls back to the vanilla 12/24/60
    /// numbers and only logs a warning through <see cref="P5Common.WarnOnce"/>.
    /// </summary>
    internal static class P6_SprayCount
    {
        internal const int SprayItemMk1 = 1141;
        internal const int SprayItemMk2 = 1142;
        internal const int SprayItemMk3 = 1143;

        /// <summary>Spray times granted by one spray item after this patch.</summary>
        internal const int TargetSprayTimes = 1;

        private static readonly int[] SprayItemIds = { SprayItemMk1, SprayItemMk2, SprayItemMk3 };

        /// <summary>Vanilla "spray times per item" captured before the first write (fallback below).</summary>
        private static readonly Dictionary<int, int> VanillaSprayTimes = new Dictionary<int, int>(3);
        private static readonly Dictionary<int, int> VanillaFallback = new Dictionary<int, int>(3)
        {
            { SprayItemMk1, 12 },
            { SprayItemMk2, 24 },
            { SprayItemMk3, 60 }
        };

        /// <summary>Live prototype instances seen in Preload (used to flip HpMax around the hash check).</summary>
        private static readonly Dictionary<int, ItemProto> SeenProtos = new Dictionary<int, ItemProto>(3);

        private static bool _armed;
        private static string _reason;
        private static bool _preloadHooked;
        private static bool _importHooked;
        private static bool _guardHooked;
        private static int _protosSeen;
        private static long _legacyMigrations;
        private static long _guardRuns;
        private static int _logged;

        internal static bool Armed
        {
            get { return _armed; }
        }

        internal static bool PreloadHooked
        {
            get { return _preloadHooked; }
        }

        /// <summary>Item ids of the three spray items (1141/1142/1143).</summary>
        internal static int[] SprayItems
        {
            get { return (int[])SprayItemIds.Clone(); }
        }

        internal static bool IsSprayItem(int itemId)
        {
            return itemId == SprayItemMk1 || itemId == SprayItemMk2 || itemId == SprayItemMk3;
        }

        internal static void Apply(Harmony harmony)
        {
            try
            {
                if (harmony == null)
                {
                    Disable("harmony instance missing");
                    return;
                }
                if (!P5Common.Enabled)
                {
                    Disable("quality system disabled (enabled=false)");
                    return;
                }

                MethodInfo preload = typeof(ItemProto).GetMethod("Preload",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { typeof(int) }, null);
                if (preload == null)
                {
                    Disable("reflection miss: ItemProto.Preload(int)");
                    return;
                }

                MethodInfo preloadPostfix = typeof(P6_SprayCount).GetMethod("PreloadPostfix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (preloadPostfix == null)
                {
                    Disable("reflection miss: P6_SprayCount.PreloadPostfix");
                    return;
                }
                harmony.Patch(preload, postfix: new HarmonyMethod(preloadPostfix));
                _preloadHooked = true;

                MethodInfo import = typeof(SpraycoaterComponent).GetMethod("Import",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { typeof(BinaryReader) }, null);
                // SpraycoaterComponent is a struct in the vanilla pool, but do not hard-code that:
                // Harmony needs `ref __instance` for a struct and a plain `__instance` for a class.
                bool isStruct = typeof(SpraycoaterComponent).IsValueType;
                MethodInfo importPostfix = typeof(P6_SprayCount).GetMethod(
                    isStruct ? "ImportPostfixValue" : "ImportPostfixRef",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (import != null && importPostfix != null)
                {
                    harmony.Patch(import, postfix: new HarmonyMethod(importPostfix));
                    _importHooked = true;
                }
                else
                {
                    P5Common.WarnOnce("spraycount.import",
                        "reflection miss: SpraycoaterComponent.Import(BinaryReader); old saves keep their "
                        + "vanilla spray uses until the coater is emptied");
                }

                // Vanilla ABN_ProtoData.CheckProto triggers the in-game "abnormal data" tip when the
                // item table hash differs. Restore the vanilla HpMax values just for that check.
                Type abn = AccessTools.TypeByName("ABN_ProtoData");
                MethodInfo check = abn == null
                    ? null
                    : abn.GetMethod("CheckProto", BindingFlags.NonPublic | BindingFlags.Instance, null,
                        Type.EmptyTypes, null);
                MethodInfo guardPrefix = typeof(P6_SprayCount).GetMethod("CheckProtoPrefix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo guardPostfix = typeof(P6_SprayCount).GetMethod("CheckProtoPostfix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (check != null && guardPrefix != null && guardPostfix != null)
                {
                    harmony.Patch(check, prefix: new HarmonyMethod(guardPrefix),
                        postfix: new HarmonyMethod(guardPostfix));
                    _guardHooked = true;
                }
                else
                {
                    P5Common.WarnOnce("spraycount.signature",
                        "ABN_ProtoData.CheckProto not found; the game may show its 'abnormal data' tip "
                        + "once the item table hash differs");
                }

                // Late Apply (config reload): the table may already be loaded, patch it right now.
                int already = ApplyLoadedProtos();
                _armed = true;

                P5Common.Info("[QSystem][spraycount] armed: Mk.I/II/III = " + TargetSprayTimes
                    + " spray per item (preload hook=" + _preloadHooked + ", legacy import hook="
                    + _importHooked + ", signature guard=" + _guardHooked + ", patched now=" + already + ")");
            }
            catch (Exception e)
            {
                Disable(e.ToString());
            }
        }

        private static void Disable(string reason)
        {
            _reason = reason;
            _armed = false;
            P5Common.WarnOnce("spraycount.install", reason);
            P5Common.Info("[QSystem][spraycount] NOT ARMED (vanilla 12/24/60 spray times): " + reason);
        }

        /// <summary>
        /// Runs for every item prototype right after the prototype table was deserialized; the vanilla
        /// HpMax is still readable here, which is where the original value is captured.
        /// </summary>
        private static void PreloadPostfix(ItemProto __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }
                int itemId = __instance.ID;
                if (!IsSprayItem(itemId))
                {
                    return;
                }
                int current = __instance.HpMax;
                if (!VanillaSprayTimes.ContainsKey(itemId) && current > TargetSprayTimes)
                {
                    VanillaSprayTimes[itemId] = current;
                }
                SeenProtos[itemId] = __instance;
                __instance.HpMax = TargetSprayTimes;
                _protosSeen++;
                P5Common.Debug(ref _logged, "[QSystem][spraycount] item " + itemId + " spray times "
                    + current + " -> " + TargetSprayTimes);
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("spraycount.preload", e.ToString());
            }
        }

        /// <summary>
        /// Old saves store <c>incCount</c> in vanilla spray times. Convert to "one unit = one item"
        /// so the loaded coater keeps the same value in items (ceil, so a partially used item does not
        /// vanish) instead of dumping vanilla-times as many spray items out of the tank.
        /// </summary>
        private static void ImportPostfixValue(ref SpraycoaterComponent __instance)
        {
            MigrateLoadedTank(ref __instance);
        }

        private static void ImportPostfixRef(SpraycoaterComponent __instance)
        {
            MigrateLoadedTank(ref __instance);
        }

        /// <summary>Old saves store <c>incCount</c> in vanilla spray times; convert to items.</summary>
        private static void MigrateLoadedTank(ref SpraycoaterComponent instance)
        {
            try
            {
                int itemId = instance.incItemId;
                if (!IsSprayItem(itemId))
                {
                    return;
                }
                int sprayTimes = instance.incSprayTimes;
                if (sprayTimes <= TargetSprayTimes)
                {
                    // Already written by the patched game (1 spray per item); nothing to convert.
                    return;
                }
                int legacyUses = VanillaOf(itemId);
                if (legacyUses <= TargetSprayTimes)
                {
                    return;
                }
                int oldCount = instance.incCount;
                // Same ceiling as RecyclerMath.LegacyUsesToItems: a partially used catalyst must not
                // vanish, and the tank may never hold more items than its capacity.
                int items = oldCount <= 0 ? 0 : (oldCount + legacyUses - 1) / legacyUses;
                int capacity = instance.incCapacity;
                if (capacity > 0 && items > capacity)
                {
                    items = capacity;
                }
                int oldExtra = instance.extraIncCount;
                instance.incCount = items < 0 ? 0 : items;
                instance.incSprayTimes = TargetSprayTimes;
                instance.extraIncCount = oldExtra <= 0 ? 0 : oldExtra / legacyUses;
                _legacyMigrations++;
                P5Common.Info("[QSystem][spraycount] legacy coater (entity " + instance.entityId
                    + ", item " + itemId + "): " + oldCount + " vanilla spray times ("
                    + legacyUses + "/item) -> " + instance.incCount + " items");
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("spraycount.import", e.ToString());
            }
        }

        private static int VanillaOf(int itemId)
        {
            int value;
            if (VanillaSprayTimes.TryGetValue(itemId, out value) && value > 0)
            {
                return value;
            }
            if (VanillaFallback.TryGetValue(itemId, out value) && value > 0)
            {
                return value;
            }
            return TargetSprayTimes;
        }

        /// <summary>Temporarily put the vanilla spray times back so the vanilla proto hash still matches.</summary>
        private static void CheckProtoPrefix()
        {
            try
            {
                _guardRuns++;
                foreach (int itemId in SprayItemIds)
                {
                    ItemProto proto = ProtoOf(itemId);
                    if (proto != null)
                    {
                        proto.HpMax = VanillaOf(itemId);
                    }
                }
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("spraycount.guard", e.ToString());
            }
        }

        private static void CheckProtoPostfix()
        {
            try
            {
                ApplyItemTargets();
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("spraycount.guard.restore", e.ToString());
            }
        }

        private static ItemProto ProtoOf(int itemId)
        {
            ItemProto proto;
            if (SeenProtos.TryGetValue(itemId, out proto) && proto != null)
            {
                return proto;
            }
            try
            {
                if (LDB.items != null)
                {
                    proto = LDB.items.Select(itemId);
                    if (proto != null)
                    {
                        SeenProtos[itemId] = proto;
                    }
                    return proto;
                }
            }
            catch (Exception)
            {
                // prototype table not loaded yet
            }
            return null;
        }

        /// <summary>Capture the vanilla values (when the table is already loaded) and force HpMax = 1.</summary>
        private static int ApplyLoadedProtos()
        {
            int changed = 0;
            if (LDB.items == null || LDB.items.dataArray == null)
            {
                return 0;
            }
            ItemProto[] data = LDB.items.dataArray;
            for (int i = 0; i < data.Length; i++)
            {
                ItemProto proto = data[i];
                if (proto == null)
                {
                    continue;
                }
                int itemId = proto.ID;
                if (!IsSprayItem(itemId))
                {
                    continue;
                }
                SeenProtos[itemId] = proto;
                if (!VanillaSprayTimes.ContainsKey(itemId) && proto.HpMax > TargetSprayTimes)
                {
                    VanillaSprayTimes[itemId] = proto.HpMax;
                }
                if (proto.HpMax != TargetSprayTimes)
                {
                    proto.HpMax = TargetSprayTimes;
                    changed++;
                }
            }
            return changed;
        }

        private static void ApplyItemTargets()
        {
            foreach (int itemId in SprayItemIds)
            {
                ItemProto proto = ProtoOf(itemId);
                if (proto != null && proto.HpMax != TargetSprayTimes)
                {
                    proto.HpMax = TargetSprayTimes;
                }
            }
        }

        internal static string Describe()
        {
            try
            {
                if (!P5Common.Enabled)
                {
                    return "spray count NOT ARMED (vanilla 12/24/60, reason: quality system disabled)";
                }
                if (!_armed)
                {
                    return "spray count NOT ARMED (vanilla 12/24/60, reason: "
                        + (_reason ?? "not applied yet") + ")";
                }
                return "spray count armed (Mk.I/II/III = " + TargetSprayTimes
                    + " spray per item, protos patched=" + _protosSeen
                    + ", legacy coater migrations=" + _legacyMigrations
                    + ", proto signature guard=" + (_guardHooked ? "on/" + _guardRuns + " checks" : "off")
                    + ", import hook=" + (_importHooked ? "on" : "off") + ")";
            }
            catch (Exception)
            {
                return "spray count state unavailable";
            }
        }
    }
}
