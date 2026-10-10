using System.Collections.Generic;
using Rimisekai.Character;

namespace Rimisekai.Combat;

/// <summary>技能门槛的类别。加一类新条件＝这里加一枚，Meets 与 Unmet 各补一支。</summary>
public enum SkillGateKind
{
    /// <summary>须装备某个流派。</summary>
    Style,

    /// <summary>该流派的熟练等级下限。</summary>
    StyleLevel,

    /// <summary>核心属性下限。</summary>
    CoreStat,

    /// <summary>生活技能下限。</summary>
    LifeSkill,

    /// <summary>须具备某个素质。</summary>
    Trait,

    /// <summary>须先解锁某个技能。</summary>
    Prerequisite,
}

/// <summary>一条未满足的门槛。Core 不持有显示文案，界面按 Kind 翻成中文。</summary>
/// <param name="Kind">门槛类别。</param>
/// <param name="Subject">主体枚举值（流派／属性／生活技能／素质）；前置技能时为 -1。</param>
/// <param name="SubjectId">前置技能的门槛为技能 Id；其余为空串。</param>
/// <param name="Required">门槛要求值；流派与前置技能这类有无判定不看它。</param>
/// <param name="Actual">角色当前值。</param>
public readonly record struct SkillGateMiss(
    SkillGateKind Kind,
    int Subject,
    string SubjectId,
    int Required,
    int Actual);

/// <summary>核心属性门槛：某项属性不低于 Min。</summary>
public readonly record struct CoreRequirement(CoreStat Stat, int Min);

/// <summary>生活技能门槛：某项生活技能不低于 Min。</summary>
public readonly record struct LifeRequirement(LifeSkill Skill, int Min);

/// <summary>
/// 技能解锁门槛：一组**全部可空**的条件，逐条 AND——全部满足才解锁。
/// 每个字段为 null 或空即不设该条件，新技能只写自己在意的那几项。
///
/// 覆盖：流派（须装备）＋该流派熟练等级、核心属性下限、生活技能下限、
/// 素质（须全部具备）、前置技能（须全部已解锁）。
///
/// 扩展方式：<see cref="SkillGateKind"/> 加一枚类别，这里加一个可空字段，
/// <see cref="Meets"/> 与 <see cref="Unmet"/> 各补一支分支即可，调用方无需改动。
/// </summary>
public sealed class SkillGate
{
    /// <summary>无门槛：人人自带（如普通攻击、防御架势）。</summary>
    public static readonly SkillGate Open = new();

    /// <summary>
    /// 技能所属的**属性扇区**——星盘按六属性分区，技能靠它决定落在哪个扇区。
    /// null = 不属任何属性（通用技能，如普通攻击与防御架势）。
    /// 注意：这是**分类**，不是条件；要求某项属性达到某值请用 <see cref="Core"/>。
    /// </summary>
    public CoreStat? Attribute { get; init; }

    /// <summary>须装备的流派。null = 不限流派。</summary>
    public StyleType? Style { get; init; }

    /// <summary>该流派的熟练等级下限。仅在 <see cref="Style"/> 非空时参与判定。</summary>
    public int StyleLevel { get; init; }

    /// <summary>核心属性下限，须全部满足。null/空 = 不限。</summary>
    public IReadOnlyList<CoreRequirement>? Core { get; init; }

    /// <summary>生活技能下限，须全部满足。null/空 = 不限。</summary>
    public IReadOnlyList<LifeRequirement>? Life { get; init; }

    /// <summary>须具备的素质，须全部具备。null/空 = 不限。</summary>
    public IReadOnlyList<Trait>? Traits { get; init; }

    /// <summary>前置技能 Id，须全部已解锁。null/空 = 无前置。</summary>
    public IReadOnlyList<string>? Prerequisites { get; init; }

    /// <summary>
    /// 是否满足门槛。<paramref name="unlocked"/> 是本次判定里已解锁的技能 Id 集，
    /// 前置技能据此判定；前置可以成链（A→B→C），由调用方迭代求不动点。
    /// </summary>
    public bool Meets(CharacterState c, IReadOnlySet<string> unlocked)
    {
        if (Style.HasValue)
        {
            if (c.EquippedStyle != Style)
                return false;
            if (c.Styles[(int)Style.Value].Level < StyleLevel)
                return false;
        }

        if (Core != null)
            foreach (var requirement in Core)
                if (c[requirement.Stat] < requirement.Min)
                    return false;

        if (Life != null)
            foreach (var requirement in Life)
                if (c.Life(requirement.Skill) < requirement.Min)
                    return false;

        if (Traits != null)
            foreach (var trait in Traits)
                if (!c.Has(trait))
                    return false;

        if (Prerequisites != null)
            foreach (var id in Prerequisites)
                if (!unlocked.Contains(id))
                    return false;

        return true;
    }

    /// <summary>逐条列出未满足的门槛，供界面显示"差什么"。全满足则返回空表。</summary>
    public IReadOnlyList<SkillGateMiss> Unmet(CharacterState c, IReadOnlySet<string> unlocked)
    {
        var misses = new List<SkillGateMiss>();

        if (Style.HasValue)
        {
            if (c.EquippedStyle != Style)
                misses.Add(new SkillGateMiss(SkillGateKind.Style, (int)Style.Value, "",
                    0, c.EquippedStyle.HasValue ? (int)c.EquippedStyle.Value : -1));
            else if (c.Styles[(int)Style.Value].Level < StyleLevel)
                misses.Add(new SkillGateMiss(SkillGateKind.StyleLevel, (int)Style.Value, "",
                    StyleLevel, c.Styles[(int)Style.Value].Level));
        }

        if (Core != null)
            foreach (var requirement in Core)
                if (c[requirement.Stat] < requirement.Min)
                    misses.Add(new SkillGateMiss(SkillGateKind.CoreStat, (int)requirement.Stat, "",
                        requirement.Min, c[requirement.Stat]));

        if (Life != null)
            foreach (var requirement in Life)
                if (c.Life(requirement.Skill) < requirement.Min)
                    misses.Add(new SkillGateMiss(SkillGateKind.LifeSkill, (int)requirement.Skill, "",
                        requirement.Min, c.Life(requirement.Skill)));

        if (Traits != null)
            foreach (var trait in Traits)
                if (!c.Has(trait))
                    misses.Add(new SkillGateMiss(SkillGateKind.Trait, (int)trait, "", 1, 0));

        if (Prerequisites != null)
            foreach (var id in Prerequisites)
                if (!unlocked.Contains(id))
                    misses.Add(new SkillGateMiss(SkillGateKind.Prerequisite, -1, id, 1, 0));

        return misses;
    }
}
