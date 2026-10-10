using System.Linq;
using Rimisekai.Character;
using Rimisekai.Defs;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Save;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// RimWorld 式仓储设置：每件仓储一张允许物品过滤（按品类、按单件；全部允许 / 全部清除）＋一档优先级（低 / 普通 / 优先 / 重要 / 关键）。
/// 搬运的人把东西送进收得下它的、档最高的仓储，空手时把低档里的东西倒进高档；水井只收水；店里的摊位起手什么都不收。
/// </summary>
[Collection("Quest definition state")]
public sealed class StorageSettingsTests
{
    private const int Grocery = 146, MarketDef = 123;
    private const int Parlor = 2, Courtyard = 1, Chest = 8, Woodlot = 11, Well = 1;

    private static Room OpenGrocery(HubSession hub, GameState state)
    {
        state.Money += 5000;
        state.Roster.Master!.Bag.Add("木材", 40);
        Assert.True(hub.DevelopVacantCell(0, 2, 3));
        Assert.True(hub.BuildRoomDef(Grocery, state.Territory.RoomAt(0, 2, 3)!.Id));
        return state.Territory.RoomAt(0, 2, 3)!;
    }

    private static Facility Stall(GameState state, Room shop) =>
        state.Territory.Facilities.Single(f => f.RoomId == shop.Id && f.Supports(ActionKind.Trade));

    /// <summary>空手的女仆：不排班，有床睡、包里有干粮，醒着就闲着——闲时倒库。</summary>
    private static CharacterState IdleMaid(HubSession hub, GameState state)
    {
        TerritoryLoopTests.GiveMaidABed(hub, state);
        var maid = TerritoryLoopTests.Maid(state);
        maid.Bag.Add("干粮", 20);
        return maid;
    }

    [Fact]
    public void Every_commercial_room_brings_a_stall_that_stores_and_starts_with_nothing_allowed()
    {
        ContentDefs.EnsureInitialized();
        var commercial = ContentDefs.BuildingRooms.Where(r => r.Tags.Contains(Territory.CommercialTag)).ToList();
        Assert.Contains(commercial, r => r.Id == Grocery);
        Assert.Contains(commercial, r => r.Id == MarketDef);
        foreach (var room in commercial)
        {
            var def = DefDatabase<FacilityDef>.GetNamed(room.BundledFacility);
            Assert.True(def.Storage, $"{room.Label} 自带的 {def.Label} 能存东西");
            Assert.Empty(def.StorageFilter);
            var stall = def.ToRuntime();
            Assert.Empty(stall.StorageFilter.Rules);
            Assert.False(stall.Allows("兽皮", "RawMaterial"));
            Assert.False(stall.Allows("eqp_1", "Armor"));
        }
        // 建出来也是空的，搬运不往里送任何东西
        var hub = TerritoryLoopTests.NewGame(out var state, 3);
        var shop = OpenGrocery(hub, state);
        var built = Stall(state, shop);
        Assert.True(state.Territory.InStorageNetwork(built));
        foreach (var item in new[] { "木材", "兽皮", "肉", "陶罐", "水" })
            Assert.NotEqual(built.Id, state.Territory.FindStorageFor(item, shop.Id)?.Id ?? -1);
    }

