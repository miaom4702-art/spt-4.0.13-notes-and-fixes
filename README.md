# SPT 4.0.13 / EFT 0.16.9.0.40087 问题记录与修复补丁

记录我在搭建、游玩 **SPT 4.0.13**（Single Player Tarkov，对应正版客户端 `0.16.9.0.40087`）过程中
**实际遇到并定位**的问题，并给出修复补丁。所有结论都来自实测或反编译证据，附可复现的取证脚本。

> 与 Battlestate Games、SPT 项目组均无隶属关系。本项目仅用于**单机模式**，不涉及任何联机用途。

---

## 一、项目解决什么问题

三个实际卡住人的问题（前两个是环境/程序缺陷，第三个是**主要交付的修复**）：

| # | 现象 | 根因 | 本项目的处理 |
|---|---|---|---|
| 1 | 启动器点 `Start Game` 报 *Escape From Tarkov isn't installed on your computer* | SPT 4.0.13 启动器 `ValidationUtil.Validate()` 的**差一 bug**：计数器初值为 `数组长度-1`，而数组里**把安装目录本身也算作一项**，于是要求"6 项里恰好存在 5 项"才通过 | 定位并给出满足条件与给上游的修复 diff → [docs/01](docs/01-launcher-validation-offbyone.md) |
| 2 | 游戏能启动但卡在加载画面、**没有主菜单**，日志 `Unable to complete SSL connection` | 游戏自身网络栈遵循**系统代理**，把 `https://127.0.0.1:6969` 也发给代理（Clash 等），TLS 握手失败；SPT 插件的请求因被 patch 绕开代理而正常，表现为"一半请求成功" | 定位并给出三种规避方案 → [docs/02](docs/02-menu-tls-proxy.md) |
| 3 | **bot 贴脸刷新**、清掉后同一个固定刷点反复补人 | BSG 的逻辑 bug：AI 刷点合法性判定 `GClass3586.IsValid(..., float distanceSqr, ...)` 被调用时传入常量 **`3f`**。该参数是**平方距离**，真实安全距离只有 `√3 ≈ 1.73 米`，等于没有距离检查 | ✅ **修复补丁** → [plugin/SpawnDistanceGuard](plugin/SpawnDistanceGuard)、[docs/03](docs/03-bot-spawn-distance-1.73m.md) |

第 3 条的关键证据（反编译 `Assembly-CSharp.dll`）：

```csharp
// GClass3586.IsValid(ISpawnPoint sp, IReadOnlyCollection<IPlayer> players, float distanceSqr, ...)
foreach (IPlayer player in players) {
    if (sp.Collider.Contains(player.Position)) return false;                        // 玩家在刷点球体内
    if ((sp.Position - player.Position).sqrMagnitude < distanceSqr) return false;   // 距离不够
}
return true;

// 而调用方 SpawnSystemClass.IsValidSpawn / method_0 传的是：
GClass3586.IsValid(spawnPoint, enemies, 3f, debugCollector);   // ← 3f 是平方距离 = 1.73 米
```

---

## 二、主要功能

### 1. `SpawnDistanceGuard` 修复插件（主要交付物）

- **Prefix 抬高阈值**：把上述 `distanceSqr` 参数抬到 `MinimumDistanceMeters²`。
  *只抬高、不降低*，因此不影响其它调用点传入的更大值。
- **Postfix 兜底判定**：再用 `GameWorld.MainPlayer` / `AllAlivePlayersList`（过滤 `IsAI`）复判一次，
  覆盖"调用方传入的玩家集合里没有真人"的路径（例如突袭刚开始、玩家还没注册进 spawn system）。
- **不改变原有流程**：返回 `false` 的刷点由游戏既有过滤链自动丢弃、改挑别的点；
  合格点不足时走它原有的 `ActionIfNotEnoughPoints` 逻辑 → 波次延迟重试。
  效果是"敌人晚点从远处来"，而不是"刷在你脸上"。
- **可配置、可观测**：阈值/开关在 `BepInEx\config\dsh.spt.spawndistanceguard.cfg`，
  游戏内 F12 可实时改；日志会打印每个被拦下的刷点及其**实际距离**。

