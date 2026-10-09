using System.Collections.Generic;

namespace Rimisekai.Hub;

/// <summary>日志条目的内容类别（见 docs/日志规范.md）。</summary>
public enum LogKind
{
    /// <summary>场景描述：玩家来到一处地方时看到的景象。</summary>
    Scene,

    /// <summary>天气变化：变天、换季、日终大事件。</summary>
    Weather,

    /// <summary>当前区域（玩家所在房间）的角色在做什么。</summary>
    Activity,

    /// <summary>玩家自己的行动与其结果。</summary>
    Action,
}

/// <summary>
/// 一条日志：上帝视角，但只写玩家感知得到的事。
/// <see cref="Fact"/>＝a 段，系统性/客观信息；<see cref="Feel"/>＝b 段，以玩家为中心的感受或角色对玩家的反应。
/// 两段在显示时合成一句「a，b」（<see cref="Text"/>）；b 没有出处时为空，只显示 a。
/// </summary>
public readonly record struct LogEntry(LogKind Kind, string Fact, string Feel, int Day, int Minutes)
{
    /// <summary>显示用的一句话：b 为空时就是 a（自带句末标点），否则 a 去掉句末句号后接「，」再接 b。</summary>
    public string Text => Compose(Fact, Feel);

    public static string Compose(string fact, string feel)
    {
        if (feel.Length == 0)
            return fact;
        var head = fact.TrimEnd('。', '，', ' ');
        return head.Length == 0 ? feel : $"{head}，{feel}";
    }
}

/// <summary>
/// 日志簿：两份视图。
/// <see cref="Operation"/>＝这一次玩家操作的输出（快照，先按 <see cref="Rank"/> 分档：玩家行动 → 环境变化 → 在场角色，档内按写入先后；
/// 同一角色的活动只留最新一句并挪到它写下的位置；新操作的首次写入清空旧的），供操作反馈用。
/// <see cref="History"/>＝跨操作的近期日志（按写入先后，最旧在前，上限 <see cref="HistoryLimit"/>），
/// 供领地页签的日志面板与日志页签用。同一角色的同一句活动不重复入史。
/// </summary>
public sealed class LogBook
{
    public const int OperationLimit = 40;
    public const int HistoryLimit = 80;

    private readonly List<LogEntry> _operation = new();
    private readonly List<LogEntry> _history = new();
    private readonly List<(int Who, LogEntry Entry)> _snapshot = new();
    private readonly Dictionary<int, string> _lastActivity = new();

    private long _opId;
    private long _writtenAt = -1;

    /// <summary>本次操作的第一条在历史里的位置（之前的历史不参与重排）。</summary>
    private int _opHistoryStart;

    public IReadOnlyList<LogEntry> Operation => _operation;
    public IReadOnlyList<LogEntry> History => _history;

    /// <summary>标记一次玩家操作开始。</summary>
    public void BeginOperation() => _opId++;

    /// <summary>
    /// 一次操作里各类日志的先后（2026-10-09 主人定）：先写玩家自己做了什么（来到哪里、和谁交流…），
    /// 再写环境变化（季节、天气，可空），最后写在场角色的行为或对话。同一档里仍按写入先后。
    /// </summary>
    public static int Rank(LogKind kind) => kind switch
    {
        LogKind.Weather => 1,
        LogKind.Activity => 2,
        _ => 0,
    };

    /// <summary>写一条非角色活动的日志。</summary>
    public void Write(LogEntry entry)
    {
        if (entry.Fact.Length == 0 && entry.Feel.Length == 0)
            return;
        Fresh();
        InsertSnapshot(-1, entry);
        Append(entry);
        Rebuild();
    }

    /// <summary>写某角色此刻在做什么：快照里一人一行（后写覆盖），历史里与上一句相同就不再入史。</summary>
    public void WriteActivity(int characterId, LogEntry entry)
    {
        if (entry.Fact.Length == 0 && entry.Feel.Length == 0)
            return;
        Fresh();
        _snapshot.RemoveAll(s => s.Who == characterId);
        InsertSnapshot(characterId, entry);
        var text = entry.Text;
        if (!_lastActivity.TryGetValue(characterId, out var last) || last != text)
        {
            _lastActivity[characterId] = text;
            Append(entry);
        }
        Rebuild();
    }

