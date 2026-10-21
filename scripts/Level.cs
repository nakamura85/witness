using System.Collections.Generic;
using Godot;

namespace Witness;

/// <summary>
/// 关卡01：南京 1937 —— 世界搭建与五幕节拍（Beat）的总编排。
/// 地图为代码搭建的炭笔风俯视场景：墙体=StaticBody2D（兼碰撞与绘制），
/// 角色=Silhouette 剪影，巡逻=视野锥，证据=发光纸页。
/// 五幕：0 沦陷前夜（护送一家人）→ 1 城破（记录教学+收留抉择）→
/// 2 沦陷之夜（巡逻下收集+阻挡抉择+物资分配）→ 3 记忆的重量（三件取舍+出城）→ 4 结算。
/// </summary>
public partial class Level : Node2D
{
    private readonly Main _main;

    // ---- 世界关键坐标 ----
    private static readonly Vector2 GateTalk = new(170, 930);      // 大门内（Beat1 士兵事件）
    private static readonly Vector2 JinlingDoor = new(900, 860);   // 金女主楼大门
    private static readonly Vector2 AssemblyPoint = new(1350, 950);// 操场集合点（被抓押送至此）
    private static readonly Vector2 PlayerSpawn = new(220, 1010);

    private Player _player;
    private readonly List<Rect2> _walls = new();
    private readonly List<Rect2> _covers = new();
    private readonly List<Vector2> _doorMarks = new();
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
        // 纸面底图（炭笔手绘校园图；素材缺失时用素纸色兜底）
        if (ResourceLoader.Exists("res://assets/art/map_bg.jpg"))
        {
            var tex = GD.Load<Texture2D>("res://assets/art/map_bg.jpg");
            var bg = new Sprite2D { Texture = tex, Centered = false };
            var ts = tex.GetSize();
            bg.Scale = new Vector2(2600f / ts.X, 1700f / ts.Y);
            AddChild(bg);
        }
        else
        {
            var paper = new ColorRect { Color = new Color(0.87f, 0.82f, 0.71f), Size = new Vector2(2600, 1700) };
            AddChild(paper);
        }

        // 墙体（碰撞 + 绘制共用）
        AddWall(new Rect2(0, 0, 2600, 26));      // 北界
        AddWall(new Rect2(0, 1674, 2600, 26));   // 南界
        AddWall(new Rect2(2574, 0, 26, 1700));   // 东界
        AddWall(new Rect2(0, 0, 26, 830));       // 西墙（大门缺口 830-950）
        AddWall(new Rect2(0, 950, 26, 750));
        AddWall(new Rect2(700, 500, 400, 300));  // 金陵女子文理学院主楼
        AddWall(new Rect2(1250, 400, 250, 250)); // 宿舍楼
        AddWall(new Rect2(1750, 300, 300, 200)); // 安全区委员会办公室
        AddWall(new Rect2(1900, 900, 350, 250)); // 教堂（马吉神父住处）
        AddWall(new Rect2(2200, 300, 300, 300)); // 鼓楼医院

        foreach (var c in new[] { (500, 1200), (1120, 1150), (1600, 600), (2000, 1300), (760, 300), (1500, 1500) })
            _covers.Add(new Rect2(c.Item1, c.Item2, 60, 40)); // 掩体（GDD §6.3）

        _doorMarks.AddRange(new[]
        {
            new Vector2(900, 806), new Vector2(1855, 506),
            new Vector2(2210, 452), new Vector2(1955, 906),
        });

        // 玩家：明妮·魏特琳
        _player = new Player { Position = PlayerSpawn };
        AddChild(_player);
        var camera = new Camera2D
        {
            PositionSmoothingEnabled = true,
            LimitLeft = 0, LimitTop = 0, LimitRight = 2600, LimitBottom = 1700,
        };
        _player.AddChild(camera);
        camera.MakeCurrent();

        // 证据点（6 件，对应史料卡 1-6）
        AddEvidence("roster", new Vector2(900, 845));    // 金女难民名册
        AddEvidence("vautrin", new Vector2(770, 845));   // 魏特琳日记
        AddEvidence("rabe", new Vector2(1855, 545));     // 拉贝日记
        AddEvidence("letter", new Vector2(2005, 545));   // 委员会抗议信
        AddEvidence("medical", new Vector2(2150, 450));  // 鼓楼医院病历
        AddEvidence("film", new Vector2(1955, 855));     // 马吉胶片

        // 难民：6 位深度交互 + 6 位环境难民（GDD §7：12 名，深度交互 6 名）
        AddDeep("r1", "food", new Vector2(1150, 1050));
        AddDeep("r2", "medicine", new Vector2(980, 1180));
        AddDeep("r3", "medicine", new Vector2(1620, 900));
        AddDeep("r4", "blanket", new Vector2(820, 1300));
        AddDeep("r5", "blanket", new Vector2(1350, 1300));
        AddDeep("r6", "food", new Vector2(1720, 1150));
        foreach (var p in new Vector2[]
        {
            new(600, 800), new(1050, 950), new(1500, 1000),
            new(1250, 1450), new(1850, 1250), new(2250, 950),
        })
        {
            AddChild(new Refugee("", "", Silhouette.Kind.Refugee, wander: true) { Position = p });
        }

