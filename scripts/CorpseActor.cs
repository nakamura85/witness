using System.Collections.Generic;
using Godot;

namespace Witness;

/// <summary>
/// 尸体：SpriteActor 立绘 + 随机不规则姿势 + 多处不规则血迹（不整整齐齐的椭圆！）。
/// 
/// 姿势不再统一 90° 平躺——每具尸体有随机歪斜、身体各部位偏移，模拟中弹倒下的自然姿态。
/// 血迹不再是单个大椭圆——2~3 片不规则血塘 + 几处渗血小斑 + 向不同方向延伸的血丝。
/// 所有姿势随机性基于 RNG 种子，同一尸体每次 _Draw 结果一致（不闪烁）。
///
/// 尸体放在 GroundArt 之后、人物之前（ZIndex ≈ -5），不挡人物视线但留在弹坑/战壕/铁丝网附近。
/// </summary>
public partial class CorpseActor : Node2D
{
    public enum Faction { IJA, NortheastArmy, Refugee }

    private readonly Sprite2D _sp;
    private readonly Faction _faction;
    private readonly float _bodyRot;    // 身体主旋转角度（弧度），不再是固定 Pi/2
    private readonly float _spriteRot;  // 立绘自身额外旋转（弧度）
    private readonly float _spriteOffX; // 立绘水平偏移（模拟中弹侧歪）
    private readonly float _spriteOffY; // 立绘垂直偏移
    private readonly float _seed;

    /// <summary>
    /// 构造 CorpseActor。
    /// position: 尸体脚底/臀部位置（地面上）。
    /// facingRight: 头朝右 true / 头朝左 false。
    /// rotJitter: 额外旋转扰动（0~0.3 弧度 ≈ ±17°）。
    /// </summary>
    public CorpseActor(Faction faction, Vector2 position, bool facingRight, float rotJitter = -1f)
    {
        _faction = faction;
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        _seed = rng.Randf();

        // 身体主旋转角度：不是固定 90°，而是 55° ~ 125° 随机（躺着有侧歪）
        float baseAngle = facingRight ? Mathf.Pi / 2f : -Mathf.Pi / 2f;
        float bodyJitter = rng.RandfRange(-0.45f, 0.45f);  // ±26° 随机歪斜
        if (rotJitter < 0f) rotJitter = rng.RandfRange(-0.25f, 0.25f);
        _bodyRot = baseAngle + bodyJitter + rotJitter;

        // 立绘自身额外旋转 ±20°，让身体和头/腿有点错位（不是一个整齐的整体）
        _spriteRot = rng.RandfRange(-0.35f, 0.35f);

        // 立绘偏移（让身体中心不正好在 position 点上，模拟中弹后翻滚错位）
        _spriteOffX = rng.RandfRange(-6f, 6f);
        _spriteOffY = rng.RandfRange(-10f, -2f);

        string texName = faction switch
        {
            Faction.IJA => "soldier_ija",
            Faction.NortheastArmy => "soldier_na",
            _ => "refugee_man",
        };

        var tex = ChromaKey.Cutout($"res://assets/art/sprites/{texName}.jpg", 0.03f, 0.05f);
        if (tex == null) return;

        float height = faction == Faction.Refugee ? 96f : 108f;
        float s = height / tex.GetSize().Y;

        _sp = new Sprite2D
        {
            Texture = tex,
            Scale = new Vector2(s, s),
            Position = new Vector2(_spriteOffX, -height * 0.5f + _spriteOffY),
            Rotation = Mathf.Pi / 2f + _spriteRot,  // 基本躺平 + 额外歪斜
            FlipH = !facingRight,
        };
        AddChild(_sp);

        Position = position;
        Rotation = _bodyRot;
        ZIndex = 0;
        Modulate = new Color(1f, 0.92f, 0.90f, 1f);  // 尸体略偏冷

        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawBlood();
        DrawTears();
    }

    private void DrawBlood()
    {
        // 尸体头方向（血迹主要扩散方向）
        float headDir = _bodyRot;
        float headX = Mathf.Sin(headDir) * 36f;
        float headY = Mathf.Cos(headDir) * 36f;
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)Mathf.RoundToInt(_seed * 1000000f);

        // —— 主血塘：不规则多边形（不是椭圆！8-12 个点，边缘锯齿状）——
        int mainPts = 8 + Mathf.FloorToInt(rng.Randf() * 5f);
        float mainR = 24f + rng.RandfRange(-4f, 8f);
        var main = new List<Vector2>();
        for (int i = 0; i < mainPts; i++)
        {
            float ang = i / (float)mainPts * Mathf.Tau + rng.RandfRange(-0.18f, 0.18f);
            float jitter = 0.55f + rng.Randf() * 0.9f;  // 随机幅度：有的点凹进去有的凸出来
            float rx = mainR * jitter;
            float ry = mainR * 0.42f * jitter;
            float x = Mathf.Cos(ang) * rx + headX * 0.22f;
            float y = Mathf.Sin(ang) * ry + headY * 0.22f;
            main.Add(new Vector2(x, y));
        }
        DrawColoredPolygon(main.ToArray(), new Color(0.32f, 0.07f, 0.05f, 0.85f));

