using System.Collections.Generic;

namespace Rimisekai.Voice;

/// <summary>
/// 定时事件里各生成槽的成品文本。
///
/// 内容表只写骨架（步骤、分支、效果）与每行"该说什么"的指令；
/// 真正的正文由后台生成，生成好就落在这里，到点开演时按槽取。
/// 键是 (事件 Id, 步骤下标, 行下标)，值是那一行的成品正文。
///
/// 用**事件 Id** 而非场景 Id 作键：同一条场景可被多条事件引用，
/// 各事件掷出的演员不同，正文必须分开存。
///
/// 这份表随存档走——生成过的句子读档后不必重烧一遍 token。
/// </summary>
public sealed class SceneTextStore
{
    private readonly Dictionary<string, string> _texts = new();

    public int Count => _texts.Count;

    /// <summary>取某一行已生成的正文；没生成过返回 null。</summary>
    public string? Get(string key, int step, int line) =>
        _texts.TryGetValue(Key(key, step, line), out var text) ? text : null;

    /// <summary>登记一行的生成结果。空文本视为没生成，不落账。</summary>
    public void Set(string key, int step, int line, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        _texts[Key(key, step, line)] = text.Trim();
    }

    /// <summary>某一行的槽是否已有成品。</summary>
    public bool Has(string key, int step, int line) =>
        _texts.ContainsKey(Key(key, step, line));

    /// <summary>某个事件当前已备好的行数（供"这一场能不能开演"的判定）。</summary>
    public int ReadyCount(string key)
    {
        var prefix = key + "\u001f";
        var count = 0;
        foreach (var entry in _texts.Keys)
        {
            if (entry.StartsWith(prefix, System.StringComparison.Ordinal))
                count++;
        }
        return count;
    }

    /// <summary>存档用：全部成品行的扁平快照。</summary>
    public List<SceneTextEntry> Export()
    {
        var list = new List<SceneTextEntry>();
        foreach (var pair in _texts)
        {
            var parts = pair.Key.Split('\u001f');
            if (parts.Length != 3)
                continue;
            if (!int.TryParse(parts[1], out var step) || !int.TryParse(parts[2], out var line))
                continue;
            list.Add(new SceneTextEntry
            {
                Scene = parts[0],
                Step = step,
                Line = line,
                Text = pair.Value,
            });
        }
        return list;
    }

    /// <summary>读档用：把快照灌回来。</summary>
    public void Import(IEnumerable<SceneTextEntry> entries)
    {
        _texts.Clear();
        foreach (var entry in entries)
            Set(entry.Scene, entry.Step, entry.Line, entry.Text);
    }

    private static string Key(string key, int step, int line) =>
        $"{key}\u001f{step}\u001f{line}";
}

/// <summary>一行生成文本的存档行。</summary>
public sealed class SceneTextEntry
{
    /// <summary>所属事件的 Id。</summary>
    public string Scene { get; set; } = "";
    public int Step { get; set; }
    public int Line { get; set; }
    public string Text { get; set; } = "";
}
