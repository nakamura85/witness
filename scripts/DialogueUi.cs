using System;
using Godot;

namespace Witness;

/// <summary>
/// v3.68 底部剧情文字：一张「少许烧毁」的手绘上色漫画风纸张。
/// 取代 v3.67 及以前的纸色 PanelContainer + 边框矩形「对话格子」。
/// 每段对白按文本哈希换 seed，纸张焦边 / 缺口 / 卷角都不同，不会张张一样。
/// 选择按钮改成漫画风纸签，保持原本的抉择交互与回调序号不变。
/// </summary>
public partial class DialogueUi : CanvasLayer
{
    private Label _name, _text;
    private HBoxContainer _buttons;
    private BurntPaperPanel _paper;
    private DialogueOverlay _overlay;

    // 25 > HouseInteriorUi(20)，屋内点选项时这张纸盖在屋内画面之上（#4）
    public DialogueUi() { Layer = 25; }

    public override void _Ready()
    {
        var root = new Control();
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(root);

        _paper = new BurntPaperPanel(BurntPaperArt.StableHash("dialogue"), 0.42f);
        _paper.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _paper.Position = new Vector2(-470, -190);
        _paper.CustomMinimumSize = new Vector2(940, 0);
        root.AddChild(_paper);

        // 漫画风「命令印章」：叠在纸张右上角，强化「不抵抗令」这类正式文书感。
        // 用全屏 Overlay 按纸张真实矩形坐标绘制，避免被 PanelContainer 布局覆盖。
        _overlay = new DialogueOverlay(_paper);
        _overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _overlay.MouseFilter = Control.MouseFilterEnum.Ignore;
        root.AddChild(_overlay);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        _paper.AddChild(box);

        _name = new Label();
        _name.AddThemeFontSizeOverride("font_size", 18);
        _name.AddThemeColorOverride("font_color", new Color(0.45f, 0.14f, 0.12f));
        box.AddChild(_name);

        // 标题下的手绘墨线分隔，像文书标题下的装饰线
        var nameRule = new InkRule();
        nameRule.CustomMinimumSize = new Vector2(0, 12);
        box.AddChild(nameRule);

        _text = new Label();
        _text.AddThemeFontSizeOverride("font_size", 19);
        _text.AddThemeColorOverride("font_color", InkStyle.InkColor);
        _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _text.CustomMinimumSize = new Vector2(820, 0);
        box.AddChild(_text);

        _buttons = new HBoxContainer();
        _buttons.AddThemeConstantOverride("separation", 14);
        _buttons.Alignment = BoxContainer.AlignmentMode.End;
        box.AddChild(_buttons);

        Visible = false;
    }

    public void ShowLine(string name, string text, Action onDone = null)
        => ShowChoice(name, text, new[] { (Loc.I.T("ui_confirm"), true) }, _ => onDone?.Invoke());

    public void ShowTextThen(string name, string text, Action next)
        => ShowLine(name, text, next);

    /// <summary>显示一个带按钮的选择；选完自动收起并回调序号。</summary>
    public void ShowChoice(string name, string text, (string label, bool enabled)[] opts, Action<int> onPick)
    {
        Visible = true;
        // 每段对白换一张纸：seed 随文本变化，烧痕位置 / 缺口 / 卷角都不一样
        _paper?.SetSeed(BurntPaperArt.StableHash((name ?? "") + "|" + (text ?? "")));
        _name.Text = name;
        _name.Visible = name != "";
        _text.Text = text;
        foreach (var c in _buttons.GetChildren())
            c.QueueFree();
        for (int i = 0; i < opts.Length; i++)
        {
            int idx = i; // 闭包捕获副本
            var b = new Button { Text = opts[i].label, Disabled = !opts[i].enabled };
            PaperButtonStyle.Apply(b);
            b.AddThemeFontSizeOverride("font_size", 17);
            b.Pressed += () =>
            {
                Visible = false;
                onPick(idx);
            };
            _buttons.AddChild(b);
        }
    }
}

/// <summary>
/// 漫画风「命令印章」叠加层：按纸张真实矩形在右上角画红印章（红底 + 粗红环 + 内圈 + 奶油五角星），
/// 给「不抵抗令」这类正式文书增加命令感。纯坐标绘制，不影响抉择逻辑，也不改全局 BurntPaperPanel。
/// </summary>
public partial class DialogueOverlay : Control
{
    private readonly BurntPaperPanel _paper;

    public DialogueOverlay(BurntPaperPanel paper)
    {
        _paper = paper;
    }

    private static Vector2[] StarPoints(Vector2 c, float ro, float ri, int n)
    {
        var pts = new Vector2[n * 2];
        for (int i = 0; i < n * 2; i++)
        {
            float ang = -Mathf.Pi / 2f + i * Mathf.Pi / n;
            float rad = (i % 2 == 0) ? ro : ri;
            pts[i] = c + new Vector2(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad);
        }
        return pts;
    }

    public override void _Draw()
    {
        var pr = _paper.GetRect();
        if (pr.Size.X < 10f) return;
        float s = 54f;
        var c = new Vector2(pr.Position.X + pr.Size.X - s * 0.5f - 16f, pr.Position.Y + s * 0.5f + 10f);
        float r = s * 0.42f;
        // 红印章：实心红底 + 粗红环描边 + 内圈
        DrawCircle(c, r, new Color(0.74f, 0.18f, 0.14f, 0.92f));
        DrawCircle(c, r, new Color(0.74f, 0.18f, 0.14f, 0.92f), false, 3f);
        DrawCircle(c, r * 0.78f, new Color(0.74f, 0.18f, 0.14f, 0.92f), false, 2f);
        // 内部五角星（奶油色）
        DrawColoredPolygon(StarPoints(c, r * 0.52f, r * 0.22f, 5), new Color(0.98f, 0.93f, 0.80f));
    }
}
