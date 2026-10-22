using System.Collections.Generic;
using Godot;

namespace Witness;

/// <summary>
/// 巡逻兵（压迫性系统，不是可战斗的敌人 —— 呈现伦理铁律）。
/// v0.3 横版：沿路径点左右巡逻 + 水平视野锥侦测：
/// - 玩家在锥内且无地形遮挡 → 曝露值上升（蹲下减速）；
/// - 曝露满 → 发出 SpottedPlayer 信号（Level 决定后果：没收证据 / 被押送）。
/// </summary>
public partial class Patrol : Node2D
{
    [Signal] public delegate void SpottedPlayerEventHandler();

    public float Aggression = 1.0f;   // C2 选择「上前阻挡」后提升（巡逻记住了你的脸）
    public bool SeeingPlayer { get; private set; }
    public float Facing { get; private set; } = 1f;  // 1 右 / -1 左

    private Level _level;
    private Player _player;
    private Vector2[] _points;
    private int _idx = 1;
    private float _wait;
    private float _scan;
    private float _exposure;
    private float _t;
    private float _walkPhase;
    private bool _distracted;         // 被石块声引开：离岗查看中
    private Vector2 _distractPoint;
    private float _distractT;
    private SpriteActor _actor;

    public override void _Ready()
    {
        _actor = new SpriteActor(SpriteActor.Kind.Soldier);
        AddChild(_actor);
    }

    private const float Range = 300f;      // 视野距离
    private const float HalfAngle = 0.5f;  // 视野半角（弧度）
    private const float Speed = 70f;
    private const float EyeY = -94f;       // 眼睛高度（脚底原点，对齐 108px 立绘的头部）

    public void Setup(Level level, Player player, Vector2[] points)
    {
        _level = level;
        _player = player;
        _points = points;
        Position = points[0];
        float d = Mathf.Sign(points[1].X - points[0].X);
        Facing = d == 0 ? 1f : d;
    }

    /// <summary>被落地石块的声音吸引：快步走到噪音点停留扫视，计时结束返回巡逻路径。</summary>
    public void Distract(Vector2 point, float seconds)
    {
        _distracted = true;
        _distractPoint = new Vector2(point.X, Position.Y); // 只沿地面移动
        _distractT = seconds;
        _wait = 0f;
        _level?.BubbleAt(Position, "bub_noise");
    }

    public override void _PhysicsProcess(double delta)
    {
        float f = (float)delta;
        _t += f;
        if (_points == null || _level == null || !_level.PlayPhase)
            return;

        bool moving = false;
        // ---- 被引开：快步走向噪音点，到了就来回扫视，计时结束返回 ----
        if (_distracted)
        {
            var toNoise = _distractPoint - Position;
            if (Mathf.Abs(toNoise.X) > 10f)
            {
                float step = Mathf.Sign(toNoise.X) * Speed * 1.15f * f;
                Position += new Vector2(step, 0);
                Facing = toNoise.X >= 0 ? 1f : -1f;
                _walkPhase += f * 10f;
                moving = true;
                _scan = 0f;
            }
            else
            {
                _distractT -= f;
                _scan = Mathf.Sin(_t * 3.2f) * 0.45f; // 疑惑地来回扫视
                if (_distractT <= 0f) _distracted = false;
            }
        }
        // ---- 沿路径点左右移动，停留时扫视 ----
        else if (_wait > 0f)
        {
            _wait -= f;
            _scan = Mathf.Sin(_t * 2.1f) * 0.28f;
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
                Facing = dir.X >= 0 ? 1f : -1f;
                _walkPhase += f * 9f;
                moving = true;
                _scan = 0f;
            }
        }
        if (_actor != null)
        {
            _actor.Facing = Facing;
            _actor.Moving = moving;
            _actor.Phase = _walkPhase;
        }
        float facing = (Facing > 0 ? 0f : Mathf.Pi) + _scan;

        // ---- 侦测：距离 + 视野角 + 视线（地形遮挡）----
        bool raw = Detect(facing);
        if (raw)
            _exposure += f * (_player.Sneaking ? 0.4f : 1.2f) * Aggression;
        else
            _exposure = Mathf.Max(-1f, _exposure - f * 0.8f);

        SeeingPlayer = raw && _exposure > 0.05f;
        if (_exposure >= 1f)
        {
            _exposure = -2f; // 冷却，避免连续触发
            _level?.BubbleAt(Position, "bub_halt");
            EmitSignal(SignalName.SpottedPlayer);
        }
        QueueRedraw();
    }

    private bool Detect(float facing)
    {
        if (_player == null) return false;
        var eye = GlobalPosition + new Vector2(0, EyeY);
        var toP = _player.GlobalPosition - eye;
        if (toP.Length() > Range) return false;

        float diff = Mathf.Wrap(toP.Angle() - facing, -Mathf.Pi, Mathf.Pi);
        if (Mathf.Abs(diff) > HalfAngle) return false;

        // 视线被地形（layer 1）挡住则看不见
        var query = PhysicsRayQueryParameters2D.Create(eye, _player.GlobalPosition, 1);
        var hit = GetWorld2D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0) return false;
        // 蒸汽遮挡（阀门解谜放出的蒸汽带）
        return _level == null || !_level.VisionBlocked(eye, _player.GlobalPosition);
    }

    public override void _Draw()
    {
        // 视野锥：淡墨色，警惕时染上洗褪的红（GDD：颜色代替血条，保持克制）
        bool alert = SeeingPlayer || _exposure > 0.25f;
        var coneColor = alert
            ? new Color(0.55f, 0.20f, 0.16f, 0.16f)
            : new Color(0.91f, 0.86f, 0.75f, 0.07f);
        float center = (Facing > 0 ? 0f : Mathf.Pi) + _scan;
        var pts = new List<Vector2> { new(0, EyeY) };
        for (int i = 0; i <= 10; i++)
        {
            float a = center - HalfAngle + (2f * HalfAngle) * i / 10f;
            pts.Add(new Vector2(0, EyeY) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Range);
        }
        DrawColoredPolygon(pts.ToArray(), coneColor);
    }
}
