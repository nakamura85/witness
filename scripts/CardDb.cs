using System.Collections.Generic;
using Godot;

/// <summary>
/// 史料卡数据库：从 res://data/cards.json 读取 10 张卡的基础数据
/// （编号/关联证据/配图路径）。卡片文字（名称/引文/出处/背景）在本地化文件里。
/// </summary>
public static class CardDb
{
    public class CardDef
    {
        public string Id;   // film / rabe / ... / oral
        public int Num;     // 1-10
        public string Art;  // res://assets/art/card_*.jpg
    }

    public static List<CardDef> Load()
    {
        var list = new List<CardDef>();
        var f = FileAccess.Open("res://data/cards.json", FileAccess.ModeFlags.Read);
        if (f == null) return list;
        var parsed = Json.ParseString(f.GetAsText());
        f.Close();
        if (parsed.VariantType == Variant.Type.Nil) return list;
        foreach (var item in parsed.AsGodotArray())
        {
            var d = item.AsGodotDictionary();
            list.Add(new CardDef
            {
                Id = d.GetValueOrDefault("id", "").AsString(),
                Num = d.GetValueOrDefault("num", 0).AsInt32(),
                Art = d.GetValueOrDefault("art", "").AsString(),
            });
        }
        return list;
    }
}
