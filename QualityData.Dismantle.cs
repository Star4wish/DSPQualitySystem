namespace DSPQualitySystem
{
    /// <summary>
    /// P3 stage 2d: build quality refund on dismantle (DEVLOG section 19). The vanilla
    /// PlayerAction_Build.DoDismantleObject path hands the returned item to the player with a
    /// hard-coded inc argument of 0 (asm_full.cs:163928); the transpiler replaces that argument
    /// with this lookup, so the building's quality points survive the round trip. Split from
    /// QualityData.cs to keep every file under the project convention of 500 lines; the storage
    /// class itself is partial.
    /// </summary>
    public static partial class QualityData
    {
        private static int _dismantleDebugLogged;

        /// <summary>
        /// Points to stamp on the item returned by dismantling the given building. Reads the
        /// stored building points only: no roll, no consumption, no state reset (the entity is
        /// cleaned later by the P1_Cleanup DismantleFinally postfix, which runs after this call).
        /// Returns 0 (vanilla) when disabled, before the config binds, or on any failure.
        /// </summary>
        public static int DismantleInc(PlanetFactory factory, int entityId)
        {
            try
            {
                if (!QualityConfig.Enabled.Value || QualityConfig.GiveDismantleInc == null
                    || !QualityConfig.GiveDismantleInc.Value)
                {
                    return 0;
                }
                if (factory == null || factory.planet == null || entityId <= 0)
                {
                    return 0;
                }
                int planetId = factory.planet.id;
                if (planetId < 0)
                {
                    return 0;
                }
                byte points = GetBuildingInc(planetId, entityId);
                int inc = QualityMath.DismantleIncFrom(points, Cargo.kIncLevelMax, true);
                if (inc > 0 && QualityConfig.DebugProbes.Value && _dismantleDebugLogged < 20)
                {
                    _dismantleDebugLogged++;
                    if (Plugin.Log != null)
                    {
                        Plugin.Log.LogInfo("[QSystem][dismantle] inc returned eid=" + entityId
                            + " points=" + points + " inc=" + inc);
                    }
                }
                return inc;
            }
            catch (System.Exception e)
            {
                QualityEnv.WarnOnce("dismantle.inc", e);
                return 0;
            }
        }
    }
}
