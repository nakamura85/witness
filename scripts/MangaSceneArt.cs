using System;
using Godot;

namespace Witness;

/// <summary>
/// v3.69 · 场景级手绘漫画风贴图的唯一入口。
///
/// 用户 2026-11-12 反馈：北大营营房是「四四方方、小孩子的画风」，
/// 铁轨「也是方方正正跟小孩子画风一样」，跟场上手绘木屋 / 雪山完全
/// 不是一支笔。根因不是色板，而是材质：`DrawMidLandmark()` 用 DrawRect
/// 画矩形墙 + 三角屋顶 + 四个等距方窗，`MangaPropArt.RailSegment` 用
/// 笔直 DrawLine + 等距小矩形枕木，都是纯几何色块。
///
/// 本文件把这两处改成与 `house_intact_near_autumn.png` 同源的手绘贴图，
/// 两关统一调用，杜绝「一处改了另一处没改」。贴图未导入时回退到带笔触
/// 的程序化画法（不规则轮廓 / 抖动墨线 / 木纹短线），**不再回退到方块画**。
///
/// 画风红线：粗深墨棕描边 + base→shadow→highlight 三档平涂 + 卡通高光
/// 短线，无渐变、无羽化、无写实细密排线。
/// </summary>
public static class MangaSceneArt
{
    private const string BarracksPath = "res://assets/art/backdrop/beidaying_barracks.png";
    private const string RailPath = "res://assets/art/props/rail_manga.png";

    /// <summary>一段铁轨贴图的绘制宽度（贴图 1536x280，等比压到 40px 高）。</summary>
    public const float RailTileW = 288f;
    /// <summary>铁轨条带在关卡里的高度，含钢轨 / 枕木 / 道砟。</summary>
    public const float RailTileH = 52f;
    /// <summary>条带顶相对关卡 railY 的偏移：钢轨落在 railY 附近。</summary>
    public const float RailTopOffset = -20f;
    /// <summary>北大营营房贴图的绘制高度（底边坐在 VillageBaseY 上）。</summary>
    public const float BarracksH = 168f;

