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
    private float _t;
    private int _stage;
    private bool _started;
    private const string Dir = "C:/Users/ChangFee/WorkBuddy/2026-10-26-09-06-52/witness-repo/_shots";

    public override void _Ready()
    {
        GD.Print("CAPTURE _Ready: building JiangqiaoLevel");
        _lvl = new JiangqiaoLevel(new Main());
        AddChild(_lvl);
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
            GD.Print("CAPTURE done");
            GetTree().Quit();
        }
    }

    private async void SaveShot(string name)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var tex = GetViewport().GetTexture();
        var img = tex.GetImage();
        if (!System.IO.Directory.Exists(Dir)) System.IO.Directory.CreateDirectory(Dir);
        string path = System.IO.Path.Combine(Dir, name);
        img.SavePng(path);
        GD.Print($"CAPTURE saved {path} ({img.GetWidth()}x{img.GetHeight()})");
    }
}
