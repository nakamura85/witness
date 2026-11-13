using System;
using System.Collections.Generic;
using Godot;

namespace Witness;

/// <summary>
/// v3.67 可探索屋内的一件实体物件（纯数据，不继承任何 UI 节点）。
/// 旧版是 Spot : Button 的「文字标签」；现在改成房间里画出来的实物，
/// 玩家小人走过去按 E 才查看。label 只用于靠近时的小提示牌。
///
/// v3.71 新增：陈列模式（Surface 台面可见 / Container 藏家具内 / FloorDrop 实时掉落）、
/// 关键物微光标记、掉落物理态。
/// </summary>
public enum RoomItemMode { Surface, Container, FloorDrop }

public sealed class RoomItem
{
    public string SpotId;
    public string Kind;      // ammo / grenade / letter / will / clue / gauze / medkit / chest / ash / power / radio
    public string Label;
    public bool Done;
    public Vector2 Pos;      // 房间画布坐标（物件中心）
    public float Progress;   // 0..1，撬箱 / 合闸 / 发报的长按进度

    // v3.71：陈列模式
    public RoomItemMode Mode = RoomItemMode.Surface;
    public string ContainerId;   // Container 模式：指向藏匿它的家具 Id
    public bool Key;             // 关键物：未搜前画一抹不刺眼的微光（仅真实烈士姓名信/遗书）
    // v3.71：掉落物理态（仅 FloorDrop 用）
    public Vector2 Vel = Vector2.Zero;
    public bool Settled;
    public float DropDelay;      // 入场后延迟多少秒才开始下落（错落感）
}

/// <summary>v3.71 室内家具：提供纵深与藏匿点。物件可摆在台面(Surface)、藏入内部(Container)、或掉在地上(FloorDrop)。</summary>
public enum FurnitureType { Desk, Cabinet, Stove, PictureFrame }

public sealed class Furniture
{
    public string Id;
    public FurnitureType Type;
    public Vector2 Center;          // 房间画布坐标（家具中心）
    public string ContainsSpotId;   // 藏匿的物件 SpotId（Container 模式）；无则 null
    public float OpenT;             // 0 关 .. 1 开（开启动画）
    public bool Opened;             // 已掀开（锁存）
    public int Seed;

    public float HalfW() => Type switch
    {
        FurnitureType.Desk => 66f,
        FurnitureType.Cabinet => 34f,
        FurnitureType.Stove => 40f,
        FurnitureType.PictureFrame => 44f,
        _ => 40f,
    };
    /// <summary>台面 y（Surface 物件摆放高度）。</summary>
    public float TopY() => Type switch
    {
        FurnitureType.PictureFrame => Center.Y,          // 挂画：凹洞在画后
        FurnitureType.Desk => Center.Y - 30f,
        FurnitureType.Cabinet => Center.Y - 44f,
        FurnitureType.Stove => Center.Y - 18f,
        _ => Center.Y - 30f,
    };
    /// <summary>藏匿物在家具开启后露出的凹洞位置。</summary>
    public Vector2 RecessPos() => Type switch
    {
        FurnitureType.Desk => Center + new Vector2(0f, 10f),
        FurnitureType.Cabinet => Center + new Vector2(0f, 8f),
        FurnitureType.Stove => Center + new Vector2(0f, 8f),
        FurnitureType.PictureFrame => Center,
        _ => Center,
    };
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
        // v3.81 回退（用户 2026-11-13 纠正「谁跟你说要程序化」）：
        // 三张室内贴图 residence / comm_outpost / burnt_warehouse 本来就是同一支笔的
        // 钢笔淡彩·水墨手绘渲染，与用户 2026-11-12 两张参考图、v3.78 基线房屋贴图同源。
        // v3.80 误把 residence 换成程序化 WashPaint，反而制造了画风不一致 —— 全部恢复加载；
        // 程序化房间只保留为「贴图未导入」时的兜底，不再当正常渲染路径。
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
    /// <summary>v3.71 家具清单（提供纵深与藏匿点）。</summary>
    public readonly List<Furniture> Furniture = new();
    /// <summary>v3.71 全局时辰（进屋时从 WorldClock 读取，驱动窗光）。</summary>
    public WorldClock.Phase WorldTime = WorldClock.Phase.Day;
    /// <summary>v3.71 地板静止线（房间画布 y），掉落物最终落在此。v3.82 焦黑柜/灶底边仍用此线。</summary>
    public const float FloorY = 506f;
    /// <summary>
    /// v3.84 完好房间的「真实地板线」：由背景贴图换算而来。
    /// residence / comm_outpost 的地板线在贴图 y≈710 / 690，乘 0.765 ⇒ 543 / 528。
    /// 旧版把 Surface 物件一律放在 y≈492，比地板线高 40~50px ⇒ 用户说的「浮在墙上 / 摆的地方不对」。
    /// 仓库不动（v3.82 已按 FloorY 验收通过），避免破坏已确认成果。
    /// </summary>
    public const float GroundY = 536f;

    /// <summary>玩家小人在房间画布上的 x（脚底落在地板行走带）。</summary>
    public float PlayerX = 132f;
    public int NearIndex = -1;
    /// <summary>v3.71 最近的可翻找家具（关闭且含藏匿物）。</summary>
    public int NearFurniture = -1;
    public bool AtDoor => PlayerX <= 82f;
    /// <summary>v3.70：进入室内自动切换第一视角（隐藏走动小人，改画准星扫视）。默认开启。</summary>
    public bool FirstPerson = true;

    private InteriorForeground _fg;
    private float _walkT;
    private float _facing = 1f;

