using System;
using System.Collections.Generic;
using Godot;

namespace Witness;

/// <summary>
/// v3.67 可探索屋内的一件实体物件（纯数据，不继承任何 UI 节点）。
/// 旧版是 Spot : Button 的「文字标签」；现在改成房间里画出来的实物，
/// 玩家小人走过去按 E 才查看。label 只用于靠近时的小提示牌。
/// </summary>
public sealed class RoomItem
{
    public string SpotId;
    public string Kind;      // ammo / grenade / letter / will / clue / gauze / medkit / chest / ash / power / radio
    public string Label;
    public bool Done;
    public Vector2 Pos;      // 房间画布坐标（物件中心）
    public float Progress;   // 0..1，撬箱 / 合闸 / 发报的长按进度
}

/// <summary>
/// v3.68 室内背景：与关卡同源的「手绘上色漫画风」插画贴图。
///
/// 用户 2026-11-12 判定：v3.67 那套纯 _Draw() 平涂几何墙 + 米黄 UI 标签牌，
/// 跟室外 AI 手绘插画贴图「根本是两回事」。所以背景层改用同一批手绘上色漫画风
/// 贴图（assets/art/interior/），只在外面罩一层 InkStyle 的 3px 深墨棕漫画分格框，
/// 保证进屋之后和屋外仍是同一个世界、同一套笔触与色调。
///
/// 三张贴图分别对应住家 / 通信据点 / 焚毁仓库；住家 variant 1 左右镜像，
/// 让两间民居不完全一样。贴图缺失时退回到 v3.65 的 RoomBg 平涂，不至于黑屏。
/// </summary>
public partial class InteriorBackdrop : Control
{
    private readonly RoomType _type;
    private readonly int _variant;
    private readonly int _seed;
    private Texture2D _tex;
    private TextureRect _view;
    private Control _frame;

    public InteriorBackdrop(RoomType type, int variant, int seed)
    {
        _type = type;
        _variant = variant;
        _seed = seed;
        MouseFilter = Control.MouseFilterEnum.Ignore;
    }

    private static string PathFor(RoomType type) => type switch
    {
        RoomType.CommOutpost => "res://assets/art/interior/comm_outpost.png",
        RoomType.BurntWarehouse => "res://assets/art/interior/burnt_warehouse.png",
        _ => "res://assets/art/interior/residence.png",
    };

    public override void _Ready()
    {
        ClipContents = true;
        _tex = GD.Load<Texture2D>(PathFor(_type));

        if (_tex != null)
        {
            _view = new TextureRect
            {
                Texture = _tex,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                FlipH = (_type == RoomType.Residence && _variant != 0),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _view.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(_view);
        }
        else
        {
            // 兜底：贴图没导入时用旧的程序化房间，只是不再叠 UI 标签
            var bg = HouseInteriorUi.CreateFallbackRoomBackground(_type, _variant, _seed);
            bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(bg);
        }

        // 漫画分格：3px 深墨棕粗框 + 四角卡通高光短线，与关卡、纸张同一套语言
        _frame = new InteriorInkFrame { MouseFilter = Control.MouseFilterEnum.Ignore };
        _frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_frame);
    }
}

/// <summary>室内贴图外罩的漫画分格框（3px 深墨棕 + 四角高光，不用圆角 UI 卡片）。</summary>
public partial class InteriorInkFrame : Control
{
    public override void _Draw()
    {
        float W = Size.X, H = Size.Y;
        if (W < 10f || H < 10f) return;
        // 上下各一条硬边压暗带，把插画「压」进漫画分格里
        DrawRect(new Rect2(0, 0, W, 10f), new Color(0.14f, 0.10f, 0.08f, 0.55f));
        DrawRect(new Rect2(0, H - 12f, W, 12f), new Color(0.14f, 0.10f, 0.08f, 0.48f));
        DrawRect(new Rect2(2, 2, W - 4, H - 4), InkStyle.InkColor, false, 3f);
        var lit = new Color(0.99f, 0.94f, 0.80f);
        foreach (var p in new[]
        {
            new Vector2(14, 14), new Vector2(W - 14, 14),
            new Vector2(14, H - 14), new Vector2(W - 14, H - 14),
        })
            DrawLine(p, p + new Vector2(10, 10), lit, 2f, true);
    }
}

/// <summary>
/// v3.67 手绘上色漫画风的「可进入、可走动、可寻找」屋内空间。
///
/// 取代 v3.64–v3.66 的 HouseInteriorUi.BuildRoom()：那一版只是
/// RoomBg 背景 + 一排 Spot : Button 文字标签，玩家角色根本没进屋。
/// 现在：
///   • 背景层仍是 RoomBg（漫画风墙 / 地 / 家具，按 RoomType 分格局）；
///   • 前景层画一扇门、散落在家具上的实体物件、以及一个能左右走动的玩家小人；
///   • HouseInteriorUi._Process 把 move_left / move_right / interact 转进来，
///     靠近物件按 E 复用原 OnSpot() 搜刮 / 读信 / 撬箱 / 发报逻辑；
///   • 走到最左边的门口按 E 离开。
///
/// 风格严格走 scripts/InkStyle.cs：3px 深墨棕粗描边、base→shadow→highlight
/// 三档平涂、卡通高光小圆点 / 短直线、硬边压扁椭圆接触阴影；
/// 不做插值渐变、柔边羽化、纸纹水痕。
/// </summary>
public partial class InteriorRoom : Control
{
    public RoomType Type;
    public int Variant;
    public int Seed;
    public string Title = "屋内";
    public string ResultText = "";
    public readonly List<RoomItem> Items = new();

    /// <summary>玩家小人在房间画布上的 x（脚底落在地板行走带）。</summary>
    public float PlayerX = 132f;
    public int NearIndex = -1;
    public bool AtDoor => PlayerX <= 82f;
    /// <summary>v3.70：进入室内自动切换第一视角（隐藏走动小人，改画准星扫视）。默认开启。</summary>
    public bool FirstPerson = true;

