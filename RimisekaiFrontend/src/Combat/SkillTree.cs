using System.Collections.Generic;
using System.Linq;
using Rimisekai.Catalog;
using Rimisekai.Character;

namespace Rimisekai.Combat;

/// <summary>技能网上的一格：第几行、第几列（列可为半格，行内不满五式时居中）。</summary>
public readonly record struct TreeSlot(SkillDef Def, int Row, float Column);

/// <summary>
/// 角色的技能网（拟案，待主人核定）。
/// 网上有什么：有身份技能池的角色＝普通攻击＋本身份池的全部技能；没有技能池的身份＝全部通用技能。
/// 防御架势是战斗指令，不算技能、不上网；道具招式（<see cref="ItemActions"/>）也不上网。
/// 排法：基础技能按 <see cref="SkillTier"/> 一到五阶自上而下（越往下越强），一行至多五式，满了另起一行；
/// 核心技能单占一行，永远插在正中间（主人 2026-10-11 定）。
/// 激活：不学习，门槛（流派、流派熟练、武器熟练、属性……）达到即自动激活（主人 2026-10-11 定）；
/// 有池的角色只有抽到的才会激活，没抽到的只在网上看得见。
/// 连线（<see cref="Links"/>）按门槛高低启发式现算：门槛更低、同一流派的技能连向门槛更高的，网上每式至少有一条进线。
/// </summary>
public static class SkillTree
{
    public const int Columns = 5;

    /// <summary>每式至多几条进线。</summary>
    public const int MaxParents = 2;

    /// <summary>网上的全部技能（不排位）。</summary>
    public static IEnumerable<SkillDef> Skills(CharacterState c)
    {
        var general = SkillTable.All.Where(s => s.Id != BattleSkills.GuardId);
        if (SkillPool.PoolOf(c.PoolIdentity) is not { } pool)
            return general;
        return general.Where(SkillTable.Innate).Concat(SkillPool.Basics(pool)).Concat(SkillPool.Cores(pool)).DistinctBy(s => s.Id);
    }

    /// <summary>网上的这一式对这个角色作不作数：没有池的角色全作数；有池的角色只有通用两式与抽到的。</summary>
    public static bool Eligible(CharacterState c, SkillDef s) =>
        SkillPool.PoolOf(c.PoolIdentity) == null || SkillTable.Innate(s) || c.SkillPool.Contains(s.Id);

    /// <summary>已激活的技能：作数且门槛全满足（前置成链，迭代到不动点）。</summary>
    public static IEnumerable<SkillDef> Active(CharacterState c) =>
        SkillTable.MeetsGates(c, Skills(c).Where(s => Eligible(c, s)).ToList());

    /// <summary>门槛的分量：流派熟练＋武器熟练（连线比高低用）。</summary>
    private static int Weight(SkillDef s) => s.Gate.StyleLevel + s.Gate.WeaponLevel;

    /// <summary>A 的门槛是否低于 B、可作 B 的来源：同一流派，流派熟练与武器熟练都不高于 B，且合起来更低；通用技能低于一切。</summary>
    private static bool Below(SkillDef a, SkillDef b)
    {
        if (a.Id == b.Id || a.Core != CoreKind.None)
            return false;
        if (SkillTable.Innate(a))
            return !SkillTable.Innate(b);
        var ga = a.Gate;
        var gb = b.Gate;
        if (ga.Style != gb.Style || ga.StyleLevel > gb.StyleLevel || ga.WeaponLevel > gb.WeaponLevel)
            return false;
        if (ga.Weapon.HasValue && ga.Weapon != gb.Weapon)
            return false;
        return Weight(a) < Weight(b);
    }

    /// <summary>
    /// 连线（来源 → 派生）。每式取门槛最接近的 <see cref="MaxParents"/> 个来源：门槛最接近优先，再挑网上横向离得近的
    /// （少交叉），再同类技能、估值接近；门槛比它低的一个都没有，就由普通攻击连过来——网上没有孤立的技能。
    /// </summary>
    public static List<(SkillDef Source, SkillDef Target)> Links(IReadOnlyList<TreeSlot> slots)
    {
        var links = new List<(SkillDef, SkillDef)>();
        var root = slots.Select(s => s.Def).FirstOrDefault(SkillTable.Innate);
        foreach (var b in slots.Where(s => !SkillTable.Innate(s.Def)))
        {
            var parents = slots.Where(a => !SkillTable.Innate(a.Def) && Below(a.Def, b.Def))
                .OrderByDescending(a => Weight(a.Def))
                .ThenBy(a => System.Math.Abs(a.Column - b.Column))
                .ThenBy(a => a.Def.Kind == b.Def.Kind ? 0 : 1)
                .ThenBy(a => System.Math.Abs(SkillTier.Score(a.Def) - SkillTier.Score(b.Def)))
                .ThenBy(a => a.Def.Id, System.StringComparer.Ordinal)
                .Take(MaxParents).Select(a => a.Def).ToList();
            if (parents.Count == 0 && root != null)
                parents.Add(root);
            links.AddRange(parents.Select(a => (a, b.Def)));
        }
        return links;
    }

    /// <summary>排位：通用技能（连线的根）单占顶行；其下同阶按估值由弱到强、再按 Id，核心行插在全部基础行的正中。</summary>
    public static List<TreeSlot> Layout(CharacterState c)
    {
        var skills = Skills(c).ToList();
        var rows = new List<List<SkillDef>>();
        foreach (var tier in skills.Where(s => s.Core == CoreKind.None && !SkillTable.Innate(s))
                     .OrderBy(s => SkillTier.Score(s)).ThenBy(s => s.Id, System.StringComparer.Ordinal)
                     .GroupBy(SkillTier.Of).OrderBy(g => g.Key))
            rows.AddRange(tier.Chunk(Columns).Select(chunk => chunk.ToList()));
        var cores = skills.Where(s => s.Core != CoreKind.None).ToList();
        if (cores.Count > 0)
            rows.Insert(rows.Count / 2, cores);
        rows.Insert(0, skills.Where(SkillTable.Innate).ToList());

        var slots = new List<TreeSlot>();
        for (var r = 0; r < rows.Count; r++)
            for (var i = 0; i < rows[r].Count; i++)
                slots.Add(new TreeSlot(rows[r][i], r, (Columns - rows[r].Count) / 2f + i));
        return slots;
    }
}
