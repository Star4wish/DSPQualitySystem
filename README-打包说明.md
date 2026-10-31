# DSP Quality System v0.19.2 —— 源码 + 开发文档打包

本包用于在另一台机器上继续开发：包含**完整源码**（含离线测试工程、打包脚本、离线 NuGet 缓存、Unity 引用 DLL）与**开发文档**。

## 1 目录结构

```
DSPQualitySystem/                 ← 源码包根目录（dist\DSPQualitySystem-0.19.2-source.zip）
├─ README-打包说明.md          ← 本文件
├─ 文档/
│  ├─ DSP品质系统-MOD开发文档.md  ← 主开发文档（v0.19.2，设计 + 实现 + 测试 + 已知限制 + 版本历史）
│  ├─ README-工程.md           ← 工程说明（目录职责、构建流水线）
│  └─ 在线调试环境-使用说明.md    ← 实机调试（BepInEx 日志、启动参数、存档路径）
├─ build.ps1                   ← 一键构建（可选 -Deploy 复制到游戏 plugins）
├─ DSPQualitySystem.csproj     ← net472 插件工程（Version 0.19.2）
├─ Plugin.cs / Quality*.cs / QualityData.SprayUses.cs / StorageGrids.cs / QualityData.Recycler.cs …  ← 插件源码（根目录）
├─ Patches/                    ← 39 个 .cs 文件 / 42 个 Harmony 补丁类（20 特性类 + 22 手工类；v0.19.0 新增 P6_SprayCount.cs / P7_Recycler.cs，v0.19.1 新增 P6_SprayUses.cs 并重写 P5_SorterFilter.cs，v0.19.2 新增 P6_FilterCopy.cs 并再次重写 P5_SorterFilter.cs 的储物仓分支与缓存门控）
├─ Save/                       ← 存档尾块编解码、legacy 迁移
├─ Diag/                       ← 启动自检（StartupAudit）
├─ tests/                      ← 离线测试（net10.0，88 checks，无需游戏）
├─ tools/                      ← 打包脚本（package.ps1 / pack-r2modman.ps1 / pack-r2modman.py）+ tools\r2\（R2 元数据/图标）+ nuget-local（离线包）+ unity-refs（Unity 引用）
├─ README.md / DEVLOG.md / TESTLOG.md  ← 玩家说明 / 开发日志 / 实机测试记录（篇幅较大）
└─ nuget.config                ← 指向 tools\nuget-local 的离线还原配置
```

## 2 版本与校验值（本次交付）

| 项 | 值 |
|---|---|
| MOD 版本 | v0.19.2（`DSPQualitySystem.csproj` `<Version>0.19.2</Version>`） |
| 插件 DLL | **`298,496` B** / SHA256 `A0BE6EBF3EEF1BB587C7EB6BA0FD32FEEFFDD8F7C98DC58C2B5308298775A8C7` / `2026-10-31 21:46:49` 构建（三个值已按 v0.19.2 实际构建回填；本仓 `.md` 中不存在 `@@DLL_*@@` 占位符） |
| 离线测试 | **88 checks**，`ALL PASS (88 checks)` |
| 启动自检 | `patchedMethods=18`（特性类计数口径）；补丁类总数与逐条锚点以开发文档 §4.2 各表为准（v0.19.2 统计：39 个补丁 .cs / 42 个补丁类 / **85 次方法级注册** / **78 个独立方法**，其中 v0.19.2 新增 `P6_FilterCopy` 1 类 11 锚点） |
| 手动发布包 | `dist\DSPQualitySystem-0.19.2.zip`（体积与 SHA256 以打包后 `Get-FileHash` 为准） |
| R2 发布包 | `dist\DSPQualitySystem-0.19.2-r2modman.zip`（体积与 SHA256 以打包后 `Get-FileHash` 为准） |
| 源码包 | `dist\DSPQualitySystem-0.19.2-source.zip`（根目录 `DSPQualitySystem/`；本文件就在包内，因此体积与 SHA256 同样以打包后为准，不在表内自引用） |

