using Godot;

namespace Witness;

/// <summary>
/// v3.66 · 手绘上色漫画风（Hand-drawn cel-shading manga）物件绘制的唯一入口。
///
/// 用户红线：整个作品只用「手绘上色漫画风」，不许混入钢笔淡彩 / 写实渲染 /
/// 细密钢笔画。本文件把「铁轨」与「手榴弹」两类此前串味的物件统一到
/// <see cref="InkStyle"/> 的语言：
///   • 3px 深墨棕粗描边（小物件按尺寸缩到 2.0~2.6px，仍是明显粗墨边）
///   • base → shadow → highlight 三档 **平涂**，块与块之间是硬边，不做插值渐变
///   • 卡通高光只用小圆点 / 短直线 / 放射短线，不用模糊光晕、羽化、半透明柔光
///   • 接触阴影是压扁的硬边椭圆
///
/// 两关（N918Level / JiangqiaoLevel）与 HUD 都改为调用这里，避免同一物件
/// 在四个地方各画一遍、风格又各自跑偏。
/// </summary>
public static class MangaPropArt
{
    // —— 铁轨三档平涂色板 ——
    public static readonly Color BallastBase = new(0.66f, 0.63f, 0.56f);
    public static readonly Color BallastShadow = new(0.44f, 0.42f, 0.38f);
    public static readonly Color BallastHighlight = new(0.86f, 0.84f, 0.78f);

    public static readonly Color SleeperBase = InkStyle.Wood;
    public static readonly Color SleeperShadow = InkStyle.WoodShadow;
    public static readonly Color SleeperHighlight = InkStyle.WoodHighlight;
    public static readonly Color CharredWood = new(0.22f, 0.18f, 0.15f);
    public static readonly Color CharredWoodShadow = new(0.13f, 0.11f, 0.10f);

    public static readonly Color SteelBase = new(0.46f, 0.48f, 0.50f);
    public static readonly Color SteelShadow = new(0.27f, 0.29f, 0.31f);
    public static readonly Color SteelHighlight = new(0.80f, 0.82f, 0.82f);

    // —— 手榴弹三档平涂色板 ——
    public static readonly Color IronBase = new(0.40f, 0.42f, 0.42f);
    public static readonly Color IronShadow = new(0.24f, 0.26f, 0.26f);
    public static readonly Color IronHighlight = new(0.66f, 0.68f, 0.68f);
    public static readonly Color BrassBase = new(0.78f, 0.63f, 0.30f);
    public static readonly Color BrassShadow = new(0.52f, 0.41f, 0.18f);
    public static readonly Color BrassHighlight = new(0.94f, 0.84f, 0.52f);

    private const float PropStroke = 2.4f;   // 小物件主轮廓：粗墨边，但别把 14px 宽的弹体吃光

    // ================= 铁轨 =================

    /// <summary>
    /// 道砟带：沿 x 撒不规则碎石（上下边缘锯齿、大小高低不齐），不是一块
    /// 平涂矩形 + 直边高光带。取代旧版「半透明水彩罩染 / 整齐矩形」的过规整观感。
    /// </summary>
    public static void Ballast(CanvasItem c, float x0, float x1, float y)
    {
        if (x1 - x0 < 12f) return;
        var rng = new RandomNumberGenerator();
        rng.Seed = (uint)(x0 * 13 + 7);
        float step = 13f;
        for (float x = x0; x < x1; x += step)
        {
            float w = step * (0.55f + rng.RandfRange(0f, 0.9f));
            float top = y + 1f + rng.RandfRange(-2f, 3f);
            float h = 17f + rng.RandfRange(-5f, 8f);
            InkStyle.PenStroke(c, new[]
            {
                new Vector2(x - w * 0.5f, top + h),
                new Vector2(x + w * 0.5f, top + h),
                new Vector2(x + w * 0.32f, top),
                new Vector2(x - w * 0.30f, top + 2f),
            }, rng.Randf() < 0.5f ? BallastBase : BallastShadow,
                InkStyle.InkColor, InkStyle.Stroke);
            // 散石小点
            c.DrawCircle(new Vector2(x + rng.RandfRange(-w, w) * 0.5f, top + h * 0.62f),
                1.4f + rng.RandfRange(0f, 2f), BallastShadow);
        }
    }

