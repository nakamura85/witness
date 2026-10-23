using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Witness;

/// <summary>
/// 关卡 00「嫩江江桥 · 1931.11.4」垂直切片。
///
/// 设计要点（对应这次的明确要求）：
///  1) 场景分五层：天空 / 远山 / 雪原村落 / 近景树线 / 前景芦苇，逐层视差；
///  2) 人物是骨架关节动画（双段 IK 腿、摆臂、呼吸、蹲伏、跳跃收腿），
///     脚踩实地面、受重力与碰撞约束，不做“纸片人上下飘”；
///  3) 玩法不是杀敌：记录三份口述，敌兵推进—射击—被压制—退走，没有死亡演出。
/// </summary>
public partial class JiangqiaoLevel : Node2D
{
    private readonly Main _main;

    public const float WorldW = 5200f;
    public const float GroundY = 620f;

    internal FieldPlayer _player;
    private GroundShadow _playerShadow;
    private Camera2D _cam;
    private Node2D _game;
    private Hud _hud;
    private Label _hpLabel;
    private ColorRect _flash;
    private ColorRect _blackout;
    private Backdrop[] _layers;
    internal readonly List<EnemySoldier> _enemies = new();
    private readonly List<Testimony> _testimonies = new();
    private TracerFx _fx;
    private SnowScreen _snow;

    private float _shake;
    private float _flashA;
    private float _blackA;
    private float _blackTarget;
    private float _record;
    private float _lastToast = -99f;
    private float _elapsed;
    private bool _started;
    private bool _ended;
    private bool _noteOpen;
    private int _wave;
    private Vector2 _checkpoint = new(220, GroundY - 2);

    public JiangqiaoLevel(Main main) { _main = main; }

    public float PlayerX => _player?.GlobalPosition.X ?? 0f;
    public bool PlayerAlive => _player != null && _player.Hp > 0;
    /// <summary>相机中心的横坐标（敌人据此判断自己是否真的在玩家画面里）。</summary>
    public float CamX => _cam?.GlobalPosition.X ?? 0f;

    public override void _Ready()
    {
        BuildSky();
        BuildBackdrop();
        BuildTerrain();
        BuildPlayer();
        BuildTestimonies();
        BuildHud();
        BuildScreenFx();

        var intro = new IntroOverlay();
        AddChild(intro);
        intro.Show(
            "1931 年 11 月 4 日 · 嫩江江桥北岸",
            "天还没亮，江面冻得能走车。\n炮弹落在冰上，声音像撕开一匹布。\n你是东北军的一名通信兵——上级要你把阵地上还活着的人说过的话，带出去。",
            "",
            () =>
            {
                _started = true;
                _hud.ShowTutorial("A / D 移动 · 空格 跳跃 · S 蹲下 · E 按住记录 · 鼠标左键射击");
                Artillery(0.55f);
            });
    }

    // ================= 构图 =================

    private void BuildSky() => AddChild(new SkyScreen());

    private void BuildBackdrop()
    {
        _layers = new[]
        {
            new Backdrop(this, Backdrop.KindFar, 0.06f, -40),
            new Backdrop(this, Backdrop.KindMid, 0.24f, -30),
            new Backdrop(this, Backdrop.KindNear, 0.55f, -20),
            new Backdrop(this, Backdrop.KindFore, 1.32f, 20),
        };
        foreach (var l in _layers) AddChild(l);
    }

