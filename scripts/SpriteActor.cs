using Godot;

namespace Witness;

/// <summary>
/// 手绘 sprite 角色（v0.5：替代程序几何假人 SilhouetteSide）。
/// 白底 AI 立绘经 ChromaKey 抠图后作为 Sprite2D 挂在角色节点下，脚底为原点：
/// - 走路时身体轻微起伏与摇摆（bob / sway）；
/// - 蹲下时整体压扁；翻越时向朝向倾斜；朝向翻转用 FlipH；
/// - 脚下画一团淡影，避免「贴纸浮空」感。
/// </summary>
public partial class SpriteActor : Node2D
{
    public enum Kind { Vautrin, Soldier, RefugeeWoman, RefugeeMan, Child, Fitch }

    public float Facing = 1f;       // 1 右 / -1 左
    public float Phase;             // 行走相位（外部累加）
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
        float bob = Moving ? Mathf.Abs(Mathf.Sin(Phase)) * 2.6f : 0f;
        float rot;
        var scale = Vector2.One;
        if (Vaulting) { rot = -0.38f * Facing; }
        else if (Crouched) { rot = 0f; scale = new Vector2(1.08f, 0.76f); }
        else if (Airborne) { rot = -0.12f * Facing; }
        else { rot = Moving ? Mathf.Sin(Phase * 2f) * 0.035f : 0f; }
        Position = new Vector2(0, -bob);
        Rotation = rot;
        Scale = scale;
        _sp.FlipH = Facing < 0f;
    }

    public override void _Draw()
    {
        // 脚下淡影（横向压扁的圆，随身高缩放）
        DrawSetTransform(new Vector2(0, -2), 0, new Vector2(2.6f, 0.55f));
        DrawCircle(Vector2.Zero, Height * 0.115f, new Color(0.10f, 0.09f, 0.08f, 0.28f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
