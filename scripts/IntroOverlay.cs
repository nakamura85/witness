using System;
using Godot;

namespace Witness;

/// <summary>
/// 幕间黑屏：日期 + 幕间导语（+ 真实历史人物注释行）。
/// 空格/回车推进。每位具名真实人物首次出场时，注释行显示
/// 「真实历史人物 · 详见史料卡 X」（GDD §4.3 呈现伦理铁律）。
/// </summary>
public partial class IntroOverlay : CanvasLayer
{
    private Label _date, _text, _note, _hint;
    private Action _onDone;
    private float _t;
    private bool _armed;

    public IntroOverlay() { Layer = 30; }

    public override void _Ready()
    {
        var bg = new ColorRect { Color = new Color(0.055f, 0.05f, 0.045f, 0.94f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 18);
        center.AddChild(box);

        _date = new Label();
        _date.AddThemeFontSizeOverride("font_size", 18);
        _date.AddThemeColorOverride("font_color", new Color(0.72f, 0.68f, 0.58f));
        _date.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_date);

        _text = new Label();
        _text.AddThemeFontSizeOverride("font_size", 23);
        _text.AddThemeColorOverride("font_color", new Color(0.91f, 0.86f, 0.75f));
        _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _text.CustomMinimumSize = new Vector2(780, 0);
        _text.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_text);

        _note = new Label();
        _note.AddThemeFontSizeOverride("font_size", 14);
        _note.AddThemeColorOverride("font_color", new Color(0.72f, 0.34f, 0.28f));
        _note.HorizontalAlignment = HorizontalAlignment.Center;
        _note.Visible = false;
        box.AddChild(_note);

        _hint = new Label { Text = Loc.I.T("ui_advance_hint") };
        _hint.AddThemeFontSizeOverride("font_size", 14);
        _hint.AddThemeColorOverride("font_color", new Color(0.55f, 0.52f, 0.45f));
        _hint.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_hint);

        Visible = false;
    }

    public void Show(string date, string intro, string note, Action onDone)
    {
        _date.Text = date;
        _text.Text = intro;
        _note.Text = note;
        _note.Visible = note != "";
        _onDone = onDone;
        _t = 0f;
        _armed = false;
        Visible = true;
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _t += (float)delta;
        if (!_armed && _t > 0.35f) _armed = true; // 防止同帧残留按键跳过
        if (_armed && Input.IsActionJustPressed("advance"))
        {
            Visible = false;
            _onDone?.Invoke();
            _onDone = null;
        }
    }
}
