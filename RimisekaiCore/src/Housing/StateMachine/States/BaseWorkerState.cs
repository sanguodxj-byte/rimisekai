using System;
using System.Collections.Generic;
using Rimisekai.Clock;
using Rimisekai.Defs;

namespace Rimisekai.Housing.StateMachine.States;


/// <summary>
/// 状态基类，提供通用的寻路、移动、房间地名与设施查询公用逻辑。
/// </summary>
public abstract class BaseWorkerState : IWorkerState
{
    public abstract ActionKind Goal { get; }
    public virtual JobDef? JobDef => DefDatabase<JobDef>.Get(Goal.ToString());

    public virtual void Enter(WorkerContext ctx) { }
    public abstract bool Tick(WorkerContext ctx);
    public virtual void Exit(WorkerContext ctx) { }
    public abstract string Describe(WorkerContext ctx);
    public virtual bool CanInterrupt(WorkerContext ctx) => true;

    protected static string GetPlaceName(WorkerContext ctx)
    {
        var room = ctx.Territory.Rooms.Find(r => r.Id == ctx.Worker.RoomId);
        return room == null ? "" : room.Name;
    }

    /// <summary>
    /// 沿 Path 前进一格。若还在路上则返回 true，已到达则返回 false。
    /// 到达即把房间落到当前格，并按有没有目标设施决定入座还是就地待着。
    /// 恶劣天气赶路：每进一个室外房间都更费劲。
    /// </summary>
    protected static bool MoveAlong(Worker worker, WorkerContext ctx)
    {
        if (worker.Path.Count > 0)
        {
            var next = ctx.Territory.Rooms.Find(r => r.Id == worker.Path.Peek());
            // 路上那间房锁上了（主人进屋睡下、或刚拧了锁）：走不过去，就地停下。
            if (next != null && ctx.Territory.IsLocked(next))
            {
                worker.Path.Clear();
                worker.Phase = WorkPhase.Idle;
                ctx.Narrate($"{ctx.Character.Name}被关在{next.Name}门外。");
                return true;
            }
            worker.RoomId = worker.Path.Dequeue();
            if (ctx.StepContext != null)
                WorldEffects.SpendMoveStamina(
                    ctx.Character, ctx.Territory, worker.RoomId, ctx.StepContext.Weather);
            if (worker.Path.Count > 0)
            {
                worker.Phase = WorkPhase.Moving;
                return true;
            }
            worker.Phase = worker.FacilityId >= 0 ? WorkPhase.Working : WorkPhase.Idle;
            return false;
        }
        worker.Phase = worker.FacilityId >= 0 ? WorkPhase.Working : WorkPhase.Idle;
        return false;
    }

    public static List<int> Route(Territory territory, int fromRoom, int toRoom, Func<Room, bool>? passable = null)
    {
        var rooms = territory.Rooms;
        var queue = new Queue<int>();
        var prev = new Dictionary<int, int> { [fromRoom] = -1 };
        queue.Enqueue(fromRoom);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (id == toRoom)
                break;
            var room = rooms.Find(r => r.Id == id);
            if (room == null)
                continue;
            foreach (var next in room.Links)
            {
                if (prev.ContainsKey(next))
                    continue;
                var node = rooms.Find(r => r.Id == next);
                if (node == null || !node.Open)
                    continue;
                if (next != toRoom && passable != null && !passable(node))
                    continue;
                prev[next] = id;
                queue.Enqueue(next);
            }
        }
        var path = new List<int>();
        if (!prev.ContainsKey(toRoom))
            return path;
        for (var id = toRoom; id != fromRoom; id = prev[id])
            path.Add(id);
        path.Reverse();
        return path;
    }

    public static void GotoRoom(Worker worker, Territory territory, int targetRoom, Func<Room, bool>? enterable = null)
    {
        worker.Path.Clear();
        if (worker.RoomId == targetRoom)
            return;

        var path = Route(territory, worker.RoomId, targetRoom, enterable);
        foreach (var r in path)
            worker.Path.Enqueue(r);
    }
}
