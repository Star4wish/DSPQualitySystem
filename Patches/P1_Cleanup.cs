using System;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// M2 spec 3.10: clear per-entity quality state when a building is dismantled so a recycled
    /// entityId never inherits the old buildingInc / pool / inputLevel (spec 2.1). The vanilla
    /// signature is DismantleFinally(Player player, int objId, ref int protoId): objId > 0 is a
    /// real entity, objId < 0 removes prebuild -objId (asm_full.cs:212540-212581).
    /// </summary>
    [HarmonyPatch(typeof(PlanetFactory), "DismantleFinally")]
    internal static class DismantleFinally_Cleanup
    {
        private static void Postfix(PlanetFactory __instance, int objId)
        {
            try
            {
                if (objId > 0)
                {
                    QualityData.ClearEntity(__instance.planetId, objId);
                }
                else if (objId < 0)
                {
                    QualityData.ClearPrebuild(-objId);
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("cleanup.dismantle", e);
            }
        }
    }
}
