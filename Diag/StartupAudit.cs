using System;
using System.Reflection;

namespace DSPQualitySystem.Diag
{
    /// <summary>
    /// M0 startup self-check (spec section 6.1). Uses reflection so that a method that
    /// moved or changed signature is reported in the log instead of breaking compilation.
    /// </summary>
    public static class StartupAudit
    {
        public static void Run()
        {
            var log = Plugin.Log;
            if (log == null)
            {
                return;
            }

            log.LogInfo("[Audit] ---- reflection self-check begin ----");
            log.LogInfo("[Audit] cargo constants: kIncLevelMax=" + Cargo.kIncLevelMax
                + " kSprayIncMax=" + Cargo.kSprayIncMax
                + " incTable.Length=" + (Cargo.incTable == null ? -1 : Cargo.incTable.Length)
                + " powerTable.Length=" + (Cargo.powerTable == null ? -1 : Cargo.powerTable.Length));

            CheckMethod(log, "PlanetFactory.PickFrom(uint,int,int,int[],out byte,out byte)",
                typeof(PlanetFactory), "PickFrom",
                new[]
                {
                    typeof(uint), typeof(int), typeof(int), typeof(int[]),
                    typeof(byte).MakeByRefType(), typeof(byte).MakeByRefType()
                },
                typeof(int),
                new[] { false, false, false, false, true, true });

            CheckMethod(log, "PlanetFactory.InsertInto(int,int,int,byte,byte,out byte)",
                typeof(PlanetFactory), "InsertInto",
                new[] { typeof(int), typeof(int), typeof(int), typeof(byte), typeof(byte), typeof(byte).MakeByRefType() },
                typeof(int),
                new[] { false, false, false, false, false, true });

            CheckMethod(log, "Player.UseHandItems(int,out int)",
                typeof(Player), "UseHandItems",
                new[] { typeof(int), typeof(int).MakeByRefType() },
                typeof(int),
                new[] { false, true });

            CheckMethod(log, "PlanetFactory.AddEntityDataWithComponents(EntityData,int)",
                typeof(PlanetFactory), "AddEntityDataWithComponents",
                new[] { typeof(EntityData), typeof(int) },
                typeof(int),
                new[] { false, false });

            CheckMethod(log, "PlanetFactory.DismantleFinally(Player,int,ref int)",
                typeof(PlanetFactory), "DismantleFinally",
                new[] { typeof(Player), typeof(int), typeof(int).MakeByRefType() },
                typeof(void),
                new[] { false, false, false });

            CheckMethod(log, "AssemblerComponent.InternalUpdate(float,int[],int[])",
                typeof(AssemblerComponent), "InternalUpdate",
                new[] { typeof(float), typeof(int[]), typeof(int[]) },
                typeof(uint),
                new[] { false, false, false });

            CheckMethod(log, "FactorySystem.GameTick(long,bool)",
                typeof(FactorySystem), "GameTick",
                new[] { typeof(long), typeof(bool) },
                typeof(void),
                new[] { false, false });

            CheckMethod(log, "MinerComponent.InternalUpdate(PlanetFactory,VeinData[],float,float,float,int[])",
                typeof(MinerComponent), "InternalUpdate",
                new[]
                {
                    typeof(PlanetFactory), typeof(VeinData[]),
                    typeof(float), typeof(float), typeof(float), typeof(int[])
                },
                typeof(uint),
                new[] { false, false, false, false, false, false });

            // v0.16.0 (Bug 1): the station-type vein collector (large miner, item 2316) does not use
            // the InsertInto output path of MinerComponent.InternalUpdate; its ore is drained by
            // StationComponent.UpdateVeinCollection. The postfix there is the quality hook.
            CheckMethod(log, "StationComponent.UpdateVeinCollection(PlanetFactory,int[])",
                typeof(StationComponent), "UpdateVeinCollection",
                new[] { typeof(PlanetFactory), typeof(int[]) },
                typeof(void),
                new[] { false, false });

            log.LogInfo("[Audit] " + Patches.StationComponent_UpdateVeinCollection_OutputInc.Describe());

            CheckMethod(log, "GameSave.SaveCurrentGame(string)",
                typeof(GameSave), "SaveCurrentGame",
                new[] { typeof(string) },
                typeof(bool),
                new[] { false });

            CheckMethod(log, "GameSave.LoadCurrentGame(string)",
                typeof(GameSave), "LoadCurrentGame",
                new[] { typeof(string) },
                typeof(bool),
                new[] { false });

            CheckMethod(log, "GameData.NewGame(GameDesc)",
                typeof(GameData), "NewGame",
                new[] { typeof(GameDesc) },
                typeof(bool),
                new[] { false });

            CheckMethod(log, "FractionatorComponent.InternalUpdate(PlanetFactory,float,SignData[],int[],int[])",
                typeof(FractionatorComponent), "InternalUpdate",
                new[] { typeof(PlanetFactory), typeof(float), typeof(SignData[]), typeof(int[]), typeof(int[]) },
                typeof(uint),
                new[] { false, false, false, false, false });

            CheckMethod(log, "FractionatorComponent.SetRecipe(int,SignData[])",
                typeof(FractionatorComponent), "SetRecipe",
                new[] { typeof(int), typeof(SignData[]) },
                typeof(void),
                new[] { false, false });

            // M4/P2: the transpiler ran during PatchAll in Awake, so its static state is final here.
            log.LogInfo("[Audit] FractionatorComponent.InternalUpdate anchors "
                + Patches.Fractionator_InternalUpdate_ProductInc.Describe());

            // P3 stage 1: the speed prefixes are registered right after PatchAll, state is final here.
            log.LogInfo("[Audit] " + Patches.P3_Speed.Describe());

            // P3 stage 2a (v0.11.0): charge-cap postfix on StationComponent.SetPCState.
            CheckMethod(log, "StationComponent.SetPCState(PowerConsumerComponent[])",
                typeof(StationComponent), "SetPCState",
                new[] { typeof(PowerConsumerComponent[]) },
                typeof(void),
                new[] { false });

            // The charge-cap postfix is registered right after PatchAll, state is final here.
            log.LogInfo("[Audit] " + Patches.P3_StationCharge.Describe());

            CheckMethod(log, "PlayerAction_Build.DoDismantleObject(int)",
                typeof(PlayerAction_Build), "DoDismantleObject",
                new[] { typeof(int) },
                typeof(bool),
                new[] { false });

            // P3 stage 2d: the dismantle transpiler is registered right after PatchAll (manual).
            log.LogInfo("[Audit] " + Patches.P3_Dismantle.Describe());

            // P4: building quality isolation in the player backpack.
            CheckMethod(log, "StorageComponent.AddItemStacked(int,int,int,out int)",
                typeof(StorageComponent), "AddItemStacked",
                new[] { typeof(int), typeof(int), typeof(int), typeof(int).MakeByRefType() },
                typeof(int),
                new[] { false, false, false, true });

            log.LogInfo("[Audit] isolation: building items = " + QualityData.CountBuildingItems());
            log.LogInfo("[Audit] " + Patches.P4_Isolation.Describe());

            // v0.11.5: generic "prefer the highest quality building item" consumption hooks.
            // (v0.15.0 removed the blueprint-carried-quality hook that used to be checked here.)
            CheckMethod(log, "Player.UseHandItems(int,out int)",
                typeof(Player), "UseHandItems",
                new[] { typeof(int), typeof(int).MakeByRefType() },
                typeof(int),
                new[] { false, true });

            CheckMethod(log, "StorageComponent.TakeTailItems(int&,int&,int&,bool)",
                typeof(StorageComponent), "TakeTailItems",
                new[]
                {
                    typeof(int).MakeByRefType(), typeof(int).MakeByRefType(),
                    typeof(int).MakeByRefType(), typeof(bool)
                },
                typeof(void),
                new[] { true, true, true, false });

            CheckMethod(log, "BuildTool_Click.CreatePrebuilds()",
                typeof(BuildTool_Click), "CreatePrebuilds",
                Type.EmptyTypes, typeof(void), new bool[0]);

            CheckMethod(log, "BuildTool_BlueprintPaste.CreatePrebuilds()",
                typeof(BuildTool_BlueprintPaste), "CreatePrebuilds",
                Type.EmptyTypes, typeof(void), new bool[0]);

            log.LogInfo("[Audit] " + Patches.P4_ConsumePriority.Describe());

            CheckMethod(log, "StorageComponent.Sort(bool)",
                typeof(StorageComponent), "Sort",
                new[] { typeof(bool) },
                typeof(void),
                new[] { false });

            // P4: the backpack sort prefix is registered right after PatchAll (manual).
            log.LogInfo("[Audit] " + Patches.P4_IsolationSort.Describe());

            // v0.12.0 (task-16): the five "A class" facility bonus targets. All patches are manual,
            // they are registered right after PatchAll, so the state printed below is final.
            CheckMethod(log, "LabComponent.InternalUpdateResearch(float,float,int[],ref TechState,ref int,ref long,ref long)",
                typeof(LabComponent), "InternalUpdateResearch",
                new[]
                {
                    typeof(float), typeof(float), typeof(int[]), typeof(TechState).MakeByRefType(),
                    typeof(int).MakeByRefType(), typeof(long).MakeByRefType(), typeof(long).MakeByRefType()
                },
                typeof(uint),
                new[] { false, false, false, false, false, false, false });
            log.LogInfo("[Audit] " + Patches.P5_LabSpeed.Describe());

            CheckTurretShoot(log, "Shoot_Gauss", false);
            CheckTurretShoot(log, "Shoot_Laser", false);
            CheckTurretShoot(log, "Shoot_Cannon", false);
            CheckTurretShoot(log, "Shoot_Plasma", true);
            CheckTurretShoot(log, "Shoot_Missile", true);
            CheckTurretShoot(log, "Shoot_LocalPlasma", false);
            log.LogInfo("[Audit] " + Patches.P5_TurretFireRate.Describe());

            CheckEnergyCap(log, "EnergyCap_Fuel", Type.EmptyTypes);
            CheckEnergyCap(log, "EnergyCap_Wind", new[] { typeof(float) });
            CheckEnergyCap(log, "EnergyCap_PV", new[] { typeof(float), typeof(float), typeof(float), typeof(float) });
            CheckEnergyCap(log, "EnergyCap_Gamma", new[] { typeof(float) });
            CheckEnergyCap(log, "EnergyCap_GTH", Type.EmptyTypes);
            log.LogInfo("[Audit] " + Patches.P5_PowerGen.Describe());

            CheckMethod(log, "EjectorComponent.InternalUpdate(float,long,DysonSwarm,AstroData[],AnimData[],int[])",
                typeof(EjectorComponent), "InternalUpdate",
                new[]
                {
                    typeof(float), typeof(long), typeof(DysonSwarm), typeof(AstroData[]),
                    typeof(AnimData[]), typeof(int[])
                },
                typeof(uint),
                new[] { false, false, false, false, false, false });

            CheckMethod(log, "SiloComponent.InternalUpdate(float,DysonSphere,AnimData[],int[])",
                typeof(SiloComponent), "InternalUpdate",
                new[] { typeof(float), typeof(DysonSphere), typeof(AnimData[]), typeof(int[]) },
                typeof(uint),
                new[] { false, false, false, false });
            log.LogInfo("[Audit] " + Patches.P5_Launcher.Describe());

            CheckMethod(log, "SpraycoaterComponent.InternalUpdate(CargoTraffic,AnimData[],int[])",
                typeof(SpraycoaterComponent), "InternalUpdate",
                new[] { typeof(CargoTraffic), typeof(AnimData[]), typeof(int[]) },
                typeof(void),
                new[] { false, false, false });
            log.LogInfo("[Audit] " + Patches.P5_Sprayer.Describe());

            // v0.11.9 (task-19): the golden ring overlay is installed in Awake, state is final here.
            // (v0.15.0 removed the screen-space outline overlay.)
            log.LogInfo("[Audit] " + Patches.P5_TenPointHalo.Describe());

            // v0.13.0 (task-17 / C-1, task-18 / C-2): anchors used by the two new features.
            CheckMethod(log, "GameData.Export(BinaryWriter)",
                typeof(GameData), "Export",
                new[] { typeof(System.IO.BinaryWriter) },
                typeof(void),
                new[] { false });
            log.LogInfo("[Audit] " + Patches.P4_StorageGrids.Describe());

            CheckMethod(log, "CargoTraffic.UpdateSplitter(ref SplitterComponent)",
                typeof(CargoTraffic), "UpdateSplitter",
                new[] { typeof(SplitterComponent).MakeByRefType() },
                typeof(void),
                new[] { true });
            CheckMethod(log, "CargoTraffic.UpdateSplitterAsync(ref SplitterComponent)",
                typeof(CargoTraffic), "UpdateSplitterAsync",
                new[] { typeof(SplitterComponent).MakeByRefType() },
                typeof(void),
                new[] { true });
            CheckMethod(log, "UISplitterWindow._OnUpdate()",
                typeof(UISplitterWindow), "_OnUpdate",
                Type.EmptyTypes, typeof(void), new bool[0]);

            // v0.17.0 (C-2 UI / C-3 UI): the rule editor lives in the vanilla item picker footer.
            //  - UIItemPicker.Popup(Vector2, Action<ItemProto>)      static
            //  - UIItemPicker._OnClose()                             protected
            //  - UISplitterWindow.OnCircleFilterClick(int)           captures the output port context
            //  - UIInserterWindow.OnSelectFilterClick()              captures the sorter context
            CheckMethod(log, "UIItemPicker.Popup(Vector2, Action<ItemProto>)",
                typeof(UIItemPicker), "Popup",
                new[] { typeof(UnityEngine.Vector2), typeof(System.Action<ItemProto>) },
                typeof(void), new bool[0]);
            CheckMethod(log, "UIItemPicker._OnClose()",
                typeof(UIItemPicker), "_OnClose",
                Type.EmptyTypes, typeof(void), new bool[0]);
            CheckMethod(log, "UISplitterWindow.OnCircleFilterClick(int)",
                typeof(UISplitterWindow), "OnCircleFilterClick",
                new[] { typeof(int) }, typeof(void), new bool[0]);
            CheckMethod(log, "UIInserterWindow.OnSelectFilterClick()",
                typeof(UIInserterWindow), "OnSelectFilterClick",
                Type.EmptyTypes, typeof(void), new bool[0]);
            log.LogInfo("[Audit] " + Patches.P5_SplitterFilter.Describe());
            log.LogInfo("[Audit] " + Patches.P5_QualityFilterPicker.Describe());

            // v0.16.1 (C-3): sorter quality filter anchors.
            //  - InserterComponent.InternalUpdate(PlanetFactory, int[][], AnimData[], float)
            //  - InserterComponent.InternalUpdate_Bidirectional(PlanetFactory, int[][], AnimData[], float)
            //  - InserterComponent.InternalUpdateNoAnim(PlanetFactory, int[][], float)
            //  - CargoTraffic.TryPickItem(int, int, int, out byte, out byte)
            //  - CargoTraffic.TryPickItem(int, int, int, int[], out byte, out byte)
            //  - UIInserterWindow._OnUpdate()
            CheckMethod(log, "CargoTraffic.TryPickItem(int,int,int,out byte,out byte)",
                typeof(CargoTraffic), "TryPickItem",
                new[]
                {
                    typeof(int), typeof(int), typeof(int),
                    typeof(byte).MakeByRefType(), typeof(byte).MakeByRefType()
                },
                typeof(int),
                new[] { false, false, false, true, true });
            CheckMethod(log, "CargoTraffic.TryPickItem(int,int,int,int[],out byte,out byte)",
                typeof(CargoTraffic), "TryPickItem",
                new[]
                {
                    typeof(int), typeof(int), typeof(int), typeof(int[]),
                    typeof(byte).MakeByRefType(), typeof(byte).MakeByRefType()
                },
                typeof(int),
                new[] { false, false, false, false, true, true });
            CheckMethod(log, "UIInserterWindow._OnUpdate()",
                typeof(UIInserterWindow), "_OnUpdate",
                Type.EmptyTypes, typeof(void), new bool[0]);
            log.LogInfo("[Audit] " + Patches.P5_SorterFilter.Describe());
            log.LogInfo("[Audit] " + Patches.P5_SorterFilterUi.Describe());

            // v0.14.0 (P6): item quality badge anchors - the two private grid rebuild hooks and the
            // per-frame hand-tip update. All three are no-arg instance methods returning void.
            CheckMethod(log, "UIStorageGrid.OnStorageSizeChanged()",
                typeof(UIStorageGrid), "OnStorageSizeChanged",
                Type.EmptyTypes, typeof(void), new bool[0]);
            CheckMethod(log, "UIStorageGrid.OnStorageContentChanged()",
                typeof(UIStorageGrid), "OnStorageContentChanged",
                Type.EmptyTypes, typeof(void), new bool[0]);
            CheckMethod(log, "UIHandTip._OnUpdate()",
                typeof(UIHandTip), "_OnUpdate",
                Type.EmptyTypes, typeof(void), new bool[0]);
            log.LogInfo("[Audit] " + Patches.P6_ItemQualityBadges.Describe());

            // v0.16.0: instant-build plus the assembler pick accounting.
            //  - StorageComponent.TakeTailItems(ref int itemId, ref int count, out int inc, bool useBan)
            //  - PlanetFactory.BuildFinally(Player player, int prebuildId, bool autoRefresh, bool flattenTerrain)
            //  - PlanetFactory.PickFrom(uint ioTargetTypedId, int offset, int filter, int[] needs,
            //                           out byte stack, out byte inc) - already checked above
            CheckMethod(log, "StorageComponent.TakeTailItems(ref int,ref int,ref int,bool)",
                typeof(StorageComponent), "TakeTailItems",
                new[]
                {
                    typeof(int).MakeByRefType(), typeof(int).MakeByRefType(),
                    typeof(int).MakeByRefType(), typeof(bool)
                },
                typeof(void),
                new[] { true, true, true, false });
            CheckMethod(log, "PlanetFactory.BuildFinally(Player,int,bool,bool)",
                typeof(PlanetFactory), "BuildFinally",
                new[] { typeof(Player), typeof(int), typeof(bool), typeof(bool) },
                typeof(void),
                new bool[4]);
            // v0.17.0: instant build opens the shared consume window from this tick prefix, so the
            // highest quality building item is consumed and its receipt bound in BuildFinally.
            CheckMethod(log, "PlayerAction_Inspect.GameTick(long)",
                typeof(PlayerAction_Inspect), "GameTick",
                new[] { typeof(long) }, typeof(void), new bool[1]);
            log.LogInfo("[Audit] " + Patches.InstantBuildCapture.Describe());

            CheckItemAbilities(log);
            log.LogInfo("[Audit] ---- reflection self-check end ----");
        }

