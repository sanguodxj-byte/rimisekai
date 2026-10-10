using System.Collections.Generic;
using System.Linq;
using Rimisekai.Catalog;
using Rimisekai.Character;

namespace Rimisekai.Combat;

/// <summary>
/// 能力表：技能的规则与查询门面。
/// 技能数据严格由 content/defs/skills.json 驱动，经 DefDatabase<SkillDef> 统一管理，
/// 禁止在代码中硬编码任何技能数据表。
/// </summary>
public static class SkillTable
{
    public static IReadOnlyList<SkillDef> All
    {
        get
        {
            Defs.DefLoader.EnsureInitialized();
            return Defs.DefDatabase<SkillDef>.All;
        }
    }

    /// <summary>
    /// 角色此刻能用的战斗技能＝技能网上已激活的（见 <see cref="SkillTree.Active"/>）：门槛（流派、熟练、属性……）达到即自动激活，不必学习。
    /// </summary>
    public static IEnumerable<SkillDef> Known(CharacterState character) => SkillTree.Active(character);

    /// <summary>无任何门槛的通用技能（普通攻击、防御架势）：人人自带。</summary>
    public static bool Innate(SkillDef skill)
    {
        var g = skill.Gate;
        return !g.Style.HasValue && !g.Weapon.HasValue && (g.Core == null || g.Core.Count == 0) && (g.Life == null || g.Life.Count == 0)
            && (g.Traits == null || g.Traits.Count == 0) && (g.Prerequisites == null || g.Prerequisites.Count == 0);
    }

    /// <summary>
    /// 求一组技能里角色已解锁的那些，按传入顺序输出。
    /// 前置技能可以成链（A→B→C），故迭代到不动点：每轮把新解锁的并入已解锁集，
    /// 直到一轮下来没有新增为止——调用方不必自行给技能排序。
    /// 独立成方法是为了能用自造技能表直接测这条链式规则。
    /// </summary>
    public static IEnumerable<SkillDef> MeetsGates(CharacterState character, IReadOnlyList<SkillDef> catalog)
    {
        var unlocked = new HashSet<string>();
        var remaining = new List<SkillDef>(catalog);

        bool changed;
        do
        {
            changed = false;
            for (var i = remaining.Count - 1; i >= 0; i--)
            {
                var skill = remaining[i];
                if (!skill.Gate.Meets(character, unlocked))
                    continue;
                unlocked.Add(skill.Id);
                remaining.RemoveAt(i);
                changed = true;
            }
        } while (changed);

        // 按目录原序输出，界面与部署的顺序稳定。
        var result = new List<SkillDef>(unlocked.Count);
        foreach (var skill in catalog)
            if (unlocked.Contains(skill.Id))
                result.Add(skill);
        return result;
    }

    /// <summary>按 Id 查能力，权威数据源为 DefDatabase：先查通用技能，再查身份技能池，再查道具招式。</summary>
    public static SkillDef? Get(string id)
    {
        Defs.DefLoader.EnsureInitialized();
        return Defs.DefDatabase<SkillDef>.Get(id) ?? SkillPool.Find(id) ?? ItemActions.Find(id);
    }
}
