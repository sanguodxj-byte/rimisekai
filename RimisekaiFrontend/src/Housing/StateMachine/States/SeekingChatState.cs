using System;

namespace Rimisekai.Housing.StateMachine.States;

/// <summary>
/// 主动搭话状态：好感与搭话欲望蓄满后，角色主动寻路前往玩家所在房间找玩家搭话。
/// 若房门被阻挡则在门外等候，超时放弃并扣减少量心情（每次 -2，每日累计至多 -6，见 <see cref="Character.Affect.TakeIgnoredChat"/>）。
/// </summary>
public sealed class SeekingChatState : BaseWorkerState
{
    public override ActionKind Goal => ActionKind.SeekChat;

    public override void Enter(WorkerContext ctx)
    {
        ctx.Worker.Goal = ActionKind.SeekChat;
        ctx.Worker.Phase = ctx.Worker.Path.Count > 0 ? WorkPhase.Moving : WorkPhase.Idle;
    }

    public override bool Tick(WorkerContext ctx)
    {
        var worker = ctx.Worker;
        var character = ctx.Character;
        var territory = ctx.Territory;
        var stepCtx = ctx.StepContext;

        if (stepCtx == null)
            return true;

        // 主人出了领地：要找的人不在，作罢（不是被拒之门外，不扣心情）。
        if (stepCtx.PlayerRoomId < 0)
            return true;

        if (worker.WaitTicks <= 0)
        {
            character.Affect.TakeIgnoredChat(stepCtx.Day);
            character.Affect.ChatDesire = 50;
            return true; // 超时放弃
        }

        worker.WaitTicks--;

        if (worker.WantsChat)
        {
            if (worker.RoomId == stepCtx.PlayerRoomId)
            {
                ShareSeat(worker, territory, stepCtx);
                return false;
            }
            worker.WantsChat = false;
        }

        var target = territory.Rooms.Find(r => r.Id == stepCtx.PlayerRoomId);
        if (target == null || !Enterable(target, character, stepCtx))
        {
            // 进不去玩家的房间（如主人私室）：在门外等，并把等待写进状态行。
            if (!worker.SeekWaiting)
            {
                worker.SeekWaiting = true;
                stepCtx.Narrate(character, $"{character.Name}似乎想对你说什么。");
            }
            return false;
        }

        if (worker.RoomId != target.Id && worker.Path.Count == 0)
        {
            GotoRoom(worker, territory, target.Id, r => Enterable(r, character, stepCtx));
            if (worker.Path.Count == 0)
            {
                worker.SeekWaiting = true;
                return false;
            }
        }

        MoveAlong(worker, ctx);

        if (worker.RoomId == stepCtx.PlayerRoomId && worker.Path.Count == 0)
        {
            worker.WantsChat = true;
            worker.Phase = WorkPhase.Idle;
            if (worker.ChatRoom != worker.RoomId)
            {
                worker.ChatRoom = worker.RoomId;
                stepCtx.SeekDialogue(character, $"{character.Name}似乎想对你说什么。");
            }
            ShareSeat(worker, territory, stepCtx);
        }

        return false;
    }

    private static bool Enterable(Room? room, Character.CharacterState who, StepContext ctx)
    {
        if (room == null || !room.Open)
            return false;
        return room.Permission switch
        {
            RoomPermission.MasterOnly => who.Id == ctx.MasterId,
            RoomPermission.Faction => who.Id == ctx.MasterId || who.FactionId == ctx.MasterFactionId,
            _ => true,
        };
    }

    private static void ShareSeat(Worker worker, Territory territory, StepContext ctx)
    {
        if (worker.FacilityId >= 0 || ctx.PlayerFixtureId < 0)
            return;
        var fixture = territory.Facilities.Find(f => f.Id == ctx.PlayerFixtureId);
        if (fixture != null && fixture.Capacity > 1)
            worker.FacilityId = fixture.Id;
    }

    public override void Exit(WorkerContext ctx)
    {
        ctx.Worker.WaitTicks = 0;
        ctx.Worker.WantsChat = false;
        ctx.Worker.SeekWaiting = false;
        ctx.Worker.ChatRoom = -1;
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

        if (worker.WantsChat)
            return "";

        return worker.SeekWaiting
            ? $"{ctx.Character.Name}似乎想对你说什么。"
            : $"{ctx.Character.Name}正想找你说话。";
    }
}
