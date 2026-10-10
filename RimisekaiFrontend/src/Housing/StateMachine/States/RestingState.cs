using System;
using Rimisekai.Character;

namespace Rimisekai.Housing.StateMachine.States;

/// <summary>
/// 休息状态：在能够休息的设施（椅子、长椅等）上闭目歇息，回复气力。
/// </summary>
public sealed class RestingState : BaseWorkerState
{
    public override ActionKind Goal => ActionKind.Rest;

    public override void Enter(WorkerContext ctx)
    {
        ctx.Worker.Goal = ActionKind.Rest;
        ctx.Worker.Task = ActionKind.None;
        ctx.Worker.Phase = ctx.Worker.Path.Count > 0 ? WorkPhase.Moving : (ctx.Worker.FacilityId >= 0 ? WorkPhase.Working : WorkPhase.Idle);
        if (ctx.Worker.FacilityId >= 0 && ctx.Worker.Phase == WorkPhase.Working)
            ctx.UsedFacilities?.Add(ctx.Worker.FacilityId);
    }

    public override bool Tick(WorkerContext ctx)
    {
        if (MoveAlong(ctx.Worker, ctx))
            return false;

        if (ctx.Worker.FacilityId >= 0 && ctx.Worker.Phase != WorkPhase.Working)
        {
            ctx.Worker.Phase = WorkPhase.Working;
            ctx.UsedFacilities?.Add(ctx.Worker.FacilityId);
        }

        ctx.Character.Condition.RestTick();
        ctx.Character.Condition.Recover(0, Traits.RestSpiritBonus(ctx.Character));

        // 气力回满即完成休息
        return ctx.Character.Condition.Spirit >= ctx.Character.Condition.MaxSpirit;
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
        var restSeat = ctx.Territory.Facilities.Find(f => f.Id == worker.FacilityId);
        return restSeat == null
            ? $"{ctx.Character.Name}在{place}歇着。"
            : $"{ctx.Character.Name}在{place}的{restSeat.Name}上歇着。";
    }
}
