# tools

本目录是**取证与辅助脚本**，不是玩游戏必需的东西。想直接用修复补丁的话看
[`../plugin/SpawnDistanceGuard`](../plugin/SpawnDistanceGuard)。

| 脚本 | 用途 |
|---|---|
| [`launcher-probe/`](launcher-probe) | 在 SPT 启动器进程内反射调用其校验函数、导出被加密的字符串常量（用于定位 [docs/01](../docs/01-launcher-validation-offbyone.md) 那个差一 bug） |
| [`analysis/find-spawn-code.ps1`](analysis/find-spawn-code.ps1) | 用 Mono.Cecil 在 `Assembly-CSharp.dll` 里做交叉引用，定位 AI 刷点选择代码与传入的常量（用于确定 Harmony 补丁点） |
| [`tune-botspawn-config.ps1`](tune-botspawn-config.ps1) | 不改代码的替代方案：压刷新频率/数量/单区域上限（自动备份 + 一键还原）；**注意这不是距离检查** |

## 典型用法

```powershell
# A. 启动器校验失败时，先看它到底在校验什么
.\launcher-probe\run-probe.ps1 -SptRoot "C:\Games\SPT" -DotnetRoot "D:\SPT-dotnet9"

# B. 游戏更新后想重新定位刷点逻辑（本插件失效时用）
.\analysis\find-spawn-code.ps1 -SptRoot "C:\Games\SPT"

# C. 只想降刷新强度（可一键还原）
.\tune-botspawn-config.ps1 -SptRoot "C:\Games\SPT" -ZeroTypeLimitsMin
# 还原：
.\tune-botspawn-config.ps1 -SptRoot "C:\Games\SPT" -Restore
```

## 依赖

- PowerShell 5.1+（Windows 自带）
- `analysis/find-spawn-code.ps1` 需要 SPT 自带的 `BepInEx\core\Mono.Cecil.dll`（随 SPT 一起安装）
- 想看反编译的 C#：`dotnet tool install --global ilspycmd`，然后按脚本最后打印的命令执行
- 探针脚本需要 .NET SDK
