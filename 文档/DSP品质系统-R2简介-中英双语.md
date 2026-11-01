# DSP 品质系统 · R2 简介（中英双语）

> 当前版本 **v0.19.2**（DLL 298,496 字节），适用于 **DSP 0.10.35 + BepInEx 5**

---

## 一、这是什么

原版游戏里有一根叫「增产点数」的隐藏数值，它只是个内部参数。这个 Mod 把它重做成一套 **0–10 级品质点数**的经济：点数可以被喷上去、被矿机挖出来、被生产抬升，再跟着产物、建筑和物流一路流动。

它不新增物品、不新增配方、不改你的存档格式 —— 只是给「增产剂」这条线做了一次完整的设计回收。

## 二、玩起来是什么样

1. **喷一下就有起点**：喷涂机把物品喷到 **1 / 2 / 4 点**（Mk.I / II / III）；一件增产剂 = 1 次喷涂。
2. **点数会跟着走**：产物继承、建筑落成、拆除返还 —— 好料造出来的机器就是好机器。
3. **品质 = 实打实的产能**：八类加成共用同一张曲线，**10 点 = 2.00×**（代价 3.5 倍电耗，和原版增产剂满级同一取舍）。
4. **看得见**：物品角标从灰蓝到金色，10 点建筑套一圈金色光环。
5. **分得开**：四向分流器每个输出口可以按「物品 + 最低品质」分流；分拣器可以只抓 ≥N 点的货。
6. **带得走**：这些筛选设置会随 **Shift 复制 / Ctrl+C·V / 蓝图** 一起迁移，不用一片片重设。

## 三、特点

- **0–10 级品质**：曲线非线性 —— 前 4 点只给 10%，**9→10 那一级最值钱**。
- **三种来源，各有上限**：喷涂只到 4 点、矿机每两级 +1、制造台按概率升品 —— 往上走必须靠产线。
- **八类加成**：制造/采矿速度、物流塔充电、研究速度、防御塔射速、发电功率、发射速度、喷涂概率、**电力塔供电范围**（第八项 v0.18.0 新增，恒开）。
- **矿机收益是确定的**：判定次数 = ⌊点数 ÷ 2⌋ 且必定成功 —— 2 点矿机每块矿必 +1，10 点必 +5。
- **升品概率复用速度曲线**：1..10 点 → 2.5% / 5% / 7.5% / 10% / 15% / 20% / 30% / 40% / 60% / 100%，7 点起额外再判一次，10 点直接 +2。
- **品质筛选可以搬运**：分流器每口规则、分拣器阈值随 Shift / Ctrl+C·V / 蓝图迁移；分拣器覆盖传送带、储物仓、机器产物口、弹射器、发射井。
- **品质回收机**：把带点数的物品从喷涂机的催化剂进料口送入，每 4 点换 1 次喷涂（v0.19.0）。
- **16 项中文开关 + 零风险**：不新增物品、不改配方；任何补丁失败都安静回退原版；品质数据写在存档尾部，卸载不丢物品。

## 四、安装

- **前置**：DSP 0.10.35、BepInEx 5.4.x（推荐 5.4.21+），r2modman 会自动带上 CommonAPI。
- **r2modman**：搜索 `DSP Quality System` 一键安装。
- **手动**：把 `DSPQualitySystem.dll` 放到 `BepInEx\plugins\DSPQualitySystem\`。
- **确认生效**：`BepInEx\LogOutput.log` 里出现 `=== DSP Quality System v0.19.2 ===` 与各功能 `armed` 行。

## 五、卸载与兼容

- 删掉 DLL 即可，**没有卸载后丢物品的地方**（储物仓品质扩容已于 v0.19.2 停用，所有容器一律原版格数）。
- 品质数据写在存档文件尾部，原版读档直接忽略；卸载后建筑变回普通建筑。
- 不新增物品与配方，与其它 Mod 冲突面小；改同一批数值表的整合包可能互相覆盖。

## 六、配置文件

`BepInEx\config\dsp.qualitysystem.cfg`，**16 个键**，全部中文注释，改完需重启：分格（2）、八类加成里的七项开关、角标（3）、光环（4）。储物仓扩容、回收机阈值、喷涂次数等平衡参数已内置为常量，不开放。

---

## English

> **v0.19.2** (DLL 298,496 bytes) · for **DSP 0.10.35 + BepInEx 5**

### What it is

Vanilla has an internal "proliferator points" value hidden behind the spray coater. This mod turns it into a full **0–10 quality point economy**: points are sprayed on, mined out, raised by production, and then flow with products, buildings and logistics. No new items, no new recipes, no save-format changes.

### How it plays

1. **A starting point**: spray coaters set items to **1 / 2 / 4 points** (Mk.I / II / III); one proliferator item = one spray.
2. **Points travel**: products inherit, buildings inherit at completion, dismantling refunds.
3. **Quality is throughput**: eight bonuses share one curve — **10 points = 2.00×** (costs 3.5× power, the same trade-off as vanilla max proliferation).
4. **Visible**: item corner badges from grey-blue to gold; 10-point buildings get a golden halo.
5. **Filterable**: each splitter output can route by "item + minimum points"; sorters can pick only ≥N-point items.
6. **Portable**: those filter settings travel with **Shift-copy / Ctrl+C·V / blueprints**.

### Features

- **Non-linear curve**: first 4 points give only 10%; the 9→10 step is the big one.
- **Three sources with caps**: spraying tops out at 4, miners give a flat +1 per two points, assemblers roll upgrades by curve.
- **Eight bonuses**: assembler/miner speed, station charging, lab speed, turret fire rate, power generation, launcher speed, sprayer chance, and **power-node coverage** (added in v0.18.0).
- **Deterministic mining**: ⌊points ÷ 2⌋ rolls, always successful — 2 points = +1 per ore, 10 points = +5.
- **Upgrade chance reuses the speed curve**: 2.5%/5%/7.5%/10%/15%/20%/30%/40%/60%/100% for 1..10 points; 7+ gets an extra roll; 10 points is a guaranteed +2.
- **Filter settings are portable**: splitter rules and sorter thresholds follow Shift-copy, Ctrl+C·V and blueprints; sorters cover belts, storages, machine output slots, ejectors and silos.
- **Quality recycler**: feed point-carrying items into the coater's proliferator input — every 4 points becomes one spray (v0.19.0).
- **16 Chinese-commented toggles, low risk**: no new items or recipes, every patch fails safe back to vanilla, quality data lives in the save trailer so uninstalling loses nothing.

### Install / Uninstall

- Requirements: DSP 0.10.35, BepInEx 5.4.x (CommonAPI pulled in by r2modman).
- Manual: drop `DSPQualitySystem.dll` into `BepInEx\plugins\DSPQualitySystem\`.
- Verify: look for `=== DSP Quality System v0.19.2 ===` and per-feature `armed` lines in `LogOutput.log`.
- Uninstall: delete the DLL; quality data is an ignored trailer, buildings revert to vanilla, and nothing is lost (storage quality expansion was retired in v0.19.2).

### Config

`BepInEx\config\dsp.qualitysystem.cfg` — 16 keys, all with Chinese comments (2 stacking, 7 bonus toggles, 3 badges, 4 halo); balance values are built-in constants.

---

当前版本 v0.19.2 ｜ DLL 298,496 字节 ｜ SHA256 `A0BE6EBF…775A8C7` ｜ 88 项离线测试全部通过
