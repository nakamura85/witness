using System.Collections.Generic;
using Godot;

namespace Witness;

/// <summary>
/// 第一章 · 源头 1895–1910（试玩关）—— v0.4 横版（side-scroll）：
/// 档案馆工作间改为侧视房间，地面带高低差 —— 长桌、书架两层、中继台都可以 E 键翻越攀爬，
/// 桌下与地板散落着六枚史料碎片（3 枚《马关条约》1895 / 3 枚《日韩合并条约》1910）。
/// 教学流程：移动 → 翻越攀爬拾取 → 右墙时间线板归位 → 炭笔历史场景显现 → 尾声指向 1931/1937。
/// 没有巡逻，没有失败，只有一间安静的房间和一段必须从源头讲起的历史。
/// </summary>
public partial class PrologueLevel : Node2D
{
    private readonly Main _main;

    // 横版工作间：宽 2400，地板顶面 y=660（脚底原点）
    private const float RoomW = 2400f;
    private const float RoomH = 760f;
    private const float Floor = 660f;

    private static readonly Vector2 BoardPos = new(2190, Floor);          // 时间线板交互点（右墙前）
    private static readonly Rect2 BoardRect = new(2230, 300, 130, 330);   // 时间线板（右墙上）
    private static readonly Vector2 PlayerSpawn = new(600, Floor);
    private static readonly Vector2[] FragmentSpots =
    {
        new(120, Floor),    // 地面左角
        new(1900, Floor),   // 地面右段
        new(1000, 560),     // 长桌面上（跳上桌面）
        new(1150, Floor),   // 长桌底下（从桌下走过）
        new(300, 460),      // 书架第一层
        new(230, 360),      // 书架第二层（桌面 → 层板1 → 层板2）
    };

    // 横版平台地形：地板 / 长桌板 / 桌腿 / 书架层板 / 中继台
    private static readonly Rect2 FloorRect = new(0, Floor, RoomW, RoomH - Floor);
    private static readonly Rect2 TableTop = new(700, 560, 800, 24);
    private static readonly Rect2 TableLegL = new(730, 584, 26, Floor - 584);
    private static readonly Rect2 TableLegR = new(1444, 584, 26, Floor - 584);
    private static readonly Rect2 ShelfL1 = new(150, 460, 300, 24);
    private static readonly Rect2 ShelfL2 = new(150, 360, 200, 24);
    private static readonly Rect2 StepMid = new(452, 560, 70, Floor - 560); // 贴书架右缘的中继台：地板→台→层板1→层板2 各 100 高，逐级翻越

    // 可翻越物列表（x, y顶面, w, h）—— 桌腿太窄不列入，翻上桌面走桌面过去
    private static System.Numerics.Vector4 V4(Rect2 r)
        => new(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y);
    private static readonly System.Numerics.Vector4[] VaultSolids =
        { V4(TableTop), V4(ShelfL1), V4(ShelfL2), V4(StepMid) };

    private bool _hasBg;
    private Player _player;
    private Hud _hud;
    private IntroOverlay _intro;
    private PrologueBoardUi _board;