    /// <summary>读档：历史整份换回，快照清空。</summary>
    public void Restore(IEnumerable<LogEntry> history)
    {
        _history.Clear();
        _lastActivity.Clear();
        _opHistoryStart = 0;
        foreach (var entry in history)
        {
            _history.Add(entry);
            while (_history.Count > HistoryLimit)
                _history.RemoveAt(0);
        }
        _opHistoryStart = _history.Count;
        _snapshot.Clear();
        _operation.Clear();
    }

    /// <summary>按 <see cref="Rank"/> 插到同档最后一条之后（不打乱同档的时间先后）。</summary>
    private void InsertSnapshot(int who, LogEntry entry)
    {
        var at = _snapshot.Count;
        while (at > 0 && Rank(_snapshot[at - 1].Entry.Kind) > Rank(entry.Kind))
            at--;
        _snapshot.Insert(at, (who, entry));
    }

    /// <summary>入史：本次操作写下的几条同样按 <see cref="Rank"/> 排，之前操作的历史不动。</summary>
    private void Append(LogEntry entry)
    {
        var at = _history.Count;
        while (at > _opHistoryStart && Rank(_history[at - 1].Kind) > Rank(entry.Kind))
            at--;
        _history.Insert(at, entry);
        while (_history.Count > HistoryLimit)
        {
            _history.RemoveAt(0);
            _opHistoryStart = System.Math.Max(0, _opHistoryStart - 1);
        }
    }

    private void Fresh()
    {
        if (_writtenAt == _opId)
            return;
        _snapshot.Clear();
        _operation.Clear();
        _writtenAt = _opId;
        _opHistoryStart = _history.Count;
    }

    private void Rebuild()
    {
        _operation.Clear();
        foreach (var (_, entry) in _snapshot)
            _operation.Add(entry);
        while (_operation.Count > OperationLimit)
            _operation.RemoveAt(0);
    }
}

public sealed partial class HubSession
{
    private readonly LogBook _book = new();

    /// <summary>这一次操作的日志（快照）。</summary>
    public IReadOnlyList<LogEntry> Log => _book.Operation;

    /// <summary>跨操作的近期日志，最旧在前。</summary>
    public IReadOnlyList<LogEntry> History => _book.History;

    /// <summary>
    /// 标记一次玩家操作开始。日志快照的更新单位是操作：新操作的首次写入会
    /// 清掉之前留下的快照（历史不清）。操作边界由输入层负责标。
    /// </summary>
    public void BeginOperation() => _book.BeginOperation();

    private LogEntry Entry(LogKind kind, string fact, string feel) =>
        new(kind, fact, feel, State.Clock.Day, State.Clock.Minutes);

    /// <summary>写入天气类日志（变天、换季、日终大事件）。</summary>
    public void WriteEnvironment(string fact, string feel = "") =>
        _book.Write(Entry(LogKind.Weather, fact, feel));

    /// <summary>写某角色此刻在做什么（只写玩家所在房间里看得见的人）。</summary>
    public void WriteActivity(int characterId, string fact, string feel = "") =>
        _book.WriteActivity(characterId, Entry(LogKind.Activity, fact, feel));

    /// <summary>写一条场景描述。</summary>
    public void WriteScene(string fact, string feel = "") =>
        _book.Write(Entry(LogKind.Scene, fact, feel));

    /// <summary>
    /// 通用写入：天气类文字进天气层，其余作为玩家行动。
    /// 快照按写入先后排（即按时间）。
    /// </summary>
    public void Write(string fact, string feel = "")
    {
        if (string.IsNullOrEmpty(fact) && string.IsNullOrEmpty(feel))
            return;
        _book.Write(Entry(IsEnvironmentText(fact) ? LogKind.Weather : LogKind.Action, fact, feel));
    }

    private static bool IsEnvironmentText(string text) =>
        text.StartsWith("天气") ||
        text.StartsWith("今日天气") ||
        text.StartsWith("季节") ||
        text.StartsWith("夜里有动静");
}
