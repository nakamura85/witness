using System.Collections.Generic;
using Godot;

namespace Witness;

/// <summary>
/// 美术工具：AI 白底素材的运行时处理（v0.5b 升级）——
/// 1) Cutout：四边 flood fill 抠白底 + Trim 裁内容包围盒 + **形态学膨胀粗描边**（VH 手绘风格）
/// 2) Tile：平铺纹理加载 + 裁水印边 + **纸质色调 overlay**
/// 3) 结果静态缓存，全场只处理一次。
/// </summary>
public static class ChromaKey
{
    private static readonly Dictionary<string, Texture2D> _cache = new();

    /// <summary>粗描边色：VH 手绘风格的深灰棕描线（不是纯黑）。</summary>
    private static readonly Color Stroke = new(0.19f, 0.16f, 0.12f, 1f);

    /// <summary>纸质色调：把干净 AI 生成图往旧纸黄色偏，模拟手绘纹理。</summary>
    private static readonly Color PaperTint = new(1.02f, 0.96f, 0.86f, 1f);

    private const int StrokeRadius = 3;   // 描边宽度（像素）

    // ========== 公共入口 ==========

    public static Texture2D Cutout(string resPath, float cropR = 0f, float cropB = 0f, int maxSize = 640)
    {
        var key = $"cut|{resPath}|{cropR}|{cropB}|{maxSize}";
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var img = LoadCropped(resPath, cropR, cropB, maxSize);
        if (img == null) return null;
        FloodKey(img, 0.78f);   // v0.5d：降低阈值抠更多浅色背景（建筑素材白墙多，0.86 太严只抠纯白）
        img = Trim(img);
        img = AddStroke(img, StrokeRadius);
        var tex = ImageTexture.CreateFromImage(img);
        _cache[key] = tex;
        return tex;
    }

    public static Texture2D Tile(string resPath, float cropR = 0f, float cropB = 0f, int maxSize = 512)
    {
        var key = $"tile|{resPath}|{cropR}|{cropB}|{maxSize}";
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var img = LoadCropped(resPath, cropR, cropB, maxSize);
        if (img == null) return null;
        ApplyPaperTint(img);
        var tex = ImageTexture.CreateFromImage(img);
        _cache[key] = tex;
        return tex;
    }

    /// <summary>在 CanvasItem 的 _Draw 中按 scale 平铺绘制纹理。
    /// 手动循环而非 Godot 内置 tile:true，避免非无缝 tile 的接缝亮线；
    /// 每条 tile 向右下 overlap 3px 覆盖接缝边界。</summary>
    public static void DrawTiled(CanvasItem ci, Texture2D tex, Rect2 r, float scale)
    {
        var ts = tex.GetSize();
        if (ts.X < 4 || ts.Y < 4) return;
        float tileW = ts.X * scale;
        float tileH = ts.Y * scale;
        if (tileW < 4 || tileH < 4) return;
        const float overlap = 3f;   // 每条 tile 向相邻 tile 延伸覆盖接缝
        for (float y = r.Position.Y - overlap; y < r.End.Y + overlap; y += tileH - overlap)
        {
            for (float x = r.Position.X - overlap; x < r.End.X + overlap; x += tileW - overlap)
            {
                ci.DrawTextureRect(tex, new Rect2(x, y, tileW + overlap, tileH + overlap), false);
            }
        }
    }

    // ========== 描边 & 色调 ==========

