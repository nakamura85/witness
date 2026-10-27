using System.Collections.Generic;
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

    public override void _Ready()
    {
        I = this;
        Settings.EnsureLoaded();
        SetLang(Settings.Lang);
    }

    public void SetLang(string lang)
    {
        Lang = LocCore.Normalize(lang);
        _cur = LocCore.Load(Lang);
        _fallback = Lang == "zh-CN" ? _cur : LocCore.Load("zh-CN");
        Settings.Lang = Lang;
        Settings.Save();
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
}
