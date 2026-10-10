using System.Collections.Generic;
using Rimisekai.Character;

namespace Rimisekai.Housing;

/// <summary>一天四块，每块 6 小时。0=0:00 1=6:00 2=12:00 3=18:00。</summary>
public static class WorkSlot
{
    public const int Count = 4;
}

/// <summary>
/// 时段开关。三类：空闲 / 工作 / 娱乐。
/// 工作与娱乐都要点名一件设施——排班是「某时段到某件设施去」，
/// 不再有优先级表那种「按档位挑活」的间接层。
/// </summary>
public enum SlotMode
{
    /// <summary>空闲：过自己的日子（歇着 / 串门 / 房里忙活）。</summary>
    Free = 0,

    /// <summary>工作：到指定的工作设施干活。</summary>
    Work = 1,

    /// <summary>娱乐：到指定的消遣设施消遣（戏台、吧台、书架这类）。</summary>
    Entertainment = 2,
}

/// <summary>
/// 一个时段的安排：开关，以及工作/娱乐时点名的那件设施。
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
    /// 存储过滤：收下哪些东西。集合里可以混存两种条目——
    /// 物品 Id（只收这一件）与品类 DefName（收整个品类，含子品类）。
    /// 空集表示来者不拒。玩家可在存储配置页按物品或品类勾选。
    /// </summary>
    public HashSet<string> StorageFilter { get; } = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>这条过滤条目是品类还是具体物品。用于界面区分两种开关。</summary>
    public static bool IsCategoryEntry(string entry) =>
        Defs.DefDatabase<Defs.ThingCategoryDef>.Get(entry) != null;

    /// <summary>
    /// 这件设施收不收这种物品：先过过滤（按物品 Id 或品类命中），再看有没有空位。
    /// 命中规则：过滤为空→全收；否则物品 Id 直接命中，或其品类（含父链）命中任一条目。
    /// </summary>
    public bool Accepts(string itemId, Defs.WeaponRegistry? weapons = null)
    {
        if (!CanStore || itemId.Length == 0)
            return false;
        if (StorageFilter.Count > 0 && !FilterAccepts(itemId, weapons))
            return false;
        return FreeSpace() > 0;
    }

    /// <summary>过滤是否放行这件物品（不看容量）。武器实例要传登记表。</summary>
    public bool FilterAccepts(string itemId, Defs.WeaponRegistry? weapons = null)
    {
        if (StorageFilter.Count == 0)
            return true;
        if (StorageFilter.Contains(itemId))
            return true;

        // 运行时武器实例：它归属武器大类，按大类命中。
        var instance = weapons?.Get(itemId);
        if (instance != null)
            return MatchesCategory("Weapon");

        var def = Defs.Items.Get(itemId);
        if (def == null || string.IsNullOrEmpty(def.Category))
            return false;

        // 物品的品类自己命中，或它的任一父品类命中，都算收。
        return MatchesCategory(def.Category);
    }

    /// <summary>从某个品类出发沿父链往上，看有没有任一条被过滤收下。</summary>
    private bool MatchesCategory(string categoryDefName)
    {
        var cat = Defs.DefDatabase<Defs.ThingCategoryDef>.Get(categoryDefName);
        while (cat != null)
        {
            if (StorageFilter.Contains(cat.DefName))
                return true;
            cat = string.IsNullOrEmpty(cat.ParentCategory)
                ? null
                : Defs.DefDatabase<Defs.ThingCategoryDef>.Get(cat.ParentCategory);
        }
        return false;
    }

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

/// <summary>领地里的客人。只记录人在哪、来干什么，不跑 AI。</summary>
public sealed class Guest
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int RoomId { get; set; }
    public string Purpose { get; init; } = "";
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
