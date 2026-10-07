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
/// <see cref="Operation"/>＝这一次玩家操作的输出（快照，固定排版：环境 → 场景 → 他人 → 自己；
/// 新操作的首次写入清空旧的），供操作反馈用。
/// <see cref="History"/>＝跨操作的近期日志（按写入先后，最旧在前，上限 <see cref="HistoryLimit"/>），
/// 供领地页签的日志面板与日志页签用。同一角色的同一句活动不重复入史。
/// </summary>
public sealed class LogBook
{
    public const int OperationLimit = 40;
    public const int HistoryLimit = 80;

    private readonly List<LogEntry> _operation = new();
    private readonly List<LogEntry> _history = new();
    private readonly List<LogEntry> _env = new();
    private readonly List<LogEntry> _scene = new();
    private readonly Dictionary<int, LogEntry> _activity = new();
    private readonly List<LogEntry> _player = new();
    private readonly Dictionary<int, string> _lastActivity = new();

    private long _opId;
    private long _writtenAt = -1;

    public IReadOnlyList<LogEntry> Operation => _operation;
    public IReadOnlyList<LogEntry> History => _history;

    /// <summary>标记一次玩家操作开始。</summary>
    public void BeginOperation() => _opId++;

    /// <summary>写一条非角色活动的日志。</summary>
    public void Write(LogEntry entry)
    {
        if (entry.Fact.Length == 0 && entry.Feel.Length == 0)
            return;
        Fresh();
        switch (entry.Kind)
        {
            case LogKind.Weather: _env.Add(entry); break;
            case LogKind.Scene: _scene.Add(entry); break;
            default: _player.Add(entry); break;
        }
        Append(entry);
        Rebuild();
    }

    /// <summary>写某角色此刻在做什么：快照里一人一行（后写覆盖），历史里与上一句相同就不再入史。</summary>
    public void WriteActivity(int characterId, LogEntry entry)
    {
        if (entry.Fact.Length == 0 && entry.Feel.Length == 0)
            return;
        Fresh();
        _activity[characterId] = entry;
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
        foreach (var entry in history)
            Append(entry);
        _env.Clear();
        _scene.Clear();
        _activity.Clear();
        _player.Clear();
        _operation.Clear();
    }

    private void Append(LogEntry entry)
    {
        _history.Add(entry);
        while (_history.Count > HistoryLimit)
            _history.RemoveAt(0);
    }

    private void Fresh()
    {
        if (_writtenAt == _opId)
            return;
        _env.Clear();
        _scene.Clear();
        _activity.Clear();
        _player.Clear();
        _operation.Clear();
        _writtenAt = _opId;
    }

    private void Rebuild()
    {
        _operation.Clear();
        _operation.AddRange(_env);
        _operation.AddRange(_scene);
        _operation.AddRange(_activity.Values);
        _operation.AddRange(_player);
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
    /// 快照的呈现顺序固定：天气 → 场景 → 他人 → 自己。
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
