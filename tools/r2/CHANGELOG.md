# Changelog

## 0.19.2

- **品质筛选设置随「复制/粘贴」与蓝图一起走**：四向分流器的每口规则、分拣器的品质阈值，现在在 Shift 复制、Ctrl+C / Ctrl+V 与蓝图复制粘贴时都会各自保留（此前只有建筑本身的品质点数会跟随，筛选规则永远回到默认）。新增补丁 `Patches\P6_FilterCopy.cs`（11 个 Harmony 挂点）：建筑设置走 `BuildingParameters.CopyFromFactoryObject` / `PasteToFactoryObject` 与 `PlanetFactory.CopyBuildingSetting` / `PasteBuildingSetting`；蓝图走**两段式**通道——`BuildTool_BlueprintPaste.CreatePrebuilds()` prefix 先从 `BlueprintBuilding.parameters` 解出筛选块（日志 `bp-stage queued=N buildings=M`），再在 `PlanetFactory.AddPrebuildDataWithComponents` postfix 按 protoId 绑到 prebuildId（`bp-stage prebuild id=.. proto=.. kind=.. min=..`），最后由 `AddEntityDataWithComponents` postfix 写到落地实体；筛选块本身由 `BuildingParameters.ToParamsArray` / `FromParamsArray` / `GenerateBlueprintData` 三处挂点写成 `parameters` 尾部的魔数块（magic `0x51465042` 即 `QFPB`、version 1、kind 1=四向分流器 / 2=分拣器、protoId、rule0..3、min），随 `.bp` 文件持久化。
- 修复**分拣器从储物仓抓货会丢品质点数**：真根因是储物仓分支把「实体 id」当成 `storagePool` 的下标用了（原版要先 `entityPool[entityId].storageId` 映射再取 `storagePool[storageId]`），命中空组件导致读到 `grids=0`、按 0 点放行。同时删掉 `lastEmptyItem` 缓存门控（阈值或物品过滤一改立即生效，「只设品质阈值、不选物品」也能生效），并补 `NotifyStorageChange()` 让仓库界面立刻刷新（此前取走货后界面残留旧货、要重开才消失）。
- **关闭「储物舱随品质扩容」**：`QualityConfig.StorageQualityGridsValue` 改为 `false`，行为等同删除该功能（代码与旧存档兼容逻辑保留），修掉打开仓库界面时的 `System.IndexOutOfRangeException at UIStorageGrid._OnUpdate()` 崩溃；启动日志改为 `[QSystem][storage] NOT ARMED (storageQualityGrids=false; vanilla containers)`，旧存档尾部残留的 overflow 数据读档时静默忽略（只 WarnOnce），不再写回。
- 新增 DEV 诊断探针（仅 `DSPQS_DEV=1` 输出，正常游戏静默）：`storage-pick`、`pick-return`、`inserter-hold`、`stack-insert2`、`cargo-insert`、`insert-into`、`bp-stage`、`blueprint block written (save)` / `blueprint block read` 等，用于定位取货点数与蓝图块读写。
- 离线测试 94 → **88 checks**（撤回 v0.19.1「喷涂次数按品质分级」那组 6 条 checks）。
- 构建：`DSPQualitySystem.dll` **298,496** 字节 / SHA256 `A0BE6EBF3EEF1BB587C7EB6BA0FD32FEEFFDD8F7C98DC58C2B5308298775A8C7` / `2026-10-31 21:46:49`；启动自检 `[QSystem][filtercopy] armed=True reason=patched 11/11 (BuildingParameters copy/paste/apply + Copy/PasteBuildingSetting + blueprint params/save/load + prebuild stage)`。

- **诊断输出默认关闭（v0.19.2 整理）**：所有 `[Probe][…]` 与 DEV 调试探针（`storage-probe` / `storage-pick` / `pick-return` / `inserter-hold` / `stack-insert*` / `insert-into` / `cargo-insert` / `bp-stage` / `blueprint block read|written`）改为**只在 `DSPQS_DEV=1` 时输出**；普通游玩只剩启动 `armed` 行、一次性 `[Audit]` 自检、`effective=` 每 200 次与 `[save]` 行。

## 0.19.1

