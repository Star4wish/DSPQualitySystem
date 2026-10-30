using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DSPQualitySystem.Patches
{
    internal static partial class P5_TenPointHalo
    {
        private static Texture2D BuildRingTexture()
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            texture.name = "DSPQualitySystem.HaloRing";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            Color gold = HaloColor;
            var pixels = new Color[TextureSize * TextureSize];
            float half = TextureSize * 0.5f;
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float q = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = QualityMath.HaloAlphaAt(q);
                    var color = new Color(gold.r, gold.g, gold.b, alpha);
                    pixels[y * TextureSize + x] = color;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static Mesh BuildQuadMesh()
        {
            var mesh = new Mesh();
            mesh.name = "DSPQualitySystem.HaloQuad";
            float[] flatVertices;
            float[] flatUvs;
            int[] triangles;
            QualityMath.HaloQuadLayout(out flatVertices, out flatUvs, out triangles);
            var vertices = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                vertices[i] = new Vector3(flatVertices[i * 3], flatVertices[i * 3 + 1], flatVertices[i * 3 + 2]);
            }

            var uvs = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                uvs[i] = new Vector2(flatUvs[i * 2], flatUvs[i * 2 + 1]);
            }

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            return mesh;
        }
    }

    /// <summary>
    /// v0.11.9 fallback drawing: textured quads for the generated ring texture when no 3D shader is
    /// available. Only used if all shader lookups fail, so normal play uses the 3D billboard path.
    /// </summary>
    internal sealed class HaloSpriteGraphic : MaskableGraphic
    {
        private static readonly List<P5_TenPointHalo.HaloSprite> Pending =
            new List<P5_TenPointHalo.HaloSprite>(64);

        public void SetSprites(List<P5_TenPointHalo.HaloSprite> sprites)
        {
            Pending.Clear();
            if (sprites != null && sprites.Count > 0)
            {
                Pending.AddRange(sprites);
            }

            SetVerticesDirty();
        }

        public override Texture mainTexture
        {
            get { return P5_TenPointHalo.RingTexture; }
        }

        public override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Texture texture = P5_TenPointHalo.RingTexture;
            if (texture == null)
            {
                return;
            }

            Color gold = P5_TenPointHalo.HaloColor;
            for (int i = 0; i < Pending.Count; i++)
            {
                P5_TenPointHalo.HaloSprite sprite = Pending[i];
                float radians = sprite.spin * Mathf.Deg2Rad;
                float cos = Mathf.Cos(radians);
                float sin = Mathf.Sin(radians);
                var color = new Color(gold.r, gold.g, gold.b, sprite.alpha);
                float r = sprite.radius;
                Vector2[] offsets =
                {
                    new Vector2(-r, -r),
                    new Vector2(r, -r),
                    new Vector2(r, r),
                    new Vector2(-r, r)
                };
                Vector2[] uvs =
                {
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, 1f)
                };
                int start = vh.currentVertCount;
                for (int v = 0; v < 4; v++)
                {
                    float rx = offsets[v].x * cos - offsets[v].y * sin;
                    float ry = offsets[v].x * sin + offsets[v].y * cos;
                    vh.AddVert(new Vector3(sprite.x + rx, sprite.y + ry, 0f), color, uvs[v]);
                }

                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start + 2, start + 3, start);
            }
        }
    }
}