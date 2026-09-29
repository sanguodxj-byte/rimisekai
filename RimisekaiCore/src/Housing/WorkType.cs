using System.Collections.Generic;
using Rimisekai.Character;

namespace Rimisekai.Housing;

/// <summary>
/// 工作类型（工种）。工作只是行为（JobDef 的 Work）的一类，
/// 工种是工作的分类：每种工作挂一项核心属性，属性决定干活快慢。
/// 行动（ActionKind）归入工种，设施承载行动——三层自上而下：
/// 属性 → 工种 → 行动 → 设施。
/// </summary>
public enum WorkType
{
    /// <summary>采掘。力量。挖矿、伐木、采石。</summary>
    Excavate = 0,

    /// <summary>搬运。力量。取水这类纯力气的搬挪。</summary>
    Haul = 1,

    /// <summary>锻造。力量。</summary>
    Smithing = 2,

    /// <summary>种植。感知。耕作、采摘。</summary>
    Farming = 3,

    /// <summary>驯兽。魅力。饲养。</summary>
    Husbandry = 4,

    /// <summary>社交。魅力。表演、招待。</summary>
    Social = 5,

    /// <summary>手工。灵巧。木工、缝纫、工艺。</summary>
    Craft = 6,

    /// <summary>研究。智力。炼金、试验。</summary>
    Research = 7,

    /// <summary>烹饪。体质。</summary>
    Cooking = 8,
}

/// <summary>工种 → 属性。干活速度由这项属性决定。</summary>
public static class WorkTypeMap
{
    public static CoreStat CoreOf(WorkType type) => type switch
    {
        WorkType.Excavate => CoreStat.Strength,
        WorkType.Haul => CoreStat.Strength,
        WorkType.Smithing => CoreStat.Strength,
        WorkType.Farming => CoreStat.Perception,
        WorkType.Husbandry => CoreStat.Charm,
        WorkType.Social => CoreStat.Charm,
        WorkType.Craft => CoreStat.Dexterity,
        WorkType.Research => CoreStat.Intellect,
        WorkType.Cooking => CoreStat.Constitution,
        _ => CoreStat.Dexterity,
    };

    /// <summary>全部工种，按工作页行序。</summary>
    public static readonly WorkType[] Ordered =
    {
        WorkType.Excavate,
        WorkType.Haul,
        WorkType.Smithing,
        WorkType.Farming,
        WorkType.Husbandry,
        WorkType.Social,
        WorkType.Craft,
        WorkType.Research,
        WorkType.Cooking,
    };

    public const int Count = 9;

    /// <summary>是不是重活。懒散、怕痛的人拒干。</summary>
    public static bool IsHard(WorkType type) =>
        type is WorkType.Excavate or WorkType.Smithing;

    /// <summary>工种名（用于日志与界面）。</summary>
    public static string LabelOf(WorkType type) => type switch
    {
        WorkType.Excavate => "采掘",
        WorkType.Haul => "搬运",
        WorkType.Smithing => "锻造",
        WorkType.Farming => "种植",
        WorkType.Husbandry => "驯兽",
        WorkType.Social => "社交",
        WorkType.Craft => "手工",
        WorkType.Research => "研究",
        WorkType.Cooking => "烹饪",
        _ => "?",
    };
}
