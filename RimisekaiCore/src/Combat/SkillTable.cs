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
    /// 角色当前已解锁的能力。前置技能可以成链，故迭代求不动点：
    /// 每轮把新解锁的技能并入已解锁集，直到一轮下来没有新增为止。
    /// </summary>
    public static IEnumerable<SkillDef> Known(CharacterState character) =>
        Known(character, All);

    /// <summary>
    /// 求一组技能里角色已解锁的那些，按传入顺序输出。
    /// 前置技能可以成链（A→B→C），故迭代到不动点：每轮把新解锁的并入已解锁集，
    /// 直到一轮下来没有新增为止——调用方不必自行给技能排序。
    /// 独立成方法是为了能用自造技能表直接测这条链式规则。
    /// </summary>
    public static IEnumerable<SkillDef> Known(CharacterState character, IReadOnlyList<SkillDef> catalog)
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

    /// <summary>按 Id 查能力，权威数据源为 DefDatabase。</summary>
    public static SkillDef? Get(string id)
    {
        Defs.DefLoader.EnsureInitialized();
        return Defs.DefDatabase<SkillDef>.Get(id);
    }
}
