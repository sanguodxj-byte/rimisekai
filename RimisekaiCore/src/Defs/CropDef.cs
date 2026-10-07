using System.Collections.Generic;
using Rimisekai.Clock;

namespace Rimisekai.Defs;

/// <summary>
/// 作物定义。耕地设施的 YieldItemId 命中某作物的 ProduceItemId 时，
/// 该设施就是一块耕地：空地播种（消耗种子），当季每天长一格，长满收获。
/// </summary>
public sealed class CropDef : Def, IIdentifiedDef
{
    /// <summary>数值 Id（按设施表关联用）。</summary>
    public int Id { get; init; }

    /// <summary>种子物品 Id（播种时从背包或仓储消耗）。</summary>
    public string SeedItemId { get; init; } = "";

    /// <summary>收获物 Id，与耕地设施的 YieldItemId 对应。</summary>
    public string ProduceItemId { get; init; } = "";

    /// <summary>应季生长天数。长满即成熟。</summary>
    public int GrowthDays { get; init; } = 6;

    /// <summary>可播种且生长的季节。非当季播种不了，也不生长（暂停不枯）。</summary>
    public List<Season> Seasons { get; init; } = new();

    /// <summary>该作物当季能不能生长（也就是能不能播种）。</summary>
    public bool GrowsIn(Season season) => Seasons.Contains(season);
}
