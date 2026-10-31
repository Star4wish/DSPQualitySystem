# DSP 品质系统 Mod — 端到端回归记录（T5 / 文档 §6.3）

版本：**v0.11.11** SHA256 `1A00BEE3683951362653736CB43C541E5EB59C4C20A5BA7238FD6DC1EB403547`（162,304 B，AssemblyVersion 0.11.11.0，离线 `ALL PASS (38 checks)`，`patchedMethods` 启动 17 / 运行时 24）。
> 注意：下表 #1–#10 的实机证据仍是 **v0.4.1 / v0.4.2 时代**（2026-10-22，pid 27036/31460）；v0.4.4 实机仅确认 `patchedMethods=13`；v0.7.0 实机确认 `patchedMethods=15` 与钩子在跑；v0.8.0 实机确认拆除返还（#14 PASS）；**v0.10.0 已实机**（拆除+背包隔离 #17 机制链 PASS、充能 3× #13 PASS；重建回 4 点待补）；**v0.11.0 已实机**（2026-10-25，pid 14916：启动全绿、建筑品质跨重启 `4=7` 往返、#15 矿机多段 `inc=2`、#18 充电上限 `cap eid=127 level=10 factor=6.00x energyPerTick=1000000->6000000`、F11 10 点注入 `F9 … 4=5 10=5`）—— **v0.11.1 已实机**（2026-10-25 晚，pid 12540：`sort patch registered … prefix=SortPrefix` + `patch points=2` + `sort regrouped items=75 grids=75 building-groups=44`，整理后「采矿机 0 点」与「采矿机 4 点」仍各占一格 ⇒ #19 PASS；`[QSystem][roll] eid=153/133/31 in=0 bld=10 mode=2 pooled=2` 结案 #16）；剩余待测见文末「交接状态」与 `开发文档\问题记录-20261025.md`。
本轮（2026-10-22 21:08）实机证据来源：`BepInEx\LogOutput.log`（13,172 B，pid 27036，`_lastexit_` 存档）+ `BepInEx\config\DSPQualitySystem\saves\`；第 3/6/7 条已由 Lead 独立核验后补录。
记录方式：每条 = 场景 / 期望 / 实测证据（日志行原文 + 归档文件）/ 结论（PASS / FAIL / 未测）。
日志路径：`C:\Program Files (x86)\Steam\steamapps\common\Dyson Sphere Program\BepInEx\LogOutput.log`；归档目录 `C:\XCM\DSP MOD\logs\LogOutput-*.log`（`dev-loop.ps1 -Mode up/down` 自动归档；下表引用归档文件名）。

| # | 场景 | 期望 | 证据（日志原文 + 来源文件） | 结论 |
|---|------|------|------|------|
| 1 | 喷涂 4 点的铁矿 → 熔炉 → 磁铁 | 磁铁 inc=4 | (a) 旧构建 0.2.0 实测：`[Probe][F5] planet=103 cursor=2 enabled=True` / `[Probe][F5] eid=54 recipe=2 inputLevel=4 poolInc=0 poolCount=0 buildingInc=0` / `[Probe][F7] recent assembler PickFrom calls (newest first), total=715` / 20 行 `[Probe][F7] eid=54 item=1102 inc=4`（1102=磁铁）→ 4 点矿→磁铁 4 点，来源 `LogOutput-20261022_190757-previous.log`；(b) 修复版：`[Probe][F5] planet=103 env=103 cursor=2 enabled=True` / `eid=54 recipe=2 inputLevel=4 pending=4 repl=1 poolInc=0 poolCount=0 buildingInc=0`，来源 `LogOutput-20261022_191146-final-pid31460.log`（本轮 v0.4.1 回归待测） | 部分通过（旧构建 PASS，修复版待回归） |
| 2 | 4 点磁铁 + 0 点铜 → 线圈（多原料取 min） | 线圈 inc=0 | 本轮 `[Probe][F7] env=103 … total=203` 的 20 行全为 `eid=26 item=1102`（磁铁），无线圈 PickFrom 记录 ⇒ 该场景本轮未被用户执行，仍待补测 | 待测 |
| 3 | 用 4 点磁铁建造熔炉 → 该熔炉产物 | 产物 inc 高于原料（R-2 升品） | 建造捕获链（M2 前半）已 PASS：`[Probe][F10] DEBUG INJECTION: item=2319 count=49 perItem=4` → `[Probe][Build] prebuild quality captured: batch=3 total=3` → `[Probe][Build] bound entityId=22 buildingInc=4` / `entityId=26 buildingInc=4` / `entityId=23 buildingInc=4`，来源 `LogOutput-20261022_192215-previous.log`。**R-2 升品已实机 PASS（本轮）**：`[Probe][F11] DEBUG INJECTION: entityId=26 proto=2319 buildingInc=4`（冶炼厂 4 品）→ `[Probe][F5] eid=26 recipe=2 inputLevel=0 pending=0 repl=1 poolInc=0 poolCount=0 buildingInc=4`（喂 0 点矿，继承 0）→ `[Probe][F7] env=103 … total=203` + 20 行 `eid=26 item=1102 inc=1` ⇒ 0（原料）+1（建筑升品，测试配置 buildingChancePerPoint=1000/maxUpgradeChance=100 ⇒ 每批必升） = 磁铁 inc=1，产物高于原料；来源本轮 `BepInEx\LogOutput.log`（21:08 写盘，13,172 B） | PASS（建筑升品生效） |
| 4 | 0 点熔炉的产物 | 继承 0 点（不凭空升品） | 待本轮进档 | 待测 |
| 5 | 4 点矿机采矿 | 概率出 1 点矿（默认 8%/件；测试配置 100%） | **PASS（v0.7.0 实机，2026-10-25 日志）**：`[Probe][F6] minerId=1 type=Vein workstate=Running productId=1002 productCount=10 veinCount=4` + `vein: id=9 productId=1002 amount=696`（在采、矿脉非空）→ F11 注入 `entityId=1 proto=2301 buildingInc=4` → `[Probe][Miner] quality hit eid=1 inc=1 hits=1…20` → `[Probe][F12] miner quality hits=660 calls=660 lastMinerCall=eid=1 level=1 enabled=True minerChancePerPoint=1000` → `[Probe][F9] planet=103 entities=63 buildingInc … 4=1`。矿机产出物品已带 inc=1（点数写进货物）。本轮矿机 `insertTarget=0`（没接出货带）⇒ 未做下游熔炉 F5 交叉验证，但"Miner→item inc"链路已实证 | ✅ PASS |
| 6 | 存档 → 退出 → 读档 | buildingInc/pool 保留，json 只含非零项 | **本轮实机 PASS**：`[QSystem][save] wrote …\saves\沙盒.json planets=1 buildings=1 pools=0`、`[QSystem][save] wrote …\saves\_lastexit_.json planets=1 buildings=1 pools=0` → 重进 `_lastexit_`：`[QSystem][save] loaded '_lastexit_' (…\_lastexit_.json): entries=1` → `[Probe][F5] eid=26 … buildingInc=4`、`[Probe][F9] planet=103 entities=63 buildingInc … 4=1`；文件原文：`沙盒.json`(93 B)=`{"version":1,"gameName":"沙盒","planets":{"103":{"buildings":[{"e":26,"i":4}],"pools":[]}}}`、`_lastexit_.json`(97 B) 同构且 `gameName="_lastexit_"`（只存非零项：仅 e=26,i=4；`pools` 空数组键保留）；来源本轮 `BepInEx\LogOutput.log` + `BepInEx\config\DSPQualitySystem\saves\` | PASS |
| 7 | 拆掉 4 点熔炉后原地重建 | 新熔炉 0 点（无 entityId 污染） | **本轮实机 PASS**：拆除前 `[Probe][F9] planet=103 entities=63 buildingInc 0=62 … 4=1` → 拆除 `eid=26` 后 `[Probe][F9] planet=103 entities=63 buildingInc 0=63 1=0 … 4=0`（4 品计数归零、无残留；同文件连续两条 F9 即拆前/拆后）；清理 hook `patched: … PlanetFactory.DismantleFinally …`；来源本轮 `BepInEx\LogOutput.log` | PASS |
| 8 | 蓝图复制/粘贴建筑 | 0 品、不崩溃 | 待本轮进档（设计上蓝图不经过 UseHandItems/CreatePrebuilds 捕获路径 ⇒ 0 品） | 待测 |
| 9 | `enabled=false` 重启 | 原版 4 表还原、无品质效果、无异常 | `[QSystem][tables] disabled (Awake): vanilla tables restored` ×3（Awake / UIGame._OnCreate / LDB ready），incTable=[0,125,200,225,250,275,300,325,350,375,400]、powerTable=[0,300,700,1100,1500,1900,2300,2700,3100,3500,3900]，来源 `LogOutput-20261022_185258-final-pid27088.log` | 表还原 PASS；玩法行为本轮复测 |
| 10 | 传送带合并不同点数物品 | 总点数守恒（池加总/均分近似，无点数凭空增减） | 离线池守恒 1e6 组 PASS（见下）；游戏内传送带合并断言待本轮 | 部分通过（离线 PASS，游戏内待测） |

| 11 | 分馏塔产物品质（v0.5.0 / M4-P2） | 成功产物继承输入平均点数并按 R-2 升品；出货带带点；换配方清池 | 启动日志 `[QSystem][fractionator] anchors ok, pooled=on siteA=1 siteB=1`（退化为 `anchor NOT FOUND (...) -> vanilla fallback`）；`[QSystem][fractionator] SetRecipe prefix active, pool cleared on recipe change`；F11 注 4 点后 F9 `4=1`、产物取走按 F7 看 `inc>0`；堵带 20s 验证 take→insert→回滚不丢点 | 待实机 |
| 12 | 建筑品质 → 制造/采集速度（v0.6.0 / P3-1） | 4 点 ≈1.08×、10 点 ≈1.20×（默认 2%/点、cap +50%）；0 点=原速 | `[QSystem][speed] assembler eid=… level=4 factor=1.08x power=A->B`；聚合 `[QSystem][speed] assembler effective=N (viaEnv=X fallback=Y)`（fallback 占比高=并行 tick 走了兜底 planetId） | 机制已实证、3× 幅度待补：v0.7.0 实机日志 `[QSystem][speed] assembler eid=… level=4 factor=1.08x power=1.00->1.08`（前缀确实改写了 `power` 参数）；`power` 即速度倍率由 IL 证明——`AssemblerComponent.InternalUpdate` 的唯一用法是 asm_full.cs:25740 `time += (int)(power * (float)speedOverride);` / :25741 `extraTime += (int)(power * (float)extraSpeed);`；HarmonyX 2.5.5 的 prefix `ref` 回写已由 IL 证据链确认（DEVLOG §19.5）。Lead 在 3× 配置（`speedPerPoint=50 maxSpeedBonus=500`）下暂未取到 4 点制造台的倍率日志（当时唯一在册制造台是 0 点且停机 ⇒ 取证场景问题，非代码问题）；**v0.10.0 已补**：4 点制造台 `[Probe][F5] eid=31 recipe=6 inputLevel=0 pending=0 repl=1 buildingInc=4`（有料在产）→ 连续 **20 条** `[QSystem][speed] assembler eid=31 level=4 factor=3.00x power=1.00->3.00`，聚合 `assembler effective=7400 (viaEnv=0 fallback=7472)` 持续增长 ⇒ 前缀确实把 `power` 从 1.00 抬到 3.00；默认 2%/点(+8%) 肉眼不可辨属幅度问题而非未生效 | ✅ PASS（3× 幅度，v0.10.0 实机 2026-10-25 pid 20428） |
| 13 | 物流塔充能加成（v0.7.0 / P3-2a） | 充能速度随建筑品质提升；**总电量不变、单位时间取电上升（不是省电）**；0 点=原速 | `[QSystem][charge] armed: station energy charge bonus (chargePerPoint=2.0 maxChargeBonus=50.0%)`；`[QSystem][charge] station eid=… level=4 factor=1.08x power=0.52->0.62`；`effective=N (env=none)`；塔电量条回充时间对比 | **✅ PASS（3× 幅度，Lead 实机 2026-10-25，v0.7.0→v0.8.0 会话）**：实机 cfg `chargePerPoint=50 maxChargeBonus=500` 下日志 `[QSystem][charge] station eid=129 level=4 factor=3.00x power=1.00->3.00`、`[QSystem][charge] station eid=1 level=4 factor=3.00x power=1.00->3.00`（4 点×50%/点=+200% ⇒ 3.0×，与公式一致）；聚合器 `[QSystem][charge] station effective=… (env=none)` 持续增长（单会话到 13000+）。默认 2%/点(+8%) 时肉眼不可辨，属幅度问题而非未生效 | ✅ PASS |
| 13b | 充能不能量语义（v0.7.0） | 只乘 `power`，不写 `energyPerTick/energyMax` | IL 依据：`StationComponent.InternalTickLocal` 开头 `0x0002-0x0013` = `energy += (int)(energyPerTick * power)`，`power`(ldarg.3) 全方法唯一读取 0x000E；`energy/energyPerTick/energyMax` 是存档字段且 `SetPCState` 每 tick 重算 ⇒ 单位时间取电上升、总电量不变 | ✅ 设计已验证（离线+IL） |
| 14 | 拆除返还品质（v0.8.0 / P3-2d） | 拆 4 点建筑 → 回背包物品 inc=4 → 重建后建筑仍 4 点；`giveDismantleInc=false` 时与 v0.7.0 完全一致（inc=0） | 启动：`[QSystem][dismantle] patch registered: PlayerAction_Build.DoDismantleObject anchor ok …` + `[Audit] PlayerAction_Build.DoDismantleObject(int) -> OK` + `[Audit] dismantle inc armed (…)`；拆除时 `[QSystem][dismantle] inc returned eid=… points=4 inc=4`；回背包物品 F7/F10 交叉看 inc=4；退化为 `[QSystem][dismantle] NOT ARMED (vanilla inc=0): …` | **✅ PASS（Lead 实机 2026-10-25，v0.8.0，pid 22088）**：启动 `[QSystem] === DSP Quality System v0.8.0 ===`、`[QSystem] Harmony PatchAll OK, harmony=2.5.5.0 patchedMethods=15`、`[QSystem][dismantle] anchor ok: DoDismantleObject inc argument replaced (was ldc.i4.0, now QualityData.DismantleInc)`、`[QSystem][dismantle] dismantle inc armed (giveDismantleInc=true; …)`、`[Audit] PlayerAction_Build.DoDismantleObject(int) -> OK`；实机 cfg `giveDismantleInc=True`（旧 cfg 已手改 true，印证"BepInEx 保留旧值不套新默认"）；拆除 4 点 `eid=40`（proto 2102）→ `[QSystem][dismantle] inc returned eid=40 points=4 inc=4`，拆前 `[Probe][F9] planet=103 entities=129 buildingInc … 4=4` → 拆后 `… 4=3`；同会话重进存档时 F9 仍 `4=4` ⇒ 建筑品质 json 跨存档往返正常 | ✅ PASS |
| 15 | 矿机多重概率升级（v0.9.0 / P3-2e） | 判定次数 = floor(点数/2)，单件矿石点数上限 = floor(点数/2)；0/1 点矿机不出点；`[Probe][Miner] quality hit … inc=` 为实际点数 1–5 | 启动：`[QSystem] rule: miner rolls = floor(points/2) (multi-point ore, cap = half); extra upgrade above=6 guaranteed +2 at=10`；F12 注入 4/10 点矿机 → F6 看 `productCount`、F12 `miner quality hits=` 与 `[Probe][Miner] quality hit eid=… inc=1..5`；采 20 件矿应有部分 inc≥2（10 点矿机） | **✅ PASS（Lead 实机 2026-10-25，v0.10.0，pid 20428，测试 cfg `minerChancePerPoint=1000`）**：启动行 `cfg … minerChancePerPoint=1000` + v0.9.0 规则行 `[QSystem] rule: miner rolls = floor(points/2) …`；用户悬停矿机按 F11 → `[Probe][F11] DEBUG INJECTION: entityId=132 proto=2301 buildingInc=4`（2301=采矿机）；随后连续 **20 条** `[Probe][Miner] quality hit eid=132 inc=2 hits=1..20`（**inc 恒为 2 = floor(4/2)，证明 4 点=2 次判定且全部成功，不是旧的固定 1 点**）；`[Probe][F12] miner quality hits=13569 calls=55758 lastMinerCall=eid=132 level=2 enabled=True minerChancePerPoint=1000`（level=2 即单件产物点数）；用户侧视觉确认"矿机输出的矿物都带 2 点增产"。限制：本轮 1000% 必成，未覆盖 2% 默认下的 0/1/2 分布 | ✅ PASS |
| 16 | 高点数建筑额外升品（v0.9.0 / P3-2e） | 7/8/9 点建筑产物最多 +2（两次同概率判定）；10 点建筑产物**必定** +2；0 点仍恒 0；`extraUpgradeAbove=0`+`doubleUpgradeAt=0` 时与 v0.8.0 完全一致 | 启动日志 `cfg … extraUpgradeAbove=6 doubleUpgradeAt=10`；F11 注 10 点熔炉、喂 0 点矿 → F7/F5 看产物 `inc=2`（恒定）；7 点建筑多次 F7 抽样应出现 0/1/2 分布；两键改 0 后行为回到旧单次判定 | **⏳ 部分 PASS（Lead 实机 2026-10-25，v0.11.0，pid 14916）**：启动行 `cfg … extraUpgradeAbove=6 doubleUpgradeAt=10`；`debugInjectPoints=10` 下 F11 可注 10 点 —— `[Probe][F11] DEBUG INJECTION: entityId=153 proto=2302 buildingInc=10`（熔炉）、`entityId=133 proto=2303 buildingInc=10`（制造台）→ `[Probe][F9] planet=103 entities=255 buildingInc … 4=5 10=5`、`[Probe][F5] eid=153 recipe=3 inputLevel=4 pending=4 repl=1 buildingInc=10`（规则已生效）。**产物逐件 +2 待 v0.11.1 的 `[QSystem][roll]` 只读日志取证**（`pooled=in+3` 判 10 点必 +2、`mode=1` 判 7/8/9 概率 +1）；离线 `CheckExtraUpgrade` 已覆盖 | ✅ PASS（Lead 实机 2026-10-25 晚，v0.11.1，pid 12540）：v0.11.1 的只读 roll 日志取证到 **10 点建筑恒 +2** —— `[QSystem][roll] eid=153 in=0 bld=10 mode=2 base=0 extra=0 pooled=2 allowExtra=true`（`eid=133`、`eid=31` 同形，三条 10 点建筑均 `mode=2` + `pooled=2`、跳过判定直接 +2）；对照组 `[QSystem][roll] eid=42 in=4 bld=4 mode=0 base=1 extra=0 pooled=5`、`[QSystem][roll] eid=115 in=2 bld=4 mode=0`（4 点建筑仍是单次 +1）。7/8/9 点无现成样本（离线 `CheckExtraUpgrade` 覆盖）。仅只读日志、RNG 未变（离线 `P3-2e roll log (roll count pinned / logging cannot change RNG)` PASS） |
| 17 | 背包建筑品质隔离（v0.10.0 / P4） | 玩家背包里同种建筑按每件点数分格：拆 4 点建筑 → 背包 4 点物品**独立成格、不与 0 点合并** → 建回仍 4 点；0 点传送带/地基与同类 0 点仍照常合并（不碎片化）；矿石/材料等非建筑照原版合并；`isolateBuildingStacks=false` 回原版 | 启动：`[QSystem][isolation] patch registered: StorageComponent.AddItemStacked prefix=AddItemStackedPrefix …` + `[QSystem][isolation] armed (building items=N, patch points=1)` + `[Audit] StorageComponent.AddItemStacked(int,int,int,out int) -> OK` + `[Audit] isolation: building items = N` + `[Audit] isolation armed (…)`；拆 4 点建筑看背包格子（`debugProbes` 下 `[QSystem][isolation] split item=… inc=… grids=…`）；退化为 `[QSystem][isolation] NOT ARMED (vanilla merge): …`；已知边界：交付包/手持仍合并、UI 手动整理绕过 | **✅ PASS（机制链，Lead 实机 2026-10-25，v0.10.0，pid 20428）**：启动 `[QSystem] === DSP Quality System v0.10.0 ===`、`[QSystem] Harmony PatchAll OK, harmony=2.5.5.0 patchedMethods=15`、`[QSystem][isolation] patch registered: StorageComponent.AddItemStacked prefix=AddItemStackedPrefix …`、`[QSystem][isolation] armed (building items=63, patch points=1)`、`[Audit] StorageComponent.AddItemStacked(int,int,int,out int) -> OK`、`[Audit] isolation: building items = 63`；实机 cfg `isolateBuildingStacks=True` + `giveDismantleInc=True`；拆 4 点建筑（`eid=129`，proto 2104）→ `[QSystem][dismantle] inc returned eid=129 points=4 inc=4` 紧接 **`[QSystem][isolation] split item=2104 inc=4 grids=2`**（4 点物品独立成格、未并入 0 点同类叠）；拆前 F9 `4=3` → 拆后 `4=2`。**未在本轮取证**：重建后仍 4 点（需把该 4 点物品放回地面，GUI 放置步骤未完成）；"两叠 0 点仍合并 / 0 vs 1 不合并"由离线 16 checks 覆盖 | ✅ PASS（机制链）；⚠️ 已知缺陷：背包"整理"按钮会绕过本补丁把不同点数同类建筑合并成 1 格（用户 2026-10-25 反馈，待修，见"已知限制"与 `开发文档\问题记录-20261025.md` #1） |
| 18 | 物流塔**充电上限**（v0.11.0 / P3-2a 重构） | 点数提升每 tick 可充入上限（`energyPerTick`）并同步抬电网需求（`requiredEnergy`）；总电量上限 `energyMax` 不变；满电/近满电不虚增需求；0 点=原版；采集器不覆盖 | 启动：`[QSystem][charge] patch registered: StationComponent.SetPCState postfix=StationChargeCapPostfix (energyPerTick + pcPool.requiredEnergy; power path retired in v0.11.0)` + `[QSystem][charge] armed: station charge cap bonus (…)` + `[Audit] StationComponent.SetPCState(PowerConsumerComponent[]) -> OK` + `[Audit] station charge cap bonus armed (…)`；实机大倍率（`chargePerPoint=50 maxChargeBonus=500` ⇒ 4 点 3.0×）下 debug 行 `[QSystem][charge] cap eid=… level=4 factor=3.00x energyPerTick=1200->3600 requiredEnergy=1200->3600`；聚合 `[QSystem][charge] cap effective=N (postfix on StationComponent.SetPCState)`；对比同配置 0 点塔电量条回充时间；退化为 `[QSystem][charge] cap NOT ARMED (vanilla charge): …`。注：v0.7.0–v0.10.x 的 `#13`（乘 `InternalTickLocal` 的 `power`）已退役，本条取代其语义 | **✅ PASS（Lead 实机 2026-10-25，v0.11.0，pid 14916）**：启动 `[QSystem] === DSP Quality System v0.11.0 ===` + `[QSystem][charge] patch registered: StationComponent.SetPCState postfix=StationChargeCapPostfix (energyPerTick + pcPool.requiredEnergy; power path retired in v0.11.0)` + `[QSystem][charge] armed: station charge cap bonus (chargePerPoint=50.0 maxChargeBonus=500.0%)` + `[Audit] StationComponent.SetPCState(PowerConsumerComponent[]) -> OK`；旧路径已无任何 charge 日志。实机 cfg `chargePerPoint=50 maxChargeBonus=500`；用 F11（`debugInjectPoints=10`）把 `eid=127`（proto 2104 星际物流运输站）注到 10 点后**连续多条**：`[QSystem][charge] cap eid=127 level=10 factor=6.00x energyPerTick=1000000->6000000 requiredEnergy=1000000->6000000`（10×50%=+500% ⇒ 6.0×；上限与电网需求同步抬高、`energyMax` 未动），聚合 `[QSystem][charge] cap effective=200/400/600 (postfix on StationComponent.SetPCState)` 持续增长；**满电塔不写**（满电守卫生效，先前 4 点满电塔无任何 `cap eid=` 行）| ✅ PASS |
| 19 | 背包"整理"不合并不同点数建筑（v0.11.1 / P4 sort） | 玩家背包点「整理/排序」后，同种建筑按每件点数仍各占一格（0 点与 4 点不合并、同点数合并、总数与总点数守恒）；矿石/材料等非建筑照原版合并；`isolateBuildingStacks=false` 回原版；异常/不确定时整段回退原版排序，绝不丢物品 | 启动：`[QSystem][isolation] sort patch registered: StorageComponent.Sort(bool) prefix=SortPrefix (building stacks are regrouped by per-item quality points)` + `[QSystem][isolation] armed (building items=N, patch points=2)` + `[Audit] StorageComponent.Sort(bool) -> OK` + `[Audit] isolation … sort armed (…)`；debugProbes 下前 20 次整包整理打 `[QSystem][isolation] sort regrouped items=… grids=… building-groups=…`；退化为 `[QSystem][isolation] NOT ARMED (vanilla sort): …`。实测：先放 0 点与 4 点熔炉各 1 个进背包 → 点整理 → 仍是两格（悬停看点数）；取 4 点熔炉建回仍 4 点 | ✅ PASS（Lead 实机 2026-10-25 晚，v0.11.1，pid 12540）：启动 `[QSystem] === DSP Quality System v0.11.1 ===` + `[QSystem][isolation] sort patch registered: StorageComponent.Sort(bool) prefix=SortPrefix (building stacks are regrouped by per-item quality points)` + `[QSystem][isolation] armed (building items=63, patch points=2)` + `[Audit] StorageComponent.Sort(bool) -> OK` + `[Audit] isolation armed (…)`；场景：背包已有 0 点采矿机叠，拆一台 4 点矿机 → `[QSystem][dismantle] inc returned eid=132 points=4 inc=4` + `[QSystem][isolation] split item=2301 inc=4 grids=2`（4 点独立成格）→ 点背包右上角「整理」→ `[QSystem][isolation] sort regrouped items=75 grids=75 building-groups=44` → 整理后背包里**「采矿机」两格并存**：一格「增产点数叠加：0 点」、紧邻一格「增产点数叠加：4 点」（截图 `C:\XCM\DSP MOD\logs\s11-sorted.png`、`crop-mv-c.png`）；对照 v0.11.0 之前该操作会并成一格。同点数合并/非建筑照原版（离线 3 条 P4Sort 断言：0vs4 两格、同点数合并、非建筑原版一致）；异常回退原版由代码快照还原保证 |
| 20 | 分段品质加成曲线（v0.11.2 / P3 曲线） | 制造台/采集器速度与物流塔充电上限按固定曲线表提升：点数 0–10 → `0/2.5/5/7.5/10/15/20/30/40/60/100 %`，10 点 = 2.00×；`speedBonusMode=curve`（默认），`linear` 回 v0.11.1 线性公式 | 启动日志 `cfg … speedBonusMode=curve`、`[QSystem][speed] … mode=curve`、`[QSystem][charge] … mode=curve`；`debugInjectPoints=10` 下 F11 注 4 点制造台 → `[QSystem][speed] assembler eid=… level=4 factor=1.10x`；10 点 → `factor=2.00x`；把 `speedBonusMode` 改 `linear` 后 4 点应回 `1.08x`（旧公式） | ✅ PASS（2026-10-26 pid 21896 / v0.11.2）：`[QSystem][speed] assembler eid=153/133/31 level=10 factor=2.00x power=1.00->2.00`、`eid=115 level=4 factor=1.10x power=1.00->1.10`；`[QSystem][charge] cap eid=222 level=10 factor=2.00x energyPerTick=1000000->2000000 requiredEnergy=1000000->2000000`，`cap effective=2400` |
| 21 | 蓝图携带建筑品质（v0.11.2 / P5 阶段A） | 复制蓝图时已建成建筑的品质点数写入 `BlueprintBuilding.parameters` 尾部（`0x51535101` + inc），粘贴后建筑保留点数；0 点建筑不写标记；`blueprintQuality=false` 时与原版蓝图逐字节一致；卸载插件后带标记蓝图仍可正常复制/粘贴/存档（原版忽略尾部 2 个 int） | 启动：`[QSystem][blueprint] patch registered: BlueprintUtils.GenerateBlueprintData postfix=BlueprintWritePostfix (building.parameters += magic+inc)` + `[QSystem][blueprint] armed: …` + `[Audit] BlueprintUtils.GenerateBlueprintData(…) -> OK` + `[Audit] blueprint quality armed (…)`；debugProbes 前 5 次 `[QSystem][blueprint] copy stored inc eid=… inc=… params=…` / `[QSystem][blueprint] paste restored inc prebuild=… inc=… (n=…)`；退化为 `[QSystem][blueprint] NOT ARMED (vanilla blueprints, reason: …)`。实测：4 点熔炉 → 复制成蓝图 → 拆掉 → 粘贴 → F9 确认仍是 4 点；**卸载兼容**：把 DLL 改名后同蓝图复制/保存/粘贴不报错 | ✅ PASS（2026-10-26 pid 21896 / v0.11.2）：`copy stored inc eid=112/133/153 inc=10 params=…`、`paste restored inc prebuild=3 inc=4 (n=1)`、`prebuild=7/29/33/49 inc=10 (n=2..5)`；F9 `4=5→6`、`10=5→14`（框选里的 4 点熔炉与 10 点建筑各自按自身点数恢复） |
| 22 | 容器建筑品质隔离（v0.11.3 / P4 容器） | 储物仓等通用 Default 容器里不同点数的同类建筑不并格；容器整理按钮也按点数重组；机甲燃料/弹药/炸弹/战斗机格、过滤容器、物流塔内部、储液罐保持原版合并；isolateStorageContainers=false 时与 v0.11.2 完全一致 | 启动：[QSystem][isolation] armed (building items=…, patch points=…, storage containers=on [AddItemStacked: … player + Default containers; sort …]) + [Audit] … storage containers=on；debugProbes 下 [QSystem][isolation] split item=… inc=… grids=… container=Default；实测：储物仓内放 0 点与 4 点熔炉各 1 → 两格并存；取 4 点熔炉建回仍 4 点；F9 分布不变；isolateStorageContainers=false 重启后回到合并。离线 24→27 checks | ⏳ 部分 PASS（2026-10-26 pid 21696… pid 7716 / v0.11.3）：离线 27 checks 全过；启动 `armed (building items=…, patch points=2, storage containers=on [AddItemStacked: player + Default containers; sort player + Default containers])` 与 `[Audit] … storage containers=on`；拆分日志已带 `container=player`。**容器内"0 点 vs 4 点同类建筑不并格"的 GUI 场景仍待复测**（把两种点数同时拖进同一储物仓，确认两格并存且容器整理后仍分格） |
| 23 | 蓝图品质结算 match（v0.11.4 / P5 阶段A 结算） | 粘贴蓝图时建筑落成点数 = `min(蓝图记录点数, 实际消耗物品点数)`（默认 `match`，绝不凭空升级）；`author` 信任蓝图记录、`off` 不带品质；未付款预建按 0 点落成（补付帧无法追溯）；收据按"同帧 + 同 itemId"配对，拿不到收据时 match 回退 author 并 warn 一次 | 启动：`[QSystem][blueprint] paste receipt: on (blueprint points are clamped to the paid item points)` + `[Audit] blueprint quality armed (mode=match, receipt=on, …)`；debugProbes 前 20 条：`[QSystem][blueprint] paste inc min(stored=10, hand=4) -> 4 eid=…` / `paste inc min(stored=10, hand=none) -> 0 eid=…` / `paste inc author stored=10 -> 10 eid=…` / `paste inc skipped (mode=off) eid=…`；退化为 `[QSystem][blueprint] paste receipt failed: …`。实测：①背包 10 点熔炉蓝图 + 手上 4 点熔炉粘贴 → 落成 4 点；②手上 0 点 → 落成 0；③手上 10 点 → 落成 10；④材料放背包（非手上）→ 尾格扣 4 点 → 落成 4；⑤材料不足 → 半透明预建补付后 0 点（已知限制）；⑥`blueprintQualityMode=author` → 落成 10 点、`off` → 0 点；⑦卸载兼容：改名 DLL 后蓝图复制/粘贴/存档不报错 | ⏳ 待实机 |
| 24 | 蓝图品质结算 `actual` + 优先消耗（v0.11.5 / P5 阶段A 结算 v2） | 粘贴蓝图时建筑落成点数**恰好等于实际被消耗那件物品的点数**（蓝图记录只用于日志，绝不凭空升级）；粘贴**优先消耗玩家持有的最高品质同类建筑物品**（手持同种物品优先于背包；背包内每件点数最高者优先、同点数低索引格优先），因此手拿 10 点物品 + 0 点蓝图 → 落成 10 点；`blueprintQualityMode=author` 信任蓝图记录、`off` 不带品质；拿不到收据（未付款预建/材料不足补付）按 0 点落成 | 启动：`[QSystem][blueprint] paste receipt: on (blueprint points are clamped to the paid item's points)`、`[QSystem][blueprint] source … armed`、`[Audit] blueprint quality armed (mode=actual, receipt=on, source=on, …)`；debugProbes 前 20 条：`[QSystem][blueprint] source pick: swapped hand(<n>pts x<k>) <- grid(<idx>, <m>pts x<j>) reason=higher` / `source pick: backpack grid(<idx>, <m>pts x<j>) reason=highest` / `pay eid=… stored=… actual=… source=… -> <final> (mode=actual)`。实测：①蓝图里 0 点熔炉 + 背包 10 点熔炉 → 粘贴落成 10 点（`pay … actual=10`）；②手握 10 点 → 优先用手持，落成 10；③手持 4 点但背包有 10 点 → 自动换成 10 点再消耗，落成 10；④`author` → 蓝图记录点数、`off` → 0 点；⑤材料不足 → 半透明预建补付后 0 点（已知限制）；⑥卸载兼容：改名 DLL 后蓝图复制/粘贴/存档不报错 | ⏳ 待实机 |
## 离线自动化部分（已完成）

