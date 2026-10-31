# DSP Quality System — 开发日志（DEVLOG）

工程目录：`C:\WorkWork\DSP MOD\DSPQualitySystem\`
目标：将原版增产 `inc`（0–10）作为品质点数使用；本文件记录构建/部署/验证命令与每个里程碑的实测结果。
规格书：`C:\WorkWork\DSP MOD\DSP品质系统-MOD开发文档.md`（v2.0，唯一实现依据）。

## 1. 构建与部署命令（已验证可用）

本机无系统 .NET SDK，使用便携 SDK（经 `DSP-MPD-runtime-debug\tools\dspenv.ps1` 重定向 DOTNET_ROOT/NUGET_PACKAGES/TMP/APPDATA/LOCALAPPDATA）。

```powershell
# 只构建（0 警告 0 错误；输出 bin\Release\DSPQualitySystem.dll）
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\WorkWork\DSP MOD\DSPQualitySystem\build.ps1"

# 构建 + 部署到游戏插件目录（游戏必须已退出，否则 dll 被占用）
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\WorkWork\DSP MOD\DSPQualitySystem\build.ps1" -Deploy
```

- 部署目标：`C:\Games\Steam\steamapps\common\Dyson Sphere Program\BepInEx\plugins\DSPQualitySystem\DSPQualitySystem.dll`
- `build.ps1` 判定标记：`BUILD OK` / `BUILD FAILED`；`-Deploy` 时 `DEPLOY OK -> <路径>` + `DEPLOY SHA256 <hash>`。
- 本机 pwsh 实际是 Windows PowerShell 5.1：`build.ps1` 保持纯 ASCII；读中文文件用 `[IO.File]::ReadAllText($p,[Text.Encoding]::UTF8)`。
- 成败以输出标记为准，不只看退出码（本机偶发假非零码）。

## 2. 启动游戏 / 取日志命令

```powershell
cd C:\WorkWork\DSP MOD\DSP-MPD-runtime-debug
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev-loop.ps1 -Mode up        # 约 30-70s 就绪
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev-loop.ps1 -Mode log -Lines 200 -Filter 'QSystem|Audit'
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev-loop.ps1 -Mode down       # 停游戏并归档日志
```

- 当前日志：`C:\Games\Steam\steamapps\common\Dyson Sphere Program\BepInEx\LogOutput.log`（UTF-8）
- 归档日志：`DSP-MPD-runtime-debug\logs\LogOutput-<stamp>-<tag>-pid<N>.log`
- 改 DLL 前必须先 `-Mode down`。修改 Harmony 补丁必须重启游戏（无热重载）。

## 3. 配置

运行后生成 `...\BepInEx\config\dsp.qualitysystem.cfg`，段名 `[quality]`，键名/默认值严格按文档 §2.4：
enabled=true、incPerPoint=100、powerPerPoint=250、buildingChancePerPoint=1.5、minerChancePerPoint=2、maxUpgradeChance=60、maxQuality=10、giveDismantleInc=false。
**注意：BepInEx 5.4.23.5 不会热重载 cfg 文件**（实测运行中改文件无 `SettingChanged` 事件），改配置需重启游戏生效。

## 4. 里程碑记录

### M0 / T1 —— 骨架 + 线性表（完成，2026-10-22）

交付文件（单文件均 <500 行）：
- `Plugin.cs`：`[BepInPlugin("dsp.qualitysystem","DSP Quality System","0.1.0")]`；Awake 打版本/build 指纹/8 项配置 → 应用 4 张表 → `Harmony.PatchAll`；Update 每 30 tick 轮询 `LDB.items.dataArray.Length>=100`，就绪后打印自检并置 `_startupReported`。
- `QualityConfig.cs`：8 个 ConfigEntry + `MaxLevel` clamp + `ClampChance`。
- `QualityData.cs`：`PlanetQuality`（buildingInc/poolInc/poolCount/inputLevel/prevProduced 按 entityId 索引）+ 池读写（§2.2）。
- `Patches\P0_Tables.cs`：`Cargo.incTable[i]=perPoint*i`、`incTableMilli[i]=perPoint*0.001*i`、`powerTable[i]=powerPerPoint*i`、`powerTableRatio[i]=1+powerPerPoint*i/1000.0`（i≤MaxLevel，逐元素写、不换数组引用）；首次调用前缓存原版 4 表，`enabled=false` 时逐元素还原；`UIGame._OnCreate` Postfix 兜底重应用。
- `Diag\StartupAudit.cs`：5 个反射签名自检 + Cargo 常量 + 增产剂 Ability 1141/1142/1143。

构建/部署结果：`BUILD OK`（0 警告 0 错误，增量 ~1.4s）；`DEPLOY OK`。

进游戏实测（enabled=true，日志 mtime=2026-10-22 18:50:58 的部署版本）：
- `Loading [DSP Quality System 0.1.0]`，`=== DSP Quality System v0.1.0 ===`，`env: game=0.10.35 unity=2022.3.62f3c1`；cfg 8 项与文档一致。
- `vanilla tables cached (inc=[0,125,200,225,250,275,300,325,350,375,400])`。
- 三处应用：Awake / `UIGame._OnCreate`（主菜单即创建，早于 LDB）/ `LDB ready (items=175)`；4 张表实测：
  - `incTable=[0,100,200,300,400,500,600,700,800,900,1000]`
  - `incTableMilli=[0,0.1,0.2,0.3,0.4,0.5,0.6,0.7,0.8,0.9,1]`
  - `powerTable=[0,250,500,750,1000,1250,1500,1750,2000,2250,2500]`
  - `powerTableRatio=[1,1.25,1.5,1.75,2,2.25,2.5,2.75,3,3.25,3.5]`
- `[Audit]` 5 条签名全部 `-> OK`；`ItemProto.Ability[1141] 增产剂 Mk.I = 1 OK`、`[1142] Mk.II = 2 OK`、`[1143] Mk.III = 4 OK`；`startup report complete`。
- 全日志仅 2 条 Error/Warning，均为既有环境问题（`Common API Nebula Compatibility` 缺 `dsp.nebula-multiplayer-api`；CommonAPI 编译 build 0.10.33 vs 运行 0.10.35），与本 Mod 无关；本 Mod 零异常。

enabled=false 回归（T5 第 9 条提前验证，2026-10-22 18:52，pid 27088）：
- `cfg enabled=False` → `vanilla tables cached (inc=[0,125,...400])` → `disabled (Awake): vanilla tables restored`
- 还原后 4 表 = 原版精确值：`incTable=[0,125,200,225,250,275,300,325,350,375,400]`、`incTableMilli=[0,0.125,0.2,...,0.4]`、`powerTable=[0,300,700,1100,1500,1900,2300,2700,3100,3500,3900]`、`powerTableRatio=[1,1.3,1.7,2.1,2.5,2.9,3.3,3.7,4.1,4.5,4.9]`。
- 结论：关开关 = 原版数值；测试后 cfg 已恢复 `enabled = true`。

M0 首版踩坑：自检最初把 `DismantleFinally(Player,int,ref int)` 的第 3 个 byref 当 out，误报 `OUT-PARAM MISMATCH`；改为逐参数 `bool[] expectOut`（DismantleFinally 传 `{false,false,false}`）后全 OK。

### M1 / T2 —— 制造台输入品质 + 产出池 + 自实现 PickFrom（代码完成并部署，主菜单验证通过；进存档验证待做，2026-10-22 18:59）

交付文件（均在 `DSPQualitySystem\`，单文件 <500 行）：
- `QualityEnv.cs`：ambient 星球作用域。`CurrentPlanetId` 优先取 `_planetId`，回退 `GameMain.localPlanet`；`Enter(FactorySystem)`/`Exit()`；`WarnOnce(key,Exception)` 每个 key 只打一次，供热路径用。
- `QualityRules.cs`：R-1/R-2 规则。`RollCycleQuality(planetId,entityId,inputLevel)`：`chance=clamp(buildingInc*buildingChancePerPoint,0,maxUpgradeChance)`，命中 `+1` 并 clamp 到 `MaxLevel`；`RollPercent` 用 `[ThreadStatic] Random.Next(10000)`（0.01% 分辨率）。
- `Patches\P0_Env.cs`：`FactorySystem.GameTick(long,bool)` Prefix `QualityEnv.Enter(__instance)` / Postfix `Exit()`。**原因**：非激活星球每 15 tick 也跑 assembler tick，用 `GameMain.localPlanet` 会串星球，必须用 `FactorySystem.planet.id`。
- `Patches\P0_Assembler.cs`：两个补丁类挂同一方法。
  - `Prefix(ref AssemblerComponent __instance)`：跳过 `recipeExecuteData?.requires` 为空/`enabled=false`；逐原料槽 `served>0 && incServed>0` 才算，初始 `lvl=Cargo.kIncLevelMax`，`lvl=min(lvl, incServed/served)`（clamp 0..10）→ `QualityData.SetInputLevel(planetId,eid,lvl)`。
  - `Postfix(ref AssemblerComponent __instance)`：`produced[]` 差值入池；首次观察（prevProduced<0）**只建基线不入池**（防读档积压被当新产出）；`delta>0` 时每 tick 每台至多 roll 一次 → `AddPool(planetId,eid,quality*delta,delta)`。
- `Patches\P0_PickFrom.cs`：自实现 assembler 分支（文档 §3.4），非 assembler 类型/任何异常 `return true` 回原版；`lock(entityMutexs[entityId])` 内重取 assemblerPool 项后按 products/produced/filter/needs[0..5] 取货；**唯一新增** `inc=(byte)QualityData.TakeOneProductInc(...)`；取不到 `__result=0`。
- `Diag\Probes.cs`：F5 制造机前 10 台状态 / F7 最近 20 次 PickFrom ring / F8 四表 / F9 buildingInc 直方图（全部只读、异常只 LogWarning）。
- `Plugin.cs` 改 v0.2.0：`Update` 拆 `PollStartup()` + `HandleHotkeys()`（F5/F7/F8/F9）；PatchAll 后打印 `patchedMethods=N`。
- `QualityData.cs` 扩：`prevProduced` 新扩槽填 -1、`GetPoolInc/GetPoolCount`、`GetPrevProduced`（越界 -1）、`SetPrevProduced`、热路径星球缓存；`ClearEntity` 重置 prevProduced=-1。
- `Diag\StartupAudit.cs` 增 2 条自检：`AssemblerComponent.InternalUpdate(float,int[],int[])`、`FactorySystem.GameTick(long,bool)`。

构建/部署：`BUILD OK` 0 警告 0 错误；`DEPLOY OK`，SHA256=`384564A5F00CD4CB2AB5E6A331B320083B2A84887C37BBF7B98A708028C83C4B`（mtime 2026-10-22 18:59:03）。

进游戏实测（pid 31032，主菜单，enabled=true）：
- `=== DSP Quality System v0.2.0 ===`；`Harmony PatchAll OK, harmony=2.9.0.0 patchedMethods=4`（UIGame._OnCreate / FactorySystem.GameTick / AssemblerComponent.InternalUpdate / PlanetFactory.PickFrom）。
- 4 表正确；`[Audit]` 7 条签名全 `-> OK`（含新增 2 条）；Ability 1/2/4 OK；`startup report complete`；本 Mod 零异常/零 warning。
- 待进存档：F5/F7 验证「熔炉进 4 点矿 → 出 4 点磁铁」「多原料取 min」。

M1 首版踩坑 1：`[HarmonyPatch(typeof(PlanetFactory),"PickFrom", new[]{... typeof(byte).MakeByRefType() ...})]` 编译报 `CS0182 特性实参必须是常量表达式`；改用 `[HarmonyPatch(typeof(PlanetFactory))]` + `static MethodBase TargetMethod()` 反射精确取 uint 重载（运行时自检确认命中的就是那一个）。

M1 首版踩坑 2（Lead review 发现，会让 M1 验收直接失败的时序 bug）：首版 `Prefix` 每 tick 用 `served/incServed` 重算并覆盖 `inputLevel`，某槽 `served<=0` 就写 0。但原版 `InternalUpdate`（asm_full.cs:25685-25737）**只在开新批的瞬间算一次批品质**（`num7 = min_i split_inc_level(served,incServed,requireCounts)`，`split_inc_level` 返回消耗前每件平均值），一批生产中 `served` 会被扣空 → 慢速供料机器整批期间 inputLevel 被写成 0，产出全部 0 品质。已改为"预测 + 批边界提交"：
- `Prefix`：只写 `pendingLevel`（= 若本 tick 能开批的批品质，条件与 asm_full.cs:25696 完全一致：`served[i] >= requireCounts[i] && served[i] > 0`；`incServed==0` 不算不可开批，avg 自然为 0）与 `wasReplicating`（call 前 `replicating` 快照）；每 tick 先复位 `pendingLevel=-1`，**不写 inputLevel**。
- `Postfix`：① 先用**旧** `inputLevel` 把 `produced[]` 差值入池（extraTime 额外产出在同一 call 开头、属当前批，天然被差值覆盖，不单独 roll）；② 再判 `started = replicating && pending>=0 && (producedThisCall>0 || wasReplicating==0)` 才 `SetInputLevel(pending)` —— 覆盖"空闲→开批"与"产出+同 call 重开批"两种情况；原料不足/产出上限提前 return 时不提交。
- `QualityData` 新增 `short[] pendingLevel`（扩容填 -1）、`byte[] wasReplicating`，`inputLevel` 扩容/`ClearEntity` 一律复位 **-1**（-1=未观测，0 是有效品质）。
- `Diag\Probes` F5 增列 `pending=`、`repl=`，F5/F7 头部打印 `env=QualityEnv.CurrentPlanetId`，便于进档第一眼确认星球作用域。
- 修正后 `BUILD OK`，DLL SHA256=`38212FB0B19A8733AC16679DE67548E9D7D7656FEB60C0AFB12CB3CD5A01E60C`（mtime 2026-10-22 19:06:09）；待部署进档验证。

## 4.1 M2（T3）建筑品质：文件与实现（2026-10-22 19:13 部署）

- `BuildQualityStash.cs`（新）：一次 `CreatePrebuilds` 批次的 FIFO 手持物品品质队列（`BeginBatch/EndBatch/Push/TryPopFor`，上限 1024；itemId 不匹配时整批丢弃并只告警一次——宁可没有品质，不能给错建筑）。
- `Patches\P1_BuildCapture.cs`（新，4 个补丁类）：
  - `Player.UseHandItems` Postfix：`perItem = useInc/used`（clamp 0..10）→ `Push(inhandItemId, perItem)`；只在批次打开期间接收。
  - `BuildTool_Click.CreatePrebuilds` Prefix/Postfix：Prefix `BeginBatch()`；Postfix 遍历 `__instance.buildPreviews`，`objId < 0` 的即新 prebuild，`TryPopFor(preview.item.ID)` 命中则 `QualityData.SetPrebuildInc(-objId, perItem)`，结束 `EndBatch()`。
  - `PlanetFactory.AddPrebuildDataWithComponents` Postfix：`ClearPrebuild(__result)` 清掉 prebuildId 复用可能残留的陈旧值（Lead review 加的保险）。
  - `PlanetFactory.AddEntityDataWithComponents` Postfix：`TakePrebuildInc(prebuildId)` → `SetBuildingInc(__instance.planetId, __result, min(10,perItem))`；蓝图/快速建造不经过 `CreatePrebuilds` ⇒ 自动 0 品（符合 §2.5）。
- `Patches\P1_Cleanup.cs`（新）：`PlanetFactory.DismantleFinally` Postfix，`objId>0` → `ClearEntity(planetId,objId)`（buildingInc/pool/inputLevel/pending/prevProduced 全清）；`objId<0` → `ClearPrebuild(-objId)`（取消未建成的预建）。
- `QualityData.cs` 增 `SetPrebuildInc/TakePrebuildInc/ClearPrebuild`（`prebuildInc` 字典随 ClearAll 一并清）。
- `Diag\Probes.cs`：F5 已含 buildingInc/pending/repl；F9 直方图加 `buildingInc==null` 保护；新增 `[Probe][Build]` 日志（批次捕获前 10 条、entityId 绑定前 20 条）；新增 **F10 调试注入**（手持物品 inc=4/件，`Player.SetHandItemInc_Unsafe(4*count)`，LogWarning 标 DEBUG INJECTION）。
- `QualityConfig.cs` 新增 `debugProbes`（默认 true，仅控制 F10 测试注入；**不影响正常玩法**，用户日常可设 false）。
- `Plugin.cs` v0.3.0：PatchAll 后打印 `patched:` + 全部被补丁方法名；`Update` 增加 F10。
- R-2 升品：已在 `QualityRules.RollCycleQuality`（M1 时即就位）——`chance = clamp(buildingInc * buildingChancePerPoint, 0, maxUpgradeChance)`，命中则产物品质 +1，受 `maxQuality` 上限约束；每台每周期最多 +1。

部署与验证（主菜单，pid 14968，19:14:11）：
- `BUILD OK` 0 警告 0 错误；`DEPLOY OK`，SHA256=`6CD3902A03C855D64D2FA1058E250F7D1F2CA10922A9BF2A60EE6D62A6F36458`。
- `Harmony PatchAll OK, patchedMethods=9`；方法名：AssemblerComponent.InternalUpdate / FactorySystem.GameTick / PlanetFactory.PickFrom / UIGame._OnCreate / Player.UseHandItems / BuildTool_Click.CreatePrebuilds / PlanetFactory.AddPrebuildDataWithComponents / PlanetFactory.AddEntityDataWithComponents / PlanetFactory.DismantleFinally。
- `[Audit]` 7 条全 OK + Ability 1/2/4 OK；本 Mod 零异常。

**M2 验收用测试配置（必须先改 cfg 再启动游戏，cfg 不热重载）**：`BepInEx\config\dsp.qualitysystem.cfg` 里 `buildingChancePerPoint=1000`、`maxUpgradeChance=100`（默认值分别是 **1.5 / 60**）。用户读档测试流程：F10 注入手持 4 品 → 放下建筑 → F5 看 `buildingInc=4` → F9 分布 → 下游产物 inc 升高。**测完必须恢复 1.5/60。**

### M1 进档实测（Lead 用旧构建 0.2.0 在用户存档复验，2026-10-22 19:0x）
- `[Probe][F5] planet=103 cursor=2 enabled=True` / `eid=54 recipe=2 inputLevel=4 poolInc=0 poolCount=0 buildingInc=0`
- `[Probe][F7] total=715`，最近 20 次全 `eid=54 item=1102 inc=4`（1102=磁铁）⇒ **4 点矿 → 磁铁 4 点**，M1 主链（Prefix→池→PickFrom 写 inc）在单原料熔炉上成立。修复版（批边界提交）需用户在本轮进档后回归确认仍为 inc=4。

## 5. 文档 vs 实际反编译差异（按实际实现，已同步 Lead）

1. `Cargo` 实际是 `public struct Cargo`（asm_full.cs:26899），4 张表是其 `public static` 字段，`Cargo.incTable` 可正常读写；文档表述为静态类无影响。
2. `UIGame._OnCreate` 在主菜单阶段就会被调用（早于 `LDB.items` 就绪），因此它只能做兜底重应用；启动自检/建表日志放在 `LDB ready` 轮询里。
3. 运行中修改 cfg 文件**不会**触发 BepInEx 5.4.23.5 热重载（实测无 `SettingChanged`）；`enabled` 的运行时切换逻辑已保留（`SettingChanged` 订阅，宁可不用），验收按"重启生效"执行。
4. `Cargo` 另有 `accTable/accTableMilli/incFastDivisionNumerator/incFastDivisionDenominator/fastIncArrowTable`（加速类）；按文档**不修改**。
5. `giveDismantleInc` 本期为暂缓项：配置项保留，M1/M2 **不得**让它生效（拆除返还品质不做）。
6. 文档 §3.2 的 Prefix 写法（每 tick 用 `served/incServed` 重算 inputLevel）与原版时序不符：原版 `AssemblerComponent.InternalUpdate` 只在**开新批**瞬间（asm_full.cs:25685-25737）计算批品质 `num7`，生产中 `served` 被 `split_inc_level` 扣空；M1 实际实现改为「Prefix 只预测 pendingLevel + wasReplicating；Postfix 先按旧 inputLevel 入池，再判批边界提交」（详见 M1 踩坑 2）。Lead 会同步到文档 §3.2「批边界」规则。
7. 文档 §3.6 的 `if (__result <= 0) return;` 会漏掉最常见路径：`Player.UseHandItems`（asm_full.cs:170099-170111）在**手持数量恰好等于 useCnt**时消耗物品但 `return inhandItemCount`（已被置 0）⇒ 返回 0。实际实现用 Prefix 快照的 `inhandItemCount` 兜底（`inhandItemCount==0 && __result==0` 时 `used=快照件数`），首次触发打一条 Info 说明。
8. 文档 §2.4 之外新增 `debugProbes`（默认 true）：仅放开 F10 调试注入（手持物品 4 品），Lead 批准用于 M2 验收；不影响正常玩法。
9. `PlanetFactory.AddPrebuildDataWithComponents` 返回的 prebuildId 是池索引、会复用；实际实现加了 Postfix `ClearPrebuild(__result)` 清陈旧值（Lead review 建议）。

## 6. 待办

- [x] T1/M0：骨架 + 4 张线性表 + 启动自检 + enabled=false 还原
- [x] T2/M1：制造台输入品质 + produced[] 差值入池 + 自实现 PickFrom（代码部署 + 主菜单验证通过；旧构建进档实测 4 点矿→4 点磁铁；修复版等本轮进档回归）
- [ ] T3/M2：建造捕获/绑定/清理 + R-2 升品（代码部署 + 主菜单验证通过；等用户进档实测 buildingInc=4 与产物升品）
- [ ] T4/M3：矿机写 inc + JSON 存档
- [ ] T5：§6.3 十条回归 + 离线单测（池守恒/Config clamp/JSON 往返）
- [ ] **测试完恢复 cfg 默认值** `buildingChancePerPoint=1.5`、`maxUpgradeChance=60`（当前测试值 1000/100）

## 7. M3（T4）—— 矿机写 inc + JSON 存档（代码完成并部署，2026-10-22 19:22，待进档验收）

交付文件（均在 `DSPQualitySystem\`，单文件 <500 行）：
- `QualityRules.cs` 新增 R-3：`RollMinerQuality(planetId, entityId)` —— 采矿机每输出一件，按 `minerChancePerPoint`（%/点）掷骰，命中则该件矿 inc=1（每件最多 1 点，符合文档 §3.5"只写 0/1"）。建筑无点数时直接返回 0，零开销。
- `Patches\P0_Miner.cs`（新）：`MinerComponent.InternalUpdate(PlanetFactory,VeinData[],float,float,float,int[])` 的 transpiler（asm_full.cs:57199）。在方法内定位唯一一处
  `factory.InsertInto(insertTarget, 0, productId, (byte)num17, 0, out remainInc)`（asm_full.cs:57429），把第 5 参 `ldc.i4.0` 替换为
  `ldarg.1`(factory) + `ldarg.0`(ref this) + `ldfld MinerComponent.entityId` + `call QualityData.MinerOutputInc(PlanetFactory,int)` + `conv.u1`。
  运行日志 `[QSystem][miner] output inc hook installed (1 site).`；任何反射/IL 形态不符只打一条 warning 并放弃（不炸矿机）。
- `QualityData.cs` 新增：`EnsureCapacity(int planetId, int capacity)`、`SetPool(int,int,int,int)`、`MinerOutputInc(PlanetFactory factory, int entityId)`（内部先 TryGet 再 roll，异常吞掉返回 0）。`planets` 字典改为可枚举供存档写盘。
- `Save\JsonLite.cs`（新）：无第三方依赖的最小 JSON（`Dictionary<string,object>` / `List<object>` / string / long / double / bool / null）。
- `Save\QualitySave.cs`（新）：文档 §4.2 schema `{"version":1,"gameName":"...","planets":{"103":{"buildings":[{"e":22,"i":4}],"pools":[{"e":54,"pi":7,"pc":2}]}}}`，只写非零项；路径 `Paths.ConfigPath\DSPQualitySystem\saves\<存档槽名>.json`（槽名做非法字符替换）；写盘用 `.tmp` + 原子 Move；读档先 `QualityData.ClearAll()` 再按 `GameMain.data.factories[planetId].entityPool.Length` 扩容后回填。JSON 与 `enabled` 解耦：开关关闭也照常读写，避免切换开关丢品质（游戏逻辑补丁仍全部受 `enabled` 控制）。
- `Patches\P1_SaveHooks.cs`（新）：`GameSave.SaveCurrentGame(string)` Postfix → 写盘；`GameSave.LoadCurrentGame(string)` Postfix → 读档；`GameData.NewGame(GameDesc)` Postfix → 清空。均判 `__result`，异常走 `WarnOnce`。
- `Diag\Probes.cs` 新增 **F11**（与 F10 同 `debugProbes` 门控）：把鼠标指向/检视中的建筑 `buildingInc` 设为 4，日志 `[Probe][F11] DEBUG INJECTION: entityId=... proto=... buildingInc=4`。
- `Diag\StartupAudit.cs` 新增 4 条自检：`MinerComponent.InternalUpdate(PlanetFactory,VeinData[],float,float,float,int[])->uint`、`GameSave.SaveCurrentGame(string)->bool`、`GameSave.LoadCurrentGame(string)->bool`、`GameData.NewGame(GameDesc)->bool`。
- `Plugin.cs` → v0.4.0，`Update` 热键加 F11。

构建 / 部署 / 启动验证（pid 36952，2026-10-22 19:22）：
- `BUILD OK` 0 警告 0 错误；`DEPLOY OK`，SHA256=`C41CB3A7757B4C342E5355CCA6EA8AAE59CEA0CECDE215E4FB065E50256F8A50`。
- `patchedMethods=13`：AssemblerComponent.InternalUpdate、FactorySystem.GameTick、**MinerComponent.InternalUpdate**、PlanetFactory.PickFrom、UIGame._OnCreate、Player.UseHandItems、BuildTool_Click.CreatePrebuilds、PlanetFactory.AddPrebuildDataWithComponents、PlanetFactory.AddEntityDataWithComponents、PlanetFactory.DismantleFinally、**GameSave.SaveCurrentGame**、**GameSave.LoadCurrentGame**、**GameData.NewGame**。
- `[Audit]` 11 条全 `-> OK`（含新增 4 条）+ Ability 1/2/4 OK；本 Mod 零异常。

测试配置（本轮进档前已改好，**验完恢复默认**）：`buildingChancePerPoint=1000`、`minerChancePerPoint=1000`、`maxUpgradeChance=100`、`debugProbes=true`（默认 1.5 / 2 / 60 / true）。

待用户进档验证清单（Lead 安排一次进档验完 M2 升品 + M3）：
1. 采矿机采若干件 → F7 应出现 `item=<矿> inc=1`（测试配置下必出；默认 2%/点 × 4 点 = 8%）。
2. 用 4 点矿进熔炉 → F7 磁铁 inc 上升 1（测试配置每批必升，上限 maxQuality=10）；对照组 0 点矿不变。
3. 对准运行中的熔炉按 F11（`buildingInc=4`）→ 数批后 F7 产物 inc 增加，验证 R-2。
4. 手动存档或让游戏写入 `_lastexit_` → 退出重进同一存档：F5 `buildingInc` 保留、F9 分布不变、`...\config\DSPQualitySystem\saves\<槽名>.json` 存在且只含非零项；日志 `[QSystem][save] wrote ...` / `loaded ...`。
5. 拆掉 4 品熔炉 → F9 该点数消失（清理生效），原地重建为 0 品（不继承）。

文档 vs 实际差异（追加，按实际实现，Lead 可同步文档）：
- #10 文档 §3.5 的 "byte 上限分块" 在本版不可达：原版 `num17` 被 clamp 到 1..4（asm_full.cs:57425-57427），单次最多输出 4 件且每件最多 1 点，`itemInc*count ≤ 4` 远小于 255；`InsertInto` 自身会对 inc 溢出的栈做 lane split。故未实现分块，transpiler 只替换 inc 常量。
- #11 Lead 建议的 F11 取 `actionBuild.coverObjId` 实际不存在（`coverObjId` 是 `BuildPreview` 字段，asm_full.cs:141452），实现改用 `player.controller.actionInspect.hoveringEntityId`（回退 `inspectId`）；`Player` 本身也没有 `actionInspect` 字段，必须经 `player.controller`。
- #12 `GameData.NewGame(GameDesc)` 是实例方法、返回 bool（asm_full.cs:192864）；存档钩子相应按 `__result` 判断成功再读写。

### 6.1 待办状态更新（2026-10-22 19:22）
- [x] T2/M1：Lead 已在用户存档复验 4 点矿→磁铁 4 点（旧构建）＋修复版（批边界）已部署，待本轮进档确认 inc 仍为 4。
- [x] T3/M2：Lead 已实机验收（F10→放三栋→F5 buildingInc=4、F9 分布正确、无错位），待本轮进档补验"R-2 产物升品"。
- [ ] T4/M3：代码/部署/主菜单验证完成，待进档验证（矿机 inc、存档往返）。
- [ ] T5：§6.3 十条回归 + 离线单测（池守恒 1e6、Config clamp、JSON 往返）。
- [ ] 测试完恢复 cfg 默认值。

## 8. T5 离线单测 + JSON 存档边界分析（2026-10-22 19:3x，用户进档测试期间不部署）

### 8.1 可测逻辑抽取：`QualityMath.cs`（新，纯函数、无 BepInEx/Unity/游戏类型）
为避免"测试只测测试代码"，把出厂路径的算法抽成纯函数，插件与离线测试链接**同一份源码**：
- `AddPool(poolInc,poolCount,incPoints,productCount,out newInc,out newCount)`：池写入（productCount<=0 无操作，负点数按 0）。
- `TakeOne(poolInc,poolCount,maxLevel,out newInc,out newCount)`：池读出（`floor(poolInc/poolCount)`，clamp 0..maxLevel，扣 1 件；余数留在池里）。
- `ClampLevel(level,maxLevel)` / `ClampPercent(percent)`。
- `UpgradeChance(buildingInc,chancePerPoint,maxChance)`（R-2）/ `MinerChance(buildingInc,chancePerPoint)`（R-3）。
- `RollPercent(percent, rng)`：0.01% 分辨率（`rng.Next(10000) < percent*100`）。
调用方已改为委托：`QualityData.AddPool/TakeOneProductInc`、`QualityRules.ClampLevel/RollCycleQuality/RollMinerQuality/RollPercent`。**行为不变**（重构后 `build.ps1` BUILD OK、0 警告 0 错误）。注意：已部署的 v0.4.1（SHA `5A30FC92…EC82E`）是重构前的 DLL，下次部署会产生新 SHA，届时需再跑一次主菜单验证。

### 8.2 离线测试工程：`tests\OfflineTests\`（net10.0，无第三方包）
- `OfflineTests.csproj`：`EnableDefaultCompileItems=false`，链接 `..\..\QualityMath.cs` 与 `..\..\Save\JsonLite.cs`；不引用游戏/BepInEx。主工程 csproj 已加 `<Compile Remove="tests\**" />`，测试代码不会进插件 DLL。
- `Program.cs` 7 组用例，逐组打印 `[TEST] ... PASS/FAIL`，结尾 `ALL PASS` / `FAILURES: n`（有失败时进程退出码非 0）。
- 一条命令跑：`powershell -NoProfile -ExecutionPolicy Bypass -File "C:\WorkWork\DSP MOD\DSPQualitySystem\tests\run-tests.ps1"`（脚本 dot-source `tools\dspenv.ps1` 复用便携 SDK/离线 NuGet 重定向，输出 `TEST PASS` / `TEST FAILED`）。

实测结果（2026-10-22）：`ALL PASS (7 checks)`，`TEST PASS`。覆盖与观测值：
1. **池守恒 1,000,000 次随机 add/take 操作**：每步断言 `poolInc>=0`、`poolCount>=0`、逐项均值不超 maxLevel；最终 `totalAdded - totalTaken == poolInc`（点数不丢）；覆盖空池取件、0 件写入、负点数 clamp 等边界。
2. Config/规则 clamp：level 0..10 边界、percent 0..100、`UpgradeChance(4,1.5,60)=6%`、`(4,1000,100)=100%`、`(4,1000,60)=60%`、`MinerChance(4,2)=8%`、clamp 到 100%。
3. JSON 往返：中文/引号/反斜杠/换行/制表/控制字符/U+0001/emoji 代理对全部 round-trip；按 §4.2 全文档解析（version=1、gameName 中文、buildings 的 e/i、pools 的 pi/pc 均为 long）；负整数、double、true、null、空对象/空数组；尾随垃圾与未闭合字符串必须抛 `FormatException`。
4. R-3 分布：8% 实测 **8.026%**、2% 实测 **1.964%**（1e6 次），100% 必出。
5. R-2 100% 必升品：从 0 级 10 个周期内必到 maxLevel；默认 4 点建筑 6%/周期实测 **6.034%**（1e6 次）。
6. 存档身份校验（详见 8.3）：seed/gameName 一致 → 接受；seed 不一致 → `seed mismatch: file=... expected=...` 拒绝；gameName 不一致 → 拒绝；**种子缺失（老文件）/seed=0（无游戏数据）→ 容错放行**；version=2 → 拒绝；null 根 → 拒绝；被接受的文档数据完整。

### 8.3 JSON 存档边界（Lead 指定分析 + seed/gameName 防串档已实现）
**已实现（本轮，未部署）：`Save\SaveGuard.cs`（纯函数，可离线测试）**
- 存档头新增 `seed` 字段：`{"version":1,"gameName":"<槽名>","seed":<galaxySeed>,"planets":{...}}`；seed 取 `GameMain.data.gameDesc.galaxySeed`（`GameData.gameDesc` 在 asm_full.cs:192776；`GameDesc.galaxySeed` int，:193879 类 / :193889 字段），取不到（主菜单/无 data）记 0。
- 读档校验顺序：根是对象 → `version==1` → `gameName` 与"本次读档槽名"一致 → `seed` 一致；任一不符 ⇒ **整体丢弃**（`QualityData.ClearAll()` 已先行、返回 0），日志 `[QSystem][save] seed mismatch: file=424242 expected=424243 (slot='...', path) -> ignored, starting fresh`（gameName/version 同理）。
- 向后兼容：老 json 没有 `seed` ⇒ 读作 0 ⇒ 跳过 seed 检查；`gameName` 为空/期望值为空同样跳过。`version` 字段仍是 1（新增字段不算破坏性变更）。
- 单测覆盖：一致接受/两向不一致拒绝/缺失 seed 容错/null 根/version=2；`ALL PASS (7 checks)`。
- 文件名撞名（`a:b`/`a?b` → `a_b.json`）不为它增加复杂度：seed+gameName 校验把"错误应用"降级为"忽略"。

**边界结论（按当前实现）**：
- **换存档/新游戏旧文件残留**：每档一个 json，文件名=游戏传入的存档槽名（`GameSave.SaveCurrentGame/LoadCurrentGame` 的字符串，`_autosave_0`、`_lastexit_`、手动档各自独立），互不覆盖。`GameData.NewGame` Postfix 只 `ClearAll()` 内存，**不删除**旧 json；新游戏若从未保存就退出，同槽旧 json 仍在，下次读同名槽会因 seed（不同银河种子）或 gameName 不同而**被丢弃**——不再把旧品质套进新档。
- **存档名含中文/特殊字符**：`SanitizeFileName` 把 `\ / : * ? " < > |` 与 `Path.GetInvalidFileNameChars()` 统一替换为 `_`，再 `Trim()`，空名回退 `unknown_save`；中文正常保留（UTF-8 写盘、NTFS 合法）。已知副作用：不同槽名若只差非法字符会**撞名**（如 `a:b` 与 `a?b` → `a_b.json`），极端长名可能超 Windows 路径长度（未截断/未 hash）——由 seed/gameName 校验兜底为"忽略"。
- **`.dsv` 被外部改名**：文件名与 json 文件名不再对应 → `Load` 找不到 json，按"无数据、全新开始"处理（打 `no quality data for '<slot>'` 日志），不崩溃、不误读别的档。改名后的档品质丢失属预期限制（json 与游戏存档无内部 id 绑定）；若玩家把 json 也一起改名，seed 一致则仍可加载。
- **读档时实体数变少**：`EnsurePlanetCapacity` 按当前 `factory.entityPool.Length` 扩容，`SetBuildingInc/SetPool` 只对合法 entityId 生效；超出当前池的旧条目不会越界（数组比当前池大，读时越界检查返回 0）。entityId 复用由 `DismantleFinally` 清理 + `AddEntityDataWithComponents` 覆盖写入兜底；`Load` 先 `ClearAll()` 再回填，不会残留上一档内存。注意数组容量只增不减（可接受的内存开销，非泄漏）。
- 其他：`Load` 对版本不符/根不是对象/planets 缺失/JSON 语法错 → `LogWarning` 并留空（不崩读档）；写盘先写 `.tmp` 再 `File.Move`，避免半写文件；`GameSave` 钩子串行执行（存档在主线程），无并发写。

### 8.4 待办（更新）
- [x] T5 离线单测（池守恒 1e6 / Config clamp / JSON 往返 / R-3 分布 / R-2 必升品）一条命令可跑，输出 `ALL PASS`
- [ ] T5 §6.3 十条端到端回归（需用户进档配合；结果写 `TESTLOG.md`）
- [ ] 用户测完后：恢复 cfg 默认值（`buildingChancePerPoint=1.5`、`minerChancePerPoint=2`、`maxUpgradeChance=60`，用 `tools\restore-defaults.ps1`）→ 重新部署（v0.4.2：QualityMath 重构 + SaveGuard）→ 主菜单复验 → complete task-3/task-4 → task-5

## 9. Park 期交付（离线可完成项；全程未碰游戏进程 / 已部署 DLL / 用户 cfg）

### 9.1 新增文件
- `README.md`：面向用户的定位/安装/Config 全表/玩法说明/已知限制/调试热键（不含内部实现细节）。
- `tools\package.ps1`：一条命令 `build.ps1 -Deploy` → 把 `bin\Release\DSPQualitySystem.dll` + `README.md` 打包为 `dist\DSPQualitySystem-<version>.zip`（版本号从 `Plugin.cs` 的 `VERSION` 读取；用 .NET `System.IO.Compression.ZipFile`，无外部 zip 依赖）；`-SkipDeploy` 只打包不部署；打印 `PACKAGE OK` / `PACKAGE VERSION` / `PACKAGE SIZE` / `PACKAGE SHA256` / `PACKAGE DLL SHA256`。
- `tools\restore-defaults.ps1`：把 cfg 三项测试值写回默认（`buildingChancePerPoint=1.5` / `minerChancePerPoint=2` / `maxUpgradeChance=60`）；`[IO.File]::ReadAllLines` + 逐行精确替换，保留 BepInEx 注释与文件其余内容；打印 `RESTORE OK` + 三行替换后原文供核验。**未执行**。

### 9.2 版本与产物（v0.4.2，尚未部署）
- `Plugin.cs`：`VERSION` 由 `0.4.1` 提升为 **`0.4.2`**（内容 = QualityMath 纯函数重构 + SaveGuard seed/gameName 校验 + 矿机/存档补丁）。
- `build.ps1`：`BUILD OK`，0 警告 0 错误。
- `bin\Release\DSPQualitySystem.dll`：SHA256 `5BB81F9975A4BFF8594D5C38584BA9FC26D6FAA4952969EE22DCBBCF0DC68596`（post-audit 加固版；`tools\package.ps1 -SkipDeploy` 产出）。
- `tools\package.ps1 -SkipDeploy` 实测：`PACKAGE OK -> dist\DSPQualitySystem-0.4.2.zip`，`PACKAGE SIZE 25776`，ZIP SHA256 `315BDFC4A0B70F8E3C9AAB0A8589C5DA1C491002B05847AC8498BA772420DB76`；ZIP 内容 = `DSPQualitySystem.dll` + `README.md`。
- **v0.4.3（矿机堆叠 inc 修复 + F6 矿机诊断探针，见 §14，尚未部署）**：`Plugin.cs VERSION=0.4.3`；`BUILD OK` 0 警告 0 错误；`bin\Release\DSPQualitySystem.dll` SHA256 **`1267ED1CB1111AE3EF1E070208D2B76988DEA5E68E2542B4648698C4F2F84FBC`**（60,928 B）；`tests\run-tests.ps1` → `ALL PASS (7 checks)`。**部署时以这个 SHA 为准**（v0.4.2 的 5BB81F99、v0.4.3 中途的 E7968901 / DC8603B4 均作废；DC8603B4 是 F13 版，用户要求改绑 F6 后重新构建为 1267ED1C）。
- `tools\restore-defaults.ps1` 在 cfg 的**临时副本**上 dry-run：`RESTORE CHANGED 3 line(s)`，文件 50 行 → 50 行，仅 3 行变化（其余含注释逐字节保留）。

### 9.3 部署 + 主菜单验证完整命令序列（等 Lead 说"可以部署"后照此执行）
```powershell
$game = 'C:\Games\Steam\steamapps\common\Dyson Sphere Program'
$rt   = 'C:\WorkWork\DSP MOD\DSPQualitySystem'

# 1) 构建 + 部署 + 打包（打印 BUILD OK / DEPLOY OK / PACKAGE OK 与新 SHA）
powershell -NoProfile -ExecutionPolicy Bypass -File "$rt\tools\package.ps1"

# 2) 启动到主菜单（约 26-40s）
cd 'C:\WorkWork\DSP MOD\DSP-MPD-runtime-debug'
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev-loop.ps1 -Mode up

# 3) 取主菜单证据（patchedMethods=13 / Audit 全 OK / miner hook installed / 零异常）
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev-loop.ps1 -Mode log -Lines 200 -Filter 'QSystem|Audit'

# 4) 退出并归档日志
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev-loop.ps1 -Mode down

# 5) 测试结束后恢复默认 cfg（用户测试完成、不再需要测试配置时）
powershell -NoProfile -ExecutionPolicy Bypass -File "$rt\tools\restore-defaults.ps1"
```
主菜单必须出现的行：`=== DSP Quality System v0.4.3 ===`、`patched:` 13 个方法（见 9.4）、`[Audit] ... -> OK` 全通过、`startup report complete`；且无 `[Error :DSP Quality System]`（既有 CommonAPI Nebula 缺依赖警告与本 Mod 无关）。

### 9.4 预期 patched 方法清单（13）
`AssemblerComponent.InternalUpdate, FactorySystem.GameTick, MinerComponent.InternalUpdate, PlanetFactory.PickFrom, UIGame._OnCreate, Player.UseHandItems, BuildTool_Click.CreatePrebuilds, PlanetFactory.AddPrebuildDataWithComponents, PlanetFactory.AddEntityDataWithComponents, PlanetFactory.DismantleFinally, GameSave.SaveCurrentGame, GameSave.LoadCurrentGame, GameData.NewGame`

## 10. 矿机 transpiler「退化保护」自审（Lead 指定，2026-10-22 追加）

**代码位置**：`Patches\P0_Miner.cs`（`Miner_InternalUpdate_OutputInc.Transpiler` / `Transpile`）。
**总原则**：任何失败分支只打**一条 WARNING** 并返回**原始指令列表**，绝不半装、绝不抛异常；矿机 tick 保持原版行为（M3 静默降级，其它补丁不受影响）。

### 10.1 失败分支与判定条件（全部实测于本机 asm_full.cs:57414-57435 的当前布局）

| 阶段 | 判定条件 | 行为 / 日志 |
|------|----------|-------------|
| 反射预取 | `InsertIntoInt == null`（`PlanetFactory.InsertInto(int,int,int,byte,byte,out byte)` 签名变化/被移除）、`Helper == null`（`QualityData.MinerOutputInc(PlanetFactory,int)` 被改）、`EntityIdField == null`（`MinerComponent.entityId` 被改名） | `WARNING [QSystem][miner] reflection lookup failed (InsertInto/Helper/entityId), miner quality disabled`，返回原 IL |
| 定位调用 | 遍历 `codes[2..]`，找 `Call/Callvirt` 且 `operand is MethodInfo == InsertIntoInt`；找不到（含被别的 Mod 换掉引用） | `WARNING [QSystem][miner] output InsertInto call not found, miner quality disabled`，返回原 IL |
| 校验前两条指令 | 要求 `codes[i-2].opcode == Ldc_I4_0`（第 5 参 itemInc=0）且 `codes[i-1].opcode == Ldloca_S/Ldloca`（out remainInc 地址）；任一不符（如游戏改成先存局部变量、参数顺序变化、多一层 dup） | `WARNING [QSystem][miner] unexpected IL around miner output InsertInto (itemInc arg), miner quality disabled`，返回原 IL。**保守策略：宁可失效也不错改**（错改会把 itemCount/remainInc 槽位写错，可能损坏矿机产出） |
| 整体异常 | `Transpile` 抛出任何异常（Harmony 边界、null、越界等） | `WARNING [QSystem][miner] transpiler disabled, keeping vanilla IL: <exception>`，返回原 IL。**Transpiler 异常会中止 PatchAll，可能连带其它补丁，必须兜住** |
| 成功 | 命中且替换完成 | `INFO [QSystem][miner] output inc hook installed (1 site).`；`patched==1` 后立刻 `break`（该方法只有这一个出货 InsertInto，不重复装） |

**原子性**：所有检查都在 `RemoveAt/InsertRange` 之前完成；一旦开始替换就只替换这一处并 `break`，不存在"替换一半后发现不对"的状态。

### 10.2 byte 上限与 clamp 说明（已写入 P0_Miner.cs 注释）
- `InsertInto(..., byte itemCount, byte itemInc, out byte remainInc)` 的 **itemInc 是 byte**；注入值为 `num17 * QualityData.MinerOutputInc(...)` 经 `conv.u1` 写入（`num17` = 本叠件数，见 §14）。
- `MinerOutputInc` = R-3 判定，只返回 **0 或 1**（每件最多 1 点）；`num17` 被原版 clamp 到 **1..4**（asm_full.cs:57425-57427）⇒ 一叠总点数 `≤ 1*4 = 4`（若未来允许 10 点/件则 ≤ 40），远小于 255，无需分块。
- 品质上限 `maxQuality` 由 `QualityConfig.MaxLevel` clamp 到 `Cargo.kIncLevelMax=10`。**若未来把 `MinerOutputInc` 改成可返回多点，必须先在该 helper 内 clamp 到 0..10，再乘件数**。
- 运行时可从启动日志区分：有 `[QSystem][miner] output inc hook installed (1 site).` = 装上；只有 WARNING = 已降级，TESTLOG 第 5 条应据此判定"未生效"而不是"未通过"。

## 11. PickFrom 复刻逐行终审（原版 asm_full.cs:217948-218019 → 实现 P0_PickFrom.cs）

**结论**：assembler 分支逐行等价（含 case 1/2/default、`filter`/`needs[0..5]` 判定、`produced[j]--`、`return` 值、锁范围）；**唯一有意的行为差异 = 写回 `inc` 品质点**；另有 3 处**防御性差异**（只影响异常输入，正常游戏路径等价）。

### 11.1 逐行对照

| 原版（asm_full.cs:行号） | 原版行为 | 我的实现（P0_PickFrom.cs:行号） | 说明 |
|---|---|---|---|
| :217948 方法签名 `PickFrom(uint,int,int,int[],out byte,out byte)` | 两个重载中的一个 | :33 Prefix 签名；:22-31 `TargetMethod()` 反射精确绑定 `typeof(byte).MakeByRefType()` | 特性写法会 CS0182，故用 TargetMethod；StartupAudit 自检该重载存在 |
| :217950-217951 `stack=1; inc=0;` | 默认值 | :36-37 | 完全一致（品质点稍后覆盖 inc） |
| :217952 `num=(int)(ioTargetTypedId & 0xFFFFFF)` | 目标 id | :57 `assemblerId` | 一致 |
| :217954 `switch((EFactoryIOTargetType)(typedId & 0xFF000000u))` | 按类型分派 | :51 非 `Assembler` ⇒ `return true` | 其它类型交回原版处理（Belt/Ejector/Silo/…全部未改） |
| :217968 `ref AssemblerComponent reference5 = ref factorySystem.assemblerPool[num]` | 直接索引 | :53-61 先判 `system/assemblerPool` 非空、`0 < assemblerId < Length`，越界 ⇒ `return true` | **防御差异 D1**：越界时交回原版（原版会抛异常；正常调用不会发生） |
| :217969-217972 `if (reference5.id <= 0) return 0;` | 锁外返回 0 | :88 `current.id <= 0` ⇒ `__result=0; return false`（锁内，重读后判） | 行为等价（结果都是 0）；我的重读避免数组扩容竞态 |
| :217973 `int entityId6 = reference5.entityId;` | 取实体 id | :64 | 一致（锁外取值，同原版） |
| :217974 `lock (entityMutexs[entityId6])` | 加锁 | :81 | 一致（锁同一 mutex） |
| :217976-217977 `array2 = ...recipeExecuteData?.products; produced2 = ...produced;` | **锁内**重读 | :82-84 | 一致（锁内重读整个 component） |
| :217978-217981 `if (array2 == null) return 0;` | products 空 ⇒ 0 | :88 `products == null` ⇒ 0 | 一致；另加 `produced == null` 防御（D2） |
| :217982 `int num2 = array2.Length;` | 槽数 | :145-149 `count = min(products.Length, produced.Length)` | **防御差异 D3**：原版假设两数组等长；正常数据等长，等价 |
| :217986-217990 case 1 判定 + `produced2[0]--` + `return array2[0]` | 单产物 | :151-168 单循环 | 判定合取式完全一致：`products[i]>0`（:154）→ `produced[i]>0`（:154）→ `filter==0||filter==products[i]`（:158）→ `needs==null||any needs[0..5]==products[i]`（:162，`NeedsMatch` :178）；命中 `produced[i]--`（:167）→ `return product`（:168） |
| :217992-218003 case 2（先槽 0 后槽 1） | 按槽序 | :151 循环 i=0,1 | 顺序一致（先 0 后 1），首个命中即返回 |
| :218004-218014 default（j=0..n-1） | 按槽序 | :151 循环 0..count-1 | 一致 |
| :217988/217995/218000/218010 `produced2[j]--` | 命中即扣 | :167 | 一致，且在锁内（:81-103） |
| :217989/217996/218001/218011 `return array2[j]` | 返回值 | :168 → :112 `__result=picked` | 一致 |
| :217951 `inc` 恒 0 | 原版不写品质 | :99-102 / :111 命中后 `pickedInc=(byte)TakeOneProductInc(...)`，锁内取池 | **唯一有意的行为差异**：产物携带品质点（M1 的目标）。取池也在锁内，保持 `produced--` 与 `pool--` 原子 |
| :218018 `return 0`（未命中） | 0 | :105-108 `__result=0; return false` | 一致 |
| （无）异常处理 | 原版不抛业务异常 | :116-129 catch → WarnOnce；若 `picked>0`（已扣 produced）返回该物品 **inc=0** 且 `return false`，否则 `return true` | **安全加固**：避免异常后 `return true` 让原版二次 `produced--` 造成物品复制/丢失 |

### 11.2 锁范围对照
- 原版：从 `lock(entityMutexs[entityId6])`（:217974）到 `return 0`（:218018），覆盖 products/produced 读取、判定、`produced--`、返回值。
- 实现：`lock`（:81）→ 重读 component、判定、`produced--`、`TakeOneProductInc`（:98-102）→ 出锁；`__result` 赋值与探针记录（:111-113）在锁外。
- 结论：与原版相比**临界区只多了一次取池**，没有把锁扩大到调用方可见范围；探针记录在锁外避免日志时间拖长临界区。
- 其它差异：`enabled=false` 时 :39-42 直接 `return true`（完全原版路径）；`planetId` 取 `__instance.planetId`，取不到回退 `QualityEnv.CurrentPlanetId`（:75-79）。

### 11.3 游戏更新后如何重做本对照（30 分钟流程）
1. 重新反编译 `Assembly-CSharp.dll` 到 `_analysis\quality-system\asm_full.cs`（工具：`_tooling\Decomp\bin\Release\net10.0\Decomp.exe`，用法见开发文档 §1.8）。
2. `grep -n "public int PickFrom(uint ioTargetTypedId" asm_full.cs` → 找到新起始行，检查 assembler 分支的 case 1/2/default 三段与锁边界是否仍如上表。
3. 重点核对：类型掩码仍是 `0xFF000000u`/`0xFFFFFF`、`needs[0..5]` 仍是 6 槽、`produced[j]--` 仍在 return 前、`recipeExecuteData.products` 语义未变。
4. 若签名/分派变化 → 先改 `TargetMethod()` 的 `Type[]`（编译期报错最好），再看 `PickProduct` 判定是否需要跟进；改完跑 `build.ps1` + 主菜单 `[Audit]` 7 条自检。

## 12. P2 分馏塔（Fractionator）设计稿（只设计，不实现；供开发文档 §3.11）

**目标**：分馏塔的**成功产物**携带输入品质（并可被建筑品质升品），失败回收的流体继续保留输入品质（原版已做）。**最小改动两处 IL** + 一处 helper，不 Prefix 复刻整个方法。

### 12.1 原版事实（asm_full.cs）
- `public struct FractionatorComponent` :53449；关键字段：`entityId` :53453、`fluidId`/`productId` :53473-53475、`fluidInputCount` :53477、`fluidInputCargoCount` :53479、`fluidInputInc` :53481、`productOutputCount` :53483、`fluidOutputCount` :53485、`fluidOutputInc` :53487、`progress` :53489、`fractionSuccess` :53491、`incUsed` :53493、`produceProb` :53471、`seed` :53505。
- `incLevel` :53507（`fluidInputInc/fluidInputCount` clamp 10）、`extraIncProduceProb` :53524（**用 `Cargo.accTableMilli[num]`，原版特例**，即 inc 提升成功率而非品质）。
- `InternalUpdate(PlanetFactory, float, SignData[], int[], int[])` :53702；每 10000 progress 一次判定（:53725-53761）：
  - `num3 = fluidInputInc>0 && fluidInputCount>0 ? fluidInputInc/fluidInputCount : 0`（:53727，本次输入每件平均点数）
  - `fractionSuccess = rand < produceProb * (1 + Cargo.accTableMilli[min(num3,10)])`（:53732-53733）——**P2 风险点：品质仍会提升成功率**
  - 成功：`productOutputCount++; productOutputTotal++; productRegister[productId]++`（无 inc！:53736-53741）
  - 失败：`fluidOutputCount++; fluidOutputInc += num3`（:53749-53751，回收流体保留点数）
  - 每轮都 `fluidInputInc -= num3`（:53754）
- 产物出货唯一路径（:53866）：`if (belt0 > 0 && isOutput0 && productOutputCount > 0 && cargoTraffic.TryInsertItemAtHead(belt0, productId, 1, 0)) productOutputCount--;` —— **第 4 参 inc 硬编码 0**（`CargoTraffic.TryInsertItemAtHead(int beltId,int itemId,byte stack,byte inc)` :83578）。
- 失败流体出货（:53778/:53785、:53826/:53833）：`TryUpdateItemAtHeadAndFillBlank(fluidId, ceil(num-0.1), 1, (byte)num4)`，`num4 = fluidOutputInc/fluidOutputCount` ⇒ 原版已把输入品质均分回流体。

### 12.2 最小改动两处 IL（transpiler 方案）
- **站点 A（成功判定分支 → 记品质池）**：锚点 `productOutputTotal++` 的 IL（`ldfld productOutputTotal; ldc.i4.1; add; stfld`，唯一出现在 :53737）。在其后插入：
  `ldarg.1`（factory）+ `ldarg.0`（ref FractionatorComponent）+ `ldfld entityId` + `ldloc num3`（需转成 int；num3 是编译器生成的局部变量）→ `call int QualityData.FractionatorProduceInc(PlanetFactory,int,int)`。
  helper 语义：`q = inputLevel(num3)`（clamp 0..10）→ 按 `QualityRules.RollCycleQuality(planetId, entityId, q)` 应用 R-2 建筑升品 → `QualityData.AddPool(planetId, entityId, q, 1)`（每成功一件入 1 件）→ 返回 void。**注意**：`productOutputCount++` 与池写入必须在同一 tick 内一一对应；若担心 `num3` 局部变量槽位不稳定，可改为读 `__instance.fluidInputInc/fluidInputCount`（在插入的 IL 里用 `ldarg.0; ldfld ...` 重算，无需引用局部变量）。
- **站点 B（产物出货 → 写 inc）**：锚点 `call CargoTraffic.TryInsertItemAtHead(int,int,byte,byte)`（:53866，唯一调用）。把第 4 参之前的 `ldc.i4.0` 替换为：`ldarg.1`（factory）+ `ldarg.0` + `ldfld entityId` + `call int QualityData.TakeOneProductIncFor(PlanetFactory,int)`（从池取一件，空池返回 0）+ `conv.u1`。
  **与矿机实现的差异**：矿机是"概率产生点数"（helper 里 roll），分馏塔是"池里已有逐件点数"（成功时入 1 件），所以 `TakeOneProductIncFor` 名字区分、语义为 `TakeOneProductInc` 的薄封装（复用现有池逻辑）。
- 出货失败（`TryInsertItemAtHead` 返回 false）时 `productOutputCount--` 不执行 ⇒ 池点数**不会被消耗**，下一 tick 重试配对，天然安全。

### 12.3 风险与待决问题
1. **双重效果**：原版 `extraIncProduceProb`/成功判定用 `accTableMilli`（我们不改 acc 表 ⇒ 输入品质仍+成功率），P2 再给产物品质 = 同一品质既加成功率又加产物点数（双收益）。建议：P2 首版**保留**该特例（少改 IL、行为可解释），文档记录为设计选择；若要"品质只影响产物"，需把 :53733 的 `accTableMilli[...]` 替换为 1（多一站 IL，且影响原版平衡）。
2. **局部变量槽位**：`num3` 是方法内局部，transpiler 引用 `ldloc` 需确认编译器分配（Release 下顺序稳定但随版本可能变）。稳妥做法：helper 只用 `factory + entityId + 从字段重算 num3`，完全不碰局部变量（站点 A 采用此方案）。
3. **方法体数百行**（:53702-53880，~180 行 IL 更上千条）⇒ **不能用 Prefix 复刻**（要复制锁、局部状态、出货逻辑，风险远高于两处 transpiler）；与矿机一样采用"锚点 + 失败即原版"的退化保护。
4. **多产物/多配方**：分馏塔每台一个 `productId`（`SetRecipe` 决定），池按 entityId 即可；但若同 entityId 换配方，池中旧产品点数会被新配方消耗——建议 `SetRecipe` patch 或池键加 productId（P2 实现时定，最简：换配方时 `QualityData.ClearPool(eid)`）。
5. **存档**：池数据已在 `QualitySave` 的 pools 里按 entityId 存，分馏塔无需新增 schema；但 §4.2 的 `buildings` 也应记录分馏塔 buildingInc（建造捕获已覆盖所有建筑，因为走 `AddEntityDataWithComponents`）。
6. **性能**：站点 A/B 每 tick 至多一次（10000 progress 才判定一次；出货每 tick 检查一次），池操作 O(1)，可接受；日志只在安装/降级时输出。

## 13. 收尾序列（用户补测矿机完成后执行，逐步确认）

前置：Lead 确认用户已退出游戏（`dev-loop -Mode status` 显示 `GAME_EXITED`）；未退出前**不得**执行 2-4 步。

1. **恢复默认 cfg**（当前测试值 buildingChancePerPoint=1000 / minerChancePerPoint=1000 / maxUpgradeChance=100，必须恢复）：
   `powershell -NoProfile -ExecutionPolicy Bypass -File "C:\WorkWork\DSP MOD\DSPQualitySystem\tools\restore-defaults.ps1"`
   期望输出 `CFG RESTORED`；人工核对 `BepInEx\config\dsp.qualitysystem.cfg`：enabled=True、incPerPoint=100、powerPerPoint=250、buildingChancePerPoint=1.5、minerChancePerPoint=2、maxUpgradeChance=60、maxQuality=10、debugProbes=True。
2. **构建**：`powershell -NoProfile -ExecutionPolicy Bypass -File "C:\WorkWork\DSP MOD\DSPQualitySystem\build.ps1"` → 期望 `BUILD OK`、`0 Warning(s) 0 Error(s)`。
3. **部署**：`… build.ps1 -Deploy` → 期望 `DEPLOY OK -> …\BepInEx\plugins\DSPQualitySystem\DSPQualitySystem.dll` 且 `DEPLOY SHA256 E7968901260C002AADA40784DF046CAC843D2C1D961A02D6854540269622BC1D`（与 `bin\Release\DSPQualitySystem.dll` 一致）。
4. **启动验证**：`cd "C:\WorkWork\DSP MOD\DSP-MPD-runtime-debug"; powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev-loop.ps1 -Mode up`（等待 `ready after … gameLoaded=False`）。核对日志：`=== DSP Quality System v0.4.3 ===`、`patchedMethods=13` + 13 个方法名、`[QSystem][miner] output inc hook installed (1 site).`、`[Audit]` 11 条全 `-> OK`、Ability 1/2/4、本 Mod 零 Error/Exception；归档文件名记入 TESTLOG。
5. **汇报 Lead**：新部署 SHA256 + patched 名单 + 主菜单关键日志行 + `dev-loop -Mode log -Filter 'QSystem|Audit'` 结果。
6. **TESTLOG 收尾**：补第 5 条（矿机实测证据）、第 8/10 条（若用户顺带验证过）、更新头部版本行为 v0.4.3 新 SHA、写总结（PASS 数 / 未测项 / 已知限制）。
7. **任务板**：complete task-4（等矿机条 PASS）→ complete task-5（T5 全部完成后）。
8. **给 Lead 的最终汇总**：文件清单+行数（`Get-ChildItem -Recurse *.cs | Measure-Object Lines` 等价命令）、构建/部署/测试命令、已知限制、文档差异清单（此前 DEVLOG 差异 #1-#12 + 本文件 §10-§14）。

## 14. 矿机堆叠 inc 稀释 bug 修复 + F6 矿机诊断探针（Lead review，v0.4.3，尚未部署）

### 14.1 真 bug：itemInc 是「整叠总点数」，不是「每件点数」
- 现象：用户 F11 给矿机 4 品后，F12 `hits=0`（旧版注入值恒为 1，4 件一叠 ⇒ 1/4 点/件 ⇒ 取整 0 ⇒ 品质丢失）。
- 原版 IL 实测（本机 `Assembly-CSharp.dll`，`MinerComponent.InternalUpdate`，用 `tools\il.exe … MinerComponent InternalUpdate 6` 导出）：
  `084B ldarg.1` / `084C ldarg.0` / `084D ldfld insertTarget` / `0852 ldc.i4.0` / `0853 ldarg.0` / `0854 ldfld productId` / `0859 ldloc.s V_27` / `085B conv.u1`（= itemCount num17） / `085C ldc.i4.0`（= itemInc，待替换） / `085D ldloca.s V_28`（out remainInc） / `085F callvirt PlanetFactory::InsertInto(int,int,int,byte,byte,byte&)`。
- 语义确认：`InsertInto` 传送带分支 → `CargoTraffic.TryInsertItem(beltId, offset, itemId, itemCount, itemInc)` → `CargoPath.TryInsertItem` → `InsertItemDirect`（asm_full.cs:83567 / :216912）；装配台分支是 `incServed[i] += itemInc`（后续除以 served 才是每件）。**itemInc 是整叠总点数**。
- 修复（P0_Miner.cs 注入序列，替换 `085C ldc.i4.0`）：
  `Dup`（复制栈上已有的 itemCount）→ `Ldarg_1`（factory）→ `Ldarg_0`（ref MinerComponent）→ `Ldfld entityId` → `Call MinerOutputInc(factory,eid)`（返回 0/1）→ `Mul`（count × level）→ `Conv_U1`；随后原版 `ldloca remainInc; callvirt InsertInto` 不变。
- 上限分析：`level(≤1) × num17(≤4) ≤ 4`；未来若 level 上限 10 ⇒ `≤40`，仍远小于 byte 255，**无需分块**。
- 已知近似：一叠只 roll 一次再乘件数（同叠同品质），不是逐件独立 roll；默认 2%/点下与逐件 roll 的期望一致、方差略大，可接受，记入已知限制。

### 14.2 F6 矿机诊断探针（原 F13；鼠标指向建筑；`debugProbes=true` 门控）
> 用户反馈「哪来的 F13 按键」⇒ 按 Lead 指令改绑空闲键位 **F6**（2026-10-22）；探针内容不变，日志标签同步为 `[Probe][F6]`。

输出固定前缀 `[Probe][F6]`：
1. 基础行：`entityId / proto / buildingInc`；非矿机打 `not a miner (minerId=…)`。
2. 矿机行：`minerId / type / workstate / productId / productCount / time / period / insertTarget / veinCount / currentVeinIndex / speedDamper / minimumVeinAmount / costFrac`。
3. `stall checks` 行：`productCount>=50` ⇒ 出矿带堵满（原版只在 `productCount < 50` 时继续采）；`veinCount==0` ⇒ 无矿脉可采。
4. `power:` 行：`pcId / consumer.entityId / networkId / workEnergyPerTick`；`networkId==0` ⇒ 无电，原版 `power<0.1` 直接 early-out。
5. `vein:` 行：`EMinerType.Vein` 时打当前矿脉 `veinId / productId / amount / groupIndex`；无效/枯竭/无矿脉会明示。
6. 目标行：`target proto / beltId / assemblerId / storageId / inserterId`；目标是传送带时再打 `beltId / segPathId / segIndex / cargoCount`（判断出矿带堵塞）。

用途：区分「矿机真停摆」「无电」「矿脉枯竭（覆盖 0 矿脉）」「出矿带堵满 → productCount 顶到 50 → 矿机按设计睡觉」四类原因。字段来源已核对：`FactorySystem.minerPool` :98054、`MinerComponent` 字段 :56871 起、`EntityData.beltId/assemblerId/storageId/minerId/inserterId` :191008-191022、`CargoTraffic.beltPool/pathPool` :81779/:81769、`BeltComponent.segPathId/segIndex` :26655/:26657、`CargoPath.cargoContainer` :27013、`CargoContainer.cargoCount` :81428、`PowerSystem.consumerPool` :105333、`PowerConsumerComponent.networkId` :58817。

### 14.3 F12 增强
`[Probe][F12] miner quality hits=N calls=M lastMinerCall=eid=X level=Y enabled=… minerChancePerPoint=…`：`calls` 统计钩子**每次被调用**（含 level=0），可区分「钩子没被调用」与「调用了但 roll 出 0」。

### 14.4 构建与验证
- `build.ps1` → `BUILD OK` 0 警告 0 错误；DLL SHA256 **`1267ED1CB1111AE3EF1E070208D2B76988DEA5E68E2542B4648698C4F2F84FBC`**（60,928 B）；`tests\run-tests.ps1` → `ALL PASS (7 checks)`。版本 v0.4.3。
- **源文件拆分（<500 行约束）**：`Diag\Probes.cs` 565 → 383 行 + 新 `Diag\ProbesMiner.cs`（F6/F12/R-3 统计，`public static partial class Probes`）；`QualityData.cs` 474 行、`Save\QualitySave.cs` 408 行，全部 <500。
- **尚未部署**（用户正在存档内，按 Lead 指令只编译不重启；IL 注入已按上面原版指令序列逐条核对，运行时效果待下次进档用 F12/F6 实机验证）。

### 14.5 排查案例：「矿机停摆」≠ 钩子故障（2026-10-22 用户实测）
- 现象：用户 F11 给矿机加点后认为矿机"停摆"，F12 `hits=0`，日志无异常。
- 定性（用户截图 + Lead 核验）：该矿机面板显示 **`覆盖 0 个矿脉` / 矿点全部枯竭 / 无矿物**，供电 24.0kW 100% 正常。⇒ 矿脉早被采空，矿机本来就不出矿，`hits=0` 是正常结果，**不是 mod、不是缺电、不是 transpiler**。
- 结论：F6 的 `vein:` 行正是为这种情况加的（`veinCount=0` / 当前矿脉无效/枯竭会直接打印）；以后再报"矿机不出矿"，先按 F6 → 看 `vein:` 与 `stall checks` 两条即可定性，不要再怀疑钩子。


## 15. 交接打包（2026-10-23）

- 交接目标：换一台机器继续开发。主文档升 **v3.0 交接版**（1036 行，替换了 §0/§3.0/§3.5/§4/§5/§6/§7/§8.A/§8.D/§9，并新增 §10 迁移与打包指南）；交接包根新增 `HANDOFF.md`。
- 工程可移植化（三处）：
  1. `DSPQualitySystem.csproj`：`<Version>0.4.3</Version>`；新增 `UnityRefDir` 解析——游戏存在就用 `$(GameManaged)`，否则回退 `tools\unity-refs`；target 改名 `CheckUnityRefs`。
  2. `build.ps1` / `tests\run-tests.ps1`：没有 `dspenv.ps1` 时打印 `[env] no dspenv.ps1 ... using dotnet from PATH`，用 `$env:DSP_DOTNET` 或 PATH 上的 `dotnet`；找不到游戏则不带 `GameDir` 编译（靠 `tools\unity-refs`），`-Deploy` 时才报错。
  3. 新增 `nuget.config`（`<clear/>` + 本地源 `tools\nuget-local`）与 `tools\nuget-local\`（11 个 nupkg，23.4 MB）、`tools\unity-refs\`（69 个 UnityEngine*.dll，6.2 MB）。
- 干净环境验证（`_analysis\quality-system\cleanroom\`）：模拟无游戏（`DSP_GAME_DIR=...\no-such-game`）+ **清空 NuGet 缓存** + 只允许离线源 → `BUILD OK`（restore 2.43s、0 警告 0 错误）+ `ALL PASS (7 checks)`，restored 包 9 个。
- 交接构建产物：`bin\Release\DSPQualitySystem.dll` SHA256 **`6E3E8676718D5925637CEC3488AF3338EB53C9DF6F577A792068B4202F4B40E8`**（60,928 B，AssemblyVersion 0.4.3.0；旧 SHA `1267ED1C…4FBC` 仅差程序集版本元数据）；安装包 `dist\DSPQualitySystem-0.4.3.zip`（28,026 B，SHA256 `C7A28B1F212F964B7675BA7C3FC2271520B08D16AFC751A47211C000F884DF11`）。**未部署**（游戏里仍是 v0.4.1）。
- 环境坑（已写进 HANDOFF §4）：便携 `dotnet.exe` 直接跑会因 `C:\Users\<中文名>\AppData\Local\Temp` 不可写而 MSB1025 失败，必须重定向 `DOTNET_CLI_HOME / NUGET_PACKAGES / TMP / TEMP / APPDATA / LOCALAPPDATA`；PowerShell 5.1 读中文用 `[IO.File]::ReadAllText($p,[Text.Encoding]::UTF8)`，`.ps1` 保持纯 ASCII。
## 16. M4/P2 分馏塔品质（v0.5.0，代码完成 + 离线验证通过，尚未部署）

### 16.1 交付文件（行数全部 <500）
- 新增 `QualityData.Fractionator.cs`（120 行，`public static partial class QualityData`）：`FractionatorProduceInc(PlanetFactory,int,int,int)`、`TryInsertFractionatorProduct(CargoTraffic,int,int,PlanetFactory,int)`、`ClearPool(int,int)`；`QualityData.cs` 474 → 475 行并改 `partial`（拆文件维持 <500 行约束）。
- 新增 `Patches\P0_Fractionator.cs`（319 行）：`Fractionator_InternalUpdate_ProductInc`（transpiler，双 IL 锚点）+ `Fractionator_SetRecipe_ClearPool`（Prefix）。
- `QualityMath.cs`（144 行）：新增纯函数 `FractionatorInputLevel(int fluidInputInc, int fluidInputCount, int maxLevel)` —— 复刻原版 num3 = `fluidInputInc / fluidInputCount` 再 clamp 0..maxLevel；任一参数 ≤0 ⇒ 0。离线测试可直接 foreach 调用。
- `Diag\StartupAudit.cs`（224 行）：新增两个反射自检 + 一行锚点状态。
- `Plugin.cs` / `DSPQualitySystem.csproj`：版本 0.5.0。
- `tests\OfflineTests\Program.cs`（463 行）：新增 2 个用例（总 9）。
- `README.md`：玩法说明补分馏塔一行；"分馏塔未改"改为"双重收益（首版保留，非 bug）"。

### 16.2 真实 IL 依据（必须用游戏安装目录的 dll）
- `tools\il.exe <dll> <type> <method> <paramCount>` dump；**NuGet gamelibs 的 Assembly-CSharp.dll 是 stub**（方法体只有 `ldnull; throw`），不能作为 IL 基准。基准 dll：`C:\Program Files (x86)\Steam\steamapps\common\Dyson Sphere Program\DSPGAME_Data\Managed\Assembly-CSharp.dll`。UTF-8 副本：`tools\tmp\fractionator_iu_utf8.txt`（660 行）、`tools\tmp\fractionator_sr_utf8.txt`（SetRecipe，74 行）。
- 签名：`System.UInt32 FractionatorComponent::InternalUpdate(PlanetFactory, System.Single, SignData[], System.Int32[], System.Int32[])`；`System.Void FractionatorComponent::SetRecipe(System.Int32, SignData[])`。`FractionatorComponent` 是 **struct**（asm_full.cs:53449；AssemblerComponent:25050、MinerComponent:56871 同为 struct），调用点 `fractionatorPool[l].InternalUpdate(...)`（asm_full.cs:99070）⇒ `ldarg.0` 是数组元素地址（&this）。
- 站点A（偏移 0x0197–0x01A0）：`ldarg.0; ldarg.0; ldfld productOutputTotal; ldc.i4.1; add; stfld productOutputTotal`，紧接 `ldarg.s productRegister`（进 lock）。全方法唯一 `stfld productOutputTotal`（0199 `ldfld` / 01A0 `stfld`）。
- 站点B（偏移 0x0656–0x066A）：`ldloc.3; ldarg.0; ldfld belt0; ldarg.0; ldfld productId; ldc.i4.1; ldc.i4.0; callvirt CargoTraffic::TryInsertItemAtHead(int,int,byte,byte); brfalse IL_067a`，失败跳过 0x066C–0x0675 的 `productOutputCount--`。全方法唯一该 4 参重载调用（0656 前面是 0x064D 的 gate `productOutputCount > 0`）。

### 16.3 注入与失败退化
- 站点A（stfld productOutputTotal 之后插入 9 条）：`ldarg.1; ldarg.0; ldfld entityId; ldarg.0; ldfld fluidInputInc; ldarg.0; ldfld fluidInputCount; call QualityData.FractionatorProduceInc(PlanetFactory,int,int,int); pop`。**不引用 num3 局部变量**（其栈槽是编译器实现细节），helper 从 struct 字段重算。
- 站点B（替换 `ldc.i4.1`、`ldc.i4.0`、`callvirt` 三条）：栈上已有 `cargoTraffic / belt0 / productId` 作为前 3 参，注入 `ldarg.1; ldarg.0; ldfld entityId; call QualityData.TryInsertFractionatorProduct(CargoTraffic,int,int,PlanetFactory,int)`。helper 内 `TakeOneProductInc` → `TryInsertItemAtHead(beltId, productId, 1, (byte)inc)` → **失败且拿走点数 >0 时 `AddPool` 回滚**，并返回原版 insert 结果 ⇒ 外层 `brfalse / productOutputCount--` 控制流完全不变。
- 原子性：先在完整指令表上做「唯一性 + 邻接指令 + 异常块（`blocks` 非空即拒绝）」校验，两者全部通过后才在 `List` 副本上替换（先站点B、后站点A，站点A 索引不受影响）；任一锚点缺失/不唯一/形状不符/异常 → **返回未修改的原始 IL**、只 warn 一次、置 `Installed=false`（不做部分注入）。
- 校验条件摘要：站点A 前 5 条必须依次 `Add / Ldc_I4_1 / Ldfld productOutputTotal / Ldarg_0 / Ldarg_0`；站点B 令 `b=call-2`，要求 `b≥7`、`[b]=Ldc_I4_1`、`[b+1]=Ldc_I4_0`、`call-3=Ldfld productId`、`call-4=Ldarg_0`、`call-5=Ldfld belt0`、`call-6=Ldarg_0`、`call-7=ldloc.3`，且 `b..call` 无异常块标记，站点A 在站点B 之前。
- 相对设计稿 §12.2 的两处偏差（**Lead 已批准**，上报时说明理由）：
  1. 设计稿站点B 只把 inc 实参 `ldc.i4.0` 换掉 ⇒ `TakeOneProductInc` 在 `TryInsertItemAtHead` 之前求值，insert 失败时池点已被消耗且无法回滚，与 §12.3「失败不消耗、下 tick 重试」矛盾（传送带堵满时每 tick 丢 1 点）；改为 take+insert+回滚的单一 helper，兑现设计意图。
  2. helper 参数不取 `ref FractionatorComponent`（避免 byref IL 类型争议/旧 HarmonyX JIT 风险），只传 `entityId + fluidInputInc + fluidInputCount` 三个字段值。

### 16.4 SetRecipe 清池防串味
`[HarmonyPatch(typeof(FractionatorComponent), "SetRecipe", new[] { typeof(int), typeof(SignData[]) })]`，`Prefix(ref FractionatorComponent __instance)` → `QualityData.ClearPool(QualityEnv.CurrentPlanetId, __instance.entityId)`。原版只在机器完全空（fluidId == 0）时才调 SetRecipe，所以清池不会丢掉仍在机内的产物点数。

### 16.5 可观测文本（启动日志 / 审计）
- 成功：`[QSystem][fractionator] anchors ok, pooled=on siteA=1 siteB=1 (quality inc injected; belt insert is atomic take/rollback)`
- 失败（只 warn 一次）：`[QSystem][fractionator] anchor NOT FOUND (<原因>), vanilla fallback: fractionator quality disabled`
- SetRecipe 首次生效：`[QSystem][fractionator] SetRecipe prefix active, pool cleared on recipe change (first call planet=N entity=M)`
- 审计行（LDB ready 后）：`[Audit] FractionatorComponent.InternalUpdate anchors -> OK (siteA=1 siteB=1, pooled=on)` 或 `-> NOT INSTALLED (vanilla fallback, reason: ...)`；另新增两行 `FractionatorComponent.InternalUpdate(PlanetFactory,float,SignData[],int[],int[]) -> OK`、`FractionatorComponent.SetRecipe(int,SignData[]) -> OK`。
- **patchedMethods：13 → 15**（+`FractionatorComponent.InternalUpdate`、+`FractionatorComponent.SetRecipe`），实机看 `[QSystem] Harmony PatchAll OK … patchedMethods=15`。

### 16.6 构建与离线测试（本机，未部署）
- `build.ps1` → `BUILD OK`，**0 警告 0 错误**；`bin\Release\DSPQualitySystem.dll` SHA256 **`B7522F547528820AA983A68CF96F930B6BC888F9709CAD6C0DFD9C26FF263482`**（68,096 B，AssemblyVersion 0.5.0.0）。
- `tests\run-tests.ps1` → `ALL PASS (9 checks)`；新增用例：`fractionator input level (num3 recompute + clamp)`（11 断言）、`fractionator pool round trip (produce / ship / rollback)`（3×10 万次：单件必定等值出货；失败回滚复原 inc/count；混合守恒 `added - takenTotal == remaining` 且 `inc ≤ count*maxLevel`）。

### 16.7 已知风险 / 需实机验证
1. 分馏塔实机是否按「成功 1 件 → 池 +1 件 → 出货带该点数」工作（本次只做到离线逻辑 + 对真实 IL 的静态核对；运行时用同一 DLL，锚点应命中，日志可直接判断）。
2. **accTableMilli 双重收益**：输入品质既提高分馏成功率（原版），又传给产物（本 Mod），首版按设计保留，未"修正"。
3. 站点B「传送带满不消耗点数」依赖 helper 回滚；实机可在产品积压后观察重试是否仍带同样点数。
4. SetRecipe 触发时机为机器全空；若原版在配方未变时也调用，清池无副作用（此时池必为空）。
5. 存档无需新 schema：池仍在 `pools`（按 entityId），已被现有 `QualitySave` 覆盖（读档后用 F10/F11 或产物点数验证）。
6. 旧 HarmonyX（运行时 2.5.5）兼容：属性写法与 P0_Miner 完全同类（类级 target + 方法名参数表），未用 2.10 独有 API；万一 PatchAll 报 `You cannot combine TargetMethod...`，退路是 `harmony.Patch(method, transpiler: ...)` 手动注册。

## 17. P3 阶段1：建筑品质 → 制造/采集速度（v0.6.0，代码完成 + 离线验证通过，尚未部署）

### 17.1 交付文件（行数全部 <500）
- 新增 `Patches\P3_Speed.cs`（367 行）：`internal static class P3_Speed`，`Apply(Harmony)` 手动注册、`AssemblerPrefix`/`MinerPrefix` 两个 Prefix、`Describe()` 状态串、可观测计数器与 debug 行。
- `QualityMath.cs`（144 → 164 行）：新增纯函数 `SpeedFactor(int buildingInc, float percentPerPoint, float maxBonusPercent)` —— 返回 `1 + min(inc*percent, cap)/100`；任一分量 ≤0 时返回 1（0 点=原速）。离线测试可直接 foreach 调用。
- `QualityConfig.cs`（68 → 104 行）：新增 `speedBonus`（bool，默认 true）、`speedPerPoint`（float，默认 2.0）、`maxSpeedBonus`（float，默认 50，单位 %）；clamp 访问器 `SpeedPerPointClamped`（0–100）、`MaxSpeedBonusClamped`（0–500）。
- `QualityEnv.cs`（78 → 102 行）：新增 `AmbientPlanetId`（当前串行 tick 的 planet，工作线程为 -1）与 `_lastPlanetId`（最近一次串行 tick 的 planet，供并行路径兜底），`CurrentPlanetId` 回退顺序 = ambient → 最近串行 planet → `GameMain.localPlanet`。
- `Plugin.cs`（213 → 221 行）：VERSION 0.6.0；PatchAll 名单后 `Patches.P3_Speed.Apply(harmony)` + `[QSystem][speed]` 状态行；`LogConfig` 追加 speed 三项。
- `Diag\StartupAudit.cs`（224 → 227 行）：新增 `[Audit] <P3_Speed.Describe()>` 一行。
- `DSPQualitySystem.csproj`：`<Version>0.6.0</Version>`。
- `tests\OfflineTests\Program.cs`（463 → 500 行）：新增用例 `P3 speed factor (monotonic / cap / zero-point untouched)`（总 10）。
- `README.md`：配置表新增 3 项、玩法说明新增速度一条、已知限制更新。

### 17.2 真实 IL 依据（必须用游戏安装目录的 dll）
- 基准 dll：`C:\Program Files (x86)\Steam\steamapps\common\Dyson Sphere Program\DSPGAME_Data\Managed\Assembly-CSharp.dll`；UTF-8 dump 副本 `tools\tmp\assembler_iu_utf8.txt`（694 行）、`tools\tmp\miner_iu_utf8.txt`（876 行）。
- 签名：`System.UInt32 AssemblerComponent::InternalUpdate(System.Single power, System.Int32[] productRegister, System.Int32[] consumeRegister)`；`System.UInt32 MinerComponent::InternalUpdate(PlanetFactory factory, VeinData[] veinPool, System.Single power, System.Single miningRate, System.Single miningSpeed, System.Int32[] productRegister)`。两者均为 struct（asm_full.cs:25050 / :56871），调用点 `fractionatorPool…` 同类写法（`fractionatorPool[l].InternalUpdate` :99070；assembler 串行 :99052、并行 GameLogic._assembler_parallel :259393；miner 串行 :98929）。
- 参数名通过 `tools\api.exe <Assembly-CSharp.dll> <Type>` 确认（`power` / `factory` 等），Harmony 按名绑定安全。
- **speed 是存档字段**：`AssemblerComponent.Export` :25094 写 `speed` :25101 / `speedOverride` :25103 / `extraSpeed` :25105，`Import` :25150 读回 :25162/:25166/:25168；`MinerComponent.Export` :56927 写 `speed` :56934 / `speedDamper` :56935，`Import` :56959 读回 :56966/:56969。⇒ 不能写这些字段（会污染存档且下周期被重算覆盖）。
- **power 是每 tick 的临时局部**：assembler 全方法只读 3 次（IL 偏移 `0000` gate、`0576` `time += (int)(power*speedOverride)`、`058D` `extraTime += (int)(power*extraSpeed)`）；miner 全方法只读 5 次（`0000` gate + `0039`/`0112`/`04AF`/`06D1` 四处 `time += ...` 进度累加；`miningSpeed` 另用于 :57416 堆叠估算，故不碰）。乘在 power 上 ⇒ 只影响本栋建筑的进度累加，不写任何字段、不碰存档。

### 17.3 注入方案（Prefix 乘算 power，手动注册）
- 注册：`Plugin.Awake` 在 `harmony.PatchAll` 之后调用 `P3_Speed.Apply(harmony)`；用反射取 `AssemblerComponent.InternalUpdate(float,int[],int[])` / `MinerComponent.InternalUpdate(PlanetFactory,VeinData[],float,float,float,int[])` 与对应 prefix，`harmony.Patch(target, prefix: new HarmonyMethod(prefix) { priority = Priority.Low })`（运行时 0Harmony 与构建引用 HarmonyX 2.7.0 都有该 6 参重载与 `Priority.Low`；已用 il.exe 核对编译产物确实调到该重载）。
- 手动注册（非 `[HarmonyPatch]` 属性）的目的：旧 HarmonyX 下任何一个目标/prefix 反射失败只跳过该目标并 warn，不会让整个 PatchAll 报错；`ArmedAssembler/ArmedMiner` 与 `Describe()` 让日志/Audit 直接看出"未武装"。
- 顺序：P0_Assembler 已有第一个 Prefix（品质 roll），P3 的 Prefix 用 `[HarmonyPriority(Priority.Low)]` + `HarmonyMethod { priority = Priority.Low }` 显式钉在 Normal 之后（两个 Prefix 逻辑无耦合：品质只读 level，P3 只改 power；写死顺序是为了不依赖默认值）。Miner 侧目前只有 transpiler，无 Prefix 冲突，仍同样设置。
- 前缀逻辑：`power < 0.1f` 早退（与原版缺电 gate 一致，不越过 gate）；`planetId`/`entityId` 无效早退；`level = QualityData.GetBuildingInc(planetId, entityId)`；`factor = SpeedFactor(level, speedPerPoint, maxSpeedBonus)`；`factor <= 1` 不改值；否则 `power *= factor`。assembler 的 planetId 取 `QualityEnv.CurrentPlanetId`（无 factory 参数），miner 取 `factory.planetId`。
- 被否方案：① transpiler 改 `speedOverride/extraSpeed`（存档字段 + 每周期重算覆盖）；② 直接写 `speed` 字段（存档污染）；③ 改 miner 的 `miningSpeed` 参数（除进度外还影响 :57416 自动插入堆叠估算）；④ Postfix（进度已在本 tick 加完，来不及）。
- `patchedMethods` 增量 = **0**（两个方法本就已被 P0_Assembler/P0_Miner patch，总数仍 15；`GetPatchedMethods()` 按方法去重，新增的是 2 个 Prefix processor；Plugin 在 `Apply` 之前打印计数，实机应看到 `patchedMethods=15`）。

### 17.4 数值与配置
- 公式：`factor = 1 + min(level × speedPerPoint, maxSpeedBonus) / 100`；默认 `speedPerPoint=2`、`maxSpeedBonus=50` ⇒ 0 点 1.00×，1 点 1.02×，10 点 1.20×，25 点起封顶 1.50×（实际上限受 `maxQuality=10` 限制，当前默认曲线最大 1.20×；cap 是给"未来更高 maxQuality / 手动调 speedPerPoint"用的安全阀）。
- clamp：`SpeedPerPointClamped` 0–100，`MaxSpeedBonusClamped` 0–500；`speedBonus=false` 是**硬开关** —— 两个 Prefix 第一行就 return（不计 effective、不打 debug 行），实机出问题可在 cfg 一键关闭而不用换 DLL。
- `speedBonus`/`speedPerPoint`/`maxSpeedBonus` 均已写入 cfg 注释与 `[QSystem] cfg` 启动日志行。

### 17.5 顺序与并行路径（可观测，不静默）
- 并行设施 tick（`GameLogic._assembler_parallel` :259303 / `_miner_parallel`，由 `FactorySystemFacilityGameTick_Parallel` :259814 派发）不在 `QualityEnv.Enter/Exit` 作用域内。处理：`QualityEnv` 新增 `_lastPlanetId` 缓存（最近一次串行 tick 的 planet，单线程顺序 tick 每帧都会先设），`CurrentPlanetId` 回退顺序 ambient → lastPlanet → localPlanet。
- 计数与日志：assembler/miner 各自统计 `effective`（实际乘算次数）、`viaEnv`（ambient>0 命中）、`fallback`（走回退）；每 200 次 effective 打一行 `[QSystem][speed] <target> effective=N (viaEnv=X fallback=Y)`。并行 tick 若占主导，`fallback` 会明显增大 ⇒ 日志可直接判定并行路径下加成是否生效/是否用了兜底 planetId。
- debug 行（`debugProbes=true` 时前 20 条/目标）：`[QSystem][speed] <target> eid=… level=… factor=1.20x power=0.52->0.62`（可用 AssemblerUI 等第三方 UI 交叉验证速度变化）。
- `Describe()` 同时带 `assembler effective/viaEnv/fallback, miner effective/viaEnv/fallback`，启动日志与 `[Audit]` 行都能看到；`[Audit]` 行文本：`speed bonus armed (assembler=on miner=on speedPerPoint=2.0 cap=50.0%; assembler effective=… viaEnv=… fallback=…, miner effective=… viaEnv=… fallback=…)`。
- 未武装文本：`[QSystem][speed] speed bonus NOT ARMED` 分支由 `Describe()` 输出 `speed bonus NOT ARMED (vanilla speed, reason: …)`；`speedBonus=false` 时 `[QSystem][speed] speed bonus disabled by config (vanilla speed)`。

### 17.6 退化保护
- `Apply` 对每个目标单独 try/catch，失败只 warn 一次（`[QSystem][speed] <key> not armed, vanilla speed: …`）并继续尝试另一个目标；两个都失败时 `Describe()` 报 `reason: patch registration failed (see warnings)`。
- Prefix 内部整体 try/catch，异常走 `QualityEnv.WarnOnce("speed.assembler"/"speed.miner", e)`，绝不把异常抛回游戏 tick；所有日志/计数都在 try 内且吞异常。
- `power` 保持 float 语义，乘算后再交给原版 `(int)(power*speedOverride)` 截断，不改变原版取整方式。

### 17.7 构建与离线测试（本机，未部署）
- `build.ps1` → `BUILD OK`，**0 警告 0 错误**；`bin\Release\DSPQualitySystem.dll` SHA256 **`5341027608474A820C5A5474AE6B4D60018422AC8BA38AE5038BFB26E0066941`**（74,752 B，AssemblyVersion 0.6.0.0）。
- `tests\run-tests.ps1` → `ALL PASS (10 checks)`；新增用例覆盖：0 点==1.0、负点数==1.0、10 点@2%≈1.2、4 点@2%≈1.08、点数 0..10 单调且不超 cap、100 点封顶 1.5、20%/点封顶、clamp 后 100%/点+500% cap=6.0、percent/cap 为 0 或负→1.0、默认曲线逐点 `1+inc*0.02`。

### 17.8 已知风险 / 需实机验证
1. 并行设施 tick（多人/后台工厂）下 assembler 无 factory 参数，planetId 只能靠 ambient/lastPlanet 兜底；单机单星球应恒为 ambient。实机看 `fallback` 计数占比：若高且加成用错星球，再评估是否改为"按 consumerPool 反查 planet"（需要额外反射，首版不做）。
2. **加速不额外耗电**：`power` 只被乘进进度，电力网络扣电走 `power` 之外的 consumer 逻辑，所以高品质建筑"白嫖"速度、不增加电耗（设计选择，与分馏塔双重收益同类，首版保留，README 已写为已知限制/非 bug）。
3. 速度乘算后 `(int)` 截断：低 power（接近 0.1f）× 小 factor 时截断误差相对可见，但只在 gate 边缘，属原版同样取整方式。
4. UI 不显示品质加速（原版面板只显示基础速度）；可用 AssemblerUI 的实时速度交叉验证，或看日志 `factor` 行。
5. HarmonyX 2.5.5 绑定失败退路已内建（手动注册只局部失败）；若日志出现 `speed.assembler.install not armed`，说明该版本参数名/重载不同，需要按实机日志调整反射。

## 18. P3 阶段2a：物流塔充能加成（v0.7.0，代码完成 + 离线验证通过，尚未部署）

### 18.1 交付文件（行数全部 <500）
- 新增 `Patches\P3_StationCharge.cs`（348 行）：`internal static class P3_StationCharge`，`Apply(Harmony)` 手动注册 + 严格校验、`StationChargePrefix`（`ref float power`）、`Describe()` 状态串、可观测计数器与 debug 行。
- `QualityConfig.cs`（104 → 141 行）：新增 `chargeBonus`（bool，默认 true）、`chargePerPoint`（float，默认 2.0）、`maxChargeBonus`（float，默认 50，单位 %）；clamp 访问器 `ChargePerPointClamped`（0–100）、`MaxChargeBonusClamped`（0–500）。
- `Plugin.cs`（221 → 229 行）：VERSION 0.7.0；`P3_Speed.Apply` 之后 `Patches.P3_StationCharge.Apply(harmony)` + `[QSystem][charge]` 状态行；`LogConfig` 追加 charge 三项。
- `Diag\StartupAudit.cs`（227 → 230 行）：新增 `[Audit] <P3_StationCharge.Describe()>` 一行。
- `DSPQualitySystem.csproj`：`<Version>0.7.0</Version>`。
- `tests\OfflineTests\Program.cs`（500 → 466 行）：新增用例 `P3 station charge factor (curve / cap / zero-point untouched)`（总 11）；为守住单文件 <500 行约定，把 P3 两个用例方法拆到新文件 `tests\OfflineTests\Checks.P3.cs`（76 行，`internal static partial class Program`），`OfflineTests.csproj` 增加 `<Compile Include="Checks.P3.cs" />`。
- `README.md`：配置表新增 3 项、玩法说明新增充能一条、已知限制/日志关键字更新。
- 未改 `QualityMath.cs`（复用 `SpeedFactor`）、未改 `Patches\P3_Speed.cs`（阶段1 保持原样）。

### 18.2 真实 IL 依据（必须用游戏安装目录的 dll）
- 基准 dll：`C:\Program Files (x86)\Steam\steamapps\common\Dyson Sphere Program\DSPGAME_Data\Managed\Assembly-CSharp.dll`；UTF-8 dump 副本（`tools\il.exe` 导出）：`tools\tmp\station_internalticklocal.txt`（2601 行）、`tools\tmp\station_internaltickremote.txt`（4178 行）、`tools\tmp\station_setpcstate.txt`、`tools\tmp\station_export.txt`、`tools\tmp\station_import.txt`。
- 签名（`tools\api.exe <dll> StationComponent` 核对参数名）：`System.Void StationComponent::InternalTickLocal(PlanetFactory factory, System.Int32 timeGene, System.Single power, System.Single droneSpeed, System.Int32 droneCarries, StationComponent[] stationPool)`（asm_full.cs:75190）。
- **充能锚点**：方法开头 `0x0002-0x0013` 即 `energy += (int)((float)energyPerTick * power)`：`ldarg.0; ldfld energy; ldarg.0; ldfld energyPerTick; conv.r4; ldarg.3(power); mul; conv.i4; conv.i8; add; stfld energy` ⇒ `power` **全方法唯一读取点 = 0x000E**；随后 `energy -= 1000L`（0x0018）与 clamp 到 `[0, energyMax]`（0x0030-0x006A）。把 `power` 乘 factor ⇒ 只按比例放大"每 tick 充入的 energy"，不写任何字段、不改 clamp、不碰存档。
- 调用点唯一：`GalacticTransport.GameTick`（asm_full.cs:103709-103710）`power = networkServes[consumerPool[stationComponent.pcId].networkId]` → `InternalTickLocal(...)`；同一循环 :103734 `SetPCState(consumerPool)`。
- **energy 三件套是存档字段（绝不写）**：`Export` :77745-77747 写 `energy`/`energyPerTick`/`energyMax`，`Import` :77881-77883 读回；且 `SetPCState` :75043-75050 每 tick 用 `requiredEnergy` 重算 `energyPerTick`（写它也会被覆盖）。
- 原版充能率 = `energyPerTick * power`，其中 `energyPerTick` 由电网 `requiredEnergy` 决定 ⇒ 放大 `power` 后单位时间取电随之上升，符合"充能加速 = 单位时间功率提高"口径。

### 18.3 注入与校验（不得半开）
- `Plugin.Awake` 在 `P3_Speed.Apply` 之后调用 `P3_StationCharge.Apply(harmony)`；手动 `harmony.Patch(target, prefix: new HarmonyMethod(prefix))`（`StationComponent.InternalTickLocal` 无其它 patch，不需要 Priority）。
- 应用前双重校验：① 目标方法参数里必须存在名为 `power` 的 `System.Single` 参数（否则 Harmony 按名绑定会静默失配）② prefix 必须精确为 `(ref StationComponent __instance, ref float power)`（名字+类型+顺序）。任一不满足 ⇒ **不 apply**、`Armed=false`、只 warn 一次（`[QSystem][charge] charge.install not armed, vanilla charge: …`），`Describe()` 报 `NOT ARMED (vanilla charge, reason: …)`，完全等价原版，绝无半开。
- Prefix 逻辑：`chargeBonus=false` 第一行硬早退（不计数、不打日志）→ `_armed`/`enabled` → `power <= 0` 早退（不越过原版缺电 gate、不凭空造电）→ planetId/entityId < 0 早退 → `level = QualityData.GetBuildingInc(planetId, entityId)` → `factor = SpeedFactor(level, ChargePerPointClamped, MaxChargeBonusClamped)` → factor<=1/NaN/Inf 早退 → `power *= factor`。整体 try/catch，异常走 `QualityEnv.WarnOnce("charge.prefix", e)`，不抛回游戏 tick。
- 编译产物核对（`:538` 之后用 il.exe 看）：`P3_StationCharge::StationChargePrefix(StationComponent&, System.Single&)`；`Apply` 内确实调到 `Harmony::Patch(...)` 手动注册重载。

### 18.4 数值与配置
- 复用阶段1 的 `QualityMath.SpeedFactor` 同一曲线：`factor = 1 + min(level × chargePerPoint, maxChargeBonus) / 100`；默认 2%/点、封顶 +50% ⇒ 0 点 1.00×、4 点 1.08×、10 点 1.20×、25 点起 1.50×（当前 maxQuality=10，默认曲线最大 1.20×；cap 是安全阀）。
- clamp：`ChargePerPointClamped` 0–100、`MaxChargeBonusClamped` 0–500；三个配置项均写入 cfg 注释与 `[QSystem] cfg` 启动行。
- 设计口径（与阶段1 一致，README 同步）：充能加速 = 单位时间功率提高，**总电量上限不变（energyMax 不涨）、单位时间取电上升**，不做省电语义；与"加速不额外耗电"同属"点数加成只放大瞬时系数、不改电力记账"的家族。

### 18.5 可观测日志
- 注册成功（grep 用）：`[QSystem][charge] patch registered: StationComponent.InternalTickLocal prefix=StationChargePrefix (power=param#2 name+type ok)`；武装行：`[QSystem][charge] armed: station energy charge bonus (chargePerPoint=2.0 maxChargeBonus=50.0%)`。
- debug 行（`debugProbes=true`，前 20 条）：`[QSystem][charge] station eid=… level=… factor=1.20x power=0.52->0.62`（同一电力网络下满电耗时缩短可交叉验证）。
- 聚合（每 200 次）：`[QSystem][charge] station effective=N (env=none)` —— 本功能只用实例字段 `planetId`/`entityId`，不依赖 `QualityEnv` ambient，故 `env=none`，并行路径也天然安全（`StationComponent` 是 class，`__instance` 就是对象本身）。
- 启动审计：`[Audit] station charge bonus armed (chargePerPoint=2.0 cap=50.0%; station effective=… env=none [power-param prefix on StationComponent.InternalTickLocal])`；未武装文本见 18.3。
- `chargeBonus=false`：`[QSystem][charge] station charge bonus disabled by config (vanilla charge)`。

### 18.6 退化保护
- 反射/校验失败只影响本功能：目标或 prefix 反射 miss、签名不符 ⇒ 不 patch、warn 一次、`Describe()` 明确报原因；`Apply` 整体 try/catch。
- HarmonyX 2.5.5 与 2.7.0 都有 `Harmony.Patch(MethodBase, HarmonyMethod prefix, …)` 6 参重载（运行时 0Harmony.dll SHA256 `7BD2BD6F…`）；若旧版失配，日志出现 `charge.install not armed`，无需换 DLL，可改回属性式注册。
- Prefix 内所有日志/计数均在 try 内且吞异常，绝不把异常抛回游戏 tick。

### 18.7 构建与离线测试（本机，未部署）
- `build.ps1` → `BUILD OK`，**0 警告 0 错误**；`bin\Release\DSPQualitySystem.dll` SHA256 **`1A6A48779C7D2091338781CF9CB61557D8078788AA17A9CC0B215F9BD7EAC660`**（80,896 B，AssemblyVersion 0.7.0.0）。
- `tests\run-tests.ps1` → `ALL PASS (11 checks)`；新增 `CheckStationChargeFactor`：0 点==1.0、负点数==1.0、10 点@2%≈1.2、4 点@2%≈1.08、点数 0..10 单调且不超 cap、100 点封顶 1.5、clamp 后 100%/点+500% cap=6.0、percent/cap 为 0 或负→1.0、非 NaN。
- `patchedMethods` 实机值 **15**（不是早期预期的 16）：`GetPatchedMethods()` 在 `PatchAll` 那行打印，而 `P3_StationCharge` / `P3_Speed` 是在计数之后才用 `harmony.Patch(...)` 手动注册的。最终被 patch 的独立方法数 = 15（13 attribute + 2 分馏塔）+ `StationComponent.InternalTickLocal` = **16**；`P3_Speed` 的两个 Prefix 挂在 15 里的 `AssemblerComponent.InternalUpdate` / `MinerComponent.InternalUpdate` 上，不新增方法。核对以启动日志 `patchedMethods=15` + 三行功能状态（speed / charge / fractionator）为准。

### 18.8 已知风险 / 需实机验证
1. 实机充能提速是否可见：看 `[QSystem][charge] station effective=` 增长 + 同一电网下满电/起飞等待时间缩短；`factor` 行与界面电量条交叉验证。
2. `power` 语义是电网满足率（0–1）：乘 factor 后 `energyPerTick*power` 可能超过"满功率"（本 Mod 不 clamp 乘算结果，与阶段1 同样让结果直接进原版算式）；若实机观察到能量溢出/异常快，可下调 `chargePerPoint`/`maxChargeBonus`（clamp 到 energyMax 仍由原版生效）。
3. 高品物流塔在缺电（低 power）时仍按原版比例充能，不额外造电；实机确认缺电塔不会因加成"假充满"。
4. 未做船速/运力/能量枢纽/电力塔范围（设计裁决：风险高，见 `_reports\task3-design.md`）。
5. 旧 HarmonyX 参数名差异退路已内建（见 18.6）。

## 19. P3 阶段2d：拆除返还品质（v0.8.0，代码完成 + 离线验证通过，尚未部署）

### 19.1 交付文件（行数全部 <500）
| 文件 | 行数 | 说明 |
|---|---|---|
| `QualityData.Dismantle.cs` | 59 | 新增 partial 文件：`DismantleInc(PlanetFactory, int)`（只读 `GetBuildingInc`，不 roll/不消耗/不清状态；try/catch + `WarnOnce("dismantle.inc")`） |
| `QualityMath.cs` | 179 | 新增纯函数 `DismantleIncFrom(int buildingInc, int kIncLevelMax, bool giveDismantleInc)`（配置门控 + clamp，离线可测） |
| `Patches/P3_Dismantle.cs` | 259 | 新增：手动注册的 transpiler，替换 `DoDismantleObject` 里 `TryAddItemToPackage` 的 inc 实参 |
| `QualityConfig.cs` | 142 | `giveDismantleInc` 默认值 **false → true**（用户明确要求），描述改为"拆除返还点数" |
| `Plugin.cs` | 234 | VERSION 0.8.0；Awake 内 `P3_Dismantle.Apply` + 状态日志；`LogConfig` 已含 `giveDismantleInc` |
| `Diag/StartupAudit.cs` | 239 | 新增 `PlayerAction_Build.DoDismantleObject(int)` 签名自检 + `[Audit] <P3_Dismantle.Describe()>` |
| `tests/OfflineTests/Checks.P3.cs` | 99 | 新增 `CheckDismantleInc`（10 条断言：config 门控、0/负点、正常返还、`kIncLevelMax` clamp、异常 max 防御） |
| `tests/OfflineTests/Program.cs` | 467 | 注册行新增第 12 个 Check |

`giveDismantleInc=false` 时 transpiler 仍会装（inc 实参读配置返回 0），行为与 v0.7.0 完全一致；`enabled=false` 时 `Apply` 直接不注册。

### 19.2 真实 IL 依据（游戏安装目录的 `Assembly-CSharp.dll`，`tools\il.exe` dump）
- 方法：`System.Boolean PlayerAction_Build::DoDismantleObject(System.Int32)`（类 `PlayerAction_Build`，字段 `public PlanetFactory factory`，继承自 `PlayerAction` 的 `player` 字段）。
- 关键偏移（dump 副本 `tools\tmp\dismantle_il.txt`）：
```
0143 ldarg.0 ; 0144 ldarg.1 ; 0145 call  ObjectAssetValue(int) ; 014A stloc.3     (num2)
014B ldloc.3 ; 014C ldc.i4.0 ; 014D ble.s IL_016a                               (returned asset <= 0 -> skip)
014F ldarg.0 ; 0150 ldfld PlayerAction::player
0155 ldloc.2        (itemId)
0156 ldloc.3        (count)
0157 ldc.i4.0       <- inc argument, hard-coded 0 (the single replacement target)
0158 ldc.i4.1       (throwTrash: true)
0159 ldarg.1        (objId)
015A ldc.i4.0       (trashLife)
015B callvirt Player::TryAddItemToPackage(Int32,Int32,Int32,Boolean,Int32,Boolean)
0160 stloc.s V_10
0162-0165 call UIItemup::Up(itemId, added)
016A ldarg.0 ; 016B ldfld factory ; 0170 ldarg.0 ; 0171 ldfld player
0176 ldarg.1 ; 0177 ldloca.s V_1 ; 0179 callvirt PlanetFactory::DismantleFinally(Player,Int32,Int32&)
017E ldc.i4.1 ; 017F ret
```
- 结论：`ldc.i4.0` 的 inc 实参在方法内唯一（前面紧跟 `ldarg.1`、`ldc.i4.1`，后面紧跟 4 参之后的 callvirt `TryAddItemToPackage(int,int,int,bool,int,bool)`），按"唯一 callvirt + 其前固定形状"定位，不依赖偏移。
- `PlayerAction_Build.factory` 是**字段**（非属性），`PlanetFactory.planet` 是属性（`<planet>k__BackingField`），`planetId` 是属性 getter。

### 19.3 注入方案与退化
- 手动注册（不用 `[HarmonyPatch]` 属性）：`harmony.Patch(target, transpiler: new HarmonyMethod(Transpiler))`，与 P3_Speed / P3_StationCharge 同一"按目标隔离失败"策略；因此它**不计入 PatchAll 的 patchedMethods 计数**。
- 校验链（任一失败 ⇒ 不 patch、warn 一次、`Describe()` 报原因、inc 保持原版 0）：目标方法反射 → `PlayerAction_Build.factory` 字段 → `Player.TryAddItemToPackage(int,int,int,bool,int,bool)` → `QualityData.DismantleInc(PlanetFactory,int)`。
- 注入内容（替换 `0157 ldc.i4.0` 单条）：`ldarg.0; ldfld <factory>; ldarg.1; call QualityData.DismantleInc(PlanetFactory,int)`（栈上已有 player/itemId/count 作为前 3 参，注入结果即第 3 参 inc）；labels 从被替换指令转移到注入序列第一条，避免分支目标落空。
- 锚点唯一性：目标方法内 `TryAddItemToPackage` 调用只允许 1 处；形状固定为 `i-4 ldc.i4.0 / i-3 ldc.i4.1 / i-2 ldarg.1 / i-1 ldc.i4.0 / i callvirt`；若出现异常块边界（`blocks` 非空）也拒绝并退化。
- 失败可观测：启动日志出现 `[QSystem][dismantle] NOT ARMED (vanilla inc=0): <reason>` 与 `[Audit] dismantle inc NOT ARMED (…)`；成功为 `[QSystem][dismantle] patch registered: PlayerAction_Build.DoDismantleObject anchor ok (inc argument now comes from building quality)` 与 `[Audit] dismantle inc armed (…)`。

### 19.4 顺序安全论证（为什么读点数不会读到已清空的值）
`DoDismantleObject` 内：加物品（`015B TryAddItemToPackage`，读点位处）→ 之后 `0179 DismantleFinally(...)` → 我们的 `P1_Cleanup.DismantleFinally` Postfix 才 `ClearEntity`。即**读点在 ClearEntity 之前**，且只读不写，不会与清理竞态；同一建筑被拆时也不会重复返还（`DismantleFinally` 只走一次）。

### 19.5 附录：HarmonyX 2.5.5 prefix `ref` 参数回写复核（A 项结论：支持，P3-1/2a 代码不改）
用户 2026-10-25 实机反馈"速度/充能体感无变化"，需排除"Harmony 不回写 `ref float` 参数"。只读 dump 运行期 `BepInEx\core\0Harmony.dll`（189,440 B，SHA256 `7BD2BD6F87C1758047DEF40F2F0F024C877456CE7C01D68031358EE0C615D850`，与 `tools\bepinex-lib\0Harmony.dll` 同一份）：
1. 2.5.5 的补丁器是 MonoMod 版 `HarmonyLib.Public.Patching.HarmonyManipulator`（旧 `HarmonyLib.MethodPatcher` 只留 `PrepareOriginal`/`DetourTo`/`CopyOriginal`）。
2. `HarmonyManipulator::WritePrefixes` IL `0x0132` 调 `EmitCallParameter(...)`，`0x013D–0x013F` 紧接着发 `call <prefix>`。
3. `HarmonyManipulator::EmitCallParameter`（7 参）判定：原始参数按名匹配 `float power`（非 out/非 byref）⇒ 标志位 V_12=true；补丁参数 `ref float power` ⇒ V_13=false；元素类型均 float(value type) ⇒ V_14=false ⇒ 控制流经 `0x0721 bne.un → 0x07C3`、`0x07CC`、`0x07D0 brfalse → 0x083E`，在 **`0x083E–0x084B` 发 `ldsfld OpCodes::Ldarga; ldloc V_15; Emit(OpCode,int)`**（即 `ldarga <argIndex>`：把**参数槽地址**交给 prefix）。
4. `HarmonyManipulator::MakePatched`（8 参）的 `variables` 字典只为 `__state` 参数建本地变量（lambda `<MakePatched>b__21_4` 即 `param.Name == STATE_VAR`），`power` 不进字典、不走"复制到 local"分支；prefix 与方法体在同一 `ILContext` 内，方法体原 `ldarg.s` 读同一槽 ⇒ **写入生效**。
⇒ 结论：byref 回写不存在问题；"没感觉"是幅度（4 点 +8%，且充能受 `energyMax`/取电速率掩盖）。大倍率复测（`speedPerPoint=50`、`chargePerPoint=50`）由 Lead 实机执行。

### 19.6 配置默认值变更（记录理由）
`giveDismantleInc` 由 `false` 改为 **`true`**：用户 2026-10-25 明确要求"拆掉的建筑回背包时带增产点数，这样重建后点位不丢"。旧版该键是死配置（写了也不生效），本版起它同时是功能开关：`false` 复原版 inc=0 行为。`DEVLOG` 此条即为"默认值变更"记录；老存档无影响（只影响拆建筑时的物品 inc）。

### 19.7 可观测文本
- 注册成功：`[QSystem][dismantle] patch registered: PlayerAction_Build.DoDismantleObject anchor ok (inc argument now comes from building quality)`
- 状态行：`[QSystem][dismantle] dismantle inc armed (giveDismantleInc=true; PlayerAction_Build.DoDismantleObject inc argument reads the building quality points)`
- 每次返还（debugProbes 前 20 条）：`[QSystem][dismantle] inc returned eid=… points=4 inc=4`
- 退化：`[QSystem][dismantle] NOT ARMED (vanilla inc=0): <reason>` + `[Audit] dismantle inc NOT ARMED (vanilla inc=0, reason: <reason>)`
- 审计：`[Audit] PlayerAction_Build.DoDismantleObject(int) -> OK`

### 19.8 构建与离线测试（本机，未部署）
- `build.ps1` → `BUILD OK`，**0 警告 0 错误**；`bin\Release\DSPQualitySystem.dll` SHA256 **`80B07E4B9810A1CF6CA5A62D540C39DC668D47EDB1A046A468107B7776F1AAC2`**（**86,528 B**，AssemblyVersion 0.8.0.0）。
- `tests\run-tests.ps1` → `ALL PASS (12 checks)` / `TEST PASS`（新增 `CheckDismantleInc`：gave=false→0、0/负点→0、4 点→4、MaxLevel→MaxLevel、超限 clamp、按给定 `kIncLevelMax` clamp、负/0 max 防御）。
- `patchedMethods`：启动日志仍打印 **15**（PatchAll 时点）；`P3_Dismantle` 是之后手动注册，最终被 patch 的独立方法数 = 15 + `StationComponent.InternalTickLocal` + `PlayerAction_Build.DoDismantleObject` = **17**（`P1_Cleanup` 的 `PlanetFactory.DismantleFinally` 早在 15 内）。`GetPatchedMethods()` 若在 Awake 之后被调用会返回 17。

### 19.9 已知限制 / 待实机验证
1. 只覆盖 **GUI 拆除路径** `PlayerAction_Build.DoDismantleObject`；批量/脚本/其它 mod 触发的 `DismantleFinally` 不带返还（原版 inc=0）。
2. 一次拆多栋**叠放**建筑时，inc 作用于整栈物品（原版 `TryAddItemToPackage` 一次给 `count=num2` 件），未逐件拆分不同点数——用户口径"各类建筑"下可接受，实机若出现混点叠放再评估。
3. 蓝图复制/粘贴仍为 0 品（设计如此），不返还也不复制点数。
4. 实机需验证：4 点熔炉拆回背包 → 物品 inc=4 → 重建后建筑仍 4 点；`giveDismantleInc=false` 时行为与 v0.7.0 完全一致。
5. 本版未改任何其它功能，P3-1/2a 的 byref 方案经 19.5 复核确认无需改代码。

---

## §20 v0.9.0 — P3 阶段2e：矿机多重概率升级 + 生产建筑额外升品

版本 **v0.9.0**（2026-10-25）。需求来源：用户 2026-10-25 明确口径（"矿机出矿改成多重概率升级""生产建筑点数 >6 再额外升品、10 点必升 2 级"）。本版只改规则层（`QualityMath` / `QualityRules` / `QualityConfig`），**不动任何 IL 补丁、锚点、存档字段**：`patchedMethods` 与 v0.8.0 完全一致（启动打印 15；最终独立方法 17）。

### 20.1 交付文件与行数
| 文件 | 行数 | 改动 |
|---|---|---|
| `QualityMath.cs` | 269 | 新增 `MinerRollsOf`、`MinerMaxInc`、`ExtraUpgradeMode`、`ApplyCycleUpgrade` 四个纯函数（离线可测） |
| `QualityRules.cs` | 139 | `RollMinerQuality` 改多重独立判定；`RollCycleQuality` 增加四参重载（`allowExtraUpgrade`，三参默认 true，分馏塔传 false）并接入额外升品模式 |
| `QualityData.Fractionator.cs` | 120 | 仅 1 行：`FractionatorProduceInc` 改调 `RollCycleQuality(..., false)`（分馏塔隔离，规则不变） |
| `QualityConfig.cs` | 176 | 新增 `extraUpgradeAbove`(int, 默认 6, clamp 0–10)、`doubleUpgradeAt`(int, 默认 10, clamp 0–10) + `ExtraUpgradeAboveClamped`/`DoubleUpgradeAtClamped` |
| `Plugin.cs` | 242 | VERSION 0.9.0；`LogConfig` 打印两个新键；Awake 新增一行规则播报 |
| `QualityData.cs` | 476 | 仅改 `MinerOutputInc` 的 XML 注释（inc 现在 0..N 点） |
| `tests\OfflineTests\Checks.P3.cs` | 222 | 新增 `CheckMinerMultiRoll`、`CheckExtraUpgrade` |
| `tests\OfflineTests\Program.cs` | 469 | 注册 2 个新用例（共 14 checks） |
| `DSPQualitySystem.csproj` | — | `<Version>0.9.0</Version>` |

### 20.2 新旧规则对照
| 项目 | v0.8.0（旧） | v0.9.0（新） |
|---|---|---|
| 矿机每件矿石 | 1 次判定，成功 0 或 1 点 | `r = floor(建筑点数 / 2)` 次**独立**判定，每次成功 +1 点，单件上限 = `r`（0/1 点矿机 → 0 次、恒 0 点） |
| 制造台/熔炉每周期升品 | 1 次判定，成功 +1 级 | 基准 1 次判定 +1；建筑点数 **7/8/9** 时再加一次同口径判定（两次都成 = +2）；建筑点数 **≥10** 时**必定 +2**（跳过判定） |
| 阈值 | 无 | `extraUpgradeAbove=6`（点数须 **>** 该值）、`doubleUpgradeAt=10`（点数 **≥** 该值）；任一为 0 = 关闭该条；两条都 0 = 与 v0.8.0 完全一致（连 RNG 消耗都一样） |
| 分馏塔 | 单次升品（`FractionatorProduceInc` → `RollCycleQuality`） | **保持单次**（`FractionatorProduceInc` 改调四参重载并传 `allowExtraUpgrade=false`，规则与 v0.8.0 一致） |

### 20.3 概率公式
记号：`L` = 建筑品质点数，`r = floor(L/2)`。

- 矿机每件：每次判定成功率沿用原式 `p = MinerChance(L, minerChancePerPoint) = clamp(L * minerChancePerPoint, 0, 100)`（%），独立判定 `r` 次，点数 `X ~ Binomial(r, p)`。
  - 期望 `E[X] = r * p`，上限 `r`，方差 `r*p*(1-p)`。
  - 默认 `minerChancePerPoint=2.0`：`L=2→p=4%, E=0.04`；`L=4→p=8%, E=0.16`；`L=6→p=12%, E=0.36`；`L=8→p=16%, E=0.64`；`L=10→p=20%, E=1.00`。
  - 注意 `p` 随总点数线性增长、`r` 也随点数增长 ⇒ 期望 ≈ `L² * minerChancePerPoint / 200`（**平方增长**，用户口径"最高等级不低于矿机点数的一半向下取整"的直接结果；嫌强调低 `minerChancePerPoint` 即可，未新增配置键）。
- 生产建筑每周期：设 `p = UpgradeChance(L, buildingChancePerPoint, maxUpgradeChance)`（%，上限 `maxUpgradeChance`）。
  - `L = 0`：+0（恒 0，不凭空升）。
  - `L ≤ extraUpgradeAbove`（默认 ≤6）：单次判定，`E[+1] = p`，最多 +1。
  - `extraUpgradeAbove < L < doubleUpgradeAt`（默认 7/8/9）：两次独立判定，每次 +1，最多 +2，`E[+2] = 2p`。
  - `L ≥ doubleUpgradeAt`（默认 10）：必定 +2（`E = 2`，不消耗 RNG）。
  - 结果统一 clamp 到 `maxQuality` / `Cargo.kIncLevelMax`；输入已到上限时不再升（也不消耗 RNG，与旧实现一致）。

### 20.4 与用户口径的对应关系
1. "判定次数 = floor(buildingInc / 2)，每次成功 +1，单件上限 = floor(buildingInc / 2)" → `QualityMath.MinerRollsOf` + `RollMinerQuality` 循环。
2. "成功率沿用现有 `QualityMath.MinerChance(buildingInc, minerChancePerPoint)`" → 每次判定都用该返回值（未按点数拆成多次小概率，避免引入新语义）。
3. "buildingInc > 6 额外再升 1 级（同一 UpgradeChance 再判一次）" → `ExtraUpgradeMode=1` 分支。
4. "buildingInc == 10 必定升 2 级（跳过概率判定）" → `ExtraUpgradeMode=2` 分支（阈值可配为 10）。
5. "阈值做成配置、0 = 禁用、两键都 0 = v0.8.0" → `extraUpgradeAbove` / `doubleUpgradeAt`（`0` 关；`mode 0` 路径的 RNG 调用序列与 v0.8.0 逐位一致）。
6. "只作用于 assembler 生产路径，不改分馏塔/矿机" → `RollCycleQuality` 原本被 assembler Postfix 与分馏塔 helper 共用，本版给它加第 4 参 `allowExtraUpgrade`：三参重载（assembler 路径）默认 true，`QualityData.FractionatorProduceInc` 显式传 **false**，因此 7/8/9/10 点分馏塔仍走 v0.8.0 的单次升品，分馏塔规则保持不变（mode 0 分支与 v0.8.0 逐位一致，含 RNG 消耗）。

### 20.5 可观测日志
- 启动（`LogConfig` 行尾新增）：`… maxChargeBonus=50 extraUpgradeAbove=6 doubleUpgradeAt=10`
- 启动规则播报（新增一行）：`[QSystem] rule: miner rolls = floor(points/2) (multi-point ore, cap = half); extra upgrade above=6 guaranteed +2 at=10`
- 矿机命中（原有行语义变化）：`[Probe][Miner] quality hit eid=… inc=1..5`（**实际点数**，不再固定 1）；F12 汇总 `[Probe][F12] miner quality hits=… calls=… lastMinerCall=eid=… level=…`（`level` 同样为实际点数）。
- 销售/取货观测不变（`[Probe][F7]`、`[Probe][F5]` 的 inc 字段即实际点数）。

### 20.6 构建与离线测试（本机，未部署）
- `build.ps1` → `BUILD OK`，**0 警告 0 错误**；`bin\Release\DSPQualitySystem.dll` SHA256 **`16087007D48A1A333F5B1126A84AE5FCC7677BB4448B9FC56E8164F01E6B90E8`**（**88,576 B**，AssemblyVersion 0.9.0.0）。注：分馏塔隔离（20.4 第 6 条）合入后重新构建，字节数/SHA 相对隔离前（88,064 B / `75BFDAA4…`）略变，代码行为差异仅 `RollCycleQuality(..., false)` 一处。
- `tests\run-tests.ps1` → `ALL PASS (14 checks)` / `TEST PASS`（新增两个用例 PASS）：
  - `CheckMinerMultiRoll`：`MinerRollsOf`/`MinerMaxInc` 对 L=0..10 全枚举等于 `L/2`；负点数→0；11 个等级各 20 万件模拟：点数不越上限、期望点数单调、0/1 点恒 0、模拟均值与理论值 `r*p` 误差 <0.02（默认 2%/点）。
  - `CheckExtraUpgrade`：默认阈值模式映射 `[0,0,0,0,0,0,0,1,1,1,2]`；0/0 配置对 L=0..10 全为旧行为；10 点建筑 1,000,000 次采样恒等于 `min(input+2, cap)`；7/8/9 点 20 万次采样上界 +2 且**不是**必升（存在 <+2 样本）；mode 0/1/2 语义与 clamp 边界断言。
- `patchedMethods`：本版未增删补丁，启动日志仍打印 **15**；最终独立方法数仍 **17**。

### 20.7 已知取舍 / 待实机验证
1. **矿机期望点数平方增长**：这是"判定次数 = 点数/2 × 成功率随点数线性"的直接结果（10 点矿机默认期望 1.0 点/件，4 点仅 0.16）；属用户口径，不做额外衰减。
2. **7/8/9 点建筑比 6 点明显更强**：两次独立判定使期望从 `p` 跳到 `2p`，且 10 点是保底 +2 —— 6→7、9→10 两个台阶是设计意图，但实机体感可能偏陡，必要时只调 `extraUpgradeAbove`/`doubleUpgradeAt`。
3. **分馏塔已隔离**：`FractionatorProduceInc` 传 `allowExtraUpgrade=false`，分馏塔保持单次升品（与用户"不要改分馏塔规则"一致）；assembler 路径不受影响。
4. **10 点必升 +2 不再消耗 RNG**：与旧实现的随机序列不同（但旧版 10 点本就不是保底，无兼容包袱）。
5. **待实机**：4 点矿机 `[Probe][Miner] quality hit … inc` 仍多为 1；10 点矿机应出现 inc≥2；10 点熔炉产物恒 +2（`inc = 原料 + 2`）；7 点建筑抽样出现 0/1/2；`extraUpgradeAbove=0,doubleUpgradeAt=0` 时行为与 v0.8.0 一致（回归对照）。
6. 矿机热路径开销：每件最多 5 次 `RollPercent`（=10000 分辨率整数比较），可忽略。
---

## 21. v0.10.0 —— P4 背包建筑品质隔离（2026-10-25）

用户反馈：背包里**不同点数的同类建筑会合并**（原版只按 itemId 合并），点数相加后拆下来再建就丢点/串点。本版把**玩家背包**中的建筑类物品按"每件品质点数"分叠：不同点数的同类建筑不再并进同一格；非建筑物品、玩家背包以外的容器完全保持原版。

### 21.1 交付文件与行数

| 文件 | 行数 | 说明 |
|------|------|------|
| `QualityMath.cs` | 320 | 新增纯函数 `SharesStack`（:257）与 `SplitInc`（:282） |
| `QualityData.Isolation.cs` | 248 | 新增 partial：隔离版 add `AddIsolatedStacked` + `IsBuildingItem` / `CountBuildingItems` + 私有 `MergeIntoGrid` / `StackSizeOf` / 日志 |
| `Patches\P4_Isolation.cs` | 223 | 手动注册 `StorageComponent.AddItemStacked` Prefix；`:36` Apply、`:74` TryInstall、`:125` Prefix、`:174` Describe |
| `QualityConfig.cs` | 182 | 新增 `isolateBuildingStacks`（默认 true） |
| `Plugin.cs` | 248 | VERSION 0.10.0；`P4_Isolation.Apply` + 状态日志；LogConfig 追加键 |
| `Diag\StartupAudit.cs` | 243 | 新增 `StorageComponent.AddItemStacked(int,int,int,out int)` 签名自检 + `[Audit] isolation: building items` + `[Audit] <Describe>` |
| `tests\OfflineTests\Checks.P4.cs` | 189 | 新增 2 个用例（合并谓词 4 类 + 容量模型；split_inc） |
| `tests\OfflineTests\Program.cs` | 471 | 注册 2 个新 Check（共 16） |
| `tests\OfflineTests\OfflineTests.csproj` | 30 | `<Compile Include="Checks.P4.cs" />` |
| `DSPQualitySystem.csproj` | — | `<Version>0.10.0</Version>` |

### 21.2 原版合并点（真实源码）与 inc 语义

加物品入口链（asm_full.cs）：

- `Player.TryAddItemToPackage(int itemId,int count,int inc,bool throwTrash,int objId,bool trashLife)` **:170218-170262**
  - :170231 `int num = package.AddItemStacked(itemId, count, inc, out remainInc);`
  - :170232 `NotifyPackageAddItem(itemId, num, inc - remainInc);`
  - :170236 `deliveryPackage.AddItem(itemId, num2, remainInc, out remainInc2);`（交付包，本版不动）
  - :170245 `ThrowTrash(...)` / :170249 手持（本版不动）
- 背包容器合并的唯一入口：`StorageComponent.AddItemStacked(int itemId,int count,int inc,out int remainInc)` **:80012-80122**。

**inc 语义（本版关键认知，v0.10.0 修正了 task8-design.md 早期草案）**：`inc` 参数是**整叠总点数**，不是每件点数。证据：

1. `StorageComponent.split_inc(ref n, ref m, p)` **:170203-170216** 对 `m` 做 `m/n` 除法再按件数 `p` 分配；
2. `Player.TryAddItemToPackage` :170232 用 `inc - remainInc` 作为"加进去的点数"；
3. `P1_BuildCapture` 从 `Player.UseHandItems(id, out useInc)` 取到总点数，代码里 `perItem = useInc / used` 才是品质等级。

⇒ 隔离判定比较 `inc/count`（交叉乘法 `(long)gridInc*incomingCount == (long)incomingInc*gridCount`，避免浮点/截断）。

### 21.3 逐行对照表（原版 vs 本版 `AddIsolatedStacked`）

| 原版 asm_full.cs | 原版行为 | 本版位置（`QualityData.Isolation.cs` / `QualityMath.cs`） |
|---|---|---|
| :80039 `int num2 = 0;` | 堆叠上限缓存 | :111 `int stackSize = 0;` |
| :80041-80047 `for(i){ if(grids[i].itemId==itemId) ... }` | 只按 itemId 判并格 | :115-120 循环头 + itemId 检查（不变） |
| **:80043（隐式条件：只判 itemId）** | **不同点数也会并格（本版唯一要改的点）** | **:121-126 `QualityMath.SharesStack(...)`：额外要求每件点数相同** |
| :80048-80051 `if(num2==0) num2=grids[i].stackSize; int num3=num2-grids[i].count;` | 取堆叠上限/剩余空间 | `MergeIntoGrid` :187-191 |
| :80052-80061 `if(count<=num3){count+=count; inc+=inc; inc=0; remainInc=0; count=0; break;}` | 整叠放入 | :196-202（同样四步赋值） |
| :80062-80066 `count=num2; inc+=split_inc(ref count,ref inc,num3); remainInc=inc;` | 部分放入按比例拆点 | :204-205 + :176 |
| :80067-80070 `if(type==DeliveryFiltered) break;` | 交付过滤容器特例 | 不适用（玩家背包 `type` 为 `EStorageType.Default`，prefix 已限定 `isPlayerInventory`） |
| :80072-80106 第二遍循环（创建空格 + 同条件并格） | 同上 | :134-165（同样两段：空槽建格 + 并格检查） |
| :80107-80113 `num5=num-count; if(num5>0){ searchStart=0; lastEmptyItem=-1; NotifyStorageChange(); }` | 记账 + 刷新缓存 | :167-175（原版不清 `lastFullItem`，本版同样不清） |
| :80114 `return num5;` | 返回放入数量 | :177 |
| :170203-170216 `split_inc` | 按比例拆点 | `QualityMath.SplitInc` :282 |

结论：**唯一语义变化是并格条件**（:121-126 / :154-159 的 `SharesStack` 门）；栈上限、整叠/部分放入、`split_inc`、`remainInc`、返回值、`NotifyStorageChange` 时机全部逐行对应。

### 21.4 注入与退化（"异常/不确定即回退原版"）

- 目标：`StorageComponent.AddItemStacked(int,int,int,out int)`；**手动注册**（`harmony.Patch(target, prefix: new HarmonyMethod(prefix))`），不计入启动打印的 `patchedMethods`。
- 注册前校验：目标方法存在 + 四参数名 `itemId/count/inc/remainInc` 且第 4 参为 `out`；prefix 形状 `(StorageComponent __instance, int itemId, int count, int inc, out int remainInc, ref int __result)`。任一不符 → 功能整体禁用（不半开），只 warn 一次。
- Prefix 硬规则：
  - 早退顺序：`!_armed` → `__instance==null || !__instance.isPlayerInventory` → `Enabled` → `IsolateBuildingStacks` → `itemId<=0 || count<=0` → `grids` 空/`size<=0` → `!IsBuildingItem(itemId)`，任一不满足 **return true（原版）**；
  - 全程 try/catch；**任何异常 → `return true` 交回原版**（只有隔离副本已完整执行后才 return false），catch 只 warn 一次，**绝不自造 `__result`**；
  - 只有"建筑 + 玩家背包 + 配置开"三条件同时成立才走隔离副本，其余路径 100% 原版。
- 可观测文本：
  - `[QSystem][isolation] patch registered: StorageComponent.AddItemStacked prefix=AddItemStackedPrefix (inc-aware merge for building items in the player inventory)`
  - `[QSystem][isolation] armed (building items=<N>, patch points=1)`（N = `ItemProto.CanBuild` 数量，由 LDB 决定，实机日志可读）
  - `[Audit] StorageComponent.AddItemStacked(int,int,int,out int) -> OK`、`[Audit] isolation: building items = <N>`、`[Audit] isolation armed (building items=<N>, patch points=1, opens=N …)`
  - 退化：`[QSystem][isolation] NOT ARMED (vanilla merge): <reason>`（只一次）
  - 分割（`debugProbes=true` 前 20 条）：`[QSystem][isolation] split item=<id> inc=<总点数> grids=<跳过的不同等级格数>`
- `patchedMethods`：启动仍打印 **15**（手动注册都在计数之后）；最终独立方法数 **18** = 15 + `StationComponent.InternalTickLocal` + `PlayerAction_Build.DoDismantleObject` + `StorageComponent.AddItemStacked`。

### 21.5 配置与语义

`isolateBuildingStacks`（bool，默认 `true`）：

- `true`：玩家背包里建筑类物品按**每件点数**分格；同等级仍合并；0 点（地基/传送带/分拣器/多数建筑）**0==0 照常合并**，不会碎片化；
- `false`：完全原版合并（等同 v0.9.0）；
- 非建筑物品（矿石/材料/产物）、背包以外的容器（箱子/物流塔/交付包/手持）完全不受影响。
- 与 v0.8.0 拆除返还联动：拆 4 点熔炉 → 返回物品 inc=4（count=1，总点数=等级）→ 与 0 点熔炉等级不同 → **独立成格**，重建仍 4 点。

### 21.6 构建与离线测试（本机，未部署）

- `build.ps1` → `BUILD OK`，**0 警告 0 错误**；`bin\Release\DSPQualitySystem.dll` = **95,232 B**，SHA256 **`B570C0F474C3DC5B22FAED4FBA4BD3E48BFCBBD6997A5670CD43929E3F6F13B4`**，AssemblyVersion **0.10.0.0**。
- `tests\run-tests.ps1` → **`ALL PASS (16 checks)` / `TEST PASS`**；新增：
  - `CheckIsolationMerge`：不同 itemId 不并；非建筑恒并（不同等级也并）；非玩家背包恒并；同等级并；**两叠 0 点必须并**；0vs1 / 0vs4 / 4vs0 / 4vs2 不并；分数等级 10/3 vs 100/30 交叉乘法判等；空格接受任意等级；容量模型（满 50 格 0 点 + 30 件 4 点 → 开新格、0 点格不动、同等级 top-up 合并、满格不吞货）。
  - `CheckSplitInc`：`(10,20,4)→8/剩6/剩12`；负数余数分支 `(3,7,1)→2/剩2/剩5`；`n=0`/负 `n`/`p=0`→0；`(8,8,8)→8`（返回**总点数增量**，与 `grids[i].inc += split_inc(...)` 用法一致）。

### 21.7 实机验证清单与已知限制

实机验证见 `_reports\VERIFY-0.10.0.md`，重点：①拆 4 点熔炉 → 背包 4 点物品不与 0 点合并 → 建回仍 4 点；②0 点传送带/地基仍合并（不碎片）；③`[QSystem][isolation] split` 行；④矿石/材料照旧合并；⑤快速造 4 点制造台/矿机验证 P3-1 的 3× 幅度（F10/F11/拆除返还三条路 + 0 点对照）。

已知限制（不是 bug）：

1. 只隔离 `Player.TryAddItemToPackage → package.AddItemStacked` 这条背包加物品路径；交付包（`DeliveryPackage.AddItem`）与手持（`inhandItemInc`）仍按原版合并，物品从交付包取回背包时再按当前总点数入格。
2. 界面里手动拖拽/整理/拆分（直接写 `GRID`）绕过本补丁，可能把不同点数合并回一格；不做 UI 层拦截。
3. 蓝图/快速建造仍 0 品（与 v0.9.0 一致）。
4. 同等级合并的总点数仍按原版 `inc += inc` 累加（等级不变），不改变点数守恒。

### 21.8 版本链

v0.4.3 `6E3E8676…` → v0.4.4 `6363BF16…` → v0.5.0 `B7522F54…` → v0.6.0 `53410276…` → v0.7.0 `1A6A4877…` → v0.8.0 `80B07E4B…` → v0.9.0 `16087007…` → **v0.10.0 `B570C0F4…3F6F13B4`（95,232 B，16 checks）**。

### 21.9 实机确认（v0.10.0，2026-10-25，Lead 本地，pid 20428）

启动全绿：`[QSystem] === DSP Quality System v0.10.0 ===`、`[QSystem] Harmony PatchAll OK, harmony=2.5.5.0 patchedMethods=15`、`[QSystem] cfg … extraUpgradeAbove=6 doubleUpgradeAt=10 isolateBuildingStacks=True`、`[QSystem][isolation] patch registered: StorageComponent.AddItemStacked prefix=AddItemStackedPrefix …`、`[QSystem][isolation] armed (building items=63, patch points=1)`、`[Audit] StorageComponent.AddItemStacked(int,int,int,out int) -> OK`、`[Audit] isolation: building items = 63`、`[QSystem] === startup report complete ===`。

拆除 + 隔离联动（实机原文）：`[QSystem][dismantle] inc returned eid=129 points=4 inc=4` 紧接 `[QSystem][isolation] split item=2104 inc=4 grids=2`（4 点物品独立成格，未并入 0 点同类叠）；拆前 `[Probe][F9] planet=103 entities=129 buildingInc … 4=3` → 拆后 `… 4=2`。

同会话另确认：充能 3× 幅度 PASS（`[QSystem][charge] station eid=129 level=4 factor=3.00x power=1.00->3.00`，`chargePerPoint=50/maxChargeBonus=500` ⇒ 4 点 3.0×）。

待补：①重建回 4 点（GUI 放置步骤未完成）；②#12 制造速度 3× 幅度取证；③#15/#16 矿机多段与 10 点必 +2；④#11 分馏塔。步骤见交付包内 `dist\DSPQualitySystem-0.10.0\VERIFY-0.10.0.md`（或工作副本 `_reports\VERIFY-0.10.0.md`）。


## §22 P3 阶段2a 重构（v0.11.0）：物流塔"充电速度上限"

### 22.1 交付文件与行数

| 文件 | 行数 | 说明 |
|---|---|---|
| `Patches\P3_StationCharge.cs` | 385 | 重写：旧 `InternalTickLocal` power Prefix 退役；改为 `SetPCState` postfix（`energyPerTick` + `pcPool[pcId].requiredEnergy`） |
| `QualityMath.cs` | 348 | 新增纯函数 `ChargeCapFrom(long energyPerTick, long energy, long energyMax, int buildingInc, float percentPerPoint, float maxBonusPercent)`（返回 0 = 不改） |
| `QualityConfig.cs` | 201 | 新增 `debugInjectPoints`（默认 4，clamp 0..maxQuality）+ `DebugInjectPointsClamped`；`debugProbes` 说明更新 |
| `Plugin.cs` | 249 | VERSION 0.11.0；charge 接线注释更新；LogConfig 追加 `debugInjectPoints` |
| `Diag\Probes.cs` | 399 | F10/F11 用 `DebugInjectPointsClamped` 取代硬编码 4 |
| `Diag\StartupAudit.cs` | 256 | 新增 `StationComponent.SetPCState(PowerConsumerComponent[])` 签名自检行 |
| `tests\OfflineTests\Checks.P3.cs` | 269 | 新增 `CheckChargeCap`（曲线/满电跳过/0 点不动/非法输入不改/长整型） |
| `tests\OfflineTests\Program.cs` | 472 | 注册第 17 个检查 |
| `README.md` | 84 | 版本/配置表（charge 语义 + `debugInjectPoints`）/玩法说明/已知限制/热键 |
| `TESTLOG.md` | 67 | v0.11.0 头、#18 行、离线 17 checks、版本链、交接状态、已知限制 |

### 22.2 为什么重构（用户口径 + 一个必须说清的数学事实）

用户口径（本轮原话）："至于星际物流塔的充电，我建议增产点数加成充电速度上限就行，这个参数才是充电关键。"

v0.7.0–v0.10.x 的做法是 Prefix 乘 `InternalTickLocal` 的瞬时参数 `power`（`energy += (int)(energyPerTick * power)`）。调查 `SetPCState`（asm_full.cs:75035-75051）后确认：

- 游戏里**没有** `maxChargePower`/`chargePower` 字段；用户所说的"充电速度上限"就是每 tick 由 `SetPCState` 派生的 `StationComponent.energyPerTick`（`requiredEnergy` 由 prefab `workEnergyPerTick`/`idleEnergyPerTick` 与电量比例算出）。
- 由于充式是 `energy += (int)(energyPerTick * power)`，**只乘 `power` 与只乘 `energyPerTick` 对乘积逐位等价**——v0.7.0 的 3× 已经是 3 倍充能，唯一问题是它不向电网申报更高需求（`requiredEnergy` 不变），属于"少报需求多拿电"。
- 因此 v0.11.0 改为 **Postfix 同时抬 `energyPerTick` 和 `pcPool[pcId].requiredEnergy`**：上限与需求一起抬，电网供给充足时真实 ×factor，不足时按电网比例分配，统计诚实。

结论：旧 `power` 前缀**退役**（保留会造成 factor²，9× 不可解释）；`Describe()`/启动日志明确写 `power path retired in v0.11.0`。

### 22.3 真实 IL 依据

- 目标：`System.Void StationComponent::SetPCState(PowerConsumerComponent[] pcPool)`（asm_full.cs:75035；单调用点 `GalacticTransport.GameTick` :103734，且仅 `!isCollector && !isVeinCollector`）。
- 原版逻辑（:75035-75051）：`if (energy == energyMax) pcPool[pcId].SetRequiredEnergy(false)`；否则 `num = 1.05 - energy/(double)energyMax`，`if (num > 1.0) num = 1.0`，`pcPool[pcId].SetRequiredEnergy(num)`；最后 `energyPerTick = pcPool[pcId].requiredEnergy`。
- IL 偏移（`tools\tmp\station_setpcstate.txt`，`il.exe <游戏 Assembly-CSharp.dll> StationComponent SetPCState 1`）：`0000-000C` 满电比较；`000E-001B` `SetRequiredEnergy(false)`；`0022-003B` num 计算；`003C-0051` 唯一 clamp（≤1.0）；`0052-005F` `SetRequiredEnergy(double)`；**`0064-0076` `energyPerTick = pcPool[pcId].requiredEnergy`（本补丁 postfix 的注入点）**；`007B ret`。
- 存档字段（不可写、也不新增 schema）：`energy` Export :77745 / Import :77881；**`energyPerTick` :77746 / :77882**；`energyMax` :77747 / :77883；`requiredEnergy`（PowerConsumerComponent）:58851 / :58874。
- 充电路径唯一（`InternalTickLocal` :75192），放电全用 `energy`（:75334/:75461/:75501/:77156/:77315/:77349）；`InternalTickRemote` 不充电。
- 电网比率 `power = networkServes[networkId]`（:103709），`networkServes` = min(1, supply/demand)（:107257/:107265-107267）。

### 22.4 注入与"不得半开"的双重校验

手动注册（与其它 P3/P4 补丁一致，不计入 PatchAll 的 attribute 数）：

1. 反射取 `StationComponent.SetPCState(PowerConsumerComponent[])` 与 `StationChargeCapPostfix`；
2. 目标校验：**必须是实例方法、返回 `void`、恰好 1 个参数且为 `PowerConsumerComponent[] pcPool`（名字也校验，Harmony 按名绑定）**；
3. postfix 形状校验：**恰好 `(ref StationComponent __instance, PowerConsumerComponent[] pcPool)`**；任一不匹配 → `harmony.Patch` 不执行、只 warn 一次、`Describe()` 输出 `NOT ARMED`；
4. 通过后 `harmony.Patch(target, postfix: new HarmonyMethod(postfix))`，日志写明 `(energyPerTick + pcPool.requiredEnergy; power path retired in v0.11.0)`。

Postfix 内的守卫顺序（**全部在第一次写字段之前**）：`chargeBonus=false` 硬早退（无计数/日志）→ `_armed` → `enabled` → `pcPool == null` → `pcId` 越界 → `energyMax <= 0` 或 `energy >= energyMax - 1`（满电/近满电跳过，避免虚增需求）→ `energyPerTick <= 0` → `requiredEnergy <= 0` → `planetId/entityId < 0` → 取建筑点数 → `ChargeCapFrom(...)`；返回 0/未增大则原样返回。**只有全部通过后才写两个字段**；`try/catch` 包住整体，异常时尚未写任何字段 ⇒ 原版充能继续（不会出现"改了一半"）。

### 22.5 数值与语义

- factor 复用 `QualityMath.SpeedFactor(level, chargePerPoint, maxChargeBonus)`：默认 2%/点、cap +50%（与阶段1同曲线，不复制第二份逻辑）；4 点 = +8%，10 点 = +20%；测试配置 `50/500` 时 4 点 = 3.0×。
- `ChargeCapFrom` 返回 `(long)(energyPerTick * factor)`；0 表示不改（满电/非法/0 点/factor≤1/四舍五入后未增大），调用方据此保持与原版逐位一致。
- 语义：**总电量上限 `energyMax` 不变、单位时间取电上升**（不是省电，与阶段1口径一致）；电网供给充足时充电时间 ≈ `energyMax/(energyPerTick·factor)`，接近满电时因原版 `num` 线性下降而自然收尾。
- 满电跳过：`energy >= energyMax - 1` 时不改，避免原版已切到 `idleEnergyPerTick` 时还被乘 factor 虚增电耗。

### 22.6 附加：`debugInjectPoints`（同版本，便于 #16 实机验证）

- 新配置 `[quality] debugInjectPoints`，默认 **4**，clamp `0..QualityConfig.MaxLevel`，仅 `debugProbes=true` 时被热键使用。
- F10（手持物品注入）与 F11（悬停建筑注入）从硬编码 4 改为该值；默认 4 时与旧行为完全一致。设 10 可验证"10 点建筑产物必 +2"（`doubleUpgradeAt=10`）与 7/8/9 点额外判定。
- `FillModel`… （无，删除此条思考）→ 热键日志会打印实际 `perItem`：`[Probe][F10] DEBUG INJECTION: … perItem=10`。

### 22.7 可观测日志

- 启动：`[QSystem][charge] patch registered: StationComponent.SetPCState postfix=StationChargeCapPostfix (energyPerTick + pcPool.requiredEnergy; power path retired in v0.11.0)`；`[QSystem][charge] armed: station charge cap bonus (chargePerPoint=2.0 maxChargeBonus=50.0%)`。
- 审计：`[Audit] StationComponent.SetPCState(PowerConsumerComponent[]) -> OK`；`[Audit] station charge cap bonus armed (chargePerPoint=… cap=…%; cap effective=N env=none [postfix on StationComponent.SetPCState: …])`。
- debug（`debugProbes=true` 前 20 条）：`[QSystem][charge] cap eid=31 level=4 factor=3.00x energyPerTick=1200->3600 requiredEnergy=1200->3600`。
- 聚合：每 200 次 `[QSystem][charge] cap effective=N (postfix on StationComponent.SetPCState)`。
- 退化：`[QSystem][charge] cap <key> not armed, vanilla charge: <detail>`（只一次）；`Describe()` → `station charge cap bonus NOT ARMED (vanilla charge, reason: …)`。

### 22.8 存档、瞬态与已知限制

- `energyPerTick`/`requiredEnergy` 虽是存档字段，但每 tick 由原版重算 ⇒ 本补丁的写入是**瞬态**。唯一持久窗口：Postfix 之后同帧发生存档；读档后首 tick `InternalTickLocal`（在 `SetPCState` 之前跑）会用偏大值多充一 tick 的电，随后被重算覆盖 ⇒ 自我修正、不改 schema、无需迁移。
- 不覆盖 `isCollector`/`isVeinCollector`（轨道采集器/矿脉采集器不调用 `SetPCState`）。
- 旧 `InternalTickLocal` power 路径退役后，该方法的 patch 由 `P3_StationCharge` 移除；启动 `patchedMethods` 仍打印 **15**（PatchAll 时点），最终独立 patch 方法数仍 **18**（`StationComponent.SetPCState` 替代 `InternalTickLocal`）。
- 电网不足时按电网比例分配，充能提升小于 factor——这是正确物理语义（不是 bug）。

### 22.9 构建与测试

- `build.ps1`（便携 dotnet 10.0.401，GameDir=Steam 安装目录）：`BUILD OK`，**0 警告 0 错误**。
- `tests\run-tests.ps1`：**`ALL PASS (17 checks)` / `TEST PASS`**；新增 `CheckChargeCap`（0 点/非法输入/满电跳过/近满电边界/默认曲线 +8% & +20%/cap 优先/长整型不溢出/单调）。
- DLL：`bin\Release\DSPQualitySystem.dll` = **97,280 B**，SHA256 **`4FDF41809AFB13E02F9414D36AA6920C1582243C43861E10EEE80A192580993A`**，AssemblyVersion **0.11.0.0**。
- 测试计数：v0.10.0 的 16 checks → v0.11.0 的 **17 checks**（新增充电上限）。

### 22.10 版本链

v0.4.3 `6E3E8676…` → v0.4.4 `6363BF16…` → v0.5.0 `B7522F54…` → v0.6.0 `53410276…` → v0.7.0 `1A6A4877…` → v0.8.0 `80B07E4B…` → v0.9.0 `16087007…` → v0.10.0 `B570C0F4…`（95,232 B，16 checks）→ **v0.11.0 `4FDF4180…580993A`（97,280 B，17 checks）**。

---

## §23 v0.11.1：背包"整理"不再合并不同点数建筑 + `[QSystem][roll]` 只读日志

### 23.1 背景

- v0.10.0 的 P4 背包隔离只拦住了 `StorageComponent.AddItemStacked`（物品进背包时的并格），但玩家点背包右上角的**整理/排序**按钮走的是另一条路径：`StorageComponent.Sort(bool)`（asm_full.cs:81153-81236）会把整包格子按物品 proto index 汇总 `count`/`inc` 后 `Clear()` 重排，于是 0 点与 4 点的同类建筑又被并成一格、点数相加。用户反馈"刚修好的隔离被整理按钮一键打回"。
- 同一版本补上 v0.11.0 遗留的观测需求：`[QSystem][roll]` 只读日志（用于实机核对 7/8/9/10 点建筑的额外升品规则），以及 `QualityConfig` 里两条充电配置的过期文案。

### 23.2 原版 IL 依据与一处行号更正

- `StorageComponent.Sort(bool raiseNotify = true)`：asm_full.cs:81153-81236。第一段 `for` 循环把每格汇总进 `s_dict4sort`（key = 物品 proto index，`value.count += grids[i].count; value.inc += grids[i].inc;`）后清空该格；`EStorageType.Default` 分支再 `Clear()` 并逐组写回（`split_inc` 分格，asm_full.cs:81360-81374）；最后 `ResetOptimizationFlags(); NotifyStorageChange();`。
- **行号更正（与 task-12 描述不同，已按真实 IL 实现）**：task-12 给的 `StorageComponent.GRID`（asm_full.cs:78854）实际是 **`DeliveryPackage.GRID`**，字段为 itemId/count/inc/**ordered**/stackSize/…；**`StorageComponent.GRID` 在 asm_full.cs:79407-79418**，字段只有 `itemId / filter / count / stackSize / inc`，**没有 ordered**。因此"任一格 ordered != 0 就放弃接管"的行不通，改成**任一格 `filter != 0` 就回退原版排序**（玩家给格子设了过滤规则时交给原版）。
- `inc` 仍是"整叠总点数"：`split_inc(ref n, ref m, p)` 以 `m/n` 求每件点数，`TryAddItemToPackage`（asm_full.cs:170232）用 `inc - remainInc` 记入物品点数。

### 23.3 纯函数 `QualityMath.PlanSortedLayout`

签名（`QualityMath.cs`）：
`public static int PlanSortedLayout(int[] itemIds, int[] counts, int[] incs, int[] stackSizes, bool[] isBuilding, int[] protoIndexes, out int[] outIds, out int[] outCounts, out int[] outIncs)`

- 分组键：非建筑 = 仅 itemId（原版语义，inc 相加）；建筑 = itemId + **每件点数**（用 `SharesStack` 的交叉乘法 `(long)gridInc*incomingCount == (long)incomingInc*gridCount` 精确比较，避免除法误差）。
- 排序：主键 = 物品 proto index（与原版 `SortedDictionary` 一致；无 proto 时回退 `10000+itemId`），次键 = 每件点数升序（0 点在前），再按首次出现顺序稳定；非建筑每 id 只有一组，因此与原版顺序一致。
- 分格：每组按 `stackSize` 切成若干格，溢出格用 `SplitInc` 分配总点数（与原版 `split_inc` 逐位一致）。
- 纯 int/bool 数组 + `Func`，不依赖 Unity/游戏类型，离线可测（`Checks.P4Sort.cs` 3 条用例）。

### 23.4 注入与失败回滚（`Patches\P4_IsolationSort.cs`，411 行）

- 手动注册（不进源码 attribute 计数）：`P4_Isolation.Apply` 在 `AddItemStacked` 前缀安装成功后调用 `P4_IsolationSort.Apply(harmony)`；`harmony.Patch(typeof(StorageComponent).GetMethod("Sort", …, new[]{typeof(bool)}), prefix: new HarmonyMethod(SortPrefix))`。
- 目标校验：方法存在、返回 void、单参 `bool` 且参数名 `raiseNotify`；前缀签名 `(StorageComponent __instance, bool raiseNotify)`。任一不符 → 只 warn 一次、`Describe()` 报 NOT ARMED、Sort 保持原版。
- 前缀守卫（全部满足才接管）：`isolateBuildingStacks` 与主开关开启、`__instance.isPlayerInventory`、`type == EStorageType.Default`、网格非空/`size>0`、**所有格 `filter == 0`**、`LDB.items` 已就绪、包内至少有一个 `CanBuild` 建筑、且规划结果里**确实存在同 itemId 的多个分组**（否则原版结果本来就一致，直接交给原版）。
- 接管时：先 `(GRID[])grids.Clone()` 快照 → `Clear()` → 按规划写回 → `ResetOptimizationFlags()/NotifyStorageChange()`（仅当 `raiseNotify`）。**任何异常：把快照拷回 → `WarnOnce` → return true 交回原版**；绝不留下"清了一半"的背包。返回 false 只在整段写入完成后。
- 日志：`[QSystem][isolation] sort patch registered: StorageComponent.Sort(bool) prefix=SortPrefix (building stacks are regrouped by per-item quality points)`；启动 `[QSystem][isolation] armed (building items=N, patch points=2)`；`Describe()` = `sort armed (StorageComponent.Sort(bool) prefix=SortPrefix, regrouped=N)` / `sort NOT ARMED (vanilla sort, reason: …)`；debugProbes 下前 20 次打印 `[QSystem][isolation] sort regrouped items=… grids=… building-groups=…`；StartupAudit 新增 `[Audit] StorageComponent.Sort(bool) -> OK` + `[Audit] …sort armed (…)`。
- `P4_Isolation.Describe()` 的 patch points 现在按 `P4_IsolationSort.Armed` 显示 2（AddItemStacked + Sort），否则 1。

### 23.5 `[QSystem][roll]` 只读日志（`QualityRules.cs` + `QualityMath.RollCycleLevel`）

- `QualityMath.RollCycleLevel(..., out mode, out baseRollHit, out extraRollHit)` 承接原 `QualityRules` 的升品主体（RNG 调用次数：mode 0→1 次、mode 1→2 次、mode 2→0 次；`chance<=0` 不 roll），`QualityRules.RollCycleQuality` 保留原签名并改调它，然后打日志。
- 日志格式：`[QSystem][roll] eid=<id> in=<输入等级> bld=<建筑点数> mode=<0|1|2> base=<0|1> extra=<0|1> pooled=<产物等级> allowExtra=<true|false>`；`debugProbes` 门控、每个 (planet,entityId) 前 20 条、键表 >4096 时清空、整体 try/catch。
- **只读保证**：日志在 roll 之后调用，使用已经算出的 mode/hit 值与 RNG 状态，不额外 roll；`Checks.P3.cs::CheckRollLogNoRngChange` 用独立 v0.11.0 公式副本做了同 seed 逐位一致性校验。分馏塔走 `allowExtraUpgrade=false`（`allowExtra=false`、mode=0、最多 1 次 roll）。
- 用途（实机核对 #16）：10 点建筑应恒 `pooled=in+2` 且 `mode=2`；7/8/9 点 `mode=1` 且 `pooled` 抽样出现 in/in+1/in+2；0 点 `bld=0 mode=0 pooled=in`。

### 23.6 配置文案修正（C 项）

`QualityConfig.cs`：`chargeBonus` 描述从旧的"scales the transient power argument of StationComponent.InternalTickLocal"改为 v0.11.0 实际语义（`SetPCState` Postfix 同时乘 `energyPerTick` 与 `pcPool.requiredEnergy`，`energyMax` 不变，满电跳过）；`chargePerPoint` 描述改为"对每 tick 充入上限的百分比加成"。**仅文本，无行为变化**。

### 23.7 构建与测试

- `build.ps1`（便携 dotnet 10.0.401，`DSP_TOOLS_DIR=C:\XCM\DSP MOD\tools`）：`BUILD OK`，**0 警告 0 错误**。
- 跑测试前先删 `tests\OfflineTests\bin|obj`（此前"18 checks"是旧测试二进制假象），`tests\run-tests.ps1`：**`ALL PASS (21 checks)` / `TEST PASS`**。
- 新增用例：`Checks.P4Sort.cs` 3 条（sort basics / sort merge+溢出 / non-building 原版合并+混合背包+确定性）、`Checks.P3.cs::CheckRollLogNoRngChange` 1 条；17 → 21 checks。
- DLL：`bin\Release\DSPQualitySystem.dll` = **104,448 B**，SHA256 **`6392E8FDCC41E572CA389A63F44842FB435669995686F3B0CB4FB4459FA4F3D7`**，AssemblyVersion **0.11.1.0**。
- `patchedMethods`：启动打印仍 **15**（Sort 与 AddItemStacked 均为手动注册，PatchAll 时点不变）；最终独立方法 **19** = 18 + `StorageComponent.Sort`。

### 23.8 已知限制 / 实机待验证

- 只覆盖 `EStorageType.Default` 的背包整理；`Filtered` 类型存储（如带过滤的箱子）走原版，不进接管（`filter != 0` 也回退）。
- 交付包（`DeliveryPackage`）的整理/合并仍按原版；手持物品不变。
- 界面**手动拖拽/拆分**（直接写 GRID）仍可把不同点数合并回一格，本补丁不拦（与 v0.10.0 的已知边界一致）。
- 实机验证（#19）：背包放 0 点与 4 点熔炉各 1 → 点整理 → 仍是两格；同点数多格 → 整理后合并；3× 测试 cfg 下悬停/建造确认 4 点保留；`[QSystem][isolation] sort regrouped …` 会说明它确实接管过。
- 日志观测（#16 结案）：`[QSystem][roll]` 出现后核对 10 点 `mode=2 pooled=in+2`。

### 23.9 版本链

v0.4.3 `6E3E8676…` → v0.4.4 `6363BF16…` → v0.5.0 `B7522F54…` → v0.6.0 `53410276…` → v0.7.0 `1A6A4877…` → v0.8.0 `80B07E4B…` → v0.9.0 `16087007…` → v0.10.0 `B570C0F4…`（95,232 B，16 checks）→ v0.11.0 `4FDF4180…580993A`（97,280 B，17 checks）→ **v0.11.1 `6392E8FD…FA4F3D7`（104,448 B，21 checks）**。

---

## §24 v0.11.2：分段曲线 + 蓝图携带建筑品质（task-13 A/B）

### 24.1 交付文件

| 文件 | 行数 | 说明 |
|---|---|---|
| `QualityMath.Curves.cs` | 53 | 新增（partial `QualityMath`）：分段曲线表 + `SpeedBonusFrom` + `SpeedFactorFor` |
| `QualityData.cs` | 555 | 只加 `ChargeCapFrom` 的 curve 重载（旧线性重载保留给回归测试） |
| `QualityConfig.cs` | 230 | 新增 `speedBonusMode`（curve/linear，默认 curve）与 `blueprintQuality`（默认 true） |
| `QualityBlueprintPlan.cs` | 105 | 新增：蓝图参数尾标记的纯函数（`Magic`/`Append`/`TryRead`/`MaxParamCount`） |
| `Patches\P4_BlueprintQuality.cs` | 348 | 新增：写=Postfix、读=复用 prebuild hook 的读取/剥离 |
| `Patches\P1_BuildCapture.cs` | 210 | `AddPrebuildDataWithComponents` Postfix 增加 `PlanetFactory`/`PrebuildData` 参数，调用读取端 |
| `Patches\P3_Speed.cs` / `P3_StationCharge.cs` | 370 / 387 | factor 改走 `SpeedFactorFor`；armed/Describe 增加 `mode=curve\\|linear` |
| `Diag\StartupAudit.cs` | 277 | 新增 `BlueprintUtils.GenerateBlueprintData(...)` 签名检查 + blueprint `Describe()` 行 |
| `Plugin.cs` / `DSPQualitySystem.csproj` | 256 | 版本 0.11.2；`P4_BlueprintQuality.Apply` + 启动日志；cfg 行补 `speedBonusMode` / `blueprintQuality` |
| `tests\OfflineTests\Checks.P3.cs` / `Checks.P4Blueprint.cs` / `Program.cs` | 455 / 62 / 479 | 新增 3 条离线用例（曲线表、曲线充能、蓝图参数往返），共 24 checks |

### 24.2 曲线（用户定稿）

点数 → 累计加成百分比：`0, 2.5, 5.0, 7.5, 10.0, 15.0, 20.0, 30.0, 40.0, 60.0, 100.0`（点 0..10）。
即 10 点 = **2.00×** 制造/采集速度、**2.00×** 物流塔充电上限；低点区间平缓、中点线性、高点陡增。
`speedBonusMode=curve`（默认）时 `speedPerPoint`/`maxSpeedBonus`/`chargePerPoint`/`maxChargeBonus` 四键**不生效**（保留供 `linear` 对照）；`linear` 用 v0.11.0 的线性公式。
`QualityMath.SpeedBonusFrom(points)` 与 `SpeedFactorFor(points, curve, perPoint, cap)` 是唯一实现；制造台/矿机（`P3_Speed`）与物流塔（`P3_StationCharge`）共用同一函数——这是用户"基础数值跟充能一样"的字面落地。
超过表上限（>10 点）按 10 点处理；负数按 0。

### 24.3 蓝图容器与参数布局（task-13 B / P5 阶段 A）

- 容器：`BlueprintBuilding.parameters`（`public int[] parameters`，类 `BlueprintBuilding` 起于 asm_full.cs:181613）。序列化 `Export`/`Import`（:181675-181745）为 **`int16 长度` + 逐个 `int32`**，所以追加元素是长度前缀安全的；`Import` 先读长度再分配，尾部多 2 个 int 不会让原版读越界。
- 布局：`[... 原版参数 ...][0x51535101]["inc"]`（`inc` = 该建筑的品质点数 0..10）。`Magic` 取 "QSQ"+1 的常量，原版不会产出。
- 追加规则 `QualityBlueprintPlan.Append`：`inc<=0` → 原样返回（0 点建筑不写标记，保持未修改蓝图与原版逐字节一致）；已有尾部标记且值相同 → 幂等返回原数组；已有标记但值不同 → 克隆后只改末元素（重复制同一建筑刷新值，不叠加第二对）；`长度+2 > 32765` → 返回 null（不改，退化原版）。
- 读取 `TryRead(parameters, count, out inc)`：以逻辑长度 `count`（`PrebuildData.paramCount`）为准；`count<=0` 回退数组长度；要求倒数第二个 == `Magic`、末元素 ≥0；任何异常/不匹配返回 false（行为与 vanilla 完全一致）。
- **读端剥离**：`P4_BlueprintQuality.CapturePasteInc` 恢复点数后，把 prebuildPool 里该 prebuild 的尾部两元素清 0、`paramCount -= 2`，再交给 `AddEntityDataWithComponents` 的绑定逻辑；因此实体侧仍按原版 2048/128/… 参数处理，不残留标记。

### 24.4 挂接点（真实 IL）

- 写：`BlueprintUtils.GenerateBlueprintData(BlueprintData, PlanetData, PlanetAuxData, int[], int, float, uint[], bool)`（asm_full.cs:183925-184180+，唯一调用点 `BuildTool_BlueprintCopy.GenerateBlueprintData` :145713）→ **Postfix**（手动 `harmony.Patch`）。
  映射：`buildings[k] ↔ _objIds[k]`（:183960-183966）；`:184169-184179` 构造 `parameters` 后才可追加。
- 读：`PlanetFactory.AddPrebuildDataWithComponents(PrebuildData)`（:209925；prebuildPool 写入 :209905）——复用 v0.4.3 就存在的 `AddPrebuildData_ClearStale` Postfix（`P1_BuildCapture.cs:153-170`），它本来就在同一方法上，只是给签名加 `PlanetFactory __instance, PrebuildData prebuild` 并调用读取端；**没有新增 patch 方法**（`patchedMethods` 启动打印仍 15）。
- 粘贴侧原版链：`BlueprintUtils.BlueprintToPreviews` 内 `SetParameters(blueprintBuilding.parameters.Length, …)`（:184513；`SetParameters` :141673 **动态扩容**，无 128 上限）→ `BuildTool_BlueprintPaste.CreatePrebuilds`（:150242-150343）把参数逐个拷进 `PrebuildData`（`paramCount` 来自 `buildPreview.paramCount`）→ `AddPrebuildDataWithComponents`。所以长度 2050 的 Station 蓝图在粘贴分配阶段是安全的。

### 24.5 卸载兼容论证（硬门槛）

- 插件被禁用（DLL 改名）后：`BlueprintBuilding.Import` 按 int16 读入 2050 个 int（:181731-181736）、`SetParameters` 动态分配、`FromParamsArray`（:137866-138130）Station/Marker 只拷前 2048、其余按索引读、`SimpleParamFromParamsArray` 等忽略尾部 ⇒ 复制/保存/粘贴均不报错、行为与原版一致（只是品质标记被忽略）。
- 原版读取 `parameters` 的位置已逐个核对（见 24.6）；唯一受长度影响的是 `BlueprintPaste.UpdatePreviewModels` :147573 的 `parameters.Length == 2`（带图标传送带标记预览）：只有对**带图标传送带**注入点数才会让长度变 4，影响预览图标显示，不影响建造/存档。`inc>0` 才写，普通传送带不触发；已列为已知限制。
- 粘贴阶段的 `ArrangeParametersArray`（:148126/:148184）只用于矿机矿脉参数（非蓝图参数），与标记无关。

### 24.6 原版读 `parameters` 的清单（grep 结果，供后续排查）

`BuildingParameters.FromParamsArray` :137866（Station 2048 上限）、`SimpleParamFromParamsArray` :138132 / prebuild 调用 :138512、`FromParamsArray(prebuildPool[num].parameters)` :139030、`FromParamsArray(bp.parameters)` :140024、`:140073 parameters[0]`、`BlueprintPaste.UpdatePreviewModels` :147573（`Length==2` 传送带图标预览）、:147148/:147174（空参数判断）、`:148142`/`:154290`（`paramCount==0`）、`ArrangeParametersArray` :148126/:148184/:154287/:154350、`BuildingParameters.template` :392528、粘贴 `SetParameters` :184513。

### 24.7 数值与可观测日志

- 曲线在 cfg 中：`speedBonusMode = curve`（默认）；启动打印 `[QSystem][speed] … mode=curve`、`[QSystem][charge] … mode=curve`；`Describe()` 同样带 `mode=`。
- 蓝图：`[QSystem][blueprint] patch registered: BlueprintUtils.GenerateBlueprintData postfix=BlueprintWritePostfix (building.parameters += magic+inc)`、`[QSystem][blueprint] armed: blueprint quality (copy stores Magic+inc …)`；debugProbes 前 5 次 `[QSystem][blueprint] copy stored inc eid=… inc=… params=…` / `[QSystem][blueprint] paste restored inc prebuild=… inc=… (n=…)`；`[Audit] BlueprintUtils.GenerateBlueprintData(...) -> OK`、`[Audit] blueprint quality armed (… copied=N restored=N)`；退化 `[QSystem][blueprint] NOT ARMED (vanilla blueprints, reason: …)` + `[Audit]` 同文本。

### 24.8 构建 / 测试

- `BUILD OK`，0 警告 0 错误。
- 离线（先删 `tests\OfflineTests\bin|obj`）：`ALL PASS (24 checks)` / `TEST PASS`；新增 3 条：`P3 curve bonus table …`、`P3 curve charge cap …`、`P5 blueprint params …`（追加/读取/容量/损坏尾部/幂等/替换）。
- DLL = **113,152 B**，SHA256 `5078A6F9BAA19D0FE43F2C1CF0DBBBB890B43C264E91A4944A03003024055E9A`，AssemblyVersion **0.11.2.0**。
- `patchedMethods`：启动打印 **15**；最终独立方法 **20** = v0.11.1 的 19 + `BlueprintUtils.GenerateBlueprintData`（读取端复用已有 `AddPrebuildDataWithComponents`，任务预期 21 是按"写/读各新方法"估算，实测少 1）。

### 24.9 已知限制 / 实机清单（#20 / #21）

- 蓝图标记只写入"已建成建筑"（`_objIds` 里 objId>0）；原版对**尚未建成的 prebuild** 或某些复制模式不经过该后置（图标带传送带见 24.5）。
- 复制→粘贴的品质只跟建筑走，蓝图文件本身仍是原版格式+尾部标记；共享给原版玩家/其它 mod 不会被识别（当普通蓝图用）。
- 实机 #20：F11 注 4 点制造台 → `[QSystem][speed] assembler … level=4 factor=1.10x`；10 点 → `factor=2.00x`；改 `speedBonusMode=linear` 后 4 点回 1.08×。
- 实机 #21：4 点熔炉 → 复制成蓝图 → 拆掉 → 粘贴 → `[QSystem][blueprint] paste restored inc …` → F9 仍 4 点；把 DLL 改名后再做一次复制/粘贴/存档，确认原版不报错（卸载兼容）。
- 曲线改版后 10 点从 v0.11.1 的 1.20× 变为 2.00×，速度/充能体感会明显变化，属用户定稿（旧线性保留 `linear` 可回退）。

### 24.10 版本链

v0.4.3 `6E3E8676…` → … → v0.11.1 `6392E8FD…`（104,448 B，21 checks）→ **v0.11.2 `5078A6F9…24055E9A`（113,152 B，24 checks）**。

## 25. v0.11.3：品质隔离扩展到其它储物容器（P4 containers）

### 25.1 背景与用户口径

用户 2026-10-25 反馈"储物仓/容器里同类建筑不同点数会被合并"（以及"储液罐似乎和物流塔是同样的问题，只有一个格子"）。v0.10.0 的 P4 隔离只覆盖**玩家背包**（`isPlayerInventory=true`），因为当时无法确定其它容器是否安全；v0.11.3 把放行范围扩到玩家可交互的通用 `StorageComponent` 容器（储物仓、战场分析基站等），但**明确不动物流塔内部**——`StationComponent.storage` 是 `StationStore[]`，每种物品一格、只存整格总点数（`StationStore` asm_full.cs:78322，`max` 字段 :78334），结构上无法逐件保留点数，隔离对它不适用（本就不该 patch，保持原版平均语义）。

### 25.2 真实 IL / 源码依据

- 容器身份：`StorageComponent.isPlayerInventory`（:79442，玩家包 :170407/:170692 置 true）、`storage.type`（`EStorageType`）、`storage.grids[i].filter`（:79413）。
- 机甲内部各仓：`Mecha.reactorStorage`=Fuel（:165643）、`warpStorage`=Filtered（:165645）、`ammoStorage`=Ammo（:165789）、`bombStorage`=Bomb（:165791）、`fighterStorage`=Fighter（:165881）⇒ 这些格子是燃料/弹药/战斗机槽，按品质拆格只会浪费容量，**一律不隔离**。
- 过滤容器（`Filtered`/`DeliveryFiltered`）与任何有 `filter != 0` 的格子：玩家显式配置了槽位，按原版合并（避免配置失效）。
- 物流塔：`StationComponent.storage`（`StationStore[]` :73764），塔内 UI 放货 :485730-485731 / :527416-527417、取货 `split_inc` :485750、`keepIncRatio` :485817-485834 —— 一种物品一格 + 整格总点数，隔离不可行（未改）。

### 25.3 放行门（双层）

1. **纯函数** `QualityMath.ShouldIsolateStorage(bool isPlayerInventory, int storageType, int storageSize, bool containersEnabled)`：`size<=0 → false`；玩家背包 → true；`!containersEnabled → false`；`storageType == 0`（`EStorageType.Default`）→ true；否则 false。
2. **游戏层** `QualityRules.ShouldIsolateStorage(StorageComponent)`：读 `QualityConfig.IsolateStorageContainers` → 纯函数判定 → 机甲仓排除（`GameMain.mainPlayer?.mecha` 懒扫描，main menu/读档时下次重试）→ 任一 `grids[i].filter != 0` 回退原版 → true。全 try/catch：任何异常返回 false（回原版）。
3. 两个前缀（`AddItemStacked` 与 `Sort(bool)`）守卫从 `!__instance.isPlayerInventory` 改为 `!QualityRules.ShouldIsolateStorage(__instance)`；`isolateBuildingStacks=false` 仍是一票否决（游戏层直接返回 false）。

### 25.4 配置与可观测

- 新增 `[quality] isolateStorageContainers`（默认 `true`，仅 `isolateBuildingStacks=true` 时生效；`false` 时与 v0.11.2 逐位一致）。
- 启动：`[QSystem][isolation] armed (building items=…, patch points=…, storage containers=on|off [AddItemStacked: … player + Default containers / player only; sort …])`；debugProbes 前 20 条 `[QSystem][isolation] split item=… inc=… grids=… container=<player|Default|…>`；`[Audit] isolation armed (…)` 同步带 `storage containers=on|off`。

### 25.5 实现

- `QualityMath.ShouldIsolateStorage` 纯函数；`QualityRules.ShouldIsolateStorage` 游戏层含机甲仓排除与过滤格判定。
- `QualityData.Isolation.cs`：`AddIsolatedStacked` 的调试日志带 `container=` 标签（`player` 或 `EStorageType` 名）。
- `P4_Isolation.cs` / `P4_IsolationSort.cs`：守卫、注册日志、`armed`/`Describe()` 文本更新，`AddItem` 指定格路径本轮不拆格（沿用 v0.11.2 的"用户点哪格进哪格"）。

### 25.6 测试（24 → 27 checks）

- `CheckShouldIsolateStorage`：玩家包恒隔离（含容器开关关）；Default 容器开/关；Fuel/Ammo 等非 Default 类型不隔离；size=0 不隔离。
- `CheckContainerIsolation`：容器内 0 点+4 点建筑分成两格、同点仍合并；`isolate=false` 时与 v0.11.2 一致（合并成一格、点数相加）。
- `CheckContainerSortIsolation`：容器里 0/4/8 点/件的三组建筑整理后 3 组、升序、数量与总点数守恒。

### 25.7 构建与验证

- `BUILD OK` 0 警告 0 错误；离线清 `bin|obj` 后 `ALL PASS (27 checks)` / `TEST PASS`。
- DLL 116,224 B，SHA256 `C378F309D226DDFCD4306687BE9BF576096A7871DBD28ACE752934B1A185F429`，AssemblyVersion 0.11.3.0。
- `patchedMethods` 启动仍 **15**；最终独立方法仍 **19**（`StorageComponent.Sort` 已是 v0.11.1 的，本次未新增方法，只是放宽了同一前缀的守卫）。

### 25.8 已知限制与实机清单

- **物流塔内部不隔离**（`StationStore` 一种一格、整格总点数，结构决定；保持原版平均语义）。
- **机甲燃料/弹药/炸弹/战斗机格不隔离**（`EStorageType` 非 Default，且按点数拆格会浪费槽位）。
- **过滤容器 / 已配置过滤格的容器不隔离**（玩家显式配置优先）。
- **储液罐（`TankComponent`）不适用**：每罐段一格、只存整格总点数（`fluidInc`），与物流塔同构，不为它 patch。
- `AddItem` 指定格子路径、交付包（`DeliveryPackage`）、手持物品仍按原版合并。
- 实机验证：放 0 点与 4 点熔炉各若干进储物仓 → 点整理/直接放入 → 悬停看两格并存；F9 点数分布不变；`isolateStorageContainers=false` 重启后回到合并行为。

### 25.9 版本链

v0.11.2 `5078A6F9…24055E9A`（113,152 B，24 checks）→ **v0.11.3 `C378F309…85F429`（116,224 B，27 checks，P4 容器隔离）**。
## 26. v0.11.4（task-15）：蓝图品质结算改为 match（点数不超过实际消耗物品）

### 26.1 交付物与行数

| 文件 | 行数 | 改动 |
|---|---|---|
| `QualityMath.Blueprint.cs` | 62 | 新增：结算纯函数 + 模式解析 |
| `QualityConfig.cs` | 272 | 新增 `blueprintQualityMode`（默认 match）+ 两个只读访问器 |
| `Patches/P4_BlueprintReceipt.cs` | 235 | 新增：手/背包消耗点数收据（手动注册，2 个既有方法） |
| `Patches/P4_BlueprintQuality.cs` | 418 | 读侧改为按模式结算；marker 始终剥离 |
| `Plugin.cs` | 258 | VERSION 0.11.4 + cfg 日志增加 mode |
| `tests/OfflineTests/Checks.P4Blueprint.cs` | 114 | 新增结算/模式解析 2 条用例（27 → 29 checks） |

### 26.2 口径（用户定稿）

- 默认 **`match`**：落成点数 = `min(蓝图记录点数, 实际消耗物品的点数)`，**绝不凭空升级**。
- `author`：信任蓝图记录（= v0.11.2 行为，可选）。
- `off`：蓝图不给任何品质（等同原版）。
- 消耗物品的最终明细（用户 2026-10-26 确认）：作者点数 10 → 使用者手上 0 点 ⇒ 落成 0；手上 4 点 ⇒ 落成 4；手上 10 点 ⇒ 落成 10。

### 26.3 A 项只读调查：原版粘贴怎么付款（真实 IL / 反编译原文）

`BuildTool_BlueprintPaste.CreatePrebuilds()`（asm_full.cs:150242-150343），循环内每栋建筑：

```
:150323  int itemId = buildPreview.item.ID;
:150325  if (player.inhandItemId == itemId && player.inhandItemCount > 0)
:150327      count = 1; player.UseHandItems(count, out var _);          // 手上物品的 inc 被丢弃
:150330  else
:150333      count = 1; player.package.TakeTailItems(ref itemId, ref count, out var _); // 背包 inc 被丢弃
:150335  if (count == 1) buildPreview.objId = -factory.AddPrebuildDataWithComponents(prebuild);
:150340  else { prebuild.itemRequired = 1; buildPreview.objId = -factory.AddPrebuildDataWithComponents(prebuild); }
```

- `Player.UseHandItems(int useCnt, out int useInc)`（:170094-170114）：部分消耗时 `useInc = split_inc(...)`（每件等级保持）；整叠消耗时返回 0 的旧 bug 由快照兜底。
- `StorageComponent.TakeTailItems(ref int itemId, ref int count, out int inc, bool useBan=false)`（:80771-80816）：从尾格向前取，`out inc` = 取走物品的**总点数**（取 1 件 = 该件点数）。
- 未付款预建（材料不足）之后由 `PlanetFactory` 的批量建造逻辑补付（asm :32829-32854），同样走 4 参 `TakeTailItems`，但发生在我们看不到的后续帧。

### 26.4 收据机制（`BlueprintPasteReceipt`）

- 手动 `harmony.Patch` 两个**已存在补丁方法**上的新 processor：`Player.UseHandItems`（prefix 快照手牌 + postfix 记 `useInc/used`）与 `StorageComponent.TakeTailItems`（postfix 记 `inc/count`）。**不新增 patched method**，启动 `patchedMethods` 仍 15。
- 收据 = `(itemId, perItem, frame)`；仅在**同一帧**且 itemId 与 prebuild 一致时才被读取并立即清除；跨帧或跨物品即丢弃（粘贴"消耗 → AddPrebuild"是同步连续的，旧收据一定无关）。
- 未付款预建（`prebuild.itemRequired=1`）在粘贴时没有消耗 ⇒ match 模式落成 0（日志 `hand=none -> 0`）；若收据子系统整体无法安装，按任务要求退化 author 并只 warn 一次。

### 26.5 结算纯函数（离线可测）

- `QualityMath.BlueprintModeId(string)`：`off`/`author`/其余（含空、拼错）→ 0/2/1（**拼错永不上调，回 match**）。
- `QualityMath.SettleBlueprintInc(stored, actualPerItem, mode)`：off → 0；author → stored；match → `min(stored, actual)`；负输入按 0，结果非负。
- 读侧始终先剥离 marker（无论模式），保证原版永远看到原始 `paramCount`；`off` 模式只是不写点数。

### 26.6 可观测日志

- 启动：`[QSystem][blueprint] patch registered: BlueprintUtils.GenerateBlueprintData postfix=BlueprintWritePostfix (building.parameters += magic+inc)`、`[QSystem][blueprint] paste receipt: on (blueprint points are clamped to the paid item's points)`、`[QSystem][blueprint] armed: …`。
- 审计：`[Audit] blueprint quality armed (mode=match, receipt=on, …; copied=N restored=N skippedOff=0 authorFallback=0)`。
- `debugProbes=true` 前 20 条（每种）：`[QSystem][blueprint] paste inc min(stored=10, hand=4) -> 4 eid=2102` / `paste inc min(stored=10, hand=none) -> 0 eid=…` / `paste inc author stored=10 -> 10 eid=…` / `paste inc skipped (mode=off) eid=…`。
- 退化：`[QSystem][blueprint] paste receipt failed: …` 或 `… receipt: OFF (…); match mode falls back to author`。

### 26.7 离线测试（29 checks）

新增 2 条：`P5 blueprint settle (match min / author / off / negative inputs)` 与 `P5 blueprint mode parse (match default / case / typo -> match)`。覆盖：match 三态（min、保持、降级到 0 点物品）、负输入、author 忽略实际点数、off 恒 0、模式解析大小写/空白/拼错回 match。既有 27 条全过。

### 26.8 构建与验证

- `build.ps1` → `BUILD OK`，0 警告 0 错误。
- `tests/run-tests.ps1`（先删 `tests/OfflineTests/bin|obj`）→ `ALL PASS (29 checks)` / `TEST PASS`。
- DLL：`bin/Release/DSPQualitySystem.dll` = **121344 B**，SHA256 `8785B59DF3AC04EF0F307AAA9E541AEAC0BD320949F4A8D5A1C4B3892A68F8BD`，AssemblyVersion **0.11.4.0**。
- `patchedMethods`：启动日志打印仍 **15**（所有手动注册都发生在 PatchAll 打印之后）；运行时最终被 patch 的方法数为 **21**（v0.11.3 的 20 + 本版新增 `StorageComponent.TakeTailItems`）。`Player.UseHandItems` 早在 v0.4.3 已被 P1_BuildCapture patch，本版只给它加了一个额外 postfix processor，不增加方法数。

### 26.9 已知限制与实机验证清单

- **未付款预建**（粘贴时材料不足）在 match 模式下按 0 点落成（之后补付的物品点数不再回填）。
- 收据按"同帧 + 同 itemId"配对：若原版未来把付款与建预建拆到不同帧，match 将退化为 0（安全方向，绝不升级）。
- 第三方蓝图工具/未装本 Mod 的玩家粘贴时 marker 被原版忽略（尾部 2 int 安全，已在 v0.11.2 论证）。
- 实机清单（Lead/用户）：
  1. 背包有 10 点熔炉、手上拿 4 点熔炉 → 粘贴蓝图里 10 点熔炉 → 落成 4 点（paste inc min(stored=10, hand=4) -> 4）。
  2. 手上 0 点 → 落成 0；手上 10 点 → 落成 10。
  3. 背包 4 点、材料置于背包（非手上）→ 从背包尾格扣 4 点物品 → 落成 4 点。
  4. 材料不足 → 出现半透明预建，落成后为 0 点（已知限制）。
  5. `blueprintQualityMode=author` → 落成 10 点（旧行为）；`off` → 0 点。
  6. 卸载兼容：改名 DLL 后带标记蓝图复制/粘贴/存档不报错。

### 26.10 版本链

v0.11.3 `C378F309…85F429`（116,224 B，27 checks）→ v0.11.4 `8785B59D…2A68F8BD`（121344 B，29 checks，蓝图 match 结算）→ **v0.11.5 `23BC6B83…B69A1BF7`（126464 B，32 checks，蓝图 actual 结算 + 粘贴优先消耗）**。

### 26.11 v0.11.5：结算改为 actual + 粘贴优先消耗高品质（task-15 C）

- **用户裁决**（2026-10-26）：用户否掉 v0.11.4 的 `match`（min → 只降不升）语义与"塞绿手铺蓝图"设计，改为：
  1. **粘贴落成点数 = 实际被消耗那件物品的点数**（蓝图记录只用于日志，绝不凭空升级）；
  2. **粘贴时优先消耗玩家持有的最高品质同类建筑物品**（用户原话："不然我拿个10级的蓝图只消耗4点物品，他不就亏了嘛"）。
- **A 项真实 IL（asm_full.cs）**：
  - `BuildTool_BlueprintPaste.CreatePrebuilds()` :150242-150343：每栋 prebuild 构造后付款——先 `if (player.inhandItemId == itemId && player.inhandItemCount > 0)` → `count=1; player.UseHandItems(count, out _)`（:150325-150327，inc 被丢弃）；否则 `count=1; player.package.TakeTailItems(ref itemId, ref count, out _)`（:150330-150333）。`count==1` → `AddPrebuildDataWithComponents`（已付款）；否则 `prebuild.itemRequired=1`（未付款，:150340）。⇒ 手/背包先后是原版写死的，手持同种物品时永远优先消耗手持。
  - `Player.UseHandItems(int useCnt, out int useInc)` :170094-170114：部分消耗 `useInc = split_inc(...)`（每件等级基本不变），整叠消耗返回 0（旧 bug，用快照兜底）。
  - `StorageComponent.TakeTailItems(ref int itemId, ref int count, out int inc, bool useBan=false)` :80771-80816：从尾格向前取第一个匹配格；格内多于所需时 `split_inc`；取空后 `itemId=filter/count=0/inc=0`，`filter==0` 时 `stackSize=0`；`lastFullItem=-1; NotifyStorageChange()`。
  - 交换原语：`Player.SetHandItems(int itemId, int count, int refGrid)` :170050-170092 —— 先 `PutHandItems()`（把手持放回背包）再 `package.TakeItemFromGrid(...)`（把指定格装到手上）；`inhandItemId/inhandItemCount/inhandItemInc`、`StorageComponent.grids/size` 均为 public。
  - 未付款预建的补付在 `PlanetFactory` 批量建造逻辑 :32829-32854（后续帧、同样丢弃 inc）⇒ 本版在粘贴帧拿不到真实点数，按 0 点落成（已知限制）。
- **结算**：`QualityMath.BlueprintSettleActual=1` 成为默认（`blueprintQualityMode` 默认 `actual`）；`author`（信任蓝图记录）/`off` 保留；`actual` 下收据缺失 → 0（不写点数）；蓝图 stored 只用于日志；`BlueprintSettleMatch` 常量保留为 actual 的别名。
- **优先消耗实现**：
  - `_inPaste` 门：手动注册 `BuildTool_BlueprintPaste.CreatePrebuilds()` 的 Prefix/Postfix（新增 1 个 patched method）。
  - **手持路径**：`Player.UseHandItems` 的新 Prefix（Priority.High，仅 `_inPaste` 生效）：若背包存在同 itemId、每件点数**严格更高**的格（同点数不换=手持优先），用 `SetHandItems` 把该格换到手上（原手持自动进背包），再让原版继续消耗 1 件；期间 `_swapping=true` 防收据污染。
  - **背包路径**：`StorageComponent.TakeTailItems`（4 参）的新 Prefix（仅 `_inPaste`）：按"每件点数最高、同点数低索引优先"选格，复刻原版取货/记账后返回 false；实际 inc 直接写进收据；任何异常/不确定 → return true 走原版（无损）。
  - **精度**：选格/换手判定用交叉乘法比较分数（`inc/count`），避免 45/10=4.5 被整数除法截断成 4 的误判。
- **收据硬化**：只在 `InPaste && !Swapping` 且同帧、同 itemId 时写入；`CapturePasteInc` 入口无条件 `Clear()`；读取即清除。
- **纯函数**：`QualityMath.PerItemPoints`、`ChooseBestMatchGrid`（最高每件点数、同点低索引）、`ShouldSwapHand`（严格更高才换）。
- **离线测试**：29 → **32 checks**（新增 actual 三态与 author/off、优先选格/换手判定、无收据=0；既有 `CheckBlueprintParamRoundTrip`/`CheckBlueprintMode`/`CheckBlueprintSettle` 按新语义更新）。
- **构建与验证**：`build.ps1` → `BUILD OK`，**0 警告 0 错误**；`tests/run-tests.ps1`（先删 `tests/OfflineTests/bin|obj`）→ `ALL PASS (32 checks)` / `TEST PASS`；DLL **126,464 B**，SHA256 `23BC6B83B013E1F5D0714864B5A0DC12D33432D8C534FCD2D7F31922B69A1BF7`，AssemblyVersion **0.11.5.0**。
- **patchedMethods**：启动日志打印仍 **15**；运行时最终被 patch 方法数 **22** = v0.11.4 的 21 + `BuildTool_BlueprintPaste.CreatePrebuilds`；`Player.UseHandItems`/`StorageComponent.TakeTailItems` 只新增 processor，不增加方法数。
- **新增日志**：`[QSystem][blueprint] paste receipt: on …`；debugProbes 前 20 条 `source pick: swapped hand(<perItem>pts x<count>) <- grid(<idx>, <perItem>pts x<count>) reason=higher` / `source pick: backpack grid(<idx>, <perItem>pts x<taken>) reason=highest` / `pay eid=… stored=… actual=… source=hand|backpack grid=… -> <final> (mode=actual)`；退化 `[QSystem][blueprint] source … not armed, vanilla pick: …`。
- **已知限制**：只对蓝图粘贴生效（手持建造/普通取物/物流塔内部/容器不参与）；交换会把原手持整叠放回背包（可能多占一格）；未付款预建仍按 0 点落成；UI 手动拖拽/拆分绕过隔离；第三方蓝图工具/未装本 Mod 的玩家不识别 marker（当普通蓝图）。
---

## §26.12 v0.11.5 最终口径（用户裁定）：通用优先消耗；蓝图品质改为可选默认关

> 说明：§26.11 中记录的 v0.11.5 早期实现（`blueprintQualityMode=actual`、`actual=min(蓝图记录, 实际消耗)`）**已被用户口径作废**，保留作历史记录。最终实现见本节。

### 26.12.1 用户口径
用户原话（要点）：蓝图中记录的品质与物品点数"横竖都应该是看消耗的物品点数"；蓝图真正要做的应该是**优先消耗手中的高品质建筑**。因此：
1. 蓝图结算逻辑不保留（不新增 `blueprintQualityMode`、不新增 `SettleBlueprintInc`）；
2. 蓝图携带品质（v0.11.2 的 magic+inc 写入/恢复）**保留但默认关闭**（`blueprintQuality=false`）；
3. 新增通用功能：手持建造与蓝图粘贴时**优先消耗玩家持有的最高品质同类建筑物品**，落成点数 = 实际被消耗物品点数。

### 26.12.2 实现
- `QualityMath.Blueprint.cs`：只保留纯函数 `PerItemPoints`、`ChooseBestMatchGrid`（每件点数最高、同点低索引、交叉乘精确比较）、`ShouldSwapHand`（严格更高才换）、`ShouldApplyBlueprintInc(bool enabled, bool markerPresent)`。
- `QualityConfig`：删除 `blueprintQualityMode` 及其派生属性；`blueprintQuality` 默认 **false**；新增 `preferHighQualityConsume` 默认 **true**。
- `P4_BlueprintQuality`：回到 v0.11.2 行为（copy 写 marker、paste 恢复并剥离 marker），`CapturePasteInc` 改为返回 bool——蓝图品质未启用/无 marker 时返回 false，交给通用消耗捕获。
- 新增 `Patches\P4_ConsumePriority.cs` + `P4_ConsumePriority.Sources.cs`（partial，共 <500 行/文件）+ `Patches\P5_ConsumeLog.cs`：
  - 窗口：`BuildTool_Click.CreatePrebuilds()` 与 `BuildTool_BlueprintPaste.CreatePrebuilds()` 的 Prefix/Postfix（`_inHandBuild` / `_inPaste`），窗口外一切原版；
  - 手持路径：`Player.UseHandItems` Priority.High Prefix——背包存在同 itemId 且每件点数严格更高的格时用 `Player.SetHandItems(itemId, grid.count, best)` 换到手上（原手持自动回背包），`_swapping` 防重入；Postfix 记录实际消耗点数（整叠返回 0 的旧 bug 用前缀快照兜底）；
  - 背包路径：`StorageComponent.TakeTailItems(int&,int&,int&,bool)` Priority.High Prefix——自选最高格并复刻原版 `SplitInc`/清格/`lastFullItem=-1`/`NotifyStorageChange()`，返回 false 跳过原版；异常/不确定一律 return true 走原版；
  - 收据单槽（itemId、每件点数（百分之一整数）、frame、来源、格号），只在窗口内且非换手时写入；`CapturePasteConsumed(prebuild, prebuildId)` 在 `P1` 的 `AddPrebuildDataWithComponents` postfix 中、蓝图未处理时给预建写入消耗物品点数（`QualityData.SetPrebuildInc`）——通用机制，不涉及蓝图结算。
- `P1_BuildCapture`：`AddPrebuildData_ClearStale.Postfix` 中先 `CapturePasteInc`（返回 bool），未处理则 `CapturePasteConsumed`。
- `Plugin`：VERSION `0.11.5`；手动注册 `P4_ConsumePriority.Apply(harmony)` 并打印 `[QSystem][consume] <Describe>`；`LogConfig` 增加 `preferHighQualityConsume`（去掉 blueprintQualityMode）。
- `StartupAudit`：新增 `Player.UseHandItems`、`StorageComponent.TakeTailItems`、`BuildTool_Click.CreatePrebuilds()`、`BuildTool_BlueprintPaste.CreatePrebuilds()` 反射自检 + `[Audit] <P4_ConsumePriority.Describe()>`。

### 26.12.3 日志与可观测
- 启动成功：`[QSystem][consume] patch registered: highest quality building item is consumed first during hand build and blueprint paste (hand swap + best grid)`、`[QSystem][consume] armed: prefer high quality building items (preferHighQualityConsume=True)`；审计 `[Audit] consume priority armed (…)`。
- debugProbes 前 20 条：`[QSystem][consume] pick item=2104 hand=4pts x1 -> grid=37 10pts x3 reason=higher`、`[QSystem][consume] pick item=2104 -> grid=41 10pts x3 reason=highest`、`[QSystem][consume] paste inherits item inc=10 source=hand prebuild=…`。
- 每 200 次聚合：`[QSystem][consume] effective=N (swapped=… bestGrid=… captured=…)`。
- 退化：`[QSystem][consume] NOT ARMED (vanilla consumption order): <reason>`。

### 26.12.4 构建与验证
- `build.ps1` → `BUILD OK`，**0 警告 0 错误**。
- 离线（先删 `tests\OfflineTests\bin|obj`）：`ALL PASS (29 checks)` / `TEST PASS`；新增/改写用例：`P5 blueprint params`（往返/损坏尾部）、`P5 consume priority`（每件点数最高/同点手持优先/严格更高才换/低索引 tie-break/fractional 精确比较）、`P5 blueprint quality gates`（默认关不注入、开启且带 marker 才注入）。
- DLL **126,464 B**，SHA256 `922E766CE14F5E33F0B8B8C5902B2341EAAE3CA6E59448A240D172A67CEDDFBF`，AssemblyVersion **0.11.5.0**。
- `patchedMethods`：启动日志打印仍 **15**（手动注册都在 PatchAll 之后）；运行时最终被 patch 方法数 **22** = v0.11.4 的 21 + `BuildTool_BlueprintPaste.CreatePrebuilds`（`BuildTool_Click.CreatePrebuilds` 早已被 P1 的 `CreatePrebuilds_Capture` patch 同一方法，本次只加 processor 不增加方法数；`Player.UseHandItems`/`StorageComponent.TakeTailItems` 同理）。以实机启动日志为准。

### 26.12.5 已知限制
- 只作用于手持建造与蓝图粘贴的建筑物品消耗步骤；普通取物/分拣器/物流塔/容器保持原版顺序。
- 换手会把原手持整叠放回背包（可能多占一格）。
- 未付款预建（材料不足、半透明）补付在后续帧，点数无法追溯，补全后按 0 点落成。
- `blueprintQuality=true` 时蓝图记录优先于消耗物品（保留 v0.11.2 行为，默认关闭）。
- 第三方蓝图工具 / 未装本 Mod 的玩家不识别 marker（当普通蓝图用）。
- 未实机验证：`SetHandItems` 换手在真实背包右键/UI 交互下是否会干扰选中状态；粘贴多栋不同物品时的收据配对（同帧同 itemId 已保证，但不同 itemId 交错未实机测）。
---

## §27 v0.11.6（P6 阶段1）：10 点建筑金色描边（纯 HUD 覆层）

### 27.1 用户口径与实现方式
用户要求：给当前星球上点数达到 `maxQuality`（默认 10）的建筑加**屏幕空间的金色描边**
（外发光 + 四角加粗、轻微呼吸），远处随距离缩放；**不要**改模型材质、不要 GPU 实例化 hack。
因此实现为**纯 HUD**：插件自建一个 `ScreenSpaceOverlay` Canvas 子节点，每帧把目标建筑的世界
包围盒投影成屏幕矩形，用一个**程序生成的 Mesh** 画描边，不 patch 任何游戏方法、不改
`MeshRenderer`/`Material`/存档，关闭配置即整层隐藏。

### 27.2 目标选择与几何
- 目标：当前星球（`GameMain.localPlanet.factory`）上 `QualityData.Get(planetId).buildingInc[eid] >= MaxLevel` 的实体。
- 实体数据来自 `PlanetFactory.entityPool`（`EntityData.id/protoId/pos/rot`），proto 取 `LDB.items.Select(protoId)`。
- 包围盒：`proto.prefabDesc.selectCenter` + `selectSize`（8 角，先按 `rot` 旋转再投影）；
  `selectSize` 缺失时退化为 `roughRadius`（水平）与 `roughHeight`（垂直），半尺寸 clamp 0.5–30m。
- 距离 > 120m 不画；屏幕外剔除；同屏最多 `tenPointOutlineMax`（默认 64）个、按摄像机距离**取近**。
- 投影用 `GameCamera.main`（回退 `Camera.main`）；`WorldToScreenPoint` 得到像素坐标。

### 27.3 绘制
- 单个 `MaskableGraphic`（`OutlineMeshGraphic`）用 `VertexHelper` 生成顶点：外发光边（alpha×0.22）、
  主描边、四角 L 形加粗；金色 `#FFD24A`；alpha 呼吸 `OutlinePulseAlpha(Time.time)`（0.65–1.0，周期 2s）。
- `raycastTarget=false`、Canvas `sortingOrder=-1000`：不拦截输入、层级低于原版弹窗。
- 无贴图资源：`mainTexture => Texture2D.whiteTexture`，顶点色直接上色。

### 27.4 配置与安全边界
| 键 | 默认 | 范围 | 说明 |
|---|---|---|---|
| `tenPointOutline` | true | bool | 总开关；false = 整层隐藏 |
| `tenPointOutlineWidth` | 2 | 1–6 px | 线宽，使用时 clamp |
| `tenPointOutlineMax` | 64 | 0–256 | 每帧上限（按距离取近），0 = 关 |
- 守卫：`enabled`/`tenPointOutline`/`GameMain.isRunning`/`localPlanet.factory`/`UIRoot.instance`/
  `VFInput.inFullscreenGUI`/相机任一不就绪 → 本帧不画；所有异常 catch 后隐藏并只 warn 一次。

### 27.5 日志（可观测）
- `[QSystem][outline] armed (max=64 width=2, screen-space golden border; model renderers and materials untouched)`
- debugProbes 前 20 条：`[QSystem][outline] mark eid=… rect=l,b,r,t alpha=…`
- 每 300 帧：`[QSystem][outline] marked=N candidates=… max=…`
- 关闭一次：`[QSystem][outline] hidden (golden border off: tenPointOutline=false)`
- 启动审计：`[Audit] outline armed (10-point golden border: max=… width=…, nearest first, screen space only; model renderers untouched)`

### 27.6 测试与构建
- 新增离线用例 3 条：`CheckOutlineMath`、`CheckOutlineRect`、`CheckOutlineSelection`。
- `ALL PASS (32 checks)`；`BUILD OK` 0 警告 0 错误；DLL **138,752 B**，SHA256
  `96890B488707508D3031ECD7B6BEC5A0BD433EA1C72313B0B8D46BB7AB12BF8F`，AssemblyVersion **0.11.6.0**。
- `patchedMethods`：本版**不新增任何补丁** ⇒ 启动日志打印仍 **15**，运行时最终仍 **22**。

### 27.7 已知限制 / 待实机
- 只画当前星球、屏幕内的实体；`tenPointOutlineMax` 截断时只画最近的。
- 包围盒来自 `PrefabDesc.selectCenter/selectSize`，少数设施该数据可能为空 → 退化为粗略球/柱。
- 未实机验证视觉效果、UI 层级冲突与 FPS；需 Lead 部署后截图确认。
- 本版同时把 `[QSystem][consume]` 日志补全：`pick item=… planet=…`、
  `paste inherits item=… planet=… perItem=…`（每次换手/选格/继承均可从日志复查）。
- 恢复 v0.11.2 “蓝图保品质”行为：把 `blueprintQuality` 设为 `true`。

### 27.8 版本链
`v0.11.5 → v0.11.6`：新增 P6 视觉标记（金色描边）+ consume 日志补全；无补丁数量变化。
### 27.9 v0.11.7 修订：投影对齐与包围盒裁剪（实机反馈后修复）
首版（v0.11.6）实机截图暴露两个视觉问题，v0.11.7 修复：
1. **系统性左上偏移（越靠右越明显）**：根因是 `Camera.WorldToScreenPoint` 返回的是**相机像素坐标**，
   而游戏可开启"渲染缩放"（render scale）使 `camera.pixelWidth/Height` 小于 `Screen.width/Height`，
   直接用相机像素当屏幕像素画，等价于把整层按 `cameraPixels/screenPixels` 比例缩向左下原点，
   于是画面越靠右/上偏差越大。修复：`QualityMath.CameraPixelsToScreen` 先按
   `screen/ cameraPixel` 归一化到真实屏幕像素，再用
   `RectTransformUtility.ScreenPointToLocalPointInRectangle` 映射进 Canvas 局部坐标，
   这样 Canvas 的 scale/矩形尺寸/DPI 也不再影响位置。
2. **框比建筑大 1.2–1.4×、高塔框到天空**：`PrefabDesc.selectSize` 是**选择框**（比模型宽松，且高塔含
   桅杆/天线）。修复：水平轴 ×0.85、垂直轴 ×0.6（即主体高度约 `selectSize.y*0.3` 半高），
   既贴住建筑主体，也不再把桅杆上方算进框。
3. 新增一次性诊断行（便于以后核对）：
   `[QSystem][outline] projection: screen=1920x1080 cameraPixels=… canvas=…`
   —— 若 `cameraPixels` 与 `screen` 不同即说明游戏开了渲染缩放，也证明修复生效。
4. 版本 **v0.11.7**；DLL **140,288 B**，SHA256
   `3D294231B46A3B6F2AA9DF137FE9910E95DDAB3E68D4BBF29301130B5DFFB0C9`，AssemblyVersion **0.11.7.0**；
   `BUILD OK` 0 警告 0 错误；`ALL PASS (32 checks)`（新增相机像素→屏幕像素换算断言）；
   `patchedMethods` 仍 15（启动）/**22**（运行时），本版不新增补丁。
5. 仍需实机确认：偏移是否归零、框体贴合度（0.85/0.6 是否合适）、高塔是否不再到天、FPS。
### 27.10 v0.11.8 修订：按实机实测收敛框尺寸/中心（诊断否定渲染缩放假说）
v0.11.7 的 `projection:` 行实测为 `screen=1920x1080 cameraPixels=1920x1080 canvas=1920x1080`
——**三者相等，游戏没有渲染缩放**，所以 v0.11.7 的相机像素归一化不是主因（保留但无害）。
真实偏差来自 `PrefabDesc.selectSize` 语义：它是**选择框**（比可见底盘大 25–30%），
且框的几何中心比视觉贴地中心高。v0.11.8 改按实机测量值收敛：
1. 水平轴系数 `0.85 → 0.66`（每轴半宽 = `size*0.5*0.66`）；垂直轴 `0.6 → 0.48`；
   高结构（`size.y > 15`）框高只取 `size.y*0.35`（半高 `size.y*0.175`）。
2. 中心下移 `(1 - factor) * size.y * 0.5`（普通 0.26×size.y、高结构 0.325×size.y），
   让框底边对齐地面足迹而不是几何中心。
3. 新增 cfg `tenPointOutlineOffsetY`（默认 12px，clamp −64..64）：屏幕像素级垂直校准，
   游戏内改 cfg 重启即可微调，不必重编。
4. 样式微调：外发光 `3.6 → 2.4px`（`width*1.2`），四角 L 长度 `8 → 10px`（`width*5`）。
5. `armed` 行带上系数：`scale=0.66/0.48 offsetY=12`，便于从日志核对；`projection:` 仍每进程一次。
6. 版本 **v0.11.8**；DLL **141,312 B**，SHA256
   `04A3BACDC0F93253507C24008CDDFE23D48E0DD6ADBF889A8FC307B72081BD36`，AssemblyVersion **0.11.8.0**；
   `BUILD OK` 0 警告 0 错误；`ALL PASS (32 checks)`（新增框尺寸/下移公式断言）；`patchedMethods` 仍 15/22。
7. 待实机确认：框是否贴合底盘（0.66/0.48 实测系数）、高塔框是否只到主体、残余像素偏移用 offsetY 微调。

## 27.11 v0.11.9：金色光环（P6 视觉标记，按用户口径替换屏幕描边）

### 27.11.1 需求口径
v0.11.6–v0.11.8 先做的是"屏幕空间金色方框"（HUD 覆层）。用户看到实机截图后否决："别描边了，我说的不是这种，我想让你沿着模型的边画一条金边……换成光环方案。"
v0.11.9 改为：**每栋 10 点建筑头顶悬浮金色光环**（程序生成环形贴图、面向相机的 billboard、缓慢自转 + 轻微浮动 + alpha 呼吸）。描边代码保留但默认关闭（`tenPointOutline=false`），并在 cfg 注释中标注"实验性，屏幕框不是模型描边"。

### 27.11.2 实现
新增文件（保持单文件 <500 行）：
- `QualityMath.Halo.cs`（191 行，纯函数）：环形贴图剖面 `HaloAlphaAt(q)`（内孔 0、0.55→0.78 升到 1、到 0.92 衰减、外圈 0.22 柔和光晕、边缘 0）；`HaloRadius(selectSizeMagnitude,sizeScale)` = `clamp(mag*0.18,0.6,2.4)*size`；`HaloPulseAlpha`（0.55~1.0，周期 2s）；`HaloBobOffset`（±0.12m，周期 2.5s）；`HaloSpinDegrees`（20°/s）；`HaloPixelRadius`（屏幕空间回退路径用）；`HaloQuadLayout`（4 顶点/4 UV/6 索引，供 Mesh 与离线测试）。
- `Patches\P5_TenPointHalo.cs`（456 行，核心）：对象池（≤`tenPointHaloMax`）、`Install`、`Tick`、`Describe`、日志。每帧：守卫（enabled/tenPointHalo/未载入/UI 未就绪/全屏 GUI/相机）→ `QualityData.CollectFullQualityEntities` → 逐 eid 取 `entityPool`/`LDB.items.Select(protoId)`/`PrefabDesc.selectCenter/selectSize` → 位置 = 包围盒顶部 + `tenPointHaloHeight` + bob；"上"方向用 `entity.pos.normalized`；距离 >120m 与屏幕外剔除 → `QualityMath.SelectOutlineTargets` 取最近的 N 个。
- `Patches\P5_TenPointHalo.Draw.cs`（244 行，partial）：3D 路径 = 池化 quad（`MeshRenderer`+`MeshFilter`+独立 `Material`）面向相机、`localScale=2*radius`、材质颜色 alpha=呼吸值、UV 随自转旋转；屏幕空间路径 = 同一贴图在 Canvas 上按投影位置/像素半径画 sprite（`HaloSpriteGraphic`）。
- `Patches\P5_HaloAssets.cs`（134 行）：`BuildRingTexture`（128×128 RGBA32，`wrapMode=Clamp`、`filterMode=Bilinear`，按 `HaloAlphaAt` 生成金色环）与 `BuildQuadMesh`（`QualityMath.HaloQuadLayout`）；`HaloSpriteGraphic` 回退绘制类。
- `QualityConfig.cs`：新增 `tenPointHalo`(true)/`tenPointHaloHeight`(1.6)/`tenPointHaloSize`(1.0)/`tenPointHaloMax`(64) 与三个 clamp 属性；`tenPointOutline` 默认改 `false`。
- `Plugin.cs`：`VERSION="0.11.9"`；Awake 安装光环并打 `[QSystem][halo] <Describe>`；Update 调 `Tick()`；`LogConfig` 增加光环四项。
- `Diag\StartupAudit.cs`：`[Audit] <P5_TenPointHalo.Describe()>`。
- `tests\OfflineTests\Checks.P5Halo.cs`（`CheckHaloMath`）+ `OfflineTests.csproj`（`Checks.P5Halo.cs`、`QualityMath.Halo.cs` 链接）+ `Program.cs` 注册。

Shader 回退顺序：`Legacy Shaders/Particles/Additive` → `Sprites/Default` → `Unlit/Transparent`；都找不到才回退屏幕空间 sprite，并 warn 一次。`renderQueue = 3050`（Transparent），不投影阴影、不参与光照。

### 27.11.3 日志
- 启动：`[QSystem][halo] armed (max=64 height=1.6 size=1.0 shader=<名字|screenspace>)`
- 每个建筑前 20 条（`debugProbes=true`）：`[QSystem][halo] mark eid=… world=… dist=… radius=… alpha=…`
- 每 300 帧：`[QSystem][halo] marked=N visible=… max=…`
- 关闭时一次：`[QSystem][halo] hidden (…)`
- 退化：`[QSystem][halo] NOT ARMED (no golden ring, reason: …)`；tick 异常只 warn 一次并本会话停用。
- 审计：`[Audit] halo armed (golden ring above 10-point buildings: max=… height=… size=… shader=…, nearest first; building renderers untouched)`

### 27.11.4 构建 / 测试
- `BUILD OK`，0 警告 0 错误（构建前先删 `tests\OfflineTests\bin|obj`）。
- 离线：`ALL PASS (33 checks)`（新增 `CheckHaloMath`：环形剖面、半径 clamp、pulse 曲线、bob 峰值/谷值/周期、spin 单调、pixel radius、quad 布局）。
- DLL：`bin\Release\DSPQualitySystem.dll` = **156,160 B**，SHA256 **`8E14425BED2E0359A0906425DF82BB35BFB37946E91ED0F3E6674503DDBDF54E`**，AssemblyVersion **0.11.9.0**。
- `patchedMethods`：本版**不新增任何补丁**（纯渲染，不 patch 游戏方法）⇒ 启动打印仍 **15**，运行时最终仍 **22**。

### 27.11.5 已知限制 / 实机核对点
- 光环是**视觉**标记：不改变建筑数值、不写存档；关掉 `tenPointHalo` 即完全消失。
- 3D 路径依赖 Unity 内置 shader；若某个版本找不到 shader，会自动回退屏幕空间 sprite（日志 `shader=screenspace` + 一条 warning），观感略不同但仍能显示。
- 只覆盖当前星球 + 已建成的 10 点建筑；蓝图预建（半透明）不显示；同屏超过 `tenPointHaloMax` 时只显示最近的。
- 实机要核对：①10 点建筑头顶能看到金色环；②远/近尺寸合理、不遮挡 UI；③`tenPointHalo=false` 后消失；④FPS 无明显下降（同屏 ≤64 个 quad）；⑤`[Audit] halo armed …` 与 shader 名字符合预期。

### 27.11.6 版本链
- v0.11.7：描边相机像素归一化 + 包围盒系数（DLL 140,288 B）。
- v0.11.8：描边系数 0.66/0.48、高结构 0.35、中心下移、`tenPointOutlineOffsetY`（DLL 141,312 B，SHA `04A3BACD…`）。
- **v0.11.9：金色光环（本版）**；描边默认关、保留为实验性。
## 27.12 v0.11.10：光环观感修正（顶部悬浮 / 更大 / 真空心环）

### 27.12.1 反馈
v0.11.9 实机（pid 22016）后用户/Lead 反馈三个问题：①光环位置太低、嵌在建筑体内被挡住；②光环太小（宽建筑尤其明显）；③近景看起来像实心黄饼，不是环。

### 27.12.2 修正
1. 位置改为"浮在建筑最高点之上"：`top = center + up * (selectSize.y * 0.5)`，`world = top + up * (tenPointHaloLift + bob)`；`tenPointHaloLift` 默认 1.2m（clamp 0–6），不再使用固定 1.6m 的绝对高度（旧键 `tenPointHaloHeight` 已删除，旧 cfg 行会被忽略）。`center = entity.pos + entity.rot * selectCenter`，`up = entity.pos.normalized`（星球表面法线）。
2. 半径改按水平占地：`radius = clamp(max(selectSize.x, selectSize.z) * 0.42, 1.0, 3.2) * tenPointHaloSize`；`tenPointHaloSize` 默认 1.0（clamp 0.3–3.0）。取不到 selectSize 时用 `roughRadius*2` 兜底。
3. 环形贴图改真空心环：尺寸 128×128 → **256×256**，`wrapMode=Clamp`、`filterMode=Bilinear`、无 mipmap；alpha 剖面 `HaloAlphaAt(q)`：`q<0.55` 全透明（内孔）、`0.55–0.58` 快速升到 1、`0.58–0.82` 环体实心、`0.82–1.0` 线性衰减的柔和外晕（`HaloGlowStrength=0.45`）、`q>=1.0` 透明。呼吸下限 0.55 → **0.60**（上限 1.0）。
4. 材质/混合：`renderQueue = 3100`；`_ZWrite=0`、`_ZTest=8 (Always)`、`_Cull=0`（用 `HasProperty` 保护，shader 不支持时跳过）；颜色 `(1.0, 0.82, 0.29, 1)`（#FFD24A），加法混合。
5. debug 行加 `top=` 与 `lift=`：`[QSystem][halo] mark eid=… world=… top=… lift=… dist=… radius=… alpha=…`；每 300 帧 `[QSystem][halo] marked=N visible=… max=…` 保持。

### 27.12.3 文件
- `QualityMath.Halo.cs`（191 行）：alpha 剖面常量与函数、`HaloRadius(sizeX, sizeZ, sizeScale)` 新签名、pulse 下限 0.60。
- `Patches\P5_TenPointHalo.cs`（约 470 行）：顶部+lift 定位、水平占地半径、`Tops` 列表、材质属性、mark 日志 top/lift。
- `QualityConfig.cs`（约 365 行）：`tenPointHaloLift`（默认 1.2，clamp 0–6）+ `TenPointHaloLiftClamped`；`TenPointHaloSizeClamped` 改 0.3–3.0。
- `Plugin.cs`：VERSION `0.11.10`、LogConfig 键名 `tenPointHaloLift=`。
- `tests\OfflineTests\Checks.P5Halo.cs`：alpha 剖面/半径/pulse 断言按新公式更新。
- `README.md`（96 行）：版本、P6 里程碑、光环配置行（含旧键改名说明）、视觉标记说明、日志关键字。

### 27.12.4 构建 / 测试
- 先删 `tests\OfflineTests\bin|obj` 再跑：`BUILD OK`，**0 警告 0 错误**；离线 **`ALL PASS (33 checks)`**。
- DLL：`bin\Release\DSPQualitySystem.dll` = **157,184 B**，SHA256 **`08EE21F7D3EBD51CA169929C53FD06398745BB7BF805B8CD5222417DEEE023B1`**，AssemblyVersion **0.11.10.0**。
- `patchedMethods`：本版不新增补丁（纯渲染）⇒ 启动打印仍 **15**，运行时最终仍 **22**。
- 启动日志预期：`[QSystem][halo] halo armed (golden ring above 10-point buildings: max=64 lift=1.2 size=1.0 shader=<名字>, nearest first; building renderers untouched)`。

### 27.12.5 配置迁移
旧键 `tenPointHaloHeight`（v0.11.9）已改名 `tenPointHaloLift`，语义从"距顶部的绝对 1.6m"改为"在建筑最高点之上的 lift（默认 1.2m）"。BepInEx 会把旧行保留在 cfg 里但不再读取；建议删除旧行（或整段重置）以避免困惑。

### 27.12.6 实机核对点
①近景：金色环应**悬在建筑最高点上方约 1.2m**、不再卡在机器中部；②宽建筑（如研究站/储物仓）环明显变大、覆盖水平占地；③环是**空心**的（内孔透明、能看到建筑本体）；④环不被建筑挡住（ZTest Always）；⑤`tenPointHaloLift`/`tenPointHaloSize` 可在 cfg 里微调后重启生效。

### 27.12.7 版本链
- v0.11.9：金色光环首版（156,160 B，SHA `8E14425B…`）。
- **v0.11.10：光环观感修正（本版）**。

## 27.13 v0.11.11：品质数据内嵌 .dsv（不再产生侧文件）

### 27.13.1 动机
从 v0.4.3 起，品质数据写在 `BepInEx\config\DSPQualitySystem\saves\<槽名>.json`。用户反馈这等于把一份存档人为劈成两半：卸载/备份/删档/回滚都会丢档或错位；自动存档尤其明显——游戏先把档写成 `_autosave_tmp.dsv`，再 `File.Move` 轮转成 `_autosave_0..3.dsv`，侧文件不会跟着改名，于是 json 的槽名和实际文件名错位（读 `_autosave_1.dsv` 时找不到 `_autosave_1.json`，只能去猜 `_autosave_tmp.json`）。

### 27.13.2 方案（Lead 逐行核对反编译代码后定案）
1. **追加点**选在 `GameData.Export(BinaryWriter)`（`asm_full.cs:193424`）**之后** —— 它是整份游戏数据写入的最后一个调用，唯一调用点是 `GameSave.SaveCurrentGame`（`:200718`）里的 `data.Export(binaryWriter)`（`:200821`）。`SaveCurrentGame` 在写完 Export 之后才执行 `long position = fileStream.Position; fileStream.Seek(6L, Begin); binaryWriter.Write(position);`（`:200823`），把总长回填到 offset 6 的 `long fileSize`，所以 Postfix 追加的字节会被自动计入 fileSize，`LoadCurrentGame` 的 `if (fileStream.Length != binaryReader.ReadInt64()) LogError("Incomplete game save")`（`:200914`，**精确相等**）仍然通过。
2. **读取点**选在 `GameData.Import(BinaryReader)`（`:193499`）**之后** —— 唯一调用点 `GameMain.data.Import(binaryReader)`（`:200979`），之后 `LoadCurrentGame` 不再读文件、也不校验尾部字节。⇒ 卸载 MOD 后原版读到原版数据末尾就停，尾部追加的 12+N 字节被完全忽略。
3. **尾部布局** `[int32 payloadLength][payload UTF-8 JSON][8B magic "DSPQSQ1\0"]`，magic 放最后，便于从末尾往前识别；payload 的 JSON schema 与 v0.4.3 以来的侧文件**逐字节同构**（`{"version":1,"gameName":…,"seed":…,"planets":{…}}`，只写非零项），所以旧 json 可以直接当尾块 payload 使用。
4. **无条件写**：即使没有任何品质数据也写空尾块（`planets={}`，12 字节 + 45 字节 JSON），用来标记"这档由本 Mod 保存过"——`EmbeddedLoaded` 与 legacy 迁移分支就靠这个事实区分。
5. **旧档迁移是一次性、只读的**：读档时若没有尾块（`TryRead` 返回 false），才去读旧 json；读到只放内存，**不写回也不删除**（此时该 `.dsv` 还没被 MOD 保存过，json 仍是唯一副本）；等该档下一次保存写入尾块后，`SaveCurrentGame` Postfix 才删除旧 json。
6. 兼容性红线：①无品质数据也必须写出合法尾块且原版能读；②不改 offset 6、不碰 fileSize；③所有 MOD 代码异常必须 catch，绝不能让原版保存/读档失败；④日志前缀统一 `[QSystem][save]`。

### 27.13.3 文件
- `Save\QualityBlobCodec.cs`（新，213 行）：`Magic` / `TrailerOverhead`(12) / `Append` / `TryRead`（含 `reason` 版）。只依赖 `System`/`System.IO`/`System.Text`，不引用 BepInEx/Unity，离线测试直接链接编译。`Append` 写完不 Flush/Dispose：writer 与 FileStream 归原版 `SaveCurrentGame` 所有，由它回填 offset 6 的 fileSize。
- `Save\QualitySave.cs`（重写，718 行）：`SaveDirectory` → `LegacySaveDirectory`、`GetPath` → `GetLegacyPath`（只用于迁移读取与清理）；新增 `SaveEmbedded` / `ReadEmbedded` / `LoadLegacy` / `DeleteLegacy`（含 `RemoveLegacyDirectoryIfEmpty`：删完最后一个侧文件后顺手清掉空的 `saves\` 目录）/ `ResetCycleFlags` / `EmbeddedLoaded` / `LastEmbeddedWriteOk`；`BuildDocument` / `ApplyDocument` 把 JSON 生成与"校验+应用"从存档 IO 里拆出来。
- `Patches\P1_SaveHooks.cs`（133 行）：新增 `GameData_Export_EmbedQuality`（`GameData.Export(BinaryWriter)` Postfix）、`GameData_Import_ReadQuality`（`GameData.Import(BinaryReader)` Postfix）；`SaveCurrentGame` Postfix 改为"仅当本次写入尾块成功才删旧 json"；`LoadCurrentGame` Postfix 改为"有尾块 ⇒ 清理多余 json／无尾块 ⇒ 一次性 legacy 迁移"；`GameData.NewGame` 清空补丁不变。全部沿用"类型+方法名+参数类型数组"特性写法。
- `tests\OfflineTests\Checks.SaveBlob.cs`（新，282 行）+ `OfflineTests.csproj`（链接 codec、登记文件）+ `Program.cs`（5 条新 `Check`）。
- `DSPQualitySystem.csproj`、`Plugin.cs`：版本 0.11.11。
- 文档：`README.md`（版本行、卸载说明、存档小节、迁移限制、日志关键字）、`TESTLOG.md`、本文件。

### 27.13.4 构建 / 测试
- 先删 `tests\OfflineTests\bin|obj` 再跑：`BUILD OK`，**0 警告 0 错误**；离线 **`ALL PASS (38 checks)`**（33 旧 + 5 新）。
- DLL：`bin\Release\DSPQualitySystem.dll` = **162,304 B**，SHA256 **`1A00BEE3683951362653736CB43C541E5EB59C4C20A5BA7238FD6DC1EB403547`**，AssemblyVersion **0.11.11.0**（v0.11.10 = 157,184 B / `08EE21F7…`）。
- `patchedMethods`：新增 2 个补丁（`GameData.Export`、`GameData.Import`）⇒ 启动打印 **15 → 17**，运行时最终 **22 → 24**。
- 新离线检查：codec 往返（长度 1/7/8/12/1000/65536，前缀字节不变）、拒绝（空流/截断/坏长度/坏 magic，逐条核对 reason）、原版前缀共存（`VFSAVE`+long+200 KB payload）、双尾块只有最后一个在 EOF（读完 `Position == Length`）、参数校验（null/空 payload 抛 `ArgumentException`）。

### 27.13.5 存档迁移
- 升级后第一次读旧档：`[QSystem][save] legacy sidecar used for '<槽名>' (<path>): migrated in memory, this .dsv gets its own trailer on the next save`；该次读档**不删** json。
- 该档下次保存：`[QSystem][save] embedded quality trailer written ('<gameName>'): planets=… bytes=…` 之后紧跟 `[QSystem][save] removed legacy sidecar <path>`；删掉最后一个侧文件后 `BepInEx\config\DSPQualitySystem\saves\` 若已空则整个目录一并删除（`RemoveLegacyDirectoryIfEmpty`，失败/非空都静默忽略）。
- 自动存档：`_autosave*` 槽若自身 json 不存在，额外回退尝试 `_autosave_tmp.json`（历史轮转错位的补救），候选路径都打进日志。
- 想立刻完成迁移：读档后手动保存一次即可；跨机器拷贝存档只需带 `.dsv` 一个文件。

### 27.13.6 实机核对点
①保存后 `BepInEx\config\DSPQualitySystem\saves\` 不再出现新 json（已存在的会被删掉）；②同一档退出重进，品质点数保留，日志出现 `embedded quality trailer used`；③把该 `.dsv` 复制一份、临时移走插件 DLL，原版能正常读档（证明尾部字节被忽略）；④自动存档轮转后 4 个 `_autosave_*.dsv` 各自带自己的品质数据、互不串档；⑤读 v0.11.10 的旧档 → 点数保留且日志出现 legacy 迁移行 → 手动保存一次 → json 消失、再读档走尾块路径。

### 27.13.7 版本链
- v0.11.10：光环观感修正（157,184 B，SHA `08EE21F7…`）。
- **v0.11.11：品质数据内嵌 .dsv（本版）**。

## 27.14 v0.12.0：task-16「A 类五件套」（建筑品质 → 研究/射速/发电/发射/喷涂）

### 27.14.1 目标
把「建筑品质点数」从制造/采集/充电（P3）扩展到五类设施，全部由**本栋建筑**的点数驱动，共用 v0.11.2 的分段曲线（`QualityMath.Curves.CurveBonusPercent`：0/2.5/5/7.5/10/15/20/30/40/60/100 %，10 点 = 2.00×）：

| # | 功能 | 作用对象 | 口径 |
|---|------|----------|------|
| A1 | 研究速度 | 研究站 `LabComponent` | 研究吞吐 × 曲线倍数 |
| A2 | 射速 | 防御塔 `TurretComponent`（高斯/激光/加农/等离子/导弹/近程等离子 6 种开火逻辑） | 开火间隔 × 曲线倍数 |
| A3 | 发电功率 | 发电机 `PowerGeneratorComponent`（风/光/地热/火力/射线接收站） | 每 tick 容量 × 曲线倍数 |
| A4 | 发射速度 | 弹射器 `EjectorComponent` + 发射井 `SiloComponent` | 发射计时增量 × 曲线倍数 |
| A5 | 喷涂概率 +1 | 喷涂机 `SpraycoaterComponent` | 曲线值当概率（0 点 0% … 10 点 100%），命中则被喷物品 +1 点（上限 10） |

### 27.14.2 实现口径（全部瞬态，不写存档）
统一原则：**只放大每 tick 由原版重算的瞬时参数**，绝不写回存档字段；每个功能一个独立 bool 开关，关闭时**连补丁都不注册**（原版 IL 逐字节等价，而不是乘 1 / 概率 0 的等价）。

1. **A1 研究站** — 前缀 `LabComponent.InternalUpdateResearch(float power, float research_speed, int[] consumeRegister, ref TechState ts, ref int techHashedThisFrame, ref long uMatrixPoint, ref long hashRegister)`（asm_full.cs:56104），`ref float power *= factor`。
   - 原版在 :56192 `num3 = (int)(power * 10000f * research_speed + 0.5f)`、:56230-56232 `extraHashBytes += (int)(power * extraSpeed * research_speed + 0.5f)` 里用 `power` 作为"研究吞吐"；改它**不影响电网耗电**（电网侧 `networkServes` 已在外层算好），也不改 `research_speed`。前缀在 `power < 0.1f` 时直接返回，与原版 :56106 的断电分支一致。
2. **A2 防御塔** — 6 个 `TurretComponent.Shoot_*`（asm_full.cs:121196 Gauss / :121327 Laser / :121428 Cannon / :121551 Plasma / :121729 Missile / :121913 LocalPlasma）统一前缀 `ref float power *= factor`。原版 :121207 `turretROF = (int)((float)turretROF * power * (supernova_strength + 1f) + 0.5f)` 把 `power` 当射速/充能倍率，所以改它只影响开火间隔，不动伤害/射程/弹药扣减逻辑。Plasma/Missile 的参数表多一个 `EnemyData[] spaceEnemyPool`，反射用精确参数类型数组区分。
3. **A3 发电机** — 5 个容量 getter 后置：`EnergyCap_Fuel()`(:60100)、`EnergyCap_Wind(float)`(:60171)、`EnergyCap_PV(float,float,float,float)`(:60178)、`EnergyCap_Gamma(float)`(:60237)、`EnergyCap_GTH()`(:60251)，`ref long __result`：`raised = (long)(__result * factor)`，同时写 `__instance.capacityCurrentTick = raised`（原版这几个方法自己就写这个**瞬态**字段，从不序列化）。`PowerSystem` 在 asm_full.cs:107087-107118 里把返回值累加进网络容量，所以抬返回值 = 真·提高发电。`EnergyCap_Gamma_Req` 是需求侧计算，不在本补丁列表内。
   - 文档化双收益：火力发电机按比例多烧燃料、射线接收站按比例多产（用户已批准）。
4. **A4 弹射器/发射井** — `EjectorComponent.InternalUpdate(float power, long tick, DysonSwarm, AstroData[], AnimData[], int[])`（asm_full.cs:37815）与 `SiloComponent.InternalUpdate(float power, DysonSphere, AnimData[], int[])`（:61221）统一前缀 `ref float power *= factor`；原版 :37864 / :61242 都是 `num2 = (int)(power * 10000f * (1f + accTableMilli[incLevel]) + 0.1f)` 然后 `time -= num2`，所以 `power` 就是发射速率。两个组件都有自己的 `planetId` 字段（:37575 / :61053），优先用它，取不到再退回 `QualityEnv` 的星球兜底。
5. **A5 喷涂机** — `SpraycoaterComponent.InternalUpdate(CargoTraffic, AnimData[], int[])`（asm_full.cs:73562）**Transpiler**：原版 :73648 `cargoPool[cargoId2].inc = (byte)(num5 * incAbility)`，在 `mul` 之后、`conv.u1` 之前插入 `ldarg.0; ldfld SpraycoaterComponent::entityId; call QualityData.SprayBonusPoints(int,int)`。匹配**不锁死操作数顺序**：先找所有"前一条是 `mul`"的 `conv.u1`，并回溯 5 条指令确认其中载入了 `SpraycoaterComponent.incAbility`；若这一轮为 0 个站点，则退回"全方法唯一的 `mul; conv.u1`"（:73644 的比较用 `cgt`、:73689 的消耗计算没有窄化 cast，所以唯一的 byte 窄化就是写入点）。插入不移动/不删除任何原指令，分支目标全部有效；站点数打进日志 `[QSystem][sprayer] transpiler matched the byte-write site at instruction #N`，站点≠1 时该功能回原版结果并只打一条 warn。
   - 纯函数 `QualityMath.SprayChancePercent(points)` = 曲线百分比 clamp 0–100；`QualityMath.SprayBonus(sprayedInc, points, roll, Cargo.kIncLevelMax)`：`sprayedInc<=0` / 已达上限 / 概率 0 / roll 为空 / roll 未命中 一律原值返回，命中才 +1。**只加不扣**，异常/未知一律回原版值。

### 27.14.3 文件
- `Patches\P5_Common.cs`（128 行，新）：五个 P5 补丁的共用注入工具（`Info`/`WarnOnce(key,detail)`/`Enabled`/`DebugOn`/`Debug`/`Effective`/`ResolvePlanetId`；consts `DebugLogLimit=20`、`EffectiveLogEvery=200`）。
- `QualityMath.ExtraBonuses.cs`（72 行，新）：纯函数 `FeatureFactorFrom(points, enabled, curve, percentPerPoint, maxBonusPercent)`、`SprayChancePercent(points)`、`SprayBonus(sprayedInc, points, rollPercent, maxInc)`。
- `QualityData.Extra.cs`（101 行，新）：`LabSpeedFactor` / `TurretFireFactor` / `PowerGenFactor` / `LauncherSpeedFactor`（→ `GetBuildingInc` + `FeatureFactorFrom`，异常回 1f）与 `SprayBonusPoints(sprayedInc, entityId)`（→ sprayer 概率 +1，并调用 `P5_Sprayer.NoteSpray` 记统计）。
- `Patches\P5_LabSpeed.cs`（135 行，新）、`Patches\P5_TurretFireRate.cs`（155 行，新）、`Patches\P5_PowerGen.cs`（172 行，新）、`Patches\P5_Launcher.cs`（227 行，新）、`Patches\P5_Sprayer.cs`（231 行，新）：每个都 `Apply(Harmony)` + 硬开关 + 前缀/后置/Transpiler + `Describe()`；异常全部 catch 后 `P5Common.WarnOnce`。
- `QualityConfig.cs`（361 → 394 行）：新增 5 个 `ConfigEntry<bool>`（全部默认 `true`，绑定在 `MaxChargeBonus` 之后）：`labSpeedBonus`、`turretFireRateBonus`、`powerGenBonus`、`launcherSpeedBonus`、`sprayerIncBonus`。
- `Plugin.cs`（281 → 300 行）：VERSION `0.12.0`；在 `P4_ConsumePriority` 之后按 A1→A5 顺序 `Apply(harmony)` + `Describe()` 日志（`[QSystem][lab]` / `[turret]` / `[powergen]` / `[launcher]` / `[sprayer]`）；`LogConfig` 增加 5 个键。
- `Diag\StartupAudit.cs`（308 → 385 行）：新增 15 条 `CheckMethod`（研究站 1、防御塔 6、发电机 5、弹射器 1、发射井 1、喷涂机 1）与 5 条 `Describe()`，新增私有辅助 `CheckTurretShoot(log, name, withEnemy)` / `CheckEnergyCap(log, name, Type[])`。
- `DSPQualitySystem.csproj`、`Plugin.cs`：版本 0.12.0（AssemblyVersion 0.12.0.0）。
- 离线：`tests\OfflineTests\Checks.P5Bonus.cs`（186 行，新，5 条检查）+ `OfflineTests.csproj`（登记新文件 & 链接 `QualityMath.ExtraBonuses.cs`）+ `Program.cs`（新增 5 条 `Check`）。
- 文档：`README.md`（103 行：版本行/DLL SHA、里程碑追加 task-16、5 个配置键行、A 类五件套玩法与边界、日志关键字）、`TESTLOG.md`、本文件。
- **`.hold` 消化情况**：`QualityData.Extra.cs.hold`、`QualityMath.ExtraBonuses.cs.hold`、`Patches\P5_Common.cs.hold`、`P5_LabSpeed.cs.hold`、`P5_PowerGen.cs.hold`、`P5_TurretFireRate.cs.hold` → 转正参与编译；`Patches\P4_BlueprintReceipt.cs.hold`、`P4_BlueprintSource.cs.hold` → **删除**（与已编译的 `P4_ConsumePriority.cs` / `P4_ConsumePriority.Sources.cs` 重复：`TakeBestGridPrefix` 前缀、`_inPaste` 门限、`CapturePasteConsumed` 收据、`BlueprintPasteReceipt` 语义都已在后者中实现，v0.11.4/5 起就是这套代码在跑）。目录内 `.hold` 归零。
- 弹射器/发射井与喷涂机的补丁源码原本不在 `.hold` 内，本版按设计文档新增 `P5_Launcher.cs` / `P5_Sprayer.cs`。

### 27.14.4 构建 / 测试
- 先删 `tests\OfflineTests\bin|obj` 再跑：`BUILD OK`，**0 警告 0 错误**；离线 **`ALL PASS (43 checks)`**（38 旧 + 5 新）。
- 新离线检查：`P5 A-class factor`（曲线表逐点 2.5/5/…/100% → 1.025…2.00×、关闭/负点数/0 点回 1f、linear 模式回归与 maxBonus 封顶、单调且不越界）、`P5 A-class feature matrix`（四个开关共用同一曲线、关闭时逐点恒 1f）、`P5 sprayer chance`（0…10 点 0/2.5/10/15/30/40/60/100%、越界 clamp、单调）、`P5 sprayer bonus`（+1 恰好一次、10 点必中、0 点不中、达到上限不再加、roll 收到的概率值正确、只加不扣、null/负值全 no-op）、`P5 sprayer events`（确定性 15% 序列 100 次恰好 15 次命中、整叠超过上限不动、统计无状态）。
- DLL：`bin\Release\DSPQualitySystem.dll` = **187,392 B**，SHA256 **`E7355998A9D1F6C9259F1CB956940C09C6BF0360126DA9D9A9D519487976A4B3`**，AssemblyVersion **0.12.0.0**（v0.11.11 = 162,304 B / `1A00BEE3…`）。
- `patchedMethods`：本版新增 **15 个独立手动补丁**（研究站 1、防御塔 6、发电机 5、弹射器 1、发射井 1、喷涂机 1）⇒ 启动打印仍 **17**（PatchAll 特性数不变），运行时最终 **24 → 39**。
- 启动日志预期（每项一条；关闭该功能时打印 `NOT ARMED` 且不注册补丁）：
  - `[QSystem][lab] lab speed armed (research speed *= quality curve; hits=… viaEnv=… fallback=…)`
  - `[QSystem][turret] turret fire rate armed (power *= quality curve; methods=6/6, hits=…)`
  - `[QSystem][powergen] generator output armed (… methods=5/5 …)`
  - `[QSystem][launcher] launcher speed armed (ejector=on silo=on; … hits ejector=… silo=…)`
  - `[QSystem][sprayer] sprayer inc bonus armed (site=1, chance from quality curve 0..100%, sprays=…, +1 hits=…)`
  - `[Audit]` 段新增 15 行 `-> OK`（含 `LabComponent.InternalUpdateResearch(7 params)`、六种 `TurretComponent.Shoot_*`、五个 `PowerGeneratorComponent.EnergyCap_*`、`EjectorComponent.InternalUpdate`、`SiloComponent.InternalUpdate`、`SpraycoaterComponent.InternalUpdate`）。

### 27.14.5 口径备注（易误解点）
- A1–A4 的倍率**不叠加**其它加成：都是对原版已经算好的瞬时 `power`/容量乘一次曲线倍数，与电网供电比例、增产剂加速、增幅基站等正交（先原版算，再乘品质）。
- 火力发电机的燃料消耗/射线接收站的产出会随功率同步放大（同一个 `power`/容量进账路径），这是**双收益**、不是 bug；若嫌强，关掉 `powerGenBonus` 即可（其它四项不受影响）。
- 喷涂机 A5 的概率来自**喷涂机建筑**的点数，不是被喷物品的点数；判定在"写入 inc"这一步，`num5` 是本次被喷件数、`num5*incAbility` 是该叠总点数，所以整叠超过 10 点时原版会拒绝覆盖，本补丁同样不会把它抬过 10。
- 全部加成只在运行时生效，**存档尾块 schema 不变**（v0.11.11 的 `[len][payload][magic]` 帧未动）。

### 27.14.6 实机核对点（Lead 执行）
1. **开关等价性**：5 个键逐个设 `false` 重启，`[Audit]` 对应行变 `NOT ARMED`、`patchedMethods` 运行时数相应减少（全关回到 24），游戏行为与原版一致。
2. **A1 研究站**：放一栋 10 点研究站，同一科技、同一电量条件下对比 0 点研究站，研究队列进度明显更快（期望 2.00×）；日志 `[QSystem][lab] effective=…` 出现。
3. **A2 防御塔**：10 点防御塔对比 0 点，弹药消耗速度/弹幕密度明显更快；6 种塔各测一种；`[QSystem][turret] effective=…`。
4. **A3 发电机**：10 点风力/光伏/地热/火电/射线接收站各一栋，电网容量相对 0 点提升（10 点约 2.00×）；火力发电机燃料消耗同步变快（预期双收益）；若条件允许核对 `EnergyCap_Gamma` 的射线接收站产出。
5. **A4 弹射器/发射井**：10 点弹射器帆发射间隔 vs 0 点；发射井火箭发射间隔与 `chargeSpend/coldSpend` 消耗节奏变快；`[QSystem][launcher] effective=…`。
6. **A5 喷涂机**：把 10 点喷涂机放在 0 点喷涂机旁，同一产线对比被喷物品点数（10 点喷涂机应 100% +1 点，直到物品到 10 点上限）；5 点喷涂机应约 15% 命中；日志 `[QSystem][sprayer] +1 quality point: inc …` 与 `+1 hits=` 增长。
7. **异常回退**：故意留一个未命中的反射（例如未来游戏版本改名）时，日志应只有 `[QSystem][p5] … not armed, vanilla: …`，游戏不崩、对应功能保持原版。
8. **存档不变**：本版前后保存的 `.dsv` 尾块帧仍可互读（`[QSystem][save] embedded quality trailer used …`），无 schema 变化。

### 27.14.7 版本链
- v0.11.10：光环观感修正（157,184 B，SHA `08EE21F7…`）。
- v0.11.11：品质数据内嵌 .dsv（162,304 B，SHA `1A00BEE3…`，38 checks）。
- v0.12.0：task-16 A 类五件套（187,392 B，SHA `E7355998…`，43 checks，运行时 patched 39）。

## 27.15 v0.12.1：光环默认缩小 70% + 汇编器越界加固 + 多 Mod 尾块共存

### 27.15.1 三件事的由来
1. **用户反馈**（原话：「金圈太丑了，给我缩小70%！」）：v0.11.10 的 10 点金色光环在实机上明显大于建筑本体，观感过重。按字面「缩小 70%」= 半径乘 0.3。
2. **实机日志缺陷**：`[QSystem][patch] assembler.prefix failed: System.IndexOutOfRangeException` + 栈 `QualityData.SetWasReplicating ← Assembler_InternalUpdate_Quality.Prefix`。异常被 `WarnOnce` 吞掉不崩游戏，但该 tick 的装配机簿记丢失，且属于「数据越界」类隐患。
3. **生态冲突**：`DSP-MolecularHub`（本工作区另一个 MOD）也在 `GameData.Export/Import` 上追加自己的 `.dsv` 尾块（magic `MHUBTRL1`，布局 `[payload][int32 len][magic]`）。当它在本 MOD 之后写时（Postfix 执行顺序不保证），本 MOD 的帧被埋进文件中部，v0.11.11 的「只看 EOF」读取就判 `bad length` 并丢弃品质数据——这正是「存档不再被分割」目标的反面：数据还在文件里，却读不回来。

### 27.15.2 改动 1：`tenPointHaloSize` 默认 1.0 → 0.3
- `QualityConfig.TenPointHaloSize` 绑定值改为 `0.3f`，描述写明「比 v0.11.10 的 1.0 小 70%（用户反馈环太大）」；`TenPointHaloSizeClamped` 的 fallback 同步 `0.3f`（clamp 区间仍是 0.3–3.0，想恢复旧观感写 `1.0`）。
- 半径公式未动：`QualityMath.HaloRadius(selectX, selectZ, scale)` 仍是 `clamp(max(selectSize.x, selectSize.z) * 0.42, 1.0, 3.2) * scale`，所以是纯倍率变化，离线参数化测试无需改。
- 实机：`[QSystem][halo] armed (max=64 lift=1.2 size=0.3 shader=…)`，`marked=64 visible=64`；截图对比 `logs/shot-20261027_003610.png`（1.0×，环直径约为建筑两倍）→ `logs/shot-20261027_010628.png`（0.3×，小圆环与建筑占地相当）。

### 27.15.3 改动 2：`MaxEntityId` 与 setter 入参护栏
- 机制：`PlanetQuality.EnsureCapacity(capacity)` 首行 `if (capacity <= buildingInc.Length) return;`。若 `entityId` 是哨兵值（对象池/预热/拆除帧里出现 `int.MaxValue` 之类），`entityId + 1` 溢出为负数，容量判定认为「够大」直接返回，随后 `quality.wasReplicating[entityId] = value` 越界。
- 修法（纵深防御，两层都做）：
  1. `QualityData.MaxEntityId = 1 << 20`（1,048,576）常量；7 个公开 setter 全部拒收 `entityId < 0 || entityId > MaxEntityId`：`SetBuildingInc`、`SetInputLevel`、`AddPool`、`SetPrevProduced`、`SetPendingLevel`、`SetWasReplicating`、`SetPool`（`AddPool` 另拒 `productCount <= 0`，`SetPrevProduced` 另拒 `slot` 越界）。
  2. 热路径补丁在调 setter 前就返回：`P0_Assembler` 的 Prefix/Postfix 各加 `entityId > QualityData.MaxEntityId` 判定，Prefix 另打一次 `[QSystem] assembler ignored an out-of-range entityId=… (planet=…, max=1048576)`（新重载 `QualityEnv.WarnOnce(string key, string message)`，每 key 每会话一条）。
- 行为不变：正常 `entityId`（< 1M）路径完全一致，没有任何性能敏感的额外分配。
- 实机：注入 255 栋 10 点建筑的会话里 `assembler.prefix failed` 出现 **0 次**，`[QSystem][speed] assembler effective=…` 正常。

### 27.15.4 改动 3：尾块读取改为「尾部倒序扫描」
- `QualityBlobCodec.TryRead`：先算 `remaining = BaseStream.Length - Position`，读入尾部窗口（最多 `MaxSearchBytes = 4 MB`，实际 `min(remaining, 4 MB)`），在窗口内**从末尾倒序**找 magic；找到 magic 后向**前**逐字节找自洽长度字段（`len > 0 && frameStart + 4 + len == magicStart`），命中即返回 payload。
- 语义变化：帧后跟任意字节（他 mod 的帧、垃圾）**不再视为损坏**，只要求「窗口内存在一个自洽帧」，且**取最靠后（最新）的那一个**；完全找不到才返回 false，`reason` 统一为 `no trailer found`（旧 `bad length` / `bad magic` 表述废弃）。
- 写入侧不变：仍是在自己的 `GameData.Export` Postfix 里往当前 EOF 追加 `[len][payload][magic]`，并靠 GameSave 回填 fileSize；如果另一个 mod 之后又追加，本 MOD 的数据依然可被倒序扫描找回。
- 边界：4 MB 窗口假设「他 mod 的尾块 + 垃圾合计小于 4 MB」；超出会记 `no trailer found`（不会误读）。seek 不可用时仍返回 `stream cannot seek`。
- 新增离线检查 `save blob codec shared trailer (another mod appended after us)`：构造 `vanilla + 本MOD帧 + MHUB帧 + 3 字节垃圾`，断言仍能读回本 MOD payload；另有「只有外来帧 ⇒ no trailer found」用例。测试期望同步更新：截断/坏长度/坏 magic 一律 `no trailer found`，「帧后有垃圾」由「期望拒绝」翻转为「期望能读」。

### 27.15.5 文件
- `Save\QualityBlobCodec.cs`：`TryRead` 重写 + 新增 `FindFrame` / `SameBytes`，常量 `MaxSearchBytes = 4 << 20`，类注释补充多 Mod 共存说明。
- `QualityData.cs`：`MaxEntityId` 常量 + 7 个 setter 护栏。
- `QualityEnv.cs`：`WarnOnce(string key, string message)` 重载。
- `Patches\P0_Assembler.cs`：Prefix/Postfix 越界提前返回 + 一次性告警。
- `QualityConfig.cs`：`tenPointHaloSize` 默认 0.3 + 描述；`TenPointHaloSizeClamped` fallback 0.3。
- `DSPQualitySystem.csproj` / `Plugin.cs`：版本 0.12.1（AssemblyVersion 0.12.1.0）。
- `tests\OfflineTests\Checks.SaveBlob.cs`、`Program.cs`：共享尾块用例 + 44 条注册。
- 文档：`README.md`（版本行/光环默认值/存档共存小节/汇编器防护条）、`TESTLOG.md`、本文件。
- 夹具（不随包发布，工作区 `_live\`）：`craft-halo-test-save.ps1`（造 `vanilla body + 本MOD帧 + MHUB帧 + 垃圾` 的测试档）、`inject-all-bonus.ps1`（把指定星球实体全写成 N 点）、`check-dsv.ps1`（校验 VFSAVE 头/offset6/fileSize/尾块帧）。

### 27.15.6 构建 / 测试
- `BUILD OK`，0 警告 0 错误；`bin\Release\DSPQualitySystem.dll` = **188,416 B**，SHA256 **`01FF6FCECF9BC45B8A56C449220E565206F5A88D712632DF28D16B9F241F17E8`**，AssemblyVersion 0.12.1.0。
- 离线：**`ALL PASS (44 checks)`**（43 旧 + 1 共享尾块新检查；改语义的拒绝用例并入原 5 条）。
- `patchedMethods` 仍为启动 17 / 运行时 39（本版没有新增补丁方法，只给已有补丁加入参判定）。

### 27.15.7 实机核对点（Lead 执行，2026-10-27 00:44–01:07）
1. 光环默认值：启动日志 `size=0.3`，画面环径缩小到约 30%（截图前后对比）。
2. 汇编器越界：改造过的 255 点档会话中 `assembler.prefix failed` 计数 0。
3. 尾块共存：把测试档造成「本 MOD 帧 + MHUB 帧 + 垃圾」→ 读档日志 `embedded quality trailer used ('沙盒'): planets=1 entries=255 bytes=4313`（旧版必失败）。附带证据：一次 gameName 写成乱码的版本被 guard 拒绝（`gameName mismatch: file='娌欑洅' expected='沙盒'`），反而证明「埋在中间的帧确实被扫描找到了」。
4. 回归：读 v0.11.11/v0.12.0 正常档仍是 `embedded quality trailer used (…) planets=1 entries=5/6`；退出写档 `embedded quality trailer written` 正常。
5. 清理：测试结束后把 `_lastexit_.dsv` 还原成注入前的原档（`_live-backup\v0.12.1-lastexit-orig.dsv.bak`）。

### 27.15.8 版本链
- v0.11.10：光环观感修正（157,184 B，SHA `08EE21F7…`）。
- v0.11.11：品质数据内嵌 .dsv（162,304 B，SHA `1A00BEE3…`，38 checks）。
- v0.12.0：task-16 A 类五件套（187,392 B，SHA `E7355998…`，43 checks，运行时 patched 39）。
- **v0.12.1：光环默认 0.3 + 汇编器越界加固 + 多 Mod 尾块共存（本版）**（188,416 B，SHA `01FF6FCE…`，44 checks）。

## 27.16 v0.12.2：10 点建筑标记做成完整 cfg 选项（玩家自选环/框/颜色/动效）

### 27.16.1 由来
- 用户指令（m00924）：「验证通过，继续开发，另外把这个光环选项做成cfg配置文件，让玩家可以自行选择。」
- 之前的状态：光环本身早就在 cfg 里（`tenPointHalo` 开关、`tenPointHaloLift`、`tenPointHaloSize`、`tenPointHaloMax`），但玩家能改的只有「多大、几个」。想换成描边得同时懂 `tenPointOutline`，想换颜色/关掉呼吸和自转根本做不到——只能改代码。
- 本版目标：把**标记的种类、颜色、动效**也交给 cfg，同时不破坏任何旧 cfg（旧键语义不变，`tenPointMarkerStyle` 默认 `auto` 就是把旧行为原样保留）。

### 27.16.2 新增 4 个配置键
| 键 | 默认 | 取值 | 作用 |
| --- | --- | --- | --- |
| `tenPointMarkerStyle` | `auto` | `auto`/`halo`/`outline`/`both`/`none` | 标记种类。`auto` 沿用旧的两个 bool；其余强制指定（覆盖 bool） |
| `tenPointHaloColor` | `FFD24A` | `RRGGBB` 或 `RRGGBBAA` | 光环颜色，`#` 可带可省、大小写不敏感；非法值回退默认金 |
| `tenPointHaloPulse` | `true` | true/false | alpha 呼吸开关；`false` 固定 `0.85` |
| `tenPointHaloSpin` | `1` | 0–5 | 自转速度倍率（基础约 20°/s），`0` = 静止 |

### 27.16.3 实现
- `QualityMath.Halo.cs` 新增纯函数（可离线测）：
  - `TryParseHexColor(string, out float r, out float g, out float b, out float a)`：6/8 位十六进制，失败返回 false 并回默认金 `(1, 0.8235294, 0.2901961)`（= `FFD24A`，v0.11.10 的原金色；注意不是 `FFC83C`）。
  - `ResolveMarkerStyle(string style, bool haloBool, bool outlineBool, out bool haloOn, out bool outlineOn)`：`auto`/null/空/未知 → 两个 bool 原样；`halo`|`ring`|`halo-only` → 只要环；`outline`|`frame`|`outline-only` → 只要框；`both`|`all` → 都要；`none`|`off`|`hidden` → 全关。
  - `HaloPulseAlpha(time, pulseEnabled)`：关掉时返回 `HaloSteadyAlpha = 0.85`。
  - `HaloSpinDegrees(time, spinScale)`：`spinScale <= 0` 或 NaN → `0`。
- `QualityConfig.cs`：4 个 `ConfigEntry` + 计算属性 `MarkerStyleName`/`HaloArmed`/`OutlineArmed`/`HaloColorHex`/`HaloPulseEnabled`/`HaloSpinClamped`（clamp 0–5）。
- `Patches/P5_TenPointHalo.cs`：`HaloColor` 走 `TryParseHexColor`；`Tick()` 判 `!QualityConfig.HaloArmed`；旋转/呼吸走新纯函数；安装日志追加 `style=… color=… pulse=… spin=…`；`Describe()`/`LogDisabledOnce()` 同步。
- `Patches/P5_TenPointOutline.cs`：开关判定改 `!QualityConfig.OutlineArmed`，于是 `tenPointMarkerStyle=both` 能在 `tenPointOutline=false` 时也把描边打开。
- `Plugin.cs`：`VERSION = "0.12.2"`，启动 `LogConfig` 追加这 4 个键。

### 27.16.4 构建 / 测试
- `BUILD OK`，0 警告 0 错误；`bin\Release\DSPQualitySystem.dll` = **193,024 B**，SHA256 **`61EB8F2385AB890B9D4402DEF3642B3BCF74358D9986A69CC1A033C135A5E245`**，AssemblyVersion 0.12.2.0。
- 离线：**`ALL PASS (45 checks)`**（44 旧 + 1 新 `P6 halo config (hex colour / marker style / pulse / spin)`）。
- 踩坑：新检查首版把默认金写成 `FFC83C`，与运行时代码里的 `FFD24A` 不一致 → `null falls back to gold green expected 0.8235294 got 0.78431374` FAIL；统一成 `FFD24A` 后全绿。

### 27.16.5 实机核对点（Lead 执行，2026-10-28 00:14–01:15）
1. 启动：`[QSystem] === DSP Quality System v0.12.2 ===`、`patchedMethods=17`。
2. 新键读入：`cfg … tenPointMarkerStyle=auto tenPointHaloColor=FFD24A tenPointHaloPulse=True tenPointHaloSpin=1`。
3. 光环行：`[QSystem][halo] armed (max=64 lift=1.2 size=0.3 style=auto color=FFD24A pulse=True spin=1.0 shader=…)`。
4. style 选择器生效：把 cfg 改成 `tenPointMarkerStyle = both`（此时 `tenPointOutline` 仍是 `false`）重启 → `[halo] armed (… style=both …)` + `[outline] armed (max=64 width=2 …)`，说明 style 真的覆盖了旧 bool；改回 `auto` 后恢复旧行为。
5. 未逐一实机验证的颜色/呼吸/自转：由 45 条离线检查覆盖（解析、回退、clamp、纯函数返回值）。

### 27.16.6 版本链
- v0.11.11：品质数据内嵌 .dsv（162,304 B，SHA `1A00BEE3…`，38 checks）。
- v0.12.0：task-16 A 类五件套（187,392 B，SHA `E7355998…`，43 checks）。
- v0.12.1：光环默认 0.3 + 汇编器越界加固 + 多 Mod 尾块共存（188,416 B，SHA `01FF6FCE…`，44 checks）。
- **v0.12.2：标记做成完整 cfg 选项（本版）**（193,024 B，SHA `61EB8F23…`，45 checks）。

## 27.17 v0.13.0：task-17 储物仓「品质格数」+ task-18 四向分流器「品质优先」

### 27.17.1 由来
- 交接任务（HANDOFF v3.3 §8 第 2/3 条，原文）：
  - 「C-1 大小仓库品质质变格数（先设计；重点验证『卸载 MOD 后带扩容仓库的存档仍可读』）」
  - 「C-2 四向分流器『品质优先』（UI 筛选器下方加 checkbox，开关存 MOD 侧表）」
- 用户口径：每个功能独立 cfg 开关、默认保守（能默认关就默认关）、关闭时补丁不注册 = 原版逐字节等价、锚点失败记录并回退原版、DEBUG 日志由 `debugProbes` 门控。
- 本版两个功能一起发 **0.13.0**（两个新 cfg 开关默认 `false`，默认行为与 v0.12.2 完全一致）。

### 27.17.2 C-1 设计：储物仓「品质质变格数」
- 定义：**小/大储物仓在建筑品质点数提升时，可用格数由点数决定**。
  - `extra = floor(points × storageQualityGridsPerPoint)`，再被两个上限夹：`storageQualityGridsMax`（每容器硬上限）与 `baseGrids`（原版格数，即最多 +100%）。
  - 默认 `perPoint = 0.5`、`max = 20`：10 点小仓 30 格 → 35 格、大仓 100 格 → 105 格；0 点/未装品质数据 = 完全原版。
  - 纯函数在 `QualityMath.Storage.cs`：`StorageExtraGrids` / `StorageTotalGrids` / `StorageGridsOutOfSync`；NaN/Inf/负值/0 一律回 0（离线检查覆盖）。
- 适用范围（与 v0.11.3 容器隔离同口径，默认**不碰**物流塔内部与机甲格）：
  - 只作用于 `StorageComponent`（`EStorageType.Default`，非玩家背包）：小/大储物仓、战场分析基站等通用储物容器。
  - **不**作用于：物流塔内部（`StationStore` 一格一种物品、整格点数）、机甲燃料/弹药/炸弹/战斗机格、过滤容器（`EStorageType.Filtered`）、储液罐（`TankComponent`，不是 `StorageComponent`）。判定直接用 `QualityMath.ShouldIsolateStorage(!isPlayerInventory, (int)type, size, true)`。
- 硬约束实现（**不改存档里的仓库结构字段**）：
  - 运行时用原版 `StorageComponent.SetSize(total)` 扩 `size`/`grids`（原版所有读写都 `for (i < size)` 直接索引，扩完无需再补任何逻辑）。
  - 导出时在 `GameData.Export(BinaryWriter)` 的 **Prefix** 里把每个已扩容容器的 `storage.size` 字段直接改回原版 base（**不调 `SetSize`**，否则数组截断会丢溢出格内容），Postfix/Finalizer 恢复。于是 `.dsv` 里 `FactoryStorage.Export` 外层写的 size 与 `StorageComponent.Export` 内层写的 size 都是原版值 —— 存档结构零改动。
  - 溢出格物品（base 之后的格子里有货时）：导出前先 `CompactIntoBase` 整格搬进 base 区空闲格（不合并堆、不改 inc 品质）；搬不进去的格子写进本 Mod 既有 `.dsv` 尾块 JSON（原版读档忽略尾部字节）。读档时 `ApplyLoadedStorage` 先恢复到 base 再按点数扩容并写回格子。
  - **代价（已知取舍，写进 README 限制）**：卸载 MOD 后这些溢出物品丢失（它们只存在 MOD 尾块里），但**存档本身仍可被原版正常读取** —— 这正是任务要求的硬约束。
  - `bans`（玩家/战场分析基站的"禁用格数"滑条，语义是尾部 N 格禁用）同步处理：导出前若 `bans > base` 夹到 base，恢复时还原；收缩时先 `SetBans(want)` 再 `SetSize`。
- 品质下降 / 开关关闭：只在溢出区**全空**时才 `SetSize` 回 base；还有货就保留额外格并在 30 帧周期的 `CollapseStep` 里重试，绝不丢物品。
- 线程模型：`BuildingIncChanged` 钩子（`SetBuildingInc` 末尾触发）只做 `ConcurrentQueue.Enqueue`，真正的 `SetSize` 全部在主线程 `Plugin.Update → StorageGrids.Tick()` 里做（dirty 队列 128/帧 + 增量 sweep 2048/帧 + collapse 8/30 帧）；读档后 `RequestFullSweep()`。

### 27.17.3 C-2 设计：四向分流器「品质优先」
- 原版锚点：`CargoTraffic.UpdateSplitter(ref SplitterComponent)`（`asm_full.cs:82854`，串行路径）与 `UpdateSplitterAsync`（`asm_full.cs:83208`，`FactorySplitterGameTick_Parallel` 并行路径，两者是同一份逻辑的重复实现）。
  - 原版按 **input0→input3** 顺序调 `GetCargoPath(beltPool[sp.inputN].segPathId).GetCargoIdAtRear()` 收 4 条输入尾部 cargo，再按 output0→output3 找第一个能收的出口：**谁先被收集，谁先被分配**。
- 实现（不改方法体、不改输出/过滤/优先级语义）：Prefix 里读 4 条输入尾部 cargo 的 `inc`，算出「有货格按 inc 降序、同 inc 稳定、空位沉底」的槽位置换，只把 `sp.input0..3` 置换成对应皮带 id；原版收集循环自然先处理高品质货。纯函数 `QualityMath.Splitter.cs`：`PlanSplitterInputOrder` / `SplitterBestInputSlot` / `SplitterPriorityApplies`。
- 让路规则：原版 `inPriority`（输入优先级预设）或 `prioritySlotPresets > 0`（玩家拖出来的优先级槽位）时**完全跳过**，避免和原版优先级抢语义；同品质保持原序；空/单品质输入 = 不置换（零开销原版路径）。
- UI：**没有做注入式 checkbox**（原因见 27.17.4）；替代方案是「分流器窗口标题后缀 + F3 切换」——`UISplitterWindow._OnOpen` 的 Postfix 在 `titleText.text` 追加 ` [Quality First]`，F3 对**当前打开窗口**的那台分流器翻转开关并立即刷新标题。每台分流器的开关存 MOD 侧表（`PlanetQuality.splitterPriority`，按 planetId + entityId 索引）。

### 27.17.4 为什么没做 checkbox UI（偏离说明）
- `UISplitterWindow`（`asm_full.cs:524070`）里**所有** UI 元素都是 `[UnityEngine.SerializeField]` 的预制体引用（`UIButton`/`Text`/`Image`/`RectTransform`/`RawImage` …），没有任何运行时创建控件的容器或可复用的 sprite 资产。
- 要加真 checkbox 只有两条路：① 新建 `UnityEngine.UI.Toggle` + 手工造背景/勾选 sprite（资产依赖、字体、缩放、本地化全要自己处理）；② 克隆面板里某个已有 toggle 预制体元素（需要知道预制体层级，且本机无法实机验证）。
- 交接说明允许「UI 注入风险过高时先做无 UI 最小版」。本版选最小版并把它做完整：状态可切换、可见（标题后缀）、可持久化、有日志；后续要做真 checkbox 时，落点就是 `UISplitterWindow._OnOpen`（已有 Postfix）里往 `windowTrans` 下 AddComponent，开关读写复用 `QualityData.Get/SetSplitterPriority`，不需要再改分流逻辑。

### 27.17.5 实现清单
- 新增 `QualityMath.Storage.cs`（84 行）：`StorageExtraGrids` / `StorageTotalGrids` / `StorageGridsOutOfSync`（纯函数，无 Unity 依赖）。
- 新增 `QualityMath.Splitter.cs`：`SplitterNoCargo = -1`、`PlanSplitterInputOrder`（稳定降序 + 空位沉底，返回是否需要置换）、`SplitterBestInputSlot`、`SplitterPriorityApplies`。
- 新增 `StorageGrids.cs`：C-1 运行时主体（dirty 队列 / 增量 sweep / collapse / 导出 shave + 恢复 / 尾块快照 / 读档恢复 / `Describe`）。
- 新增 `QualityData.Storage.cs`：侧表访问器（`Get/Set/ClearStorageBase`、`Get/SetSplitterPriority`、`HasAnySplitterPriority`、`HasAnyStorageBase`）+ `BuildingIncChanged`。
- 新增 `Patches/P4_StorageGrids.cs`（185 行）：手动注册 `GameData.Export(BinaryWriter)` 的 Prefix/Postfix/Finalizer（shave/restore；finalizer 绝不掩盖原版存档异常）；cfg 关或 `perPoint/max` 为 0 时只记 NOT ARMED 不注册。
- 新增 `Patches/P5_SplitterPriority.cs`：手动注册 `CargoTraffic.UpdateSplitter` + `UpdateSplitterAsync`（同一个 Prefix）+ `UISplitterWindow._OnOpen`（标题后缀）；F3 热键；scratch 缓冲用 `[ThreadStatic]`（并行路径会多线程进入 Prefix，共享静态数组会被交错打乱）。
- 改 `QualityData.cs`：`PlanetQuality` 新增 `int[] storageBase`（0 = 未知/非容器）与 `byte[] splitterPriority` 两个数组，`EnsureCapacity` 一起扩容、`ClearEntity` 一起清零；`SetBuildingInc` 末尾触发 `BuildingIncChanged`。
- 改 `QualityConfig.cs`：4 个新键 + `StorageGridsEnabled`/`StorageGridsPerPointClamped`(0–2)/`StorageGridsMaxClamped`(0–200)/`SplitterQualityPriorityEnabled`（这两个 `…Enabled` 同时受总开关 `enabled` 门控，与 P3_StationCharge/P4_Isolation 同口径）。
- 改 `QualityEnv.cs`：新增 `LogInfo(string)`（各功能统一 `[QSystem][xxx]` 前缀）。
- 改 `Save/QualitySave.cs`：`BuildDocument`/`ApplyDocument` 每个星球节点新增 `"storages":[{e,b,cells:[{g,i,f,c,s,q}]}]`（只写非空溢出格）与 `"splitters":[{e}]`（只写 flag=1）；`SaveEmbedded`/`ReadEmbedded` 日志加 `storages=… splitters=…`；读档末尾 `RequestFullSweep()`，写档末尾 `ClearSaveSnapshot()`。**SchemaVersion 保持 1（加字段不升版本）**：SaveGuard 忽略未知字段、缺字段按空处理 ⇒ v0.12.x 旧档能读，v0.13.0 新档在旧版里只是少了两个节点（旧版忽略）。
- 改 `Patches/P1_SaveHooks.cs`：`GameData.Import` / `GameData.NewGame` 的 Postfix 里重置 `StorageGrids` 运行时状态。
- 改 `Plugin.cs`：`VERSION = "0.13.0"`、Awake 手动 Apply 两个新补丁并打印 `[QSystem][storage]` / `[QSystem][splitters]` 状态行、`Update()` 加 `StorageGrids.Tick()` + `HandleHotkey()`、`LogConfig` 追加 4 个新键。
- 改 `Diag/StartupAudit.cs`：新增 `GameData.Export(BinaryWriter)`、`CargoTraffic.UpdateSplitter(ref SplitterComponent)`、`CargoTraffic.UpdateSplitterAsync(ref SplitterComponent)` 三条反射自检 + 两行功能状态。
- 改 `DSPQualitySystem.csproj`：`<Version>0.13.0</Version>`。
- 测试工程：`OfflineTests.csproj` 链接 `QualityMath.Storage.cs` / `QualityMath.Splitter.cs` + 两个新 Checks 文件；`Program.cs` 追加 5 条检查。

### 27.17.6 构建 / 测试
- `BUILD OK`，0 警告 0 错误；`bin\Release\DSPQualitySystem.dll` = **217,088 B**（复核修复后重新构建为 218,112 B / `384087A3…`），SHA256 **`CABF9BE4EC4432AD4EB0AC4B2745C85B8BDA5974155D366849BBEBA8FF9F6D57`**，AssemblyVersion 0.13.0.0。
- 离线：**`ALL PASS (50 checks)`**（45 旧 + 5 新）：
  - `P4 storage grids (perPoint floor / max / base cap / degenerate)`：默认 0.5/20 给 5 格、大仓同样 5 格、`floor` 行为、1 点 1 格、0/负/NaN/Inf/perPoint 0/max 0/base 0 全 0、两重上限、0–64 点单调。
  - `P4 storage total (never negative / reshape predicate)`：`30+5=35`、负数不缩、out-of-sync 五例。
  - `P5 splitter order (quality first / stable / idle / tiny buffers)`：混品质降序、等品质稳定、空位沉底、全空/单品质不置换、升序反转、0 点货算有货、null/越界退化。
  - `P5 splitter gate (best slot / vanilla priority steps aside)`：最高点槽位、平局取低槽、`inPriority`/`presets` 让路。
  - `P5 splitter+storage trailer JSON (v0.13.0 nodes round trip / v0.12.x doc loads)`：新节点存读往返 + 旧 v0.12.x 文档仍能解析/校验（新节点缺失 = 空）。
- 踩坑：① net472 的 `ConcurrentQueue<T>` 没有 `Clear()`（改排水循环）；② `ApplyDocument` 新增 5 个 out 参数后 `LoadLegacy` 旧调用点漏改（CS7036）；③ 并行路径共享 scratch 数组的数据竞争（改 `[ThreadStatic]`）；④ 导出 shave 时 `bans > base` 会写出负数可用格（导出前夹 + 恢复）。

### 27.17.7 实机核对点（Lead 执行）
1. 启动：`[QSystem] === DSP Quality System v0.13.0 ===`、`patchedMethods=17`（本版全是手动注册，PatchAll 时点不变）。
2. 默认关闭：`cfg … storageQualityGrids=False storageQualityGridsPerPoint=0.5 storageQualityGridsMax=20 splitterQualityPriority=False`；`[QSystem][storage] NOT ARMED (storageQualityGrids=false; vanilla containers)`、`[QSystem][splitters] NOT ARMED (splitterQualityPriority=false; vanilla distribution)`；`[Audit] … -> OK` 全部覆盖新方法。
3. 打开 `storageQualityGrids=true` 重启：`[QSystem][storage] patch registered: GameData.Export(BinaryWriter) [shave+restore]` + `rule armed: perPoint=0.5 maxExtra=20`；给储物仓建筑注入 10 点（F11）后容器格数 +5；`[Audit] GameData.Export(BinaryWriter) -> OK`。
4. 存档结构不变（硬约束）：扩容状态下保存 → 用工具/存档编辑器或最小解析脚本确认 `.dsv` 里该容器的 `size` 仍是原版格数（小仓 30 / 大仓 100），`bans` 不超过 size；`[QSystem][storage] save shave: containers=1 …`（溢出格被搬回 base 时 cells=0）。
5. 卸载兼容：把 DLL 移走、用同一份带尾块 `.dsv` 启动原版 → 能正常读档（这是任务要求的硬验证项）。
6. 溢出格：把 base 区填满再让物品落进额外格 → 保存 → 日志出现 `save shave: … overflowCells=N (kept in the MOD trailer)`；读档后 `[QSystem][storage] restored p=… e=… base=30 cells=N`，物品回到额外格。
7. 品质下降回收：把该建筑点数改回 0（F11 注入 0 或拆掉重建）→ 溢出区为空时容器在 1 秒内缩回原版格数（`CollapseStep`）；溢出区有货时保持扩容不丢物品。
8. 打开 `splitterQualityPriority=true` 重启：`[QSystem][splitters] patch registered: CargoTraffic.UpdateSplitter + UpdateSplitterAsync …`；`[Audit] CargoTraffic.UpdateSplitter(ref SplitterComponent) -> OK` / `UpdateSplitterAsync -> OK`。
9. 分流器语义：打开分流器窗口 → 标题出现 ` [Quality First]`；F3 切换 → 日志 `[QSystem][splitters] F3 splitter p=… e=… quality-first=ON toggles=1`，标题后缀即时增减；混合品质输入时高点数货优先被输出（`debugProbes=true` 时 `priority order … inc=[…] order=[…]`），同品质仍按原序，四路输出规则不变。
10. 存档往返：分流器开关 ON 后保存 → 尾块 `"splitters":[{"e":<entityId>}]`；重进档后窗口标题仍有后缀、F3 可关闭；旧 v0.12.x 存档读入无报错。
11. 让路：给分流器设置原版输入优先级（`inPriority`）→ 本功能不再置换（应看到与原版一致的分配），日志无 `priority order`。
12. 关闭开关重启：两条 NOT ARMED 日志 + 运行时 `patchedMethods` 回到 39（本版 `GameData.Export` 早在 17 内，不新增方法）。

### 27.17.8 版本链
- v0.12.0：task-16 A 类五件套（187,392 B，SHA `E7355998…`，43 checks）。
- v0.12.1：光环默认 0.3 + 汇编器越界加固 + 多 Mod 尾块共存（188,416 B，SHA `01FF6FCE…`，44 checks）。
- v0.12.2：标记做成完整 cfg 选项（193,024 B，SHA `61EB8F23…`，45 checks）。
- **v0.13.0：task-17 储物仓品质格数 + task-18 分流器品质优先（本版）**（218,112 B，SHA `384087A3…`，50 checks）。

### 27.17.9 Lead 复核（2026-10-28 01:00–01:30）：C-1 实机失效的根因与修复
- 复核结论：构建与离线测试与交付一致（`BUILD OK`、`ALL PASS (50 checks)`、217,088 B / `CABF9BE4…`（修复后重建 218,112 B / `384087A3…`））；两功能默认关、开关打开后补丁确实注册；但 **C-1 实机完全不扩容**，与 27.17.7 第 3 条预期不符。
- 复现现场（`debugProbes=true`，`_lastexit_.dsv` 尾块注入 planet 103 实体 0–254 各 10 点）：
  - F9：`[Probe][F9] planet=103 entities=255 buildingInc 0=1 … 10=254` ⇒ 点位数据已按 planet 103 进内存。
  - 扫描日志：`[QSystem][storage] container e=38 storageId=1 size=60 type=Default points=0 supported=True`（8 只为 `e=38/60/75/116/131/169/191/212`、`size=60`），全部 `points=0` ⇒ `StorageExtraGrids` 得 0，不扩容。
- **根因（真实 bug）**：`GameData.factories` 不是按 planetId 索引，而是按 `PlanetData.factoryIndex` 索引。
  - 证据（`_analysis\quality-system\asm_full.cs`）：`192889 factories = new PlanetFactory[_gameDesc.starCount * 6]`；`186111 public int factoryIndex;`；`192576 PlanetData[] planets = stars[i].planets;`；`77079 int factoryIndex = galaxy.PlanetById(planetId).factoryIndex;`；UI 侧也是 `gameData.factories[planetData.factoryIndex]`（226569/227109/227212/434994）。
  - 本档 `十字架三-3号星` 的 `planet.id = 103`、`factoryIndex = 0`，于是 `StorageGrids.FactoryOf(planetId)` 拿 `data.factories[103]`（另一颗行星或 null）⇒ sweep 以 `sweepPlanet`（= factoryIndex）当 planetId 传给 `Apply`/`GetBuildingInc`，读到 planet 0 的点数表（全 0）⇒ 永不扩容。同时 `BuildDocument` 因此写出了多余的 `"0":{…}` 星球节点。
- 修复（Lead 直接改，含诊断增强）：
  - `StorageGrids.FactoryOf(int planetId)`：改为 `GameMain.data.galaxy.PlanetById(planetId)` → `planet.factoryIndex` → `data.factories[factoryIndex]`，并加注释说明 asm 行号与本 bug。
  - `StorageGrids.SweepStep()`：`sweepPlanet` 只当 factories 下标用，取 `sweptPlanetId = factory.planet.id` 后用于 `GetBuildingInc` / `Apply` / `PrunePlanet`；工厂 `planet == null` 时跳过。
  - `StorageGrids.BeginExportShave()`：`expanded` 键解出的 planetId 改走 `FactoryOf(planetId)`，不再直接 `data.factories[planetId]`。
  - `Save/QualitySave.cs EnsurePlanetCapacity(GameData, int planetId)`：同样改 `galaxy.PlanetById(planetId).factoryIndex` 取工厂（v0.11.11 起就存在的同型错误；只是"预扩容快路径"，不影响正确性，这次一并修正）。
  - 诊断（`debugProbes` 门控，长期保留）：`sweep done: containers=N expandedInSweep=M expandedTotal=K (perPoint=… maxExtra=…)`、每容器一行 `container p=… e=… storageId=… size=… type=… points=… supported=…`（最多 8 行）、`skip unsupported container p=… e=… type=… size=…`。
- 修复后构建：`bin\Release\DSPQualitySystem.dll` = **218,112 B**，SHA256 **`384087A3E276A58471A3190BB1563BFCD2A1F817AC02E4953B2501A784555DBB`**，AssemblyVersion 0.13.0.0；离线仍 **`ALL PASS (50 checks)`**。
- 修复后实机证据（pid 27880，两开关均 true）：
  - 读档扫描：`[QSystem][storage] expanded p=103 e=60 points=10 base=60 extra=5 total=65`（8 只储物仓全部 60 → 65）、`sweep done: containers=8 expandedInSweep=4 expandedTotal=8`（首帧预算 2048 实体，4 只当帧完成、4 只下一帧）。
  - 保存：`[QSystem][storage] save shave: containers=8 overflowCells=0` + `[QSystem][save] embedded quality trailer written ('沙盒1'): planets=1 buildings=255 pools=0 storages=0 splitters=0 bytes=4349`（溢出格为空 ⇒ 不需要 `storages` 节点；额外格由点数在下次读档时重算）。
  - **卸载兼容硬验证**：把 DLL 移出 `BepInEx\plugins\DSPQualitySystem\`（存档保持 shave 后的原版 `size`）→ 原版启动读同一份档成功进基地，`Player.log` 无 `Incomplete game save`、无 `[QSystem]` 行 ⇒ 任务硬约束「卸载 MOD 后带扩容仓库的存档仍可读」PASS。
- 仍未实机覆盖（写进 TESTLOG）：① 溢出格有货的路径（需要往额外格塞物品，`overflowCells>0` + `storages` 节点往返）；② 点数掉回 0 后的自动缩回（`CollapseStep`）；③ 分流器 F3 + 标题 ` [Quality First]` 交互与真实混品质四路分配（本档分流器操作依赖鼠标命中，GUI 自动化不可靠）。

## 27.18 v0.13.1：四向分流器品质优先的 UI 勾选（2026-10-28）

### 27.18.1 动机 / 目标
- v0.13.0 的 C-2 用「F3 热键 + 标题后缀」代表开关（见 §27.17.4），玩家反馈不够直观：打开分流器面板看不到开关，必须先知道 F3。
- 本版把它升级成真正的 UI 勾选框：在分流器窗口标题行右侧显示「品质优先 / Quality First」，点击即时切换该台分流器、刷新标题与勾选态、无需重启；F3 保留为快捷方式。

### 27.18.2 方案
- 新增 `Patches/P5_SplitterPriorityUi.cs`（547 行）：在 `UISplitterWindow._OnOpen` postfix 里懒构建一行控件（挂在标题行 `titleText.transform.parent`；失败回退到 `window.windowTrans` 左上角 `(18,-64)`）：
  - 优先**克隆原版 `UIToggle`**：`UIRoot.instance.uiGame.dispenserWindow.recycleAllToggle` → `playerModeToggle` → `sandboxMenu.veinFilterToggle` → `veinRandomRotToggle` → `Resources.FindObjectsOfTypeAll<UIToggle>()`，`Object.Instantiate` 后清空序列化监听（反射重置 `Toggle.m_OnValueChanged`，避免误改原窗口）、清 `group`、断开 `onObjects/offObjects`、重挂 `onValueChanged`；
  - 找不到时回退 uGUI：`Image` + `Toggle` + `Text`（字体取 `window.titleText.font`，再退 `Resources.GetBuiltinResource<Font>("Arial.ttf")`）。
  - 行布局：标题宿主锚点/pivot `(1,0.5)`、`anchoredPosition (-10,0)`、尺寸 `214×26`；回退宿主锚点 `(0,1)`、`(18,-64)`。
  - `Sync()` 每次打开窗口/热键切换后同步勾选态、标签颜色（开=亮黄，关=灰）；`_suppress` 防回环。
  - 点击回调 `OnToggleChanged`：`QualityData.SetSplitterPriority(planetId, entityId, on)` → `_clicks++` → `P5_SplitterPriority.RefreshTitle(window)` → 日志 `[QSystem][splitters] checkbox splitter p=… e=… quality-first=ON|OFF clicks=N`。
- `P5_SplitterPriority.cs` 重构：抽出 `TryGetCurrentSplitter(window, out planetId, out entityId, out on)`（窗口/实体/工厂校验），`OpenPostfix` 追加 `P5_SplitterPriorityUi.Attach`，`RefreshTitle` 改为 `QualityMath.SplitterTitleText` + `P5_SplitterPriorityUi.Sync`，`HandleHotkey` 复用同一套校验（F3 仍可用）；`Describe()` 附带 checkbox built/lazy。
- 纯函数下沉 `QualityMath.Splitter.cs`（+89 行，总 207 行）：`SplitterToggleText`、`SplitterTitleSuffix`、`SplitterToggleLabel`、`SplitterToggleVisible`、`SplitterToggleNext`、`SplitterFlagToSaved/FromSaved`、`SplitterTitleText`、`PlanSplitterPriorityOrder`，供离线测试。
- 存档：**零改动**——每台分流器开关 v0.13.0 已写在尾块 `splitters` 节点，UI 只是读写同一份 `QualityData` 侧表。
- cfg：`splitterQualityPriority` 键/默认值不变（false），注释与描述改成「总开关 + 每个分流器面板内独立勾选」；关闭时 `Apply` 直接返回 ⇒ 不注册任何补丁（含 `_OnOpen`）⇒ 不创建 UI，原版逐字节等价。
- 版本：`csproj` `<Version>0.13.1</Version>`、`Plugin.VERSION = "0.13.1"`。

### 27.18.3 文件
- 新增 `Patches/P5_SplitterPriorityUi.cs`（547 行）。
- 改 `Patches/P5_SplitterPriority.cs`（399 → 428 行）。
- 改 `QualityMath.Splitter.cs`（118 → 207 行）。
- 改 `QualityConfig.cs`（注释/描述，键与默认值不变）。
- 改 `Plugin.cs`（VERSION）、`DSPQualitySystem.csproj`（Version）。
- 测试：新增 `tests/OfflineTests/Checks.P5SplitterUi.cs`（180 行、4 条检查），`OfflineTests.csproj` 登记该文件，`Program.cs` 追加 4 条 `Check(...)`。

### 27.18.4 构建 / 测试
- `BUILD OK`，0 警告 0 错误；`bin\Release\DSPQualitySystem.dll` = **227,328 B**，SHA256 **`C91AAFF55D52F9A4208C9CC8D9E8D14382F0E9D11A2B642C7D1A0831BAC7AB22`**，AssemblyVersion 0.13.1.0。
- 离线（删除 `tests\OfflineTests\bin|obj` 后）：**`ALL PASS (54 checks)`** / `TEST PASS`（v0.13.0 的 50 条 + 新增 4 条）。
- 新增检查：`P5 splitter checkbox model`（勾选翻转 / 存档 flag 编解码 / 可见性矩阵 / 标签）、`P5 splitter title summary`（标题后缀增删与幂等）、`P5 splitter order gate`（off = 恒等顺序、on = 与旧计划一致）、`P5 splitter checkbox persistence`（尾块 `splitters` 节点往返 / 旧档无节点 = 全关）。
- 踩坑：新测试文件忘了 `using DSPQualitySystem.Save;`（`JsonLite`/`SaveGuard` CS0103），补上即可。

### 27.18.5 实机核对点（Lead 执行）
1. 默认关闭：`splitterQualityPriority=false` → 启动 `[QSystem][splitters] NOT ARMED (splitterQualityPriority=false; …)`、无 checkbox 日志，打开分流器面板**看不到**任何新增控件（原版等价）。
2. 打开开关重启：`[QSystem][splitters] patch registered: …` + `in-window checkbox built: cloned UIToggle '<来源名>' under '<宿主>'`（或 `uGUI fallback`）；`[Audit]` 行仍 OK。
3. 打开一台分流器 → 标题行右侧出现「品质优先 / Quality First」勾选框（未选中灰）；点一下 → 勾选变亮、标题出现 ` [Quality First]`、日志 `checkbox splitter p=… e=… quality-first=ON clicks=1`；再点取消。
4. 换另一台分流器 → 勾选框状态跟随该台（互不影响）；同一台重开面板仍保持。
5. 存档往返：勾选 ON 后保存 → 尾块 `"splitters":[{"e":<entityId>}]`；重进档后打开该分流器仍为勾选态；卸载 DLL 后原版读档正常（存档结构未变）。
6. 让路：给分流器设原版输入优先级（`inPriority`）→ 勾选框可点但分配仍按原版（功能让路），无 `priority order` 日志。
7. F3 兼容：窗口内按 F3 与点勾选框效果一致，视觉同步。
8. 位置/皮肤：不同 UI 分辨率下确认勾选框不遮挡标题文字；若重叠请记录窗口尺寸/分辨率（锚点坐标已记录在 §27.18.2，便于调整）。

### 27.18.6 版本链
- v0.13.0：task-17 储物仓品质格数 + task-18 分流器品质优先（218,112 B，SHA `384087A3…`，50 checks）。
- **v0.13.1：分流器品质优先 UI 勾选（本版）**（227,328 B，SHA `C91AAFF5…`，54 checks）。

## 27.19 v0.13.2：四向分流器勾选框的布局修正（2026-10-28）

### 27.19.1 动机 / 实机证据
- v0.13.1 的**功能链已实机全 PASS**（Lead 隐藏桌面 + 新版 QA harness v5）：启动 `patchedMethods=17`、首次打开分流器面板 `[QSystem][splitters] in-window checkbox built: uGUI fallback under 'panel-bg'`、经 `Toggle.isOn` 的点击路径 `checkbox splitter p=103 e=87 quality-first=ON clicks=1`、标题确实变成 `四向分流器 [Quality First]`、存档尾块 `splitters=1 bytes=276`、重进档恢复勾选态。
- 但**视觉位置错**：`uitree splitterWindow`（2560×1600、UI 缩放 200%）显示 `QSystemSplitterQualityRow pos=(-10.000,0.000) size=(214.000,26.000)`，挂在 `panel-bg` 上、锚点 `(1,0.5)` ⇒ 渲染在**面板右侧垂直正中**，正好压在 `splitter-circle`/`splitter-image` 大图标上，文字为半透明白、几乎读不出来（截图 `_live\c2-box-off.png` / `c2-box-on.png`）。
- 目标：勾选框移到**标题文字下方的空白带**；同时补验「关闭路径」与「多台分流器互不影响」；版本 v0.13.2。

### 27.19.2 坐标推导（实测 uitree，2560×1600 / UI 缩放 200%）
- `panel-bg` 下的实测子控件：`title-text pos=(40,-18) size=(145,36)`；`btn-box pos=(-50,-30) size=(40,30)`（关闭按钮，锚在面板右上）。
- `title-text` 与 `panel-bg` 共用左上角坐标系 ⇒ 标题带占 y = **-18 … -54**；分流器图标带从 y ≈ **-215** 开始，中间 y≈-46…-215 是空白带。
- 因此行采用 `anchorMin=anchorMax=(0,1)`、`pivot=(0,1)`、`anchoredPosition=(12,-62)`、`sizeDelta=(214,28)`：顶部距面板顶 62px（标题底 54 + 8px 间隙）、左端距面板左 12px（标题 x=40 之左）、右端 x=12+214=226（因锚点固定在左侧，**不随面板宽度漂移**）、距图标带 150+px。
- 回退宿主（`windowTrans`）保持左上角 `(0,1)` 锚点 + `(18,-64)`，高度同步改为 28。
- 跨分辨率风险：所有偏移量都相对 `panel-bg` 左上角（锚点全 0/1 比例，不是屏幕坐标），面板放大/缩小不会让行漂移；唯一风险是**极窄面板**（参考宽度 < 260）时右端可能接近右上关闭按钮，正常 UI 缩放下不触发。

### 27.19.3 方案
- `Patches/P5_SplitterPriorityUi.cs`（547 → 654 行）：
  - 新增布局常量：`RowWidth=214f`、`RowHeight=28f`、`RowLeft=12f`、`RowTop=-62f`、`BoxSize=18f`、`BoxLeft=6f`、`LabelLeft=30f`、`LabelWidth=176f`、`LabelFontSize=15`；颜色 `RowBgColor=(0.05,0.10,0.15,0.75)`、`BoxOnColor=(0.32,0.72,0.42,1)`、`BoxOffColor=(0.20,0.22,0.24,1)`。
  - `ApplyRowLayout` 重写为左上角锚点（v0.13.1 的右中锚点被彻底移除，注释里写明原因）；新增 `AddRowBackground` 给整行加半透明深色 `Image`（`raycastTarget=false`，不抢点击）。
  - uGUI 回退从「整行 `Image+Toggle`」改为「行左端 **18×18 子色块**（`Image+Toggle`，`anchoredPosition=(6,0)`、`pivot=(0,0.5)`、`Transition.None`、`ToggleTransition.None`）+ 右侧 `Text`（`LabelLeft=30`、`sizeDelta=(176,24)`、`fontSize=15`、`MiddleLeft`、`color=titleText.color` 或白色）」。克隆路径的 `UIToggle` 也缩到 18×18、左端 6px。
  - `Sync`：标签颜色 = 标题同色（开）/ 同色压暗到 82%（关）；色块按 `BoxOnColor`/`BoxOffColor` 换色。`_suppress` 防回环逻辑不变。
  - `ResolveHost` 增加三条明确日志：`titleText` 为 null / 父级为 null / 与 window 同 transform 时，分别打印「fallback to windowTrans top-left」后回退。
  - 构建日志补全主机信息：`… under '<宿主>' via=title-parent|windowTrans anchorMin=anchorMax=(0,1) pivot=(0,1) anchoredPosition=(12,-62) size=(214,28) font=15 labelLeft=30`，Lead 可直接用 `uitree` 对照。
- 关闭路径 / 多分流器：逻辑本身沿用 v0.13.1（`QualityData.SetSplitterPriority(...,false)` 删除侧表节点、`RefreshTitle` 用 `SplitterTitleText(...,false)` 去后缀、`TryGetCurrentSplitter` 每次读当前焦点分流器自己的 flag），本版**新增 2 条离线断言**覆盖 OFF 路径与两台独立状态。
- 版本：`csproj` `<Version>0.13.2</Version>`、`Plugin.VERSION = "0.13.2"`。

### 27.19.4 文件
- 改 `Patches/P5_SplitterPriorityUi.cs`（547 → 654 行：布局常量、行底色、18×18 勾选框、标签字体/颜色、宿主日志）。
- 改 `tests/OfflineTests/Checks.P5SplitterUi.cs`（180 → 271 行，新增 `CheckSplitterToggleOffPath`、`CheckSplitterPerSplitterIndependence`）。
- 改 `tests/OfflineTests/Program.cs`（513 → 517 行，追加 2 条 `Check(...)`）。
- 改 `Plugin.cs`（VERSION）、`DSPQualitySystem.csproj`（Version）、`README.md`（版本行/配置表/玩法/限制/热键/日志关键字）、`DEVLOG.md`、`TESTLOG.md`。
- **未改**：`QualityMath.Splitter.cs`、`Patches/P5_SplitterPriority.cs`、`QualityConfig.cs`、`Save/` 存档尾块逻辑、任何 `.ps1`。

### 27.19.5 构建 / 测试
- `BUILD OK`，**0 警告 0 错误**；`bin\Release\DSPQualitySystem.dll` = **228,864 B**，SHA256 **`D4D38AE7DA510AD81B5FDB1A57522DD8BAD7C0B795F99238985C97EDDF28987D`**，AssemblyVersion 0.13.2.0。
- 离线（先删 `tests\OfflineTests\bin|obj`）：**`ALL PASS (56 checks)`** / `TEST PASS`（v0.13.1 的 54 条 + 新增 2 条）。
- 新增检查：`P5 splitter checkbox off path`（OFF→ON→OFF：标题后缀增删、flag 1/0、`[x]`/`[ ]` 标签、5 次循环稳定）、`P5 splitter per-splitter flags`（87/88 两台独立、尾块只记录开着的、重开面板标题跟随各自 flag、单台关闭不影响另一台）。
- 回归：其余 54 条全 PASS，无退化。

### 27.19.6 实机核对点（Lead 执行）
1. 打开分流器面板：`[QSystem][splitters] in-window checkbox built: … under 'panel-bg' via=title-parent anchorMin=anchorMax=(0,1) pivot=(0,1) anchoredPosition=(12,-62) size=(214,28) font=15 labelLeft=30`。
2. `uitree splitterWindow`：`QSystemSplitterQualityRow` 的 `pos` 应为 `12.000,-62.000`、`size=214.000,28.000`，且不与 `title-text`（40,-18）/`btn-box`（-50,-30）/`splitter-circle` 重叠。
3. 视觉：18×18 色块在行左端（关=深灰、开=绿），文字与标题同色、深色半透明垫底可读，字号 15。
4. 点击 ON：`priority=true`、标题 `四向分流器 [Quality First]`、`clicks` 递增；再点击 OFF：`priority=false`、标题回 `四向分流器`、`clicks` 继续递增。
5. 两台分流器：各自勾选互不影响；A 开 B 关时重开面板，各自状态跟随。
6. 存档往返：ON 后保存 → 尾块 `splitters=1`；重进档 → 对应分流器仍勾选。
7. `splitterQualityPriority=false` 重启：无 built 日志、无任何控件（原版等价）。
8. 其它 UI 皮肤（若 `titleText`/父级缺失）：日志出现 fallback 行，控件落在 `windowTrans` 左上角 `(18,-64)`。

### 27.19.7 版本链
- v0.13.0：task-17 储物仓品质格数 + task-18 分流器品质优先（218,112 B，SHA `384087A3…`，50 checks）。
- v0.13.1：分流器品质优先 UI 勾选（227,328 B，SHA `C91AAFF5…`，54 checks）。
- **v0.13.2：勾选框布局修正（本版）**（228,864 B，SHA `D4D38AE7…`，56 checks）。



### 27.19.8 Lead 实机复核（2026-10-28 22:00–23:05）：勾选框布局与功能链全部 PASS

**复核手段**：隐藏 Win32 桌面实例（`_live\hidden-host.ps1`，用户桌面1 完全不受影响）+ 测试插件 `DSPQSDebugHarness`（文件命令队列），测试档 `QSTest`（= `沙盒.dsv` 拷贝），planet 103。断言证据以运行时组件字段与 MOD 日志为准，截图只作视觉复核。

- **启动**：`[QSystem] === DSP Quality System v0.13.2 ===`、`patchedMethods=17`。
- **构建日志**（新布局字段全部落盘）：`[QSystem][splitters] in-window checkbox built: uGUI fallback under 'panel-bg' via=title-parent anchorMin=anchorMax=(0,1) pivot=(0,1) anchoredPosition=(12,-62) size=(214,28) font=15 labelLeft=30`。
- **`uitree splitterWindow`**：`QSystemSplitterQualityRow active=true pos=12.000,-62.000 size=214.000,28.000`；同层 `title-text pos=40,-18 size=145x36`、`btn-box pos=-50,-30 size=40x30`、`splitter-circle size=186x186` 均不与其相交 ⇒ 27.19.2 的坐标推导成立。
- **视觉**：ON `_live\c2-v132.png`（深色半透明垫底 + 绿色 18×18 色块 + `[x] 品质优先 / Quality First`，标题 `四向分流器 [Quality First]`）；OFF `_live\c2-v132-off.png`（灰块、文字压暗、标题无后缀）。
- **点击链**：`toggle 87 on` → `applied=true path=mod:_toggle isOn=true before=false clicks=1 clicksDelta=1 priority=true`；日志 `checkbox splitter p=103 e=87 quality-first=ON clicks=1`；`toggle 87 off` → `isOn=false before=true clicks=2 priority=false`，再 `on` → `clicks=3`。走的正是 `Toggle.onValueChanged` ⇒ 与鼠标点击同一回调路径。
- **存档往返**：`save QSTest` → 尾块 `splitters=1 bytes=276`；`loadfull QSTest` → `embedded quality trailer used … splitters=1`；`toggle state 87` → `isOn=true priority=true`。
- **门控**：cfg `splitterQualityPriority=false` 重启 → `NOT ARMED (splitterQualityPriority=false; vanilla distribution)`，`uitree` 的 `panel-bg child=6`（无 `QSystemSplitterQualityRow`）⇒ 不注册补丁、不创建任何 UI。
- **未触发分支**：本机默认皮肤找不到可克隆的原版 `UIToggle`（`uitree` 无同名控件），走 `uGUI fallback`；`fallback to windowTrans top-left` 分支本机未触发，代码保留。

### 27.20 A 类五件套实机数值对比（v0.13.2）

**方法**：harness v5 的 `place`/`setpoints`/`forcepower`/`fuel`/`bullet`/`matrix`/`queue` 在真实游戏里造出测试建筑并喂料，逐功能比较「0 点 vs 10 点」。曲线定义见 §27.14：10 点 = 2.00×。

| 功能 | 0 点 | 10 点 | 结论 |
|---|---|---|---|
| A1 研究站研究速度（`place 2901`→eid=53，`labmode research` + `queue 3901`） | `hashUploaded` 60.06 hash/s | **120.06 hash/s**，日志 `[QSystem][lab] effective=2000` | 2.00× PASS |
| A2 防御塔射速（`place 3005`→eid=214） | 12 s 内 `fire rate bonus active` 0 条 | 12 s 内 20 条（debug 上限）+ `[QSystem][turret] effective=200/400/600/800 (methods=6)` | Prefix 在真实 `Shoot_*` 里生效 PASS |
| A3 发电功率（`place 2204`→eid=215，`fuel 215 1120 10`） | `capacity=36000` | **`capacity=72000`**（`serve=1.000`） | 2.00× PASS |
| A4 弹射器发射速度（`place 2311`→eid=214，`launch` 后计 `bulletCount` 消费） | 11.88 发/分 | **19.62 发/分**；日志 `[QSystem][launcher] ejector bonus active (planet=103 entity=214 factor=2.00 viaEnv=false)` | factor=2.00 生效；发数比 1.65× 属原版 `tick%30==id%30` 节流 PASS |
| A5 喷涂机概率 +1（基础档 eid=40） | 喷涂确有消耗（`incCount` 792→790） | `transpiler matched the byte-write site at instruction #361`、`armed (patch points=1)`；测试档带流不足，未复现 `+1 quality point` | 受限：反射站点 + 离线 5 条覆盖 |
| 发射井（`place 2312`→eid=217） | — | `launch` 只置 `boost=true`；`hasNode` 派生自 `GetAutoNodeCount()>0`，测试档无球面节点 | 仅补丁注册 + `boost` |

**结论**：A 类五件套实机均由建筑品质点数经分段曲线驱动；可量化的 A1/A2/A3 精确 2.00×，A4 方向一致（受原版发射节流影响 1.65×），A5 需真实生产带流、发射井需球面节点计划，二者以站点匹配 + 离线测试覆盖。


### 27.21 v0.13.2 补充实机（2026-10-28 22:30–22:45）：C-1 溢出格闭环、自动存档轮转、大档尾块开销

清掉 v0.13.0 遗留的「溢出格」「自动存档轮转」「24 MB 大档耗时」三项未覆盖。

- **C-1 溢出格写入**：容器 eid=38 点数 10 → `expanded … base=60 extra=5 total=65`；用 7 种建筑 × inc 1–10 填满 65 格；`save QSTest` → `[QSystem][storage] save shave: containers=1 overflowCells=5 (kept in the MOD trailer)`，尾块 `"storages":[{"e":38,"b":60,"cells":[{"g":60,"i":2212,"f":0,"c":1,"s":30,"q":8},…]}]`，文件 length=fileSize=7,050,787。
- **卸载兼容（硬约束）**：移走 `BepInEx\plugins\DSPQualitySystem\DSPQualitySystem.dll` 后用原版载同一档 → `ready=yes / errored=false`、`storage dump 38 → size=60 filled=60`、`Player.log` 零 `QSystem` 行、无 `Incomplete game save`。溢出格物品按设计只在 MOD 尾块里（卸载后不可见，但存档可读）。
- **恢复路径**：DLL 装回后重载 → `[QSystem][storage] restored p=103 e=38 base=60 cells=5`、`size=65 filled=65`，grid 60–64 = `2212 inc 8/9/10`、`2210 inc 1/2`，与尾块逐字段一致。
- **自动存档轮转**：默认 `asm_full.cs:198201 autoSaveTime = 480` 秒；22:42:18 观察到 `_autosave_0.dsv` 轮转写入（7,059,806 B，尾块 payloadLen=525 含 `storages` 溢出格），`_autosave_tmp.dsv` 已滚走 ⇒ 内嵌尾块随 `File.Move` 轮转天然跟随。
- **从自动存档载入**：`loadfull _autosave_0` → `restored … cells=5`、`size=65 filled=65`、`errored=false`。
- **24 MB 大档尾块开销**：离线基准 `_live\bench`（net10，链接 `Save\QualityBlobCodec.cs`）：7 MB **1.715 ms/读**；24 MB **1.427 ms/读**（1000 次均值 1.532 ms）；最坏「24 MB 且无尾块」需扫满 4 MB 窗口 **5.412 ms/读** ⇒ 对读档时间可忽略。
- **收尾**：游戏退出；用户存档 `_lastexit_.dsv` / `_autosave_0.dsv` 从 `_live-backup\saves-before-4items-20261028-205832` 复原，`QSTest` 与 `_autosave_tmp` 删除；harness 从 `plugins` 移出。

## 27.22 v0.14.0：物品品质角标（2026-10-28）

### 27.22.1 动机 / 目标
- v0.13.2 之后玩家反馈的下一项体验缺口：**品质点数只能通过 tooltip 或建筑光环间接看到**——背包/储物仓里 4 点和 10 点的同类建筑物品图标完全一样，要逐件悬停 `UIItemTip` 才能比较，批量整理/挑选高品物品很费眼。
- 目标（用户第 4 项要求）：在物品图标角上直接画**每件品质点数**，覆盖**玩家背包**、**打开的储物仓/容器面板**、**手持物品提示**；纯显示、不写存档、不改变游戏逻辑；`itemQualityBadges=false` 时补丁不注册、不创建任何 UI = 原版逐字节等价。
- Lead 追加 4 条硬约束（m00955）：① 每格角标对象必须与原版数字文本**同生命周期**（`OnStorageSizeChanged` 重建时一起建/毁，刷新时绝不 `new` GameObject，否则反复开关面板会泄漏）；② 关闭时零补丁、零对象，运行时切换安全无残留；③ 只在 `inc>0` 且**每件点数** ≥ `itemQualityBadgeMinPoints` 时显示，每件点数 = `grid.inc / grid.count`（`grid.inc` 是整格总点数）；④ 文档必须写明**建筑产物悬停预览不做角标**（只画配方产物、无真实 inc 数据，已由 `servingBox` 网格覆盖）。

### 27.22.2 asm 锚点（`_analysis\quality-system\asm_full.cs`）
- `UIStorageGrid`：类 :533304；`prefabNumText`（每格堆叠数字 `UI.Text` 预制体，角标模板来源）、`numTexts`（`Text[400]`）、`rectTrans`、`contentRect`、`textYOffset=-34`、`kGridSize=50`、`colCount=10`、`rowCount=5`。
- 生命周期 / 刷新：`CreateGridGraphic(int)` :533783（首次 `Instantiate(prefabNumText, rectTrans)`）；`RepositionGridGraphic(int)` :533827（`(index%col)*50, (index/col)*-50 + textYOffset`）；`OnStorageSizeChanged()` :533846（`rowCount` 重算 → `SetRectSize` → `DeactiveAllGridGraphics` → 按 `colCount*rowCount` 重建 → `OnStorageContentChanged`）；`OnStorageContentChanged()` :533865（逐格 `grids[i]`：`itemId>0` 时 `int num = count>0 ? inc/count : 0` clamp 0..10，写 `iconIndexArray`/`stateArray`；注意原版已在 icon 索引高字节塞 `Cargo.fastIncArrowTable[num]` 画小箭头，**不是数字角标**）；`_OnUpdate()` :533526 末尾 `if (storage.changed) { OnStorageContentChanged(); storage.changed=false; }` ⇒ 脏标记刷新钩子，无需每帧扫全表。
- `UIHandTip` :424386：`_OnUpdate()` :424441；`icon`（`Image`）、`iconbg`、`iconBlack`、`tipText`、`rtrans`、`arrow1/2/3`；持有点数 = `GameMain.mainPlayer.inhandItemInc / inhandItemCount`；建造模式下手持物品图标被隐藏（`icon.enabled=false`）⇒ 角标需跟随 `icon.enabled && activeInHierarchy`。
- 宿主窗口：`UIInventoryWindow` :499908（`inventory`=玩家背包 `UIStorageGrid`，`_OnOpen` → `inventory._Init(player.package); inventory._Open();`）、`UIStorageWindow` :535095（`storageUI`）、`UIAssemblerWindow` :466684（`playerInventory` :466714、`servingBox` :466780）、`UIGame` :385474 字段 `inventoryWindow` :385498 / `handTip` :385508 / `storageWindow` :385528 / `assemblerWindow` :385534。

### 27.22.3 方案
- 纯函数层 `QualityMath.Badges.cs`（新增，游戏无关，可离线测试）：`BadgeMaxPoints=10`、`BadgeStyleNumber/Dot/Both`、`BadgeDotGlyph="●"`；`BadgePointsFromGrid(totalInc,count)`（`count<=0||inc<=0 → 0`，整除后 clamp 0..10）、`BadgeMinPointsClamped`（1..10）、`BadgeStyleNormalized`（trim + 忽略大小写，未知→`number`）、`BadgeVisible(points,enabled,min)`、`BadgeLabelText(points,style)`（越界返回空串）、`BadgeTier(points)`（0 越界；1=1–2、2=3–4、3=5–7、4=8–10）、`BadgeColorRgb(tier)`（`0xB8C4D0` / `0x4FC3F7` / `0xB388FF` / `0xFFD54F`，越界 0）。颜色分档只做**背景/描边色**，文字统一浅色 + `Outline` 保证可读。
- 补丁层 `Patches\P6_ItemQualityBadges.cs`（新增）：
  - `Apply(Harmony)` 先过 `QualityConfig.ItemQualityBadgesEnabled`（false 时只打 `NOT ARMED` 日志，**一个 patch 都不注册**）；再用反射按「类型 + 方法名 + 无参签名」手工 `harmony.Patch(postfix: …)` 三个点：`UIStorageGrid.OnStorageSizeChanged()`、`UIStorageGrid.OnStorageContentChanged()`、`UIHandTip._OnUpdate()`。
  - `OnStorageSizeChanged` postfix：`EnsureGridHost()` 在 `grid.rectTrans.gameObject` 上挂**唯一一个** `QualityBadgeHost`（`AddComponent` 一次），`SyncCapacity` 把 `Text[]` 调整到 `clamp(colCount*rowCount, 0, 400)`：**新建格才 Instantiate**（克隆 `grid.prefabNumText`，失败则 `new GameObject`+`Text`），缩小时 `Destroy` 多余项 ⇒ 与 `numTexts` 重建一一对应，不随刷新泄漏。
  - `OnStorageContentChanged` postfix：`Refresh(grid)` 逐格读 `grids[i]`（反射缓存字段 `storage`）：`itemId>0 && count>0` → 纯函数算点数与可见性，只改 `text`/`color`/`SetActive`；不可见格 `SetActive(false)`（对象保留复用）。刷新计数每 200 次打一条 `grid badges effective=… badges=… size=cols x rows`。
  - `UIHandTip._OnUpdate` postfix：`GameMain.mainPlayer.inhandItemInc/inhandItemCount` → 点数；仅当 `tip.icon != null && icon.enabled && activeInHierarchy` 时显示（建造模式自动消失）；角标懒创建在 `tip.icon.rectTransform`（克隆 `tip.tipText` 失败则新建 `Text`）。
  - 角标定位（`PlaceBadge`）：`anchorMin=anchorMax=(0,1)`、`pivot=(0,1)`、`sizeDelta=(30,16)`、`anchoredPosition=(col*50+1, -(row*50)-1)` —— 格子左上角 (0,0) 是 `rectTrans` 左上，格子内缩 1px；手持角标 `(2,-2)`、`26×14`、fontSize 11（格子 12）。字体优先 `titleText.font`（`storageWindow` → `inventoryWindow`）→ `Resources.GetBuiltinResource<Font>("Arial.ttf")`。
  - 关闭路径：`ItemQualityBadges.SettingChanged` → 关闭时遍历 `UIStorageGrid.openedStorages` 复用已有 host 隐藏全部角标（**不创建**新对象），并清手持角标；不注销 Harmony patch（cfg 说明写「修改后需重启游戏」，重启时 false 即完全不注册）。
- 配置 `QualityConfig.cs`：新增 `itemQualityBadges`（bool，默认 `true`）、`itemQualityBadgeStyle`（string，默认 `number`）、`itemQualityBadgeMinPoints`（int，默认 `1`）；helpers `ItemQualityBadgesEnabled`（受总开关 `enabled` 叠加）、`ItemQualityBadgeMinPointsClamped`、`ItemQualityBadgeStyleNormalized`。
- 版本：`csproj` `<Version>0.14.0</Version>`、`Plugin.VERSION="0.14.0"`；`Plugin.Awake` 在 splitter Apply 之后 `P6_ItemQualityBadges.Apply(harmony)` + `Describe()` 日志；`LogConfig` 追加 3 键；`StartupAudit` 新增 3 条 `CheckMethod`（`UIStorageGrid.OnStorageSizeChanged`、`OnStorageContentChanged`、`UIHandTip._OnUpdate`，均无参 void）+ `P6_ItemQualityBadges.Describe()`。

### 27.22.4 文件
- 新增 `QualityMath.Badges.cs`（约 150 行：点数/样式/可见性/四档颜色纯函数）。
- 新增 `Patches\P6_ItemQualityBadges.cs`（约 721 行：3 个 postfix + `QualityBadgeHost` + `QualityHandBadgeHost`）。
- 改 `QualityConfig.cs`（字段 + 3 个 bind + 3 个 helper）、`Plugin.cs`（VERSION、Apply/Describe、LogConfig）、`Diag\StartupAudit.cs`（3 条反射自检 + Describe）、`DSPQualitySystem.csproj`（Version）。
- 测试：新增 `tests\OfflineTests\Checks.P5Badges.cs`（约 150 行、3 条检查）；`tests\OfflineTests\OfflineTests.csproj` 注册新文件 + 链接 `QualityMath.Badges.cs`；`tests\OfflineTests\Program.cs` 追加 3 条 `Check(...)`（末尾 58/59/60 行附近）。
- 文档：`README.md`（版本行、里程碑、3 条 cfg 行、玩法「物品品质角标」、已知限制、日志关键字）、`DEVLOG.md`（本节）、`TESTLOG.md`（v0.14.0 交接段）。
- **未改**：`Save/` 存档尾块逻辑、`QualityMath.Splitter.cs`、`Patches/P5_*`、任何 `.ps1`、`dist/`。

### 27.22.5 构建 / 测试
- `BUILD OK`，**0 警告 0 错误**；`bin\Release\DSPQualitySystem.dll` = **242,176 B**，SHA256 **`F146B33CDDF83ED52DA628EF0E611369272364C0D1048DDD00C595EC388C3F1D`**，AssemblyVersion 0.14.0.0。
- 离线（先删 `tests\OfflineTests\bin|obj`）：**`ALL PASS (59 checks)`** / `TEST PASS`（v0.13.2 的 56 条 + 新增 3 条，无退化）。
- 新增检查：`P5 item badges points`（`inc/count` 每件点数：500/100=5、1000/100=10、155/100=1 丢弃余数、150/10→钳 10、99/100=0、0/负 inc、0/负 count、单件 100=`10`、随 inc 单调升/随 count 单调降）、`P5 item badges visibility`（功能关全隐；min=1 时 1–10 全显；min 钳 0/-5→1、11→10；阈值 5 时 <5 隐、≥5 显；0/负/11 不显）、`P5 item badges style+tier`（样式归一化含 `DOT`/`Both`/乱码；label：`number`="7"、`dot`="●"、`both`="●7"、0/11 空；tier 边界 0/1–2/3–4/5–7/8–10 且单调；每档都有颜色、换档换色、1 档 `B8C4D0`、4 档 `FFD54F`）。
- 首次失败记录：`P6_ItemQualityBadges.cs(226,30) error CS0136`（`OnSettingChanged(object,EventArgs e)` 与 `catch(Exception e)` 重名）→ catch 变量改 `ex`；离线首跑 2 条断言是我自己写错（`150/10` 是 15→钳 10 不是 1；同档内颜色相同被误判为「不同档必须不同色」）→ 修正断言后全 PASS。
- patchedMethods 预期：**启动 17 不变**（本版全部是手工 patch，不走 PatchAll）；运行时不装 splitter、开 badges（默认）时 **24 基线 + 15 A 类 + 3 badges = 42**；badges 关闭则 39；splitter 也开则 43。

### 27.22.6 实机核对点（Lead 执行）
1. 启动：`[QSystem] === DSP Quality System v0.14.0 ===`、`patchedMethods=17`、`[QSystem][badges] armed (grid size+content postfix, hand-tip postfix; hosts=0 …style=number minPoints=1)`。
2. 打开背包（`inventoryWindow`）与储物仓（`storageWindow`）：带品质的建筑物品图标**右上角**出现数字角标；0 点物品无角标；`uitree` 应能看到每格 `QSystemQualityBadge` 的 `pos` 落在 `(col*50+1, -(row*50)-1)` 附近、`size=(30,16)`。
3. 视觉：四档颜色（1–2 灰蓝 / 3–4 青 / 5–7 紫 / 8–10 金），深色描边可读；200% UI 缩放下不与原版堆叠数字严重重叠（如重叠请记录分辨率/截图）。
4. 样式：cfg 改 `itemQualityBadgeStyle=dot` / `both` / `number` 重启，分别只圆点 / 圆点+数字 / 只数字。
5. 阈值：`itemQualityBadgeMinPoints=10` 重启后只有满品物品有角标。
6. 手持物品提示：手拿带品质物品时提示图标角上有角标；进入建造模式（图标隐藏）后角标同步消失。
7. 刷新与容量：同一面板反复开关/扩容/整理 20+ 次，`hosts` 不增长（每次面板重建才新增 1 个 host）、内存无持续上涨；`grid badges effective` 每 200 次一条。
8. 关闭：`itemQualityBadges=false` 重启 → `NOT ARMED`、`uitree` 无 `QSystemQualityBadge`、物品图标与原版完全一致。
9. 存档：保存/重进档角标照常显示；存档体积/尾块与 v0.13.2 相同（角标纯显示）。
10. 未覆盖分支：`Resources.GetBuiltinResource<Font>("Arial.ttf")` 回退路径、手持物品提示在 `icon` 为 null 的极端皮肤下（代码保留 try/catch）。

### 27.22.7 版本链
- v0.13.0：task-17 储物仓品质格数 + task-18 分流器品质优先（218,112 B，SHA `384087A3…`，50 checks）。
- v0.13.1：分流器品质优先 UI 勾选（227,328 B，SHA `C91AAFF5…`，54 checks）。
- v0.13.2：勾选框布局修正（228,864 B，SHA `D4D38AE7…`，56 checks）。
- **v0.14.0：物品品质角标（本版）**（242,176 B，SHA `F146B33C…`，59 checks；Lead 复核后按用户反馈把角标调暗，最终 SHA 见 §27.23.6）。

## 27.23 v0.14.0 角标调暗 + Lead 实机复核（2026-10-28）

### 27.23.1 用户反馈
实机截图后用户反馈（m02869）：「10级品质物品角标的光太亮了，调暗一些或者不发光。」原因是 10 点档的金色 `FFD54F` 全亮度 + 游戏 HDR 泛光（bloom），数字被糊成一团白光。

### 27.23.2 改动
1. `QualityMath.Badges.cs` `BadgeColorRgb` 四档颜色整体压暗（保留色相、降低明度与饱和）：
   - 档 1（1–2 点）`B8C4D0` → `93A0AE`；
   - 档 2（3–4 点）`4FC3F7` → `2F86AE`；
   - 档 3（5–7 点）`B388FF` → `7A5BB0`；
   - 档 4（8–10 点）`FFD54F` → `C79A2A`。
2. `Patches\P6_ItemQualityBadges.cs`：
   - 新增 `internal const float BadgeTextAlpha = 0.82f;`，`RgbToColor` 用它做 alpha（原为 1.0）——文字不再满亮度，泛光不再溢出；
   - `EnsureOutline` 的 `effectColor` alpha `0.9` → `0.5`（描边更轻，不会自己发亮）。
3. 离线测试 `Checks.P5Badges.cs` 的两条颜色断言同步到 `0x93A0AE` / `0xC79A2A`。

### 27.23.3 构建 / 测试
- `BUILD OK`（0 警告 0 错误）；`bin\Release\DSPQualitySystem.dll` = **242,176 B**，SHA256 **`F58204933742F9FD1FC4E63288E22FE546D21FF7520F79E3F9B8C99C3139C71B`**，AssemblyVersion 0.14.0.0。
- 离线（先删 `tests\OfflineTests\bin|obj`）：**`ALL PASS (59 checks)`** / `TEST PASS`。

### 27.23.4 实机复核（离屏窗口 + DSPQSDebugHarness v8，QSTest 测试档，planet 103）
1. 启动：`[QSystem] === DSP Quality System v0.14.0 ===`、`patchedMethods=17`、`[QSystem][badges] armed (grid size+content postfix, hand-tip postfix; hosts=0 … style=number minPoints=1)`；`[Audit] UIStorageGrid.OnStorageSizeChanged() -> OK`、`OnStorageContentChanged() -> OK`、`UIHandTip._OnUpdate() -> OK`。
2. 角标出现与数值正确：储物仓 `eid=38`（大型储物仓 60 格）存入 `item=2319` 的 0 点 / 10 点（1 件 + 2 件合并成 3 件 30 点）/ 7 点 / 2 点各一格；`storage dump 38` 为 grid3 inc=0、grid4 count=3 inc=30（每件 10）、grid5 count=1 inc=7、grid6 count=1 inc=2；截图 `_live\badge1.png` + 放大 `badge1-zoom2.png`：grid4 显示金色 `10`、grid5 显示紫色 `7`、inc=0 的 grid3 无角标。
3. 角标对象位置/数量：`uitree storageWindow` 显示每格一个 `QSystemQualityBadge`，`pos=(col*50+1, -(row*50)-1)`（如 col4 → `201,-1`、col5 → `251,-1`）、`size=(30,16)`；只有这两格 `active=true`，其余 58 格 `active=false`。
4. 亮度：调暗后截图 `_live\badge-dim.png` + 放大 `badge-dim-zoom.png` —— 金色 `10` 为哑光黄、无泛光晕圈；紫 `7`、灰 `2` 同样平实（对比调暗前 `badge1-zoom2.png` 中 10 点档有明显黄白色光斑）。
5. 样式与阈值同一次重启覆盖两项：cfg `itemQualityBadgeStyle=both` + `itemQualityBadgeMinPoints=3` → 截图 `_live\badge-both.png` + 放大 `badge-both-zoom.png`：10 点显示 `● 10`、7 点显示 `● 7`，2 点物品**不显示**（低于阈值 3）⇒ `both` 组合样式与阈值过滤都生效。
6. 反复开关无泄漏：`closeui` + `open storage 38` 交替 5 轮后 `uitree` 中 `QSystemQualityBadge` 对象数仍为 **60**（=格子数）、`active=2`，无重复创建。
7. 关闭等价原版：cfg `itemQualityBadges=false` 重启 → `[QSystem][badges] NOT ARMED: itemQualityBadges=false (or enabled=false); vanilla item icons`、`[Audit] badges NOT ARMED`，`uitree` 里没有任何 `QSystemQualityBadge`。
8. 存档往返：先 `store` 四种点数的物品 → `save QSTest`（写尾块）→ 退出重进 `loadfull QSTest` → 打开同一储物仓，角标按存档里的点数照常显示（10 / 7 / 2），说明角标完全由存档数据驱动、不依赖内存残留。

### 27.23.5 未覆盖（保留说明）
- 手持物品提示角标的**视觉**：离屏窗口无法移动鼠标悬停，只能确认 `[QSystem][badges] hand tip badge effective=…` 日志路径被执行、以及 `UIHandTip._OnUpdate` 反射自检 OK；真正的悬停截图需要真人操作。
- 200% UI 缩放外其它分辨率、极端皮肤下 `Arial.ttf` 回退路径未单独构造场景。

### 27.23.6 版本链（本版最终）
- v0.14.0（最终）：物品品质角标 + 调暗 —— `242,176 B`，SHA `F58204933742F9FD1FC4E63288E22FE546D21FF7520F79E3F9B8C99C3139C71B`，59 checks。

### 27.23.7 用户口径记录
- 2026-10-28 用户判定「蓝图携带品质」为错误方向、不再开发（已在 `DSP品质系统-MOD开发文档.md` 第 25 行与 README 记录）；下一版只做「物品品质角标」，随后不再自动扩展新功能，等用户指定。

## 27.24 v0.15.0：配置项精简 + 删除屏幕描边/蓝图携带品质（2026-10-29）

### 27.24.1 动机 / 目标（Lead 需求，m01164）
1. 玩家 cfg 太臃肿（v0.14.0 有 40+ 键，多数是历史调试/平衡旋钮），收敛为 **16 个键**：隔离 2、品质加成开关 7、可视化 7。
2. 其余全部转为**内置常量**、默认值与原来完全一致；`enabled` 总开关也转为常量（恒 true）。
3. `debugProbes` / `debugInjectPoints` 不再进玩家 cfg，改由环境变量 **`DSPQS_DEV=1`** 开启调试探针与注入热键（注入点数固定 4）。
4. **彻底删除两个已废弃功能**：
   - 屏幕描边（v0.11.6–v0.11.8 实验性、v0.11.9 起默认关，用户判定不是"沿模型边"的效果）；
   - 蓝图携带品质（用户 2026-10-28 判定为错误方向，见 §27.23.7）。
5. 删除后行为：蓝图复制/粘贴与原版逐字节一致、不写任何标记；粘贴落成的建筑点数仍按"优先消耗实际消耗物品点数"走。

### 27.24.2 方案
1. `QualityConfig.cs` 重写：保留 16 个真实 `ConfigEntry`（`isolateBuildingStacks`、`isolateStorageContainers`、`speedBonus`、`chargeBonus`、`labSpeedBonus`、`turretFireRateBonus`、`powerGenBonus`、`launcherSpeedBonus`、`sprayerIncBonus`、`itemQualityBadges`、`itemQualityBadgeStyle`、`itemQualityBadgeMinPoints`、`tenPointMarkerStyle`、`tenPointHaloSize`、`tenPointHaloPulse`、`tenPointHaloSpin`）；被移除的键用 `*Value` 内置常量表达（`EnabledValue=true`、`IncPerPointValue=100`、`PowerPerPointValue=250`、`BuildingChancePerPointValue=1.5`、`MinerChancePerPointValue=2`、`MaxUpgradeChanceValue=60`、`MaxQualityValue=10`、`GiveDismantleIncValue=true`、`SpeedPerPointValue=2`、`MaxSpeedBonusValue=50`、`ChargePerPointValue=2`、`MaxChargeBonusValue=50`、`SpeedBonusModeValue="curve"`、`ExtraUpgradeAboveValue=6`、`DoubleUpgradeAtValue=10`、`PreferHighQualityConsumeValue=true`、`TenPointHaloColorValue="FFD24A"`、`TenPointHaloLiftValue=1.2`、`TenPointHaloMaxValue=64`、`StorageQualityGridsValue=true`、`StorageQualityGridsPerPointValue=0.5`、`StorageQualityGridsMaxValue=20`、`SplitterQualityPriorityValue=true`、`DebugInjectPointsValue=4`）。
2. 为了让 30+ 处既有 `QualityConfig.Xxx.Value` 调用点无需改动，引入极小替身类型 `QualityFixedBool/Int/Float/String`（提供 `.Value`）与 `QualityDevSwitch`（`DevMode` = `Environment.GetEnvironmentVariable("DSPQS_DEV")=="1"`）；`QualityConfig.DebugProbes` 指向 `QualityDevSwitch`、`DebugInjectPoints` 指向固定 4。
3. 删除 `QualityConfig` 的 `Enabled.SettingChanged` 事件与 `OutlineArmed` / 描边 helper / `BlueprintQualityEnabled`；`HaloArmed` 改为"`tenPointMarkerStyle != none`"。
4. `tenPointMarkerStyle` 收窄为 `auto` / `halo` / `none`：`QualityMath.ResolveMarkerStyle(string, out bool haloOn)` 去掉了 outline/both 语义，旧值（`outline`/`both`/未知）统一按 `auto`（= 画光环）处理。
5. `QualityMath.Outline.cs` 整个删除；其中光环仍需要的两个纯函数搬进 `QualityMath.Halo.cs`：`SelectHaloTargets`（原 `SelectOutlineTargets` 的稳定最近优先选择，上限常量改为 `HaloMaxDrawDistance=120f`）与 `CameraPixelsToScreen`（相机像素 → 屏幕像素换算，供光环屏幕空间回退用）。
6. `Patches\P5_TenPointOutline.cs`、`Patches\P4_BlueprintQuality.cs`、`Patches\P5_OutlineGraphic.cs`（只含描边 mesh）删除；`QualityData.Outline.cs`、`QualityMath.Blueprint.cs` 保留（前者是光环的"满点建筑收集"，后者只剩通用的 `PerItemPoints`/`ChooseBestMatchGrid`/`ShouldSwapHand`，删掉 `ShouldApplyBlueprintInc`）。
7. `Patches\P1_BuildCapture.cs`：`AddPrebuildData_ClearStale.Postfix` 里"蓝图接管则跳过"的分支删除，统一 `P4_ConsumePriority.CapturePasteConsumed(prebuild, __result);`。
8. `Plugin.cs`：`VERSION="0.15.0"`；删掉 `OnEnabledChanged` 订阅/方法、`P4_BlueprintQuality.Apply/Describe`、`P5_TenPointOutline.Install/Tick/Describe`；`HandleHotkeys()` 首行 `if (!QualityConfig.DevMode) return;`；`LogConfig()` 改为两行——16 个玩家键 + 一行 `cfg builtin=(internal constants…)` 打全部内置常量与 `debugProbes=<DevMode>`。
9. `Diag\StartupAudit.cs`：删掉 `BlueprintUtils.GenerateBlueprintData` 自检与 `P4_BlueprintQuality.Describe()`、`P5_TenPointOutline.Describe()`（保留光环 describe）。
10. **cfg 说明中文化 + 老档键集合自愈（用户追加需求）**：`QualityConfig.Init` 里 16 个玩家的 `cfg.Bind(...)` 描述全部改写为中文 `<中文名>：<作用/取值说明>`；第一个键（`isolateBuildingStacks`）的描述开头加一段总说明「本文件只保留 16 个玩家可选开关 / 其余平衡参数已内置 / `DSPQS_DEV=1` 开调试」。中文描述集中到新增纯文件 `QualityConfigText.cs` 作为唯一来源（Bind 里写 `QualityConfigText.DescriptionOf("key")`），这样离线测试能直接断言文件文本。
11. **老档升级自愈（m01575 实机反馈后的修正）**：第一次实机把 v0.14.0 的 47 键英文 cfg 升级成 63 键（BepInEx 把旧键当 orphan 保留，且 `ConfigFile.Save()` 只会增/改、永远不会删键，迁移日志也没出现）。改成**自己写文件**：`Init` 末尾调用 `MigrateCfgLayout(ConfigFile)` —— 读出整份 cfg 文本 → `QualityConfigText.ParseKeyValues` 解析所有 `key = value`（忽略注释/段名）→ **若键集合正好是这 16 个键就早退、不碰文件**；否则把玩家当前值按类型写回 `ConfigEntry`（bool/int/float/string，解析失败用默认值，期间 `SaveOnConfigSet=false` 抑制 BepInEx 自动保存），再用 `QualityConfigText.BuildCfgText(Plugin.VERSION, values)` 生成一份只含 16 个键、中文注释、`[quality]` 单段、UTF-8 无 BOM 的完整文本直接覆盖写入；随后用反射清空 BepInEx 的 `ConfigFile.OrphanedEntries`（属性或字段，取不到就静默跳过）。成功打 `[QSystem][config] rewrote cfg with cleaned Chinese layout (16 keys, dropped N legacy keys)`；无变化时零日志、不动 mtime；异常走 `QualityEnv.WarnOnce("config.chinese", e)` 且不阻塞启动。全新安装（无 cfg）时由本轮 Bind 直接生成 16 键中文文件，迁移早退。

### 27.24.3 文件
- 重写：`QualityConfig.cs`（~600 行）、`Plugin.cs`（~280 行）、`tests\OfflineTests\Checks.P5HaloCfg.cs`（marker-style 断言改为单一输出）。
- 修改：`QualityMath.Halo.cs`（+`SelectHaloTargets` / `CameraPixelsToScreen` / 新 `ResolveMarkerStyle`）、`Patches\P5_TenPointHalo.cs`（改用 `HaloMaxDrawDistance`/`SelectHaloTargets`，调试日志改看 `DevMode`）、`Patches\P1_BuildCapture.cs`、`Diag\StartupAudit.cs`、`QualityMath.Blueprint.cs`（注释与 `ShouldApplyBlueprintInc` 删除）、`DSPQualitySystem.csproj`（0.15.0）、`tests\OfflineTests\Program.cs`（59→55 条）、`tests\OfflineTests\OfflineTests.csproj`、README.md。
- 新增：`QualityConfigText.cs`（纯文本构建器：16 键元数据 + `ParseKeyValues` / `IsPlayerKeySet` / `CountLegacyKeys` / `BuildCfgText`，无 BepInEx/Unity 依赖、可离线测试）、`tests\OfflineTests\Checks.CfgText.cs`（cfg 文本检查）、`tests\OfflineTests\Checks.P4ConsumePriority.cs`（覆盖 `PerItemPoints` / `ChooseBestMatchGrid` / `ShouldSwapHand`，替代被删的蓝图品质检查）。
- 删除：`Patches\P5_TenPointOutline.cs`、`Patches\P4_BlueprintQuality.cs`、`Patches\P5_OutlineGraphic.cs`、`QualityMath.Outline.cs`、`tests\OfflineTests\Checks.P5Outline.cs`、`tests\OfflineTests\Checks.P4Blueprint.cs`。

### 27.24.4 构建 / 测试
- `BUILD OK`（0 警告 0 错误）。构建期两个小问题：① 删掉 `QualityMath.Outline.cs` 后 `P5_TenPointHalo.Draw.cs:72` 报 `CS0117 CameraPixelsToScreen 未定义` → 把该纯函数搬进 `QualityMath.Halo.cs`；② `P5_OutlineGraphic.cs` 因只被描边使用出现 6 个 `CS0649` 警告 → 直接删除该文件。
- 离线（先删 `tests\OfflineTests\bin|obj`）：**`ALL PASS (55 checks)`** / `TEST PASS`（v0.14.0 的 59 条中删掉 5 条：蓝图参数、蓝图品质门、outline math/rect/selection → 54 条；本版再新增 1 条 cfg 文本检查 → 55 条；`CheckConsumePriorityPick` 是原 `Checks.P4Blueprint.cs` 里保留改写的，不增计数）。新测试初版有一条自己的数据写错（`ChooseBestMatchGrid` 期望 index 2，但 index 3 的 20/2=10 才最大）→ 把 `incs` 改为 `{50,0,90,10}` 后全绿；cfg 检查初版把样例键数写成 7（实际 6）→ 改为 6 后全绿。
- `bin\Release\DSPQualitySystem.dll` = **213,504 B**，SHA256 **`0A9DCD9A8E8A80E5A087CBD2061608ADEA08D5FADD803517072353002453D2FB`**，AssemblyVersion 0.15.0.0（cfg 键集合自愈 + 首键注释两段式排版修正后的最终值；中间态 211,968 B / `04B6AC44…`、208,384 B / `B8CD0574…`、214,016 B / `0EE14EF5…` 均已作废）。

### 27.24.5 实机核对点（Lead 执行）
1. 启动横幅 `v0.15.0`、`patchedMethods=17`；`LogConfig` 只列 16 个玩家键 + 一行 builtin 常量；`cfg` 文件里只剩 16 个键（旧键由本版自愈逻辑清掉，见 §27.24.7；`BepInEx\config\dsp.qualitysystem.cfg` 打开后不应再有 `enabled` / `debugProbes` / `tenPointHaloColor` 等旧键）。
2. `[Audit]` 里不再出现 `BlueprintUtils.GenerateBlueprintData` / `P4_BlueprintQuality` / `P5_TenPointOutline` 行；光环 self-check 仍在。
3. 不设 `DSPQS_DEV` 时 F5/F6/F7/F8/F9/F10/F11/F12 全部无效（不注册），设 `DSPQS_DEV=1` 后恢复；`F10/F11` 注入固定 4 点。
4. 蓝图复制/粘贴：任意 10 点建筑复制出的蓝图，与原版蓝图无差异（可对比 v0.14.0 存档里的旧蓝图）；粘贴落成点数 = 消耗物品点数（消耗最高品质逻辑不变）。
5. `tenPointMarkerStyle`：`auto`/`halo` 画光环、`none` 不画；旧 cfg 写 `outline`/`both` → 按 `auto` 画光环，不报错。
6. 光环颜色/浮高/数量上限虽不可配，但默认表现与 v0.14.0 相同（金色 `FFD24A`、浮高 1.2m、同屏 64）。
7. 16 个键逐个关掉：隔离/加速/加成/角标/光环各项仍保持"关闭 = 原版等价"，尤其 `itemQualityBadges=false` 无角标、`tenPointMarkerStyle=none` 无光环。

### 27.24.6 版本链
- v0.14.0：物品品质角标 + 调暗 —— `242,176 B`，SHA `F5820493…C3139C71B`，59 checks。
- **v0.15.0（本版最终）**：配置项精简 + 删除描边/蓝图携带品质 + cfg 说明中文化 + 老档键集合自愈 + 首键注释两段式排版 —— `213,504 B`，SHA `0A9DCD9A…53D2FB`，55 checks。

### 27.24.7 cfg 中文化 / 老档自愈落地细节

这是本版唯一一处**实机返工**的修复（Lead m01575）：初版用 `ConfigFile.Save()` 重写，实测把 v0.14.0 的 47 键英文 cfg 升成了 **63 键**（16 新键 + 47 旧键被 BepInEx 当 orphan 保留），且迁移日志从未出现。原因是 `ConfigFile.Save()` 只会增/改键、**永远不会删除**已有键；orphan 键在内存里也一直存在。

修正后的落地：

- **中文描述单一来源**：16 个 `cfg.Bind` 的描述参数改为 `QualityConfigText.DescriptionOf("<key>")`；`QualityConfigText.PlayerEntries` 里同时带 `Key` / `TypeName` / `DefaultText` / `Description`，与 Bind 的默认值逐项对齐。总说明写在第一个键 `isolateBuildingStacks` 的描述开头（不新增 `configNote` 键）。
- **自愈触发条件 = 键集合**：`Init` 末尾调用 `MigrateCfgLayout(ConfigFile)`。读 `cfg.ConfigFilePath` 全文 → `QualityConfigText.ParseKeyValues` 解析所有 `key = value`（跳过空行/注释/`[section]`，重复键先到先得）→ **键集合正好等于 16 个玩家键时立即早退**（全新安装、以及本版之后每次正常启动都**不会**重写、不动 mtime、不打日志）。缺失 / 多余 / 旧键残留都会触发重写。
- **保留玩家值**：触发后先按 `PlayerEntries` 逐个 `FindEntry` → `ApplyParsedValue`（bool/int/float 用 InvariantCulture 解析、float 拒绝 NaN/Inf、string 去首尾空白；解析失败保留 BepInEx 已加载的值/默认值），期间 `cfg.SaveOnConfigSet=false` 抑制 BepInEx 自动保存。
- **自己写文件**：`File.WriteAllText(path, QualityConfigText.BuildCfgText(Plugin.VERSION, values), new UTF8Encoding(false))`，内容固定为：两行 `## Settings file was created by plugin DSP Quality System v{version}` / `## Plugin GUID: dsp.qualitysystem` → 空行 → `[quality]` → 空行 → 每个键 `## <中文说明>` / `# Setting type: Boolean|Int32|Single|String` / `# Default value: …` / `key = value` / 空行。CRLF 统一、UTF-8 无 BOM、不带任何旧键。
- **清 orphan**：写完后 `ClearOrphanedEntries(cfg)` 用反射拿 `ConfigFile.OrphanedEntries`（属性，取不到再找 `orphanedEntries` 字段），按 `IDictionary` 清空；整段 try/catch 静默（BepInEx 版本差异时最坏只是内存里留着 orphan，不影响文件与启动）。
- **日志**：重写成功固定打 `[QSystem][config] rewrote cfg with cleaned Chinese layout (16 keys, dropped N legacy keys)`（`N` = 旧键数量，例如 Lead 那台机器为 47）；无变化时零日志；异常 `QualityEnv.WarnOnce("config.chinese", e)` → `[QSystem][patch] config.chinese failed: <exception>`，每次启动最多一条、不阻塞启动。
- **预期行为**：63 键老档 → 首次启动读入 63 键 → 判定 ≠16 → 写回 16 键（值保留、旧键全清、注释中文、头版本 v0.15.0）→ 打上面那条日志；同一次启动内存 orphan 也被清空；第二次启动读到 16 键 → 早退、不重写。全新安装：Bind 直接生成 16 键中文文件 → 迁移因 `!File.Exists` 早退（BepInEx 已在 Bind 时建好文件，因此第二次启动同样早退）。
- **离线覆盖**：`tests\OfflineTests\Checks.CfgText.cs::CheckCfgText`（第 55 条检查）用一段 v0.14.0 风格文本（3 个旧键 + 改过的 bool/string/float）断言：解析、触发矩阵（16 键 true / 多 1 键 false / 少 1 键 false / 大写仍 true）、`BuildCfgText` 输出（v0.15.0 头、GUID、单段 `[quality]`、CRLF、恰好 16 个键、旧键消失、改动值保留、未改动键回默认、中文注释与 `# Setting type`/`# Default value` 行齐全、null 值回退默认、重复键先到先得）。
- **首键注释排版修正（Lead m01777 实机反馈，不升版本号）**：自愈后的 cfg 里第一个键 `isolateBuildingStacks` 的注释曾是一整行长文本（总说明与键说明之间用 `===` 粘连，玩家读起来像一条）。现改为两段式：总说明精简为「【DSP 品质系统 v0.15.0】本文件只开放 16 个玩家开关，其余平衡参数已内置；调试探针需环境变量 DSPQS_DEV=1。」，再用中文全角「｜」连接该键说明 `隔离-背包建筑分格：…`（BepInEx 只在描述前加一个 `## `，多行拼接不会自动加前缀，所以只能用行内分隔符，不能用 `===`/`---` 这类 ASCII 分隔线）。其余 15 个键复查：无 `===`/`---` 拼接、无重复版本号字样（`tenPointMarkerStyle` 描述里的「v0.15.0 已删除屏幕描边」改为「屏幕描边功能已删除」）。`CheckCfgText` 新增 3 条断言锁死排版：必须含 `｜隔离-背包建筑分格`、不得含 `===` 与 `---`、总说明句只出现 1 次。键名、默认值、迁移逻辑与触发条件全部不变；改后 `BUILD OK`（0 警告 0 错误）+ `ALL PASS (55 checks)`，DLL 变为 213,504 B。

## 27.25 v0.15.0：R2/Thunderstore 发布包（图标 + manifest）

- **需求**：用户要求打成「直接能在 R2 上发布」的格式，注意 manifest 的 256 字与字符类型限制，icon 用异星工厂传奇品质图标打在戴森球 MK.III 增产剂上，简介写明品质系统的可玩性和特点。
- **图标素材**：
  - 戴森球「增产剂 Mk.III」= item 1143。游戏资源里 `Texture2D "1143"` 只是白色剪影遮罩（80x80，`resources.assets`，UnityPy 导出验证），真正彩色图标由运行时生成，故改用**游戏内截图**：离屏窗口（`_live\vis-host.ps1`）+ 测试插件 `DSPQSDebugHarness` 命令队列：`place 2102` 新建大型储物仓 → `store <eid> 1143 1 0`（数量 1 时不画数量文字）→ `open storage` → `_live\capture.ps1` 抓 2560x1600，裁出 71x82 图标，用「边缘泛洪 + 最大连通域」做透明抠图。
  - 异星工厂传奇品质图标：`C:\Games\Steam\steamapps\common\Factorio\data\quality\graphics\icons\quality-legendary.png`（64x64 橙色五瓣，等级色），放大到 86px，加深色圆盘底 + 金色描边 + 暖色辉光，置于图标右下角。
  - 合成：256x256 深蓝径向渐变底 + 六边形网格纹理 + 蓝色辉光 + 瓶身投影；脚本 `_live\make-icon3.py`，成品 `tools\r2\icon.png`。
- **manifest**（`tools\r2\manifest.template.json`，`{{VERSION}}` 占位）：
  `name=DSPQualitySystem`、`version_number=0.15.0`、`website_url=""`、`description`=129 字符中文简介（玩法/品质经济/容器/分流器/光环/角标/16 开关/内嵌存档）、`dependencies=[xiaoye97-BepInEx-5.4.17, CommonAPI-CommonAPI-1.6.7]`。
- **打包脚本** `tools\pack-r2modman.py`：读 csproj 版本 → 校验 manifest 全部硬性限制（name 字符集/长度、semver、description ≤250 单行、website_url、依赖格式）→ 校验 icon 256x256 → 拷贝 DLL → 生成固定时间戳 zip（可复现）。README/CHANGELOG 源文件在 `tools\r2\`，README 面向玩家写清玩法特点与安装/配置/兼容性。
- **构建与校验**：DLL 213,504 B / `0A9DCD9A8E8A80E5A087CBD2061608ADEA08D5FADD803517072353002453D2FB`；`python tools\pack-r2modman.py` 两次运行产出同一 zip：**138,395 B，SHA256 `E7E87004AD9C223A823C117EB53372D3A48419C8843BDDC1CF9C86DA7C68127F`**；`_live\validate-r2.py` 全项 PASS（包结构/manifest 字段/icon 尺寸/无多余目录/DLL 哈希）。
- **环境**：图标采集用的离屏实例与 `QSTest` 测试档已清理（`quit` 会写 `_lastexit_`，已用用户 `沙盒.dsv` 回填 `_lastexit_.dsv`+`_lastexit_.moddsv`，被覆盖的测试版留档 `_live-backup\_lastexit_-polluted-by-icon-test.dsv`）；测试插件移回 `_live-backup\plugins-parked-harness\`。
- **文档同步**：开发文档 §6.1 改为「55 条实际执行的 `Check(...)` 调用」（`Program.cs` 搜 `Check(` 多出的 1 处来自非调用文本）、§3.1 明确自愈触发「只看键集合，不看注释文本」，并新增 `### 8.6 R2 / Thunderstore 发布包`。
- **版本链**：v0.15.0 起同时产出两种包——`dist\DSPQualitySystem-0.15.0.zip`（交接/手动安装，98,611 B / `7675C417…`）与 `dist\DSPQualitySystem-0.15.0-r2modman.zip`（Thunderstore/r2modman，138,395 B / `E7E87004…`）。

## 27.26 v0.16.0：三处实机问题修复（大型采矿机 / Shift 立即建造 / 增产额外产物）

### 27.26.1 动机（Lead m01936 的实机 bug 清单）

Lead 在 v0.15.0 实机验收中报告了 3 个功能不生效的问题，要求先给出 asm 证据再修：

1. **大型采矿机（物品 2316）拿不到品质加成**（品质与速度都不对），小型矿机（2301）正常；
2. **Shift+左键「复制建筑（立即建造）」落成的建筑丢品质**（用 4 点物品建出来是 0 点）；
3. **增产（productive）模式下额外产物是 0 点**，普通产物基本正常。

### 27.26.2 根因（全部用 `_analysis\quality-system\asm_full.cs` 行号+IL 证据）

- **Bug 1 — 大型采矿机是「站点 + 采集器」结构，产物不走矿机输出出口。**
  - `desc.veinMiner && desc.isVeinCollector`（:105684）让这台建筑走 `StationComponent` 分支（:208908-208926：`stationComponent.minerId = num; stationComponent.storage[0].itemId = …; collectionIds[0] = …`）；它的 `MinerComponent.insertTarget` 始终为 0（:208341-208346 只在"矿机没接物流站"时才 `SetMinerInsertTarget`），所以 `MinerComponent.InternalUpdate` 里唯一的 `factory.InsertInto(insertTarget, 0, productId, (byte)num17, 0, out remainInc)`（:57429）**永远不会为它执行** —— 这也解释了为什么已有的 `P0_Miner` IL 钩子对它无效。
  - 产物由 `StationComponent.UpdateVeinCollection(PlanetFactory, int[])`（:77689-77722）把 `minerPool[minerId].productCount` 搬进 `storage[0]`：`storage[0].count += num2; minerPool[minerId].productCount -= num2;`，**原版这条路径从不写 `storage[0].inc`** ⇒ 品质点数丢失。
  - 速度方面 `MinerComponent.InternalUpdate` 仍由矿机池循环调用（:98920-98929 与并行版 :259194-259230），`P3_Speed.MinerPrefix` 乘的 `power` 参数同样作用于采集站里的矿机，理论上速度加成已生效；实机若仍看不到差异，需另行排查电量/展示口径（本版未改速度路径）。
- **Bug 2 — 立即建造把取料返回的 inc 丢掉了。** `PlayerAction_Inspect.GameTick`（:132659）的立即建造块（:133137-133169）里 `player.package.TakeTailItems(ref itemId, ref count, out var _)` 把 `out inc`（本次取料的**总点数**，:80771-80816）直接丢弃，然后 `factory.BuildFinally(player, num2)`（:212456）落成；这条路径不经过 `Player.UseHandItems`，所以 `P1_BuildCapture` 的 `BuildQualityStash` 捕获不到。
- **Bug 3 — 品质池按「净增量」记账，被同窗口取货抵消。** `P0_Assembler.Postfix` 用 `produced[]` 的差值当产量写池；但 `PickFrom`（:217966-218019）每取一件就 `produced[j]--`，两次 tick 之间取货会把产量抵消。普通产物是整批爆发（一次 +N），少量取货只抵消一部分；**增产额外产物按每 10 个周期 +1 件的节奏滴出**（`extraTimeSpend = TimeSpend*100000`，:293442），几乎每件都被同期取货抵消 ⇒ 池子长期缺额 ⇒ 额外产物取出来就是 0 点。

### 27.26.3 方案

- **Bug 1**：新增 `Patches\P0_MinerCollector.cs`（属性补丁 `[HarmonyPatch(typeof(StationComponent), "UpdateVeinCollection", new[]{ typeof(PlanetFactory), typeof(int[]) })]`）。Prefix 记录 `storage[0].count`，Postfix 用差值 `added` 调 `QualityData.MinerCollectorIncLevel(factory, station.entityId, added)` 做一次品质判定（复用 `QualityRules.RollMinerQuality`，0 点建筑=0），再 `storage[0].inc = QualityMath.StationIncAfterTransfer(before, added, perItem, storage[0].count)`（封顶 `count*10`）。新纯函数 `QualityMath.StationIncAfterTransfer` + 常量 `IncPerItemMax=10`；`QualityData.MinerCollectorIncLevel` 带 `Diag.Probes` 计数与 `WarnOnce("miner.collector.roll")` 降级。
- **Bug 2**：新增 `Patches\P1_InstantBuild.cs`（`internal static class InstantBuildCapture`，手动 `harmony.Patch`，因为 HarmonyX 特性无法写 `ref` 参数类型）。Postfix `StorageComponent.TakeTailItems(ref int, ref int, ref int, bool)` 记录「同帧最后一次取料（itemId/count/inc）」；Prefix `PlanetFactory.BuildFinally(Player,int,bool,bool)` 在同帧、且幽灵建筑 `protoId/itemRequired` 与记录一致时，`QualityData.SetPrebuildInc(prebuildId, QualityMath.InstantBuildPerItem(inc, count))`，随后由既有 `AddEntityData_Bind` Postfix 绑定到新实体。捕获条件不匹配则什么都不做（原版行为）。新纯函数 `QualityMath.InstantBuildPerItem(totalInc, count)`。
- **Bug 3**：`QualityData` 新增 `pickedSinceTick[]` 侧表 + `NoteProductPicked/ConsumeProductPicked`（`ClearEntity` 同步清零）；`P0_PickFrom` 在成功取货后记账；`P0_Assembler.Postfix` 改为 `netProduced = Σ(now - previous)`，`producedThisCall = QualityMath.ProducedDelta(netProduced, picks)`（纯函数，`picks>0 ? max(0, net+picks) : max(0, net)`）。产物件数、增产概率、耗电、`produced[]`/`productRegister` 语义均不变；`DSPQS_DEV=1` 下会打印 `[QSystem][assembler] pool credited produced=… net=… picked=… pooled=…`。
- **无新增玩家 cfg 键**（仍是 16 个）；三处修复都不写存档结构（`pickedSinceTick` 是运行时侧表；立即建造只写既有的 prebuild→building 绑定表）。

### 27.26.4 文件

- 新增：`Patches\P0_MinerCollector.cs`（约 190 行）、`Patches\P1_InstantBuild.cs`（约 230 行）、`tests\OfflineTests\Checks.P5Miner.cs`（2 条检查）、`tests\OfflineTests\Checks.P5Fix.cs`（2 条检查）。
- 修改：`QualityMath.cs`（`IncPerItemMax`、`StationIncAfterTransfer`、`ProducedDelta`、`InstantBuildPerItem`）、`QualityData.cs`（`pickedSinceTick` 侧表 + 3 个新方法、`ClearEntity`）、`Patches\P0_Assembler.cs`（净增量 + 取货补偿 + 末位 `DSPQS_DEV` 日志）、`Patches\P0_PickFrom.cs`（取货记账）、`Plugin.cs`（注册 `InstantBuildCapture` + `[QSystem][build]` 日志、`VERSION = "0.16.0"`）、`Diag\StartupAudit.cs`（3 个新反射自检 + `InstantBuildCapture.Describe()`）、`DSPQualitySystem.csproj`（`<Version>0.16.0</Version>`）、`tests\OfflineTests\OfflineTests.csproj`、`tests\OfflineTests\Program.cs`（54 → 59 检查）。
- 未改：`Save\`（存档尾块 schema 不变）、`P3_Speed.cs`（速度路径未动）、cfg 16 键与中文描述。

### 27.26.5 构建 / 测试

- `build.ps1`：`BUILD OK`，**0 警告 0 错误**（首轮曾因 HarmonyX 特性不能写 `ref` 参数类型报 `CS0182`×3，改为手动 `harmony.Patch` 后通过）。
- 离线（先删 `tests\OfflineTests\bin|obj`）：**`ALL PASS (59 checks)` / `TEST PASS`**（54 → 59：新增 `CheckMinerCollectorInc`、`CheckMinerCollectorRollBounds`、`CheckProductDelta`、`CheckInstantBuildPerItem`；旧检查无退化）。
- 产物：`bin\Release\DSPQualitySystem.dll` = **225,280 B**，SHA256 **`8CB2CB75C39E992D01A1A4F623E090D8F5D39BB63AEF800598DD08128358A99B`**，AssemblyVersion 0.16.0.0。
- `patchedMethods` 预期：启动 **17 → 18**（本版新增一个属性补丁 `StationComponent.UpdateVeinCollection`，落在 PatchAll 计数内）；运行时默认 **45 → 47**（新增 `StationComponent.UpdateVeinCollection` 属性补丁 1 个 + `PlanetFactory.BuildFinally` 手动补丁 1 个；`StorageComponent.TakeTailItems` 原本已被 `P4_ConsumePriority` 手动补丁覆盖，只加 processor 不新增方法计数）。以启动日志实测为准。

### 27.26.6 实机核对点（Lead 执行）

1. 版本与启动：`[QSystem] === DSP Quality System v0.16.0 ===`、`patchedMethods=18`（PatchAll 段，比 v0.15.0 多一个 `StationComponent.UpdateVeinCollection`）且后续日志出现 `[QSystem][build] instant-build quality capture (…)` 与 `[QSystem][miner] vein-collector quality hook (…)`。
2. **Bug 1**：放一台大型采矿机（2316），用 4 点/10 点物品建造 → `miner <eid>` 看 `quality … modApi=GetBuildingInc` 非 0；让矿脉产出搬运几次 → 首次出现 `[QSystem][miner] vein-collector quality path active: p=… e=… added=… perItem=… inc=X->Y`；把站点储物格的矿取出用 tooltip/角标看点数 >0；对照小型矿机行为不变。速度：用 `miner <eid>` 记录 `time` 增量或观察产出速率，确认采集站矿机也受品质速度加成（若仍无差异，需报回具体数值，本版未改速度路径）。
3. **Bug 2**：用 4 点物品（背包里该物品 `inc/堆叠数`=4）对准幽灵建筑按住 Shift 立即建造 → 日志 `[QSystem][build] instant-build quality captured: prebuild=… item=… count=… inc=… perItem=4 bound=…`；对建成的建筑 `F9`/`miner <eid>` 查 `GetBuildingInc`=4；换 0 点物品重复 → 落成 0 点（不得凭空加点）。
4. **Bug 3**：给装配机喂 4 点原料、开增产模式，等数批后取产物 → tooltip/角标显示点数 >0，尤其检查**增产额外产物**（同一产物格中额外产出的那部分不再固定 0 点）；`DSPQS_DEV=1` 时看 `[QSystem][assembler] pool credited … picked=…` 确认取货补偿在工作；总产量/耗电与原版一致。
5. 回归：普通手持建造、蓝图粘贴（含 Ctrl+C/V 复制粘贴）、小型矿机、拆除返还、存读档后品质池仍正常。
6. 卸载/关闭：本版没有新增 cfg 开关；`itemQualityBadges=false`、`speedBonus=false` 等既有开关仍要与原版逐字节等价（补丁不注册或空转）。

### 27.26.7 版本链

- v0.15.0 `0A9DCD9A…` 213,504 B（55 checks）→ **v0.16.0 `8CB2CB75…` 225,280 B（59 checks）**。

## 27.27 v0.16.1：分流器「输出口品质过滤」+ 分拣器「品质过滤」（2026-10-30）

### 27.27.1 动机（Lead m00756 转述的用户口径）

> 四向分流器不符合设计预期，无法控制让有品质的物品从某个特定输出口流出，让无品质或低品质物品从另外输出口流出，操作按钮应放置在四向分流器的过滤器界面，可以从这个界面指定高于某品质点数的某个物品，同时这个过滤器界面应还能作用于分拣器。

即 v0.13.0–v0.13.2 的「品质优先」（按输入 inc 降序置换 `input0..3`，让原版 first-fit 先花掉高品质货）**语义不成立**：它只能改变「先处理哪条输入」，无法把「某物品且品质 ≥N」定向送到指定输出口，也不能作用于分拣器。本版按用户口径**重做 C-2**（输出侧过滤）并新增 C-3（分拣器过滤）。

### 27.27.2 原版锚点与根因（`_analysis\quality-system\asm_full.cs` 行号）

- **原版没有「每口过滤」**：`SplitterComponent`（:72695）只有一个全局 `outFilter`；`UISplitterWindow.OnItemPickerReturn`（:525343）写的就是 `traffic.splitterPool[splitterId].outFilter = item?.ID ?? 0`。四个过滤圈共用同一个值，所以「每口一个物品 + 阈值」无法用原版字段表达，必须接管分配循环。
- **分配循环**：`CargoTraffic.UpdateSplitter(ref SplitterComponent)`（:82854）与并行版 `UpdateSplitterAsync`（:83208）。两者都是 public、单 `ref SplitterComponent` 参数（HarmonyX 特性写不了 ref 参数类型，用 `harmony.Patch` 手动传 `MethodInfo`）。循环结构：`CheckPriorityPreset()` → 早退判断 → 按 `input0..3` 顺序 `GetCargoIdAtRear()` 收集最多 4 件尾部货 → `while` 循环里对每件货按 `outFilter` 匹配与 `TestBlankAtHead()/pathLength>10` 选输出 → `TryPickCargoAtEnd()` + `InsertCargoAtHeadDirect()` + `InputAlternate()/OutputAlternate()`，最后才是 `topId` 储物仓回灌分支。**在 Prefix 里复刻前两段（收集 + 选口 + 移动）并插入规则路由，`topId` 分支保持让路即可。**
- **输出口身份**：`output0..3` 是**轮转顺序**不是物理口，物理口靠 `beltA..D` 的 beltId 匹配（`GetSlotBelt(slot)` :72730 区）；`OutputAlternate`（:73190 区）只在 `outPriority` 时锁 output0。⇒ 规则按物理 port 存，运行时把 port→beltId→`output0..3` 里的槽位换算（`QualityMath.FindOutputIndex`）。
- **其它陷阱**：`CargoTraffic.GetCargoPath(int)`（:82238）卫语句写成 `if (id <= 0 && id >= pathCursor)`（应为 `||` 的笔误），调用前必须自己校验 `beltPool/pathPool` 下标；`CargoPath.pathLength` 是 public 属性（:27075），可用于过滤 `pathLength > 10`。
- **分拣器**：`InserterComponent.InternalUpdate`（:54218）/`InternalUpdate_Bidirectional`（:54458）/`InternalUpdateNoAnim`（:54730）在抓货时调用 `factory.PickFrom(pickTargetTypedId, pickOffset, filter, …)`（:54252/:54290），最终落到 `CargoTraffic.TryPickItem`（:83650 带 filter、:83659 needs 版；`filter==0` 时走无过滤重载 :83656）。`CargoPath.TryPickItem`（:28858/:28905）**只比较 `item == filter`，读出的 `inc` 从不判断** ⇒ 品质阈值必须在这一层接管。`grade`（:54076 `prefabDesc.inserterGrade`）是建筑 Mk 等级，与品质无关（原版 `filterIncs[0..2]` 只是显示 Mk 等级）。
- **分拣器 UI**：`UIInserterWindow`（:499506）已有 `filterButton`（`UIButton`，`onRightClick` 是 `Action<int>`）、`filterText`、`filterIncs`；`OnSelectFilterClick`（:499862）用 `UIItemPicker.Popup`（:426232）选物品，`OnItemPickerReturn`（:499877）写 `inserter.filter`。⇒ 沿用原版物品过滤，只叠加「阈值」侧表 + Shift 右键手势，零新资产。
- **分流器 UI**：`UISplitterWindow`（:524070）四个过滤圈只在 `outPriority && outFilter != 0` 时才显示（`_OnUpdate` :524396 区），普通输出口用 `SetCircleOutput`。⇒ 直接在窗口里挂一组自建行控件（原版面板空间足够：type0/2 = 390×438、type1 = 420×588，`OnSplitterIdChange` :525519）。

### 27.27.3 设计

**C-2 数据模型**：规则按「物理输出口」存，packed int：`itemId & 0xFFFF | (min << 16)`，`0` = 无规则，`min` 取值 0–10（0 = 不限制物品，即「该口只收该物品」仍生效；物品 0 = 任意物品）。每台分流器 4 条，放在独立侧表 `QualityData.QualityFilters.cs`（不写进 `PlanetQuality` 的旧数组，P1 约束；对外 API 稳定，日后可搬迁）。

**C-2 运行时**：`P5_SplitterFilter.UpdatePrefix` 在 `UpdateSplitter/UpdateSplitterAsync` 前接管：
1. 让路（返回 true 走原版）：`sp.id/entityId <= 0`、`inPriority || outPriority || outFilter != 0 || prioritySlotPresets != 0`、`sp.topId != 0`（上方储物仓分支）、`planet == null`、该分流器无任何规则、`input0 == 0`。
2. 复刻收集：按 `input0..3` 顺序取 `GetCargoIdAtRear()`（每线程 `[ThreadStatic]` 缓冲，并行 `UpdateSplitterAsync` 安全）。
3. 每件货调 `QualityMath.PlanSplitterRoute`：候选 = ①命中规则的输出口（min 降序、同 min 按 port 升序）②**无规则**的输出口按原版 `output0..3` 顺序兜底；规则口不参与兜底。
4. 按候选顺序找第一个「`pathLength > 10` 且 `TestBlankAtHead() >= 0`」的口，`TryPickCargoAtEnd()` + `InsertCargoAtHeadDirect()` + `InputAlternate/OutputAlternate`；都不行就留给 `topId` 分支或原地不动（不销毁）。
5. 成功后 `__0 = sp; return false`（完全接管）；任何异常 `WarnOnce` + 返回 true 回原版。

**C-2 UI（方案 B，Lead 指定）**：`P5_SplitterFilterUi` 在 `UISplitterWindow` 内挂一组自建控件：底部「品质过滤 ▸/▾」折叠标题 + 4 行（`口 A/B/C/D` + 物品按钮 + `≥N` 按钮 + 清除按钮），全部英文物件名（`QSystemSplitterQualityPanel` / `QSystemSplitterOut{0..3}Rule|Item|Min|Clear`、`QSystemSplitterQualityToggle`）便于 `uitree` 核对；物品按钮左键 `UIItemPicker.Popup`、右键设 0（任意物品）；阈值按钮左键 `NextQualityMin`、右键 `PrevQualityMin`；清除清该口规则；折叠状态默认收起，展开时按行数把 `windowTrans.sizeDelta.y` 增高、收起复原。每帧由 `_OnUpdate` Postfix 同步（窗口换机/关闭时自动重建/销毁）。

**C-3 运行时**：`P5_SorterFilter` 用 `[ThreadStatic] int _ctxMin` 传递「当前分拣器阈值」——三个 `InternalUpdate*` Prefix 读侧表设 `_ctxMin`、Postfix 清 0；`CargoTraffic.TryPickItem` 两个重载 Prefix 在 `_ctxMin > 0` 时接管：自校验 belt/path、`lock(path.buffer)` 后按原版步进扫描抓取窗口（`pivotOnPath + offset - 2`，长度 5），收集候选后 `QualityMath.PlanSorterPick` 选第一件「物品匹配 + `inc >= min`」（needs 版同时校验 needs），`RemoveCargoAtIndexDirect`（:28695，public、不加锁、自带 `updateLen` 维护）移除并写回 `stack/inc/__result`；无合格货 ⇒ `__result = 0; stack = 1; inc = 0`（拦截，不消耗）。**明确不覆盖 `CargoPath.TryPickFuel`（:28804）** ⇒ 发电厂燃料抓取不受影响（写进已知限制）。

**C-3 UI**：沿用原版 `filterButton` 选物品，`_OnRegEvent`/`_OnUnregEvent` Postfix 成对挂/卸 `onRightClick`（`Action<int>`）：Shift+右键循环阈值、Ctrl+Shift+右键清 0；`_OnUpdate` Postfix 把 `filterText` 改成「物品名　品质≥N」；`OnResetFilterButtonClick` Postfix 清阈值。

**存档**：每星球节点新增 `splittersV2:[{"e":eid,"o":[{"p":port,"i":itemId,"m":min}]}]`（只写非空规则）与 `sorters:[{"e":eid,"m":min}]`（只写 min>0）。**不复用旧的 `splitters` 节点**：v0.15 及更早的读取器会把它当成布尔勾选，混用会串语义；旧 `splitters` 节点读档时忽略 + 一次性日志 `legacy 'splitters' node ignored (pre-v0.16.1 quality-priority flag)`。`SaveGuard.SchemaVersion` 保持 1（加字段不升版本：旧档缺字段 = 空，新档在旧 DLL 里被忽略）。

**cfg**：**没有新增玩家键**（仍 16 个）。v0.15.0 的 `SplitterQualityPriorityValue` 改为 `SplitterQualityFilterValue = true`，并新增 `SorterQualityFilterValue = true`；`QualityConfig.SplitterQualityFilterEnabled` / `SorterQualityFilterEnabled` 由总开关 `Enabled` 兜底。0 条规则 / 阈值 0 = 原版行为（分流器 Prefix 发现无规则直接回原版；分拣器 `_ctxMin == 0` 直接回原版）。

### 27.27.4 文件

- **新增**：`Patches\P5_SplitterFilter.cs`（约 500 行）、`Patches\P5_SplitterFilterUi.cs`（约 560 行）、`Patches\P5_SorterFilter.cs`（约 480 行）、`Patches\P5_SorterFilterUi.cs`（约 390 行）、`QualityMath.SplitterFilter.cs`（P1，362 行；本版追加 4 个 UI 文案纯函数）、`QualityData.QualityFilters.cs`（P1，499 行）、`tests\OfflineTests\Checks.SplitterFilter.cs`（P1，242 行）、`tests\OfflineTests\Checks.P5QualityFilter.cs`（265 行，10 条检查）。
- **修改**：`QualityData.cs`（删 `splitterPriority` 数组；`RemovePlanet`/`ClearAll`/`ClearEntity` 接 `ClearFiltersForPlanet`/`ClearAllFilters`/`ClearFilterEntity`）、`QualityData.Storage.cs`（删旧分流器访问器）、`QualityConfig.cs`（两个新内置常量/属性）、`Save\QualitySave.cs`（`splittersV2`/`sorters` 读写 + `sorterCount` + 旧节点忽略日志）、`Patches\P1_SaveHooks.cs`（读档/新游戏 reset 改为两个新补丁类）、`Plugin.cs`（`VERSION = "0.16.1"`、注册两个新 patch + Describe、删 old splitter Apply/HandleHotkey、LogConfig 改两个新常量）、`Diag\StartupAudit.cs`（分拣器三条 tick + 两个 `TryPickItem` 重载 + `UIInserterWindow` 方法反射自检 + 新 Describe）、`DSPQualitySystem.csproj`（`<Version>0.16.1</Version>`）、`tests\OfflineTests\OfflineTests.csproj`、`tests\OfflineTests\Program.cs`（59 → 66 条：替换 9 条旧分流器检查为 16 条新语义检查）。
- **删除**：`Patches\P5_SplitterPriority.cs`、`Patches\P5_SplitterPriorityUi.cs`、`QualityMath.Splitter.cs`、`tests\OfflineTests\Checks.P5Splitter.cs`、`tests\OfflineTests\Checks.P5SplitterUi.cs`（v0.13 的「品质优先」整套退役；`F3` 热键、标题后缀、面板勾选框随之删除）。
- **未改**：`Save\SaveGuard.cs`（schema 保持 1）、`Save\JsonLite.cs`、`QualityBlobCodec.cs`、`.ps1` 脚本、`dist\`、`_handoff-20261026\`、`_analysis\`。

### 27.27.5 构建 / 测试

- `build.ps1`：`BUILD OK`，**0 警告 0 错误**（首轮曾因 `UIButton.onRightClick` 是 `Action<int>`、`DebugProbes` 已改名为 `DevMode`、`_inPaths` 缓冲类型写错、`sorterCount` 声明/调用点漏改等报错，逐项修完后通过）。
- 离线（先删 `tests\OfflineTests\bin|obj`）：**`ALL PASS (66 checks)` / `TEST PASS`**（59 → 66：删 9 条旧分流器检查、加 16 条 = 6 条 P1 纯函数 + 10 条新语义/持久化/UI 文案检查；旧功能检查无退化）。
- 新增离线检查：规则 pack/match/route/outputIndex/阈值循环/sorter pick、分流器「无规则=原版顺序」「最严阈值优先」「规则口不参与兜底」「规则口非输出被忽略」「四口全规则时无匹配=留原地」、分拣器 needs 叠加、v0.16.1 尾块 JSON 往返（`splittersV2`/`sorters` 字段级断言）、v0.15 旧档（旧 `splitters` 节点）可读且不产生新节点、UI 文案纯函数。
- 产物：`bin\Release\DSPQualitySystem.dll` = **235,520 B**，SHA256 **`1B8F7FFD96EA264988F26758E020A96BC8C6B8C77B570E30E93D61363701DA2E`**，AssemblyVersion 0.16.1.0。
- `patchedMethods` 预期：启动 **18**（PatchAll 段不变，本版新增补丁全部手动注册）；运行时默认 **47 → 56**（净 +9：删掉旧分流器 3 个手动目标 `CargoTraffic.UpdateSplitter`/`UpdateSplitterAsync`、`UISplitterWindow._OnOpen`；新增 12 个不同方法——分流器 `CargoTraffic.UpdateSplitter`/`UpdateSplitterAsync`、`UISplitterWindow._OnUpdate`；分拣器 `InserterComponent.InternalUpdate`/`InternalUpdate_Bidirectional`/`InternalUpdateNoAnim`、`CargoTraffic.TryPickItem` 两个重载；分拣器 UI `UIInserterWindow._OnUpdate`/`_OnRegEvent`/`_OnUnregEvent`/`OnResetFilterButtonClick`）。以启动日志实测为准。

### 27.27.6 实机核对点（Lead 执行）

1. 版本与启动：`[QSystem] === DSP Quality System v0.16.1 ===`；`patchedMethods=18`；出现 `[QSystem][splitterfilter] patch registered` 与 `[QSystem][sorterfilter] patch registered`（含 hotkey/UI 说明）。
2. 分流器面板：打开一台四向分流器 → 面板底部出现「品质过滤 ▸」折叠标题；点开出现 A/B/C/D 四行，且**不与四个过滤圈/图例/标题重叠**（`uitree` 可查 `QSystemSplitterQualityPanel`/`QSystemSplitterOut0Rule`… 的坐标）。
3. 分流器规则生效：口 A 设「物品=磁铁、≥5」，口 B 不设规则；输入混流 0 点与 8 点磁铁 → 8 点磁铁全部从 A 出、0 点磁铁从 B（或其它的无规则口）出；把阈值改 9 → 8 点也回到无规则口。
4. 分流器让路：同一条分流器上打开原版「输入优先级」预设（或设了原版过滤器图标）→ 规则暂不生效、日志出现 `paused …`；取消后规则恢复。
5. 分流器持久化：设好规则 → 存盘 → 退出重进读档 → 面板里规则仍在（尾块 `splittersV2` 节点存在）。
6. 同一物品多档：口 A `≥8`、口 B `≥5` → 9 点去 A、6 点去 B、3 点去无规则口。
7. 分拣器：给分拣器设物品=磁铁 + Shift 右键把阈值调到 5；面板文字显示「磁铁　品质≥5」；输入 0 点与 6 点磁铁混流 → 只抓 6 点；把阈值设为 0 → 恢复原版抓取。
8. 分拣器边界：发电厂燃料抓取不受阈值影响；Ctrl+Shift+右键清零生效并立即刷新文字。
9. 分拣器持久化：存读档后阈值仍在（尾块 `sorters` 节点）。
10. 旧档兼容：读 v0.15/v0.13 存档（含旧 `splitters` 布尔节点）→ 正常进入游戏，日志出现一次 `legacy 'splitters' node ignored`，无报错、无串档。
11. 卸载/关闭等价：本版没有新增玩家 cfg 开关；0 条规则时分流器与分拣器行为与原版一致（可对照同一台机器设/清规则）。
12. 回归：储物仓品质格数、物品角标、A 类五件套、大型采矿机、量产、拆除返还、分拣器原版过滤（不设阈值时）均无退化；`F3` 已不再是热键（按下无反应）。

### 27.27.7 版本链

- v0.16.0 `8CB2CB75…` 225,280 B（59 checks）→ **v0.16.1 `1B8F7FFD…` 235,520 B（66 checks）**。
- v0.16.1 Lead hotfix 后最终 **`ECF9AE7B10DCF8F2FABD40B61F9E2DB367EE895DDD5B7229BACD9A4150C22553` / 237,056 B（66 checks）**，见 §27.28。

## 27.28 v0.16.1 Lead 实机复核：过滤 UI 未接线 + 面板布局修正

### 27.28.1 现场

- 部署 C-pack 的 235,520 B 版本启动：横幅 `=== DSP Quality System v0.16.1 ===`、`patchedMethods=18`，两条功能日志都 `armed`（`splitterfilter armed (methods=2, routed=0)`、`sorterfilter armed (methods=5, picked=0)`）。
- harness v11 的 `splitterrule 139 <port> <item> <min>` CRUD 全通（`packed` 回读一致），`filtercount` 报 `totalRules=2`。但 `open splitter 139` 后 `uitreeall QSystem` → `matched=0`；`[Audit]` 里分流器报 `ui idle`、分拣器报 `NOT ARMED (ui not applied)`。

### 27.28.2 根因（两个）

1. **UI 死代码**：`Patches\P5_SplitterFilterUi.cs` 只定义了 `Sync(UISplitterWindow)`，既没有 `Apply(Harmony)`，也没有任何地方给 `UISplitterWindow._OnUpdate` 注册 postfix；`P5_SorterFilterUi.Apply` 虽然存在，但 `Plugin.cs` 从未调用。⇒ 规则数据与路由补丁都在跑，玩家却找不到任何输入入口。
2. **布局缺陷**（接线后暴露）：折叠态只显示「品质过滤 ▸ (n/4 口有规则)」一行，但它锚在窗口最底边，正好压在原版圆形窗口的底边框/装饰条上，2560×1600 下几乎被裁掉一半；展开态则依赖折叠态的高度，窗口不增高时 4 行会溢出到窗口外。

### 27.28.3 修复（全部在 Lead 侧，未改 C-pack 的算法）

- `P5_SplitterFilterUi`：新增 `Apply(Harmony)`（反射取 `UISplitterWindow._OnUpdate` 实例方法、注册 `UpdatePostfix` → 调 `Sync`；成功日志 `UI patch registered: UISplitterWindow._OnUpdate postfix (QSystemSplitterQualityPanel)`；失败只 `WarnOnce` 并保持 `NOT ARMED (ui …)`），新增 `_armed/_reason/Armed/Describe()`；`Plugin.cs` 在 `P5_SplitterFilter.Apply` 后调用它并打印 `Describe()`，在 `P5_SorterFilter.Apply` 后同样调用 `P5_SorterFilterUi.Apply`。
- 布局：`ApplyHeight` 改为**恒定把窗口增高**——折叠态 `_baseHeight + HeaderHeight + Margin*2 + BottomInset`、展开态 `_baseHeight + panelHeight + Margin + BottomInset`（`_baseHeight` 取原版窗口高度，过小时回落 438）；新增 `const float BottomInset = 26f` 并把面板 `anchoredPosition` 抬到 `(Margin+4, Margin+BottomInset)`；新增 `_lastSplitterId`，分流器类型切换（0/1 两种原版布局高度不同）时重新捕获 `_baseHeight`，避免按旧基准把窗口压小。

### 27.28.4 构建与实机验证

- 中间态 1：`8B816D89…` 237,056 B / 66 checks（接线）→ 实机确认两条 `UI patch registered` 日志、`uitreeall QSystemSplitter` 出现 `QSystemSplitterQualityPanel pos=12,8 size=366,24` 与 4 行 `QSystemSplitterOut{0..3}Rule`（折叠时 `active=false`），截图 `_live\v0161-splitter-ui.png` —— 面板已建但头部压底边框。
- 中间态 2：`820D5F89…` 237,056 B（窗口恒定增高）→ 截图 `_live\v0161-ui2.png`：头部完整但**仍贴底边框**。
- 最终：**`ECF9AE7B10DCF8F2FABD40B61F9E2DB367EE895DDD5B7229BACD9A4150C22553` / 237,056 B（66 checks）** → 截图 `_live\v0161-ui3.png` + 裁剪 `v0161-window.png`：头部「品质过滤 ▸ (2/4 口有规则)」完整落在窗口内、离底边框约 26 px，可读。
- 持久化实机：`splitterrule 139 0 1001 3` + `splitterrule 139 1 1102 6` → `save QSTest` → `loadfull QSTest` → `filtercount` 仍 `totalRules=2`、`splitterrules 139` 回读 `port=0 铁矿 min=3` / `port=1 磁铁 min=6` ⇒ 尾块 `splittersV2` 往返 PASS。
- 展开态与行布局（harness v12 新增 `splitteruiexpand`/`uitreewin`/`sorteruiopen`）：展开后 `QSystemSplitterQualityPanel` 由 24 px 变 124 px，4 行 `QSystemSplitterOut{0..3}Rule` 全部 `active=true`（口 A/B/C/D + 物品按钮 + `≥N` 阈值按钮 + 清空按钮，实测文本「口A」「口B」…）；初版把行向下排、并试图用 `windowTrans.sizeDelta` 增高窗口，但原版分流器边框是**固定尺寸装饰图**、不随 sizeDelta 拉伸 ⇒ 行溢出到窗口外（截图 `_live\v0161-exp-good.png`）。最终改为**行从头部向上堆叠**、窗口高度完全不动（`ApplyHeight` 只调面板自身高度），并把口序反转为 A 在上（`-(HeaderHeight + (PortCount-1-port)*RowHeight)`）。
- 最终实机截图：折叠 `_live\v0161-ui3.png`（头部在框内、距底边 26 px）、展开 `_live\v0161-exp3.png`（4 行在框内、A→D 自上而下、头部「品质过滤 ▸ (2/4 口有规则)」在行下方）；分拣器 `_live\v0161-sorter2.png`：原版分拣器窗口的过滤器按钮下方显示 `品质≥6`。
- 分拣器阈值实机：`place 2014 -90 -150 0` → eid=125；`sorterrule 125 6` → `inserterId=15 min=0 none=true set=6 back=6 ok=true`；`sorteruiopen 125` → `ok=true title=集装分拣器 filterText=设置筛选器`、截图 UI 上按钮下方为 `品质≥6` ⇒ 阈值读取与文案刷新 PASS（Shift+右键真实点击未验，离屏无输入注入）。

### 27.28.5 未覆盖（进 TESTLOG）

- 展开态 4 行 UI 的真实点击（物品选择器、阈值 +/-、清除）、分拣器窗口按钮文案与 Shift+右键阈值、真实多口物流下「规则命中/兜底/堵带」行为——离屏实例没有键鼠注入，需要用户实机点一遍或用后续 harness 命令补。
- `patchedMethods` 运行时实测值（预期 56）未逐项核对。


## 27.29 v0.17.0：品质过滤编辑器迁入原版物品选取器（UI 重构 + 让路条件放宽）

### 27.29.1 由来

v0.16.1 把分流器/分拣器的品质过滤 UI 做成了「窗口内面板 + Shift 手势」，用户实机否决两条：

1. 分流器窗口里的「品质过滤」面板与按钮**点不到**；
2. 用户期望的入口不是分流器窗口，而是原版那个「物品选取」过滤器弹窗（截图即 `UIItemPicker`）——要在**那个界面**里设置「品质分流开关 + 最低品质筛选」。

用户口径（转述）：「操作按钮应放置在四向分流器的过滤器界面，可以从这个界面指定高于某品质点数的某个物品，同时这个过滤器界面应还能作用于分拣器。」本版把整套编辑器**整体搬进原版物品选取器底部一行**，并删除分流器窗口里的旧面板。同批的「大矿机点数丢失 / Shift 立即建造 0 点」由另一路修复，不在本版范围。

### 27.29.2 原版锚点与「点不到」根因（`_analysis\quality-system\asm_full.cs`）

- `public class UIItemPicker : ManualBehaviour` **:426160**：`pickerTrans` :426171、`typeButton1/2`、`public System.Action<ItemProto> onReturn`；
  - `public static void Popup(Vector2 pos, Action<ItemProto> _onReturn)` **:426232**：占位入口，若 `!inited || active` 则直接 `_onReturn?.Invoke(null)`（**不打开**）；
  - `_OnOpen()` :426333 区：清缓冲、`OnTypeButtonClick/SetBufferData/SetMaterialProps`、`selectedProto = null`；
  - `_OnClose()`：`if (onReturn != null) { onReturn(selectedProto); onReturn = null; }`（选物品/取消/点窗外都走这里，取消时 `selectedProto == null`）；
  - `_OnUpdate()`：用 `RectTransformUtility.RectangleContainsScreenPoint(pickerTrans, Input.mousePosition, UIRoot.instance.overlayCanvas.worldCamera)` 判「点窗外关闭」⇒ **新增控件必须落在 `pickerTrans` 矩形内，否则点控件会被判成点窗外、picker 直接关掉**。
- 两个入口：`UISplitterWindow.OnCircleFilterClick(int slot)` **:525309**（`UIItemPicker.Popup(windowTrans.anchoredPosition + new Vector2(-305f,225f), OnItemPickerReturn)` 在 :525319）；`UIInserterWindow.OnSelectFilterClick()` **:499862**（同类 Popup，偏移 `(-300f,235f)`）。两者原本的回调只写 `outFilter` / `inserter.filter`。
- 点不到根因（v0.16.1 面板）：面板挂在**分流器窗口**标题父节点下（原版窗口边框是固定尺寸装饰图，`windowTrans.sizeDelta` 拉不高），一行行控件还被挤到窗口可视区外；交互只依赖自定义 `IPointerClickHandler`、部分新建 GameObject 的 `layer` 不是 UI(5)、可见图形未设 `raycastTarget` ⇒ uGUI 射线打不到。本版从三方面根治：挂到 `pickerTrans` 内部、`layer=5`、用真正的 `UnityEngine.UI.Button` + `Image.raycastTarget=true`。

### 27.29.3 设计

- **单一 footer 行**（`QSystemFilterPickerRow`）：`UIItemPicker.Popup` Postfix 里，若上下文有效，则在 `pickerTrans` 底部居中建一行：
  - 分流器模式：`口A 品质分流` + `[开/关]` 按钮 + `物品: xxx` 标签 + `[-]  ≥N  [+]`；
  - 分拣器模式：`分拣器 品质阈值` + `[-]  ≥N  [+]`。
- **上下文捕获**：`UISplitterWindow.OnCircleFilterClick(int slot)` Prefix 记 `(planetId, splitterEntityId, slot)`；`UIInserterWindow.OnSelectFilterClick()` Prefix 记 `(planetId, inserterEntityId)`；`Popup` Postfix 消费上下文建行；`UIItemPicker._OnClose` Prefix 销毁视觉、**Postfix** 清上下文（`_OnClose` 本体要调用 `onReturn`，包装器写规则时上下文必须还在）。
- **onReturn 包装**：`WrapReturn` 先写我们的规则（item 用用户点选的 `ItemProto.ID`，min 用 footer 值），再链式调用原 delegate ⇒ 原版 `outFilter`/`inserter.filter` 照旧生效，互不覆盖。
- **交互**：开关点一下 = 切换 `_on` 并立刻写/清规则；`[-]`/`[+]` = `NextQualityMin`/`PrevQualityMin` 循环 0–10（0 = 不限制），分流器模式改阈值会自动 `_on = true` 并写规则；分拣器模式直接 `SetSorterMin`（min<=0 → `ClearSorterMin`）。
- **点击可靠性**：全部 `gameObject.layer = 5`；可点元素是 `Image(raycastTarget=true)` + `UnityEngine.UI.Button`（`targetGraphic`、`transition = None`、`onClick.AddListener`）；所有 `Text.raycastTarget = false`；footer 背景 `raycastTarget = true` 吸收误点；父级 = `picker.pickerTrans`。
- **让路条件放宽**：v0.16.1 只要 `outPriority` 或 `outFilter != 0` 就整台分流器让路——但原版**过滤圆必须先设成输出优先级才会显示**，那样新 UI 就成了死路。本版只保留 `inPriority`、`prioritySlotPresets != 0`、`topId != 0` 让路；只设了输出优先级/全局过滤的分流器仍由本 Mod 接管，未命中任何规则的货物按原版 `outFilter` 语义兜底（新增 `TryVanillaFallback`/`TryMoveVanilla` 复刻原版 first-fit：`outFilter == 0` → output0 失败再 output1..3；`item == outFilter` → 仅 output0；否则 → output1..3）。
- 已知行为差异：原版在 `sp.output0 == 0` 时直接 return，本版仍会尝试其它规则口（更宽松，只在"输出口 0 未连"这种非典型布置下可观察）。

### 27.29.4 文件

- 新增 `Patches\P5_QualityFilterPicker.cs`（上下文捕获 + picker footer 编辑器，约 700 行）。
- 删除 `Patches\P5_SplitterFilterUi.cs`（分流器窗口内旧面板整份移除，含 `QSystemSplitter*` 控件）。
- 修改 `Patches\P5_SplitterFilter.cs`（让路条件 + 原版兜底）、`Plugin.cs`（`VERSION = "0.17.0"`、`P5_QualityFilterPicker` 的 Apply/日志、删旧 UI Apply）、`Diag\StartupAudit.cs`（Describe 换新类；新增 `UIItemPicker.Popup`、`UIItemPicker._OnClose`、`UISplitterWindow.OnCircleFilterClick`、`UIInserterWindow.OnSelectFilterClick` 四条反射自检）、`DSPQualitySystem.csproj`（`<Version>0.17.0</Version>`）、README/DEVLOG/TESTLOG。

### 27.29.5 构建与测试

- `build.ps1`：`BUILD OK`，0 警告 0 错误。
- 删 `tests\OfflineTests\bin|obj` 后 `tests\run-tests.ps1`：`ALL PASS (66 checks)` / `TEST PASS`（离线检查全部沿用 v0.16.1 的纯函数/存档用例，未受影响）。
- DLL **238,592 字节**，SHA256 **`F71768BE27D49C8FFEA2F2AD170FFA4EB0F6608C1ACEBACE0DB2A699CDC31854`**，AssemblyVersion **0.17.0.0**。
- `patchedMethods` 预期：启动 **18 → 21**（-1 旧 `UISplitterWindow._OnUpdate`，+4 新：`UISplitterWindow.OnCircleFilterClick`、`UIInserterWindow.OnSelectFilterClick`、`UIItemPicker.Popup`、`UIItemPicker._OnClose`）；运行时 **56 → 59**。
- 存档与配置不变：`splittersV2`（逐口 `p`/`i`/`m`）、`sorters`（`e`/`m`），`SaveGuard.SchemaVersion` 仍 1，旧档可读；玩家 cfg 仍 16 键（本版不新增/不删除玩家键）。
- 踩坑：`UIItemPicker.Popup` 是 **static** 方法且带 `Action<ItemProto>` 参数，Harmony 手动 Patch 需要 `GetMethod("Popup", Public|NonPublic|Static, null, new[]{typeof(Vector2), typeof(Action<ItemProto>)}, null)`；`_OnClose` 必须在 Postfix 而不是 Prefix 清上下文（否则包装器拿不到 `_kind`）。

### 27.29.6 实机核对点（Lead 执行）

1. 启动日志 `[QSystem][filterpicker] armed (vanilla item picker footer; …)`，且 `[Audit] filterpicker -> …` 无 MISSING。
2. 分流器：点过滤圆 → 物品选取器打开，底部出现 `口A 品质分流 [关] 物品: 任意物品 [-] 不限 [+]`。
3. 点开关 → 变 `[开]`、日志出现规则写入；点 `[+]`×3 → `≥3`；在网格里点铁矿 → 物品变「铁矿石」、规则 = 铁矿 ≥3。
4. 关闭 picker 再重开同一口 → footer 反映已存规则（开 + 物品 + 阈值）。
5. 物流验证：上游混品质铁矿、规则「口A 铁矿 ≥3」，高品走 A 口、低品走其它无规则口；`[QSystem][splitterfilter] routed=` 递增。
6. 分流器设了原版输出优先级/全局过滤仍能生效（不再整体 paused）。
7. 分拣器：点过滤按钮 → picker 底部出现 `分拣器 品质阈值 [-] 不限 [+]`；`[+]`×5 → `≥5`；选物品后阈值保留。
8. 分拣器物流：皮带混 0/5 点同物品，setter 只抓 ≥5 的；`[QSystem][sorterfilter] picked/blocked` 增长。
9. 存档往返：设 2 口规则 + 1 个分拣器阈值 → `save` → `loadfull` → footer 回读一致、`[QSystem][save] … splitters=2 sorters=1`。
10. 旧档兼容：v0.15 旧档（有 `splitters:[{"e"}]`、无 `splittersV2`）能正常读，日志一次性 `legacy 'splitters' node ignored`。
11. `uitree` 核对：`QSystemFilterPickerRow` 落在 itemPicker 矩形内、layer=5；点击不放空（不出现"点一下就关"）。
12. `patchedMethods` 启动 21 / 运行时 59。

### 27.29.7 版本链

- **v0.17.0：品质过滤编辑器迁入原版物品选取器（本版）**（238,592 B，SHA `F71768BE…`，66 checks）。

## 27.30 v0.17.0：大型采矿机放置点数 + Shift 立即建造品质（根因修复，2026-10-30）

### 27.30.1 实机反馈

Lead 实机复核（m02553）报告两处仍未修复：

1. **大型采矿机**（物品 2316）用 4 点物品建造、落成后建筑点数是 **0**（拆除返还也是 0）——v0.16.0 的 `P0_MinerCollector` 只修了「站点把矿搬进储物格」的**产物**品质，没修建筑自身的**放置点数**。
2. **Shift+左键「立即建造」** 仍落成 0 点、产物也没有品质——v0.16.0 的 `P1_InstantBuild` 依赖「同一帧内 `TakeTailItems` → `BuildFinally`」启发式，实机从未触发。

### 27.30.2 根因（asm_full.cs 行号）

共同根因：建造点数的记录链断在「取料那一步没有被消费窗口捕获、落成绑定也没认领」。

- 原版建造取料（`BuildTool_Click.CreatePrebuilds`，asm:155409）分两条路：
  - 手上有同种物品：`player.inhandItemId == itemId && inhandItemCount > 0` → `player.UseHandItems(1, out _)`（asm:155440-155444）。`BuildQualityStash` 只在 `UseHandItems` 的 postfix 里捕获，所以这条路 v0.16.0 是好的。
  - 否则：`player.package.TakeTailItems(ref itemId, ref count, out var _)`（asm:155445-155448）——**背包分支**，既不经过 `UseHandItems`，`out inc` 也被丢弃 ⇒ 没有任何点数进入 MOD。
- `P4_ConsumePriority.CapturePasteConsumed`（Sources.cs:257）原来只在 `_inPaste > 0`（蓝图粘贴窗口）时绑定；手持建造窗口 `_inHandBuild > 0` 记下的收据**永远不会被认领**。背包分支建造（正是大型采矿机的放置路径）既没进 `BuildQualityStash`、收据也没人认领 ⇒ 落成 0 点。
- **立即建造**：取料发生在 `PlayerAction_Inspect.GameTick`（asm:132659）内，块 asm:133137-133169：`if (lmbPressingTime >= 0.65) { PrebuildData prebuildData3 = factory.prebuildPool[num2]; int itemId = prebuildData3.protoId; int count = prebuildData3.itemRequired; player.package.TakeTailItems(ref itemId, ref count, out var _); factory.BuildFinally(player, num2); }`。这次取料**不在** `P4_ConsumePriority` 的任何消费窗口里 ⇒「最高品质优先消耗」不生效，原版按 tail 顺序取栈，可能取到 0 点尾栈（玩家手里的 4 点还在别处），且 `out var _` 丢弃点数 ⇒ 0 点。
- HarmonyX 2.5.5 离线探针（`DSP-MPD-runtime-debug\tools\tmp\harmonyprobe`，验证后已删）：`TakeTailItems(ref int, ref int, out int, bool)` 的普通 `Postfix(int itemId, int count, int inc)` 可以正常拿到 `inc`（探针里拿到 42），说明 v0.16.0 的签名没写错；`object[] __args` 在 HarmonyX 2.5.5 不支持、位置参数 `__1/__2/__3` 会错位——所以失败点在窗口/认领，不在签名。

### 27.30.3 方案（共享收据 + 瞬时消费窗口；无新 cfg 键、不改存档）

1. `P4_ConsumePriority`：消费窗口扩成三合一 `QualityMath.ConsumeWindowActive(inPaste, inHandBuild, instantWindow)`；新增 `OpenInstantWindow()`（`_instantFrame = SafeFrame()`，只在该帧有效）与 `InConsumeWindow` 联合判断；`CapturePasteConsumed` 改用 `InConsumeWindow` 而不是 `_inPaste>0`，并通过新的 `TryTakeReceipt(expectedItemId, out perItem)` 认领（校验 `_valid`、同帧、`QualityMath.ReceiptItemMatches`，一次性消费）。
2. `P1_BuildCapture.AddPrebuildData_ClearStale.Postfix` 调 `CapturePasteConsumed`（现为通用收据）⇒ **背包分支建造**在 `AddPrebuildDataWithComponents` 阶段就绑上点数，`AddEntityData_Bind` 的 prebuild→entity 链路不变。
3. `P1_InstantBuild` 重写：
   - `PlayerAction_Inspect.GameTick(long)` Prefix：复刻原版条件（`VFInput.instantConstruct && hoveringPrebuildId > 0 && !hoveringPrebuildTooFar && hasSupport && hasEnoughItem`）→ 记 `_windowFrame = Time.frameCount` 并 `P4_ConsumePriority.OpenInstantWindow()`，让这次取料走「最高品质优先消耗」。
   - `StorageComponent.TakeTailItems(ref,ref,ref,bool)` Postfix：仍记录「本次取料总点数 / 件数」作兜底（仅窗口帧内）。
   - `PlanetFactory.BuildFinally(Player,int,bool,bool)` Prefix：窗口帧内校验 `prebuildPool[prebuildId].id == prebuildId`，先 `TryTakeReceipt(prebuild.protoId)`，失败再用取料记录（要求 `_itemId == protoId && _count == itemRequired`），最后 `QualityMath.PreferBindingValue` 合并 → `QualityData.SetPrebuildInc(prebuildId, perItem)`。
   - 纯函数（新增）：`QualityMath.ReceiptPerItem`（百分位 → 整点，floor+clamp）、`ReceiptItemMatches`（0/负 = 通配）、`ConsumeWindowActive`、`PreferBindingValue`。
4. 退化策略：任何一环不匹配（非窗口帧、protoId/itemRequired 不一致、收据过期）⇒ 不绑定点数 = 原版 0 点，建造本身与耗材不变；普通取物/分拣/物流不受影响（窗口只在对应 tick 生效）。
5. 无新 cfg 键（继续 16 键）、存档结构不变（点数仍在内存 `prebuildInc`/`buildingInc`，随 `.dsv` 尾块 `buildings` 节点持久化）。

### 27.30.4 文件

- 修改 `QualityMath.cs`（+4 纯函数）、`Patches\P4_ConsumePriority.cs`（瞬时窗口字段 / `OpenInstantWindow` / `Describe`）、`Patches\P4_ConsumePriority.Sources.cs`（通用收据 `TryTakeReceipt` + `CapturePasteConsumed` 改口径 + 绑定日志）、`Patches\P1_BuildCapture.cs`（注释说明背包分支依据）、`Diag\StartupAudit.cs`（新增 `PlayerAction_Inspect.GameTick(long)` 反射自检）、`tests\OfflineTests\OfflineTests.csproj`、`tests\OfflineTests\Program.cs`、README/DEVLOG/TESTLOG。
- 重写 `Patches\P1_InstantBuild.cs`（约 470 行：GameTick 窗口 + BuildFinally 绑定 + 取料兜底 + 统计/日志）。
- 新增 `tests\OfflineTests\Checks.P5InstantBuild.cs`（6 个检查）。
- 版本：`DSPQualitySystem.csproj` `<Version>0.17.0</Version>`（沿用）、`Plugin.VERSION = "0.17.0"`（沿用）。

### 27.30.5 构建与测试

- `build.ps1` → `BUILD OK`，0 警告 0 错误。
- 删除 `tests\OfflineTests\bin|obj` 后 `tests\run-tests.ps1` → **`ALL PASS (72 checks)` / `TEST PASS`**（v0.16.1 的 66 + 本次 6 条：receipt per-item / item match / window union / count>1 dilution / binding value / prebuild receipt scenario）。
- DLL **241,664 字节**，SHA256 **`7217980504A716BA54DB06330DCACF74A2BE81D43FCC76879E364CBF9DD1B0CD`**，AssemblyVersion **0.17.0.0**。
- `patchedMethods` 预期：启动 **21**（与 §27.29 相同，本版未加/删特性补丁）；运行时 **59 → 60**（新增 1 个手动补丁目标 `PlayerAction_Inspect.GameTick`；`BuildFinally`/`TakeTailItems` 在 v0.16.0 已计入）。
- 踩坑：`StorageComponent.TakeTailItems` 的 postfix 签名保持 v0.16.0 的 `(int,int,int)`（探针已验证能拿到 out 参数）；不要用 `object[] __args`（HarmonyX 2.5.5 直接抛异常）。

### 27.30.6 实机核对点（Lead 执行）

1. 启动日志出现 `[QSystem][build] patch registered: instant-build consume window (GameTick) + BuildFinally binding …`，`[Audit] PlayerAction_Inspect.GameTick(long) -> OK`。
2. 普通背包建造（手上不拿同种物品）：拿 4 点熔炉直接在背包建造一栋 ⇒ 落成 4 点（F9 / 拆除返还 / 日志 `prebuild binds consumed item`）。
3. 大型采矿机：4 点物品放大型采矿机 ⇒ 落成 4 点；0 点物品 ⇒ 0 点。
4. 手持建造（手上有 4 点同种物品）仍落成 4 点（旧路径不回归）。
5. Shift+左键立即建造：背包混放 4 点与 0 点同种物品 ⇒ 落成 4 点（日志 `instant-build quality bound … source=receipt`）；只有 0 点物品 ⇒ 落成 0 点。
6. Shift 立即建造后产物/产业链正常（无卡料、无多余耗材）；普通取物/分拣/物流顺序不变。
7. `save` → `loadfull`：建筑点数往返一致（既有 `buildings` 节点，不新增字段）。
8. `patchedMethods` 启动 21 / 运行时 60。
9. 本版与 §27.29 的「品质过滤编辑器迁入原版物品选取器」同在 v0.17.0 内，实机请一并复核 picker footer 仍可用。

### 27.30.7 版本链

- v0.16.1：品质过滤（分流器 / 分拣器）→ 66 checks。
- v0.17.0（§27.29）：品质过滤编辑器迁入原版物品选取器（238,592 B，SHA `F71768BE…`，66 checks）。
- **v0.17.0（本版 §27.30）：大型采矿机放置点数 + Shift 立即建造品质根因修复**（**241,664 B，SHA `72179805…`，72 checks）——DLL 字节与 SHA 以本节为准。

## 27.31 v0.17.1 品质过滤修复（分流器漏货 + 分拣器阈值形同虚设）

### 27.31.1 用户实机反馈（原文）

1. 「四向分流器的品质有限（过滤）不是完全生效，依旧会放过一些无品质物品」
2. 「分拣器的品质过滤似乎完全没有生效」

先复现再说：用 `DSPQS_DEV=1` 打诊断版本，读用户失败现场（`_lastexit_.dsv` 尾块：`splittersV2":[{"e":139,"o":[{"p":3,"i":0,"m":1}]}]`、`sorters":[{"e":176,"m":2}]`），把用户存档复制为 `QSTest.dsv` 后实机复现（用户存档未改动）。

### 27.31.2 根因（asm_full.cs 行号）

**共同根因：`Cargo.inc` 是整栈总点数，不是每件点数。** 原版 `CargoPath.TryUpdateItemAtHeadAndFillBlank(int itemId, int maxStack, byte stack, byte inc)`（asm:28567）合并同 item 栈时执行 `cargoPool[cargoIdAtIndex].inc += inc;`（asm:28583），所以一叠 40 件 1 点物品的 `inc` 是 40，一叠 2 件 1 点物品是 2。MOD 自己的 UI/角标一直是每件点数（`QualityMath.Badges.cs:39 int points = totalInc / count;`、`QualityMath.Blueprint.cs:22`），**但两个过滤器把原始 `Cargo.inc` 直接当每件点数与阈值 min（0–10）比较**：

- 分流器：`P5_SplitterFilter.cs:305`（v0.17.0 版）`int plan = QualityMath.PlanSplitterRoute(cargo.item, cargo.inc, rules, …)` ⇒ 2 件 1 点（每件 0.5 点、角标显示 0）的栈因为 total=2 ≥ min=1 被判定「合格」送进规则口 ⇒ 用户看到「无品质物品照样进」。
- 分拣器：`P5_SorterFilter.cs` 扫描里 `incs[count] = container.cargoPool[cargoId].inc;` ⇒ 同理，`min=2` 只拒绝 total<2 的栈，几乎所有多件栈都能被抓 ⇒ 用户看到「阈值没生效」。

**第二根因（分流器另一半）：v0.17.0 的页脚只在开关已打开时才写 MOD 规则。** `P5_QualityFilterPicker.cs` `OnProtoPicked` 原逻辑 `if (_on) WriteSplitterRule();`——用户点过滤圆后在网格里直接点物品时 `_on == false`（`SplitterClickPrefix` 初始化），于是 **MOD 规则没写**，但 `WrapReturn` 随后仍调用原版 `onReturn`，原版 `UISplitterWindow.OnItemPickerReturn`（asm:525343）把 `outFilter = item.ID` 写脏。之后该分流器无规则 ⇒ 原版 first-fit 按 `outFilter` 分流，而且点击过滤圆前玩家必然已把该口设成输出优先级（原版过滤圆只在输出优先级下出现），`outPriority` 会把该口锁在 `output0` ⇒ 与 `outFilter` 同物品的 0 点货全部灌进这个「以为被规则保护」的口。

**第三根因（分流器兜底路径）：旧 `TryVanillaFallback` 会把货送进规则口。** v0.17.0 在「规则口都满/都不匹配」时逐字复刻原版 first-fit：`outFilter == 0 → output0 → output1..3`、`item == outFilter → 只 output0`。复刻时 `output0` 完全可能就是规则口本身（用户现场 port3 = beltD 131 = output0），于是低品质货被兜底逻辑送回规则口。另外 v0.17.0 的让路条件（`inPriority` / `prioritySlotPresets` / `topId != 0`）任一命中就整台交还原版，同样会把脏 `outFilter` 的货送进规则口。

### 27.31.3 修复

1. **每件点数换算**：复用既有纯函数 `QualityMath.PerItemPoints(totalInc, count)`（`QualityMath.Blueprint.cs:16`，`totalInc<=0||count<=0 → 0`，否则整除），并补 XML 注释说明 v0.17.1 起分流器/分拣器**必须**用它换算，禁止直接用 `Cargo.inc`。
2. **分流器路由**（`Patches\P5_SplitterFilter.cs`）：
   - `UpdatePrefix` 改成「有规则就完全接管」：只保留 `EntityHasSplitterRule` 判断，不再因 `inPriority` / `prioritySlotPresets` / `topId` / `input0==0` 让路给原版；接管前若 `prioritySlotPresets != 0` 先调 `sp.CheckPriorityPreset()` 复刻原版首行语义。
   - `Route` 复刻原版前置（`topId==0` 时 `input0==0 || output0==0` 直接不动；`topId!=0` 时主循环接管、但**不复刻**储物仓回灌，日志 `skip[topStorage]`），每件货 `points = QualityMath.PerItemPoints(cargo.inc, cargo.stack)` 后再进 `PlanSplitterRoute`。
   - 命中规则（`plan>0`）只尝试规则候选口；**都满就原地等待**（`_unrouted++`），绝不溢出到无规则口。
   - 未命中规则（`plan==0`）走新增 `QualityMath.PlanSplitterFallback(portRules, portBelts, outputBelts, candidates)`：按 `output0..3` 槽位顺序返回**没有规则**的物理口；有规则时完全不看原版 `outFilter`（`outFilter` 从此只作原版 UI 图标显示）。全口有规则 ⇒ 返回 0 ⇒ 货原地等。
   - 删除旧 `TryVanillaFallback`（泄漏路径整段铲掉），搬运用 `TryMoveVanilla` + 新 `TryMoveToSlot`，保留 `PathOf`/`pathLength<=10`/`TestBlankAtHead`/`TryPickCargoAtEnd`/`InsertCargoAtHeadDirect`/`InputAlternate`/`OutputAlternate` 的原版约束。
3. **分拣器**（`Patches\P5_SorterFilter.cs`）：扫描时 `incs[count] = QualityMath.PerItemPoints(cargo.inc, cargo.stack)` 参与 `PlanSorterPick`，取货仍整栈取、返回给原版的 `inc` 仍是该栈原始 total inc（原版语义不变）。`min=0` 时 `_ctxMin==0` 直接 return true = 原版逐字节等价。
4. **页脚**（`Patches\P5_QualityFilterPicker.cs`）：`OnProtoPicked` 分流器分支改为点网格物品即 `_itemId = proto.ID; _on = true; WriteSplitterRule(); RefreshRow();`——「点物品」本身就是给该口按此物品过滤（阈值沿用当前值，初始 0 = 不限）。仍链原版 `original(proto)` 以便原版面板图标/`outFilter` 显示，但路由层已彻底忽略 `outFilter`。
5. **DEV 诊断日志**（`DSPQS_DEV=1` 才打印，默认零开销）：分流器 `skip[presetApplied/noRule/topStorage]`、`noop`、`route … cargos=N: [item x stack @ inc/pt]`、`blocked`、`fallback … outFilter=… plainPorts=… moved=…`、`routed …`；分拣器 `tick`（只打前 3 条）、`pick`、`scan … count=N: [item x stack @ rawInc/pt]`、`picked … inc=… pts=… min=…`；统计进 `Describe()`（`took/fallback/unrouted/noop`、`ticks/tickMin/picks/scanned`）。

### 27.31.4 文件

- 修改：`Patches\P5_SplitterFilter.cs`（大改：完全接管 + per-item + 回退排除规则口）、`Patches\P5_SorterFilter.cs`（per-item 阈值）、`Patches\P5_QualityFilterPicker.cs`（点物品即写规则）、`QualityMath.SplitterFilter.cs`（新增 `PlanSplitterFallback`、`PlanSplitterRoute`/`PlanSorterPick` 文档）、`QualityMath.Blueprint.cs`（`PerItemPoints` 文档）、`Diag\StartupAudit.cs`（无改动，复用）、`DSPQualitySystem.csproj`（0.17.1）、`Plugin.cs`（`VERSION = "0.17.1"`）、`tests\OfflineTests\Checks.SplitterFilter.cs`、`tests\OfflineTests\Program.cs`。
- 文档：`README.md`、`DEVLOG.md`（本节）、`TESTLOG.md`、`tools\r2\CHANGELOG.md`、`tools\r2\README.md`。

### 27.31.5 构建与测试

- `build.ps1` → `BUILD OK`，0 警告 0 错误。
- 离线测试 72 → **78 checks**：`ALL PASS (78 checks)`。新增 6 条：`cargo points`（整栈 → 每件）、`splitter fallback`（原版槽位顺序 + 排除规则口）、`splitter fallback rotation`（轮转/空槽/规则口当前是输入）、`route cargo points`（低品质栈不进规则口，对照 raw total 会误匹配）、`sorter cargo points`（整栈 → 每件阈值）、`sorter per-item tiers`（混合栈顺序 + `min=0` 回归）。
- DLL **245,248 字节**，SHA256 **`CB7769A7DAFC7FC7C44E35C39264958E399F2A7452A07779D5B4D0EA861849C4`**，AssemblyVersion **0.17.1.0**。
- 启动日志预期：`[splitterfilter] armed (methods=2, routed=0, took=0, fallback=0, unrouted=0, noop=0, skip[presetApplied=0,noRule=0,topStorage=0])`、`[sorterfilter] armed (methods=5, picked=0, blocked=0, ticks=0, tickMin=0, picks=0, pickMin0=0, scanned=0)`、`=== DSP Quality System v0.17.1 ===`。

### 27.31.6 实机核对点（用户可自行复测）

1. **分流器低品质不入规则口**：一条带子上混放 0 点与 8 点同种物品，给某输出口在物品选取器底部设「物品 X、`≥4`」⇒ 8 点进规则口，0 点只走**没有设规则**的口/停在带上；**规则口一个 0 点都不许进**（可点开分流器面板看各口计数）。
2. **分流器脏 outFilter 不泄漏**：先按原版习惯给该口设输出优先级/全局过滤（`outFilter` 非 0），再设 MOD 规则 ⇒ 低品质货仍不得进规则口。
3. **分拣器阈值**：带子上混放 0 点与 8 点物品，`Shift+右键`把阈值调到 4（面板显示「物品名　品质≥N」）⇒ 只抓 8 点，0 点在窗口内也不抓；阈值 0 = 完全原版。
4. **回归**：把该分流器/分拣器的规则全部清除（分流器页脚开关关掉、分拣器阈值调回 0）⇒ 行为与原版一致。
5. 存读档往返：规则仍随 `.dsv` 尾块 `splittersV2`/`sorters` 保存与恢复，schema 仍为 1、16 个玩家 cfg 键不变。

### 27.31.7 验证环境说明（本轮自动化实机受环境限制）

- 本轮在自动化会话里尝试实机复现（harness v13 + `DSPQS_DEV=1` + 复制用户失败现场为 `QSTest`）：已确认 `loadfull QSTest` 在该会话不可用——`DSPGame.StartGame(string)` 在标题界面调用时 `EndGame()` 销毁菜单演示 `GameMain`，`GameLoader` frame 1 立即 `Instantiate` 新 `GameMain`，`GameMain.Awake → CreateGPUInstancing → GPUInstancingManager.Init` 触发 `NullReferenceException at GPUInstancingManager.Init () [0x006F]`（LDB 模型 `prefabDesc` 未就绪）。
- **隔离实验**：移走 `DSPQualitySystem.dll`（只留其它插件 + harness）后 `loadfull QSTest` 仍然同样失败 ⇒ 与本 MOD 无关，是 harness 的 `loadfull` 调用方式在该环境不可用；`load`（`GameSave.LoadCurrentGame` 路径）同样不可用。
- 该会话的桌面无法被 `CopyFromScreen`/`PrintWindow` 合成（截图全黑，含整个虚拟桌面）⇒ 自动化会话拿不到游戏内截图。因此本轮实机证据只能到「启动日志 + armed 行 + 反射自检全部 OK」，行为级验证请按 §27.31.6 由用户在真实会话复测（harness 命令队列、`splitterdump`/`splitterrules`/`sorterrule`/`storage dump` 均可用）。
- 已清理：harness 移回 `_live-backup\plugins-parked-harness\`，测试存档 `QSTest.*` 删除，用户存档 `沙盒.dsv`/`_lastexit_.dsv` 未改动；游戏内插件已换回 v0.17.1（245,248 B）。

### 27.31.8 版本链

- v0.16.1：品质过滤（分流器 / 分拣器）→ 66 checks。
- v0.17.0（§27.29）：编辑器迁入原版物品选取器（238,592 B，66 checks）。
- v0.17.0（§27.30）：大型采矿机放置点数 + Shift 立即建造（241,664 B，72 checks）。
- **v0.17.1（本节）：品质过滤 per-item 换算 + 分流器完全接管/回退排除规则口 + 分拣器阈值生效 + 点物品即写规则**（**245,248 B，SHA `CB7769A7…`，78 checks**）——DLL 字节与 SHA 以本节为准。

## 27.32 v0.18.0 四项平衡/机制修复（发电与供电范围、升品曲线、矿机确定性、虚影补付）

### 27.32.1 用户口径（原文摘要）

1. 「风力/光伏/地热/火力/射线接收站这些发电类设施并未改变发电功率，且电力扩展类建筑的供电范围没有正确扩展」
2. 「制造类建筑产物升品概率过低，对标速度加成曲线重新设计数值」
3. 「矿机和大矿机修改产物增产逻辑，改为每两级固定给产物附加 1 点增产点数，2 级矿机必定附加 1 点，4 级矿机必定附加 2 点，可以考虑直接把矿机附加增产点数概率改为 100% 来实现，复用原本的多判代码」
4. 预建模式材料不足时白色虚影「按 0 点落成」是错的 —— 希望虚影建筑在建造时依然按所消耗的建筑点数附加品质

### 27.32.2 根因

**① 发电功率"没变"不是代码 bug，是用户实机装的是旧版 DLL。** 用户游戏目录 `BepInEx\plugins\DSPQualitySystem\DSPQualitySystem.dll` 实测 **157,184 B / 2026-10-26 01:00:49**，`BepInEx\LogOutput.log` 首行 `=== DSP Quality System v0.11.10 ===`、`assembly=0.11.10.0`。P5 五件套（含 `P5_PowerGen`）是 **v0.12.0** 才加入的 ⇒ 该 DLL 里根本没有注册发电补丁，风力/光伏/地热/火力/射线自然还是原版功率。工作区内当时也没有任何已构建的 v0.17.1 DLL。
- 另核对：当前游戏 `DSPGAME_Data\Managed\Assembly-CSharp.dll`（8,034,304 B / 2026-10-19，游戏 10-19 更新过）上 `PowerGeneratorComponent.EnergyCap_Fuel()`、`EnergyCap_Wind(float)`、`EnergyCap_PV(float,float,float,float)`、`EnergyCap_Gamma(float)`、`EnergyCap_GTH()` 五个方法签名与 `System.Int64` 返回**全部未变**（Mono.Cecil `il.exe` 实测）⇒ 既有 `P5_PowerGen` 在新游戏上仍可绑定，抬高 `__result` 并把 `capacityCurrentTick` 同步写回即可真实提高发电（`tools\game_src\PowerSystem.cs:1785-1826` 累加返回值进 `num23`、`:1979-2003` 用 `capacityCurrentTick × generaterRatio` 算实际发电量、`:1941` 写 `powerNetwork.energyCapacity`）。

**② 供电范围是"从未实现"，不是"扩展错了"。** 全源码 grep `powerConnectDistance|powerCoverRadius|PowerNode|coverRadius|connectDistance` = **0 命中**；`开发文档\README-工程.md` 原文明写「电力塔范围等后续阶段未做」。原版链路：`PlanetFactory.cs:1072` `if (desc.isPowerNode) powerSystem.NewNodeComponent(entityId, desc.powerConnectDistance, desc.powerCoverRadius)` → `PowerSystem.cs:573-574` 写入 `nodePool[num].connectDistance/.coverRadius`（**随存档序列化的持久字段**，`:194/:348` Export/Import）→ 每帧 `:915-926` 复算 `node.connDistance2 / coverRadius2` → 覆盖判定 `:945-965`（连接距离）、`:985-986`、`:1096`（`distance² <= coverRadius2`）。所以按「prefab 原值 × 曲线倍率」幂等重写这两个字段即可，既生效又能随存档保留。

**③ 升品概率与速度曲线脱节。** 旧实现 `UpgradeChance = min(点数 × 1.5%, 60%)`，而速度/充电/发电都查同一张每点曲线（0/2.5/5/7.5/10/15/20/30/40/60/100 %，10 点 = 2.00×）。1 点只有 1.5%、4 点只有 6%，体感"几乎不升品"。

**④ 矿机概率模型与用户口径不符。** 旧 `MinerChance = 点数 × 2%`、判定次数 `floor(点数/2)` ⇒ 4 点期望 0.16 点/件、10 点期望 1.0 点/件，且随机性大。

**⑤ 虚影按 0 点落成的真因。** 材料不足时原版只写半透明预建、`Player.UseHandItems` 不跑 ⇒ `BuildQualityStash` 推不进条目 ⇒ `CreatePrebuilds` 做 `TryPopFor` 失败、`prebuildInc` 为空。材料到齐后的补付发生在 `ConstructionModuleComponent.PlaceItems`（Mono.Cecil 扫 `PrebuildData.itemRequired` 的 **唯一 `ldflda` 就地扣减点**）：每预建 `TakeTailItems(ref itemId, ref need, ref inc, false)` → `Assert.True(need == pre->itemRequired)`（要求一次全额付清）→ `pre->itemRequired -= need` →（==0 时）`RefreshPrebuildWarning` → `NotifyPrebuildChange(prebuildId, 3)` → `AlterPrebuildModelState`。原版把 `inc` 丢进一个用完即弃的 out 参数（与 Shift 立即建造同款）⇒ 落成时没有任何点数可继承。

### 27.32.3 修复

1. **发电功率**：代码不动（`P5_PowerGen` 逻辑本就正确），本版把 v0.18.0 重新构建并部署到游戏目录（`build.ps1 -Deploy`），启动日志出现 `[powergen] armed (… methods=5/5 …)` 即生效。若仍不见效，先看这一行是否 armed。
2. **供电范围（新增 `Patches\P5_PowerRange.cs`）**：
   - 判定用 `PrefabDesc.isPowerNode`（感应塔/无线输电塔/配电站），不按物品 id 白名单。
   - `ApplyOne`：`factor = QualityData.PowerRangeFactor(planetId, entityId)`（→ `FeatureFactor` → `QualityMath.FeatureFactorFrom`，与发电/速度同一张曲线）；`nodePool[nodeId].connectDistance = desc.powerConnectDistance * factor`、`.coverRadius = desc.powerCoverRadius * factor`（**从 prefab 原值重算，幂等**，不累乘）。
   - 触发点：① `P1_BuildCapture.AddEntityData_Bind` postfix 落成即套用（`entityPool[entityId].powerNodeId` 定位节点，`NewNodeComponent` 里由原版写入）；② `PowerSystem.GameTick(long,bool,bool,int)` prefix 每星球一次全量重算（覆盖读档/旧存档/跨存档迁移），`ResetState()` 挂在 `GameData.Import`/`NewGame` postfix 上。
   - 改完调 `planet.factoryModel.RefreshPowerNodes()` 刷新可视化；异常只吞可视化。
3. **升品概率复用曲线**：
   - 新增 `QualityMath.UpgradeCurveChance(int buildingInc)` = `ClampPercent((float)SpeedBonusFrom(buildingInc))`（1..10 点 = 2.5/5/7.5/10/15/20/30/40/60/100 %）。
   - `QualityMath` 把原 `RollCycleLevel` 拆成 `RollCycleLevelCore(...)`（原体，chance 变成入参）+ 两个入口：旧线性 `RollCycleLevel`（保留给离线 A/B）与新的 `RollCycleLevelCurve(...)`。
   - `QualityRules.RollCycleQuality` 改调 `RollCycleLevelCurve`；**掷骰结构不变**（0–6 单掷、7–9 额外再判一次、10 点必 +2 不掷骰，分馏塔仍单掷）。
4. **矿机确定性**：`QualityConfig.MinerChancePerPointValue` 2.0f → **100f**。`QualityRules.RollMinerQuality` 一行未改 —— 既有 `if (chance >= 100f || RollPercent(chance))` 必中分支直接复用多判循环，于是 `rolls = floor(点数/2)` 每次必成 ⇒ 2 点 +1、4 点 +2、10 点 +5（1 点仍 0）。大型采矿机（站点 `StationIncAfterTransfer` 路径）吃同一套，无需另改。
5. **虚影补付（新增 `Patches\P1_DeferredPay.cs`）**：
   - `ConstructionModuleComponent.PlaceItems(PlanetFactory,Player)` prefix/postfix 开/关补付窗口；
   - `StorageComponent.TakeTailItems(ref int,ref int,ref int,bool)` **postfix** 只在窗口内把真实取料 `(itemId, count, inc)` 压进待合并队列（只读，不改原版 out 值）；
   - `PlanetFactory.NotifyPrebuildChange(int,int)` prefix：窗口内 `change == 3` 时把队列合并进该 prebuildId 的 `(点数, 件数)` 累加器；`itemRequired != 0`（基地分帧补付）就先留着，`itemRequired == 0` 时 `QualityData.SetPrebuildInc(prebuildId, QualityMath.DeferredPayPerItem(points, items))`；
   - `RemovePrebuildWithComponents` postfix 清累加器；`ResetState()` 挂存读档钩子。
   - **只观察不劫持**：原版 `Assert.True(need == prebuild.itemRequired)` 要求一次全额付清，所以 `P4_ConsumePriority.TakeBestGridPrefix` 在 `P1_DeferredPay.PayWindowActive` 期间直接回退原版（不换格、不拆点）。落成绑定复用既有 `AddEntityData_Bind` → `TakePrebuildInc` → `SetBuildingInc`，链路闭合。

### 27.32.4 文件

- 新增：`Patches\P5_PowerRange.cs`、`Patches\P1_DeferredPay.cs`、`tests\OfflineTests\Checks.V0180.cs`。
- 修改：`QualityMath.Curves.cs`（`UpgradeCurveChance`）、`QualityMath.cs`（`RollCycleLevelCore`/`RollCycleLevelCurve`/`DeferredPayPerItem`）、`QualityRules.cs`（改调曲线入口）、`QualityConfig.cs`（`MinerChancePerPointValue = 100f`、新增内部常量 `PowerRangeBonus`）、`QualityData.Extra.cs`（`PowerRangeFactor`）、`Patches\P1_BuildCapture.cs`（落成套用供电范围）、`Patches\P1_SaveHooks.cs`（两处 `ResetState`）、`Patches\P4_ConsumePriority.Sources.cs`（补付期回退）、`Plugin.cs`（`VERSION = "0.18.0"` + 两处 Apply/Describe）、`DSPQualitySystem.csproj`（0.18.0.0）、`tests\OfflineTests\OfflineTests.csproj`、`tests\OfflineTests\Program.cs`。
- 文档：`DSP品质系统-MOD开发文档.md`（v0.18.0：§2.2/§2.3/§2.4/§3.3/§4.1/§4.2/§4.2.2/§4.2.4/§4.3/§6.1/§6.2/§6.3/§6.4/§7）、`README-工程.md`、`README.md`、`DEVLOG.md`（本节）、`TESTLOG.md`。

### 27.32.5 构建与测试

- `build.ps1` → `BUILD OK`，0 警告 0 错误。
- 离线测试 78 → **82 checks**：`ALL PASS (82 checks)`。新增 4 条：`upgrade curve chance`（0→0、1→2.5、4→10、7→30、9→60、10→100、11→100，且旧线性 `UpgradeChance(4,1.5f,60f)==6` 保持）、`roll cycle curve`（10 点必 +2 且不调 RNG、8 点双掷、6 点单掷、分馏塔单掷、夹上限）、`miner guaranteed rolls`（`MinerChance(p,100f)==100`、`rolls=p/2`、`RollPercent(100f)` 必真）、`deferred pay per item`（16/4→4、7/2→3、0/5→0、999/2→10）。
- DLL **253,952 字节**，SHA256 **`F3B9E800368F48AAB7B08CEEA2E3EF76366C62593F92C96C7A847C38490191DE`**，AssemblyVersion **0.18.0.0**。
- 启动日志预期：`=== DSP Quality System v0.18.0 ===`、`[powergen] armed (… methods=5/5 …)`、`[powerrange] armed (… methods=1 …)`、`[deferred] armed (… methods=4 …)`。

### 27.32.6 实机核对点（用户复测）

1. **发电功率**：先确认启动日志 `=== … v0.18.0 ===` 且 `[powergen] armed`；给一台 10 点风力发电机通电，电网面板容量应为同型号 0 点机的 2.00×（火力机燃料消耗同步变快、射线接收站产出同步变多，属文档化双收益）。
2. **供电范围**：10 点感应塔/无线输电塔/配电站的覆盖圈与拉线距离约为 0 点同型号的 2 倍；读档后仍保持；拆掉重建按当前点数重算。
3. **升品概率**：1 点制造台 2.5%、4 点 10%、7 点双掷 30%、9 点双掷 60%、10 点必 +2；`DSPQS_DEV=1` 时 `[QSystem][roll]` 的 `base=`/`extra=` 应显示曲线百分比。
4. **矿机**：2 点矿机产出的矿每件固定 1 点、4 点固定 2 点、10 点固定 5 点；大矿机（站点型）同样。
5. **虚影补付**：手头材料不足时点建造 → 白色半透明预建；补足材料后自动建成，落成建筑应带点数（用 `DSPQS_DEV=1` 看 `[deferred] payments/bound` 递增，或拆掉返还物品看点数）。

### 27.32.7 版本链

- v0.17.0（§27.29/§27.30）：选取器编辑器 + 大矿机 + 立即建造 → 72 checks。
- v0.17.1（§27.31）：品质过滤 per-item 修复 → 78 checks（245,248 B）。
- **v0.18.0（本节）：发电部署 + 供电范围 + 升品曲线 + 矿机确定性 + 虚影补付**（**253,952 B，SHA `F3B9E800…`，82 checks**）——DLL 字节与 SHA 以本节为准。


### 27.32.8 v0.18.0 实机静默调试复核（2026-10-31 14:45）

用 `DSPQS_DEV=1` 打开调试热键（F5 制造台 / F9 建筑点数分布 / F11 给瞄准建筑注入 4 点 / F12 矿机统计），
在 `TEST.dsv`（天市右垣五 - 3号星，id=103）上逐条复核：

1. **发电功率（①a）**：射线接收站 entity 11（proto 2210）注入 4 点后出现
   `[powergen] output bonus active (planet=103 entity=11 factor=1.10 capacity=264000005)`，
   星球电网 `supply` 从 `720,000,000` 抬到 `744,000,005`（+24M = 240M × 10%）。
   → 抬 `EnergyCap_*` 返回值确实进入 `PowerSystem` 容量汇总，修复有效。
2. **供电范围（①b）**：电力塔 entity 38 注入 4 点，退出到主菜单再"继续"重载后，读档重算路径打印
   `[powerrange] eid=38 node=1 points=4 factor=1.1 conn=58.85 cover=29.15`
   （prefab 基准 conn 53.5 / cover 26.5 × 1.1）；射线接收站作为电力节点同样命中
   `eid=11 node=3 ... conn=16.5 cover=5.5`。→ `nodePool[].connectDistance/coverRadius` 幂等重算生效。
3. **升品概率（②）**：`[roll] eid=164 in=0 bld=10 mode=2 base=0 extra=0 pooled=2`（10 点必定 +2 不掷骰）；
   `bld=4 mode=0`（4 点单掷，曲线 10%）。掷骰结构未变，概率来源换成曲线表。
4. **矿机（③）**：`[Probe][F12] miner quality hits=63 calls=63 lastMinerCall=eid=132 level=2 minerChancePerPoint=100`
   → 2 点矿机每次产出必 +1；点数分布探针 `[Probe][F9] ... 4=6 10=12` 与场景一致。
5. **白虚影补付（④）**：Lead 侧只有自检行 `deferredPay armed=True payments=0 bound=0`；
   **用户于 2026-10-31 14:52 实测确认已修复**——材料不足时的白色虚影在材料补齐后落成，建筑带点数。
   → `ConstructionModuleComponent.PlaceItems` 补付窗口 + `NotifyPrebuildChange(id,3)` 绑定链路端到端成立。

测试污染说明：为取证给 entity 11、38 注入了 4 点，写入 `TEST.dsv` 派生存档，后续可拆除重建清除。

---

## 27.33 v0.19.0 建筑面板品质文案（**实现后主动回滚**）+ 喷涂次数 = 1 + 品质回收机（2026-10-31）

### 27.33.1 用户口径

1. **建筑参数说明跟随品质加成**：打开建筑面板时，面板上的速率/产量等数值应当体现品质带来的加成，而不是仍显示原版数值（否则玩家会以为加成没生效）。**——该项实现后经用户反馈撤回，回滚过程见 §27.33.5。**
2. **品质回收机**：参照 `开发文档\品质回收机-开发难度评估.md` 开发——把带品质点数的物品回收成增产剂。
3. **三种增产剂可用喷涂次数改为 1**（原版 Mk.I/II/III = 12/24/60 次）。

约束：不新增玩家配置键（仍 16 键）、存档 schema 向后兼容（新增字段必须可缺省）、不影响既有八项加成与三条过滤链。

### 27.33.2 实现

- **A 面板文案（已主动回滚，见 §27.33.5；以下为当时的实现记录）**：新增 `Patches\P6_DescPanel.cs`（737 行）与 `QualityDescMath.cs`。对 8 个窗口
  （`UIAssemblerWindow` / `UIMinerWindow` / `UILabWindow` / `UITurretWindow` / `UIEjectorWindow` /
  `UISiloWindow` / `UIPowerGeneratorWindow` / `UIStationWindow`）的 `_OnUpdate()` 加 postfix：
  主速率字段（`speedText` / `bpmText` / `valueText5` / `value0Text`）按品质倍数缩放并追加 `（品质 +N%）`；
  其余速率行只缩放首位数字不加后缀（窄框放不下后缀）；`RateScan.Applied` 逐控件记录已缩放值保证**幂等不叠加**；
  发电机与物流塔的数字已由 `P5_PowerGen` / `P3_StationCharge` 抬高，只补后缀。跳过 `UIPowerNodeWindow`（无范围字段）
  与 `UIItemTip.SetTip`（无 entityId）。纯显示层，不改实际数值。
- **B 喷涂次数 = 1**：新增 `Patches\P6_SprayCount.cs`（444 行）。数字来源是 `ItemProto.HpMax`
  （`ItemProto.cs:39`，tooltip `ItemProto.cs:1812`；插入路径 `PlanetFactory.cs:12272-12286`/`12330-12339` 与
  取出路径 `CargoTraffic.cs:3716`、`SpraycoaterComponent.InternalUpdate` IL 0x014D/0x01E5 都活读该字段）。
  `ItemProto.Preload(int)` postfix 记录原值后把 1141/1142/1143 写成 1；`SpraycoaterComponent.Import` postfix
  把旧档"次数"按 `ceil(次数/原版次数)` 折算成件数并清 `extraIncCount`；`ABN_ProtoData.CheckProto` prefix+postfix
  在校验期间临时还原 12/24/60，避免被判定"数据异常"。
- **C 品质回收机（v1 已废弃 → v2「设计 A：点数进罐」）**：新增 `Patches\P7_Recycler.cs`、`QualityData.Recycler.cs`。
  **v1（首版，保留为历史）**：复用喷涂机，判定 `cargoBeltId>0 && incBeltId==0 && incCount+extraIncCount<=0`（只接物品带、不接催化剂带、空罐），
  **prefix 接管** `SpraycoaterComponent.InternalUpdate`，自行「读喷槽 → 清 inc → 记账 → 向下方传送带投放 1143」，每 **4 点** 换 1 个 Mk.III、
  单件 <3 点原样通过。**实机完全无效果**，根因两条：① 投放位置守卫过严、且用户机器下游根本没接带 → 原版直插接口插入静默失败（无返回值、无日志）；
  同时点数在「取走清 0」那一刻已销毁，UI 上完全不可观测。② 候选判定依赖 `incBeltId==0 && cargoBeltId>0`，与原版字段语义耦合（原版在催化剂
  path 为 null 时会主动把 `incBeltId` 清 0），候选条件时有时无。
  **v2「设计 A：点数进罐」（最终落地）**：载体仍是原版**喷涂机**（2313），回收通道改为它的**催化剂（增产剂）进料带**（原版放 1141/1142/1143 的那一侧），
  物品带那一侧照常喷涂、互不影响。触发：喷涂机通电（`powerRatio > 0.1`）、进料带上出现**非增产剂**且单件点数 `Cargo.inc / Cargo.stack ≥ minPerItem`
  （默认 1，`QualityRecyclerMath.DefaultMinPerItem = 1`）的 cargo → 整叠**消耗并销毁**（不返还、下游不产出物品），`cargo.inc` 记入该机**回收账号**；**每 4 点 = 1 次喷涂次数**灌进原版喷槽
  （`pointsPerSpray=4`、`incItemId=1143`、`incSprayTimes = max(1, HpMax)` 即 1、容量 600 次），超出容量的点数留在账号；**0 点/件（未喷涂）**
  → **整叠原样放行、不消耗**，Mk.I 的 1 点/件起全部参与（阈值 3 是首版取值，已于 2026-10-31 按用户反馈降到 1，见本节末条）；满槽停收（probe `mode=tankFull`），
  消耗出空间后自动恢复。每机账号（0..`600*4` 点）随存档持久化，拆机后 1200 tick 惰性清扫孤儿账号（`PrunedEvery=1200`）。
  **注入改为 postfix**：`SpraycoaterComponent.InternalUpdate(CargoTraffic, AnimData[], int[])`（struct / class 双签名按 `IsValueType` 反射选择；
  每次 `Refresh` 从 `spraycoaterPool[id]` 重读组件、改完 `Persist` 写回，避免 struct 副本丢改动）。IL 依据（当前游戏 DLL）：
  `0x000E if(incBeltId<=0) goto 0x025F`、`0x0189 TryPickItem(segIndex+segPivotOffset-2, 5, incItemId, ...)`、`0x02AE if(cargoBeltId<=0) end`、
  `0x0257` 之后才判 `powerRatio>0.1`；`CargoTraffic.SpraycoaterTick` 对每台喷涂机**无条件**调 `InternalUpdate`。
  存档节点改为 `"recyclers":[{"e":entityId,"p":points,"c":capacity}]`（`p` = 累计点数、`c` = 机体容量；无回收机不写、旧档缺 key 跳过），
  v1 的 `"o"`（投递计数）字段读档一律忽略。**加 probe 的直接原因**：v1 失败时一条相关日志都没有——既看不到候选判定，也看不到插入失败，
  只能靠猜；v2 因此新增 `[QSystem][recycler][probe]` 诊断行（状态变化打 Info、DEV 下每 200 次打 Debug，逐条 reason）
  与启动自检行 `[QSystem][recycler] recycler armed=… mode=inc-feed selfCheck=ok pointsPerSpray=4 minPerItem=1 accounts=… carried=…`。
- **2026-10-31 阈值下调（用户反馈）**：用户问「为什么单件 ≥3 点？<3 点的就不能回收了吗？」→ 默认阈值由 3 降到 **1**
  （`QualityData.Recycler.cs` 的 `QualityRecyclerMath.DefaultMinPerItem = 1`，`Patches\P7_Recycler.cs:76` 的 `MinPerItemValue` 取该默认值；仍是内部开关、**不是** cfg 键）。
  **理由：兑换本身是亏损的**——4 点只换 1 次喷涂次数，而 1 次喷涂最多值 3 点，任何方向都套不了利，阈值只需用来放行 0 点货物（原来"避免用小喷叠点套利"的说法站不住）。
  改成 3 即回到「只收 Mk.III 喷过的」。重新出包：**271,360 B / SHA256 `5A43F0C747BD01862051C94B7C26C7267B51D003AE38D10DBE5A6DF190FE52E8` / 2026-10-31 16:35:03 构建 / 86 checks**。
- `Plugin.cs` 注册三处新补丁并打印 `[QSystem][desc]` / `[spraycount]` / `[recycler]`；`VERSION` 与 csproj 版本 → 0.19.0。（回滚时移除了 `P6_DescPanel` 的两行注册，见 §27.33.5。）
- 补丁统计（回滚后）：38 个 .cs 文件 / **40 个补丁类**（18 特性 + 22 手工）、**74 次注册**、**67 个独立方法**（v0.19.0 实现时曾为 41 类 / 82 次 / 75 个，回滚后回到原值）。

### 27.33.3 构建与测试

- `BUILD OK`，0 警告 0 错误；当时新增离线用例 `Checks.P6Desc.cs`（1 组，**回滚时已删除**）与 `Checks.P7Recycler.cs`（4 组），
  规模当时 82 → 87 checks；回滚后为 **82 → 86 checks**，`tests\run-tests.ps1` → `ALL PASS (86 checks)`。
- 实机记录（回滚前，保留备查；`DSPQS_DEV=1`，`TEST.dsv`，planet 103）：
  - `[QSystem][desc]` armed；10 点制造台面板 `制造速度 90.0/分钟 45.0/分钟` → **`180.0/分钟 90.0/分钟`**，左列同步 180，
    0 产量行显示 `0.0/分钟（品质 +100%）`（截图 `logs\shot-20261031_154902.png`、裁剪 `logs\crop_desc_v3.png`）；
  - `[QSystem][spraycount] armed: Mk.I/II/III = 1 spray per item (preload hook=True, legacy import hook=True, signature guard=True, patched now=3)`；
    旧档三台喷涂机折算 `entity 68: 604 -> 11 items`、`104: 341 -> 6`、`161: 658 -> 11`；
  - （v1，保留备查）`[QSystem][recycler] armed=True minPerItem=3（v1 历史值）… anchor=SpraycoaterComponent.InternalUpdate mode=prefix-takeover`、`selfCheck=ok`——**放机跑带完全无效果**（根因见 §27.33.2 C）；v2 改为 inc-feed postfix 并新增 probe 行定位，`recycler armed=… mode=inc-feed minPerItem=1 …` 已随 2026-10-31 实机验证通过与阈值下调一并确认。

### 27.33.4 文件

- 新增：`Patches\P6_SprayCount.cs`、`Patches\P7_Recycler.cs`、`QualityData.Recycler.cs`、`tests\OfflineTests\Checks.P7Recycler.cs`；
  v0.19.0 实现时还新增过 `Patches\P6_DescPanel.cs`、`QualityDescMath.cs`、`tests\OfflineTests\Checks.P6Desc.cs`，**三者在回滚时已删除**（见 §27.33.5）。
- 修改：`Plugin.cs`、`DSPQualitySystem.csproj`、`QualityConfig.cs`（4 个新内置常量）、`Save\QualitySave.cs`（recyclers 节点）、
  `Diag\StartupAudit.cs`（新自检项）、`tests\OfflineTests\Program.cs` / `OfflineTests.csproj`。
- 文档：README.md、README-工程.md、开发文档（§2.11–2.13 / §3.3 / §4.2.8 / §6 / §7.11 / §9）、README-打包说明.md、
  tools\r2 两份、TESTLOG.md。

### 27.33.5 主动回滚：建筑面板品质文案（实现当天撤回）

- **触发**：功能实机可用后，用户反馈"整个面板变得很乱"，明确要求回滚该功能。
- **排版副作用（两轮返工、v1 / v2 / v3 三版之后仍然存在）**：① `RateScan` 按文本正则扫描窗口时，会命中原版面板里**子预制体拼出来的文本**——原版顶部速率行 `90.0/分钟 45.0/分钟` 是同一个 Label 的两段数字拼接，缩放后与其它行的格式不再统一；② 主字段追加 `（品质 +N%）` 后缀后，`best-fit` 的窄框（防御塔 `转速`、弹射器/发射井进度、左列速率行）被自动缩到极小字号，观感明显差于原版；③ 退成"只缩放数字、不加后缀"后，8 个窗口的字段布局各不相同，逐控件幂等表在窗口复用/切页时仍偶发残留旧值。
- **功能重叠**：第三方 MOD **`AssemblerUI v3.0.0 by Shad0wlife`** 已提供同类"原版面板显示品质加成后数值"的功能，且更贴近原版观感；本 MOD 再维护一份只会与它打架。
- **回滚动作**：删除 `Patches\P6_DescPanel.cs`、`QualityDescMath.cs`、`tests\OfflineTests\Checks.P6Desc.cs`；`Plugin.cs` 去掉 `Patches.P6_DescPanel.Apply` / `Patches.P6_DescPanel.Describe` 两行（原位置留一行注释说明该功能已回滚）；`tests\OfflineTests\OfflineTests.csproj` 去掉对应 `<Compile Include>`、`tests\OfflineTests\Program.cs` 去掉对应 `Check` 调用。
- **回滚后状态**：`build.ps1` 绿、`tests\run-tests.ps1` = `ALL PASS (86 checks)`（回滚前 87）；补丁统计回到 **38 个 .cs 文件 / 40 个类（18 特性 + 22 手工）/ 74 次注册 / 67 个独立方法**。
- **保留的教训**：改原版面板文本前先确认那个 Label 是否被多个子预制体共用；`best-fit` 容器内不要追加后缀文本；当社区已有同类 UI MOD 且实现更原生时，自研版本应及时放弃而不是继续返工。

### 27.33.6 版本链

- v0.17.1（§27.31）：78 checks（245,248 B）。
- v0.18.0（§27.32）：发电部署 + 供电范围 + 升品曲线 + 矿机确定性 + 虚影补付（253,952 B，`F3B9E800…`，82 checks）。
- **v0.19.0（本节）：喷涂次数 = 1 + 品质回收机**（**271,360 B，SHA `5A43F0C747BD01862051C94B7C26C7267B51D003AE38D10DBE5A6DF190FE52E8`，86 checks**，2026-10-31 16:35:03 构建）——回收机默认阈值 `minPerItem=1`（任意带点数货物可回收、0 点放行），按用户反馈由 3 下调后重新出包；行为级验证已由用户实机确认通过。

## 27.34 v0.19.1 读档丢批次点数 + 批次四舍五入 + 喷涂次数分级 + 分拣器过滤重写（2026-10-31）

### 27.34.1 用户报告与取证

1. **"读档后第一批产物点数偏低"**：制造台的"本批原料点数"（`QualityData.inputLevel`）自 v0.9.0 起**只存在内存**，用于"本批产物继承多少点"。存档里从来没有它——读档后正在生产的那一批 `inputLevel` 回落到 `-1`（未初始化），本批产物于是按 **0 点原料**结算。
   - 取证（`DSPQS_DEV=1`）：F5 探针直接显示 `inputLevel=-1`；同一批的升品日志 `[QSystem][roll] eid=… in=0 pooled=2`（`in=0` 是"本批输入点数"，`pooled=2` 是产物池里已经攒下的点数）。
   - 结论：不是四舍五入的问题，是**持久化缺字段**——四舍五入只改变"每批点数怎么算"，丢字段会让整批归零，比取整误差严重得多。两件事都在 v0.19.1 修。
2. **用户报"7 点处理器"**：用户描述「10 点制造台、6 点原料，产物却是 7 点，应该是 8 点」。定量归因：
   - 制造台本批品质 = `min(各输入槽的每件平均点数)`，其中每件平均点数 = `int(槽总 inc / 槽件数)`（**原版是整除截断**，本 MOD 沿用）。
   - 该批 5 件原料共 29 点（料带上混进了少量低点数料，实际平均 `5.8`）→ 截断成 **5 点** → 产物池按 5 点记 → 10 点建筑再走"必 +2"→ 最终 **7 点**。
   - 改四舍五入后：`(29 + 5/2) / 5 = 31/5 = 6` → 产物池按 6 点记 → 6 + 2 = **8 点**，与用户预期一致。
   - 注意这**不是**"建筑品质没生效"：10 点的 +2 一直生效，差的是"这批原料到底算 5 点还是 6 点"。

### 27.34.2 实现

- **A 批次点数持久化**（`Save\QualitySave.cs`）：
  - 写：行星节点在 `pools` 之后追加可选节点 `"inputs":[{"e":entityId,"l":level}]`，只写 `inputLevel >= 0` 的实体（`QualitySave.cs:467-490`，`sb.Append("],\"inputs\":[");`）。
  - 读：`var inputs = GetList(planet, "inputs")`，逐项读 `e`/`l`，`e<=0 || l<0` 跳过，其余 `QualityData.SetInputLevel(planetId, e, (short)Math.Min(l, 10))`（`QualitySave.cs:714-735`）。
  - **向后兼容**：旧档没有 `inputs` 节点 → 列表为空 → 什么都不做，`inputLevel` 保持 `-1`（与旧行为一致）；**schema `version` 仍为 1**（加字段不升版本，旧 DLL 读到新档会忽略该节点，只是又丢点数）。
  - 修后取证：同一台机器读档后 F5 显示 `inputLevel=6`，日志 `in=6 pooled=8`（与 §27.34.1 的两条旧值一一对应）。
- **B 批次点数四舍五入**（`QualityMath.cs` + `Patches\P0_Assembler.cs`）：
  - 新函数 `public static int BatchInputLevel(int incServed, int served, int maxLevel)`：`served <= 0 → 0`；负 `incServed` 归 0；`level = (incServed + (served >> 1)) / served`（half-up，`served>>1` 即 `served/2` 向下取整，所以 29/5 → (29+2)/5 = 6）；最后夹到 `[0, maxLevel]`。
  - `P0_Assembler` 里原来的整除截断替换为 `int average = QualityMath.BatchInputLevel(inc, have, Cargo.kIncLevelMax);`，再按原逻辑取各槽**最小值**作为本批品质。
  - DEV 探针 `[QSystem][assembler][batch] eid=… planet=… lvl=… s0=<served>/<incServed>/<requireCounts>`（`P0_Assembler.cs:136-146`），只在 `DSPQS_DEV=1` 时按条数上限打印——这就是定位 §27.34.1 那批 `29/5` 的直接工具。
- **C 喷涂次数按品质点数分级**（新增 `QualityData.SprayUses.cs` + `Patches\P6_SprayUses.cs`）：
  - 表（`SprayUsesMath.UsesForPoints`）：`p<=2 → 1`、`p<=4 → 2`、`p<=6 → 4`、`p<=8 → 5`、`p==9 → 7`、`else 10`；即 0–2→1、3–4→2、5–6→4、7–8→5、9→7、10→10。`MaxPoints=10`、`MinUsesPerItem=1`、`MaxUsesPerItem=10`、`DefaultCapacity=600`。
  - **为什么还要保留 `P6_SprayCount`**：原版所有插入路径都按 `件数 × HpMax` 记账（`incCount += count * HpMax`），罐内单位与"件"绑定。`HpMax=1` 让"罐内单位 = 次数 = 件数"，每件先只记 1 次；再由 `P6_SprayUses` 用**插前快照 + 插后补差**把这一件补到表值（补 `UsesForPoints(p) - 1` 次）。这样既不改原版 UI/tooltip 语义，也不需要重写整条插入链。
  - 补差覆盖 4 条插入路径（6 锚点，全或无 `_armed = ok >= 6`）：① `PlanetFactory.EntityFastFillIn(int,bool,out ItemBundle)`（手插/背包插，prefix 快照 + postfix 补差）；② `Player.TakeItemFromPlayer`（5 参数 postfix）；③ `SpraycoaterComponent.InternalUpdate(CargoTraffic,AnimData[],int[])`（struct/class 双签名，料带插入）；④ `CargoPath.TryPickItem`（5 参数 postfix，罐内搬运）。
  - **取回不能凭空复制**：原版 `CargoTraffic.TakeBackItems_Spraycoater` 按 `件数 = 次数 / HpMax` 反算，`HpMax=1` 时等于"有多少次就还多少件"——分级之后一件可能带 10 次，取回就会还 10 件，凭空复制增产剂。改为 **prefix 接管**，按每台喷涂机账本反算：`PerItemUsesDeduced(itemsIn, usesIn) = ceil(usesIn / itemsIn)`（每件当初投入时记了多少次），`ItemsForUses(uses, itemsIn, usesIn) = uses / perItem` 再**夹到 ≤ itemsIn**（取回件数永远不超过投入件数）。账本节点 `"sprayuses":[{"e":entityId,"i":itemsIn,"u":usesIn}]`（`QualityData.AppendSprayUsesJson` / `ApplySprayUsesJson`，按 entityId 升序、`Items<=0` 跳过、空则回撤逗号；读档先清该星球再按节点恢复，缺节点静默跳过）。**旧档无账本**时 `ItemsForUsesNoLedger(uses) = max(1, uses/10)` 保守回退（宁可少给）。
  - `CargoTraffic.ClearItems_Spraycoater` prefix+postfix 清账本；`VanillaExtraPerItem` 复现原版 `(int)(hpMax*incTable[p]*0.001)` 用于从总额里扣掉原版那部分，避免重复计算。
  - **Mk 档次无关**：`ItemProto.Ability` = Mk.I/II/III **1/2/4**（`Diag\StartupAudit.cs:475-476` 的 `ids={1141,1142,1143}` / `expected={1,2,4}`），每次喷涂给物品加的点数不变；本版只改"一件能用几次"。
  - 日志：`[QSystem][sprayuses] armed=… patch=insert+belt+takeback inserts=… beltPicks=… takebacks=… ledger(planets=… coaters=… items=… uses=…)`；事件行 `insert eid=…` / `belt eid=…` / `takeback eid=…` / `tank over capacity eid=…`。
- **D 分拣器品质阈值过滤重写**（`Patches\P5_SorterFilter.cs`，1084 行）：
  - **v0.18.0 的缺陷**：只 prefix `CargoTraffic.TryPickItem`。该方法只在"分拣器从传送带上抓货"时被调用，而**储物仓取货**、**机器产物口取货**、**弹射器/发射井取货**都走 `PlanetFactory.PickFrom(uint ioTargetTypedId, int offset, int filter, int[] needs, out byte stack, out byte inc)`，所以这些来源的低品质货照样被取走——用户看到的就是"分拣器阈值只对传送带有效"。
  - **现在两级挂钩**：① `CargoPath.TryPickItem` 的**两个带 filter 重载**（`plain` + `needs`）——皮带来源，prefix 用窗口 5 格（`WindowLength=5`）判断"这一件够不够"；② `PlanetFactory.PickFrom(uint,…)` 的 `Priority.High` prefix，按 `EFactoryIOTargetType` 分来源处理（`SourceCount=8`：`belt/storage/assembler/ejector/silo/station/lab/powerGen`）：
    - `storage`：只读网格扫描，镜像原版 `TakeTailItems` + `split_inc` 的取栈顺序，**不修改任何状态**；
    - `assembler`：只"**预测**下一件产物的点数"——不足就返回 false 拦下，足够就返回 true 放行，**绝不消耗产物池点数**（真正的扣点仍由 `P0_PickFrom` 做）；
    - `ejector` / `silo`：用 `bulletInc` / `bulletCount` 预检；
    - `station` / `lab` / `powerGen`：**明确跳过**并在 `Describe()`/日志里说明——物流塔的 `TakeItem` 选择序与 `keepMode` 无法安全复现，研究站产物没有品质池（点数恒 0），燃料本身没有点数。
  - **与 `P0_PickFrom` 互斥**：两者都是 `PickFrom(uint)` 的 prefix，本补丁用 `Priority.High` 先跑——返回 `true` 表示"放行"，让 `P0_PickFrom` 继续按品质扣点；返回 `false` 表示"拦下"，此时 P0 也不会跑，所以不会出现"拦了却已经扣了池点数"。来源 = `assembler` 时本补丁**永远**返回 `true`（只做判断，不做扣点）。
  - 武装是全或无：tick 3/3（`InserterComponent.InternalUpdate` / `InternalUpdate_Bidirectional` / `InternalUpdateNoAnim`）+ belt 2/2 + pickFrom 1/1，任一不足 `Disable(...)`。`Describe()` 增加 `pathCalls` / `pathMin0` / `pickCalls` / `pickMin0` / `scanned` 与按来源的 `bySource=[belt:p/b/s storage:…]`。
- **E 版本**：`Plugin.cs` 的 `VERSION` 与 csproj → **0.19.1**；`Plugin.Awake` 在 `P6_SprayCount` 之后注册 `P6_SprayUses` 并打印 `[QSystem][sprayuses] …`。

### 27.34.3 构建与测试

- 离线：新增 `tests\OfflineTests\Checks.V0191.cs`（`BatchInputLevel`：`29/5→6`、`10/4→3`、`7/2→4`、`9/4→2`、`0/1→0`、负投入/0 除数→0、`max=0`→0、钳 `maxLevel`）与 `Checks.P6SprayUses.cs`（分级表 0–10 全表、账本加/取/清、`PerItemUsesDeduced`、`ItemsForUses` 夹取回上限、无账本回退）。
- 规模 **86 → 94 checks**（`Program.cs` 挂 `CheckBatchInputLevel` 等，输出 `ALL PASS (94 checks)`）。
- 实机核对清单（**待构建后执行**）：①读档后制造台探针 `inputLevel=6`、日志 `in=6 pooled=8`；②一台 10 点喷涂机插 10 点增产剂后可用 10 次、3 点增产剂 2 次；③反复取回不增件（件数 ≤ 投入件数）；④分拣器阈值在储物仓 / 机器产物口 / 弹射器 / 发射井生效，`bySource=` 有对应计数，`station`/`lab`/`powerGen` 记 skipped；⑤`[QSystem][sorterfilter] armed … tick=3/3 belt=2/2 pickFrom=1/1`。

### 27.34.4 文件

- 新增：`QualityData.SprayUses.cs`、`Patches\P6_SprayUses.cs`、`tests\OfflineTests\Checks.P6SprayUses.cs`、`tests\OfflineTests\Checks.V0191.cs`。
- 修改：`QualityMath.cs`（`BatchInputLevel`）、`Patches\P0_Assembler.cs`（改调用 + DEV 探针）、`Patches\P5_SorterFilter.cs`（重写）、`Save\QualitySave.cs`（`inputs` / `sprayuses` 节点）、`Plugin.cs`、`DSPQualitySystem.csproj`、`tests\OfflineTests\Program.cs` / `OfflineTests.csproj`。
- 文档：`README.md`、`README-工程.md`、`README-打包说明.md`、开发文档（§2.2 / §2.8 / §2.11 / §3.3 / §4.1 / §4.2.6 / §4.2.8 / §4.6 / §6.1–§6.4 / §7.8 / §7.11 / §9）、`TESTLOG.md`、`tools\r2` 两份。

### 27.34.5 版本链

- v0.18.0（§27.32）：82 checks（253,952 B）。
- v0.19.0（§27.33）：喷涂次数 = 1 + 品质回收机（271,360 B，86 checks）。
- **v0.19.1（本节）：读档丢批次点数修复 + 批次四舍五入 + 喷涂次数分级 + 分拣器过滤重写**（DLL `293,376` B，SHA `9F5E1D94E954C2EFF5961A87D4999210C4B25A0CD81797C80F840B26E39A0154`，`2026-10-31 17:15:58` 构建，**94 checks**）。
- 补丁统计：`Patches\` 下 38 个补丁 `.cs` 文件 / 40 个补丁类（18 特性 + 22 手工）；`P6_SprayUses` 追加 6 个锚点，逐条以开发文档 §4.2 各表为准（v0.19.0 口径的 74 次注册 / 67 个独立方法不再作为累计基数使用）。

## 27.35 v0.19.2（2026-10-31）

### 27.35.1 用户口径（实机反馈原意）

- **① 仓库 → 传送带丢品质点数**：分拣器设了品质阈值后，从储物仓抓货送上传送带，货的点数没有被保留（看起来像"点数被清零/丢掉"），而同一批货从传送带抓是正常的。
- **② 蓝图复制出来的分拣器全变 6**：把带筛选设置的分拣器/四向分流器做成蓝图再粘贴，落成后各台的品质筛选设置全被抹平成同一个值（表现为"全变 6"），玩家得逐台重设。
- **③ 仓库界面残留旧货 + 崩溃**：取走货后仓库界面仍显示旧货、要关掉面板重开才消失；另有一次打开仓库界面时崩溃。

### 27.35.2 根因

- **储物仓分支的 id 映射错**（对应 ①）：`P5_SorterFilter` 的 `storage` 来源把**实体 id（entityId）**直接当成 `storagePool` 的下标去取网格组件，而原版顺序是先 `entityPool[entityId].storageId` 映射、再用 `storagePool[storageId]` 取。错用后命中空组件 → `grids=0` → 读不到货的真实点数，于是"达标判定"与"点数回填"都失效，点数在搬运环节被丢掉。
- **`lastEmptyItem` 缓存门控**（对应 ①/③ 的一部分症状）：上一次扫描空手而归时结果被缓存，导致阈值或物品过滤变化后不立即生效；"只设品质阈值、不选物品"的场景更是被这道门控直接短路，等于阈值没设。
- **取货后没通知界面**（对应 ③ 的残留旧货）：取走货后没有调用 `NotifyStorageChange()`，仓库 UI 没有重算，于是继续画已取走的旧货，直到关闭重开面板。
- **蓝图块在「预览 → 预建」环节被截断**（对应 ②）：蓝图里的建筑在 `BuildTool_BlueprintPaste.CreatePrebuilds()` 阶段先变成"预建"，此时**还没有落地实体 id**；只在 `AddEntityDataWithComponents` 一处写筛选设置的话，这段中间态无处安放，设置就丢了——表现正是"粘贴后全变 6（默认值）"。
- **仓库扩容的越界崩溃**（对应 ③ 的崩溃）：`storageQualityGrids` 开启时容器 `size` 只在内存被放大，`UIStorageGrid` 按放大的格数去索引原版数组，触发 `System.IndexOutOfRangeException at UIStorageGrid._OnUpdate()`。

### 27.35.3 修复

- **新增 `Patches\P6_FilterCopy.cs`（11 个挂点，全或无）**：
  - 建筑设置复制（Shift 拖放 / Ctrl+C·Ctrl+V）：`BuildingParameters.CopyFromFactoryObject` prefix、`BuildingParameters.PasteToFactoryObject` postfix、`PlanetFactory.CopyBuildingSetting` prefix、`PlanetFactory.PasteBuildingSetting` postfix；
  - 蓝图**两段式**：`BuildTool_BlueprintPaste.CreatePrebuilds()` prefix 遍历 `blueprint.buildings`，用 `TryReadBlueprintBlock(building.parameters)` 解码入队（日志 `bp-stage queued=N buildings=M`）→ `PlanetFactory.AddPrebuildDataWithComponents` postfix 按 `protoId` 把设置绑到 `prebuildId`（日志 `bp-stage prebuild id=.. proto=.. kind=.. min=..`）→ `PlanetFactory.AddEntityDataWithComponents` postfix 写到落地实体；
  - 持久化：`BuildingParameters.ToParamsArray` postfix / `FromParamsArray` prefix / `GenerateBlueprintData` prefix+postfix，把 **9 个 int 的魔数块**追加在 `BlueprintBuilding.parameters` 尾部——magic `0x51465042`（`'QFPB'`）、version 1、kind（1=四向分流器 / 2=分拣器）、protoId、rule0..3、min；原版与未装本 MOD 的客户端仍能正常读这份蓝图，只是不认筛选设置；
  - 启动日志：`[QSystem][filtercopy] armed=True reason=patched 11/11 (BuildingParameters copy/paste/apply + Copy/PasteBuildingSetting + blueprint params/save/load + prebuild stage)`。
- **`P5_SorterFilter` 三处修复**（不动锚点、只改实现）：① 储物仓分支改为 `entityPool[entityId].storageId` → `storagePool[storageId]` 的正确映射，修掉 `grids=0` 与丢点数；② 删除 `lastEmptyItem` 缓存门控，阈值/物品过滤变化与"只设阈值不选物品"立即生效；③ 补 `NotifyStorageChange()`，取货后立刻刷新仓库界面。锚点仍为 tick=3/3、belt=2/2、pickFrom=1/1 全或无。
- **关闭「储物舱随品质扩容」**：`QualityConfig.StorageQualityGridsValue` 改为 `false`（功能停用、行为等同删除），修掉 `UIStorageGrid._OnUpdate()` 越界崩溃；代码与存档兼容逻辑保留，旧档尾块 `storages` 的溢出数据读档静默忽略（只 `WarnOnce`）。启动日志固定 `[QSystem][storage] NOT ARMED (storageQualityGrids=false; vanilla containers)`。
- **回滚 v0.19.1 的「喷涂次数按品质分级」那组离线 checks**（`Checks.P6SprayUses.cs` 6 条），离线规模 94 → 88。
- **新增 DEV 诊断探针**（仅 `DSPQS_DEV=1` 生效，正常游戏静默）：`storage-probe`、`storage-pick`、`pick-return`、`inserter-hold`、`stack-insert2`、`stack-insert2b`、`cargo-insert`、`insert-into`、`bp-stage`、`blueprint block written (save)` / `blueprint block read`。

### 27.35.4 构建与测试

- 版本：`Plugin.cs` 的 `VERSION = "0.19.2"`；`DSPQualitySystem.csproj` 为 `0.19.2` / `0.19.2.0` / `0.19.2.0`。
- 运行 DLL：**298,496 字节**，SHA256 `A0BE6EBF3EEF1BB587C7EB6BA0FD32FEEFFDD8F7C98DC58C2B5308298775A8C7`，mtime `2026-10-31 21:46:49`。
- 离线测试：**88 checks ALL PASS**（v0.19.1 为 94；本版撤回「喷涂次数按品质分级」6 条 checks，94 − 6 = 88）。
- 启动自检预期：`[QSystem][sorterfilter] armed: sorter quality threshold is enforced on tick=3/3 belt=2/2 pickFrom=1/1`、`[QSystem][filtercopy] armed=True reason=patched 11/11 (…)`、`[QSystem][storage] NOT ARMED (storageQualityGrids=false; vanilla containers)`。

### 27.35.5 实机验证（2026-10-31 21:24 用户确认）

- **① 通过**：分拣器从储物仓抓货到传送带，**品质点数成功保留**（对应根因的 `entityId → storageId` 映射修复）。
- **② 通过**：蓝图复制出来的分拣器 / 四向分流器**成功保存各自的品质筛选设置**。日志证据：`bp-stage queued=4 buildings=20`，随后 4 条 `bp-stage prebuild … min=6/8/7`，四向分流器为 `kind=1`。
- **③ 通过（本版已修）**：仓库界面不再崩溃、也不再显示已取走的旧货。
- **④ 观察项**：储物舱格数不再随品质点数变化（`[QSystem][storage] NOT ARMED …`）。

### 27.35.6 版本链

- v0.18.0（§27.32）：82 checks（253,952 B）。
- v0.19.0（§27.33）：喷涂次数 = 1 + 品质回收机（271,360 B，86 checks）。
- v0.19.1（§27.34）：读档丢批次点数修复 + 批次四舍五入 + 喷涂次数分级 + 分拣器过滤重写（293,376 B，94 checks）。
- **v0.19.2（本节）：品质筛选随复制/粘贴与蓝图迁移 + 分拣器储物仓映射修复 + 储物舱扩容停用 + 88 checks**（DLL `298,496` B，SHA `A0BE6EBF3EEF1BB587C7EB6BA0FD32FEEFFDD8F7C98DC58C2B5308298775A8C7`，`2026-10-31 21:46:49` 构建）。
- 补丁统计（v0.19.2）：`Patches\` 下 **39 个补丁 .cs / 42 个补丁类**（20 特性 + 22 手工；实测口径），**85 次方法级注册 / 去重 78 个独立方法**；本版新增 `P6_FilterCopy` 一类 11 个锚点。
