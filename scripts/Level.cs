using System.Collections.Generic;
using Godot;

namespace Witness;

/// <summary>
/// 关卡01：南京 1937 —— v0.4 方案 B（无跳跃 · E 互动翻越 + VH 式环境解谜）。
/// 美术同步重做：手绘上色卡通（参考《Valiant Hearts》着色方式，题材为亚洲/南京）——
/// 灰砖、瓦顶、木门、彩色卡通人物；暴力仍以留白与档案呈现。
/// 解谜节点：推木箱垫脚上委员会高台 / 扔石块引开巡逻 / 拉门闩开侧巷补给 / 拧阀门蒸汽遮挡视野。
/// 五幕节拍保留：0 沦陷前夜 → 1 城破 → 2 沦陷之夜 → 3 记忆的重量 → 4 结算。
/// </summary>
public partial class Level : Node2D
{
    private readonly Main _main;

    // ---- 世界常量（横版长条：宽 7600，地面顶面 y=800）----
    private const float WorldW = 7600f;
    private const float WorldH = 900f;
    private const float Ground = 800f;

    // ---- 世界关键坐标 ----
    private static readonly Vector2 GateTalk = new(2100, Ground);   // 金女大门内（Beat1 士兵事件）
    private static readonly Vector2 JinlingDoor = new(2250, Ground);// 金女主楼大门
    private static readonly Vector2 AssemblyPoint = new(5450, Ground); // 集合点（被抓押送至此）
    private static readonly Vector2 PlayerSpawn = new(140, Ground);
    private static readonly Vector2 CityGate = new(7410, Ground);   // 出城门洞

    // ---- 解谜物件坐标 ----
    private static readonly Vector2 CrateHome = new(4650, Ground);       // 可推木箱（垫脚上委员会高台）
    private static readonly Vector2 StonePile1 = new(3300, Ground);      // 瓦砾旁的石块堆
    private static readonly Vector2 StonePile2 = new(5300, Ground);
    private static readonly Vector2 BoltPos = new(3924, Ground);         // 侧巷铁门门闩
    private static readonly Vector2 SupplyPos = new(4050, Ground);       // 巷内补给（食物 +1）
    private static readonly Vector2 ValvePos = new(5680, Ground);        // 蒸汽阀门
    private static readonly Rect2 SteamRect = new(5740, 600, PuzzleLogic.SteamW, PuzzleLogic.SteamH);
    private static readonly Rect2 DoorRect = new(3950, 700, 26, 100);    // 门闩控制的木门（拉开后移除）
    private static readonly Rect2 ReturnCrate = new(5332, Ground - PuzzleLogic.CrateH, PuzzleLogic.CrateW, PuzzleLogic.CrateH); // 高台东侧的现成木箱（回程垫脚）

    /// <summary>
    /// 横版地形表（碰撞与绘制共用）。Walkable=true 的平台是家人 NPC 可循行的地面线
    /// （GroundY 取其中最高台面）；其余为障碍/高台（需要 E 翻越或解谜垫脚）。
    /// </summary>
    private static readonly (Rect2 Rect, bool Walkable)[] Terrain =
    {
        // 主地面（全图）
        (new Rect2(0, Ground, WorldW, WorldH - Ground), true),
        // 左右边界
        (new Rect2(0, 0, 16, WorldH), false),
        (new Rect2(WorldW - 16, 0, 16, WorldH), false),
        // 起点后的台阶段（家人可循行，教学：台阶也可逐级翻越）
        (new Rect2(600, 755, 170, 45), true),
        (new Rect2(770, 710, 170, 90), true),
        (new Rect2(940, 665, 240, 135), true),
        (new Rect2(1180, 710, 170, 90), true),
        (new Rect2(1350, 755, 170, 45), true),
        // 绕行屋（逐级 42px 台阶翻越上二层）：二层地板 + 一层内堵墙 + 楼梯
        (new Rect2(2660, 560, 600, 30), false),
        (new Rect2(2920, 590, 60, Ground - 590), false),
        (new Rect2(2660, 758, 42, 42), false),
        (new Rect2(2702, 716, 42, 84), false),
        (new Rect2(2744, 674, 42, 126), false),
        (new Rect2(2786, 632, 42, 168), false),
        (new Rect2(2828, 590, 92, 210), false),
        // 城市瓦砾（翻越段兼掩体）
        (new Rect2(3420, 730, 120, 70), false),
        (new Rect2(3600, 700, 150, 100), false),
        // 侧巷高墙（200 高翻不过，门闩解谜进巷）
        (new Rect2(3950, 600, 26, 100), false),
        (new Rect2(4140, 600, 26, 200), false),
        // 委员会高台（180 高：需推木箱垫脚，先翻上木箱再翻上高台）
        (new Rect2(4830, 620, 470, Ground - 620), false),
        // 鼓楼医院门前台阶
        (new Rect2(5760, 760, 130, 40), false),
        (new Rect2(5890, 720, 170, Ground - 720), false),
        // 教堂高台
        (new Rect2(6450, 750, 100, 50), false),
        (new Rect2(6550, 700, 220, Ground - 700), false),
        // 高台东侧现成木箱（回程垫脚，固定）
        (ReturnCrate, false),
    };

    /// <summary>家人/环境难民的循行地面线：取该 x 处最高的 Walkable 台面。</summary>
    public static float GroundY(float x)
    {
        float best = float.MaxValue;
        foreach (var (r, walk) in Terrain)
            if (walk && x >= r.Position.X && x <= r.End.X && r.Position.Y < best)
                best = r.Position.Y;
        return best;
    }

    private Player _player;
    private readonly List<EvidencePoint> _evidence = new();
    private readonly List<Refugee> _deepRefugees = new();
    private readonly List<Patrol> _patrols = new();

    private Node2D _family;
    private bool _familyFollowing;
    private Node2D _soldiers;
    private Refugee _fitch;
    private Node2D _c2Group;
    private readonly HashSet<string> _talked = new();

    private Hud _hud;
    private DialogueUi _dialogue;
    private IntroOverlay _intro;
    private PackUi _pack;

    private int _beat;
    private enum Phase { Intro, Play, Talk, Pack, Walk, Montage }
    private Phase _phase = Phase.Intro;
    private bool _c1Done, _c2Done, _fitchTalked;

    // 记录系统（GDD §6.1：按住 E 1.2s；被看见 → 残缺；被发现 → 没收）
    private string _recordingId;
    private float _progress;
    private bool _tainted;

    // ---- 解谜状态（关卡内存，重进重置）----
    private StaticBody2D _crateBody;       // 可推木箱
    private bool _crateGrabbed;
    private bool _crateTutShown;
    private bool _hasStone;                // 手里握着石块
    private bool _stoneFlying;
    private Vector2 _stoneFrom, _stoneTo;
    private float _stoneT;
    private StaticBody2D _doorBody;        // 侧巷木门（门闩控制）
    private bool _boltOpen;
    private bool _supplyTaken;
    private float _steamT;                 // 蒸汽剩余时长（>0 时遮挡视野）
    private Vector2? _eMark;               // 世界空间 E 提示气泡位置

    // ---- v0.5 手绘素材（平铺纹理 + 抠图物件）----
    private Texture2D _texBrick, _texWood, _texRubble;
    private Texture2D _texCrate, _texStone, _texDoor, _texValve;

    public Level(Main main) { _main = main; }

