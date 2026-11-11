using System;
using System.Collections.Generic;
using Godot;

namespace Witness;

/// <summary>
/// A farmhouse standing near the line, half ruined by the war. Player presses E
/// at the door to search inside: ammo, grenades, last letters, clue papers,
/// sometimes a locked ammo chest that needs prying. Loot ids persist in save.
/// </summary>
public partial class EnterableHouse : Node2D
{
    private readonly string _id;
    private readonly string _texName;
    private readonly Texture2D _tex;
    private readonly float _w;
    private readonly bool _enterable;
    private readonly float _lift;

    public string Id => _id;
    public bool Enterable => _enterable;

    /// <summary>
    /// Where the doorway of each enterable house is, as a fraction of the
    /// house width, and how tall it stands as a fraction of the house height.
    ///
    /// v3.20: this used to be one hardcoded 0.18 for every house, and a flat
    /// dark slab was painted over that spot to mark it. The slab was the
    /// only flat fill anywhere in the scene, it sat about 35px below the
    /// doorway the artist had already drawn, and it covered the plank door
    /// of the intact house and the torn opening of the ruined one. Players
    /// read it as a black board propped in front of the wall.
    ///
    /// Fx is the centre of the doorway in house-width fractions, Fh the
    /// height in house-height fractions, and HangLeaf says the texture has
    /// no door of its own and one has to be put back.
    /// </summary>
    private readonly record struct DoorSpec(float Fx, float Fh, bool HangLeaf);

    private static readonly Dictionary<string, DoorSpec> _doorSpecs = new()
    {
        // intact house: a plank door with a round knob, painted between the
        // two timber posts of the front wall
        ["house_intact_near"] = new(0.492f, 0.36f, false),
        // 军事通信据点：由 house_intact_near 图生图改造，门口位置保持一致
        ["radio_station"] = new(0.492f, 0.36f, false),
        // ruined house: the wall is torn open beside the door frame, snow
        // drifted across the sill
        ["house_ruined_near"] = new(0.200f, 0.50f, false),
        // burnt shell: the roof burned off and nothing is left to find, so
        // a half-burnt door leaf leans back into the frame (v3.45: was an
        // intact plank door - a burned house would not keep one)
        ["house_burned_near"] = new(0.260f, 0.42f, true),
        // v3.64b: 九一八关卡用 _autumn 变体贴图（同图上色），此前没登记
        // 导致 DoorX 全部 fallback 到 0.18 —— 门画错位置，沙包避让也算错门口。
        ["house_intact_near_autumn"] = new(0.492f, 0.36f, false),
        ["house_ruined_near_autumn"] = new(0.200f, 0.50f, false),
        ["house_burned_near_autumn"] = new(0.260f, 0.42f, true),
    };

    private static Texture2D _leafTex;

    private float _doorFx = -1f;

    private float DoorFx
    {
        get
        {
            if (_doorFx < 0f)
                _doorFx = _doorSpecs.TryGetValue(_texName, out var s) ? s.Fx : 0.18f;
            return _doorFx;
        }
    }

    public float DoorX => Position.X + _w * DoorFx;
    public float HalfW => _w * 0.5f;

    private static readonly Dictionary<string, Texture2D> _texCache = new();

    public EnterableHouse(string id, Vector2 pos, string texName, float w,
        bool enterable = true, float lift = 0f)
    {
        _id = id;
        _texName = texName;
        _tex = LoadTex(texName);
        _w = w;
        _enterable = enterable;
        _lift = lift;
        Position = pos;
        // lifted houses sit a step further back in the depth row.
        // v3.8: near row now draws IN FRONT of the ground art (the railway
        // band is part of it), so the track passes behind the village houses
        // instead of slicing through their front walls.
        // v3.25: the whole stack was renumbered, because a flame has to be
        // able to sit behind a wall AND in front of one. The row reads
        // ground art -10, footprints -8, fire inside the far row -7, far
        // houses -6, fire outside the far row -5, fire inside the near row
        // -4, near houses -3, fire outside the near row -2, smoke -1.
        ZIndex = lift > 0f ? -6 : -3;
    }

    private static Texture2D LoadTex(string name)
    {
        if (_texCache.TryGetValue(name, out var t)) return t;
        t = GD.Load<Texture2D>($"res://assets/art/props/{name}.png");
        _texCache[name] = t;
        return t;
    }

