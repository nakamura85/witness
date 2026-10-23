using System.Collections.Generic;
using Godot;

namespace Witness;

/// <summary>
/// 尸体：SpriteActor 立绘旋转 90° 躺地 + 程序化血迹 + 衣物破损。
/// 
/// 不使用 .jpg 资产，全部程序化：
/// • 立绘复用 SpriteActor 的立绘（soldier_ija = 日军尸体，soldier_na = 东北军尸体），整体旋转 90°
/// • 血迹：暗红椭圆 + 不规则渗血边缘多边形 + 雪上渗入感渐变
/// • 衣物破损：暗色锯齿多边形叠加在躯干/大腿位置（表示子弹撕开的口子）
///
/// 尸体放在 GroundArt 之后、人物之前（ZIndex ≈ -5），不挡人物视线但留在弹坑/战壕/铁丝网附近。
/// 血迹只画在 Origin 一侧（尸体头/脚方向），模拟中弹倒下后的流血方向。
/// </summary>
public partial class CorpseActor : Node2D
{
    public enum Faction { IJA, NortheastArmy, Refugee }

    private readonly Sprite2D _sp;
    private readonly Faction _faction;
    private readonly float _rotation;   // 躺地方向（弧度，0 = 朝右，π = 朝左）

    /// <summary>
    /// 构造 CorpseActor。
    /// position: 尸体脚底/臀部位置（地面上）。
    /// facingRight: 头朝右 true / 头朝左 false。
    /// rotJitter: 额外旋转扰动（0~0.3 弧度 ≈ ±17°）。
    /// </summary>
    public CorpseActor(Faction faction, Vector2 position, bool facingRight, float rotJitter = -1f)
    {
        _faction = faction;
        float dir = facingRight ? 0f : Mathf.Pi;
        if (rotJitter < 0f)
        {
            // 随机歪斜（-17° ~ +17°）
            var rng = new RandomNumberGenerator();
            rng.Randomize();
            rotJitter = rng.RandfRange(-0.30f, 0.30f);
        }
        _rotation = dir + rotJitter;

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

        // 躺地：立绘本来是脚底在 y=0 往上长；旋转 90° 后立会变成水平。
        // 把立绘中心放在原点下方（脚底位置），这样尸体中心大约在 position。
        _sp = new Sprite2D
        {
            Texture = tex,
            Scale = new Vector2(s, s),
            Position = new Vector2(0, -height * 0.5f),
            Rotation = Mathf.Pi / 2f,  // 让原本竖直的立绘变成水平躺着
            FlipH = !facingRight,
        };
        AddChild(_sp);

        Position = position;
        Rotation = _rotation;
        ZIndex = -5;  // 在 GroundArt 之上、玩家之下
        Modulate = new Color(1f, 0.92f, 0.90f, 1f);  // 尸体略偏冷

        QueueRedraw();
    }

    public override void _Draw()
    {
        // —— 血迹（先画，在立绘下面）——
        DrawBlood();

        // —— 衣物破损（后画，在立绘上面叠加）——
        DrawTears();
    }

