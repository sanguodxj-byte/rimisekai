namespace Rimisekai.Defs;

/// <summary>
/// 防具槽位基表。五件甲各有一个基础防御与**物品名词**，
/// 两者都以这张表为准（equipment.xml），代码里只有加载失败时的兜底。
///
/// 名词与槽位显示名是两回事：槽位在界面上叫"帽子/上装/下装"（玩家对装备栏的称呼），
/// 而拼进物品名的必须是"帽/甲/腿/手/靴"这类能单独成词的名词——
/// "铁上装"不是人话，"铁甲""铁帽"才是。
/// </summary>
public sealed class ArmorSlotDef : Def
{
    /// <summary>基础防御（材料加成与品质乘数都算在这之上）。</summary>
    public int BaseDefence { get; init; }

    /// <summary>基础身价（材料价值系数与品质系数都乘在这之上）。</summary>
    public int BaseValue { get; init; }

    /// <summary>拼名字用的物品名词，如"盔""甲""腿""手""靴"。</summary>
    public string Noun { get; init; } = "";
}

/// <summary>防具槽位的基础防御与物品名词，严格由 equipment.xml 驱动。</summary>
public static class ArmorSlots
{
    /// <summary>这个槽位的物品名词：布甲/皮甲/铁甲的"甲"，铁帽的"帽"。</summary>
    public static string Noun(EquipSlot slot)
    {
        DefLoader.EnsureInitialized();
        var def = DefDatabase<ArmorSlotDef>.Get(slot.ToString());
        if (def == null || string.IsNullOrEmpty(def.Noun))
            throw new System.Collections.Generic.KeyNotFoundException($"未在 equipment.xml 中配置防具槽位 {slot} 的物品名词 Noun");
        return def.Noun;
    }

    /// <summary>这个槽位的基础防御。</summary>
    public static int BaseDefence(EquipSlot slot)
    {
        DefLoader.EnsureInitialized();
        var def = DefDatabase<ArmorSlotDef>.Get(slot.ToString());
        if (def == null)
            throw new System.Collections.Generic.KeyNotFoundException($"未在 equipment.xml 中配置防具槽位 {slot} 的基础防御 BaseDefence");
        return def.BaseDefence;
    }

    /// <summary>这个槽位的基础身价。</summary>
    public static int BaseValue(EquipSlot slot)
    {
        DefLoader.EnsureInitialized();
        var def = DefDatabase<ArmorSlotDef>.Get(slot.ToString());
        if (def == null)
            throw new System.Collections.Generic.KeyNotFoundException($"未在 equipment.xml 中配置防具槽位 {slot} 的基础身价 BaseValue");
        return def.BaseValue;
    }
}
