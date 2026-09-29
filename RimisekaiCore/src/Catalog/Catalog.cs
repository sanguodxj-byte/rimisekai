using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Housing;

namespace Rimisekai.Catalog;

/// <summary>
/// 数据目录的空槽。XML/CSV 的行以后灌进这些定义，逻辑层只按 Id 查。
/// </summary>
public sealed class StatDef
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
}

public enum SkillKind
{
    /// <summary>物理打击，吃命中与防御。</summary>
    Strike,
    /// <summary>法术，必中，伤害吃法强，只吃防御的一半减免。</summary>
    Spell,
    /// <summary>治疗，给友方回血。</summary>
    Heal,
    /// <summary>增益/减益，按 Status 三件套落状态。</summary>
    Buff,
}

public enum SkillTarget
{
    Enemy,
    Ally,
    Self,
    AllEnemies,
    AllAllies,
}

/// <summary>增益/减益作用在哪个战斗数值上。</summary>
public enum StatusStat
{
    None,
    Attack,
    Defence,
    Dodge,
    SpellPower,
    /// <summary>敌视权重：挑衅类正值、隐匿类负值，只影响被索敌的倾向。</summary>
    Threat,
}

/// <summary>敌人的索敌取向。</summary>
public enum TargetMode
{
    /// <summary>按敌视权重随机取目标。</summary>
    Balanced,
    /// <summary>咬住血量最少的敌人。</summary>
    HuntWeak,
    /// <summary>优先打攻击最高的敌人。</summary>
    HuntStrong,
}

public sealed class SkillDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public int MaxLevel { get; init; } = 1;
    public int Cooldown { get; init; }

    public SkillKind Kind { get; init; } = SkillKind.Strike;
    public SkillTarget Target { get; init; } = SkillTarget.Enemy;

    /// <summary>威力百分比：打击/法术/治疗都按各自基数乘这一档。</summary>
    public int Power { get; init; } = 100;

    /// <summary>命中修正（百分点），只对打击类生效。</summary>
    public int HitMod { get; init; }

    public int MpCost { get; init; }

    /// <summary>状态三件套：作用数值、增减幅度（基础值百分比，可负）、持续回合。</summary>
    public StatusStat Stat { get; init; } = StatusStat.None;
    public int StatusPercent { get; init; }
    public int StatusRounds { get; init; }

    /// <summary>增益类技能可以顺带给目标叠护盾点数；护盾只挡物理，法术穿透。</summary>
    public int ShieldPoints { get; init; }
}

public sealed class ItemDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
}

/// <summary>武器面板数据。熟练按种类取，风格仍由装备配置推导。</summary>
public sealed class WeaponDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public WeaponType Type { get; init; }
    public int Panel { get; init; }
}

/// <summary>敌人掉落的一行：物品、数量区间、掉率（百分比）。</summary>
public sealed class EnemyLoot
{
    public string ItemId { get; init; } = "";
    public int Min { get; init; } = 1;
    public int Max { get; init; } = 1;
    /// <summary>掉率，百分比（0~100）。</summary>
    public int RatePercent { get; init; } = 100;
}

public sealed class EnemyDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int MaxHp { get; init; }
    public int Attack { get; init; }
    public int Defence { get; init; }
    public int Dodge { get; init; }
    public int SpellPower { get; init; }
    public int Threat { get; init; }

    /// <summary>护甲：实数减挡，物理全额、法术减半。</summary>
    public int Armour { get; init; }

    /// <summary>索敌取向，默认按敌视权重随机。</summary>
    public TargetMode Targeting { get; init; } = TargetMode.Balanced;

    /// <summary>击坠后的金钱奖励。</summary>
    public long Money { get; init; }

    /// <summary>会用的技能 Id。普通攻击与防御架势人人自带，不必写。</summary>
    public List<string> Skills { get; init; } = new();

    /// <summary>掉落表，击坠后逐行掷骰。</summary>
    public List<EnemyLoot> Loot { get; init; } = new();
}

public sealed class GameCatalog
{
    public Dictionary<int, StatDef> Stats { get; } = new();
    public Dictionary<string, SkillDef> Skills { get; } = new();
    public Dictionary<string, ItemDef> Items { get; } = new();
    public Dictionary<string, WeaponDef> Weapons { get; } = new();
    public Dictionary<string, EnemyDef> Enemies { get; } = new();
    public Dictionary<int, Defs.RoomDef> Rooms { get; } = new();
    public Dictionary<int, Defs.FacilityDef> Facilities { get; } = new();
}
