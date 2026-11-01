# 戴森球品质系统 Mod —— 设计文档

| 项目 | 内容 |
|---|---|
| 适用版本 | **v0.19.2** |
| 代码基线 | `C:\XCM\DSP MOD\DSPQualitySystem\`（`Plugin.cs` 中 `VERSION = "0.19.2"`；`DSPQualitySystem.csproj` 为 `0.19.2` / `0.19.2.0` / `0.19.2.0`） |
| 游戏 / 运行时 | Dyson Sphere Program **0.10.35**；BepInEx **5.4.x**（`BepInEx.Core 5.4.21`）；.NET Framework **4.7.2** |
| 插件标识 | GUID `dsp.qualitysystem`，配置段 `[quality]`，配置文件 `BepInEx\config\dsp.qualitysystem.cfg` |
| 交付形态 | 单个 `DSPQualitySystem.dll`（v0.19.2：`298,496` 字节，SHA256 `A0BE6EBF3EEF1BB587C7EB6BA0FD32FEEFFDD8F7C98DC58C2B5308298775A8C7`，`2026-10-31 21:46:49` 构建） |
| 文档范围 | 只描述 v0.19.2 的现状；不含被移除功能、历史计划与过程记录 |

> **核对约定**：文中 `asm:NNNNN` 指反编译参考 `C:\WorkWork\DSP MOD\_analysis\quality-system\asm_full.cs` 的行号；`文件.cs:NN` 指当前源码行号。凡本文与代码不一致之处，**以代码为准**。

---

## 1 项目概述

### 1.1 这是什么

戴森球品质系统（DSP Quality System）把原版"增产剂点数（inc）"重构成一套 **0–10 级品质点数**体系：

- 喷涂机、建筑自身品质、矿机（含大型采矿机站点）产出、拆除返还四条路径产生品质点数；
- 点数沿原版 inc 通道在原料 → 产物 → 建筑 → 物品之间传递；
- 建筑点数再驱动八类性能加成（速度、充电、研究、射速、发电、发射、喷涂、供电范围）；
- 四向分流器输出口与分拣器可按"物品 + 最低品质点数"过滤，把高品质货物分流到指定线路（两个入口都复用原版物品选取器）；
- 视觉层用物品角标与 10 点建筑光环把点数显示出来。

整套设计与原版 inc 语义完全兼容：`Cargo.inc` 仍是"整叠总点数"的 `byte`，因此堆叠上限、传送带规则、存档结构都不需要扩展。

### 1.2 技术栈与依赖

| 依赖 | 版本 / 说明 |
|---|---|
| BepInEx | 5.4.x，`BepInEx.Core 5.4.21`（`PrivateAssets=all`） |
| GameLibs | `DysonSphereProgram.GameLibs 0.10.35.29088-r.0`（`ExcludeAssets=runtime`） |
| 运行时框架 | .NET Framework 4.7.2（`net472`），`Microsoft.NETFramework.ReferenceAssemblies 1.0.2` |
| Unity 引用 | 优先取游戏目录 `DSPGAME_Data\Managed\UnityEngine*.dll`，缺失时回退 `tools\unity-refs` |
| 不需要 | DSPModSave、LDBTool（存档尾块自己实现） |

### 1.3 设计约束

1. **关闭即原版**：每个可关闭功能（八个加成、两个隔离、角标、光环）关闭时不注册补丁、不创建 UI/渲染对象，原版路径逐字节不变。恒开功能（分流器输出口过滤、分拣器品质阈值、供电范围、品质筛选复制）的开关是内置常量 `true`，规则为 0 条/阈值 0 时同样与原版逐字节一致，见第 3 章常量表（**仓库品质扩容自 v0.19.2 起已改为 `false` 停用**，见 §2.6）。
2. **不破坏原版存档**：品质数据只追加在 `.dsv` 尾部，原版读取器读到自身数据末尾即停止；容器扩容只在内存中发生，导出前把结构字段收回原版值（该功能 v0.19.2 起停用，收回逻辑作为旧档兼容保留）。
3. **崩溃隔离**：所有 Harmony 补丁、每帧逻辑、存档读写都在 `try/catch` 内，失败走原版路径并 `WarnOnce` 只记一条日志。
4. **纯视觉不写档**：物品角标与 10 点光环不写存档、不改物品数据、不改模型材质。

### 1.4 代码组织

| 文件 / 目录 | 职责 |
|---|---|
| `Plugin.cs` | BepInEx 入口：配置初始化、改原版表、注册补丁、每帧轮询与热键 |
| `QualityConfig.cs` | 16 个玩家配置键 + 全部内置常量 + 钳制属性 + 开发者开关 |
| `QualityMath*.cs` | 纯计算：池、掷骰、曲线、隔离、拆解、储物仓、分流器/分拣器过滤、角标、光环、蓝图与立即建造辅助 |
| `QualityRules.cs` | 规则编排：升品判定、矿机判定、容器隔离判定、随机数 |
| `QualityData*.cs` | 运行时状态：每星球并行数组、预建造点搬迁、矿机与大型采矿机钩子、品质过滤侧表（`QualityData.QualityFilters.cs`） |
| `QualityEnv.cs` | 当前 `FactorySystem`/星球作用域（供无 planet 引用的组件使用） |
| `StorageGrids.cs` | 容器额外格的运行时扩容、保存收回、溢出搬运、缩回 |
| `BuildQualityStash.cs` | 建造时"被消耗物品点数"的帧内暂存 |
| `Save\*.cs` | `JsonLite`（无依赖 JSON）、`SaveGuard`（校验）、`QualityBlobCodec`（尾块）、`QualitySave`（装配/迁移） |
| `Diag\*.cs` | `StartupAudit` 启动自检、`Probes`/`ProbesMiner` 开发者探针 |
| `Patches\P*.cs` | Harmony 补丁类 + 各功能的手工注册入口 |
| `tests\OfflineTests\` | 无游戏依赖的离线测试工程（只链接纯逻辑代码） |

---

## 2 当前功能清单

### 2.1 品质点数的来源

| 来源 | 规则 | 上限 |
|---|---|---|
| 喷涂机 | 喷涂增产剂时把 `Ability` 写入物品点数：Mk.I/II/III = 1/2/4 点；`num5 * incAbility > cargo.inc` 才覆盖，因此只升不降 | 每件 ≤ `Ability`（最高 4 点） |
| 建筑施工升品 | 每个生产周期开始时按本栋点数判定一次，成功则产物再 +1 点（10 点必 +2，见 2.3） | 10 点 |
| 矿机 | 按本栋点数多段判定，每次成功 +1 点（见 2.3） | 10 点 |
| 大型采矿机（物品 2316，站点 + 采集器） | 原版 `StationComponent.UpdateVeinCollection` 把矿搬进站点 `storage[0].count` 时只搬数量、不写 inc；MOD 按相邻两次 `count` 差量掷矿机骰，把点数补进该格 | 10 点 |
| 拆除返还 | 拆除建筑时把它自己的点数写回返还的物品堆（常开） | 10 点 |

**要点**：品质点数以"整叠总点数"存在 `Cargo.inc`（`byte`，上限 255）。因此"每件点数"= `inc / stack`（向下取整），且单件上限 10 由 `Cargo.kIncLevelMax` 决定。

### 2.2 传递与消耗

- **产物继承**：一个生产批次只算一次输入品质，取**所有输入槽每件点数的平均值**——原版做法是 `min(split_inc_level(served[i], incServed[i], requireCounts[i]))`（`asm:25538` 起 `AssemblerComponent.InternalUpdate`）。
- **批次原料点数（`inputLevel`，v0.19.1）**：上面这个"每批原料点数"在 MOD 里另有内存副本（`QualityData.SetInputLevel`），v0.19.1 起随存档持久化——写进行星节点的 `"inputs": [ { "e": entityId, "l": level } ]`（只写 `level >= 0` 的实体），读档时按 `{"e","l"}` 逐条 `SetInputLevel` 恢复；旧存档没有该节点则整段跳过（**schema 版本仍是 1**，向后兼容）。修复前该值只在内存里，读档后首批产物按 0 点原料计算。
- **取整改为四舍五入（v0.19.1）**：每批原料点数由原版的整除截断改为 `QualityMath.BatchInputLevel(incServed, served, maxLevel)`——`(incServed + served/2) / served`（half-up）再钳到 `[0, maxLevel]`，`served <= 0` 或负投入记 0。用户报告案例：10 点制造台、6 点原料，一批 5 件共 29 点总点数（料带混入少量低点数料），原来 `29/5 = 5`（截断）出 7 点产物，现在 `5.8 → 6` 出 8 点。
- **与产物池的关系（v0.19.1）**：`inputLevel` 只是"本批原料点数"的输入侧副本；产物点数仍由产物池（`pools` 节点，`{"e","pi","pc"}`）在批次结束时提交，两者互不覆盖——读档丢的从来只是输入侧的副本，产物池一直是持久化的。
- **建筑落成点数**：建造时取被消耗材料的每件点数平均值；具体取整与聚合以 `QualityMath` 的平均点数实现为准。
- **优先消耗（常开）**：手持建造、蓝图粘贴与背包路径建造（如大型采矿机 2316，原版直接从背包尾部取料）都会优先消耗玩家持有的最高品质同类建筑物品——手上同种物品优先于背包，背包内按每件点数最高者优先、同点数取低索引格、**严格更高才换手**；落成点数 = 实际被消耗那件物品的点数，通过**消费收据**（`P4_ConsumePriority.TryTakeReceipt`，`P4_ConsumePriority.Sources.cs`）交给建造绑定。
- **未付款预建（v0.18.0 起）**：材料不足时按住建造键会先立起半透明虚影、由建造模块（`ConstructionModuleComponent.PlaceItems`）在材料补齐后**一次性全额补付**再落成；原版把这次扣料的 `inc` 丢进丢弃的 `out` 参数。`P1_DeferredPay` 在 `PlaceItems` 期间打开**补付窗口**：`StorageComponent.TakeTailItems` 后置读取实际被扣的物品/件数/总点数，`PlanetFactory.NotifyPrebuildChange(prebuildId, 3)` 前缀在该预建 `itemRequired == 0`（付款完成）时把 `总点数 / 件数` 写进 `prebuildInc`，于是白色虚影落成时同样继承**实际被消耗建筑物品的点数**（只观察不劫持原版付款，见 4.2.2）。
- **拆除返还（常开）**：先取建筑点数再返还物品；一次拆多栋时点数作用于整栈。
- **增产模式额外产物**：装配机按"产物净增量"记账，净增量 = `produced[]` 差量 + 本 tick 被取走的件数（`QualityMath.ProducedDelta`，`QualityMath.cs:182`）。原版增产模式每 10 周期多产 1 件，这件额外的产量长期被同期取货抵消，曾因此记为 0 件、拿不到点数；加上回补后额外产物同样参与品质判定。
- **Shift + 左键「立即建造/复制建造」**：该路径不走 `Player.UseHandItems`，而是 `PlayerAction_Inspect.GameTick` 的立即建造块内部扣料（原版把 inc 丢进丢弃的 `out` 参数）。v0.17.0 起 `InstantBuildCapture` 在该 tick 的前缀里打开**瞬时消费窗口**（`QualityMath.ConsumeWindowActive`，`QualityMath.cs:246`），让同一套"优先消耗最高品质"逻辑先取料并留下收据；`PlanetFactory.BuildFinally` 前缀按 `prebuild.protoId` 取走收据写到该预建。取不到收据时退回同帧 `TakeTailItems` 记录，再对不上就退化为原版 0 点。

### 2.3 升品与矿机判定

**建筑升品**（制造台、矿机等产物升品；分馏塔单列）：

| 条件 | 结果 |
|---|---|
| 基础概率（v0.18.0 起） | 与速度/充能/发电共用同一张每点曲线（`QualityMath.UpgradeCurveChance`）：1..10 点 = 2.5 / 5 / 7.5 / 10 / 15 / 20 / 30 / 40 / 60 / 100 % |
| 点数 > 6 | 额外再判一次（与基础同概率），命中再 +1 |
| 点数 = 10 | 必定 +2（跳过掷骰） |
| 分馏塔 | 只做一次基础判定，不享受"额外一次"与"必 +2" |

> v0.18.0 之前的线性公式 `点数 × 1.5%`（上限 60%）仍保留在 `QualityMath.UpgradeChance` 中，仅供离线测试做 A、B 对照，运行时不再使用；曲线概率在 1 点即 2.5%（旧公式 1.5%），4 点 10%（旧 6%），10 点 100%（旧 60%）。

**矿机品质**：

- 判定次数 `rolls = floor(点数 / 2)`（`QualityMath.MinerRollsOf`，`QualityMath.cs:110`）；
- 每次判定**必定成功**（v0.18.0 起 `minerChancePerPoint` = 100，`chance >= 100f` 走既有的必中分支、不掷骰，因此复用原多判代码即可）；
- 每成功一次产出 +1 点，最终 `min(点数, rolls)`——即 **2 点固定 +1、4 点固定 +2、10 点固定 +5**（1 点仍为 0）；
- 产物先进原版 `productCount` 缓冲（上限 50 件），出货时按件写点数。
- **大型采矿机站点**：站点矿机的 `insertTarget` 恒为 0（原版只对连接实体为站点的情况才设目标，站点走 `StationComponent.UpdateVeinCollection`），因此普通矿机补丁打不到。`P0_MinerCollector` 在 `UpdateVeinCollection` 的 prefix/postfix 上按 `storage[0].count` 差量掷同一套矿机骰（键 = 站点 entityId），并把 `store[0].inc` 累加到"原总点数 + 新增件数 × 每件点数"（`QualityMath.StationIncAfterTransfer`，`QualityMath.cs:145`）；速度路径不变。

**分馏塔**：产物品质由 `fluidInputInc / fluidInputCount` 继承（`QualityMath.FractionatorInputLevel`，`QualityMath.cs:252`），并做一次升品；切换配方时清空该塔的产物池。

### 2.4 八个品质加成

八项都由**建筑自身**的品质点数驱动，共用同一张曲线（见 4.3）。各项独立开关，关闭时补丁不注册。

| 配置键 | 作用对象 | 实现方式 | 电耗影响 |
|---|---|---|---|
| `speedBonus` | 制造台、矿机 | 乘算每批 `speed` 瞬时值（`AssemblerComponent.InternalUpdate` 的 `speed` 参数 / `MinerComponent` 的采集速度） | 不额外耗电 |
| `chargeBonus` | 物流塔 | 同时抬高 `StationComponent.SetPCState` 里的 `energyPerTick` 与 `pcPool[].requiredEnergy` | 总电量上限 `energyMax` 不变，单位时间取电上升 |
| `labSpeedBonus` | 研究站 | 放大 `LabComponent.InternalUpdateResearch` 的瞬时 `power` | 不改电网耗电、不改存档字段 |
| `turretFireRateBonus` | 6 种防御塔（高斯/激光/加农/等离子/导弹/近程等离子） | 放大 `TurretComponent.Shoot_*` 的开火计时 `power` | 不改伤害/射程/弹药逻辑 |
| `powerGenBonus` | 5 类发电机（风/光/地热/火/射线） | 放大 `PowerGeneratorComponent.EnergyCap_*` 的 `capacityCurrentTick` 与返回值 | 火力按比例多烧燃料、射线按比例多产（双收益） |
| `powerRangeBonus`（v0.18.0，内部常量恒开） | 电力塔类建筑（`PrefabDesc.isPowerNode`：感应塔/无线输电塔/配电站） | 落成与读档后按 `prefab × 曲线倍率` **幂等重算** `PowerSystem.nodePool[].connectDistance` 与 `.coverRadius` | 覆盖半径/拉线距离随品质放大，不额外耗电 |
| `launcherSpeedBonus` | 电磁轨道弹射器、垂直发射井 | 放大 `EjectorComponent` / `SiloComponent` 的每 tick 发射进度增量 | 不改耗电/弹药配置 |
| `sprayerIncBonus` | 喷涂机 | 按**喷涂机建筑**点数把曲线百分比当概率，命中时给被喷物品额外 +1 点 | 不改喷涂次数/增产剂消耗 |

### 2.5 背包装备隔离（按点数分格）

| 项 | 说明 |
|---|---|
| 范围 | 玩家背包内**建筑类物品**按每件点数分格；同点数仍合并，0 点堆叠照常合并 |
| 容器 | `isolateStorageContainers=true` 时扩展到通用 `Default` 储物容器（储物仓、战场分析基站等） |
| 不隔离 | 物流塔内部、机甲燃料/弹药/炸弹/战斗机格、过滤容器、储液罐（`TankComponent`） |
| 整理按钮 | 背包与容器的"整理"由 `StorageComponent.Sort` 前缀接管，按点数重组，不会把不同点数并成一格 |
| 关闭 | `isolateBuildingStacks=false` 时背包与容器同时回退原版合并（容器项依赖前一项） |

### 2.6 仓库品质扩容（**v0.19.2 起停用**）

> **状态（v0.19.2）**：`QualityConfig.StorageQualityGridsValue` 已改为 **`false`**，功能停用，行为等同删除——储物仓格数恒为原版值，启动日志固定打印 `[QSystem][storage] NOT ARMED (storageQualityGrids=false; vanilla containers)`。停用原因是打开仓库界面时 `System.IndexOutOfRangeException at UIStorageGrid._OnUpdate()` 崩溃。**代码与存档兼容逻辑保留**（旧存档尾块 `storages` 里的溢出数据读档时静默忽略、只打一条 `WarnOnce`），以便旧档无损读取。下面描述的是该功能被停用前的设计，保留作历史与将来复用的参考。

储物仓类容器（通用 `EStorageType.Default`：小/大储物仓、战场分析基站等）的**可用格数**随该建筑品质点数增加：

```
额外格 = floor(点数 × 0.5)，再被 max(20) 与"原版格数"双重夹住
总格数 = 原版格数 + 额外格
```

- 例：10 点小储物仓 30 → 35 格，大储物仓 100 → 105 格；
- 扩容只在**内存**中发生：导出存档前的 prefix 把容器 `size` 收回原版值，postfix 与 finalizer 恢复；存档里 `size`/`grids` 永远是原版值；
- 已放进原版格子的物品不受影响；放得进原版空格的额外格物品在保存前自动搬回，搬不进去的写入尾块 `storages` 节点；
- 点数下降或功能关闭时，容器在额外格搬空后缩回原版格数（每 30 帧重试一次，不丢物品）；
- 带禁用格（`bans`）的容器按"原版可用格数"夹，不会占用禁用格。

### 2.7 分流器输出口品质过滤（常开）

- 入口：点原版分流器窗口的过滤圆（`UISplitterWindow.OnCircleFilterClick`）打开**原版物品选取器**，MOD 在选取器底部追加一行「品质分流」设置；v0.16.1 的自建折叠面板已在 v0.17.0 删除；
- 页脚按"口"组织：`[口A] [品质分流 开/关] [物品: 任意物品] [-] [≥N / 不限] [+]`；开关关闭即无规则，该口回原版分配；
  - 点「物品：…」在选取器列表里选物品（支持搜索/翻页），选中即**自动打开该口开关并写入规则**；「任意物品」= 只按品质过滤；
  - `[-]` / `[+]` 步进最低点数 `0 → 1 → … → 10 → 0`（0 = 不限点数）；再次打开同一口会回填当前规则；
- 匹配规则：物品相同（物品 0 = 任意）**且该票货的每件点数 ≥ N**；同一物品存在多档规则时按 N 降序匹配（严格者优先），同档按口号升序；
  - **每件点数** = `QualityMath.PerItemPoints(Cargo.inc, Cargo.stack)`（整栈总点数 ÷ 堆叠数，向下取整）。原版 `Cargo.inc` 是**整栈总点数**（合并栈时累加、喷涂机按 `count × 能力` 写入），v0.17.0 曾把它当每件点数比较，导致 2 件各 1 点（总量 2）也通过阈值——v0.17.1 修正；
- **只要该分流器有一条规则，MOD 就完全接管**（`CargoTraffic.UpdateSplitter`/`UpdateSplitterAsync` 的 Prefix 返回 `false`，原版循环不执行）：
  - 命中规则的货**只允许进规则口**；规则口全满时货留在原输入皮带等待（背压），不会溢到无规则口；
  - 未命中规则的货在**无规则的输出口**里按原版 first-fit 顺序（output0 → output3）分配，规则口不参与（`QualityMath.PlanSplitterFallback`）；
  - 原版全局 `outFilter`（原版选取器仍会为图标写入它）**不参与路由**，因此不可能把低品质同种货送进规则口；
- 无规则的分流器整台交还原版（`skip-no-rule` 计数），行为与原版逐字节一致；
- 原版输入优先级 / 优先级槽位预设：接管前先 `CheckPriorityPreset()` 把预设落到槽位（与原版一致），路由仍按规则；
- 顶部压了箱子（`topId != 0`）的分流器：**不实现原版"货入顶部储物"分支**，进不了输出的货留在皮带（普通地面四向分流器 `topId == 0`，不受影响）；
- 规则只存在 MOD 侧表（`QualityData.SetSplitterRule`），不写原版 `SplitterComponent` 字段，随尾块 `splittersV2` 节点持久化；
- 诊断计数（`DSPQS_DEV=1` 时日志）：`took` / `routed` / `fallback` / `unrouted` / `noop` / `skip-no-rule` / `skip-preset` / `top-storage`；
- 旧的「品质优先」自动输入排序、面板勾选框与 `F3` 热键已在 v0.16.1 **删除**——逐口规则能表达"高品质走哪个口"，而自动排序在规则口全占满时会覆盖玩家意图；v0.17.0 起规则的编辑统一走原版选取器页脚。
- **品质筛选可复制（v0.19.2）**：四向分流器的**每口规则**（口位 + 允许物品 + 最低品质）会随「建筑设置复制」一起迁移——Shift 拖放复制、Ctrl+C / Ctrl+V，以及蓝图复制/粘贴，都各自保留原建筑的规则；实现见 §4.2.9 的 `P6_FilterCopy`。注意这与"建筑自身品质点数"是两件事：蓝图文件里存的建筑品质点数仍按原版语义处理（即 MOD 不接管蓝图里的建筑品质点数），筛选规则是 MOD 追加在 `BlueprintBuilding.parameters` 尾部的独立魔数块。

### 2.8 分拣器品质阈值过滤（常开）

- 入口一：原版分拣器窗口的过滤器按钮（`UIInserterWindow.OnSelectFilterClick`）打开原版物品选取器，MOD 在其底部追加 `分拣器 品质阈值 [-] ≥N [+]` 行；入口二（保留）：**Shift + 右键** = 阈值循环 `0 → 1 → … → 10 → 0`，**Ctrl + Shift + 右键** = 清零；
- 设置阈值后，原版过滤器标题被改写为 `磁铁  品质≥5` 形式，不额外开面板即可看到状态（阈值为 0 时保持原版标题）；
- 取货语义（v0.19.1 重写）：分拣器 tick 前缀把"当前分拣器 + 其阈值"记进 `[ThreadStatic] _ctxMin`；**传送带**来源在 `CargoPath.TryPickItem` 的两个带 filter 重载（`plain` / `needs`）前缀里，于**原版的 5 格取货窗口**（`belt.pivotOnPath + offset - 2`，长 `WindowLength = 5`）逐件扫描，选出第一个"物品符合原版过滤器（或需求列表 `needs`）**且每件点数 ≥ 阈值**"的货物，自行 `RemoveCargoAtIndexDirect` 后把 `item / stack / inc` 回填给原版 caller（`inc` 保持**整栈总点数**，即原版单位）；
- **覆盖范围（v0.19.1）**：除传送带外，另一级挂钩是 `PlanetFactory.PickFrom(uint ioTargetTypedId, int offset, int filter, int[] needs, out byte stack, out byte inc)` 的 prefix（`Priority.High`），按 `EFactoryIOTargetType` 分来源处理——**`storage`**（储物仓/容器）：只读网格扫描镜像原版 `TakeTailItems` + `split_inc`，达标才取；**`assembler`**（机器产物口）：只**预测下一件点数**，不足就拦，**不消耗产物池点数**（放行时原样交给 `P0_PickFrom`）；**`ejector` / `silo`**：按 `bulletInc` / `bulletCount` 预检；**跳过 `station` / `lab` / `powerGen`**——`station` 的 `TakeItem` 选择序与 `keepMode` 无法安全复现、`lab` 产物无品质池（点数恒 0）、`powerGen` 燃料无点数，这三类来源保持原版取货（跳过原因同时写进启动日志与 `Describe()` 的 `bySource=`）；
- **与 `P0_PickFrom` 的互斥**：两者都 prefix 同一个 `PickFrom(uint,...)`，本补丁 `Priority.High` 先跑——返回 `true` 表示放行给 `P0_PickFrom`（来源 = `assembler` 时必走这条），返回 `false` 表示自己已拦下，避免 `P0_PickFrom` 再去消耗产物池点数；
- 窗口内没有达标货物时按原版"过滤不匹配"处理（`__result = 0`，货留在皮带继续流动、不销毁）；阈值为 0 时**完全走原版**（`pick-min0` 计数）；
- 只影响分拣器自己取货（传送带 = `CargoPath.TryPickItem` 两个带 filter 的重载；其它来源 = `PlanetFactory.PickFrom(uint,...)`；外加三个 `InserterComponent.InternalUpdate*` tick 前缀建立线程上下文）；发电厂抓燃料（`TryPickFuel`）不受阈值影响；
- v0.18.0 的缺陷（v0.19.1 修复）：当时只拦 `CargoTraffic.TryPickItem`，**这条路径只覆盖传送带**，而储物仓、机器产物口、弹射器/发射井走的是 `PlanetFactory.PickFrom`，于是低品质货照样被取走、阈值形同虚设；
- **v0.19.2 修复三处（`P5_SorterFilter` 重写）**：① **真根因是储物仓分支的 id 映射写错**——`storage` 来源把**实体 id（entityId）**直接当成 `storagePool` 的下标去取组件，而原版要求先 `entityPool[entityId].storageId` 映射、再用 `storagePool[storageId]` 取网格，错用导致命中空组件、`grids=0`，从储物仓抓货时品质点数直接丢失（表现为"仓库 → 传送带丢点数"）；② **删掉 `lastEmptyItem` 缓存门控**——此前"上次扫描空手而归"会把结果缓存住，导致阈值或物品过滤变化后不立即生效，且"只设品质阈值、不选物品"时阈值形同未设；现在每次取货都按当前阈值与过滤实时判定；③ **补 `NotifyStorageChange()`**——取货后立刻通知仓库界面刷新，修掉"取走货后界面残留旧货、要关掉面板重开才消失"的问题；
- **阈值设置同样可复制（v0.19.2）**：分拣器的品质阈值会随 Shift 复制、Ctrl+C / Ctrl+V 与蓝图复制/粘贴一起迁移，与分流器规则同一套机制（见 §4.2.9 的 `P6_FilterCopy`）；
- **覆盖来源清单（最终）**：**支持**——传送带（`CargoPath.TryPickItem` 两个带 filter 的重载）、储物仓/容器（`PickFrom` 的 `storage`）、机器产物口（`assembler`）、弹射器（`ejector`）、发射井（`silo`）；**不支持**——物流站（`station`）、研究站（`lab`）、发电机（`powerGen`），原因见上一条 `bySource=` 与启动日志里的跳过说明；
- 阈值写入尾块 `sorters` 节点，物品 id 仍由原版 `InserterComponent.filter` 保存；
- 诊断计数（`DSPQS_DEV=1` 时日志）：`picked` / `blocked` / `skipped` / `ticks` / `pathCalls` / `pathMin0` / `pickCalls` / `pickMin0` / `scanned`，以及按来源拆开的 `bySource=[belt:… storage:… assembler:… ejector:… silo:…]`（v0.19.1 起才有 `pickCalls`/`bySource`）；v0.19.2 新增的 DEV 探针为 `storage-probe`（储物仓网格/映射诊断）、`storage-pick`（储物仓取货逐件判定）、`pick-return`（取货回填的 item/stack/inc 核对）、`inserter-hold`（分拣器持货窗口状态）；
- **两个过滤功能都没有新增玩家配置键**（总开关是内置常量、默认开启）；0 条规则 / 阈值 0 时与原版行为完全一致。

### 2.9 物品品质角标（常开）

- 在物品图标右上角显示**每件品质点数**（`inc / 堆叠数` 向下取整，0 点不显示），显示阈值由 `itemQualityBadgeMinPoints` 控制（默认 1）；
- 覆盖范围：玩家背包格、打开的储物仓/容器面板格、手持物品提示；
- 样式 `number` / `dot` / `both`，颜色四档：1–2 点灰蓝、3–4 点青、5–7 点紫、8–10 点金；字号 12px（手持 11px）；
- 每格角标与原版格子数字文本同生命周期（打开/扩容/关闭时一起创建销毁，刷新只改文字/颜色/显隐）；
- **纯显示**：不写存档、不改物品数据、不拦截鼠标；
- 建筑产物悬停预览（装配机/熔炉顶部大图标）**刻意不做角标**（无真实 inc 数据）。

### 2.10 10 点建筑光环（默认开启）

对当前星球上点数 = `maxQuality`（10）的建筑画一圈金色光环：

| 项 | 值 / 规则 |
|---|---|
| 样式 | `tenPointMarkerStyle`：`auto` / `halo` = 画；`none` = 完全不画 |
| 贴图 | 256×256 程序生成空心环形贴图（内孔透明、环体实心、外圈柔和光晕） |
| 朝向 / 位置 | 面向相机的 billboard，浮在 `selectCenter + selectSize.y*0.5 + 1.2m` |
| 尺寸 | `clamp(max(selectSize.x, selectSize.z) * 0.42, 1.0, 3.2) × tenPointHaloSize` |
| 动画 | 约 20°/s × `tenPointHaloSpin` 自转；±0.12m 浮动；alpha 0.60–1.0 呼吸（`tenPointHaloPulse=false` 时恒定 0.85） |
| 剔除 | 120m 外不画；同屏最多 64 个（按距离取近） |
| 渲染 | 加法混合、`ZTest Always`、`renderQueue = 3100`，不被建筑遮挡 |

### 2.11 品质回收机（常开，v0.19.0）

把带品质点数的物品「回炉」成增产剂喷涂次数——复用原版**喷涂机**（物品 2313），不新增建筑 / 配方 / UI。

- 载体与通道：回收通道 = 喷涂机的**增产剂（催化剂）进料带**，也就是原版放 1141/1142/1143 的那一侧；物品带那一侧照常喷涂，两者互不影响；
- 触发条件（全部满足才回收）：
  - 喷涂机**通电**：原版 `consumerPool` / `powerRatio > 0.1`（与原版判定一致）；
  - 催化剂进料带上出现 cargo，且该货是**非增产剂物品**；
  - 该货**单件点数** `Cargo.inc / Cargo.stack ≥ minPerItem`（默认 `minPerItem=1`，常量定义 `QualityRecyclerMath.DefaultMinPerItem = 1`）：**任何真正带点数（≥1 点/件）的货物都回收**——Mk.I 喷过的 1 点/件、Mk.II 的 2 点/件、Mk.III 的 3 点/件及以上一律参与；**0 点/件（未喷涂）整叠原样放行、不消耗**。无需用阈值防套利：兑换本身是亏损的（4 点只换 1 次喷涂次数，而 1 次喷涂最多值 3 点）；把常量改成 3 才回到旧版「只收 Mk.III 喷过的」行为；
  - 该机喷槽未满（容量 600 次）。
- 行为：整叠被**消耗并销毁**（不返还、不在下游产出任何物品）；点数 `cargo.inc` 记入该喷涂机的**回收账号**；**每 4 点 = 1 次喷涂次数**灌进原版喷槽（`pointsPerSpray=4`、`incItemId=1143`、`incSprayTimes = max(1, HpMax)` 即 1、容量 **600 次**），超出容量的点数留在账号；满槽时停止回收（probe `mode=tankFull`），消耗后有空间自动恢复回收；
- 余额：每台机一个账号（`0..600*4` 点），随存档持久化；拆机后 `1200` tick 的惰性清扫会清掉孤儿账号（`PrunedEvery=1200`）；
- 注入点：`SpraycoaterComponent.InternalUpdate(CargoTraffic, AnimData[], int[])` 的 **postfix**（struct / class 双签名按 `IsValueType` 反射选择；每次先 `Refresh` 从 `spraycoaterPool[id]` 重读组件、改完 `Persist` 写回，避免 struct 副本丢改动）。IL 依据（当前游戏 DLL）：`0x000E if(incBeltId<=0) goto 0x025F`、`0x0189 TryPickItem(segIndex+segPivotOffset-2, 5, incItemId, ...)`、`0x02AE if(cargoBeltId<=0) end`、`0x0257` 之后才判 `powerRatio>0.1`；`CargoTraffic.SpraycoaterTick` 对每台喷涂机**无条件**调 `InternalUpdate`；
- 存档：行星节点新增可选 `"recyclers": [ { "e": entityId, "p": points, "c": capacity } ]`（`p` = 该机账号累计点数，`c` = 该机容量，按点数计 600 次 × 4 点 = 2400）；无回收机不写节点；旧档无此 key 直接跳过；本轮迭代早期版本遗留的 `"o"`（投递计数）字段**读档一律忽略**；
- 数值口径：一件被 Mk.III 喷满 10 点的物品 → 10 点 = 2 次喷涂 + 余 2 点；4 点/次 × 2 = 8 点用掉，余 2 点留在账号；
- 日志：启动 `[QSystem][recycler] recycler armed=<True/False> reason=<..> mode=inc-feed selfCheck=ok pointsPerSpray=4 minPerItem=1 accounts=<n>@<planets> carried=<points>`；probe（状态变化 Info、DEV 下每 200 次 Debug）`[QSystem][recycler][probe] eid=.. incBelt=.. cargoBelt=.. brand=.. tank=<incCount>/<incCapacity> extra=.. room=.. inputIdx=.. cargo{item=,stack=,inc=,per=} mode=<harvest|sprayer|idle> reason=<noIncBelt|badBelt|noBelt|noPath|noCargo|emptyCargo|catalyst|noPoints|lowPoints|tankFull|accountFull|pickFailed|queued|..> calls=.. account=<points>`；回收成功（Debug，最多 20 行）`[QSystem][recycler] recycled coater=.. points=+.. sprays=+.. tank=<..>/<..> bonus=<..> account=<..>`；计数 `effective=..`（每 200 次）、`pruned=..`；
- 状态：启动自检 `selfCheck=ok`、离线单测通过，**行为级（实际放机跑带）已由用户 2026-10-31 实机验证通过**；验证后按用户反馈把默认阈值由 3 降到 1。

---

## 3 配置

### 3.1 文件与生效方式

- 配置文件：`BepInEx\config\dsp.qualitysystem.cfg`，节 `[quality]`；
- **cfg 内注释为中文**：每个键上方的说明文字取自 `cfg.Bind` 的中文描述（唯一来源是 `QualityConfigText.PlayerEntries`，由 `QualityConfigText.DescriptionOf(key)` 提供），玩家打开 `BepInEx\config\dsp.qualitysystem.cfg` 即可读懂每个键的作用，无需查文档；
- **旧 cfg 自愈 + 清理旧键（一次性）**：启动时用 `QualityConfigText.ParseKeyValues` 读出文件全部 `key = value`；若键集合 **≠ 16 个玩家键**（缺键、多键、旧版本残留键都算；判据只看键集合，不看注释文本——旧版英文布局必然携带旧键，因此必然触发重写），则整份覆盖重写：
  - 重写为 **16 键中文文件**（`QualityConfigText.BuildCfgText(version, values)`，UTF-8 无 BOM）；
  - 玩家在这 16 键上**已有的取值全部保留**（按类型重新解析后写入 live entry，解析失败回落默认值，且不写 BepInEx 的 orphan 列表）；
  - 已删除功能的旧键（`incPerPoint`、`debugProbes`、`blueprintQuality`、`storageQualityGrids*`、`splitterQualityPriority` 等）**全部丢弃**，并清除 BepInEx 内存里的 orphan 条目，之后保存也不会被写回；
  - 日志：`[QSystem][config] rewrote cfg with cleaned Chinese layout (16 keys, dropped N legacy keys)`（N = `QualityConfigText.CountLegacyKeys` 统计的旧键个数）；
- **二次启动不重写**：文件已合格（键集合匹配）立即返回，内容不再变化；BepInEx 自身可能刷新文件时间戳，属正常现象；
- 运行时通过 `QualityConfig` 属性读取（`ConfigEntry.Value` 热读），但**补丁注册与渲染器安装发生在启动时**，因此修改配置后需重启游戏生效；
- 只有 **16 个玩家键**会被写入 cfg；其余参数均为内置常量，不出现在配置文件中（见 §3.3）；
- 旧版本遗留的已废弃键会被 BepInEx 保留但**不再被读取**，可直接删除。

### 3.2 玩家配置（16 键）

| 键 | 默认值 | 中文名 | 作用 |
|---|---|---|---|
| `isolateBuildingStacks` | `true` | 背包按品质分格 | 背包内建筑类物品按每件点数分格；关闭 = 背包与容器一起回原版合并 |
| `isolateStorageContainers` | `true` | 容器按品质分格 | 隔离扩展到通用 `Default` 储物容器；需上一项为 `true` |
| `speedBonus` | `true` | 制造/采集速度加成 | 制造台与矿机按本栋点数提速（曲线，10 点 = 2.00×），不额外耗电 |
| `chargeBonus` | `true` | 物流塔充电上限加成 | 每 tick 充电上限与电网需求同步提升（曲线） |
| `labSpeedBonus` | `true` | 研究站研究速度加成 | 放大研究吞吐，不改电耗 |
| `turretFireRateBonus` | `true` | 防御塔射速加成 | 6 种防御塔开火间隔缩短 |
| `powerGenBonus` | `true` | 发电功率加成 | 5 类发电机功率提升（火力多烧燃料、射线多产为双收益） |
| `launcherSpeedBonus` | `true` | 弹射器/发射井速度加成 | 发射进度增量放大 |
| `sprayerIncBonus` | `true` | 喷涂机额外出品加成 | 按喷涂机点数把曲线当概率，命中时被喷物品 +1 点 |
| `itemQualityBadges` | `true` | 物品品质角标 | 物品图标角上显示每件点数；关闭时不注册补丁、不创建角标 |
| `itemQualityBadgeStyle` | `"number"` | 角标样式 | `number` 数字 / `dot` 圆点 / `both` 两者；未知值按 `number` |
| `itemQualityBadgeMinPoints` | `1` | 角标显示阈值 | 每件点数达到该值才显示，钳制 1–10 |
| `tenPointMarkerStyle` | `"auto"` | 10 点标记样式 | `auto` / `halo` 画光环，`none` 不画；未知值按 `auto` |
| `tenPointHaloSize` | `0.3` | 光环大小 | 半径倍率，钳制 0.3–3.0 |
| `tenPointHaloPulse` | `true` | 光环呼吸 | `false` 时恒定亮度 0.85 |
| `tenPointHaloSpin` | `1` | 光环自转速度 | 基础约 20°/s 的倍率，钳制 0–5 |

### 3.3 内置常量（不再对玩家开放）

以下参数已内置为编译期常量（定义在 `QualityConfig.cs` 的 `*Value` 字段），**不再出现在玩家 cfg 中**，修改需改源码重新编译。其中"分流器输出口过滤""分拣器品质阈值"随功能转正常开（恒 `true`）；"仓库品质扩容"自 **v0.19.2 起改为 `false`（停用）**，见 §2.6。其余取值沿用原有平衡默认值，行为不变。

| 常量 | 值 | 作用 |
|---|---|---|
| `Enabled` | `true` | 总开关恒开（不存在"关闭 MOD 功能"的玩家键） |
| `incPerPoint` | `100` | 每点品质 = +10% 产出（写入 `incTable`/`incTableMilli`） |
| `powerPerPoint` | `250` | 每点品质 = +25% 电耗（写入 `powerTable`/`powerTableRatio`） |
| `buildingChancePerPoint` | `1.5` | 旧线性升品公式系数（%/点），仅保留给离线 A/B，运行时改用曲线（见 2.3） |
| `minerChancePerPoint` | `100` | 矿机每次判定成功率（%/点）；100 = 每 2 点必定 +1（v0.18.0 前为 2） |
| `maxUpgradeChance` | `60` | 旧线性升品概率上限（%），仅离线 A/B 使用（曲线路径由 `ClampPercent` 限 100） |
| `maxQuality` | `10` | 品质点数上限 |
| `giveDismantleInc` | `true` | 拆除返还点数（常开） |
| `speedPerPoint` | `2` | 线性公式系数（当前曲线模式不参与计算，保留以支持公式对照） |
| `maxSpeedBonus` | `50` | 线性公式速度加成上限（%） |
| `chargePerPoint` | `2` | 线性公式充电系数 |
| `maxChargeBonus` | `50` | 线性公式充电加成上限（%） |
| `speedBonusMode` | `"curve"` | 速度/充电加成曲线开关：`curve` = 查表；`linear` 才会用上面四个线性参数 |
| `extraUpgradeAbove` | `6` | 超过该点数多一次升品判定 |
| `doubleUpgradeAt` | `10` | 达到该点数必定 +2 |
| `preferHighQualityConsume` | `true` | 优先消耗最高品质建筑物品（常开） |
| `tenPointHaloColor` | `"FFD24A"` | 光环颜色（RRGGBB/ARGB 十六进制） |
| `tenPointHaloLift` | `1.2` | 光环相对建筑最高点的抬升（米） |
| `tenPointHaloMax` | `64` | 同屏光环数量上限 |
| `storageQualityGrids` | **`false`**（v0.19.2 起） | 仓库品质扩容总开关——**已停用**：值由 `true` 改为 `false`，功能行为等同删除，修掉 `UIStorageGrid._OnUpdate()` 的 `System.IndexOutOfRangeException` 崩溃；代码保留以兼容旧存档尾块 `storages` 的溢出数据（读档静默忽略、只 `WarnOnce`） |
| `storageQualityGridsPerPoint` | `0.5` | 每点品质的额外格数（随功能停用而不生效） |
| `storageQualityGridsMax` | `20` | 单个容器额外格上限（随功能停用而不生效） |
| `splitterQualityFilter` | `true` | 分流器输出口品质过滤总开关（常开） |
| `sorterQualityFilter` | `true` | 分拣器品质阈值过滤总开关（常开） |
| `powerRangeBonus`（v0.18.0） | `true` | 电力塔类建筑的连接距离/覆盖半径随品质曲线放大的总开关（内置常量，恒开） |
| `recyclerEnabled`（v0.19.0，内置开关，非 cfg 键） | `true` | 品质回收机总开关（常开；关闭时不注册 `P7_Recycler`） |
| `MinPerItemValue`（v0.19.0，`Patches\P7_Recycler.cs` 内部开关，非 cfg 键） | `1` | 回收机接收单件物品的最低点数（默认取 `QualityRecyclerMath.DefaultMinPerItem=1`）：低于该值整叠原样放行、不消耗；默认 1 = 任意带点数的货物都回收、未喷涂的 0 点货物放行；改成 3 即回到「只收 Mk.III 喷过的」 |
| `pointsPerSpray`（v0.19.0，内置开关，非 cfg 键） | `4` | 回收机每累计多少点灌 1 次喷涂次数（写入原版喷槽，物品 1143）；喷槽容量 600 次 |
| `sprayTimesPerItem`（v0.19.0） | `1` | 三种增产剂每件可用喷涂次数的**基准值**（原版 Mk.I/II/III = 12/24/60，本 MOD 的 `HpMax` 统一写 1）（v0.19.2 起恒为 1 次/件；喷涂次数分级已回滚删除） |
| `debugInjectPoints` | `4` | 开发者注入热键（F10/F11）使用的点数 |

> v0.19.1 另新增一个存档节点（不是常量）：`"inputs"` = 制造台"本批原料点数"（`[{"e":entityId,"l":level}]`）；该节点是**可选节点**、旧档缺失即跳过，结构见 §4.6。

### 3.4 开发者开关

- `DSPQS_DEV` 环境变量 = `"1"` 时启用**开发者模式**（`QualityConfig.DevMode` → `QualityDevSwitch`，`QualityConfig.cs` 中 `EnvVar = "DSPQS_DEV"`）。
- 启用后：调试探针日志（如升品判定 `[QSystem][roll]`）开始输出，并注册热键 F5/F6/F7/F8/F9/F10/F11/F12。
- 未启用时这些热键**完全不响应**，日志安静。
- `F3`（旧的"品质优先"切换）已随该功能删除；分流器过滤规则全部通过面板编辑，不需要热键。

---

## 4 技术实现

### 4.1 载入顺序

1. BepInEx 载入 DLL，调用 `Plugin.Awake`。
2. `QualityConfig.Init(Config)`：绑定 16 个玩家键，其余常量就位。
3. 打印日志：版本、构建信息、完整配置、规则宣告行（矿机 `floor(points/2)`、额外升品阈值、必升阈值）。
4. `Patches.P0_Tables.Apply("Awake")`：无条件改写 4 张原版静态表（`incTable`、`incTableMilli`、`powerTable`、`powerTableRatio`）。
5. `new Harmony(GUID)` + `PatchAll(assembly)`：注册 **18 个特性补丁类**，统计并打印 `patchedMethods` 数量与方法名单（启动日志此处为 **18**）。
6. 逐个手工 `Apply(harmony)` 注册 **22 个手工补丁类**（`P4_Isolation.Apply` 内部转调 `P4_IsolationSort.Apply`；包括 `P5_SplitterFilter`、`P5_SorterFilter`、`P5_SorterFilterUi`、`P5_QualityFilterPicker`、`Patches.InstantBuildCapture`、`Patches.P1_DeferredPay`、`Patches.P5_PowerRange`、v0.19.0 新增的 `Patches.P6_SprayCount`、`Patches.P7_Recycler`、v0.19.2 新增的 `Patches.P6_FilterCopy` 等）：每个补丁类的失败被单独隔离，不会中断后续注册。合计向原版方法发起 **85 次方法级注册**、去重后覆盖 **78 个独立方法**（**v0.19.2 统计**：v0.19.0 口径 74 次 / 67 方法 + v0.19.2 新增 `P6_FilterCopy` 11 个锚点；v0.19.1 的 `P6_SprayUses` 6 个锚点已随回滚删除，不计入。代码树实测：`Patches\` 39 个补丁 `.cs`、20 处 `[HarmonyPatch]`、22 个手工 `Apply(Harmony)` 类、59 处 `harmony.Patch(` 调用点；逐条锚点以 §4.2 各表为准）。
7. `P5_TenPointHalo.Install(gameObject)`：挂载光环渲染组件（非 Harmony，自建 HUD 对象）。
8. 每帧 `Update`：
   - `PollStartup()`（每 30 tick）：等 `LDB.items` 就绪后再次 `P0_Tables.Apply("LDB ready")`，随后执行 `Diag.StartupAudit.Run()`；
   - `HandleHotkeys()`（仅开发者模式）；
   - `P5_TenPointHalo.Tick()`（每帧刷新光环目标与绘制）；
   - `StorageGrids.Tick()`（容器扩容/缩回的增量泵）。
   - 分流器/分拣器过滤没有每帧 tick：规则在补丁内部按侧表读取。

> 表改写执行两次是幂等的：`Awake` 时保证最早生效，`LDB ready` 时再对齐一次物品元数据就绪后的状态。

### 4.2 Harmony 补丁总表

共 **42 个补丁类**（`Patches\` 下 **39 个补丁 `.cs`** 文件，另有 `P5_Common.cs` 等共享文件；v0.19.2 实测口径 = 20 处 `[HarmonyPatch]` + 22 个 `Apply(Harmony)` 手工类）：20 个由 `[HarmonyPatch]` 特性自动注册，22 个由 `Plugin.Awake` 手工 `Apply` 注册（便于单点失败隔离；v0.19.2 新增 `P6_FilterCopy` 一类，v0.19.1 的 `P6_SprayUses` 已随回滚删除）。方法级注册的数量与去重结果以 4.2.1–4.2.9 各表的逐条锚点为准（**v0.19.2 统计**为 74 + 11 = 85 次方法级注册 / 78 个不同方法；v0.19.1 的 `P6_SprayUses` 6 个锚点已回滚删除，不计入；代码树实测 59 处 `harmony.Patch(` 调用点）。v0.19.0 起有 6 个方法被两类补丁共同接管：`AssemblerComponent.InternalUpdate`、`MinerComponent.InternalUpdate`、`Player.UseHandItems`、`BuildTool_Click.CreatePrebuilds`、`StorageComponent.TakeTailItems`、`GameData.Export`。启动日志 `patchedMethods=18` 是 `PatchAll` 时点的**特性类**计数。

#### 4.2.1 品质表、规则与产物路径

| 类型.方法 | 补丁点 | 作用 | 补丁类 |
|---|---|---|---|
| `Cargo.incTable` / `incTableMilli` / `powerTable` / `powerTableRatio` | 非 Harmony：直接改静态数组 | 让原版 `inc → 产出/电耗` 公式按 100 点/250 电耗解释 | `P0_Tables` |
| `UIGame._OnCreate` | postfix | UI 创建时再对齐一次表 | `P0_Tables` |
| `PlanetFactory.PickFrom(uint,int,int,int[],out byte,out byte)` | prefix | 自实现取货：仅 `EFactoryIOTargetType.Assembler` 走品质池，其余原版 | `P0_PickFrom`（裸 `[HarmonyPatch]` + `TargetMethod()`） |
| `MinerComponent.InternalUpdate(PlanetFactory,VeinData[],float,float,float,int[])` | transpiler | 把矿机出货 `InsertInto(..., itemInc=0)` 改写成 `MinerOutputInc(factory, entityId) * count` | `P0_Miner` |
| `StationComponent.UpdateVeinCollection(PlanetFactory,int[])` | prefix + postfix | 大型采矿机站点：按 `storage[0].count` 差量为站点格补品质点数（原版这条路径只搬数量、不写 inc） | `P0_MinerCollector` |
| `AssemblerComponent.InternalUpdate` | prefix + postfix | prefix 清 `pendingLevel`、记录 `wasReplicating`、拒收越界 entityId；postfix 在批次结束时提交产物点数 | `P0_Assembler` |
| `FractionatorComponent.InternalUpdate(...)` | prefix + postfix | 分馏塔产物池的生成/出货/回滚接入品质 | `P0_Fractionator` |
| `FractionatorComponent.SetRecipe(int,SignData[])` | postfix | 切换配方时清空产物池 | `P0_Fractionator` |
| `FactorySystem.GameTick(long,bool)` | prefix + postfix | `QualityEnv.Enter/Exit` 建立"当前星球"作用域 | `P0_Env` |

#### 4.2.2 建造、消耗与拆除

| 类型.方法 | 补丁点 | 作用 | 补丁类 |
|---|---|---|---|
| `Player.UseHandItems(int,out int)` | prefix + postfix | 建造扣料时记录被消耗物品的品质点数 | `P1_BuildCapture` |
| `BuildTool_Click.CreatePrebuilds()` | prefix + postfix | 捕获点击建造每一栋的消耗上下文 | `P1_BuildCapture` |
| `PlanetFactory.AddPrebuildDataWithComponents` | postfix | 预建造点搬迁（`prebuildInc`）+ 按 `protoId` 取消费收据绑定品质（覆盖原版从背包取料的普通建造/蓝图粘贴） | `P1_BuildCapture` |
| `PlanetFactory.AddEntityDataWithComponents(EntityData,int)` | postfix | 新实体绑定唯一可靠点：把预建造点落到 entityId | `P1_BuildCapture` |
| `PlanetFactory.DismantleFinally` | postfix | 实体消失时 `QualityData.ClearEntity` | `P1_Cleanup` |
| `PlayerAction_Build.DoDismantleObject(int)` | transpiler | 拆除返还：把建筑点数写进返还物品的 `inc` | `P3_Dismantle` |
| `Player.UseHandItems(int,out int)` | prefix + postfix | 优先消耗：换手到最高品质同类物品 | `P4_ConsumePriority` |
| `StorageComponent.TakeTailItems(int&,int&,int&,bool)` | prefix | 优先消耗：从点数最高的格子取 | `P4_ConsumePriority` |
| `PlayerAction_Inspect.GameTick(long)` | prefix | Shift 立即建造：满足原版即时建造条件时打开瞬时消费窗口（`P4_ConsumePriority.OpenInstantWindow`） | `InstantBuildCapture` |
| `StorageComponent.TakeTailItems(ref int,ref int,ref int,bool)` | postfix | 同帧兜底记录：记下窗口内被扣物品的 inc 与件数 | `InstantBuildCapture` |
| `PlanetFactory.BuildFinally(Player,int,bool,bool)` | prefix | 按 `prebuild.protoId` 取消费收据写到该预建；取不到收据再退同帧记录，仍对不上保留原版 0 点 | `InstantBuildCapture` |
| `BuildTool_Click.CreatePrebuilds()` | prefix + postfix | 优先消耗：点击建造路径 | `P4_ConsumePriority` |
| `BuildTool_BlueprintPaste.CreatePrebuilds()` | prefix + postfix | 优先消耗：蓝图粘贴路径（落成点数取实际消耗物品） | `P4_ConsumePriority` |
| `ConstructionModuleComponent.PlaceItems(PlanetFactory,Player)` | prefix + postfix | v0.18.0 未付款预建：开启/关闭"补付观察窗口"并清空待合并收据 | `P1_DeferredPay` |
| `StorageComponent.TakeTailItems(ref int,ref int,ref int,bool)` | postfix（仅补付窗口内） | 记录建造模块补付时实际被扣的物品/件数/总点数（不改原值） | `P1_DeferredPay` |
| `PlanetFactory.NotifyPrebuildChange(int,int)` | prefix | `change == 3` 且该预建 `itemRequired == 0` 时把合并后的 `总点数/件数` 写进 `prebuildInc` | `P1_DeferredPay` |
| `PlanetFactory.RemovePrebuildWithComponents(int)` | postfix | 预建被移除时丢弃未结清的累加器 | `P1_DeferredPay` |

> `P1_DeferredPay` 只观察不劫持：付款仍由原版 `PlaceItems` 完成（原版在该方法里有 `Assert.True(need == prebuild.itemRequired)` 要求一次全额付清），因此 `P4_ConsumePriority.TakeBestGridPrefix` 在 `P1_DeferredPay.PayWindowActive` 期间直接回退原版取料顺序。

#### 4.2.3 存档

| 类型.方法 | 补丁点 | 作用 | 补丁类 |
|---|---|---|---|
| `GameData.Export(BinaryWriter)` | postfix | 在原版写完、回填文件大小之前追加品质尾块 | `P1_SaveHooks` |
| `GameData.Import(BinaryReader)` | postfix | 读取尾块；无尾块时迁移 legacy 侧文件 | `P1_SaveHooks` |
| `GameSave.SaveCurrentGame(string)` | postfix | 保存成功后删除已迁移的 legacy 侧文件 | `P1_SaveHooks` |
| `GameSave.LoadCurrentGame(string)` | postfix | 读档后按需迁移 | `P1_SaveHooks` |
| `GameData.NewGame(GameDesc)` | postfix | 新游戏清空全部状态并重置标志 | `P1_SaveHooks` |
| `GameData.Export(BinaryWriter)` | prefix + postfix + finalizer | 仓库扩容（**v0.19.2 起该功能已停用**，补丁不再改变行为）：导出前收回容器 `size`，导出后恢复，异常兜底 | `P4_StorageGrids` |

#### 4.2.4 性能加成

| 类型.方法 | 补丁点 | 作用 | 补丁类 |
|---|---|---|---|
| `AssemblerComponent.InternalUpdate(float,int[],int[])` | prefix（`Priority.Low`） | 制造台速度加成 | `P3_Speed` |
| `MinerComponent.InternalUpdate(...)` | prefix（`Priority.Low`） | 矿机采集速度加成 | `P3_Speed` |
| `StationComponent.SetPCState(PowerConsumerComponent[])` | postfix | 物流塔充电上限 + 电网需求同步提升 | `P3_StationCharge` |
| `LabComponent.InternalUpdateResearch` | prefix | 研究站研究速度 | `P5_LabSpeed` |
| `TurretComponent.Shoot_Gauss/Laser/Cannon/Plasma/Missile/LocalPlasma` | prefix ×6 | 防御塔射速 | `P5_TurretFireRate` |
| `PowerGeneratorComponent.EnergyCap_Fuel/Wind/PV/Gamma/GTH` | postfix ×5 | 发电机功率 | `P5_PowerGen` |
| `PowerSystem.GameTick(long,bool,bool,int)` | prefix | v0.18.0 每星球一次：按品质曲线幂等重算全部电力塔类节点的 `connectDistance`/`coverRadius`（构建时另由 `AddEntityData_Bind` 立即套用） | `P5_PowerRange` |
| `EjectorComponent.InternalUpdate` / `SiloComponent.InternalUpdate` | prefix ×2 | 弹射器/发射井发射速度 | `P5_Launcher` |
| `SpraycoaterComponent.InternalUpdate(CargoTraffic,AnimData[],int[])` | transpiler | 喷涂机额外 +1 点概率 | `P5_Sprayer` |

#### 4.2.5 隔离

| 类型.方法 | 补丁点 | 作用 | 补丁类 |
|---|---|---|---|
| `StorageComponent.AddItemStacked(int,int,int,out int)` | prefix | 背包/容器按每件点数分格 | `P4_Isolation` |
| `StorageComponent.Sort(bool)` | prefix | "整理"按钮按点数重组 | `P4_IsolationSort` |

#### 4.2.6 分流器与分拣器品质过滤

| 类型.方法 | 补丁点 | 作用 | 补丁类 |
|---|---|---|---|
| `CargoTraffic.UpdateSplitter(ref SplitterComponent)` | prefix | 分流器：**有任一逐口规则即完全接管**（返回 `false`，原版循环不执行）；命中的货只进规则口、未命中的只在无规则口按原版 first-fit 顺序分配 | `P5_SplitterFilter` |
| `CargoTraffic.UpdateSplitterAsync(ref SplitterComponent)` | prefix | 同上（并行 tick 版本，两个目标各注册一次；v0.17.1 起与同步版本同语义） | `P5_SplitterFilter` |
| `UISplitterWindow.OnCircleFilterClick(int)` | prefix | 分流器过滤圆的点击入口：捕获分流器与物理口上下文 | `P5_QualityFilterPicker` |
| `UIInserterWindow.OnSelectFilterClick()` | prefix | 分拣器过滤器按钮的点击入口：捕获分拣器上下文 | `P5_QualityFilterPicker` |
| `UIItemPicker.Popup(Vector2,Action<ItemProto>)` | postfix | 在原版物品选取器底部追加页脚（分流器：开关/物品/最低点数；分拣器：最低点数） | `P5_QualityFilterPicker` |
| `UIItemPicker._OnClose` | prefix + postfix | 拆页脚、清上下文，避免泄漏到下一次 picker | `P5_QualityFilterPicker` |
| `CargoPath.TryPickItem(...)` 两个带 filter 的重载（`plain` / `needs`） | prefix ×2（`Priority.High`） | 分拣器 · **传送带来源**：在 5 格取货窗口内按「物品 + 每件点数 ≥ 阈值」选货并自行搬运；无达标货时拒绝本次取货。v0.19.1 把锚点从 `CargoTraffic.TryPickItem` 换到这里——原锚点只覆盖传送带，储物仓/机器产物口/弹射器/发射井走 `PickFrom` 根本没被拦 | `P5_SorterFilter` |
| `PlanetFactory.PickFrom(uint,int,int,int[],out byte,out byte)` | prefix（`Priority.High`） | 分拣器 · **非传送带来源**（v0.19.1）：按 `EFactoryIOTargetType` 分来源——`storage` 只读网格扫描镜像 `TakeTailItems` + `split_inc`、`assembler` 只预测下一件点数（**不消耗产物池点数**，达标即放行给 `P0_PickFrom`）、`ejector`/`silo` 用 `bulletInc`/`bulletCount` 预检；`station`/`lab`/`powerGen` 显式跳过并记进 `bySource=` | `P5_SorterFilter` |
| `InserterComponent.InternalUpdate` / `InternalUpdate_Bidirectional` / `InternalUpdateNoAnim` | prefix ×3 | 建立"当前分拣器 + 其阈值"的线程上下文，避免影响其它建筑取货 | `P5_SorterFilter` |
| `UIInserterWindow._OnUpdate/_OnRegEvent/_OnUnregEvent/OnResetFilterButtonClick` | postfix ×4 | 标题显示 `品质≥N`、Shift+右键循环阈值、重置按钮清阈值 | `P5_SorterFilterUi` |

> `P5_SorterFilter` 是**全或无**：启动时必须凑齐 tick=3/3、belt=2/2、pickFrom=1/1，缺任意一个就 `Disable()` 并整体回退原版取货——避免出现"广告了阈值、但部分来源仍走原版"的假开启。

> **v0.19.2 修复（同属 `P5_SorterFilter`，不改锚点、只改实现）**：① 储物仓分支的 id 映射——此前把**实体 id** 直接当 `storagePool` 下标，原版要求 `entityPool[entityId].storageId` 映射后再取 `storagePool[storageId]`，错用命中空组件、`grids=0`，导致储物仓来源丢品质点数；② 删除 `lastEmptyItem` 缓存门控，阈值/物品过滤变化与"只设阈值不选物品"都能立即生效；③ 新增 `NotifyStorageChange()` 调用，取货后立即刷新仓库界面（此前残留旧货需重开面板）。

#### 4.2.7 物品角标与光环

| 类型.方法 | 补丁点 | 作用 | 补丁类 |
|---|---|---|---|
| `UIStorageGrid.OnStorageContentChanged()` | postfix | 容器/背包内容变化时刷新角标 | `P6_ItemQualityBadges` |
| `UIStorageGrid.OnStorageSizeChanged()` | postfix | 扩容/缩回时同步角标 | `P6_ItemQualityBadges` |
| `UIHandTip._OnUpdate()` | postfix | 手持物品提示角标 | `P6_ItemQualityBadges` |
| 光环渲染（`P5_TenPointHalo` + `P5_HaloAssets`） | 非 Harmony | 自建 MonoBehaviour 与程序生成贴图，每帧选目标并绘制 billboard | — |

#### 4.2.8 喷涂次数（恒为 1 次/件）与品质回收机（v0.19.0）

| 类型.方法 | 补丁点 | 作用 | 补丁类 |
|---|---|---|---|
| `ItemProto.Preload(int)` | postfix | 记录原版 `HpMax`，把增产剂 1141/1142/1143 的可用喷涂次数写成 1 | `P6_SprayCount` |
| `SpraycoaterComponent.Import(BinaryReader)` | postfix | 读档时把旧「喷涂次数」折算成「件数」并清 `extraIncCount`（struct / class 按 `IsValueType` 各注册一次） | `P6_SprayCount` |
| `ABN_ProtoData.CheckProto()` | prefix + postfix | 存档签名校验期间临时还原 12/24/60，避免"数据异常"弹窗 | `P6_SprayCount` |
| `SpraycoaterComponent.InternalUpdate(CargoTraffic, AnimData[], int[])` | **postfix（struct / class 双签名）** | 品质回收机：postfix 读催化剂进料带 cargo，整叠消耗并把点数按 4 点/次灌进原版喷槽；每次 `Refresh` → 改 → `Persist` 写回，避免 struct 副本丢改动 | `P7_Recycler` |

#### 4.2.9 品质筛选随复制与蓝图迁移（v0.19.2 新增，`P6_FilterCopy`）

> **目标**：让四向分流器的**每口规则**（口位 / 允许物品 / 最低品质）与分拣器的**品质阈值**在「建筑设置复制」与「蓝图复制/粘贴」后**各自保留**，而不是落成一台"筛选被清零"的建筑。注意这里迁移的只是**筛选设置**，与建筑自身品质点数（`blueprintQuality` 那套语义）互不相干。

| 类型.方法 | 补丁点 | 作用 | 补丁类 |
|---|---|---|---|
| `BuildingParameters.CopyFromFactoryObject(PlanetFactory,int)` | prefix | **Shift 复制源**：按 `entityId` 把该建筑的分流器每口规则 / 分拣器阈值打包进快照，供随后的粘贴读取 | `P6_FilterCopy` |
| `BuildingParameters.PasteToFactoryObject(PlanetFactory,int)` | postfix | **Shift 复制目标**：把快照里的筛选设置写回到落点建筑并持久化 | `P6_FilterCopy` |
| `PlanetFactory.CopyBuildingSetting(int)` | prefix | **Ctrl+C / Ctrl+V 复制源**：与 Shift 复制共用同一份快照结构（`CopyBuildingSetting` 走的是另一条原版入口） | `P6_FilterCopy` |
| `PlanetFactory.PasteBuildingSetting(int)` | postfix | **Ctrl+C / Ctrl+V 目标**：写回筛选设置 | `P6_FilterCopy` |
| `BuildingParameters.ToParamsArray()` | postfix | **蓝图保存**：在 `BlueprintBuilding.parameters` **尾部**追加 9 个 int 的魔数块（见下），随 `.bp` 文件持久化 | `P6_FilterCopy` |
| `BuildingParameters.FromParamsArray(int[])` | prefix | **蓝图读取**：识别尾部的魔数块并解码成筛选设置；非本 MOD 的蓝图（尾部无魔数块）原样交给原版 | `P6_FilterCopy` |
| `GenerateBlueprintData`（`BlueprintData` 生成） | prefix + postfix | 生成蓝图数据时先清掉上一份残留、写出后再把魔数块塞进每条 `BlueprintBuilding.parameters`（prefix 与 postfix 各一次注册） | `P6_FilterCopy` |
| `BuildTool_BlueprintPaste.CreatePrebuilds()` | prefix | **蓝图两段式（第一段）**：遍历 `blueprint.buildings`，用 `TryReadBlueprintBlock(building.parameters)` 解码每条建筑的魔数块并入队；日志 `bp-stage queued=N buildings=M` | `P6_FilterCopy` |
| `PlanetFactory.AddPrebuildDataWithComponents` | postfix | **蓝图两段式（第二段）**：新落成的预建建筑还没有实体 id，按 `protoId` 把队列里的筛选设置绑到 `prebuildId`；日志 `bp-stage prebuild id=.. proto=.. kind=.. min=..` | `P6_FilterCopy` |
| `PlanetFactory.AddEntityDataWithComponents` | postfix | **蓝图两段式（收尾）**：实体真正落地后把暂存在 `prebuildId` 上的筛选设置写到实体侧表 | `P6_FilterCopy` |

**魔数块格式（9 个 `int`，写在 `BlueprintBuilding.parameters` 尾部）**：

```
[ magic 0x51465042 ('QFPB') | version 1 | kind | protoId | rule0 | rule1 | rule2 | rule3 | min ]
   kind: 1 = 四向分流器（rule0..rule3 = 四个输出口的规则打包）
         2 = 分拣器    （rule0 = 物品/过滤打包，min = 品质阈值，其余保留）
```

- 尾部追加而非新增字段：保证未装本 MOD 的客户端仍能正常读取 `.bp`（原版忽略多余尾部数据），只是筛选设置不生效；
- `version` 为将来格式演进留位；解码时魔数或版本不匹配就整体跳过（等价于"这份蓝图没有筛选设置"），不会破坏原版蓝图；
- 启动日志（全或无）：`[QSystem][filtercopy] armed=True reason=patched 11/11 (BuildingParameters copy/paste/apply + Copy/PasteBuildingSetting + blueprint params/save/load + prebuild stage)`——11 个锚点缺任意一个就整体不生效，回退"复制后筛选清零"的原版行为；
- DEV 探针（仅 `DSPQS_DEV=1`）：`bp-stage`（两段式队列与绑定）、`blueprint block written (save)` / `blueprint block read`（魔数块读写）、`apply-to`（设置落到实体）。

### 4.3 品质曲线表

`SpeedBonusFrom` / `FeatureFactorFrom` 共用同一张 11 项表（`QualityMath.Curves.cs`）：

| 每件点数 | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 累计加成 | 0% | 2.5% | 5% | 7.5% | 10% | 15% | 20% | 30% | 40% | 60% | 100% |
| 倍率（速度/充电/研究/射速/发电/发射） | 1.00× | 1.025× | 1.05× | 1.075× | 1.10× | 1.15× | 1.20× | 1.30× | 1.40× | 1.60× | 2.00× |

- 喷涂机把同一列百分比直接当**概率**使用（0 点 0% … 10 点 100%）；
- v0.18.0 起**制造台升品概率**也查这张表（`QualityMath.UpgradeCurveChance`），只是掷骰次数结构不变（0–6 点单掷、7–9 点双掷、10 点必 +2）；
- 倍数 = `1 + 加成 / 100`，因此 10 点恰好 2.00×；
- 线性模式（`speedBonusMode = "linear"` 常量切换）才会启用 `speedPerPoint`/`maxSpeedBonus`/`chargePerPoint`/`maxChargeBonus` 的旧公式。

### 4.4 线程与主线程约束

- 游戏逻辑在主线程串行 tick（`FactorySystem.GameTick(long,bool)`，`asm:256226`）；`P0_Env` 在该方法的 prefix/postfix 里维护"当前星球/工厂"作用域。
- 无 `factory`/`planet` 引用的组件（如 `AssemblerComponent` 结构体实例）通过该作用域取当前星球。
- 并行设施 tick 若发生在其作用域之外，`QualityEnv` 回退到最近一次串行 tick 的星球，日志带 `fallback=`。
- `QualityRules` 的随机数使用 `[ThreadStatic] Random`（种子 = `TickCount * 31 + 线程 id`），避免多线程共享；离线测试可用 `ResetRandomForTests(seed)` 定种。
- Unity 对象（光环 HUD、角标、分流器/分拣器过滤面板控件）只在主线程读写；存档读写也在原版主线程回调中进行。
- 所有补丁与热路径 `try/catch` → `QualityEnv.WarnOnce(键)`，同一键只报一次，失败回退原版路径。

### 4.5 存档尾块格式

品质数据追加在 `.dsv` 原版数据之后：

```
[原版存档数据][int32 小端 payloadLength][payload: UTF-8 JSON][8 字节 magic]
                                                    magic = 44 53 50 51 53 51 31 00  ("DSPQSQ1\0")
```

- 写入：`QualityBlobCodec.Append(BinaryWriter, byte[] payload)`。**不 Flush / 不 Dispose**——原版在写完数据后从 `fileStream.Position` 回填头部偏移 6 的文件大小，提前 flush 会破坏这一回填。
- 读取：`QualityBlobCodec.TryRead`。
  1. 要求流可 seek；
  2. 在文件尾向前最多 **4 MB** 窗口内倒序查找 magic；
  3. 命中后向前解析 4 字节长度字段，要求 `declared == magicStart - (frameStart + 4)`（自洽校验）；
  4. 倒序扫描保证**最新帧胜出**；
  5. 失败不改变流位置，`reason` 取值：`ok` / `stream cannot seek` / `no trailing data` / `truncated` / `no trailer found`。
- 即使本次没有任何品质数据，也写一个**空的合法 JSON 尾块**（约 12 B 开销 + 约 40 B 头），作为"本存档已被本 Mod 保存过"的标记，避免以后误读残留的 legacy 侧文件。
- **多 Mod 共存**：别的 Mod 可能在本 Mod 之后继续追加自己的尾块；本 Mod 不解释未知尾块，只靠"从文件尾扫描自己的 magic"定位，因此追加顺序无关。
- 原版读取器读到自身数据末尾即停止，因此删除本 Mod 后存档仍可正常读取。

### 4.6 存档 JSON 结构

```json
{
  "version": 1,
  "gameName": "<存档名>",
  "seed": <银河种子>,
  "planets": {
    "<planetId>": {
      "buildings": [ { "e": <entityId>, "i": <inc> } ],
      "pools":     [ { "e": <entityId>, "pi": <poolInc>, "pc": <poolCount> } ],
      "storages":  [ { "e": <entityId>, "b": <baseGrids>,
                       "cells": [ { "g": <grid>, "i": <itemId>, "f": <filter>, "c": <count>, "s": <stackSize>, "q": <inc> } ] } ],
      "splittersV2": [ { "e": <entityId>, "o": [ { "p": <口号 0–3>, "i": <itemId，0=任意>, "m": <最低点数 1–10> } ] } ],
      "sorters":     [ { "e": <entityId>, "m": <最低点数 1–10> } ],
      "recyclers":   [ { "e": <entityId>, "p": <累计点数>, "c": <该机容量> } ],
      "inputs":      [ { "e": <entityId>, "l": <本批原料点数 0–10> } ]
    }
  }
}
```

- **只写非零项**：`inc == 0` 的建筑、空池、空容器、没有规则的分流器口、阈值为 0 的分拣器都不写；
- `storages` 只保存"放不进原版格子"的溢出物品；`splittersV2` 只写有规则的输出口；`sorters` 只写阈值 > 0 的实体（物品 id 仍由原版 `InserterComponent.filter` 保存）；`recyclers`（v0.19.0 新增可选节点）只写账号点数非零的回收机，`p` = 该机账号累计点数（0–`600×4`）、`c` = 该机容量（按点数计 2400），无回收机不写该节点、旧档缺该字段视为空；本轮迭代早期版本的 `"o"`（投递计数）字段**读档一律忽略**；
- `inputs`（v0.19.1 新增的可选节点）：写制造台"本批原料点数"（`l` = 0–10，只写 `level >= 0` 的实体，读回时 `SetInputLevel` 并钳到 0–10），**只写非零项**，旧档缺节点即静默跳过（不升 schema 版本）；
- 旧 `splitters` 节点（v0.13–v0.15 的"品质优先"布尔标记）不再识别：读到只打一条 `[QSystem][save] legacy 'splitters' node ignored` 日志，**不做迁移**（布尔语义无法表达逐口规则，分流器回原版行为直到玩家重新设规则）；
- 分流器/分拣器实体 id 同样受 `1 << 20` 上限约束；载入时规则用 `QualityMath.PackSplitterRule` 重新打包（越界物品 id 归"无规则"，不会出现永远无法匹配、却把货物吞掉的规则）；
- `version` 当前为 1；新增可选字段不升版本——旧档缺字段视为空，旧 DLL 读新档忽略未知字段；
- 载入时 `SaveGuard.ValidateDocument` 校验 `gameName` 与银河种子，**不匹配即丢弃**（防止复制/改名存档串档）；
- 字段容错：`planetId < 0` 跳过；`inc` 读入钳到 0–255；`e <= 0` 跳过；`storages` 要求 `e > 0 && b > 0`，cell 要求 `g >= 0 && itemId > 0 && count > 0`；数字兼容 `long`/`double`/字符串；
- 载入完成后扩容数据按 `PlanetData.factoryIndex` 索引 `GameData.factories`（不是 planetId），并 `StorageGrids.RequestFullSweep()` 重新扩容。

### 4.7 legacy 侧文件迁移

v0.11.10 及更早把品质数据写在 `BepInEx\config\DSPQualitySystem\saves\<槽名>.json`。当前版本**只读不写**这些文件：

1. `GameData.Import` postfix 尝试读尾块；失败（旧档，无尾块）时进入迁移；
2. `QualitySave.LoadLegacy(saveName)` 读取主路径；自动存档槽额外尝试 `_autosave_tmp.json`（自动存档轮转改名后旧 JSON 仍留在保存时的槽名）；
3. 数据只进内存（日志 `legacy sidecar used for '<槽名>'`），**不写回、不删除**——此时 JSON 仍是唯一副本；
4. 该存档下一次保存时，数据写入尾块，`GameSave.SaveCurrentGame` postfix 删除旧 JSON（含 `_autosave_tmp.json`），目录空则移除整个 `saves` 目录；
5. 想立即完成迁移：读档后手动保存一次。

### 4.8 关键原版 API 约束（必须遵守，否则补丁失效或崩溃）

| 事实 | 位置 | 影响 |
|---|---|---|
| `Cargo` 布局：`byte stack`、`byte inc`（整叠总点数）、`short item`、`Vector3 position`、`Quaternion rotation`，`dataLen = 32` | `asm:26911` 起 | 不能给物品加字段，只能在 `inc` 上编码点数 |
| 8 张静态表长度 11、`public static` 非只读 | `asm:26911-26974` | 直接改字段即可全局生效 |
| `AssemblerComponent` 是 struct | — | 补丁用 `ref __instance`；`served[]/incServed[]` 固定 6 长，`requires[]/requireCounts[]` 长度 = 实际原料种数（不补零） |
| `PlanetFactory.PickFrom` 有多个重载 | `asm:217948` 起 | Harmony 必须显式指定参数类型（含 `byte&`），否则取错重载 |
| `InsertInto` 的 `itemInc` 是整批总点数且为 `byte` | `asm:57429` 调用点 | 矿机多件出货必须按件累加后再写，不能逐件乘 |
| `DismantleFinally(Player,int,ref int)` 第三参数是 `ref` 非 `out` | `asm:212533/212540` | 启动自检按 `out` 反射会误报 BYREF/OUT（见第 7 章） |
| `Player.UseHandItems(int,out int)` 在手持数量恰好等于用量时可能返回 0 而实际已消耗 | `asm:170099` 起 | 建造捕获必须用 Prefix 快照兜底 |
| `entityId` 由 `AddEntityDataWithComponents` 返回，`BuildFinally` 是 void | `asm:212456` | 建筑点数的唯一可靠绑定点是该方法的 postfix |
| 喷涂机覆盖式写入：`if (num5*incAbility > cargo.inc) inc = ...` | — | 喷涂只升不降，且每件上限 = 喷涂剂 `Ability` |
| 分馏塔成功率用 `accTableMilli`、产物出货 `inc` 硬编码 0 | `asm:53732/53866` | 分馏塔需要独立的产物池与出货钩子 |
| 时间基准 1 秒 = 60 tick | — | 自转/浮动/呼吸等动画按时间换算 |
| `EFactoryIOTargetType` 高位 `0xFF000000`（Assembler = 33554432） | — | `PickFrom` 自实现用 `id & 0xFF000000` 判类型、`id & 0xFFFFFF` 取索引 |
| 大型采矿机（站点矿机）的 `insertTarget` 恒为 0，物品改由 `StationComponent.UpdateVeinCollection` 搬进 `storage[0].count` | `asm:57414`、`asm:77689` | 普通矿机补丁的 transpiler 站点打不到，必须单独补丁；这条原版路径只搬数量、不写 inc |
| `StationStore.inc` 是该格**总点数**（`int`，不是每件） | `asm:78322` | 站点格写点数 = 原总点数 + 新增件数 × 每件点数 |
| `CargoTraffic.TryPickItem` 只比较 item id，从不看 `Cargo.inc` | `asm:83650/83659` | 分拣器品质阈值必须接管取货（两个重载），只改 `InserterComponent.filter` 无效 |
| `PlayerAction_Inspect.GameTick` 的立即建造块直接扣料、把 inc 丢进丢弃的 `out` 参数 | `asm:132659` 起（立即建造块 `133137–133169`） | 必须在 tick 前缀抢先接管取料（瞬时消费窗口 + 收据）；只靠 `TakeTailItems` 记录会取到背包尾部 0 点栈 |
| 普通建造/蓝图粘贴也可能不经 `Player.UseHandItems`，而由原版从背包尾部取料（典型：大型采矿机 2316） | 构建由 `AddPrebuildDataWithComponents` 落成 | 建造绑定必须按 `prebuild.protoId` 消费收据，单靠 `UseHandItems` 快照会漏 |

---

## 5 数据结构

### 5.1 每星球状态（`QualityData.PlanetQuality`）

以 `entityId` 为下标的一组并行数组，容量按需增长：

| 字段 | 类型 | 含义 | 是否进尾块 |
|---|---|---|---|
| `buildingInc` | `byte[]` | 建筑自带品质点数（0–10） | 是（`buildings[].i`） |
| `poolInc` | `int[]` | 制造台产物点数额 | 是（`pools[].pi`） |
| `poolCount` | `int[]` | 产物池件数 | 是（`pools[].pc`） |
| `inputLevel` | `short[]` | 本批输入品质（-1 = 未设置） | 否（下个批次重算） |
| `pendingLevel` | `short[]` | 本批计算中的产物点数（-1 = 无） | 否（帧内） |
| `wasReplicating` | `byte[]` | 批次开始时的复制状态快照 | 否 |
| `prevProduced` | `int[]` | 每实体 4 槽的产物计数基线（`entityId*4+slot`，-1 = 未观测） | 否（运行时基线） |
| `storageBase` | `int[]` | 容器原版格数（0 = 未知/非容器） | 否（导出时把 `size` 收回该值） |
| `pickedSinceTick` | `int[]` | 本 tick 被取走的产物件数（装配机产物净增量补偿用，读取后清零） | 否（帧内记账） |

另有 `prebuildInc: Dictionary<int, byte>`（预建造点 → 落成点搬迁，帧内）与单条目行星缓存 `_cachedPlanetId/_cached`。

品质过滤规则单独放在 per-planet 侧表 `QualityData.PlanetFilterTable`（`QualityData.QualityFilters.cs`），不占用 `PlanetQuality` 数组：

| 字段 | 类型 | 含义 | 是否进尾块 |
|---|---|---|---|
| `splitterRules` | `int[]` | 分流器逐口规则，下标 `entityId * 4 + 口`；打包为 bit0–15 物品 id、bit16–23 最低点数，0 = 无规则 | 是（`splittersV2`） |
| `sorterMin` | `byte[]` | 分拣器最低点数（0 = 原版，不下发阈值） | 是（`sorters`） |

侧表写入只发生在主线程；读取走无锁路径（`FiltersForRead`：无规则时直接短路，`filterEpoch` 变化时刷新 `[ThreadStatic]` 缓存），保证并行 tick 安全。

### 5.2 容量与边界

| 项 | 规则 |
|---|---|
| `Capacity` | `buildingInc.Length` |
| 扩容 | `EnsureCapacity(capacity)`：新容量 = 旧长 < 64 时 64，否则旧长 × 2，不足则取 `capacity` |
| 新元素初始化 | `inputLevel`/`pendingLevel` 填 -1；`prevProduced` 填 -1（首次观测记为基线，避免把读档产量当新增） |
| entityId 上限 | `MaxEntityId = 1 << 20`（1,048,576）；越界 id 一律拒收，防止 `entityId + 1` 溢出与哨兵 id。品质过滤侧表使用同一上限（`FilterMaxEntityId`），数组按需增长 |
| 产物槽数 | `ProducedSlots = 4` |
| 清理 | `ClearEntity` 重置该 id 全部数组；`RemovePlanet`/`ClearAll` 同时失效行星缓存 |
| 写建筑点数 | `SetBuildingInc` 钳到 `Cargo.kIncLevelMax`，并通知观察者让容器扩容规则重算 |

### 5.3 内存与持久化边界

- **容器额外格只在内存（v0.19.2 起功能停用、逻辑保留）**：`StorageGrids` 维护 `baseGrids` 与实时 `size`；保存前 `P4_StorageGrids` prefix 把所有容器 `size` 收回原版值，postfix 恢复，finalizer 兜底；只有放不进原版格子的物品进尾块 `storages`。`storageQualityGrids=false` 后不再产生新的额外格，尾块 `storages` 里的旧数据读档时静默忽略（只 `WarnOnce`），写出端也不再追加该节点。
- **额外格物品卸载后丢失**：这部分物品只存在于尾块，删除 MOD 后原版读档不会加载它们（存档本身可读，见第 7 章）。
- **品质过滤规则**逐台持久化（`splittersV2`/`sorters`）；0 规则、0 阈值不写。
- **不持久化的运行时字段**：`inputLevel`/`pendingLevel`/`wasReplicating`/`prevProduced`/`storageBase`/`prebuildInc`——都由原版状态或帧内上下文重建。

### 5.4 存档层职责

| 组件 | 职责 |
|---|---|
| `Save\JsonLite.cs` | 无第三方依赖的 JSON 生成/解析（写入端与读取端共用，离线测试也链接它） |
| `Save\SaveGuard.cs` | schema 校验（`version`/`planets` 结构）与身份校验（`gameName` + 银河种子） |
| `Save\QualityBlobCodec.cs` | 尾块读写（magic、长度自洽、4 MB 回扫）；**不引用 BepInEx/Unity**，供离线测试直接链接 |
| `Save\QualitySave.cs` | 装配 `BuildDocument`/`ApplyDocument`、嵌入与读出、legacy 迁移与删除、容量对齐 |

---

## 6 测试与验证

### 6.1 离线测试工程

- 位置：`tests\OfflineTests\`（独立 csproj，主工程通过 `<Compile Remove="tests\**" />` 排除）；
- 只链接纯逻辑代码（`QualityMath*`、`QualityData*`、`QualityRules`、`Save\*`），**不需要游戏**；
- 运行：`tests\run-tests.ps1` → `dotnet run --project tests\OfflineTests\OfflineTests.csproj -c Release`；
- 当前规模：**88 条实际执行的 `Check(...)` 调用**（v0.19.2；输出 `ALL PASS (88 checks)`；`Program.cs` 中搜 `Check(` 会多出几处命中，来自函数定义与辅助重载），输出 `[TEST] <name> ... PASS/FAIL`，结束打印 `ALL PASS (<n> checks)` 或 `FAILURES: n`；
- 用例分文件：`Checks.P3/P4*/P5*/SaveBlob/CfgText` 为旧域；v0.16.x 新增 `Checks.P5Miner.cs`（大型采矿机站点差量）、`Checks.P5Fix.cs`（产物净增量补偿与立即建造取整）、`Checks.P5QualityFilter.cs`（分流器规则打包/路由/兜底）、`Checks.SplitterFilter.cs`（逐口规则与分拣器阈值的持久化往返）；v0.17.0 新增消费窗口/收据（`ConsumeWindowActive`/`ReceiptPerItem`/`PreferBindingValue`/`ReceiptItemMatches`）与背包路径建造绑定、选取器页脚控件模型的用例；v0.17.1 新增 6 条过滤回归（`PerItemPoints` 整栈/单件换算、规则口拒收低品质、脏 `outFilter` 不泄漏到规则口、未命中只走无规则口、分拣器窗口按每件点数选货/拒取、阈值 0 与原版等价）；v0.18.0 新增 4 条（曲线升品概率表、曲线掷骰形状、矿机必中、补付每件点数换算）；v0.19.0 新增 `Checks.P7Recycler.cs`（4 组：每 4 点 = 1 次喷涂次数的换算与账号余数、低于阈值的整叠原样放行、满槽停止回收与有空间后恢复、`recyclers` 节点 `{"e","p","c"}` 往返与旧 `"o"` 字段忽略）；v0.19.1 新增 `Checks.V0191.cs`（`QualityMath.BatchInputLevel` 的四舍五入、负投入/0 除数、钳到 `maxLevel`）与 `Checks.P6SprayUses.cs`（喷涂次数分级表 0–10 全表与分段边界），86 → **94 checks**；**v0.19.2 撤回 `Checks.P6SprayUses.cs` 的那 6 条喷涂次数分级 checks（随该功能的分级逻辑一并回滚），并从 `Checks.V0191.cs` 移除对应的分级表断言，94 − 6 = 88**，新增的 `P6_FilterCopy` 魔数块编解码与 `P5_SorterFilter` 的 storageId 映射用例在离线侧只覆盖纯逻辑部分。

| 覆盖域 | 代表用例 |
|---|---|
| 产物池守恒 | 100 万次随机 `AddPool`/`TakeOne` 后总量守恒；池边界与上限 |
| 规则与钳制 | 配置钳制、升品模式、曲线表、线性回归、随机数不受只读日志扰动 |
| 存档编解码 | 1–65536 字节往返；拒绝空/截断/坏长度/坏 magic；原版头 + 200 KB 前缀；双追加只取最新帧；共享尾块 |
| 升品/矿机 | 曲线升品概率（1..10 点查表、7/8/9 双掷、10 点必 +2、分馏塔单掷）、2%/8% 掷骰分布、100%/6% 旧线性分布、矿机多段、每 2 点必定 +1、大型采矿机站点差量与每件上限、分馏塔输入重算与池回滚 |
| 性能加成 | 速度因子、充电因子与充电上限曲线、A 类五件套因子矩阵、喷涂概率/加成/事件 |
| 隔离与消耗 | 隔离合并、`split_inc`、整理重组、消耗优先级选择、容器 gate |
| 仓库与品质过滤 | 额外格公式、缩回；分流器逐口规则打包/路由/兜底/全满留货、分拣器阈值选货、尾块 `splittersV2`/`sorters` 往返、无规则时与原版一致 |
| 产物与立即建造 | `ProducedDelta` 净增量补偿、`InstantBuildPerItem` 取整、立即建造对不上时的退化路径、未付款预建补付的每件点数换算（`DeferredPayPerItem` 与立即建造同规则） |
| 视觉数学 | 光环半径/像素/alpha/自转、标记样式解析、角标点数/可见性/样式/色档 |
| 品质回收机（v0.19.0） | 每 4 点 = 1 次喷涂次数与账号余数、10 点 = 2 次 + 余 2、单件低于阈值整叠放行、满槽停止回收、`recyclers` 节点 `{"e","p","c"}` 往返与旧 `"o"` 字段忽略、旧档缺 key 视为空 |
| 批次点数（v0.19.1 / v0.19.2） | `BatchInputLevel(incServed, served, maxLevel)` 四舍五入：`29/5→6`、`10/4→3`、`7/2→4`、`9/4→2`、`0/1→0`、负投入与 `served<=0` 归 0、结果钳到 `maxLevel`；**v0.19.2 起喷涂次数分级表那组断言随功能回滚撤出，合计 94 → 88 checks** |
| 配置文本 | `CheckCfgText`：16 键布局、旧键计数与丢弃、已有取值保留、中文注释存在、`null` 值回落默认、键名大小写不敏感、缺键/多键均触发重写 |

### 6.2 启动自检（`Diag\StartupAudit.cs`）

`LDB.items` 就绪后自动执行，日志以 `[Audit] ---- reflection self-check begin/end ----` 包裹：

- 用 `CheckMethod` 校验全部补丁依赖的原版方法签名（`PickFrom`、`InsertInto`、`UseHandItems`、`AddEntityDataWithComponents`、`DismantleFinally`、`GameTick`、`FractionatorComponent.*`、`SetPCState`、`DoDismantleObject`、`AddItemStacked`、`TakeTailItems`、`Sort`、`InternalUpdateResearch`、6 个 `TurretComponent.Shoot_*`、5 个 `EnergyCap_*`、`EjectorComponent`/`SiloComponent`、`SpraycoaterComponent`、`Export`/`Import`、`UpdateSplitter`/`UpdateSplitterAsync`、`UpdateVeinCollection`、`BuildFinally`、`PlayerAction_Inspect.GameTick`、`TryPickItem` 两个重载、`InserterComponent.InternalUpdate*` 三个版本、`UISplitterWindow.OnCircleFilterClick`、`UIInserterWindow.OnSelectFilterClick`、`UIItemPicker.Popup`/`UIItemPicker._OnClose`、`UIInserterWindow.*`、`UIStorageGrid.*`、`UIHandTip._OnUpdate`、v0.19.0 的 `ItemProto.Preload(int)`、`SpraycoaterComponent.Import(BinaryReader)`、`SpraycoaterComponent.InternalUpdate`、`ABN_ProtoData.CheckProto`（可选，缺失只警告）等）；
- `CheckItemAbilities` 校验增产剂 1141/1142/1143 的 `ItemProto.Ability` = 1/2/4，并打印 verdict；
- 打印 Cargo 常量与各补丁类的 `Describe()`（armed / NOT ARMED、是否注册、effective 参数），含大型采矿机、立即建造、分流器过滤、分拣器过滤、选取器页脚、**未付款预建补付、供电范围、喷涂次数、品质回收机、品质筛选复制（v0.19.2）**等补丁；启动日志里的 `patchedMethods=18` 是 `PatchAll` 时点的**特性类**计数（另有 22 个手工补丁类，含 v0.19.2 新增的 `P6_FilterCopy`）；默认全内置开启时覆盖的方法数以 §4.2 各表为准（**v0.19.2 统计：85 次方法级注册 / 去重 78 个独立方法**，见 §4.1/§4.2）；
- 已知局限：`DismantleFinally` 第三参数是 `ref` 而早期自检按 `out` 反射，会输出一条 `BYREF/OUT MISMATCH` 误报——**不影响补丁本身**，以自检输出的其他项为准。

### 6.3 实机验证

- 方式：离屏窗口运行 `DSPGame.exe -popupwindow`，加载测试插件 `DSPQSDebugHarness`（v8），通过其命令队列（HTTP `POST /command`，如 `set_pause`）驱动游戏，配合 `QSTest` 测试存档（`沙盒.dsv` 副本，planet 103）；
- 测试插件与测试存档**不进发布包**；
- 核对手段：游戏日志（`[QSystem]`/`[Audit]`/探针）+ 面板实际表现（角标、光环、选取器页脚、存档尾块大小变化）；
- v0.17.0 实机核对要点（已通过）：分流器与分拣器两条入口都能弹出带页脚的原版物品选取器（页脚控件层 `layer = 5`、按钮 `onClick` 非空即点击生效），切换开关/步进点数/点选物品都会落规则（日志 `[QSystem][filterpicker] splitter port=0 on=True item=1002 min=4`、`sorter min=5`），`save` → `loadfull` 后尾块 `splitters=1 sorters=1` 回读一致；
- 离线测试与实机验证都通过后才打包。
- v0.18.0 实机核对要点（**已通过**，2026-10-31 静默调试）：①电网面板容量随发电机品质按曲线放大（射线接收站注入 4 点后 `720,000,000 → 744,000,005`，日志 `[QSystem][powergen] output bonus active … factor=1.10`）；②电力塔类建筑覆盖圈与拉线距离随点数放大（重载后 `[QSystem][powerrange] eid=38 node=1 points=4 factor=1.1 conn=58.85 cover=29.15`，即 prefab 基准 × 1.1）；③材料不足先立起的白色虚影在材料补齐落成后带有点数（用户实机复测确认已修复）；④矿机 2 点必 +1（`[Probe][F12] lastMinerCall=eid=132 level=2 minerChancePerPoint=100`）；⑤制造台升品概率明显提高（`[QSystem][roll] bld=10 mode=2 pooled=2`、`bld=4 mode=0`）。
- v0.19.0 实机核对要点（喷涂次数与回收机**均已通过**，用户 2026-10-31 实机确认）：①喷射次数改成 1（日志 `[QSystem][spraycount] armed … 1 spray per item`），旧档三台喷涂机自动折算（`604 -> 11 items`、`341 -> 6`、`658 -> 11`）；②品质回收机：给喷涂机接上**催化剂进料带**、放**带点数（≥1 点/件，含 Mk.I 的 1 点）**的物品后，该物品消失、喷槽喷涂次数上涨，probe 出现 `mode=harvest`（10 点物品 → 2 次喷涂 + 余 2 点），余数存读档保留、拆机 1200 tick 后 `pruned` +1；③验证通过后按用户反馈把阈值由 3 降到 **1**（见 §7.11），0 点未喷涂货物仍整叠放行。
- v0.19.1 实机核对要点（**历史记录**；其中 ②「喷涂次数分级」与 ⑤ 的 94 checks 已于 v0.19.2 回滚/作废，原文保留备查）：①读档后制造台探针不再显示 `inputLevel=-1` / `in=0 pooled=2`，而是 `inputLevel=6`、`in=6 pooled=8`（`DSPQS_DEV=1` 时看 `[QSystem][assembler][batch] eid=… planet=… lvl=… s0=…`，`lvl` 应等于读档前那一批的原料点数）；②喷涂次数分级：把不同点数的增产剂分别插进同一台喷涂机，件数净增 = 分级表值（10 点件 → 10 次），启动行 `[QSystem][sprayuses] armed=… patch=insert+belt+takeback … ledger(planets=… coaters=… items=… uses=…)`；③取回不复制：反复"插入 → 取回"后罐内次数与背包件数守恒，取回件数 **≤ 投入件数**；④分拣器在**储物仓 / 机器产物口 / 弹射器 / 发射井**都生效（`[QSystem][sorterfilter] armed: … tick=3/3 belt=2/2 pickFrom=1/1`，`Describe()` 的 `bySource=[storage:… assembler:…]` 有计数），`station` / `lab` / `powerGen` 不生效且日志写明跳过原因；⑤离线测试 `ALL PASS (94 checks)`。
- **v0.19.2 实机核对要点（①② 已通过，用户 2026-10-31 21:24 确认）**：① **分拣器从储物仓抓货到传送带，品质点数成功保留**（此前 `storageId` 映射错导致 `grids=0` 丢点数）；② **蓝图复制出来的分拣器 / 四向分流器成功保存各自的品质筛选设置**——日志证据 `bp-stage queued=4 buildings=20` + 4 条 `bp-stage prebuild … min=6/8/7` + 四向分流器的 `kind=1`；③ **仓库界面不再崩溃**（不再出现 `System.IndexOutOfRangeException at UIStorageGrid._OnUpdate()`），**也不再显示已取走的旧货**（`NotifyStorageChange()` 生效）；④ **储物舱格数不再随品质点数变化**（`[QSystem][storage] NOT ARMED (storageQualityGrids=false; vanilla containers)`）；⑤启动自检应能看到 `[QSystem][sorterfilter] armed: sorter quality threshold is enforced on tick=3/3 belt=2/2 pickFrom=1/1` 与 `[QSystem][filtercopy] armed=True reason=patched 11/11 (…)`；⑥离线测试 `ALL PASS (88 checks)`。

### 6.4 日志关键字

| 关键字 | 内容 |
|---|---|
| `[QSystem]` | 插件启动、配置、补丁注册结果、规则宣告、错误 |
| `[QSystem][speed]` / `[charge]` / `[lab]` / `[turret]` / `[powergen]` / `[powerrange]` / `[launcher]` / `[sprayer]` | 八个加成的 armed/effective/debug 状态 |
| `[QSystem][deferred]` | 未付款预建补付窗口的 armed 状态、`payments`/`bound` 计数（v0.18.0） |
| `[QSystem][spraycount]` | 喷涂次数 = 1 的 armed 状态（含 preload / import / signature-guard 三个钩子是否就位）与旧档折算日志（`legacy coater (entity N, item 1143): 604 vanilla spray times (60/item) -> 11 items`）（v0.19.0）；v0.19.1 起这只是 `HpMax` 基准，实际每件次数见 §2.11 与 `[QSystem][sprayuses]` |
| `[QSystem][recycler]` | 品质回收机的启动自检与计数（`armed=… mode=inc-feed`、`pointsPerSpray=4`、`minPerItem=1`、`accounts=`/`carried=`、`selfCheck=ok`）、probe 诊断行（`[QSystem][recycler][probe] … mode=harvest\|sprayer\|idle reason=… account=…`）、回收成功行（`recycled coater=… points=+… sprays=+…`）与 `effective=`/`pruned=`（v0.19.0） |
| `[QSystem][dismantle]` / `[isolation]` / `[consume]` / `[splitterfilter]` / `[filterpicker]` / `[miner]` / `[build]` / `[badges]` / `[halo]` / `[save]` | 各功能状态；过滤规则 armed/路由/picked/blocked；大型采矿机与立即建造捕获；角标与光环；尾块读写 |
| `[QSystem][storage]` | 储物仓品质格数：**v0.19.2 起固定打印 `NOT ARMED (storageQualityGrids=false; vanilla containers)`**（功能停用、货仓格数恒为原版）；旧版另有 `armed` / `rule armed` / `expanded` / `save shave` / `restored` / `skipped` 等行，仅见于 ≤ v0.19.1 |
| `[QSystem][filtercopy]` | **v0.19.2 品质筛选复制**：启动全或无 `armed=True reason=patched 11/11 (BuildingParameters copy/paste/apply + Copy/PasteBuildingSetting + blueprint params/save/load + prebuild stage)`；蓝图两段式 `bp-stage queued=N buildings=M`（预建阶段解码入队）与 `bp-stage prebuild id=.. proto=.. kind=.. min=..`（按 protoId 绑 prebuildId）；魔数块写入/读取 `blueprint block written (save)` / `blueprint block read`；设置落到实体 `apply-to` |
| `[QSystem][sorterfilter]` | 分拣器阈值的 armed 状态（`armed: sorter quality threshold is enforced on tick=3/3 belt=2/2 pickFrom=1/1`）、`Describe()` 计数（`picked` / `blocked` / `skipped` / `ticks` / `pathCalls` / `pathMin0` / `pickCalls` / `pickMin0` / `scanned`）与**按来源**拆分的 `bySource=[belt:… storage:… assembler:… ejector:… silo:…]`；v0.19.1 起才有 `pickFrom=` 与 `bySource=`（v0.18.0 只有传送带那一路），v0.19.2 修好 `storage` 分支的 `entityId → storageId` 映射并移除 `lastEmptyItem` 门控 |
| `[QSystem][storage-pick]`（DEV 探针，`DSPQS_DEV=1`） | v0.19.2 储物仓取货逐件判定：把 `entityId → storageId` 映射、目标网格的 `grids`/点数与阈值比较结果打出来，用来核对"仓库 → 传送带丢点数"是否已修好；同批探针还有 `storage-probe`（网格/映射诊断）、`pick-return`（回填的 item/stack/inc）、`inserter-hold`（分拣器持货窗口）、`stack-insert2` / `stack-insert2b`（整叠插入两条路径）、`cargo-insert` / `insert-into`（物品写入路径） |
| `[QSystem][assembler][batch]` | 制造台"本批原料点数"的 DEV 探针：`eid=… planet=… lvl=… s0=<served>/<incServed>/<requireCounts>`——用来核对读档后 `lvl` 是否被 `inputs` 节点恢复、以及四舍五入后的取值（v0.19.1） |
| `[QSystem][config]` | cfg 自愈重写：`rewrote cfg with cleaned Chinese layout (16 keys, dropped N legacy keys)` |
| `[QSystem][roll]` | 升品判定只读记录（开发者模式，每建筑前 20 条） |
| `[Probe]` / `[Audit]` | 探针输出 / 启动自检 |

---

## 7 兼容性与已知限制

### 7.1 存档与卸载

- 删除 MOD 后原版仍可正常读档：品质数据在 `.dsv` 尾部，原版读取器读到自身数据末尾即停。
- **容器额外格里的物品会丢失**（它们只存在于尾块），存档本身可读；原版格子里的物品不受影响。
- 存档身份校验（存档名 + 银河种子）不通过时品质数据被丢弃——丢点数无害，套错会静默腐坏存档。
- 尾块可与其它 Mod 的尾块共存，追加顺序无关。
- 品质过滤规则同样只存在于尾块：删除 MOD 后分流器/分拣器回原版行为，规则不会迁移到别处（存档本身可读）。

### 7.2 视觉

- 物品角标：建筑产物悬停预览（装配机/熔炉顶部大图标）**不显示**角标，因为该图标只是配方预览、没有真实 inc 数据；要看产物品质请看同一窗口的产物格或容器面板。
- 角标：200% UI 缩放或特殊分辨率下可能与原版堆叠数字轻微重叠，可用 `itemQualityBadgeStyle = "dot"` 缓解。
- 机甲燃料/弹药格、物流塔内部没有独立 `UIStorageGrid`，不显示角标。
- 光环：120m 外不绘制、同屏最多 64 个（按距离取近），密集基地远处建筑没有标记。

### 7.3 隔离

- 只覆盖建筑类物品与通用 `Default` 容器；物流塔内部、机甲各专用格、过滤容器、储液罐保持原版平均语义（结构上无法逐件保留点数）。
- 界面手动拖拽/拆分直接写 GRID，绕过补丁，可能把不同点数合并回一格。
- 交付包、`AddItem` 指定格路径仍按原版合并。

### 7.4 优先消耗

- 只作用于建筑物品消耗：手持建造、蓝图粘贴、背包路径建造（如大型采矿机 2316）与 Shift 立即建造（v0.17.0 起）；普通取物、分拣器、物流塔、容器进出仍是原版顺序。
- 落成点数以**消费收据**为准：优先消耗逻辑取料时留下收据（物品 id + 每件点数），建造落地时按 `prebuild.protoId` 取走；收据取不到时退回同帧 `TakeTailItems` 记录，再对不上按 0 点落成（仍会正常建造）。
- 换手时原手持整叠会放回背包，可能多占一格。
- 材料不足的未付款预建：v0.18.0 起由 `P1_DeferredPay` 观察建造模块的补付过程，把实际被扣物品的点数写进该预建，落成点数与"材料当场充足"的建造一致；补付窗口外的其它取料路径不受影响。
- 未付款预建的补付由原版 `ConstructionModuleComponent.PlaceItems` 完成，**一次全额付清**（原版内部断言），所以该路径下不会做"优先消耗最高品质"的换格/拆点，只会如实继承实际被扣的那批物品的点数。
- 供电范围倍率写在 `PowerSystem.nodePool[]` 的持久字段里（随存档序列化），实现按 `prefab × 曲线倍率` 幂等重算：同一栋建筑反复套用不会累乘；拆除重建按新点数重算。
- 收据是**同帧**机制：连续快速建造或多栋同帧落成时极端情况下可能取不到收据而退化为原版行为；`protoId` 匹配保证不会误用其它建筑的收据。

### 7.5 拆除返还

- 只覆盖 GUI 拆除路径；一次拆多栋重叠建筑时，点数作用于整栈。

### 7.6 矿机与分馏塔

- 矿机期望产出随点数呈平方增长（判定次数 × 单次概率）。
- 大型采矿机（站点+采集器）的判定挂在站点本身（按 `storage[0].count` 差量），**站点格里的点数从加载起算**：读档/进入星球时已在站点里的存量按当次差量递增，不追溯历史；站点被拆除时点数随 `ClearEntity` 一并丢弃。
- 分馏塔：品质同时影响成功率（原版 `accTableMilli`）与产物品质，属双收益；分馏塔不享受"额外升品"与"必 +2"。

### 7.7 物流塔充电

- 轨道采集器/矿脉采集器（`isCollector`/`isVeinCollector`）不调用 `SetPCState`，不受充电加成。
- 若恰好在加成生效的同一帧存档，读档后首 tick 最多多充一 tick 的电量（字段每 tick 由原版重算，自我修正）。

### 7.8 分流器与分拣器品质过滤

- 分流器：**只要该分流器有一条规则，原版 `UpdateSplitter` 就不再执行**（Prefix 返回 `false`），原版全局 `outFilter` 与 first-fit 顺序都无法把货送进规则口；无规则的分流器完全交还原版。若官方改动 `SplitterComponent` 的输入/输出槽位语义，接管逻辑可能失效（异常时自动 `return true` 回退原版并 `WarnOnce`）。
- 规则口全满时货物留在原输入皮带（背压），可能堵住一条输入——这是设计取舍：宁可不流，也不混品质。
- 规则里的物品只是"该口允许的物品"，**不检查下游建筑是否接收**。
- 顶部压储物（`topId != 0`）的分流器不实现原版"货入顶部储物"分支：进不了输出的货留在皮带。普通地面四向分流器 `topId == 0`，不受影响。
- 两个过滤入口都复用原版物品选取器页脚（自建面板已在 v0.17.0 删除）；页脚只在 MOD 打开的选取器里出现。
- 物理口与 `beltId` 在放置时绑定：搬动/旋转分流器会重建实体，规则按实体 id 保存，需要在面板里重新确认。
- 分拣器阈值 v0.19.1 起覆盖传送带（`CargoPath.TryPickItem` 两个带 filter 的重载）与 `PlanetFactory.PickFrom` 的 `storage` / `assembler` / `ejector` / `silo` 四类来源；`station` / `lab` / `powerGen` 明确不覆盖（原因见 §2.8），`CargoPath.TryPickFuel`（电厂抓燃料）也保持原版；比较的是**每件**点数（`inc / stack` 向下取整），不是整栈总点数；`assembler` 来源只做"下一件点数不足就拦"的**预测**，不消耗产物池点数。
- v0.15 及更早的「品质优先」布尔节点不再迁移：旧存档读入后忽略该节点（打一条 `legacy 'splitters' node ignored` 日志），分流器回原版行为直到玩家重新设规则。
- **v0.19.2 起筛选设置可复制**：分流器每口规则与分拣器阈值会随 Shift 复制、Ctrl+C / Ctrl+V 与蓝图复制/粘贴迁移（§4.2.9 `P6_FilterCopy`）。边界：蓝图里存的是 MOD 追加在 `BlueprintBuilding.parameters` 尾部的 `QFPB` 魔数块，**未装本 MOD 的客户端放同一份蓝图仍能正常落成，只是筛选设置不生效**；被复制/粘贴的两台建筑若 `protoId` 不同（分流器 ↔ 分拣器）则整体跳过，不会串味；"建筑自身品质点数"与筛选设置是两件事，前者仍走原版语义。

### 7.9 仓库扩容（**v0.19.2 起停用**）

- **状态**：`QualityConfig.StorageQualityGridsValue = false`，功能停用、行为等同删除；启动日志固定 `[QSystem][storage] NOT ARMED (storageQualityGrids=false; vanilla containers)`。存留代码只为兼容旧存档（尾块 `storages` 的溢出数据读档静默忽略、只 `WarnOnce`）。下面两条是停用前的行为记录。
- 只作用于通用 `Default` 容器；物流塔内部、机甲格、过滤容器、储液罐不扩容。
- 点数下降/功能关闭后，容器要等额外格搬空才缩回（期间可能保留几格空位，最长 30 帧重试一次），不丢物品。

### 7.10 运行时防护

- `entityId` 超出 `1 << 20` 的写入被拒收（组装机预热/拆除/对象池复用帧可能出现哨兵 id），日志每次启动最多一条。
- 并行设施 tick 的星球兜底可能产生 `fallback=` 日志，属预期。
- 所有补丁失败均回退原版路径，不会中断游戏逻辑。

### 7.11 品质回收机与喷涂次数（v0.19.0）


- **回收机要通电**：原版 `consumerPool` / `powerRatio > 0.1`，断电不回收；回收占用的是喷涂机的**催化剂（增产剂）进料带**那一侧，物品带那一侧照常喷涂、互不影响。
- 只收**单件点数 ≥ `MinPerItemValue`（默认 1，即任意带点数的货物）**的物品；未喷涂的 0 点货物整叠原样放行（不消耗）。阈值**不是 cfg 键**，是 `Patches\P7_Recycler.cs` 的内部开关 `MinPerItemValue`（默认取 `QualityRecyclerMath.DefaultMinPerItem = 1`），想回到旧行为把它改成 `3`（只收 Mk.III 喷过的）后重编译。默认取 1 的理由：**兑换本身是亏损的**——4 点只换 1 次喷涂次数，而 1 次喷涂最多值 3 点，没有套利空间，阈值只需用来放行 0 点货物。
- **点数进罐、不是产出物品**：整叠物品被消耗销毁，产物是原版喷槽里的**喷涂次数**（4 点/次，物品 1143），不会在下游传送带产出任何物品；喷槽满 **600 次**时停止回收（probe `mode=tankFull`），消耗腾出空间后自动恢复回收；超出容量的点数留在账号。
- 每台机的账号（`0..600×4` 点）进存档 `recyclers` 节点（`{"e","p","c"}`：累计点数 / 该机容量）；旧档缺该 key 直接跳过，本轮迭代早期版本遗留的 `"o"`（投递计数）字段读档一律忽略；拆机后账号保留 `1200` tick 才被 `pruned` 清理。
- 与其它"增产剂可用次数"类 MOD 共存时会互相覆盖：两者都以 `ItemProto.HpMax` 为准，**后加载者生效**；本 MOD 只读 `Cargo.inc`、不改 `ResultCounts`。
- 喷涂次数 = 1 是**全局改写**：任何复用 1141/1142/1143 `ItemProto` 的第三方内容都会变成 1 次/件；自建 `ItemProto` 的 MOD 不受影响。
- 旧档迁移只读档时做一次（`SpraycoaterComponent.Import` postfix），折算结果写回原版次数字段；旧版残留的"超大次数"罐会按 `ceil(次数 / 原版次数)` 折算成件数（日志打印 `-> N items`）。

---

## 8 构建与发布

### 8.1 环境依赖

| 项 | 说明 |
|---|---|
| .NET SDK | 通过 `tools\dspenv.ps1`（`DSP_TOOLS_DIR`）或 `DSP_DOTNET` 环境变量提供 |
| .NET Framework 4.7.2 引用程序集 | 由 `Microsoft.NETFramework.ReferenceAssemblies 1.0.2` 自动还原 |
| 游戏程序集 | 从 `GameDir\DSPGAME_Data\Managed\` 取 `Assembly-CSharp.dll` 与 Unity 引用；缺失时回退 `tools\unity-refs` |
| 离线还原 | `nuget.config` 只保留本地源 `tools\nuget-local`（内含所需 nupkg），无网络也能还原 |
| NuGet 依赖 | `BepInEx.Core 5.4.21`、`DysonSphereProgram.GameLibs 0.10.35.29088-r.0` |

### 8.2 `build.ps1`

- 默认 `Release`；`GameDir` 解析顺序：`-p:GameDir` 参数 → `$env:DSP_GAME_DIR` → 4 个常见 Steam 安装路径（`C:\Games\Steam…`、`C:\Program Files (x86)\Steam…`、`D:\SteamLibrary…`、`E:\SteamLibrary…`），并校验 `DSPGAME_Data\Managed\Assembly-CSharp.dll` 存在；
- 找不到游戏目录时走"无 GameDir 构建"（csproj 回退 `tools\unity-refs`）；
- 未传 `GameDir` 但 csproj 的 `CheckUnityRefs` 目标缺引用时会直接报错并提示三种解决方式；
- 输出标记：`BUILD OK` / `BUILD FAILED`；
- `-Deploy`：复制 DLL 到 `<GameDir>\BepInEx\plugins\DSPQualitySystem\`，**复制后比对 SHA256**，一致才打印 `DEPLOY OK` 与 `DEPLOY SHA256`，否则 `DEPLOY FAILED`。

### 8.3 `tests\run-tests.ps1`

先 dot-source `tools\dspenv.ps1`，再 `dotnet run` 离线测试工程；退出码 0 = `TEST PASS`，1 = `TEST FAILED`。

### 8.4 `tools\package.ps1`

1. 用正则从 `Plugin.cs` 读 `VERSION = "…"` 作为版本号（当前 `0.17.0`）；
2. 默认先执行 `build.ps1 -Deploy`，并要求输出包含 `DEPLOY OK`；
3. 把 `bin\<Config>\DSPQualitySystem.dll` 与 `README.md` 收进 `dist\DSPQualitySystem-<version>.zip`；
4. 打印 `PACKAGE OK` / `VERSION` / `SIZE` / `SHA256` / `DLL SHA256`，失败打印 `PACKAGE FAILED`。

### 8.5 发布包与校验

- 运行侧文件：`BepInEx\plugins\DSPQualitySystem\DSPQualitySystem.dll` 与 `BepInEx\config\dsp.qualitysystem.cfg`；
- 交接包附带 `SHA256SUMS.txt`，安装后可用哈希校验 DLL 与配置是否一致；
- 发布前应同时满足：离线测试 `ALL PASS`、启动自检无 MISSING、实机验证通过。

---

### 8.6 R2 / Thunderstore 发布包

- 脚本：`tools\pack-r2modman.py`（Python；源文件 `tools\r2\`：`manifest.template.json`、`icon.png`、`README.md`、`CHANGELOG.md`）。
- 版本号默认读 `DSPQualitySystem.csproj` 的 `<Version>`，可用 `--version` 覆盖；DLL 默认取 `bin\Release\DSPQualitySystem.dll`。
- 包内布局：`manifest.json`、`icon.png`(256x256)、`README.md`、`CHANGELOG.md`、`BepInEx/plugins/DSPQualitySystem/DSPQualitySystem.dll`。
- manifest 校验（脚本内置）：`name` 仅 `[A-Za-z0-9_]`、≤64；`version_number` 为 `Major.Minor.Patch`、≤16；`description` ≤250 字符且单行；`website_url` 空或 http(s)、≤255；`dependencies` 必须 `Namespace-Name-Version`。
- 依赖声明：`xiaoye97-BepInEx-5.4.17`、`CommonAPI-CommonAPI-1.6.7`（DLL 本身只引用 BepInEx/0Harmony/Assembly-CSharp/Unity 模块，CommonAPI 为社区惯例与 README 口径）。
- 产物：`dist\DSPQualitySystem-<version>-r2modman.zip`；zip 条目时间戳固定（2026-01-01），重复构建哈希稳定。
- 图标：戴森球「增产剂 Mk.III」图标（从游戏内储物仓截图抠图）+ 异星工厂 `data\quality\graphics\icons\quality-legendary.png` 传奇品质角标合成，见 `tools\r2\icon.png`。

---

## 9 版本历史

| 版本 | 核心变化 |
|---|---|
| v0.11.10 | 10 点建筑头顶金色光环（程序生成空心环形贴图、面向相机、自转 + 浮动 + 呼吸，纯视觉）。 |
| v0.11.11 | 品质数据改为内嵌在 `.dsv` 尾部，不再产生侧文件。 |
| v0.12.x | A 类五件套（研究站、6 种防御塔、5 类发电机、弹射器/发射井、喷涂机概率 +1）；光环半径缩小 70%、装配机 `entityId` 越界加固、尾块倒序扫描支持与其他 Mod 共存；10 点标记可配置（样式/颜色/呼吸/自转）。 |
| v0.13.x | 储物仓品质格数（内存扩容、存档结构零改动）；分流器品质优先 + 面板内勾选框（修正锚点位置）。 |
| v0.14.0 | 物品品质角标（数字/圆点/两者、四档颜色、纯显示不写档）。 |
| v0.15.0 | 玩家配置精简为 16 键（其余平衡参数内置为常量）；仓库品质扩容与分流器品质优先转为常开；调试热键改为 `DSPQS_DEV=1` 启用。 |
| v0.16.0 | 三个功能修复：大型采矿机（站点+采集器）吃品质、Shift 立即建造保留品质、增产模式额外产物不再 0 品（DLL 225,280 B / `8CB2CB75…` / 59 checks）。 |
| v0.16.1 | 分流器改为「输出口品质过滤」（面板底部折叠区、4 口规则、无规则口原版兜底），新增分拣器品质阈值过滤；退役旧「品质优先」自动排序与 F3；最终 DLL **237,056 B / `ECF9AE7B10DCF8F2FABD40B61F9E2DB367EE895DDD5B7229BACD9A4150C22553`** / 66 checks。 |
| v0.17.0 | 过滤 UI 迁到原版物品选取器底部页脚（分流器/分拣器共用入口，自建面板删除）；修复背包路径建造（大型采矿机 2316）与 Shift 立即建造丢失品质（统一消费窗口 + 消费收据 `TryTakeReceipt`，`BuildFinally` 按 `protoId` 绑定）；分流器未命中规则的货物改走原版 first-fit 兜底；最终 DLL **241,664 B / `7217980504A716BA54DB06330DCACF74A2BE81D43FCC76879E364CBF9DD1B0CD`** / 72 checks。 |
| v0.17.1 | 两个过滤 bug 修复：① `Cargo.inc` 是**整栈总点数**（`CargoPath.TryUpdateItemAtHeadAndFillBlank` 合并栈累加、喷涂机 `inc = count × 能力`），v0.17.0 误当每件点数与阈值比较 ⇒ 统一 `QualityMath.PerItemPoints(inc, stack)`；② 分流器改为「有规则即完全接管」（不再让路、未命中只在无规则口 first-fit、点物品即自动开规则），修掉脏 `outFilter` 把低品质同种货送进规则口；③ 分拣器改为在 5 格窗口内按每件点数选货并自行搬运，`min=0` 原版等价。最终 DLL **245,248 B / `CB7769A7DAFC7FC7C44E35C39264958E399F2A7452A07779D5B4D0EA861849C4`** / 78 checks。 |
| v0.18.0 | 四项用户口径修复：① **发电功率**（风/光/地热/火/射线接收站）真正生效——用户实机装的是 v0.11.10 DLL、根本没有 v0.12.0 才加入的发电补丁，本版重新构建并加固 armed 自检；② **新增电力塔类建筑供电范围**（`PrefabDesc.isPowerNode`：感应塔/无线输电塔/配电站），`connectDistance`/`coverRadius` 按 `prefab × 曲线倍率` 幂等重算（10 点 = 2.00×），构建落成与读档后都生效（`Patches\P5_PowerRange.cs`）；③ **制造台升品概率**改为复用速度/发电同一张每点曲线（`QualityMath.UpgradeCurveChance`：1..10 点 = 2.5/5/7.5/10/15/20/30/40/60/100 %；掷骰结构不变：0–6 单掷、7–9 双掷、10 点必 +2），旧线性「点数 × 1.5%」仅留作离线 A/B；④ **矿机确定性**（`minerChancePerPoint`=100，判定次数 `floor(点数/2)` 且每次必中：2→+1、4→+2、10→+5，复用原多判代码；大矿机同规则）；⑤ **未付款预建（白色虚影）补付后按实际消耗的材料点数落成**（新增 `Patches\P1_DeferredPay.cs`，观察 `ConstructionModuleComponent.PlaceItems` 的一次性全额补付，`TakeTailItems` postfix 记点数、`NotifyPrebuildChange(...,3)` 且 `itemRequired==0` 时写 `prebuildInc`）。玩家 cfg 仍 16 键、存档 schema 不变；离线 78 → **82 checks**。最终 DLL **253,952 B / `F3B9E800368F48AAB7B08CEEA2E3EF76366C62593F92C96C7A847C38490191DE`** / 82 checks。 |
| v0.19.0 | 两项新功能（玩家 cfg 仍 16 键、存档 schema 向后兼容）：① **三种增产剂可用喷涂次数统一改为 1**（`Patches\P6_SprayCount.cs`）——改写 `ItemProto.HpMax`（原版所有插入/取出路径活读该字段），`ItemProto.Preload` postfix 落地、`SpraycoaterComponent.Import` postfix 折算旧档次数为件数、`ABN_ProtoData.CheckProto` prefix+postfix 临时还原原值以绕过存档哈希校验。② **品质回收机**（`Patches\P7_Recycler.cs` + `QualityData.Recycler.cs`）——复用原版喷涂机（2313），回收通道是它的**催化剂进料带**：通电、进料带上出现非增产剂且单件点数 ≥ `MinPerItemValue`（默认 **1**，即任意带点数的货物）的 cargo 时，整叠消耗销毁、点数记入该机账号，每 **4 点 = 1 次喷涂次数**灌进原版喷槽（1143，容量 600 次），满槽停收；每机账号随存档 `recyclers` 节点（`{"e","p","c"}`）持久化、拆机 1200 tick 清理；注入改为 `InternalUpdate` **postfix**（struct/class 双签名，`Refresh`→改→`Persist`）。离线 82 → **86 checks**。最终 DLL **271,360 B / `5A43F0C747BD01862051C94B7C26C7267B51D003AE38D10DBE5A6DF190FE52E8`** / 86 checks（2026-10-31 16:35:03 构建；阈值按用户反馈由 3 降到 1 后重新出包）。 |
| v0.19.1 | 五项修复（玩家 cfg 仍 16 键、存档 schema 仍为 1、向后兼容）：① **读档丢批次点数修复**——制造台"本批原料点数"（`inputLevel`）以前只在内存，读档后首批产物按 0 点原料算；现在写进行星节点 `"inputs": [{"e":entityId,"l":level}]`，读档时 `SetInputLevel` 恢复，旧档无该节点则跳过。② **批次原料点数改为四舍五入**（`QualityMath.BatchInputLevel`：`(incServed + served/2) / served`，钳到 `maxLevel`，原版是整除截断）——用户报告的 10 点制造台 × 6 点原料（一批 5 件共 29 点）由 `29/5 = 5` 出 7 点改为 `5.8 → 6` 出 8 点。③ **喷涂次数按品质点数分级**（v0.19.2 已回滚删除，当前恒为 1 次/件）。④ **分拣器品质阈值过滤重写**（`P5_SorterFilter`）——v0.18.0 只拦 `CargoTraffic.TryPickItem`（**仅覆盖传送带**，储物仓/机器产物口照样被取）；现在传送带走 `CargoPath.TryPickItem` 两个带 filter 的重载，其余来源走 `PlanetFactory.PickFrom(uint,…)` prefix（`Priority.High`），覆盖 `belt` / `storage` / `assembler` / `ejector` / `silo`，`station` / `lab` / `powerGen` 显式跳过并在 `bySource=` 说明；来源 = `assembler` 时与 `P0_PickFrom` 互斥（本补丁只预测、返回 `true` 放行给 P0）。离线 86 → **94 checks**。最终 DLL **`293,376` B / `9F5E1D94E954C2EFF5961A87D4999210C4B25A0CD81797C80F840B26E39A0154`** / 94 checks（`2026-10-31 17:15:58` 构建）。 |
| v0.19.2 | 四项（玩家 cfg 仍 16 键、存档 schema 仍为 1、向后兼容）：① **品质筛选设置随复制/粘贴与蓝图迁移**（新增 `Patches\P6_FilterCopy.cs`，**11 个挂点**，见 §4.2.9）——Shift 拖放复制与 Ctrl+C / Ctrl+V 走 `BuildingParameters.CopyFromFactoryObject` / `PasteToFactoryObject` / `PlanetFactory.CopyBuildingSetting` / `PasteBuildingSetting`；蓝图另走**两段式**：`BuildTool_BlueprintPaste.CreatePrebuilds()` prefix 遍历 `blueprint.buildings` 用 `TryReadBlueprintBlock(building.parameters)` 解码入队（`bp-stage queued=N buildings=M`）→ `AddPrebuildDataWithComponents` postfix 按 protoId 绑到 prebuildId（`bp-stage prebuild id=.. proto=.. kind=.. min=..`）→ `AddEntityDataWithComponents` postfix 写到落地实体；持久化由 `BuildingParameters.ToParamsArray` postfix / `FromParamsArray` prefix / `GenerateBlueprintData` prefix+postfix 把 **9 个 int 的 `QFPB` 魔数块**（magic `0x51465042`、version 1、kind 1=四向分流器 / 2=分拣器、protoId、rule0..3、min）追加在 `BlueprintBuilding.parameters` 尾部随 `.bp` 文件保存，原版与未装本 MOD 的客户端仍能正常读取该蓝图。② **分拣器品质阈值过滤修复**（`P5_SorterFilter`，不动锚点只改实现）——真根因是 `storage` 分支把**实体 id** 直接当 `storagePool` 下标用，而原版要求先 `entityPool[entityId].storageId` 映射再取 `storagePool[storageId]`，错用命中空组件导致 `grids=0`、从储物仓抓货时品质点数丢失；同时删除 `lastEmptyItem` 缓存门控（阈值/物品过滤变化与"只设阈值不选物品"立即生效）、补 `NotifyStorageChange()` 让仓库界面即时刷新。③ **关闭"储物舱随品质扩容"**——`QualityConfig.StorageQualityGridsValue` 改为 `false`，功能停用、行为等同删除，修掉打开仓库界面时 `System.IndexOutOfRangeException at UIStorageGrid._OnUpdate()` 崩溃；代码与存档兼容逻辑保留，旧档尾块 `storages` 的溢出数据读档静默忽略（只 `WarnOnce`）。④ **v0.19.1 的"喷涂次数按品质分级"回滚**（含 `Checks.P6SprayUses.cs` 那 6 条 checks），离线 94 − 6 = **88 checks**。新增 DEV 探针（仅 `DSPQS_DEV=1`）：`storage-probe` / `storage-pick` / `pick-return` / `inserter-hold` / `stack-insert2` / `stack-insert2b` / `cargo-insert` / `insert-into` / `bp-stage` / `blueprint block written (save)` / `blueprint block read`。实机验证（用户 2026-10-31 21:24 确认）：分拣器从储物仓抓货到传送带**品质点数保留**、蓝图复制的分拣器/四向分流器**各自保留筛选设置**（`bp-stage queued=4 buildings=20` + 4 条 `min=6/8/7` + 分流 `kind=1`）、仓库界面不再崩溃也不再残留旧货。最终 DLL **`298,496` B / `A0BE6EBF3EEF1BB587C7EB6BA0FD32FEEFFDD8F7C98DC58C2B5308298775A8C7`** / 88 checks（`2026-10-31 21:46:49` 构建）。 |
