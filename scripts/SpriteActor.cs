using Godot;

namespace Witness;

/// <summary>
/// 手绘 sprite 角色（v0.5c：走路动画重写，告别鬼影弹跳）。
/// 
/// 白底 AI 立绘经 ChromaKey 抠图后作为 Sprite2D 挂在角色节点下，脚底为原点。
/// v0.5c 动画重写（参考 VH 风格单帧立绘的"假动画"技巧）：
/// - 走路去掉 |sin| 弹跳，改为 **左右重心转移**（sin(Phase*2) 横向偏移）+ 
///   **微小自然起伏**（sin(Phase) 不是 abs，脚落地时沉一点）+ 身体微转；
/// - 蹲下：整体压扁 + 身体微前倾；
/// - 翻越：向前倾斜（根据 Facing），模拟攀爬姿势；
/// - 影子随步形变形：脚向前时横向压扁拉长。
/// </summary>
public partial class SpriteActor : Node2D
{
    public enum Kind { Vautrin, Soldier, RefugeeWoman, RefugeeMan, Child, Fitch }

    public float Facing = 1f;       // 1 右 / -1 左
    public float Phase;             // 行走相位（外部累加，通常 = 移动距离 × 行走频率）
    public bool Moving;
    public bool Crouched;
    public bool Vaulting;
    public bool Airborne;

    private readonly Sprite2D _sp;

    /// <summary>角色像素身高（VH 比例：约占 720p 画面的 1/7）。</summary>
    public readonly float Height;

    public SpriteActor(Kind kind)
    {
        var (name, height) = kind switch
        {
            Kind.Vautrin => ("vautrin", 104f),
            Kind.Soldier => ("soldier_ija", 108f),
            Kind.RefugeeWoman => ("refugee_woman", 96f),
            Kind.RefugeeMan => ("refugee_man", 100f),
            Kind.Child => ("refugee_child", 68f),
            _ => ("fitch", 104f),
        };
        Height = height;
        var tex = ChromaKey.Cutout($"res://assets/art/sprites/{name}.jpg", 0.03f, 0.05f);
        if (tex == null) return;
        float s = height / tex.GetSize().Y;
        _sp = new Sprite2D
        {
            Texture = tex,
            Scale = new Vector2(s, s),
            Position = new Vector2(0, -height / 2f), // 脚底对齐原点
        };
        AddChild(_sp);
    }

    public override void _Process(double delta)
    {
        if (_sp == null) return;

        // --- VH 风格走路动画：重心转移为主，上下起伏为辅 ---
        // Phase 由外部累加（Player/Patrol 里 Phase += f * 8f 之类），单位约 = 弧度/秒
        float centerX = 0f;    // 身体横向偏移（重心转移）
        float bob = 0f;        // 身体上下偏移（脚落地沉一点）
        float rot = 0f;
        var scale = Vector2.One;

        if (Vaulting)
        {
            rot = -0.38f * Facing;
            bob = -2f;   // 翻越时身体抬高一点
        }
        else if (Crouched)
        {
            rot = 0f;
            scale = new Vector2(1.10f, 0.74f);   // 蹲下压扁，重心稍下
            bob = 2f;
        }
        else if (Airborne)
        {
            rot = -0.12f * Facing;
            bob = -3f;   // 腾空时身体上飘
        }
        else if (Moving)
        {
            // 核心改变：用 sin 不用 abs —— 走路像走路，不是弹跳
            float step = Mathf.Sin(Phase);        // 完整正弦步周期：+/- 交替
            float sway = Mathf.Sin(Phase * 2f);    // 两倍频：重心左右转移

            // 横向重心转移：身体随脚步微摆（模拟双脚交替承重）
            centerX = sway * 3.2f;

            // 上下起伏：脚落地时沉、迈步时稍高（自然步行曲线）
            bob = step * 1.8f;    // 不用 |sin|！沉下去是自然的

            // 身体微转：换重心时身体跟着摆一点点
            rot = sway * 0.045f * Facing;
        }

        // 应用
        Position = new Vector2(centerX, -bob);
        Rotation = rot;
        Scale = scale;
        _sp.FlipH = Facing < 0f;

        QueueRedraw();   // 让影子随动画同步变形
    }

    public override void _Draw()
    {
        // 脚下淡影：走路时影子随步形变形（脚向前时横向压扁拉长，像踩在地上）
        float shadowScaleX, shadowScaleY;
        float shadowRadius = Height * 0.115f;
        if (Moving)
        {
            // 当 sin(Phase) > 0 时脚前伸，影子横向拉长；< 0 时脚落地，影子恢复圆形
            float step = Mathf.Sin(Phase);
            float stretch = Mathf.Max(0.75f, 1.25f + step * 0.5f);   // 0.75 ~ 1.75
            shadowScaleX = stretch;
            shadowScaleY = 1f / stretch * 0.55f;   // 体积守恒
            // 脚前伸时影子稍微向前偏移 1px，增加"推进感"
            DrawSetTransform(new Vector2(-Facing * step * 1.5f, -2), 0,
                new Vector2(shadowScaleX, shadowScaleY));
        }
        else if (Crouched)
        {
            // 蹲下时影子变大、变扁
            shadowScaleX = 1.8f;
            shadowScaleY = 0.35f;
            DrawSetTransform(new Vector2(0, -2), 0,
                new Vector2(shadowScaleX, shadowScaleY));
        }
        else
        {
            // 静止时圆形淡影
            DrawSetTransform(new Vector2(0, -2), 0, new Vector2(2.6f, 0.55f));
        }

        DrawCircle(Vector2.Zero, shadowRadius,
            new Color(0.10f, 0.09f, 0.08f, 0.28f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
