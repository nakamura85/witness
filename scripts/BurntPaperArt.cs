using System;
using System.Collections.Generic;
using Godot;

namespace Witness;

/// <summary>
/// v3.83 纸张 UI：钢笔淡彩 / 水墨手绘的「旧纸」贴图绘制原语。
///
/// ★ 风格定调（用户 2026-11-13 亲自纠正，v3.81 起全作品唯一美术锚点）：
///   细而不均、随形体断续的墨线；斑驳罩染、有渐层与笔触；柔和淡墨阴影。
///   **不是**赛璐璐漫画风，**不是** 3px 均匀粗描边 + 三档硬边平涂。
///   实现手段是 PNG 贴图资产，不是运行时程序化几何色块。
///
/// 纸张（用户参考第二张图）＝ 米黄斑驳渐层 + 满纸揉皱折痕 +
///   四周一圈不规则锯齿状焦边（纸色 → 浅褐 → 深褐 → 炭黑）；
///   **没有**工整硬边框、**没有**规整的圆形烧穿孔、**没有**卷角三角。
///   这跟 v3.68~v3.82「规整矩形 + 几个圆烧孔 + 暗红横线」的打印证书感是两回事。
///
/// 「纸张不要全部都一样」：seed 决定使用 3 张焦边变体中的哪一张，
///   同一份记录（日期 + 标题哈希）每次打开一致。
/// 贴图资产由 tools/bridge_restyle/make_burnt_paper_textures.py 生成，可复现。
/// DrawSheetLegacy 是被否决的旧画法，仅作贴图缺失时的兜底。
/// </summary>
public static class BurntPaperArt
{
    private static readonly Color Ink = InkStyle.InkColor;
    private static readonly Color InkSoft = InkStyle.InkSoft;

    // =====================================================================
    // v3.83 纸张贴图（贴图资产，不是运行时程序化几何色块）
    // ---------------------------------------------------------------------
    // 用户 2026-11-13：「那些留言或者纸条的或者纸张的我都希望那个风格可以
    // 参考第二张这样的美术画风哦 跟你理解的烧坏的纸张不同」
    //   被否定的旧理解 = 规整矩形 + 3px 均匀粗墨框 + 三档平涂 + 圆形烧穿孔
    //                    （读起来像打印表格 / 证书）
    //   用户参考图     = 米黄斑驳渐层 + 满纸揉皱折痕 + 四周不规则锯齿焦边
    //                    （纸色→浅褐→深褐→炭黑），无硬边框、无规整圆孔
    // 实现：离线脚本自绘的 PNG 贴图（原创造纹，不复制带水印的图库素材），
    //       运行时用 DrawTextureRectRegion 做九宫格采样；中心格 1:1 平铺，
    //       所以任意面板尺寸都不会把纸纹拉伸变形。
    // 样式仍按 seed 分流 3 张变体，保持「纸张不要全部都一样」。
    // =====================================================================
    private const float SheetSrc = 384f;   // paper_burnt_v*.png 边长
    private const float SheetEdge = 80f;   // 该图九宫格边距（源像素）
    public const float SlipSrcSize = 256f; // paper_slip.png 边长（PaperSlip 共用同一约定）
    public const float SlipEdgeSize = 32f; // 该图九宫格边距（源像素）

    private static Texture2D[] _sheets;

    private static Texture2D[] Sheets
    {
        get
        {
            if (_sheets != null) return _sheets;
            _sheets = new[]
            {
                GD.Load<Texture2D>("res://assets/art/ui/paper_burnt_v1.png"),
                GD.Load<Texture2D>("res://assets/art/ui/paper_burnt_v2.png"),
                GD.Load<Texture2D>("res://assets/art/ui/paper_burnt_v3.png"),
            };
            return _sheets;
        }
    }

