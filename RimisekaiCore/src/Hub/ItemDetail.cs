using System.Collections.Generic;
using Rimisekai.Defs;
using Rimisekai.Housing;

namespace Rimisekai.Hub;

/// <summary>
/// 一件物品的详情：副题（品类）、品质（实例才有）、逐行「标签 · 数值」条、末尾一段风味说明。
/// 仓储、装备、结算点开的详情都按这一份排版，界面上不再出现整段字段堆砌。
/// </summary>
public readonly record struct ItemDetail(string Title, string Category, Quality? Quality, IReadOnlyList<DetailLine> Lines, string Flavor);

public sealed partial class HubSession
{
    /// <summary>背包里某件东西的详情：独特物品 / 材料看定义表，武器与防具饰品看实例。</summary>
    public ItemDetail DescribeItem(string itemId)
    {
        var count = Stock().TryGetValue(itemId, out var n) ? n : 0;
        if (State.Weapons.Get(itemId) is { } weapon)
        {
            var lines = new List<DetailLine>(weapon.DescribeDetails())
            {
                new("持有", $"×{count}"),
                new("价值", $"{weapon.Value}G"),
            };
            return new ItemDetail(WeaponForge.NameOf(weapon), "武器", weapon.Quality, lines, "");
        }
        if (State.Equips.Get(itemId) is { } gear)
        {
            var lines = new List<DetailLine>(gear.DescribeDetails())
            {
                new("持有", $"×{count}"),
                new("价值", $"{gear.Value}G"),
            };
            return new ItemDetail(EquipForge.NameOf(gear), gear.Kind == EquipKind.Armor ? "防具" : "饰品", gear.Quality, lines, "");
        }
        var def = Items.Get(itemId)!;
        var category = def.Category.Length > 0 ? DefDatabase<ThingCategoryDef>.Get(def.Category)!.Label : "";
        var rows = new List<DetailLine>
        {
            new("持有", $"×{count}"),
            new("价值", $"{def.MarketValue}G"),
        };
        if (def.IsFood)
        {
            rows.Add(new("档次", FoodTiers.Label(def.FoodTier)));
            rows.Add(new("营养", $"+{def.Nutrition}"));
            if (def.MoodBonus != 0)
                rows.Add(new("心情", def.MoodBonus.ToString("+0;-0")));
        }
        return new ItemDetail(def.Label.Length > 0 ? def.Label : def.DefName, category, null, rows, def.Description);
    }
}
