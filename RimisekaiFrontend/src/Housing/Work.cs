using System.Collections.Generic;
using Rimisekai.Character;

namespace Rimisekai.Housing;

/// <summary>一天四块，每块 6 小时。0=0:00 1=6:00 2=12:00 3=18:00。</summary>
public static class WorkSlot
{
    public const int Count = 4;
}

/// <summary>
/// 时段开关。两类：空闲 / 工作。
/// 工作要点名一件工作设施——排班是「某时段到某件设施去」，
/// 不再有优先级表那种「按档位挑活」的间接层。
/// </summary>
public enum SlotMode
{
    /// <summary>空闲：过自己的日子（歇着 / 串门 / 房里忙活）。</summary>
    Free = 0,

    /// <summary>工作：到指定的工作设施干活。</summary>
    Work = 1,
}

/// <summary>
/// 一个时段的安排：开关，以及工作时点名的那件设施。
/// FacilityId 为 -1 表示没点名（工作时段会退回空闲）。
/// </summary>
public struct SlotAssignment
{
    public SlotMode Mode { get; set; }
    public int FacilityId { get; set; }

    public static SlotAssignment Free => new() { Mode = SlotMode.Free, FacilityId = -1 };
}

/// <summary>
/// 搬运的子阶段。全局禁止瞬移，搬运分为：去取料（前往源设施取料）与送货（携带料前往目标）。
/// </summary>
public enum HaulPhase
{
    /// <summary>前往源设施取料。</summary>
    Fetching = 0,

    /// <summary>携带料前往目标设施卸料。</summary>
    Delivering = 1,
}

/// <summary>一天四段的安排。默认全空闲——不设默认日程，排班只由玩家下。</summary>
public sealed class Schedule
{
    public SlotAssignment[] Slots { get; } = NewDay();

    public static SlotAssignment[] NewDay()
    {
        var slots = new SlotAssignment[WorkSlot.Count];
        for (var i = 0; i < slots.Length; i++)
            slots[i] = SlotAssignment.Free;
        return slots;
    }
}

/// <summary>资源点或工作台。Usage 决定它接什么（工作行动或日常角色），Capacity 是同时能上的人数。</summary>
public sealed class Facility
{
    /// <summary>领地内唯一的实例号。开局摆位与读档时按摆位/存档里的号设置。</summary>
    public int Id { get; set; }
    public string Name { get; init; } = "";
    public int RoomId { get; set; }
    public FacilityUsage Usage { get; init; }
    public int Capacity { get; init; } = 1;
    public string YieldItemId { get; init; } = "";

    /// <summary>
    /// 耕地上种着的作物 DefName（CropDef.DefName）。空串 = 空地。
    /// 只有 YieldItemId 命中某 CropDef 产物的设施才是耕地；其余设施不受影响。
    /// </summary>
    public string CropDefName { get; set; } = "";

    /// <summary>作物已生长的天数。当季每天 +1，长满 CropDef.GrowthDays 即成熟。</summary>
    public int Growth { get; set; }
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

    /// <summary>只能摆进带这个标签的房间（「室内」/「室外」）；空串哪儿都行。见 <see cref="Territory.Fits"/>。</summary>
    public string RoomTag { get; init; } = "";

    /// <summary>手艺门类（「锻」「窑」或空串）：只做门类相同的配方。见 <see cref="Defs.FacilityDef.Craft"/>。</summary>
    public string Craft { get; init; } = "";

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
    /// 存储过滤：允许放进来的物品（按品类或单件勾选，见 <see cref="ItemFilter"/>）。
    /// 水井例外：只收水，与过滤怎么勾无关（<see cref="IsWell"/>）。
    /// </summary>
    public ItemFilter StorageFilter { get; } = new();

    /// <summary>仓储优先级：搬运的人先往档高的送，低档里的东西会被倒进档更高、也收它的仓储。</summary>
    public StoragePriority Priority { get; set; } = StoragePriority.Normal;

    /// <summary>水井：只存水，过滤与优先级都定死（关键档——井里的水不往外倒）。</summary>
    public bool IsWell => YieldItemId == Territory.WellItemId;

    /// <summary>过滤放不放行这件东西（不看容量）。<paramref name="category"/> 是它的品类或实例大类。</summary>
    public bool Allows(string itemId, string category)
    {
        if (!CanStore || itemId.Length == 0)
            return false;
        if (IsWell)
            return itemId == Territory.WellItemId;
        return StorageFilter.Allows(itemId, category);
    }

    /// <summary>收不收：过滤放行、还有空位。</summary>
    public bool Accepts(string itemId, string category) => Allows(itemId, category) && FreeSpace() > 0;

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

/// <summary>食物档次的显示名。</summary>
public static class FoodTiers
{
    public static string Label(FoodTier tier) => tier switch
    {
        FoodTier.Plain => "朴素",
        FoodTier.Delicate => "精致",
        FoodTier.Feast => "丰盛",
        FoodTier.Exquisite => "绝味",
    };
}

public sealed class Recipe
{
    public string ItemId { get; init; } = "";
    public ActionKind Station { get; init; }
    public int OutputCount { get; init; } = 1;
    public LifeSkill Skill { get; init; } = LifeSkill.Craft;
    public List<RecipeCost> Costs { get; init; } = new();

    /// <summary>装备配方的规格；null = 普通物品配方。</summary>
    public Defs.RecipeGear? Gear { get; init; }

    /// <summary>手艺门类：只在 <see cref="Facility.Craft"/> 相同的台子上做。</summary>
    public string Craft { get; init; } = "";
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
