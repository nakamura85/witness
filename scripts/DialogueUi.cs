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

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        _paper.AddChild(box);

        _name = new Label();
        _name.AddThemeFontSizeOverride("font_size", 15);
        _name.AddThemeColorOverride("font_color", new Color(0.55f, 0.20f, 0.16f));
        box.AddChild(_name);

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
