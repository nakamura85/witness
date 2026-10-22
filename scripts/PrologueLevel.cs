using System.Collections.Generic;
using Godot;

namespace Witness;

/// <summary>
/// 第一章 · 源头 1895–1910（试玩关）：
/// 档案馆工作间里散落着六枚史料碎片——3 枚属于《马关条约》（1895），
/// 3 枚属于《日韩合并条约》（1910）。教学流程：移动 → 拾取 → 归位时间线板
/// → 炭笔历史场景显现 → 尾声指向 1931/1937。没有巡逻，没有失败，
/// 只有一间安静的房间和一段必须从源头讲起的历史。
/// </summary>
public partial class PrologueLevel : Node2D
{
    private readonly Main _main;

    // 以下坐标与 prologue_bg.jpg 的构图对齐：居中大长桌（约 220,200 → 1223,794），
    // 四周留木地板走道供行走与拾取。
    private static readonly Vector2 BoardTable = new(720, 770);              // 长桌南缘交互点
    private static readonly Rect2 TableRect = new(220, 200, 1003, 594);      // 长桌（兼碰撞）
    private static readonly Vector2 PlayerSpawn = new(750, 835);             // 桌前地板
    private static readonly Vector2[] FragmentSpots =
    {
        new(500, 110), new(1000, 120),   // 桌上方地板
        new(120, 420), new(1380, 420),   // 桌左右走道
        new(120, 780), new(1370, 780),   // 桌下两角
    };

    private bool _hasBg;                 // 底图存在时不做实心填色
    private Player _player;
    private Hud _hud;
    private IntroOverlay _intro;
    private PrologueBoardUi _board;

    private readonly List<Fragment> _fragments = new();
    private bool _tableActive;          // 全部拾取后长桌开始发光
    private bool _boardOpened;
    private bool _moved;                // 教学节点①：是否已移动过
    private enum Phase { Intro, Play, Board, Scene, Outro }
    private Phase _phase = Phase.Intro;

    public PrologueLevel(Main main) { _main = main; }

    public override void _Ready()
    {
        BuildRoom();

        _hud = new Hud();
        AddChild(_hud);
        _hud.SetDate(Loc.I.T("pro_date"));

        _intro = new IntroOverlay();
        AddChild(_intro);

        SetPhase(Phase.Intro);
        _intro.Show(Loc.I.T("pro_date"), Loc.I.T("pro_intro"), "", () =>
        {
            SetPhase(Phase.Play);
            _hud.ShowTutorial(Loc.I.T("tut2_move"));   // 教学①：移动
            RefreshObjective();
        });
    }

