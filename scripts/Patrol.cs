using Godot;

namespace Witness;

/// <summary>
/// 巡逻兵（压迫性系统，不是可战斗的敌人 —— 呈现伦理铁律）。
/// 沿路径点巡逻 + 视野锥侦测：
/// - 玩家在锥内且无墙体遮挡 → 曝露值上升（屏息减速）；
/// - 曝露满 → 发出 SpottedPlayer 信号（Level 决定后果：没收证据 / 被押送）。
/// </summary>
public partial class Patrol : Node2D
{
    [Signal] public delegate void SpottedPlayerEventHandler();

    public float Aggression = 1.0f;   // C2 选择「上前阻挡」后提升（巡逻记住了你的脸）
    public bool SeeingPlayer { get; private set; }

    private Level _level;
    private Player _player;
    private Vector2[] _points;
    private int _idx = 1;
    private float _wait;
    private float _baseFacing;
    private float _scan;
    private float _exposure;
    private float _t;

    private const float Range = 280f;      // 视野距离
    private const float HalfAngle = 0.55f; // 视野半角（弧度）
    private const float Speed = 70f;

    public void Setup(Level level, Player player, Vector2[] points)
    {
        _level = level;
        _player = player;
        _points = points;
        Position = points[0];
        _baseFacing = (points[1] - points[0]).Angle();
    }

    public override void _PhysicsProcess(double delta)
    {
        float f = (float)delta;
        _t += f;
        if (_points == null || _level == null || !_level.PlayPhase)
            return;

        // ---- 沿路径点移动，停留时左右扫视 ----
        if (_wait > 0f)
        {
            _wait -= f;
            _scan = Mathf.Sin(_t * 2.1f) * 0.6f;
        }
        else
        {
            var target = _points[_idx];
            var to = target - Position;
            if (to.Length() < 6f)
            {
                _idx = (_idx + 1) % _points.Length;
                _wait = 1.5f;
            }
            else
            {
                var dir = to.Normalized();
                Position += dir * Speed * f;
                _baseFacing = to.Angle();
                _scan = 0f;
            }
        }
        var facing = _baseFacing + _scan;

        // ---- 侦测：距离 + 视野角 + 视线（墙体遮挡）----
        bool raw = Detect(facing);
        if (raw)
            _exposure += f * (_player.Sneaking ? 0.4f : 1.2f) * Aggression;
        else
            _exposure = Mathf.Max(-1f, _exposure - f * 0.8f);

        SeeingPlayer = raw && _exposure > 0.05f;
        if (_exposure >= 1f)
        {
            _exposure = -2f; // 冷却，避免连续触发
            EmitSignal(SignalName.SpottedPlayer);
        }
        QueueRedraw();
    }

    private bool Detect(float facing)
    {
        if (_player == null) return false;
        var eye = GlobalPosition + new Vector2(0, -20);
        var toP = _player.GlobalPosition - eye;
        if (toP.Length() > Range) return false;

        float diff = Mathf.Wrap(toP.Angle() - facing, -Mathf.Pi, Mathf.Pi);
        if (Mathf.Abs(diff) > HalfAngle) return false;

        // 视线被墙（layer 1）挡住则看不见
        var query = PhysicsRayQueryParameters2D.Create(eye, _player.GlobalPosition, 1);
        var hit = GetWorld2D().DirectSpaceState.IntersectRay(query);
        return hit.Count == 0;
    }

    public override void _Draw()
    {
        // 视野锥：淡墨色，警惕时染上洗褪的红（GDD：颜色代替血条，保持克制）
        bool alert = SeeingPlayer || _exposure > 0.25f;
        var coneColor = alert
            ? new Color(0.55f, 0.20f, 0.16f, 0.16f)
            : new Color(0.91f, 0.86f, 0.75f, 0.07f);
        var pts = new System.Collections.Generic.List<Vector2> { new(0, -20) };
        for (int i = 0; i <= 10; i++)
        {
            float a = _baseFacing + _scan - HalfAngle + (2f * HalfAngle) * i / 10f;
            pts.Add(new Vector2(0, -20) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Range);
        }
        DrawColoredPolygon(pts.ToArray(), coneColor);

        Silhouette.Draw(this, Silhouette.Kind.Soldier);
    }
}