- 命令：`powershell -NoProfile -ExecutionPolicy Bypass -File "C:\XCM\DSP MOD\DSPQualitySystem\tests\run-tests.ps1"`（需先 `$env:DSP_TOOLS_DIR='C:\XCM\DSP MOD\tools'`）
- 结果：`ALL PASS (21 checks)` / `TEST PASS`（v0.11.1 新增 4 条：`P4 sort basics (0 vs 4 point building stay in two grids)`、`P4 sort merge (same level merges / different level splits / stack overflow)`、`P4 sort non-building (vanilla merge + mixed backpack + determinism)`、`P3-2e roll log (roll count pinned / logging cannot change RNG)`）（v0.11.1 快照清 bin/obj 后复跑；池守恒 1e6 组、池边界、Config clamp、JSON 往返、存档身份校验 seed/gameName、R-3 分布 8.026%/1.964%、R-2 100% 必升品 + 6% 实测 6.034%、分馏塔输入档位重算、分馏塔池往返 produce/ship/rollback、P3 速度曲线/上限/0 点不动、P3 充能曲线/上限/0 点不动、v0.11.0 P3-2a 充电上限（曲线/满电跳过/0 点不动/非法输入不改/长整型不溢出）、P3 拆除返还 inc 的 config 门控/clamp/0 点、v0.9.0 矿机多段升级（rolls=floor(点数/2)/上限/单调/期望均值/0-1 点不出点）、v0.9.0 额外升品（7/8/9 概率 +2、10 点必 +2、0/0 配置回旧行为）、v0.10.0 P4 合并谓词（引用/分数等级/0vs0/0vs1/容量模型）与 split_inc 分布）
- 详细说明见 `DEVLOG.md` §8、§16（M4）、§17（P3-1）、§18（P3-2a）、§19（P3-2d，含 §19.5 byref 回写复核）、§20（P3-2e）、§21（P4）、§22（P3-2a 重构：充电上限）、§23（v0.11.1：背包「整理」Sort 隔离前缀 + roll 只读日志）。

## 已知限制（不属回归失败）

- 蓝图建造的产物/建筑为 0 品（设计如此，§2.5）。
- 拆除返还仅在 GUI 拆除路径生效（`PlayerAction_Build.DoDismantleObject`），批量/脚本拆除不返还；多栋叠放一次拆除时点数作用于整栈物品。
- 背包隔离只覆盖建筑类物品 + 只覆盖 `Player.TryAddItemToPackage → AddItemStacked` 这条路径：非建筑物品仍按原版合并；交付包与手持仍按原版合并；界面手动拖拽/整理/拆分（直接写 GRID）绕过本补丁（v0.10.0 P4 已知边界，见 DEVLOG §21.7）。
- **[未修・用户 2026-10-25 反馈] 背包"整理"按钮会把不同点数的同类建筑合并成一格**：点背包右上角"整理/排序"时走的是 `StorageComponent` 的整理路径（直接写 GRID / 重排），不经过 `AddItemStacked` 前缀，因此 4 点建筑会被并进 0 点同类叠（点数按原版合并规则处理）。⇒ 目前**在使用背包整理前请勿混放不同点数的同类建筑**；待修方案：补整理/重排路径的隔离谓词，或整理时按点数拆格（记录于 `开发文档\问题记录-20261025.md` #1）。
- 物流塔充电加成（v0.11.0）两条边界：`isCollector`/`isVeinCollector` 不调用 `SetPCState` 故不受加成；`energyPerTick` 每 tick 由原版重算，同帧存档时读档首 tick 最多多充一 tick 电量（自我修正，无 schema 变化，见 DEVLOG §22.8）。
- 矿机单件点数上限 = floor(建筑点数/2)（v0.9.0 起，2 点→1、10 点→5；0/1 点矿机不出点）；期望点数随点数平方增长（默认 2%/点：4 点→0.16、10 点→1.0 点/件），嫌强可调低 `minerChancePerPoint`（沿用原键）。

## 存档槽细节确认（Lead 指定核查，2026-10-22 实机 + 源码对照）

核查对象：`BepInEx\config\DSPQualitySystem\saves\` 下现有两个文件 + `Save\QualitySave.cs` / `Save\SaveGuard.cs`（只读核查，未改代码）。

1. **`pools` 为空时键仍保留**：`QualitySave.Save` 对每个星球固定写 `"buildings":[...],"pools":[...]` 两个键（数组可空）。实测 `沙盒.json`（93 B）= `{"version":1,"gameName":"沙盒","planets":{"103":{"buildings":[{"e":26,"i":4}],"pools":[]}}}`，`"pools":[]` 保留 ⇒ 与 §4.2 schema 一致，解析端可无条件读两个键。
2. **槽间不互串**：路径 = `saves\<存档名安全化>.json`（`QualitySave.GetPath` → `SanitizeFileName` 替换 `\/:*?"<>|` 与 `Path.GetInvalidFileNameChars()` 为 `_`）。`沙盒.json` 与 `_lastexit_.json` 各自独立；每份文件头写 `gameName` 与（v0.4.2 起）`seed`，`Load` 只读自己那份路径，不会去读另一个槽。
3. **身份校验（防"外部改名/换档"误读）**：`SaveGuard.ValidateDocument` 校验 `version==1` → `gameName` 相等 → `seed` 相等；任一不匹配返回 reason，`QualitySave.Load` 丢弃**整份文件**并打一条日志（`[QSystem][save] ignored …`，点数可丢、绝不污染当前档）。兼容策略：文件旧格式 `gameName` 空或 `seed=0` 时视为 legacy、不做该校验（当前 v0.4.1 写出的两份文件无 `seed` 字段，部署 v0.4.2 后首次存盘会自动补上）。
4. **`.dsv` 被外部改名场景**：json 仍按旧存档名留存、不会自动跟随；下次以新名存档时写新 json（旧 json 留作孤儿文件，不参与任何读档，可用 `tools\restore-defaults.ps1` 之外的路径手工清理，无副作用）。
5. **自动档/临时槽**：`_autosave_tmp` 无 json 时 `starting fresh`（实测日志 `[QSystem][save] no quality data for '_autosave_tmp' …`）；`_autosave_0..3`、`_lastexit_` 与普通槽同一套键规则，互不覆盖。
6. **读档时实体数变少 / entityId 复用**：`Load` 按 `GameMain.data.factories[planetId].entityPool.Length` 先 `QualityData.EnsureCapacity(planetId, capacity)` 再逐条 `SetBuildingInc`/`SetPool`；数组只增不减、越界条目不会写入；被拆建筑的旧点数由 `DismantleFinally` Postfix（`objId>0`）在运行时清空，读档后旧 entityId 若被复用则由新建筑的建造捕获重新赋值 ⇒ 不继承旧值（第 7 条实测支持）。

## 交接状态（2026-10-25 深夜，v0.11.1 时点）

