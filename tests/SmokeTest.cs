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

        // 5. 存档序列化往返（与 GameState.JsonOpts 完全一致的选项）
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

        GD.Print($"==== 完成：失败 {_failed} 项 ====");
        Quit(_failed == 0 ? 0 : 1);
    }

    private void Check(string name, bool ok)
    {
        GD.Print($"  {(ok ? "PASS" : "FAIL")}  {name}");
        if (!ok) _failed++;
    }
}
