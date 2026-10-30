using System;

namespace DSPQualitySystem.Diag
{
    /// <summary>
    /// Miner probes: R-3 statistics (F12) and the F6 triage dump for the "miner stalls after
    /// quality injection" report (miner state, power/vein health and insert-target congestion).
    /// Split out of Probes.cs to keep every source file below the 500 line limit.
    /// </summary>
    public static partial class Probes
    {
        private static int _minerHits;
        private static int _minerHitLogs;
        private static int _minerCalls;
        private static int _lastMinerEntity;
        private static int _lastMinerLevel;

        /// <summary>R-3 hook: one mined item received quality points (rare unless testing).</summary>
        public static void RecordMinerHit(int entityId, int inc)
        {
            _minerHits++;
            if (_minerHitLogs < 20)
            {
                _minerHitLogs++;
                var log = Plugin.Log;
                if (log != null)
                {
                    log.LogInfo("[Probe][Miner] quality hit eid=" + entityId + " inc=" + inc
                        + " hits=" + _minerHits);
                }
            }
        }

        /// <summary>
        /// F12 helper: count every invocation of the miner output hook (level=0 included) so the
        /// probe can tell "the transpiler hook was never called" apart from "called but rolled 0".
        /// </summary>
        public static void RecordMinerCall(int entityId, int level)
        {
            _minerCalls++;
            _lastMinerEntity = entityId;
            _lastMinerLevel = level;
        }

        /// <summary>F12: miner quality statistics (miner output does not go through PickFrom).</summary>
        public static void DumpMinerStats()
        {
            var log = Plugin.Log;
            if (log == null)
            {
                return;
            }

            try
            {
                log.LogInfo("[Probe][F12] miner quality hits=" + _minerHits
                    + " calls=" + _minerCalls
                    + " lastMinerCall=eid=" + _lastMinerEntity + " level=" + _lastMinerLevel
                    + " enabled=" + QualityConfig.Enabled.Value
                    + " minerChancePerPoint=" + QualityConfig.MinerChancePerPoint.Value);
            }
            catch (Exception e)
            {
                log.LogWarning("[Probe][F12] failed: " + e);
            }
        }

