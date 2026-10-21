using System.Collections.Generic;
using System.Text.Json;
using Godot;
using Witness;

/// <summary>
/// Autoload：当前语言 + 文本查询。
/// 用法：Loc.I.T("hud_prompt_record")；带参数 Loc.I.Tf("set_helped", 7)。
/// 语言偏好存 user://settings.json。
/// </summary>
public partial class Loc : Node
{
    public static Loc I { get; private set; }

    public string Lang { get; private set; } = "zh-CN";
    private Dictionary<string, string> _cur = new();
    private Dictionary<string, string> _fallback = new();

    [Signal] public delegate void LanguageChangedEventHandler();

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public override void _Ready()
    {
        I = this;
        SetLang(ReadStoredLang());
    }

    public void SetLang(string lang)
    {
        Lang = LocCore.Normalize(lang);
        _cur = LocCore.Load(Lang);
        _fallback = Lang == "zh-CN" ? _cur : LocCore.Load("zh-CN");
        WriteStoredLang();
        EmitSignal(SignalName.LanguageChanged);
    }

    /// <summary>查词条；缺键回退简中；再缺则原样返回键名（便于发现漏翻）。</summary>
    public string T(string key)
    {
        if (_cur.TryGetValue(key, out var v)) return v;
        if (_fallback.TryGetValue(key, out var f)) return f;
        GD.PushWarning($"[Loc] 缺少词条: {key} ({Lang})");
        return key;
    }

    public string Tf(string key, params object[] args) => string.Format(T(key), args);

    // ---- 语言偏好持久化 ----

    private string ReadStoredLang()
    {
        var f = FileAccess.Open("user://settings.json", FileAccess.ModeFlags.Read);
        if (f == null) return "zh-CN";
        var parsed = Json.ParseString(f.GetAsText());
        f.Close();
        if (parsed.VariantType == Variant.Type.Nil) return "zh-CN";
        return parsed.AsGodotDictionary().GetValueOrDefault("lang", "zh-CN").AsString();
    }

    private void WriteStoredLang()
    {
        var f = FileAccess.Open("user://settings.json", FileAccess.ModeFlags.Write);
        if (f == null) return;
        f.StoreString(JsonSerializer.Serialize(new SettingsData { lang = Lang }, JsonOpts));
        f.Close();
    }

    private class SettingsData { public string lang { get; set; } }
}