    private InteriorForeground _fg;
    private float _walkT;
    private float _facing = 1f;

    /// <summary>只给 CaptureRoom 预览用：没有真实 loot 时，按房间格局造一组示例物件。</summary>
    public static InteriorRoom Create(RoomType type, int variant, int seed, string title,
        IEnumerable<RoomItem> items = null)
    {
        var r = new InteriorRoom
        {
            Type = type,
            Variant = variant,
            Seed = seed,
            Title = string.IsNullOrEmpty(title) ? "屋内" : title,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        if (items != null) r.Items.AddRange(items);
        else r.Items.AddRange(PreviewItems(type, seed));
        r.Layout();
        return r;
    }

    /// <summary>按房间格局把物件摆到对应家具上，而不是均匀排队。</summary>
    public void Layout()
    {
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)(Seed & 0xffffff) + 7919u;

        // 每类房间一组「家具锚点」：信压桌上、弹药盒靠箱、电台在角落……
        // 坐标基于约 952×592 的屋内画布，地板行走带 y≈430..510。
        Vector2[] anchors = Type switch
        {
            RoomType.CommOutpost => new[]
            {
                new Vector2(170f, 452f),  // 左墙电闸 / 手摇发电机旁
                new Vector2(300f, 468f),  // 木箱
                new Vector2(430f, 440f),  // 电台桌面
                new Vector2(548f, 462f),  // 桌前地面
                new Vector2(672f, 442f),  // 地图桌
                new Vector2(800f, 458f),  // 右墙发信机下
                new Vector2(360f, 486f),  // 地面散落
                new Vector2(620f, 488f),
                new Vector2(740f, 486f),
                new Vector2(230f, 486f),
                new Vector2(500f, 490f),
            },
            RoomType.BurntWarehouse => new[]
            {
                new Vector2(190f, 460f),  // 残墙下
                new Vector2(320f, 478f),  // 灰烬堆
                new Vector2(470f, 462f),  // 半塌的桌
                new Vector2(620f, 470f),  // 木箱
                new Vector2(760f, 458f),  // 焦梁下
                new Vector2(390f, 490f),
                new Vector2(560f, 490f),
                new Vector2(690f, 490f),
                new Vector2(260f, 490f),
                new Vector2(830f, 486f),
                new Vector2(500f, 490f),
            },
            _ => new[]
            {
                new Vector2(190f, 452f),  // 炕（左）/ 右（variant 1 镜像）
                new Vector2(320f, 470f),  // 屋中地
                new Vector2(455f, 446f),  // 方桌
                new Vector2(590f, 472f),  // 柜子前
                new Vector2(720f, 458f),  // 灶台
                new Vector2(835f, 480f),  // 墙角
                new Vector2(260f, 490f),
                new Vector2(400f, 490f),
                new Vector2(540f, 490f),
                new Vector2(660f, 490f),
                new Vector2(780f, 490f),
            },
        };

        for (int i = 0; i < Items.Count; i++)
        {
            Vector2 a = anchors[i % anchors.Length];
            if (Type == RoomType.Residence && Variant != 0)
                a.X = 952f - a.X;   // 另一间民居左右镜像，格局不完全一样
            a += new Vector2(rng.RandfRange(-14f, 14f), rng.RandfRange(-9f, 9f));
            a.X = Mathf.Clamp(a.X, 118f, 872f);
            // v3.67b：上移到地板上半带，避免被进屋帧底部剧情对白框挡住。
            a.Y = Mathf.Clamp(a.Y - 58f, 392f, 458f);
            Items[i].Pos = a;
        }
    }

    public override void _Ready()
    {
        // 背景层：沿用 v3.65 起的手绘上色漫画风墙 / 地 / 家具
        var bg = HouseInteriorUi.CreateRoomBackground(Type, Variant, Seed);
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        bg.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(bg);
        bg.QueueRedraw();

        // 前景层：门 + 实体物件 + 玩家小人 + 提示牌（画在背景之上）
        _fg = new InteriorForeground(this);
        _fg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_fg);
    }

    /// <summary>由 HouseInteriorUi._Process 驱动：走动、靠近判定、重绘。</summary>
    public void Step(float dt, float dir)
    {
        if (_fg == null) return;
        float W = Size.X > 0f ? Size.X : 952f;
        if (Mathf.Abs(dir) > 0.01f)
        {
            PlayerX = Mathf.Clamp(PlayerX + dir * 205f * dt, 54f, W - 52f);
            _walkT += dt * 9f;
            _facing = dir > 0f ? 1f : -1f;
        }

        // 找最近的、还没搜过的物件；全搜完时仍允许靠近已搜过的（会提示「已翻过」）
        NearIndex = -1;
        float best = 52f;
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].Done) continue;
            float d = Mathf.Abs(Items[i].Pos.X - PlayerX);
            if (d < best) { best = d; NearIndex = i; }
        }
        _fg.WalkPhase = _walkT;
        _fg.Facing = _facing;
        _fg.QueueRedraw();
    }

    /// <summary>搜刮完成后同步状态，避免物件仍亮着「可查看」。</summary>
    public void MarkDone(string spotId, string resultText)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].SpotId == spotId)
            {
                Items[i].Done = true;
                Items[i].Progress = 0f;
                break;
            }
        }
        if (!string.IsNullOrEmpty(resultText)) ResultText = resultText;
        _fg?.QueueRedraw();
    }

    public void SetProgress(string spotId, float t)
    {
        for (int i = 0; i < Items.Count; i++)
            if (Items[i].SpotId == spotId) { Items[i].Progress = t; break; }
        _fg?.QueueRedraw();
    }

    /// <summary>预览场景用：按房间类型造一组有代表性的可调查物件。</summary>
    private static List<RoomItem> PreviewItems(RoomType type, int seed)
    {
        string[] kinds = type == RoomType.CommOutpost
            ? new[] { "power", "chest", "letter", "ammo", "will", "radio", "grenade", "gauze", "clue", "ash", "medkit" }
            : type == RoomType.BurntWarehouse
                ? new[] { "letter", "ash", "chest", "clue", "ammo", "grenade", "gauze", "will", "medkit" }
                : new[] { "letter", "will", "chest", "ammo", "grenade", "gauze", "clue", "ash", "medkit" };
        string[] labels =
        {
            "弹药盒", "压在灶台下的信", "叠得整整齐齐的遗书", "木柄手榴弹", "半卷干净的纱布",
            "军医留下的医药包", "上锁的弹药箱", "烧剩一半的文件", "一摊烧焦的灰烬",
            "墙边的电闸与手摇发电机", "墙角的无线电发信机",
        };
        var list = new List<RoomItem>();
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)(seed & 0xffffff);
        for (int i = 0; i < kinds.Length; i++)
        {
            string k = kinds[i];
            string lb = k switch
            {
                "power" => "墙边的电闸与手摇发电机",
                "radio" => "墙角的无线电发信机",
                "chest" => "上锁的弹药箱",
                "letter" => type == RoomType.CommOutpost ? "桌上的信封"
                          : type == RoomType.BurntWarehouse ? "残墙下压着的信" : "压在灶台下的信",
                "ash" => type == RoomType.BurntWarehouse ? "一摊烧焦的灰烬" : "烧塌的柜子",
                _ => labels[k switch
                {
                    "ammo" => 0, "will" => 2, "grenade" => 3, "gauze" => 4,
                    "medkit" => 5, "clue" => 7, _ => 1,
                }],
            };
            list.Add(new RoomItem
            {
                SpotId = $"preview:{type}:{i}",
                Kind = k,
                Label = lb,
                Done = false,
            });
        }
        return list;
    }
}

