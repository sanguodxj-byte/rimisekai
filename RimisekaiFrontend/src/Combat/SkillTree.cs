using System.Collections.Generic;
using System.Linq;
using Rimisekai.Catalog;
using Rimisekai.Character;

namespace Rimisekai.Combat;

/// <summary>技能网上的一格：第几行、第几列（列可为半格，行内不满五式时居中）。</summary>
public readonly record struct TreeSlot(SkillDef Def, int Row, float Column);

/// <summary>
/// 角色的技能网（拟案，待主人核定）：有身份技能池的角色，网上是通用两式（普通攻击、防御架势）＋本身份池的全部技能；
/// 没有技能池的身份，网上是全部通用技能（skills.json）。
/// 基础技能按 <see cref="SkillTier"/> 一到五阶自上而下排（越往下越强），一行至多五式，满了另起一行；
/// 核心技能单占一行，永远插在正中间（主人 2026-10-11 定）。派生学习与出身即会都只认网上的技能。
/// </summary>
public static class SkillTree
{
    public const int Columns = 5;

    /// <summary>网上的全部技能（不排位）。</summary>
    public static IEnumerable<SkillDef> Skills(CharacterState c)
    {
        var all = SkillTable.All.Where(s => s.Item.Length == 0);
        if (SkillPool.PoolOf(c.PoolIdentity) is not { } pool)
            return all;
        return all.Where(SkillLearning.Innate).Concat(SkillPool.Basics(pool)).Concat(SkillPool.Cores(pool)).DistinctBy(s => s.Id);
    }

    public static bool Contains(CharacterState c, SkillDef s) => Skills(c).Any(x => x.Id == s.Id);

    /// <summary>排位：同阶按估值由弱到强、再按 Id，核心行插在全部基础行的正中。</summary>
    public static List<TreeSlot> Layout(CharacterState c)
    {
        var skills = Skills(c).ToList();
        var rows = new List<List<SkillDef>>();
        foreach (var tier in skills.Where(s => s.Core == CoreKind.None)
                     .OrderBy(s => SkillTier.Score(s)).ThenBy(s => s.Id, System.StringComparer.Ordinal)
                     .GroupBy(SkillTier.Of).OrderBy(g => g.Key))
            rows.AddRange(tier.Chunk(Columns).Select(chunk => chunk.ToList()));
        var cores = skills.Where(s => s.Core != CoreKind.None).ToList();
        if (cores.Count > 0)
            rows.Insert(rows.Count / 2, cores);

        var slots = new List<TreeSlot>();
        for (var r = 0; r < rows.Count; r++)
            for (var i = 0; i < rows[r].Count; i++)
                slots.Add(new TreeSlot(rows[r][i], r, (Columns - rows[r].Count) / 2f + i));
        return slots;
    }
}
