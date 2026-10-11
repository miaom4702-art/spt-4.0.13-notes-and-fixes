# SPT 4.0.13 / EFT 0.16.9.0.40087 踩坑记录与修复补丁

本仓库记录我在搭建、游玩 **SPT 4.0.13**（对应正版客户端 `0.16.9.0.40087`）过程中**实际遇到并定位**的问题，
以及其中一个问题的**修复补丁**（BepInEx 插件，带源码）。

所有结论都来自实测或反编译证据，不是猜的。每条都附上定位过程与验证方法，方便你复现或核对。

## 问题一览

| # | 现象 | 性质 | 状态 |
|---|---|---|---|
| 1 | 启动器点 Start Game 报 `Escape From Tarkov isn't installed on your computer` | **SPT 4.0.13 启动器自身的差一(off-by-one) bug** | 已定位，附最小满足条件 |
| 2 | 能进游戏但卡在加载画面、没有主菜单，日志 `Unable to complete SSL connection` | 系统代理（Clash 等）劫持了 `127.0.0.1` 的 HTTPS | 已定位，附规避方法 |
| 3 | bot 贴脸刷新、同一个固定点反复补人 | **BSG 的逻辑 bug**：AI 刷点的"安全距离"被写成 √3 ≈ **1.73 米** | ✅ 已修复，[插件源码](plugin/SpawnDistanceGuard) |

## 目录

```
docs/
  01-launcher-validation-offbyone.md   启动器校验的差一 bug（附 .NET startup hook 取证脚本）
  02-menu-tls-proxy.md                 卡加载画面 / Unable to complete SSL connection
  03-bot-spawn-distance-1.73m.md       ⭐ bot 贴脸刷的根因与修复设计
  04-manual-install-40087.md           手动安装 SPT 4.0.13 的要点（.NET 9、补丁、校验）
plugin/
  SpawnDistanceGuard/                  ⭐ 修复插件（C# / BepInEx 5 / Harmony），含源码与构建脚本
tools/
  launcher-probe/                      在启动器进程内反射调用其校验函数、导出真实字符串的取证工具
  analysis/                            反编译辅助脚本（Mono.Cecil 交叉引用，用于定位 Harmony 补丁点）
  tune-botspawn-config.ps1             "低侵入替代方案"：不改代码，只压刷新频率/数量（可一键还原）
LICENSE                                MIT（仅覆盖本仓库自有代码）
```

## 问题 3 速览（最值得看的一条）

SPT / 正版客户端里负责挑 AI 刷点的函数（反编译 `Assembly-CSharp.dll`）:

```csharp
// GClass3586.IsValid(ISpawnPoint spawnPoint, IReadOnlyCollection<IPlayer> players,
//                    float distanceSqr, GClass699 debugCollector)
foreach (IPlayer player in players)
{
    if (spawnPoint.Collider.Contains(player.Position)) return false;                       // 玩家在刷点球体内
    if ((spawnPoint.Position - player.Position).sqrMagnitude < distanceSqr) return false;  // 距离不够
}
return true;
```

判定逻辑是对的，**但调用方传进来的 `distanceSqr` 是常量 `3f`**（`SpawnSystemClass.IsValidSpawn` 与
`SpawnSystemClass.method_0` 都是）。注意参数名——它是**平方距离**，所以真实安全距离只有
`√3 ≈ 1.73 米`。等于**没有距离检查**，于是 bot 会刷新在你脸上、并在固定刷点被反复复用。

修复：用 Harmony 把该参数抬高到 `MinimumDistanceMeters²`（只抬高、不降低），并用真实玩家坐标兜一道。
详见 [docs/03-bot-spawn-distance-1.73m.md](docs/03-bot-spawn-distance-1.73m.md)。

## 插件安装（问题 3）

1. 下载/构建 `SpawnDistanceGuard.dll`
2. 放到 `<SPT 根目录>\BepInEx\plugins\SpawnDistanceGuard\SpawnDistanceGuard.dll`
3. 启动游戏，配置文件自动生成：`BepInEx\config\dsh.spt.spawndistanceguard.cfg`

```ini
[General]
Enabled = true
# AI 刷新点与玩家的最小距离（米）。原版实际是 sqrt(3) ≈ 1.73 米。
# 建议：大图 80~120；工厂/实验室等小图 40~60
MinimumDistanceMeters = 60
LogBlockedPoints = true
```

游戏内按 **F12**（需 Configuration Manager）可实时改。

进图后 `BepInEx\LogOutput.log` 应该出现：

```
[SpawnDistanceGuard] 刷点判定被调用 #1（补丁已在工作）
[SpawnDistanceGuard] 心跳：累计判定 128 个刷点候选，拦截 37 个过近刷点（阈值 60m）
[SpawnDistanceGuard] 拦下刷点 id=... zone=ZoneDormitory 距玩家 37.4m（阈值 60m）
```

## 自行构建

需要 .NET SDK（本项目用 9.0.x 验证）与一份 SPT 4.0.13 安装（用于引用游戏程序集）：

```powershell
cd plugin\SpawnDistanceGuard
dotnet build -c Release -p:SptRoot="D:\ETF\SPT-4.0.13"
```

`SptRoot` 指向你的 SPT 根目录（内含 `EscapeFromTarkov_Data\Managed` 与 `BepInEx\core`）。

## 免责声明

- 本仓库**不包含**任何游戏本体文件、游戏程序集、证书、存档或 SPT 发行包，只包含自写代码、脚本与文字记录。
- 所有内容仅用于**单机(Single Player Tarkov)** 场景，不涉及、也不支持任何联机/在线模式用途。
- 与 Battlestate Games、SPT 项目组均无隶属关系。SPT 本身免费，若你为它付过钱，那是被骗了。
- 插件按原样提供（AS IS），请自行评估风险；改客户端行为类插件在游戏更新后可能失效。

---

## English summary

Notes and fixes collected while setting up **SPT 4.0.13** (retail client `0.16.9.0.40087`):

1. **Launcher: "Escape From Tarkov isn't installed on your computer"** — an off-by-one bug in SPT 4.0.13's
   `ValidationUtil.Validate()`: the counter starts at `array.Length - 1` (the array *includes* the install
   directory) and is decremented for every existing entry, so validation only passes when **exactly 5 of 6**
   entries exist. Real BSG installs satisfy this by accident because they ship `BattlEye\BEService_x64.exe`
   but not `BEService_x64.dll`. See `docs/01`.
2. **Stuck on the loading screen / `Unable to complete SSL connection`** — the game's own HTTP stack follows
   the Windows system proxy, so a local proxy (Clash etc.) hijacks `https://127.0.0.1:6969` and the TLS
   handshake dies. The SPT plugin's requests still work because SPT patches them to bypass the proxy. See `docs/02`.
3. **Bots spawning right next to the player** — the AI spawn-point validity check is called with
   `distanceSqr = 3f`, i.e. a real safety distance of `√3 ≈ 1.73 m`: effectively no check at all.
   `plugin/SpawnDistanceGuard` raises that threshold via a Harmony prefix (plus a fallback postfix against the
   live player position). See `docs/03`.

Build the plugin with `dotnet build -c Release -p:SptRoot="<your SPT root>"`.
