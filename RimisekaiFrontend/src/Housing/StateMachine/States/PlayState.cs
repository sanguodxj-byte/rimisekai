namespace Rimisekai.Housing.StateMachine.States;

/// <summary>
/// 消遣娱乐状态：前往带 Leisure 行动的设施进行消遣，耗尽时长后回决策。
/// </summary>
public sealed class PlayState : BaseWorkerState
{
    public override ActionKind Goal => ActionKind.Loiter;

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
        if (MoveAlong(ctx.Worker, ctx))
            return false;

        if (ctx.Worker.FacilityId >= 0 && ctx.Worker.Phase != WorkPhase.Working)
        {
            ctx.Worker.Phase = WorkPhase.Working;
            ctx.UsedFacilities?.Add(ctx.Worker.FacilityId);
        }

        if (ctx.StepContext != null)
            ctx.Character.Affect.LastPlayDay = ctx.StepContext.Day;

        if (ctx.Worker.PlayTicks > 0)
            ctx.Worker.PlayTicks--;
        if (ctx.Worker.PlayTicks > 0)
            return false;

        // 在箭靶上消遣完一回，就是练了一回。
        if (ctx.Territory.Facilities.Find(f => f.Id == ctx.Worker.FacilityId)?.Supports(ActionKind.Train) == true)
            Territory.Drill(ctx.Character);
        return true;
    }

    public override void Exit(WorkerContext ctx)
    {
        if (ctx.Worker.FacilityId >= 0)
        {
            ctx.UsedFacilities?.Remove(ctx.Worker.FacilityId);
        }
        ctx.Worker.PlayTicks = 0;
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
        var facility = ctx.Territory.Facilities.Find(f => f.Id == worker.FacilityId);
        return facility == null
            ? $"{ctx.Character.Name}在{place}消遣。"
            : $"{ctx.Character.Name}在{place}的{facility.Name}消遣。";
    }
}
