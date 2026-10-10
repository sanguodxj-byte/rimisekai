using System.Collections.Generic;
using System.Linq;

namespace Rimisekai.Hub;

/// <summary>据点角色卡。战斗形态下界面按威胁等级重新排位（等级越高越靠前）。</summary>
public readonly record struct CharacterCard(
    int Id, string Name, bool IsPlayer, int RoomId, int ThreatTier, int Favor = 0);

public sealed partial class HubSession
{
    public int SelectedCharacterId { get; private set; } = -1;

    public IReadOnlyList<CharacterCard> Party()
    {
        var list = new List<CharacterCard>();
        foreach (var character in State.Roster.Members)
        {
            list.Add(new CharacterCard(
                character.Id,
                character.Name,
                character.IsMaster,
                _presence.GetValueOrDefault(character.Id, -1),
                character.ThreatTier,
                character.Condition.Favor));
        }
        return list;
    }

    /// <summary>此刻在领地里的所有人：住户（<see cref="Party"/>）之后接正在店里进出的访客。地图格的棋子与「此刻」带都按它画。</summary>
    public IReadOnlyList<CharacterCard> Present()
    {
        var list = new List<CharacterCard>(Party());
        foreach (var visit in State.Territory.Visits)
        {
            var visitor = State.Roster.Visitor(visit.CharacterId)!;
            list.Add(new CharacterCard(visitor.Id, visitor.Name, false,
                _presence.GetValueOrDefault(visitor.Id, -1), visitor.ThreatTier, visitor.Condition.Favor));
        }
        return list;
    }

    /// <summary>左下角可选取的头像：主角不可选，只有同房的其他角色（住户在前，路过的访客在后）。</summary>
    public IReadOnlyList<CharacterCard> CardsHere()
    {
        var list = new List<CharacterCard>();
        foreach (var character in State.Roster.Members.Concat(State.Roster.Visitors))
        {
            if (character.IsMaster)
                continue;
            if (_presence.GetValueOrDefault(character.Id, -1) != PlayerRoomId)
                continue;
            list.Add(new CharacterCard(
                character.Id,
                character.Name,
                false,
                PlayerRoomId,
                character.ThreatTier,
                character.Condition.Favor));
        }
        return list;
    }

    public bool Select(int characterId)
    {
        var character = State.Roster.Person(characterId);
        if (character == null || character.IsMaster)
            return false;
        if (_presence.GetValueOrDefault(character.Id, -1) != PlayerRoomId)
            return false;
        SelectedCharacterId = characterId;
        return true;
    }

    public void Place(int characterId, int roomId)
    {
        var character = State.Roster.Find(characterId);
        var room = Room(roomId);
        if (character == null || room == null)
            return;
        // 门锁着、这人进不去（见 Territory.BarsEntry）。
        if (State.Territory.BarsEntry(room, character))
        {
            Write($"{character.Name}进不了{room.Name}——门锁着。");
            return;
        }
        _presence[characterId] = roomId;
        Day.Track(characterId, roomId).RoomId = roomId;
    }

    /// <summary>
    /// 取消选中。界面据此在“交流”与“行动”之间互斥切换。
    /// </summary>
    public void ClearSelection() => SelectedCharacterId = -1;

    /// <summary>
    /// 选中的角色已不在玩家所在房间时取消选中。
    /// 角色自己走开（推进时间时）和玩家走开都走这里，保证右下角
    /// 从“交流”退回“行动”，不会停在“交流 · XX（不在场）”。
    /// </summary>
    public void DropSelectionIfGone()
    {
        var selected = State.Roster.Person(SelectedCharacterId);
        if (selected == null || selected.IsMaster
            || _presence.GetValueOrDefault(selected.Id, -1) != PlayerRoomId)
            SelectedCharacterId = -1;
    }

    /// <summary>是否有选中的交流对象。</summary>
    public bool HasSelection => SelectedCharacterId >= 0;
}
