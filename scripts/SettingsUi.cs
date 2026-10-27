using System;
using Godot;
using Witness;

/// <summary>
/// Settings overlay from the title screen: language + Master/Music/SFX
/// volume sliders. Volumes apply live and persist into
/// user://settings.json when the panel closes.
/// </summary>
public partial class SettingsUi : CanvasLayer
{
    public event Action Closed;

    private Label _title, _hint;
    private Label _langLabel;
    private readonly Label[] _sliderLabels = new Label[3];
    private OptionButton _lang;
    private Button _back;

    public SettingsUi() => Layer = 30;

    public override void _Ready()
    {
        var dim = new ColorRect { Color = new Color(0.04f, 0.035f, 0.03f, 0.72f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(560, 0) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.905f, 0.855f, 0.735f),
            BorderColor = new Color(0.24f, 0.20f, 0.16f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            ContentMarginLeft = 34, ContentMarginRight = 34, ContentMarginTop = 26, ContentMarginBottom = 24,
            ShadowColor = new Color(0, 0, 0, 0.45f), ShadowSize = 14, ShadowOffset = new Vector2(6, 8),
        });
        center.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 16);
        panel.AddChild(box);

        _title = new Label();
        _title.AddThemeFontSizeOverride("font_size", 26);
        _title.AddThemeColorOverride("font_color", new Color(0.16f, 0.14f, 0.12f));
        box.AddChild(_title);

        box.AddChild(new ColorRect
        {
            Color = new Color(0.55f, 0.20f, 0.16f, 0.75f),
            CustomMinimumSize = new Vector2(0, 2),
        });

        // language row
        var langRow = new HBoxContainer();
        langRow.AddThemeConstantOverride("separation", 14);
        _langLabel = new Label { CustomMinimumSize = new Vector2(150, 0) };
        _langLabel.AddThemeFontSizeOverride("font_size", 17);
        _langLabel.AddThemeColorOverride("font_color", new Color(0.22f, 0.19f, 0.16f));
        langRow.AddChild(_langLabel);

        _lang = new OptionButton { CustomMinimumSize = new Vector2(240, 38) };
        _lang.AddThemeFontSizeOverride("font_size", 16);
        foreach (var (_, name) in LocCore.Languages)
            _lang.AddItem(name);
        _lang.ItemSelected += idx =>
        {
            Loc.I.SetLang(LocCore.Languages[(int)idx].Code);
            RefreshTexts();
        };
        langRow.AddChild(_lang);
        box.AddChild(langRow);

        // volume rows, live-applied to the audio buses
        box.AddChild(MakeSlider(0, Loc.I.T("set_master"), Settings.Master,
            v => { Settings.Master = v; Settings.ApplyAudio(); }));
        box.AddChild(MakeSlider(1, Loc.I.T("set_music"), Settings.Music,
            v => { Settings.Music = v; Settings.ApplyAudio(); }));
        box.AddChild(MakeSlider(2, Loc.I.T("set_sfx"), Settings.Sfx,
            v => { Settings.Sfx = v; Settings.ApplyAudio(); }));

        _hint = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(470, 0) };
        _hint.AddThemeFontSizeOverride("font_size", 13);
        _hint.AddThemeColorOverride("font_color", new Color(0.45f, 0.40f, 0.33f));
        box.AddChild(_hint);

        _back = new Button { CustomMinimumSize = new Vector2(170, 38) };
        _back.AddThemeFontSizeOverride("font_size", 16);
        _back.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        _back.Pressed += Close;
        box.AddChild(_back);

        _lang.Select(System.Array.FindIndex(LocCore.Languages, l => l.Code == Loc.I.Lang));
        RefreshTexts();
    }

    private HBoxContainer MakeSlider(int i, string label, float value, Action<float> onChanged)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);

        _sliderLabels[i] = new Label { Text = label, CustomMinimumSize = new Vector2(150, 0) };
        _sliderLabels[i].AddThemeFontSizeOverride("font_size", 17);
        _sliderLabels[i].AddThemeColorOverride("font_color", new Color(0.22f, 0.19f, 0.16f));
        row.AddChild(_sliderLabels[i]);

        var slider = new HSlider
        {
            MinValue = 0, MaxValue = 100, Step = 1,
            Value = Mathf.Round(value * 100f),
            CustomMinimumSize = new Vector2(240, 30),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        var pct = new Label { Text = $"{(int)slider.Value}%", CustomMinimumSize = new Vector2(52, 0) };
        pct.AddThemeFontSizeOverride("font_size", 15);
        pct.AddThemeColorOverride("font_color", new Color(0.32f, 0.28f, 0.24f));
        slider.ValueChanged += v =>
        {
            pct.Text = $"{(int)v}%";
            onChanged((float)v / 100f);
        };
        row.AddChild(slider);
        row.AddChild(pct);
        return row;
    }

    private void RefreshTexts()
    {
        _title.Text = Loc.I.T("ui_settings");
        _langLabel.Text = Loc.I.T("ui_language");
        _sliderLabels[0].Text = Loc.I.T("set_master");
        _sliderLabels[1].Text = Loc.I.T("set_music");
        _sliderLabels[2].Text = Loc.I.T("set_sfx");
        _hint.Text = Loc.I.T("set_hint");
        _back.Text = Loc.I.T("ui_close");
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("pause"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Close()
    {
        Settings.Save();
        Closed?.Invoke();
        QueueFree();
    }
}
