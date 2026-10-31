using System;

namespace DSPQualitySystem
{
    /// <summary>
    /// v0.11.9 (task-19): pure math for the golden halo above max-quality buildings. Kept free of
    /// UnityEngine types so ring profile, radius, pulse, bob, spin and the quad layout can be unit
    /// tested offline.
    /// </summary>
    public static partial class QualityMath
    {
        /// <summary>Halo quad size in world metres is radius * 2.</summary>
        public const float HaloInnerRatio = 0.55f;
        public const float HaloOuterRatio = 1.0f;

        /// <summary>Breathing alpha range and period for the halo.</summary>
        public const float HaloPulseMinAlpha = 0.60f;
        public const float HaloPulseMaxAlpha = 1.0f;
        public const float HaloPulsePeriod = 2.0f;

        /// <summary>Slow floating movement: +-0.12 m with a 2.5 s period.</summary>
        public const float HaloBobAmplitude = 0.12f;
        public const float HaloBobPeriod = 2.5f;

        /// <summary>Slow spin around the view axis (degrees per second).</summary>
        public const float HaloSpinDegreesPerSecond = 20f;

        /// <summary>Radius = clamp(max(selectSize.x, selectSize.z) * 0.42, 1.0, 3.2) * tenPointHaloSize.</summary>
        public const float HaloRadiusFactor = 0.42f;
        public const float HaloMinRadius = 1.0f;
        public const float HaloMaxRadius = 3.2f;

        // Ring profile in normalised texture radius (0 = centre, 1 = half texture edge). The ring
        // body sits between HaloRingInnerQ and HaloRingOuterQ; the glow fades out to HaloGlowEndQ.
        public const float HaloHoleQ = 0.55f;
        public const float HaloRingInnerQ = 0.58f;
        public const float HaloRingOuterQ = 0.82f;
        public const float HaloGlowEndQ = 1.0f;
        public const float HaloGlowStrength = 0.45f;

        /// <summary>Alpha of the generated ring texture at a normalised radial coordinate.</summary>
        public static float HaloAlphaAt(float q)
        {
            if (float.IsNaN(q) || q < 0f)
            {
                return 0f;
            }

            if (q < HaloHoleQ)
            {
                return 0f;
            }

            if (q < HaloRingInnerQ)
            {
                return (q - HaloHoleQ) / (HaloRingInnerQ - HaloHoleQ);
            }

            if (q <= HaloRingOuterQ)
            {
                return 1f;
            }

            if (q >= HaloGlowEndQ)
            {
                return 0f;
            }

            float glow = 1f - (q - HaloRingOuterQ) / (HaloGlowEndQ - HaloRingOuterQ);
            if (glow < 0f) glow = 0f;
            return glow * HaloGlowStrength;
        }

        /// <summary>World-space halo radius from the horizontal footprint of a building.</summary>
        public static float HaloRadius(float selectSizeX, float selectSizeZ, float sizeScale)
        {
            if (float.IsNaN(selectSizeX) || selectSizeX < 0f)
            {
                selectSizeX = 0f;
            }

            if (float.IsNaN(selectSizeZ) || selectSizeZ < 0f)
            {
                selectSizeZ = 0f;
            }

            float horizontal = selectSizeX > selectSizeZ ? selectSizeX : selectSizeZ;
            float radius = horizontal * HaloRadiusFactor;
            if (radius < HaloMinRadius) radius = HaloMinRadius;
            else if (radius > HaloMaxRadius) radius = HaloMaxRadius;

            if (float.IsNaN(sizeScale) || sizeScale <= 0f)
            {
                return radius;
            }

            return radius * sizeScale;
        }

        /// <summary>Breathing alpha in [0.6, 1.0] with a 2 s period; t is seconds.</summary>
        public static float HaloPulseAlpha(float timeSeconds)
        {
            if (float.IsNaN(timeSeconds) || float.IsInfinity(timeSeconds) || timeSeconds < 0f)
            {
                timeSeconds = 0f;
            }

            double phase = 2.0 * Math.PI * (timeSeconds / HaloPulsePeriod);
            float wave = (float)(0.5 - 0.5 * Math.Cos(phase));
            if (wave < 0f) wave = 0f;
            else if (wave > 1f) wave = 1f;
            return HaloPulseMinAlpha + (HaloPulseMaxAlpha - HaloPulseMinAlpha) * wave;
        }

