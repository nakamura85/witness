using Godot;

namespace Witness;

/// <summary>
/// v3.71 全局时辰：关卡加载时设定，室内窗光 / 窗外晕染 / 房间光晕 / 地面光束都读它。
///
/// 用户诉求③：窗口照进来的光要符合外边时辰 —— 晚上月光、早上日光、黄昏另有氛围。
/// 采用「关卡全局时辰联动」：两关各自设一个 Phase，进屋后室内读取，不另搞昼夜系统。
///
/// 静态类、无 Autoload 节点；纯取色与参数，不持有任何节点。
/// </summary>
public static class WorldClock
{
    public enum Phase { Day, Dusk, Night }

    /// <summary>当前时辰。关卡加载时由 N918Level / JiangqiaoLevel 写入；默认白天。</summary>
    public static Phase Time = Phase.Day;

    public sealed class Palette
    {
        public Color WindowSky;     // 窗外天空
        public Color WindowGround;  // 窗外地面 / 远景
        public Color RoomWash;      // 房间整体光晕（低 alpha，叠在背景上）
        public Color Beam;          // 窗投到地面的光束
        public bool HasMoon;        // 是否画月亮 + 星点
        public string Label;
    }

    private static readonly Palette Day = new()
    {
        WindowSky = new Color(0.80f, 0.88f, 0.96f),
        WindowGround = new Color(0.86f, 0.87f, 0.88f),
        RoomWash = new Color(1.00f, 0.97f, 0.86f, 0.16f),
        Beam = new Color(1.00f, 0.95f, 0.78f, 0.20f),
        HasMoon = false,
        Label = "清晨日光",
    };

    private static readonly Palette Dusk = new()
    {
        WindowSky = new Color(0.96f, 0.56f, 0.32f),
        WindowGround = new Color(0.42f, 0.32f, 0.30f),
        RoomWash = new Color(0.98f, 0.68f, 0.38f, 0.18f),
        Beam = new Color(0.98f, 0.66f, 0.36f, 0.22f),
        HasMoon = false,
        Label = "黄昏余晖",
    };

    private static readonly Palette Night = new()
    {
        WindowSky = new Color(0.12f, 0.16f, 0.34f),
        WindowGround = new Color(0.09f, 0.11f, 0.20f),
        RoomWash = new Color(0.55f, 0.66f, 0.96f, 0.18f),
        Beam = new Color(0.74f, 0.82f, 0.98f, 0.16f),
        HasMoon = true,
        Label = "夜晚月光",
    };

    public static Palette Current() => For(Time);

    /// <summary>按指定时辰取调色板（每屋可携带自己的 WorldTime，不一定要读全局）。</summary>
    public static Palette For(Phase p)
    {
        return p switch
        {
            Phase.Dusk => Dusk,
            Phase.Night => Night,
            _ => Day,
        };
    }
}
