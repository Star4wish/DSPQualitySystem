using System;
using System.Text;

namespace DSPQualitySystem.Diag
{
    /// <summary>
    /// In-game debug probes (spec section 6.2). Bound to F5/F7/F8/F9 in Plugin.Update.
    /// All probes are read-only and must never throw into the game loop.
    /// </summary>
    public static partial class Probes
    {
        private const int PickRingSize = 20;

        private static readonly int[] _pickEntity = new int[PickRingSize];
        private static readonly int[] _pickItem = new int[PickRingSize];
        private static readonly int[] _pickInc = new int[PickRingSize];
        private static int _pickCursor;
        private static int _pickTotal;

        private static int _buildBoundTotal;
        private static int _buildBatchLogs;
        private static int _buildBindLogs;

        /// <summary>Called from the PickFrom prefix for every assembler product handed out.</summary>
        public static void RecordPick(int entityId, int itemId, int inc)
        {
            int index = _pickCursor;
            _pickEntity[index] = entityId;
            _pickItem[index] = itemId;
            _pickInc[index] = inc;
            _pickCursor = (index + 1) % PickRingSize;
            _pickTotal++;
        }

        /// <summary>M2: one CreatePrebuilds batch captured N prebuild quality entries.</summary>
        public static void RecordBuildBatch(int bound)
        {
            _buildBoundTotal += bound;
            if (QualityConfig.DebugProbes == null || !QualityConfig.DebugProbes.Value)
            {
                return;
            }
            if (_buildBatchLogs >= 10)
            {
                return;
            }
            _buildBatchLogs++;
            var log = Plugin.Log;
            if (log != null)
            {
                log.LogInfo("[Probe][Build] prebuild quality captured: batch=" + bound
                    + " total=" + _buildBoundTotal);
            }
        }

        /// <summary>M2: a prebuild became an entity and received buildingInc.</summary>
        public static void RecordBuildBinding(int entityId, int perItem)
        {
            if (QualityConfig.DebugProbes == null || !QualityConfig.DebugProbes.Value)
            {
                return;
            }
            if (_buildBindLogs >= 20)
            {
                return;
            }
            _buildBindLogs++;
            var log = Plugin.Log;
            if (log != null)
            {
                log.LogInfo("[Probe][Build] bound entityId=" + entityId + " buildingInc=" + perItem);
            }
        }

        /// <summary>
        /// F10 debug injection: give the currently held item stack debugInjectPoints points per item.
        /// Only a test aid (spec 6.2 extension requested during M2 acceptance) so a tester can
        /// verify "high quality hand item -> buildingInc" without a full high quality production
        /// line. Gated by config debugProbes.
        /// </summary>
        public static void InjectHandItemQuality()
        {
            var log = Plugin.Log;
            try
            {
                if (!QualityConfig.DebugProbes.Value)
                {
                    if (log != null)
                    {
                        log.LogInfo("[Probe][F10] disabled by config debugProbes=false");
                    }
                    return;
                }
                if (!QualityConfig.Enabled.Value)
                {
                    if (log != null)
                    {
                        log.LogInfo("[Probe][F10] ignored because the mod is disabled");
                    }
                    return;
                }

                Player player = GameMain.mainPlayer;
                if (player == null || player.inhandItemId <= 0 || player.inhandItemCount <= 0)
                {
                    if (log != null)
                    {
                        log.LogInfo("[Probe][F10] no item in hand");
                    }
                    return;
                }

                int perItem = QualityConfig.DebugInjectPointsClamped;
                if (perItem <= 0)
                {
                    if (log != null)
                    {
                        log.LogInfo("[Probe][F10] debugInjectPoints=" + perItem + " (nothing injected)");
                    }
                    return;
                }
                player.SetHandItemInc_Unsafe(perItem * player.inhandItemCount);
                if (log != null)
                {
                    log.LogWarning("[Probe][F10] DEBUG INJECTION: item=" + player.inhandItemId
                        + " count=" + player.inhandItemCount + " perItem=" + perItem
                        + " (debugProbes=true; test only, do not use in normal play)");
                }
            }
            catch (Exception e)
            {
                if (log != null)
                {
                    log.LogWarning("[Probe][F10] failed: " + e);
                }
            }
        }

