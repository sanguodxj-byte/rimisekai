using System.Collections.Generic;
using System.Linq;
using Rimisekai.Character;

namespace Rimisekai.Defs;

/// <summary>
/// 一件**运行时生成**的防具或饰品实例。
///
/// 与武器同构：材料 × 槽位/类型是基座，每炉出一件实例，
/// 各自带品质、强化、附魔、祝福，名字也是当场拼的。
/// 实例不可堆叠，在背包/仓储里以 Id 记 1 件。
/// </summary>
public sealed class EquipInstance
{
    /// <summary>实例 Id，形如 "eqp_7"。</summary>
    public string Id { get; init; } = "";

    /// <summary>进哪个槽。</summary>
    public EquipSlot Slot { get; init; }

    /// <summary>大类（防具或饰品）。</summary>
    public EquipKind Kind { get; init; }

    /// <summary>材料轴 DefName——与武器共用同一张材料表（materials.xml）。</summary>
    public string MaterialDefName { get; init; } = "";

    /// <summary>饰品类型 DefName；防具留空。</summary>
    public string Accessory { get; init; } = "";

    /// <summary>品质。既是价值倍率，也是数值乘数。</summary>
    public Quality Quality { get; init; }

    /// <summary>强化等级。0 = 未强化。</summary>
    public int Enhance { get; init; }

    /// <summary>附魔 DefName；空串表示没有附魔。</summary>
    public string Enchant { get; init; } = "";

    /// <summary>是否被祝福。</summary>
    public bool Blessed { get; init; }

    /// <summary>运行时生成的名字。</summary>
    public string Name { get; init; } = "";

    // ---- 派生 ----

    /// <summary>材料轴定义（全项目唯一那张材料表）。</summary>
    public MaterialDef? Material => DefDatabase<MaterialDef>.Get(MaterialDefName);

    public AccessoryDef? AccessoryDef => Accessory.Length == 0
        ? null
        : DefDatabase<AccessoryDef>.Get(Accessory);

    public EnchantDef? EnchantDef => Enchant.Length == 0
        ? null
        : DefDatabase<EnchantDef>.Get(Enchant);

    /// <summary>防具防御值：（槽位底防 + 材料甲加成）× 品质乘数，再加强化与祝福的加成。</summary>
    public int Defence
    {
        get
        {
            if (Kind != EquipKind.Armor)
                return 0;
            var total = (ArmorSlots.BaseDefence(Slot) + (Material?.ArmorBonus ?? 0)) * QualityOf.PanelFactor(Quality) / 100;
            total += Enhance;
            if (EnchantDef is { } e)
                total += e.CoreBonus;
            if (Blessed)
                total += 2;
            return total;
        }
    }

    /// <summary>饰品加成的战斗属性；不是饰品返回 null。</summary>
    public CoreStat? BonusStat =>
        Kind == EquipKind.Accessory ? AccessoryDef?.Core : null;

    /// <summary>饰品加成点数：类型基础 × 材料倍率 × 品质乘数，再加强化与祝福。</summary>
    public int BonusAmount
    {
        get
        {
            if (Kind != EquipKind.Accessory)
                return 0;
            var acc = AccessoryDef;
            if (acc == null)
                return 0;
            var total = acc.BaseBonus * (Material?.ValueFactor ?? 100) / 100;
            total = total * QualityOf.PanelFactor(Quality) / 100;
            total += Enhance;
            if (EnchantDef is { } e)
                total += e.CoreBonus;
            if (Blessed)
                total += 1;
            return total;
        }
    }

    /// <summary>
    /// 市场价值。防具按槽位基础与材料算，饰品按类型基础与材料算，再乘品质倍率。
    /// </summary>
    public int Value
    {
        get
        {
            var material = Material;
            if (material == null)
                return 1;
            var baseValue = Kind switch
            {
                EquipKind.Armor => ArmorSlots.BaseValue(Slot),
                EquipKind.Accessory => AccessoryDef?.BaseBonus * 4 ?? 10,
                _ => 1,
            };
            var total = baseValue * material.ValueFactor / 100;
            total = total * QualityOf.ValueFactor(Quality) / 100;
            total += Enhance * 4;
            if (EnchantDef is { } e)
                total += 10 + e.Weight;
            if (Blessed)
                total = total * 3 / 2;
            return total < 1 ? 1 : total;
        }
    }

    /// <summary>
    /// 详情里逐行列明这件装备带着什么。祝福与附魔已写在名字里，这里不重复。
    /// </summary>
    public IReadOnlyList<DetailLine> DescribeDetails()
    {
        var list = new List<DetailLine>
        {
            new("品质", QualityOf.Label(Quality)),
            new("槽位", EquipSlots.Label(Slot)),
        };

        if (Kind == EquipKind.Armor)
        {
            list.Add(new("材料", Material?.Label ?? "?"));
            list.Add(new("防御", Defence.ToString()));
        }
        else
        {
            list.Add(new("类型", StatLabel(AccessoryDef?.Core)));
            list.Add(new("加成", $"{StatLabel(BonusStat)} +{BonusAmount}"));
        }

        if (Enhance > 0)
            list.Add(new("强化", $"+{Enhance}"));
        if (EnchantDef is { } e)
            list.Add(new("附魔", e.Prefix, e.Effect));
        if (Blessed)
            list.Add(new("祝福", "受祝福"));
        return list;
    }

    private static string StatLabel(CoreStat? stat) => stat switch
    {
        CoreStat.Strength => "力量",
        CoreStat.Dexterity => "敏捷",
        CoreStat.Constitution => "体质",
        CoreStat.Perception => "感知",
        CoreStat.Intellect => "智力",
        _ => "?",
    };
}
