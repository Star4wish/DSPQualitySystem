using System;
using System.Reflection;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// M1 spec 3.4: reimplementation of the Assembler branch of
    /// PlanetFactory.PickFrom(uint ioTargetTypedId, int offset, int filter, int[] needs,
    /// out byte stack, out byte inc) (asm_full.cs:217948-218019). The only behavioural
    /// difference is that the picked product carries the pooled quality level as its inc.
    /// Other IO target types are returned to vanilla untouched.
    /// TargetMethod is used because typeof(byte).MakeByRefType() is not a valid attribute
    /// constant and the attribute form cannot disambiguate the two PickFrom overloads.
    /// The attribute is deliberately bare: BepInEx 5.4.17 ships HarmonyX 2.5.5, whose
    /// PatchClassProcessor rejects a class-level target type combined with TargetMethod()
    /// ("You cannot combine TargetMethod, TargetMethods or PatchAll with individual
    /// annotations"). Bare [HarmonyPatch] + TargetMethod is the portable form that works on
    /// both 2.5.5 (game runtime) and 2.10 (BepInEx 5.4.21 build reference).
    /// </summary>
    [HarmonyPatch]
    internal static class PickFrom_AssemblerInc
    {
        private const uint TypeMask = 0xFF000000u;
        private const uint IdMask = 0xFFFFFFu;

        private static MethodBase TargetMethod()
        {
            return typeof(PlanetFactory).GetMethod("PickFrom",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[]
                {
                    typeof(uint), typeof(int), typeof(int), typeof(int[]),
                    typeof(byte).MakeByRefType(), typeof(byte).MakeByRefType()
                }, null);
        }

        private static bool Prefix(PlanetFactory __instance, uint ioTargetTypedId, int filter, int[] needs,
            out byte stack, out byte inc, ref int __result)
        {
            stack = 1;
            inc = 0;

            if (!QualityConfig.Enabled.Value)
            {
                return true;
            }

            // Declared outside the try so the catch block knows whether produced[] was already
            // decremented (see the double-decrement guard there).
            int picked = 0;
            byte pickedInc = 0;

            try
            {
                if ((EFactoryIOTargetType)(ioTargetTypedId & TypeMask) != EFactoryIOTargetType.Assembler)
                {
                    return true;
                }

                FactorySystem system = __instance.factorySystem;
                int assemblerId = (int)(ioTargetTypedId & IdMask);
                if (system == null || system.assemblerPool == null
                    || assemblerId <= 0 || assemblerId >= system.assemblerPool.Length)
                {
                    return true;
                }

                int entityId = system.assemblerPool[assemblerId].entityId;
                if (entityId <= 0 || __instance.entityMutexs == null
                    || entityId >= __instance.entityMutexs.Length)
                {
                    __result = 0;
                    return false;
                }

                // Vanilla: int num = (int)(ioTargetTypedId & 0xFFFFFF) (asm_full.cs:217952),
                // ref AssemblerComponent reference5 = ref factorySystem.assemblerPool[num]
                // (:217968), entityId6 = reference5.entityId (:217973), then lock (entityMutexs[entityId6]) (:217974).
                int planetId = __instance.planetId;
                if (planetId <= 0)
                {
                    planetId = QualityEnv.CurrentPlanetId;
                }

                lock (__instance.entityMutexs[entityId])
                {
                    // Re-read inside the lock: the pool may have been resized between the
                    // entityId lookup above and taking the mutex.
                    AssemblerComponent current = system.assemblerPool[assemblerId];
                    int[] products = current.recipeExecuteData == null ? null : current.recipeExecuteData.products;
                    int[] produced = current.produced;
                    if (current.id <= 0 || products == null || produced == null)
                    {
                        __result = 0;
                        return false;
                    }

                    // Vanilla decrements produced[j] and returns products[j] while still holding the
                    // lock (:217988-218011). Taking the pooled quality point in the same critical
                    // section keeps "one item == one point" atomic for this assembler; the only
                    // behavioural addition versus vanilla is this TakeOneProductInc call.
                    picked = PickProduct(products, produced, filter, needs);
                    if (picked > 0 && planetId > 0)
                    {
                        pickedInc = (byte)QualityData.TakeOneProductInc(planetId, entityId);
                        // v0.16.0 (Bug 3): PickFrom decrements produced[] between two assembler
                        // ticks; remember the pick so the assembler postfix can add it back to the
                        // produced[] delta instead of losing production to the quality pool.
                        QualityData.NoteProductPicked(planetId, entityId);
                    }
                }

                if (picked <= 0)
                {
                    __result = 0;
                    return false;
                }

                inc = pickedInc;
                __result = picked;
                Diag.Probes.RecordPick(entityId, picked, inc);
                return false;
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("pickfrom", e);
                if (picked > 0)
                {
                    // produced[] was already decremented inside the lock. Returning true would let
                    // vanilla run the whole body again and decrement a second time (duplicating an
                    // item), so hand the already-removed item back without a quality point instead.
                    inc = 0;
                    __result = picked;
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Vanilla assembler branch rewritten as a single loop:
        ///   products.Length == 1 -> case 1 at asm_full.cs:217986-217990
        ///   products.Length == 2 -> case 2 at :217992-218003 (slot 0 first, then slot 1)
        ///   otherwise            -> default at :218004-218014 (slot 0..n-1 in order)
        /// Every case uses the same predicate: produced[j] > 0 && products[j] > 0 &&
        /// (filter == 0 || filter == products[j]) && (needs == null || any needs[0..5] == products[j]);
        /// the first matching slot wins and is decremented before products[j] is returned.
        /// Deviation: vanilla indexes produced[j] up to products.Length without a bounds check; here
        /// the loop stops at min(products.Length, produced.Length) (they are equal in practice).
        /// </summary>
        private static int PickProduct(int[] products, int[] produced, int filter, int[] needs)
        {
            int count = products.Length;
            if (produced.Length < count)
            {
                count = produced.Length;
            }

            for (int i = 0; i < count; i++)
            {
                int product = products[i];
                if (product <= 0 || produced[i] <= 0)
                {
                    continue;
                }
                if (filter != 0 && filter != product)
                {
                    continue;
                }
                if (!NeedsMatch(needs, product))
                {
                    continue;
                }

                produced[i]--;
                return product;
            }
            return 0;
        }

        /// <summary>
        /// Vanilla compares needs[0..5] slot by slot inside the same predicate (e.g. asm_full.cs:217986).
        /// It dereferences needs[0..5] unconditionally once needs != null (so a shorter array would
        /// throw in vanilla); this helper clamps to min(needs.Length, 6) and is therefore safe.
        /// </summary>
        private static bool NeedsMatch(int[] needs, int itemId)
        {
            if (needs == null)
            {
                return true;
            }
            int count = needs.Length < 6 ? needs.Length : 6;
            for (int i = 0; i < count; i++)
            {
                if (needs[i] == itemId)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
