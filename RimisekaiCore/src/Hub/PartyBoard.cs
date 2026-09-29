using System.Collections.Generic;

namespace Rimisekai.Hub;

public readonly record struct CharacterCard(int Id, string Name, bool IsPlayer, int RoomId);

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
                _presence.GetValueOrDefault(character.Id, -1)));
        }
        return list;
    }

    /// <summary>左下角可选取的头像：主角不可选，只有同房的其他角色。</summary>
    public IReadOnlyList<CharacterCard> CardsHere()
    {
        var list = new List<CharacterCard>();
        foreach (var character in State.Roster.Members)
        {
            if (character.IsMaster)
                continue;
            if (_presence.GetValueOrDefault(character.Id, -1) != PlayerRoomId)
                continue;
            list.Add(new CharacterCard(character.Id, character.Name, false, PlayerRoomId));
        }
        return list;
    }

    public bool Select(int characterId)
    {
        var character = State.Roster.Find(characterId);
        if (character == null || character.IsMaster)
            return false;
        if (_presence.GetValueOrDefault(character.Id, -1) != PlayerRoomId)
            return false;
        SelectedCharacterId = characterId;
        return true;
    }

    public void Place(int characterId, int roomId)
    {
        if (State.Roster.Find(characterId) == null || Room(roomId) == null)
            return;
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
        var selected = State.Roster.Find(SelectedCharacterId);
        if (selected == null || selected.IsMaster
            || _presence.GetValueOrDefault(selected.Id, -1) != PlayerRoomId)
            SelectedCharacterId = -1;
    }

    /// <summary>是否有选中的交流对象。</summary>
    public bool HasSelection => SelectedCharacterId >= 0;
}