    /// <summary>巡逻兵据此判断是否行动（对话/结算期间全场静止）。</summary>
    public bool PlayPhase => _phase == Phase.Play;

    /// <summary>蒸汽遮挡：阀门拧开后，穿过蒸汽带的视线被阻断（Patrol 侦测调用）。</summary>
    public bool VisionBlocked(Vector2 a, Vector2 b)
        => _steamT > 0f && PuzzleLogic.SteamBlocks(
            new System.Numerics.Vector2(a.X, a.Y), new System.Numerics.Vector2(b.X, b.Y),
            new System.Numerics.Vector4(SteamRect.Position.X, SteamRect.Position.Y, SteamRect.Size.X, SteamRect.Size.Y));

    /// <summary>世界坐标处冒出漫画对白气泡（关键剧情点，克制使用）。</summary>
    public void BubbleAt(Vector2 feetPos, string locKey)
        => SpeechBubble.Show(this, feetPos + new Vector2(0, -112), Loc.I.T(locKey));

    public override void _Ready()
    {
        TextureRepeat = TextureRepeatEnum.Enabled; // 平铺纹理绘制需要
        _texBrick = ChromaKey.Tile("res://assets/art/sprites/tile_brick.jpg", 0.08f, 0.15f);
        _texWood = ChromaKey.Tile("res://assets/art/sprites/tile_wood.jpg", 0.08f, 0.15f);
        _texRubble = ChromaKey.Tile("res://assets/art/sprites/tile_rubble.jpg", 0.08f, 0.15f);
        _texCrate = ChromaKey.Cutout("res://assets/art/sprites/prop_crate.jpg", 0.04f, 0.08f);
        _texStone = ChromaKey.Cutout("res://assets/art/sprites/prop_stone_pile.jpg", 0.04f, 0.08f);
        _texDoor = ChromaKey.Cutout("res://assets/art/sprites/prop_door.jpg", 0.04f, 0.08f);
        _texValve = ChromaKey.Cutout("res://assets/art/sprites/prop_valve.jpg", 0.04f, 0.08f);
        BuildWorld();
        _hud = new Hud();
        AddChild(_hud);
        AddChild(new VignetteLayer());       // VH 式画框暗角
        _dialogue = new DialogueUi();
        AddChild(_dialogue);
        _intro = new IntroOverlay();
        AddChild(_intro);
        _pack = new PackUi();
        AddChild(_pack);

        int resume = GameState.I.S.Beat;
        StartBeat(resume is < 0 or > 4 ? 0 : resume);
    }

    // ================= 世界搭建 =================

    private void BuildWorld()
    {
        BuildParallax();
        BuildGroundStrip();
        BuildBackdrop();

        foreach (var (r, _) in Terrain)
            AddWall(r);

        // 解谜物件：可推木箱（带碰撞，可站上）+ 门闩控制的木门
        _crateBody = AddWall(new Rect2(CrateHome.X - PuzzleLogic.CrateW / 2, Ground - PuzzleLogic.CrateH,
            PuzzleLogic.CrateW, PuzzleLogic.CrateH));
        _doorBody = AddWall(DoorRect);

        // 玩家：明妮·魏特琳（镜头横向跟随）
        _player = new Player { Position = PlayerSpawn };
        AddChild(_player);
        var camera = new Camera2D
        {
            PositionSmoothingEnabled = true,
            PositionSmoothingSpeed = 6f,
            LimitLeft = 0, LimitTop = 0, LimitRight = (int)WorldW, LimitBottom = (int)WorldH,
        };
        _player.AddChild(camera);
        camera.MakeCurrent();

        // 证据点（6 件，对应史料卡 1-6）：roster/vautrin 在金女门口，其余在城市段各处
        AddEvidence("roster", new Vector2(2350, 782));
        AddEvidence("vautrin", new Vector2(2180, 782));
        AddEvidence("rabe", new Vector2(5050, 602));    // 委员会高台（需垫脚）
        AddEvidence("letter", new Vector2(5210, 602));  // 委员会高台（需垫脚）
        AddEvidence("medical", new Vector2(5960, 702)); // 医院门前台阶
        AddEvidence("film", new Vector2(6650, 682));    // 教堂高台

        // 难民：6 位深度交互 + 3 位环境难民（GDD §7）
        AddDeep("r1", "food", new Vector2(3350, Ground));
        AddDeep("r2", "medicine", new Vector2(3760, Ground));
        AddDeep("r3", "medicine", new Vector2(4400, Ground));
        AddDeep("r4", "blanket", new Vector2(6380, Ground));
        AddDeep("r5", "blanket", new Vector2(6220, Ground));
        AddDeep("r6", "food", new Vector2(7150, Ground));
        foreach (var x in new[] { 2750f, 4100f, 7250f })
            AddChild(new Refugee("", "", Silhouette.Kind.Refugee, wander: true)
                { Position = new Vector2(x, Ground) });

        // 逃难的一家（Beat0 护送目标）
        _family = new Node2D { Position = new Vector2(330, Ground) };
        _family.AddChild(new Refugee("", "", Silhouette.Kind.FamilyAdult));
        _family.AddChild(new Refugee("", "", Silhouette.Kind.FamilyChild) { Position = new Vector2(26, 0) });
        AddChild(_family);

        // 缴械的中国士兵（Beat1，初始隐藏）
        _soldiers = new Node2D { Position = new Vector2(2060, Ground), Visible = false };
        for (int i = 0; i < 3; i++)
            _soldiers.AddChild(new Refugee("", "", Silhouette.Kind.Refugee) { Position = new Vector2(i * 30, 0) });
        AddChild(_soldiers);

        // 乔治·费奇（Beat2 末出现的带片人，真实历史人物）
        _fitch = new Refugee("fitch", "", Silhouette.Kind.Fitch) { Position = new Vector2(6800, Ground), Visible = false };
        AddChild(_fitch);
    }

    /// <summary>背景 2 层视差：远层城墙天际线 + 中层残破街景（VH 彩色手绘）。</summary>
    private void BuildParallax()
    {
        AddParallaxLayer("res://assets/art/skyline_far.jpg", 0.15f, new Vector2(800, 330));
        AddParallaxLayer("res://assets/art/ruins_mid.jpg", 0.4f, new Vector2(800, 520));
    }

    private void AddParallaxLayer(string path, float scale, Vector2 center)
    {
        if (!ResourceLoader.Exists(path)) return;
        var tex = GD.Load<Texture2D>(path);
        var ts = tex.GetSize();
        float w = 1600f;
        var layer = new Parallax2D
        {
            ScrollScale = new Vector2(scale, 1f),
            RepeatSize = new Vector2(w, 0),
        };
        var sprite = new Sprite2D
        {
            Texture = tex,
            Position = center,
            Scale = new Vector2(w / ts.X, w / ts.X),
            ZIndex = -1,
        };
        layer.AddChild(sprite);
        AddChild(layer);
    }

    /// <summary>主地面街道条（可平铺纹理）。</summary>
    private void BuildGroundStrip()
    {
        const string path = "res://assets/art/street_ground.jpg";
        if (!ResourceLoader.Exists(path)) return;
        var tex = GD.Load<Texture2D>(path);
        var strip = new Sprite2D
        {
            Texture = tex,
            TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled,
            RegionEnabled = true,
            RegionRect = new Rect2(0, 0, WorldW, 220),
            Centered = false,
            Position = new Vector2(0, Ground - 10),
            Scale = new Vector2(1f, 0.5f), // 显示高度 110
            ZIndex = -1,
        };
        AddChild(strip);
    }