v0.19.2 关键变更（详见开发文档 §2.6/§2.8/§3.3/§4.2.6/§4.2.9/§5/§6/§7.8/§7.9/§9）：

1. **新增 `Patches\P6_FilterCopy.cs`（11 个挂点）：品质筛选设置随复制/粘贴与蓝图迁移**——四向分流器的每口规则与分拣器的品质阈值，在 Shift 复制、Ctrl+C / Ctrl+V、蓝图复制粘贴时各自保留。**建筑设置**：`BuildingParameters.CopyFromFactoryObject`、`BuildingParameters.PasteToFactoryObject`、`PlanetFactory.CopyBuildingSetting`、`PlanetFactory.PasteBuildingSetting`。**蓝图两段式**：`BuildTool_BlueprintPaste.CreatePrebuilds()` prefix 遍历 `blueprint.buildings`，用 `TryReadBlueprintBlock(building.parameters)` 解码入队（日志 `bp-stage queued=N buildings=M`）→ `PlanetFactory.AddPrebuildDataWithComponents` postfix 按 protoId 绑到 prebuildId（日志 `bp-stage prebuild id=.. proto=.. kind=.. min=..`）→ `PlanetFactory.AddEntityDataWithComponents` postfix 写到落地实体。**持久化**：`BuildingParameters.ToParamsArray` postfix / `FromParamsArray` prefix / `GenerateBlueprintData` prefix+postfix 把 **9 个 int 的魔数块**塞进 `BlueprintBuilding.parameters` 尾部随 `.bp` 文件走——magic `0x51465042`（字母 QFPB）、version 1、kind 1=四向分流器 / 2=分拣器、protoId、rule0..3、min。启动日志 `[QSystem][filtercopy] armed=True reason=patched 11/11 (BuildingParameters copy/paste/apply + Copy/PasteBuildingSetting + blueprint params/save/load + prebuild stage)`，11 个锚点全或无。
2. **分拣器品质阈值过滤修复**（`Patches\P5_SorterFilter.cs`）——真根因是储物仓分支把「实体 id」当成 `storagePool` 下标用了（原版要先 `entityPool[entityId].storageId` 映射、再取 `storagePool[storageId]`），命中空组件得到 `grids=0`，低品质货被当成 0 点放行（用户现象：仓库→传送带丢点数）。同时删掉 `lastEmptyItem` 缓存门控（阈值或物品过滤一改立即生效，「只设品质阈值、不选物品」也能生效），并补 `NotifyStorageChange()` 让仓库界面立刻刷新（此前取走货后界面残留旧货、要重开才消失）。覆盖来源：传送带 / 储物仓 / 机器产物口 / 弹射器 / 发射井；物流站、研究站、发电机不支持。启动日志 `[QSystem][sorterfilter] armed: sorter quality threshold is enforced on tick=3/3 belt=2/2 pickFrom=1/1`。
3. **关闭「储物舱随品质扩容」**（`QualityConfig.StorageQualityGridsValue = false`）——功能停用，行为等同删除（代码与存档兼容逻辑保留，以便读旧档），修掉打开仓库界面时的 `System.IndexOutOfRangeException at UIStorageGrid._OnUpdate()` 崩溃。启动日志 `[QSystem][storage] NOT ARMED (storageQualityGrids=false; vanilla containers)`；旧存档 trailer 里的 overflow 数据读档时静默忽略（只 WarnOnce）。
4. **新增 DEV 诊断探针**（仅在 `DSPQS_DEV=1` 下输出，正常游戏静默）：`storage-probe`、`storage-pick`、`pick-return`、`inserter-hold`、`stack-insert2`、`stack-insert2b`、`cargo-insert`、`insert-into`、`bp-stage`、`blueprint block written (save)` / `blueprint block read`。

