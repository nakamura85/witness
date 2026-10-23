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
    /// 手绘漫画风标题背景：古城墙 + 枯树 + 乌鸦 + 石碑剪影。
    /// 整体用墨色剪影 + 粗描边 + 卡通高光。
    /// </summary>
    private partial class TitleArt : Control
    {
        private float _t;
        public override void _Ready()
        {
            SetAnchorsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Ignore;
        }
        public override void _Process(double delta)
        {
            _t += (float)delta;
            QueueRedraw();
        }
        public override void _Draw()
        {
            var s = GetViewportRect().Size;
            float W = s.X, H = s.Y;
            var centerY = H * 0.82f;

            // —— 远景淡墨云 ——
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            for (int i = 0; i < 6; i++)
            {
                float cx = 180f + i * 200f + Mathf.Sin(_t * 0.2f + i) * 8f;
                float cy = 120f + (i % 2) * 30f;
                InkStyle.PenStroke(this, new Vector2[]
                {
                    new(cx - 70, cy), new(cx - 40, cy - 14),
                    new(cx + 10, cy - 18), new(cx + 50, cy - 10),
                    new(cx + 80, cy), new(cx + 50, cy + 10),
                    new(cx - 30, cy + 10),
                }, new Color(0.78f, 0.70f, 0.55f, 0.45f), new Color(0.60f, 0.48f, 0.30f, 0.40f), 2f);
            }

            // —— 城墙剪影（中央偏右，类似原仓库 README 里的城门楼）——
            float wallLeft = W * 0.42f;
            float wallRight = W * 0.96f;
            float wallTop = centerY - 200f;
            float wallH = H - centerY + 20f;

            // 墙身（城砖墙）
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
            InkStyle.PenStroke(this, wallBody, new Color(0.30f, 0.22f, 0.15f), InkStyle.InkColor, 3.5f);

            // 城垛（雉堞）
            for (int i = 0; i < 12; i++)
            {
                float bx = wallLeft + 20f + i * ((wallRight - wallLeft) - 40f) / 11f;
                var crenel = new Vector2[]
                {
                    new(bx - 8f, wallTop), new(bx - 8f, wallTop - 18f),
                    new(bx + 6f, wallTop - 18f), new(bx + 6f, wallTop),
                };
                InkStyle.PenStroke(this, crenel, new Color(0.34f, 0.26f, 0.18f), InkStyle.InkColor, 2.5f);
            }

            // 城门洞（涂黑的方拱门）
            float gateX = wallLeft + 140f;
            float gateW = 70f, gateH = 120f;
            var gate = new Vector2[]
            {
                new(gateX, H), new(gateX, H - gateH),
                new(gateX + gateW * 0.5f, H - gateH - 30f),   // 拱形顶
                new(gateX + gateW, H - gateH), new(gateX + gateW, H),
            };
            InkStyle.PenStroke(this, gate, new Color(0.06f, 0.04f, 0.03f), InkStyle.InkColor, 3f);

            // —— 枯树（左侧，枝桠伸到画面上半）——
            DrawDeadTree(W * 0.08f, centerY + 20f, 5.2f);

            // —— 石碑剪影（城墙门前，左下前景）——
            float stX = W * 0.22f;
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
            InkStyle.PenStroke(this, stele, new Color(0.28f, 0.22f, 0.16f), InkStyle.InkColor, 3f);
            // 石碑顶上一道高光
            InkStyle.KawaiiLineHighlight(this,
                new Vector2(stX - 14f, stY - 122f),
                new Vector2(stX + 14f, stY - 122f),
                new Color(0.88f, 0.76f, 0.52f), 2.6f);

            // —— 乌鸦（2-3 只，用时间做轻微振翅）——
            DrawCrow(W * 0.60f, 130f + Mathf.Sin(_t * 1.3f) * 8f, _t * 4f);
            DrawCrow(W * 0.72f, 100f + Mathf.Sin(_t * 1.1f + 2f) * 6f, _t * 3f + 1f);
            DrawCrow(W * 0.28f, 160f + Mathf.Sin(_t * 1.4f + 1f) * 10f, _t * 5f + 2f);

            // —— 前景地面（比城墙更暗的墨色）——
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

            // —— 地面上零星石子 + 断枝（卡通化小点缀）——
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
            InkStyle.PenStroke(this, trunk, new Color(0.20f, 0.14f, 0.08f), InkStyle.InkColor, 3f);

            // 枯枝（8 条，分别从主干不同位置分出，越往上越细）
            float[][] branches =
            {
                // 起点 Y, 终点 x, 终点 y, 宽度
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

        private void DrawCrow(float x, float y, float flapPhase)
        {
            // 翅膀上下摆动的黑色剪影；越远越小
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
