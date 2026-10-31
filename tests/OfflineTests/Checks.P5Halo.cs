using System;

namespace DSPQualitySystem.OfflineTests
{
    /// <summary>
    /// v0.11.9 (task-19): offline checks for the golden halo above max-quality buildings. Only the
    /// pure math (ring profile, radius, animation curves, screen fallback radius, quad layout) is
    /// exercised here; textures/renderers need the game.
    /// </summary>
    internal static partial class Program
    {
        private static void CheckHaloMath()
        {
            // Ring texture profile: transparent centre, bright ring body, soft glow, transparent edge.
            AssertNear(0f, QualityMath.HaloAlphaAt(0f), 1e-6f, "halo alpha at centre");
            AssertNear(0f, QualityMath.HaloAlphaAt(0.4f), 1e-6f, "halo alpha inside the hole");
            float ramp = QualityMath.HaloAlphaAt(0.565f);
            Assert(ramp > 0f && ramp < 1f, "halo alpha must ramp at the inner edge: " + ramp);
            AssertNear(1f, QualityMath.HaloAlphaAt(0.7f), 1e-4f, "halo alpha on the ring body");
            float glow = QualityMath.HaloAlphaAt(0.9f);
            Assert(glow > 0f && glow < QualityMath.HaloGlowStrength, "halo glow must be subtle: " + glow);
            AssertNear(0f, QualityMath.HaloAlphaAt(1f), 1e-6f, "halo alpha at the outer edge");
            AssertNear(0f, QualityMath.HaloAlphaAt(1.2f), 1e-6f, "halo alpha outside the ring");

            // Radius: clamp(max(size.x, size.z) * 0.42, 1.0, 3.2) * sizeScale.
            AssertNear(1.0f, QualityMath.HaloRadius(0f, 0f, 1f), 1e-4f, "tiny building uses min radius");
            AssertNear(2.1f, QualityMath.HaloRadius(5f, 4f, 1f), 1e-4f, "horizontal footprint drives radius");
            AssertNear(3.2f, QualityMath.HaloRadius(50f, 50f, 1f), 1e-4f, "huge building uses max radius");
            AssertNear(4.2f, QualityMath.HaloRadius(5f, 4f, 2f), 1e-4f, "tenPointHaloSize scales radius");
            AssertNear(1.0f, QualityMath.HaloRadius(float.NaN, float.NaN, 1f), 1e-4f, "NaN size falls back to min");
            AssertNear(2.1f, QualityMath.HaloRadius(5f, 4f, 0f), 1e-4f, "invalid size scale is ignored");

            // Pulse: 0.6 .. 1.0 with a 2 s period.
            AssertNear(QualityMath.HaloPulseMinAlpha, QualityMath.HaloPulseAlpha(0f), 1e-4f, "halo pulse at t=0");
            AssertNear(QualityMath.HaloPulseMaxAlpha, QualityMath.HaloPulseAlpha(1f), 1e-4f, "halo pulse at half period");
            AssertNear(QualityMath.HaloPulseMinAlpha, QualityMath.HaloPulseAlpha(2f), 1e-4f, "halo pulse at full period");
            AssertNear(QualityMath.HaloPulseMinAlpha, QualityMath.HaloPulseAlpha(-3f), 1e-4f, "halo pulse negative time");

            // Bob: +-0.12 m with a 2.5 s period (peak at a quarter period).
            AssertNear(0f, QualityMath.HaloBobOffset(0f), 1e-5f, "halo bob at t=0");
            AssertNear(QualityMath.HaloBobAmplitude, QualityMath.HaloBobOffset(0.625f), 1e-4f, "halo bob peak");
            AssertNear(0f, QualityMath.HaloBobOffset(1.25f), 1e-5f, "halo bob at half period");
            AssertNear(0f, QualityMath.HaloBobOffset(2.5f), 1e-5f, "halo bob full period");
            AssertNear(-QualityMath.HaloBobAmplitude, QualityMath.HaloBobOffset(1.875f), 1e-4f, "halo bob trough");
            AssertNear(0f, QualityMath.HaloBobOffset(3.75f), 1e-5f, "halo bob at 1.5 periods");

            // Spin: 20 degrees per second, wrapped to [0, 360).
            AssertNear(0f, QualityMath.HaloSpinDegrees(0f), 1e-4f, "halo spin at t=0");
            AssertNear(20f, QualityMath.HaloSpinDegrees(1f), 1e-4f, "halo spin after one second");
            AssertNear(20f, QualityMath.HaloSpinDegrees(19f), 1e-4f, "halo spin wraps modulo 360");
            Assert(QualityMath.HaloSpinDegrees(0.5f) > 0f, "halo spin is positive");

            // Screen-space fallback radius uses the vertical FOV: r / (tan(fov/2)*distance) * screenH/2.
            float pixels = QualityMath.HaloPixelRadius(1f, 10f, 1080f, 60f);
            AssertNear(93.5307f, pixels, 0.01f, "halo pixel radius for 1 m at 10 m / 60 deg / 1080p");
            AssertNear(0f, QualityMath.HaloPixelRadius(0f, 10f, 1080f, 60f), 1e-6f, "zero radius -> no sprite");
            AssertNear(0f, QualityMath.HaloPixelRadius(1f, 0f, 1080f, 60f), 1e-6f, "zero distance -> no sprite");
            AssertNear(0f, QualityMath.HaloPixelRadius(1f, 10f, 0f, 60f), 1e-6f, "zero screen height -> no sprite");
            Assert(QualityMath.HaloPixelRadius(1f, 10f, 1080f, 0f) > 0f, "invalid fov falls back to 60 degrees");

            // Quad layout: 4 vertices / 2 triangles / matching UVs.
            float[] vertices;
            float[] uvs;
            int[] triangles;
            QualityMath.HaloQuadLayout(out vertices, out uvs, out triangles);
            Assert(vertices != null && vertices.Length == 12, "halo quad needs 4 vertices (3 floats each)");
            Assert(uvs != null && uvs.Length == 8, "halo quad needs 4 UVs (2 floats each)");
            Assert(triangles != null && triangles.Length == 6, "halo quad needs 2 triangles (6 indices)");
            AssertNear(-0.5f, vertices[0], 1e-6f, "halo quad vertex 0 x");
            AssertNear(-0.5f, vertices[1], 1e-6f, "halo quad vertex 0 y");
            AssertNear(0.5f, vertices[3], 1e-6f, "halo quad vertex 1 x");
            AssertNear(0f, uvs[0], 1e-6f, "halo quad uv 0 x");
            AssertNear(1f, uvs[2], 1e-6f, "halo quad uv 1 x");
            Assert(triangles[0] == 0 && triangles[1] == 1 && triangles[2] == 2, "halo quad first triangle");
            Assert(triangles[3] == 2 && triangles[4] == 3 && triangles[5] == 0, "halo quad second triangle");
        }
    }
}