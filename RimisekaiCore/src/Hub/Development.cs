using System.Collections.Generic;
using Rimisekai.Defs;
using Rimisekai.Housing;

namespace Rimisekai.Hub;

public sealed partial class HubSession
{
    public const int CostDevelop = 1;

    /// <summary>开发：照抄一间已有房间。花费该类型的材料，新房默认开放，与四面已开放的邻房开门连通。</summary>
    public bool AddRoomCopy(int sourceRoomId, int x, int y)
    {
        var source = Room(sourceRoomId);
        if (source == null || !source.Buildable
            || State.Territory.RoomAt(source.RegionId, x, y) != null)
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
        State.Territory.LinkNeighbors(added);
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
        if (room == null || State.Territory.RoomAt(room.RegionId, x, y) != null)
            return false;
        if (room.X == x && room.Y == y)
            return false;
        room.X = x;
        room.Y = y;
        State.Territory.PruneDetachedLinks(room);
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

    /// <summary>
    /// 开发：把「挨着已开发地方」的未开发房间开拓成一间空房。
    /// 花该房间的钱（OpenCost）与材料（MaterialCost），开出来就是一间空房间，
    /// 家具另配（去中下操作面板里建设施）。
    /// </summary>
    /// <summary>未开发房间现在能不能开拓：相邻、钱与材料都够。与 DevelopEmptyRoom 同一前置。</summary>
    public bool CanDevelopEmptyRoom(int roomId)
    {
        var room = Room(roomId);
        if (room == null || room.Open || room.X < 0 || room.Y < 0)
            return false;
        if (!State.Territory.NearOpenRoom(room))
            return false;
        if (State.Money < room.OpenCost)
            return false;
        return State.Territory.CanPayWith(State.Roster.Master, room.MaterialCost);
    }

    public bool DevelopEmptyRoom(int roomId)
    {
        var room = Room(roomId);
        if (room == null || room.Open || room.X < 0 || room.Y < 0)
            return false;
        if (!State.Territory.NearOpenRoom(room))
            return false;
        var money = State.Money;
        if (money < room.OpenCost)
            return false;
        if (!State.Territory.CanPayWith(State.Roster.Master, room.MaterialCost))
            return false;
        PassTime(CostDevelop * TerritoryClock.StepMinutes);
        if (!State.Territory.PayWith(State.Roster.Master, room.MaterialCost))
            return false;
        money -= room.OpenCost;
        room.Open = true;
        State.Territory.LinkNeighbors(room);
        State.Money = money;
        Write($"开拓了{room.Name}。");
        return true;
    }

    // ---- 开拓空地（空格子）----
    // 空地是「还没有房间实体的格子」。开出来是一间「空房」（Vacant），
    // 之后把建好的房间安装进去（PlaceRoom）。

    /// <summary>开拓定价：钱 1000 ＋ 木材 5 起步，每开一格按 1.2 倍递增（复利）。</summary>
    public const int VacantBaseMoney = 1000;
    public const int VacantBaseWood = 5;

    /// <summary>第 3 个起额外要石材，基准也是 5，同比例涨。</summary>
    public const int VacantBaseStone = 5;

    /// <summary>从第几个开始要石材（0 基：2 ＝ 第 3 个）。</summary>
    public const int VacantStoneFrom = 2;

    /// <summary>每级涨幅。</summary>
    public const double VacantCostGrowth = 1.2;

    public const string VacantCostItemId = "木材";
    public const string VacantCostStoneItemId = "石材";

    /// <summary>
    /// 已经开拓过几格空地。定价按它指数递增——**每开一格贵 20%**。
    /// 存在 <see cref="Territory"/> 上，随存档走；跨区域也继续涨。
    /// </summary>
    private int VacantDevelopCount => State.Territory.VacantDevelopCount;

    private double VacantGrowth =>
        System.Math.Pow(VacantCostGrowth, System.Math.Max(0, VacantDevelopCount));

    private static int VacantScaled(int baseAmount, double growth) =>
        System.Math.Max(1, (int)System.Math.Round(baseAmount * growth,
            System.MidpointRounding.AwayFromZero));

    /// <summary>下一格空地的钱（界面显示用）。</summary>
    public int VacantCostMoney => VacantScaled(VacantBaseMoney, VacantGrowth);

    /// <summary>下一格空地的木材。</summary>
    public int VacantCostWood => VacantScaled(VacantBaseWood, VacantGrowth);

    /// <summary>下一格空地的石材；还没到第 3 个就是 0。</summary>
    public int VacantCostStone => VacantDevelopCount < VacantStoneFrom
        ? 0
        : VacantScaled(VacantBaseStone, VacantGrowth);

    /// <summary>下一格空地要的材料（木材，第 3 个起加石材）。</summary>
    public List<RecipeCost> VacantCostMaterial()
    {
        var list = new List<RecipeCost> { new(VacantCostItemId, VacantCostWood) };
        if (VacantCostStone > 0)
            list.Add(new RecipeCost(VacantCostStoneItemId, VacantCostStone));
        return list;
    }

    /// <summary>这个空格子能不能开拓：得是空格、挨着**同一区域**已开发的地方、钱和料都够。</summary>
    public bool CanDevelopVacantCell(int regionId, int x, int y)
    {
        if (State.Territory.RoomAt(regionId, x, y) != null)
            return false;
        if (!State.Territory.NearOpenAt(regionId, x, y))
            return false;
        if (State.Money < VacantCostMoney)
            return false;
        return State.Territory.CanPayWith(State.Roster.Master, VacantCostMaterial());
    }

    /// <summary>
    /// 开发：把「挨着已开发地方」的空格子开拓成一间空房。
    /// 花按 1.2 倍递增的钱、木材（第 3 个起加石材），开出来是一间毛坯，
    /// 之后把建好的房间安装进去（见 <see cref="PlaceRoom"/>）。
    /// 开完顺手判一次「这块铺满了没有」——满了就解锁下一档区域。
    /// </summary>
    public bool DevelopVacantCell(int regionId, int x, int y)
    {
        if (!CanDevelopVacantCell(regionId, x, y))
            return false;
        var moneyCost = VacantCostMoney;
        var material = VacantCostMaterial();
        PassTime(CostDevelop * TerritoryClock.StepMinutes);
        if (!State.Territory.PayWith(State.Roster.Master, material))
            return false;
        var id = 0;
        foreach (var room in State.Territory.Rooms)
            id = System.Math.Max(id, room.Id);
        var added = new Room
        {
            Id = id + 1, Name = "空房", RegionId = regionId,
            X = x, Y = y, Open = true, Vacant = true,
        };
        added.EnsureDefaultTag();
        if (!State.Territory.AddRoom(added))
            return false;
        State.Territory.LinkNeighbors(added);
        State.Money -= moneyCost;
        State.Territory.VacantDevelopCount++;
        Write("开拓了一间空房。");

        // 铺满 → 解锁下一档。
        var opened = State.Territory.TryUnlockByFill();
        if (opened.Count > 0)
        {
            var names = new List<string>();
            foreach (var r in opened)
                names.Add(Territory.RegionName(r));
            Write($"这里铺满了，{string.Join("、", names)}跟着开出来了。");
        }
        return true;
    }

    // ---- 过界：走到边缘连接点，从那儿去隔壁区域 ----

    private static readonly Territory.RegionDir[] GateDirs =
    {
        Territory.RegionDir.North, Territory.RegionDir.East,
        Territory.RegionDir.South, Territory.RegionDir.West,
    };

    /// <summary>这间房是朝哪个方向的连接点；不是连接点返回 null。</summary>
    private static Territory.RegionDir? GateDirOf(Room room)
    {
        foreach (var dir in GateDirs)
        {
            var (gx, gy) = Territory.RegionGate(dir);
            if (room.X == gx && room.Y == gy)
                return dir;
        }
        return null;
    }

    /// <summary>某区域朝某方向的连接点上的房间；没房就是 null。</summary>
    public Room? GateRoom(int regionId, Territory.RegionDir dir)
    {
        var (gx, gy) = Territory.RegionGate(dir);
        return State.Territory.RoomAt(regionId, gx, gy);
    }

    /// <summary>
    /// 人站在这间房里能不能过界，能的话去的是哪块区域；不能返回 -1。
    /// 条件：这间房在本区域的连接点上，对面区域已解锁，
    /// 且**对面那块地图的对应连接点上也有房**——两边各有房才通。
    /// </summary>
    public int CrossTargetRegion(int roomId)
    {
        var room = Room(roomId);
        if (room == null || room.X < 0 || room.Y < 0)
            return -1;
        if (room.RegionId >= Territory.MaxTerritoryRegions)
            return PoiCrossLink(room)?.RegionId ?? -1;
        var dir = GateDirOf(room);
        if (dir == null)
            return -1;
        var neighbor = Territory.RegionNeighbor(room.RegionId, dir.Value);
        if (neighbor < 0 || !State.Territory.IsRegionUnlocked(neighbor))
            return -1;
        var other = GateRoom(neighbor, Territory.Opposite(dir.Value));
        if (other == null || !other.Open)
            return -1;
        return neighbor;
    }

    /// <summary>
    /// 人站在这间房里过界是朝哪个方向出去；不能过界返回 null。
    /// 领地按连接点所在的边；兴趣点 / 地城按边界通道两端的块内坐标（两块相邻，通道两端分处相对的两条边）。
    /// </summary>
    public Territory.RegionDir? CrossDir(int roomId)
    {
        if (CrossTargetRegion(roomId) < 0)
            return null;
        var room = Room(roomId)!;
        if (room.RegionId < Territory.MaxTerritoryRegions)
            return GateDirOf(room);
        var across = PoiCrossLink(room)!;
        var edge = Territory.RegionSize - 1;
        if (room.Y == across.Y && room.X == edge && across.X == 0)
            return Territory.RegionDir.East;
        if (room.Y == across.Y && room.X == 0 && across.X == edge)
            return Territory.RegionDir.West;
        if (room.X == across.X && room.Y == edge && across.Y == 0)
            return Territory.RegionDir.South;
        if (room.X == across.X && room.Y == 0 && across.Y == edge)
            return Territory.RegionDir.North;
        throw new System.InvalidOperationException($"边界通道 {room.Id}→{across.Id} 两端不在相对的两条边上。");
    }

    /// <summary>兴趣点里这间房通往别的块的那间房（生成器的边界通道）；没有返回 null。</summary>
    private Room? PoiCrossLink(Room room)
    {
        foreach (var id in room.Links)
        {
            var other = State.Territory.Rooms.Find(r => r.Id == id);
            if (other != null && other.Open && other.RegionId != room.RegionId)
                return other;
        }
        return null;
    }

    /// <summary>过界：把人送到对面区域的连接点房。</summary>
    public bool CrossTo(int regionId)
    {
        var here = Room(PlayerRoomId);
        if (here == null || PendingEncounter != null || CrossTargetRegion(here.Id) != regionId)
            return false;
        if (here.RegionId >= Territory.MaxTerritoryRegions)
        {
            // 兴趣点的块与块之间不按领地的四正连接点，而是生成器打通的边界通道：直接走过去。
            var across = PoiCrossLink(here)!;
            PassTime(CostMove * TerritoryClock.StepMinutes);
            Enter(across.Id);
            WriteArrival(across.Id);
            CheckDungeonRoom(here.Id, across.Id);
            return true;
        }
        var dir = GateDirOf(here);
        if (dir == null)
            return false;
        var other = GateRoom(regionId, Territory.Opposite(dir.Value));
        if (other == null)
            return false;
        PassTime(CostMove * TerritoryClock.StepMinutes);
        Enter(other.Id);
        Write($"你去了{Territory.RegionName(regionId)}。");
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

    /// <summary>建造：开 / 封某间房某个朝向上的门（只对同区网格四邻、两边都已开放的房间）。</summary>
    public bool SetDoor(int roomId, RoomDir dir, bool open)
    {
        var room = Room(roomId);
        if (room == null || !room.Open)
            return false;
        var other = State.Territory.NeighborAt(room, dir);
        if (other == null || !other.Open)
            return false;
        return SetLink(roomId, other.Id, open);
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
        if (DefDatabase<RoomDef>.GetById(defId) is not RoomDef def || !def.Buildable)
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
            Illustration = def.Illustration,
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
        if (DefDatabase<FacilityDef>.GetById(defId) is not FacilityDef def || !def.Buildable)
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

    /// <summary>
    /// 建一间房间并直接安装进指定空房。
    /// 所有前置条件都在扣料之前验完——否则建成了却装不进去，材料就白扣了。
    /// </summary>
    public bool BuildRoomDef(int defId, int vacantRoomId)
    {
        var vacant = Room(vacantRoomId);
        if (vacant == null || !vacant.Vacant || vacant.X < 0 || vacant.Y < 0)
            return false;
        if (DefDatabase<RoomDef>.GetById(defId) is not RoomDef def || !def.Buildable)
            return false;
        if (!BuildRoomDef(defId))
            return false;
        return PlaceRoom(State.Territory.Rooms[^1].Id, vacantRoomId);
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

    /// <summary>
    /// 开发：把已建好的房间安装进一间空房。空房本体被顶替掉，格子从此归新房间。
    /// 房间只能装在空房里——想往别处放，得先开拓出空房。
    /// </summary>
    public bool PlaceRoom(int roomId, int vacantRoomId)
    {
        var room = Room(roomId);
        var vacant = Room(vacantRoomId);
        if (room == null || room.X >= 0)
            return false;
        if (vacant == null || !vacant.Vacant || vacant.X < 0 || vacant.Y < 0)
            return false;
        var x = vacant.X;
        var y = vacant.Y;
        State.Territory.Rooms.Remove(vacant);
        if (!State.Territory.PlaceRoom(x, y, room))
        {
            State.Territory.Rooms.Add(vacant); // 放不回去就还原，别把空房弄丢
            return false;
        }
        // 门跟着格子走：空房开着的门原样过给新房。
        foreach (var link in new List<int>(vacant.Links))
        {
            State.Territory.Unlink(vacant.Id, link);
            State.Territory.Link(room.Id, link);
        }
        Write($"把{room.Name}装进了空房。");
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
