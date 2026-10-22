using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Witness.Tests;

/// <summary>
/// headless 冒烟测试（无画面，纯逻辑验证）。
/// 运行方式：
///   Godot_v4.7-stable_mono_win64.exe --headless --path . --script res://tests/SmokeTest.cs
/// 退出码 0 = 全部通过；1 = 存在失败（失败详情打印在控制台）。
/// </summary>
public partial class SmokeTest : SceneTree
{
    private int _failed;

    public override void _Initialize()
    {
        GD.Print("==== Witness 冒烟测试 ====");

        // 1. 语言归一化（LocCore.Normalize）
        Check("空标签 → 简中", LocCore.Normalize("") == "zh-CN");
        Check("zh_TW（下划线）→ 繁中", LocCore.Normalize("zh_TW") == "zh-TW");
        Check("ZH-TW（大小写）→ 繁中", LocCore.Normalize("ZH-TW") == "zh-TW");
        Check("zh-HK → 繁中", LocCore.Normalize("zh-HK") == "zh-TW");
        Check("zh-SG → 简中", LocCore.Normalize("zh-SG") == "zh-CN");
        Check("en-US → en", LocCore.Normalize("en-US") == "en");
        Check("RU（大写）→ ru", LocCore.Normalize("RU") == "ru");
        Check("ms-MY → ms", LocCore.Normalize("ms-MY") == "ms");
        Check("未知语言 xx → 简中", LocCore.Normalize("xx") == "zh-CN");

        // 2. 九种语言资源完整性：键数与简中一致、app_title 非空
        var baseD = LocCore.Load("zh-CN");
        Check("简中词条非空", baseD.Count > 100);
        foreach (var (code, _) in LocCore.Languages)
        {
            var d = LocCore.Load(code);
            Check($"[{code}] 键数与简中一致 ({d.Count}/{baseD.Count})", d.Count == baseD.Count);
            Check($"[{code}] app_title 非空", d.TryGetValue("app_title", out var t) && !string.IsNullOrWhiteSpace(t));
        }

        // 3. 证据状态机与结算（GDD §6.1 / §6.4）
        var s = new WitnessState();
        Check("初始 6 件证据未收集", s.RecordedCount == 0);
        s.Record("roster", torn: false);
        s.Record("vautrin", torn: true);
        Check("完整记录 roster", (EvidenceStatus)s.Evidence["roster"] == EvidenceStatus.Complete);
        Check("残缺记录 vautrin", (EvidenceStatus)s.Evidence["vautrin"] == EvidenceStatus.Torn);
        Check("残缺进入标记集", s.TornMarked.Contains("vautrin"));
        Check("记录计数 = 2", s.RecordedCount == 2);
        s.Destroy("rabe");
        Check("销毁 rabe", (EvidenceStatus)s.Evidence["rabe"] == EvidenceStatus.Destroyed);

        // 取舍：只带出 vautrin（残缺也允许带出）→ roster 湮没，卡 3 解锁、卡 4 不解锁
        s.ApplySettlement(new List<string> { "vautrin" });
        Check("带出 → Carried", (EvidenceStatus)s.Evidence["vautrin"] == EvidenceStatus.Carried);
        Check("未带出 → Lost（湮没）", (EvidenceStatus)s.Evidence["roster"] == EvidenceStatus.Lost);
        Check("解锁 card3", s.CardsUnlocked.Contains("card3"));
        Check("未解锁 card4", !s.CardsUnlocked.Contains("card4"));
        Check("尾声卡 7/8/9/10 自动解锁",
            s.CardsUnlocked.Contains("card7") && s.CardsUnlocked.Contains("card8")
            && s.CardsUnlocked.Contains("card9") && s.CardsUnlocked.Contains("card10"));
        Check("通关标记 Finished", s.Finished);

        // 4. 卡 10 文案变体（帮助 ≥5 → high / ≥1 → mid / 0 → none）
        Check("帮助 0 人 → none", s.OralVariant() == "none");
        s.Refugees["r1"] = 1;
        Check("帮助 1 人 → mid", s.OralVariant() == "mid");
        for (int i = 2; i <= 5; i++) s.Refugees["r" + i] = 1;
        Check("帮助 5 人 → high", s.OralVariant() == "high");

        // 5. 源头关逻辑（v0.2：碎片拾取/归位/通关解锁）
        var p = new WitnessState();
        Check("源头关：初始未拾取", p.PrologueCollected.Count == 0 && p.ProloguePlaced.Count == 0 && !p.PrologueDone);
        Check("源头关：6 枚碎片定义", PrologueLogic.FragmentIds.Length == 6);
        Check("源头关：1895/1910 各 3 枚",
            System.Linq.Enumerable.Count(PrologueLogic.FragmentIds, id => PrologueLogic.YearOf(id) == "1895") == 3
            && System.Linq.Enumerable.Count(PrologueLogic.FragmentIds, id => PrologueLogic.YearOf(id) == "1910") == 3);
        Check("碎片词条键存在", PrologueLogic.NameKey("1895a") == "pro_frag_1895a");
        PrologueLogic.Collect(p, "1895a");
        Check("拾取后 CanCollect=false", !PrologueLogic.CanCollect(p, "1895a"));
        Check("未拾取 CanCollect=true", PrologueLogic.CanCollect(p, "1910a"));
        Check("放错年代被拒绝", !PrologueLogic.Place(p, "1895a", "1910"));
        Check("放错不计入 Placed", p.ProloguePlaced.Count == 0);
        Check("放对年代成功", PrologueLogic.Place(p, "1895a", "1895"));
        Check("重复放置被拒绝", !PrologueLogic.Place(p, "1895a", "1895"));
        foreach (var id in PrologueLogic.FragmentIds) PrologueLogic.Collect(p, id);
        Check("AllPlaced 未满", !PrologueLogic.AllPlaced(p));
        PrologueLogic.Place(p, "1895b", "1895");
        PrologueLogic.Place(p, "1895c", "1895");
        PrologueLogic.Place(p, "1910a", "1910");
        PrologueLogic.Place(p, "1910b", "1910");
        PrologueLogic.Place(p, "1910c", "1910");
        Check("AllPlaced 已满", PrologueLogic.AllPlaced(p));
        p.ApplyPrologue();
        Check("ApplyPrologue 置 PrologueDone", p.PrologueDone);
        Check("ApplyPrologue 解锁 card11/card12",
            p.CardsUnlocked.Contains("card11") && p.CardsUnlocked.Contains("card12"));
        Check("ChaptersWitnessed = 1（仅源头）", p.ChaptersWitnessed == 1);
        p.Finished = true;
        Check("ChaptersWitnessed = 2（源头+南京）", p.ChaptersWitnessed == 2);

        // 5.5 横版地形（v0.3：GroundY 取该 x 处最高的可循行台面）
        Check("横版地形：主地面 y=800", Level.GroundY(100f) == 800f);
        Check("横版地形：台阶顶 y=665", Level.GroundY(1000f) == 665f);
        Check("横版地形：台阶外回到主地面", Level.GroundY(3000f) == 800f);
        Check("横版地形：金女门前主地面", Level.GroundY(2200f) == 800f);

        // 5.6 解谜纯逻辑（v0.4：翻越边界 / FindVault / 木箱 / 石块 / 蒸汽遮挡）
        Check("翻越：高差 0 不可翻", !PuzzleLogic.CanVault(0f));
        Check("翻越：高差 17 太矮", !PuzzleLogic.CanVault(17f));
        Check("翻越：高差 18 边界可翻", PuzzleLogic.CanVault(18f));
        Check("翻越：高差 100 可翻", PuzzleLogic.CanVault(100f));
        Check("翻越：高差 140 边界可翻", PuzzleLogic.CanVault(140f));
        Check("翻越：高差 141 太高", !PuzzleLogic.CanVault(141f));

        var solids = new System.Numerics.Vector4[]
        {
            new(100f, 700f, 56f, 100f),   // 顶面 y=700，从 y=800 地面 rise=100
            new(600f, 620f, 470f, 180f),  // rise=180 太高，不可直翻
        };
        var hitVault = PuzzleLogic.FindVault(new System.Numerics.Vector2(70f, 800f), 1f, solids);
        Check("FindVault：面前 30px 命中矮台", hitVault != null && hitVault.Value.topY == 700f);
        Check("FindVault：落点在台面内", hitVault != null && hitVault.Value.landX > 100f && hitVault.Value.landX < 200f);
        Check("FindVault：背对不命中", PuzzleLogic.FindVault(new System.Numerics.Vector2(70f, 800f), -1f, solids) == null);
        Check("FindVault：太远不命中", PuzzleLogic.FindVault(new System.Numerics.Vector2(10f, 800f), 1f, solids) == null);
        var tallOnly = new System.Numerics.Vector4[] { new(600f, 620f, 470f, 180f) };
        Check("FindVault：180 高台不可直翻", PuzzleLogic.FindVault(new System.Numerics.Vector2(570f, 800f), 1f, tallOnly) == null);

        Check("木箱：抓取距离内可抓", PuzzleLogic.CanGrabCrate(100f, 130f, 800f, 800f));
        Check("木箱：超距不可抓", !PuzzleLogic.CanGrabCrate(100f, 300f, 800f, 800f));
        Check("木箱：脚下高差错位不可抓", !PuzzleLogic.CanGrabCrate(100f, 130f, 800f, 700f));

        Check("石块：听觉半径内被吸引", PuzzleLogic.HeardStone(1000f, 1400f));
        Check("石块：超距听不见", !PuzzleLogic.HeardStone(1000f, 2000f));

        var steam = new System.Numerics.Vector4(500f, 600f, 130f, 200f); // x 500-630, y 600-800
        Check("蒸汽：视线穿过则被遮挡", PuzzleLogic.SteamBlocks(
            new System.Numerics.Vector2(300f, 700f), new System.Numerics.Vector2(800f, 700f), steam));
        Check("蒸汽：从上方越过不遮挡", !PuzzleLogic.SteamBlocks(
            new System.Numerics.Vector2(400f, 500f), new System.Numerics.Vector2(800f, 500f), steam));
        Check("蒸汽：同侧不遮挡", !PuzzleLogic.SteamBlocks(
            new System.Numerics.Vector2(700f, 700f), new System.Numerics.Vector2(800f, 700f), steam));

        // 6. 史料卡数据库：12 张
        Check("史料卡共 12 张", CardDb.Load().Count == 12);

        // 7. 存档序列化往返（与 GameState.JsonOpts 完全一致的选项）
        var opts = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
        s.Beat = 2;
        s.Choice1 = 1;
        s.Choice2 = 2;
        s.Food = 0;
        s.SpotCount = 1;
        s.Caught = true;
        string json = JsonSerializer.Serialize(s, opts);
        var back = JsonSerializer.Deserialize<WitnessState>(json, opts);
        Check("存档往返：Beat", back.Beat == 2);
        Check("存档往返：Choice1/2", back.Choice1 == 1 && back.Choice2 == 2);
        Check("存档往返：证据状态", (EvidenceStatus)back.Evidence["vautrin"] == EvidenceStatus.Carried);
        Check("存档往返：CardsUnlocked", back.CardsUnlocked.Contains("card3"));
        Check("存档往返：TornMarked", back.TornMarked.Contains("vautrin"));
        Check("存档往返：Carried", back.Carried.Count == 1 && back.Carried[0] == "vautrin");
        Check("存档往返：Caught", back.Caught && back.SpotCount == 1);

        // v0.2 新字段的往返
        back.PrologueCollected.Add("1895a");
        back.ProloguePlaced.Add("1910a");
        back.PrologueDone = true;
        string json2 = JsonSerializer.Serialize(back, opts);
        var back2 = JsonSerializer.Deserialize<WitnessState>(json2, opts);
        Check("存档往返：Prologue 字段",
            back2.PrologueDone && back2.PrologueCollected.Contains("1895a") && back2.ProloguePlaced.Contains("1910a"));

        GD.Print($"==== 完成：失败 {_failed} 项 ====");
        Quit(_failed == 0 ? 0 : 1);
    }

    private void Check(string name, bool ok)
    {
        GD.Print($"  {(ok ? "PASS" : "FAIL")}  {name}");
        if (!ok) _failed++;
    }
}
