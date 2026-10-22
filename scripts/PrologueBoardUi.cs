using System.Collections.Generic;
using Godot;

namespace Witness;

/// <summary>
/// 时间线板（Patrick's Parabox 式「归位」解谜的简化版）：
/// 从拾取的碎片中选一枚，放回 1895 / 1910 两个年代槽位。
/// 放错不惩罚只提示；全部归位 → 回调关卡播放历史场景。
/// </summary>
public partial class PrologueBoardUi : CanvasLayer
{
    [Signal] public delegate void FinishedEventHandler();

    private Label _fragName, _progress, _feedback, _slot1895, _slot1910;
    private Button _prev, _next, _place1895, _place1910;
    private int _sel;

    public PrologueBoardUi() { Layer = 42; }

    public override void _Ready()
    {
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.72f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.90f, 0.85f, 0.74f),
            BorderColor = new Color(0.16f, 0.14f, 0.12f),
            BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
            ContentMarginLeft = 30, ContentMarginRight = 30, ContentMarginTop = 24, ContentMarginBottom = 24,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        panel.CustomMinimumSize = new Vector2(880, 0);
        center.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);
        panel.AddChild(box);

        var title = new Label { Text = Loc.I.T("pro_board_title") };
        title.AddThemeFontSizeOverride("font_size", 26);
        title.AddThemeColorOverride("font_color", new Color(0.16f, 0.14f, 0.12f));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(title);

        var hint = new Label { Text = Loc.I.T("pro_board_hint") };
        hint.AddThemeFontSizeOverride("font_size", 15);
        hint.AddThemeColorOverride("font_color", new Color(0.5f, 0.46f, 0.4f));
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(hint);

        // 碎片轮选：◀ 名称 ▶
        var carousel = new HBoxContainer();
        carousel.AddThemeConstantOverride("separation", 16);
        carousel.Alignment = BoxContainer.AlignmentMode.Center;
        box.AddChild(carousel);

        _prev = CarouselButton("◀");
        _prev.Pressed += () => { _sel--; Refresh(); };
        carousel.AddChild(_prev);

        _fragName = new Label();
        _fragName.AddThemeFontSizeOverride("font_size", 22);
        _fragName.AddThemeColorOverride("font_color", new Color(0.28f, 0.25f, 0.21f));
        _fragName.CustomMinimumSize = new Vector2(420, 0);
        _fragName.HorizontalAlignment = HorizontalAlignment.Center;
        carousel.AddChild(_fragName);

        _next = CarouselButton("▶");
        _next.Pressed += () => { _sel++; Refresh(); };
        carousel.AddChild(_next);

        // 放置槽位按钮
        var places = new HBoxContainer();
        places.AddThemeConstantOverride("separation", 20);
        places.Alignment = BoxContainer.AlignmentMode.Center;
        box.AddChild(places);

        _place1895 = PlaceButton("pro_place_1895", "1895");
        _place1910 = PlaceButton("pro_place_1910", "1910");
        places.AddChild(_place1895);
        places.AddChild(_place1910);

        // 进度与槽内已放回的碎片
        _progress = new Label();
        _progress.AddThemeFontSizeOverride("font_size", 16);
        _progress.AddThemeColorOverride("font_color", new Color(0.5f, 0.46f, 0.4f));
        _progress.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_progress);

        _slot1895 = SlotLabel();
        _slot1910 = SlotLabel();
        box.AddChild(_slot1895);
        box.AddChild(_slot1910);

        _feedback = new Label();
        _feedback.AddThemeFontSizeOverride("font_size", 17);
        _feedback.HorizontalAlignment = HorizontalAlignment.Center;
        _feedback.CustomMinimumSize = new Vector2(0, 26);
        box.AddChild(_feedback);

        Refresh();
    }

    private Button CarouselButton(string glyph)
    {
        var b = new Button { Text = glyph };
        b.AddThemeFontSizeOverride("font_size", 22);
        b.CustomMinimumSize = new Vector2(56, 44);
        return b;
    }

    private Button PlaceButton(string key, string year)
    {
        var b = new Button { Text = Loc.I.T(key) };
        b.AddThemeFontSizeOverride("font_size", 19);
        b.CustomMinimumSize = new Vector2(330, 52);
        b.Pressed += () => TryPlace(year);
        return b;
    }

    private Label SlotLabel()
    {
        var l = new Label();
        l.AddThemeFontSizeOverride("font_size", 15);
        l.AddThemeColorOverride("font_color", new Color(0.42f, 0.39f, 0.33f));
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.HorizontalAlignment = HorizontalAlignment.Center;
        return l;
    }

    /// <summary>当前可放置（已拾取且未放回）的碎片。</summary>
    private List<string> Available()
    {
        var s = GameState.I.S;
        var list = new List<string>();
        foreach (var id in PrologueLogic.FragmentIds)
            if (s.PrologueCollected.Contains(id) && !s.ProloguePlaced.Contains(id))
                list.Add(id);
        return list;
    }

    private void TryPlace(string year)
    {
        var avail = Available();
        if (avail.Count == 0) return;
        var id = avail[Mathf.Clamp(_sel, 0, avail.Count - 1)];
        var s = GameState.I.S;

        if (PrologueLogic.Place(s, id, year))
        {
            _feedback.AddThemeColorOverride("font_color", new Color(0.30f, 0.45f, 0.25f));
            _feedback.Text = Loc.I.T("pro_board_correct");
            GameState.I.Save();
            if (PrologueLogic.AllPlaced(s))
            {
                // 让玩家看一眼最后一块的落位，再收板放场景
                SetEnabled(false);
                var t = GetTree().CreateTimer(1.0);
                t.Timeout += () => EmitSignal(SignalName.Finished);
            }
            Refresh();
        }
        else
        {
            _feedback.AddThemeColorOverride("font_color", new Color(0.55f, 0.20f, 0.16f));
            _feedback.Text = Loc.I.T("pro_board_wrong");
            Refresh();
        }
    }

    private void SetEnabled(bool on)
    {
        _prev.Disabled = !on;
        _next.Disabled = !on;
        _place1895.Disabled = !on;
        _place1910.Disabled = !on;
    }

    private void Refresh()
    {
        var s = GameState.I.S;
        var avail = Available();
        if (avail.Count == 0)
        {
            _fragName.Text = "—";
            _prev.Disabled = true;
            _next.Disabled = true;
            _place1895.Disabled = true;
            _place1910.Disabled = true;
        }
        else
        {
            if (_sel < 0) _sel = avail.Count - 1;
            if (_sel >= avail.Count) _sel = 0;
            _fragName.Text = Loc.I.T(PrologueLogic.NameKey(avail[_sel]));
            bool multi = avail.Count > 1;
            _prev.Disabled = !multi;
            _next.Disabled = !multi;
            _place1895.Disabled = false;
            _place1910.Disabled = false;
        }

        _progress.Text = Loc.I.Tf("pro_board_progress", s.ProloguePlaced.Count, PrologueLogic.FragmentIds.Length);
        _slot1895.Text = SlotText("1895");
        _slot1910.Text = SlotText("1910");
    }

    private string SlotText(string year)
    {
        var s = GameState.I.S;
        var names = new List<string>();
        foreach (var id in PrologueLogic.FragmentIds)
            if (year == PrologueLogic.YearOf(id) && s.ProloguePlaced.Contains(id))
                names.Add(Loc.I.T(PrologueLogic.NameKey(id)));
        string header = year switch
        {
            "1895" => Loc.I.T("pro_slot_1895"),
            _ => Loc.I.T("pro_slot_1910"),
        };
        return names.Count > 0 ? $"{header}  ✦ {string.Join(" · ", names)}" : $"{header}  ——";
    }
}