    private readonly List<Fragment> _fragments = new();
    private bool _boardActive;          // 全部拾取后时间线板开始发光
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
            _hud.ShowTutorial(Loc.I.T("tut2_move"));   // 教学①：移动与跳跃
            RefreshObjective();
        });
    }

    private void BuildRoom()
    {
        // 档案馆工作间侧景底图（素材缺失时用素纸色兜底）
        const string bgPath = "res://assets/art/workroom_side.jpg";
        if (ResourceLoader.Exists(bgPath))
        {
            _hasBg = true;
            var tex = GD.Load<Texture2D>(bgPath);
            var bg = new Sprite2D
            {
                Texture = tex,
                TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled,
                RegionEnabled = true,
                RegionRect = new Rect2(0, 0, RoomW, RoomH),
                Centered = false,
                ZIndex = -1,
            };
            AddChild(bg);
        }
        else
        {
            var paper = new ColorRect { Color = new Color(0.87f, 0.82f, 0.71f), Size = new Vector2(RoomW, RoomH) };
            AddChild(paper);
        }

        // 房间四界 + 平台地形
        AddWall(new Rect2(0, 0, RoomW, 20));       // 顶
        AddWall(FloorRect);                        // 地板
        AddWall(new Rect2(0, 0, 20, RoomH));       // 左墙
        AddWall(new Rect2(RoomW - 20, 0, 20, RoomH)); // 右墙
        AddWall(TableTop);
        AddWall(TableLegL);
        AddWall(TableLegR);
        AddWall(ShelfL1);
        AddWall(ShelfL2);
        AddWall(StepMid);

        _player = new Player { Position = PlayerSpawn };
        AddChild(_player);
        var camera = new Camera2D
        {
            PositionSmoothingEnabled = true,
            PositionSmoothingSpeed = 6f,
            LimitLeft = 0, LimitTop = 0, LimitRight = (int)RoomW, LimitBottom = (int)RoomH,
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
        _boardActive = PrologueLogic.AllPlaced(s) || s.PrologueCollected.Count >= PrologueLogic.FragmentIds.Length;
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

        bool uiBusy = UpdatePickup();
        uiBusy |= UpdateTable();
        if (!uiBusy) uiBusy = UpdateVault();
        if (!uiBusy) _hud.ShowPrompt("");
        _hud.SetSneak(_player.Sneaking);
        if (_boardActive) QueueRedraw(); // 时间线板呼吸光需要逐帧重绘
    }

    // ---- 教学②：拾取碎片 ----

    private bool UpdatePickup()
    {
        Fragment near = null;
        float best = 64f;
        foreach (var f in _fragments)
        {
            if (f.Taken) continue;
            float d = f.Position.DistanceTo(_player.Position);
            if (d < best) { best = d; near = f; }
        }

        if (near == null) return false;
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
                _boardActive = true;
                _hud.ShowTutorial(Loc.I.T("tut2_board"));  // 教学③：去找时间线板
                _hud.Toast(Loc.I.T("pro_all_collected"));
            }
            else if (n == 1)
            {
                _hud.ShowTutorial(Loc.I.T("tut2_pickup_more"));
            }
        }
        return true;
    }

    // ---- 教学③：时间线板 ----

    private bool UpdateTable()
    {
        if (_boardActive && !PrologueLogic.AllPlaced(GameState.I.S))
        {
            float d = BoardPos.DistanceTo(_player.Position);
            if (d < 130f)
            {
                _hud.ShowPrompt(Loc.I.T("pro_prompt_board"));
                if (Input.IsActionJustPressed("interact") && !_boardOpened)
                    OpenBoard();
                return true;
            }
        }
        return false;
    }

    // ---- E 翻越（v0.4 起取代跳跃）：靠近桌沿/层板/中继台按 E 攀上去 ----

    private bool UpdateVault()
    {
        if (_player.Vaulting || _player.Sneaking) return false;
        var feet = new System.Numerics.Vector2(_player.Position.X, _player.Position.Y);
        var hit = PuzzleLogic.FindVault(feet, _player.Facing, VaultSolids);
        if (hit == null) return false;
        _hud.ShowPrompt(Loc.I.T("hud_prompt_vault"));
        if (Input.IsActionJustPressed("interact"))
        {
            _hud.ShowPrompt("");
            _player.StartVault(hit.Value.topY, hit.Value.landX);
        }
        return true;
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

    // ---- 房间绘制：彩色手绘（VH 式）木色平台 + 发光的时间线板 ----

    public override void _Draw()
    {
        var outline = new Color(0.18f, 0.16f, 0.13f);
        var woodFloor = new Color(0.45f, 0.35f, 0.25f);
        var woodDark = new Color(0.36f, 0.28f, 0.20f);
        var woodWarm = new Color(0.56f, 0.43f, 0.29f);

        // 地板：暖木 + 木板缝
        DrawRect(FloorRect, woodFloor);
        for (float x = 60; x < RoomW; x += 90)
            DrawLine(new Vector2(x, Floor + 8), new Vector2(x, Floor + 28), woodDark, 1.6f);
        DrawLine(FloorRect.Position, FloorRect.Position + new Vector2(RoomW, 0), outline, 2.4f);

        // 长桌：暖木桌面 + 深色桌腿
        DrawRect(TableTop, woodWarm);
        DrawRect(TableTop, outline, false, 2f);
        DrawRect(TableLegL, woodDark);
        DrawRect(TableLegR, woodDark);

        // 书架层板
        DrawRect(ShelfL1, woodWarm);
        DrawRect(ShelfL1, outline, false, 1.8f);
        DrawRect(ShelfL2, woodWarm);
        DrawRect(ShelfL2, outline, false, 1.8f);

        // 中继台（木箱）
        DrawRect(StepMid, woodDark);
        DrawRect(StepMid, outline, false, 1.8f);

        // 右墙时间线板：深色木框 + 纸色空槽位
        DrawRect(BoardRect, woodDark);
        DrawRect(BoardRect, outline, false, 2.2f);
        var slot = new Color(0.88f, 0.83f, 0.71f, 0.5f);
        for (int i = 0; i < 3; i++)
            DrawRect(new Rect2(BoardRect.Position + new Vector2(14, 26 + i * 92), new Vector2(102, 76)), slot, false, 1.4f);

        if (_boardActive)
        {
            // 时间线板微微发光（呼吸）
            float pulse = 0.30f + 0.25f * Mathf.Sin((float)Time.GetTicksMsec() / 400.0f);
            var glow = new Rect2(BoardRect.Position + new Vector2(-10, -10), BoardRect.Size + new Vector2(20, 20));
            DrawRect(glow, new Color(0.93f, 0.89f, 0.78f, pulse));
        }
    }
}
