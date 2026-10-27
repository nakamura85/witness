using System.IO;
using System.Threading.Tasks;
using Godot;

namespace Witness.Tests;

/// <summary>
/// 自动巡游截图（以临时 autoload 方式挂载，正常启动游戏后自动跑）：
/// 标题→档案馆→源头关（拾取/放碎片/场景显现）→史料馆→南京，逐屏截图到 docs/screenshots/。
/// 运行：dotnet build 后正常启动游戏即可；跑完自动退出。
/// </summary>
public partial class ShotTour : Node
{
    private string _dir;
    private string _logPath;

    public override void _Ready()
    {
        _dir = ProjectSettings.GlobalizePath("res://docs/screenshots");
        Directory.CreateDirectory(_dir);
        // v2.9: the log used to be written next to the screenshots, so the
        // gallery folder ended up with a tour_log.txt in it that no one
        // wants in the published set. _shots/ is the scratch area.
        _logPath = ProjectSettings.GlobalizePath("res://_shots/tour_log.txt");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_logPath)!);
        File.WriteAllText(_logPath, $"tour start {System.DateTime.Now:HH:mm:ss.fff}\r\n");
        _ = Run();
    }

    private void Log(string msg) =>
        File.AppendAllText(_logPath, $"{System.DateTime.Now:HH:mm:ss.fff}  {msg}\r\n");

    private async Task Run()
    {
        try
        {
            // 重置进度：删除旧存档，保证截图反映全新试玩
            Log("reset begin");
            string savePath = ProjectSettings.GlobalizePath("user://save.json");
            if (File.Exists(savePath)) File.Delete(savePath);
            GameState.I.Reset();
            Log("state reset ok");

            var main = GetTree().CurrentScene;
            GD.Print("TOUR: main=" + (main != null ? main.Name : "null"));

            await Frames(40); Log("frames40 ok"); Shot("01_warning");
            main.Call("ShowTitle"); await Frames(45); Shot("02_title");
            main.Call("ShowHub"); await Frames(45); Shot("03_hub");
            main.Call("StartPrologue"); await Frames(55);
            // 关闭幕间 → 进入 Play（教学提示出现）再截「教学」画面
            await Tap("advance");
            await Frames(30); Shot("04_tutorial");
            await Frames(10);

            // 按类型查找玩家与相机（运行时创建的 C# 节点不叫 "Player"）
            Player player = null;
            Camera2D cam = null;
            foreach (Node n in main.FindChildren("*", "", true, false))
            {
                if (n is Player p) player = p;
                else if (n is Camera2D c) cam = c;
            }
            GD.Print($"TOUR: player={(player != null)} cam={(cam != null)}");

            // 依次传送至 6 个碎片点拾取（E）——横版布局：地面×2 / 桌上 / 桌下 / 层板1 / 层板2
            Vector2[] spots =
            {
                new(120, 660), new(1900, 660), new(1000, 560),
                new(1150, 660), new(300, 460), new(230, 360),
            };
            for (int i = 0; i < spots.Length; i++)
            {
                if (player != null) player.Position = spots[i];
                cam?.ResetSmoothing();
                await Frames(8);
                if (i == 0) Shot("05_pickup");
                await Tap("interact");
            }
            Log($"collected={GameState.I.S.PrologueCollected.Count}");

            // 到右墙时间线板前开板（交互点 BoardPos）
            if (player != null) player.Position = new Vector2(2190, 660);
            cam?.ResetSmoothing();
            await Frames(12);
            await Tap("interact");
            await Frames(30); Shot("06_board");

            // 找到两个「放回」按钮并按序放满
            Button b1895 = null, b1910 = null;
            foreach (Node n in GetTree().Root.FindChildren("*", "Button", true, false))
                if (n is Button b)
                {
                    if (b.Text.Contains("1895")) b1895 = b;
                    else if (b.Text.Contains("1910")) b1910 = b;
                }
            GD.Print($"TOUR: buttons 1895={b1895 != null} 1910={b1910 != null}");

            bool played = false;
            Log($"buttons 1895={b1895 != null} 1910={b1910 != null}");
            if (b1895 != null && b1910 != null)
            {
                int placed = GameState.I.S.ProloguePlaced.Count;
                b1895.EmitSignal(BaseButton.SignalName.Pressed); await Frames(6);
                if (GameState.I.S.ProloguePlaced.Count > placed)
                {
                    // _sel 从头开始：1895×3 → 1910×3
                    b1895.EmitSignal(BaseButton.SignalName.Pressed); await Frames(6);
                    b1895.EmitSignal(BaseButton.SignalName.Pressed); await Frames(6);
                    for (int i = 0; i < 3; i++) { b1910.EmitSignal(BaseButton.SignalName.Pressed); await Frames(6); }
                }
                else
                {
                    // _sel 指向末尾：1910×3 → 1895×3
                    for (int i = 0; i < 2; i++) { b1910.EmitSignal(BaseButton.SignalName.Pressed); await Frames(6); }
                    for (int i = 0; i < 3; i++) { b1895.EmitSignal(BaseButton.SignalName.Pressed); await Frames(6); }
                }
                Log($"placed={GameState.I.S.ProloguePlaced.Count}");
                if (GameState.I.S.ProloguePlaced.Count >= 6) played = true;
            }

            if (played)
            {
                // 板收起 → 1895 场景显现（两行旁白需两次推进 → 1910 场景）
                await Frames(150); Shot("07_scene_1895");
                await Tap("advance"); await Frames(30);
                await Tap("advance"); await Frames(100); Shot("08_scene_1910");
                // 走完尾声蒙太奇（4 次推进：1910 两行 → 蒙太奇两页）→ 回档案馆
                for (int k = 0; k < 4; k++)
                {
                    await Tap("advance"); await Frames(40);
                    Log($"after tap{k + 1}: done={GameState.I.S.PrologueDone} cards={GameState.I.S.CardsUnlocked.Count}");
                }
                await Frames(120);
                int layers = 0;
                foreach (Node n in main.GetChildren()) if (n is CanvasLayer) layers++;
                Log($"final: canvasLayers={layers} done={GameState.I.S.PrologueDone}");
            }
            else
            {
                main.Call("ShowHub"); await Frames(40);
            }

            main.Call("ShowGallery", false); await Frames(45); Shot("09_gallery");

            // 南京关：幕间 + 开场
            main.Call("StartNanjing"); await Frames(70); Shot("10_nanjing_intro");
            for (int i = 0; i < 3; i++) { await Tap("advance"); await Frames(25); }
            await Frames(45); Shot("11_nanjing");

            // —— 玩法画面补充（低风险先拍，巡逻兵警戒放最后以免触发押送影响后续截图）——
            // 南京关场景重建过，源头关找到的 player/cam 已销毁，必须重新查找
            Player njPlayer = null;
            Camera2D njCam = null;
            foreach (Node n in main.FindChildren("*", "", true, false))
            {
                if (n is Player p) njPlayer = p;
                else if (n is Camera2D c) njCam = c;
            }
            if (njPlayer != null && njCam != null)
            {
                // 翻越：矮台阶前按 E（v0.4 替代跳跃，台阶左边缘 x=940，rise=135 可翻）
                njPlayer.Position = new Vector2(920, 800); njCam.ResetSmoothing(); await Frames(15);
                await Tap("interact"); await Frames(18); Shot("12_vault");
                await Frames(40);

                // 蹲下潜行：淡墨涟漪
                Input.ActionPress("crouch"); await Frames(20); Shot("13_crouch");
                Input.ActionRelease("crouch"); await Frames(10);

                // 证据记录中：按住 E，HUD 进度环走到一半
                njPlayer.Position = new Vector2(5050, 692); njCam.ResetSmoothing(); await Frames(20);
                Input.ActionPress("interact"); await Frames(45); Shot("15_record");
                Input.ActionRelease("interact"); await Frames(10);

                // 金陵女子文理学院门前（侧立面 + 外楼梯）
                njPlayer.Position = new Vector2(2250, 800); njCam.ResetSmoothing(); await Frames(30); Shot("16_ginling");

                // 城门：终点方向
                njPlayer.Position = new Vector2(7350, 800); njCam.ResetSmoothing(); await Frames(30); Shot("17_gate");

                // 巡逻兵路径中段：视野锥 / 警戒（放最后，被抓也不影响前面的截图）
                njPlayer.Position = new Vector2(3900, 800); njCam.ResetSmoothing(); await Frames(30); Shot("14_patrol");
            }

            GD.Print("==== ShotTour 完成 ====");
            GetTree().Quit(0);
        }
        catch (System.Exception e)
        {
            Log("TOUR FAIL: " + e);
            GD.PrintErr("TOUR FAIL: " + e);
            GetTree().Quit(1);
        }
    }

    /// <summary>长等待：n 个「标称 60fps 帧」对应的真实秒数（游戏未锁帧，按帧数等待会过快）。</summary>
    private async Task Frames(int n)
    {
        var t = GetTree().CreateTimer(n / 60.0);
        await ToSignal(t, SceneTreeTimer.SignalName.Timeout);
    }

    /// <summary>短步进：按真实帧等待。计时器回调发生在帧末，按键帧戳会错位，故输入节奏必须按帧走。</summary>
    private async Task Step(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Tap(string action)
    {
        await Step(1); // 计时器恢复点在帧末，先对齐到帧首，按键帧戳才能被本帧 _Process 看见
        Input.ActionPress(action);
        await Step(2);
        Input.ActionRelease(action);
        await Step(2);
    }

    private void Shot(string name)
    {
        var img = GetTree().Root.GetTexture().GetImage();
        img.SavePng(Path.Combine(_dir, name + ".png"));
        GD.Print("SHOT " + name);
    }
}
