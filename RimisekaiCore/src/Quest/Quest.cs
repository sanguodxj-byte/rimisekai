using System.Collections.Generic;
using Rimisekai.Character;

namespace Rimisekai.Quest;

/// <summary>
/// 任务形态。MAP/GMAP 进地图探索会话，Dialogue 只跑一段事件后返回。
/// </summary>
public enum QuestKind
{
    Map,
    GraphicalMap,
    Dialogue,
}

public sealed class QuestDef
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public QuestKind Kind { get; init; }
    public int CooldownDays { get; init; }
}

public sealed class MapNode
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public List<int> Links { get; init; } = new();
    public List<int> EventIds { get; init; } = new();
}

public sealed class QuestMap
{
    public int QuestId { get; init; }
    public List<MapNode> Nodes { get; init; } = new();
}

/// <summary>一次任务的运行时。旗标在任务内，通关次数和冷却跨任务保留。</summary>
public sealed class QuestRun
{
    public int QuestId { get; }
    public QuestKind Kind { get; }
    public List<int> PartyIds { get; } = new();
    public int CurrentNodeId { get; set; } = -1;
    public HashSet<int> Explored { get; } = new();
    public Dictionary<int, int> Flags { get; } = new();
    public bool Finished { get; private set; }

    public QuestRun(QuestDef def)
    {
        QuestId = def.Id;
        Kind = def.Kind;
    }

    public bool MoveTo(QuestMap map, int nodeId)
    {
        var node = map.Nodes.Find(n => n.Id == nodeId);
        if (node == null)
            return false;
        if (CurrentNodeId >= 0)
        {
            var here = map.Nodes.Find(n => n.Id == CurrentNodeId);
            if (here == null || !here.Links.Contains(nodeId))
                return false;
        }
        CurrentNodeId = nodeId;
        Explored.Add(nodeId);
        return true;
    }

    public void Finish() => Finished = true;
}

public sealed class QuestRecord
{
    private readonly Dictionary<int, QuestDef> _defs = new();
    public Dictionary<int, int> ClearCount { get; } = new();
    public Dictionary<int, int> CooldownRemaining { get; } = new();

    public void Register(QuestDef def) => _defs[def.Id] = def;

    public QuestDef? Find(int id) => _defs.TryGetValue(id, out var d) ? d : null;

    public bool IsAvailable(int id) =>
        _defs.ContainsKey(id) && (!CooldownRemaining.TryGetValue(id, out var c) || c <= 0);

    public QuestRun? Start(int id, IReadOnlyList<int> party)
    {
        if (!IsAvailable(id) || party.Count == 0)
            return null;
        var run = new QuestRun(_defs[id]);
        run.PartyIds.AddRange(party);
        return run;
    }

    /// <summary>通关结算。传名册则参战者凯旋，心情 +15。</summary>
    public void Complete(QuestRun run, Roster? roster = null)
    {
        run.Finish();
        ClearCount[run.QuestId] = ClearCount.GetValueOrDefault(run.QuestId) + 1;
        if (_defs.TryGetValue(run.QuestId, out var def) && def.CooldownDays > 0)
            CooldownRemaining[run.QuestId] = def.CooldownDays;
        if (roster != null)
        {
            foreach (var id in run.PartyIds)
                roster.Find(id)?.Affect.AddMood(8);
        }
    }

    public void TickDay()
    {
        var keys = new List<int>(CooldownRemaining.Keys);
        foreach (var id in keys)
        {
            if (CooldownRemaining[id] > 0)
                CooldownRemaining[id]--;
        }
    }
}
