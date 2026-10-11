# 在 SPT 启动器进程内取证的 .NET startup hook

SPT 4.0.13 的启动器核心（`SPT.Launcher.Base`）做了**字符串加密/标识符混淆**，
所以用 `strings` 之类的方法在 exe 里搜不到它校验时用的注册表路径、文件名；
日志里那些消息也是在运行时才解密的。

这个探针利用 .NET 的 `DOTNET_STARTUP_HOOKS` 机制，在 **启动器自己的进程里、Main 之前**运行，
然后：

1. 反射调用 `GameStarter.DetectOriginalGamePath()`，拿到它**实际解析出的"正版安装目录"**；
2. 反射调用 `ValidationUtil.Validate()`，拿到真实返回值；
3. 打印 `ValidationUtil` 所有**静态字段的值**（例如那个 `v0` 计数器）；
4. 把 `Validate()` / `a()` / `b()` / `g()` / `d()` 方法 IL 里的所有 `ldstr` 常量解析出来
   —— 因为字符串是运行时解密的，这一步能直接看到**真实的注册表路径与文件名清单**。

## 用法

```powershell
# 1) 构建探针
dotnet build .\SptProbe.csproj -c Release -o .\out

# 2) 用探针启动启动器（会自动关掉已运行的启动器）
.\run-probe.ps1 -SptRoot "C:\Games\SPT" -DotnetRoot "D:\SPT-dotnet9"

# 3) 读结果
Get-Content "$env:TEMP\spt-launcher-probe.log"
```

## 输出示例（实测）

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
  Int32   v0 = '-1'
```

结论与推导见 [`../../docs/01-launcher-validation-offbyone.md`](../../docs/01-launcher-validation-offbyone.md)。

> 探针只读不写：它不修改任何文件，只调用两个静态方法并读取元数据。
