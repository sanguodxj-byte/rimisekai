using System.Collections.Generic;
using Rimisekai.Housing;
using Rimisekai.Character;

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

    /// <summary>某房间里的设施（排班页选设施用）。</summary>
    public IReadOnlyList<Facility> FixturesIn(int roomId)
    {
        var list = new List<Facility>();
        foreach (var facility in State.Territory.Facilities)
        {
            if (facility.RoomId == roomId)
                list.Add(facility);
        }
        return list;
    }

    /// <summary>当前在该设施上的所有角色卡（玩家及占用中的 NPC），供界面画具体棋子用。</summary>
    public IReadOnlyList<CharacterCard> WorkersAtFixture(int fixtureId)
    {
        var list = new List<CharacterCard>();
        if (UsingFixtureId == fixtureId && State.Roster.Master != null)
        {
            var m = State.Roster.Master;
            list.Add(new CharacterCard(m.Id, m.Name, true, PlayerRoomId, m.ThreatTier, m.Condition.Favor));
        }
        foreach (var worker in Day.Workers)
        {
            if (worker.FacilityId == fixtureId && (worker.Phase == WorkPhase.Working || worker.Path.Count == 0))
            {
                var c = State.Roster.Find(worker.CharacterId);
                if (c != null)
                    list.Add(new CharacterCard(c.Id, c.Name, c.IsMaster, worker.RoomId, c.ThreatTier, c.Condition.Favor));
            }
        }
        return list;
    }

    /// <summary>
    /// 玩家所在这间房是不是私人空间（卧室类）。行动面板据此决定要不要画门锁那个钮。
    /// </summary>
    public bool CurrentRoomIsPrivate() =>
        Room(PlayerRoomId)?.HasTag(Territory.PrivateTag) == true;

    /// <summary>玩家所在这间房此刻锁没锁。</summary>
    public bool CurrentRoomLocked() =>
        Room(PlayerRoomId) is { } room && State.Territory.IsLocked(room);

    /// <summary>
    /// 拧门锁。只能在卧室类房间里操作，且人得在屋里——锁的是"主人自己动手"这件事。
    /// 三档循环：自动 → 手动锁 → 手动解锁 → 自动。
    /// </summary>
    public bool ToggleRoomLock()
    {
        var room = Room(PlayerRoomId);
        if (room == null || !room.HasTag(Territory.PrivateTag))
            return false;
        room.Lock = room.Lock switch
        {
            RoomLock.Auto => RoomLock.Locked,
            RoomLock.Locked => RoomLock.Unlocked,
            _ => RoomLock.Auto,
        };
        var locked = State.Territory.IsLocked(room);
        Write(locked ? $"你把{room.Name}的门锁上了。" : $"你把{room.Name}的门打开了。");
        return true;
    }

    /// <summary>设施名；找不到返回空串。</summary>
    public string FacilityName(int facilityId) =>
        State.Territory.Facilities.Find(f => f.Id == facilityId)?.Name ?? "";

    /// <summary>
    /// 上一次 <see cref="Use"/> 被人拒在外头的那句话（如被睡在床上的人踢下床）；没被拒是空串。
    /// 不进日志，界面弹成提示签。
    /// </summary>
    public string UseRefusal { get; private set; } = "";

    public bool Use(int fixtureId)
    {
        UseRefusal = "";
        var facility = Fixture(fixtureId);
        if (facility == null || facility.RoomId != PlayerRoomId)
            return false;
        // 往有人睡着的床上钻：好感不够同床（Intimacy.SharesBed），被人一脚踢下床，人不上床。
        if (facility.Supports(ActionKind.Sleep) && BedHolderRefusing(facility) is { } holder)
        {
            UseRefusal = $"你刚钻进{facility.Name}，就被{holder.Name}一脚踢了下来。";
            return false;
        }
        LeaveFixture();
        UsingFixtureId = fixtureId;
        PassTime(CostUse * TerritoryClock.StepMinutes);
        Write($"你在{facility.Name}。");
        return true;
    }

    /// <summary>睡在这张床上、又不肯和主人同床的人；没有就是 null。</summary>
    private CharacterState? BedHolderRefusing(Facility bed)
    {
        foreach (var worker in Day.Workers)
        {
            if (worker.FacilityId != bed.Id || worker.Goal != ActionKind.Sleep || worker.Path.Count > 0)
                continue;
            var who = State.Roster.Find(worker.CharacterId);
            if (who != null && !who.IsMaster && !Intimacy.SharesBed(who))
                return who;
        }
        return null;
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
        foreach (var worker in Day.Workers)
        {
            if (worker.FacilityId == fixtureId && (worker.Phase == WorkPhase.Working || worker.Path.Count == 0))
                n++;
        }
        return n;
    }

    private void LeaveFixture()
    {
        UsingFixtureId = null;
        CloseStorage();
    }
}
