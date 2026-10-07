using Rimisekai.Character;
using Rimisekai.Defs;

namespace Rimisekai.Housing;

/// <summary>
/// 行动。人（玩家或角色）做的**一件事**。这是行动的唯一枚举：
/// 玩家点击与角色 AI 决策引用同一套值，区别只在驱动源与叙述人称。
///
/// 行动按承载分三类：
/// - 工作行动：在资源点/工作台上干活，有产出，归工种（<see cref="WorkType"/>），可委派。
/// - 日常行动：在设施上起居（吃饭、睡觉、读书……）。
/// - 房间行动：设施就是房间本身（打扫、观察），不占用具体设施。
/// 另有自主行为（搬运、搭话、闲转）：由 AI 决策发起，不需要设施。
/// </summary>
public enum ActionKind
{
    /// <summary>没在做任何事。Worker 的默认值，不是真实行动。</summary>
    None = -1,

    // ---------- 工作行动（归工种，有产出，可委派） ----------

    /// <summary>挖矿。采掘。</summary>
    Mine = 0,

    /// <summary>伐木。采掘。</summary>
    Fell = 1,

    /// <summary>锻造。锻造。</summary>
    Forge = 2,

    /// <summary>耕作。种植。</summary>
    Till = 3,

    /// <summary>饲养。驯兽。</summary>
    Tend = 4,

    /// <summary>表演。社交。</summary>
    Perform = 5,

    /// <summary>交易。社交。在摊位上做买卖。</summary>
    Trade = 6,

    /// <summary>木工。手工。</summary>
    Woodwork = 7,

    /// <summary>缝纫。手工。</summary>
    Sew = 8,

    /// <summary>炼金。研究。</summary>
    Brew = 9,

    /// <summary>烹饪。烹饪。</summary>
    Cook = 10,

    // ---------- 日常行动（在设施上起居） ----------

    /// <summary>吃饭。</summary>
    Meal = 20,

    /// <summary>睡觉。</summary>
    Sleep = 21,

    /// <summary>休息。</summary>
    Rest = 22,

    /// <summary>洗澡。</summary>
    Bathe = 23,

    /// <summary>喝一杯。</summary>
    Drink = 24,

    /// <summary>看书。</summary>
    Read = 25,

    /// <summary>祈祷。</summary>
    Pray = 26,

    /// <summary>操练。</summary>
    Train = 27,

    /// <summary>冥想。</summary>
    Meditate = 28,

    /// <summary>看戏。</summary>
    Watch = 29,

    /// <summary>看星星。</summary>
    Stargaze = 30,

    /// <summary>眺望远方。</summary>
    Lookout = 31,

    /// <summary>存取东西。</summary>
    Store = 33,

    // ---------- 房间行动（设施是房间本身） ----------
    // 注意：打扫不是行动——那是女仆闲时的叙述文案，见 Workday.DescribeLoiter。

    /// <summary>观察四周。房间本身承载。</summary>
    Observe = 41,

    // ---------- 自主行为（AI 决策，不需要设施） ----------

    /// <summary>搬运：把背包里的东西送进仓储。</summary>
    Haul = 50,

    /// <summary>搭话：主动去找玩家说话。</summary>
    SeekChat = 51,

    /// <summary>闲转：无委派时的默认行为，歇脚或逛逛。</summary>
    Loiter = 52,

    /// <summary>跟随：接受邀请后跟着玩家走，玩家干什么就在旁边。</summary>
    Follow = 53,
}

/// <summary>行动的分类与派生属性。</summary>
public static class ActionKindMap
{

    /// <summary>是不是工作行动（归工种、有产出、可委派）。</summary>
    public static bool IsWork(ActionKind action) => action is >= ActionKind.Mine and <= ActionKind.Cook;

    /// <summary>是不是日常行动（在设施上起居）。</summary>
    public static bool IsDaily(ActionKind action) => action is >= ActionKind.Meal and <= ActionKind.Store;

    /// <summary>是不是房间行动（设施是房间本身）。</summary>
    public static bool IsRoom(ActionKind action) => action == ActionKind.Observe;

    /// <summary>是不是自主行为（AI 决策，不需设施）。</summary>
    public static bool IsAutonomous(ActionKind action) =>
        action is ActionKind.Haul or ActionKind.SeekChat or ActionKind.Loiter;

    /// <summary>工作行动归入哪个工种。非工作行动返回 null。</summary>
    public static WorkType? TypeOf(ActionKind action)
    {
        var def = DefDatabase<ActionDef>.Get(action.ToString());
        if (def != null && !string.IsNullOrEmpty(def.WorkType))
        {
            if (System.Enum.TryParse<WorkType>(def.WorkType, ignoreCase: true, out var wt))
                return wt;
        }

        return action switch
        {
            ActionKind.Mine => WorkType.Excavate,
            ActionKind.Fell => WorkType.Excavate,
            ActionKind.Forge => WorkType.Smithing,
            ActionKind.Till => WorkType.Farming,
            ActionKind.Tend => WorkType.Husbandry,
            ActionKind.Perform => WorkType.Social,
            ActionKind.Trade => WorkType.Social,
            ActionKind.Woodwork => WorkType.Craft,
            ActionKind.Sew => WorkType.Craft,
            ActionKind.Brew => WorkType.Research,
            ActionKind.Cook => WorkType.Cooking,
            _ => null,
        };
    }

