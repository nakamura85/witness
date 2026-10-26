using Godot;
using System.Collections.Generic;

namespace Witness;

/// <summary>
/// 纸娃娃腿部分件（v2.6）。
///
/// 老做法是把下半身沿贴图中线竖着切两半，结果有两个硬伤：
///   1) 前腿的大腿横跨中线，被切成两半，一走起来就是"大腿跟小腿分离"；
///   2) 斜挎在手上的步枪正好插进腿部区域，被复制进两条腿片里，
///      腿一摆动，枪的复制件就从手里飞出去 —— 就是玩家老看到的
///      "日本兵的手和枪那边有残影"。
///
/// 这里换三步走：
///   1) 掩膜：只保留"能踩到地面"的像素。手里握着的步枪永远踩不到地面，
///      所以任何挂在胯上的东西都不可能混进腿片；
///   2) 裆缝：两腿从真正的腿缝切开，不是贴图中线；
///   3) 身体挖洞：身体贴图把腿挖掉，避免同一块像素被画两遍。
/// </summary>
public static class FigureParts
{
    /// <summary>切缝用的判定：这一行的空白够宽才算是真腿缝。</summary>
    private const int MinGapPx = 8;

    /// <summary>腿缝至少要连续这么多行才算真的是两条腿（长袍/大褂只有脚尖露出来不算）。</summary>
    private const int MinGapDepth = 12;

    /// <summary>髋部桥接片高度（贴图像素），用来盖住腿摆动时髋部张开的缝。</summary>
    private const int BridgeBand = 22;

    /// <summary>
    /// How many extra pixels the leg strip carries upward (texture space).
    ///
    /// v2.7: cutRow is a hard horizontal edge and the body hem, the leg top
    /// and the bridge all line up on it. Three layers stacked on one line
    /// leave a thin bright seam at the waist - that white line.
    /// Let the leg carry a band above the cut and let the bridge cover that
    /// band, so the hard edge ends up buried under the cloth.
    /// </summary>
    private const int LegTopOverlap = 10;

    /// <summary>描边外扩几像素，让腿片保留原画的墨线轮廓。</summary>
    private const int DilatePx = 4;

    public class Parts
    {
        /// <summary>身体（挖掉腿之后），整张贴图，尺寸跟原画加描边后一致。</summary>
        public Texture2D Body;
        /// <summary>后腿（只含属于腿的像素）。</summary>
        public Texture2D LegBack;
        /// <summary>前腿。</summary>
        public Texture2D LegFront;
        /// <summary>髋部桥接片，盖住腿片转动时在胯上开的缝。</summary>
        public Texture2D Bridge;
        /// <summary>身体和腿的分割行（贴图坐标，含描边留白）。</summary>
        public int CutRow;
        /// <summary>裆部铰链点（贴图坐标，含描边留白）。</summary>
        public int PivotX;
        public int PivotY;
        /// <summary>桥接片在身体贴图上的左上角坐标。</summary>
        public int BridgeX;
        public int BridgeY;
    }

    /// <summary>
    /// 拆出一套腿部零件。返回 null 表示这套立绘不适合拆分
    /// （比如长袍把腿全遮住了），调用方走老路。
    /// </summary>
    public static Parts Build(string resPath, float cropR, float cropB)
    {
        var src = ChromaKey.CutoutImage(resPath, cropR, cropB, 640);
        if (src == null) return null;
        int W = src.GetWidth(), H = src.GetHeight();
        if (W < 16 || H < 16) return null;

        // 胯线：身体的裁切高度跟纸娃娃那边保持一致
        int cutRow = Mathf.Clamp(Mathf.RoundToInt(H * 0.58f), 8, H - 8);

        var mask = LegMask(src, W, H, cutRow);
        int maskCount = 0;
        bool anyAtCut = false;
        for (int i = 0; i < mask.Length; i++) if (mask[i]) maskCount++;
        for (int x = 0; x < W; x++) if (mask[cutRow * W + x]) anyAtCut = true;
        if (maskCount < 400 || !anyAtCut) return null;

        var split = SplitLine(mask, W, H, cutRow, out bool twoLegs);

        // 描边之后再分件，这样腿片能带上原画的墨线轮廓；
        // 掩膜往外扩一圈，扩出来的像素只可能是紧贴腿边的墨线（枪离得远，够不着）。
        var grow = Dilate(mask, W, H, DilatePx);

        int pad = ChromaKey.StrokePad;
        int pw = W + pad * 2, ph = H + pad * 2;
        var stroked = ChromaKey.AddStroke(src, ChromaKey.StrokeRadius);
        var bodyImg = stroked.Duplicate() as Image;
        var backImg = Image.CreateEmpty(pw, ph, false, Image.Format.Rgba8);
        var frontImg = Image.CreateEmpty(pw, ph, false, Image.Format.Rgba8);
        backImg.Fill(new Color(0, 0, 0, 0));
        frontImg.Fill(new Color(0, 0, 0, 0));

        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            int i = y * W + x;
            if (!grow[i]) continue;
            var c = stroked.GetPixel(x + pad, y + pad);
            // 扩出来的一圈里只有不透明的才算腿的一部分（背景是透明的，不算），
            // 这样身体挖掉的洞跟腿片覆盖的范围一模一样，中间不会漏出雪地
            bool isLeg = mask[i] || c.A > 0.12f;
            if (!isLeg) continue;
            // 身体挖掉腿（含腿的墨线），这样同一块像素不会被画两遍
            bodyImg.SetPixel(x + pad, y + pad, new Color(c.R, c.G, c.B, 0f));
            // leg strip carries LegTopOverlap pixels above the cut (v2.7 white line fix):
            // the bridge covers the hard edge, and the leg itself wraps a bit
            // higher, so neither layer shows a seam
            bool inLeg = mask[i];
            bool inOverlapBand = y >= cutRow - LegTopOverlap && y < cutRow && c.A > 0.12f;
            if (!inLeg && !inOverlapBand) continue;
            if (x < split[y]) backImg.SetPixel(x + pad, y + pad, c);
            else frontImg.SetPixel(x + pad, y + pad, c);
        }

