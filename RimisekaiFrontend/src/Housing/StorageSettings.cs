using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Defs;

namespace Rimisekai.Housing;

/// <summary>仓储的优先级（RimWorld 式五档）：搬运的人把东西送进收得下它的、档最高的仓储；低档里的会往高档里倒。</summary>
public enum StoragePriority
{
    Low = 1,
    Normal = 2,
    Preferred = 3,
    Important = 4,
    Critical = 5,
}

public static class StoragePriorities
{
    public static readonly StoragePriority[] All =
        { StoragePriority.Low, StoragePriority.Normal, StoragePriority.Preferred, StoragePriority.Important, StoragePriority.Critical };

    public static string Label(StoragePriority p) => p switch
    {
        StoragePriority.Low => "低",
        StoragePriority.Normal => "普通",
        StoragePriority.Preferred => "优先",
        StoragePriority.Important => "重要",
        StoragePriority.Critical => "关键",
    };
}

/// <summary>品类在过滤里的收放状态：全收 / 部分 / 全不收。</summary>
public enum FilterState
{
    None,
    Some,
    All,
}

/// <summary>
/// 仓储的允许物品过滤（RimWorld 式）：一张「条目 → 收 / 不收」的规则表，条目是物品 Id 或品类 DefName。
/// 判一件东西：先看物品自己的规则，没有就沿它的品类往上找最近的一条，一路找到根都没有＝不收。
/// 运行时的实例（武器、甲、饰品）没有物品定义，按它的大类（Weapon / Armor / Accessory）判。
/// 给一个品类下规则会清掉它底下更细的规则——「整类收」就是整类收。
/// </summary>
public sealed class ItemFilter
{
    public const string RootCategory = "Root";

    private readonly Dictionary<string, bool> _rules = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>新建的过滤全收（代码里直接 new 的设施）；内容表里的设施由 <see cref="FacilityDef"/> 照表写规则。</summary>
    public ItemFilter() => _rules[RootCategory] = true;

    public IReadOnlyDictionary<string, bool> Rules => _rules;

    public void AllowAll()
    {
        _rules.Clear();
        _rules[RootCategory] = true;
    }

    /// <summary>全部清除：什么都不收。</summary>
    public void Clear() => _rules.Clear();

    /// <summary>只收这些条目（物品或品类）。空表＝什么都不收。</summary>
    public void Only(IEnumerable<string> entries)
    {
        _rules.Clear();
        foreach (var entry in entries)
            _rules[entry] = true;
    }

    /// <summary>读档：原样换上一张规则表。</summary>
    public void Restore(IEnumerable<KeyValuePair<string, bool>> rules)
    {
        _rules.Clear();
        foreach (var pair in rules)
            _rules[pair.Key] = pair.Value;
    }

    /// <summary>收不收这件东西。<paramref name="category"/> 是它的品类（物品定义的品类，或实例的大类）。</summary>
    public bool Allows(string itemId, string category)
    {
        if (_rules.TryGetValue(itemId, out var own))
            return own;
        return CategoryAllows(category);
    }

    /// <summary>从某个品类沿父链往上，最近的一条规则说了算；没有＝不收。</summary>
    public bool CategoryAllows(string category)
    {
        var cat = DefDatabase<ThingCategoryDef>.Get(category);
        while (cat != null)
        {
            if (_rules.TryGetValue(cat.DefName, out var rule))
                return rule;
            cat = cat.ParentCategory.Length == 0 ? null : DefDatabase<ThingCategoryDef>.Get(cat.ParentCategory);
        }
        return false;
    }

    /// <summary>给一个条目下规则。是品类就先清掉它底下所有更细的规则（子品类与其中的物品）。</summary>
    public void Set(string entry, bool allowed)
    {
        if (DefDatabase<ThingCategoryDef>.Get(entry) is { } cat)
        {
            foreach (var key in _rules.Keys.ToList())
            {
                if (key.Equals(cat.DefName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var under = DefDatabase<ThingCategoryDef>.Get(key) is { } sub
                    ? sub.IsOrChildOf(cat.DefName)
                    : Items.Get(key) is { } def && def.IsInCategory(cat.DefName);
                if (under)
                    _rules.Remove(key);
            }
        }
        _rules[entry] = allowed;
    }

    /// <summary>
    /// 一个品类在这张过滤里的收放：数它底下的每一样（物品定义，外加本身就是实例大类、没有物品定义的品类）。
    /// </summary>
    public FilterState StateOf(string category)
    {
        int yes = 0, total = 0;
        foreach (var (id, cat) in Leaves(category))
        {
            total++;
            if (id.Length == 0 ? CategoryAllows(cat) : Allows(id, cat))
                yes++;
        }
        return total == 0 ? (CategoryAllows(category) ? FilterState.All : FilterState.None)
            : yes == 0 ? FilterState.None : yes == total ? FilterState.All : FilterState.Some;
    }

    /// <summary>品类底下的每一样：物品（Id, 品类），以及没有物品定义的子品类本身（"", 品类）——实例大类就在这里。</summary>
    public static IEnumerable<(string Id, string Category)> Leaves(string category)
    {
        foreach (var def in Items.All())
        {
            if (def.IsInCategory(category))
                yield return (def.DefName, def.Category);
        }
        foreach (var cat in DefDatabase<ThingCategoryDef>.All)
        {
            if (cat.IsOrChildOf(category) && !Items.All().Any(d => d.Category.Equals(cat.DefName, StringComparison.OrdinalIgnoreCase))
                && !DefDatabase<ThingCategoryDef>.All.Any(c => c.ParentCategory.Equals(cat.DefName, StringComparison.OrdinalIgnoreCase)))
                yield return ("", cat.DefName);
        }
    }
}
