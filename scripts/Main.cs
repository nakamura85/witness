using Godot;

namespace Witness;

/// <summary>
/// 入口节点（Main.tscn 根）：
/// 1) 装配全局字体（内嵌 Noto CJK，缺失时退回系统字体）；
/// 2) 管理屏幕切换：内容警告 → 标题 → 关卡 / 史料馆 / 结算。
/// 所有界面都是代码构建的 Control/Node2D，不依赖额外场景文件。
/// v5b：Swap 加 fade-to-black 过场（0.15s 遮黑 → 换屏 → 0.25s 淡入），模拟《勇敢的心》丝滑转场。
/// </summary>
public partial class Main : Node
{
    private Node _current;
    private ColorRect _fade;   // fade-to-black 用的全屏遮罩

    public override void _Ready()
    {
        SetupFont();
        SetupFade();
        ShowContentWarning();
    }

    private void SetupFont()
    {
        Font font = null;
        const string bundled = "res://assets/fonts/NotoSansCJKsc-Regular.otf";
        if (ResourceLoader.Exists(bundled))
            font = GD.Load<FontFile>(bundled);
        if (font == null)
        {
            var sys = new SystemFont();
            sys.FontNames = new[]
            {
                "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC",
                "Noto Sans CJK SC", "Hiragino Sans", "Malgun Gothic", "Arial",
            };
            font = sys;
        }
        ThemeDB.FallbackFont = font;
    }

    private void SetupFade()
    {
        _fade = new ColorRect
        {
            Color = new Color(0.04f, 0.03f, 0.03f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 100,   // 顶层，盖住一切
        };
        _fade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_fade);
    }

    /// <summary>v5b 丝滑转场：fade-to-black（0.15s 遮黑 → 换屏 → 0.25s 淡入）。</summary>
    private void Swap(Node next)
    {
        // CanvasItem 子类（Node2D / Control）才有 Modulate
        if (next is CanvasItem ci)
            ci.Modulate = new Color(1, 1, 1, 1);

        // 如果已有旧屏幕，先遮黑再换；首次启动没有旧屏幕，直接淡入
        if (_current == null)
        {
            _current = next;
            AddChild(next);
            if (next is Control c)
                c.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _fade.Color = new Color(0.04f, 0.03f, 0.03f, 1f);
            var t = CreateTween();
            t.TweenProperty(_fade, "color:a", 0f, 0.35f).SetTrans(Tween.TransitionType.Sine);
            return;
        }

        // 已有旧屏幕：0.15s 遮黑 → callback 换屏 → 0.25s 淡入
        var fade = _fade;
        var oldScreen = _current;
        _current = next;

        fade.Color = new Color(0.04f, 0.03f, 0.03f, 0f);
        fade.Visible = true;
        var tween = CreateTween();
        tween.SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(fade, "color:a", 1f, 0.15f);
        tween.TweenCallback(Callable.From(() =>
        {
            // never let a bad screen _Ready strand us under a black veil
            try
            {
                oldScreen.QueueFree();
                AddChild(next);
                if (next is Control c)
                    c.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            }
            catch (System.Exception e)
            {
                GD.PushError($"[Main] swap into {next.GetType().Name} failed: {e.Message}");
            }
        }));
        tween.TweenInterval(0.05f);
        tween.TweenProperty(fade, "color:a", 0f, 0.25f);
    }

    public void ShowContentWarning() => Swap(new ContentWarning(this));
    public void ShowTitle() => Swap(new TitleScreen(this));
    public void ShowHub() => Swap(new ArchiveHub(this));
    public void ShowAsiaMap() => Swap(new AsiaMap(this));
    public void ShowGallery(bool fromHub = false) => Swap(new GalleryUi(this, fromHub));
    public void ShowSettlement() => Swap(new SettlementUi(this));

    public void StartPrologue() => Swap(new PrologueLevel(this));
    public void StartNanjing() => Swap(new Level(this));
    public void StartJiangqiao() => Swap(new JiangqiaoLevel(this));

    /// <summary>新的见证：先走 1895/1910 源头纸条 → 东亚大地图 → 1931 嫩江江桥。</summary>
    public void StartNewGame()
    {
        GameState.I.Reset();
        ShowAsiaMap();
    }

    public void ContinueGame()
    {
        if (GameState.I.LoadSave()) ShowAsiaMap();
        else StartNewGame();
    }
}