/// <summary>
/// 前景绘制层：全部走 InkStyle 的漫画风原语。只画图形，文字交给 Label 子节点，
/// 保证中文用项目默认字体正常显示（不用 DrawString 硬猜字体）。
/// </summary>
public partial class InteriorForeground : Control
{
    private readonly InteriorRoom _room;
    private Label _prompt;
    private Label _result;
    private Label _title;
    private Label _hint;

    public float WalkPhase;
    public float Facing = 1f;

    private static readonly Color Ink = InkStyle.InkColor;
    private static readonly Color Paper = new(0.92f, 0.85f, 0.68f);
    private static readonly Color PaperLit = new(0.99f, 0.94f, 0.80f);
    private static readonly Color PaperDim = new(0.66f, 0.61f, 0.50f);
    private static readonly Color Wood = InkStyle.Wood;
    private static readonly Color WoodShadow = InkStyle.WoodShadow;
    private static readonly Color WoodLit = InkStyle.WoodHighlight;
    private static readonly Color Steel = new(0.46f, 0.48f, 0.50f);
    private static readonly Color SteelShadow = new(0.27f, 0.29f, 0.31f);
    private static readonly Color SteelLit = new(0.80f, 0.82f, 0.82f);
    private static readonly Color Red = new(0.62f, 0.21f, 0.16f);
    private static readonly Color Night = InkStyle.Night;

    public InteriorForeground(InteriorRoom room)
    {
        _room = room;
        MouseFilter = Control.MouseFilterEnum.Ignore;
    }

