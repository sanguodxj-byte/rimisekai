using System.Collections.Generic;
using Rimisekai.Housing;

namespace Rimisekai.Hub;

public sealed partial class HubSession
{
    public const int CostDevelop = 1;

    /// <summary>开发：照抄一间已有房间。花费该类型的材料，新房默认开放、无通路。</summary>
    public bool AddRoomCopy(int sourceRoomId, int x, int y)
    {
        var source = Room(sourceRoomId);
        if (source == null || !source.Buildable || State.Territory.RoomAt(x, y) != null)
            return false;
        if (!State.Territory.CanPayWith(State.Roster.Master, source.MaterialCost))
            return false;
        PassTime(CostDevelop * TerritoryClock.StepMinutes);
        if (!State.Territory.PayWith(State.Roster.Master, source.MaterialCost))
            return false;
        var id = 0;
        foreach (var room in State.Territory.Rooms)
            id = System.Math.Max(id, room.Id);
        var added = new Room
        {
            Id = id + 1, Name = source.Name, RegionId = source.RegionId,
            X = x, Y = y, Open = true, Permission = source.Permission,
            Buildable = source.Buildable,
        };
        added.MaterialCost.AddRange(source.MaterialCost);
        if (!State.Territory.AddRoom(added))
            return false;
        Write($"新建了{added.Name}。");
        return true;
    }

    /// <summary>开发：照抄一件已有设施，放到指定房间。花费该类型的材料，建好即用。</summary>
    public bool AddFacilityCopy(int sourceFacilityId, int roomId)
    {
        var source = Fixture(sourceFacilityId);
        var room = Room(roomId);
        if (source == null || !source.Buildable || room == null || !room.Open)
            return false;
        if (!State.Territory.CanPayWith(State.Roster.Master, source.MaterialCost))
            return false;
        PassTime(CostDevelop * TerritoryClock.StepMinutes);
        if (!State.Territory.PayWith(State.Roster.Master, source.MaterialCost))
            return false;
        var id = 0;
        foreach (var facility in State.Territory.Facilities)
            id = System.Math.Max(id, facility.Id);
        var added = new Facility
        {
            Id = id + 1, Name = source.Name, RoomId = roomId, Usage = source.Usage,
            Capacity = source.Capacity, YieldItemId = source.YieldItemId,
            Built = true, EffectId = source.EffectId, Buildable = source.Buildable,
        };
        added.MaterialCost.AddRange(source.MaterialCost);
        if (!State.Territory.AddFacility(added))
            return false;
        Write($"新建了{added.Name}。");
        return true;
    }

    /// <summary>开发：移动房间到空格子。里面的人和东西跟着走。</summary>
    public bool MoveRoom(int roomId, int x, int y)
    {
        var room = Room(roomId);
        if (room == null || State.Territory.RoomAt(x, y) != null)
            return false;
        if (room.X == x && room.Y == y)
            return false;
        room.X = x;
        room.Y = y;
        PassTime(CostDevelop * TerritoryClock.StepMinutes);
        Write($"移动了{room.Name}。");
        return true;
    }

    /// <summary>开发：移动设施到已开放房间。在上面干活的人回到待命。</summary>
    public bool MoveFacility(int facilityId, int roomId)
    {
        var facility = Fixture(facilityId);
        var room = Room(roomId);
        if (facility == null || room == null || !room.Open || facility.RoomId == roomId)
            return false;
        facility.RoomId = roomId;
        ResetFacilityWorkers(facilityId);
        PassTime(CostDevelop * TerritoryClock.StepMinutes);
        Write($"移动了{facility.Name}。");
        return true;
    }

    /// <summary>开发：开关房间。开花钱，关免费（里面的人先挪走，玩家在里面也能关，出得来）。</summary>
    public bool SetRoomOpen(int roomId, bool open)
    {
        var room = Room(roomId);
        if (room == null || room.Open == open)
            return false;
        if (open)
        {
            var money = State.Money;
            if (!State.Territory.OpenRoom(roomId, ref money))
                return false;
            PassTime(CostDevelop * TerritoryClock.StepMinutes);
            State.Money = money;
            Write($"开拓了{room.Name}。");
            return true;
        }
        if (!RelocateRoom(roomId))
            return false;
        room.Open = false;
        PassTime(CostDevelop * TerritoryClock.StepMinutes);
        Write($"关闭了{room.Name}。");
        return true;
    }

