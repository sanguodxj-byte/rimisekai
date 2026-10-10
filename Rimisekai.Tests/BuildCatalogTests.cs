using System.Linq;
using Rimisekai.Defs;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Save;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 建造目录（BuildOptions）：一项不藏、各带条件与状态；开拓并建房一步到位、失败全退；刚建的那一笔可撤、全额退回。
/// </summary>
public sealed class BuildCatalogTests
{
    private const int Courtyard = 1, Parlor = 2, Bedroom = 3;
    private const int BedDef = 5, PigstyDef = 1068, GroceryDef = 146;

    private static (int X, int Y) Cell(GameState state, int roomId)
    {
        var room = state.Territory.Room(roomId)!;
        return (room.X, room.Y);
    }

    private static BuildOption Option(HubSession hub, GameState state, int roomId, int defId)
    {
        var (x, y) = Cell(state, roomId);
        return hub.BuildOptions(0, x, y).Single(o => o.DefId == defId);
    }

    [Fact]
    public void Every_buildable_def_has_a_category_of_its_own_target()
    {
        ContentDefs.EnsureInitialized();
        var facilityCats = HubSession.BuildCategories(BuildTarget.Facility).Select(c => c.DefName).ToHashSet();
        var roomCats = HubSession.BuildCategories(BuildTarget.Room).Select(c => c.DefName).ToHashSet();
        Assert.Equal(8, facilityCats.Count);
        Assert.Equal(6, roomCats.Count);
        foreach (var f in ContentDefs.BuildingFacilities.Where(f => f.Buildable))
            Assert.Contains(f.BuildCategory, facilityCats);
        foreach (var r in ContentDefs.BuildingRooms.Concat(ContentDefs.AreaRooms).Where(r => r.Buildable))
            Assert.Contains(r.BuildCategory, roomCats);
    }

    [Fact]
    public void Category_labels_are_two_characters_and_unique_per_target()
    {
        ContentDefs.EnsureInitialized();
        foreach (var target in new[] { BuildTarget.Facility, BuildTarget.Room })
        {
            var labels = HubSession.BuildCategories(target).Select(c => c.Label).ToList();
            Assert.All(labels, l => Assert.Equal(2, l.Length));
            Assert.Equal(labels.Count, labels.Distinct().Count());
        }
    }

    [Fact]
    public void Every_station_that_takes_materials_is_under_production()
    {
        ContentDefs.EnsureInitialized();
        var stations = DefDatabase<RecipeDef>.All.Select(r => r.Station).ToHashSet();
        var crafting = ContentDefs.BuildingFacilities.Where(f => f.Buildable && f.Actions.Any(stations.Contains)).ToList();
        Assert.NotEmpty(crafting);
        Assert.All(crafting, f => Assert.Equal("Build_Production", f.BuildCategory));
    }

