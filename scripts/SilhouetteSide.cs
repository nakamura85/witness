using Godot;

/// <summary>
/// 侧视彩色卡通人物（v0.4 美术重做：参考《Valiant Hearts》手绘上色漫画风 ——
/// 大圆头、简洁四肢、衣物色块、细深色描边；题材换为 1937 南京：中式服装与日军卡其军服）。
/// 脚底为原点 (0,0)，人物面朝 +x；朝向翻转由调用方用
/// DrawSetTransform(Vector2.Zero, 0, new Vector2(facing, 1)) 完成。
/// 复用 Silhouette.Kind 枚举；支持 走/蹲/攀爬(climb) 姿态。
/// </summary>
public static class SilhouetteSide
{
    // ---- 调色板（VH 式明快但压暗半度，贴合战时南京的灰蒙天色）----
    private static readonly Color Outline = new(0.18f, 0.16f, 0.13f, 0.92f);
    private static readonly Color Skin = new(0.91f, 0.75f, 0.58f);
    private static readonly Color SkinShade = new(0.82f, 0.64f, 0.47f);
    private static readonly Color HairDark = new(0.16f, 0.14f, 0.12f);
    private static readonly Color HairGrey = new(0.45f, 0.40f, 0.34f);

    private static readonly Color DressVautrin = new(0.90f, 0.85f, 0.74f);  // 魏特琳：浅色西式长裙
    private static readonly Color DressShade = new(0.78f, 0.72f, 0.61f);
    private static readonly Color ClothRefugee = new(0.46f, 0.51f, 0.58f);  // 难民：灰蓝土布
    private static readonly Color ClothRefugeeDk = new(0.30f, 0.33f, 0.38f);
    private static readonly Color ClothFamily = new(0.56f, 0.44f, 0.30f);   // 一家人：土褐
    private static readonly Color ClothFamilyDk = new(0.38f, 0.30f, 0.21f);
    private static readonly Color ClothKid = new(0.62f, 0.52f, 0.38f);
    private static readonly Color UniformIja = new(0.60f, 0.53f, 0.30f);    // 日军：卡其/土黄军服
    private static readonly Color UniformDk = new(0.42f, 0.37f, 0.21f);
    private static readonly Color SuitFitch = new(0.26f, 0.29f, 0.34f);     // 费奇：深色西装
    private static readonly Color WoodGun = new(0.30f, 0.22f, 0.15f);
    private static readonly Color Shoe = new(0.20f, 0.17f, 0.14f);

    /// <summary>
    /// 绘制侧视卡通人物。phase：行走摆腿相位；airborne：滞空收腿；crouch：蹲伏；climb：翻越攀爬。
    /// </summary>
    public static void Draw(CanvasItem ci, Silhouette.Kind k, float phase = 0f, bool airborne = false, bool crouch = false, bool climb = false)
    {
        switch (k)
        {
            case Silhouette.Kind.Vautrin: Vautrin(ci, phase, airborne, crouch, climb); break;
            case Silhouette.Kind.Soldier: Soldier(ci, phase, airborne); break;
            case Silhouette.Kind.Refugee: Refugee(ci, phase, airborne); break;
            case Silhouette.Kind.RefugeeChild: Child(ci, ClothRefugee, ClothRefugeeDk); break;
            case Silhouette.Kind.FamilyAdult: FamilyAdult(ci, phase, airborne); break;
            case Silhouette.Kind.FamilyChild: Child(ci, ClothKid, ClothFamilyDk); break;
            case Silhouette.Kind.Fitch: Fitch(ci, phase, airborne); break;
        }
    }

    // ---- 基础件 ----

    /// <summary>填充 + 描边多边形。</summary>
    private static void Poly(CanvasItem ci, Vector2[] pts, Color fill)
    {
        ci.DrawColoredPolygon(pts, fill);
        var loop = new Vector2[pts.Length + 1];
        pts.CopyTo(loop, 0);
        loop[pts.Length] = pts[0];
        ci.DrawPolyline(loop, Outline, 1.6f, true);
    }

    /// <summary>头：大圆头 + 朝向侧的眼睛点 + 简单耳朵。cx 略偏前（面朝 +x）。</summary>
    private static void Head(CanvasItem ci, float cx, float cy, float r, Color hair, bool bun = false)
    {
        ci.DrawCircle(new Vector2(cx, cy), r, Skin);
        ci.DrawArc(new Vector2(cx, cy), r, 0, Mathf.Tau, 20, Outline, 1.6f, true);
        // 头发盖过后脑与头顶
        ci.DrawArc(new Vector2(cx - r * 0.12f, cy - r * 0.14f), r * 0.96f, Mathf.Pi * 0.85f, Mathf.Pi * 1.9f, 16, hair, r * 0.75f, true);
        if (bun) ci.DrawCircle(new Vector2(cx - r * 0.85f, cy - r * 0.55f), r * 0.34f, hair);
        // 眼睛（前侧一个小墨点，VH 式极简）
        ci.DrawCircle(new Vector2(cx + r * 0.42f, cy - r * 0.05f), r * 0.11f, Outline);
    }