    public override void _Ready()
    {
        _title = new Label
        {
            // 预览场景传入的标题已含「屋内」，游戏内传入的是屋名；避免叠成「· 屋内 · 屋内」
            Text = _room.Title.Contains("屋内") ? _room.Title : $"{_room.Title} · 屋内",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _title.AddThemeFontSizeOverride("font_size", 24);
        _title.AddThemeColorOverride("font_color", _room.Type == RoomType.BurntWarehouse ? PaperLit : Ink);
        _title.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _title.OffsetTop = 8;
        _title.OffsetBottom = 40;
        AddChild(_title);

        _hint = new Label
        {
            Text = "A / D 或 ← / → 环视屋内　·　准星对准物件按 E 搜寻　·　移到门口按 E 离开",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _hint.AddThemeFontSizeOverride("font_size", 14);
        _hint.AddThemeColorOverride("font_color", new Color(0.98f, 0.91f, 0.75f));
        _hint.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _hint.OffsetTop = -36;
        _hint.OffsetBottom = -8;
        AddChild(_hint);

        _prompt = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _prompt.AddThemeFontSizeOverride("font_size", 15);
        _prompt.AddThemeColorOverride("font_color", Ink);
        AddChild(_prompt);

        _result = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Left,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _result.AddThemeFontSizeOverride("font_size", 15);
        _result.AddThemeColorOverride("font_color", Ink);
        AddChild(_result);
    }

    public override void _Draw()
    {
        float W = Size.X, H = Size.Y;
        if (W < 10f || H < 10f) return;

        DrawDoor(W, H);

        // 物件：先全部画，再画小人，小人站最前
        for (int i = 0; i < _room.Items.Count; i++)
            DrawItem(_room.Items[i], i == _room.NearIndex);

        // v3.70：进入室内自动切换第一视角 —— 隐藏走动小人，改画「准星扫视」
        if (_room.FirstPerson) DrawReticle(W, H);
        else DrawPlayer(W, H);

        // 靠近物件 / 门口时的漫画风小提示牌（米黄土实色 + 3px 深墨棕粗框 + 硬投影）
        RefreshPrompt(W, H);
        RefreshResult(W, H);
    }

    private void DrawDoor(float W, float H)
    {
        float floor = H * 0.635f;
        float dx = 12f, dw = 68f, dh = H * 0.60f;
        float dy = floor + 16f - dh;

        // 门外是秋夜 / 雪夜：深蓝实色，不做渐变
        Box(new Rect2(dx + 5f, dy + 6f, dw - 10f, dh - 6f), Night, Ink, 3f);
        // 门板：木板三档平涂
        Box(new Rect2(dx + 11f, dy + 14f, dw - 22f, dh - 20f), Wood, Ink, 3f);
        for (float y = dy + 34f; y < dy + dh - 20f; y += 30f)
            Line(new Vector2(dx + 14f, y), new Vector2(dx + dw - 14f, y), 1.6f, WoodShadow);
        // 门框粗墨线
        Line(new Vector2(dx, dy), new Vector2(dx, dy + dh), 4f, Ink);
        Line(new Vector2(dx + dw, dy), new Vector2(dx + dw, dy + dh), 4f, Ink);
        Line(new Vector2(dx, dy), new Vector2(dx + dw, dy), 4f, Ink);
        // 门把：黄铜小圆点
        Dot(new Vector2(dx + dw - 18f, dy + dh * 0.52f), 5f, new Color(0.78f, 0.63f, 0.30f));
        // 卡通高光：短直线，不是模糊光晕
        Line(new Vector2(dx + 16f, dy + 24f), new Vector2(dx + 30f, dy + 24f), 2.4f, WoodLit);

        // 门口地面箭头（硬边三角），提示「往这边走出去」
        if (_room.AtDoor)
        {
            DrawColoredPolygon(new[]
            {
                new Vector2(30f, floor + 74f),
                new Vector2(52f, floor + 66f),
                new Vector2(30f, floor + 58f),
            }, PaperLit);
        }
    }

    /// <summary>接触阴影：硬边压扁椭圆（手绘，带轻微抖动），不用柔边羽化。</summary>
    private void DrawContactShadow(Vector2 p, float a)
    {
        DrawColoredPolygon(new[]
        {
            new Vector2(p.X - 26f, p.Y + 20f),
            new Vector2(p.X - 12f, p.Y + 15f),
            new Vector2(p.X + 12f, p.Y + 15f),
            new Vector2(p.X + 26f, p.Y + 20f),
            new Vector2(p.X + 18f, p.Y + 25f),
            new Vector2(p.X - 18f, p.Y + 25f),
        }, new Color(0.10f, 0.08f, 0.06f, 0.42f * a));
    }

    private void DrawItem(RoomItem it, bool near)
    {
        Vector2 p = it.Pos;
        float a = it.Done ? 0.55f : 1f;
        float seed = StrHash(it.SpotId);

        DrawContactShadow(p, a);

        // 物件本体（v3.70：手绘漫画风，抖动墨线 + 三档平涂 + 内部细节，不再用方块图标）
        switch (it.Kind)
        {
            case "chest":   DrawChest(p, a, seed); break;
            case "ammo":    DrawAmmoBox(p, a, seed); break;
            case "grenade": DrawGrenade(p, a, seed); break;
            case "letter":
            case "will":
            case "clue":    DrawEnvelope(p, a, seed, it.Kind == "clue"); break;
            case "gauze":   DrawGauze(p, a, seed); break;
            case "medkit":  DrawMedkit(p, a, seed); break;
            case "ash":     DrawAsh(p, a, seed); break;
            case "power":   DrawPowerBox(p, a, seed); break;
            case "radio":   DrawRadio(p, a, seed); break;
            default:        DrawAmmoBox(p, a, seed); break;
        }

        // 未搜过：物件上方一颗卡通高光十字星（不是文字标签）
        if (!it.Done)
        {
            float t = (float)(Time.GetTicksMsec() % 1400) / 1400f;
            float s = 4f + Mathf.Sin(t * Mathf.Tau) * 1.4f;
            Line(new Vector2(p.X + 22f, p.Y - 26f - s), new Vector2(p.X + 22f, p.Y - 26f + s), 2f, PaperLit);
            Line(new Vector2(p.X + 22f - s, p.Y - 26f), new Vector2(p.X + 22f + s, p.Y - 26f), 2f, PaperLit);
        }
        else
        {
            // 已翻过：硬边斜杠 + 变暗，不留可点击的标签
            Line(new Vector2(p.X - 18f, p.Y - 18f), new Vector2(p.X + 18f, p.Y + 18f), 2.4f,
                new Color(0.30f, 0.24f, 0.18f, a));
        }

        // 靠近：四角漫画分格标记（不画整圈红框，免得压住物件与小人）
        if (near)
        {
            float l = p.X - 30f, r = p.X + 30f, t = p.Y - 34f, b = p.Y + 26f;
            float[] xs = { l, r, l, r }, ys = { t, t, b, b }, sx = { 1f, -1f, 1f, -1f }, sy = { 1f, 1f, -1f, -1f };
            for (int k = 0; k < 4; k++)
            {
                Line(new Vector2(xs[k], ys[k]), new Vector2(xs[k] + 14f * sx[k], ys[k]), 3f, PaperLit);
                Line(new Vector2(xs[k], ys[k]), new Vector2(xs[k], ys[k] + 14f * sy[k]), 3f, PaperLit);
            }
        }

        // 长按进度（撬箱 / 合闸 / 发报）：硬边实色条
        if (it.Progress > 0f)
        {
            Box(new Rect2(p.X - 32f, p.Y - 48f, 64f, 9f), PaperDim, Ink, 2f);
            DrawRect(new Rect2(p.X - 30f, p.Y - 46f, 60f * Mathf.Clamp(it.Progress, 0f, 1f), 5f), Red);
        }
    }

    // ---------- 各类实体物件（v3.70：手绘漫画风 —— 抖动墨线 + 三档平涂 + 内部细节） ----------
    private void DrawChest(Vector2 p, float a, float seed)
    {
        float w = 50f, h = 36f;
        var r = new Rect2(p.X - w / 2f, p.Y - h / 2f, w, h);
        HandRect(r, C(Wood, a), Ink, 3f, seed, C(WoodShadow, a), C(WoodLit, a));
        // 木纹横缝
        for (float y = r.Position.Y + 9f; y < r.Position.Y + h - 5f; y += 9f)
        {
            float j = (Jit(seed + y) - 0.5f) * 1.6f;
            Line(new Vector2(r.Position.X + 4f, y + j),
                 new Vector2(r.Position.X + w - 4f, y + j + (Jit(seed + y + 1) - 0.5f)), 1.4f, C(WoodShadow, a));
        }
        // 斜撑
        Line(new Vector2(r.Position.X + 4f, r.Position.Y + 4f), new Vector2(r.Position.X + w - 4f, r.Position.Y + h - 4f), 1.2f, C(InkStyle.InkSoft, a));
        Line(new Vector2(r.Position.X + w - 4f, r.Position.Y + 4f), new Vector2(r.Position.X + 4f, r.Position.Y + h - 4f), 1.2f, C(InkStyle.InkSoft, a));
        // 金属包角
        foreach (var c in new[] { r.Position, new Vector2(r.Position.X + w, r.Position.Y), new Vector2(r.Position.X + w, r.Position.Y + h), new Vector2(r.Position.X, r.Position.Y + h) })
            Dot(c, 2.4f, C(SteelLit, a));
        // 锁扣
        Box(new Rect2(p.X - 7f, p.Y - 4f, 14f, 12f), C(new Color(0.78f, 0.63f, 0.30f), a), Ink, 2f);
        Dot(new Vector2(p.X, p.Y + 1f), 2f, Ink);
    }

    private void DrawAmmoBox(Vector2 p, float a, float seed)
    {
        float w = 38f, h = 26f;
        var r = new Rect2(p.X - w / 2f, p.Y - h / 2f, w, h);
        HandRect(r, C(Wood, a), Ink, 3f, seed, C(WoodShadow, a), C(WoodLit, a));
        // 木纹横缝
        Line(new Vector2(r.Position.X + 4f, r.Position.Y + 7f), new Vector2(r.Position.X + w - 4f, r.Position.Y + 7f), 1.2f, C(WoodLit, a));
        // 露出的子弹尖（铜色）
        for (int i = -1; i <= 1; i++)
        {
            float x = p.X + i * 9f;
            Line(new Vector2(x, r.Position.Y + 4f), new Vector2(x, r.Position.Y + h - 4f), 1.6f, C(SteelLit, a));
            DrawColoredPolygon(new[]
            {
                new Vector2(x - 2.4f, r.Position.Y - 1f), new Vector2(x + 2.4f, r.Position.Y - 1f),
                new Vector2(x, r.Position.Y - 8f),
            }, C(SteelLit, a));
        }
        // 金属包边铆钉
        Dot(new Vector2(r.Position.X + 5f, r.Position.Y + 5f), 1.8f, C(Steel, a));
        Dot(new Vector2(r.Position.X + w - 5f, r.Position.Y + 5f), 1.8f, C(Steel, a));
    }

    private void DrawGrenade(Vector2 p, float a, float seed)
    {
        // 横躺的木柄手榴弹（手绘漫画风，木柄有纹理、钢头有凹槽、顶端引信）
        var handle = new Rect2(p.X - 27f, p.Y - 5f, 30f, 10f);
        HandRect(handle, C(Wood, a), Ink, 3f, seed, C(WoodShadow, a), C(WoodLit, a));
        for (float y = handle.Position.Y + 2f; y < handle.Position.Y + handle.Size.Y; y += 3f)
            Line(new Vector2(handle.Position.X + 3f, y), new Vector2(handle.Position.X + handle.Size.X - 3f, y), 0.8f, C(WoodShadow, a));
        var head = new Rect2(p.X + 3f, p.Y - 9f, 21f, 18f);
        HandRect(head, C(Steel, a), Ink, 3f, seed + 50f, C(SteelShadow, a), C(SteelLit, a));
        for (int i = 0; i < 3; i++)
            Line(new Vector2(head.Position.X + 4f + i * 5f, head.Position.Y + 3f),
                 new Vector2(head.Position.X + 4f + i * 5f, head.Position.Y + head.Size.Y - 3f), 1.2f, C(SteelShadow, a));
        // 引信
        Line(new Vector2(head.Position.X + head.Size.X - 2f, head.Position.Y + 2f), new Vector2(head.Position.X + head.Size.X + 6f, head.Position.Y - 4f), 2f, Ink);
        Dot(new Vector2(p.X - 22f, p.Y - 1f), 2.2f, C(WoodLit, a));
    }

    private void DrawEnvelope(Vector2 p, float a, float seed, bool burnt)
    {
        float w = 42f, h = 26f;
        var r = new Rect2(p.X - w / 2f, p.Y - h / 2f, w, h);
        Color paper = burnt ? new Color(0.72f, 0.62f, 0.48f) : Paper;
        HandRect(r, C(paper, a), Ink, 3f, seed,
            C(burnt ? new Color(0.50f, 0.42f, 0.32f) : PaperDim, a), C(PaperLit, a));
        // 信封折线（汇聚到中心的 V）
        Line(new Vector2(r.Position.X, r.Position.Y), new Vector2(p.X, p.Y + 3f), 1.6f, Ink);
        Line(new Vector2(r.Position.X + w, r.Position.Y), new Vector2(p.X, p.Y + 3f), 1.6f, Ink);
        Line(new Vector2(r.Position.X, r.Position.Y + h), new Vector2(p.X, p.Y + 3f), 1.6f, Ink);
        Line(new Vector2(r.Position.X + w, r.Position.Y + h), new Vector2(p.X, p.Y + 3f), 1.6f, Ink);
        if (burnt)
        {
            // 烧焦缺角 + 余烬
            DrawColoredPolygon(new[]
            {
                new Vector2(r.Position.X + w - 12f, r.Position.Y + h),
                new Vector2(r.Position.X + w, r.Position.Y + h),
                new Vector2(r.Position.X + w, r.Position.Y + h - 14f),
                new Vector2(r.Position.X + w - 12f, r.Position.Y + h - 4f),
            }, C(new Color(0.26f, 0.20f, 0.16f), a));
            Dot(new Vector2(p.X + 10f, p.Y + 2f), 2f, C(new Color(0.72f, 0.40f, 0.24f), a));
        }
        else
        {
            // 红蜡封
            Dot(new Vector2(p.X, p.Y + 2f), 3.2f, C(Red, a));
            Dot(new Vector2(p.X, p.Y + 2f), 1.4f, C(new Color(0.40f, 0.10f, 0.08f), a));
        }
    }

    private void DrawGauze(Vector2 p, float a, float seed)
    {
        // 卷起的纱布：椭圆筒 + 卷纹 + 红十字
        Disk(new Vector2(p.X, p.Y), 15f, Ink, C(new Color(0.94f, 0.92f, 0.86f), a), 3f);
        DrawArc(new Vector2(p.X, p.Y), 9f, 0.4f, Mathf.Tau - 0.4f, 18, C(new Color(0.78f, 0.76f, 0.72f), a), 1.4f, true);
        DrawRect(new Rect2(p.X - 2.5f, p.Y - 7f, 5f, 14f), C(Red, a));
        DrawRect(new Rect2(p.X - 7f, p.Y - 2.5f, 14f, 5f), C(Red, a));
        Line(new Vector2(p.X - 11f, p.Y - 10f), new Vector2(p.X - 5f, p.Y - 13f), 2f, C(new Color(1f, 1f, 1f), a));
    }

    private void DrawMedkit(Vector2 p, float a, float seed)
    {
        float w = 38f, h = 30f;
        var r = new Rect2(p.X - w / 2f, p.Y - h / 2f, w, h);
        HandRect(r, C(new Color(0.90f, 0.88f, 0.80f), a), Ink, 3f, seed,
            C(new Color(0.66f, 0.64f, 0.58f), a), C(new Color(0.99f, 0.98f, 0.92f), a));
        // 红十字
        DrawRect(new Rect2(p.X - 3f, p.Y - 9f, 6f, 18f), C(Red, a));
        DrawRect(new Rect2(p.X - 9f, p.Y - 3f, 18f, 6f), C(Red, a));
        // 卡扣
        Dot(new Vector2(r.Position.X + w / 2f, r.Position.Y + 2f), 2f, Ink);
        Dot(new Vector2(r.Position.X + w / 2f, r.Position.Y + h - 2f), 2f, Ink);
    }

    private void DrawAsh(Vector2 p, float a, float seed)
    {
        var grey = new Color(0.38f, 0.36f, 0.34f);
        // 不规则灰堆（手绘多边形，非对称）
        DrawColoredPolygon(new[]
        {
            new Vector2(p.X - 24f, p.Y + 12f),
            new Vector2(p.X - 15f, p.Y - 7f + (Jit(seed) - 0.5f) * 4f),
            new Vector2(p.X - 3f, p.Y - 13f),
            new Vector2(p.X + 13f, p.Y - 5f + (Jit(seed + 1) - 0.5f) * 4f),
            new Vector2(p.X + 25f, p.Y + 12f),
        }, C(grey, a));
        DrawColoredPolygon(new[]
        {
            new Vector2(p.X - 18f, p.Y + 10f),
            new Vector2(p.X - 8f, p.Y - 2f),
            new Vector2(p.X + 4f, p.Y + 4f),
            new Vector2(p.X + 14f, p.Y + 10f),
        }, C(new Color(0.26f, 0.24f, 0.22f), a));
        // 焦黑木炭 + 余烬
        Line(new Vector2(p.X - 10f, p.Y - 2f), new Vector2(p.X - 4f, p.Y - 7f), 2f, C(new Color(0.62f, 0.60f, 0.56f), a));
        Dot(new Vector2(p.X + 8f, p.Y + 1f), 2.4f, C(new Color(0.72f, 0.40f, 0.24f), a));
        Dot(new Vector2(p.X + 2f, p.Y - 4f), 1.8f, C(new Color(0.62f, 0.30f, 0.18f), a));
    }

    private void DrawPowerBox(Vector2 p, float a, float seed)
    {
        float w = 46f, h = 38f;
        var r = new Rect2(p.X - w / 2f, p.Y - h / 2f, w, h);
        HandRect(r, C(Steel, a), Ink, 3f, seed, C(SteelShadow, a), C(SteelLit, a));
        // 瓷瓶保险
        for (int i = -1; i <= 1; i++)
        {
            float x = p.X + i * 12f;
            Dot(new Vector2(x, p.Y - 12f), 4f, C(new Color(0.88f, 0.78f, 0.52f), a));
            Line(new Vector2(x, p.Y - 9f), new Vector2(x + i * 5f, p.Y + 3f), 2f, C(new Color(0.45f, 0.40f, 0.32f), a));
        }
        // 刀闸把手
        Line(new Vector2(p.X - 9f, p.Y + 3f), new Vector2(p.X + 7f, p.Y - 7f), 3f, Ink);
        Dot(new Vector2(p.X + 9f, p.Y + 1f), 3f, C(Red, a));
        // 导线
        Line(new Vector2(r.Position.X + 5f, r.Position.Y + h - 4f), new Vector2(r.Position.X + w - 5f, r.Position.Y + h - 4f), 1.4f, Ink);
    }

    private void DrawRadio(Vector2 p, float a, float seed)
    {
        float w = 50f, h = 34f;
        var r = new Rect2(p.X - w / 2f, p.Y - h / 2f, w, h);
        HandRect(r, C(Steel, a), Ink, 3f, seed, C(SteelShadow, a), C(SteelLit, a));
        // 表盘
        Disk(new Vector2(p.X - 14f, p.Y - 4f), 9f, Ink, C(new Color(0.86f, 0.84f, 0.72f), a), 2.5f);
        Line(new Vector2(p.X - 14f, p.Y - 4f), new Vector2(p.X - 10f, p.Y - 10f), 1.8f, C(Red, a));
        // 旋钮
        for (int i = 0; i < 2; i++)
        {
            float x = p.X + 6f + i * 11f;
            Dot(new Vector2(x, p.Y - 4f), 3.4f, C(new Color(0.30f, 0.28f, 0.24f), a));
            Dot(new Vector2(x, p.Y - 4f), 1.6f, C(new Color(0.62f, 0.56f, 0.44f), a));
        }
        // 散热栅
        for (int i = 0; i < 4; i++)
            Line(new Vector2(r.Position.X + 2f + i * 11f, r.Position.Y + h - 6f), new Vector2(r.Position.X + 2f + i * 11f, r.Position.Y + h - 1f), 1.4f, Ink);
    }

    // ---------- 玩家小人：粗墨边平涂，走路摆腿 ----------
    private void DrawPlayer(float W, float H)
    {
        float x = _room.PlayerX;
        float foot = H * 0.745f;
        float swing = Mathf.Sin(WalkPhase) * 7f;
        float f = Facing;

        // 接触阴影
        DrawColoredPolygon(new[]
        {
            new Vector2(x - 18f, foot + 2f), new Vector2(x - 8f, foot - 2f),
            new Vector2(x + 8f, foot - 2f), new Vector2(x + 18f, foot + 2f),
            new Vector2(x + 10f, foot + 6f), new Vector2(x - 10f, foot + 6f),
        }, new Color(0.10f, 0.08f, 0.06f, 0.45f));

        // 亮土黄外套：在灰蓝地板上必须一眼能认出「这是我」
        Color coat = new Color(0.90f, 0.82f, 0.62f);
        Color coatShade = new Color(0.62f, 0.52f, 0.34f);
        Color cap = new Color(0.34f, 0.40f, 0.48f);
        Color capShade = new Color(0.22f, 0.27f, 0.34f);

        // 腿（走路摆动）
        Line(new Vector2(x - 5f, foot - 30f), new Vector2(x - 7f + swing, foot), 7f, Ink);
        Line(new Vector2(x + 5f, foot - 30f), new Vector2(x + 7f - swing, foot), 7f, Ink);
        Line(new Vector2(x - 5f, foot - 30f), new Vector2(x - 7f + swing, foot), 4f, new Color(0.30f, 0.27f, 0.24f));
        Line(new Vector2(x + 5f, foot - 30f), new Vector2(x + 7f - swing, foot), 4f, new Color(0.30f, 0.27f, 0.24f));

        // 身体：平涂 + 下缘硬边暗部 + 3px 粗墨框
        Box(new Rect2(x - 13f, foot - 68f, 26f, 40f), coat, Ink, 3f);
        DrawRect(new Rect2(x - 13f, foot - 40f, 26f, 12f), coatShade);
        Line(new Vector2(x + 2f * f, foot - 62f), new Vector2(x + 2f * f, foot - 36f), 2f, Ink);
        DrawColoredPolygon(new[]
        {
            new Vector2(x - 7f, foot - 68f), new Vector2(x + 7f, foot - 68f),
            new Vector2(x, foot - 56f),
        }, Red);
        // 手臂
        Line(new Vector2(x + 11f * f, foot - 62f), new Vector2(x + 16f * f, foot - 42f), 6f, Ink);
        Line(new Vector2(x + 11f * f, foot - 62f), new Vector2(x + 16f * f, foot - 42f), 3.4f, coat);

        // 头 + 帽
        Disk(new Vector2(x, foot - 80f), 11f, Ink, InkStyle.Skin, 3f);
        DrawRect(new Rect2(x - 12f, foot - 92f, 24f, 8f), cap);
        DrawRect(new Rect2(x - 12f, foot - 92f, 24f, 8f), Ink, false, 3f);
        DrawRect(new Rect2(x - 14f * f, foot - 86f, 24f, 4f), capShade);
        // 卡通高光：小圆点 + 短直线
        Dot(new Vector2(x - 4f, foot - 82f), 2.2f, InkStyle.SkinHighlight);
        Line(new Vector2(x - 10f, foot - 90f), new Vector2(x - 3f, foot - 90f), 2f, PaperLit);
    }

    /// <summary>
    /// v3.70 第一视角准星：A/D 移动的水平扫视线 + 手绘漫画风十字准星（墨圈 + 四向刻线 +
    /// 高光点），不画 UI 圆角框。靠近物件时准星下落到该物件高度，强调「我在盯著它看」。
    /// </summary>
    private void DrawReticle(float W, float H)
    {
        float x = _room.PlayerX;
        // 靠近物件时准星落到该物件高度，否则停在房间中上部扫视
        float y = H * 0.50f;
        if (_room.NearIndex >= 0 && _room.NearIndex < _room.Items.Count)
            y = _room.Items[_room.NearIndex].Pos.Y;

        // 竖直扫视引导线（淡）
        Line(new Vector2(x, H * 0.10f), new Vector2(x, H * 0.92f), 1.4f, new Color(0.99f, 0.94f, 0.80f, 0.20f));

        // 准星：墨圈 + 内圈淡光 + 四向刻线 + 高光点
        float r = 13f;
        DrawArc(new Vector2(x, y), r, 0, Mathf.Tau, 28, Ink, 3f, true);
        DrawCircle(new Vector2(x, y), r - 3f, new Color(0.99f, 0.94f, 0.80f, 0.10f));
        foreach (var d in new[] { new Vector2(0, -1), new Vector2(0, 1), new Vector2(-1, 0), new Vector2(1, 0) })
            Line(new Vector2(x + d.X * (r + 3f), y + d.Y * (r + 3f)),
                 new Vector2(x + d.X * (r + 11f), y + d.Y * (r + 11f)), 2.4f, Ink);
        Dot(new Vector2(x - 3f, y - 3f), 2.2f, new Color(0.99f, 0.94f, 0.80f));
        Dot(new Vector2(x, y), 1.8f, Red);
    }

    // ---------- 提示牌 / 结果条 ----------
    private void RefreshPrompt(float W, float H)
    {
        string text;
        Vector2 pos;
        if (_room.AtDoor)
        {
            text = "E · 离开屋子";
            pos = new Vector2(96f, H * 0.635f + 24f);
        }
        else if (_room.NearIndex >= 0 && _room.NearIndex < _room.Items.Count)
        {
            text = $"E · 查看「{_room.Items[_room.NearIndex].Label}」";
            var it = _room.Items[_room.NearIndex];
            pos = new Vector2(Mathf.Clamp(it.Pos.X - 110f, 90f, W - 240f), it.Pos.Y - 92f);
        }
        else
        {
            _prompt.Text = "";
            return;
        }

        float pw = Mathf.Clamp(text.Length * 15f + 30f, 150f, 280f);
        // v3.68：米黄矩形 UI 标签牌 -> 一张撕边 + 局部焦角的纸条
        PaperSlip.Draw(this, new Rect2(pos.X, pos.Y, pw, 32f),
            BurntPaperArt.StableHash(text) + _room.Seed);
        _prompt.Text = text;
        _prompt.Position = new Vector2(pos.X + 8f, pos.Y + 5f);
        _prompt.Size = new Vector2(pw - 16f, 24f);
        _prompt.HorizontalAlignment = HorizontalAlignment.Center;
    }

    private void RefreshResult(float W, float H)
    {
        if (string.IsNullOrEmpty(_room.ResultText))
        {
            _result.Text = "";
            return;
        }
        float y = H - 112f;
        float x = 96f, w = W - 192f;
        // v3.68：结果条同样换成纸条，不再是对话格子
        PaperSlip.Draw(this, new Rect2(x, y, w, 56f),
            BurntPaperArt.StableHash(_room.ResultText) + _room.Seed);
        _result.Text = _room.ResultText;
        _result.Position = new Vector2(x + 18f, y + 9f);
        _result.Size = new Vector2(w - 36f, 40f);
    }

    // ---------- 漫画风绘制原语 ----------
    private static Color C(Color c, float a) => new(c.R, c.G, c.B, c.A * a);

    private void Box(Rect2 r, Color fill, Color outline, float bw)
    {
        DrawRect(r, fill);
        DrawRect(r, outline, false, bw);
    }

    private void Disk(Vector2 c, float radius, Color outline, Color fill, float bw)
    {
        DrawCircle(c, radius, outline);
        DrawCircle(c, Mathf.Max(1f, radius - bw), fill);
    }

    private void Dot(Vector2 c, float radius, Color col) => DrawCircle(c, radius, col);

    private void Line(Vector2 a, Vector2 b, float w, Color col) => DrawLine(a, b, col, w, false);

    /// <summary>确定性字符串散列 → 0..1000 浮点，保证同一物件每次绘制一致（不闪）。</summary>
    private static float StrHash(string s)
    {
        float h = 0f;
        for (int i = 0; i < s.Length; i++) h = (h * 31f + s[i]) % 1000f;
        return h;
    }

    /// <summary>确定性抖动 0..1（手绘勾线感，不依赖随机，画面稳定）。</summary>
    private static float Jit(float n) => Mathf.Abs(Mathf.Sin(n * 12.9898f + 7.13f)) % 1f;

    /// <summary>手绘多边形：先粗描边再平涂（自动闭合）。</summary>
    private void InkPoly(Vector2[] poly, Color fill, Color outline, float bw)
    {
        if (poly == null || poly.Length < 3) return;
        DrawColoredPolygon(poly, fill);
        var closed = new Vector2[poly.Length + 1];
        Array.Copy(poly, closed, poly.Length);
        closed[poly.Length] = poly[0];
        DrawPolyline(closed, outline, bw, true);
    }

    /// <summary>
    /// 手绘矩形：抖动墨线轮廓（四角 + 每边中点轻微摆动）+ 三档平涂
    /// （base / 下缘暗部 / 上缘高光）。不再用完美直角矩形，读起来像手绘上色漫画。
    /// </summary>
    private void HandRect(Rect2 r, Color fill, Color outline, float bw, float seed, Color? shade = null, Color? lit = null)
    {
        var corners = new[]
        {
            r.Position,
            new Vector2(r.Position.X + r.Size.X, r.Position.Y),
            new Vector2(r.Position.X + r.Size.X, r.Position.Y + r.Size.Y),
            new Vector2(r.Position.X, r.Position.Y + r.Size.Y),
        };
        var poly = new Vector2[8];
        for (int i = 0; i < 4; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % 4];
            poly[i * 2] = a + new Vector2((Jit(seed + i * 2) - 0.5f) * 2f, (Jit(seed + i * 2 + 1) - 0.5f) * 2f);
            var mid = (a + b) * 0.5f;
            poly[i * 2 + 1] = mid + new Vector2((Jit(seed + 20 + i * 2) - 0.5f) * 3.4f, (Jit(seed + 20 + i * 2 + 1) - 0.5f) * 3.4f);
        }
        InkPoly(poly, fill, outline, bw);
        if (shade.HasValue)
        {
            float sh = Mathf.Clamp(r.Size.Y * 0.30f, 3f, 14f);
            DrawRect(new Rect2(r.Position.X + 1f, r.Position.Y + r.Size.Y - sh, r.Size.X - 2f, sh), shade.Value);
        }
        if (lit.HasValue)
        {
            float hl = Mathf.Clamp(r.Size.Y * 0.14f, 2f, 8f);
            DrawRect(new Rect2(r.Position.X + 1f, r.Position.Y + 1f, r.Size.X - 2f, hl), lit.Value);
        }
    }
}