    private void DrawBlood()
    {
        // 尸体头的方向（也是血迹扩散方向）
        float headX = Mathf.Sin(_rotation) * 30f;
        float headY = Mathf.Cos(_rotation) * 30f;

        // 主血泊：暗红椭圆 + 不规则边缘
        var main = new List<Vector2>();
        int points = 14;
        float baseR = 32f + (_faction == Faction.IJA ? 4f : 0f);
        for (int i = 0; i < points; i++)
        {
            float ang = i / (float)points * Mathf.Tau;
            float jitter = 1f + Mathf.Sin(i * 2.7f + _rotation * 3f) * 0.18f;
            float rx = baseR * jitter;
            float ry = baseR * 0.45f * jitter;
            float x = Mathf.Cos(ang) * rx;
            float y = Mathf.Sin(ang) * ry;
            // 血迹向头部方向拉长
            x += headX * 0.22f;
            main.Add(new Vector2(x, y));
        }
        // 主血（干透的暗红）
        DrawColoredPolygon(main.ToArray(), new Color(0.35f, 0.08f, 0.06f, 0.85f));

        // 渗血层：更大的半透明淡红（让血看起来渗进雪里）
        var bleed = new List<Vector2>();
        for (int i = 0; i < points; i++)
        {
            float ang = i / (float)points * Mathf.Tau;
            float jitter = 1f + Mathf.Sin(i * 1.9f + _rotation * 5f) * 0.28f;
            float rx = (baseR + 14f) * jitter;
            float ry = (baseR + 14f) * 0.5f * jitter;
            float x = Mathf.Cos(ang) * rx;
            float y = Mathf.Sin(ang) * ry;
            x += headX * 0.28f;
            bleed.Add(new Vector2(x, y));
        }
        DrawColoredPolygon(bleed.ToArray(), new Color(0.52f, 0.14f, 0.10f, 0.25f));

        // 向雪地渗入的细血丝（3~4 条细线，从主血向远处延伸）
        int drips = 3 + Mathf.FloorToInt(Mathf.Abs(Mathf.Sin(_rotation * 7f)) * 2f);
        for (int i = 0; i < drips; i++)
        {
            float spread = Mathf.Sin(i * 2.1f + _rotation * 3f) * 0.6f;
            var drip = new[]
            {
                new Vector2(Mathf.Sin(_rotation + spread) * 14f, Mathf.Cos(_rotation + spread) * 14f),
                new Vector2(Mathf.Sin(_rotation + spread) * (42f + i * 6f),
                    Mathf.Cos(_rotation + spread) * (12f + i * 5f)),
            };
            DrawLine(drip[0], drip[1], new Color(0.45f, 0.10f, 0.08f, 0.5f), 1.6f, true);
            // 血丝末端的小圆点
            DrawCircle(drip[1], 1.8f, new Color(0.45f, 0.10f, 0.08f, 0.6f));
        }

        // 头附近的浓血（头部中弹血更集中）
        DrawSetTransform(new Vector2(headX * 0.8f, headY * 0.8f), _rotation, new Vector2(1.4f, 0.5f));
        DrawCircle(Vector2.Zero, 8f, new Color(0.28f, 0.06f, 0.05f, 0.75f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    private void DrawTears()
    {
        // 破损碎片：随机位置的暗色锯齿多边形（2-4 片，每片 3-5 个点）
        int tearCount = 2 + Mathf.FloorToInt(Mathf.Abs(Mathf.Sin(_rotation * 9f)) * 2f);
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)(Mathf.Abs((int)(_rotation * 1000)) + 1);

        for (int i = 0; i < tearCount; i++)
        {
            // 破损位置：沿身体随机偏移（身体是水平躺着的，所以沿着 rotation 方向找位置）
            float t = rng.RandfRange(0.2f, 0.9f);  // 沿着身体（头→脚）的位置
            float bodyLen = 52f;                    // 身体总长度一半
            float tx = Mathf.Sin(_rotation) * (t - 0.5f) * bodyLen;
            float ty = Mathf.Cos(_rotation) * (t - 0.5f) * bodyLen;
            // 破损离身体中心的偏移（垂直方向，模拟在胸前/腿上/手臂）
            float perpx = Mathf.Cos(_rotation) * rng.RandfRange(-16f, 16f);
            float perpy = -Mathf.Sin(_rotation) * rng.RandfRange(-16f, 16f);
            var tearCenter = new Vector2(tx + perpx, ty + perpy);

            int corners = 3 + (int)(rng.Randf() * 3f);  // 3~5 个角
            float r = 7f + rng.RandfRange(-1.5f, 4f);
            var tear = new List<Vector2>();
            for (int c = 0; c < corners; c++)
            {
                float ang = c / (float)corners * Mathf.Tau + rng.RandfRange(-0.3f, 0.3f);
                float jitter = r * (0.6f + rng.Randf() * 0.6f);
                tear.Add(tearCenter + new Vector2(Mathf.Cos(ang) * jitter, Mathf.Sin(ang) * jitter));
            }
            // 破损颜色：深褐红（像破布+血混在一起的颜色）
            DrawColoredPolygon(tear.ToArray(), new Color(0.22f, 0.04f, 0.03f, 0.85f));
            // 破损边缘的小毛边（撕裂纤维）
            foreach (var p in tear)
            {
                float dirAng = p.AngleTo(tearCenter);
                var tip = p + new Vector2(Mathf.Cos(dirAng), Mathf.Sin(dirAng)) * 2f;
                DrawLine(p, tip, new Color(0.15f, 0.02f, 0.02f, 0.7f), 1f, true);
            }
        }

        // 头部破损（如果是头部中弹，头周围加一片暗斑）
        if (rng.Randf() < 0.5f)
        {
            float headX = Mathf.Sin(_rotation) * 50f;
            float headY = Mathf.Cos(_rotation) * 50f;
            DrawSetTransform(new Vector2(headX, headY), _rotation, new Vector2(1f, 0.7f));
            DrawCircle(Vector2.Zero, 10f, new Color(0.18f, 0.03f, 0.02f, 0.55f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }
}
