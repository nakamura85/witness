using Godot;

namespace Witness;

/// <summary>
/// 仅用于自动化截图：直接加载江桥关卡，绕过 Main 的内容警告/标题 UI 流。
/// 江桥关卡运行期不依赖 _main（只在关卡结束的 UI 回调才用），故传一个未入树的占位 Main 即可。
/// 由 scenes/Capture.tscn 单独运行（窗口模式，脚本在约 3.3s 后自动退出）。
/// </summary>
public partial class Capture : Node
{
    private JiangqiaoLevel _lvl;
    private SubViewport _sub;
    private float _t;
    private float _frozeAt;
    private bool _jumped;      // v3.46 TEMP: landing-puff shot bookkeeping
    private float _landTimer;
    private int _stage;
    private bool _started;
    // v2.9: this used to be a hardcoded absolute path that included the
    // original author's home folder, so a fresh clone wrote to a directory
    // that does not exist on anyone else's machine. res:// resolves against
    // the project folder wherever the project is checked out.
    private static string Dir
        => ProjectSettings.GlobalizePath("res://_shots");

    public override void _Ready()
    {
        GD.Print("CAPTURE _Ready: building JiangqiaoLevel");
        // render into an explicit SubViewport: the headless dummy renderer does
        // not back the main window viewport with a readable texture, so we read
        // the SubViewport's render target instead (it is allocated reliably).
        _sub = new SubViewport();
        _sub.Size = new Vector2I(1280, 720);
        _sub.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        AddChild(_sub);
        _lvl = new JiangqiaoLevel(new Main());
        // gallery shots in full winter daylight (11:00); the day/night cycle
        // otherwise starts pre-dawn and every shot would be dark blue
        _lvl.DebugClock = 11.0f;
        _sub.AddChild(_lvl);
        _started = true;
        _t = 0f;
        _stage = 0;
        Input.ActionPress("move_right");
        GD.Print("CAPTURE _Ready done; player present=" + (_lvl._player != null));
    }

