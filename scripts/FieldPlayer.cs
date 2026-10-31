using System;
using Godot;

namespace Witness;

/// <summary>
/// 嫩江江桥关卡的玩家：东北军通信兵。
/// CharacterBody2D + 真实重力/碰撞；角色本体是 SpriteActor 的史实立绘。
/// </summary>
public partial class FieldPlayer : CharacterBody2D
{
    public event Action<Vector2, bool> Fired;   // 枪口世界坐标、朝向
    public event Action Hit;
    public event Action Down;
    public event Action Landed;
    public event Action DryFired;               // trigger pulled, no rounds left

    public SpriteActor Figure { get; private set; }
    public const int HpMax = 5;
    public int Hp = HpMax;
    public bool ControlLocked;

    // ammo economy: belt rounds run out, loot rifles from the dead to keep fighting
    public const int AmmoCap = 15;
    public int Ammo = 15;
    public int AmmoMax = AmmoCap;   // belt never grows past 15; spent rounds stay on the HUD as dim shells
    public const int GrenadeCap = 5;
    public int Grenades = 1;

    /// <summary>bandage / medkit healing, clamped at max</summary>
    public void Heal(int n) => Hp = Mathf.Min(HpMax, Hp + Mathf.Max(0, n));

    public bool Wounded => Hp < HpMax;

    /// <summary>returns rounds actually taken, bag full means fewer</summary>
    public int AddAmmo(int n)
    {
        int took = Mathf.Min(n, AmmoCap - Ammo);
        Ammo += took;
        return took;
    }

    public void AddGrenade(int n) => Grenades = Mathf.Min(GrenadeCap, Grenades + n);

    private float _facing = 1f;
    private float _coyote;
    private float _jumpBuffer;
    private float _shootCd;
    private float _aimTimer;
    private float _invuln;
    private bool _wasOnFloor = true;

    // v3.13 手感：移动速度不再硬切。站着→蹲下→趴下之间用一个 0.14 秒的
    // 过渡量把速度接起来，先慢下来再压低，读起来像"沉下去"，
    // 而不是一按键速度从 232 直接掉到 96/55（立绘本身仍是即时切换）。
    private float _poseBlend;

    private const float RunSpeed = 232f;
    /// <summary>v3.53: set by the level while the player is tucked behind a
    /// sandbag wall (E). Forces the crouch pose; the level clears it when the
    /// player presses E again, moves, or the wall gets blown up.</summary>
    public bool CoverHold { get; set; }

    private const float CrouchSpeed = 96f;
    private const float ProneSpeed = 55f;
    private const float Gravity = 1980f;
    private const float JumpVel = -610f;
    // 顶点悬停：接近最高点时重力变轻，跳跃高度与滞空更好控
    private const float ApexBand = 120f;
    private const float ApexGravity = 0.62f;

