using System;
using System.Reflection;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.13.0 (task-17 / C-1): registers the storage "quality grids" save shave.
    ///
    /// The only patch this feature needs is a Guard around <c>GameData.Export(BinaryWriter)</c>:
    /// the extra grids granted by quality points live in memory (<c>StorageComponent.size</c> and
    /// <c>grids</c> are grown with <c>SetSize</c>), but the vanilla save format writes the
    /// container size twice (once by <c>FactoryStorage.Export</c>, once by
    /// <c>StorageComponent.Export</c>). The prefix temporarily puts every expanded container back
    /// to its vanilla grid count so the .dsv keeps the vanilla container shape, and the
    /// postfix/finalizer restore the runtime shape afterwards.
    ///
    /// A container that still held one of its grids between the vanilla count and the expanded
    /// count is read back after removing the MOD as a vanilla container and the extra items are
    /// simply gone (they are stored in the MOD trailer only); this is the documented price of the
    /// "save stays readable without the MOD" hard requirement.
    /// </summary>
    internal static class P4_StorageGrids
    {
        private const string Tag = "[QSystem][storage] ";

        private static bool _armed;
        private static bool _warned;
        private static string _reason = "not applied";

        internal static bool Armed
        {
            get { return _armed; }
        }

        internal static string Reason
        {
            get { return _reason == null ? "not applied" : _reason; }
        }

        internal static void Apply(Harmony harmony)
        {
            try
            {
                if (harmony == null)
                {
                    _reason = "no harmony instance";
                    return;
                }

                if (!QualityConfig.StorageGridsEnabled)
                {
                    _reason = "storageQualityGrids=false";
                    LogInfo(Tag + "NOT ARMED (" + _reason + "; vanilla containers)");
                    return;
                }

                if (QualityConfig.StorageGridsPerPointClamped <= 0f
                    || QualityConfig.StorageGridsMaxClamped <= 0)
                {
                    _reason = "storageQualityGridsPerPoint or storageQualityGridsMax is 0";
                    LogInfo(Tag + "NOT ARMED (" + _reason + "; vanilla containers)");
                    return;
                }

                MethodInfo export = typeof(GameData).GetMethod(
                    "Export",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new[] { typeof(System.IO.BinaryWriter) },
                    null);
                if (export == null)
                {
                    _reason = "GameData.Export(BinaryWriter) not found";
                    Warn(_reason);
                    return;
                }

                MethodInfo prefix = typeof(P4_StorageGrids).GetMethod(
                    "ExportPrefix", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo postfix = typeof(P4_StorageGrids).GetMethod(
                    "ExportPostfix", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo finalizer = typeof(P4_StorageGrids).GetMethod(
                    "ExportFinalizer", BindingFlags.Static | BindingFlags.NonPublic);
                if (prefix == null || postfix == null || finalizer == null)
                {
                    _reason = "patch method lookup failed";
                    Warn(_reason);
                    return;
                }

                harmony.Patch(
                    export,
                    prefix: new HarmonyMethod(prefix),
                    postfix: new HarmonyMethod(postfix),
                    finalizer: new HarmonyMethod(finalizer));

                StorageGrids.Install();
                _armed = true;
                _reason = "armed";
                LogInfo(Tag + "patch registered: GameData.Export(BinaryWriter) [shave+restore]");
            }
            catch (Exception e)
            {
                _armed = false;
                _reason = "EXCEPTION " + e.GetType().Name;
                QualityEnv.WarnOnce("storage.apply", e);
            }
        }

        internal static string Describe()
        {
            if (!_armed)
            {
                return "NOT ARMED (" + Reason + ")";
            }
            return "armed; " + StorageGrids.Describe();
        }

        internal static void ResetState()
        {
            // Feature stays armed; only the runtime bookkeeping is dropped (new game / load).
            _warned = false;
        }

        private static void ExportPrefix(GameData __instance)
        {
            try
            {
                if (!_armed)
                {
                    return;
                }
                StorageGrids.BeginExportShave(__instance);
            }
            catch (Exception e)
            {
                if (!_warned)
                {
                    _warned = true;
                    QualityEnv.WarnOnce("storage.shave", e);
                }
            }
        }

        private static void ExportPostfix()
        {
            try
            {
                StorageGrids.EndExportShave();
            }
            catch (Exception e)
            {
                if (!_warned)
                {
                    _warned = true;
                    QualityEnv.WarnOnce("storage.restore", e);
                }
            }
        }

        private static Exception ExportFinalizer(Exception __exception)
        {
            try
            {
                StorageGrids.EndExportShave();
            }
            catch (Exception)
            {
                // best effort: never mask the vanilla save exception
            }
            return __exception;
        }

        private static void LogInfo(string message)
        {
            QualityEnv.LogInfo(message);
        }

        private static void Warn(string detail)
        {
            QualityEnv.WarnOnce("storage.notarmed", Tag + "NOT ARMED (vanilla containers): " + detail);
        }
    }
}
