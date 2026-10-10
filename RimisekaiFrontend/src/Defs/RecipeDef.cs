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

    /// <summary>
    /// 装备配方：照这份规格锻一件兵器或甲的实例（见 <see cref="Territory.ForgeGear"/>），
    /// 此时 <see cref="ItemId"/> 只是配方的名字（如「铁剑」），不是物品。null = 普通物品配方。
    /// </summary>
    public RecipeGear? Gear { get; init; }

    /// <summary>手艺门类（「锻」「工」或空串）：只在门类相同的台子上做，见 <see cref="FacilityDef.Craft"/>。</summary>
    public string Craft { get; init; } = "";

    /// <summary>转为运行时配方对象。</summary>
    public Recipe ToRuntime()
    {
        var recipe = new Recipe
        {
            ItemId = ItemId,
            Station = Station,
            OutputCount = OutputCount,
            Skill = Skill,
            Gear = Gear,
            Craft = Craft,
        };
        recipe.Costs.AddRange(Costs);
        return recipe;
    }
}

/// <summary>装备配方的规格：什么材料，打哪种兵器或缝哪个槽位的甲（二选一）。</summary>
public sealed class RecipeGear
{
    /// <summary>材料 DefName（材料等级表 materials.xml 里的一档）。</summary>
    public string Material { get; init; } = "";

    /// <summary>兵器种类；做甲时为 null。</summary>
    public WeaponType? Weapon { get; init; }

    /// <summary>甲的槽位；做兵器时为 null。</summary>
    public EquipSlot? Slot { get; init; }
}
