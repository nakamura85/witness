using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Witness;

/// <summary>
/// Beat3「记忆的重量」取舍界面（GDD §6.4 / §9.4）：
/// 从已收集证据中最多选 3 件放入包袱；确认后不可反悔。
/// 一件没收到 → 特殊尾声文案（在结算呈现）。
/// </summary>
public partial class PackUi : CanvasLayer
{
    private readonly List<string> _avail = new();
    private readonly HashSet<string> _selected = new();
    private Action<List<string>> _onConfirm;

    private Label _note, _sel;
    private GridContainer _grid;
    private Button _confirm;

    public PackUi() { Layer = 44; }

    public override void _Ready()
    {
        var dim = new ColorRect { Color = new Color(0.055f, 0.05f, 0.045f, 0.88f) };
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
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            ContentMarginLeft = 26, ContentMarginRight = 26, ContentMarginTop = 20, ContentMarginBottom = 20,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        center.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);
        panel.AddChild(box);

        var title = new Label { Text = Loc.I.T("pack_title") };
        title.AddThemeFontSizeOverride("font_size", 27);
        title.AddThemeColorOverride("font_color", new Color(0.16f, 0.14f, 0.12f));
        box.AddChild(title);

        _note = new Label();
        _note.AddThemeFontSizeOverride("font_size", 15);
        _note.AddThemeColorOverride("font_color", new Color(0.55f, 0.20f, 0.16f));
        box.AddChild(_note);

        _grid = new GridContainer { Columns = 4 };
        _grid.AddThemeConstantOverride("h_separation", 10);
        _grid.AddThemeConstantOverride("v_separation", 10);
        box.AddChild(_grid);

        _sel = new Label();
        _sel.AddThemeFontSizeOverride("font_size", 17);
        _sel.AddThemeColorOverride("font_color", new Color(0.16f, 0.14f, 0.12f));
        box.AddChild(_sel);

        _confirm = new Button { Text = Loc.I.T("pack_confirm") };
        _confirm.AddThemeFontSizeOverride("font_size", 18);
        _confirm.Pressed += Confirm;
        box.AddChild(_confirm);

        Visible = false;
    }

    public void Open(List<string> avail, Action<List<string>> onConfirm)
    {
        _avail.Clear();
        _avail.AddRange(avail);
        _onConfirm = onConfirm;
        _selected.Clear();
        Visible = true;
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (var c in _grid.GetChildren())
            c.QueueFree();

        if (_avail.Count == 0)
        {
            _note.Text = Loc.I.T("pack_none");
            _grid.Visible = false;
        }
        else
        {
            _note.Text = Loc.I.T("pack_note");
            _grid.Visible = true;
            var ordered = _avail
                .OrderBy(id => WitnessState.EvidenceToCard[id]);
            foreach (var id in ordered)
            {
                string captured = id; // 闭包副本
                var b = new Button { ToggleMode = true };
                b.CustomMinimumSize = new Vector2(190, 86);
                int num = WitnessState.EvidenceToCard[id];
                b.Text = $"档案 {num:00}\n{Loc.I.T($"card{num}_name")}";
                b.AddThemeFontSizeOverride("font_size", 15);
                b.Toggled += on =>
                {
                    if (on)
                    {
                        if (_selected.Count >= 3) // 超过 3 件直接拒绝勾选
                        {
                            b.SetPressedNoSignal(false);
                            return;
                        }
                        _selected.Add(captured);
                    }
                    else _selected.Remove(captured);
                    UpdateSel();
                };
                _grid.AddChild(b);
            }
        }
        UpdateSel();
    }

    private void UpdateSel()
    {
        _sel.Text = Loc.I.Tf("pack_selected", _selected.Count);
        _confirm.Disabled = _selected.Count == 0;
    }

    private void Confirm()
    {
        var carried = _avail
            .Where(_selected.Contains)
            .OrderBy(id => WitnessState.EvidenceToCard[id])
            .ToList();
        Visible = false;
        _onConfirm?.Invoke(carried);
        _onConfirm = null;
    }
}