    /// <summary>
    /// 九宫格贴图绘制：四角固定、四边沿单轴拉伸、中心格拉伸。
    /// 9 块都取自同一张连续图，边界像素天然连续 —— 不采用「中心格平铺」，
    /// 因为中心裁块自身不具周期性，平铺会在纸面上留下淡矩形接缝（v3.83 首轮真帧实测）。
    /// 代价是纸纹随笔画面板尺寸略有横向拉伸；低频斑驳水渍被拉长后反而更像旧纸。
    /// </summary>
    public static void DrawNine(CanvasItem c, Texture2D tex, Rect2 r,
        float srcSize, float srcEdge, float edge)
    {
        if (tex == null || r.Size.X < 24f || r.Size.Y < 24f) return;
        float W = r.Size.X, H = r.Size.Y;
        float e = Mathf.Min(edge, Mathf.Min(W * 0.22f, H * 0.22f));
        float C = srcSize - 2f * srcEdge;
        if (C <= 0f) return;
        float x = r.Position.X, y = r.Position.Y;
        float x1 = x + e, y1 = y + e;
        float cw = W - 2f * e, ch = H - 2f * e;

        void Blit(Rect2 src, Rect2 dst) => c.DrawTextureRectRegion(tex, dst, src);

        // 四角：源 srcEdge² → 目标 e²（焦角，压缩不影响读感）
        Blit(new Rect2(0, 0, srcEdge, srcEdge), new Rect2(x, y, e, e));
        Blit(new Rect2(srcSize - srcEdge, 0, srcEdge, srcEdge), new Rect2(x + W - e, y, e, e));
        Blit(new Rect2(0, srcSize - srcEdge, srcEdge, srcEdge), new Rect2(x, y + H - e, e, e));
        Blit(new Rect2(srcSize - srcEdge, srcSize - srcEdge, srcEdge, srcEdge),
             new Rect2(x + W - e, y + H - e, e, e));

        // 四边：整段一次拉伸（不循环平铺 → 无接缝）
        Blit(new Rect2(srcEdge, 0, C, srcEdge), new Rect2(x1, y, cw, e));
        Blit(new Rect2(srcEdge, srcSize - srcEdge, C, srcEdge), new Rect2(x1, y + H - e, cw, e));
        Blit(new Rect2(0, srcEdge, srcEdge, C), new Rect2(x, y1, e, ch));
        Blit(new Rect2(srcSize - srcEdge, srcEdge, srcEdge, C), new Rect2(x + W - e, y1, e, ch));
        // 中心：整块一次拉伸，与四边共用同一张连续图的边界像素
        Blit(new Rect2(srcEdge, srcEdge, C, C), new Rect2(x1, y1, cw, ch));
    }

    /// <summary>
    /// v3.83 主入口：贴图化旧纸。seed 决定用哪张焦边变体，burn 决定焦边可见厚度。
    /// 贴图缺失时自动回退到 v3.82 的程序化绘制，绝不出现空白面板。
    /// </summary>
    public static void DrawSheet(CanvasItem c, Rect2 r, int seed, float burn = 0.6f)
    {
        var arr = Sheets;
        var tex = arr[(seed & 0x7fffffff) % arr.Length];
        if (tex == null) { DrawSheetLegacy(c, r, seed, burn); return; }
        // e 上限 42px：BurntPaperPanel 的 ContentMargin 为 48/48/42/46，
        // 焦边不会啃到正文（极值验算：burn=1.0 → 44 → 截断 42）。
        float edge = Mathf.Clamp(30f + burn * 14f, 24f, 42f);
        DrawNine(c, tex, r, SheetSrc, SheetEdge, edge);
    }

    /// <summary>稳定哈希（FNV-1a），不用 string.GetHashCode（.NET 每次进程会随机化）。</summary>
    public static int StableHash(string s)
    {
        uint h = 2166136261u;
        foreach (char ch in s ?? "")
        {
            h ^= ch;
            h *= 16777619u;
        }
        return (int)(h & 0x7fffffff);
    }

