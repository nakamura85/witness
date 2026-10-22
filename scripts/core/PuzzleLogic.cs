using System.Collections.Generic;

namespace Witness;

/// <summary>
/// 环境解谜纯逻辑（v0.4 方案 B：无跳跃，E 互动翻越 + VH 式环境解谜）。
/// 与 Godot 解耦，供 headless 冒烟测试直接断言。
/// </summary>
public static class PuzzleLogic
{
    // ---- 翻越（vault）：替代跳跃的核心位移动词 ----
    public const float MinVaultRise = 18f;    // 低于此不算是障碍，直接走上去
    public const float MaxVaultRise = 140f;   // 高于此翻不上去（需要垫脚/绕行）
    public const float VaultReachX = 46f;     // 面前多近的可以攀住
    public const float VaultSeconds = 0.55f;  // 翻越补间时长

    /// <summary>高差是否可翻越。</summary>
    public static bool CanVault(float rise) => rise >= MinVaultRise && rise <= MaxVaultRise;

    /// <summary>
    /// 在实心矩形列表中寻找面前的可翻越物，返回 (攀缘顶面 y, 落点 x)。
    /// feet 为脚底世界坐标，facing 为朝向（±1）。不可翻越返回 null。
    /// </summary>
    public static (float topY, float landX)? FindVault(
        System.Numerics.Vector2 feet, float facing, IEnumerable<System.Numerics.Vector4> solids)
    {
        float bestTop = float.MaxValue;
        float bestLand = 0f;
        foreach (var s in solids)
        {
            // s = (x, y, w, h)，y 为顶面
            float x = s.X, top = s.Y, w = s.Z, h = s.W;
            float rise = feet.Y - top;
            if (!CanVault(rise)) continue;
            // 面向的侧边要在可攀范围内
            float edge = facing > 0 ? x : x + w;
            float gap = (edge - feet.X) * facing;
            if (gap < -2f || gap > VaultReachX) continue;
            // 落点：顶面往对面方向 30px，且落点必须在矩形宽度内（太窄的垛口翻过去站在对面边缘）
            float land = facing > 0 ? x + 26f : x + w - 26f;
            if (top < bestTop) { bestTop = top; bestLand = land; }
        }
        if (bestTop == float.MaxValue) return null;
        return (bestTop, bestLand);
    }

    // ---- 木箱（推/拉垫脚）----
    public const float CrateW = 56f;
    public const float CrateH = 70f;          // 箱顶比地面高 70：站上后再翻越 ≤140 的高台
    public const float CratePushSpeed = 62f;  // 推着走比步行慢得多
    public const float CrateGrabDist = 64f;   // 多远可以抓住
    public const float CrateKeepDist = 96f;   // 超出此距离自动松手

    /// <summary>玩家是否站在箱子可抓取范围内。</summary>
    public static bool CanGrabCrate(float playerX, float crateCenterX, float groundY, float crateGroundY)
        => System.Math.Abs(playerX - crateCenterX) <= CrateGrabDist && System.Math.Abs(groundY - crateGroundY) < 8f;

    // ---- 石块（投掷引开巡逻）----
    public const float StoneThrowDist = 320f;   // 投掷水平距离
    public const float StoneNoiseRadius = 560f; // 落点多大范围内的巡逻会被吸引
    public const float DistractSeconds = 6f;    // 巡逻离岗查看时长

    /// <summary>巡逻是否会被落点吸引（在听觉范围内）。</summary>
    public static bool HeardStone(float patrolX, float landX)
        => System.Math.Abs(patrolX - landX) <= StoneNoiseRadius;

    // ---- 门闩（拉开后打开侧巷补给点）----
    public const float BoltDist = 60f; // 多近可以拉门闩

    // ---- 阀门（蒸汽遮挡视野）----
    public const float ValveDist = 64f;      // 多近可以拧阀门
    public const float SteamSeconds = 18f;   // 蒸汽持续时长
    public const float SteamW = 130f;        // 蒸汽云宽度（遮挡带）
    public const float SteamH = 200f;        // 蒸汽云高度

    /// <summary>线段是否穿过蒸汽遮挡带（纯几何：简化为线段中点与两端逐段采样）。</summary>
    public static bool SteamBlocks(
        System.Numerics.Vector2 a, System.Numerics.Vector2 b,
        System.Numerics.Vector4 steamRect)
    {
        // 采样 8 段，任一样本落入矩形即遮挡（足够近似，无 Godot 依赖）
        for (int i = 0; i <= 8; i++)
        {
            float t = i / 8f;
            float x = a.X + (b.X - a.X) * t;
            float y = a.Y + (b.Y - a.Y) * t;
            if (x >= steamRect.X && x <= steamRect.X + steamRect.Z &&
                y >= steamRect.Y && y <= steamRect.Y + steamRect.W)
                return true;
        }
        return false;
    }
}