- **当前交付版本 v0.11.3**：DLL `C378F309D226DDFCD4306687BE9BF576096A7871DBD28ACE752934B1A185F429`（116,224 B，AssemblyVersion 0.11.3.0），离线 `ALL PASS (27 checks)`；本机独立复现构建+测试通过，并已部署启动核验（`armed (… patch points=2, storage containers=on …)`、`[Audit] … storage containers=on`、`patchedMethods=15`）。交付包 `dist\DSPQualitySystem-0.11.3\`（7 文件）+ `dist\DSPQualitySystem-0.11.3.zip`（53,375 B，SHA256 `0766F2C60959D151A734CA460868344D1D3CB328952009A96FDB29E4FF9F66D3`）。v0.11.2 的 #20/#21/#22 见上；#22 的容器内「0 点 vs 4 点不并格」GUI 场景仍待复测。
- **交付包**：`C:\XCM\DSP MOD\dist\DSPQualitySystem-0.11.1\`（7 文件：DLL + release/test cfg + deploy.ps1 + README.txt + VERIFY-0.11.1.md + SHA256SUMS.txt）与 `dist\DSPQualitySystem-0.11.1.zip`（**51,144 B**，SHA256 `5426E1552301E92DEFAD8CCAC616120504695C31DE01D261CB8F2D091123E8BA`）。部署用 `deploy.ps1 -Profile test|release`（游戏须关闭；`-RevertLast` 回滚）。v0.11.0 包仍在 `dist\DSPQualitySystem-0.11.0*` 留档。
- **版本链（全部可复现实测）**：v0.4.3 `6E3E8676…`60,928B(7) → v0.4.4 `6363BF16…`60,928B(HarmonyX 2.5.5 兼容修复) → v0.5.0 `B7522F54…`68,096B(9，M4 分馏塔) → v0.6.0 `53410276…`74,752B(10，P3-1 速度) → v0.7.0 `1A6A4877…`80,896B(11，P3-2a 充能旧方案) → v0.8.0 `80B07E4B…`86,528B(12，P3-2d 拆除返还) → v0.9.0 `16087007…`88,576B(14，P3-2e 矿机多段 + 高点数额外升品) → v0.10.0 `B570C0F4…`95,232B(16，P4 背包隔离) → v0.11.0 `4FDF4180…`97,280B(17，P3-2a 重构：充电上限+电网需求) → **v0.11.1 `6392E8FD…`104,448B(21，修背包整理 + roll 日志 + 配置文案)**。
- **`patchedMethods` 口径**：启动打印 **15**（PatchAll 时点：13 attribute + 2 分馏塔；手动注册不计入）；v0.11.1 最终独立方法 **19** = v0.11.0 的 18 + `StorageComponent.Sort`（`InternalTickLocal` 已不再被 patch）。
- **回归表状态**：✅ #3 / #5 / #6 / #7 / #9 / #11 / #12（3×）/ #13 / #14 / #15 / #16（10 点滚动日志 + 必 +2）/ #17（机制链）/ #18 / **#19（整理不并格，实机两格 0 点 vs 4 点）**；✅ #20（分段曲线 2.00x/1.10x）/ #21（蓝图 copy→paste 保点，F9 4=5→6、10=5→14）；⏳ #22（容器隔离：离线 27 checks + 前缀接管容器已实机确认，容器内不并格 GUI 场景待复测）；⏳ #8（蓝图 0 品，设计如此，待抽测）；待复测 #1 / #2 / #4（证据仍是 v0.4.x 时代）；#10 按设计无需游戏内验收（用户裁定）。
- **✅ 已修缺陷（用户 2026-10-25 反馈，`开发文档\问题记录-20261025.md` #1）**：v0.11.0 及更早时背包「整理」按钮直接写 GRID、绕过隔离前缀，会把不同增产点数的同类建筑合并成一格。**v0.11.1 已修**：新增 `StorageComponent.Sort(bool)` 前缀按「每件点数」重组（实测 `sort regrouped items=75 grids=75 building-groups=44`），并在异常/不适用场景整段回退原版且先用快照还原（不丢物品）。新限制见 `dist\DSPQualitySystem-0.11.1\VERIFY-0.11.1.md` §5。
- **下一步（新环境接手顺序）**：① #8 蓝图 0 品抽测 + #1/#2/#4 老回归复测（证据仍是 v0.4.x 时代）；② 若要继续开发：P4 整理/隔离的边界补测（大背包、交付包路径、`isolateBuildingStacks=false` 对照）；③ 之后才考虑新功能（增幅基站用户已明确暂不做）。完整交接见 `C:\XCM\DSP MOD\开发文档\HANDOFF-v3.2-交接-20261025.md`。
- **环境**：DSP 0.10.35.29104 + BepInEx 5.4.17 / **HarmonyX 2.5.5.0**（编译引用却是 BepInEx.Core 5.4.21 ⇒ 类级 `[HarmonyPatch(typeof(T))]`+`TargetMethod()` 混写会让 PatchAll 整体中止，必须裸 `[HarmonyPatch]` 或手动 `harmony.Patch`）；构建需 `$env:DSP_TOOLS_DIR='C:\XCM\DSP MOD\tools'`（便携 SDK 10.0.401 / bepinex-lib 5.4.17 / refs）；ps1 一律 ASCII、PS 5.1。

---

## 规则修订（2026-10-26，用户口径）→ v0.11.5

用户原话："蓝图点数大于物品时按物品点数生效，物品点数大于蓝图时按物品点数生效，横竖都应该是看消耗的物品点数。蓝图真正要做的应该是优先消耗手中的高品质建筑。"

⇒ #23 的 match=min(蓝图记录, 实际消耗) 语义**作废**，改为：
1. **结算 = 纯实际消耗点数**（ctual 默认档）：落成点数恒等于被吃掉那件物品的点数，与蓝图记录无关；无收据 → 0。
2. **优先消耗最高品质的同类建筑**：粘贴时在手持 + 背包所有匹配格里选"每件点数最高"的先消耗（同点数手持优先，其次背包低索引），由 _inPaste 门限定的 UseHandItems/TakeTailItems 两个前缀实现，异常一律回原版。
3. 蓝图 parameters 里的 magic+inc 仍写（用于展示/兼容），但不再作为落成依据。

### v0.11.4 已取到的证据（保留，写入侧）
- 跑通 v0.11.4（DLL 121,344 B sha 8785B59D…2A68F8BD，29 checks）启动自检全绿：
  [QSystem][blueprint] receipt installed: hand UseHandItems + backpack TakeTailItems consumption points are observable、
  paste receipt: on (blueprint points are clamped to the paid item's points)、
  lueprint quality armed (mode=match, receipt=on, …)、[Audit] Player.UseHandItems(int,out int) -> OK、patchedMethods=15。
- **写入侧离线独立验证**：用户手动保存的 C:\Users\Administrator\Documents\Dyson Sphere Program\Blueprint\新蓝图 001.txt（2,033 B，23:47:49）→ 解 BLUEPRINT:1,…,"<base64>" 的 gzip 载荷得 6,619 B 原始数据，内含 **5 处 magic** 01 51 53 51 + inc(int32 LE)：偏移 1056=10、1178=10、3037=**4**、4225=10、5286=10 ⇒ 与存档里"4 栋 10 点 + 1 栋 4 点"一致（paste restored inc prebuild=3 inc=4 对应那个 4 点熔炉）。
- 未完成：v0.11.4 的粘贴实测在用户改口径前中断，不再补测（语义要改）。
---

## 交接状态（2026-10-26 01:05 停机打包，v0.11.10 时点）

- **当前交付版本 v0.11.10**：DLL `08EE21F7D3EBD51CA169929C53FD06398745BB7BF805B8CD5222417DEEE023B1`（157,184 B，AssemblyVersion 0.11.10.0），离线 `ALL PASS (33 checks)`；本机独立复现构建+测试通过。交付包 `dist\DSPQualitySystem-0.11.10\`（7 文件）+ `dist\DSPQualitySystem-0.11.10.zip`（81,211 B，SHA256 `9AD8EFA90E5B32340420782BE5FC8D32CC06189A1D0E6BF6A1A4F6100AD906C4`）。
- **版本链（全部可复现实测）**：v0.4.3 `6E3E8676…`60,928B(7) → v0.4.4 `6363BF16…`60,928B(HarmonyX 2.5.5 兼容) → v0.5.0 `B7522F54…`68,096B(9，分馏塔) → v0.6.0 `53410276…`74,752B(10，速度) → v0.7.0 `1A6A4877…`80,896B(11，充电 v1) → v0.8.0 `80B07E4B…`86,528B(12，拆除返还) → v0.9.0 `16087007…`88,576B(14，矿机多段 + 高点额外升品) → v0.10.0 `B570C0F4…`95,232B(16，背包隔离) → v0.11.0 `4FDF4180…`97,280B(17，充电上限) → v0.11.1 `6392E8FD…`104,448B(21，修整理) → v0.11.2 `5078A6F9…`113,152B(24，分段曲线 + 蓝图写 marker) → v0.11.3 `C378F309…`116,224B(27，容器隔离) → v0.11.4 `8785B59D…`121,344B(29，match 结算，已被 v0.11.5 取代) → v0.11.5 `922E766C…`126,464B(29，通用优先消耗) → v0.11.6 `96890B48…`138,752B(32，屏幕描边框) → v0.11.7 `3D294231…`140,288B(32) → v0.11.8 `04A3BACD…`141,312B(32，被否) → **v0.11.10 `08EE21F7…`157,184B(33，金色光环)**。
- **回归表新增结论**：
  - ✅ #20 分段曲线（实机 `assembler eid=153/133/31 level=10 factor=2.00x`、`eid=115 level=4 factor=1.10x`；`charge cap eid=222 level=10 factor=2.00x energyPerTick=1000000->2000000`、`cap effective=2400`）
  - ✅ #21 蓝图写 marker（实机 `copy stored inc eid=112/133/153 inc=10`、`paste restored inc prebuild=3 inc=4 / prebuild=7,29,33,49 inc=10`；F9 `4=5→6`、`10=5→14`）
  - ✅ #22 充能上限（同上 `cap eid=222`）
  - ⏳ #22b 容器内"0 点 vs 4 点不并格"GUI 场景（离线 27 checks 覆盖 + 前缀接管已实机确认，仅差一次手动复测）
  - ✅ #23 蓝图结算口径（v0.11.5 起改为"按实际消耗物品点数 + 优先消耗高品质"，`blueprintQuality` 默认 false）
  - ✅ #26 金色光环（v0.11.10 实机：`[halo] halo armed (... lift=1.2 size=1.0 shader=Legacy Shaders/Particles/Additive)`、`marked=11 visible=11 max=64`；截图 `logs\s0-world.png`、`logs\s0-near.png`；用户已确认观感）
- **明确不做**：传送带品级、储液罐容积（用户裁定）；增幅基站（用户明确暂不开发）。
- **下一步（新环境接手顺序）**：① 先跑 `dist\DSPQualitySystem-0.11.10\deploy.ps1 -Profile release` 部署 + 按 `VERIFY-0.11.10.md` 自检；② 补 #22b 容器 GUI 复测与 #1/#2/#4 老回归；③ 继续开发：task-16「A 类五件套」（研究站速度 / 防御塔射速 / 发电功率 / 弹射器+发射井 / 喷涂机概率，设计已批准，8 个 `.hold` 源码在 `DSPQualitySystem\Patches\` 内未进编译）；④ 之后 C 类两项（C-1 仓库质变格数试验、C-2 四向分流器「品质优先」开关）。
- **环境**：DSP 0.10.35.29196 + BepInEx 5.4.17 / **HarmonyX 2.5.5.0**（编译引用 BepInEx.Core 5.4.21 ⇒ 类级 `[HarmonyPatch(typeof(T))]`+`TargetMethod()` 混写会让 PatchAll 整体中止，必须裸 `[HarmonyPatch]` 或手动 `harmony.Patch`）；构建需 `$env:DSP_TOOLS_DIR='C:\XCM\DSP MOD\tools'`（便携 SDK / bepinex-lib 5.4.17 / refs）；ps1 一律 ASCII、PowerShell 5.1；打包前先删 `tests\OfflineTests\bin|obj` 再跑测试。

## 交接状态（2026-10-26 23:30，v0.11.11 时点：品质数据内嵌 .dsv）

- **本版改动**：品质数据从 `BepInEx\config\DSPQualitySystem\saves\<槽名>.json` 侧文件改为**内嵌在 `.dsv` 尾部**（`[int32 长度][UTF-8 JSON][8B magic "DSPQSQ1\0"]`，由 `GameData.Export` Postfix 追加、`GameData.Import` Postfix 读取，schema 与旧 json 逐字节同构）。MOD 目录下不再产生任何存档文件；旧 json 在读档时一次性迁入内存，该档下一次保存写入尾块后才删除；卸载 MOD 后原版照常读档（尾部字节被忽略，fileSize 由原版自己回填）。
- **交付物**：`bin\Release\DSPQualitySystem.dll` = **162,304 B**，SHA256 **`1A00BEE3683951362653736CB43C541E5EB59C4C20A5BA7238FD6DC1EB403547`**，AssemblyVersion **0.11.11.0**；构建 `BUILD OK`（0 警告 0 错误）；离线 **`ALL PASS (38 checks)`**（33 旧 + 5 条 codec 检查）；`patchedMethods` 启动 **17** / 运行时 **24**（新增 `GameData.Export`、`GameData.Import` 两个 Postfix）。**本版已实机验证（见文末「v0.11.11 实机验证」一节，8 个步骤全 PASS）。**
- **本版实机核对（优先级最高，全部与存档体积/尾块有关）**：
  1. 保存后 `BepInEx\config\DSPQualitySystem\saves\` 不再出现新 json；该槽旧 json 被删除，日志 `[QSystem][save] removed legacy sidecar <path>`；最后一个侧文件删完后空的 `saves\` 目录也会被一并清理（`RemoveLegacyDirectoryIfEmpty`）。
  2. 同一档退出重进：品质点数保留，日志 `[QSystem][save] embedded quality trailer used ('<gameName>'): planets=… entries=… bytes=…`。
  3. **复制该 `.dsv` 后临时移走插件 DLL**，原版能正常读档（关键证据：尾部字节被忽略、fileSize 正确、无 `Incomplete game save`）。
  4. 自动存档轮转：`_autosave_0..3` 各自携带自己的品质数据、互不串档（旧方案的槽名错位问题消失）；`_lastexit_` 同样内嵌。
  5. 读 v0.11.10 旧档：点数保留 + 日志 `legacy sidecar used for '<槽名>' …`，此时 json **仍在**；手动保存一次后 json 消失、再读档走尾块路径。
- **版本链追加**：…… v0.11.9 `8E14425B…`156,160B(33) → v0.11.10 `08EE21F7…`157,184B(33，光环修正) → **v0.11.11 `1A00BEE3…`162,304B(38，内嵌 .dsv)**。
- **回归表口径变化**：v0.11.10 及更早的 24 条结论不变；其中 #6（存档往返）与「存档槽细节确认」里凡以 `saves\*.json` 内容为证据的条目，从本版起改按尾块日志核对（机制等价，证据来源从侧文件变为 `.dsv` 尾部）。旧的 json 证据保留为历史记录，不再新增。
- **环境**：DSP 0.10.35.29196 + BepInEx 5.4.17 / HarmonyX 2.5.5.0；构建 `$env:DSP_TOOLS_DIR='C:\WorkWork\DSP MOD\DSP-MPD-runtime-debug\tools'`；打包前先删 `tests\OfflineTests\bin|obj` 再跑测试；ps1 一律 ASCII、PowerShell 5.1。

## v0.11.11 实机验证（2026-10-26 23:42–00:06，Lead 执行，全部 PASS）

环境：`DSPGAME` pid 28260 / 10740 / 31952 三个会话；存档 `沙盒.dsv`（seed 25361413，planet 103，10 点上限关闭态）。所有 `.dsv` 校验用本次新增工具 `_live\check-dsv.ps1`（读 VFSAVE 头、offset 6 的 fileSize、文件尾 magic、`[len][payload][magic]` 帧自洽性），备份在 `_live-backup\v0.11.11-verify\`。

| 步骤 | 证据（日志/文件原文） | 结论 |
|---|---|---|
| 1 部署 + 启动 | `dll=…\BepInEx\plugins\DSPQualitySystem\DSPQualitySystem.dll`、`assembly=0.11.11.0`、`=== DSP Quality System v0.11.11 ===`、`patchedMethods=17`、`patched: … GameData.Export, GameData.Import, GameSave.SaveCurrentGame, GameSave.LoadCurrentGame, GameData.NewGame`、`startup report complete` | ✅ 两个新 Postfix 注册成功 |
| 2 读旧档（侧文件迁移） | `no embedded quality trailer ('沙盒'): no trailing data, vanilla save` → `loaded legacy sidecar '…\saves\_lastexit_.json': entries=6` → `legacy sidecar used for '_lastexit_' (…): migrated in memory, this .dsv gets its own trailer on the next save`；侧文件此时仍在 | ✅ 旧档一次性迁移，不写回不删 |
| 3 手动保存（暂停菜单「保存游戏」→「保存」） | `embedded quality trailer written ('沙盒'): planets=1 buildings=0 pools=5 bytes=239` + `removed legacy sidecar …\saves\沙盒.json`；`沙盒.dsv` length **7,049,012** = offset 6 fileSize（原版自回填，未手工改），尾部 `44 53 50 51 53 51 31 00`，payloadLen **239**，payload 原文 `{"version":1,"gameName":"沙盒","seed":25361413,"planets":{"103":{"buildings":[],"pools":[{"e":106,"pi":0,"pc":494394},{"e":130,…574337},{"e":139,…415899},{"e":180,…367856},{"e":196,…327396}]}}}` | ✅ 尾块写入 + 侧文件删除 |
| 4 优雅退出写 `_lastexit_` | `embedded quality trailer written ('沙盒'): planets=1 buildings=0 pools=5 bytes=239` + `removed legacy sidecar …\_lastexit_.json`；`_lastexit_.dsv` 7,049,749 B = fileSize，magic PRESENT | ✅ 退出存档同样内嵌 |
| 5 **原版兼容（硬证据）** | 备份带尾块 `_lastexit_.dsv` → 移走 `BepInEx\plugins\DSPQualitySystem\DSPQualitySystem.dll` → 启动原版 → 主菜单「继续」→ 进档成功（截图 `shot-20261026_235649.png`，同一星球/基地）；日志零 `QSystem` 行、**无 `Incomplete game save`**、无新增异常（仅既有的 NebulaAPI 缺失告警，与 MOD 无关） | ✅ 卸载/无 MOD 时原版能读带尾块存档 |
| 6 恢复 DLL + 恢复尾块档后读档 | `embedded quality trailer used ('沙盒'): planets=1 entries=5 bytes=239`（`Player.log`）→ 进档（`[QSystem][roll] eid=180…`、F9 `planet=103 entities=255` 正常），**且此时所有侧 json 已不存在** ⇒ entries=5 只可能来自尾块 | ✅ 读尾块路径打通 |
| 7 再退出写档（数据真的进了内存） | payload 中矿机池计数由读档时的 `494394/574337/415899/367856/327396` 增长为 `650392/870377/562151/498181/457380`，`_lastexit_.dsv` 7,144,898 B = fileSize、magic PRESENT、payloadLen 239 | ✅ 读→玩→写全链路往返，数据非静态副本 |
| 8 侧文件清理 | 游戏内 `BepInEx\config\DSPQualitySystem\saves\` 只剩最后一条老 `_autosave_tmp.json`（旧版遗留、seed 校验被忽略）；收尾时手工清掉该遗留文件与空目录（备份 `_live-backup\saves-20261026-231404\legacy-config`）⇒ 该目录现已完全不存在 | ✅ MOD 目录不再留任何存档文件 |

- **已知观察（非本版回归）**：本次读档后实体 53（9 天前测试存档里仅有的一条建筑点数 `{"e":53,"i":4}`）被清零，因此尾块 `buildings=[]`。判定为 `[DSP Common API] Checking missing machines!` 在 Import 之后拆除了"缺失 MOD 的机器"（该档来自装有「星环」MOD 的 2026-10-22 环境），拆除走 `PlanetFactory.DismantleFinally` → `QualityData.ClearEntity(planetId, objId)`，属预期清理；同一份数据里 5 条**原版矿机**池数据完好往返（见步骤 7）。旧 v0.11.1 之所以保留 e53/i4，是因为它的读档 Postfix 在 CommonAPI 清理之后执行、把已拆除实体的点数又写回内存（残留脏数据）。
- **未覆盖/待补**：① 自动存档轮转实测（`_autosave_tmp` → `_autosave_0..3`，机制上尾块随 `File.Move` 一起走，本次会话未触发自动存档）；② `_autosave_*` 槽的旧 json 回退迁移（代码路径存在，未制造场景）；③ 大档（24 MB 星环档）上的尾块写入耗时未测量（量级 +251 B 可忽略）。
- **构建/打包**：clean rebuild 后 SHA256 仍为 `1A00BEE3683951362653736CB43C541E5EB59C4C20A5BA7238FD6DC1EB403547`（构建确定性成立）；`tools\package.ps1 -SkipDeploy` → `dist\DSPQualitySystem-0.11.11.zip`（70,903 B，SHA256 `349F0C2274AB5278A8E256864167242B763CF63A0EF577E22EC59C045FC6E851`，内含 DLL + README.md）。

## 交接状态（2026-10-27 00:30，v0.12.0 时点：task-16「A 类五件套」）

- **本版改动**：建筑品质点数新增驱动五类设施——A1 研究站研究速度、A2 防御塔射速（高斯/激光/加农/等离子/导弹/近程等离子）、A3 发电机功率（风/光/地热/火/射线接收站）、A4 弹射器+发射井发射速度、A5 喷涂机概率 +1 点。A1–A4 共用 v0.11.2 分段曲线（10 点 = 2.00×）且只乘每 tick 由原版重算的瞬时 `power`/容量返回值；A5 用同曲线当概率（0 点 0%…10 点 100%），命中才 +1、上限 `Cargo.kIncLevelMax=10`、只加不扣。五个功能各一个独立开关（`labSpeedBonus`/`turretFireRateBonus`/`powerGenBonus`/`launcherSpeedBonus`/`sprayerIncBonus`，默认全 `true`），关闭时补丁根本不注册 = 原版逐字节等价。存档尾块 schema 不变。
- **交付物**：`bin\Release\DSPQualitySystem.dll` = **187,392 B**，SHA256 **`E7355998A9D1F6C9259F1CB956940C09C6BF0360126DA9D9A9D519487976A4B3`**，AssemblyVersion **0.12.0.0**；构建 `BUILD OK`（**0 警告 0 错误**）；离线 **`ALL PASS (43 checks)`**（38 旧 + 5 条 P5 A 类检查，含 ExtraBonuses 纯函数与喷涂事件断言）；`patchedMethods` 启动 **17** / 运行时 **39**（新增 15 个独立手动补丁）。
- **本版离线已验证**（不需要进游戏）：曲线倍率逐点（0/1/4/5/7/8/10/11 点 → 1.00/1.025/1.10/1.15/1.30/1.40/2.00/2.00）、关闭开关恒 1f、linear 模式回归 + maxBonus 封顶、单调性；喷涂概率逐点（0/2.5/10/15/30/40/60/100%，越界 clamp）；喷涂 +1 规则（恰好 +1、10 点必中、0 点不中、达上限不加、roll 收到正确概率、只加不扣、null/负值 no-op）；确定性 15% 序列 100 次恰 15 命中。全部为真实断言（非空转）。
- **本版实机核对（Lead 执行，按 DEVLOG §27.14.6）**：
  1. 5 个开关逐个设 `false`：`[Audit]` 对应行 `NOT ARMED`，运行时 `patchedMethods` 相应下降（全关回 24），行为回原版。
  2. A1 研究站：10 点 vs 0 点同条件研究进度对比（期望 ~2.00×），日志 `[QSystem][lab] effective=…`。
  3. A2 防御塔：10 点塔弹幕密度/弹药消耗明显加快，6 种塔各测一种，`[QSystem][turret] effective=…`。
  4. A3 发电机：10 点风/光/地热/火/射线容量提升，火电燃料同步多烧（双收益），`[QSystem][powergen] effective=…`。
  5. A4 弹射器/发射井：10 点 vs 0 点帆/火箭发射间隔，`[QSystem][launcher] effective=…`。
  6. A5 喷涂机：10 点喷涂机应 100% 给被喷物品 +1（到 10 点上限为止）；5 点约 15%；日志 `[QSystem][sprayer] +1 quality point` 与 `+1 hits=` 增长。
  7. 反射未命中（游戏版本改名）时只打 `[QSystem][p5] … not armed, vanilla: …`，游戏不崩、功能回原版。
  8. 存档：本版前后 `.dsv` 尾块仍可互读（`embedded quality trailer used`），schema 无变化。
- **`.hold` 清理**：6 个转正参与编译（`QualityData.Extra.cs`、`QualityMath.ExtraBonuses.cs`、`P5_Common.cs`、`P5_LabSpeed.cs`、`P5_PowerGen.cs`、`P5_TurretFireRate.cs`）；`P4_BlueprintReceipt.cs.hold` / `P4_BlueprintSource.cs.hold` 判定为已编译 `P4_ConsumePriority.cs` + `P4_ConsumePriority.Sources.cs` 的重复源码（`TakeBestGridPrefix` / `_inPaste` / `CapturePasteConsumed` 已在跑）→ 删除。目录内 `.hold` 归零。弹射器/发射井与喷涂机补丁源码原本缺失，本版新增 `P5_Launcher.cs`（227 行）与 `P5_Sprayer.cs`（200 行）。
- **版本链追加**：…… v0.11.10 `08EE21F7…`157,184B(33) → v0.11.11 `1A00BEE3…`162,304B(38，内嵌 .dsv) → **v0.12.0 `E7355998…`187,392B(43，A 类五件套)**。
- **环境**：DSP 0.10.35.29196 + BepInEx 5.4.17 / HarmonyX 2.5.5.0；构建 `$env:DSP_TOOLS_DIR='C:\WorkWork\DSP MOD\DSP-MPD-runtime-debug\tools'`；打包/测试前先删 `tests\OfflineTests\bin|obj`；ps1 一律 ASCII、PowerShell 5.1。本版未部署、未启动游戏（开发子智能体只改工作区代码/文档）。

## v0.12.0 实机验证（2026-10-27 00:32–00:40，Lead 执行，全部 PASS）

环境：`DSPGAME` pid 16108（v0.12.0 已部署，DLL 187,392 B / `E7355998…`）；存档 `沙盒`（seed 25361413，planet 103）；本机 cfg 为测试值 `buildingChancePerPoint=1000`、`minerChancePerPoint=1000`、`debugProbes=true`；验证夹具 `_live\inject-all-bonus.ps1`（改写 `.dsv` 尾块把 planet 103 的 255 个实体全写成 10 点，保留原 pools 并回填 offset 6 fileSize）。

| 步骤 | 证据（日志/文件原文） | 结论 |
|---|---|---|
| 1 启动 | `=== DSP Quality System v0.12.0 ===`、`harmony=2.5.5.0 patchedMethods=17`；`[QSystem][lab] patch registered: LabComponent.InternalUpdateResearch prefix=Prefix …` + `armed: research lab research speed bonus (labSpeedBonus=True)`；`[QSystem][turret] … for 6/6 methods` + armed；`[QSystem][powergen] … for 5/5 methods` + armed；`[QSystem][launcher] … EjectorPrefix` / `SiloPrefix` + `armed: … ejector=on silo=on`；`[QSystem][sprayer] patch registered: SpraycoaterComponent.InternalUpdate transpiler=SprayerTranspiler …` + `armed (sprayerIncBonus=True, patch points=1)` | ✅ 五个功能全部注册并 armed |
| 2 反射/Transpiler 站点 | `[Audit]` 15 行全 `-> OK`（`LabComponent.InternalUpdateResearch(7 params)`、6× `TurretComponent.Shoot_*`、5× `PowerGeneratorComponent.EnergyCap_*`、`EjectorComponent.InternalUpdate`、`SiloComponent.InternalUpdate`、`SpraycoaterComponent.InternalUpdate`）；`[QSystem][sprayer] transpiler matched the byte-write site at instruction #361 (inc = SprayBonusPoints(inc, entityId))` | ✅ 目标方法全部命中，喷涂机改写站点确定 |
| 3 读档兼容 | `embedded quality trailer used ('沙盒'): planets=1 entries=5 bytes=239` → 进档（截图 `shot-20261027_003243.png`） | ✅ v0.12.0 未破坏 v0.11.11 尾块 |
| 4 注入 10 点后读档 | `embedded quality trailer used ('沙盒'): planets=1 entries=260 bytes=4463`（255 建筑 + 5 池 ✓）、F9 `planet=103 entities=255 buildingInc 0=1 … 10=254` | ✅ 大批量建筑点数进内存 |
| 5 数值效果（A3） | `[QSystem][powergen] output bonus active (planet=103 entity=7 factor=2.00 capacity=480000000)`；`[QSystem][powergen] effective=200/400/…/1000 (methods=5 viaEnv=0 fallback=…)` 每 tick 命中 | ✅ 10 点 = 曲线满级 2.00× 实机确认 |
| 6 退出写档（255 条规模） | `embedded quality trailer written ('沙盒'): planets=1 buildings=255 pools=5 bytes=4481`；payload 255 条 `{"e":N,"i":10}`；pools `pi` 由 0 → `13420/26630/12936/12304/902`、`pc` 增长为 `657204/883746/568748/504453/457940` | ✅ 大条目量写盘正常 |
| 7 清理 | 用 `_live-backup\v0.12.0-verify-lastexit.dsv.bak` 还原 `_lastexit_.dsv` | ✅ 测试数据不留在玩家存档 |

- **非回归项**：`[Audit] StorageComponent.TakeTailItems(int&,int&,int&,bool) -> BYREF/OUT MISMATCH: ref Int32&,ref Int32&,out Int32&,Boolean` 在 v0.11.11 归档日志中同样存在（历史既有）。
- **未覆盖**：本档没有研究站/防御塔/弹射器/发射井/喷涂机建筑，A1/A2/A4/A5 的数值效果只能靠「同一 `FeatureFactorFrom` 曲线 + 反射与 Transpiler 站点 OK + 离线 5 条检查」推断，未做逐功能实机对比；5 个开关逐个关闭的 `NOT ARMED` 门控未实机跑。

## v0.12.1 实机验证（2026-10-27 00:44–01:07，Lead 执行，全部 PASS）

环境：`DSPGAME` pid 15712（v0.12.1，DLL 188,416 B / `01FF6FCE…`）；cfg `tenPointHaloSize=0.3`（其余 halo 键 `tenPointHalo=true` / `lift=1.2` / `max=64`）；验证夹具 `_live\craft-halo-test-save.ps1`（造 `vanilla body + 本MOD帧(255×10点, pools:[]) + 外来 MHUB 帧 + 3 字节垃圾`）。

| 步骤 | 证据（日志/文件原文） | 结论 |
|---|---|---|
| 1 光环默认值 | `[QSystem][halo] armed (max=64 lift=1.2 size=0.3 shader=…)`；截图 `logs\shot-20261027_010628.png`（小圆环与建筑占地相当）对比 `logs\shot-20261027_003610.png`（1.0× 时环直径约为建筑两倍） | ✅ 缩小 70% 视觉确认 |
| 2 汇编器越界加固 | 本次会话 `assembler.prefix failed` 出现 **0 次**（v0.12.0 会话曾出现 `IndexOutOfRangeException … at DSPQualitySystem.QualityData.SetWasReplicating ← Assembler_InternalUpdate_Quality.Prefix`）；`[QSystem][speed] assembler effective=…` 正常 | ✅ 护栏生效 |
| 3 尾块共存（核心） | 测试档尾部为「本 MOD 帧 + MHUB 帧 + 3 字节垃圾」，旧版必判 `bad length`；v0.12.1 日志 **`embedded quality trailer used ('沙盒'): planets=1 entries=255 bytes=4313`** | ✅ 倒序扫描找回被埋的帧 |
| 4 扫描定位旁证 | 一次 gameName 乱码版本被 guard 拒绝：`[Warning] [QSystem][save] embedded quality trailer ('沙盒') -> ignored: gameName mismatch: file='娌欑洅' expected='沙盒'` ⇒ 说明「埋在中间的帧」确实被扫描到并解析出了 gameName | ✅ 证明不是靠 EOF 侥幸 |
| 5 写档回归 | 退出写档 `embedded quality trailer written ('沙盒'): …` 正常；`_lastexit_.dsv` fileSize 自洽、magic PRESENT | ✅ 写路径未受影响 |
| 6 清理 | 用 `_live-backup\v0.12.1-lastexit-orig.dsv.bak`（7,149,942 B）还原 `_lastexit_.dsv`，清除 255×10 注入数据 | ✅ 玩家存档复原 |

- **离线**：`ALL PASS (44 checks)`（43 旧 + `save blob codec shared trailer (another mod appended after us)`；改语义的拒绝用例并入原 5 条，`bad length`/`bad magic` → `no trailer found`）。
- **已知取舍**：4 MB 扫描窗口；若他 mod 尾块 + 垃圾合计超过 4 MB 会记 `no trailer found`（宁可不读也不误读）。

## 交接状态（2026-10-27 01:10，v0.12.1 时点）

- **本版改动（相对 v0.12.0）**：① `tenPointHaloSize` 默认 `1.0` → `0.3`（用户反馈「金圈太丑，缩小 70%」，clamp 仍 0.3–3.0，想恢复写 1.0）；② 汇编器/装配机哨兵 `entityId` 越界加固（`QualityData.MaxEntityId = 1<<20`，7 个 setter 护栏 + `P0_Assembler` Prefix/Postfix 提前返回 + 一次性告警）；③ `QualityBlobCodec.TryRead` 改为文件尾 4 MB 内倒序扫描（解决 `DSP-MolecularHub` 的 `MHUBTRL1` 尾块把本 MOD 帧埋掉导致读不到的问题），`reason` 语义统一为 `no trailer found`。
- **交付物**：`bin\Release\DSPQualitySystem.dll` = **188,416 B**，SHA256 **`01FF6FCECF9BC45B8A56C449220E565206F5A88D712632DF28D16B9F241F17E8`**，AssemblyVersion **0.12.1.0**；`BUILD OK`（0 警告 0 错误）；离线 **`ALL PASS (44 checks)`**；`patchedMethods` 启动 17 / 运行时 39（本版未新增补丁方法）。
- **版本链追加**：v0.11.10 `08EE21F7…`157,184B(33) → v0.11.11 `1A00BEE3…`162,304B(38，内嵌 .dsv) → v0.12.0 `E7355998…`187,392B(43，A 类五件套) → **v0.12.1 `01FF6FCE…`188,416B(44，光环 0.3 + 越界加固 + 尾块共存)**。
- **待办（下一版）**：task-17 C-1（大小仓库品质质变格数）、task-18 C-2（四向分流器「品质优先」）、老回归 #1/#2/#4 与 #22b、A 类五件套逐功能实机数值对比、5 开关 `NOT ARMED` 门控实机验证。


## v0.12.2 实机验证（2026-10-28 00:14–01:15，Lead 执行，PASS）

