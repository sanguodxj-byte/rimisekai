using System.Collections.Generic;
using System.Linq;
using Rimisekai.Defs;
using Rimisekai.Housing;
using Rimisekai.Voice;
using Rimisekai.Clock;

namespace Rimisekai.Hub;

/// <summary>
/// 场景日志：玩家来到一处地方时只写「你来到了X。」；房间定义里的描述原文（RoomDef.Description）
/// 只在「观察四周」时写出。在场角色对玩家的反应（Meet 口上，挑不出就不写——不编兜底句）单独成行，排在角色档。
/// </summary>
public sealed partial class HubSession
{
    /// <summary>
    /// 房间的场景描述原文。只按名字权威检索（DefName/Label），禁止按数值 Id 撞库。
    /// 查不到返回空串。卧室在夜间（20:00~6:00）换成与插画 room_bedroom_night 呼应的夜景描述。
    /// </summary>
    public string SceneDescription(Room room)
    {
        var def = DefDatabase<RoomDef>.Get(room.Name)
               ?? DefDatabase<RoomDef>.All.FirstOrDefault(d => d.Name == room.Name || d.Label == room.Name);
        var desc = def != null && def.Description.Length > 0 ? def.Description : "";
        if ((room.Name.Contains("卧") || room.HasTag("卧室")) && (State.Clock.Hour < 6 || State.Clock.Hour >= 20))
            desc = "月光透过尖拱石窗斜洒在木床上，床幔半掩，粗石壁炉前留有一层静寂的灰烬。";
        if (desc.Length > 0 && !desc.EndsWith("。") && !desc.EndsWith("，"))
            desc += "。";
        return desc;
    }

    /// <summary>
    /// 点格即前往：落位到该房间（同 <see cref="Enter"/>），并写一条场景描述日志。
    /// 已经在这间房里则什么也不写。
    /// </summary>
    /// <summary>
    /// 前往某房间：只能沿连通的门走（<see cref="Territory.Route"/>），锁着进不去的房间走不通（<see cref="PlayerBarred"/>），
    /// 每过一道门花一步的时间；与这里不连通（非连通域）就去不了，返回 false 且不动。
    /// </summary>
    public bool Arrive(int roomId)
    {
        if (roomId == PlayerRoomId)
            return false;
        var target = Room(roomId);
        if (target == null || !target.Open || PendingEncounter != null || !CanReach(roomId))
            return false;
        if (InDungeon)
            return ArriveThroughDungeon(roomId);
        var steps = State.Territory.Route(PlayerRoomId, roomId, barred: PlayerBarred).Count;
        Walk(CostMove * steps * TerritoryClock.StepMinutes);
        LeaveFixture();
        Enter(roomId);
        if (PlayerRoomId != roomId)
            return false;
        WorldEffects.SpendMoveStamina(State.Roster.Master, State.Territory, roomId, State.Weather);
        WriteArrival(roomId);
        return true;
    }

    /// <summary>
    /// 主角眼下能不能走到这间房（沿连通的门，同一领地区内）。
    /// 地城里只认看得见的房：迷雾里的去不了，也不从迷雾里抄近路。
    /// </summary>
    public bool CanReach(int roomId) =>
        roomId == PlayerRoomId || (RoomShown(roomId)
            && State.Territory.Route(PlayerRoomId, roomId, r => RoomShown(r.Id), PlayerBarred).Count > 0);

    /// <summary>主角进不进得了这间房：门锁对主人一视同仁（有人锁门睡下就进不去），见 <see cref="Territory.BarsEntry"/>。</summary>
    public bool PlayerBarred(Room room) =>
        State.Roster.Master is { } master && State.Territory.BarsEntry(room, master);

    /// <summary>这间房连得通、只是被门锁挡着（界面据此说「门锁着」而不是「不连通」）。</summary>
    public bool LockedOut(int roomId) =>
        !CanReach(roomId) && RoomShown(roomId)
        && State.Territory.Route(PlayerRoomId, roomId, r => RoomShown(r.Id), _ => false).Count > 0;

    /// <summary>新开局：主角落脚的那间房照常写一条场景描述（与走进房间同一句），日志不从空白开始。</summary>
    public void WriteOpening() => WriteArrival(PlayerRoomId);

    /// <summary>
    /// 来到某房间的日志（2026-10-09 主人改）：只写「你来到了X。」——房间的详细描述改由「观察四周」写出；
    /// 环境变化照常插在其后；在场角色这一档写他们在做什么，有人对你开口（Meet 口上）就以那句话代替他的行为。
    /// </summary>
    private void WriteArrival(int roomId)
    {
        var room = Room(roomId);
        if (room == null)
            return;
        WriteScene($"你来到了{room.Name}。");
        SeeAround();
        if (MeetReaction(roomId) is { } meet)
            WriteActivity(meet.Who, meet.Line);
    }

    /// <summary>
    /// 在场角色对玩家到来的反应：按名册顺序找第一个挑得出 Meet 口上、且 <see cref="GreetCooldownMinutes"/> 内没打过招呼的人，
    /// 写成「某某说「……」」。谁都没有就返回 null。
    /// </summary>
    /// <summary>同一个人隔这么久（游戏分钟）没打过招呼，才会在你进门时再开口；其余时候只写他在做什么。</summary>
    public const int GreetCooldownMinutes = 120;

    private readonly Dictionary<int, long> _lastGreeted = new();

    private long ClockStamp => (long)State.Clock.Day * 24 * 60 + State.Clock.Minutes;

    private (int Who, string Line)? MeetReaction(int roomId)
    {
        foreach (var who in State.Roster.Members)
        {
            if (who.IsMaster || _presence.GetValueOrDefault(who.Id, -1) != roomId)
                continue;
            if (_lastGreeted.TryGetValue(who.Id, out var at) && ClockStamp - at < GreetCooldownMinutes)
                continue;
            var utterance = PickVoice(who, VoiceTrigger.Meet, kind: VoiceKind.Speech);
            if (utterance == null || utterance.Value.Lines.Count == 0)
                continue;
            _lastGreeted[who.Id] = ClockStamp;
            return (who.Id, $"{utterance.Value.Speaker}说「{string.Join("", utterance.Value.Lines)}」");
        }
        return null;
    }
}
