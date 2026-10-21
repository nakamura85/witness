using Godot;

namespace Witness;

/// <summary>
/// 极简 HUD（GDD §9.1）：日期 + 目标（左上）、物资（右上）、
/// 交互提示（右下）、记录进度环（下中）、提示语与教学浮字。
/// 用颜色与文字保持克制，不做状态条堆砌。
/// </summary>
public partial class Hud : CanvasLayer
{
    private Label _date, _sneak, _objective, _supplies, _prompt, _toast, _tutorial;
    private RecordRing _ring;
    private float _toastT;

    public Hud() { Layer = 10; }

    public override void _Ready()
    {
        var root = new Control();
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(root);

        var paper = new Color(0.91f, 0.86f, 0.75f);
        var faded = new Color(0.72f, 0.68f, 0.58f);
        var red = new Color(0.72f, 0.30f, 0.24f);

        _date = MakeLabel(20, paper);
        _date.Position = new Vector2(18, 12);
        root.AddChild(_date);

        _sneak = MakeLabel(14, red);
        _sneak.Position = new Vector2(18, 42);
        _sneak.Visible = false;
        root.AddChild(_sneak);

        _objective = MakeLabel(15, faded);
        _objective.Position = new Vector2(18, 64);
        _objective.CustomMinimumSize = new Vector2(540, 0);
        _objective.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(_objective);

        _supplies = MakeLabel(16, paper);
        _supplies.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _supplies.Position = new Vector2(-330, 12);
        _supplies.HorizontalAlignment = HorizontalAlignment.Right;
        _supplies.Visible = false;
        root.AddChild(_supplies);

        _prompt = MakeLabel(19, paper);
        _prompt.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _prompt.Position = new Vector2(-460, -46);
        _prompt.CustomMinimumSize = new Vector2(440, 0);
        _prompt.HorizontalAlignment = HorizontalAlignment.Right;
        root.AddChild(_prompt);

        _ring = new RecordRing();
        _ring.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _ring.Position = new Vector2(-40, -130);
        root.AddChild(_ring);

        _toast = MakeLabel(17, paper);
        _toast.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _toast.Position = new Vector2(-400, -168);
        _toast.CustomMinimumSize = new Vector2(800, 0);
        _toast.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(_toast);

        _tutorial = MakeLabel(15, faded);
        _tutorial.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _tutorial.Position = new Vector2(-360, -66);
        _tutorial.CustomMinimumSize = new Vector2(720, 0);
        _tutorial.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(_tutorial);
    }

    private static Label MakeLabel(int size, Color color)
    {
        var l = new Label();
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        l.MouseFilter = Control.MouseFilterEnum.Ignore;
        return l;
    }

    public void SetDate(string text) => _date.Text = text;

    public void SetObjective(string text) => _objective.Text = text;

    public void ShowSupplies(bool on) => _supplies.Visible = on;

    public void RefreshSupplies(int food, int medicine, int blanket)
        => _supplies.Text = $"{Loc.I.T("item_food")} {food}    {Loc.I.T("item_medicine")} {medicine}    {Loc.I.T("item_blanket")} {blanket}";

    public void ShowPrompt(string text) => _prompt.Text = text;

    public void SetRecord(float progress, bool danger) => _ring.Set(progress, danger);

    public void SetSneak(bool on) => _sneak.Visible = on;

    /// <summary>短提示：浮现 → 停留 → 淡出（克制，不弹窗）。</summary>
    public void Toast(string text)
    {
        _toast.Text = text;
        _toast.Modulate = new Color(1, 1, 1, 1);
        _toastT = 2.6f;
    }

    public void ShowTutorial(string text)
    {
        _tutorial.Text = text;
        _tutorial.Modulate = new Color(1, 1, 1, 1);
    }

    public void HideTutorial() => _tutorial.Modulate = new Color(1, 1, 1, 0);

    public override void _Process(double delta)
    {
        if (_toastT > 0f)
        {
            _toastT -= (float)delta;
            if (_toastT <= 0f)
                _toast.Modulate = new Color(1, 1, 1, 0);
        }
    }
}
