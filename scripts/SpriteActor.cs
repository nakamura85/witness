using Godot;

namespace Witness;

/// <summary>
/// 手绘 sprite 角色（v1.2：纸娃娃分层动画 + 子容器翻转）。
///
/// 白底 AI 立绘经 ChromaKey 抠图后，按髋部分成三片：
///   • 后腿（纹理左半下半身）→ 躯干（上半身，盖住髋部接缝）→ 前腿（右半下半身）
/// 走路时两腿以髋为轴剪刀式摆动、躯干随步伐起伏 + 前倾；
/// 跳跃收腿、落地压腿缓冲、开枪躯干后仰；
/// Facing<0 时用子 Node2D 容器 Scale.X=-1 整体翻转（比 FlipH 更安全，
/// 不会在 Centered=false 的 Sprite2D 上产生位置偏移）。
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
    /// <summary>开枪后坐量 1→0，躯干短暂后仰 + 后移。</summary>
    public float Recoil;

    // 翻转子容器：所有立绘 Sprite2D 作为其子节点
    // Facing<0 时 Scale.X=-1 整体水平镜像，比 FlipH 更安全
    private readonly Node2D _flip = new();
    private Sprite2D _torso, _legBack, _legFront;
    private ShaderMaterial _mat;
    private bool _wasGrounded = true;
    private float _landT;             // 落地缓冲计时 1→0
    private float _s;                 // 立绘统一缩放（height/texH）
    private float _hipY, _legTopScreen, _legR, _overlapScreen;

    public readonly float Height;

    public SpriteActor(Kind kind)
    {
        var (name, height, white) = kind switch
        {
            Kind.Vautrin => ("vautrin", 104f, false),
            Kind.Soldier => ("soldier_ija", 108f, false),
            Kind.NortheastArmy => ("soldier_na", 108f, false),  // 原始东北军立绘
            Kind.RefugeeWoman => ("refugee_woman", 96f, false),
            Kind.RefugeeMan => ("refugee_man", 100f, false),
            Kind.Child => ("refugee_child", 68f, false),
            _ => ("fitch", 104f, false),
        };
        Height = height;

        var tex = ChromaKey.Cutout($"res://assets/art/sprites/{name}.jpg", 0.03f, 0.05f);
        if (tex == null) return;

        // 仅在需要色相旋转时才创建 shader（默认 hue_offset=0 跳过 HSV round-trip）
        var shader = GD.Load<Shader>("res://shaders/hue_shift.gdshader");
        if (shader != null)
        {
            _mat = new ShaderMaterial { Shader = shader };
            _mat.SetShaderParameter("hue_offset", 0f);
            _mat.SetShaderParameter("saturation", 1.0f);
        }

        var size = tex.GetSize();
        float texW = size.X, texH = size.Y;
        _s = height / texH;
        float s = _s;

        // —— 髋部分层：髋线取身高 58%，躯干下摆多画一点盖住腿旋转时露出的接缝 ——
        float hipFromTop = texH * 0.58f;
        float overlap = texH * 0.05f;
        _overlapScreen = overlap * s;
        _hipY = -(texH - hipFromTop) * s;                 // 髋线（脚底原点）
        _legTopScreen = hipFromTop - overlap;             // 腿片区域顶部（纹理坐标）
        _legR = (texH - hipFromTop) * s + _overlapScreen; // 腿片绘制高度（屏幕坐标）

        _legBack = MakeLeg(tex, 0f, texW * 0.5f, s);
        _legFront = MakeLeg(tex, texW * 0.5f, texW * 0.5f, s);

        _torso = new Sprite2D
        {
            Texture = tex,
            Centered = false,
            RegionEnabled = true,
            RegionRect = new Rect2(0, 0, texW, hipFromTop + overlap),
            Scale = new Vector2(s, s),
            Position = new Vector2(0, -texH * s),
        };
        _torso.Material = _mat;

        // 立绘子容器：所有 Sprite2D 都 AddChild 到 _flip
        AddChild(_flip);
        _flip.AddChild(_legBack);
        _flip.AddChild(_torso);
        _flip.AddChild(_legFront);
    }

    private Sprite2D MakeLeg(Godot.Texture2D tex, float x0, float w, float s)
    {
        var leg = new Sprite2D
        {
            Texture = tex,
            Centered = false,
            RegionEnabled = true,
            RegionRect = new Rect2(x0, _legTopScreen, w, _legR / s),
            Scale = new Vector2(s, s),
            // 旋转轴=髋：Position 放在髋线上
            Position = new Vector2(x0 * s, -_legR),
        };
        leg.Material = _mat;
        return leg;
    }

    public override void _Process(double delta)
    {
        if (_torso == null) return;
        float f = (float)delta;

        if (MuzzleFlash > 0f) MuzzleFlash = Mathf.Max(0f, MuzzleFlash - f * 9f);
        if (HurtFlash > 0f) HurtFlash = Mathf.Max(0f, HurtFlash - f * 3.2f);
        if (Recoil > 0f) Recoil = Mathf.Max(0f, Recoil - f * 5.5f);

        // 落地缓冲：由腾空转落地触发一次
        bool groundedNow = Grounded && !Airborne;
        if (!_wasGrounded && groundedNow) _landT = 1f;
        _wasGrounded = groundedNow;
        if (_landT > 0f) _landT = Mathf.Max(0f, _landT - f * 5.5f);

        bool air = Airborne || !Grounded;
        bool standingWalk = !air && !Vaulting && !Crouched && (Moving || Speed > 2f);
        bool crouchWalk = !air && !Vaulting && Crouched && (Moving || Speed > 2f);

        // —— 翻转：子容器 Scale.X = FacingRight ? 1 : -1 ——
        // 比 Sprite2D.FlipH 更安全（FlipH 在 Centered=false 上会产生位置偏移）
        _flip.Scale = new Vector2(FacingRight ? 1f : -1f, 1f);

        // —— 腿：剪刀步 / 跳跃收腿 / 蹲姿折叠 ——
        float legK = 1f; // 腿的纵向压缩（蹲/落地缓冲时 <1，脚底要贴回地面）
        float swing = Mathf.Sin(Phase);
        float frontRot = 0f, backRot = 0f;
        if (standingWalk)
        {
            frontRot = -swing * 0.36f * Facing;   // 前腿向前摆（翻转容器会镜像方向，这里乘以 Facing 补偿）
            backRot = swing * 0.36f * Facing;
        }
        else if (crouchWalk)
        {
            // 蹲走：腿小幅度摆动（不像站姿剪刀步那么大），同时保持弯曲
            legK = 0.72f;
            frontRot = (-0.32f - swing * 0.16f) * Facing;
            backRot = (0.24f + swing * 0.14f) * Facing;
        }
        else if (air)
        {
            frontRot = -0.62f * Facing;           // 收腿：前腿抬起
            backRot = 0.34f * Facing;             // 后腿后拖
        }
        else if (Crouched)
        {
            legK = 0.72f;
            frontRot = -0.42f * Facing;
            backRot = 0.30f * Facing;
        }
        if (_landT > 0f && !Crouched)
            legK *= 1f - 0.10f * _landT;

        SetLeg(_legFront, frontRot, legK);
        SetLeg(_legBack, backRot, legK);

        // —— 躯干：步伐起伏 + 前倾 + 后坐 + 蹲压 + 落地缓冲 ——
        float torsoY = -Height;
        float torsoX = 0f;
        float torsoRot = 0f;
        var torsoScale = Vector2.One;

        if (Vaulting)
        {
            torsoRot = -0.30f * Facing;
            torsoY += 3f;
        }
        else if (crouchWalk)
        {
            // 蹲走：保持蹲姿压缩 + 小幅度重心起伏（不会像站姿那么大）
            torsoScale = new Vector2(1.06f, 0.84f);
            torsoY += Height * 0.16f - Mathf.Abs(Mathf.Cos(Phase)) * 1.2f + 0.6f;
            torsoRot = (0.10f + Mathf.Sin(Phase * 2f) * 0.02f) * Facing;
        }
        else if (Crouched)
        {
            torsoScale = new Vector2(1.06f, 0.84f);
            torsoY += Height * 0.16f;
            torsoRot = 0.10f * Facing;
        }
        else if (air)
        {
            torsoRot = -0.06f * Facing;
            torsoScale = new Vector2(0.98f, 1.04f);
        }
        else if (standingWalk)
        {
            torsoY += -Mathf.Abs(Mathf.Cos(Phase)) * 2.6f + 1.3f; // 迈步重心起伏
            torsoRot = (0.085f + Mathf.Sin(Phase * 2f) * 0.03f) * Facing;
        }
        else
        {
            torsoY += Mathf.Sin(Godot.Time.GetTicksMsec() / 480f) * 0.7f; // 站立呼吸
        }

        if (Aim > 0.5f && !air) torsoRot += 0.10f * Facing;
        if (Recoil > 0f)
        {
            torsoRot -= 0.26f * Recoil * Facing;   // 后仰
            torsoX -= Facing * 5f * Recoil;        // 后移
        }
        if (_landT > 0f && !Crouched)
            torsoScale = new Vector2(torsoScale.X * (1f + 0.04f * _landT),
                                     torsoScale.Y * (1f - 0.07f * _landT));

        // 腿压缩后脚仍在地面：躯干跟着下沉，髋部不脱节
        torsoY += (1f - legK) * (-_hipY);

        _torso.Position = new Vector2(torsoX, torsoY);
        _torso.Rotation = torsoRot;
        _torso.Scale = new Vector2(_s * torsoScale.X, _s * torsoScale.Y);

        var tint = new Color(1f, 1f, 1f, 1f).Lerp(new Color(1f, 0.88f, 0.88f, 1f), HurtFlash * 0.9f);
        _torso.Modulate = tint;
        _legFront.Modulate = tint;
        _legBack.Modulate = tint;

        QueueRedraw();
    }

    /// <summary>摆腿 + 纵向压缩。压缩时 Position 下移让脚底始终贴地。</summary>
    private void SetLeg(Sprite2D leg, float rot, float k)
    {
        leg.Rotation = rot;
        leg.Scale = new Vector2(_s, _s * k);
        leg.Position = new Vector2(leg.Position.X, -_legR * k);
    }

    public override void _Draw()
    {
        if (_torso == null) return;

        // —— 脚下接触阴影：随步伐轻微变形，始终钉在地面 ——
        // 蹲姿（无论静态还是蹲走）阴影都固定，只有站姿走路才跟随步伐伸缩
        float shadowScaleX, shadowScaleY;
        if (Crouched)
        {
            shadowScaleX = 1.8f;
            shadowScaleY = 0.35f;
            DrawSetTransform(new Vector2(-3, -2), 0,
                new Vector2(shadowScaleX, shadowScaleY));
        }
        else if ((Moving || Speed > 2f) && Grounded && !Airborne)
        {
            float step = Mathf.Sin(Phase);
            float stretch = Mathf.Max(0.75f, 1.25f + step * 0.5f);
            shadowScaleX = stretch;
            shadowScaleY = 1f / stretch * 0.55f;
            DrawSetTransform(new Vector2(-3 - Facing * step * 1.5f, -2), 0,
                new Vector2(shadowScaleX, shadowScaleY));
        }
        else
        {
            DrawSetTransform(new Vector2(-3, -2), 0, new Vector2(2.6f, 0.55f));
        }
        DrawCircle(Vector2.Zero, Height * 0.115f,
            new Color(0.10f, 0.09f, 0.08f, 0.28f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        // —— 瞄准指示线 ——
        if (Aim > 0.5f)
        {
            var muzzle = new Vector2(Facing * (Height * 0.45f), -Height * 0.55f);
            DrawLine(muzzle, muzzle + new Vector2(Facing * 14f, -2f),
                InkStyle.InkColor, 1.5f, true);
        }

        // —— 枪口火光：亮核 + 外晕 + 前向火舌 ——
        if (MuzzleFlash > 0.05f)
        {
            var muzzle = new Vector2(Facing * (Height * 0.45f), -Height * 0.55f);
            float r = 4f + MuzzleFlash * 6f;
            DrawCircle(muzzle, r, new Color(1f, 0.85f, 0.30f, MuzzleFlash * 0.85f));
            DrawCircle(muzzle, r * 0.55f, new Color(1f, 1f, 0.62f, MuzzleFlash));
            DrawLine(muzzle, muzzle + new Vector2(Facing * (12f + r * 2.2f), -1f),
                new Color(1f, 0.92f, 0.55f, MuzzleFlash * 0.8f), 2.2f, true);
        }
    }
}
