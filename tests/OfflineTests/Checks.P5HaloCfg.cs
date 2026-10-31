using System;

namespace DSPQualitySystem.OfflineTests
{
    /// <summary>
    /// v0.12.2: offline checks for the halo/marker configuration helpers. These are the pure parts of
    /// the "let players choose their max-quality marker" feature: hex colour parsing and the
    /// tenPointMarkerStyle resolver. The config bindings themselves live in QualityConfig.cs, which
    /// needs BepInEx/Unity and is therefore not linked into this harness.
    /// </summary>
    internal static partial class Program
    {
        private static void CheckHaloConfig()
        {
            // ---- hex colour parsing ------------------------------------------------
            float r;
            float g;
            float b;
            float a;

            Assert(QualityMath.TryParseHexColor("FFC83C", out r, out g, out b, out a), "gold hex must parse");
            AssertNear(1f, r, 1e-4f, "FFC83C red channel");
            AssertNear(200f / 255f, g, 1e-4f, "FFC83C green channel");
            AssertNear(60f / 255f, b, 1e-4f, "FFC83C blue channel");
            AssertNear(1f, a, 1e-4f, "6-digit hex is fully opaque");

            Assert(QualityMath.TryParseHexColor("#7fd4ff", out r, out g, out b, out a), "lowercase #hex must parse");
            AssertNear(127f / 255f, r, 1e-4f, "#7fd4ff red channel");
            AssertNear(212f / 255f, g, 1e-4f, "#7fd4ff green channel");
            AssertNear(1f, b, 1e-4f, "#7fd4ff blue channel");

            Assert(QualityMath.TryParseHexColor("00FF0080", out r, out g, out b, out a), "8-digit hex must parse");
            AssertNear(0f, r, 1e-4f, "8-digit hex red channel");
            AssertNear(1f, g, 1e-4f, "8-digit hex green channel");
            AssertNear(128f / 255f, a, 1e-4f, "8-digit hex alpha channel");

            // Malformed values are rejected and leave the default gold colour behind.
            Assert(!QualityMath.TryParseHexColor(null, out r, out g, out b, out a), "null hex is rejected");
            AssertNear(1f, r, 1e-4f, "null falls back to gold red");
            AssertNear(210f / 255f, g, 1e-4f, "null falls back to gold green");
            AssertNear(74f / 255f, b, 1e-4f, "null falls back to gold blue");
            Assert(!QualityMath.TryParseHexColor("", out r, out g, out b, out a), "empty hex is rejected");
            Assert(!QualityMath.TryParseHexColor("FFF", out r, out g, out b, out a), "3-digit hex is rejected");
            Assert(!QualityMath.TryParseHexColor("FFC83", out r, out g, out b, out a), "5-digit hex is rejected");
            Assert(!QualityMath.TryParseHexColor("GGGGGG", out r, out g, out b, out a), "non-hex letters are rejected");
            Assert(!QualityMath.TryParseHexColor("FF C83C", out r, out g, out b, out a), "spaces inside are rejected");
            AssertNear(1f, r, 1e-4f, "rejected value keeps gold red");
            Assert(QualityMath.TryParseHexColor("  AAAABB  ", out r, out g, out b, out a), "surrounding spaces are trimmed");
            AssertNear(170f / 255f, r, 1e-4f, "trimmed hex red channel");

            // ---- marker style resolver (v0.15.0: the ring is the only marker) ------
            bool halo;

            // auto / halo / unknown / the removed "outline" + "both" values all draw the ring.
            QualityMath.ResolveMarkerStyle("auto", out halo);
            Assert(halo, "auto draws the ring");
            QualityMath.ResolveMarkerStyle(null, out halo);
            Assert(halo, "null style behaves like auto");
            QualityMath.ResolveMarkerStyle("", out halo);
            Assert(halo, "empty style behaves like auto");
            QualityMath.ResolveMarkerStyle("nonsense", out halo);
            Assert(halo, "unknown style behaves like auto");
            QualityMath.ResolveMarkerStyle("halo", out halo);
            Assert(halo, "style=halo draws the ring");
            QualityMath.ResolveMarkerStyle(" Halo ", out halo);
            Assert(halo, "style is trimmed and case-insensitive");
            QualityMath.ResolveMarkerStyle("outline", out halo);
            Assert(halo, "the removed style=outline falls back to the ring");
            QualityMath.ResolveMarkerStyle("both", out halo);
            Assert(halo, "the removed style=both falls back to the ring");

            // Only "none" (and its aliases) hides the marker.
            QualityMath.ResolveMarkerStyle("none", out halo);
            Assert(!halo, "style=none hides the marker");
            QualityMath.ResolveMarkerStyle(" NONE ", out halo);
            Assert(!halo, "style=none is trimmed and case-insensitive");
            QualityMath.ResolveMarkerStyle("off", out halo);
            Assert(!halo, "style=off hides the marker");
            QualityMath.ResolveMarkerStyle("hidden", out halo);
            Assert(!halo, "style=hidden hides the marker");

            // ---- pulse / spin helpers --------------------------------------------
            AssertNear(QualityMath.HaloPulseAlpha(0.6f), QualityMath.HaloPulseAlpha(0.6f, true), 1e-6f,
                "pulse enabled keeps the breathing curve");
            AssertNear(QualityMath.HaloSteadyAlpha, QualityMath.HaloPulseAlpha(0.6f, false), 1e-6f,
                "pulse disabled uses the steady alpha");
            AssertNear(0f, QualityMath.HaloSpinDegrees(3f, 0f), 1e-6f, "spin multiplier 0 disables rotation");
            AssertNear(QualityMath.HaloSpinDegrees(3f), QualityMath.HaloSpinDegrees(3f, 1f), 1e-6f,
                "spin multiplier 1 keeps the base speed");
            AssertNear(QualityMath.HaloSpinDegrees(3f) * 2f, QualityMath.HaloSpinDegrees(3f, 2f), 1e-4f,
                "spin multiplier 2 doubles the base speed");
            AssertNear(0f, QualityMath.HaloSpinDegrees(3f, float.NaN), 1e-6f, "NaN spin multiplier disables rotation");
        }
    }
}
