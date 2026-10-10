using System.Collections.Generic;
using System.Linq;
using Rimisekai.Catalog;
using Rimisekai.Defs;

namespace Rimisekai.Combat;

/// <summary>
/// 道具招式：战斗里用一个物品（如药剂），由物品表现造，不进技能表、不上技能网。
/// 招式 Id 与名字就是物品的 defName 与名字；用一次耗一个（<see cref="SkillDef.Item"/>），只有控制方带着存货时可用。
/// 现只有回血一种（<see cref="ThingDef.BattleHealPercent"/>）。
/// </summary>
public static class ItemActions
{
    public static IReadOnlyList<SkillDef> All
    {
        get
        {
            DefLoader.EnsureInitialized();
            return DefDatabase<ThingDef>.All.Where(t => t.BattleHealPercent > 0).Select(ToAction).ToList();
        }
    }

    public static SkillDef? Find(string id)
    {
        DefLoader.EnsureInitialized();
        return DefDatabase<ThingDef>.Get(id) is { BattleHealPercent: > 0 } thing ? ToAction(thing) : null;
    }

    private static SkillDef ToAction(ThingDef thing) => new()
    {
        DefName = thing.DefName,
        Label = thing.Label,
        Kind = SkillKind.Heal,
        Target = SkillTarget.Ally,
        Range = SkillRange.Ranged,
        Power = thing.BattleHealPercent,
        Item = thing.DefName,
    };
}
