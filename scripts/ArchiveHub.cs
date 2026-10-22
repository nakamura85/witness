using System.Collections.Generic;
using Godot;

namespace Witness;

/// <summary>
/// 记忆档案馆 Hub（Talos Principle 式的中枢世界 + 大地图 + 时间线墙）：
/// 1895→1948 五个章节节点横列在时间线上；已见证的点亮，
/// 未解锁的沉默存在。从「源头」进入第一章试玩，也可重访南京 1937。
/// </summary>
public partial class ArchiveHub : Control
{
    private readonly Main _main;
    private Label _desc;
    private readonly List<Button> _nodes = new();

    public ArchiveHub(Main main) { _main = main; }

    /// <summary>章节节点定义：(年份标签, 名称键, 描述键, 是否解锁, 进入回调)。</summary>
    private (string year, string nameKey, string descKey, bool unlocked, System.Action enter)[] Chapters()
    {
        var s = GameState.I.S;
        return new[]
        {
            ("1895–1910", "node_source", "hub_desc_source", true, (System.Action)_main.StartPrologue),
            ("1931",      "node_1931",   "hub_desc_1931",   false, (System.Action)null),
            ("1937",      "node_1937",   "hub_desc_1937",   true,  (System.Action)_main.StartNanjing),
            ("1942",      "node_1942",   "hub_desc_1942",   false, (System.Action)null),
            ("1946–48",   "node_1948",   "hub_desc_1948",   false, (System.Action)null),
        };
    }

    public override void _Ready()
    {
        var bg = new ColorRect { Color = new Color(0.086f, 0.075f, 0.062f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // 档案馆大地图（炭笔世界地图；素材缺失时以素色兜底）
        if (ResourceLoader.Exists("res://assets/art/hub_bg.jpg"))
        {
            var map = new TextureRect
            {
                Texture = GD.Load<Texture2D>("res://assets/art/hub_bg.jpg"),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                Modulate = new Color(1, 1, 1, 0.55f),
            };
            map.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(map);
        }

        // 顶部标题区
        var top = new VBoxContainer();
        top.SetAnchorsPreset(LayoutPreset.TopWide);
        top.OffsetTop = 30;
        top.AddThemeConstantOverride("separation", 6);
        AddChild(top);

        var title = new Label { Text = Loc.I.T("hub_title") };
        title.AddThemeFontSizeOverride("font_size", 40);
        title.AddThemeColorOverride("font_color", new Color(0.91f, 0.86f, 0.75f));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        top.AddChild(title);

        var sub = new Label { Text = Loc.I.T("hub_subtitle") };
        sub.AddThemeFontSizeOverride("font_size", 16);
        sub.AddThemeColorOverride("font_color", new Color(0.72f, 0.68f, 0.58f));
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        top.AddChild(sub);

        var prog = new Label { Text = Loc.I.Tf("hub_progress", GameState.I.S.ChaptersWitnessed, 5) };
        prog.AddThemeFontSizeOverride("font_size", 14);
        prog.AddThemeColorOverride("font_color", new Color(0.66f, 0.62f, 0.54f));
        prog.HorizontalAlignment = HorizontalAlignment.Center;
        top.AddChild(prog);

        // 时间线（横向节点行 + 一条贯穿的线）
        var wrap = new Control();
        wrap.SetAnchorsPreset(LayoutPreset.CenterBottom);
        wrap.OffsetLeft = -640;
        wrap.OffsetRight = 640;
        wrap.OffsetTop = -300;
        wrap.OffsetBottom = -140;
        AddChild(wrap);

        var line = new ColorRect { Color = new Color(0.72f, 0.68f, 0.58f, 0.35f) };
        line.SetAnchorsPreset(LayoutPreset.TopWide);
        line.OffsetLeft = 40;
        line.OffsetRight = -40;
        line.OffsetTop = 60;
        line.OffsetBottom = 63;
        wrap.AddChild(line);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 18);
        row.Alignment = BoxContainer.AlignmentMode.Center;
        row.SetAnchorsPreset(LayoutPreset.FullRect);
        wrap.AddChild(row);

        var chapters = Chapters();
        Button first = null;
        foreach (var (year, nameKey, descKey, unlocked, enter) in chapters)
        {
            var b = new Button();
            b.CustomMinimumSize = new Vector2(196, 120);
            b.AddThemeFontSizeOverride("font_size", 16);
            b.Disabled = !unlocked;
            if (unlocked)
            {
                b.Pressed += enter;
                b.MouseEntered += () => ShowDesc(descKey);
                b.FocusEntered += () => ShowDesc(descKey);
            }
            else
            {
                b.MouseEntered += () => ShowDesc(descKey);
                b.FocusEntered += () => ShowDesc(descKey);
            }
            b.SetMeta("yearKey", year);
            b.SetMeta("nameKey", nameKey);
            b.SetMeta("descKey", descKey);
            b.SetMeta("unlocked", unlocked);
            row.AddChild(b);
            _nodes.Add(b);
            if (unlocked && first == null) first = b;
        }

        // 节点描述（默认操作提示）
        _desc = new Label { Text = Loc.I.T("hub_hint") };
        _desc.AddThemeFontSizeOverride("font_size", 16);
        _desc.AddThemeColorOverride("font_color", new Color(0.85f, 0.81f, 0.72f));
        _desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _desc.HorizontalAlignment = HorizontalAlignment.Center;
        _desc.SetAnchorsPreset(LayoutPreset.CenterTop);
        _desc.OffsetLeft = -420;
        _desc.OffsetRight = 420;
        _desc.OffsetTop = -118;
        _desc.OffsetBottom = -58;
        AddChild(_desc);

        // 底部按钮
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 20);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        buttons.SetAnchorsPreset(LayoutPreset.CenterBottom);
        buttons.OffsetLeft = -320;
        buttons.OffsetRight = 320;
        buttons.OffsetTop = -52;
        buttons.OffsetBottom = -10;
        AddChild(buttons);

        var gal = new Button { Text = Loc.I.T("ui_gallery") };
        gal.AddThemeFontSizeOverride("font_size", 18);
        gal.CustomMinimumSize = new Vector2(200, 42);
        gal.Pressed += () => _main.ShowGallery(fromHub: true);
        buttons.AddChild(gal);

        var back = new Button { Text = Loc.I.T("ui_back") };
        back.AddThemeFontSizeOverride("font_size", 18);
        back.CustomMinimumSize = new Vector2(200, 42);
        back.Pressed += () => _main.ShowTitle();
        buttons.AddChild(back);

        RefreshNodes();
        first?.GrabFocus();
    }

    private void ShowDesc(string descKey) => _desc.Text = Loc.I.T(descKey);

    private void RefreshNodes()
    {
        var s = GameState.I.S;
        for (int i = 0; i < _nodes.Count; i++)
        {
            var b = _nodes[i];
            string nameKey = (string)b.GetMeta("nameKey");
            bool unlocked = (bool)b.GetMeta("unlocked");

            // 源头节点看 PrologueDone；南京节点看 Finished；其余未解锁
            bool done = i == 0 && s.PrologueDone || i == 2 && s.Finished;
            string status = done ? Loc.I.T("hub_status_done")
                : unlocked ? Loc.I.T("hub_status_open")
                : Loc.I.T("hub_status_locked");
            b.Text = $"{(string)b.GetMeta("yearKey")}\n{Loc.I.T(nameKey)}\n{status}";
        }
    }
}