        /// <summary>v0.12.0 (task-16): the six turret fire-rate signatures differ only by the enemy pool.</summary>
        private static void CheckTurretShoot(BepInEx.Logging.ManualLogSource log, string name, bool withEnemy)
        {
            Type[] parameters = withEnemy
                ? new[]
                {
                    typeof(PlanetFactory), typeof(EnemyData[]), typeof(PrefabDesc), typeof(int[]),
                    typeof(float), typeof(long), typeof(CombatUpgradeData).MakeByRefType()
                }
                : new[]
                {
                    typeof(PlanetFactory), typeof(PrefabDesc), typeof(int[]),
                    typeof(float), typeof(long), typeof(CombatUpgradeData).MakeByRefType()
                };
            CheckMethod(log, "TurretComponent." + name + "(" + parameters.Length + " params)",
                typeof(TurretComponent), name, parameters, typeof(void), new bool[parameters.Length]);
        }

        /// <summary>v0.12.0 (task-16): the five generator capacity getters (return type long).</summary>
        private static void CheckEnergyCap(BepInEx.Logging.ManualLogSource log, string name, Type[] parameters)
        {
            CheckMethod(log, "PowerGeneratorComponent." + name + "(" + parameters.Length + " params)",
                typeof(PowerGeneratorComponent), name, parameters, typeof(long), new bool[parameters.Length]);
        }