        /// <summary>
        /// F11 debug injection: set buildingInc=debugInjectPoints on the building under the cursor.
        /// Tester hover (or open) a running assembler, press F11, then F7 shows whether the
        /// produced item inc grew by one (with buildingChancePerPoint=maxUpgradeChance=100).
        /// Same debugProbes gate as F10.
        /// </summary>
        public static void InjectAimedBuildingQuality()
        {
            var log = Plugin.Log;
            try
            {
                if (!QualityConfig.DebugProbes.Value)
                {
                    if (log != null)
                    {
                        log.LogInfo("[Probe][F11] disabled by config debugProbes=false");
                    }
                    return;
                }
                if (!QualityConfig.Enabled.Value)
                {
                    if (log != null)
                    {
                        log.LogInfo("[Probe][F11] ignored because the mod is disabled");
                    }
                    return;
                }

                Player player = GameMain.mainPlayer;
                if (player == null)
                {
                    if (log != null)
                    {
                        log.LogInfo("[Probe][F11] no main player (main menu or loading)");
                    }
                    return;
                }

                int entityId = 0;
                PlayerController controller = player.controller;
                if (controller != null && controller.actionInspect != null)
                {
                    entityId = controller.actionInspect.hoveringEntityId;
                    if (entityId <= 0)
                    {
                        entityId = controller.actionInspect.inspectId;
                    }
                }

                PlanetData planet = GameMain.localPlanet;
                if (entityId <= 0 || planet == null || planet.factory == null
                    || planet.factory.entityPool == null || entityId >= planet.factory.entityPool.Length
                    || planet.factory.entityPool[entityId].id != entityId)
                {
                    if (log != null)
                    {
                        log.LogInfo("[Probe][F11] no aimed building (hover a building first, entityId="
                            + entityId + ")");
                    }
                    return;
                }

                int perItem = QualityConfig.DebugInjectPointsClamped;
                if (perItem <= 0)
                {
                    if (log != null)
                    {
                        log.LogInfo("[Probe][F11] debugInjectPoints=" + perItem + " (nothing injected)");
                    }
                    return;
                }
                QualityData.SetBuildingInc(planet.id, entityId, (byte)perItem);
                if (log != null)
                {
                    log.LogWarning("[Probe][F11] DEBUG INJECTION: entityId=" + entityId
                        + " proto=" + planet.factory.entityPool[entityId].protoId
                        + " buildingInc=" + perItem
                        + " (debugProbes=true; test only, do not use in normal play)");
                }
            }
            catch (Exception e)
            {
                if (log != null)
                {
                    log.LogWarning("[Probe][F11] failed: " + e);
                }
            }
        }


        /// <summary>F5: first 10 assemblers on the local planet.</summary>
        public static void DumpAssemblers()
        {
            var log = Plugin.Log;
            if (log == null)
            {
                return;
            }

            try
            {
                PlanetData planet = GameMain.localPlanet;
                if (planet == null || planet.factory == null || planet.factory.factorySystem == null)
                {
                    log.LogInfo("[Probe][F5] no local planet/factory (main menu or loading)");
                    return;
                }

                int planetId = planet.id;
                FactorySystem system = planet.factory.factorySystem;
                AssemblerComponent[] pool = system.assemblerPool;
                if (pool == null)
                {
                    log.LogInfo("[Probe][F5] assemblerPool is null");
                    return;
                }

                int cursor = system.assemblerCursor;
                if (cursor > pool.Length)
                {
                    cursor = pool.Length;
                }

                log.LogInfo("[Probe][F5] planet=" + planetId + " env=" + QualityEnv.CurrentPlanetId
                    + " cursor=" + cursor + " enabled=" + QualityConfig.Enabled.Value);
                int shown = 0;
                for (int i = 1; i < cursor && shown < 10; i++)
                {
                    ref AssemblerComponent assembler = ref pool[i];
                    if (assembler.id != i || assembler.entityId <= 0)
                    {
                        continue;
                    }

                    int entityId = assembler.entityId;
                    log.LogInfo("[Probe][F5] eid=" + entityId
                        + " recipe=" + assembler.recipeId
                        + " inputLevel=" + QualityData.GetInputLevel(planetId, entityId)
                        + " pending=" + QualityData.GetPendingLevel(planetId, entityId)
                        + " repl=" + QualityData.GetWasReplicating(planetId, entityId)
                        + " poolInc=" + QualityData.GetPoolInc(planetId, entityId)
                        + " poolCount=" + QualityData.GetPoolCount(planetId, entityId)
                        + " buildingInc=" + QualityData.GetBuildingInc(planetId, entityId));

                    // v0.19.1: the batch level an assembler commits is min over slots of
                    // incServed/served, so the raw slot counters are what tells a "lost one
                    // quality level after load" report apart from a genuinely mixed input batch.
                    RecipeExecuteData recipe = assembler.recipeExecuteData;
                    if (recipe != null && recipe.requireCounts != null
                        && assembler.served != null && assembler.incServed != null)
                    {
                        int slotCount = recipe.requireCounts.Length;
                        if (assembler.served.Length < slotCount)
                        {
                            slotCount = assembler.served.Length;
                        }
                        if (assembler.incServed.Length < slotCount)
                        {
                            slotCount = assembler.incServed.Length;
                        }
                        string slots = "";
                        for (int k = 0; k < slotCount; k++)
                        {
                            slots += " s" + k + "=" + assembler.served[k]
                                + "/" + assembler.incServed[k]
                                + "/" + recipe.requireCounts[k];
                        }
                        log.LogInfo("[Probe][F5] eid=" + entityId + " slots(served/incServed/need)" + slots);
                    }

                    shown++;
                }

                log.LogInfo("[Probe][F5] shown=" + shown);
            }
            catch (Exception e)
            {
                log.LogWarning("[Probe][F5] failed: " + e);
            }
        }

