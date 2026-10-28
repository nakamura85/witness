using Godot;

namespace Witness;

/// <summary>
/// Auto-shot driver for the title screen + East Asia map flow.
/// Refreshes the v00-v04 gallery shots that the level-only Capture.cs
/// cannot reach. Run windowed via scenes/CaptureMap.tscn, quits by itself.
/// Note-read flags are snapshotted first and restored on the way out, so the
/// player's real save is not touched.
/// </summary>
public partial class CaptureMap : Node
{
    private SubViewport _sub;
    private Control _screen;
    private float _t;
    private int _stage;
    private bool _started;
    private bool _ko0, _tw0;

    private static string Dir
        => ProjectSettings.GlobalizePath("res://_shots");

    public override void _Ready()
    {
        GD.Print("CAPTUREMAP _Ready");
        _sub = new SubViewport();
        _sub.Size = new Vector2I(1280, 720);
        _sub.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        AddChild(_sub);

        _ko0 = GameState.I.S.MapNoteKorea;
        _tw0 = GameState.I.S.MapNoteTaiwan;

        SwapTo(new TitleScreen(new Main()));
        _started = true;
        _t = 0f;
        _stage = 0;
    }

    private void SwapTo(Control next)
    {
        if (_screen != null) _screen.QueueFree();
        _screen = next;
        _sub.AddChild(next);
    }

    private Button FindButton(Node root, string prefix)
    {
        if (root is Button b && b.Text != null && b.Text.StartsWith(prefix)) return b;
        foreach (var c in root.GetChildren())
        {
            var r = FindButton(c, prefix);
            if (r != null) return r;
        }
        return null;
    }

    public override void _Process(double delta)
    {
        if (!_started) return;
        _t += (float)delta;

        // shots and scene-changing actions live in separate stages: SaveShot
        // grabs the texture a frame later, so pressing a note in the same
        // stage would leak into the "clean map" frame (it did - first run).
        // v3.13: the title shot had the same bug - the swap to AsiaMap ran in
        // the very same stage, so the "title" picture was really the map and
        // v00_title.png was a byte-for-byte copy of v01_map.png. The swap now
        // waits for its own stage.
        if (_stage == 0 && _t > 1.6f)
        {
            SaveShot("map_00_title.png");
            _stage = 10;
        }
        else if (_stage == 10 && _t > 2.6f)
        {
            SwapTo(new AsiaMap(new Main()));
            _stage = 1;
        }
        else if (_stage == 1 && _t > 3.2f)
        {
            SaveShot("map_01_map.png");
            _stage = 2;
        }
        else if (_stage == 2 && _t > 3.7f)
        {
            FindButton(_sub, "纸条 01")?.EmitSignal(BaseButton.SignalName.Pressed);
            _stage = 3;
        }
        else if (_stage == 3 && _t > 4.7f)
        {
            SaveShot("map_02_note_korea.png");
            _stage = 4;
        }
        else if (_stage == 4 && _t > 5.2f)
        {
            FindButton(_sub, "收起纸条")?.EmitSignal(BaseButton.SignalName.Pressed);
            _stage = 5;
        }
        else if (_stage == 5 && _t > 6.2f)
        {
            FindButton(_sub, "纸条 02")?.EmitSignal(BaseButton.SignalName.Pressed);
            _stage = 6;
        }
        else if (_stage == 6 && _t > 7.2f)
        {
            SaveShot("map_03_note_taiwan.png");
            _stage = 7;
        }
        else if (_stage == 7 && _t > 7.7f)
        {
            FindButton(_sub, "收起纸条")?.EmitSignal(BaseButton.SignalName.Pressed);
            _stage = 8;
        }
        else if (_stage == 8 && _t > 10.2f)
        {
            // both notes read: route arrows grow over 2.4s, so wait well past
            // that before the last frame
            SaveShot("map_04_arrows.png");
            _stage = 9;
        }
        else if (_stage == 9 && _t > 10.7f)
        {
            // restore the player's note-read state so the save stays clean
            GameState.I.S.MapNoteKorea = _ko0;
            GameState.I.S.MapNoteTaiwan = _tw0;
            GameState.I.Save();
            GD.Print("CAPTUREMAP done");
            GetTree().Quit();
        }
    }

    private async void SaveShot(string name)
    {
        Image img = null;
        for (int attempt = 0; attempt < 10 && img == null; attempt++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var tex = _sub.GetTexture();
            if (tex != null) img = tex.GetImage();
        }
        if (img == null)
        {
            GD.PrintErr($"CAPTUREMAP skip {name}");
            return;
        }
        if (!System.IO.Directory.Exists(Dir)) System.IO.Directory.CreateDirectory(Dir);
        string path = System.IO.Path.Combine(Dir, name);
        img.SavePng(path);
        GD.Print($"CAPTUREMAP saved {path} ({img.GetWidth()}x{img.GetHeight()})");
    }
}