    /// <summary>侧视立面与建筑：金女立面图、绕行屋室内、医院/教堂/城门手绘立面（v0.5）。</summary>
    private void BuildBackdrop()
    {
        AddSprite("res://assets/art/ginling_facade.jpg", new Vector2(2250, Ground - 240), 640f);
        AddSprite("res://assets/art/house_interior.jpg", new Vector2(2930, Ground - 120), 660f, top: Ground - 240);
        AddCutoutSprite("building_hospital", new Vector2(5930, Ground), 560f);
        AddCutoutSprite("building_church", new Vector2(6660, Ground), 470f);
        AddCutoutSprite("building_citygate", new Vector2(7400, Ground), 620f);
    }

    /// <summary>白底建筑立面图：抠图后按「底部对齐地面」放置（替代程序矩形立面）。</summary>
    private void AddCutoutSprite(string name, Vector2 bottomCenter, float width)
    {
        var path = $"res://assets/art/sprites/{name}.jpg";
        if (!ResourceLoader.Exists(path)) return;
        var tex = ChromaKey.Cutout(path, 0.04f, 0.14f);
        if (tex == null) return;
        var ts = tex.GetSize();
        float s = width / ts.X;
        var sp = new Sprite2D
        {
            Texture = tex,
            ZIndex = -1,
            Scale = new Vector2(s, s),
            Position = new Vector2(bottomCenter.X, bottomCenter.Y - ts.Y * s / 2f),
        };
        AddChild(sp);
    }

    private void AddSprite(string path, Vector2 center, float width, float? top = null)
    {
        if (!ResourceLoader.Exists(path)) return;
        var tex = GD.Load<Texture2D>(path);
        var ts = tex.GetSize();
        var sprite = new Sprite2D
        {
            Texture = tex,
            ZIndex = -1,
            Scale = new Vector2(width / ts.X, width / ts.X),
        };
        if (top.HasValue)
            sprite.Position = new Vector2(center.X, top.Value + width * ts.Y / ts.X / 2f);
        else
            sprite.Position = center;
        AddChild(sprite);
    }

    private StaticBody2D AddWall(Rect2 r)
    {
        var body = new StaticBody2D { Position = r.GetCenter(), CollisionLayer = 1 };
        var shape = new CollisionShape2D();
        var rect = new RectangleShape2D { Size = r.Size };
        shape.Shape = rect;
        body.AddChild(shape);
        AddChild(body);
        return body;
    }

    private void AddEvidence(string id, Vector2 pos)
    {
        var ev = new EvidencePoint(id) { Position = pos };
        AddChild(ev);
        _evidence.Add(ev);
    }

    private void AddDeep(string id, string need, Vector2 pos)
    {
        var r = new Refugee(id, need, Silhouette.Kind.Refugee) { Position = pos };
        AddChild(r);
        _deepRefugees.Add(r);
    }

    // ================= 节拍推进 =================

    private void StartBeat(int n)
    {
        _beat = n;
        var s = GameState.I.S;
        s.Beat = n;
        GameState.I.Save();

        _hud.SetDate(Loc.I.T($"hud_date_beat{n}"));
        _hud.ShowSupplies(false);

        if (n >= 1) FindEvidence("roster").Active = true;
        if (n >= 2)
        {
            foreach (var id in new[] { "rabe", "vautrin", "letter", "medical", "film" })
                FindEvidence(id).Active = true;
            SpawnPatrols();
            _hud.ShowSupplies(true);
            _hud.RefreshSupplies(s.Food, s.Medicine, s.Blanket);
        }

        _c1Done = s.Choice1 != 0;
        _c2Done = s.Choice2 != 0;
        if (_c2Done) _fitch.Visible = true;

        // 教学浮字（自动淡出，靠近互动物才出现提示图标）
        switch (n)
        {
            case 0: _hud.ShowTutorial(Loc.I.T("tut_move")); break;
            case 1: _hud.ShowTutorial(Loc.I.T("tut_record")); break;
            case 2: _hud.ShowTutorial(Loc.I.T("tut_sneak")); break;
            default: _hud.HideTutorial(); break;
        }

        if (n >= 4)
        {
            SetPhase(Phase.Montage);
            _hud.Visible = false;
            StartMontage();
            return;
        }

        SetPhase(Phase.Intro);
        string note = n is 0 or 1 or 2 ? "person_note_vautrin" : "person_note_fitch";
        _intro.Show(Loc.I.T($"hud_date_beat{n}"), Loc.I.T($"beat{n}_intro"), Loc.I.T(note), () =>
        {
            SetPhase(Phase.Play);
            if (n == 3) OpenPack();
        });
    }

    private EvidencePoint FindEvidence(string id)
    {
        foreach (var e in _evidence)
            if (e.Id == id) return e;
        return null;
    }

    private void SpawnPatrols()
    {
        if (_patrols.Count > 0) return; // 读档重入时避免重复
        AddPatrol(new Vector2[] { new(3260, Ground), new(4600, Ground) });   // 城市瓦砾段
        AddPatrol(new Vector2[] { new(5350, Ground), new(6400, Ground) });   // 医院段
        AddPatrol(new Vector2[] { new(2680, Ground), new(2890, Ground) });   // 绕行屋一层
    }

    private void AddPatrol(Vector2[] pts)
    {
        var p = new Patrol();
        p.Setup(this, _player, pts);
        p.SpottedPlayer += OnSpotted;
        AddChild(p);
        _patrols.Add(p);
    }

    private void SetPhase(Phase p)
    {
        _phase = p;
        if (_player != null)
            _player.SetPhysicsProcess(p == Phase.Play); // 对话/结算期间全场静止
        if (p != Phase.Play) _hud.SetArrow(false, Vector2.Zero, 0f);
    }

    // ================= 每帧逻辑 =================

    public override void _Process(double delta)
    {
        float f = (float)delta;
        if (_phase != Phase.Play)
        {
            _hud.SetRecord(0f, false);
            return;
        }
        if (_steamT > 0f) { _steamT -= f; QueueRedraw(); }
        if (_stoneFlying) { _stoneT += f / 0.42f; if (_stoneT >= 1f) { _stoneFlying = false; StoneLand(); } QueueRedraw(); }

        _eMark = null;
        UpdateFamily(f);
        UpdateTriggers();
        UpdateRecording(f);
        UpdateInteract();
        UpdatePuzzles(f);
        UpdateArrow();
        _hud.SetSneak(_player.Sneaking);
        RefreshObjectives();
        if (_eMark != null || _crateGrabbed || _steamT > 0f) QueueRedraw();
    }

    private void UpdateFamily(float f)
    {
        if (_beat != 0 || !_familyFollowing) return;
        if (Mathf.Abs(_family.Position.X - JinlingDoor.X) < 80)
        {
            _familyFollowing = false;
            SetPhase(Phase.Talk);
            BubbleAt(_family.Position, "bub_family_done");
            _dialogue.ShowLine(Loc.I.T("npc_family"), Loc.I.T("npc_family_done"), () => StartBeat(1));
            return;
        }
        // 横版跟随：水平追赶 + 贴合可循行地面线
        float dx = _player.Position.X - _family.Position.X;
        if (Mathf.Abs(dx) > 90)
            _family.Position += new Vector2(Mathf.Sign(dx) * 200f * f, 0);
        float gy = GroundY(_family.Position.X);
        _family.Position = new Vector2(_family.Position.X,
            Mathf.Lerp(_family.Position.Y, gy, Mathf.Min(1f, f * 8f)));
    }

