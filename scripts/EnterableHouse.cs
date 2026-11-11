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
            BgColor = s.Done ? new Color(0.17f, 0.15f, 0.13f) : new Color(0.905f, 0.855f, 0.735f),
            BorderColor = new Color(0.24f, 0.20f, 0.16f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 8, ContentMarginBottom = 8,
        };
        s.AddThemeStyleboxOverride("normal", normal);
        var hover = new StyleBoxFlat
        {
            BgColor = s.Done ? new Color(0.20f, 0.18f, 0.15f) : new Color(0.95f, 0.91f, 0.80f),
            BorderColor = new Color(0.24f, 0.20f, 0.16f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 8, ContentMarginBottom = 8,
        };
        s.AddThemeStyleboxOverride("hover", hover);
        s.AddThemeColorOverride("font_color", s.Done ? new Color(0.45f, 0.42f, 0.36f) : new Color(0.18f, 0.15f, 0.12f));
        s.AddThemeFontSizeOverride("font_size", 15);
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

        var room = new RoomBg(_roomType, _roomVariant);
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
        title.AddThemeColorOverride("font_color", new Color(0.92f, 0.88f, 0.76f));
        title.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        title.OffsetTop = 6;
        title.HorizontalAlignment = HorizontalAlignment.Center;
        content.AddChild(title);

        _hint = new Label { Text = "在屋子里翻找能用的东西，点一下就能查看。按 E 离开。" };
        _hint.AddThemeFontSizeOverride("font_size", 14);
        _hint.AddThemeColorOverride("font_color", new Color(0.64f, 0.59f, 0.50f));
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
            float w = (s.Kind == "radio" || s.Kind == "power") ? 224f : 188f;
            s.Position = p;
            s.Size = new Vector2(w, 46);
            s.CustomMinimumSize = new Vector2(w, 46);
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
                ["power"]   = new Vector2(34, 64),    // 发电机 / 电闸（左）
                ["radio"]   = new Vector2(700, 50),   // 发信机（右墙）
                ["letter"]  = new Vector2(430, 360),  // 地图桌
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
    /// 屋内背景：三种固定常识格局（住家 / 通信据点机房 / 仓库焚毁）。
    /// 纯程序绘制（与项目其余 InkStyle 一致），不引入新贴图资产。
    /// </summary>
    private partial class RoomBg : Control
    {
        private readonly RoomType _type;
        private readonly int _variant;
        public RoomBg(RoomType type = RoomType.Residence, int variant = 0)
        {
            _type = type;
            _variant = variant;
        }
        public override void _Draw()
        {
            float W = Size.X, H = Size.Y;
            switch (_type)
            {
                case RoomType.CommOutpost: DrawCommOutpost(W, H); break;
                case RoomType.BurntWarehouse: DrawBurnt(W, H); break;
                default: DrawResidence(W, H, _variant); break;
            }
        }
        // ---------- 住家：炕 / 灶台 / 桌子 / 柜子 ----------
        private void DrawResidence(float W, float H, int variant)
        {
            bool flip = variant != 0;
            float fy = H * 0.64f;

            // 后墙（暖色木墙）
            DrawRect(new Rect2(0, 0, W, fy), new Color(0.18f, 0.14f, 0.11f));
            for (float y = 30; y < fy; y += 42)
                DrawLine(new Vector2(0, y), new Vector2(W, y), new Color(0.13f, 0.10f, 0.08f), 1);

            // 地板（透视斜线）
            DrawRect(new Rect2(0, fy, W, H - fy), new Color(0.11f, 0.085f, 0.06f));
            for (float x = 12; x < W; x += 56)
                DrawLine(new Vector2(x, fy), new Vector2(x - 70, H), new Color(0.075f, 0.058f, 0.04f), 1);

            // 夜窗（外头初秋夜色 + 月）— variant 决定左右
            float wx = flip ? W - 184f : 34f, wy = 34f, ww = 150f, wh = 104f;
            DrawRect(new Rect2(wx, wy, ww, wh), new Color(0.05f, 0.07f, 0.12f));
            DrawLine(new Vector2(wx + ww / 2, wy), new Vector2(wx + ww / 2, wy + wh), new Color(0.22f, 0.17f, 0.13f), 3);
            DrawLine(new Vector2(wx, wy + wh / 2), new Vector2(wx + ww, wy + wh / 2), new Color(0.22f, 0.17f, 0.13f), 3);
            DrawCircle(new Vector2(wx + 112, wy + 30), 12, new Color(0.86f, 0.83f, 0.60f, 0.9f));

            // 炕（床，靠一侧的台子）
            float kx = flip ? W - 250f : 36f, ky = fy - 86f, kw = 214f, kh = 86f;
            DrawRect(new Rect2(kx, ky, kw, kh), new Color(0.20f, 0.15f, 0.11f));
            DrawRect(new Rect2(kx + 10, ky + 10, kw - 20, 18), new Color(0.30f, 0.24f, 0.18f)); // 被褥
            DrawRect(new Rect2(kx + 10, ky + 34, kw - 20, 16), new Color(0.26f, 0.21f, 0.16f));
            for (float x = kx + 14; x < kx + kw - 10; x += 30)
                DrawLine(new Vector2(x, ky), new Vector2(x, ky + kh), new Color(0.14f, 0.11f, 0.08f), 1);

            // 灶台（另一侧，砖砌 + 灶火）
            float sx = flip ? 60f : W - 168f, sy = H - 150f, sw = 124f, sh = 124f;
            DrawRect(new Rect2(sx, sy, sw, sh), new Color(0.08f, 0.07f, 0.06f));
            DrawRect(new Rect2(sx + 14, sy + 12, sw - 28, 12), new Color(0.13f, 0.10f, 0.08f));
            DrawCircle(new Vector2(sx + sw / 2, sy + 64), 24, new Color(0.95f, 0.55f, 0.22f, 0.5f));
            DrawCircle(new Vector2(sx + sw / 2, sy + 64), 12, new Color(1.0f, 0.75f, 0.35f, 0.6f));

            // 桌子（中下）
            float tx = W * 0.40f, ty = H - 120f, tw = 236f, th = 16f;
            DrawRect(new Rect2(tx, ty, tw, th), new Color(0.21f, 0.15f, 0.10f));
            DrawLine(new Vector2(tx + 20, ty + th), new Vector2(tx + 20, H - 28), new Color(0.15f, 0.10f, 0.07f), 6);
            DrawLine(new Vector2(tx + tw - 20, ty + th), new Vector2(tx + tw - 20, H - 28), new Color(0.15f, 0.10f, 0.07f), 6);

            // 柜子（一侧墙角）
            float cx = flip ? 300f : W - 150f, cy = H - 150f, cw = 120f, ch = 128f;
            DrawRect(new Rect2(cx, cy, cw, ch), new Color(0.13f, 0.10f, 0.07f));
            DrawLine(new Vector2(cx + cw / 2, cy), new Vector2(cx + cw / 2, cy + ch), new Color(0.09f, 0.07f, 0.05f), 3);
            DrawCircle(new Vector2(cx + cw / 2 - 12, cy + ch / 2), 3, new Color(0.4f, 0.34f, 0.26f));
            DrawCircle(new Vector2(cx + cw / 2 + 12, cy + ch / 2), 3, new Color(0.4f, 0.34f, 0.26f));

            // 水缸（点缀）
            float vx = flip ? W - 210f : 210f;
            DrawCircle(new Vector2(vx, H - 36f), 22, new Color(0.10f, 0.12f, 0.13f));
            DrawCircle(new Vector2(vx, H - 36f), 16, new Color(0.16f, 0.20f, 0.22f, 0.7f));
        }

        // ---------- 通信据点机房：发电机 / 电闸 / 发信机 / 地图桌 ----------
        private void DrawCommOutpost(float W, float H)
        {
            float fy = H * 0.66f;

            // 后墙（机房混凝土灰）
            DrawRect(new Rect2(0, 0, W, fy), new Color(0.15f, 0.15f, 0.16f));
            for (float y = 28; y < fy; y += 40)
                DrawLine(new Vector2(0, y), new Vector2(W, y), new Color(0.11f, 0.11f, 0.12f), 1);

            // 地板（木箱拼地）
            DrawRect(new Rect2(0, fy, W, H - fy), new Color(0.09f, 0.075f, 0.06f));
            for (float x = 12; x < W; x += 54)
                DrawLine(new Vector2(x, fy), new Vector2(x - 66, H), new Color(0.06f, 0.05f, 0.04f), 1);

            // 作战地图（墙上，居中靠上）
            float mapX = W * 0.30f;
            DrawRect(new Rect2(mapX, 30, 220, 130), new Color(0.22f, 0.19f, 0.14f));
            DrawLine(new Vector2(mapX + 14, 60), new Vector2(mapX + 206, 60), new Color(0.55f, 0.35f, 0.18f), 2);
            DrawLine(new Vector2(mapX + 14, 90), new Vector2(mapX + 160, 130), new Color(0.55f, 0.35f, 0.18f), 2);
            DrawCircle(new Vector2(mapX + 120, 95), 5, new Color(0.7f, 0.2f, 0.18f));

            // 电闸箱（左墙）
            DrawRect(new Rect2(20, 40, 150, 96), new Color(0.10f, 0.10f, 0.11f));
            DrawLine(new Vector2(40, 70), new Vector2(150, 70), new Color(0.5f, 0.42f, 0.18f), 3); // 闸刀
            DrawLine(new Vector2(95, 58), new Vector2(95, 120), new Color(0.30f, 0.30f, 0.32f), 2);

            // 发电机（左，电闸旁）
            DrawRect(new Rect2(34, 150, 130, 104), new Color(0.14f, 0.12f, 0.10f));
            DrawCircle(new Vector2(99, 202), 26, new Color(0.30f, 0.27f, 0.22f));
            DrawCircle(new Vector2(99, 202), 9, new Color(0.10f, 0.09f, 0.07f));
            DrawLine(new Vector2(99, 150), new Vector2(99, 110), new Color(0.25f, 0.22f, 0.18f), 3);

            // 地图桌（中下，桌上铺地图）
            float tx = W * 0.34f, ty = H - 134f, tw = 280f, th = 16f;
            DrawRect(new Rect2(tx, ty, tw, th), new Color(0.20f, 0.16f, 0.11f));
            DrawLine(new Vector2(tx + 24, ty + th), new Vector2(tx + 24, H - 26), new Color(0.14f, 0.11f, 0.08f), 6);
            DrawLine(new Vector2(tx + tw - 24, ty + th), new Vector2(tx + tw - 24, H - 26), new Color(0.14f, 0.11f, 0.08f), 6);
            DrawRect(new Rect2(tx + 30, ty - 30, tw - 60, 28), new Color(0.23f, 0.20f, 0.15f));

            // 发信机（右墙，天线伸出画外）
            DrawRect(new Rect2(W - 236, 36, 200, 96), new Color(0.14f, 0.12f, 0.10f));
            DrawLine(new Vector2(W - 136, 36), new Vector2(W - 136, -30), new Color(0.20f, 0.17f, 0.13f), 4);
            DrawCircle(new Vector2(W - 136, -30), 8, new Color(0.20f, 0.17f, 0.13f));
            DrawRect(new Rect2(W - 210, 70, 70, 40), new Color(0.10f, 0.09f, 0.08f)); // 电键

            // 木箱（左中）
            DrawRect(new Rect2(250, H - 120, 96, 96), new Color(0.18f, 0.13f, 0.09f));
            DrawLine(new Vector2(250, H - 72), new Vector2(346, H - 72), new Color(0.12f, 0.09f, 0.06f), 2);
            DrawRect(new Rect2(360, H - 104, 80, 80), new Color(0.16f, 0.12f, 0.08f));
        }

        // ---------- 仓库 / 焚毁的院落：货架 / 木箱 / 灰烬 ----------
        private void DrawBurnt(float W, float H)
        {
            float fy = H * 0.62f;

            // 破墙（烧黑的砖，墙洞透光）
            DrawRect(new Rect2(0, 0, W, fy), new Color(0.12f, 0.10f, 0.09f));
            for (float y = 26; y < fy; y += 36)
                DrawLine(new Vector2(0, y), new Vector2(W, y), new Color(0.08f, 0.07f, 0.06f), 1);
            // 透光洞（右上方，外头天光）
            DrawRect(new Rect2(W - 220, 30, 150, 120), new Color(0.20f, 0.22f, 0.24f, 0.55f));
            DrawLine(new Vector2(W - 220, 30), new Vector2(W - 70, 30), new Color(0.05f, 0.05f, 0.05f), 4);
            DrawLine(new Vector2(W - 220, 150), new Vector2(W - 70, 150), new Color(0.05f, 0.05f, 0.05f), 4);
            DrawLine(new Vector2(W - 220, 30), new Vector2(W - 220, 150), new Color(0.05f, 0.05f, 0.05f), 4);
            DrawLine(new Vector2(W - 70, 30), new Vector2(W - 70, 150), new Color(0.05f, 0.05f, 0.05f), 4);

            // 地面（焦土 + 斜梁）
            DrawRect(new Rect2(0, fy, W, H - fy), new Color(0.08f, 0.07f, 0.06f));
            DrawLine(new Vector2(120, fy), new Vector2(W - 40, H), new Color(0.06f, 0.05f, 0.04f), 1);
            // 斜塌的房梁
            DrawLine(new Vector2(40, 20), new Vector2(W * 0.5f, fy + 30), new Color(0.14f, 0.10f, 0.07f), 8);
            DrawLine(new Vector2(W - 60, 10), new Vector2(W * 0.42f, fy + 50), new Color(0.14f, 0.10f, 0.07f), 8);

            // 残货架（右中，断裂）
            DrawRect(new Rect2(W - 250, 200, 150, 12), new Color(0.16f, 0.12f, 0.09f));
            DrawRect(new Rect2(W - 235, 150, 40, 44), new Color(0.18f, 0.13f, 0.10f));
            DrawRect(new Rect2(W - 175, 162, 46, 38), new Color(0.15f, 0.11f, 0.08f));

            // 木箱（中）
            DrawRect(new Rect2(600, H - 128, 110, 104), new Color(0.17f, 0.12f, 0.08f));
            DrawLine(new Vector2(600, H - 76), new Vector2(710, H - 76), new Color(0.11f, 0.08f, 0.05f), 2);
            DrawRect(new Rect2(470, H - 110, 90, 90), new Color(0.15f, 0.11f, 0.07f));

            // 灰烬堆（中下，余烬）
            DrawCircle(new Vector2(470, H - 34), 40, new Color(0.06f, 0.05f, 0.04f));
            DrawCircle(new Vector2(452, H - 40), 6, new Color(0.9f, 0.4f, 0.12f, 0.7f));
            DrawCircle(new Vector2(486, H - 30), 5, new Color(0.9f, 0.45f, 0.15f, 0.6f));
            DrawCircle(new Vector2(470, H - 48), 4, new Color(0.95f, 0.5f, 0.18f, 0.5f));

            // 残桌（左）
            DrawRect(new Rect2(230, H - 118, 150, 14), new Color(0.13f, 0.10f, 0.07f));
            DrawLine(new Vector2(250, H - 104), new Vector2(250, H - 30), new Color(0.10f, 0.08f, 0.05f), 5);
            DrawLine(new Vector2(360, H - 104), new Vector2(360, H - 30), new Color(0.10f, 0.08f, 0.05f), 5);
        }
    }
}
