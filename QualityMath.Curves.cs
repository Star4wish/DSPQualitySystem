using System;

namespace DSPQualitySystem
{
    /// <summary>
    /// v0.11.2 (task-13 A): the shared per-point bonus curve. Split out of QualityMath.cs so the
    /// main math file stays under the project file-size convention.
    ///
    /// Point  0    1    2     3     4     5     6     7     8     9     10
    /// Bonus  0  2.5  5.0   7.5  10.0  15.0  20.0  30.0  40.0  60.0  100.0  (percent)
    /// So 10 points = x2.00 speed / charge cap, and the shape starts nearly flat, climbs in the
    /// middle and pays off hard at the top (matches the user's requested curve).
    /// </summary>
    public static partial class QualityMath
    {
        public const int CurveMaxPoints = 10;

        private static readonly double[] CurveBonusPercent =
        {
            0.0, 2.5, 5.0, 7.5, 10.0, 15.0, 20.0, 30.0, 40.0, 60.0, 100.0
        };

        /// <summary>v0.11.2: cumulative bonus percent for a building level under the default curve.</summary>
        public static double SpeedBonusFrom(int points)
        {
            if (points <= 0)
            {
                return 0.0;
            }
            if (points >= CurveMaxPoints)
            {
                return CurveBonusPercent[CurveMaxPoints];
            }
            return CurveBonusPercent[points];
        }

        /// <summary>
        /// v0.11.2: factor used by speed stage 1 and charge stage 2a. curve=true uses the shared
        /// per-point table above; curve=false keeps the legacy linear formula for A/B comparison.
        /// Returns exactly 1f when no bonus applies.
        /// </summary>
        public static float SpeedFactorFor(int buildingInc, bool curve, float percentPerPoint,
            float maxBonusPercent)
        {
            if (curve)
            {
                double bonus = SpeedBonusFrom(buildingInc);
                return bonus <= 0.0 ? 1f : (float)(1.0 + bonus / 100.0);
            }
            return SpeedFactor(buildingInc, percentPerPoint, maxBonusPercent);
        }
    }
}