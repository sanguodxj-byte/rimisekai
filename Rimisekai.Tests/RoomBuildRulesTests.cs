using System.Linq;
using System.Text.Json;
using Rimisekai.Defs;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Ink;
using Rimisekai.Save;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 建房规则：每类房间建成白送一件对口设施（占设施位）；家具只能摆室内、田地圈舍资源点只能建室外；
/// 对口的房间里干对口的活快 20%；铁砧、缝纫台的料价。
/// </summary>
public sealed class RoomBuildRulesTests
{
    private const int Courtyard = 1, Parlor = 2, Bedroom = 3;
    private const int BedDef = 5, PigstyDef = 1068, AnvilDef = 1013, SewingDef = 1016;
    private const int SmithyDef = 112;

    /// <summary>开拓客厅西边的空格（1,1），返回空房。</summary>
    private static Room Vacant(HubSession hub, GameState state, int x = 1, int y = 1)
    {
        state.Money += 5000;
        state.Roster.Master!.Bag.Add("木材", 80);
        state.Roster.Master!.Bag.Add("石材", 80);
        Assert.True(hub.DevelopVacantCell(0, x, y));
        return state.Territory.RoomAt(0, x, y)!;
    }

    [Fact]
    public void Every_room_type_bundles_a_facility_that_fits_it()
    {
        // 只看内容表（别的测试会往全局 DefDatabase 里注册临时房型）。
        ContentDefs.EnsureInitialized();
        foreach (var room in ContentDefs.BuildingRooms.Concat(ContentDefs.AreaRooms).Where(r => r.Buildable))
        {
            Assert.False(string.IsNullOrEmpty(room.BundledFacility), $"{room.Name} 没配送的设施");
            var bundled = ContentDefs.BuildingFacilities.Single(f => f.DefName == room.BundledFacility);
            Assert.True(Territory.Fits(room.ToRuntime(), bundled.RoomTag),
                $"{room.Name} 送的 {bundled.Name} 要 {bundled.RoomTag}，房间标签 {string.Join("/", room.Tags)}");
        }
    }

