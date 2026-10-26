using Godot;

namespace Witness;

/// <summary>
/// One job only: prove the background stops moving when the player stops.
///
/// The player gets pinned in place so the camera locks, then two frames are
/// shot a beat apart. Any pixel that changed in the sky / mountain / village
/// band means something is still drifting, which is the "the bushes keep
/// shuffling on their own" complaint.
///
/// Verification tool only, the real game never loads it.
/// </summary>
public partial class ShakeCheck : Node
{
    private JiangqiaoLevel _lvl;
    private float _t;
    private int _shot;
    private bool _started;
    private const string Dir = "C:/Users/ChangFee/WorkBuddy/2026-10-26-09-06-52/witness-repo/_shots";

    public override void _Ready()
    {
        _lvl = new JiangqiaoLevel(new Main());
        _lvl.DebugClock = 11.0f;
        // v2.8: falling snow moves on its own, so it would drown the signal.
        // The check is about the BACKDROP, so kill the snow for this run.
        // Has to be set before the level enters the tree.
        if (System.Environment.GetEnvironmentVariable("WBNOSNOW") == "1")
            _lvl.DebugNoSnow = true;
        // chimney smoke is supposed to move too, so it can be cut as well
        if (System.Environment.GetEnvironmentVariable("WBNOSMOKE") == "1")
            _lvl.DebugNoSmoke = true;
        AddChild(_lvl);
        _started = true;
        GD.Print("SHAKECHECK ready");
    }

    public override void _Process(double delta)
    {
        if (!_started) return;
        _t += (float)delta;

        if (_t > 1.2f && _t < 1.35f) Input.ActionPress("advance");
        else if (_t >= 1.35f) Input.ActionRelease("advance");

        // walk to the village cluster then stop dead
        float px = _lvl?._player?.Position.X ?? 0f;
        if (px < 420f) Input.ActionPress("move_right");
        else Input.ActionRelease("move_right");

        if (_shot == 0 && _t > 9f)
        {
            Save("shakecheck_a.png");
            _shot = 1;
        }
        else if (_shot == 1 && _t > 10.6f)
        {
            Save("shakecheck_b.png");
            _shot = 2;
        }
        else if (_shot == 2 && _t > 11.4f)
        {
            GD.Print("SHAKECHECK done playerX=" + (_lvl?._player?.Position.X ?? -1f));
            GetTree().Quit();
        }
    }

    private async void Save(string name)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var img = GetViewport().GetTexture().GetImage();
        if (!System.IO.Directory.Exists(Dir)) System.IO.Directory.CreateDirectory(Dir);
        img.SavePng(System.IO.Path.Combine(Dir, name));
        var cam = GetViewport().GetCamera2D();
        GD.Print($"SHAKECHECK saved {name} playerX={_lvl?._player?.Position.X:F2} " +
                 $"cam=({cam?.GlobalPosition.X:F2},{cam?.GlobalPosition.Y:F2})");
    }
}
