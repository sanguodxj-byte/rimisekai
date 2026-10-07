using System.Collections.Generic;
using Rimisekai.Housing;

namespace Rimisekai.Defs;

/// <summary>
/// 任何"东西"的解析结果。三种来源共用这一个形状：
/// 独特物品（ThingDef 表）、运行时武器实例（WeaponRegistry）。
/// 界面与定价只认这一份，不用各自分辨来源。
/// </summary>
public readonly record struct ThingInfo(
    string Id,
    string Label,
    string Category,
    int MarketValue,
    bool IsFood,
    FoodTier FoodTier,
    bool IsWeaponInstance)
{
    public static ThingInfo Of(ThingDef def) => new(
        def.DefName, def.Label, def.Category, def.MarketValue,
        def.IsFood, def.FoodTier, false);

    public static ThingInfo Of(WeaponInstance w) => new(
        w.Id, w.Name, "Weapon", w.Value, false, FoodTier.Plain, true);
}

/// <summary>
/// 物品解析入口。先查独特物品表，再查运行时武器实例登记表。
/// </summary>
public static class Items
{
    /// <summary>
    /// 全部物品定义：独特物品表 + 材料表。材料继承 ThingDef，本身也是物品，
    /// 遍历物品的地方（市场、可交易表、库存页）都走这里，不再各自分辨来源。
    /// </summary>
    public static IEnumerable<ThingDef> All()
    {
        foreach (var def in DefDatabase<ThingDef>.All)
            yield return def;
        foreach (var def in DefDatabase<MaterialDef>.All)
            yield return def;
    }

    /// <summary>
    /// 按 Id 取定义。先查物品表，再查材料表——材料继承 ThingDef，
    /// 本身也是物品，但全项目只有一份（ materials.xml ），不与 ThingDef 重复。
    /// </summary>
    public static ThingDef? Get(string itemId)
    {
        if (string.IsNullOrEmpty(itemId))
            return null;
        var def = DefDatabase<ThingDef>.Get(itemId);
        if (def != null)
            return def;
        return DefDatabase<MaterialDef>.Get(itemId);
    }

    /// <summary>按 Id 取统一解析结果；武器实例要传登记表。</summary>
    public static ThingInfo? Info(WeaponRegistry? registry, string itemId)
    {
        if (string.IsNullOrEmpty(itemId))
            return null;
        var def = Get(itemId);
        if (def != null)
            return ThingInfo.Of(def);
        var instance = registry?.Get(itemId);
        return instance == null ? null : ThingInfo.Of(instance);
    }

    /// <summary>是否已定义（独特物品或登记在册的武器实例）。</summary>
    public static bool Exists(WeaponRegistry? registry, string itemId) =>
        Info(registry, itemId) != null;
}