    [Fact]
    public void A_room_lists_every_facility_with_its_blocking_condition()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 11);
        state.Roster.Master!.Bag.Add("木材", 100);
        var (x, y) = Cell(state, Bedroom);
        var options = hub.BuildOptions(0, x, y);
        Assert.Equal(DefDatabase<FacilityDef>.All.Count(d => d.Buildable), options.Count);

        var bed = options.Single(o => o.DefId == BedDef);
        Assert.Equal(BuildState.Ready, bed.State);
        var pigsty = options.Single(o => o.DefId == PigstyDef);
        Assert.Equal(BuildState.Locked, pigsty.State);
        Assert.Contains(pigsty.Conditions, c => c.Check == BuildCheck.Tag && c.Label == Territory.OutdoorTag && !c.Met);

        // 排序：能建 → 缺料 → 挡住。
        var states = options.Select(o => o.State).ToList();
        Assert.Equal(states.OrderBy(s => s).ToList(), states);
    }

    [Fact]
    public void Missing_material_is_short_with_need_and_have()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 12);
        var master = state.Roster.Master!;
        master.Bag.Add("木材", -master.Bag.Get("木材"));
        foreach (var s in state.Territory.Storages)
            s.Contents.Add("木材", -s.Contents.Get("木材"));
        master.Bag.Add("木材", 3);
        var bed = Option(hub, state, Bedroom, BedDef);
        Assert.Equal(BuildState.Short, bed.State);
        var wood = bed.Conditions.Single(c => c.Check == BuildCheck.Material && c.Label == "木材");
        Assert.False(wood.Met);
        Assert.Equal(3, wood.Have);
        Assert.Equal($"3/{wood.Need}", wood.Value);
        var (x, y) = Cell(state, Bedroom);
        Assert.False(hub.BuildAt(0, x, y, BuildTarget.Facility, BedDef));
        Assert.Equal(3, master.Bag.Get("木材"));
    }

    [Fact]
    public void A_full_room_locks_every_facility_on_the_slot()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 13);
        state.Roster.Master!.Bag.Add("木材", 200);
        while (state.Territory.HasFacilitySlot(Bedroom))
            Assert.True(hub.BuildFacilityDef(BedDef, Bedroom));
        var (x, y) = Cell(state, Bedroom);
        foreach (var option in hub.BuildOptions(0, x, y))
        {
            Assert.Equal(BuildState.Locked, option.State);
            var slot = option.Conditions.Single(c => c.Check == BuildCheck.Slot);
            Assert.False(slot.Met);
            Assert.Equal($"{Room.MaxFacilities}/{Room.MaxFacilities}", slot.Value);
        }
    }

    [Fact]
    public void A_plot_lists_rooms_with_the_opening_cost_added()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 14);
        state.Money = 0;
        Assert.Equal(BuildSite.Plot, hub.SiteAt(0, 1, 1));
        var grocery = hub.BuildOptions(0, 1, 1).Single(o => o.DefId == GroceryDef);
        Assert.Equal(BuildState.Short, grocery.State);
        var money = grocery.Conditions.Single(c => c.Check == BuildCheck.Money);
        Assert.Equal(hub.VacantCostMoney, money.Need);
        Assert.False(money.Met);
        var def = DefDatabase<RoomDef>.GetById(GroceryDef)!;
        var wood = grocery.Conditions.Single(c => c.Check == BuildCheck.Material && c.Label == "木材");
        Assert.Equal(hub.VacantCostWood + def.MaterialCost.Where(c => c.ItemId == "木材").Sum(c => c.Count), wood.Need);
        Assert.Equal("摊位", grocery.Bundled);
    }

    [Fact]
    public void Territory_level_gates_a_room_and_charges_nothing()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 15);
        var gated = new RoomDef
        {
            Id = 99015, Name = "测试高阶房", Buildable = true, MinTerritoryLevel = state.Territory.Level + 1,
            BundledFacility = "床", BuildCategory = "BuildRoom_Living", Tags = { Territory.IndoorTag },
        };
        DefDatabase<RoomDef>.Register(gated);
        state.Money = 100000;
        state.Roster.Master!.Bag.Add("木材", 100);
        state.Roster.Master!.Bag.Add("石材", 100);
        var option = hub.BuildOptions(0, 1, 1).Single(o => o.DefId == gated.Id);
        Assert.Equal(BuildState.Locked, option.State);
        Assert.Contains(option.Conditions, c => c.Check == BuildCheck.Level && !c.Met && c.Need == state.Territory.Level + 1);
        var money = state.Money;
        Assert.False(hub.BuildAt(0, 1, 1, BuildTarget.Room, gated.Id));
        Assert.Equal(money, state.Money);
        Assert.Null(state.Territory.RoomAt(0, 1, 1));
    }

    [Fact]
    public void Plot_and_room_in_one_step_or_nothing_at_all()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 16);
        var master = state.Roster.Master!;
        state.Money = 100000;
        master.Bag.Add("石材", 100);
        // 木材只够开拓、不够再建杂货铺：一样都不扣。
        foreach (var s in state.Territory.Storages)
            s.Contents.Add("木材", -s.Contents.Get("木材"));
        master.Bag.Add("木材", hub.VacantCostWood - master.Bag.Get("木材"));
        var count = state.Territory.VacantDevelopCount;
        Assert.False(hub.DevelopAndBuild(0, 1, 1, GroceryDef));
        Assert.Equal(100000, state.Money);
        Assert.Equal(hub.VacantCostWood, master.Bag.Get("木材"));
        Assert.Null(state.Territory.RoomAt(0, 1, 1));

        master.Bag.Add("木材", 100);
        Assert.True(hub.DevelopAndBuild(0, 1, 1, GroceryDef));
        var shop = state.Territory.RoomAt(0, 1, 1)!;
        Assert.Equal("杂货铺", shop.Name);
        Assert.False(shop.Vacant);
        Assert.Contains(state.Territory.Facilities, f => f.RoomId == shop.Id && f.Name == "摊位");
        Assert.Equal(count + 1, state.Territory.VacantDevelopCount);
    }

    [Fact]
    public void A_failure_halfway_through_rolls_the_plot_back()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 17);
        var master = state.Roster.Master!;
        state.Money = 100000;
        master.Bag.Add("木材", 100);
        master.Bag.Add("石材", 100);
        // 房数只差一间到顶：开拓出的空房占掉最后一个名额，建房那一步加不进去——得把空房也撤掉。
        var id = 50000;
        while (state.Territory.Rooms.Count(r => r.RegionId < Territory.MaxTerritoryRegions) < Territory.MaxRooms - 1)
            Assert.True(state.Territory.AddRoom(new Room { Id = id++, Name = "占位", RegionId = 0, X = -1, Y = -1 }));
        var wood = master.Bag.Get("木材");
        var count = state.Territory.VacantDevelopCount;
        Assert.False(hub.DevelopAndBuild(0, 1, 1, GroceryDef));
        Assert.Equal(100000, state.Money);
        Assert.Equal(wood, master.Bag.Get("木材"));
        Assert.Equal(count, state.Territory.VacantDevelopCount);
        Assert.Null(state.Territory.RoomAt(0, 1, 1));
        Assert.False(hub.CanUndoBuild);
    }

    [Fact]
    public void Undo_refunds_a_facility_to_where_it_was_paid_from()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 18);
        var master = state.Roster.Master!;
        master.Bag.Add("木材", 1 - master.Bag.Get("木材"));
        var chest = state.Territory.Storages.First(s => s.Allows("木材", state.Territory.CategoryOf("木材")));
        chest.Contents.Add("木材", 50);
        var inChest = chest.Contents.Get("木材");
        var facilities = state.Territory.Facilities.Count;

        var (x, y) = Cell(state, Bedroom);
        Assert.True(hub.BuildAt(0, x, y, BuildTarget.Facility, BedDef));
        Assert.True(hub.CanUndoBuild);
        Assert.Equal("床", hub.LastBuildName);
        Assert.True(hub.UndoLastBuild());
        Assert.Equal(facilities, state.Territory.Facilities.Count);
        Assert.Equal(1, master.Bag.Get("木材"));
        Assert.Equal(inChest, chest.Contents.Get("木材"));
        Assert.False(hub.CanUndoBuild);
    }

    [Fact]
    public void Undo_reopens_the_plot_and_returns_the_money()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 19);
        state.Money = 100000;
        state.Roster.Master!.Bag.Add("木材", 100);
        state.Roster.Master!.Bag.Add("石材", 100);
        var wood = state.Territory.CountWith(state.Roster.Master, "木材");
        var count = state.Territory.VacantDevelopCount;
        var rooms = state.Territory.Rooms.Count;
        Assert.True(hub.BuildAt(0, 1, 1, BuildTarget.Room, GroceryDef));
        Assert.True(hub.UndoLastBuild());
        Assert.Equal(100000, state.Money);
        Assert.Equal(wood, state.Territory.CountWith(state.Roster.Master, "木材"));
        Assert.Equal(count, state.Territory.VacantDevelopCount);
        Assert.Equal(rooms, state.Territory.Rooms.Count);
        Assert.Equal(BuildSite.Plot, hub.SiteAt(0, 1, 1));
    }

    [Fact]
    public void Undo_puts_the_vacant_room_back_with_its_doors()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 20);
        state.Money = 100000;
        state.Roster.Master!.Bag.Add("木材", 100);
        state.Roster.Master!.Bag.Add("石材", 100);
        Assert.True(hub.DevelopVacantCell(0, 1, 1));
        var vacant = state.Territory.RoomAt(0, 1, 1)!;
        var links = vacant.Links.OrderBy(l => l).ToList();
        Assert.NotEmpty(links);
        Assert.True(hub.BuildAt(0, 1, 1, BuildTarget.Room, GroceryDef));
        Assert.True(hub.UndoLastBuild());
        var back = state.Territory.RoomAt(0, 1, 1)!;
        Assert.Same(vacant, back);
        Assert.True(back.Vacant);
        Assert.Equal(links, back.Links.OrderBy(l => l).ToList());
        foreach (var other in links)
            Assert.Contains(vacant.Id, state.Territory.Room(other)!.Links);
    }

    [Fact]
    public void Undo_expires_once_time_moves_on()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 21);
        state.Roster.Master!.Bag.Add("木材", 100);
        var (x, y) = Cell(state, Bedroom);
        Assert.True(hub.BuildAt(0, x, y, BuildTarget.Facility, BedDef));
        hub.PassTime(10);
        Assert.False(hub.CanUndoBuild);
        Assert.False(hub.UndoLastBuild());
    }
}
