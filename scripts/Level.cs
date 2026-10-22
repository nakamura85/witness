using System.Collections.Generic;
using Godot;

namespace Witness;

/// <summary>
/// 关卡01：南京 1937 —— v0.3 横版（side-scroll）重建。
/// 参考横板叙事玩法：主路线横向推进，台阶/瓦砾/绕行屋（上楼翻越高墙），
/// 背景 2 层视差保持纵深，镜头横向跟随。
/// 五幕节拍保留：0 沦陷前夜（护送一家人到金女）→ 1 城破（记录教学+缴械士兵抉择）→
/// 2 沦陷之夜（巡逻下收集+门口抉择）→ 3 记忆的重量（取舍+出城）→ 4 结算。
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

    /// <summary>
    /// 横版地形表（碰撞与绘制共用）。Walkable=true 的平台是家人 NPC 可循行的地面线
    /// （GroundY 取其中最高台面）；其余为障碍/高台（需要跳跃或绕行）。
    /// </summary>
    private static readonly (Rect2 Rect, bool Walkable)[] Terrain =
    {
        // 主地面（全图）
        (new Rect2(0, Ground, WorldW, WorldH - Ground), true),
        // 左右边界
        (new Rect2(0, 0, 16, WorldH), false),
        (new Rect2(WorldW - 16, 0, 16, WorldH), false),
        // 起点后的台阶段（家人可循行，教学翻越）
        (new Rect2(600, 755, 170, 45), true),
        (new Rect2(770, 710, 170, 90), true),
        (new Rect2(940, 665, 240, 135), true),
        (new Rect2(1180, 710, 170, 90), true),
        (new Rect2(1350, 755, 170, 45), true),
        // 绕行屋（上楼翻越高墙）：二层地板 + 一层内堵墙 + 楼梯
        (new Rect2(2660, 560, 600, 30), false),
        (new Rect2(2920, 590, 60, Ground - 590), false),
        (new Rect2(2660, 758, 42, 42), false),
        (new Rect2(2702, 716, 42, 84), false),
        (new Rect2(2744, 674, 42, 126), false),
        (new Rect2(2786, 632, 42, 168), false),
        (new Rect2(2828, 590, 92, 210), false),
        // 城市瓦砾（跳跃段兼掩体）
        (new Rect2(3420, 730, 120, 70), false),
        (new Rect2(3600, 700, 150, 100), false),
        // 委员会高台
        (new Rect2(4700, 755, 130, 45), false),
        (new Rect2(4830, 710, 470, Ground - 710), false),
        // 鼓楼医院门前台阶
        (new Rect2(5760, 760, 130, 40), false),
        (new Rect2(5890, 720, 170, Ground - 720), false),
        // 教堂高台
        (new Rect2(6450, 750, 100, 50), false),
        (new Rect2(6550, 700, 220, Ground - 700), false),
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

    public Level(Main main) { _main = main; }

    /// <summary>巡逻兵据此判断是否行动（对话/结算期间全场静止）。</summary>
    public bool PlayPhase => _phase == Phase.Play;

    public override void _Ready()
    {
        BuildWorld();
        _hud = new Hud();
        AddChild(_hud);
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
        AddEvidence("rabe", new Vector2(5050, 692));    // 委员会高台
        AddEvidence("letter", new Vector2(5210, 692));  // 委员会高台
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

    /// <summary>背景 2 层视差：远层城墙天际线 + 中层残破街景（保持纵深感）。</summary>
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

    /// <summary>主地面瓦砾街道条（可平铺纹理）。</summary>
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

    /// <summary>侧视立面与建筑：金女立面图、绕行屋室内、教堂/医院/城门炭笔立面。</summary>
    private void BuildBackdrop()
    {
        AddSprite("res://assets/art/ginling_facade.jpg", new Vector2(2250, Ground - 240), 640f);
        AddSprite("res://assets/art/house_interior.jpg", new Vector2(2930, Ground - 120), 660f, top: Ground - 240);
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

    private void AddWall(Rect2 r)
    {
        var body = new StaticBody2D { Position = r.GetCenter(), CollisionLayer = 1 };
        var shape = new CollisionShape2D();
        var rect = new RectangleShape2D { Size = r.Size };
        shape.Shape = rect;
        body.AddChild(shape);
        AddChild(body);
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

        // 教学浮字（横版操作）
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
        UpdateFamily(f);
        UpdateTriggers();
        UpdateRecording(f);
        UpdateInteract();
        UpdateArrow();
        _hud.SetSneak(_player.Sneaking);
        RefreshObjectives();
    }

    private void UpdateFamily(float f)
    {
        if (_beat != 0 || !_familyFollowing) return;
        if (Mathf.Abs(_family.Position.X - JinlingDoor.X) < 80)
        {
            _familyFollowing = false;
            SetPhase(Phase.Talk);
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

    // ---- 屏幕边缘方向箭头（替代俯视红圈目标点）----

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

    // ---- 地形绘制：炭黑块面 + 纸色描边，叠加炭笔立面 ----

    public override void _Draw()
    {
        var ink = new Color(0.13f, 0.115f, 0.10f, 0.85f);
        var edge = new Color(0.55f, 0.50f, 0.42f, 0.5f);

        foreach (var (r, _) in Terrain)
        {
            DrawRect(r, ink);
            DrawRect(r, edge, false, 2f);
        }

        // 瓦砾的碎片感：几道炭笔斜线
        var rub = new Color(0.30f, 0.27f, 0.23f, 0.7f);
        foreach (var (x, y) in new[] { (3440, 760), (3480, 745), (3630, 745), (3680, 730), (3520, 790), (3710, 780) })
        {
            DrawLine(new Vector2(x, y), new Vector2(x + 34, y - 12), rub, 2.2f);
            DrawLine(new Vector2(x + 8, y + 8), new Vector2(x + 40, y + 2), rub, 1.6f);
        }

        DrawChurch();
        DrawHospital();
        DrawCityGate();

        // 操场集合点：虚线圆
        for (int i = 0; i < 24; i++)
        {
            float a0 = i / 24f * Mathf.Tau;
            float a1 = (i + 0.5f) / 24f * Mathf.Tau;
            DrawArc(AssemblyPoint + new Vector2(0, -60), 70f, a0, a1, 6, edge, 2f);
        }
    }

    /// <summary>教堂立面（炭笔）：山墙 + 十字 + 玫瑰窗。</summary>
    private void DrawChurch()
    {
        var ink = new Color(0.16f, 0.14f, 0.12f, 0.92f);
        var edge = new Color(0.55f, 0.50f, 0.42f, 0.55f);
        DrawRect(new Rect2(6500, 580, 320, Ground - 580), ink);
        DrawColoredPolygon(new Vector2[]
        {
            new(6470, 580), new(6850, 580), new(6660, 468),
        }, ink);
        DrawRect(new Rect2(6500, 580, 320, Ground - 580), edge, false, 2f);
        DrawLine(new Vector2(6660, 400), new Vector2(6660, 452), ink, 4f);   // 十字竖
        DrawLine(new Vector2(6640, 416), new Vector2(6680, 416), ink, 4f);   // 十字横
        DrawArc(new Vector2(6660, 640), 30f, 0, Mathf.Tau, 28, edge, 2.5f);  // 玫瑰窗
    }

    /// <summary>鼓楼医院立面（炭笔）：主楼 + 檐口 + 洗褪的红十字。</summary>
    private void DrawHospital()
    {
        var ink = new Color(0.16f, 0.14f, 0.12f, 0.92f);
        var edge = new Color(0.55f, 0.50f, 0.42f, 0.55f);
        DrawRect(new Rect2(5740, 480, 380, Ground - 480), ink);
        DrawRect(new Rect2(5726, 462, 408, 18), ink);
        DrawRect(new Rect2(5740, 480, 380, Ground - 480), edge, false, 2f);
        var red = Silhouette.FadedRed;
        DrawRect(new Rect2(5912, 540, 24, 84), red);
        DrawRect(new Rect2(5882, 570, 84, 24), red);
    }

    /// <summary>出城门楼（炭笔）：城台 + 门洞。</summary>
    private void DrawCityGate()
    {
        var ink = new Color(0.16f, 0.14f, 0.12f, 0.94f);
        var edge = new Color(0.55f, 0.50f, 0.42f, 0.55f);
        var paper = new Color(0.87f, 0.82f, 0.71f, 0.9f);
        DrawRect(new Rect2(7260, 430, 280, Ground - 430), ink);
        DrawRect(new Rect2(7240, 414, 320, 18), ink);
        DrawRect(new Rect2(7260, 430, 280, Ground - 430), edge, false, 2f);
        DrawRect(new Rect2(7350, 640, 110, Ground - 640), paper); // 门洞透光
        DrawRect(new Rect2(7350, 640, 110, Ground - 640), edge, false, 2f);
    }
}
