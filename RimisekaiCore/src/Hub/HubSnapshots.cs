using System.Collections.Generic;

namespace Rimisekai.Hub;

/// <summary>主界面可存档的部分。地盘和名册存在 GameState 里，这里只存位置和日志。</summary>
public sealed class HubSnapshot
{
    public int Region { get; set; }
    public int PlayerRoom { get; set; } = -1;
    public int Selected { get; set; } = -1;
    public Dictionary<int, int> Presence { get; set; } = new();
    public List<string> Log { get; set; } = new();
}

public sealed partial class HubSession
{
    public HubSnapshot Snapshot() => new()
    {
        Region = RegionId,
        PlayerRoom = PlayerRoomId,
        Selected = SelectedCharacterId,
        Presence = new Dictionary<int, int>(_presence),
        Log = _log.ConvertAll(l => l.Text),
    };

    public void Restore(HubSnapshot snapshot)
    {
        SelectRegion(snapshot.Region);
        foreach (var pair in snapshot.Presence)
            Place(pair.Key, pair.Value);
        if (snapshot.PlayerRoom >= 0)
            Enter(snapshot.PlayerRoom);
        if (snapshot.Selected >= 0)
            Select(snapshot.Selected);
        foreach (var text in snapshot.Log)
            Write(text);
    }
}
