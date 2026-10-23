using Godot;

namespace Witness;

/// <summary>
/// 手绘 sprite 角色（v1.0：史实立绘 + 色相 shader + 兼容 FigureActor API）。
/// 
/// 白底 AI 立绘经 ChromaKey 抠图后作为 Sprite2D 挂在角色节点下，脚底为原点。
/// 角色按阵营使用不同立绘：
///   • Kind.Soldier       → soldier_ija.jpg（史实关东军卡其军服，原色）
///   • Kind.NortheastArmy → soldier_na.jpg（色相旋转后的东北军灰蓝棉军装）
///   • 其他难民/平民立绘
///
/// 动画（VH 风格单帧立绘的"假动画"技巧）：
/// - 走路：微幅重心转移 + 脚下影子随步形变形
/// - 蹲下：整体压扁 + 身体微前倾
/// - 跳跃：身体旋转 + 位置上飘
/// - 瞄准：身体微前倾
/// - 枪口火光 / 受击闪白：程序化瞬时效果
/// </summary>
public partial class SpriteActor : Node2D
{
    public enum Kind { Vautrin, Soldier, NortheastArmy, RefugeeWoman, RefugeeMan, Child, Fitch }

    public float Facing = 1f;
    public bool FacingRight { get => Facing > 0f; set => Facing = value ? 1f : -1f; }

    public float Phase;
    public float Speed;
    public bool Grounded = true;
    public bool Crouching { get => Crouched; set => Crouched = value; }
    public bool Crouched;
    public bool Vaulting;
    public bool Airborne;
    public bool Moving;

    public float Aim;
    public float MuzzleFlash;
    public float HurtFlash;

    private readonly Sprite2D _sp;

    public readonly float Height;

    public SpriteActor(Kind kind)
    {
        var (name, height, white) = kind switch
        {
            Kind.Vautrin => ("vautrin", 104f, false),
            Kind.Soldier => ("soldier_ija", 108f, false),
            Kind.NortheastArmy => ("soldier_na", 108f, false),  // 色相旋转后的东北军立绘
            Kind.RefugeeWoman => ("refugee_woman", 96f, false),
            Kind.RefugeeMan => ("refugee_man", 100f, false),
            Kind.Child => ("refugee_child", 68f, false),
            _ => ("fitch", 104f, false),
        };
        Height = height;

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
        AddChild(_sp);
    }

    public override void _Process(double delta)
    {
        if (_sp == null) return;
        float f = (float)delta;

        if (MuzzleFlash > 0f) MuzzleFlash = Mathf.Max(0f, MuzzleFlash - f * 9f);
        if (HurtFlash > 0f) HurtFlash = Mathf.Max(0f, HurtFlash - f * 3.2f);

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
            rot = -0.08f * Facing;
            bob = 1f;
        }
        else if (Moving && Grounded)
        {
            float step = Mathf.Sin(Phase * 2f);
            centerX = step * 0.6f * Facing;
            bob = -Mathf.Abs(step) * 0.8f;
        }

        Position = new Vector2(centerX, -bob);
        Rotation = rot;
        Scale = scale;
        _sp.FlipH = Facing < 0f;

        QueueRedraw();
    }

    public override void _Draw()
    {
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

        if (Aim > 0.5f && _sp != null)
        {
            var muzzle = new Vector2(Facing * (Height * 0.45f), -Height * 0.55f);
            DrawLine(muzzle, muzzle + new Vector2(Facing * 14f, -2f),
                InkStyle.InkColor, 1.5f, true);
        }

        if (MuzzleFlash > 0.05f)
        {
            var muzzle = new Vector2(Facing * (Height * 0.45f), -Height * 0.55f);
            float r = 4f + MuzzleFlash * 5f;
            DrawCircle(muzzle, r, new Color(1f, 0.85f, 0.30f, MuzzleFlash * 0.9f));
            DrawCircle(muzzle, r * 0.55f, new Color(1f, 1f, 0.6f, MuzzleFlash));
        }

        // 受击闪白（程序化 modulate）
        if (_sp != null)
            _sp.Modulate = new Color(1f, 1f, 1f, 1f).Lerp(new Color(1f, 0.88f, 0.88f, 1f), HurtFlash * 0.9f);
    }
}
