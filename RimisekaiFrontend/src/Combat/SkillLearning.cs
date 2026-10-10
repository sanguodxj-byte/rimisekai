using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Catalog;
using Rimisekai.Character;

namespace Rimisekai.Combat;

/// <summary>
/// 战斗技能的派生学习：技能不随门槛达标自动解锁，而是在战斗里使用它的派生源（<see cref="SkillDef.DeriveFrom"/>）时按几率学习，
/// 学习后永久记在 <see cref="CharacterState.LearnedSkills"/>。派生关系是一张网——同一技能可由多个源派生，
/// 也可跨流派、跨属性，用哪招决定下一招往哪长。
///
/// 门槛（<see cref="SkillGate"/>）只定学习率：素质须具备、前置须已学习、至少一个派生源已学习，这三条是硬条件；
/// 流派熟练 / 属性 / 生活技能按「离门槛有多近」给几率——全部达标 <see cref="ReadyPercent"/>%，
/// 每项都到门槛的 <see cref="NearRatio"/> 以上 <see cref="NearPercent"/>%，再远不会学习。
/// 学习不要求此刻持有该流派（跨流派派生），使用才要求。数值为提案值，调参先过主人。
/// </summary>
public static class SkillLearning
{
    public const int ReadyPercent = 12;
    public const int NearPercent = 4;
    public const double NearRatio = 0.75;

    /// <summary>一场战斗里每人至多掷这么多次（按技能使用次数，多用多机会）。</summary>
    public const int MaxTries = 8;

    /// <summary>无任何门槛的通用技能（普通攻击、防御架势）：人人自带，不需要学习。</summary>
    public static bool Innate(SkillDef skill)
    {
        var g = skill.Gate;
        return !g.Style.HasValue && (g.Core == null || g.Core.Count == 0) && (g.Life == null || g.Life.Count == 0)
            && (g.Traits == null || g.Traits.Count == 0) && (g.Prerequisites == null || g.Prerequisites.Count == 0);
    }

    /// <summary>是否已学会：通用技能或已学习。</summary>
    public static bool Learned(CharacterState c, SkillDef skill) => Innate(skill) || c.LearnedSkills.Contains(skill.Id);

    /// <summary>此刻用一次派生源学习该技能的几率（百分比）；已学会、或硬条件不满足为 0。</summary>
    public static int Chance(CharacterState c, SkillDef skill)
    {
        if (Learned(c, skill))
            return 0;
        var g = skill.Gate;
        if (g.Traits != null && g.Traits.Any(t => !c.Has(t)))
            return 0;
        if (g.Prerequisites != null && g.Prerequisites.Any(id => !Learned(c, SkillTable.Get(id)!)))
            return 0;
        if (!skill.DeriveFrom.Any(id => Learned(c, SkillTable.Get(id)!)))
            return 0;
        var readiness = Readiness(c, g);
        return readiness >= 1.0 ? ReadyPercent : readiness >= NearRatio ? NearPercent : 0;
    }

    /// <summary>离门槛最远那一项的完成度（实际 / 要求，取最小）；没有数值门槛为 1。</summary>
    public static double Readiness(CharacterState c, SkillGate g)
    {
        var ratio = 1.0;
        if (g.Style.HasValue && g.StyleLevel > 0)
            ratio = Math.Min(ratio, (double)c.Styles[(int)g.Style.Value].Level / g.StyleLevel);
        if (g.Core != null)
            foreach (var r in g.Core)
                ratio = Math.Min(ratio, (double)c[r.Stat] / r.Min);
        if (g.Life != null)
            foreach (var r in g.Life)
                ratio = Math.Min(ratio, (double)c.Life(r.Skill) / r.Min);
        return ratio;
    }

    /// <summary>
    /// 战后掷派生：本场每用一次某技能，就对它的各个派生技按学习率各掷一次（总计至多 <see cref="MaxTries"/> 次使用），
    /// 一场至多学习一式。学习的技能记进 <see cref="CharacterState.LearnedSkills"/> 并返回，没学习返回 null。
    /// 使用次数按技能 Id 排序逐个掷，结果只取决于骰子，不取决于字典顺序。
    /// </summary>
    public static SkillDef? Roll(CharacterState c, IReadOnlyDictionary<string, int> uses, Func<int> d100)
    {
        var tries = 0;
        foreach (var (source, count) in uses.OrderBy(u => u.Key, StringComparer.Ordinal))
        {
            var derived = SkillTree.Skills(c).Where(s => s.DeriveFrom.Contains(source))
                .Select(s => (Skill: s, Chance: Chance(c, s))).Where(x => x.Chance > 0)
                .OrderByDescending(x => x.Chance).ToList();
            if (derived.Count == 0)
                continue;
            for (var i = 0; i < count && tries < MaxTries; i++, tries++)
                foreach (var (skill, chance) in derived)
                    if (d100() < chance)
                    {
                        c.LearnedSkills.Add(skill.Id);
                        return skill;
                    }
        }
        return null;
    }
}