    public override void _Process(double delta)
    {
        if (!_started) return;
        _t += (float)delta;

        // 开火截图窗口内把枪口火光钉在满值并冻结衰减
        //（窗口模式帧率低时 9/s 衰减一帧就把火光清零，截不到）
        if (_stage >= 4 && _stage <= 6 && _lvl?._player?.Figure != null)
        {
            _lvl._player.Figure.FreezeFx = _stage == 5;
            if (_stage == 5) _lvl._player.Figure.MuzzleFlash = 1f;
        }

        if (_stage == 0 && _t > 1.6f)
        {
            Input.ActionRelease("move_right");
            GD.Print("CAPTURE stage0 -> shot scrolling");
            SaveShot("capture_01_intro.png");
            _stage = 1;
        }
        else if (_stage == 1 && _t > 1.9f)
        {
            // 主动按一次空格推进幕间导语，保证后续截图一定是实机关卡画面
            Input.ActionPress("advance");
            _stage = 2;
        }
        else if (_stage == 2 && _t > 2.1f)
        {
            Input.ActionRelease("advance");
            GD.Print("CAPTURE stage2 -> intro dismissed");
            _stage = 3;
        }
        else if (_stage == 3 && _t > 2.7f)
        {
            GD.Print("CAPTURE stage3 -> shot scene");
            SaveShot("capture_02_scene.png");
            _stage = 4;
        }
        else if (_stage == 4 && _t > 3.04f)
        {
            _lvl?._player?.ForceFire(1f);
            GD.Print("CAPTURE stage4 -> fired");
            _stage = 5;
        }
        else if (_stage == 5 && _t > 3.06f)
        {
            GD.Print("CAPTURE stage5 -> shot fire");
            SaveShot("capture_03_fire.png");
            _stage = 6;
        }
        else if (_stage == 6 && _t > 3.5f)
        {
            Input.ActionPress("crouch");
            _stage = 7;
        }
        else if (_stage == 7 && _t > 3.9f)
        {
            GD.Print("CAPTURE stage7 -> shot crouch");
            SaveShot("capture_04_crouch.png");
            _stage = 8;
        }
        else if (_stage == 8 && _t > 4.1f)
        {
            Input.ActionRelease("crouch");
            Input.ActionPress("prone");
            GD.Print("CAPTURE stage8 -> prone pressed");
            _stage = 9;
        }
        else if (_stage == 9 && _t > 4.5f)
        {
            GD.Print("CAPTURE stage9 -> shot prone");
            SaveShot("capture_05_prone.png");
            _stage = 10;
        }
        else if (_stage == 10 && _t > 5.0f)
        {
            Input.ActionRelease("prone");
            Input.ActionPress("move_right");
            GD.Print("CAPTURE stage10 -> running");
            _stage = 11;
        }
        else if (_stage == 11 && _t > 5.8f)
        {
            GD.Print("CAPTURE stage11 -> shot walk");
            SaveShot("capture_06_walk.png");
            // v3.46 TEMP: one jump to exercise the landing snow puff - the
            // shot fires ~0.25s after touchdown, right at puff peak.
            Input.ActionPress("advance");
            _stage = 30;
        }
        else if (_stage == 30)
        {
            Input.ActionRelease("advance");
            if (_t > 6.05f && !_lvl._player.IsOnFloor()) _jumped = true;
            if (_jumped && _lvl._player.IsOnFloor())
            {
                _landTimer += 1f / 60f;
                if (_landTimer > 0.05f)
                {
                    GD.Print("CAPTURE stage30 -> shot landing puff");
                    SaveShot("capture_06b_landing.png");
                    _stage = 12;
                }
            }
        }
        else if (_stage == 12 && (PlayerX() > 1020f || _t > 11.5f))
        {
            GD.Print("CAPTURE stage12 -> shot corpse x="+PlayerX());
            SaveShot("capture_07_corpse.png");
            _stage = 13;
        }
        else if (_stage == 13 && (PlayerX() > 1400f || _t > 14.5f))
        {
            GD.Print("CAPTURE stage13 -> shot enemy x="+PlayerX());
            SaveShot("capture_08_enemy.png");
            _stage = 14;
        }
        else if (_stage == 14 && (PlayerX() > 1780f || _t > 17.5f))
        {
            GD.Print("CAPTURE stage14 -> shot sandbags x="+PlayerX());
            SaveShot("capture_09_sandbags.png");
            _stage = 15;
        }
        else if (_stage == 15 && (PlayerX() > 1990f || _t > 20.5f))
        {
            GD.Print("CAPTURE stage15 -> shot intact chimney x="+PlayerX());
            SaveShot("capture_10_chimney.png");
            _stage = 16;
        }
        else if (_stage == 16 && _t > 21.1f)
        {
            // second frame of the same spot, one beat later, to prove the smoke
            // actually moves instead of sitting there as a painted cloud
            GD.Print("CAPTURE stage16 -> shot intact chimney again");
            SaveShot("capture_10b_chimney_moved.png");
            _stage = 17;
        }
        else if (_stage == 17 && (PlayerX() > 2400f || _t > 24f))
        {
            GD.Print("CAPTURE stage17 -> shot burned house x="+PlayerX());
            SaveShot("capture_10c_burnedhouse.png");
            // v3.44: freeze at the burnt house h2 and let the camera glide
            // back before saving. The shot used to fire the same frame while
            // the camera was still ~1000px ahead (it had been following the
            // runner toward the bridge), so the h2 doorway landed off screen
            // left and the frame showed the far-row shell f3 instead, which
            // has no door by design (enterable=false).
            Input.ActionRelease("move_right");
            _lvl._player.Position = new Vector2(2760f, JiangqiaoLevel.GroundY - 2f);
            _stage = 21;
        }
        else if (_stage == 21 && _t > 27f)
        {
            // camera smoothing is k=1-exp(-6.5*dt): after ~2s the residual
            // gap is well under 1px, so the h2 doorway is centred here.
            foreach (var hh in _lvl._houses)
            {
                if (hh.Id != "h2") continue;
                var ct = _lvl.GetCanvasTransform();
                Vector2 doorTop = ct * new Vector2(hh.DoorX, JiangqiaoLevel.GroundY - 100f);
                GD.Print($"CAPTURE stage21 door h2 world={hh.DoorX:F0} screenTop={doorTop}");
            }
            GD.Print("CAPTURE stage21 -> burnt door frozen shot saved");
            SaveShot("capture_10d_burned_door.png");
            Input.ActionPress("move_right");
            _stage = 18;
        }
        else if (_stage == 18 && PlayerX() > 4290f)
        {
            // v3.25: freeze the runner at a fixed spot so the bridge frame
            // lands the same way every time - the shot used to depend on
            // where the run happened to be when the frame fired, and the
            // span drifted in and out of the picture.
            Input.ActionRelease("move_right");
            _lvl._player.Position = new Vector2(4350f, JiangqiaoLevel.GroundY - 2f);
            _frozeAt = _t;
            GD.Print("CAPTURE stage18 -> frozen at the bridge");
            _stage = 19;
        }
        else if (_stage == 19 && _t > _frozeAt + 2.5f)
        {
            // v3.15: was PlayerX() > 2760 - that fired while the runner was
            // still halfway down the level, so the "burnt village" frame
            // landed wherever the frame timing happened to stop him and the
            // bridge often fell outside the shot. Wait until he reaches the
            // bridge itself so the frame always shows the burnt village AND
            // the span, and stops drifting from run to run.
            GD.Print("CAPTURE stage19 -> shot burnt village x="+PlayerX());
            SaveShot("capture_11_burnt.png");
            _stage = 20;
        }
        else if (_stage == 20 && _t > 48f)
        {
            GD.Print("CAPTURE done");
            GetTree().Quit();
        }
    }

    private float PlayerX() => _lvl?._player?.Position.X ?? 0f;

    // once any viewport texture has been read successfully, later shots only
    // need a couple of attempts - this stops a headless startup race from
    // cascading into a long hang where every shot waits out its retries.
    private static bool _texOk;

    private async void SaveShot(string name)
    {
        // headless dummy renderer occasionally hands back a null viewport
        // texture for the first few frames - wait it out instead of dropping
        // the shot (the burnt-village frame used to get skipped).
        Image img = null;
        int max = _texOk ? 3 : 60;
        for (int attempt = 0; attempt < max && img == null; attempt++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var tex = _sub.GetTexture();
            if (tex != null) img = tex.GetImage();
        }
        if (img == null)
        {
            GD.PrintErr($"CAPTURE skip {name} (no viewport texture after {max} tries)");
            return;
        }
        _texOk = true;
        if (!System.IO.Directory.Exists(Dir)) System.IO.Directory.CreateDirectory(Dir);
        string path = System.IO.Path.Combine(Dir, name);
        img.SavePng(path);
        GD.Print($"CAPTURE saved {path} ({img.GetWidth()}x{img.GetHeight()})");
    }
}
