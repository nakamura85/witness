using System;
using Godot;

namespace Witness;

/// <summary>
/// v0.8 标题画面：
///   • 背景 = 真实照片 assets/art/title.jpg（古城墙雪景）
///   • 程序化叠层 = 飘雪粒子 + 灯笼暖光晕 + 暗角 vignette
///   • UI 文字用白色（深色照片上更清楚）
/// </summary>
public partial class TitleScreen : Control
{
    private readonly Main _main;
    private Label _subtitle, _prompt;
    private Button _btnNew, _btnContinue, _btnGallery;
    private OptionButton _lang;
    private Label _ver;

    public TitleScreen(Main main) { _main = main; }

    public override void _Ready()
    {
        // —— 背景层：真实照片 title.jpg 全屏铺满 ——
        var tex = GD.Load<Texture2D>("res://assets/art/title.jpg");
        if (tex != null)
        {
            var bg = new TextureRect
            {
                Texture = tex,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            };
            bg.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(bg);
        }
        else
        {
            // fallback：纯米黄
            var paper = new ColorRect { Color = new Color(0.92f, 0.88f, 0.78f) };
            paper.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(paper);
        }

        // —— 程序化装饰层（飘雪 + 灯笼光晕 + 暗角）——
        var fx = new TitleFx { ZIndex = 1 };
        fx.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(fx);

        // —— UI 层（文字 + 按钮，ZIndex 最高）——
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        center.ZIndex = 2;
        AddChild(center);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 18);
        box.CustomMinimumSize = new Vector2(640, 480);
        center.AddChild(box);

