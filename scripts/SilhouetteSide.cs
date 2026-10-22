using Godot;

/// <summary>
/// 侧视炭笔剪影（v0.3 横版）：与 Silhouette 同一套墨色语言，
/// 但人物改为侧面姿势 —— 行走摆腿、跳跃收腿、蹲伏压低。
/// 脚底为原点 (0,0)，人物面朝 +x；朝向翻转由调用方用
/// DrawSetTransform(Vector2.Zero, 0, new Vector2(facing, 1)) 完成。
/// 复用 Silhouette.Kind 枚举与墨色。
/// </summary>
public static class SilhouetteSide
{
    /// <summary>
    /// 绘制侧视剪影。phase：行走摆腿相位（静止传 0）；airborne：空中收腿；crouch：蹲伏（仅玩家使用）。
    /// </summary>
    public static void Draw(CanvasItem ci, Silhouette.Kind k, float phase = 0f, bool airborne = false, bool crouch = false)
    {
        switch (k)
        {
            case Silhouette.Kind.Vautrin: Vautrin(ci, phase, airborne, crouch); break;
            case Silhouette.Kind.Soldier: Soldier(ci, phase, airborne); break;
            case Silhouette.Kind.Refugee: Refugee(ci, phase, airborne); break;
            case Silhouette.Kind.RefugeeChild: Child(ci); break;
            case Silhouette.Kind.FamilyAdult: FamilyAdult(ci, phase, airborne); break;
            case Silhouette.Kind.FamilyChild: Child(ci); break;
            case Silhouette.Kind.Fitch: Fitch(ci, phase, airborne); break;
        }
    }

    // ---- 明妮·魏特琳：长裙女性，侧影 ----

    private static void Vautrin(CanvasItem ci, float phase, bool airborne, bool crouch)
    {
        var ink = Silhouette.Ink;
        if (crouch)
        {
            // 蹲伏：重心压低，裙摆收拢铺地
            ci.DrawCircle(new Vector2(3, -27), 6.5f, ink);
            ci.DrawColoredPolygon(new Vector2[]
            {
                new(0, -21), new(9, -14), new(11, -3), new(9, 0),
                new(-10, 0), new(-11, -4), new(-7, -13),
            }, ink);
            return;
        }

        float sway = airborne ? 0f : Mathf.Sin(phase) * 3f;
        // 长裙（侧视：前缘略前倾，底缘随步伐微摆）
        ci.DrawColoredPolygon(new Vector2[]
        {
            new(2, -36), new(8, -28), new(10, -6), new(12 + sway, 0),
            new(-10 - sway, 0), new(-8, -12), new(-5, -28),
        }, ink);
        // 躯干与头
        ci.DrawCircle(new Vector2(2, -43), 7f, ink);
        // 手臂：垂在身前
        ci.DrawLine(new Vector2(4, -32), new Vector2(9 + sway * 0.6f, -16), ink, 2.6f);
        // 露出的脚尖
        if (!airborne)
        {
            ci.DrawLine(new Vector2(10 + sway, 0), new Vector2(15 + sway, 0), ink, 2.4f);
            ci.DrawLine(new Vector2(-6 - sway, 0), new Vector2(-1 - sway, 0), ink, 2.4f);
        }
    }

    // ---- 巡逻兵：钢帽 + 端枪（压迫性系统，不可战斗敌人）----

    private static void Soldier(CanvasItem ci, float phase, bool airborne)
    {
        var ink = Silhouette.Ink;
        ci.DrawCircle(new Vector2(3, -46), 6.5f, ink);
        ci.DrawRect(new Rect2(-5, -52, 15, 3.2f), ink); // 钢帽檐前伸
        ci.DrawColoredPolygon(new Vector2[]
        {
            new(0, -40), new(8, -32), new(6, -16), new(-6, -16), new(-7, -32),
        }, ink);
        Legs(ci, -16, phase, airborne);
        // 步枪：端在身前，枪口指向前方
        ci.DrawLine(new Vector2(4, -26), new Vector2(26, -21), ink, 2.6f);
        ci.DrawLine(new Vector2(22, -22.5f), new Vector2(26, -25), ink, 2.2f); // 枪口准星
    }

    // ---- 难民：弯腰驮包袱 ----

    private static void Refugee(CanvasItem ci, float phase, bool airborne)
    {
        var ink = Silhouette.Ink;
        ci.DrawCircle(new Vector2(7, -33), 5.5f, ink);   // 前倾的头
        ci.DrawColoredPolygon(new Vector2[]
        {
            new(1, -28), new(10, -22), new(7, -11), new(-5, -11), new(-6, -23),
        }, ink);
        ci.DrawCircle(new Vector2(13, -23), 5f, Silhouette.InkSoft); // 怀里的包袱
        Legs(ci, -11, phase, airborne);
    }

    // ---- 孩子 ----

    private static void Child(CanvasItem ci)
    {
        var ink = Silhouette.Ink;
        ci.DrawCircle(new Vector2(1, -23), 5f, ink);
        ci.DrawColoredPolygon(new Vector2[]
        {
            new(0, -18), new(6, -10), new(5, 0), new(-4, 0), new(-5, -10),
        }, ink);
    }

    // ---- 逃难的一家 · 大人：背着行囊 ----

    private static void FamilyAdult(CanvasItem ci, float phase, bool airborne)
    {
        var ink = Silhouette.Ink;
        ci.DrawCircle(new Vector2(2, -43), 6f, ink);
        ci.DrawColoredPolygon(new Vector2[]
        {
            new(0, -37), new(8, -27), new(6, -11), new(-5, -11), new(-6, -27),
        }, ink);
        ci.DrawCircle(new Vector2(-9, -30), 5.5f, Silhouette.InkSoft); // 背上的行囊
        Legs(ci, -11, phase, airborne);
    }

    // ---- 乔治·费奇：西装 + 手提胶片盒 ----

    private static void Fitch(CanvasItem ci, float phase, bool airborne)
    {
        var ink = Silhouette.Ink;
        ci.DrawCircle(new Vector2(2, -44), 6.5f, ink);
        ci.DrawColoredPolygon(new Vector2[]
        {
            new(0, -38), new(8, -28), new(6, -10), new(-4, -10), new(-6, -28),
        }, ink);
        ci.DrawCircle(new Vector2(11, -14), 4.5f, Silhouette.InkSoft); // 手边的胶片盒
        ci.DrawLine(new Vector2(6, -24), new Vector2(11, -18), ink, 2.4f);
        Legs(ci, -10, phase, airborne);
    }

    /// <summary>通用双腿：髋部在 (0, hipY)，行走摆腿 / 空中收腿。</summary>
    private static void Legs(CanvasItem ci, float hipY, float phase, bool airborne)
    {
        var ink = Silhouette.Ink;
        if (airborne)
        {
            ci.DrawLine(new Vector2(0, hipY), new Vector2(9, hipY * 0.45f), ink, 3f);
            ci.DrawLine(new Vector2(0, hipY), new Vector2(-7, -2), ink, 3f);
            return;
        }
        float s = Mathf.Sin(phase) * 8f;
        ci.DrawLine(new Vector2(0, hipY), new Vector2(s, 0), ink, 3f);
        ci.DrawLine(new Vector2(0, hipY), new Vector2(-s, 0), ink, 3f);
    }
}
