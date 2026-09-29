using System;
using Rimisekai.Character;

namespace Rimisekai.Housing.StateMachine.States;

/// <summary>
/// 睡眠状态：前往有床的房间，在床上安歇，随时间恢复体能并解除疲劳，直到清晨苏醒。
/// </summary>
public sealed class SleepingState : BaseWorkerState
{
    public override ActionKind Goal => ActionKind.Sleep;
    public override bool CanInterrupt(WorkerContext ctx) => false;

    public override void Enter(WorkerContext ctx)
    {
        ctx.Worker.Goal = ActionKind.Sleep;
        ctx.Worker.Task = ActionKind.None;
        ctx.Worker.Progress = 0;
        ctx.Worker.Phase = ctx.Worker.Path.Count > 0 ? WorkPhase.Moving : (ctx.Worker.FacilityId >= 0 ? WorkPhase.Working : WorkPhase.Idle);
        if (ctx.Worker.FacilityId >= 0 && ctx.Worker.Phase == WorkPhase.Working)
            ctx.UsedFacilities?.Add(ctx.Worker.FacilityId);
    }

    public override bool Tick(WorkerContext ctx)
    {
        if (MoveAlong(ctx.Worker))
            return false;

        if (ctx.Worker.FacilityId >= 0 && ctx.Worker.Phase != WorkPhase.Working)
        {
            ctx.Worker.Phase = WorkPhase.Working;
            ctx.UsedFacilities?.Add(ctx.Worker.FacilityId);
        }

        ctx.Character.Condition.SleepTick();
        return false;
    }

    public override void Exit(WorkerContext ctx)
    {
        if (ctx.Worker.FacilityId >= 0)
        {
            ctx.UsedFacilities?.Remove(ctx.Worker.FacilityId);
        }
    }

    public override string Describe(WorkerContext ctx)
    {
        var worker = ctx.Worker;
        if (worker.Phase == WorkPhase.Moving && worker.Path.Count > 0)
        {
            var next = ctx.Territory.Rooms.Find(r => r.Id == worker.Path.Peek());
            return next == null
                ? $"{ctx.Character.Name}在路上。"
                : $"{ctx.Character.Name}正往{next.Name}去。";
        }

        var place = GetPlaceName(ctx);
        var bed = ctx.Territory.Facilities.Find(f => f.Id == worker.FacilityId);
        return bed == null
            ? $"{ctx.Character.Name}在{place}睡觉。"
            : $"{ctx.Character.Name}在{place}的{bed.Name}上睡觉。";
    }
}
