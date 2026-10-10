using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Defs;
using Rimisekai.Housing;

namespace Rimisekai.Hub;

/// <summary>网格上一格能建什么：设施（已建的房）、房间（空房），或开拓并建房（挨着已开发地方的空地）。</summary>
public enum BuildSite
{
    None,
    Plot,
    Vacant,
    Room,
}

/// <summary>一项建造此刻的状态：能建、只差钱料、被房间标签/设施位/领地等级挡住。排序即此先后。</summary>
public enum BuildState
{
    Ready,
    Short,
    Locked,
}

/// <summary>建造条件的种类。</summary>
public enum BuildCheck
{
    Tag,
    Slot,
    Level,
    Money,
    Material,
}

/// <summary>一条建造条件：要多少、有多少、满足没有。标签类条件只看 Met，Need/Have 不用。</summary>
public readonly record struct BuildCondition(BuildCheck Check, string Label, long Need, long Have, bool Met)
{
    /// <summary>数值栏：「现有/需要」；设施位是「已摆/上限」；标签条件没有数值。</summary>
    public string Value => Check == BuildCheck.Tag ? "" : $"{Have}/{Need}";
}

/// <summary>建造目录里的一项（一格里的一个格子）。</summary>
public sealed record BuildOption(
    BuildTarget Target,
    int DefId,
    string Name,
    string Category,
    IReadOnlyList<string> Tags,
    string Bundled,
    IReadOnlyList<BuildCondition> Conditions,
    BuildState State);

/// <summary>一笔建造的收据：撤销时照它原样退钱退料、拆掉建出来的东西。</summary>
internal sealed class BuildReceipt
{
    public string Name = "";
    public long Money;
    public readonly List<PaidItem> Paid = new();
    public readonly List<int> NewFacilities = new();
    public int NewRoom = -1;
    public int DevelopedRoom = -1;
    public Room? ReplacedVacant;
    public readonly List<int> VacantLinks = new();

    /// <summary>这笔开出了新区域：区域不回收，所以撤不了。</summary>
    public bool Final;

    /// <summary>建成那一刻（总分钟）。之后时间一动就不能撤。</summary>
    public int At;
}

public sealed partial class HubSession
{
    public const string SlotLabel = "设施位";
    public const string LevelLabel = "领地等级";
    public const string MoneyLabel = "钱";
    public const string AllCategoryLabel = "全部";
    public const string BundledLabel = "自带";

    private BuildReceipt? _recording;
    private BuildReceipt? _lastBuild;

    /// <summary>这一类的建造分类（表里先后）。</summary>
    public static IReadOnlyList<BuildCategoryDef> BuildCategories(BuildTarget target) =>
        DefDatabase<BuildCategoryDef>.All.Where(c => c.Target == target).ToList();

    /// <summary>网格上这一格的建造场地。</summary>
    public BuildSite SiteAt(int regionId, int x, int y)
    {
        var room = State.Territory.RoomAt(regionId, x, y);
        if (room == null)
            return State.Territory.NearOpenAt(regionId, x, y) ? BuildSite.Plot : BuildSite.None;
        if (!room.Open)
            return BuildSite.None;
        return room.Vacant ? BuildSite.Vacant : BuildSite.Room;
    }

    /// <summary>
    /// 这一格的全部建造项：一项不藏，各带条件与状态。
    /// 已建的房列设施；空房列房间；空地列房间且条件里算上开拓的钱料。
    /// 排序：能建 → 只差钱料 → 被挡住；同档按分类先后、表内先后。
    /// </summary>
    public IReadOnlyList<BuildOption> BuildOptions(int regionId, int x, int y)
    {
        var site = SiteAt(regionId, x, y);
        var options = new List<BuildOption>();
        if (site == BuildSite.Room)
        {
            var room = State.Territory.RoomAt(regionId, x, y)!;
            foreach (var def in DefDatabase<FacilityDef>.All.Where(d => d.Buildable))
                options.Add(FacilityOption(def, room));
        }
        else if (site != BuildSite.None)
        {
            foreach (var def in DefDatabase<RoomDef>.All.Where(d => d.Buildable))
                options.Add(RoomOption(def, site == BuildSite.Plot));
        }
        var order = DefDatabase<BuildCategoryDef>.All.Select(c => c.DefName).ToList();
        return options
            .Select((o, i) => (o, i))
            .OrderBy(p => p.o.State)
            .ThenBy(p => order.IndexOf(p.o.Category))
            .ThenBy(p => p.i)
            .Select(p => p.o)
            .ToList();
    }