    /// <summary>三档纸色：base / shadow / highlight（不同 seed 有轻微冷暖差）。</summary>
    public static void Palette(int seed, out Color baseC, out Color shade, out Color lit)
    {
        var rng = NewRng(seed + 991);
        float warm = rng.RandfRange(-0.035f, 0.035f);
        // v3.80：中度烧毁旧纸——偏鞣黄/旧纸色，不再是干净象牙白
        baseC = new Color(0.84f + warm * 0.6f, 0.72f + warm * 0.4f, 0.52f - warm * 0.2f);
        shade = new Color(baseC.R * 0.78f, baseC.G * 0.76f, baseC.B * 0.72f);
        lit = new Color(Mathf.Min(1f, baseC.R * 1.10f), Mathf.Min(1f, baseC.G * 1.10f),
            Mathf.Min(1f, baseC.B * 1.12f));
    }

    private static RandomNumberGenerator NewRng(int seed)
    {
        var r = new RandomNumberGenerator();
        r.Seed = (ulong)((seed & 0x7fffffff) + 19310918u);
        return r;
    }

    private static float J(float n, int seed)
        => Mathf.Abs(Mathf.Sin(n * 12.9898f + seed * 78.233f) * 43758.5453f) % 1f;

    /// <summary>确定性伪随机（0..1），同一 seed 每次结果一致，画面不闪。</summary>
    public static float H(float n, int seed) => J(n, seed);

