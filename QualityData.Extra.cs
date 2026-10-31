namespace DSPQualitySystem
{
    /// <summary>
    /// v0.12.0 (task-16): data-layer helpers for the five "A class" bonuses. Each helper resolves the
    /// building's quality points exactly like the P3 speed bonus (QualityData.GetBuildingInc) and then
    /// applies the shared curve. A missing planet, a disabled feature or 0 points returns the neutral
    /// value (1f factor / the original inc), so callers can be thin.
    ///
    /// Split from QualityData.cs to keep every file under the project convention of 500 lines; the
    /// storage class itself is partial.
    /// </summary>
    public static partial class QualityData
    {
        /// <summary>v0.12.0 research-lab research speed factor (1f = vanilla).</summary>
        public static float LabSpeedFactor(int planetId, int entityId)
        {
            return FeatureFactor(planetId, entityId,
                QualityConfig.LabSpeedBonus != null && QualityConfig.LabSpeedBonus.Value);
        }

        /// <summary>v0.12.0 turret fire-rate factor (1f = vanilla).</summary>
        public static float TurretFireFactor(int planetId, int entityId)
        {
            return FeatureFactor(planetId, entityId,
                QualityConfig.TurretFireRateBonus != null && QualityConfig.TurretFireRateBonus.Value);
        }

        /// <summary>v0.12.0 power-generator output factor (1f = vanilla).</summary>
        public static float PowerGenFactor(int planetId, int entityId)
        {
            return FeatureFactor(planetId, entityId,
                QualityConfig.PowerGenBonus != null && QualityConfig.PowerGenBonus.Value);
        }

        /// <summary>v0.12.0 ejector / silo launch-rate factor (1f = vanilla).</summary>
        public static float LauncherSpeedFactor(int planetId, int entityId)
        {
            return FeatureFactor(planetId, entityId,
                QualityConfig.LauncherSpeedBonus != null && QualityConfig.LauncherSpeedBonus.Value);
        }

        /// <summary>
        /// v0.18.0 (issue 1b): factor applied to a power node's connect distance and cover radius
        /// (tesla tower / wireless power tower / substation). Same shared curve as every other bonus,
        /// so 0 points = 1.00x and 10 points = 2.00x. 1f means "leave the vanilla prefab numbers".
        /// </summary>
        public static float PowerRangeFactor(int planetId, int entityId)
        {
            return FeatureFactor(planetId, entityId,
                QualityConfig.PowerRangeBonus != null && QualityConfig.PowerRangeBonus.Value);
        }

        private static float FeatureFactor(int planetId, int entityId, bool enabled)
        {
            try
            {
                if (!enabled || QualityConfig.Enabled == null || !QualityConfig.Enabled.Value)
                {
                    return 1f;
                }
                if (planetId < 0 || entityId < 0)
                {
                    return 1f;
                }
                int points = GetBuildingInc(planetId, entityId);
                return QualityMath.FeatureFactorFrom(points, enabled, QualityConfig.SpeedBonusIsCurve,
                    QualityConfig.SpeedPerPointClamped, QualityConfig.MaxSpeedBonusClamped);
            }
            catch (System.Exception)
            {
                return 1f;
            }
        }

        /// <summary>
        /// v0.12.0 sprayer: new inc for the sprayed item. The vanilla writer already computed
        /// sprayedInc = num5 * incAbility (asm_full.cs:73648); this adds one point with the curve
        /// chance and clamps to Cargo.kIncLevelMax. Never lowers the vanilla value; on any problem the
        /// original value is returned so the spray behaves exactly like vanilla.
        /// </summary>
        public static int SprayBonusPoints(int sprayedInc, int entityId)
        {
            try
            {
                if (sprayedInc <= 0 || entityId < 0)
                {
                    return sprayedInc;
                }
                if (QualityConfig.Enabled == null || !QualityConfig.Enabled.Value
                    || QualityConfig.SprayerIncBonus == null || !QualityConfig.SprayerIncBonus.Value)
                {
                    return sprayedInc;
                }
                int planetId = QualityEnv.CurrentPlanetId;
                if (planetId < 0)
                {
                    return sprayedInc;
                }
                int points = GetBuildingInc(planetId, entityId);
                int after = QualityMath.SprayBonus(sprayedInc, points, QualityRules.RollPercent,
                    Cargo.kIncLevelMax);
                // Diagnostics only: lets Describe() prove in-game that the roll site is live.
                Patches.P5_Sprayer.NoteSpray(sprayedInc, after);
                return after;
            }
            catch (System.Exception)
            {
                return sprayedInc;
            }
        }
    }
}
