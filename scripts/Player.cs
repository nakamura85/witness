using Godot;

namespace Witness;

/// <summary>
/// 玩家角色：明妮·魏特琳（真实历史人物，方案A —— GDD §4.1）。
/// CharacterBody2D 俯视角移动；按住 Shift 屏息（降低被巡逻发现的概率，GDD §6.3）。
/// 剪影由 Silhouette 绘制。
/// </summary>
public partial class Player : CharacterBody2D
{
    private const float WalkSpeed = 230f;
    private const float SneakSpeed = 115f;

    /// <summary>是否正在屏息（影响巡逻发现速度与移动速度）。</summary>
    public bool Sneaking => Input.IsActionPressed("sneak");

    public Player()
    {
        var shape = new CollisionShape2D();
        var circle = new CircleShape2D();
        circle.Radius = 14f;
        shape.Shape = circle;
        AddChild(shape);
        CollisionLayer = 2;
        CollisionMask = 1; // 只与墙体碰撞
    }

    public override void _PhysicsProcess(double delta)
    {
        var dir = Input.GetVector("move_left", "move_right", "move_up", "move_down");
        Velocity = dir * (Sneaking ? SneakSpeed : WalkSpeed);
        MoveAndSlide();
        QueueRedraw();
    }

    public override void _Draw()
    {
        Silhouette.Draw(this, Silhouette.Kind.Vautrin);
        // 屏息时脚下画一圈淡墨涟漪，提示状态（克制，不做成状态条）
        if (Sneaking)
            DrawArc(new Vector2(0, 18), 12f, 0, Mathf.Tau, 24,
                new Color(0.91f, 0.86f, 0.75f, 0.35f), 1.6f);
    }
}