环境：`DSPGAME` pid 30040 / 25764（v0.12.2，DLL 193,024 B / `61EB8F23…`）；cfg 新增 4 键首次生成即为默认值；游戏未进档（只验启动与 cfg 生效）。

| 步骤 | 证据（日志原文） | 结论 |
|---|---|---|
| 1 启动 | `[QSystem] === DSP Quality System v0.12.2 ===`、`Harmony PatchAll OK, harmony=2.5.5.0 patchedMethods=17` | ✅ 版本/补丁数正常 |
| 2 新键写入并读回 | `cfg … tenPointHaloSize=0.3 tenPointHaloMax=64 tenPointMarkerStyle=auto tenPointHaloColor=FFD24A tenPointHaloPulse=True tenPointHaloSpin=1` | ✅ 4 个新键进 cfg 且被读取 |
| 3 光环 armed 串 | `[QSystem][halo] armed (max=64 lift=1.2 size=0.3 style=auto color=FFD24A pulse=True spin=1.0 shader=Legacy Shaders/Particles/Additive)` | ✅ style/color/pulse/spin 全部生效 |
| 4 style 覆盖旧 bool | cfg 改 `tenPointMarkerStyle = both`（`tenPointOutline` 仍 `false`）重启 → `[QSystem][halo] armed (… style=both …)` + `[QSystem][outline] armed (max=64 width=2 scale=0.66/0.48 offsetY=12, …)` | ✅ 选择器真的能打开原本默认关闭的描边 |
| 5 还原 | 改回 `tenPointMarkerStyle = auto`，重启日志回到 `[outline] armed but disabled by config (tenPointMarkerStyle=auto, tenPointOutline=False)` | ✅ 向后兼容旧 cfg |

- **离线**：`ALL PASS (45 checks)`（44 旧 + `P6 halo config (hex colour / marker style / pulse / spin)`）。
- **未实机覆盖**（由离线检查兜住）：具体颜色观感、`tenPointHaloPulse=false` 恒定亮度、`tenPointHaloSpin=0/2`、非法颜色回退。

## 交接状态（2026-10-28 01:20，v0.12.2 时点）

- **本版改动（相对 v0.12.1）**：10 点建筑标记整套交给 cfg —— 新增 `tenPointMarkerStyle`（`auto`/`halo`/`outline`/`both`/`none`，默认 `auto` 保持旧行为）、`tenPointHaloColor`（`RRGGBB(A)`，默认原金色 `FFD24A`）、`tenPointHaloPulse`（默认 `true`，关掉后恒定 alpha 0.85）、`tenPointHaloSpin`（0–5 倍率，`0` 停转）。旧键 `tenPointHalo`/`tenPointHaloLift`/`tenPointHaloSize`/`tenPointHaloMax`/`tenPointOutline*` 全部保留且 `auto` 下语义不变。
- **交付物**：`bin\Release\DSPQualitySystem.dll` = **193,024 B**，SHA256 **`61EB8F2385AB890B9D4402DEF3642B3BCF74358D9986A69CC1A033C135A5E245`**，AssemblyVersion **0.12.2.0**；`BUILD OK`；离线 **`ALL PASS (45 checks)`**；`patchedMethods` 启动 17 / 运行时 39 不变（无新增补丁方法）；包 `dist\DSPQualitySystem-0.12.2.zip`。
- **版本链追加**：v0.11.11 `1A00BEE3…`162,304B(38) → v0.12.0 `E7355998…`187,392B(43) → v0.12.1 `01FF6FCE…`188,416B(44) → **v0.12.2 `61EB8F23…`193,024B(45，标记 cfg 化)**。
- **待办（下一版）**：task-17 C-1、task-18 C-2（v0.13.0 已完成，见下）、老回归 #1/#2/#4 与 #22b、A 类五件套逐功能实机数值对比、5 开关 `NOT ARMED` 门控实机验证。

## v0.13.0 待实机验证项（2026-10-28 构建，尚未实机；Lead 待执行）

环境：DLL `bin\Release\DSPQualitySystem.dll` = 218,112 B / SHA256 `384087A3E276A58471A3190BB1563BFCD2A1F817AC02E4953B2501A784555DBB` / AssemblyVersion 0.13.0.0；cfg 段 `[quality]` 新增 `storageQualityGrids`（默认 false）、`storageQualityGridsPerPoint`（0.5）、`storageQualityGridsMax`（20）、`splitterQualityPriority`（默认 false）。

| # | 验证项 | 期望证据（日志/观察原文） |
|---|---|---|
| 1 | 启动版本与补丁数 | `[QSystem] === DSP Quality System v0.13.0 ===`、`Harmony PatchAll OK … patchedMethods=17`（本版两个功能都是手动注册，PatchAll 时点不变） |
| 2 | 4 个新键首次生成即默认值 | `LogConfig` 末尾 `storageQualityGrids=False storageQualityGridsPerPoint=0.5 storageQualityGridsMax=20 splitterQualityPriority=False` |
| 3 | 默认关闭 = 原版 | `[QSystem][storage] NOT ARMED (storageQualityGrids=false; vanilla containers)`、`[QSystem][splitters] NOT ARMED (splitterQualityPriority=false; vanilla distribution)`；游戏内储物仓 30/100 格不变、分流器行为不变 |
| 4 | 自检覆盖新锚点 | `[Audit] GameData.Export(BinaryWriter) -> OK`、`[Audit] CargoTraffic.UpdateSplitter(ref SplitterComponent) -> OK`、`[Audit] CargoTraffic.UpdateSplitterAsync(ref SplitterComponent) -> OK`、两行 `[Audit] armed; …` ／ `NOT ARMED (…)` |
| 5 | 开 C-1 后注册 | `storageQualityGrids=true` 重启 → `[QSystem][storage] patch registered: GameData.Export(BinaryWriter) [shave+restore]`、`[QSystem][storage] rule armed: perPoint=0.5 maxExtra=20` |
| 6 | 点数 → 格数 | F11 给一座小储物仓注入 10 点 → 打开容器看到 **35 格**（30+5）；大储物仓 100→105；0 点仍是 30/100；`[QSystem][storage] expanded p=… e=… from=30 to=35`（debugProbes 时前 20 条） |
| 7 | **存档结构不变（硬约束）** | 扩容状态下保存 → 用存档工具/最小解析脚本确认该容器在 `.dsv` 里的 `size` 仍是 30/100、`grids` 条数不变、`bans ≤ size`；日志 `[QSystem][storage] save shave: containers=… overflowCells=…` |
| 8 | **卸载 MOD 后存档可读（硬约束）** | 移走 DLL → 用同一份带尾块的 `.dsv` 启动原版 → 能进档、储物仓内容正常（额外格里的物品丢失属预期） |
| 9 | 溢出格保存/恢复 | 把原版 30 格填满再往额外格放货 → 保存后日志 `save shave: … overflowCells=N (kept in the MOD trailer)` → 读档 `[QSystem][storage] restored p=… e=… base=30 cells=N`，物品回到额外格 |
| 10 | 品质下降/关闭回收 | 把该建筑点数改回 0 → 额外格为空时容器 1 秒内缩回 30 格；额外格有货时保持扩容且物品不丢 |
| 11 | 开 C-2 后注册 | `splitterQualityPriority=true` 重启 → `[QSystem][splitters] patch registered: CargoTraffic.UpdateSplitter + UpdateSplitterAsync …` |
| 12 | F3 与标题后缀 | 打开分流器面板 → 标题 `四向分流器 [Quality First]` 切换后即时增减；日志 `[QSystem][splitters] F3 splitter p=… e=… quality-first=ON/OFF toggles=N` |
| 13 | 品质优先分配 | 四条输入混品质（如 9/5/2/空）→ 高点数货先被送出（`debugProbes=true` 时 `priority order … inc=[…] order=[…]`）；同品质保持原输入序；四路输出/过滤规则不变 |
| 14 | 存档往返 | 分流器开关 ON 后保存 → 尾块 `"splitters":[{"e":<entityId>}]`；重进档标题仍有后缀、F3 可关；v0.12.x 旧档读入无报错（`storages`/`splitters` 缺省 = 空） |
| 15 | 原版优先级让路 | 给分流器设原版输入优先级（`inPriority`）→ 本功能不置换（与原版一致），无 `priority order` 日志 |
| 16 | 关开关回原版 | 两开关都改回 false 重启 → 两条 NOT ARMED；运行时 `patchedMethods` 回到 39（`GameData.Export` 本就在 17 内，不新增方法） |

- **离线已覆盖（不占实机）**：点数→额外格公式（floor/上限/双夹/退化）、`StorageTotalGrids`/`StorageGridsOutOfSync`、分流器稳定降序与空位沉底、`SplitterBestInputSlot`、`inPriority` 让路判定、尾块 `storages`/`splitters` 节点 JSON 往返与 v0.12.x 旧档兼容（`ALL PASS (50 checks)`）。
- **只有实机能验**：上表 1–16（尤其 #7 存档字段、#8 卸载可读、#9 溢出格搬运、#13 真实四路分配）。

## 交接状态（2026-10-28，v0.13.0 时点）

- **本版改动（相对 v0.12.2）**：① task-17 C-1「储物仓品质格数」——新增 `storageQualityGrids`/`storageQualityGridsPerPoint`/`storageQualityGridsMax`，运行时按建筑品质点数扩容通用 `Default` 储物容器，导出时把 `size` 收回原版值（存档结构零改动），溢出物品走尾块 `storages` 节点；② task-18 C-2「四向分流器品质优先」——新增 `splitterQualityPriority`，Prefix 只重排四条输入皮带的处理顺序（`CargoTraffic.UpdateSplitter` + `UpdateSplitterAsync` 两处），F3 + 标题后缀代替 checkbox（UI 注入风险说明见 DEVLOG §27.17.4），每台开关存尾块 `splitters` 节点；③ 尾块 JSON 加两个可选节点、`SchemaVersion` 保持 1（旧档可读）；④ 新增自检 3 条 + 5 条离线检查。
- **交付物**：`bin\Release\DSPQualitySystem.dll` = **218,112 B**，SHA256 **`384087A3E276A58471A3190BB1563BFCD2A1F817AC02E4953B2501A784555DBB`**，AssemblyVersion **0.13.0.0**；`BUILD OK`（0 警告 0 错误）；离线 **`ALL PASS (50 checks)`**；`patchedMethods` 启动 17 / 运行时 39（两开关默认关，与 v0.12.2 相同）→ 两开关都开 = **42**（新增 `CargoTraffic.UpdateSplitter`、`CargoTraffic.UpdateSplitterAsync`、`UISplitterWindow._OnOpen`）。
- **版本链追加**：v0.12.0 `E7355998…`187,392B(43) → v0.12.1 `01FF6FCE…`188,416B(44) → v0.12.2 `61EB8F23…`193,024B(45) → **v0.13.0 `384087A3…`218,112B(50，C-1 储物仓品质格数 + C-2 分流器品质优先)**。
- **待办（下一版）**：v0.13.0 待实机验证 16 项（上表）、老回归 #1/#2/#4 与 #22b、A 类五件套逐功能实机数值对比、5 开关 `NOT ARMED` 门控实机验证、C-2 真 checkbox UI（落点 `UISplitterWindow._OnOpen`）。
## v0.13.0 实机验证（Lead 执行，2026-10-28 01:00–01:30）

环境：`BepInEx\plugins\DSPQualitySystem\DSPQualitySystem.dll` = **218,112 B / SHA256 `384087A3E276A58471A3190BB1563BFCD2A1F817AC02E4953B2501A784555DBB`**（复核修复后重新构建，与 clean rebuild 哈希一致）；cfg `storageQualityGrids=true`、`splitterQualityPriority=true`、`debugProbes=true`；测试档 = `_lastexit_.dsv` 尾块注入 planet 103 实体 0–254 各 10 点（`_live\craft-halo-test-save.ps1`），验证完已用 `_live-backup` 里的干净备份复原。

| # | 验证项 | 结果 | 证据（原文） |
|---|---|---|---|
| 1 | 启动版本 / patchedMethods | PASS | `[QSystem] === DSP Quality System v0.13.0 ===`、`Harmony PatchAll OK … patchedMethods=17` |
| 2 | 4 个新键默认值 | PASS | `LogConfig … storageQualityGrids=False storageQualityGridsPerPoint=0.5 storageQualityGridsMax=20 splitterQualityPriority=False` |
| 3 | 默认关闭 = 原版 | PASS | `[QSystem][storage] NOT ARMED (storageQualityGrids=false; vanilla containers)`、`[QSystem][splitters] NOT ARMED (splitterQualityPriority=false; vanilla distribution)` |
| 4 | 自检覆盖新锚点 | PASS | `[Audit] GameData.Export(BinaryWriter) -> OK`、`[Audit] CargoTraffic.UpdateSplitter(ref SplitterComponent) -> BYREF/OUT MISMATCH`（检查器对 `ref struct` 的历史局限，方法实际已注册并生效）、`[Audit] CargoTraffic.UpdateSplitterAsync(ref SplitterComponent) -> OK` |
| 5 | 开 C-1 后注册 | PASS | `[QSystem][storage] patch registered: GameData.Export(BinaryWriter) [shave+restore]`、`[QSystem][storage] rule armed: perPoint=0.5 maxExtra=20` |
| 6 | 点数 → 格数 | PASS（修复后） | 8 只 `size=60` 储物仓全部 `[QSystem][storage] expanded p=103 e=60 points=10 base=60 extra=5 total=65`；`sweep done: containers=8 expandedInSweep=4 expandedTotal=8 (perPoint=0.5 maxExtra=20)` |
| 7 | 存档结构不变 | PASS | `[QSystem][storage] save shave: containers=8 overflowCells=0`；`.dsv` 由原版重新写出为 7,120,842 B 且原版可继续读 |
| 8 | 卸载 MOD 后存档可读（硬约束） | PASS | 移走 DLL 后原版启动读同一份档成功进档（截图 `logs\shot-20261028_012039.png`），`Player.log` 无 `Incomplete game save`、无任何 `[QSystem]` 行 |
| 9 | 溢出格保存/恢复 | 未覆盖 | 需要把原版 30/60 格填满再塞额外格（本档容器为空，`overflowCells=0`，尾块 `storages=0`）；公式与 JSON 往返已在离线 50 checks 覆盖 |
| 10 | 品质下降/关闭回收 | 未覆盖 | 需要 F11 改点数或拆建，GUI 命中不稳定 |
| 11 | 开 C-2 后注册 | PASS | `[QSystem][splitters] patch registered: CargoTraffic.UpdateSplitter + UpdateSplitterAsync (input-slot permutation); UISplitterWindow._OnOpen (title marker); hotkey F3 toggles the focused splitter` |
| 12–15 | F3 / 标题后缀 / 四路分配 / 让路 / 尾块往返 | 未覆盖（仅离线 + 反射 + 注册证据） | 需要鼠标命中分流器并打开面板，GUI 自动化不可靠；`splitters` 节点 JSON 往返与让路判定由离线检查覆盖 |
| 16 | 关开关回原版 | 未覆盖（部分） | v0.12.2 时点已验证「两开关全关 → 条条 NOT ARMED」，本版沿用同一门控路径 |

