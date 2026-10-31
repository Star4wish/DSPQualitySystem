using System;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// M1 spec 3.2/3.3 (R-1 inheritance) with batch-boundary semantics matching vanilla
    /// AssemblerComponent.InternalUpdate (asm_full.cs:25538-25748):
    ///  - vanilla computes the batch quality (num7, 25687-25723) ONLY when it starts a new
    ///    batch (the !replicating block at 25685), from served[]/incServed[] BEFORE
    ///    split_inc_level consumes them; during a batch served[] is drained to 0.
    ///  - therefore the Prefix must NOT overwrite inputLevel every tick. It only predicts the
    ///    quality of a batch that can start this call (identical gate to vanilla line 25696)
    ///    and stores it in pendingLevel; the Postfix commits it only when the call actually
    ///    started a batch (replicating==true afterwards and either idle before or produced).
    ///  - products are pooled with the quality of the batch that produced them (inputLevel as
    ///    of the call start), because one call can finish a batch and start the next one. The
    ///    pool write happens before the pending commit.
    ///  - the extra productive output (extraTime block, line 25546) happens at the top of the
    ///    same call and belongs to the running batch, so it is covered by the same delta/pool
    ///    write; no separate roll for it.
    /// pendingLevel / wasReplicating live in QualityData so they follow planet scope and are
    /// visible to the F5 probe; they are reset for the entity on every Prefix call.
    /// </summary>
    [HarmonyPatch(typeof(AssemblerComponent), "InternalUpdate")]
    internal static class Assembler_InternalUpdate_Quality
    {
        private const int DebugLogLimit = 20;

        // v0.19.1 diagnosis: how many "batch started" probe lines a session may print. The
        // ingredient batch level is what the next cycle derives quality from, so when a save
        // round-trip loses it the probe shows which average the machine actually read.
        private const int BatchProbeLimit = 60;
        private static int _reconcileLogs;
        private static int _batchProbes;

        private static void Prefix(ref AssemblerComponent __instance)
        {
            try
            {
                int entityId = __instance.entityId;
                if (entityId <= 0)
                {
                    return;
                }

                if (entityId > QualityData.MaxEntityId)
                {
                    // v0.12.1: a dummy/pooled component can carry a sentinel id; never let it
                    // reach the quality arrays (that used to throw IndexOutOfRangeException).
                    QualityEnv.WarnOnce("assembler.entityId",
                        "assembler ignored an out-of-range entityId=" + entityId
                        + " (planet=" + QualityEnv.CurrentPlanetId + ", max=" + QualityData.MaxEntityId + ")");
                    return;
                }

                // Disabled: keep the hot path allocation-free (no quality data is created).
                if (!QualityConfig.Enabled.Value)
                {
                    return;
                }

                int planetId = QualityEnv.CurrentPlanetId;
                if (planetId <= 0)
                {
                    return;
                }

                // Always drop the previous tick's prediction first: a stale pending level must
                // never be committed by a call that did not start a batch.
                QualityData.SetPendingLevel(planetId, entityId, -1);

                QualityData.SetWasReplicating(planetId, entityId, __instance.replicating ? (byte)1 : (byte)0);

                RecipeExecuteData data = __instance.recipeExecuteData;
                if (data == null)
                {
                    return;
                }

                int[] requireCounts = data.requireCounts;
                if (requireCounts == null || requireCounts.Length == 0)
                {
                    return;
                }

                int[] served = __instance.served;
                int[] incServed = __instance.incServed;
                if (served == null || incServed == null)
                {
                    return;
                }

                int count = requireCounts.Length;
                if (served.Length < count)
                {
                    count = served.Length;
                }
                if (incServed.Length < count)
                {
                    count = incServed.Length;
                }

                int level = Cargo.kIncLevelMax;
                for (int i = 0; i < count; i++)
                {
                    int need = requireCounts[i];
                    if (need <= 0)
                    {
                        continue;
                    }

                    int have = served[i];
                    // Same gate as vanilla asm_full.cs:25696: cannot start a batch.
                    if (have <= 0 || have < need)
                    {
                        return;
                    }

                    int inc = incServed[i];
                    if (inc < 0)
                    {
                        inc = 0;
                    }

                    // v0.19.1: round half up instead of vanilla's truncation, so a batch whose
                    // ingredients average 5.5+ keeps the higher level (user report: a 10 point
                    // assembler fed by a belt with a few low level items produced 7 instead of 8).
                    int average = QualityMath.BatchInputLevel(inc, have, Cargo.kIncLevelMax);
                    if (average < level)
                    {
                        level = average;
                    }
                }

                if (QualityConfig.DevMode && _batchProbes < BatchProbeLimit)
                {
                    _batchProbes++;
                    string slots = "";
                    for (int i = 0; i < count; i++)
                    {
                        slots += " s" + i + "=" + served[i] + "/" + incServed[i] + "/" + requireCounts[i];
                    }
                    Plugin.Log.LogInfo("[QSystem][assembler][batch] eid=" + entityId
                        + " planet=" + planetId + " lvl=" + level + slots);
                }

                QualityData.SetPendingLevel(planetId, entityId, (short)level);
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("assembler.prefix", e);
            }
        }

        private static void Postfix(ref AssemblerComponent __instance)
        {
            try
            {
                if (!QualityConfig.Enabled.Value)
                {
                    return;
                }

                int entityId = __instance.entityId;
                if (entityId <= 0)
                {
                    return;
                }

                if (entityId > QualityData.MaxEntityId)
                {
                    return;
                }

                int planetId = QualityEnv.CurrentPlanetId;
                if (planetId <= 0)
                {
                    return;
                }

                RecipeExecuteData data = __instance.recipeExecuteData;
                if (data == null || data.products == null || data.products.Length == 0)
                {
                    return;
                }

                int[] produced = __instance.produced;
                if (produced == null)
                {
                    return;
                }

                int slots = produced.Length;
                if (slots > QualityData.ProducedSlots)
                {
                    slots = QualityData.ProducedSlots;
                }

                int inherited = QualityData.GetInputLevel(planetId, entityId);
                int quality = inherited < 0 ? 0 : inherited;

                int producedThisCall = 0;
                int netProduced = 0;
                for (int i = 0; i < slots; i++)
                {
                    int now = produced[i];
                    int previous = QualityData.GetPrevProduced(planetId, entityId, i);
                    if (previous < 0)
                    {
                        // First observation after placement/load: baseline only, never a pool write.
                        QualityData.SetPrevProduced(planetId, entityId, i, now);
                        continue;
                    }

                    netProduced += now - previous;
                    QualityData.SetPrevProduced(planetId, entityId, i, now);
                }

                // v0.16.0 (Bug 3): PickFrom decrements produced[] between two ticks, so the raw
                // delta alone misses production that was picked in the same window. Adding the
                // picks back keeps the pool credited (this is what fixed 增产 extras arriving at
                // 0 points: they trickle out one item per 10 cycles and were cancelled by picks).
                int pickedThisTick = QualityData.ConsumeProductPicked(planetId, entityId);
                producedThisCall = QualityMath.ProducedDelta(netProduced, pickedThisTick);

                if (producedThisCall > 0)
                {
                    // R-2 (building quality upgrade) rolls at most once per produced batch per
                    // machine tick; with buildingInc == 0 (M1) this is a no-op.
                    int pooled = QualityRules.RollCycleQuality(planetId, entityId, quality);
                    int poolBefore = QualityData.GetPoolInc(planetId, entityId);
                    int countBefore = QualityData.GetPoolCount(planetId, entityId);
                    QualityData.AddPool(planetId, entityId, pooled * producedThisCall, producedThisCall);
                    if (QualityConfig.DevMode && _reconcileLogs < DebugLogLimit)
                    {
                        _reconcileLogs++;
                        Plugin.Log.LogInfo("[QSystem][assembler] pool credited produced="
                            + producedThisCall + " net=" + netProduced + " picked=" + pickedThisTick
                            + " pooled=" + pooled + " in=" + quality
                            + " pool=" + poolBefore + "/" + countBefore
                            + "->" + QualityData.GetPoolInc(planetId, entityId)
                            + "/" + QualityData.GetPoolCount(planetId, entityId));
                    }
                }

                // Commit the new batch only if this call actually started one:
                //  - replicating==true after the call, and
                //  - either we were idle before (normal start) or production happened (finish+start).
                int pending = QualityData.GetPendingLevel(planetId, entityId);
                bool startedBatch = pending >= 0
                    && __instance.replicating
                    && (QualityData.GetWasReplicating(planetId, entityId) == 0 || producedThisCall > 0);
                if (startedBatch)
                {
                    int previous = QualityData.GetInputLevel(planetId, entityId);
                    QualityData.SetInputLevel(planetId, entityId, (short)pending);
                    if (QualityConfig.DevMode && _batchProbes < BatchProbeLimit)
                    {
                        _batchProbes++;
                        Plugin.Log.LogInfo("[QSystem][assembler][batch] eid=" + entityId
                            + " commit in=" + previous + "->" + pending
                            + " produced=" + producedThisCall);
                    }
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("assembler.postfix", e);
            }
        }
    }
}