        /// <summary>F7: last 20 assembler PickFrom calls.</summary>
        public static void DumpPicks()
        {
            var log = Plugin.Log;
            if (log == null)
            {
                return;
            }

            try
            {
                int count = _pickTotal < PickRingSize ? _pickTotal : PickRingSize;
                log.LogInfo("[Probe][F7] env=" + QualityEnv.CurrentPlanetId
                    + " recent assembler PickFrom calls (newest first), total=" + _pickTotal);
                for (int k = 0; k < count; k++)
                {
                    int index = _pickCursor - 1 - k;
                    while (index < 0)
                    {
                        index += PickRingSize;
                    }
                    log.LogInfo("[Probe][F7] eid=" + _pickEntity[index]
                        + " item=" + _pickItem[index]
                        + " inc=" + _pickInc[index]);
                }
            }
            catch (Exception e)
            {
                log.LogWarning("[Probe][F7] failed: " + e);
            }
        }

        /// <summary>F8: current Cargo linear tables.</summary>
        public static void DumpTables()
        {
            var log = Plugin.Log;
            if (log == null)
            {
                return;
            }

            try
            {
                log.LogInfo("[Probe][F8] enabled=" + QualityConfig.Enabled.Value);
                Patches.P0_Tables.LogArrays(log);
            }
            catch (Exception e)
            {
                log.LogWarning("[Probe][F8] failed: " + e);
            }
        }

        /// <summary>F9: building quality distribution on the local planet.</summary>
        public static void DumpBuildingInc()
        {
            var log = Plugin.Log;
            if (log == null)
            {
                return;
            }

            try
            {
                PlanetData planet = GameMain.localPlanet;
                if (planet == null)
                {
                    log.LogInfo("[Probe][F9] no local planet (main menu or loading)");
                    return;
                }

                PlanetQuality quality = QualityData.Get(planet.id);
                int[] histogram = new int[Cargo.kIncLevelMax + 1];
                int total = 0;
                if (quality != null)
                {
                    byte[] buildingInc = quality.buildingInc;
                    if (buildingInc == null)
                    {
                        log.LogInfo("[Probe][F9] planet=" + planet.id + " buildingInc array is null");
                        return;
                    }
                    for (int i = 1; i < buildingInc.Length; i++)
                    {
                        int level = buildingInc[i];
                        if (level < 0)
                        {
                            level = 0;
                        }
                        if (level > Cargo.kIncLevelMax)
                        {
                            level = Cargo.kIncLevelMax;
                        }
                        histogram[level]++;
                        total++;
                    }
                }

                var sb = new StringBuilder();
                for (int level = 0; level < histogram.Length; level++)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(' ');
                    }
                    sb.Append(level).Append('=').Append(histogram[level]);
                }
                log.LogInfo("[Probe][F9] planet=" + planet.id + " entities=" + total + " buildingInc " + sb);
            }
            catch (Exception e)
            {
                log.LogWarning("[Probe][F9] failed: " + e);
            }
        }
    }
}
