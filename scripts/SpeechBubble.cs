using Godot;

namespace Witness;

/// <summary>
/// 漫画对白气泡（《勇敢的心》式轻量叙事）：世界空间 Node2D，
/// 纸色圆角底 + 深色描边 + 小尾巴，淡入 → 停留 → 淡出后自动释放。
/// 用 <see cref="Show"/> 挂在任意节点上（Level / Patrol / NPC 头顶）。
/// </summary>
public partial class SpeechBubble : Node2D
{
    private string _text = "";
    private float _life;                       // 停留秒数（不含淡入/淡出）
    private float _t;

    private const float FadeIn = 0.18f;
    private const float FadeOut = 0.35f;
    private const int FontSize = 14;

    private static readonly Color Paper = new(0.93f, 0.89f, 0.78f);
    private static readonly Color Ink = new(0.18f, 0.16f, 0.13f);

    /// <summary>在 parent 的世界坐标 pos 处弹出气泡（pos 为气泡底端/尾巴根部）。</summary>
    public static SpeechBubble Show(Node parent, Vector2 pos, string text, float seconds = 2.6f)
    {
        var b = new SpeechBubble
        {
            _text = text,
            _life = seconds,
            Position = pos,
            ZIndex = 60,
        };
        parent.AddChild(b);
        return b;
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_t >= FadeIn + _life + FadeOut)
        {
            QueueFree();
            return;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        float alpha = Mathf.Clamp(
            Mathf.Min(_t / FadeIn, (FadeIn + _life + FadeOut - _t) / FadeOut), 0f, 1f);
        if (alpha <= 0f) return;

        var font = ThemeDB.FallbackFont;
        var size = font.GetStringSize(_text, fontSize: FontSize);
        float w = size.X + 20f, h = size.Y + 12f;
        var rect = new Rect2(-w / 2f, -h - 8f, w, h);   // 底边留出尾巴位置
        float bottom = rect.Position.Y + rect.Size.Y;

        var paper = new Color(Paper, alpha);
        var ink = new Color(Ink, alpha);

        var sb = new StyleBoxFlat
        {
            BgColor = paper,
            BorderColor = ink,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
        };
        sb.SetBorderWidthAll(2);
        DrawStyleBox(sb, rect);

        // 小尾巴：朝下指向说话者
        var tail = new[] { new Vector2(-5, bottom - 1f), new Vector2(6, bottom - 1f), new Vector2(0, bottom + 8f) };
        DrawColoredPolygon(tail, paper);
        DrawLine(tail[0], tail[2], ink, 2f);
        DrawLine(tail[1], tail[2], ink, 2f);

        DrawString(font, new Vector2(-size.X / 2f, rect.Position.Y + h / 2f + size.Y * 0.32f),
            _text, fontSize: FontSize, modulate: ink);
    }
}