    private BuildOption FacilityOption(FacilityDef def, Room room)
    {
        var conditions = new List<BuildCondition>();
        if (def.RoomTag.Length > 0)
            conditions.Add(new BuildCondition(BuildCheck.Tag, def.RoomTag, 0, 0, Territory.Fits(room, def.RoomTag)));
        var count = State.Territory.FacilityCount(room.Id);
        conditions.Add(new BuildCondition(BuildCheck.Slot, SlotLabel, Housing.Room.MaxFacilities, count,
            count < Housing.Room.MaxFacilities));
        AddMaterialConditions(conditions, def.MaterialCost);
        var tags = def.RoomTag.Length > 0 ? new List<string> { def.RoomTag } : new List<string>();
        return new BuildOption(BuildTarget.Facility, def.Id, def.Name, def.BuildCategory, tags, "", conditions,
            StateOf(conditions));
    }

    private BuildOption RoomOption(RoomDef def, bool plot)
    {
        var conditions = new List<BuildCondition>
        {
            new(BuildCheck.Level, LevelLabel, def.MinTerritoryLevel, State.Territory.Level,
                State.Territory.Level >= def.MinTerritoryLevel),
        };
        var costs = def.MaterialCost;
        if (plot)
        {
            conditions.Add(new BuildCondition(BuildCheck.Money, MoneyLabel, VacantCostMoney, State.Money,
                State.Money >= VacantCostMoney));
            costs = MergeCosts(VacantCostMaterial(), def.MaterialCost);
        }
        AddMaterialConditions(conditions, costs);
        var bundled = def.BundledFacility.Length > 0 ? DefDatabase<FacilityDef>.GetNamed(def.BundledFacility).Name : "";
        return new BuildOption(BuildTarget.Room, def.Id, def.Name, def.BuildCategory, def.Tags, bundled, conditions,
            StateOf(conditions));
    }

    private void AddMaterialConditions(List<BuildCondition> conditions, IReadOnlyList<RecipeCost> costs)
    {
        foreach (var cost in costs)
        {
            var have = State.Territory.CountWith(State.Roster.Master, cost.ItemId);
            conditions.Add(new BuildCondition(BuildCheck.Material, cost.ItemId, cost.Count, have, have >= cost.Count));
        }
    }

    private static BuildState StateOf(IReadOnlyList<BuildCondition> conditions)
    {
        if (conditions.Any(c => !c.Met && c.Check is BuildCheck.Tag or BuildCheck.Slot or BuildCheck.Level))
            return BuildState.Locked;
        return conditions.All(c => c.Met) ? BuildState.Ready : BuildState.Short;
    }

    /// <summary>两份材料单按物品合并（开拓的料＋房间的料一起算够不够）。</summary>
    public static List<RecipeCost> MergeCosts(IReadOnlyList<RecipeCost> a, IReadOnlyList<RecipeCost> b)
    {
        var merged = new List<RecipeCost>();
        foreach (var cost in a.Concat(b))
        {
            var i = merged.FindIndex(m => m.ItemId == cost.ItemId);
            if (i < 0)
                merged.Add(new RecipeCost(cost.ItemId, cost.Count));
            else
                merged[i] = new RecipeCost(cost.ItemId, merged[i].Count + cost.Count);
        }
        return merged;
    }

    /// <summary>在这一格建这一项（按场地分派：添设施 / 空房建房 / 开拓并建房）。</summary>
    public bool BuildAt(int regionId, int x, int y, BuildTarget target, int defId)
    {
        var site = SiteAt(regionId, x, y);
        var room = State.Territory.RoomAt(regionId, x, y);
        return (site, target) switch
        {
            (BuildSite.Room, BuildTarget.Facility) => BuildFacilityDef(defId, room!.Id),
            (BuildSite.Vacant, BuildTarget.Room) => BuildRoomDef(defId, room!.Id),
            (BuildSite.Plot, BuildTarget.Room) => DevelopAndBuild(regionId, x, y, defId),
            _ => false,
        };
    }