    /// <summary>枕木：粗墨框 + base/shadow/highlight 三档平涂 + 一条卡通高光短线。</summary>
    public static void Sleeper(CanvasItem c, Rect2 r, bool charred, bool highlightOn)
    {
        Color baseC = charred ? CharredWood : SleeperBase;
        Color shadowC = charred ? CharredWoodShadow : SleeperShadow;
        InkStyle.InkRect(c, r, baseC, InkStyle.InkColor, PropStroke);
        // 下半平涂暗面
        c.DrawRect(new Rect2(r.Position.X + 1.4f, r.Position.Y + r.Size.Y * 0.52f,
            r.Size.X - 2.8f, r.Size.Y * 0.36f), shadowC);
        // 卡通高光：一小段短直线，不是贯通白线
        if (highlightOn && !charred)
            InkStyle.KawaiiLineHighlight(c,
                new Vector2(r.Position.X + 2.5f, r.Position.Y + 2.2f),
                new Vector2(r.Position.X + r.Size.X - 3.5f, r.Position.Y + 2.2f),
                SleeperHighlight, 2f);
    }

    /// <summary>钢轨：粗墨底 → 钢色平涂 → 亮面短线 → 短促雪帽（全实色，无半透明）。</summary>
    public static void RailSegment(CanvasItem c, float x0, float x1, float y, bool snowCap)
    {
        if (x1 - x0 < 4f) return;
        c.DrawLine(new Vector2(x0, y), new Vector2(x1, y), InkStyle.InkColor, 6f, true);
        c.DrawLine(new Vector2(x0, y), new Vector2(x1, y), SteelBase, 3.4f, true);
        c.DrawLine(new Vector2(x0 + 2f, y - 1f), new Vector2(x1 - 2f, y - 1f),
            SteelHighlight, 1.2f, true);
        if (snowCap && x1 - x0 > 26f)
            c.DrawLine(new Vector2(x0 + 8f, y - 2.4f),
                new Vector2(Mathf.Min(x1 - 6f, x0 + 30f), y - 2.4f),
                InkStyle.Snow, 2.2f, true);
    }

    /// <summary>
    /// 钢板桥面（仅嫩江江桥关卡使用，史实有桥）：三档平涂 + 粗墨边 + 铆钉小圆点。
    /// 九一八关卡不调用本方法——那里没有桥。
    /// </summary>
    public static void DeckPlate(CanvasItem c, float x0, float x1, float y, float h)
    {
        if (x1 - x0 < 12f) return;
        InkStyle.PenStroke(c, new[]
        {
            new Vector2(x0, y), new Vector2(x1, y),
            new Vector2(x1, y + h), new Vector2(x0, y + h),
        }, SteelShadow, InkStyle.InkColor, InkStyle.BoldStroke);
        c.DrawRect(new Rect2(x0 + 2f, y + 2f, x1 - x0 - 4f, 3.5f), SteelHighlight);
        c.DrawRect(new Rect2(x0 + 2f, y + 5.5f, x1 - x0 - 4f, h - 8f), SteelBase);
        // 铆钉 = 卡通高光小圆点
        for (float x = x0 + 16f; x < x1 - 8f; x += 30f)
            c.DrawCircle(new Vector2(x, y + h * 0.55f), 1.7f, BrassHighlight);
    }

