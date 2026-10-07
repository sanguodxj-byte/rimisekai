using System;
using Rimisekai.Character;
using Rimisekai.Housing;

namespace Rimisekai.Defs;

/// <summary>
/// 物品定义。包含分类、堆叠上限、市场基准价值、是否可食用及食用收益。
/// </summary>
public class ThingDef : Def
{
    /// <summary>所属品类的 DefName。</summary>
    public string Category { get; init; } = "";

    /// <summary>堆叠上限（0 表示不限）。</summary>
    public int StackLimit { get; init; } = 999;

    /// <summary>市场基准价格（铜币/银币）。</summary>
    public int MarketValue { get; init; } = 1;

    /// <summary>是否可食用。</summary>
    public bool IsFood { get; init; }

    /// <summary>食物品级（普通/精致/丰盛/绝味）。</summary>
    public FoodTier FoodTier { get; init; } = FoodTier.Plain;

    /// <summary>营养/饱腹恢复点数。</summary>
    public int Nutrition { get; init; } = 20;

    /// <summary>食用后提供的心情加成（可正可负）。</summary>
    public int MoodBonus { get; init; }

    /// <summary>快速查询当前物品是否属于指定品类（含子级继承）。</summary>
    public bool IsInCategory(string categoryDefName)
    {
        if (string.IsNullOrEmpty(Category))
            return false;
        var cat = DefDatabase<ThingCategoryDef>.Get(Category);
        return cat != null && cat.IsOrChildOf(categoryDefName);
    }

    /// <summary>武器类型。null 表示不是武器。</summary>
    public WeaponType? Weapon { get; init; }

    /// <summary>材料标签（木、铁、石……）。武器由"材料 + 类型"两轴决定，这里记材料那一轴。</summary>
    public string Material { get; init; } = "";

    /// <summary>是不是武器。</summary>
    public bool IsWeapon => Weapon.HasValue;

    /// <summary>是不是某种材料做的（按材料标签比，大小写不敏感）。</summary>
    public bool IsOfMaterial(string material) =>
        !string.IsNullOrEmpty(Material)
        && string.Equals(Material, material, StringComparison.OrdinalIgnoreCase);
}
