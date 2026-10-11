# 03 · bot 贴脸刷新 / 固定刷点反复补人 —— BSG 把安全距离写成了 1.73 米

## 现象

- bot（scav / PMC）会在**玩家附近**直接出现，甚至贴脸；
- 清掉一处敌人后，过一会儿**同一个固定位置**又刷出新的。

SPT 官方 FAQ 把这条列为"已知行为"：

> SPT uses the game's PvE bot spawning system. ... **Bot spawns aren't checked for the distance to you
> or other bots** which can let bots can spawn next to you.

官方建议装刷怪 MOD。但把代码翻出来看之后，这其实**不是设计取舍，而是一个写错的常量**。

## 根因（反编译证据）

选点调用链：

```
EFT.BotSpawner.TryToSpawnInZoneInner(...)
    list = SpawnSystem.SelectAISpawnPoints(data, botZone, count, null, ActionIfNotEnoughPoints.DuplicateIfAtLeastOne);
    SpawnBotsInZoneOnPositions(list.ToList(), botZone, data);
        └─ SpawnSystemClass.SelectAISpawnPoints(...) -> method_0(...)   // 真正的筛选
```

`SpawnSystemClass.method_0` 对每个候选刷点依次做有效性过滤，其中距离那一项是：

```csharp
// SpawnSystemClass.method_0 中（普通 bot 分支）
HashSet<IPlayer> hashSet = smethod_3(difficulty, role, "SelectPoints.baseCase").ToHashSet();
hashSet.AddRange(range);                    // range = 非 AI 玩家（也就是你）
IPlayer[] enemies = hashSet.ToArray();
...
flag3 = flag3 && GClass3586.IsValid(spawnPoint, enemies, 3f, debugCollector);   // ← 这里
```

被调用的判定函数：

```csharp
// GClass3586.IsValid(ISpawnPoint spawnPoint, IReadOnlyCollection<IPlayer> players,
//                    float distanceSqr, GClass699 debugCollector)
foreach (IPlayer player in players)
{
    bool inside = spawnPoint.Collider.Contains(player.Position);
    if (!inside)
    {
        float sqrMagnitude = (spawnPoint.Position - player.Position).sqrMagnitude;
        if (sqrMagnitude >= distanceSqr) continue;   // 够远 → 放过
        return false;                                 // 太近 → 否决
    }
    return false;                                     // 玩家就在刷点球体内 → 否决
}
return true;
```

**参数名是 `distanceSqr`（平方距离），而调用方传的是常量 `3f`。**
于是真实的安全距离是 `√3 ≈ 1.73 米` —— 对一个"刷怪点是否离玩家太近"的判断来说，等于没有检查。
同样的问题也出现在 `SpawnSystemClass.IsValidSpawn`（它把 `ExceptAI(allPlayers)` 传进去，阈值同样是 `3f`）。

结论：**只要玩家不在刷点球体（`SpawnColliderParams.Radius`，常见 20 米左右）内部，
且离刷点超过 1.73 米，这个点就会被判定为"合法"** —— bot 当然会出现在你脸上。

## 修复设计（[plugin/SpawnDistanceGuard](../plugin/SpawnDistanceGuard)）

思路：**不改原逻辑，只把那个被传错的阈值抬到合理值**，让既有过滤链自己生效。

```
[HarmonyPatch] GClass3586.IsValid(ISpawnPoint, IReadOnlyCollection<IPlayer>, float, GClass699)
  Prefix(ref float distanceSqr)   → distanceSqr = max(distanceSqr, MinimumDistanceMeters²)
  Postfix(ref bool __result)      → 再用 GameWorld 里真实玩家的坐标兜一道
```

要点：

- **Prefix 只抬高、不降低**：原版若在别的调用点传了更大的值（例如 PMC 路径的
  `SpawnSystemClass/Class2557.distanceSqr`），不会被我改小。
- 返回 `false` 的点会被 `SpawnSystemClass.method_0` 的既有过滤链自动丢弃，系统会改挑别的点；
  合格点不足时走它原有的 `ActionIfNotEnoughPoints` 逻辑 → **波次延迟重试**，
  即"宁可不刷，也不刷你脸上"。
- **Postfix 兜底**：某些调用方传入的玩家集合里可能没有真人（例如突袭刚开始、玩家尚未注册进
  `IplayersCollection_0`），此时原函数会直接返回 `true`。Postfix 用
  `Singleton<GameWorld>.Instance.MainPlayer` + `AllAlivePlayersList`（过滤 `IsAI`）再判一次。
- 阈值可配置（默认 60 米），游戏内 F12 可实时改。

### 为什么用 Harmony 而不是改服务端

突袭中**服务端拿不到玩家的实时坐标**，它只在开局把"波次 + 区域(Zone)"下发到客户端；
真正挑具体刷点的是客户端。所以：

- 改服务端配置（刷新间隔/数量/区域）只能**降低频率**，无法变成距离检查
  —— 这类做法见 [`tools/tune-botspawn-config.ps1`](../tools/tune-botspawn-config.ps1)（可一键还原）；
- 社区 MOD 如 ABPS / SWAG+Donuts 也是在"波次与区域/自定义刷点"层面工作，其配置项里没有
  "与玩家的实时距离"这一项（ABPS 2.0.x 的 `config.default.json` 实测无 distance 相关键）。
- 只有客户端补丁能实现真正的距离判断。

## 验证

`BepInEx\LogOutput.log`：

```
[SpawnDistanceGuard] 已加载并打上补丁。minDistance=60m enabled=True
[SpawnDistanceGuard] 刷点判定被调用 #1（补丁已在工作）
[SpawnDistanceGuard] 心跳：累计判定 128 个刷点候选，拦截 37 个过近刷点（阈值 60m）
[SpawnDistanceGuard] 拦下刷点 id=6670b804-... zone=ZoneDormitory 距玩家 37.4m（阈值 60m）
```

`刷点判定被调用` 说明补丁确实挂上了（`AccessTools` 找到目标方法），
`拦下刷点` 的每一条都带**实际距离**，可以直接判断阈值是否合适。

## 阈值建议与取舍

| 地图规模 | 建议值 | 说明 |
|---|---|---|
| 大图（海关/森林/海岸线/街区/立交桥） | 80 ~ 120 | 体验最好，敌人仍会从远处推进过来 |
| 小图（工厂/实验室/迷宫） | 40 ~ 60 | 地图本身就小，设太大会导致波次一直延迟、地图显空 |

阈值设得过大时的表现是：**敌人不来**，而不是敌人贴脸 —— 如果觉得地图太空，往下调即可。

## 已知局限

本插件拦的是"刷在你附近"。**同一个刷点被反复选中**这一问题它不额外处理：
被拦下的点不会被"冷却"，只是这一轮不合格。若你需要"最近用过的点降权"，
可以在 Prefix 里维护一份 (spawnPoint.Id → 上次使用时间) 的表并返回 false，
不过这属于另一个特性，本仓库暂未实现。