    private void BuildRoom()
    {
        // 档案馆工作间底图（素材缺失时用素纸色兜底）
        if (ResourceLoader.Exists("res://assets/art/prologue_bg.jpg"))
        {
            _hasBg = true;
            var tex = GD.Load<Texture2D>("res://assets/art/prologue_bg.jpg");
            var bg = new Sprite2D { Texture = tex, Centered = false };
            var ts = tex.GetSize();
            bg.Scale = new Vector2(1500f / ts.X, 900f / ts.Y);
            AddChild(bg);
        }
        else
        {
            var paper = new ColorRect { Color = new Color(0.87f, 0.82f, 0.71f), Size = new Vector2(1500, 900) };
            AddChild(paper);
        }

        // 房间四界 + 长桌
        AddWall(new Rect2(0, 0, 1500, 26));
        AddWall(new Rect2(0, 874, 1500, 26));
        AddWall(new Rect2(0, 0, 26, 900));
        AddWall(new Rect2(1474, 0, 26, 900));
        AddWall(TableRect); // 时间线板长桌（兼碰撞）

        _player = new Player { Position = PlayerSpawn };
        AddChild(_player);
        var camera = new Camera2D
        {
            PositionSmoothingEnabled = true,
            LimitLeft = 0, LimitTop = 0, LimitRight = 1500, LimitBottom = 900,
        };
        _player.AddChild(camera);
        camera.MakeCurrent();

        // 六枚史料碎片（读档恢复已拾取的）
        var s = GameState.I.S;
        for (int i = 0; i < PrologueLogic.FragmentIds.Length; i++)
        {
            string id = PrologueLogic.FragmentIds[i];
            var f = new Fragment(id) { Position = FragmentSpots[i], Taken = s.PrologueCollected.Contains(id) };
            AddChild(f);
            _fragments.Add(f);
        }
        _tableActive = PrologueLogic.AllPlaced(s) || s.PrologueCollected.Count >= PrologueLogic.FragmentIds.Length;
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

    private void SetPhase(Phase p)
    {
        _phase = p;
        if (_player != null)
            _player.SetPhysicsProcess(p == Phase.Play);
    }

    public override void _Process(double delta)
    {
        if (_phase != Phase.Play) return;

        // 教学①→②：第一次移动后提示拾取
        if (!_moved && _player.Position.DistanceTo(PlayerSpawn) > 80f)
        {
            _moved = true;
            if (GameState.I.S.PrologueCollected.Count == 0)
                _hud.ShowTutorial(Loc.I.T("tut2_move_pickup"));
        }

        UpdatePickup();
        UpdateTable();
        _hud.SetSneak(_player.Sneaking);
        if (_tableActive) QueueRedraw(); // 长桌呼吸光需要逐帧重绘
    }

    // ---- 教学②：拾取碎片 ----

    private void UpdatePickup()
    {
        Fragment near = null;
        float best = 64f;
        foreach (var f in _fragments)
        {
            if (f.Taken) continue;
            float d = f.Position.DistanceTo(_player.Position);
            if (d < best) { best = d; near = f; }
        }

        if (near == null) { _hud.ShowPrompt(""); return; }
        _hud.ShowPrompt(Loc.I.T("pro_prompt_pickup"));
        if (Input.IsActionJustPressed("interact"))
        {
            near.Taken = true;
            near.QueueRedraw();
            PrologueLogic.Collect(GameState.I.S, near.Id);
            GameState.I.Save();
            int n = GameState.I.S.PrologueCollected.Count;
            _hud.Toast(Loc.I.Tf("pro_picked", n, PrologueLogic.FragmentIds.Length));
            RefreshObjective();

            if (n >= PrologueLogic.FragmentIds.Length)
            {
                _tableActive = true;
                _hud.ShowTutorial(Loc.I.T("tut2_board"));  // 教学③：去找时间线板
                _hud.Toast(Loc.I.T("pro_all_collected"));
            }
            else if (n == 1)
            {
                _hud.ShowTutorial(Loc.I.T("tut2_pickup_more"));
            }
        }
    }

    // ---- 教学③：时间线板 ----

    private void UpdateTable()
    {
        if (_tableActive && !PrologueLogic.AllPlaced(GameState.I.S))
        {
            float d = BoardTable.DistanceTo(_player.Position);
            if (d < 130f)
            {
                _hud.ShowPrompt(Loc.I.T("pro_prompt_board"));
                if (Input.IsActionJustPressed("interact") && !_boardOpened)
                    OpenBoard();
            }
        }
        else
        {
            _hud.ShowPrompt("");
        }
    }

    private void OpenBoard()
    {
        _boardOpened = true;
        SetPhase(Phase.Board);
        _hud.ShowPrompt("");
        _hud.HideTutorial();
        _board = new PrologueBoardUi();
        _board.Finished += () =>
        {
            _board.QueueFree();
            _board = null;
            PlayScenes();
        };
        AddChild(_board);
    }

    // ---- 场景显现与尾声 ----

    private void PlayScenes()
    {
        SetPhase(Phase.Scene);
        var sc1895 = new PrologueSceneOverlay(
            "res://assets/art/scene_1895.jpg", "pro_scene1895_date",
            new[] { "pro_scene1895_1", "pro_scene1895_2" },
            () =>
            {
                var sc1910 = new PrologueSceneOverlay(
                    "res://assets/art/scene_1910.jpg", "pro_scene1910_date",
                    new[] { "pro_scene1910_1", "pro_scene1910_2" },
                    StartOutro);
                AddChild(sc1910);
            });
        AddChild(sc1895);
    }

    private void StartOutro()
    {
        SetPhase(Phase.Outro);
        var m = new MontageUi(new[] { "pro_outro_1", "pro_outro_2" });
        m.Finished += () =>
        {
            m.QueueFree();
            GameState.I.S.ApplyPrologue();
            GameState.I.Save();
            _main.ShowHub();
        };
        AddChild(m);
    }

    private void RefreshObjective()
    {
        var s = GameState.I.S;
        string text;
        if (s.ProloguePlaced.Count >= PrologueLogic.FragmentIds.Length)
            text = Loc.I.T("pro_obj_witness");
        else if (s.PrologueCollected.Count >= PrologueLogic.FragmentIds.Length)
            text = Loc.I.T("pro_obj_board");
        else
            text = Loc.I.Tf("pro_obj_collect", s.PrologueCollected.Count, PrologueLogic.FragmentIds.Length);
        _hud.SetObjective(text);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("pause"))
        {
            GameState.I.Save();
            _main.ShowTitle();
        }
    }

    // ---- 房间绘制：墙体 + 发光的长桌 ----

    public override void _Draw()
    {
        var ink = new Color(0.13f, 0.115f, 0.10f);
        var edge = new Color(0.55f, 0.50f, 0.42f, 0.45f);
        if (!_hasBg)
            DrawRect(TableRect, ink);       // 仅兜底色时画实心桌面
        DrawRect(TableRect, edge, false, 2f);

        if (_tableActive)
        {
            // 时间线板在桌上微微发光（呼吸）
            float pulse = 0.30f + 0.25f * Mathf.Sin((float)Time.GetTicksMsec() / 400.0f);
            var glow = new Rect2(TableRect.Position + new Vector2(-10, -10), TableRect.Size + new Vector2(20, 20));
            DrawRect(glow, new Color(0.93f, 0.89f, 0.78f, pulse));
        }
    }
}
