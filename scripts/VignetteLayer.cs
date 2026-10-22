using Godot;

namespace Witness;

/// <summary>
/// 《勇敢的心》式画框暗角：全屏径向渐变 + 四边柔和暗带，
/// 像一页手绘漫画的边框，把视线收进画面中央。纯视觉层，不拦截输入。
/// </summary>
public partial class VignetteLayer : CanvasLayer
{
    public VignetteLayer() { Layer = 4; }

    public override void _Ready() => AddChild(new VignetteControl());

    private partial class VignetteControl : Control
    {
        private static readonly Color Ink = new(0.10f, 0.09f, 0.08f);
        private const float Edge = 64f;

        public override void _Ready()
        {
            SetAnchorsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Notification(int what)
        {
            if (what == NotificationResized) QueueRedraw();
        }

        private static GradientTexture2D Band(Vector2 from, Vector2 to, int w, int h, float a)
        {
            var g = new Gradient();
            g.SetColor(0, new Color(Ink, a));
            g.SetColor(1, new Color(Ink, 0f));
            return new GradientTexture2D
            {
                Gradient = g,
                Width = w,
                Height = h,
                FillFrom = from,
                FillTo = to,
            };
        }

        public override void _Draw()
        {
            var s = GetViewportRect().Size;
            if (s.X < 8f || s.Y < 8f) return;

            // 全屏径向暗角（四角压暗）
            var radial = new Gradient();
            radial.SetColor(0, new Color(Ink, 0f));
            radial.SetColor(1, new Color(Ink, 0.38f));
            var rtex = new GradientTexture2D
            {
                Gradient = radial,
                Width = 512,
                Height = 288,
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f),
                FillTo = new Vector2(1f, 0.5f),
            };
            DrawTextureRect(rtex, new Rect2(Vector2.Zero, s), false);

            // 四边柔和暗带（画框感）
            int ex = (int)Edge, ey = (int)Edge;
            DrawTextureRect(Band(new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), 256, ey, 0.50f),
                new Rect2(0, 0, s.X, ey), false);                                            // 上
            DrawTextureRect(Band(new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), 256, ey, 0.55f),
                new Rect2(0, s.Y - ey, s.X, ey), false);                                     // 下
            DrawTextureRect(Band(new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), ex, 256, 0.45f),
                new Rect2(0, ey, ex, s.Y - ey * 2), false);                                  // 左
            DrawTextureRect(Band(new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), ex, 256, 0.45f),
                new Rect2(s.X - ex, ey, ex, s.Y - ey * 2), false);                           // 右
        }
    }
}
