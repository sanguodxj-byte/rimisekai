using System.Collections.Generic;
using Rimisekai.Character;

namespace Rimisekai.Housing;

/// <summary>一天四块，每块 6 小时。0=0:00 1=6:00 2=12:00 3=18:00。</summary>
public static class WorkSlot
{
    public const int Count = 4;
}

/// <summary>
/// 时段开关。取代过去"每段派一件具体事"——现在是三类：
/// 空闲 / 工作 / 不干活。工作时段里干什么由优先级表决定
/// （见 <see cref="Territory.PriorityOf"/>）。
/// </summary>
public enum SlotMode
{
    /// <summary>空闲：过自己的日子（歇着 / 串门 / 房里忙活）。</summary>
    Free = 0,

    /// <summary>工作：按优先级表挑一件能干的活。</summary>
    Work = 1,

    /// <summary>不干活：歇着。</summary>
    Rest = 2,
}

/// <summary>一天四段的开关。默认全空闲——不设默认日程，委派只由玩家下。</summary>
public sealed class Schedule
{
    public SlotMode[] Slots { get; } = NewDay();

    public static SlotMode[] NewDay() => new SlotMode[WorkSlot.Count];
}

/// <summary>资源点或工作台。Usage 决定它接什么（工作行动或日常角色），Capacity 是同时能上的人数。</summary>
public sealed class Facility
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int RoomId { get; set; }
    public FacilityUsage Usage { get; init; }
    public int Capacity { get; init; } = 1;
    public string YieldItemId { get; init; } = "";
    public bool Built { get; set; } = true;
    public int BuildCost { get; init; }
    public string EffectId { get; init; } = "";
    public List<RecipeCost> MaterialCost { get; } = new();
    public bool Buildable { get; set; }

    /// <summary>
    /// 这件设施支持的行动。空集表示刻意不可交互（如桌类，只影响吃饭心情）。
    /// 判定走 <see cref="FacilityActions.Supports"/>。
    /// </summary>
    public HashSet<ActionKind> Actions { get; } = new();

    /// <summary>
    /// 是不是桌子。桌子本身没有行动，但“同一间房里有桌子”会让在这里吃饭的人不扣心情。
    /// 用显式标记而不是名字或用途推断：同为桌子的长桌（Cooking）与会议桌（Free）用途并不一致。
    /// </summary>
    public bool IsTable { get; set; }

    /// <summary>
    /// 这件设施能不能存东西。由内容包声明（storage 字段）——
    /// 货架、仓库、矿脉这类能存，灶、床这类不能。
    /// 能存的设施自带一份 <see cref="Contents"/>，物品就放在这里，没有虚空库存。
    /// </summary>
    public bool CanStore { get; set; }

    /// <summary>这件设施里存着的东西。只有 CanStore 为真时才会用到。</summary>
    public Stock Contents { get; } = new();

    /// <summary>
    /// 存储容量（能放多少件）。0 表示不限。
    /// 玩家可在存储配置页调整；NPC 搬运时会检查还剩多少空位。
    /// </summary>
    public int StorageCapacity { get; set; }

    /// <summary>
    /// 存储过滤：只允许放这些物品 Id。空集表示来者不拒。
    /// 玩家可在存储配置页按物品勾选。
    /// </summary>
    public HashSet<string> StorageFilter { get; } = new();

    /// <summary>这件设施此刻装着多少件东西。</summary>
    public int StoredCount()
    {
        var total = 0;
        foreach (var pair in Contents.Items)
            total += pair.Value;
        return total;
    }

    /// <summary>还能再放多少件。不限容量时返回 <see cref="int.MaxValue"/>。</summary>
    public int FreeSpace() =>
        StorageCapacity <= 0 ? int.MaxValue : System.Math.Max(0, StorageCapacity - StoredCount());

    /// <summary>这件设施收不收这种物品：先过白名单，再看有没有空位。</summary>
    public bool Accepts(string itemId)
    {
        if (!CanStore || itemId.Length == 0)
            return false;
        if (StorageFilter.Count > 0 && !StorageFilter.Contains(itemId))
            return false;
        return FreeSpace() > 0;
    }
}

public readonly record struct RecipeCost(string ItemId, int Count);

/// <summary>食物品级。普通不回心情，精致/丰盛/绝味回 2/4/8。</summary>
public enum FoodTier
{
    Plain = 0,
    Delicate = 1,
    Feast = 2,
    Exquisite = 3,
}

/// <summary>领地里的客人。只记录人在哪、来干什么，不跑 AI。</summary>
public sealed class Guest
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int RoomId { get; set; }
    public string Purpose { get; init; } = "";
}

/// <summary>商店报价。收购价是卖给店，售价是从店买。</summary>
public sealed class MarketOffer
{
    public string ItemId { get; init; } = "";
    public int BuyPrice { get; init; }
    public int SellPrice { get; init; }
}

public sealed class Recipe
{
    public string ItemId { get; init; } = "";
    public ActionKind Station { get; init; }
    public int OutputCount { get; init; } = 1;
    public LifeSkill Skill { get; init; } = LifeSkill.Craft;
    public List<RecipeCost> Costs { get; init; } = new();
}

public sealed class WorkLog
{
    public int CharacterId { get; init; }
    public int Slot { get; init; }
    public ActionKind Task { get; init; }
    public string ItemId { get; init; } = "";
    public int Count { get; init; }
    public LifeSkill Skill { get; init; }
    public int Exp { get; init; }
    public bool Fallback { get; init; }
}
