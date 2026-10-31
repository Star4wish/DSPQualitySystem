using System;

namespace DSPQualitySystem
{
    /// <summary>
    /// v0.12.0 (task-16): pure helpers for the five "A class" quality bonuses. Every bonus reuses the
    /// shared curve table of <see cref="QualityMath.SpeedBonusFrom"/> (0/2.5/5/7.5/10/15/20/30/40/60/
    /// 100 %, 10 points = 2.00x); only the sprayer turns the same table into a bonus-point chance.
    /// No game references here so the offline tests exercise the exact same math.
    /// </summary>
    public static partial class QualityMath
    {
        /// <summary>
        /// Factor for a quality-driven extra bonus. Disabled or 0 points returns exactly 1f so the
        /// patched code path is a no-op and the vanilla value is preserved bit for bit.
        /// </summary>
        public static float FeatureFactorFrom(int points, bool enabled, bool curve, float percentPerPoint,
            float maxBonusPercent)
        {
            if (!enabled || points <= 0)
            {
                return 1f;
            }
            return SpeedFactorFor(points, curve, percentPerPoint, maxBonusPercent);
        }

        /// <summary>
        /// v0.12.0 sprayer: percent chance (0..100) for one quality point bonus, using the shared curve
        /// table. 0 points = 0 %, 10 points = 100 % (guaranteed +1 point per spray application).
        /// </summary>
        public static float SprayChancePercent(int points)
        {
            if (points <= 0)
            {
                return 0f;
            }
            double bonus = SpeedBonusFrom(points);
            if (bonus < 0.0)
            {
                return 0f;
            }
            if (bonus > 100.0)
            {
                return 100f;
            }
            return (float)bonus;
        }

        /// <summary>
        /// v0.12.0 sprayer: one spray application outcome. When the chance roll hits, the sprayed item
        /// gets one extra quality point (never above maxInc). The original sprayed value is returned
        /// unchanged for 0 points, a failed roll or an already maxed item; the input is never lowered.
        /// </summary>
        public static int SprayBonus(int sprayedInc, int points, Func<float, bool> rollPercent, int maxInc)
        {
            if (sprayedInc <= 0 || maxInc > 0 && sprayedInc >= maxInc)
            {
                return sprayedInc;
            }
            float chance = SprayChancePercent(points);
            if (chance <= 0f || rollPercent == null)
            {
                return sprayedInc;
            }
            if (!rollPercent(chance))
            {
                return sprayedInc;
            }
            return sprayedInc + 1;
        }
    }
}
