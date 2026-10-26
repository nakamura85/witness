using Godot;
using System.Collections.Generic;

namespace Witness;

/// <summary>
/// One job only: show the dropped rifles on their own, so the snow cover
/// can be judged without the level's sandbags, houses and bridge in the way.
///
/// v2.8. Every attempt to photograph a rifle inside the level ran into
/// the scenery - a sandbag wall, a bridge pier, the burnt house - always
/// between the camera and the gun. So this builds its OWN little scene:
/// a flat patch of snow, five rifles laid out in a row, and a camera
/// looking straight down at them.
///
/// Verification tool only, the real game never loads it.
/// </summary>
public partial class GunCheck : Node2D
{
    private float _t;
    private int _shot;
    private bool _started;
    private Camera2D _cam;
    private const string Dir = "C:/Users/ChangFee/WorkBuddy/2026-10-26-09-06-52/witness-repo/_shots";

    // five different seeds, so the three burial depths all show up
    private static readonly float[] Seeds = { 0.13f, 0.41f, 0.62f, 0.78f, 0.94f };

    public override void _Ready()
    {
        // the rifles, spread out so each one can be looked at on its own.
        // the node origin sits on the snow line, same as in the level.
        for (int i = 0; i < Seeds.Length; i++)
        {
            var g = new WeaponPickup(WeaponPickup.Kind.Usable, 4)
            {
                Position = new Vector2(-320f + i * 160f, 0f),
            };
            g.SetSeed(Seeds[i]);
            AddChild(g);
        }

        _cam = new Camera2D
        {
            Enabled = true,
            Position = new Vector2(0, 0),
            Zoom = new Vector2(2.6f, 2.6f),
        };
        AddChild(_cam);
        _cam.MakeCurrent();

        _started = true;
        GD.Print("GUNCHECK ready");
    }

    /// <summary>Flat snow ground, so the rifles are not floating in the void.</summary>
    public override void _Draw()
    {
        DrawLine(new Vector2(-900, 4), new Vector2(900, 4),
            new Color(0.855f, 0.875f, 0.875f), 400f);
    }

    public override void _Process(double delta)
    {
        if (!_started) return;
        _t += (float)delta;

        // one shot per rifle, then a wide shot of all five together
        if (_shot < Seeds.Length && _t > 0.6f + _shot * 0.5f)
        {
            _cam.Position = new Vector2(-320f + _shot * 160f, 0f);
            _cam.Zoom = new Vector2(4.5f, 4.5f);
            Save($"guncheck_{_shot}.png");
            _shot++;
        }
        else if (_shot == Seeds.Length && _t > 0.6f + Seeds.Length * 0.5f)
        {
            _cam.Position = new Vector2(0, 0f);
            _cam.Zoom = new Vector2(2.2f, 2.2f);
            Save("guncheck_all.png");
            _shot++;
        }
        else if (_shot > Seeds.Length && _t > 0.6f + Seeds.Length * 0.5f + 0.6f)
        {
            GD.Print("GUNCHECK done");
            GetTree().Quit();
        }
    }

    private async void Save(string name)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = GetViewport().GetTexture().GetImage();
        if (!System.IO.Directory.Exists(Dir)) System.IO.Directory.CreateDirectory(Dir);
        img.SavePng(System.IO.Path.Combine(Dir, name));
        GD.Print($"GUNCHECK saved {name}");
    }
}