    private void UpdateTriggers()
    {
        if (_beat == 1 && !_c1Done && _player.Position.DistanceTo(GateTalk) < 150) TriggerC1();
        if (_beat == 2 && !_c2Done && _player.Position.DistanceTo(JinlingDoor) < 150) TriggerC2();
    }

    // ---- 屏幕边缘方向箭头（目标指引）----

    private void UpdateArrow()
    {
        Vector2? target = CurrentTarget();
        if (target == null) { _hud.SetArrow(false, Vector2.Zero, 0f); return; }

        var sp = GetViewport().GetCanvasTransform() * target.Value;
        var margin = 64f;
        var inside = new Rect2(margin, margin,
            GetViewport().GetVisibleRect().Size.X - margin * 2,
            GetViewport().GetVisibleRect().Size.Y - margin * 2);
        if (inside.HasPoint(sp)) { _hud.SetArrow(false, Vector2.Zero, 0f); return; }

        var clamped = new Vector2(
            Mathf.Clamp(sp.X, margin, GetViewport().GetVisibleRect().Size.X - margin),
            Mathf.Clamp(sp.Y, margin, GetViewport().GetVisibleRect().Size.Y - margin));
        float angle = (clamped - GetViewport().GetVisibleRect().Size / 2f).Angle();
        _hud.SetArrow(true, clamped, angle);
    }

    private Vector2? CurrentTarget()
    {
        switch (_beat)
        {
            case 0: return _familyFollowing ? JinlingDoor : _family.Position;
            case 1:
                if ((EvidenceStatus)GameState.I.S.Evidence["roster"] == EvidenceStatus.Uncollected)
                    return FindEvidence("roster").Position;
                if (!_c1Done) return GateTalk;
                return null;
            case 2:
                Vector2? best = null;
                float bestD = float.MaxValue;
                foreach (var ev in _evidence)
                {
                    if (!ev.Active) continue;
                    if ((EvidenceStatus)GameState.I.S.Evidence[ev.Id] != EvidenceStatus.Uncollected) continue;
                    float d = ev.Position.DistanceTo(_player.Position);
                    if (d < bestD) { bestD = d; best = ev.Position; }
                }
                if (best != null) return best;
                if (!_c2Done) return JinlingDoor;
                if (_fitch.Visible && !_fitchTalked) return _fitch.Position;
                return null;
            default: return null;
        }
    }

    // ---- 记录系统（核心动词）----

    private void UpdateRecording(float f)
    {
        EvidencePoint near = null;
        float best = 58f;
        foreach (var ev in _evidence)
        {
            if (!ev.Active) continue;
            if ((EvidenceStatus)GameState.I.S.Evidence[ev.Id] != EvidenceStatus.Uncollected) continue;
            float d = ev.Position.DistanceTo(_player.Position);
            if (d < best) { best = d; near = ev; }
        }
        bool danger = AnyPatrolSeeing();

        if (_recordingId == null)
        {
            if (near != null)
            {
                _hud.ShowPrompt(Loc.I.T("hud_prompt_record"));
                _eMark = near.Position + new Vector2(0, -34);
                if (Input.IsActionJustPressed("interact"))
                {
                    _recordingId = near.Id;
                    _progress = 0f;
                    _tainted = danger; // 起笔时巡逻已在场 → 注定残缺
                }
            }
            else if (NearRecordedEvidence() && Input.IsActionJustPressed("interact"))
            {
                _hud.Toast(Loc.I.T("hud_already"));
            }
        }
        else
        {
            if (danger) _tainted = true; // 记录中被看见 → 残缺（GDD §6.1）
            if (Input.IsActionPressed("interact"))
            {
                _progress += f / 1.2f;
                if (_progress >= 1f)
                {
                    GameState.I.S.Record(_recordingId, _tainted);
                    _hud.Toast(Loc.I.T(_tainted ? "hud_torn" : "hud_recorded"));
                    _recordingId = null;
                    _progress = 0f;
                }
            }
            else
            {
                _recordingId = null; // 松手中断
                _progress = 0f;
            }
        }
        _hud.SetRecord(_recordingId != null ? _progress : 0f, danger);
    }

    private bool NearRecordedEvidence()
    {
        foreach (var ev in _evidence)
        {
            if (!ev.Active) continue;
            var st = (EvidenceStatus)GameState.I.S.Evidence[ev.Id];
            if ((st == EvidenceStatus.Complete || st == EvidenceStatus.Torn) &&
                ev.Position.DistanceTo(_player.Position) < 58f) return true;
        }
        return false;
    }

    // ---- 交谈与事件 ----

    private void UpdateInteract()
    {
        if (_recordingId != null) return; // 记录优先，不显示交谈提示
        var (node, kind) = NearestTalk();
        if (kind == TalkKind.None)
        {
            _hud.ShowPrompt("");
            return;
        }
        _hud.ShowPrompt(Loc.I.T("hud_prompt_talk"));
        _eMark = (node != null ? node.Position : (kind == TalkKind.Family ? _family.Position : GateTalk))
                 + new Vector2(0, -62);
        if (Input.IsActionJustPressed("interact"))
        {
            switch (kind)
            {
                case TalkKind.Family: TalkFamily(); break;
                case TalkKind.Soldiers: TriggerC1(); break;
                case TalkKind.Refugee: TalkRefugee(node); break;
                case TalkKind.Fitch: TalkFitch(); break;
            }
        }
    }

    private enum TalkKind { None, Family, Soldiers, Refugee, Fitch }

    private (Refugee node, TalkKind kind) NearestTalk()
    {
        Refugee best = null;
        var kind = TalkKind.None;
        float bestD = 80f;

        if (_beat == 0 && !_familyFollowing)
        {
            float d = _family.Position.DistanceTo(_player.Position);
            if (d < bestD) { bestD = d; kind = TalkKind.Family; }
        }
        if (_beat == 1 && !_c1Done)
        {
            float d = _player.Position.DistanceTo(GateTalk);
            if (d < 150f && d < bestD) { bestD = d; kind = TalkKind.Soldiers; }
        }
        if (_beat >= 2)
        {
            foreach (var r in _deepRefugees)
            {
                if (r.Taken) continue;
                float d = r.Position.DistanceTo(_player.Position);
                if (d < 60f && d < bestD) { bestD = d; best = r; kind = TalkKind.Refugee; }
            }
            if (_fitch.Visible)
            {
                float d = _fitch.Position.DistanceTo(_player.Position);
                if (d < 70f && d < bestD) { bestD = d; best = _fitch; kind = TalkKind.Fitch; }
            }
        }
        return (best, kind);
    }

    private void TalkFamily()
    {
        SetPhase(Phase.Talk);
        BubbleAt(_family.Position, "bub_family");
        string name = Loc.I.T("npc_family");
        _dialogue.ShowTextThen(name, Loc.I.T("npc_family_1"), () =>
            _dialogue.ShowTextThen(name, Loc.I.T("npc_family_2"), () =>
            {
                _familyFollowing = true;
                SetPhase(Phase.Play);
            }));
    }