    [Fact]
    public void Starting_area_facilities_sit_in_rooms_that_fit_them()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 1);
        foreach (var f in state.Territory.Facilities)
            Assert.True(Territory.Fits(state.Territory.Room(f.RoomId)!, f.RoomTag), $"{f.Name} 在 {state.Territory.Room(f.RoomId)!.Name}");
        // 躺椅是家具：开局摆在卧室，不在庭院。
        Assert.Equal(Bedroom, state.Territory.Facilities.Single(f => f.Name == "躺椅").RoomId);
    }

    [Fact]
    public void Furniture_is_indoor_only_and_pens_are_outdoor_only()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 2);
        var master = state.Roster.Master!;
        master.Bag.Add("木材", 50);
        master.Bag.Add("石材", 50);
        var wood = master.Bag.Get("木材");

        Assert.False(hub.BuildFacilityDef(BedDef, Courtyard), "床不能打在庭院");
        Assert.False(hub.BuildFacilityDef(PigstyDef, Bedroom), "猪圈不能建在卧室");
        Assert.Equal(wood, master.Bag.Get("木材"));          // 不合规矩不扣料
        Assert.True(hub.BuildFacilityDef(BedDef, Bedroom));
        Assert.True(hub.BuildFacilityDef(PigstyDef, Courtyard));

        // 未放置的家具也只能放进室内。
        Assert.True(hub.BuildFacilityDef(BedDef));
        var loose = state.Territory.Facilities[^1];
        Assert.False(hub.PlaceFacility(loose.Id, Courtyard));
        Assert.True(hub.PlaceFacility(loose.Id, Bedroom));
    }

    [Fact]
    public void Build_menu_greys_out_misfits_with_the_reason()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 3);
        state.Roster.Master!.Bag.Add("木材", 50);
        var vm = new InkViewModel(hub);
        // 在网格里点中庭院那一格（格子下标按网格列表顺序）。
        InkDevModel? model = null;
        for (var i = 0; i < 25 && model?.RoomId != Courtyard; i++)
        {
            var q = new InkPageQuery(i, -1, -1, "", false, 0, 0, false, -1, -1);
            model = InkPageBuilder.Build(vm, InkPage.Develop, in q).Dev!;
        }
        Assert.Equal(Courtyard, model!.RoomId);
        var bed = model.ActionRows.Single(r => r.Action == InkAction.DevBuildFacility && r.Index == BedDef);
        Assert.False(bed.Enabled);
        Assert.Equal("只能摆在室内", bed.Note);
        var pigsty = model.ActionRows.Single(r => r.Action == InkAction.DevBuildFacility && r.Index == PigstyDef);
        Assert.Equal("", pigsty.Note);
    }

    [Fact]
    public void A_new_room_comes_with_its_facility_which_takes_a_slot()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 4);
        var vacant = Vacant(hub, state);
        var stone = state.Roster.Master!.Bag.Get("石材") + state.Territory.Facilities.Sum(f => f.Contents.Get("石材"));
        Assert.True(hub.BuildRoomDef(SmithyDef, vacant.Id));
        var smithy = state.Territory.RoomAt(0, 1, 1)!;
        Assert.Equal("铁匠铺", smithy.Name);
        var anvil = Assert.Single(state.Territory.Facilities, f => f.RoomId == smithy.Id);
        Assert.Equal("铁砧", anvil.Name);
        Assert.True(anvil.Supports(ActionKind.Forge));
        // 送的铁砧不扣料（只扣了建房的 10 石材）
        Assert.Equal(stone - 10, state.Roster.Master!.Bag.Get("石材") + state.Territory.Facilities.Sum(f => f.Contents.Get("石材")));
        // 占一个位：再建三件就满。
        state.Roster.Master!.Bag.Add("木材", 100);
        for (var i = 0; i < Room.MaxFacilities - 1; i++)
            Assert.True(hub.BuildFacilityDef(BedDef, smithy.Id));
        Assert.False(hub.BuildFacilityDef(BedDef, smithy.Id));
    }

    [Fact]
    public void Matching_work_in_a_matching_room_is_twenty_percent_faster()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 5);
        var vacant = Vacant(hub, state);
        Assert.True(hub.BuildRoomDef(SmithyDef, vacant.Id));
        var smithy = state.Territory.RoomAt(0, 1, 1)!;
        Assert.Equal(120, state.Territory.RoomWorkPercent(smithy.Id, ActionKind.Forge));
        Assert.Equal(100, state.Territory.RoomWorkPercent(smithy.Id, ActionKind.Sew));
        Assert.Equal(100, state.Territory.RoomWorkPercent(Courtyard, ActionKind.Forge));

        // 同一座铁砧样式：铁匠铺里那座比庭院里那座打一回少花时间。
        state.Roster.Master!.Bag.Add("铁矿", 40);
        var yardAnvil = state.Territory.Facilities[^1];
        Assert.True(hub.BuildFacilityDef(AnvilDef, Courtyard));
        yardAnvil = state.Territory.Facilities[^1];
        var smithyAnvil = state.Territory.Facilities.Single(f => f.RoomId == smithy.Id);
        int Minutes(int roomId, int facilityId)
        {
            hub.Enter(roomId);
            Assert.True(hub.Use(facilityId));
            var before = state.Clock.TotalMinutes;
            Assert.True(hub.ActAtFixture(ActionKind.Forge));
            return (int)(state.Clock.TotalMinutes - before);
        }
        var plain = Minutes(Courtyard, yardAnvil.Id);
        var fast = Minutes(smithy.Id, smithyAnvil.Id);
        Assert.True(fast < plain, $"铁匠铺 {fast} 分钟，庭院 {plain} 分钟");
    }

    [Fact]
    public void Anvil_and_sewing_bench_costs()
    {
        ContentDefs.EnsureInitialized();
        TerritoryLoopTests.NewGame(out _, 1);
        var anvil = DefDatabase<FacilityDef>.GetById(AnvilDef)!;
        Assert.Equal(new[] { ("石材", 30), ("铁矿", 20) }, anvil.MaterialCost.Select(c => (c.ItemId, c.Count)));
        var sewing = DefDatabase<FacilityDef>.GetById(SewingDef)!;
        Assert.Equal(new[] { ("木材", 30) }, sewing.MaterialCost.Select(c => (c.ItemId, c.Count)));
    }

    [Fact]
    public void Room_bonus_and_facility_tag_survive_save_and_load()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 6);
        var vacant = Vacant(hub, state);
        Assert.True(hub.BuildRoomDef(SmithyDef, vacant.Id));
        var data = JsonSerializer.Deserialize<SaveData>(SaveSystem.Save(state, hub))!;
        var loaded = SaveSystem.Restore(data);
        var smithy = loaded.Territory.RoomAt(0, 1, 1)!;
        Assert.Equal(120, loaded.Territory.RoomWorkPercent(smithy.Id, ActionKind.Forge));
        Assert.Equal("室内", loaded.Territory.Facilities.Single(f => f.Name == "床").RoomTag);
    }
}