- **复核发现并修复的 bug（重要）**：C-1 首次实机完全未扩容。根因 `GameData.factories` 是按 `PlanetData.factoryIndex` 索引（`asm_full.cs:192889` / `186111` / `77079`），而 `StorageGrids.FactoryOf(planetId)` 与 sweep 循环把它当 planetId 用；本档 `planet.id=103` 但 `factoryIndex=0`，于是 sweep 读到 planet 0 的空点数表（`container … points=0`），永不扩容。修复后 `Factories` 一律经 `galaxy.PlanetById(planetId).factoryIndex` 映射；顺带修正 `QualitySave.EnsurePlanetCapacity` 的同型错误（v0.11.11 起存在，仅预扩容快路径）。详见 DEVLOG §27.17.9。
- **诊断日志（本版新增，`debugProbes` 门控，长期保留）**：`[QSystem][storage] sweep done: containers=… expandedInSweep=… expandedTotal=…`、`container p=… e=… storageId=… size=… type=… points=… supported=…`（最多 8 行）、`skip unsupported container p=… e=… type=… size=…`。
- **实机过程中被覆盖的测试档**：`沙盒.dsv` / `_lastexit_.dsv` 曾被注入数据后再保存；已用 `_live-backup\v0.11.11-verify\沙盒.dsv.trailer.bak`（7,049,012 B）与 `_live-backup\v0.12.1-lastexit-orig.dsv.bak`（7,149,942 B）复原，复原前的污染版本另存 `_live-backup\saves-20261028-pre-restore\`。

## v0.13.1 待实机验证（Lead 执行，本版交接时点 2026-10-28 21:23）

**交付物**：`bin\Release\DSPQualitySystem.dll` = **227,328 B**，SHA256 **`C91AAFF55D52F9A4208C9CC8D9E8D14382F0E9D11A2B642C7D1A0831BAC7AB22`**，AssemblyVersion **0.13.1.0**；`BUILD OK`（0 警告 0 错误）；离线 **`ALL PASS (54 checks)`**。
**patchedMethods 预期**：与 v0.13.0 相同——启动 **17**、运行时 **39**（默认关）/ **42**（`splitterQualityPriority=true`）；本版只改 `UISplitterWindow._OnOpen` postfix 的内部逻辑（追加 checkbox 构建），**不新增被 patch 的方法**。

| # | 验证项 | 期望 / 证据 | 备注 |
|---|---|---|---|
| 1 | 启动版本 / patchedMethods | `[QSystem] === DSP Quality System v0.13.1 ===`、`Harmony PatchAll OK … patchedMethods=17` | 默认 cfg |
| 2 | 默认关闭 = 原版（无 UI） | `[QSystem][splitters] NOT ARMED (splitterQualityPriority=false; …)`；打开分流器面板**看不到**任何新增控件、无 checkbox 日志 | 硬约束「关闭即原版等价」 |
| 3 | 开 C-2 后 checkbox 创建 | `splitterQualityPriority=true` 重启 → `[QSystem][splitters] patch registered: …` + 首次打开分流器面板后 `[QSystem][splitters] in-window checkbox built: cloned UIToggle '<来源名>' under '<宿主>'`（找不到克隆源时为 `uGUI fallback`） | 记录日志里的来源/宿主名 |
| 4 | 点击生效 | 标题行右侧出现「品质优先 / Quality First」勾选框（未选中灰）→ 点一下变亮、标题出现 ` [Quality First]`、日志 `checkbox splitter p=… e=… quality-first=ON clicks=1`；再点取消 | 需鼠标命中，GUI 自动化不可靠 |
| 5 | 状态跟随 + 互不影响 | 换另一台分流器 → 勾选框状态跟随该台；同一台重开面板仍保持；两台开关互不影响 | |
| 6 | 存档往返 | 勾选 ON 后保存 → 尾块 `"splitters":[{"e":<entityId>}]`；重进档后打开该分流器仍为勾选态 | 离线已覆盖 JSON 往返 |
| 7 | 卸载 MOD 后存档可读 | 移走 DLL → 原版启动读同一份档可进基地，`Player.log` 无 `Incomplete game save`、无 `[QSystem]` 行 | 存档结构零改动（本版未动 Save） |
| 8 | 原版优先级让路 | 给分流器设 `inPriority` → 勾选框仍可点，但分配保持原版、无 `priority order` 日志 | |
| 9 | F3 兼容 | 窗口内按 F3 与点勾选框效果一致、视觉同步 | |
| 10 | 位置 / UI 缩放 | 不同分辨率/UI 缩放下勾选框不遮挡标题文字；若重叠记录窗口尺寸与分辨率 | 回退宿主坐标 `window.windowTrans` 左上 `(18,-64)`，见 DEVLOG §27.18.2 |

- **离线已覆盖（不占实机）**：勾选翻转/存档 flag 编解码/可见性矩阵/标签文案、标题后缀增删与幂等刷新、off = 恒等输入顺序（on = 与旧计划一致）、尾块 `splitters` 节点 JSON 往返与旧档（无该节点 = 全关）兼容 —— 见 `ALL PASS (54 checks)` 最后 4 条。
- **只有实机能验**：上表 2–10（checkbox 是否出现/位置/点击命中、状态互不影响与存档往返、F3 与勾选视觉同步、不同分辨率下的遮挡）。
- **风险 / 偏离说明**：checkbox 是**运行时克隆原版 `UIToggle`**（不修改任何预制体/资源），若某套 UI 皮肤下标题行空间不足可能遮挡——回退宿主已备好（`window.windowTrans` 左上角）；若克隆源在 `Resources.FindObjectsOfTypeAll<UIToggle>()` 也找不到，会自动退到 uGUI `Image+Toggle+Text` 生成（功能相同、样式朴素），日志会打印 `uGUI fallback`。
- **待办（下一版）**：上表 10 项、v0.13.0 遗留 16 项、老回归 #1/#2/#4 与 #22b、A 类五件套逐功能实机数值对比、5 开关 `NOT ARMED` 门控实机验证。

## 老回归复核 #1/#2/#4 + #22b（2026-10-28 21:00–21:40，Lead 执行，全部 PASS）

**环境**：私有 Win32 桌面 `DSPQSDebug`（隐藏、不占用用户桌面 1）+ 直接启动 `DSPGAME.exe`（`steam_appid.txt`=1366540）+ 测试专用 BepInEx 插件 `DSPQSDebugHarness`（文件命令队列，v3/v4）+ 测试槽 `QSTest.dsv`（`沙盒.dsv` 的副本），所有改动只在内存，未保存到用户存档。游戏 deck 值：`planet=103 十字架三-3号星`、`entities=211`。

| # | 场景 | 结果 | 实测证据 |
|---|---|---|---|
| 1 | 4 点铁矿 → 熔炉 → 磁铁 = 4 点 | **PASS** | 熔炉 `eid=54`（proto 2319 负熵熔炉，`recipe=2 磁铁`）：`building addinput 54 1001 16 4` → `pending 54` → `inputLevel=4 pendingLevel=4 wasReplicating=1`；`probe f7` 日志连续 14 条 **`[Probe][F7] eid=54 item=1102 inc=4`**（1102=磁铁） |
| 2 | 4 点磁铁 + 0 点铜 → 线圈（多原料取 min）= 0 点 | **PASS** | 制造台 `eid=87`（`recipe=6 磁线圈`，`in=[1102:2,1104:1]`）：`addinput 87 1102 8 4` + `addinput 87 1104 4 0` → `pending 87` → `inputLevel=0 pendingLevel=0`；`pool 87` → `poolInc=0 poolCount=8 avgInc=0.000`。对照两路都 inc=4 → `inputLevel=4`，`pool` 增量 24 点/6 件 = **4.0** |
| 4 | 0 点熔炉产物 = 0 点（不凭空升品） | **PASS** | `building clear 54` → `addinput 54 1001 16 0` → `pending 54` → `inputLevel=0 pendingLevel=0`；`probe f7` 连续 16 条 **`[Probe][F7] eid=54 item=1102 inc=0`** |
| 22b | 储物仓内 0 点与 4 点同类建筑不并格 | **PASS** | 储物仓 `eid=38`（proto 2102 大型储物仓，`size=60`）：`store 38 2319 1 0` → `grid=3 item=2319 count=1 inc=0`；`store 38 2319 1 4` → `grid=4 item=2319 count=1 inc=4`（未与 0 点合并）；`storagesort 38` 后仍两格，日志 `[QSystem][isolation] sort regrouped items=5 grids=5 building-groups=2`；`store 38 2319 3 4` → `grid=4 count=4 inc=16`（同品质合并）；`store 38 2319 2 0` → `grid=3 count=3 inc=0`；日志 `[QSystem][isolation] split item=2319 inc=4 grids=2 container=Default`。GUI：`open storage 38 ok inited=true storageId=1 active=true visible=true`（窗口可打开），截图受下述会话渲染限制 |

**同时确认的既有非缺陷 / 非回归项**
- `isolateStorageContainers` 关闭时回原版合并：本版门控路径与 v0.11.3 相同（启动日志 `storage containers=on/off` 分支），未单独重跑。
- **原版崩溃（与 MOD 无关，已证明）**：同一测试环境里把 `BepInEx\plugins\DSPQualitySystem\DSPQualitySystem.dll` 移到 `_live-backup\plugins-parked-0.13.0b\` 后只用 harness `load` 载档、不发送任何修改命令，仍在 frame≈170989 崩：`Player.log` 依次报 `[FactoryCargoPath] GameLogic.ContextCollect_Rocket_MultiMain IndexOutOfRangeException`（无 MOD）/ 有 MOD 时先报 `[SpaceSector] EnemyDFHiveSystem.GameTickLogic NullReferenceException` 再级联。根因是 harness 裸调 `GameSave.LoadCurrentGame`，漏掉 `GameMain.Start()` 里的 `data.galaxy.StartAutoScanning()` / `CreateUniverseSimulator()` / `CreateIconSet()` / `UIRoot.instance.OnGameMainObjectCreated()` / `data.preferences.Restore()`（见 asm_full.cs:349390-349460），不是 MOD 缺陷。
- 隐藏桌面截图限制：由于上述流程缺失，游戏主相机仍停在主菜单场景，`PrintWindow` 截图看到的是「主菜单 + 背景世界」；v5 harness 会补齐载档流程后再补 GUI 截图。

**结论**：老回归 #1、#2、#4 与 #22b（含容器隔离、整理不并格、同品质合并）在 v0.13.0 代码上全部 PASS，判定依据是游戏运行时组件字段与 MOD 自身日志（比截图更硬）。

## v0.13.2 待实机验证（Lead 执行，本版交接时点 2026-10-28 21:52）

**交付物**：`bin\Release\DSPQualitySystem.dll` = **228,864 B**，SHA256 **`D4D38AE7DA510AD81B5FDB1A57522DD8BAD7C0B795F99238985C97EDDF28987D`**，AssemblyVersion **0.13.2.0**；`BUILD OK`（0 警告 0 错误）；离线（删 `tests\OfflineTests\bin|obj` 后）**`ALL PASS (56 checks)`**。
**patchedMethods 预期**：与 v0.13.1 相同——启动 **17**、运行时 **39**（默认关）/ **42**（`splitterQualityPriority=true`）；本版只改勾选框的构建/布局/同步代码，**不新增被 patch 的方法**。

| # | 验证项 | 期望 / 证据 | 备注 |
|---|---|---|---|
| 1 | 启动版本 / patchedMethods | `[QSystem] === DSP Quality System v0.13.2 ===`、`Harmony PatchAll OK … patchedMethods=17` | 默认 cfg |
| 2 | 勾选框构建日志（新布局字段） | `[QSystem][splitters] in-window checkbox built: … under 'panel-bg' via=title-parent anchorMin=anchorMax=(0,1) pivot=(0,1) anchoredPosition=(12,-62) size=(214,28) font=15 labelLeft=30` | 本机皮肤仍应走 `uGUI fallback` |
| 3 | uitree 坐标 | `QSystemSplitterQualityRow pos=12.000,-62.000 size=214.000,28.000`，不压 `title-text`、`btn-box`、`splitter-circle` | 用 `uitree splitterWindow` 复核 |
| 4 | 视觉可读性 | 行内 18×18 色块在左端（关=深灰、开=绿），文字用标题同色、深色半透明垫底、字号 15，截图可读 | 截图 `_live\c2-box-off.png` / `c2-box-on.png` 复查 |
| 5 | 点击 ON | `priority=true`、标题 `四向分流器 [Quality First]`、日志 `checkbox splitter p=… e=… quality-first=ON clicks=N` | 需鼠标命中 |
| 6 | 点击 OFF（本版补） | 再点一下 → `priority=false`、标题回 `四向分流器`、`clicks` 继续递增 | v0.13.1 未实测的路径 |
| 7 | 两台分流器互不影响 | A 开 B 关；重开各自面板状态跟随；切 A 不影响 B | 离线已覆盖尾块/标题模型 |
| 8 | 存档往返 | ON 后保存 → 尾块 `splitters=1`；重进档 → 对应分流器仍勾选 | 离线已覆盖 JSON 往返 |
| 9 | 关闭总开关 = 原版 | `splitterQualityPriority=false` 重启 → `NOT ARMED`、无 built 日志、面板无任何新增控件 | 硬约束 |
| 10 | 其它皮肤回退 | `titleText`/父级缺失时日志出现 `fallback to windowTrans top-left`，控件落在 `(18,-64)` | 本机若未触发可跳过 |

- **离线已覆盖（不占实机）**：OFF→ON→OFF 标题后缀增删、flag 1/0、`[x]`/`[ ]` 标签、5 次循环稳定；87/88 两台分流器 flag 独立、尾块只记录开着的、重开面板标题跟随各自 flag —— 见 `ALL PASS (56 checks)` 最后 2 条。
- **只有实机能验**：上表 2–10（控件出现位置/坐标、点击命中与 OFF 路径、两台独立性、存档往返、其它皮肤回退）。
- **风险 / 偏离说明**：布局全部用相对 `panel-bg` 左上角的锚点（0/1 比例）与固定像素偏移，不随分辨率/UI 缩放漂移；极端窄面板（参考宽度 < 260）时右端可能靠近关闭按钮，正常缩放下不触发。若仍重叠：看一眼 built 日志里的 `via/anchor/pos` 与 `uitree` 输出即可定位，可临时用 `F3` 切换。
- **待办（下一版）**：上表 10 项、v0.13.1 遗留 10 项、v0.13.0 遗留 16 项、老回归 #1/#2/#4 与 #22b（已在 v0.13.0 代码上 PASS）、A 类五件套逐功能实机数值对比、5 开关 `NOT ARMED` 门控实机验证。


## v0.13.1 / v0.13.2 实机验证（Lead 执行，2026-10-28 22:00–23:05，全部必测项 PASS）

**环境**：隐藏桌面实例（`_live\hidden-host.ps1`，完全不影响桌面1）+ 测试插件 `DSPQSDebugHarness` v5；测试档 `QSTest`（= 用户 `沙盒.dsv` 的拷贝），planet 103。所有断言证据 = 游戏运行时组件字段读数 + MOD 自身日志；截图只作视觉复核。

| # | 验证项 | 实测证据 | 结论 |
|---|---|---|---|
| 1 | 启动版本 / patchedMethods | `[QSystem] === DSP Quality System v0.13.2 ===`、`Harmony PatchAll OK, harmony=2.5.5.0 patchedMethods=17` | PASS |
| 2 | 勾选框构建（新布局字段） | `[QSystem][splitters] in-window checkbox built: uGUI fallback under 'panel-bg' via=title-parent anchorMin=anchorMax=(0,1) pivot=(0,1) anchoredPosition=(12,-62) size=(214,28) font=15 labelLeft=30` | PASS |
| 3 | uitree 坐标不重叠 | `QSystemSplitterQualityRow active=true pos=12.000,-62.000 size=214.000,28.000`；同层 `title-text(40,-18,145x36)`、`btn-box(-50,-30,40x30)`、`splitter-circle(0,0,186x186)` 均不与其相交 | PASS |
| 4 | 视觉可读性 | ON：`_live\c2-v132.png`（深色半透明垫底 + 绿色 18×18 色块 + `[x] 品质优先 / Quality First`，标题 `四向分流器 [Quality First]`）；OFF：`_live\c2-v132-off.png`（灰块、文字压暗、标题无后缀） | PASS |
| 5 | 点击 ON | `toggle 87 on` → `applied=true found=true path=mod:_toggle isOn=true before=false label='[x] 品质优先 / Quality First' clicks=1 clicksDelta=1 priority=true`；日志 `[QSystem][splitters] checkbox splitter p=103 e=87 quality-first=ON clicks=1` | PASS |
| 6 | 点击 OFF（v0.13.1 未测路径） | `toggle 87 off` → `isOn=false before=true clicks=2 clicksDelta=1 priority=false`，标题回 `四向分流器`；再开 `clicks=3 priority=true` | PASS |
| 7 | 两台分流器互不影响 | 87 关 / 219 开（v0.13.1 会话）各自 `priority` 独立，重开面板各自跟随 | PASS |
| 8 | 存档往返 | 开启后 `save QSTest` → 尾块 `splitters=1 bytes=276`；`loadfull QSTest` → `embedded quality trailer used … splitters=1`；`toggle state 87` → `isOn=true priority=true` | PASS |
| 9 | 关闭总开关 = 原版 | cfg `splitterQualityPriority=false` 重启 → `[QSystem][splitters] NOT ARMED (splitterQualityPriority=false; vanilla distribution)`；`uitree splitterWindow` 的 `panel-bg child=6`（无 `QSystemSplitterQualityRow`），无 built 日志 | PASS |
| 10 | 其它皮肤回退 | 本机（默认皮肤、2560×1600/200%）未找到可克隆的原版 `UIToggle`，走 `uGUI fallback` 且创建成功；`fallback to windowTrans top-left` 分支本机未触发（代码保留） | 部分覆盖 |

**说明**：harness 的 `loadfull`（v5）补齐 `GameMain.Start()` 载档流程后，隐藏桌面实例可长时间运行、主相机正常切进游戏场景；此前「约 2.5 分钟必崩」的原版级联异常不再出现（`[FactoryCargoPath]` / `[SpaceSector]` 相关），截图也能拿到真实游戏画面。原崩溃与 MOD 无关的判定过程见上一节。

## A 类五件套逐功能实机数值对比（v0.13.2，Lead 执行，2026-10-28 22:30–23:10）

**方法**：隐藏桌面 + harness v5 提供 `place`（运行时放置建筑）、`setpoints`（改建筑品质点数）、`forcepower`（测试用保持供电）、`fuel` / `bullet` / `matrix` / `queue`（喂料），逐功能对比「0 点 vs 10 点」。曲线定义 v0.11.2：10 点 = 2.00×。

| 功能 | 建筑 / 探针 | 0 点实测 | 10 点实测 | 结论 |
|---|---|---|---|---|
| **A1 研究站研究速度** | `place 2901` → eid=53；`labmode 53 research` + `queue 3901`（研究速度）+ 每槽 `matrix … 50 0` | `hashUploaded` 1184→3055 / 31.2 s = **60.06 hash/s** | 5696→9436 / 31.2 s = **120.06 hash/s**；日志 `[QSystem][lab] effective=2000` | **PASS，2.00×** |
| **A2 防御塔射速** | `place 3005` → eid=214；`[QSystem][turret]` 探针 | 12 s 内 `fire rate bonus active …` 日志 **0 条**（factor<=1 直接 return） | 12 s 内 **20 条**（`DebugLogLimit=20` 上限）；随后 `[QSystem][turret] effective=200/400/600/800 (methods=6)` | **PASS，Prefix 在真实 `Shoot_*` 调用里生效** |
| **A3 发电功率** | `place 2204` → eid=215；`fuel 215 1120 10`（氢）→ `powergen 215` | `capacity=36000` | `capacity=72000`（净网 `serve=1.000`） | **PASS，2.00×** |
| **A4 弹射器发射速度** | `place 2311` → eid=214；`forcepower` + `bullet 214 1501 N 0` + `launch 214`（orbitId=1） | `bulletCount` 消费 39→15 / 121 s = **11.88 发/分** | 60→40 / 61 s = **19.62 发/分**（≈1.65×）；日志 `[QSystem][launcher] ejector bonus active (planet=103 entity=214 factor=2.00 viaEnv=false)` | **PASS（factor=2.00 生效；发数比 1.65× 因原版每 30 tick 才允许发射一次的节流 `tick%30==id%30` 而次线性）** |
| **A5 喷涂机概率 +1** | 基础档喷涂机 eid=40（`cargoBeltId=37`、`incAbility=4`、`incItemId=1143` 余量 790） | 有消耗证明喷涂发生（`incCount` 792→790） | `transpiler matched the byte-write site at instruction #361`、`armed (patch points=1)`、`[QSystem][sprayer] armed (site=1, chance from quality curve 0..100%, sprays=0, +1 hits=0)`；本档传送带无真实货流（`beltpush 46 1001 1 0` 可插入但物品未经过喷涂位），未复现 `+1 quality point` 日志 | **受限**：站点匹配 + 离线 5 条检查（合流/满级/0 概率/不降级/无状态）已覆盖；数值效果需真实带流 |
| **发射井** | `place 2312` → eid=217 | — | `launch` 只置 `boost=true`；`hasNode` 由 `GetAutoNodeCount()>0` 每帧派生，`QSTest` 无球面节点 ⇒ 火箭不发射 | **仅补丁注册 + `boost` 已置位** |

**结论**：A 类五件套在实机上都由「建筑品质点数 × 分段曲线」驱动；可量化的 A1/A2/A3 都精确等于 2.00×，A4 方向与 factor=2.00 一致（受原版节流影响发数比 1.65×），A5 因测试档没有真实生产带流只证到反射站点与开关，发射井受球面节点缺失限制。全部证据均为运行时字段/日志读数。

## 交接状态（2026-10-28，v0.13.2 时点）

- **本版（v0.13.2）**：分流器品质优先勾选框的布局从「面板右侧垂直居中」改为「标题下方左上角 (12,-62)」，并加深色半透明垫底、18×18 色块、标题同色文字（字号 15）；构建日志补齐 `via/anchor/pos/size/font`；离线测试 54 → 56 条（补 OFF 路径与多分流器状态互不影响）。
- **交付物**：`bin\Release\DSPQualitySystem.dll` = **228,864 B**，SHA256 **`D4D38AE7DA510AD81B5FDB1A57522DD8BAD7C0B795F99238985C97EDDF28987D`**，AssemblyVersion **0.13.2.0**；离线 **`ALL PASS (56 checks)`**；dist 包 `dist\DSPQualitySystem-0.13.2.zip` = 99,531 B，SHA256 `FA6559AAD68923CDC412D064A1E8DFED9E2CF66C283C8CA0430D725EF9FEAA14`（内含 DLL `D4D38AE7…`）。
- **四项收尾状态**：① 老回归 #1/#2/#4 **PASS**；② #22b 容器内 0 点 vs 4 点不并格 **PASS**（代码路径 + GUI 面板可打开）；③ C-2 真勾选框 UI **PASS**（上表 10 项）；④ A 类五件套数值 **A1/A2/A3/A4 PASS，A5 受限**。
- **未覆盖 / 已知限制**：A5 真实货流、发射井球面节点、`_autosave_*` 自动存档轮转、24 MB 大档耗时、其它皮肤下勾选框回退位置（`fallback to windowTrans top-left` 分支本机未触发）。
- **测试痕迹**：所有破坏性操作只用于 `QSTest` 测试档；用户 `沙盒.dsv` / `_lastexit_.dsv` 保持干净（备份 `_live-backup\saves-before-4items-20261028-205832`，22 文件 188,800,246 B）。


## v0.13.2 补充实机验证（2026-10-28 22:30–22:45，Lead 执行）：C-1 溢出格闭环 / 自动存档轮转 / 大档尾块开销

**环境**：隐藏桌面实例 + harness v7；测试档 `QSTest`（`沙盒.dsv` 拷贝）。此节清掉上一节列的「未覆盖」中三项。

| # | 项目 | 场景与实测证据 | 结论 |
|---|---|---|---|
| 1 | **C-1 溢出格写入** | 容器 eid=38（大型储物仓，base=60）点数设 10 → `[QSystem][storage] expanded p=103 e=38 points=10 base=60 extra=5 total=65`；用 7 种建筑 × inc 1–10 的 `store` 填满 65 格（`storage dump 38 → size=65 filled=65`，其中 grid 60–64 为溢出格）；`save QSTest` → 日志 `[QSystem][storage] save shave: containers=1 overflowCells=5 (kept in the MOD trailer)` + `embedded quality trailer written ('沙盒'): planets=1 buildings=1 pools=5 storages=1 splitters=0 bytes=524`；尾块 `"storages":[{"e":38,"b":60,"cells":[{"g":60,"i":2212,"f":0,"c":1,"s":30,"q":8},…{"g":64,"i":2210,"f":0,"c":1,"s":20,"q":2}]}]`；文件 length=fileSize=7,050,787 | PASS |
| 2 | **卸载 MOD 后原版可读（含扩容仓库）** | 保存后把 `BepInEx\plugins\DSPQualitySystem\DSPQualitySystem.dll` 移出 → 用原版（无 QSystem 插件，仅 harness）`loadfull QSTest` → `loadstatus ready=yes … errored=false`；`storage dump 38 → size=60 filled=60`（回到原版格数）；`Player.log` 里 `QSystem` 行 **0 条**、无 `Incomplete game save`、无异常 | PASS |
| 3 | **MOD 装回后的恢复路径** | DLL 装回（228,864 B / `D4D38AE7…`）→ `loadfull QSTest` → `[QSystem][storage] restored p=103 e=38 base=60 cells=5`、`embedded quality trailer used … storages=1 bytes=524`；`storage dump 38 → size=65 filled=65`，grid 60–64 分别为 `2212 inc=8/9/10`、`2210 inc=1/2`，与尾块逐字段一致 | PASS |
| 4 | **自动存档轮转** | 会话内等待默认间隔（`asm_full.cs:198201 autoSaveTime = 480` 秒，UI 侧 120–1500 钳制）；22:42:18 观察到 `_autosave_0.dsv` 被轮转写入（7,059,806 B），`_autosave_tmp.dsv` 已不存在；该档 length=fileSize、尾块 `payloadLen=525` 且含 `"storages"` 溢出格；日志 `save shave: containers=1 overflowCells=5` + `embedded quality trailer written ('沙盒') … bytes=525` | PASS |
| 5 | **从自动存档载入** | `loadfull _autosave_0` → `ready=yes errored=false`；`restored p=103 e=38 base=60 cells=5`、`storage dump 38 → size=65 filled=65` ⇒ 轮转后的自动存档同样可恢复 | PASS |
| 6 | **24 MB 大档尾块读取开销** | 离线基准 `_live\bench`（net10，直接编译链接 `Save\QualityBlobCodec.cs`，50/1000 次取均值）：7 MB 存档 **1.715 ms/次**；24 MB 存档 **1.427 ms/次**（1000 次 1.532 ms/次）；最坏情况「24 MB 且完全没有尾块」（需扫完 4 MB 窗口）**5.412 ms/次** | PASS（对读档时间可忽略） |

**结论**：C-1 的「扩容 → 溢出格进尾块 → 卸载原版可读 → 装回恢复 → 自动存档轮转跟随 → 从自动存档恢复」整条链闭环；24 MB 级存档的尾块读取成本 ≤5.5 ms。上一节列的未覆盖项只剩：A5 喷涂机真实带流、发射井球面节点、其它皮肤下勾选框回退位置。

**收尾**：游戏已退出；用户存档 `_lastexit_.dsv` / `_autosave_0.dsv` 已从 `_live-backup\saves-before-4items-20261028-205832` 复原，测试槽 `QSTest` 与轮转产生的 `_autosave_tmp` 已删除；测试插件 `DSPQSDebugHarness` 已从 `plugins` 移出（源码与 DLL 留在 `_tooling\DSPQSDebugHarness\`）。

## v0.14.0 待实机验证（Lead 执行，本版交接时点 2026-10-28 23:15）

**交付物**：`bin\Release\DSPQualitySystem.dll` = **242,176 B**，SHA256 **`F146B33CDDF83ED52DA628EF0E611369272364C0D1048DDD00C595EC388C3F1D`**，AssemblyVersion **0.14.0.0**；`BUILD OK`（0 警告 0 错误）；离线（删 `tests\OfflineTests\bin|obj` 后）**`ALL PASS (59 checks)`**；本版未部署、未启动游戏、未改动 `Save/` 与 `dist/`。
**patchedMethods 预期**：启动 **17 不变**（本版补丁全部走手工 `harmony.Patch`，不进 PatchAll）；运行时默认（`splitterQualityPriority=false`、`itemQualityBadges=true`）= **24 基线 + 15 A 类 + 3 角标 = 42**；把 `itemQualityBadges=false` 则为 **39**；再开 `splitterQualityPriority=true` 则为 **43**。

| # | 验证项 | 期望 / 证据 | 备注 |
|---|---|---|---|
| 1 | 启动版本 / patchedMethods | `[QSystem] === DSP Quality System v0.14.0 ===`、`Harmony PatchAll OK … patchedMethods=17`；`[QSystem][badges] armed (grid size+content postfix, hand-tip postfix; hosts=0 …; style=number minPoints=1)` | 默认 cfg |
| 2 | 背包角标 | 打开玩家背包（`inventoryWindow`）：带品质的建筑物品图标右上角出现数字角标；0 点物品无角标；`uitree` 可见每格 `QSystemQualityBadge` | 需真实 UI 截图 + uitree |
| 3 | 储物仓角标 | 打开任意储物仓（`storageWindow`）：同上；同种物品 0 点/4 点/10 点三格角标分别为空 / `4` / `10` | 与 P4 隔离的分格语义一致 |
| 4 | 角标定位 | `uitree`：角标 `pos ≈ (col*50+1, -(row*50)-1)`、`size=(30,16)`，落在对应物品图标左上角、不压原版堆叠数字 | 200% UI 缩放下复核 |
| 5 | 四档颜色 | 1–2 点灰蓝、3–4 点青、5–7 点紫、8–10 点金，深色 `Outline` 描边在亮/暗图标上都可读 | 截图 `_live\q-badge-*.png` |
| 6 | 样式三态 | `itemQualityBadgeStyle` = `number` / `dot` / `both` 重启后分别只数字 / 只圆点 / 圆点+数字；乱码值回退 `number` | 需重启游戏 |
| 7 | 阈值 | `itemQualityBadgeMinPoints=10` 重启后只有满品物品有角标；默认 1 时任何带品质点物品都显示 | 需重启游戏 |
| 8 | 手持物品提示 | 手拿带品质物品时鼠标提示图标角上有角标（`inhandItemInc/inhandItemCount`）；进入建造模式（图标被原版隐藏）后角标同步消失 | |
| 9 | 生命周期 / 无泄漏 | 同一储物仓面板反复开关/扩容/整理 20+ 次：`hosts` 不持续增长（每面板仅 1 个 host，随面板重建才重建）、无内存持续上涨；`grid badges effective=…` 每 200 次一条 | 硬约束① |
| 10 | 关闭 = 原版 | `itemQualityBadges=false` 重启 → `[QSystem][badges] NOT ARMED: itemQualityBadges=false (or enabled=false); vanilla item icons`，`Describe()` 为 `badges NOT ARMED (itemQualityBadges=false (or enabled=false))`；`uitree` 无 `QSystemQualityBadge`、物品图标与原版完全一致 | 硬约束② |
| 11 | 存档不受影响 | 保存/重进档角标照常显示；尾块内容与 v0.13.2 相同（角标纯显示、不进 `.dsv`） | |
| 12 | 产物预览边界 | 装配机/熔炉顶部产物预览图标**无**角标（设计如此）；产物品质看同窗口产物格/`servingBox` 网格 | 已写进 README 已知限制 |

- **离线已覆盖（不占实机）**：每件点数推导（`inc/count`、整除丢余、超 10 钳 10、0/负 inc/count、随 inc 单调升/随 count 单调降）、可见性（功能开关、阈值钳制 1–10、阈值上下、越界值）、样式归一化与三种标签文案、四档 tier 边界与颜色（含每档唯一色、1 档 `B8C4D0`、4 档 `FFD54F`）—— 见 `ALL PASS (59 checks)` 最后 3 条。
- **只有实机能验**：上表 2–9 与 11–12（角标是否出现/位置/颜色可读性、样式与阈值重启生效、手持提示与建造模式联动、反复开关无泄漏、产物预览确实无角标）。
- **风险 / 偏离说明**：① 角标用 `Text` + `Outline`（克隆 `grid.prefabNumText`，失败则运行时新建），字号 12px（手持 11px），在极低 UI 缩放/超小分辨率下可能与原版堆叠数字轻微重叠 —— 可用 `itemQualityBadgeStyle=dot` 规避；② 关掉开关后 **Harmony patch 不注销**（cfg 说明要求重启），运行中重新打开开关不会立刻补建角标，需重启或等下一次面板重建；③ 建筑产物预览（`productIcon0/1`）按设计不做角标，已在 README 写明。
- **待办（下一版）**：上表 12 项、v0.13.2 遗留 10 项、v0.13.1 遗留项、A5 喷涂机真实带流与发射井球面节点、其它皮肤下勾选框回退位置、`_autosave_*` 与 24 MB 大档（v0.13.2 补充节已 PASS，无需重跑）。

## A5 喷涂机真实带流 + 发射井（Lead 执行，2026-10-28，PASS）
环境：离屏窗口 `DSPGame.exe -popupwindow` + `DSPQSDebugHarness` v8（`141,824 B` / `CAEE4B92…`）+ `QSTest` 测试档（`沙盒.dsv` 副本）；planet 103。

| # | 验证项 | 期望 / 证据 | 结论 |
|---|---|---|---|
| A5-1 | 喷涂机站点 | `[QSystem][sprayer] spinner … transpiler matched the byte-write site at instruction #361`、`armed (patch points=1)` | PASS |
| A5-2 | 淤塞让货物在 pivot 停留 | `beltscan` 找喷涂带 `beltId=37 / eid=46 / pathId=7`；`beltpush 46 1001 20 0` 后货物在 path 末端堆积；`qs-fill.ps1` 逐件推 120 件造成拥堵 | PASS |
| A5-3 | **10 点 = 原版 +1** | `setpoints 40 10` → 淤塞后 `modstate sprayer`：`sprays=2, +1 hits=1`；`beltdump 46` 命中 `point=85 item=1001 stack=1 inc=5 point=5.000`（原版 1×incAbility 4 = 4，MOD +1） | PASS |
| A5-4 | 0 点 = 原版 | 重载档 → `setpoints 40 0` → 淤塞后 `beltdump 46`：`point=85 stack=1 inc=4`（=1×4），`+1 hits` 不增长 | PASS |
| A5-5 | 喷洒消耗 | `sprayer 40`：`incCount 792 → 791`、`incAbility=4`、`sprayTime=10000` | PASS |
| 井-1 | 发射井 Factor | `place 2312` → eid=87；`setpoints 87 10` + `forcepower 87`（`serveAfter=1.000`）；日志 `[QSystem][launcher] silo bonus active (planet=103 entity=87 factor=2.00 viaEnv=false)` 多次；`modstate launcher` → `hits ejector=0 silo=357` | PASS |
| 井-2 | 无球面节点 | `silo 87` → `hasNode=false`（`GetAutoNodeCount()>0` 派生，QSTest 无球面节点）⇒ 只能验 factor 应用，发射时序不可测 | 已知限制 |

## v0.14.0 物品品质角标实机验证（Lead 执行，2026-10-28，PASS）
环境：同上；部署 v0.14.0（最终 `F5820493…`）。