    private void TriggerC1()
    {
        if (_phase != Phase.Play) return;
        SetPhase(Phase.Talk);
        // 大门内侧跪着缴械的士兵 —— 首个道德选择（GDD Beat1）
        _soldiers.Visible = true;
        _dialogue.ShowChoice("", Loc.I.T("c1_title"),
            new[] { (Loc.I.T("c1_a"), true), (Loc.I.T("c1_b"), true) }, i =>
        {
            var s = GameState.I.S;
            s.Choice1 = i + 1;
            _c1Done = true;
            _dialogue.ShowTextThen("", Loc.I.T(i == 0 ? "c1_ra" : "c1_rb"), () => SetPhase(Phase.Play));
        });
    }

    private void TriggerC2()
    {
        if (_phase != Phase.Play) return;
        SetPhase(Phase.Talk);
        // 门口的史实场面：日军拉扯妇女，魏特琳挡在门前（GDD §4.1）
        if (_c2Group == null)
        {
            _c2Group = new Node2D { Position = JinlingDoor + new Vector2(50, 0) };
            for (int i = 0; i < 3; i++)
                _c2Group.AddChild(new Refugee("w" + (i + 1), "", Silhouette.Kind.Refugee)
                {
                    Position = new Vector2(i * 32, 0),
                });
            _c2Group.AddChild(new Refugee("", "", Silhouette.Kind.Soldier) { Position = new Vector2(108, 0) });
            AddChild(_c2Group);
        }
        _dialogue.ShowChoice("", Loc.I.T("c2_title"),
            new[] { (Loc.I.T("c2_a"), true), (Loc.I.T("c2_b"), true) }, i =>
        {
            var s = GameState.I.S;
            s.Choice2 = i + 1;
            _c2Done = true;
            if (i == 0)
            {
                foreach (var w in new[] { "w1", "w2", "w3" }) s.Refugees[w] = 1; // 三位妇女安全
                foreach (var p in _patrols) p.Aggression = 1.5f;                 // 巡逻记住了你的脸
            }
            else
            {
                foreach (var w in new[] { "w1", "w2", "w3" }) s.Refugees[w] = -1; // 被带走
            }
            _dialogue.ShowTextThen("", Loc.I.T(i == 0 ? "c2_ra" : "c2_rb"), () =>
            {
                _fitch.Visible = true; // 阻挡抉择落定后，费奇出现在教堂前
                SetPhase(Phase.Play);
            });
        });
    }

    private void TalkRefugee(Refugee r)
    {
        SetPhase(Phase.Talk);
        _talked.Add(r.Id);
        var s = GameState.I.S;
        string name = Loc.I.T($"npc_{r.Id}_name");

        if (_beat < 2)
        {
            _dialogue.ShowTextThen(name, Loc.I.T("npc_generic"), () => SetPhase(Phase.Play));
            return;
        }
        if (r.Helped)
        {
            _dialogue.ShowTextThen(name, Loc.I.T($"npc_{r.Id}_thanks"), () => SetPhase(Phase.Play));
            return;
        }
        int have = r.Need switch { "food" => s.Food, "medicine" => s.Medicine, _ => s.Blanket };
        string give = Loc.I.Tf("dlg_give", Loc.I.T("item_" + r.Need), have);
        _dialogue.ShowChoice(name, Loc.I.T($"npc_{r.Id}_greet"),
            new[] { (give, have > 0), (Loc.I.T("dlg_leave"), true) }, i =>
        {
            if (i != 0) { SetPhase(Phase.Play); return; }
            // 一次确认弹窗：分配确认后不可撤销（GDD §6.2）
            _dialogue.ShowChoice("", Loc.I.T("dlg_give_confirm"),
                new[] { (Loc.I.T("ui_confirm"), true), (Loc.I.T("ui_cancel"), true) }, j =>
            {
                if (j == 0)
                {
                    switch (r.Need)
                    {
                        case "food": s.Food--; break;
                        case "medicine": s.Medicine--; break;
                        default: s.Blanket--; break;
                    }
                    s.Refugees[r.Id] = 1;
                    _hud.RefreshSupplies(s.Food, s.Medicine, s.Blanket);
                    BubbleAt(r.Position, "bub_thanks");
                    _dialogue.ShowTextThen(name, Loc.I.T($"npc_{r.Id}_thanks"), () => SetPhase(Phase.Play));
                }
                else SetPhase(Phase.Play);
            });
        });
    }

    private void TalkFitch()
    {
        SetPhase(Phase.Talk);
        int remaining = 0;
        foreach (var r in _deepRefugees)
            if (!_talked.Contains(r.Id) && !r.Helped && !r.Taken) remaining++;

        string name = Loc.I.T("fitch_name");
        if (remaining > 0)
        {
            _dialogue.ShowChoice(name, Loc.I.T("fitch_1"),
                new[] { (Loc.I.T("fitch_choice_go"), true), (Loc.I.T("fitch_choice_stay"), true) }, i =>
            {
                if (i == 0) OpenPack();
                else SetPhase(Phase.Play);
            });
        }
        else
        {
            _dialogue.ShowTextThen(name, Loc.I.T("fitch_1"), OpenPack);
        }
    }

    // ---- 环境解谜（方案 B：木箱 / 石块 / 门闩 / 阀门 / 翻越）----

