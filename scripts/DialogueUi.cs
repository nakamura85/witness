using System;
using Godot;

namespace Witness;

/// <summary>
/// 底部对话面板：单行文本或 1-2 个选择按钮。
/// 纸色卡片 + 炭墨描边，贴合档案拼贴基调。
/// </summary>
public partial class DialogueUi : CanvasLayer
{
    private Label _name, _text;
    private HBoxContainer _buttons;

    public DialogueUi() { Layer = 20; }

    public override void _Ready()
    {
        var root = new Control();
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(root);

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        panel.Position = new Vector2(-470, -190);
        panel.CustomMinimumSize = new Vector2(940, 0);
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.90f, 0.85f, 0.74f),
            BorderColor = new Color(0.16f, 0.14f, 0.12f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
            ContentMarginLeft = 20, ContentMarginRight = 20, ContentMarginTop = 14, ContentMarginBottom = 16,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        root.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 4);
        margin.AddThemeConstantOverride("margin_right", 4);
        panel.AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        margin.AddChild(box);

        _name = new Label();
        _name.AddThemeFontSizeOverride("font_size", 15);
        _name.AddThemeColorOverride("font_color", new Color(0.55f, 0.20f, 0.16f));
        box.AddChild(_name);

        _text = new Label();
        _text.AddThemeFontSizeOverride("font_size", 19);
        _text.AddThemeColorOverride("font_color", new Color(0.16f, 0.14f, 0.12f));
        _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _text.CustomMinimumSize = new Vector2(880, 0);
        box.AddChild(_text);

        _buttons = new HBoxContainer();
        _buttons.AddThemeConstantOverride("separation", 14);
        _buttons.Alignment = BoxContainer.AlignmentMode.End;
        box.AddChild(_buttons);

        Visible = false;
    }

    public void ShowLine(string name, string text, Action onDone = null)
        => ShowChoice(name, text, new[] { (Loc.I.T("ui_confirm"), true) }, _ => onDone?.Invoke());

    public void ShowTextThen(string name, string text, Action next)
        => ShowLine(name, text, next);

    /// <summary>显示一个带按钮的选择；选完自动收起并回调序号。</summary>
    public void ShowChoice(string name, string text, (string label, bool enabled)[] opts, Action<int> onPick)
    {
        Visible = true;
        _name.Text = name;
        _name.Visible = name != "";
        _text.Text = text;
        foreach (var c in _buttons.GetChildren())
            c.QueueFree();
        for (int i = 0; i < opts.Length; i++)
        {
            int idx = i; // 闭包捕获副本
            var b = new Button { Text = opts[i].label, Disabled = !opts[i].enabled };
            b.AddThemeFontSizeOverride("font_size", 17);
            b.Pressed += () =>
            {
                Visible = false;
                onPick(idx);
            };
            _buttons.AddChild(b);
        }
    }
}
