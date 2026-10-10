namespace Rimisekai.Housing.StateMachine.States;

/// <summary>
/// 跟随状态：接受邀请后跟着玩家走。玩家去哪间房就跟到哪间房；
/// 玩家坐下时，多人设施挨着坐，单人设施进不去就留在房间里等着。
/// 只有玩家躺上床再睡（同床走好感判定，见 HubSession 的设施行动），这里不主动上床。
/// </summary>
public sealed class FollowingState : BaseWorkerState
{
    public override ActionKind Goal => ActionKind.Follow;

    public override void Enter(WorkerContext ctx)
    {
        ctx.Worker.Goal = ActionKind.Follow;
        ctx.Worker.Task = ActionKind.None;
        ctx.Worker.Progress = 0;
        ctx.Worker.Path.Clear();
        ctx.Worker.FacilityId = -1;
        ctx.Worker.Phase = WorkPhase.Idle;
    }

    public override bool Tick(WorkerContext ctx)
    {
        var worker = ctx.Worker;
        var stepCtx = ctx.StepContext;
        if (stepCtx == null || stepCtx.PlayerRoomId < 0)
            return false;

        var target = ctx.Territory.Rooms.Find(r => r.Id == stepCtx.PlayerRoomId);
        if (target == null || !Enterable(target, ctx.Character, stepCtx))
        {
            // 玩家进了进不去的房（主人的私室）：在门外等着。
            ReleaseSeat(worker);
            ctx.Narrate($"{ctx.Character.Name}在门外等着。");
            return false;
        }

        if (worker.RoomId != target.Id)
        {
            ReleaseSeat(worker);
            if (worker.Path.Count == 0)
                GotoRoom(worker, ctx.Territory, target.Id, ctx.Character, r => Enterable(r, ctx.Character, stepCtx));
            if (worker.Path.Count == 0)
                return false;
            MoveAlong(worker, ctx);
            ctx.Narrate($"{ctx.Character.Name}跟着你。");
            return false;
        }

        worker.Phase = WorkPhase.Idle;
        ShareSeat(worker, ctx, stepCtx);
        ctx.Narrate($"{ctx.Character.Name}跟着你。");
        return false;
    }

    /// <summary>
    /// 玩家坐下的设施：多人设施且不是床就挨着坐（床要睡得走好感判定，
    /// 这里不上床）；单人设施进不去，留在房间里等着。
    /// </summary>
    private static void ShareSeat(Worker worker, WorkerContext ctx, StepContext stepCtx)
    {
        var fixture = stepCtx.PlayerFixtureId < 0
            ? null
            : ctx.Territory.Facilities.Find(f => f.Id == stepCtx.PlayerFixtureId);
        var share = fixture != null
            && fixture.Capacity > 1
            && !fixture.Supports(ActionKind.Sleep)
            && fixture.RoomId == worker.RoomId;
        if (!share)
        {
            ReleaseSeat(worker);
            return;
        }
        worker.FacilityId = fixture!.Id;
        worker.Phase = WorkPhase.Idle;
    }

    private static void ReleaseSeat(Worker worker)
    {
        worker.FacilityId = -1;
        worker.Phase = WorkPhase.Idle;
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

    public override void Exit(WorkerContext ctx)
    {
        // 不在这里释放座位：转去睡觉等新状态要接着用 FacilityId（玩家那张床、自己的床），
        // 清座位统一由 Enter 与 EndRoutine 负责。
    }

    public override string Describe(WorkerContext ctx) =>
        $"{ctx.Character.Name}跟着你。";
}