        /// <summary>
        /// F6 debug probe (triage for "miner stalls after quality injection"): dump the miner
        /// under the mouse cursor plus the congestion of its insert target, so a real stall can be
        /// told apart from "output belt is full => vanilla productCount hits 50 => miner sleeps"
        /// (vanilla keeps mining only while `time >= period && productCount < 50`).
        /// </summary>
        public static void DumpAimedMiner()
        {
            var log = Plugin.Log;
            if (log == null)
            {
                return;
            }

            try
            {
                if (!QualityConfig.DebugProbes.Value)
                {
                    log.LogInfo("[Probe][F6] disabled by config debugProbes=false");
                    return;
                }

                Player player = GameMain.mainPlayer;
                if (player == null)
                {
                    log.LogInfo("[Probe][F6] no main player (main menu or loading)");
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
                PlanetFactory factory = planet != null ? planet.factory : null;
                if (factory == null || factory.entityPool == null || factory.factorySystem == null
                    || entityId <= 0 || entityId >= factory.entityPool.Length
                    || factory.entityPool[entityId].id != entityId)
                {
                    log.LogInfo("[Probe][F6] no aimed building (hover a building first, entityId="
                        + entityId + ")");
                    return;
                }

                log.LogInfo("[Probe][F6] entityId=" + entityId
                    + " proto=" + factory.entityPool[entityId].protoId
                    + " buildingInc=" + QualityData.GetBuildingInc(planet.id, entityId));

                int minerId = factory.entityPool[entityId].minerId;
                if (minerId <= 0)
                {
                    log.LogInfo("[Probe][F6] not a miner (minerId=" + minerId + ")");
                    return;
                }
                if (factory.factorySystem.minerPool == null
                    || minerId >= factory.factorySystem.minerPool.Length)
                {
                    log.LogInfo("[Probe][F6] minerId=" + minerId + " out of minerPool range");
                    return;
                }

                MinerComponent miner = factory.factorySystem.minerPool[minerId];
                log.LogInfo("[Probe][F6] minerId=" + minerId
                    + " type=" + miner.type + " workstate=" + miner.workstate
                    + " productId=" + miner.productId + " productCount=" + miner.productCount
                    + " time=" + miner.time + " period=" + miner.period
                    + " insertTarget=" + miner.insertTarget + " veinCount=" + miner.veinCount
                    + " currentVeinIndex=" + miner.currentVeinIndex
                    + " speedDamper=" + miner.speedDamper
                    + " minimumVeinAmount=" + miner.minimumVeinAmount
                    + " costFrac=" + miner.costFrac);
                log.LogInfo("[Probe][F6] stall checks: productCount=" + miner.productCount
                    + " (vanilla keeps mining only while productCount < 50; 50 => output belt backed up)"
                    + " veinCount=" + miner.veinCount + " (0 => no veins left)");
                DumpMinerPower(factory, miner);
                DumpMinerVein(factory, miner);
                DumpInsertTarget(factory, miner.insertTarget);
            }
            catch (Exception e)
            {
                log.LogWarning("[Probe][F6] failed: " + e);
            }
        }

        private static void DumpInsertTarget(PlanetFactory factory, int target)
        {
            var log = Plugin.Log;
            if (log == null)
            {
                return;
            }

            if (target <= 0 || factory.entityPool == null || target >= factory.entityPool.Length
                || factory.entityPool[target].id != target)
            {
                log.LogInfo("[Probe][F6] insertTarget=" + target
                    + " (none/invalid; no route for mined items)");
                return;
            }

            EntityData targetEntity = factory.entityPool[target];
            log.LogInfo("[Probe][F6] target proto=" + targetEntity.protoId
                + " beltId=" + targetEntity.beltId
                + " assemblerId=" + targetEntity.assemblerId
                + " storageId=" + targetEntity.storageId
                + " inserterId=" + targetEntity.inserterId);

            CargoTraffic traffic = factory.cargoTraffic;
            if (targetEntity.beltId <= 0 || traffic == null || traffic.beltPool == null
                || targetEntity.beltId >= traffic.beltPool.Length)
            {
                return;
            }

            BeltComponent belt = traffic.beltPool[targetEntity.beltId];
            int segPathId = belt.segPathId;
            int cargoCount = -1;
            if (traffic.pathPool != null && segPathId > 0 && segPathId < traffic.pathPool.Length
                && traffic.pathPool[segPathId] != null
                && traffic.pathPool[segPathId].cargoContainer != null)
            {
                cargoCount = traffic.pathPool[segPathId].cargoContainer.cargoCount;
            }
            log.LogInfo("[Probe][F6] beltId=" + targetEntity.beltId
                + " segPathId=" + segPathId + " segIndex=" + belt.segIndex
                + " cargoCount=" + cargoCount
                + " (belt congestion; if productCount keeps at 50 the miner is blocked by a full belt)");
        }

        /// <summary>
        /// F6 helper: power consumer state of the miner. Vanilla <c>MinerComponent.InternalUpdate</c>
        /// returns immediately when <c>power &lt; 0.1f</c>, so an unpowered miner looks identical to a
        /// stalled one unless this is printed.
        /// </summary>
        private static void DumpMinerPower(PlanetFactory factory, MinerComponent miner)
        {
            var log = Plugin.Log;
            if (log == null)
            {
                return;
            }

            PowerSystem powerSystem = factory.powerSystem;
            if (miner.pcId <= 0 || powerSystem == null || powerSystem.consumerPool == null
                || miner.pcId >= powerSystem.consumerPool.Length)
            {
                log.LogInfo("[Probe][F6] power: pcId=" + miner.pcId
                    + " (no power consumer; if unpowered vanilla takes the power<0.1 early-out)");
                return;
            }

            PowerConsumerComponent consumer = powerSystem.consumerPool[miner.pcId];
            log.LogInfo("[Probe][F6] power: pcId=" + miner.pcId
                + " entityId=" + consumer.entityId + " networkId=" + consumer.networkId
                + " workEnergyPerTick=" + consumer.workEnergyPerTick
                + " (networkId==0 => unpowered => miner idles via power<0.1 early-out)");
        }

        /// <summary>F6 helper: current vein health (empty/removed vein makes a Vein miner idle).</summary>
        private static void DumpMinerVein(PlanetFactory factory, MinerComponent miner)
        {
            var log = Plugin.Log;
            if (log == null)
            {
                return;
            }

            if (miner.type != EMinerType.Vein)
            {
                log.LogInfo("[Probe][F6] vein: n/a (type=" + miner.type + ")");
                return;
            }
            if (miner.veins == null || miner.veinCount <= 0)
            {
                log.LogInfo("[Probe][F6] vein: no veins attached (veinCount=" + miner.veinCount + ")");
                return;
            }

            int index = miner.currentVeinIndex;
            if (index < 0 || index >= miner.veins.Length)
            {
                log.LogInfo("[Probe][F6] vein: currentVeinIndex=" + index + " out of range");
                return;
            }

            int veinId = miner.veins[index];
            VeinData[] veinPool = factory.veinPool;
            if (veinPool == null || veinId <= 0 || veinId >= veinPool.Length
                || veinPool[veinId].id != veinId)
            {
                log.LogInfo("[Probe][F6] vein: current veinId=" + veinId
                    + " (invalid/exhausted; miner re-indexes next tick)");
                return;
            }

            VeinData vein = veinPool[veinId];
            log.LogInfo("[Probe][F6] vein: id=" + veinId + " productId=" + vein.productId
                + " amount=" + vein.amount + " groupIndex=" + vein.groupIndex);
        }
    }
}
