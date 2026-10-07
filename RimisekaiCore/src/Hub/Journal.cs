using System.Collections.Generic;

namespace Rimisekai.Hub;

public readonly record struct LogLine(string Text);

public sealed partial class HubSession
{
    public const int LogLimit = 40;

    private readonly List<LogLine> _log = new();

    /// <summary>当前操作序号。每次玩家操作开始时加一。</summary>
    private long _operation;

    /// <summary>最近一次写入所属的操作序号。-1 保证开局第一笔也走清空路径。</summary>
    private long _writtenAtOperation = -1;

    /// <summary>固定顺序第 1 层：环境变化行（天气、季节、日终大事件等）。</summary>
    private readonly List<string> _envLines = new();

    /// <summary>固定顺序第 2 层：其他角色行动行（一人一行快照，后写覆盖先写）。键为角色 Id。</summary>
    private readonly Dictionary<int, string> _activityLines = new();

    /// <summary>固定顺序第 3 层：玩家行动行（打量四周、移动、工作、使用设施等）。</summary>
    private readonly List<string> _playerLines = new();

    public IReadOnlyList<LogLine> Log => _log;

    /// <summary>
    /// 标记一次玩家操作开始。日志的更新单位是操作：新操作的首次写入会
    /// 清掉之前留下的全部内容，禁止跨操作堆积。操作边界由输入层负责标
    /// （据点界面每次点击调一次），Core 不逐方法插桩。
    /// </summary>
    public void BeginOperation() => _operation++;

    private void EnsureFreshOperation()
    {
        if (_writtenAtOperation != _operation)
        {
            _envLines.Clear();
            _activityLines.Clear();
            _playerLines.Clear();
            _log.Clear();
            _writtenAtOperation = _operation;
        }
    }

    /// <summary>
    /// 写入环境变化日志（固定排版第 1 层：天气、季节等）。
    /// </summary>
    public void WriteEnvironment(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        EnsureFreshOperation();
        _envLines.Add(text);
        RebuildLog();
    }

    /// <summary>
    /// 写某角色此刻在做什么（固定排版第 2 层：其他角色行动）。
    /// 同一角色只保留一行快照——重复调用会替换旧行。
    /// </summary>
    public void WriteActivity(int characterId, string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        EnsureFreshOperation();
        _activityLines[characterId] = text;
        RebuildLog();
    }

    /// <summary>
    /// 通用写入：根据内容特征智能分流（环境变化进第 1 层，其余作为玩家行动进第 3 层）。
    /// 无论调用先后，最终呈现的日志顺序永远固定：
    /// 1. 环境变化(天气，季节等等)
    /// 2. 其他角色行动
    /// 3. 玩家行动(观察属于这里)
    /// </summary>
    public void Write(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        if (IsEnvironmentText(text))
        {
            WriteEnvironment(text);
            return;
        }
        EnsureFreshOperation();
        _playerLines.Add(text);
        RebuildLog();
    }

    private static bool IsEnvironmentText(string text) =>
        text.StartsWith("天气") ||
        text.StartsWith("今日天气") ||
        text.StartsWith("季节") ||
        text.StartsWith("夜里有动静");

    private void RebuildLog()
    {
        _log.Clear();
        foreach (var line in _envLines)
            _log.Add(new LogLine(line));
        foreach (var line in _activityLines.Values)
            _log.Add(new LogLine(line));
        foreach (var line in _playerLines)
            _log.Add(new LogLine(line));

        while (_log.Count > LogLimit)
            _log.RemoveAt(0);
    }
}
