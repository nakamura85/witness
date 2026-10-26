using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Witness;

/// <summary>
/// 尸体：纯代码按人体结构绘制的雪地躺姿（弃用 AI 立绘横摆——立绘本身不是躺姿人体，
/// 横过来放只会读成一坨色块，完全没有人体的肩/髋/四肢比例）。
/// 三种史实化姿势（依据 1931 江桥抗战史实照片）：
/// - 冲锋中弹 → 向前扑倒（俯卧 prone，一腿蹬直一腿半屈、一臂前伸）
/// - 站立中弹 → 向后倒下（仰卧 supine，双腿自然分开、手臂摊开）
/// - 侧卧极少（约 5%，双腿叠放微屈）
/// 下半身半陷进雪里（雪脊覆盖脚部），血迹单侧渗透。
/// 所有随机多边形先经 Star() 角度排序保证单边形——自交多边形三角化失败
/// 会忽隐忽现，正是画面闪白的元凶之一。
/// </summary>
public partial class CorpseActor : Node2D
{
    public enum Faction { IJA, NortheastArmy, Refugee }
    private enum Pose { Prone, Supine, SideLying }

    private readonly Node2D _body;      // 只用它的 Rotation 承载躺倒角度
    private readonly Faction _faction;
    private readonly Pose _pose;
    private readonly float _seed;

    public CorpseActor(Faction faction, Vector2 position, bool facingRight, float rotJitter = -1f)
    {
        _faction = faction;
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        _seed = rng.Randf();

        float r = rng.Randf();
        if (r < 0.70f) _pose = Pose.Prone;
        else if (r < 0.95f) _pose = Pose.Supine;
        else _pose = Pose.SideLying;

        // 头朝倒下方向 + ±15° 轻微歪斜（中弹瞬间的自然反应，不乱转）
        float headAngle = facingRight ? Mathf.Pi / 2f : -Mathf.Pi / 2f;
        headAngle += rng.RandfRange(-0.26f, 0.26f);

        _body = new Node2D { Rotation = headAngle };
        AddChild(_body);

        Position = position;
        ZIndex = 0;
        Modulate = new Color(1f, 0.94f, 0.92f, 1f);
        QueueRedraw();
    }

    /// <summary>body-space（u=沿身体从脚到头，v=垂直方向）→ 节点局部坐标。</summary>
    private Vector2 V(float ang, float u, float v)
        => new(Mathf.Sin(ang) * u + Mathf.Cos(ang) * v,
               Mathf.Cos(ang) * u - Mathf.Sin(ang) * v);

    /// <summary>
    /// 把任意点集按质心角度排序成"星形多边形"——结果一定是单边形，
    /// DrawColoredPolygon 三角化永不失败（闪烁修复）。
    /// </summary>
    private static Vector2[] Star(List<Vector2> pts)
    {
        if (pts.Count < 3) return pts.ToArray();
        var c = Vector2.Zero;
        foreach (var p in pts) c += p;
        c /= pts.Count;
        return pts.OrderBy(p => (p - c).Angle()).ToArray();
    }

    public override void _Draw()
    {
        float ang = _body.Rotation;
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)Mathf.RoundToInt(_seed * 1000000f);

