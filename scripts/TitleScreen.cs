using System;
using Godot;

namespace Witness;

/// <summary>
/// 手绘漫画风标题界面（重写）：
///   • 背景：古城墙剪影 + 枯树 + 乌鸦 + 石碑（全部程序化绘制，粗描边+平涂）
///   • 居中大字"见证者"，副标题"从 1895 年的源头开始，走进被见证的历史"
///   • 三个菜单按钮 + 9 语言选择器
///   • 语言切换即时生效；版本号脚注
/// </summary>
public partial class TitleScreen : Control
{
    private readonly Main _main;
    private Label _subtitle, _prompt;
    private Button _btnNew, _btnContinue, _btnGallery;
    private OptionButton _lang;
    private Label _ver;
    private float _t;

    public TitleScreen(Main main) { _main = main; }

    public override void _Ready()
    {
        // 先铺一张浅米黄的"纸"底，上面叠加手绘标题图（程序化 Node2D）
        var paper = new ColorRect { Color = new Color(0.95f, 0.90f, 0.78f) };
        paper.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(paper);

        var art = new TitleArt { ZIndex = 0 };
        art.SetAnchorsPreset(LayoutPreset.FullRect);   // Node2D 不支持，后续手动在 _Draw 里用 ViewportRect
        AddChild(art);

        // 居中文字栏
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        center.ZIndex = 1;
        AddChild(center);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 16);
        box.CustomMinimumSize = new Vector2(620, 420);
        center.AddChild(box);

        var title = new Label
        {
            Text = Loc.I.T("app_title"),
        };
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

        // 语言选择器
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

        // 版本脚注
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

    public override void _Process(double delta)
    {
        _t += (float)delta;
        QueueRedraw();
    }

