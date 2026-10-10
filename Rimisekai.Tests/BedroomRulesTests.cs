using System;
using System.Linq;
using Rimisekai.Character;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Save;
using Xunit;
using Xunit.Abstractions;

namespace Rimisekai.Tests;

/// <summary>
/// 起居规则：女仆与主人同屋不扣心情、同床看好感；主人躺着的床别人好感不够不上；
/// 往睡着人的床上钻会被踢下来；别人睡下就锁门赶人；水井只存水；第一天伐木就够打一张床。
/// 开局照标准种子装配（见 <see cref="TerritoryLoopTests.NewGame"/>）。
/// </summary>
public sealed class BedroomRulesTests
{
    private const int Courtyard = 1, Parlor = 2, Bedroom = 3, Forest = 4;
    private const int StartBed = 5;      // 卧室里开局那张床
    private const int BedDef = 5;        // 建筑表里的「床」：木材 10
    private const int Well = 1, Chest = 8, Woodlot = 11;
    private const int GuestRoomDef = 3;   // 卧室：建成自带一张床

    private readonly ITestOutputHelper _out;

    public BedroomRulesTests(ITestOutputHelper output) => _out = output;

    private static CharacterState Maid(GameState state) => state.Roster.Members.Single(c => !c.IsMaster && c.IsMaid());

    private static Worker W(HubSession hub, int characterId) => hub.Day.Workers.Single(w => w.CharacterId == characterId);

    private static bool Asleep(HubSession hub, int characterId) =>
        hub.Day.Workers.FirstOrDefault(w => w.CharacterId == characterId) is { Goal: ActionKind.Sleep, Path.Count: 0 };

    /// <summary>按 10 分钟一格推进，直到条件成立；两天内不成立判失败。</summary>
    private static void RunUntil(HubSession hub, Func<bool> done, string what)
    {
        for (var i = 0; i < 2 * 24 * 6; i++)
        {
            if (done())
                return;
            hub.PassTime(10);
        }
        Assert.Fail($"两天内没等到：{what}");
    }

    /// <summary>
    /// 开拓客厅西边的空格、再装一间卧室（建成自带一张床）。家具只能摆室内，
    /// 庭院打不了床，外人要睡就得有间屋子。返回第二间卧室的房间 Id。
    /// </summary>
    private static int GuestRoom(HubSession hub, GameState state)
    {
        state.Money += 5000;
        state.Roster.Master!.Bag.Add("木材", 60);
        state.Roster.Master!.Bag.Add("石材", 60);
        Assert.True(hub.DevelopVacantCell(0, 1, 1));
        Assert.True(hub.BuildRoomDef(GuestRoomDef, state.Territory.RoomAt(0, 1, 1)!.Id));
        var room = state.Territory.RoomAt(0, 1, 1)!;
        Assert.Contains(state.Territory.Facilities, f => f.RoomId == room.Id && f.Supports(ActionKind.Sleep));
        return room.Id;
    }

    private static Facility BuildBed(HubSession hub, GameState state, int roomId)
    {
        state.Roster.Master!.Bag.Add("木材", 10);
        Assert.True(hub.BuildFacilityDef(BedDef, roomId));
        return state.Territory.Facilities[^1];
    }

    /// <summary>主人待在某间房里，等某人在那间睡到天亮，返回起床那一下的心情变化。</summary>
    private static int WakeDeltaNextToMaster(HubSession hub, GameState state, CharacterState who, int roomId)
    {
        hub.Enter(roomId);
        RunUntil(hub, () => Asleep(hub, who.Id) && W(hub, who.Id).RoomId == roomId && state.Clock.Minutes >= 5 * 60 + 50
            && state.Clock.Minutes < 6 * 60, "睡到清晨五点五十");
        var before = who.Affect.Mood;
        RunUntil(hub, () => !Asleep(hub, who.Id), "起床");
        return who.Affect.Mood - before;
    }