实机验证（用户 2026-10-31 21:24 确认）：① 分拣器从储物仓抓货到传送带，**品质点数成功保留**；② 蓝图复制出来的分拣器 / 四向分流器**成功保存各自的品质筛选设置**（证据：`bp-stage queued=4 buildings=20` + 4 条 `bp-stage prebuild … min=6/8/7` + 分流 `kind=1`）；③ 仓库界面不再崩溃、也不再显示已取走的旧货。

#### 附：v0.19.1 四项（历史记录）

1. **读档丢"本批原料点数"修复**（`Save\QualitySave.cs` + `Patches\P0_Assembler.cs`）：制造台的 `inputLevel`（本批原料每件平均点数）原本只在内存，读档后第一批产物按 0 点原料结算（探针 `inputLevel=-1`、日志 `in=0 pooled=2`）。现在写进存档尾块的行星节点 `"inputs":[{"e":entityId,"l":level}]`，读档走 `QualityData.SetInputLevel` 恢复（修后 `inputLevel=6`、`in=6 pooled=8`）；旧档缺该节点直接跳过，**schema 版本仍是 1**。
2. **批次原料点数改为四舍五入**（`QualityMath.BatchInputLevel(int incServed, int served, int maxLevel)`）：由原版整除截断改为 half-up——`(incServed + served/2) / served` 再夹到 `[0, maxLevel]`，`P0_Assembler` 取各输入槽的最小值。用户报的"10 点制造台 + 6 点原料出 7 点"根因即一批 5 件共 29 点：`29/5 = 5`（截断）→ 7 点产物；改后 `5.8 → 6` → 8 点产物。DEV 探针 `[QSystem][assembler][batch] eid=… lvl=… s0=<served>/<incServed>/<requireCounts>`。
3. **喷涂次数按品质点数分级**（新增 `Patches\P6_SprayUses.cs` + `QualityData.SprayUses.cs`）：一件增产剂的可用次数 = 0–2 点→1、3–4 点→2、5–6 点→4、7–8 点→5、9 点→7、10 点→10（`SprayUsesMath.UsesForPoints`）。保留 `P6_SprayCount` 把 `ItemProto.HpMax` 固定为 1（罐内单位 = 次数），再用"插前快照 + 插后补差"让每件净增 = 表值，6 锚点全或无（`PlanetFactory.EntityFastFillIn` / `Player.TakeItemFromPlayer` / `SpraycoaterComponent.InternalUpdate` / `CargoPath.TryPickItem` / `CargoTraffic.TakeBackItems_Spraycoater` / `CargoTraffic.ClearItems_Spraycoater`，`_armed = ok >= 6`）。**取回**改为 prefix 接管 + 每台喷涂机账本 `"sprayuses":[{"e","i","u"}]`：件数 = `ceil(usesIn / itemsIn)` 且**夹到 ≤ 投入件数**，旧档无账本按 `uses/10` 保守回退。Mk.I/II/III 每喷加点数（`ItemProto.Ability` = 1/2/4）不变。
4. **分拣器品质阈值过滤重写**（`Patches\P5_SorterFilter.cs`）：v0.18.0 只 prefix `CargoTraffic.TryPickItem`，因此**只覆盖传送带**，储物仓与机器产物口的低品质货照样被取。现在两级挂钩——① `CargoPath.TryPickItem` 两个带 filter 的重载（`plain` + `needs`）；② `PlanetFactory.PickFrom(uint,int,int,int[],out byte,out byte)` 的 `Priority.High` prefix，按来源覆盖 `belt`/`storage`/`assembler`/`ejector`/`silo`（`storage` 走只读网格扫描镜像 `TakeTailItems`；`assembler` 只"预测下一件点数不足就拦"、不消耗产物池，实际扣点放行给 `P0_PickFrom`；`ejector`/`silo` 用 `bulletInc`/`bulletCount` 预检），显式跳过 `station`/`lab`/`powerGen` 并写进 `bySource=`。武装条件全或无：tick 3/3 + belt 2/2 + pickFrom 1/1。

#### 附：v0.19.0 两项（历史记录）

