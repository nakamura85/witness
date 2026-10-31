using Godot;

namespace Witness;

/// <summary>
/// 只做一件事：验证烟是真的在动。
/// 把玩家钉在原地不动（相机自然也不动），隔一段时间拍两帧同一位置，
/// 再把两张图裁出来对比，烟柱形状必须不一样。
/// 这只是验收工具，正式游戏流程不引用它。
/// </summary>
public partial class SmokeCheck : Node
{
    private JiangqiaoLevel _lvl;
    private float _t;
    private int _shot;
    private bool _started;
    // v2.9: this was a hardcoded absolute path including the original
    // author's home folder, so a fresh clone wrote to a folder that does not
    // exist on anyone else's machine. res:// resolves against the project.
    private static string Dir
        => ProjectSettings.GlobalizePath("res://_shots");

    public override void _Ready()
    {
        _lvl = new JiangqiaoLevel(new Main());
        _lvl.DebugClock = 11.0f;
        AddChild(_lvl);
        _started = true;
        GD.Print("SMOKECHECK ready target=" + Debug.TargetX);
    }

    public override void _Process(double delta)
    {
        if (!_started) return;
        _t += (float)delta;

        // 幕间导语先按掉，不然画面被字幕盖住看不到烟
        if (_t > 1.2f && _t < 1.35f)
        {
            Input.ActionPress("advance");
        }
        else if (_t >= 1.35f)
        {
            Input.ActionRelease("advance");
        }

        // 走到完好房屋（x=1885）旁边站定，相机自然锁住
        float px = _lvl?._player?.Position.X ?? 0f;
        float target = Debug.TargetX;
        if (px < target) Input.ActionPress("move_right");
        else Input.ActionRelease("move_right");

        if (_shot == 0 && _t > 16f)
        {
            Save("smokecheck_a.png");
            _shot = 1;
        }
        else if (_shot == 1 && _t > 17.2f)
        {
            Save("smokecheck_b.png");
            _shot = 2;
        }
        else if (_shot == 2 && _t > 18f)
        {
            GD.Print("SMOKECHECK done");
            GetTree().Quit();
        }
    }

    /// <summary>站在哪个世界坐标就改成哪个，方便对不同房屋做同样的动画验收。</summary>
    public static class Debug
    {
        public static float TargetX = 2010f;
    }

    private async void Save(string name)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var img = GetViewport().GetTexture().GetImage();
        if (!System.IO.Directory.Exists(Dir)) System.IO.Directory.CreateDirectory(Dir);
        img.SavePng(System.IO.Path.Combine(Dir, name));
        GD.Print("SMOKECHECK saved " + name);
        DumpScreenMap();
    }

    /// <summary>
    /// 打印关键世界坐标落在屏幕的哪个像素，这样裁图就不用靠肉眼猜了。
    /// </summary>
    private void DumpScreenMap()
    {
        var cam = GetViewport().GetCamera2D();
        if (cam == null) { GD.Print("SMOKECHECK no camera"); return; }
        float zoom = cam.Zoom.X;
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        Vector2 c = cam.GetScreenCenterPosition();
        GD.Print($"SMOKECHECK map zoom={zoom:F3} viewport={vp} center={c}");
        foreach (var wx in new[] { 1885f, 2760f, 845f, 3180f })
        {
            var s = (new Vector2(wx, JiangqiaoLevel.GroundY) - c) * zoom + vp * 0.5f;
            GD.Print($"SMOKECHECK world x={wx} -> screen x={s.X:F0} y={s.Y:F0}");
        }
    }
}
