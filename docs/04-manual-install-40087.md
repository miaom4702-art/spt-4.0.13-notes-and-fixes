# 04 · 手动安装 SPT 4.0.13（客户端 0.16.9.0.40087）的要点

官方 4.0 手动安装步骤见 SPT wiki；这里只记录**实测中真正会卡住人的地方**。

## 版本对应关系（先确认这个）

| SPT 版本 | 支持的正版客户端 build |
|---|---|
| **4.0.13** | **0.16.9.0.40087** |
| 4.1.x | 0.16.9.0.40743 等更新的 build |

- 只要你的客户端 build 已经是 **40087**，官方流程里的"降级补丁(downgrade patcher)"**不需要**，
  直接"复制客户端 → 覆盖 SPT 发行包"即可。
- 版本不对时会卡在启动器报 `core_dll_file_version_mismatch`（错误码 -8）。

## 需要 .NET 9 运行时（很容易漏）

`SPT.Server.exe` 是 ASP.NET Core 应用，需要 **ASP.NET Core 9** 运行时；启动器还需要
**WindowsDesktop 9**。如果机器上只有 .NET 8/10，会直接报：

```
You must install or update .NET to run this application.
Framework: 'Microsoft.AspNetCore.App', version '9.0.0' (x64)
```

两种装法：

1. 官方安装包（需要管理员）：
   `.NET Desktop Runtime 9` + `ASP.NET Core Runtime 9`。
2. **免管理员**：用官方 `dotnet-install.ps1` 装到独立目录，启动时用 `DOTNET_ROOT` 指过去：

   ```powershell
   # 下载 https://dot.net/v1/dotnet-install.ps1 后：
   .\dotnet-install.ps1 -Runtime aspnetcore    -Channel 9.0 -InstallDir D:\SPT-dotnet9 -NoPath
   .\dotnet-install.ps1 -Runtime windowsdesktop -Channel 9.0 -InstallDir D:\SPT-dotnet9 -NoPath
   ```

   启动脚本里设置（**服务端和启动器都要**，否则会报缺运行时）：

   ```bat
   set "DOTNET_ROOT=D:\SPT-dotnet9"
   set "DOTNET_ROOT_X64=D:\SPT-dotnet9"
   set "PATH=D:\SPT-dotnet9;%PATH%"
   ```

   注意：**不要**把 `DOTNET_ROOT` 设成用户级环境变量全局生效，那样会影响机器上其他 .NET 应用；
   写在专用的 .bat 里即可。

## 服务端必须有自己的控制台窗口

`SPT.Server.exe` 启动时会调用 `GetConsoleMode`。如果你把它以**重定向输出**的方式启动
（`Start-Process -RedirectStandardOutput ...`、`> log.txt` 等），会直接崩：

```
System.Exception: Unable to get console mode
   at SPTarkov.Server.Program.SetConsoleOutputMode()
```

想后台跑就别重定向输出；要判断是否就绪，看 `SPT\user\logs\spt\*.log` 里的
`Server has started, happy playing`，或探测 `127.0.0.1:6969` 是否在监听。

## 客户端文件是否"纯净"可以自查

客户端根目录的 `ConsistencyInfo` 是 BSG 的完整性清单（JSON）：

```
{"Version":"0.16.9.0.40087","Entries":[{"Path":"EscapeFromTarkov.exe","Hash":"<md5>","Size":...}, ...]}
```

把关键文件的 MD5 与清单比对，可以确认客户端没被动过（这对"增量补丁能否套用"很关键）：

```powershell
$txt = Get-Content '<SPT 根>\ConsistencyInfo' -Raw
$o = $txt.Substring($txt.IndexOf('{')) | ConvertFrom-Json
$md5 = [Security.Cryptography.MD5]::Create()
foreach ($t in 'EscapeFromTarkov.exe','EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll') {
  $e = $o.Entries | Where-Object { $_.Path -eq $t } | Select-Object -First 1
  $h = [BitConverter]::ToString($md5.ComputeHash([IO.File]::ReadAllBytes((Join-Path '<SPT 根>' $t)))).Replace('-','').ToLower()
  "{0,-56} match={1}" -f $t, ($h -eq $e.Hash.ToLower())
}
```

## 增量补丁可以离线验证

启动器打的是 **HDiffPatch** 格式的补丁（`Assembly-CSharp.dll.delta`，zstd 压缩）。
想确认它能否套用、或想手动排查补丁失败，可以用官方 `hpatchz.exe` 在**副本**上试：

```powershell
hpatchz.exe <原版 Assembly-CSharp.dll> <...Assembly-CSharp.dll.delta> <输出.dll>
```

实测输出（build 40087）：

```
input oldDataSize: 15994432
     diffDataSize: 4337034
     diffDataType: HDiff
saved newDataSize: 15899648
     compressType: "zstd"
patch ok!
```

启动器每次开游戏时会先把 `.spt-bak` 备份还原、再重新打补丁，所以不用担心"补丁打两次"。

## 常见错误码对照（启动器）

| 码 | 提示 key | 含义 |
|---|---|---|
| -1 | `installed_in_live_game_warning` | SPT 被装进了正版客户端目录里 |
| -2 | `no_official_game_warning` | 校验失败（见 [docs/01](01-launcher-validation-offbyone.md)） |
| -3 | `failed_to_receive_patches` | 收不到补丁 |
| -4 | `failed_core_patch` | 核心补丁失败（多半是客户端文件版本不对） |
| -5 | `failed_mod_patch` | MOD 补丁失败 |
| -6 | `eft_exe_not_found_warning` | Game Path 下没有 EscapeFromTarkov.exe |
| -8 | `core_dll_file_version_mismatch` | `BepInEx\plugins\spt\spt-core.dll` 与服务端版本不一致 |

## 其他实测细节

- 启动器与 `SPT.Server.exe` **必须留在 `<SPT 根>\SPT` 目录内**（移动出去会报 Watermark 错误）。
- `BatilEye`、`ConsistencyInfo`、`UnityCrashHandler64.exe` 等会被启动器在开游戏前从 **SPT 副本**里删掉，
  这是设计行为；但**正版安装目录**里对应的这些文件必须保留（校验要用）。
- `SPT_Data/database/locations/<map>/base.json` 是纯文本 JSON，改完必须**重启服务端**才生效
  （数据库在服务端启动时导入）。改这些文件时注意**不要写入 UTF-8 BOM**。