    private void UpdatePuzzles(float f)
    {
        if (_recordingId != null || _player.Vaulting) return;
        if (NearestTalk().kind != TalkKind.None) return; // 交谈优先

        // 记录提示优先（附近有未记录证据时不抢 E）
        foreach (var ev in _evidence)
        {
            if (!ev.Active) continue;
            if ((EvidenceStatus)GameState.I.S.Evidence[ev.Id] != EvidenceStatus.Uncollected) continue;
            if (ev.Position.DistanceTo(_player.Position) < 58f) return;
        }

        // —— 木箱：抓住后推/拉 ——
        var crateRect = new Rect2(_crateBody.Position.X - PuzzleLogic.CrateW / 2,
            _crateBody.Position.Y - PuzzleLogic.CrateH / 2, PuzzleLogic.CrateW, PuzzleLogic.CrateH);
        if (_crateGrabbed)
        {
            _hud.ShowPrompt(Loc.I.T("hud_prompt_crate_off"));
            _eMark = _crateBody.Position + new Vector2(0, -64);
            float dx = _player.Position.X - _crateBody.Position.X;
            if (Input.IsActionJustPressed("interact") || Mathf.Abs(dx) > PuzzleLogic.CrateKeepDist)
            {
                _crateGrabbed = false;
                _player.SpeedOverride = -1f;
                return;
            }
            var dir = Input.GetAxis("move_left", "move_right");
            if (dir != 0f)
            {
                float newX = Mathf.Clamp(_crateBody.Position.X + dir * PuzzleLogic.CratePushSpeed * f,
                    3200f, 4802f); // 留在瓦砾以东、高台以西
                _crateBody.Position = new Vector2(newX, _crateBody.Position.Y);
                QueueRedraw();
            }
            return;
        }

        // —— 手里有石块：E 投掷 ——
        if (_hasStone)
        {
            _hud.ShowPrompt(Loc.I.T("hud_prompt_stone_throw"));
            _eMark = _player.Position + new Vector2(0, -66);
            if (Input.IsActionJustPressed("interact"))
            {
                _hasStone = false;
                _stoneFlying = true;
                _stoneFrom = _player.Position + new Vector2(0, -46);
                _stoneTo = new Vector2(_player.Position.X + _player.Facing * PuzzleLogic.StoneThrowDist, Ground);
                _stoneT = 0f;
            }
            return;
        }

        var pp = _player.Position;

        // —— 石块堆：E 拾起 ——
        foreach (var pile in new[] { StonePile1, StonePile2 })
        {
            if (Mathf.Abs(pp.X - pile.X) < 52f && Mathf.Abs(pp.Y - pile.Y) < 40f)
            {
                _hud.ShowPrompt(Loc.I.T("hud_prompt_stone_take"));
                _eMark = pile + new Vector2(0, -30);
                if (Input.IsActionJustPressed("interact"))
                {
                    _hasStone = true;
                    _hud.Toast(Loc.I.T("hud_stone_taken"));
                }
                return;
            }
        }

        // —— 门闩：拉开侧巷木门 ——
        if (!_boltOpen && Mathf.Abs(pp.X - BoltPos.X) < PuzzleLogic.BoltDist && Mathf.Abs(pp.Y - BoltPos.Y) < 60f)
        {
            _hud.ShowPrompt(Loc.I.T("hud_prompt_bolt"));
            _eMark = BoltPos + new Vector2(10, -56);
            if (Input.IsActionJustPressed("interact"))
            {
                _boltOpen = true;
                _doorBody.QueueFree();
                _hud.Toast(Loc.I.T("hud_bolt_open"));
                QueueRedraw();
            }
            return;
        }

        // —— 巷内补给：食物 +1 ——
        if (_boltOpen && !_supplyTaken && pp.DistanceTo(SupplyPos) < 56f)
        {
            _hud.ShowPrompt(Loc.I.T("hud_prompt_supply"));
            _eMark = SupplyPos + new Vector2(0, -36);
            if (Input.IsActionJustPressed("interact"))
            {
                _supplyTaken = true;
                GameState.I.S.Food++;
                _hud.RefreshSupplies(GameState.I.S.Food, GameState.I.S.Medicine, GameState.I.S.Blanket);
                _hud.Toast(Loc.I.T("hud_supply_food"));
                QueueRedraw();
            }
            return;
        }

        // —— 蒸汽阀门：拧开遮挡巡逻视野 ——
        if (_steamT <= 0f && pp.DistanceTo(ValvePos) < PuzzleLogic.ValveDist)
        {
            _hud.ShowPrompt(Loc.I.T("hud_prompt_valve"));
            _eMark = ValvePos + new Vector2(0, -66);
            if (Input.IsActionJustPressed("interact"))
            {
                _steamT = PuzzleLogic.SteamSeconds;
                _hud.Toast(Loc.I.T("hud_steam_on"));
                QueueRedraw();
            }
            return;
        }

        // —— 木箱抓取 ——
        if (PuzzleLogic.CanGrabCrate(pp.X, _crateBody.Position.X, pp.Y, _crateBody.Position.Y + PuzzleLogic.CrateH / 2))
        {
            _hud.ShowPrompt(Loc.I.T("hud_prompt_crate"));
            _eMark = _crateBody.Position + new Vector2(0, -64);
            if (Input.IsActionJustPressed("interact"))
            {
                _crateGrabbed = true;
                _player.SpeedOverride = PuzzleLogic.CratePushSpeed;
                if (!_crateTutShown) { _crateTutShown = true; _hud.ShowTutorial(Loc.I.T("tut_crate")); }
            }
            return;
        }

        // —— 翻越（最低优先级）：面前有可攀的矮墙/瓦砾/台阶 ——
        var solids = new List<System.Numerics.Vector4>();
        foreach (var (r, _) in Terrain)
            solids.Add(new System.Numerics.Vector4(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y));
        solids.Add(new System.Numerics.Vector4(crateRect.Position.X, crateRect.Position.Y, crateRect.Size.X, crateRect.Size.Y));
        var vault = PuzzleLogic.FindVault(
            new System.Numerics.Vector2(pp.X, pp.Y), _player.Facing, solids);
        if (vault != null && !_player.Crouched)
        {
            _hud.ShowPrompt(Loc.I.T("hud_prompt_vault"));
            float edgeX = _player.Facing > 0 ? pp.X + 20 : pp.X - 20;
            _eMark = new Vector2(edgeX, vault.Value.topY - 26);
            if (Input.IsActionJustPressed("interact"))
                _player.StartVault(vault.Value.topY, vault.Value.landX);
        }
    }

    /// <summary>石块落地：吸引听觉范围内的巡逻去查看（引开）。</summary>
    private void StoneLand()
    {
        foreach (var p in _patrols)
            if (PuzzleLogic.HeardStone(p.Position.X, _stoneTo.X))
                p.Distract(new Vector2(_stoneTo.X, Ground), PuzzleLogic.DistractSeconds);
    }

    // ---- 被巡逻发现（GDD §6.3：没有 Game Over，只有损失）----

    private void OnSpotted()
    {
        if (_phase != Phase.Play) return;
        var s = GameState.I.S;
        s.SpotCount++;

        if (_recordingId != null)
        {
            s.Destroy(_recordingId); // 记录时被发现 → 证据被没收销毁
            _recordingId = null;
            _progress = 0f;
            _hud.Toast(Loc.I.T("hud_confiscated"));
        }
        else
        {
            _hud.Toast(Loc.I.T("hud_spotted"));
        }

        if (s.SpotCount >= 2 && !s.Caught)
        {
            s.Caught = true;
            foreach (var id in WitnessState.EvidenceIds)  // 剩余证据点全部失去
                if ((EvidenceStatus)s.Evidence[id] == EvidenceStatus.Uncollected)
                    s.Destroy(id);
            _player.Position = AssemblyPoint; // 被押送至集合点
            _player.Velocity = Vector2.Zero;
            _hud.Toast(Loc.I.T("hud_caught"));
        }
    }

    // ---- Beat3 取舍 → 出城 → 蒙太奇 → 结算 ----

    private void OpenPack()
    {
        _fitchTalked = true;
        SetPhase(Phase.Pack);
        _hud.Visible = false;

        var avail = new List<string>();
        foreach (var id in WitnessState.EvidenceIds)
        {
            var st = (EvidenceStatus)GameState.I.S.Evidence[id];
            if (st == EvidenceStatus.Complete || st == EvidenceStatus.Torn)
                avail.Add(id);
        }
        _pack.Open(avail, carried =>
        {
            GameState.I.S.ApplySettlement(carried); // 确认后不可反悔：带出解锁，其余湮没
            GameState.I.Save();
            StartWalk();
        });
    }

    private void StartWalk()
    {
        SetPhase(Phase.Walk);
        var walk = new WalkUi();
        walk.Finished += () =>
        {
            walk.QueueFree();
            StartMontage();
        };
        AddChild(walk);
    }

    private void StartMontage()
    {
        SetPhase(Phase.Montage);
        var m = new MontageUi(new[] { "montage_1", "montage_2", "montage_3", "montage_4" });
        m.Finished += () =>
        {
            m.QueueFree();
            _main.ShowSettlement();
        };
        AddChild(m);
    }

    // ---- 目标与杂项 ----