        // —— 渗血层：更大的半透明淡红（渗进雪里的感觉）——
        var bleed = new List<Vector2>();
        for (int i = 0; i < mainPts; i++)
        {
            float ang = i / (float)mainPts * Mathf.Tau + rng.RandfRange(-0.25f, 0.25f);
            float jitter = 0.7f + rng.Randf() * 0.8f;
            float rx = (mainR + 16f) * jitter;
            float ry = (mainR + 16f) * 0.48f * jitter;
            float x = Mathf.Cos(ang) * rx + headX * 0.28f;
            float y = Mathf.Sin(ang) * ry + headY * 0.28f;
            bleed.Add(new Vector2(x, y));
        }
        DrawColoredPolygon(bleed.ToArray(), new Color(0.50f, 0.13f, 0.09f, 0.22f));

        // —— 小血斑：尸体周围 2-4 个分散的小圆斑（子弹飞溅的血点）——
        int splatterCount = 2 + Mathf.FloorToInt(rng.Randf() * 3f);
        for (int i = 0; i < splatterCount; i++)
        {
            float ang = rng.RandfRange(0f, Mathf.Tau);
            float dist = rng.RandfRange(22f, 68f);
            float cx = Mathf.Cos(ang) * dist + headX * 0.15f;
            float cy = Mathf.Sin(ang) * dist;
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

        // —— 血丝：向不同方向延伸的细红线（3-5 条，长度不同）——
        int dripCount = 3 + Mathf.FloorToInt(rng.Randf() * 3f);
        for (int i = 0; i < dripCount; i++)
        {
            float spread = Mathf.Sin(i * 2.1f + _bodyRot * 3f) * 0.6f + rng.RandfRange(-0.3f, 0.3f);
            float baseX = Mathf.Sin(_bodyRot + spread) * 14f + rng.RandfRange(-6f, 6f);
            float baseY = Mathf.Cos(_bodyRot + spread) * 14f + rng.RandfRange(-6f, 6f);
            float len = 32f + rng.Randf() * 28f;
            float tipX = Mathf.Sin(_bodyRot + spread) * len;
            float tipY = Mathf.Cos(_bodyRot + spread) * (len * 0.45f);
            // 血丝不是直线——加一个中间控制点让它弯
            float ctrlX = baseX + (tipX - baseX) * 0.5f + rng.RandfRange(-4f, 4f);
            float ctrlY = baseY + (tipY - baseY) * 0.5f + rng.RandfRange(-4f, 4f);
            var dripLine = new List<Vector2>
            {
                new Vector2(baseX, baseY),
                new Vector2(ctrlX, ctrlY),
                new Vector2(tipX, tipY),
            };
            DrawLine(dripLine[0], dripLine[1], new Color(0.42f, 0.09f, 0.07f, 0.5f), 1.5f, true);
            DrawLine(dripLine[1], dripLine[2], new Color(0.42f, 0.09f, 0.07f, 0.5f), 1.5f, true);
            // 血丝末端的小圆点
            DrawCircle(dripLine[2], 1.8f + rng.Randf() * 1.5f,
                new Color(0.42f, 0.09f, 0.07f, 0.6f));
        }

        // 头附近浓血：头部中弹血更集中
        DrawSetTransform(new Vector2(headX * 0.8f, headY * 0.8f), _bodyRot, new Vector2(1.4f, 0.5f));
        DrawCircle(Vector2.Zero, 9f, new Color(0.28f, 0.06f, 0.05f, 0.75f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    private void DrawTears()
    {
        // 破损碎片：2-4 片不规则暗斑（3-6 个角，不是规整的锯齿）
        int tearCount = 2 + Mathf.FloorToInt(_seed * 3f);
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)(Mathf.Abs((int)(_bodyRot * 1000)) + 1);

        for (int i = 0; i < tearCount; i++)
        {
            float t = rng.RandfRange(0.15f, 0.88f);
            float bodyLen = 52f;
            float tx = Mathf.Sin(_bodyRot) * (t - 0.5f) * bodyLen;
            float ty = Mathf.Cos(_bodyRot) * (t - 0.5f) * bodyLen;
            float perpx = Mathf.Cos(_bodyRot) * rng.RandfRange(-16f, 16f);
            float perpy = -Mathf.Sin(_bodyRot) * rng.RandfRange(-16f, 16f);
            var tearCenter = new Vector2(tx + perpx, ty + perpy);

            int corners = 3 + (int)(rng.Randf() * 4f);
            float r = 7f + rng.RandfRange(-1.5f, 4f);
            var tear = new List<Vector2>();
            for (int c = 0; c < corners; c++)
            {
                float ang = c / (float)corners * Mathf.Tau + rng.RandfRange(-0.35f, 0.35f);
                float jitter = 0.55f + rng.Randf() * 0.7f;
                tear.Add(tearCenter + new Vector2(Mathf.Cos(ang) * r * jitter,
                    Mathf.Sin(ang) * r * jitter));
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
            float headX = Mathf.Sin(_bodyRot) * 50f;
            float headY = Mathf.Cos(_bodyRot) * 50f;
            DrawSetTransform(new Vector2(headX, headY), _bodyRot, new Vector2(1f, 0.7f));
            DrawCircle(Vector2.Zero, 10f, new Color(0.18f, 0.03f, 0.02f, 0.55f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }
}
