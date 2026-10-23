using Godot;

namespace Witness;

/// <summary>
/// 手绘上色漫画风（Hand-drawn cel-shading manga）全局风格常量与辅助。
///
/// 设计规则：
///   • 所有轮廓统一用 3px 深墨棕描边（<see cref="Stroke"/> / <see cref="BoldStroke"/>）。
///   • 每块物体只有 **base → shadow → highlight** 三档平涂色板；**不做插值渐变**。
///     这样画出来是"一块块的"平涂感，像漫画上色。
///   • 卡通高光用一个小圆点 / 一条细线（<see cref="KawaiiHighlight"/>），不要模糊光晕。
///   • 接触阴影画成压扁的椭圆，颜色是底图颜色加一层冷色半透明叠，不要柔和羽化。
///
/// 这是整版手绘漫画风的唯一入口。所有 Poly/DrawRect/DrawLine 都应该从这里取色
/// （或调用 Ink() 来先描边再平涂），保证整画风格统一。
/// </summary>
public static class InkStyle
{
    // —— 轮廓线 ——
    /// <summary>墨色描边（深棕近黑，模拟手绘勾线笔）。</summary>
    public static readonly Color InkColor = new(0.14f, 0.10f, 0.08f);
    /// <summary>次一级描边（浅色物体的描边稍浅，避免"贴"在黑纸上）。</summary>
    public static readonly Color InkSoft = new(0.28f, 0.22f, 0.18f);
    /// <summary>粗描边宽度（主轮廓）。</summary>
    public const float BoldStroke = 3.0f;
    /// <summary>次描边宽度（内部缝线/折痕）。</summary>
    public const float Stroke = 1.6f;
    /// <summary>最细描边（细节）。</summary>
    public const float ThinStroke = 0.9f;

    // —— 通用接触阴影（冷色半透明叠，平涂）——
    public static readonly Color GroundShadow = new(0.10f, 0.12f, 0.14f, 0.38f);

    // —— 标准色板（base → shadow → highlight）——
    // 肤色
    public static readonly Color Skin = new(0.96f, 0.82f, 0.72f);
    public static readonly Color SkinShadow = new(0.80f, 0.64f, 0.54f);
    public static readonly Color SkinHighlight = new(1.00f, 0.92f, 0.86f);

    // 东北军棉军装（灰蓝）
    public static readonly Color NACoat = new(0.52f, 0.58f, 0.64f);
    public static readonly Color NACoatShadow = new(0.36f, 0.40f, 0.46f);
    public static readonly Color NACoatHighlight = new(0.66f, 0.72f, 0.78f);

    // 关东军卡其军装
    public static readonly Color JPCoat = new(0.76f, 0.70f, 0.44f);
    public static readonly Color JPCoatShadow = new(0.54f, 0.48f, 0.26f);
    public static readonly Color JPCoatHighlight = new(0.88f, 0.82f, 0.58f);

    // 日军钢盔
    public static readonly Color JHelmet = new(0.38f, 0.36f, 0.28f);
    public static readonly Color JHelmetShadow = new(0.22f, 0.20f, 0.14f);
    public static readonly Color JHelmetHighlight = new(0.54f, 0.52f, 0.42f);

    // 东北军布棉帽
    public static readonly Color NACap = new(0.46f, 0.52f, 0.58f);
    public static readonly Color NACapShadow = new(0.30f, 0.34f, 0.38f);

    // 平民头巾/棉袍
    public static readonly Color CivilianCoat = new(0.82f, 0.74f, 0.58f);
    public static readonly Color CivilianCoatShadow = new(0.60f, 0.52f, 0.38f);

    // 雪
    public static readonly Color Snow = new(0.96f, 0.97f, 0.98f);
    public static readonly Color SnowShadow = new(0.74f, 0.78f, 0.82f);

    // 木箱 / 沙袋
    public static readonly Color Wood = new(0.62f, 0.44f, 0.24f);
    public static readonly Color WoodShadow = new(0.40f, 0.26f, 0.14f);
    public static readonly Color WoodHighlight = new(0.78f, 0.60f, 0.34f);
    public static readonly Color Sand = new(0.72f, 0.62f, 0.44f);
    public static readonly Color SandShadow = new(0.48f, 0.40f, 0.26f);
    // 真实沙包颜色（比Sand偏暗偏褐，有帆布质感）
    public static readonly Color SackBase = new(0.68f, 0.58f, 0.38f);
    public static readonly Color SackVariation = new(0.76f, 0.64f, 0.42f);
    public static readonly Color SackShadow = new(0.38f, 0.30f, 0.18f);

    // 东北民居（1931嫩江土坯房）
    public static readonly Color AdobeWall = new(0.72f, 0.66f, 0.56f);       // 夯土墙（黄褐色，有土的质感）
    public static readonly Color AdobeWallShadow = new(0.52f, 0.46f, 0.36f);  // 土墙阴影面
    public static readonly Color PlankRoof = new(0.46f, 0.36f, 0.24f);        // 木板瓦屋顶（深棕）
    public static readonly Color PlankRoofShadow = new(0.32f, 0.24f, 0.16f);  // 屋顶暗面
    public static readonly Color WindowGlow = new(0.88f, 0.70f, 0.40f);       // 窗户暖光
    public static readonly Color Chimney = new(0.42f, 0.38f, 0.34f);          // 砖烟囱

    // 星空夜
    public static readonly Color Night = new(0.12f, 0.14f, 0.22f);

    // —— 辅助方法 ——

    /// <summary>
    /// 手绘漫画式画多边形：先粗描边再平涂色，保证不管多近看都是漫画感。
    /// 用法：InkStyle.PenStroke(canvas, polygon, fill, outline)。
    /// </summary>
    public static void PenStroke(CanvasItem canvas, Vector2[] poly, Color fill, Color outline, float bw = BoldStroke)
    {
        if (poly.Length < 3) return;
        canvas.DrawColoredPolygon(poly, fill);
        canvas.DrawPolyline(poly, outline, bw, true);
    }

    /// <summary>先描边再填色的矩形（木箱/沙袋/砖块都用这个）。</summary>
    public static void InkRect(CanvasItem canvas, Rect2 r, Color fill, Color outline, float bw = BoldStroke)
    {
        canvas.DrawRect(r, fill);
        canvas.DrawRect(r, outline, false, bw);
    }

    /// <summary>卡通高光：一个小圆点，画在物体的左上受光角（<paramref name="litPoint"/>）。</summary>
    public static void KawaiiHighlight(CanvasItem canvas, Vector2 litPoint, float size, Color color)
    {
        canvas.DrawCircle(litPoint, size, color);
    }

    /// <summary>卡通高光：一条短直线。</summary>
    public static void KawaiiLineHighlight(CanvasItem canvas, Vector2 from, Vector2 to, Color color, float width = 2f)
    {
        canvas.DrawLine(from, to, color, width, true);
    }

    /// <summary>
    /// 把一个颜色调一档做阴影：比 Darkened 温和，平涂漫画的阴影不会死黑。
    /// </summary>
    public static Color Shade(Color baseC, float k = 0.62f) => new(baseC.R * k, baseC.G * k, baseC.B * k);

    /// <summary>调一档做高光：亮一档但不发白。</summary>
    public static Color Lighten(Color baseC, float k = 1.18f) => new(
        Mathf.Min(1f, baseC.R * k),
        Mathf.Min(1f, baseC.G * k),
        Mathf.Min(1f, baseC.B * k));
}
