using System;

namespace Rimisekai.Housing.StateMachine;

/// <summary>
/// 角色行为状态机。管理当前处于何种行为状态（睡觉/吃饭/工作/搬运/闲逛等），
/// 负责状态流转、打断仲裁与每步驱动。
/// </summary>
public sealed class WorkerStateMachine
{
    private IWorkerState? _currentState;

    public IWorkerState? CurrentState => _currentState;

    /// <summary>
    /// 强制流转或打断切换到新状态。
    /// </summary>
    public void TransitionTo(IWorkerState? newState, WorkerContext ctx)
    {
        if (ReferenceEquals(_currentState, newState))
            return;

        if (_currentState != null)
        {
            _currentState.Exit(ctx);
        }

        _currentState = newState;

        if (_currentState != null)
        {
            ctx.Worker.Goal = _currentState.Goal;
            _currentState.Enter(ctx);
        }
        else
        {
            ctx.Worker.Goal = ActionKind.None;
            ctx.Worker.Phase = WorkPhase.Idle;
        }
    }

    /// <summary>
    /// 每步时间更新。如果当前状态执行完毕，则清空当前状态，留待下一步重新决策。
    /// </summary>
    public bool Step(WorkerContext ctx)
    {
        if (_currentState == null)
            return false;

        var finished = _currentState.Tick(ctx);
        if (finished)
        {
            _currentState.Exit(ctx);
            _currentState = null;
            ctx.Worker.Goal = ActionKind.None;
            ctx.Worker.Phase = WorkPhase.Idle;
            ctx.Worker.FacilityId = -1;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 获取当前状态的一行描述快照。
    /// </summary>
    public string Describe(WorkerContext ctx)
    {
        return _currentState?.Describe(ctx) ?? "";
    }
}
