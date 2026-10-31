using Godot;
using System.Collections.Generic;

namespace Witness;

/// <summary>
/// Footprint layer: one generated watercolor boot-print texture stamped per
/// footfall, rotated along the move direction, left/right print mirrored.
/// Prints age out (alpha fade + shrink) as fresh snow covers them, max 300.
/// The old six-layer code-drawn ellipse stack looked like a trail of
/// droppings - totally off from the hand-drawn style of the rest of the scene.
/// </summary>
public partial class FootprintLayer : Node2D
{
    private readonly JiangqiaoLevel _lvl;
    private Texture2D _tex;

    private struct Print
    {
        public Vector2 Pos;   // 中心位置
        public float Rot;     // 朝向移动方向的旋转（弧度）
        public float Age;
        public float Side;    // -1 = 左脚，+1 = 右脚（用于纹路微扰）
        public float SpeedMag; // 落地瞬间速度（惯性决定 toe drag 长度）
    }

    private readonly List<Print> _prints = new();
    private float _playerStepPhase;  // 累积相位，超过 2π 触发落地
    private bool _playerLeftFoot;    // 左脚 / 右脚 toggle
    private bool _enemyLeftFoot;
    private Vector2 _playerPrevPos;  // 上一帧位置，用来算朝向

    private const float MaxAge = 90f;
    private const int MaxPrints = 300;

    public FootprintLayer(JiangqiaoLevel lvl) { _lvl = lvl; }

    public override void _Ready()
    {
        // 关卡构建顺序里 FootprintLayer 先于玩家创建，_player 此时还是 null，
        // 上一帧位置推迟到 _Process 里首次取样。
        if (_lvl._player != null)
            _playerPrevPos = _lvl._player.GlobalPosition;
    }

    public override void _Process(double delta)
    {
        if (_lvl._player == null) return;
        float f = (float)delta;

        // 玩家
        var ppos = _lvl._player.GlobalPosition;
        var pvel = ppos - _playerPrevPos;
        _playerPrevPos = ppos;
        var fig = _lvl._player.Figure;
        if (fig.Grounded && fig.Speed > 12f)
        {
            // Phase grows unbounded, so the old "crossed π" test never fired even
            // once. Compare half-cycle buckets of PosMod(Phase, 2π) instead:
            // every bucket change = one footfall = one print
            float npM = Mathf.PosMod(fig.Phase, Mathf.Tau);
            float ppM = Mathf.PosMod(_playerStepPhase, Mathf.Tau);
            _playerStepPhase = fig.Phase;
            if ((int)(npM / Mathf.Pi) != (int)(ppM / Mathf.Pi))
            {
                float dir = Mathf.Atan2(pvel.Y, pvel.X);
                AddPrint(new Print
                {
                    // v2.9: sink the print a few px BELOW the boot line. The old
                    // y sat right on the bright surface polyline, so every print
                    // straddled the horizon edge and read as a dark blob
                    // floating on the snow rim. Inside the field it reads as a
                    // dent pressed into the snow.
                    Pos = new Vector2(ppos.X + (_playerLeftFoot ? -5f : 5f),
                                      ppos.Y + 3f + (_playerLeftFoot ? 1.6f : -0.6f)),
                    Rot = dir,
                    Age = 0f,
                    Side = _playerLeftFoot ? -1f : 1f,
                    SpeedMag = Mathf.Clamp(pvel.Length() * 60f, 20f, 100f),
                });
                _playerLeftFoot = !_playerLeftFoot;
            }
        }
        else
        {
            // keep tracking while idle so walking again don't dump a burst of prints
            _playerStepPhase = fig.Phase;
        }

        // 敌人
        foreach (var e in _lvl._enemies)
        {
            if (e.Gone) continue;
            if (e._fig.Grounded && e._fig.Speed > 12f)
            {
                float npM = Mathf.PosMod(e._fig.Phase, Mathf.Tau);
                float ppM = Mathf.PosMod(e._prevPhase, Mathf.Tau);
                e._prevPhase = e._fig.Phase;
                if ((int)(npM / Mathf.Pi) != (int)(ppM / Mathf.Pi))
                {
                    float dir = e._fig.FacingRight ? 0f : Mathf.Pi;  // 敌人只左右移动
                    var epos = e.GlobalPosition;
                    AddPrint(new Print
                    {
                        Pos = new Vector2(epos.X + (_enemyLeftFoot ? -4.5f : 4.5f),
                                          epos.Y + 3f + (_enemyLeftFoot ? 1.5f : -0.5f)),
                        Rot = dir,
                        Age = 0f,
                        Side = _enemyLeftFoot ? -1f : 1f,
                        SpeedMag = e._fig.Speed,
                    });
                    _enemyLeftFoot = !_enemyLeftFoot;
                }
            }
            else
            {
                e._prevPhase = e._fig.Phase;
            }
        }

        // 老化
        for (int i = _prints.Count - 1; i >= 0; i--)
        {
            var p = _prints[i];
            p.Age += f;
            if (p.Age > MaxAge) _prints.RemoveAt(i);
            else _prints[i] = p;
        }

        QueueRedraw();
    }

    private void AddPrint(Print p)
    {
        _prints.Add(p);
        while (_prints.Count > MaxPrints) _prints.RemoveAt(0);
    }

    public override void _Draw()
    {
        // v3.22: this was the whole snow patch, so a trail of them read as a row
        // of blue puddles lying on the snow. The new asset is just the boot
        // print, remapped to the colour of the snow it presses into.
        _tex ??= GD.Load<Texture2D>("res://assets/art/props/bootprint.png");
        if (_tex == null) return;
        var tsz = _tex.GetSize();

        foreach (var p in _prints)
        {
            float k = Mathf.Clamp(p.Age / MaxAge, 0f, 1f);
            float a = 1f - k;
            // older prints get covered by fresh snow: fade out and shrink a bit
            float depthMul = 1f - k * 0.7f;
            var at = p.Pos;

            float len = 26f * depthMul;
            float wid = len * tsz.Y / tsz.X;
            // mirror alternate prints so the trail don't read as one boot stamped
            float sY = p.Side < 0f ? -1f : 1f;
            // slight per-print rotation jitter off its own position, deterministic
            float jrot = Mathf.Sin(at.X * 1.7f + at.Y) * 0.08f;

            var mod = new Color(1f, 1f, 1f, 0.9f * a);
            DrawSetTransform(at, p.Rot + jrot, new Vector2(depthMul, sY * depthMul));
            DrawTextureRect(_tex, new Rect2(-len * 0.42f, -wid * 0.55f, len, wid), false, mod);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
