# 用 GitHub Desktop 把「戴森球计划 · 品质系统」工程上传到 GitHub

> 面向：Windows + 已安装 [GitHub Desktop](https://desktop.github.com/)（本机为 3.6.9，界面是英文）。
> 本教程只做一件事：把已经拆好的**纯工程文件夹**变成一个 GitHub 仓库，并发布上去。

---

## 0. 先看清：这次拆出来的是什么

| 位置 | 内容 | 大小 | 要不要上传 GitHub |
| --- | --- | --- | --- |
| `dist\github\DSPQualitySystem\` | **纯工程文件**（103 个文件：82 个 `.cs`、2 个 `.csproj`、4 个 `.ps1`、1 个 `.py`、9 个 `.md`、`nuget.config`、`.gitignore`、图标等） | 1.64 MB | ✅ **就上传这个文件夹** |
| `dist\github\DSPQualitySystem-0.17.1-source.zip` | 上面那个文件夹的压缩包 | 0.55 MB | ❌ 不必（GitHub 上直接看源码更好） |
| `dist\github\DSPQualitySystem-0.17.1-offline-deps.zip` | **第三方离线依赖**：`tools\nuget-local\`（11 个 `.nupkg`）、`tools\unity-refs\`（69 个 Unity 引用 DLL） | 25.7 MB | ❌ **不要上传**（见 §6 的说明） |

**为什么这么拆**：GitHub 对单个文件有 100 MB 硬限制、超 50 MB 会警告；而 `unity-refs` 的 DLL 和 `nuget-local` 的包都是 Unity / BepInEx / .NET 官方的第三方文件，不属于这个 MOD 的版权，也不该进版本库。工程本身只要 1.6 MB，干净好读。

**文件完整性已实测确认**（不是"看起来没缺"）：

- 用拆出来的工程 + 还原回去的离线依赖，直接跑 `build.ps1` → 输出 `BUILD OK`；
- 编译出的 `bin\Release\DSPQualitySystem.dll` 为 **245,248 字节**，SHA256 = `CB7769A7DAFC7FC7C44E35C39264958E399F2A7452A07779D5B4D0EA861849C4`，与官方 v0.17.1 发布版 DLL **逐字节相同**；
- 跑离线测试 → `ALL PASS (78 checks)`。

---

## 1. 一次性准备

### 1.1 打开 GitHub Desktop

开始菜单搜 `GitHub Desktop` 打开即可。**不需要另外安装 Git**——GitHub Desktop 自带一份 Git（本机为 `C:\Users\Administrator\AppData\Local\GitHubDesktop\app-3.6.9\resources\app\git\cmd\git.exe`，2.56.0）。

界面是英文的（官方无中文语言包）。本教程涉及的主要英文位置：

| 英文 | 含义 |
| --- | --- |
| `File` → `Add local repository…` | 文件 → 添加本地仓库 |
| `Create repository` | 创建仓库 |
| `Commit to main` | 提交到 main 分支 |
| `Publish repository` | 发布仓库（推送到 GitHub） |
| `Push origin` / `Fetch origin` | 推送 / 拉取（同步） |
| `History` / `Changes` | 历史 / 改动 |
| `Keep this code private` | 保持代码私有 |

### 1.2 有 GitHub 账号并已登录

- 没有账号：去 <https://github.com/signup> 注册（免费）。
- 已登录/要换号：`File` → `Options…` → 左侧 `Accounts` → `Sign in to GitHub.com`，浏览器里点授权即可。
- 顺手确认 `Options…` → `Git` 页的 `Name` 和 `Email`：它们会写进每一条提交记录，尽量用你 GitHub 的公开邮箱，或开启邮箱隐私（见 §7.5）。

---

## 2. 把工程文件夹变成「本地仓库」

我们**不需要**新建空文件夹再往里拷东西——直接让 GitHub Desktop 把现有文件夹收编成仓库即可，文件一个都不会被改动。

1. 菜单 `File` → `Add local repository…`（快捷键 `Ctrl+O`）。
2. 点 `Choose…`，选择文件夹：

   ```
   C:\XCM\DSP MOD\dist\github\DSPQualitySystem
   ```

3. 因为该文件夹里还没有 `.git`，编辑器会提示：

   > This directory does not appear to be a Git repository.
   > Would you like to create a repository here instead?

   点这段提示里的蓝色链接 **`create a repository`**。
4. 弹出 `Create repository` 对话框，只改必要项：
   - `Name`：`DSPQualitySystem`（保持默认即可）
   - `Local path`：保持 `C:\XCM\DSP MOD\dist\github` 不要动（这样仓库就落在 `…\dist\github\DSPQualitySystem`）
   - `Description`：可留空，或填「戴森球计划 MOD：物品品质系统」
   - `Git ignore`：**选 `None`**（我们已经自带 `.gitignore`，再选模板会覆盖/重复）
   - `License`：可留空（想开源再选，见 §8）
5. 点 **`Create repository`**。

> 点完之后左侧 `Changes` 面板里会出现 100 多个 `Added` 文件——这说明仓库建好了，文件都进来了。
> 如果对话框里没有 `Initialize with README` 之类的选项，属正常：3.6.9 这个版本不提供该复选框。

---

## 3. 第一次提交（Commit）

1. 左侧 `Changes` 面板确认改动数量（应约 103 个新增文件）。
2. 左下角 `Summary` 输入框填提交说明，例如：

   ```
   初始化：DSP 品质系统 v0.17.1 源码工程
   ```

   （想用英文也行：`initial commit: DSP Quality System v0.17.1 source`）
3. 点蓝色的 **`Commit to main`** 按钮。

提交成功后：`Changes` 变空，`History` 里出现你的第一条提交，顶部按钮从 `Publish repository` 变成 `Push origin`/`Fetch origin` 之类。

> 如果按钮是 `Commit to master` 而不是 `main`：说明仓库默认分支叫 `master`（旧默认）。不影响上传，可以照常提交；想统一成 `main` 可在 `Repository` → `Repository settings…`（或 `Branch` 菜单）里重命名。

---

## 4. 发布到 GitHub（Publish repository）

1. 点界面顶部中间的 **`Publish repository`**（也可以 `Repository` 菜单里找同名项）。
2. 对话框里填：
   - `Name`：`DSPQualitySystem`（也可改成 `DSPQualitySystem-Mod` 等，只影响网址）
   - `Description`：可选，例如 `Dyson Sphere Program mod: item quality system`
   - **`Keep this code private`**：⚠️ 建议**先勾上**（先私有），等确认仓库内容没问题再改成公开；想一步到位开源就不勾。
3. 点 **`Publish repository`**，等进度条走完。
4. 成功后点右上角 `Repository` → **`View on GitHub`**（或浏览器直接开 `https://github.com/<你的用户名>/DSPQualitySystem`）验证：网页上能看到 82 个 `.cs`、`README.md`、`文档\` 等。

**私有改公开**：GitHub 网页 → 仓库 → `Settings` → 页面最下方 `Danger Zone` → `Change visibility` → `Change to public`（按提示输入仓库名确认）。

---

## 5. 以后改了代码怎么更新

1. 用编辑器改 `C:\XCM\DSP MOD\dist\github\DSPQualitySystem` 里的文件，保存。
2. 回到 GitHub Desktop（它会自动 `Fetch origin`），`Changes` 里列出你改了什么。
3. 写 `Summary` → 点 `Commit to main`。
4. 点右上角 **`Push origin`**（快捷键 `Ctrl+P`）把提交推到 GitHub。

可以攒好几条本地提交，最后一次性 `Push origin`。想撤销还没推的提交：`History` 里右键该提交 → `Undo commit…`。

---

## 6. 仓库里该有什么、不该有什么

仓库根目录的 `.gitignore` 已经把这些规则写好（本仓库自带，**不要**再让 GitHub Desktop 生成模板）：

| 会进仓库（✅） | 不会进仓库（❌，已被 `.gitignore` 挡住） |
| --- | --- |
| `*.cs`、`*.csproj`、`nuget.config` | `bin/`、`obj/`、`artifacts/`、`dist/` |
| `*.ps1`、`*.py`、`tools\r2\*`（清单模板/图标） | `tools\nuget-local\`（11 个 `.nupkg`）、`tools\unity-refs\`（69 个 DLL） |
| `*.md`（README/DEVLOG/TESTLOG/文档） | `*.nupkg`、`*.pdb`、`*.log`、`BepInEx/`、存档 `*.dsv` |
| `.gitignore`、`README-离线依赖.md` | `.vs/`、`*.user`、`Thumbs.db`、`~$*` 等临时文件 |

**特别注意**：`DSPQualitySystem-0.17.1-offline-deps.zip` 这个 25.7 MB 的压缩包**不要放进仓库文件夹**（`.gitignore` 只忽略 `tools/nuget-local/` 这类目录，管不到一个 zip）。它留在 `dist\github\` 下、作为发布附件分发即可；想给玩家下载可以挂到 GitHub 的 `Releases`。

如果误把大文件提交了：`Changes`/`History` 里撤销提交（未推送），或推送后直接在网页删文件再提交一次（历史里仍留痕，但仓库可用）。

---

## 7. 别人从 GitHub clone 之后怎么编译

仓库里没有游戏 DLL 和 NuGet 离线包，所以分两种情况（详见仓库内 `README-离线依赖.md`）：

- **有游戏 + 有 .NET SDK**：直接跑
  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
  ```
  `build.ps1` 会按 `-p:GameDir` > 环境变量 `DSP_GAME_DIR` > 默认 Steam 路径 的顺序找游戏目录，找不到才退回 `tools\unity-refs`。
- **没有游戏**：先把我给你的 `DSPQualitySystem-0.17.1-offline-deps.zip` 解压到仓库根目录（会得到 `tools\nuget-local\` 和 `tools\unity-refs\`），再跑同一条命令。
- **离线测试**：`tests\run-tests.ps1`。测试工程目标框架是 `net10.0`，如果你的 SDK 低于 10，把 `tests\OfflineTests\OfflineTests.csproj` 里的 `net10.0` 改成 `net9.0`（或 `net8.0`）再跑，成功会打印 `ALL PASS (78 checks)` 与 `TEST PASS`。

本机已验证路径：.NET SDK 9.0.318 + 已装游戏 → `BUILD OK`，产物与官方发布 DLL 完全一致；测试改 `net9.0` 后 `ALL PASS (78 checks)`。

---

## 8. 可选：给仓库加开源许可证

工程默认没有 LICENSE。想让人能合法地二次使用/分发，最好补一个：在 GitHub 网页仓库页点 `Add file` → `Create new file`，文件名 `LICENSE`，GitHub 会在右侧给「Choose a license template」入口；选 MIT（最宽松、最常见）即可。或者在本地建 `LICENSE` 文件后再提交。

---

## 9. 常见问题

| 现象 | 原因 / 解决 |
| --- | --- |
| 打开 `Add local repository…` 后报 `This directory does not appear to be a Git repository` | 正常，说明还没初始化；点提示里的 `create a repository` 按 §2 建仓 |
| `Publish repository` 点不动 / 报认证失败 | 没登录或登录过期：`File` → `Options…` → `Accounts` 重新 `Sign in`；有代理就在同页 `Network` 里配代理 |
| 推送被拒，提示 `GH007: Your push would publish a private email address.` | 你在 GitHub 开启了邮箱隐私：去 <https://github.com/settings/emails> 取消勾选 `Keep my email address private`，回来重新 push；推上去后可再勾回去 |
| 上传很慢 / 卡住 | 镜像仓库里有大量历史文档（`DEVLOG.md` 354 KB、`TESTLOG.md` 150 KB），首次推送 1.6 MB 量级，正常几秒；卡住多为网络，重试 `Push origin` |
| 网页上中文文件名变成乱码 | GitHub 支持 UTF-8 中文文件名，正常显示；本地 `git status` 里的 `\346\226\207` 只是 git 的转义显示，不代表上传出错 |
| 提交里想署名别的邮箱 | `Options…` → `Git` → 改 `Name`/`Email`，之后的提交才生效 |
| 想把仓库从 GitHub 删掉 | 网页仓库 → `Settings` → `Danger Zone` → `Delete this repository`；本地文件夹不受影响 |
| 想改仓库网址里的名字 | 网页 `Settings` → `Repository name` 改名；本地仓库的 remote 需在 GitHub Desktop `Repository` → `Repository settings…` → `Remote` 里同步改 |

---

## 10. 三步速查（懒人版）

1. `File` → `Add local repository…` → 选 `C:\XCM\DSP MOD\dist\github\DSPQualitySystem` → 点 `create a repository` → `Create repository`。
2. 左下 `Summary` 写一句说明 → `Commit to main`。
3. 顶部 `Publish repository` → 勾 `Keep this code private`（先私有）→ 发布 → `View on GitHub` 检查。

搞定。之后每次改动 = 改文件 → `Commit to main` → `Push origin`。