| # | 验证项 | 期望 / 证据 | 结论 |
|---|---|---|---|
| 1 | 启动 | `[QSystem] === DSP Quality System v0.14.0 ===`、`patchedMethods=17`、`[badges] armed (… style=number minPoints=1)`；`[Audit] UIStorageGrid.OnStorageSizeChanged() -> OK`、`OnStorageContentChanged() -> OK`、`UIHandTip._OnUpdate() -> OK` | PASS |
| 2 | 角标数值 | 储物仓 eid=38（60 格）存 0 点 / 10 点（并入 3 件 30 点）/ 7 点 / 2 点各一格；`storage dump 38` → grid3 inc=0、grid4 count=3 inc=30（每件 10）、grid5 count=1 inc=7、grid6 count=1 inc=2；截图 `_live\badge1.png` + 放大 `badge1-zoom2.png`：grid4 金 `10`、grid5 紫 `7`、grid3 空 | PASS |
| 3 | 定位/数量 | `uitree storageWindow`：每格一个 `QSystemQualityBadge`，`pos=(col*50+1,-(row*50)-1)`（col4 `201,-1`、col5 `251,-1`）、`size=(30,16)`；仅 2 格 `active=true`，其余 58 格隐藏 | PASS |
| 4 | 亮度调整 | 用户反馈 10 点角标过亮；改为四档色值压暗（`93A0AE`/`2F86AE`/`7A5BB0`/`C79A2A`）+ 文字 alpha 0.82 + 描边 alpha 0.5；截图 `_live\badge-dim.png` + 放大 `badge-dim-zoom.png`：哑光黄、无泛光晕圈 | PASS |
| 5 | 样式 both + 阈值 3 | cfg `itemQualityBadgeStyle=both` + `itemQualityBadgeMinPoints=3` 重启 → 截图 `_live\badge-both.png` + `badge-both-zoom.png`：`● 10`、`● 7`，2 点物品不显示 | PASS |
| 6 | 无泄漏 | `closeui`/`open storage 38` 交替 5 轮后 `uitree` 中 `QSystemQualityBadge` 仍 60 个、active=2 | PASS |
| 7 | 关闭 = 原版 | `itemQualityBadges=false` 重启 → `[badges] NOT ARMED: itemQualityBadges=false (or enabled=false); vanilla item icons`、`[Audit] badges NOT ARMED`；`uitree` 无任何 `QSystemQualityBadge` | PASS |
| 8 | 存档往返 | `store` 四种点数 → `save QSTest`（写尾块）→ 退出重进 `loadfull QSTest` → 打开同一仓，角标按存档点数显示（10/7/2） | PASS |
| 9 | 手持提示视觉 | 仅确认 `[QSystem][badges] hand tip badge effective=…` 路径执行与 `UIHandTip._OnUpdate` 反射自检 OK；离屏窗口无法移动鼠标悬停，视觉需真人 | 未覆盖 |

**本版交付物（最终）**：`bin\Release\DSPQualitySystem.dll` = **242,176 B**，SHA256 **`F58204933742F9FD1FC4E63288E22FE546D21FF7520F79E3F9B8C99C3139C71B`**，AssemblyVersion 0.14.0.0；`BUILD OK`；离线 **`ALL PASS (59 checks)`**；已部署到游戏插件目录。
**用户口径（2026-10-28）**：② 物品品质角标为本版内容；③「蓝图携带品质」判定为错误方向、取消，已从开发文档第 25 行删除（README/DEVLOG 同步记录）；A5 与发射井的实机缺口已在上面补齐。

## 交接状态（2026-10-28 23:40，v0.14.0 时点）
- 本版：物品品质角标（P6）+ 角标调暗（按用户反馈）+ A5 喷涂机真实带流与发射井 factor 实机闭环。
- 未覆盖：手持提示角标的**视觉**（离屏窗口不能悬停）、发射井真实发射时序（需球面节点）、其它皮肤下勾选框回退位置、A5 在真实玩家流水线上的长期表现。
- 版本链：v0.13.0 / v0.13.1 / v0.13.2 / **v0.14.0（本版，`F5820493…`，59 checks）**。
- 下一步：等用户指定；蓝图携带品质不再做。

## v0.15.0 待实机验证（Lead 执行，本版交接时点 2026-10-29 00:35）

**本版交付物**：`bin\Release\DSPQualitySystem.dll` = **213,504 B**，SHA256 **`0A9DCD9A8E8A80E5A087CBD2061608ADEA08D5FADD803517072353002453D2FB`**，AssemblyVersion 0.15.0.0；`BUILD OK`（0 警告 0 错误）；离线 **`ALL PASS (55 checks)`**（先删 `tests\OfflineTests\bin|obj`）。未部署、未启动游戏。

**本版内容**：玩家 cfg 从 40+ 键收敛为 **16 个键**（其余全部转为内置常量、默认值不变）；`debugProbes`/`debugInjectPoints` 改为环境变量 `DSPQS_DEV=1`；**删除屏幕描边功能**与**蓝图携带品质功能**（含对应源文件与离线测试）；`tenPointMarkerStyle` 收窄为 `auto`/`halo`/`none`；**cfg 说明全部中文化**，并且**老档升级会按键集合自愈**：只要 cfg 不是正好这 16 个键（旧版 40+ 键残留 / 缺失 / 多余），Mod 会用玩家当前值 + 新中文注释**整份重写**该文件（旧键清零、只留 16 键），日志 `[QSystem][config] rewrote cfg with cleaned Chinese layout (16 keys, dropped N legacy keys)`；已清理过的档下次启动不重写、不动 mtime。**首键注释为两段式**：一句总说明 + 中文全角「｜」+ 该键说明（不再用 `===` 拼接；其余 15 键也已复查无 ASCII 分隔线、无重复版本号字样）。

**patchedMethods 预期**：启动（PatchAll 属性补丁）仍 **17**（被删的两个类本来就没有属性补丁）。运行时独立手工补丁：v0.14.0 默认 42（含角标 3，分流器默认关）；本版分流器总开关转为内置常量 `true` ⇒ 预期默认 **45**；若把 `itemQualityBadges=false` 则 **42**。储物仓品质格数补丁挂在本就被 PatchAll 覆盖的 `GameData.Export` 上，不新增方法计数。以启动日志 `patchedMethods=` 实测为准。

| # | 核对项 | 通过标准 |
|---|--------|----------|
| 1 | 启动横幅/版本 | `[QSystem] === DSP Quality System v0.15.0 ===`；`patchedMethods=` ≈ 预期值（见上） |
| 2 | cfg 中文化 + 老档自愈 | 部署前旧 cfg 为英文 47 键（Lead m01575 的机器上曾变成 63 键）；启动后日志出现 `[QSystem][config] rewrote cfg with cleaned Chinese layout (16 keys, dropped N legacy keys)`（N=47）；打开 cfg 只剩 16 个键、注释全为中文、玩家改过的键值保留；首键注释为「总说明｜隔离-背包建筑分格：…」两段式，无 `===` 拼接 |
| 3 | cfg 中文化（全新安装） | 删掉 cfg 再启动：自动生成中文 cfg，**不出现** rewrote 日志；只有 16 个键 |
| 4 | 只剩 16 键 + 二次启动不重写 | cfg `[quality]` 段仅 16 键；旧键已从文件消失（不再只是"被忽略"）；再启动一次：无 rewrote 日志、cfg mtime 不变；`LogConfig` 打印 16 玩家键 + 一行 builtin |
| 5 | 描边已删除 | `[Audit]` 无 `P5_TenPointOutline` 行；`tenPointMarkerStyle=outline`/`both` 启动不报错且按 `auto` 画光环；日志无 `[QSystem][outline]` |
| 6 | 蓝图携带品质已删除 | 复制 10 点建筑出的蓝图与原版无差异（不再写 marker 参数）；粘贴落成点数 = 消耗物品点数（优先消耗逻辑不变）；`[Audit]` 无 `BlueprintUtils.GenerateBlueprintData` 行 |
| 7 | DSPQS_DEV 门控 | 不设环境变量：F5/F6/F7…F12 全部无效、无相关日志；设 `DSPQS_DEV=1` 后热键恢复、F10/F11 注入固定 4 点 |
| 8 | 标记样式三档 | `auto`/`halo` 画金色光环、`none` 不画；光环观感与 v0.14.0 一致（金色、浮高 1.2m、同屏 ≤64） |
| 9 | 保留开关仍可关 | 逐项验证 `speedBonus`/`chargeBonus`/A 类 5 键/隔离 2 键/`itemQualityBadges` 关闭后回到原版行为；`tenPointHaloSpin=0` 不自转 |
| 10 | 存档不受影响 | v0.14.0 存档直接用 v0.15.0 读取：品质点数、储物仓溢出格、分流器勾选状态全部保留；保存后尾块结构不变 |

**离线已覆盖（不占实机）**：品质点数/角标纯函数与颜色；储物仓格数；分流器排序/开关/存档节点；A 类五件套倍率；光环数学与 marker-style 解析；`PerItemPoints`/`ChooseBestMatchGrid`/`ShouldSwapHand`（优先消耗）；存档尾块 codec；**cfg 文本自愈**（`Checks.CfgText.cs`：16 键/旧键清掉/值保留/中文注释/头版本/GUID/CRLF/默认值回退）。
**只有实机能验**：上表 1–10（尤其 2/3/4 cfg 自愈与二次启动不重写、5/6 两个功能确实消失、7 环境变量门控）。
**风险 / 偏离**：① 删掉 `enabled` 总开关后没有"一键全关"，玩家想回原版只能逐项关 16 个开关（按 Lead 需求执行）；② cfg 重写是 Mod 自己 `File.WriteAllText`（不用 `cfg.Save()`，否则删不掉旧键）；若磁盘只读/被杀软锁住会 `WarnOnce` 并保留旧文件，下次启动会再试一次（不影响运行）；③ 旧键不会"被忽略"而是被**主动清掉**——如果玩家改过旧键里已转常量的值，这些修改会丢失（值已固定为原默认，属 v0.15.0 设计）；④ patchedMethods 预期值需以实机日志确认（见上）；⑤ 自愈触发判定只看「键名集合」，注释里残留旧键名不影响。

### Lead 实机结果（2026-10-29 00:20–00:36，离屏窗口 + 真实游戏，v0.15.0 最终版 213,504 B / `0A9DCD9A…`）

| # | 核对项 | 实测证据 | 结论 |
|---|--------|----------|------|
| 1 | 启动横幅/版本 | `[QSystem] === DSP Quality System v0.15.0 ===`、`Harmony PatchAll OK, harmony=2.5.5.0 patchedMethods=17`、`build: mtime=2026-10-29 00:32:28 assembly=0.15.0.0` | PASS |
| 2 | cfg 中文化 + 老档自愈 | 旧 v0.14 期 cfg 31 个旧键残留：`[QSystem][config] rewrote cfg with cleaned Chinese layout (16 keys, dropped 31 legacy keys)`；cfg 5293 B / 85 行 / 16 键、注释全中文 | PASS |
| 2b | 旧键回填再自愈 | 手动回填 `debugProbes = true`（17 键）启动 → `rewrote cfg with cleaned Chinese layout (16 keys, dropped 1 legacy keys)`；cfg 5052 B / 85 行 / 16 键 | PASS |
| 2c | 首键两段式中文 | `## 【DSP 品质系统 v0.15.0】…DSPQS_DEV=1。｜隔离-背包建筑分格：true = …`，无 `===`/`---` 拼接 | PASS |
| 4 | 二次启动不重写 | 干净 16 键档再启动：日志**无** `rewrote cfg`；cfg SHA256 前后相同 `7ACA1C296EFD7152FFB625B44936E299CE7714E214C8C607E5F01FC61B3D7823` | PASS |
| 5 | 描边已删除 | `LogOutput.log` 中 `outline` 0 处、`[Audit]` 无 outline 行 | PASS |
| - | 玩家键/builtin 打印 | `cfg isolateBuildingStacks=True …  sprayedIncBonus=True \| itemQualityBadges=True itemQualityBadgeStyle=number itemQualityBadgeMinPoints=1 tenPointMarkerStyle=auto tenPointHaloSize=0.30 tenPointHaloPulse=True tenPointHaloSpin=1.0` + `cfg builtin=(internal constants, no longer user-editable) … debugProbes=False (env DSPQS_DEV=1 enables debug hotkeys/probes)` | PASS |
| 3/6/7/8/9/10 | 全新安装生成、蓝图差异、DSPQS_DEV 门控、marker 三档、逐项关开关、v0.14 存档直读 | 本版收尾只跑了 1/2/2b/2c/4/5 与 cfg 打印；其余留待下一轮实机 | 未覆盖 |

**实机通道与坑**：`_live\vis-host.ps1`（离屏窗口）必须用后台 job 启动；`Start-Process -WindowStyle Hidden` 会让宿主立即退出并带走游戏。抓图 `_live\capture.ps1 -ProcessId <pid> -Out <png>`。直接起 `DSPGAME.exe` 需游戏目录有 `steam_appid.txt`（内容 `1366540`，已放置）。
**最终交付物**：`bin\Release\DSPQualitySystem.dll` = **213,504 B**，SHA256 **`0A9DCD9A8E8A80E5A087CBD2061608ADEA08D5FADD803517072353002453D2FB`**，AssemblyVersion 0.15.0.0；clean rebuild 哈希一致；离线 **`ALL PASS (55 checks)`**；`dist\DSPQualitySystem-0.15.0.zip` = **98,611 B**，SHA256 **`7675C41787CBE801ED151F6BF1EE655B3895157090ED5703F8C107BDA917B4DC`**。

## v0.15.0 R2/Thunderstore 发布包校验（Lead 执行，全部 PASS）

| # | 检查项 | 实测 | 结论 |
|---|--------|------|------|
| R2-1 | 包结构 | zip 根含 `manifest.json`/`icon.png`/`README.md`/`CHANGELOG.md`，插件 DLL 在 `BepInEx/plugins/DSPQualitySystem/DSPQualitySystem.dll`，无 `__MACOSX`/`.DS_Store` | PASS |
| R2-2 | manifest.name | `DSPQualitySystem`（16 字符，匹配 `[A-Za-z0-9_]`，≤64） | PASS |
| R2-3 | manifest.version_number | `0.15.0`（semver，6 字符 ≤16） | PASS |
| R2-4 | manifest.description | 129 字符（Thunderstore 上限 250），单行，UTF-8 中文正常 | PASS |
| R2-5 | manifest.website_url | 空（允许） | PASS |
| R2-6 | manifest.dependencies | `xiaoye97-BepInEx-5.4.17`、`CommonAPI-CommonAPI-1.6.7`，均符合 `Namespace-Name-Version` | PASS |
| R2-7 | icon.png | PNG 256x256、51,529 B（≤1 MB）；内容=戴森球 MK.III 增产剂 + Factorio 传奇品质角标 | PASS |
| R2-8 | DLL 一致性 | 213,504 B / `0A9DCD9A…`，与 v0.15.0 交付构建一致 | PASS |
| R2-9 | 可复现性 | `tools\pack-r2modman.py` 连跑两次 zip 哈希相同 `E7E87004…`（138,395 B） | PASS |
| R2-10 | r2modman 导入形态 | 根级 manifest + 图标 + 插件目录齐全，符合 r2modman「从文件安装」/Thunderstore 上传要求 | PASS |

**产物**：`dist\DSPQualitySystem-0.15.0-r2modman.zip`（138,395 B，SHA256 `E7E87004AD9C223A823C117EB53372D3A48419C8843BDDC1CF9C86DA7C68127F`），与手工安装用的 `dist\DSPQualitySystem-0.15.0.zip`（98,611 B）并存。

## v0.16.0 待实机验证（Lead 执行，本版交接时点 2026-10-30 00:15）

**交付物**：`bin\Release\DSPQualitySystem.dll` = **225,280 B**，SHA256 **`8CB2CB75C39E992D01A1A4F623E090D8F5D39BB63AEF800598DD08128358A99B`**，AssemblyVersion 0.16.0.0；`build.ps1` → `BUILD OK` 0 警告 0 错误；离线（先删 `tests\OfflineTests\bin|obj`）→ **`ALL PASS (59 checks)` / `TEST PASS`**；未部署、未启动游戏，`Save\`/`dist\`/`tools\`/`_handoff-20261026\` 未改动。

**本版内容**：修 3 个实机 bug——（1）大型采矿机（采矿区收集站型，物品 2316）产物由原版 `StationComponent.UpdateVeinCollection` 搬入站点储物格且从不写 `inc`，新增采集站转移补丁按建筑点数累加点数；（2）Shift+左键立即建造丢弃 `TakeTailItems` 的 `out inc`，新增捕获→`SetPrebuildInc` 绑定；（3）增产额外产物被"净增量记账 + 同窗口取货"抵消而长期 0 点，改为净增量 + 取货次数补偿。无新增玩家 cfg 键（仍 16 键）；存档 schema 不变。

**patchedMethods 预期**：启动 **17 → 18**（本版新增属性补丁 `StationComponent.UpdateVeinCollection`，计入 PatchAll）；运行时默认 **45 → 47**（+`StationComponent.UpdateVeinCollection` 属性补丁、+`PlanetFactory.BuildFinally` 手动补丁；`StorageComponent.TakeTailItems` 原本已被 `P4_ConsumePriority` 手动补丁覆盖，只加 processor 不新增方法计数）。以启动日志实测为准。

| # | 核对项 | 期望 | 结果 |
| :-: | :--- | :--- | :--- |
| 1 | 版本与启动 | 横幅 `v0.16.0`；日志含 `[QSystem][build] instant-build quality capture`、`[QSystem][miner] vein-collector quality hook` | 待填 |
| 2 | Bug1 建筑点数 | 大型采矿机 `miner <eid>` 的 `modApi=GetBuildingInc` 非 0（4 点物品 → 4） | 待填 |
| 3 | Bug1 品质落点 | 首次出现 `[QSystem][miner] vein-collector quality path active: … inc=X->Y`；站点储物格的矿 tooltip/角标点数 >0；`inc` 不超过 `count×10` | 待填 |
| 4 | Bug1 速度 | 采集站里的矿机同样受速度加成（产出速率/`miner <eid>` 的 `time` 增量对比）；若仍无差异请附数值 | 待填 |
| 5 | Bug2 立即建造 | 4 点物品 Shift+立即建造 → `[QSystem][build] … perItem=4 bound=…`，落成建筑 `GetBuildingInc=4`；0 点物品 → 0 点 | 待填 |
| 6 | Bug2 隔离 | 普通手持建造、蓝图粘贴（Ctrl+C/V 与蓝图工具）品质行为不变（不重复/不丢） | 待填 |
| 7 | Bug3 增产额外产物 | 4 点原料 + 增产 → 额外产物 tooltip/角标点数 >0（不再固定 0）；与普通产物一致 | 待填 |
| 8 | Bug3 记账日志 | `DSPQS_DEV=1` 时 `[QSystem][assembler] pool credited produced=… net=… picked=… pooled=…` 出现且 `produced ≥ net` | 待填 |
| 9 | Bug3 原版语义 | 总产量、增产概率、耗电、配方与原版一致（不因修复改变产出数量） | 待填 |
| 10 | 存读档 | 存档尾块 schema 不变；读档后矿机/装配机品质池与建筑点数继续工作；v0.15.0 存档可读 | 待填 |
| 11 | 关闭等价 | 既有开关（`itemQualityBadges`/`speedBonus` 等）关闭时仍与原版逐字节等价；本版新增补丁不新增 cfg 开关 | 待填 |
| 12 | 回归 | 小型矿机、拆除返还、分馏塔、储物仓格数、分流器勾选、10 点光环均无退化 | 待填 |

**离线已覆盖（不占实机）**：`QualityMath.StationIncAfterTransfer`（0/负值、`perItem` 上限 10、`count×10` 封顶、单调）、`MinerMaxInc/MinerChance` 边界（R-3 半点点数）、`QualityMath.ProducedDelta`（取货补偿、增产单件滴出、负净值、`int.MaxValue` 溢出）、`QualityMath.InstantBuildPerItem`（`inc/count` 转换、混合叠平均、钳制 0..10、零/负输入）。合计 59 条检查全部 PASS。

**只有实机能验**：采集站真机搬运与 `storage[0].inc` 的落点/上限、大型采矿机速度的实际表现、Shift+立即建造的 `TakeTailItems`→`BuildFinally` 同帧假设与多建筑/复制模式下的匹配、增产额外产物在真实取货节奏下的点数、`patchedMethods` 实际数字、存读档往返、UI 提示/角标读到的点数。

**风险 / 偏差**：
1. Bug2 的捕获是**同帧启发式**（同帧 `TakeTailItems` + `BuildFinally` + `protoId/itemRequired` 匹配）；若官方流程或其它 Mod 改变取料/建造时序，该次会退化为原版 0 点（不会误给品质，也不会影响建造）。
2. Bug1 只在 `UpdateVeinCollection` 真的搬运动作时结算；仓储满/未搬运期间不记账，等下一次搬运补上，`inc` 封顶 `count×10`（站点储物格数量大时点数会按比例摊薄，属原版 `inc` 是整格总点数的语义）。
3. Bug3 只改品质池记账，不改 `produced[]`/`productRegister`/产量；若实机发现额外产物仍 0 点，需用 `pool/asser` 命令导出 `poolCount` 与 `produced` 数值再定位。
4. 本版未升级存档 schema；`pickedSinceTick` 为运行时侧表，不持久化（读档后从 0 开始，首 tick 由基线规则跳过）。

## v0.16.1 待实机验证（Lead 执行，本版交接时点 2026-10-30）

**交付物**：`bin\Release\DSPQualitySystem.dll` = **237,056 B**，SHA256 **`ECF9AE7B10DCF8F2FABD40B61F9E2DB367EE895DDD5B7229BACD9A4150C22553`**，AssemblyVersion 0.16.1.0（C-pack 交付时 235,520 B / `1B8F7FFD…`，Lead 实机复核发现过滤 UI 未接线并修正布局后为最终 237,056 B，见 `## v0.16.1 实机验证` 与 DEVLOG §27.28）；`build.ps1` → `BUILD OK` 0 警告 0 错误；离线（先删 `tests\OfflineTests\bin|obj`）→ **`ALL PASS (66 checks)` / `TEST PASS`**；未部署、未启动游戏，`dist\`/`_handoff-20261026\`/`_analysis\` 未改动。

**本版内容**：按用户口径**重做 C-2**——废弃 v0.13 的「品质优先（按输入品质降序）」，改为**四向分流器每个输出口可设「物品 X + 品质 ≥N」规则**：命中规则的货优先走该口、其余走无规则口；规则在分流器面板底部「品质过滤」折叠区设置（每口一行：物品按钮 + `≥N` 按钮 + 清除）。**新增 C-3**：同一套「物品+阈值」接入分拣器（原版物品过滤器 + Shift 右键阈值），抓货时跳过低于阈值的货。旧 `P5_SplitterPriority*` / `QualityMath.Splitter.cs` / 测试与 F3 热键全部退役删除；旧存档 `splitters` 布尔节点读档时忽略并打一次性日志。无新增玩家 cfg 键（仍 16 键），两个功能由内置常量控制；存档 schema 仍为 1（新节点 `splittersV2` / `sorters`）。

**patchedMethods 预期**：启动 **18**（PatchAll 段不变）；运行时默认 **47 → 56**（净 +9：删旧分流器 3 个手动目标，新增分流器 3 + 分拣器 tick 3 + `CargoTraffic.TryPickItem` 2 个重载 + 分拣器 UI 4 = 12 个不同方法）。以启动日志实测为准。

| # | 核对项 | 期望 | 结果 |
| :-: | :--- | :--- | :--- |
| 1 | 版本与启动 | 横幅 `v0.16.1`；`patchedMethods=18`；日志含 `[QSystem][splitterfilter] patch registered`、`[QSystem][sorterfilter] patch registered` | 待填 |
| 2 | 分流器 UI 位置 | 打开分流器面板：底部出现「品质过滤 ▸」折叠标题；展开后 A/B/C/D 四行与四个过滤圈/标题/图例**不重叠**（`uitree` 查 `QSystemSplitterQualityPanel`、`QSystemSplitterOut0Rule` 坐标） | 待填 |
| 3 | 分流器规则生效 | 口 A 设「磁铁 ≥5」、口 B 无规则：8 点磁铁只从 A 出，0 点磁铁从 B 出；阈值改 9 后 8 点磁铁回到无规则口 | 待填 |
| 4 | 分流器多档 | 口 A `≥8`、口 B `≥5`：9 点→A、6 点→B、3 点→无规则口 | 待填 |
| 5 | 分流器让路 | 同一台设原版输入/输出优先级或原版过滤器图标、或上方叠储物仓 → 本功能整体让路（日志 `paused`），取消后规则恢复 | 待填 |
| 6 | 分流器持久化 | 设规则 → 存盘 → 重进读档：规则仍在（尾块 `splittersV2` 节点）；旧 `splitters` 节点被忽略并打一次日志 | 待填 |
| 7 | 分拣器 UI | `filterButton` 选物品后 Shift+右键可循环阈值，`filterText` 显示「物品名　品质≥N」；Ctrl+Shift+右键清零并立即刷新 | 待填 |
| 8 | 分拣器生效 | 物品=磁铁、≥5：0/6 点混流只抓 6 点；阈值改 0 恢复原版抓取；无合格货时不抓、不卡住皮带 | 待填 |
| 9 | 分拣器边界 | 发电厂/燃料抓取（`CargoPath.TryPickFuel`）不受阈值影响；needs 配方（如研究/制造需料）仍按原版取料清单 | 待填 |
| 10 | 分拣器持久化 | 存读档后阈值仍在（尾块 `sorters` 节点） | 待填 |
| 11 | 旧档兼容 | 读 v0.15/v0.13 存档（含旧 `splitters` 布尔节点）：正常进游戏、无报错、日志一条 `legacy 'splitters' node ignored` | 待填 |
| 12 | 关闭/等价 | 0 条规则时分流器与分拣器行为与原版一致（同一台机器设/清规则对照）；`F3` 按下无反应（热键已删） | 待填 |
| 13 | 回归 | 储物仓品质格数、物品角标、A 类五件套、大型采矿机、增产、拆除返还、原版分拣器过滤（不设阈值）均无退化 | 待填 |
| 14 | 存读档整体 | v0.16.0 存档可读；尾块 JSON 仍与其它 Mod 尾块共存；`ALL` 品质数据正常 | 待填 |

