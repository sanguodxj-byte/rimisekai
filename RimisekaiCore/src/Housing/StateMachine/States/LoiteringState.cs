using Rimisekai.Character;

namespace Rimisekai.Housing.StateMachine.States;

/// <summary>
/// 闲暇状态：无委派或处于闲时，角色在房内坐歇、串门闲转或忙活打扫零活（女仆打扫卫生/其余人收拾东西）。
/// </summary>
public sealed class LoiteringState : BaseWorkerState
{
    public override ActionKind Goal => ActionKind.Loiter;
    public override bool CanInterrupt(WorkerContext ctx) => true;

    public override void Enter(WorkerContext ctx)
    {
        ctx.Worker.Goal = ActionKind.Loiter;
        ctx.Worker.Task = ActionKind.None;
        ctx.Worker.Phase = ctx.Worker.Path.Count > 0 ? WorkPhase.Moving : (ctx.Worker.FacilityId >= 0 ? WorkPhase.Working : WorkPhase.Idle);
        if (ctx.Worker.FacilityId >= 0 && ctx.Worker.Phase == WorkPhase.Working)
            ctx.UsedFacilities?.Add(ctx.Worker.FacilityId);
    }

    public override bool Tick(WorkerContext ctx)
    {
        var worker = ctx.Worker;
        if (worker.Path.Count > 0)
        {
            MoveAlong(worker);
            if (worker.Path.Count > 0)
                return false;
        }

        if (worker.FacilityId >= 0 && worker.Phase != WorkPhase.Working)
        {
            worker.Phase = WorkPhase.Working;
            ctx.UsedFacilities?.Add(worker.FacilityId);
        }

        if (worker.LoiterTicks > 0)
            worker.LoiterTicks--;

        return worker.LoiterTicks <= 0;
    }

    public override void Exit(WorkerContext ctx)
    {
        if (ctx.Worker.FacilityId >= 0)
        {
            ctx.UsedFacilities?.Remove(ctx.Worker.FacilityId);
        }
        ctx.Worker.Loiter = LoiterKind.None;
        ctx.Worker.LoiterTicks = 0;
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
        switch (worker.Loiter)
        {
            case LoiterKind.Sitting:
                var seat = ctx.Territory.Facilities.Find(f => f.Id == worker.FacilityId);
                return seat == null
                    ? $"{ctx.Character.Name}在{place}找了个地方歇着。"
                    : $"{ctx.Character.Name}在{place}的{seat.Name}上歇着。";
            case LoiterKind.Wandering:
                return $"{ctx.Character.Name}在{place}转悠。";
            default:
                var fac = ctx.Territory.Facilities.Find(f => f.Id == worker.FacilityId);
                if (fac != null)
                {
                    return ctx.Character.Has(Trait.Maid)
                        ? $"{ctx.Character.Name}在{place}打扫{fac.Name}。"
                        : $"{ctx.Character.Name}在{place}收拾{fac.Name}。";
                }
                return ctx.Character.Has(Trait.Maid)
                    ? $"{ctx.Character.Name}在{place}打扫卫生。"
                    : $"{ctx.Character.Name}在{place}收拾东西。";
        }
    }
}