    private void RefreshObjectives()
    {
        string Mark(string key, bool done) => Loc.I.T(key) + (done ? "  ✔" : "");
        bool allResolved = true;
        foreach (var id in WitnessState.EvidenceIds)
            if ((EvidenceStatus)GameState.I.S.Evidence[id] == EvidenceStatus.Uncollected)
                allResolved = false;

        string text = _beat switch
        {
            0 => Mark("obj0", _familyFollowing),
            1 => Mark("obj1a", (EvidenceStatus)GameState.I.S.Evidence["roster"] != EvidenceStatus.Uncollected)
                 + "\n" + Mark("obj1b", _c1Done),
            2 => Mark("obj2a", allResolved) + "\n" + Mark("obj2b", _c2Done) + "\n"
                 + Mark("obj2c", SuppliesDone()) + (_c2Done ? "\n" + Mark("obj2d", _fitchTalked) : ""),
            3 => Loc.I.T("obj3"),
            _ => "",
        };
        _hud.SetObjective(text);
    }

    private bool SuppliesDone()
    {
        var s = GameState.I.S;
        return s.Food + s.Medicine + s.Blanket == 0 || _talked.Count >= 6;
    }

    private bool AnyPatrolSeeing()
    {
        foreach (var p in _patrols)
            if (p.SeeingPlayer) return true;
        return false;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("pause"))
        {
            GameState.I.Save();
            _main.ShowTitle();
        }
    }

    // ---- 地形绘制：VH 式手绘上色（灰砖 / 木 / 瓦砾色块 + 深色描边）----

    private static readonly Color BrickGrey = new(0.42f, 0.44f, 0.43f, 0.94f);   // 南京青砖
    private static readonly Color BrickEdge = new(0.22f, 0.21f, 0.19f, 0.9f);
    private static readonly Color WoodBrown = new(0.46f, 0.35f, 0.23f, 0.95f);
    private static readonly Color WoodEdge = new(0.24f, 0.18f, 0.12f, 0.9f);

    public override void _Draw()
    {
        foreach (var (r, _) in Terrain)
        {
            if (r.Position.Y >= Ground && r.Size.X > 100) continue; // 主地面交给街道纹理
            FillByTerrain(r);
            DrawTerrainDressing(r);   // 平台旁的环境碎屑：坍塌墙根/弹坑/碎砖（v5b）
        }
        DrawRubbleMarks();
        DrawPuzzleProps();

        // 操场集合点：虚线圆
        var edge = new Color(0.9f, 0.86f, 0.74f, 0.5f);
        for (int i = 0; i < 24; i++)
        {
            float a0 = i / 24f * Mathf.Tau;
            float a1 = (i + 0.5f) / 24f * Mathf.Tau;
            DrawArc(AssemblyPoint + new Vector2(0, -60), 70f, a0, a1, 6, edge, 2f);
        }

        DrawEMark();
    }

    /// <summary>地形块手绘化：青砖/木板/瓦砾平铺纹理 + 顶面亮沿 + 深色描边（v0.5 替代纯色矩形）。</summary>
    private void FillByTerrain(Rect2 r)
    {
        bool woodHouse = r.Position.X >= 2660 && r.Position.X <= 2980 && r.Position.Y < Ground;
        bool rubble = r.Position.X >= 3400 && r.Position.X < 3800 && r.Position.Y < Ground;
        var tex = woodHouse ? _texWood : (rubble ? _texRubble : _texBrick);
        if (tex != null)
            ChromaKey.DrawTiled(this, tex, r, woodHouse ? 0.20f : (rubble ? 0.40f : 0.22f));
        else
            DrawRect(r, woodHouse ? WoodBrown : BrickGrey);
        // 顶面亮沿（可站立面的「台阶沿」读感）+ 外描边
        DrawRect(new Rect2(r.Position, new Vector2(r.Size.X, Mathf.Min(6f, r.Size.Y))),
            new Color(0.88f, 0.85f, 0.76f, 0.5f));
        DrawRect(r, woodHouse ? WoodEdge : BrickEdge, false, 2f);
    }

    /// <summary>平台旁的环境碎屑（v5b）：坍塌墙根碎砖 / 弹坑 / 散落木梁。
    /// 让地形看起来像二战废墟的一部分，而不是凭空出现的方块。</summary>
    private void DrawTerrainDressing(Rect2 r)
    {
        // 太矮的平台（<20px）不需要 dressing
        if (r.Size.Y < 20f) return;

        bool woodHouse = r.Position.X >= 2660 && r.Position.X <= 2980 && r.Position.Y < Ground;
        bool rubble = r.Position.X >= 3400 && r.Position.X < 3800 && r.Position.Y < Ground;
        bool isStep = r.Size.Y <= 60f;   // 台阶 vs 高墙
        float topY = r.Position.Y;
        float bottomY = Mathf.Min(r.Position.Y + r.Size.Y, Ground);
        var edge = woodHouse ? WoodEdge : BrickEdge;

        // --- 平台脚下的坍塌碎砖 / 弹坑 ---
        // 主要发生在砖结构和瓦砾区，木台旁画散落的断梁
        if (!woodHouse)
        {
            var rubColor = new Color(0.50f, 0.48f, 0.44f, 0.95f);
            var brickRubble = new Color(0.72f, 0.56f, 0.48f, 0.9f);   // 红砖碎块
            // 平台脚下沿地面散落几块碎砖（底部 2~3 块小矩形，向左右各延伸 12px）
            for (int side = -1; side <= 1; side += 2)
            {
                float cx = r.Position.X + (side > 0 ? r.Size.X - 4f : 4f);
                // 1~2 块碎砖
                for (int k = 0; k < 2; k++)
                {
                    float bx = cx + side * (10 + k * 10);
                    float by = bottomY - (3 + k * 3);
                    if (bx < 0 || bx > WorldW - 20) continue;
                    DrawRect(new Rect2(bx - 5f, by, 9f, 5f), brickRubble);
                    DrawRect(new Rect2(bx - 5f, by, 9f, 5f), edge, false, 1.2f);
                    DrawRect(new Rect2(bx - 3f, by - 4f, 7f, 4f), rubColor);
                    DrawRect(new Rect2(bx - 3f, by - 4f, 7f, 4f), edge, false, 1.0f);
                }
            }

            // 瓦砾区的弹坑（黑色小椭圆 + 裂纹放射）
            if (rubble)
            {
                float craterX = r.Position.X + r.Size.X * 0.55f;
                float craterY = topY + r.Size.Y * 0.3f;
                DrawSetTransform(new Vector2(craterX, craterY), 0, Vector2.One);
                DrawEllipse(Vector2.Zero, 14f, 5f, new Color(0.18f, 0.15f, 0.12f, 0.7f));
                DrawEllipse(Vector2.Zero, 6f, 2.2f, new Color(0.08f, 0.06f, 0.05f, 0.85f));
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                // 辐射裂纹线
                var crack = new Color(0.18f, 0.15f, 0.12f, 0.75f);
                DrawLine(new Vector2(craterX - 14, craterY), new Vector2(craterX - 28, craterY - 6), crack, 1.6f);
                DrawLine(new Vector2(craterX + 14, craterY), new Vector2(craterX + 30, craterY + 4), crack, 1.6f);
                DrawLine(new Vector2(craterX - 6, craterY - 4), new Vector2(craterX - 12, craterY - 16), crack, 1.4f);
                DrawLine(new Vector2(craterX + 4, craterY - 3), new Vector2(craterX + 10, craterY - 12), crack, 1.2f);
            }
        }

        // --- 木台旁的断梁/散落木板 ---
        if (woodHouse && isStep)
        {
            var beam = new Color(0.40f, 0.30f, 0.18f, 0.9f);
            // 平台脚下侧倒一根断梁
            float bxy = r.Position.X + r.Size.X * 0.55f;
            float by = bottomY - 6f;
            DrawSetTransform(new Vector2(bxy, by), -0.35f, Vector2.One);
            DrawRect(new Rect2(-20f, -4f, 40f, 8f), beam);
            DrawRect(new Rect2(-20f, -4f, 40f, 8f), WoodEdge, false, 1.6f);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        // --- 高墙（非台阶）加顶部坍塌缺口（几块砖掉了的锯齿边）---
        if (!isStep && r.Size.Y > 80f && !woodHouse)
        {
            var broken = new Color(0.25f, 0.22f, 0.19f, 0.85f);
            // 在墙顶部某段画 2~3 块缺失的砖位（暗色缺口）
            float gapStart = r.Position.X + r.Size.X * 0.35f;
            float gapLen = Mathf.Min(40f, r.Size.X * 0.22f);
            DrawRect(new Rect2(gapStart, topY - 2f, gapLen, 10f), broken);
            // 缺口下方延伸几条竖向裂纹
            var crack = new Color(0.25f, 0.20f, 0.16f, 0.6f);
            for (int i = 0; i < 3; i++)
            {
                float cx = gapStart + 6 + i * 10;
                DrawLine(new Vector2(cx, topY + 6), new Vector2(cx + (i - 1) * 4, topY + 38 + i * 8), crack, 1.4f);
            }
        }
    }

    /// <summary>瓦砾的碎片感：几道裂纹斜线。</summary>
    private void DrawRubbleMarks()
    {
        var rub = new Color(0.28f, 0.26f, 0.23f, 0.8f);
        foreach (var (x, y) in new[] { (3440, 760), (3480, 745), (3630, 745), (3680, 730), (3520, 790), (3710, 780) })
        {
            DrawLine(new Vector2(x, y), new Vector2(x + 34, y - 12), rub, 2.2f);
            DrawLine(new Vector2(x + 8, y + 8), new Vector2(x + 40, y + 2), rub, 1.6f);
        }
    }

    /// <summary>按宽度等比绘制抠图道具（底边中点锚定，保持纹理宽高比不被压扁）。</summary>
    private void DrawProp(Texture2D tex, Vector2 bottomCenter, float width)
    {
        var ts = tex.GetSize();
        if (ts.X < 1f) return;
        float h = width * ts.Y / ts.X;
        DrawTextureRect(tex, new Rect2(bottomCenter.X - width / 2f, bottomCenter.Y - h, width, h), false);
    }

    /// <summary>解谜物件绘制（v0.5 手绘贴图）：木箱 / 石块堆 / 门闩木门 / 补给 / 阀门与蒸汽 / 飞石。</summary>
    private void DrawPuzzleProps()
    {
        // 可推木箱（手绘木箱贴图，带碰撞体同步移动）
        var cw = PuzzleLogic.CrateW;
        var crateRect = new Rect2(_crateBody.Position + new Vector2(-cw / 2 - 4, -PuzzleLogic.CrateH / 2 - 5),
            new Vector2(cw + 8, PuzzleLogic.CrateH + 9));
        if (_texCrate != null)
            DrawProp(_texCrate, _crateBody.Position + new Vector2(0, PuzzleLogic.CrateH / 2f), cw + 18f);
        else { DrawRect(crateRect, WoodBrown); DrawRect(crateRect, WoodEdge, false, 2.4f); }

        // 石块堆
        var stoneC = new Color(0.5f, 0.49f, 0.45f);
        foreach (var pile in new[] { StonePile1, StonePile2 })
            if (_texStone != null)
                DrawProp(_texStone, pile, 88f);
            else
                DrawCircle(pile + new Vector2(0, -6), 7f, stoneC);

        // 侧巷木门与门闩（拉开后只余门洞暗影）
        if (!_boltOpen)
        {
            if (_texDoor != null)
                DrawTextureRect(_texDoor, new Rect2(DoorRect.Position.X - 5, DoorRect.Position.Y - 7,
                    DoorRect.Size.X + 10, DoorRect.Size.Y + 9), false);
            else
            {
                DrawRect(DoorRect, new Color(0.36f, 0.26f, 0.16f, 0.98f));
                DrawRect(DoorRect, WoodEdge, false, 2.2f);
            }
        }
        else
        {
            DrawRect(DoorRect, new Color(0.16f, 0.13f, 0.10f, 0.85f)); // 敞开的门洞
        }

        // 巷内补给（一小袋米）
        if (_boltOpen && !_supplyTaken)
        {
            var sack = new Rect2(SupplyPos + new Vector2(-11, -18), new Vector2(22, 18));
            DrawRect(sack, new Color(0.72f, 0.64f, 0.48f));
            DrawRect(sack, WoodEdge, false, 1.6f);
            DrawLine(SupplyPos + new Vector2(-11, -12), SupplyPos + new Vector2(11, -12), WoodEdge, 1.4f);
        }

        // 蒸汽阀门（管道 + 手轮贴图）
        if (_texValve != null)
            DrawProp(_texValve, ValvePos, 64f);
        else
        {
            var pipeC = new Color(0.44f, 0.46f, 0.44f);
            DrawRect(new Rect2(ValvePos.X - 8, ValvePos.Y - 70, 16, 70), pipeC);
            DrawArc(ValvePos + new Vector2(0, -46), 10f, 0, Mathf.Tau, 16, WoodEdge, 3f);
        }

        // 蒸汽云（阀门拧开时喷涌，遮挡视线）
        if (_steamT > 0f)
        {
            float fade = Mathf.Min(1f, _steamT / 3f);
            var sc = new Color(0.88f, 0.87f, 0.82f, 0.5f * fade);
            for (int i = 0; i < 5; i++)
            {
                float cx = SteamRect.Position.X + 20 + i * 24 + Mathf.Sin((float)Time.GetTicksMsec() / 300f + i) * 5f;
                DrawCircle(new Vector2(cx, SteamRect.Position.Y + 60 + (i % 2) * 60), 34 + i * 3, sc);
            }
        }

        // 飞行中的石块（小抛物线）
        if (_stoneFlying)
        {
            var p = _stoneFrom.Lerp(_stoneTo, _stoneT);
            p.Y -= Mathf.Sin(_stoneT * Mathf.Pi) * 70f;
            DrawCircle(p, 4f, stoneC);
        }
    }

    /// <summary>世界空间 E 提示气泡（VH 式靠近才浮现的互动图标）。</summary>
    private void DrawEMark()
    {
        if (_eMark == null) return;
        var p = _eMark.Value + new Vector2(0, Mathf.Sin((float)Time.GetTicksMsec() / 260f) * 3f);
        var paper = new Color(0.95f, 0.92f, 0.82f, 0.95f);
        DrawCircle(p, 11f, paper);
        DrawArc(p, 11f, 0, Mathf.Tau, 18, BrickEdge, 1.8f);
        var font = ThemeDB.FallbackFont;
        DrawString(font, p + new Vector2(0, 5), "E", HorizontalAlignment.Center, -1, 15, BrickEdge);
    }
}