    /// <summary>工作行动对应的生活技能。非工作行动返回 null。</summary>
    public static LifeSkill? SkillOf(ActionKind action)
    {
        var def = DefDatabase<ActionDef>.Get(action.ToString());
        if (def != null && !string.IsNullOrEmpty(def.Skill))
        {
            if (System.Enum.TryParse<LifeSkill>(def.Skill, ignoreCase: true, out var ls))
                return ls;
        }

        return action switch
        {
            ActionKind.Mine or ActionKind.Fell => LifeSkill.Mining,
            ActionKind.Forge => LifeSkill.Smithing,
            ActionKind.Till => LifeSkill.Farming,
            ActionKind.Tend => LifeSkill.Husbandry,
            ActionKind.Perform or ActionKind.Trade => LifeSkill.Social,
            ActionKind.Woodwork or ActionKind.Sew => LifeSkill.Craft,
            ActionKind.Brew => LifeSkill.Research,
            ActionKind.Cook => LifeSkill.Cooking,
            _ => null,
        };
    }

    /// <summary>采集类工作行动：产量按技能算，受季节天气影响。</summary>
    public static bool IsExtractive(ActionKind action) =>
        action is ActionKind.Mine or ActionKind.Fell
            or ActionKind.Till or ActionKind.Tend;

    /// <summary>可委派的工作行动，按工作页行序。</summary>
    public static readonly ActionKind[] WorkOrdered =
    {
        ActionKind.Mine,
        ActionKind.Fell,
        ActionKind.Forge,
        ActionKind.Till,
        ActionKind.Tend,
        ActionKind.Perform,
        ActionKind.Trade,
        ActionKind.Woodwork,
        ActionKind.Sew,
        ActionKind.Brew,
        ActionKind.Cook,
    };

    /// <summary>技能基准点：这一档技能值对应 100% 速度。</summary>
    public const int SkillBaseline = 40;

    /// <summary>技能速度系数的上下限（百分比）。</summary>
    public const int SpeedMinPercent = 50;
    public const int SpeedMaxPercent = 200;

    /// <summary>
    /// 技能对干活速度的加成（百分比）。基准 40 为 100%，
    /// 每高 1 点快 1%，每低 1 点慢 1%，夹在 50~200。
    /// 技能 = 核心属性 + 该项经验/100，因此属性经技能直接决定手快慢。
    /// </summary>
    public static int SpeedPercent(CharacterState c, ActionKind action)
    {
        var skill = SkillOf(action);
        if (skill == null)
            return 100;
        var value = c.Life(skill.Value);
        return System.Math.Clamp(100 + (value - SkillBaseline), SpeedMinPercent, SpeedMaxPercent);
    }

    /// <summary>行动名。工作页行名、日志文案优先取数据表。</summary>
    public static string LabelOf(ActionKind action)
    {
        var def = DefDatabase<ActionDef>.Get(action.ToString());
        if (def != null && !string.IsNullOrEmpty(def.Label))
            return def.Label;

        return action switch
        {
            ActionKind.Mine => "挖矿",
            ActionKind.Fell => "伐木",
            ActionKind.Forge => "锻造",
            ActionKind.Till => "耕作",
            ActionKind.Tend => "饲养",
            ActionKind.Perform => "表演",
            ActionKind.Trade => "交易",
            ActionKind.Woodwork => "木工",
            ActionKind.Sew => "缝纫",
            ActionKind.Brew => "炼金",
            ActionKind.Cook => "烹饪",
            ActionKind.Meal => "吃饭",
            ActionKind.Sleep => "睡觉",
            ActionKind.Rest => "休息",
            ActionKind.Bathe => "洗澡",
            ActionKind.Drink => "喝一杯",
            ActionKind.Read => "看书",
            ActionKind.Pray => "祈祷",
            ActionKind.Train => "操练",
            ActionKind.Meditate => "冥想",
            ActionKind.Watch => "看戏",
            ActionKind.Stargaze => "看星星",
            ActionKind.Lookout => "眺望",
            ActionKind.Store => "存取",
            ActionKind.Observe => "观察",
            ActionKind.Haul => "搬运",
            ActionKind.SeekChat => "搭话",
            ActionKind.Loiter => "闲转",
            ActionKind.Follow => "跟随",
            _ => "?",
        };
    }

    /// <summary>行动的耗时（格，1 格=5 分钟）。优先取数据表配置。</summary>
    public static int Ticks(ActionKind action)
    {
        var def = DefDatabase<ActionDef>.Get(action.ToString());
        if (def != null && def.Ticks > 0)
            return def.Ticks;

        return action switch
        {
            ActionKind.Cook or ActionKind.Trade => 6,
            ActionKind.Woodwork or ActionKind.Sew => 12,
            ActionKind.Forge or ActionKind.Brew => 24,
            ActionKind.Mine or ActionKind.Fell
                or ActionKind.Till or ActionKind.Tend => 8,
            _ => 4,
        };
    }
}
