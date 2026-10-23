using System;
using System.Collections.Generic;
using Godot;

namespace Witness;

/// <summary>
/// 档案核对：把 1895 / 1910 / 1931 三张纸条按年代放进对应格子里。
/// 这是「读纸条」与「看进军路线」之间的一步——玩家自己把因果顺序排出来，
/// 地图上的箭头才亮起。纸片用 Button 实现：可点选、可键盘操作。
/// </summary>
public partial class SortBoardUi : CanvasLayer
{
    public event Action<bool> Closed;

    private readonly int[] _years = { 1895, 1910, 1931 };
    private readonly string[] _titles = { "《马关条约》· 台湾割让", "《日韩合并条约》· 朝鲜并入", "九一八 · 关东军北上" };
    private readonly Dictionary<int, Button> _slips = new();
    private readonly Dictionary<int, Button> _slots = new();
    private readonly HashSet<int> _filled = new();

    private Label _toast;
    private Label _stamp;
    private Button _selected;
    private int _selectedYear;
    private float _toastT;
    private bool _solved;
    private float _solvedT;

    public SortBoardUi() { Layer = 22; }

    public override void _Ready()
    {
        var dim = new ColorRect { Color = new Color(0.05f, 0.04f, 0.03f, 0.62f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);

        var card = new Panel { Position = new Vector2(210, 88) };
        card.Size = new Vector2(860, 556);
        card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.90f, 0.855f, 0.745f),
            BorderColor = new Color(0.38f, 0.31f, 0.23f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            ShadowColor = new Color(0, 0, 0, 0.45f), ShadowSize = 14, ShadowOffset = new Vector2(6, 9),
            ContentMarginLeft = 0, ContentMarginTop = 0,
        });
        AddChild(card);

        Label Text(string s, Vector2 p, int size, Color c, bool bold = false)
        {
            var l = new Label { Text = s, Position = p };
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", c);
            l.MouseFilter = Control.MouseFilterEnum.Ignore;
            if (bold) l.AddThemeColorOverride("font_outline_color", new Color(0.25f, 0.20f, 0.15f, 0.25f));
            card.AddChild(l);
            return l;
        }

        Text("档案核对", new Vector2(44, 30), 30, new Color(0.18f, 0.15f, 0.12f));
        Text("把三张纸条按年代放进下面的格子里。顺序对了，地图上的进军路线才会亮起。",
            new Vector2(44, 78), 15, new Color(0.33f, 0.28f, 0.22f));
        Text("先点一张纸条，再点它该在的年份。放错会被退回。",
            new Vector2(44, 104), 14, new Color(0.45f, 0.38f, 0.29f));

        var line = new ColorRect { Color = new Color(0.35f, 0.28f, 0.21f, 0.55f) };
        line.Position = new Vector2(44, 134);
        line.Size = new Vector2(772, 2);
        card.AddChild(line);

        // 三个年份格
        for (int i = 0; i < 3; i++)
        {
            int year = _years[i];
            var slot = new Button
            {
                Name = "slot_" + year,
                Text = year.ToString(),
                Position = new Vector2(50 + i * 260, 160),
                CustomMinimumSize = new Vector2(240, 168),
            };
            slot.AddThemeFontSizeOverride("font_size", 40);
            slot.AddThemeColorOverride("font_color", new Color(0.44f, 0.36f, 0.27f));
            slot.AddThemeColorOverride("font_hover_color", new Color(0.55f, 0.20f, 0.16f));
            var empty = new StyleBoxFlat
            {
                BgColor = new Color(0.86f, 0.81f, 0.70f, 0.55f),
                BorderColor = new Color(0.42f, 0.34f, 0.25f, 0.75f),
                BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
                CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
            };
            var hov = (StyleBoxFlat)empty.Duplicate();
            hov.BgColor = new Color(0.93f, 0.87f, 0.74f, 0.75f);
            hov.BorderColor = new Color(0.55f, 0.20f, 0.16f, 0.9f);
            slot.AddThemeStyleboxOverride("normal", empty);
            slot.AddThemeStyleboxOverride("hover", hov);
            slot.Pressed += () => Place(year);
            card.AddChild(slot);
            _slots[year] = slot;
        }

        _trayLabel = Text("未归档", new Vector2(50, 358), 14, new Color(0.42f, 0.35f, 0.27f));

        // 纸条（故意打乱：1910 → 1931 → 1895）
        var order = new[] { 1910, 1931, 1895 };
        for (int i = 0; i < order.Length; i++)
        {
            int year = order[i];
            int idx = Array.IndexOf(_years, year);
            var slip = MakeSlip(year, _titles[idx], new Vector2(50 + i * 260, 392));
            card.AddChild(slip);
            _slips[year] = slip;
        }

        _toast = Text("", new Vector2(50, 508), 16, new Color(0.62f, 0.20f, 0.15f));
        _toast.CustomMinimumSize = new Vector2(760, 0);