    // ---------- 轮廓：手绘撕裂锯齿边 + 局部被火啃掉的缺口 ----------
    /// <summary>
    /// 生成顺时针轮廓点。每条边等分采样并加确定性抖动；焦区段的点向内凹，
    /// 形成「被烧掉一块」的缺口，而不是整齐的矩形。
    /// </summary>
    private static List<Vector2> BuildOutline(Rect2 r, int seed, out int burnEdge,
        out int burnStart, out int burnLen)
    {
        var rng = NewRng(seed);
        float x0 = r.Position.X, y0 = r.Position.Y;
        float W = r.Size.X, H = r.Size.Y;

        const int NX = 22, NY = 12;           // 顶/底 与 左/右 的采样段数
        int total = NX * 2 + NY * 2;
        burnEdge = rng.RandiRange(0, 3);      // 0=顶 1=右 2=底 3=左
        burnLen = rng.RandiRange(4, 8);
        burnStart = rng.RandiRange(1, Mathf.Max(1, (burnEdge % 2 == 0 ? NX : NY) - burnLen - 1));

        var pts = new List<Vector2>(total);
        // 四角轻微不等，纸不是印刷裁切的直角
        var corners = new[]
        {
            new Vector2(x0 + rng.RandfRange(0, 5), y0 + rng.RandfRange(0, 4)),                 // 左上
            new Vector2(x0 + W - rng.RandfRange(0, 5), y0 + rng.RandfRange(0, 4)),            // 右上
            new Vector2(x0 + W - rng.RandfRange(0, 5), y0 + H - rng.RandfRange(0, 4)),        // 右下
            new Vector2(x0 + rng.RandfRange(0, 5), y0 + H - rng.RandfRange(0, 4)),            // 左下
        };

        for (int e = 0; e < 4; e++)
        {
            int n = (e % 2 == 0) ? NX : NY;
            Vector2 a = corners[e], b = corners[(e + 1) % 4];
            Vector2 inward = e switch
            {
                0 => new Vector2(0, 1),     // 顶边向内 = 下
                1 => new Vector2(-1, 0),   // 右边向内 = 左
                2 => new Vector2(0, -1),   // 底边向内 = 上
                _ => new Vector2(1, 0),    // 左边向内 = 右
            };
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                Vector2 p = a.Lerp(b, t);
                // 手绘撕边：沿法线方向小幅抖动
                float amp = 1.6f + J(seed + e * 100f + i * 3.7f, seed) * 2.6f;
                Vector2 outward = -inward;
                p += outward * (amp * (J(seed + i * 5.1f, seed + e) - 0.35f));

                // 焦区段：往内啃出缺口，缺口边缘呈锯齿状
                bool inBurn = (e == burnEdge) && i >= burnStart && i < burnStart + burnLen;
                if (inBurn)
                {
                    float k = (float)(i - burnStart) / burnLen;
                    float bell = Mathf.Sin(k * Mathf.Pi);              // 中间啃得深，两头浅
                    // v3.68「少许烧毁」：缺口只啃掉一小块，纸仍是完整可读的一页
                    float depth = (5f + bell * 17f) * (0.72f + J(seed + i * 2.3f, seed) * 0.5f);
                    p += inward * depth;
                }
                pts.Add(p);
            }
        }
        return pts;
    }

    /// <summary>把轮廓点转成 Godot 需要的数组（去掉几乎重合点，避免 triangulation failed）。</summary>
    private static Vector2[] Clean(List<Vector2> pts)
    {
        var list = new List<Vector2>(pts.Count);
        foreach (var p in pts)
            if (list.Count == 0 || list[^1].DistanceTo(p) > 0.4f) list.Add(p);
        if (list.Count > 2 && list[0].DistanceTo(list[^1]) <= 0.4f) list.RemoveAt(list.Count - 1);
        return list.ToArray();
    }

    /// <summary>
    /// v3.82 及以前的「三档平涂 + 圆形烧穿孔」画法 —— 已被用户否定的旧理解。
    /// 仅在 v3.83 贴图资产加载失败时作为兜底调用，正常路径不会执行。
    /// 保留它是为了让整套改动一行可回退，不是继续沿用这套观感。
    /// </summary>
    private static void DrawSheetLegacy(CanvasItem c, Rect2 r, int seed, float burn = 0.6f)
    {
        if (r.Size.X < 60f || r.Size.Y < 60f) return;
        var rng = NewRng(seed);
        Palette(seed, out var paper, out var shade, out var lit);

        var pts = BuildOutline(r, seed, out int burnEdge, out int burnStart, out int burnLen);
        var poly = Clean(pts);
        if (poly.Length < 3) return;

        float W = r.Size.X, H = r.Size.Y;
        float x = r.Position.X, y = r.Position.Y;

        // 硬边接触阴影：整张纸往右下错开一块压扁的深色多边形（不羽化）
        var shadow = new Vector2[poly.Length];
        for (int i = 0; i < poly.Length; i++) shadow[i] = poly[i] + new Vector2(9f, 11f);
        c.DrawColoredPolygon(shadow, new Color(0.08f, 0.06f, 0.05f, 0.42f));

        // 纸主体：一整块 base 平涂
        c.DrawColoredPolygon(poly, paper);

        // 三档平涂：下缘硬边暗部带、上缘硬边高光带（用矩形裁在纸内，不越出轮廓太多）
        c.DrawRect(new Rect2(x + 12f, y + H - 34f, W - 24f, 22f), shade);
        c.DrawRect(new Rect2(x + 12f, y + 8f, W - 24f, 10f), lit);
        c.DrawRect(new Rect2(x + 8f, y + 12f, 9f, H - 24f), shade);

        // 焦边：沿焦区轮廓内侧画一条硬边焦褐带 + 深墨棕碳化块
        DrawBurnBand(c, pts, burnEdge, burnStart, burnLen, seed, burn);

        // 烧穿的小洞：1–2 个，位置随 seed。v3.68 收尾：全部锁在上下留白带，
        // 绝不落在正文上——纸上是真实烈士档案，烧焦不能把名字/数字啃掉。
        // v3.80：中度烧毁——3~5 个烧穿洞（仍锁在上下留白带，不啃正文）
        int holes = rng.RandiRange(3, 5);
        for (int i = 0; i < holes; i++)
        {
            float hx = x + rng.RandfRange(30f, W - 30f);
            bool upper = (i % 2 == 0);
            float hy = upper
                ? y + rng.RandfRange(12f, 28f)
                : y + H - rng.RandfRange(12f, 28f);
            float rad = rng.RandfRange(4.5f, 8.5f);
            DrawBurnHole(c, new Vector2(hx, hy), rad, seed + i * 37);
        }

        // 翻卷的角：随机一个角，亮面三角 + 3px 粗墨线
        DrawFoldedCorner(c, r, rng.RandiRange(0, 3), seed, paper, lit, shade);

        // 折痕：1–2 条硬边墨线（不是渐变）。同样压在上下留白带，
        // 避免一条横线穿过阵亡通报正文、被读成 UI 分隔线。
        int folds = rng.RandiRange(1, 2);
        for (int i = 0; i < folds; i++)
        {
            bool upper = (i % 2 == 0);
            float fy = upper
                ? y + H * rng.RandfRange(0.08f, 0.13f)
                : y + H * rng.RandfRange(0.88f, 0.93f);
            c.DrawLine(new Vector2(x + 18f, fy), new Vector2(x + W - 18f, fy + rng.RandfRange(-2f, 2f)),
                new Color(InkSoft.R, InkSoft.G, InkSoft.B, 0.30f), 1.4f, true);
        }

        // 整张纸 3px 深墨棕粗描边（焦区加粗到 4.5px）
        c.DrawPolyline(poly, Ink, 3f, true);

        // 卡通高光：左上受光角两条短直线 + 一个小圆点，硬边
        c.DrawLine(new Vector2(x + 26f, y + 22f), new Vector2(x + 74f, y + 22f), lit, 2.4f, true);
        c.DrawLine(new Vector2(x + 26f, y + 28f), new Vector2(x + 52f, y + 28f), lit, 2f, true);
        c.DrawCircle(new Vector2(x + 30f, y + 40f), 2.6f, lit);

        // 灰烬小点：散在焦边附近，硬边实色
        for (int i = 0; i < 7; i++)
        {
            float ax = x + rng.RandfRange(10f, W - 10f);
            float ay = y + rng.RandfRange(H - 34f, H - 8f);
            c.DrawCircle(new Vector2(ax, ay), rng.RandfRange(1.2f, 2.6f),
                new Color(0.30f, 0.26f, 0.22f, 0.8f));
        }

        // 做旧焦斑：散落的细小碳化点，集中在上下留白带，强化「旧纸」观感又不压正文
        for (int i = 0; i < 6; i++)
        {
            float cx = x + rng.RandfRange(34f, W - 34f);
            float cy = y + (i % 2 == 0
                ? rng.RandfRange(14f, 30f)
                : rng.RandfRange(H - 30f, H - 14f));
            c.DrawCircle(new Vector2(cx, cy), rng.RandfRange(0.8f, 1.9f),
                new Color(0.20f, 0.15f, 0.11f, 0.5f));
        }
    }

    private static void DrawBurnBand(CanvasItem c, List<Vector2> pts, int edge, int start, int len,
        int seed, float burn)
    {
        int n = (edge % 2 == 0) ? 22 : 12;
        int baseIdx = 0;
        for (int e = 0; e < edge; e++) baseIdx += (e % 2 == 0) ? 22 : 12;

        Vector2 inward = edge switch
        {
            0 => new Vector2(0, 1),
            1 => new Vector2(-1, 0),
            2 => new Vector2(0, -1),
            _ => new Vector2(1, 0),
        };

        // 逐段四边形画焦褐带：避免整条凹多边形触发 Godot 三角化失败 / 白线
        for (int i = start - 1; i < start + len; i++)
        {
            int i0 = (baseIdx + i + pts.Count) % pts.Count;
            int i1 = (baseIdx + i + 1 + pts.Count) % pts.Count;
            float d0 = 8f + 9f * burn + J(seed + i, seed) * 6f;
            float d1 = 8f + 9f * burn + J(seed + i + 1, seed) * 6f;
            var quad = new[]
            {
                pts[i0], pts[i1],
                pts[i1] + inward * d1, pts[i0] + inward * d0,
            };
            c.DrawColoredPolygon(quad, new Color(0.46f, 0.27f, 0.13f, 0.96f));
            c.DrawLine(quad[0], quad[1], new Color(0.36f, 0.22f, 0.12f), 2f, true);
        }

        // 碳化块：沿焦区外沿几块深墨棕，做出「烧过火」的层次
        for (int i = start; i < start + len; i += 2)
        {
            int idx = (baseIdx + i + pts.Count) % pts.Count;
            Vector2 p = pts[idx];
            float w = 5f + J(seed + i * 3.1f, seed + 5) * 6f;
            var chunk = new[]
            {
                p,
                p + new Vector2(w, 2f),
                p + new Vector2(w * 0.7f, 2f + inward.Y * 5f),
                p + new Vector2(w * 0.2f, inward.Y * 4f + 2f),
            };
            c.DrawColoredPolygon(chunk, Ink);
        }
    }

    private static void DrawBurnHole(CanvasItem c, Vector2 p, float rad, int seed)
    {
        var outer = new List<Vector2>();
        var inner = new List<Vector2>();
        for (int i = 0; i < 9; i++)
        {
            float a = i * Mathf.Tau / 9f;
            float j = 0.75f + J(seed + i * 2.7f, seed) * 0.5f;
            outer.Add(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad * j * 1.55f);
            inner.Add(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad * j);
        }
        // 焦黄硬边圈
        c.DrawColoredPolygon(Clean(outer), new Color(0.48f, 0.30f, 0.14f));
        // 烧穿的洞：露出底下暗色（不透明，硬边）
        c.DrawColoredPolygon(Clean(inner), new Color(0.13f, 0.10f, 0.08f));
        c.DrawPolyline(Clean(inner), Ink, 2f, true);
    }

    private static void DrawFoldedCorner(CanvasItem c, Rect2 r, int corner, int seed,
        Color paper, Color lit, Color shade)
    {
        float x = r.Position.X, y = r.Position.Y, W = r.Size.X, H = r.Size.Y;
        float s = 26f + J(seed, seed + 7) * 12f;
        Vector2 a, b, d, tip;
        switch (corner)
        {
            case 0: // 左上角
                a = new Vector2(x + s, y + 6); b = new Vector2(x + 6, y + s);
                tip = new Vector2(x + 6, y + 6); d = new Vector2(-1, 1); break;
            case 1: // 右上角
                a = new Vector2(x + W - s, y + 6); b = new Vector2(x + W - 6, y + s);
                tip = new Vector2(x + W - 6, y + 6); d = new Vector2(1, 1); break;
            case 2: // 右下角
                a = new Vector2(x + W - s, y + H - 6); b = new Vector2(x + W - 6, y + H - s);
                tip = new Vector2(x + W - 6, y + H - 6); d = new Vector2(1, -1); break;
            default: // 左下角
                a = new Vector2(x + s, y + H - 6); b = new Vector2(x + 6, y + H - s);
                tip = new Vector2(x + 6, y + H - 6); d = new Vector2(-1, -1); break;
        }
        // 卷起后露出的纸背（亮一档）+ 三档中的暗部三角
        var tri = new[] { a, b, tip };
        c.DrawColoredPolygon(tri, lit);
        c.DrawPolyline(tri, Ink, 3f, true);
        c.DrawLine(a, b, InkSoft, 2f, true);
        // 卷边投在纸上的硬边小阴影
        Vector2 q = a.Lerp(b, 0.5f) - d * 9f;
        var sh = new[] { a, b, q };
        c.DrawColoredPolygon(sh, shade);
        c.DrawLine(a, b, Ink, 3f, true);
    }
}

