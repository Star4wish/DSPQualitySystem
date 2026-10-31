using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// P3 stage 2d: dismantled buildings hand their quality points back to the inventory
    /// (DEVLOG section 19). The vanilla GUI dismantle path
    /// PlayerAction_Build.DoDismantleObject(int objId) (asm_full.cs:163889) calls
    /// player.TryAddItemToPackage(itemId, count, 0, throwTrash: true, objId) at asm_full.cs:163928
    /// with the inc argument hard-coded to 0. The transpiler replaces that single ldc.i4.0 with a
    /// call to QualityData.DismantleInc(factory, objId); the add-item call happens BEFORE
    /// factory.DismantleFinally(...), so reading the stored points here is safe and the later
    /// ClearEntity cleanup does not race the refund.
    ///
    /// Registration is manual (harmony.Patch) instead of a [HarmonyPatch] attribute so that a
    /// binding/anchor failure is isolated to this feature on the older runtime HarmonyX and can
    /// never abort the assembly-wide PatchAll. Anchor failure = vanilla inc 0, warn once only.
    /// </summary>
    internal static class P3_Dismantle
    {
        private const string TargetTypeName = "PlayerAction_Build";
        private const string TargetMethodName = "DoDismantleObject";
        private const string TryAddMethodName = "TryAddItemToPackage";

        private static bool _armed;
        private static bool _warned;
        private static string _reason;

        private static MethodInfo _tryAddMethod;
        private static FieldInfo _factoryField;
        private static MethodInfo _incHelper;

        internal static bool Armed
        {
            get { return _armed; }
        }

        /// <summary>Register the DoDismantleObject transpiler; failures keep vanilla inc=0.</summary>
        internal static void Apply(Harmony harmony)
        {
            try
            {
                if (harmony == null)
                {
                    SetReason("harmony instance is null");
                    return;
                }
                if (QualityConfig.Enabled == null || !QualityConfig.Enabled.Value)
                {
                    SetReason("master enabled=false");
                    return;
                }
                if (QualityConfig.GiveDismantleInc == null || !QualityConfig.GiveDismantleInc.Value)
                {
                    SetReason("giveDismantleInc disabled by config");
                    return;
                }

                MethodInfo target = typeof(PlayerAction_Build).GetMethod(TargetMethodName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new[] { typeof(int) }, null);
                MethodInfo transpiler = typeof(P3_Dismantle).GetMethod("Transpiler",
                    BindingFlags.NonPublic | BindingFlags.Static);
                _factoryField = typeof(PlayerAction_Build).GetField("factory",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                _tryAddMethod = typeof(Player).GetMethod(TryAddMethodName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new[]
                    {
                        typeof(int), typeof(int), typeof(int), typeof(bool), typeof(int), typeof(bool)
                    },
                    null);
                _incHelper = typeof(QualityData).GetMethod("DismantleInc",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null, new[] { typeof(PlanetFactory), typeof(int) }, null);

                string missing = null;
                if (target == null) missing = "target " + TargetTypeName + "." + TargetMethodName + "(int)";
                else if (transpiler == null) missing = "transpiler";
                else if (_factoryField == null) missing = TargetTypeName + ".factory field";
                else if (_tryAddMethod == null) missing = "Player." + TryAddMethodName + "(int,int,int,bool,int,bool)";
                else if (_incHelper == null) missing = "QualityData.DismantleInc(PlanetFactory,int)";
                if (missing != null)
                {
                    SetReason("reflection missing: " + missing);
                    Warn();
                    return;
                }

                harmony.Patch(target, transpiler: new HarmonyMethod(transpiler));
                _armed = true;
                _reason = null;
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogInfo("[QSystem][dismantle] patch registered: PlayerAction_Build."
                        + TargetMethodName + " anchor ok (inc argument now comes from building quality)");
                }
            }
            catch (Exception e)
            {
                SetReason("apply exception: " + e.GetType().Name);
                Warn();
            }
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
                SetReason("transpiler exception: " + e.GetType().Name);
                Warn();
                return codes;
            }
        }

        private static List<CodeInstruction> Transpile(List<CodeInstruction> codes)
        {
            if (codes == null || codes.Count == 0)
            {
                SetReason("empty IL stream");
                Warn();
                return codes;
            }
            if (_factoryField == null || _tryAddMethod == null || _incHelper == null)
            {
                SetReason("static reflection not initialised");
                Warn();
                return codes;
            }

            int callIndex = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction ci = codes[i];
                if (ci.opcode != OpCodes.Call && ci.opcode != OpCodes.Callvirt)
                {
                    continue;
                }
                if (!Equals(ci.operand, _tryAddMethod))
                {
                    continue;
                }
                if (callIndex >= 0)
                {
                    SetReason("ambiguous " + TryAddMethodName + " call site");
                    Warn();
                    return codes;
                }
                callIndex = i;
            }

            if (callIndex < 4)
            {
                SetReason("no " + TryAddMethodName + " call site found");
                Warn();
                return codes;
            }

            // Vanilla shape expected right before the call:
            //   callIndex-4: ldc.i4.0   <- the inc argument we replace
            //   callIndex-3: ldc.i4.1   (throwTrash: true)
            //   callIndex-2: ldarg.1    (objId)
            //   callIndex-1: ldc.i4.0   (trashLife)
            bool shapeOk = codes[callIndex - 4].opcode == OpCodes.Ldc_I4_0
                && codes[callIndex - 3].opcode == OpCodes.Ldc_I4_1
                && codes[callIndex - 2].opcode == OpCodes.Ldarg_1
                && codes[callIndex - 1].opcode == OpCodes.Ldc_I4_0;
            if (!shapeOk)
            {
                SetReason("inc argument shape mismatch before " + TryAddMethodName);
                Warn();
                return codes;
            }

            CodeInstruction incArg = codes[callIndex - 4];
            if (incArg.blocks != null && incArg.blocks.Count > 0)
            {
                SetReason("inc argument instruction starts an exception block");
                Warn();
                return codes;
            }

            var injected = new List<CodeInstruction>(4)
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, _factoryField),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call, _incHelper)
            };
            // Preserve branch targets: anything that jumped at the inc argument must still land on
            // the first instruction of the replacement sequence.
            injected[0].labels.AddRange(incArg.labels);

            codes.RemoveAt(callIndex - 4);
            codes.InsertRange(callIndex - 4, injected);

            if (Plugin.Log != null)
            {
                Plugin.Log.LogInfo("[QSystem][dismantle] anchor ok: DoDismantleObject inc argument"
                    + " replaced (was ldc.i4.0, now QualityData.DismantleInc)");
            }
            return codes;
        }

        /// <summary>One-line startup-audit status (never throws).</summary>
        internal static string Describe()
        {
            try
            {
                if (QualityConfig.GiveDismantleInc == null || !QualityConfig.GiveDismantleInc.Value)
                {
                    return "dismantle inc NOT ARMED (vanilla inc=0, reason: giveDismantleInc disabled by config)";
                }
                if (!_armed)
                {
                    string reason = string.IsNullOrEmpty(_reason) ? "not applied yet" : _reason;
                    return "dismantle inc NOT ARMED (vanilla inc=0, reason: " + reason + ")";
                }
                return "dismantle inc armed (giveDismantleInc=true; PlayerAction_Build."
                    + TargetMethodName + " inc argument reads the building quality points)";
            }
            catch (Exception)
            {
                return "dismantle inc NOT ARMED (vanilla inc=0, reason: describe failed)";
            }
        }

        private static void SetReason(string reason)
        {
            if (string.IsNullOrEmpty(_reason))
            {
                _reason = reason;
            }
        }

        private static void Warn()
        {
            if (_warned)
            {
                return;
            }
            _warned = true;
            if (Plugin.Log != null && _reason != null)
            {
                Plugin.Log.LogWarning("[QSystem][dismantle] NOT ARMED (vanilla inc=0): " + _reason);
            }
        }
    }
}
