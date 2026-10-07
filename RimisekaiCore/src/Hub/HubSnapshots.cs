using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Save;

namespace Rimisekai.Hub;

/// <summary>主界面可存档的部分。地盘和名册存在 GameState 里，这里只存位置、日志与赶集记录。</summary>
public sealed class HubSnapshot
{
    public int Region { get; set; }
    public int PlayerRoom { get; set; } = -1;
    public int Selected { get; set; } = -1;

    /// <summary>出发去兴趣点前站的本家房间；-1 = 没出过门。</summary>
    public int TerritoryHomeRoom { get; set; } = -1;
    public Dictionary<int, int> Presence { get; set; } = new();
    public List<string> Log { get; set; } = new();

    /// <summary>最近一次出发去集市的日期（按出发日计，0 点刷新次数）。-1 = 还没去过。</summary>
    public int MarketSettledDay { get; set; } = -1;

    /// <summary>
    /// 定时事件的暂存演员：已掷好但还没登场，因此不在名册里，
    /// 只能随会话快照单独存一份（名册那份存不到他们）。
    /// </summary>
    public List<StagedActorData> StagedActors { get; set; } = new();
}

public sealed partial class HubSession
{
    public HubSnapshot Snapshot() => new()
    {
        Region = RegionId,
        PlayerRoom = PlayerRoomId,
        Selected = SelectedCharacterId,
        TerritoryHomeRoom = _territoryHomeRoomId,
        Presence = new Dictionary<int, int>(_presence),
        Log = _log.ConvertAll(l => l.Text),
        MarketSettledDay = MarketSettledDay,
        StagedActors = CaptureStagedActors(),
    };

    public void Restore(HubSnapshot snapshot)
    {
        _territoryHomeRoomId = snapshot.TerritoryHomeRoom;
        SelectRegion(snapshot.Region);
        foreach (var pair in snapshot.Presence)
            Place(pair.Key, pair.Value);
        if (snapshot.PlayerRoom >= 0)
            Enter(snapshot.PlayerRoom);
        if (snapshot.Selected >= 0)
            Select(snapshot.Selected);
        foreach (var text in snapshot.Log)
            Write(text);
        // 读档即人在据点：在集市状态是行程中的临时态，不进存档。
        AtMarket = false;
        MarketSettledDay = snapshot.MarketSettledDay;
        RestoreStagedActors(snapshot.StagedActors);
        // 读档后补排班：暂存演员已在，这里只把还没生成的行重新排进后台队列。
        InitializeScheduledEvents();
    }

    /// <summary>暂存演员落成存档行。</summary>
    private List<StagedActorData> CaptureStagedActors()
    {
        var list = new List<StagedActorData>();
        foreach (var pair in _staged)
        {
            State.Voice.Generation.Personas.TryGetValue(pair.Value.Name, out var persona);
            list.Add(new StagedActorData
            {
                Scene = pair.Key,
                Actor = SaveSystem.CaptureMember(pair.Value),
                Persona = persona ?? "",
            });
        }
        return list;
    }

    /// <summary>读档还原暂存演员。人设一并补回生成层，后台才能继续按身份写台词。</summary>
    private void RestoreStagedActors(List<StagedActorData>? staged)
    {
        if (staged == null)
            return;
        foreach (var entry in staged)
        {
            var actor = SaveSystem.RestoreMember(entry.Actor);
            _staged[entry.Scene] = actor;
            if (entry.Persona.Length > 0)
                State.Voice.Generation.Personas[actor.Name] = entry.Persona;
        }
    }
}
