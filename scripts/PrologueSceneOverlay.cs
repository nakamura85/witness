using System;
using Godot;

namespace Witness;

/// <summary>
/// 历史场景显现：史料放回时间线后，炭笔场景渐显 + 旁白逐行推进（Viewfinder 时刻）。
/// 空格/回车推进；全部播完回调。图 + 文都走本地化词条与 assets/art。
/// </summary>
public partial class PrologueSceneOverlay : CanvasLayer
{
    private readonly string _artPath;
    private readonly string[] _lineKeys;
    private readonly Action _onDone;

    private Label _date, _text, _hint;
    private TextureRect _art;
    private int _idx = -1;
    private float _t;
    private bool _armed;

    public PrologueSceneOverlay(string artPath, string dateKey, string[] lineKeys, Action onDone)
    {
        _artPath = artPath;
        _lineKeys = lineKeys;
        _onDone = onDone;
        DateKey = dateKey;
        Layer = 44;
    }

    private string DateKey { get; }

    public override void _Ready()
    {
        var bg = new ColorRect { Color = new Color(0.055f, 0.05f, 0.045f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);
        center.AddChild(box);

        // 炭笔历史场景（素材缺失时留空 —— 文字本身即场景）
        if (ResourceLoader.Exists(_artPath))
        {
            _art = new TextureRect
            {
                Texture = GD.Load<Texture2D>(_artPath),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(640, 300),
                Modulate = new Color(1, 1, 1, 0),
            };
            box.AddChild(_art);
        }

        _date = new Label { Text = Loc.I.T(DateKey) };
        _date.AddThemeFontSizeOverride("font_size", 18);
        _date.AddThemeColorOverride("font_color", new Color(0.72f, 0.68f, 0.58f));
        _date.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_date);

        _text = new Label();
        _text.AddThemeFontSizeOverride("font_size", 22);
        _text.AddThemeColorOverride("font_color", new Color(0.91f, 0.86f, 0.75f));
        _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _text.CustomMinimumSize = new Vector2(820, 0);
        _text.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_text);

        _hint = new Label { Text = Loc.I.T("ui_advance_hint") };
        _hint.AddThemeFontSizeOverride("font_size", 14);
        _hint.AddThemeColorOverride("font_color", new Color(0.55f, 0.52f, 0.45f));
        _hint.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_hint);

        Advance();
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (!_armed && _t > 0.45f) _armed = true;
        if (_armed && Input.IsActionJustPressed("advance")) Advance();
    }

    private void Advance()
    {
        _t = 0f;
        _idx++;
        if (_idx >= _lineKeys.Length)
        {
            _onDone?.Invoke();
            QueueFree();
            return;
        }
        _text.Text = Loc.I.T(_lineKeys[_idx]);
        _text.Modulate = new Color(1, 1, 1, 0);
        var tween = CreateTween();
        tween.TweenProperty(_text, "modulate:a", 1.0f, 0.7f);
        if (_art != null && _art.Modulate.A < 1f)
        {
            var t2 = CreateTween();
            t2.TweenProperty(_art, "modulate:a", 1.0f, 1.2f);
        }
    }
}
