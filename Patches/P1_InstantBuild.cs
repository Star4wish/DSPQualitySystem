using System;
using HarmonyLib;
using UnityEngine;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.16.0 / v0.17.0 (Bug 2 "Shift+click instant build loses quality").
    ///
    /// Vanilla `PlayerAction_Inspect.GameTick` (asm_full.cs:132659, instant-construct block at
    /// 133137-133169) builds the hovered ghost prebuild while the player holds the instant-construct
    /// key:
    ///
    ///     PrebuildData prebuildData3 = factory.prebuildPool[num2];
    ///     int itemId = prebuildData3.protoId;
    ///     int count = prebuildData3.itemRequired;
    ///     player.package.TakeTailItems(ref itemId, ref count, out var _);  // incTable discarded!
    ///     factory.BuildFinally(player, num2);
    ///
    /// v0.16.0 only captured the discarded `out inc` value, which cannot help when vanilla consumes a
    /// 0-point stack from the backpack tail while the player's 4-point item sits elsewhere: the
    /// building then correctly inherits 0 points, or the record is empty and nothing binds at all.
    ///
    /// v0.17.0 fixes the cause, not the symptom:
    ///  - Prefix on `PlayerAction_Inspect.GameTick(long)` arms the shared `P4_ConsumePriority` consume
    ///    window for exactly the frame in which vanilla will perform the instant build (the mirrored
    ///    conditions are the same ones vanilla checks at asm_full.cs:133137-133139). Inside that
    ///    window `P4_ConsumePriority` consumes the highest quality matching grid instead of the
    ///    backpack tail, exactly like a normal hand build, and records a receipt for it.
    ///  - Prefix on `PlanetFactory.BuildFinally(Player, int, bool, bool)` binds that receipt to the
    ///    built prebuild (`TryTakeReceipt`, matched against `prebuild.protoId`); the existing
    ///    `AddEntityData_Bind` postfix then attaches the points to the new entity.
    ///
    /// The v0.16.0 `TakeTailItems` postfix record is kept as the fallback for the case where the
    /// receipt is unavailable (e.g. the P4 patch is not armed). Degradation is explicit: if the
    /// window cannot be opened the take falls back to vanilla tail order and only the consumed
    /// stack's own points are bound; if nothing matches, the building is built with 0 points exactly
    /// like vanilla. Any exception degrades to vanilla with a one-time warning.
    /// </summary>
    internal static class InstantBuildCapture
    {
        private const string Tag = "[QSystem][build] ";
        private const int DebugLogLimit = 20;

        /// <summary>Frame in which `PlayerAction_Inspect.GameTick` opened the instant window.</summary>
        private static int _windowFrame = -1;

        /// <summary>Frame in which the fallback `TakeTailItems` record was captured.</summary>
        private static int _recordFrame = -1;
        private static int _itemId;
        private static int _count;
        private static int _inc;

        private static long _windows;
        private static long _captures;
        private static long _bound;
        private static long _boundReceipt;
        private static long _boundRecord;
        private static long _skipped;
        private static int _debugLogs;

        /// <summary>
        /// Prefix of `PlayerAction_Inspect.GameTick(long)`: when the player is about to instant-build
        /// the hovered prebuild, arm the consume window for this frame. `hoveringPrebuildId` is
        /// assigned earlier in the same tick (asm_full.cs:132822/132827), so it is already current.
        /// </summary>
        internal static void OnInspectGameTick(PlayerAction_Inspect instance)
        {
            try
            {
                if (instance == null)
                {
                    return;
                }
                if (!VFInput.instantConstruct)
                {
                    return;
                }
                if (instance.hoveringPrebuildId <= 0)
                {
                    return;
                }
                if (instance.hoveringPrebuildTooFar || !instance.hasSupport
                    || !instance.hasEnoughItem)
                {
                    return;
                }
                _windowFrame = Time.frameCount;
                _windows++;
                ResetRecord();
                P4_ConsumePriority.OpenInstantWindow();
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("instant.window", e);
            }
        }

        /// <summary>
        /// Fallback capture of the raw `TakeTailItems(ref int, ref int, out int, bool)` call. Only
        /// records inside a frame in which the instant-build window was opened, so unrelated backpack
        /// takes can never leak into a build.
        /// </summary>
        internal static void OnTakeTailItems(int itemId, int count, int inc)
        {
            try
            {
                if (_windowFrame != Time.frameCount)
                {
                    return;
                }
                if (itemId > 0 && count > 0 && inc > 0)
                {
                    _itemId = itemId;
                    _count = count;
                    _inc = inc;
                    _recordFrame = Time.frameCount;
                    _captures++;
                }
                else
                {
                    ResetRecord();
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("instant.capture", e);
            }
        }

        /// <summary>
        /// Prefix of `PlanetFactory.BuildFinally(Player, int, bool, bool)`. Only acts when the current
        /// frame belongs to an instant build; normal builds and blueprint paste have their own binding
        /// paths and are untouched.
        /// </summary>
        internal static void TryBind(PlanetFactory factory, int prebuildId)
        {
            try
            {
                if (_windowFrame != Time.frameCount)
                {
                    return;
                }
                _windowFrame = -1;
                if (factory == null || prebuildId <= 0)
                {
                    ResetRecord();
                    _skipped++;
                    return;
                }
                PrebuildData prebuild = factory.prebuildPool[prebuildId];
                if (prebuild.id != prebuildId || prebuild.protoId <= 0)
                {
                    ResetRecord();
                    _skipped++;
                    return;
                }

                int receiptPerItem = 0;
                P4_ConsumePriority.TryTakeReceipt(prebuild.protoId, out receiptPerItem);

                int recordPerItem = 0;
                if (_recordFrame == Time.frameCount && _itemId > 0 && _count > 0
                    && _itemId == prebuild.protoId && _count == prebuild.itemRequired)
                {
                    recordPerItem = QualityMath.InstantBuildPerItem(_inc, _count);
                }

                int perItem = QualityMath.PreferBindingValue(receiptPerItem, recordPerItem);
                ResetRecord();
                if (perItem <= 0)
                {
                    _skipped++;
                    return;
                }

                QualityData.SetPrebuildInc(prebuildId, perItem);
                _bound++;
                if (receiptPerItem > 0)
                {
                    _boundReceipt++;
                }
                else
                {
                    _boundRecord++;
                }
                LogBind(prebuildId, prebuild.protoId, prebuild.itemRequired, perItem,
                    receiptPerItem > 0 ? "receipt" : "record");
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("instant.bind", e);
            }
        }

        internal static string Describe()
        {
            return "instant-build quality capture (Inspect.GameTick window -> highest quality grid / "
                + "BuildFinally receipt; windows=" + _windows + " captures=" + _captures
                + " bound=" + _bound + " (receipt=" + _boundReceipt
                + " record=" + _boundRecord + ") skipped=" + _skipped + ")";
        }

        internal static void ResetState()
        {
            ResetRecord();
            _windowFrame = -1;
            _windows = 0;
            _captures = 0;
            _bound = 0;
            _boundReceipt = 0;
            _boundRecord = 0;
            _skipped = 0;
            _debugLogs = 0;
        }

        private static void LogBind(int prebuildId, int itemId, int required, int perItem,
            string source)
        {
            try
            {
                bool debug = QualityConfig.DevMode;
                if (_bound <= 3 || _bound % EffectiveLogEvery == 0 || (debug && _debugLogs < DebugLogLimit))
                {
                    if (!(_bound <= 3) && !(_bound % EffectiveLogEvery == 0))
                    {
                        _debugLogs++;
                    }
                    Plugin.Log.LogInfo(Tag + "instant-build quality bound: prebuild=" + prebuildId
                        + " item=" + itemId + " required=" + required + " perItem=" + perItem
                        + " source=" + source + " bound=" + _bound);
                }
            }
            catch (Exception)
            {
            }
        }

        private const int EffectiveLogEvery = 200;

        private static void ResetRecord()
        {
            _itemId = 0;
            _count = 0;
            _inc = 0;
            _recordFrame = -1;
        }

        /// <summary>
        /// Manual registration (HarmonyX attributes cannot express `ref`/`out` parameter types):
        ///  - PlayerAction_Inspect.GameTick(long)                          prefix (window)
        ///  - StorageComponent.TakeTailItems(ref int, ref int, ref int, bool) postfix (fallback record)
        ///  - PlanetFactory.BuildFinally(Player, int, bool, bool)          prefix (bind)
        /// </summary>
        internal static void Apply(HarmonyLib.Harmony harmony)
        {
            try
            {
                if (harmony == null)
                {
                    return;
                }
                bool ok = true;

                var inspect = typeof(PlayerAction_Inspect).GetMethod("GameTick",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                    null, new[] { typeof(long) }, null);
                if (inspect == null)
                {
                    ok = false;
                    QualityEnv.WarnOnce("instant.apply.inspect",
                        "PlayerAction_Inspect.GameTick(long) not found");
                }
                else
                {
                    harmony.Patch(inspect,
                        prefix: new HarmonyLib.HarmonyMethod(typeof(InstantBuildCapture).GetMethod(
                            "InspectGameTickPrefix",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)));
                }

                var takeTail = typeof(StorageComponent).GetMethod("TakeTailItems",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                    null,
                    new[]
                    {
                        typeof(int).MakeByRefType(), typeof(int).MakeByRefType(),
                        typeof(int).MakeByRefType(), typeof(bool)
                    },
                    null);
                if (takeTail == null)
                {
                    ok = false;
                    QualityEnv.WarnOnce("instant.apply.take",
                        "StorageComponent.TakeTailItems(ref int,ref int,ref int,bool) not found");
                }
                else
                {
                    harmony.Patch(takeTail,
                        postfix: new HarmonyLib.HarmonyMethod(typeof(InstantBuildCapture).GetMethod(
                            "TakeTailItemsPostfix",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)));
                }

                var buildFinally = typeof(PlanetFactory).GetMethod("BuildFinally",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                    null,
                    new[] { typeof(Player), typeof(int), typeof(bool), typeof(bool) },
                    null);
                if (buildFinally == null)
                {
                    ok = false;
                    QualityEnv.WarnOnce("instant.apply.build",
                        "PlanetFactory.BuildFinally(Player,int,bool,bool) not found");
                }
                else
                {
                    harmony.Patch(buildFinally,
                        prefix: new HarmonyLib.HarmonyMethod(typeof(InstantBuildCapture).GetMethod(
                            "BuildFinallyPrefix",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)));
                }

                if (ok)
                {
                    Plugin.Log.LogInfo(Tag + "patch registered: instant-build consume window "
                        + "(GameTick) + BuildFinally binding (vanilla fallback on any failure)");
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("instant.apply", e);
            }
        }

        private static void InspectGameTickPrefix(PlayerAction_Inspect __instance)
        {
            OnInspectGameTick(__instance);
        }

        private static void TakeTailItemsPostfix(int itemId, int count, int inc)
        {
            OnTakeTailItems(itemId, count, inc);
        }

        private static void BuildFinallyPrefix(PlanetFactory __instance, int prebuildId)
        {
            TryBind(__instance, prebuildId);
        }
    }
}