1. **三种增产剂可用喷涂次数 = 1**（`Patches\P6_SprayCount.cs`）：改写 `ItemProto.HpMax`（原版插入 `PlanetFactory.cs:12272-12286`/`12330-12339`、取出 `CargoTraffic.cs:3716`、UI tooltip 都活读该字段），`ItemProto.Preload(int)` postfix 落地为 1；`SpraycoaterComponent.Import` postfix 把旧档"喷涂次数"按 `ceil(次数/原版次数)` 折算成件数并清 `extraIncCount`；`ABN_ProtoData.CheckProto` prefix+postfix 在校验期间临时还原 12/24/60，避免读档被判"数据异常"。
2. **品质回收机**（`Patches\P7_Recycler.cs` + `QualityData.Recycler.cs`）：复用原版喷涂机（2313），不新增建筑/配方/UI。回收通道 = 喷涂机的**催化剂（增产剂）进料带**（原版放 1141/1142/1143 的那一侧）：喷涂机通电（`powerRatio > 0.1`）、进料带上出现**非增产剂**且单件点数 `Cargo.inc / Cargo.stack ≥ 1` 的 cargo 时（即任意带点数的货物，含 Mk.I 的 1 点/件），整叠被**消耗销毁**（不返还、下游不产出物品），点数记入该机账号，**每 4 点 = 1 次喷涂次数**灌进原版喷槽（`pointsPerSpray=4`、`incItemId=1143`、`incSprayTimes=max(1,HpMax)=1`、容量 600 次）；未喷涂的 0 点/件货物 → 整叠原样放行。阈值是 `P7_Recycler.cs` 的内部开关 `MinPerItemValue`（默认取 `QualityRecyclerMath.DefaultMinPerItem = 1`、**不是 cfg 键**）：兑换本身亏损（4 点换 1 次喷涂次数、1 次最多值 3 点），不需要靠阈值防套利；改成 3 即回到「只收 Mk.III 喷过的」。满槽停收（probe `mode=tankFull`）。注入为 `SpraycoaterComponent.InternalUpdate` **postfix**（struct/class 双签名，`Refresh`→改→`Persist`）；每机账号（0..600*4 点）进存档可选节点 `"recyclers":[{"e":…,"p":点数,"c":容量}]`（旧档缺 key 跳过、早期 `"o"` 字段忽略），拆机 1200 tick 后清理。

继承自 v0.18.0（不再展开）：发电功率重建部署、电力塔类供电范围（第八项加成）、升品概率改查曲线、矿机确定性（每 2 点 +1）、未付款预建（白色虚影）按实际消耗材料点数落成。

#### 附：v0.18.0 五项（历史记录）

