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
    private SpriteActor _actor;
    private float _phase;

    public Refugee(string id, string need, Silhouette.Kind kind, bool wander = false)
    {
        Id = id;
        Need = need;
        Kind = kind;
        Wander = wander;
    }

    /// <summary>旧 Silhouette.Kind → 手绘立绘 sprite 映射（v0.5 完整版）。</summary>
    private static SpriteActor.Kind MapKind(Silhouette.Kind k) => k switch
    {
        Silhouette.Kind.Vautrin => SpriteActor.Kind.Vautrin,
        Silhouette.Kind.Soldier => SpriteActor.Kind.Soldier,
        Silhouette.Kind.Fitch => SpriteActor.Kind.Fitch,
        Silhouette.Kind.FamilyAdult => SpriteActor.Kind.RefugeeMan,
        Silhouette.Kind.RefugeeChild or Silhouette.Kind.FamilyChild => SpriteActor.Kind.Child,
        _ => SpriteActor.Kind.RefugeeWoman,
    };

    public override void _Ready()
    {
        _home = Position;
        _targetX = Position.X;
        _actor = new SpriteActor(MapKind(Kind)) { Facing = Facing };
        AddChild(_actor);
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
        bool moving = Mathf.Abs(dx) > 4f;
        if (moving)
        {
            Position += new Vector2(Mathf.Sign(dx) * 26f * f, 0);
            Facing = Mathf.Sign(dx);
            _phase += f * 8f;
        }
        Position = new Vector2(Position.X, Level.GroundY(Position.X)); // 贴着主地面走
        _actor.Facing = Facing;
        _actor.Moving = moving;
        _actor.Phase = _phase;
    }

    public override void _Draw()
    {
        if (Taken) // 被带走的妇女不再出现在画面里 —— 留白比惩罚更重
        {
            if (_actor != null) _actor.Visible = false;
            return;
        }

        // 需求徽记：碗（食物）/ 十字（药品）/ 毯（毯子）—— 画在头顶，不随朝向翻转
        if (Id != "" && Need != "" && !Helped)
        {
            float h = (_actor != null ? _actor.Height : 96f) + 12f; // 立绘头顶上方
            var c = Silhouette.FadedRed;
            switch (Need)
            {
                case "food": // 一只碗
                    DrawArc(new Vector2(0, -h), 4.5f, 0.15f, Mathf.Pi - 0.15f, 12, c, 2.2f);
                    DrawLine(new Vector2(-4.5f, -h), new Vector2(4.5f, -h), c, 2.2f);
                    break;
                case "medicine": // 一道十字
                    DrawRect(new Rect2(-1.4f, -h - 6, 2.8f, 12), c);
                    DrawRect(new Rect2(-6, -h - 1.4f, 12, 2.8f), c);
                    break;
                case "blanket": // 一叠毯子
                    DrawRect(new Rect2(-6, -h - 4, 12, 3), c);
                    DrawRect(new Rect2(-5, -h, 10, 3), c);
                    break;
            }
        }
    }
}
