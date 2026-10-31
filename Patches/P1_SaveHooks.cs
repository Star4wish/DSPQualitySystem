using System;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// Quality persistence hooks.
    /// Since v0.11.11 the quality data is embedded in the player's own .dsv (a trailer appended at
    /// the end of GameData.Export and read back at the end of GameData.Import), so the mod no longer
    /// writes any save file of its own. That keeps one save = one file: deleting, copying, backing up
    /// or rolling back a .dsv carries the quality data along with it, and the vanilla loader - which
    /// stops reading after the vanilla data - ignores the trailer when the mod is uninstalled.
    ///   GameData.Export(BinaryWriter)     postfix -> append [len][json][magic] to the save stream
    ///   GameData.Import(BinaryReader)     postfix -> read that trailer back into memory
    ///   GameSave.SaveCurrentGame(string)  postfix -> delete the legacy sidecar, if one is left
    ///   GameSave.LoadCurrentGame(string)  postfix -> drop the sidecar, or read it once (old save)
    ///   GameData.NewGame(GameDesc)        postfix -> drop data from a previous game
    /// The Export/Import postfixes must stay attribute-only (no TargetMethod) and must never throw:
    /// an exception there would make the vanilla save/load fail.
    /// </summary>
    [HarmonyPatch(typeof(GameData), "Export", new[] { typeof(System.IO.BinaryWriter) })]
    internal static class GameData_Export_EmbedQuality
    {
        private static void Postfix(GameData __instance, System.IO.BinaryWriter __0)
        {
            try
            {
                Save.QualitySave.SaveEmbedded(__instance, __0);
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("save.write", e);
            }
        }
    }

    [HarmonyPatch(typeof(GameData), "Import", new[] { typeof(System.IO.BinaryReader) })]
    internal static class GameData_Import_ReadQuality
    {
        private static void Postfix(GameData __instance, System.IO.BinaryReader __0)
        {
            try
            {
                // v0.16.1: the container/splitter/sorter runtime bookkeeping describes the previous
                // game world, so drop it before the trailer restores the new one.
                StorageGrids.ResetRuntime();
                Patches.P5_SplitterFilter.ResetState();
                Patches.P5_SorterFilter.ResetState();
                // v0.18.0: a re-loaded save must re-run the one-shot per-planet power-node sweep
                // (the nodePool fields are persistent, but the quality data only now came back).
                Patches.P5_PowerRange.ResetState();
                Patches.P1_DeferredPay.ResetState();
                Save.QualitySave.ReadEmbedded(__instance, __0);
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("save.read", e);
            }
        }
    }

    [HarmonyPatch(typeof(GameSave), "SaveCurrentGame", new[] { typeof(string) })]
    internal static class GameSave_SaveCurrentGame_SaveQuality
    {
        private static void Postfix(string saveName, bool __result)
        {
            if (!__result)
            {
                return;
            }
            try
            {
                // The trailer went into the .dsv during Export, so the .dsv is now self-contained and
                // the pre-v0.11.11 sidecar (if any) is stale. Only touch it when the export really
                // wrote a trailer, otherwise deleting it would lose the only copy of the data.
                if (Save.QualitySave.LastEmbeddedWriteOk)
                {
                    Save.QualitySave.DeleteLegacy(saveName);
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("save.write", e);
            }
        }
    }

    [HarmonyPatch(typeof(GameSave), "LoadCurrentGame", new[] { typeof(string) })]
    internal static class GameSave_LoadCurrentGame_LoadQuality
    {
        private static void Postfix(string saveName, bool __result)
        {
            if (!__result)
            {
                return;
            }
            try
            {
                if (Save.QualitySave.EmbeddedLoaded)
                {
                    // The .dsv carried its own quality data; DeleteLegacy here catches sidecars that
                    // were left behind by an older build (or a save that was copied before one).
                    Save.QualitySave.DeleteLegacy(saveName);
                    return;
                }

                // No trailer: an old save. Read the sidecar once so the player keeps the points, but
                // do not write it back and do not delete it - this .dsv has never been saved with the
                // trailer, so the JSON is still the only copy. The next save embeds it and removes it.
                Save.QualitySave.LoadLegacy(saveName);
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("save.read", e);
            }
        }
    }

    [HarmonyPatch(typeof(GameData), "NewGame", new[] { typeof(GameDesc) })]
    internal static class GameData_NewGame_ClearQuality
    {
        private static void Postfix(bool __result)
        {
            if (!__result)
            {
                return;
            }
            try
            {
                QualityData.ClearAll();
                StorageGrids.ResetRuntime();
                Patches.P5_SplitterFilter.ResetState();
                Patches.P5_SorterFilter.ResetState();
                Patches.P5_PowerRange.ResetState();
                Patches.P1_DeferredPay.ResetState();
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogInfo("[QSystem][save] new game: quality data cleared");
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("save.newgame", e);
            }
        }
    }
}