    /// <summary>断轨口：焦黑爆破痕 + 翻起错落的道砟 + 扭曲翻卷的轨头（漫画风硬边，强调「被炸断」而非整齐切口）。</summary>
    public static void TornRailEnd(CanvasItem c, float x, float y, float dir, uint seed)
    {
        var rng = new RandomNumberGenerator();
        rng.Seed = seed;
        var ink = InkStyle.InkColor;

        // 散落碎石：缺口附近先撒一层不规则石粒，打破「干净一刀切」的边界
        ScatterBallast(c, x - dir * 34f, x + dir * 20f, y, seed + 911u, 12);

        // 焦黑爆破痕：多块大小/高低都不齐的不规则实色块堆出炸点
        // （宽度随机、上下偏移随机、用 DrawColoredPolygon 而非整齐矩形）
        for (int i = 0; i < 9; i++)
        {
            float cx = x - dir * (2f + i * 5f + rng.RandfRange(-4f, 4f));
            float cw = 9f + rng.RandfRange(0f, 18f);
            float ch = 3f + rng.RandfRange(0f, 8f);
            float cy = y + 14f + rng.RandfRange(-5f, 9f);
            float skew = rng.RandfRange(-3f, 3f);
            c.DrawColoredPolygon(new[]
            {
                new Vector2(cx - cw * 0.5f, cy + ch * 0.5f),
                new Vector2(cx - cw * 0.2f + skew, cy - ch * 0.5f),
                new Vector2(cx + cw * 0.5f, cy + ch * 0.2f),
                new Vector2(cx + cw * 0.15f - skew, cy + ch * 0.6f),
            }, i % 2 == 0 ? CharredWood : ink);
        }

        // 翻起道砟：6~8 块硬边多边形，高低错落、前后散开，绝不排成整齐一条
        for (int i = 0; i < 7; i++)
        {
            float w = 6f + rng.RandfRange(0f, 15f);
            float px = x + dir * (1f + i * 7f + rng.RandfRange(-5f, 5f));
            float py = y + 15f + rng.RandfRange(-3f, 4f);
            float lift = 4f + rng.RandfRange(0f, 17f);
            float back = rng.RandfRange(-3f, 4f);
            InkStyle.PenStroke(c, new[]
            {
                new Vector2(px - w * 0.5f, py + back),
                new Vector2(px + w * 0.5f, py - back),
                new Vector2(px + w * 0.32f, py - lift),
                new Vector2(px - w * 0.30f, py - lift * 0.55f),
            }, i % 2 == 0 ? BallastBase : BallastShadow, ink, InkStyle.Stroke);
        }

        // 上翘 / 扭曲的轨头：多段折线画出弯折翻卷，不是直挺挺一截；
        // 再加一截「飞脱的弯轨」斜插出去，强调爆炸把钢轨掀翻的力道
        foreach (float ry in new[] { y, y + 13f })
        {
            int segs = 4 + (int)rng.RandfRange(0f, 2f);
            float total = 18f + rng.RandfRange(0f, 14f);
            float a = 0.5f + rng.RandfRange(-0.15f, 0.5f);
            Vector2[] inkPts = new Vector2[segs + 1];
            inkPts[0] = new Vector2(x, ry);
            float lx = x, ly = ry;
            for (int s = 0; s < segs; s++)
            {
                float segLen = total / segs * (0.7f + rng.RandfRange(0f, 0.7f));
                a += rng.RandfRange(-0.6f, 0.7f);
                lx += dir * segLen * Mathf.Cos(a);
                ly -= segLen * Mathf.Sin(a);
                inkPts[s + 1] = new Vector2(lx, ly);
            }
            c.DrawPolyline(inkPts, ink, 5f, true);
            Vector2[] steelPts = new Vector2[segs + 1];
            for (int k = 0; k <= segs; k++) steelPts[k] = inkPts[k] + new Vector2(0f, -1.5f);
            c.DrawPolyline(steelPts, SteelBase, 2.6f, true);
            InkStyle.KawaiiHighlight(c, inkPts[segs], 1.8f, SteelHighlight);
        }

        // 一截飞脱的弯轨：从炸点斜抛出，落点随机，强化「钢轨被掀翻」而非整齐切断
        float fl = rng.RandfRange(14f, 26f);
        float fa = (0.9f + rng.RandfRange(-0.2f, 0.4f)) * (rng.Randf() < 0.5f ? 1f : -1f);
        var fp0 = new Vector2(x, y + 6f);
        var fp1 = fp0 + new Vector2(dir * fl * Mathf.Cos(fa), -fl * Mathf.Sin(fa) - 6f);
        var fp2 = fp1 + new Vector2(dir * fl * 0.5f * Mathf.Cos(fa + 0.5f), -fl * 0.4f * Mathf.Sin(fa + 0.5f));
        c.DrawPolyline(new[] { fp0, fp1, fp2 }, ink, 4.4f, true);
        c.DrawPolyline(new[] { fp0 + new Vector2(0, -1.4f), fp1 + new Vector2(0, -1.4f), fp2 + new Vector2(0, -1.4f) },
            SteelBase, 2.2f, true);
    }

