using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Rimisekai.Character;
using Rimisekai.Defs;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Save;
using Xunit;
using Xunit.Abstractions;

namespace Rimisekai.Tests;

/// <summary>
/// 领地经营闭环：采集 → 建造 → 排班干活 → 工坊加工 → 再建造，逻辑层按真实时间推进跑通。
/// 开局照标准种子装配（与 InkWorldBootstrap.Create 同序，只是不灌台词），随机数全部定种，结果可复现。
/// </summary>
public sealed class TerritoryLoopTests
{
    private const int Quarry = 1006;  // 采石点：Mine → 石材
    private const int Kiln = 1070;    // 陶器坊：Forge（拉坯 3 石材 → 陶罐；冶铁 2 铁矿 → 铁）
    private const int Chest = 8;      // 箱子
    private const int Forest = 4, Mountain = 5, Courtyard = 1, Parlor = 2;
    private const int Woodlot = 11, IronVein = 7;

    private readonly ITestOutputHelper _out;

    public TerritoryLoopTests(ITestOutputHelper output) => _out = output;

    internal static HubSession NewGame(out GameState state, int seed)
    {
        ContentDefs.EnsureInitialized();
        var seedDef = ContentDefs.StandardSeed;
        state = new GameState { Money = seedDef.Money };
        state.RegenerateWorld(seed);
        state.Territory.Name = ContentDefs.TerritoryName;
        var generator = new CharacterGenerator(new Random(seed));
        foreach (var entry in seedDef.Characters)
        {
            var character = state.Roster.Add(entry.Name, entry.Master);
            if (entry.Master)
                character.FactionId = GameState.PlayerFaction;
            if (entry.Favor != 0)
                character.Condition.AddFavor(entry.Favor);
            foreach (var name in entry.Traits)
                character.Grant(Enum.Parse<Trait>(name, ignoreCase: true));
            generator.Populate(character, entry.Traits);
        }
        foreach (var pair in seedDef.BagStock)
            state.Roster.Master!.Bag.Add(pair.Key, pair.Value);
        foreach (var def in ContentDefs.BuildingRooms)
            DefDatabase<RoomDef>.Register(def);
        foreach (var def in ContentDefs.BuildingFacilities)
            DefDatabase<FacilityDef>.Register(def);
        foreach (var def in ContentDefs.AreaRooms)
        {
            DefDatabase<RoomDef>.Register(def);
            state.Territory.AddRoom(def.ToRuntime());
        }
        foreach (var pair in ContentDefs.AreaLinks)
        {
            foreach (var to in pair.Value)
                state.Territory.Link(pair.Key, to);
        }
        foreach (var place in ContentDefs.AreaFacilities)
        {
            var facility = DefDatabase<FacilityDef>.GetNamed(place.FacilityDefName).ToRuntime();
            facility.Id = place.Id;
            facility.RoomId = place.RoomId;
            foreach (var pair in place.Contents)
                facility.Contents.Add(pair.Key, pair.Value);
            state.Territory.AddFacility(facility);
        }
        var hub = new HubSession(state);
        hub.Day.Rng = new Random(seed);
        hub.Enter(seedDef.StartRoomId);
        foreach (var entry in seedDef.Characters)
        {
            if (entry.RoomId >= 0)
                hub.Place(state.Roster.Find(entry.Name)!.Id, entry.RoomId);
        }
        return hub;
    }

    /// <summary>某物品在全领地（所有人背包 + 所有设施）的总数。</summary>
    private static int Total(GameState state, string itemId) =>
        state.Roster.Members.Sum(c => c.Bag.Get(itemId))
        + state.Territory.Facilities.Sum(f => f.Contents.Get(itemId));

    private static CharacterState Maid(GameState state) => state.Roster.Members.Single(c => !c.IsMaster);

    private static void AssertNoNegativeStock(GameState state)
    {
        foreach (var c in state.Roster.Members)
            Assert.All(c.Bag.Items, p => Assert.True(p.Value >= 0, $"{c.Name} 背包 {p.Key}={p.Value}"));
        foreach (var f in state.Territory.Facilities)
            Assert.All(f.Contents.Items, p => Assert.True(p.Value >= 0, $"{f.Name}#{f.Id} {p.Key}={p.Value}"));
        Assert.True(state.Money >= 0, $"金钱 {state.Money}");
    }

