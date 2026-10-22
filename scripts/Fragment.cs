using Godot;

namespace Witness;

/// <summary>
/// 史料碎片：散落在档案馆工作间里的发光纸片（教学关可拾取物）。
/// 拾取后消失；教学浮字引导玩家第一次走近它。
/// </summary>
public partial class Fragment : Node2D
{
    public string Id;
    public bool Taken;

    private float _t;

    public Fragment(string id) { Id = id; }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (!Taken) QueueRedraw();
    }

    public override void _Draw()
    {
        if (Taken) return;

        var paper = new Color(0.93f, 0.89f, 0.78f);
        var ink = new Color(0.16f, 0.14f, 0.12f, 0.85f);

        // 呼吸微光：与南京关的「该记录的东西」同一种视觉语言
        float pulse = 0.5f + 0.5f * Mathf.Sin(_t * 2.4f);
        DrawRect(new Rect2(-18, -22, 36, 44), new Color(0.93f, 0.89f, 0.78f, 0.28f + 0.35f * pulse));

        // 一张折过的小纸片
        DrawRect(new Rect2(-15, -19, 30, 38), paper);
        DrawRect(new Rect2(-15, -19, 30, 8), new Color(0.86f, 0.81f, 0.68f));
        for (int i = 0; i < 4; i++)
            DrawLine(new Vector2(-10, -4 + i * 6f), new Vector2(10, -4 + i * 6f), ink, 1.2f);
    }
}
