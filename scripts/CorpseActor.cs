using System.Collections.Generic;
using Godot;

namespace Witness;

/// <summary>
/// 尸体：SpriteActor 立绘 + 史实化倒下姿势 + 多处不规则血迹。
/// 
/// 姿势依据 1931 江桥抗战史实照片：
/// - 冲锋中弹 → 向前扑倒（俯卧 face-down），身体约 180° 翻转头着地，立绘 FlipV
/// - 站立中弹 → 向后倒下（仰卧 face-up），正常 90° 躺着
/// - 侧卧极少（约 5%），侧面中弹倒下
/// 所有姿势的随机性只做轻微 ±15° 歪斜（不是乱倒 55-125°！），模拟中弹瞬间的自然反应。
/// 血迹不规则多边形（不是椭圆），基于 RNG 种子确保同一尸体每次一致。
/// </summary>
public partial class CorpseActor : Node2D
{
    public enum Faction { IJA, NortheastArmy, Refugee }

    /// <summary>
    /// 三种倒下姿势（基于中弹方向）。
    /// </summary>
    private enum Pose { Prone, Supine, SideLying }

    private readonly Sprite2D _sp;
    private readonly Node2D _body;      // 中间旋转节点：负责水平躺倒
    private readonly Faction _faction;
    private readonly Pose _pose;
    private readonly float _seed;

    public CorpseActor(Faction faction, Vector2 position, bool facingRight, float rotJitter = -1f)
    {
        _faction = faction;
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        _seed = rng.Randf();

        // 先决定头朝哪个方向（史实化）
        float r = rng.Randf();
        float headAngle;  // 90° 或 -90°（水平躺）
        if (r < 0.70f)
        {
            _pose = Pose.Prone;
            // 冲锋中弹向前扑 → 头朝冲锋方向
            headAngle = facingRight ? Mathf.Pi / 2f : -Mathf.Pi / 2f;
        }
        else if (r < 0.95f)
        {
            _pose = Pose.Supine;
            // 站立中弹向后倒 → 头朝反方向
            headAngle = facingRight ? Mathf.Pi / 2f : -Mathf.Pi / 2f;
        }
        else
        {
            _pose = Pose.SideLying;
            headAngle = facingRight ? Mathf.Pi / 2f : -Mathf.Pi / 2f;
        }
        // ±15° 轻微歪斜
        headAngle += rng.RandfRange(-0.26f, 0.26f);

        string texName = faction switch
        {
            // 尸体专用立绘（*_corpse）：武器已从手中移除——
            // 人死亡时枪会掉落，不会还握在手里；掉落的步枪由 DrawDroppedRifle 画在尸体旁。
            Faction.IJA => "soldier_ija_corpse",
            Faction.NortheastArmy => "soldier_na_corpse",
            _ => "refugee_man",
        };

        var tex = ChromaKey.Cutout($"res://assets/art/sprites/{texName}.jpg", 0.03f, 0.05f);
        if (tex == null) return;

        float height = faction == Faction.Refugee ? 96f : 108f;
        float s = height / tex.GetSize().Y;

        // —— 三层结构，每层只做一件事：——
        // CorpseActor (Node2D): Position=尸体位置, Rotation=0（不旋转）
        //   └─ _body (Node2D): Rotation=90°水平躺倒 + 轻微歪斜
        //       └─ _sp (Sprite2D): 原始方向，只翻转头朝哪

        _body = new Node2D
        {
            Rotation = headAngle,
        };

        bool headTowardRight = _pose switch
        {
            Pose.Prone => facingRight,       // 向前扑 → 头朝冲锋方向
            Pose.Supine => !facingRight,     // 向后倒 → 头朝反方向
            _ => rng.Randf() > 0.5f,
        };

        _sp = new Sprite2D
        {
            Texture = tex,
            Scale = new Vector2(s, s),
            // 注意：Sprite2D 原始方向是头朝上（Y负），旋转 90° 后头就朝 X正方向（右）
            // 所以 headTowardRight=true 时 FlipH=false；头朝左时 FlipH=true
            Position = new Vector2(0, -height * 0.5f),  // 立绘脚底在原点
            FlipH = !headTowardRight,
            FlipV = false,  // 永远不上下翻转！侧面横版里尸体就是水平躺的
        };

        _body.AddChild(_sp);
        AddChild(_body);

        Position = position;
        Rotation = 0f;
        ZIndex = 0;
        Modulate = new Color(1f, 0.92f, 0.90f, 1f);

        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawContactShadow();
        DrawDroppedRifle();
        DrawBlood();
        DrawTears();
    }

