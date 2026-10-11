using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Catalog;
using Rimisekai.Character;
using Rimisekai.Defs;

namespace Rimisekai.Combat;

/// <summary>掉落的一件装备实例：武器或防具/饰品，二者恰有其一。登记与入袋由结算方做。</summary>
public sealed class LootGear
{
    private LootGear(WeaponInstance? weapon, EquipInstance? equip)
    {
        Weapon = weapon;
        Equip = equip;
    }

    public static LootGear Of(WeaponInstance weapon) => new(weapon, null);
    public static LootGear Of(EquipInstance equip) => new(null, equip);

    public WeaponInstance? Weapon { get; }
    public EquipInstance? Equip { get; }

    public string Id => Weapon != null ? Weapon.Id : Equip!.Id;
    public string Name => Weapon != null ? Weapon.Name : Equip!.Name;
    public Quality Quality => Weapon != null ? Weapon.Quality : Equip!.Quality;
    public string MaterialDefName => Weapon != null ? Weapon.MaterialDefName : Equip!.MaterialDefName;
}

/// <summary>
/// 怪物掉装备：照 <see cref="GearDropDef"/>（content/defs/gear_drops.json）给一只倒下的敌人掷装备实例。
/// 掉不掉看 CR 段（正主走首领掉率并再掷第二件，委托首领保底），种类、材质（CR 段窗口，正主可升一档）、
/// 品质（小兵按 CR 段、正主按首领表）、附魔、祝福、强化都按表掷好，再显式交给 <see cref="WeaponForge"/> / <see cref="EquipForge"/> 出实例。
/// 站位层（threatTier）不参与。
/// </summary>
public static class GearLoot
{
    /// <summary>一只倒下的敌人掉的装备。<paramref name="rng"/> 掷全部骰子（测试可注入定种子）。</summary>
    public static List<LootGear> Roll(EnemyDef enemy, bool questBoss, Random rng)
    {
        var rules = GearDropDef.Standard;
        var band = rules.Band(enemy.Cr);
        var drops = new List<LootGear>();
        var row = enemy.Gear;
        var firstRate = row?.RatePercent ?? (enemy.Boss ? rules.Boss.DropPercent : band.DropPercent);
        if (rng.Next(100) < firstRate)
            drops.Add(Forge(rules, band, enemy, row, rng));
        if (enemy.Boss && rng.Next(100) < rules.Boss.ExtraDropPercent)
            drops.Add(Forge(rules, band, enemy, null, rng));
        while (questBoss && drops.Count < rules.QuestBossMinDrops)
            drops.Add(Forge(rules, band, enemy, row, rng));
        return drops;
    }

    /// <summary>出一件：种类 → 材质 → 品质/附魔/祝福/强化，全部显式传给锻炉。</summary>
    private static LootGear Forge(GearDropDef rules, GearDropBand band, EnemyDef enemy, EnemyGear? row, Random rng)
    {
        var boss = enemy.Boss;
        GearDropGrade grade = boss ? rules.Boss : rules.Minion;
        var kind = row?.Kind ?? RollKind(rules, rng);

        var quality = Weighted(boss ? rules.Boss.Quality : band.Quality, q => q.Weight, rng).Quality;
        if (row?.MinQuality is { } floor && quality < floor)
            quality = floor;
        var enchantPercent = band.EnchantPercent + (boss ? rules.Boss.EnchantBonus : 0);
        var enchant = rng.Next(100) < enchantPercent ? WeaponForge.RollEnchantPublic(rng)! : "";
        var blessed = rng.Next(100) < grade.BlessedPercent;
        var enhance = rng.Next(100) < grade.EnhancePercent ? rng.Next(grade.EnhanceMin, grade.EnhanceMax + 1) : 0;
        var minMaterial = row?.MinMaterial ?? "";

        switch (kind)
        {
            case EquipKind.Weapon:
            {
                var types = DefDatabase<WeaponTypeDef>.All.ToList();
                var type = row?.WeaponType ?? enemy.Weapon ?? types[rng.Next(types.Count)].Type;
                var material = Material(rules, band.Weapon, m => m.WeaponUsable, boss, minMaterial, rng);
                return LootGear.Of(WeaponForge.Forge(material, type, quality, enchant, blessed, enhance, rng: rng));
            }
            case EquipKind.Armor:
            {
                var slots = DefDatabase<ArmorSlotDef>.All.ToList();
                var slot = row?.Slot ?? Enum.Parse<EquipSlot>(slots[rng.Next(slots.Count)].DefName);
                var material = Material(rules, band.Armor, m => m.ArmorUsable, boss, minMaterial, rng);
                return LootGear.Of(EquipForge.ForgeArmor(slot, material, quality, enchant, blessed, enhance, rng));
            }
            default:
            {
                var slot = row?.Slot ?? Weighted(rules.AccessorySlots, s => s.Weight, rng).Slot;
                var accessory = Weighted(DefDatabase<AccessoryDef>.All.Where(a => a.Weight > 0).ToList(), a => a.Weight, rng);
                var material = Material(rules, band.Armor, m => m.ArmorUsable, boss, minMaterial, rng);
                return LootGear.Of(EquipForge.ForgeAccessory(slot, accessory.DefName, material, quality, enchant, blessed, enhance, rng));
            }
        }
    }

    private static EquipKind RollKind(GearDropDef rules, Random rng)
    {
        var roll = rng.Next(rules.WeaponWeight + rules.ArmorWeight + rules.AccessoryWeight);
        if (roll < rules.WeaponWeight)
            return EquipKind.Weapon;
        return roll < rules.WeaponWeight + rules.ArmorWeight ? EquipKind.Armor : EquipKind.Accessory;
    }

    /// <summary>
    /// 材质：CR 段窗口里等权抽一种；正主按升档率沿材料表（该种类能用的材料按 tier 排）再升一档，以太封顶；
    /// 覆盖行给了材质下限的，低于下限换成下限。
    /// </summary>
    private static string Material(GearDropDef rules, List<string> pool, Func<MaterialDef, bool> usable,
        bool boss, string minMaterial, Random rng)
    {
        var ladder = DefDatabase<MaterialDef>.All.Where(usable).OrderBy(m => m.Tier).ToList();
        var material = DefDatabase<MaterialDef>.Get(pool[rng.Next(pool.Count)])!;
        if (boss && rng.Next(100) < rules.Boss.MaterialUpgradePercent
            && ladder.FirstOrDefault(m => m.Tier > material.Tier) is { } better)
            material = better;
        if (minMaterial.Length > 0 && DefDatabase<MaterialDef>.Get(minMaterial)!.Tier > material.Tier)
            return minMaterial;
        return material.DefName;
    }

    private static T Weighted<T>(IReadOnlyList<T> rows, Func<T, int> weight, Random rng)
    {
        var roll = rng.Next(rows.Sum(weight));
        foreach (var row in rows)
        {
            roll -= weight(row);
            if (roll < 0)
                return row;
        }
        throw new InvalidOperationException("权重表为空。");
    }
}
