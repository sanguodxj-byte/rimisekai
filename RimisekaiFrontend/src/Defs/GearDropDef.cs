using System.Collections.Generic;
using System.Linq;

namespace Rimisekai.Defs;

/// <summary>
/// 怪物掉装备的规则表（content/defs/gear_drops.json，全项目一行 <see cref="StandardDefName"/>）。
/// 「最好的装备都是掉的」：所有敌人都可能掉、每场不设件数上限，掉落品质分布比锻造高，强化可越过锻造上限。
/// 数值全在表里，<see cref="Combat.GearLoot"/> 只读表掷骰。
/// </summary>
public sealed class GearDropDef : Def
{
    public const string StandardDefName = "GearDrop_Standard";

    /// <summary>全项目唯一那行规则。</summary>
    public static GearDropDef Standard => DefDatabase<GearDropDef>.Get(StandardDefName)!;

    /// <summary>按敌人威胁等级（1–4）的一件掉率。</summary>
    public List<GearDropThreat> Threats { get; init; } = new();

    /// <summary>威胁等级达到此值的敌人再掷一次第二件。</summary>
    public int ExtraDropThreat { get; init; }

    /// <summary>第二件的掉率（百分比）。</summary>
    public int ExtraDropPercent { get; init; }

    /// <summary>委托首领（委托地城的正主 / 首领战委托的首领）至少掉几件。</summary>
    public int QuestBossMinDrops { get; init; }

    /// <summary>威胁等级达到此值算首领：品质、附魔、祝福、强化走首领那套，并有材质升档机会。</summary>
    public int BossThreat { get; init; }

    /// <summary>种类权重：武器 / 防具 / 饰品。</summary>
    public int WeaponWeight { get; init; }
    public int ArmorWeight { get; init; }
    public int AccessoryWeight { get; init; }

    /// <summary>饰品落在哪种饰位（戒指 Ring1 / 项链 Neck）的权重；戒指进背包后两枚戒指位都能戴。</summary>
    public List<GearDropSlot> AccessorySlots { get; init; } = new();

    /// <summary>按危险等级（1–4）的材质窗口与附魔率。</summary>
    public List<GearDropTier> Tiers { get; init; } = new();

    /// <summary>首领掉的那件按此百分比把材质沿材料表再升一档（以太封顶）。</summary>
    public int BossMaterialUpgradePercent { get; init; }

    /// <summary>首领的附魔率在危险等级附魔率上再加几个百分点。</summary>
    public int BossEnchantBonus { get; init; }

    public GearDropGrade Minion { get; init; } = new();
    public GearDropGrade Boss { get; init; } = new();

    public GearDropTier Tier(int dangerTier) => Tiers.Single(t => t.Tier == dangerTier);

    public int DropPercent(int threat) => Threats.Single(t => t.Threat == threat).DropPercent;

    public GearDropGrade Grade(int threat) => threat >= BossThreat ? Boss : Minion;
}

public sealed class GearDropThreat
{
    public int Threat { get; init; }
    public int DropPercent { get; init; }
}

public sealed class GearDropSlot
{
    public EquipSlot Slot { get; init; }
    public int Weight { get; init; }
}

/// <summary>一档危险等级：武器材质池、防具与饰品材质池（等权抽一种）、附魔率。</summary>
public sealed class GearDropTier
{
    public int Tier { get; init; }
    public List<string> Weapon { get; init; } = new();
    public List<string> Armor { get; init; } = new();
    public int EnchantPercent { get; init; }
}

/// <summary>小兵或首领的一套：品质权重、祝福率、强化机会与区间。</summary>
public sealed class GearDropGrade
{
    public List<GearDropQuality> Quality { get; init; } = new();
    public int BlessedPercent { get; init; }
    public int EnhancePercent { get; init; }
    public int EnhanceMin { get; init; }
    public int EnhanceMax { get; init; }
}

public sealed class GearDropQuality
{
    public Quality Quality { get; init; }
    public int Weight { get; init; }
}
