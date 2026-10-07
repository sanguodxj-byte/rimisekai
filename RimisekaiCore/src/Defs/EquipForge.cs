using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimisekai.Defs;

/// <summary>
/// 装备锻炉。防具按"材料 × 槽位"、饰品按"材料 × 类型"出实例，
/// 字段在生成时定，名字当场拼——与武器同一套哲学。
/// </summary>
public static class EquipForge
{
    private static int _nextId = 1;

    public static string NextId() => $"eqp_{_nextId++}";

    /// <summary>重置 Id 计数（读档时对齐）。</summary>
    public static void ResetIds(int next) => _nextId = next < 1 ? 1 : next;

    /// <summary>锻一件防具。</summary>
    public static EquipInstance ForgeArmor(EquipSlot slot, string materialDefName,
        Quality? quality = null, string? enchant = null, bool? blessed = null,
        int? enhance = null, Random? rng = null)
    {
        rng ??= new Random();
        var material = DefDatabase<MaterialDef>.Get(materialDefName);
        if (material == null || !material.ArmorUsable || !EquipSlots.Accepts(slot, EquipKind.Armor))
            throw new ArgumentException($"防具基座不存在：{materialDefName} × {slot}");

        var rolled = Roll(quality, enchant, blessed, enhance, rng);
        var draft = new EquipInstance
        {
            Id = NextId(),
            Slot = slot,
            Kind = EquipKind.Armor,
            MaterialDefName = materialDefName,
            Quality = rolled.Quality,
            Enchant = rolled.Enchant,
            Blessed = rolled.Blessed,
            Enhance = rolled.Enhance,
        };
        return new EquipInstance
        {
            Id = draft.Id,
            Slot = draft.Slot,
            Kind = draft.Kind,
            MaterialDefName = draft.MaterialDefName,
            Accessory = "",
            Quality = draft.Quality,
            Enchant = draft.Enchant,
            Blessed = draft.Blessed,
            Enhance = draft.Enhance,
            Name = NameOf(draft),
        };
    }

    /// <summary>锻一件饰品。戒指进 Ring1/Ring2，项链进 Neck。</summary>
    public static EquipInstance ForgeAccessory(EquipSlot slot, string accessoryDefName,
        string materialDefName, Quality? quality = null, string? enchant = null,
        bool? blessed = null, int? enhance = null, Random? rng = null)
    {
        rng ??= new Random();
        var accessory = DefDatabase<AccessoryDef>.Get(accessoryDefName);
        var material = DefDatabase<MaterialDef>.Get(materialDefName);
        if (accessory == null || material == null || !material.ArmorUsable
            || !EquipSlots.Accepts(slot, EquipKind.Accessory))
            throw new ArgumentException($"饰品基座不存在：{accessoryDefName} × {materialDefName}");

        var rolled = Roll(quality, enchant, blessed, enhance, rng);
        var draft = new EquipInstance
        {
            Id = NextId(),
            Slot = slot,
            Kind = EquipKind.Accessory,
            MaterialDefName = materialDefName,
            Accessory = accessoryDefName,
            Quality = rolled.Quality,
            Enchant = rolled.Enchant,
            Blessed = rolled.Blessed,
            Enhance = rolled.Enhance,
        };
        return new EquipInstance
        {
            Id = draft.Id,
            Slot = draft.Slot,
            Kind = draft.Kind,
            MaterialDefName = draft.MaterialDefName,
            Accessory = draft.Accessory,
            Quality = draft.Quality,
            Enchant = draft.Enchant,
            Blessed = draft.Blessed,
            Enhance = draft.Enhance,
            Name = NameOf(draft),
        };
    }

    /// <summary>
    /// 起名。**只有祝福与附魔进名字**，其余进详情。
    /// 防具：{祝福}{附魔}{材料}{槽位名}，如"受祝福的炽热的精金甲"。
    /// 饰品：{祝福}{附魔}{材料}{戒指/项链}，如"炽热的秘银戒指"。
    /// 饰品加什么属性是类型的事，写在详情里，不进名字。
    /// </summary>
    public static string NameOf(EquipInstance e)
    {
        var parts = new List<string>();
        if (e.Blessed)
            parts.Add("受祝福的");
        if (e.EnchantDef is { } enchant && enchant.Prefix.Length > 0)
            parts.Add(enchant.Prefix);

        var material = e.Material?.Label ?? "?";
        var noun = e.Kind == EquipKind.Accessory
            ? EquipSlots.AccessoryNoun(e.Slot)
            : ArmorSlots.Noun(e.Slot);
        return string.Join("", parts) + material + noun;
    }

    private static (Quality Quality, string Enchant, bool Blessed, int Enhance) Roll(
        Quality? quality, string? enchant, bool? blessed, int? enhance, Random rng)
    {
        var rolledQuality = quality ?? WeaponForge.RollQualityPublic(rng);
        if (rolledQuality == Quality.Unique)
            throw new ArgumentException("独特装备固定名与属性，须逐件登记，不能随机生成。");

        var rolledEnchant = enchant ?? WeaponForge.RollEnchantPublic(rng);
        return (rolledQuality, rolledEnchant ?? "", blessed ?? (rng.Next(100) < 8),
            enhance ?? (rng.Next(100) < 20 ? rng.Next(1, 4) : 0));
    }

    /// <summary>防具基座组合数（能缝甲的材料 × 5 槽）。</summary>
    public static int ArmorBaseCount() =>
        DefDatabase<MaterialDef>.All.Count(m => m.ArmorUsable) * 5;

    /// <summary>防具槽位基表是否已装载。</summary>
    public static bool ArmorSlotsLoaded() =>
        DefDatabase<ArmorSlotDef>.All.Count > 0;

    /// <summary>饰品基座组合数（材料 × 饰品类型 × 3 饰位）。</summary>
    public static int AccessoryBaseCount() =>
        DefDatabase<MaterialDef>.All.Count(m => m.ArmorUsable)
        * DefDatabase<AccessoryDef>.All.Count(a => a.Weight > 0)
        * 3;
}