    private static Facility Build(HubSession hub, GameState state, int defId, int roomId)
    {
        Assert.True(hub.BuildFacilityDef(defId, roomId), $"建造 {defId} 进房间 {roomId}");
        var built = state.Territory.Facilities[^1];
        Assert.Equal(roomId, built.RoomId);
        return built;
    }

    private static void Days(HubSession hub, GameState state, int days)
    {
        var target = state.Clock.Day + days;
        while (state.Clock.Day < target)
            hub.PassTime(60);
    }

    // ---------- 排班点名哪件设施，就去哪件 ----------

    [Fact]
    public void Work_goes_to_the_assigned_facility_not_the_first_of_its_kind()
    {
        // 矿脉（铁矿）与采石点（石材）都是 Mine。排到采石点，就该出石材而不是跑去矿脉挖铁。
        var hub = NewGame(out var state, 1);
        var maid = Maid(state);
        maid.Bag.Add("干粮", 30);
        var quarry = Build(hub, state, Quarry, Mountain);
        Assert.True(hub.Assign(maid.Id, 1, SlotMode.Work, quarry.Id));
        Assert.True(hub.Assign(maid.Id, 2, SlotMode.Work, quarry.Id));
        var stone = Total(state, "石材");

        Days(hub, state, 2);

        Assert.True(Total(state, "石材") > stone, $"石材 {stone} → {Total(state, "石材")}");
        Assert.Equal(0, Total(state, "铁矿"));
    }

    [Fact]
    public void Same_task_in_two_slots_follows_each_slots_facility()
    {
        // 上午挖铁、下午采石：同是 Mine，换段时要换到这一段点名的设施上。
        var hub = NewGame(out var state, 1);
        var maid = Maid(state);
        maid.Bag.Add("干粮", 30);
        var quarry = Build(hub, state, Quarry, Mountain);
        Assert.True(hub.Assign(maid.Id, 1, SlotMode.Work, IronVein));
        Assert.True(hub.Assign(maid.Id, 2, SlotMode.Work, quarry.Id));
        var stone = Total(state, "石材");

        Days(hub, state, 2);

        Assert.True(Total(state, "铁矿") > 0, "上午段在矿脉出铁矿");
        Assert.True(Total(state, "石材") > stone, "下午段在采石点出石材");
    }

    [Fact]
    public void Bench_materials_are_fetched_to_the_assigned_bench()
    {
        // 两座陶器坊：排到后建的那座（庭院），料就该搬到它台上，先建的那座（森林）一直空着。
        var hub = NewGame(out var state, 1);
        var maid = Maid(state);
        maid.Bag.Add("干粮", 30);
        state.Roster.Master!.Bag.Add("木材", 30);
        state.Roster.Master!.Bag.Add("石材", 30);
        var first = Build(hub, state, Kiln, Forest);
        var assigned = Build(hub, state, Kiln, Courtyard);
        for (var slot = 1; slot < WorkSlot.Count; slot++)
            Assert.True(hub.Assign(maid.Id, slot, SlotMode.Work, assigned.Id));

        var target = state.Clock.Day + 2;
        var fetched = false;
        while (state.Clock.Day < target)
        {
            hub.PassTime(60);
            Assert.DoesNotContain(first.Contents.Items, p => p.Value > 0);
            fetched |= assigned.Contents.Get("石材") > 0;
        }

        Assert.True(fetched, "石材被搬上了排班点名的那座陶器坊");
        Assert.True(Total(state, "陶罐") > 0, "陶器坊出了陶罐");
    }

    // ---------- 整条闭环 ----------

