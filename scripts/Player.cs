using Godot;

namespace Witness;

/// <summary>
/// 玩家角色：明妮·魏特琳（真实历史人物，GDD §4.1）。
/// v0.4 方案 B（无跳跃）：A/D 左右移动，E 互动翻越矮墙/瓦砾/窗台（PuzzleLogic.CanVault），
/// S 蹲下潜行（降低被巡逻发现的概率，GDD §6.3）。
/// 翻越是一段受控补间：攀住 → 起身 → 落到对面，期间物理停摆、播攀爬姿态。
/// 卡通形象由 SilhouetteSide 绘制（侧视彩色卡通，脚底原点，FlipH 由 Facing 控制）。
/// </summary>
public partial class Player : CharacterBody2D
{
    private const float WalkSpeed = 230f;
    private const float CrouchSpeed = 105f;
    private const float Gravity = 1500f;

    public bool Sneaking => Input.IsActionPressed("crouch"); // 蹲下 = 潜行（Patrol 侦测读它）
    public bool Crouched => _crouched;
    public bool Airborne => !IsOnFloor() && !Vaulting;
    public bool Vaulting { get; private set; }   // 翻越中（Level 用来屏蔽其它交互）
    public bool Climbing => Vaulting;            // 攀爬姿态（绘制用）
    public float Facing { get; private set; } = 1f;          // 1 右 / -1 左

    /// <summary>推箱减速：被 Level 抓取木箱时设为 CratePushSpeed。</summary>
    public float SpeedOverride = -1f;

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
        if (Vaulting) { QueueRedraw(); return; } // 翻越补间期间物理停摆

        bool wantCrouch = Sneaking;
        var dir = Input.GetAxis("move_left", "move_right");

        // 蹲伏切换（起身后形状延迟切换避免物理帧冲突）
        bool crouch = wantCrouch && IsOnFloor();
        if (crouch != _crouched)
        {
            _crouched = crouch;
            _stand.SetDeferred("disabled", crouch);
            _crouchShape.SetDeferred("disabled", !crouch);
        }

        float speed = SpeedOverride > 0f ? SpeedOverride : (_crouched ? CrouchSpeed : WalkSpeed);
        Velocity = new Vector2(dir * speed, Velocity.Y + Gravity * f);

        if (dir != 0f)
            Facing = Mathf.Sign(dir);

        MoveAndSlide();

        if (IsOnFloor() && Mathf.Abs(Velocity.X) > 1f)
            _phase += f * 11f;
        QueueRedraw();
    }

    /// <summary>
    /// 开始翻越：从当前位置攀上 topY 顶面并落到 landX（受控补间，无物理）。
    /// 由 Level/PrologueLevel 在 E 互动时调用（PuzzleLogic.FindVault 已确认可翻越）。
    /// </summary>
    public void StartVault(float topY, float landX)
    {
        if (Vaulting) return;
        Vaulting = true;
        Velocity = Vector2.Zero;
        _stand.SetDeferred("disabled", true);
        _crouchShape.SetDeferred("disabled", true);

        var from = Position;
        var grip = new Vector2(landX, topY - 52f); // 攀缘最高点（站高 52）
        var tween = CreateTween();
        // 先升起攀住，再平移到落点上方，最后落地 —— 三段读作一次「攀-跨-落」
        tween.TweenProperty(this, "position", new Vector2(from.X, topY - 60f), PuzzleLogic.VaultSeconds * 0.45f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(this, "position", new Vector2(landX, topY - 60f), PuzzleLogic.VaultSeconds * 0.3f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(this, "position", new Vector2(landX, topY), PuzzleLogic.VaultSeconds * 0.25f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(() =>
        {
            _stand.Disabled = false;
            _crouched = false;
            Vaulting = false;
            Velocity = Vector2.Zero;
        }));
        _ = grip; // 保留说明：grip 用于将来加手部攀缘特效
    }

    public override void _Draw()
    {
        // 朝向翻转绘制侧视卡通形象
        DrawSetTransform(new Vector2(0, 1), 0, new Vector2(Facing, 1));
        SilhouetteSide.Draw(this, Silhouette.Kind.Vautrin, _phase, Airborne, _crouched, Vaulting);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        // 蹲下时脚下画一圈淡色涟漪，提示潜行状态（克制，不做成状态条）
        if (Sneaking && !Vaulting)
            DrawArc(new Vector2(0, -2), 12f, 0, Mathf.Tau, 24,
                new Color(0.93f, 0.90f, 0.82f, 0.35f), 1.6f);
    }
}
