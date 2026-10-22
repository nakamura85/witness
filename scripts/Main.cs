using Godot;

namespace Witness;

/// <summary>
/// 入口节点（Main.tscn 根）：
/// 1) 装配全局字体（内嵌 Noto CJK，缺失时退回系统字体）；
/// 2) 管理屏幕切换：内容警告 → 标题 → 关卡 / 史料馆 / 结算。
/// 所有界面都是代码构建的 Control/Node2D，不依赖额外场景文件。
/// </summary>
public partial class Main : Node
{
    private Node _current;

    public override void _Ready()
    {
        SetupFont();
        ShowContentWarning(); // 硬红线：每次启动整屏内容警告（GDD §6.5）
    }

    private void SetupFont()
    {
        Font font = null;
        const string bundled = "res://assets/fonts/NotoSansCJKsc-Regular.otf";
        if (ResourceLoader.Exists(bundled))
            font = GD.Load<FontFile>(bundled);
        if (font == null)
        {
            // 兜底：直接用系统字体（仅影响本机预览；发布版应内嵌 Noto 字体，OFL 许可）
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

    /// <summary>切换当前屏幕（旧屏幕直接释放）。</summary>
    private void Swap(Node next)
    {
        if (_current != null)
        {
            _current.QueueFree();
            _current = null;
        }
        _current = next;
        AddChild(next);
    }

    public void ShowContentWarning() => Swap(new ContentWarning(this));
    public void ShowTitle() => Swap(new TitleScreen(this));
    public void ShowHub() => Swap(new ArchiveHub(this));
    public void ShowGallery(bool fromHub = false) => Swap(new GalleryUi(this, fromHub));
    public void ShowSettlement() => Swap(new SettlementUi(this));

    public void StartPrologue() => Swap(new PrologueLevel(this));
    public void StartNanjing() => Swap(new Level(this));

    /// <summary>新的见证：重置进度后进入记忆档案馆（v0.2 起以档案馆为中枢）。</summary>
    public void StartNewGame()
    {
        GameState.I.Reset();
        ShowHub();
    }

    public void ContinueGame()
    {
        if (GameState.I.LoadSave()) ShowHub();
        else StartNewGame();
    }
}
