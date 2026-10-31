using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace DSPQualitySystem.Patches
{
    /// <summary>
    /// v0.19.2 - keeps the MOD quality filters (splitter per-port rules and sorter minimum points)
    /// inside every vanilla "copy building settings" path, so a copied building carries them.
    ///
    /// Vanilla has three of them and all three are hooked here:
    ///
    ///   1. <c>BuildingParameters.CopyFromFactoryObject(objectId, factory, copyInserters,
    ///      copyAllSettings)</c> - the Shift+click "copy this building" action of the build tool
    ///      (<c>VFInput._copyBuilding</c>). The paste side is
    ///      <c>BuildingParameters.PasteToFactoryObject(objectId, factory)</c> and, when the copy is
    ///      built from a prebuild, the static
    ///      <c>BuildingParameters.ApplyPrebuildParametersToEntity(entityId, recipeId, filterId,
    ///      parameters, content, factory)</c>.
    ///   2. <c>PlanetFactory.CopyBuildingSetting(objectId)</c> / <c>PasteBuildingSetting(objectId)</c>
    ///      - the Ctrl+C / Ctrl+V inspect-mode action and the copy/paste buttons of the building
    ///      panels.
    ///
    /// The MOD keeps those filters in its own table (keyed by entity id, persisted in the save
    /// trailer), so vanilla knows nothing about them; capturing them next to the vanilla clipboard
    /// and re-applying them on paste makes the behaviour match vanilla's other settings.
    ///
    /// Every hook is pure observation plus a table write. A missing target or an exception can only
    /// degrade to the old behaviour (settings not copied), never break vanilla copy/paste.
    /// </summary>
    internal static class P6_FilterCopy
    {
        private const string Tag = "[QSystem][filtercopy] ";

        private static bool _armed;
        private static string _reason = "not run";

        private static bool _clipValid;
        private static int _clipProtoId;
        private static bool _clipSplitter;
        private static bool _clipSorter;
        private static readonly int[] _clipRules = new int[QualityMath.SplitterPortCount];
        private static int _clipMin = QualityMath.SorterMinNone;

        private static int _copies;
        private static long _pastes;
        private static int _logCopies;

        // ---- blueprint transport (v0.19.2) ---------------------------------------------------
        //
        // Vanilla stores every blueprint building's settings as an int[] in
        // BuildingParameters.ToParamsArray(ref parameters, ref paramCount) and reads them back in
        // BuildingParameters.FromParamsArray(int[]). Neither is aware of our tables, so a blueprint
        // used to lose the splitter rules completely and to give every sorter the LAST copied
        // minimum. The fix appends one small self-describing block to that array (only when we know
        // we are serialising a blueprint building, or when we are pasting a block we just read), so
        // the per-building values travel with the blueprint and its file format.
        //
        // Block layout (ints): magic, version, kind (1 = splitter, 2 = sorter), proto id,
        // rule0..rule3, sorter min. It is detected by scanning the TAIL of the array, because
        // vanilla only ever appends to the front part and never resizes afterwards.
        private static bool _inBlueprintGen;
        private static int _bpGenFrame = -1;
        private static bool _bpPendingValid;
        private static int _bpPendingKind;
        private static int _bpPendingProtoId;
        private static readonly int[] _bpPendingRules = new int[QualityMath.SplitterPortCount];
        private static int _bpPendingMin = QualityMath.SorterMinNone;
        private static bool _bpAppendedThisPaste;
        private static long _blueprintWrites;
        private static long _blueprintReads;
        private static int _logBlueprint;
        private static int _logPreviewParams;
        private static int _logParamNoBlock;
        private static int _logApplyNoBlock;

        // ---- two-stage blueprint paste channel (v0.19.3) -------------------------------------
        //
        // Real-machine evidence showed the block survives the .bp file and reaches
        // BuildPreview.SetParameters, but by the time ApplyPrebuildParametersToEntity runs the array
        // is back to the vanilla length: the paste tool rebuilds the prebuild parameters through
        // BuildingParameters instead of reusing the preview array. So the blueprint settings are
        // carried out of band instead:
        //
        //   BuildTool_BlueprintPaste.CreatePrebuilds   (prefix)  - decode every blueprint building's
        //                                                         block into a small queue
        //   PlanetFactory.AddPrebuildDataWithComponents (postfix) - bind the matching clip to the new
        //                                                         prebuild id
        //   PlanetFactory.AddEntityDataWithComponents   (postfix) - write the clip onto the finished
        //                                                         entity id, then drop it
        //
        // The BuildingParameters channel above stays as a fallback (and still serves Shift copy).
        private sealed class BpClip
        {
            internal int Kind;
            internal int ProtoId;
            internal int Min;
            internal int[] Rules;
        }

        private static readonly List<BpClip> _bpQueue = new List<BpClip>();
        private static readonly Dictionary<int, BpClip> _bpByPrebuild = new Dictionary<int, BpClip>();
        private static long _bpStaged;
        private static long _bpFlushed;
        private static int _logPasteStage;

        internal static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo copyPost = AccessTools.Method(typeof(P6_FilterCopy), "CopyPostfix");
                MethodInfo pastePost = AccessTools.Method(typeof(P6_FilterCopy), "PastePostfix");
                MethodInfo bpCopyPost = AccessTools.Method(typeof(P6_FilterCopy), "BpCopyPostfix");
                MethodInfo bpPastePost = AccessTools.Method(typeof(P6_FilterCopy), "BpPastePostfix");
                MethodInfo bpApplyPost = AccessTools.Method(typeof(P6_FilterCopy), "BpApplyPostfix");
                MethodInfo bpPastePre = AccessTools.Method(typeof(P6_FilterCopy), "BpPastePrefix");
                MethodInfo bpGenPre = AccessTools.Method(typeof(P6_FilterCopy), "BpGenPrefix");
                MethodInfo bpGenPost = AccessTools.Method(typeof(P6_FilterCopy), "BpGenPostfix");
                MethodInfo paramWritePost = AccessTools.Method(typeof(P6_FilterCopy), "BpParamWritePostfix");
                MethodInfo paramReadPre = AccessTools.Method(typeof(P6_FilterCopy), "BpParamReadPrefix");
                if (copyPost == null || pastePost == null || bpCopyPost == null || bpPastePost == null
                    || bpApplyPost == null || bpPastePre == null || bpGenPre == null || bpGenPost == null
                    || paramWritePost == null || paramReadPre == null)
                {
                    Disable("postfix resolvers missing");
                    return;
                }

                int patched = 0;

                // (1) build-tool Shift+click copy/paste.
                MethodInfo bpCopy = AccessTools.Method(typeof(BuildingParameters), "CopyFromFactoryObject",
                    new[] { typeof(int), typeof(PlanetFactory), typeof(bool), typeof(bool) });
                MethodInfo bpPaste = AccessTools.Method(typeof(BuildingParameters), "PasteToFactoryObject",
                    new[] { typeof(int), typeof(PlanetFactory) });
                MethodInfo bpApply = AccessTools.Method(typeof(BuildingParameters), "ApplyPrebuildParametersToEntity",
                    new[] { typeof(int), typeof(int), typeof(int), typeof(int[]), typeof(string), typeof(PlanetFactory) });

                if (bpCopy != null)
                {
                    harmony.Patch(bpCopy, postfix: new HarmonyMethod(bpCopyPost));
                    patched++;
                }

                if (bpPaste != null)
                {
                    harmony.Patch(bpPaste, prefix: new HarmonyMethod(bpPastePre), postfix: new HarmonyMethod(bpPastePost));
                    patched++;
                }

                if (bpApply != null)
                {
                    harmony.Patch(bpApply, postfix: new HarmonyMethod(bpApplyPost));
                    patched++;
                }

                // (2) inspect-mode Ctrl+C / Ctrl+V and the panel copy/paste buttons.
                MethodInfo copy = AccessTools.Method(typeof(PlanetFactory), "CopyBuildingSetting", new[] { typeof(int) });
                MethodInfo paste = AccessTools.Method(typeof(PlanetFactory), "PasteBuildingSetting", new[] { typeof(int) });
                if (copy != null && paste != null)
                {
                    harmony.Patch(copy, postfix: new HarmonyMethod(copyPost));
                    harmony.Patch(paste, postfix: new HarmonyMethod(pastePost));
                    patched += 2;
                }

                // (3) blueprints: the per-building settings travel inside BuildingParameters' int[].
                MethodInfo bpParamWrite = AccessTools.Method(typeof(BuildingParameters), "ToParamsArray",
                    new[] { typeof(int[]).MakeByRefType(), typeof(int).MakeByRefType() });
                MethodInfo bpParamRead = AccessTools.Method(typeof(BuildingParameters), "FromParamsArray",
                    new[] { typeof(int[]) });
                MethodInfo bpGen = AccessTools.Method(typeof(BlueprintUtils), "GenerateBlueprintData",
                    new[]
                    {
                        typeof(BlueprintData), typeof(PlanetData), typeof(PlanetAuxData), typeof(int[]), typeof(int),
                        typeof(float), typeof(uint[]), typeof(bool),
                    });

                if (bpParamWrite != null)
                {
                    harmony.Patch(bpParamWrite, postfix: new HarmonyMethod(paramWritePost));
                    patched++;
                }

                if (bpParamRead != null)
                {
                    harmony.Patch(bpParamRead, prefix: new HarmonyMethod(paramReadPre));
                    patched++;
                }

                if (bpGen != null)
                {
                    harmony.Patch(bpGen, prefix: new HarmonyMethod(bpGenPre), postfix: new HarmonyMethod(bpGenPost));
                    patched++;
                }

                // Diagnostic probe (v0.19.2): the single funnel where a blueprint's parameter array
                // enters the build preview. Logs the array tail so a real-machine test can tell a
                // blueprint that carries our block from one written by an older build.
                MethodInfo bpPreviewParams = AccessTools.Method(typeof(BuildPreview), "SetParameters",
                    new[] { typeof(int), typeof(int[]) });
                MethodInfo previewParamsPre = AccessTools.Method(typeof(P6_FilterCopy), "BpPreviewParamsPrefix");
                if (bpPreviewParams != null && previewParamsPre != null)
                {
                    harmony.Patch(bpPreviewParams, prefix: new HarmonyMethod(previewParamsPre));
                }

                // Two-stage blueprint paste channel: prebuild-parameters cannot be trusted, so the
                // decoded clips travel from the pasted blueprint to the finished entity out of band.
                MethodInfo pastePrebuilds = AccessTools.Method(typeof(BuildTool_BlueprintPaste), "CreatePrebuilds",
                    Type.EmptyTypes);
                MethodInfo pastePrebuildsPre = AccessTools.Method(typeof(P6_FilterCopy), "PastePrebuildsPrefix");
                if (pastePrebuilds != null && pastePrebuildsPre != null)
                {
                    harmony.Patch(pastePrebuilds, prefix: new HarmonyMethod(pastePrebuildsPre));
                    patched++;
                }

                MethodInfo addPrebuild = AccessTools.Method(typeof(PlanetFactory), "AddPrebuildDataWithComponents",
                    new[] { typeof(PrebuildData) });
                MethodInfo addPrebuildPost = AccessTools.Method(typeof(P6_FilterCopy), "AddPrebuildPostfix");
                if (addPrebuild != null && addPrebuildPost != null)
                {
                    harmony.Patch(addPrebuild, postfix: new HarmonyMethod(addPrebuildPost));
                    patched++;
                }

                MethodInfo addEntity = AccessTools.Method(typeof(PlanetFactory), "AddEntityDataWithComponents",
                    new[] { typeof(EntityData), typeof(int) });
                MethodInfo addEntityPost = AccessTools.Method(typeof(P6_FilterCopy), "AddEntityPostfix");
                if (addEntity != null && addEntityPost != null)
                {
                    // Late postfix: the entity is registered by the time AddEntityDataWithComponents
                    // returns, so the quality tables accept the id no matter which order this runs in.
                    harmony.Patch(addEntity, postfix: new HarmonyMethod(addEntityPost));
                    patched++;
                }

                _armed = patched > 0;
                _reason = "patched " + patched + "/11 (BuildingParameters copy/paste/apply + Copy/PasteBuildingSetting"
                    + " + blueprint params/save/load + prebuild stage)";
            }
            catch (Exception e)
            {
                Disable(e.GetType().Name + ": " + e.Message);
            }
        }

        private static void Disable(string reason)
        {
            _armed = false;
            _reason = reason;
            P5Common.WarnOnce("filtercopy.apply", Tag + "disabled: " + reason);
        }

        // ---- hooks -------------------------------------------------------------------------

        /// <summary>Build tool Shift+click: vanilla captured the settings, now capture ours.</summary>
        private static void BpCopyPostfix(int objectId, PlanetFactory factory)
        {
            Capture(factory, objectId, "bp-copy");
        }

        /// <summary>Build tool paste: reset the per-paste "block already written" flag.</summary>
        private static void BpPastePrefix()
        {
            _bpAppendedThisPaste = false;
        }

        /// <summary>Build tool: the copied settings were pasted onto a finished object.</summary>
        private static void BpPastePostfix(int objectId, PlanetFactory factory)
        {
            ApplyTo(factory, objectId, "bp-paste");

            if (_bpAppendedThisPaste)
            {
                // The block is now part of the prebuild parameters; the entity applies it later
                // through ApplyPrebuildParametersToEntity (BpApplyPostfix).
                _bpPendingValid = false;
            }
        }

        /// <summary>Build tool: a prebuild finished and vanilla applied its copied parameters.</summary>
        private static void BpApplyPostfix(int entityId, int[] parameters, PlanetFactory factory)
        {
            try
            {
                if (_armed && TryReadBlock(parameters, out int kind, out int protoId, out int[] rules, out int min)
                    && ApplyBlock(factory, entityId, kind, protoId, rules, min, "bp-apply-params"))
                {
                    return;
                }

                if (_armed && P5Common.DebugOn && _logApplyNoBlock < 12)
                {
                    _logApplyNoBlock++;
                    P5Common.Info(Tag + "bp-apply without block eid=" + entityId + " len="
                        + (parameters == null ? -1 : parameters.Length) + " tail=[" + TailText(parameters) + "]");
                }
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("filtercopy.blueprint-apply", e.ToString());
            }

            ApplyTo(factory, entityId, "bp-apply");
        }

        /// <summary>Blueprint generation: vanilla is serialising one blueprint building.</summary>
        private static void BpGenPrefix()
        {
            _inBlueprintGen = true;
            _bpGenFrame = Time.frameCount;
        }

        private static void BpGenPostfix()
        {
            _inBlueprintGen = false;
            _bpGenFrame = -1;
        }

        /// <summary>BuildingParameters.ToParamsArray: append our block to the vanilla array.</summary>
        private static void BpParamWritePostfix(ref int[] _parameters, ref int _paramCount)
        {
            try
            {
                if (!_armed)
                {
                    return;
                }

                bool saveSide = _inBlueprintGen && _bpGenFrame == Time.frameCount && _clipValid;
                if (saveSide)
                {
                    int kind = _clipSplitter ? 1 : (_clipSorter ? 2 : 0);
                    if (kind == 0)
                    {
                        return;
                    }

                    AppendBlock(ref _parameters, ref _paramCount, kind, _clipProtoId, _clipRules, _clipMin);
                    _blueprintWrites++;
                    P5Common.Debug(ref _logBlueprint, Tag + "blueprint block written (save) kind=" + kind
                        + " proto=" + _clipProtoId + " min=" + _clipMin + " ints=" + _parameters.Length);
                    return;
                }

                if (_bpPendingValid)
                {
                    AppendBlock(ref _parameters, ref _paramCount, _bpPendingKind, _bpPendingProtoId,
                        _bpPendingRules, _bpPendingMin);
                    _bpAppendedThisPaste = true;
                    _blueprintWrites++;
                    P5Common.Debug(ref _logBlueprint, Tag + "blueprint block written (paste) kind="
                        + _bpPendingKind + " proto=" + _bpPendingProtoId + " ints=" + _parameters.Length);
                }
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("filtercopy.blueprint-write", e.ToString());
            }
        }

        /// <summary>BuildingParameters.FromParamsArray: remember the block a blueprint carried.</summary>
        private static void BpParamReadPrefix(int[] _parameters)
        {            try
            {
                if (!_armed)
                {
                    return;
                }

                if (!TryReadBlock(_parameters, out int kind, out int protoId, out int[] rules, out int min))
                {
                    if (P5Common.DebugOn && _logParamNoBlock < 12)
                    {
                        _logParamNoBlock++;
                        P5Common.Info(Tag + "bp-params read without block len="
                            + (_parameters == null ? -1 : _parameters.Length) + " tail=[" + TailText(_parameters) + "]");
                    }

                    return;
                }

                _bpPendingValid = true;
                _bpPendingKind = kind;
                _bpPendingProtoId = protoId;
                _bpPendingMin = min;
                _blueprintReads++;
                Array.Clear(_bpPendingRules, 0, _bpPendingRules.Length);
                Array.Copy(rules, _bpPendingRules, Math.Min(rules.Length, _bpPendingRules.Length));
                P5Common.Debug(ref _logBlueprint, Tag + "blueprint block read kind=" + kind + " proto=" + protoId
                    + " min=" + min + " rules=[" + _bpPendingRules[0] + "," + _bpPendingRules[1] + ","
                    + _bpPendingRules[2] + "," + _bpPendingRules[3] + "]");
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("filtercopy.blueprint-read", e.ToString());
            }
        }

        /// <summary>
        /// Diagnostic probe (<c>BuildPreview.SetParameters(int, int[])</c>): the one funnel where a
        /// blueprint building's parameter array enters the build preview. If the tail already lost our
        /// block here, the blueprint itself is the problem (for example one created before v0.19.2);
        /// if the block is present here, a missing apply is our bug.
        /// </summary>
        private static void BpPreviewParamsPrefix(int count, int[] paramArr)
        {
            try
            {
                if (!P5Common.DebugOn || paramArr == null || paramArr.Length <= 0)
                {
                    // ResetAll / empty previews call this with a null array constantly; skip them so the
                    // real blueprint previews are not pushed out of the log budget.
                    return;
                }

                bool hasBlock = TryReadBlock(paramArr, out int kind, out int protoId, out _, out int min);
                if (!hasBlock && _logPreviewParams >= 12)
                {
                    return;
                }

                _logPreviewParams++;
                P5Common.Info(Tag + "bp-preview params count=" + count
                    + " len=" + (paramArr == null ? -1 : paramArr.Length)
                    + " block=" + (hasBlock ? ("kind:" + kind + "/proto:" + protoId + "/min:" + min) : "none")
                    + " tail=[" + TailText(paramArr) + "]");
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("filtercopy.preview-params", e.ToString());
            }
        }

        /// <summary>Renders the last nine ints of an array (the blueprint block region) for logs.</summary>
        private static string TailText(int[] array)
        {
            if (array == null)
            {
                return "null";
            }

            int from = Math.Max(0, array.Length - QualityMath.BlueprintBlockLength);
            StringBuilder sb = new StringBuilder();
            for (int i = from; i < array.Length; i++)
            {
                if (sb.Length > 0)
                {
                    sb.Append(',');
                }

                sb.Append(array[i]);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Blueprint paste, stage 1: decode every pasted building's clip from the blueprint data (the
        /// same array SetParameters receives, but read from the source of truth so the rebuilt
        /// prebuild parameters cannot lose it again).
        /// </summary>
        private static void PastePrebuildsPrefix(BuildTool_BlueprintPaste __instance)
        {
            try
            {
                _bpQueue.Clear();
                _bpByPrebuild.Clear();

                if (!_armed || __instance == null || __instance.blueprint == null)
                {
                    return;
                }

                BlueprintBuilding[] buildings = __instance.blueprint.buildings;
                if (buildings == null)
                {
                    return;
                }

                int staged = 0;
                for (int i = 0; i < buildings.Length; i++)
                {
                    BlueprintBuilding building = buildings[i];
                    if (building == null
                        || !TryReadBlock(building.parameters, out int kind, out int protoId, out int[] rules, out int min))
                    {
                        continue;
                    }

                    int proto = protoId > 0 ? protoId : building.itemId;
                    _bpQueue.Add(new BpClip
                    {
                        Kind = kind,
                        ProtoId = proto,
                        Min = min,
                        Rules = rules,
                    });
                    staged++;
                }

                _bpStaged += staged;
                if (P5Common.DebugOn && _logPasteStage < 6)
                {
                    _logPasteStage++;
                    P5Common.Info(Tag + "bp-stage queued=" + staged + " buildings=" + buildings.Length);
                }
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("filtercopy.paste-stage", e.ToString());
            }
        }

        /// <summary>Blueprint paste, stage 2: bind the queued clip to the prebuild it belongs to.</summary>
        private static void AddPrebuildPostfix(PrebuildData prebuild, ref int __result)
        {
            try
            {
                if (!_armed || __result <= 0 || _bpQueue.Count == 0)
                {
                    return;
                }

                int protoId = prebuild.protoId;
                int found = -1;
                for (int i = 0; i < _bpQueue.Count; i++)
                {
                    if (_bpQueue[i].ProtoId == protoId)
                    {
                        found = i;
                        break;
                    }
                }

                if (found < 0)
                {
                    return;
                }

                BpClip clip = _bpQueue[found];
                _bpQueue.RemoveAt(found);
                _bpByPrebuild[__result] = clip;

                if (P5Common.DebugOn && _logPasteStage < 12)
                {
                    _logPasteStage++;
                    P5Common.Info(Tag + "bp-stage prebuild id=" + __result + " proto=" + protoId
                        + " kind=" + clip.Kind + " min=" + clip.Min);
                }
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("filtercopy.stage-prebuild", e.ToString());
            }
        }

        /// <summary>Blueprint paste, stage 3: write the clip onto the finished entity.</summary>
        private static void AddEntityPostfix(EntityData entity, int prebuildId, PlanetFactory __instance, ref int __result)
        {
            try
            {
                if (!_armed || prebuildId <= 0 || __result <= 0)
                {
                    return;
                }

                if (!_bpByPrebuild.TryGetValue(prebuildId, out BpClip clip))
                {
                    return;
                }

                _bpByPrebuild.Remove(prebuildId);
                if (ApplyBlock(__instance, __result, clip.Kind, clip.ProtoId, clip.Rules, clip.Min, "bp-stage"))
                {
                    _bpFlushed++;
                }
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("filtercopy.stage-entity", e.ToString());
            }
        }

        /// <summary>Inspect mode Ctrl+C / panel copy button.</summary>
        private static void CopyPostfix(PlanetFactory __instance, int objectId)
        {
            Capture(__instance, objectId, "copy-setting");
        }

        /// <summary>Inspect mode Ctrl+V / panel paste button.</summary>
        private static void PastePostfix(PlanetFactory __instance, int objectId)
        {
            ApplyTo(__instance, objectId, "paste-setting");
        }

        // ---- shared logic ------------------------------------------------------------------

        private static void Capture(PlanetFactory factory, int entityId, string source)
        {
            try
            {
                if (!_armed || factory == null)
                {
                    return;
                }

                // A fresh vanilla copy invalidates any blueprint block still pending from a paste.
                _bpPendingValid = false;

                if (!DescribeEntity(factory, entityId, out int planetId, out int protoId, out bool splitter, out bool sorter))
                {
                    _clipValid = false;
                    return;
                }

                _clipValid = true;
                _clipProtoId = protoId;
                _clipSplitter = splitter;
                _clipSorter = sorter;
                for (int port = 0; port < QualityMath.SplitterPortCount; port++)
                {
                    _clipRules[port] = splitter ? QualityData.GetSplitterRule(planetId, entityId, port) : QualityMath.SplitterRuleNone;
                }

                _clipMin = sorter ? QualityData.GetSorterMin(planetId, entityId) : QualityMath.SorterMinNone;
                _copies++;

                P5Common.Debug(ref _logCopies, Tag + source + " eid=" + entityId + " proto=" + protoId
                    + " split=" + (splitter ? "1" : "0") + " sorter=" + (sorter ? "1" : "0")
                    + " min=" + _clipMin + " rules=[" + _clipRules[0] + "," + _clipRules[1] + ","
                    + _clipRules[2] + "," + _clipRules[3] + "]");
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("filtercopy.capture", e.ToString());
            }
        }

        private static void ApplyTo(PlanetFactory factory, int entityId, string source)
        {
            try
            {
                if (!_armed || factory == null)
                {
                    return;
                }

                // A per-building blueprint block always wins over the single vanilla clipboard slot.
                if (_bpPendingValid
                    && ApplyBlock(factory, entityId, _bpPendingKind, _bpPendingProtoId, _bpPendingRules, _bpPendingMin,
                        source + "-block"))
                {
                    _bpPendingValid = false;
                    return;
                }

                if (!_clipValid)
                {
                    return;
                }

                if (!DescribeEntity(factory, entityId, out int planetId, out int protoId, out bool splitter, out bool sorter))
                {
                    return;
                }

                if ((splitter && !_clipSplitter) || (sorter && !_clipSorter))
                {
                    return;
                }

                if (splitter)
                {
                    for (int port = 0; port < QualityMath.SplitterPortCount; port++)
                    {
                        QualityData.SetSplitterRule(planetId, entityId, port, _clipRules[port]);
                    }
                }

                if (sorter)
                {
                    QualityData.SetSorterMin(planetId, entityId, _clipMin);
                }

                _pastes++;
                P5Common.Effective(ref _pastes, "filtercopy", " " + source + " eid=" + entityId
                    + " proto=" + protoId + " from=" + _clipProtoId + " min=" + _clipMin);
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("filtercopy.apply-to", e.ToString());
            }
        }

        /// <summary>Appends one self-describing block to a BuildingParameters int[] array.</summary>
        private static void AppendBlock(ref int[] parameters, ref int paramCount, int kind, int protoId, int[] rules,
            int min)
        {
            parameters = QualityMath.AppendBlueprintBlock(parameters, kind, protoId, rules, min);
            if (paramCount < parameters.Length)
            {
                paramCount = parameters.Length;
            }
        }

        /// <summary>Reads back a block from the tail of a BuildingParameters int[] array.</summary>
        private static bool TryReadBlock(int[] parameters, out int kind, out int protoId, out int[] rules, out int min)
        {
            return QualityMath.TryReadBlueprintBlock(parameters, out kind, out protoId, out rules, out min);
        }

        /// <summary>Writes a decoded block onto a live building (kind and proto must match).</summary>
        private static bool ApplyBlock(PlanetFactory factory, int entityId, int kind, int protoId, int[] rules, int min,
            string source)
        {
            try
            {
                if (!_armed || factory == null || kind == 0)
                {
                    return false;
                }

                if (!DescribeEntity(factory, entityId, out int planetId, out int realProtoId, out bool splitter,
                        out bool sorter))
                {
                    return false;
                }

                if ((kind == 1 && !splitter) || (kind == 2 && !sorter))
                {
                    return false;
                }

                if (protoId > 0 && protoId != realProtoId)
                {
                    return false;
                }

                if (splitter)
                {
                    for (int port = 0; port < QualityMath.SplitterPortCount; port++)
                    {
                        int rule = rules != null && port < rules.Length ? rules[port] : QualityMath.SplitterRuleNone;
                        QualityData.SetSplitterRule(planetId, entityId, port, rule);
                    }
                }

                if (sorter)
                {
                    QualityData.SetSorterMin(planetId, entityId, min);
                }

                _pastes++;
                P5Common.Effective(ref _pastes, "filtercopy", " " + source + " eid=" + entityId
                    + " proto=" + realProtoId + " kind=" + kind + " min=" + min);
                return true;
            }
            catch (Exception e)
            {
                P5Common.WarnOnce("filtercopy.apply-block", e.ToString());
                return false;
            }
        }

        private static bool DescribeEntity(PlanetFactory factory, int entityId, out int planetId, out int protoId,
            out bool splitter, out bool sorter)
        {
            planetId = 0;
            protoId = 0;
            splitter = false;
            sorter = false;

            EntityData[] pool = factory.entityPool;
            if (pool == null || entityId <= 0 || entityId >= pool.Length)
            {
                return false;
            }

            protoId = pool[entityId].protoId;
            if (protoId <= 0)
            {
                return false;
            }

            ItemProto proto = LDB.items.Select(protoId);
            if (proto == null || proto.prefabDesc == null)
            {
                return false;
            }

            splitter = proto.prefabDesc.isSplitter;
            sorter = proto.prefabDesc.isInserter;
            if (!splitter && !sorter)
            {
                return false;
            }

            planetId = factory.planetId;
            return planetId >= 0;
        }

        internal static bool Armed => _armed;

        internal static void ResetState()
        {
            _clipValid = false;
            _clipProtoId = 0;
            _clipSplitter = false;
            _clipSorter = false;
            Array.Clear(_clipRules, 0, _clipRules.Length);
            _clipMin = QualityMath.SorterMinNone;

            _bpPendingValid = false;
            _bpPendingKind = 0;
            _bpPendingProtoId = 0;
            Array.Clear(_bpPendingRules, 0, _bpPendingRules.Length);
            _bpPendingMin = QualityMath.SorterMinNone;
            _bpAppendedThisPaste = false;
            _inBlueprintGen = false;
            _bpGenFrame = -1;
            _bpQueue.Clear();
            _bpByPrebuild.Clear();
        }

        internal static string Describe()
        {
            return "armed=" + _armed + " reason=" + _reason + " copies=" + _copies + " pastes=" + _pastes
                + " clip=" + (_clipValid ? ("proto:" + _clipProtoId) : "empty")
                + " blueprintWrites=" + _blueprintWrites + " blueprintReads=" + _blueprintReads
                + " staged=" + _bpStaged + " flushed=" + _bpFlushed
                + " bp=" + (_bpPendingValid ? ("kind:" + _bpPendingKind + "/proto:" + _bpPendingProtoId) : "empty");
        }
    }
}
