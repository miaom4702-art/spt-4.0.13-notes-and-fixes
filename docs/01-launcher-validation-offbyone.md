# 01 · 启动器校验的差一(off-by-one) bug

## 现象

SPT 启动器里已经注册好档案、服务端也在跑，点 **Start Game** 却弹：

> Escape From Tarkov isn't installed on your computer.
> Ensure your BSG launcher can start the game before starting SPT.

对应启动器日志（`SPT\user\logs\launcher.log`）：

```
[Info] [LaunchGame] spt-core.dll version: 4.0.13
[Info] [LaunchGame] Installed in Live :: NO
[Info] [LaunchGame] Setup Game Files ...
[Error] [LaunchGame] Game Validation   :: FAILED
```

`Game Validation :: FAILED` 对应的返回码是 `-2`，即 `no_official_game_warning`（见
`SPT.Launcher.Base/Models/Launcher/GameStarterResult.cs`）。

## 定位过程

启动器核心是**做过字符串加密的混淆程序集**，所以直接搜 exe 里的字符串找不到校验用的路径与文件名。
我改用 .NET **startup hook** 把探针注入启动器进程，用反射直接调用它的校验函数、并把 `Validate()`
方法 IL 里所有 `ldstr` 常量解析出来（脚本见 [`tools/launcher-probe`](../tools/launcher-probe)）。

探针输出（节选）：

```
--- GameStarter.DetectOriginalGamePath() ---
  returned: 'C:\Install_EFT'
  [DetectOriginalGamePath] 4 ldstr -> build || build ||
      Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov || InstallLocation
  [Validate] 6 ldstr -> InstallLocation || BattlEye\BEClient_x64.dll || BattlEye\BEService_x64.dll ||
      ConsistencyInfo || UnityPlayer.dll || UnityCrashHandler64.exe
  returned: False
--- static fields of ValidationUtil ---
  Boolean b1 = 'False'
  String  c0 = 'Software\Wow6432Node\...\Uninstall\EscapeFromTarkov'
  Int32   v0 = '-1'        <-- 关键
```

反编译出的判定（4.0.13 的 `ValidationUtil.Validate()`，变量名就是混淆后的原样）：

```csharp
var v3 = new DirectoryInfo(v2);                 // v2 = 注册表 InstallLocation
var v4 = new FileSystemInfo[]
{
    v3,                                          // ← 目录本身也算一项
    new FileInfo(Path.Join(v2, @"BattlEye\BEClient_x64.dll")),
    new FileInfo(Path.Join(v2, @"BattlEye\BEService_x64.dll")),
    new FileInfo(Path.Join(v2, "ConsistencyInfo")),
    new FileInfo(Path.Join(v2, "UnityPlayer.dll")),
    new FileInfo(Path.Join(v2, "UnityCrashHandler64.exe"))
};

v0 = v4.Length - 1;                              // = 5
foreach (var value in v4) { if (value.Exists) { --v0; } }
return v0 == 0;
```

数组长度是 6，计数器初值却是 `6 - 1 = 5`，然后**每存在一项就减 1**，最后要求等于 0。
也就是说：**6 项里必须恰好存在 5 项**，多一项、少一项都会失败。

实测对照：

| 该目录下实际存在的项 | v0 | 结果 |
|---|---|---|
| 4 项（目录 + BEClient_x64.dll + ConsistencyInfo + UnityCrashHandler64.exe） | 1 | 失败 |
| 6 项（补齐 BEService_x64.dll 与 UnityPlayer.dll） | **-1** | **仍然失败** |
| 5 项（目录 + BEClient_x64.dll + ConsistencyInfo + UnityPlayer.dll + UnityCrashHandler64.exe） | 0 | ✅ 通过 |

## 为什么正版玩家不会踩到

真实 BSG 客户端里 **只有 `BattlEye\BEService_x64.exe`，没有 `BEService_x64.dll`**，
所以正版安装天然就是"5 项存在" → `v0 == 0` → 通过。这个 bug 恰好被正版的文件布局掩盖了；
而各种"整合包/伪目录"方案（往注册表 `InstallLocation` 指向的目录里塞占位文件）只要数量不对就会失败。

## 规避方法

让注册表 `HKLM\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov`
的 `InstallLocation` 指向的目录**恰好满足 5 项**（可参考 [tools](../tools) 里的思路）：

- 目录本身 ✔
- `BattlEye\BEClient_x64.dll` ✔
- `ConsistencyInfo` ✔
- `UnityPlayer.dll` ✔
- `UnityCrashHandler64.exe` ✔
- **不要**放 `BattlEye\BEService_x64.dll`（放了反而变成 6 项 → 失败）

更稳的做法是把那个目录做成"像真实客户端"的目录：复制真实客户端的根文件与 `BattlEye` 目录，
大目录（`EscapeFromTarkov_Data`/`MonoBleedingEdge`/`NLog`）用**目录联接(junction)**指过去，避免占空间。

## 给 SPT 项目的建议

```diff
- v0 = v4.Length - 1;
+ v0 = v4.Length - 1;            // 本意大概是"只统计 5 个文件"
- foreach (var value in v4) { if (value.Exists) { --v0; } }
+ foreach (var value in v4.Skip(1)) { if (value.Exists) { --v0; } }   // 跳过目录本身
```

或直接把目录从数组里拿出来单独判断、计数器初值等于文件数。这样对正版安装行为不变，
但不会因为"文件更全"而失败。
