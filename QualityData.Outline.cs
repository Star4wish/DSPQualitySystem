using System.Collections.Generic;

namespace DSPQualitySystem
{
    public static partial class QualityData
    {
        /// <summary>
        /// v0.11.6 (task-19): fills entityIds with every entity on the planet whose building quality
        /// reaches minLevel. Only reads the in-memory table, never touches the game state.
        /// </summary>
        public static int CollectFullQualityEntities(int planetId, int minLevel, List<int> entityIds)
        {
            if (entityIds == null)
            {
                return 0;
            }

            entityIds.Clear();
            try
            {
                PlanetQuality quality = Get(planetId);
                if (quality == null || quality.buildingInc == null)
                {
                    return 0;
                }

                int min = minLevel <= 0 ? 1 : minLevel;
                if (minLevel <= 0)
                {
                    // maxLevel == 0 disables the outline entirely; do not enumerate anything.
                    return 0;
                }

                byte[] inc = quality.buildingInc;
                for (int eid = 1; eid < inc.Length; eid++)
                {
                    if (inc[eid] >= min)
                    {
                        entityIds.Add(eid);
                    }
                }
            }
            catch (System.Exception e)
            {
                QualityEnv.WarnOnce("outline.collect", e);
            }

            return entityIds.Count;
        }
    }
}
