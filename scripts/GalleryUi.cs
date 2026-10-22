using Godot;

namespace Witness;

/// <summary>
/// 史料馆（个人记忆档案馆，企划书核心循环的长期层）：
/// 12 个档案格，通关解锁的亮起可点开，未解锁的以「？？？」沉默存在。
/// </summary>
public partial class GalleryUi : CanvasLayer
{
    private readonly Main _main;
    private readonly bool _fromHub;

    public GalleryUi(Main main, bool fromHub = false)
    {
        _main = main;
        _fromHub = fromHub;
        Layer = 40;
    }

    public override void _Ready()
    {
        var bg = new ColorRect { Color = new Color(0.086f, 0.075f, 0.062f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);

        var root = new MarginContainer();
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddThemeConstantOverride("margin_left", 50);
        root.AddThemeConstantOverride("margin_right", 50);
        root.AddThemeConstantOverride("margin_top", 26);
        root.AddThemeConstantOverride("margin_bottom", 26);
        AddChild(root);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 16);
        root.AddChild(box);

        var title = new Label { Text = Loc.I.T("gal_title") };
        title.AddThemeFontSizeOverride("font_size", 30);
        title.AddThemeColorOverride("font_color", new Color(0.91f, 0.86f, 0.75f));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(title);

        var grid = new GridContainer { Columns = 5 };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 14);
        grid.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        box.AddChild(grid);

        int total = CardDb.Load().Count;
        for (int num = 1; num <= total; num++)
        {
            string cardId = $"card{num}";
            bool unlocked = GameState.I.S.CardsUnlocked.Contains(cardId);

            var b = new Button();
            b.CustomMinimumSize = new Vector2(212, 168);
            b.AddThemeFontSizeOverride("font_size", 16);
            if (unlocked)
            {
                b.Text = $"档案 {num:00}\n{Loc.I.T($"{cardId}_name")}";
                int captured = num; // 闭包副本
                b.Pressed += () => AddChild(new CardOverlayUi(captured));
            }
            else
            {
                b.Text = $"???\n{Loc.I.T("gal_locked")}";
                b.Disabled = true;
            }
            grid.AddChild(b);
        }

        var back = new Button { Text = Loc.I.T("ui_back") };
        back.AddThemeFontSizeOverride("font_size", 19);
        back.CustomMinimumSize = new Vector2(220, 44);
        back.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        back.Pressed += () =>
        {
            if (_fromHub) _main.ShowHub();
            else _main.ShowTitle();
        };
        box.AddChild(back);
    }
}