/// <summary>
/// 一张「真实纸张」容器：PanelContainer 只负责撑尺寸，可见外观由 _Draw 用贴图绘制。
/// 子节点（文字/按钮）叠在纸之上，48/48/42/46 的内边距留给焦边，保证文字不被烧痕啃到。
/// </summary>
public partial class BurntPaperPanel : PanelContainer
{
    public int Seed { get; private set; }
    public float Burn { get; }

    public BurntPaperPanel(int seed, float burn = 0.6f)
    {
        Seed = seed;
        Burn = burn;
        // 内边距给焦边与撕口留出安全区，文字不会被烧痕啃到
        var sb = new StyleBoxEmpty();
        sb.ContentMarginLeft = 48f;
        sb.ContentMarginRight = 48f;
        sb.ContentMarginTop = 42f;
        sb.ContentMarginBottom = 46f;
        AddThemeStyleboxOverride("panel", sb);
        MouseFilter = Control.MouseFilterEnum.Stop;
        // 每张纸摆放角度略有不同（±0.3°），像随手压在桌上的一张真纸
        Rotation = ((seed % 7) - 3) * 0.0018f;
    }

    /// <summary>换一张纸（同一浮层连续显示多段内容时，让每张长得不一样）。</summary>
    public void SetSeed(int seed)
    {
        Seed = seed;
        Rotation = ((seed % 7) - 3) * 0.0018f;
        QueueRedraw();
    }