**离线已覆盖（不占实机）**：规则 pack/unpack 与边界（item 0/越界、min 0–10 钳制）、`SplitterRuleMatches` 含等号、`PlanSplitterRoute`（无规则=output0..3 原版顺序、min 降序优先、同 min 按口序、规则口不参与兜底、规则口非输出被忽略、四口全规则无匹配=0 候选、null/退化输入）、`FindOutputIndex`、阈值循环（0→…→10→0 与反向）、`PlanSorterPick`（item 过滤、inc≥min、needs 叠加、空/全不合格）、UI 文案纯函数、v0.16.1 尾块 JSON 往返（`splittersV2`/`sorters` 字段级 + `SaveGuard` 校验）、v0.15 旧档（旧 `splitters` 节点）解析且不产生新节点。合计 66 条检查全部 PASS。

**只有实机能验**：分流器 4 行 UI 的真实坐标/遮挡与折叠增高、点击物品选择器与实际物品图标、每口规则下的真实物流走向、原版优先级/过滤器让路后的表现、分拣器 Shift+右键手势与原版 `filterButton` 的事件共存（是否被原版覆盖）、`filterText` 显示、`TryPickItem` 接管后抓取节奏与吞吐、发电厂燃料路径、旧档忽略日志、`patchedMethods` 实际数字、存读档往返。

**风险 / 偏差**：
1. 分流器接管是**完整复刻原版收集/分配循环**（Prefix 返回 false），原版的 `topId` 储物仓回灌分支、`bottomId`、优先级预设全部让路保持原版；若官方改动 `UpdateSplitter` 内部结构，本补丁可能冻结旧语义——启动自检只校验方法签名存在（`ref SplitterComponent`），不校验 IL，实机若出现"货物被吞/不动"请先看 `[QSystem][splitterfilter]` 日志。
2. 规则口**不参与兜底**：若某物品只该去规则口但所有规则口都占满，货会留在原皮带（不丢），可能造成短暂堵带——这是与"规则即专属口"语义一致的取舍。
3. 分流器规则以「物理口」存（`beltA..D` 的 beltId），皮带重连后 port 语义随物理位置保持；若玩家拆掉该口皮带，规则仍保留在 port 上、等接回后再生效。
4. 分拣器只覆盖 `CargoTraffic.TryPickItem`；`CargoPath.TryPickFuel` 的发电厂燃料抓取不覆盖（阈值对燃料无效），已写入 README 已知限制。
5. 分拣器在「抓取窗口」内找第一件合格货（原版窗口 = 皮带尾部 5 格附近），窗口外的货要等它走进窗口——不是全局扫描，避免每 tick 遍历整条皮带。
6. 旧存档 `splitters` 布尔勾选**不做迁移**：旧语义（先处理高品质输入）与新语义（每口规则）不同，强行映射会改变物流；旧节点只忽略并打一次性日志。
7. 本版未升级 `SaveGuard.SchemaVersion`；新节点在旧 DLL 里被忽略（旧 DDL 读新档 = 无过滤规则，不影响存档可读性）。
8. 两个新功能**没有玩家 cfg 开关**（内置常量默认开启，遵守 v0.15.0「16 键」约束）；0 规则/0 阈值时与原版行为一致。

**交接状态（2026-10-30，v0.16.1 时点）**

- **本版改动（相对 v0.16.0）**：C-2 重做为「分流器输出口品质过滤」（4 行面板 UI）+ 新增 C-3「分拣器品质过滤」（原版 filterButton + Shift 右键阈值）；退役删除 v0.13 的「品质优先」整套（2 个 patch 文件 + 纯函数文件 + 2 个测试文件 + F3 热键 + 面板勾选框 + 标题后缀 + 旧 `splitters` 读写）。
- **交付物**：见上「交付物」段。
- **版本链追加**：v0.16.0 `8CB2CB75…` 225,280 B（59 checks）→ v0.16.1 C-pack 交付 `1B8F7FFD…` 235,520 B → **v0.16.1 最终 `52844426…` 237,056 B（66 checks，Lead hotfix）**。
- **待办（下一版）**：v0.16.1 上述 14 项待实机验证；老回归 #1/#2/#4 与 #22b、A 类五件套逐功能实机数值对比；分流器规则在真实多口物流（含储物仓回灌机型）下的表现；分拣器阈值对各类取料目标（装配机/研究站/储物仓/物流塔）的覆盖确认；README/DEVLOG 已在同版同步。

## v0.16.1 实机验证（Lead 执行，2026-10-30 00:05–00:55，离屏窗口 + harness v11）

| # | 项目 | 期望 | 实测 / 证据 | 结论 |
|---|---|---|---|---|
| 1 | 启动横幅与补丁数 | `v0.16.1`、`patchedMethods=18` | `=== DSP Quality System v0.16.1 ===`、`Harmony PatchAll OK … patchedMethods=18` | ✅ |
| 2 | 分流器路由补丁 | 注册 + armed | `[QSystem][splitterfilter] patch registered: CargoTraffic.UpdateSplitter + UpdateSplitterAsync [per-port quality filter]`、`armed (methods=2, routed=0, paused-topId=0)` | ✅ |
| 3 | 分拣器阈值补丁 | 注册 + armed | `[QSystem][sorterfilter] patch registered: InserterComponent ticks=3, CargoTraffic.TryPickItem=2 [quality threshold]`、`armed (methods=5, picked=0, blocked=0)` | ✅ |
| 4 | 规则 CRUD（harness 反射 MOD API） | 4 口读写成对 | `splitterrule 139 0 1001 3` → `packed=197609 back=197609 ok=true`；`… 1 1102 6` → `packed=394318`；`splitterrules 139` 回读 port0/port1 正确、port2/3 空 | ✅ |
| 5 | 规则计数 | `totalRules=2` | `filtercount hasAnyRules=true totalRules=2 planet=102 splitterRules=2 sorterRules=0` | ✅ |
| 6 | 过滤 UI 接线（Lead 修复后） | 面板创建 + 4 行 | `[QSystem][splitterfilter] UI patch registered: UISplitterWindow._OnUpdate postfix (QSystemSplitterQualityPanel)`；`uitreeall QSystemSplitter` → `QSystemSplitterQualityPanel pos=12,8 size=366,24`、`QSystemSplitterOut0..3Rule`（折叠 `active=false`） | ✅ |
| 7 | 分拣器 UI 接线（Lead 修复后） | UI patch registered | `[QSystem][sorterfilter] ui patch registered: UIInserterWindow methods=4 [Shift+right-click = quality threshold]`、`armed (ui clicks=0, captions=0)` | ✅ |
| 8 | 折叠条布局 | 完整可读、不被圆角边框裁切 | 修复前两版均压底边框；最终版截图 `_live\v0161-ui3.png`（裁剪 `v0161-window.png`）头部在窗口内、距底边约 26 px | ✅ |
| 9 | 规则随存档往返 | 存读档后规则不变 | `save QSTest` → `loadfull QSTest` → `filtercount totalRules=2`、`splitterrules 139` 回读 port0 `铁矿 min=3`、port1 `磁铁 min=6` | ✅ |
| 10 | 展开态 4 行 UI（布局/行内容/口序） | 4 行完整落在窗口内、口 A→D 自上而下 | harness v12 `splitteruiexpand 139 on` → `expanded=true rows=4`、面板 24→124 px；`uitreewin splitterWindow 4` 显示 4 行各自 `Port/Item/Min/Clear`；截图 `_live\v0161-exp3.png` 口A 铁矿≥3 / 口B 磁铁≥6 / 口C-口D 不限，头部在行下方、全部在框内 | ✅（真实鼠标点击物品/阈值/清除未验） |
| 11 | 分拣器窗口阈值文案 | 过滤器按钮下方显示「品质≥N」 | `place 2014` → eid=125；`sorterrule 125 6` → `inserterId=15 min=0 set=6 back=6 ok=true`；`sorteruiopen 125` → `ok=true title=集装分拣器`；截图 `_live\v0161-sorter2.png` 按钮下方显示 `品质≥6` | ✅（Shift+右键真实点击未验） |
| 12 | 真实多口物流路由（规则命中 / 兜底 / 全占满堵带） | 高质点走规则口、低质点走无规则口 | 未验证（需要实机铺带喂货；用户侧验证项） | ⏳ |
| 13 | v0.16.0 三 bug（大型采矿机品质 / Shift 立即建造 / 增产额外产物） | 见 DEVLOG §27.26 | 启动自检 OK（`vein-collector quality hook`、`instant-build quality capture`）；真实矿脉/立即建造/增产场景未在离屏档复现 | ⏳ |

**实机通道**：`_live\vis-host.ps1`（离屏窗口 1280x720 → SetWindowPos 到 (4000,4000)）+ `DSPQSDebugHarness` v11（命令队列 `BepInEx\config\DSPQSDebugHarness\cmd`）+ `_live\qs-send.ps1` + `_live\capture.ps1`（PrintWindow 2560x1600）。

- 「⏳」项需要在有真实矿脉、真实带流或键鼠注入的环境补测；本轮已把 UI 入口、布局与规则持久化三条关键链路验通。


## v0.17.0 待实机验证（Lead 执行，本版交接时点 2026-10-30）

- **本版改动（相对 v0.16.1）**：品质过滤编辑器（分流器每输出口「物品 + 品质 ≥N」、分拣器品质阈值）**整体迁入原版物品选取器 `UIItemPicker` 底部一行**；删除分流器窗口里的旧面板（`P5_SplitterFilterUi.cs` 整份移除）；分流器让路条件放宽（不再因 `outPriority`/`outFilter != 0` 整体让路，未命中规则货物按原版语义兜底）；存档格式与玩家 cfg 键不变。
- **交付物**：`bin\Release\DSPQualitySystem.dll` **238,592 字节**，SHA256 **`F71768BE27D49C8FFEA2F2AD170FFA4EB0F6608C1ACEBACE0DB2A699CDC31854`**，AssemblyVersion **0.17.0.0**，`BUILD OK`（0 警告 0 错误），离线 `ALL PASS (66 checks)`。
- **`patchedMethods` 预期**：启动 **21**（v0.16.1 为 18；-1 旧 `UISplitterWindow._OnUpdate`，+4：`UISplitterWindow.OnCircleFilterClick`、`UIInserterWindow.OnSelectFilterClick`、`UIItemPicker.Popup`、`UIItemPicker._OnClose`）；运行时 **59**。

| 步骤 | 期望证据（日志原文/观察） | 结论 |
| --- | --- | --- |
| 1. 启动 | `[QSystem][filterpicker] armed (vanilla item picker footer; clicks=0 writes=0)`；`[Audit] filterpicker -> armed …` 无 MISSING；四条新 CheckMethod 全 OK | |
| 2. 分流器入口 | 点分流器面板的过滤圆 → 物品选取器打开，底部出现 `口A 品质分流 [关] 物品: 任意物品 [-] 不限 [+]`（`uitree` 可见 `QSystemFilterPickerRow`，layer=5，落在 itemPicker 矩形内） | |
| 3. 点击开关/阈值 | 点开关 → `[开]`；点 `[+]`×3 → `≥3`；日志 `[QSystem][filterpicker] splitter p=… e=… port=0 on=True item=0 min=3` | |
| 4. 选物品 | 在网格点「铁矿石」→ 物品标签变「铁矿石」，规则 = 铁矿 ≥3；同时原版过滤圈/`outFilter` 照旧生效 | |
| 5. 重开保持 | 关掉 picker 再点同一过滤圆 → footer 显示 开 / 铁矿石 / ≥3 | |
| 6. 物流（分流器） | 混品质铁矿：≥3 点走 A 口，其它走无规则口；`[QSystem][splitterfilter] routed=` 递增；堵带时不丢货 | |
| 7. 输出优先级/过滤兼容 | 该分流器设了原版输出优先级或全局过滤后仍能按规则分流（不再整台 paused），未命中规则的货按原版 `outFilter` 语义走 | |
| 8. 分拣器入口 | 点分拣器过滤按钮 → picker 底部出现 `分拣器 品质阈值 [-] 不限 [+]`；`[+]`×5 → `≥5` 并保留物品选择 | |
| 9. 物流（分拣器） | 皮带混 0/5 点同物品：setter 只抓 ≥5 点；`[QSystem][sorterfilter] picked=… blocked=…` 增长；低于阈值不消耗、不卡死 | |
| 10. 存档往返 | 设 2 口规则 + 1 个分拣器阈值 → `save QS17` → `loadfull QS17` → footer 回读一致；日志 `… splitters=2 sorters=1` | |
| 11. 旧档兼容 | v0.15 旧档（`splitters:[{"e"}]`、无 `splittersV2`）可正常读，一次性日志 `legacy 'splitters' node ignored` | |
| 12. 点击可靠性 | 反复点开关/加减/清空 20 次无一次落空、不会把 picker 点关（点窗外判定不再触发） | |
| 13. 显示 | 高分辨率与 150%/200% UI 缩放下 footer 不与原版底部控件重叠（若重叠记录截图，属已知视觉瑕疵） | |

- **离线已覆盖**：规则打包/匹配、路由顺序（阈值降序→口号升序、无规则口兜底、规则口不参与兜底、全口占满留原地）、阈值 0–10 循环、分拣器窗口内候选选择与 needs 叠加、UI 文案纯函数、`splittersV2`/`sorters` 尾块 JSON 往返与旧档兼容、SaveGuard 校验。
- **只有实机能验**：`UIItemPicker` footer 的建立与点击命中、开关/加减/清空的实际写入、选物品时原版 `outFilter`/`inserter.filter` 与本 Mod 规则是否都落盘、真实多口物流下「规则命中/兜底/堵带」、分拣器真实抓货筛选、存读档往返后的 footer 回读、`uitree` 坐标/layer、`patchedMethods` 实测值。
- **风险/偏差**：① picker 里新增控件若布局与原版底部元素重叠，只影响观感不影响点击；② `UIItemPicker._OnClose` Postfix 清上下文，若原版关闭流程被别的 MOD 改写可能拿不到规则写入时机（写入已在包装器内完成，最坏退化为不写）；③ 让路条件放宽后，设了原版输出优先级的分流器也会被接管——未命中规则的货物已按原版语义兜底，但极端布置（output0 未连）下比原版更宽松；④ 分拣器窗口里不再有阈值文案/手势（v0.16.1 的 `filterText` 改写与 Shift+右键已移除），全部入口集中在 picker footer。

**交接状态（2026-10-30，v0.17.0 时点）**

- **本版改动**：品质过滤编辑器迁入原版物品选取器（UI 重构）；`P5_SplitterFilterUi.cs` 删除；分流器让路条件放宽 + 原版兜底复刻；存档/cfg 不变。
- **交付物**：DLL 238,592 B / SHA256 `F71768BE27D49C8FFEA2F2AD170FFA4EB0F6608C1ACEBACE0DB2A699CDC31854` / AssemblyVersion 0.17.0.0 / `BUILD OK` 0 警告 0 错误 / 离线 `ALL PASS (66 checks)` / `patchedMethods` 启动 21、运行时 59（预期）。
- **版本链追加**：v0.17.0（§27.29 交付为 238,592 B，`F71768BE…`，66 checks；§27.30 根因修复后为 **241,664 B，`72179805…`，72 checks**，以本文件下方「待实机验证（二）」为准）。
- **待办（下一版）**：本表 13 条实机核对；「大矿机点数丢失」「Shift 立即建造 0 点」已由 DEVLOG §27.30 根因修复（见本文件「待实机验证（二）」）；A 类五件套逐功能数值对比；老回归 #1/#2/#4 与 #22b；5 个内置常量的 NOT ARMED 门控实机验证。

## v0.17.0 待实机验证（二）：大型采矿机放置点数 + Shift 立即建造品质（根因修复，Lead 执行，交接时点 2026-10-30 01:35）

- **本版改动（相对 §27.29 的 v0.17.0）**：把「最高品质优先消耗」的消费窗口从「蓝图粘贴」泛化到「手持建造 / 背包建造 / 蓝图粘贴 / Shift 立即建造」四种，并把收据（receipt）交给建造落成绑定——修复①从背包分支建造的建筑丢点数（大型采矿机 2316 即此路径）②Shift+左键立即建造取料丢点数且可能取到 0 点尾栈。**无新 cfg 键、存档结构不变**。
- **交付物**：`bin\Release\DSPQualitySystem.dll` **241,664 字节**，SHA256 **`7217980504A716BA54DB06330DCACF74A2BE81D43FCC76879E364CBF9DD1B0CD`**，AssemblyVersion **0.17.0.0**，`BUILD OK`（0 警告 0 错误），离线 **`ALL PASS (72 checks)`**。
- **`patchedMethods` 预期**：启动 **21**（与 §27.29 相同，本版未增删特性补丁）；运行时 **60**（+1 手动补丁目标 `PlayerAction_Inspect.GameTick`）。

| 步骤 | 期望证据（日志原文/观察） | 结论 |
| --- | --- | --- |
| 1. 启动 | `[QSystem][build] patch registered: instant-build consume window (GameTick) + BuildFinally binding …`；`[Audit] PlayerAction_Inspect.GameTick(long) -> OK`；`[QSystem][consume] armed …` | |
| 2. 普通背包建造 | 手上不拿同种物品、用 4 点熔炉直接背包建造 ⇒ 落成 4 点（F9 / 拆除返还 4 点 / 日志 `prebuild binds consumed item inc=4`） | |
| 3. 大型采矿机放置 | 4 点物品建造大型采矿机 ⇒ 建筑点数 4（拆除返还 4）；0 点物品 ⇒ 0 点 | |
| 4. 手持建造回归 | 手上有 4 点同种物品建造 ⇒ 仍落成 4 点（旧 `BuildQualityStash` 路径不回归） | |
| 5. Shift 立即建造（高品） | 背包混放 4 点 / 0 点同种物品，Shift+左键立即建造 ⇒ 落成 4 点；日志 `instant-build quality bound … source=receipt` | |
| 6. Shift 立即建造（0 点） | 背包只有 0 点物品 ⇒ 落成 0 点（退化原版、不凭空造品质） | |
| 7. 立即建造副作用 | 建造后产物/产业链正常：无卡料、无多余耗材、无重复扣料；普通取物/分拣/物流顺序不变 | |
| 8. 收据兜底 | （可选）用 `DSPQS_DEV=1` 观察 `[QSystem][build] instant-build quality captured …`（无收据时的取料记录兜底）与 `[QSystem][assembler] pool credited …` 不异常刷屏 | |
| 9. 存档往返 | 建 4 点大型采矿机 + 4 点普通建筑 → `save QS18` → `loadfull QS18` ⇒ 点数一致（既有 `buildings` 节点，不新增字段） | |
| 10. `patchedMethods` | 启动 21 / 运行时 60 | |

- **离线已覆盖**：`QualityMath.ReceiptPerItem`（百分位 floor/clamp/单调）、`ReceiptItemMatches`（大型采矿机 2316 与普通建筑 2301 同规则、0/负 = 通配、异物品拒绝）、`ConsumeWindowActive`（三窗口并集 8 种组合）、`InstantBuildPerItem`（count>1 摊薄：4/1→4、16/4→4、12/3→4、11/3→3、3/4→0、999/2→10；站点型与普通建筑同一条纯函数）、`PreferBindingValue`（收据优先/记录兜底/clamp）、端到端决策表（背包建造 4→4、立即建造收据 4→4、无收据退回记录、0 点输入 0 点输出）。
- **只有实机能验**：`PlayerAction_Inspect.GameTick` Prefix 是否在按住 Shift 的那一帧打开窗口（Harmony 补丁是否命中当前游戏版本）、原版是否真的走 `P4_ConsumePriority` 的最高品质取料（需要背包混品质实测）、`BuildFinally` Prefix 的 `prebuildPool[prebuildId].id` 校验在真实建造流程下是否成立、大型采矿机放置点数是否随拆除返还、普通手持建造/蓝图粘贴不回归、真实存档往返、`patchedMethods` 实测值。
- **风险/偏差**：① 收据只在同帧内有效，若官方流程把「取料 → BuildFinally」拆到不同帧（例如网络/延迟帧），会退化为原版 0 点（建造本身与耗材不变）；② 立即建造的窗口条件复刻原版（`instantConstruct` + `hoveringPrebuildId>0` + hasSupport/hasEnoughItem），若 `hoveringPrebuildId` 在取料帧为 0（极端时序）则窗口不打开、退回取料记录兜底；③ 背包分支建造现在会优先消耗最高品质同类建筑物品——与手持建造口径一致，但若玩家依赖「先消耗低品质」的原版尾栈顺序会感到行为变化（设计目标）；④ `pickedSinceTick`/`prebuildInc` 仍为运行时内存数据，不随存档持久化（放置点数落成后进入 `buildingInc` 正常持久化）。

## v0.17.1 待实机验证：品质过滤 per-item 修复（交接时点 2026-10-30 02:35）

- **本版改动**：① 分流器/分拣器把「整栈总 `Cargo.inc`」换算成「每件点数」`QualityMath.PerItemPoints(inc, stack)` 后再与阈值比较（根因见 DEVLOG §27.31.2，asm:28583 合并栈 `inc += inc`）；② 分流器有规则时**完全接管**（不再因 `inPriority`/`prioritySlotPresets`/`topId`/`input0==0` 让路），命中规则的货只进规则口、口满就原地等；未命中规则的货走新 `PlanSplitterFallback`，**按 output0..3 顺序只选没有规则的口**、完全不看原版 `outFilter`；旧 `TryVanillaFallback` 删除；③ picker 里点网格物品即写 MOD 规则（v0.17.0 只在开关已开时才写，导致只留下被写脏的原版 `outFilter`）；④ 分拣器扫描用每件点数，整栈取货与原版 `filter` 语义不变。无新 cfg 键、存档 schema 仍 1（`splittersV2`/`sorters` 不变）。
- **交付物**：`bin\Release\DSPQualitySystem.dll` **245,248 字节**，SHA256 **`CB7769A7DAFC7FC7C44E35C39264958E399F2A7452A07779D5B4D0EA861849C4`**，AssemblyVersion **0.17.1.0**，`BUILD OK`（0 警告 0 错误），离线 **`ALL PASS (78 checks)`**（72 + 6）。

| 步骤 | 期望证据（日志原文/观察） | 结论 |
| --- | --- | --- |
| 1. 启动 | `=== DSP Quality System v0.17.1 ===`；`[splitterfilter] armed (methods=2, routed=0, took=0, fallback=0, unrouted=0, noop=0, skip[presetApplied=0,noRule=0,topStorage=0])`；`[sorterfilter] armed (methods=5, …, scanned=0)` | |
| 2. 分流器：低品质不入规则口 | 带上混放 0 点与 8 点同种物品，规则「物品 X ≥4」⇒ 8 点进规则口；0 点只走无规则口/停带；规则口 0 点计数不增长 | |
| 3. 分流器：脏 outFilter | 先设输出优先级/原版全局过滤再设 MOD 规则 ⇒ 低品质货仍不入规则口（路由层忽略 `outFilter`） | |
| 4. 分流器：规则口满 | 规则口堵满时合格货原地等待（DEV 日志 `blocked …`/`unrouted` 增长），不溢出到无规则口 | |
| 5. 分流器：点物品即生效 | 打开过滤圆 → 网格点物品（不点开关/阈值）⇒ 立即写入规则（日志 `splitter port=… on=True item=…`），低品质货开始被挡 | |
| 6. 分拣器：阈值生效 | 带混 0/8 点，`Shift+右键`调到 4 ⇒ 只抓 8 点；DEV 日志 `pick …`/`scan … [1002x2@0/0]`/`blocked`/`picked … pts=…` 可核对 | |
| 7. 分拣器：min=0 回归 | 阈值调回 0 ⇒ `pick-min0` 后完全走原版，抓货顺序/数量不变 | |
| 8. 无规则回归 | 清空所有规则 ⇒ 分流器 Prefix 走 `skip[noRule]` 直接返回原版；分拣器 `picked=0 blocked=0` | |
| 9. 存档往返 | 规则写入 → `save` → 重进 ⇒ `splitterrules`/`sorterrule` 回读一致，schema 1、16 cfg 键不变 | |

