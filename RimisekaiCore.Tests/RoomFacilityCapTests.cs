using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Save;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>一间房最多 <see cref="Room.MaxFacilities"/> 件设施：界面一排设施牌摆得下几块，房间表就只许放几件。</summary>
public sealed class RoomFacilityCapTests
{
    private static (GameState State, HubSession Hub) Fixture()
    {
        var state = new GameState { Money = 100000 };
        state.Roster.Add("你", master: true);
        state.Territory.AddRoom(new Room { Id = 1, Name = "甲", X = 0, Y = 0, Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "乙", X = 1, Y = 0, Open = true });
        state.Territory.Link(1, 2);
        var hub = new HubSession(state);
        hub.Enter(1);
        return (state, hub);
    }

    private static Facility Make(int id, int roomId) =>
        new() { Id = id, Name = $"设施{id}", RoomId = roomId, Built = true, Capacity = 1 };

    private static void Fill(GameState state, int roomId, int firstId)
    {
        for (var i = 0; i < Room.MaxFacilities; i++)
            Assert.True(state.Territory.AddFacility(Make(firstId + i, roomId)));
    }

    [Fact]
    public void Cap_matches_one_row_of_facility_plaques()
    {
        Assert.Equal(3, Room.MaxFacilities);
    }

    [Fact]
    public void Add_refuses_past_cap()
    {
        var (state, _) = Fixture();
        Fill(state, 1, 10);
        Assert.False(state.Territory.HasFacilitySlot(1));
        Assert.False(state.Territory.AddFacility(Make(99, 1)));
        Assert.Equal(Room.MaxFacilities, state.Territory.FacilityCount(1));
        Assert.True(state.Territory.AddFacility(Make(99, 2)));
    }

    [Fact]
    public void Place_and_move_refuse_into_full_room()
    {
        var (state, hub) = Fixture();
        Fill(state, 1, 10);
        var loose = Make(50, -1);
        Assert.True(state.Territory.AddUnplacedFacility(loose));
        Assert.False(hub.PlaceFacility(50, 1));
        Assert.Equal(-1, loose.RoomId);
        Assert.True(hub.PlaceFacility(50, 2));

        Assert.False(hub.MoveFacility(50, 1));
        Assert.Equal(2, loose.RoomId);
    }

    [Fact]
    public void Build_into_full_room_spends_nothing()
    {
        var (state, hub) = Fixture();
        Fill(state, 1, 10);
        state.Roster.Master!.Bag.Add("木材", 50);
        var before = state.Territory.Facilities.Count;
        Assert.False(hub.BuildFacilityDef(1001, 1));
        Assert.Equal(50, state.Roster.Master!.Bag.Get("木材"));
        Assert.Equal(before, state.Territory.Facilities.Count);
    }
}
