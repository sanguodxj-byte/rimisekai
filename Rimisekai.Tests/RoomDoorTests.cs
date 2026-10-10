using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Save;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>房间朝向与门：只有连通的门能走，非连通域过不去；建造里逐面开 / 封门。</summary>
public sealed class RoomDoorTests
{
    private static (GameState State, HubSession Hub) Fixture()
    {
        var state = new GameState { Money = 100 };
        state.Roster.Add("你", master: true);
        // 甲(0,0) — 乙(1,0) — 丙(2,0)，丁(0,1) 在甲之南但不连通。
        state.Territory.AddRoom(new Room { Id = 1, Name = "甲", X = 0, Y = 0, Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "乙", X = 1, Y = 0, Open = true });
        state.Territory.AddRoom(new Room { Id = 3, Name = "丙", X = 2, Y = 0, Open = true });
        state.Territory.AddRoom(new Room { Id = 4, Name = "丁", X = 0, Y = 1, Open = true });
        state.Territory.Link(1, 2);
        state.Territory.Link(2, 3);
        var hub = new HubSession(state);
        hub.Enter(1);
        return (state, hub);
    }

    [Fact]
    public void Doors_follow_grid_direction()
    {
        var (state, _) = Fixture();
        var a = state.Territory.Room(1)!;
        Assert.True(state.Territory.DoorOpen(a, RoomDir.East));
        Assert.False(state.Territory.DoorOpen(a, RoomDir.South));
        Assert.Null(state.Territory.NeighborAt(a, RoomDir.North));
        Assert.Equal(4, state.Territory.NeighborAt(a, RoomDir.South)!.Id);
    }

    [Fact]
    public void Arrive_only_through_connected_doors()
    {
        var (_, hub) = Fixture();
        Assert.False(hub.CanReach(4));
        Assert.False(hub.Arrive(4));
        Assert.Equal(1, hub.PlayerRoomId);
        Assert.True(hub.Arrive(3));
        Assert.Equal(3, hub.PlayerRoomId);
    }

    [Fact]
    public void SetDoor_opens_and_seals_walls()
    {
        var (state, hub) = Fixture();
        Assert.True(hub.SetDoor(1, RoomDir.South, true));
        Assert.True(hub.CanReach(4));
        Assert.True(hub.SetDoor(2, RoomDir.West, false));
        Assert.False(state.Territory.DoorOpen(state.Territory.Room(1)!, RoomDir.East));
        Assert.False(hub.CanReach(3));
        Assert.False(hub.SetDoor(1, RoomDir.North, true));
    }
}
