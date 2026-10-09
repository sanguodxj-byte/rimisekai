using System;
using System.Collections.Generic;
using Rimisekai.Catalog;
using Rimisekai.Character;
using Rimisekai.Defs;

namespace Rimisekai.Quest;

/// <summary>
/// 委托只有两类：纯战斗（接下即开打，细分见 <see cref="QuestBattle"/>）与地城探索（马车送进一座现生成的地城，最深处是正主）。
/// 采集、送货一类不做委托。
/// </summary>
public enum QuestKind
{
    Battle,
    Dungeon,
}

/// <summary>
/// 战斗委托的打法：普通战斗；首领战（阵中有一名占多格的首领）；
/// 连战（首波之后一波接一波补上来，每清一波有一个补给回合）。
/// </summary>
public enum QuestBattle
{
    Normal,
    Boss,
    Waves,
}

/// <summary>
/// 任务定义。任务名走 Def.Label，客观描述走 Def.Description；
/// 传言是来源不明的道听途说，只做氛围、不保证属实。
/// </summary>
public sealed class QuestDef : Def, IIdentifiedDef
{
    private int _id;
    public int Id
    {
        get => _id;
        init
        {
            _id = value;
            if (string.IsNullOrEmpty(DefName))
                DefName = $"Quest_{value}";
        }
    }
    public string Name
    {
        get => string.IsNullOrEmpty(Label) ? DefName : Label;
        init
        {
            Label = value;
            if (string.IsNullOrEmpty(DefName))
                DefName = !string.IsNullOrEmpty(value) ? value : $"Quest_{Id}";
        }
    }
    public QuestKind Kind { get; init; }

    /// <summary>战斗委托的打法；地城委托不看。</summary>
    public QuestBattle Battle { get; init; }

    /// <summary>连战在首波（<see cref="Foes"/>）之后依次补上的各波敌人；非连战为空。</summary>
    public List<List<EnemyDef>> Waves { get; init; } = new();

    /// <summary>委托卡上的类别字：普通战斗 / 首领战 / 连战 N 波 / 地城探索。</summary>
    public string KindText => Kind == QuestKind.Dungeon
        ? "地城探索"
        : Battle switch
        {
            QuestBattle.Boss => "首领战",
            QuestBattle.Waves => $"连战 {1 + Waves.Count} 波",
            _ => "普通战斗",
        };

    public int CooldownDays { get; init; }

    /// <summary>难度，按星计，半星用 0.5 表达。</summary>
    public double Difficulty { get; init; }

    /// <summary>奖励清单，一行一条。</summary>
    public List<string> Rewards { get; init; } = new();

    /// <summary>最多参与人数，含玩家本人。</summary>
    public int MaxPartySize { get; init; }

    /// <summary>传言，展示时包在直角括号里以暗色显示。</summary>
    public string Rumor { get; init; } = "";

    /// <summary>战斗委托的首波阵容；地城委托为最深处的正主（玩家可见的敌人名权威在此）。</summary>
    public List<EnemyDef> Foes { get; init; } = new();

    /// <summary>难度显示：实心为整星，镂空 ☆ 为半星；超过 10 星改为 ★×数量。</summary>
    public string DifficultyText
    {
        get
        {
            if (Difficulty > 10)
                return $"★×{(int)Math.Ceiling(Difficulty)}";
            var half = (int)Math.Round(Difficulty * 2);
            var text = new string('★', half / 2);
            if (half % 2 == 1)
                text += "☆";
            return text;
        }
    }
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
    public QuestDef Def { get; }
    public int QuestId { get; }
    public QuestKind Kind { get; }
    public List<int> PartyIds { get; } = new();
    public int CurrentNodeId { get; set; } = -1;
    public HashSet<int> Explored { get; } = new();
    public Dictionary<int, int> Flags { get; } = new();
    public bool Finished { get; private set; }

    public QuestRun(QuestDef def)
    {
        Def = def;
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

/// <summary>
/// 任务进度记录：通关次数与冷却跨任务保留。任务定义的权威在 DefDatabase，
/// 这里只管每个存档自己的进度。
/// </summary>
public sealed class QuestRecord
{
    public Dictionary<int, int> ClearCount { get; } = new();
    public Dictionary<int, int> CooldownRemaining { get; } = new();

    public void Register(QuestDef def) => DefDatabase<QuestDef>.Register(def);

    public QuestDef? Find(int id) => DefDatabase<QuestDef>.GetById(id);

    public bool IsAvailable(int id) =>
        !CooldownRemaining.TryGetValue(id, out var c) || c <= 0;

    /// <summary>按任务定义与编成名单启动任务；冷却中或名单为空返回 null。</summary>
    public QuestRun? Start(QuestDef def, IReadOnlyList<int> party)
    {
        if (party.Count == 0 || !IsAvailable(def.Id))
            return null;
        var run = new QuestRun(def);
        run.PartyIds.AddRange(party);
        return run;
    }

    public QuestRun? Start(int id, IReadOnlyList<int> party)
    {
        var def = Find(id);
        return def != null ? Start(def, party) : null;
    }

    /// <summary>通关结算。传名册则参战者凯旋，心情 +8。</summary>
    public void Complete(QuestRun run, Roster? roster = null)
    {
        run.Finish();
        ClearCount[run.QuestId] = ClearCount.GetValueOrDefault(run.QuestId) + 1;
        var def = run.Def ?? Find(run.QuestId);
        if (def != null && def.CooldownDays > 0)
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
