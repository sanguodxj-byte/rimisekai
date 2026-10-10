using System;
using System.Collections.Generic;
using Rimisekai.Character;

namespace Rimisekai.Housing.StateMachine;

/// <summary>
/// 状态机单步执行时的环境上下文。包含领地、角色、时间、设施占用表及日志输出。
/// </summary>
public sealed class WorkerContext
{
    public Territory Territory { get; init; } = null!;
    public CharacterState Character { get; init; } = null!;
    public Worker Worker { get; init; } = null!;
    public StepContext? StepContext { get; init; }
    public int Slot { get; init; }
    public HashSet<int> UsedFacilities { get; init; } = null!;
    public Func<ActionKind, int>? YieldFor { get; init; }
    public List<WorkLog> WorkLogs { get; init; } = null!;
    public Random Rng { get; init; } = null!;

    public int CurrentHour => StepContext == null ? 12 : (StepContext.NowTotal / 60) % 24;
    public int CurrentMinutes => StepContext?.NowTotal ?? 0;
    public int CurrentDay => StepContext?.Day ?? 1;

    public void Narrate(string text)
    {
        if (StepContext != null && !string.IsNullOrEmpty(text))
            StepContext.Narrate(Character, text);
    }
}
