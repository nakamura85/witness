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
public static class SoftSnow
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
}

/// <summary>
/// 关卡 00「嫩江江桥 · 1931.11.4」垂直切片。
///
/// 设计要点（对应这次的明确要求）：
///  1) 场景分五层：天空 / 远山 / 雪原村落 / 近景树线 / 前景芦苇，逐层视差；
///  2) 人物是纸娃娃分层动画（躯干+双腿剪刀步、跳跃收腿、落地缓冲、开枪后坐），
///     脚踩实地面、受重力与碰撞约束，不做“纸片人上下飘”；
///  3) 敌兵会反击：中弹/听到枪声立刻警觉举枪反打，身中两枪阵亡并在雪地留下尸体；
///  4) 每波敌兵人数随机 1~3、陆续出场，没有“每次恰好两人”的巧合。
/// </summary>
public partial class JiangqiaoLevel : Node2D
{
    private readonly Main _main;

    public const float WorldW = 5200f;
    // walk line sits INSIDE the open snowfield, not on its top edge:
    // boots, corpses and sandbag walls all sink into the white expanse (v2.5)
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
    private Label _hpLabel;
    private AmmoBar _ammoBar;
    private ColorRect _flash;
    private ColorRect _blackout;
    private Backdrop[] _layers;
    private SkyScreen _sky;
    private CanvasModulate _tint;
    private CoverArt _coverArt;
    internal readonly List<EnemySoldier> _enemies = new();
    private readonly List<Testimony> _testimonies = new();
    internal readonly List<WeaponPickup> _pickups = new();
    internal readonly List<EnterableHouse> _houses = new();
    private HouseInteriorUi _houseUi;
    private readonly List<GrenadeProj> _grenades = new();
    private TracerFx _fx;
    private SnowScreen _snow;
    private SmokeFx _smokeWorld;    // smoke + fire over the near houses (world layer)
    private SmokeFx _smokeMid;      // chimney smoke over the distant village (parallax mid)
    private const float MidFactor = 0.24f;

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

    // —— 昼夜：1931-11-04 江桥（泰来县江桥镇，46.9N 123.4E，UTC+8）实算天文数据 ——
    // 日出 06:32 / 日落 16:29（程序验算），开战日凌晨天上挂 39% 照亮的残月，13:42 月落
    public const float SunriseH = 6.533f;
    public const float SunsetH = 16.483f;
    public const float MoonLit = 0.392f;      // 残月照亮比
    private float _clock = 5.6f;              // 游戏钟（小时），开场 05:36 天还没亮
    public const float ClockRate = 82f;       // 1 真秒 = 82 游戏秒，一局正好从凌晨打到黄昏
    private const float ClockMax = 19.2f;
    /// <summary>实机截图用：强制指定钟点（&lt;0 = 正常走时）。</summary>
    public float DebugClock = -1f;

    /// <summary>v2.8: verification switch, hides the falling snow so the shake
    /// check can measure the backdrop on its own.</summary>
    public bool DebugNoSnow;

    /// <summary>v2.8: verification switch, hides the chimney smoke so the
    /// shake check can measure the backdrop on its own.</summary>
    public bool DebugNoSmoke;
    private float _lastDryToast = -99f;