        /// <summary>Vertical bob offset in metres, +-0.12 m with a 2.5 s period.</summary>
        public static float HaloBobOffset(float timeSeconds)
        {
            if (float.IsNaN(timeSeconds) || float.IsInfinity(timeSeconds) || timeSeconds < 0f)
            {
                timeSeconds = 0f;
            }

            return (float)Math.Sin(2.0 * Math.PI * (timeSeconds / HaloBobPeriod)) * HaloBobAmplitude;
        }

        /// <summary>Spin angle in degrees around the view axis; always positive and monotonic.</summary>
        public static float HaloSpinDegrees(float timeSeconds)
        {
            if (float.IsNaN(timeSeconds) || float.IsInfinity(timeSeconds) || timeSeconds < 0f)
            {
                timeSeconds = 0f;
            }

            float degrees = timeSeconds * HaloSpinDegreesPerSecond;
            return degrees % 360f;
        }

        /// <summary>
        /// Pixel radius used by the screen-space fallback: radius / distance scaled by the vertical
        /// field of view. Returns 0 for degenerate input.
        /// </summary>
        public static float HaloPixelRadius(float radius, float distance, float screenHeight, float fieldOfViewDegrees)
        {
            if (radius <= 0f || distance <= 0.01f || screenHeight <= 0f)
            {
                return 0f;
            }

            if (fieldOfViewDegrees < 1f || fieldOfViewDegrees > 179f)
            {
                fieldOfViewDegrees = 60f;
            }

            double halfHeight = Math.Tan(fieldOfViewDegrees * 0.5 * Math.PI / 180.0) * distance;
            if (halfHeight <= 1e-6)
            {
                return 0f;
            }

            float pixels = (float)(radius / halfHeight) * (screenHeight * 0.5f);
            if (float.IsNaN(pixels) || pixels < 0f)
            {
                return 0f;
            }

            return pixels;
        }

        /// <summary>
        /// Unit quad layout for the halo billboard: 4 vertices in the XY plane, matching UVs and two
        /// triangles. Kept pure so the offline tests can verify the mesh layout.
        /// </summary>
        public static void HaloQuadLayout(out float[] vertices, out float[] uvs, out int[] triangles)
        {
            vertices = new[]
            {
                -0.5f, -0.5f, 0f,
                0.5f, -0.5f, 0f,
                0.5f, 0.5f, 0f,
                -0.5f, 0.5f, 0f
            };
            uvs = new[]
            {
                0f, 0f,
                1f, 0f,
                1f, 1f,
                0f, 1f
            };
            triangles = new[] { 0, 1, 2, 2, 3, 0 };
        }

        /// <summary>Steady alpha used when the breathing pulse is disabled (v0.12.2).</summary>
        public const float HaloSteadyAlpha = 0.85f;

        /// <summary>Default golden marker colour as RRGGBB (v0.12.2): the original v0.11.10 gold FFD24A.</summary>
        public const string HaloDefaultColorHex = "FFD24A";

        /// <summary>
        /// Parses "RRGGBB" / "RRGGBBAA" / "#RRGGBB" hex colours (case-insensitive). Returns false and
        /// the default golden colour on any malformed input; never throws (v0.12.2).
        /// </summary>
        public static bool TryParseHexColor(string text, out float r, out float g, out float b, out float a)
        {
            r = 1f;
            g = 0.8235294f;
            b = 0.2901961f;
            a = 1f;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string s = text.Trim();
            if (s.Length > 0 && s[0] == '#')
            {
                s = s.Substring(1);
            }

            if (s.Length != 6 && s.Length != 8)
            {
                return false;
            }

            int packed = 0;
            for (int i = 0; i < s.Length; i++)
            {
                int digit = HexDigit(s[i]);
                if (digit < 0)
                {
                    return false;
                }

                packed = (packed << 4) | digit;
            }

            if (s.Length == 6)
            {
                r = ((packed >> 16) & 0xFF) / 255f;
                g = ((packed >> 8) & 0xFF) / 255f;
                b = (packed & 0xFF) / 255f;
                a = 1f;
            }
            else
            {
                r = ((packed >> 24) & 0xFF) / 255f;
                g = ((packed >> 16) & 0xFF) / 255f;
                b = ((packed >> 8) & 0xFF) / 255f;
                a = (packed & 0xFF) / 255f;
            }

            return true;
        }