    /// <summary>双腿：髋部在 (0, hipY)，行走摆腿 / 滞空收腿。带鞋。</summary>
    private static void LegsC(CanvasItem ci, float hipY, float phase, bool airborne, Color pants)
    {
        float s = airborne ? 6f : Mathf.Sin(phase) * 9f;
        void One(float dir, float lift)
        {
            var knee = new Vector2(dir * 0.5f, hipY + 12 - lift);
            var foot = airborne ? new Vector2(dir, hipY + 20) : new Vector2(dir, 0);
            ci.DrawLine(new Vector2(0, hipY), knee, pants, 4.4f, true);
            ci.DrawLine(knee, foot, pants, 4f, true);
            ci.DrawCircle(foot, 3.2f, Shoe);
        }
        One(s, airborne ? 4 : 0);
        One(-s, airborne ? 8 : 0);
    }

    // ---- 明妮·魏特琳：浅色长裙的西方女性（玩家）----

    private static void Vautrin(CanvasItem ci, float phase, bool airborne, bool crouch, bool climb)
    {
        if (crouch)
        {
            // 蹲伏：裙摆铺地，身体前倾
            Poly(ci, new Vector2[]
            {
                new(-1, -22), new(9, -15), new(12, -3), new(10, 0),
                new(-11, 0), new(-12, -5), new(-8, -14),
            }, DressVautrin);
            Head(ci, 4, -28, 7, HairGrey, bun: true);
            // 撑地的手
            ci.DrawLine(new Vector2(6, -16), new Vector2(13, -2), DressShade, 3.4f, true);
            ci.DrawCircle(new Vector2(13, -2), 2.6f, Skin);
            return;
        }

        if (climb)
        {
            // 翻越攀爬：双手攀住前上方，裙摆收起
            Poly(ci, new Vector2[]
            {
                new(-1, -34), new(8, -26), new(9, -10), new(6, -2),
                new(-8, -2), new(-9, -14), new(-6, -28),
            }, DressVautrin);
            Head(ci, 3, -41, 7, HairGrey, bun: true);
            ci.DrawLine(new Vector2(4, -30), new Vector2(14, -44), DressShade, 3.6f, true);
            ci.DrawCircle(new Vector2(14, -44), 2.8f, Skin);   // 攀住的手
            ci.DrawLine(new Vector2(1, -30), new Vector2(10, -40), DressVautrin, 3.6f, true);
            ci.DrawCircle(new Vector2(10, -40), 2.8f, Skin);
            // 收起的脚尖
            ci.DrawCircle(new Vector2(8, -1), 2.6f, Shoe);
            ci.DrawCircle(new Vector2(-6, -1), 2.6f, Shoe);
            return;
        }

        float sway = airborne ? 0f : Mathf.Sin(phase) * 3f;
        // 长裙（前缘略前倾，底缘随步伐微摆）
        Poly(ci, new Vector2[]
        {
            new(1, -36), new(8, -28), new(10, -6), new(12 + sway, 0),
            new(-10 - sway, 0), new(-8, -12), new(-5, -28),
        }, DressVautrin);
        // 裙下摆阴影褶皱
        ci.DrawLine(new Vector2(-6, -6), new Vector2(-4 - sway * 0.5f, -0.5f), DressShade, 1.4f, true);
        ci.DrawLine(new Vector2(4, -7), new Vector2(6 + sway * 0.5f, -0.5f), DressShade, 1.4f, true);
        Head(ci, 2, -43, 7.2f, HairGrey, bun: true);
        // 手臂垂在身前
        ci.DrawLine(new Vector2(4, -31), new Vector2(9 + sway * 0.6f, -15), DressShade, 3.4f, true);
        ci.DrawCircle(new Vector2(9 + sway * 0.6f, -15), 2.6f, Skin);
        // 露出的鞋尖
        if (!airborne)
        {
            ci.DrawCircle(new Vector2(12 + sway, 0), 2.6f, Shoe);
            ci.DrawCircle(new Vector2(-6 - sway, 0), 2.6f, Shoe);
        }
    }

    // ---- 日军巡逻兵：卡其军服 + 军帽 + 步枪（压迫性系统，不可战斗敌人）----

