using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Godot;
using Rimisekai.Hub;
using Rimisekai.Voice;

namespace Rimisekai.Portrait;

/// <summary>
/// 后台生成泵。定时事件的台词不在到点那一刻现生成——开局就已排进队列，
/// 本泵在后台一个个发出去，生成好写回成品表，到点直接播。
///
/// 形态照搬 AgentBridge 的成熟做法：工作线程跑网络，结果经
/// <see cref="ConcurrentQueue{T}"/> 回主线程，在 <c>_Process</c> 里落账。
/// 因此主线程从不等待、从不阻塞；没有 <c>Task.Wait</c>、没有 sleep、没有轮询循环。
///
/// 一次只飞一个任务：顺序、可预期，也免得并发把 API 打限流。
/// 失败的槽不丢弃——它留在队列里等重排，退避一段时间再试，
/// 免得网络一断就每帧重打。
/// </summary>
public partial class PortraitGenerationPump : Node
{
    /// <summary>失败后隔多久才重排同一个槽（秒）。</summary>
    private const double RetryDelaySeconds = 8.0;

    private sealed class Result
    {
        public GenerationTask Task = null!;
        public IReadOnlyList<string> Lines = Array.Empty<string>();
    }

    private readonly ConcurrentQueue<Result> _done = new();

    private HubSession? _hub;
    private GenerationTask? _inFlight;
    private bool _running;

    /// <summary>最近一次失败之后要等到这个时刻（引擎时间）才再取任务。</summary>
    private double _retryAfter;

    /// <summary>绑一个据点会话，开始为它的定时事件预生成。</summary>
    public void Bind(HubSession? hub)
    {
        // 换会话：在飞的任务交回队列等新会话取，不记一次失败。
        if (_inFlight != null)
            _hub?.ReleaseGeneration(_inFlight);
        _inFlight = null;
        _retryAfter = 0;
        _hub = hub;
    }

    public override void _Ready()
    {
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (!_running || _hub == null)
            return;

        // 先落账上一批回来的结果（只碰 Core，主线程安全）。
        while (_done.TryDequeue(out var result))
        {
            _inFlight = null;
            if (result.Lines.Count == 0)
            {
                // 失败：任务留在队列里，退避一段时间再重排。
                _hub.FailGeneration(result.Task, "生成器未产出正文");
                _retryAfter = Now() + RetryDelaySeconds;
            }
            else
            {
                _hub.CompleteGeneration(result.Task, result.Lines);
            }
        }

        if (_inFlight != null)
            return;
        if (!_hub.GenerationAvailable)
            return;
        if (Now() < _retryAfter)
            return;

        var task = _hub.TakeGenerationTask();
        if (task == null)
            return;

        var generator = _hub.VoiceGenerator;
        if (generator == null)
        {
            _hub.FailGeneration(task, "没有接入生成器");
            _retryAfter = Now() + RetryDelaySeconds;
            return;
        }

        _inFlight = task;
        Dispatch(generator, task);
    }

    private double Now() => Time.GetTicksMsec() / 1000.0;

    /// <summary>
    /// 发一个任务出去。用 fire-and-forget：不 await，不阻塞主线程；
    /// 结果回来就塞进队列，下一帧在主线程落账。
    /// </summary>
    private void Dispatch(IVoiceGenerator generator, GenerationTask task)
    {
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            IReadOnlyList<string> lines;
            try
            {
                lines = await generator.GenerateAsync(task.Request).ConfigureAwait(false);
            }
            catch (Exception)
            {
                lines = Array.Empty<string>();
            }
            _done.Enqueue(new Result { Task = task, Lines = lines });
        });
    }

    /// <summary>开始/停止后台生成。停止时把在飞的任务交回队列，下次继续。</summary>
    public void SetRunning(bool running)
    {
        _running = running;
        if (!running && _inFlight != null)
        {
            _hub?.ReleaseGeneration(_inFlight);
            _inFlight = null;
        }
    }
}
