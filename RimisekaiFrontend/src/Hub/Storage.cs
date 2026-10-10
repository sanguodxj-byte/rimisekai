using System;
using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Clock;
using Rimisekai.Housing;

namespace Rimisekai.Hub;

/// <summary>存储页的一行：某件物品在设施里/背包里各有多少。</summary>
public readonly record struct StorageRow(string ItemId, int InStorage, int InBag);

/// <summary>存储配置页的品类行：整个品类收或不收。</summary>
public readonly record struct StorageCategoryRow(string DefName, string Label, bool Accepted);

/// <summary>
/// 存储设置的过滤树的一行：品类或物品。Depth＝缩进层级（根下第一层为 0）；Parent＝上一级品类（顶层为空串）；
/// 物品行 State 只有 All / None。
/// </summary>
public readonly record struct StorageFilterRow(string Entry, string Label, int Depth, string Parent, bool IsCategory, FilterState State);

/// <summary>
/// 玩家对设施的存取与存储配置。与 NPC 搬运共用 Territory 的底层（StoreFrom/TakeFrom），
/// 因此玩家手动搬与 NPC 自动搬的口径完全一致。
/// </summary>
public sealed partial class HubSession
{
    /// <summary>每存取一件物品花的时间（格，1 格 = 5 分钟）。</summary>
    public const int CostStorageTicks = 1;

    /// <summary>玩家当前打开操作界面的设施 Id；-1 表示没打开。</summary>
    public int OpenStorageId { get; private set; } = -1;

    /// <summary>打开某件设施的存储界面。只能开当前房间里能存货的设施。</summary>
    public bool OpenStorage(int facilityId)
    {
        var facility = Fixture(facilityId);
        if (facility == null || !facility.CanStore || facility.RoomId != PlayerRoomId)
            return false;
        OpenStorageId = facilityId;
        return true;
    }

    /// <summary>关掉存储界面。</summary>
    public void CloseStorage() => OpenStorageId = -1;

    /// <summary>当前打开操作的设施；没打开返回 null。</summary>
    public Facility? OpenStorageFacility => Fixture(OpenStorageId);

    /// <summary>
    /// 存储界面的一览：该设施里有的物品 + 玩家背包里有的物品，合并成一张表。
    /// 按物品 Id 排序，行序稳定。
    /// </summary>
    public IReadOnlyList<StorageRow> StorageRows()
    {
        var facility = OpenStorageFacility;
        var bag = State.Roster.Master?.Bag;
        if (facility == null || bag == null)
            return new List<StorageRow>();

        var ids = new HashSet<string>();
        foreach (var pair in facility.Contents.Items)
            ids.Add(pair.Key);
        foreach (var pair in bag.Items)
            ids.Add(pair.Key);

        var list = new List<StorageRow>();
        foreach (var id in ids)
            list.Add(new StorageRow(id, facility.Contents.Get(id), bag.Get(id)));
        list.Sort((a, b) => string.CompareOrdinal(a.ItemId, b.ItemId));
        return list;
    }

    /// <summary>玩家把手上一份物品放进当前打开的设施。满了或被过滤掉就失败。</summary>
    public bool StoreOne(string itemId, int count = 1)
    {
        var facility = OpenStorageFacility;
        var master = State.Roster.Master;
        if (facility == null || master == null || count <= 0)
            return false;
        if (State.Territory.StoreFrom(master, facility, itemId, count) <= 0)
            return false;
        PassTime(CostStorageTicks * TerritoryClock.StepMinutes);
        Write($"你把{itemId}放进了{facility.Name}。");
        return true;
    }

    /// <summary>玩家从当前打开的设施取一份物品进背包。</summary>
    public bool TakeOne(string itemId, int count = 1)
    {
        var facility = OpenStorageFacility;
        var master = State.Roster.Master;
        if (facility == null || master == null || count <= 0)
            return false;
        if (State.Territory.TakeFrom(master, facility, itemId, count) <= 0)
            return false;
        PassTime(CostStorageTicks * TerritoryClock.StepMinutes);
        Write($"你从{facility.Name}取出了{itemId}。");
        return true;
    }

    /// <summary>当前打开的设施能不能改存储设置（仓储网里的、不是水井）。</summary>
    public bool StorageConfigurable => OpenStorageFacility is { } f && State.Territory.StorageConfigurable(f);