    private Button MakeMenuButton(VBoxContainer parent)
    {
        var b = new Button();
        b.AddThemeFontSizeOverride("font_size", 22);
        b.CustomMinimumSize = new Vector2(280, 50);
        b.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;

        // 手绘漫画风按钮：浅米色 + 深墨描边 + 卡通阴影偏移
        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.98f, 0.94f, 0.82f),
            BorderColor = InkStyle.InkColor,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            BorderWidthBottom = 4,   // 下边比上边粗一点，像手绘压笔
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 18,
            ContentMarginRight = 18,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
        };
        var hover = new StyleBoxFlat
        {
            BgColor = new Color(0.92f, 0.84f, 0.62f),
            BorderColor = InkStyle.InkColor,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            BorderWidthBottom = 4,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 18,
            ContentMarginRight = 18,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
        };
        var pressed = new StyleBoxFlat
        {
            BgColor = new Color(0.82f, 0.72f, 0.50f),
            BorderColor = InkStyle.InkColor,
            BorderWidthLeft = 3,
            BorderWidthRight = 3,
            BorderWidthTop = 4,
            BorderWidthBottom = 2,   // 按下时上下粗度反转
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 18,
            ContentMarginRight = 18,
            ContentMarginTop = 12,
            ContentMarginBottom = 8,
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
        _lang.Select(System.Array.FindIndex(LocCore.Languages, l => l.Code == Loc.I.Lang));
        var langLabel = _lang.GetParent().GetNode<Label>("LangLabel");
        langLabel.Text = Loc.I.T("ui_language");
    }

    /// <summary>
    /// 手绘漫画风标题背景：古城墙 + 枯树 + 乌鸦 + 石碑 + 飘雪 + 灯笼光。
    /// 整体用墨色剪影 + 粗描边 + 卡通高光 + 少量径向渐变（灯笼/月亮光晕）。
    /// </summary>
    private partial class TitleArt : Control
    {
        private float _t;
        private Vector2 _mouse;
        private record Snowflake(float X, float Y, float Size, float SpeedY, float Drift);
        private Snowflake[] _snow = Array.Empty<Snowflake>();

        public override void _Ready()
        {
            SetAnchorsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Ignore;
            // 预先生成 40 颗飘雪
            var rnd = new RandomNumberGenerator();
            rnd.Randomize();
            _snow = new Snowflake[40];
            for (int i = 0; i < _snow.Length; i++)
                _snow[i] = new Snowflake(
                    X: rnd.RandfRange(0f, 1f),     // 比例坐标，与分辨率无关
                    Y: rnd.RandfRange(-0.2f, 1.0f),
                    Size: rnd.RandfRange(1.0f, 3.0f),
                    SpeedY: rnd.RandfRange(0.25f, 0.7f),
                    Drift: rnd.RandfRange(-0.06f, 0.06f));
        }

        public override void _Process(double delta)
        {
            _t += (float)delta;
            _mouse = GetViewport().GetMousePosition();
            // 更新飘雪位置（用比例坐标，适配任意分辨率）
            float dt = (float)delta;
            for (int i = 0; i < _snow.Length; i++)
            {
                var s = _snow[i];
                float ny = s.Y + s.SpeedY * dt * 0.06f;
                float nx = s.X + s.Drift * dt * 0.5f + Mathf.Sin(_t * 0.7f + i) * 0.0003f;
                if (ny > 1.05f) ny = -0.1f;
                if (nx < -0.05f) nx = 1.05f;
                if (nx > 1.05f) nx = -0.05f;
                _snow[i] = s with { X = nx, Y = ny };
            }
            QueueRedraw();
        }

        public override void _Draw()
        {
            var s = GetViewportRect().Size;
            float W = s.X, H = s.Y;
            var centerY = H * 0.82f;

            // —— 天空渐变（唯一场合：月亮光晕需要）——
            var skyRect = new Rect2(Vector2.Zero, s);
            // 夜空米黄渐变（顶部亮，底部暗，突出夜景氛围）
            GradientTexture2D sky = new();
            sky.Gradient = new Gradient();
            sky.Gradient.SetColor(0, new Color(0.85f, 0.82f, 0.68f));    // 顶部米黄
            sky.Gradient.SetColor(1, new Color(0.62f, 0.54f, 0.40f));    // 底部暗褐
            sky.Width = (int)W; sky.Height = (int)H;
            sky.FillFrom = new Vector2(0.5f, 0f);
            sky.FillTo = new Vector2(0.5f, 1f);
            DrawTextureRect(sky, skyRect, false);

            // —— 月亮（右上，18px 圆 + 光晕）——
            float moonX = W * 0.82f;
            float moonY = H * 0.18f;
            // 光晕
            GradientTexture2D moonGlow = new();
            moonGlow.Gradient = new Gradient();
            moonGlow.Gradient.SetColor(0, new Color(1f, 1f, 0.9f, 0.35f));
            moonGlow.Gradient.SetColor(1, new Color(1f, 1f, 0.9f, 0f));
            moonGlow.Width = 200; moonGlow.Height = 200;
            moonGlow.Fill = GradientTexture2D.FillEnum.Radial;
            moonGlow.FillFrom = new Vector2(0.5f, 0.5f);
            moonGlow.FillTo = new Vector2(1f, 0.5f);
            DrawSetTransform(new Vector2(moonX, moonY), 0, Vector2.One);
            DrawTextureRect(moonGlow, new Rect2(-100, -100, 200, 200), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            DrawCircle(new Vector2(moonX, moonY), 18f, new Color(0.96f, 0.92f, 0.78f));
            DrawArc(new Vector2(moonX, moonY), 18f + 0.5f, 0f, Mathf.Tau, 16, InkStyle.InkColor, 2f, true);

            // —— 远景远山剪影（视差层 1：最远，最淡）——
            float parallaxFarX = (_mouse.X - W * 0.5f) * -0.005f;   // 反方向微动
            var mountainFar = new Vector2[]
            {
                new(parallaxFarX - 50, centerY - 60f),
                new(parallaxFarX + 120, centerY - 170f),
                new(parallaxFarX + 260, centerY - 120f),
                new(parallaxFarX + 400, centerY - 200f),
                new(parallaxFarX + 540, centerY - 140f),
                new(parallaxFarX + 720, centerY - 210f),
                new(parallaxFarX + 900, centerY - 160f),
                new(parallaxFarX + 1100, centerY - 200f),
                new(parallaxFarX + 1280, centerY - 150f),
                new(parallaxFarX + 1280, centerY - 60f),
            };
            // 闭合到底部
            var mFarClosed = new Vector2[mountainFar.Length + 2];
            Array.Copy(mountainFar, mFarClosed, mountainFar.Length);
            mFarClosed[mountainFar.Length] = new(parallaxFarX + 1280, H);
            mFarClosed[mountainFar.Length + 1] = new(parallaxFarX - 50, H);
            InkStyle.PenStroke(this, mFarClosed, new Color(0.55f, 0.48f, 0.36f), new Color(0.40f, 0.32f, 0.22f), 1.5f);

            // —— 中景山剪影（视差层 2：更靠前，颜色更深）——
            float parallaxMidX = (_mouse.X - W * 0.5f) * -0.01f;
            var mountainMid = new Vector2[]
            {
                new(parallaxMidX - 80, centerY - 30f),
                new(parallaxMidX + 100, centerY - 130f),
                new(parallaxMidX + 280, centerY - 80f),
                new(parallaxMidX + 460, centerY - 160f),
                new(parallaxMidX + 640, centerY - 100f),
                new(parallaxMidX + 820, centerY - 170f),
                new(parallaxMidX + 1020, centerY - 120f),
                new(parallaxMidX + 1280, centerY - 100f),
            };
            var mMidClosed = new Vector2[mountainMid.Length + 2];
            Array.Copy(mountainMid, mMidClosed, mountainMid.Length);
            mMidClosed[mountainMid.Length] = new(parallaxMidX + 1280, H);
            mMidClosed[mountainMid.Length + 1] = new(parallaxMidX - 80, H);
            InkStyle.PenStroke(this, mMidClosed, new Color(0.38f, 0.32f, 0.22f), new Color(0.28f, 0.22f, 0.16f), 2f);

            // —— 远景淡墨云（随鼠标微动）——
            float cloudParallax = (_mouse.X - W * 0.5f) * -0.008f;
            for (int i = 0; i < 5; i++)
            {
                float cx = 180f + i * 250f + Mathf.Sin(_t * 0.15f + i) * 10f + cloudParallax;
                float cy = 90f + (i % 2) * 35f;
                InkStyle.PenStroke(this, new Vector2[]
                {
                    new(cx - 70, cy), new(cx - 40, cy - 14),
                    new(cx + 10, cy - 18), new(cx + 50, cy - 10),
                    new(cx + 80, cy), new(cx + 50, cy + 10),
                    new(cx - 30, cy + 10),
                }, new Color(0.82f, 0.76f, 0.62f, 0.55f), new Color(0.62f, 0.50f, 0.32f, 0.45f), 2f);
            }

            // —— 城墙剪影（中央偏右，主焦点）——
            float wallLeft = W * 0.42f + (_mouse.X - W * 0.5f) * -0.01f;  // 视差
            float wallRight = W * 0.96f + (_mouse.X - W * 0.5f) * -0.01f;
            float wallTop = centerY - 200f;
            float wallH = H - centerY + 20f;

            // 墙身
            var wallBody = new Vector2[]
            {
                new(wallLeft, H), new(wallLeft, wallTop + 30f),
                new(wallLeft + 30f, wallTop + 30f), new(wallLeft + 30f, wallTop),
                new(wallLeft + 70f, wallTop), new(wallLeft + 70f, wallTop + 30f),
                new(wallLeft + 120f, wallTop + 30f), new(wallLeft + 120f, wallTop - 12f),
                new(wallLeft + 170f, wallTop - 12f), new(wallLeft + 170f, wallTop + 30f),
                new(wallLeft + 230f, wallTop + 30f), new(wallLeft + 230f, wallTop),
                new(wallRight - 250f, wallTop), new(wallRight - 250f, wallTop + 30f),
                new(wallRight - 200f, wallTop + 30f), new(wallRight - 200f, wallTop),
                new(wallRight - 140f, wallTop), new(wallRight - 140f, wallTop + 30f),
                new(wallRight, wallTop + 30f), new(wallRight, H),
            };
            InkStyle.PenStroke(this, wallBody, new Color(0.32f, 0.24f, 0.16f), InkStyle.InkColor, 3.5f);

            // 城墙顶部积雪帽（细白线，画出城砖缝上的雪）
            for (int i = 0; i < 12; i++)
            {
                float bx = wallLeft + 20f + i * ((wallRight - wallLeft) - 40f) / 11f;
                InkStyle.PenStroke(this, new Vector2[]
                {
                    new(bx - 6f, wallTop), new(bx - 4f, wallTop - 3f),
                    new(bx + 2f, wallTop - 2f), new(bx + 4f, wallTop),
                }, new Color(0.95f, 0.96f, 0.98f), new Color(0.92f, 0.93f, 0.95f, 0.6f), 1f);
            }

            // 城垛（雉堞）
            for (int i = 0; i < 12; i++)
            {
                float bx = wallLeft + 20f + i * ((wallRight - wallLeft) - 40f) / 11f;
                var crenel = new Vector2[]
                {
                    new(bx - 8f, wallTop), new(bx - 8f, wallTop - 18f),
                    new(bx + 6f, wallTop - 18f), new(bx + 6f, wallTop),
                };
                InkStyle.PenStroke(this, crenel, new Color(0.36f, 0.28f, 0.20f), InkStyle.InkColor, 2.5f);
                // 垛子顶积雪
                DrawLine(new Vector2(bx - 7f, wallTop - 18f), new Vector2(bx + 5f, wallTop - 18f),
                    new Color(0.95f, 0.96f, 0.98f, 0.85f), 2.0f, true);
            }

            // 城门洞 + 灯笼光
            float gateX = wallLeft + 140f;
            float gateW = 70f, gateH = 120f;
            var gate = new Vector2[]
            {
                new(gateX, H), new(gateX, H - gateH),
                new(gateX + gateW * 0.5f, H - gateH - 30f),
                new(gateX + gateW, H - gateH), new(gateX + gateW, H),
            };
            InkStyle.PenStroke(this, gate, new Color(0.06f, 0.04f, 0.03f), InkStyle.InkColor, 3f);

            // 灯笼光（温暖橘黄径向渐变，城门洞内向外溢出）
            float gateCenterX = gateX + gateW * 0.5f;
            float gateCenterY = H - gateH * 0.45f;
            GradientTexture2D lantern = new();
            lantern.Gradient = new Gradient();
            lantern.Gradient.SetColor(0, new Color(1f, 0.72f, 0.30f, 0.55f));   // 内亮
            lantern.Gradient.SetColor(1, new Color(1f, 0.72f, 0.30f, 0f));    // 外淡
            lantern.Width = 180; lantern.Height = 180;
            lantern.Fill = GradientTexture2D.FillEnum.Radial;
            lantern.FillFrom = new Vector2(0.5f, 0.5f);
            lantern.FillTo = new Vector2(1f, 0.5f);
            DrawSetTransform(new Vector2(gateCenterX, gateCenterY), 0, Vector2.One);
            DrawTextureRect(lantern, new Rect2(-90, -90, 180, 180), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);

            // 灯笼本体（城门洞内一个小方灯笼）
            float lanternX = gateCenterX;
            float lanternY = gateCenterY - 10f;
            var lanternBox = new Vector2[]
            {
                new(lanternX - 8f, lanternY - 10f),
                new(lanternX + 8f, lanternY - 10f),
                new(lanternX + 8f, lanternY + 10f),
                new(lanternX - 8f, lanternY + 10f),
            };
            InkStyle.PenStroke(this, lanternBox, new Color(0.96f, 0.72f, 0.30f), InkStyle.InkColor, 1.5f);
            DrawLine(new Vector2(lanternX - 8f, lanternY), new Vector2(lanternX + 8f, lanternY), InkStyle.InkColor, 1f, true);

            // —— 枯树（左侧前景，视差最强）——
            float treeX = W * 0.08f + (_mouse.X - W * 0.5f) * -0.02f;
            float treeY = centerY + 20f;
            DrawDeadTree(treeX, treeY, 5.5f);
            // 枯枝上的冻霜（白色细线，覆盖在枯枝上）
            DrawFrostOnTree(treeX, treeY);

            // —— 石碑剪影（枯树旁，前景最强）——
            float stX = W * 0.22f + (_mouse.X - W * 0.5f) * -0.015f;
            float stY = centerY + 40f;
            var stele = new Vector2[]
            {
                new(stX - 18f, stY),
                new(stX - 18f, stY - 110f),
                new(stX - 14f, stY - 122f),
                new(stX, stY - 128f),
                new(stX + 14f, stY - 122f),
                new(stX + 18f, stY - 110f),
                new(stX + 18f, stY),
            };
            InkStyle.PenStroke(this, stele, new Color(0.30f, 0.24f, 0.18f), InkStyle.InkColor, 3f);
            InkStyle.KawaiiLineHighlight(this,
                new Vector2(stX - 14f, stY - 122f),
                new Vector2(stX + 14f, stY - 122f),
                new Color(0.88f, 0.76f, 0.52f), 2.6f);

            // —— 乌鸦（3 只，振翅 + 浮动）——
            DrawCrow(W * 0.60f + Mathf.Sin(_t * 0.4f) * 6f, 130f + Mathf.Sin(_t * 1.3f) * 8f, _t * 4f);
            DrawCrow(W * 0.72f + Mathf.Sin(_t * 0.5f + 1f) * 5f, 100f + Mathf.Sin(_t * 1.1f + 2f) * 6f, _t * 3f + 1f);
            DrawCrow(W * 0.28f + Mathf.Sin(_t * 0.3f + 2f) * 8f, 160f + Mathf.Sin(_t * 1.4f + 1f) * 10f, _t * 5f + 2f);

            // —— 前景地面（墨色，比墙更暗）——
            var ground = new Vector2[]
            {
                new(0, H), new(0, centerY + 40f),
                new(W * 0.15f, centerY + 50f),
                new(W * 0.30f, centerY + 44f),
                new(W * 0.55f, centerY + 52f),
                new(W * 0.80f, centerY + 46f),
                new(W, centerY + 42f), new(W, H),
            };
            InkStyle.PenStroke(this, ground, new Color(0.18f, 0.14f, 0.10f), InkStyle.InkColor, 3f);

            // 地面点缀（石子 + 断枝 + 小草 + 积雪小片）
            for (int i = 0; i < 9; i++)
            {
                float rx = W * 0.05f + i * W * 0.105f;
                float ry = centerY + 48f + ((i * 37) % 12);
                InkStyle.PenStroke(this, new Vector2[]
                {
                    new(rx - 4f, ry), new(rx - 2f, ry - 3f),
                    new(rx + 3f, ry - 2f), new(rx + 4f, ry + 1f),
                    new(rx, ry + 2f),
                }, new Color(0.34f, 0.26f, 0.18f), InkStyle.InkColor, 1.6f);
            }

            // —— 飘雪层（前景，最上方）——
            for (int i = 0; i < _snow.Length; i++)
            {
                var fl = _snow[i];
                float sx = fl.X * W;
                float sy = fl.Y * H;
                DrawCircle(new Vector2(sx, sy), fl.Size, new Color(0.95f, 0.96f, 0.98f, 0.85f));
            }
        }

        private void DrawDeadTree(float x, float y, float trunkW)
        {
            // 主干（粗描边，平涂深棕）
            var trunk = new Vector2[]
            {
                new(x - trunkW, y),
                new(x - trunkW * 0.5f, y - 180f),
                new(x + trunkW * 0.5f, y - 220f),
                new(x + trunkW, y),
            };
            InkStyle.PenStroke(this, trunk, new Color(0.22f, 0.16f, 0.10f), InkStyle.InkColor, 3f);
            // 主干顶端积雪
            DrawCircle(new Vector2(x, y - 220f), 4f, new Color(0.95f, 0.96f, 0.98f));

            float[][] branches =
            {
                new[] { y - 40f, x - 70f, y - 90f, 1.4f },
                new[] { y - 70f, x + 60f, y - 130f, 1.6f },
                new[] { y - 100f, x - 90f, y - 140f, 1.2f },
                new[] { y - 120f, x + 80f, y - 170f, 1.4f },
                new[] { y - 150f, x - 40f, y - 200f, 1.0f },
                new[] { y - 160f, x + 100f, y - 210f, 1.2f },
                new[] { y - 190f, x + 30f, y - 250f, 1.0f },
                new[] { y - 200f, x - 50f, y - 240f, 1.2f },
            };
            foreach (var b in branches)
            {
                DrawLine(new Vector2(x, b[0]), new Vector2(b[1], b[2]), InkStyle.InkColor, b[3] * 2f, true);
            }
        }

        /// <summary>枯枝上的冻霜（白细线覆盖在分支上）。</summary>
        private void DrawFrostOnTree(float x, float y)
        {
            float[][] frostPoints =
            {
                new[] { y - 40f, x - 70f, y - 90f, 3f },
                new[] { y - 70f, x + 60f, y - 130f, 4f },
                new[] { y - 120f, x + 80f, y - 170f, 3.5f },
                new[] { y - 160f, x + 100f, y - 210f, 3f },
                new[] { y - 200f, x - 50f, y - 240f, 3.5f },
            };
            foreach (var fp in frostPoints)
            {
                Vector2 from = new Vector2(x, fp[0]);
                Vector2 to = new Vector2(fp[1], fp[2]);
                Vector2 mid = (from + to) * 0.5f;
                // 从主干到分支中点的一段白霜（只画靠近主干的一半，避免糊成一片）
                Vector2 frostEnd = from + (to - from) * 0.45f;
                DrawLine(from, frostEnd, new Color(0.95f, 0.96f, 0.98f, 0.55f), 2.2f, true);
                // 分支中点一个小雪花结
                DrawCircle(mid, 1.2f, new Color(0.95f, 0.96f, 0.98f, 0.8f));
            }
        }

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
    }
}
