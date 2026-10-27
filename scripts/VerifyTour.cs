using System.IO;
using System.Threading.Tasks;
using Godot;

namespace Witness;

/// <summary>临时验收巡游：东亚地图 → 三张纸条 → 嫩江江桥关卡，逐屏截图后自动退出。</summary>
public partial class VerifyTour : Node
{
    private string _dir;
    private string _scratch;
    private string _log;

    // Only these five frames belong to the published gallery. Everything else
    // is a mid-tour audit frame and must stay out of docs/screenshots, otherwise
    // every audit run litters the gallery with a dozen unreferenced frames.
    //
    // The level frames (v46..v51) are ShotTour's job - it owns those.
    // Capture.cs is a different thing entirely: it writes to _shots/ for
    // eyeballing, not to the published gallery.
    private static readonly string[] GalleryShots =
    {
        "v00_title", "v01_map", "v02_note_korea", "v03_note_taiwan", "v04_map_arrows"
    };

    public override void _Ready()
    {
        _dir = ProjectSettings.GlobalizePath("res://docs/screenshots");
        Directory.CreateDirectory(_dir);
        _scratch = ProjectSettings.GlobalizePath("user://verify_shots");
        Directory.CreateDirectory(_scratch);
        _log = Path.Combine(_scratch, "verify_log.txt");
        File.WriteAllText(_log, $"start {System.DateTime.Now:HH:mm:ss}\r\n");
        _ = Run();
    }

    private void Log(string m) => File.AppendAllText(_log, m + "\r\n");

    private async Task Run()
    {
        try
        {
            string save = ProjectSettings.GlobalizePath("user://save.json");
            if (File.Exists(save)) File.Delete(save);
            GameState.I.Reset();

            var main = GetTree().CurrentScene;

            // —— 先点 ContentWarning 的"我明白了，继续"进入标题画面 ——
            await Frames(30);
            await Click("我明白了");
            await Frames(60);          // fade-to-black + TitleScreen 渲染
            await Shot("v00_title");

            // 点"新的见证"进入东亚地图
            await Click("新的见证");
            await Frames(40);
            await Shot("v01_map");

            await Click("纸条 01");
            await Frames(35);
            await Shot("v02_note_korea");
            await Click("收起纸条");
            await Frames(25);

            await Click("纸条 02");
            await Frames(35);
            await Shot("v03_note_taiwan");
            await Click("收起纸条");
            await Frames(150);           // 进军箭头生长
            await Shot("v04_map_arrows");

            await Click("作战简报");
            await Frames(35);
            await Shot("v05_note_brief");
            await Click("收起纸条");
            await Frames(30);
            await Shot("v06_map_enter");

            await Click("进入关卡");
            await Frames(70);
            await Shot("v07_intro");
            await Tap("advance");
            await Frames(45);
            await Shot("v08_level_start");

            FieldPlayer p = null;
            Camera2D cam = null;
            foreach (Node n in main.FindChildren("*", "", true, false))
            {
                if (n is FieldPlayer fp) p = fp;
                else if (n is Camera2D c) cam = c;
            }
            Log($"player={p != null} cam={cam != null}");
            if (p == null) { GetTree().Quit(1); return; }

            // 敌兵：走到触发线之后等他们推进到近处
            p.GlobalPosition = new Vector2(1420, 618);
            p.Velocity = Vector2.Zero;
            cam?.ResetSmoothing();
            await Frames(150);
            await Shot("v12_enemy_advance");
            await Frames(60);
            await Shot("v13_enemy_fire");

            // —— 战斗验证：开枪击毙一名敌兵 → 阵亡留尸体（不再透明消失）——
            JiangqiaoLevel lvl = null;
            foreach (Node n in main.FindChildren("*", "", true, false))
                if (n is JiangqiaoLevel l) lvl = l;
            if (lvl != null)
            {
                int corpsesBefore = CountNodes<CorpseActor>(main);
                Log($"corpses_before={corpsesBefore}");

                // v2.8: the old code reached into the private "Fire" method by
                // reflection and called it with no arguments. "Fire" now takes
                // a facing argument, so that call threw
                // TargetParameterCountException and killed the tour half way
                // through. FieldPlayer exposes ForceFire for exactly this,
                // and it takes the facing directly - no field poking needed.
                p.ForceFire(1f);                 // 第一枪：命中→敌兵警觉反击
                await Frames(10);
                await Shot("v13b_player_fire_recoil");
                await Frames(60);
                p.ForceFire(1f);                 // 第二枪：两枪阵亡→留尸体
                await Frames(40);

                int corpsesAfter = CountNodes<CorpseActor>(main);
                int enemiesLeft = 0;
                foreach (var e in lvl._enemies)
                    if (IsInstanceValid(e) && !e.Gone) enemiesLeft++;
                Log($"corpses_after={corpsesAfter} enemies_left={enemiesLeft}");
                Log(corpsesAfter > corpsesBefore ? "CORPSE_OK" : "CORPSE_FAIL");
                await Shot("v26_corpse");
            }

            // 敌兵人数随机 1~3：重置位置触发下一波，只记录不判失败
            p.GlobalPosition = new Vector2(2050, 618);
            p.Velocity = Vector2.Zero;
            cam?.ResetSmoothing();
            await Frames(120);
            if (lvl != null)
            {
                int alive = 0;
                foreach (var e in lvl._enemies)
                    if (IsInstanceValid(e) && !e.Gone) alive++;
                Log($"wave2_alive={alive}");
            }
            await Shot("v27_wave2");

            // 行走动画：按住 D 两秒，连拍两帧对比
            Input.ActionPress("move_right");
            await Frames(30);
            await Shot("v14_walk_a");
            await Frames(12);
            await Shot("v15_walk_b");
            Input.ActionRelease("move_right");
            await Frames(20);

            // 跳跃中
            Input.ActionPress("advance");
            await Frames(4);
            Input.ActionRelease("advance");
            await Frames(10);
            await Shot("v16_jump");
            await Frames(45);

            // 各段场景
            float[] spots = { 720, 1500, 2220, 3000, 3480, 4200, 4700 };
            string[] names = { "v17_t1", "v18_mid1", "v19_t2", "v20_mid2", "v21_t3", "v22_bridge", "v23_end" };
            for (int i = 0; i < spots.Length; i++)
            {
                p.GlobalPosition = new Vector2(spots[i], 618);
                p.Velocity = Vector2.Zero;
                cam?.ResetSmoothing();
                await Frames(26);
                await Shot(names[i]);
            }

            // 记录证言：回到 t1 按住 E
            p.GlobalPosition = new Vector2(720, 618);
            p.Velocity = Vector2.Zero;
            cam?.ResetSmoothing();
            await Frames(20);
            Input.ActionPress("interact");
            await Frames(50);
            await Shot("v24_record");
            Input.ActionRelease("interact");
            await Frames(30);
            await Shot("v25_note_recorded");

            Log("done");
            GetTree().Quit(0);
        }
        catch (System.Exception e)
        {
            Log("FAIL " + e);
            GD.PrintErr("VERIFY FAIL " + e);
            GetTree().Quit(1);
        }
    }

