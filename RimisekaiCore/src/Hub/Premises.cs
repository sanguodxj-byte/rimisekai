using System.Collections.Generic;
using Rimisekai.Housing;

namespace Rimisekai.Hub;

public readonly record struct FixtureView(int Id, string Name, int SeatsLeft, int Occupants, bool PlayerHere);

public readonly record struct GuestCard(int Id, string Name, string Purpose);

public sealed partial class HubSession
{
    public const int CostUse = 1;

    public int? UsingFixtureId { get; private set; }

    public IReadOnlyList<FixtureView> Here()
    {
        var list = new List<FixtureView>();
        foreach (var facility in State.Territory.Facilities)
        {
            if (facility.RoomId != PlayerRoomId)
                continue;
            var occupants = Occupants(facility.Id);
            list.Add(new FixtureView(facility.Id, facility.Name, facility.Capacity - occupants, occupants, UsingFixtureId == facility.Id));
        }
        return list;
    }

    public bool Use(int fixtureId)
    {
        var facility = Fixture(fixtureId);
        if (facility == null || facility.RoomId != PlayerRoomId || Occupants(fixtureId) >= facility.Capacity)
            return false;
        LeaveFixture();
        UsingFixtureId = fixtureId;
        PassTime(CostUse * TerritoryClock.StepMinutes);
        Write($"你在{facility.Name}。");
        return true;
    }

    /// <summary>
    /// 玩家当前所坐设施支持的行动，按枚举顺序。没坐设施时为空表。
    /// 界面右下角据此列出“在这件东西上能做什么”，与 Core 的判定同源，
    /// 因此不会出现“按钮画得出来却点不动”。
    /// </summary>
    public IReadOnlyList<ActionKind> ActionsAtCurrentFixture()
    {
        var list = new List<ActionKind>();
        var facility = Fixture(UsingFixtureId ?? -1);
        if (facility == null)
            return list;
        foreach (var action in System.Enum.GetValues<ActionKind>())
        {
            if (facility.Supports(action))
                list.Add(action);
        }
        return list;
    }

    /// <summary>玩家所在房间支持的行动并集（房内设施各自行动集的并）。</summary>
    public IReadOnlyList<ActionKind> ActionsInCurrentRoom() =>
        PlayerRoomId < 0
            ? new List<ActionKind>()
            : State.Territory.RoomActions(PlayerRoomId);

    public IReadOnlyList<GuestCard> GuestsHere()
    {
        var list = new List<GuestCard>();
        foreach (var guest in State.Territory.Guests)
        {
            if (guest.RoomId == PlayerRoomId)
                list.Add(new GuestCard(guest.Id, guest.Name, guest.Purpose));
        }
        return list;
    }

    private int Occupants(int fixtureId)
    {
        var n = UsingFixtureId == fixtureId ? 1 : 0;
        return n;
    }

    private void LeaveFixture()
    {
        UsingFixtureId = null;
        CloseStorage();
    }
}
