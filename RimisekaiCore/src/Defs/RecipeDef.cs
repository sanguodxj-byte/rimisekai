using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Housing;

namespace Rimisekai.Defs;

/// <summary>
/// 制作/烹饪配方定义。纯数据驱动，由 DefDatabase<RecipeDef> 统一管理。
/// 声明在哪个台子（Station）做、耗什么料（Costs）、产出什么（ItemId）及几件（OutputCount）。
/// </summary>
public sealed class RecipeDef : Def
{
    /// <summary>产出的物品 DefName（如 bread, stew, 铁锭）。</summary>
    public string ItemId { get; init; } = "";

    /// <summary>承载该配方的行动（如 Cook, Forge, Sew）。</summary>
    public ActionKind Station { get; init; }

    /// <summary>单次制作产出件数。</summary>
    public int OutputCount { get; init; } = 1;

    /// <summary>制作所提升的生活技能。</summary>
    public LifeSkill Skill { get; init; } = LifeSkill.Cooking;

    /// <summary>所需的原材料清单（物品 DefName + 数量）。</summary>
    public List<RecipeCost> Costs { get; init; } = new();

    /// <summary>转为运行时配方对象。</summary>
    public Recipe ToRuntime()
    {
        var recipe = new Recipe
        {
            ItemId = ItemId,
            Station = Station,
            OutputCount = OutputCount,
            Skill = Skill,
        };
        recipe.Costs.AddRange(Costs);
        return recipe;
    }
}