    /// <summary>
    /// 一个勤快的玩家：开局在客厅加一口箱子当食橱（餐桌只收熟食，干粮放不上去），
    /// 把干粮都搁进去；伐木点上午、采石点下午、攒够料建陶器坊排晚上；每天早中晚各找女仆说一次话，
    /// 食橱见底就去集市买干粮。跑 30 天，第 15 天存读档一次，读档后接着跑。
    /// </summary>
    [Fact]
    public void Territory_loop_runs_thirty_days_with_save_load_midway()
    {
        var hub = NewGame(out var state, 1);
        var maidId = Maid(state).Id;

        // 第 1 天：食橱 + 采石点
        var pantry = Build(hub, state, Chest, Parlor);
        StockPantry(hub, state, pantry.Id);
        Assert.True(pantry.Contents.Get("干粮") >= 10, "开局干粮进了食橱");
        var quarry = Build(hub, state, Quarry, Mountain);
        Assert.True(hub.Assign(maidId, 1, SlotMode.Work, Woodlot));
        Assert.True(hub.Assign(maidId, 2, SlotMode.Work, quarry.Id));

        var kilnId = -1;
        var kilnDay = -1;
        var history = new List<(int Day, int Wood, int Stone, int Pots, int Mood)>();
        for (var day = 1; day <= 30; day++)
        {
            if (day == 16)
            {
                var data = JsonSerializer.Deserialize<SaveData>(SaveSystem.Save(state, hub))!;
                state = SaveSystem.Restore(data);
                hub = new HubSession(state);
                hub.Day.Rng = new Random(16);
                hub.Restore(data.Hub!);
                Assert.Equal(SlotMode.Work, hub.AssignmentOf(maidId, 3).Mode);
                Assert.Equal(kilnId, hub.AssignmentOf(maidId, 3).FacilityId);
            }
            var target = state.Clock.Day + 1;
            while (state.Clock.Day < target)
            {
                var hour = state.Clock.Minutes / 60;
                if (hour is 8 or 13 or 19)
                    TalkTo(hub, state, maidId);
                if (hour == 8)
                {
                    if (state.Territory.Facilities.Single(f => f.Id == pantry.Id).Contents.Get("干粮") < 6)
                    {
                        hub.OpenTrade();
                        Assert.True(hub.MarketTrade("干粮", 6, selling: false), $"第 {day} 天买干粮");
                        hub.LeaveMarket();
                        StockPantry(hub, state, pantry.Id);
                    }
                    if (kilnId < 0 && hub.BuildFacilityDef(Kiln, Forest))
                    {
                        kilnId = state.Territory.Facilities[^1].Id;
                        kilnDay = day;
                        Assert.True(hub.Assign(maidId, 3, SlotMode.Work, kilnId));
                    }
                }
                hub.PassTime(60);
                AssertNoNegativeStock(state);
            }
            var maid = state.Roster.Find(maidId)!;
            history.Add((day, Total(state, "木材"), Total(state, "石材"), Total(state, "陶罐"), maid.Affect.Mood));
            _out.WriteLine($"第{day}天 金钱={state.Money} 心情={maid.Affect.Mood} 木材={history[^1].Wood} 石材={history[^1].Stone} 陶罐={history[^1].Pots}");
        }

        // 建造：攒料两天内就建起了第一座工坊
        Assert.InRange(kilnDay, 1, 3);
        // 采集：木材在 30 天里持续增长（伐木点没停）
        Assert.True(history[^1].Wood > history[kilnDay].Wood + 20, $"木材 {history[kilnDay].Wood} → {history[^1].Wood}");
        // 加工：陶器坊把采来的石材加工成陶罐，读档前后都在出
        Assert.True(history[14].Pots > 0, "读档前已有陶罐");
        Assert.True(history[^1].Pots > history[14].Pots, $"读档后陶罐继续增长 {history[14].Pots} → {history[^1].Pots}");
        // 被照顾的工人不会心情崩到罢工
        Assert.All(history, h => Assert.True(h.Mood > Affect.RefuseWorkAt, $"第 {h.Day} 天心情 {h.Mood}"));

        // 卖：仓里的陶罐取到手上，在集市出手换钱
        foreach (var store in state.Territory.Facilities.Where(f => f.Contents.Get("陶罐") > 0).ToList())
        {
            MoveTo(hub, store.RoomId);
            Assert.True(hub.OpenStorage(store.Id));
            Assert.True(hub.TakeOne("陶罐", store.Contents.Get("陶罐")));
            hub.CloseStorage();
        }
        var held = state.Roster.Master!.Bag.Get("陶罐");
        Assert.True(held > 0, "仓里有陶罐可卖");
        var money = state.Money;
        hub.OpenTrade();
        Assert.True(hub.MarketTrade("陶罐", held, selling: true));
        hub.LeaveMarket();
        _out.WriteLine($"卖出陶罐 {held} 个：金钱 {money} → {state.Money}");
        Assert.True(state.Money > money);
    }