    /// <summary>在一段区间内撒散落道砟碎片 + 零星焦痕，用于断口 / 弹坑内，避免出现「干净一刀切」的缺口。</summary>
    public static void ScatterBallast(CanvasItem c, float x0, float x1, float y, uint seed, int count = 10)
    {
        if (x1 - x0 < 4f) return;
        var rng = new RandomNumberGenerator();
        rng.Seed = seed;
        for (int i = 0; i < count; i++)
        {
            float px = x0 + rng.RandfRange(0f, x1 - x0);
            float gy = y + 3f + rng.RandfRange(-3f, 15f);
            float w = 3f + rng.RandfRange(0f, 7f);
            float h = 2f + rng.RandfRange(0f, 4f);
            c.DrawRect(new Rect2(px, gy, w, h), rng.Randf() < 0.5f ? BallastBase : BallastShadow);
        }
    }

    // ================= 手榴弹 =================

    /// <summary>引信火花：实心亮圆 + 四条放射短线的卡通光芒（不是柔光圆）。</summary>
    private static void FuseSpark(CanvasItem c, Vector2 p)
    {
        var glow = new Color(1.00f, 0.84f, 0.34f);
        c.DrawCircle(p, 3.2f, glow);
        c.DrawCircle(p, 1.5f, new Color(1f, 0.98f, 0.82f));
        for (int i = 0; i < 4; i++)
        {
            float a = Mathf.Pi * 0.25f + i * Mathf.Pi * 0.5f;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            c.DrawLine(p + d * 4.6f, p + d * 7.8f, glow, 1.8f, true);
        }
    }

