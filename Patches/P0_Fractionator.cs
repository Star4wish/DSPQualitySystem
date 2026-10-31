using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// M4/P2: fractionator product quality (spec section 3.11, DEVLOG section 12).
    ///
    /// Real IL was dumped from the shipped Assembly-CSharp.dll (tools\il.exe, UTF-8 copy in
    /// tools\tmp\fractionator_iu_utf8.txt). Method signature:
    ///   System.UInt32 FractionatorComponent::InternalUpdate(PlanetFactory, System.Single,
    ///                                                       SignData[], System.Int32[], System.Int32[])
    /// FractionatorComponent is a value type, so ldarg.0 is a managed pointer to the component.
    ///
    /// Site A - IL 0x0197-0x01A0, inside the "one cycle succeeded" branch:
    ///   ldarg.0; ldarg.0; ldfld productOutputTotal; ldc.i4.1; add; stfld productOutputTotal
    /// followed by the productRegister lock. The product is only counted, it carries no quality
    /// (vanilla still writes the plain item id), so we inject right after the stfld:
    ///   ldarg.1; ldarg.0; ldfld entityId;
    ///   ldarg.0; ldfld fluidInputInc; ldarg.0; ldfld fluidInputCount;
    ///   call QualityData.FractionatorProduceInc(PlanetFactory,int,int,int); pop
    /// The helper recomputes num3 = fluidInputInc / fluidInputCount from the fields instead of
    /// reading the vanilla local: the local's stack slot is an implementation detail (DEVLOG
    /// 12.3), the struct fields are not.
    ///
    /// Site B - IL 0x0656-0x066A, the only product output path:
    ///   ldloc.3 (CargoTraffic); ldarg.0; ldfld belt0; ldarg.0; ldfld productId;
    ///   ldc.i4.1 (count); ldc.i4.0 (inc); callvirt TryInsertItemAtHead(int,int,byte,byte);
    ///   brfalse IL_067a (a failed insert skips the productOutputCount-- below)
    /// The hard-coded 0 is the item inc. The design draft (DEVLOG 12.2) replaces only that byte
    /// constant. We replace the count+inc+call triple with
    ///   ldarg.1; ldarg.0; ldfld entityId; call QualityData.TryInsertFractionatorProduct(...)
    /// so that "take one pool point" and "insert the product" are a single atomic call: the
    /// helper returns the vanilla insert result and puts the point back when the belt refuses it.
    /// The draft's ordering would consume one point per failed attempt, contradicting the
    /// documented intent that a failed insert leaves the pool untouched (DEVLOG 12.3).
    ///
    /// Both anchors are located and validated first, then applied to a copy of the list. Any
    /// mismatch, reflection failure or exception returns the untouched instruction list and logs
    /// exactly one warning - the fractionator must never break because of this mod (degradation
    /// policy, DEVLOG section 10).
    /// </summary>
    [HarmonyPatch(typeof(FractionatorComponent), "InternalUpdate", new[]
    {
        typeof(PlanetFactory), typeof(float), typeof(SignData[]), typeof(int[]), typeof(int[])
    })]
    internal static class Fractionator_InternalUpdate_ProductInc
    {
        internal static bool Installed;
        internal static int SiteA;
        internal static int SiteB;
        internal static string FailureReason = "not run";

        private static bool _warned;

        private static readonly FieldInfo EntityIdField = Field("entityId");
        private static readonly FieldInfo FluidInputIncField = Field("fluidInputInc");
        private static readonly FieldInfo FluidInputCountField = Field("fluidInputCount");
        private static readonly FieldInfo ProductOutputTotalField = Field("productOutputTotal");
        private static readonly FieldInfo ProductIdField = Field("productId");
        private static readonly FieldInfo Belt0Field = Field("belt0");

        private static readonly MethodInfo ProduceHelper = typeof(QualityData).GetMethod(
            "FractionatorProduceInc",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null,
            new[] { typeof(PlanetFactory), typeof(int), typeof(int), typeof(int) }, null);

        private static readonly MethodInfo ShipHelper = typeof(QualityData).GetMethod(
            "TryInsertFractionatorProduct",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null,
            new[] { typeof(CargoTraffic), typeof(int), typeof(int), typeof(PlanetFactory), typeof(int) },
            null);

        private static readonly MethodInfo TryInsertMethod = typeof(CargoTraffic).GetMethod(
            "TryInsertItemAtHead",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
            new[] { typeof(int), typeof(int), typeof(byte), typeof(byte) }, null);

        private static FieldInfo Field(string name)
        {
            return typeof(FractionatorComponent).GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            try
            {
                return Transpile(codes);
            }
            catch (Exception e)
            {
                Fail("transpiler exception: " + e);
                return codes;
            }
        }

        private static List<CodeInstruction> Transpile(List<CodeInstruction> codes)
        {
            if (ProduceHelper == null || ShipHelper == null || TryInsertMethod == null
                || EntityIdField == null || FluidInputIncField == null || FluidInputCountField == null
                || ProductOutputTotalField == null || ProductIdField == null || Belt0Field == null)
            {
                Fail("reflection lookup failed");
                return codes;
            }

            // ---- site A: the unique productOutputTotal++ ----
            int siteA = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction ci = codes[i];
                if (ci.opcode == OpCodes.Stfld && Equals(ci.operand, ProductOutputTotalField))
                {
                    if (siteA >= 0)
                    {
                        Fail("multiple productOutputTotal writes");
                        return codes;
                    }
                    siteA = i;
                }
            }
            if (siteA < 5)
            {
                Fail("productOutputTotal write not found");
                return codes;
            }
            if (codes[siteA - 1].opcode != OpCodes.Add
                || codes[siteA - 2].opcode != OpCodes.Ldc_I4_1
                || codes[siteA - 3].opcode != OpCodes.Ldfld
                || !Equals(codes[siteA - 3].operand, ProductOutputTotalField)
                || codes[siteA - 4].opcode != OpCodes.Ldarg_0
                || codes[siteA - 5].opcode != OpCodes.Ldarg_0)
            {
                Fail("unexpected IL around productOutputTotal write");
                return codes;
            }

            // ---- site B: the unique product output insert ----
            int call = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction ci = codes[i];
                if ((ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call)
                    && Equals(ci.operand, TryInsertMethod))
                {
                    if (call >= 0)
                    {
                        Fail("multiple TryInsertItemAtHead calls");
                        return codes;
                    }
                    call = i;
                }
            }
            int b = call - 2; // ldc.i4.1 (itemCount), ldc.i4.0 (itemInc)
            if (call < 0)
            {
                Fail("TryInsertItemAtHead call not found");
                return codes;
            }
            if (b < 7
                || codes[b].opcode != OpCodes.Ldc_I4_1
                || codes[b + 1].opcode != OpCodes.Ldc_I4_0
                || codes[call - 3].opcode != OpCodes.Ldfld
                || !Equals(codes[call - 3].operand, ProductIdField)
                || codes[call - 4].opcode != OpCodes.Ldarg_0
                || codes[call - 5].opcode != OpCodes.Ldfld
                || !Equals(codes[call - 5].operand, Belt0Field)
                || codes[call - 6].opcode != OpCodes.Ldarg_0
                || !IsLocal3(codes[call - 7]))
            {
                Fail("unexpected IL around TryInsertItemAtHead");
                return codes;
            }
            if (siteA >= b)
            {
                Fail("anchor order unexpected");
                return codes;
            }

            // A replaced instruction must not open an exception block: the block range would then
            // point at an instruction that no longer exists.
            for (int i = b; i <= call; i++)
            {
                if (codes[i].blocks != null && codes[i].blocks.Count > 0)
                {
                    Fail("unexpected exception block at TryInsertItemAtHead");
                    return codes;
                }
            }

            // ---- site B first (it is later in the list, so siteA keeps its index) ----
            // Stack already holds cargoTraffic, belt0, productId in helper argument order.
            var ship = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_1),               // PlanetFactory factory
                new CodeInstruction(OpCodes.Ldarg_0),               // &this
                new CodeInstruction(OpCodes.Ldfld, EntityIdField),  // entityId
                new CodeInstruction(OpCodes.Call, ShipHelper)       // bool = vanilla insert result
            };
            ship[0].labels.AddRange(codes[b].labels);
            ship[0].labels.AddRange(codes[b + 1].labels);
            ship[0].labels.AddRange(codes[call].labels);
            codes.RemoveRange(b, 3);
            codes.InsertRange(b, ship);

            // ---- site A (index untouched by the site B edit) ----
            var produce = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_1),                    // PlanetFactory factory
                new CodeInstruction(OpCodes.Ldarg_0),                    // &this
                new CodeInstruction(OpCodes.Ldfld, EntityIdField),       // entityId
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, FluidInputIncField),  // fluidInputInc
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, FluidInputCountField),// fluidInputCount
                new CodeInstruction(OpCodes.Call, ProduceHelper),        // int level, only for probes
                new CodeInstruction(OpCodes.Pop)
            };
            codes.InsertRange(siteA + 1, produce);

            Installed = true;
            SiteA = 1;
            SiteB = 1;
            FailureReason = string.Empty;
            if (Plugin.Log != null)
            {
                Plugin.Log.LogInfo("[QSystem][fractionator] anchors ok, pooled=on siteA=1 siteB=1 "
                    + "(quality inc injected; belt insert is atomic take/rollback)");
            }
            return codes;
        }

        private static bool IsLocal3(CodeInstruction ci)
        {
            if (ci.opcode == OpCodes.Ldloc_3)
            {
                return true;
            }
            if (ci.opcode == OpCodes.Ldloc_S || ci.opcode == OpCodes.Ldloc)
            {
                var local = ci.operand as LocalBuilder;
                return local != null && local.LocalIndex == 3;
            }
            return false;
        }

        private static void Fail(string reason)
        {
            FailureReason = reason;
            Installed = false;
            SiteA = 0;
            SiteB = 0;
            if (_warned)
            {
                return;
            }
            _warned = true;
            if (Plugin.Log != null)
            {
                Plugin.Log.LogWarning("[QSystem][fractionator] anchor NOT FOUND (" + reason
                    + "), vanilla fallback: fractionator quality disabled");
            }
        }

        /// <summary>One-line anchor status for StartupAudit (the transpiler already ran in Awake).</summary>
        internal static string Describe()
        {
            if (Installed)
            {
                return "-> OK (siteA=" + SiteA + " siteB=" + SiteB + ", pooled=on)";
            }
            return "-> NOT INSTALLED (vanilla fallback, reason: " + FailureReason + ")";
        }
    }

    /// <summary>
    /// M4/P2: a new fractionator recipe must not inherit the product pool of the previous one
    /// (DEVLOG 12.3). Vanilla only calls SetRecipe when the machine is completely empty
    /// (fluidId == 0 at IL_067a), i.e. after all queued products have been shipped, so clearing
    /// here cannot throw away points of items that are still inside the machine.
    /// </summary>
    [HarmonyPatch(typeof(FractionatorComponent), "SetRecipe", new[] { typeof(int), typeof(SignData[]) })]
    internal static class Fractionator_SetRecipe_ClearPool
    {
        private static bool _loggedFirstCall;

        private static void Prefix(ref FractionatorComponent __instance)
        {
            try
            {
                int planetId = QualityEnv.CurrentPlanetId;
                if (planetId < 0)
                {
                    return;
                }
                QualityData.ClearPool(planetId, __instance.entityId);
                if (!_loggedFirstCall)
                {
                    _loggedFirstCall = true;
                    if (Plugin.Log != null)
                    {
                        Plugin.Log.LogInfo("[QSystem][fractionator] SetRecipe prefix active, "
                            + "pool cleared on recipe change (first call planet=" + planetId
                            + " entity=" + __instance.entityId + ")");
                    }
                }
            }
            catch (Exception e)
            {
                QualityEnv.WarnOnce("fractionator.clearpool", e);
            }
        }
    }
}
