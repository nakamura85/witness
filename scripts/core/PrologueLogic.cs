using System.Collections.Generic;

namespace Witness;

/// <summary>
/// 「源头 1895–1910」史料拼图（Viewfinder 式：碎片放回时间线 → 历史场景显现）。
/// 纯逻辑，供 PrologueLevel 与 headless 测试共用；拾取/放置状态存 WitnessState。
/// </summary>
public static class PrologueLogic
{
    /// <summary>6 枚史料碎片：3 枚属于《马关条约》（1895），3 枚属于《日韩合并条约》（1910）。</summary>
    public static readonly string[] FragmentIds = { "1895a", "1895b", "1895c", "1910a", "1910b", "1910c" };

    /// <summary>碎片名称的本地化键：pro_frag_{id}。</summary>
    public static string NameKey(string id) => $"pro_frag_{id}";

    /// <summary>碎片所属的年份（时间线槽位）。</summary>
    public static string YearOf(string id) => id.StartsWith("1895") ? "1895" : "1910";

    /// <summary>拾取（持久化到存档）。</summary>
    public static void Collect(WitnessState s, string id) => s.PrologueCollected.Add(id);

    /// <summary>是否可拾取（未拾取过）。</summary>
    public static bool CanCollect(WitnessState s, string id) => !s.PrologueCollected.Contains(id);

    /// <summary>放置判定：放对年份返回 true 并记入存档；放错返回 false（不惩罚，重试即可）。</summary>
    public static bool Place(WitnessState s, string id, string year)
    {
        if (!s.PrologueCollected.Contains(id) || s.ProloguePlaced.Contains(id)) return false;
        if (YearOf(id) != year) return false;
        s.ProloguePlaced.Add(id);
        return true;
    }

    /// <summary>全部放回 → 触发两段历史场景。</summary>
    public static bool AllPlaced(WitnessState s) => s.ProloguePlaced.Count >= FragmentIds.Length;
}
