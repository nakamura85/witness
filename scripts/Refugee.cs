using Godot;

namespace Witness;

/// <summary>
/// 难民 NPC（v0.3 横版侧视）。12 名在场（GDD §7），其中 6 位可深度交互（r1-r6），
/// 其余为环境难民（沿地面缓慢游走，把眼神藏起来）。w1-w3 是 Beat2 门口的三位妇女。
/// 需求徽记用洗褪的红画在头顶 —— 不做血条，沉默而具体。
/// </summary>
public partial class Refugee : Node2D
{
    public string Id;                       // r1..r6 / w1..w3 / 空 = 环境难民
    public string Need;                     // food / medicine / blanket / 空
    public Silhouette.Kind Kind;
    public bool Wander;                     // 环境难民缓慢游走
    public float Facing = -1f;              // 默认面向左（玩家通常从左侧来）

    private Vector2 _home;
    private float _targetX;
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
        _targetX = Position.X;
    }

    public bool Helped => Id != "" && GameState.I.S.Refugees.TryGetValue(Id, out var v) && v == 1;
    public bool Taken => Id != "" && GameState.I.S.Refugees.TryGetValue(Id, out var v) && v == -1;

    public override void _PhysicsProcess(double delta)
    {
        QueueRedraw(); // 徽记/帮助状态可能变化，保持重绘
        if (Taken || !Wander) return;
        float f = (float)delta;
        _retarget -= f;
        if (_retarget <= 0f)
        {
            _retarget = (float)(_rng.NextDouble() * 4.0 + 2.0);
            _targetX = _home.X + (float)(_rng.NextDouble() - 0.5) * 150f;
        }
        float dx = _targetX - Position.X;
        if (Mathf.Abs(dx) > 4f)
        {
            Position += new Vector2(Mathf.Sign(dx) * 26f * f, 0);
            Facing = Mathf.Sign(dx);
        }
        Position = new Vector2(Position.X, Level.GroundY(Position.X)); // 贴着主地面走
    }

    public override void _Draw()
    {
        if (Taken) return; // 被带走的妇女不再出现在画面里 —— 留白比惩罚更重

        DrawSetTransform(Vector2.Zero, 0, new Vector2(Facing, 1));
        SilhouetteSide.Draw(this, Kind);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        // 需求徽记：碗（食物）/ 十字（药品）/ 毯（毯子）—— 画在头顶，不随朝向翻转
        if (Id != "" && Need != "" && !Helped)
        {
            var c = Silhouette.FadedRed;
            switch (Need)
            {
                case "food": // 一只碗
                    DrawArc(new Vector2(0, -52), 4.5f, 0.15f, Mathf.Pi - 0.15f, 12, c, 2.2f);
                    DrawLine(new Vector2(-4.5f, -52), new Vector2(4.5f, -52), c, 2.2f);
                    break;
                case "medicine": // 一道十字
                    DrawRect(new Rect2(-1.4f, -58, 2.8f, 12), c);
                    DrawRect(new Rect2(-6, -53.4f, 12, 2.8f), c);
                    break;
                case "blanket": // 一叠毯子
                    DrawRect(new Rect2(-6, -56, 12, 3), c);
                    DrawRect(new Rect2(-5, -52, 10, 3), c);
                    break;
            }
        }
    }
}