        var p = new Parts
        {
            Body = ImageTexture.CreateFromImage(bodyImg),
            LegBack = ImageTexture.CreateFromImage(backImg),
            LegFront = ImageTexture.CreateFromImage(frontImg),
            CutRow = cutRow + pad,
            PivotX = split[cutRow] + pad,
            PivotY = cutRow + pad,
        };

        // hip bridge: the topmost slice of the legs, static, glued to the torso
        // to cover the seam where the hips open up.
        // v2.7: the bridge must use the same stroked image and the same grow
        // mask as the body and the legs. Otherwise the edges do not line up
        // and the background leaks through the seam = the white line at the waist.
        var bridge = BuildBridge(stroked, grow, W, H, cutRow, out int bx, out int by, out int bw, out int bh);
        if (bridge != null)
        {
            p.Bridge = bridge;
            p.BridgeX = bx + pad;
            p.BridgeY = by + pad;
        }
        else
        {
            p.Bridge = null;
        }
        return p;
    }

    /// <summary>
    /// 只保留能一路踩到地面的像素。手里的枪、背上的包、腰上挂的东西都踩不到地面，
    /// 所以它们留在身体贴图里，绝不会被复制到腿上去。
    /// </summary>
    private static bool[] LegMask(Image img, int W, int H, int cutRow)
    {
        var solid = new bool[W * H];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
            solid[y * W + x] = img.GetPixel(x, y).A > 0.35f;

        int groundY = H - 1;
        while (groundY >= 0 && !RowAny(solid, W, groundY)) groundY--;
        var mask = new bool[W * H];
        if (groundY < cutRow) return mask;

        var prev = Runs(solid, W, groundY);
        foreach (var (a, b) in prev) Mark(mask, W, groundY, a, b);

        // 从地面往上走，只保留跟下方腿段还搭着的连续段
        for (int y = groundY - 1; y >= cutRow; y--)
        {
            var cur = Runs(solid, W, y);
            var keep = new List<(int, int)>();
            foreach (var (a, b) in cur)
            {
                foreach (var (pa, pb) in prev)
                {
                    if (a <= pb && pa <= b) { keep.Add((a, b)); break; }
                }
            }
            foreach (var (a, b) in keep) Mark(mask, W, y, a, b);
            prev = keep;
        }
        return mask;
    }

    /// <summary>
    /// 逐行找腿缝。真的分开的行用缝的中点，粘在一起的大腿沿用下面那一行的缝位置，
    /// 这样切缝会顺着腿型走，而不是在裤裆上硬切一刀。
    /// </summary>
    private static int[] SplitLine(bool[] mask, int W, int H, int cutRow, out bool twoLegs)
    {
        var split = new int[H];
        for (int y = 0; y < H; y++) split[y] = W / 2;

        // 从掩膜的最底一行往上找腿缝
        int groundY = H - 1;
        while (groundY >= cutRow && !RowAny(mask, W, groundY)) groundY--;
        if (groundY < cutRow) { twoLegs = false; return split; }

        int last = W / 2;
        int depth = 0;
        int gapTop = groundY + 1;

        for (int y = groundY; y >= cutRow; y--)
        {
            if (!WidestGap(mask, W, y, out int gap, out int mid)) break;
            if (gap < MinGapPx) break;
            depth++;
            last = mid;
            split[y] = mid;
            gapTop = y;
        }

        twoLegs = depth >= MinGapDepth;
        // 腿缝以上的粘合区（大腿根）沿用最下面那条缝的位置，
        // 这样切缝顺着腿型走，不会在裤裆上硬切一刀
        for (int y = gapTop; y <= cutRow; y++) split[y] = last;
        return split;
    }

    /// <summary>
    /// Hip bridge: a static strip of cloth at the very top of the legs,
    /// glued to the torso to cover the seam where the hips open up.
    ///
    /// v2.7 white line fix: the old version took the **unstroked** original
    /// src here, while the body and the legs both used the stroked image.
    /// The two differ by a 3~4 px outline band, so the bridge edge never
    /// matched the body edge and the background (snow) leaked through the
    /// seam - that is the white line at the waist.
    /// Now the bridge uses the same stroked image, with the same grow range
    /// as the legs, so all three layers have identical pixel boundaries and
    /// nothing can leak through anymore.
    /// </summary>
    private static Texture2D BuildBridge(Image stroked, bool[] grow, int W, int H, int cutRow,
                                          out int bx, out int by, out int bw, out int bh)
    {
        bx = by = bw = bh = 0;
        int x0 = W, x1 = -1, y0 = H, y1 = -1;
        // v2.7: the bridge covers LegTopOverlap pixels above cutRow, so the
        // hard edge of the cut ends up buried under the bridge (white line fix)
        int top = Mathf.Max(0, cutRow - LegTopOverlap);
        int bot = Mathf.Min(H - 1, cutRow + BridgeBand);
        for (int y = top; y <= bot; y++)
        for (int x = 0; x < W; x++)
        {
            if (!grow[y * W + x]) continue;
            if (x < x0) x0 = x;
            if (x > x1) x1 = x;
            if (y < y0) y0 = y;
            if (y > y1) y1 = y;
        }
        if (x1 < 0) return null;
        bx = x0; by = y0; bw = x1 - x0 + 1; bh = y1 - y0 + 1;

        var img = Image.CreateEmpty(bw, bh, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        for (int y = 0; y < bh; y++)
        for (int x = 0; x < bw; x++)
        {
            int sx = bx + x, sy = by + y;
            if (sy < 0 || sy >= H || sx < 0 || sx >= W) continue;
            if (!grow[sy * W + sx]) continue;
            // the stroked image has StrokePad extra on all sides,
            // so the coordinates shift by that amount
            var c = stroked.GetPixel(sx + ChromaKey.StrokePad, sy + ChromaKey.StrokePad);
            if (c.A <= 0.01f) continue;
            img.SetPixel(x, y, c);
        }
        return ImageTexture.CreateFromImage(img);
    }

    // ========== 小工具 ==========

    private static bool RowAny(bool[] solid, int W, int y)
    {
        for (int x = 0; x < W; x++) if (solid[y * W + x]) return true;
        return false;
    }

    private static void Mark(bool[] mask, int W, int y, int a, int b)
    {
        for (int x = a; x <= b; x++) mask[y * W + x] = true;
    }

    /// <summary>取一行里连续为真的区间。</summary>
    private static List<(int, int)> Runs(bool[] solid, int W, int y)
    {
        var outp = new List<(int, int)>();
        int x = 0;
        while (x < W)
        {
            if (!solid[y * W + x]) { x++; continue; }
            int s = x;
            while (x < W && solid[y * W + x]) x++;
            outp.Add((s, x - 1));
        }
        return outp;
    }

    /// <summary>这一行最宽的空白缝（腿缝）有多大、在哪。</summary>
    private static bool WidestGap(bool[] mask, int W, int y, out int gap, out int mid)
    {
        gap = 0; mid = -1;
        var rs = Runs(mask, W, y);
        for (int i = 0; i < rs.Count - 1; i++)
        {
            int g = rs[i + 1].Item1 - rs[i].Item2 - 1;
            if (g > gap)
            {
                gap = g;
                mid = (rs[i].Item2 + rs[i + 1].Item1) / 2;
            }
        }
        return mid >= 0;
    }

    /// <summary>掩膜往外扩 n 像素（四邻域）。</summary>
    private static bool[] Dilate(bool[] mask, int W, int H, int n)
    {
        var outp = (bool[])mask.Clone();
        for (int step = 0; step < n; step++)
        {
            var next = (bool[])outp.Clone();
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (outp[y * W + x]) continue;
                bool near = (x > 0 && outp[y * W + x - 1])
                         || (x < W - 1 && outp[y * W + x + 1])
                         || (y > 0 && outp[(y - 1) * W + x])
                         || (y < H - 1 && outp[(y + 1) * W + x]);
                if (near) next[y * W + x] = true;
            }
            outp = next;
        }
        return outp;
    }
}
