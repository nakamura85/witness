using Godot;

namespace Witness;

/// <summary>
/// 标题界面：炭笔标题图 + 游戏名 + 「新的见证 / 继续 / 史料馆」+ 9 语言选择器。
/// 语言切换即时生效（重刷本屏文字，后续界面按当前语言构建）。
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
        var bg = new ColorRect { Color = new Color(0.086f, 0.075f, 0.062f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // 炭笔标题图（若美术素材已生成）
        if (ResourceLoader.Exists("res://assets/art/title.jpg"))
        {
            var art = new TextureRect
            {
                Texture = GD.Load<Texture2D>("res://assets/art/title.jpg"),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                Modulate = new Color(1, 1, 1, 0.62f),
            };
            art.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(art);

            var shade = new ColorRect { Color = new Color(0.086f, 0.075f, 0.062f, 0.45f) };
            shade.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(shade);
        }

        // 居中文字栏（v0.2 修正：原 CenterLeft 锚点导致画面偏左上）
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);
        box.CustomMinimumSize = new Vector2(560, 340);
        center.AddChild(box);

        var title = new Label
        {
            Text = Loc.I.T("app_title"),
        };
        title.AddThemeFontSizeOverride("font_size", 76);
        title.AddThemeColorOverride("font_color", new Color(0.91f, 0.86f, 0.75f));
        box.AddChild(title);

        _subtitle = new Label();
        _subtitle.AddThemeFontSizeOverride("font_size", 24);
        _subtitle.AddThemeColorOverride("font_color", new Color(0.75f, 0.70f, 0.60f));
        box.AddChild(_subtitle);

        _prompt = new Label();
        _prompt.AddThemeFontSizeOverride("font_size", 16);
        _prompt.AddThemeColorOverride("font_color", new Color(0.66f, 0.62f, 0.54f));
        _prompt.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_prompt);

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

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
        langLabel.AddThemeColorOverride("font_color", new Color(0.66f, 0.62f, 0.54f));
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

        // 版本脚注（v0.2：底部居中）
        _ver = new Label();
        _ver.AddThemeFontSizeOverride("font_size", 13);
        _ver.AddThemeColorOverride("font_color", new Color(0.5f, 0.47f, 0.4f));
        _ver.SetAnchorsPreset(LayoutPreset.BottomWide);
        _ver.OffsetTop = -34;
        _ver.OffsetBottom = -12;
        _ver.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_ver);

        UpdateTexts();
        Loc.I.LanguageChanged += UpdateTexts;
    }

    private Button MakeMenuButton(VBoxContainer parent)
    {
        var b = new Button();
        b.AddThemeFontSizeOverride("font_size", 20);
        b.CustomMinimumSize = new Vector2(280, 44);
        b.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
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
}
