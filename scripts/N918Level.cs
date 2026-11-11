using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Witness;

/// <summary>
/// Soft radial falloff sprites, used wherever a hard-edged circle would leave a
/// visible rim on the flat snow.
///
/// v2.8: plain DrawCircle blobs were the reason bright white arcs kept
/// flashing across the snowfield. A radial gradient fading to zero alpha has
/// no rim, so the shape melts into the ground.
///
/// v2.8 fix: this did NOT use GradientTexture2D with Fill=Radial. That fill
/// interpolates along the axis from FillFrom to FillTo, and any pixel on the
/// far side of FillFrom clamps to offset 0 - which is fully opaque. The left
/// half of the sprite came out solid with a straight vertical cut down the
/// middle, which is exactly the hard edged pale slab that showed up on the
/// snow. Now the falloff is computed per pixel from the real distance to the
/// centre, so it fades out in every direction and has no edge at all.
/// </summary>
public static class N918SoftSnow
{
    private static readonly Dictionary<int, ImageTexture> _cache = new();

    public static ImageTexture Blob(Color tint, float alpha)
    {
        // v2.9: the cache key was tint.GetHashCode() ^ (int)(alpha*1000).
        //
        // That was broken in a nasty way. Godot's C# Color.GetHashCode()
        // casts R/G/B straight from float to uint, so 0.90f becomes 0 and
        // 0.55f also becomes 0 - every colour we ever ask for hashed to the
        // SAME number, and alpha is not part of the hash at all. The key
        // therefore carried nothing but the alpha, so any two callers using
        // the same alpha shared one texture no matter how different their
        // colours were.
        //
        // The visible symptom: the pale snow drift around a corpse asked for
        // a light blob and got back the near-black house shadow cached under
        // the same alpha, so a hard dark octagon sat on the snow next to
        // every body.
        //
        // The key is now built from the exact bytes, so no two requests can
        // ever share a slot.
        int key = ((int)(tint.R * 255f) << 24) ^ ((int)(tint.G * 255f) << 16)
            ^ ((int)(tint.B * 255f) << 8) ^ ((int)(tint.A * 255f))
            ^ ((int)(alpha * 1000f) * 7919);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        const int size = 64;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var half = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + 0.5f - half) / half;
            float dy = (y + 0.5f - half) / half;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            // smoothstep falloff: fully opaque in the middle, zero at the rim
            float a = 1f - Mathf.Clamp(r, 0f, 1f);
            a = a * a * (3f - 2f * a);
            img.SetPixel(x, y, new Color(tint.R, tint.G, tint.B, tint.A * alpha * a));
        }

        var tex = ImageTexture.CreateFromImage(img);
        _cache[key] = tex;
        return tex;
    }

    /// <summary>
    /// v3.16: a "melt band" - the snow that swallows the foot of a house or
    /// the bottom of a tree sprite.
    ///
    /// It used to be a GradientTexture2D stretched over a rectangle. That
    /// looked fine in the vertical direction but the rectangle itself had hard
    /// left and right edges, and a gradient texture also puts a hard step on
    /// its very first row. Over the distant village that left a bright slab
    /// with a ruler-straight edge lying on the snow under every far house -
    /// the "white box" the player kept pointing at.
    ///
    /// This one fades out in every direction: alpha ramps up from the top,
    /// and the left and right ends taper to nothing, so there is no edge
    /// anywhere for the eye to catch.
    /// </summary>
    public static ImageTexture MeltBand(Color tint, float alpha)
    {
        int key = ((int)(tint.R * 255f) << 24) ^ ((int)(tint.G * 255f) << 16)
            ^ ((int)(tint.B * 255f) << 8) ^ ((int)(tint.A * 255f))
            ^ ((int)(alpha * 1000f) * 7919) ^ 0x5E17;
        if (_cache.TryGetValue(key, out var cached)) return cached;

        const int w = 128, hgt = 64;
        var img = Image.CreateEmpty(w, hgt, false, Image.Format.Rgba8);
        for (int y = 0; y < hgt; y++)
        for (int x = 0; x < w; x++)
        {
            float u = (x + 0.5f) / w;
            float v = (y + 0.5f) / hgt;
            float up = v * v * (3f - 2f * v);
            float side = Mathf.Sin(Mathf.Pi * u);
            side = Mathf.Pow(side, 0.6f);
            float a = up * side;
            img.SetPixel(x, y, new Color(tint.R, tint.G, tint.B, tint.A * alpha * a));
        }

        var tex = ImageTexture.CreateFromImage(img);
        _cache[key] = tex;
        return tex;
    }
}

/// <summary>
/// 关卡 00「九一八事变 · 1931.9.18 · 沈阳近郊柳条湖 / 北大营 / 通信据点」垂直切片。
///
/// 季节还原历史初秋：1931.9.18 沈阳「晴朗无风初秋凉夜」，无飘雪、无积雪地、
/// 无深冬色温；改用初秋暖色（枯草黄、暖色天光、清朗夜+星、微风），仍 100% InkStyle
/// manga 管线。
///
/// 玩法结构（单场景多区段）：
///  1) 柳条湖铁轨爆破点 —— 见证爆炸、记录巡道工口述；
///  2) 北大营围墙 —— 不抵抗令的悲剧抉择（遵令 / 违令）；
///  3) 民居群 —— 多封可收集信、难民口述；
///  4) 通信据点 —— 先合闸供电，再操作无线电发信机把实情传出去通关；
///  2) 人物纸娃娃分层动画，受重力与碰撞约束；
///  3) 关东军尖兵会反击，身中两枪阵亡并留下尸体（含东北军官兵尸体）。
/// </summary>
public partial class N918Level : Node2D
{
    private readonly Main _main;

    public const float WorldW = 5200f;
    // walk line sits INSIDE the open ground, not on its top edge:
    // boots, corpses and walls all sink into the autumn field (v2.5)
    public const float GroundY = 655f;

    /// <summary>圆离散成多边形顶点（默认全圆，a0/a1 可画弧段）。</summary>
    internal static Vector2[] CircleVerts(float r, int n, float a0 = 0f, float a1 = Mathf.Tau)
    {
        // 用 i/n 而非 i/(n-1)：满圆时最后一个顶点不再与第一个重合，
        // 避免出现零长边导致 Godot 三角化偶发失败（白线闪烁）。
        if (n < 3) n = 3;
        var v = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float a = Mathf.Lerp(a0, a1, t);
            v[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }
        return v;
    }

    internal FieldPlayer _player;
    private GroundShadow _playerShadow;
    internal Camera2D _cam;
    private Node2D _game;
    private Hud _hud;
    private N918VitalsBar _vitals;
    private N918AmmoBar _ammoBar;
    private ColorRect _flash;
    private ColorRect _blackout;
    private Backdrop[] _layers;
    private SkyScreen _sky;
    private CanvasModulate _tint;
    private CoverArt _coverArt;
    internal readonly List<N918EnemySoldier> _enemies = new();
    private readonly List<N918Testimony> _testimonies = new();
    internal readonly List<WeaponPickup> _pickups = new();
    internal readonly List<EnterableHouse> _houses = new();
    private HouseInteriorUi _houseUi;
    private readonly List<N918GrenadeProj> _grenades = new();
    // v3.53: sandbag cover interaction state
    private int _coverIdx = -1;
    private Tween _coverTween;
    private float _coverCd;            // blocks enter/exit re-triggering on one press
    private float _enemyNadeTimer = -1f;
    private TracerFx _fx;
    private SnowScreen _snow;
    private SmokeFx _smokeWorld;    // smoke columns, in front of every house
    private SmokeFx _smokeMid;      // chimney smoke over the distant village (parallax mid)
    private SmokeFx _fireFarIn;     // fire burning INSIDE the far row houses (behind them)
    private SmokeFx _fireFarOut;    // fire burning outside the far row houses (in front of them)
    private SmokeFx _fireNearIn;    // fire burning INSIDE the near row houses (behind them)
    private SmokeFx _fireNearOut;   // fire burning outside the near row houses (in front of them)
    private const float MidFactor = 0.24f;

    /// <summary>
    /// A person is 114px tall. The soldier art is 108px and both the player
    /// and the enemy walk around at scale 1.06, so 108 x 1.06 = 114.5.
    /// Everything in the level that has to sit next to a human - a house
    /// wall, a bridge - is measured against this number.
    ///
    /// v3.39: this 114.5px is the protagonist's height, anchored to 170cm
    /// (a normal East-Asian male; the 160-180cm band, with rare outliers
    /// like Yao Ming ~200cm+). So 1px ~= 1.484cm. Houses stand 1.5x this
    /// (WallH = 255cm) and the bridge 2.5x (BridgeH). Soldier/enemy share
    /// the same PersonH reference, so the protagonist reads as a normal
    /// built man next to them.
    /// </summary>
    public const float PersonH = 114.5f;

    /// <summary>
    /// v3.25: the house rule the user set again - a house that is not burnt
    /// down stands one and a half man-heights at the eave. Burnt shells and
    /// charred stumps keep the size they have: a ruin with no wall has no
    /// wall to measure, and the village only reads as a village while the
    /// standing houses are the big ones.
    /// </summary>
    public const float WallH = PersonH * 1.5f;

    /// <summary>
    /// v3.25: the bridge is two and a half man-heights tall. Measured from
    /// the snow line it stands on up to the top of the truss - that is the
    /// height the eye reads, and it is what puts the span over the person's
    /// head instead of at his shoulder.
    /// </summary>
    public const float BridgeH = PersonH * 2.5f;

    // viewport 1280x720 -> half width 640 / half height 360.
    // used when the camera clamps itself: the lens must not cross the left
    // and right edges of the world, and must not drift off the snow vertically.
    private const float ViewHalfW = 640f;
    private const float ViewHalfH = 360f;

    private float _shake;
    private float _flashA;
    private float _blackA;
    private float _blackTarget;
    private float _record;
    private float _lastToast = -99f;
    private float _elapsed;
    private float _camX = 220f;   // 手动平滑后的相机 X（与视差层同一帧更新）
    private bool _started;
    private bool _ended;
    private bool _noteOpen;
    private int _wave;
    private Vector2 _checkpoint = new(220, GroundY - 2);

    // —— 昼夜：1931-09-18 沈阳近郊（41.8N 123.4E，UTC+8）实算天文数据 ——
    // 初秋凉夜：日落约 18:02，23 点前后月上（接近满月前后的清朗夜），无风。
    // 开场即夜晚，玩家在星光与暖色月光下走过柳条湖——与江桥的「天没亮」相反。
    public const float SunriseH = 5.617f;     // 约 05:37
    public const float SunsetH = 18.033f;     // 约 18:02
    public const float MoonLit = 0.71f;       // 初秋夜里偏亮的月（接近满月前后）
    private float _clock = 22.0f;             // 游戏钟（小时），开场 22:00 晴朗秋夜
    public const float ClockRate = 82f;       // 1 真秒 = 82 游戏秒；九一八关全程在初秋凉夜，钟基本停在 22:00 前后
    private const float ClockMax = 24.6f;     // 上限放宽到午夜后，避免把 22:00 的夜景 clamp 回黄昏（黄昏无星）
    /// <summary>实机截图用：强制指定钟点（&lt;0 = 正常走时）。</summary>
    public float DebugClock = -1f;

    /// <summary>v2.8: verification switch, hides the falling snow so the shake
    /// check can measure the backdrop on its own.</summary>
    public bool DebugNoSnow;

    /// <summary>v2.8: verification switch, hides the chimney smoke so the
    /// shake check can measure the backdrop on its own.</summary>
    public bool DebugNoSmoke;
    private float _lastDryToast = -99f;

    public N918Level(Main main) { _main = main; }

    public float PlayerX => _player?.GlobalPosition.X ?? 0f;
    public bool PlayerAlive => _player != null && _player.Hp > 0;
    /// <summary>相机中心的横坐标（敌人据此判断自己是否真的在玩家画面里）。</summary>
    public float CamX => _cam?.GlobalPosition.X ?? 0f;

    public override void _Ready()
    {
        BuildSky();
        BuildBackdrop();
        BuildMidSmoke();
        BuildTerrain();
        BuildHouses();
        BuildPlayer();
        BuildPickups();
        BuildTestimonies();
        BuildHud();
        BuildScreenFx();

        // world tint: night bluish-dark, dawn warm, noon white, dusk amber
        _tint = new CanvasModulate { Color = Colors.White };
        AddChild(_tint);

        var intro = new IntroOverlay();
        AddChild(intro);
        intro.Show(
            "1931 年 9 月 18 日 · 中国（中华民国时代）沈阳近郊 · 柳条湖",
            "初秋的夜很凉，没有风。天上有星，月亮也亮。\n南满铁路柳条湖段的铁轨边，今夜会被炸开一个口子。\n你是东北军的一名通信兵——把这条铁路沿线还活着的人说过的话，带出去。",
            "",
            () =>
            {
                _started = true;
                _hud.ShowTutorial("A / D 移动 · 空格 跳跃 · S 蹲下 · Ctrl 匍匐 · E 记录/进屋/捡枪 · G 手榴弹 · 鼠标左键射击");
                // v3.48（沿用）：开场给场景一息（~1s），再落下远处炮声，避免「按回车闪一下抖一下」。
                GetTree().CreateTimer(1.0).Timeout += () =>
                {
                    if (IsInsideTree() && !_ended)
                        Artillery(0.4f);
                };
            });
    }

    // ================= 构图 =================

    private void BuildSky()
    {
        _sky = new SkyScreen();
        AddChild(_sky);
    }

    /// <summary>
    /// 九一八单场景四区段布局（沿 X 自西向东）：
    ///   柳条湖爆破点  ~600-1200  →  h1 巡道工小屋（可搜，含信）
    ///   北大营围墙    ~1700-2600  →  h2 营房外民居（可搜，含信）；此地触发不抵抗令抉择
    ///   民居群        ~2900-3900  →  h3 完整民居 / h4 屋顶坍塌院落（可搜，含信）
    ///   通信据点      ~4000-4400  →  comm 无线电发信机据点（可搜 + 供电 + 发报通关；置于桥台之前、落在地面，不压桥面/铁轨结构）
    /// 远景废墟列（f1~f4）为纯背景，不可进入。
    ///
    /// comm 直接用专属军事据点贴图 radio_station（本就无雪、带天线与墙脚沙袋，落地面）。
    /// HouseEaveRow / HouseWidthForWall / 烟雾开关均已登记 radio_station，比例与落地由定位逻辑统一处理。
    /// </summary>
    private void BuildHouses()
    {
        // 远景废墟列（不可进入背景）——初秋变体
        AddHouse("f1", 460f + Jit(28), "house_posts_autumn", 250f, false, 14f);
        AddHouse("f2", 1294f + Jit(42), "house_skeleton_near_autumn", 350f, false, 16f);
        AddHouse("f4", 1680f + Jit(45), "house_posts_autumn", 210f, false, 19f);
        AddHouse("f3", 3910f + Jit(35), "house_burned_near_autumn", 331f, false, 12f);

        // 近景可进入：柳条湖巡道工小屋
        AddHouse("h1", 845f + Jit(24), "house_ruined_near_autumn", HouseWidthForWall("house_ruined_near_autumn", WallH), true, 0f);
        // 北大营外民居（固定位，给不抵抗令抉择留出空间）
        AddHouse("h2", 2080f, "house_intact_near_autumn", HouseWidthForWall("house_intact_near_autumn", WallH), true, 0f);
        // 民居群：完整民居 + 屋顶坍塌院落
        AddHouse("h3", 3120f, "house_ruined_near_autumn", HouseWidthForWall("house_ruined_near_autumn", WallH), true, 0f);
        AddHouse("h4", 3700f + Jit(40), "house_posts_autumn", HouseWidthForWall("house_posts_autumn", WallH), false, 0f);
        // 通信据点（无线电发信机）：专属军事据点建筑 radio_station（本就无雪，沿用）
        // v_next: 原先落在 x4620（正好压在江桥桥面 4457-4902 上）→ 移到 4000，落在桥台之前的地面、不压桥面/铁轨结构
        AddHouse("comm", 4000f, "radio_station", HouseWidthForWall("radio_station", WallH), true, 0f);
    }

    /// <summary>
    /// Which row of the house texture the eave sits on - the line where the
    /// roof closes and the wall starts. Measured off the paintings with a
    /// row scan (the roof silhouette keeps widening until that row, then
    /// snaps back to the wall width).
    ///
    /// intact 1531x995 -> eave row 495, ruined 1467x918 -> eave row 460.
    /// </summary>
    private static readonly Dictionary<string, float> HouseEaveRow = new()
    {
        ["house_intact_near"] = 495f,
        ["house_ruined_near"] = 460f,
        // v3.39: house_posts is a roofless ruined frame, so the whole
        // standing structure (top of the art down to the snow) is the
        // "wall" the eave row locks to 1.5 man-heights.
        ["house_posts"] = 12f,
        // 初秋变体：与冬雪原图同尺寸、同轮廓，eave 行保持一致
        ["house_intact_near_autumn"] = 495f,
        ["house_ruined_near_autumn"] = 460f,
        ["house_posts_autumn"] = 12f,
        // 通信据点 radio_station.png (1024x1285)：天线尖在 ~22%，屋顶在 ~45-62%，
        // 墙体恒定宽段从 ~60%(y≈771) 起、沙袋基底到 98%；eave 取屋顶转墙体的位置
        ["radio_station"] = 770f,
    };

    /// <summary>
    /// How wide the house has to be drawn so its wall stands <paramref name="wallPx"/>
    /// tall between the eave and the snow line.
    ///
    /// The house node sits 8px sunk into the snow and the feet line is 2px
    /// above the snow, so the wall measures (h * (1 - eave/texH)) - 10. The
    /// texture is drawn straight, so w = h * texW / texH.
    /// </summary>
    private static float HouseWidthForWall(string tex, float wallPx)
    {
        if (!HouseEaveRow.TryGetValue(tex, out float eave)) return 400f;
        var t = GD.Load<Texture2D>($"res://assets/art/props/{tex}.png");
        if (t == null) return 400f;
        var sz = t.GetSize();
        float h = (wallPx + 10f) / (1f - eave / sz.Y);
        return h * sz.X / sz.Y;
    }

    /// <summary>
    /// Smoke + fire for every house. A house that still stands puffs thin
    /// domestic smoke from the chimney, a burnt one chokes out a thick black
    /// column and still has embers crawling in the ashes.
    ///
    /// v3.13: every house used to get the SAME size for its texture type, so
    /// all the fires in the village looked cloned - the user pointed at two
    /// screenshots of the same kind of house and said the flames and the smoke
    /// must not be one size. Now each house rolls its own: the damage state
    /// decides the range, the building width scales it up, and a random pick
    /// inside the range settles the final look. A house that is nearly burnt
    /// out rolls a tiny flame and only a thread of smoke left.
    ///
    /// v3.25: the fire is split in two, because a flame has to sit either in
    /// front of a wall or behind it - never in the same layer as the house.
    /// The user spelled it out: fire burning inside the house has to be
    /// drawn UNDER the house layer, fire burning outside has to be drawn
    /// OVER it. Before this every flame shared the single smoke layer, which
    /// sat in front of the houses, so a fire that was supposed to be inside
    /// a burnt shell was pasted on top of the wall.
    ///
    /// So the fire goes into its own layers:
    ///   - the fire anchored on the house itself is drawn BEHIND the house,
    ///     so the wall and the charred beams cross in front of the flame
    ///     and the house reads as burning from the inside;
    ///   - the fire on the ground in front of the house is drawn IN FRONT of
    ///     the house, so it licks up over the wall.
    /// The smoke column is neither - it climbs into the air, so it stays in
    /// front of everything and drifts over the roof.
    /// </summary>
    private void AddHouseSmoke(EnterableHouse h, string tex, float x, float w, float lift)
    {
        if (_smokeWorld == null) return;
        var tex2 = GD.Load<Texture2D>($"res://assets/art/props/{tex}.png");
        if (tex2 == null) return;
        var sz = tex2.GetSize();
        float hgt = w * sz.Y / sz.X;

        // source point in texture space (normalised), then mapped to world
        float u = 0.5f, v = 0.4f;
        float sizeLo = 0.8f, sizeHi = 1.0f;
        float strLo = 0.5f, strHi = 0.7f;
        float fireLo = 0f, fireHi = 0f;
        float riseLo = 70f, riseHi = 90f;
        // where the fire burns inside the house, in texture fractions. The
        // points are the real openings cut into the paintings, so the wall
        // frame crosses in front of the flame instead of hiding it.
        Vector2[] inner = System.Array.Empty<Vector2>();
        var tint = new Color(0.72f, 0.73f, 0.75f);
        switch (tex)
        {
            // 通信据点是功能性建筑（通电+发报），不是民居，不在正中冒炊烟
            case "radio_station":
                return;

            case "house_intact_near": case "house_intact_near_autumn":
                // the painted smoke that used to sit over this chimney is gone
                // now, so the anchor goes right onto the chimney mouth itself
                u = 0.700f; v = 0.147f;
                sizeLo = 0.52f; sizeHi = 0.92f;
                strLo = 0.42f; strHi = 0.78f;
                fireLo = 0f; fireHi = 0f;
                riseLo = 52f; riseHi = 96f;
                tint = new Color(0.82f, 0.85f, 0.89f);
                break;
            case "house_burned_near": case "house_burned_near_autumn":
                // still going strong: big flame, fat black column
                u = 0.545f; v = 0.615f;
                sizeLo = 1.05f; sizeHi = 1.85f;
                strLo = 0.72f; strHi = 1.00f;
                fireLo = 0.42f; fireHi = 0.95f;
                riseLo = 108f; riseHi = 178f;
                tint = new Color(0.33f, 0.32f, 0.34f);
                // the two big burnt-out window holes on the left bay
                inner = new[] { new Vector2(0.251f, 0.585f), new Vector2(0.315f, 0.580f) };
                break;
            case "house_skeleton_near": case "house_skeleton_near_autumn":
                // frame still standing, burning hard
                u = 0.45f; v = 0.42f;
                sizeLo = 1.08f; sizeHi = 1.95f;
                strLo = 0.66f; strHi = 1.00f;
                fireLo = 0.28f; fireHi = 0.80f;
                riseLo = 118f; riseHi = 190f;
                tint = new Color(0.38f, 0.38f, 0.41f);
                // the two big gaps between the standing posts
                inner = new[] { new Vector2(0.157f, 0.585f), new Vector2(0.251f, 0.575f) };
                break;
            case "house_ruined_near": case "house_ruined_near_autumn":
                // mostly done, only embers left in the shell
                u = 0.45f; v = 0.26f;
                sizeLo = 0.58f; sizeHi = 1.08f;
                strLo = 0.30f; strHi = 0.60f;
                fireLo = 0.06f; fireHi = 0.30f;
                riseLo = 62f; riseHi = 108f;
                tint = new Color(0.58f, 0.59f, 0.62f);
                // the wall of this one has no opening cut into it at all, so
                // there is nothing for an inside fire to show through - it
                // burns on the snow in front of the ruin instead
                break;
            case "house_posts_autumn":
            default:  // house_posts: nothing left but charred stumps
                // nearly burnt out -> tiny flame, only a thin wisp of smoke
                u = 0.50f; v = 0.50f;
                sizeLo = 0.26f; sizeHi = 0.54f;
                strLo = 0.14f; strHi = 0.32f;
                fireLo = 0.05f; fireHi = 0.20f;
                riseLo = 32f; riseHi = 62f;
                tint = new Color(0.46f, 0.46f, 0.49f);
                break;
        }

        float size = Mathf.Lerp(sizeLo, sizeHi, GD.Randf());
        float strength = Mathf.Lerp(strLo, strHi, GD.Randf());
        float fire = Mathf.Lerp(fireLo, fireHi, GD.Randf());
        float rise = Mathf.Lerp(riseLo, riseHi, GD.Randf());

        // a bigger building burns bigger: 250px -> 0.72x, 360px -> 1.15x
        float wScale = 0.72f + Mathf.Clamp((w - 250f) / 110f, 0f, 1f) * 0.43f;
        size *= wScale;
        fire *= wScale;

        float px = x + u * w;
        float py = GroundY - hgt + v * hgt;
        // far row sits deeper in the haze: smaller, dimmer
        float dim = lift > 0f ? 0.6f : 1f;

        // the column is pure smoke now - the fire lives in its own layers
        _smokeWorld.Add(new SmokeFx.Source
        {
            Pos = new Vector2(px, py),
            Size = size * (lift > 0f ? 0.7f : 1f),
            Strength = strength * dim,
            Fire = 0f,
            Rise = rise * (lift > 0f ? 0.7f : 1f),
            Drift = 0.5f + Jit(0.25f),
            Phase = GD.Randf(),
            Tint = tint,
        });

        // a big fire gets a second, thinner column beside the main one, so
        // the plume is not one symmetric sausage
        if (size > 1.15f)
        {
            float side = Jit(26f) + (GD.Randf() < 0.5f ? -1f : 1f) * 26f;
            _smokeWorld.Add(new SmokeFx.Source
            {
                Pos = new Vector2(px + side, py - GD.Randf() * 14f),
                Size = size * 0.52f,
                Strength = strength * 0.55f * dim,
                Fire = 0f,
                Rise = rise * 0.72f * (lift > 0f ? 0.7f : 1f),
                Drift = 0.5f + Jit(0.3f),
                Phase = GD.Randf(),
                Tint = tint,
            });
        }

        // v3.13: the flame gets its own size, not the smoke column's.
        // They used to share one number, so a house that rolled a thin
        // wisp of smoke also got a flame too small to see.
        float fireSize = 0.45f + fire * 1.35f;

        // the fire inside the house: drawn BEHIND the house layer, so the
        // wall and the charred beams cross in front of the flame
        if (fire > 0.01f && inner.Length > 0)
        {
            var inLayer = lift > 0f ? _fireFarIn : _fireNearIn;
            if (inLayer != null)
            {
                foreach (var ip in inner)
                {
                    inLayer.Add(new SmokeFx.Source
                    {
                        Pos = new Vector2(x + ip.X * w, GroundY - hgt + ip.Y * hgt),
                        Size = fireSize * 0.85f,
                        Strength = 0f,
                        Fire = fire * dim,
                        Rise = 0f,
                        Drift = 0f,
                        Phase = GD.Randf(),
                        Tint = tint,
                    });
                }
            }
        }

        // the fire itself sits on the ground under the smoke column
        if (fire > 0.01f)
        {
            // drawn IN FRONT of the house layer - this flame is on the snow
            // outside the building, so it has to lick up over the wall
            var outLayer = lift > 0f ? _fireFarOut : _fireNearOut;
            if (outLayer != null)
                outLayer.Add(new SmokeFx.Source
                {
                    Pos = new Vector2(px + Jit(10f), GroundY - 3f),
                    Size = fireSize,
                    Strength = 0f,
                    Fire = fire * dim,
                    Rise = 0f,
                    Drift = 0f,
                    Phase = GD.Randf(),
                    Tint = tint,
                });
        }
    }

    private void AddHouse(string id, float x, string tex, float w, bool enterable, float lift)
    {
        // 屋基下沉 8px 进雪面：雪面顶边是起伏波浪线，平的房屋底边会露缝显得"飘在雪上"（#6）
        var h = new EnterableHouse(id, new Vector2(x, GroundY + 8f), tex, w, enterable, lift);
        _game.AddChild(h);
        _houseRects.Add((x, w));
        if (enterable) _houses.Add(h);
        AddHouseSmoke(h, tex, x, w, lift);
    }

    // every house footprint (near AND far rows) - ground clutter such as
    // barbed-wire stakes must not ride through a wall (v3.46).
    // static: read from the nested near-layer class (same pattern as
    // InsideSandbag); the level is a singleton so this stays consistent.
    private static readonly List<(float X, float W)> _houseRects = new();

    /// <summary>
    /// True when x falls under a house footprint (with a small shoulder),
    /// so ground clutter is kept out of the walls. v3.46: three of the four
    /// barbed-wire runs used to cross straight through a house base.
    /// </summary>
    private static bool InsideHouseFootprint(float x, float margin = 12f)
    {
        foreach (var (hx, hw) in _houseRects)
            if (x > hx - margin && x < hx + hw + margin) return true;
        return false;
    }

    /// <summary>
    /// Where the distant village stands.
    ///
    /// v2.8: this used to be 578, which is exactly where the snow plain starts,
    /// so every house sat right on the horizon line and the ridge behind ran
    /// straight through the roof line. The whole row then read as one stripe
    /// painted onto the mountain. Dropping it further down the plain puts the
    /// houses on open ground with the ridge clearly behind them.
    /// </summary>
    public const float VillageBaseY = 608f;

    /// <summary>
    /// One house in the distant village.
    ///
    /// v2.8: this used to be a crop cut out of village_ruined.png, but that
    /// sheet is painted from a top-down three-quarter angle while the level is
    /// shot from the side. Mixing the two angles is what made the village look
    /// like stickers glued onto the mountain. Now every house uses the same
    /// side-on art as the near houses, so the perspective matches.
    /// </summary>
    public readonly struct VillageHouse
    {
        /// <summary>Texture file name (no extension), under art/props or art/backdrop.</summary>
        public readonly string Tex;
        /// <summary>World X (house bottom edge, centred).</summary>
        public readonly float X;
        /// <summary>World height (pixels).</summary>
        public readonly float H;
        /// <summary>Mirror left/right.</summary>
        public readonly bool Flip;
        /// <summary>What kind of smoke this one gives off (0 = none, 1 = domestic, 2 = black).</summary>
        public readonly int Smoke;
        /// <summary>Where the chimney sits inside the texture (0~1).</summary>
        public readonly Vector2 SmokeAt;
        /// <summary>True when the texture lives under art/backdrop instead of art/props.</summary>
        public readonly bool Backdrop;

        public VillageHouse(string tex, float x, float h, bool flip, int smoke, Vector2 smokeAt,
                            bool backdrop = false)
        {
            Tex = tex;
            X = x;
            H = h;
            Flip = flip;
            Smoke = smoke;
            SmokeAt = smokeAt;
            Backdrop = backdrop;
        }
    }

    /// <summary>
    /// Where each distant house stands.
    ///
    /// Heights vary on purpose (38~118px). The parallax layer runs at 0.24 so
    /// the horizontal spread gets squashed, and without a real size range every
    /// house ends up the same size and the row looks printed rather than built.
    /// </summary>
    public static readonly VillageHouse[] VillageHouses =
    {
        // —— 九一八 · 沈阳北大营：驻军营盘。前排成排营房 + 一座木瞭望塔（见 DrawMidLandmark）。
        // 夜袭轰炸，所以黑烟（Smoke=2）明显比江桥那关多，整体读起来是“被炮火覆盖的军营”。
        // 这套排布刻意和 JiangqiaoLevel 的 VillageHouses 错开，让两关远景不再一模一样。
        new("village_autumn", -700f, 112f, false, 2, new Vector2(0.482f, 0.152f), true),   // 被轰的长营房
        new("house_intact_near_autumn", -430f, 82f, true, 1, new Vector2(0.700f, 0.147f)),  // 卫兵房，还在冒烟
        new("house_skeleton_near_autumn", -250f, 64f, false, 2, new Vector2(0.45f, 0.42f)),
        new("house_posts_autumn", -90f, 40f, true, 0, new Vector2(0.50f, 0.50f)),
        new("house_ruined_near_autumn", 80f, 72f, false, 2, new Vector2(0.45f, 0.26f)),

        // —— 营门：立着的主房 + 一座在烧的营房
        new("house_intact_near_autumn", 300f, 94f, false, 1, new Vector2(0.700f, 0.147f)),
        new("village_autumn", 560f, 122f, true, 2, new Vector2(0.482f, 0.152f), true),     // 营房中弹起火
        new("house_skeleton_near_autumn", 760f, 58f, false, 2, new Vector2(0.45f, 0.42f)),
        new("house_posts_autumn", 880f, 36f, true, 0, new Vector2(0.50f, 0.50f)),

        // —— 中后段：稀疏，但多是被打穿的残骸（轰炸带），不是完整村落
        new("house_ruined_near_autumn", 2440f, 68f, true, 2, new Vector2(0.45f, 0.26f)),
        new("house_intact_near_autumn", 2920f, 56f, false, 1, new Vector2(0.700f, 0.147f)),
        new("house_burned_near_autumn", 3060f, 78f, true, 2, new Vector2(0.545f, 0.615f)),
        new("house_posts_autumn", 3190f, 34f, false, 0, new Vector2(0.50f, 0.50f)),
        new("house_skeleton_near_autumn", 4180f, 70f, true, 2, new Vector2(0.45f, 0.42f)),
        new("village_autumn", 4620f, 92f, false, 2, new Vector2(0.482f, 0.152f), true),
    };

