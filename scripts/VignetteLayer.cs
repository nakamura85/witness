using Godot;

namespace Witness;

/// <summary>
/// 《勇敢的心》式画框暗角：全屏径向渐变 + 四边柔和暗带，
/// 像一页手绘漫画的边框，把视线收进画面中央。纯视觉层，不拦截输入。
///
/// 纹理只在尺寸变化时重建一次并缓存。以前每次 _Draw 都新建 GradientTexture2D，
/// 旧纹理随即被释放，绘制指令就抓住了一个失效纹理——整层会画成纯白，
/// 把整个战场盖住（HUD 在更高层，所以只剩 HUD 浮在白底上）。
/// </summary>
public partial class VignetteLayer : CanvasLayer
{
    public VignetteLayer() { Layer = 4; }

    public override void _Ready() => AddChild(new VignetteControl());

    private partial class VignetteControl : Control
    {
        private static readonly Color Ink = new(0.10f, 0.09f, 0.08f);
        private const float Edge = 64f;

        private GradientTexture2D _radial;
        private GradientTexture2D _top, _bottom, _left, _right;

        public override void _Ready()
        {
            SetAnchorsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Ignore;
            BuildTextures();
        }

        public override void _Notification(int what)
        {
            if (what == NotificationResized)
            {
                BuildTextures();
                QueueRedraw();
            }
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

        private void BuildTextures()
        {
            // 全屏径向暗角（四角压暗）
            var radial = new Gradient();
            radial.SetColor(0, new Color(Ink, 0f));
            radial.SetColor(1, new Color(Ink, 0.38f));
            _radial = new GradientTexture2D
            {
                Gradient = radial,
                Width = 512,
                Height = 288,
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f),
                FillTo = new Vector2(1f, 0.5f),
            };

            // 四边柔和暗带（画框感）
            int ex = (int)Edge, ey = (int)Edge;
            _top = Band(new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), 256, ey, 0.50f);
            _bottom = Band(new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), 256, ey, 0.55f);
            _left = Band(new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), ex, 256, 0.45f);
            _right = Band(new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), ex, 256, 0.45f);
        }

        public override void _Draw()
        {
            var s = GetViewportRect().Size;
            if (s.X < 8f || s.Y < 8f) return;
            if (_radial == null) BuildTextures();

            DrawTextureRect(_radial, new Rect2(Vector2.Zero, s), false);
            int ey = (int)Edge, ex = (int)Edge;
            DrawTextureRect(_top, new Rect2(0, 0, s.X, ey), false);
            DrawTextureRect(_bottom, new Rect2(0, s.Y - ey, s.X, ey), false);
            DrawTextureRect(_left, new Rect2(0, ey, ex, s.Y - ey * 2), false);
            DrawTextureRect(_right, new Rect2(s.X - ex, ey, ex, s.Y - ey * 2), false);
        }
    }
}
