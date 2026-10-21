using Godot;

namespace Witness;

/// <summary>
/// 史料卡浮层（GDD §9.3）：档案编号 + 配图 + 引文（保留原语言）+ 出处 + 一句话背景。
/// 卡 10「幸存者口述」按玩家表现引用不同变体文本（GDD §8）。
/// </summary>
public partial class CardOverlayUi : CanvasLayer
{
    private readonly int _num;

    public CardOverlayUi(int num)
    {
        _num = num;
        Layer = 50;
    }

    public override void _Ready()
    {
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.72f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.91f, 0.87f, 0.77f),
            BorderColor = new Color(0.16f, 0.14f, 0.12f),
            BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
            ContentMarginLeft = 26, ContentMarginRight = 26, ContentMarginTop = 22, ContentMarginBottom = 22,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        panel.CustomMinimumSize = new Vector2(960, 520);
        center.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        panel.AddChild(box);

        var numLabel = new Label { Text = Loc.I.Tf("gal_card_num", _num) };
        numLabel.AddThemeFontSizeOverride("font_size", 14);
        numLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.46f, 0.4f));
        box.AddChild(numLabel);

        var name = new Label { Text = Loc.I.T($"card{_num}_name") };
        name.AddThemeFontSizeOverride("font_size", 28);
        name.AddThemeColorOverride("font_color", new Color(0.16f, 0.14f, 0.12f));
        box.AddChild(name);

        var rule = new ColorRect
        {
            Color = new Color(0.55f, 0.20f, 0.16f, 0.7f),
            CustomMinimumSize = new Vector2(0, 2),
        };
        box.AddChild(rule);

        // 配图（炭笔卡片插画；素材缺失时留空 —— 纸面本身就是版面）
        if (ResourceLoader.Exists($"res://assets/art/card_{_num:00}.jpg"))
        {
            var art = new TextureRect
            {
                Texture = GD.Load<Texture2D>($"res://assets/art/card_{_num:00}.jpg"),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(0, 150),
            };
            box.AddChild(art);
        }

        // 引文（卡 10 按表现取变体）
        string quoteKey = _num == 10
            ? $"card10_quote_{GameState.I.S.OralVariant()}"
            : $"card{_num}_quote";
        var quote = new Label { Text = Loc.I.T(quoteKey) };
        quote.AddThemeFontSizeOverride("font_size", 19);
        quote.AddThemeColorOverride("font_color", new Color(0.28f, 0.25f, 0.21f));
        quote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        quote.CustomMinimumSize = new Vector2(900, 0);
        box.AddChild(quote);

        var source = new Label
        {
            Text = Loc.I.T("card_source_prefix") + " " + Loc.I.T($"card{_num}_source"),
        };
        source.AddThemeFontSizeOverride("font_size", 14);
        source.AddThemeColorOverride("font_color", new Color(0.5f, 0.46f, 0.4f));
        source.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(source);

        var note = new Label
        {
            Text = Loc.I.T("card_note_prefix") + " " + Loc.I.T($"card{_num}_note"),
        };
        note.AddThemeFontSizeOverride("font_size", 14);
        note.AddThemeColorOverride("font_color", new Color(0.5f, 0.46f, 0.4f));
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(note);

        var close = new Button { Text = Loc.I.T("ui_close") };
        close.AddThemeFontSizeOverride("font_size", 16);
        close.CustomMinimumSize = new Vector2(160, 38);
        close.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        close.Pressed += QueueFree;
        box.AddChild(close);
    }
}
