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
    private readonly Faction _faction;
    private readonly Pose _pose;
    private readonly float _bodyRot;     // 身体主旋转角度（弧度）
    private readonly float _spriteRot;   // 立绘自身额外旋转（弧度，轻微歪斜）
    private readonly float _spriteOffX;  // 立绘水平偏移
    private readonly float _spriteOffY;  // 立绘垂直偏移
    private readonly bool _flipV;        // 是否上下翻转（俯卧时需要）
    private readonly float _seed;

    /// <summary>
    /// 构造 CorpseActor。
    /// position: 尸体腰部/臀部位置（地面上）。
    /// facingRight: 冲锋朝向（中弹前的运动方向）。
    /// </summary>
    public CorpseActor(Faction faction, Vector2 position, bool facingRight, float rotJitter = -1f)
    {
        _faction = faction;
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        _seed = rng.Randf();

        // —— 基于冲锋中弹方向决定倒下姿势（史实化）——
        float r = rng.Randf();
        if (r < 0.70f)
        {
            // 70%：冲锋中弹向前扑倒 → 俯卧 face-down
            _pose = Pose.Prone;
            _bodyRot = facingRight ? Mathf.Pi / 2f : -Mathf.Pi / 2f; // 基本躺平
            _flipV = true;                                             // 立绘上下翻转（头朝下）
        }
        else if (r < 0.95f)
        {
            // 25%：站立中弹向后倒 → 仰卧 face-up
            _pose = Pose.Supine;
            _bodyRot = facingRight ? Mathf.Pi / 2f : -Mathf.Pi / 2f; // 正常躺平
            _flipV = false;
        }
        else
        {
            // 5%：侧面中弹侧卧
            _pose = Pose.SideLying;
            _bodyRot = (facingRight ? Mathf.Pi / 2f : -Mathf.Pi / 2f) 
                       + rng.RandfRange(-0.25f, 0.25f);
            _flipV = false;
        }

        // 所有姿势只加轻微 ±15° 歪斜（模拟中弹瞬间的重心偏移）
        _bodyRot += rng.RandfRange(-0.25f, 0.25f);  // ±15°

        // 立绘自身也加轻微歪斜 ±10°
        _spriteRot = rng.RandfRange(-0.18f, 0.18f);

        // 立绘轻微偏移（身体不是机械地正好压在 position 上）
        _spriteOffX = rng.RandfRange(-4f, 4f);
        _spriteOffY = rng.RandfRange(-6f, 0f);

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
            Rotation = Mathf.Pi / 2f + _spriteRot,
            FlipH = !facingRight,
            FlipV = _flipV,
        };
        AddChild(_sp);

        Position = position;
        Rotation = _bodyRot;
        ZIndex = 0;
        Modulate = new Color(1f, 0.92f, 0.90f, 1f);  // 尸体略偏冷色

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
