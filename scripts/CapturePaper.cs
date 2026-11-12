using System;
using System.IO;
using Godot;

namespace Witness;

/// <summary>
/// v3.68 烧毁档案纸实机截图。CanvasLayer 在 SubViewport 里合成不出来，
/// 所以直接把 BurntPaperPanel（普通 Control）装进 SubViewport，读 GetTexture() 出真图。
/// 运行：Godot --path . --rendering-driver opengl3 --resolution 1280x720 scenes/CapturePaper.tscn
/// 产物：res://_shots/paper_*.png
/// 每张纸用不同的真实档案（不同 seed），用来核对「纸张不要全部都一样」。
/// </summary>
public partial class CapturePaper : Node
{
    private SubViewport _sub;
    private Control _holder;
    private float _t;
    private int _stage;
    private static string Dir => ProjectSettings.GlobalizePath("res://_shots");

    public override void _Ready()
    {
        _sub = new SubViewport
        {
            Size = new Vector2I(1280, 720),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(_sub);
        _holder = new Control();
        _holder.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _sub.AddChild(_holder);

        Build(0);
        _t = 0f;
        _stage = 0;
        GD.Print("CAPTURE_PAPER ready");
    }

    private void Build(int which)
    {
        foreach (Node c in _holder.GetChildren())
        {
            _holder.RemoveChild(c);
            c.QueueFree();
        }

        var dim = new ColorRect { Color = new Color(0.10f, 0.09f, 0.08f, 0.96f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _holder.AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _holder.AddChild(center);

        MartyrRecord rec = which < 3 ? MartyrArchive.N918[which] : MartyrArchive.Jiangqiao[which - 3];
        string date = which < 3
            ? "九一八 · 1931 年 9 月 18 日 · 沈阳近郊"
            : "嫩江江桥 · 1931 年 11 月 · 三间房";

        var paper = new BurntPaperPanel(BurntPaperArt.StableHash(date + "|" + rec.Title), 0.55f)
        { CustomMinimumSize = new Vector2(940, 0) };
        paper.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        paper.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        center.AddChild(paper);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 13);
        paper.AddChild(box);

        var d = new Label { Text = date };
        d.AddThemeFontSizeOverride("font_size", 14);
        d.AddThemeColorOverride("font_color", new Color(0.44f, 0.36f, 0.27f));
        box.AddChild(d);

        var t = new Label { Text = rec.Title };
        t.AddThemeFontSizeOverride("font_size", 25);
        t.AddThemeColorOverride("font_color", InkStyle.InkColor);
        t.CustomMinimumSize = new Vector2(820, 0);
        box.AddChild(t);

        box.AddChild(new InkRule { CustomMinimumSize = new Vector2(820, 9) });

        foreach (var line in rec.Lines)
        {
            var l = new Label { Text = line };
            l.AddThemeFontSizeOverride("font_size", 16);
            l.AddThemeColorOverride("font_color", new Color(0.20f, 0.16f, 0.13f));
            l.AddThemeConstantOverride("line_spacing", 4);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Vector2(820, 0);
            box.AddChild(l);
        }

        var src = new Label { Text = rec.Source };
        src.AddThemeFontSizeOverride("font_size", 13);
        src.AddThemeColorOverride("font_color", new Color(0.42f, 0.35f, 0.28f));
        src.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        src.CustomMinimumSize = new Vector2(820, 0);
        box.AddChild(src);
    }

    /// <summary>
    /// 画廊专用帧：两张不同 seed 的档案纸并排放在同一个 Godot 实机帧里，
    /// 直接证明「纸张不要全部都一样」。不是后期拼接，两者都是真实 BurntPaperPanel 节点。
    /// </summary>
    private void BuildVariants()
    {
        foreach (Node c in _holder.GetChildren())
        {
            _holder.RemoveChild(c);
            c.QueueFree();
        }

        var dim = new ColorRect { Color = new Color(0.10f, 0.09f, 0.08f, 0.96f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _holder.AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _holder.AddChild(center);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 34);
        center.AddChild(row);

        AddVariant(row, "九一八 · 1931 年 9 月 18 日 · 沈阳近郊", MartyrArchive.N918[0]);
        AddVariant(row, "嫩江江桥 · 1931 年 11 月 · 三间房", MartyrArchive.Jiangqiao[0]);
    }

    private void AddVariant(HBoxContainer row, string date, MartyrRecord rec)
    {
        var paper = new BurntPaperPanel(BurntPaperArt.StableHash(date + "|" + rec.Title), 0.55f)
        { CustomMinimumSize = new Vector2(520, 0) };
        paper.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(paper);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        paper.AddChild(box);

        var d = new Label { Text = date };
        d.AddThemeFontSizeOverride("font_size", 12);
        d.AddThemeColorOverride("font_color", new Color(0.44f, 0.36f, 0.27f));
        box.AddChild(d);

        var t = new Label { Text = rec.Title };
        t.AddThemeFontSizeOverride("font_size", 20);
        t.AddThemeColorOverride("font_color", InkStyle.InkColor);
        t.CustomMinimumSize = new Vector2(420, 0);
        box.AddChild(t);

        box.AddChild(new InkRule { CustomMinimumSize = new Vector2(420, 9) });

        foreach (var line in rec.Lines)
        {
            var l = new Label { Text = line };
            l.AddThemeFontSizeOverride("font_size", 13);
            l.AddThemeColorOverride("font_color", new Color(0.20f, 0.16f, 0.13f));
            l.AddThemeConstantOverride("line_spacing", 3);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Vector2(420, 0);
            box.AddChild(l);
        }
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_stage == 0 && _t > 1.0f)
        {
            SaveShot("paper_01_918_zhangzhanyuan.png", () => Build(1));
            _stage = 1;
        }
        else if (_stage == 1 && _t > 2.0f)
        {
            SaveShot("paper_02_918_seventh_brigade.png", () => Build(3));
            _stage = 2;
        }
        else if (_stage == 2 && _t > 3.0f)
        {
            SaveShot("paper_03_jiangqiao_zhangjiuzhou.png", () => Build(5));
            _stage = 3;
        }
        else if (_stage == 3 && _t > 4.0f)
        {
            SaveShot("paper_04_jiangqiao_zhangdexin.png", BuildVariants);
            _stage = 4;
        }
        else if (_stage == 4 && _t > 5.0f)
        {
            SaveShot("paper_05_variants.png");
            _stage = 5;
        }
        else if (_stage == 5 && _t > 5.9f)
        {
            GD.Print("CAPTURE_PAPER done");
            GetTree().Quit();
        }
    }

    private async void SaveShot(string name, Action onSaved = null)
    {
        Image img = null;
        for (int i = 0; i < 20 && img == null; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var tex = _sub.GetTexture();
            if (tex != null) img = tex.GetImage();
        }
        if (img == null) { GD.PrintErr("CAPTURE_PAPER skip " + name); return; }
        if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
        img.SavePng(Path.Combine(Dir, name));
        GD.Print("CAPTURE_PAPER saved " + name);
        onSaved?.Invoke();
    }
}
