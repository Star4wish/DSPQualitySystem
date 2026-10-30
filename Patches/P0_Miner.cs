using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// M3 spec 3.5: miner output quality. In
    /// MinerComponent.InternalUpdate(PlanetFactory, VeinData[], float, float, float, int[])
    /// (asm_full.cs:57199) the mined items are flushed with
    /// factory.InsertInto(insertTarget, 0, productId, (byte)num17, 0, out remainInc)
    /// (asm_full.cs:57429) - note the hard-coded 0 for itemInc. The transpiler replaces that
    /// constant with num17 * QualityData.MinerOutputInc(factory, entityId) and a conv.u1.
    ///
    /// Why the count multiplier is required (Lead review, DEVLOG difference #13): itemInc is the
    /// TOTAL quality points of the whole stack, not the per-item value. InsertInto's belt branch
    /// goes to CargoTraffic.TryInsertItem(beltId, offset, itemId, itemCount, itemInc) ->
    /// CargoPath.TryInsertItem -> InsertItemDirect (asm_full.cs:83567, :216912), and the assembler
    /// branch does incServed[i] += itemInc (divided by served later). Passing level=1 for a
    /// 4-item stack gives 0.25 point/item, which truncates to 0 and silently loses the quality.
    /// Correct value is level * num17.
    ///
    /// No chunking is needed: num17 is clamped to 1..4 by vanilla (asm_full.cs:57425-57427) and
    /// MinerOutputInc returns 0 or 1, so level * count <= 4 (future multi-point levels are bounded
    /// by QualityConfig.MaxLevel <= Cargo.kIncLevelMax = 10, so 10 * 4 = 40) - far below the
    /// byte limit of the InsertInto(..., byte itemInc, ...) argument.
    ///
    /// Degradation policy (self-audit, DEVLOG section 10): every failure branch below logs one
    /// WARNING and returns the original instruction list unchanged - a half-installed or
    /// throwing hook must never break the miner tick.
    /// </summary>
    [HarmonyPatch(typeof(MinerComponent), "InternalUpdate", new[]
    {
        typeof(PlanetFactory), typeof(VeinData[]), typeof(float), typeof(float), typeof(float), typeof(int[])
    })]
    internal static class Miner_InternalUpdate_OutputInc
    {
        private static readonly MethodInfo InsertIntoInt = typeof(PlanetFactory).GetMethod("InsertInto",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
            new[] { typeof(int), typeof(int), typeof(int), typeof(byte), typeof(byte), typeof(byte).MakeByRefType() },
            null);

        private static readonly MethodInfo Helper = typeof(QualityData).GetMethod("MinerOutputInc",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null,
            new[] { typeof(PlanetFactory), typeof(int) }, null);

        private static readonly FieldInfo EntityIdField = typeof(MinerComponent).GetField("entityId",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            try
            {
                return Transpile(codes);
            }
            catch (Exception e)
            {
                // Never let a malformed IL layout or a Harmony edge case escape: an exception thrown
                // from a transpiler would abort patching (and depending on ordering could take other
                // patches down with it), while returning the untouched list simply disables M3.
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning("[QSystem][miner] transpiler disabled, keeping vanilla IL: " + e);
                }
                return codes;
            }
        }

        private static IEnumerable<CodeInstruction> Transpile(List<CodeInstruction> codes)
        {
            if (InsertIntoInt == null || Helper == null || EntityIdField == null)
            {
                Warn("reflection lookup failed (InsertInto/Helper/entityId), miner quality disabled");
                return codes;
            }

            int patched = 0;
            for (int i = 2; i < codes.Count; i++)
            {
                CodeInstruction call = codes[i];
                if (call.opcode != System.Reflection.Emit.OpCodes.Call
                    && call.opcode != System.Reflection.Emit.OpCodes.Callvirt)
                {
                    continue;
                }

                if (!(call.operand is MethodInfo method) || method != InsertIntoInt)
                {
                    continue;
                }

                // Expected stack layout right before the call:
                //   ... ldc.i4.0 (itemInc = 0), ldloca remainInc, call InsertInto
                // At this point the stack already holds the itemCount argument (num17, from
                // "(byte)num17"), so Dup duplicates it and Mul turns the per-item level into the
                // total stack inc required by InsertInto.
                bool directConstant = codes[i - 2].opcode == System.Reflection.Emit.OpCodes.Ldc_I4_0;
                bool localAddress = codes[i - 1].opcode == System.Reflection.Emit.OpCodes.Ldloca_S
                    || codes[i - 1].opcode == System.Reflection.Emit.OpCodes.Ldloca;
                if (!directConstant || !localAddress)
                {
                    Warn("unexpected IL around miner output InsertInto (itemInc arg), miner quality disabled");
                    return codes;
                }

                var injected = new List<CodeInstruction>
                {
                    new CodeInstruction(System.Reflection.Emit.OpCodes.Dup),      // itemCount (num17)
                    new CodeInstruction(System.Reflection.Emit.OpCodes.Ldarg_1),  // PlanetFactory factory
                    new CodeInstruction(System.Reflection.Emit.OpCodes.Ldarg_0),  // ref MinerComponent
                    new CodeInstruction(System.Reflection.Emit.OpCodes.Ldfld, EntityIdField),
                    new CodeInstruction(System.Reflection.Emit.OpCodes.Call, Helper), // level 0/1
                    new CodeInstruction(System.Reflection.Emit.OpCodes.Mul),      // total = count * level
                    new CodeInstruction(System.Reflection.Emit.OpCodes.Conv_U1)
                };

                codes.RemoveAt(i - 2);
                codes.InsertRange(i - 2, injected);
                patched++;
                break; // only one output InsertInto in this method
            }

            if (patched == 0)
            {
                Warn("output InsertInto call not found, miner quality disabled");
            }
            else if (Plugin.Log != null)
            {
                Plugin.Log.LogInfo("[QSystem][miner] output inc hook installed (" + patched + " site).");
            }

            return codes;
        }

        private static void Warn(string message)
        {
            if (Plugin.Log != null)
            {
                Plugin.Log.LogWarning("[QSystem][miner] " + message);
            }
        }
    }
}
