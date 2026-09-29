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

    /// <summary>
    /// 角色行为行在 _log 里的下标。一个角色只占一行：再写就替换旧行，
    /// 因此日志里看到的永远是“他此刻在做什么”，而不是一路做过的事。
    /// </summary>
    private readonly Dictionary<int, int> _activityRow = new();

    public IReadOnlyList<LogLine> Log => _log;

    /// <summary>
    /// 标记一次玩家操作开始。日志的更新单位是操作：新操作的首次写入会
    /// 清掉之前留下的全部内容，禁止跨操作堆积。操作边界由输入层负责标
    /// （据点界面每次点击调一次），Core 不逐方法插桩——玩家操作有几百种，
    /// 逐个标记必然漏。没写出任何内容的操作（校验失败、纯界面点击）不动旧日志。
    /// </summary>
    public void BeginOperation() => _operation++;

    public void Write(string text)
    {
        if (_writtenAtOperation != _operation)
        {
            _log.Clear();
            _activityRow.Clear();
            _writtenAtOperation = _operation;
        }
        _log.Add(new LogLine(text));
        if (_log.Count > LogLimit)
            _log.RemoveAt(0);
    }

    /// <summary>
    /// 写某角色此刻在做什么。同一角色只保留一行——重复调用会替换旧行，
    /// 因此推进时间不会把一个角色的过程堆成好几行。
    /// </summary>
    public void WriteActivity(int characterId, string text)
    {
        if (text.Length == 0)
            return;
        if (_writtenAtOperation != _operation)
        {
            _log.Clear();
            _activityRow.Clear();
            _writtenAtOperation = _operation;
        }
        if (_activityRow.TryGetValue(characterId, out var row) && row < _log.Count)
        {
            _log[row] = new LogLine(text);
            return;
        }
        _activityRow[characterId] = _log.Count;
        _log.Add(new LogLine(text));
        if (_log.Count > LogLimit)
            _log.RemoveAt(0);
    }
}
