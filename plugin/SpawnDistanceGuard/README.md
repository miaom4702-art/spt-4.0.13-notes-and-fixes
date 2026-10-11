# SpawnDistanceGuard · SPT 4.0.13 刷点距离保护

修复"bot 贴脸刷新 / 固定刷点反复补人"的 BepInEx 5 插件。
原理与取证过程见 [docs/03](../../docs/03-bot-spawn-distance-1.73m.md)。

## 它做了什么

游戏里负责挑 AI 刷点的函数被调用时，传给它的"安全距离"参数是常量 `3f`——
注意那是**平方距离**，换算成实际距离只有 `√3 ≈ 1.73 米`，等于没有检查。

本插件用 Harmony：

- **Prefix**：把该参数抬高到 `MinimumDistanceMeters²`（只抬高，不降低）；
- **Postfix**：再用 `GameWorld` 里真实玩家的坐标兜一道，防止某些调用路径的玩家集合里没有真人。

被否决的刷点由游戏原有过滤链自动丢弃，改挑别的点；合格点不足时走原有的延迟重试逻辑，
表现为"敌人晚点从远处来"，而不是"刷在你脸上"。

## 安装

```
<SPT 根>\BepInEx\plugins\SpawnDistanceGuard\SpawnDistanceGuard.dll
```

启动游戏后自动生成 `<SPT 根>\BepInEx\config\dsh.spt.spawndistanceguard.cfg`：

```ini
[General]
Enabled = true
# AI 刷新点与玩家的最小距离（米）。原版实际是 sqrt(3) ≈ 1.73 米。
# 建议：大图 80~120；工厂/实验室等小图 40~60
MinimumDistanceMeters = 60
LogBlockedPoints = true
```

游戏内按 **F12**（需 Configuration Manager）可实时改；改 cfg 文件需重启游戏。

## 构建

需要 .NET SDK（9.0.x 验证通过）与一份 SPT 4.0.13 安装：

```powershell
dotnet build -c Release -p:SptRoot="C:\Games\SPT"
# 产物：bin\Release\net472\SpawnDistanceGuard.dll
```

或直接跑：

```powershell
.\build.ps1 -SptRoot "C:\Games\SPT" -Deploy
```

`-Deploy` 会把 DLL 复制进 `BepInEx\plugins\SpawnDistanceGuard\`。

`SptRoot` 下需要存在：

```
EscapeFromTarkov_Data\Managed\{Assembly-CSharp,UnityEngine,UnityEngine.CoreModule,Comfort}.dll
BepInEx\core\{BepInEx,0Harmony}.dll
```

## 验证它在工作

看 `<SPT 根>\BepInEx\LogOutput.log`，进图后应有：

```
[SpawnDistanceGuard] 已加载并打上补丁。minDistance=60m enabled=True
[SpawnDistanceGuard] 刷点判定被调用 #1（补丁已在工作）
[SpawnDistanceGuard] 心跳：累计判定 128 个刷点候选，拦截 37 个过近刷点（阈值 60m）
[SpawnDistanceGuard] 拦下刷点 id=6670b804-... zone=ZoneDormitory 距玩家 37.4m（阈值 60m）
```

若看到 `找不到类型 GClass3586` / `找不到 IsValid(...)`，说明游戏版本与本插件不匹配
（本插件针对 **EFT 0.16.9.0.40087 + SPT 4.0.13**；游戏更新后混淆类名可能变化）。

## 卸载

删掉 `BepInEx\plugins\SpawnDistanceGuard` 整个文件夹即可，无需改动游戏或服务端任何文件。

## 兼容性与风险

- 目标方法 `GClass3586.IsValid(ISpawnPoint, IReadOnlyCollection<IPlayer>, float, GClass699)` 是
  **混淆名**，游戏更新后有失效的可能（失效时插件会记录错误日志，不会导致崩溃）。
- 若同时使用其它刷怪类 MOD（ABPS / SWAG+Donuts 等），它们改的是"波次与区域/刷点集合"，
  本插件改的是"候选点的合法性判定"，两者理论上可以共存；实际效果请自行观察。
- 阈值过大时的副作用是**敌人变少/波次延迟**，不是崩溃。觉得空就调小。