    /// <summary>某条目（物品 Id 或品类 DefName）在当前设施过滤里的收放。</summary>
    public FilterState StorageEntryState(string entry)
    {
        var facility = OpenStorageFacility!;
        if (Defs.DefDatabase<Defs.ThingCategoryDef>.Get(entry) != null)
            return facility.StorageFilter.StateOf(entry);
        return State.Territory.Allows(facility, entry) ? FilterState.All : FilterState.None;
    }

    /// <summary>
    /// 切换一个条目：全收的改成全不收，其余（不收、部分）改成全收。改品类会连带它底下的每一样。
    /// 不可设置的设施（水井、工作台）返回 false。
    /// </summary>
    public bool ToggleStorageFilter(string entry)
    {
        if (!StorageConfigurable || entry.Length == 0)
            return false;
        OpenStorageFacility!.StorageFilter.Set(entry, StorageEntryState(entry) != FilterState.All);
        return true;
    }

    /// <summary>全部允许。</summary>
    public bool AllowAllStorage()
    {
        if (!StorageConfigurable)
            return false;
        OpenStorageFacility!.StorageFilter.AllowAll();
        return true;
    }

    /// <summary>全部清除（什么都不收）。</summary>
    public bool ClearStorageFilter()
    {
        if (!StorageConfigurable)
            return false;
        OpenStorageFacility!.StorageFilter.Clear();
        return true;
    }

    /// <summary>设当前设施的仓储优先级。</summary>
    public bool SetStoragePriority(StoragePriority priority)
    {
        if (!StorageConfigurable)
            return false;
        OpenStorageFacility!.Priority = priority;
        return true;
    }

    /// <summary>
    /// 存储设置的过滤树：从根下第一层品类起，先列子品类（递归），再列直属的物品；按内容表顺序。
    /// <paramref name="expanded"/> 里的品类展开，其余只列品类本身。
    /// </summary>
    public IReadOnlyList<StorageFilterRow> StorageFilterRows(ISet<string> expanded)
    {
        var list = new List<StorageFilterRow>();
        if (OpenStorageFacility == null)
            return list;
        void Walk(string parent, int depth)
        {
            foreach (var cat in Defs.DefDatabase<Defs.ThingCategoryDef>.All)
            {
                if (!cat.ParentCategory.Equals(parent, StringComparison.OrdinalIgnoreCase))
                    continue;
                list.Add(new StorageFilterRow(cat.DefName, cat.Label, depth, depth == 0 ? "" : parent, true, StorageEntryState(cat.DefName)));
                if (!expanded.Contains(cat.DefName))
                    continue;
                Walk(cat.DefName, depth + 1);
                foreach (var def in Defs.Items.All())
                {
                    if (def.Category.Equals(cat.DefName, StringComparison.OrdinalIgnoreCase))
                        list.Add(new StorageFilterRow(def.DefName, def.Label.Length > 0 ? def.Label : def.DefName, depth + 1,
                            cat.DefName, false, StorageEntryState(def.DefName)));
                }
            }
        }
        Walk(Housing.ItemFilter.RootCategory, 0);
        return list;
    }

    /// <summary>
    /// 存储配置页的品类行（旧版横屏页）：内容包里注册的全部品类（不含根），标注当前设施是不是整类全收。
    /// </summary>
    public IReadOnlyList<StorageCategoryRow> StorageCategoryRows()
    {
        var facility = OpenStorageFacility;
        if (facility == null)
            return new List<StorageCategoryRow>();

        var list = new List<StorageCategoryRow>();
        foreach (var cat in Defs.DefDatabase<Defs.ThingCategoryDef>.All)
        {
            if (cat.DefName.Equals(Housing.ItemFilter.RootCategory, StringComparison.OrdinalIgnoreCase))
                continue;
            list.Add(new StorageCategoryRow(cat.DefName, cat.Label, facility.StorageFilter.StateOf(cat.DefName) == FilterState.All));
        }
        return list;
    }

    /// <summary>调整当前设施的容量（0 = 不限）。</summary>
    public bool SetStorageCapacity(int capacity)
    {
        var facility = OpenStorageFacility;
        if (facility == null || capacity < 0)
            return false;
        facility.StorageCapacity = capacity;
        return true;
    }
}
