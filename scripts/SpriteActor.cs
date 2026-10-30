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
    public bool Prone;
    public bool Vaulting;
    public bool Airborne;
    public bool Moving;

    public float Aim;
    public float MuzzleFlash;
    public float HurtFlash;
    /// <summary>开枪后坐量 1→0，躯干短暂后仰 + 后移。</summary>
    public float Recoil;
    /// <summary>自动化截图用：true 时暂停火光/受击/后坐衰减（低帧率下截不到火光）。</summary>
    public bool FreezeFx;

    /// <summary>contact shadow drawn by SnowShadow kit; player turns this off, level GroundShadow handles him</summary>
    public bool DrawShadow = true;

    /// <summary>专姿立绘激活时的枪口锚点（flip 空间；NaN=未激活，走纸娃娃估算）。</summary>
    private Vector2 _poseMuzzle = new(float.NaN, float.NaN);

    /// <summary>当前应使用的专姿立绘（趴下&gt;蹲下&gt;瞄准）+ 枪管高度比例（实测立绘：aim 0.165 / crouch 0.31 / prone 0.58）。</summary>
    private (Texture2D tex, float s, float pox, float muzY) PoseInfo()
    {
        if (_pose == null) return (null, 0f, 0f, 0f);
        if (Prone) return (_proneTex, _proneS, -_proneTex.GetSize().X * _proneS * 0.30f, 0.58f);
        if (Crouched) return (_crouchTex, _crouchS, 0f, 0.31f);
        if (Aim > 0.5f && !(Airborne || !Grounded) && !Vaulting)
            return (_aimTex, _aimS, 0f, 0.165f);
        return (null, 0f, 0f, 0f);
    }

    /// <summary>当前枪口位置（画面空间）：专姿立绘用立绘内锚点，纸娃娃按姿态估算。</summary>
    private Vector2 MuzzlePoint()
    {
        var (tex, s, pox, muzY) = PoseInfo();
        if (tex != null)
        {
            var sz = tex.GetSize();
            return new Vector2((pox + sz.X * s * 0.97f) * Facing, -sz.Y * s * (1f - muzY));
        }
        return Prone
            ? new Vector2(Facing * Height * 0.92f, -Height * 0.22f)
            : new Vector2(Facing * Height * 0.92f, -Height * 0.55f);
    }

    /// <summary>世界坐标枪口（子弹起点/近战判定用）。</summary>
    public Vector2 MuzzleWorld() => GlobalPosition + MuzzlePoint();

    // 翻转子容器：所有立绘 Sprite2D 作为其子节点
    // Facing<0 时 Scale.X=-1 整体水平镜像，比 FlipH 更安全
    private readonly Node2D _flip = new();
    private Sprite2D _torso, _legBack, _legFront, _hip, _pose;
    /// <summary>hip strip top-left in texture coords, used to glue it to the torso.</summary>
    private Vector2 _hipOrigin;
    /// <summary>hip pivot in texture coords (from top-left): the point that must
    /// stay glued to the leg pieces whenever the torso rotates.</summary>
    private Vector2 _hipPivot;
    private Texture2D _aimTex, _crouchTex, _proneTex;
    private float _aimS, _crouchS, _proneS;
    private ShaderMaterial _mat;
    private bool _wasGrounded = true;
    private float _landT;             // 落地缓冲计时 1→0
    private float _s;                 // 立绘统一缩放（height/texH）
    private float _hipY, _legTopScreen, _legR, _overlapScreen;
    /// <summary>用了分件腿贴图（掩膜 + 裆缝切开），不是老的中线竖切。</summary>
    private bool _maskedLegs;
    /// <summary>裆部铰链点，贴图坐标（两条腿共用，绕它转就不会扯开裤裆）。</summary>
    private int _pivotX, _pivotY;
    private float _texH;

    // v2.7 recoil: rotation around the hip line + compensation + upper body push back
    private float _recoilRot, _recoilBack;

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

        var srcPath = $"res://assets/art/sprites/{name}.jpg";
        // v2.6：先试分件腿（掩膜 + 裆缝），分不出来才退回老的中线竖切
        var parts = FigureParts.Build(srcPath, 0.03f, 0.05f);
        var tex = parts != null ? parts.Body : ChromaKey.Cutout(srcPath, 0.03f, 0.05f);
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
        _texH = texH;
        _s = height / texH;
        float s = _s;

        if (parts != null && parts.LegBack != null && parts.LegFront != null)
        {
            // —— 分件腿：身体已经把腿挖掉，腿片只含属于腿的像素 ——
            // 挎在手上的步枪不会混进腿片，走路摆腿时手上不会多出一支枪。
            _maskedLegs = true;
            _pivotX = parts.PivotX;
            _pivotY = parts.PivotY;
            _hipPivot = new Vector2(_pivotX, _pivotY);
            _hipY = -(texH - parts.PivotY) * s;

            _legBack = MakeMaskedLeg(parts.LegBack, s, texH);
            _legFront = MakeMaskedLeg(parts.LegFront, s, texH);

            // v2.7: the torso rotation pivot moves from "top of head" to "hip line".
            // The old way did _torso.Position = (0, -texH*s) with Centered=false,
            // so the node origin landed on the top left of the texture (the head),
            // and rotating it swung the whole upper body around the head - the
            // hips swung off the lower body. That is the "upper and lower body
            // separate when firing" the player keeps reporting.
            // Use Offset to push the texture down so the node origin sits on the
            // hip line, then the upper body rotates around the hip and the hips
            // stay perfectly still.
            _torso = new Sprite2D
            {
                Texture = tex,
                Centered = false,
                Scale = new Vector2(s, s),
                Position = new Vector2(0, -texH * s),
            };
            _torso.Material = _mat;

            // 髋部桥接片：胯上静止的一条布，贴在躯干上盖住腿摆动时张开的缝
            _hipOrigin = new Vector2(parts.BridgeX, parts.BridgeY);
            if (parts.Bridge != null)
            {
                _hip = new Sprite2D
                {
                    Texture = parts.Bridge,
                    Centered = false,
                    Scale = new Vector2(s, s),
                };
                _hip.Material = _mat;
            }
        }
        else
        {
            // —— 老路：沿贴图中线竖切（立绘分不出腿缝时用）——
            float hipFromTop = texH * 0.58f;
            float overlap = texH * 0.05f;
            _hipPivot = new Vector2(texW * 0.5f, hipFromTop);
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

            // 髋部桥接片：取下半身中段顶部一小条（盖在腿之上、躯干之下），
            // 不随腿剪刀摆——专门盖住两腿绕髋旋转时中缝张开 + 腰部分离，
            // 让脚/身体/手脚在视觉上连成一体（修正"动起来手脚头分离"的观感）。
            float hipHalf = texW * 0.30f;
            // thin seam band only: the old one stretched halfway down the thigh,
            // copied the sling / rifle butt / thigh pixels into a static patch
            // glued to the torso = second gun image + phantom third leg + knee
            // looking pecah whenever the torso bobbed
            float pelvisH = overlap * 2.4f;
            _hipOrigin = new Vector2(texW * 0.5f - hipHalf, _legTopScreen);
            _hip = new Sprite2D
            {
                Texture = tex,
                Centered = false,
                RegionEnabled = true,
                RegionRect = new Rect2(texW * 0.5f - hipHalf, _legTopScreen, hipHalf * 2f, pelvisH),
                Scale = new Vector2(s, s),
                Position = new Vector2((texW * 0.5f - hipHalf) * s, -_legR),
            };
            _hip.Material = _mat;
        }

        // 立绘子容器：所有 Sprite2D 都 AddChild 到 _flip
        AddChild(_flip);
        _flip.AddChild(_legBack);
        _flip.AddChild(_torso);
        _flip.AddChild(_legFront);
        if (_hip != null) _flip.AddChild(_hip);      // 髋片最后绘制，盖在最上层

        // —— 军人标准专姿立绘（仅玩家）：瞄准/蹲/趴不再用纸娃娃刚性变形，
        // 而是整体换一张同画风、同军服的专项姿势立绘（军事动作标准）——
        if (kind == Kind.NortheastArmy)
        {
            _aimTex = ChromaKey.Cutout("res://assets/art/sprites/soldier_na_aim.jpg", 0.03f, 0.05f);
            _crouchTex = ChromaKey.Cutout("res://assets/art/sprites/soldier_na_crouch.jpg", 0.03f, 0.05f);
            _proneTex = ChromaKey.Cutout("res://assets/art/sprites/soldier_na_prone.jpg", 0.03f, 0.05f);
            // 蹲：跪姿真实高度比 0.74（同一个人只是姿势变矮，头/装备不缩小）
            // 趴：按身体长度=站立身高定缩放（趴下身长不变，横向贴地）
            if (_aimTex != null) _aimS = Height / _aimTex.GetSize().Y;
            if (_crouchTex != null) _crouchS = Height * 0.74f / _crouchTex.GetSize().Y;
            if (_proneTex != null) _proneS = Height / _proneTex.GetSize().X;
            _pose = new Sprite2D { Centered = false, Visible = false, Material = _mat };
            _flip.AddChild(_pose);
        }
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

    /// <summary>
    /// 分件腿的摆法：整块腿贴图绕裆部铰链点转。
    /// Offset 把铰链挪到节点原点，Position 再把节点摆回脚底坐标。
    /// </summary>
    private Sprite2D MakeMaskedLeg(Texture2D legTex, float s, float texH)
    {
        var leg = new Sprite2D
        {
            Texture = legTex,
            Centered = false,
            Scale = new Vector2(s, s),
            Offset = new Vector2(-_pivotX, -_pivotY),
            Position = new Vector2(_pivotX * s, -(texH - _pivotY) * s),
        };
        leg.Material = _mat;
        return leg;
    }

    public override void _Process(double delta)
    {
        if (_torso == null) return;
        float f = (float)delta;

        if (!FreezeFx)
        {
            if (MuzzleFlash > 0f) MuzzleFlash = Mathf.Max(0f, MuzzleFlash - f * 9f);
            if (HurtFlash > 0f) HurtFlash = Mathf.Max(0f, HurtFlash - f * 3.2f);
            if (Recoil > 0f) Recoil = Mathf.Max(0f, Recoil - f * 5.5f);
        }

        // 落地缓冲：由腾空转落地触发一次
        bool groundedNow = Grounded && !Airborne;
        if (!_wasGrounded && groundedNow) _landT = 1f;
        _wasGrounded = groundedNow;
        if (_landT > 0f) _landT = Mathf.Max(0f, _landT - f * 5.5f);

        bool air = Airborne || !Grounded;
        bool low = Crouched || Prone;
        bool standingWalk = !air && !Vaulting && !low && (Moving || Speed > 2f);
        bool crouchWalk = !air && !Vaulting && Crouched && !Prone && (Moving || Speed > 2f);
        bool proneWalk = !air && !Vaulting && Prone && (Moving || Speed > 2f);

        // —— 翻转：子容器 Scale.X = FacingRight ? 1 : -1 ——
        // 比 Sprite2D.FlipH 更安全（FlipH 在 Centered=false 上会产生位置偏移）
        _flip.Scale = new Vector2(FacingRight ? 1f : -1f, 1f);

        // —— 专姿立绘切换（军人标准姿势）：趴下 > 蹲下 > 站姿瞄准 ——
        // 优先级最高的状态用整张专项立绘替换纸娃娃，动作是按军事条令画的，
        // 不再靠刚性贴图变形硬凑。腾空/翻越时不进入专姿。
        var (ptex, ps, pox, muzY) = PoseInfo();
        // 趴/蹲：无论静止还是前进，一律用整张专项立绘（姿势按军事条令画、解剖正确）。
        // 之前 v3.12 为了让"前进"有腿部摆动，把移动中的趴/蹲退回纸娃娃——结果把站立的
        // 上半身立绘竖直压扁到 48% 再配横过来的腿，横板下像人折坏/压扁，观感很怪；
        // 又或者整张专姿图被冻结、原地滑动（"纸片人滑过去"）。
        // 现在改为：移动时仍用正确的专姿图，只在图上叠加轻微爬行错动（上下浮动 + 前后微摆），
        // 既保留正确姿势，又明确读出"正在匍匐 / 正在蹲行"，不再出现压扁或冻结滑动。
        if (ptex != null)
        {
            var sz = ptex.GetSize();
            _torso.Visible = false; _legBack.Visible = false; _legFront.Visible = false;
            if (_hip != null) _hip.Visible = false;
            _pose.Texture = ptex;
            _pose.Scale = new Vector2(ps, ps);

            float bob = 0f, sway = 0f;
            if (proneWalk)
            {
                // #anim1: prone crawl - make the body clearly heave and rock so
                // it reads as "dragging forward on elbows", not a frozen sheet
                // sliding. Amplitude is in px on a ~108px figure.
                bob = Mathf.Sin(Phase) * 4.6f;
                sway = Mathf.Cos(Phase) * 3.0f;
            }
            else if (crouchWalk)
            {
                // #anim1: crouch-walk - heavier weight drop on each step + rock,
                // so the motion is legible without distorting the crouch silhouette.
                bob = Mathf.Abs(Mathf.Cos(Phase)) * 3.4f - 1.7f;
                sway = Mathf.Sin(Phase) * 2.2f;
            }
            // v3.13: 落地缓冲也接到专姿立绘上——着地那一瞬间整个人往下沉一下
            // 再弹回来。以前只有纸娃娃分支有，蹲/趴/站专姿图落地是硬接地的。
            bob -= _landT * 3.4f;

            _pose.Position = new Vector2(pox - Facing * 6f * Recoil + sway, -sz.Y * ps + bob);
            _pose.Visible = true;
            // 枪口锚点（flip 空间，x 朝前），_Draw 里乘 Facing 转回画面空间
            _poseMuzzle = new Vector2(pox + sz.X * ps * 0.97f, -sz.Y * ps * (1f - muzY));
            QueueRedraw();
            return;
        }
        if (_pose != null && _pose.Visible)
        {
            _pose.Visible = false;
            _torso.Visible = true; _legBack.Visible = true; _legFront.Visible = true;
            _poseMuzzle = new Vector2(float.NaN, float.NaN);
        }

        // —— 腿：剪刀步 / 跳跃收腿 / 蹲姿折叠 ——
        float legK = 1f; // 腿的纵向压缩（蹲/落地缓冲时 <1，脚底要贴回地面）
        float swing = Mathf.Sin(Phase);
        float frontRot = 0f, backRot = 0f;
        if (standingWalk)
        {
            // #anim1: widen the scissor so the stride reads as walking, not
            // sliding-in-place. Kept asymmetric (front lifts a touch more) so
            // the two leg strips don't fan open at the crotch seam.
            frontRot = -swing * 0.36f * Facing;
            backRot = swing * 0.30f * Facing;
        }
        else if (Prone)
        {
            // 匍匐：双腿向后伸平贴地拖行。镜像对称姿势，旋转不乘 Facing
            //（镜像容器会自动把方向翻到角色背后）
            legK = 1f;
            float crawl = proneWalk ? Mathf.Sin(Phase) * 0.40f : 0f;  // 加大爬行腿摆幅度，明确匍匐前进感（#8）
            frontRot = 1.42f + crawl;
            backRot = 1.56f - crawl;
        }
        else if (crouchWalk)
        {
            // 蹲走：低姿蹲行（双膝深弯、重心下坐、上身后倾 sightly），比站姿矮一头
            legK = 0.55f;
            // v2.9: keep the crotch closed. The old pair (-0.72 / +0.50) swung
            // both leg strips opposite ways around the same hip hinge, the
            // wedge between them opened wide and the background showed
            // through the hollow body = legs looking detached from the torso.
            // 蹲走：保留 v2.9 的"髋部闭合"基准角，仅加大摆幅让腿部明显交替（#8）
            frontRot = (-0.58f - swing * 0.30f) * Facing;
            backRot = (0.26f + swing * 0.26f) * Facing;
        }
        else if (air)
        {
            // #anim1: stronger jump tuck so the leap reads as "push off - tuck -
            // land" instead of a flat hop. Both knees come up together (same
            // rotation sign) to keep the hip seam shut.
            legK = 0.88f;
            frontRot = -0.62f * Facing;
            backRot = -0.34f * Facing;
        }
        else if (Crouched)
        {
            // 静蹲：双膝深弯重心坐实，比站姿明显矮一头
            legK = 0.55f;
            frontRot = -0.58f * Facing;
            backRot = 0.26f * Facing;
        }
        if (_landT > 0f && !Crouched)
            legK *= 1f - 0.10f * _landT;

        SetLeg(_legFront, frontRot, legK, Prone ? -Height * 0.26f : float.NaN);
        SetLeg(_legBack, backRot, legK, Prone ? -Height * 0.24f : float.NaN);

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
        else if (Prone)
        {
            // 匍匐：躯干大幅压平贴地（不旋转——立绘里步枪与脊柱垂直，
            // 一旦放倒步枪会竖着戳地；压平则枪线保持水平，读作"低姿持枪匍匐"）。
            // 髋部桥片已隐藏，腿部由 Prone 分支向后伸平衔接。
            torsoScale = new Vector2(1.02f, 0.48f);
            // 前进时躯干随爬行前后错动 + 轻微扭转，给出明确的爬行感（#8）
            torsoY = -Height * 0.37f + (proneWalk ? Mathf.Sin(Phase) * 2.2f : 0f);
            torsoRot = 0.05f + (proneWalk ? Mathf.Sin(Phase) * 0.04f : 0f);
        }
        else if (crouchWalk)
        {
            // 蹲走：蹲姿压缩 + 小幅度重心起伏（下沉量与腿部压缩匹配，髋部不脱节）
            torsoScale = new Vector2(1.06f, 0.76f);
            torsoY += Height * 0.30f - Mathf.Abs(Mathf.Cos(Phase)) * 1.2f + 0.6f;
            torsoRot = (0.18f + Mathf.Sin(Phase * 2f) * 0.02f) * Facing;
        }
        else if (Crouched)
        {
            torsoScale = new Vector2(1.06f, 0.76f);
            torsoY += Height * 0.30f;
            torsoRot = 0.18f * Facing;
        }
        else if (air)
        {
            torsoRot = -0.06f * Facing;
            torsoScale = new Vector2(0.98f, 1.04f);
        }
        else if (standingWalk)
        {
            // #anim1: bigger weight bounce per step so the stride is obvious
            torsoY += -Mathf.Abs(Mathf.Cos(Phase)) * 3.6f + 1.8f;
            torsoRot = (0.10f + Mathf.Sin(Phase * 2f) * 0.04f) * Facing;
        }
        else
        {
            torsoY += Mathf.Sin(Godot.Time.GetTicksMsec() / 480f) * 0.7f; // 站立呼吸
        }

        if (Aim > 0.5f && !air && !Prone)
        {
            torsoRot += 0.16f * Facing;   // 持枪瞄准：上身前倾把枪线压平
            torsoY += 2.5f;               // 微沉：枪托抵肩更稳
        }
        if (Recoil > 0f)
        {
            // v2.7: the upper body kicks backwards when firing (the stock digs
            // into the shoulder). The rotation part is handled by the general
            // hip glue below, together with the walk and aim lean.
            _recoilRot = -0.20f * Recoil * Facing;
            _recoilBack = Facing * 5f * Recoil;
        }
        else
        {
            _recoilRot = 0f; _recoilBack = 0f;
        }
        if (_landT > 0f && !Crouched)
            torsoScale = new Vector2(torsoScale.X * (1f + 0.04f * _landT),
                                     torsoScale.Y * (1f - 0.07f * _landT));

        // 腿压缩后脚仍在地面：躯干跟着下沉，髋部不脱节
        // （蹲/匍匐已在各自分支里对齐髋线，再叠加会把躯干压进地里——正是"蹲下姿势奇怪"的根源）
        if (!low)
            torsoY += (1f - legK) * (-_hipY);

        // v3.6: general hip glue. ANY torso rotation (walk lean, aim lean,
        // recoil) used to spin around the texture's top-left corner, so the
        // cut-out hip hole swung sideways while the legs stayed put - the leg
        // tops showed through as a bright waist seam, worst while strolling
        // (0.085 rad lean ≈ 5-7 px of swing, very visible against snow).
        // Rotating around the corner by theta is the same as rotating around
        // the hip pixel by theta + translating, so we translate the result
        // back onto the hip point. The hip (and the bridge patch glued to it)
        // now stays pinned to the leg pivot for every pose.
        torsoRot += _recoilRot;
        torsoX -= _recoilBack;
        {
            float sx2 = _s * torsoScale.X, sy2 = _s * torsoScale.Y;
            float vx2 = _hipPivot.X * sx2, vy2 = _hipPivot.Y * sy2;
            float c2 = Mathf.Cos(torsoRot), s2 = Mathf.Sin(torsoRot);
            float rx2 = vx2 * c2 - vy2 * s2, ry2 = vx2 * s2 + vy2 * c2;
            torsoX += vx2 - rx2;
            torsoY += vy2 - ry2;
        }

        _torso.Position = new Vector2(torsoX, torsoY);
        _torso.Rotation = torsoRot;
        _torso.Scale = new Vector2(_s * torsoScale.X, _s * torsoScale.Y);

        // v2.7: when the torso kicks backwards the legs have to step back a
        // little too, otherwise the upper body has already retreated while the
        // hips stay put, and the seam pulls wider.
        // (the compensation lives in SetLeg, because SetLeg resets the leg
        // position every frame)

        // hip bridge glued to the torso: same pos/rot/scale so the strip pixels
        // always match the body ones. old version kept it nailed in place, so
        // whenever the torso lean or kick back the same patch near the rifle
        // butt kena duplicated = that ghost sticker both sides of the fight
        if (_hip != null)
        {
            var off = new Vector2(_hipOrigin.X * _torso.Scale.X, _hipOrigin.Y * _torso.Scale.Y);
            _hip.Position = _torso.Position + off.Rotated(_torso.Rotation);
            _hip.Rotation = _torso.Rotation;
            _hip.Scale = _torso.Scale;
        }

        var tint = new Color(1f, 1f, 1f, 1f).Lerp(new Color(1f, 0.88f, 0.88f, 1f), HurtFlash * 0.9f);
        _torso.Modulate = tint;
        _legFront.Modulate = tint;
        _legBack.Modulate = tint;
        if (_hip != null)
        {
            _hip.Modulate = tint;
            _hip.Visible = !Prone;   // 匍匐时躯干已放倒盖住髋部，桥片会悬空穿帮
        }

        QueueRedraw();
    }

    /// <summary>摆腿 + 纵向压缩。压缩时 Position 下移让脚底始终贴地。
    /// pivotY 非默认时直接指定髋部 pivot 高度（匍匐用：髋点压到贴地）。</summary>
    private void SetLeg(Sprite2D leg, float rot, float k, float pivotY = float.NaN)
    {
        leg.Rotation = rot;
        leg.Scale = new Vector2(_s, _s * k);
        // v2.7: when the torso kicks back on recoil the legs step back a bit
        // too, so the hips never get pulled out of the seam
        float back = -_recoilBack * 0.35f;
        if (_maskedLegs)
        {
            // 分件腿：绕裆部铰链点转，压缩时把脚底拉回地面
            leg.Position = new Vector2(_pivotX * _s + back,
                float.IsNaN(pivotY) ? -(_texH - _pivotY) * _s * k : pivotY);
        }
        else
        {
            leg.Position = new Vector2(leg.Position.X + back,
                float.IsNaN(pivotY) ? -_legR * k : pivotY);
        }
    }

    public override void _Draw()
    {
        if (_torso == null) return;

        // one unified ground shadow, tone and sun angle come from SnowShadow kit;
        // no more per-state pulsing blob that reads like a ghost under the legs
        if (DrawShadow)
        {
            float stretch = Prone ? 2.6f : Crouched ? 1.5f : 1.15f;
            SnowShadow.Draw(this, new Vector2(Prone ? Facing * Height * 0.16f : -1f, -1f),
                Height * 0.13f, 0.26f, stretch);
        }

        // 枪口火光提供开火反馈；原本 Aim>0.5 时从枪口向前画的 InkColor 深墨"瞄准指示线"
        // 被误读成枪口黑线，已移除（鼠标本身即是瞄准参考）。
        // —— muzzle flash: quick warm pop, gone in a blink, smoke stays a bit ——
        if (MuzzleFlash > 0.05f)
        {
            var muzzle = MuzzlePoint();
            float a = MuzzleFlash;
            float r = 3f + a * 3.5f;
            DrawCircle(muzzle, r * 1.9f, new Color(1f, 0.78f, 0.40f, 0.15f * a));
            DrawCircle(muzzle, r, new Color(0.98f, 0.80f, 0.44f, 0.50f * a));
            DrawCircle(muzzle, r * 0.5f, new Color(1f, 0.94f, 0.80f, 0.80f * a));
            DrawColoredPolygon(new[]
            {
                muzzle + new Vector2(0, -r * 0.5f),
                muzzle + new Vector2(Facing * (r * 2.6f + 4f), -1f),
                muzzle + new Vector2(0, r * 0.5f),
            }, new Color(0.98f, 0.82f, 0.48f, 0.45f * a));
            // smoke lingers one beat longer than the flame itself
            if (a < 0.85f)
            {
                float s = (0.85f - a) / 0.85f;
                var up = muzzle + new Vector2(Facing * 3f, -6f - 6f * s);
                DrawCircle(up, 2.5f + 4f * s, new Color(0.60f, 0.62f, 0.65f, 0.28f * s));
            }
        }
    }
}