        DrawContactShadow(ang);
        DrawBody(ang, rng);
        DrawDroppedRifle();
        DrawBlood(ang, rng);
        DrawTears(ang);
    }

    private (Color coat, Color pants, Color capC, Color skin) Palette()
        => _faction switch
        {
            Faction.IJA => (new Color(0.72f, 0.60f, 0.30f), new Color(0.55f, 0.46f, 0.24f),
                            new Color(0.42f, 0.35f, 0.18f), new Color(0.80f, 0.64f, 0.52f)),
            Faction.NortheastArmy => (new Color(0.44f, 0.49f, 0.56f), new Color(0.34f, 0.38f, 0.45f),
                            new Color(0.27f, 0.30f, 0.36f), new Color(0.80f, 0.64f, 0.52f)),
            _ => (new Color(0.52f, 0.46f, 0.38f), new Color(0.40f, 0.35f, 0.28f),
                  new Color(0.30f, 0.26f, 0.22f), new Color(0.80f, 0.64f, 0.52f)),
        };

    private void DrawBody(float ang, RandomNumberGenerator rng)
    {
        var (coat, pants, capC, skin) = Palette();
        var boot = new Color(0.20f, 0.17f, 0.14f);
        var coatDark = new Color(coat.R * 0.82f, coat.G * 0.82f, coat.B * 0.82f);

        // —— 腿（髋 u≈46 → 脚 u≈0）：先画远侧腿（暗一档），再画近侧腿 ——
        (float foot, float kneeU, float kneeV)[] legs = _pose switch
        {
            // 俯卧：一腿蹬直、一腿半屈（跑步扑倒的惯性）
            Pose.Prone => new[] { (rng.RandfRange(-2f, 3f), 24f, 6f), (rng.RandfRange(10f, 16f), 28f, -6f) },
            // 仰卧：双腿自然分开伸直
            Pose.Supine => new[] { (rng.RandfRange(-3f, 2f), 24f, 8f), (rng.RandfRange(0f, 5f), 24f, -8f) },
            // 侧卧：双腿叠放微屈
            _ => new[] { (rng.RandfRange(2f, 8f), 22f, 3f), (rng.RandfRange(6f, 12f), 24f, -3f) },
        };

        for (int i = 0; i < legs.Length; i++)
        {
            var (foot, kneeU, kneeV) = legs[i];
            var c = i == 0 ? coatDark : pants;   // 远侧腿吃阴影
            var hip = V(ang, 46f, i == 0 ? 4f : -4f);
            var knee = V(ang, kneeU, kneeV);
            var ft = V(ang, foot, i == 0 ? 6f : -6f);
            DrawLine(hip, knee, c, 7.5f, true);
            DrawLine(knee, ft, c, 6.5f, true);
            DrawCircle(ft, 4.2f, boot);          // 靴子
        }

        // —— 躯干（髋 u46 → 肩 u88）：大衣躯干，圆肩圆臀 ——
        var hipC = V(ang, 46f, 0f);
        var shoulder = V(ang, 88f, 0f);
        DrawLine(hipC, shoulder, coat, 16f, true);
        DrawCircle(hipC, 9.5f, coat);            // 臀/大衣下摆
        DrawCircle(shoulder, 8.5f, coat);        // 肩

        // 大衣下摆盖住大腿根（一片短裙形，人体着装的自然垂落）
        var hem = new List<Vector2>();
        for (int k = 0; k < 6; k++)
        {
            float t = k / 5f;
            hem.Add(V(ang, 46f - t * 8f, Mathf.Lerp(-11f, 11f, t)));
        }
        hem.Add(V(ang, 52f, 8f));
        hem.Add(V(ang, 52f, -8f));
        DrawColoredPolygon(Star(hem), coatDark);

        // —— 手臂：肩 u86 出发，按姿势摆 ——
        (float elbowU, float elbowV, float handU, float handV) arm1, arm2;
        switch (_pose)
        {
            case Pose.Prone:     // 一臂前伸抓地、一臂压在胸前
                arm1 = (104f, 10f, 122f, 6f);
                arm2 = (78f, -12f, 64f, -13f);
                break;
            case Pose.Supine:    // 双臂摊在身体两侧
                arm1 = (92f, 16f, 84f, 24f);
                arm2 = (92f, -16f, 100f, -22f);
                break;
            default:             // 侧卧：上臂搭在身侧、下臂垫在头下
                arm1 = (94f, 12f, 102f, 16f);
                arm2 = (96f, -8f, 104f, -4f);
                break;
        }
        foreach (var a in new[] { arm1, arm2 })
        {
            var s0 = V(ang, 86f, (a.elbowV > 0f ? 1 : -1) * 5f);
            var el = V(ang, a.elbowU, a.elbowV);
            var hd = V(ang, a.handU, a.handV);
            DrawLine(s0, el, coat, 5.5f, true);
            DrawLine(el, hd, coat, 4.8f, true);
            DrawCircle(hd, 3.2f, skin);          // 手
        }

        // —— 头（u≈98）+ 军帽/棉帽 ——
        var headC = V(ang, 98f, 2f);
        DrawCircle(headC, 7.8f, skin);                       // 脸
        var capTop = V(ang, 100f, -4f);
        DrawCircle(capTop, 7.2f, capC);                      // 帽顶
        DrawLine(V(ang, 92f, -5f), V(ang, 100f, -8f), capC, 3.5f, true); // 帽檐

        // —— 下半身雪脊：脚下半截埋进雪里（人在雪地躺了一段时间） ——
        var drift = V(ang, 12f, 0f);
        DrawSetTransform(drift, Mathf.Pi / 2f - ang, new Vector2(2.1f, 0.55f));
        DrawCircle(Vector2.Zero, 15f, new Color(0.90f, 0.93f, 0.94f, 0.92f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    /// <summary>
    /// 接触阴影：沿身体方向的压扁椭圆压在雪面上——尸体是「躺」在雪地里，
    /// 不是浮在雪面上（与其他物件的 GroundShadow 同一套视觉语言）。
    /// </summary>
    private void DrawContactShadow(float ang)
    {
        var center = V(ang, 54f, 2f) + new Vector2(-7f, 3f);
        DrawSetTransform(center, Mathf.Pi / 2f - ang, new Vector2(1.9f, 0.30f));
        DrawCircle(Vector2.Zero, 52f, new Color(0.10f, 0.09f, 0.08f, 0.20f));
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

    private void DrawBlood(float ang, RandomNumberGenerator rng)
    {
        // 锚点：躯干中心。身体全长约 106、脚底在原点，躯干中心在沿身 54px 处
        float bcX = Mathf.Sin(ang) * 54f;
        float bcY = Mathf.Cos(ang) * 54f + 4f;    // +4 沉进雪面
        // 头部位置（沿身 ~98px），头部中弹的浓血集中在这里
        float headX = Mathf.Sin(ang) * 98f;
        float headY = Mathf.Cos(ang) * 98f + 3f;

        // 血只会朝一个方向渗（地势低的一侧），绝不环绕尸体一圈——
        // 对称的红色光环是「贴纸感」的根源。
        float flow = rng.Randf() > 0.5f ? 1f : -1f;
        var blood = new Color(0.30f, 0.06f, 0.05f);
        var bloodMid = new Color(0.36f, 0.08f, 0.06f);

        // —— 主血塘：藏在身下、朝 flow 侧拖出一条「舌形」——
        int mainPts = 10 + Mathf.FloorToInt(rng.Randf() * 4f);
        float mainLen = 34f + rng.Randf() * 16f;      // 朝流向的延伸半径
        float mainWide = 15f + rng.Randf() * 6f;      // 横向最大半宽
        var main = new List<Vector2>();
        for (int i = 0; i < mainPts; i++)
        {
            float a = i / (float)mainPts * Mathf.Tau + rng.RandfRange(-0.15f, 0.15f);
            float ca = Mathf.Cos(a);   // 沿流向分量（带符号）
            float sa = Mathf.Sin(a);   // 横向分量
            float stretch = ca * flow > 0f ? 1f : 0.40f;          // 顺流长、逆流短
            float jitter = 0.72f + rng.Randf() * 0.56f;
            float rx = mainLen * stretch * jitter;
            float ry = mainWide * (0.45f + 0.55f * (1f - Mathf.Abs(ca))) * jitter;
            main.Add(new Vector2(bcX + ca * rx, bcY + sa * ry));
        }
        DrawColoredPolygon(Star(main), new Color(blood, 0.88f));

        // —— 渗血层：顺流再拖远一截的淡红（血渗进雪里扩散），同样单侧 ——
        var bleed = new List<Vector2>();
        for (int i = 0; i < mainPts; i++)
        {
            float a = i / (float)mainPts * Mathf.Tau + rng.RandfRange(-0.22f, 0.22f);
            float ca = Mathf.Cos(a);
            float sa = Mathf.Sin(a);
            float stretch = ca * flow > 0f ? 1.25f : 0.45f;
            float jitter = 0.75f + rng.Randf() * 0.5f;
            float rx = (mainLen + 16f) * stretch * jitter;
            float ry = (mainWide + 7f) * (0.4f + 0.6f * (1f - Mathf.Abs(ca))) * jitter;
            bleed.Add(new Vector2(bcX + flow * mainLen * 0.30f + ca * rx, bcY + sa * ry));
        }
        DrawColoredPolygon(Star(bleed), new Color(0.48f, 0.12f, 0.08f, 0.16f));

        // —— 小血斑：绝大多数落在顺流侧的血塘外围，少量贴近身体另一侧 ——
        int splatterCount = 3 + Mathf.FloorToInt(rng.Randf() * 3f);
        for (int i = 0; i < splatterCount; i++)
        {
            bool farSide = i < splatterCount - 1;   // 最后一个留给逆流侧
            float cx = bcX + flow * rng.RandfRange(mainLen * 0.85f, mainLen * 1.7f);
            if (!farSide) cx = bcX - flow * rng.RandfRange(18f, 34f);
            float cy = bcY + rng.RandfRange(-9f, 9f);
            float r = 1.6f + rng.Randf() * 3.4f;    // 离主体越远越小
            int sPts = 4 + Mathf.FloorToInt(rng.Randf() * 3f);
            var spot = new List<Vector2>();
            for (int k = 0; k < sPts; k++)
            {
                float sAng = k / (float)sPts * Mathf.Tau + rng.RandfRange(-0.3f, 0.3f);
                float sJit = 0.55f + rng.Randf() * 0.7f;
                spot.Add(new Vector2(cx + Mathf.Cos(sAng) * r * sJit,
                    cy + Mathf.Sin(sAng) * r * sJit));
            }
            DrawColoredPolygon(Star(spot), new Color(bloodMid, 0.6f));
        }

        // —— 血丝：贴雪面顺流而行的锥形细流（根部粗、末端细，不带圆点收尾）——
        int dripCount = 2 + Mathf.FloorToInt(rng.Randf() * 2f);
        for (int i = 0; i < dripCount; i++)
        {
            float len = 26f + rng.Randf() * 34f;
            float y0 = bcY + rng.RandfRange(-4f, 4f);
            float x0 = bcX + flow * rng.RandfRange(mainLen * 0.4f, mainLen * 0.7f);
            int steps = 4;
            var path = new Vector2[steps + 1];
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                path[s] = new Vector2(
                    x0 + flow * len * t,
                    y0 + Mathf.Sin(t * Mathf.Pi + i * 2.1f) * 3f + t * rng.RandfRange(-1f, 3f));
            }
            for (int s = 0; s < steps; s++)
                DrawLine(path[s], path[s + 1], new Color(bloodMid, 0.55f),
                    2.2f * (1f - s / (float)steps) + 0.5f, true);
        }

        // 头部浓血：不规则小血塘压在头位下方、顺流偏一点
        DrawSetTransform(new Vector2(headX + flow * 4f, headY), 0, new Vector2(1.5f, 0.5f));
        int headPts = 8;
        var headPool = new List<Vector2>();
        for (int i = 0; i < headPts; i++)
        {
            float a = i / (float)headPts * Mathf.Tau + rng.RandfRange(-0.2f, 0.2f);
            float r = 8f * (0.6f + rng.Randf() * 0.7f);
            headPool.Add(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r));
        }
        DrawColoredPolygon(Star(headPool), new Color(0.26f, 0.05f, 0.04f, 0.7f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    private void DrawTears(float ang)
    {
        // 破损碎片沿躯干分布（锚躯干中心，不是脚底原点）
        float bcX = Mathf.Sin(ang) * 54f;
        float bcY = Mathf.Cos(ang) * 54f;
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
            DrawColoredPolygon(Star(tear), new Color(0.22f, 0.04f, 0.03f, 0.85f));
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
            float headX = Mathf.Sin(ang) * 98f;
            float headY = Mathf.Cos(ang) * 98f + 3f;
            DrawSetTransform(new Vector2(headX, headY), 0, new Vector2(1f, 0.7f));
            DrawCircle(Vector2.Zero, 10f, new Color(0.18f, 0.03f, 0.02f, 0.55f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }
}
