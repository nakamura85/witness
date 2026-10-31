using Godot;

namespace Witness;

/// <summary>
/// 证据点：一张等待被记录的纸页。
/// 按住 E 约 1.2 秒完成记录（进度环画在 HUD）；已记录的显示墨迹勾，
/// 被销毁的显示一道深墨叉 —— 沉默地呈现损失（GDD：不做成惩罚动画）。
/// </summary>
public partial class EvidencePoint : Node2D
{
    public string Id;
    public bool Active; // 是否在本 Beat 可见/可记录

    private float _t;

    public EvidencePoint(string id)
    {
        Id = id;
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (Active) QueueRedraw();
    }

    public override void _Draw()
    {
        var st = (EvidenceStatus)GameState.I.S.Evidence[Id];
        if (!Active && st == EvidenceStatus.Uncollected)
            return; // 还没到这一幕，纸页尚未出现

        var paper = new Color(0.93f, 0.89f, 0.78f);
        var ink = new Color(0.16f, 0.14f, 0.12f, 0.85f);

        float tilt = 0.12f;
        if (st == EvidenceStatus.Uncollected)
        {
            // 呼吸微光：柔和圆形光晕（取代方块发光，不再像调试方块 / #3、#7）
            float pulse = 0.5f + 0.5f * Mathf.Sin(_t * 2.4f);
            DrawCircle(Vector2.Zero, 16f + pulse * 2f,
                new Color(0.93f, 0.89f, 0.78f, 0.12f + 0.07f * pulse));
        }

        // 纸页：带轻微倾斜的小四边形，读作随手放在雪里的便条，而非方块
        var page = new[]
        {
            new Vector2(-13, -17).Rotated(tilt),
            new Vector2(13, -19).Rotated(tilt),
            new Vector2(15, 17).Rotated(tilt),
            new Vector2(-11, 19).Rotated(tilt),
        };
        DrawColoredPolygon(page, st == EvidenceStatus.Destroyed
            ? new Color(0.45f, 0.42f, 0.38f, 0.9f) : paper);
        DrawPolyline(page, new Color(0.35f, 0.29f, 0.22f, 0.9f), 1.2f, true);

        // 纸上的字迹线条（随便条一起倾斜）
        var line = st == EvidenceStatus.Destroyed ? new Color(0.3f, 0.28f, 0.26f, 0.6f) : ink;
        for (int i = 0; i < 5; i++)
        {
            var a = new Vector2(-9, -11 + i * 5.5f).Rotated(tilt);
            var b = new Vector2(9, -11 + i * 5.5f).Rotated(tilt);
            DrawLine(a, b, line, 1.2f);
        }

        // 状态标记
        if (st == EvidenceStatus.Complete || st == EvidenceStatus.Carried)
        {
            DrawLine(new Vector2(-7, 8), new Vector2(-2, 13), Silhouette.FadedRed, 2.4f);
            DrawLine(new Vector2(-2, 13), new Vector2(7, -2), Silhouette.FadedRed, 2.4f);
        }
        else if (st == EvidenceStatus.Torn)
        {
            // 残缺：一道撕裂的斜痕
            DrawLine(new Vector2(-10, -16), new Vector2(10, 14), new Color(0.45f, 0.3f, 0.25f, 0.95f), 3f);
        }
        else if (st == EvidenceStatus.Destroyed)
        {
            DrawLine(new Vector2(-9, -12), new Vector2(9, 12), new Color(0.12f, 0.1f, 0.09f, 0.95f), 3f);
            DrawLine(new Vector2(9, -12), new Vector2(-9, 12), new Color(0.12f, 0.1f, 0.09f, 0.95f), 3f);
        }
    }
}