    [Fact]
    public void Defaults_route_by_table_priority()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 3);
        var t = state.Territory;
        var chest = t.Facilities.Single(f => f.Id == Chest);
        Assert.Equal(StoragePriority.Normal, chest.Priority);
        Assert.Equal(FilterState.All, chest.StorageFilter.StateOf(ItemFilter.RootCategory));
        var table = t.Facilities.First(f => f.IsTable && f.RoomId == Parlor);
        Assert.Equal(StoragePriority.Important, table.Priority);
        Assert.Equal(table.Id, t.FindStorageFor("stew")!.Id);  // 熟食上桌
        Assert.Equal(chest.Id, t.FindStorageFor("木材", Courtyard)!.Id);
        var well = t.Facilities.Single(f => f.Id == Well);
        well.StorageCapacity = well.StoredCount() + 5;
        Assert.Equal(Well, t.FindStorageFor("水")!.Id);            // 关键档的井先收水
        var stove = DefDatabase<FacilityDef>.All.Single(d => d.Id == 1037).ToRuntime();   // 大灶
        stove.Id = 9999;
        stove.RoomId = Parlor;
        stove.Built = true;
        t.Facilities.Add(stove);
        Assert.True(stove.CanStore && t.IsWorkbench(stove));
        Assert.False(t.InStorageNetwork(stove), "灶台的存货是备料，不进仓储网");
        Assert.NotEqual(stove.Id, t.FindStorageFor("木材", Parlor)?.Id ?? -1);
    }

    [Fact]
    public void Haulers_go_to_the_highest_priority_storage_that_allows_the_item()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 3);
        var t = state.Territory;
        var chest = t.Facilities.Single(f => f.Id == Chest);
        var shop = OpenGrocery(hub, state);
        var stall = Stall(state, shop);
        stall.StorageFilter.Set("BuildingMaterial", true);
        Assert.Equal(StoragePriority.Preferred, stall.Priority);
        Assert.Equal(stall.Id, t.FindStorageFor("石材", Courtyard)!.Id);   // 优先 > 普通
        Assert.Equal(chest.Id, t.FindStorageFor("铁矿", Courtyard)!.Id);   // 摊位不收矿石
        stall.Priority = StoragePriority.Low;
        Assert.Equal(chest.Id, t.FindStorageFor("石材", Courtyard)!.Id);   // 低 < 普通
        chest.StorageFilter.Set("石材", false);
        Assert.Equal(stall.Id, t.FindStorageFor("石材", Courtyard)!.Id);   // 箱子不收了，退而求其次
        // 满了也不收
        stall.StorageCapacity = 1;
        stall.Contents.Add("石材", 1);
        Assert.NotEqual(stall.Id, t.FindStorageFor("石材", Courtyard)?.Id ?? -1);

        // 女仆伐木：开局木材送进箱子；摊位放行木材后新砍的直接送上摊
        stall.Contents.Clear();
        stall.StorageCapacity = 0;
        stall.Priority = StoragePriority.Preferred;
        var maid = TerritoryLoopTests.Maid(state);
        TerritoryLoopTests.GiveMaidABed(hub, state);
        maid.Bag.Add("干粮", 20);
        for (var slot = 1; slot < 3; slot++)
            Assert.True(hub.Assign(maid.Id, slot, SlotMode.Work, Woodlot));
        var chestWood = chest.Contents.Get("木材");
        hub.PassTime(24 * 60);
        Assert.Equal(0, stall.Contents.Get("木材"));
        Assert.True(chest.Contents.Get("木材") > chestWood, "没放行木材：砍的进箱子");
        TerritoryLoopTests.MoveTo(hub, shop.Id);
        Assert.True(hub.OpenStorage(stall.Id));
        Assert.True(hub.ToggleStorageFilter("木材"));
        hub.CloseStorage();
        hub.PassTime(24 * 60);
        Assert.True(stall.Contents.Get("木材") > 0, "放行木材后搬运的人自己往摊上补");
    }

    [Fact]
    public void Idle_haulers_move_goods_from_lower_to_higher_priority_storage()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 3);
        var t = state.Territory;
        var chest = t.Facilities.Single(f => f.Id == Chest);
        var shop = OpenGrocery(hub, state);
        var stall = Stall(state, shop);
        chest.Contents.Add("兽皮", 6);
        chest.Contents.Add("陶罐", 3);
        Assert.False(t.FindRehaul(Parlor) is { } early && early.Target.Id == stall.Id, "摊位什么都不收时不往上倒");
        stall.StorageFilter.Only(new[] { "兽皮", "陶罐" });
        var job = t.FindRehaul(Parlor)!.Value;
        Assert.Same(chest, job.Source);
        Assert.Same(stall, job.Target);

        IdleMaid(hub, state);
        hub.PassTime(24 * 60);
        Assert.Equal(6, stall.Contents.Get("兽皮"));
        Assert.Equal(3, stall.Contents.Get("陶罐"));
        Assert.Equal(0, chest.Contents.Get("兽皮"));

        // 取消放行：摊上的兽皮成了不该在这里的东西，倒回箱子；陶罐还留着卖
        stall.StorageFilter.Set("兽皮", false);
        hub.PassTime(24 * 60);
        Assert.Equal(0, stall.Contents.Get("兽皮"));
        Assert.Equal(6, chest.Contents.Get("兽皮"));
        Assert.Equal(3, stall.Contents.Get("陶罐"));
        // 同档之间不来回倒
        Assert.Null(t.FindRehaul(Parlor));
    }

    [Fact]
    public void Well_holds_water_only_and_is_never_drained_by_rehauling()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 3);
        var t = state.Territory;
        var well = t.Facilities.Single(f => f.Id == Well);
        var chest = t.Facilities.Single(f => f.Id == Chest);
        Assert.True(well.IsWell);
        Assert.Equal(StoragePriority.Critical, well.Priority);
        Assert.False(t.StorageConfigurable(well));
        well.StorageFilter.AllowAll();
        Assert.False(t.Allows(well, "木材"));
        Assert.True(well.Contents.Get("水") > 0);
        // 箱子虽全收（含水），井是关键档：水只会往井里倒，不会从井里倒出来
        Assert.True(t.Allows(chest, "水"));
        Assert.NotEqual(well.Id, t.FindRehaul(Courtyard)?.Source.Id ?? -1);
        // 玩家打开井：存储设置改不了
        TerritoryLoopTests.MoveTo(hub, well.RoomId);
        Assert.True(hub.OpenStorage(well.Id));
        Assert.False(hub.StorageConfigurable);
        Assert.False(hub.ToggleStorageFilter("木材"));
        Assert.False(hub.SetStoragePriority(StoragePriority.Low));
    }

    [Fact]
    public void Category_and_item_toggles_allow_all_and_clear_all()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 3);
        var chest = state.Territory.Facilities.Single(f => f.Id == Chest);
        TerritoryLoopTests.MoveTo(hub, chest.RoomId);
        Assert.True(hub.OpenStorage(chest.Id));
        Assert.True(hub.StorageConfigurable);
        Assert.True(hub.ClearStorageFilter());
        Assert.Equal(FilterState.None, hub.StorageEntryState("Food"));
        Assert.True(hub.ToggleStorageFilter("Food"));                 // 整类收
        Assert.Equal(FilterState.All, hub.StorageEntryState("RawFood"));
        Assert.True(hub.ToggleStorageFilter("肉"));                   // 单件拿掉
        Assert.Equal(FilterState.None, hub.StorageEntryState("肉"));
        Assert.Equal(FilterState.Some, hub.StorageEntryState("Food"));
        Assert.Equal(FilterState.Some, hub.StorageEntryState("RawFood"));
        Assert.Equal(FilterState.All, hub.StorageEntryState("Meal"));
        Assert.True(hub.ToggleStorageFilter("Food"));                 // 部分→整类收，单件规则清掉
        Assert.Equal(FilterState.All, hub.StorageEntryState("肉"));
        Assert.True(hub.ToggleStorageFilter("Armor"));                // 实例大类
        Assert.True(state.Territory.Allows(chest, "eqp_x") == false); // 认不出的实例不收
        Assert.True(chest.Allows("eqp_1", "Armor"));
        Assert.True(hub.AllowAllStorage());
        Assert.Equal(FilterState.All, hub.StorageEntryState(ItemFilter.RootCategory));
        Assert.True(hub.SetStoragePriority(StoragePriority.Important));
        Assert.Equal(StoragePriority.Important, chest.Priority);
        // 过滤树：顶层全是根下的品类，展开后有子品类与物品
        var rows = hub.StorageFilterRows(new System.Collections.Generic.HashSet<string>());
        Assert.All(rows, r => Assert.True(r.IsCategory && r.Depth == 0));
        var open = hub.StorageFilterRows(new System.Collections.Generic.HashSet<string> { "Food", "RawFood" });
        Assert.Contains(open, r => r.Entry == "RawFood" && r.Depth == 1 && r.Parent == "Food");
        Assert.Contains(open, r => r.Entry == "肉" && !r.IsCategory && r.Depth == 2);
    }

    [Fact]
    public void Filters_and_priorities_survive_save_and_load()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 3);
        var shop = OpenGrocery(hub, state);
        var stall = Stall(state, shop);
        var chest = state.Territory.Facilities.Single(f => f.Id == Chest);
        stall.StorageFilter.Only(new[] { "兽皮", "Armor" });
        stall.StorageFilter.Set("陶罐", true);
        stall.Priority = StoragePriority.Critical;
        chest.StorageFilter.Set("RawFood", false);
        chest.StorageFilter.Set("鱼", true);
        chest.Priority = StoragePriority.Low;
        var loaded = SaveSystem.Load(SaveSystem.Save(state, hub));
        var stall2 = loaded.Territory.Facilities.Single(f => f.Id == stall.Id);
        var chest2 = loaded.Territory.Facilities.Single(f => f.Id == Chest);
        Assert.Equal(stall.StorageFilter.Rules.OrderBy(p => p.Key), stall2.StorageFilter.Rules.OrderBy(p => p.Key));
        Assert.Equal(chest.StorageFilter.Rules.OrderBy(p => p.Key), chest2.StorageFilter.Rules.OrderBy(p => p.Key));
        Assert.Equal(StoragePriority.Critical, stall2.Priority);
        Assert.Equal(StoragePriority.Low, chest2.Priority);
        Assert.False(loaded.Territory.Allows(chest2, "肉"));
        Assert.True(loaded.Territory.Allows(chest2, "鱼"));
        Assert.True(loaded.Territory.Allows(chest2, "木材"));
    }

    [Fact]
    public void Selling_draws_from_the_stall_storage()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 3);
        var shop = OpenGrocery(hub, state);
        var stall = Stall(state, shop);
        stall.StorageFilter.Only(new[] { "陶罐" });
        stall.Contents.Add("陶罐", 5);
        var rng = new System.Random(3);
        var sold = 0;
        for (var i = 0; i < 20 && stall.Contents.Get("陶罐") > 0; i++)
            sold += Commerce.Sell(state.Territory, shop, TerritoryLoopTests.Maid(state), 999, rng)!.Value.Count;
        Assert.Equal(5 - sold, stall.Contents.Get("陶罐"));
        Assert.True(sold > 0);
    }
}