- 修复**读档后制造台首批产物点数偏低**：制造台的「本批原料点数」（`inputLevel`）以前只存在内存里，存档再读回就丢了 —— 读档后该值回落为 `-1`，首批产物按「0 点原料」算（日志 `in=0`）。现在写进行星节点 `"inputs":[{"e":entityId,"l":level}]`，读档时 `SetInputLevel` 恢复（修后 `in=6`）；旧档没有该节点直接跳过，存档 schema 仍是 1。
- 制造台每批原料点数由原版**整除截断改为四舍五入**（`QualityMath.BatchInputLevel`）：用户报的「10 点制造台喂 6 点原料却出 7 点产物」是 5 件共 29 点、`int(29/5)=5` 再 +2 得 7；改成 half-up 后 `31/5=6`，产物 8 点。取各输入槽最小值的规则不变。
- **增产剂可用次数按每件品质点数分级**（原来恒为 1）：0–2 点 → 1 次、3–4 → 2、5–6 → 4、7–8 → 5、9 → 7、10 → 10。`ItemProto.HpMax` 仍固定为 1（罐内单位 = 次数），由新的 `P6_SprayUses` 在插入的四个路径上「插前快照 + 插后补差」把每件净增补到表值；Mk.I/II/III 每次喷涂加的点数（`ItemProto.Ability` = 1/2/4）**不变**，此版只动「可用次数」。
- 修复**取回喷涂剂会凭空复制**：取回改由 prefix 接管，按每台喷涂机的账本 `"sprayuses":[{"e","i","u"}]` 反推件数，取回件数夹到 **≤ 投入件数**；旧档没有账本时按 `uses/10` 保守回退。清空喷涂机（`ClearItems_Spraycoater`）同时清账本。
- 分拣器品质阈值过滤**覆盖范围补齐**：0.18.0 只拦传送带取货，储物仓 / 机器产物口 / 弹射器 / 发射井里的低品质货仍会被抓走。现在传送带拦 `CargoPath.TryPickItem` 的两个带 filter 重载，其余来源由 `PlanetFactory.PickFrom` 的 `Priority.High` prefix 覆盖（storage / assembler / ejector / silo），机器产物口只做「下一件点数不够就拦」、不消耗产物池点数；物流塔 / 研究站 / 电站明确跳过并在 `bySource=` 里可见。与 `P0_PickFrom` 互斥（来源=assembler 时原样放行给它）。
- 无新增 cfg 键（仍 16 个）、存档 schema 向后兼容（行星节点仅新增可选 `inputs` / `sprayuses`）；离线测试 86 → 94 条。

## 0.19.0

- **三种增产剂的可用喷涂次数统一改为 1 次**（原版 12 / 24 / 60）：改写在 `ItemProto.HpMax`，插入 / 取出 / tooltip 三条原版路径自动一致；旧档罐内旧「次数」读档时按 `ceil(次数 ÷ 原版次数)` 折算成件数（日志如 `604 vanilla spray times (60/item) -> 11 items`），不会读档后喷出巨量；存档哈希校验期间临时还原原版值，避免误报「数据异常」弹窗。
- 新增**品质回收机**（复用原版喷涂机 2313）：回收通道是喷涂机的**催化剂（增产剂）进料带**（原版放 1141/1142/1143 的那一侧）——喷涂机通电时，进料带上单件点数 ≥1 的非增产剂物品（即任意带点数的货物）被**整叠消耗销毁**，点数记入该机账号，**每 4 点 = 1 次喷涂次数**灌进原版喷槽（1143，容量 600 次），超出容量的点数留在账号；未喷涂的 0 点货物整叠原样放行。默认阈值 1（`QualityRecyclerMath.DefaultMinPerItem`，内部开关 `P7_Recycler.MinPerItemValue`、非 cfg 键）——兑换本身亏损（4 点只换 1 次喷涂次数、1 次最多值 3 点），无需用阈值防套利；改成 3 即回到「只收 Mk.III 喷过的」。物品带侧照常喷涂、互不影响；喷槽满 600 次时暂停回收（耗尽后自动恢复）。账号写进存档尾部的 `"recyclers"` 节点（`{"e","p","c"}`）、跨读档保留；拆机后 1200 tick 清理。
- 无新增 cfg 键（仍 16 个）、存档 schema 向后兼容（行星节点仅新增可选 `recyclers`，旧档无此键照常读、早期 `"o"` 字段读档忽略）；离线测试 82 → 86 条。

## 0.18.0