    public override void _Draw()
    {
        BurntPaperArt.DrawSheet(this, new Rect2(Vector2.Zero, Size), Seed, Burn);
    }
}

/// <summary>
/// v3.83 纸上的钢笔分隔线：细（1.1~1.7px）、不均、随形体断续，
/// 不再是 v3.68 那条 3px 均匀粗墨线 + 焦褐端点圆点（属被否决的漫画风定调）。
/// </summary>
public partial class InkRule : Control
{
    public override void _Draw()
    {
        if (Size.X < 20f) return;
        float y = Size.Y * 0.5f;
        int n = Mathf.Max(10, (int)(Size.X / 26f));
        var col = new Color(0.29f, 0.22f, 0.16f, 0.62f);
        bool hasPrev = false;
        Vector2 prev = Vector2.Zero;
        for (int i = 0; i <= n; i++)
        {
            float t = (float)i / n;
            var p = new Vector2(Size.X * t, y + (H(i * 2.7f, 17) - 0.5f) * 2.2f);
            // 约 1/8 的段故意断笔，线随形体断续而不是一根印刷直线
            if (hasPrev && H(i * 1.7f, 31) > 0.12f)
                DrawLine(prev, p, col, 1.1f + H(i * 3.3f, 7) * 0.6f, true);
            prev = p;
            hasPrev = true;
        }
    }