1. **发电功率**：五个 `PowerGeneratorComponent.EnergyCap_*`（风/光/地热/火力/射线接收站）后置抬 `__result` 并同步写 `capacityCurrentTick`。用户实机反馈「没变化」的根因是**跑的是 v0.11.10 DLL**（157,184 B，无 v0.12.0 起的发电补丁），本版重新构建部署即可；同时加固自检（绑定失败会在启动日志报 `[powergen]` 明细）。
2. **供电范围（新增，第八项加成）**：`PrefabDesc.isPowerNode` 的电力塔类建筑（感应塔/无线输电塔/配电站）按 `prefab × 曲线倍率` **幂等**重算 `PowerSystem.nodePool[].connectDistance` 与 `.coverRadius`（10 点 = 2.00×）。触发点：构建落成（`AddEntityData_Bind` → `P5_PowerRange.ApplyForEntity`）与读档/每星球首帧一次性扫描（`PowerSystem.GameTick` 前缀 `EnsurePlanet`）。字段在存档里，所以必须按 `prefab` 基准重算而非在当前值上累乘。新增内部常量 `PowerRangeBonus`（恒开、无 cfg 键）。
3. **升品概率**：制造台升品概率改为复用速度/发电那张每点曲线（`QualityMath.UpgradeCurveChance`：1..10 点 = 2.5/5/7.5/10/15/20/30/40/60/100 %），掷骰结构不变（0–6 点单掷、7–9 点双掷、10 点必 +2 不掷骰、分馏塔恒单掷）。旧线性「点数 × 1.5%、上限 60%」保留在 `QualityMath.UpgradeChance`，仅离线 A/B 用。
4. **矿机确定性**：`minerChancePerPoint` 由 2 改为 **100**，配合既有多判代码（判定次数 `floor(点数/2)`、`chance >= 100f` 必中分支）得到「每 2 点固定 +1 增产点」：2→+1、4→+2、10→+5（1 点仍 0）。大型采矿机同规则（站点 `storage[0].inc` 走 `StationIncAfterTransfer`）。
5. **未付款预建（白色虚影）补付**：新增 `P1_DeferredPay`——`ConstructionModuleComponent.PlaceItems` 开/关补付窗口，`StorageComponent.TakeTailItems` 后置记录实际被扣「物品 + 件数 + 总点数」，`PlanetFactory.NotifyPrebuildChange(prebuildId, 3)` 且 `prebuildPool[id].itemRequired == 0` 时把 `DeferredPayPerItem(points, items)` 写入 `QualityData.SetPrebuildInc`，落成时由既有 `AddEntityData_Bind` 绑定到建筑。**只观察不劫持**：原版要求一次全额付清（`Assert.True(need == itemRequired)`），因此 `P4_ConsumePriority.TakeBestGridPrefix` 在 `PayWindowActive` 期间回退原版、不做换格/拆点。

## 3 新机器上如何构建

前置：Windows + .NET SDK（构建 `net472` 需要 `Microsoft.NETFramework.ReferenceAssemblies`，已在 `tools\nuget-local`；若本机 `dotnet --list-sdks` 有 6.0 及以上即可）。

```powershell
# 方式 A：本机装了戴森球（推荐，用游戏内 Assembly-CSharp 与 UnityEngine）
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Deploy `
    # 游戏目录未在默认 Steam 路径时，先设置：
    # $env:DSP_GAME_DIR = 'D:\Games\Dyson Sphere Program'

# 方式 B：没装游戏，用包内 tools\unity-refs 的 Unity 引用（编不过 Assembly-CSharp 的部分会失败，
# 因为 GameLibs 已含 Assembly-CSharp.dll，因此 B 方式实际可行）
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

成功标志：控制台打印 `BUILD OK`（`-Deploy` 成功再打印 `DEPLOY OK -> …` 与 `DEPLOY SHA256 …`）。
脚本行为：优先使用 `$env:DSP_TOOLS_DIR\dspenv.ps1`（本工作区便携 SDK）；没有则用 PATH 上的 `dotnet`；游戏目录按 `-p:GameDir` > `$env:DSP_GAME_DIR` > 默认 Steam 路径解析；找不到游戏时退回 `tools\unity-refs`。

## 4 如何跑离线测试

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\run-tests.ps1
```

注意：测试工程目标框架是 **net10.0**（与插件无关，纯逻辑测试）。若新机器只有 .NET 8/9 SDK，请把 `tests\OfflineTests\OfflineTests.csproj` 的 `<TargetFramework>` 改成 `net8.0`（代码只用基础 BCL，可直接降级编译）。

## 5 如何打包发布件

```powershell
# 手动安装包（dist\DSPQualitySystem-<version>.zip，含 DLL + README）
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\package.ps1 -SkipDeploy

