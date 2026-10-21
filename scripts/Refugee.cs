using Godot;

namespace Witness;

/// <summary>
/// 难民 NPC。12 名在场（GDD §7），其中 6 位可深度交互（r1-r6，各有物资需求），
/// 其余为环境难民（缓慢游走，把眼神藏起来）。w1-w3 是 Beat2 门口的三位妇女（C2 事件）。
/// 需求徽记用洗褪的红画在头顶 —— 不做血条，沉默而具体。
/// </summary>
public partial class Refugee : Node2D
{
    public string Id;                       // r1..r6 / w1..w3 / 空 = 环境难民
    public string Need;                     // food / medicine / blanket / 空
    public Silhouette.Kind Kind;
    public bool Wander;                     // 环境难民缓慢游走

    private Vector2 _home;
    private Vector2 _target;
    private System.Random _rng = new();
    private float _retarget;

    public Refugee(string id, string need, Silhouette.Kind kind, bool wander = false)
    {
        Id = id;
        Need = need;
        Kind = kind;
        Wander = wander;
    }

    public override void _Ready()
    {
        _home = Position;
        _target = Position;
    }

    public bool Helped => Id != "" && GameState.I.S.Refugees.TryGetValue(Id, out var v) && v == 1;
    public bool Taken => Id != "" && GameState.I.S.Refugees.TryGetValue(Id, out var v) && v == -1;

    public override void _PhysicsProcess(double delta)
    {
        QueueRedraw(); // 徽记/帮助状态可能变化，保持重绘
        if (Taken) return;
        if (!Wander) return;
        float f = (float)delta;
        _retarget -= f;
        if (_retarget <= 0f)
        {
            _retarget = (float)(_rng.NextDouble() * 4.0 + 2.0);
            _target = _home + new Vector2((float)(_rng.NextDouble() - 0.5) * 90f,
                                           (float)(_rng.NextDouble() - 0.5) * 70f);
        }
        var to = _target - Position;
        if (to.Length() > 3f)
            Position += to.Normalized() * 26f * f;
    }

    public override void _Draw()
    {
        if (Taken) return; // 被带走的妇女不再出现在画面里 —— 留白比惩罚更重
        Silhouette.Draw(this, Kind);

        // 需求徽记：碗（食物）/ 十字（药品）/ 毯（毯子）
        if (Id != "" && Need != "" && !Helped)
        {
            var c = Silhouette.FadedRed;
            switch (Need)
            {
                case "food": // 一只碗
                    DrawArc(new Vector2(0, -40), 4.5f, 0.15f, Mathf.Pi - 0.15f, 12, c, 2.2f);
                    DrawLine(new Vector2(-4.5f, -40), new Vector2(4.5f, -40), c, 2.2f);
                    break;
                case "medicine": // 一道十字
                    DrawRect(new Rect2(-1.4f, -46, 2.8f, 12), c);
                    DrawRect(new Rect2(-6, -41.4f, 12, 2.8f), c);
                    break;
                case "blanket": // 一叠毯子
                    DrawRect(new Rect2(-6, -44, 12, 3), c);
                    DrawRect(new Rect2(-5, -40, 10, 3), c);
                    break;
            }
        }
    }
}
