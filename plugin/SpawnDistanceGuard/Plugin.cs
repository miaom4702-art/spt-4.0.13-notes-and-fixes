using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Game.Spawning;
using HarmonyLib;
using UnityEngine;

namespace SpawnDistanceGuard
{
    /// <summary>
    /// 修掉 BSG 在 AI 刷点上的"形同虚设的距离检查"。
    ///
    /// 原版逻辑（反编译自 Assembly-CSharp.dll）:
    ///   GClass3586.IsValid(ISpawnPoint sp, IReadOnlyCollection&lt;IPlayer&gt; players, float distanceSqr, ...)
    ///   {
    ///       foreach (player in players) {
    ///           if (sp.Collider.Contains(player.Position)) return false;      // 玩家在刷点球体内 -> 否决
    ///           if ((sp.Position - player.Position).sqrMagnitude &lt; distanceSqr) return false;  // 太近 -> 否决
    ///       }
    ///       return true;
    ///   }
    /// 调用方 SpawnSystemClass.IsValidSpawn / method_0 传的是 **3f**（平方距离），
    /// 也就是实际安全距离只有 sqrt(3) ≈ 1.73 米 —— 等于没有检查，所以 bot 会贴脸刷。
    ///
    /// 本插件做两件事：
    ///   1) Prefix : 把这个 distanceSqr 抬到 (MinimumDistanceMeters)^2（只抬高，不降低）
    ///   2) Postfix: 再用 GameWorld 里真实的玩家坐标兜一道（防止某些调用方传入的玩家集合里没有真人）
    /// 两者都不改变原逻辑，返回 false 的刷点会被系统自动跳过，找不到点时波次会延迟重试。
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class SpawnDistanceGuardPlugin : BaseUnityPlugin
    {
        public const string Guid = "dsh.spt.spawndistanceguard";
        public const string Name = "SPT Spawn Distance Guard";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> EnabledCfg;
        internal static ConfigEntry<float> MinDistanceCfg;
        internal static ConfigEntry<bool> LogBlockedCfg;

        internal static int BlockedCount;
        internal static int CheckedCount;
        private static float _lastSummaryTime;

        internal static void CountChecked()
        {
            CheckedCount++;
            if (CheckedCount <= 5)
            {
                Log.LogInfo(string.Format("[SpawnDistanceGuard] 刷点判定被调用 #{0}（补丁已在工作）", CheckedCount));
            }
        }

        private void Awake()
        {
            Log = Logger;

            EnabledCfg = Config.Bind(
                "General", "Enabled", true,
                "是否启用距离保护。true = 按 MinimumDistanceMeters 阻止 bot 刷在玩家附近。");

            MinDistanceCfg = Config.Bind(
                "General", "MinimumDistanceMeters", 60f,
                "AI 刷新点与玩家的最小距离（米）。原版这个值实际是 sqrt(3)≈1.73 米。\n" +
                "建议：大图 80~120；工厂/实验室等小图 40~60；设太大可能导致波次一直延迟。");

            LogBlockedCfg = Config.Bind(
                "General", "LogBlockedPoints", true,
                "把被拦下的刷点写进 BepInEx 日志（用于确认插件生效）。");

            try
            {
                var harmony = new Harmony(Guid);
                harmony.PatchAll(typeof(SpawnDistanceGuardPlugin).Assembly);
                Log.LogInfo(string.Format(
                    "[SpawnDistanceGuard] 已加载并打上补丁。minDistance={0}m enabled={1}",
                    MinDistanceCfg.Value, EnabledCfg.Value));
            }
            catch (Exception e)
            {
                Log.LogError("[SpawnDistanceGuard] 打补丁失败: " + e);
            }
        }

        private void Update()
        {
            // 每 60 秒汇报一次计数，方便验证插件是否真的在工作
            if (Time.time - _lastSummaryTime < 60f) return;
            _lastSummaryTime = Time.time;
            if (CheckedCount > 0 || BlockedCount > 0)
            {
                Log.LogInfo(string.Format(
                    "[SpawnDistanceGuard] 心跳：累计判定 {0} 个刷点候选，拦截 {1} 个过近刷点（阈值 {2}m）",
                    CheckedCount, BlockedCount, MinDistanceCfg.Value));
            }
        }

        internal static void ReportBlocked(ISpawnPoint sp, float distance)
        {
            BlockedCount++;
            if (!LogBlockedCfg.Value) return;
            if (BlockedCount <= 20 || BlockedCount % 25 == 0)
            {
                Log.LogInfo(string.Format(
                    "[SpawnDistanceGuard] 拦下刷点 id={0} zone={1} 距玩家 {2:F1}m（阈值 {3}m）",
                    sp.Id, sp.BotZoneName, distance, MinDistanceCfg.Value));
            }
        }

        internal static float MinDistanceSqr
        {
            get
            {
                float d = MinDistanceCfg.Value;
                return d * d;
            }
        }
    }