    private static void Soldier(CanvasItem ci, float phase, bool airborne)
    {
        LegsC(ci, -16, phase, airborne, UniformDk);
        // 军服躯干（略腆肚的夸张比例）
        Poly(ci, new Vector2[]
        {
            new(-1, -40), new(9, -33), new(8, -16), new(-7, -16), new(-8, -33),
        }, UniformIja);
        // 腰带
        ci.DrawRect(new Rect2(-8, -22, 16, 3), UniformDk);
        // 军帽（带帽檐与后颈垂布）
        Head(ci, 3, -46, 6.8f, HairDark);
        Poly(ci, new Vector2[]
        {
            new(-6, -49), new(12, -49), new(10, -53), new(-4, -53),
        }, UniformIja);
        ci.DrawLine(new Vector2(12, -49), new Vector2(15, -48), UniformDk, 2.2f, true); // 帽檐
        // 端在身前的步枪
        ci.DrawLine(new Vector2(2, -27), new Vector2(26, -21), WoodGun, 3f, true);
        ci.DrawLine(new Vector2(2, -27), new Vector2(9, -30), UniformDk, 2.4f, true);  // 护木手
        ci.DrawCircle(new Vector2(9, -30), 2.4f, Skin);
    }

    // ---- 难民：弯腰驮包袱（灰蓝土布）----

    private static void Refugee(CanvasItem ci, float phase, bool airborne)
    {
        LegsC(ci, -11, phase, airborne, ClothRefugeeDk);
        // 前倾的躯干
        Poly(ci, new Vector2[]
        {
            new(0, -28), new(10, -23), new(8, -11), new(-6, -11), new(-7, -24),
        }, ClothRefugee);
        // 怀里的包袱（土色布包）
        ci.DrawCircle(new Vector2(13, -22), 5.5f, ClothFamily);
        ci.DrawArc(new Vector2(13, -22), 5.5f, 0, Mathf.Tau, 14, Outline, 1.4f, true);
        ci.DrawLine(new Vector2(8, -24), new Vector2(13, -26), ClothFamilyDk, 2f, true);
        // 抱包袱的手臂
        ci.DrawLine(new Vector2(6, -22), new Vector2(13, -18), ClothRefugee, 3.2f, true);
        ci.DrawCircle(new Vector2(13, -18), 2.4f, Skin);
        Head(ci, 8, -33, 5.8f, HairDark);
    }

    // ---- 孩子 ----

    private static void Child(CanvasItem ci, Color cloth, Color clothDk)
    {
        // 小短腿
        ci.DrawLine(new Vector2(0, -9), new Vector2(3.5f, 0), clothDk, 3.4f, true);
        ci.DrawLine(new Vector2(0, -9), new Vector2(-3.5f, 0), clothDk, 3.4f, true);
        ci.DrawCircle(new Vector2(3.5f, 0), 2.4f, Shoe);
        ci.DrawCircle(new Vector2(-3.5f, 0), 2.4f, Shoe);
        Poly(ci, new Vector2[]
        {
            new(-1, -17), new(6, -10), new(5, -8), new(-5, -8), new(-6, -12),
        }, cloth);
        Head(ci, 1, -23, 5.6f, HairDark);
    }

    // ---- 逃难的一家 · 大人：背着行囊（土褐）----

    private static void FamilyAdult(CanvasItem ci, float phase, bool airborne)
    {
        LegsC(ci, -11, phase, airborne, ClothFamilyDk);
        Poly(ci, new Vector2[]
        {
            new(-1, -36), new(8, -28), new(7, -11), new(-6, -11), new(-7, -28),
        }, ClothFamily);
        // 背上的行囊
        ci.DrawCircle(new Vector2(-9, -29), 6f, ClothRefugee);
        ci.DrawArc(new Vector2(-9, -29), 6f, 0, Mathf.Tau, 14, Outline, 1.4f, true);
        ci.DrawLine(new Vector2(-4, -33), new Vector2(-13, -25), ClothRefugeeDk, 2f, true);
        Head(ci, 2, -42, 6.4f, HairDark);
        ci.DrawLine(new Vector2(4, -28), new Vector2(9, -14), ClothFamily, 3.2f, true);
        ci.DrawCircle(new Vector2(9, -14), 2.4f, Skin);
    }

    // ---- 乔治·费奇：深色西装 + 手提胶片盒 ----

    private static void Fitch(CanvasItem ci, float phase, bool airborne)
    {
        LegsC(ci, -10, phase, airborne, SuitFitch);
        Poly(ci, new Vector2[]
        {
            new(-1, -37), new(8, -29), new(7, -10), new(-5, -10), new(-7, -29),
        }, SuitFitch);
        // 衬衫领口
        Poly(ci, new Vector2[] { new(1, -36), new(5, -33), new(2, -31) }, new Color(0.92f, 0.90f, 0.85f));
        Head(ci, 2, -43, 6.6f, HairGrey);
        // 手提胶片盒
        ci.DrawLine(new Vector2(5, -26), new Vector2(11, -16), SuitFitch, 3.2f, true);
        ci.DrawCircle(new Vector2(11, -16), 2.4f, Skin);
        ci.DrawRect(new Rect2(8, -15, 8, 6), new Color(0.35f, 0.32f, 0.28f));
        ci.DrawRect(new Rect2(8, -15, 8, 8), Outline, false, 1.4f);
    }
}
