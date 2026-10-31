using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.14.0 (P6 / 物品品质角标): paints the per-item quality points onto item icons.
    ///
    /// Design (Lead hard requirements):
    /// - One badge per storage grid cell, created/destroyed together with the vanilla per-cell
    ///   number texts. Nothing is instantiated while refreshing, so opening/expanding/closing a
    ///   container cannot leak GameObjects.
    /// - The points are derived from the existing grid data: per-item = grid.inc / grid.count.
    ///   Empty cells (count == 0) and cells without points (inc == 0) never get a badge.
    /// - Pure display: no save data is written, no game data is touched.
    /// - itemQualityBadges=false: the patch is never registered (zero patches, zero GameObjects,
    ///   vanilla identical). Switching it off at runtime hides the badge objects that already exist.
    ///
    /// Hooks:
    /// - UIStorageGrid.OnStorageSizeChanged postfix  -> grow/shrink the per-cell badge array and
    ///   reposition the badges (same rebuild point as numTexts).
    /// - UIStorageGrid.OnStorageContentChanged postfix -> refresh text/colour/visibility. This runs
    ///   after the vanilla stack-number pass, so one pass per content change (dirty flag driven by
    ///   storage.changed in _OnUpdate) instead of a per-frame scan.
    /// - UIHandTip._OnUpdate postfix -> badge for the item held by the player.
    ///
    /// Product preview icons (UIAssemblerWindow.productIcon0/1 etc.) are intentionally not covered:
    /// they show recipe products without real inc data; the same items are already badged in the
    /// servingBox grid of that window (documented in README known limitations).
    /// </summary>
    internal static class P6_ItemQualityBadges
    {
        internal const string Tag = "[QSystem][badges] ";

        private const BindingFlags AnyInstance =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        internal const int MaxCells = 400;
        private const long EffectiveLogEvery = 200;

        private static readonly FieldInfo StorageField =
            typeof(UIStorageGrid).GetField("storage", AnyInstance);

        private static bool _armed;
        private static string _reason = "not applied yet";
        private static long _gridRefreshes;
        private static long _handRefreshes;
        private static int _hostsCreated;
        private static int _debugLogged;

        internal static bool Armed { get { return _armed; } }
        internal static string Reason { get { return _reason; } }

        internal static void Apply(Harmony harmony)
        {
            try
            {
                if (harmony == null)
                {
                    _reason = "harmony is null";
                    return;
                }
                if (!QualityConfig.ItemQualityBadgesEnabled)
                {
                    _reason = "itemQualityBadges=false (or enabled=false)";
                    Info("NOT ARMED: " + _reason + "; vanilla item icons");
                    return;
                }

                MethodInfo content = FindNoArg(typeof(UIStorageGrid), "OnStorageContentChanged");
                MethodInfo size = FindNoArg(typeof(UIStorageGrid), "OnStorageSizeChanged");
                MethodInfo hand = FindNoArg(typeof(UIHandTip), "_OnUpdate");
                if (content == null || size == null || hand == null)
                {
                    _reason = "missing target method(s):"
                        + (content == null ? " OnStorageContentChanged" : string.Empty)
                        + (size == null ? " OnStorageSizeChanged" : string.Empty)
                        + (hand == null ? " UIHandTip._OnUpdate" : string.Empty);
                    Warn(_reason);
                    return;
                }

                harmony.Patch(content, postfix: new HarmonyMethod(
                    typeof(P6_ItemQualityBadges).GetMethod("GridContentPostfix",
                        BindingFlags.NonPublic | BindingFlags.Static)));
                harmony.Patch(size, postfix: new HarmonyMethod(
                    typeof(P6_ItemQualityBadges).GetMethod("GridSizePostfix",
                        BindingFlags.NonPublic | BindingFlags.Static)));
                harmony.Patch(hand, postfix: new HarmonyMethod(
                    typeof(P6_ItemQualityBadges).GetMethod("HandTipPostfix",
                        BindingFlags.NonPublic | BindingFlags.Static)));

                _armed = true;
                _reason = null;
                if (QualityConfig.ItemQualityBadges != null)
                {
                    QualityConfig.ItemQualityBadges.SettingChanged += OnSettingChanged;
                }
                Info("patch registered: UIStorageGrid.OnStorageSizeChanged + "
                    + "OnStorageContentChanged + UIHandTip._OnUpdate postfixes "
                    + "(style=" + QualityConfig.ItemQualityBadgeStyleNormalized
                    + " minPoints=" + QualityConfig.ItemQualityBadgeMinPointsClamped + ")");
            }
            catch (Exception e)
            {
                _reason = e.GetType().Name + ": " + e.Message;
                QualityEnv.WarnOnce("badges.apply", e);
            }
        }

        internal static string Describe()
        {
            if (!_armed)
            {
                return "badges NOT ARMED (" + _reason + ")";
            }
            return "armed (grid size+content postfix, hand-tip postfix; hosts=" + _hostsCreated
                + " gridRefreshes=" + _gridRefreshes
                + " handRefreshes=" + _handRefreshes
                + "; style=" + QualityConfig.ItemQualityBadgeStyleNormalized
                + " minPoints=" + QualityConfig.ItemQualityBadgeMinPointsClamped + ")";
        }

        internal static void ResetState()
        {
            _gridRefreshes = 0;
            _handRefreshes = 0;
            _debugLogged = 0;
        }

        // ---------------------------------------------------------------- patches

        private static void GridSizePostfix(UIStorageGrid __instance)
        {
            try
            {
                if (!_armed || __instance == null) return;
                if (!QualityConfig.ItemQualityBadgesEnabled)
                {
                    HideGrid(__instance);
                    return;
                }
                QualityBadgeHost host = EnsureGridHost(__instance);
                if (host != null) host.SyncCapacity(__instance);
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("badges.gridsize", e);
            }
        }

        private static void GridContentPostfix(UIStorageGrid __instance)
        {
            try
            {
                if (!_armed || __instance == null) return;
                if (!QualityConfig.ItemQualityBadgesEnabled)
                {
                    HideGrid(__instance);
                    return;
                }
                QualityBadgeHost host = EnsureGridHost(__instance);
                if (host == null) return;
                // Idempotent: covers the first open and storage growth without a separate size pass.
                host.SyncCapacity(__instance);
                host.Refresh(__instance);
                _gridRefreshes++;
                if (_gridRefreshes == 1 || _gridRefreshes % EffectiveLogEvery == 0)
                {
                    Info("grid badges effective=" + _gridRefreshes
                        + " badges=" + host.BadgeCount
                        + " size=" + __instance.colCount + "x" + __instance.rowCount);
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("badges.gridcontent", e);
            }
        }

        private static void HandTipPostfix(UIHandTip __instance)
        {
            try
            {
                if (!_armed || __instance == null) return;
                if (!QualityConfig.ItemQualityBadgesEnabled)
                {
                    HideHandTip();
                    return;
                }
                QualityHandBadgeHost host = EnsureHandHost(__instance);
                if (host == null) return;
                host.Refresh(__instance);
                _handRefreshes++;
                if (_handRefreshes == 1 || _handRefreshes % EffectiveLogEvery == 0)
                {
                    Info("hand tip badge effective=" + _handRefreshes);
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("badges.handtip", e);
            }
        }

        private static void OnSettingChanged(object sender, EventArgs e)
        {
            try
            {
                if (!_armed) return;
                if (!QualityConfig.ItemQualityBadgesEnabled)
                {
                    HideAllKnownBadges();
                    Info("disabled at runtime: badges hidden; vanilla item icons (patches stay "
                        + "registered but do nothing until the switch is turned back on)");
                }
                else
                {
                    Info("enabled at runtime: badges return with the next grid/tip refresh "
                        + "(no restart needed for the display path)");
                }
            }
            catch (Exception ex)
            {
                QualityEnv.WarnOnce("badges.setting", ex);
            }
        }

        // ---------------------------------------------------------------- hosts

        private static QualityBadgeHost EnsureGridHost(UIStorageGrid grid)
        {
            if (grid == null || grid.rectTrans == null) return null;
            GameObject go = grid.rectTrans.gameObject;
            QualityBadgeHost host = go.GetComponent<QualityBadgeHost>();
            if (host == null)
            {
                host = go.AddComponent<QualityBadgeHost>();
                host.Bind(grid);
                _hostsCreated++;
            }
            return host;
        }

        private static QualityHandBadgeHost EnsureHandHost(UIHandTip tip)
        {
            if (tip == null) return null;
            RectTransform parent = tip.icon != null ? tip.icon.rectTransform : tip.rtrans;
            if (parent == null) return null;
            QualityHandBadgeHost host = parent.GetComponent<QualityHandBadgeHost>();
            if (host == null)
            {
                host = parent.gameObject.AddComponent<QualityHandBadgeHost>();
                _hostsCreated++;
            }
            return host;
        }

        private static void HideGrid(UIStorageGrid grid)
        {
            if (grid == null || grid.rectTrans == null) return;
            QualityBadgeHost host = grid.rectTrans.gameObject.GetComponent<QualityBadgeHost>();
            if (host != null) host.HideAll();
        }

        private static void HideHandTip()
        {
            try
            {
                UIGame uiGame = UIRoot.instance == null ? null : UIRoot.instance.uiGame;
                if (uiGame == null || uiGame.handTip == null) return;
                UIHandTip tip = uiGame.handTip;
                RectTransform parent = tip.icon != null ? tip.icon.rectTransform : tip.rtrans;
                if (parent == null) return;
                QualityHandBadgeHost host = parent.GetComponent<QualityHandBadgeHost>();
                if (host != null) host.Hide();
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("badges.hidehand", e);
            }
        }

        private static void HideAllKnownBadges()
        {
            try
            {
                var opened = UIStorageGrid.openedStorages;
                if (opened != null)
                {
                    for (int i = 0; i < opened.Count; i++)
                    {
                        HideGrid(opened[i]);
                    }
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("badges.hideall", e);
            }
            HideHandTip();
        }

        // ---------------------------------------------------------------- helpers

        private static MethodInfo FindNoArg(Type type, string name)
        {
            try
            {
                return type.GetMethod(name, AnyInstance, null, Type.EmptyTypes, null);
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("badges.find." + name, e);
                return null;
            }
        }

        internal static StorageComponent GetStorage(UIStorageGrid grid)
        {
            if (grid == null || StorageField == null) return null;
            try
            {
                return StorageField.GetValue(grid) as StorageComponent;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Badge text alpha: dim enough that the game's bloom does not halo the digits.</summary>
        internal const float BadgeTextAlpha = 0.82f;

        internal static Color RgbToColor(int rgb)
        {
            return new Color(
                ((rgb >> 16) & 0xFF) / 255f,
                ((rgb >> 8) & 0xFF) / 255f,
                (rgb & 0xFF) / 255f,
                BadgeTextAlpha);
        }

        internal static void EnsureOutline(GameObject go)
        {
            if (go == null) return;
            Outline outline = go.GetComponent<Outline>();
            if (outline == null) outline = go.AddComponent<Outline>();
            if (outline == null) return;
            outline.effectColor = new Color(0f, 0f, 0f, 0.5f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;
        }

        internal static Font ResolveFont(Text reference)
        {
            if (reference != null && reference.font != null) return reference.font;
            try
            {
                UIGame uiGame = UIRoot.instance == null ? null : UIRoot.instance.uiGame;
                if (uiGame != null)
                {
                    if (uiGame.storageWindow != null && uiGame.storageWindow.titleText != null
                        && uiGame.storageWindow.titleText.font != null)
                    {
                        return uiGame.storageWindow.titleText.font;
                    }
                    if (uiGame.inventoryWindow != null && uiGame.inventoryWindow.titleText != null
                        && uiGame.inventoryWindow.titleText.font != null)
                    {
                        return uiGame.inventoryWindow.titleText.font;
                    }
                }
            }
            catch
            {
                // fall through to the built-in font
            }
            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        internal static void Info(string message)
        {
            try
            {
                Plugin.Log.LogInfo(Tag + message);
            }
            catch
            {
                // never throw from a log helper
            }
        }

        private static void Warn(string message)
        {
            try
            {
                Info(message);
            }
            catch
            {
                // never throw from a log helper
            }
        }

        internal static void DebugDraw(string message)
        {
            if (_debugLogged >= 20) return;
            _debugLogged++;
            try
            {
                Plugin.Log.LogInfo(Tag + "debug " + message);
            }
            catch
            {
                // never throw from a log helper
            }
        }
    }

    /// <summary>
    /// Per-grid badge owner: exactly one badge Text per visible cell, created/destroyed only when
    /// the vanilla grid is rebuilt (OnStorageSizeChanged). It lives on the grid's rectTrans, so it
    /// is destroyed together with the UI window.
    /// </summary>
    internal sealed class QualityBadgeHost : MonoBehaviour
    {
        private const float CellSize = 50f;
        private const float BadgeInset = 1f;

        private Text[] _badges = new Text[0];
        private UIStorageGrid _grid;
        private Font _fallbackFont;

        internal int BadgeCount
        {
            get { return _badges == null ? 0 : _badges.Length; }
        }

        internal void Bind(UIStorageGrid grid)
        {
            _grid = grid;
        }

        internal void SyncCapacity(UIStorageGrid grid)
        {
            _grid = grid;
            if (grid == null || grid.rectTrans == null) return;
            int cols = Mathf.Max(1, grid.colCount);
            int rows = Mathf.Max(0, grid.rowCount);
            int target = cols * rows;
            if (target > P6_ItemQualityBadges.MaxCells) target = P6_ItemQualityBadges.MaxCells;

            if (_badges.Length < target)
            {
                Text[] grown = new Text[target];
                Array.Copy(_badges, grown, _badges.Length);
                for (int i = _badges.Length; i < target; i++)
                {
                    grown[i] = CreateBadge(grid, i, cols);
                }
                _badges = grown;
            }
            else if (_badges.Length > target)
            {
                for (int i = target; i < _badges.Length; i++)
                {
                    if (_badges[i] != null) Destroy(_badges[i].gameObject);
                }
                Text[] shrunk = new Text[target];
                Array.Copy(_badges, shrunk, target);
                _badges = shrunk;
            }

            for (int i = 0; i < _badges.Length; i++)
            {
                PlaceBadge(_badges[i], i, cols);
            }
        }

        internal void Refresh(UIStorageGrid grid)
        {
            _grid = grid;
            if (grid == null) return;
            StorageComponent storage = P6_ItemQualityBadges.GetStorage(grid);
            bool enabled = QualityConfig.ItemQualityBadgesEnabled;
            string style = QualityConfig.ItemQualityBadgeStyleNormalized;
            int minPoints = QualityConfig.ItemQualityBadgeMinPointsClamped;

            for (int i = 0; i < _badges.Length; i++)
            {
                Text badge = _badges[i];
                if (badge == null) continue;

                int points = 0;
                if (enabled && storage != null && i < storage.size)
                {
                    StorageComponent.GRID[] grids = storage.grids;
                    if (grids != null && i < grids.Length)
                    {
                        StorageComponent.GRID cell = grids[i];
                        if (cell.itemId > 0 && cell.count > 0)
                        {
                            points = QualityMath.BadgePointsFromGrid(cell.inc, cell.count);
                        }
                    }
                }

                bool visible = QualityMath.BadgeVisible(points, enabled, minPoints);
                if (visible)
                {
                    badge.text = QualityMath.BadgeLabelText(points, style);
                    badge.color = P6_ItemQualityBadges.RgbToColor(QualityMath.BadgeColorRgb(points));
                    if (!badge.gameObject.activeSelf) badge.gameObject.SetActive(true);
                }
                else if (badge.gameObject.activeSelf)
                {
                    badge.gameObject.SetActive(false);
                }
            }
        }

        internal void HideAll()
        {
            for (int i = 0; i < _badges.Length; i++)
            {
                Text badge = _badges[i];
                if (badge != null && badge.gameObject.activeSelf) badge.gameObject.SetActive(false);
            }
        }

        private Text CreateBadge(UIStorageGrid grid, int index, int cols)
        {
            Text badge = null;
            try
            {
                if (grid.prefabNumText != null)
                {
                    badge = Instantiate(grid.prefabNumText, grid.rectTrans);
                }
                else
                {
                    GameObject go = new GameObject("QSystemQualityBadge",
                        typeof(RectTransform), typeof(Text));
                    RectTransform rt = go.GetComponent<RectTransform>();
                    rt.SetParent(grid.rectTrans, false);
                    badge = go.GetComponent<Text>();
                    badge.font = _fallbackFont != null ? _fallbackFont
                        : (_fallbackFont = P6_ItemQualityBadges.ResolveFont(grid.prefabNumText));
                }
                if (badge == null) return null;

                badge.name = "QSystemQualityBadge";
                badge.text = string.Empty;
                badge.fontSize = 12;
                badge.fontStyle = FontStyle.Bold;
                badge.alignment = TextAnchor.UpperLeft;
                badge.color = Color.white;
                badge.raycastTarget = false;
                badge.supportRichText = false;
                badge.horizontalOverflow = HorizontalWrapMode.Overflow;
                badge.verticalOverflow = VerticalWrapMode.Overflow;
                if (badge.font == null)
                {
                    badge.font = _fallbackFont != null ? _fallbackFont
                        : (_fallbackFont = P6_ItemQualityBadges.ResolveFont(grid.prefabNumText));
                }
                P6_ItemQualityBadges.EnsureOutline(badge.gameObject);
                PlaceBadge(badge, index, cols);
                badge.gameObject.SetActive(false);
                return badge;
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("badges.create", e);
                if (badge != null)
                {
                    Destroy(badge.gameObject);
                }
                return null;
            }
        }

        private static void PlaceBadge(Text badge, int index, int cols)
        {
            if (badge == null) return;
            RectTransform rt = badge.rectTransform;
            if (rt == null) return;
            int col = cols > 0 ? index % cols : 0;
            int row = cols > 0 ? index / cols : 0;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(30f, 16f);
            rt.anchoredPosition = new Vector2(col * CellSize + BadgeInset, -(row * CellSize) - BadgeInset);
            rt.localScale = Vector3.one;
        }
    }

    /// <summary>
    /// Badge for the item currently held by the player (UIHandTip). Created lazily on the icon of
    /// the hand tip once, then only text/colour/visibility change.
    /// </summary>
    internal sealed class QualityHandBadgeHost : MonoBehaviour
    {
        private const float BadgeInset = 2f;

        private Text _badge;
        private Font _fallbackFont;

        internal void Refresh(UIHandTip tip)
        {
            if (tip == null) return;
            Player player = GameMain.mainPlayer;
            if (player == null)
            {
                Hide();
                return;
            }

            bool feature = QualityConfig.ItemQualityBadgesEnabled;
            // Vanilla hides the icon while a build command is active; hide the badge with it.
            bool iconVisible = tip.icon != null && tip.icon.enabled
                && tip.icon.gameObject.activeInHierarchy;

            int points = 0;
            if (feature)
            {
                int inc = player.inhandItemInc;
                int count = player.inhandItemCount;
                if (count > 0 && inc > 0)
                {
                    points = QualityMath.BadgePointsFromGrid(inc, count);
                }
            }

            bool visible = iconVisible
                && QualityMath.BadgeVisible(points, feature,
                    QualityConfig.ItemQualityBadgeMinPointsClamped);
            if (!visible)
            {
                Hide();
                return;
            }

            if (_badge == null) _badge = CreateBadge(tip);
            if (_badge == null) return;
            _badge.text = QualityMath.BadgeLabelText(points,
                QualityConfig.ItemQualityBadgeStyleNormalized);
            _badge.color = P6_ItemQualityBadges.RgbToColor(QualityMath.BadgeColorRgb(points));
            if (!_badge.gameObject.activeSelf) _badge.gameObject.SetActive(true);
        }

        internal void Hide()
        {
            if (_badge != null && _badge.gameObject.activeSelf)
            {
                _badge.gameObject.SetActive(false);
            }
        }

        private Text CreateBadge(UIHandTip tip)
        {
            Text badge = null;
            try
            {
                RectTransform parent = tip.icon != null ? tip.icon.rectTransform : tip.rtrans;
                if (parent == null) return null;

                if (tip.tipText != null)
                {
                    badge = Instantiate(tip.tipText, parent);
                }
                else
                {
                    GameObject go = new GameObject("QSystemQualityHandBadge",
                        typeof(RectTransform), typeof(Text));
                    RectTransform rt = go.GetComponent<RectTransform>();
                    rt.SetParent(parent, false);
                    badge = go.GetComponent<Text>();
                    badge.font = _fallbackFont != null ? _fallbackFont
                        : (_fallbackFont = P6_ItemQualityBadges.ResolveFont(tip.tipText));
                }
                if (badge == null) return null;

                RectTransform badgeRect = badge.rectTransform;
                badgeRect.anchorMin = new Vector2(0f, 1f);
                badgeRect.anchorMax = new Vector2(0f, 1f);
                badgeRect.pivot = new Vector2(0f, 1f);
                badgeRect.sizeDelta = new Vector2(26f, 14f);
                badgeRect.anchoredPosition = new Vector2(BadgeInset, -BadgeInset);
                badgeRect.localScale = Vector3.one;

                badge.name = "QSystemQualityHandBadge";
                badge.text = string.Empty;
                badge.fontSize = 11;
                badge.fontStyle = FontStyle.Bold;
                badge.alignment = TextAnchor.UpperLeft;
                badge.color = Color.white;
                badge.raycastTarget = false;
                badge.supportRichText = false;
                badge.horizontalOverflow = HorizontalWrapMode.Overflow;
                badge.verticalOverflow = VerticalWrapMode.Overflow;
                if (badge.font == null)
                {
                    badge.font = _fallbackFont != null ? _fallbackFont
                        : (_fallbackFont = P6_ItemQualityBadges.ResolveFont(tip.tipText));
                }
                P6_ItemQualityBadges.EnsureOutline(badge.gameObject);
                badge.gameObject.SetActive(false);
                return badge;
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("badges.createhand", e);
                if (badge != null) Destroy(badge.gameObject);
                return null;
            }
        }
    }
}