    /// <summary>
    /// 给抠好的图加一圈描边：新建稍大的 canvas，先画描边（alpha mask 膨胀 radius），
    /// 再把原图画在中心。VH 风格粗描线让 AI 素材瞬间"手绘化"。
    /// </summary>
    private static Image AddStroke(Image src, int radius)
    {
        int sw = src.GetWidth(), sh = src.GetHeight();
        if (sw < 4 || sh < 4 || radius <= 0) return src;

        int pad = radius + 1;   // 四周留白给描边
        int w = sw + pad * 2, h = sh + pad * 2;
        var canvas = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        canvas.Fill(new Color(0, 0, 0, 0));

        // 1) 形态学膨胀：标记哪些像素属于"描边区域"
        var strokeMask = new bool[w * h];
        for (int y = 0; y < sh; y++)
        {
            for (int x = 0; x < sw; x++)
            {
                float a = src.GetPixel(x, y).A;
                if (a <= 0.1f) continue;
                // 以 (x+pad, y+pad) 为中心，在半径 radius 内标记
                for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > radius * radius) continue; // 圆形半径
                    int sx = x + pad + dx, sy = y + pad + dy;
                    if (sx < 0 || sy < 0 || sx >= w || sy >= h) continue;
                    strokeMask[sy * w + sx] = true;
                }
            }
        }

        // 2) 先画描边色
        for (int i = 0; i < w * h; i++)
            if (strokeMask[i])
            {
                int x = i % w, y = i / w;
                canvas.SetPixel(x, y, Stroke);
            }

        // 3) 再把原图画在中心（覆盖描边内部，保留外描边）
        for (int y = 0; y < sh; y++)
        for (int x = 0; x < sw; x++)
        {
            var c = src.GetPixel(x, y);
            if (c.A <= 0.01f) continue;
            canvas.SetPixel(x + pad, y + pad, c);
        }

        return canvas;
    }

    /// <summary>给平铺纹理加旧纸色调：RGB 乘以 PaperTint，轻微往黄棕偏，去掉 AI 干净感。</summary>
    private static void ApplyPaperTint(Image img)
    {
        int w = img.GetWidth(), h = img.GetHeight();
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var c = img.GetPixel(x, y);
            img.SetPixel(x, y, new Color(
                Mathf.Clamp(c.R * PaperTint.R, 0, 1),
                Mathf.Clamp(c.G * PaperTint.G, 0, 1),
                Mathf.Clamp(c.B * PaperTint.B, 0, 1),
                c.A));
        }
    }

    // ========== 底层：裁剪 / Trim / FloodKey ==========

    private static Image LoadCropped(string resPath, float cropR, float cropB, int maxSize)
    {
        var img = Image.LoadFromFile(ProjectSettings.GlobalizePath(resPath));
        if (img == null) return null;
        img.Convert(Image.Format.Rgba8);
        int w = img.GetWidth(), h = img.GetHeight();
        int cw = Mathf.Max(8, w - (int)(w * cropR));
        int ch = Mathf.Max(8, h - (int)(h * cropB));
        if (cw != w || ch != h) img.Crop(cw, ch);
        int m = Mathf.Max(cw, ch);
        if (m > maxSize)
        {
            float s = (float)maxSize / m;
            img.Resize(Mathf.Max(8, (int)(cw * s)), Mathf.Max(8, (int)(ch * s)), Image.Interpolation.Lanczos);
        }
        return img;
    }

    /// <summary>裁到非透明内容的包围盒（留 4px 描边空间）。</summary>
    private static Image Trim(Image img)
    {
        int w = img.GetWidth(), h = img.GetHeight();
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            if (img.GetPixel(x, y).A <= 0.05f) continue;
            if (x < x0) x0 = x; if (x > x1) x1 = x;
            if (y < y0) y0 = y; if (y > y1) y1 = y;
        }
        if (x1 < 0) return img;
        x0 = Mathf.Max(0, x0 - 4); y0 = Mathf.Max(0, y0 - 4);
        x1 = Mathf.Min(w - 1, x1 + 4); y1 = Mathf.Min(h - 1, y1 + 4);
        if (x1 - x0 + 1 >= w && y1 - y0 + 1 >= h) return img;
        return img.GetRegion(new Rect2I(x0, y0, x1 - x0 + 1, y1 - y0 + 1));
    }

    /// <summary>从四边出发的 flood fill：把与边缘连通的近白像素置为透明。</summary>
    private static void FloodKey(Image img, float thresh)
    {
        int w = img.GetWidth(), h = img.GetHeight();
        var seen = new bool[w * h];
        var stack = new Stack<int>();

        void Push(int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            int i = y * w + x;
            if (seen[i]) return;
            var c = img.GetPixel(x, y);
            if (c.R < thresh || c.G < thresh || c.B < thresh) return;
            seen[i] = true;
            stack.Push(i);
        }

        for (int x = 0; x < w; x++) { Push(x, 0); Push(x, h - 1); }
        for (int y = 0; y < h; y++) { Push(0, y); Push(w - 1, y); }

        while (stack.Count > 0)
        {
            int i = stack.Pop();
            int x = i % w, y = i / w;
            var c = img.GetPixel(x, y);
            img.SetPixel(x, y, new Color(c.R, c.G, c.B, 0f));
            Push(x + 1, y); Push(x - 1, y); Push(x, y + 1); Push(x, y - 1);
        }
    }
}
