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
}