    /// <summary>
    /// v3.84：真实游戏与截图预览共用同一套「家具 + 藏匿分配表」。
    ///
    /// 【修掉的隐藏 bug】旧版这里先 BuildContents 造一批示例物件（竹箩 / 木箱 / 焦砖），
    /// 再把真实 loot 全部 Mode=Surface 追加到 Items 尾部；而 EnterableHouse._Process 用
    /// NearIndex 直接索引 _spots（真实 loot 数组）⇒ 两个数组长度与顺序都不一致，
    /// 准星对准示例竹箩按 E，搜到的却是 _spots 里不相干的一项；真实 loot 也永远没有藏匿玩法。
    /// 现在 Items 严格等于传入的 items（一一对应），藏匿由 AssignPlacement 在原地分派。
    /// items == null 时按同一分配表造预览示例，保证「截图看到的 = 游戏里玩到的」。
    /// </summary>
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
        r.BuildFurniture(type, variant, seed);
        if (items != null)
            foreach (var it in items) r.Items.Add(it);
        else
            r.BuildPreviewItems(type, seed);
        r.AssignPlacement();
        r.Layout();
        return r;
    }

    /// <summary>
    /// v3.71：Surface/Container 位置已在 BuildContents 定好；此处仅把 FloorDrop 复位到
    /// 抬高起点，交由 PhysicsStep 实时下落。Container 物件保持凹洞位，Surface 保持台面/地面位。
    /// </summary>
    public void Layout()
    {
        foreach (var it in Items)
        {
            if (it.Mode == RoomItemMode.FloorDrop)
            {
                float x = it.Pos.X;
                it.Pos = new Vector2(x, FloorY - 150f);
                it.Vel = Vector2.Zero;
                it.Settled = false;
            }
        }
    }

    public override void _Ready()
    {
        // v3.71：进屋时读取关卡全局时辰，驱动窗光 / 窗外晕染 / 房间光晕
        WorldTime = WorldClock.Time;

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

    /// <summary>由 HouseInteriorUi._Process 驱动：走动、靠近判定、掉落物理、重绘。</summary>
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

        // 家具开启动画（翻开后抽屉滑出 / 门摆开）
        foreach (var f in Furniture)
            if (f.Opened && f.OpenT < 1f) f.OpenT = Mathf.Min(1f, f.OpenT + dt * 3.2f);

        // 掉落物理（实时下落 / 回弹 / 静止）
        PhysicsStep(dt);

        // 最近可搜查物件：Container 物件需家具已开才能搜；FloorDrop 需已静止；已 Done 跳过
        NearIndex = -1;
        float best = 56f;
        for (int i = 0; i < Items.Count; i++)
        {
            var it = Items[i];
            if (it.Done) continue;
            if (it.Mode == RoomItemMode.Container)
            {
                var f = Furniture.Find(x => x.Id == it.ContainerId);
                if (f == null || !f.Opened) continue;
            }
            if (it.Mode == RoomItemMode.FloorDrop && !it.Settled) continue;
            float d = Mathf.Abs(it.Pos.X - PlayerX);
            if (d < best) { best = d; NearIndex = i; }
        }

        // v3.84 最近可翻找家具：必须「未开 + 里面还有没搜过的物件」，判定半径按家具体量给，
        // 柜门宽约 140px，旧版统一 74px 会让人站在柜前却翻不开。
        NearFurniture = -1;
        float bestF = float.MaxValue;
        for (int i = 0; i < Furniture.Count; i++)
        {
            var f = Furniture[i];
            if (f.Opened || !HasClosedContents(f)) continue;
            float reach = f.Type switch
            {
                FurnitureType.Cabinet => 112f,
                FurnitureType.Desk => 96f,
                FurnitureType.PictureFrame => 92f,
                _ => 88f,
            };
            float d = Mathf.Abs(f.Center.X - PlayerX);
            if (d < reach && d < bestF) { bestF = d; NearFurniture = i; }
        }

        _fg.WalkPhase = _walkT;
        _fg.Facing = _facing;
        _fg.QueueRedraw();
    }

    /// <summary>v3.71 实时掉落积分：重力 + 地板/家具顶碰撞 + 回弹衰减，落到合理处并静止。</summary>
    private void PhysicsStep(float dt)
    {
        foreach (var it in Items)
        {
            if (it.Mode != RoomItemMode.FloorDrop || it.Settled) continue;
            if (it.DropDelay > 0f) { it.DropDelay -= dt; continue; }
            it.Vel = new Vector2(it.Vel.X, it.Vel.Y + 1500f * dt);
            it.Pos = new Vector2(it.Pos.X, it.Pos.Y + it.Vel.Y * dt);
            // 家具顶碰撞：落到某家具台面上
            foreach (var f in Furniture)
            {
                float top = f.TopY();
                float hw = f.HalfW();
                if (it.Pos.X > f.Center.X - hw && it.Pos.X < f.Center.X + hw
                    && it.Pos.Y >= top - 2f && it.Pos.Y <= top + 30f)
                {
                    it.Pos = new Vector2(it.Pos.X, top);
                    it.Vel = Vector2.Zero; it.Settled = true; break;
                }
            }
            if (it.Settled) continue;
            // 地板碰撞
            if (it.Pos.Y >= FloorY)
            {
                it.Pos = new Vector2(it.Pos.X, FloorY);
                if (Mathf.Abs(it.Vel.Y) < 70f) { it.Vel = Vector2.Zero; it.Settled = true; }
                else it.Vel = new Vector2(it.Vel.X, -it.Vel.Y * 0.32f);
            }
        }
    }

    /// <summary>v3.84 该家具是否还藏着没搜过的物件（决定能否翻、要不要画提示）。</summary>
    private bool HasClosedContents(Furniture f)
    {
        foreach (var it in Items)
            if (it.Mode == RoomItemMode.Container && it.ContainerId == f.Id && !it.Done)
                return true;
        return false;
    }

    /// <summary>
    /// v3.84 翻找最近的可开家具（E 键入口）。返回是否真的打开了，
    /// 让 EnterableHouse._Process 决定这次 interact 是「开柜」还是「搜物件」。
    /// v3.71~v3.83 这个方法是孤儿：全仓零调用点，柜门在游戏中永远打不开，
    /// 只有 CaptureRoom 靠 foreach 强制 Opened=true 才看得到藏匿物。
    /// </summary>
    public bool OpenNearbyFurniture()
    {
        if (NearFurniture < 0 || NearFurniture >= Furniture.Count) return false;
        var f = Furniture[NearFurniture];
        if (f.Opened || !HasClosedContents(f)) return false;
        f.Opened = true;
        return true;
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

    // =====================================================================
    // v3.84 摆位与藏匿规格（全部可复算，坐标不再凭感觉给）
    // ---------------------------------------------------------------------
    // 背景贴图 1280x800，TextureRect(KeepAspectCovered) 铺进约 952x612 的房间画布：
    //   scale = max(952/1280, 612/800) = 612/800 = 0.765
    //   缩放后 979.2 x 612，左右各裁 (979.2-952)/2 = 13.6 px
    //   ⇒ canvasX = texX * 0.765 - 13.6 ；canvasY = texY * 0.765
    // 下表统一用「贴图像素」写家具锚点，运行时换算；住家 variant!=0 背景 FlipH，
    // MX(x)=952-x 与背景镜像同步。锚点取自带坐标网格的 previews/grid_*.png 逐格核对。
    // =====================================================================
    public const float BgScale = 0.765f;
    public const float BgCropX = 13.6f;
    public static Vector2 Bg(float texX, float texY)
        => new(texX * BgScale - BgCropX, texY * BgScale);

    /// <summary>家具锚点：(id, 类型, 贴图 x, 贴图 y) —— 对齐背景里真实画着的柜门 / 炉口 / 抽屉 / 挂画。</summary>
    private static (string, FurnitureType, float, float)[] FurnAnchors(RoomType type) => type switch
    {
        // 卷轴挂画中心(260,290) / 方桌抽屉(590,620) / 砖灶炉口(855,710) / 木柜门(1150,645)
        // 旧坐标 cab=(590,486) 与真实柜门 canvas x≈866 差 276px ⇒ 藏匿物全摆到画面正中
        RoomType.Residence => new (string, FurnitureType, float, float)[]
        {
            ("pic",   FurnitureType.PictureFrame, 260f, 290f),
            ("desk",  FurnitureType.Desk,         590f, 620f),
            ("stove", FurnitureType.Stove,        855f, 710f),
            ("cab",   FurnitureType.Cabinet,     1150f, 645f),
        },
        // 地图挂画(210,260) / 电台桌抽屉(580,590) / 砖灶炉口(855,645) / 木柜门(1150,585)
        // 旧坐标 desk=(672,446) 与真实桌抽屉 canvas x≈430 差 242px
        RoomType.CommOutpost => new (string, FurnitureType, float, float)[]
        {
            ("pic",   FurnitureType.PictureFrame, 210f, 260f),
            ("desk",  FurnitureType.Desk,         580f, 590f),
            ("stove", FurnitureType.Stove,        855f, 645f),
            ("cab",   FurnitureType.Cabinet,     1150f, 585f),
        },
        // 焚毁仓库：v3.82 已按 FloorY 补出焦黑柜 / 塌砖灶并经真帧验收，锚点换算回贴图像素后保持不变
        _ => new (string, FurnitureType, float, float)[]
        {
            ("desk",  FurnitureType.Desk,         632f, 604f),
            ("cab",   FurnitureType.Cabinet,      828f, 617f),
            ("stove", FurnitureType.Stove,        439f, 625f),
        },
    };

    /// <summary>
    /// v3.84 藏匿分配表：(家具 id, 允许藏入的 kind 优先序)。每个容器最多 2 件，
    /// 摊开时左右错位不重叠；放不下的顺延到下一容器，仍放不下才留明面。
    /// 用户 2026-11-13：「信，子弹有一些是藏在柜子里面……挂画……暗格放手榴弹，子弹或者遗书」。
    /// ⇒ 柜子吃 ammo/letter/medkit；挂画暗格吃 grenade/will/ammo/clue；桌抽屉吃信；灶膛吃遗书。
    /// </summary>
    private static (string furn, string[] kinds)[] HideRules(RoomType type) => type switch
    {
        RoomType.CommOutpost => new (string, string[])[]
        {
            ("cab",   new[] { "ammo", "medkit", "letter", "grenade" }),
            ("pic",   new[] { "grenade", "will", "clue", "ammo" }),
            ("desk",  new[] { "letter", "clue", "will" }),
            ("stove", new[] { "will", "ash" }),
        },
        RoomType.BurntWarehouse => new (string, string[])[]
        {
            ("cab",   new[] { "ammo", "medkit", "grenade" }),
            ("desk",  new[] { "letter", "clue", "will" }),
            ("stove", new[] { "will", "ash" }),
        },
        _ => new (string, string[])[]
        {
            ("cab",   new[] { "ammo", "letter", "medkit", "will" }),
            ("pic",   new[] { "grenade", "will", "clue", "ammo" }),
            ("desk",  new[] { "letter", "clue" }),
            ("stove", new[] { "will", "ash" }),
        },
    };

    /// <summary>明面锚点：只落在背景「空白地面」，避开柜/灶/桌/挂画，不再浮到墙上或家具上。</summary>
    private static Vector2[] SurfaceAnchors(RoomType type) => type switch
    {
        RoomType.BurntWarehouse => new[]
        {
            new Vector2(760f, 492f), new Vector2(540f, 492f), new Vector2(470f, 498f),
            new Vector2(410f, 500f), new Vector2(720f, 500f),
        },
        _ => new[]
        {
            new Vector2(120f, GroundY - 12f), new Vector2(300f, GroundY - 12f),
            new Vector2(760f, GroundY - 12f), new Vector2(210f, GroundY - 22f),
            new Vector2(690f, GroundY - 22f), new Vector2(390f, GroundY - 28f),
        },
    };

    /// <summary>v3.84：只造家具（不造示例物件），锚点按背景贴图换算；仓库沿用 v3.82 的 FloorY 体量。</summary>
    private void BuildFurniture(RoomType type, int variant, int seed)
    {
        bool mirror = (type == RoomType.Residence && variant != 0);
        float MX(float x) => mirror ? 952f - x : x;
        var rng = new RandomNumberGenerator(); rng.Seed = (ulong)(seed & 0xffffff) + 7919u;
        foreach (var a in FurnAnchors(type))
        {
            Vector2 c = Bg(a.Item3, a.Item4);
            Furniture.Add(new Furniture
            {
                Id = a.Item1,
                Type = a.Item2,
                Center = new Vector2(MX(c.X), c.Y),
                ContainsSpotId = null,     // 由 AssignPlacement 按真实 loot 回填
                Seed = (int)(rng.Randi() % 99991),
            });
        }
    }

    /// <summary>截图预览用的示例 loot（items==null 时）。走与真实游戏完全相同的分配表。</summary>
    private void BuildPreviewItems(RoomType type, int seed)
    {
        var demo = type switch
        {
            RoomType.CommOutpost => new (string, string)[]
            {
                ("ammo", "弹药盒"), ("letter", "地图桌抽屉里的作战图"),
                ("grenade", "木柄手榴弹"), ("will", "叠得整整齐齐的遗书"),
                ("medkit", "军医留下的医药包"), ("chest", "上锁的弹药箱"),
                ("ash", "墙角的废墟"), ("clue", "桌上的便条"),
                ("power", "墙边的电闸与手摇发电机"), ("radio", "无线电发信机"),
            },
            RoomType.BurntWarehouse => new (string, string)[]
            {
                ("ammo", "弹药盒"), ("grenade", "木柄手榴弹"),
                ("letter", "半塌桌抽屉里的残信"), ("medkit", "焦柜里的医药包"),
                ("will", "残灶里没烧尽的遗书"), ("chest", "焦黑的木箱"),
                ("gauze", "半卷纱布"), ("ash", "一摊烧焦的灰烬"),
            },
            _ => new (string, string)[]
            {
                ("ammo", "弹药盒"), ("letter", "抽屉里的家信"),
                ("grenade", "木柄手榴弹"), ("will", "灶台里叠好的遗书"),
                ("medkit", "柜中的医药包"), ("chest", "屋角上锁的木箱"),
                ("gauze", "炕上半卷纱布"), ("ash", "灶边烧焦的灰"),
                ("clue", "挂画后墙洞里的文件"),
            },
        };
        int i = 0;
        foreach (var d in demo)
            Items.Add(new RoomItem
            {
                SpotId = $"{type}:demo:{i++}", Kind = d.Item1, Label = d.Item2,
                Done = false,
            });
    }

    /// <summary>
    /// v3.84 核心：把每件 loot 分派到「藏进哪个家具」或「留在明面哪块空地」。
    /// 确定性、可复算；一个容器最多 2 件；真实游戏与预览共用。
    /// </summary>
    private void AssignPlacement()
    {
        // v3.84：焚毁仓库沿用 v3.82 已逐帧验收过的摆位，本轮不重排（避免破坏已确认成果）
        if (Type == RoomType.BurntWarehouse)
        {
            AssignPlacementBurntV382();
            return;
        }
        bool mirror = (Type == RoomType.Residence && Variant != 0);
        float MX(float x) => mirror ? 952f - x : x;
        var placed = new HashSet<RoomItem>();

        foreach (var (furnId, kinds) in HideRules(Type))
        {
            var f = Furniture.Find(x => x.Id == furnId);
            if (f == null) continue;
            int cap = 2;
            foreach (var kind in kinds)
            {
                if (cap <= 0) break;
                foreach (var it in Items)
                {
                    if (cap <= 0) break;
                    if (placed.Contains(it) || it.Kind != kind) continue;
                    it.Mode = RoomItemMode.Container;
                    it.ContainerId = f.Id;
                    it.Pos = f.RecessPos();
                    placed.Add(it);
                    cap--;
                    if (f.ContainsSpotId == null) f.ContainsSpotId = it.SpotId;
                }
            }
        }

        var anchors = SurfaceAnchors(Type);
        int ai = 0;
        foreach (var it in Items)
        {
            // 关键物（真实烈士姓名的信/遗书）沿用 v3.71 的不刺眼微光；真实 loot 由这里补齐标记
            if (it.Kind == "letter" || it.Kind == "will") it.Key = true;
            if (placed.Contains(it)) continue;
            it.Mode = RoomItemMode.Surface;
            var a = anchors[ai % anchors.Length];
            it.Pos = new Vector2(MX(a.X), a.Y);
            ai++;
        }
    }

    /// <summary>
    /// v3.84 仓库兼容分支：完整复刻 v3.82 的藏匿与摆位（用户 2026-11-13 逐帧验收通过）。
    /// 本轮「锚点对齐 / 小件尺寸表 / 挂画暗格」只作用于住家与通信据点；
    /// 仓库的焦黑柜、塌砖灶、灰堆与实时掉落一律不动。
    /// </summary>
    private void AssignPlacementBurntV382()
    {
        foreach (var f in Furniture) f.ContainsSpotId = null;
        var desk = Furniture.Find(x => x.Id == "desk");
        var cab = Furniture.Find(x => x.Id == "cab");
        var stove = Furniture.Find(x => x.Id == "stove");
        foreach (var it in Items)
        {
            if (it.Kind == "letter" || it.Kind == "will") it.Key = true;
            switch (it.Kind)
            {
                case "letter" when desk != null:  HideIn(it, desk); break;
                case "medkit" when cab != null:   HideIn(it, cab); break;
                case "will" when stove != null:   HideIn(it, stove); break;
                case "ammo":                      DropAt(it, 410f, 0f); break;
                case "grenade":                   DropAt(it, 720f, 0.14f); break;
                case "chest":                     SurfaceAt(it, 760f, 492f); break;
                case "gauze":                     SurfaceAt(it, 540f, 492f); break;
                case "ash":                       SurfaceAt(it, 470f, 498f); break;
                default:                          SurfaceAt(it, 620f, 496f); break;
            }
        }
    }

    private void HideIn(RoomItem it, Furniture f)
    {
        it.Mode = RoomItemMode.Container;
        it.ContainerId = f.Id;
        it.Pos = f.RecessPos();
        if (f.ContainsSpotId == null) f.ContainsSpotId = it.SpotId;
    }

    private void DropAt(RoomItem it, float x, float delay)
    {
        it.Mode = RoomItemMode.FloorDrop;
        it.Pos = new Vector2(x, FloorY - 150f);
        it.DropDelay = delay;
    }

    private void SurfaceAt(RoomItem it, float x, float y)
    {
        it.Mode = RoomItemMode.Surface;
        it.Pos = new Vector2(x, y);
    }

    /// <summary>v3.84：给 UI 的翻找动词（未开家具的提示牌文案）。</summary>
    public string FurnitureVerb(int idx)
    {
        if (idx < 0 || idx >= Furniture.Count) return "翻找";
        return Furniture[idx].Type switch
        {
            FurnitureType.Cabinet => "拉开柜门",
            FurnitureType.PictureFrame => "掀起挂画",
            FurnitureType.Desk => "拉开抽屉",
            FurnitureType.Stove => "扒开灶膛",
            _ => "翻找",
        };
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

    // =====================================================================
    // v3.82 道具贴图（贴图资产，不是运行时程序化绘制）
    // ---------------------------------------------------------------------
    // _propsAtlas      : 住家 / 通信据点 —— 完好家具（v3.81 原图集，不动）
    // _propsAtlasBurnt : 焚毁仓库 —— 从 burnt_warehouse.png 同源裁片 + 焦化残件（新增，
    //                    不覆盖原图集；原图集里的木柜 / 砖灶 / 竹箩 / 茶壶全是完好鲜亮的，
    //                    叠到焦黑废墟上就成了亮橙色小贴纸）
    // 用户 2026-11-13：「因为你给的图片是烧坏的房子，那些橱柜或者灶子等等物品
    // 应该有烧坏的迹象和款式，还有各自的尺寸也没有把握好」
    //   ⇒ ① 按 RoomType 分流图集；② 每类物件给独立尺寸，不再一律 68x56。
    // =====================================================================
    private static Texture2D _propsAtlas;
    private static Texture2D _propsAtlasBurnt;

    private bool IsBurnt => _room.Type == RoomType.BurntWarehouse;
    /// <summary>焚毁仓库专用图集缺失时自动回退到原图集，绝不黑屏。</summary>
    private Texture2D Atlas => (IsBurnt && _propsAtlasBurnt != null) ? _propsAtlasBurnt : _propsAtlas;

    /// <summary>
    /// v3.82 每类物件的目标宽度（房间画布 px）。高度由裁片比例自动推导 h = w * sh / 160，
    /// 保证 160x120 格永不被拉变形。
    /// 基准：画布 952x612，背景里那张方桌约 290px 宽 ≈ 120cm ⇒ 1px ≈ 0.41cm。
    ///   L 大件 96–132px ≈ 40–55cm（木箱 / 灰堆 / 电台 / 电闸）
    ///   M 中件 60–74px ≈ 25–30cm（弹药箱 / 医药包 / 纱布 / 手榴弹）
    ///   S 小件 44–46px ≈ 18–19cm（信 / 遗书 / 便条）
    /// 极值验算：最大 ash = 132x42.9，最小 clue = 44x33；
    ///   面积比 (132*42.9)/(44*33) ≈ 3.90×（v3.81 统一 68x56 时为 1.00×，全无主次）；
    ///   单件最宽 132/952 = 13.9% 画布宽，不遮挡窗与房梁。
    /// ash 只取格子中段 52px 扁条（灰堆摊在地上，不该是 4:3 的方块）。
    /// </summary>
    private static readonly Dictionary<string, (int tile, float w, float sy, float sh)> PropSpecBurnt = new()
    {
        { "chest",   (0, 108f,  0f, 120f) },
        { "ammo",    (1,  74f,  0f, 120f) },
        { "grenade", (2,  60f,  0f, 120f) },
        { "letter",  (3,  46f,  0f, 120f) },
        { "will",    (4,  46f,  0f, 120f) },
        { "clue",    (5,  44f,  0f, 120f) },
        { "gauze",   (6,  62f,  0f, 120f) },
        { "medkit",  (7,  70f,  0f, 120f) },
        { "ash",     (8, 132f, 38f,  52f) },
        { "power",   (9,  96f,  0f, 120f) },
        { "radio",  (10, 112f,  0f, 120f) },
    };

    /// <summary>
    /// v3.84 完好房间（住家 / 通信据点）小件表：(贴图源, srcX, srcY, srcW, srcH, 目标宽)。
    /// sheet 0 = props_atlas.png(640×360) / 1 = props_atlas_burnt.png(640×480) / 2 = paper_burnt_v1.png(384×384)。
    ///
    /// 【为什么必须拆表】v3.82 的 L/M/S 尺寸表被全局套用，完好房间也放大到 44–132px；
    /// 而 props_atlas 的 12 格其实全是背景里「整件大家具」（木柜 / 砖灶 / 竹箩堆 / 卷轴 / 电表 / 电台）
    /// 的裁片，叠到已经画着同款家具的背景上 ⇒ 用户圈的「一叠竹箩浮在桌上 / 尺寸不对 / 摆放不对」。
    /// 现在：① 只裁单个小木箱 / 竹箩上沿等局部；② 目标宽收敛到 38–52px（能拿在手里的尺寸）；
    /// ③ 信件类直接取 v3.83 验收过的揉皱旧纸贴图，语义与纸张 UI 统一。
    /// 高由 h = w × srcH / srcW 推导，src 与 dst 同比 ⇒ 永不拉伸。
    /// </summary>
    private static readonly Dictionary<string, (int sheet, int sx, int sy, int sw, int sh, float w)> IntactSpec = new()
    {
        { "chest",   (0, 168,  10, 145, 100, 52f) },  // tile1 单个木箱
        { "ammo",    (0, 326,  20, 140,  90, 44f) },  // tile2 小木箱
        { "grenade", (0, 486, 246, 140,  80, 40f) },  // tile11 木箱组里的单个
        { "letter",  (2,  96,  96, 192, 140, 42f) },  // 揉皱旧纸（v3.83 同源资产）
        { "will",    (2,  96,  96, 192, 140, 42f) },
        { "clue",    (2,  96,  96, 192, 140, 38f) },
        { "gauze",   (0, 486, 126, 148,  70, 44f) },  // tile7 竹箩上沿（浅色编织，读作卷布）
        { "medkit",  (0, 326, 246, 148,  90, 46f) },  // tile10 侧面木箱
        { "ash",     (1,   0, 278, 160,  52, 50f) },  // v3.82 已验收的灰堆扁条
        { "power",   (0, 166, 246, 150, 110, 48f) },  // tile9 电表
        { "radio",   (0, 326, 246, 150, 110, 50f) },  // tile10 电台
    };

    /// <summary>v3.84 信件类用的揉皱旧纸贴图（v3.83 资产，不新增美术）。</summary>
    private static Texture2D _paperTex;

    /// <summary>
    /// v3.82 焚毁仓库家具前脸：(格号, 目标宽, 目标高, srcX, srcY, srcW, srcH)。
    /// src 与 dst 宽高比严格一致 ⇒ 不拉伸；柜 / 灶底边落在 FloorY，站得住。
    /// 背景贴图里只有一张方桌，没有橱柜和砖灶 —— 这两件必须补出实体，
    /// 否则只剩 v3.81 的 48x42 小贴纸浮在半空（用户说的「款式 / 尺寸没把握好」）。
    /// </summary>
    private static readonly Dictionary<FurnitureType, (int tile, float w, float h, float sx, float sy, float sw, float sh)> BurntFront = new()
    {
        { FurnitureType.Cabinet,      (12, 116f, 152f, 34f,  0f,  92f, 120f) },
        { FurnitureType.Stove,        (13, 120f,  92f, 20f, 14f, 120f,  92f) },
        { FurnitureType.Desk,         (14,  64f,  30f, 48f, 45f,  64f,  30f) },
        { FurnitureType.PictureFrame, (15,  60f,  84f, 50f,  0f,  60f,  84f) },
    };

    /// <summary>
    /// v3.84 完好房间「柜门 / 画心 / 抽屉 / 灶面」在背景贴图里的源矩形（贴图像素）。
    /// 前景从**同一张背景贴图**裁出、按 Bg() 换算 1:1 画回原处：
    ///   • 关闭时与背景像素完全重合 ⇒ 不会像 v3.81 那样叠一小块不协调的贴片（用户圈的「摆放不对」）；
    ///   • OpenT 时整块平移 + 淡出 ⇒ 真正的「拉开柜门 / 掀起挂画」动画，露出暗格里的物件。
    /// 零新增美术资产、零运行时程序化场景实体、零拉伸（src 与 dst 同比）。
    /// 源矩形来自 previews/grid_residence.png / grid_comm_outpost.png 逐格核对。
    /// </summary>
    private static readonly Dictionary<(RoomType, FurnitureType), Rect2> FrontSrc = new()
    {
        { (RoomType.Residence, FurnitureType.Cabinet),       new Rect2(1060, 520, 180, 250) },
        { (RoomType.Residence, FurnitureType.PictureFrame),  new Rect2( 190,  60, 140, 460) },
        { (RoomType.Residence, FurnitureType.Desk),          new Rect2( 410, 525, 360,  55) },
        { (RoomType.Residence, FurnitureType.Stove),         new Rect2( 790, 555, 140, 205) },
        { (RoomType.CommOutpost, FurnitureType.Cabinet),      new Rect2(1060, 460, 180, 250) },
        { (RoomType.CommOutpost, FurnitureType.PictureFrame), new Rect2( 100, 120, 220, 280) },
        { (RoomType.CommOutpost, FurnitureType.Desk),         new Rect2( 400, 490, 360,  50) },
        { (RoomType.CommOutpost, FurnitureType.Stove),        new Rect2( 790, 500, 140, 200) },
    };

    /// <summary>贴图像素矩形 → 房间画布矩形（与 InteriorBackdrop 的 KeepAspectCovered 同一换算）。</summary>
    private static Rect2 BgRect(Rect2 src) => new(
        src.Position.X * InteriorRoom.BgScale - InteriorRoom.BgCropX,
        src.Position.Y * InteriorRoom.BgScale,
        src.Size.X * InteriorRoom.BgScale,
        src.Size.Y * InteriorRoom.BgScale);

    /// <summary>v3.84 前景柜门裁片用的背景贴图（与 InteriorBackdrop 同一份资源，不新增资产）。</summary>
    private Texture2D _roomTex;

    public InteriorForeground(InteriorRoom room)
    {
        _room = room;
        MouseFilter = Control.MouseFilterEnum.Ignore;
    }

    public override void _Ready()
    {
        _propsAtlas ??= GD.Load<Texture2D>("res://assets/art/interior/props_atlas.png");
        _propsAtlasBurnt ??= GD.Load<Texture2D>("res://assets/art/interior/props_atlas_burnt.png");
        _paperTex ??= GD.Load<Texture2D>("res://assets/art/ui/paper_burnt_v1.png");
        // v3.84：柜门 / 画心裁片直接取同一张背景贴图，不新增美术资产
        _roomTex = GD.Load<Texture2D>(_room.Type switch
        {
            RoomType.CommOutpost => "res://assets/art/interior/comm_outpost.png",
            RoomType.BurntWarehouse => "res://assets/art/interior/burnt_warehouse.png",
            _ => "res://assets/art/interior/residence.png",
        });
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

        // 0) 窗光 + 窗外晕染（最底层，背景之上、家具之下）
        DrawWindowLight(W, H, _room.WorldTime);
        // 1) 门
        DrawDoor(W, H, _room.WorldTime);

        // 2) v3.84 逐家具分层：地落物 → 暗格（开门后遮住背景柜门）→ 全部藏匿物（左右错位）
        //    → 柜门/画心裁片。裁片在最上层：关闭时完全盖住暗格与物件 ⇒ 「不开就看不见」；
        //      OpenT 平移 + 淡出 ⇒ 开门过程中渐渐看见里面的子弹/手榴弹/遗书。
        //    旧版 Find() 只画第一件，一个柜子藏多件时后面几件永远不出现。
        foreach (var f in _room.Furniture)
        {
            foreach (var it in _room.Items)
                if (it.Mode == RoomItemMode.FloorDrop && OccludedBy(f, it))
                    DrawDropItem(it);
            DrawFurnitureBody(f, _room.WorldTime);
            if (f.OpenT > 0.05f)
            {
                int total = 0;
                foreach (var it in _room.Items)
                    if (it.Mode == RoomItemMode.Container && it.ContainerId == f.Id) total++;
                int k = 0;
                foreach (var it in _room.Items)
                {
                    if (it.Mode != RoomItemMode.Container || it.ContainerId != f.Id) continue;
                    Vector2 p = f.RecessPos() + new Vector2(
                        (k - (total - 1) * 0.5f) * 38f, (k % 2 == 0 ? 0f : 7f));
                    bool near = _room.NearIndex >= 0 && _room.NearIndex < _room.Items.Count
                                && _room.Items[_room.NearIndex] == it;
                    DrawItem(it, p, near, f.OpenT);
                    k++;
                }
            }
            DrawFurnitureFront(f, _room.WorldTime);
        }

        // 3) 不被遮挡的地落物（画在最前）
        foreach (var it in _room.Items)
            if (it.Mode == RoomItemMode.FloorDrop && !OccludedAny(it))
                DrawDropItem(it);

        // 4) 台面 / 地面 Surface 物件
        foreach (var it in _room.Items)
            if (it.Mode == RoomItemMode.Surface)
                DrawItem(it, it.Pos, _room.NearIndex >= 0 && _room.Items[_room.NearIndex] == it);

        // v3.70：进入室内自动切换第一视角 —— 隐藏走动小人，改画「准星扫视」
        if (_room.FirstPerson) DrawReticle(W, H);
        else DrawPlayer(W, H);

        // 靠近物件 / 门口时的漫画风小提示牌（米黄土实色 + 3px 深墨棕粗框 + 硬投影）
        RefreshPrompt(W, H);
        RefreshResult(W, H);
    }

    private void DrawDoor(float W, float H, WorldClock.Phase t)
    {
        float floor = H * 0.635f;
        // v3.81：不再画程序化几何门 —— 背景贴图 residence / comm_outpost / burnt_warehouse
        // 本身就有手绘木门与卷轴，再叠一层 Box/Line 色块只会造成画风割裂。
        // 仅保留门口交互箭头（UI 反馈，非场景美术）。
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

    // =====================================================================
    // v3.71 窗光 / 家具 / 掉落 绘制子系统（对应五大诉求）
    // =====================================================================

    /// <summary>
    /// v3.71 用户诉求③：窗光随时辰 —— 夜=月光、晨=日光、黄昏=余晖。
    /// 画在背景之上、家具之下：窗外晕染（天空/地面/月或日）+ 房间光晕 + 地面光束（+夜：月与星）。
    /// 取色全部来自 WorldClock.For(t)，与关卡全局时辰联动。
    /// </summary>
    private void DrawWindowLight(float W, float H, WorldClock.Phase t)
    {
        var pal = WorldClock.For(t);
        float wx = W * 0.66f, wy = 54f, ww = 150f, wh = 118f;

        // v3.80（用户反馈 #6「这个根本不需要吧」+ 全局画风一致红线）：移除原先浮在
        // 右上墙面的「浅黄 2×2 方格 + 悬空圆盘」纯色窗框。它读起来像 UI 调试格子、
        // 与整体手绘上色漫画风冲突；房间左上已有手绘漫画风木格窗承担「有窗」信息。
        // v3.71 已确认的「窗光随时辰」功能核心保留：改为画外高窗透入的时辰光晕 +
        // 地面光束表达（颜色仍由 WorldClock.For(t) 联动，夜偏冷 / 晨昏偏暖），不回退功能。

        // 房间整体光晕（低 alpha，叠在背景上，不覆盖家具/物件）
        DrawRect(new Rect2(0, 0, W, H), pal.RoomWash);

        // 窗投到地面的光束（平行四边形：上端窄、落地宽；半透明）
        float lx = wx + 24f, rx = wx + ww - 24f;
        var beam = new Vector2[]
        {
            new Vector2(lx, wy + wh),
            new Vector2(rx, wy + wh),
            new Vector2(rx + 46f, InteriorRoom.FloorY),
            new Vector2(lx - 34f, InteriorRoom.FloorY),
        };
        DrawColoredPolygon(beam, pal.Beam);
        // 地面光斑（压扁椭圆）
        DrawFlatEllipse(new Vector2((lx + rx) / 2f + 6f, InteriorRoom.FloorY + 4f), 96f, 20f, pal.Beam);
    }

    /// <summary>压扁椭圆（光斑 / 地面阴影用，避免依赖 DrawEllipse 版本差异）。</summary>
    private void DrawFlatEllipse(Vector2 c, float rx, float ry, Color col, int seg = 26)
    {
        var pts = new Vector2[seg];
        for (int i = 0; i < seg; i++)
        {
            float ang = i * Mathf.Tau / seg;
            pts[i] = new Vector2(c.X + Mathf.Cos(ang) * rx, c.Y + Mathf.Sin(ang) * ry);
        }
        DrawColoredPolygon(pts, col);
    }

    /// <summary>
    /// v3.84 暗格矩形：按背景家具真实体量给尺寸，能并排放 2 件 34–52px 小件。
    /// 旧版一律 48×42，两件必然重叠；柜门宽 138px 却只开 48px 的洞也不合比例。
    /// 仓库沿用 v3.82 已验收的小凹洞（焦黑柜/灶另有 BurntFront 前脸盖着）。
    /// </summary>
    private Rect2 RecessRect(Furniture f)
    {
        if (IsBurnt)
        {
            float bw = f.Type switch { FurnitureType.Cabinet => 48f, FurnitureType.PictureFrame => 46f, FurnitureType.Stove => 46f, _ => 52f };
            float bh = f.Type switch { FurnitureType.Cabinet => 42f, FurnitureType.PictureFrame => 42f, FurnitureType.Stove => 22f, _ => 26f };
            Vector2 bc = f.RecessPos();
            return new Rect2(bc.X - bw / 2f, bc.Y - bh / 2f, bw, bh);
        }
        float w = f.Type switch { FurnitureType.Cabinet => 110f, FurnitureType.PictureFrame => 100f, FurnitureType.Stove => 76f, _ => 106f };
        float h = f.Type switch { FurnitureType.Cabinet => 82f, FurnitureType.PictureFrame => 86f, FurnitureType.Stove => 40f, _ => 44f };
        Vector2 c = f.RecessPos();
        return new Rect2(c.X - w / 2f, c.Y - h / 2f, w, h);
    }

    /// <summary>判断地落物是否被某家具前脸遮挡（其 x 落在家具前脸水平范围内、且低于台面 → 藏在家具后面）。</summary>
    private bool OccludedBy(Furniture f, RoomItem it)
    {
        if (it.Mode != RoomItemMode.FloorDrop) return false;
        float hw = f.HalfW() * 1.5f;
        if (it.Pos.X < f.Center.X - hw || it.Pos.X > f.Center.X + hw) return false;
        // 落在家具台面上（被 PhysicsStep 顶住）不算遮挡，应画在家具之上
        if (it.Settled && Mathf.Abs(it.Pos.Y - f.TopY()) < 6f) return false;
        return it.Pos.Y > f.TopY() + 4f;
    }

    /// <summary>是否被任意家具遮挡。</summary>
    private bool OccludedAny(RoomItem it)
    {
        foreach (var f in _room.Furniture)
            if (OccludedBy(f, it)) return true;
        return false;
    }

    /// <summary>
    /// v3.71 用户诉求①：物件不平贴背景，要有纵深 —— 家具主体（含凹洞）提供遮挡关系。
    /// 先画凹洞（暗腔），再画家具 carcass 包住它；藏匿物在 _Draw 里于 body 与 front 之间露出。
    /// </summary>
    private void DrawFurnitureBody(Furniture f, WorldClock.Phase t)
    {
        if (IsBurnt)
        {
            // v3.82 焦黑仓库：保持已验收的低对比暗腔，本轮不动
            DrawRect(RecessRect(f), new Color(0.10f, 0.08f, 0.07f, 0.50f));
            return;
        }
        // v3.84 完好房间：只在柜门 / 画心开始让位后才出现暗格。
        // 【不用纯色黑块】第一版真帧里不透明 DrawRect 读成「柜门上贴了块黑补丁」，
        // 既难看又违背「场景实体不用程序化色块」的红线。改为：把暗格对应的那块
        // **同一张背景贴图**重新采样、压暗 modulate —— 木纹 / 墙纹 / 砖缝都还在，
        // 只是变暗，读作「柜子里 / 墙洞里是暗的」，零新增美术资产。
        if (f.OpenT <= 0.02f || _roomTex == null) return;
        var r = RecessRect(f);
        // canvas → 贴图像素（BgRect 的逆运算）
        var src = new Rect2(
            (r.Position.X + InteriorRoom.BgCropX) / InteriorRoom.BgScale,
            r.Position.Y / InteriorRoom.BgScale,
            r.Size.X / InteriorRoom.BgScale,
            r.Size.Y / InteriorRoom.BgScale);
        float a = Mathf.Clamp(f.OpenT, 0f, 1f);
        bool mirror = _room.Type == RoomType.Residence && _room.Variant != 0;
        if (mirror)
            DrawSetTransformMatrix(new Transform2D(
                new Vector2(-1f, 0f), new Vector2(0f, 1f), new Vector2(952f, 0f)));
        // 压暗幅度收敛成「淡墨阴影」，不读作黑补丁；笔触仍可见
        DrawTextureRectRegion(_roomTex, r, src, new Color(0.60f, 0.53f, 0.45f, a));
        if (mirror) DrawSetTransformMatrix(Transform2D.Identity);
        // 一圈细墨线标出暗格边界（交互提示层，不是场景实体）
        DrawRect(r, new Color(0.28f, 0.21f, 0.15f, 0.45f * a), false, 1.3f);
    }

    /// <summary>
    /// v3.71 用户诉求②：家具前脸（抽屉面 / 柜门 / 炉门 / 画心）盖住凹洞；
    /// 翻开时按 OpenT 平移离开，露出凹洞与藏匿物（开启动画）。
    /// </summary>
    private void DrawFurnitureFront(Furniture f, WorldClock.Phase t)
    {
        // v3.82 焚毁仓库 → 焦黑橱柜 / 塌半边砖灶，按家具真实体量画；本轮保持不动。
        if (IsBurnt && _propsAtlasBurnt != null && BurntFront.TryGetValue(f.Type, out var b))
        {
            var br = FrontRect(f);
            Vector2 bOff = f.Type switch
            {
                FurnitureType.Cabinet => new Vector2(b.w * 1.05f * f.OpenT, 0f),
                FurnitureType.PictureFrame => new Vector2(0f, -b.h * 1.05f * f.OpenT),
                _ => new Vector2(0f, b.h * 0.90f * f.OpenT),
            };
            var bfr = new Rect2(br.Position.X + bOff.X, br.Position.Y + bOff.Y, br.Size.X, br.Size.Y);
            var bsrc = new Rect2((b.tile % 4) * 160f + b.sx,
                                 (b.tile / 4) * 120f + b.sy, b.sw, b.sh);
            DrawTextureRectRegion(_propsAtlasBurnt, bfr, bsrc);
            return;
        }

        // v3.84 住家 / 通信据点：柜门、画心、抽屉、灶面从**同一张背景贴图**裁出，
        // 按 BgRect 1:1 画回原处（关闭时与背景像素完全重合，不再有 v3.81 那种
        // 48×42 小贴片浮在桌面 / 凳子上）；OpenT 时整块平移 + 淡出 = 开柜 / 掀画动画。
        if (_roomTex == null || !FrontSrc.TryGetValue((_room.Type, f.Type), out var src)) return;
        var dst = BgRect(src);
        Vector2 off = f.Type switch
        {
            FurnitureType.PictureFrame => new Vector2(0f, -dst.Size.Y * 0.94f * f.OpenT), // 向上掀起
            FurnitureType.Cabinet => new Vector2(dst.Size.X * 0.88f * f.OpenT, 0f),      // 向右拉开
            FurnitureType.Desk => new Vector2(0f, dst.Size.Y * 1.6f * f.OpenT),          // 抽屉抽出
            _ => new Vector2(dst.Size.X * 0.82f * f.OpenT, 0f),                          // 灶面挪开
        };
        var moved = new Rect2(dst.Position + off, dst.Size);
        float alpha = 1f - Mathf.Clamp(f.OpenT * 1.1f, 0f, 0.94f);
        bool mirror = _room.Type == RoomType.Residence && _room.Variant != 0;
        if (mirror)
            DrawSetTransformMatrix(new Transform2D(
                new Vector2(-1f, 0f), new Vector2(0f, 1f), new Vector2(952f, 0f)));
        DrawTextureRectRegion(_roomTex, moved, src, new Color(1f, 1f, 1f, alpha));
        if (mirror) DrawSetTransformMatrix(Transform2D.Identity);

        // 未开且靠近：细墨线四角，提示「这里能翻」（交互层，不是场景实体美术）
        if (!f.Opened && _room.NearFurniture >= 0 && _room.NearFurniture < _room.Furniture.Count
            && _room.Furniture[_room.NearFurniture] == f)
        {
            var q = RecessRect(f);
            float l = q.Position.X - 10f, rr = q.Position.X + q.Size.X + 10f;
            float tp = q.Position.Y - 10f, bt = q.Position.Y + q.Size.Y + 10f;
            float[] xs = { l, rr, l, rr }, ys = { tp, tp, bt, bt },
                     sx = { 1f, -1f, 1f, -1f }, sy = { 1f, 1f, -1f, -1f };
            var col = new Color(0.99f, 0.93f, 0.76f, 0.85f);
            for (int k = 0; k < 4; k++)
            {
                Line(new Vector2(xs[k], ys[k]), new Vector2(xs[k] + 13f * sx[k], ys[k]), 1.6f, col);
                Line(new Vector2(xs[k], ys[k]), new Vector2(xs[k], ys[k] + 13f * sy[k]), 1.6f, col);
            }
        }
    }

    /// <summary>v3.82 烧毁家具前脸矩形：柜 / 灶底边贴地板，桌抽屉沿用凹洞位。</summary>
    private Rect2 FrontRect(Furniture f)
    {
        if (!BurntFront.TryGetValue(f.Type, out var b)) return RecessRect(f);
        float cy = (f.Type == FurnitureType.Cabinet || f.Type == FurnitureType.Stove)
            ? InteriorRoom.FloorY - b.h / 2f
            : f.RecessPos().Y;
        return new Rect2(f.Center.X - b.w / 2f, cy - b.h / 2f, b.w, b.h);
    }

    /// <summary>v3.71 用户诉求④：地落物 —— 接触阴影落在地面、随离地高度缩放；本体由 DrawItemCore 画。</summary>
    private void DrawDropItem(RoomItem it)
    {
        bool near = _room.NearIndex >= 0 && _room.NearIndex < _room.Items.Count && _room.Items[_room.NearIndex] == it;
        DrawItemCore(it, it.Pos, near, 1f, true);
    }

    /// <summary>关键物（真实烈士信/遗书）微光：不刺眼的暖光脉冲，画在物件之前（被物件压住，只剩光晕）。</summary>
    private Vector2[] GlowPoly(Vector2 c, float r)
    {
        int seg = 18;
        var pts = new Vector2[seg];
        for (int i = 0; i < seg; i++)
        {
            float ang = i * Mathf.Tau / seg;
            pts[i] = new Vector2(c.X + Mathf.Cos(ang) * r, c.Y + Mathf.Sin(ang) * r * 0.7f);
        }
        return pts;
    }

    /// <summary>
    /// 物件绘制核心。origin=绘制中心；reveal=开启淡入(0..1)；floorShadow=true 时阴影画在地面（地落物）。
    /// v3.71：关键物未搜前画一抹随低频 sin 脉冲的暖光，提醒但不刺眼。
    /// </summary>
    private void DrawItemCore(RoomItem it, Vector2 p, bool near, float reveal, bool floorShadow)
    {
        float a = (it.Done ? 0.55f : 1f) * reveal;
        float seed = StrHash(it.SpotId);

        if (floorShadow)
        {
            float above = InteriorRoom.FloorY - it.Pos.Y;
            float k = Mathf.Clamp(1f - above / 220f, 0.15f, 1f);
            DrawFlatEllipse(new Vector2(it.Pos.X, InteriorRoom.FloorY + 18f),
                20f * (0.45f + 0.55f * k), 7f, new Color(0.10f, 0.08f, 0.06f, 0.42f * k));
        }
        else
        {
            DrawContactShadow(p, a);
        }

        // 关键物微光（仅真实烈士姓名信/遗书，未搜前）
        if (it.Key && !it.Done)
        {
            float g = (float)(Time.GetTicksMsec() % 1600) / 1600f;
            float pulse = 0.10f + 0.07f * (0.5f + 0.5f * Mathf.Sin(g * Mathf.Tau));
            for (int i = 3; i >= 1; i--)
                DrawColoredPolygon(GlowPoly(p, 30f * i + 10f), new Color(0.99f, 0.86f, 0.55f, pulse / i));
        }

        // v3.84：仓库沿用 v3.82 已验收的大件表；完好房间改用 38–52px 小件表 + 多贴图源。
        // src 与 dst 同比 ⇒ 不拉伸；Surface 物件底边贴 GroundY（背景真实地板线），不再浮在墙上。
        bool drawn = false;
        if (IsBurnt)
        {
            var tex = Atlas;
            if (tex != null)
            {
                var s = PropSpecBurnt.TryGetValue(it.Kind, out var got)
                    ? got : (tile: 11, w: 68f, sy: 0f, sh: 120f);
                float w = s.w;
                float h = w * s.sh / 160f;
                float cy = it.Mode == RoomItemMode.Surface
                    ? Mathf.Min(p.Y, InteriorRoom.FloorY - h / 2f)
                    : p.Y;
                var src = new Rect2((s.tile % 4) * 160f, (s.tile / 4) * 120f + s.sy, 160f, s.sh);
                var dst = new Rect2(p.X - w / 2f, cy - h / 2f, w, h);
                DrawTextureRectRegion(tex, dst, src, new Color(1f, 1f, 1f, a));
                drawn = true;
            }
        }
        else
        {
            var s = IntactSpec.TryGetValue(it.Kind, out var got)
                ? got : (sheet: 0, sx: 480, sy: 240, sw: 160, sh: 120, w: 44f);
            Texture2D t = s.sheet switch { 1 => _propsAtlasBurnt, 2 => _paperTex, _ => _propsAtlas };
            if (t != null)
            {
                float w = s.w;
                float h = w * s.sh / s.sw;
                float cy = it.Mode == RoomItemMode.Surface
                    ? Mathf.Min(p.Y, InteriorRoom.GroundY - h / 2f)
                    : p.Y;
                var src = new Rect2(s.sx, s.sy, s.sw, s.sh);
                var dst = new Rect2(p.X - w / 2f, cy - h / 2f, w, h);
                // 旧纸中心偏亮，在这间暖褐屋子里压到米黄，避免白得像 UI 贴片
                var mod = s.sheet == 2
                    ? new Color(0.76f, 0.70f, 0.60f, a)
                    : new Color(1f, 1f, 1f, a);
                DrawTextureRectRegion(t, dst, src, mod);
                drawn = true;
            }
        }
        if (!drawn)
        {
            // 兜底：仅当贴图未导入时才走旧程序化绘制
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

    // v3.71：新重载 —— 支持凹洞 origin + 开启淡入 reveal
    private void DrawItem(RoomItem it, Vector2 origin, bool near, float reveal = 1f)
        => DrawItemCore(it, origin, near, reveal, false);

    // 兼容旧调用（若有）
    private void DrawItem(RoomItem it, bool near)
        => DrawItemCore(it, it.Pos, near, 1f, false);

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
        else if (_room.NearFurniture >= 0 && _room.NearFurniture < _room.Furniture.Count)
        {
            // v3.84：未开的柜 / 挂画 / 抽屉 / 灶膛，先提示「拉开 / 掀起」，
            // 而不是直接写里面有什么 —— 里面的东西必须翻开后才让玩家知道。
            var f = _room.Furniture[_room.NearFurniture];
            text = $"E · {_room.FurnitureVerb(_room.NearFurniture)}";
            pos = new Vector2(Mathf.Clamp(f.Center.X - 110f, 90f, W - 240f),
                              Mathf.Clamp(f.Center.Y - 112f, 58f, H - 150f));
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
