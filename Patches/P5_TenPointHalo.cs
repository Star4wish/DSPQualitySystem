using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.11.9 (task-19): a golden ring hovering above every max-quality building. The ring is a
    /// generated texture on a camera-facing quad (3D MeshRenderer), pooled and recycled per frame.
    /// Nothing on the building itself is touched: no renderer/material/save changes.
    /// If no suitable shader exists, the same texture is drawn through a screen-space canvas as a
    /// fallback (one warning is logged).
    /// </summary>
    internal static partial class P5_TenPointHalo
    {
        private const int MarkLogLimit = 20;
        private const int EffectiveLogEvery = 300;
        private const int TextureSize = 256;
        private const int MaxPoolSize = 256;

        private static bool _installed;
        private static string _reason;
        private static Transform _root;
        private static Texture2D _texture;
        private static Mesh _quadMesh;
        private static Material _materialTemplate;
        private static string _shaderName = "screenspace";
        private static bool _screenSpace;

        private static Canvas _canvas;
        private static HaloSpriteGraphic _graphic;

        private static readonly List<HaloInstance> Pool = new List<HaloInstance>(64);
        private static readonly List<int> Ids = new List<int>(128);
        private static readonly List<int> CandidateIds = new List<int>(128);
        private static readonly List<Vector3> Positions = new List<Vector3>(128);
        private static readonly List<Vector3> Tops = new List<Vector3>(128);
        private static readonly List<float> Distances = new List<float>(128);
        private static readonly List<float> Radii = new List<float>(128);
        private static readonly List<HaloSprite> Sprites = new List<HaloSprite>(64);
        private static int[] _selected = new int[256];
        private static float[] _distanceBuffer = new float[256];

        private static int _marked;
        private static int _visible;
        private static long _tickCounter;
        private static int _markLogCount;
        private static bool _disabledLogged;
        private static bool _tickWarned;
        private static bool _projectionLogged;

        internal struct HaloSprite
        {
            public float x;
            public float y;
            public float radius;
            public float alpha;
            public float spin;
        }

        private sealed class HaloInstance
        {
            public GameObject go;
            public MeshRenderer renderer;
            public Material material;
            public bool used;
        }

        internal static Texture2D RingTexture
        {
            get { return _texture; }
        }

        internal static Color HaloColor
        {
            get
            {
                // v0.12.2: tint comes from the config file (tenPointHaloColor) so players can pick
                // their own marker colour; malformed values fall back to the default gold.
                float r;
                float g;
                float b;
                float a;
                if (!QualityMath.TryParseHexColor(QualityConfig.HaloColorHex, out r, out g, out b, out a))
                {
                    return new Color(1f, 0.8235294f, 0.2901961f);
                }

                return new Color(r, g, b, a);
            }
        }

        public static bool Installed
        {
            get { return _installed; }
        }

        public static void Install(GameObject host)
        {
            if (_installed)
            {
                return;
            }

            if (host == null)
            {
                _reason = "plugin host object is null";
                return;
            }

            try
            {
                var rootObject = new GameObject("DSPQualitySystem.HaloRoot");
                rootObject.transform.SetParent(host.transform, false);
                _root = rootObject.transform;

                _texture = BuildRingTexture();
                _quadMesh = BuildQuadMesh();

                Shader shader = null;
                string[] preferences = { "Legacy Shaders/Particles/Additive", "Sprites/Default", "Unlit/Transparent" };
                for (int i = 0; i < preferences.Length; i++)
                {
                    Shader candidate = Shader.Find(preferences[i]);
                    if (candidate != null)
                    {
                        shader = candidate;
                        _shaderName = preferences[i];
                        break;
                    }
                }

                if (shader != null)
                {
                    _materialTemplate = new Material(shader);
                    _materialTemplate.name = "DSPQualitySystem.Halo";
                    if (_materialTemplate.HasProperty("_MainTex"))
                    {
                        _materialTemplate.SetTexture("_MainTex", _texture);
                    }

                    _materialTemplate.renderQueue = 3100;
                    if (_materialTemplate.HasProperty("_ZWrite"))
                    {
                        _materialTemplate.SetInt("_ZWrite", 0);
                    }

                    if (_materialTemplate.HasProperty("_ZTest"))
                    {
                        _materialTemplate.SetInt("_ZTest", 8);
                    }

                    if (_materialTemplate.HasProperty("_Cull"))
                    {
                        _materialTemplate.SetInt("_Cull", 0);
                    }
                    _screenSpace = false;
                }
                else
                {
                    _screenSpace = true;
                    _shaderName = "screenspace";
                    var canvasObject = new GameObject("DSPQualitySystem.HaloCanvas");
                    canvasObject.transform.SetParent(host.transform, false);
                    _canvas = canvasObject.AddComponent<Canvas>();
                    _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    _canvas.sortingOrder = -999;

                    var meshObject = new GameObject("HaloMesh");
                    meshObject.transform.SetParent(canvasObject.transform, false);
                    var rect = meshObject.AddComponent<RectTransform>();
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.pivot = Vector2.zero;
                    rect.offsetMin = Vector2.zero;
                    rect.offsetMax = Vector2.zero;
                    rect.localScale = Vector3.one;
                    _graphic = meshObject.AddComponent<HaloSpriteGraphic>();
                    _graphic.raycastTarget = false;

                    if (Plugin.Log != null)
                    {
                        Plugin.Log.LogWarning("[QSystem][halo] no suitable shader found, falling back to screen space sprites");
                    }
                }

                _installed = true;
                _reason = null;
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogInfo("[QSystem][halo] armed (max=" + QualityConfig.TenPointHaloMaxClamped
                        + " lift=" + QualityConfig.TenPointHaloLiftClamped.ToString("F1")
                        + " size=" + QualityConfig.TenPointHaloSizeClamped.ToString("F1")
                        + " style=" + QualityConfig.MarkerStyleName
                        + " color=" + QualityConfig.HaloColorHex
                        + " pulse=" + QualityConfig.HaloPulseEnabled
                        + " spin=" + QualityConfig.HaloSpinClamped.ToString("F1")
                        + " shader=" + _shaderName + ")");
                }
            }
            catch (Exception e)
            {
                _installed = false;
                _reason = "install failed: " + e.Message;
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning("[QSystem][halo] NOT ARMED (no golden halo): " + e);
                }
            }
        }

        public static void Tick()
        {
            if (!_installed)
            {
                return;
            }

            try
            {
                int maxTargets = QualityConfig.TenPointHaloMaxClamped;
                if (QualityConfig.Enabled == null || !QualityConfig.Enabled.Value
                    || !QualityConfig.HaloArmed
                    || maxTargets <= 0)
                {
                    Hide();
                    LogDisabledOnce();
                    return;
                }

                PlanetData planet = GameMain.localPlanet;
                if (planet == null || planet.factory == null || planet.id <= 0)
                {
                    Hide();
                    return;
                }

                Camera camera = GameCamera.main;
                if (camera == null)
                {
                    camera = Camera.main;
                }

                if (camera == null)
                {
                    Hide();
                    return;
                }

                int minLevel = QualityConfig.MaxLevel;
                if (minLevel <= 0)
                {
                    Hide();
                    return;
                }

                QualityData.CollectFullQualityEntities(planet.id, minLevel, Ids);
                if (Ids.Count == 0)
                {
                    Hide();
                    LogEffective();
                    return;
                }

                PlanetFactory factory = planet.factory;
                EntityData[] entityPool = factory.entityPool;
                if (entityPool == null)
                {
                    Hide();
                    return;
                }

                float screenWidth = Screen.width;
                float screenHeight = Screen.height;
                float haloLift = QualityConfig.TenPointHaloLiftClamped;
                float haloScale = QualityConfig.TenPointHaloSizeClamped;
                float time = Time.time;
                // v0.12.2: pulse and spin are config driven (tenPointHaloPulse / tenPointHaloSpin).
                float pulse = QualityMath.HaloPulseAlpha(time, QualityConfig.HaloPulseEnabled);
                float bob = QualityMath.HaloBobOffset(time);
                float spin = QualityMath.HaloSpinDegrees(time, QualityConfig.HaloSpinClamped);

                CandidateIds.Clear();
                Positions.Clear();
                Tops.Clear();
                Distances.Clear();
                Radii.Clear();
                for (int i = 0; i < Ids.Count; i++)
                {
                    int eid = Ids[i];
                    if (eid <= 0 || eid >= entityPool.Length)
                    {
                        continue;
                    }

                    EntityData entity = entityPool[eid];
                    if (entity.id != eid || entity.protoId <= 0)
                    {
                        continue;
                    }

                    ItemProto proto = LDB.items.Select(entity.protoId);
                    if (proto == null)
                    {
                        continue;
                    }

                    PrefabDesc desc = proto.prefabDesc;
                    Vector3 center = entity.pos;
                    float sizeX = 4f;
                    float sizeY = 3f;
                    float sizeZ = 4f;
                    if (desc != null)
                    {
                        center += entity.rot * desc.selectCenter;
                        if (desc.selectSize.x > 0.01f)
                        {
                            sizeX = desc.selectSize.x;
                        }

                        if (desc.selectSize.y > 0.01f)
                        {
                            sizeY = desc.selectSize.y;
                        }

                        if (desc.selectSize.z > 0.01f)
                        {
                            sizeZ = desc.selectSize.z;
                        }

                        if (sizeX < 0.01f && sizeZ < 0.01f && desc.roughRadius > 0.01f)
                        {
                            sizeX = desc.roughRadius * 2f;
                            sizeZ = desc.roughRadius * 2f;
                        }
                    }

                    Vector3 up = entity.pos.sqrMagnitude > 1e-4f ? entity.pos.normalized : Vector3.up;
                    Vector3 top = center + up * (sizeY * 0.5f);
                    Vector3 position = top + up * (haloLift + bob);
                    float distance = Vector3.Distance(camera.transform.position, position);
                    if (distance > QualityMath.HaloMaxDrawDistance)
                    {
                        continue;
                    }

                    Vector3 screenPoint = camera.WorldToScreenPoint(position);
                    if (screenPoint.z <= 0f || screenPoint.x < 0f || screenPoint.y < 0f
                        || screenPoint.x > screenWidth || screenPoint.y > screenHeight)
                    {
                        continue;
                    }

                    CandidateIds.Add(eid);
                    Positions.Add(position);
                    Tops.Add(top);
                    Distances.Add(distance);
                    Radii.Add(QualityMath.HaloRadius(sizeX, sizeZ, haloScale));
                }

                if (CandidateIds.Count == 0)
                {
                    Hide();
                    LogEffective();
                    return;
                }

                EnsureBuffers(CandidateIds.Count);
                for (int i = 0; i < CandidateIds.Count; i++)
                {
                    _distanceBuffer[i] = Distances[i];
                }

                int selected = QualityMath.SelectHaloTargets(_distanceBuffer, CandidateIds.Count,
                    maxTargets, _selected);
                if (selected <= 0)
                {
                    Hide();
                    LogEffective();
                    return;
                }

                if (_screenSpace)
                {
                    DrawScreenSpace(camera, selected, pulse, spin);
                }
                else
                {
                    DrawWorld(camera, selected, pulse, spin);
                }

                _marked = selected;
                LogMarks(selected, pulse, haloLift);
                LogEffective();
            }
            catch (Exception e)
            {
                Hide();
                if (!_tickWarned)
                {
                    _tickWarned = true;
                    if (Plugin.Log != null)
                    {
                        Plugin.Log.LogWarning("[QSystem][halo] tick failed, halo disabled this session: " + e);
                    }
                }
            }
        }

        public static string Describe()
        {
            if (!_installed)
            {
                return "halo NOT ARMED (no golden ring, reason: " + (_reason ?? "not installed") + ")";
            }

            if (QualityConfig.Enabled == null || !QualityConfig.Enabled.Value)
            {
                return "halo armed but disabled by config (enabled=false)";
            }

            if (!QualityConfig.HaloArmed)
            {
                return "halo armed but disabled by config (tenPointMarkerStyle=" + QualityConfig.MarkerStyleName
                    + ", tenPointHalo=" + (QualityConfig.TenPointHalo == null || QualityConfig.TenPointHalo.Value) + ")";
            }

            int maxTargets = QualityConfig.TenPointHaloMaxClamped;
            if (maxTargets <= 0)
            {
                return "halo armed but disabled by config (tenPointHaloMax=0)";
            }

            return "halo armed (ring above 10-point buildings: style=" + QualityConfig.MarkerStyleName
                + " max=" + maxTargets
                + " lift=" + QualityConfig.TenPointHaloLiftClamped.ToString("F1")
                + " size=" + QualityConfig.TenPointHaloSizeClamped.ToString("F1")
                + " color=" + QualityConfig.HaloColorHex
                + " pulse=" + QualityConfig.HaloPulseEnabled
                + " spin=" + QualityConfig.HaloSpinClamped.ToString("F1")
                + " shader=" + _shaderName + ", nearest first; building renderers untouched)";
        }

        private static void LogMarks(int selected, float pulse, float haloLiftForLog)
        {
            if (_markLogCount >= MarkLogLimit || !QualityConfig.DevMode || Plugin.Log == null)
            {
                return;
            }

            for (int i = 0; i < selected && _markLogCount < MarkLogLimit; i++)
            {
                int index = _selected[i];
                Plugin.Log.LogInfo("[QSystem][halo] mark eid=" + CandidateIds[index]
                    + " world=" + Positions[index].ToString("F1")
                    + " top=" + Tops[index].ToString("F1")
                    + " lift=" + haloLiftForLog.ToString("F1")
                    + " dist=" + Distances[index].ToString("F1")
                    + " radius=" + Radii[index].ToString("F2")
                    + " alpha=" + pulse.ToString("F2"));
                _markLogCount++;
            }
        }

        private static void LogProjectionOnce(Rect canvasRect)
        {
            if (_projectionLogged)
            {
                return;
            }

            _projectionLogged = true;
            if (Plugin.Log == null)
            {
                return;
            }

            Plugin.Log.LogInfo("[QSystem][halo] projection: screen=" + Screen.width + "x" + Screen.height
                + " canvas=" + canvasRect.width.ToString("F0") + "x" + canvasRect.height.ToString("F0"));
        }

        private static void LogEffective()
        {
            _tickCounter++;
            if (_tickCounter % EffectiveLogEvery != 0 || Plugin.Log == null)
            {
                return;
            }

            Plugin.Log.LogInfo("[QSystem][halo] marked=" + _marked + " visible=" + _visible
                + " max=" + QualityConfig.TenPointHaloMaxClamped);
        }

        private static void LogDisabledOnce()
        {
            if (_disabledLogged || Plugin.Log == null)
            {
                return;
            }

            _disabledLogged = true;
            string reason = QualityConfig.TenPointHaloMaxClamped <= 0
                ? "tenPointHaloMax=0"
                : "tenPointMarkerStyle=" + QualityConfig.MarkerStyleName
                    + " / tenPointHalo=false (or master enabled=false)";
            Plugin.Log.LogInfo("[QSystem][halo] hidden (" + reason + ")");
        }

    }
}
