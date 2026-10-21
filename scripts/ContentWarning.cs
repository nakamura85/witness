using Godot;

namespace Witness;

/// <summary>
/// 整屏内容警告（硬红线，GDD §6.5 / 企划书 §9：每关开场强制显示，
/// 列出敏感内容类型；全部文字走本地化词条）。
/// </summary>
public partial class ContentWarning : Control
{
    private readonly Main _main;
    private Label _title, _body, _note;
    private Button _go;

    public ContentWarning(Main main) { _main = main; }

    public override void _Ready()
    {
        // 全屏深色底
        var bg = new ColorRect { Color = new Color(0.086f, 0.075f, 0.062f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 22);
        center.AddChild(box);

        _title = MakeLabel(36, new Color(0.91f, 0.86f, 0.75f));
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_title);

        var rule = new ColorRect { Color = new Color(0.55f, 0.20f, 0.16f, 0.7f), CustomMinimumSize = new Vector2(120, 2) };
        var ruleWrap = new CenterContainer();
        ruleWrap.AddChild(rule);
        box.AddChild(ruleWrap);

        _body = MakeLabel(19, new Color(0.85f, 0.81f, 0.72f));
        _body.CustomMinimumSize = new Vector2(760, 0);
        _body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_body);

        _note = MakeLabel(14, new Color(0.66f, 0.62f, 0.54f));
        _note.CustomMinimumSize = new Vector2(760, 0);
        _note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_note);

        _go = new Button();
        _go.AddThemeFontSizeOverride("font_size", 19);
        _go.CustomMinimumSize = new Vector2(260, 46);
        _go.Pressed += () => _main.ShowTitle();
        var goWrap = new CenterContainer();
        goWrap.AddChild(_go);
        box.AddChild(goWrap);

        UpdateTexts();
        Loc.I.LanguageChanged += UpdateTexts;
    }

    private static Label MakeLabel(int size, Color color)
    {
        var l = new Label();
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    private void UpdateTexts()
    {
        _title.Text = Loc.I.T("cw_title");
        _body.Text = Loc.I.T("cw_body");
        _note.Text = Loc.I.T("cw_note");
        _go.Text = Loc.I.T("ui_proceed");
    }
}
