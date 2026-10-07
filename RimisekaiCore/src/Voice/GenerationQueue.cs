using System.Collections.Generic;

namespace Rimisekai.Voice;

/// <summary>
/// 一次后台生成任务：某条事件里某一个生成槽该产出的正文。
/// 请求在排队时就拼好（那一刻的世界状态），因此执行端只管发出去、把结果写回。
///
/// 键是 <see cref="Key"/>（事件 Id），不是场景 Id——同一条场景可被多条事件引用，
/// 各事件的演员不同，正文必须分开存。
/// </summary>
public sealed class GenerationTask
{
    /// <summary>所属事件的 Id（也是成品表的键）。</summary>
    public string Key { get; init; } = "";
    public int Step { get; init; }
    public int Line { get; init; }

    /// <summary>这一行由哪个角色说（演员名）。</summary>
    public string ActorName { get; init; } = "";

    /// <summary>拼好的请求。执行端直接交给生成器。</summary>
    public required VoiceRequest Request { get; init; }

    /// <summary>已经试过几次。失败重排时累加，用于放弃。</summary>
    public int Attempts { get; set; }
}

/// <summary>
/// 后台生成队列。开局把定时事件的各生成槽编译成任务排进来，
/// 前端泵一次取一个发出去，结果写回 <see cref="SceneTextStore"/>。
///
/// 本类只做数据与状态机，不碰线程、不碰 HTTP——执行在宿主（前端泵）。
/// 这样 Core 保持纯逻辑、可单测，异步只集中在宿主一处。
/// </summary>
public sealed class GenerationQueue
{
    /// <summary>同一任务失败重排的上限，超过就放弃（那一场便永远缺这一行）。</summary>
    public const int MaxAttempts = 3;

    private readonly List<GenerationTask> _pending = new();
    private readonly HashSet<string> _inFlight = new();
    private readonly SceneTextStore _store;

    public GenerationQueue(SceneTextStore store) => _store = store;

    /// <summary>还没完成的任务数（含在飞的）。</summary>
    public int Count => _pending.Count;

    /// <summary>是否还有可取的任务。</summary>
    public bool HasWork
    {
        get
        {
            foreach (var task in _pending)
            {
                if (!_inFlight.Contains(TaskKey(task)))
                    return true;
            }
            return false;
        }
    }

    /// <summary>排一个任务。同一槽重复排只留一个。</summary>
    public void Enqueue(GenerationTask task)
    {
        var key = TaskKey(task);
        foreach (var existing in _pending)
        {
            if (TaskKey(existing) == key)
                return;
        }
        _pending.Add(task);
    }

    /// <summary>
    /// 取一个待办任务并标记为在飞。没有可取的返回 null。
    /// 已生成过的槽（读档后 store 里已有成品）顺手清掉，不重复烧 token。
    /// </summary>
    public GenerationTask? TakeNext()
    {
        for (var i = 0; i < _pending.Count; i++)
        {
            var task = _pending[i];
            var key = TaskKey(task);
            if (_inFlight.Contains(key))
                continue;
            if (_store.Has(task.Key, task.Step, task.Line))
            {
                _pending.RemoveAt(i);
                i--;
                continue;
            }
            _inFlight.Add(key);
            return task;
        }
        return null;
    }

    /// <summary>
    /// 任务完成：把正文写进成品表，任务出队。
    /// 返回是否成功（生成器没产出按失败处理）。
    /// </summary>
    public bool Complete(GenerationTask task, IReadOnlyList<string> lines)
    {
        _inFlight.Remove(TaskKey(task));
        if (lines == null || lines.Count == 0)
            return Fail(task, "生成器未产出正文");

        // 多行压成一行：场景的一行只呈现一句，多出来的丢掉。
        _store.Set(task.Key, task.Step, task.Line, lines[0]);
        _pending.Remove(task);
        return true;
    }

    /// <summary>
    /// 任务失败：出在飞表。还没到上限就留在队列里等下次重排，到上限就丢弃。
    /// </summary>
    public bool Fail(GenerationTask task, string error)
    {
        _inFlight.Remove(TaskKey(task));
        task.Attempts++;
        if (task.Attempts >= MaxAttempts)
            _pending.Remove(task);
        return false;
    }

    /// <summary>
    /// 该场景（在 <paramref name="key"/> 这条事件下）的全部生成槽是否都已备好。
    /// 事件板据此决定到点能不能开演。
    /// </summary>
    public bool SceneReady(SceneEvent scene, string key)
    {
        for (var s = 0; s < scene.Steps.Count; s++)
        {
            var step = scene.Steps[s];
            for (var l = 0; l < step.Lines.Count; l++)
            {
                if (!step.Lines[l].NeedsGeneration)
                    continue;
                if (!_store.Has(key, s, l))
                    return false;
            }
        }
        return true;
    }

    private static string TaskKey(GenerationTask task) =>
        $"{task.Key}\u001f{task.Step}\u001f{task.Line}";
}