    private static float H(float n, int seed) => BurntPaperArt.H(n, seed);
}

/// <summary>
/// v3.83 纸签按钮：收敛成 1px 细墨线 + 淡墨软投影，
/// 与旧纸贴图同调；保留按钮的可点击识别度，不再是 3px 均匀粗框的漫画风纸牌。
/// </summary>
public static class PaperButtonStyle
{
    public static void Apply(Button b)
    {
        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.91f, 0.84f, 0.70f, 0.96f),
            BorderColor = new Color(0.42f, 0.32f, 0.22f, 0.85f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 16, ContentMarginRight = 16,
            ContentMarginTop = 6, ContentMarginBottom = 6,
            ShadowColor = new Color(0.12f, 0.09f, 0.06f, 0.28f),
            ShadowSize = 2, ShadowOffset = new Vector2(2, 2),
        };
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = new Color(0.97f, 0.92f, 0.79f, 0.98f);
        var pressed = (StyleBoxFlat)normal.Duplicate();
        pressed.BgColor = new Color(0.82f, 0.74f, 0.58f, 0.98f);
        pressed.ShadowOffset = new Vector2(1, 1);
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", pressed);
        b.AddThemeColorOverride("font_color", InkStyle.InkColor);
        b.AddThemeColorOverride("font_hover_color", InkStyle.InkColor);
        b.AddThemeColorOverride("font_pressed_color", InkStyle.InkColor);
    }
}