# R2 / Thunderstore 包（读 csproj 版本，内置 manifest 校验）——本机无可用 python，用 PowerShell 版：
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\pack-r2modman.ps1
# 有 python 的机器仍可用原版：python .\tools\pack-r2modman.py
```

`tools\package.ps1` 不带 `-SkipDeploy` 时会顺带部署到游戏；只出包时用 `-SkipDeploy`。R2 元数据在 `tools\r2\`（`manifest.template.json`、`icon.png` 256×256、`README.md`、`CHANGELOG.md`）。

> 维护提示：`tools\pack-r2modman.ps1` 必须保持**纯 ASCII**。Windows PowerShell 5.1 会把无 BOM 的 `.ps1` 按 ANSI 解码，脚本里的中文注释会破坏解析（表现为 `You cannot call a method on a null-valued expression` 指向 `$manifest.Replace(...)`）。另外本机 ExecutionPolicy 受限，`-NoProfile -ExecutionPolicy Bypass -File` 是必需的调用形式。

## 6 开发文档怎么读

`文档\DSP品质系统-MOD开发文档.md`（905 行）结构：

- §1 概述/技术栈/设计约束/代码组织
- §2 功能清单（点数来源与传递、未付款预建补付、升品与矿机判定、八类加成、隔离、仓库扩容（**v0.19.2 停用**）、分流器过滤、分拣器阈值、**品质筛选随 Shift 复制与蓝图迁移**、角标、光环、**喷涂次数 = 1、品质回收机**）
- §3 配置（16 个玩家键、内置常量、`DSPQS_DEV` 调试开关）
- §4 技术实现（载入顺序、**Harmony 补丁总表**（按域分表，含 v0.19.0 新增 `P6_SprayCount` 4 处、`P7_Recycler` 1 处，v0.19.1 新增 `P6_SprayUses` 6 锚点与 `P5_SorterFilter` 的 `CargoPath.TryPickItem` ×2 / `PlanetFactory.PickFrom` ×1，v0.19.2 新增 `P6_FilterCopy` 11 锚点）、品质曲线、线程约束、存档尾块格式、JSON 结构（含可选 `recyclers` 节点，v0.19.1 增 `inputs` / `sprayuses`）、legacy 迁移、原版 API 约束）
- §5 数据结构（每星球状态、容量边界、内存/持久化边界、存档层职责）
- §6 测试与验证（离线工程 88 checks、启动自检、实机要点、日志关键字）
- §7 兼容性与已知限制（存档卸载、视觉、隔离、优先消耗、拆除、矿机、充电、过滤、扩容、运行时防护、供电范围、补付路径、**品质回收机与喷涂次数**）
- §8 构建与发布（`build.ps1`、`run-tests.ps1`、`package.ps1`、校验、R2）
- §9 版本历史（v0.11.10 → v0.19.2）

文中 `asm:NNNNN` 指反编译参考 `asm_full.cs` 的行号（该文件体积大，未随包附带；需要时用 ILSpy 对 `Assembly-CSharp.dll` 重新导出即可，`Patches\` 内每个补丁的目标方法名与参数签名都写在类注释里）。

## 7 已知限制（维护时最容易被踩到）

- 分流器：有规则即接管，**规则口满 = 货物留在输入皮带**（背压），可能堵一条输入；顶部压储物（`topId != 0`）的分流器不实现原版"货入顶部储物"分支。
- 分流器/分拣器规则只存 MOD 侧表（存档尾块 `splittersV2` / `sorters`），不写原版 `SplitterComponent`/`InserterComponent.filter`（分拣器的物品 id 仍走原版字段）。
- 分拣器阈值（v0.19.1 重写）：传送带覆盖 `CargoPath.TryPickItem` 两个带 filter 的重载；`PlanetFactory.PickFrom` 覆盖 `storage` / `assembler` / `ejector` / `silo` 四类来源（`assembler` 只预测、不消耗产物池点数，扣点仍由 `P0_PickFrom` 负责）；`station` / `lab` / `powerGen` 明确跳过（塔内 `TakeItem` 轮转与 keepMode 无法安全复现、研究站产物无品质池、燃料无点数）；电厂抓燃料 `CargoPath.TryPickFuel` 不受影响。v0.18.0 只拦 `CargoTraffic.TryPickItem`，所以那时**只有传送带**受控。**v0.19.2 修正**：储物仓分支此前把实体 id 误当 `storagePool` 下标（缺 `entityPool[entityId].storageId` 映射），实际读到 `grids=0`、点数被当 0 点放行；本版修好映射、移除 `lastEmptyItem` 缓存门控、补 `NotifyStorageChange()` 让仓库界面即时刷新。
- 存档：品质数据是 `.dsv` 尾部自定义块（magic `DSPQSQ1\0`，尾部 4 MB 内倒序扫描，可与其它也写尾块的 Mod 共存）。
- 供电范围：`connectDistance`/`coverRadius` 写进原版持久字段 `PowerSystem.nodePool[]`，MOD 卸载后仍保留放大值；重新装回 MOD 会按 `prefab × 当前点数` 幂等重算（不会叠加），拆除重建也按新点数重算。
- 未付款预建补付：原版一次全额付清，该路径不做「最高品质优先消耗」的换格/拆点，只继承**实际被扣那批物品**的点数；基地分支可能分批付，累加器跨帧保留到 `itemRequired == 0`。
- 实机调试：BepInEx 日志在 `%USERPROFILE%\AppData\LocalLow\Youthcat Studio\Dyson Sphere Program\Player.log`，存档在 **`%USERPROFILE%\Documents\Dyson Sphere Program\Save\`**（不是 LocalLow）。
- 品质回收机（v0.19.0）：**喷涂机必须通电**（`powerRatio > 0.1`）才回收；回收通道是催化剂进料带，只收单件点数 ≥ 1 的非增产剂物品（任意带点数的货物，含 Mk.I 的 1 点/件），未喷涂的 0 点货物整叠原样放行、不消耗；阈值是 `Patches\P7_Recycler.cs` 的内部开关 `MinPerItemValue`（默认 1、**不是 cfg 键**，改成 3 = 只收 Mk.III 喷过的）；产物是原版喷槽里的喷涂次数（4 点/次）、**不是产出物品**，喷槽满 600 次时停止回收。**行为级（放机跑带）已由用户 2026-10-31 实机验证通过**，自检与离线单测亦通过。每机账号（`{"e","p","c"}`）写在存档 `recyclers` 可选节点，旧版 DLL 读新档会忽略该节点（不崩档但丢点数）。
- 喷涂次数 = 1（v0.19.0）：全局改写 `ItemProto.HpMax`，任何复用 1141/1142/1143 `ItemProto` 的第三方内容都会变成 1 次/件；与其它"增产剂可用次数"类 MOD 共存时后加载者生效。
- 喷涂次数分级（v0.19.1）：`HpMax` 仍是 1，实际可用次数由 `P6_SprayUses` 按件补差到分级表值（0–2→1、3–4→2、5–6→4、7–8→5、9→7、10→10），因此直接读 `HpMax` 的第三方 UI 会显示"1 次"；取回件数夹到 ≤ 投入件数、旧档无 `sprayuses` 账本按 `uses/10` 回退（只会少给）；账本随尾块保存、卸载后丢失（喷槽里已有的次数仍属原版存档字段）。该组 6 条离线 checks 于 **v0.19.2 撤回**，故离线总数 94 → 88。
- 仓库扩容（v0.19.2 停用）：`QualityConfig.StorageQualityGridsValue` 已置 `false`，储物仓恢复原版格数、不再随品质点数加格；旧存档 trailer 里的 overflow 数据读档静默忽略（只 WarnOnce）、不再写回。停用原因是它会让打开仓库界面时抛 `System.IndexOutOfRangeException at UIStorageGrid._OnUpdate()` 崩溃。
- 品质筛选复制（v0.19.2）：筛选设置存在 `BlueprintBuilding.parameters` 尾部的 `QFPB` 魔数块里，**旧版 DLL / 原版客户端读到的蓝图仍可正常放置**（尾部字节被忽略），只是筛选设置丢失、退回默认；分流器规则与分拣器阈值都只存在 MOD 侧表（存档尾块 `splittersV2` / `sorters`），因此跨版本回退不丢建筑本身。
