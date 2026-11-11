using System.Linq;
using Godot;

namespace Witness;

/// <summary>
/// 仅用于自动化截图：直接加载九一八事变关卡，绕过 Main 的内容警告/标题 UI 流。
/// 由 scenes/CaptureN918.tscn 单独运行（窗口模式，跑完自动退出）。
/// 输出到 res://_shots/，再由提交步骤拷进 docs/screenshots/ 同名覆盖。
/// 关键：不靠注入移动键长距离行走（窗口失焦时不可靠），改为直接瞬移机位。
/// </summary>
public partial class CaptureN918 : Node
{
    private N918Level _lvl;
    private SubViewport _sub;
    private float _t;
    private int _stage;
    private bool _started;
    private float _frozeAt;
    private static string Dir => ProjectSettings.GlobalizePath("res://_shots");

    public override void _Ready()
    {
        GD.Print("CAPTURE_N918 _Ready: building N918Level");
        _sub = new SubViewport();
        _sub.Size = new Vector2I(1280, 720);
        _sub.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        AddChild(_sub);
        _lvl = new N918Level(new Main());
        _lvl.DebugClock = 11.0f;   // 初秋暖色白昼
        _sub.AddChild(_lvl);
        _started = true;
        _t = 0f;
        _stage = 0;
        GD.Print("CAPTURE_N918 _Ready done; player present=" + (_lvl._player != null));
    }

    private void FreezeAt(float x)
    {
        var p = _lvl._player;
        if (p != null) p.Position = new Vector2(x, N918Level.GroundY - 2f);
    }

    public override void _Process(double delta)
    {
        if (!_started) return;
        _t += (float)delta;
        if (_stage >= 0 && _stage <= 8 && (_t % 1f) < 0.05f)
            GD.Print($"CAPTURE_N918 t={_t:F1} stage={_stage} playerX={_lvl.PlayerX:F0}");

        // 0: 导语黑屏仍盖在最上层——拍 1931.9.18 开场字幕
        if (_stage == 0 && _t > 1.6f)
        {
            GD.Print("CAPTURE_N918 stage0 -> shot intro overlay");
            SaveShot("n918_01_intro.png");
            _stage = 1;
        }
        else if (_stage == 1 && _t > 1.9f)
        {
            Input.ActionPress("advance");
            _stage = 2;
        }
        else if (_stage == 2 && _t > 2.1f)
        {
            Input.ActionRelease("advance");
            GD.Print("CAPTURE_N918 stage2 -> intro dismissed");
            _stage = 3;
        }
        // 3: 柳条湖起点（x≈230）初秋场景
        else if (_stage == 3 && _t > 2.9f)
        {
            GD.Print("CAPTURE_N918 stage3 -> shot liutiaohu scene");
            SaveShot("n918_02_scene.png");
            FreezeAt(2080f);          // 北大营外民居（h2）
            _frozeAt = _t;
            _stage = 4;
        }
        else if (_stage == 4 && _t > _frozeAt + 2.5f)
        {
            GD.Print("CAPTURE_N918 stage4 -> shot civilian house");
            SaveShot("n918_03_house.png");
            FreezeAt(4580f);          // 走近通信据点
            _frozeAt = _t;
            _stage = 5;
        }
        // 5: 拍据点外观（保持 x≈4580 不动，给 SaveShot 的下一帧读纹理留出稳定画面）
        else if (_stage == 5 && _t > _frozeAt + 2.5f)
        {
            GD.Print("CAPTURE_N918 stage5 -> shot radio station exterior");
            SaveShot("n918_04_radio_station.png");
            FreezeAt(4400f);          // v3.66: 柳条湖南满铁路断轨爆破点（史实，无桥）
            _frozeAt = _t;
            _stage = 6;
        }
        // 6 (v3.66 新增): 断轨爆破点实机帧——证明九一八现场没有桥，只有被炸断的路轨
        else if (_stage == 6 && _t > _frozeAt + 2.5f)
        {
            GD.Print("CAPTURE_N918 stage6 -> shot liutiaohu rail break");
            SaveShot("n918_06_liutiaohu_rail_break.png");
            _frozeAt = _t;
            _stage = 7;
        }
        // 7: 上一站纹理已稳定读出，现在才瞬移门口 + 进据点
        else if (_stage == 7 && _t > _frozeAt + 2.5f)
        {
            float doorX = 4000f;
            var comm = _lvl._houses.FirstOrDefault(h => h.Id == "comm");
            if (comm != null) doorX = comm.DoorX;
            FreezeAt(doorX);
            GD.Print($"CAPTURE_N918 stage7 comm doorX={doorX:F0}, enter");
            Input.ActionPress("interact");
            _frozeAt = _t;
            _stage = 8;
        }
        else if (_stage == 8 && _t > _frozeAt + 1.2f)
        {
            Input.ActionRelease("interact");
            GD.Print("CAPTURE_N918 stage8 -> shot interior");
            SaveShot("n918_05_interior.png");
            _stage = 9;
        }
        else if (_stage == 9 && _t > 48f)
        {
            GD.Print("CAPTURE_N918 done");
            GetTree().Quit();
        }
    }

    private static bool _texOk;

    private async void SaveShot(string name)
    {
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
            GD.PrintErr($"CAPTURE_N918 skip {name} (no viewport texture after {max} tries)");
            return;
        }
        _texOk = true;
        if (!System.IO.Directory.Exists(Dir)) System.IO.Directory.CreateDirectory(Dir);
        string path = System.IO.Path.Combine(Dir, name);
        img.SavePng(path);
        GD.Print($"CAPTURE_N918 saved {path} ({img.GetWidth()}x{img.GetHeight()})");
    }
}
