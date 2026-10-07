using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Housing;

using Rimisekai.Combat;

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

    /// <summary>打击格：某一列敌人整列（玩家方 AOE 用）。</summary>
    FoesColumn,
}

/// <summary>攻击射程：近战只能打最前排，远程按列/威胁权重索敌。</summary>
public enum SkillRange
{
    Melee,
    Ranged,
}

/// <summary>增益/减益作用在哪个战斗数值上。</summary>
public enum StatusStat
{
    None,
    Attack,
    Defence,
    Dodge,
    SpellPower,

    /// <summary>行动速度：加速/减速，作用于战斗跑条的行动间隔。</summary>
    Speed,
}

public sealed class SkillDef : Defs.Def
{
    public string Id
    {
        get => string.IsNullOrEmpty(DefName) ? _id : DefName;
        init
        {
            _id = value;
            if (string.IsNullOrEmpty(DefName)) DefName = value;
        }
    }
    private string _id = "";

    public string Name
    {
        get => string.IsNullOrEmpty(Label) ? (string.IsNullOrEmpty(_name) ? DefName : _name) : Label;
        init
        {
            _name = value;
            if (string.IsNullOrEmpty(Label)) Label = value;
        }
    }
    private string _name = "";
    public string Category { get; init; } = "";
    public int MaxLevel { get; init; } = 1;

    /// <summary>
    /// 解锁门槛：一组可空条件（流派／熟练／属性／生活技能／素质／前置技能），逐条 AND。
    /// 无门槛的技能（普通攻击、防御架势）用 <see cref="SkillGate.Open"/>。
    /// 技能的流派归属即 <see cref="SkillGate.Style"/>，星盘据此分扇区。
    /// </summary>
    public SkillGate Gate { get; init; } = SkillGate.Open;

    /// <summary>射程：近程打最前排，远程选列后按威胁权重落点。</summary>
    public SkillRange Range { get; init; } = SkillRange.Melee;

    public SkillKind Kind { get; init; } = SkillKind.Strike;
    public SkillTarget Target { get; init; } = SkillTarget.Enemy;

    /// <summary>威力百分比：打击/法术/治疗都按各自基数乘这一档。</summary>
    public int Power { get; init; } = 100;

    /// <summary>命中修正（百分点），只对打击类生效。</summary>
    public int HitMod { get; init; }

    /// <summary>
    /// 咏唱回合数。0 = 瞬发（打击/增益等）；
    /// ≥1 = 法术类需要咏唱，咏唱期间被任意控制状态命中即打断。
    /// </summary>
    public int ChantRounds { get; init; }

    /// <summary>控制类：命中即打断目标的咏唱。</summary>
    public bool Control { get; init; }

    /// <summary>
    /// 附加状态：StatMod = 数值增减；Dot = 每轮伤害的持续弱化；Shield = 点数护盾。
    /// null = 不带状态。打击类命中才附加，法术/增益类直接生效。
    /// </summary>
    public StatusKind? Status { get; init; }

    /// <summary>StatMod 的作用面。</summary>
    public StatusStat StatusStat { get; init; } = StatusStat.None;

    /// <summary>StatMod 的幅度（百分点，可负）。</summary>
    public int StatusPercent { get; init; }

    /// <summary>DoT 每轮伤害 / Shield 初始点数。</summary>
    public int StatusPower { get; init; }

    /// <summary>状态持续回合。</summary>
    public int StatusRounds { get; init; }

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

    /// <summary>行动速度：决定跑条上的行动间隔。</summary>
    public int Speed { get; init; } = 10;

    /// <summary>威胁等级 1-5：越高越靠前（同列更高层级），敌方近战只打最前的玩家。</summary>
    public int ThreatTier { get; init; } = 1;

    /// <summary>站位列 1-4：跨列的精英/首领以其最左列声明，整场最多 4x4。</summary>
    public int Column { get; init; } = 1;

    /// <summary>占位边长：1=杂兵 1x1，2=精英 2x2，4=首领 4x4，锚在威胁等级所在排向纵深延展。</summary>
    public int Size { get; init; } = 1;

    /// <summary>行动点数：战斗界面在首领血条下方按此数目画实心菱。默认 1。</summary>
    public int ActionPoints { get; init; } = 1;

    /// <summary>立绘资产名：映射 assets/portraits/monster/{portrait}.png，空串表示暂无立绘。</summary>
    public string Portrait { get; init; } = "";

    /// <summary>护甲：实数减挡，物理全额、法术减半。</summary>
    public int Armour { get; init; }

    /// <summary>击坠后的金钱奖励。</summary>
    public long Money { get; init; }

    /// <summary>会用的技能 Id。普通攻击与防御架势人人自带，不必写。</summary>
    public List<string> Skills { get; init; } = new();

    /// <summary>掉落表，击坠后逐行掷骰。</summary>
    public List<EnemyLoot> Loot { get; init; } = new();
}

/// <summary>
/// 存档内的战斗/物品目录。房间与设施的定义不在这里——
/// 它们是内容包定义，权威在 <see cref="Defs.DefDatabase{T}"/>，按 defName 查。
/// </summary>
public sealed class GameCatalog
{
    public Dictionary<int, StatDef> Stats { get; } = new();
    public Dictionary<string, SkillDef> Skills { get; } = new();
    public Dictionary<string, ItemDef> Items { get; } = new();
    public Dictionary<string, WeaponDef> Weapons { get; } = new();
    public Dictionary<string, EnemyDef> Enemies { get; } = new();
}