    private static Texture2D _barracks;
    private static Texture2D _rail;
    private static bool _loaded;
    private static bool _diag;

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        if (ResourceLoader.Exists(BarracksPath))
            _barracks = GD.Load<Texture2D>(BarracksPath);
        if (ResourceLoader.Exists(RailPath))
            _rail = GD.Load<Texture2D>(RailPath);
        // 验收日志：一次运行打一行，证明贴图真的被导入并被关卡调用
        if (!_diag)
        {
            _diag = true;
            GD.Print($"[V369] barracksTex={_barracks != null} railTex={_rail != null}");
        }
    }

    public static bool BarracksReady { get { Load(); return _barracks != null; } }
    public static bool RailReady { get { Load(); return _rail != null; } }

    // ================= 北大营营盘 =================

    /// <summary>
    /// 三栋成排营房 + 营门 + 旗杆，一次贴图绘制。底边对齐 baseY，
    /// 宽度按贴图原始比例自动算，不会把建筑拉成方块。
    /// </summary>
    public static void Barracks(CanvasItem c, float centerX, float baseY, float h = BarracksH)
    {
        Load();
        if (_barracks == null)
        {
            FallbackBarracks(c, centerX, baseY, h);
            return;
        }
        var sz = _barracks.GetSize();
        float w = sz.X / sz.Y * h;
        // 硬边压扁接触阴影：漫画风的「坐在地上」，不是柔光
        c.DrawColoredPolygon(new[]
        {
            new Vector2(centerX - w * 0.48f, baseY + 1f),
            new Vector2(centerX - w * 0.30f, baseY - 4f),
            new Vector2(centerX + w * 0.34f, baseY - 4f),
            new Vector2(centerX + w * 0.50f, baseY + 1f),
        }, new Color(0.10f, 0.08f, 0.06f, 0.26f));
        c.DrawTextureRect(_barracks, new Rect2(centerX - w * 0.5f, baseY - h, w, h), false);
    }

    /// <summary>
    /// 贴图缺失时的程序化兜底：屋顶是带手绘抖动的多段折线（不是三角），
    /// 墙面是轻微梯形，窗是错落的木格窗，全部粗墨边 + 三档平涂。
    /// 绝不回到 v3.67 的「矩形 + 四个等距方窗」。
    /// </summary>
    private static void FallbackBarracks(CanvasItem c, float centerX, float baseY, float h)
    {
        var ink = InkStyle.InkColor;
        var wall = new Color(0.72f, 0.64f, 0.48f);
        var wallShade = new Color(0.52f, 0.45f, 0.34f);
        var roof = new Color(0.33f, 0.30f, 0.27f);
        var glow = new Color(0.88f, 0.70f, 0.40f);
        float w = h * 2.90f;
        float left = centerX - w * 0.5f;

        for (int i = 0; i < 3; i++)
        {
            float bw = w * (0.32f - i * 0.018f);
            float bh = h * (0.60f - i * 0.035f);
            float bx = left + i * w * 0.335f;
            float by = baseY - bh;
            float jitter = Mathf.Sin(i * 3.1f) * 2.2f;

            // 墙：上窄下宽的手绘梯形，边缘有轻微不齐
            var body = new[]
            {
                new Vector2(bx + 2f, by + 2f), new Vector2(bx + bw - 3f, by + jitter),
                new Vector2(bx + bw, baseY - 2f), new Vector2(bx - 2f, baseY),
            };
            InkStyle.PenStroke(c, body, wall, ink, InkStyle.BoldStroke);
            c.DrawColoredPolygon(new[]
            {
                new Vector2(bx + 1f, baseY - bh * 0.34f), new Vector2(bx + bw - 2f, baseY - bh * 0.38f),
                new Vector2(bx + bw - 1f, baseY - 2f), new Vector2(bx, baseY - 1f),
            }, wallShade);

            // 屋顶：五段折线的瓦面，不是完美三角形
            var ridge = new[]
            {
                new Vector2(bx - 10f, by + 1f),
                new Vector2(bx + bw * 0.16f, by - 13f + jitter),
                new Vector2(bx + bw * 0.50f, by - 25f),
                new Vector2(bx + bw * 0.83f, by - 12f - jitter),
                new Vector2(bx + bw + 10f, by + 2f),
            };
            var roofPoly = new[]
            {
                ridge[0], ridge[1], ridge[2], ridge[3], ridge[4],
                new Vector2(bx + bw - 2f, by + 2f), new Vector2(bx + 2f, by + 2f),
            };
            InkStyle.PenStroke(c, roofPoly, roof, ink, InkStyle.BoldStroke);
            // 瓦垄短线：手绘排线，不是等距矩形窗
            for (int t = 1; t < 9; t++)
            {
                float tx = bx - 4f + t * (bw + 8f) / 9f;
                c.DrawLine(new Vector2(tx, by - 6f + Mathf.Sin(t * 2.3f) * 3f),
                    new Vector2(tx + 3f, by + 1f), new Color(ink.R, ink.G, ink.B, 0.34f), 1.2f, true);
            }

            // 木格窗：位置错落、大小不一，窗内两根竖棂
            for (int k = 0; k < 3; k++)
            {
                float wx = bx + 14f + k * (bw - 30f) / 3f + Mathf.Sin(k * 2.7f + i) * 3f;
                float wh = 22f + Mathf.Sin(k * 1.9f + i) * 3f;
                var wr = new Rect2(wx, by + 20f + (k % 2) * 3f, 16f, wh);
                InkStyle.InkRect(c, wr, glow, ink, 2.2f);
                c.DrawLine(new Vector2(wr.Position.X + 8f, wr.Position.Y),
                    new Vector2(wr.Position.X + 8f, wr.Position.Y + wh), ink, 1.4f, true);
            }
            InkStyle.KawaiiLineHighlight(c,
                new Vector2(bx + 6f, by + 4f), new Vector2(bx + bw * 0.35f, by + 4f),
                new Color(0.86f, 0.81f, 0.68f), 1.8f);
        }
    }

    // ================= 铁路 =================

    /// <summary>
    /// 平铺一段手绘铁轨。width 可小于 RailTileW（断口 / 弹坑处精确收口），
    /// flip 交替镜像打散重复感；deckOnly 只取「钢轨 + 枕木」上半部，
    /// 用在江桥钢板桥面上，不让道砟盖住铆钉钢板。
    /// </summary>
    public static void RailTile(CanvasItem c, float x, float railY, float width,
        bool flip = false, bool deckOnly = false)
    {
        if (width <= 1f) return;
        Load();
        if (_rail == null)
        {
            FallbackRailTile(c, x, railY, width, flip, deckOnly);
            return;
        }

        var ts = _rail.GetSize();
        float frac = Mathf.Clamp(width / RailTileW, 0.02f, 1f);
        float srcW = ts.X * frac;
        // v3.72b：桥面钢轨加高到 46px（田野段 52px），消除「大轨上桥缩成小轨」的观感；
        // 同时多取贴图上段(0.85)让钢轨+枕木比例与田野段一致，仅不画最底部道砟以露出钢板铆钉。
        float srcH = deckOnly ? ts.Y * 0.85f : ts.Y;
        float destH = deckOnly ? 46f : RailTileH;
        var src = new Rect2(flip ? ts.X - srcW : 0f, 0f, srcW, srcH);
        var dest = new Rect2(x, railY + RailTopOffset, width, destH);
        var white = new Color(1f, 1f, 1f, 1f);

        if (flip)
        {
            c.DrawSetTransform(new Vector2(x + width, 0f), 0f, new Vector2(-1f, 1f));
            c.DrawTextureRectRegion(_rail, new Rect2(0f, dest.Position.Y, width, destH), src, white, false);
            c.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }
        else
        {
            c.DrawTextureRectRegion(_rail, dest, src, white, false);
        }
    }

    /// <summary>
    /// 把 [x0,x1] 的手绘铁轨按 288px 一段平铺，交替镜像打散重复感、
    /// 相邻段重叠 6px 盖住接缝。isGap 命中的位置（柳条湖炸断口 / 弹坑 /
    /// 弹着点）整段精确收口，不会把缺口糊成一大块空白。
    /// </summary>
    public static void RailRun(CanvasItem c, float x0, float x1, float railY,
        Func<float, bool> isGap = null, bool deckOnly = false)
    {
        const float Probe = 6f;
        const float Step = RailTileW - 6f;
        float cursor = x0;
        while (cursor < x1)
        {
            if (isGap != null && isGap(cursor)) { cursor += Probe; continue; }
            float start = cursor;
            while (cursor < x1 && !(isGap != null && isGap(cursor))) cursor += Probe;
            float p = start;
            int idx = 0;
            while (p < cursor)
            {
                float w = Mathf.Min(RailTileW, cursor - p);
                RailTile(c, p, railY, w, (idx & 1) == 1, deckOnly);
                p += Step;
                idx++;
            }
        }
    }

    /// <summary>
    /// 贴图缺失时的程序化兜底：钢轨用 5 段带轻微起伏的手绘折线（不是一条
    /// 笔直 DrawLine），枕木是带倾斜的四边形 + 木纹短线，道砟是不规则
    /// 碎石多边形。仍遵守三档平涂 + 粗墨边，但不再有「小方块」观感。
    /// </summary>
    private static void FallbackRailTile(CanvasItem c, float x, float railY, float width,
        bool flip, bool deckOnly)
    {
        var ink = InkStyle.InkColor;
        float end = x + width;
        int seg = Math.Max(2, Mathf.RoundToInt(width / 46f));

        if (!deckOnly)
        {
            // 道砟带：上下边缘都带锯齿的手绘多边形
            var top = new Vector2[seg + 1];
            var bot = new Vector2[seg + 1];
            for (int i = 0; i <= seg; i++)
            {
                float px = x + i * width / seg;
                top[i] = new Vector2(px, railY + 7f + Mathf.Sin(i * 2.7f + x * 0.01f) * 1.8f);
                bot[i] = new Vector2(px, railY + 25f + Mathf.Sin(i * 3.3f + x * 0.02f) * 2.6f);
            }
            var poly = new Vector2[(seg + 1) * 2];
            for (int i = 0; i <= seg; i++) poly[i] = top[i];
            for (int i = 0; i <= seg; i++) poly[(seg + 1) + i] = bot[seg - i];
            InkStyle.PenStroke(c, poly, MangaPropArt.BallastBase, ink, 2.0f);
            for (int i = 0; i < seg; i += 2)
            {
                float px = x + (i + 0.5f) * width / seg;
                c.DrawCircle(new Vector2(px, railY + 18f + Mathf.Sin(i * 1.7f) * 3f),
                    1.6f, MangaPropArt.BallastShadow);
            }
        }

        // 枕木：轻微倾斜的四边形，两端不整齐
        int n = Math.Max(1, Mathf.RoundToInt(width / 48f));
        for (int i = 0; i < n; i++)
        {
            float px = x + (i + 0.3f) * width / n;
            float w = Mathf.Min(26f, width / n * 0.62f);
            float tilt = Mathf.Sin(i * 2.3f + x * 0.013f) * 1.8f;
            InkStyle.PenStroke(c, new[]
            {
                new Vector2(px, railY + 5f + tilt), new Vector2(px + w, railY + 4f - tilt),
                new Vector2(px + w - 1f, railY + 17f - tilt), new Vector2(px + 1f, railY + 18f + tilt),
            }, MangaPropArt.SleeperBase, ink, 2.0f);
            c.DrawLine(new Vector2(px + 3f, railY + 9f), new Vector2(px + w - 4f, railY + 9f),
                MangaPropArt.SleeperShadow, 1.2f, true);
            InkStyle.KawaiiLineHighlight(c,
                new Vector2(px + 3f, railY + 6f), new Vector2(px + w * 0.55f, railY + 6f),
                MangaPropArt.SleeperHighlight, 1.5f);
        }

        // 钢轨：分段折线，每段有极小起伏，手绘感来自不齐而不是渐变
        foreach (float ry in new[] { railY - 3f, railY + 9f })
        {
            for (int i = 0; i < seg; i++)
            {
                float x0 = x + i * width / seg;
                float x1 = x + (i + 1) * width / seg;
                float y0 = ry + Mathf.Sin(i * 1.9f + x * 0.008f) * 0.8f;
                float y1 = ry + Mathf.Sin((i + 1) * 1.9f + x * 0.008f) * 0.8f;
                c.DrawLine(new Vector2(x0, y0), new Vector2(x1, y1), ink, 5.2f, true);
                c.DrawLine(new Vector2(x0, y0), new Vector2(x1, y1), MangaPropArt.SteelBase, 2.8f, true);
            }
            InkStyle.KawaiiLineHighlight(c,
                new Vector2(x + 8f, ry - 1.4f), new Vector2(Mathf.Min(end - 6f, x + 54f), ry - 1.4f),
                MangaPropArt.SteelHighlight, 1.4f);
        }
    }
}
