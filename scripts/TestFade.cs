using Godot;

namespace Witness;

/// <summary>Temp test scene: drive Main through swaps and log fade alpha to find stuck overlay.</summary>
public partial class TestFade : Node
{
    private Main _main;
    private float _t;
    private int _stage;

    public override void _Ready()
    {
        _main = new Main();
        AddChild(_main);
        GetWindow().MoveToForeground();
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        _stage++;
        ColorRect fade = null;
        foreach (var c in _main.GetChildren())
            if (c is ColorRect cr) { fade = cr; break; }
        if (_stage == 30) _main.ShowTitle();
        if (_stage == 90) _main.StartNewGame();
        if (_stage % 30 == 0)
            GD.Print($"T+{_t:F1}s children={_main.GetChildCount()} fade={fade.Color} paused={GetTree().Paused} tscale={Engine.TimeScale}");
        if (_t > 20f) GetTree().Quit();
    }
}
