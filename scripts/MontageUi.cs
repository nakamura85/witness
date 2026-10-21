using Godot;

namespace Witness;

/// <summary>
/// Beat4 蒙太奇（GDD §5 Beat4）：1938 → 1948 的四段黑屏文字——
/// 胶片在美国放映 → 东京审判 → 南京审判 → 战犯的结局。空格或 5 秒自动推进。
/// </summary>
public partial class MontageUi : CanvasLayer
{
    [Signal] public delegate void FinishedEventHandler();

    private readonly string[] _keys;
    private int _idx = -1;
    private float _t;
    private bool _done;
    private Label _text;

    public MontageUi(string[] keys)
    {
        _keys = keys;
        Layer = 46;
    }

    public override void _Ready()
    {
        var bg = new ColorRect { Color = new Color(0.055f, 0.05f, 0.045f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);

        _text = new Label();
        _text.AddThemeFontSizeOverride("font_size", 24);
        _text.AddThemeColorOverride("font_color", new Color(0.91f, 0.86f, 0.75f));
        _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _text.CustomMinimumSize = new Vector2(820, 0);
        _text.HorizontalAlignment = HorizontalAlignment.Center;
        center.AddChild(_text);

        Advance();
    }

    public override void _Process(double delta)
    {
        if (_done) return;
        _t += (float)delta;
        if (_t > 0.45f && (Input.IsActionJustPressed("advance") || _t > 5.2f))
            Advance();
    }

    private void Advance()
    {
        _t = 0f;
        _idx++;
        if (_idx >= _keys.Length)
        {
            _done = true;
            EmitSignal(SignalName.Finished);
            return;
        }
        _text.Text = Loc.I.T(_keys[_idx]);
        _text.Modulate = new Color(1, 1, 1, 0);
        var tween = CreateTween();
        tween.TweenProperty(_text, "modulate:a", 1.0f, 0.8f);
    }
}