    /// <summary>开发：加/拆两房之间的通路。</summary>
    public bool SetLink(int fromId, int toId, bool linked)
    {
        if (fromId == toId)
            return false;
        var a = Room(fromId);
        var b = Room(toId);
        if (a == null || b == null)
            return false;
        if (linked == a.Links.Contains(toId))
            return false;
        if (linked)
            State.Territory.Link(fromId, toId);
        else
            State.Territory.Unlink(fromId, toId);
        PassTime(CostDevelop * TerritoryClock.StepMinutes);
        Write(linked ? $"连通了{a.Name}和{b.Name}。" : $"断开了{a.Name}和{b.Name}。");
        return true;
    }

    /// <summary>开发：拆除房间。玩家在里面不行；里面的人先挪走；房内设施一起拆；材料按 60% 返还。</summary>
    public bool RemoveRoom(int roomId)
    {
        var room = Room(roomId);
        if (room == null || PlayerRoomId == roomId)
            return false;
        if (!RelocateRoom(roomId))
            return false;
        var fixtures = State.Territory.Facilities.FindAll(f => f.RoomId == roomId);
        foreach (var fixture in fixtures)
        {
            if (UsingFixtureId == fixture.Id)
                LeaveFixture();
            ResetFacilityWorkers(fixture.Id);
            State.Territory.Facilities.Remove(fixture);
        }
        foreach (var link in new List<int>(room.Links))
            State.Territory.Unlink(roomId, link);
        State.Territory.Rooms.Remove(room);
        PassTime(CostDevelop * TerritoryClock.StepMinutes);
        foreach (var fixture in fixtures)
            Refund(fixture.MaterialCost);
        Refund(room.MaterialCost);
        Write($"拆除了{room.Name}。");
        return true;
    }

    /// <summary>开发：拆除设施。材料按 60% 返还，在上面的人回到待命。</summary>
    public bool RemoveFacility(int facilityId)
    {
        var facility = Fixture(facilityId);
        if (facility == null)
            return false;
        ResetFacilityWorkers(facilityId);
        if (UsingFixtureId == facilityId)
            LeaveFixture();
        State.Territory.RemoveEffect(facility);
        State.Territory.Facilities.Remove(facility);
        PassTime(CostDevelop * TerritoryClock.StepMinutes);
        Refund(facility.MaterialCost);
        Write($"拆除了{facility.Name}。");
        return true;
    }

    private void Refund(List<RecipeCost> costs)
    {
        foreach (var cost in costs)
        {
            var back = cost.Count * 60 / 100;
            if (back > 0)
                State.Roster.Master?.Bag.Add(cost.ItemId, back);
        }
    }

    private void ResetFacilityWorkers(int facilityId)
    {
        foreach (var worker in Day.Workers)
        {
            if (worker.FacilityId != facilityId)
                continue;
            worker.Task = ActionKind.None;
            worker.Phase = WorkPhase.Idle;
            worker.Goal = ActionKind.None;
            worker.Path.Clear();
            worker.FacilityId = -1;
        }
    }

    /// <summary>把房间里的 NPC 挪到最近的开放房间。玩家不动，挪不走返回 false。</summary>
    private bool RelocateRoom(int roomId)
    {
        var room = Room(roomId);
        var fallback = -1;
        if (room != null)
        {
            foreach (var link in room.Links)
            {
                var neighbor = Room(link);
                if (neighbor != null && neighbor.Open && neighbor.Id != roomId)
                {
                    fallback = neighbor.Id;
                    break;
                }
            }
        }
        if (fallback < 0)
        {
            foreach (var candidate in State.Territory.Rooms)
            {
                if (candidate.Open && candidate.Id != roomId)
                {
                    fallback = candidate.Id;
                    break;
                }
            }
        }
        foreach (var character in State.Roster.Members)
        {
            if (character.IsMaster)
                continue;
            var worker = FindWorker(character.Id);
            var there = _presence.GetValueOrDefault(character.Id, -1) == roomId
                || (worker != null && worker.RoomId == roomId);
            if (!there)
                continue;
            if (fallback < 0)
                return false;
            if (worker != null)
            {
                worker.RoomId = fallback;
                worker.Task = ActionKind.None;
                worker.Phase = WorkPhase.Idle;
                worker.Goal = ActionKind.None;
                worker.Path.Clear();
                worker.FacilityId = -1;
                worker.WaitTicks = 0;
                worker.WantsChat = false;
                worker.SeekWaiting = false;
                worker.ChatRoom = -1;
                worker.Together.Clear();
            }
            _presence[character.Id] = fallback;
        }
        return true;
    }