    /// <summary>
    /// M24 / 巩县仿造木柄手榴弹（东北军制式）：局部坐标，弹头在上方（y -26..+7）。
    /// 粗墨描边 + 三档平涂 + 卡通高光，与关卡里所有程序化物件同源。
    /// </summary>
    public static void StickGrenade(CanvasItem c, float fuse)
    {
        var ink = InkStyle.InkColor;

        // 木柄：上宽下窄的锥形，粗墨边 + 平涂
        InkStyle.PenStroke(c, new[]
        {
            new Vector2(-4.2f, -12f), new Vector2(4.2f, -12f),
            new Vector2(3.1f, 5f), new Vector2(-3.1f, 5f),
        }, SleeperBase, ink, PropStroke);
        // 右侧平涂暗面
        c.DrawColoredPolygon(new[]
        {
            new Vector2(1.2f, -11.4f), new Vector2(3.7f, -11.4f),
            new Vector2(2.7f, 4.4f), new Vector2(1.2f, 4.4f),
        }, SleeperShadow);
        // 卡通高光短直线（木柄受光面）
        InkStyle.KawaiiLineHighlight(c, new Vector2(-2.6f, -9f), new Vector2(-1.9f, 1f),
            SleeperHighlight, 1.8f);

        // 颈箍
        InkStyle.InkRect(c, new Rect2(-4.8f, -14.5f, 9.6f, 3.4f), SteelShadow, ink, 1.8f);
        c.DrawRect(new Rect2(-4.2f, -14.2f, 8.4f, 1.2f), SteelHighlight);

        // 铸铁弹头：切角圆柱，粗墨边 + 平涂
        InkStyle.PenStroke(c, new[]
        {
            new Vector2(-6.6f, -23.5f), new Vector2(-5.2f, -26f),
            new Vector2(5.2f, -26f), new Vector2(6.6f, -23.5f),
            new Vector2(6.6f, -14.5f), new Vector2(-6.6f, -14.5f),
        }, IronBase, ink, PropStroke);
        // 左侧平涂暗面
        c.DrawColoredPolygon(new[]
        {
            new Vector2(-6.0f, -23.2f), new Vector2(-4.9f, -25.4f),
            new Vector2(-3.2f, -25.4f), new Vector2(-3.2f, -14.9f),
            new Vector2(-6.0f, -14.9f),
        }, IronShadow);
        // 顶盖横槽 + 卡通高光点
        c.DrawLine(new Vector2(-5.8f, -22.6f), new Vector2(5.8f, -22.6f), ink, InkStyle.Stroke, true);
        InkStyle.KawaiiLineHighlight(c, new Vector2(2.0f, -24.2f), new Vector2(5.0f, -24.2f),
            IronHighlight, 1.6f);
        InkStyle.KawaiiHighlight(c, new Vector2(3.6f, -18.6f), 1.2f, IronHighlight);

        // 柄尾拉盖
        InkStyle.InkRect(c, new Rect2(-3.4f, 4.4f, 6.8f, 2.8f), SteelShadow, ink, 1.6f);
        // 拉绳环：粗墨圈 + 亮面高光
        c.DrawArc(new Vector2(0f, 9.2f), 2.6f, 0f, Mathf.Tau, 12, ink, 2.0f, true);
        c.DrawArc(new Vector2(0f, 9.2f), 2.6f, Mathf.Pi * 1.1f, Mathf.Pi * 1.7f, 6,
            BrassHighlight, 1.2f, true);

        if (fuse < 0.6f && (int)(fuse * 14f) % 2 == 0)
            FuseSpark(c, new Vector2(0f, -29.5f));
    }

    /// <summary>
    /// 日军九一式手雷（1931 年列装）：卵形/近圆柱铸铁弹体带预制破片横沟、
    /// 黄铜击发管与拉销环。不是木柄弹。同样粗墨描边 + 三档平涂。
    /// </summary>
    public static void Type91Grenade(CanvasItem c, float fuse)
    {
        var ink = InkStyle.InkColor;

        InkStyle.PenStroke(c, new[]
        {
            new Vector2(-7.0f, -16.5f), new Vector2(-5.4f, -20.0f),
            new Vector2(5.4f, -20.0f), new Vector2(7.0f, -16.5f),
            new Vector2(7.0f, -6.5f), new Vector2(5.6f, -3.0f),
            new Vector2(-5.6f, -3.0f), new Vector2(-7.0f, -6.5f),
        }, IronBase, ink, PropStroke);
        // 左侧平涂暗面
        c.DrawColoredPolygon(new[]
        {
            new Vector2(-6.3f, -16.2f), new Vector2(-5.1f, -19.4f),
            new Vector2(-2.8f, -19.4f), new Vector2(-2.8f, -3.6f),
            new Vector2(-6.3f, -6.8f),
        }, IronShadow);
        // 预制破片横沟（内部缝线，次描边）
        foreach (float gy in new[] { -15.0f, -11.0f, -7.0f })
            c.DrawLine(new Vector2(-6.2f, gy), new Vector2(6.2f, gy), ink, InkStyle.Stroke, true);
        // 卡通高光：右侧一条短直线 + 小圆点
        InkStyle.KawaiiLineHighlight(c, new Vector2(3.2f, -18.2f), new Vector2(5.4f, -18.2f),
            IronHighlight, 1.6f);
        InkStyle.KawaiiHighlight(c, new Vector2(4.6f, -12.8f), 1.2f, IronHighlight);

        // 底座缘
        InkStyle.InkRect(c, new Rect2(-6.4f, -3.0f, 12.8f, 3.0f), IronShadow, ink, 1.6f);
        c.DrawRect(new Rect2(-5.6f, -2.6f, 11.2f, 1.0f), IronHighlight);

        // 顶部黄铜击发管
        InkStyle.InkRect(c, new Rect2(-2.6f, -27.0f, 5.2f, 7.5f), BrassBase, ink, 1.8f);
        c.DrawRect(new Rect2(-2.0f, -26.4f, 1.4f, 6.2f), BrassShadow);
        InkStyle.KawaiiLineHighlight(c, new Vector2(1.0f, -25.6f), new Vector2(1.0f, -21.0f),
            BrassHighlight, 1.4f);
        // 拉销环
        c.DrawArc(new Vector2(4.8f, -24.5f), 2.6f, 0f, Mathf.Tau, 10, ink, 1.8f, true);
        c.DrawArc(new Vector2(4.8f, -24.5f), 2.6f, Mathf.Pi * 1.1f, Mathf.Pi * 1.7f, 5,
            BrassHighlight, 1.2f, true);

        if (fuse < 0.6f && (int)(fuse * 14f) % 2 == 0)
            FuseSpark(c, new Vector2(0f, -30f));
    }