    private static int CountNodes<T>(Node root) where T : Node
    {
        int n = 0;
        foreach (Node c in root.FindChildren("*", "", true, false))
            if (c is T) n++;
        return n;
    }

    private async Task Frames(int n)
    {
        var t = GetTree().CreateTimer(n / 60.0);
        await ToSignal(t, SceneTreeTimer.SignalName.Timeout);
    }

    private async Task Tap(string action)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Input.ActionPress(action);
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Input.ActionRelease(action);
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task<bool> Click(string text)
    {
        foreach (Node n in GetTree().Root.FindChildren("*", "Button", true, false))
        {
            if (n is Button b && b.IsVisibleInTree()
                && (b.Name.ToString() == text || b.Text.Contains(text)))
            {
                b.EmitSignal(BaseButton.SignalName.Pressed);
                Log("click " + text);
                await Frames(2);
                return true;
            }
        }
        Log("MISS button " + text);
        return false;
    }

    private async Task Shot(string name)
    {
        // 根视口的底图必须等当前帧真正画完再读，否则读到的是被清空的下一个缓冲
        // （表现就是“整屏纯白”——之前误判成游戏 bug 的正是这个）。
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = GetTree().Root.GetTexture().GetImage();
        bool gallery = System.Array.IndexOf(GalleryShots, name) >= 0;
        img.SavePng(Path.Combine(gallery ? _dir : _scratch, name + ".png"));
        GD.Print("SHOT " + name + (gallery ? " [gallery]" : ""));
        Log($"px300={img.GetPixel(400, 300).ToHtml()}");
    }
}