    /// <summary>Cached house textures for the distant village (loaded once, then kept).</summary>
    private static readonly Dictionary<string, Texture2D> _vhouseTex = new();

    /// <summary>
    /// Loads the art for one distant house.
    ///
    /// v2.8: this used to slice the big village_ruined panorama into single
    /// houses at runtime. That panorama is painted from a top-down angle, so
    /// the slices never matched the side-on level and the village read as
    /// stickers pasted on the mountain. Now it is just a plain file load of the
    /// same side-on art the near houses use.
    /// </summary>
    private static Texture2D VillageHouseTex(string name, bool backdrop)
    {
        if (_vhouseTex.TryGetValue(name, out var cached)) return cached;

        var path = backdrop
            ? $"res://assets/art/backdrop/{name}.png"
            : $"res://assets/art/props/{name}.png";
        var tex = GD.Load<Texture2D>(path);
        _vhouseTex[name] = tex;
        return tex;
    }


    /// <summary>
    /// Chimney smoke over the distant village: black smoke from the collapsed
    /// roofs, white smoke from the chimney of the intact house.
    /// These used to be painted into the texture as a static cloud, now they
    /// are procedural and animated.
    /// v2.7: each house carries its own Smoke / SmokeAt in the layout table.
    /// Before this the position was computed from the proportions of the whole
    /// panorama, so once the houses were split apart the smoke drifted into
    /// the neighbour's house.
    /// </summary>
    private void BuildMidSmoke()
    {
        if (_smokeMid == null) return;

        // v3.13: the distant row used the same size for every house of the same
        // kind, so the whole village smoked in lockstep. Each one rolls its own
        // now - a fixed seed keeps the backdrop stable between reloads, but no
        // two chimneys share a size any more.
        var rng = new RandomNumberGenerator();
        rng.Seed = 19311104;

        for (int i = 0; i < VillageHouses.Length; i++)
        {
            var h = VillageHouses[i];
            if (h.Smoke == 0) continue;

            var ht = VillageHouseTex(h.Tex, h.Backdrop);
            if (ht == null) continue;
            float wid = ht.GetSize().X / ht.GetSize().Y * h.H;
            float ax = h.X - wid * 0.5f + h.SmokeAt.X * wid;
            float ay = VillageBaseY - h.H + h.SmokeAt.Y * h.H;

            if (h.Smoke == 2)
            {
                // burnt roof: thick black column
                _smokeMid.Add(new SmokeFx.Source
                {
                    Pos = new Vector2(ax, ay),
                    Size = Mathf.Lerp(0.40f, 0.82f, rng.Randf()),
                    Strength = Mathf.Lerp(0.54f, 0.86f, rng.Randf()),
                    Fire = 0f,
                    Rise = Mathf.Lerp(32f, 64f, rng.Randf()),
                    Drift = 0.4f + rng.Randf() * 0.4f,
                    Phase = i * 0.37f + rng.Randf() * 0.2f,
                    Tint = new Color(0.40f, 0.40f, 0.44f),
                });
            }
            else
            {
                // chimney of the house that is still standing
                _smokeMid.Add(new SmokeFx.Source
                {
                    Pos = new Vector2(ax, ay),
                    Size = Mathf.Lerp(0.22f, 0.48f, rng.Randf()),
                    Strength = Mathf.Lerp(0.28f, 0.58f, rng.Randf()),
                    Fire = 0f,
                    Rise = Mathf.Lerp(20f, 44f, rng.Randf()),
                    Drift = 0.3f + rng.Randf() * 0.3f,
                    Phase = i * 0.71f + 0.4f,
                    Tint = new Color(0.84f, 0.86f, 0.90f),
                });
            }
        }
    }

    /// <summary>雪地里捡得到的枪：有的是死者掉的还有子弹，有的已经冻坏打不响。</summary>
    private void BuildPickups()
    {
        // 预置尸体旁的枪：一部分变成可拾取实体，一部分仍画在尸体上（不是每支都捡得到）。
        // 落点带随机滑动，枪不会整整齐齐排一排。
        AddPickup(1270 + 78 + Jit(18), GroundY - 3 - Jit(3), RollCondition());
        AddPickup(2090 - 64 + Jit(18), GroundY - 3 - Jit(3), RollCondition());
        AddPickup(620 + Jit(22), GroundY - 3 - Jit(3), WeaponPickup.Kind.Broken, 0);
        AddPickup(3690 + 40 + Jit(18), GroundY - 3 - Jit(3), RollCondition());
        AddPickup(2470 + 90 + Jit(18), GroundY - 3 - Jit(3), RollCondition());
    }

    private static float Jit(float amp) => (float)GD.RandRange(-amp, amp);

    private (WeaponPickup.Kind, int) RollCondition()
    {
        // 四成冻坏/打不响，六成还有几发——真实战场上捡枪就是这样赌运气
        if (GD.Randf() < 0.4f) return (WeaponPickup.Kind.Broken, 0);
        // loose rounds: usually a handful, finding 8+ is a lucky day
        int n = GD.Randf() < 0.12f ? (int)GD.RandRange(6, 8) : (int)GD.RandRange(1, 5);
        return (WeaponPickup.Kind.Usable, n);
    }

    private void AddPickup(float x, float y, WeaponPickup.Kind kind, int ammo)
    {
        var p = new WeaponPickup(kind, ammo) { Position = new Vector2(x, y) };
        _game.AddChild(p);
        _pickups.Add(p);
    }

    private void AddPickup(float x, float y, (WeaponPickup.Kind, int) roll)
        => AddPickup(x, y, roll.Item1, roll.Item2);

    private void BuildBackdrop()
    {
        _layers = new[]
        {
            new Backdrop(this, Backdrop.KindFar, 0.06f, -40),
            new Backdrop(this, Backdrop.KindMid, MidFactor, -30),            new Backdrop(this, Backdrop.KindNear, 0.55f, -20),
            // v3.48: the ground band (near trees / willow clumps / wire fence)
            // moved OUT of the 0.55 parallax layer into the world plane. It
            // used to share the ground line with the houses (663) and the
            // railway (654) while scrolling at 0.55, so every step slid the
            // trunks sideways across doors and walls - the user kept catching
            // a tree glued to a doorway in screenshots. Factor 1.0 pins the
            // band to the world: a tree beside a door STAYS beside that door.
            new Backdrop(this, Backdrop.KindGround, 1f, -5),
            new Backdrop(this, Backdrop.KindFore, 1.32f, 20),
        };
        foreach (var l in _layers) AddChild(l);

        // distant village smoke rides the same parallax as the mid backdrop
        _smokeMid = new SmokeFx { ZIndex = -29 };
        AddChild(_smokeMid);
        // v2.8: verification switch, lets the shake check cut the smoke
        if (DebugNoSmoke) _smokeMid.Visible = false;
    }