### 2. 问题 1 / 问题 2 的取证与处置

- **启动器取证探针**（`tools/launcher-probe`）：利用 .NET `DOTNET_STARTUP_HOOKS` 在**启动器自己的进程里**
  反射调用 `GameStarter.DetectOriginalGamePath()` / `ValidationUtil.Validate()`，
  并解析方法 IL 里全部 `ldstr` 常量 —— 用于绕过启动器核心的**字符串加密**，拿到真实的注册表路径与文件名清单。
- **反编译辅助脚本**（`tools/analysis`）：用 SPT 自带的 Mono.Cecil 做交叉引用，
  定位"谁调用了刷点判定、前面传了什么常量"，便于游戏更新后重新定位补丁点。
- **低侵入替代方案**（`tools/tune-botspawn-config.ps1`）：不改任何代码，只压
  `BotSpawnPeriodCheck` / `BotSpawnCountStep` / `MaxBotPerZone` 与 `botTypeLimits.min`，
  自动备份并可一键还原。**注意：这不是距离检查**，只是降低刷新强度。

---

## 三、安装方法

### 前置条件

| 项目 | 要求 |
|---|---|
| 系统 | Windows |
| 游戏 | EFT **0.16.9.0.40087** + **SPT 4.0.13**（版本必须匹配，否则启动器会报 `-8`） |
| 运行时 | BepInEx 5.4.x（SPT 自带）、.NET Framework 4.7.2（Windows 自带） |
| 构建插件才需要 | .NET SDK 9.0.x |

### 方式 A：用 Release 里的现成 DLL（推荐）

