using System.Collections.Generic;
using Godot;

namespace Witness;

/// <summary>
/// 美术工具：AI 白底素材的运行时处理 ——
/// 1) Cutout：四角 flood fill 抠掉连通的近白背景（中间被描边包围的白色衣物不受影响）；
/// 2) Tile：平铺纹理加载（不抠图）；
/// 3) 两者都支持右/底边比例裁剪（去掉生成图右下角的水印区）并缩放控内存。
/// 结果纹理静态缓存，全场只处理一次。
/// </summary>
public static class ChromaKey
{
    private static readonly Dictionary<string, Texture2D> _cache = new();

    /// <summary>加载白底立绘并抠掉背景。cropR/cropB 为右/底边裁剪比例（0~1）。</summary>
    public static Texture2D Cutout(string resPath, float cropR = 0f, float cropB = 0f, int maxSize = 640)
    {
        var key = $"cut|{resPath}|{cropR}|{cropB}|{maxSize}";
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var img = LoadCropped(resPath, cropR, cropB, maxSize);
        if (img == null) return null;
        FloodKey(img, 0.86f);
        img = Trim(img);   // 裁到内容包围盒：避免大片透明边导致绘制时被压缩变形
        var tex = ImageTexture.CreateFromImage(img);
        _cache[key] = tex;
        return tex;
    }

    /// <summary>加载平铺纹理（不抠图），裁掉水印边。</summary>
    public static Texture2D Tile(string resPath, float cropR = 0f, float cropB = 0f, int maxSize = 512)
    {
        var key = $"tile:{resPath}|{cropR}|{cropB}|{maxSize}";
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var img = LoadCropped(resPath, cropR, cropB, maxSize);
        if (img == null) return null;
        var tex = ImageTexture.CreateFromImage(img);
        _cache[key] = tex;
        return tex;
    }

    /// <summary>在 CanvasItem 的 _Draw 中按 scale 平铺绘制纹理（需要该节点 TextureRepeat=Enabled）。</summary>
    public static void DrawTiled(CanvasItem ci, Texture2D tex, Rect2 r, float scale)
    {
        ci.DrawSetTransform(r.Position, 0, new Vector2(scale, scale));
        ci.DrawTextureRect(tex, new Rect2(Vector2.Zero, r.Size / scale), true);
        ci.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

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

    /// <summary>裁到非透明内容的包围盒（留 2px 边），抠图后调用。</summary>
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
        if (x1 < 0) return img; // 全透明，原样返回
        x0 = Mathf.Max(0, x0 - 2); y0 = Mathf.Max(0, y0 - 2);
        x1 = Mathf.Min(w - 1, x1 + 2); y1 = Mathf.Min(h - 1, y1 + 2);
        if (x1 - x0 + 1 >= w && y1 - y0 + 1 >= h) return img;
        return img.GetRegion(new Rect2I(x0, y0, x1 - x0 + 1, y1 - y0 + 1));
    }

    /// <summary>从四角出发的 flood fill：把与边缘连通的近白像素置为透明。</summary>
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

        // 从四边所有近白像素出发（比只取四角更耐遮挡）
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
