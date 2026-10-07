namespace Rimisekai.Defs;

/// <summary>
/// 行动定义。人（玩家或角色）做的**一件事**——这是行动的唯一定义：
/// 玩家点击与角色 AI 决策引用同一张表，区别只在驱动源与叙述人称。
///
/// 行动按承载分四类（<see cref="ActionCarrier"/>）：
/// - Work：在资源点/工作台上干活，有产出，归工种（WorkTypeDef），可委派。
/// - Daily：在设施上起居（吃饭、睡觉、读书……）。
/// - Room：设施就是房间本身（观察），不占用具体设施。
/// - Autonomous：由 AI 决策发起，不需要设施。
/// </summary>
public sealed class ActionDef : Def
{
    /// <summary>谁承载这个行动。</summary>
    public ActionCarrier Carrier { get; init; } = ActionCarrier.Daily;

    /// <summary>工作行动归入哪个工种（WorkTypeDef 的 DefName）。非工作行动留空。</summary>
    public string WorkType { get; init; } = "";

    /// <summary>工作行动对应的生活技能。非工作行动留空。</summary>
    public string Skill { get; init; } = "";

    /// <summary>采集类工作行动：产量按技能算，受季节天气影响。</summary>
    public bool Extractive { get; init; }

    /// <summary>是不是生理必需项（睡眠、吃饭这类不能长期欠的）。</summary>
    public bool Vital { get; init; }

    /// <summary>可委派的工作行动在优先级表里的排序键，小的排前面。</summary>
    public int Order { get; init; }

    /// <summary>行动的耗时（格，1 格=5 分钟）。</summary>
    public int Ticks { get; init; } = 4;

    /// <summary>执行该行动单次消耗的气力点数（气力是工作消耗的精力，体力是战斗血量）。</summary>
    public int SpiritCost { get; init; } = 15;
}

/// <summary>行动由谁承载。</summary>
public enum ActionCarrier
{
    /// <summary>工作行动：归工种、有产出、可委派。</summary>
    Work,

    /// <summary>日常行动：在设施上起居。</summary>
    Daily,

    /// <summary>房间行动：设施就是房间本身。</summary>
    Room,

    /// <summary>自主行为：AI 决策发起，不需要设施。</summary>
    Autonomous,
}
