using System;
using Godot;

namespace Witness;

/// <summary>
/// 手绘漫画风标题界面（v0.7 重绘）。
/// 背景：古城墙（砖缝+裂纹+破损）+ 枯树（曲线锥形主干+分叉+树皮）+ 八角灯笼 + 远山 + 飘雪。
/// 全部程序化绘制，粗墨描边 + 平涂色块 + 内部硬边纹理线（Valiant Hearts 风格）。
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
        var paper = new ColorRect { Color = new Color(0.95f, 0.90f, 0.78f) };
        paper.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(paper);

        var art = new TitleArt { ZIndex = 0 };
        art.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(art);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        center.ZIndex = 1;
        AddChild(center);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 16);
        box.CustomMinimumSize = new Vector2(620, 420);
        center.AddChild(box);

        var title = new Label { Text = Loc.I.T("app_title") };
        title.AddThemeFontSizeOverride("font_size", 84);
        title.AddThemeColorOverride("font_color", InkStyle.InkColor);
        title.AddThemeColorOverride("font_outline_color", new Color(0.85f, 0.76f, 0.52f));
        title.AddThemeConstantOverride("outline_size", 4);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(title);

        _subtitle = new Label();
        _subtitle.AddThemeFontSizeOverride("font_size", 26);
        _subtitle.AddThemeColorOverride("font_color", new Color(0.30f, 0.22f, 0.12f));
        _subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_subtitle);

        _prompt = new Label();
        _prompt.AddThemeFontSizeOverride("font_size", 17);
        _prompt.AddThemeColorOverride("font_color", new Color(0.42f, 0.34f, 0.22f));
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

        var langRow = new HBoxContainer();
        langRow.AddThemeConstantOverride("separation", 12);
        langRow.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        var langLabel = new Label();
        langLabel.AddThemeFontSizeOverride("font_size", 16);
        langLabel.AddThemeColorOverride("font_color", new Color(0.42f, 0.34f, 0.22f));
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

        _ver = new Label();
        _ver.AddThemeFontSizeOverride("font_size", 13);
        _ver.AddThemeColorOverride("font_color", new Color(0.40f, 0.32f, 0.20f));
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
        b.CustomMinimumSize = new Vector2(280, 50);
        b.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;

        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.98f, 0.94f, 0.82f),
            BorderColor = InkStyle.InkColor,
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 4,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 10, ContentMarginBottom = 10,
        };
        var hover = new StyleBoxFlat
        {
            BgColor = new Color(0.92f, 0.84f, 0.62f),
            BorderColor = InkStyle.InkColor,
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 4,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 10, ContentMarginBottom = 10,
        };
        var pressed = new StyleBoxFlat
        {
            BgColor = new Color(0.82f, 0.72f, 0.50f),
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

    /// <summary>背景绘制（全部程序化，粗描边+平涂+内部硬边纹理线）。</summary>
    private partial class TitleArt : Control
    {
        private float _t;
        private Vector2 _mouse;

        // 缓存渐变纹理（严禁每帧 new！VignetteLayer 白屏坑）
        private GradientTexture2D _skyGrad, _moonGlowGrad, _lanternGrad;
        private Vector2 _lastSize = new(-1, -1);

        private record Snowflake(float X, float Y, float Size, float SpeedY, float Drift, float RotSpeed);
        private Snowflake[] _snow = Array.Empty<Snowflake>();

        public override void _Ready()
        {
            SetAnchorsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Ignore;
            EnsureGradients();
            BuildSnow();
        }

        public override void _Notification(int what)
        {
            base._Notification(what);
            if (what == NotificationResized)
            {
                EnsureGradients();
            }
        }

        private void EnsureGradients()
        {
            var size = GetViewportRect().Size;
            if (size == _lastSize) return;
            _lastSize = size;

            // 天空（顶部淡米黄 → 底部暗褐）
            _skyGrad = new GradientTexture2D();
            _skyGrad.Gradient = new Gradient();
            _skyGrad.Gradient.SetColor(0, new Color(0.88f, 0.84f, 0.70f));
            _skyGrad.Gradient.SetColor(1, new Color(0.58f, 0.50f, 0.36f));
            _skyGrad.Width = Math.Max(1, (int)size.X);
            _skyGrad.Height = Math.Max(1, (int)size.Y);
            _skyGrad.FillFrom = new Vector2(0.5f, 0f);
            _skyGrad.FillTo = new Vector2(0.5f, 1f);

            // 月亮光晕
            _moonGlowGrad = new GradientTexture2D();
            _moonGlowGrad.Gradient = new Gradient();
            _moonGlowGrad.Gradient.SetColor(0, new Color(1f, 1f, 0.88f, 0.30f));
            _moonGlowGrad.Gradient.SetColor(1, new Color(1f, 1f, 0.88f, 0f));
            _moonGlowGrad.Width = 220; _moonGlowGrad.Height = 220;
            _moonGlowGrad.Fill = GradientTexture2D.FillEnum.Radial;
            _moonGlowGrad.FillFrom = new Vector2(0.5f, 0.5f);
            _moonGlowGrad.FillTo = new Vector2(1f, 0.5f);

            // 灯笼光晕
            _lanternGrad = new GradientTexture2D();
            _lanternGrad.Gradient = new Gradient();
            _lanternGrad.Gradient.SetColor(0, new Color(1f, 0.72f, 0.30f, 0.55f));
            _lanternGrad.Gradient.SetColor(1, new Color(1f, 0.72f, 0.30f, 0f));
            _lanternGrad.Width = 200; _lanternGrad.Height = 200;
            _lanternGrad.Fill = GradientTexture2D.FillEnum.Radial;
            _lanternGrad.FillFrom = new Vector2(0.5f, 0.5f);
            _lanternGrad.FillTo = new Vector2(1f, 0.5f);
        }

        private void BuildSnow()
        {
            var rnd = new RandomNumberGenerator();
            rnd.Randomize();
            _snow = new Snowflake[55];
            for (int i = 0; i < _snow.Length; i++)
            {
                float sz;
                float spd;
                // 分层：小雪花多（飘得快）、大雪花少（飘得慢，前景）
                if (i < 38) { sz = rnd.RandfRange(1.0f, 2.2f); spd = rnd.RandfRange(0.4f, 0.9f); }
                else if (i < 50) { sz = rnd.RandfRange(2.5f, 4.5f); spd = rnd.RandfRange(0.25f, 0.5f); }
                else { sz = rnd.RandfRange(4.5f, 6.5f); spd = rnd.RandfRange(0.15f, 0.35f); }

                _snow[i] = new Snowflake(
                    X: rnd.RandfRange(0f, 1f),
                    Y: rnd.RandfRange(-0.2f, 1.0f),
                    Size: sz, SpeedY: spd,
                    Drift: rnd.RandfRange(-0.07f, 0.07f),
                    RotSpeed: rnd.RandfRange(-1.2f, 1.2f));
            }
        }

        public override void _Process(double delta)
        {
            _t += (float)delta;
            _mouse = GetViewport().GetMousePosition();
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

        // =========================================================
        // 主绘制
        // =========================================================
        public override void _Draw()
        {
            var s = GetViewportRect().Size;
            float W = s.X, H = s.Y;
            float groundY = H * 0.82f;

            // —— 天空 ——
            DrawTextureRect(_skyGrad, new Rect2(Vector2.Zero, s), false);

            // —— 月亮（右上）——
            float moonX = W * 0.82f;
            float moonY = H * 0.18f;
            DrawSetTransform(new Vector2(moonX, moonY), 0, Vector2.One);
            DrawTextureRect(_moonGlowGrad, new Rect2(-110, -110, 220, 220), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            DrawCircle(new Vector2(moonX, moonY), 18f, new Color(0.96f, 0.92f, 0.78f));
            DrawArc(new Vector2(moonX, moonY), 18f + 0.5f, 0f, Mathf.Tau, 16, InkStyle.InkColor, 2f, true);

            // —— 云（远山之上，淡墨剪影）——
            DrawClouds(W, H);

            // —— 远山三层（视差）——
            DrawFarMountains(W, H, groundY);
            DrawMidMountains(W, H, groundY);

            // —— 城墙（砖缝 + 裂纹 + 破损 + 箭窗 + 不整齐城垛 + 锯齿积雪帽）——
            float wallLeft = W * 0.40f + (_mouse.X - W * 0.5f) * -0.010f;
            float wallRight = W * 0.97f + (_mouse.X - W * 0.5f) * -0.010f;
            float wallTop = groundY - 190f;
            DrawCityWall(wallLeft, wallRight, wallTop, H);

            // —— 城门洞（拱顶曲线 + 石阶 + 内侧墙砖 + 挂绳 + 八角灯笼 + 光晕）——
            DrawGateAndLantern(wallLeft, wallTop, H);

            // —— 枯树（曲线锥形主干 + 锥形分叉分支 + 树皮纹 + 树节 + 枝头积雪簇 + 鸟巢）——
            float treeX = W * 0.08f + (_mouse.X - W * 0.5f) * -0.020f;
            float treeBaseY = groundY + 30f;
            DrawDeadTree(treeX, treeBaseY);

            // —— 石碑（基座 + 裂纹 + 积雪锯齿轮廓）——
            float stX = W * 0.21f + (_mouse.X - W * 0.5f) * -0.015f;
            DrawStele(stX, groundY + 40f);

            // —— 乌鸦（3 只，振翅 + 浮动）——
            DrawCrow(W * 0.58f + Mathf.Sin(_t * 0.4f) * 6f, 135f + Mathf.Sin(_t * 1.3f) * 8f, _t * 4f);
            DrawCrow(W * 0.74f + Mathf.Sin(_t * 0.5f + 1f) * 5f, 105f + Mathf.Sin(_t * 1.1f + 2f) * 6f, _t * 3f + 1f);
            DrawCrow(W * 0.29f + Mathf.Sin(_t * 0.3f + 2f) * 8f, 165f + Mathf.Sin(_t * 1.4f + 1f) * 10f, _t * 5f + 2f);

            // —— 地面（起伏 + 石子 + 断枝 + 草 + 积雪小片 + 不整齐边缘）——
            DrawGround(W, H, groundY);

            // —— 飘雪层（最上，分大小 + 旋转 + 下落角度）——
            DrawSnow(W, H);
        }

        // =========================================================
        // 云（淡墨剪影，随鼠标微动）
        // =========================================================
        private void DrawClouds(float W, float H)
        {
            float cloudParallax = (_mouse.X - W * 0.5f) * -0.008f;
            var cloudColor = new Color(0.78f, 0.72f, 0.58f, 0.50f);
            var cloudShadow = new Color(0.60f, 0.48f, 0.30f, 0.40f);
            for (int i = 0; i < 6; i++)
            {
                float cx = 140f + i * 220f + Mathf.Sin(_t * 0.12f + i) * 12f + cloudParallax;
                float cy = 80f + (i % 3) * 30f;
                // 云由多个椭圆剪影合并（用 PenStroke 模拟）
                var cloudShape = new Vector2[]
                {
                    new(cx - 85, cy), new(cx - 60, cy - 18),
                    new(cx - 30, cy - 24), new(cx + 10, cy - 22),
                    new(cx + 45, cy - 16), new(cx + 75, cy - 10),
                    new(cx + 90, cy), new(cx + 60, cy + 12),
                    new(cx - 20, cy + 12), new(cx - 70, cy + 8),
                };
                InkStyle.PenStroke(this, cloudShape, cloudColor, cloudShadow, 1.5f);
            }
        }

        // =========================================================
        // 远山（最远层：淡）
        // =========================================================
        private void DrawFarMountains(float W, float H, float groundY)
        {
            float parallaxX = (_mouse.X - W * 0.5f) * -0.005f;
            // 多峰起伏，不是单调三角
            var peaks = new Vector2[]
            {
                new(parallaxX - 60, groundY - 55),
                new(parallaxX + 80, groundY - 120),
                new(parallaxX + 200, groundY - 85),
                new(parallaxX + 310, groundY - 155),
                new(parallaxX + 430, groundY - 105),
                new(parallaxX + 560, groundY - 180),   // 主峰
                new(parallaxX + 680, groundY - 120),
                new(parallaxX + 800, groundY - 200),   // 次主峰
                new(parallaxX + 930, groundY - 145),
                new(parallaxX + 1060, groundY - 175),
                new(parallaxX + 1200, groundY - 130),
                new(parallaxX + 1290, groundY - 60),
            };
            var closed = new Vector2[peaks.Length + 2];
            peaks.CopyTo(closed, 0);
            closed[peaks.Length] = new(parallaxX + 1290, H);
            closed[peaks.Length + 1] = new(parallaxX - 60, H);
            var fillColor = new Color(0.55f, 0.48f, 0.36f);
            var shadowColor = new Color(0.40f, 0.32f, 0.22f);
            InkStyle.PenStroke(this, closed, fillColor, shadowColor, 1.5f);
            // 最远层山脚淡雾（用细折线模拟雾的上边缘）
            var fogEdge = new Vector2[]
            {
                new(parallaxX - 60, groundY - 55),
                new(parallaxX + 200, groundY - 50),
                new(parallaxX + 560, groundY - 45),
                new(parallaxX + 900, groundY - 55),
                new(parallaxX + 1290, groundY - 60),
            };
            DrawPolyline(fogEdge, new Color(0.75f, 0.72f, 0.65f, 0.25f), 40f, false);
        }

        // =========================================================
        // 中景山（更深）
        // =========================================================
        private void DrawMidMountains(float W, float H, float groundY)
        {
            float parallaxX = (_mouse.X - W * 0.5f) * -0.010f;
            var peaks = new Vector2[]
            {
                new(parallaxX - 90, groundY - 25),
                new(parallaxX + 60, groundY - 105),
                new(parallaxX + 180, groundY - 60),
                new(parallaxX + 300, groundY - 140),
                new(parallaxX + 440, groundY - 80),
                new(parallaxX + 580, groundY - 160),   // 主峰
                new(parallaxX + 720, groundY - 95),
                new(parallaxX + 860, groundY - 185),   // 次主峰
                new(parallaxX + 1000, groundY - 110),
                new(parallaxX + 1150, groundY - 140),
                new(parallaxX + 1290, groundY - 90),
            };
            var closed = new Vector2[peaks.Length + 2];
            peaks.CopyTo(closed, 0);
            closed[peaks.Length] = new(parallaxX + 1290, H);
            closed[peaks.Length + 1] = new(parallaxX - 90, H);
            var fillColor = new Color(0.38f, 0.32f, 0.22f);
            var shadowColor = new Color(0.28f, 0.22f, 0.16f);
            InkStyle.PenStroke(this, closed, fillColor, shadowColor, 2f);

            // 主峰+次主峰 山顶积雪（锯齿小多边形）
            DrawMountainSnow(parallaxX + 580, groundY - 160, 38f);
            DrawMountainSnow(parallaxX + 860, groundY - 185, 44f);
            DrawMountainSnow(parallaxX + 300, groundY - 140, 28f);

            // 中山脊线：淡色折线（模拟受光面棱线）
            var ridge = new Vector2[]
            {
                new(parallaxX + 300, groundY - 140),
                new(parallaxX + 440, groundY - 128),
                new(parallaxX + 580, groundY - 160),
            };
            DrawPolyline(ridge, new Color(0.52f, 0.44f, 0.30f, 0.7f), 1.5f, false);
        }

        private void DrawMountainSnow(float peakX, float peakY, float halfW)
        {
            var snow = new Vector2[]
            {
                new(peakX - halfW, peakY + halfW * 0.6f),
                new(peakX - halfW * 0.7f, peakY + 4f),
                new(peakX - halfW * 0.3f, peakY - 2f),
                new(peakX - halfW * 0.1f, peakY + 3f),
                new(peakX, peakY),
                new(peakX + halfW * 0.15f, peakY + 4f),
                new(peakX + halfW * 0.5f, peakY - 3f),
                new(peakX + halfW * 0.8f, peakY + 5f),
                new(peakX + halfW, peakY + halfW * 0.5f),
            };
            InkStyle.PenStroke(this, snow, InkStyle.Snow, InkStyle.SnowShadow, 1.2f);
        }

        // =========================================================
        // 古城墙（砖缝 + 裂纹 + 破损 + 箭窗 + 不整齐城垛 + 锯齿积雪帽）
        // =========================================================
        private void DrawCityWall(float xL, float xR, float top, float H)
        {
            float wallH = H - top + 20f;
            var wallColor = new Color(0.32f, 0.24f, 0.16f);
            var wallShadow = InkStyle.Shade(wallColor, 0.72f);
            var wallLight = InkStyle.Lighten(wallColor, 1.12f);

            // —— 1. 墙身主体（带一点轮廓起伏，不是直线）——
            int bricks = 28;  // 横向砖数
            float brickW = (xR - xL) / bricks;
            var body = new Vector2[]
            {
                new(xL, H),
                new(xL, top + 30f),
                new(xL + brickW * 0.8f, top + 28f),   // 左端略凸起
                new(xL + brickW, top + 30f),
                new(xR - brickW, top + 32f),          // 右端略凹
                new(xR - brickW * 0.4f, top + 30f),
                new(xR, top + 33f),
                new(xR, H),
            };
            InkStyle.PenStroke(this, body, wallColor, InkStyle.InkColor, 3.2f);

            // —— 2. 横向砖缝（每 24px 一行，6 行）——
            float[] rowYs = { top + 55f, top + 82f, top + 110f, top + 140f, top + 170f, top + 200f };
            foreach (float ry in rowYs)
            {
                if (ry > H - 20f) continue;
                DrawLine(new Vector2(xL + 4f, ry), new Vector2(xR - 4f, ry),
                    wallShadow, 1.2f, true);
            }

            // —— 3. 竖向灰缝（错位：奇偶行偏移 brickW/2）——
            int rows = 6;
            float firstRowY = top + 55f;
            for (int r = 0; r < rows; r++)
            {
                float ry = firstRowY + r * 28f;
                if (ry > H - 20f) continue;
                float offsetX = (r % 2 == 0) ? 0f : brickW * 0.5f;
                for (int b = 0; b <= bricks; b++)
                {
                    float vx = xL + b * brickW + offsetX;
                    if (vx <= xL + 2f || vx >= xR - 2f) continue;
                    DrawLine(new Vector2(vx, ry - 1f), new Vector2(vx, ry + 20f),
                        wallShadow, 0.9f, true);
                }
            }

            // —— 4. 箭窗（3 个，均匀分布，带内凹阴影）——
            float[] archXs = { xL + (xR - xL) * 0.18f, xL + (xR - xL) * 0.50f, xL + (xR - xL) * 0.82f };
            float archY = top + 90f;
            foreach (float ax in archXs)
            {
                // 墙身在箭窗位置有缺口（画黑色内凹）
                var archHole = new Vector2[]
                {
                    new(ax - 10f, archY),
                    new(ax - 10f, archY - 14f),
                    new(ax - 6f, archY - 18f),
                    new(ax, archY - 20f),
                    new(ax + 6f, archY - 18f),
                    new(ax + 10f, archY - 14f),
                    new(ax + 10f, archY),
                };
                InkStyle.PenStroke(this, archHole, new Color(0.06f, 0.04f, 0.03f), InkStyle.InkColor, 1.5f);
            }

            // —— 5. 破损缺口（墙身一处有破碎砖堆）——
            float breakX = xL + (xR - xL) * 0.68f;
            float breakY = top + 150f;
            var brokenHole = new Vector2[]
            {
                new(breakX - 22f, breakY),
                new(breakX - 18f, breakY - 26f),
                new(breakX - 8f, breakY - 18f),
                new(breakX - 2f, breakY - 30f),
                new(breakX + 10f, breakY - 20f),
                new(breakX + 18f, breakY - 32f),
                new(breakX + 24f, breakY - 22f),
                new(breakX + 20f, breakY),
            };
            InkStyle.PenStroke(this, brokenHole, new Color(0.06f, 0.04f, 0.03f), InkStyle.InkColor, 2f);
            // 缺口下堆 3 块破砖（不规则多边形）
            var brick1 = new Vector2[]
            {
                new(breakX - 22f, breakY + 2f), new(breakX - 14f, breakY - 2f),
                new(breakX - 8f, breakY + 4f), new(breakX - 18f, breakY + 8f),
            };
            InkStyle.PenStroke(this, brick1, wallColor, InkStyle.InkColor, 1.5f);
            var brick2 = new Vector2[]
            {
                new(breakX - 4f, breakY + 1f), new(breakX + 8f, breakY - 3f),
                new(breakX + 12f, breakY + 5f), new(breakX + 2f, breakY + 7f),
            };
            InkStyle.PenStroke(this, brick2, wallColor, InkStyle.InkColor, 1.5f);

            // —— 6. 随机裂纹（2 条折线）——
            var crack1 = new Vector2[]
            {
                new(xL + (xR - xL) * 0.32f, top + 45f),
                new(xL + (xR - xL) * 0.34f, top + 85f),
                new(xL + (xR - xL) * 0.31f, top + 125f),
                new(xL + (xR - xL) * 0.35f, top + 165f),
            };
            DrawPolyline(crack1, wallShadow, 1.0f, false);
            var crack2 = new Vector2[]
            {
                new(xL + (xR - xL) * 0.58f, top + 60f),
                new(xL + (xR - xL) * 0.56f, top + 110f),
                new(xL + (xR - xL) * 0.59f, top + 150f),
            };
            DrawPolyline(crack2, wallShadow, 1.0f, false);

            // —— 7. 右侧墙体外立面（硬边阴影，体现体积感）——
            var shadowFace = new Vector2[]
            {
                new(xR - brickW * 0.4f, top + 30f),
                new(xR, top + 33f),
                new(xR, H),
                new(xR - brickW * 1.2f, H),
            };
            DrawColoredPolygon(shadowFace, wallShadow);

            // —— 8. 城垛（雉堞，不整齐：高矮不一、有些倾斜、有些缺角）——
            int crenels = 14;
            float crenelW = (xR - xL - 40f) / crenels;
            float crenelBaseY = top + 30f;
            var rnd = new RandomNumberGenerator();
            for (int i = 0; i < crenels; i++)
            {
                float bx = xL + 20f + i * crenelW;
                rnd.Seed = (ulong)(42 + i * 17);
                float h = rnd.RandfRange(14f, 22f);
                float tilt = rnd.RandfRange(-2f, 2f);   // 倾斜
                float miss = rnd.RandfRange(0f, 1f);    // 0.15 概率缺角
                var cr = new Vector2[]
                {
                    new(bx - 6f + tilt * 0.5f, crenelBaseY),
                    new(bx - 6f + tilt, crenelBaseY - h),
                    new(bx + 5f + tilt * 0.3f, crenelBaseY - h - (i == 5 ? 3f : 0f)),
                    new(bx + 6f, crenelBaseY),
                };
                if (miss > 0.15f)
                    InkStyle.PenStroke(this, cr, wallColor, InkStyle.InkColor, 2.5f);
                else
                {
                    // 缺角：画半截
                    var crHalf = new Vector2[]
                    {
                        new(bx - 6f, crenelBaseY),
                        new(bx - 6f + tilt, crenelBaseY - h * 0.5f),
                        new(bx + 2f, crenelBaseY - h * 0.4f),
                        new(bx + 6f, crenelBaseY),
                    };
                    InkStyle.PenStroke(this, crHalf, wallColor, InkStyle.InkColor, 2f);
                }
                // 垛顶积雪（小三角）
                if (miss <= 0.15f || miss > 0.15f)
                {
                    float snowH = 2.5f;
                    var snowTop = new Vector2[]
                    {
                        new(bx - 5.5f, crenelBaseY - h + snowH),
                        new(bx + 5.5f, crenelBaseY - h + snowH),
                        new(bx + 3f, crenelBaseY - h),
                        new(bx - 3f, crenelBaseY - h),
                    };
                    InkStyle.PenStroke(this, snowTop, InkStyle.Snow, InkStyle.SnowShadow, 1f);
                }
            }

            // —— 9. 城墙顶部积雪帽（锯齿波浪，不是平直线）——
            float snowRowY = top + 30f;
            var snowCap = new Vector2[bricks * 4 + 2];
            snowCap[0] = new(xL, snowRowY);
            for (int i = 0; i < bricks; i++)
            {
                float bx = xL + i * brickW;
                // 每块砖上方一个积雪小峰
                snowCap[i * 4 + 1] = new(bx + 2f, snowRowY - 2.5f);
                snowCap[i * 4 + 2] = new(bx + brickW * 0.4f, snowRowY - 5f);
                snowCap[i * 4 + 3] = new(bx + brickW * 0.6f, snowRowY - 3f);
                snowCap[i * 4 + 4] = new(bx + brickW - 1f, snowRowY - 1.5f);
            }
            snowCap[bricks * 4 + 1] = new(xR, snowRowY);
            // 只画上面的边缘线（不闭合，因为墙已经画过了）
            DrawPolyline(snowCap, InkStyle.Snow, 2f, false);
        }

        // =========================================================
        // 城门洞（拱顶曲线 + 石阶 + 内侧墙砖 + 挂绳 + 八角灯笼 + 光晕）
        // =========================================================
        private void DrawGateAndLantern(float wallLeft, float wallTop, float H)
        {
            float gateX = wallLeft + 150f;
            float gateW = 76f;
            float gateBottomY = H - 60f;
            float gateTopArcY = gateBottomY - 110f;

            // —— 1. 拱顶曲线（用多段折线模拟椭圆拱）——
            var gateOutline = new Vector2[]
            {
                new(gateX, H),
                new(gateX, gateTopArcY + 20f),
                new(gateX + 12f, gateTopArcY - 28f),
                new(gateX + gateW * 0.5f, gateTopArcY - 40f),
                new(gateX + gateW - 12f, gateTopArcY - 28f),
                new(gateX + gateW, gateTopArcY + 20f),
                new(gateX + gateW, H),
            };
            InkStyle.PenStroke(this, gateOutline, new Color(0.05f, 0.03f, 0.02f), InkStyle.InkColor, 3.5f);

            // —— 2. 内侧墙砖纹理（门洞内有水平砖缝 + 竖向灰缝）——
            Color innerColor = new(0.12f, 0.08f, 0.06f);
            // 左右内侧墙面（三角形，从拱顶到下沿逐渐变宽）
            var innerLeft = new Vector2[]
            {
                new(gateX, gateTopArcY + 20f),
                new(gateX + 6f, gateTopArcY + 18f),
                new(gateX + 8f, gateBottomY - 20f),
                new(gateX + 10f, H - 10f),
                new(gateX, H),
            };
            InkStyle.PenStroke(this, innerLeft, innerColor, InkStyle.InkColor, 1.5f);
            var innerRight = new Vector2[]
            {
                new(gateX + gateW, gateTopArcY + 20f),
                new(gateX + gateW - 6f, gateTopArcY + 18f),
                new(gateX + gateW - 8f, gateBottomY - 20f),
                new(gateX + gateW - 10f, H - 10f),
                new(gateX + gateW, H),
            };
            InkStyle.PenStroke(this, innerRight, innerColor, InkStyle.InkColor, 1.5f);

            // —— 3. 石阶（门洞下沿，4 层叠起来）——
            for (int i = 0; i < 4; i++)
            {
                float stepH = 14f;
                float stepTop = H - 10f - stepH * (i + 1);
                float stepW = gateW - i * 6f;  // 向内收窄
                float stepX = gateX + (gateW - stepW) / 2f;
                var step = new Vector2[]
                {
                    new(stepX - 2f, stepTop + stepH),
                    new(stepX, stepTop),
                    new(stepX + stepW, stepTop),
                    new(stepX + stepW + 2f, stepTop + stepH),
                };
                InkStyle.PenStroke(this, step, new Color(0.36f, 0.28f, 0.18f), InkStyle.InkColor, 1.8f);
            }

            // —— 4. 灯笼光晕（在门洞内向外溢出）——
            float lanternCx = gateX + gateW * 0.5f;
            float lanternCy = gateBottomY - 50f;
            DrawSetTransform(new Vector2(lanternCx, lanternCy), 0, Vector2.One);
            DrawTextureRect(_lanternGrad, new Rect2(-100, -100, 200, 200), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);

            // —— 5. 灯笼挂绳（从拱顶中央垂下来）——
            DrawLine(new Vector2(gateX + gateW * 0.5f, gateTopArcY - 38f),
                new Vector2(lanternCx, lanternCy - 18f),
                InkStyle.InkColor, 1.3f, true);

            // —— 6. 八角灯笼本体（上下木框 + 中间朱红发光面 + 描边）——
            DrawOctLantern(lanternCx, lanternCy);
        }

        private void DrawOctLantern(float cx, float cy)
        {
            float r = 14f;
            // 灯笼八边形（上下收窄一点）
            var body = new Vector2[]
            {
                new(cx, cy - r - 4f),                          // 顶
                new(cx - r * 0.7f, cy - r * 0.5f - 2f),       // 右上斜
                new(cx - r * 1.0f, cy),                       // 右
                new(cx - r * 0.7f, cy + r * 0.5f + 2f),       // 右下斜
                new(cx, cy + r + 4f),                         // 底
                new(cx + r * 0.7f, cy + r * 0.5f + 2f),       // 左下斜
                new(cx + r * 1.0f, cy),                       // 左
                new(cx + r * 0.7f, cy - r * 0.5f - 2f),       // 左上斜
            };
            // 发光面（暖黄，内部）
            InkStyle.PenStroke(this, body, new Color(1f, 0.78f, 0.36f), InkStyle.InkColor, 2f);

            // 上下木框（棕色短横条）
            InkStyle.InkRect(this,
                new Rect2(cx - r * 0.6f, cy - r - 6f, r * 1.2f, 3f),
                new Color(0.34f, 0.22f, 0.12f), InkStyle.InkColor, 1.3f);
            InkStyle.InkRect(this,
                new Rect2(cx - r * 0.6f, cy + r + 3f, r * 1.2f, 3f),
                new Color(0.34f, 0.22f, 0.12f), InkStyle.InkColor, 1.3f);

            // 中间朱红竖条（贴在灯笼上的红纸，两个窄条）
            DrawLine(new Vector2(cx - 4f, cy - r), new Vector2(cx - 4f, cy + r),
                new Color(0.88f, 0.22f, 0.18f), 2.2f, true);
            DrawLine(new Vector2(cx + 4f, cy - r), new Vector2(cx + 4f, cy + r),
                new Color(0.88f, 0.22f, 0.18f), 2.2f, true);

            // 高光（左上小椭圆）
            DrawCircle(new Vector2(cx - r * 0.4f, cy - r * 0.3f), 2f,
                new Color(1f, 0.92f, 0.68f, 0.9f));

            // 流苏（底部 3 根短飘带）
            DrawLine(new Vector2(cx - 4f, cy + r + 6f), new Vector2(cx - 5f, cy + r + 16f),
                new Color(0.88f, 0.22f, 0.18f), 1.4f, true);
            DrawLine(new Vector2(cx, cy + r + 6f), new Vector2(cx, cy + r + 18f),
                new Color(0.88f, 0.22f, 0.18f), 1.4f, true);
            DrawLine(new Vector2(cx + 4f, cy + r + 6f), new Vector2(cx + 5f, cy + r + 16f),
                new Color(0.88f, 0.22f, 0.18f), 1.4f, true);
        }

        // =========================================================
        // 枯树（曲线锥形主干 + 锥形分叉分支 + 树皮纹 + 树节 + 枝头积雪簇 + 鸟巢）
        // =========================================================
        private void DrawDeadTree(float baseX, float baseY)
        {
            // —— 主干：多段折线逼近 S 型曲线锥形 ——
            // 控制点：底部 → 中部（S 弯）→ 顶部（被折断）
            var trunkPath = new Vector2[]
            {
                new(baseX, baseY),                  // 底
                new(baseX - 1f, baseY - 40f),
                new(baseX + 3f, baseY - 80f),      // 向右 S 弯
                new(baseX - 2f, baseY - 120f),
                new(baseX + 4f, baseY - 165f),     // 顶部前的小弯
                new(baseX + 1f, baseY - 200f),     // 顶端（被折断，尖头）
            };
            // 画锥形：每段宽度递减
            float[] trunkWidths = { 16f, 13f, 10f, 7f, 4f, 2f };
            var leftSide = new Vector2[trunkPath.Length];
            var rightSide = new Vector2[trunkPath.Length];
            for (int i = 0; i < trunkPath.Length; i++)
            {
                if (i < trunkPath.Length - 1)
                {
                    Vector2 dir = (trunkPath[i + 1] - trunkPath[i]).Normalized();
                    Vector2 perp = new Vector2(-dir.Y, dir.X);
                    leftSide[i] = trunkPath[i] + perp * trunkWidths[i];
                    rightSide[i] = trunkPath[i] - perp * trunkWidths[i];
                }
                else
                {
                    leftSide[i] = trunkPath[i];
                    rightSide[i] = trunkPath[i];
                }
            }
            // 组合成闭合多边形：left[0..N] + reverse(right[N-2..0])
            var trunkClosed = new Vector2[leftSide.Length + rightSide.Length - 1];
            for (int i = 0; i < leftSide.Length; i++) trunkClosed[i] = leftSide[i];
            int ti = leftSide.Length;
            for (int i = rightSide.Length - 2; i >= 0; i--) trunkClosed[ti++] = rightSide[i];
            trunkClosed[^1] = leftSide[0];   // 闭合回起点

            var barkColor = new Color(0.22f, 0.16f, 0.10f);
            var barkShadow = InkStyle.Shade(barkColor, 0.6f);
            var barkHighlight = InkStyle.Lighten(barkColor, 1.15f);
            InkStyle.PenStroke(this, trunkClosed, barkColor, InkStyle.InkColor, 2.8f);

            // —— 树皮纹：4 条纵向曲线描边 + 2 个树节圆环 ——
            DrawBarkLines(trunkPath, leftSide, rightSide);
            DrawKnot(new Vector2(baseX - 2f, baseY - 65f), 5f);
            DrawKnot(new Vector2(baseX + 2f, baseY - 130f), 4f);

            // —— 顶端折断毛刺（8-10 条短斜线朝外放射）——
            float topX = trunkPath[^1].X, topY = trunkPath[^1].Y;
            var rnd = new RandomNumberGenerator();
            rnd.Seed = 777;
            for (int i = 0; i < 10; i++)
            {
                float ang = Mathf.Pi * 0.5f + rnd.RandfRange(-0.7f, 0.7f) + i * 0.1f;
                float len = rnd.RandfRange(4f, 10f);
                DrawLine(new Vector2(topX, topY),
                    new Vector2(topX + Mathf.Cos(ang) * len, topY + Mathf.Sin(ang) * len),
                    InkStyle.InkColor, 1.4f, true);
            }

            // —— 主干顶端积雪 ——
            DrawSnowCluster(new Vector2(topX, topY - 1f), 3f, 5f);

            // —— 分支（8 个大分支，每个大分支再分 2-3 个小锥形分支）——
            // 每个分支 = (起点在 trunkPath 的第几段, 方向角度, 长度, 基宽)
            float[][] bigBranches =
            {
                // 左侧分支（从主干向左伸）
                new[] { 1f, -1.4f, 65f, 6f },   // 段1, 向左下
                new[] { 2.5f, -2.0f, 85f, 7f },  // 段2.5, 向左上
                new[] { 3.5f, -1.7f, 55f, 5f },  // 段3.5, 向左平
                new[] { 4f, -2.3f, 75f, 6f },    // 段4, 向左上
                // 右侧分支
                new[] { 0.5f, -0.3f, 55f, 6f },  // 段0.5, 向右下
                new[] { 1.8f, -0.8f, 75f, 7f },  // 段1.8, 向右
                new[] { 3f, -0.5f, 60f, 5f },    // 段3, 向右
                new[] { 4.2f, -0.9f, 80f, 6f },  // 段4.2, 向右上
            };
            foreach (var br in bigBranches)
            {
                float segIdx = br[0];  // 在 trunkPath 第几段
                float angle = br[1];   // 从 +X 轴向下的角度（弧度）
                float len = br[2];
                float baseW = br[3];
                Vector2 startPt = InterpOnTrunk(trunkPath, segIdx);
                Vector2 dirVec = new(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 endPt = startPt + dirVec * len;

                // 画主分支（锥形多边形，基宽 baseW → 尖端 1.5f）
                DrawConeBranch(startPt, endPt, baseW, 1.5f, barkColor, barkShadow);

                // 主分支再分叉 2-3 个小分支
                float smallCount = 2 + (segIdx < 3f ? 1 : 0);  // 下方分支多一个
                rnd.Seed = (ulong)(42 + (int)(segIdx * 10f));
                for (int k = 0; k < smallCount; k++)
                {
                    float t = rnd.RandfRange(0.3f, 0.75f);  // 在主分支上的位置
                    Vector2 smallStart = startPt + dirVec * len * t;
                    float smallLen = rnd.RandfRange(len * 0.3f, len * 0.55f);
                    float offsetAng = rnd.RandfRange(-0.6f, 0.6f) + (k % 2 == 0 ? -0.3f : 0.3f);
                    Vector2 smallDir = Rotate(dirVec, offsetAng);
                    Vector2 smallEnd = smallStart + smallDir * smallLen;
                    DrawConeBranch(smallStart, smallEnd, baseW * 0.55f, 1.0f, barkColor, barkShadow);

                    // 小分支末端积雪簇（70% 概率）
                    if (rnd.Randf() > 0.3f)
                        DrawSnowCluster(smallEnd, 2.5f, 4f);
                }

                // 大分支顶端积雪簇
                DrawSnowCluster(endPt, 3f, 5f);
            }

            // —— 鸟巢（右上分支的叉处）——
            Vector2 nestPt = InterpOnTrunk(trunkPath, 2.5f) + new Vector2(30f, -25f);
            DrawBirdNest(nestPt);
        }

        private Vector2 InterpOnTrunk(Vector2[] path, float segIdx)
        {
            int i = Mathf.FloorToInt(segIdx);
            float t = segIdx - i;
            if (i < 0) i = 0;
            if (i >= path.Length - 1) i = path.Length - 2;
            return path[i] + (path[i + 1] - path[i]) * t;
        }

        private Vector2 Rotate(Vector2 v, float angle)
        {
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
        }

        /// <summary>画锥形分支：基宽 baseW，尖端 tipW，中间略微弯曲（加一个控制点）。</summary>
        private void DrawConeBranch(Vector2 from, Vector2 to, float baseW, float tipW, Color fill, Color shadow)
        {
            Vector2 dir = (to - from).Normalized();
            Vector2 perp = new(-dir.Y, dir.X);
            // 中间控制点：朝 perp 方向凸一点
            Vector2 midPt = (from + to) * 0.5f + perp * 4f;

            Vector2 aL = from + perp * baseW;
            Vector2 aR = from - perp * baseW;
            Vector2 mL = midPt + perp * (baseW + tipW) * 0.5f * 0.6f;
            Vector2 mR = midPt - perp * (baseW + tipW) * 0.5f * 0.6f;
            Vector2 bL = to + perp * tipW;
            Vector2 bR = to - perp * tipW;

            var cone = new Vector2[] { aL, mL, bL, bR, mR, aR };
            InkStyle.PenStroke(this, cone, fill, InkStyle.InkColor, 2.2f);

            // 高光（沿左侧 perp 画一条淡色细线）
            DrawLine(aL, mL, shadow, 1.0f, false);
        }

        /// <summary>树皮纵纹：沿主干画几条随机弯曲的细线。</summary>
        private void DrawBarkLines(Vector2[] path, Vector2[] left, Vector2[] right)
        {
            var rnd = new RandomNumberGenerator();
            var barkStroke = new Color(0.12f, 0.08f, 0.05f);
            for (int line = 0; line < 4; line++)
            {
                rnd.Seed = (ulong)(line * 1234);
                float shift = rnd.RandfRange(-1f, 1f);
                var linePts = new Vector2[path.Length];
                for (int i = 0; i < path.Length; i++)
                {
                    float t = (float)i / (path.Length - 1);
                    Vector2 center = path[i];
                    Vector2 dir = i < path.Length - 1 ? (path[i + 1] - path[i]).Normalized() : Vector2.Zero;
                    Vector2 perp = new(-dir.Y, dir.X);
                    // 在左侧 + shift*0.3 到右侧 + shift*0.3 之间波动
                    float wAtI = Mathf.Lerp(10f, 2f, t);
                    float offset = shift * wAtI * 0.35f + rnd.RandfRange(-1.5f, 1.5f);
                    linePts[i] = center + perp * offset;
                }
                DrawPolyline(linePts, barkStroke, 0.9f, false);
            }
        }

        /// <summary>树节：一个小圆环 + 中心墨点。</summary>
        private void DrawKnot(Vector2 c, float r)
        {
            DrawArc(c, r, 0, Mathf.Tau, 12, InkStyle.InkColor, 1.3f, true);
            DrawCircle(c, r * 0.4f, InkStyle.InkColor);
            DrawCircle(c + new Vector2(-r * 0.15f, -r * 0.15f), r * 0.1f, new Color(0.45f, 0.32f, 0.20f));
        }

        /// <summary>积雪簇：不规则小锯齿多边形（不是圆点）。</summary>
        private void DrawSnowCluster(Vector2 c, float halfW, float height)
        {
            var rnd = new RandomNumberGenerator();
            rnd.Seed = (ulong)((int)c.X * 31 + (int)c.Y * 17);
            int segs = 6;
            var pts = new Vector2[segs];
            for (int i = 0; i < segs; i++)
            {
                float t = (float)i / (segs - 1);
                float x = c.X - halfW + t * halfW * 2f;
                float y = c.Y - height + rnd.RandfRange(-1.5f, 1.5f);
                pts[i] = new Vector2(x, y);
            }
            var cluster = new Vector2[segs + 2];
            cluster[0] = new Vector2(c.X + halfW, c.Y);
            for (int i = 0; i < segs; i++) cluster[1 + i] = pts[segs - 1 - i];
            cluster[segs + 1] = new Vector2(c.X - halfW, c.Y);
            InkStyle.PenStroke(this, cluster, InkStyle.Snow, InkStyle.SnowShadow, 1.2f);
        }

        /// <summary>鸟巢：一堆交叉的短树枝 + 一个椭圆底。</summary>
        private void DrawBirdNest(Vector2 c)
        {
            var nest = new Vector2[]
            {
                new(c.X - 10f, c.Y),
                new(c.X - 8f, c.Y - 6f),
                new(c.X, c.Y - 9f),
                new(c.X + 8f, c.Y - 6f),
                new(c.X + 10f, c.Y),
            };
            InkStyle.PenStroke(this, nest, new Color(0.36f, 0.24f, 0.14f), InkStyle.InkColor, 1.5f);
            // 3 根交叉短枝
            DrawLine(new Vector2(c.X - 8f, c.Y - 3f), new Vector2(c.X + 6f, c.Y - 7f),
                InkStyle.InkColor, 1.1f, true);
            DrawLine(new Vector2(c.X + 8f, c.Y - 2f), new Vector2(c.X - 5f, c.Y - 8f),
                InkStyle.InkColor, 1.1f, true);
            DrawLine(new Vector2(c.X - 3f, c.Y - 1f), new Vector2(c.X + 2f, c.Y - 9f),
                InkStyle.InkColor, 1.0f, true);
        }

        // =========================================================
        // 石碑（基座 + 碑身裂纹 + 积雪锯齿轮廓）
        // =========================================================
        private void DrawStele(float cx, float baseY)
        {
            // 基座（梯形，在碑身下方）
            var pedestal = new Vector2[]
            {
                new(cx - 26f, baseY),
                new(cx - 22f, baseY - 12f),
                new(cx + 22f, baseY - 12f),
                new(cx + 26f, baseY),
            };
            InkStyle.PenStroke(this, pedestal, new Color(0.30f, 0.22f, 0.14f), InkStyle.InkColor, 2.5f);

            // 碑身（瘦高梯形 + 顶帽）
            float topY = baseY - 122f;
            float midY = baseY - 68f;
            var body = new Vector2[]
            {
                new(cx - 17f, baseY - 12f),
                new(cx - 14f, topY),
                new(cx, topY - 10f),
                new(cx + 14f, topY),
                new(cx + 17f, baseY - 12f),
            };
            InkStyle.PenStroke(this, body, new Color(0.34f, 0.26f, 0.18f), InkStyle.InkColor, 2.8f);

            // 碑身裂纹（一条折线）
            var crack = new Vector2[]
            {
                new(cx - 5f, topY + 18f),
                new(cx - 2f, topY + 45f),
                new(cx - 6f, topY + 72f),
                new(cx - 3f, topY + 96f),
            };
            DrawPolyline(crack, InkStyle.Shade(new Color(0.34f, 0.26f, 0.18f), 0.5f), 0.9f, false);

            // 碑顶积雪（锯齿小多边形，贴合碑帽轮廓）
            var stSnow = new Vector2[]
            {
                new(cx - 16f, topY),
                new(cx - 10f, topY - 4f),
                new(cx - 4f, topY - 2f),
                new(cx, topY - 10f),
                new(cx + 4f, topY - 2f),
                new(cx + 10f, topY - 4f),
                new(cx + 16f, topY),
            };
            InkStyle.PenStroke(this, stSnow, InkStyle.Snow, InkStyle.SnowShadow, 1.2f);

            // 卡通高光小点
            InkStyle.KawaiiHighlight(this, new Vector2(cx - 12f, topY + 30f), 2f,
                new Color(0.88f, 0.76f, 0.52f));
        }

        // =========================================================
        // 乌鸦（振翅 + 浮动）
        // =========================================================
        private void DrawCrow(float x, float y, float flapPhase)
        {
            float flap = Mathf.Sin(flapPhase) * 6f;
            var crow = new Vector2[]
            {
                new(x - 12f, y + flap),
                new(x - 2f, y - 4f),
                new(x + 2f, y - 2f),
                new(x + 12f, y + flap),
                new(x + 6f, y + 2f),
                new(x, y + 3f),
            };
            DrawColoredPolygon(crow, new Color(0.08f, 0.06f, 0.05f));
            DrawPolyline(crow, InkStyle.InkColor, 2f, true);
        }

        // =========================================================
        // 地面（起伏 + 石子 + 断枝 + 草 + 积雪小片 + 不整齐上缘）
        // =========================================================
        private void DrawGround(float W, float H, float groundY)
        {
            var topEdge = new Vector2[]
            {
                new(0, groundY + 40f),
                new(W * 0.08f, groundY + 44f),
                new(W * 0.18f, groundY + 36f),
                new(W * 0.30f, groundY + 50f),
                new(W * 0.42f, groundY + 38f),
                new(W * 0.55f, groundY + 52f),
                new(W * 0.68f, groundY + 40f),
                new(W * 0.82f, groundY + 48f),
                new(W * 0.92f, groundY + 36f),
                new(W, groundY + 42f),
            };
            var groundPts = new Vector2[topEdge.Length + 2];
            Array.Copy(topEdge, groundPts, topEdge.Length);
            groundPts[topEdge.Length] = new(W, H);
            groundPts[topEdge.Length + 1] = new(0, H);
            var groundColor = new Color(0.18f, 0.14f, 0.10f);
            InkStyle.PenStroke(this, groundPts, groundColor, InkStyle.InkColor, 3f);

            // 地面点缀（石子 + 断枝 + 积雪小片 + 草）
            var rnd = new RandomNumberGenerator();
            for (int i = 0; i < 22; i++)
            {
                rnd.Seed = (ulong)(i * 47);
                float rx = rnd.RandfRange(20f, W - 20f);
                float ry = groundY + 42f + rnd.RandfRange(-2f, 8f);
                float type = rnd.Randf();
                if (type < 0.45f)
                {
                    // 小石子（不规则多边形 + 描边）
                    var rock = new Vector2[]
                    {
                        new(rx - 4f, ry), new(rx - 2f, ry - 3f),
                        new(rx + 3f, ry - 2f), new(rx + 4f, ry + 1f),
                        new(rx, ry + 2f),
                    };
                    InkStyle.PenStroke(this, rock, new Color(0.32f, 0.24f, 0.16f), InkStyle.InkColor, 1.6f);
                }
                else if (type < 0.7f)
                {
                    // 断枝（2-3 节短折线）
                    DrawLine(new Vector2(rx - 5f, ry), new Vector2(rx + 1f, ry - 2f),
                        InkStyle.InkColor, 1.3f, true);
                    if (rnd.Randf() > 0.5f)
                        DrawLine(new Vector2(rx + 1f, ry - 2f), new Vector2(rx + 5f, ry),
                            InkStyle.InkColor, 1.2f, true);
                }
                else if (type < 0.88f)
                {
                    // 积雪小片（小椭圆）
                    DrawCircle(new Vector2(rx, ry - 1f), rnd.RandfRange(2f, 4f),
                        new Color(0.92f, 0.93f, 0.95f, 0.8f));
                }
                else
                {
                    // 小草（3 根短竖线）
                    DrawLine(new Vector2(rx, ry), new Vector2(rx - 2f, ry - 4f),
                        InkStyle.InkColor, 1.0f, true);
                    DrawLine(new Vector2(rx, ry), new Vector2(rx, ry - 5f),
                        InkStyle.InkColor, 1.0f, true);
                    DrawLine(new Vector2(rx, ry), new Vector2(rx + 2f, ry - 3.5f),
                        InkStyle.InkColor, 1.0f, true);
                }
            }
        }

        // =========================================================
        // 飘雪（分大小三层 + 微旋转 + 下落角度）
        // =========================================================
        private void DrawSnow(float W, float H)
        {
            for (int i = 0; i < _snow.Length; i++)
            {
                var fl = _snow[i];
                float sx = fl.X * W;
                float sy = fl.Y * H;

                // 小雪花：白点
                if (fl.Size <= 2.2f)
                {
                    DrawCircle(new Vector2(sx, sy), fl.Size, new Color(0.96f, 0.97f, 0.98f, 0.8f));
                }
                else if (fl.Size <= 4.5f)
                {
                    // 中雪花：小六角星（6 根短线）
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
                    // 大雪花：双层六角星（主 + 次）
                    float rot = _t * fl.RotSpeed * 0.4f;
                    for (int a = 0; a < 6; a++)
                    {
                        float ang = rot + a * Mathf.Tau / 6f;
                        DrawLine(new Vector2(sx, sy),
                            new Vector2(sx + Mathf.Cos(ang) * fl.Size,
                                        sy + Mathf.Sin(ang) * fl.Size),
                            new Color(0.96f, 0.97f, 0.98f, 0.85f), 1.6f, true);
                        // 次层：30° 偏移 + 更短
                        float ang2 = rot + a * Mathf.Tau / 6f + Mathf.Pi / 6f;
                        DrawLine(new Vector2(sx, sy),
                            new Vector2(sx + Mathf.Cos(ang2) * fl.Size * 0.6f,
                                        sy + Mathf.Sin(ang2) * fl.Size * 0.6f),
                            new Color(0.96f, 0.97f, 0.98f, 0.6f), 1.0f, true);
                    }
                }
            }
        }
    }
}
