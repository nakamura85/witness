using Godot;

namespace Witness;

public enum FigureKind
{
    /// <summary>东北军士兵（守方，灰蓝棉军装、布棉帽）。</summary>
    NortheastArmy,
    /// <summary>关东军士兵（卡其军装、钢盔、背包）。</summary>
    Japanese,
    /// <summary>平民（棉袍 / 头巾）。</summary>
    Civilian,
}

/// <summary>
/// 骨架式 2D 人物（不依赖逐帧贴图）：
/// 髋部 + 双段 IK 腿 + 双段 IK 臂 + 躯干 + 头 + 枪，
/// 全部按关节解算后当场绘制。走路有真实的支撑脚 / 摆动脚，
/// 脚始终踩在地面（跳跃时收腿），不会出现“纸片人上下飘”。
/// </summary>
public partial class FigureActor : Node2D
{
    public FigureKind Kind = FigureKind.NortheastArmy;
    public bool FacingRight = true;

    /// <summary>步态相位（由外部按位移推进，保证脚步与移动速度同步）。</summary>
    public float Phase;
    /// <summary>当前水平速度绝对值，用来决定步幅。</summary>
    public float Speed;
    public bool Grounded = true;
    public bool Crouching;
    /// <summary>0=持枪下垂，1=举枪瞄准。</summary>
    public float Aim;
    /// <summary>枪口火光 0..1，开火瞬间置 1，自行衰减。</summary>
    public float MuzzleFlash;
    /// <summary>受击闪白 0..1。</summary>
    public float HurtFlash;

    private float _time;
    private float _breath;

    private static readonly Color Outline = InkStyle.InkColor;
    private static readonly Color Shadow = InkStyle.GroundShadow;

    public override void _Process(double delta)
    {
        float f = (float)delta;
        _time += f;
        _breath = Mathf.Sin(_time * 2.1f);
        if (MuzzleFlash > 0f) MuzzleFlash = Mathf.Max(0f, MuzzleFlash - f * 9f);
        if (HurtFlash > 0f) HurtFlash = Mathf.Max(0f, HurtFlash - f * 3.2f);
        QueueRedraw();
    }

    public Vector2 MuzzleLocal => Aim > 0.5f
        ? new Vector2(FacingRight ? 52f : -52f, -64f)
        : new Vector2(FacingRight ? 12f : -12f, -78f);