        private static void CheckMethod(BepInEx.Logging.ManualLogSource log, string label, Type type, string name,
            Type[] parameters, Type returnType, bool[] expectOut)
        {
            try
            {
                MethodInfo method = type.GetMethod(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static,
                    null, parameters, null);

                if (method == null)
                {
                    log.LogWarning("[Audit] " + label + " -> MISSING");
                    return;
                }

                string detail = "-> OK";
                if (method.ReturnType != returnType)
                {
                    detail = "-> RETURN MISMATCH got " + method.ReturnType.Name + " expected " + returnType.Name;
                }
                else if (!ParametersMatch(method, parameters, expectOut))
                {
                    detail = "-> BYREF/OUT MISMATCH: " + DescribeParameters(method);
                }

                log.LogInfo("[Audit] " + label + " " + detail);
            }
            catch (Exception e)
            {
                log.LogWarning("[Audit] " + label + " -> ERROR " + e.Message);
            }
        }

        private static bool ParametersMatch(MethodInfo method, Type[] parameters, bool[] expectOut)
        {
            var actual = method.GetParameters();
            if (actual.Length != parameters.Length || expectOut.Length != parameters.Length)
            {
                return false;
            }

            for (int i = 0; i < parameters.Length; i++)
            {
                bool expectedByRef = parameters[i].IsByRef;
                if (expectedByRef != actual[i].ParameterType.IsByRef)
                {
                    return false;
                }
                if (expectedByRef && actual[i].IsOut != expectOut[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static string DescribeParameters(MethodInfo method)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var parameter in method.GetParameters())
            {
                if (sb.Length > 0)
                {
                    sb.Append(',');
                }
                sb.Append(parameter.ParameterType.IsByRef ? (parameter.IsOut ? "out " : "ref ") : "");
                sb.Append(parameter.ParameterType.Name);
            }
            return sb.ToString();
        }

        private static void CheckItemAbilities(BepInEx.Logging.ManualLogSource log)
        {
            try
            {
                if (LDB.items == null)
                {
                    log.LogWarning("[Audit] ItemProto.Ability -> LDB.items is null");
                    return;
                }

                int[] ids = { 1141, 1142, 1143 };
                int[] expected = { 1, 2, 4 };
                for (int i = 0; i < ids.Length; i++)
                {
                    ItemProto proto = LDB.items.Select(ids[i]);
                    if (proto == null)
                    {
                        log.LogWarning("[Audit] ItemProto.Ability[" + ids[i] + "] -> MISSING item");
                        continue;
                    }

                    string verdict = proto.Ability == expected[i]
                        ? "OK"
                        : "MISMATCH expected " + expected[i];
                    log.LogInfo("[Audit] ItemProto.Ability[" + ids[i] + "] " + proto.name + " = " + proto.Ability + " " + verdict);
                }
            }
            catch (Exception e)
            {
                log.LogWarning("[Audit] ItemProto.Ability -> ERROR " + e.Message);
            }
        }
    }
}
