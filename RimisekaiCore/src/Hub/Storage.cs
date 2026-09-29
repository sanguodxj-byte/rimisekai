using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Clock;
using Rimisekai.Housing;

namespace Rimisekai.Hub;

/// <summary>存储页的一行：某件物品在设施里/背包里各有多少。</summary>
public readonly record struct StorageRow(string ItemId, int InStorage, int InBag);

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
        foreach (var id in facility.StorageFilter)
            ids.Add(id);

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

    /// <summary>
    /// 切换当前设施对某物品的收放：在“只收这些”与“不收这个”之间切换。
    /// 过滤器为空时表示来者不拒；一旦开始排除，就变成白名单。
    /// </summary>
    public bool ToggleStorageFilter(string itemId)
    {
        var facility = OpenStorageFacility;
        if (facility == null || itemId.Length == 0)
            return false;
        if (facility.StorageFilter.Count == 0)
        {
            // 原本全部允许；要禁止这件，先把其他既有物品加进白名单。
            foreach (var row in StorageRows())
            {
                if (row.ItemId != itemId)
                    facility.StorageFilter.Add(row.ItemId);
            }
            return true;
        }
        if (facility.StorageFilter.Contains(itemId))
            facility.StorageFilter.Remove(itemId);
        else
            facility.StorageFilter.Add(itemId);
        return true;
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