    /// <summary>HUD 小图标：同样粗墨边 + 三档平涂；用尽的手雷是暗色实心，不是半透明幽灵。</summary>
    public static void StickGrenadeIcon(CanvasItem c, Vector2 p, bool full)
    {
        var ink = InkStyle.InkColor;
        Color stick = full ? SleeperBase : new Color(0.36f, 0.34f, 0.32f);
        Color stickSh = full ? SleeperShadow : new Color(0.24f, 0.23f, 0.22f);
        Color head = full ? IronBase : new Color(0.32f, 0.33f, 0.33f);
        Color headSh = full ? IronShadow : new Color(0.22f, 0.23f, 0.23f);
        Color hi = full ? IronHighlight : new Color(0.42f, 0.42f, 0.42f);

        // 木柄（粗墨框 + 平涂暗面）
        InkStyle.InkRect(c, new Rect2(p.X - 2.0f, p.Y - 6f, 4.0f, 12f), stick, ink, 1.8f);
        c.DrawRect(new Rect2(p.X + 0.4f, p.Y - 5f, 1.2f, 10f), stickSh);
        // 弹头（切角圆柱，粗墨边）
        InkStyle.PenStroke(c, new[]
        {
            new Vector2(p.X - 4.0f, p.Y - 11.6f), new Vector2(p.X - 3.0f, p.Y - 13.4f),
            new Vector2(p.X + 3.0f, p.Y - 13.4f), new Vector2(p.X + 4.0f, p.Y - 11.6f),
            new Vector2(p.X + 4.0f, p.Y - 6.0f), new Vector2(p.X - 4.0f, p.Y - 6.0f),
        }, head, ink, 1.8f);
        c.DrawColoredPolygon(new[]
        {
            new Vector2(p.X - 3.5f, p.Y - 11.4f), new Vector2(p.X - 2.8f, p.Y - 13.0f),
            new Vector2(p.X - 1.2f, p.Y - 13.0f), new Vector2(p.X - 1.2f, p.Y - 6.4f),
            new Vector2(p.X - 3.5f, p.Y - 6.4f),
        }, headSh);
        // 顶盖 + 卡通高光点
        c.DrawRect(new Rect2(p.X - 2.1f, p.Y - 14.6f, 4.2f, 2.0f), hi);
        c.DrawRect(new Rect2(p.X - 2.1f, p.Y - 14.6f, 4.2f, 2.0f), ink, false, 1.4f);
        if (full) InkStyle.KawaiiHighlight(c, new Vector2(p.X + 2.2f, p.Y - 9.6f), 1.1f, hi);
    }
}