- 修复**发电功率没变化**：旧安装包里的 DLL 实际是 v0.11.10（其中没有任何发电补丁，v0.12.0 才加入），本版重新构建；风力/光伏/地热/火力/射线接收站在 10 点时真实达到 2.00× 功率。
- 新增**供电范围加成**：电力塔类建筑（感应塔 / 无线输电塔 / 配电站，按 `PrefabDesc.isPowerNode` 判定）的连接距离与覆盖半径随同一条品质曲线放大（10 点 = 2.00×），构建落成与读档后都生效、按 prefab 原值幂等重算。
- **制造台升品概率改用速度/充电/发电那张每点曲线**：1/4/7/10 点 = 2.5%/10%/30%/100%，7–9 点额外再判一次、10 点必 +2，掷骰结构不变（分馏塔仍单掷）。
- **矿机产出点数改为确定性**：判定次数 = 点数 ÷ 2 且每次必定成功 —— 2 点固定 +1、4 点固定 +2、10 点固定 +5；大型采矿机同规则。
- 修复**材料不足时的白色虚影补付后按 0 点落成**：新增补付观察，建成时按实际被扣材料物品的品质点数落成（与材料当场充足一致）。
- 无新增 cfg 键（仍 16 个）、存档 schema 不变；离线测试 78 → 82 条。

## 0.17.1

- 修复**四向分流器品质过滤仍会放过无品质物品**：品质判据改为「每件点数 = 栈总 inc ÷ 栈件数」（原版 `Cargo.inc` 是整栈总点数，旧版直接拿它比阈值，多件低品栈被误放行）。
- 修复分流器**规则口泄漏**：有规则即完全接管（不再因输入优先级/优先级预设/上方储物仓/`input0==0` 让路给原版）；命中规则只进规则口、口满原地等待；未命中规则只去**没有设规则**的口，原版 `outFilter` 彻底不参与路由（v0.17.0 的 first-fit 兜底路径已删除）。
- 修复**在物品选取器里点物品却不写规则**（旧版只在开关已开时才写，导致只留下原版被写脏的 `outFilter`）；现在点物品即按该物品写入规则。
- 修复**分拣器品质阈值形同虚设**：同样改为按每件点数比较；整栈取货与原版 `filter` 语义不变，`min=0` 时原版等价。
- 无新 cfg 键、存档 schema 仍为 1（`splittersV2` / `sorters` 不变）；离线测试 72 → 78 条。

## 0.17.0

- 品质过滤编辑器迁入**原版「物品选取」窗口**：点分流器的过滤圆 / 分拣器的过滤按钮打开选取器后，底部出现品质设置行（分流器：`口A 品质分流 [开/关] 物品: xxx [-] ≥N [+]`；分拣器：`品质阈值 [-] ≥N [+]`），网格里点物品仍走原版流程。
- 修复分流器窗口里旧品质过滤面板「点不到」的问题：旧面板整份删除，改用真按钮控件。
- 修复**大型采矿机放置后品质点数丢失**（背包分支建造现在也能拿到实际消耗物品的点数）。
- 修复 **Shift+左键「立即建造」仍造出 0 点建筑**（瞬时消费窗口 + 落成时按物品匹配绑定点数）。
- 离线测试 66 → 72 条。

## 0.16.1

- 四向分流器改为**逐输出口规则**（物品 + 品质 ≥N），替代 0.13 的「品质优先」；分拣器新增品质阈值。
- 旧「品质优先」勾选框、标题后缀与 F3 热键退役。
- 过滤 UI 实际未接线的问题修复（补丁注册 + 面板布局）。

## 0.16.0

- 修复大型采矿机（采集站型建筑）不给矿加品质。
- 修复 Shift+左键立即建造丢品质点数。
- 修复增产模式的额外产物品质被取货抵消。

## 0.15.0

- cfg 精简为 16 个玩家开关，全部中文注释；其余平衡参数改为内置常量。
- 新增 cfg 自愈：旧版本残留键会被整份重写清掉，只留 16 键并保留玩家当前值。
- 删除「屏幕描边」与「蓝图携带品质」两个功能（后者经用户判定为错误方向）。
- 调试热键移出 cfg，改为环境变量 `DSPQS_DEV=1` 开启。
- 开发文档重写为纯净版（功能/配置/实现/兼容性）。

## 0.14.0

- 新增物品品质角标（储物仓/背包/手持提示），支持数字/圆点/两者与显示阈值。
- 角标配色压暗，避免 10 点金色在泛光下发白。

## 0.13.2

- 四向分流器面板内新增「品质优先」勾选框，位置与遮挡问题修复。
- A 类五件套（研究站/防御塔/发电机/弹射器/发射井）逐项实机数值验证。

## 0.13.0

- 新增储物仓「品质质变格数」（按点数扩容，溢出物品存存档尾部）。
- 新增四向分流器「品质优先」（输入槽按品质重排）。

## 0.11.11

- 品质数据从侧文件改为内嵌 `.dsv` 尾部，不再产生额外存档文件。
