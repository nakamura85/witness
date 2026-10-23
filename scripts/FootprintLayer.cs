using Godot;
using System.Collections.Generic;

namespace Witness;

/// <summary>
/// 脚印层：玩家与敌人走路时在雪地上留下的写实脚印。
/// 
/// 设计要点：
/// - 真正的左右脚交替（Pos.X 偏移 ±5px，方向随移动朝向旋转）
/// - 军靴细节：靴底纹路弧、后跟更深处的 heel stamp、前掌的 toe drag（被风/惯性带过）
/// - 雪凹痕三层：最暗的洞（被踩到底）+ 中间的被压实雪 + 边缘被挤到旁边的亮雪
/// - 脚印随时间老化 alpha 渐淡 + 新雪飘落覆盖（120 秒）
/// - 上限 300 个，超过后最老的自动移除
/// </summary>
public partial class FootprintLayer : Node2D
{
    private readonly JiangqiaoLevel _lvl;

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

    private const float MaxAge = 120f;
    private const int MaxPrints = 300;

    public FootprintLayer(JiangqiaoLevel lvl) { _lvl = lvl; }

    public override void _Ready()
    {
        _playerPrevPos = _lvl._player.GlobalPosition;
    }

    public override void _Process(double delta)
    {
        float f = (float)delta;

        // 玩家
        var ppos = _lvl._player.GlobalPosition;
        var pvel = ppos - _playerPrevPos;
        _playerPrevPos = ppos;
        var fig = _lvl._player.Figure;
        if (fig.Grounded && fig.Speed > 12f)
        {
            // 步频校准：走路 phase 直接用 Figure.Phase（0~2π 循环）
            // 不直接比较，用差值过零检测
            float newPhase = fig.Phase;
            bool crossedDown = newPhase < Mathf.Pi && _playerStepPhase > Mathf.Pi;  // 后脚即将踩到地面
            _playerStepPhase = newPhase;
            if (crossedDown || newPhase < 0.02f && _playerStepPhase > 6f)
            {
                float dir = Mathf.Atan2(pvel.Y, pvel.X);
                AddPrint(new Print
                {
                    Pos = new Vector2(ppos.X + (_playerLeftFoot ? -5f : 5f), ppos.Y - 1f),
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
            // 静止时 phase 重置，防止刚起步立即触发
            _playerStepPhase = fig.Phase > Mathf.Pi ? 0f : fig.Phase;
        }

        // 敌人
        foreach (var e in _lvl._enemies)
        {
            if (e.Gone) continue;
            if (e._fig.Grounded && e._fig.Speed > 12f)
            {
                float newPhase = e._fig.Phase;
                bool crossedDown = newPhase < Mathf.Pi && e._prevPhase > Mathf.Pi;
                e._prevPhase = newPhase;
                if (crossedDown)
                {
                    float dir = e._fig.FacingRight ? 0f : Mathf.Pi;  // 敌人只左右移动
                    var epos = e.GlobalPosition;
                    AddPrint(new Print
                    {
                        Pos = new Vector2(epos.X + (_enemyLeftFoot ? -4.5f : 4.5f), epos.Y - 1f),
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
                e._prevPhase = 0f;
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
        foreach (var p in _prints)
        {
            float k = Mathf.Clamp(p.Age / MaxAge, 0f, 1f);
            float a = (1f - k);
            // 越旧越被新雪填平：凹痕变浅，亮边消失
            float depthMul = 1f - k * 0.75f;

            DrawSetTransform(p.Pos, p.Rot, Vector2.One);

            // 第 1 层：最暗的洞底（雪被踩到底部 → 露出下面的冻土颜色）
            DrawSetTransform(Vector2.Zero, 0, new Vector2(9f, 4f) * depthMul);
            DrawCircle(Vector2.Zero, 1f, new Color(0.22f, 0.24f, 0.26f, 0.55f * a));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);

            // 第 2 层：压实雪层（鞋跟部分更深——heel stamp）
            DrawSetTransform(Vector2.Zero, 0, new Vector2(7.5f, 4.2f) * depthMul);
            DrawCircle(new Vector2(0.2f * p.Side, -0.5f), 1.1f, new Color(0.36f, 0.39f, 0.42f, 0.50f * a));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);

            // 第 3 层：鞋整体轮廓（椭圆 = 靴底压实雪）
            DrawSetTransform(Vector2.Zero, 0, new Vector2(8f + p.SpeedMag * 0.02f, 4.6f) * depthMul);
            DrawCircle(Vector2.Zero, 1f, new Color(0.50f, 0.53f, 0.56f, 0.40f * a));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);

            // 第 4 层：靴底纹路弧（3 条横线 → 军靴纹路），只在年轻脚印可见
            if (k < 0.5f)
            {
                float treadAlpha = 0.55f * (1f - k * 2f) * a;
                DrawLine(new Vector2(-5f, -0.8f), new Vector2(5f, -0.8f), new Color(0.30f, 0.32f, 0.34f, treadAlpha), 0.8f, true);
                DrawLine(new Vector2(-5f, 0.2f), new Vector2(5f, 0.2f), new Color(0.30f, 0.32f, 0.34f, treadAlpha), 0.8f, true);
                DrawLine(new Vector2(-4f, 1.2f), new Vector2(4f, 1.2f), new Color(0.30f, 0.32f, 0.34f, treadAlpha * 0.7f), 0.7f, true);
            }

            // 第 5 层：toe drag（前掌被拖动的雪屑线）——高速走路才明显
            if (p.SpeedMag > 45f && k < 0.3f)
            {
                float dragAlpha = 0.4f * (1f - k / 0.3f) * a;
                float dragLen = (p.SpeedMag - 45f) * 0.18f;
                // 拖动线 = 前掌前方的浅灰色虚线
                for (int di = 0; di < 3; di++)
                {
                    float t0 = 0.3f + di * 0.22f;
                    float t1 = t0 + 0.08f;
                    DrawLine(
                        new Vector2(7f + t0 * dragLen, -0.5f),
                        new Vector2(7f + t1 * dragLen, -0.3f + di * 0.15f),
                        new Color(0.55f, 0.58f, 0.60f, dragAlpha), 1.0f, true);
                }
            }

            // 第 6 层：被挤到旁边的亮雪边（雪被鞋压出去 → 隆起的亮边）
            float rimAlpha = 0.50f * (1f - k * 1.5f) * a;
            if (rimAlpha > 0.02f)
            {
                DrawLine(new Vector2(-7f * depthMul, -2.2f * depthMul),
                    new Vector2(-3f * depthMul, -2.8f * depthMul),
                    new Color(0.90f, 0.94f, 0.96f, rimAlpha), 1.1f, true);
                DrawLine(new Vector2(3f * depthMul, -2.8f * depthMul),
                    new Vector2(7f * depthMul, -2.2f * depthMul),
                    new Color(0.90f, 0.94f, 0.96f, rimAlpha), 1.1f, true);
                // 鞋跟后面的小冰碴
                DrawCircle(new Vector2(1.0f * p.Side, 3.0f * depthMul), 1.2f,
                    new Color(0.92f, 0.94f, 0.96f, rimAlpha * 0.5f));
            }

            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }
}
