using System;

namespace Rimisekai.Housing.StateMachine.States;

/// <summary>
/// 搬运状态：把劳动产出运往仓储，或将配方材料从库房取货备料至工作台。
/// </summary>
public sealed class HaulingState : BaseWorkerState
{
    public override ActionKind Goal => ActionKind.Haul;

    public override void Enter(WorkerContext ctx)
    {
        ctx.Worker.Goal = ActionKind.Haul;
        ctx.Worker.Phase = ctx.Worker.Path.Count > 0 ? WorkPhase.Moving : WorkPhase.Idle;
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

        var target = ctx.Territory.Facilities.Find(f => f.Id == worker.HaulTargetId && f.Built);
        if (target == null)
        {
            EndHaul(worker);
            return true;
        }

        var moved = target.CanStore
            ? ctx.Territory.StoreFrom(ctx.Character, target, worker.HaulItemId, worker.HaulCount)
            : Deposit(ctx.Character, target, worker.HaulItemId, worker.HaulCount);

        if (moved > 0)
            ctx.Narrate($"{ctx.Character.Name}把{worker.HaulItemId}放到了{target.Name}。");

        EndHaul(worker);
        return true;
    }

    private static int Deposit(Character.CharacterState who, Facility target, string itemId, int count)
    {
        if (count <= 0 || itemId.Length == 0)
            return 0;
        var moved = Math.Min(count, who.Bag.Get(itemId));
        if (moved <= 0)
            return 0;
        who.Bag.Add(itemId, -moved);
        target.Contents.Add(itemId, moved);
        return moved;
    }

    private static void EndHaul(Worker worker)
    {
        worker.HaulItemId = "";
        worker.HaulCount = 0;
        worker.HaulTargetId = -1;
        worker.Goal = ActionKind.None;
        worker.Phase = WorkPhase.Idle;
    }

    public override void Exit(WorkerContext ctx)
    {
        EndHaul(ctx.Worker);
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
        var target = ctx.Territory.Facilities.Find(f => f.Id == worker.HaulTargetId);
        return target == null
            ? $"{ctx.Character.Name}正把{worker.HaulItemId}送去{place}。"
            : $"{ctx.Character.Name}正把{worker.HaulItemId}送去{place}的{target.Name}。";
    }
}
