using System;
using System.Reflection;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.11.5: generic "prefer the highest quality building item" for the building-item consumption
    /// step. This deliberately does NOT touch blueprint settlement: a hand build or a blueprint paste
    /// consumes the best matching building item the player owns (hand first on ties, then the backpack
    /// grid with the highest exact points per item, lowest grid index on ties) and the new building
    /// inherits the points of the item that actually paid for it.
    ///
    /// The preference only applies inside two short windows: BuildTool_Click.CreatePrebuilds (hand
    /// build) and BuildTool_BlueprintPaste.CreatePrebuilds (blueprint paste). Every other hand,
    /// pickup, insert or storage operation keeps vanilla behaviour.
    /// </summary>
    internal static partial class P4_ConsumePriority
    {
        internal const int DebugLogLimit = 20;
        internal const int EffectiveLogEvery = 200;
        internal const int SourceHand = 0;
        internal const int SourceBackpack = 1;

        internal struct HandState
        {
            public int itemId;
            public int count;
        }

        private static bool _armed;
        private static bool _warnedInstall;
        private static string _reason;
        private static int _inPaste;
        private static int _inHandBuild;
        private static int _swapping;
        private static long _swapped;
        private static long _bestGridPicks;
        private static long _effective;
        private static long _captured;
        private static int _debugLogs;

        private static bool _valid;
        private static int _itemId;
        private static int _perItemHundredths;
        private static int _source;
        private static int _grid = -1;
        private static int _frame = int.MinValue;

        /// <summary>
        /// v0.17.0: one-frame instant-build window. `PlayerAction_Inspect.GameTick` consumes the
        /// building item itself when the player holds the instant-construct key, outside the hand
        /// build / blueprint paste windows. Our GameTick prefix opens this window for that frame so
        /// the same "highest quality grid first" preference runs there too; the frame number makes it
        /// expire automatically if vanilla bails out before consuming anything.
        /// </summary>
        private static int _instantFrame = int.MinValue;
        private static long _instantWindows;

        internal static bool Armed
        {
            get { return _armed; }
        }

        private static bool PreferEnabled
        {
            get
            {
                try
                {
                    return QualityConfig.Enabled != null && QualityConfig.Enabled.Value
                        && QualityConfig.PreferHighQualityConsume != null
                        && QualityConfig.PreferHighQualityConsume.Value;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        private static bool InConsumeWindow
        {
            get
            {
                return QualityMath.ConsumeWindowActive(_inPaste > 0, _inHandBuild > 0,
                    _instantFrame != int.MinValue && _instantFrame == SafeFrame());
            }
        }

        /// <summary>
        /// v0.17.0: arm the instant-build window for the current frame. Called from the
        /// `PlayerAction_Inspect.GameTick` prefix when the player is about to instant-build the
        /// hovered ghost prebuild (asm_full.cs:133137-133169). The window never outlives its frame.
        /// </summary>
        internal static void OpenInstantWindow()
        {
            try
            {
                _instantFrame = SafeFrame();
                _instantWindows++;
            }
            catch (Exception)
            {
            }
        }

        internal static bool InstantWindowOpen
        {
            get { return _instantFrame != int.MinValue && _instantFrame == SafeFrame(); }
        }

        internal static long InstantWindows
        {
            get { return _instantWindows; }
        }

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
            {
                return;
            }
            try
            {
                if (QualityConfig.PreferHighQualityConsume == null
                    || !QualityConfig.PreferHighQualityConsume.Value)
                {
                    Disable("preferHighQualityConsume disabled by config");
                    return;
                }
                MethodInfo hand = typeof(Player).GetMethod("UseHandItems",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { typeof(int), typeof(int).MakeByRefType() }, null);
                MethodInfo take = typeof(StorageComponent).GetMethod("TakeTailItems",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[]
                    {
                        typeof(int).MakeByRefType(), typeof(int).MakeByRefType(),
                        typeof(int).MakeByRefType(), typeof(bool)
                    }, null);
                MethodInfo handPrefix = typeof(P4_ConsumePriority).GetMethod("HandPrefix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo handPostfix = typeof(P4_ConsumePriority).GetMethod("HandPostfix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo takePrefix = typeof(P4_ConsumePriority).GetMethod("TakeBestGridPrefix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo click = typeof(BuildTool_Click).GetMethod("CreatePrebuilds",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                    Type.EmptyTypes, null);
                MethodInfo paste = typeof(BuildTool_BlueprintPaste).GetMethod("CreatePrebuilds",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                    Type.EmptyTypes, null);
                MethodInfo clickPre = typeof(P4_ConsumePriority).GetMethod("HandBuildPrefix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo clickPost = typeof(P4_ConsumePriority).GetMethod("HandBuildPostfix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo pastePre = typeof(P4_ConsumePriority).GetMethod("PastePrefix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo pastePost = typeof(P4_ConsumePriority).GetMethod("PastePostfix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (hand == null || take == null || handPrefix == null || handPostfix == null
                    || takePrefix == null || click == null || paste == null
                    || clickPre == null || clickPost == null || pastePre == null || pastePost == null)
                {
                    Disable("consumption patch targets not found");
                    return;
                }
                harmony.Patch(hand, prefix: new HarmonyMethod(handPrefix)
                {
                    priority = Priority.High
                }, postfix: new HarmonyMethod(handPostfix));
                harmony.Patch(take, prefix: new HarmonyMethod(takePrefix)
                {
                    priority = Priority.High
                });
                harmony.Patch(click, prefix: new HarmonyMethod(clickPre),
                    postfix: new HarmonyMethod(clickPost));
                harmony.Patch(paste, prefix: new HarmonyMethod(pastePre),
                    postfix: new HarmonyMethod(pastePost));
                _armed = true;
                P5_ConsumeLog.Info("[QSystem][consume] patch registered: highest quality building item "
                    + "is consumed first during hand build and blueprint paste (hand swap + best grid)");
                P5_ConsumeLog.Info("[QSystem][consume] armed: prefer high quality building items "
                    + "(preferHighQualityConsume=True)");
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
            if (!_warnedInstall)
            {
                _warnedInstall = true;
                P5_ConsumeLog.Warn("[QSystem][consume] install failed: " + reason);
            }
            P5_ConsumeLog.Info("[QSystem][consume] NOT ARMED (vanilla consumption order): " + reason);
        }

        private static void HandBuildPrefix()
        {
            _inHandBuild++;
            _valid = false;
        }

        private static void HandBuildPostfix()
        {
            if (_inHandBuild > 0)
            {
                _inHandBuild--;
            }
            _valid = false;
        }

        private static void PastePrefix()
        {
            _inPaste++;
            _valid = false;
        }

        private static void PastePostfix()
        {
            if (_inPaste > 0)
            {
                _inPaste--;
            }
            _valid = false;
        }

        internal static string Describe()
        {
            try
            {
                if (QualityConfig.PreferHighQualityConsume == null
                    || !QualityConfig.PreferHighQualityConsume.Value)
                {
                    return "consume priority NOT ARMED (vanilla consumption order, "
                        + "reason: preferHighQualityConsume disabled)";
                }
                if (!_armed)
                {
                    return "consume priority NOT ARMED (vanilla consumption order, reason: "
                        + (_reason ?? "not applied yet") + ")";
                }
                return "consume priority armed (hand build + blueprint paste + instant build consume "
                    + "the highest quality building item first; swapped=" + _swapped
                    + " bestGrid=" + _bestGridPicks + " captured=" + _captured
                    + " instantWindows=" + _instantWindows + ")";
            }
            catch (Exception)
            {
                return "consume priority state unavailable";
            }
        }
    }
}