    [Fact]
    public void Worker_asleep_in_the_auto_locked_bedroom_is_still_there_after_load()
    {
        // 卧室是私人空间，主人不在屋里就自动上锁；女仆照样睡在那唯一的床上。
        // 读档不能按门锁把她筛出去——否则在场记录成 -1，人从地图上消失，从此不再干活。
        var hub = NewGame(out var state, 1);
        var maid = Maid(state);
        maid.Bag.Add("干粮", 30);
        Assert.True(hub.Assign(maid.Id, 1, SlotMode.Work, Woodlot));
        Days(hub, state, 1);
        var bedroom = state.Territory.Facilities.Single(f => f.Name == "床").RoomId;
        Assert.Equal(bedroom, hub.Party().Single(c => c.Id == maid.Id).RoomId);
        Assert.NotEqual(bedroom, hub.PlayerRoomId);
        Assert.True(state.Territory.IsLocked(state.Territory.Room(bedroom)!));

        var data = JsonSerializer.Deserialize<SaveData>(SaveSystem.Save(state, hub))!;
        var loaded = SaveSystem.Restore(data);
        var reloaded = new HubSession(loaded);
        reloaded.Restore(data.Hub!);

        Assert.Equal(bedroom, reloaded.Party().Single(c => c.Id == maid.Id).RoomId);
        var wood = Total(loaded, "木材");
        Days(reloaded, loaded, 1);
        Assert.True(Total(loaded, "木材") > wood, "读档后照常去伐木点干活");
    }

    // ---------- 配表：每样建材、原料、产物都是能交易的物品 ----------

    [Fact]
    public void Every_cost_input_and_output_is_a_tradeable_item()
    {
        // 集市每天上架全部物品定义，所以「是物品定义」＝至少能买到；领地里一时产不出的料（如炼金尘）也不会卡死建造。
        ContentDefs.EnsureInitialized();
        var unknown = new List<string>();
        foreach (var f in DefDatabase<FacilityDef>.All)
        {
            unknown.AddRange(f.MaterialCost.Where(c => Items.Get(c.ItemId) == null).Select(c => $"设施 {f.Name} 料 {c.ItemId}"));
            if (f.YieldItemId.Length > 0 && Items.Get(f.YieldItemId) == null)
                unknown.Add($"设施 {f.Name} 产 {f.YieldItemId}");
        }
        foreach (var r in DefDatabase<RoomDef>.All)
            unknown.AddRange(r.MaterialCost.Where(c => Items.Get(c.ItemId) == null).Select(c => $"房间 {r.Name} 料 {c.ItemId}"));
        foreach (var r in DefDatabase<RecipeDef>.All.Select(d => d.ToRuntime()))
        {
            unknown.AddRange(r.Costs.Where(c => Items.Get(c.ItemId) == null).Select(c => $"配方 {r.ItemId} 料 {c.ItemId}"));
            if (Items.Get(r.ItemId) == null)
                unknown.Add($"配方产物 {r.ItemId}");
        }
        foreach (var crop in DefDatabase<CropDef>.All)
        {
            if (Items.Get(crop.SeedItemId) == null)
                unknown.Add($"作物 {crop.DefName} 种子 {crop.SeedItemId}");
        }
        Assert.True(unknown.Count == 0, string.Join("\n", unknown));
    }

    private static void StockPantry(HubSession hub, GameState state, int pantryId)
    {
        var master = state.Roster.Master!;
        if (state.Territory.Facilities.Single(f => f.Id == Chest).Contents.Get("干粮") > 0)
        {
            MoveTo(hub, state.Territory.Facilities.Single(f => f.Id == Chest).RoomId);
            Assert.True(hub.OpenStorage(Chest));
            hub.TakeOne("干粮", state.Territory.Facilities.Single(f => f.Id == Chest).Contents.Get("干粮"));
            hub.CloseStorage();
        }
        MoveTo(hub, Parlor);
        Assert.True(hub.OpenStorage(pantryId));
        Assert.True(hub.StoreOne("干粮", master.Bag.Get("干粮")));
        hub.CloseStorage();
    }

    private static void MoveTo(HubSession hub, int roomId)
    {
        if (hub.PlayerRoomId != roomId)
            Assert.True(hub.Arrive(roomId), $"走到房间 {roomId}");
    }

    private static void TalkTo(HubSession hub, GameState state, int characterId)
    {
        var room = hub.Party().Single(c => c.Id == characterId).RoomId;
        if (room < 0 || (hub.PlayerRoomId != room && !hub.Arrive(room)))
            return;
        if (hub.Select(characterId))
            hub.Social(SocialAction.Talk);
    }
}
