using System.Text.Json;
using Godot;
using Witness;

/// <summary>
/// Autoload：整局状态容器 + user://save.json 存档。
/// 存档内容 = WitnessState（证据/选择/难民/取舍/史料卡解锁），每个 Beat 开始与结算时自动保存。
/// </summary>
public partial class GameState : Node
{
    public static GameState I { get; private set; }

    public WitnessState S { get; private set; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        IncludeFields = true,   // WitnessState 用的是字段（fields），需要显式包含
        WriteIndented = true,
    };

    public override void _Ready() => I = this;

    public void Reset()
    {
        S = new WitnessState();
        Save();
    }

    public bool HasSave() => FileAccess.FileExists("user://save.json");

    public bool LoadSave()
    {
        var f = FileAccess.Open("user://save.json", FileAccess.ModeFlags.Read);
        if (f == null) return false;
        try
        {
            var s = JsonSerializer.Deserialize<WitnessState>(f.GetAsText(), JsonOpts);
            f.Close();
            if (s != null) { S = s; return true; }
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"[GameState] 存档读取失败: {e.Message}");
        }
        return false;
    }

    public void Save()
    {
        var f = FileAccess.Open("user://save.json", FileAccess.ModeFlags.Write);
        if (f == null) return;
        f.StoreString(JsonSerializer.Serialize(S, JsonOpts));
        f.Close();
    }
}