        // 标题：白色 + 深色 outline（照片背景上最清楚）
        var title = new Label { Text = Loc.I.T("app_title") };
        title.AddThemeFontSizeOverride("font_size", 90);
        title.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        title.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.04f, 0.03f, 0.85f));
        title.AddThemeConstantOverride("outline_size", 6);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(title);

        _subtitle = new Label();
        _subtitle.AddThemeFontSizeOverride("font_size", 26);
        _subtitle.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        _subtitle.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.04f, 0.03f, 0.7f));
        _subtitle.AddThemeConstantOverride("outline_size", 3);
        _subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_subtitle);

        _prompt = new Label();
        _prompt.AddThemeFontSizeOverride("font_size", 17);
        _prompt.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.9f));
        _prompt.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.04f, 0.03f, 0.5f));
        _prompt.AddThemeConstantOverride("outline_size", 2);
        _prompt.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _prompt.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_prompt);

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

        _btnNew = MakeMenuButton(box);
        _btnNew.Pressed += () => _main.StartNewGame();

        _btnContinue = MakeMenuButton(box);
        _btnContinue.Pressed += () => _main.ContinueGame();

        _btnGallery = MakeMenuButton(box);
        _btnGallery.Pressed += () => _main.ShowGallery();

        // 语言选择器
        var langRow = new HBoxContainer();
        langRow.AddThemeConstantOverride("separation", 12);
        langRow.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        var langLabel = new Label();
        langLabel.AddThemeFontSizeOverride("font_size", 16);
        langLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        langLabel.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.04f, 0.03f, 0.7f));
        langLabel.AddThemeConstantOverride("outline_size", 2);
        langLabel.Name = "LangLabel";
        langRow.AddChild(langLabel);

        _lang = new OptionButton();
        _lang.AddThemeFontSizeOverride("font_size", 16);
        foreach (var (code, name) in LocCore.Languages)
            _lang.AddItem(name);
        _lang.ItemSelected += idx =>
        {
            Loc.I.SetLang(LocCore.Languages[(int)idx].Code);
            UpdateTexts();
        };
        langRow.AddChild(_lang);
        box.AddChild(langRow);

        // 版本脚注
        _ver = new Label();
        _ver.AddThemeFontSizeOverride("font_size", 13);
        _ver.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.8f));
        _ver.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.04f, 0.03f, 0.5f));
        _ver.AddThemeConstantOverride("outline_size", 2);
        _ver.SetAnchorsPreset(LayoutPreset.BottomWide);
        _ver.OffsetTop = -34;
        _ver.OffsetBottom = -12;
        _ver.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_ver);

        UpdateTexts();
        Loc.I.LanguageChanged += UpdateTexts;
    }

    public override void _Process(double delta) { QueueRedraw(); }

    private Button MakeMenuButton(VBoxContainer parent)
    {
        var b = new Button();
        b.AddThemeFontSizeOverride("font_size", 22);
        b.CustomMinimumSize = new Vector2(280, 54);
        b.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;

        // 白色底 + 深墨粗描边 + 下边压笔感（保持手绘漫画风）
        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.97f, 0.95f, 0.88f),
            BorderColor = InkStyle.InkColor,
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 4,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 10, ContentMarginBottom = 10,
        };
        var hover = new StyleBoxFlat
        {
            BgColor = new Color(0.90f, 0.85f, 0.70f),
            BorderColor = InkStyle.InkColor,
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 4,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 10, ContentMarginBottom = 10,
        };
        var pressed = new StyleBoxFlat
        {
            BgColor = new Color(0.78f, 0.70f, 0.54f),
            BorderColor = InkStyle.InkColor,
            BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 4, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 12, ContentMarginBottom = 8,
        };
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", pressed);
        b.AddThemeStyleboxOverride("focus", hover);
        b.AddThemeColorOverride("font_color", InkStyle.InkColor);
        parent.AddChild(b);
        return b;
    }

    private void UpdateTexts()
    {
        _subtitle.Text = Loc.I.T("app_subtitle");
        _prompt.Text = Loc.I.T("title_prompt");
        _btnNew.Text = Loc.I.T("ui_new_game");
        _btnContinue.Text = Loc.I.T("ui_continue");
        _btnContinue.Visible = GameState.I.HasSave();
        _btnGallery.Text = Loc.I.T("ui_gallery");
        _ver.Text = Loc.I.T("ui_version");
        _lang.Select(Array.FindIndex(LocCore.Languages, l => l.Code == Loc.I.Lang));
        var langLabel = _lang.GetParent().GetNode<Label>("LangLabel");
        langLabel.Text = Loc.I.T("ui_language");
    }

    // =========================================================
    // 程序化叠层：飘雪 + 灯笼光晕 + 暗角 vignette
    // =========================================================
    private partial class TitleFx : Control
    {
        private float _t;

        // 缓存 GradientTexture2D（禁止每帧 new！）
        private GradientTexture2D _lanternGrad, _vignetteGrad;
        private Vector2 _lastSize = new(-1, -1);

        private record Snowflake(float X, float Y, float Size, float SpeedY, float Drift, float RotSpeed);
        private Snowflake[] _snow = Array.Empty<Snowflake>();

        public override void _Ready()
        {
            SetAnchorsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Ignore;
            BuildSnow();
        }

        public override void _Notification(int what)
        {
            base._Notification(what);
            if (what == NotificationResized) EnsureGradients();
        }

        private void EnsureGradients()
        {
            var size = GetViewportRect().Size;
            if (size == _lastSize || size.X < 10) return;
            _lastSize = size;

            // 灯笼光晕（径向渐变，在城门洞位置）
            _lanternGrad = new GradientTexture2D();
            _lanternGrad.Gradient = new Gradient();
            _lanternGrad.Gradient.SetColor(0, new Color(1f, 0.72f, 0.30f, 0.55f));
            _lanternGrad.Gradient.SetColor(1, new Color(1f, 0.72f, 0.30f, 0f));
            _lanternGrad.Width = 240; _lanternGrad.Height = 240;
            _lanternGrad.Fill = GradientTexture2D.FillEnum.Radial;
            _lanternGrad.FillFrom = new Vector2(0.5f, 0.5f);
            _lanternGrad.FillTo = new Vector2(1f, 0.5f);

            // 暗角 vignette（让中心文字突出）
            var vigTex = new GradientTexture2D();
            vigTex.Gradient = new Gradient();
            vigTex.Gradient.SetColor(0, new Color(0, 0, 0, 0f));
            vigTex.Gradient.SetColor(1, new Color(0, 0, 0, 0.55f));
            vigTex.Width = (int)size.X; vigTex.Height = (int)size.Y;
            vigTex.Fill = GradientTexture2D.FillEnum.Radial;
            vigTex.FillFrom = new Vector2(0.5f, 0.6f);
            vigTex.FillTo = new Vector2(1f, 0.5f);
            _vignetteGrad = vigTex;
        }

        private void BuildSnow()
        {
            var rnd = new RandomNumberGenerator();
            rnd.Randomize();
            _snow = new Snowflake[60];
            for (int i = 0; i < _snow.Length; i++)
            {
                float sz, spd;
                if (i < 42) { sz = rnd.RandfRange(1.0f, 2.2f); spd = rnd.RandfRange(0.4f, 0.9f); }
                else if (i < 54) { sz = rnd.RandfRange(2.5f, 4.5f); spd = rnd.RandfRange(0.25f, 0.5f); }
                else { sz = rnd.RandfRange(4.5f, 6.5f); spd = rnd.RandfRange(0.15f, 0.35f); }
                _snow[i] = new Snowflake(
                    X: rnd.RandfRange(0f, 1f), Y: rnd.RandfRange(-0.2f, 1.0f),
                    Size: sz, SpeedY: spd,
                    Drift: rnd.RandfRange(-0.07f, 0.07f),
                    RotSpeed: rnd.RandfRange(-1.2f, 1.2f));
            }
        }

        public override void _Process(double delta)
        {
            _t += (float)delta;
            float dt = (float)delta;
            for (int i = 0; i < _snow.Length; i++)
            {
                var s = _snow[i];
                float ny = s.Y + s.SpeedY * dt * 0.07f;
                float nx = s.X + s.Drift * dt * 0.55f + Mathf.Sin(_t * 0.8f + i) * 0.0004f;
                if (ny > 1.08f) ny = -0.12f;
                if (nx < -0.08f) nx = 1.08f;
                if (nx > 1.08f) nx = -0.08f;
                _snow[i] = s with { X = nx, Y = ny };
            }
            QueueRedraw();
        }

        public override void _Draw()
        {
            var s = GetViewportRect().Size;
            float W = s.X, H = s.Y;

            // 确保渐变纹理已建
            EnsureGradients();
            if (_lanternGrad == null) return;

            // —— 暗角（最底层，先画）——
            DrawTextureRect(_vignetteGrad, new Rect2(Vector2.Zero, s), false);

            // —— 灯笼暖光晕（叠加在城门洞位置，坐标估一下）——
            float lx = W * 0.52f;
            float ly = H * 0.80f;
            DrawSetTransform(new Vector2(lx, ly), 0, Vector2.One);
            DrawTextureRect(_lanternGrad, new Rect2(-120, -120, 240, 240), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);

            // —— 飘雪层（最上）——
            for (int i = 0; i < _snow.Length; i++)
            {
                var fl = _snow[i];
                float sx = fl.X * W, sy = fl.Y * H;
                if (fl.Size <= 2.2f)
                    DrawCircle(new Vector2(sx, sy), fl.Size, new Color(0.96f, 0.97f, 0.98f, 0.85f));
                else if (fl.Size <= 4.5f)
                {
                    float rot = _t * fl.RotSpeed * 0.5f;
                    for (int a = 0; a < 6; a++)
                    {
                        float ang = rot + a * Mathf.Tau / 6f;
                        DrawLine(new Vector2(sx, sy),
                            new Vector2(sx + Mathf.Cos(ang) * fl.Size * 0.9f,
                                        sy + Mathf.Sin(ang) * fl.Size * 0.9f),
                            new Color(0.96f, 0.97f, 0.98f, 0.75f), 1.3f, true);
                    }
                }
                else
                {
                    float rot = _t * fl.RotSpeed * 0.4f;
                    for (int a = 0; a < 6; a++)
                    {
                        float ang = rot + a * Mathf.Tau / 6f;
                        DrawLine(new Vector2(sx, sy),
                            new Vector2(sx + Mathf.Cos(ang) * fl.Size,
                                        sy + Mathf.Sin(ang) * fl.Size),
                            new Color(0.96f, 0.97f, 0.98f, 0.85f), 1.6f, true);
                    }
                }
            }
        }
    }
}
