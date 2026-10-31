using System;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// M2 spec 3.6: expose Player.UseHandItems(id, out useInc). The postfix records the quality
    /// points of the consumed hand item into BuildQualityStash while a build batch is open.
    /// Vanilla quirk (asm_full.cs:170099-170111): when the hand holds exactly useCnt items the
    /// method consumes them but returns 0, so the snapshot count is used as the fallback.
    /// </summary>
    [HarmonyPatch(typeof(Player), "UseHandItems")]
    internal static class UseHandItems_ExposeInc
    {
        private struct State
        {
            public int itemId;
            public int count;
        }

        private static bool _warnedLastItem;

        private static void Prefix(Player __instance, out State __state)
        {
            __state = new State
            {
                itemId = __instance.inhandItemId,
                count = __instance.inhandItemCount
            };
        }

        private static void Postfix(Player __instance, int useCnt, int useInc, int __result, State __state)
        {
            if (!BuildQualityStash.Capturing)
            {
                return;
            }

            try
            {
                if (__state.itemId <= 0)
                {
                    return;
                }

                int used = __result;
                if (used <= 0 && __state.count > 0 && __instance.inhandItemCount == 0)
                {
                    // The whole remaining stack was consumed but vanilla returned 0.
                    used = __state.count > useCnt ? useCnt : __state.count;
                    if (!_warnedLastItem)
                    {
                        _warnedLastItem = true;
                        if (Plugin.Log != null)
                        {
                            Plugin.Log.LogInfo("[QSystem][build] last-item path: used=" + used
                                + " taken from snapshot because vanilla UseHandItems returned 0");
                        }
                    }
                }

                if (used <= 0)
                {
                    return;
                }

                int perItem = useInc / used;
                if (perItem < 0)
                {
                    perItem = 0;
                }
                else if (perItem > Cargo.kIncLevelMax)
                {
                    perItem = Cargo.kIncLevelMax;
                }
                BuildQualityStash.Push(__state.itemId, perItem);
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("build.usehanditems", e);
            }
        }
    }

    /// <summary>
    /// M2 spec 3.7: after BuildTool_Click.CreatePrebuilds() every successfully paid building
    /// holds buildPreview.objId = -prebuildId. Map the hand-item quality queue onto those
    /// prebuildIds and remember the points until the entity data is created.
    /// </summary>
    [HarmonyPatch(typeof(BuildTool_Click), "CreatePrebuilds")]
    internal static class CreatePrebuilds_Capture
    {
        private static void Prefix()
        {
            BuildQualityStash.BeginBatch();
        }

        private static void Postfix(BuildTool_Click __instance)
        {
            try
            {
                var previews = __instance.buildPreviews;
                if (previews == null)
                {
                    return;
                }

                int bound = 0;
                for (int i = 0; i < previews.Count; i++)
                {
                    BuildPreview preview = previews[i];
                    if (preview.objId >= 0 || preview.item == null)
                    {
                        continue;
                    }

                    int perItem;
                    if (!BuildQualityStash.TryPopFor(preview.item.ID, out perItem))
                    {
                        continue;
                    }
                    if (perItem <= 0)
                    {
                        continue;
                    }

                    QualityData.SetPrebuildInc(-preview.objId, perItem);
                    bound++;
                }

                if (bound > 0)
                {
                    Diag.Probes.RecordBuildBatch(bound);
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("build.prebuilds", e);
            }
            finally
            {
                BuildQualityStash.EndBatch();
            }
        }
    }

    /// <summary>
    /// M2 hardening: AddPrebuildDataWithComponents returns a pooled prebuild index that can be
    /// recycled. Clear any stale stored points for that id right after vanilla creates the
    /// prebuild, so a non-hand-built building can never inherit an old entry. Our own
    /// CreatePrebuilds postfix writes the fresh value afterwards, so the order is safe.
    /// </summary>
    [HarmonyPatch(typeof(PlanetFactory), "AddPrebuildDataWithComponents")]
    internal static class AddPrebuildData_ClearStale
    {
        private static void Postfix(PlanetFactory __instance, PrebuildData prebuild, int __result)
        {
            try
            {
                if (__result > 0)
                {
                    QualityData.ClearPrebuild(__result);
                    // v0.11.5: generic preference - the building inherits the points of the building
                    // item that was actually consumed for it.
                    // v0.15.0: the blueprint-carried-quality experiment was removed, so this is the
                    // only receipt path (no marker is written or read any more).
                    // v0.17.0: the receipt now also covers the normal hand-build window, including
                    // the backpack branch of CreatePrebuilds (asm_full.cs:155445-155448) which never
                    // calls Player.UseHandItems. That branch is why backpack-built buildings such as
                    // the 大型采矿机 previously stayed at 0 points.
                    P4_ConsumePriority.CapturePasteConsumed(prebuild, __result);
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("build.clearstale", e);
            }
        }
    }

    /// <summary>
    /// M2 spec 3.8: AddEntityDataWithComponents(entity, prebuildId) is the only place that has
    /// both the prebuildId and the new entityId (its return value). Bind the remembered points.
    /// Hand-built / instant-build paths bind through BuildQualityStash; pasted blueprints receive
    /// the generic P4_ConsumePriority receipt of the item that was actually consumed (v0.15.0
    /// removed the blueprint-carried-quality marker path).
    /// </summary>
    [HarmonyPatch(typeof(PlanetFactory), "AddEntityDataWithComponents")]
    internal static class AddEntityData_Bind
    {
        private static void Postfix(PlanetFactory __instance, int prebuildId, int __result)
        {
            try
            {
                if (__result <= 0 || prebuildId <= 0)
                {
                    return;
                }

                int perItem = QualityData.TakePrebuildInc(prebuildId);
                if (perItem <= 0)
                {
                    return;
                }

                int inc = perItem > Cargo.kIncLevelMax ? Cargo.kIncLevelMax : perItem;
                QualityData.SetBuildingInc(__instance.planetId, __result, (byte)inc);
                Diag.Probes.RecordBuildBinding(__result, inc);
                // v0.18.0 (issue 1b): a power node (tesla tower / wireless tower / substation) only
                // knows its quality points now, so scale its connect distance / cover radius here.
                P5_PowerRange.ApplyForEntity(__instance, __instance.planetId, __result);
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("build.bind", e);
            }
        }
    }
}