/// <summary>
/// v3.83 屋内小提示牌用的「矮纸条」：与大纸同一套旧纸质感
/// （米黄揉皱纸纹 + 上下细窄不规则焦边），只是九宫格边距更薄。
/// 高度只有 30~56px，运行时走 paper_slip.png 贴图；贴图缺失时回退旧画法。
/// </summary>
public static class PaperSlip
{
    private static Texture2D _slipTex;

    public static void Draw(CanvasItem c, Rect2 r, int seed)
    {
        if (r.Size.X < 40f || r.Size.Y < 12f) return;
        _slipTex ??= GD.Load<Texture2D>("res://assets/art/ui/paper_slip.png");
        if (_slipTex != null)
        {
            // 极值验算：H=32 → e=min(9, 7.68, W*0.12)=7.68px；
            // H=56 → e=min(9, 13.44, …)=9px。焦边不会吃掉纸条上的文字行。
            float e = Mathf.Min(9f, Mathf.Min(r.Size.Y * 0.24f, r.Size.X * 0.12f));
            BurntPaperArt.DrawNine(c, _slipTex, r,
                BurntPaperArt.SlipSrcSize, BurntPaperArt.SlipEdgeSize, e);
            return;
        }
        BurntPaperArt.Palette(seed, out var paper, out var shade, out var lit);
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)((seed & 0x7fffffff) + 19311104u);

        float x = r.Position.X, y = r.Position.Y, W = r.Size.X, H = r.Size.Y;
        // 上缘手绘撕边（逐段折线，不渐变）
        var top = new List<Vector2>();
        var bot = new List<Vector2>();
        for (int i = 0; i <= 10; i++)
        {
            float t = (float)i / 10f;
            float jt = (BurntPaperArt.H(seed + i * 3.3f, seed) - 0.5f) * 3f;
            float jb = (BurntPaperArt.H(seed + i * 5.7f, seed + 9) - 0.5f) * 2.4f;
            top.Add(new Vector2(x + W * t, y + jt));
            bot.Add(new Vector2(x + W * t, y + H + jb));
        }
        var poly = new List<Vector2>(top);
        for (int i = bot.Count - 1; i >= 0; i--) poly.Add(bot[i]);

        // 硬边投影
        var sh = new List<Vector2>();
        foreach (var p in poly) sh.Add(p + new Vector2(4f, 5f));
        c.DrawColoredPolygon(sh.ToArray(), new Color(0.08f, 0.06f, 0.05f, 0.45f));
        c.DrawColoredPolygon(poly.ToArray(), paper);
        c.DrawRect(new Rect2(x + 4f, y + H - 7f, W - 8f, 5f), shade);
        c.DrawRect(new Rect2(x + 4f, y + 3f, W - 8f, 3f), lit);
        c.DrawPolyline(poly.ToArray(), InkStyle.InkColor, 3f, true);

        // 少许烧毁：随机一角的焦褐缺口 + 焦边
        int corner = rng.RandiRange(0, 1);
        float bx = corner == 0 ? x + 6f : x + W - 26f;
        var burn = new[]
        {
            new Vector2(bx, y + 1f),
            new Vector2(bx + 20f, y + 1f),
            new Vector2(bx + 15f, y + H * 0.55f),
            new Vector2(bx + 4f, y + H * 0.38f),
        };
        c.DrawColoredPolygon(burn, new Color(0.50f, 0.31f, 0.15f));
        c.DrawPolyline(burn, InkStyle.InkColor, 2f, true);
    }
}

