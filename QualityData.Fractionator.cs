namespace DSPQualitySystem
{
    /// <summary>
    /// M4/P2 fractionator quality (spec section 3.11, DEVLOG section 12): produce/ship pool
    /// hooks and the recipe-change pool reset. Split from QualityData.cs to keep every file
    /// under the project convention of 500 lines; the storage class itself is partial.
    /// </summary>
    public static partial class QualityData
    {
        /// <summary>
        /// M4/P2 hook called from the FractionatorComponent.InternalUpdate transpiler immediately
        /// after a successful production cycle (productOutputTotal++, asm_full.cs:53736-53741).
        /// The cycle inherits the input quality level num3 = fluidInputInc / fluidInputCount; the
        /// level is recomputed from the fields (never read from the vanilla local, DEVLOG 12.3),
        /// R-1 + R-2 decide the product level, and one single-item product of that level enters the
        /// pool so the output path can stamp it on the shipped item. Returns the credited level
        /// (0 when disabled or out of range); the caller only pops it.
        /// </summary>
        public static int FractionatorProduceInc(PlanetFactory factory, int entityId,
            int fluidInputInc, int fluidInputCount)
        {
            try
            {
                if (!QualityConfig.Enabled.Value || factory == null || entityId < 0)
                {
                    return 0;
                }
                int planetId = factory.planetId;
                if (planetId < 0)
                {
                    return 0;
                }
                int inputLevel = QualityMath.FractionatorInputLevel(fluidInputInc, fluidInputCount,
                    QualityConfig.MaxLevel);
                int level = QualityRules.RollCycleQuality(planetId, entityId, inputLevel, false);
                AddPool(planetId, entityId, level, 1);
                return level;
            }
            catch (System.Exception e)
            {
                QualityEnv.WarnOnce("fractionator.produce", e);
                return 0;
            }
        }

        /// <summary>
        /// M4/P2 atomic ship hook (replaces cargoTraffic.TryInsertItemAtHead(belt0, productId, 1, 0)
        /// in FractionatorComponent.InternalUpdate, asm_full.cs:53866). Takes one point out of the
        /// pool, inserts the product with that inc, and puts the point back when the belt refuses
        /// the item. The return value is exactly the vanilla insert result, so the surrounding
        /// "if (TryInsertItemAtHead(...)) productOutputCount--;" control flow is unchanged.
        ///
        /// Why take+insert are one call instead of only replacing the hard-coded 0 (design draft
        /// DEVLOG 12.2): the design's ordering would consume a pool point on every failed insert
        /// although DEVLOG 12.3 states the pool must be untouched when the item cannot be inserted.
        /// </summary>
        public static bool TryInsertFractionatorProduct(CargoTraffic traffic, int beltId, int productId,
            PlanetFactory factory, int entityId)
        {
            if (traffic == null)
            {
                return false;
            }

            bool took = false;
            int planetId = -1;
            int inc = 0;
            try
            {
                if (!QualityConfig.Enabled.Value || factory == null || entityId < 0)
                {
                    return traffic.TryInsertItemAtHead(beltId, productId, 1, 0);
                }
                planetId = factory.planetId;
                if (planetId < 0)
                {
                    return traffic.TryInsertItemAtHead(beltId, productId, 1, 0);
                }
                inc = TakeOneProductInc(planetId, entityId);
                took = inc > 0;
                bool inserted = traffic.TryInsertItemAtHead(beltId, productId, 1, (byte)inc);
                if (!inserted && took)
                {
                    // Belt full: the product stays in productOutputCount and is retried next tick,
                    // so the point has to go back to the pool (atomic take/rollback).
                    AddPool(planetId, entityId, inc, 1);
                }
                return inserted;
            }
            catch (System.Exception e)
            {
                if (took)
                {
                    try
                    {
                        AddPool(planetId, entityId, inc, 1);
                    }
                    catch (System.Exception)
                    {
                        // never let rollback bookkeeping break the factory tick
                    }
                }
                QualityEnv.WarnOnce("fractionator.ship", e);
                return false;
            }
        }

        /// <summary>P2: a new fractionator recipe must not inherit the pool of the previous one.</summary>
        public static void ClearPool(int planetId, int entityId)
        {
            PlanetQuality quality = Get(planetId);
            if (quality == null || entityId < 0 || entityId >= quality.poolCount.Length)
            {
                return;
            }
            quality.poolInc[entityId] = 0;
            quality.poolCount[entityId] = 0;
        }
    }
}