using Godot;

namespace Witness;

/// <summary>
/// Beat3 出城行走段（GDD §5 Beat3 / §10.2）：
/// 无 HUD，只有背影、地平线、风声般的四行独白；
/// 按住空格加速。约 20 秒的无声慢走 —— 用节奏落差让选择变得庄重。
/// </summary>
public partial class WalkUi : CanvasLayer
{
    [Signal] public delegate void FinishedEventHandler();

    private float _t;
    private bool _done;
    private readonly Label[] _lines = new Label[4];
    private static readonly float[] AppearAt = { 0.10f, 0.36f, 0.62f, 0.87f };
    private WalkScene _scene;

    public WalkUi() { Layer = 45; }

    public override void _Ready()
    {
        // 顺序即绘制层级：背景（底）→ 地平线与背影（中）→ 文字（顶）
        var bg = new ColorRect { Color = new Color(0.055f, 0.05f, 0.045f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);

        _scene = new WalkScene();
        AddChild(_scene);

        for (int i = 0; i < 4; i++)
        {
            var l = new Label { Text = Loc.I.T($"walk_line{i + 1}") };
            l.AddThemeFontSizeOverride("font_size", 19);
            l.AddThemeColorOverride("font_color", new Color(0.85f, 0.81f, 0.72f));
            l.Modulate = new Color(1, 1, 1, 0);
            l.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
            l.Position = new Vector2(-380, -250 + i * 46);
            l.CustomMinimumSize = new Vector2(760, 0);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.HorizontalAlignment = HorizontalAlignment.Center;
            AddChild(l);
            _lines[i] = l;
        }
    }

    public override void _Process(double delta)
    {
        float f = (float)delta * (Input.IsActionPressed("advance") ? 3f : 1f);
        _t += f;
        float p = Mathf.Min(_t / 22f, 1f);

        for (int i = 0; i < 4; i++)
        {
            if (p >= AppearAt[i] && _lines[i].Modulate.A < 1f)
                _lines[i].Modulate = new Color(1, 1, 1, Mathf.Min(1f, _lines[i].Modulate.A + f * 1.4f));
        }
        _scene.T = _t;
        _scene.QueueRedraw();

        if (p >= 1f && !_done)
        {
            _done = true;
            EmitSignal(SignalName.Finished);
        }
    }
}

/// <summary>出城段的实际绘制：地平线 + 缓缓西行的背影（v0.5 手绘立绘深色调制，读作剪影）。</summary>
public partial class WalkScene : Node2D
{
    public float T;
    private Texture2D _texVautrin;

    public override void _Draw()
    {
        // 地平线
        DrawLine(new Vector2(0, 340), new Vector2(1280, 340),
            new Color(0.5f, 0.46f, 0.4f, 0.35f), 1.5f);
        // 缓缓西行的背影
        float x = Mathf.Lerp(120f, 1160f, Mathf.Min(T / 22f, 1f));
        _texVautrin ??= ChromaKey.Cutout("res://assets/art/sprites/vautrin.jpg", 0.03f, 0.05f);
        if (_texVautrin != null)
        {
            float h = 140f;
            float w = h * _texVautrin.GetSize().X / _texVautrin.GetSize().Y;
            float bob = Mathf.Abs(Mathf.Sin(T * 5.2f)) * 3f;
            DrawTextureRect(_texVautrin, new Rect2(x - w / 2, 340 - h - bob, w, h), false,
                new Color(0.16f, 0.14f, 0.12f, 0.92f));
        }
    }
}
