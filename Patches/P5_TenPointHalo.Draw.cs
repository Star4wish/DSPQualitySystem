using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DSPQualitySystem.Patches
{
    internal static partial class P5_TenPointHalo
    {
        private static void DrawWorld(Camera camera, int selected, float pulse, float spin)
        {
            _visible = 0;
            Quaternion viewRotation = camera.transform.rotation;
            Color color = HaloColor;
            color.a = pulse;
            for (int i = 0; i < selected; i++)
            {
                int index = _selected[i];
                HaloInstance instance = GetInstance(i);
                if (instance == null || instance.go == null)
                {
                    continue;
                }

                instance.used = true;
                instance.go.SetActive(true);
                instance.go.transform.position = Positions[index];
                instance.go.transform.rotation = viewRotation * Quaternion.Euler(0f, 0f, spin);
                float diameter = Radii[index] * 2f;
                instance.go.transform.localScale = new Vector3(diameter, diameter, diameter);
                ApplyColor(instance.material, color);
                instance.renderer.enabled = true;
                _visible++;
            }

            RecycleUnused();
            EnsureGraphicHidden();
        }

        private static void DrawScreenSpace(Camera camera, int selected, float pulse, float spin)
        {
            if (_graphic == null)
            {
                Hide();
                return;
            }

            RectTransform graphicRect = _graphic.rectTransform;
            Rect canvasRect = graphicRect == null
                ? new Rect(0f, 0f, Screen.width, Screen.height)
                : graphicRect.rect;
            if (canvasRect.width <= 1f || canvasRect.height <= 1f)
            {
                canvasRect = new Rect(0f, 0f, Screen.width, Screen.height);
            }

            LogProjectionOnce(canvasRect);
            Sprites.Clear();
            float screenWidth = Screen.width;
            float screenHeight = Screen.height;
            for (int i = 0; i < selected; i++)
            {
                int index = _selected[i];
                Vector3 screenPoint = camera.WorldToScreenPoint(Positions[index]);
                if (screenPoint.z <= 0f)
                {
                    continue;
                }

                float screenX;
                float screenY;
                QualityMath.CameraPixelsToScreen(screenPoint.x, screenPoint.y, camera.pixelWidth,
                    camera.pixelHeight, (int)screenWidth, (int)screenHeight, out screenX, out screenY);
                Vector2 local;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(graphicRect,
                    new Vector2(screenX, screenY), null, out local);
                float pixelRadius = QualityMath.HaloPixelRadius(Radii[index], Distances[index],
                    screenHeight, camera.fieldOfView);
                if (pixelRadius <= 0.5f)
                {
                    continue;
                }

                var sprite = new HaloSprite();
                sprite.x = local.x;
                sprite.y = local.y;
                sprite.radius = pixelRadius;
                sprite.alpha = pulse;
                sprite.spin = spin;
                Sprites.Add(sprite);
            }

            _visible = Sprites.Count;
            _graphic.SetSprites(Sprites);
            EnsureGraphicVisible(_visible > 0);
            RecycleUnused();
        }

        private static void ApplyColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            if (material.HasProperty("_TintColor"))
            {
                material.SetColor("_TintColor", color);
            }
        }

        private static HaloInstance GetInstance(int slot)
        {
            while (Pool.Count <= slot)
            {
                if (Pool.Count >= MaxPoolSize)
                {
                    break;
                }

                HaloInstance created = CreateInstance();
                if (created == null)
                {
                    return null;
                }

                Pool.Add(created);
            }

            if (slot >= Pool.Count)
            {
                return null;
            }

            return Pool[slot];
        }

        private static HaloInstance CreateInstance()
        {
            if (_root == null || _quadMesh == null || _materialTemplate == null)
            {
                return null;
            }

            var go = new GameObject("HaloQuad");
            go.transform.SetParent(_root, false);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = _quadMesh;
            var renderer = go.AddComponent<MeshRenderer>();
            var material = new Material(_materialTemplate);
            material.name = "DSPQualitySystem.HaloInstance";
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            go.SetActive(false);

            var instance = new HaloInstance();
            instance.go = go;
            instance.renderer = renderer;
            instance.material = material;
            return instance;
        }

        private static void RecycleUnused()
        {
            for (int i = 0; i < Pool.Count; i++)
            {
                HaloInstance instance = Pool[i];
                if (instance == null || instance.go == null)
                {
                    continue;
                }

                if (!instance.used)
                {
                    if (instance.go.activeSelf)
                    {
                        instance.go.SetActive(false);
                    }
                }

                instance.used = false;
            }
        }

        private static void EnsureGraphicVisible(bool visible)
        {
            if (_canvas == null)
            {
                return;
            }

            if (_canvas.gameObject.activeSelf != visible)
            {
                _canvas.gameObject.SetActive(visible);
            }
        }

        private static void EnsureGraphicHidden()
        {
            if (_canvas != null && _canvas.gameObject.activeSelf)
            {
                _canvas.gameObject.SetActive(false);
            }
        }

        private static void Hide()
        {
            _marked = 0;
            _visible = 0;
            for (int i = 0; i < Pool.Count; i++)
            {
                HaloInstance instance = Pool[i];
                if (instance != null && instance.go != null && instance.go.activeSelf)
                {
                    instance.go.SetActive(false);
                }
            }

            EnsureGraphicHidden();
        }

        private static void EnsureBuffers(int count)
        {
            if (_distanceBuffer.Length < count)
            {
                _distanceBuffer = new float[count];
            }

            if (_selected.Length < count)
            {
                _selected = new int[count];
            }
        }

    }
}