    /// <summary>
    /// 开拓空地并当场建房：开拓的钱料与房间的料合起来先验够不够，一步不成就全部退回，不留半截空房。
    /// </summary>
    public bool DevelopAndBuild(int regionId, int x, int y, int roomDefId)
    {
        if (DefDatabase<RoomDef>.GetById(roomDefId) is not RoomDef def || !def.Buildable)
            return false;
        if (State.Territory.Level < def.MinTerritoryLevel || !CanDevelopVacantCell(regionId, x, y))
            return false;
        if (!State.Territory.CanPayWith(State.Roster.Master, MergeCosts(VacantCostMaterial(), def.MaterialCost)))
            return false;
        return Receipted(() => DevelopVacantCell(regionId, x, y)
            && BuildRoomDef(roomDefId, State.Territory.RoomAt(regionId, x, y)!.Id));
    }

    /// <summary>刚建的那一笔还能不能撤：时间没再走、没开出新区域、你不在建出来的房里。</summary>
    public bool CanUndoBuild => _lastBuild is { } r && r.At == State.Clock.TotalMinutes
        && PlayerRoomId != r.NewRoom && PlayerRoomId != r.DevelopedRoom;

    /// <summary>能撤的那一笔建的是什么（房名或设施名）。</summary>
    public string LastBuildName => _lastBuild?.Name ?? "";

    /// <summary>撤销刚建的那一笔：拆掉建出来的房与设施、空房还原，钱料全额退回原处。时间不倒回。</summary>
    public bool UndoLastBuild()
    {
        if (!CanUndoBuild)
            return false;
        var receipt = _lastBuild!;
        _lastBuild = null;
        Revert(receipt);
        Write($"撤销了{receipt.Name}。");
        return true;
    }

    /// <summary>一笔建造全程记账；中途失败就按账还原，成了就留作可撤的那一笔。</summary>
    private bool Receipted(Func<bool> body)
    {
        if (_recording != null)
            return body();
        var receipt = new BuildReceipt();
        _recording = receipt;
        var ok = body();
        _recording = null;
        if (!ok)
        {
            Revert(receipt);
            return false;
        }
        receipt.At = State.Clock.TotalMinutes;
        _lastBuild = receipt.Final ? null : receipt;
        return true;
    }

    private void Revert(BuildReceipt receipt)
    {
        var territory = State.Territory;
        foreach (var id in receipt.NewFacilities)
        {
            var facility = territory.Facilities.Find(f => f.Id == id)!;
            if (UsingFixtureId == id)
                LeaveFixture();
            ResetFacilityWorkers(id);
            if (facility.RoomId >= 0)
                territory.RemoveEffect(facility);
            territory.Unassign(id);
            territory.Facilities.Remove(facility);
        }
        if (receipt.NewRoom >= 0 && territory.Room(receipt.NewRoom) is { } built)
            DropRoom(built);
        if (receipt.DevelopedRoom >= 0)
        {
            if (territory.Room(receipt.DevelopedRoom) is { } developed)
                DropRoom(developed);
            territory.VacantDevelopCount--;
        }
        else if (receipt.ReplacedVacant is { } vacant)
        {
            territory.Rooms.Add(vacant);
            foreach (var link in receipt.VacantLinks)
                territory.Link(vacant.Id, link);
        }
        var master = State.Roster.Master;
        foreach (var paid in receipt.Paid)
        {
            if (paid.FacilityId == PaidItem.FromBag)
                master!.Bag.Add(paid.ItemId, paid.Count);
            else
                territory.Facilities.Find(f => f.Id == paid.FacilityId)!.Contents.Add(paid.ItemId, paid.Count);
        }
        State.Money += receipt.Money;
    }

    /// <summary>把一间房从网格上撤掉：里面的人挪到邻房，门全拆。</summary>
    private void DropRoom(Room room)
    {
        RelocateRoom(room.Id);
        foreach (var link in new List<int>(room.Links))
            State.Territory.Unlink(room.Id, link);
        State.Territory.Rooms.Remove(room);
    }
}
