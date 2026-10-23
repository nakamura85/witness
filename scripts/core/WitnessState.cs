using System.Collections.Generic;

namespace Witness;

/// <summary>
/// 证据状态机。设计出处：GDD §6.1 —— 记录时被巡逻看见 → 残缺；
/// 被发现时正在记录 → 没收销毁；没带出的 → 湮没。没有 Game Over，只有损失。
/// </summary>
public enum EvidenceStatus : int
{
    Uncollected = 0, // 尚未记录
    Complete = 1,    // 完整记录
    Torn = 2,        // 残缺（记录时被巡逻看见）
    Destroyed = 3,   // 被没收销毁
    Carried = 4,     // Beat3 被选中带出城
    Lost = 5,        // 记录了但没带出 → 湮没
}

/// <summary>
/// 整局游戏状态（纯 C#，不依赖 Godot，方便 headless 冒烟测试直接实例化）。
/// 含：证据 flags、三次道德选择、难民与物资、被抓次数、Beat3 取舍、史料卡解锁。
/// </summary>
public class WitnessState
{
    public int Beat;                                          // 当前节拍 0-4
    public Dictionary<string, int> Evidence = new();          // 证据 id → (int)EvidenceStatus
    public int Choice1;                                       // 1=收留缴械士兵 2=拒之门外
    public int Choice2;                                       // 1=上前阻挡 2=退回记录
    public Dictionary<string, int> Refugees = new();          // 难民 id → 1已帮助 / 0未帮助 / -1被带走
    public int Food = 2, Medicine = 1, Blanket = 2;           // 物资刻意不够 6 位难民的需（GDD §6.2）
    public int SpotCount;                                     // 被巡逻发现次数
    public bool Caught;                                       // 两次被发现 → 被押送
    public List<string> Carried = new();                      // Beat3 带出的证据 id
    public HashSet<string> TornMarked = new();                // 记录时被打断 → 残缺标记（结算卡面注明）
    public HashSet<string> CardsUnlocked = new();             // 已解锁史料卡（card1..card12，跨局持久）
    public bool Finished;                                     // 是否通关过本关（南京 1937）
    public bool PrologueDone;                                 // 是否通关「源头 1895–1910」
    public HashSet<string> PrologueCollected = new();         // 已拾取的史料碎片 id
    public HashSet<string> ProloguePlaced = new();            // 已放回时间线的碎片 id
    public bool MapNoteKorea;                                 // 东亚大地图：已读《日韩合并条约》纸条
    public bool MapNoteTaiwan;                                // 东亚大地图：已读《马关条约》割台纸条
    public bool MapOrdered;                                   // 东亚大地图：三张纸条已按年代归档
    public bool JiangqiaoDone;                                // 是否通关 1931 嫩江江桥试玩关
    public HashSet<string> JiangqiaoTestimony = new();        // 已记录的江桥证言 id

    /// <summary>关卡内可收集的 6 件证据（对应史料卡 1-6）。</summary>
    public static readonly string[] EvidenceIds = { "film", "rabe", "vautrin", "roster", "medical", "letter" };

    /// <summary>证据 id → 史料卡编号（卡 7/8/9 为尾声自动解锁，卡 10 按表现生成）。</summary>
    public static readonly Dictionary<string, int> EvidenceToCard = new()
    {
        { "film", 1 }, { "rabe", 2 }, { "vautrin", 3 },
        { "roster", 4 }, { "medical", 5 }, { "letter", 6 },
    };

    public WitnessState()
    {
        foreach (var id in EvidenceIds) Evidence[id] = (int)EvidenceStatus.Uncollected;
        // 六位可深度交互的难民 + 三位 Beat2 门口的妇女（在世原型一律不用真名 —— 呈现伦理铁律）
        foreach (var id in new[] { "r1", "r2", "r3", "r4", "r5", "r6", "w1", "w2", "w3" })
            Refugees[id] = 0;
    }

    public int RecordedCount   // 完整+残缺都算「记录过」
    {
        get { int n = 0; foreach (var s in Evidence.Values) if (s == (int)EvidenceStatus.Complete || s == (int)EvidenceStatus.Torn || s == (int)EvidenceStatus.Carried) n++; return n; }
    }

    public int HelpedCount     // 帮助人数（含保护下来的妇女）
    {
        get { int n = 0; foreach (var v in Refugees.Values) if (v == 1) n++; return n; }
    }

    public void Record(string id, bool torn)
    {
        Evidence[id] = torn ? (int)EvidenceStatus.Torn : (int)EvidenceStatus.Complete;
        if (torn) TornMarked.Add(id);
    }

    public void Destroy(string id)
        => Evidence[id] = (int)EvidenceStatus.Destroyed;

    /// <summary>Beat3 确认取舍后：带出的解锁史料卡，没带出的标记湮没（不可反悔）。</summary>
    public void ApplySettlement(List<string> carried)
    {
        Carried = new List<string>(carried);
        foreach (var id in EvidenceIds)
        {
            var st = (EvidenceStatus)Evidence[id];
            if (st == EvidenceStatus.Complete || st == EvidenceStatus.Torn)
            {
                if (carried.Contains(id))
                {
                    Evidence[id] = (int)EvidenceStatus.Carried;
                    CardsUnlocked.Add("card" + EvidenceToCard[id]);
                }
                else Evidence[id] = (int)EvidenceStatus.Lost;
            }
        }
        // 卡 7/8/9 尾声自动解锁；卡 10 幸存者口述按表现生成，同样自动解锁
        CardsUnlocked.Add("card7");
        CardsUnlocked.Add("card8");
        CardsUnlocked.Add("card9");
        CardsUnlocked.Add("card10");
        Finished = true;
    }

    /// <summary>卡 10 文案变体：帮助多 / 帮助少 / 一无所获（GDD §6.4 边界②）。</summary>
    public string OralVariant()
        => HelpedCount >= 5 ? "high" : HelpedCount >= 1 ? "mid" : "none";

    /// <summary>「源头」关通关：点亮 1895/1910 节点，解锁条约史料卡 11/12。</summary>
    public void ApplyPrologue()
    {
        PrologueDone = true;
        CardsUnlocked.Add("card11");
        CardsUnlocked.Add("card12");
    }

    /// <summary>档案馆已见证的章节数（共 5 章，供大地图进度显示）。</summary>
    public int ChaptersWitnessed => (PrologueDone ? 1 : 0) + (Finished ? 1 : 0);
}
