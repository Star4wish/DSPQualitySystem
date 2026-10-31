using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.12.0 (task-16 A5): a high-quality spray coater gives the sprayed item one extra quality
    /// point with a chance derived from the sprayer building's quality points (shared curve:
    /// 0 points = 0%, 1..9 points = 2.5..60%, 10 points = 100%).
    ///
    /// Anchor: <c>SpraycoaterComponent.InternalUpdate(CargoTraffic, AnimData[], int[])</c>
    /// (asm_full.cs:73562). The vanilla write is asm_full.cs:73648
    /// <c>cargoPath2.cargoContainer.cargoPool[cargoId2].inc = (byte)(num5 * incAbility);</c>
    /// where <c>num5</c> is the sprayed item count and <c>incAbility</c> the item's spray points.
    /// The transpiler inserts exactly three instructions between the <c>mul</c> and the following
    /// <c>conv.u1</c>: <c>ldarg.0; ldfld SpraycoaterComponent::entityId;
    /// call QualityData.SprayBonusPoints(int,int)</c>. Existing instructions and their branch targets
    /// are never moved or removed, so every other path of the method stays byte-identical.
    ///
    /// Why a value postfix instead of touching incAbility: incAbility is a serialized building field
    /// and the roll must happen per spray event, not once per building. Item points are capped at
    /// <c>Cargo.kIncLevelMax</c> (=10) inside the pure helper, and any failure returns the vanilla
    /// value, so spraying can never lose points.
    ///
    /// The switch is hard: <c>sprayerIncBonus=false</c> means the transpiler is not registered, so the
    /// vanilla IL runs untouched.
    /// </summary>
    internal static class P5_Sprayer
    {
        private static bool _armed;
        private static string _reason;
        private static int _sites;
        private static long _hits;
        private static long _sprayed;
        private static int _logged;

        internal static bool Armed
        {
            get { return _armed; }
        }

        internal static void Apply(Harmony harmony)
        {
            try
            {
                if (QualityConfig.SprayerIncBonus == null || !QualityConfig.SprayerIncBonus.Value)
                {
                    Disable("sprayerIncBonus disabled by config");
                    return;
                }
                if (harmony == null)
                {
                    Disable("harmony instance missing");
                    return;
                }

                MethodInfo target = typeof(SpraycoaterComponent).GetMethod("InternalUpdate",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { typeof(CargoTraffic), typeof(AnimData[]), typeof(int[]) }, null);
                MethodInfo transpiler = typeof(P5_Sprayer).GetMethod("Transpiler",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (target == null || transpiler == null)
                {
                    Disable("reflection miss (target=" + (target != null) + ", transpiler=" + (transpiler != null) + ")");
                    return;
                }

                harmony.Patch(target, transpiler: new HarmonyMethod(transpiler));
                _armed = true;
                P5Common.Info("[QSystem][sprayer] patch registered: SpraycoaterComponent.InternalUpdate"
                    + " transpiler=SprayerTranspiler (inc = SprayBonusPoints(inc, entityId))");
                if (_sites <= 0)
                {
                    _armed = false;
                    Disable("transpiler ran but the (num5 * incAbility) byte write site was not found");
                    return;
                }
                P5Common.Info("[QSystem][sprayer] armed (sprayerIncBonus=True, patch points=" + _sites + ")");
            }
            catch (Exception e)
            {
                Disable(e.ToString());
            }
        }

        private static void Disable(string reason)
        {
            _reason = reason;
            _armed = false;
            P5Common.WarnOnce("sprayer.install", reason);
            P5Common.Info("[QSystem][sprayer] NOT ARMED (vanilla spray result): " + reason);
        }

        /// <summary>
        /// Finds the unique <c>ldfld incAbility</c> + <c>mul</c> + <c>conv.u1</c> triple (the
        /// asm_full.cs:73648 byte write) and inserts the quality roll between the multiply and the
        /// narrowing conversion. The other <c>incAbility</c> use (:73644 comparison and :73689
        /// scrape cost) has no <c>conv.u1</c> directly after the multiply, so it never matches.
        /// </summary>
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            try
            {
                FieldInfo entityField = typeof(SpraycoaterComponent).GetField("entityId",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo bonus = typeof(QualityData).GetMethod("SprayBonusPoints",
                    BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(int), typeof(int) }, null);
                if (entityField == null || bonus == null)
                {
                    P5Common.WarnOnce("sprayer.transpiler",
                        "reflection miss (entityId=" + (entityField != null) + ", bonus=" + (bonus != null) + ")");
                    return list;
                }

                // Candidate write sites: the operand of `conv.u1` is the byte truncation of
                // (num5 * incAbility) at asm_full.cs:73648. The Mono compiler may order the two
                // operands either way, so do not pin the exact 3-instruction sequence: find every
                // `mul; conv.u1` whose feeding window loads SpraycoaterComponent.incAbility.
                var candidates = new List<int>();
                for (int i = 1; i < list.Count; i++)
                {
                    if (list[i].opcode != OpCodes.Conv_U1 || list[i - 1].opcode != OpCodes.Mul)
                    {
                        continue;
                    }
                    int from = Math.Max(0, i - 5);
                    bool loadsAbility = false;
                    for (int j = from; j < i; j++)
                    {
                        var field = list[j].operand as FieldInfo;
                        if (list[j].opcode == OpCodes.Ldfld && field != null && field.Name == "incAbility"
                            && field.DeclaringType == typeof(SpraycoaterComponent))
                        {
                            loadsAbility = true;
                            break;
                        }
                    }
                    if (loadsAbility)
                    {
                        candidates.Add(i);
                    }
                }
                // Fallback: the method has exactly one `mul; conv.u1` byte narrowing in total (the
                // :73644 comparison uses cgt, the :73689 scrape cost has no narrowing cast).
                if (candidates.Count == 0)
                {
                    for (int i = 1; i < list.Count; i++)
                    {
                        if (list[i].opcode == OpCodes.Conv_U1 && list[i - 1].opcode == OpCodes.Mul)
                        {
                            candidates.Add(i);
                        }
                    }
                }

                int inserted = 0;
                if (candidates.Count == 1)
                {
                    int at = candidates[0];
                    list.InsertRange(at, new[]
                    {
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Ldfld, entityField),
                        new CodeInstruction(OpCodes.Call, bonus)
                    });
                    inserted = 1;
                    P5Common.Info("[QSystem][sprayer] transpiler matched the byte-write site at instruction #"
                        + at + " (inc = SprayBonusPoints(inc, entityId))");
                }

                _sites = inserted;
                if (inserted != 1)
                {
                    P5Common.WarnOnce("sprayer.transpiler.sites", "expected 1 byte-write site, candidates="
                        + candidates.Count + "; spray bonus falls back to the vanilla result");
                }
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("sprayer.transpiler", e.ToString());
            }
            return list;
        }

        /// <summary>Called by <see cref="QualityData.SprayBonusPoints"/> through the inserted call.</summary>
        internal static void NoteSpray(int before, int after)
        {
            try
            {
                if (after != before)
                {
                    _hits++;
                    P5Common.Debug(ref _logged, "[QSystem][sprayer] +1 quality point: inc " + before
                        + " -> " + after);
                }
                _sprayed++;
            }
            catch (Exception)
            {
                // diagnostics only
            }
        }

        internal static string Describe()
        {
            try
            {
                if (QualityConfig.SprayerIncBonus == null || !QualityConfig.SprayerIncBonus.Value)
                {
                    return "sprayer inc bonus NOT ARMED (vanilla spray result, reason: sprayerIncBonus disabled)";
                }
                if (!_armed)
                {
                    return "sprayer inc bonus NOT ARMED (vanilla spray result, reason: "
                        + (_reason ?? "not applied yet") + ")";
                }
                return "sprayer inc bonus armed (site=" + _sites + ", chance from quality curve"
                    + " 0..100%, sprays=" + _sprayed + ", +1 hits=" + _hits + ")";
            }
            catch (Exception)
            {
                return "sprayer inc bonus state unavailable";
            }
        }
    }
}