    private void BuildTerrain()
    {
        _game = new Node2D { ZIndex = 0 };
        AddChild(_game);

        // v3.25: the stack got two more rungs, because a flame has to sit
        // either behind a house or in front of it and the old single smoke
        // layer could not do both at once.
        //
        //   ground art   -10   snow, rails, craters, the bridge
        //   footprints    -8   prints on the snow
        //   fire in far   -7   burning INSIDE the far row houses
        //   far houses    -6   the scenery ruins, further away
        //   fire out far  -5   burning outside the far row houses
        //   fire in near  -4   burning INSIDE the near row houses
        //   near houses   -3   the ones the player can walk into
        //   fire out near -2   burning outside the near row houses
        //   smoke         -1   columns, they climb over the roofs
        //   people         0   player, enemies, corpses
        //
        // Smoke still reads as a backdrop the characters walk in front of.
        _fireFarIn = new SmokeFx { ZIndex = -7 };
        _game.AddChild(_fireFarIn);
        _fireFarOut = new SmokeFx { ZIndex = -5 };
        _game.AddChild(_fireFarOut);
        _fireNearIn = new SmokeFx { ZIndex = -4 };
        _game.AddChild(_fireNearIn);
        _fireNearOut = new SmokeFx { ZIndex = -2 };
        _game.AddChild(_fireNearOut);
        _smokeWorld = new SmokeFx { ZIndex = -1 };
        _game.AddChild(_smokeWorld);
        if (DebugNoSmoke)
        {
            _fireFarIn.Visible = false;
            _fireFarOut.Visible = false;
            _fireNearIn.Visible = false;
            _fireNearOut.Visible = false;
            _smokeWorld.Visible = false;
        }

        var floor = new StaticBody2D { Position = new Vector2(WorldW * 0.5f, GroundY + 120f) };
        var fs = new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(WorldW + 800f, 240f) } };
        floor.AddChild(fs);
        AddChild(floor);

        // 左右无形的边界墙，防止走出世界
        foreach (float x in new[] { -40f, WorldW + 40f })
        {
            var wall = new StaticBody2D { Position = new Vector2(x, GroundY - 200f) };
            wall.AddChild(new CollisionShape2D
            {
                Shape = new RectangleShape2D { Size = new Vector2(80, 420) },
            });
            AddChild(wall);
        }

        var art = new GroundArt(this) { ZIndex = -10 };
        _game.AddChild(art);

        // 尸体（GroundArt 之上、玩家之下）：每场战役都有阵亡者
        AddCorpses();

        // 掩体沙袋在人之前（ZIndex 5，高于玩家 0 与敌兵 2），
        // 经过时人从垛子后面过，而不是站在垛子顶上。
        _coverArt = new CoverArt { ZIndex = 5 };
        _game.AddChild(_coverArt);

    }

    private void AddCorpses()
    {
        // 尸体散布点：柳条湖爆破点、北大营围墙外、民居群旁、通信据点旁
        // CorpseActor ZIndex = 0（跟玩家同层，站在房子和地面前面）

        // 1. 柳条湖爆破点（日军爆破兵，趴在铁轨缺口旁）——这一夜的起因
        // v3.19/v3.21: corpse tilt is cancelled against the painting's own angle; pose fixed.
        _game.AddChild(new CorpseActor(CorpseActor.Faction.IJA,
            new Vector2(1010, GroundY - 2), false, 0.03f, true, CorpseActor.Pose.Prone));

        // 2. 北大营围墙外（东北军，仰卧，双臂摊开）——奉令未还手，倒在自己营房前
        _game.AddChild(new CorpseActor(CorpseActor.Faction.NortheastArmy,
            new Vector2(2380, GroundY - 2), true, -0.02f, true, CorpseActor.Pose.Supine));

        // 3. 北大营外（东北军，俯卧，头朝营房）——那一夜的沉默
        _game.AddChild(new CorpseActor(CorpseActor.Faction.NortheastArmy,
            new Vector2(2050, GroundY - 2), false, 0.04f, true, CorpseActor.Pose.Prone));

        // 4. 民居群旁（东北军，俯卧，头朝村落）——护着身后的人
        _game.AddChild(new CorpseActor(CorpseActor.Faction.NortheastArmy,
            new Vector2(3300, GroundY - 2), false, -0.03f, true, CorpseActor.Pose.Supine));

        // 5. 通信据点旁（东北军，俯卧）——倒下前还在护着那台发信机
        _game.AddChild(new CorpseActor(CorpseActor.Faction.NortheastArmy,
            new Vector2(4500, GroundY + 9), true, 0.03f, true, CorpseActor.Pose.Prone));
    }

    private void BuildPlayer()
    {        _playerShadow = new GroundShadow();
        _game.AddChild(_playerShadow);

        _player = new FieldPlayer { Position = new Vector2(230, GroundY - 2) };
        // v3.19: 1.22 was the old scale. The soldier now reads 94px against
        // a 108px before, which puts the eave of a village house - measured
        // at 1.25x its old size - at 1.49 soldier heights instead of 1.03.
        // A man standing in front of a house should be clearly shorter than
        // the building, not the same height.
        _player.Scale = new Vector2(1.06f, 1.06f);
        _player.Fired += OnPlayerFired;
        _player.Hit += OnPlayerHit;
        _player.Down += OnPlayerDown;
        _player.DryFired += OnDryFired;
        // v3.46: the Landed event existed since the coyote/jump-buffer pass
        // but nothing subscribed - falls ended in silence. A snow burst at
        // the boots sells the impact the figure animation can't.
        _player.Landed += OnPlayerLanded;
        _game.AddChild(_player);
        // GroundShadow node owns the player shadow, figure must not draw a second one
        // (two overlapping ellipses = the dark mud puddle under his legs)
        _player.Figure.DrawShadow = false;

        _cam = new Camera2D
        {
            // v2.7: the left and right limits are opened up, so the camera and
            // the parallax layers share the same _camX.
            // Before this LimitLeft=0 / LimitRight=WorldW, so Godot hard clamped
            // the screen centre into [640, WorldW-640], while the parallax layers
            // moved by _camX (the player X). Two different sources ->
            // from x=230 up to x=640 the background kept sliding relative to
            // the player, and at x=640 it snapped into place all at once.
            // That is the mystery jolt at the start of the level.
            // The vertical limit stays (the camera must not leave the snow).
            LimitLeft = -100000,
            LimitRight = 100000,
            LimitTop = 0,
            LimitBottom = 900,
            // 不用内置平滑：它与 _Process 里的视差层更新不在同一时序，
            // 会差一帧互相拉扯 → 走动时背景树/草疯狂鬼造抖动。改为手动平滑（见 _Process）。
            PositionSmoothingEnabled = false,
        };
        AddChild(_cam);
        _cam.MakeCurrent();

        _fx = new TracerFx { ZIndex = 8 };
        _game.AddChild(_fx);
    }

    private const int TestimonyTotal = 5;

    private void BuildTestimonies()
    {
        AddTestimony("t1", new Vector2(720, GroundY), "口述 01 · 南满铁路巡道工 王福顺（化名，时年 52 岁）",
            new[]
            {
                "“那夜里静得出奇，连虫都不叫。我巡到柳条湖这一段，刚蹲下紧一道鱼尾板，后头‘轰’一声——铁轨翘上天，枕木飞出十几丈。”",
                "“可那趟南下的客货混合列车， timetable 上明明还得一个时辰才到。火车还没来，地就先炸了。这账，怎么算？”",
            },
            "依据：1931.9.18 柳条湖段南满铁路被炸、关东军反诬中国军队所为的史实；人物为复合虚构，不对应单一在世者。");

        AddTestimony("t2", new Vector2(2000, GroundY), "口述 02 · 东北军独立第七旅士兵 李大山（化名，时年 21 岁）",
            new[]
            {
                "“营里电话响了一夜。上头一句话传下来：‘不准抵抗，挺着死，为国牺牲。’我们趴在工事里，看着北大营的墙被人翻进来。”",
                "“我班长把枪栓拉开了又合上，合上了又拉开。最后没敢扣。那一夜，我们丢了整座营房，没放几枪。”",
            },
            "依据：九一八当夜东北军奉「不抵抗令」、北大营万余人撤出、营房被占的史实；人物为复合虚构。");

        AddTestimony("t3", new Vector2(3000, GroundY), "口述 03 · 沟帮子一带农户 张王氏（化名，时年 38 岁）",
            new[]
            {
                "“天没亮，前村的狗叫成一片。我家男人说‘怕是要变天’，把门槛外的石磨都搬进屋。天一亮，门口站了端枪的人。”",
                "“他们翻了柜，抱走了鸡。我没拦。我拦得住什么。我只把孩子的名儿记在袄里子上，怕哪天走散了，他别忘了自己叫什么。”",
            },
            "依据：九一八后日军迅速占领沈阳及周边、民众仓促逃难的史实；人物为复合虚构。");

        AddTestimony("t4", new Vector2(3520, GroundY), "口述 04 · 逃难母亲 秀兰（化名，时年 29 岁）",
            new[]
            {
                "“我背着小女儿走了两天。她问我‘娘，咱们去哪’。我说‘去有灯的地方’。她又问‘那咱家的灯呢’。我说‘娘就是你的灯。’”",
                "“路上有兵，也有兵给咱们让过道。我不知道哪边是对的。我只知道，孩子得活。”",
            },
            "依据：九一八后东北民众大规模流亡关内的史实；人物为复合虚构。");

        AddTestimony("t5", new Vector2(4320, GroundY), "口述 05 · 邮电局线务员 小赵（化名，时年 25 岁）",
            new[]
            {
                "“天亮前，局里命令把通往奉天和长春的长途线先掐了——说是怕消息传出去乱。可掐了线，外头更不知道里头出了什么事。”",
                "“你们这台发信机，要是还能摇起电、把北大营实情发出去，就发出去。纸面上的‘平安’，顶不了一个活人的嘴。”",
            },
            "依据：九一八当夜东北地方通信与电报系统被控制、消息封锁的史实；人物为复合虚构。");
    }

    private void AddTestimony(string id, Vector2 pos, string title, string[] lines, string source)
    {
        var t = new N918Testimony(this, id, pos, title, lines, source);
        _game.AddChild(t);
        _testimonies.Add(t);
    }

    private void BuildHud()
    {
        _hud = new Hud();
        AddChild(_hud);
        _hud.SetDate("1931 年 9 月 18 日 · 中国（中华民国时代）沈阳近郊 · 柳条湖—北大营—通信据点");
        UpdateObjective();

        var layer = new CanvasLayer { Layer = 11 };
        AddChild(layer);

        // vitals drawn as icons: red hearts + stick grenades, self-redraws
        _vitals = new N918VitalsBar { Position = new Vector2(880, 10) };
        _vitals.Bind(_player);
        layer.AddChild(_vitals);

        // 弹药改用子弹图标：打掉一发暗一发，拾取了再点亮，不再用点点点
        _ammoBar = new N918AmmoBar { Position = new Vector2(920, 38) };
        _ammoBar.Bind(_player);
        layer.AddChild(_ammoBar);
    }

    private void BuildScreenFx()
    {
        AddChild(new VignetteLayer());
        AddChild(new FogScreen());

        var snowLayer = new CanvasLayer { Layer = 6 };
        AddChild(snowLayer);
        _snow = new SnowScreen();
        snowLayer.AddChild(_snow);
        // 九一八初秋：无飘雪粒子，始终隐藏 SnowScreen（其 _Wind/_Burst 仍调用但不可见）。
        // 若日后要表现「微风飘叶」，可在此换用落叶粒子，不改其它逻辑。
        _snow.Visible = false;

        var flashLayer = new CanvasLayer { Layer = 7 };
        AddChild(flashLayer);
        _flash = new ColorRect { Color = new Color(1, 0.97f, 0.90f, 0) };
        _flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _flash.MouseFilter = Control.MouseFilterEnum.Ignore;
        flashLayer.AddChild(_flash);

        var blackLayer = new CanvasLayer { Layer = 8 };
        AddChild(blackLayer);
        _blackout = new ColorRect { Color = new Color(0, 0, 0, 0) };
        _blackout.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _blackout.MouseFilter = Control.MouseFilterEnum.Ignore;
        blackLayer.AddChild(_blackout);
    }

    // ================= 主循环 =================

    public override void _Process(double delta)
    {
        // Clamp delta 防止大跳帧（某帧卡顿导致 delta 远大于 1/60s，
        // 指数平滑 k 突增 → 视差层平移跳变 → 树草鬼畜 + 白线闪烁）
        float f = Mathf.Min(0.05f, (float)delta);
        _elapsed += f;

        // parallax: camera X is smoothed by hand, and the same value has to go
        // to the camera and to the parallax layers in the same frame,
        // otherwise the layers pull against each other and shake.
        var target = new Vector2(_player.GlobalPosition.X, _player.GlobalPosition.Y - 118f);
        float k = 1f - Mathf.Exp(-6.5f * f);
        _camX += (target.X - _camX) * k;
        // v2.7: the camera clamps itself (no more Camera2D.Limit), and the very
        // same clamped value is then fed to the parallax layers.
        // Before this Camera2D.Limit clamped the camera while _camX drove the
        // parallax layers - two different sources ->
        // at the start the player was at x=230 so the camera was pinned at 640,
        // but the parallax layers were still computing from 230. The background
        // slid against the world the whole way, and the instant the camera was
        // released at x=640 it snapped into place. That is the mystery jolt.
        float camX = Mathf.Clamp(_camX, ViewHalfW, WorldW - ViewHalfW);
        float camY = Mathf.Clamp(target.Y, ViewHalfH, 900f - ViewHalfH);
        _cam.Position = new Vector2(camX, camY);
        foreach (var l in _layers)
            l.Position = new Vector2(camX * (1f - l.Factor), 0f);
        if (System.Environment.GetEnvironmentVariable("WBDUMP") == "1" && _elapsed > 2f && _elapsed < 2.05f)
            foreach (var l in _layers)
                GD.Print($"DIAGDBG layer kind={l.Kind} factor={l.Factor} pos={l.Position} camX={camX}");
        if (_smokeMid != null)
            _smokeMid.Position = new Vector2(camX * (1f - MidFactor), 0f);
        _snow.Wind = 0.55f + Mathf.Sin(_elapsed * 0.35f) * 0.35f;

        UpdateDayCycle(f);

        // 相机抖动（爆炸/开枪冲击）：trauma² 模型，平滑衰减 + 低频抖动，
        // 不再每帧纯随机 → 没有"嗡嗡"的鬼畜感；trauma 越高抖得越狠。
        _shake = Mathf.Max(0f, _shake - f * 1.6f);
        float trauma = _shake * _shake;
        float sPhase = _elapsed * 30f;
        var off = new Vector2(
            Mathf.Sin(sPhase) * trauma * 42f,
            Mathf.Cos(sPhase * 1.3f) * trauma * 30f);
        _cam.Offset = off;

        // 玩家的影子钉在地面：跳得越高越小越淡，人不会显得脱离地面
        _playerShadow.X = _player.GlobalPosition.X;
        _playerShadow.Air = Mathf.Max(0f, (GroundY - 2f) - _player.GlobalPosition.Y);
        _playerShadow.QueueRedraw();

        _flashA = Mathf.Max(0f, _flashA - f * 1.6f);
        _flash.Color = new Color(1f, 0.96f, 0.88f, _flashA);
        _blackA = Mathf.MoveToward(_blackA, _blackTarget, f * 2.2f);
        _blackout.Color = new Color(0, 0, 0, _blackA);

        bool overlay = _noteOpen || _ended || !_started || _houseUi != null;
        _player.ControlLocked = overlay;

        if (!_started || _ended) return;

        UpdateInteractions();
        UpdateGrenades(f);
        UpdateCover(f);
        UpdateRecording(f);
        UpdateObjective();
        UpdatePrompts();
        TryResistChoice();
        CheckWaves();
        CheckEnding();
    }

    // ================= 昼夜 =================

    /// <summary>
    /// 游戏钟推进 + 天色。九一八初秋凉夜：开场 22:00，月已落，仅星空；
    /// 日出约 05:37 / 日落约 18:02（沈阳近郊实算），全程基本停在夜里。
    /// </summary>
    private void UpdateDayCycle(float f)
    {
        if (DebugClock >= 0f)
        {
            _clock = DebugClock;
        }
        else if (_started && !_ended)
        {
            _clock += f * ClockRate / 3600f;
            if (_clock > ClockMax) _clock = ClockMax;
        }

        float t = _clock;
        // ---- world tint keyframes: pre-dawn / dawn / day / dusk / night ----
        Color tint;
        if (t < SunriseH - 0.9f)          // deep pre-dawn
            tint = new Color(0.42f, 0.47f, 0.60f);
        else if (t < SunriseH + 0.7f)     // dawn warms up
            tint = LerpC(new Color(0.42f, 0.47f, 0.60f), new Color(0.85f, 0.78f, 0.72f),
                (t - (SunriseH - 0.9f)) / 1.6f);
        else if (t < SunsetH - 0.8f)      // full winter day
            tint = new Color(1f, 1f, 1f);
        else if (t < SunsetH + 0.9f)      // dusk amber
            tint = LerpC(new Color(1f, 1f, 1f), new Color(0.72f, 0.62f, 0.58f),
                (t - (SunsetH - 0.8f)) / 1.7f);
        else                              // night falls, no moon tonight (月已落)，初秋清朗夜的暖中性夜色
            tint = new Color(0.52f, 0.50f, 0.56f);
        _tint.Color = tint;

        // shadow kit follows the same clock: sun azimuth + night softness
        SnowShadow.DayT = t;

        if (_sky != null) _sky.Art.DayT = t;
    }

    private static Color LerpC(Color a, Color b, float t)
    {
        t = Mathf.Clamp(t, 0f, 1f);
        return new Color(
            Mathf.Lerp(a.R, b.R, t),
            Mathf.Lerp(a.G, b.G, t),
            Mathf.Lerp(a.B, b.B, t));
    }

    // ================= 捡枪 / 进屋 / 手榴弹 =================

    /// <summary>
    /// v3.54: one referee for the E key. House door, rifle pickup and sandbag
    /// stack each used to run their own fixed priority (house beat pickup
    /// outright, cover half-in), so a rifle by a doorstep was unreachable and
    /// tapping E beside a testimony opened the house instead. Now the CLOSEST
    /// candidate wins; on a tie pickup beats cover beats house, and the pickup
    /// gets an 8px favour because it is the smallest, most precise target.
    /// </summary>
    private void NearestTargets(float px, out EnterableHouse house,
        out WeaponPickup pickup, out int cover, out float dist)
    {
        house = null;
        float hd = 9999f;
        foreach (var h in _houses)
        {
            float d = Mathf.Abs(h.DoorX - px);
            if (d < 78f && d < hd) { hd = d; house = h; }
        }

        pickup = null;
        float pd = 9999f;
        foreach (var p in _pickups)
        {
            if (p.Looted || !IsInstanceValid(p)) continue;
            float d = Mathf.Abs(p.Position.X - px) - 8f;
            if (d < 64f && d < pd) { pd = d; pickup = p; }
        }

        cover = -1;
        float cd = 9999f;
        int ci = _coverArt?.NearestStack(px, 100f) ?? -1;
        if (ci >= 0) { cd = Mathf.Abs(_coverArt.StackX(ci) - px); cover = ci; }

        dist = Mathf.Min(hd, Mathf.Min(pd, cd));
        if (pd > hd || pd > cd) pickup = null;
        if (hd > pd || hd > cd) house = null;
        if (cd > pd || cd > hd) cover = -1;
    }

    private void UpdateInteractions()
    {
        if (_houseUi != null) return;
        float px = _player.GlobalPosition.X;

        NearestTargets(px, out var nearHouse, out var nearPickup, out int coverIdx, out _);

        if (Input.IsActionJustPressed("interact"))
        {
            if (nearPickup != null) LootPickup(nearPickup);
            else if (coverIdx >= 0) EnterCover(coverIdx);
            else if (nearHouse != null) OpenHouse(nearHouse);
        }
    }

    // —— v3.53 沙袋掩体：E 躲入、挡子弹不挡手榴弹、可被炸毁 ——

    private void EnterCover(int idx)
    {
        if (_coverIdx >= 0 || _coverCd > 0f || _coverArt == null || _player == null) return;
        if (!_player.IsOnFloor()) return;
        _coverIdx = idx;
        _coverCd = 0.3f;
        _player.CoverHold = true;
        _player.Velocity = Vector2.Zero;
        // v3.54: the old instant teleport read as a glitch-frame ("怪怪的").
        // A short eased slide into the tuck spot keeps the world continuous.
        // Bound to the PLAYER node (always in-tree while physics runs) - the
        // level-bound CreateTween() returned null inside the capture harness
        // and the tuck silently broke. Falls back to the instant tuck so the
        // interaction can never die to a tween hiccup.
        var tuck = new Vector2(_coverArt.StackX(idx) - 36f, GroundY - 2f);
        var tw = _player.CreateTween();
        if (tw != null && tw.IsValid())
        {
            _coverTween = tw;
            _coverTween.TweenProperty(_player, "position", tuck, 0.14f)
                .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        }
        else
        {
            _player.Position = tuck;
        }
        _hud.Toast("你缩到沙袋后面——挡子弹，挡不住手榴弹（E 或移动键离开）");
    }

    private void ExitCover()
    {
        _coverTween?.Kill();
        _coverTween = null;
        _coverIdx = -1;
        _coverCd = 0.3f;
        if (_player != null) _player.CoverHold = false;
    }

    // ---- v3.53: capture-scene test hooks (also handy for tuning) ----
    internal float DebugCoverX(int i) => _coverArt == null ? 0f : _coverArt.StackX(i);
    internal void DebugBlast(float x) => Explode(new Vector2(x, GroundY - 20f));

    private void UpdateCover(float f)
    {
        if (_coverCd > 0f) _coverCd -= f;

        if (_coverIdx >= 0 && _ended)
        {
            ExitCover();
            return;
        }
        if (_noteOpen || _houseUi != null || _ended) return;

        if (_coverIdx >= 0)
        {
            if (_coverArt.IsDestroyed(_coverIdx))
            {
                ExitCover();
                _hud.Toast("沙袋被炸烂了——这里不能再躲了！");
            }
            else if (_coverCd <= 0f
                     && (Input.IsActionJustPressed("interact")
                         || Mathf.Abs(Input.GetAxis("move_left", "move_right")) > 0.3f
                         || Input.IsActionJustPressed("advance")))
            {
                ExitCover();
                _hud.Toast("离开了沙袋掩体。");
            }
            else if (PlayerAlive)
            {
                // 敌兵不会让你永远缩着：定时掷木柄手榴弹逼你挪窝
                if (_enemyNadeTimer < 0f) _enemyNadeTimer = (float)GD.RandRange(6.0, 10.0);
                _enemyNadeTimer -= f;
                if (_enemyNadeTimer <= 0f)
                {
                    _enemyNadeTimer = (float)GD.RandRange(8.0, 13.0);
                    EnemyThrowGrenade();
                }
            }
        }
    }

    private void EnemyThrowGrenade()
    {
        N918EnemySoldier thrower = null;
        float best = 820f;
        foreach (var e in _enemies)
        {
            if (!IsInstanceValid(e) || e.Gone) continue;
            float d = Mathf.Abs(e.Position.X - _player.GlobalPosition.X);
            if (d < best) { best = d; thrower = e; }
        }
        if (thrower == null || thrower._throwing) return;

        var start = thrower.Position + new Vector2(-20f, -70f);
        var target = new Vector2(_coverArt.StackX(_coverIdx) + 10f, GroundY - 8f);
        float T = 1.05f;                      // flight time
        float g = 1500f;                      // matches N918GrenadeProj gravity
        var vel = new Vector2((target.X - start.X) / T,
                              (target.Y - start.Y - 0.5f * g * T * T) / T);

        // v3.55: 标准投弹动作——拉弦（摆出挥臂待掷姿）→ 蓄力半秒 → 出手。
        // 以前是凭空出现一颗雷，既没有动作也没有预警帧。
        thrower._throwing = true;
        thrower._fig.ThrowPose = 1f;
        GetTree().CreateTimer(0.5f).Timeout += () =>
        {
            if (!IsInstanceValid(thrower) || thrower.Gone) return;
            thrower._fig.ThrowPose = 0f;
            thrower._throwing = false;
            var nade = new N918GrenadeProj(start, vel);
            _game.AddChild(nade);
            _grenades.Add(nade);
            _hud.Toast("敌兵拉弦掷来木柄手榴弹——快离开落点！");
            Shake(0.08f);
        };
    }

    private void LootPickup(WeaponPickup p)
    {
        p.MarkLooted();
        if (p.Condition == WeaponPickup.Kind.Broken)
        {
            _hud.Toast("枪栓锈死了，拉不开——这支打不响。");
            return;
        }
        int took = _player.AddAmmo(p.Ammo);
        _hud.Toast(took >= p.Ammo
            ? $"捡获一支步枪，里面还剩 {p.Ammo} 发。"
            : $"捡获步枪，弹袋满了，只带上 {took} 发。");
        Shake(0.06f);
    }

    private void OpenHouse(EnterableHouse h)
    {
        string title = h.Id switch
        {
            "h1" => "柳条湖边的小屋",
            "h2" => "北大营外的民居",
            "h3" => "沟帮子农户的院子（半塌）",
            "comm" => "通信据点 · 无线电发信机",
            _ => "烧塌的院落",
        };

        HouseInteriorUi ui;
        // 三种固定常识格局：住家(两间民居左右变体) / 半塌农舍(焚毁) / 通信据点机房
        RoomType rt = h.Id == "comm" ? RoomType.CommOutpost
                    : h.Id == "h3" ? RoomType.BurntWarehouse
                    : RoomType.Residence;
        int rv = h.Id == "h2" ? 1 : 0;
        if (h.Id == "comm")
        {
            // 通信据点：注入发信机前置（信齐且通电）与发报/合闸回调
            ui = new HouseInteriorUi(h.Id, title, GameState.I.S.N918Loot,
                roomType: rt,
                canTransmit: () => GameState.I.S.N918EnvelopeCount >= EnvelopeNeed
                                 && GameState.I.S.N918Powered,
                onTransmit: () =>
                {
                    GameState.I.S.N918RadioSent = true;
                    GameState.I.Save();
                    UpdateObjective();
                    CheckEnding();
                },
                onPower: () =>
                {
                    GameState.I.S.N918Powered = true;
                    GameState.I.Save();
                    UpdateObjective();
                },
                roomLayout: true);
        }
        else
        {
            ui = new HouseInteriorUi(h.Id, title, GameState.I.S.N918Loot,
                roomType: rt, roomVariant: rv, roomLayout: true);
        }

        ui.CanHeal = () => _player.Wounded;
        ui.GotAmmo += n =>
        {
            int took = _player.AddAmmo(n);
            if (took < n) _hud.Toast($"弹袋只装得下 {took} 发，剩下的只好留在盒里。");
        };
        ui.GotGrenade += n => _player.AddGrenade(n);
        ui.GotHeal += n =>
        {
            int before = _player.Hp;
            _player.Heal(n);
            int gained = _player.Hp - before;
            _hud.Toast(gained > 0
                ? (n > 1 ? "医药包把伤全处理了。" : "纱布裹紧伤口，好受了些。")
                : "身上没伤口，用不上。");
        };
        // 信：每间屋灶台下压着一封，集齐三封才够发报
        ui.GotLetter += n =>
        {
            GameState.I.S.N918EnvelopeCount += n;
            GameState.I.Save();
            _hud.Toast($"拾得信 ×{n}（共 {GameState.I.S.N918EnvelopeCount}/{EnvelopeNeed}）");
            UpdateObjective();
        };
        ui.Closed += () => _houseUi = null;
        AddChild(ui);
        _houseUi = ui;
    }

    /// <summary>发报所需的最少信件数（每间屋至多 1 封，需走访 ≥3 间）。</summary>
    private const int EnvelopeNeed = 3;

    private bool _playerThrowing;

    private void UpdateGrenades(float f)
    {
        if (_noteOpen || _houseUi != null || _ended) return;
        // v3.55: 玩家掷弹也走标准动作——拉弦挥臂 0.45s 蓄力，出手位置按松手瞬间算
        if (Input.IsActionJustPressed("grenade") && _player.Grenades > 0 && !_playerThrowing)
        {
            _player.Grenades--;
            _playerThrowing = true;
            _player.Figure.ThrowPose = 1f;
            GetTree().CreateTimer(0.45f).Timeout += () =>
            {
                _playerThrowing = false;
                if (_ended || !IsInstanceValid(_player)) return;
                _player.Figure.ThrowPose = 0f;
                var g = new N918GrenadeProj(_player.GlobalPosition + new Vector2(_player.Facing * 14f, -46f),
                    _player.Facing);
                _game.AddChild(g);
                _grenades.Add(g);
                _hud.Toast("手榴弹出手——卧倒！");
            };
        }

        for (int i = _grenades.Count - 1; i >= 0; i--)
        {
            var g = _grenades[i];
            if (!g.Exploded) continue;
            _grenades.RemoveAt(i);
            Explode(g.GlobalPosition);
            g.QueueFree();
        }
    }

    private void Explode(Vector2 at)
    {
        Artillery(0.65f);
        Shake(0.9f);
        _fx.Puff(at, 30f);
        _fx.Puff(at + new Vector2(-24f, 8f), 22f);
        _fx.Puff(new Vector2(at.X, GroundY - 4f), 26f);
        foreach (var e in _enemies)
        {
            if (!IsInstanceValid(e) || e.Gone) continue;
            if (e.Position.DistanceTo(at) < 165f)
            {
                e.Hit();
                e.Hit();   // blast kills outright
            }
        }
        // v3.53: the blast chews through sandbag cover too - direct hit
        // flattens the wall, a blast one wall over mangles it. The player
        // damage check below deliberately ignores cover: a grenade does not
        // care that you are hiding.
        if (_coverArt != null && _coverArt.Blast(at.X))
        {
            _hud.Toast("沙袋掩体被炸毁了！");
            // v3.55: 墙被炸开的瞬间一蓬沙袋真的飞出去（散落场由 DrawWreck 留下）
            if (!float.IsNaN(_coverArt.WreckX))
                _game.AddChild(new CoverBurstFx { Position = new Vector2(_coverArt.WreckX, GroundY - 14f) });
        }
        if (_player.GlobalPosition.DistanceTo(at) < 120f)
            _player.TakeHit(at.X);
    }

    /// <summary>玩家是否正躲在沙袋掩体后（蹲/趴才算真掩护）。</summary>
    public bool PlayerInCover()
    {
        if (_player == null || !_player.IsOnFloor()) return false;
        // v3.54: E-tuck counts even without holding the crouch key - the pose
        // is already crouched (CoverHold), but this check only read raw input,
        // so a tucked player who never touched crouch was shot at standing
        // odds. That was the real "hiding behind sandbags and still got hit".
        if (_coverIdx >= 0) return true;
        bool low = Input.IsActionPressed("crouch") || Input.IsActionPressed("prone");
        if (!low) return false;
        float px = _player.GlobalPosition.X;
        return _coverArt != null && _coverArt.Covers(px);
    }

    private void UpdateRecording(float f)
    {
        N918Testimony near = null;
        float best = 86f;
        foreach (var t in _testimonies)
        {
            if (t.Recorded) continue;
            float d = Mathf.Abs(t.GlobalPosition.X - _player.GlobalPosition.X);
            if (d < best) { best = d; near = t; }
        }

        bool holding = Input.IsActionPressed("interact") && near != null && !_noteOpen;
        if (holding)
        {
            _record += f;
            _hud.SetRecord(Mathf.Min(1f, _record / 1.25f), NearEnemy(320));
            if (_record >= 1.25f)
            {
                _record = 0f;
                _hud.SetRecord(0, false);
                Record(near);
            }
        }
        else
        {
            _record = Mathf.Max(0f, _record - f * 2.4f);
            _hud.SetRecord(_record > 0f ? Mathf.Min(1f, _record / 1.25f) : 0f, NearEnemy(320));
        }
    }

    private bool NearEnemy(float range)
    {
        foreach (var e in _enemies)
            if (!e.Gone && Mathf.Abs(e.Position.X - _player.GlobalPosition.X) < range) return true;
        return false;
    }

    private void Record(N918Testimony t)
    {
        t.Recorded = true;
        GameState.I.S.N918Testimony.Add(t.Id);
        GameState.I.S.N918Visited.Add(t.Id);
        if (t.Id == "t1") GameState.I.S.N918Visited.Add("柳条湖");   // 柳条湖见证点
        GameState.I.Save();
        _checkpoint = new Vector2(Mathf.Max(220f, t.GlobalPosition.X - 180f), GroundY - 2f);
        _hud.Toast($"已记录口述  {GameState.I.S.N918Testimony.Count} / {TestimonyTotal}");

        var note = new FieldNoteUi("九一八 · 1931 年 9 月 18 日 · 沈阳近郊", t.Title, t.Lines, t.Source, () =>
        {
            _noteOpen = false;
        });
        _noteOpen = true;
        AddChild(note);
        UpdateObjective();
    }

    private void UpdateObjective()
    {
        var s = GameState.I.S;
        int letters = s.N918EnvelopeCount;
        string tail;
        if (s.N918RadioSent)
            tail = "电波已发出——你带出了那夜的真相。";
        else if (!s.N918Powered)
            tail = letters >= EnvelopeNeed
                ? "信已集齐——到通信据点合闸供电，再发报。"
                : $"记录口述、搜屋拾信（{letters}/{EnvelopeNeed}），再到通信据点发报。";
        else
            tail = $"已供电——操作发信机，把实情传出去（信 {letters}/{EnvelopeNeed}）。";
        _hud.SetObjective($"信 {letters}/{EnvelopeNeed} · 供电{(s.N918Powered ? "✓" : "…")} · {tail}");
    }

    private void UpdatePrompts()
    {
        float px = _player.GlobalPosition.X;

        // v3.54: same referee as the E handler, so the prompt never lies
        // (a doorstep rifle used to show "进屋搜索").
        NearestTargets(px, out var nearHouse, out var nearPickup, out _, out _);

        N918Testimony near = null;
        float bestT = 86f;
        foreach (var t in _testimonies)
        {
            if (t.Recorded) continue;
            float d = Mathf.Abs(t.GlobalPosition.X - px);
            if (d < bestT) { bestT = d; near = t; }
        }

        if (nearPickup != null)
            _hud.ShowPrompt(nearPickup.Condition == WeaponPickup.Kind.Broken
                ? "按 E · 捡起步枪（可能打不响）"
                : "按 E · 捡枪拿子弹");
        else if (nearHouse != null)
            _hud.ShowPrompt("按 E · 进屋搜索");
        else if (near != null)
            _hud.ShowPrompt("按住 E · 记录口述");
        else if (px > 3900f && !GameState.I.S.N918RadioSent)
            _hud.ShowPrompt("进通信据点 · 发报通关");
        else
            _hud.ShowPrompt("");

        // target arrow below; vitals + ammo bars redraw themselves

        // 目标在屏幕外时给纸色箭头：默认指向通信据点（通关点）
        Vector2 target = near != null
            ? near.GlobalPosition
            : new Vector2(4000, GroundY);
        Vector2 screen = target - _cam.GlobalPosition + new Vector2(640, 360);
        bool off = screen.X > 1190f || screen.X < 60f;
        if (off && (_elapsed % 4f) < 3.2f)
            _hud.SetArrow(true, new Vector2(1230, Mathf.Clamp(screen.Y, 220, 560)), 0f);
        else
            _hud.SetArrow(false, Vector2.Zero, 0f);
    }

    private void CheckWaves()
    {
        float px = PlayerX;
        int alive = 0;
        foreach (var e in _enemies) if (IsInstanceValid(e) && !e.Gone) alive++;

        int want = _wave;
        if (px > 1150f) want = Mathf.Max(want, 1);
        if (px > 2050f) want = Mathf.Max(want, 2);
        if (px > 2980f) want = Mathf.Max(want, 3);
        if (px > 3900f) want = Mathf.Max(want, 4);
        if (want == _wave || alive >= 3) return;

        _wave = want;
        float baseX = _wave switch
        {
            1 => 1720f,
            2 => 2580f,
            3 => 3520f,
            _ => 4380f,
        };
        // 人数随机 1~3（历史里没有“每次都恰好两个人”的巧合），站位与出场也错开
        int count = (int)GD.RandRange(1, 3);
        var lanes = new[] { -48f, 0f, 48f };
        for (int i = lanes.Length - 1; i > 0; i--)
        {
            int j = (int)GD.RandRange(0, i);
            (lanes[i], lanes[j]) = (lanes[j], lanes[i]);
        }
        for (int i = 0; i < count; i++)
        {
            float lx = baseX + i * 110f + (float)GD.RandRange(-45f, 45f);
            float ln = lanes[i];
            if (i == 0)
                SpawnEnemy(lx, ln);
            else
            {
                // 后续的间隔 0.5~0.9s 从雪梁后陆续跟出，不是整队齐刷刷刷出来
                float delay = 0.5f * i + (float)GD.RandRange(0f, 0.4f);
                GetTree().CreateTimer(delay).Timeout += () =>
                {
                    if (!_ended) SpawnEnemy(lx, ln);
                };
            }
        }
        Artillery(0.35f);
        _hud.Toast("前方土坎后出现关东军尖兵");
    }

    /// <summary>
    /// 敌人的景深比例。同一个波次里三个人站三条 lane，lane 越小站得越远，
    /// 越远的人要明显小一号 —— 不然三个人站一排像三胞胎，一眼假。
    /// 玩家站在最前面那一层（1.0），远一排 0.82，更远一排 0.92。
    /// </summary>
    private static float EnemyScaleForLane(float lane)
    {
        if (lane < -1f) return 1.06f * 0.82f;   // 最远的一排
        if (lane > 1f) return 1.06f * 1.00f;    // 最近的一排，跟玩家同层
        return 1.06f * 0.92f;                   // 中间
    }

    private void SpawnEnemy(float x, float lane)
    {
        if (x > WorldW - 120f) x = WorldW - 120f;
        var e = new N918EnemySoldier(this, new Vector2(x, GroundY), lane);
        // v3.25: the far lane used to be the same size as the near one and
        // only a little paler, so three men of the same height stood in a
        // row at three different depths and the whole depth read collapsed.
        // Now the lane decides the size as well as the tint: the further
        // back a man stands the smaller he is, the closer the bigger.
        e.Scale = new Vector2(EnemyScaleForLane(lane), EnemyScaleForLane(lane));
        e.Modulate = lane < 0f
            ? new Color(0.86f, 0.87f, 0.88f, 1f)
            : new Color(0.97f, 0.97f, 0.98f, 1f);
        _game.AddChild(e);
        _enemies.Add(e);

        // 铁锹/雪尘：他们进入画面时踢起的雪
        for (int i = 0; i < 4; i++)
            _fx.Puff(new Vector2(x - i * 9f, GroundY - 3f), 10f);
    }

    private void CheckEnding()
    {
        if (_ended) return;
        // 通关门禁：无线电发信机把实情发出去（N918RadioSent）
        if (!GameState.I.S.N918RadioSent) return;

        _ended = true;
        GameState.I.S.N918Done = true;
        GameState.I.S.MapNote918 = true;   // 已实际通关九一八关卡，进入江桥前不再重复弹九一八档案
        GameState.I.Save();
        _player.ControlLocked = true;
        _blackTarget = 0.65f;
        _hud.ShowPrompt("");

        var end = new N918LevelEndUi(_main);
        AddChild(end);
    }

    private DialogueUi _dialogue;

    /// <summary>
    /// 北大营围墙附近的不抵抗令悲剧抉择（仅触发一次）。遵令 / 违令 写入 N918Resist，
    /// 影响结算双结局。复用 _noteOpen 锁定玩家移动；对话框自身以 CanvasLayer 捕获鼠标点击。
    /// </summary>
    private void TryResistChoice()
    {
        if (_ended || _noteOpen || _houseUi != null) return;
        if (GameState.I.S.N918Resist != 0) return;     // 已抉择（含「抉择中」哨兵 -1）
        if (PlayerX < 1900f) return;                    // 抵达北大营才触发
        GameState.I.S.N918Resist = -1;                  // 哨兵：防止重复弹出
        _noteOpen = true;
        _dialogue ??= new DialogueUi();
        if (_dialogue.GetParent() == null) AddChild(_dialogue);
        _dialogue.ShowChoice("不抵抗令",
            "北大营的墙外人影幢幢。电话里那句话又响起来——「不准抵抗，挺着死，为国牺牲」。\n你握着枪，知道这一步之后，今夜的走向就定了。",
            new (string, bool)[] { ("遵令 · 放下枪，按上头的来", true), ("违令 · 还手，护住身后的人", true) },
            pick =>
            {
                GameState.I.S.N918Resist = pick == 0 ? 1 : 2;
                GameState.I.Save();
                _noteOpen = false;
                _hud.Toast(pick == 0
                    ? "你合上了枪栓。北大营一夜未还手。"
                    : "你拉开了枪栓。墙外的人影顿了一下——这一夜，不再只是沉默。");
            });
    }

    // ================= 战斗与受击 =================

    private void OnPlayerFired(Vector2 origin, bool faceRight)
    {
        // v3.51: was 0.55 - trauma^2 put every bolt shot at +-13px of screen
        // jolt, which read as a glitch rather than recoil (user report
        // 2026-10-31). 0.20 -> ~+-2px: a nudge in the shoulder, the real
        // feedback stays with the muzzle flash and the body pushback.
        Shake(0.20f);
        N918EnemySoldier target = null;
        float best = 980f;
        foreach (var e in _enemies)
        {
            if (!IsInstanceValid(e) || e.Gone) continue;
            float dx = e.Position.X - origin.X;
            if ((faceRight && dx < 0) || (!faceRight && dx > 0)) continue;
            if (Mathf.Abs(dx) > 980f) continue;
            if (Mathf.Abs(dx) < best) { best = Mathf.Abs(dx); target = e; }
        }

        // aim with the mouse but a bolt rifle fires flat: clamp the cone so
        // the tracer never climbs into the sky when the cursor sits up high
        Vector2 dir = GetGlobalMousePosition() - origin;
        if (dir.LengthSquared() < 4f || Mathf.Abs(dir.X) < 8f)
            dir = new Vector2(faceRight ? 1f : -1f, 0f);
        float elev = Mathf.Atan2(dir.Y, Mathf.Abs(dir.X));
        elev = Mathf.Clamp(elev, -0.26f, 0.16f);
        float sx = Mathf.Sign(dir.X);
        dir = new Vector2(sx * Mathf.Cos(elev), Mathf.Sin(elev));

        Vector2 end = origin + dir * 900f;
        if (target != null)
        {
            end = target.Position + new Vector2(0, -62);
            target.Hit();
            _fx.Puff(end, 9f);
        }
        // 枪声惊动 900px 内的所有敌人：没被打中的也会转头举枪反击
        foreach (var e in _enemies)
        {
            if (!IsInstanceValid(e) || e.Gone) continue;
            if (Mathf.Abs(e.Position.X - origin.X) < 900f)
                e.Alert();
        }
        _fx.Shoot(origin, end, new Color(1f, 0.92f, 0.70f), 120f);
    }

    /// <summary>敌兵阵亡：原地留下尸体 + 一支可捡的步枪（可能没子弹也可能冻坏）。</summary>
    public void EnemyDied(N918EnemySoldier e)
    {
        var corpse = new CorpseActor(CorpseActor.Faction.IJA,
            new Vector2(e.Position.X, GroundY - 2), false, withRifle: false);
        _game.AddChild(corpse);
        // the rifle he was carrying drops beside the body, lootable now
        var roll = GD.Randf() < 0.4f
            ? (WeaponPickup.Kind.Broken, 0)
            : (WeaponPickup.Kind.Usable, (int)GD.RandRange(2, 8));
        AddPickup(e.Position.X + (float)GD.RandRange(-70f, 70f), GroundY - 3, roll.Item1, roll.Item2);
        // 中弹处的血雾与雪尘
        _fx.Puff(e.Position + new Vector2(0, -62f), 14f);
        _fx.Puff(new Vector2(e.Position.X, GroundY - 3f), 10f);
        Shake(0.30f);
    }

    private void OnPlayerHit()
    {
        // v3.51: was 0.5 + full-screen flash 0.30 - read as a render fault.
        // 0.32 / 0.15 keeps the hurt feedback but below the glitch line.
        Shake(0.32f);
        _flashA = Mathf.Max(_flashA, 0.15f);
        _hud.Toast("你中弹了——蹲下能减少暴露");
    }

    private void OnDryFired()
    {
        if (_elapsed - _lastDryToast < 1.2f) return;
        _lastDryToast = _elapsed;
        _hud.Toast("弹仓空了——去尸体边捡枪，或者进屋搜弹药。");
    }

    /// <summary>
    /// v3.46: snow burst where the player lands. Impact speed (fall speed at
    /// touchdown) scales the puff - a hop kicks a whisper of powder, a leap
    /// off a rubble pile throws a real spray.
    /// </summary>
    private void OnPlayerLanded()
    {
        float vy = _player.Velocity.Y;
        if (vy < 90f) return;
        var puff = new SnowPuffFx(vy / 460f)
        {
            Position = new Vector2(_player.GlobalPosition.X, GroundY - 3f)
        };
        _game.AddChild(puff);
    }

    private void OnPlayerDown()
    {
        _blackTarget = 1f;
        _record = 0f;
        _hud.SetRecord(0, false);

        GetTree().CreateTimer(0.7f).Timeout += () =>
        {
            _player.ResetAt(_checkpoint);
            _blackTarget = 0f;
            _hud.Toast("枪声停了。你被人拖回掩体——记录还在。");
        };
    }

    public void EnemyFire(N918EnemySoldier e)
    {
        if (!PlayerAlive || _ended || !_started) return;

        var origin = e.Position + new Vector2(-46, -66);
        Vector2 to = _player.GlobalPosition + new Vector2(0, -38);
        bool crouching = Input.IsActionPressed("crouch") && _player.IsOnFloor();
        bool cover = PlayerInCover();
        // v3.54: sandbags stop the bullet, not the grenade - tucked in means
        // 0% bullet hits (was 0.02, read as "cover leaked"). Suppressing fire
        // still chews the wall, grenades still land, so cover stays consumable.
        float hitChance = cover ? 0f : crouching ? 0.10f : 0.28f;
        bool hit = GD.Randf() < hitChance;

        if (cover && _coverArt != null)
        {
            int si = _coverArt.StackAt(_player.GlobalPosition.X);
            if (si >= 0) _coverArt.Damage(si, 1.8f);
        }

        if (hit)
        {
            to = _player.GlobalPosition + new Vector2(0, -30);
            _player.TakeHit(e.Position.X);
        }
        else
        {
            to += new Vector2((float)GD.RandRange(-26f, 26f), (float)GD.RandRange(-34f, 6f));
            _fx.Puff(new Vector2(to.X, GroundY - 2), 7f);
        }
        _fx.Tracer(origin, to, new Color(1f, 0.80f, 0.52f));
        Shake(0.10f);
    }

    private void Artillery(float strength)
    {
        // v3.51: was full-strength flash + 0.7s+0.2 shake. The full-screen
        // warm wash at 0.4-0.65 alpha read as a screen fault every time a
        // wave spawned (user report 2026-10-31). Halved flash, softer jolt:
        // a distant shell you feel, not a screen you distrust.
        _flashA = Mathf.Max(_flashA, strength * 0.55f);
        Shake(0.45f * strength + 0.1f);
        // 远处的雪雾
        _snow.Burst();
    }

    public void Shake(float amount) => _shake = Mathf.Max(_shake, amount);

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("pause") && !_ended)
        {
            _main.ShowAsiaMap();
            GetViewport().SetInputAsHandled();
        }
    }

    // ================= 图层绘制 =================

    private partial class SkyScreen : CanvasLayer
    {
        public SkyScreen() { Layer = -20; }

        public SkyArt Art { get; private set; }

        public override void _Ready()
        {
            Art = new SkyArt();
            AddChild(Art);
        }

        /// <summary>
        /// 1931-09-18 沈阳近郊上空（41.8N 123.4E，初秋）。晴朗无风凉夜，
        /// 月已落、仅星空；无飘雪、无深冬色温。
        /// </summary>
        public partial class SkyArt : Control
        {
            private GradientTexture2D _glow;
            private GradientTexture2D _moonGlow;
            private float[] _starX;
            private float[] _starY;
            private float[] _starS;

            public float DayT = 5.6f;   // game clock, hours

            /// <summary>
            /// The sky has to be redrawn whenever the clock moves.
            /// Before this SkyArt only drew once on the first _Draw and never
            /// refreshed when DayT changed -
            /// so the sky stayed the colour of the opening moment (before dawn)
            /// for the whole match, the sun came up and the sky was still full
            /// of stars, and the day/night cycle did nothing.
            /// </summary>
            public override void _Process(double delta)
            {
                // v3.13: this used to redraw the sky every single frame. The
                // sky only depends on DayT, which creeps along at ClockRate, so
                // most frames painted the exact same pixels. Now it redraws
                // only when the clock actually moved (or when the window was
                // resized, which changes the band layout).
                float t = DayT;
                var vp = GetViewportRect().Size;
                if (_drawnT < 0f || Mathf.Abs(t - _drawnT) > 0.0015f || vp != _drawnSize)
                {
                    _drawnT = t;
                    _drawnSize = vp;
                    QueueRedraw();
                }
            }

            private float _drawnT = -1f;
            private Vector2 _drawnSize = new(-1f, -1f);

            private static readonly Vector4 NightTop = new(0.05f, 0.07f, 0.13f, 1f);
            private static readonly Vector4 PreDawnTop = new(0.07f, 0.10f, 0.18f, 1f);
            private static readonly Vector4 DawnTop = new(0.28f, 0.30f, 0.42f, 1f);
            private static readonly Vector4 DayTop = new(0.16f, 0.20f, 0.24f, 1f);
            private static readonly Vector4 DuskTop = new(0.30f, 0.28f, 0.38f, 1f);
            private static readonly Vector4 NightMid = new(0.10f, 0.13f, 0.20f, 1f);
            private static readonly Vector4 PreDawnMid = new(0.18f, 0.21f, 0.30f, 1f);
            private static readonly Vector4 DawnMid = new(0.65f, 0.55f, 0.50f, 1f);
            private static readonly Vector4 DayMid = new(0.44f, 0.52f, 0.56f, 1f);
            private static readonly Vector4 DuskMid = new(0.68f, 0.52f, 0.45f, 1f);
            private static readonly Vector4 NightLow = new(0.17f, 0.18f, 0.23f, 1f);
            private static readonly Vector4 PreDawnLow = new(0.36f, 0.34f, 0.36f, 1f);
            private static readonly Vector4 DawnLow = new(0.88f, 0.74f, 0.58f, 1f);
            private static readonly Vector4 DayLow = new(0.72f, 0.76f, 0.74f, 1f);
            private static readonly Vector4 DuskLow = new(0.90f, 0.68f, 0.50f, 1f);

            public override void _Ready()
            {
                SetAnchorsPreset(LayoutPreset.FullRect);
                MouseFilter = MouseFilterEnum.Ignore;

                // 冬日太阳的光晕：径向渐变，避免一圈一圈的硬边同心圆
                var g = new Gradient();
                g.SetColor(0, new Color(0.97f, 0.95f, 0.87f, 0.42f));
                g.SetColor(1, new Color(0.97f, 0.95f, 0.87f, 0f));
                _glow = new GradientTexture2D
                {
                    Gradient = g,
                    Width = 256,
                    Height = 256,
                    Fill = GradientTexture2D.FillEnum.Radial,
                    FillFrom = new Vector2(0.5f, 0.5f),
                    FillTo = new Vector2(1f, 0.5f),
                };

                var mg = new Gradient();
                mg.SetColor(0, new Color(0.82f, 0.85f, 0.92f, 0.30f));
                mg.SetColor(1, new Color(0.82f, 0.85f, 0.92f, 0f));
                _moonGlow = new GradientTexture2D
                {
                    Gradient = mg,
                    Width = 192,
                    Height = 192,
                    Fill = GradientTexture2D.FillEnum.Radial,
                    FillFrom = new Vector2(0.5f, 0.5f),
                    FillTo = new Vector2(1f, 0.5f),
                };

                // fixed star field, never twinkles position (no per-frame random)
                var rng = new RandomNumberGenerator();
                rng.Seed = 19311104;
                int n = 110;
                _starX = new float[n];
                _starY = new float[n];
                _starS = new float[n];
                for (int i = 0; i < n; i++)
                {
                    _starX[i] = rng.Randf();
                    _starY[i] = rng.Randf() * 0.62f;
                    _starS[i] = 0.8f + rng.Randf() * 1.4f;
                }
            }

            private static Color V(Vector4 v) => new(v.X, v.Y, v.Z);

            /// <summary>sky gradient keyframes over the game clock.</summary>
            private (Color top, Color mid, Color low) SkyColors(float t)
            {
                float sr = N918Level.SunriseH, ss = N918Level.SunsetH;
                Color top, mid, low;
                if (t < 5.0f) { top = V(NightTop); mid = V(NightMid); low = V(NightLow); }
                else if (t < sr - 0.7f)
                {
                    float k = (t - 5.0f) / (sr - 0.7f - 5.0f);
                    top = V(NightTop).Lerp(V(PreDawnTop), k); mid = V(NightMid).Lerp(V(PreDawnMid), k);
                    low = V(NightLow).Lerp(V(PreDawnLow), k);
                }
                else if (t < sr + 1.3f)
                {
                    float k = (t - (sr - 0.7f)) / 2.0f;
                    top = V(PreDawnTop).Lerp(V(DawnTop), k); mid = V(PreDawnMid).Lerp(V(DawnMid), k);
                    low = V(PreDawnLow).Lerp(V(DawnLow), k);
                }
                else if (t < sr + 2.6f)
                {
                    float k = (t - (sr + 1.3f)) / 1.3f;
                    top = V(DawnTop).Lerp(V(DayTop), k); mid = V(DawnMid).Lerp(V(DayMid), k);
                    low = V(DawnLow).Lerp(V(DayLow), k);
                }
                else if (t < ss - 0.9f) { top = V(DayTop); mid = V(DayMid); low = V(DayLow); }
                else if (t < ss + 0.6f)
                {
                    float k = (t - (ss - 0.9f)) / 1.5f;
                    top = V(DayTop).Lerp(V(DuskTop), k); mid = V(DayMid).Lerp(V(DuskMid), k);
                    low = V(DayLow).Lerp(V(DuskLow), k);
                }
                else if (t < ss + 2.2f)
                {
                    float k = (t - (ss + 0.6f)) / 1.6f;
                    top = V(DuskTop).Lerp(V(NightTop), k); mid = V(DuskMid).Lerp(V(NightMid), k);
                    low = V(DuskLow).Lerp(V(NightLow), k);
                }
                else { top = V(NightTop); mid = V(NightMid); low = V(NightLow); }
                return (top, mid, low);
            }

            public override void _Draw()
            {
                var s = GetViewportRect().Size;
                float t = DayT;
                float sr = N918Level.SunriseH, ss = N918Level.SunsetH;
                var (top, mid, low) = SkyColors(t);

                int bands = 30;
                for (int i = 0; i < bands; i++)
                {
                    float f = i / (float)bands;
                    Color c = f < 0.55f ? top.Lerp(mid, f / 0.55f) : mid.Lerp(low, (f - 0.55f) / 0.45f);
                    DrawRect(new Rect2(0, s.Y * f, s.X, s.Y / bands + 1.5f), c);
                }

                // ---- stars: only in the dark, moonset already gone by nightfall ----
                float nightA = 0f;
                if (t < sr + 0.4f) nightA = Mathf.Clamp(1f - (t - 5.8f) / (sr + 0.4f - 5.8f), 0f, 1f) * 0.9f;
                else if (t > ss + 1.2f) nightA = Mathf.Clamp((t - (ss + 1.2f)) / 1.2f, 0f, 1f) * 0.9f;
                if (nightA > 0.01f)
                {
                    for (int i = 0; i < _starX.Length; i++)
                        DrawCircle(new Vector2(_starX[i] * s.X, _starY[i] * s.Y),
                            _starS[i], new Color(0.92f, 0.94f, 1f, nightA * (0.35f + 0.5f * (i % 5) / 5f)));
                }

                // ---- moon: 初秋夜月已落，本关夜里不画月，仅星空 ----
                // (real ephemeris: 1931-09-18 沈阳月落早于夜半，夜间无月)
                if (t < sr + 1.1f)
                {
                    float fade = t < sr ? 1f : Mathf.Clamp(1f - (t - sr) / 1.1f, 0f, 1f);
                    var moon = new Vector2(s.X * 0.68f, 168f);
                    DrawTextureRect(_moonGlow, new Rect2(moon - new Vector2(96, 96), new Vector2(192, 192)), false);
                    float r = 21f;
                    DrawCircle(moon, r, new Color(0.87f, 0.89f, 0.93f, 0.95f * fade));
                    // terminator carve: lit limb faces east (right), shadow bites from the left
                    float d = (1f - 2f * N918Level.MoonLit) * r;
                    DrawCircle(moon + new Vector2(-d * 1.9f, 1.5f), r * 0.96f, top.Lerp(mid, 0.4f));
                }

                // ---- sun: low winter arc, rises right (east) sets left (west) ----
                if (t > sr - 0.5f && t < ss + 0.5f)
                {
                    float day = Mathf.Clamp((t - (sr - 0.5f)) / ((ss + 0.5f) - (sr - 0.5f)), 0f, 1f);
                    var sun = new Vector2(
                        Mathf.Lerp(s.X * 0.80f, s.X * 0.20f, day),
                        s.Y * 0.62f - Mathf.Sin(day * Mathf.Pi) * (s.Y * 0.62f - 110f));
                    DrawTextureRect(_glow, new Rect2(sun - new Vector2(150, 150), new Vector2(300, 300)), false);
                    DrawCircle(sun, 26f, new Color(0.97f, 0.95f, 0.88f, 0.55f));
                    DrawCircle(sun, 18f, new Color(0.98f, 0.96f, 0.90f, 0.35f));
                }

                // ---- clouds: low stratus, tinted by the sky itself ----
                for (int i = 0; i < 5; i++)
                {
                    float cy = 96 + i * 46 + Mathf.Sin(i * 2.1f) * 14f;
                    float cx = s.X * 0.5f + Mathf.Sin(i * 2.7f) * 300f;
                    float w = 86 + 26 * Mathf.Sin(i * 1.9f);
                    var cloud = mid.Lerp(low, 0.4f);
                    cloud.A = 0.10f;
                    for (int k = 0; k < 3; k++)
                    {
                        float off = (k - 1) * w * 0.55f;
                        DrawSetTransform(new Vector2(cx + off, cy + k * 5f), 0, new Vector2(2.4f, 0.72f));
                        DrawCircle(Vector2.Zero, w, new Color(cloud, 0.075f - k * 0.012f));
                    }
                }
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
        }
    }

    private partial class Backdrop : Node2D
    {
        public const int KindFar = 0, KindMid = 1, KindNear = 2, KindFore = 3, KindGround = 4;

        private readonly N918Level _lvl;
        public int Kind { get; }
        public float Factor { get; }

        public Backdrop(N918Level lvl, int kind, float factor, int z)
        {
            _lvl = lvl;
            Kind = kind;
            Factor = factor;
            ZIndex = z;
        }

        public override void _Process(double delta)
        {
            // 背景全部由固定种子驱动：内容每帧完全一致，玩家不动时画面一像素都不变。
            // 以前这里每帧 QueueRedraw + _windTime 风摆，树草会自己"进退"；
            // 现在静止时连重绘都不需要（节点平移时引擎自动重渲染），走路只有视差位移。
        }

        public override void _Draw()
        {
            switch (Kind)
            {
                case KindFar: DrawFar(); break;
                case KindMid: DrawMid(); break;
                case KindNear: DrawNear(); break;
                case KindGround: DrawGroundBand(); break;
                default: DrawFore(); break;
            }
        }

        // generated backdrop art, same watercolor manga style, one texture per layer
        private readonly System.Collections.Generic.Dictionary<string, Texture2D> _texCache = new();

        public override void _Ready()
        {
            // preload so first _Draw never hitches
            // v2.7: "willow" no longer preloads, the procedural willow clump replaced it
            foreach (var n in new[] { "mountains_autumn", "tree_elm_autumn", "tree_birch", "grass" })
                Tex(n);
            // v2.8: the distant village reuses the side-on house art
            foreach (var n in new[] { "village_autumn", "house_intact_near_autumn", "house_ruined_near_autumn",
                                      "house_skeleton_near_autumn", "house_burned_near_autumn", "house_posts_autumn" })
                VillageHouseTex(n, n == "village_autumn");
        }

        private Texture2D Tex(string name)
        {
            if (_texCache.TryGetValue(name, out var t)) return t;
            t = GD.Load<Texture2D>($"res://assets/art/backdrop/{name}.png");
            _texCache[name] = t;
            return t;
        }

        // ground-anchored sprite: baseCenter = bottom-middle, h = world height
        // fade: 0 = no fade, 1 = fully transparent at the bottom (kills the
        // hard cut line where a pasted texture meets the snow field)
        private void Sprite(string name, Vector2 baseCenter, float h, float alpha = 1f, bool flip = false,
                            float fade = 0f)
        {
            var t = Tex(name);
            if (t == null || h <= 0f) return;
            var sz = t.GetSize();
            float w = sz.X / sz.Y * h;
            var col = new Color(1f, 1f, 1f, alpha);
            if (flip)
            {
                DrawSetTransform(new Vector2(baseCenter.X, baseCenter.Y), 0, new Vector2(-1f, 1f));
                DrawTextureRect(t, new Rect2(-w * 0.5f, -h, w, h), false, col);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
            else
            {
                DrawTextureRect(t, new Rect2(baseCenter.X - w * 0.5f, baseCenter.Y - h, w, h), false, col);
            }

            // vertical fade: paint the snow colour back over the lower part of
            // the sprite so it melts into the snow field instead of showing
            // the texture's own hard bottom edge
            if (fade > 0f)
            {
                // v3.16: was a GradientTexture2D over a rectangle - the
                // rectangle's left and right edges stayed hard, so every tree
                // wore a straight-edged pale slab around its feet.
                DrawTextureRect(N918SoftSnow.MeltBand(new Color(0.62f, 0.55f, 0.37f), fade),
                    new Rect2(baseCenter.X - w * 0.5f, baseCenter.Y - h * fade * 1.6f, w, h * fade * 1.6f), false);
            }
        }

        // —— 远山：三层山脊，越远越淡（空气透视），山脊线用值噪声做出真锯齿 ——
        private static Rect2 Full(float x, float y, float h) => new(x, y, WorldW + 3800f, h);


        private void DrawFar()
        {
            // far range: generated watercolor panorama, mirror-tiled so seams match
            var mtn = Tex("mountains_autumn");
            if (mtn != null)
            {
                float h = 296f;
                float tileW = mtn.GetSize().X / mtn.GetSize().Y * h;
                float baseY = 570f;
                int idx = 0;
                for (float x = -1400f; x < WorldW + 2400f; x += tileW, idx++)
                {
                    if (idx % 2 == 1)
                    {
                        DrawSetTransform(new Vector2(x + tileW * 0.5f, baseY), 0, new Vector2(-1f, 1f));
                        DrawTextureRect(mtn, new Rect2(-tileW * 0.5f, -h, tileW, h), false);
                        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                    }
                    else
                    {
                        DrawTextureRect(mtn, new Rect2(x, baseY - h, tileW, h), false);
                    }
                }
            }
            else
            {
                // fallback: procedural ridges if the texture ever goes missing
                var far = new Color(0.60f, 0.66f, 0.70f);
                Ridge(448f, 104f, far, 1.3f, 0.0085f, 0.042f);
                Ridge(448f, 104f, new Color(0.80f, 0.84f, 0.84f, 0.20f), 1.3f, 0.0085f, 0.042f);
                var r1 = Ridge(492f, 76f, new Color(0.47f, 0.54f, 0.60f), 3.7f, 0.0110f, 0.058f);
                var r2 = Ridge(524f, 48f, new Color(0.35f, 0.42f, 0.48f), 6.1f, 0.0160f, 0.078f);
                RidgeSnow(r1, new Color(0.74f, 0.71f, 0.64f, 0.10f), lineW: 1.4f, lenMax: 1);
                RidgeSnow(r2, new Color(0.70f, 0.67f, 0.60f, 0.05f), lineW: 1.2f, lenMax: 1);
                CrestLine(r1, new Color(0.95f, 0.97f, 0.98f, 0.55f), 1.6f);
                CrestLine(r2, new Color(0.92f, 0.95f, 0.96f, 0.45f), 1.3f);
            }

            // 山脚雾：一条窄带，只把山脚和雪原接起来。
            // v3.6: the old haze colour (0.82,0.86,0.87) is BRIGHTER than the
            // snow field (0.775,...), and the panorama texture carries a near
            // pure-white strip along its bottom edge (rows 526-538 avg 232/255
            // vs 72 above). In day light all that read as a soft foot haze,
            // but the pre-dawn CanvasModulate multiplies everything down and
            // the band popped out as a hard white line across the whole
            // horizon. Now the haze uses the snow colour itself and stops
            // where the solid snow takes over, so no band can ever end up
            // brighter than the field it melts into.
            // v3.8: the texture's snow-covered foot (rows ~440-530, mean 215-237)
            // was STILL brighter than the plain (198) and popped as a thick
            // white band in daylight. Texture is darkened in place now; this
            // haze stays weak and long so it only softens, never bands.
            // v3.13: the foot is melted by MeltFoot() in the mid layer now.
            // This far haze is drawn with BandFade instead of one gradient
            // rect - a gradient rect puts a hard edge on its first row, and
            // across the whole screen that edge reads as a white line.
            BandFade(472f, 88f, new Color(0.62f, 0.55f, 0.37f, 0f),
                                new Color(0.62f, 0.55f, 0.37f, 0.24f));
        }

        private List<Vector2> Ridge(float baseY, float amp, Color color, float seed, float f1, float f2)
        {
            var pts = new List<Vector2>();
            // 画到画面之外很远：视差会把整层左右平移上千像素，
            // 只画「世界范围」就会在屏幕边缘露出一条竖直硬边。
            // 步长 16（原 26）让山脊轮廓更平滑，避免"锯齿/奇怪"的硬边。
            for (float x = -1400; x <= WorldW + 2400; x += 16)
            {
                float a = Ridged(x * f1 + seed, seed);
                float b = Ridged(x * f2 + seed * 3.1f, seed + 9f);
                float h = 0.26f + 0.74f * (a * 0.74f + b * 0.26f);
                pts.Add(new Vector2(x, baseY - amp * h));
            }
            var poly = new List<Vector2>(pts);
            poly.Add(new Vector2(pts[^1].X, 760));
            poly.Add(new Vector2(pts[0].X, 760));
            DrawColoredPolygon(poly.ToArray(), color);
            return pts;
        }

        private void RidgeSnow(List<Vector2> r, Color snow, float lineW = 1.4f, int lenMax = 3)
        {
            int drawn = 0;
            for (int i = 3; i < r.Count - 3; i++)
            {
                // 只在局部最高点（比前后3个点都高/Y更小）才画，避免整条脊线都挂雪
                bool isPeak = true;
                for (int k = -3; k <= 3; k++)
                {
                    if (k == 0) continue;
                    if (r[i].Y >= r[i + k].Y) { isPeak = false; break; }
                }
                if (!isPeak) continue;
                if (drawn++ >= lenMax * 14) break;  // 每脊最多画 lenMax*14 个山尖雪团
                var peak = r[i];
                // 圆簇雪冠：交叠圆 + 扁椭圆底座。多边形雪帽偶发三角化失败（自交）
                // 会让雪帽忽隐忽现 = 画面闪白；圆永远不会失败。
                float capA = Mathf.Clamp(snow.A * 4.5f, 0.12f, 0.55f);
                var soft = new Color(snow.R, snow.G, snow.B, capA * 0.65f);
                DrawSetTransform(peak + new Vector2(0f, 8f), 0, new Vector2(2.4f, 0.5f));
                DrawCircle(Vector2.Zero, 8f, soft);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                DrawCircle(peak + new Vector2(0f, -1f), 6.5f, new Color(snow.R, snow.G, snow.B, capA));
                DrawCircle(peak + new Vector2(-6f, 4f), 5f, soft);
                DrawCircle(peak + new Vector2(6f, 4f), 5f, soft);
            }
        }

        /// <summary>沿山脊顶部 40% 描一条淡白雪线，把散落的雪帽连成一片雪盖。</summary>
        private void CrestLine(List<Vector2> r, Color c, float w)
        {
            float minY = r[0].Y, maxY = r[0].Y;
            foreach (var p in r) { if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y; }
            float snowLine = minY + (maxY - minY) * 0.40f;
            var pts = new List<Vector2>();
            for (int i = 0; i < r.Count; i++)
                if (r[i].Y <= snowLine) pts.Add(r[i] + new Vector2(0, -2f));
            if (pts.Count > 1) DrawPolyline(pts.ToArray(), c, w, false);
        }

        private static float Hash1(int i, float seed)
        {
            float v = Mathf.Sin(i * 12.9898f + seed * 78.233f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        private static float Noise1(float x, float seed)
        {
            int i = Mathf.FloorToInt(x);
            float t = x - i;
            t = t * t * (3f - 2f * t);
            return Mathf.Lerp(Hash1(i, seed), Hash1(i + 1, seed), t);
        }

        /// <summary>把噪声折成尖峰（1-|n|），比正弦更像山脊。</summary>
        private static float Ridged(float t, float seed)
            => 1f - Mathf.Abs(Noise1(t, seed) * 2f - 1f);

        // 雪山下缘溶解：不用整块渐变矩形，改成很多细条、每条自己算 alpha。
        // 整块渐变贴图在顶端会留一条硬边，横贯整屏就是一条白线；
        // 细条没有边，只有渐变，所以山脚是"化进去"而不是"切下去"。
        private void MeltFoot(Color snow)
        {
            const int bands = 64;
            const float top = 430f;
            const float bandH = 2.25f;
            for (int i = 0; i < bands; i++)
            {
                float t = (i + 0.5f) / bands;
                float a = Mathf.SmoothStep(0f, 1f, t) * 0.97f;
                if (a <= 0.004f) continue;
                DrawRect(new Rect2(-1400f, top + i * bandH, WorldW + 3800f, bandH + 0.7f),
                    new Color(snow, a));
            }
        }

        // 全宽的渐变带：一样切成细条，每条自己算 alpha。
        // GradientTexture2D 铺出来的带子，第一行就是硬边 ——
        // 一条横贯 1280px 的硬边，看着就是"白线"。
        private void BandFade(float y, float h, Color from, Color to, int bands = 28)
        {
            float bh = h / bands;
            for (int i = 0; i < bands; i++)
            {
                float t = (i + 0.5f) / bands;
                DrawRect(new Rect2(-1400f, y + i * bh, WorldW + 3800f, bh + 0.7f),
                    from.Lerp(to, t));
            }
        }

        // —— 中景：雪原、村落、电报杆、树线、烟 ——
        private void DrawMid()
        {
            var snow = new Color(0.62f, 0.55f, 0.37f);

            // 雪原上缘：先渐变到实色，避免一条生硬的地平线。
            // v3.6: the gradient used to complete at y=578 while the mountain
            // texture already ends at 570 - its white bottom strip (563-570)
            // showed through the 74-86% fade as a thin bright line. Solid snow
            // now starts at 560 and the fade runs 500-560, so the strip is
            // buried under full-strength snow no matter the tint.
            // v3.8: fade widened to 468-560 so the mountain foot blends over
            // 90px instead of 60 — no leftover bright ledge above the plain.
            // v3.13: that single gradient rect is gone (see MeltFoot below) —
            // its top edge was the hard line across the screen.
            DrawRect(Full(-1400, 560, 258), snow);

            // 雪原起伏的亮边与投影
            // v2.9: tone it down. At 0.65 alpha this crest also ran as a
            // full-width white line behind the village row, stacking with the
            // ground one into two horizon stripes.
            var crest = new List<Vector2>();
            for (float x = -1400; x <= WorldW + 2400; x += 70)
                crest.Add(new Vector2(x, 574 + Mathf.Sin(x * 0.0031f) * 6f + Mathf.Sin(x * 0.011f) * 3f));
            DrawPolyline(crest.ToArray(), new Color(0.74f, 0.69f, 0.52f, 0.22f), 1.6f, true);
            // 只留一条极淡的投影带，别做成一排平行线（会像贴纸）
            // v3.13: 用细条画，两端都是 0，中间才到最深 ——
            // 整块渐变矩形的首行是硬边，横贯全屏又是一条线。
            for (int i = 0; i < 26; i++)
            {
                float t = i / 25f;
                float a = Mathf.Sin(t * Mathf.Pi) * 0.18f;
                DrawRect(new Rect2(-1400f, 578f + i, WorldW + 3800f, 1.8f),
                    new Color(0.52f, 0.49f, 0.36f, a));
            }

            // 风蚀纹：长而软的灰度条，让雪原不是一整片死白
            for (int i = 0; i < 52; i++)
            {
                float x = -1300 + i * 190 + Mathf.Sin(i * 2.6f) * 58f;
                float y = 592 + (i * 37 % 44);
                float w = 86 + (i * 53 % 120);
                DrawLine(new Vector2(x, y), new Vector2(x + w, y + Mathf.Sin(i * 1.7f) * 5f),
                    new Color(0.55f, 0.52f, 0.38f, 0.10f), 3f + (i % 3), true);
            }

            // Snow haze first, THEN the village.
            //
            // v2.8: the haze used to be painted after the houses, so that opaque
            // white band (y 520~578) swallowed most of every roof and the row
            // came out as a thin smear. Distance haze belongs behind the
            // village anyway — the fog is out on the plain, the houses stand
            // in front of it.
            //
            // v3.13: this used to be ONE rect carrying a vertical gradient
            // (468..560). The panorama still had dark ridge strokes right at
            // the top of that band, so the moment the overlay stepped in it
            // cut across the whole screen as one straight bright line right
            // under the snow range — the "white line" the player pointed at.
            // Now the foot is dissolved strip by strip: 64 thin bands, each
            // with its own alpha on a smoothstep. No edge anywhere, only a
            // gradient, and the range melts into the plain.
            MeltFoot(snow);

            // The distant village: war-torn farmhouses on the open snow.
            //
            // v2.8 rewrite. The old art was sliced out of village_ruined.png,
            // a sheet painted from a top-down three-quarter angle, so the pieces
            // never matched this side-on level and the village looked like
            // stickers glued onto the mountain. Now every house is the same
            // side-on art as the near houses: correct perspective, one drawing
            // style across the whole scene, and a real damage ladder (intact /
            // ruined / burnt frame / charred stumps only).
            foreach (var vh in VillageHouses)
            {
                var at = VillageHouseTex(vh.Tex, vh.Backdrop);
                if (at == null) continue;
                float wid = at.GetSize().X / at.GetSize().Y * vh.H;

                // Aerial perspective: the further off to the side, the more the
                // haze eats into the contrast. Deterministic, no random.
                float depth = 1f - Mathf.Clamp(Mathf.Abs(vh.X - 200f) / 5200f, 0f, 1f) * 0.18f;

                // Contact shadow first, so the house stands on the snow instead
                // of floating above it.
                DrawSetTransform(new Vector2(vh.X, VillageBaseY - 1f), 0, new Vector2(wid * 0.42f, 0.16f));
                DrawTextureRect(SoftBlob(new Color(0.42f, 0.47f, 0.52f), 0.22f),
                    new Rect2(-20f, -20f, 40f, 40f), false);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);

                if (vh.Flip)
                {
                    DrawSetTransform(new Vector2(vh.X, VillageBaseY), 0, new Vector2(-1f, 1f));
                    DrawTextureRect(at, new Rect2(-wid * 0.5f, -vh.H, wid, vh.H), false,
                        new Color(1f, 1f, 1f, depth));
                    DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                }
                else
                {
                    DrawTextureRect(at, new Rect2(vh.X - wid * 0.5f, VillageBaseY - vh.H, wid, vh.H),
                        false, new Color(1f, 1f, 1f, depth));
                }

                // Melt the foot of the house into the snow. A rectangular
                // gradient would leave a hard white box where the texture is
                // transparent, so the snow is laid on as a soft ellipse that
                // only touches the bottom of the building.
                // v3.16: the "rectangular gradient" was the white slab the
                // player kept pointing at - the rectangle's left and right
                // edges were ruler-straight. MeltBand tapers on every side.
                DrawTextureRect(N918SoftSnow.MeltBand(new Color(0.62f, 0.55f, 0.37f), 0.95f),
                    new Rect2(vh.X - wid * 0.5f, VillageBaseY - vh.H * 0.22f, wid, vh.H * 0.22f), false);
                DrawSetTransform(new Vector2(vh.X, VillageBaseY), 0, new Vector2(wid * 0.30f, 0.13f));
                DrawTextureRect(SoftBlob(new Color(0.80f, 0.82f, 0.83f), 0.80f),
                    new Rect2(-20f, -20f, 40f, 40f), false);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }

            // 关卡专属地标：九一八 = 北大营木瞭望塔，和江桥那关的土垒旗杆区分开来
            DrawMidLandmark();

            // mid tree line: generated birch/elm sprites, fixed seed layout
            var midTreeRng = new RandomNumberGenerator();
            midTreeRng.Seed = 19311104;
            float tx = -1400f;
            while (tx < WorldW + 1200)
            {
                float gap = 90f + midTreeRng.Randf() * 60f;
                float hBase = 30f + midTreeRng.Randf() * 42f;
                if (midTreeRng.Randf() < 0.30f)
                    Sprite("tree_birch", new Vector2(tx, 578f), hBase * 1.5f, 0.72f,
                        flip: midTreeRng.Randf() < 0.5f);
                else
                    Sprite("tree_elm_autumn", new Vector2(tx, 578f), hBase, 0.75f,
                        flip: midTreeRng.Randf() < 0.5f);
                tx += gap;
            }

            // 电报杆 + 电线（东北的“现代性”，也提示铁路方向）
            for (float x = -1344; x < WorldW + 2400; x += 336)
            {
                float tall = 156 + Mathf.Sin(x * 0.003f) * 16f;
                DrawLine(new Vector2(x, 580), new Vector2(x, 580 - tall), new Color(0.31f, 0.30f, 0.28f, 0.9f), 3f, true);
                DrawLine(new Vector2(x - 15, 580 - tall + 18), new Vector2(x + 15, 580 - tall + 18),
                    new Color(0.31f, 0.30f, 0.28f, 0.9f), 1.8f, true);
                DrawLine(new Vector2(x - 9, 580 - tall + 36), new Vector2(x + 9, 580 - tall + 36),
                    new Color(0.31f, 0.30f, 0.28f, 0.9f), 1.6f, true);
                var wire = Curved(new Vector2(x, 580 - tall + 20), new Vector2(x + 168, 580 - tall + 56),
                    new Vector2(x + 336, 580 - tall + 20), 12);
                DrawPolyline(wire, new Color(0.29f, 0.28f, 0.26f, 0.45f), 1.1f, true);
            }

            // 远处燃烧的村庄：薄烟，别做成泡泡
            foreach (float x in new[] { 860f, 2740f, 4520f })
                for (int i = 0; i < 4; i++)
                    DrawCircle(new Vector2(x + Mathf.Sin(i * 1.4f) * 14f, 500 - i * 34),
                        20f + i * 9f, new Color(0.30f, 0.29f, 0.28f, 0.055f));
        }

        /// <summary>九一八专属地标：远处一座木瞭望塔，点出“驻军营盘”的轮廓（与江桥的土垒旗杆区分）。</summary>
        private void DrawMidLandmark()
        {
            float bx = 1500f;
            float baseY = VillageBaseY;
            float topY = baseY - 150f;
            Color timber = new Color(0.32f, 0.29f, 0.26f, 0.95f);
            // 接触阴影，让塔站在雪上而不是飘着
            DrawSetTransform(new Vector2(bx, baseY - 1f), 0, new Vector2(0.42f, 0.16f));
            DrawTextureRect(SoftBlob(new Color(0.42f, 0.47f, 0.52f), 0.22f),
                new Rect2(-20f, -20f, 40f, 40f), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            // 斜撑（剪刀撑）+ 主柱
            DrawLine(new Vector2(bx - 10f, baseY), new Vector2(bx, topY + 26f), timber, 3f, true);
            DrawLine(new Vector2(bx + 10f, baseY), new Vector2(bx, topY + 26f), timber, 3f, true);
            DrawLine(new Vector2(bx - 6f, baseY - 30f), new Vector2(bx + 6f, baseY - 30f), timber, 2f, true);
            DrawLine(new Vector2(bx - 5f, baseY - 60f), new Vector2(bx + 5f, baseY - 60f), timber, 2f, true);
            DrawLine(new Vector2(bx, baseY), new Vector2(bx, topY + 22f), timber, 3.4f, true);
            // 顶部瞭望小屋 + 屋顶
            DrawRect(new Rect2(bx - 16f, topY - 2f, 32f, 24f), new Color(0.40f, 0.36f, 0.31f, 0.96f));
            DrawColoredPolygon(new[]
            {
                new Vector2(bx - 20f, topY - 2f),
                new Vector2(bx + 20f, topY - 2f),
                new Vector2(bx, topY - 20f),
            }, new Color(0.30f, 0.27f, 0.24f, 0.96f));
        }

        // —— 近景：枯树带、灌木柳丛、雪坎、铁丝网、废车（一律低于人物腰部，绝不遮脸）——
        private void DrawNear()
        {
            // 雪坎：软边（三层同心椭圆），不是一个个白泡泡
            // v3.48: this is all that is left in the 0.55 parallax layer.
            // The trees, willow clumps and wire fence used to live here too,
            // but they stand on the same ground line as the world-plane
            // houses and railway, so the 0.55 scroll made them slide across
            // doors and walls as the player walked. They moved to
            // DrawGroundBand (factor 1.0, world pinned) below.
            for (int i = 0; i < 22; i++)
            {
                var c = new Vector2(-1300 + i * 410, 626);
                Drift(c, 130f + (i % 3) * 34f, 30f + (i % 2) * 8f, new Color(0.60f, 0.54f, 0.37f));
            }
        }

        /// <summary>
        /// v3.48: the ground band - near trees, willow clumps, wire fence -
        /// drawn in the WORLD plane (factor 1.0, z -5: in front of the
        /// ground art / railway, behind the near houses). Everything here
        /// stands within a few pixels of the houses' ground line (663), so
        /// it has to share the houses' parallax or it visibly swims against
        /// them. Tree baselines moved 650/654 -> 660 so they stand just
        /// behind the house line, and trees skip house footprints (the same
        /// rule the fence stakes got in v3.46 - which only ever worked at
        /// one camera position while the band was still parallaxed).
        ///
        /// v3.50: 660 was still IN FRONT of the railway (rail top 654) and
        /// the loop ran to WorldW+1200, so elms stood on the ballast and
        /// grew on the bridge deck / frozen river (user screenshot
        /// 2026-10-31). Baseline moved back to 642 - behind the track -
        /// and the band now stops 240px short of the bridge approach: no
        /// tree grows on the deck, the wreck pile or the river.
        /// </summary>
        private void DrawGroundBand()
        {
            const float TreeLine = 642f;      // behind the rail top (654)
            const float BandEnd = GroundArt.BridgeDeckX - 240f;  // no trees at the bridge
            // near band: generated elm / willow sprites, fixed seed, sparse gaps
            var nearRng = new RandomNumberGenerator();
            nearRng.Seed = 19311105;
            float nx = -1400f;
            while (nx < BandEnd)
            {
                float gap = 280f + nearRng.Randf() * 140f;
                float kind = nearRng.Randf();
                bool blocked = InsideHouseFootprint(nx, 30f);
                if (!blocked)
                {
                    if (kind < 0.5f)
                        Sprite("tree_elm_autumn", new Vector2(nx, TreeLine), 60f + nearRng.Randf() * 60f, 1f,
                            flip: nearRng.Randf() < 0.5f);
                    else if (kind < 0.85f)
                    {
                        // the willow clump is procedural: one clump of arching twigs.
                        // before this it used the willow.png texture, but that image
                        // carries a whole white snow base, and scaled down to 26~48px
                        // it became a pile of black spikes radiating out of the
                        // ground, very jarring on open snow - that is the odd object
                        // the user pointed at in picture 7.
                        WillowClump(nx, TreeLine, 24f + nearRng.Randf() * 20f);
                    }
                }
                nx += gap;
            }
            // three taller lone trees to break the rhythm
            // v3.50: the third one stood at x=4900 - on the torn bridge deck
            // over the river. Moved to the open field west of the start.
            foreach (var (tx2, th) in new[] { (-820f, 138f), (2460f, 112f), (560f, 158f) })
            {
                if (InsideHouseFootprint(tx2, 30f)) continue;
                Sprite("tree_elm_autumn", new Vector2(tx2, TreeLine), th, 1f, flip: tx2 < 0f);
            }

            // 铁丝网：木桩 + 两道下垂铁丝（几处断口，不铺满整条路）
            // v3.18: the stake spacing was fixed and the run at 3820 dropped
            // three stakes through the sandbag wall at 4160 and two through
            // the wall at 4388. A stake standing inside a sandbag stack reads
            // as a bundle of rods jammed into the pile. Any stake that would
            // land inside a wall is skipped now, and the wire run stops at
            // the last valid stake so it never spans a gap either.
            // v3.46: the same rule now covers HOUSE footprints - three of the
            // four runs rode straight through a house base (880-1480 through
            // h1, 2540-3140 through h2, 3820-4360 through f3) and at close
            // range read as one black torn band across the lower wall, which
            // is exactly what the user circled. Stakes are gathered first so
            // a sagging wire only ever joins two adjacent surviving stakes;
            // the strands are also lightened (3 -> 2, alpha .6 -> .42) so the
            // run reads as wire, and each stake gets a snow mound at its foot
            // so it sits in the drift instead of poking out of nothing.
            foreach (float seg in new[] { -980f, 880f, 2540f, 3820f })
            for (float x0 = seg; x0 < seg + 600; x0 += 300)
            {
                var stakes = new List<float>();
                for (int p = 0; p < 6; p++)
                {
                    float x = x0 + p * 50;
                    // v3.18: no stake past the bridge head - the wire run at
                    // 3820 reached 4420, straight into the span.
                    if (x > 4360f) break;
                    if (InsideSandbag(x) || InsideHouseFootprint(x)) continue;
                    stakes.Add(x);
                }
                for (int s = 0; s < stakes.Count; s++)
                {
                    float x = stakes[s];
                    DrawLine(new Vector2(x, 668), new Vector2(x - 3, 616), new Color(0.30f, 0.28f, 0.26f), 2.2f, true);
                    DrawCircle(new Vector2(x - 3, 616), 2f, new Color(0.90f, 0.92f, 0.92f, 0.7f));
                    // snow kicked up around the stake foot: grounds the post
                    DrawSetTransform(new Vector2(x, 666f), 0f, new Vector2(0.7f, 0.32f));
                    DrawTextureRect(N918SoftSnow.Blob(new Color(0.55f, 0.50f, 0.36f), 0.7f),
                        new Rect2(-32f, -32f, 64f, 64f), false);
                    DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                    if (s + 1 < stakes.Count && stakes[s + 1] - x < 60f)
                        for (int w = 0; w < 2; w++)
                        {
                            float y = 626 + w * 18;
                            var wire = Curved(new Vector2(x, y), new Vector2(x + 25, y + 8), new Vector2(x + 50, y), 4);
                            DrawPolyline(wire, new Color(0.28f, 0.27f, 0.25f, 0.42f), 0.9f, true);
                        }
                }
            }


        }


        /// <summary>
        /// A snow drift: pale rim, brighter core, soft edge.
        ///
        /// v2.8: this used to be plain DrawCircle blobs. A circle has a hard
        /// rim, and on flat snow those rims showed up as bright white arcs
        /// lying on the ground — the "white lines flashing across the screen"
        /// the player kept reporting. Now the shape is a soft radial falloff,
        /// so the drift melts into the snow with no edge to catch the light.
        /// </summary>
        private void Drift(Vector2 c, float w, float h, Color snow)
        {
            DrawSetTransform(c + new Vector2(0, 4), 0, new Vector2(w / 20f, h / 20f * 1.4f));
            DrawTextureRect(SoftBlob(snow, 0.16f), new Rect2(-20f, -20f, 40f, 40f), false);
            DrawSetTransform(c, 0, new Vector2(w / 20f, h / 20f));
            DrawTextureRect(SoftBlob(snow, 0.55f), new Rect2(-20f, -20f, 40f, 40f), false);
            DrawSetTransform(c + new Vector2(-w * 0.12f, -h * 0.16f), 0, new Vector2(1.2f, 0.42f));
            DrawTextureRect(SoftBlob(new Color(0.98f, 0.99f, 0.99f), 0.5f), new Rect2(-20f, -20f, 40f, 40f), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

    /// <summary>Radial falloff sprite for soft snow shapes, cached per colour.</summary>
    private static ImageTexture SoftBlob(Color tint, float alpha)
        => N918SoftSnow.Blob(tint, alpha);

        /// <summary>
        /// 落叶枯树（史实：嫩江平原的蒙古黄榆/小叶杨，11 月阔叶树全部落叶只剩枝桠）：
        /// 主干和枝桠全部是贝塞尔曲线（真实树木没有直线——枝条先平伸再上翘、末梢下挂），
        /// 弯度/分枝/高度逐棵随机，绝无两棵相同，也绝无「天线/直线玩具」感。
        /// </summary>
        private void BareTree(float x, float baseY, float h, Color c, float w, float leanDeg = 0f)
        {
            // 静止立姿：不再做时间风摆（风摆=玩家不动时树自己摇 = "鬼畜"），
            // 只保留每棵固定的倾斜 leanDeg
            float sway = Mathf.DegToRad(leanDeg);
            DrawSetTransform(new Vector2(x, baseY), sway, Vector2.One);

            // 弯主干：二次贝塞尔平滑单弯（每棵弯向/弯度不同），分段变细画出锥形
            float bend = Mathf.Sin(x * 0.73f) * h * 0.17f;
            var trunk = Curved(new Vector2(0, 0),
                new Vector2(bend, -h * 0.55f),
                new Vector2(bend * 1.6f + Mathf.Sin(x * 1.9f) * h * 0.06f, -h), 8);
            int segs = trunk.Length - 1;
            for (int i = 0; i < segs; i++)
                DrawLine(trunk[i], trunk[i + 1], c, w * (1f - (i + 0.5f) / segs * 0.55f), true);
            var top = trunk[segs];

            // 枝桠：主干不同高度向左右交替伸出的弯曲枝（先平伸再上翘——真实阔叶树枝形）
            int branchCount = 3 + (int)(Mathf.Abs(Mathf.Sin(x * 0.37f + 1.3f)) * 2.9f);  // 3-5 根
            for (int b = 0; b < branchCount; b++)
            {
                float t = 0.40f + 0.56f * b / Mathf.Max(1f, branchCount - 1f);   // 0.40..0.96
                var anchor = PathPoint(trunk, t);
                float side = b % 2 == 0 ? 1f : -1f;
                float len = h * (0.36f - t * 0.18f) *
                    (0.75f + 0.5f * Mathf.Abs(Mathf.Sin(x + b * 2.7f)));
                float rise = len * (0.30f + 0.22f * Mathf.Abs(Mathf.Sin(x * 0.5f + b * 1.1f)));
                var tip = anchor + new Vector2(side * len, -rise);
                var ctrl = anchor + new Vector2(side * len * 0.55f, -rise * 0.15f); // 控制点低：先平后翘
                var branch = Curved(anchor, ctrl, tip, 6);
                int bs = branch.Length - 1;
                for (int i = 0; i < bs; i++)
                    DrawLine(branch[i], branch[i + 1], c,
                        Mathf.Max(1.1f, w * 0.38f * (1f - t * 0.4f) * (1f - i / (float)bs * 0.6f)), true);

                // 二级小枝：枝中段上挑一根 + 末梢下挂一根（枯枝的自然乱感，全是曲线）
                var mid = PathPoint(branch, 0.55f);
                var midBranch = Curved(mid, mid + new Vector2(side * len * 0.10f, -len * 0.04f),
                    mid + new Vector2(side * len * 0.22f, -len * 0.20f), 4);
                DrawPolyline(midBranch, c, Mathf.Max(0.9f, w * 0.16f), true);
                var droop = Curved(tip, tip + new Vector2(side * len * 0.08f, len * 0.02f),
                    tip + new Vector2(side * len * 0.14f, len * 0.16f), 4);
                DrawPolyline(droop, c, Mathf.Max(0.8f, w * 0.13f), true);
            }

            // 顶梢：2-3 根向上的细梢（高低错落，不是对称三叉）
            int sprouts = 2 + (int)(Mathf.Abs(Mathf.Sin(x * 2.1f)) * 1.9f);
            for (int s = 0; s < sprouts; s++)
            {
                float side = s % 2 == 0 ? 1f : -1f;
                float sl = h * (0.10f + 0.05f * Mathf.Abs(Mathf.Sin(x + s * 1.7f)));
                var sprout = Curved(top, top + new Vector2(side * sl * 0.2f, -sl * 0.5f),
                    top + new Vector2(side * sl * (0.5f + 0.4f * Mathf.Sin(x * 0.9f + s)), -sl), 4);
                DrawPolyline(sprout, c, Mathf.Max(0.9f, w * 0.18f), true);
            }
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        /// <summary>折线路径上取 t∈[0,1] 处的点（按段长近似，用于在曲线干/枝上长出分枝）。</summary>
        private static Vector2 PathPoint(Vector2[] pts, float t)
        {
            float total = 0f;
            var segLens = new float[pts.Length - 1];
            for (int i = 0; i < pts.Length - 1; i++)
            {
                segLens[i] = pts[i].DistanceTo(pts[i + 1]);
                total += segLens[i];
            }
            float d = Mathf.Clamp(t, 0f, 1f) * total;
            for (int i = 0; i < segLens.Length; i++)
            {
                if (d <= segLens[i] || i == segLens.Length - 1)
                    return pts[i].Lerp(pts[i + 1], segLens[i] <= 0f ? 0f : d / segLens[i]);
                d -= segLens[i];
            }
            return pts[pts.Length - 1];
        }

    /// <summary>
    /// Shrub willow (historically: sallow grows in clumps along the river
    /// hollows): a low clump of arching thin twigs plus snow at the roots.
    /// </summary>
    /// <remarks>
    /// v2.7 rewrite: before this all 7 branches shot out of the same point
    /// (0,0), which drew a bunch of black spikes radiating from the centre.
    /// On open snow that was very jarring - it is the odd object the user
    /// pointed at in picture 7.
    /// Now each branch grows from its own spot inside a small patch at the
    /// base, arches up first and then droops down, each branch going from
    /// thick to thin, with a layer of snow on top. It is a clump of willow,
    /// not a pile of spikes.
    /// </remarks>
    private void WillowClump(float x, float baseY, float h)
    {
        var c = new Color(0.31f, 0.28f, 0.22f);
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)(int)(x * 7919f) + 19311105UL;

        // a small patch at the base, each branch grows from a different spot
        // (no more squeezing them all into one point)
        int stems = 6;
        for (int b = 0; b < stems; b++)
        {
            float t = b / (stems - 1f) - 0.5f;          // -0.5 .. 0.5
            float rootX = t * h * 0.62f;
            float spread = t * h * 1.35f;               // spread to the sides
            float rise = h * (0.62f + 0.38f * Mathf.Cos(t * 2.2f));  // tall in the middle, short at the edges
            // arch up first, then droop outwards (sallow branches hang down like this)
            var arch = Curved(new Vector2(rootX, 0f),
                new Vector2(rootX + spread * 0.25f, -rise * 1.12f),
                new Vector2(rootX + spread * 0.82f, -rise * 0.42f), 6);
            DrawPolyline(arch, c, 1.6f, true);

            // the tip droops one more stretch, thinnest at the end
            var tail = Curved(arch[^1],
                arch[^1] + new Vector2(spread * 0.10f, rise * 0.16f),
                arch[^1] + new Vector2(spread * 0.16f, rise * 0.52f), 4);
            DrawPolyline(tail, new Color(c.R, c.G, c.B, 0.85f), 1.1f, true);
        }

        // snow on the branches: a small pinch on the back of a few upward branches only
        for (int s = 0; s < 3; s++)
        {
            float sx = (rng.Randf() - 0.5f) * h * 0.9f;
            float sy = -h * (0.42f + rng.Randf() * 0.30f);
            DrawSetTransform(new Vector2(x + sx, baseY + sy), 0, new Vector2(0.9f, 0.26f));
            DrawTextureRect(N918SoftSnow.Blob(new Color(0.56f, 0.51f, 0.37f), 0.62f),
                new Rect2(-20f, -20f, 40f, 40f), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        // snow at the base: a soft edged ellipse that buries the roots
        DrawSetTransform(new Vector2(x, baseY + 1f), 0, new Vector2(1.5f, 0.26f));
        DrawTextureRect(N918SoftSnow.Blob(new Color(0.56f, 0.51f, 0.37f), 0.78f),
            new Rect2(-20f, -20f, 40f, 40f), false);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

        // —— 前景：贴镜头掠过的芦苇与雪檐（加深景深）——
        private void DrawFore()
        {
            // 雪檐：近处地面微微抬起的坡，边缘有雪堆
            var crest = new List<Vector2>();
            for (float x = -1400; x <= 7600; x += 120)
                crest.Add(new Vector2(x, 722 + Mathf.Sin(x * 0.006f) * 9f + Mathf.Sin(x * 0.021f) * 4f));
            var bank = new List<Vector2>(crest);
            bank.Add(new Vector2(7600, 900));
            bank.Add(new Vector2(-1400, 900));
            DrawColoredPolygon(bank.ToArray(), new Color(0.88f, 0.90f, 0.91f));
            DrawPolyline(crest.ToArray(), new Color(0.97f, 0.98f, 0.98f, 0.8f), 4f, true);
            DrawPolyline(crest.Select(p => p + new Vector2(0, 10)).ToArray(),
                new Color(0.70f, 0.75f, 0.78f, 0.35f), 8f, true);

            // snow grass: generated tuft clusters, tips only above the snow line
            var reedRng = new RandomNumberGenerator();
            reedRng.Seed = 19311106;
            for (int i = 0; i < 30; i++)
            {
                float x = -1350 + i * 340 + Mathf.Sin(i * 2.3f) * 82f + reedRng.RandfRange(-30f, 30f);
                float baseY = 722 + Mathf.Sin(x * 0.006f) * 9f;
                Sprite("grass", new Vector2(x, baseY + 3f), 24f + reedRng.Randf() * 14f, 0.95f,
                    flip: reedRng.Randf() < 0.5f);
                if (i % 2 == 0)
                    Sprite("grass", new Vector2(x + 70f, baseY + 4f), 16f + reedRng.Randf() * 10f, 0.85f,
                        flip: reedRng.Randf() >= 0.5f);
            }
        }

        /// <summary>
        /// 中景阔叶枯树：弯曲主干 + 先平后翘的弧形枝。雪只以「短雪帽」形式
        /// 贴在部分枝条的**上侧**（雪落在枝背上），不再是沿枝平行的白线
        /// ——那种白线远看像没掉光的叶子，不符合 11 月全落叶的史实。
        /// </summary>
        private void Tree(float x, float baseY, float h, Color c, float w)
        {
            DrawSetTransform(new Vector2(x, baseY), 0, Vector2.One);

            // 弯主干：贝塞尔曲线 + 分段变细
            float bend = Mathf.Sin(x * 0.61f + 0.8f) * h * 0.15f;
            var trunk = Curved(new Vector2(0, 0), new Vector2(bend, -h * 0.55f),
                new Vector2(bend * 1.5f, -h), 7);
            int segs = trunk.Length - 1;
            for (int i = 0; i < segs; i++)
                DrawLine(trunk[i], trunk[i + 1], c, w * (1f - (i + 0.5f) / segs * 0.5f), true);
            var top = trunk[segs];

            var snow = new Color(0.62f, 0.55f, 0.37f, 0.8f);
            int branchCount = 3 + (int)(Mathf.Abs(Mathf.Sin(x * 0.53f)) * 2.4f);  // 3-5 根
            for (int b = 0; b < branchCount; b++)
            {
                float t = 0.38f + 0.58f * b / Mathf.Max(1f, branchCount - 1f);
                var anchor = PathPoint(trunk, t);
                float side = b % 2 == 0 ? 1f : -1f;
                float len = h * (0.30f - t * 0.12f) *
                    (0.8f + 0.4f * Mathf.Abs(Mathf.Sin(x + b * 2.1f)));
                float rise = len * (0.30f + 0.15f * Mathf.Abs(Mathf.Sin(x * 0.7f + b)));
                var tip = anchor + new Vector2(side * len, -rise);
                var ctrl = anchor + new Vector2(side * len * 0.55f, -rise * 0.12f);
                var branch = Curved(anchor, ctrl, tip, 5);
                int bs = branch.Length - 1;
                float bw = Mathf.Max(1.2f, w * 0.5f * (1f - t * 0.35f));
                for (int i = 0; i < bs; i++)
                    DrawLine(branch[i], branch[i + 1], c, bw * (1f - i / (float)bs * 0.5f), true);

                // 雪帽：隔一根枝给一小截，贴着枝线上侧（雪真的「落」在枝上）
                if (b % 2 == 0)
                    for (int i = 0; i < 2 && i < bs; i++)
                        DrawLine(branch[i] + new Vector2(0, -bw * 0.35f),
                            branch[i + 1] + new Vector2(0, -bw * 0.35f), snow, bw * 0.9f, true);

                // 末梢小枝上挑
                var tw = Curved(tip, tip + new Vector2(side * len * 0.10f, -len * 0.06f),
                    tip + new Vector2(side * len * 0.20f, -len * 0.16f), 3);
                DrawPolyline(tw, c, Mathf.Max(0.9f, w * 0.2f), true);
            }

            // 顶梢
            var topS = Curved(top, top + new Vector2(Mathf.Sin(x) * 3f, -h * 0.06f),
                top + new Vector2(Mathf.Sin(x * 1.3f) * 6f, -h * 0.12f), 4);
            DrawPolyline(topS, c, Mathf.Max(1f, w * 0.25f), true);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        /// <summary>白桦：白细干 + 黑斑纹 + 稀疏枯枝（冬天落叶后只剩细枝）。</summary>
        private void Birch(float x, float baseY, float h, float alpha)
        {
            // 白桦不做风摆（静止树），保持固定姿态
            var trunk = new Color(0.82f, 0.84f, 0.84f, alpha);
            var dark = new Color(0.22f, 0.22f, 0.22f, alpha * 0.85f);

            // 白色细树干（顶端略尖，下方略粗）
            DrawLine(new Vector2(x, baseY), new Vector2(x + 2, baseY - h), trunk, 2.8f, true);
            // 树干上的横纹黑斑（白桦标志性特征）
            for (int k = 0; k < 5; k++)
            {
                float ty = baseY - h * (0.18f + k * 0.15f);
                float bw = 4f + Mathf.Sin(k * 1.7f) * 2f;
                DrawSetTransform(new Vector2(x, ty), 0, Vector2.One);
                DrawRect(new Rect2(-bw / 2f, -1.2f, bw, 2.4f), dark);
            }
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);

            // 稀疏枯枝（冬天白桦只剩细枝）——细枝同样是微弯曲线，不是直线
            for (int i = 0; i < 5; i++)
            {
                float t = 0.30f + i * 0.13f;
                float y = baseY - h * t;
                float dir = i % 2 == 0 ? 1f : -1f;
                float len = h * (0.18f - i * 0.018f);
                DrawSetTransform(new Vector2(x + 1, y), 0, Vector2.One);
                var twig = Curved(new Vector2(0, 0), new Vector2(dir * len * 0.5f, -len * 0.18f),
                    new Vector2(dir * len, -len * 0.7f), 4);
                DrawPolyline(twig, dark, 1.6f, true);
                // 小枝的末梢分叉（继续上挑）
                if (i % 2 == 0)
                {
                    var fork = Curved(new Vector2(dir * len, -len * 0.7f),
                        new Vector2(dir * len * 1.05f, -len * 0.88f),
                        new Vector2(dir * len * 1.2f, -len * 1.08f), 3);
                    DrawPolyline(fork, dark, 1f, true);
                }
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
            // 顶端一撮细枝（最尖处）
            DrawSetTransform(new Vector2(x + 2, baseY - h), 0, Vector2.One);
            DrawLine(new Vector2(0, 0), new Vector2(-4, -10), dark, 1.4f, true);
            DrawLine(new Vector2(0, 0), new Vector2(5, -8), dark, 1.4f, true);
            DrawLine(new Vector2(0, 0), new Vector2(-2, -14), dark, 1.4f, true);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        private static Vector2[] Curved(Vector2 a, Vector2 control, Vector2 b, int steps)
        {
            var pts = new Vector2[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                pts[i] = a.Lerp(control, t).Lerp(control.Lerp(b, t), t);
            }
            return pts;
        }

        /// <summary>
        /// True when x falls inside one of the sandbag walls, so the barbed
        /// wire stakes can be kept out of the piles. v3.18: the stakes used to
        /// be dropped on a fixed grid and three of them stood inside the wall
        /// at 4160 while two more stood inside the wall at 4388, which read
        /// as rods jammed into the sandbags.
        /// </summary>
        private static bool InsideSandbag(float x)
        {
            foreach (float bx in CoverArt.SandbagBaseX)
                if (Mathf.Abs(x - bx) < CoverArt.SandbagHalfWidth) return true;
            return false;
        }
    }

    /// <summary>贴在地面的角色影子：随腾空高度缩小、变淡，给跳跃一个重量参照。</summary>
    private partial class GroundShadow : Node2D
    {
        public float X;
        public float Air;

        public override void _Draw()
        {
            float k = 1f / (1f + Air / 240f);
            float a = 0.30f / (1f + Air / 110f);
            // same kit tone + sun direction as everything else on the ground
            float dir = SnowShadow.DirX();
            float skew = (3f + Air * 0.045f) * (dir == 0f ? -0.6f : dir);
            // v2.8: radial falloff, a hard rimmed circle leaves a visible arc
            DrawSetTransform(new Vector2(X - skew, GroundY - 1f), 0, new Vector2(1f, 0.24f));
            DrawTextureRect(N918SoftSnow.Blob(new Color(0.12f, 0.13f, 0.16f), a),
                new Rect2(-20f, -20f, 40f, 40f), false);
            DrawSetTransform(new Vector2(X - skew * 2.1f, GroundY - 1f), 0, new Vector2(1f, 0.15f));
            DrawTextureRect(N918SoftSnow.Blob(new Color(0.12f, 0.13f, 0.16f), a * 0.5f),
                new Rect2(-20f, -20f, 40f, 40f), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }

    /// <summary>地面、铁路、弹坑、江桥：全部画在玩法层之下，但按前后顺序叠。</summary>
    private partial class GroundArt : Node2D
    {
        private readonly N918Level _l;

        // —— 预计算的随机参数（全部在 _Ready 生成一次，_Draw 不做 Randf）——
        private float[] _craterX = System.Array.Empty<float>();
        private float[] _craterR = System.Array.Empty<float>();
        private float[] _crateX = System.Array.Empty<float>();     // 弹药木箱：位置/高矮/歪斜每只不同
        private float[] _crateH = System.Array.Empty<float>();
        private float[] _crateRot = System.Array.Empty<float>();
        private int[] _railSkipIdx = System.Array.Empty<int>();       // 被去掉的枕木序号（破损感）
        private float[] _railDmgX = System.Array.Empty<float>();      // shell damage spots along the whole rail line

        /// <summary>
        /// Where the span stands in world coordinates, measured off
        /// bridge.png (1530x812, drawn with a vertical scale of 420/812 and
        /// shifted 48px east): the deck starts at source column 9 and the
        /// collapsed span begins at source column 870.
        ///
        /// v3.25: the railway needs both numbers. The track has to run
        /// onto the deck and stop at the wreck - otherwise the line is cut
        /// off in front of the bridge and the two read as two pictures
        /// laid side by side with nothing joining them.
        /// </summary>
        // v3.50: internal (was private) - the Backdrop ground band also needs
        // the deck position to keep trees off the bridge and the river.
        internal const float BridgeDeckX = 4457f;
        private const float BridgeWreckX = 4902f;
        private int[] _trussSkipDiags = System.Array.Empty<int>();    // 桥桁架被去掉的斜腹杆
        private int[] _pierDamageIdx = System.Array.Empty<int>();     // 被炮弹蹭过的桥墩（画烧黑斑）

        public GroundArt(N918Level l) { _l = l; }

        private Texture2D _crateTex;
        private Texture2D CrateTex()
        {
            if (_crateTex == null)
                _crateTex = GD.Load<Texture2D>("res://assets/art/props/crate.png");
            return _crateTex;
        }

        public override void _Ready()
        {
            var rng = new RandomNumberGenerator();
            rng.Randomize();

            // 弹坑位置 ±20、半径 ±6。
            // anchors picked so the rim never kisses a sandbag wall, bunker or
            // ammo crate - a shell hole right against the cover reads wrong.
            // v3.8: dropped 2080/2890/3260 — all three sat inside near-house
            // footprints (h3/h2/h4) and would vanish behind them now that the
            // houses draw in front of the ground art. 2540/3780 stay to break
            // the rails where they are visible.
            // v3.11: dropped 2290 as well — its rim sat inside the sandbag
            // stack at 2320, so the stack hid half the hole. 1640 moved to
            // 1760: the old rim ran under bunker 1550's right base.
            var craterListX = new List<float>();
            var craterListR = new List<float>();
            foreach (float x in new[] { 1260f, 1760f, 2540f, 3780f })
            {
                craterListX.Add(x + rng.RandfRange(-22f, 22f));
                craterListR.Add(26f + rng.RandfRange(-6f, 15f));
            }
            _craterX = craterListX.ToArray();
            _craterR = craterListR.ToArray();

            // 弹药木箱：错开沙包垛位摆放，每只高矮/歪斜略不同（不再像贴图一样复制叠上去）
            // v3.54: 730 sat on top of testimony t1 (720), 3960 stood inside
            // the f3 ruin wall (3910..4241). 790 clears t1, 3855 threads
            // between the 3780 crater rim and f3's west edge.
            var crateListX = new List<float>();
            var crateListH = new List<float>();
            var crateListRot = new List<float>();
            foreach (float x in new[] { 790f, 1450f, 2680f, 3855f })
            {
                crateListX.Add(x + rng.RandfRange(-18f, 18f));
                crateListH.Add(30f + rng.RandfRange(-3f, 4f));
                crateListRot.Add(rng.RandfRange(-0.10f, 0.10f));
            }
            _crateX = crateListX.ToArray();
            _crateH = crateListH.ToArray();
            _crateRot = crateListRot.ToArray();

            // 铁路：随机挑约 30 根枕木去掉（画得到的就是 84 根，三成缺失 = 战时打烂的样子）
            var railSkip = new HashSet<int>();
            int railCount = (int)((WorldW - 2400f) / 34f) + 2;
            for (int k = 0; k < 30; k++) railSkip.Add((int)rng.RandfRange(0, railCount));
            _railSkipIdx = railSkip.ToArray();

            // shell damage strung out along the whole run of track, not only
            // where the big craters sit - shelling never lands in one spot
            // v3.25: it stops before the bridge. The track on the span has
            // no sleepers to twist, and the twisted stubs would have been
            // painted over the deck.
            var railDmg = new List<float>();
            float dmgX = 2620f;
            while (dmgX < BridgeDeckX - 90f)
            {
                bool nearCrater = false;
                for (int i = 0; i < _craterX.Length; i++)
                    if (Mathf.Abs(_craterX[i] - dmgX) < 110f) nearCrater = true;
                if (!nearCrater) railDmg.Add(dmgX);
                dmgX += rng.RandfRange(260f, 430f);
            }
            _railDmgX = railDmg.ToArray();

            // 桥桁架：随机去掉 2~4 根斜腹杆
            var diagSkip = new HashSet<int>();
            for (int k = 0; k < 3 + (int)rng.Randf() * 2; k++) diagSkip.Add((int)rng.RandfRange(0, 6));
            _trussSkipDiags = diagSkip.ToArray();

            // 桥墩：随机挑 1 个画烧黑斑
            var pierList = new HashSet<int>();
            pierList.Add((int)rng.RandfRange(0, 4));
            _pierDamageIdx = pierList.ToArray();
        }

        public override void _Draw()
        {
            // —— 雪地本体 ——
            var surface = new List<Vector2>();
            for (float x = -400; x <= WorldW + 600; x += 44)
                surface.Add(new Vector2(x, GroundY - Mathf.Sin(x * 0.008f) * 2.2f - Mathf.Sin(x * 0.031f) * 1.1f));
            var ground = new List<Vector2>(surface);
            ground.Add(new Vector2(WorldW + 600, 900));
            ground.Add(new Vector2(-400, 900));
            DrawColoredPolygon(ground.ToArray(), new Color(0.66f, 0.59f, 0.40f));
            // v2.9: the old edge highlight was near-white at 0.9 alpha, so a
            // crisp white line ran the full width of the level straight behind
            // every character. Soften it into the snow fill: same line, but a
            // tone close to the surface and low alpha, just enough to suggest
            // a wind-packed crest instead of drawing a horizon stripe.
            DrawPolyline(surface.ToArray(), new Color(0.80f, 0.72f, 0.50f, 0.30f), 2f, true);
            DrawPolyline(surface.Select(p => p + new Vector2(0, 8)).ToArray(),
                new Color(0.58f, 0.54f, 0.38f, 0.26f), 5f, true);

            // 草地细节：枯草丛 / 小石块 / 泥土斑 / 野草，破掉“一片纯色无生机”的枯燥感
            DrawGroundDetailing();

            // 冰面裂缝与车辙
            for (int i = 0; i < 26; i++)
            {
                float x = 60 + i * 190;
                float y = GroundY + 26 + (i % 3) * 12;
                DrawLine(new Vector2(x, y), new Vector2(x + 70, y + 8), new Color(0.72f, 0.78f, 0.82f, 0.35f), 1.4f, true);
            }

            DrawRiverIce();
                // v3.25: the span is laid down BEFORE the track now. It used to
                // be painted over the rails, which cut the line dead in front
                // of the bridge - the user called it "the bridge and the rails
                // do not connect, a texture just stuck on top with nothing
                // linking them". With the bridge down first the railway can
                // run onto the deck and stop where the span is blown apart.
                DrawBridge();
                // the snow band that swallows the foot of the piers. Every
                // tree and every house in this level already gets one, and
                // the bridge without it is the one object standing in the
                // scene with a hard cut at its base - that is the "pasted
                // on" the player kept seeing.
                DrawRailway();
            DrawSupplyCart();

            DrawCraters();
            DrawRailBlastDamage();
            DrawTrenchLines();
        }

        /// <summary>
        /// v3.55 初秋野地细节：在地面之上撒入枯草丛、小石块、泥土斑与野草，
        /// 破掉“一片纯色、没有任何生动”的枯燥感（用户 Phase C 第 1 条）。
        /// 固定随机种子 → 每帧重画结果完全一致，不会抖。避开房屋脚、不画到
        /// 桥前的江面。颜色走枯黄/赭石/灰石，和地表的土黄调子叠出层次。
        /// </summary>
        private void DrawGroundDetailing()
        {
            var rng = new RandomNumberGenerator();
            rng.Seed = 19310918;   // 与树林带 19311105 区分，免得草和树同相
            const float EndX = BridgeDeckX - 240f;   // 桥前是河面/桥，不画草

            float x = -400f;
            while (x < EndX)
            {
                if (InsideHouseFootprint(x, 22f)) { x += 42f; continue; }

                // 地表起伏和 _Draw 里的 surface 同一条正弦曲线，草才站得住
                float sy = GroundY - Mathf.Sin(x * 0.008f) * 2.2f - Mathf.Sin(x * 0.031f) * 1.1f;

                float pick = rng.Randf();
                if (pick < 0.46f)
                {
                    // 枯草丛：3~5 根向外弯的细线
                    int blades = 3 + (int)(rng.Randf() * 3f);
                    float h = 7f + rng.Randf() * 9f;
                    Color tuft = new Color(0.52f + rng.Randf() * 0.06f,
                                          0.46f + rng.Randf() * 0.06f,
                                          0.30f + rng.Randf() * 0.05f, 0.85f);
                    for (int b = 0; b < blades; b++)
                    {
                        float dx = (b - (blades - 1) * 0.5f) * (2.2f + rng.Randf() * 1.6f);
                        float lean = dx * (0.5f + rng.Randf() * 0.45f);
                        var g = new List<Vector2>
                        {
                            new Vector2(x + dx, sy),
                            new Vector2(x + dx * 0.6f + lean * 0.4f, sy - h * 0.55f),
                            new Vector2(x + dx * 0.2f + lean * 0.9f, sy - h),
                        };
                        DrawPolyline(g.ToArray(), tuft, 1.1f, false);
                    }
                }
                else if (pick < 0.66f)
                {
                    // 小石块：扁椭圆 + 一点贴地暗影
                    float rw = 4f + rng.Randf() * 6f;
                    float rh = rw * (0.45f + rng.Randf() * 0.22f);
                    var rock = new List<Vector2>();
                    int seg = 10;
                    for (int s = 0; s <= seg; s++)
                    {
                        float a = Mathf.Tau * s / seg;
                        float r = 1f + Mathf.Sin(a * 2f) * 0.12f;
                        rock.Add(new Vector2(x + Mathf.Cos(a) * rw * r,
                                              sy - rh + Mathf.Sin(a) * rh * r));
                    }
                    DrawColoredPolygon(rock.ToArray(), new Color(0.54f, 0.53f, 0.55f, 0.9f));
                    DrawLine(new Vector2(x - rw, sy), new Vector2(x + rw, sy),
                        new Color(0.34f, 0.32f, 0.30f, 0.5f), 1.6f, true);
                }
                else if (pick < 0.86f)
                {
                    // 泥土斑：贴地浅色土斑，柔化“纯色地表”
                    float pw = 16f + rng.Randf() * 30f;
                    var patch = new List<Vector2>();
                    int seg = 12;
                    for (int s = 0; s <= seg; s++)
                    {
                        float t = (float)s / seg;
                        float px = x - pw * 0.5f + t * pw;
                        float py = sy + 3f + Mathf.Sin(t * Mathf.Tau) * (3f + rng.Randf() * 3f);
                        patch.Add(new Vector2(px, py));
                    }
                    DrawColoredPolygon(patch.ToArray(), new Color(0.50f, 0.44f, 0.30f, 0.30f));
                }
                else
                {
                    // 野草：单根带穗的细草，微微歪
                    float h = 10f + rng.Randf() * 12f;
                    float lean = rng.RandfRange(-5f, 5f);
                    Color weed = new Color(0.46f, 0.50f, 0.30f, 0.8f);
                    DrawLine(new Vector2(x, sy), new Vector2(x + lean, sy - h),
                        new Color(weed.R, weed.G, weed.B, weed.A), 1.0f, false);
                    DrawLine(new Vector2(x + lean, sy - h),
                        new Vector2(x + lean + 2.5f, sy - h - 4f),
                        new Color(weed.R + 0.1f, weed.G + 0.08f, weed.B, weed.A), 1.0f, false);
                }

                x += 34f + rng.Randf() * 40f;
            }
        }

        private void DrawRiverIce()
        {
            // 桥下的江面（冰封）
            //
            // v3.25: this used to be a plain rectangle - four hard corners, a
            // ruler-straight near edge running the whole width of the screen
            // and a vertical cut at the west end. It read as a grey slab
            // lying on the snow, not as a frozen river, and the bridge
            // piers stood on top of it like blocks set on a table.
            //
            // The banks are irregular now and snow has drifted over the
            // near edge, so there is no straight line anywhere for the eye
            // to catch.
            const float iceL = 4436f, iceR = 5244f;
            const float bot = 792f;
            var ice = new List<Vector2>();
            // far edge, hidden under the bridge deck
            for (float x = iceL; x <= iceR; x += 34f)
                ice.Add(new Vector2(x, GroundY));
            // east bank slants in
            ice.Add(new Vector2(iceR + 30f, bot - 44f));
            // near edge: wind-blown snow over the ice, so it is a wave
            for (float x = iceR + 30f; x >= iceL - 30f; x -= 34f)
                ice.Add(new Vector2(x, bot - 44f + Mathf.Sin(x * 0.019f) * 15f));
            // west bank slants out
            ice.Add(new Vector2(iceL - 30f, GroundY + 16f));
            DrawColoredPolygon(ice.ToArray(), new Color(0.46f, 0.55f, 0.56f));
            // 水面高光：淡一档的同色调，半透明
            DrawColoredPolygon(new[]
            {
                new Vector2(iceL, GroundY), new Vector2(iceR, GroundY),
                new Vector2(iceR, GroundY + 34), new Vector2(iceL, GroundY + 34),
            }, new Color(0.62f, 0.70f, 0.70f, 0.40f));

            // v3.25: the span's own shadow on the ice. Everything else in
            // this level casts a shadow - the houses, the trees, the sandbag
            // walls, the bodies - and the bridge was the one thing standing
            // on the ground without one, so it hovered over the river
            // instead of standing on it.
            DrawSetTransform(new Vector2((iceL + iceR) * 0.5f, GroundY + 46f), 0, new Vector2(13f, 0.85f));
            DrawTextureRect(N918SoftSnow.Blob(new Color(0.40f, 0.45f, 0.50f), 0.32f),
                new Rect2(-20f, -20f, 40f, 40f), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);

            // 冰裂缝：不规则折线（用噪声偏移中间点，不是直竖线）
            for (int i = 0; i < 14; i++)
            {
                float x = 4475 + i * 54;
                var crack = new List<Vector2>();
                int steps = 5;
                for (int s = 0; s <= steps; s++)
                {
                    float t = (float)s / steps;
                    float px = x + Mathf.Sin(i * 2.3f + s * 1.7f) * 18f * (1f - Mathf.Abs(t - 0.5f) * 1.4f);
                    float py = GroundY + 8 + t * 160f + Mathf.Sin(i * 0.7f + s * 2.1f) * 4f;
                    crack.Add(new Vector2(px, py));
                }
                DrawPolyline(crack.ToArray(), new Color(0.40f, 0.48f, 0.50f, 0.40f), 1.8f, true);
                // 裂缝上的高光细线
                DrawPolyline(crack.ToArray(), new Color(0.70f, 0.78f, 0.78f, 0.22f), 0.8f, true);
            }
            // 斜向的交叉裂缝（更自然）
            for (int i = 0; i < 6; i++)
            {
                float x0 = 4490 + i * 110;
                float y0 = GroundY + 40 + (i % 3) * 30;
                var crack = new List<Vector2>
                {
                    new Vector2(x0, y0),
                    new Vector2(x0 + 24, y0 + 32),
                    new Vector2(x0 + 48, y0 + 58),
                    new Vector2(x0 + 72, y0 + 84),
                };
                DrawPolyline(crack.ToArray(), new Color(0.38f, 0.46f, 0.48f, 0.36f), 1.6f, true);
            }

            // 初秋：江面改为流动秋水，不再绘制冰面雪块与冻土堆
        }

        /// <summary>
        /// v3.8 dressing at every rail break: scorch on the ballast, twisted
        /// rail stubs curling out of both cut ends, splinters and debris.
        /// Without this the segmented rails just showed a naked gap in the
        /// line - "why would the track stop mid-air for no reason".
        /// Deterministic per crater index, stable between frames.
        /// Drawn after the crater textures so the twisted steel stays visible.
        /// </summary>
        private void DrawRailBlastDamage()
        {
            float railY = GroundY - 1f;
            for (int i = 0; i < _craterX.Length; i++)
            {
                float cx = _craterX[i];
                if (cx <= 2420f || cx >= WorldW - 24f) continue;
                float r = _craterR[i] * 0.82f;

                var rng = new RandomNumberGenerator();
                rng.Seed = (ulong)(i * 7919 + 13);

                // scorched bed under the gap
                DrawSetTransform(new Vector2(cx, railY + 9f), 0, new Vector2(r / 20f, 0.42f));
                DrawTextureRect(N918SoftSnow.Blob(new Color(0.19f, 0.175f, 0.16f), 0.5f),
                    new Rect2(-20f, -20f, 40f, 40f), false);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);

                // twisted stubs curling up and out from both cut ends, one pair per rail line
                foreach (float ry in new[] { railY, railY + 12 })
                foreach (float sgn in new[] { -1f, 1f })
                {
                    float ex = cx + sgn * r;
                    for (int s = 0; s < 2; s++)
                    {
                        float len = rng.RandfRange(8f, 15f);
                        float tilt = 0.35f + 0.45f * s + rng.RandfRange(-0.12f, 0.12f);
                        float dx = sgn * len * Mathf.Cos(tilt);
                        float dy = -len * Mathf.Sin(tilt);
                        DrawLine(new Vector2(ex, ry), new Vector2(ex + dx, ry + dy),
                            InkStyle.InkColor, InkStyle.ThinStroke, true);
                        // bright torn steel at the tip
                        DrawCircle(new Vector2(ex + dx, ry + dy), 1.3f, new Color(0.85f, 0.86f, 0.84f));
                    }
                }

                // splinters and kicked-up ballast around the hole
                for (int s = 0; s < 6; s++)
                {
                    float px = cx + rng.RandfRange(-r, r) * 1.15f;
                    float py = railY + rng.RandfRange(2f, 16f);
                    DrawCircle(new Vector2(px, py), rng.RandfRange(0.8f, 1.7f),
                        new Color(0.27f, 0.25f, 0.23f, 0.8f));
                }
            }

            // lighter shell nicks all along the track - short broken rail with
            // curled stubs, scorch on the bed, kicked ballast. one every few
            // hundred px so no stretch of line reads as untouched.
            foreach (float dxp in _railDmgX)
            {
                var rng2 = new RandomNumberGenerator();
                rng2.Seed = (ulong)(int)(dxp * 0.71f) + 977;

                DrawSetTransform(new Vector2(dxp, railY + 9f), 0, new Vector2(0.55f, 0.34f));
                DrawTextureRect(N918SoftSnow.Blob(new Color(0.21f, 0.19f, 0.17f), 0.5f),
                    new Rect2(-20f, -20f, 40f, 40f), false);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);

                foreach (float ry in new[] { railY, railY + 12 })
                foreach (float sgn in new[] { -1f, 1f })
                {
                    float ex = dxp + sgn * 10f;
                    float len = rng2.RandfRange(6f, 11f);
                    float tilt = 0.4f + rng2.RandfRange(-0.1f, 0.25f);
                    float tx = ex + sgn * len * Mathf.Cos(tilt);
                    float ty = ry - len * Mathf.Sin(tilt);
                    DrawLine(new Vector2(ex, ry), new Vector2(tx, ty),
                        InkStyle.InkColor, InkStyle.ThinStroke, true);
                    DrawCircle(new Vector2(tx, ty), 1.1f, new Color(0.85f, 0.86f, 0.84f));
                }

                for (int s = 0; s < 3; s++)
                {
                    float px = dxp + rng2.RandfRange(-12f, 12f);
                    float py = railY + rng2.RandfRange(2f, 15f);
                    DrawCircle(new Vector2(px, py), rng2.RandfRange(0.7f, 1.4f),
                        new Color(0.27f, 0.25f, 0.23f, 0.8f));
                }
            }
        }


        /// <summary>
        /// The abandoned supply cart at the head of the level: a plank crate
        /// body on two wheels with a snapped shaft lying in the snow.
        ///
        /// v3.19: this was a separate node in the game layer, and it never
        /// appeared on screen at all - the node drew, but the game layer put
        /// it outside the camera and nothing showed. It is painted here in
        /// the ground art now, the same place the craters and the ammo
        /// crates live, so it sits in the snow with the track running behind
        /// it.
        ///
        /// The body is the real crate texture, painted in the same hand as
        /// every other prop. The player was right that the earlier version -
        /// flat filled rectangles and circles - looked like a different art
        /// style pasted next to the watercolour props. Only the wheels and
        /// the snapped shaft are code, because a rectangle cannot fake them.
        /// </summary>
        private void DrawSupplyCart()
        {
            // v3.19: was 280, which is where the player starts - the soldier
            // spawned right on top of it and hid the whole thing. 350 puts
            // it a stride ahead, still the first thing the player walks past.
            const float cartX = 350f;
            const float boxW = 46f;
            const float wheelR = 7f;

            var crate = CrateTex();
            if (crate == null) return;
            var csz = crate.GetSize();

            // drift behind and a soft contact shadow, same treatment the
            // ammo crates get
            DrawSetTransform(new Vector2(cartX + boxW * 0.5f, GroundY - 2f), 0, new Vector2(1.8f, 0.5f));
            DrawTextureRect(N918SoftSnow.Blob(new Color(0.72f, 0.66f, 0.48f), 0.5f),
                new Rect2(-boxW, -boxW, boxW * 2f, boxW * 2f), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            SnowShadow.Draw(this, new Vector2(cartX + boxW * 0.5f, GroundY - 1f), boxW * 0.6f, 0.22f);

            // the crate body, tipped over on its side with a slight tilt
            float tw = boxW * 1.3f;
            float th = tw * csz.Y / csz.X;
            DrawSetTransform(new Vector2(cartX + boxW * 0.5f, GroundY - 1f - wheelR * 0.6f), -0.05f,
                Vector2.One);
            DrawTextureRect(crate, new Rect2(-tw * 0.5f, -th, tw, th), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);

            // the two wheels, sunk into the drift
            foreach (float wx in new[] { cartX + 9f, cartX + boxW - 9f })
            {
                DrawCircle(new Vector2(wx, GroundY - wheelR + 1.5f), wheelR, new Color(0.24f, 0.21f, 0.19f));
                DrawCircle(new Vector2(wx, GroundY - wheelR + 1.5f), wheelR * 0.62f, new Color(0.50f, 0.43f, 0.35f));
                DrawCircle(new Vector2(wx, GroundY - wheelR + 1.5f), wheelR * 0.22f, new Color(0.30f, 0.26f, 0.21f));
                DrawLine(new Vector2(wx - wheelR * 0.8f, GroundY - wheelR * 1.7f),
                    new Vector2(wx + wheelR * 0.8f, GroundY - wheelR * 1.7f),
                    new Color(0.92f, 0.94f, 0.94f, 0.85f), 1.3f, true);
            }

            // the shaft, snapped off and lying in the snow beside the wreck
            DrawLine(new Vector2(cartX - 24f, GroundY - 2f), new Vector2(cartX - 4f, GroundY - 12f),
                new Color(0.34f, 0.27f, 0.19f), 2.4f, true);
            DrawLine(new Vector2(cartX - 24f, GroundY - 3f), new Vector2(cartX - 4f, GroundY - 13f),
                new Color(0.90f, 0.92f, 0.92f, 0.55f), 1f, true);
        }

        private void DrawRailway()
        {
            var skip = new HashSet<int>(_railSkipIdx);
            float railY = GroundY - 1f;

            // v3.25: the track now runs ONTO the bridge.
            //
            //   fieldEnd - the open field's ballast bed and its sleepers
            //              stop at the west end of the deck. A railway
            //              bridge carries its track on a steel plate, it
            //              has no ballast and no sleepers.
            //   trackEnd - the rails carry on across the deck and stop
            //              where the span is blown apart. Past that there
            //              is no bridge left to run on.
            //
            // Before this the whole line was drawn first and the bridge
            // was painted over the end of it, so the track stopped in
            // mid snow and the bridge stood next to it like a separate
            // picture - no joint, no rails running onto the deck.
            float fieldEnd = BridgeDeckX;
            float trackEnd = BridgeWreckX;

            // a crater sitting on the rail line means the shelling broke the
            // track - skip sleepers and leave a gap in the rails there, so the
            // pits read as battlefield damage instead of holes punched through
            // a pristine line.
            bool OnRailCrater(float x)
            {
                for (int i = 0; i < _craterX.Length; i++)
                    if (_craterX[i] > 2380f && _craterX[i] < WorldW && Mathf.Abs(x - _craterX[i]) < _craterR[i] * 0.82f)
                        return true;
                return false;
            }

            bool NearRailDmg(float x, float tol)
            {
                foreach (float d in _railDmgX)
                    if (Mathf.Abs(x - d) < tol) return true;
                return false;
            }

            // ballast bed: packed darker snow with a gently wavy top edge so
            // it doesn't read as a ruler-straight line drawn on white paper
            var ballast = new List<Vector2>();
            for (float x = 2380f; x <= fieldEnd; x += 38f)
                ballast.Add(new Vector2(x, railY + 2f + Mathf.Sin(x * 0.021f) * 1.6f - Mathf.Sin(x * 0.053f) * 0.8f));
            ballast.Add(new Vector2(fieldEnd, railY + 22f));
            ballast.Add(new Vector2(2380f, railY + 22f));
            DrawColoredPolygon(ballast.ToArray(), new Color(0.76f, 0.745f, 0.72f, 0.5f));
            // soft top-edge highlight following the same wave
            var topEdge = new List<Vector2>();
            for (float x = 2380f; x <= fieldEnd; x += 38f)
                topEdge.Add(new Vector2(x, railY + 2f + Mathf.Sin(x * 0.021f) * 1.6f - Mathf.Sin(x * 0.053f) * 0.8f));
            DrawPolyline(topEdge.ToArray(), new Color(0.92f, 0.91f, 0.885f, 0.7f), 2.6f, false);

            int k = 0;
            for (float x = 2400; x < fieldEnd; x += 34, k++)
            {
                // v3.25: the last stretch before the deck is never skipped.
                // One missing tie right at the joint leaves a bare strip of
                // ballast between the field and the bridge and the line
                // reads as cut off again.
                if ((skip.Contains(k) && x < fieldEnd - 150f) || OnRailCrater(x + 11f)) continue;
                // each sleeper got own height/brightness jitter (deterministic,
                // picture stays stable between frames)
                float jy = Mathf.Sin(k * 12.9898f) * 2.2f;
                float shade = 0.40f + 0.06f * Mathf.Sin(k * 3.7f);
                bool charred = NearRailDmg(x + 11f, 30f);
                var wood = charred
                    ? new Color(0.17f, 0.14f, 0.12f)
                    : new Color(shade, shade * 0.80f, shade * 0.60f);
                var r = new Rect2(x, railY + 3 + jy, 22, 13);
                // sleeper: ink frame then wood fill, the hand-drawn manga way
                InkStyle.InkRect(this, r, wood, InkStyle.InkColor, InkStyle.Stroke);
                // shadow edge, lower-left away from the light
                InkStyle.InkRect(this, new Rect2(x - 2, r.Position.Y + 7, 22, 5),
                    new Color(0.26f, 0.20f, 0.14f, 0.8f), InkStyle.InkSoft, InkStyle.ThinStroke);
                // thin snow on the sleeper top for ~60% of them, phase-shifted
                // so it don't read as a copied row
                if (!charred && Mathf.Sin(k * 7.31f) > -0.2f)
                    DrawLine(new Vector2(x + 2, r.Position.Y + 1.5f),
                        new Vector2(x + 20, r.Position.Y + 1.2f),
                        new Color(0.93f, 0.95f, 0.95f, 0.8f), 2.0f, true);
            }

            // v3.26: the deck plate.
            //
            // The span used to get a flat band of tan sleepers laid straight
            // onto the texture's riveted girder web. Two full-width bands of
            // contrasting tone with nothing between them - a tan strip lying
            // on a dark strip - and the eye read the bridge as having TWO
            // roads on it. The user called it "one of them is a texture and
            // the rail is painted on, so it looks like the bridge has two
            // paths".
            //
            // A railway bridge carries its track on a steel deck plate, and
            // the plate is part of the structure: rail on sleeper, sleeper
            // on plate, plate on main girder. Drawing that plate here - in
            // the level's own InkStyle - slots a third, clearly structural
            // layer between the track and the girder, so the track reads as
            // ONE continuous line running from the field onto the span.
            //
            // The plate's top edge IS the rail line, so the two sit exactly
            // on each other and the joint with the field track is seamless.
            const float plateH = 13f;
            var plate = new[]
            {
                new Vector2(fieldEnd - 8f, railY + 1f),
                new Vector2(trackEnd, railY + 1f),
                new Vector2(trackEnd, railY + 1f + plateH),
                new Vector2(fieldEnd - 8f, railY + 1f + plateH),
            };
            // steel base - a step darker than the snow, a step lighter than
            // the girder web below it, so the three layers separate
            DrawColoredPolygon(plate, new Color(0.36f, 0.335f, 0.30f));
            // the top flange catches the flat winter light
            DrawColoredPolygon(new[]
            {
                new Vector2(fieldEnd - 8f, railY + 1f),
                new Vector2(trackEnd, railY + 1f),
                new Vector2(trackEnd, railY + 4f),
                new Vector2(fieldEnd - 8f, railY + 4f),
            }, new Color(0.52f, 0.50f, 0.46f));
            // closed ink outline - the plate is a cut steel edge, it reads as
            // one plate and not as a loose stripe
            var plateEdge = new List<Vector2>(plate) { plate[0] };
            DrawPolyline(plateEdge.ToArray(), InkStyle.InkColor, 2.2f, true);
            // the last stretch of ballast tapers into the plate instead of
            // stopping dead at it
            DrawColoredPolygon(new[]
            {
                new Vector2(fieldEnd - 104f, railY + 2f),
                new Vector2(fieldEnd - 8f, railY + 2f),
                new Vector2(fieldEnd - 8f, railY + 12f),
                new Vector2(fieldEnd - 104f, railY + 22f),
            }, new Color(0.73f, 0.755f, 0.765f, 0.5f));

            // v3.26: the sleepers on the span go through the SAME routine as
            // the sleepers on the field - same wood tone, same height
            // jitter, same shadow strip, same snow on top. The old bridge
            // sleepers were a flat un-jittered 9px tan bar, and a perfect
            // repeating row of flat bars reads as a painted stripe rather
            // than as track. One rhythm, one material, field to span.
            for (float x = fieldEnd; x < trackEnd; x += 34f)
            {
                int tk = Mathf.RoundToInt((x - 2400f) / 34f);
                float jy = Mathf.Sin(tk * 12.9898f) * 2.2f;
                float shade = 0.40f + 0.06f * Mathf.Sin(tk * 3.7f);
                bool charred = NearRailDmg(x + 11f, 30f);
                var wood = charred
                    ? new Color(0.17f, 0.14f, 0.12f)
                    : new Color(shade, shade * 0.80f, shade * 0.60f);
                // laid flat on the steel plate: thinner than the field ties,
                // which are sunk in ballast
                var r = new Rect2(x, railY + 3f + jy, 22, 10f);
                InkStyle.InkRect(this, r, wood, InkStyle.InkColor, InkStyle.Stroke);
                // shadow edge, lower-left away from the light
                InkStyle.InkRect(this, new Rect2(x - 2, r.Position.Y + 5f, 22, 4f),
                    new Color(0.26f, 0.20f, 0.14f, 0.8f), InkStyle.InkSoft, InkStyle.ThinStroke);
                // thin snow on the sleeper top, phase-shifted exactly like the
                // field ties, so the two halves of the line carry the same
                // hand-placed rhythm
                if (!charred && Mathf.Sin(tk * 7.31f) > -0.2f)
                    DrawLine(new Vector2(x + 2, r.Position.Y + 1.5f),
                        new Vector2(x + 20, r.Position.Y + 1.2f),
                        new Color(0.93f, 0.95f, 0.95f, 0.8f), 2.0f, true);
            }

            // two parallel rails: broken at the craters, drawn as short intact
            // segments with steel sheen. snow caps sit on top in short patches.
            // v3.25: they carry on across the bridge deck and stop at the
            // wrecked span, so the line and the bridge are one thing.
            foreach (float ry in new[] { railY, railY + 12 })
            {
                // ink base, segmented around the blast gaps
                float segStart = 2400f;
                for (float x = 2400f; x <= trackEnd; x += 10f)
                {
                    if (OnRailCrater(x) || NearRailDmg(x, 9f))
                    {
                        if (x - segStart > 4f)
                            DrawLine(new Vector2(segStart, ry), new Vector2(x, ry), InkStyle.InkColor, InkStyle.BoldStroke, true);
                        segStart = x + 10f;
                    }
                }
                if (trackEnd - segStart > 4f)
                    DrawLine(new Vector2(segStart, ry), new Vector2(trackEnd, ry), InkStyle.InkColor, InkStyle.BoldStroke, true);

                // steel sheen only on the intact lengths
                segStart = 2400f;
                for (float x = 2400f; x <= trackEnd; x += 10f)
                {
                    if (OnRailCrater(x) || NearRailDmg(x, 9f))
                    {
                        if (x - segStart > 4f)
                            DrawLine(new Vector2(segStart, ry), new Vector2(x, ry), new Color(0.87f, 0.86f, 0.83f), 1.6f, true);
                        segStart = x + 10f;
                    }
                }
                if (trackEnd - segStart > 4f)
                    DrawLine(new Vector2(segStart, ry), new Vector2(trackEnd, ry), new Color(0.85f, 0.86f, 0.84f), 1.6f, true);
            }

            // the track is torn apart where the span is blown out: the rails
            // do not just stop, they end in twisted steel
            DrawTornTrackEnd(trackEnd, railY);

            // 轨顶积雪：短促的亮段（不是通条白线，雪是被风一段段吹积的），避开被炸断处
            //
            // v3.26: on the span these segments used to be exactly as bright
            // and as long as the ones on the field. Against the flat dark
            // deck plate that reads as DASHED LANE MARKINGS on a road - and
            // that is half of what made the bridge look like it carried two
            // roads. Over the span they are shorter, thinner and half as
            // opaque: snow lying on a rail in the open, not paint on a
            // carriageway.
            for (float sx = 2420; sx < trackEnd; sx += 88)
            {
                if (OnRailCrater(sx) || NearRailDmg(sx, 20f)) continue;
                bool onSpan = sx >= fieldEnd;
                float seg = onSpan ? 8f + Mathf.Sin(sx * 0.07f) * 4f : 14f + Mathf.Sin(sx * 0.07f) * 8f;
                float wid = onSpan ? 1.6f : 2.4f;
                float alp = onSpan ? 0.42f : 0.85f;
                DrawLine(new Vector2(sx, railY - 1.2f), new Vector2(sx + seg, railY - 1.2f),
                    new Color(0.96f, 0.97f, 0.97f, alp), wid, true);
            }

            // scattered gravel / ice specks on the ballast so it isn't a flat wash
            for (float gx = 2410; gx < fieldEnd; gx += 57f)
            {
                if (OnRailCrater(gx) || NearRailDmg(gx, 12f)) continue;
                float gy = railY + 8f + Mathf.Sin(gx * 0.13f) * 4f;
                DrawCircle(new Vector2(gx, gy), 1.4f, new Color(0.64f, 0.63f, 0.60f, 0.7f));
            }
        }

        /// <summary>
        /// The end of the track on the bridge. The rails do not simply stop
        /// where the span is blown apart - they end in twisted steel, the
        /// way a rail tears when the structure under it goes. Without this
        /// the line stopped in a clean cut and read as a rendering seam
        /// rather than as a destroyed bridge.
        /// </summary>
        private void DrawTornTrackEnd(float x, float railY)
        {
            var rng = new RandomNumberGenerator();
            rng.Seed = 19311104u;
            foreach (float ry in new[] { railY, railY + 12 })
            {
                for (int s = 0; s < 3; s++)
                {
                    float len = rng.RandfRange(7f, 15f);
                    float tilt = 0.30f + 0.42f * s + rng.RandfRange(-0.12f, 0.12f);
                    float dx = len * Mathf.Cos(tilt);
                    float dy = -len * Mathf.Sin(tilt);
                    DrawLine(new Vector2(x, ry), new Vector2(x + dx, ry + dy),
                        InkStyle.InkColor, InkStyle.ThinStroke, true);
                    DrawCircle(new Vector2(x + dx, ry + dy), 1.2f, new Color(0.85f, 0.86f, 0.84f));
                }
            }
            // splintered ties and kicked up snow around the break
            for (int s = 0; s < 7; s++)
            {
                float px = x + rng.RandfRange(-18f, 14f);
                float py = railY + rng.RandfRange(3f, 14f);
                DrawLine(new Vector2(px, py), new Vector2(px + rng.RandfRange(-5f, 5f), py + rng.RandfRange(-2f, 3f)),
                    new Color(0.30f, 0.26f, 0.22f, 0.85f), 1.6f, true);
            }
        }

        private Texture2D _craterTex;

        private void DrawCraters()
        {
            // craters are generated watercolor textures now. The old stacked
            // ellipses read like a flat bullseye pancake, totally off from the
            // hand-drawn style of everything else on the field.
            _craterTex ??= GD.Load<Texture2D>("res://assets/art/props/crater_painted.png");
            if (_craterTex == null) return;
            var tsz = _craterTex.GetSize();
            for (int i = 0; i < _craterX.Length; i++)
            {
                float x = _craterX[i];
                float r = _craterR[i];
                // per-crater mirror + size jitter off its own x, stable between frames.
                // bottom-anchored on the ground line: the texture is a top-down
                // crater, squashed flat so it reads as a dent in the snow field
                bool flip = (i % 2 == 1);
                float jitter = 1f + Mathf.Sin(x * 0.37f) * 0.06f;
                float w = r * 3.0f * jitter;
                // v3.22: squashed to 45% the dark interior of the hole became
                // a thin navy band and the whole thing read as a puddle of
                // water lying on the snow. The art is remapped now (dark
                // earth in the bowl, cold snow on the rim) and the squash is
                // eased off so the bowl keeps its depth.
                float hEff = w * tsz.Y / tsz.X * 0.58f;
                float sx = flip ? -1f : 1f;
                DrawSetTransform(new Vector2(x, GroundY + 3f), 0, new Vector2(sx, 1f));
                DrawTextureRect(_craterTex, new Rect2(-w * 0.5f, -hEff, w, hEff), false);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
        }

        private Texture2D _bridgeTex;

        private void DrawBridge()
        {
            const float start = 4400f;
            const float end = 5200f;
            const float top = 376f;
            const float bottom = GroundY + 8f;

            // generated bridge art (truss + piers, burned mid-section) replaces the old
            // code-drawn one; keep the code path as fallback if the texture is missing
            _bridgeTex ??= GD.Load<Texture2D>("res://assets/art/props/bridge_autumn.png");
            if (_bridgeTex != null)
            {
                var bsz = _bridgeTex.GetSize();
                // v3.25: the user set the size rule - the bridge is 2.5 man
                // heights tall. Measured from the snow line it stands on up
                // to the top of the truss, because that is the height the
                // eye reads; the piers go down into the river ice and are
                // not part of the silhouette.
                //
                // Before this the truss top stood 179px over the snow - 1.57
                // man heights - so the span came up to the shoulder of the
                // man walking towards it.
                //
                // The texture cannot simply be scaled up: it is 1530x812 and
                // the span only has 800px of world to sit in, so a uniform
                // 1.55x would push the burnt-out middle of the truss off the
                // right edge of the level. Instead the truss is drawn DEEPER
                // (stretched vertically) and the piers keep their own
                // proportions - a deeper truss is what a big railway bridge
                // looks like anyway, and the concrete stays a squat pier
                // instead of turning into a stick.
                const float bh = 420f;               // whole texture, unchanged scale
                float bw = bh * bsz.X / bsz.Y;
                // v3.25: the span moved 48px east. The painting already
                // carries a concrete pier for the west end, but the deck of
                // the texture runs on past it - the girder under the last
                // 45px of the deck had nothing under it and the span read
                // as a piece of bridge dropped in the field and stopping
                // in mid air over the snow.
                //
                // Instead of bolting a grey slab onto the side (the v3.15
                // abutment, which the player called a stray box), the span
                // is simply set down on the pier that is already in the
                // painting. The west end of the deck now lands on the
                // concrete, and the bank edge of the frozen river sits just
                // under it - so the bridge is standing on the ground
                // instead of hovering over it.
                //
                // BridgeDeckX is the deck's west end in world coordinates
                // and the railway is laid along it - both come from the
                // same two measurements off the painting.
                float bx = BridgeDeckX - 9f * (bh / bsz.Y);
                // v3.17 seat fix: the old note claimed the deck sits at 53% of
                // the texture height and put row 430 on the rail line. A row
                // scan of bridge.png says otherwise - the truss runs 0-340,
                // the railing 340-365, the rail-bearing deck 365-400, the pier
                // caps 400-440 and the piers 440-803. So the old factor parked
                // the deck 40px ABOVE the rails: the track ran across the
                // frozen river band under the bridge instead of along its
                // deck, which is what made the two look like they had
                // nothing to do with each other. Seat row 355 on the rail
                // line instead and the rails run onto the bridge.
                const float deckRow = 355f;
                const float apexRow = 10f;          // where the truss reaches its top
                float scale = bh / bsz.Y;

                // how deep the truss has to be so its top stands BridgeH
                // above the snow line
                float trussScale = (BridgeH - 1f) / (deckRow - apexRow);
                float trussH = deckRow * trussScale;
                float trussTop = (GroundY - 1f) - deckRow * trussScale;

                // v3.21: the bridge art is a photoreal render while the whole
                // level is watercolour, and next to the painted houses it
                // read as a different game - the player called it a pyramid
                // dropped in the snow. Two passes push it back into the
                // picture: a cool tint so the steel takes the colour of the
                // air, then a pale blue haze over the whole span so the
                // distance eats its contrast the way it eats the
                // mountains behind it.
                // v3.24: the original art was right all along. The rendered
                // steel with its rivets and dark beams sits in the snowfield
                // perfectly well next to the burnt timber houses, and every
                // pass I made over it - desaturating it, flattening the
                // shading, washing it in blue - only pushed it further from
                // the level. It is back to the plain texture, untouched.
                // The two halves meet exactly on the deck row, so there is no
                // seam where the deeper truss hands over to the piers.
                DrawTextureRectRegion(_bridgeTex,
                    new Rect2(bx, trussTop, bw, trussH),
                    new Rect2(0f, 0f, bsz.X, deckRow));
                // the deck and the piers keep the original scale, so the
                // concrete does not turn into a tall thin post
                float pierH = (bsz.Y - deckRow) * scale;
                DrawTextureRectRegion(_bridgeTex,
                    new Rect2(bx, GroundY - 1f, bw, pierH),
                    new Rect2(0f, deckRow, bsz.X, bsz.Y - deckRow));

                // v3.25: snow drifted against the west end of the deck.
                // The painting ends in a straight vertical cut of the steel
                // and that cut is the clearest "this is a texture" tell in
                // the whole shot - the deck looked like a strip of picture
                // hanging in the air. A drift piled against the end is what
                // a real bridge head looks like in a northern winter.
                DrawSetTransform(new Vector2(bx + 24f, GroundY + 18f), 0, new Vector2(2.2f, 0.95f));
                DrawTextureRect(N918SoftSnow.Blob(new Color(0.62f, 0.55f, 0.37f), 0.95f),
                    new Rect2(-20f, -20f, 40f, 40f), false);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);

                // v3.25: the west end of the span used to die in mid air.
                // The first concrete pier of the texture stands 46px in
                // from the end of the deck, and there is nothing under that
                // 46px - the river ice only starts further east - so the
                // deck stopped over open snow with its underside showing.
                // The user said the bridge has to sit ON the snow.
                //
                // An abutment carries the end of the deck down into the
                // bank now: a concrete block in the same tone the piers
                // use, a snow cap on top of it and a couple of ink lines
                // for the edges, so it belongs to the level and does not
                // read as a second art style dropped next to the render.
                // v3.19: the "fresh snow line on the deck edge" is gone too.
                // It was a straight 3px white DrawLine running 788px from
                // 4406 to 5194 at a fixed height - the deck edge is not
                // straight, the span is not that long above the snow, and
                // the line floated over the truss and the far bank as a thin
                // bright streak whose tone did not match the mountains
                // behind it. That streak was the white line the player kept
                // seeing. The texture already carries snow along the deck.

                // v2.9 float fix, part 2: bury the texture bottom edge. The pier
                // feet ended on a hard horizontal cut against the snow - every
                // support read as "standing on the snow, not in it". Soft drift
                // blobs melt the base line into the ice and snow.
                // pier feet now land at ~GroundY+141 with the new seat, so the
                // drift blobs bury that cut line where the feet meet the ice
                for (float dx2 = start + 90f; dx2 < end - 40f; dx2 += 150f)
                {
                    DrawSetTransform(new Vector2(dx2, GroundY + 138f), 0, new Vector2(2.1f, 0.42f));
                    DrawTextureRect(N918SoftSnow.Blob(new Color(0.62f, 0.55f, 0.37f), 0.75f),
                        new Rect2(-20f, -20f, 40f, 40f), false);
                    DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                }
                // contact shading where the span crosses the near snowfield
                DrawSetTransform(new Vector2(start + 130f, GroundY + 12f), 0, new Vector2(3.2f, 0.5f));
                DrawTextureRect(N918SoftSnow.Blob(new Color(0.55f, 0.60f, 0.64f), 0.30f),
                    new Rect2(-20f, -20f, 40f, 40f), false);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                return;
                            return;
            }

            // 远端桁架（偏后，颜色淡 + 墨边更细）——完好
            Truss(start + 40, end, top + 26, bottom - 4, new Color(0.28f, 0.32f, 0.34f, 0.55f), InkStyle.InkSoft, 3.4f, null);

            // 桥墩：混凝土 + 破冰棱（冬季嫩江结冰需要尖端破冰）
            var damagedPiers = new HashSet<int>(_pierDamageIdx);
            int pi = 0;
            for (float px = 4540; px <= 5140; px += 200, pi++)
            {
                // 墩身（混凝土矩形 + 墨边）
                InkStyle.InkRect(this, new Rect2(px - 15, GroundY + 2, 30, 120),
                    new Color(0.38f, 0.40f, 0.42f), InkStyle.InkSoft, InkStyle.Stroke);
                // 墩身阴影面（左侧背光——太阳在画面右上）
                var pierShadow = new[]
                {
                    new Vector2(px - 15, GroundY + 2),
                    new Vector2(px - 12, GroundY + 2),
                    new Vector2(px - 12, GroundY + 122),
                    new Vector2(px - 15, GroundY + 122),
                };
                DrawColoredPolygon(pierShadow, new Color(0.24f, 0.26f, 0.28f, 0.7f));
                // 底部破冰棱（三角形尖端，向下30px）
                var iceBreaker = new[]
                {
                    new Vector2(px - 20, GroundY + 118),
                    new Vector2(px - 15, GroundY + 118),
                    new Vector2(px - 15, GroundY + 145),
                    new Vector2(px, GroundY + 160),       // 尖端！
                    new Vector2(px + 15, GroundY + 145),
                    new Vector2(px + 15, GroundY + 118),
                    new Vector2(px + 20, GroundY + 118),
                };
                InkStyle.PenStroke(this, iceBreaker,
                    new Color(0.34f, 0.36f, 0.38f), InkStyle.InkSoft, InkStyle.Stroke);
                // 墩顶帽梁（桥面处的混凝土帽，接承重梁）
                InkStyle.InkRect(this, new Rect2(px - 22, GroundY - 6, 44, 8),
                    new Color(0.82f, 0.85f, 0.86f), InkStyle.InkSoft, InkStyle.ThinStroke);
                // 墩顶积雪（帽梁上一点雪）
                DrawLine(new Vector2(px - 20, GroundY - 6), new Vector2(px + 20, GroundY - 6),
                    new Color(0.94f, 0.96f, 0.96f), 2.5f, true);
                // 炮弹烧黑的弹痕
                if (damagedPiers.Contains(pi))
                {
                    DrawCircle(new Vector2(px, GroundY + 55), 18f, new Color(0.10f, 0.09f, 0.08f, 0.7f));
                    DrawCircle(new Vector2(px + 5, GroundY + 90), 10f, new Color(0.10f, 0.09f, 0.08f, 0.5f));
                    DrawCircle(new Vector2(px - 4, GroundY + 130), 7f, new Color(0.18f, 0.16f, 0.14f, 0.55f));
                    DrawLine(new Vector2(px - 16, GroundY + 50), new Vector2(px + 16, GroundY + 62),
                        new Color(0.10f, 0.08f, 0.07f, 0.5f), 1.5f, true);
                }
            }

            // 桥面板：先描墨色粗边再填木色，加桥枕
            InkStyle.InkRect(this, new Rect2(start, GroundY - 8, end - start, 22),
                new Color(0.30f, 0.26f, 0.22f), InkStyle.InkColor, InkStyle.BoldStroke);
            for (float x = start; x < end; x += 22)
                DrawLine(new Vector2(x, GroundY - 8), new Vector2(x, GroundY + 14),
                    InkStyle.InkColor, 1.2f, true);

            // 中段烧焦痕迹
            DrawRect(new Rect2(4680, GroundY - 10, 90, 24), new Color(0.08f, 0.07f, 0.06f, 0.8f));
            DrawLine(new Vector2(4680, GroundY - 10), new Vector2(4770, GroundY - 10),
                new Color(0.08f, 0.07f, 0.06f), 2f, true);

            // 近端桁架——有几根管被打断
            Truss(start, end, top, bottom + 4, new Color(0.20f, 0.22f, 0.24f), InkStyle.InkColor, 6f, _trussSkipDiags);

            // 桁架上的积雪
            for (float x = start; x < end; x += 42)
                DrawLine(new Vector2(x, top - 3), new Vector2(x + 30, top - 3),
                    new Color(0.92f, 0.94f, 0.94f, 0.9f), 3f, true);
            DrawRect(new Rect2(4680, top - 5, 90, 6), new Color(0.14f, 0.12f, 0.10f));
        }

        /// <summary>Pratt 风格平行弦桁架（节间更大更稀疏，真实铁路桥比例）。</summary>
        private void Truss(float start, float end, float top, float bottom, Color fill, Color ink, float w, int[] skipDiag)
        {
            const int panelW = 200;  // 节间宽度（真实铁路桥 4-5m 节间，游戏比例 200px）
            var skip = skipDiag == null ? null : new HashSet<int>(skipDiag);
            // 上下弦：先填粗色再墨边
            DrawLine(new Vector2(start, top), new Vector2(end, top), fill, w, true);
            DrawLine(new Vector2(start, top), new Vector2(end, top), ink, InkStyle.Stroke, true);
            DrawLine(new Vector2(start, bottom), new Vector2(end, bottom), fill, w, true);
            DrawLine(new Vector2(start, bottom), new Vector2(end, bottom), ink, InkStyle.Stroke, true);
            int diagIdx = 0;
            for (float x = start; x <= end; x += panelW, diagIdx++)
            {
                // 竖直腹杆
                DrawLine(new Vector2(x, top), new Vector2(x, bottom), fill, w * 0.7f, true);
                DrawLine(new Vector2(x, top), new Vector2(x, bottom), ink, InkStyle.ThinStroke, true);
                if (x + panelW <= end)
                {
                    bool skipA = skip != null && skip.Contains(diagIdx);
                    bool skipB = skip != null && skip.Contains(diagIdx + 1);
                    if (!skipA)
                    {
                        DrawLine(new Vector2(x, top), new Vector2(x + panelW, bottom), fill, w * 0.45f, true);
                        DrawLine(new Vector2(x, top), new Vector2(x + panelW, bottom), ink, InkStyle.ThinStroke, true);
                    }
                    if (!skipB)
                    {
                        DrawLine(new Vector2(x, bottom), new Vector2(x + panelW, top), fill, w * 0.45f, true);
                        DrawLine(new Vector2(x, bottom), new Vector2(x + panelW, top), ink, InkStyle.ThinStroke, true);
                    }
                }
            }
        }

        private void DrawTrenchLines()
        {
            // 第一道战壕：一条下陷的暗线 + 两侧翻雪（离开出生点，不然像脚下一块黑木板）
            var dark = new Color(0.42f, 0.40f, 0.37f, 0.32f);
            DrawPolyline(new[]
            {
                new Vector2(1120, GroundY + 4), new Vector2(1320, GroundY + 6),
                new Vector2(1540, GroundY + 2), new Vector2(1700, GroundY + 5),
            }, dark, 7f, true);
            DrawPolyline(new[]
            {
                new Vector2(1120, GroundY + 9), new Vector2(1700, GroundY + 10),
            }, new Color(0.97f, 0.98f, 0.98f, 0.55f), 3f, true);

            // trench-side ammo crates, generated watercolor sprite now.
            // blended into the snow: a drift behind, a snow cap on top and a
            // base mound so the box reads as weathered/half-buried, not a hard
            // black block dropped on white paper.
            var crate = CrateTex();
            if (crate != null)
            {
                var csz = crate.GetSize();
                for (int ci = 0; ci < _crateX.Length; ci++)
                {
                    float x = _crateX[ci];
                    float ch = _crateH[ci];
                    float cw = ch * csz.X / csz.Y;
                    float rot = _crateRot[ci];
                    // snow drift behind the crate
                    DrawSetTransform(new Vector2(x + 2f, GroundY - 2f), 0, new Vector2(1.6f, 0.5f));
                    DrawTextureRect(N918SoftSnow.Blob(new Color(0.72f, 0.66f, 0.48f), 0.5f),
                        new Rect2(-cw * 0.7f, -cw * 0.7f, cw * 1.4f, cw * 1.4f), false);
                    DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                    // soft contact shadow
                    SnowShadow.Draw(this, new Vector2(x + 10f, GroundY - 1f), cw * 0.55f, 0.22f);
                    // the crate itself, tilted, sunk a touch
                    DrawSetTransform(new Vector2(x + 8f, GroundY - 1f), rot, Vector2.One);
                    DrawTextureRect(crate, new Rect2(-cw * 0.5f, -ch + 2f, cw, ch), false);
                    DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                    // snow piled on the crate top -> half-buried look
                    DrawSetTransform(new Vector2(x + 8f, GroundY - 1f - ch + 4f), rot, Vector2.One);
                    DrawTextureRect(N918SoftSnow.Blob(new Color(0.62f, 0.55f, 0.37f), 0.7f),
                        new Rect2(-cw * 0.55f, -6f, cw * 1.1f, 12f), false);
                    DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                    // snow mound at the base front
                    DrawSetTransform(new Vector2(x + 6f, GroundY + 1f), 0, new Vector2(1.4f, 0.4f));
                    DrawTextureRect(N918SoftSnow.Blob(new Color(0.62f, 0.55f, 0.37f), 0.55f),
                        new Rect2(-cw * 0.5f, -8f, cw, 16f), false);
                    DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                }
            }

            // 散落的钢盔与断枪（不指向尸体，只留物）
            foreach (float x in new[] { 1440f, 2780f, 3880f })
            {
                DrawArc(new Vector2(x, GroundY - 2), 10f, Mathf.Pi, Mathf.Tau, 14, new Color(0.30f, 0.30f, 0.26f), 6f, true);
                DrawLine(new Vector2(x + 18, GroundY - 1), new Vector2(x + 60, GroundY - 12), new Color(0.22f, 0.20f, 0.18f), 2f, true);
            }

        }
    }

    /// <summary>
    /// 掩体沙袋单独一层：ZIndex 高于玩家与敌兵，画在人前面。
    /// 人经过时是“躲到掩体后面”，而不是“站在一摞麻袋顶上”——
    /// 之前沙袋画在地面层，人物只要走到同一条 x 上就像踩着沙袋。
    /// 
    /// 全部位置/尺寸/歪斜在 _Ready 一次性随机化，_Draw 只消费字段。
    /// </summary>
    private partial class CoverArt : Node2D
    {
        private struct StackSpec
        {
            public float X;
            public float BaseY;
            public float W;        // drawn wall width
            public float H;        // drawn wall height
        }

        /// <summary>
        /// v3.18: the sandbag walls live in this node but the barbed wire is
        /// drawn in GroundArt, so the wire had no way to know where the walls
        /// are. The stake positions were hardcoded, and the run at 3820 put
        /// three stakes straight through the wall at 4160 and two through the
        /// wall at 4388 - the stakes poked out of the sandbags like a bundle
        /// of rods someone had stuck in them. The base positions are shared
        /// here so the wire can skip any stake that would land inside a wall.
        ///
        /// The wall at 4388 is gone as well. It sat at 4319-4457, which is
        /// right on top of the bridge head at 4400 and the abutment under it
        /// - a sandbag wall parked in the gateway of the bridge blocked the
        /// view of the span and sat across the structure. A bridge mouth is a
        /// passage, not a place to stack cover.
        /// </summary>
        internal static readonly float[] SandbagBaseX = { 980f, 2320f, 3640f, 4160f };
        internal static readonly float SandbagHalfWidth = 54f * 2.55f * 0.5f + 24f;

        internal const float FullHp = 100f;

        private StackSpec[] _stacks = System.Array.Empty<StackSpec>();
        private Texture2D _sackTex;
        private float[] _hp = System.Array.Empty<float>();

        /// <summary>v3.55: one scattered bag of the wreck debris field.</summary>
        private sealed class WreckBag
        {
            public Vector2 Pos;      // world
            public float Rot;
            public Vector2 Size;
            public bool Torn;        // darker, torn-open bag
            public bool Snow;        // snow-capped (half-buried look)
        }

        private List<WreckBag>[] _wreck;

        /// <summary>
        /// v3.55: build the scattered debris layout for one stack. A blast does
        /// not leave a tidy mound: the wall bursts into a low chaotic pile at
        /// the old footprint PLUS bags flung out into the snow on both sides,
        /// half-buried, some torn open, sand spilled between them. Seeded per
        /// stack so the field never reshimmers frame to frame.
        /// </summary>
        private void BuildWreckLayout(int i)
        {
            var rng = new RandomNumberGenerator();
            rng.Seed = 7311UL + (ulong)i * 977UL;
            var s = _stacks[i];
            var list = new List<WreckBag>();

            // —— 低矮乱堆：旧墙脚印内 9~13 只随机旋转的散袋 ——
            int mound = rng.RandiRange(9, 13);
            for (int k = 0; k < mound; k++)
            {
                float w = rng.RandfRange(19f, 27f);
                list.Add(new WreckBag
                {
                    Pos = new Vector2(s.X + rng.RandfRange(-s.W * 0.48f, s.W * 0.48f),
                                      GroundY - 2f - rng.RandfRange(1f, 15f)),
                    Rot = rng.RandfRange(-0.55f, 0.55f),
                    Size = new Vector2(w, w * rng.RandfRange(0.42f, 0.55f)),
                    Torn = rng.Randf() < 0.30f,
                    Snow = rng.Randf() < 0.35f,
                });
            }
            // —— 抛飞散袋：砸到雪里，两侧 0.6~1.8 倍墙宽处，半埋 + 积雪 ——
            int flung = rng.RandiRange(5, 8);
            for (int k = 0; k < flung; k++)
            {
                float w = rng.RandfRange(17f, 24f);
                float side = rng.Randf() < 0.5f ? -1f : 1f;
                list.Add(new WreckBag
                {
                    Pos = new Vector2(s.X + side * rng.RandfRange(s.W * 0.60f, s.W * 1.80f),
                                      GroundY - 2f - rng.RandfRange(0f, 5f)),
                    Rot = rng.RandfRange(-1.35f, 1.35f),
                    Size = new Vector2(w, w * rng.RandfRange(0.36f, 0.46f)),
                    Torn = rng.Randf() < 0.25f,
                    Snow = true,
                });
            }
            list.Sort((a, b) => a.Pos.Y.CompareTo(b.Pos.Y));   // 远的先画
            _wreck[i] = list;
        }

        /// <summary>v3.55: 本轮爆炸把哪一垛炸飞了（无则 NaN），Explode 用来挂飞散特效。</summary>
        internal float WreckX { get; private set; } = float.NaN;

        /// <summary>沙袋是给主角躲的：蹲/趴在垛子投影范围内算进掩体。
        /// v3.53: destroyed walls cover nobody.</summary>
        public bool Covers(float x)
        {
            for (int i = 0; i < _stacks.Length; i++)
            {
                if (_hp[i] <= 0f) continue;
                var s = _stacks[i];
                if (Mathf.Abs(x - s.X) < s.W * 0.5f + 26f) return true;
            }
            return false;
        }

        // ---- v3.53: destructible cover ----

        internal bool IsDestroyed(int i) => _hp[i] <= 0f;

        internal float StackX(int i) => _stacks[i].X;

        /// <summary>Nearest intact stack within range of px, or -1.</summary>
        internal int NearestStack(float px, float range)
        {
            int best = -1;
            float bd = range;
            for (int i = 0; i < _stacks.Length; i++)
            {
                if (_hp[i] <= 0f) continue;
                float d = Mathf.Abs(px - _stacks[i].X);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>The intact stack covering x, or -1 (bullet chips land here).</summary>
        internal int StackAt(float x)
        {
            for (int i = 0; i < _stacks.Length; i++)
            {
                if (_hp[i] <= 0f) continue;
                var s = _stacks[i];
                if (Mathf.Abs(x - s.X) < s.W * 0.5f + 26f) return i;
            }
            return -1;
        }

        internal void Damage(int i, float dmg)
        {
            if (i < 0 || i >= _hp.Length || _hp[i] <= 0f) return;
            _hp[i] = Mathf.Max(0f, _hp[i] - dmg);
            QueueRedraw();
        }

        /// <summary>Grenade blast at world x: a direct hit flattens the wall
        /// (unusable, visual swaps to the wreck), a blast one wall over just
        /// mangles it. Returns true when a wall was destroyed by THIS blast.</summary>
        internal bool Blast(float x)
        {
            bool wrecked = false;
            WreckX = float.NaN;
            for (int i = 0; i < _stacks.Length; i++)
            {
                if (_hp[i] <= 0f) continue;
                float d = Mathf.Abs(x - _stacks[i].X);
                if (d < 75f)
                {
                    wrecked = true;
                    Damage(i, 999f);
                    WreckX = _stacks[i].X;
                }
                else if (d < 175f)
                {
                    float before = _hp[i];
                    Damage(i, 65f);
                    if (before > 0f && _hp[i] <= 0f) wrecked = true;
                }
            }
            return wrecked;
        }

        public override void _Ready()
        {
            var rng = new RandomNumberGenerator();
            rng.Randomize();
            // v3.11: 3580 -> 3640. The old anchor's left edge could reach into
            // the h4 footprint (3180-3545), so the house corner poked out
            // behind the stack like a random block. 3640 clears h4 fully and
            // still stops short of the rail crater at 3780.
            float[] bases = SandbagBaseX;
            var list = new List<StackSpec>();
            for (int i = 0; i < bases.Length; i++)
            {
                // waist-high cover: a standing rifleman must top the wall by half a torso,
                // wall wide enough for one man prone behind it
                // v3.19: was 54. The soldier is 94px now instead of 108, so
                // the wall has to come down with him or it reads as chest
                // high cover.
                float h = 47f + rng.RandfRange(-4f, 5f);
                list.Add(new StackSpec
                {
                    X = bases[i] + rng.RandfRange(-24f, 24f),
                    BaseY = GroundY - 2f,
                    W = h * (2.55f + rng.RandfRange(-0.25f, 0.3f)),
                    H = h,
                });
            }
            _stacks = list.ToArray();
            _sackTex = GD.Load<Texture2D>("res://assets/art/props/sandbags.png");
            // v3.55: the tidy wreck texture is retired - an explosion SCATTERS
            // the wall, it does not leave a neat mound. Each stack gets a
            // deterministic debris layout (seeded, built once) used by _Draw.
            _wreck = new List<WreckBag>[_stacks.Length];
            for (int i = 0; i < _stacks.Length; i++) BuildWreckLayout(i);
            _hp = new float[_stacks.Length];
            for (int i = 0; i < _hp.Length; i++) _hp[i] = FullHp;
        }

        public override void _Draw()
        {
            if (_sackTex == null) return;
            for (int i = 0; i < _stacks.Length; i++)
            {
                var s = _stacks[i];
                float cx = s.X;
                bool destroyed = _hp[i] <= 0f;
                bool damaged = !destroyed && _hp[i] < 60f;

                SnowShadow.Draw(this, new Vector2(cx, GroundY - 1f),
                    destroyed ? s.W * 0.8f : s.W * 0.55f, 0.30f);

                bool flip = i % 2 == 1;
                if (destroyed)
                {
                    // v3.55: the wall SCATTERED - a low chaotic pile of torn
                    // bags where it stood plus bags flung out into the snow,
                    // half-buried, sand spilled between them. The old tidy
                    // wreck texture read as "still a neat wall, just shorter".
                    DrawWreck(i);
                    continue;
                }

                if (flip)
                {
                    DrawSetTransform(new Vector2(cx, GroundY + 1f), 0, new Vector2(-1f, 1f));
                    DrawTextureRect(_sackTex, new Rect2(-s.W * 0.5f, -s.H, s.W, s.H), false);
                    DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                }
                else
                {
                    DrawTextureRect(_sackTex, new Rect2(cx - s.W * 0.5f, GroundY + 1f - s.H, s.W, s.H), false);
                }

                if (damaged)
                {
                    // chipped wall: scorch blotch on the face + one bag knocked
                    // loose at the foot. Still usable, visibly worse for wear.
                    float side = flip ? -1f : 1f;
                    DrawSetTransform(new Vector2(cx - side * s.W * 0.22f, GroundY - s.H * 0.55f), 0.10f * side,
                        new Vector2(1.4f, 1.1f));
                    DrawTextureRect(N918SoftSnow.Blob(new Color(0.14f, 0.12f, 0.09f), 0.5f),
                        new Rect2(-14f, -14f, 28f, 28f), false);
                    DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                    var sack = new Color(0.62f, 0.55f, 0.42f);
                    float lx = cx + side * s.W * 0.62f;
                    DrawRect(new Rect2(lx - 11f, GroundY - 10f, 22f, 12f), sack);
                    DrawRect(new Rect2(lx - 11f, GroundY - 10f, 22f, 12f),
                        new Color(0.2f, 0.17f, 0.12f, 0.75f), false, 1.2f);
                }
            }
        }

        /// <summary>
        /// v3.55: draw the scattered wreck. Bags share the hand-drawn language
        /// of the chipped-wall loose bag above (same tan fill + ink outline),
        /// torn ones darker with the seam split open, snow-capped ones get a
        /// white cap so the flung bags read as half-buried in drift.
        /// </summary>
        private void DrawWreck(int i)
        {
            var bags = _wreck[i];
            if (bags == null) return;
            var s = _stacks[i];
            var sack = new Color(0.62f, 0.55f, 0.42f);
            var sackDark = new Color(0.46f, 0.39f, 0.29f);
            var ink = new Color(0.2f, 0.17f, 0.12f, 0.8f);
            var spill = new Color(0.55f, 0.48f, 0.36f, 0.30f);

            // 溢出的沙：堆间几摊浅色
            for (int k = 0; k < 3; k++)
            {
                float px = s.X + (k - 1) * s.W * 0.35f;
                DrawSetTransform(new Vector2(px, GroundY - 2f), 0, new Vector2(1f, 0.45f));
                DrawTextureRect(N918SoftSnow.Blob(spill, 0.5f), new Rect2(-22f, -22f, 44f, 44f), false);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }

            // 焦痕：爆点中央
            DrawSetTransform(new Vector2(s.X, GroundY - 2f), 0, new Vector2(s.W * 0.045f, 0.6f));
            DrawTextureRect(N918SoftSnow.Blob(new Color(0.16f, 0.14f, 0.11f), 0.40f),
                new Rect2(-32f, -32f, 64f, 64f), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);

            foreach (var b in bags)
            {
                DrawSetTransform(b.Pos, b.Rot, Vector2.One);
                var fill = b.Torn ? sackDark : sack;
                DrawRect(new Rect2(-b.Size.X * 0.5f, -b.Size.Y * 0.5f, b.Size.X, b.Size.Y), fill);
                // 撕口：袋身中缝一道深色裂线
                if (b.Torn)
                    DrawRect(new Rect2(-b.Size.X * 0.5f, -1f, b.Size.X, 2f),
                        new Color(0.25f, 0.2f, 0.14f, 0.9f));
                DrawRect(new Rect2(-b.Size.X * 0.5f, -b.Size.Y * 0.5f, b.Size.X, b.Size.Y), ink, false, 1.2f);
                if (b.Snow)
                    DrawRect(new Rect2(-b.Size.X * 0.5f, -b.Size.Y * 0.5f, b.Size.X, b.Size.Y * 0.34f),
                        new Color(0.94f, 0.95f, 0.97f, 0.85f));
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
        }
    }

    /// <summary>
    /// The abandoned supply cart, standing in the snow.
    ///
    /// v3.18: it used to live in the parallax near band, which meant the
    /// ground-layer railway drew straight across it and the near houses
    /// chopped it into pieces - the cart looked like a few brown scraps
    /// floating between the rails. It is a world object now, drawn in the
    /// cover layer so the track passes behind it the way it passes behind
    /// the sandbag walls.
    ///
    /// v3.19: the drawing was still the weak part. Every rectangle and
    /// circle in it was a flat fill, and the player was right that it looked
    /// like a different art style pasted next to the watercolour props -
    /// a flat brown box with two coins under it. The body is now the real
    /// crate texture, painted in the same hand as everything else, and only
    /// the wheels and the snapped shaft are code, because those are the two
    /// shapes a rectangle cannot fake.
    ///
    /// It stands at 280, the stretch of open snow the player walks into at
    /// the start of the level. Everywhere past that is occupied - a house, a
    /// sandbag wall, a crater, a bunker or the railway - and the only other
    /// gap on the whole map is the frozen river beyond the bridge.
    /// </summary>

    // ================= 特效 =================

    /// <summary>
    /// v3.55: one-shot bag burst when a sandbag wall dies. A handful of bags
    /// flies out on real ballistics (gravity, one snow bounce), sand puffs
    /// along the way, everything fades into the debris field that _Draw now
    /// leaves behind. Self-freeing.
    /// </summary>
    private partial class CoverBurstFx : Node2D
    {
        private sealed class Bag
        {
            public Vector2 Pos, Vel;
            public float Rot, RotV, W, H;
            public bool Landed;
        }

        private readonly List<Bag> _bags = new();
        private float _t;
        private const float Life = 1.1f;

        public CoverBurstFx()
        {
            var rng = new RandomNumberGenerator();
            rng.Randomize();
            for (int k = 0; k < 7; k++)
            {
                float w = rng.RandfRange(18f, 26f);
                _bags.Add(new Bag
                {
                    Pos = new Vector2(rng.RandfRange(-24f, 24f), rng.RandfRange(-26f, -6f)),
                    Vel = new Vector2(rng.RandfRange(-190f, 190f), rng.RandfRange(-330f, -140f)),
                    Rot = 0f,
                    RotV = rng.RandfRange(-7f, 7f),
                    W = w, H = w * 0.48f,
                });
            }
        }

        public override void _Process(double delta)
        {
            float f = (float)delta;
            _t += f;
            if (_t > Life) { QueueFree(); return; }
            foreach (var b in _bags)
            {
                if (b.Landed) continue;
                b.Vel += new Vector2(0f, 980f * f);
                b.Pos += b.Vel * f;
                b.Rot += b.RotV * f;
                if (b.Pos.Y > -3f)              // 砸进雪里就停住、转势归零
                {
                    b.Pos = new Vector2(b.Pos.X, -3f);
                    b.Landed = true;
                }
            }
            QueueRedraw();
        }

        public override void _Draw()
        {
            float fade = Mathf.Clamp((Life - _t) / 0.3f, 0f, 1f);
            var sack = new Color(0.62f, 0.55f, 0.42f, fade);
            var ink = new Color(0.2f, 0.17f, 0.12f, 0.8f * fade);
            foreach (var b in _bags)
            {
                DrawSetTransform(b.Pos, b.Rot, Vector2.One);
                DrawRect(new Rect2(-b.W * 0.5f, -b.H * 0.5f, b.W, b.H), sack);
                DrawRect(new Rect2(-b.W * 0.5f, -b.H * 0.5f, b.W, b.H), ink, false, 1.1f);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
        }
    }

    private partial class TracerFx : Node2D
    {
        private readonly List<(Vector2 a, Vector2 b, float life, Color c)> _tracers = new();
        private readonly List<(Vector2 a, Vector2 b, float t, float dur, Color c, float tail)> _shots = new();
        private readonly List<(Vector2 p, float life, float r)> _puffs = new();

        public void Tracer(Vector2 a, Vector2 b, Color c) => _tracers.Add((a, b, 0.14f, c));

        // 飞行弹道：子弹从 a 飞向 b（约 0.20s 抵达），带亮头 + 尾迹，开枪更有"射出去"的感觉
        public void Shoot(Vector2 a, Vector2 b, Color c, float tail = 110f)
            => _shots.Add((a, b, 0f, 0.20f, c, tail));

        public void Puff(Vector2 p, float r) => _puffs.Add((p, 0.55f, r));

        public override void _Process(double delta)
        {
            float f = (float)delta;
            for (int i = _tracers.Count - 1; i >= 0; i--)
            {
                var t = _tracers[i];
                t.life -= f;
                if (t.life <= 0f) _tracers.RemoveAt(i);
                else _tracers[i] = t;
            }
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                var s = _shots[i];
                s.t += f;
                if (s.t >= s.dur) _shots.RemoveAt(i);
                else _shots[i] = s;
            }
            for (int i = _puffs.Count - 1; i >= 0; i--)
            {
                var p = _puffs[i];
                p.life -= f;
                p.r += f * 26f;
                if (p.life <= 0f) _puffs.RemoveAt(i);
                else _puffs[i] = p;
            }
            QueueRedraw();
        }

        public override void _Draw()
        {
            foreach (var t in _tracers)
            {
                float a = Mathf.Clamp(t.life / 0.14f, 0f, 1f);
                DrawLine(t.a, t.b, new Color(t.c, 0.35f * a), 5f, true);
                DrawLine(t.a, t.b, new Color(t.c, 0.95f * a), 1.7f, true);
            }
            foreach (var s in _shots)
            {
                // ease-out：子弹先快后慢地飞抵
                float p = 1f - Mathf.Pow(1f - Mathf.Clamp(s.t / s.dur, 0f, 1f), 2f);
                var head = s.a.Lerp(s.b, p);
                var dir = s.b - s.a;
                if (dir.LengthSquared() > 0.001f) dir = dir.Normalized();
                var tailStart = head - dir * s.tail;
                float fade = Mathf.Clamp((s.dur - s.t) / 0.05f, 0f, 1f);
                DrawLine(tailStart, head, new Color(s.c, 0.30f * fade), 6f, true);
                DrawLine(tailStart, head, new Color(s.c, 0.95f * fade), 2.2f, true);
                DrawCircle(head, 2.8f * fade, new Color(1f, 0.95f, 0.70f, 0.95f * fade));
            }
            foreach (var p in _puffs)
            {
                float a = Mathf.Clamp(p.life / 0.55f, 0f, 1f);
                DrawCircle(p.p, p.r, new Color(0.95f, 0.96f, 0.97f, 0.28f * a));
                DrawCircle(p.p, p.r * 0.55f, new Color(0.95f, 0.96f, 0.97f, 0.34f * a));
            }
        }
    }

    private partial class SnowScreen : Node2D
    {
        private struct Flake
        {
            public float X, Y, Spd, Size, Sway, Depth;
        }

        private readonly List<Flake> _flakes = new();
        private readonly RandomNumberGenerator _rng = new();
        public float Wind = 0.6f;
        private float _gust;
        private float _blizzardT;
        private float _nextBlizzard;
        private float _blizzardIntensity;
        public float Blizzard => _blizzardIntensity;

        public override void _Ready()
        {
            _rng.Seed = 20311104;
            for (int i = 0; i < 240; i++) AddFlake();
            _nextBlizzard = 25f + _rng.Randf() * 20f;
        }

        private void AddFlake()
        {
            float depth = _rng.Randf();
            _flakes.Add(new Flake
            {
                X = _rng.Randf() * 1280f,
                Y = _rng.Randf() * 720f,
                Spd = 26f + depth * 74f,          // 近处快、远处慢
                Size = 0.9f + depth * 2.3f,       // 全是柔和圆点，没有几何形状
                Sway = _rng.Randf() * Mathf.Tau,
                Depth = depth,
            });
        }

        public void Burst()
        {
            for (int i = 0; i < _flakes.Count; i++)
            {
                var f = _flakes[i];
                f.Y -= 26f;
                f.Spd += 30f;
                _flakes[i] = f;
            }
        }

        public override void _Process(double delta)
        {
            float f = (float)delta;
            float t = (float)Time.GetTicksMsec() / 1000f;

            // 暴风雪计时
            _nextBlizzard -= f;
            if (_blizzardT > 0f)
            {
                _blizzardT -= f;
                if (_blizzardT <= 0f)
                {
                    _blizzardT = 0f;
                    _nextBlizzard = 20f + _rng.Randf() * 25f;
                }
            }
            else if (_nextBlizzard <= 0f)
            {
                _blizzardT = 8f + _rng.Randf() * 7f;
            }

            // 平滑过渡
            float targetBlizzard = _blizzardT > 0f ? 1f : 0f;
            _blizzardIntensity = Mathf.Lerp(_blizzardIntensity, targetBlizzard, f * 0.6f);

            // 动态增减：暴风雪时 ~640 颗（风雪弥漫），平时 240
            int targetCount = (int)Mathf.Lerp(240f, 640f, _blizzardIntensity);
            while (_flakes.Count < targetCount) AddFlake();
            if (_flakes.Count > targetCount)
                _flakes.RemoveRange(targetCount, _flakes.Count - targetCount);

            // 阵风 + 暴风雪额外风（幅度收敛：是“风裹着雪”，不是雪被抽飞）
            _gust = Mathf.Sin(t * 0.3f) * 0.4f + Mathf.Sin(t * 1.7f) * 0.15f;
            float extraWind = _blizzardIntensity * 1.6f;
            for (int i = 0; i < _flakes.Count; i++)
            {
                var p = _flakes[i];
                float spdMul = 1f + _blizzardIntensity * 1.05f;
                p.Y += p.Spd * spdMul * f;
                float baseHoriz = -34f * (Wind + extraWind) - p.Spd * 0.30f * (0.4f + _blizzardIntensity);
                float gustHoriz = -_gust * (16f + _blizzardIntensity * 34f);
                float swayHoriz = Mathf.Sin(t * 1.3f + p.Sway) * (5f + p.Depth * 7f) * f * (1f + _blizzardIntensity * 0.5f);
                p.X += (baseHoriz + gustHoriz) * f + swayHoriz;
                if (p.Y > 730f) { p.Y = -10f; p.X = (p.X + 397f) % 1280f; }
                if (p.X < -20f) p.X += 1300f;
                if (p.X > 1300f) p.X -= 1300f;
                _flakes[i] = p;
            }
            QueueRedraw();
        }

        public override void _Draw()
        {
            // 暴风雪 fog：垂直渐变遮罩（顶浓脚淡，压远景不压玩家脚边）
            if (_blizzardIntensity > 0.12f)
            {
                var s = GetViewportRect().Size;
                float topAlpha = 0.34f * _blizzardIntensity;
                float midAlpha = 0.20f * _blizzardIntensity;
                float botAlpha = 0.06f * _blizzardIntensity;
                float midY = s.Y * 0.55f;
                float botY = s.Y * 0.82f;
                DrawRect(new Rect2(0, 0, s.X, midY), new Color(0.72f, 0.77f, 0.80f, topAlpha));
                DrawRect(new Rect2(0, midY, s.X, botY - midY), new Color(0.74f, 0.78f, 0.81f, midAlpha));
                DrawRect(new Rect2(0, botY, s.X, s.Y - botY), new Color(0.78f, 0.81f, 0.83f, botAlpha));
                DrawRect(new Rect2(0, 0, s.X, s.Y), new Color(0.82f, 0.85f, 0.87f, 0.05f * _blizzardIntensity));
            }

            // 雪花：永远是柔和圆点（外晕 + 实心核），没有任何线条形态；
            // 只在暴风雪时给一点点顺风的短拖影表达速度感（≤5px，半透明）。
            foreach (var p in _flakes)
            {
                float a = Mathf.Min(0.82f, (0.26f + p.Depth * 0.42f) * (1f + _blizzardIntensity * 0.25f));
                var pos = new Vector2(p.X, p.Y);
                DrawCircle(pos, p.Size * 1.55f, new Color(0.97f, 0.98f, 1f, a * 0.30f));
                DrawCircle(pos, p.Size, new Color(0.97f, 0.98f, 1f, a));
                if (_blizzardIntensity > 0.35f && p.Depth > 0.45f)
                {
                    float tail = (2f + p.Size * 0.8f) * _blizzardIntensity; // ≤ ~5px
                    DrawLine(pos, pos + new Vector2(tail, tail * 0.30f),
                        new Color(0.97f, 0.98f, 1f, a * 0.30f), p.Size * 0.8f, true);
                }
            }
        }
    }

    private partial class FogScreen : CanvasLayer
    {
        public FogScreen() { Layer = 5; }

        public override void _Ready() => AddChild(new FogArt());

        private partial class FogArt : Control
        {
            private float _t;

            public override void _Ready()
            {
                SetAnchorsPreset(LayoutPreset.FullRect);
                MouseFilter = MouseFilterEnum.Ignore;
            }

            public override void _Process(double delta)
            {
                _t += (float)delta;
                QueueRedraw();
            }

            public override void _Draw()
            {
                var s = GetViewportRect().Size;

                // Soft drifting mist.
                //
                // v2.8: this used to be four big DrawCircle blobs. A circle has a
                // hard rim, so on the flat snow those rims showed up as bright
                // white arcs that crawled around — exactly the "white lines
                // flashing across the screen" the player reported. Now each band
                // is painted with a soft radial falloff texture, so there is no
                // edge to catch the light and the mist just breathes.
                var soft = N918SoftSnow.Blob(new Color(0.95f, 0.97f, 0.98f), 0.10f);
                for (int i = 0; i < 4; i++)
                {
                    float y = 430 + i * 52 + Mathf.Sin(_t * 0.22f + i * 1.7f) * 18f;
                    float x = -120 + Mathf.Sin(_t * 0.11f + i) * 180f;
                    DrawSetTransform(new Vector2(x + s.X * 0.5f, y), 0, new Vector2(3.4f, 0.22f));
                    DrawTextureRect(soft, new Rect2(-220f, -220f, 440f, 440f), false);
                    DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                }
            }
        }
    }
}

/// <summary>关东军尖兵：从右侧雪梁后出现，向玩家推进并射击；
/// 中弹会立刻警觉反击（枪声还会惊动附近同伴），身中两枪阵亡，
/// 原地留下一具雪地尸体（CorpseActor）与掉落的步枪 —— 没有透明消失。</summary>
public partial class N918EnemySoldier : Node2D
{
    private readonly N918Level _level;
    public SpriteActor _fig;
    public float _prevPhase;
    private float _fireCd;
    private float _phase;
    private float _aimHold;
    private int _hits;
    private readonly float _holdX;
    /// <summary>同一波里的站位错位：两人不能停在同一条 x 上，否则轮廓会叠成一团。</summary>
    private readonly float _lane;
    private float _alertT;            // 警觉剩余时间：被击中或听到枪声后进入战斗状态

    public bool Gone { get; private set; }
    public bool Alerted => _alertT > 0f;

    /// <summary>v3.55: 正在拉弦蓄力掷弹（同一次只掷一颗）。</summary>
    internal bool _throwing;

    public N918EnemySoldier(N918Level level, Vector2 pos, float lane)
    {
        _level = level;
        Position = pos;
        ZIndex = 2;
        _lane = lane;
        // 每人只推进到自己的警戒线：不会一路贴着玩家追到起点，
        // 也就不会出现“玩家在 720、敌人却在 1720 隔空命中”的情况。
        _holdX = pos.X - 540f;
    }

    public override void _Ready()
    {
        _fig = new SpriteActor(SpriteActor.Kind.Soldier) { Facing = -1f };
        AddChild(_fig);
        _fireCd = 0.8f + (float)GD.RandRange(0f, 0.8f);
    }

    /// <summary>被击中或听到枪声：进入警觉——立刻转向举枪、加快射击节奏、压上推进。</summary>
    public void Alert()
    {
        if (Gone) return;
        _alertT = 8f;
        _aimHold = Mathf.Max(_aimHold, 0.5f);
        _fireCd = Mathf.Min(_fireCd, 0.5f + (float)GD.RandRange(0f, 0.3f));
    }

    public override void _Process(double delta)
    {
        if (Gone) return;
        float f = (float)delta;
        if (_alertT > 0f) _alertT -= f;
        bool engaged = _alertT > 0f;

        float px = _level.PlayerX;
        float dist = Position.X - px;

        // 推进到距玩家约 330px 处停下；到警戒线也停；玩家逼近则退半步。
        // 警觉时压上更坚决（更快逼近、站位更贴）。
        // 玩家缩在掩体后面就往前顶——躲沙包不是无敌，日军会逼近到投弹距离。
        float targetDist = engaged ? 290f + _lane : 330f + _lane;
        if (_level.PlayerInCover()) targetDist -= 95f;
        float stopX = Mathf.Max(_holdX + _lane, _level.PlayerX + targetDist);
        float runSpeed = engaged ? 96f : 64f;
        if (Position.X > stopX + 16f)
        {
            Position -= new Vector2(runSpeed * f, 0);
            _fig.Speed = runSpeed;
            _fig.Phase += f * (engaged ? 8.4f : 6.2f);
        }
        else if (dist < 220f - _lane && _level.PlayerAlive && Position.X < _holdX + 96f)
        {
            Position += new Vector2(38f * f, 0);
            _fig.Speed = 38f;
            _fig.Phase += f * 5f;
        }
        else
        {
            _fig.Speed = 0f;
            _fig.Phase = Mathf.Lerp(_fig.Phase, 0f, f * 4f);
        }
        // 永远面向玩家（玩家在其左侧）：绝不背对主角发呆
        _fig.FacingRight = false;
        _fig.Grounded = true;
        _aimHold = Mathf.Max(0f, _aimHold - f);
        _fig.Aim = Mathf.MoveToward(_fig.Aim, _aimHold > 0f ? 1f : 0f, f * 5f);

        _fireCd -= f;
        // 只有真的出现在玩家画面里、且在有效射程内才开火：
        // 画面外的敌人不能凭空打中你，这一条比弹道本身更影响公平感。
        bool onScreen = Mathf.Abs(Position.X - _level.CamX) < 540f;
        float fireRange = engaged ? 760f : 700f;
        if (_fireCd <= 0f && onScreen && dist > 160f && dist < fireRange && _level.PlayerAlive)
        {
            _fireCd = engaged ? 1.5f + (float)GD.RandRange(-0.2f, 0.5f)
                              : 2.5f + (float)GD.RandRange(-0.3f, 0.5f);
            _aimHold = 0.55f;
            _fig.MuzzleFlash = 1f;
            _fig.Recoil = 1f;
            _level.EnemyFire(this);
        }
    }

    /// <summary>中弹：第一枪立刻反击；第二枪阵亡，原地留下尸体。</summary>
    public void Hit()
    {
        if (Gone) return;
        _hits++;
        _fig.HurtFlash = 1f;
        Position += new Vector2(10f, 0);
        _level.Shake(0.12f);
        if (_hits >= 2)
        {
            Gone = true;
            _level.EnemyDied(this);
            QueueFree();
        }
        else
        {
            Alert();
        }
    }
}

/// <summary>木柄手榴弹：抛物线 + 2.2 秒引信，落点爆炸由关卡结算伤害。</summary>
public partial class N918GrenadeProj : Node2D
{
    private Vector2 _vel;
    private float _fuse = 2.2f;
    private float _spin;

    public bool Exploded { get; private set; }

    public N918GrenadeProj(Vector2 pos, float facing)
    {
        Position = pos;
        _vel = new Vector2(facing * 320f, -330f);
        ZIndex = 3;
    }

    /// <summary>v3.53: explicit ballistics for enemy throws at the player's
    /// sandbag cover.</summary>
    public N918GrenadeProj(Vector2 pos, Vector2 vel)
    {
        Position = pos;
        _vel = vel;
        ZIndex = 3;
    }

    public override void _Process(double delta)
    {
        if (Exploded) return;
        float f = (float)delta;
        _vel += new Vector2(0, 1500f * f);
        Position += _vel * f;

        // 投掷的木柄手榴弹：空中随弹道缓慢翻滚（约 1.1 rad/s，整段飞行不到半圈，
        // 不再像"转圈圈"的轮子），落雪后立直静置、引信火花在顶端闪——去掉原先
        // 全程 2.4 rad/s 的连续自旋（#5 已降速仍被读成"转圈圈爆炸"）。
        if (Position.Y > N918Level.GroundY - 8f)
            _spin = Mathf.Lerp(_spin, 0f, f * 8f);
        else
            _spin += f * 1.1f;

        // bounce once on snow, roll a little
        if (Position.Y > N918Level.GroundY - 6f && _vel.Y > 0f)
        {
            Position = new Vector2(Position.X, N918Level.GroundY - 6f);
            _vel = new Vector2(_vel.X * 0.35f, -_vel.Y * 0.28f);
            if (Mathf.Abs(_vel.Y) < 60f) _vel = new Vector2(_vel.X * 0.8f, 0f);
        }

        _fuse -= f;
        QueueRedraw();
        if (_fuse <= 0f) Exploded = true;
    }

    public override void _Draw()
    {
        DrawSetTransform(new Vector2(0, N918Level.GroundY - Position.Y + 2f), 0, new Vector2(1.3f, 0.3f));
        DrawCircle(Vector2.Zero, 7f, new Color(0.10f, 0.09f, 0.08f, 0.25f));
        DrawSetTransform(new Vector2(0, 0), _spin, Vector2.One);
        // wooden handle + head
        DrawRect(new Rect2(-4f, -12f, 8f, 16f), new Color(0.42f, 0.32f, 0.20f));
        DrawRect(new Rect2(-7f, -22f, 14f, 12f), new Color(0.30f, 0.31f, 0.33f));
        DrawRect(new Rect2(-7f, -22f, 14f, 12f), new Color(0.18f, 0.18f, 0.20f, 0.8f), false, 1.2f);
        // fuse spark near the end
        if (_fuse < 0.6f && (int)(_fuse * 14f) % 2 == 0)
            DrawCircle(new Vector2(0f, -24f), 3f, new Color(1f, 0.85f, 0.4f, 0.95f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}

/// <summary>阵亡者之外的“记录点”：一部野战电话 + 一张压在雪里的纸。</summary>
public partial class N918Testimony : Node2D
{
    private readonly N918Level _level;
    public string Id { get; }
    public string Title { get; }
    public string[] Lines { get; }
    public string Source { get; }
    public bool Recorded;

    private float _t;
    private Texture2D _boxTex;

    public N918Testimony(N918Level level, string id, Vector2 pos, string title, string[] lines, string source)
    {
        _level = level;
        Id = id;
        Position = pos;
        Title = title;
        Lines = lines;
        Source = source;
        Recorded = GameState.I.S.N918Testimony.Contains(id);
        ZIndex = 3;
        // same watercolor pipeline as every other prop, so the box matches the scene art
        _boxTex = GD.Load<Texture2D>("res://assets/art/props/telcrate.png");
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var paper = new Color(0.93f, 0.90f, 0.80f);

        // unified ground shadow, box and page sit in the same light as everything else
        SnowShadow.Draw(this, new Vector2(2, -1), 26f, 0.26f);
        SnowShadow.Draw(this, new Vector2(22, -1), 13f, 0.20f);

        // field telephone crate: watercolor sprite from the same art pipeline,
        // half-buried in the snow like everything else on this field
        if (_boxTex != null)
        {
            var sz = _boxTex.GetSize();
            float bh = 34f;
            float bw = bh * sz.X / sz.Y;
            DrawSetTransform(new Vector2(-2f, -1f), -0.06f, Vector2.One);
            DrawTextureRect(_boxTex, new Rect2(-bw * 0.5f, -bh, bw, bh), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        // testimony page in the snow, small tilt so it reads as dropped not planted
        var page = new[]
        {
            new Vector2(9, -5), new Vector2(25, -8), new Vector2(27, 1), new Vector2(11, 3),
        };
        DrawColoredPolygon(page, paper);
        DrawPolyline(new[] { page[0], page[1], page[2], page[3], page[0] }, new Color(0.35f, 0.29f, 0.22f), 1.2f, true);
        for (int i = 0; i < 3; i++)
            DrawLine(new Vector2(12, -3 + i * 2.2f), new Vector2(24, -5f + i * 2.2f), new Color(0.42f, 0.36f, 0.28f), 0.9f, true);

        if (Recorded)
        {
            DrawCircle(new Vector2(0, -38), 6f, new Color(0.42f, 0.55f, 0.35f));
            DrawLine(new Vector2(-2.6f, -38), new Vector2(-0.4f, -35.5f), new Color(0.94f, 0.95f, 0.88f), 1.6f, true);
            DrawLine(new Vector2(-0.4f, -35.5f), new Vector2(3.2f, -41.5f), new Color(0.94f, 0.95f, 0.88f), 1.6f, true);
            return;
        }

        // waiting to be recorded: a soft warm lantern glow rising from the box,
        // no more big flat ring floating around the whole crate
        float pulse = 0.5f + 0.5f * Mathf.Sin(_t * 2.2f);
        var glowC = new Color(0.93f, 0.80f, 0.52f);
        DrawCircle(new Vector2(-3, -34), 15f + pulse * 3f, new Color(glowC.R, glowC.G, glowC.B, 0.06f + 0.05f * pulse));
        DrawCircle(new Vector2(-3, -34), 8f + pulse * 2f, new Color(glowC.R, glowC.G, glowC.B, 0.10f + 0.07f * pulse));
        DrawCircle(new Vector2(-3, -34), 2.6f, new Color(glowC.R, glowC.G, glowC.B, 0.45f + 0.25f * pulse));
    }
}

/// <summary>
/// HUD 弹药条：一颗子弹一个图标。开枪打掉的那发不消失——变成暗壳留在弹排里；
/// 弹袋最多 15 发，右上角一排右对齐。
/// </summary>
public partial class N918AmmoBar : Control
{
    private FieldPlayer _p;
    private const float IconW = 6f, IconH = 15f, Gap = 2.5f, RowW = 330f;

    public void Bind(FieldPlayer p) { _p = p; }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (_p == null) return;
        int total = FieldPlayer.AmmoCap;
        int shown = Mathf.Min(total, 40);
        float step = IconW + Gap;

        for (int i = 0; i < shown; i++)
        {
            // right-aligned row: newest rounds pile in from the right
            float x = RowW - (i + 1) * step;
            bool live = i < _p.Ammo;
            var casing = live ? new Color(0.84f, 0.66f, 0.28f) : new Color(0.40f, 0.37f, 0.32f, 0.85f);
            var tip = live ? new Color(0.60f, 0.42f, 0.19f) : new Color(0.31f, 0.29f, 0.26f, 0.85f);

            // cartridge: brass body + rounded nose, faint dark rim so it reads on snow paper
            DrawRect(new Rect2(x, 6f, IconW, IconH - 4f), casing);
            DrawColoredPolygon(new[]
            {
                new Vector2(x, 6f), new Vector2(x + IconW, 6f),
                new Vector2(x + IconW, 3.2f), new Vector2(x + IconW * 0.5f, 0.5f),
                new Vector2(x, 3.2f),
            }, tip);
            DrawRect(new Rect2(x, IconH + 1f, IconW, 1.4f), new Color(0, 0, 0, 0.25f));
        }

        // count badge so the number is still exact when the row overflows
        var font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(RowW - shown * step - 40f, 15f),
            $"{_p.Ammo}/{total}", HorizontalAlignment.Left, -1, 12,
            new Color(0.86f, 0.82f, 0.72f));
    }
}

/// <summary>
/// 生命 / 手榴弹图标条：鲜红心形一排（最多 5，空掉的是暗色空心），
/// 下面一排小木柄手榴弹（最多 5），全部自绘，不用文字点。
/// </summary>
public partial class N918VitalsBar : Control
{
    private FieldPlayer _p;
    private const int MaxHearts = FieldPlayer.HpMax;
    private const int MaxGrenades = FieldPlayer.GrenadeCap;
    private const float HeartW = 15f, HeartGap = 21f, NadeW = 9f, NadeGap = 14f;

    public void Bind(FieldPlayer p) { _p = p; }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (_p == null) return;
        var font = ThemeDB.FallbackFont;
        var labelC = new Color(0.86f, 0.82f, 0.72f);
        var heart = new Color(0.86f, 0.09f, 0.13f);          // bright fresh red
        var heartDark = new Color(0.26f, 0.16f, 0.15f, 0.9f); // lost heart: hollow dark
        float heartRow = 8f, nadeRow = 42f;

        DrawString(font, new Vector2(0, heartRow + 5f), "生命", HorizontalAlignment.Left, -1, 14, labelC);
        DrawString(font, new Vector2(0, nadeRow + 5f), "手榴弹", HorizontalAlignment.Left, -1, 14, labelC);

        float hx = 62f;
        for (int i = 0; i < MaxHearts; i++)
            DrawHeart(new Vector2(hx + i * HeartGap, heartRow), heart, i < _p.Hp);

        float gx = 62f;
        for (int i = 0; i < MaxGrenades; i++)
            DrawStickGrenade(new Vector2(gx + i * NadeGap, nadeRow), i < _p.Grenades);
    }

    private void DrawHeart(Vector2 c, Color col, bool full)
    {
        // heart = two lobes + V bottom, filled when alive
        float r = 3.4f;
        var lobeL = c + new Vector2(-r * 0.75f, -2.2f);
        var lobeR = c + new Vector2(r * 0.75f, -2.2f);
        var pts = new Vector2[]
        {
            new(c.X - 6.6f, c.Y - 3.4f), new(c.X, c.Y + 7.4f), new(c.X + 6.6f, c.Y - 3.4f),
        };
        if (full)
        {
            DrawCircle(lobeL, r, col);
            DrawCircle(lobeR, r, col);
            DrawColoredPolygon(pts, col);
            // tiny paper highlight so it reads as an object, not a flat dot
            DrawCircle(lobeL + new Vector2(-1.1f, -1.1f), 1.1f, new Color(1f, 0.75f, 0.72f, 0.55f));
        }
        else
        {
            var dim = new Color(0.30f, 0.24f, 0.22f, 0.55f);
            DrawCircle(lobeL, r, dim);
            DrawCircle(lobeR, r, dim);
            DrawColoredPolygon(pts, dim);
            DrawArc(c + new Vector2(0, 0.6f), 8.4f, Mathf.Pi, Mathf.Tau, 20,
                new Color(0.45f, 0.20f, 0.18f, 0.5f), 1.2f, true);
        }
    }

    private void DrawStickGrenade(Vector2 basePt, bool full)
    {
        // Type 24 style: wooden stick handle + tin oval head + end cap
        var stick = full ? new Color(0.58f, 0.42f, 0.24f) : new Color(0.30f, 0.26f, 0.22f, 0.55f);
        var head = full ? new Color(0.36f, 0.38f, 0.33f) : new Color(0.26f, 0.26f, 0.24f, 0.55f);
        var cap = full ? new Color(0.66f, 0.62f, 0.54f) : new Color(0.34f, 0.33f, 0.30f, 0.55f);

        // stick: vertical, head sits on top
        DrawRect(new Rect2(basePt.X - 1.6f, basePt.Y - 6f, 3.2f, 12f), stick);
        // tin head
        DrawCircle(basePt + new Vector2(0, -8.2f), 4.2f, head);
        DrawRect(new Rect2(basePt.X - 2.1f, basePt.Y - 14.6f, 4.2f, 2.6f), cap);
        // pull cord hint on a live one
        if (full)
            DrawLine(new Vector2(basePt.X + 1.8f, basePt.Y + 6f),
                new Vector2(basePt.X + 3.6f, basePt.Y + 8.6f),
                new Color(0.80f, 0.74f, 0.62f, 0.8f), 1f, true);
    }
}

/// <summary>通关档案卡：史实结果 + 后续章节提示 + 返回/重玩。</summary>
public partial class N918LevelEndUi : CanvasLayer
{
    private readonly Main _main;

    public N918LevelEndUi(Main main)
    {
        _main = main;
        Layer = 26;
    }

    public override void _Ready()
    {
        var dim = new ColorRect { Color = new Color(0.03f, 0.03f, 0.03f, 0.70f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(900, 0) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.905f, 0.855f, 0.735f),
            BorderColor = new Color(0.24f, 0.20f, 0.16f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            ContentMarginLeft = 36, ContentMarginRight = 36, ContentMarginTop = 26, ContentMarginBottom = 26,
            ShadowColor = new Color(0, 0, 0, 0.5f), ShadowSize = 16, ShadowOffset = new Vector2(6, 8),
        });
        center.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        panel.AddChild(box);

        Label L(string text, int size, Color color)
        {
            var l = new Label { Text = text };
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", color);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Vector2(820, 0);
            return l;
        }

        int resist = GameState.I.S.N918Resist;
        int testimony = GameState.I.S.N918Testimony.Count;
        int letters = GameState.I.S.N918EnvelopeCount;

        box.AddChild(L("1931 年 9 月 18 日夜 · 沈阳近郊 · 柳条湖—北大营", 14, new Color(0.48f, 0.43f, 0.35f)));
        box.AddChild(L("九一八事变 · 那一夜的见证", 27, new Color(0.16f, 0.14f, 0.12f)));
        box.AddChild(new ColorRect
        {
            Color = new Color(0.55f, 0.20f, 0.16f, 0.75f),
            CustomMinimumSize = new Vector2(0, 2),
        });
        box.AddChild(L("1931 年 9 月 18 日夜，关东军炸毁南满铁路柳条湖段，反诬中国军队，遂向北大营进攻。东北军奉「不抵抗令」撤出，沈阳及东北大部迅速沦陷——这是十四年抗战的起点。", 16, new Color(0.22f, 0.19f, 0.16f)));
        box.AddChild(L($"你记下了 {testimony} 段口述、收齐 {letters} 封信，最终在通信据点把那夜的实情发了出去。这些文字没能挡住坦克，却留下了「有人亲眼看见」的证据。", 16, new Color(0.22f, 0.19f, 0.16f)));

        // 不抵抗令抉择的双结局
        if (resist == 2)
            box.AddChild(L("那一夜，你选择了还手。在奉令不抵抗的洪流里，这一枪不能力挽狂澜，却让「服从」之外，还留着另一种可能。", 16, new Color(0.22f, 0.19f, 0.16f)));
        else
            box.AddChild(L("那一夜，你选择了遵令。北大营一夜未还手，整座营房被人走进来。历史记住的，正是这道没被扣下的扳机。", 16, new Color(0.22f, 0.19f, 0.16f)));

        box.AddChild(L("依据：九一八事变史实（柳条湖事件、北大营、不抵抗令、东北沦陷）；口述为复合虚构，不对应单一在世者。", 13, new Color(0.45f, 0.40f, 0.33f)));
        box.AddChild(L("后续章节（待制作）：1937 南京 · 1942 马来亚知知港 · 731 部队", 14, new Color(0.42f, 0.38f, 0.31f)));

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        row.Alignment = BoxContainer.AlignmentMode.End;

        var map = new Button { Text = "返回东亚地图" };
        map.AddThemeFontSizeOverride("font_size", 16);
        map.CustomMinimumSize = new Vector2(170, 40);
        map.Pressed += () => _main.ShowAsiaMap();
        row.AddChild(map);

        var replay = new Button { Text = "重玩本关" };
        replay.AddThemeFontSizeOverride("font_size", 16);
        replay.CustomMinimumSize = new Vector2(140, 40);
        replay.Pressed += () =>
        {
            var s = GameState.I.S;
            s.N918Testimony.Clear();
            s.N918Loot.Clear();
            s.N918Visited.Clear();
            s.N918EnvelopeCount = 0;
            s.N918RadioSent = false;
            s.N918Powered = false;
            s.N918Resist = 0;
            s.N918Refugees = 0;
            GameState.I.Save();
            _main.StartN918();
        };
        row.AddChild(replay);

        box.AddChild(row);
    }
}
