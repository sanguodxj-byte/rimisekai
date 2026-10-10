using System.Linq;
using Rimisekai.Character;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Save;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 门锁（2026-10-10 主人定）：门锁对主人一视同仁；主人能锁的只有自己的房间（摆着主人的床那间）。
/// 自动＝主人不在屋里或在睡时锁、只放女仆；手动锁＝主人在屋里时谁都进不来（女仆也不例外），主人一出门回到自动；
/// 手动敞开＝谁都进得来。
/// </summary>
public class RoomLockTests
{
    private const int Bedroom = 1, Hall = 2, Bed = 10;

    private static (GameState State, CharacterState Master, CharacterState Maid, CharacterState Guest) Setup()
    {
        var state = new GameState();
        var master = state.Roster.Add("你", master: true);
        var maid = state.Roster.Add("女仆");
        maid.Grant(Trait.Maid);
        var guest = state.Roster.Add("客人");
        var t = state.Territory;
        var bedroom = new Room { Id = Bedroom, Name = "卧室", Open = true };
        bedroom.AddTag("室内");
        var hall = new Room { Id = Hall, Name = "客厅", Open = true };
        hall.AddTag("室内");
        t.AddRoom(bedroom);
        t.AddRoom(hall);
        t.Link(Bedroom, Hall);
        var bed = new Facility { Id = Bed, Name = "床", RoomId = Bedroom, Capacity = 2, Built = true };
        bed.Actions.Add(ActionKind.Sleep);
        Assert.True(t.AddFacility(bed));
        t.MasterBedId = Bed;
        return (state, master, maid, guest);
    }

    [Fact]
    public void Auto_lock_keeps_strangers_out_of_the_masters_room_but_lets_the_maid_in()
    {
        var (state, master, maid, guest) = Setup();
        var t = state.Territory;
        var room = t.Rooms.Single(r => r.Id == Bedroom);
        Assert.Equal(Bedroom, t.MasterBedroomId);

        t.MasterRoomId = Hall;                   // 主人不在
        Assert.True(t.IsLocked(room));
        Assert.True(t.BarsEntry(room, guest));
        Assert.False(t.BarsEntry(room, maid));
        Assert.False(t.BarsEntry(room, master));

        t.MasterRoomId = Bedroom;                // 主人在屋里醒着
        Assert.False(t.BarsEntry(room, guest));

        t.MasterAsleep = true;                   // 主人睡下
        Assert.True(t.BarsEntry(room, guest));
        Assert.False(t.BarsEntry(room, maid));
    }

    [Fact]
    public void Manual_lock_keeps_everyone_else_out_including_the_maid()
    {
        var (state, master, maid, guest) = Setup();
        var t = state.Territory;
        var room = t.Rooms.Single(r => r.Id == Bedroom);
        t.MasterRoomId = Bedroom;
        room.Lock = RoomLock.Locked;
        Assert.True(t.BarsEntry(room, guest));
        Assert.True(t.BarsEntry(room, maid));
        Assert.False(t.BarsEntry(room, master));
        // 路也走不通。
        Assert.Empty(t.Route(Hall, Bedroom, barred: r => t.BarsEntry(r, maid)));

        room.Lock = RoomLock.Unlocked;           // 敞开：主人不在也进得去
        t.MasterRoomId = Hall;
        Assert.False(t.BarsEntry(room, guest));
        Assert.False(t.IsLocked(room));
    }

    [Fact]
    public void Only_the_masters_own_room_has_a_lock()
    {
        var (state, _, maid, guest) = Setup();
        var t = state.Territory;
        var hall = t.Rooms.Single(r => r.Id == Hall);
        hall.Lock = RoomLock.Locked;             // 硬拧也没用——不是主人的房间（客厅里的饭谁都够得着）
        Assert.False(t.IsLocked(hall));
        Assert.False(t.BarsEntry(hall, guest));
        Assert.False(t.BarsEntry(hall, maid));
    }

    [Fact]
    public void Sleeper_locks_bar_the_master_too_unless_the_sleeper_would_share_his_bed()
    {
        var (state, master, _, guest) = Setup();
        var t = state.Territory;
        var hall = t.Rooms.Single(r => r.Id == Hall);
        t.SleeperLocks[Hall] = new Territory.SleeperLock(guest.Id, AdmitsMaster: false);
        Assert.True(t.BarsEntry(hall, master));
        Assert.False(t.BarsEntry(hall, guest));
        Assert.Empty(t.Route(Bedroom, Hall, barred: r => t.BarsEntry(r, master)));
        t.SleeperLocks[Hall] = new Territory.SleeperLock(guest.Id, AdmitsMaster: true);
        Assert.False(t.BarsEntry(hall, master));
    }

    [Fact]
    public void Toggle_works_only_in_the_masters_room_cycles_three_states_and_reverts_on_leaving()
    {
        var (state, _, _, _) = Setup();
        var room = state.Territory.Rooms.Single(r => r.Id == Bedroom);
        var hub = new HubSession(state);

        hub.Enter(Hall);
        Assert.False(hub.CurrentRoomLockable());
        Assert.False(hub.ToggleRoomLock());

        hub.Enter(Bedroom);
        Assert.True(hub.CurrentRoomLockable());
        Assert.Equal("门·自动", hub.RoomLockLabel());
        Assert.True(hub.ToggleRoomLock());
        Assert.Equal(RoomLock.Locked, room.Lock);
        Assert.Equal("门·锁着", hub.RoomLockLabel());
        Assert.True(hub.CurrentRoomLocked());
        Assert.True(hub.ToggleRoomLock());
        Assert.Equal(RoomLock.Unlocked, room.Lock);
        Assert.False(hub.CurrentRoomLocked());
        Assert.True(hub.ToggleRoomLock());
        Assert.Equal(RoomLock.Auto, room.Lock);

        // 锁上后出门：回到自动，不会把女仆的床长期锁死。
        Assert.True(hub.ToggleRoomLock());
        hub.Enter(Hall);
        Assert.Equal(RoomLock.Auto, room.Lock);
    }

    [Fact]
    public void Placing_people_respects_the_lock()
    {
        var (state, _, maid, guest) = Setup();
        var hub = new HubSession(state);
        hub.Enter(Hall);                         // 主人不在卧室：自动锁
        hub.Place(guest.Id, Bedroom);
        hub.Place(maid.Id, Bedroom);
        Assert.Equal(Bedroom, hub.Party().Single(c => c.Id == maid.Id).RoomId);
        Assert.NotEqual(Bedroom, hub.Party().Single(c => c.Id == guest.Id).RoomId);

        hub.Place(maid.Id, Hall);
        hub.Enter(Bedroom);
        Assert.True(hub.ToggleRoomLock());       // 主人在屋里把门锁死
        hub.Place(maid.Id, Bedroom);
        Assert.NotEqual(Bedroom, hub.Party().Single(c => c.Id == maid.Id).RoomId);
    }

    [Fact]
    public void Lock_survives_save_and_load()
    {
        var (state, _, _, _) = Setup();
        var hub = new HubSession(state);
        hub.Enter(Bedroom);
        Assert.True(hub.ToggleRoomLock());
        Assert.True(hub.ToggleRoomLock());       // 敞开
        var data = System.Text.Json.JsonSerializer.Deserialize<SaveData>(SaveSystem.Save(state, hub))!;
        var loaded = SaveSystem.Restore(data);
        Assert.Equal(RoomLock.Unlocked, loaded.Territory.Rooms.Single(r => r.Id == Bedroom).Lock);
        Assert.Equal(Bed, loaded.Territory.MasterBedId);
    }
}
