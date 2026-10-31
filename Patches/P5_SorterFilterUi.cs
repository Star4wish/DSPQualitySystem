using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.16.1 (C-3 UI): quality threshold display and shortcuts inside the vanilla sorter window.
    ///
    /// WHY THIS SHAPE
    ///   The vanilla sorter filter UI is a single filter button plus a three-lamp Mk indicator
    ///   (UIInserterWindow.filterButton / filterIncs). The item filter itself is persisted by the
    ///   game (InserterComponent.filter), so only the *minimum quality points* need a MOD side table
    ///   and a place to edit it. Rather than adding another prefab-dependent control, the existing
    ///   filter button learns one extra gesture:
    ///     left click          = vanilla item picker (untouched)
    ///     Shift + right click = cycle the minimum points 0 -> 1 -> ... -> 10 -> 0
    ///     Ctrl + Shift + right click = clear the threshold (vanilla behaviour)
    ///   and the vanilla filter caption is rewritten to "磁铁  品质≥5" while a threshold is set, so
    ///   the state is visible without opening any extra panel.
    /// </summary>
    internal static class P5_SorterFilterUi
    {
        internal const string Tag = "[QSystem][sorterfilter] ";

        private static bool _armed;
        private static bool _warned;
        private static string _reason = "not applied";
        private static int _clicks;
        private static int _texts;

        internal static bool Armed { get { return _armed; } }

        internal static string Describe()
        {
            if (!_armed)
            {
                return "NOT ARMED (ui " + _reason + ")";
            }

            return "armed (ui clicks=" + _clicks + ", captions=" + _texts + ")";
        }

        internal static void ResetState()
        {
            _warned = false;
        }

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
            {
                _reason = "harmony null";
                return;
            }

            if (!QualityConfig.SorterQualityFilterEnabled)
            {
                _reason = "feature off";
                return;
            }

            try
            {
                MethodInfo openPostfix = typeof(P5_SorterFilterUi).GetMethod(
                    "UpdatePostfix", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo regPostfix = typeof(P5_SorterFilterUi).GetMethod(
                    "RegPostfix", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo unregPostfix = typeof(P5_SorterFilterUi).GetMethod(
                    "UnregPostfix", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo resetPostfix = typeof(P5_SorterFilterUi).GetMethod(
                    "ResetPostfix", BindingFlags.Static | BindingFlags.NonPublic);
                if (openPostfix == null || regPostfix == null || unregPostfix == null || resetPostfix == null)
                {
                    _reason = "helper missing";
                    return;
                }

                int installed = 0;
                installed += PatchOne(harmony, "UIInserterWindow", "_OnUpdate", Type.EmptyTypes, openPostfix);
                installed += PatchOne(harmony, "UIInserterWindow", "_OnRegEvent", Type.EmptyTypes, regPostfix);
                installed += PatchOne(harmony, "UIInserterWindow", "_OnUnregEvent", Type.EmptyTypes, unregPostfix);
                installed += PatchOne(harmony, "UIInserterWindow", "OnResetFilterButtonClick",
                    Type.EmptyTypes, resetPostfix);
                if (installed <= 0)
                {
                    _reason = "anchor not found";
                    QualityEnv.WarnOnce("sorterfilter.ui.apply",
                        Tag + "NOT ARMED (UIInserterWindow anchor not found): sorter quality UI stays vanilla");
                    return;
                }

                _armed = true;
                _reason = "armed";
                QualityEnv.LogInfo(Tag + "ui patch registered: UIInserterWindow methods=" + installed
                    + " [Shift+right-click = quality threshold]");
            }
            catch (Exception e)
            {
                _armed = false;
                _reason = "apply failed";
                QualityEnv.WarnOnce("sorterfilter.ui.apply", e);
            }
        }

        private static int PatchOne(Harmony harmony, string typeName, string methodName, Type[] args,
            MethodInfo postfix)
        {
            try
            {
                Type type = typeName == "UIInserterWindow" ? typeof(UIInserterWindow) : null;
                if (type == null)
                {
                    return 0;
                }

                MethodInfo target = type.GetMethod(methodName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, args, null);
                if (target == null)
                {
                    return 0;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                return 1;
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("sorterfilter.ui.patch." + methodName, e);
                return 0;
            }
        }

        /// <summary>Postfix on UIInserterWindow._OnUpdate: refresh the caption of the filter button.</summary>
        private static void UpdatePostfix(UIInserterWindow __instance)
        {
            if (!_armed || __instance == null)
            {
                return;
            }

            try
            {
                int planetId;
                int entityId;
                if (!TryResolve(__instance, out planetId, out entityId))
                {
                    return;
                }

                int min = QualityData.GetSorterMin(planetId, entityId);
                if (min <= 0 || __instance.filterText == null)
                {
                    return;
                }

                // Reuse the vanilla item name when the game has already written it, otherwise look
                // the filter up ourselves (the vanilla caption is "设置过滤器" while filter == 0).
                string name = null;
                try
                {
                    InserterComponent inserter = __instance.factory.factorySystem.inserterPool[__instance.inserterId];
                    if (inserter.filter != 0)
                    {
                        ItemProto proto = LDB.items.Select(inserter.filter);
                        if (proto != null)
                        {
                            name = proto.name;
                        }
                    }
                }
                catch (Exception)
                {
                    // fall back to the caption the vanilla window already produced
                }

                if (name == null)
                {
                    name = __instance.filterText.text;
                }

                __instance.filterText.text = QualityMath.SorterFilterText(name, min);
                _texts++;
            }
            catch (Exception e)
            {
                WarnOnce("sorterfilter.ui.update", e);
            }
        }

        /// <summary>Postfix on UIInserterWindow._OnRegEvent: learn the right-click gesture.</summary>
        private static void RegPostfix(UIInserterWindow __instance)
        {
            if (!_armed || __instance == null || __instance.filterButton == null)
            {
                return;
            }

            try
            {
                __instance.filterButton.onRightClick += OnFilterRightClick;
            }
            catch (Exception e)
            {
                WarnOnce("sorterfilter.ui.reg", e);
            }
        }

        /// <summary>Postfix on UIInserterWindow._OnUnregEvent: drop it again (the window is pooled).</summary>
        private static void UnregPostfix(UIInserterWindow __instance)
        {
            if (__instance == null || __instance.filterButton == null)
            {
                return;
            }

            try
            {
                __instance.filterButton.onRightClick -= OnFilterRightClick;
            }
            catch (Exception)
            {
                // the window is going away with the session
            }
        }

        /// <summary>Postfix on UIInserterWindow.OnResetFilterButtonClick: drop the threshold too.</summary>
        private static void ResetPostfix(UIInserterWindow __instance)
        {
            if (!_armed || __instance == null)
            {
                return;
            }

            try
            {
                int planetId;
                int entityId;
                if (!TryResolve(__instance, out planetId, out entityId))
                {
                    return;
                }

                QualityData.ClearSorterMin(planetId, entityId);
                QualityEnv.LogInfo(Tag + "ui threshold cleared by reset button p=" + planetId
                    + " e=" + entityId);
            }
            catch (Exception e)
            {
                WarnOnce("sorterfilter.ui.reset", e);
            }
        }

        private static void OnFilterRightClick(int slot)
        {
            try
            {
                UIGame uiGame = UIRoot.instance == null ? null : UIRoot.instance.uiGame;
                UIInserterWindow window = uiGame == null ? null : uiGame.inserterWindow;
                if (window == null || !window.active)
                {
                    return;
                }

                int planetId;
                int entityId;
                if (!TryResolve(window, out planetId, out entityId))
                {
                    return;
                }

                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                if (!shift)
                {
                    return;
                }

                int current = QualityData.GetSorterMin(planetId, entityId);
                int next = ctrl ? 0 : QualityMath.NextQualityMin(current);
                QualityData.SetSorterMin(planetId, entityId, next);
                _clicks++;
                if (window.filterText != null)
                {
                    string name = window.filterText.text;
                    window.filterText.text = QualityMath.SorterFilterText(name, next);
                }

                QualityEnv.LogInfo(Tag + "ui threshold p=" + planetId + " e=" + entityId
                    + " " + current + " -> " + next + " clicks=" + _clicks);
            }
            catch (Exception e)
            {
                WarnOnce("sorterfilter.ui.click", e);
            }
        }

        private static bool TryResolve(UIInserterWindow window, out int planetId, out int entityId)
        {
            planetId = -1;
            entityId = -1;
            try
            {
                if (window.factory == null || window.factory.planet == null)
                {
                    return false;
                }

                FactorySystem system = window.factory.factorySystem;
                int id = window.inserterId;
                if (system == null || system.inserterPool == null || id <= 0 || id >= system.inserterPool.Length)
                {
                    return false;
                }

                InserterComponent inserter = system.inserterPool[id];
                if (inserter.id != id || inserter.entityId <= 0)
                {
                    return false;
                }

                planetId = window.factory.planet.id;
                entityId = inserter.entityId;
                return true;
            }
            catch (Exception)
            {
                planetId = -1;
                entityId = -1;
                return false;
            }
        }

        private static void WarnOnce(string key, Exception e)
        {
            if (_warned)
            {
                return;
            }

            _warned = true;
            QualityEnv.WarnOnce(key, e);
        }
    }
}