    [Fact]
    public void Maid_shares_the_masters_room_without_the_crowding_penalty()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 11);
        TerritoryLoopTests.GiveMaidABed(hub, state);
        var maidDelta = WakeDeltaNextToMaster(hub, state, Maid(state), Bedroom);
        Assert.True(maidDelta > -10, $"女仆与主人同屋醒来心情 {maidDelta}");

        // 对照：同一个人去掉女仆身份，与（睡在第二间卧室的）主人同屋醒来就是挤房 -15。
        // 外人不去主人的卧室睡（见 Lock_happy_sleepers_never_take_the_masters_bedroom），所以换到第二间卧室比。
        hub = TerritoryLoopTests.NewGame(out state, 11);
        var plain = Maid(state);
        plain.Talents.Remove((int)Trait.Maid);
        var guestRoom = GuestRoom(hub, state);
        state.Territory.MasterAsleep = true; // 主人在第二间卧室打地铺睡着：睡着的人不被请出门
        var plainDelta = WakeDeltaNextToMaster(hub, state, plain, guestRoom);
        Assert.True(plainDelta <= -10, $"外人与主人同屋醒来心情 {plainDelta}");
    }

    [Fact]
    public void Sharing_the_masters_bed_needs_the_co_sleep_affection_tier()
    {
        NewMaid(out var maid);
        Assert.Equal(800, Intimacy.ShareBed);
        Assert.Equal(HubSession.FavorCoSleep, Intimacy.ShareBed);
        Assert.False(Intimacy.SharesBed(maid), "开局好感不够同床");
        maid.Condition.AddFavor(Intimacy.ShareBed - maid.Condition.Favor);
        Assert.True(Intimacy.SharesBed(maid), "好感到 800（心情 50）就肯同床");
    }

    private static HubSession NewMaid(out CharacterState maid)
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 12);
        maid = Maid(state);
        return hub;
    }

    [Fact]
    public void Maid_sleeps_in_the_other_bed_when_the_master_is_in_hers()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 13);
        var maid = Maid(state);
        var second = BuildBed(hub, state, Bedroom);
        hub.Enter(Bedroom);
        Assert.True(hub.Use(StartBed));
        RunUntil(hub, () => Asleep(hub, maid.Id), "女仆睡下");
        Assert.Equal(second.Id, W(hub, maid.Id).FacilityId);
        Assert.Equal(StartBed, hub.UsingFixtureId);
        Assert.Empty(state.Territory.SleeperLocks); // 女仆睡下不锁门
    }

    [Fact]
    public void Maid_never_climbs_into_the_bed_the_master_lies_in_without_affection()
    {
        // 卧室只有主人躺着的那张床：女仆去别处的床——一张都没有就打地铺（见 No_bed_means_a_floor_in_an_indoor_room_and_never_outdoors），也不往主人床上挤。
        var hub = TerritoryLoopTests.NewGame(out var state, 14);
        var maid = Maid(state);
        hub.Enter(Bedroom);
        Assert.True(hub.Use(StartBed));
        for (var i = 0; i < 24 * 6; i++)
        {
            hub.PassTime(10);
            Assert.False(Asleep(hub, maid.Id) && W(hub, maid.Id).FacilityId == StartBed, $"{state.Clock.Minutes / 60} 点女仆上了主人的床");
        }

        // 好感够了就同床。
        maid.Condition.AddFavor(1000);
        RunUntil(hub, () => Asleep(hub, maid.Id), "好感满了同床");
        Assert.Equal(StartBed, W(hub, maid.Id).FacilityId);
    }

    [Fact]
    public void Master_climbing_into_the_sleeping_maids_bed_gets_kicked_out()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 15);
        var maid = Maid(state);
        var hers = TerritoryLoopTests.GiveMaidABed(hub, state);
        RunUntil(hub, () => Asleep(hub, maid.Id) && W(hub, maid.Id).FacilityId == hers.Id, "女仆在她自己的床上睡下");
        hub.Enter(Bedroom);
        var log = hub.Log.Count;
        Assert.False(hub.Use(hers.Id));
        Assert.Null(hub.UsingFixtureId);
        Assert.Equal($"你刚钻进床，就被{maid.Name}一脚踢了下来。", hub.UseRefusal);
        Assert.Equal(log, hub.Log.Count); // 提示签，不进日志
        // 自己的床照睡。
        Assert.True(hub.Use(StartBed));

        maid.Condition.AddFavor(1000);
        Assert.True(hub.Use(hers.Id));
        Assert.Equal("", hub.UseRefusal);
    }

    [Fact]
    public void Other_sleepers_lock_their_door_evict_the_awake_and_keep_others_out_until_they_wake()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 16);
        var maid = Maid(state);
        var guest = state.Roster.Add("旅人", false);
        var guestRoom = GuestRoom(hub, state);
        hub.Place(guest.Id, guestRoom);
        // 主人躺在卧室那张床上：旅人与女仆好感都不够同床，旅人只能去第二间卧室那张。
        hub.Enter(Bedroom);
        Assert.True(hub.Use(StartBed));
        RunUntil(hub, () => Asleep(hub, guest.Id), "旅人在第二间卧室睡下");
        var yard = state.Territory.Rooms.Single(r => r.Id == guestRoom);
        Assert.True(state.Territory.IsLocked(yard));
        Assert.Equal(guest.Id, state.Territory.SleeperLocks[guestRoom].SleeperId);

        // 醒着待在屋里的人被请出去。
        hub.Day.EndRoutineOf(maid.Id);
        W(hub, maid.Id).RoomId = guestRoom;
        hub.PassTime(10);
        Assert.NotEqual(guestRoom, W(hub, maid.Id).RoomId);
        // 门锁着进不来。
        hub.Place(maid.Id, guestRoom);
        Assert.NotEqual(guestRoom, W(hub, maid.Id).RoomId);
        // 主人也进不来（门锁一视同仁）。
        hub.Enter(guestRoom);
        Assert.Equal(Bedroom, hub.PlayerRoomId);
        Assert.False(hub.CanReach(guestRoom));
        Assert.True(hub.LockedOut(guestRoom));

        RunUntil(hub, () => !Asleep(hub, guest.Id), "旅人起床");
        hub.PassTime(10);
        Assert.False(state.Territory.IsLocked(yard));
        Assert.True(hub.Arrive(guestRoom), "醒了门开，主人进得去");
    }

    [Fact]
    public void Masters_lock_keeps_the_maid_out_only_while_he_stays_inside()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 22);
        var maid = Maid(state);
        var hers = TerritoryLoopTests.GiveMaidABed(hub, state);
        hub.Place(maid.Id, Parlor);
        hub.Enter(Bedroom);
        Assert.True(hub.ToggleRoomLock());
        Assert.Equal(RoomLock.Locked, state.Territory.Rooms.Single(r => r.Id == Bedroom).Lock);
        // 锁着：女仆进不来，就算到了睡觉的点、她的床就在里头。
        while (state.Clock.Minutes / 60 != 23)
        {
            hub.PassTime(10);
            Assert.NotEqual(Bedroom, W(hub, maid.Id).RoomId);
        }
        // 主人出门：门回到自动，女仆进得去，回自己的床睡。
        Assert.True(hub.Arrive(Parlor));
        RunUntil(hub, () => Asleep(hub, maid.Id) && W(hub, maid.Id).FacilityId == hers.Id, "女仆回她的床睡下");
    }

    [Fact]
    public void Awake_master_is_evicted_when_someone_locks_the_room_to_sleep()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 19);
        var guest = state.Roster.Add("旅人", false);
        var guestRoom = GuestRoom(hub, state);
        hub.Place(guest.Id, guestRoom);
        hub.Enter(guestRoom);
        RunUntil(hub, () => Asleep(hub, guest.Id), "旅人在第二间卧室睡下");
        Assert.NotEqual(guestRoom, hub.PlayerRoomId);
        Assert.Contains(hub.Log, e => e.Text.Contains($"{guest.Name}锁门睡下，你被请出了卧室"));
        Assert.False(hub.Arrive(guestRoom));
        Assert.True(hub.LockedOut(guestRoom));
    }

    [Fact]
    public void A_sleeper_with_no_open_room_next_door_does_not_shut_the_master_out_of_everything()
    {
        // 请出门却无处可去：主人留在原地，不被关死。
        var hub = TerritoryLoopTests.NewGame(out var state, 20);
        var guest = state.Roster.Add("旅人", false);
        var guestRoom = GuestRoom(hub, state);
        var room = state.Territory.Rooms.Single(r => r.Id == guestRoom);
        hub.Place(guest.Id, guestRoom);
        foreach (var id in room.Links.ToList())
            state.Territory.Rooms.Single(r => r.Id == id).Open = false;
        hub.Enter(guestRoom);
        RunUntil(hub, () => Asleep(hub, guest.Id), "旅人在第二间卧室睡下");
        Assert.Equal(guestRoom, hub.PlayerRoomId);
    }

    [Fact]
    public void Lock_happy_sleepers_never_take_the_masters_bedroom()
    {
        // 卧室里除了主人的床还有一张空床，旅人别处无床：他也不去主人的卧室睡（一锁门就把主人关在自己房外）。
        var hub = TerritoryLoopTests.NewGame(out var state, 21);
        var maid = Maid(state);
        maid.Condition.AddFavor(1000); // 女仆与主人同床，空出她那张
        var guest = state.Roster.Add("旅人", false);
        BuildBed(hub, state, Bedroom);
        hub.Place(guest.Id, Parlor);
        hub.Enter(Bedroom);
        for (var i = 0; i < 24 * 6; i++)
        {
            hub.PassTime(10);
            Assert.False(Asleep(hub, guest.Id) && W(hub, guest.Id).RoomId == Bedroom, "旅人睡进了主人的卧室");
            Assert.False(state.Territory.SleeperLocks.ContainsKey(Bedroom));
        }
    }

    [Fact]
    public void Well_stores_water_only()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 17);
        var well = state.Territory.Facilities.Single(f => f.Id == Well);
        var master = state.Roster.Master!;
        Assert.True(well.CanStore);
        Assert.False(state.Territory.Allows(well, "木材"));
        Assert.False(state.Territory.Allows(well, "肉"));
        Assert.True(state.Territory.Allows(well, "水"));
        Assert.Equal(StoragePriority.Critical, well.Priority);
        // 过滤勾成什么样都一样；存储设置也改不了它。
        well.StorageFilter.AllowAll();
        Assert.False(state.Territory.Allows(well, "木材"));
        Assert.False(state.Territory.StorageConfigurable(well));
        // 搬运找仓储不会挑中井：庭院里干活的石材送去客厅的箱子。
        Assert.Equal(Chest, state.Territory.FindStorageFor("石材", preferRoomId: Courtyard)!.Id);
        // 玩家亲手往井里放也放不进。
        hub.Enter(Courtyard);
        Assert.True(hub.OpenStorage(Well));
        Assert.False(hub.StoreOne("木材"));
        Assert.Equal(0, well.Contents.Get("木材"));
        // 水照存。
        master.Bag.Add("水", 1);
        well.Contents.Add("水", -well.Contents.Get("水"));
        Assert.True(hub.StoreOne("水"));
        Assert.Equal(1, well.Contents.Get("水"));
    }

    [Fact]
    public void Day_one_wood_from_the_woodlot_builds_the_maid_a_bed()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 18);
        var master = state.Roster.Master!;
        // 开局的木材另有用处（采石点、猪圈）：清掉，只靠当天伐的木头。
        master.Bag.Add("木材", -master.Bag.Get("木材"));
        var chest = state.Territory.Facilities.Single(f => f.Id == Chest);
        chest.Contents.Add("木材", -chest.Contents.Get("木材"));
        var start = state.Clock.Minutes;

        hub.Enter(Forest);
        Assert.True(hub.Use(Woodlot));
        var swings = 0;
        while (master.Bag.Get("木材") < 10)
        {
            Assert.True(hub.ActAtFixture(ActionKind.Fell));
            swings++;
        }
        Assert.Equal(1, state.Clock.Day);
        _out.WriteLine($"伐木 {swings} 次，{start / 60}:{start % 60:00} → {state.Clock.Minutes / 60}:{state.Clock.Minutes % 60:00}，木材 {master.Bag.Get("木材")}");
        Assert.True(state.Clock.Minutes <= 18 * 60, $"第一天天黑前伐够 10 木材（到 {state.Clock.Minutes / 60} 点）");

        Assert.True(hub.BuildFacilityDef(BedDef, Bedroom), "卧室里给女仆打一张床");
        var bed = state.Territory.Facilities[^1];
        Assert.Equal(Bedroom, bed.RoomId);
        Assert.True(bed.Supports(ActionKind.Sleep));
    }
}