    /// <summary>
    /// 抬高 GClass3586.IsValid(sp, players, distanceSqr, debugCollector) 的安全距离参数。
    /// </summary>
    [HarmonyPatch]
    internal static class SpawnPointDistancePrefix
    {
        private static MethodBase _target;

        private static bool Prepare()
        {
            // 全局命名空间下的混淆类，按名字+签名查找
            var t = AccessTools.TypeByName("GClass3586");
            if (t == null)
            {
                SpawnDistanceGuardPlugin.Log.LogError("[SpawnDistanceGuard] 找不到类型 GClass3586，补丁未生效");
                return false;
            }

            _target = AccessTools.Method(t, "IsValid", new[]
            {
                typeof(ISpawnPoint),
                typeof(IReadOnlyCollection<IPlayer>),
                typeof(float),
                AccessTools.TypeByName("GClass699")
            });

            if (_target == null)
            {
                SpawnDistanceGuardPlugin.Log.LogError("[SpawnDistanceGuard] 找不到 IsValid(ISpawnPoint, players, float, collector)");
                return false;
            }
            return true;
        }

        private static MethodBase TargetMethod()
        {
            return _target;
        }

        // 抬高阈值：原版 3f(=1.73m) -> 配置值
        private static void Prefix(ref float distanceSqr)
        {
            SpawnDistanceGuardPlugin.CountChecked();
            if (!SpawnDistanceGuardPlugin.EnabledCfg.Value) return;
            float want = SpawnDistanceGuardPlugin.MinDistanceSqr;
            if (distanceSqr < want) distanceSqr = want;
        }

        // 兜底 + 日志：用真实玩家坐标再判一次
        private static void Postfix(ISpawnPoint spawnPoint, ref bool __result)
        {
            if (!SpawnDistanceGuardPlugin.EnabledCfg.Value) return;
            if (spawnPoint == null) return;

            var world = Singleton<GameWorld>.Instance;
            if (world == null) return;

            float minSqr = SpawnDistanceGuardPlugin.MinDistanceSqr;
            float nearestSqr = float.MaxValue;

            var main = world.MainPlayer;
            if (main != null && !main.IsAI)
            {
                float d = (spawnPoint.Position - main.Position).sqrMagnitude;
                if (d < nearestSqr) nearestSqr = d;
            }

            var alive = world.AllAlivePlayersList;
            if (alive != null)
            {
                for (int i = 0; i < alive.Count; i++)
                {
                    var p = alive[i];
                    if (p == null || p.IsAI) continue;
                    float d = (spawnPoint.Position - p.Position).sqrMagnitude;
                    if (d < nearestSqr) nearestSqr = d;
                }
            }

            if (nearestSqr == float.MaxValue) return;      // 拿不到玩家坐标，交给原逻辑
            if (nearestSqr >= minSqr) return;              // 够远

            if (__result)
            {
                __result = false;                          // 原逻辑放过了，这里拦掉
                SpawnDistanceGuardPlugin.ReportBlocked(spawnPoint, Mathf.Sqrt(nearestSqr));
            }
        }
    }
}
