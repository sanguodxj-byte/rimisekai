using System;
using Rimisekai.Character;
using Rimisekai.Clock;

namespace Rimisekai.Housing.StateMachine.States;

/// <summary>
/// 生产工作状态：在工作台或资源点进行采掘、采集、锻造或加工，
/// 消耗台面/背包物料，累积进度并将产出放入工匠背包。
/// </summary>
public sealed class WorkingState : BaseWorkerState
{
    public override ActionKind Goal => ActionKind.None;

    public override void Enter(WorkerContext ctx)
    {
        ctx.Worker.Goal = ActionKind.None;
        ctx.Worker.Phase = ctx.Worker.Path.Count > 0 ? WorkPhase.Moving : WorkPhase.Idle;
    }

    public override bool Tick(WorkerContext ctx)
    {
        var worker = ctx.Worker;
        var territory = ctx.Territory;
        var character = ctx.Character;

        if (worker.Path.Count > 0)
        {
            MoveAlong(worker, ctx);
            if (worker.Path.Count > 0)
                return false;
        }

        if (worker.Phase != WorkPhase.Working)
        {
            var targetFacility = territory.Facilities.Find(f => f.Id == worker.FacilityId && f.Built);
            if (targetFacility == null || ctx.UsedFacilities.Contains(targetFacility.Id))
            {
                worker.Phase = WorkPhase.Idle;
                return true; // 设施失效或被占，回决策
            }
            ctx.UsedFacilities.Add(targetFacility.Id);
            worker.Phase = WorkPhase.Working;
            worker.RoomId = targetFacility.RoomId;
        }

        var facility = territory.Facilities.Find(f => f.Id == worker.FacilityId && f.Built);
        if (facility == null)
            return true;

        var tick = ActionKindMap.IsExtractive(worker.Task)
            ? Territory.ProgressPerTick
            : Territory.CraftProgressPerTick;
        tick = Math.Max(1, (int)Math.Round(tick * character.Affect.Efficiency(), MidpointRounding.AwayFromZero));
        tick = Math.Max(1, tick * PersonalityTraits.WorkProgressPercent(character, worker.Task, ctx.CurrentHour) / 100);
        // 技能决定手快慢，与 Workday 的进度链同源。
        tick = Math.Max(1, tick * ActionKindMap.SpeedPercent(character, worker.Task) / 100);
        // 对口的房间（铁匠铺里锻造、木工房里木作……）手更快。
        tick = tick * territory.RoomWorkPercent(facility.RoomId, worker.Task) / 100;

        worker.Progress += tick;

        if (worker.Progress >= Territory.FinishAt)
        {
            var log = Finish(territory, character, worker, facility, ctx.YieldFor,
                ctx.StepContext?.Season ?? Season.Spring);
            if (log != null)
                ctx.WorkLogs.Add(log);
            worker.Progress = 0;
        }

        return false;
    }

    private static WorkLog? Finish(Territory territory, CharacterState character, Worker worker,
        Facility facility, Func<ActionKind, int>? yieldFor, Season season)
    {
        if (ActionKindMap.IsExtractive(worker.Task))
        {
            // 耕地按播种/收获结算，不是无中生有的抽取；生长中这一格空过。
            var farm = territory.FarmWork(character, facility, worker.Task, season, yieldFor, out var handled);
            if (handled)
            {
                character.Condition.Spend(0, 0);
                // 耕完这一下若地里已无事可做，回决策层重挑。
                if (territory.PlotState(facility, season, character)
                    is Territory.FarmState.Growing
                    or Territory.FarmState.OutOfSeason
                    or Territory.FarmState.NoSeed)
                {
                    worker.Goal = ActionKind.None;
                    worker.Task = ActionKind.None;
                    worker.Phase = WorkPhase.Idle;
                }
                return farm;
            }
            var amount = Math.Clamp(Math.Max(1, character.Life(ActionKindMap.SkillOf(worker.Task)!.Value)) / 40, 1, 4);
            if (yieldFor != null)
                amount = Math.Max(1, amount * yieldFor(worker.Task) / 100);
            if (facility.YieldItemId.Length > 0)
                territory.Produce(character, facility.YieldItemId, amount);
            character.GainLifeExp(ActionKindMap.SkillOf(worker.Task)!.Value, Territory.GatherExp);
            character.Condition.Spend(0, 0);
            return new WorkLog
            {
                CharacterId = worker.CharacterId,
                Task = worker.Task,
                ItemId = facility.YieldItemId,
                Count = amount,
                Skill = ActionKindMap.SkillOf(worker.Task)!.Value,
                Exp = Territory.GatherExp,
            };
        }

        var recipe = territory.Recipes.Find(r => territory.Makes(r, worker.Task, facility) && territory.CanPayAt(facility, character, r.Costs));
        if (recipe == null || !territory.PayAt(facility, character, recipe.Costs))
            return null;

        territory.Finish(character, recipe);
        character.GainLifeExp(recipe.Skill, Territory.CraftExp);
        return new WorkLog
        {
            CharacterId = worker.CharacterId,
            Task = worker.Task,
            ItemId = recipe.ItemId,
            Count = recipe.OutputCount,
            Skill = recipe.Skill,
            Exp = Territory.CraftExp,
        };
    }

    public override void Exit(WorkerContext ctx)
    {
        if (ctx.Worker.FacilityId >= 0)
        {
            ctx.UsedFacilities?.Remove(ctx.Worker.FacilityId);
        }
        ctx.Worker.Phase = WorkPhase.Idle;
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
        var station = ctx.Territory.Facilities.Find(f => f.Id == worker.FacilityId);
        return station == null
            ? $"{ctx.Character.Name}在{place}干活。"
            : $"{ctx.Character.Name}在{place}的{station.Name}干活。";
    }
}
