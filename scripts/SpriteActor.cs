using Godot;

namespace Witness;

/// <summary>
/// 手绘 sprite 角色（v0.9：兼容 FigureActor API，全功能替代）。
/// 
/// 白底 AI 立绘经 ChromaKey 抠图后作为 Sprite2D 挂在角色节点下，脚底为原点。
/// 
/// 动画（VH 风格单帧立绘的"假动画"技巧）：
/// - 走路：身体静止 + 脚下影子随步形变形（脚前伸时横向拉长）
/// - 蹲下：整体压扁 + 身体微前倾
/// - 跳跃：身体旋转 + 位置上飘
/// - 瞄准：身体微前倾 + 枪口方向线
/// - 枪口火光 / 受击闪白：程序化瞬时效果
/// 
/// 阵营区分：
/// - Kind.Soldier → 直接用 soldier_ija.jpg（史实关东军卡其军服，原色）
/// - Kind.NortheastArmy → 同一立绘 + 蓝色 modulation 滤镜（伪装东北军灰蓝棉军装）
/// - Kind.RefugeeWoman/Man/Child → 直接用对应照片立绘
/// </summary>
public partial class SpriteActor : Node2D
{
    public enum Kind { Vautrin, Soldier, NortheastArmy, RefugeeWoman, RefugeeMan, Child, Fitch }

    /// <summary>1 = 右 / -1 = 左（外部 EnemySoldier/FieldPlayer 读这个）。</summary>
    public float Facing = 1f;
    public bool FacingRight { get => Facing > 0f; set => Facing = value ? 1f : -1f; }

    public float Phase;          // 行走相位（由外部累加）
    public float Speed;          // 水平速度（外部设定，SpriteActor 只用来判断 Moving）
    public bool Grounded = true; // 外部状态传递
    public bool Crouching { get => Crouched; set => Crouched = value; }
    public bool Crouched;
    public bool Vaulting;
    public bool Airborne;
    public bool Moving;

    public float Aim;          // 0 = 持枪下垂，1 = 举枪瞄准
    public float MuzzleFlash;  // 开火瞬间置 1，自行衰减
    public float HurtFlash;    // 受击瞬间置 1，自行衰减

    private readonly Sprite2D _sp;
    private readonly bool _blueTint;  // 东北军蓝色滤镜

    /// <summary>角色像素身高（VH 比例：约占 720p 画面的 1/7）。</summary>
    public readonly float Height;

    public SpriteActor(Kind kind)
    {
        var (name, height, blue) = kind switch
        {
            Kind.Vautrin => ("vautrin", 104f, false),
            Kind.Soldier => ("soldier_ija", 108f, false),
            Kind.NortheastArmy => ("soldier_ija", 108f, true),
            Kind.RefugeeWoman => ("refugee_woman", 96f, false),
            Kind.RefugeeMan => ("refugee_man", 100f, false),
            Kind.Child => ("refugee_child", 68f, false),
            _ => ("fitch", 104f, false),
        };
        Height = height;
        _blueTint = blue;

        var tex = ChromaKey.Cutout($"res://assets/art/sprites/{name}.jpg", 0.03f, 0.05f);
        if (tex == null) return;

        float s = height / tex.GetSize().Y;
        _sp = new Sprite2D
        {
            Texture = tex,
            Scale = new Vector2(s, s),
            Position = new Vector2(0, -height / 2f),
            FlipH = Facing < 0f,
        };
        if (_blueTint)
            _sp.Modulate = new Color(0.55f, 0.62f, 0.72f, 1f);  // 蓝灰色调，伪装东北军

        AddChild(_sp);
    }

    public override void _Process(double delta)
    {
        if (_sp == null) return;
        float f = (float)delta;

        // —— 瞬时效果衰减 ——
        if (MuzzleFlash > 0f) MuzzleFlash = Mathf.Max(0f, MuzzleFlash - f * 9f);
        if (HurtFlash > 0f) HurtFlash = Mathf.Max(0f, HurtFlash - f * 3.2f);

        // —— 状态 → 动画参数 ——
        float centerX = 0f;
        float bob = 0f;
        float rot = 0f;
        var scale = Vector2.One;

        if (Vaulting)
        {
            rot = -0.38f * Facing;
            bob = -2f;
        }
        else if (Crouched)
        {
            rot = 0f;
            scale = new Vector2(1.10f, 0.74f);
            bob = 2f;
        }
        else if (Airborne)
        {
            rot = -0.12f * Facing;
            bob = -3f;
        }
        else if (Aim > 0.5f)
        {
            // 瞄准：身体微前倾
            rot = -0.08f * Facing;
            bob = 1f;
        }
        else if (Moving && Grounded)
        {
            // v0.9：行走时身体有极其微小的重心转移（不破坏立绘稳定性）
            float step = Mathf.Sin(Phase * 2f);
            centerX = step * 0.6f * Facing;
            bob = -Mathf.Abs(step) * 0.8f;  // 脚落地时略沉
        }

        // 应用
        Position = new Vector2(centerX, -bob);
        Rotation = rot;
        Scale = scale;
        _sp.FlipH = Facing < 0f;

        QueueRedraw();
    }

    public override void _Draw()
    {
        // —— 脚下接触阴影 ——
        float shadowScaleX, shadowScaleY;
        float shadowRadius = Height * 0.115f;
        if (Crouched)
        {
            shadowScaleX = 1.8f;
            shadowScaleY = 0.35f;
            DrawSetTransform(new Vector2(0, -2), 0,
                new Vector2(shadowScaleX, shadowScaleY));
        }
        else if (Moving && Grounded)
        {
            float step = Mathf.Sin(Phase);
            float stretch = Mathf.Max(0.75f, 1.25f + step * 0.5f);
            shadowScaleX = stretch;
            shadowScaleY = 1f / stretch * 0.55f;
            DrawSetTransform(new Vector2(-Facing * step * 1.5f, -2), 0,
                new Vector2(shadowScaleX, shadowScaleY));
        }
        else
        {
            DrawSetTransform(new Vector2(0, -2), 0, new Vector2(2.6f, 0.55f));
        }
        DrawCircle(Vector2.Zero, shadowRadius,
            new Color(0.10f, 0.09f, 0.08f, 0.28f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        // —— 受击闪白 ——
        if (HurtFlash > 0.05f && _sp != null)
            _sp.Modulate = new Color(1f, 1f, 1f, HurtFlash) * (_blueTint ? new Color(0.55f, 0.62f, 0.72f) : Colors.White);

        // —— 瞄准方向线（提示玩家角色在瞄准哪里）——
        if (Aim > 0.5f && _sp != null)
        {
            var muzzle = new Vector2(Facing * (Height * 0.45f), -Height * 0.55f);
            // 画一条细墨线从枪口向右延伸（指示瞄准方向）
            DrawLine(muzzle, muzzle + new Vector2(Facing * 14f, -2f),
                InkStyle.InkColor, 1.5f, true);
        }

        // —— 枪口火光（开火瞬间）——
        if (MuzzleFlash > 0.05f)
        {
            var muzzle = new Vector2(Facing * (Height * 0.45f), -Height * 0.55f);
            float r = 4f + MuzzleFlash * 5f;
            DrawCircle(muzzle, r, new Color(1f, 0.85f, 0.30f, MuzzleFlash * 0.9f));
            DrawCircle(muzzle, r * 0.55f, new Color(1f, 1f, 0.6f, MuzzleFlash));
            DrawArc(muzzle, r + 1f, 0f, Mathf.Tau, 8,
                new Color(0.9f, 0.5f, 0.15f, MuzzleFlash), 1f, true);
        }
    }
}