    /// <summary>
    /// 接触阴影：沿身体方向的压扁椭圆压在雪面上——尸体是「躺」在雪地里，
    /// 不是浮在雪面上（与其他物件的 GroundShadow 同一套视觉语言）。
    /// </summary>
    private void DrawContactShadow()
    {
        float ang = _body.Rotation;
        var center = new Vector2(Mathf.Sin(ang) * 54f, 2f);   // 躯干中心正下方
        DrawSetTransform(center, ang - Mathf.Pi / 2f, new Vector2(1f, 0.15f));
        DrawCircle(Vector2.Zero, 64f, new Color(0.10f, 0.09f, 0.08f, 0.20f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    /// <summary>
    /// 掉落的步枪：人死后武器脱手落地，掉落距离由身高/倒地姿态与撞击力度决定（45-115px 不等），
    /// 枪身近水平歪斜（±32°），半埋进雪里。基于 _seed 固定，同一具尸体每次绘制一致。
    /// </summary>
    private void DrawDroppedRifle()
    {
        if (_faction == Faction.Refugee) return;
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)(Mathf.RoundToInt(_seed * 1000000f) + 7);

        float dropDist = rng.RandfRange(45f, 115f);
        float side = rng.Randf() > 0.35f ? 1f : -1f;   // 多数落在身体一侧，少数滑到另一侧
        float rx = side * dropDist;
        float ry = rng.RandfRange(4f, 13f);            // 贴地：枪身半埋进雪
        float ang = rng.RandfRange(-0.56f, 0.56f);

        DrawSetTransform(new Vector2(rx, ry), ang, Vector2.One);
        var wood = new Color(0.34f, 0.25f, 0.17f, 0.96f);
        var metal = new Color(0.24f, 0.26f, 0.29f, 0.96f);
        // 枪托（弯形五边形）
        var stock = new[]
        {
            new Vector2(-36f, 0f), new Vector2(-14f, -4.6f), new Vector2(-8f, -1.2f),
            new Vector2(-10f, 3.4f), new Vector2(-34f, 4.6f),
        };
        DrawColoredPolygon(stock, wood);
        DrawRect(new Rect2(-8f, -3.4f, 46f, 3.8f), metal);   // 枪管+机匣
        DrawRect(new Rect2(8f, -5.2f, 10f, 2.4f), metal);    // 表尺/准星座
        DrawCircle(new Vector2(1.5f, 0.8f), 1.7f, metal);    // 拉栓柄
        // 半埋雪：枪下缘一道雪脊（在雪地里躺了一段时间）
        DrawRect(new Rect2(-32f, 2.6f, 64f, 2.8f), new Color(0.93f, 0.95f, 0.96f, 0.82f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    private void DrawBlood()
    {
        float ang = _body.Rotation;
        // 锚点：躯干中心。立绘高 108、脚底在节点原点，躺倒后躯干中心在沿身方向 54px 处
        // ——血从躯干（中弹部位）下方渗出，不是从脚底渗出（之前血塘画在原点，和身体脱节）。
        float bcX = Mathf.Sin(ang) * 54f;
        float bcY = Mathf.Cos(ang) * 54f + 4f;    // +4 沉进雪面
        // 头部位置（沿身 ~90px），头部中弹的浓血集中在这里
        float headX = Mathf.Sin(ang) * 90f;
        float headY = Mathf.Cos(ang) * 90f + 3f;
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)Mathf.RoundToInt(_seed * 1000000f);

        // —— 主血塘：不规则多边形（不是椭圆！8-12 个点，边缘锯齿状）——
        int mainPts = 8 + Mathf.FloorToInt(rng.Randf() * 5f);
        float mainR = 24f + rng.RandfRange(-4f, 8f);
        var main = new List<Vector2>();
        for (int i = 0; i < mainPts; i++)
        {
            float a = i / (float)mainPts * Mathf.Tau + rng.RandfRange(-0.18f, 0.18f);
            float jitter = 0.55f + rng.Randf() * 0.9f;  // 随机幅度：有的点凹进去有的凸出来
            float rx = mainR * jitter * 1.35f;          // 沿身方向拉长（血顺着身体渗）
            float ry = mainR * 0.38f * jitter;
            float x = bcX + Mathf.Cos(ang) * rx + Mathf.Cos(ang + Mathf.Pi / 2f) * ry;
            float y = bcY + Mathf.Sin(ang) * rx + Mathf.Sin(ang + Mathf.Pi / 2f) * ry;
            main.Add(new Vector2(x, y));
        }
        DrawColoredPolygon(main.ToArray(), new Color(0.32f, 0.07f, 0.05f, 0.85f));

        // —— 渗血层：更大的半透明淡红（渗进雪里的感觉）——
        var bleed = new List<Vector2>();
        for (int i = 0; i < mainPts; i++)
        {
            float a = i / (float)mainPts * Mathf.Tau + rng.RandfRange(-0.25f, 0.25f);
            float jitter = 0.7f + rng.Randf() * 0.8f;
            float rx = (mainR + 18f) * jitter * 1.4f;
            float ry = (mainR + 18f) * 0.42f * jitter;
            float x = bcX + Mathf.Cos(ang) * rx + Mathf.Cos(ang + Mathf.Pi / 2f) * ry;
            float y = bcY + Mathf.Sin(ang) * rx + Mathf.Sin(ang + Mathf.Pi / 2f) * ry;
            bleed.Add(new Vector2(x, y));
        }
        DrawColoredPolygon(bleed.ToArray(), new Color(0.50f, 0.13f, 0.09f, 0.22f));

        // —— 小血斑：沿身体两侧的水平带状散布（血在雪面上平铺，不往天上溅）——
        int splatterCount = 2 + Mathf.FloorToInt(rng.Randf() * 3f);
        for (int i = 0; i < splatterCount; i++)
        {
            float cx = bcX + Mathf.Sin(ang) * rng.RandfRange(-72f, 72f)
                     + Mathf.Cos(ang) * rng.RandfRange(-26f, 26f);
            float cy = bcY + rng.RandfRange(-5f, 9f);
            float r = 3f + rng.Randf() * 5f;
            // 小血斑是不规则多边形（3-5 点），不是圆
            int sPts = 3 + Mathf.FloorToInt(rng.Randf() * 3f);
            var spot = new List<Vector2>();
            for (int k = 0; k < sPts; k++)
            {
                float sAng = k / (float)sPts * Mathf.Tau + rng.RandfRange(-0.3f, 0.3f);
                float sJit = 0.5f + rng.Randf() * 0.7f;
                spot.Add(new Vector2(cx + Mathf.Cos(sAng) * r * sJit,
                    cy + Mathf.Sin(sAng) * r * sJit));
            }
            DrawColoredPolygon(spot.ToArray(), new Color(0.42f, 0.09f, 0.07f, 0.65f));
        }

        // —— 血丝：贴着雪面向低处流（3-5 条，长度不同，不是朝天放射）——
        int dripCount = 3 + Mathf.FloorToInt(rng.Randf() * 3f);
        for (int i = 0; i < dripCount; i++)
        {
            float dir = rng.Randf() > 0.5f ? 1f : -1f;
            float len = 30f + rng.Randf() * 30f;
            var p0 = new Vector2(bcX + dir * rng.RandfRange(6f, 18f), bcY + rng.RandfRange(-3f, 2f));
            var p2 = new Vector2(p0.X + dir * len, bcY + rng.RandfRange(1f, 5f));
            var p1 = new Vector2((p0.X + p2.X) * 0.5f + rng.RandfRange(-5f, 5f),
                (p0.Y + p2.Y) * 0.5f + rng.RandfRange(-3f, 3f));
            DrawLine(p0, p1, new Color(0.42f, 0.09f, 0.07f, 0.5f), 1.5f, true);
            DrawLine(p1, p2, new Color(0.42f, 0.09f, 0.07f, 0.5f), 1.5f, true);
            // 血丝末端的小圆点
            DrawCircle(p2, 1.8f + rng.Randf() * 1.5f,
                new Color(0.42f, 0.09f, 0.07f, 0.6f));
        }

        // 头部浓血：头部中弹血更集中（位置锚到真正的头部，不是 29px 的半途）
        DrawSetTransform(new Vector2(headX, headY), 0, new Vector2(1.5f, 0.5f));
        DrawCircle(Vector2.Zero, 9f, new Color(0.28f, 0.06f, 0.05f, 0.75f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    private void DrawTears()
    {
        float ang = _body.Rotation;
        // 破损碎片沿躯干分布（锚躯干中心，不是脚底原点）
        float bcX = Mathf.Sin(ang) * 54f;
        float bcY = Mathf.Cos(ang) * 54f;
        // 破损碎片：2-4 片不规则暗斑（3-6 个角，不是规整的锯齿）
        int tearCount = 2 + Mathf.FloorToInt(_seed * 3f);
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)(Mathf.Abs((int)(_body.Rotation * 1000)) + 1);

        for (int i = 0; i < tearCount; i++)
        {
            float t = rng.RandfRange(0.15f, 0.88f);
            float tx = bcX + Mathf.Sin(ang) * (t - 0.5f) * 92f;
            float ty = 2f + Mathf.Cos(ang) * (t - 0.5f) * 92f;
            float perpx = Mathf.Cos(ang) * rng.RandfRange(-16f, 16f);
            float perpy = -Mathf.Sin(ang) * rng.RandfRange(-16f, 16f);
            var tearCenter = new Vector2(tx + perpx, ty + perpy);

            int corners = 3 + (int)(rng.Randf() * 4f);
            float r = 7f + rng.RandfRange(-1.5f, 4f);
            var tear = new List<Vector2>();
            for (int c = 0; c < corners; c++)
            {
                float cornerAng = c / (float)corners * Mathf.Tau + rng.RandfRange(-0.35f, 0.35f);
                float jitter = 0.55f + rng.Randf() * 0.7f;
                tear.Add(tearCenter + new Vector2(Mathf.Cos(cornerAng) * r * jitter,
                    Mathf.Sin(cornerAng) * r * jitter));
            }
            DrawColoredPolygon(tear.ToArray(), new Color(0.22f, 0.04f, 0.03f, 0.85f));
            foreach (var p in tear)
            {
                float dirAng = p.AngleTo(tearCenter);
                var tip = p + new Vector2(Mathf.Cos(dirAng), Mathf.Sin(dirAng)) * 2f;
                DrawLine(p, tip, new Color(0.15f, 0.02f, 0.02f, 0.7f), 1f, true);
            }
        }

        // 头部破损
        if (rng.Randf() < 0.5f)
        {
            float headX = Mathf.Sin(ang) * 90f;
            float headY = Mathf.Cos(ang) * 90f + 3f;
            DrawSetTransform(new Vector2(headX, headY), 0, new Vector2(1f, 0.7f));
            DrawCircle(Vector2.Zero, 10f, new Color(0.18f, 0.03f, 0.02f, 0.55f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }
}