    /// <summary>开发：按建筑表建新房间。花材料，建成后先进入未放置列表。</summary>
    public bool BuildRoomDef(int defId)
    {
        if (!State.Catalog.Rooms.TryGetValue(defId, out var def) || !def.Buildable)
            return false;
        if (State.Territory.Level < def.MinTerritoryLevel)
            return false;
        if (!State.Territory.CanPayWith(State.Roster.Master, def.MaterialCost))
            return false;
        PassTime(CostDevelop * TerritoryClock.StepMinutes);
        if (!State.Territory.PayWith(State.Roster.Master, def.MaterialCost))
            return false;
        var id = 0;
        foreach (var room in State.Territory.Rooms)
            id = System.Math.Max(id, room.Id);
        var added = new Room
        {
            Id = id + 1, Name = def.Name, RegionId = def.RegionId,
            X = -1, Y = -1, Open = true, Permission = def.Permission,
            Buildable = def.Buildable,
        };
        added.MaterialCost.AddRange(def.MaterialCost);
        foreach (var tag in def.Tags)
            added.AddTag(tag);
        added.EnsureDefaultTag();
        if (!State.Territory.AddRoom(added))
            return false;
        Write($"新建了{added.Name}。");
        return true;
    }

    /// <summary>开发：按建筑表建新设施。花材料，建成后先进入未放置列表。</summary>
    public bool BuildFacilityDef(int defId)
    {
        if (!State.Catalog.Facilities.TryGetValue(defId, out var def) || !def.Buildable)
            return false;
        if (!State.Territory.CanPayWith(State.Roster.Master, def.MaterialCost))
            return false;
        PassTime(CostDevelop * TerritoryClock.StepMinutes);
        if (!State.Territory.PayWith(State.Roster.Master, def.MaterialCost))
            return false;
        var id = 0;
        foreach (var facility in State.Territory.Facilities)
            id = System.Math.Max(id, facility.Id);
        var added = new Facility
        {
            Id = id + 1, Name = def.Name, RoomId = -1, Usage = def.Usage,
            Capacity = def.Capacity, YieldItemId = def.YieldItemId,
            Built = true, EffectId = def.EffectId, Buildable = def.Buildable,
            IsTable = def.IsTable,
            CanStore = def.Storage,
        };
        added.MaterialCost.AddRange(def.MaterialCost);
        foreach (var action in def.Actions)
            added.Actions.Add(action);
        if (!State.Territory.AddUnplacedFacility(added))
            return false;
        Write($"建造了{added.Name}（未放置）。");
        return true;
    }

    public bool BuildRoomDef(int defId, int x, int y)
    {
        if (!BuildRoomDef(defId))
            return false;
        var last = State.Territory.Rooms[^1];
        return PlaceRoom(last.Id, x, y);
    }

    public bool BuildFacilityDef(int defId, int roomId)
    {
        if (!BuildFacilityDef(defId))
            return false;
        var last = State.Territory.Facilities[^1];
        return PlaceFacility(last.Id, roomId);
    }

    /// <summary>开发：把未放置的设施放进房间。</summary>
    public bool PlaceFacility(int facilityId, int roomId)
    {
        var facility = State.Territory.Facilities.Find(f => f.Id == facilityId);
        if (facility == null || facility.RoomId >= 0)
            return false;
        var roomName = Room(roomId)?.Name ?? "";
        if (!State.Territory.PlaceFacility(roomId, facility))
            return false;
        Write($"把{facility.Name}放进了{roomName}。");
        return true;
    }

    /// <summary>开发：把未放置的房间放到网格空位上。</summary>
    public bool PlaceRoom(int roomId, int x, int y)
    {
        var room = Room(roomId);
        if (room == null || room.X >= 0)
            return false;
        if (!State.Territory.PlaceRoom(x, y, room))
            return false;
        Write($"把{room.Name}放到了网格上。");
        return true;
    }

    /// <summary>建造设施：花钱 + 按工作台分表花时间。</summary>
    public bool BuildFacility(int facilityId)
    {
        var facility = State.Territory.Facilities.Find(f => f.Id == facilityId);
        if (facility == null || facility.Built)
            return false;
        var money = State.Money;
        if (!State.Territory.Build(facilityId, ref money))
            return false;
        PassTime(ActionKindMap.Ticks(facility.Actions.Count > 0 ? System.Linq.Enumerable.First(facility.Actions) : ActionKind.Observe) * TerritoryClock.StepMinutes);
        State.Money = money;
        Write($"建造了{facility.Name}。");
        return true;
    }
}
