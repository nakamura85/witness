using Godot;

namespace Witness;

/// <summary>
/// 记录进度环（GDD §9.2）：按住 E 时显示；巡逻接近时环染上洗褪的红。
/// 用颜色不用血条 —— 保持克制。
/// </summary>
public partial class RecordRing : Control
{
    private float _progress;
    private bool _danger;

    public RecordRing()
    {
        CustomMinimumSize = new Vector2(80, 80);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Set(float progress, bool danger)
    {
        if (Mathf.IsEqualApprox(progress, _progress) && danger == _danger) return;
        _progress = progress;
        _danger = danger;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_progress <= 0f) return;
        var c = Size / 2f;
        // 背景环
        DrawArc(c, 26f, 0, Mathf.Tau, 48, new Color(0.91f, 0.86f, 0.75f, 0.18f), 1.6f);
        // 进度弧
        var col = _danger
            ? new Color(0.72f, 0.26f, 0.20f, 0.95f)
            : new Color(0.91f, 0.86f, 0.75f, 0.95f);
        DrawArc(c, 26f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * _progress, 48, col, 5f);
    }
}