        private static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return c - '0';
            }

            if (c >= 'a' && c <= 'f')
            {
                return c - 'a' + 10;
            }

            if (c >= 'A' && c <= 'F')
            {
                return c - 'A' + 10;
            }

            return -1;
        }

        /// <summary>
        /// Resolves the max-quality marker style selector (v0.15.0). The golden ring is the only
        /// marker left, so the rule is simply: "none" hides it, everything else (auto / halo /
        /// unknown / the removed "outline" and "both" values) draws it.
        /// </summary>
        public static void ResolveMarkerStyle(string style, out bool haloOn)
        {
            haloOn = true;
            if (string.IsNullOrEmpty(style))
            {
                return;
            }

            string s = style.Trim().ToLowerInvariant();
            if (s.Length == 0)
            {
                return;
            }

            if (s == "none" || s == "off" || s == "hidden")
            {
                haloOn = false;
            }
        }

        /// <summary>
        /// Per-frame budget for the marker overlay: candidates farther than this many metres are
        /// skipped (was QualityMath.OutlineMaxDistance before v0.15.0 deleted the outline).
        /// </summary>
        public const float HaloMaxDrawDistance = 120f;

        /// <summary>
        /// Picks up to maxCount candidate indices, nearest first. A negative distance means "not
        /// drawable"; distances above HaloMaxDrawDistance are ignored. outIndices is filled from 0.
        /// This helper lived in QualityMath.Outline.cs until v0.15.0; the golden ring uses it too.
        /// </summary>
        public static int SelectHaloTargets(float[] distances, int count, int maxCount, int[] outIndices)
        {
            if (outIndices == null || outIndices.Length == 0 || maxCount <= 0)
            {
                return 0;
            }

            int cap = maxCount;
            if (cap > outIndices.Length) cap = outIndices.Length;
            int n = count;
            if (distances == null) n = 0;
            if (n > distances.Length) n = distances.Length;
            if (n <= 0)
            {
                return 0;
            }

            int result = 0;
            for (int slot = 0; slot < cap; slot++)
            {
                int best = -1;
                float bestDistance = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    float d = distances[i];
                    if (float.IsNaN(d) || d < 0f || d > HaloMaxDrawDistance)
                    {
                        continue;
                    }

                    bool taken = false;
                    for (int k = 0; k < result; k++)
                    {
                        if (outIndices[k] == i)
                        {
                            taken = true;
                            break;
                        }
                    }

                    if (taken)
                    {
                        continue;
                    }

                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = i;
                    }
                }

                if (best < 0)
                {
                    break;
                }

                outIndices[result] = best;
                result++;
            }

            return result;
        }

        /// <summary>Spin angle in degrees scaled by a multiplier; 0 or invalid disables the spin (v0.12.2).</summary>
        public static float HaloSpinDegrees(float timeSeconds, float spinScale)
        {
            if (float.IsNaN(spinScale) || spinScale <= 0f)
            {
                return 0f;
            }

            return HaloSpinDegrees(timeSeconds) * spinScale;
        }

        /// <summary>Breathing alpha, or the steady alpha when the pulse is disabled (v0.12.2).</summary>
        public static float HaloPulseAlpha(float timeSeconds, bool pulseEnabled)
        {
            return pulseEnabled ? HaloPulseAlpha(timeSeconds) : HaloSteadyAlpha;
        }

        /// <summary>
        /// Converts a camera-pixel coordinate (Camera.WorldToScreenPoint space) into screen-pixel
        /// space. Both spaces are equal on a normal window; the scale factors only matter when the
        /// camera renders at a different pixel size than the screen backing.
        /// This helper lived in QualityMath.Outline.cs until v0.15.0; the golden-ring screen-space
        /// fallback still needs it.
        /// </summary>
        public static void CameraPixelsToScreen(float pixelsX, float pixelsY, int pixelWidth, int pixelHeight,
            int screenWidth, int screenHeight, out float screenX, out float screenY)
        {
            screenX = pixelsX;
            screenY = pixelsY;
            if (pixelWidth > 0 && screenWidth > 0 && pixelWidth != screenWidth)
            {
                screenX = pixelsX * screenWidth / (float)pixelWidth;
            }

            if (pixelHeight > 0 && screenHeight > 0 && pixelHeight != screenHeight)
            {
                screenY = pixelsY * screenHeight / (float)pixelHeight;
            }
        }
    }
}