    public override void _Ready()
    {
        MotionMode = MotionModeEnum.Grounded;
        FloorSnapLength = 8f;
        UpDirection = Vector2.Up;

        var shape = new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(22, 54) },
            Position = new Vector2(0, -27),
        };
        AddChild(shape);

        Figure = new SpriteActor(SpriteActor.Kind.NortheastArmy);
        AddChild(Figure);
    }

    public override void _PhysicsProcess(double delta)
    {
        float f = (float)delta;
        bool onFloor = IsOnFloor();
        if (!_wasOnFloor && onFloor && Velocity.Y > 60f) Landed?.Invoke();
        _wasOnFloor = onFloor;

        if (_shootCd > 0f) _shootCd -= f;
        if (_aimTimer > 0f) _aimTimer -= f;
        if (_invuln > 0f) _invuln -= f;
        if (onFloor) _coyote = 0.12f;
        else _coyote = Mathf.Max(0f, _coyote - f);
        if (_jumpBuffer > 0f) _jumpBuffer -= f;

        bool wantProne = !ControlLocked && Input.IsActionPressed("prone") && onFloor;
        // v3.53: CoverHold (sandbag interact) forces the crouch pose
        bool crouch = (!ControlLocked && Input.IsActionPressed("crouch") && onFloor && !wantProne)
                      || (!ControlLocked && CoverHold);
        float input = ControlLocked ? 0f : Input.GetAxis("move_left", "move_right");

        // v3.51: facing follows input while CROUCHED too - before this the
        // facing was locked once crouched (`&& !crouch`), so crouch-walking
        // backwards slid the pose away tail-first with no mirrored side
        // (user report 2026-10-31). The aiming branch below still wins while
        // the mouse aims, so combat facing is unaffected.
        if (Mathf.Abs(input) > 0.05f)
            _facing = Mathf.Sign(input);

        // 瞄准：按住右键，或刚开过枪
        bool aiming = !ControlLocked
            && (Input.IsMouseButtonPressed(MouseButton.Right) || _aimTimer > 0f);
        if (aiming)
        {
            float mx = GetGlobalMousePosition().X - GlobalPosition.X;
            if (Mathf.Abs(mx) > 24f) _facing = Mathf.Sign(mx);
        }

        // 姿势过渡：速度先跟着姿势慢慢变，不要一按键就"啪"地换速度
        float poseTarget = wantProne ? 2f : (crouch ? 1f : 0f);
        _poseBlend = Mathf.MoveToward(_poseBlend, poseTarget, f * 7.2f);
        float poseSpeed = Mathf.Lerp(RunSpeed, CrouchSpeed, Mathf.Clamp(_poseBlend, 0f, 1f));
        poseSpeed = Mathf.Lerp(poseSpeed, ProneSpeed, Mathf.Clamp(_poseBlend - 1f, 0f, 1f));

        float target = input * poseSpeed;
        float accel = onFloor ? 1900f : 1100f;
        if (Mathf.Abs(input) < 0.05f || crouch)
            accel = onFloor ? 2600f : 700f;
        // 转身时脚下要"刹"一下，不然反向输入像滑冰
        if (Mathf.Abs(input) > 0.05f && Mathf.Abs(Velocity.X) > 40f
            && Mathf.Sign(input) != Mathf.Sign(Velocity.X))
            accel *= 0.62f;
        Velocity = new Vector2(Mathf.MoveToward(Velocity.X, target, accel * f), Velocity.Y);

        // 跳跃（带 coyote 与输入缓冲，落地/起跳都实在）
        if (!ControlLocked && !wantProne && Input.IsActionJustPressed("advance"))
            _jumpBuffer = 0.12f;
        if (_jumpBuffer > 0f && _coyote > 0f)
        {
            Velocity = new Vector2(Velocity.X, JumpVel);
            _jumpBuffer = 0f;
            _coyote = 0f;
        }
        // 松键短跳：以前乘 0.90，轻点一下也几乎跳满，等于没有"小跳"
        if (!Input.IsActionPressed("advance") && Velocity.Y < -140f)
            Velocity = new Vector2(Velocity.X, Velocity.Y * 0.55f);

        // 顶点悬停：快到最高点时重力减一档，跳起来更"飘"也更好控高度
        float g = Gravity;
        if (Velocity.Y < 0f && Velocity.Y > -ApexBand) g *= ApexGravity;
        Velocity += new Vector2(0, g * f);
        MoveAndSlide();

        if (GlobalPosition.Y > 900f)
            GlobalPosition = new Vector2(GlobalPosition.X, 600f);

        // —— 动画状态 ——
        float hs = Mathf.Abs(Velocity.X);
        Figure.Speed = onFloor ? hs : 0f;
        Figure.Grounded = onFloor;
        Figure.Crouching = crouch;
        Figure.Prone = wantProne;
        Figure.FacingRight = _facing > 0f;
        // v3.56: tucked behind sandbags the soldier keeps the rifle shouldered
        // (crouch-aim art once it lands) instead of loosely cradling it.
        Figure.Aim = (aiming || CoverHold) ? 1f : 0f;
        if (onFloor && hs > 6f)
            Figure.Phase += hs * f * 0.095f;
        else if (onFloor)
            Figure.Phase = Mathf.Lerp(Figure.Phase, 0f, f * 5f);

        // 开火
        if (!ControlLocked && Input.IsMouseButtonPressed(MouseButton.Left) && _shootCd <= 0f)
            Fire();
    }

    private void Fire(float forcedFacing = 0f)
    {
        if (Ammo <= 0)
        {
            // bolt clicks on an empty chamber
            _shootCd = 0.45f;
            DryFired?.Invoke();
            return;
        }
        Ammo--;
        _shootCd = 0.62f;
        // v3.57: aim window must OUTLAST the fire cooldown (0.62s) - at 0.55s
        // the pose flipped aim<->cradle between every shot of a burst, which
        // read as "never aiming". 0.9s holds the shouldered pose through.
        _aimTimer = 0.9f;
        Figure.MuzzleFlash = 1f;
        Figure.Aim = 1f;
        Figure.Recoil = 1f;   // 躯干后仰 + 后移（SpriteActor 消费）
        float mx = GetGlobalMousePosition().X - GlobalPosition.X;
        if (forcedFacing != 0f) _facing = forcedFacing > 0f ? 1f : -1f;
        else if (Mathf.Abs(mx) > 20f) _facing = Mathf.Sign(mx);
        Figure.FacingRight = _facing > 0f;

        // 弹道起点直接取专姿立绘的枪口锚点（与画面上的枪管尖完全对齐）
        var origin = Figure.MuzzleWorld();
        Fired?.Invoke(origin, _facing > 0f);

        // 后坐力：站姿略退，蹲姿更稳
        Velocity = new Vector2(Velocity.X - _facing * (Figure.Crouching ? 26f : 52f), Velocity.Y);
    }

    /// <summary>供自动化截图 / 测试触发一次开火（可指定朝向，避开无鼠标时的朝向抖动）。</summary>
    public void ForceFire(float face = 1f) => Fire(face);

    public void TakeHit(float fromX)
    {
        if (_invuln > 0f || Hp <= 0) return;
        _invuln = 1.15f;
        Hp--;
        Figure.HurtFlash = 1f;
        float away = GlobalPosition.X < fromX ? -1f : 1f;
        Velocity = new Vector2(away * 190f, -230f);
        Hit?.Invoke();
        if (Hp <= 0) Down?.Invoke();
    }

    /// <summary>v3.60: 爆炸超压伤害——一次扣多颗心。手雷的冲击波不是
    /// 逐步消耗：一颗雷炸到就是重伤（重创带 2~3 心 → 半血残血），击退
    /// 力度也比中弹大（整个人被掀起来）。</summary>
    public void TakeBlast(int hearts, float fromX)
    {
        if (_invuln > 0f || Hp <= 0) return;
        _invuln = 1.5f;
        Hp = Mathf.Max(0, Hp - Mathf.Max(1, hearts));
        Figure.HurtFlash = 1f;
        float away = GlobalPosition.X < fromX ? -1f : 1f;
        Velocity = new Vector2(away * 260f, -300f);   // 冲击波掀翻比子弹更狠
        Hit?.Invoke();
        if (Hp <= 0) Down?.Invoke();
    }

    public void ResetAt(Vector2 pos)
    {
        GlobalPosition = pos;
        Velocity = Vector2.Zero;
        Hp = HpMax;
        _invuln = 1.2f;
    }

    public float Facing => _facing;
}