- **离线已覆盖（78 checks，本版新增 6 条）**：`QualityMath.PerItemPoints`（1pt/2 件=0、23pt/100 件=0、stack<=0、clamp）、`PlanSplitterFallback`（原版槽位顺序、排除规则口、空槽、规则口当前是输入、全口有规则→0）、低品质栈不进规则口（对照 raw total 会误匹配）、`PlanSorterPick` 每件阈值 + `min=0` 原版回归 + needs 叠加。
- **只有实机能验**：真实皮带栈的 total/per-item 分布、用户现场（分流器 e=139 口3、分拣器 e=176）修复后的实际物流、原版过滤圆 `outFilter` 与 MOD 规则共存时的显示/行为、`patchedMethods` 实测值。
- **本轮自动化实机环境限制（重要）**：`loadfull <slot>` 在本次会话稳定失败——`DSPGame.StartGame` 从标题界面调用时 `GameLoader` frame 1 `Instantiate` 新 `GameMain`，`GPUInstancingManager.Init` 抛 `NullReferenceException at GPUInstancingManager.Init () [0x006F]`（LDB 模型 prefab 未就绪）。**隔离实验：移除 `DSPQualitySystem.dll` 后同样失败** ⇒ 与本 MOD 无关，是 harness `loadfull` 调用方式问题；`load`（`GameSave.LoadCurrentGame`）亦不可用。另该会话桌面无法被 `CopyFromScreen`/`PrintWindow` 合成（截图全黑），故本轮没有游戏内截图，仅取得启动日志（armed/反射自检全部 OK）。请用户按上表在真实会话复测。

## v0.18.0 待实机验证：发电部署 + 供电范围 + 升品曲线 + 矿机确定性 + 虚影补付（交接时点 2026-10-31 14:05）

- **背景**：用户实机反馈"发电类设施不改变发电功率、电力扩展类建筑供电范围没扩展、制造类升品概率过低、矿机增产点数不符合预期、材料不足的虚影补付后按 0 点落成"。经取证，第一条的真因是**用户装的 DLL 是 v0.11.10（157,184 B / 2026-10-26 01:00:49）**，里面没有 v0.12.0 才加入的 P5 发电补丁；供电范围是 MOD 从未实现的新功能；其余三条是数值/机制问题。
- **交付物**：`bin\Release\DSPQualitySystem.dll` **253,952 字节**，SHA256 **`F3B9E800368F48AAB7B08CEEA2E3EF76366C62593F92C96C7A847C38490191DE`**，AssemblyVersion **0.18.0.0**，`BUILD OK`（0 警告 0 错误），离线 **`ALL PASS (82 checks)`**（78 + 4）。
- **部署状态**：需要用户**关闭游戏**后执行 `build.ps1 -Deploy`（或手工替换 `BepInEx\plugins\DSPQualitySystem\DSPQualitySystem.dll`）。2026-10-31 13:57 时游戏进程 `DSPGAME`（PID 29340）仍在运行、插件目录仍是旧 DLL。

### 启动自检（先看这几行）

| # | 检查项 | 期望 | 结果 |
|---|---|---|---|
| 1. 启动 | `=== DSP Quality System v0.18.0 ===` | 出现 | |
| 2. 发电 | `[powergen] armed (… methods=5/5 …)` | 5/5 全绑定 | |
| 3. 供电范围 | `[powerrange] armed (… methods=1 …)` | armed，无 `NOT ARMED` | |
| 4. 虚影补付 | `[deferred] armed (… methods=4 …)` | armed | |
| 5. 自检 | `[Audit]` 段全部 `-> OK` | 无 FAIL | |

### 行为核对清单

| # | 场景 | 期望 | 结果 |
|---|---|---|---|
| 1 | 10 点风力发电机 vs 0 点同型号，看电网面板容量 | 10 点 ≈ **2.00×**（火力机燃料消耗同步变快、射线接收站产出同步变多 = 文档化双收益） | |
| 2 | 10 点感应塔/无线输电塔/配电站 | 覆盖圈与拉线距离 ≈ 0 点的 **2 倍**；存读档往返后仍保持；拆掉重建按当前点数重算 | |
| 3 | 制造台升品：1 点 / 4 点 / 7 点 / 10 点 | `[QSystem][roll]` 的 `base=` 依次约 2.5% / 10% / 30%（7 点双掷）/ 10 点必 +2（不掷骰） | |
| 4 | 矿机：2 点 / 4 点 / 10 点 | 产物每件固定 **+1 / +2 / +5**（确定性，不再随机）；大矿机（站点型）同规则 | |
| 5 | 材料不足时建造 → 白虚影 → 补足材料 | 补付完成后落成建筑**带点数**（`[deferred] payments/bound` 递增；或拆掉看返还物品点数） | |
| 6 | 回归：`powerGenBonus=false`（cfg） | 发电回到原版功率；供电范围为内置常量恒开，不受该键影响 | |
| 7 | 回归：旧存档（v0.17.x）读档 | 已有电力塔按当前点数重算范围，无报错；品质数据照常读取 | |

### 已知边界

1. 供电范围倍率写在原版持久字段 `PowerSystem.nodePool[].connectDistance/.coverRadius`，按「prefab 原值 × 倍率」幂等重算；卸载本 MOD 后这些字段保持放大后的值（存档已写实），不会自动回收。
2. 虚影补付走原版 `ConstructionModuleComponent.PlaceItems` 的一次性全额付款，因此该路径**不做**"最高品质优先消耗"的换格/拆点，只继承实际被扣材料物品的点数。
3. 火力发电 / 射线接收站的"双收益"（多烧燃料、多产出）是设计选择，不是 bug；嫌强把 `powerGenBonus` 关掉即可。

## v0.18.0 实机静默调试复核（2026-10-31 14:45，Lead 亲自执行）

环境：`DSPQS_DEV=1`（用户级环境变量，重启 Steam 客户端后生效）→ 调试热键 F5..F12 与逐帧调试日志开启；
游戏 pid 7952 / unity 2022.3.62f3c1 / game 0.10.35.29151；存档 `TEST.dsv`（星球 天市右垣五 - 3号星 id=103）。

| 需求 | 取证方式 | 结果 |
|---|---|---|
| ①a 发电功率 | 对射线接收站（entity 11, proto 2210）注入 4 点 | `[QSystem][powergen] output bonus active (planet=103 entity=11 factor=1.10 capacity=264000005)`；`/state` 电网 supply `720,000,000 -> 744,000,005`（+24,000,005 = 240M 的 10%），**通过** |
| ①b 供电范围 | 对电力节点（entity 38）注入 4 点，退出到主菜单后"继续"重载 | `[QSystem][powerrange] eid=38 node=1 points=4 factor=1.1 conn=58.85 cover=29.15`、`eid=11 node=3 points=4 factor=1.1 conn=16.5 cover=5.5`（prefab 基准 conn 53.5/15、cover 26.5/5，乘 1.1），**通过** |
| ② 升品概率 | 运行期 roll 日志 | `[roll] eid=164 in=0 bld=10 mode=2 base=0 extra=0 pooled=2`（10 点必定 +2、不掷骰）、`bld=4 mode=0`（单掷，曲线 10%），**通过** |
| ③ 矿机确定性 | F12 探针 | `[Probe][F12] miner quality hits=63 calls=63 lastMinerCall=eid=132 level=2 minerChancePerPoint=100` → 2 点矿机每次产出必 +1，**通过** |
| ④ 白虚影补付 | 自检行 + **用户实测** | `[QSystem][deferred] deferredPay armed=True reason=- payments=0 bound=0`（Lead 侧仅 armed）；**用户 2026-10-31 14:52 实测确认已修复**：材料不足产生的白色虚影在材料补齐后落成，建筑带点数 |

其它证据：
- `[Probe][F9] planet=103 entities=1023 buildingInc 0=1005 4=6 10=12`（该星球 1023 个建筑里 12 个 10 点、6 个 4 点）。
- `[Probe][F5]` 制造台 eid=164/191/212/228/31/96/116/181 均为 `buildingInc=10`，产出走 `mode=2`。
- 注：本轮为取证在 `TEST.dsv`（经"继续"落盘的 `_lastexit_`）里给 entity 11、38 注入了 4 点，属测试污染，复测后可拆除重建或忽略。

复现命令（下次可直接抄）：

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev-loop.ps1 -Mode up -TimeoutSec 300
# 主菜单点"继续"（物理像素 220,393）；进档后：
powershell ... -Mode key -Key f9     # 建筑点数分布
powershell ... -Mode key -Key f12    # 矿机统计
powershell ... -Mode key -Key f5     # 制造台点数与掷骰
powershell ... -Mode click -X <屏幕坐标> -Y <屏幕坐标> 然后 -Mode key -Key f11  # 给瞄准建筑注入 4 点
```

进档后如需回主菜单：Esc 打开暂停菜单 -> 点"退出游戏"（物理像素 961,751）-> 主菜单点"继续"（220,393）。

## v0.19.0 交接（2026-10-31，喷涂次数 = 1 / 品质回收机）

### 交付物

- `DSPQualitySystem.dll` **271,360 字节 / SHA256 `5A43F0C747BD01862051C94B7C26C7267B51D003AE38D10DBE5A6DF190FE52E8` / 2026-10-31 16:35:03 构建 / AssemblyVersion 0.19.0.0**（阈值由 3 下调为 1 后重新出包）；
- 离线测试 **86 checks**（`tests\run-tests.ps1` → `ALL PASS (86 checks)`）；
- 发版包：`dist\DSPQualitySystem-0.19.0.zip`、`-r2modman.zip`、`-source.zip`（体积与哈希以打包后 `Get-FileHash` 为准）。

### 启动自检（读 `BepInEx\LogOutput.log`）

| 关键字 | 预期 |
|---|---|
| `=== DSP Quality System v0.19.0 ===` | 版本、`assembly=0.19.0.0` |
| `[QSystem][spraycount]` | `armed: Mk.I/II/III = 1 spray per item (preload hook=True, legacy import hook=True, signature guard=True, patched now=3)` |
| `[QSystem][recycler]` | `recycler armed=True … mode=inc-feed selfCheck=ok pointsPerSpray=4 minPerItem=1 accounts=… carried=…` |
| `[Audit]` | 新增 `ItemProto.Preload(int)`、`SpraycoaterComponent.Import(BinaryReader)`、`SpraycoaterComponent.InternalUpdate`、`ABN_ProtoData.CheckProto` 全部 `-> OK`（`ABN_ProtoData.CheckProto` 缺失只警告，不影响其它功能） |
| `[QSystem][patch]` | `patchedMethods=18` 不变（特性类计数），全部手工注册后覆盖 67 个方法 |

### 行为核对清单（用户真机）

| # | 操作 | 预期 |
|---|---|---|
| 1 | 查看手持增产剂 tooltip / 喷涂机面板 | 写 "可用喷涂次数 1"（原版 12/24/60） |
| 2 | 载入旧存档后看喷涂机 | 罐内旧"次数"被折算成件数（日志 `[spraycount] legacy coater (entity N, item 1143): 604 -> 11 items`），喷出量与件数一致、不出现 60 倍量 |
| 3 | 启动后搜 `[QSystem][recycler]` | `recycler armed=True … mode=inc-feed selfCheck=ok pointsPerSpray=4 minPerItem=1 accounts=… carried=…` |
| 4 | 喷涂机**接上催化剂进料带**（原版放 1141/1142/1143 的那一侧），把**带点数的物品（≥1 点/件，含 Mk.I 的 1 点）**送进去 | 物品消失、不返还；probe 出现 `mode=harvest`、`tank` 次数上涨；10 点物品 = 2 次喷涂 + 余 2 点（日志 `recycled coater=… points=+10 sprays=+2`）；未喷涂的 0 点物品**不被消耗**（probe `reason=noPoints`） |
| 5 | 物品带那一侧照常喂未喷涂物品 | 照常喷涂，与回收互不影响 |
| 6 | 让喷槽满 600 次后继续喂 | 停止回收（probe `mode=tankFull`）、物品不再被消耗；消耗腾出空间后自动恢复 |
| 7 | 回收若干件后存档退出、重进 | 每机账号点数保留（`recyclers` 节点回读一致、不丢点） |
| 8 | 拆掉回收机后等 1200 tick | 日志 `pruned` +1，孤儿账号被清 |
| 9 | 回归：已有八项加成 / 三条过滤链 | 全部照常（`[speed]/[charge]/[lab]/[turret]/[powergen]/[powerrange]/[launcher]/[sprayer]` armed 行不变） |

### 已知边界

1. 品质回收机的**行为级验证已由用户 2026-10-31 实机确认通过**（上表第 3–8 条），离线单测 + 启动自检 `selfCheck=ok` 亦通过；验证通过后默认阈值按用户反馈由 3 下调为 1（<3 点的物品也回收），0 点货物仍放行。
2. 喷涂次数 = 1 是全局改写 `ItemProto.HpMax`：任何复用 1141/1142/1143 `ItemProto` 的第三方内容都会变成 1 次/件；自建 `ItemProto` 的 MOD 不受影响。
3. 与"增产剂可用次数"类 MOD 共存时后加载者生效（都改 `ItemProto.HpMax`）。
4. 回收机存档节点 `recyclers` 为可选字段（形如 `{"e":entityId,"p":累计点数,"c":容量}`）：旧档缺 key 视为空、早期 `"o"` 字段读档忽略；把新档给旧版 DLL 读时该节点被忽略（不会崩档，但账号点数丢失）。

### 复现命令

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev-loop.ps1 -Mode up -TimeoutSec 300
# 主菜单点"继续"（物理像素 220,393）；进档后按需要：
powershell ... -Mode shot                      # 截图（logs\shot-*.png）
powershell ... -Mode click -X <x> -Y <y>       # 物理像素点击
powershell ... -Mode log -Grep "\[QSystem\]\[(spraycount|recycler)\]"
```

| 2026-10-31 16:39 | 回收机阈值改为 1 后的构建（271,360 B / SHA256 5A43F0C7...） | 启动行 `[QSystem][recycler] recycler armed=True ... selfCheck=ok pointsPerSpray=4 minPerItem=1`；进档后原测试机 eid=439 罐已满，持续打 `effective=... tank-full eid=439 tank=600/600`（说明补丁在跑、只是罐满拒收） | 待验：空罐喷涂机的催化剂带上放单件 1-2 点货物，应被吞掉并每 4 点涨 1 次喷涂 |
## v0.19.1 交接（2026-10-31，读档丢批次点数 / 批次四舍五入 / 喷涂次数分级 / 分拣器过滤重写）

### 交付物

- `DSPQualitySystem.dll`：`293,376` 字节 / SHA256 `9F5E1D94E954C2EFF5961A87D4999210C4B25A0CD81797C80F840B26E39A0154` / `2026-10-31 17:15:58` 构建 / AssemblyVersion 0.19.1.0（构建后回填）；
- 离线测试 **94 checks**（`tests\run-tests.ps1` → `ALL PASS (94 checks)`，v0.19.0 为 86）；
- 发版包：`dist\DSPQualitySystem-0.19.1.zip`、`-r2modman.zip`、`-source.zip`（体积与哈希以打包后 `Get-FileHash` 为准）。

### 构建验证项（出包前后逐条勾）

| # | 检查 | 预期 |
|---|---|---|
| 1 | `build.ps1` | `BUILD OK`，0 警告 0 错误 |
| 2 | `tests\run-tests.ps1` | `ALL PASS (94 checks)`（新增 `Checks.V0191.cs` / `Checks.P6SprayUses.cs` 全过） |
| 3 | 启动行 | `=== DSP Quality System v0.19.1 ===`、`assembly=0.19.1.0` |
| 4 | `[QSystem][sprayuses]` | `armed=… patch=insert+belt+takeback …`；6 锚点全或无（`_armed = ok >= 6`，缺锚点打印 `patched N/6` 并列出缺哪个） |
| 5 | `[QSystem][sorterfilter]` | `armed: sorter quality threshold is enforced on tick=3/3 belt=2/2 pickFrom=1/1`（任一不足会 `NOT ARMED`） |
| 6 | `[Audit]` | 新增锚点 `PlanetFactory.EntityFastFillIn`、`Player.TakeItemFromPlayer`、`CargoPath.TryPickItem`、`CargoTraffic.TakeBackItems_Spraycoater`、`CargoTraffic.ClearItems_Spraycoater` 全部 `-> OK` |
| 7 | `[QSystem][patch]` | `patchedMethods=18` 不变（特性类计数） |
| 8 | 占位符 | 出包前把三份文档里的 `293,376` / `9F5E1D94E954C2EFF5961A87D4999210C4B25A0CD81797C80F840B26E39A0154` / `2026-10-31 17:15:58` 回填成真实值 |

### 实机核对清单（用户真机）

| # | 操作 | 预期 |
|---|---|---|
| 1 | 载入旧档（v0.19.0 保存的档），看正在生产的制造台 | 不再出现"首批产物点数偏低"：`DSPQS_DEV=1` 时 F5 显示 `inputLevel=6`（不是 `-1`），日志 `[QSystem][roll] … in=6 pooled=8`（不是 `in=0 pooled=2`） |
| 2 | 10 点制造台 + 6 点原料（一批 5 件共 29 点） | 探针 `[QSystem][assembler][batch] eid=… lvl=6 s0=5/29/…`；产物最终 8 点（旧版 7 点） |
| 3 | 喷涂次数分级表 | 0–2 点 → 1 次、3–4 → 2、5–6 → 4、7–8 → 5、9 → 7、10 → 10；同一台喷涂机分别插不同点数的增产剂，可用次数按表变化 |
| 4 | 取回不复制 | 往喷涂机插 1 件 10 点增产剂（10 次）再取回：件数 **≤ 1**（不会还 10 件）；反复插拔若干次，总件数不增长（`[QSystem][sprayuses] takeback eid=…` 与 `ledger(items=… uses=…)` 一致） |
| 5 | 分拣器在**储物仓**生效 | 储物仓里同时放 1 点与 10 点同类物品、阈值设 8：只抓 10 点那叠，1 点那叠留仓（`bySource=[storage:…]` 计数变化） |
| 6 | 分拣器在**机器产物口**生效 | 制造台产物口 + 阈值 8：低品质产物不被抓走（`bySource=[assembler:…]`），且产物池点数不被本补丁消耗（`pooled` 不异常下降） |
| 7 | 分拣器在**弹射器 / 发射井**生效 | 两者产物同样按阈值过滤（`bySource` 出现 `ejector` / `silo` 计数） |
| 8 | 分拣器在**物流塔 / 研究站 / 电站** | 明确不生效（本补丁记 `skipped`，行为与原版一致） |
| 9 | 回归：传送带分拣器 | 与 v0.19.0 相同：`[QSystem][sorterfilter] picked/blocked` 正常累加，`min=0` 时完全不接管 |
| 10 | 回归：回收机 / 喷涂机 / 八项加成 | `[recycler]` / `[spraycount]` / `[speed]/[charge]/[lab]/[turret]/[powergen]/[powerrange]/[launcher]/[sprayer]` armed 行全部照常 |

### 已知边界

1. `inputs` 与 `sprayuses` 都是**可选**节点：旧档缺节点直接跳过，schema `version` 仍是 1；把新档交给旧 DLL 读时这两个节点被忽略（`inputs` 丢批次点数、`sprayuses` 按 `uses/10` 回退）。
2. 取回件数被夹到 **≤ 投入件数**；无账本时按 `max(1, uses/10)` 保守回退——只会少给、不会多给。
3. `ItemProto.HpMax` 仍为 1（罐内单位 = 次数），"一件能用几次"由 `P6_SprayUses` 补差；直接读 `HpMax` 的第三方 UI 会显示 1 次。
4. 分拣器过滤覆盖 belt / storage / assembler / ejector / silo；`station` / `lab` / `powerGen` 与 `CargoPath.TryPickFuel`（电厂抓燃料）不在范围内。
5. 批次四舍五入只改"每批怎么算"：`BatchInputLevel` 仍取各输入槽的**最小值**，多原料混投时低品质原料照样拉低整批。

### 复现命令

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev-loop.ps1 -Mode up -TimeoutSec 300
# 进档后按需要：
powershell ... -Mode log -Grep "\[QSystem\]\[(sprayuses|sorterfilter|assembler)\]"
powershell ... -Mode key -Key f5        # 看 inputLevel 是否被 inputs 节点恢复
```

---

## v0.19.2 交接与验证（2026-10-31）

### 构建信息

| 项 | 值 |
|---|---|
| MOD 版本 | **0.19.2**（`Plugin.cs` `VERSION = "0.19.2"`；csproj `0.19.2` / `0.19.2.0` / `0.19.2.0`） |
| 运行 DLL | **298,496 字节** |
| SHA256 | `A0BE6EBF3EEF1BB587C7EB6BA0FD32FEEFFDD8F7C98DC58C2B5308298775A8C7` |
| mtime | `2026-10-31 21:46:49` |
| 离线测试 | **88 checks ALL PASS**（v0.19.1 为 94；本版撤回「喷涂次数按品质分级」6 条 checks，94 − 6 = 88） |
| 存档 schema | 仍为 1；玩家 cfg 仍 16 键（无新增键） |

### 启动自检预期行

`DSPQS_DEV=1` 时按顺序在日志里核对：

```
[QSystem] DSP Quality System v0.19.2
[QSystem][sorterfilter] armed: sorter quality threshold is enforced on tick=3/3 belt=2/2 pickFrom=1/1
[QSystem][filtercopy] armed=True reason=patched 11/11 (BuildingParameters copy/paste/apply + Copy/PasteBuildingSetting + blueprint params/save/load + prebuild stage)
[QSystem][storage] NOT ARMED (storageQualityGrids=false; vanilla containers)
[Audit] ---- reflection self-check begin/end ----
```

- `v0.19.2`：版本行必须一致；
- `[sorterfilter] … tick=3/3 belt=2/2 pickFrom=1/1`：全或无，任一计数不足说明锚点没装全、整体已回退原版取货；
- `[filtercopy] armed=True reason=patched 11/11`：11 个挂点全中就绪；出现 `11/11` 之外的数字即视为未启用，蓝图/复制不会带筛选；
- `[storage] NOT ARMED`：**这是本版的预期状态**（储物舱扩容已停用），不是故障；
- 蓝图相关探针（仅 DEV）：`bp-stage queued=N buildings=M`、`bp-stage prebuild id=.. proto=.. kind=.. min=..`、`blueprint block written (save)` / `blueprint block read`、`apply-to`。

### 实机核对项

| # | 核对项 | 判定方式 | 结果 |
|---|---|---|---|
| ① | 分拣器从**储物仓**抓货到传送带，品质点数保留 | 仓库内放带点数货物、分拣器设品质阈值，看落到传送带上的每件点数与角标；DEV 探针 `storage-pick` / `pick-return` 可看映射与回填 | **通过**（用户 2026-10-31 21:24 确认） |
| ② | 蓝图复制出来的**分拣器 / 四向分流器**保留各自的品质筛选设置 | 做蓝图 → 粘贴 → 逐台打开面板看阈值/每口规则是否各自保留；日志应有 `bp-stage queued=4 buildings=20` + 4 条 `bp-stage prebuild … min=6/8/7`（分流为 `kind=1`） | **通过**（用户 2026-10-31 21:24 确认） |
| ③ | 仓库界面**不崩溃、不残留旧货** | 反复取货/放货并开关仓库面板；不再出现 `System.IndexOutOfRangeException at UIStorageGrid._OnUpdate()`，取走后旧货立即消失 | **本版已修**（不再崩溃、不再残留；`NotifyStorageChange()` 生效） |
| ④ | 储物舱**格数不再随品质点数变化** | 给储物仓注入点数后开面板数格子；启动行应为 `[QSystem][storage] NOT ARMED (storageQualityGrids=false; vanilla containers)` | 观察项（按停用设计恒为原版格数） |

### 已通过的记录

- **2026-10-31 21:24 用户实机确认 ①② 通过**：① 分拣器从储物仓抓货到传送带，**品质点数成功保留**（此前 `entityId` 当 `storagePool` 下标的映射错误导致 `grids=0` 丢点数）；② 蓝图复制出的分拣器/四向分流器**各自保留品质筛选设置**（由新增的 `Patches\P6_FilterCopy.cs` 11 个挂点 + 蓝图两段式接管，`QFPB` 魔数块随 `.bp` 文件持久化）。
- **③ 本版已修**：仓库界面不再崩溃，也不再显示已取走的旧货。
- 构建与离线：`298,496` B / `A0BE6EBF3EEF1BB587C7EB6BA0FD32FEEFFDD8F7C98DC58C2B5308298775A8C7` / `2026-10-31 21:46:49` / **88 checks ALL PASS**。

### 本版已知限制

1. 品质阈值过滤的覆盖来源：**支持** 传送带 / 储物仓 / 机器产物口 / 弹射器 / 发射井；**不支持** 物流站（`station`）、研究站（`lab`）、发电机（`powerGen`）——这三类保持原版取货，跳过原因写在启动日志与 `bySource=`。
2. 「储物舱随品质扩容」自 v0.19.2 起**停用**：格数恒为原版值，不再随点数加格；代码保留以兼容旧存档，尾块 `storages` 的溢出数据读档静默忽略（只 `WarnOnce`）。
3. 品质筛选复制依赖 MOD 追加在 `BlueprintBuilding.parameters` 尾部的 `QFPB` 魔数块：**旧版 DLL 或原版客户端**放同一份蓝图仍能正常落成，但筛选设置不生效/丢失。
4. 分拣器阈值比较的是**每件**点数（`inc / stack` 向下取整），不是整栈总点数。
