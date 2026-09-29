using System;

namespace Rimisekai.Housing.StateMachine.States;

/// <summary>
/// 进餐状态：前往餐座坐下，消耗一份食物，若房间无餐桌则施加“无桌进餐”心情扣减。
/// </summary>
public sealed class DiningState : BaseWorkerState
{
    public override ActionKind Goal => ActionKind.Meal;
    public override bool CanInterrupt(WorkerContext ctx) => false;

    public override void Enter(WorkerContext ctx)
    {
        ctx.Worker.Goal = ActionKind.Meal;
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

        Eat(ctx);
        return true;
    }

    private static void Eat(WorkerContext ctx)
    {
        var food = ctx.Territory.ConsumeFood(ctx.Character, ctx.Worker.RoomId);
        if (food == null)
            return;

        var character = ctx.Character;
        character.Condition.Recover(
            Character.Traits.MealStamina(character, 80),
            Character.Traits.MealSpirit(character, 60), false);

        var tier = ctx.Territory.FoodTierOf(food);
        var bonus = tier switch
        {
            FoodTier.Delicate => 2,
            FoodTier.Feast => 4,
            FoodTier.Exquisite => 8,
            _ => 0,
        };

        character.Affect.AddMood(Character.Traits.ScaledMood(character, bonus) * Character.Traits.MealMoodPercent(character) / 100);

        var hasTable = ctx.Territory.Facilities.Exists(f => f.Built && f.RoomId == ctx.Worker.RoomId && f.IsTable);
        if (!hasTable)
            character.Affect.AddMood(-3);
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
        var chair = ctx.Territory.Facilities.Find(f => f.Id == worker.FacilityId);
        return chair == null
            ? $"{ctx.Character.Name}在{place}吃饭。"
            : $"{ctx.Character.Name}在{place}的{chair.Name}上吃饭。";
    }
}
