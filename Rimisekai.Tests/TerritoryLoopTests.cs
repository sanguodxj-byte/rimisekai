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
    private const int Woodlot = 11, IronVein = 7, HerbBush = 6;
    private const int Pigsty = 1068;  // 猪圈：Tend → 肉
    private const int GuestBed = 1019; // 客房床
    private const int GuestRoom = 145;  // 卧室（12 木材）
    private const int GuestCellX = 1, GuestCellY = 1; // 客厅西边、森林北边的空格
    private const int Sofa = 4;        // 客厅沙发（娱乐时段去歇着）
    private const int Bed = 5;         // 床：木材 10
    private const int Bedroom = 3;

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
            if (place.MasterBed)
                state.Territory.MasterBedId = facility.Id;
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

    /// <summary>卧室里给女仆打一张床（开局那张是主人的，她好感不够不睡）。料另给，不动开局存货。</summary>
    internal static Facility GiveMaidABed(HubSession hub, GameState state)
    {
        state.Roster.Master!.Bag.Add("木材", 10);
        Assert.True(hub.BuildFacilityDef(Bed, Bedroom));
        return state.Territory.Facilities[^1];
    }

    [Fact]
    public void Untouched_start_maid_eats_rations_from_the_dining_room_chest()
    {
        // 开局什么都不做：箱子在客厅（有餐桌），女仆自己按饭点去吃，三天一顿不落。
        var hub = NewGame(out var state, 7);
        var chest = state.Territory.Facilities.Single(f => f.Id == Chest);
        Assert.Equal(Parlor, chest.RoomId);
        Assert.Contains(state.Territory.Facilities, f => f.IsTable && f.RoomId == chest.RoomId);
        var maid = Maid(state);
        var missed = 0;
        var rations = chest.Contents.Get("干粮");
        for (var i = 0; i < 3 * 24 * 6; i++)
        {
            var mood = maid.Affect.Mood;
            // 她没床（开局那张是主人的），夜里打地铺，起床那一下的心情扣减不算漏饭。
            var asleep = hub.Day.Workers.Single(w => w.CharacterId == maid.Id).Goal == ActionKind.Sleep;
            hub.PassTime(10);
            var woke = asleep && hub.Day.Workers.Single(w => w.CharacterId == maid.Id).Goal != ActionKind.Sleep;
            if (!woke && maid.Affect.Mood <= mood - 8)
                missed++;
            AnswerWhoeverWantsToTalk(hub, state);
        }
        Assert.Equal(0, missed);
        Assert.True(chest.Contents.Get("干粮") <= rations - 6, $"干粮 {rations} → {chest.Contents.Get("干粮")}");
    }

    [Fact]
    public void Food_from_outdoor_work_goes_to_the_dining_room_store_not_the_well()
    {
        NewGame(out var state, 1);
        // 庭院里的水井只存水，人又只在有餐桌的客厅里找吃的：肉得送进客厅的箱子。
        var store = state.Territory.FindStorageFor("肉", preferRoomId: Courtyard);
        Assert.NotNull(store);
        Assert.Equal(Chest, store!.Id);
    }

    [Fact]
    public void Npcs_pick_an_empty_bedroom_over_sharing_with_a_stranger()
    {
        var hub = NewGame(out var state, 3);
        var maid = Maid(state);
        GiveMaidABed(hub, state);
        var guest = state.Roster.Add("旅人", false);
        hub.Place(guest.Id, Parlor);
        // 第二间卧室：开拓客厅西边的空格、装卧室、摆客房床。
        state.Roster.Master!.Bag.Add("木材", 40);
        Assert.True(hub.DevelopVacantCell(0, GuestCellX, GuestCellY));
        var vacant = state.Territory.RoomAt(0, GuestCellX, GuestCellY)!.Id;
        Assert.True(hub.BuildRoomDef(GuestRoom, vacant));
        var room = state.Territory.RoomAt(0, GuestCellX, GuestCellY)!.Id;
        Assert.True(hub.BuildFacilityDef(GuestBed, room));
        // 跑到半夜：两人各睡一间，不挤一张床。
        while (state.Clock.Minutes / 60 != 1)
        {
            hub.PassTime(10);
            AnswerWhoeverWantsToTalk(hub, state);
        }
        var beds = new[] { maid.Id, guest.Id }
            .Select(id => hub.Day.Workers.Single(w => w.CharacterId == id))
            .Select(w => (w.Goal, Room: state.Territory.Facilities.Single(f => f.Id == w.FacilityId).RoomId))
            .ToList();
        Assert.All(beds, b => Assert.Equal(ActionKind.Sleep, b.Goal));
        Assert.NotEqual(beds[0].Room, beds[1].Room);
    }

    [Fact]
    public void Working_master_does_not_get_bored()
    {
        // 主人的消遣由玩家自己安排：排他上工好几天，不会因为「没娱乐」每天扣心情。
        var hub = NewGame(out var state, 2);
        var master = state.Roster.Master!;
        for (var slot = 1; slot <= 2; slot++)
            Assert.True(hub.Assign(master.Id, slot, SlotMode.Work, Woodlot));
        master.Bag.Add("干粮", 30);
        for (var i = 0; i < 5 * 24 * 6; i++)
        {
            hub.PassTime(10);
            AnswerWhoeverWantsToTalk(hub, state);
        }
        Assert.Equal(-1, master.Affect.LastBoredDay);
    }

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
        GiveMaidABed(hub, state);
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
            // 逐格看：料一搬上台就被拉坯用掉，按小时抽查会错过。
            hub.PassTime(TerritoryClock.StepMinutes);
            Assert.DoesNotContain(first.Contents.Items, p => p.Value > 0);
            fetched |= assigned.Contents.Get("石材") > 0;
        }

        Assert.True(fetched, "石材被搬上了排班点名的那座陶器坊");
        Assert.True(Total(state, "陶罐") > 0, "陶器坊出了陶罐");
    }

    // ---------- 整条闭环 ----------

    /// <summary>
    /// 三个人的领地：玩家、开局女仆、第 3 天登门留下的随机旅人。没有雇佣，只有这三双手。
    /// 开局箱子就在客厅（餐厅）里，干粮够得着，谁都不用喂。玩家照一个用心的玩家来：
    /// 第 1 天用开局木材建采石点、猪圈，再亲手去林场伐木，攒够 10 木材在卧室给女仆打一张床
    /// （女仆与主人同屋不介意，同床要好感）；之后玩家自己排班去养猪（出肉当口粮）；
    /// 女仆上午、下午伐木，陶器坊建起来后下午改去拉坯；旅人上午采草药、下午缺石材采石缺木材伐木、陶器坊建起来后采石；
    /// 两人晚上排娱乐（沙发）。旅人不是女仆，睡觉锁门、与人挤房扣心情：给他开拓一间客房（1000G）摆张床。
    /// 谁来找玩家说话就应一声；每两天去一趟集市，箱里口粮不够就买。
    /// 钱主要靠委托与探索（这里不跑），所以不卖产物：只看领地本身越滚越大、口粮花得起。
    /// 跑 30 天，第 16 天存读档一次。
    /// </summary>
    [Fact]
    public void Three_people_territory_loop_runs_thirty_days_with_save_load_midway()
    {
        var hub = NewGame(out var state, 1);
        LoadVisitorEvent(state);
        hub.InitializeScheduledEvents();
        DrainGeneration(hub);
        var master = state.Roster.Master!;
        var maidId = Maid(state).Id;
        var startMoney = state.Money;

        // 第 1 天：采石点、猪圈（料都从开局背包 + 客厅箱子里出）
        var quarry = Build(hub, state, Quarry, Mountain);
        var pigstyId = Build(hub, state, Pigsty, Courtyard).Id;
        Assert.True(hub.Assign(maidId, 1, SlotMode.Work, Woodlot));
        Assert.True(hub.Assign(maidId, 2, SlotMode.Work, Woodlot));
        Assert.True(hub.Assign(maidId, 3, SlotMode.Entertainment, Sofa));
        // 玩家亲手伐木，够一张床就回卧室打床
        MoveTo(hub, Forest);
        Assert.True(hub.Use(Woodlot));
        while (Total(state, "木材") < 10)
        {
            Assert.True(hub.ActAtFixture(ActionKind.Fell));
            AnswerWhoeverWantsToTalk(hub, state);
            if (hub.PlayerRoomId != Forest)
            {
                MoveTo(hub, Forest);
                Assert.True(hub.Use(Woodlot));
            }
        }
        Assert.Equal(1, state.Clock.Day);
        Assert.True(hub.BuildFacilityDef(Bed, Bedroom), "第一天伐的木头给女仆打床");
        var maidBedId = state.Territory.Facilities[^1].Id;
        _out.WriteLine($"第 1 天 {state.Clock.Minutes / 60}:{state.Clock.Minutes % 60:00} 床打好了");
        for (var slot = 1; slot <= 2; slot++)
            Assert.True(hub.Assign(master.Id, slot, SlotMode.Work, pigstyId), "玩家自己能排班");

        var guestRoomId = -1;
        var guestBedId = -1;
        var kilnId = -1;
        var visitorId = -1;
        var maidSleptInHerBed = false;
        long spent = 0;
        var history = new List<(int Day, long Money, int Wood, int Stone, int Meat, int Pots, int Dust)>();
        var moods = new Dictionary<int, int>();
        var produced = new Dictionary<int, int>();
        for (var day = 1; day <= 30; day++)
        {
            if (day == 16)
            {
                var data = JsonSerializer.Deserialize<SaveData>(SaveSystem.Save(state, hub))!;
                state = SaveSystem.Restore(data);
                LoadVisitorEvent(state);
                hub = new HubSession(state);
                hub.Day.Rng = new Random(16);
                hub.Restore(data.Hub!);
                master = state.Roster.Master!;
                Assert.Equal(3, state.Roster.Members.Count);
                Assert.Equal(kilnId, hub.AssignmentOf(maidId, 2).FacilityId);
                Assert.Equal(pigstyId, hub.AssignmentOf(master.Id, 1).FacilityId);
            }
            var target = state.Clock.Day + 1;
            var built = false;
            var traded = false;
            while (state.Clock.Day < target)
            {
                var hour = state.Clock.Minutes / 60;
                if (hour >= 20 && !built)
                {
                    built = true;
                    // 先给第三个人收拾出一间客房：开拓空地 → 装卧室 → 摆客房床
                    if (guestRoomId < 0 && hub.DevelopVacantCell(0, GuestCellX, GuestCellY))
                    {
                        guestRoomId = state.Territory.RoomAt(0, GuestCellX, GuestCellY)!.Id;
                    }
                    if (guestRoomId >= 0 && state.Territory.Rooms.Find(r => r.Id == guestRoomId)!.Vacant
                        && hub.BuildRoomDef(GuestRoom, guestRoomId))
                    {
                        guestRoomId = state.Territory.RoomAt(0, GuestCellX, GuestCellY)!.Id;
                    }
                    if (guestRoomId >= 0 && guestBedId < 0 && !state.Territory.Rooms.Find(r => r.Id == guestRoomId)!.Vacant
                        && hub.BuildFacilityDef(Bed, guestRoomId))
                    {
                        guestBedId = state.Territory.Facilities[^1].Id;
                    }
                    // 木材堆着用不完就让女仆上午也去采石，喂陶器坊；木材见底再回林场
                    var wood = Total(state, "木材");
                    if (kilnId >= 0)
                        Assert.True(hub.Assign(maidId, 1, SlotMode.Work, wood >= 30 ? quarry.Id
                            : wood < 15 ? Woodlot : hub.AssignmentOf(maidId, 1).FacilityId));
                    // 陶器坊建起来之前，旅人下午缺什么补什么：石材不够 16 去采石，够了去伐木
                    if (visitorId >= 0 && kilnId < 0)
                        Assert.True(hub.Assign(visitorId, 2, SlotMode.Work, Total(state, "石材") < 16 ? quarry.Id : Woodlot));
                    if (guestBedId >= 0 && kilnId < 0 && hub.BuildFacilityDef(Kiln, Forest))
                    {
                        kilnId = state.Territory.Facilities[^1].Id;
                        Assert.True(hub.Assign(maidId, 2, SlotMode.Work, kilnId));
                        // 陶器坊要石材：旅人下午从林场改去采石
                        if (visitorId >= 0)
                            Assert.True(hub.Assign(visitorId, 2, SlotMode.Work, quarry.Id));
                    }
                }
                if (hour >= 21 && !traded && day % 2 == 0 && (traded = true))
                    spent = MarketRun(hub, state, spent);
                foreach (var log in hub.PassTime(10))
                    produced[log.CharacterId] = produced.GetValueOrDefault(log.CharacterId) + log.Count;
                if (hub.ScenePlaying)
                {
                    // 第 3 天十一点：旅人登门，留下他。
                    PlayScene(hub, choice: 0);
                    var visitor = state.Roster.Members.Single(c => !c.IsMaster && c.Id != maidId);
                    visitorId = visitor.Id;
                    Assert.Equal(3, day);
                    Assert.True(hub.Assign(visitorId, 1, SlotMode.Work, HerbBush));
                    Assert.True(hub.Assign(visitorId, 2, SlotMode.Work, kilnId >= 0 ? quarry.Id : Woodlot));
                    Assert.True(hub.Assign(visitorId, 3, SlotMode.Entertainment, Sofa));
                }
                AnswerWhoeverWantsToTalk(hub, state);
                AssertNoNegativeStock(state);
                maidSleptInHerBed |= hub.Day.Workers.Any(w => w.CharacterId == maidId && w.Goal == ActionKind.Sleep && w.FacilityId == maidBedId);
            }
            foreach (var c in state.Roster.Members)
                moods[c.Id] = Math.Min(moods.GetValueOrDefault(c.Id, 100), c.Affect.Mood);
            history.Add((day, state.Money, Total(state, "木材"), Total(state, "石材"), Total(state, "肉"),
                Total(state, "陶罐"), Total(state, "炼金尘")));
            var h = history[^1];
            _out.WriteLine($"第{day}天 金钱={h.Money} 木材={h.Wood} 石材={h.Stone} 草药={Total(state, "herb")} 肉={h.Meat} 陶罐={h.Pots} 炼金尘={h.Dust} 干粮={Total(state, "干粮")} "
                + string.Join(" ", state.Roster.Members.Select(c => $"{c.Name}心情={c.Affect.Mood} 产出={produced.GetValueOrDefault(c.Id)}")));
        }
        _out.WriteLine($"30 天：买口粮 {spent}G；金钱 {startMoney} → {state.Money}；"
            + string.Join(" ", state.Roster.Members.Select(c => $"{c.Name}产出={produced.GetValueOrDefault(c.Id)}")));

        // 三个人都在干活出东西：玩家（伐木、养猪）、女仆、第 3 天来的旅人
        Assert.True(visitorId >= 0, "第 3 天有旅人登门留下");
        Assert.All(state.Roster.Members, c => Assert.True(produced.GetValueOrDefault(c.Id) > 30, $"{c.Name} 30 天产出 {produced.GetValueOrDefault(c.Id)}"));
        Assert.True(maidSleptInHerBed, "女仆睡在第一天给她打的床上");
        // 建造：客房、陶器坊、猪圈都建起来了
        Assert.True(guestBedId >= 0 && kilnId >= 0 && pigstyId >= 0, $"客房床 {guestBedId} 陶器坊 {kilnId} 猪圈 {pigstyId}");
        // 领地越滚越大：成品库存后半月比前半月攒得多（陶器坊开工后），木材石材没见底
        var mid = history.Single(h => h.Day == 15);
        var end = history[^1];
        Assert.True(end.Pots + end.Dust - mid.Pots - mid.Dust > mid.Pots + mid.Dust,
            $"陶罐+炼金尘 第15天 {mid.Pots + mid.Dust} → 第30天 {end.Pots + end.Dust}");
        Assert.True(end.Wood + end.Stone > 0, $"木材 {end.Wood} 石材 {end.Stone}");
        // 钱：不靠卖产物。除开拓客房的 1000G 投资外，口粮开销花得起、金钱从未见底
        Assert.True(history.All(h => h.Money > 0), "金钱从未见底");
        Assert.True(spent < startMoney - Hub.HubSession.VacantBaseMoney, $"口粮 {spent}G");
        // 没人心情崩到罢工（玩家自己也算）
        Assert.All(moods, m => Assert.True(m.Value > Affect.RefuseWorkAt, $"#{m.Key} 最低心情 {m.Value}"));
    }

    /// <summary>有人来找玩家说话（在玩家屋里等着，或在门外等）就应一声。</summary>
    private static void AnswerWhoeverWantsToTalk(HubSession hub, GameState state)
    {
        foreach (var w in hub.Day.Workers.Where(w => w.WantsChat || w.SeekWaiting).ToList())
            TalkTo(hub, state, w.CharacterId);
    }

    /// <summary>集市一趟：客厅箱子口粮不足 18 就买到 18（不卖产物，钱另有来路）。返回累计花销。</summary>
    private static long MarketRun(HubSession hub, GameState state, long spent)
    {
        var master = state.Roster.Master!;
        hub.OpenTrade();
        var chest = state.Territory.Facilities.Single(f => f.Id == Chest);
        var need = 18 - chest.Contents.Items.Where(p => state.Territory.IsFood(p.Key)).Sum(p => p.Value);
        foreach (var food in Provisions)
        {
            while (need > 0)
            {
                var before = state.Money;
                if (!hub.MarketTrade(food, 1, selling: false))
                    break;
                spent += before - state.Money;
                need--;
            }
        }
        hub.LeaveMarket();
        var bought = Provisions.Where(f => master.Bag.Get(f) > 0).ToList();
        if (bought.Count > 0)
        {
            MoveTo(hub, chest.RoomId);
            Assert.True(hub.OpenStorage(Chest));
            foreach (var f in bought)
                Assert.True(hub.StoreOne(f, master.Bag.Get(f)));
            hub.CloseStorage();
        }
        return spent;
    }

    /// <summary>集市补口粮：干粮先买，卖完了买别的现成吃食。</summary>
    private static readonly string[] Provisions = { "干粮", "面包", "果实", "鸡蛋", "鱼" };

    /// <summary>台词包里的访客场景与它的定时事件（第 3 天 11 点）。生成层不在测试里跑，由 <see cref="DrainGeneration"/> 代写。</summary>
    private static void LoadVisitorEvent(GameState state)
    {
        var json = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "content", "voice.json"));
        Assert.True(Rimisekai.Voice.VoicePackJson.TryParse(json, out _, out _, out var scenes, out var events, out _, out var error), error);
        state.Voice.Scenes.RegisterRange(scenes.Where(s => s.Id == "visitor_arrival"));
        state.Voice.Events.RegisterRange(events.Where(e => e.Id == "visitor_arrival"));
    }

    private static string RepoRoot() =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static void DrainGeneration(HubSession hub)
    {
        while (hub.TakeGenerationTask() is { } task)
            hub.CompleteGeneration(task, new[] { "（测试代写的台词。）" });
    }

    private static void PlayScene(HubSession hub, int choice)
    {
        for (var guard = 0; guard < 100 && hub.ScenePlaying; guard++)
        {
            if (hub.SceneChoices.Count > 0)
                hub.SceneChoose(choice);
            else
                hub.SceneContinue();
        }
        Assert.False(hub.ScenePlaying);
    }

    [Fact]
    public void Worker_asleep_in_the_auto_locked_bedroom_is_still_there_after_load()
    {
        // 卧室是私人空间，主人不在屋里就自动上锁；女仆照样睡在卧室里她自己那张床上。
        // 读档不能按门锁把她筛出去——否则在场记录成 -1，人从地图上消失，从此不再干活。
        var hub = NewGame(out var state, 1);
        var maid = Maid(state);
        maid.Bag.Add("干粮", 30);
        GiveMaidABed(hub, state);
        Assert.True(hub.Assign(maid.Id, 1, SlotMode.Work, Woodlot));
        Days(hub, state, 1);
        var bedroom = Bedroom;
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
