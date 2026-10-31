# 离线依赖包（不要提交到 GitHub）

本包是从 `DSPQualitySystem-0.17.1-src.zip` 里单独拆出来的第三方离线依赖：

- `tools\nuget-local\` —— 11 个离线 NuGet 包（BepInEx.Core / GameLibs / HarmonyX / MonoMod / net472 参考程序集等），约 23 MB
- `tools\unity-refs\` —— 69 个 UnityEngine 引用 DLL，约 5 MB

**用途**：没装游戏、或没有可用外网 NuGet 源时，让 `build.ps1` 能编译出 `DSPQualitySystem.dll`。

**用法**：把本包的 `tools\` 目录解压 / 合并到仓库根目录（与 `DSPQualitySystem.csproj` 同级），然后正常执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

**注意**：仓库根目录的 `.gitignore` 已经忽略 `tools/nuget-local/` 与 `tools/unity-refs/`，解压后不会被误提交。
这些文件不是本项目版权，请勿上传、勿再分发。