    public override void _Draw()
    {
        if (_tex == null) return;
        var sz = _tex.GetSize();
        float h = _w * sz.Y / sz.X;
        // local space: node Position sits ON the ground line, so ground = local y 0
        float gl = 0f;
        // far houses sink slightly into the snow haze to sell the depth row
        var fade = _lift > 0f ? new Color(1f, 1f, 1f, 0.88f) : Colors.White;

        // contact shadow along the base
        // v2.8: this was a hard rimmed DrawCircle. Stretched over the full
        // house width the rim showed up as a pale slab lying on the snow, so
        // it now uses the radial falloff sprite.
        DrawSetTransform(new Vector2(_w * 0.5f, gl - 1f), 0, new Vector2(1.15f, 0.24f));
        DrawTextureRect(SoftSnow.Blob(new Color(0.10f, 0.09f, 0.08f), 0.26f),
            new Rect2(-20f, -20f, 40f, 40f), false);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        // v3.47: the burned / skeletal / posts / ruined textures are full of
        // see-through gaps, mostly in their lower half. Through them the
        // parallax tree layer (z -20) and the railway band showed at a ground
        // line ABOVE the house's own base, so the ruin read as floating above
        // the snow, and the burnt shell's hung door leaf read as planted in
        // open snow right on the track (user screenshots 2026-10-31). A soft
        // charred-interior wash behind the wall fills those gaps: what shows
        // through a hole is now the house's own darkness, not the landscape
        // behind it. The intact house is fully painted and needs none.
        if (_texName != "house_intact_near")
        {
            bool charred = _texName == "house_burned_near" || _texName == "house_skeleton_near";
            var hole = charred
                ? new Color(0.055f, 0.045f, 0.038f, 0.56f)   // charred interior
                : new Color(0.10f, 0.085f, 0.07f, 0.38f);    // ruined / posts: dim interior
            // lifted far-row houses are drawn at 0.88 fade - keep the wash
            // one notch lighter so it sinks into the haze with them
            if (_lift > 0f) hole.A *= 0.82f;
            // two overlapping soft lobes: a wide low one hugging the base and
            // a narrower one reaching up the middle - organic coverage, and
            // the radial falloff means no rim can peek past the wall silhouette
            DrawSetTransform(new Vector2(_w * 0.5f, gl - h * 0.26f), 0f,
                new Vector2(_w * 0.85f / 64f, h * 0.55f / 64f));
            DrawTextureRect(SoftSnow.Blob(hole, 1f), new Rect2(-32f, -32f, 64f, 64f), false);
            DrawSetTransform(new Vector2(_w * 0.44f, gl - h * 0.42f), 0f,
                new Vector2(_w * 0.55f / 64f, h * 0.50f / 64f));
            DrawTextureRect(SoftSnow.Blob(hole, 1f), new Rect2(-32f, -32f, 64f, 64f), false);
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }

        // house body, bottom sits on the snow line
        DrawTextureRect(_tex, new Rect2(0f, gl - h, _w, h), false, fade);

        if (!_enterable) return;   // scenery ruin: no door light, nothing to search

        // v3.20: the doorway is wherever the painting put it. Two of the
        // three enterable houses already have one - a plank door on the
        // intact house, a torn opening in the ruined one - so all that is
        // left to draw is the trampled snow on the doorstep.
        //
        // v3.5 used to paint a flat near-black slab over the doorway here.
        // It was the only flat fill in the whole scene, it did not line up
        // with the painted opening, and it hid the artwork underneath, so
        // it read as a black board leaning on the wall.
        //
        // The burnt shell has no door left to find, so one gets hung back
        // into the frame - the plank leaf lifted straight out of the intact
        // house texture, so it carries the same brush and the same ink
        // outlines as everything else.
        var spec = _doorSpecs.TryGetValue(_texName, out var ds)
            ? ds : new DoorSpec(0.18f, 0.42f, false);
        float dx = _w * spec.Fx;
        float dh = h * spec.Fh;

        // v3.44: load the leaf once so the cast shadow, the leaf, the ink
        // jamb and the sill contact can all share its measured width.
        // v3.45: the burnt shell hangs a fire-damaged leaf (charred planks,
        // torn corner, splintered foot) so the entrance reads without
        // claiming the fire skipped the door. The intact leaf
        // (door_leaf.png) is kept for any future standing-house use.
        _leafTex ??= GD.Load<Texture2D>("res://assets/art/props/door_leaf_burnt.png");
        float lw = _leafTex != null ? dh * _leafTex.GetSize().X / _leafTex.GetSize().Y : dh * 0.315f;
        // v3.45: the burnt leaf's bbox carries the torn spread of the fire
        // damage, so hung at its natural aspect (0.67) it reads as a cabinet
        // panel rather than a door. Squash to ~0.46: slimmer than the bbox,
        // still wider than the intact leaf, keeps the ragged silhouette.
        lw *= 0.68f;
        // v3.49: the leaf HANGS from the burnt lintel and sags - pivot moved
        // from the foot to the top centre, rotated ~11° so the foot swings out
        // of the opening and sinks into a drift. Before this the leaf stood
        // bolt upright (only -0.045 rad) with a surviving doorknob, and the
        // outdoor fire flickering in front of the doorway hid/revealed it as
        // the flames rose and fell, so to the player a full door kept
        // "suddenly reappearing" in a burnt-out shell (user report 2026-10-31,
        // direction chosen: heavy lean + half buried).
        const float Lean = 0.19f;   // rad (~11°), foot swings toward the right jamb
        Rect2 leafRect = new Rect2(-lw * 0.5f, 0f, lw, dh);
        float leafCy = gl - dh;     // top-centre pivot: the hinge side stays up

        if (spec.HangLeaf && _leafTex != null)
        {
            // v3.44: the leaf texture has a hard rectangular alpha edge (its
            // plank frame runs to the image border), so hung as-is it reads
            // as a flat block pasted on the charred wall. Seat it as a door in
            // the burnt doorway: a soft cast shadow to the lower-right, a dark
            // contact strip at the sill, and a thin ink jamb frame. The real
            // fix is alpha-padding the texture so the edge disappears (flagged
            // for art).
            // soft cast shadow pooled at the foot, offset to the lean side
            DrawSetTransform(new Vector2(dx + dh * Lean * 0.6f + 4f, gl + 2f), 0,
                new Vector2(lw / 40f * 1.5f, 0.32f));
            DrawTextureRect(SoftSnow.Blob(new Color(0.05f, 0.04f, 0.03f), 0.38f),
                new Rect2(-20f, -20f, 40f, 40f), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            // the leaf: hinged at the lintel, sagging out of the opening.
            // The texture is already soot-dark, so only a whisper of tint.
            DrawSetTransform(new Vector2(dx, leafCy), Lean, Vector2.One);
            DrawTextureRect(_leafTex, leafRect, false, new Color(0.92f, 0.90f, 0.87f));
            // burn-through where the doorknob survived the fire: a soft char
            // blob over the handle side (texture-relative ~13% from the left,
            // ~54% down), drawn in leaf-local space so it rides the lean. It
            // eats the left edge raggedly - one more thing that no longer
            // reads as "a complete door".
            DrawTextureRect(SoftSnow.Blob(new Color(0.05f, 0.04f, 0.03f), 0.9f),
                new Rect2(-lw * 0.52f, dh * 0.42f, lw * 0.38f, dh * 0.24f), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            // thin ink jamb frame: two posts and a lintel, kept UPRIGHT - the
            // burnt frame of the opening stands while the leaf sags in it.
            // v3.47: no bottom bar (it drew a hard ink line across the
            // railway band; the sill contact grounds the foot instead).
            var jamb = InkStyle.InkColor;
            float jl = dx - lw * 0.5f, jr = dx + lw * 0.5f, jt = gl - dh;
            DrawLine(new Vector2(jl, jt), new Vector2(jl, gl), jamb, 1.6f, true);
            DrawLine(new Vector2(jr, jt), new Vector2(jr, gl), jamb, 1.6f, true);
            DrawLine(new Vector2(jl, jt), new Vector2(jr, jt), jamb, 1.6f, true);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        // doorstep snow disturbed
        // v2.8: radial falloff, so the trodden patch has no hard rim
        DrawSetTransform(new Vector2(dx, gl - 1f), 0, new Vector2(1.4f, 0.3f));
        DrawTextureRect(SoftSnow.Blob(new Color(0.86f, 0.89f, 0.91f), 0.7f),
            new Rect2(-20f, -20f, 40f, 40f), false);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        // v3.44: dark contact at the sill, over the snow, so the burnt-house
        // leaf reads as grounded instead of floating.
        // v3.49: the contact follows the sagged foot, and a snow bank drifted
        // OVER the sunken bottom of the leaf finishes the job - the door no
        // longer stands on the ground line, it is buried in it.
        if (spec.HangLeaf && _leafTex != null)
        {
            DrawSetTransform(new Vector2(dx + dh * Lean * 0.5f, gl - 3f), 0,
                new Vector2(lw / 40f * 1.3f, 0.34f));
            DrawTextureRect(SoftSnow.Blob(new Color(0.04f, 0.03f, 0.02f), 0.5f),
                new Rect2(-20f, -20f, 40f, 40f), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            // snow bank over the foot - drawn last so it sits on the sill
            // contact and the doorstep patch alike
            DrawSetTransform(new Vector2(dx + dh * Lean * 0.75f, gl - 2f), 0,
                new Vector2(lw * 1.7f / 64f, dh * 0.36f / 64f));
            DrawTextureRect(SoftSnow.Blob(new Color(0.90f, 0.92f, 0.94f), 0.85f),
                new Rect2(-32f, -32f, 64f, 64f), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }
}

/// <summary>屋内格局类型：三种固定常识布局，对应不同功能的房子。</summary>
public enum RoomType
{
    /// <summary>住家：炕 / 灶台 / 桌子 / 柜子，平民的屋子。</summary>
    Residence,
    /// <summary>通信据点机房：发电机 / 电闸 / 发信机 / 地图桌。</summary>
    CommOutpost,
    /// <summary>仓库或焚毁的院落：货架 / 木箱 / 灰烬，半塌的农舍。</summary>
    BurntWarehouse,
}

/// <summary>
/// Interior overlay when searching a house. Paper-chip hotspots, FieldNoteUi
/// reuse for letters and clue papers, chest needs hold-to-pry and can jam.
/// </summary>
public partial class HouseInteriorUi : CanvasLayer
{
    /// <summary>
    /// 截图/预览用：造一间屋内水彩背景（不依赖存档、关卡或 CanvasLayer）。
    /// scenes/CaptureRoom.tscn 用它渲染真实帧，验证室内画风。
    /// </summary>
    public static Control CreateRoomPreview(RoomType type, int variant, int seed)
        => new RoomBg(type, variant, seed);

    public event Action Closed;
    public event Action<int> GotAmmo;
    public event Action<int> GotGrenade;
    public event Action<int> GotHeal;       // 1 = gauze, 99 = medkit (full)
    public event Action<int> GotLetter;     // 拾得信（letter spot）→ 关卡累计信数 / 口信
    public Func<bool> CanHeal = () => true; // level asks the player; full hp wastes nothing

    private readonly string _houseId;
    private readonly string _houseTitle;
    private readonly RandomNumberGenerator _rng = new();
    private readonly HashSet<string> _looted;
    private readonly Func<bool> _canTransmit;   // 发信机前置判定：信齐且通电才允许发报（通信据点屋专用）
    private readonly Action _onTransmit;        // 发报完成回调：关卡据此标记 N918RadioSent 并通关
    private readonly Action _onPower;           // 合闸通电完成回调：关卡据此置 N918Powered
    private readonly List<Spot> _spots = new();
    private readonly bool _roomLayout;
    private readonly RoomType _roomType;      // 屋内格局类型（住家 / 机房 / 焚毁仓库）
    private readonly int _roomVariant;        // 同类型的轻微变体（如住家左右镜像），让每间屋不完全一样
    // 信/遗书落款：默认江桥（东北军官兵家书改为江桥抗战官兵），九一八屋（roomLayout）用东北军落款
    private string _noteSource = "注：人物为复合虚构，不对应单一在世者。江桥抗战官兵家书意象改写。";
    private Control _root;
    private Label _hint;
    private Spot _chestSpot;
    private float _pryT;
    private bool _prying;
    private Spot _radioSpot;                 // 无线电发信机（通信据点屋）
    private float _radioT;
    private bool _transmitting;
    private Spot _powerSpot;                 // 电闸 / 手摇发电机（通信据点屋供电小解谜）
    private float _powerT;
    private bool _powering;
    private bool _closed;

    /// <summary>
    /// 默认构造（江桥沿用）：loot 直接绑定江桥存档集合，无发信机。
    /// 保持与江桥现有调用 `new HouseInteriorUi(id, title)` 完全兼容、零改动。
    /// </summary>
    public HouseInteriorUi(string houseId, string houseTitle)
        : this(houseId, houseTitle, GameState.I.S.JiangqiaoLoot) { }

    /// <summary>
    /// 注入式构造（九一八用）：loot 集合由关卡指定（N918Loot），
    /// 并可传入发信机前置判定与发报完成回调（仅通信据点屋传入，普通屋传 null）。
    /// </summary>
    public HouseInteriorUi(string houseId, string houseTitle, HashSet<string> loot,
        RoomType roomType = RoomType.Residence, int roomVariant = 0,
        Func<bool> canTransmit = null, Action onTransmit = null, Action onPower = null,
        bool roomLayout = false)
    {
        _houseId = houseId;
        _houseTitle = houseTitle;
        Layer = 20;
        _rng.Seed = (ulong)(houseId.GetHashCode() & 0xffffff);
        _looted = loot;
        _roomType = roomType;
        _roomVariant = roomVariant;
        _canTransmit = canTransmit;
        _onTransmit = onTransmit;
        _onPower = onPower;
        _roomLayout = roomLayout;
        if (roomLayout) _noteSource = "注：人物为复合虚构，不对应单一在世者。东北军官兵家书意象改写。";
    }

    private partial class Spot : Button
    {
        public string SpotId;
        public string Kind;        // ammo / grenade / letter / will / clue / gauze / medkit / chest / ash
        public string ResultText;
        public bool Done;
        public float PryProgress;  // chest only
    }

    public override void _Ready()
    {
        if (_roomLayout) { BuildRoom(); return; }
        var dim = new ColorRect { Color = new Color(0.04f, 0.035f, 0.03f, 0.80f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);

        _root = new CenterContainer();
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(880, 560) };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.13f, 0.11f, 0.09f),
            BorderColor = new Color(0.24f, 0.20f, 0.16f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            ContentMarginLeft = 26, ContentMarginRight = 26, ContentMarginTop = 20, ContentMarginBottom = 18,
            ShadowColor = new Color(0, 0, 0, 0.5f), ShadowSize = 16, ShadowOffset = new Vector2(6, 8),
        };
        panel.AddThemeStyleboxOverride("panel", style);
        _root.AddChild(panel);

        var box = new VBoxContainer { };
        box.AddThemeConstantOverride("separation", 12);
        panel.AddChild(box);

        var title = new Label { Text = $"{_houseTitle} · 屋内" };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", new Color(0.86f, 0.82f, 0.72f));
        box.AddChild(title);

        var rule = new ColorRect { Color = new Color(0.55f, 0.20f, 0.16f, 0.75f), CustomMinimumSize = new Vector2(0, 2) };
        box.AddChild(rule);

        // interior floor: item chips laid out in a wrap flow, feels like rummaging shelves
        var flow = new HFlowContainer { };
        flow.AddThemeConstantOverride("h_separation", 14);
        flow.AddThemeConstantOverride("v_separation", 14);
        flow.CustomMinimumSize = new Vector2(820, 330);
        box.AddChild(flow);

        BuildSpots();
        foreach (var s in _spots) flow.AddChild(s);

        _hint = new Label { Text = "翻找屋内的东西。按 E 离开。" };
        _hint.AddThemeFontSizeOverride("font_size", 14);
        _hint.AddThemeColorOverride("font_color", new Color(0.60f, 0.55f, 0.46f));
        box.AddChild(_hint);

        var leave = new Button { Text = "离开屋子 (E)" };
        leave.Pressed += Close;
        var lb = new StyleBoxFlat
        {
            BgColor = new Color(0.24f, 0.20f, 0.16f),
            ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 8, ContentMarginBottom = 8,
        };
        leave.AddThemeStyleboxOverride("normal", lb);
        leave.AddThemeColorOverride("font_color", new Color(0.88f, 0.84f, 0.74f));
        box.AddChild(leave);
    }

    private void BuildSpots()
    {
        // every house rolls its own loot table, chest always somewhere around
        // ammo: usually just a few rounds, a full box is a rare lucky find
        int ammoN = _rng.Randf() < 0.12f ? _rng.RandiRange(6, 8) : _rng.RandiRange(1, 5);
        AddSpot("ammo1", "弹药盒", "ammo", $"翻出一小袋步枪弹（+{ammoN} 发）");
        // 信/灰烬的落点随屋内格局变化，文案也要跟着常识走
        string letterLabel = _roomType == RoomType.CommOutpost ? "桌上的信封"
            : _roomType == RoomType.BurntWarehouse ? "残墙下压着的信"
            : "压在灶台下的信";
        AddSpot("letter1", letterLabel, "letter", "");
        if (_rng.Randf() > 0.5f)
            AddSpot("will1", "叠得整整齐齐的遗书", "will", "");
        if (_rng.Randf() > 0.35f)
            AddSpot("grenade1", "木柄手榴弹", "grenade", "");
        if (_rng.Randf() < 0.18f)
            AddSpot("gauze1", "半卷干净的纱布", "gauze", "");
        if (_rng.Randf() < 0.05f)
            AddSpot("medkit1", "军医留下的医药包", "medkit", "");
        AddSpot("chest1", "上锁的弹药箱", "chest", "");
        if (_rng.Randf() > 0.5f)
            AddSpot("clue1", "烧剩一半的文件", "clue", "");
        string ashLabel = _roomType == RoomType.BurntWarehouse ? "一摊烧焦的灰烬"
            : _roomType == RoomType.CommOutpost ? "墙角的废墟"
            : "烧塌的柜子";
        string ashText = _roomType == RoomType.BurntWarehouse
            ? "灰堆里只剩烧变形的搪瓷缸和半截枪管。"
            : "柜子里只剩灰和烧变形的搪瓷缸。";
        AddSpot("ash1", ashLabel, "ash", ashText);

        // 通信据点屋专用（_canTransmit 仅在该屋注入）：先合闸通电，再操作发信机发报
        if (_canTransmit != null)
        {
            AddSpot("power1", "墙边的电闸与手摇发电机", "power", "");
            AddSpot("radio1", "墙角的无线电发信机", "radio", "");
        }
    }

    private void AddSpot(string spotId, string label, string kind, string result)
    {
        string fullId = $"{_houseId}:{spotId}";
        var s = new Spot
        {
            SpotId = fullId,
            Kind = kind,
            ResultText = result,
            Done = _looted.Contains(fullId),
            Text = label,
        };
        var normal = new StyleBoxFlat
        {
            BgColor = s.Done ? new Color(0.78f, 0.74f, 0.66f, 0.88f) : new Color(0.965f, 0.94f, 0.875f, 0.96f),
            BorderColor = new Color(0.30f, 0.25f, 0.20f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 1, CornerRadiusTopRight = 1, CornerRadiusBottomLeft = 1, CornerRadiusBottomRight = 1,
            ShadowColor = new Color(0.12f, 0.10f, 0.08f, 0.30f), ShadowSize = 4, ShadowOffset = new Vector2(2, 3),
            ContentMarginLeft = 11, ContentMarginRight = 11, ContentMarginTop = 6, ContentMarginBottom = 6,
        };
        s.AddThemeStyleboxOverride("normal", normal);
        var hover = new StyleBoxFlat
        {
            BgColor = s.Done ? new Color(0.82f, 0.78f, 0.70f, 0.92f) : new Color(0.99f, 0.97f, 0.92f),
            BorderColor = new Color(0.42f, 0.20f, 0.15f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            ShadowColor = new Color(0.12f, 0.10f, 0.08f, 0.34f), ShadowSize = 5, ShadowOffset = new Vector2(2, 3),
            ContentMarginLeft = 11, ContentMarginRight = 11, ContentMarginTop = 6, ContentMarginBottom = 6,
        };
        s.AddThemeStyleboxOverride("hover", hover);
        s.AddThemeColorOverride("font_color", s.Done ? new Color(0.42f, 0.38f, 0.32f) : new Color(0.22f, 0.18f, 0.15f));
        s.AddThemeFontSizeOverride("font_size", 14);
        s.Pressed += () => OnSpot(s);
        _spots.Add(s);
        if (kind == "chest") _chestSpot = s;
        if (kind == "radio") _radioSpot = s;
        if (kind == "power") _powerSpot = s;
        if (s.Done) s.Disabled = kind == "chest" || kind == "radio" || kind == "power";
    }

    private void OnSpot(Spot s)
    {
        if (_closed || s.Done && s.Kind != "chest") return;
        switch (s.Kind)
        {
            case "ammo":
            {
                int n = _rng.Randf() < 0.12f ? _rng.RandiRange(6, 8) : _rng.RandiRange(1, 5);
                GotAmmo?.Invoke(n);
                s.ResultText = $"翻出一小袋步枪弹（+{n} 发）";
                MarkDone(s);
                break;
            }
            case "grenade":
            {
                // wet wood and old fuses: some of these things simply won't go off
                float r = _rng.Randf();
                if (r < 0.20f)
                {
                    s.ResultText = "从房梁上摸到一颗手榴弹——木柄受潮，引信哑了，扔不得。";
                    MarkDone(s);
                    break;
                }
                int n = r > 0.90f ? 2 : 1;
                GotGrenade?.Invoke(n);
                s.ResultText = n == 2
                    ? "房梁上挂着一小串，两颗还能用（+2）。"
                    : "从房梁上摸到一颗手榴弹（+1）";
                MarkDone(s);
                break;
            }
            case "gauze":
            {
                if (!CanHeal())
                {
                    s.ResultText = "身上没伤口，纱布先留在这儿吧。";
                    s.Text = s.ResultText;
                    s.Disabled = false;
                    break;
                }
                GotHeal?.Invoke(1);
                s.ResultText = "绷带还干净，裹紧伤口好受了些（生命 +1）。";
                MarkDone(s);
                break;
            }
            case "medkit":
            {
                if (!CanHeal())
                {
                    s.ResultText = "眼下没伤，医药包先收在原处。";
                    s.Text = s.ResultText;
                    s.Disabled = false;
                    break;
                }
                GotHeal?.Invoke(99);
                s.ResultText = "纱布、碘酒、夹板样样齐全——伤全处理了一遍（生命全回复）。";
                MarkDone(s);
                break;
            }
            case "letter":
                OpenNote("灶台砖下压着的一封信",
                    new[]
                    {
                        "“娘，这边冷得很，手里的枪比我的手还凉。等把桥守住了，我就回家帮您劈柴。”",
                        "“要是我回不去，别叫人把我的名字写错了。是永胜的胜，得胜的胜。”",
                    },
                    _noteSource);
                MarkDone(s);
                GotLetter?.Invoke(1);   // 关卡据此累计「信」数 / 口信（N918Testimony / N918EnvelopeCount）
                break;
            case "will":
                OpenNote("叠得整整齐齐的遗书",
                    new[]
                    {
                        "“若我阵亡，勿念。此身许国，不得许家。”",
                        "“口袋里余钱四十枚，托同乡带回，给妹妹缴学费。她若问起我，就说我在北边教书的。”",
                    },
                    _noteSource);
                MarkDone(s);
                break;
            case "clue":
                OpenNote("烧剩一半的文件",
                    new[]
                    {
                        "纸只剩半页，字迹被火燎过：「……军列午后过江桥，皮坎子……车头挂斗三十……」",
                        "后半截烧成了黑灰。落款只看得到一个「熙」字。",
                    },
                    "线索：指向日军军列过桥的时刻表。与铁路工人孙德山的口述互相印证。");
                MarkDone(s);
                break;
            case "chest":
                if (!s.Done) StartPry(s);
                break;
            case "power":
                // 通信据点供电小解谜：长按合闸 / 摇发电机；通电后才允许发报
                if (!s.Done) StartPower(s);
                break;
            case "radio":
                // 发信机提交点：前置不满足（信不足 / 没电）只提示不推进；满足则长按发报
                if (_canTransmit != null && !_canTransmit())
                {
                    s.ResultText = "信件还没凑齐（或发信机没电），机器空转，什么也传不出去。";
                    _hint.Text = s.ResultText;
                    break;
                }
                if (!s.Done) StartTransmit(s);
                break;
            case "ash":
                MarkDone(s);
                break;
        }
    }

    private void StartPry(Spot s)
    {
        if (_prying) return;
        _prying = true;
        _pryT = 0f;
        s.Text = "用刺刀撬锁……";
        s.Disabled = true;
    }

    /// <summary>
    /// 通信据点供电小解谜：长按合闸 / 摇手摇发电机，约 1.2s 完成。
    /// 完成后置 _powerSpot 为已搜并回调 _onPower（关卡据此标记 N918Powered）。
    /// </summary>
    private void StartPower(Spot s)
    {
        if (_powering) return;
        _powering = true;
        _powerT = 0f;
        s.Text = "合闸供电……";
        s.Disabled = true;
    }

    /// <summary>
    /// 无线电发信机：前置（信齐且已通电）满足后长按发报，约 1.8s 完成。
    /// 完成后置 _radioSpot 为已搜并回调 _onTransmit（关卡据此标记 N918RadioSent 并通关）。
    /// 前置不满足时 OnSpot 已拦截，不会走到这里。
    /// </summary>
    private void StartTransmit(Spot s)
    {
        if (_transmitting) return;
        _transmitting = true;
        _radioT = 0f;
        s.Text = "发报中……";
        s.Disabled = true;
    }

    public override void _Process(double delta)
    {
        if (_closed) return;
        if (_prying && _chestSpot != null)
        {
            _pryT += (float)delta;
            float t = Mathf.Min(1f, _pryT / 1.4f);
            _chestSpot.Text = $"用刺刀撬锁…… {(int)(t * 100)}%";
            _chestSpot.PryProgress = t;
            if (t >= 1f)
            {
                _prying = false;
                bool open = _rng.Randf() < 0.6f;
                if (open)
                {
                    int n = _rng.RandiRange(2, 6);
                    bool hasNade = _rng.Randf() < 0.5f;
                    GotAmmo?.Invoke(n);
                    if (hasNade) GotGrenade?.Invoke(1);
                    _chestSpot.ResultText = hasNade
                        ? $"锁开了：一小袋步枪弹（+{n} 发）和一颗手榴弹。"
                        : $"锁开了：一小袋步枪弹（+{n} 发）。";
                    _chestSpot.Text = "弹药箱（已撬开）";
                    MarkDone(_chestSpot);
                    _chestSpot.Disabled = false;
                    _chestSpot.Done = true;
                }
                else
                {
                    _chestSpot.Text = "锁头冻死了，撬不开";
                    _chestSpot.ResultText = "锁芯冻死了，刺刀撬到弯也打不开。";
                    MarkDone(_chestSpot);
                }
            }
        }
        if (_powering && _powerSpot != null)
        {
            _powerT += (float)delta;
            float t = Mathf.Min(1f, _powerT / 1.2f);
            _powerSpot.Text = $"合闸供电…… {(int)(t * 100)}%";
            _powerSpot.PryProgress = t;
            if (t >= 1f)
            {
                _powering = false;
                _powerSpot.ResultText = "发电机摇起来了，电闸合上，据点的灯亮了。";
                MarkDone(_powerSpot);
                _powerSpot.Disabled = false;
                _powerSpot.Done = true;
                _onPower?.Invoke();
            }
        }
        if (_transmitting && _radioSpot != null)
        {
            _radioT += (float)delta;
            float t = Mathf.Min(1f, _radioT / 1.8f);
            _radioSpot.Text = $"发报中…… {(int)(t * 100)}%";
            _radioSpot.PryProgress = t;
            if (t >= 1f)
            {
                _transmitting = false;
                _radioSpot.ResultText = "电键叩下去，电波穿过秋夜，把北大营的实情传了出去。";
                MarkDone(_radioSpot);
                _radioSpot.Disabled = false;
                _radioSpot.Done = true;
                _onTransmit?.Invoke();
            }
        }
    }

    private void MarkDone(Spot s)
    {
        s.Done = true;
        s.Text = string.IsNullOrEmpty(s.ResultText) ? $"{s.Text}（已搜过）" : s.ResultText;
        s.Disabled = true;
        _looted.Add(s.SpotId);
        GameState.I.Save();
        _hint.Text = s.ResultText;
    }

    private void OpenNote(string title, string[] paragraphs, string source)
    {
        var note = new FieldNoteUi(_houseTitle, title, paragraphs, source, () => { });
        AddChild(note);
    }

    private void Close()
    {
        if (_closed) return;
        _closed = true;
        Closed?.Invoke();
        QueueFree();
    }

    /// <summary>
    /// 房间格局模式（九一八用）：画一个暖色屋内背景，把每个 loot 物件作为可点击标签
    /// 摆进房间（灶台/货架/桌子/柜子/发电机/发信机旁），玩家像在屋里翻找一样逐个搜寻。
    /// 搜索逻辑（OnSpot / MarkDone / 撬箱 / 发报 / 供电）完全复用，只换呈现层。
    /// </summary>
    private void BuildRoom()
    {
        var dim = new ColorRect { Color = new Color(0.04f, 0.035f, 0.03f, 0.82f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(1000, 640) };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.08f, 0.06f),
            BorderColor = new Color(0.30f, 0.24f, 0.17f),
            BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
            ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 24, ContentMarginBottom = 24,
            ShadowColor = new Color(0, 0, 0, 0.5f), ShadowSize = 18, ShadowOffset = new Vector2(6, 8),
        };
        panel.AddThemeStyleboxOverride("panel", style);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);
        center.AddChild(panel);

        // PanelContainer only lays out a single child cleanly — wrap everything in one Content node
        // (same pattern as the chip-mode box), otherwise extra children render blank.
        var content = new Control();
        content.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        panel.AddChild(content);

        // v3.64: 钢笔淡彩水彩室内；houseId 播种让每间屋的纸纹/水痕/排线都不同
        var room = new RoomBg(_roomType, _roomVariant, _houseId.GetHashCode() & 0xffff);
        room.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        content.AddChild(room);
        room.QueueRedraw();

        var spotsLayer = new Control();
        spotsLayer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        content.AddChild(spotsLayer);

        // title / hint / leave are anchored children of the single Content node
        // (PanelContainer only manages its first child; extra direct children render blank)
        var title = new Label { Text = $"{_houseTitle} · 屋内" };
        title.AddThemeFontSizeOverride("font_size", 22);
        // v3.64: 屋内改成纸本色底，标题/提示必须改成墨色才看得清
        title.AddThemeColorOverride("font_color", new Color(0.24f, 0.20f, 0.17f));
        title.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        title.OffsetTop = 6;
        title.HorizontalAlignment = HorizontalAlignment.Center;
        content.AddChild(title);

        _hint = new Label { Text = "在屋子里翻找能用的东西，点一下就能查看。按 E 离开。" };
        _hint.AddThemeFontSizeOverride("font_size", 14);
        _hint.AddThemeColorOverride("font_color", new Color(0.38f, 0.33f, 0.27f));
        _hint.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _hint.OffsetBottom = -48;
        _hint.HorizontalAlignment = HorizontalAlignment.Center;
        content.AddChild(_hint);

        var leave = new Button { Text = "离开屋子 (E)" };
        leave.Pressed += Close;
        var lb = new StyleBoxFlat
        {
            BgColor = new Color(0.26f, 0.21f, 0.16f),
            ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 8, ContentMarginBottom = 8,
        };
        leave.AddThemeStyleboxOverride("normal", lb);
        leave.AddThemeColorOverride("font_color", new Color(0.92f, 0.88f, 0.78f));
        leave.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        leave.OffsetBottom = -10;
        leave.Alignment = HorizontalAlignment.Center;
        content.AddChild(leave);

        BuildSpots();
        var posByKind = GetRoomSpotPositions();
        int fb = 0;
        foreach (var s in _spots)
        {
            Vector2 p;
            if (!posByKind.TryGetValue(s.Kind, out p))
            {
                p = new Vector2(40 + (fb % 4) * 220, 506);
                fb++;
            }
            // v3.62: 物件落点按 houseId 播种随机抖动——同一间屋每次进都一致
            //（记忆点不乱跳），但不同的屋落点各不相同，不再千篇一律。
            p += new Vector2(_rng.RandfRange(-18f, 18f), _rng.RandfRange(-12f, 12f));
            float w = (s.Kind == "radio" || s.Kind == "power") ? 224f : 188f;
            s.Position = p;
            s.Size = new Vector2(w, 42);
            s.CustomMinimumSize = new Vector2(w, 42);
            // v3.64: 标签不再是一排整齐的按钮，而是随手贴在物件旁的小纸签
            s.PivotOffset = s.Size * 0.5f;
            s.Rotation = Mathf.DegToRad(_rng.RandfRange(-2.2f, 2.2f));
            spotsLayer.AddChild(s);
        }
    }

    /// <summary>
    /// 按屋内格局类型（及住家变体）给出每个 loot 标签的摆放位置，
    /// 让物件正好落在对应的家具上（信压灶台、灰烬在柜子、发信机在角落等）。
    /// 坐标基于约 950×590 的屋内画布，与 RoomBg 的绘制保持一致。
    /// </summary>
    private System.Collections.Generic.Dictionary<string, Vector2> GetRoomSpotPositions()
    {
        if (_roomType == RoomType.CommOutpost)
        {
            return new System.Collections.Generic.Dictionary<string, Vector2>
            {
                ["power"]   = new Vector2(24, 272),   // 手摇发电机下方（不压住电闸箱）
                ["radio"]   = new Vector2(690, 154),  // 发信机下方（不压住表盘旋钮）
                ["letter"]  = new Vector2(430, 360),  // 电台桌上方
                ["ash"]     = new Vector2(770, 470),  // 墙角废墟
                ["chest"]   = new Vector2(250, 360),  // 木箱
                ["ammo"]    = new Vector2(340, 300),
                ["grenade"] = new Vector2(520, 300),
                ["gauze"]   = new Vector2(620, 300),
                ["medkit"]  = new Vector2(700, 360),
                ["will"]    = new Vector2(430, 200),  // 地图桌上方
                ["clue"]    = new Vector2(250, 200),
            };
        }
        if (_roomType == RoomType.BurntWarehouse)
        {
            return new System.Collections.Generic.Dictionary<string, Vector2>
            {
                ["letter"]  = new Vector2(200, 360),  // 残墙下
                ["ash"]     = new Vector2(470, 470),  // 灰烬堆
                ["chest"]   = new Vector2(620, 400),  // 木箱
                ["ammo"]    = new Vector2(300, 300),
                ["grenade"] = new Vector2(420, 280),
                ["gauze"]   = new Vector2(560, 300),
                ["medkit"]  = new Vector2(700, 320),
                ["will"]    = new Vector2(260, 200),  // 残桌
                ["clue"]    = new Vector2(430, 200),
            };
        }
        // 住家：variant 0 / 1 左右互换，让两间民居不完全一样
        if (_roomVariant != 0)
        {
            return new System.Collections.Generic.Dictionary<string, Vector2>
            {
                ["letter"]  = new Vector2(120, 430),  // 灶台（左）
                ["ash"]     = new Vector2(360, 470),  // 柜子
                ["chest"]   = new Vector2(430, 360),  // 屋中地
                ["ammo"]    = new Vector2(300, 300),
                ["grenade"] = new Vector2(520, 300),
                ["gauze"]   = new Vector2(640, 300),
                ["medkit"]  = new Vector2(700, 360),
                ["will"]    = new Vector2(760, 320),  // 炕（右）
                ["clue"]    = new Vector2(620, 300),
            };
        }
        return new System.Collections.Generic.Dictionary<string, Vector2>
        {
                ["letter"]  = new Vector2(796, 426),  // 灶台（右）
                ["ash"]     = new Vector2(856, 470),  // 柜子
            ["chest"]   = new Vector2(430, 360),  // 屋中地
            ["ammo"]    = new Vector2(300, 300),
            ["grenade"] = new Vector2(520, 300),
            ["gauze"]   = new Vector2(640, 300),
            ["medkit"]  = new Vector2(700, 360),
            ["will"]    = new Vector2(120, 320),  // 炕（左）
            ["clue"]    = new Vector2(240, 300),
        };
    }

    /// <summary>
    /// v3.64 钢笔淡彩（line-and-wash / pen &amp; wash）室内绘制工具。
    ///
    /// 与 assets/art 的美术同源：细深褐灰墨线（手绘轻重变化、断续排线）、
    /// 低饱和水彩罩染柔边（多层半透明错位叠色，不做硬边赛璐璐平涂）、
    /// 可见纸纹与水痕。全部确定性伪随机，同一间屋每次打开完全一致。
    /// </summary>
    internal static class WashPaint
    {
        public static readonly Color Paper = new(0.905f, 0.865f, 0.765f);       // 纸本色
        public static readonly Color PaperLit = new(0.945f, 0.915f, 0.835f);    // 受光纸面
        public static readonly Color PaperShade = new(0.80f, 0.75f, 0.65f);     // 背光纸面
        public static readonly Color Ink = new(0.235f, 0.195f, 0.165f);         // 深褐灰墨线
        public static readonly Color InkSoft = new(0.38f, 0.33f, 0.28f);        // 次级墨线
        public static readonly Color WallWarm = new(0.55f, 0.46f, 0.35f);       // 土坯墙罩染
        public static readonly Color WallCool = new(0.44f, 0.47f, 0.48f);       // 机房灰蓝罩染
        public static readonly Color Wood = new(0.52f, 0.38f, 0.25f);           // 木器罩染
        public static readonly Color WoodDark = new(0.34f, 0.25f, 0.18f);
        public static readonly Color ClothBlue = new(0.46f, 0.51f, 0.55f);      // 被褥 / 军毯
        public static readonly Color NightBlue = new(0.22f, 0.27f, 0.34f);      // 窗外夜色
        public static readonly Color Ember = new(0.82f, 0.42f, 0.18f);          // 灶火 / 余烬
        public static readonly Color Red = new(0.62f, 0.21f, 0.16f);            // 作战图朱红
        public static readonly Color Char = new(0.20f, 0.17f, 0.15f);           // 焦木

        /// <summary>确定性伪随机（0..1），同 seed 同结果，画面不闪。</summary>
        public static float H(float n, float seed)
            => Mathf.Abs(Mathf.Sin(n * 12.9898f + seed * 78.233f) * 43758.5453f) % 1f;

        /// <summary>
        /// 水彩罩染矩形：逐顶点 alpha 羽化的柔边 + 颜料浓淡斑 + 沉积水痕。
        /// v3.64b：旧版是 4 层硬边 DrawRect 叠加，边缘只有几像素阶梯，
        /// 实机看仍是平涂色块；这里改成外环全透明 / 内环实色的三角带，
        /// 才是真正的水彩柔边（不是赛璐璐硬边）。
        /// </summary>
        public static void Wash(CanvasItem c, Rect2 r, Color col, float alpha, float seed, int layers = 4)
        {
            SoftWash(c, r, col, alpha, seed);

            // 颜料浓淡：色块内部深浅不一，模拟水彩未干时的聚色与留白
            int n = layers < 2 ? 2 : layers;
            for (int i = 0; i < n; i++)
            {
                float bx = r.Position.X + H(seed + i * 7.3f, 61f) * r.Size.X;
                float by = r.Position.Y + H(seed + i * 11.7f, 67f) * r.Size.Y;
                float br = Mathf.Min(r.Size.X, r.Size.Y) *
                           (0.16f + H(seed + i * 3.1f, 71f) * 0.34f);
                var bc = H(seed + i, 73f) > 0.45f
                    ? new Color(col.R * 0.80f, col.G * 0.80f, col.B * 0.80f)
                    : new Color(0.96f, 0.93f, 0.85f);
                Blot(c, new Vector2(bx, by), br, bc,
                    alpha * (0.10f + 0.10f * H(seed + i, 79f)), seed + i * 5f);
            }

            // 下缘颜料沉积（水彩干掉后颜色堆在边界）
            WaterEdge(c,
                new Vector2(r.Position.X + 3f, r.Position.Y + r.Size.Y - 2f),
                new Vector2(r.Position.X + r.Size.X - 3f, r.Position.Y + r.Size.Y - 2f),
                new Color(col.R * 0.68f, col.G * 0.68f, col.B * 0.68f), seed + 17f);
        }

        /// <summary>逐顶点羽化罩染：外环 alpha=0 → 内环实色，三角带插值出柔边。</summary>
        public static void SoftWash(CanvasItem c, Rect2 r, Color col, float alpha, float seed)
        {
            const int Per = 5;
            const int N = Per * 4;
            if (r.Size.X < 2f || r.Size.Y < 2f) return;

            var outer = new Vector2[N];
            int k = 0;
            for (int i = 0; i < Per; i++)
                outer[k++] = new Vector2(r.Position.X + r.Size.X * i / Per, r.Position.Y);
            for (int i = 0; i < Per; i++)
                outer[k++] = new Vector2(r.Position.X + r.Size.X, r.Position.Y + r.Size.Y * i / Per);
            for (int i = 0; i < Per; i++)
                outer[k++] = new Vector2(r.Position.X + r.Size.X * (1f - (float)i / Per),
                                         r.Position.Y + r.Size.Y);
            for (int i = 0; i < Per; i++)
                outer[k++] = new Vector2(r.Position.X,
                                         r.Position.Y + r.Size.Y * (1f - (float)i / Per));

            // 边缘手绘抖动，避免尺子画出来的死直线。
            // v3.64b: 抖动必须随尺寸缩放 —— 小物件（如 16×14 铁锁）相邻采样点
            // 只有 3px，固定 ±1.6px 会让轮廓自交，触发 triangulation failed。
            float jit = Mathf.Min(3.2f, Mathf.Min(r.Size.X, r.Size.Y) / 10f);
            for (int i = 0; i < N; i++)
                outer[i] += new Vector2(
                    (H(seed + i * 1.9f, 5f) - 0.5f) * jit,
                    (H(seed + i * 2.7f, 9f) - 0.5f) * jit);

            float feather = Mathf.Clamp(Mathf.Min(r.Size.X, r.Size.Y) * 0.16f, 1.5f, 15f);
            float kx = Mathf.Clamp(1f - feather / Mathf.Max(1f, r.Size.X * 0.5f), 0.45f, 1f);
            float ky = Mathf.Clamp(1f - feather / Mathf.Max(1f, r.Size.Y * 0.5f), 0.45f, 1f);
            var center = r.Position + r.Size * 0.5f;
            var inner = new Vector2[N];
            for (int i = 0; i < N; i++)
                inner[i] = center + new Vector2(
                    (outer[i].X - center.X) * kx,
                    (outer[i].Y - center.Y) * ky);

            var clear = new Color(col.R, col.G, col.B, 0f);
            var full = new Color(col.R, col.G, col.B, alpha);
            for (int i = 0; i < N; i++)
            {
                int j = (i + 1) % N;
                c.DrawPolygon(new[] { outer[i], outer[j], inner[j] },
                    new[] { clear, clear, full });
                c.DrawPolygon(new[] { outer[i], inner[j], inner[i] },
                    new[] { clear, full, full });
            }
            var innerCols = new Color[N];
            for (int i = 0; i < N; i++) innerCols[i] = full;
            c.DrawPolygon(inner, innerCols);
        }

        /// <summary>柔边色斑：多层错位递减 alpha 的圆，模拟水彩晕开的一摊颜料。</summary>
        public static void Blot(CanvasItem c, Vector2 p, float radius, Color col, float alpha, float seed)
        {
            if (radius < 1f) return;
            for (int i = 5; i >= 1; i--)
            {
                float rr = radius * (0.30f + i * 0.17f);
                var q = p + new Vector2(
                    (H(seed + i, 83f) - 0.5f) * radius * 0.5f,
                    (H(seed + i, 89f) - 0.5f) * radius * 0.5f);
                c.DrawCircle(q, rr, new Color(col.R, col.G, col.B, alpha / (i + 1f)));
            }
        }

        /// <summary>水彩罩染不规则多边形（顶点带手绘抖动）。</summary>
        public static void WashPoly(CanvasItem c, Vector2[] poly, Color col, float alpha, float seed)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                float j = pass * 2.4f;
                var p = new Vector2[poly.Length];
                for (int i = 0; i < poly.Length; i++)
                    p[i] = poly[i] + new Vector2(
                        (H(seed + i * 2.1f + pass, 3f) - 0.5f) * (2f + j),
                        (H(seed + i * 3.7f + pass, 9f) - 0.5f) * (2f + j));
                c.DrawColoredPolygon(p, new Color(col.R, col.G, col.B, alpha * (0.30f + 0.18f * pass)));
            }
            c.DrawColoredPolygon(poly, new Color(col.R, col.G, col.B, alpha * 0.34f));
        }

        /// <summary>手绘墨线：分段折线 + 确定性抖动，避免尺子画出来的死直线。</summary>
        public static void Pen(CanvasItem c, Vector2 a, Vector2 b, float width, Color col, float seed, float jitter = 1.1f)
        {
            int seg = Mathf.Max(2, (int)(a.DistanceTo(b) / 42f));
            var pts = new Vector2[seg + 1];
            var n = (b - a).Normalized().Orthogonal();
            for (int i = 0; i <= seg; i++)
            {
                float t = (float)i / seg;
                float falloff = (i == 0 || i == seg) ? 0.25f : 1f;
                pts[i] = a.Lerp(b, t) + n * (H(seed + i * 1.7f, 5f) - 0.5f) * jitter * 2f * falloff;
            }
            c.DrawPolyline(pts, col, width, true);
        }

        /// <summary>矩形墨线勾边（四条手绘线，角上略微出头，像钢笔速写）。</summary>
        public static void InkRect(CanvasItem c, Rect2 r, float width, Color col, float seed)
        {
            var a = r.Position; var b = r.Position + r.Size;
            Pen(c, a + new Vector2(-1.5f, 0), new Vector2(b.X + 1.5f, a.Y), width, col, seed);
            Pen(c, new Vector2(b.X, a.Y - 1f), new Vector2(b.X, b.Y + 1.5f), width, col, seed + 3f);
            Pen(c, new Vector2(b.X + 1.5f, b.Y), new Vector2(a.X - 1f, b.Y), width, col, seed + 6f);
            Pen(c, new Vector2(a.X, b.Y + 1f), new Vector2(a.X, a.Y - 1.5f), width, col, seed + 9f);
        }

        /// <summary>断续排线阴影（水彩画的交叉排线，不是平涂黑块）。</summary>
        public static void Hatch(CanvasItem c, Rect2 r, int n, Color col, float seed, float angle = -0.55f)
        {
            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            for (int i = 0; i < n; i++)
            {
                float t = (i + 0.5f) / n;
                var o = new Vector2(r.Position.X + r.Size.X * t, r.Position.Y + H(seed + i, 13f) * 4f);
                float len = r.Size.Y * (0.55f + H(seed + i, 17f) * 0.45f);
                Pen(c, o, o + dir * len, 0.8f, new Color(col.R, col.G, col.B, 0.42f), seed + i * 2.3f, 0.7f);
            }
        }

        /// <summary>柔边水痕（多层同心半透明圆），用于灶火、灯光、余烬、月光。</summary>
        public static void Bloom(CanvasItem c, Vector2 p, float radius, Color col, float alpha, float seed)
        {
            for (int i = 4; i >= 1; i--)
            {
                float rr = radius * (0.55f + i * 0.35f);
                var q = p + new Vector2((H(seed + i, 23f) - 0.5f) * radius * 0.3f,
                                        (H(seed + i, 29f) - 0.5f) * radius * 0.3f);
                c.DrawCircle(q, rr, new Color(col.R, col.G, col.B, alpha / (i + 1f)));
            }
        }

        /// <summary>
        /// 纸纹：低对比细点 + 纸纤维短斜线，铺满整张纸。
        /// v3.64b：260 点铺 1000×640 等于看不见，提到 1400 并加入纤维斜线，
        /// 让纸本颗粒在实机分辨率下真的可见。
        /// </summary>
        public static void Grain(CanvasItem c, float W, float Hh, float seed, int count = 1400)
        {
            for (int i = 0; i < count; i++)
            {
                float x = H(seed + i * 1.13f, 31f) * W;
                float y = H(seed + i * 2.71f, 37f) * Hh;
                float a = 0.030f + H(seed + i, 41f) * 0.055f;
                var col = new Color(0.30f, 0.25f, 0.20f, a);
                if (H(seed + i, 43f) < 0.66f)
                    c.DrawCircle(new Vector2(x, y), 0.5f + H(seed + i, 47f) * 0.65f, col);
                else
                    c.DrawLine(new Vector2(x, y),
                        new Vector2(x + 2.0f + H(seed + i, 53f) * 3.5f,
                                    y - 1.2f - H(seed + i, 59f) * 1.4f), col, 0.7f, true);
            }
        }

        /// <summary>水渍：一条淡色不规则曲线，模拟水彩干掉后的边界。</summary>
        public static void WaterEdge(CanvasItem c, Vector2 a, Vector2 b, Color col, float seed)
        {
            int seg = 8;
            var pts = new Vector2[seg + 1];
            var n = (b - a).Normalized().Orthogonal();
            for (int i = 0; i <= seg; i++)
                pts[i] = a.Lerp(b, (float)i / seg)
                    + n * (H(seed + i, 53f) - 0.5f) * 5f;
            c.DrawPolyline(pts, new Color(col.R, col.G, col.B, 0.22f), 2.2f, true);
        }
    }

    /// <summary>
    /// 屋内背景：钢笔淡彩水彩风（v3.64 重绘）。
    /// 三种格局：住家 / 通信据点机房（按 1931 年野战电台史实布置）/ 焚毁仓库。
    /// 主体家具位置固定（与可点击落点对齐），装饰细节按 houseId 播种变化。
    /// </summary>
    private partial class RoomBg : Control
    {
        private readonly RoomType _type;
        private readonly int _variant;
        private readonly int _seed;

        public RoomBg(RoomType type = RoomType.Residence, int variant = 0, int seed = 0)
        {
            _type = type;
            _variant = variant;
            _seed = seed;
        }

        public override void _Draw()
        {
            float W = Size.X, H = Size.Y;
            // 纸本色打底（整张画都是画在纸上的，不再是深棕纯色块）
            DrawRect(new Rect2(0, 0, W, H), WashPaint.Paper);
            WashPaint.Wash(this, new Rect2(0, 0, W, H), WashPaint.PaperShade, 0.16f, _seed + 1f, 3);
            switch (_type)
            {
                case RoomType.CommOutpost: DrawCommOutpost(W, H); break;
                case RoomType.BurntWarehouse: DrawBurnt(W, H); break;
                default: DrawResidence(W, H, _variant); break;
            }

            // 整张画再罩一层极淡的纸面不均匀水痕：水彩纸吸色不匀，
            // 有些地方偏冷灰、有些地方偏暖，而不是一整块均匀底色。
            for (int i = 0; i < 7; i++)
            {
                float x = WashPaint.H(_seed + i * 13.1f, 97f) * W;
                float y = WashPaint.H(_seed + i * 17.7f, 101f) * H;
                WashPaint.Blot(this, new Vector2(x, y),
                    90f + WashPaint.H(_seed + i, 103f) * 150f,
                    i % 2 == 0 ? WashPaint.PaperShade : new Color(0.72f, 0.75f, 0.74f),
                    0.055f, _seed + i * 3f);
            }
            WashPaint.Grain(this, W, H, _seed + 91f);
            // 画面四周淡淡的纸边水痕
            WashPaint.WaterEdge(this, new Vector2(6, 8),
                new Vector2(W - 6, 8 + (WashPaint.H(_seed + 2f, _seed + 0.77f) - 0.5f) * 3f),
                WashPaint.InkSoft, _seed + 3f);
            WashPaint.WaterEdge(this, new Vector2(6, H - 8), new Vector2(W - 6, H - 8),
                WashPaint.InkSoft, _seed + 5f);
        }

        private float H(float n) => WashPaint.H(n, _seed + 0.77f);
        private Color Shade(Color c, float k = 0.72f) => new(c.R * k, c.G * k, c.B * k, c.A);
        private Color WithA(Color c, float a) => new(c.R, c.G, c.B, a);

        // ---------- 共用：墙 / 地 ----------
        private void DrawWallAndFloor(float W, float Hh, float fy, Color wall, float seed)
        {
            // 后墙：上浅下深的水彩罩染
            // v3.64b：主罩染浓度下调，让纸白留白透出来 —— 钢笔「淡」彩，
            // 不是把整面墙涂满不透明色。
            WashPaint.Wash(this, new Rect2(0, 0, W, fy), WashPaint.PaperLit, 0.22f, seed + 1f, 3);
            WashPaint.Wash(this, new Rect2(0, 0, W, fy), wall, 0.31f, seed + 2f, 4);
            WashPaint.Wash(this, new Rect2(0, fy * 0.62f, W, fy * 0.38f), Shade(wall), 0.20f, seed + 4f, 3);

            // 墙板横缝：断续手绘墨线，轻重不一
            for (float y = 34f; y < fy - 6f; y += 44f)
            {
                float seg = 0.6f + H(y) * 0.4f;
                WashPaint.Pen(this, new Vector2(0, y), new Vector2(W * seg, y + (H(y + 1f) - 0.5f) * 2f),
                    0.9f, WithA(WashPaint.InkSoft, 0.55f), seed + y, 1.4f);
                if (seg < 0.98f)
                    WashPaint.Pen(this, new Vector2(W * (seg + 0.04f), y + 1f),
                        new Vector2(W, y - 0.5f), 0.8f, WithA(WashPaint.InkSoft, 0.40f), seed + y + 7f, 1.2f);
            }
            // 墙角排线阴影
            WashPaint.Hatch(this, new Rect2(0, 0, 34, fy), 9, WashPaint.InkSoft, seed + 21f);
            WashPaint.Hatch(this, new Rect2(W - 34, 0, 34, fy), 9, WashPaint.InkSoft, seed + 31f);

            // 地面：土黄 / 水泥灰罩染 + 透视斜板缝
            WashPaint.Wash(this, new Rect2(0, fy, W, Hh - fy), Shade(wall, 0.78f), 0.40f, seed + 8f, 4);
            WashPaint.Pen(this, new Vector2(0, fy), new Vector2(W, fy), 1.6f, WashPaint.Ink, seed + 11f, 1.2f);
            for (float x = 10f; x < W + 90f; x += 62f)
                WashPaint.Pen(this, new Vector2(x, fy), new Vector2(x - 78f, Hh),
                    0.85f, WithA(WashPaint.InkSoft, 0.55f), seed + x, 1.6f);
            WashPaint.WaterEdge(this, new Vector2(0, fy + 12f), new Vector2(W, fy + 8f),
                WashPaint.InkSoft, seed + 13f);
        }

        /// <summary>纸窗：深蓝夜色水彩 + 木格墨线 + 月色柔晕。</summary>
        private void DrawPaperWindow(float x, float y, float w, float h, float seed)
        {
            WashPaint.Wash(this, new Rect2(x, y, w, h), WashPaint.NightBlue, 0.82f, seed + 1f, 4);
            // 窗纸受月光照亮的一角
            WashPaint.Bloom(this, new Vector2(x + w * 0.72f, y + h * 0.30f), 22f,
                new Color(0.92f, 0.90f, 0.72f), 0.55f, seed);
            WashPaint.InkRect(this, new Rect2(x, y, w, h), 2.2f, WashPaint.Ink, seed + 2f);
            WashPaint.Pen(this, new Vector2(x + w / 2, y), new Vector2(x + w / 2, y + h),
                1.5f, WashPaint.Ink, seed + 3f);
            WashPaint.Pen(this, new Vector2(x, y + h / 2), new Vector2(x + w, y + h / 2),
                1.5f, WashPaint.Ink, seed + 4f);
            WashPaint.Hatch(this, new Rect2(x + 2, y + h - 24, w - 4, 22), 6, WashPaint.Ink, seed + 5f);
        }

        /// <summary>木桌：水彩木色罩染 + 手绘墨线 + 桌腿排线。</summary>
        private void DrawTable(float x, float y, float w, float seed)
        {
            WashPaint.Wash(this, new Rect2(x, y, w, 16f), WashPaint.Wood, 0.72f, seed, 3);
            WashPaint.InkRect(this, new Rect2(x, y, w, 16f), 1.6f, WashPaint.Ink, seed + 1f);
            WashPaint.Pen(this, new Vector2(x + 4, y + 4), new Vector2(x + w - 4, y + 4),
                0.8f, WithA(WashPaint.InkSoft, 0.6f), seed + 2f);
            foreach (float lx in new[] { x + 20f, x + w - 20f })
            {
                WashPaint.Wash(this, new Rect2(lx - 5f, y + 16f, 10f, 92f), WashPaint.WoodDark, 0.62f, seed + lx, 2);
                WashPaint.Pen(this, new Vector2(lx, y + 16f), new Vector2(lx + (H(lx) - 0.5f) * 4f, y + 108f),
                    3.2f, WashPaint.Ink, seed + lx + 1f);
            }
            WashPaint.Hatch(this, new Rect2(x, y + 16f, w, 20f), 10, WashPaint.Ink, seed + 7f);
        }

        /// <summary>木箱 / 弹药箱：水彩木色 + 板缝 + 铁扣。</summary>
        private void DrawCrate(float x, float y, float w, float h, float seed, bool locked = false)
        {
            WashPaint.Wash(this, new Rect2(x, y, w, h), WashPaint.Wood, 0.66f, seed, 3);
            WashPaint.Wash(this, new Rect2(x, y + h * 0.62f, w, h * 0.38f), WashPaint.WoodDark, 0.40f, seed + 2f, 2);
            WashPaint.InkRect(this, new Rect2(x, y, w, h), 1.9f, WashPaint.Ink, seed + 3f);
            WashPaint.Pen(this, new Vector2(x, y + h * 0.46f), new Vector2(x + w, y + h * 0.46f),
                1.1f, WithA(WashPaint.Ink, 0.75f), seed + 4f);
            // 斜撑
            WashPaint.Pen(this, new Vector2(x + 5, y + 5), new Vector2(x + w - 5, y + h - 5),
                1.0f, WithA(WashPaint.InkSoft, 0.55f), seed + 5f);
            if (locked)
            {
                WashPaint.Bloom(this, new Vector2(x + w / 2, y + h * 0.46f), 12f,
                    new Color(0.40f, 0.36f, 0.30f), 0.35f, seed + 6f);
                DrawRect(new Rect2(x + w / 2 - 8f, y + h * 0.40f, 16f, 14f),
                    new Color(0.36f, 0.32f, 0.26f, 0.85f));
                WashPaint.InkRect(this, new Rect2(x + w / 2 - 8f, y + h * 0.40f, 16f, 14f),
                    1.1f, WashPaint.Ink, seed + 7f);
            }
            WashPaint.Hatch(this, new Rect2(x, y + h - 14f, w, 12f), 7, WashPaint.Ink, seed + 8f);
        }

        // ---------- 住家：土炕 / 灶台 / 木桌 / 柜子 / 水缸 ----------
        private void DrawResidence(float W, float Hh, int variant)
        {
            bool flip = variant != 0;
            float fy = Hh * 0.64f;
            DrawWallAndFloor(W, Hh, fy, WashPaint.WallWarm, _seed + 10f);

            // 纸窗（左右随变体）
            float wx = flip ? W - 196f : 36f;
            DrawPaperWindow(wx, 36f, 154f, 108f, _seed + 20f);

            // 土炕：砖台 + 层叠被褥（水彩罩染，不再是纯色矩形）
            float kx = flip ? W - 262f : 38f, ky = fy - 92f, kw = 224f, kh = 90f;
            WashPaint.Wash(this, new Rect2(kx, ky, kw, kh), WashPaint.WallWarm, 0.62f, _seed + 30f, 3);
            WashPaint.InkRect(this, new Rect2(kx, ky, kw, kh), 1.8f, WashPaint.Ink, _seed + 31f);
            for (float bx = kx + 16f; bx < kx + kw - 10f; bx += 34f)
                WashPaint.Pen(this, new Vector2(bx, ky + 4), new Vector2(bx, ky + kh - 4),
                    0.8f, WithA(WashPaint.InkSoft, 0.5f), _seed + bx);
            // 叠好的棉被（两床，蓝灰 + 米白）
            WashPaint.WashPoly(this, new[] {
                new Vector2(kx + 14, ky + 10), new Vector2(kx + kw - 16, ky + 8),
                new Vector2(kx + kw - 12, ky + 34), new Vector2(kx + 12, ky + 36),
            }, WashPaint.ClothBlue, 0.78f, _seed + 32f);
            WashPaint.WashPoly(this, new[] {
                new Vector2(kx + 20, ky + 38), new Vector2(kx + kw - 22, ky + 36),
                new Vector2(kx + kw - 18, ky + 58), new Vector2(kx + 18, ky + 60),
            }, WashPaint.PaperLit, 0.72f, _seed + 33f);
            WashPaint.Pen(this, new Vector2(kx + 14, ky + 36), new Vector2(kx + kw - 12, ky + 34),
                1.1f, WashPaint.Ink, _seed + 34f);
            WashPaint.Hatch(this, new Rect2(kx + 10, ky + kh - 20, kw - 20, 18), 9, WashPaint.Ink, _seed + 35f);

            // 灶台：砖砌 + 灶口暖光 + 铁锅
            float sx = flip ? 62f : W - 186f, sy = Hh - 158f, sw = 132f, sh = 128f;
            WashPaint.Wash(this, new Rect2(sx, sy, sw, sh), Shade(WashPaint.WallWarm, 0.62f), 0.70f, _seed + 40f, 3);
            WashPaint.InkRect(this, new Rect2(sx, sy, sw, sh), 2.0f, WashPaint.Ink, _seed + 41f);
            for (float by = sy + 24f; by < sy + sh; by += 26f)
                WashPaint.Pen(this, new Vector2(sx, by), new Vector2(sx + sw, by),
                    0.8f, WithA(WashPaint.InkSoft, 0.55f), _seed + by);
            // 灶膛 + 火光水痕
            WashPaint.Bloom(this, new Vector2(sx + sw * 0.5f, sy + 76f), 34f, WashPaint.Ember, 0.50f, _seed + 42f);
            DrawCircle(new Vector2(sx + sw * 0.5f, sy + 76f), 17f, new Color(0.16f, 0.13f, 0.11f, 0.85f));
            WashPaint.Bloom(this, new Vector2(sx + sw * 0.5f, sy + 76f), 11f,
                new Color(0.95f, 0.72f, 0.36f), 0.60f, _seed + 43f);
            // 铁锅（墨线椭圆 + 水彩罩染）
            WashPaint.WashPoly(this, new[] {
                new Vector2(sx + 12, sy - 6), new Vector2(sx + sw - 12, sy - 6),
                new Vector2(sx + sw - 26, sy + 12), new Vector2(sx + 26, sy + 12),
            }, new Color(0.28f, 0.30f, 0.31f), 0.78f, _seed + 44f);
            WashPaint.Pen(this, new Vector2(sx + 12, sy - 6), new Vector2(sx + sw - 12, sy - 6),
                2.0f, WashPaint.Ink, _seed + 45f);

            // 木桌（中下）
            DrawTable(W * 0.39f, Hh - 126f, 236f, _seed + 50f);
            // 桌上的粗瓷碗 + 一小叠纸
            WashPaint.Bloom(this, new Vector2(W * 0.39f + 58f, Hh - 132f), 14f,
                WashPaint.PaperLit, 0.42f, _seed + 51f);
            WashPaint.Pen(this, new Vector2(W * 0.39f + 44f, Hh - 130f),
                new Vector2(W * 0.39f + 72f, Hh - 130f), 1.6f, WashPaint.Ink, _seed + 52f);
            WashPaint.WashPoly(this, new[] {
                new Vector2(W * 0.39f + 150, Hh - 138), new Vector2(W * 0.39f + 194, Hh - 140),
                new Vector2(W * 0.39f + 196, Hh - 126), new Vector2(W * 0.39f + 148, Hh - 126),
            }, WashPaint.PaperLit, 0.80f, _seed + 53f);

            // 柜子（墙角，双门 + 把手）
            float cx = flip ? 300f : W - 158f, cy = Hh - 156f, cw = 126f, ch = 132f;
            WashPaint.Wash(this, new Rect2(cx, cy, cw, ch), WashPaint.WoodDark, 0.62f, _seed + 60f, 3);
            WashPaint.InkRect(this, new Rect2(cx, cy, cw, ch), 1.9f, WashPaint.Ink, _seed + 61f);
            WashPaint.Pen(this, new Vector2(cx + cw / 2, cy), new Vector2(cx + cw / 2, cy + ch),
                1.3f, WashPaint.Ink, _seed + 62f);
            DrawCircle(new Vector2(cx + cw / 2 - 12, cy + ch / 2), 3f, new Color(0.62f, 0.55f, 0.40f));
            DrawCircle(new Vector2(cx + cw / 2 + 12, cy + ch / 2), 3f, new Color(0.62f, 0.55f, 0.40f));
            WashPaint.Hatch(this, new Rect2(cx + 4, cy + 4, cw - 8, 18), 7, WashPaint.Ink, _seed + 63f);

            // 水缸（点缀）+ 墙上的干辣椒串（生活气）
            float vx = flip ? W - 220f : 218f;
            WashPaint.WashPoly(this, new[] {
                new Vector2(vx - 22, Hh - 58), new Vector2(vx + 22, Hh - 58),
                new Vector2(vx + 16, Hh - 16), new Vector2(vx - 16, Hh - 16),
            }, new Color(0.36f, 0.40f, 0.42f), 0.72f, _seed + 70f);
            WashPaint.Pen(this, new Vector2(vx - 22, Hh - 58), new Vector2(vx + 22, Hh - 58),
                1.6f, WashPaint.Ink, _seed + 71f);
            for (int i = 0; i < 5; i++)
            {
                float cxp = (flip ? 60f : W - 90f) + (H(i + 80f) - 0.5f) * 26f;
                float cyp = 150f + i * 17f;
                WashPaint.Pen(this, new Vector2(cxp, cyp), new Vector2(cxp + 9f, cyp + 8f),
                    2.6f, new Color(0.62f, 0.25f, 0.16f, 0.75f), _seed + 72f + i);
            }
            // 地面水渍与墙脚阴影
            WashPaint.WaterEdge(this, new Vector2(0, Hh - 24f), new Vector2(W, Hh - 30f),
                WashPaint.InkSoft, _seed + 75f);
        }

        // ---------- 通信据点机房（1931 野战电台史实布局） ----------
        /// <summary>
        /// 史实依据：1931 年东北军 / 关东军野战通信据点为「收发报机 + 手摇发电机
        /// （或蓄电池）+ 电键 + 耳机 + 天线引入 + 电闸配电 + 密码本 / 作战地图 + 电台桌」
        /// 的组合。布置按操作动线：左墙配电与人力发电 → 中桌操作电键、抄收电文
        /// → 右侧发信机靠天线引入口；墙上挂作战地图，地面走线绕开踏板。
        /// </summary>
        private void DrawCommOutpost(float W, float Hh)
        {
            float fy = Hh * 0.66f;
            DrawWallAndFloor(W, Hh, fy, WashPaint.WallCool, _seed + 110f);

            // 墙上作战地图（米黄纸 + 铁路线 + 朱红圈注 + 图钉）
            float mx = W * 0.30f, my = 28f, mw = 232f, mh = 138f;
            WashPaint.Wash(this, new Rect2(mx, my, mw, mh), WashPaint.PaperLit, 0.86f, _seed + 120f, 3);
            WashPaint.InkRect(this, new Rect2(mx, my, mw, mh), 1.8f, WashPaint.Ink, _seed + 121f);
            WashPaint.Pen(this, new Vector2(mx + 14, my + 62), new Vector2(mx + mw - 16, my + 50),
                1.8f, WithA(WashPaint.InkSoft, 0.8f), _seed + 122f);
            WashPaint.Pen(this, new Vector2(mx + 14, my + 100), new Vector2(mx + mw - 40, my + 70),
                1.4f, WithA(WashPaint.InkSoft, 0.7f), _seed + 123f);
            DrawArc(new Vector2(mx + 120, my + 80), 20f, 0, Mathf.Tau, 18,
                new Color(WashPaint.Red.R, WashPaint.Red.G, WashPaint.Red.B, 0.75f), 1.8f, true);
            DrawCircle(new Vector2(mx + 22, my + 16), 3.2f, WashPaint.Red);
            DrawCircle(new Vector2(mx + mw - 22, my + 16), 3.2f, WashPaint.Red);
            WashPaint.Hatch(this, new Rect2(mx + 4, my + mh - 16, mw - 8, 14), 8, WashPaint.Ink, _seed + 124f);

            // 左墙：电闸箱（刀闸 + 瓷瓶 + 导线）
            float ex = 22f, ey = 42f, ew = 152f, eh = 100f;
            WashPaint.Wash(this, new Rect2(ex, ey, ew, eh), new Color(0.30f, 0.30f, 0.29f), 0.72f, _seed + 130f, 3);
            WashPaint.InkRect(this, new Rect2(ex, ey, ew, eh), 2.0f, WashPaint.Ink, _seed + 131f);
            for (int i = 0; i < 3; i++)
            {
                float px = ex + 30f + i * 44f;
                DrawCircle(new Vector2(px, ey + 30f), 5f, new Color(0.78f, 0.74f, 0.62f, 0.9f));
                WashPaint.Pen(this, new Vector2(px, ey + 34f), new Vector2(px + (i - 1) * 8f, ey + 62f),
                    2.2f, new Color(0.45f, 0.40f, 0.32f), _seed + 132f + i);
            }
            WashPaint.Pen(this, new Vector2(ex + 8, ey + eh - 14), new Vector2(ex + ew - 8, ey + eh - 14),
                1.4f, WashPaint.Ink, _seed + 135f);

            // 手摇发电机（木箱机身 + 飞轮 + 曲柄 + 皮带）
            float gx = 36f, gy = 156f, gw = 138f, gh = 104f;
            WashPaint.Wash(this, new Rect2(gx, gy, gw, gh), WashPaint.WoodDark, 0.66f, _seed + 140f, 3);
            WashPaint.InkRect(this, new Rect2(gx, gy, gw, gh), 2.0f, WashPaint.Ink, _seed + 141f);
            DrawArc(new Vector2(gx + 52f, gy + 54f), 26f, 0, Mathf.Tau, 20,
                new Color(0.34f, 0.32f, 0.28f, 0.9f), 4.0f, true);
            DrawCircle(new Vector2(gx + 52f, gy + 54f), 8f, new Color(0.26f, 0.24f, 0.20f, 0.9f));
            WashPaint.Pen(this, new Vector2(gx + 52f, gy + 54f), new Vector2(gx + 76f, gy + 34f),
                3.0f, WashPaint.Ink, _seed + 142f);
            DrawCircle(new Vector2(gx + 78f, gy + 32f), 6f, new Color(0.52f, 0.44f, 0.32f));
            // 引到电闸箱的导线
            WashPaint.Pen(this, new Vector2(gx + gw - 6f, gy + 12f), new Vector2(ex + ew - 10f, ey + eh + 6f),
                1.6f, WithA(WashPaint.Ink, 0.8f), _seed + 143f, 3.2f);
            WashPaint.Hatch(this, new Rect2(gx, gy + gh - 16, gw, 14), 7, WashPaint.Ink, _seed + 144f);

            // 电台桌（中下）：铺电报纸、电键、耳机、油灯
            float tx = W * 0.33f, ty = Hh - 138f, tw = 292f;
            DrawTable(tx, ty, tw, _seed + 150f);
            // 电报纸
            WashPaint.WashPoly(this, new[] {
                new Vector2(tx + 24, ty - 26), new Vector2(tx + 128, ty - 30),
                new Vector2(tx + 130, ty - 4), new Vector2(tx + 22, ty - 4),
            }, WashPaint.PaperLit, 0.92f, _seed + 151f);
            for (int i = 0; i < 4; i++)
                WashPaint.Pen(this, new Vector2(tx + 30, ty - 22 + i * 5f),
                    new Vector2(tx + 118 - i * 8f, ty - 22 + i * 5f), 0.8f,
                    WithA(WashPaint.InkSoft, 0.6f), _seed + 152f + i);
            // 电键（发报键）：底座 + 杠杆 +  knob
            WashPaint.Wash(this, new Rect2(tx + 162f, ty - 16f, 58f, 14f),
                new Color(0.30f, 0.28f, 0.24f), 0.80f, _seed + 156f, 2);
            WashPaint.InkRect(this, new Rect2(tx + 162f, ty - 16f, 58f, 14f), 1.4f, WashPaint.Ink, _seed + 157f);
            WashPaint.Pen(this, new Vector2(tx + 168f, ty - 16f), new Vector2(tx + 214f, ty - 28f),
                2.4f, WashPaint.Ink, _seed + 158f);
            DrawCircle(new Vector2(tx + 214f, ty - 30f), 7f, new Color(0.24f, 0.21f, 0.18f, 0.9f));
            // 耳机（两个听筒 + 弧形头带）
            DrawArc(new Vector2(tx + 252f, ty - 6f), 17f, Mathf.Pi, Mathf.Tau, 12,
                WashPaint.Ink, 2.2f, true);
            DrawCircle(new Vector2(tx + 235f, ty - 4f), 5f, new Color(0.28f, 0.26f, 0.23f));
            DrawCircle(new Vector2(tx + 269f, ty - 4f), 5f, new Color(0.28f, 0.26f, 0.23f));
            // 油灯（暖光水痕）
            WashPaint.Bloom(this, new Vector2(tx + 16f, ty - 24f), 22f,
                new Color(0.92f, 0.72f, 0.38f), 0.52f, _seed + 160f);
            WashPaint.Pen(this, new Vector2(tx + 10f, ty - 4f), new Vector2(tx + 22f, ty - 4f),
                3.0f, WashPaint.Ink, _seed + 161f);

            // 右墙：无线电发信机（铁箱 + 表盘 + 旋钮 + 散热栅 + 电子管）
            float rx = W - 244f, ry = 34f, rw = 208f, rh = 104f;
            WashPaint.Wash(this, new Rect2(rx, ry, rw, rh), new Color(0.36f, 0.35f, 0.32f), 0.74f, _seed + 170f, 3);
            WashPaint.InkRect(this, new Rect2(rx, ry, rw, rh), 2.2f, WashPaint.Ink, _seed + 171f);
            // 波段表盘
            DrawArc(new Vector2(rx + 46f, ry + 42f), 24f, 0, Mathf.Tau, 20,
                new Color(0.86f, 0.82f, 0.68f, 0.9f), 7f, true);
            DrawCircle(new Vector2(rx + 46f, ry + 42f), 18f, new Color(0.78f, 0.74f, 0.60f, 0.75f));
            WashPaint.Pen(this, new Vector2(rx + 46f, ry + 42f), new Vector2(rx + 58f, ry + 30f),
                1.8f, WashPaint.Red, _seed + 172f);
            // 旋钮
            for (int i = 0; i < 3; i++)
            {
                float kx = rx + 104f + i * 34f;
                DrawCircle(new Vector2(kx, ry + 42f), 9f, new Color(0.22f, 0.20f, 0.18f, 0.9f));
                DrawCircle(new Vector2(kx, ry + 42f), 4f, new Color(0.62f, 0.56f, 0.44f, 0.9f));
            }
            // 散热栅
            for (int i = 0; i < 7; i++)
                WashPaint.Pen(this, new Vector2(rx + 18f + i * 26f, ry + 76f),
                    new Vector2(rx + 18f + i * 26f, ry + 92f), 1.2f,
                    WithA(WashPaint.Ink, 0.7f), _seed + 173f + i);
            // 天线引入线：穿墙上出画，带绝缘子
            WashPaint.Pen(this, new Vector2(rx + rw - 20f, ry), new Vector2(rx + rw - 20f, -26f),
                2.0f, WashPaint.Ink, _seed + 180f);
            DrawCircle(new Vector2(rx + rw - 20f, ry + 4f), 6f, new Color(0.80f, 0.76f, 0.62f, 0.9f));
            WashPaint.Pen(this, new Vector2(rx + rw - 20f, -26f), new Vector2(W - 26f, -34f),
                1.6f, WashPaint.InkSoft, _seed + 181f);

            // 蓄电池组（木箱里的玻璃瓶）+ 备用木箱
            DrawCrate(250f, Hh - 122f, 100f, 98f, _seed + 190f);
            for (int i = 0; i < 3; i++)
            {
                float bx = 264f + i * 30f;
                WashPaint.Wash(this, new Rect2(bx, Hh - 138f, 20f, 24f),
                    new Color(0.44f, 0.52f, 0.48f), 0.70f, _seed + 191f + i, 2);
                WashPaint.InkRect(this, new Rect2(bx, Hh - 138f, 20f, 24f), 1.0f, WashPaint.Ink, _seed + 194f + i);
            }
            DrawCrate(366f, Hh - 108f, 84f, 84f, _seed + 200f);
            // 沿地走线（从发电机绕到电台桌，再上发信机）
            WashPaint.Pen(this, new Vector2(gx + gw, Hh - 40f), new Vector2(tx - 10f, Hh - 44f),
                1.6f, WithA(WashPaint.Ink, 0.7f), _seed + 210f, 4.0f);
            WashPaint.Pen(this, new Vector2(tx + tw + 8f, Hh - 46f), new Vector2(rx + 20f, ry + rh + 22f),
                1.4f, WithA(WashPaint.Ink, 0.6f), _seed + 211f, 4.0f);
            // 墙角废墟（灰烬水彩团）与遗书纸片
            WashPaint.Bloom(this, new Vector2(W - 74f, Hh - 56f), 34f,
                new Color(0.32f, 0.30f, 0.28f), 0.42f, _seed + 220f);
            WashPaint.WashPoly(this, new[] {
                new Vector2(W - 108, Hh - 66), new Vector2(W - 62, Hh - 70),
                new Vector2(W - 54, Hh - 48), new Vector2(W - 104, Hh - 46),
            }, WashPaint.PaperLit, 0.66f, _seed + 221f);
        }

        // ---------- 仓库 / 焚毁的院落：焦梁 / 破墙透光 / 灰烬 ----------
        private void DrawBurnt(float W, float Hh)
        {
            float fy = Hh * 0.62f;
            // 焦墙：深灰褐罩染，不是纯黑块
            DrawWallAndFloor(W, Hh, fy, WashPaint.Char, _seed + 310f);
            WashPaint.Wash(this, new Rect2(0, 0, W, fy), new Color(0.14f, 0.12f, 0.11f), 0.55f, _seed + 311f, 4);
            // 炭化纹理：不规则墨线 + 交叉排线
            for (int i = 0; i < 12; i++)
            {
                float x = 20f + H(320f + i) * (W - 40f);
                float y = 20f + H(330f + i) * (fy - 40f);
                WashPaint.Pen(this, new Vector2(x, y), new Vector2(x + 30f + H(340f + i) * 40f, y + 22f),
                    1.1f, WithA(WashPaint.Ink, 0.5f), _seed + 350f + i, 3f);
            }
            WashPaint.Hatch(this, new Rect2(W * 0.42f, 0, W * 0.34f, fy), 14,
                WashPaint.Ink, _seed + 360f, 0.5f);

            // 透光破洞：冷蓝天光水痕 + 参差墨边
            float hx = W - 232f, hy = 28f, hw = 162f, hh = 126f;
            WashPaint.Wash(this, new Rect2(hx, hy, hw, hh), new Color(0.50f, 0.56f, 0.60f), 0.70f, _seed + 370f, 4);
            WashPaint.Bloom(this, new Vector2(hx + hw * 0.5f, hy + hh * 0.4f), 46f,
                WashPaint.PaperLit, 0.48f, _seed + 371f);
            WashPaint.InkRect(this, new Rect2(hx, hy, hw, hh), 2.6f, WashPaint.Ink, _seed + 372f);

            // 斜塌的房梁（粗墨线 + 焦木罩染，交叉）
            WashPaint.WashPoly(this, new[] {
                new Vector2(36, 12), new Vector2(66, 6), new Vector2(W * 0.52f + 12, fy + 34),
                new Vector2(W * 0.52f - 10, fy + 40),
            }, WashPaint.WoodDark, 0.72f, _seed + 380f);
            WashPaint.Pen(this, new Vector2(44, 12), new Vector2(W * 0.52f, fy + 36),
                7.0f, WashPaint.Ink, _seed + 381f, 3.0f);
            WashPaint.WashPoly(this, new[] {
                new Vector2(W - 64, 6), new Vector2(W - 34, 14), new Vector2(W * 0.42f + 10, fy + 52),
                new Vector2(W * 0.42f - 12, fy + 46),
            }, WashPaint.WoodDark, 0.66f, _seed + 382f);
            WashPaint.Pen(this, new Vector2(W - 48, 10), new Vector2(W * 0.42f, fy + 48),
                6.0f, WashPaint.Ink, _seed + 383f, 3.0f);

            // 残货架（断裂木条）
            float shx = W - 262f, shy = 196f;
            WashPaint.Wash(this, new Rect2(shx, shy, 158f, 12f), WashPaint.WoodDark, 0.66f, _seed + 390f, 2);
            WashPaint.Pen(this, new Vector2(shx - 4, shy), new Vector2(shx + 120f, shy + 4),
                3.0f, WashPaint.Ink, _seed + 391f);
            WashPaint.Pen(this, new Vector2(shx + 20, shy - 52f), new Vector2(shx + 18, shy),
                3.0f, WashPaint.Ink, _seed + 392f);
            DrawCrate(shx + 10f, shy - 50f, 44f, 40f, _seed + 393f);
            DrawCrate(shx + 74f, shy - 38f, 48f, 34f, _seed + 394f);

            // 木箱（中）+ 残桌（左）
            DrawCrate(600f, Hh - 132f, 112f, 106f, _seed + 400f, locked: true);
            DrawCrate(470f, Hh - 112f, 92f, 90f, _seed + 401f);
            DrawTable(226f, Hh - 122f, 152f, _seed + 402f);

            // 灰烬堆（灰蓝水彩团 + 余烬）
            WashPaint.Bloom(this, new Vector2(470f, Hh - 34f), 46f,
                new Color(0.30f, 0.29f, 0.27f), 0.55f, _seed + 410f);
            for (int i = 0; i < 5; i++)
            {
                float px = 440f + H(420f + i) * 70f;
                float py = Hh - 30f - H(430f + i) * 18f;
                WashPaint.Bloom(this, new Vector2(px, py), 7f, WashPaint.Ember, 0.46f, _seed + 440f + i);
            }
            // 地上散落的焦纸片
            for (int i = 0; i < 4; i++)
            {
                float px = 120f + H(450f + i) * 300f;
                float py = Hh - 40f - H(460f + i) * 60f;
                WashPaint.WashPoly(this, new[] {
                    new Vector2(px, py), new Vector2(px + 18, py - 3),
                    new Vector2(px + 20, py + 10), new Vector2(px + 2, py + 12),
                }, WashPaint.PaperShade, 0.60f, _seed + 470f + i);
            }
            WashPaint.WaterEdge(this, new Vector2(0, Hh - 20f), new Vector2(W, Hh - 26f),
                WashPaint.InkSoft, _seed + 480f);
        }
    }
}
