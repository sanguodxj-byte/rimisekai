using System.Linq;
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
    public Dictionary<int, int> Presence { get; set; } = new();
    /// <summary>近期日志（两段式，最旧在前）。</summary>
    public List<LogEntryData> Log { get; set; } = new();

    /// <summary>
    /// 定时事件的暂存演员：已掷好但还没登场，因此不在名册里，
    /// 只能随会话快照单独存一份（名册那份存不到他们）。
    /// </summary>
    public List<StagedActorData> StagedActors { get; set; } = new();
}

/// <summary>一条日志的存档行。</summary>
public sealed class LogEntryData
{
    public LogKind Kind { get; set; }
    public string Fact { get; set; } = "";
    public string Feel { get; set; } = "";
    public int Day { get; set; }
    public int Minutes { get; set; }
    /// <summary>角色档那一行是谁的；旧档没有这一项，读出来是 -1（不按人筛）。</summary>
    public int Who { get; set; } = -1;
}

public sealed partial class HubSession
{
    /// <summary>
    /// 会话快照。读档即人在据点：人在兴趣点里或走在大地图上时，记成出发前的本家落脚点，
    /// 落在兴趣点房里（或随主人出门）的在场记录一并改记到那里。
    /// </summary>
    public HubSnapshot Snapshot()
    {
        var away = Layer != MapLayer.Territory;
        var home = away ? _territoryHomeRoomId : PlayerRoomId;
        var homeRegion = !away ? RegionId : home >= 0 ? State.Territory.Rooms.Find(r => r.Id == home)!.RegionId : 0;
        var presence = new Dictionary<int, int>();
        foreach (var pair in _presence)
            presence[pair.Key] = pair.Value >= PoiRoomIdBase || (away && pair.Value < 0) ? home : pair.Value;
        return Build(homeRegion, home, presence);
    }

    private HubSnapshot Build(int region, int playerRoom, Dictionary<int, int> presence) => new()
    {
        Region = region,
        PlayerRoom = playerRoom,
        Selected = SelectedCharacterId,
        Presence = presence,
        Log = History.Select(l => new LogEntryData { Kind = l.Kind, Fact = l.Fact, Feel = l.Feel, Day = l.Day, Minutes = l.Minutes, Who = l.Who }).ToList(),
        StagedActors = CaptureStagedActors(),
    };

    public void Restore(HubSnapshot snapshot)
    {
        SelectRegion(snapshot.Region);
        // 在场记录原样落回，不走 Place 的进门规则：存档时人就在那里（例如睡在主人不在、自动上锁的卧室里），
        // 读档按门锁再筛一遍会把人从地图上抹掉（在场 -1，从此哪儿也去不了）。
        foreach (var pair in snapshot.Presence)
        {
            _presence[pair.Key] = pair.Value;
            Day.Track(pair.Key, pair.Value).RoomId = pair.Value;
        }
        if (snapshot.PlayerRoom >= 0)
            Enter(snapshot.PlayerRoom);
        if (snapshot.Selected >= 0)
            Select(snapshot.Selected);
        _book.Restore(snapshot.Log.Select(l => new LogEntry(l.Kind, l.Fact, l.Feel, l.Day, l.Minutes, l.Who)));
        // 读档即人在据点：在集市状态是行程中的临时态，不进存档。
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
                EventId = pair.Key,
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
            _staged[entry.EventId] = actor;
            if (entry.Persona.Length > 0)
                State.Voice.Generation.Personas[actor.Name] = entry.Persona;
        }
    }
}