    public override void _Draw()
    {
        // 手绘漫画风接触阴影：一个压扁的扁椭圆，平涂 GroundShadow 冷色
        if (Grounded)
        {
            DrawSetTransform(new Vector2(0, -1), 0, new Vector2(1.05f, 0.22f));
            DrawCircle(Vector2.Zero, 15.5f, InkStyle.GroundShadow);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        // 朝向翻转
        DrawSetTransform(new Vector2(0, -2), 0, new Vector2(FacingRight ? 1f : -1f, 1f));

        var pal = Palette(Kind);
        float stride = Mathf.Clamp(Speed / 105f, 0f, 1.3f);
        if (Crouching) stride *= 0.42f;
        bool moving = Speed > 6f && Grounded;
        float phase = Phase;

        // —— 关节解算 ——
        float hipY = -46f;
        hipY += Mathf.Sin(phase * 2f) * 1.4f * stride;                 // 重心起伏：脚落地时略沉
        if (Crouching) hipY = -32f;
        if (!Grounded) hipY = -47f;

        float lean = 0.02f + 0.09f * stride + 0.05f * Aim;
        if (Crouching) lean = 0.24f;
        if (!Grounded) lean = 0.12f;
        lean += _breath * 0.006f;

        var hip = new Vector2(Mathf.Sin(phase) * 1.6f * stride, hipY);
        var shoulder = hip + new Vector2(Mathf.Sin(lean) * 33f, -Mathf.Cos(lean) * 33f);
        var dir = (shoulder - hip).Normalized();
        var side = new Vector2(-dir.Y, dir.X);

        // 双脚目标（世界地面 y=0）
        Vector2 footA;
        Vector2 footB;
        if (!Grounded)
        {
            footA = new Vector2(13, -15);
            footB = new Vector2(-10, -5);
        }
        else if (Crouching)
        {
            footA = new Vector2(14, 0);
            footB = new Vector2(-13, 0);
        }
        else
        {
            float liftA = Mathf.Max(0f, Mathf.Cos(phase)) * stride * 7f;
            float liftB = Mathf.Max(0f, -Mathf.Cos(phase)) * stride * 7f;
            footA = new Vector2(stride * 13f * Mathf.Sin(phase) + 2.5f, -liftA);
            footB = new Vector2(stride * 13f * Mathf.Sin(phase + Mathf.Pi) - 2.5f, -liftB);
        }

        var kneeA = SolveIK(hip, footA, 24f, 24f, 1f);
        var kneeB = SolveIK(hip, footB, 24f, 24f, 1f);

        // —— 背包 / 行囊（在身体之后）——
        if (Kind == FigureKind.Japanese)
        {
            var packC = shoulder + side * -7f + new Vector2(-2, 15);
            var pack = new[]
            {
                packC + new Vector2(-10, -12), packC + new Vector2(8, -13),
                packC + new Vector2(11, 12), packC + new Vector2(-11, 13),
            };
            Poly(pack, new Color(0.40f, 0.36f, 0.24f), 2f);
            DrawLine(packC + new Vector2(-9, -2), packC + new Vector2(9, -2), new Color(0.25f, 0.22f, 0.16f), 1.2f, true);
        }

        // —— 后腿（压暗，制造前后层次）——
        DrawLeg(kneeB, footB, pal.Dark, 1f);
        // —— 后臂 ——
        Vector2 handB = HandTarget(false, phase, stride, lean);
        Vector2 elbowB = SolveIK(shoulder + side * -7.5f, handB, 17.5f, 17.5f, -1f);
        Tube(shoulder + side * -7.5f, elbowB, pal.Dark, 8f);
        Tube(elbowB, handB, pal.Dark, 7f);
        DrawCircle(handB, 3.4f, pal.SkinDark);

        // —— 前腿 ——
        DrawLeg(kneeA, footA, pal.Coat, 1f);

        // —— 躯干（棉军装 / 棉袍）——
        var hemSway = new Vector2(Mathf.Sin(_time * 2.2f) * 1.4f * stride + 1.2f, 1f);
        // 用正规的四点躯干：肩后 → 肩前 → 髋前 → 髋后
        var torso = new[]
        {
            shoulder + side * -12.5f,
            shoulder + side * 12.5f,
            hip + side * 11f,
            hip + side * -11f,
        };
        Poly(torso, pal.Coat, 2.3f);
        // 手绘漫画风躯干：背光侧一块硬边阴影（平涂不透明色块）
        DrawColoredPolygon(new[]
        {
            shoulder + side * -12.2f, shoulder + side * -3.4f,
            hip + side * -2.6f, hip + side * -10.7f,
        }, pal.CoatDark);

        // 卡通高光：一个小圆点在右上受光角（取代半透明渐变高光带）
        InkStyle.KawaiiHighlight(this,
            shoulder + side * 10.2f + new Vector2(0, 0.5f), 2.5f,
            InkStyle.Lighten(pal.Coat, 1.05f));
        InkStyle.KawaiiLineHighlight(this,
            shoulder + side * 8.0f + new Vector2(0, 1f),
            shoulder + side * 3.0f + new Vector2(2f, 5f),
            InkStyle.Lighten(pal.Coat, 1.12f), 1.6f);

        // 大衣下摆：从髋线往下垂，略微向后扫。原来写成“向后伸 16px、只降 3px”，
        // 结果腰后横着一块板——布是有重量的，下摆必须往下掉。
        float hemLen = Crouching ? 12f : 18f;
        var skirt = new[]
        {
            hip + side * 11f,
            hip + side * -11f,
            hip + side * -10f + new Vector2(-5f - 2f * stride, hemLen - 3f) + hemSway,
            hip + side * 10f + new Vector2(-3f - 2f * stride, hemLen) + hemSway,
        };
        Poly(skirt, pal.CoatDark, 2.1f);
        // 下摆内侧的暗面 + 前襟开衩：把布和腿分开，不然下摆和腿糊成一块
        DrawLine(hip + side * -9.8f + new Vector2(-5f, hemLen - 4f) + hemSway,
            hip + side * -2f + new Vector2(-2f, hemLen - 8f),
            new Color(pal.CoatDark.Darkened(0.30f), 0.62f), 3.4f, true);
        DrawLine(hip + side * 10f + new Vector2(-3f, hemLen - 1f) + hemSway,
            hip + side * 3f + new Vector2(-1f, 1f),
            new Color(pal.CoatDark.Lightened(0.18f), 0.45f), 1.6f, true);

        // 衣褶与受光面：让棉衣有体积，不是一块平色
        var lit = pal.Coat.Lightened(0.22f);
        DrawLine(shoulder + side * 9.4f, hip + side * 8.4f, new Color(lit, 0.55f), 1.6f, true);
        DrawLine(shoulder + side * 3f + new Vector2(2, 6), hip + side * 2f + new Vector2(4, 2),
            new Color(pal.CoatDark, 0.45f), 1.5f, true);
        DrawLine(shoulder + side * -6f, shoulder + side * 4f, new Color(lit, 0.35f), 2.2f, true);

        // 腰带 / 弹带
        DrawLine(hip + side * -11f + new Vector2(0, -3), hip + side * 11f + new Vector2(0, -3),
            pal.Belt, 3.2f, true);

        // 东北军通信兵的挎包（斜挎带 + 包体 + 翻盖）
        if (Kind == FigureKind.NortheastArmy)
        {
            var strap = new Color(0.30f, 0.24f, 0.17f);
            DrawLine(shoulder + side * 6f, hip + side * 7f + new Vector2(-5, 3), strap, 2.2f, true);
            var bagC = hip + new Vector2(-8f, 3f);
            var bag = new[]
            {
                bagC + new Vector2(-5.5f, -6f), bagC + new Vector2(5f, -6.5f),
                bagC + new Vector2(5.5f, 6f), bagC + new Vector2(-5f, 6.5f),
            };
            Poly(bag, new Color(0.46f, 0.37f, 0.25f), 1.6f);
            DrawLine(bagC + new Vector2(-5f, -1.5f), bagC + new Vector2(5f, -2f),
                new Color(0.28f, 0.22f, 0.16f), 1.3f, true);           // 翻盖缝
            DrawRect(new Rect2(bagC + new Vector2(-1.2f, -1f), new Vector2(2.4f, 1.8f)),
                new Color(0.72f, 0.62f, 0.42f));                        // 铜扣
        }

        // —— 头 ——
        Vector2 neck = shoulder + new Vector2(Mathf.Sin(lean) * 3f, -2f);
        Vector2 head = neck + new Vector2(Mathf.Sin(lean) * 4f, -10f + _breath * 0.5f);
        DrawHead(head, pal, lean);

        // —— 前臂：比躯干略亮一档，才能从躯干上“翻”出来，不然手臂会糊成一块 ——
        var sleeve = pal.Coat.Lightened(0.10f);
        Vector2 handA = HandTarget(true, phase, stride, lean);
        Vector2 elbowA = SolveIK(shoulder + side * 7.5f, handA, 17.5f, 17.5f, -1f);
        Tube(shoulder + side * 7.5f, elbowA, sleeve, 8.5f);
        Tube(elbowA, handA, sleeve, 7.5f);
        DrawCircle(handA, 3.5f, pal.Skin);

        // —— 枪 ——
        if (Kind != FigureKind.Civilian)
            DrawRifle(handB, handA);

        // 脚底的雪尘（只在移动时）
        if (moving && stride > 0.25f)
        {
            float puff = 0.35f + 0.25f * Mathf.Sin(_time * 9f);
            DrawSetTransform(new Vector2(0, -1), 0, new Vector2(1, 0.3f));
            DrawCircle(new Vector2(-8, 0), 6.5f * stride, new Color(0.88f, 0.90f, 0.93f, 0.10f * puff));
            DrawCircle(new Vector2(9, 0), 5f * stride, new Color(0.88f, 0.90f, 0.93f, 0.08f * puff));
        }

        if (HurtFlash > 0f)
        {
            // 受击反馈：一圈圈极淡的同心圆叠成柔光，不是一个实心粉圆片。
            // 实心圆会在人物身后留下一块“贴纸”，反而更假。
            DrawSetTransform(new Vector2(0, -2), 0, new Vector2(FacingRight ? 1f : -1f, 1f));
            for (int i = 0; i < 4; i++)
            {
                float r = 34f - i * 7f;
                DrawCircle(new Vector2(0, -56), r,
                    new Color(0.82f, 0.18f, 0.14f, 0.045f * HurtFlash));
            }
        }

        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    private void DrawLeg(Vector2 knee, Vector2 foot, Color color, float scale)
    {
        var hip = new Vector2(Mathf.Sin(Phase) * 1.6f * Mathf.Clamp(Speed / 105f, 0, 1.3f),
            Crouching ? -32f : -46f + Mathf.Sin(Phase * 2f) * 1.4f * Mathf.Clamp(Speed / 105f, 0, 1.3f));
        if (!Grounded) hip = new Vector2(0, -47f);
        Tube(hip, knee, color, 10f * scale);
        Tube(knee, foot, color, 8.4f * scale);

        // 棉靴：靴筒 + 前掌 + 后跟。后跟必须有，否则脚就是一块楔子，
        // 踩在雪上也不会有“压下去”的重量感。
        var bootC = new Color(0.17f, 0.16f, 0.15f);
        var boot = new[]
        {
            foot + new Vector2(-5.6f, -7.2f), foot + new Vector2(1.4f, -7.6f),
            foot + new Vector2(3.0f, -3.0f), foot + new Vector2(10.6f, -2.4f),
            foot + new Vector2(11.2f, 1.4f), foot + new Vector2(-5.6f, 1.8f),
        };
        Poly(boot, bootC, 1.7f);
        // 靴口翻边的受光面 + 靴底暗线（脚落地时把地面压住）
        DrawLine(foot + new Vector2(-5.2f, -6.2f), foot + new Vector2(1.2f, -6.6f),
            new Color(bootC.Lightened(0.42f), 0.80f), 1.8f, true);
        DrawLine(foot + new Vector2(-5.2f, -0.4f), foot + new Vector2(10.4f, -0.6f),
            new Color(bootC.Lightened(0.18f), 0.42f), 1.1f, true);
        DrawLine(foot + new Vector2(-5.4f, 1.5f), foot + new Vector2(11.0f, 1.2f),
            new Color(0.06f, 0.06f, 0.07f, 0.55f), 1.4f, true);
    }

    private void DrawHead(Vector2 head, Pal p, float lean)
    {
        // 脖子
        Tube(head + new Vector2(-2, 6), head + new Vector2(0, 13), p.SkinDark, 6.5f);

        // 头骨：不是正圆——后脑略鼓、颧骨与前颌收进去，侧面才有“人”的形状
        var skull = new[]
        {
            head + new Vector2(-8.6f, -2.4f), head + new Vector2(-7.0f, -8.4f),
            head + new Vector2(-2.2f, -10.4f), head + new Vector2(3.4f, -9.6f),
            head + new Vector2(8.0f, -6.0f), head + new Vector2(9.2f, -1.4f),
            head + new Vector2(7.4f, 4.0f), head + new Vector2(4.0f, 7.6f),
            head + new Vector2(-1.6f, 8.2f), head + new Vector2(-7.2f, 4.6f),
        };
        Poly(skull, p.Skin, 2f);

        // 后脑的背光面 + 下颌的转折：两块暗面把脸“垫”出一个体积
        DrawColoredPolygon(new[]
        {
            head + new Vector2(-8.4f, -2.2f), head + new Vector2(-6.8f, -8.2f),
            head + new Vector2(-2.4f, -10.0f), head + new Vector2(-1.2f, -2.0f),
            head + new Vector2(-2.0f, 8.0f), head + new Vector2(-7.0f, 4.4f),
        }, new Color(p.SkinDark, 0.55f));
        DrawColoredPolygon(new[]
        {
            head + new Vector2(7.2f, 4.2f), head + new Vector2(4.0f, 7.6f),
            head + new Vector2(-1.6f, 8.2f), head + new Vector2(1.0f, 2.6f),
            head + new Vector2(7.2f, 0.2f),
        }, new Color(p.SkinDark, 0.30f));

        // 鼻梁与鼻尖：侧面轮廓最关键的一笔
        DrawColoredPolygon(new[]
        {
            head + new Vector2(6.6f, -4.6f), head + new Vector2(10.4f, 0.2f),
            head + new Vector2(7.0f, 1.0f),
        }, p.Skin);
        DrawLine(head + new Vector2(9.6f, 1.2f), head + new Vector2(6.6f, 2.0f),
            new Color(p.SkinDark, 0.65f), 1f, true);

        // 耳朵
        DrawCircle(head + new Vector2(-4.2f, 1.2f), 2.1f, p.SkinDark);

        // 眼睛 / 嘴（朝向 +x）——眼睛必须落在帽檐之下。
        // 之前帽体一直盖到 y=+2，把眼鼻口全糊掉，人物就只剩一顶帽子和一个下巴。
        DrawCircle(head + new Vector2(5.0f, 0.6f), 1.05f, Outline);
        DrawLine(head + new Vector2(3.6f, 5.0f), head + new Vector2(6.8f, 5.2f), new Color(0.35f, 0.22f, 0.18f), 1.2f, true);
        // 脸颊冻红
        DrawCircle(head + new Vector2(2.4f, 3.4f), 2.2f, new Color(0.72f, 0.38f, 0.32f, 0.32f));

        switch (Kind)
        {
            case FigureKind.Japanese:
            {
                // 钢盔：扣在眉线以上（下缘 -3），前沿挑出帽檐，后颈垂布。
                var helm = new[]
                {
                    head + new Vector2(-11.0f, -3.0f), head + new Vector2(-10.2f, -7.8f),
                    head + new Vector2(-5.8f, -11.6f), head + new Vector2(1.6f, -12.4f),
                    head + new Vector2(7.2f, -10.0f), head + new Vector2(10.0f, -5.8f),
                    head + new Vector2(10.4f, -3.2f),
                };
                Poly(helm, p.Helmet, 1.8f);
                // 盔顶受光脊 + 下缘背光：一条亮一条暗，盔才是圆的
                DrawLine(head + new Vector2(-6.2f, -9.6f), head + new Vector2(4.4f, -11.0f),
                    new Color(p.Helmet.Lightened(0.30f), 0.85f), 2.4f, true);
                DrawLine(head + new Vector2(-9.0f, -3.6f), head + new Vector2(9.2f, -3.8f),
                    new Color(p.Helmet.Darkened(0.30f), 0.75f), 2f, true);
                // 帽檐：只在前额挑出，压不到眼睛
                Poly(new[]
                {
                    head + new Vector2(3.2f, -3.4f), head + new Vector2(12.6f, -3.8f),
                    head + new Vector2(12.0f, -1.9f), head + new Vector2(3.8f, -1.7f),
                }, p.Helmet.Darkened(0.08f), 1.4f);
                // 后颈垂布（随呼吸轻摆）
                var flap = new[]
                {
                    head + new Vector2(-11.4f, -2.6f),
                    head + new Vector2(-13.8f, 8.0f + _breath * 0.8f),
                    head + new Vector2(-8.6f, 11.0f + _breath * 0.8f),
                    head + new Vector2(-4.8f, 1.6f),
                };
                Poly(flap, new Color(0.40f, 0.38f, 0.29f), 1.4f);
                // 盔徽
                DrawCircle(head + new Vector2(7.4f, -6.6f), 1.7f, new Color(0.83f, 0.72f, 0.35f));
                break;
            }
            case FigureKind.NortheastArmy:
            {
                // 布棉帽：帽体扣到眉线（-3），护耳挂在耳后（-x 侧）垂下。
                // 之前护耳画在 +x 的脸这一侧，把鼻子整根盖掉。
                var cap = new[]
                {
                    head + new Vector2(-9.9f, -3.0f), head + new Vector2(-9.0f, -8.8f),
                    head + new Vector2(-1.2f, -12.2f), head + new Vector2(7.4f, -9.6f),
                    head + new Vector2(9.9f, -3.2f),
                };
                Poly(cap, new Color(0.36f, 0.40f, 0.40f), 1.9f);
                DrawLine(head + new Vector2(-9.0f, -4.2f), head + new Vector2(9.2f, -4.4f),
                    new Color(0.22f, 0.25f, 0.26f), 1.3f, true);
                DrawLine(head + new Vector2(-6.4f, -9.0f), head + new Vector2(4.0f, -10.6f),
                    new Color(0.50f, 0.54f, 0.53f, 0.70f), 2f, true);
                // 护耳（随呼吸轻摆）
                var earFlap = new[]
                {
                    head + new Vector2(-9.6f, -2.6f), head + new Vector2(-12.2f, 2.0f),
                    head + new Vector2(-10.6f, 10.5f + _breath * 0.9f), head + new Vector2(-6.2f, 7.0f),
                };
                Poly(earFlap, new Color(0.33f, 0.37f, 0.37f), 1.5f);
                // 下颌系带
                DrawLine(head + new Vector2(-8.8f, 0.4f), head + new Vector2(3.2f, 8.4f),
                    new Color(0.25f, 0.28f, 0.28f, 0.80f), 1.4f, true);
                break;
            }
            default:
            {
                // 平民：头巾包住头顶，额前压一道褶，脑后打结垂下布尾
                var scarf = new[]
                {
                    head + new Vector2(-9.6f, -1.8f), head + new Vector2(-8.8f, -8.6f),
                    head + new Vector2(-0.6f, -12.2f), head + new Vector2(7.2f, -9.4f),
                    head + new Vector2(9.6f, -2.4f), head + new Vector2(3.0f, -2.6f),
                };
                Poly(scarf, new Color(0.70f, 0.66f, 0.56f), 1.7f);
                DrawLine(head + new Vector2(-8.4f, -5.4f), head + new Vector2(8.2f, -5.0f),
                    new Color(0.50f, 0.47f, 0.40f, 0.80f), 1.2f, true);
                // 脑后的结与布尾
                DrawCircle(head + new Vector2(-10.2f, 1.2f), 2.4f, new Color(0.66f, 0.62f, 0.53f));
                DrawLine(head + new Vector2(-10.6f, 2.6f), head + new Vector2(-12.4f, 12.4f + _breath * 0.7f),
                    new Color(0.66f, 0.62f, 0.53f), 2.4f, true);
                // 棉袍的宽领（在颈根，不在下巴）
                var collar = new[]
                {
                    head + new Vector2(-8.4f, 8.0f), head + new Vector2(0f, 15f),
                    head + new Vector2(8.4f, 8.0f), head + new Vector2(3.4f, 11.0f), head + new Vector2(-3.4f, 11.0f),
                };
                Poly(collar, new Color(0.33f, 0.28f, 0.26f), 1.4f);
                break;
            }
        }
    }

    private Vector2 HandTarget(bool front, float phase, float stride, float lean)
    {
        if (Aim > 0.45f)
        {
            // 双手端枪：右手在前，左手托后
            Vector2 f = new(27f, -66f);
            Vector2 b = new(9f, -62f);
            if (Crouching) { f += new Vector2(0, 13); b += new Vector2(0, 13); }
            return front ? f : b;
        }

        if (!Grounded)
            return front ? new Vector2(15f, -74f) : new Vector2(-2f, -80f);

        // 站立 / 行走：枪竖在身侧，双手一高一低都握在枪身上。
        // 手必须落在躯干前缘之外（躯干前缘约 x=12.7），否则手臂整根埋进身体里，
        // 远看只剩一条竖直的板子——这是最容易露馅的“纸片”来源。
        float swing = stride * 5f * Mathf.Sin(phase);
        if (Crouching)
            return front ? new Vector2(17f, -28f) : new Vector2(12f, -41f);
        if (front)
            return new Vector2(17f - swing * 0.6f, -43f + Mathf.Sin(phase * 2f) * 0.6f);
        return new Vector2(12f + swing * 0.5f, -57f + Mathf.Sin(phase * 2f + 1f) * 0.6f);
    }

    private void DrawRifle(Vector2 backHand, Vector2 frontHand)
    {
        // 三个姿态各自给出「枪托 → 枪口」的轴向：
        // 瞄准=水平、腾空=斜上抱枪、站立=近乎竖直立在身侧（不是横在胸前的长棍）
        Vector2 dir;
        Vector2 butt;
        Vector2 tip;
        if (Aim > 0.45f)
        {
            dir = new Vector2(1f, 0f);
            butt = frontHand - dir * 22f;
            tip = frontHand + dir * 24f;
        }
        else if (!Grounded)
        {
            dir = new Vector2(0.55f, -0.83f).Normalized();
            butt = frontHand - dir * 16f;
            tip = frontHand + dir * 22f;
        }
        else
        {
            // 略向前倾，枪身离开大腿，轮廓才不会和腿糊在一起
            dir = new Vector2(0.22f, -1f).Normalized();
            butt = frontHand - dir * 22f;
            tip = frontHand + dir * 32f;
            // 后手扶在枪身中段
            backHand = frontHand + dir * 5f;
        }

        var metal = new Color(0.16f, 0.16f, 0.17f);
        var wood = new Color(0.38f, 0.25f, 0.16f);

        // 枪托 → 枪管：托底宽、托颈收窄，另加护木和枪管。
        // 全部等宽线画出来时，枪在远处只是一根黑棍，人物立刻掉价。
        var stockB = butt + dir * 13f;
        var perp = new Vector2(-dir.Y, dir.X);
        Poly(new[]
        {
            butt + perp * 3.4f - dir * 1.6f,
            stockB + perp * 2.1f,
            stockB - perp * 2.1f,
            butt - perp * 3.4f - dir * 1.6f,
        }, wood, 1.3f);
        // 护木（木）+ 枪管（金属）
        DrawLine(stockB - dir * 1f, stockB + dir * 9f, Outline, 5.4f, true);
        DrawLine(stockB - dir * 1f, stockB + dir * 9f, wood.Darkened(0.12f), 3.8f, true);
        DrawLine(stockB + dir * 9f, tip, Outline, 3.2f, true);
        DrawLine(stockB + dir * 9f, tip, metal, 2.0f, true);
        // 枪栓 / 准星
        DrawCircle(backHand + dir * 9f, 1.7f, Outline);
        DrawLine(tip - dir * 3f + new Vector2(0, -2.6f), tip - dir * 3f + new Vector2(0, 2.6f), metal, 1.5f, true);

        if (MuzzleFlash > 0.01f)
        {
            float a = MuzzleFlash;
            var c = new Color(1f, 0.82f, 0.42f, 0.85f * a);
            DrawCircle(tip, 7.5f * a, new Color(1f, 0.70f, 0.30f, 0.25f * a));
            DrawColoredPolygon(new[]
            {
                tip + new Vector2(0, -2.2f), tip + dir * (16f * a), tip + new Vector2(0, 2.2f),
            }, c);
            DrawCircle(tip, 2.6f * a, new Color(1f, 0.96f, 0.80f));
        }
    }

    // ================= 数学 / 绘制工具 =================

    private static Vector2 SolveIK(Vector2 root, Vector2 target, float l1, float l2, float bendSign)
    {
        var d = target - root;
        float dist = Mathf.Clamp(d.Length(), 0.001f, l1 + l2 - 0.01f);
        var dir = d.Normalized();
        float a = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);
        float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));
        var mid = root + dir * a;
        var perp = new Vector2(dir.Y, -dir.X) * bendSign;
        return mid + perp * h;
    }

    private void Tube(Vector2 a, Vector2 b, Color color, float width)
    {
        // 手绘漫画风手臂：
        // 1) 粗墨描边（先画一个矩形外框）
        // 2) 平涂色块填充
        // 3) 关节同色圆补缝
        // 4) 一条硬边阴影条（平涂长条，不做渐变）
        // 5) 卡通高光小圆点

        Vector2 dir = (b - a).Normalized();
        Vector2 side = new Vector2(-dir.Y, dir.X);
        Vector2 p1 = a + side * width * 0.5f;
        Vector2 p2 = b + side * width * 0.5f;
        Vector2 p3 = b - side * width * 0.5f;
        Vector2 p4 = a - side * width * 0.5f;

        var rect = new Vector2[] { p1, p2, p3, p4 };
        DrawColoredPolygon(rect, color);
        DrawPolyline(rect, InkStyle.InkColor, InkStyle.BoldStroke, true);

        // 关节同色圆 + 墨描边环（用 DrawArc 画整圆描边）
        DrawCircle(a, width * 0.5f, color);
        DrawCircle(b, width * 0.5f, color);
        DrawArc(a, width * 0.5f + 0.5f, 0f, Mathf.Tau, 16, InkStyle.InkColor, 1.4f, true);
        DrawArc(b, width * 0.5f + 0.5f, 0f, Mathf.Tau, 16, InkStyle.InkColor, 1.4f, true);

        // 硬边阴影条（背光侧一条扁长条，平涂）
        float shadeW = width * 0.28f;
        Vector2 shA1 = a - side * width * 0.22f;
        Vector2 shA2 = b - side * width * 0.22f;
        Vector2 shA3 = b - side * (width * 0.22f + shadeW);
        Vector2 shA4 = a - side * (width * 0.22f + shadeW);
        DrawColoredPolygon(new[] { shA1, shA2, shA3, shA4 }, InkStyle.Shade(color, 0.6f));

        // 卡通高光点（一个小白点在右上受光角）
        Vector2 mid = (a + b) * 0.5f;
        Vector2 hi = mid + side * width * 0.35f + dir * width * 0.15f;
        DrawCircle(hi, Mathf.Max(1.4f, width * 0.10f), new Color(1f, 1f, 1f, 0.65f));
    }

    private void Poly(Vector2[] pts, Color fill, float outline)
    {
        // 手绘漫画风多边形：先平涂再粗墨描边
        DrawColoredPolygon(pts, fill);
        var closed = new Vector2[pts.Length + 1];
        System.Array.Copy(pts, closed, pts.Length);
        closed[pts.Length] = pts[0];
        float bw = outline > 2f ? InkStyle.BoldStroke : outline;
        DrawPolyline(closed, InkStyle.InkColor, bw, true);
    }

    private readonly struct Pal
    {
        public readonly Color Coat, CoatDark, Skin, SkinDark, Belt, Helmet;

        public Pal(Color coat, Color coatDark, Color skin, Color skinDark, Color belt, Color helmet)
        {
            Coat = coat;
            CoatDark = coatDark;
            Skin = skin;
            SkinDark = skinDark;
            Belt = belt;
            Helmet = helmet;
        }

        public Color Dark => Coat.Darkened(0.28f);
    }

    private static Pal Palette(FigureKind kind) => kind switch
    {
        // 手绘漫画风：饱和平涂色板，色彩鲜艳，不做渐变。
        // 日军卡其军装（偏黄）、东北军灰蓝棉军装、平民土黄棉袍
        FigureKind.Japanese => new Pal(
            InkStyle.JPCoat, InkStyle.JPCoatShadow,
            InkStyle.Skin, InkStyle.SkinShadow,
            new Color(0.34f, 0.29f, 0.20f), InkStyle.JHelmet),
        FigureKind.NortheastArmy => new Pal(
            InkStyle.NACoat, InkStyle.NACoatShadow,
            InkStyle.Skin, InkStyle.SkinShadow,
            new Color(0.30f, 0.28f, 0.24f), InkStyle.NACap),
        _ => new Pal(
            InkStyle.CivilianCoat, InkStyle.CivilianCoatShadow,
            InkStyle.Skin, InkStyle.SkinShadow,
            new Color(0.38f, 0.32f, 0.25f), new Color(0.72f, 0.68f, 0.58f)),
    };
}