    public JiangqiaoLevel(Main main) { _main = main; }

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
            "1931 年 11 月 4 日 · 中国（中华民国时代）中国大陆地区嫩江江桥北岸",
            "天还没亮，江面冻得能走车。\n天边挂着一弯残月，炮弹落在冰上，声音像撕开一匹布。\n你是东北军的一名通信兵——上级要你把阵地上还活着的人说过的话，带出去。",
            "",
            () =>
            {
                _started = true;
                _hud.ShowTutorial("A / D 移动 · 空格 跳跃 · S 蹲下 · Ctrl 匍匐 · E 记录/捡枪/进屋 · G 手榴弹 · 鼠标左键射击");
                Artillery(0.55f);
            });
    }

    // ================= 构图 =================

    private void BuildSky()
    {
        _sky = new SkyScreen();
        AddChild(_sky);
    }

    /// <summary>
    /// Village in two rows: far row is pure war backdrop (collapsed bones /
    /// distant ruins, cannot enter), near row got houses in all kinds of
    /// damage state, some enterable for looting. Far row lifted up a bit
    /// (lift > 0), drawn smaller and fainter. Layout purposely not tidy:
    /// widths, gaps and damage levels all over the place, a few spots burnt
    /// down to bare charred posts only, like the real Jiangqiao winter.
    /// </summary>
    private void BuildHouses()
    {
        // far row: scenery ruins, cannot enter (lift > 0 = reads further away)
        AddHouse("f1", 460f + Jit(28), "house_posts", 190f, false, 14f);
        AddHouse("f2", 1230f + Jit(42), "house_skeleton_near", 235f, false, 16f);
        AddHouse("f4", 1680f + Jit(45), "house_posts", 145f, false, 19f);
        AddHouse("f3", 3910f + Jit(35), "house_burned_near", 210f, false, 12f);

        // near row: enterable loot spots, the intact one teaches players
        // "the house that still looks whole got the goods" lah
        AddHouse("h1", 845f + Jit(24), "house_ruined_near", 330f, true, 0f);
        AddHouse("h3", 1885f + Jit(30), "house_intact_near", 312f, true, 0f);
        AddHouse("h4", 3180f + Jit(45), "house_posts", 235f, false, 0f);
        AddHouse("h2", 2760f, "house_burned_near", 360f, true, 0f);
    }

    /// <summary>
    /// Smoke + fire for every house. A house that still stands puffs thin
    /// domestic smoke from the chimney, a burnt one chokes out a thick black
    /// column and still has embers crawling in the ashes.
    /// </summary>
    private void AddHouseSmoke(EnterableHouse h, string tex, float x, float w, float lift)
    {
        if (_smokeWorld == null) return;
        var tex2 = GD.Load<Texture2D>($"res://assets/art/props/{tex}.png");
        if (tex2 == null) return;
        var sz = tex2.GetSize();
        float hgt = w * sz.Y / sz.X;

        // source point in texture space (normalised), then mapped to world
        float u = 0.5f, v = 0.4f, size = 0.9f, strength = 0.6f, fire = 0f, rise = 80f;
        var tint = new Color(0.72f, 0.73f, 0.75f);
        switch (tex)
        {
            case "house_intact_near":
                // the painted smoke that used to sit over this chimney is gone
                // now, so the anchor goes right onto the chimney mouth itself
                u = 0.700f; v = 0.147f; size = 0.85f; strength = 0.70f; fire = 0f; rise = 74f;
                tint = new Color(0.82f, 0.85f, 0.89f);
                break;
            case "house_burned_near":
                u = 0.545f; v = 0.615f; size = 1.50f; strength = 1.0f; fire = 0.75f; rise = 145f;
                tint = new Color(0.33f, 0.32f, 0.34f);
                break;
            case "house_skeleton_near":
                u = 0.45f; v = 0.42f; size = 1.60f; strength = 0.88f; fire = 0.42f; rise = 160f;
                tint = new Color(0.38f, 0.38f, 0.41f);
                break;
            case "house_ruined_near":
                u = 0.45f; v = 0.26f; size = 1.05f; strength = 0.62f; fire = 0.22f; rise = 96f;
                tint = new Color(0.58f, 0.59f, 0.62f);
                break;
            default:  // house_posts: nothing left but charred stumps
                u = 0.50f; v = 0.50f; size = 0.90f; strength = 0.55f; fire = 0.30f; rise = 68f;
                tint = new Color(0.46f, 0.46f, 0.49f);
                break;
        }

        float px = x + u * w;
        float py = GroundY - hgt + v * hgt;
        // far row sits deeper in the haze: smaller, dimmer
        float dim = lift > 0f ? 0.6f : 1f;

        _smokeWorld.Add(new SmokeFx.Source
        {
            Pos = new Vector2(px, py),
            Size = size * (lift > 0f ? 0.7f : 1f),
            Strength = strength * dim,
            Fire = fire * dim,
            Rise = rise * (lift > 0f ? 0.7f : 1f),
            Drift = 0.5f + Jit(0.25f),
            Phase = GD.Randf(),
            Tint = tint,
        });

        // the fire itself sits on the ground under the smoke column
        if (fire > 0.01f)
        {
            _smokeWorld.Add(new SmokeFx.Source
            {
                Pos = new Vector2(px, GroundY - 3f),
                Size = size * 0.55f,
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
        var h = new EnterableHouse(id, new Vector2(x, GroundY), tex, w, enterable, lift);
        _game.AddChild(h);
        if (enterable) _houses.Add(h);
        AddHouseSmoke(h, tex, x, w, lift);
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
        // —— west of the start: the cluster the user pointed at ——
        new("village", -640f, 104f, false, 1, new Vector2(0.482f, 0.152f), true),
        new("house_skeleton_near", -395f, 66f, true, 2, new Vector2(0.45f, 0.42f)),
        new("house_posts", -255f, 44f, false, 0, new Vector2(0.50f, 0.50f)),
        new("house_ruined_near", -110f, 74f, true, 0, new Vector2(0.45f, 0.26f)),

        // —— village gate: the one house still standing, chimney still smoking ——
        new("house_intact_near", 285f, 96f, false, 1, new Vector2(0.700f, 0.147f)),
        new("village", 470f, 118f, true, 1, new Vector2(0.482f, 0.152f), true),
        new("house_skeleton_near", 660f, 62f, false, 2, new Vector2(0.45f, 0.42f)),
        new("house_posts", 760f, 40f, true, 0, new Vector2(0.50f, 0.50f)),

        // —— middle and back: keep them sparse, never a continuous row ——
        new("house_ruined_near", 2480f, 70f, true, 0, new Vector2(0.45f, 0.26f)),
        new("house_intact_near", 2990f, 58f, false, 1, new Vector2(0.700f, 0.147f)),
        new("house_posts", 3040f, 38f, true, 0, new Vector2(0.50f, 0.50f)),
        new("house_burned_near", 4180f, 78f, false, 2, new Vector2(0.545f, 0.615f)),
        new("village", 4580f, 88f, true, 1, new Vector2(0.482f, 0.152f), true),
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
                    Size = 0.58f,
                    Strength = 0.72f,
                    Fire = 0f,
                    Rise = 44f,
                    Drift = 0.6f,
                    Phase = i * 0.37f,
                    Tint = new Color(0.40f, 0.40f, 0.44f),
                });
            }
            else
            {
                // chimney of the house that is still standing
                _smokeMid.Add(new SmokeFx.Source
                {
                    Pos = new Vector2(ax, ay),
                    Size = 0.36f,
                    Strength = 0.48f,
                    Fire = 0f,
                    Rise = 30f,
                    Drift = 0.4f,
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
        return (WeaponPickup.Kind.Usable, (int)GD.RandRange(2, 8));
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

        // smoke and fire over the near houses: above the houses, below the fighters
        _smokeWorld = new SmokeFx { ZIndex = -2 };
        _game.AddChild(_smokeWorld);
        if (DebugNoSmoke) _smokeWorld.Visible = false;

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

        var art = new GroundArt(this) { ZIndex = -1 };
        _game.AddChild(art);

        // 尸体（GroundArt 之上、玩家之下）：每场战役都有阵亡者
        AddCorpses();

        // footprint layer at z -1: above the ground art but below corpse/player,
        // prints kena stepped on by the body, not covering it
        _game.AddChild(new FootprintLayer(this) { ZIndex = -1 });

        // 掩体沙袋在人之前（ZIndex 5，高于玩家 0 与敌兵 2），
        // 经过时人从垛子后面过，而不是站在垛子顶上。
        _coverArt = new CoverArt { ZIndex = 5 };
        _game.AddChild(_coverArt);
    }

    private void AddCorpses()
    {
        // 尸体散布点：弹坑旁、战壕边、碉堡前、桥头、铁丝网附近
        // CorpseActor ZIndex = -5（GroundArt 之上、玩家 0 之下）

        // 1. 弹坑旁（日军，身体朝右倒向弹坑）
        _game.AddChild(new CorpseActor(CorpseActor.Faction.IJA,
            new Vector2(1270, GroundY - 2), false, 0.08f));

        // 2. 另一个弹坑（日军，朝向相反）
        _game.AddChild(new CorpseActor(CorpseActor.Faction.IJA,
            new Vector2(2090, GroundY - 2), true, -0.12f));

        // 3. 战壕边（东北军，头倒在战壕方向）
        _game.AddChild(new CorpseActor(CorpseActor.Faction.NortheastArmy,
            new Vector2(1550, GroundY - 2), false, 0.22f));

        // 4. 碉堡前（日军，身体挡在碉堡入口前）
        _game.AddChild(new CorpseActor(CorpseActor.Faction.IJA,
            new Vector2(3000, GroundY - 2), true, -0.18f));

        // 5. 桥头铁路旁（东北军，头朝冰面倒）
        _game.AddChild(new CorpseActor(CorpseActor.Faction.NortheastArmy,
            new Vector2(4380, GroundY - 2), true, 0.14f));
    }

    private void BuildPlayer()
    {
        _playerShadow = new GroundShadow();
        _game.AddChild(_playerShadow);

        _player = new FieldPlayer { Position = new Vector2(230, GroundY - 2) };
        _player.Scale = new Vector2(1.22f, 1.22f);
        _player.Fired += OnPlayerFired;
        _player.Hit += OnPlayerHit;
        _player.Down += OnPlayerDown;
        _player.DryFired += OnDryFired;
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

    private void BuildTestimonies()
    {
        AddTestimony("t1", new Vector2(720, GroundY), "口述 01 · 机枪手 赵永生（化名，时年 24 岁）",
            new[]
            {
                "“十一月四日天没亮，江面冻得能走车。他们的炮先打，冰炸开了，马车过不去。我们连的机枪架在土坎后头，打到第三箱子弹，枪管红了，就用雪捂。”",
                "“后来记不清了。只记得有人在雪里喊我的名字，我说我在这儿。声音传出去，就没再回来。”",
            },
            "依据：江桥抗战公开口述史料改写；人物为复合虚构，不对应单一在世者。");

        AddTestimony("t2", new Vector2(2220, GroundY), "口述 02 · 卫生员 李桂兰（化名，时年 19 岁）",
            new[]
            {
                "“担架不够，棉被撕成条。最冷的时候零下二十几度，血一落地就冻住，要用热水化开才能包扎。”",
                "“有个孩子兵一直问我，我们什么时候能回家。我说等打完这一仗。他说那打完这一仗，家还在吗。”",
            },
            "依据：江桥抗战、东北抗战医疗口述史料改写；人物为复合虚构。");

        AddTestimony("t3", new Vector2(3480, GroundY), "口述 03 · 铁路工人 孙德山（化名，时年 31 岁）",
            new[]
            {
                "“江桥的钢梁是俄国人修的，铆钉一颗一颗敲进去。我们夜里拆掉一段铁轨，想让他们的车过不来。”",
                "“天一亮，他们的飞机来了。铁路没了可以再修，人没了就没了。”",
            },
            "依据：《黑龙江省志·铁路志》与江桥抗战口述史料改写；人物为复合虚构。");
    }

    private void AddTestimony(string id, Vector2 pos, string title, string[] lines, string source)
    {
        var t = new Testimony(this, id, pos, title, lines, source);
        _game.AddChild(t);
        _testimonies.Add(t);
    }

    private void BuildHud()
    {
        _hud = new Hud();
        AddChild(_hud);
        _hud.SetDate("1931 年 11 月 4 日 · 中国（中华民国时代）中国大陆地区嫩江江桥北岸");
        UpdateObjective();

        var layer = new CanvasLayer { Layer = 11 };
        AddChild(layer);
        _hpLabel = new Label
        {
            Text = "生命  ● ● ●",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _hpLabel.AddThemeFontSizeOverride("font_size", 15);
        _hpLabel.AddThemeColorOverride("font_color", new Color(0.86f, 0.82f, 0.72f));
        _hpLabel.Position = new Vector2(1080, 14);
        _hpLabel.CustomMinimumSize = new Vector2(180, 0);
        _hpLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        layer.AddChild(_hpLabel);

        // 弹药改用子弹图标：打掉一发暗一发，拾取了再点亮，不再用点点点
        _ammoBar = new AmmoBar { Position = new Vector2(920, 34) };
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
        // v2.8: verification switch, lets the shake check kill the falling snow
        // so only the backdrop is measured
        if (DebugNoSnow) _snow.Visible = false;

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
        UpdateRecording(f);
        UpdateObjective();
        UpdatePrompts();
        CheckWaves();
        CheckEnding();
    }

    // ================= 昼夜 =================

    /// <summary>
    /// 游戏钟推进 + 天色。日月位置与月相全部来自 1931-11-04 的实算数据：
    /// 凌晨残月（39% 照亮）挂在东边，06:32 日出，16:29 日落，入夜无月只有星。
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
        else                              // night falls, no moon tonight (月已落)
            tint = new Color(0.40f, 0.44f, 0.58f);
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

    private void UpdateInteractions()
    {
        if (_houseUi != null) return;
        float px = _player.GlobalPosition.X;

        // enter house?
        EnterableHouse nearHouse = null;
        foreach (var h in _houses)
            if (Mathf.Abs(h.DoorX - px) < 78f) { nearHouse = h; break; }

        // loot rifle?
        WeaponPickup nearPickup = null;
        float bestP = 64f;
        foreach (var p in _pickups)
        {
            if (p.Looted || !IsInstanceValid(p)) continue;
            float d = Mathf.Abs(p.Position.X - px);
            if (d < bestP) { bestP = d; nearPickup = p; }
        }

        if (Input.IsActionJustPressed("interact"))
        {
            if (nearHouse != null) OpenHouse(nearHouse);
            else if (nearPickup != null) LootPickup(nearPickup);
        }
    }

    private void LootPickup(WeaponPickup p)
    {
        p.MarkLooted();
        if (p.Condition == WeaponPickup.Kind.Broken)
        {
            _hud.Toast("枪栓冻死了，拉不开——这支打不响。");
            return;
        }
        _player.Ammo += p.Ammo;
        if (_player.Ammo > _player.AmmoMax) _player.AmmoMax = _player.Ammo;
        _hud.Toast($"捡获一支步枪，里面还剩 {p.Ammo} 发。");
        Shake(0.06f);
    }

    private void OpenHouse(EnterableHouse h)
    {
        string title = h.Id switch
        {
            "h1" => "江边村舍（半毁）",
            "h3" => "没打完的民居（还算完整）",
            _ => "烧塌的院落",
        };
        var ui = new HouseInteriorUi(h.Id, title);
        ui.GotAmmo += n => { _player.Ammo += n; if (_player.Ammo > _player.AmmoMax) _player.AmmoMax = _player.Ammo; };
        ui.GotGrenade += n => _player.Grenades += n;
        ui.Closed += () => _houseUi = null;
        AddChild(ui);
        _houseUi = ui;
    }

    private void UpdateGrenades(float f)
    {
        if (_noteOpen || _houseUi != null || _ended) return;
        if (Input.IsActionJustPressed("grenade") && _player.Grenades > 0)
        {
            _player.Grenades--;
            var g = new GrenadeProj(_player.GlobalPosition + new Vector2(_player.Facing * 14f, -46f),
                _player.Facing);
            _game.AddChild(g);
            _grenades.Add(g);
            _hud.Toast("手榴弹出手——卧倒！");
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
        if (_player.GlobalPosition.DistanceTo(at) < 120f)
            _player.TakeHit(at.X);
    }

    /// <summary>玩家是否正躲在沙袋掩体后（蹲/趴才算真掩护）。</summary>
    public bool PlayerInCover()
    {
        if (_player == null || !_player.IsOnFloor()) return false;
        bool low = Input.IsActionPressed("crouch") || Input.IsActionPressed("prone");
        if (!low) return false;
        float px = _player.GlobalPosition.X;
        return _coverArt != null && _coverArt.Covers(px);
    }

    private void UpdateRecording(float f)
    {
        Testimony near = null;
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

    private void Record(Testimony t)
    {
        t.Recorded = true;
        GameState.I.S.JiangqiaoTestimony.Add(t.Id);
        GameState.I.Save();
        _checkpoint = new Vector2(Mathf.Max(220f, t.GlobalPosition.X - 180f), GroundY - 2f);
        _hud.Toast($"已记录口述  {Mathf.Min(GameState.I.S.JiangqiaoTestimony.Count, 3)} / 3");

        var note = new FieldNoteUi("嫩江江桥 · 1931 年 11 月 4 日", t.Title, t.Lines, t.Source, () =>
        {
            _noteOpen = false;
        });
        _noteOpen = true;
        AddChild(note);
        UpdateObjective();
    }

    private void UpdateObjective()
    {
        int n = Mathf.Min(GameState.I.S.JiangqiaoTestimony.Count, 3);
        string tail = n >= 3 ? "三份口述已齐——沿铁路向东，送出记录。" : "沿阵地向东，记录阵地上还活着的人（按住 E）。";
        _hud.SetObjective($"已记录 {n} / 3    ·    {tail}");
    }

    private void UpdatePrompts()
    {
        float px = _player.GlobalPosition.X;

        EnterableHouse nearHouse = null;
        foreach (var h in _houses)
            if (Mathf.Abs(h.DoorX - px) < 78f) { nearHouse = h; break; }

        WeaponPickup nearPickup = null;
        float bestP = 64f;
        foreach (var p in _pickups)
        {
            if (p.Looted || !IsInstanceValid(p)) continue;
            float d = Mathf.Abs(p.Position.X - px);
            if (d < bestP) { bestP = d; nearPickup = p; }
        }

        Testimony near = null;
        float bestT = 86f;
        foreach (var t in _testimonies)
        {
            if (t.Recorded) continue;
            float d = Mathf.Abs(t.GlobalPosition.X - px);
            if (d < bestT) { bestT = d; near = t; }
        }

        if (nearHouse != null)
            _hud.ShowPrompt("按 E · 进屋搜索");
        else if (nearPickup != null)
            _hud.ShowPrompt(nearPickup.Condition == WeaponPickup.Kind.Broken
                ? "按 E · 捡起步枪（可能打不响）"
                : "按 E · 捡枪拿子弹");
        else if (near != null)
            _hud.ShowPrompt("按住 E · 记录口述");
        else if (GameState.I.S.JiangqiaoTestimony.Count >= 3 && px > 4200f)
            _hud.ShowPrompt("向东 · 走上江桥");
        else
            _hud.ShowPrompt("");

        // 右上生命 / 弹药（图标条自己重绘） / 手榴弹
        string dots = _player.Hp >= 3 ? "● ● ●" : _player.Hp == 2 ? "● ● ○" : _player.Hp == 1 ? "● ○ ○" : "○ ○ ○";
        _hpLabel.Text = $"生命  {dots}    手榴弹 ×{_player.Grenades}";

        // 目标在屏幕外时给纸色箭头
        Vector2 target = near != null
            ? near.GlobalPosition
            : new Vector2(4800, GroundY);
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
        _hud.Toast("前方雪梁后出现关东军尖兵");
    }

    private void SpawnEnemy(float x, float lane)
    {
        if (x > WorldW - 120f) x = WorldW - 120f;
        var e = new EnemySoldier(this, new Vector2(x, GroundY), lane);
        // 敌人体型与玩家统一（玩家 1.22f），景深用 Modulate 淡化
        e.Scale = new Vector2(1.22f, 1.22f);
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
        if (PlayerX < 4780f) return;
        if (GameState.I.S.JiangqiaoTestimony.Count < 3)
        {
            if (_elapsed - _lastToast > 3.2f)
            {
                _lastToast = _elapsed;
                _hud.Toast($"记录还不完整：{GameState.I.S.JiangqiaoTestimony.Count} / 3");
            }
            return;
        }

        _ended = true;
        GameState.I.S.JiangqiaoDone = true;
        GameState.I.Save();
        _player.ControlLocked = true;
        _blackTarget = 0.65f;
        _hud.ShowPrompt("");

        var end = new LevelEndUi(_main);
        AddChild(end);
    }

    // ================= 战斗与受击 =================

    private void OnPlayerFired(Vector2 origin, bool faceRight)
    {
        Shake(0.55f);
        EnemySoldier target = null;
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
    public void EnemyDied(EnemySoldier e)
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
        Shake(0.5f);
        _flashA = Mathf.Max(_flashA, 0.30f);
        _hud.Toast("你中弹了——蹲下能减少暴露");
    }

    private void OnDryFired()
    {
        if (_elapsed - _lastDryToast < 1.2f) return;
        _lastDryToast = _elapsed;
        _hud.Toast("弹仓空了——去尸体边捡枪，或者进屋搜弹药。");
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

    public void EnemyFire(EnemySoldier e)
    {
        if (!PlayerAlive || _ended || !_started) return;

        var origin = e.Position + new Vector2(-46, -66);
        Vector2 to = _player.GlobalPosition + new Vector2(0, -38);
        bool crouching = Input.IsActionPressed("crouch") && _player.IsOnFloor();
        bool cover = PlayerInCover();
        // 沙袋后面趴着基本打不中（真实掩体），站着挨打最惨
        float hitChance = cover ? 0.04f : crouching ? 0.10f : 0.28f;
        bool hit = GD.Randf() < hitChance;

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
        _flashA = Mathf.Max(_flashA, strength);
        Shake(0.7f * strength + 0.2f);
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
        /// 1931-11-04 江桥上空（46.9N 123.4E，日出 06:32 / 日落 16:29，程序实算）。
        /// 凌晨挂 39% 残月，白天冬日低太阳，入夜月已落、只剩星空。
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
                QueueRedraw();
            }

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
                float sr = JiangqiaoLevel.SunriseH, ss = JiangqiaoLevel.SunsetH;
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
                float sr = JiangqiaoLevel.SunriseH, ss = JiangqiaoLevel.SunsetH;
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

                // ---- moon: waning crescent, 39% lit, east sky before dawn ----
                // (real ephemeris: it set 13:42 that day, so no moon at night in this mission)
                if (t < sr + 1.1f)
                {
                    float fade = t < sr ? 1f : Mathf.Clamp(1f - (t - sr) / 1.1f, 0f, 1f);
                    var moon = new Vector2(s.X * 0.68f, 168f);
                    DrawTextureRect(_moonGlow, new Rect2(moon - new Vector2(96, 96), new Vector2(192, 192)), false);
                    float r = 21f;
                    DrawCircle(moon, r, new Color(0.87f, 0.89f, 0.93f, 0.95f * fade));
                    // terminator carve: lit limb faces east (right), shadow bites from the left
                    float d = (1f - 2f * JiangqiaoLevel.MoonLit) * r;
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
        public const int KindFar = 0, KindMid = 1, KindNear = 2, KindFore = 3;

        private readonly JiangqiaoLevel _lvl;
        public int Kind { get; }
        public float Factor { get; }

        public Backdrop(JiangqiaoLevel lvl, int kind, float factor, int z)
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
                default: DrawFore(); break;
            }
        }

        // generated backdrop art, same watercolor manga style, one texture per layer
        private readonly System.Collections.Generic.Dictionary<string, Texture2D> _texCache = new();

        public override void _Ready()
        {
            // preload so first _Draw never hitches
            // v2.7: "willow" no longer preloads, the procedural willow clump replaced it
            foreach (var n in new[] { "mountains", "tree_elm", "tree_birch", "grass" })
                Tex(n);
            // v2.8: the distant village reuses the side-on house art
            foreach (var n in new[] { "village", "house_intact_near", "house_ruined_near",
                                      "house_skeleton_near", "house_burned_near", "house_posts" })
                VillageHouseTex(n, n == "village");
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
                var g = VGradient(new Color(0f, 0f, 0f, 0f), new Color(0.775f, 0.795f, 0.795f, fade));
                DrawTextureRect(g, new Rect2(baseCenter.X - w * 0.5f, baseCenter.Y - h * fade * 1.6f, w, h * fade * 1.6f), false);
            }
        }

        // —— 远山：三层山脊，越远越淡（空气透视），山脊线用值噪声做出真锯齿 ——
        private static Rect2 Full(float x, float y, float h) => new(x, y, WorldW + 3800f, h);

        private void DrawFar()
        {
            // far range: generated watercolor panorama, mirror-tiled so seams match
            var mtn = Tex("mountains");
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
                RidgeSnow(r1, new Color(0.93f, 0.96f, 0.97f, 0.10f), lineW: 1.4f, lenMax: 1);
                RidgeSnow(r2, new Color(0.88f, 0.92f, 0.94f, 0.05f), lineW: 1.2f, lenMax: 1);
                CrestLine(r1, new Color(0.95f, 0.97f, 0.98f, 0.55f), 1.6f);
                CrestLine(r2, new Color(0.92f, 0.95f, 0.96f, 0.45f), 1.3f);
            }

            // 山脚雾：一条窄带，只把山脚和雪原接起来
            var haze = VGradient(new Color(0.82f, 0.86f, 0.87f, 0f), new Color(0.82f, 0.86f, 0.87f, 0.72f));
            DrawTextureRect(haze, Full(-1400, 542, 62), false);
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

        private static GradientTexture2D VGradient(Color from, Color to)
        {
            var g = new Gradient();
            g.SetColor(0, from);
            g.SetColor(1, to);
            return new GradientTexture2D
            {
                Gradient = g,
                Width = 8,
                Height = 256,
                FillFrom = new Vector2(0.5f, 0f),
                FillTo = new Vector2(0.5f, 1f),
            };
        }

        // —— 中景：雪原、村落、电报杆、树线、烟 ——
        private void DrawMid()
        {
            var snow = new Color(0.775f, 0.795f, 0.795f);

            // 雪原上缘：先渐变到实色，避免一条生硬的地平线
            var fade = VGradient(new Color(snow, 0f), new Color(snow, 1f));
            DrawRect(Full(-1400, 578, 240), snow);

            // 雪原起伏的亮边与投影
            // v2.9: tone it down. At 0.65 alpha this crest also ran as a
            // full-width white line behind the village row, stacking with the
            // ground one into two horizon stripes.
            var crest = new List<Vector2>();
            for (float x = -1400; x <= WorldW + 2400; x += 70)
                crest.Add(new Vector2(x, 574 + Mathf.Sin(x * 0.0031f) * 6f + Mathf.Sin(x * 0.011f) * 3f));
            DrawPolyline(crest.ToArray(), new Color(0.88f, 0.91f, 0.92f, 0.26f), 1.6f, true);
            // 只留一条极淡的投影带，别做成一排平行线（会像贴纸）
            var under = VGradient(new Color(0.60f, 0.67f, 0.71f, 0.18f), new Color(0.60f, 0.67f, 0.71f, 0f));
            DrawTextureRect(under, Full(-1400, 578, 26), false);

            // 风蚀纹：长而软的灰度条，让雪原不是一整片死白
            for (int i = 0; i < 52; i++)
            {
                float x = -1300 + i * 190 + Mathf.Sin(i * 2.6f) * 58f;
                float y = 592 + (i * 37 % 44);
                float w = 86 + (i * 53 % 120);
                DrawLine(new Vector2(x, y), new Vector2(x + w, y + Mathf.Sin(i * 1.7f) * 5f),
                    new Color(0.62f, 0.69f, 0.73f, 0.10f), 3f + (i % 3), true);
            }

            // Snow haze first, THEN the village.
            //
            // v2.8: the haze used to be painted after the houses, so that opaque
            // white band (y 520~578) swallowed most of every roof and the row
            // came out as a thin smear. Distance haze belongs behind the
            // village anyway — the fog is out on the plain, the houses stand
            // in front of it.
            DrawTextureRect(fade, Full(-1400, 520, 58), false);

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
                var melt = VGradient(new Color(0.775f, 0.795f, 0.795f, 0f),
                    new Color(0.775f, 0.795f, 0.795f, 0.95f));
                DrawTextureRect(melt,
                    new Rect2(vh.X - wid * 0.5f, VillageBaseY - vh.H * 0.22f, wid, vh.H * 0.22f), false);
                DrawSetTransform(new Vector2(vh.X, VillageBaseY), 0, new Vector2(wid * 0.30f, 0.13f));
                DrawTextureRect(SoftBlob(new Color(0.80f, 0.82f, 0.83f), 0.80f),
                    new Rect2(-20f, -20f, 40f, 40f), false);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }

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
                    Sprite("tree_elm", new Vector2(tx, 578f), hBase, 0.75f,
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

        // —— 近景：枯树带、灌木柳丛、雪坎、铁丝网、废车（一律低于人物腰部，绝不遮脸）——
        private void DrawNear()
        {
            // near band: generated elm / willow sprites, fixed seed, sparse gaps
            var nearRng = new RandomNumberGenerator();
            nearRng.Seed = 19311105;
            float nx = -1400f;
            while (nx < WorldW + 1200)
            {
                float gap = 280f + nearRng.Randf() * 140f;
                float kind = nearRng.Randf();
                if (kind < 0.5f)
                    Sprite("tree_elm", new Vector2(nx, 672f), 60f + nearRng.Randf() * 60f, 1f,
                        flip: nearRng.Randf() < 0.5f);
                else if (kind < 0.85f)
                {
                    // the willow clump is now procedural: one clump of arching twigs.
                    // before this it used the willow.png texture, but that image
                    // carries a whole white snow base, and scaled down to 26~48px
                    // it became a pile of black spikes radiating out of the
                    // ground, very jarring on open snow - that is the odd object
                    // the user pointed at in picture 7.
                    // the procedural version is twigs hugging the ground plus
                    // snow at the roots, no hard edges.
                    WillowClump(nx, 668f, 24f + nearRng.Randf() * 20f);
                }
                nx += gap;
            }
            // three taller lone trees to break the rhythm
            foreach (var (tx2, th) in new[] { (-820f, 138f), (2460f, 112f), (4900f, 158f) })
                Sprite("tree_elm", new Vector2(tx2, 676f), th, 1f, flip: tx2 < 0f);

            // 雪坎：软边（三层同心椭圆），不是一个个白泡泡
            for (int i = 0; i < 22; i++)
            {
                var c = new Vector2(-1300 + i * 410, 626);
                Drift(c, 130f + (i % 3) * 34f, 30f + (i % 2) * 8f, new Color(0.92f, 0.94f, 0.94f));
            }

            // 铁丝网：木桩 + 三道下垂铁丝（几处断口，不铺满整条路）
            foreach (float seg in new[] { -980f, 880f, 2540f, 3820f })
            for (float x0 = seg; x0 < seg + 600; x0 += 300)
            {
                for (int p = 0; p < 6; p++)
                {
                    float x = x0 + p * 50;
                    DrawLine(new Vector2(x, 668), new Vector2(x - 3, 616), new Color(0.28f, 0.26f, 0.24f), 2.6f, true);
                    DrawCircle(new Vector2(x - 3, 616), 2f, new Color(0.90f, 0.92f, 0.92f, 0.7f));
                    if (p < 5)
                        for (int w = 0; w < 3; w++)
                        {
                            float y = 622 + w * 15;
                            var wire = Curved(new Vector2(x, y), new Vector2(x + 25, y + 9), new Vector2(x + 50, y), 4);
                            DrawPolyline(wire, new Color(0.26f, 0.25f, 0.23f, 0.6f), 1f, true);
                        }
                }
            }

            // 废弃的辎重车（半埋进雪里）
            var cx = 1760f;
            DrawRect(new Rect2(cx, 592, 74, 24), new Color(0.40f, 0.32f, 0.23f));
            DrawRect(new Rect2(cx, 592, 74, 24), new Color(0.26f, 0.21f, 0.16f, 0.7f), false, 1.2f);
            DrawRect(new Rect2(cx - 4, 584, 86, 8), new Color(0.92f, 0.94f, 0.94f, 0.9f));
            DrawCircle(new Vector2(cx + 14, 620), 11f, new Color(0.24f, 0.22f, 0.20f));
            DrawCircle(new Vector2(cx + 14, 620), 4f, new Color(0.48f, 0.42f, 0.34f));
            DrawCircle(new Vector2(cx + 60, 620), 11f, new Color(0.24f, 0.22f, 0.20f));
            DrawLine(new Vector2(cx + 70, 592), new Vector2(cx + 104, 570), new Color(0.36f, 0.29f, 0.20f), 4f, true);
            DrawLine(new Vector2(cx + 68, 589), new Vector2(cx + 104, 567), new Color(0.90f, 0.92f, 0.92f, 0.7f), 2f, true);
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
        => SoftSnow.Blob(tint, alpha);

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
            DrawTextureRect(SoftSnow.Blob(new Color(0.95f, 0.96f, 0.97f), 0.62f),
                new Rect2(-20f, -20f, 40f, 40f), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        // snow at the base: a soft edged ellipse that buries the roots
        DrawSetTransform(new Vector2(x, baseY + 1f), 0, new Vector2(1.5f, 0.26f));
        DrawTextureRect(SoftSnow.Blob(new Color(0.95f, 0.96f, 0.97f), 0.78f),
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

            var snow = new Color(0.93f, 0.95f, 0.95f, 0.8f);
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
            DrawTextureRect(SoftSnow.Blob(new Color(0.12f, 0.13f, 0.16f), a),
                new Rect2(-20f, -20f, 40f, 40f), false);
            DrawSetTransform(new Vector2(X - skew * 2.1f, GroundY - 1f), 0, new Vector2(1f, 0.15f));
            DrawTextureRect(SoftSnow.Blob(new Color(0.12f, 0.13f, 0.16f), a * 0.5f),
                new Rect2(-20f, -20f, 40f, 40f), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }

    /// <summary>地面、铁路、弹坑、江桥：全部画在玩法层之下，但按前后顺序叠。</summary>
    private partial class GroundArt : Node2D
    {
        private readonly JiangqiaoLevel _l;

        // —— 预计算的随机参数（全部在 _Ready 生成一次，_Draw 不做 Randf）——
        private float[] _craterX = System.Array.Empty<float>();
        private float[] _craterR = System.Array.Empty<float>();
        private float[] _crateX = System.Array.Empty<float>();     // 弹药木箱：位置/高矮/歪斜每只不同
        private float[] _crateH = System.Array.Empty<float>();
        private float[] _crateRot = System.Array.Empty<float>();
        private float[] _bunkerX = System.Array.Empty<float>();
        private float[] _bunkerBW = System.Array.Empty<float>();
        private float[] _bunkerFH = System.Array.Empty<float>();
        private int[] _railSkipIdx = System.Array.Empty<int>();       // 被去掉的枕木序号（破损感）
        private int[] _trussSkipDiags = System.Array.Empty<int>();    // 桥桁架被去掉的斜腹杆
        private int[] _pierDamageIdx = System.Array.Empty<int>();     // 被炮弹蹭过的桥墩（画烧黑斑）

        public GroundArt(JiangqiaoLevel l) { _l = l; }

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

            // 弹坑位置 ±20、半径 ±6
            var craterListX = new List<float>();
            var craterListR = new List<float>();
            foreach (float x in new[] { 1260f, 1640f, 2080f, 2480f, 2890f, 3260f, 3720f, 4060f })
            {
                craterListX.Add(x + rng.RandfRange(-22f, 22f));
                craterListR.Add(26f + rng.RandfRange(-6f, 15f));
            }
            _craterX = craterListX.ToArray();
            _craterR = craterListR.ToArray();

            // 弹药木箱：错开沙包垛位摆放，每只高矮/歪斜略不同（不再像贴图一样复制叠上去）
            var crateListX = new List<float>();
            var crateListH = new List<float>();
            var crateListRot = new List<float>();
            foreach (float x in new[] { 730f, 1450f, 2680f, 3960f })
            {
                crateListX.Add(x + rng.RandfRange(-18f, 18f));
                crateListH.Add(30f + rng.RandfRange(-3f, 4f));
                crateListRot.Add(rng.RandfRange(-0.10f, 0.10f));
            }
            _crateX = crateListX.ToArray();
            _crateH = crateListH.ToArray();
            _crateRot = crateListRot.ToArray();

            // 碉堡位置 ±18、宽 ±6、高 ±5
            var bxList = new List<float>();
            var bwList = new List<float>();
            var fhList = new List<float>();
            foreach (float bx in new[] { 1550f, 3050f, 4050f })
            {
                bxList.Add(bx + rng.RandfRange(-18f, 18f));
                bwList.Add(56f + rng.RandfRange(-4f, 8f));
                fhList.Add(38f + rng.RandfRange(-5f, 5f));
            }
            _bunkerX = bxList.ToArray();
            _bunkerBW = bwList.ToArray();
            _bunkerFH = fhList.ToArray();

            // 铁路：随机挑 8~12 根枕木去掉（每 60 根里去掉 1 根 = 稀疏破损）
            var railSkip = new HashSet<int>();
            int railCount = (int)(5200f / 34f);  // ≈ 152 根
            for (int k = 0; k < 22; k++) railSkip.Add((int)rng.RandfRange(0, railCount));
            _railSkipIdx = railSkip.ToArray();

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
            DrawColoredPolygon(ground.ToArray(), new Color(0.855f, 0.875f, 0.875f));
            // v2.9: the old edge highlight was near-white at 0.9 alpha, so a
            // crisp white line ran the full width of the level straight behind
            // every character. Soften it into the snow fill: same line, but a
            // tone close to the surface and low alpha, just enough to suggest
            // a wind-packed crest instead of drawing a horizon stripe.
            DrawPolyline(surface.ToArray(), new Color(0.92f, 0.945f, 0.945f, 0.4f), 2f, true);
            DrawPolyline(surface.Select(p => p + new Vector2(0, 8)).ToArray(),
                new Color(0.70f, 0.75f, 0.78f, 0.32f), 5f, true);

            // 冰面裂缝与车辙
            for (int i = 0; i < 26; i++)
            {
                float x = 60 + i * 190;
                float y = GroundY + 26 + (i % 3) * 12;
                DrawLine(new Vector2(x, y), new Vector2(x + 70, y + 8), new Color(0.72f, 0.78f, 0.82f, 0.35f), 1.4f, true);
            }

            DrawRiverIce();
            DrawRailway();
            DrawCraters();
            DrawBridge();
            DrawTrenchLines();
        }

        private void DrawRiverIce()
        {
            // 桥下的江面（冰封）
            var ice = new[]
            {
                new Vector2(4460, GroundY), new Vector2(5220, GroundY), new Vector2(5220, 780),
                new Vector2(4460, 780),
            };
            DrawColoredPolygon(ice, new Color(0.72f, 0.79f, 0.82f));
            // 冰面上层：更亮的冰壳（半透明白）
            DrawColoredPolygon(new[]
            {
                new Vector2(4460, GroundY), new Vector2(5220, GroundY),
                new Vector2(5220, GroundY + 28), new Vector2(4460, GroundY + 28),
            }, new Color(0.88f, 0.92f, 0.93f, 0.5f));

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
                DrawPolyline(crack.ToArray(), new Color(0.50f, 0.58f, 0.64f, 0.42f), 1.8f, true);
                // 裂缝上的高光细线
                DrawPolyline(crack.ToArray(), new Color(0.88f, 0.93f, 0.96f, 0.25f), 0.8f, true);
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
                DrawPolyline(crack.ToArray(), new Color(0.48f, 0.56f, 0.62f, 0.38f), 1.6f, true);
            }

            // 冰上的雪块（冻在冰面上）
            for (int i = 0; i < 14; i++)
            {
                var c = new Vector2(4485 + i * 52 + Mathf.Sin(i * 1.9f) * 14f,
                    GroundY + 24 + (i % 4) * 22);
                DrawSetTransform(c, 0, new Vector2(1.6f, 0.28f));
                DrawTextureRect(SoftSnow.Blob(new Color(0.93f, 0.95f, 0.95f), 0.75f),
                    new Rect2(-20f, -20f, 40f, 40f), false);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
            // 远处冰面上的冻土堆（小冰丘）
            for (int i = 0; i < 5; i++)
            {
                var c = new Vector2(4520 + i * 140, GroundY + 540);
                DrawSetTransform(c, 0, new Vector2(1.8f, 0.5f));
                DrawTextureRect(SoftSnow.Blob(new Color(0.78f, 0.83f, 0.86f), 0.6f),
                    new Rect2(-20f, -20f, 40f, 40f), false);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
        }

        private void DrawRailway()
        {
            var skip = new HashSet<int>(_railSkipIdx);
            float railY = GroundY - 1f;

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

            // ballast bed: packed darker snow with a gently wavy top edge so
            // it doesn't read as a ruler-straight line drawn on white paper
            var ballast = new List<Vector2>();
            for (float x = 2380f; x <= WorldW + 60f; x += 38f)
                ballast.Add(new Vector2(x, railY + 2f + Mathf.Sin(x * 0.021f) * 1.6f - Mathf.Sin(x * 0.053f) * 0.8f));
            ballast.Add(new Vector2(WorldW + 60f, railY + 22f));
            ballast.Add(new Vector2(2380f, railY + 22f));
            DrawColoredPolygon(ballast.ToArray(), new Color(0.73f, 0.755f, 0.765f, 0.5f));
            // soft top-edge highlight following the same wave
            var topEdge = new List<Vector2>();
            for (float x = 2380f; x <= WorldW + 60f; x += 38f)
                topEdge.Add(new Vector2(x, railY + 2f + Mathf.Sin(x * 0.021f) * 1.6f - Mathf.Sin(x * 0.053f) * 0.8f));
            DrawPolyline(topEdge.ToArray(), new Color(0.90f, 0.925f, 0.93f, 0.7f), 2.6f, false);

            int k = 0;
            for (float x = 2400; x < WorldW; x += 34, k++)
            {
                if (skip.Contains(k) || OnRailCrater(x + 11f)) continue;
                // each sleeper got own height/brightness jitter (deterministic,
                // picture stays stable between frames)
                float jy = Mathf.Sin(k * 12.9898f) * 2.2f;
                float shade = 0.40f + 0.06f * Mathf.Sin(k * 3.7f);
                var wood = new Color(shade, shade * 0.80f, shade * 0.60f);
                var r = new Rect2(x, railY + 3 + jy, 22, 13);
                // sleeper: ink frame then wood fill, the hand-drawn manga way
                InkStyle.InkRect(this, r, wood, InkStyle.InkColor, InkStyle.Stroke);
                // shadow edge, lower-left away from the light
                InkStyle.InkRect(this, new Rect2(x - 2, r.Position.Y + 7, 22, 5),
                    new Color(0.26f, 0.20f, 0.14f, 0.8f), InkStyle.InkSoft, InkStyle.ThinStroke);
                // thin snow on the sleeper top for ~60% of them, phase-shifted
                // so it don't read as a copied row
                if (Mathf.Sin(k * 7.31f) > -0.2f)
                    DrawLine(new Vector2(x + 2, r.Position.Y + 1.5f),
                        new Vector2(x + 20, r.Position.Y + 1.2f),
                        new Color(0.93f, 0.95f, 0.95f, 0.8f), 2.0f, true);
            }

            // two parallel rails: broken at the craters, drawn as short intact
            // segments with steel sheen. snow caps sit on top in short patches.
            foreach (float ry in new[] { railY, railY + 12 })
            {
                // ink base, segmented around the blast gaps
                float segStart = 2400f;
                for (float x = 2400f; x <= WorldW; x += 10f)
                {
                    if (OnRailCrater(x))
                    {
                        if (x - segStart > 4f)
                            DrawLine(new Vector2(segStart, ry), new Vector2(x, ry), InkStyle.InkColor, InkStyle.BoldStroke, true);
                        segStart = x + 10f;
                    }
                }
                if (WorldW - segStart > 4f)
                    DrawLine(new Vector2(segStart, ry), new Vector2(WorldW, ry), InkStyle.InkColor, InkStyle.BoldStroke, true);

                // steel sheen only on the intact lengths
                segStart = 2400f;
                for (float x = 2400f; x <= WorldW; x += 10f)
                {
                    if (OnRailCrater(x))
                    {
                        if (x - segStart > 4f)
                            DrawLine(new Vector2(segStart, ry), new Vector2(x, ry), new Color(0.85f, 0.86f, 0.84f), 1.6f, true);
                        segStart = x + 10f;
                    }
                }
                if (WorldW - segStart > 4f)
                    DrawLine(new Vector2(segStart, ry), new Vector2(WorldW, ry), new Color(0.85f, 0.86f, 0.84f), 1.6f, true);
            }

            // 轨顶积雪：短促的亮段（不是通条白线，雪是被风一段段吹积的），避开被炸断处
            for (float sx = 2420; sx < WorldW; sx += 88)
            {
                if (OnRailCrater(sx)) continue;
                float seg = 14f + Mathf.Sin(sx * 0.07f) * 8f;
                DrawLine(new Vector2(sx, railY - 1.2f), new Vector2(sx + seg, railY - 1.2f),
                    new Color(0.96f, 0.97f, 0.97f, 0.85f), 2.4f, true);
            }

            // scattered gravel / ice specks on the ballast so it isn't a flat wash
            for (float gx = 2410; gx < WorldW; gx += 57f)
            {
                if (OnRailCrater(gx)) continue;
                float gy = railY + 8f + Mathf.Sin(gx * 0.13f) * 4f;
                DrawCircle(new Vector2(gx, gy), 1.4f, new Color(0.62f, 0.64f, 0.66f, 0.7f));
            }
        }

        private Texture2D _craterTex;

        private void DrawCraters()
        {
            // craters are generated watercolor textures now. The old stacked
            // ellipses read like a flat bullseye pancake, totally off from the
            // hand-drawn style of everything else on the field.
            _craterTex ??= GD.Load<Texture2D>("res://assets/art/props/crater.png");
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
                float hEff = w * tsz.Y / tsz.X * 0.45f;
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
            _bridgeTex ??= GD.Load<Texture2D>("res://assets/art/props/bridge.png");
            if (_bridgeTex != null)
            {
                var bsz = _bridgeTex.GetSize();
                float bh = 420f;
                float bw = bh * bsz.X / bsz.Y;
                float bx = start + (end - start - bw) * 0.5f;
                // v3.0 seat fix: measured the texture with a pixel scan - the
                // deck / bottom chord sits at 53% of the texture height, piers
                // run down to ~87%. The old rect parked the texture bottom at
                // GroundY+168, which left the chord floating 30px ABOVE the
                // rail line - that gap was the "levitating bridge". Now the
                // chord top lands exactly on railY so the rails run onto the
                // deck and the pier feet bury below the ice.
                DrawTextureRect(_bridgeTex, new Rect2(bx, GroundY - 2f - 0.53f * bh, bw, bh), false);

                // v2.9 float fix, part 1: west abutment. The truss used to just
                // stop in mid-air at the span start - no support, no ground
                // contact, whole bridge read like it was levitating. A concrete
                // abutment under the west end seats the truss into the ground.
                var abut = new[]
                {
                    new Vector2(start - 14f, GroundY - 46f),
                    new Vector2(start + 62f, GroundY - 46f),
                    new Vector2(start + 70f, GroundY + 34f),
                    new Vector2(start - 22f, GroundY + 34f),
                };
                DrawColoredPolygon(abut, new Color(0.40f, 0.42f, 0.44f));
                DrawPolyline(abut, new Color(0.20f, 0.21f, 0.22f, 0.85f), 1.6f, true);
                // abutment shading + snow cap, same manga treatment as the piers
                DrawColoredPolygon(new[]
                {
                    new Vector2(start - 14f, GroundY - 46f),
                    new Vector2(start - 4f, GroundY - 46f),
                    new Vector2(start - 10f, GroundY + 34f),
                    new Vector2(start - 22f, GroundY + 34f),
                }, new Color(0.28f, 0.30f, 0.32f, 0.7f));
                DrawLine(new Vector2(start - 18f, GroundY - 46f), new Vector2(start + 60f, GroundY - 46f),
                    new Color(0.94f, 0.96f, 0.96f, 0.9f), 3f, true);
                // fresh snow line on the deck edge, ties the texture into the scene
                DrawLine(new Vector2(start + 6f, GroundY - 6f), new Vector2(end - 6f, GroundY - 6f),
                    new Color(0.92f, 0.94f, 0.94f, 0.75f), 3f, true);

                // v2.9 float fix, part 2: bury the texture bottom edge. The pier
                // feet ended on a hard horizontal cut against the snow - every
                // support read as "standing on the snow, not in it". Soft drift
                // blobs melt the base line into the ice and snow.
                // pier feet now land at ~GroundY+141 with the new seat, so the
                // drift blobs bury that cut line where the feet meet the ice
                for (float dx2 = start + 90f; dx2 < end - 40f; dx2 += 150f)
                {
                    DrawSetTransform(new Vector2(dx2, GroundY + 138f), 0, new Vector2(2.1f, 0.42f));
                    DrawTextureRect(SoftSnow.Blob(new Color(0.90f, 0.93f, 0.94f), 0.75f),
                        new Rect2(-20f, -20f, 40f, 40f), false);
                    DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                }
                // contact shading where the span crosses the near snowfield
                DrawSetTransform(new Vector2(start + 130f, GroundY + 12f), 0, new Vector2(3.2f, 0.5f));
                DrawTextureRect(SoftSnow.Blob(new Color(0.55f, 0.60f, 0.64f), 0.30f),
                    new Rect2(-20f, -20f, 40f, 40f), false);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
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
                    DrawTextureRect(SoftSnow.Blob(new Color(0.88f, 0.91f, 0.92f), 0.5f),
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
                    DrawTextureRect(SoftSnow.Blob(new Color(0.95f, 0.97f, 0.97f), 0.7f),
                        new Rect2(-cw * 0.55f, -6f, cw * 1.1f, 12f), false);
                    DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                    // snow mound at the base front
                    DrawSetTransform(new Vector2(x + 6f, GroundY + 1f), 0, new Vector2(1.4f, 0.4f));
                    DrawTextureRect(SoftSnow.Blob(new Color(0.94f, 0.96f, 0.96f), 0.55f),
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

            // 碉堡（梯形混凝土 + 射击孔 + 积雪）：1931 江桥阵地的土木碉堡
            for (int bi = 0; bi < _bunkerX.Length; bi++)
            {
                float bx = _bunkerX[bi];
                float bw = _bunkerBW[bi];
                float fh = _bunkerFH[bi];
                // 接触阴影（先画，在碉堡下面）
                SnowShadow.Draw(this, new Vector2(bx + 24f, GroundY - 1f), 40f, 0.28f);

                // 碉堡本体：梯形（前窄后宽）
                var bunker = new[]
                {
                    new Vector2(bx + 28f, GroundY),
                    new Vector2(bx + bw + 28f, GroundY),
                    new Vector2(bx + bw + 10f, GroundY - fh),
                    new Vector2(bx + 46f, GroundY - fh),
                };
                DrawColoredPolygon(bunker, new Color(0.48f, 0.46f, 0.42f));
                // 侧面受光（高光条）
                var hl = new[]
                {
                    new Vector2(bx + bw + 10f, GroundY - fh),
                    new Vector2(bx + bw + 28f, GroundY),
                    new Vector2(bx + bw + 34f, GroundY),
                    new Vector2(bx + bw + 16f, GroundY - fh),
                };
                DrawColoredPolygon(hl, new Color(0.58f, 0.56f, 0.52f));
                // 碉堡描边
                DrawPolyline(new[]
                {
                    new Vector2(bx + 28f, GroundY), new Vector2(bx + 46f, GroundY - fh),
                    new Vector2(bx + bw + 10f, GroundY - fh), new Vector2(bx + bw + 28f, GroundY),
                }, new Color(0.20f, 0.19f, 0.17f, 0.85f), 1.6f, true);

                // 射击孔（暗色长条 + 十字线表示射击位）
                float spx = bx + 38f; float spy = GroundY - 22f;
                DrawRect(new Rect2(spx, spy, 22, 10), new Color(0.08f, 0.09f, 0.10f, 0.95f));
                DrawLine(new Vector2(spx + 11, spy), new Vector2(spx + 11, spy + 10),
                    new Color(0.40f, 0.38f, 0.34f, 0.7f), 1.4f, true);
                DrawLine(new Vector2(spx, spy + 5), new Vector2(spx + 22, spy + 5),
                    new Color(0.40f, 0.38f, 0.34f, 0.7f), 1.4f, true);

                // 碉堡顶上的积雪（梯形积雪帽）
                var snow = new[]
                {
                    new Vector2(bx + 43f, GroundY - fh - 1),
                    new Vector2(bx + bw + 12f, GroundY - fh - 1),
                    new Vector2(bx + bw + 6f, GroundY - fh - 6),
                    new Vector2(bx + 49f, GroundY - fh - 6),
                };
                DrawColoredPolygon(snow, new Color(0.92f, 0.94f, 0.94f, 0.9f));
                DrawPolyline(new[]
                {
                    new Vector2(bx + 43f, GroundY - fh - 1),
                    new Vector2(bx + bw + 12f, GroundY - fh - 1),
                }, new Color(0.98f, 0.99f, 0.99f), 2.2f, true);

                // 碉堡旁边的铁丝网桩（一根木桩 + 一根铁丝连到远处）
                float fx = bx - 30f;
                DrawLine(new Vector2(fx, GroundY), new Vector2(fx - 2, GroundY - 52),
                    new Color(0.30f, 0.28f, 0.26f), 2.8f, true);
                DrawLine(new Vector2(fx - 2, GroundY - 44), new Vector2(fx + 4, GroundY - 44),
                    new Color(0.30f, 0.28f, 0.26f), 1.6f, true);
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

        private StackSpec[] _stacks = System.Array.Empty<StackSpec>();
        private Texture2D _sackTex;

        /// <summary>沙袋是给主角躲的：蹲/趴在垛子投影范围内算进掩体。</summary>
        public bool Covers(float x)
        {
            foreach (var s in _stacks)
                if (Mathf.Abs(x - s.X) < s.W * 0.5f + 26f) return true;
            return false;
        }

        public override void _Ready()
        {
            var rng = new RandomNumberGenerator();
            rng.Randomize();
            float[] bases = { 980f, 2320f, 3580f, 4160f, 4388f };
            var list = new List<StackSpec>();
            for (int i = 0; i < bases.Length; i++)
            {
                // waist-high cover: a standing rifleman must top the wall by half a torso,
                // wall wide enough for one man prone behind it
                float h = 54f + rng.RandfRange(-4f, 5f);
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
        }

        public override void _Draw()
        {
            if (_sackTex == null) return;
            for (int i = 0; i < _stacks.Length; i++)
            {
                var s = _stacks[i];
                float cx = s.X;

                SnowShadow.Draw(this, new Vector2(cx, GroundY - 1f), s.W * 0.55f, 0.30f);

                bool flip = i % 2 == 1;
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
            }
        }
    }

    // ================= 特效 =================

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
                var soft = SoftSnow.Blob(new Color(0.95f, 0.97f, 0.98f), 0.10f);
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
public partial class EnemySoldier : Node2D
{
    private readonly JiangqiaoLevel _level;
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

    public EnemySoldier(JiangqiaoLevel level, Vector2 pos, float lane)
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
public partial class GrenadeProj : Node2D
{
    private Vector2 _vel;
    private float _fuse = 2.2f;
    private float _spin;

    public bool Exploded { get; private set; }

    public GrenadeProj(Vector2 pos, float facing)
    {
        Position = pos;
        _vel = new Vector2(facing * 320f, -330f);
        ZIndex = 3;
    }

    public override void _Process(double delta)
    {
        if (Exploded) return;
        float f = (float)delta;
        _vel += new Vector2(0, 1500f * f);
        Position += _vel * f;
        _spin += f * 9f;

        // bounce once on snow, roll a little
        if (Position.Y > JiangqiaoLevel.GroundY - 6f && _vel.Y > 0f)
        {
            Position = new Vector2(Position.X, JiangqiaoLevel.GroundY - 6f);
            _vel = new Vector2(_vel.X * 0.35f, -_vel.Y * 0.28f);
            if (Mathf.Abs(_vel.Y) < 60f) _vel = new Vector2(_vel.X * 0.8f, 0f);
        }

        _fuse -= f;
        QueueRedraw();
        if (_fuse <= 0f) Exploded = true;
    }

    public override void _Draw()
    {
        DrawSetTransform(new Vector2(0, JiangqiaoLevel.GroundY - Position.Y + 2f), 0, new Vector2(1.3f, 0.3f));
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
public partial class Testimony : Node2D
{
    private readonly JiangqiaoLevel _level;
    public string Id { get; }
    public string Title { get; }
    public string[] Lines { get; }
    public string Source { get; }
    public bool Recorded;

    private float _t;
    private Texture2D _boxTex;

    public Testimony(JiangqiaoLevel level, string id, Vector2 pos, string title, string[] lines, string source)
    {
        _level = level;
        Id = id;
        Position = pos;
        Title = title;
        Lines = lines;
        Source = source;
        Recorded = GameState.I.S.JiangqiaoTestimony.Contains(id);
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
/// 拾取补给时把新弹点亮、并把总容量撑大。右上角一排右对齐。
/// </summary>
public partial class AmmoBar : Control
{
    private FieldPlayer _p;
    private const float IconW = 6f, IconH = 15f, Gap = 2.5f, RowW = 330f;

    public void Bind(FieldPlayer p) { _p = p; }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (_p == null) return;
        int total = Mathf.Max(_p.AmmoMax, _p.Ammo);
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

/// <summary>通关档案卡：史实结果 + 后续章节提示 + 返回/重玩。</summary>
public partial class LevelEndUi : CanvasLayer
{
    private readonly Main _main;

    public LevelEndUi(Main main)
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

        box.AddChild(L("1931 年 11 月 19 日 · 齐齐哈尔陷落", 14, new Color(0.48f, 0.43f, 0.35f)));
        box.AddChild(L("江桥抗战 · 十六天", 27, new Color(0.16f, 0.14f, 0.12f)));
        box.AddChild(new ColorRect
        {
            Color = new Color(0.55f, 0.20f, 0.16f, 0.75f),
            CustomMinimumSize = new Vector2(0, 2),
        });
        box.AddChild(L("江桥抗战从 11 月 4 日打到 11 月 19 日。马占山部在装备、补给、空军全部劣势下坚持了十六天，最终撤退。这是九一八之后，中国军队第一次有组织的大规模抵抗；也是此后十四年抗战的开端。", 16, new Color(0.22f, 0.19f, 0.16f)));
        box.AddChild(L("你带出的三份口述没有改变战局。它们只证明：在那片雪地上，确实有人说过那些话。", 16, new Color(0.22f, 0.19f, 0.16f)));
        box.AddChild(L("依据：马占山《江桥抗战经过》；《黑龙江省志·军事志》；江桥抗战公开口述史料。人物为复合虚构，不对应单一在世者。", 13, new Color(0.45f, 0.40f, 0.33f)));
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
            GameState.I.S.JiangqiaoTestimony.Clear();
            GameState.I.Save();
            _main.StartJiangqiao();
        };
        row.AddChild(replay);

        box.AddChild(row);
    }
}
