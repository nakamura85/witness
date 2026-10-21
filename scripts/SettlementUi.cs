using System.Collections.Generic;
using Godot;

namespace Witness;

/// <summary>
/// 结算三栏（GDD §9.5）：带出的记忆 / 湮没的记录 / 你的见证。
/// 沉默地呈现结果 —— 没有评分、没有评级，只有名单与数字。
/// </summary>
public partial class SettlementUi : CanvasLayer
{
    private readonly Main _main;

    public SettlementUi(Main main)
    {
        _main = main;
        Layer = 40;
    }

    public override void _Ready()
    {
        var s = GameState.I.S;

        // 旧纸底色
        var bg = new ColorRect { Color = new Color(0.89f, 0.84f, 0.73f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);

        var root = new MarginContainer();
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddThemeConstantOverride("margin_left", 60);
        root.AddThemeConstantOverride("margin_right", 60);
        root.AddThemeConstantOverride("margin_top", 34);
        root.AddThemeConstantOverride("margin_bottom", 34);
        AddChild(root);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 18);
        root.AddChild(box);

        var title = new Label { Text = Loc.I.T("set_title") };
        title.AddThemeFontSizeOverride("font_size", 30);
        title.AddThemeColorOverride("font_color", new Color(0.16f, 0.14f, 0.12f));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(title);

        var rule = new ColorRect
        {
            Color = new Color(0.55f, 0.20f, 0.16f, 0.7f),
            CustomMinimumSize = new Vector2(0, 2),
        };
        box.AddChild(rule);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 26);
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        box.AddChild(columns);

        // —— 第一栏：带出的记忆 ——
        var carriedItems = new List<string>();
        foreach (var id in WitnessState.EvidenceIds)
        {
            if ((EvidenceStatus)s.Evidence[id] == EvidenceStatus.Carried)
            {
                int num = WitnessState.EvidenceToCard[id];
                string line = $"档案 {num:00}  {Loc.I.T($"card{num}_name")}";
                if (s.TornMarked.Contains(id)) line += " · " + Loc.I.T("set_torn_note");
                carriedItems.Add(line);
            }
        }
        columns.AddChild(MakeColumn(Loc.I.T("set_carried"), carriedItems, false));

        // —— 第二栏：湮没的记录 ——
        var lostItems = new List<string>();
        foreach (var id in WitnessState.EvidenceIds)
        {
            var st = (EvidenceStatus)s.Evidence[id];
            if (st == EvidenceStatus.Lost || st == EvidenceStatus.Destroyed)
            {
                int num = WitnessState.EvidenceToCard[id];
                lostItems.Add($"档案 {num:00}  {Loc.I.T($"card{num}_name")} · "
                    + Loc.I.T(st == EvidenceStatus.Destroyed ? "set_destroyed_note" : "set_lost_note"));
            }
        }
        columns.AddChild(MakeColumn(Loc.I.T("set_lost"),
            lostItems.Count > 0 ? lostItems : new List<string> { Loc.I.T("set_none") }, false));

        // —— 第三栏：你的见证 ——
        int lostCount = lostItems.Count;
        var stats = new List<string>
        {
            Loc.I.Tf("set_helped", s.HelpedCount),
            Loc.I.Tf("set_recorded", s.RecordedCount),
            Loc.I.Tf("set_lost_count", lostCount),
            "",
            Loc.I.Tf("set_unlocked", s.CardsUnlocked.Count),
            "",
            Loc.I.T("set_what_left"),
        };
        columns.AddChild(MakeColumn(Loc.I.T("set_stats"), stats, true));

        // —— 按钮 ——
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 18);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        box.AddChild(buttons);

        var openGallery = new Button { Text = Loc.I.T("set_open_gallery") };
        openGallery.AddThemeFontSizeOverride("font_size", 19);
        openGallery.CustomMinimumSize = new Vector2(240, 46);
        openGallery.Pressed += () => _main.ShowGallery();
        buttons.AddChild(openGallery);

        var toTitle = new Button { Text = Loc.I.T("ui_to_title") };
        toTitle.AddThemeFontSizeOverride("font_size", 19);
        toTitle.CustomMinimumSize = new Vector2(240, 46);
        toTitle.Pressed += () => _main.ShowTitle();
        buttons.AddChild(toTitle);
    }

    private static VBoxContainer MakeColumn(string header, List<string> items, bool faded)
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        col.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        var h = new Label { Text = header };
        h.AddThemeFontSizeOverride("font_size", 21);
        h.AddThemeColorOverride("font_color", new Color(0.55f, 0.20f, 0.16f));
        col.AddChild(h);

        var line = new ColorRect
        {
            Color = new Color(0.16f, 0.14f, 0.12f, 0.35f),
            CustomMinimumSize = new Vector2(0, 1),
        };
        col.AddChild(line);

        var textColor = faded
            ? new Color(0.42f, 0.39f, 0.33f)
            : new Color(0.16f, 0.14f, 0.12f);
        foreach (var item in items)
        {
            var l = new Label { Text = item };
            l.AddThemeFontSizeOverride("font_size", 17);
            l.AddThemeColorOverride("font_color", textColor);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            col.AddChild(l);
        }
        return col;
    }
}
