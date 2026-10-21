using System.Collections.Generic;
using Godot;

namespace Witness;

/// <summary>
/// 本地化核心（纯逻辑，供 Loc autoload 与 headless 测试共用）。
/// 9 种语言：简中/繁中/英/日/韩/德/法/俄/马来文；缺键回退简体中文。
/// </summary>
public static class LocCore
{
    /// <summary>(语言代码, 显示名) —— 顺序即标题界面选择器顺序，简中默认。</summary>
    public static readonly (string Code, string Name)[] Languages =
    {
        ("zh-CN", "简体中文"),
        ("zh-TW", "繁體中文"),
        ("en",    "English"),
        ("ja",    "日本語"),
        ("ko",    "한국어"),
        ("de",    "Deutsch"),
        ("fr",    "Français"),
        ("ru",    "Русский"),
        ("ms",    "Bahasa Melayu"),
    };

    /// <summary>把系统/存档里的语言标签归一化到 9 种支持语言之一（默认简中）。</summary>
    public static string Normalize(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return "zh-CN";
        var t = tag.Replace('_', '-').Trim();
        foreach (var (code, _) in Languages)
            if (string.Equals(code, t, System.StringComparison.OrdinalIgnoreCase))
                return code;
        var lower = t.ToLowerInvariant();
        if (lower.StartsWith("zh"))
            return lower.Contains("hant") || lower.Contains("tw") || lower.Contains("hk") || lower.Contains("mo")
                ? "zh-TW" : "zh-CN";
        return lower.Substring(0, System.Math.Min(2, lower.Length)) switch
        {
            "en" => "en", "ja" => "ja", "ko" => "ko", "de" => "de",
            "fr" => "fr", "ru" => "ru", "ms" => "ms", _ => "zh-CN",
        };
    }

    /// <summary>从 res://data/loc/{lang}.json 载入键值表；文件缺失返回空表。</summary>
    public static Dictionary<string, string> Load(string lang)
    {
        var dict = new Dictionary<string, string>();
        var file = FileAccess.Open($"res://data/loc/{lang}.json", FileAccess.ModeFlags.Read);
        if (file == null) return dict;
        var parsed = Json.ParseString(file.GetAsText());
        file.Close();
        if (parsed.VariantType == Variant.Type.Nil) return dict;
        var data = parsed.AsGodotDictionary();
        foreach (var kv in data)
            dict[kv.Key.AsString()] = kv.Value.AsString();
        return dict;
    }
}
