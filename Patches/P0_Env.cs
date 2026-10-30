using System;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// M1: keep QualityEnv.CurrentPlanetId in sync with the factory that is ticking.
    /// AssemblerComponent has no factory/planet reference, so its patches rely on this scope.
    /// </summary>
    [HarmonyPatch(typeof(FactorySystem), "GameTick", new[] { typeof(long), typeof(bool) })]
    internal static class FactorySystem_GameTick_Scope
    {
        private static void Prefix(FactorySystem __instance)
        {
            try
            {
                QualityEnv.Enter(__instance);
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("env.enter", e);
            }
        }

        private static void Postfix()
        {
            QualityEnv.Exit();
        }
    }
}
