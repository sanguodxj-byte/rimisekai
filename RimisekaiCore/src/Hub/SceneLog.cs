using System.Collections.Generic;
using System.Linq;
using Rimisekai.Defs;
using Rimisekai.Housing;
using Rimisekai.Voice;
using Rimisekai.Clock;

namespace Rimisekai.Hub;

/// <summary>
/// 场景描述日志：玩家来到一处地方时写一条「a，b」。
/// a＝来到哪里＋该房间定义里的描述原文（RoomDef.Description）；
/// b＝在场角色对玩家的反应（该角色台词包里的 Meet 口上，挑不出就空着——不编兜底句）。
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
    /// 前往某房间：只能沿连通的门走（<see cref="Territory.Route"/>，主人不受门锁所限），
    /// 每过一道门花一步的时间；与这里不连通（非连通域）就去不了，返回 false 且不动。
    /// </summary>
    public bool Arrive(int roomId)
    {
        if (roomId == PlayerRoomId)
            return false;
        var target = Room(roomId);
        if (target == null || !target.Open || !CanReach(roomId))
            return false;
        var steps = State.Territory.Route(PlayerRoomId, roomId, ignoreLocks: true).Count;
        PassTime(CostMove * steps * TerritoryClock.StepMinutes);
        LeaveFixture();
        Enter(roomId);
        if (PlayerRoomId != roomId)
            return false;
        WorldEffects.SpendMoveStamina(State.Roster.Master, State.Territory, roomId, State.Weather);
        WriteArrival(roomId);
        return true;
    }

    /// <summary>主角眼下能不能走到这间房（沿连通的门，同一领地区内）。</summary>
    public bool CanReach(int roomId) =>
        roomId == PlayerRoomId || State.Territory.Route(PlayerRoomId, roomId, ignoreLocks: true).Count > 0;

    /// <summary>写来到某房间的场景日志：a＝「你来到了X。描述」，b＝在场者的 Meet 口上。</summary>
    private void WriteArrival(int roomId)
    {
        var room = Room(roomId);
        if (room == null)
            return;
        var desc = SceneDescription(room);
        var fact = desc.Length > 0 ? $"你来到了{room.Name}。{desc}" : $"你来到了{room.Name}。";
        WriteScene(fact, MeetReaction(roomId));
    }

    /// <summary>
    /// 在场角色对玩家到来的反应：按名册顺序找第一个挑得出 Meet 口上的人，
    /// 写成「某某说「……」」。谁都没有就返回空串。
    /// </summary>
    private string MeetReaction(int roomId)
    {
        foreach (var who in State.Roster.Members)
        {
            if (who.IsMaster || _presence.GetValueOrDefault(who.Id, -1) != roomId)
                continue;
            var utterance = PickVoice(who, VoiceTrigger.Meet, kind: VoiceKind.Speech);
            if (utterance == null || utterance.Value.Lines.Count == 0)
                continue;
            return $"{utterance.Value.Speaker}说「{string.Join("", utterance.Value.Lines)}」";
        }
        return "";
    }
}
