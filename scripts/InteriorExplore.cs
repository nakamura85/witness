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

    /// <summary>
    /// v3.85 指针（鼠标）在房间画布内的本地坐标；(-1,-1) = 没有有效指针。
    /// 用户诉求：「鼠标点到挂画时候是有提示按 E 可以掀起来……如果鼠标没有选中就没有提示」。
    /// 旧版只有 A/D 水平扫视，判定只看 |x - PlayerX|：挂画在 y≈220 根本无法被「指向」，
    /// 而柜子 112px 的横向半径又让准星停在柜前一大片都算命中 ⇒ 提示看起来常驻。
    /// 现在改成 X+Y 双轴矩形命中，指针移开就没有任何提示。
    /// </summary>
    public Vector2 Pointer = new(-1f, -1f);
    public bool PointerActive => Pointer.X >= 0f && Pointer.Y >= 0f;
    /// <summary>真实游戏自动读鼠标；截图场景置 false，避免 headless 的 (0,0) 误命中。</summary>
    public bool AutoMouse = true;
    /// <summary>鼠标静止超过该秒数后回落 A/D 准星，键盘玩家不会被锁死。</summary>
    public const float PointerIdleTimeout = 0.6f;
    private float _pointerIdle = 99f;
    private Vector2 _lastMouse = new(-1f, -1f);

    public bool AtDoor => (PointerActive ? Pointer.X : PlayerX) <= 82f;
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
    public void Step(float dt, float dir, Vector2? pointer = null, bool clicked = false)
    {
        if (_fg == null) return;
        float W = Size.X > 0f ? Size.X : 952f;

        // ---- v3.86 指针来源优先级：截图强制 > 左键点击 > 真实鼠标 > 超时回落 A/D 准星 ----
        // 用户拍板「鼠标左键点击是否也能触发／可以的」：点击那一帧即便鼠标已静止 0.6s
        // （Pointer 已回落 (-1,-1)），也要用当前鼠标位置重新算一次命中，否则点了没反应。
        // 点击在屋内画布之外不生效，避免点到关卡 UI 误开柜。
        if (!pointer.HasValue && clicked && AutoMouse && GetViewport() != null)
        {
            Vector2 clickGlobal = GetViewport().GetMousePosition();
            if (GetGlobalRect().HasPoint(clickGlobal))
            {
                Pointer = GetLocalMousePosition();
                _lastMouse = clickGlobal;
                _pointerIdle = 0f;
            }
        }
        if (pointer.HasValue)
        {
            Pointer = pointer.Value;
            _pointerIdle = 0f;
        }
        else if (AutoMouse && GetViewport() != null)
        {
            Vector2 global = GetViewport().GetMousePosition();
            if (GetGlobalRect().HasPoint(global))
            {
                if (global.DistanceTo(_lastMouse) > 0.5f)
                {
                    Pointer = GetLocalMousePosition();
                    _lastMouse = global;
                    _pointerIdle = 0f;
                }
                else _pointerIdle += dt;
            }
            else { Pointer = new Vector2(-1f, -1f); _pointerIdle += dt; }
        }
        else _pointerIdle += dt;
        if (!pointer.HasValue && _pointerIdle > PointerIdleTimeout)
            Pointer = new Vector2(-1f, -1f);

        if (Mathf.Abs(dir) > 0.01f)
        {
            // 鼠标模式下 A/D 移动指针（保持 y），键盘模式下移动小人；两套输入不打架
            if (PointerActive)
                Pointer = new Vector2(Mathf.Clamp(Pointer.X + dir * 205f * dt, 54f, W - 52f), Pointer.Y);
            else
                PlayerX = Mathf.Clamp(PlayerX + dir * 205f * dt, 54f, W - 52f);
            _walkT += dt * 9f;
            _facing = dir > 0f ? 1f : -1f;
        }

        // 家具开启动画（翻开后抽屉滑出 / 门摆开）
        foreach (var f in Furniture)
            if (f.Opened && f.OpenT < 1f) f.OpenT = Mathf.Min(1f, f.OpenT + dt * 3.2f);

        // 掉落物理（实时下落 / 回弹 / 静止）
        PhysicsStep(dt);

        // v3.85 最近可搜查物件：Container 需家具已开、FloorDrop 需静止；
        // 命中必须是「指针真的落在这件东西上」，不再是一整条水平带。
        NearIndex = -1;
        float best = float.MaxValue;
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
            if (!HitItem(it)) continue;
            float d = PointerActive ? Pointer.DistanceTo(it.Pos) : Mathf.Abs(it.Pos.X - PlayerX);
            if (d < best) { best = d; NearIndex = i; }
        }

        // v3.85 最近可翻找家具：未开 + 里面还有未搜物件 + 指针落在柜门/画心裁片矩形内。
        NearFurniture = -1;
        float bestF = float.MaxValue;
        for (int i = 0; i < Furniture.Count; i++)
        {
            var f = Furniture[i];
            if (f.Opened || !HasClosedContents(f)) continue;
            if (!HitFurniture(f)) continue;
            float d = PointerActive ? Pointer.DistanceTo(f.Center) : Mathf.Abs(f.Center.X - PlayerX);
            if (d < bestF) { bestF = d; NearFurniture = i; }
        }

        _fg.WalkPhase = _walkT;
        _fg.Facing = _facing;
        _fg.QueueRedraw();
    }

    /// <summary>
    /// v3.85 物件命中框：X±30 / Y±34。最小 38px 小件外留 11px 容差，
    /// 既能点到又不会「站在半米外就冒提示」；无指针时回落旧的水平 56px 判定。
    /// </summary>
    private bool HitItem(RoomItem it)
        => PointerActive
            ? Mathf.Abs(Pointer.X - it.Pos.X) <= 30f && Mathf.Abs(Pointer.Y - it.Pos.Y) <= 34f
            : Mathf.Abs(it.Pos.X - PlayerX) < 56f;

    /// <summary>
    /// v3.85 家具命中框：直接复用前景柜门/画心裁片在画布上的真实矩形，
    /// 画心是竖长条、柜门是竖矩形，必须按各自形状命中，不用 88~112px 横向大半径。
    /// </summary>
    private bool HitFurniture(Furniture f)
    {
        if (!PointerActive)
        {
            float reach = f.Type switch
            {
                FurnitureType.Cabinet => 112f,
                FurnitureType.Desk => 96f,
                FurnitureType.PictureFrame => 92f,
                _ => 88f,
            };
            return Mathf.Abs(f.Center.X - PlayerX) < reach;
        }
        if (InteriorForeground.FrontSrc.TryGetValue((Type, f.Type), out var src))
        {
            var r = InteriorForeground.BgRect(src);
            // 住家镜像 variant：画布左右翻转，命中框同步翻
            if (Type == RoomType.Residence && Variant != 0)
                r = new Rect2(952f - r.Position.X - r.Size.X, r.Position.Y, r.Size);
            return r.HasPoint(Pointer);
        }
        // 仓库 / 无裁片家具：退回暗格矩形外扩 12px
        float hw = f.HalfW() + 12f, hh = 46f;
        return Mathf.Abs(Pointer.X - f.Center.X) <= hw && Mathf.Abs(Pointer.Y - f.RecessPos().Y) <= hh;
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
    /// v3.86 藏匿 / 明面编排：同一 seed 结果可复算，但真实游戏每次进屋 seed 不同 ⇒ 随机。
    /// 用户拍板 Q3「完整的房子不用但是如果被烧坏的就要（减少明面小件）」、
    ///         Q7「三间房是否都要同款指针交互／要随机」。
    /// 洗牌对象：容器顺序、每容器 kind 优先序、loot 顺序、明面锚点。
    /// 完好房间：容器 cap=2，放不下的照旧留明面（数量不减少，用户说「不用」）。
    /// 焚毁仓库：容器 cap = 柜3 / 灶2 / 桌1（受 BurntFront 前脸体量约束，共 6），
    ///           不看 kind 随机兜底塞满，只留 ≤2 件明面（优先灰烬 / 焦木箱这类焦黑残物）；
    ///           标准 8 件 loot 下明面从 v3.82 的 5 件降到 2 件。
    /// 真实 loot 一件都不会消失：藏进容器的仍可搜，明面的仍可搜。
    /// </summary>
    private void AssignPlacement()
    {
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)((uint)(Seed ^ (Variant * 7919) ^ (Items.Count * 104729) ^ 0x5f3759df) % 1000003u) + 1u;

        bool burnt = (Type == RoomType.BurntWarehouse);
        bool mirror = (Type == RoomType.Residence && Variant != 0);
        float MX(float x) => mirror ? 952f - x : x;

        // 1) loot 洗牌；仓库把灰烬 / 焦木箱排到末尾，让它们优先留在明面（符合烧毁屋子）
        var pool = new List<RoomItem>(Items);
        Shuffle(pool, rng);
        if (burnt)
        {
            var tail = pool.FindAll(x => x.Kind == "ash" || x.Kind == "chest");
            pool.RemoveAll(x => x.Kind == "ash" || x.Kind == "chest");
            pool.AddRange(tail);
        }

        // 2) 容器规则洗牌 + 每容器 kind 优先序洗牌；容量按房间给
        var rules = new List<(string furn, string[] kinds)>();
        foreach (var (furnId, kinds) in HideRules(Type))
        {
            var k = (string[])kinds.Clone();
            Shuffle(k, rng);
            rules.Add((furnId, k));
        }
        Shuffle(rules, rng);

        var cap = new Dictionary<string, int>();
        foreach (var (furnId, _) in rules)
            cap[furnId] = burnt
                ? (furnId == "cab" ? 3 : furnId == "stove" ? 2 : 1)
                : 2;

        var placed = new HashSet<RoomItem>();
        foreach (var (furnId, kinds) in rules)
        {
            var f = Furniture.Find(x => x.Id == furnId);
            if (f == null || cap[furnId] <= 0) continue;
            // 先按 kind 优先序藏（信进抽屉、手榴弹进挂画暗格…）
            foreach (var kind in kinds)
            {
                if (cap[furnId] <= 0) break;
                foreach (var it in pool)
                {
                    if (cap[furnId] <= 0) break;
                    if (placed.Contains(it) || it.Kind != kind) continue;
                    HideItem(it, f); placed.Add(it); cap[furnId]--;
                }
            }
            // 仓库：不看 kind 随机兜底塞满，把原本摊在明面的小件收进柜 / 灶 / 桌
            if (burnt)
            {
                foreach (var it in pool)
                {
                    if (cap[furnId] <= 0) break;
                    if (placed.Contains(it)) continue;
                    HideItem(it, f); placed.Add(it); cap[furnId]--;
                }
            }
        }

        // 3) 剩余留明面；锚点也洗牌 ⇒ 三间房不会站成同一排
        var anchors = new List<Vector2>(SurfaceAnchors(Type));
        Shuffle(anchors, rng);
        int ai = 0;
        foreach (var it in pool)
        {
            if (it.Kind == "letter" || it.Kind == "will") it.Key = true;
            if (placed.Contains(it)) continue;
            var a = anchors[ai % anchors.Count]; ai++;
            it.Mode = RoomItemMode.Surface;
            it.Pos = burnt ? a : new Vector2(MX(a.X), a.Y);
        }
    }

    private static void Shuffle<T>(IList<T> list, RandomNumberGenerator rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = (int)(rng.Randi() % (uint)(i + 1));
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>v3.86：藏匿物摆在与前脸裁片绑定的暗格中心，不再用家具 Center 的近似值。</summary>
    private void HideItem(RoomItem it, Furniture f)
    {
        it.Mode = RoomItemMode.Container;
        it.ContainerId = f.Id;
        // 完好房间：暗格中心由柜门 / 画心裁片推导（含镜像）；仓库沿用 v3.82 的 RecessPos。
        it.Pos = Type == RoomType.BurntWarehouse
            ? f.RecessPos()
            : InteriorForeground.RecessCenterMirrored(Type, Variant, f.Type);
        if (f.ContainsSpotId == null) f.ContainsSpotId = it.SpotId;
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
    /// <summary>
    /// v3.86 淡墨软阴影贴图资产（离线自绘，见 tools/bridge_restyle/make_contact_shadow.py）。
    /// 用户拍板「不要程式化感觉会怪怪」⇒ 删掉 v3.85 运行时的三层 DrawFlatEllipse，
    /// 改为一次性贴图采样；运行时零程序化几何色块。
    /// </summary>
    private Texture2D _shadowTex;

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
    public static readonly Dictionary<(RoomType, FurnitureType), Rect2> FrontSrc = new()
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
    public static Rect2 BgRect(Rect2 src) => new(
        src.Position.X * InteriorRoom.BgScale - InteriorRoom.BgCropX,
        src.Position.Y * InteriorRoom.BgScale,
        src.Size.X * InteriorRoom.BgScale,
        src.Size.Y * InteriorRoom.BgScale);

    /// <summary>
    /// v3.86 前脸（柜门 / 画心 / 抽屉 / 灶面）在房间画布上的矩形；仓库没有裁片时返回空矩形。
    /// 这是「挂画 / 柜门到底盖住了哪一块」的唯一事实来源。
    /// </summary>
    public static Rect2 CoverRect(RoomType type, FurnitureType ft)
        => FrontSrc.TryGetValue((type, ft), out var src) ? BgRect(src) : new Rect2();

    /// <summary>
    /// v3.86 由前脸矩形推导规整暗格：四边内缩 14px、且每边至少留 8px，
    /// 所以 Recess ⊂ Front 恒成立 ⇒ 用户说的「前面的挂画一定要刚刚好遮到暗格」。
    /// 不做不规则边缘（用户：「暗格通常是已经挖好的，直接四四方方或长方形即可」）。
    /// </summary>
    public static Rect2 RecessForCover(Rect2 front)
    {
        if (front.Size.X < 16f || front.Size.Y < 16f) return front;
        float w = Mathf.Min(Mathf.Max(80f, front.Size.X - 28f), front.Size.X - 16f);
        float h = Mathf.Min(Mathf.Max(28f, front.Size.Y - 28f), front.Size.Y - 16f);
        return new Rect2(front.Position.X + (front.Size.X - w) * 0.5f,
                         front.Position.Y + (front.Size.Y - h) * 0.5f, w, h);
    }

    /// <summary>
    /// v3.86 暗格中心（画布坐标，含住家镜像 variant 翻转）。
    /// 藏匿物位置与指针命中都用它，保证「东西就摆在暗格里」，不飘到裁片外面。
    /// </summary>
    public static Vector2 RecessCenterMirrored(RoomType type, int variant, FurnitureType ft)
    {
        var c = RecessForCover(CoverRect(type, ft)).GetCenter();
        if (type == RoomType.Residence && variant != 0) c.X = 952f - c.X;
        return c;
    }

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
        // v3.86：软阴影 / 地面光斑统一用同一张淡墨贴图，不再运行时画椭圆
        _shadowTex ??= GD.Load<Texture2D>("res://assets/art/interior/contact_shadow.png");
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

        // v3.86 用户拍板「底部说明要不要彻底去掉／去掉」：
        // 整行常驻操作说明（含 v3.85 的淡墨小字版本）彻底删除。
        // 画面里唯一的操作反馈只剩「鼠标选中时才出现」的那张 E 纸条。

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
                    // v3.86：物件排在「由前脸裁片推导出来的暗格」中心，
                    // 与 AssignPlacement 写入的 it.Pos 用同一套坐标；2 列网格容纳最多 3 件不重叠。
                    Vector2 rc = IsBurnt
                        ? RecessRect(f).GetCenter()
                        : InteriorForeground.RecessCenterMirrored(_room.Type, _room.Variant, f.Type);
                    int rows = Mathf.CeilToInt(total / 2f);
                    Vector2 p = rc + new Vector2(
                        (k % 2 == 0 ? -19f : 19f),
                        (k / 2 - (rows - 1) * 0.5f) * 30f);
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

    /// <summary>
    /// v3.86 接触阴影：只采样一张离线自绘的淡墨晕染贴图，运行时零程序化椭圆。
    /// v3.85 的三层 DrawFlatEllipse 虽然压淡了，但形状是完美同心椭圆，
    /// 用户拍板「不要程式化感觉会怪怪」⇒ 换成有手绘不规则边 + 斑驳浓淡的 PNG。
    /// 贴图本身 alpha ≤0.44，叠在暖褐背景上读作一抹淡墨，不会像垫了块硬纸片。
    /// </summary>
    private void DrawContactShadow(Vector2 p, float a)
    {
        if (_shadowTex == null) return;
        float w = 58f, h = 20f;
        DrawTextureRect(_shadowTex, new Rect2(p.X - w / 2f, p.Y + 9f, w, h), false,
            new Color(1f, 1f, 1f, a));
    }

    // =====================================================================
    // v3.71 窗光 / 家具 / 掉落 绘制子系统（对应五大诉求）
    // =====================================================================

    /// <summary>
    /// v3.71 用户诉求③：窗光随时辰 —— 夜=月光、晨=日光、黄昏=余晖。
    /// 画在背景之上、家具之下：窗外晕染（天空/地面/月或日）+ 房间光晕 + 地面光束（+夜：月与星）。
    /// 取色全部来自 WorldClock.For(t)，与关卡全局时辰联动。
    /// </summary>
    /// <summary>
    /// v3.89 窗光修正（用户 2026-11-13：阴影区被凭空打亮、右侧灶台烟囱一带浮空亮块）。
    /// 根因：旧版把光束死钉在 wx=0.66W（右侧），但三张室内背景贴图的窗户/受光面都在
    /// 左侧（仅住家 variant!=0 因 FlipH 镜像转到右侧），等于在阴影侧凭空打光；
    /// 且全屏 RoomWash 均匀提亮，把本该暗的角落也提亮。
    /// 修正：① 光束移到真实窗户所在侧；② 整体光晕改为「靠窗渐亮、向阴影侧 smoothstep
    /// 衰减到 0」的局部光照，阴影侧不再被提亮（背景贴图本身已画好暗部）。
    /// </summary>
    // v3.91：窗光重做——删掉「墙上窗投光束平行四边形 + 地面矩形光斑」。
    // 这两块落在墙/地上的、边界清晰的浅色光形，在 v3.89/3.90 被反复判读成
    // 「淡化格子 / 浮贴方块」，用户明确「很不合理」。背景贴图本身已画好窗光与暗部，
    // 不再叠第二层离散光形。仅保留极淡的近窗竖向渐变（靠窗微亮、向阴影侧 smoothstep 归零），
    // 且整段用 0.5× 压暗，确保只是「空气里有光」的暗示，绝不是一块可见的亮斑/网格。
    private void DrawWindowLight(float W, float H, WorldClock.Phase t)
    {
        var pal = WorldClock.For(t);
        // 所有室内背景贴图的窗户/受光面都在左侧；仅住家 variant!=0 因贴图 FlipH 镜像，窗户转到右侧。
        bool rightWin = (_room.Type == RoomType.Residence && _room.Variant != 0);
        float winX = rightWin ? W * 0.74f : W * 0.30f;   // 窗户中心 x（背景贴图实测受光侧）

        // 局部光晕：仅在受光侧（窗户附近）做柔和的水平渐变提亮，绝不整屏均匀提亮、
        // 也绝不在墙/地上画任何离散光形。竖直细条只按 x 渐变，纵向无分条 ⇒ 读不出网格。
        int strips = 48;
        float colW = W / strips + 1f;
        for (int i = 0; i < strips; i++)
        {
            float x = i * colW;
            float d = rightWin ? (x - winX) / (W - winX) : (winX - x) / winX;
            d = Mathf.Clamp(d, 0f, 1f);
            float a = pal.RoomWash.A * 0.5f * (1f - Mathf.SmoothStep(0f, 1f, d) * 0.96f);
            if (a <= 0.003f) continue;
            DrawRect(new Rect2(x, 0f, colW, H),
                new Color(pal.RoomWash.R, pal.RoomWash.G, pal.RoomWash.B, a));
        }
    }

    // v3.86：DrawFlatEllipse 已删除 —— 接触阴影、地落阴影、地面光斑全部改贴图采样。
    // 用户明确「不要程式化感觉会怪怪」，运行时不再画任何椭圆 / 几何色块。

    /// <summary>
    /// v3.84 暗格矩形：按背景家具真实体量给尺寸，能并排放 2 件 34–52px 小件。
    /// 旧版一律 48×42，两件必然重叠；柜门宽 138px 却只开 48px 的洞也不合比例。
    /// 仓库沿用 v3.82 已验收的小凹洞（焦黑柜/灶另有 BurntFront 前脸盖着）。
    /// </summary>
    private Rect2 RecessRect(Furniture f)
    {
        if (IsBurnt)
        {
            // v3.86 仓库：暗格仍是规整矩形，尺寸按 BurntFront 前脸体量给
            // （柜 116x152 / 灶 120x92 / 断桌板 64x30 / 挂画 60x84），
            // 全部严格小于前脸 ⇒ 前脸刚好完全遮住暗格，且放得下 2 件藏匿物。
            float bw = f.Type switch { FurnitureType.Cabinet => 100f, FurnitureType.Stove => 92f, FurnitureType.PictureFrame => 56f, _ => 56f };
            float bh = f.Type switch { FurnitureType.Cabinet => 84f, FurnitureType.Stove => 62f, FurnitureType.PictureFrame => 72f, _ => 26f };
            Vector2 bc = f.RecessPos();
            return new Rect2(bc.X - bw / 2f, bc.Y - bh / 2f, bw, bh);
        }
        // v3.91 关键修正：暗格改为覆盖「整片前脸」，而非内缩小框。
        // 根因：背景贴图本身就把挂画 / 柜门 / 抽屉画在墙上（前脸裁片就是从背景同一处切的），
        // 旧版只挖一个内缩小暗格，翻开后四周会露出背景里那幅「关着的」家具原画
        // —— 正是用户说的「翻开后还是看到挂画 / 柜门」。整片覆盖才能把背景原画遮掉，
        // 露出真正的暗腔。藏匿物坐标仍由 RecessCenterMirrored（内缩框中心 = 整片中心）决定，不受影响。
        return CoverRect(_room.Type, f.Type);
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
        // v3.90 暗格：复用 v3.86 已验收的淡墨软阴影贴图（contact_shadow）以深墨棕压暗采样，
        // 画成「带柔和边缘的凹陷暗腔」，取代旧版的纯色硬边矩形 / 平贴重采样色块
        // （用户与 ViewImage 均判读为「墙上贴了块方块、无凹陷感」）。零新增美术资产。
        if (f.OpenT <= 0.02f) return;
        var r = RecessRect(f);
        float a = Mathf.Clamp(f.OpenT, 0f, 1f);
        const float inset = 0.14f;
        var inner = new Rect2(r.Position + r.Size * inset, r.Size * (1f - 2f * inset));

        // v3.91 关键修正：暗格必须先以「近乎不透明」的暗腔盖住背景里那幅关着的家具原画
        // （背景贴图本身就把挂画 / 柜门 / 抽屉画在墙上，前脸裁片就是从背景同一处切的）。
        // 旧版只用半透明 soft-shadow（中心暗、四周全透）去遮，结果背景原画从边缘 + 半透明处透出来，
        // 用户就会看到「翻开后挂画 / 柜门还在」。
        // 现在：三层嵌套暗矩形做不透明基底（边缘→中心渐不透明，读作凹陷的洞，彻底遮掉背景原画），
        // 再叠 soft-shadow 贴图只做深度暗示，不再承担「遮背景」职责。
        const float cr = 0.10f, cg = 0.07f, cb = 0.05f;
        DrawRect(r,                                   new Color(cr, cg, cb, 0.94f * a));
        DrawRect(new Rect2(r.Position + r.Size * 0.05f, r.Size * 0.90f), new Color(cr, cg, cb, 0.98f * a));
        DrawRect(inner,                              new Color(0.08f, 0.06f, 0.04f, 1.00f * a));
        if (_shadowTex != null)
        {
            // soft-shadow 只做柔腔深度（中心压暗），叠在不透明基底上，绝不靠它去遮背景
            DrawTextureRect(_shadowTex, r,     false, new Color(0.05f, 0.035f, 0.025f, 0.50f * a));
            DrawTextureRect(_shadowTex, inner, false, new Color(0.03f, 0.02f, 0.015f, 0.50f * a));
        }
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

        // v3.91 室内开合重做（用户 2026-11-13：挂画翻开应「卷起、只露暗格」；
        // 柜门翻开应「门可见地旋开、直接看到柜内」；抽屉拉开应「面板抽出、露内腔」，
        // 而非「整块平移移走、藏物再淡入闪现」）。全部用背景裁片做资源（零新增资产），
        // 靠变换表现开合，门/画/抽屉仍是其原本的贴图，只是以正确方式被打开。
        if (_roomTex == null || !FrontSrc.TryGetValue((_room.Type, f.Type), out var src)) return;
        var dst = BgRect(src);
        float ot = f.OpenT;
        bool mirror = _room.Type == RoomType.Residence && _room.Variant != 0;
        var mirrorM = new Transform2D(new Vector2(-1f, 0f), new Vector2(0f, 1f), new Vector2(952f, 0f));

        if (f.Type == FurnitureType.PictureFrame)
        {
            // v3.92 挂画「往上卷」：画心自底部向上卷入，底部卷口（横向卷轴）随卷起上升；
            // ot→1 时整幅卷起、卷轴停在框顶，只留暗格（暗格由 DrawFurnitureBody 已画在底层）。
            // 用户 2026-11-13：「挂画掀开是拨开的意思或者将画往上卷才会看到暗格」。
            if (mirror) DrawSetTransformMatrix(mirrorM);
            float visibleH = dst.Size.Y * (1f - ot);
            float rollH = Mathf.Min(dst.Size.Y * 0.11f, 16f);
            if (visibleH > 1f)
            {
                var dDst = new Rect2(dst.Position.X, dst.Position.Y, dst.Size.X, visibleH);
                var dSrc = new Rect2(src.Position.X, src.Position.Y, src.Size.X, src.Size.Y * (1f - ot));
                DrawTextureRectRegion(_roomTex, dDst, dSrc, new Color(1f, 1f, 1f, 1f));
                // 卷口：在仍可见画心的下缘画一截横向卷轴（矩形+两端圆角），读作「往上卷」
                float cy = dst.Position.Y + visibleH;
                DrawRect(new Rect2(dst.Position.X, cy - rollH * 0.5f, dst.Size.X, rollH),
                         new Color(0.42f, 0.32f, 0.22f, 0.95f));
                DrawCircle(new Vector2(dst.Position.X, cy), rollH * 0.5f, new Color(0.42f, 0.32f, 0.22f, 0.95f));
                DrawCircle(new Vector2(dst.Position.X + dst.Size.X, cy), rollH * 0.5f, new Color(0.42f, 0.32f, 0.22f, 0.95f));
            }
            else
            {
                // 完全卷起：卷轴停在框顶，框内只剩暗格（仍读作「画已卷上去」）
                float cy = dst.Position.Y + rollH * 0.5f;
                DrawRect(new Rect2(dst.Position.X, cy - rollH * 0.5f, dst.Size.X, rollH),
                         new Color(0.42f, 0.32f, 0.22f, 0.95f));
                DrawCircle(new Vector2(dst.Position.X, cy), rollH * 0.5f, new Color(0.42f, 0.32f, 0.22f, 0.95f));
                DrawCircle(new Vector2(dst.Position.X + dst.Size.X, cy), rollH * 0.5f, new Color(0.42f, 0.32f, 0.22f, 0.95f));
            }
            if (mirror) DrawSetTransformMatrix(Transform2D.Identity);
            return;
        }

        if (f.Type == FurnitureType.Cabinet)
        {
            // v3.92 双开门（对开门）：两扇门各自以外侧合页为轴，开启时向两侧「压扁」（水平透视缩短），
            // 露出门后暗腔与藏物；ot→1 时两扇收成门侧窄条、中间暗腔完全露出。
            // 每扇门把「整片半边门面」横向压缩进窄条（而非只取门边），确保左右两扇都看得到木门面；
            // 背景只画了一扇门且右半偏暗，故两扇均取亮的左半源、右扇再水平镜像让亮木纹朝外、左右对称。
            // 参考用户 2026-11-13 所发「中式双开门老柜子」图：两扇门各自向左右翻开、平贴两侧。
            float scale = 1f - ot * 0.86f;            // 1(关) → 0.14(全开，收成门侧窄条)
            float halfDstW = dst.Size.X * 0.5f;
            float wL = halfDstW * scale;
            float wR = halfDstW * scale;
            // 显式设定基础矩阵，避免继承上一件家具遗留的变换状态
            DrawSetTransformMatrix(mirror ? mirrorM : Transform2D.Identity);
            // 左扇：整片左半门面横向压缩进左缘窄条（合页在左）
            {
                Rect2 dstL = new Rect2(dst.Position.X, dst.Position.Y, wL, dst.Size.Y);
                Rect2 srcL = new Rect2(src.Position.X, src.Position.Y, src.Size.X * 0.5f, src.Size.Y);
                DrawTextureRectRegion(_roomTex, dstL, srcL, new Color(1f, 1f, 1f, 1f));
            }
            // 右扇：取亮的左半门面、水平镜像后压缩进右缘窄条，使亮木纹朝外、左右对称
            {
                Rect2 dstR = new Rect2(dst.Position.X + dst.Size.X - wR, dst.Position.Y, wR, dst.Size.Y);
                Rect2 srcR = new Rect2(src.Position.X, src.Position.Y, src.Size.X * 0.5f, src.Size.Y);
                if (!mirror)
                {
                    // 在右窄条内做水平镜像，让亮木纹落在右扇外侧（origin = 2*X + W 使窄条原地镜像）
                    var flip = new Transform2D(new Vector2(-1f, 0f), new Vector2(0f, 1f),
                                               new Vector2(2f * dstR.Position.X + dstR.Size.X, 0f));
                    DrawSetTransformMatrix(flip);
                }
                DrawTextureRectRegion(_roomTex, dstR, srcR, new Color(1f, 1f, 1f, 1f));
            }
            DrawSetTransformMatrix(Transform2D.Identity);
            return;
        }

        if (f.Type == FurnitureType.Desk)
        {
            // 抽屉面板向下抽出，露出柔和的抽屉内腔（接触阴影贴图，不画硬边矩形）；
            // 藏物落在内腔（由中层已画），不再是「移走再淡入」。
            if (mirror) DrawSetTransformMatrix(mirrorM);
            float pull = dst.Size.Y * 1.05f * ot;
            if (_shadowTex != null)
            {
                var cav = new Rect2(dst.Position.X, dst.Position.Y - dst.Size.Y * 0.15f, dst.Size.X, dst.Size.Y * 1.2f);
                DrawTextureRect(_shadowTex, cav, false, new Color(0.16f, 0.12f, 0.09f, 0.7f * ot));
            }
            var dDst = new Rect2(dst.Position.X, dst.Position.Y + pull, dst.Size.X, dst.Size.Y);
            DrawTextureRectRegion(_roomTex, dDst, src, new Color(1f, 1f, 1f, 1f));
            if (mirror) DrawSetTransformMatrix(Transform2D.Identity);
            return;
        }

        // 灶面：保持平移掀开（非门非卷轴），但不淡出，避免「闪现」
        if (mirror) DrawSetTransformMatrix(mirrorM);
        var sDst = new Rect2(dst.Position.X + dst.Size.X * 0.82f * ot, dst.Position.Y, dst.Size.X, dst.Size.Y);
        DrawTextureRectRegion(_roomTex, sDst, src, new Color(1f, 1f, 1f, 1f));
        if (mirror) DrawSetTransformMatrix(Transform2D.Identity);

        // v3.85：删掉「未开且靠近」时画在柜门/画心上的亮黄四角线。
        // 用户要的是「只有鼠标选中才有 E 提示，没选中就没有任何提示」，
        // 且这些亮线压在背景贴图上又是一层格格不入的 UI 标记。
        // 现在唯一的命中反馈就是 RefreshPrompt 的 E 纸条；柜门与挂画本身保持原画。
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

    /// <summary>
    /// 物件绘制核心。origin=绘制中心；reveal=开启淡入(0..1)；floorShadow=true 时阴影画在地面（地落物）。
    /// v3.85：near 参数仅保留签名兼容，不再在物件本体上画任何命中标记 ——
    /// 「选中才有提示」统一由 RefreshPrompt 的 E 纸条承担；微光 / 十字星 / 斜杠 / 四角框全部删除。
    /// </summary>
    private void DrawItemCore(RoomItem it, Vector2 p, bool near, float reveal, bool floorShadow)
    {
        // v3.90：移除「已点击 / 已搜」物品的 0.55 半透明幽灵态（用户：淡化 + 亮光看着怪）。
        // 已搜状态改由文本日志（RefreshResult）承载；物品本体永远以完整不透明度绘制。
        float a = 1f * reveal;
        float seed = StrHash(it.SpotId);

        if (floorShadow)
        {
            float above = InteriorRoom.FloorY - it.Pos.Y;
            float k = Mathf.Clamp(1f - above / 220f, 0.15f, 1f);
            // v3.86：地落接触阴影同样改用淡墨贴图（离地越高越淡越窄）
            if (_shadowTex != null)
            {
                float dw = 44f * (0.45f + 0.55f * k);
                DrawTextureRect(_shadowTex,
                    new Rect2(it.Pos.X - dw / 2f, InteriorRoom.FloorY + 8f, dw, 15f),
                    false, new Color(1f, 1f, 1f, k));
            }
        }
        else
        {
            DrawContactShadow(p, a);
        }

        // v3.85：删掉关键物的三层暖光脉冲。用户要的是「融入背景」，
        // 发光晕圈只会让小件读成 UI 图标 / 贴纸；关键与否改由搜刮后的文本承载。

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
                // v3.85 融入背景：完好房间的小件统一压到与墙面/木器同一暖褐调，
                // 不再用原色 (1,1,1) 直接贴上去；旧纸再压一档，避免白得像 UI 贴片。
                var mod = s.sheet == 2
                    ? new Color(0.70f, 0.64f, 0.55f, a)
                    : new Color(0.86f, 0.79f, 0.68f, a);
                // v3.90：暗格已改为柔和凹陷暗腔，物件直接以正常亮度落在暗腔里即可读清，
                // 不再额外压暗（旧版 0.82 在暗腔里会糊成一团、且让藏物像浮贴的亮块）。
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

        // v3.85 用户原话：「如果鼠标没有选中就没有提示」「看起来融入背景不然格格不入」。
        // 旧版每个未搜物件都常驻一颗脉动亮白十字星、已搜画硬边斜杠、命中再叠 3px 亮白四角框，
        // 三样都是画在物件本体上的 UI 标记 —— 正是「贴纸感」的来源，全部删除。
        // 选中时的唯一反馈是 RefreshPrompt 画的那张 E 提示纸条；物件本身永远只有贴图 + 接触阴影。
        // （已搜物件靠 a = 0.55 自然变暗，不再画斜杠。）

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
        // v3.85：有鼠标指针时准星跟随鼠标（X+Y 都能动），才能真正「指向挂画」；
        // 无指针时回落旧的 A/D 水平扫视。指针模式下不画整条竖直引导线，免得画面读成 UI。
        bool ptr = _room.PointerActive;
        float x = ptr ? _room.Pointer.X : _room.PlayerX;
        float y = ptr ? _room.Pointer.Y : H * 0.50f;
        if (!ptr && _room.NearIndex >= 0 && _room.NearIndex < _room.Items.Count)
            y = _room.Items[_room.NearIndex].Pos.Y;

        if (!ptr)
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
        // v3.86：底部常驻说明已整体删除；这里只负责画「选中时才出现」的 E 纸条。
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
            // v3.85 提示牌贴着指针，不再固定在物件上方一大截
            var anchor = _room.PointerActive ? _room.Pointer : it.Pos;
            pos = new Vector2(Mathf.Clamp(anchor.X - 110f, 90f, W - 240f),
                              Mathf.Clamp(anchor.Y - 72f, 58f, H - 150f));
        }
        else if (_room.NearFurniture >= 0 && _room.NearFurniture < _room.Furniture.Count)
        {
            // v3.84：未开的柜 / 挂画 / 抽屉 / 灶膛，先提示「拉开 / 掀起」，
            // 而不是直接写里面有什么 —— 里面的东西必须翻开后才让玩家知道。
            var f = _room.Furniture[_room.NearFurniture];
            text = $"E · {_room.FurnitureVerb(_room.NearFurniture)}";
            var anchor = _room.PointerActive ? _room.Pointer : f.Center;
            pos = new Vector2(Mathf.Clamp(anchor.X - 110f, 90f, W - 240f),
                              Mathf.Clamp(anchor.Y - 72f, 58f, H - 150f));
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
