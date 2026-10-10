using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Save;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 私人空间（卧室类）的门锁：主人不在屋内或睡觉时自动锁，
/// 手动拧的锁压过自动规则；锁只挡别人，主人自己随时进得去。
/// </summary>
public class PrivateSpaceTests
{
    /// <summary>一间客卧（私人空间）+ 一间客厅，连在一起。</summary>
    private static Territory WithTerritory()
    {
        var t = new Territory();
        var bedroom = new Room { Id = 1, Name = "客卧", Open = true };
        bedroom.AddTag("室内");
        bedroom.AddTag("卧室");
        bedroom.AddTag(Territory.PrivateTag);
        var hall = new Room { Id = 2, Name = "客厅", Open = true };
        hall.AddTag("室内");
        t.AddRoom(bedroom);
        t.AddRoom(hall);
        t.Link(1, 2);
        return t;
    }

    private static (GameState State, CharacterState Guest) Setup()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var guest = state.Roster.Add("客人");
        return (state, guest);
    }

    /// <summary>在玩家领地里造一间客卧（私人空间）加一间客厅。</summary>
    private static Territory AddRooms(GameState state)
    {
        var t = state.Territory;
        var bedroom = new Room { Id = 1, Name = "客卧", Open = true };
        bedroom.AddTag("室内");
        bedroom.AddTag("卧室");
        bedroom.AddTag(Territory.PrivateTag);
        var hall = new Room { Id = 2, Name = "客厅", Open = true };
        hall.AddTag("室内");
        t.AddRoom(bedroom);
        t.AddRoom(hall);
        t.Link(1, 2);
        return t;
    }

    [Fact]
    public void Private_room_locks_when_master_is_out()
    {
        var t = WithTerritory();
        t.MasterRoomId = 2;                      // 主人在客厅
        Assert.True(t.IsLocked(t.Rooms[0]));
        t.MasterRoomId = 1;                      // 主人进屋
        Assert.False(t.IsLocked(t.Rooms[0]));
    }

    [Fact]
    public void Private_room_locks_while_master_sleeps_inside()
    {
        var t = WithTerritory();
        t.MasterRoomId = 1;
        Assert.False(t.IsLocked(t.Rooms[0]));
        t.MasterAsleep = true;                   // 主人在屋里睡下
        Assert.True(t.IsLocked(t.Rooms[0]));
        t.MasterAsleep = false;                  // 醒了
        Assert.False(t.IsLocked(t.Rooms[0]));
    }

    [Fact]
    public void Manual_lock_overrides_the_automatic_rule()
    {
        var t = WithTerritory();
        var bedroom = t.Rooms[0];
        t.MasterRoomId = 1;                      // 本该是开的

        bedroom.Lock = RoomLock.Locked;          // 手动拧锁：人在屋里也锁
        Assert.True(t.IsLocked(bedroom));

        bedroom.Lock = RoomLock.Unlocked;        // 手动开锁：人不在也不锁
        t.MasterRoomId = 2;
        Assert.False(t.IsLocked(bedroom));

        bedroom.Lock = RoomLock.Auto;
        t.MasterRoomId = 2;
        Assert.True(t.IsLocked(bedroom));        // 回到自动
    }

    [Fact]
    public void Rooms_without_the_private_tag_never_lock()
    {
        var t = WithTerritory();
        t.MasterRoomId = 1;
        t.MasterAsleep = true;
        t.Rooms[1].Lock = RoomLock.Locked;       // 硬拧也没用——不是私室
        Assert.False(t.IsLocked(t.Rooms[1]));
    }

    [Fact]
    public void Locked_room_is_impassable_for_routing()
    {
        var t = WithTerritory();
        t.MasterRoomId = 2;
        // 主人在客厅 → 客卧锁着 → 从客厅走不过去
        Assert.Empty(t.Route(2, 1));
        t.MasterRoomId = 1;
        Assert.Equal(new[] { 1 }, t.Route(2, 1));
    }

    [Fact]
    public void Toggle_cycles_three_states_and_only_works_inside_the_room()
    {
        var (state, _) = Setup();
        AddRooms(state);
        var t = state.Territory;
        var hub = new HubSession(state);

        hub.Enter(2);                            // 人在客厅
        Assert.False(hub.ToggleRoomLock());      // 不在私室里，拧不动
        Assert.Equal(RoomLock.Auto, t.Rooms[0].Lock);

        hub.Enter(1);                            // 进屋
        Assert.True(hub.ToggleRoomLock());       // 自动 → 手动锁
        Assert.Equal(RoomLock.Locked, t.Rooms[0].Lock);
        Assert.True(hub.CurrentRoomIsPrivate());
        Assert.True(hub.CurrentRoomLocked());

        Assert.True(hub.ToggleRoomLock());       // 手动锁 → 手动解锁
        Assert.Equal(RoomLock.Unlocked, t.Rooms[0].Lock);
        Assert.False(hub.CurrentRoomLocked());

        Assert.True(hub.ToggleRoomLock());       // 手动解锁 → 自动
        Assert.Equal(RoomLock.Auto, t.Rooms[0].Lock);
    }

    [Fact]
    public void Others_cannot_be_placed_into_a_locked_room()
    {
        var (state, guest) = Setup();
        AddRooms(state);
        var t = state.Territory;
        var hub = new HubSession(state);

        hub.Enter(1);                            // 主人在屋里
        t.MasterAsleep = true;                   // 睡下 → 门锁着
        hub.Place(guest!.Id, 1);                 // 想把人塞进锁着的卧室
        Assert.Empty(hub.CardsHere());

        t.MasterAsleep = false;                  // 醒了，门开了
        hub.Place(guest!.Id, 1);
        Assert.Single(hub.CardsHere());
    }

    [Fact]
    public void The_master_can_always_enter_their_own_private_room()
    {
        var (state, _) = Setup();
        AddRooms(state);
        var t = state.Territory;
        var hub = new HubSession(state);
        t.MasterRoomId = 2;                      // 门锁着的状态
        t.MasterAsleep = true;

        hub.Enter(1);                            // 主人自己进得去自家卧室
        Assert.Equal(1, hub.PlayerRoomId);
    }
}
