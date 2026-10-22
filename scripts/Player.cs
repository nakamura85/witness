using Godot;

namespace Witness;

/// <summary>
/// 玩家角色：明妮·魏特琳（真实历史人物，GDD §4.1）。
/// v0.3 横版（side-scroll）：A/D 左右移动，Space/W 跳跃翻越障碍，
/// S 蹲下潜行（降低被巡逻发现的概率，GDD §6.3）。
/// 剪影由 SilhouetteSide 绘制（侧视，脚底原点，FlipH 由 Facing 控制）。
/// </summary>
public partial class Player : CharacterBody2D
{
    private const float WalkSpeed = 230f;
    private const float CrouchSpeed = 105f;
    private const float Gravity = 1500f;
    private const float JumpVel = -560f;   // 跳高约 104px

    public bool Sneaking => Input.IsActionPressed("crouch"); // 蹲下 = 潜行（Patrol 侦测读它）
    public bool Crouched => _crouched;
    public bool Airborne => !IsOnFloor();
    public float Facing { get; private set; } = 1f;          // 1 右 / -1 左

    private bool _crouched;
    private float _phase; // 行走摆腿相位
    private CollisionShape2D _stand, _crouchShape;

    public Player()
    {
        _stand = new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(26, 52) },
            Position = new Vector2(0, -26),
        };
        _crouchShape = new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(26, 30) },
            Position = new Vector2(0, -15),
        };
        _crouchShape.Disabled = true;
        AddChild(_stand);
        AddChild(_crouchShape);
        CollisionLayer = 2;
        CollisionMask = 1; // 只与地形碰撞
    }

    public override void _PhysicsProcess(double delta)
    {
        float f = (float)delta;
        bool wantCrouch = Sneaking;

        var dir = Input.GetAxis("move_left", "move_right");

        // 起跳（蹲下时不起跳）
        if (IsOnFloor() && !_crouched && Input.IsActionJustPressed("jump"))
            Velocity = new Vector2(Velocity.X, JumpVel);

        // 蹲伏切换（空中不蹲；起身后形状延迟切换避免物理帧冲突）
        bool crouch = wantCrouch && IsOnFloor();
        if (crouch != _crouched)
        {
            _crouched = crouch;
            _stand.SetDeferred("disabled", crouch);
            _crouchShape.SetDeferred("disabled", !crouch);
        }

        Velocity = new Vector2(
            dir * (_crouched ? CrouchSpeed : WalkSpeed),
            Velocity.Y + Gravity * f);

        if (dir != 0f)
            Facing = Mathf.Sign(dir);

        MoveAndSlide();

        if (IsOnFloor() && Mathf.Abs(Velocity.X) > 1f)
            _phase += f * 11f;
        QueueRedraw();
    }

    public override void _Draw()
    {
        // 朝向翻转绘制侧视剪影
        DrawSetTransform(new Vector2(0, 1), 0, new Vector2(Facing, 1));
        SilhouetteSide.Draw(this, Silhouette.Kind.Vautrin, _phase, Airborne, _crouched);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        // 蹲下时脚下画一圈淡墨涟漪，提示潜行状态（克制，不做成状态条）
        if (Sneaking)
            DrawArc(new Vector2(0, -2), 12f, 0, Mathf.Tau, 24,
                new Color(0.91f, 0.86f, 0.75f, 0.35f), 1.6f);
    }
}
