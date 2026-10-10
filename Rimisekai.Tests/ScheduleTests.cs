using System.Linq;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>日程（工作 / 空闲）排下去之后的实际效果。</summary>
public class ScheduleTests
{
    private const int HerbBush = 6, Woodlot = 11, IronVein = 7, Courtyard = 1, Forest = 4;

    [Fact]
    public void Master_walking_away_during_work_is_not_pulled_back_in_one_step()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 7);
        var master = state.Roster.Master!;
        Assert.True(hub.Assign(master.Id, state.Clock.Slot, SlotMode.Work, HerbBush));
        hub.PassTime(60);
        Assert.Equal(Forest, hub.PlayerRoomId);

        Assert.True(hub.Arrive(Courtyard));
        hub.PassTime(TerritoryClock.StepMinutes);
        var route = state.Territory.Route(Courtyard, Forest, barred: null);
        Assert.True(hub.PlayerRoomId == Courtyard || hub.PlayerRoomId == route.First(),
            $"五分钟最多走一格，实际在 {hub.PlayerRoomId}");
    }

    [Fact]
    public void Master_starts_next_work_slot_from_where_the_player_left_him()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 7);
        var master = state.Roster.Master!;
        Assert.True(hub.Assign(master.Id, state.Clock.Slot, SlotMode.Work, HerbBush));
        hub.PassTime(60);
        Assert.True(hub.Assign(master.Id, state.Clock.Slot, SlotMode.Free));
        hub.PassTime(10);
        Assert.True(hub.Arrive(Courtyard));
        Assert.Equal(Courtyard, hub.Day.Workers.Single(w => w.CharacterId == master.Id).RoomId);
    }

    [Fact]
    public void Removing_a_facility_frees_the_slots_assigned_to_it()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 7);
        var maid = TerritoryLoopTests.Maid(state);
        Assert.True(hub.Assign(maid.Id, 1, SlotMode.Work, HerbBush));
        Assert.True(hub.RemoveFacility(HerbBush));
        var a = hub.AssignmentOf(maid.Id, 1);
        Assert.Equal(SlotMode.Free, a.Mode);
        Assert.Equal(-1, a.FacilityId);
    }

    [Fact]
    public void Night_slot_work_is_done_instead_of_sleeping()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 7);
        var maid = TerritoryLoopTests.Maid(state);
        var master = state.Roster.Master!;
        Assert.True(hub.Assign(maid.Id, 0, SlotMode.Work, Woodlot));
        Assert.True(hub.Assign(master.Id, 0, SlotMode.Work, IronVein));
        while (state.Clock.Day == 1)
            hub.PassTime(30);
        var maidFelled = false;
        var masterMined = false;
        while (state.Clock.Minutes < 6 * 60 - 30)
        {
            hub.PassTime(30);
            maidFelled |= hub.Day.Workers.Single(w => w.CharacterId == maid.Id).Goal == ActionKind.Fell;
            var mw = hub.Day.Workers.Single(w => w.CharacterId == master.Id);
            Assert.NotEqual(ActionKind.Sleep, mw.Goal);
            masterMined |= mw.Goal == ActionKind.Mine;
        }
        Assert.True(maidFelled, "女仆夜里排了伐木，应该起来去林场");
        Assert.True(masterMined, "主人夜里排了采矿，应该在矿脉干活");
    }
}
