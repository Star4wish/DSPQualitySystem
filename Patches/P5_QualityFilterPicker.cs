using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.17.0: quality-filter editor hosted INSIDE the vanilla item picker (UIItemPicker).
    ///
    /// WHY THIS FILE EXISTS
    ///   v0.16.1 drew a four-row panel inside the splitter window. In game the buttons were not
    ///   clickable (the panel lived outside the picker/window raycast area) and the user wanted the
    ///   entry point that the vanilla game already uses for item filters: the item picker popup.
    ///   So the whole editor moved here:
    ///     * clicking a splitter filter circle (UISplitterWindow.OnCircleFilterClick) records
    ///       (planet, splitter, port), then the picker popup grows one footer row:
    ///         [口A 品质分流 开/关] [物品: xxx] [-] ≥N [+]
    ///     * clicking a sorter filter button (UIInserterWindow.OnSelectFilterClick) records
    ///       (planet, inserter) and the popup grows:
    ///         [分拣器 品质阈值] [-] ≥N [+]
    ///   Picking an item from the grid keeps the vanilla behaviour (the original picker callback is
    ///   still invoked, so the vanilla global filter is written) and additionally stores the item as
    ///   the port rule item when the switch is on.
    ///
    /// CLICK RELIABILITY (the user could not click the old panel)
    ///   Everything is parented to the picker's own RectTransform (inside the rect the picker uses to
    ///   decide "clicked outside -> close"), every GameObject is on layer 5 (UI), clickable parts are
    ///   a real UnityEngine.UI.Button with an Image whose raycastTarget is true and
    ///   transition == None, and all Text graphics have raycastTarget == false.
    ///
    /// DATA
    ///   Writes only to the MOD side tables (QualityData.SetSplitterRule / SetSorterMin), i.e. the
    ///   v0.16 trailer JSON ("splittersV2" / "sorters"). Nothing on the vanilla component is written
    ///   by this file except through the vanilla picker callback that already existed.
    /// </summary>
    internal static class P5_QualityFilterPicker
    {
        internal const string Tag = "[QSystem][filterpicker] ";

        internal const string RowName = "QSystemFilterPickerRow";
        internal const string SwitchName = "QSystemFilterPickerSwitch";
        internal const string ItemName = "QSystemFilterPickerItem";
        internal const string MinusName = "QSystemFilterPickerMinus";
        internal const string MinName = "QSystemFilterPickerMin";
        internal const string PlusName = "QSystemFilterPickerPlus";

        private const float RowHeight = 28f;
        private const float RowBottom = 6f;
        private const float FallbackWidth = 620f;
        private const int LayerUi = 5;

        private enum Kind
        {
            None = 0,
            Splitter = 1,
            Sorter = 2
        }

        private static bool _armed;
        private static string _reason = "not applied";
        private static int _clicks;
        private static int _writes;

        // Captured context: which object opened the picker, and for which port.
        private static Kind _kind;
        private static int _planetId = -1;
        private static int _entityId;
        private static int _port = -1;

        // Current editor state while the picker is open.
        private static int _itemId;
        private static int _min;
        private static bool _on;

        // Built UI (destroyed on picker close).
        private static UIItemPicker _picker;
        private static RectTransform _row;
        private static Text _switchText;
        private static Text _itemText;
        private static Text _minText;

        internal static bool Armed { get { return _armed; } }
        internal static string Reason { get { return _reason; } }

        internal static string Describe()
        {
            if (!_armed)
            {
                return "NOT ARMED (" + _reason + ")";
            }

            return "armed (vanilla item picker footer; clicks=" + _clicks + " writes=" + _writes + ")";
        }

        // ------------------------------------------------------------------ patching

        internal static void Apply(Harmony harmony)
        {
            _armed = false;
            _reason = "not applied";

            if (harmony == null)
            {
                _reason = "harmony null";
                return;
            }

            bool splitter = QualityConfig.SplitterQualityFilterEnabled;
            bool sorter = QualityConfig.SorterQualityFilterEnabled;
            if (!splitter && !sorter)
            {
                _reason = "feature off";
                QualityEnv.LogInfo(Tag + "picker UI NOT ARMED (quality filter features off)");
                return;
            }

            try
            {
                if (splitter)
                {
                    PatchSplitterEntry(harmony);
                }

                if (sorter)
                {
                    PatchSorterEntry(harmony);
                }

                PatchPopup(harmony);
                PatchClose(harmony);

                _armed = true;
                _reason = "armed";
                QualityEnv.LogInfo(Tag + "picker UI registered: splitter=" + splitter + " sorter=" + sorter);
            }
            catch (Exception e)
            {
                _reason = "patch failed";
                QualityEnv.WarnOnce("filterpicker.apply", e);
            }
        }

        private static void PatchSplitterEntry(Harmony harmony)
        {
            MethodInfo anchor = typeof(UISplitterWindow).GetMethod("OnCircleFilterClick",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(int) }, null);
            MethodInfo prefix = typeof(P5_QualityFilterPicker).GetMethod("SplitterClickPrefix",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (anchor == null || prefix == null)
            {
                QualityEnv.WarnOnce("filterpicker.splitter.entry",
                    Tag + "splitter filter entry not found; splitter rules can still be edited from the splitter window? (no)");
                return;
            }

            harmony.Patch(anchor, prefix: new HarmonyMethod(prefix));
        }

        private static void PatchSorterEntry(Harmony harmony)
        {
            MethodInfo anchor = typeof(UIInserterWindow).GetMethod("OnSelectFilterClick",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                Type.EmptyTypes, null);
            MethodInfo prefix = typeof(P5_QualityFilterPicker).GetMethod("SorterClickPrefix",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (anchor == null || prefix == null)
            {
                QualityEnv.WarnOnce("filterpicker.sorter.entry",
                    Tag + "sorter filter entry not found; sorter thresholds cannot be edited from the picker");
                return;
            }

            harmony.Patch(anchor, prefix: new HarmonyMethod(prefix));
        }

        private static void PatchPopup(Harmony harmony)
        {
            MethodInfo anchor = typeof(UIItemPicker).GetMethod("Popup",
                BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(Vector2), typeof(Action<ItemProto>) }, null);
            MethodInfo postfix = typeof(P5_QualityFilterPicker).GetMethod("PopupPostfix",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (anchor == null || postfix == null)
            {
                QualityEnv.WarnOnce("filterpicker.popup",
                    Tag + "UIItemPicker.Popup not found; picker footer cannot be attached");
                return;
            }

            harmony.Patch(anchor, postfix: new HarmonyMethod(postfix));
        }

        private static void PatchClose(Harmony harmony)
        {
            MethodInfo anchor = typeof(UIItemPicker).GetMethod("_OnClose",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                Type.EmptyTypes, null);
            if (anchor == null)
            {
                return;
            }

            MethodInfo prefix = typeof(P5_QualityFilterPicker).GetMethod("ClosePrefix",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo postfix = typeof(P5_QualityFilterPicker).GetMethod("ClosePostfix",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix != null && postfix != null)
            {
                harmony.Patch(anchor, prefix: new HarmonyMethod(prefix),
                    postfix: new HarmonyMethod(postfix));
            }
        }

        // ------------------------------------------------------------------ entry points

        private static void SplitterClickPrefix(UISplitterWindow __instance, int slot)
        {
            try
            {
                // A second click on the same circle only closes the popup; do not take a context.
                if (UIItemPicker.isOpened || __instance == null)
                {
                    return;
                }

                if (!QualityConfig.SplitterQualityFilterEnabled)
                {
                    return;
                }

                int splitterId = __instance.splitterId;
                if (splitterId <= 0 || __instance.traffic == null || __instance.traffic.splitterPool == null
                    || splitterId >= __instance.traffic.splitterPool.Length
                    || __instance.factory == null || __instance.factory.planet == null)
                {
                    return;
                }

                SplitterComponent sp = __instance.traffic.splitterPool[splitterId];
                if (sp.id != splitterId || sp.entityId <= 0)
                {
                    return;
                }

                _kind = Kind.Splitter;
                _planetId = __instance.factory.planet.id;
                _entityId = sp.entityId;
                _port = slot;
                _itemId = 0;
                _min = 0;
                _on = false;
            }
            catch (Exception e)
            {
                _kind = Kind.None;
                QualityEnv.WarnOnce("filterpicker.splitter.click", e);
            }
        }

        private static void SorterClickPrefix(UIInserterWindow __instance)
        {
            try
            {
                if (UIItemPicker.isOpened || __instance == null)
                {
                    return;
                }

                if (!QualityConfig.SorterQualityFilterEnabled)
                {
                    return;
                }

                int inserterId = __instance.inserterId;
                if (inserterId <= 0 || __instance.factory == null || __instance.factory.planet == null
                    || __instance.factorySystem == null || __instance.factorySystem.inserterPool == null
                    || inserterId >= __instance.factorySystem.inserterPool.Length)
                {
                    return;
                }

                InserterComponent ins = __instance.factorySystem.inserterPool[inserterId];
                if (ins.id != inserterId || ins.entityId <= 0)
                {
                    return;
                }

                _kind = Kind.Sorter;
                _planetId = __instance.factory.planet.id;
                _entityId = ins.entityId;
                _port = -1;
                _itemId = 0;
                _min = 0;
                _on = false;
            }
            catch (Exception e)
            {
                _kind = Kind.None;
                QualityEnv.WarnOnce("filterpicker.sorter.click", e);
            }
        }

        private static void PopupPostfix(Vector2 __0, Action<ItemProto> __1)
        {
            try
            {
                if (_kind == Kind.None || UIRoot.instance == null || UIRoot.instance.uiGame == null)
                {
                    return;
                }

                UIItemPicker picker = UIRoot.instance.uiGame.itemPicker;
                if (picker == null || !picker.active || picker.pickerTrans == null)
                {
                    // Popup refused to open (already open / not inited): drop the stale context.
                    _kind = Kind.None;
                    return;
                }

                _picker = picker;
                if (_kind == Kind.Splitter)
                {
                    if (!QualityConfig.SplitterQualityFilterEnabled || _planetId < 0 || _port < 0)
                    {
                        _kind = Kind.None;
                        return;
                    }

                    int packed = QualityData.GetSplitterRule(_planetId, _entityId, _port);
                    _itemId = QualityMath.SplitterRuleItem(packed);
                    _min = QualityMath.SplitterRuleMin(packed);
                    _on = !QualityMath.SplitterRuleIsEmpty(packed);
                    BuildRow(picker, true);
                    WrapReturn(picker);
                }
                else
                {
                    if (!QualityConfig.SorterQualityFilterEnabled || _planetId < 0)
                    {
                        _kind = Kind.None;
                        return;
                    }

                    _min = QualityData.GetSorterMin(_planetId, _entityId);
                    _on = _min > 0;
                    BuildRow(picker, false);
                }

                RefreshRow();
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("filterpicker.popup", e);
                DestroyRow();
            }
        }

        private static void ClosePrefix()
        {
            // Runs before the vanilla body invokes onReturn, so only tear down the visuals here.
            DestroyRow();
        }

        private static void ClosePostfix()
        {
            _kind = Kind.None;
            _planetId = -1;
            _entityId = 0;
            _port = -1;
            _picker = null;
        }

        private static void WrapReturn(UIItemPicker picker)
        {
            Action<ItemProto> original = picker.onReturn;
            picker.onReturn = delegate(ItemProto proto)
            {
                try
                {
                    OnProtoPicked(proto);
                }
                catch (Exception e)
                {
                    QualityEnv.WarnOnce("filterpicker.return", e);
                }

                if (original != null)
                {
                    original(proto);
                }
            };
        }

        private static void OnProtoPicked(ItemProto proto)
        {
            _clicks++;
            if (proto == null || _kind != Kind.Splitter)
            {
                return;
            }

            // The picked item becomes the rule item; quality filtering only applies while enabled.
            // v0.17.1: picking an item IS the "filter this port by this item" action, so it enables
            // the rule instead of silently doing nothing (v0.17.0 only wrote the rule when the
            // player had already flipped the switch or stepped the threshold, so a plain item pick
            // left just the vanilla global outFilter behind - one of the two live bugs).
            _itemId = proto.ID;
            _on = true;
            WriteSplitterRule();
            RefreshRow();
        }

        // ------------------------------------------------------------------ UI construction

        private static void BuildRow(UIItemPicker picker, bool splitter)
        {
            DestroyRow();

            RectTransform host = picker.pickerTrans;
            float width = FallbackWidth;
            try
            {
                if (host.rect.width > 40f)
                {
                    width = Mathf.Clamp(host.rect.width - 16f, 360f, 900f);
                }
            }
            catch (Exception)
            {
                width = FallbackWidth;
            }

            GameObject rowGo = new GameObject(RowName, typeof(RectTransform));
            rowGo.layer = LayerUi;
            RectTransform row = rowGo.transform as RectTransform;
            row.SetParent(host, false);
            row.anchorMin = new Vector2(0.5f, 0f);
            row.anchorMax = new Vector2(0.5f, 0f);
            row.pivot = new Vector2(0.5f, 0f);
            row.anchoredPosition = new Vector2(0f, RowBottom);
            row.sizeDelta = new Vector2(width, RowHeight);
            row.localScale = Vector3.one;

            Image background = rowGo.AddComponent<Image>();
            background.color = new Color(0.04f, 0.07f, 0.11f, 0.92f);
            background.raycastTarget = true;
            rowGo.transform.SetAsLastSibling();

            float x = 8f;
            Text title = CreateLabel(row, "QSystemFilterPickerTitle", x, 4f, 118f, RowHeight - 8f,
                splitter ? "口" + (char)('A' + _port) + " 品质分流" : "分拣器 品质阈值", 14);

            if (splitter)
            {
                x += 122f;
                Button switchButton = CreateButton(row, SwitchName, x, 2f, 44f, RowHeight - 4f,
                    delegate { OnToggleSwitch(); });
                _switchText = CreateLabel(switchButton.transform, SwitchName + "Text", 0f, 2f,
                    44f, RowHeight - 8f, "关", 14);
                _switchText.alignment = TextAnchor.MiddleCenter;

                x += 50f;
                _itemText = CreateLabel(row, ItemName, x, 4f, 150f, RowHeight - 8f, "物品: 任意物品", 14);
            }

            float right = width - 8f;
            Button plus = CreateButton(row, PlusName, right - 30f, 2f, 30f, RowHeight - 4f,
                delegate { OnStepMin(true); });
            right -= 34f;
            _minText = CreateLabel(row, MinName, right - 62f, 4f, 62f, RowHeight - 8f, "不限", 14);
            _minText.alignment = TextAnchor.MiddleCenter;
            right -= 64f;
            Button minus = CreateButton(row, MinusName, right - 30f, 2f, 30f, RowHeight - 4f,
                delegate { OnStepMin(false); });

            if (plus != null)
            {
                CreateCaption(plus.transform, PlusName + "Text", "+");
            }

            if (minus != null)
            {
                CreateCaption(minus.transform, MinusName + "Text", "-");
            }

            if (title != null)
            {
                title.raycastTarget = false;
            }

            _row = row;
        }

        private static void CreateCaption(Transform parent, string name, string caption)
        {
            Text label = CreateLabel(parent, name, 0f, 2f, 30f, RowHeight - 8f, caption, 15);
            label.alignment = TextAnchor.MiddleCenter;
        }

        private static void DestroyRow()
        {
            if (_row != null)
            {
                try
                {
                    UnityEngine.Object.Destroy(_row.gameObject);
                }
                catch (Exception)
                {
                    // object already gone
                }
            }

            _row = null;
            _switchText = null;
            _itemText = null;
            _minText = null;
        }

        private static GameObject CreateVisual(Transform parent, string name, float x, float y,
            float width, float height, Color color, bool raycast)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerUi;
            RectTransform rect = go.transform as RectTransform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one;

            Image image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return go;
        }

        private static Button CreateButton(Transform parent, string name, float x, float y,
            float width, float height, Action onClick)
        {
            GameObject go = CreateVisual(parent, name, x, y, width, height,
                new Color(0.16f, 0.24f, 0.32f, 0.95f), true);
            Button button = go.AddComponent<Button>();
            Image image = go.GetComponent<Image>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            if (onClick != null)
            {
                button.onClick.AddListener(delegate
                {
                    try
                    {
                        onClick();
                    }
                    catch (Exception e)
                    {
                        QualityEnv.WarnOnce("filterpicker.button", e);
                    }
                });
            }

            return button;
        }

        private static Text CreateLabel(Transform parent, string name, float x, float y,
            float width, float height, string text, int fontSize)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerUi;
            RectTransform rect = go.transform as RectTransform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one;

            Text label = go.AddComponent<Text>();
            label.font = ResolveFont();
            label.fontSize = fontSize;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            label.text = text;
            label.color = LabelColor();
            return label;
        }

        private static Font ResolveFont()
        {
            try
            {
                UISplitterWindow window = UIRoot.instance != null && UIRoot.instance.uiGame != null
                    ? UIRoot.instance.uiGame.splitterWindow
                    : null;
                if (window != null && window.titleText != null && window.titleText.font != null)
                {
                    return window.titleText.font;
                }
            }
            catch (Exception)
            {
                // fall through
            }

            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        private static Color LabelColor()
        {
            return new Color(0.92f, 0.95f, 1f, 1f);
        }

        // ------------------------------------------------------------------ interactions

        private static void OnToggleSwitch()
        {
            _clicks++;
            _on = !_on;
            if (_kind == Kind.Splitter)
            {
                WriteSplitterRule();
            }

            RefreshRow();
        }

        private static void OnStepMin(bool forward)
        {
            _clicks++;
            _min = forward ? QualityMath.NextQualityMin(_min) : QualityMath.PrevQualityMin(_min);
            if (_kind == Kind.Splitter)
            {
                // Stepping the threshold is an explicit intent to filter: enable the switch.
                _on = true;
                WriteSplitterRule();
            }
            else if (_kind == Kind.Sorter && _planetId >= 0 && _entityId > 0)
            {
                if (_min <= 0)
                {
                    QualityData.ClearSorterMin(_planetId, _entityId);
                }
                else
                {
                    QualityData.SetSorterMin(_planetId, _entityId, _min);
                }

                _writes++;
                QualityEnv.LogInfo(Tag + "sorter min=" + _min + " p=" + _planetId + " e=" + _entityId);
            }

            RefreshRow();
        }

        private static void WriteSplitterRule()
        {
            if (_kind != Kind.Splitter || _planetId < 0 || _entityId <= 0 || _port < 0)
            {
                return;
            }

            int packed = _on
                ? QualityMath.PackSplitterRule(_itemId, _min)
                : QualityMath.SplitterRuleNone;
            QualityData.SetSplitterRule(_planetId, _entityId, _port, packed);
            _writes++;
            QualityEnv.LogInfo(Tag + "splitter port=" + _port + " on=" + _on
                + " item=" + _itemId + " min=" + _min + " p=" + _planetId + " e=" + _entityId);
        }

        private static void RefreshRow()
        {
            if (_switchText != null)
            {
                _switchText.text = _on ? "开" : "关";
            }

            if (_itemText != null)
            {
                _itemText.text = "物品: " + QualityMath.SplitterRuleItemText(_itemId, ItemNameOf(_itemId));
            }

            if (_minText != null)
            {
                if (_kind == Kind.Sorter)
                {
                    _minText.text = QualityMath.SplitterRuleMinText(_min);
                }
                else
                {
                    _minText.text = QualityMath.SplitterRuleMinText(_on ? _min : 0);
                }
            }
        }

        private static string ItemNameOf(int itemId)
        {
            if (itemId <= 0)
            {
                return null;
            }

            try
            {
                ItemProto proto = LDB.items.Select(itemId);
                if (proto != null)
                {
                    return proto.name;
                }
            }
            catch (Exception)
            {
                // fall through
            }

            return "#" + itemId;
        }
    }
}