        // 逃难的一家（Beat0 护送目标）
        _family = new Node2D { Position = new Vector2(300, 1060) };
        _family.AddChild(new Refugee("", "", Silhouette.Kind.FamilyAdult));
        _family.AddChild(new Refugee("", "", Silhouette.Kind.FamilyChild) { Position = new Vector2(14, 6) });
        AddChild(_family);

        // 缴械的中国士兵（Beat1，初始隐藏）
        _soldiers = new Node2D { Position = new Vector2(150, 940), Visible = false };
        for (int i = 0; i < 3; i++)
            _soldiers.AddChild(new Refugee("", "", Silhouette.Kind.Refugee) { Position = new Vector2(i * 26 - 26, 0) });
        AddChild(_soldiers);

        // 乔治·费奇（Beat2 末出现的带片人，真实历史人物）
        _fitch = new Refugee("fitch", "", Silhouette.Kind.Fitch) { Position = new Vector2(2050, 970), Visible = false };
        AddChild(_fitch);
    }

    private void AddWall(Rect2 r)
    {
        _walls.Add(r);
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

        // 教学浮字（GDD §5 Beat0-2 各带一条）
        switch (n)
        {
            case 0: _hud.ShowTutorial(Loc.I.T("tut_move")); break;
            case 1: _hud.ShowTutorial(Loc.I.T("tut_record")); break;
            case 2: _hud.ShowTutorial(Loc.I.T("tut_sneak")); break;
            default: _hud.HideTutorial(); break;
        }

        if (n >= 4)
        {
            // 直接重进结算（通关后读档的情形）
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
        AddPatrol(new Vector2[] { new(600, 1050), new(1450, 1050), new(1450, 720), new(600, 720) });
        AddPatrol(new Vector2[] { new(1550, 1300), new(2320, 1300), new(2320, 760), new(1550, 760) });
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
        _hud.SetSneak(_player.Sneaking);
        RefreshObjectives();
    }

    private void UpdateFamily(float f)
    {
        if (_beat != 0 || !_familyFollowing) return;
        if (_family.Position.DistanceTo(JinlingDoor) < 80)
        {
            _familyFollowing = false;
            SetPhase(Phase.Talk);
            _dialogue.ShowLine(Loc.I.T("npc_family"), Loc.I.T("npc_family_done"), () => StartBeat(1));
            return;
        }
        if (_family.Position.DistanceTo(_player.Position) > 80)
            _family.Position += (_player.Position - _family.Position).Normalized() * 190f * f;
    }

    private void UpdateTriggers()
    {
        if (_beat == 1 && !_c1Done && _player.Position.DistanceTo(GateTalk) < 150) TriggerC1();
        if (_beat == 2 && !_c2Done && _player.Position.DistanceTo(JinlingDoor) < 150) TriggerC2();
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
        // 大门外跪着缴械的士兵 —— 首个道德选择（GDD Beat1）
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
            _c2Group = new Node2D { Position = JinlingDoor + new Vector2(40, -50) };
            for (int i = 0; i < 3; i++)
                _c2Group.AddChild(new Refugee("w" + (i + 1), "", Silhouette.Kind.Refugee)
                {
                    Position = new Vector2(i * 30 - 30, 0),
                });
            _c2Group.AddChild(new Refugee("", "", Silhouette.Kind.Soldier) { Position = new Vector2(34, -22) });
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

    // ---- 地图绘制：炭黑块面 + 纸色描边 ----

    public override void _Draw()
    {
        var ink = new Color(0.13f, 0.115f, 0.10f);
        var edge = new Color(0.55f, 0.50f, 0.42f, 0.45f);

        foreach (var r in _walls)
        {
            DrawRect(r, ink);
            DrawRect(r, edge, false, 2f);
        }
        foreach (var r in _covers)
        {
            DrawRect(r, new Color(0.20f, 0.18f, 0.16f));
            DrawRect(r, edge, false, 1.5f);
        }
        // 操场集合点：虚线圆
        for (int i = 0; i < 24; i++)
        {
            float a0 = i / 24f * Mathf.Tau;
            float a1 = (i + 0.5f) / 24f * Mathf.Tau;
            DrawArc(AssemblyPoint, 90f, a0, a1, 6, edge, 2f);
        }
        // 大门门柱
        DrawRect(new Rect2(6, 808, 20, 22), ink);
        DrawRect(new Rect2(6, 950, 20, 22), ink);
        // 各门口的纸片标记
        foreach (var p in _doorMarks)
            DrawRect(new Rect2(p.X - 14, p.Y - 4, 28, 8), new Color(0.93f, 0.89f, 0.78f, 0.5f));
    }
}
