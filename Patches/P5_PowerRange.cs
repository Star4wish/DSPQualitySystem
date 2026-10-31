using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.18.0 (issue 1b): power node buildings scale their supply geometry with quality points.
    ///
    /// Vanilla builds every power node through
    /// <c>PlanetFactory.AddEntityDataWithComponents -&gt; PowerSystem.NewNodeComponent(entityId,
    /// desc.powerConnectDistance, desc.powerCoverRadius)</c>, which copies those two prefab numbers into
    /// the persistent <c>PowerSystem.nodePool[nodeId].connectDistance/.coverRadius</c> fields. The
    /// power network re-reads them every frame (node.connDistance2 / node.coverRadius2), so rewriting
    /// them is enough to grow both the cable reach and the supply circle of a tesla tower / wireless
    /// power tower / substation.
    ///
    /// The quality points of the brand new entity only exist *after* the factory bound them (our
    /// <c>P1_BuildCapture.AddEntityData_Bind</c> postfix), therefore the range is applied from three
    /// places, all idempotent (desc value x factor, never the current stored value x factor):
    ///   1. right after the entity binding above (a freshly built node),
    ///   2. once per planet from a <c>PowerSystem.GameTick</c> prefix, which covers every node of a
    ///      game that was loaded with quality data already on disk (and any planet loading later),
    ///   3. <see cref="ResetState"/> on import / new game so (2) runs again for a re-loaded save.
    /// </summary>
    internal static class P5_PowerRange
    {
        private static bool _armed;
        private static string _reason;
        private static long _applied;
        private static long _planets;
        private static int _logged;

        /// <summary>Planets whose whole node pool has already been re-applied in this session.</summary>
        private static readonly HashSet<int> PlanetsDone = new HashSet<int>();

        internal static bool Armed
        {
            get { return _armed; }
        }

        internal static void Apply(Harmony harmony)
        {
            try
            {
                if (QualityConfig.PowerRangeBonus == null || !QualityConfig.PowerRangeBonus.Value)
                {
                    Disable("powerRangeBonus disabled");
                    return;
                }

                // Every member below is public in the shipped Assembly-CSharp; check them once so a game
                // update renames one, the feature switches itself off instead of throwing in a tick.
                string missing = MissingMember();
                if (missing != null)
                {
                    Disable(missing);
                    return;
                }

                MethodInfo tick = typeof(PowerSystem).GetMethod("GameTick",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { typeof(long), typeof(bool), typeof(bool), typeof(int) }, null);
                if (tick == null)
                {
                    Disable("PowerSystem.GameTick(long,bool,bool,int) not found");
                    return;
                }

                MethodInfo prefix = typeof(P5_PowerRange).GetMethod("OnPowerGameTick",
                    BindingFlags.NonPublic | BindingFlags.Static);
                harmony.Patch(tick, prefix: new HarmonyMethod(prefix));
                _armed = true;
                P5Common.Info("[QSystem][powerrange] armed: power node connect distance / cover radius"
                    + " x quality curve (one-shot planet sweep + per-build apply)");
            }
            catch (Exception e)
            {
                Disable("install failed: " + e.Message);
                P5Common.WarnOnce("powerrange.install", e.ToString());
            }
        }

        private static string MissingMember()
        {
            if (typeof(PowerSystem).GetField("nodePool") == null)
            {
                return "PowerSystem.nodePool missing";
            }
            if (typeof(PlanetFactory).GetField("entityPool") == null)
            {
                return "PlanetFactory.entityPool missing";
            }
            if (typeof(EntityData).GetField("powerNodeId") == null)
            {
                return "EntityData.powerNodeId missing";
            }
            if (typeof(PrefabDesc).GetField("isPowerNode") == null)
            {
                return "PrefabDesc.isPowerNode missing";
            }
            if (typeof(PrefabDesc).GetField("powerConnectDistance") == null)
            {
                return "PrefabDesc.powerConnectDistance missing";
            }
            if (typeof(PrefabDesc).GetField("powerCoverRadius") == null)
            {
                return "PrefabDesc.powerCoverRadius missing";
            }
            return null;
        }

        /// <summary>One-shot per planet: rewrite every existing power node from its prefab x factor.</summary>
        private static void OnPowerGameTick(PowerSystem __instance)
        {
            if (!_armed)
            {
                return;
            }
            try
            {
                EnsurePlanet(__instance);
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("powerrange.tick", e.ToString());
            }
        }

        private static void EnsurePlanet(PowerSystem powerSystem)
        {
            if (powerSystem == null)
            {
                return;
            }
            PlanetFactory factory = powerSystem.factory;
            if (factory == null || powerSystem.nodePool == null)
            {
                return;
            }
            int planetId = factory.planetId;
            if (planetId < 0 || !PlanetsDone.Add(planetId))
            {
                return;
            }
            _planets++;
            int cursor = powerSystem.nodeCursor;
            for (int nodeId = 0; nodeId < cursor && nodeId < powerSystem.nodePool.Length; nodeId++)
            {
                PowerNodeComponent node = powerSystem.nodePool[nodeId];
                if (node.id != nodeId || node.entityId <= 0)
                {
                    continue;
                }
                ApplyOne(factory, planetId, node.entityId, nodeId);
            }
            RefreshModel(factory);
        }

        /// <summary>
        /// v0.18.0: called from <c>P1_BuildCapture.AddEntityData_Bind</c> once a freshly built entity got
        /// its quality points. Cheap no-op for anything that is not a power node.
        /// </summary>
        internal static void ApplyForEntity(PlanetFactory factory, int planetId, int entityId)
        {
            if (!_armed || factory == null || entityId <= 0)
            {
                return;
            }
            try
            {
                EntityData[] pool = factory.entityPool;
                if (pool == null || entityId >= pool.Length)
                {
                    return;
                }
                int nodeId = pool[entityId].powerNodeId;
                if (nodeId <= 0)
                {
                    return;
                }
                ApplyOne(factory, planetId, entityId, nodeId);
                RefreshModel(factory);
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("powerrange.entity", e.ToString());
            }
        }

        private static void ApplyOne(PlanetFactory factory, int planetId, int entityId, int nodeId)
        {
            PowerSystem powerSystem = factory.powerSystem;
            if (powerSystem == null || powerSystem.nodePool == null
                || nodeId < 0 || nodeId >= powerSystem.nodePool.Length)
            {
                return;
            }
            if (powerSystem.nodePool[nodeId].entityId != entityId)
            {
                return;
            }

            EntityData[] pool = factory.entityPool;
            if (pool == null || entityId <= 0 || entityId >= pool.Length)
            {
                return;
            }
            ItemProto proto = LDB.items.Select(pool[entityId].protoId);
            if (proto == null || proto.prefabDesc == null || !proto.prefabDesc.isPowerNode)
            {
                return;
            }
            PrefabDesc desc = proto.prefabDesc;
            float factor = QualityData.PowerRangeFactor(planetId, entityId);
            if (!(factor > 0f) || float.IsNaN(factor))
            {
                factor = 1f;
            }

            // Idempotent: always rebuild from the prefab numbers, so applying twice (build + planet
            // sweep) or reloading a save written by this version can never compound the bonus.
            float connect = desc.powerConnectDistance * factor;
            float cover = desc.powerCoverRadius * factor;
            powerSystem.nodePool[nodeId].connectDistance = connect;
            powerSystem.nodePool[nodeId].coverRadius = cover;
            _applied++;
            if (factor > 1f)
            {
                P5Common.Debug(ref _logged, "[QSystem][powerrange] eid=" + entityId + " node=" + nodeId
                    + " points=" + QualityData.GetBuildingInc(planetId, entityId)
                    + " factor=" + factor.ToString("0.###")
                    + " conn=" + connect.ToString("0.##") + " cover=" + cover.ToString("0.##"));
            }
        }

        private static void RefreshModel(PlanetFactory factory)
        {
            try
            {
                PlanetData planet = factory.planet;
                if (planet != null && planet.factoryLoaded && planet.factoryModel != null)
                {
                    planet.factoryModel.RefreshPowerNodes();
                }
            }
            catch (Exception)
            {
                // Visual refresh only; the network logic reads nodePool directly.
            }
        }

        internal static void ResetState()
        {
            try
            {
                PlanetsDone.Clear();
                _applied = 0;
                _planets = 0;
                _logged = 0;
            }
            catch (Exception)
            {
            }
        }

        private static void Disable(string reason)
        {
            _armed = false;
            _reason = reason;
            P5Common.Info("[QSystem][powerrange] NOT ARMED (vanilla node range): " + reason);
        }

        internal static string Describe()
        {
            return "armed=" + _armed + " reason=" + (_reason ?? "-")
                + " planets=" + _planets + " applied=" + _applied;
        }
    }
}