    private void BuildTerrain()
    {
        _game = new Node2D { ZIndex = 0 };
        AddChild(_game);

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

        // 脚印层（ZIndex 0，在 GroundArt(-1) 之上、玩家之前 AddChild → 画在玩家下面）
        _game.AddChild(new FootprintLayer(this) { ZIndex = 0 });

        // 掩体沙袋在人之前（ZIndex 5，高于玩家 0 与敌兵 2），
        // 经过时人从垛子后面过，而不是站在垛子顶上。
        _game.AddChild(new CoverArt { ZIndex = 5 });
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
        _game.AddChild(_player);

        _cam = new Camera2D
        {
            LimitLeft = 0,
            LimitRight = (int)WorldW,
            LimitTop = 0,
            LimitBottom = 900,
            PositionSmoothingEnabled = true,
            PositionSmoothingSpeed = 5.5f,
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
        _hud.SetDate("1931 年 11 月 4 日 · 嫩江江桥北岸");
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
    }

    private void BuildScreenFx()
    {
        AddChild(new VignetteLayer());
        AddChild(new FogScreen());

        var snowLayer = new CanvasLayer { Layer = 6 };
        AddChild(snowLayer);
        _snow = new SnowScreen();
        snowLayer.AddChild(_snow);

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
        float f = (float)delta;
        _elapsed += f;

        // 视差：每一层按自己的系数反向位移（层与层之间的深度差就是“层次感”）
        float camX = _cam.GlobalPosition.X;
        foreach (var l in _layers)
            l.Position = new Vector2(camX * (1f - l.Factor), 0f);
        _snow.Wind = 0.55f + Mathf.Sin(_elapsed * 0.35f) * 0.35f;

        // 相机跟随 + 抖动 + 轻微呼吸
        _shake = Mathf.Max(0f, _shake - f * 2.2f);
        var target = new Vector2(_player.GlobalPosition.X, _player.GlobalPosition.Y - 118f);
        _cam.Position = target;
        var off = new Vector2(
            (float)GD.RandRange(-1f, 1f) * _shake * 9f,
            (float)GD.RandRange(-1f, 1f) * _shake * 7f);
        _cam.Offset = off;

        // 玩家的影子钉在地面：跳得越高越小越淡，人不会显得脱离地面
        _playerShadow.X = _player.GlobalPosition.X;
        _playerShadow.Air = Mathf.Max(0f, (GroundY - 2f) - _player.GlobalPosition.Y);
        _playerShadow.QueueRedraw();

        _flashA = Mathf.Max(0f, _flashA - f * 1.6f);
        _flash.Color = new Color(1f, 0.96f, 0.88f, _flashA);
        _blackA = Mathf.MoveToward(_blackA, _blackTarget, f * 2.2f);
        _blackout.Color = new Color(0, 0, 0, _blackA);

        bool overlay = _noteOpen || _ended || !_started;
        _player.ControlLocked = overlay;

        if (!_started || _ended) return;

        UpdateRecording(f);
        UpdateObjective();
        UpdatePrompts();
        CheckWaves();
        CheckEnding();
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
        Testimony near = null;
        float best = 86f;
        foreach (var t in _testimonies)
        {
            if (t.Recorded) continue;
            float d = Mathf.Abs(t.GlobalPosition.X - _player.GlobalPosition.X);
            if (d < best) { best = d; near = t; }
        }

        if (near != null)
            _hud.ShowPrompt("按住 E · 记录口述");
        else if (GameState.I.S.JiangqiaoTestimony.Count >= 3 && PlayerX > 4200f)
            _hud.ShowPrompt("向东 · 走上江桥");
        else
            _hud.ShowPrompt("");

        // 右上生命
        string dots = _player.Hp >= 3 ? "● ● ●" : _player.Hp == 2 ? "● ● ○" : _player.Hp == 1 ? "● ○ ○" : "○ ○ ○";
        _hpLabel.Text = $"生命  {dots}";

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
        SpawnEnemy(baseX, -36f);
        SpawnEnemy(baseX + 120f, 36f);
        Artillery(0.35f);
        _hud.Toast("前方雪梁后出现关东军尖兵");
    }

    private void SpawnEnemy(float x, float lane)
    {
        if (x > WorldW - 120f) x = WorldW - 120f;
        var e = new EnemySoldier(this, new Vector2(x, GroundY), lane);
        // 两人一律 1.18 会让轮廓一模一样；微差一档，一前一后才立得起来。
        float s = lane < 0f ? 1.14f : 1.19f;
        e.Scale = new Vector2(s, s);
        e.Modulate = lane < 0f ? new Color(0.94f, 0.95f, 0.96f) : Colors.White;
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
        Shake(0.22f);
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

        Vector2 end = origin + new Vector2(faceRight ? 900f : -900f, 0);
        if (target != null)
        {
            end = target.Position + new Vector2(0, -62);
            target.Hit();
            _fx.Puff(end, 9f);
        }
        _fx.Tracer(origin, end, new Color(1f, 0.92f, 0.70f));
    }

    private void OnPlayerHit()
    {
        Shake(0.5f);
        _flashA = Mathf.Max(_flashA, 0.30f);
        _hud.Toast("你中弹了——蹲下能减少暴露");
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
        bool hit = GD.Randf() < (crouching ? 0.10f : 0.28f);

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

        public override void _Ready() => AddChild(new SkyArt());

        private partial class SkyArt : Control
        {
            private GradientTexture2D _glow;

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
            }

            public override void _Draw()
            {
                var s = GetViewportRect().Size;
                var top = new Color(0.16f, 0.20f, 0.24f);
                var mid = new Color(0.44f, 0.52f, 0.56f);
                var low = new Color(0.72f, 0.76f, 0.74f);
                int bands = 30;
                for (int i = 0; i < bands; i++)
                {
                    float t = i / (float)bands;
                    Color c = t < 0.55f ? top.Lerp(mid, t / 0.55f) : mid.Lerp(low, (t - 0.55f) / 0.45f);
                    DrawRect(new Rect2(0, s.Y * t, s.X, s.Y / bands + 1.5f), c);
                }

                // 冬日太阳：低、白、被云吃掉一半
                var sun = new Vector2(s.X * 0.76f, 150);
                DrawTextureRect(_glow, new Rect2(sun - new Vector2(150, 150), new Vector2(300, 300)), false);
                DrawCircle(sun, 26f, new Color(0.97f, 0.95f, 0.88f, 0.55f));
                DrawCircle(sun, 18f, new Color(0.98f, 0.96f, 0.90f, 0.35f));

                // 云：几团低矮的层云，边缘靠缩放拉软；不要拉成长条，否则像画面撕裂
                for (int i = 0; i < 5; i++)
                {
                    float cy = 96 + i * 46 + Mathf.Sin(i * 2.1f) * 14f;
                    float cx = s.X * 0.5f + Mathf.Sin(i * 2.7f) * 300f;
                    float w = 86 + 26 * Mathf.Sin(i * 1.9f);
                    for (int k = 0; k < 3; k++)
                    {
                        float off = (k - 1) * w * 0.55f;
                        DrawSetTransform(new Vector2(cx + off, cy + k * 5f), 0, new Vector2(2.4f, 0.72f));
                        DrawCircle(Vector2.Zero, w, new Color(0.30f, 0.36f, 0.41f, 0.075f - k * 0.012f));
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
        private float _windTime;

        public Backdrop(JiangqiaoLevel lvl, int kind, float factor, int z)
        {
            _lvl = lvl;
            Kind = kind;
            Factor = factor;
            ZIndex = z;
        }

        public override void _Process(double delta)
        {
            _windTime += (float)delta * 0.6f;
            if (Kind == KindMid || Kind == KindFore) QueueRedraw();
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

        // —— 远山：三层山脊，越远越淡（空气透视），山脊线用值噪声做出真锯齿 ——
        private static Rect2 Full(float x, float y, float h) => new(x, y, WorldW + 3800f, h);

        private void DrawFar()
        {
            // 三层山脊用不透明色填充、靠明度拉开距离。
            // （半透明填充会让亮天空从山体里透出来，山就会变成一团“雾”）
            var far = new Color(0.60f, 0.66f, 0.70f);
            var mid = new Color(0.47f, 0.54f, 0.60f);
            var near = new Color(0.35f, 0.42f, 0.48f);

            Ridge(448f, 104f, far, 1.3f, 0.0085f, 0.042f);
            // 远脊上压一层天空色，做空气透视（必须紧接着远脊画，
            // 否则它会把后面画的中脊、近脊也一起罩上一层白）
            Ridge(448f, 104f, new Color(0.80f, 0.84f, 0.84f, 0.20f), 1.3f, 0.0085f, 0.042f);

            var r1 = Ridge(492f, 76f, mid, 3.7f, 0.0110f, 0.058f);
            var r2 = Ridge(524f, 48f, near, 6.1f, 0.0160f, 0.078f);

            // 雪只挂在峰脊的迎风坡，越近的脊越淡，制造三层明度台阶
            RidgeSnow(r1, new Color(0.93f, 0.96f, 0.97f, 0.55f));
            RidgeSnow(r2, new Color(0.88f, 0.92f, 0.94f, 0.26f));

            // 山脚雾：一条窄带，只把山脚和雪原接起来
            var haze = VGradient(new Color(0.82f, 0.86f, 0.87f, 0f), new Color(0.82f, 0.86f, 0.87f, 0.72f));
            DrawTextureRect(haze, Full(-1400, 542, 62), false);
        }

        private List<Vector2> Ridge(float baseY, float amp, Color color, float seed, float f1, float f2)
        {
            var pts = new List<Vector2>();
            // 画到画面之外很远：视差会把整层左右平移上千像素，
            // 只画「世界范围」就会在屏幕边缘露出一条竖直硬边。
            for (float x = -1400; x <= WorldW + 2400; x += 26)
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

        private void RidgeSnow(List<Vector2> r, Color snow)
        {
            for (int i = 1; i < r.Count - 1; i++)
            {
                if (r[i].Y >= r[i - 1].Y) continue;      // 只画上升坡
                var a = r[i];
                var b = r[i + 1];
                var d = (b - a).Normalized();
                var up = new Vector2(-d.Y, d.X);
                float len = 16f + 12f * Mathf.Abs(Mathf.Sin(i * 0.73f));
                DrawLine(a + up * 1.5f, a + d * len, snow, 2.4f, true);
            }
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
            DrawTextureRect(fade, Full(-1400, 520, 58), false);
            DrawRect(Full(-1400, 578, 240), snow);

            // 雪原起伏的亮边与投影
            var crest = new List<Vector2>();
            for (float x = -1400; x <= WorldW + 2400; x += 70)
                crest.Add(new Vector2(x, 574 + Mathf.Sin(x * 0.0031f) * 6f + Mathf.Sin(x * 0.011f) * 3f));
            DrawPolyline(crest.ToArray(), new Color(0.92f, 0.94f, 0.945f, 0.65f), 2.2f, true);
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

            // 村落：压低、被雪埋住一半，屋顶压着厚雪
            float[] villages = { -760, 260, 690, 2520, 3000, 4180, 4590 };
            for (int i = 0; i < villages.Length; i++)
            {
                float x = villages[i];
                float w = 74 + (i % 3) * 22;
                float h = 34 + (i % 2) * 10;
                // 墙
                DrawRect(new Rect2(x - w * 0.42f, 566, w * 0.84f, 34), new Color(0.47f, 0.43f, 0.39f));
                DrawRect(new Rect2(x - w * 0.42f, 566, w * 0.84f, 34), new Color(0.28f, 0.26f, 0.24f, 0.5f), false, 1.2f);
                // 窗（一点暖光，避免整个场景死掉）
                if (i == 2 || i == 4)
                    DrawRect(new Rect2(x - 8, 576, 10, 10), new Color(0.86f, 0.68f, 0.38f, 0.55f));
                // 屋顶 + 厚雪
                var roof = new[]
                {
                    new Vector2(x - w * 0.58f, 566), new Vector2(x, 566 - h), new Vector2(x + w * 0.58f, 566),
                };
                DrawColoredPolygon(roof, new Color(0.42f, 0.44f, 0.44f));
                DrawPolyline(new[] { roof[0], roof[1], roof[2] }, new Color(0.26f, 0.27f, 0.27f, 0.7f), 1.4f, true);
                var cap = new[]
                {
                    new Vector2(x - w * 0.60f, 565), new Vector2(x, 565 - h - 4), new Vector2(x + w * 0.60f, 565),
                    new Vector2(x + w * 0.42f, 568), new Vector2(x, 568 - h + 3), new Vector2(x - w * 0.42f, 568),
                };
                DrawColoredPolygon(cap, new Color(0.90f, 0.92f, 0.92f));
                // 烟囱
                DrawRect(new Rect2(x + w * 0.20f, 552 - h, 8, 22), new Color(0.33f, 0.31f, 0.29f));
                DrawLine(new Vector2(x + w * 0.20f - 1, 552 - h), new Vector2(x + w * 0.20f + 9, 552 - h),
                    new Color(0.92f, 0.94f, 0.94f), 2.5f, true);
            }

            // 枯树线 + 白桦混排（嫩江 11 月白桦刚落叶，白树干配雪非常显眼）
            for (int i = 0; i < 96; i++)
            {
                float x = -1400 + i * 108 + Mathf.Sin(i * 3.7f) * 26f;
                float h = 32 + (i * 37 % 40);
                if (i % 4 == 0)
                    Birch(x, 578, h * 1.15f, 0.62f);
                else if (i % 3 == 0)
                    Tree(x, 578, h * 0.9f, new Color(0.34f, 0.36f, 0.36f, 0.55f), 2.2f);
                else
                    Fir(x, 580, h + 14, new Color(0.36f, 0.42f, 0.43f, 0.62f), 0.7f);
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

        // —— 近景：矮冷杉带、雪坎、铁丝网、废车（一律低于人物腰部，绝不遮脸）——
        private void DrawNear()
        {
            // 近景：矮冷杉带、雪坎、铁丝网、废车（一律低于人物腰部，绝不遮脸）
            for (int i = 0; i < 24; i++)
            {
                float x = -1400 + i * 350 + (i % 3) * 62;
                float h = 48 + (i * 37 % 34);
                Fir(x, 672, h, new Color(0.20f, 0.25f, 0.26f), 0.95f);
                DrawSetTransform(new Vector2(x, 670), 0, new Vector2(2f, 0.30f));
                DrawCircle(Vector2.Zero, 20f, new Color(0.95f, 0.96f, 0.96f, 0.8f));
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
            // 两棵略高的枯树，打破规则性（基线同样压在画面下缘）
            for (int i = 0; i < 7; i++)
            {
                float x = -1200 + i * 1180;
                Tree(x, 676, 86 + (i % 3) * 9, new Color(0.24f, 0.25f, 0.24f), 5f);
            }

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

        /// <summary>雪堆：外浅内白，边缘柔，避免硬边圆片。</summary>
        private void Drift(Vector2 c, float w, float h, Color snow)
        {
            DrawSetTransform(c + new Vector2(0, 4), 0, new Vector2(w / 20f, h / 20f * 1.4f));
            DrawCircle(Vector2.Zero, 20f, new Color(0.62f, 0.68f, 0.71f, 0.16f));
            DrawSetTransform(c, 0, new Vector2(w / 20f, h / 20f));
            DrawCircle(Vector2.Zero, 20f, new Color(snow, 0.55f));
            DrawCircle(Vector2.Zero, 15f, new Color(snow, 0.9f));
            DrawSetTransform(c + new Vector2(-w * 0.12f, -h * 0.16f), 0, new Vector2(1.2f, 0.42f));
            DrawCircle(Vector2.Zero, 20f, new Color(0.98f, 0.99f, 0.99f, 0.5f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        /// <summary>冷杉：塔形树冠 + 雪压枝线。</summary>
        private void Fir(float x, float baseY, float h, Color c, float alpha)
        {
            var trunk = new Color(c, alpha * 0.9f);
            DrawLine(new Vector2(x, baseY), new Vector2(x, baseY - h * 0.32f), trunk, 2.6f, true);

            float sway = Mathf.Sin(_windTime * 1.1f + x * 0.015f) * 0.03f;

            for (int k = 0; k < 4; k++)
            {
                float ty = baseY - h * (0.22f + 0.20f * k);
                float w = h * 0.30f * (1f - k * 0.21f);
                float top = ty - h * 0.30f;
                DrawSetTransform(new Vector2(x, ty + h * 0.15f), sway * (1f + k * 0.3f), Vector2.One);
                var tri = new[]
                {
                    new Vector2(x - w, ty), new Vector2(x, top), new Vector2(x + w, ty),
                };
                DrawColoredPolygon(tri, new Color(c, alpha));
                DrawLine(new Vector2(x - w * 0.94f, ty - 1.5f), new Vector2(x, top + 2f),
                    new Color(0.90f, 0.93f, 0.94f, 0.60f * alpha + 0.18f), 2.2f, true);
                DrawLine(new Vector2(x, top + 2f), new Vector2(x + w * 0.94f, ty - 1.5f),
                    new Color(0.86f, 0.90f, 0.91f, 0.45f * alpha + 0.14f), 1.8f, true);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
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

            // 芦苇：成簇的弯曲叶片，根部压在雪檐里（只到小腿高度，避免抢人物视线）
            float windPush = Mathf.Sin(_windTime * 1.4f) * 12f;
            for (int i = 0; i < 34; i++)
            {
                float x = -1350 + i * 330 + Mathf.Sin(i * 2.3f) * 52f;
                float baseY = 722 + Mathf.Sin(x * 0.006f) * 9f;
                float h = 52 + (i * 41 % 44);
                for (int b = 0; b < 8; b++)
                {
                    float dx = (b - 4) * 3.4f + Mathf.Sin(b * 2.1f) * 1.6f;
                    float lean = Mathf.Sin(i * 1.3f + b * 0.7f) * 15f + 5f + windPush * (0.5f + 0.5f * Mathf.Sin(i * 0.7f));
                    var root = new Vector2(x + dx, baseY);
                    var mid = new Vector2(x + dx * 1.3f + lean * 0.45f, baseY - h * 0.55f);
                    var tip = new Vector2(x + dx * 1.9f + lean, baseY - h - (b % 3) * 9f);
                    var blade = Curved(root, mid, tip, 6);
                    DrawPolyline(blade, new Color(0.16f, 0.17f, 0.17f, 0.58f), 2.2f, true);
                    if (b % 4 == 0)
                        DrawCircle(tip, 1.4f, new Color(0.93f, 0.95f, 0.95f, 0.6f));
                }
                // 根部的雪团
                DrawSetTransform(new Vector2(x, baseY + 1f), 0, new Vector2(1.8f, 0.3f));
                DrawCircle(Vector2.Zero, 12f, new Color(0.96f, 0.97f, 0.97f, 0.55f));
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
        }

        private void Tree(float x, float baseY, float h, Color c, float w)
        {
            DrawLine(new Vector2(x, baseY), new Vector2(x + 4, baseY - h), c, w, true);
            float sway = Mathf.Sin(_windTime * 0.9f + x * 0.018f) * 0.035f;
            for (int i = 0; i < 7; i++)
            {
                float t = 0.28f + i * 0.10f;
                float y = baseY - h * t;
                float dir = i % 2 == 0 ? 1f : -1f;
                float len = h * (0.30f - i * 0.022f);
                DrawSetTransform(new Vector2(x + 3, y), sway * (1f + i * 0.15f), Vector2.One);
                DrawLine(new Vector2(x + 3, y), new Vector2(x + 3 + dir * len, y - len * 0.55f), c, Mathf.Max(1.4f, w * 0.55f), true);
                DrawLine(new Vector2(x + 6 + dir * len * 0.6f, y - len * 0.30f),
                    new Vector2(x + 6 + dir * len * 0.9f, y - len * 0.40f),
                    new Color(0.90f, 0.92f, 0.92f, 0.55f), Mathf.Max(1.2f, w * 0.4f), true);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
        }

        /// <summary>白桦：白细干 + 黑斑纹 + 稀疏枯枝（冬天落叶后只剩细枝）。</summary>
        private void Birch(float x, float baseY, float h, float alpha)
        {
            float sway = Mathf.Sin(_windTime * 0.8f + x * 0.022f) * 0.04f;
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

            // 稀疏枯枝（冬天白桦只剩细枝）
            for (int i = 0; i < 5; i++)
            {
                float t = 0.30f + i * 0.13f;
                float y = baseY - h * t;
                float dir = i % 2 == 0 ? 1f : -1f;
                float len = h * (0.18f - i * 0.018f);
                DrawSetTransform(new Vector2(x + 1, y), sway * (1f + i * 0.1f), Vector2.One);
                DrawLine(new Vector2(x + 1, y), new Vector2(x + 1 + dir * len, y - len * 0.7f), dark, 1.6f, true);
                // 小枝的末梢分叉
                if (i % 2 == 0)
                    DrawLine(new Vector2(x + 1 + dir * len * 0.6f, y - len * 0.45f),
                        new Vector2(x + 1 + dir * len * 0.9f, y - len * 0.85f), dark, 1f, true);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
            // 顶端一撮细枝（最尖处）
            DrawSetTransform(new Vector2(x + 2, baseY - h), sway * 1.3f, Vector2.One);
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
            float a = 0.44f / (1f + Air / 110f);
            DrawSetTransform(new Vector2(X, GroundY - 1f), 0, new Vector2(1f, 0.26f));
            DrawCircle(Vector2.Zero, 21f * k, new Color(0.12f, 0.13f, 0.16f, a));
            DrawSetTransform(new Vector2(X + 4f, GroundY - 1f), 0, new Vector2(1f, 0.16f));
            DrawCircle(Vector2.Zero, 12f * k, new Color(0.12f, 0.13f, 0.16f, a * 0.55f));
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
        private float[] _bunkerX = System.Array.Empty<float>();
        private float[] _bunkerBW = System.Array.Empty<float>();
        private float[] _bunkerFH = System.Array.Empty<float>();
        private int[] _railSkipIdx = System.Array.Empty<int>();       // 被去掉的枕木序号（破损感）
        private int[] _trussSkipDiags = System.Array.Empty<int>();    // 桥桁架被去掉的斜腹杆
        private int[] _pierDamageIdx = System.Array.Empty<int>();     // 被炮弹蹭过的桥墩（画烧黑斑）

        public GroundArt(JiangqiaoLevel l) { _l = l; }

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
            DrawPolyline(surface.ToArray(), new Color(0.98f, 0.99f, 0.99f, 0.9f), 3f, true);
            DrawPolyline(surface.Select(p => p + new Vector2(0, 8)).ToArray(),
                new Color(0.66f, 0.72f, 0.76f, 0.55f), 6f, true);

            // 冰面裂缝与车辙
            for (int i = 0; i < 26; i++)
            {
                float x = 60 + i * 190;
                float y = 646 + (i % 3) * 12;
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
                DrawCircle(Vector2.Zero, 13f + (i % 3) * 3f, new Color(0.93f, 0.95f, 0.95f, 0.75f));
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
            // 远处冰面上的冻土堆（小冰丘）
            for (int i = 0; i < 5; i++)
            {
                var c = new Vector2(4520 + i * 140, GroundY + 540);
                DrawSetTransform(c, 0, new Vector2(1.8f, 0.5f));
                DrawCircle(Vector2.Zero, 10f, new Color(0.78f, 0.83f, 0.86f, 0.6f));
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
        }

        private void DrawRailway()
        {
            // 铁路从右侧战场一直铺到桥上
            var skip = new HashSet<int>(_railSkipIdx);
            int k = 0;
            for (float x = 2400; x < WorldW; x += 34, k++)
            {
                if (skip.Contains(k)) continue;  // 被炮弹削掉的枕木
                DrawLine(new Vector2(x, 622), new Vector2(x + 22, 632), new Color(0.31f, 0.26f, 0.21f), 5f, true);
                DrawLine(new Vector2(x, 626), new Vector2(x + 22, 636), new Color(0.88f, 0.88f, 0.85f), 2f, true);
            }
            DrawLine(new Vector2(2400, 620), new Vector2(WorldW, 620), new Color(0.24f, 0.22f, 0.20f), 2.4f, true);
            DrawLine(new Vector2(2400, 632), new Vector2(WorldW, 632), new Color(0.24f, 0.22f, 0.20f), 2.4f, true);
        }

        private void DrawCraters()
        {
            for (int i = 0; i < _craterX.Length; i++)
            {
                float x = _craterX[i];
                float r = _craterR[i];
                DrawSetTransform(new Vector2(x, GroundY + 2), 0, new Vector2(1f, 0.34f));
                DrawCircle(Vector2.Zero, r, new Color(0.38f, 0.35f, 0.32f, 0.75f));
                DrawCircle(Vector2.Zero, r * 0.72f, new Color(0.24f, 0.22f, 0.20f, 0.9f));
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
        }

        private void DrawBridge()
        {
            const float start = 4400f;
            const float end = 5200f;
            const float top = 376f;
            const float bottom = GroundY + 8f;

            // 远端桁架（偏后，颜色淡）——完好
            Truss(start + 40, end, top + 26, bottom - 4, new Color(0.20f, 0.23f, 0.25f, 0.40f), 3.4f, null);

            // 桥墩：从桥面落到冰面，其中一个被炮弹蹭过画烧黑斑
            var damagedPiers = new HashSet<int>(_pierDamageIdx);
            int pi = 0;
            for (float px = 4540; px <= 5140; px += 200, pi++)
            {
                DrawRect(new Rect2(px - 15, GroundY + 2, 30, 156), new Color(0.33f, 0.35f, 0.36f));
                DrawRect(new Rect2(px - 15, GroundY + 2, 30, 156), new Color(0.18f, 0.20f, 0.21f, 0.8f), false, 1.5f);
                DrawRect(new Rect2(px - 25, GroundY + 96, 50, 11), new Color(0.27f, 0.29f, 0.30f));
                DrawRect(new Rect2(px - 20, GroundY + 3, 40, 8), new Color(0.93f, 0.95f, 0.95f, 0.8f));
                // 炮弹烧黑的弹痕
                if (damagedPiers.Contains(pi))
                {
                    DrawCircle(new Vector2(px, GroundY + 55), 18f, new Color(0.10f, 0.09f, 0.08f, 0.7f));
                    DrawCircle(new Vector2(px + 5, GroundY + 90), 10f, new Color(0.10f, 0.09f, 0.08f, 0.5f));
                    DrawCircle(new Vector2(px - 4, GroundY + 130), 7f, new Color(0.18f, 0.16f, 0.14f, 0.55f));
                }
            }

            // 桥面板（侧梁 + 枕木）——其中一段被烧焦
            DrawRect(new Rect2(start, GroundY - 8, end - start, 22), new Color(0.24f, 0.23f, 0.21f));
            for (float x = start; x < end; x += 22)
                DrawLine(new Vector2(x, GroundY - 8), new Vector2(x, GroundY + 14), new Color(0.13f, 0.13f, 0.12f, 0.55f), 1f);
            DrawLine(new Vector2(start, GroundY - 8), new Vector2(end, GroundY - 8), new Color(0.55f, 0.52f, 0.45f, 0.7f), 3f, true);

            // 中段烧焦痕迹（桥面 + 枕木变黑）
            DrawRect(new Rect2(4680, GroundY - 10, 90, 24), new Color(0.08f, 0.07f, 0.06f, 0.8f));
            DrawLine(new Vector2(4680, GroundY - 10), new Vector2(4770, GroundY - 10), new Color(0.08f, 0.07f, 0.06f), 2f, true);

            // 近端桁架——有几根管被打断
            Truss(start, end, top, bottom + 4, new Color(0.14f, 0.17f, 0.19f), 6f, _trussSkipDiags);

            // 桁架上的积雪
            for (float x = start; x < end; x += 42)
                DrawLine(new Vector2(x, top - 3), new Vector2(x + 30, top - 3),
                    new Color(0.92f, 0.94f, 0.94f, 0.9f), 3f, true);
            // 烧焦那段桁架积雪被烧掉
            DrawRect(new Rect2(4680, top - 5, 90, 6), new Color(0.14f, 0.12f, 0.10f));

            // 桥头堡的沙袋由 CoverArt 画在前景（见下），这样人是从掩体后面过桥。
        }

        private void Truss(float start, float end, float top, float bottom, Color c, float w, int[] skipDiag)
        {
            var skip = skipDiag == null ? null : new HashSet<int>(skipDiag);
            DrawLine(new Vector2(start, top), new Vector2(end, top), c, w, true);
            DrawLine(new Vector2(start, bottom), new Vector2(end, bottom), c, w, true);
            int diagIdx = 0;
            for (float x = start; x <= end; x += 130, diagIdx++)
            {
                DrawLine(new Vector2(x, top), new Vector2(x, bottom), c, w * 0.8f, true);
                if (x + 130 <= end)
                {
                    bool skipThisDiag = skip != null && skip.Contains(diagIdx);
                    if (!skipThisDiag)
                        DrawLine(new Vector2(x, top), new Vector2(x + 130, bottom), c, w * 0.5f, true);
                    if (!(skip != null && skip.Contains(diagIdx + 1)))
                        DrawLine(new Vector2(x, bottom), new Vector2(x + 130, top), c, w * 0.5f, true);
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

            // 战壕边的木箱、弹药箱
            foreach (float x in new[] { 620f, 1030f, 2470f, 3690f })
            {
                // 接触阴影：箱子是“压进雪里”的，不是一个色块贴在雪面上。
                DrawSetTransform(new Vector2(x + 15f, GroundY - 1f), 0, new Vector2(1f, 0.24f));
                DrawCircle(Vector2.Zero, 20.5f, new Color(0.13f, 0.14f, 0.15f, 0.32f));
                DrawCircle(new Vector2(2.5f, 0f), 12.5f, new Color(0.11f, 0.12f, 0.13f, 0.24f));
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);

                DrawRect(new Rect2(x, GroundY - 26, 30, 26), new Color(0.44f, 0.36f, 0.24f));
                // 顶面受光一档，箱体才有上下的厚度
                DrawRect(new Rect2(x, GroundY - 26, 30, 4), new Color(0.55f, 0.46f, 0.31f));
                DrawLine(new Vector2(x, GroundY - 18), new Vector2(x + 30, GroundY - 18), new Color(0.28f, 0.23f, 0.16f), 1.4f, true);
                // 箱盖积雪
                DrawLine(new Vector2(x + 2, GroundY - 26.6f), new Vector2(x + 28, GroundY - 26.6f), new Color(0.90f, 0.93f, 0.94f, 0.82f), 2.2f, true);
                // 箱底把雪压出一圈高光接缝
                DrawLine(new Vector2(x + 1, GroundY - 0.8f), new Vector2(x + 29, GroundY - 0.8f), new Color(0.87f, 0.90f, 0.92f, 0.40f), 1.6f, true);
                DrawRect(new Rect2(x, GroundY - 26, 30, 26), new Color(0.22f, 0.19f, 0.15f), false, 1.4f);
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
                DrawSetTransform(new Vector2(bx + 24f, GroundY - 1f), 0, new Vector2(1f, 0.22f));
                DrawCircle(Vector2.Zero, 40f, new Color(0.12f, 0.13f, 0.14f, 0.32f));
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);

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
            public int Cols;
            public float Dx;
            public float RowShift;
            public float RowH;
            public float K;
        }

        private StackSpec[] _stacks = System.Array.Empty<StackSpec>();
        private SackSeed[][] _sackSeeds = new SackSeed[0][];

        private struct SackSeed { public float Tilt; public float SnowExtra; public float ArcK; }

        public override void _Ready()
        {
            var rng = new RandomNumberGenerator();
            rng.Randomize();
            float[] bases = { 980f, 2320f, 3580f, 4160f, 4388f };
            var list = new List<StackSpec>();
            var seedsList = new List<SackSeed[]>();
            for (int i = 0; i < bases.Length; i++)
            {
                bool isBridge = i == bases.Length - 1;
                list.Add(new StackSpec
                {
                    X = bases[i] + rng.RandfRange(-24f, 24f),
                    BaseY = isBridge ? GroundY - 4f + rng.RandfRange(-1f, 2f) : GroundY - 11f + rng.RandfRange(-1.5f, 1.5f),
                    Cols = (int)rng.RandfRange(isBridge ? 3f : 4f, 6.99f),
                    Dx = isBridge ? 18f + rng.RandfRange(-2f, 2f) : 19f + rng.RandfRange(-3f, 3f),
                    RowShift = 10f + rng.RandfRange(-2f, 2f),
                    RowH = 13f + rng.RandfRange(-2f, 2f),
                    K = isBridge ? 1f + rng.RandfRange(-0.05f, 0.05f) : 1.05f + rng.RandfRange(-0.06f, 0.06f),
                });
                // 预先生成这一垛每个沙袋的随机微扰（每 row×i 一个）
                var seeds = new List<SackSeed>();
                int maxCells = 3 * 6;  // 足够大
                for (int c = 0; c < maxCells; c++)
                    seeds.Add(new SackSeed
                    {
                        Tilt = rng.RandfRange(-0.04f, 0.04f),
                        SnowExtra = rng.RandfRange(-1.2f, 1.6f),
                        ArcK = rng.RandfRange(0.88f, 1.12f),
                    });
                seedsList.Add(seeds.ToArray());
            }
            _stacks = list.ToArray();
            _sackSeeds = seedsList.ToArray();
        }

        public override void _Draw()
        {
            for (int s = 0; s < _stacks.Length; s++)
                Stack(_stacks[s], _sackSeeds[s]);
        }

        private void Stack(StackSpec s, SackSeed[] seeds)
        {
            float w = (s.Cols - 1) * s.Dx + 20f;
            DrawSetTransform(new Vector2(s.X + w * 0.5f, GroundY - 1f), 0, new Vector2(1f, 0.22f));
            DrawCircle(Vector2.Zero, w * 0.56f, new Color(0.13f, 0.14f, 0.15f, 0.30f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);

            int si = 0;
            for (int row = 0; row < 3; row++)
                for (int i = 0; i < s.Cols - row; i++)
                {
                    var c = new Vector2(s.X + i * s.Dx + row * s.RowShift, s.BaseY - row * s.RowH);
                    float baseTilt = (row + i) % 2 == 0 ? 0.055f : -0.065f;
                    Sack(c, baseTilt + seeds[si].Tilt, s.K, seeds[si]);
                    si++;
                }
        }

        private void Sack(Vector2 c, float tilt, float k, SackSeed sd)
        {
            DrawSetTransform(c, tilt, new Vector2(1.55f * k, 0.70f * k));
            DrawCircle(Vector2.Zero, 14f, new Color(0.24f, 0.21f, 0.16f, 0.85f));
            DrawCircle(Vector2.Zero, 12.4f, new Color(0.62f, 0.56f, 0.43f));
            DrawArc(Vector2.Zero, 8.6f * k * sd.ArcK, 0.35f, Mathf.Pi - 0.35f, 12, new Color(0.42f, 0.37f, 0.28f, 0.6f), 1.4f, true);
            float snowR = Mathf.Clamp(6.4f * k + sd.SnowExtra, 4f, 9f);
            DrawCircle(new Vector2(0, -7.4f * k), snowR, new Color(0.90f, 0.92f, 0.91f, 0.9f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }

    // ================= 特效 =================

    private partial class TracerFx : Node2D
    {
        private readonly List<(Vector2 a, Vector2 b, float life, Color c)> _tracers = new();
        private readonly List<(Vector2 p, float life, float r)> _puffs = new();

        public void Tracer(Vector2 a, Vector2 b, Color c) => _tracers.Add((a, b, 0.14f, c));

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
            public float X, Y, Spd, Size, Sway, Depth, Rot;
            public bool Hex;
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
            for (int i = 0; i < 160; i++) AddFlake();
            _nextBlizzard = 25f + _rng.Randf() * 20f;
        }

        private void AddFlake()
        {
            float depth = _rng.Randf();
            _flakes.Add(new Flake
            {
                X = _rng.Randf() * 1280f,
                Y = _rng.Randf() * 720f,
                Spd = 22f + depth * 78f,
                Size = 0.6f + depth * 2.8f,
                Sway = _rng.Randf() * Mathf.Tau,
                Depth = depth,
                Hex = depth > 0.78f,
                Rot = _rng.Randf() * Mathf.Tau,
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

            // 动态增减：暴风雪时 ~520 颗（伸手不见五指），平时 160
            int targetCount = _blizzardIntensity > 0.3f ? 520 : 160;
            while (_flakes.Count < targetCount) AddFlake();
            if (_flakes.Count > targetCount)
                _flakes.RemoveRange(targetCount, _flakes.Count - targetCount);

            // 阵风 + 暴风雪额外风
            _gust = Mathf.Sin(t * 0.3f) * 0.4f + Mathf.Sin(t * 1.7f) * 0.15f;
            float extraWind = _blizzardIntensity * 3.2f;
            for (int i = 0; i < _flakes.Count; i++)
            {
                var p = _flakes[i];
                float spdMul = 1f + _blizzardIntensity * 1.8f;
                p.Y += p.Spd * spdMul * f;
                float baseHoriz = -30f * (Wind + extraWind) - p.Spd * 0.18f;
                float gustHoriz = -_gust * (24f + _blizzardIntensity * 55f);
                float swayHoriz = Mathf.Sin(t * 1.6f + p.Sway) * 12f * f * (1f + _blizzardIntensity);
                p.X += (baseHoriz + gustHoriz) * f + swayHoriz;
                p.Rot += f * 0.8f * (1f + p.Depth);
                if (p.Y > 730f) { p.Y = -10f; p.X = (p.X + 397f) % 1280f; }
                if (p.X < -20f) p.X += 1300f;
                if (p.X > 1300f) p.X -= 1300f;
                _flakes[i] = p;
            }
            QueueRedraw();
        }

        public override void _Draw()
        {
            // 暴风雪 fog：垂直渐变遮罩
            // 顶部（地平线方向）最浓 → 完全吃掉远景；底部（玩家脚边）稍透明
            if (_blizzardIntensity > 0.12f)
            {
                var s = GetViewportRect().Size;
                float topAlpha = 0.42f * _blizzardIntensity;    // 地平线处几乎完全遮罩
                float midAlpha = 0.25f * _blizzardIntensity;
                float botAlpha = 0.08f * _blizzardIntensity;    // 脚边还能看见地面
                // 用三个水平 Rect 堆成伪渐变（不用 GradientTexture2D——缓存嫌麻烦）
                float midY = s.Y * 0.55f;
                float botY = s.Y * 0.82f;
                DrawRect(new Rect2(0, 0, s.X, midY), new Color(0.72f, 0.77f, 0.80f, topAlpha));
                DrawRect(new Rect2(0, midY, s.X, botY - midY), new Color(0.74f, 0.78f, 0.81f, midAlpha));
                DrawRect(new Rect2(0, botY, s.X, s.Y - botY), new Color(0.78f, 0.81f, 0.83f, botAlpha));
                // 再加一层非常淡的整体 haze
                DrawRect(new Rect2(0, 0, s.X, s.Y), new Color(0.82f, 0.85f, 0.87f, 0.06f * _blizzardIntensity));
            }

            foreach (var p in _flakes)
            {
                float a = 0.28f + p.Depth * 0.5f;
                a *= (1f + _blizzardIntensity * 0.5f);

                if (p.Hex && p.Size > 1.5f && _blizzardIntensity < 0.4f)
                {
                    // 六角形雪花（只在平静时可见；暴风雪时全变斜线）
                    DrawSetTransform(new Vector2(p.X, p.Y), p.Rot, Vector2.One);
                    var pts = new Vector2[6];
                    for (int i = 0; i < 6; i++)
                    {
                        float ang = i * Mathf.Tau / 6f;
                        pts[i] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * p.Size;
                    }
                    DrawColoredPolygon(pts, new Color(0.97f, 0.99f, 1f, a));
                    DrawPolyline(pts, new Color(1f, 1f, 1f, a * 0.7f), 0.8f, true);
                    DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                }
                else
                {
                    // 圆形雪花（含暴风雪时所有雪花变斜线）
                    if (_blizzardIntensity > 0.5f)
                    {
                        // 暴风雪斜线：水平飘飞的白色细线（拖尾）
                        float tailLen = 6f + p.Depth * 10f + _blizzardIntensity * 12f;
                        DrawLine(
                            new Vector2(p.X, p.Y),
                            new Vector2(p.X + tailLen, p.Y + 1.5f),  // 略微向下
                            new Color(0.98f, 0.99f, 1f, a * 0.9f),
                            1f + p.Size * 0.3f, true);
                    }
                    else
                    {
                        DrawCircle(new Vector2(p.X, p.Y), p.Size, new Color(0.97f, 0.98f, 1f, a));
                    }
                    if (p.Depth > 0.7f && _blizzardIntensity < 0.5f)
                        DrawLine(new Vector2(p.X, p.Y), new Vector2(p.X - 4f, p.Y + 3f),
                            new Color(1, 1, 1, a * 0.45f), 1f, true);
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
                for (int i = 0; i < 4; i++)
                {
                    float y = 430 + i * 52 + Mathf.Sin(_t * 0.22f + i * 1.7f) * 18f;
                    float x = -120 + Mathf.Sin(_t * 0.11f + i) * 180f;
                    DrawSetTransform(new Vector2(x + s.X * 0.5f, y), 0, new Vector2(3.4f, 0.22f));
                    DrawCircle(Vector2.Zero, 220f, new Color(0.93f, 0.95f, 0.96f, 0.045f));
                    DrawCircle(Vector2.Zero, 150f, new Color(0.95f, 0.97f, 0.98f, 0.05f));
                }
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
        }
    }
}

/// <summary>关东军尖兵：从右侧雪梁后出现，推进—射击—被压制—退走（没有死亡演出）。</summary>
public partial class EnemySoldier : Node2D
{
    private readonly JiangqiaoLevel _level;
    public SpriteActor _fig;
    public float _prevPhase;
    private float _fireCd;
    private float _phase;
    private float _retreatT;
    private float _aimHold;
    private int _hits;
    private float _fade = 1f;
    private readonly float _holdX;
    /// <summary>同一波里的站位错位：两人不能停在同一条 x 上，否则轮廓会叠成一团。</summary>
    private readonly float _lane;

    public bool Gone { get; private set; }
    private bool _retreat;

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

    public override void _Process(double delta)
    {
        if (Gone) return;
        float f = (float)delta;

        if (_retreat)
        {
            _retreatT += f;
            Position += new Vector2(92f * f, 0);
            _fig.FacingRight = true;
            _fig.Speed = 92f;
            _fig.Phase += f * 9f;
            _fade = Mathf.Max(0f, 1f - Mathf.Max(0f, _retreatT - 1.2f) * 0.9f);
            Modulate = new Color(1, 1, 1, _fade);
            if (_fade <= 0.02f || Position.X > 5300f)
            {
                Gone = true;
                QueueFree();
            }
            return;
        }

        float px = _level.PlayerX;
        float dist = Position.X - px;

        // 推进到距玩家约 330px 处停下；到警戒线也停；玩家逼近则退半步。
        // 停位要带上各自的 lane，否则同波两人会叠在同一个 x 上变成一团黑影。
        float targetDist = 330f + _lane;
        float stopX = Mathf.Max(_holdX + _lane, _level.PlayerX + targetDist);
        if (Position.X > stopX + 16f)
        {
            Position -= new Vector2(64f * f, 0);
            _fig.Speed = 64f;
            _fig.Phase += f * 6.2f;
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
        _fig.FacingRight = false;
        _fig.Grounded = true;
        _fig.Crouching = dist < 380f && dist > 240f;
        _aimHold = Mathf.Max(0f, _aimHold - f);
        _fig.Aim = _aimHold > 0f || _fig.Crouching ? 1f : 0f;
        _fig.Aim = Mathf.MoveToward(_fig.Aim, _aimHold > 0f || _fig.Crouching ? 1f : 0f, f * 5f);

        _fireCd -= f;
        // 只有真的出现在玩家画面里、且在有效射程内才开火：
        // 画面外的敌人不能凭空打中你，这一条比弹道本身更影响公平感。
        bool onScreen = Mathf.Abs(Position.X - _level.CamX) < 540f;
        if (_fireCd <= 0f && onScreen && dist > 160f && dist < 700f && _level.PlayerAlive)
        {
            _fireCd = 2.5f + (float)GD.RandRange(-0.3f, 0.5f);
            _aimHold = 0.55f;
            _fig.MuzzleFlash = 1f;
            _level.EnemyFire(this);
        }
    }

    public void Hit()
    {
        if (Gone) return;
        _hits++;
        _fig.HurtFlash = 1f;
        Position += new Vector2(10f, 0);
        if (_hits >= 2)
        {
            _retreat = true;
            _level.Shake(0.1f);
        }
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
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var paper = new Color(0.93f, 0.90f, 0.80f);

        // 影子：电话木箱 + 纸页各压一片，物件才陷在雪里
        DrawSetTransform(new Vector2(1, -1), 0, new Vector2(1, 0.26f));
        DrawCircle(Vector2.Zero, 19f, new Color(0.12f, 0.13f, 0.14f, 0.26f));
        DrawSetTransform(new Vector2(18, -1), 0, new Vector2(1, 0.24f));
        DrawCircle(Vector2.Zero, 12f, new Color(0.12f, 0.13f, 0.14f, 0.20f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        // 野战电话：木箱 + 摇柄 + 听筒线。箱体分正面 / 顶面 / 侧面三块，
        // 不然就是一个描边方块，摆在雪地上像个界标。
        var bx0 = -14f; var bx1 = 8f; var by0 = -26f; var dx = 7f; var dy = -5f;
        DrawColoredPolygon(new[] { new Vector2(bx0, by0), new Vector2(bx0 + dx, by0 + dy), new Vector2(bx1 + dx, by0 + dy), new Vector2(bx1, by0) },
            new Color(0.47f, 0.38f, 0.28f));
        DrawColoredPolygon(new[] { new Vector2(bx1, by0), new Vector2(bx1 + dx, by0 + dy), new Vector2(bx1 + dx, -6f + dy), new Vector2(bx1, -1f) },
            new Color(0.24f, 0.19f, 0.14f));
        DrawRect(new Rect2(bx0, by0, bx1 - bx0, 25), new Color(0.36f, 0.28f, 0.20f));
        DrawRect(new Rect2(bx0, by0, bx1 - bx0, 25), new Color(0.16f, 0.13f, 0.10f), false, 1.6f);
        DrawPolyline(new[]
        {
            new Vector2(bx0, by0), new Vector2(bx0 + dx, by0 + dy),
            new Vector2(bx1 + dx, by0 + dy), new Vector2(bx1 + dx, -6f + dy),
            new Vector2(bx1, -1f), new Vector2(bx1, by0),
        }, new Color(0.16f, 0.13f, 0.10f), 1.3f, true);
        // 箱盖上的雪
        DrawLine(new Vector2(bx0 + 3, by0 + dy + 1), new Vector2(bx1 + dx - 2, by0 + dy + 1),
            new Color(0.90f, 0.93f, 0.94f, 0.72f), 1.6f, true);
        DrawRect(new Rect2(-11, -23, 22, 8), new Color(0.23f, 0.20f, 0.17f));
        DrawCircle(new Vector2(9, -22), 3f, new Color(0.52f, 0.42f, 0.24f));
        DrawLine(new Vector2(14, -18), new Vector2(21, -9), new Color(0.20f, 0.17f, 0.14f), 1.4f, true);
        DrawLine(new Vector2(-14, -20), new Vector2(-23, -8), new Color(0.22f, 0.19f, 0.15f), 1.2f, true);

        // 雪里的纸页
        var page = new[]
        {
            new Vector2(8, -6), new Vector2(24, -8), new Vector2(26, 1), new Vector2(10, 2),
        };
        DrawColoredPolygon(page, paper);
        DrawPolyline(new[] { page[0], page[1], page[2], page[3], page[0] }, new Color(0.35f, 0.29f, 0.22f), 1.2f, true);
        for (int i = 0; i < 3; i++)
            DrawLine(new Vector2(11, -4 + i * 2), new Vector2(23, -4.5f + i * 2), new Color(0.42f, 0.36f, 0.28f), 0.9f, true);

        if (Recorded)
        {
            DrawCircle(new Vector2(0, -34), 6f, new Color(0.42f, 0.55f, 0.35f));
            DrawLine(new Vector2(-2.6f, -34), new Vector2(-0.4f, -31.5f), new Color(0.94f, 0.95f, 0.88f), 1.6f, true);
            DrawLine(new Vector2(-0.4f, -31.5f), new Vector2(3.2f, -37.5f), new Color(0.94f, 0.95f, 0.88f), 1.6f, true);
            return;
        }

        // 呼吸光圈：告诉玩家这里可以记录
        float pulse = 0.5f + 0.5f * Mathf.Sin(_t * 2.2f);
        DrawArc(new Vector2(0, -10), 20f + pulse * 5f, 0, Mathf.Tau, 30,
            new Color(0.88f, 0.72f, 0.38f, 0.20f + 0.15f * pulse), 2f, true);
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