1. 到 [Releases](https://github.com/miaom4702-art/spt-4.0.13-notes-and-fixes/releases) 下载
   `SpawnDistanceGuard.dll`；
2. 复制到：

   ```
   <SPT 根目录>\BepInEx\plugins\SpawnDistanceGuard\SpawnDistanceGuard.dll
   ```

3. 启动游戏一次，配置文件自动生成在
   `<SPT 根目录>\BepInEx\config\dsh.spt.spawndistanceguard.cfg`。

### 方式 B：自己编译

```powershell
git clone https://github.com/miaom4702-art/spt-4.0.13-notes-and-fixes.git
cd spt-4.0.13-notes-and-fixes\plugin\SpawnDistanceGuard

# -p:SptRoot 指向你的 SPT 根目录（内含 EscapeFromTarkov_Data\Managed 与 BepInEx\core）
dotnet build -c Release -p:SptRoot="C:\Games\SPT"

# 或一键构建并部署：
.\build.ps1 -SptRoot "C:\Games\SPT" -Deploy
```

产物：`bin\Release\net472\SpawnDistanceGuard.dll`。

### 卸载

删掉 `<SPT 根目录>\BepInEx\plugins\SpawnDistanceGuard` 整个文件夹即可，**不需要**改动游戏或服务端任何文件。

---

## 四、使用方法

### 1. 配置项

`<SPT 根目录>\BepInEx\config\dsh.spt.spawndistanceguard.cfg`：

```ini
[General]

## 是否启用距离保护
Enabled = true

## AI 刷新点与玩家的最小距离（米）。原版实际是 sqrt(3) ≈ 1.73 米。
## 建议：大图 80~120；工厂/实验室等小图 40~60
MinimumDistanceMeters = 60

## 把被拦下的刷点写进 BepInEx 日志（用于确认插件生效）
LogBlockedPoints = true
```

改配置的两种方式：

- **游戏内**：按 `F12`（需 Configuration Manager，SPT 默认自带）实时修改；
- **改文件**：编辑上面的 `.cfg` 后重启游戏。

### 2. 阈值怎么选

| 地图规模 | 建议 `MinimumDistanceMeters` | 说明 |
|---|---|---|
| 大图：海关 / 森林 / 海岸线 / 街区 / 立交桥 / 灯塔 / 储备站 | **80 ~ 120** | 敌人仍会从远处推进过来，体验最好 |
| 小图：工厂 / 实验室 / 迷宫 | **40 ~ 60** | 地图本身小，设太大会导致波次一直延迟、地图显空 |

阈值偏大的副作用是**敌人变少 / 波次延迟**（不是崩溃、不是报错）。觉得地图空就往下调。

### 3. 其它工具的使用

```powershell
# 启动器校验失败时，先看它到底在校验什么
.\tools\launcher-probe\run-probe.ps1 -SptRoot "C:\Games\SPT" -DotnetRoot "D:\SPT-dotnet9"

# 游戏更新后重新定位刷点逻辑 / 补丁点
.\tools\analysis\find-spawn-code.ps1 -SptRoot "C:\Games\SPT"

# 只想降刷新强度（可一键还原；不是距离检查）
.\tools\tune-botspawn-config.ps1 -SptRoot "C:\Games\SPT" -ZeroTypeLimitsMin
.\tools\tune-botspawn-config.ps1 -SptRoot "C:\Games\SPT" -Restore
```

---

## 五、输入输出示例

### 示例 1：插件运行时（输入 = 配置；输出 = 日志）

**输入**

```ini
MinimumDistanceMeters = 60
LogBlockedPoints = true
```

**输出**（`<SPT 根目录>\BepInEx\LogOutput.log`，进图后出现）

```
[Info   :SPT Spawn Distance Guard] [SpawnDistanceGuard] 已加载并打上补丁。minDistance=60m enabled=True
[Info   :SPT Spawn Distance Guard] [SpawnDistanceGuard] 刷点判定被调用 #1（补丁已在工作）
[Info   :SPT Spawn Distance Guard] [SpawnDistanceGuard] 心跳：累计判定 128 个刷点候选，拦截 37 个过近刷点（阈值 60m）
[Info   :SPT Spawn Distance Guard] [SpawnDistanceGuard] 拦下刷点 id=6670b804-5f1a-45f1-a462-585cb8619d09 zone=ZoneDormitory 距玩家 37.4m（阈值 60m）
```

- `刷点判定被调用 #N` → 补丁确实挂上了（`AccessTools` 找到了目标方法）；
- `拦下刷点 ... 距玩家 37.4m` → 该刷点距你 37.4 米，被拦下，游戏改挑别的点。

**游戏版本不匹配时的输出**（不会崩溃，只是不生效）：

```
[Error  :SPT Spawn Distance Guard] [SpawnDistanceGuard] 找不到类型 GClass3586，补丁未生效
```

### 示例 2：启动器取证探针（输入 = 命令；输出 = 校验真相）

**输入**

```powershell
.\tools\launcher-probe\run-probe.ps1 -SptRoot "C:\Games\SPT" -DotnetRoot "D:\SPT-dotnet9"
```

**输出**（`%TEMP%\spt-launcher-probe.log`）

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
  Int32   v0 = '-1'          <-- 6 项全存在 → 计数器 -1 → 判定失败
```

对照实验（同一目录下不同文件数量）：

| 该目录实际存在的项 | `v0` | `Validate()` |
|---|---|---|
| 4 项 | 1 | false |
| 6 项 | **-1** | **false** |
| 5 项 | 0 | ✅ true |

### 示例 3：配置调优脚本（输入 = 参数；输出 = 改动表 + 备份）

**输入**

```powershell
.\tools\tune-botspawn-config.ps1 -SptRoot "C:\Games\SPT" -PeriodCheck 90 -CountStep 1 -MaxBotPerZone 2
```

**输出**

```
bigmap          BotSpawnPeriodCheck 15->90, BotSpawnCountStep 4->1, MaxBotPerZone 4->2
factory4_day    BotSpawnPeriodCheck 15->90, BotSpawnCountStep 3->1, MaxBotPerZone 4->2
...（共 13 张实战图）
备份 + 还原脚本: C:\Games\SPT\_botspawn_backup_20261011_103005
全部 JSON 校验通过。改完请重启 SPT.Server。
```

### 示例 4：构建插件（输入 = 命令；输出 = 产物）

```
> dotnet build -c Release -p:SptRoot="C:\Games\SPT"
  SpawnDistanceGuard -> ...\bin\Release\net472\SpawnDistanceGuard.dll
  已成功生成。 0 个警告 0 个错误
```

---

## 文档索引

| 文档 | 内容 |
|---|---|
| [docs/01](docs/01-launcher-validation-offbyone.md) | 启动器校验的差一 bug：证据、最小满足条件、给上游的修复 diff |
| [docs/02](docs/02-menu-tls-proxy.md) | 卡加载画面 / `Unable to complete SSL connection`：代理劫持的对照实验与规避方案 |
| [docs/03](docs/03-bot-spawn-distance-1.73m.md) | ⭐ bot 贴脸刷的根因、修复设计、阈值取舍、已知局限 |
| [docs/04](docs/04-manual-install-40087.md) | 手动安装要点：.NET 9 运行时、服务端控制台要求、`ConsistencyInfo` 自检、hpatchz 离线验证、错误码对照 |

## 仓库结构

```
plugin/SpawnDistanceGuard/     修复插件（C# / BepInEx 5 / Harmony），含源码与构建脚本
tools/launcher-probe/          启动器取证探针（.NET startup hook）
tools/analysis/                Mono.Cecil 交叉引用脚本（定位 Harmony 补丁点）
tools/tune-botspawn-config.ps1 低侵入替代方案（压刷新强度，可还原）
docs/                          四个问题的详细记录
```

## 兼容性与风险

- 目标方法 `GClass3586.IsValid(ISpawnPoint, IReadOnlyCollection<IPlayer>, float, GClass699)` 是
  **游戏混淆后的名字**，游戏更新后可能失效。失效时表现为日志里记录"找不到类型/方法"，
  **不会导致崩溃**；此时用 `tools/analysis/find-spawn-code.ps1` 重新定位即可。
- 与其它刷怪类 MOD（ABPS / SWAG+Donuts 等）改的不是同一层：它们管"波次与刷点集合"，
  本插件管"候选点的合法性判定"，理论上可共存；实际效果请自行观察。
- 本仓库**不包含**任何游戏文件、游戏程序集、SPT 发行包、证书或存档；编译时仅以
  `<Private>false</Private>` 方式**引用**游戏程序集，不会随产物分发。

## 声明

- 与 Battlestate Games、SPT 项目组均无隶属关系；SPT 免费，若你为它付过钱，那是被骗了。
- 仅用于单机（Single Player Tarkov）场景。
- 代码按原样提供（AS IS），改客户端行为类插件请自行评估风险。
- 许可证：[MIT](LICENSE)（仅覆盖本仓库自有代码与文档）。

---

## English summary

Notes and a fix for **SPT 4.0.13 / EFT 0.16.9.0.40087**:

1. **Launcher: "Escape From Tarkov isn't installed on your computer"** — off-by-one bug in SPT 4.0.13's
   `ValidationUtil.Validate()`: the counter starts at `array.Length - 1` while the array *includes* the
   install directory, so validation passes only when **exactly 5 of 6** entries exist. Real BSG installs
   satisfy this by accident (they ship `BattlEye\BEService_x64.exe` but not `BEService_x64.dll`).
2. **Stuck on the loading screen / `Unable to complete SSL connection`** — the game's HTTP stack follows the
   Windows system proxy, so a local proxy hijacks `https://127.0.0.1:6969` and TLS fails, while SPT's own
   plugin requests (patched to bypass the proxy) keep working.
3. **Bots spawning on top of the player** — the AI spawn-point check is called with `distanceSqr = 3f`, i.e. a
   real safety distance of `√3 ≈ 1.73 m`. `plugin/SpawnDistanceGuard` raises that threshold via a Harmony
   prefix (plus a postfix fallback against the live player position). Configurable, and it logs every
   rejected spawn point with its actual distance.

Build: `dotnet build -c Release -p:SptRoot="<your SPT root>"`.
Install: copy the DLL to `<SPT root>\BepInEx\plugins\SpawnDistanceGuard\`.