        var close = new Button
        {
            Text = "稍后再核对",
            Position = new Vector2(656, 30),
            CustomMinimumSize = new Vector2(160, 38),
        };
        close.AddThemeFontSizeOverride("font_size", 15);
        close.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = new Color(0.86f, 0.81f, 0.70f, 0.85f),
            BorderColor = new Color(0.42f, 0.34f, 0.25f, 0.8f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
        });
        close.AddThemeStyleboxOverride("hover", new StyleBoxFlat
        {
            BgColor = new Color(0.93f, 0.88f, 0.76f),
            BorderColor = new Color(0.55f, 0.20f, 0.16f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
        });
        close.AddThemeColorOverride("font_color", new Color(0.24f, 0.20f, 0.15f));
        close.Pressed += () => Close(false);
        card.AddChild(close);
        _skip = close;

        _stamp = new Label
        {
            Text = "已 归 档",
            Position = new Vector2(292, 512),
            Visible = false,
            Rotation = Mathf.DegToRad(-7f),
        };
        _stamp.AddThemeFontSizeOverride("font_size", 62);
        _stamp.AddThemeColorOverride("font_color", new Color(0.62f, 0.16f, 0.12f, 0.86f));
        _stamp.MouseFilter = Control.MouseFilterEnum.Ignore;
        card.AddChild(_stamp);
    }

    private Button _skip;
    private Label _trayLabel;

    private Button MakeSlip(int year, string title, Vector2 pos)
    {
        var b = new Button
        {
            Name = "slip_" + year,
            Text = $"{year}\n{title}",
            Position = pos,
            CustomMinimumSize = new Vector2(240, 104),
            PivotOffset = new Vector2(120, 52),
            Rotation = Mathf.DegToRad(year == 1895 ? -2.2f : year == 1910 ? 1.6f : -1.0f),
        };
        b.AddThemeFontSizeOverride("font_size", 17);
        var paper = new StyleBoxFlat
        {
            BgColor = new Color(0.945f, 0.905f, 0.795f),
            BorderColor = new Color(0.40f, 0.32f, 0.23f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            ShadowColor = new Color(0, 0, 0, 0.30f), ShadowSize = 6, ShadowOffset = new Vector2(3, 4),
            ContentMarginLeft = 10, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6,
        };
        var hover = (StyleBoxFlat)paper.Duplicate();
        hover.BgColor = new Color(0.97f, 0.93f, 0.82f);
        hover.BorderColor = new Color(0.55f, 0.20f, 0.16f);
        var picked = (StyleBoxFlat)paper.Duplicate();
        picked.BgColor = new Color(0.98f, 0.94f, 0.83f);
        picked.BorderColor = new Color(0.62f, 0.18f, 0.13f);
        picked.BorderWidthLeft = 3; picked.BorderWidthRight = 3;
        picked.BorderWidthTop = 3; picked.BorderWidthBottom = 3;

        b.AddThemeStyleboxOverride("normal", paper);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", picked);
        b.AddThemeColorOverride("font_color", new Color(0.18f, 0.15f, 0.12f));
        b.Pressed += () => Select(b, year);
        return b;
    }

    private void Select(Button b, int year)
    {
        if (_solved || _filled.Contains(year)) return;
        _selected = b;
        _selectedYear = year;
        foreach (var kv in _slips)
        {
            if (_filled.Contains(kv.Key)) continue;
            bool on = kv.Key == year;
            kv.Value.AddThemeStyleboxOverride("normal", on ? _pickedStyle : _paperStyle);
            kv.Value.AddThemeStyleboxOverride("hover", on ? _pickedStyle : _paperStyle);
        }
        Say($"已选中 {year} — 点上面它该在的年份。");
    }

    private readonly StyleBoxFlat _paperStyle = new()
    {
        BgColor = new Color(0.945f, 0.905f, 0.795f),
        BorderColor = new Color(0.40f, 0.32f, 0.23f),
        BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
        ShadowColor = new Color(0, 0, 0, 0.30f), ShadowSize = 6, ShadowOffset = new Vector2(3, 4),
        ContentMarginLeft = 10, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6,
    };

    private readonly StyleBoxFlat _pickedStyle = new()
    {
        BgColor = new Color(0.98f, 0.94f, 0.83f),
        BorderColor = new Color(0.62f, 0.18f, 0.13f),
        BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
        ShadowColor = new Color(0, 0, 0, 0.34f), ShadowSize = 7, ShadowOffset = new Vector2(3, 4),
        ContentMarginLeft = 10, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6,
    };

    private void Place(int slotYear)
    {
        if (_solved) return;
        if (_selected == null)
        {
            Say("先点下面一张纸条。");
            return;
        }
        if (_filled.Contains(slotYear)) return;

        if (_selectedYear != slotYear)
        {
            Say($"退回了：{_selectedYear} 年的纸条不属于 {slotYear}。");
            return;
        }

        _filled.Add(slotYear);
        _selected.MouseFilter = Control.MouseFilterEnum.Ignore;
        _selected.Position = new Vector2(_slots[slotYear].Position.X + 2, _slots[slotYear].Position.Y + 34);
        _selected.Rotation = 0f;
        _selected.Modulate = new Color(1, 1, 1, 0.96f);
        _slots[slotYear].Text = "";
        _selected = null;
        Say("");

        if (_filled.Count == 3) Solve();
    }

    private void Solve()
    {
        _solved = true;
        _stamp.Visible = true;
        _trayLabel.Visible = false;
        _toast.Text = "";
        _skip.Visible = false;
        GameState.I.S.MapOrdered = true;
        GameState.I.Save();
    }

    private void Say(string s)
    {
        _toast.Text = s;
        _toastT = 3f;
    }

    public override void _Process(double delta)
    {
        float f = (float)delta;
        if (_toastT > 0f)
        {
            _toastT -= f;
            if (_toastT <= 0f) _toast.Text = "";
        }
        if (_solved)
        {
            _solvedT += f;
            _stamp.Modulate = new Color(1, 1, 1, Mathf.Min(1f, _solvedT * 2.4f));
            if (_solvedT > 1.4f) Close(true);
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("pause")) Close(false);
    }

    private bool _closed;

    private void Close(bool solved)
    {
        if (_closed) return;
        _closed = true;
        Closed?.Invoke(solved || _solved);
        QueueFree();
    }
}
