using Godot;
using System.Collections.Generic;

namespace Witness;

/// <summary>
/// 脚印层：玩家与敌人走路时在雪地上留下的脚印。
/// 
/// 实现原理：
/// - 记录每个角色上一次的 phase 和位置
/// - 当 phase 前进超过 ~π（= 1 步）且角色在地面上移动，就在当前脚底画一个脚印
/// - 左脚 / 右脚交替（用 floor(phase/π) 的奇偶）
/// - 脚印随时间老化 alpha 渐淡（120 秒完全消失）
/// - 上限 400 个，超过后最老的自动移除
/// </summary>
public partial class FootprintLayer : Node2D
{
    private readonly JiangqiaoLevel _lvl;

    private struct Print
    {
        public Vector2 Pos;
        public float Age;
    }

    private readonly List<Print> _prints = new();
    private float _playerStepPhase;  // 累积的步相位，超过 2π 时触发一次落地

    private const float MaxAge = 120f;
    private const int MaxPrints = 400;

    public FootprintLayer(JiangqiaoLevel lvl) { _lvl = lvl; }

    public override void _Process(double delta)
    {
        float f = (float)delta;

        // 玩家
        var fig = _lvl._player.Figure;
        if (fig.Grounded && fig.Speed > 12f)
        {
            float stepDelta = fig.Speed * f * 0.095f * 2f;  // 走路推进速度
            _playerStepPhase += stepDelta;
            if (_playerStepPhase > Mathf.Pi * 2f)
            {
                _playerStepPhase -= Mathf.Pi * 2f;
                var pos = _lvl._player.GlobalPosition;
                AddPrint(new Vector2(pos.X, pos.Y - 2));
            }
        }

        // 敌人
        foreach (var e in _lvl._enemies)
        {
            if (e.Gone) continue;
            if (e._fig.Grounded && e._fig.Speed > 12f)
            {
                float stepDelta = e._fig.Speed * f * 0.095f * 2f;
                e._prevPhase += stepDelta;  // 复用 _prevPhase 字段做敌人的 step 累积
                if (e._prevPhase > Mathf.Pi * 2f)
                {
                    e._prevPhase -= Mathf.Pi * 2f;
                    var pos = e.GlobalPosition;
                    AddPrint(new Vector2(pos.X, pos.Y - 2));
                }
            }
            else
            {
                // 不动时缓慢衰减（避免重新起步立即触发）
                e._prevPhase = Mathf.Lerp(e._prevPhase, 0f, f * 3f);
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

    private void AddPrint(Vector2 pos)
    {
        _prints.Add(new Print { Pos = pos, Age = 0f });
        while (_prints.Count > MaxPrints) _prints.RemoveAt(0);
    }

    public override void _Draw()
    {
        foreach (var p in _prints)
        {
            float k = Mathf.Clamp(p.Age / MaxAge, 0f, 1f);
            float alpha = 0.40f * (1f - k);
            float size = 5f + k * 1.2f;

            // 雪凹痕（暗灰椭圆 = 雪被踩塌）
            DrawSetTransform(p.Pos, 0, new Vector2(2.2f, 0.55f));
            DrawCircle(Vector2.Zero, size, new Color(0.38f, 0.42f, 0.46f, alpha));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);

            // 凹痕上的亮边（被挤到边缘的薄雪）
            DrawLine(new Vector2(p.Pos.X - 8f, p.Pos.Y - 2f),
                new Vector2(p.Pos.X + 8f, p.Pos.Y - 2f),
                new Color(0.85f, 0.90f, 0.92f, alpha * 0.45f), 1.2f, true);
        }
    }
}
