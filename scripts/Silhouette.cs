using Godot;

/// <summary>
/// 炭笔剪影绘制工具：所有角色都是代码绘制的炭黑剪影，
/// 贴合「炭笔/版画 + 档案拼贴」的美术基调（GDD §10.1），不依赖外部图片。
/// </summary>
public static class Silhouette
{
    public enum Kind { Vautrin, Refugee, RefugeeChild, Soldier, FamilyAdult, FamilyChild, Fitch }

    /// <summary>炭黑（主墨色）与淡墨（配饰/包袱）。</summary>
    public static readonly Color Ink = new(0.16f, 0.14f, 0.12f, 0.96f);
    public static readonly Color InkSoft = new(0.16f, 0.14f, 0.12f, 0.72f);

    /// <summary>洗褪的红 —— 用于巡逻警戒/需求徽记，是全画面唯一的暖色（GDD 美术基调）。</summary>
    public static readonly Color FadedRed = new(0.55f, 0.20f, 0.16f, 0.85f);

    public static void Draw(CanvasItem ci, Kind k)
    {
        switch (k)
        {
            case Kind.Vautrin: // 明妮·魏特琳：长裙剪影，站姿挺直
                ci.DrawCircle(new Vector2(0, -28), 7.5f, Ink);
                ci.DrawColoredPolygon(new Vector2[]
                {
                    new(0, -21), new(10, -8), new(8, 4), new(9, 16),
                    new(4, 20), new(-4, 20), new(-9, 16), new(-8, 4), new(-10, -8),
                }, Ink);
                break;

            case Kind.Refugee: // 佝偻的难民，怀里抱着包袱
                ci.DrawCircle(new Vector2(2, -19), 6f, Ink);
                ci.DrawColoredPolygon(new Vector2[]
                {
                    new(0, -14), new(8, -6), new(6, 8), new(3, 14), new(-4, 14), new(-7, 2), new(-5, -8),
                }, Ink);
                ci.DrawCircle(new Vector2(9, -4), 4.5f, InkSoft);
                break;

            case Kind.RefugeeChild: // 孩子蹲坐着
                ci.DrawCircle(new Vector2(0, -9), 5f, Ink);
                ci.DrawColoredPolygon(new Vector2[]
                {
                    new(0, -5), new(6, 0), new(5, 8), new(-5, 8), new(-6, 0),
                }, Ink);
                break;

            case Kind.Soldier: // 巡逻兵：钢帽 + 步枪剪影（压迫性系统，不是可战斗敌人）
                ci.DrawCircle(new Vector2(0, -27), 7f, Ink);
                ci.DrawRect(new Rect2(-8, -34, 16, 4), Ink); // 钢帽檐
                ci.DrawColoredPolygon(new Vector2[]
                {
                    new(0, -20), new(7, -10), new(6, 8), new(3, 16), new(-3, 16), new(-6, 8), new(-7, -10),
                }, Ink);
                ci.DrawLine(new Vector2(9, 2), new Vector2(21, -18), Ink, 2.6f); // 步枪
                break;

            case Kind.FamilyAdult: // 逃难的一家 · 大人
                ci.DrawCircle(new Vector2(0, -24), 6.5f, Ink);
                ci.DrawColoredPolygon(new Vector2[]
                {
                    new(0, -18), new(8, -6), new(6, 8), new(3, 16), new(-3, 16), new(-6, 8), new(-8, -6),
                }, Ink);
                ci.DrawCircle(new Vector2(-8, -8), 5f, InkSoft); // 背上的行囊
                break;

            case Kind.FamilyChild: // 逃难的一家 · 孩子
                Draw(ci, Kind.RefugeeChild);
                break;

            case Kind.Fitch: // 乔治·费奇：西装剪影
                ci.DrawCircle(new Vector2(0, -28), 7f, Ink);
                ci.DrawColoredPolygon(new Vector2[]
                {
                    new(0, -21), new(7, -12), new(6, 4), new(4, 18), new(-4, 18), new(-6, 4), new(-7, -12),
                }, Ink);
                ci.DrawCircle(new Vector2(8, -2), 4.5f, InkSoft); // 手边的胶片盒
                break;
        }
    }
}
