using System.Collections.Generic;
using System.Linq;
using Rimisekai.Catalog;

namespace Rimisekai.Defs;

/// <summary>
/// 怪物掉装备的规则表（content/defs/gear_drops.json，全项目一行 <see cref="StandardDefName"/>）。
/// 「最好的装备都是掉的」：所有敌人都可能掉、每场不设件数上限，掉落品质分布比锻造高，强化可越过锻造上限。
/// 掉率、材质窗口、附魔率、小兵品质按敌人的挑战等级（CR）分段；遭遇里的正主（<see cref="EnemyDef.Boss"/>）走首领那套。
/// 站位层（threatTier）与掉落无关。数值全在表里，<see cref="Combat.GearLoot"/> 只读表掷骰。
/// </summary>
public sealed class GearDropDef : Def
{
    public const string StandardDefName = "GearDrop_Standard";

    /// <summary>全项目唯一那行规则。</summary>
    public static GearDropDef Standard => DefDatabase<GearDropDef>.Get(StandardDefName)!;

    /// <summary>CR 段：每段从 <see cref="GearDropBand.CrMin"/> 起，到下一段的起点为止（按 crMin 升序写）。</summary>
    public List<GearDropBand> Bands { get; init; } = new();

    /// <summary>委托首领（委托写明的正主）至少掉几件。</summary>
    public int QuestBossMinDrops { get; init; }

    /// <summary>种类权重：武器 / 防具 / 饰品。</summary>
    public int WeaponWeight { get; init; }
    public int ArmorWeight { get; init; }
    public int AccessoryWeight { get; init; }

    /// <summary>饰品落在哪种饰位（戒指 Ring1 / 项链 Neck）的权重；戒指进背包后两枚戒指位都能戴。</summary>
    public List<GearDropSlot> AccessorySlots { get; init; } = new();

    /// <summary>小兵（非正主）的祝福与强化。品质看 CR 段。</summary>
    public GearDropGrade Minion { get; init; } = new();

    /// <summary>正主的掉率、第二件、材质升档、附魔加成、品质、祝福与强化。</summary>
    public GearDropBoss Boss { get; init; } = new();

    /// <summary>该 CR 落在哪一段：crMin 不超过它的最后一段。</summary>
    public GearDropBand Band(ChallengeRating cr) => Bands.Last(b => b.CrMin <= cr);
}

public sealed class GearDropSlot
{
    public EquipSlot Slot { get; init; }
    public int Weight { get; init; }
}

/// <summary>一段 CR：小兵一件的掉率、附魔率、武器材质池、防具与饰品材质池（等权抽一种）、小兵品质权重。</summary>
public sealed class GearDropBand
{
    public ChallengeRating CrMin { get; init; }
    public int DropPercent { get; init; }
    public int EnchantPercent { get; init; }
    public List<string> Weapon { get; init; } = new();
    public List<string> Armor { get; init; } = new();
    public List<GearDropQuality> Quality { get; init; } = new();
}

/// <summary>祝福率、强化机会与区间。</summary>
public class GearDropGrade
{
    public int BlessedPercent { get; init; }
    public int EnhancePercent { get; init; }
    public int EnhanceMin { get; init; }
    public int EnhanceMax { get; init; }
}

/// <summary>
/// 正主那套：一件的掉率、再掷第二件的概率、材质沿材料表升一档的概率（以太封顶）、附魔率加成、品质权重，
/// 祝福与强化同 <see cref="GearDropGrade"/>。
/// </summary>
public sealed class GearDropBoss : GearDropGrade
{
    public int DropPercent { get; init; }
    public int ExtraDropPercent { get; init; }
    public int MaterialUpgradePercent { get; init; }
    public int EnchantBonus { get; init; }
    public List<GearDropQuality> Quality { get; init; } = new();
}

public sealed class GearDropQuality
{
    public Quality Quality { get; init; }
    public int Weight { get; init; }
}
