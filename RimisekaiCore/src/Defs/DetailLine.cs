namespace Rimisekai.Defs;

/// <summary>
/// 物品详情的一行：标签 / 值，外加可选的一句注解（如附魔的效果）。
/// 前端按「暗标签＋亮数值」逐行排，不再由 Core 拼带全角空格的整串。
/// </summary>
public readonly record struct DetailLine(string Label, string Value, string Note = "");
