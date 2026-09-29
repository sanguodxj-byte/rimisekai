using System;
using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Voice;

namespace Rimisekai.Housing;

public enum WorkPhase
{
    Idle,
    Moving,
    Working,
}


/// <summary>自主行动需要的世界上下文。主界面每步推进时填好传进来。</summary>
public sealed class StepContext
{
    public int PlayerRoomId { get; set; } = -1;
    public int PlayerFixtureId { get; set; } = -1;
    public int MasterId { get; set; } = -1;
    public int MasterFactionId { get; set; } = -1;
    public int Day { get; set; } = 1;
    public int NowTotal { get; set; }

    /// <summary>
    /// 每个角色此刻在做什么，一行一个角色。键是角色 Id。
    /// 日志是快照不是流水：同一步内后写覆盖先写，因此一个角色只会留一行、
    /// 且始终是当前行为，不会把过程累积成好几行。
    /// </summary>
    public Dictionary<int, string> Activity { get; } = new();

    /// <summary>
    /// 自动节律里角色开口的出口。为 null 时 Seek 退回叙述文案，
    /// 因此不接台词系统也能照常跑（旧行为）。
    /// </summary>
    public IVoiceSink? Voice { get; set; }

    /// <summary>
    /// 记下某角色此刻在做什么。覆盖该角色上一次的记录——日志只显示当前行为。
    /// </summary>
    public void Narrate(CharacterState who, string text)
    {
        if (text.Length > 0)
            Activity[who.Id] = text;
    }

    /// <summary>
    /// 角色主动找玩家搭话：台词进对话框（弹层唯一的自动入口）；
    /// 没说成时退回这条行为叙述。返回是否真的走了对话框——
    /// 走了对话框就不再往日志写同一句，避免两边重复。
    /// </summary>
    public bool SeekDialogue(CharacterState who, string fallback)
    {
        if (Voice?.Dialogue(who, VoiceTrigger.Seek) == true)
            return true;
        Narrate(who, fallback);
        return false;
    }
}

/// <summary>一个被委派角色此刻在干什么。走路占分钟，到设施后占座并攒进度。</summary>
public sealed class Worker
{
    public int CharacterId { get; init; }
    public WorkPhase Phase { get; set; } = WorkPhase.Idle;
    public int RoomId { get; set; } = -1;
    public int FacilityId { get; set; } = -1;
    public ActionKind Task { get; set; } = ActionKind.None;
    public int Progress { get; set; }
    public Queue<int> Path { get; } = new();
    public ActionKind Goal { get; set; } = ActionKind.None;
    public int WaitTicks { get; set; }
    public bool WantsChat { get; set; }
    public bool SeekWaiting { get; set; }
    public int ChatRoom { get; set; } = -1;
    public Dictionary<int, int> Together { get; } = new();

    /// <summary>
    /// 当前闲时活动做的是哪件事（坐下歇脚 / 在房里忙活 / 串门）。
    /// 用来写日志，也用来决定要坐哪件设施。
    /// </summary>
    public LoiterKind Loiter { get; set; } = LoiterKind.None;

    /// <summary>这件闲时活动还剩几格。到点重新挑，避免一站一整天。</summary>
    public int LoiterTicks { get; set; }

    /// <summary>闲时活动是否已经写过日志。动作开头写一次，不逐格刷屏。</summary>
    public bool LoiterAnnounced { get; set; }

    /// <summary>
    /// 娱乐还在这儿待几格。到了才走，避免“进戏台看一眼就走”的假动作。
    /// </summary>
    public int PlayTicks { get; set; }

    /// <summary>正在搬运的物品 Id；空表示这一趟没东西可搬。</summary>
    public string HaulItemId { get; set; } = "";

    /// <summary>正在搬运的件数。</summary>
    public int HaulCount { get; set; }

    /// <summary>这一趟要把东西送到哪件仓储（目标设施 Id）。</summary>
    public int HaulTargetId { get; set; } = -1;

    /// <summary>角色行为状态机，管理当前执行的 Job 及其生命周期与快照描述。</summary>
    public StateMachine.WorkerStateMachine StateMachine { get; } = new();

    /// <summary>
    /// 角色当前状态翻译成内容包认识的活动。这是**事实**，不是文本——
    /// 地文包按它挑句子。睡觉/吃饭/找人/闲转优先，其余看走路或具体劳作。
    /// </summary>
    internal static VoiceActivity ActivityOf(Worker worker) => worker.Goal switch
    {
        ActionKind.Sleep => VoiceActivity.Sleeping,
        ActionKind.Meal => VoiceActivity.Eating,
        ActionKind.SeekChat => VoiceActivity.Seeking,
        ActionKind.Loiter => VoiceActivity.Playing,
        ActionKind.Rest => VoiceActivity.Resting,
        _ => worker.Phase == WorkPhase.Moving ? VoiceActivity.Moving : ActionActivity(worker.Task),
    };

    private static VoiceActivity ActionActivity(ActionKind action) => action switch
    {
        ActionKind.Cook => VoiceActivity.Cooking,
        ActionKind.Mine or ActionKind.Fell => VoiceActivity.Mining,
        ActionKind.Till or ActionKind.Tend => VoiceActivity.Farming,
        ActionKind.Tinker or ActionKind.Sew => VoiceActivity.Crafting,
        ActionKind.Woodwork or ActionKind.Forge or ActionKind.Brew => VoiceActivity.Working,
        ActionKind.Train => VoiceActivity.Training,
        _ => VoiceActivity.Idle,
    };

    /// <summary>角色正用的设施名。没有则为空串。也是事实，供地文包拼句。</summary>
    internal static string FacilityNameOf(Territory territory, Worker worker) =>
        worker.FacilityId < 0
            ? ""
            : territory.Facilities.Find(f => f.Id == worker.FacilityId)?.Name ?? "";
}

/// <summary>闲时活动的种类。无委派角色靠它“过日子”，而不是原地发呆。</summary>
public enum LoiterKind
{
    None,

    /// <summary>找件能坐的东西歇着（沙发、椅子）。</summary>
    Sitting,

    /// <summary>在房里做点零活。仅女仆会打扫；其余人闲时只是待着。</summary>
    Chores,

    /// <summary>溜达到别的房间串门。</summary>
    Wandering,
}

public sealed class TerritoryClock
{
    public const int StepMinutes = 5;
    public const int ChatThreshold = 100;
    public const int SeekBudget = 36;
    public const int WakeHourDefault = 6;
    public const int WakeHourLazy = 8;
    public const int BedHourDefault = 22;
    public const int BedHourCurious = 23;

    public Random Rng { get; set; } = new Random();

    private readonly List<Worker> _workers = new();

    public IReadOnlyList<Worker> Workers => _workers;

    public Worker Track(int characterId, int roomId)
    {
        var found = _workers.Find(w => w.CharacterId == characterId);
        if (found != null)
            return found;
        found = new Worker { CharacterId = characterId, RoomId = roomId };
        _workers.Add(found);
        return found;
    }

    /// <summary>推进一个时间步。换委派槽时重新找设施；座位满则改走后备。
    /// 传 ctx 才跑自主节律（睡眠/三餐/找人/娱乐/休息），不传保持旧行为。</summary>
    public List<WorkLog> Step(Territory territory, Roster roster, int slot, Func<ActionKind, int>? yieldFor = null, StepContext? ctx = null)
    {
        var logs = new List<WorkLog>();
        var used = Seats();
        foreach (var character in roster.Members)
        {
            if (character.IsMaster)
                continue;
            var worker = Track(character.Id, workerRoom(character.Id));
            var mode = territory.ScheduleOf(character.Id).Slots[slot];
            if (ctx != null)
            {
                UpdateRoutine(character, worker, territory, roster, mode, ctx, used);
                // 只有起居/自主行为进 ProcessRoutine；工作行动在主循环内联结算（见下方 tick）。
                if (worker.Goal != ActionKind.None && !ActionKindMap.IsWork(worker.Goal))
                {
                    ProcessRoutine(character, worker, territory, roster, ctx);
                    // 这一支可能刚把活动做完（Goal 归 None）。那种情况下不写行，
                    // 保留上一行的行为描述——下一格会立刻重挑一件事，不必露出“发呆”的空档。
                    if (worker.Goal != ActionKind.None)
                        ctx.Narrate(character, Describe(character, worker, territory, ctx));
                    continue;
                }
                if (worker.Goal == ActionKind.None)
                {
                    // 活动刚结束，下一格就重挑；不写“发呆”，保留上一行的描述。
                    continue;
                }
            }
            else
            {
                // 无上下文（旧路径 / 测试）：直接按优先级挑活，累了或不肯干就歇着。
                var task = mode == SlotMode.Work ? PickWork(character, worker, territory, used) : ActionKind.None;
                if (task == ActionKind.None || character.Condition.Tired)
                {
                    Release(worker, used);
                    worker.Task = ActionKind.None;
                    worker.Phase = WorkPhase.Idle;
                    continue;
                }
                if (worker.Task != task)
                    Retarget(territory, worker, task, used);
            }
            // 干活时 Goal 就是那件具体的活（原 Assigned 语义）。
            worker.Goal = worker.Task;
            if (worker.Phase == WorkPhase.Moving && worker.Path.Count > 0)
            {
                worker.RoomId = worker.Path.Dequeue();
                if (worker.Path.Count == 0)
                    Sit(territory, worker, used);
            }
            else if (worker.Phase == WorkPhase.Working)
            {
                var facility = territory.Facilities.Find(f => f.Id == worker.FacilityId && f.Built);
                if (facility == null)
                {
                    if (ctx != null)
                        ctx.Narrate(character, Describe(character, worker, territory, ctx));
                    continue;
                }
                var tick = ActionKindMap.IsExtractive(worker.Task)
                    ? Territory.ProgressPerTick
                    : Territory.CraftProgressPerTick;
                tick = System.Math.Max(1, (int)System.Math.Round(tick * character.Affect.Efficiency(), System.MidpointRounding.AwayFromZero));
                tick = System.Math.Max(1, tick * Traits.WorkProgressPercent(character, worker.Task, ctx == null ? 12 : ctx.NowTotal / 60 % 24) / 100);
                // 技能决定手快慢：同一个人干对口的活更快，干不对口的更慢。
                tick = System.Math.Max(1, tick * ActionKindMap.SpeedPercent(character, worker.Task) / 100);
                worker.Progress += tick;
                if (character.Condition.Fatigue >= 100)
                    character.Affect.AddMood(-1);
                if (worker.Progress >= Territory.FinishAt)
                {
                    var log = Finish(territory, character, worker, facility, yieldFor);
                    if (log != null)
                        logs.Add(log);
                    worker.Progress = 0;
                }
            }
            if (ctx != null)
                ctx.Narrate(character, Describe(character, worker, territory, ctx));
        }
        return logs;
    }

    /// <summary>
    /// 这个角色此刻在做什么，一句话。日志是快照：每个角色恒定一行，
    /// 每次推进覆盖，所以只会看到当前行为，不会堆成流水账。
    ///
    /// 文本归地文包：先问角色的状态地文（VoiceTrigger.State），挑不出就返回空串
    /// ——**什么都不显示**。引擎不提供通用兜底句，因为替角色编造行为是错的。
    /// </summary>
    private static string Describe(CharacterState character, Worker worker, Territory territory, StepContext ctx)
    {
        var stateText = ctx.Voice?.StateLine(character, Worker.ActivityOf(worker), Worker.FacilityNameOf(territory, worker));
        if (stateText != null)
            return stateText;
        if (ctx.Voice != null)
            return "";
        var workerCtx = new StateMachine.WorkerContext
        {
            Territory = territory,
            Character = character,
            Worker = worker,
            StepContext = ctx,
        };

        if (worker.StateMachine.CurrentState != null)
        {
            var text = worker.StateMachine.Describe(workerCtx);
            if (!string.IsNullOrEmpty(text))
                return text;
        }

        var room = territory.Rooms.Find(r => r.Id == worker.RoomId);
        var place = room == null ? "" : room.Name;

        // 在路上：写明正去哪儿。
        if (worker.Phase == WorkPhase.Moving && worker.Path.Count > 0)
        {
            var next = territory.Rooms.Find(r => r.Id == worker.Path.Peek());
            return next == null
                ? $"{character.Name}在路上。"
                : $"{character.Name}正往{next.Name}去。";
        }

        switch (worker.Goal)
        {
            case ActionKind.Sleep:
                var bed = territory.Facilities.Find(f => f.Id == worker.FacilityId);
                return bed == null
                    ? $"{character.Name}在{place}睡觉。"
                    : $"{character.Name}在{place}的{bed.Name}上睡觉。";
            case ActionKind.Meal:
                var chair = territory.Facilities.Find(f => f.Id == worker.FacilityId);
                return chair == null
                    ? $"{character.Name}在{place}吃饭。"
                    : $"{character.Name}在{place}的{chair.Name}上吃饭。";
            case ActionKind.Rest:
                var restSeat = territory.Facilities.Find(f => f.Id == worker.FacilityId);
                return restSeat == null
                    ? $"{character.Name}在{place}歇着。"
                    : $"{character.Name}在{place}的{restSeat.Name}上歇着。";
            case ActionKind.SeekChat:
                // 已经走到你面前、对话框弹出时，日志不再重复一遍——
                // 那句由对话框承担；只有进不去、在原地等时才写进日志。
                if (worker.WantsChat)
                    return "";
                return worker.SeekWaiting
                    ? $"{character.Name}似乎想对你说什么。"
                    : $"{character.Name}正想找你说话。";
            case ActionKind.Loiter:
                return DescribeLoiter(character, worker, territory, place);
            case ActionKind.Haul:
                var target = territory.Facilities.Find(f => f.Id == worker.HaulTargetId);
                return target == null
                    ? $"{character.Name}正把{worker.HaulItemId}送去{place}。"
                    : $"{character.Name}正把{worker.HaulItemId}送去{place}的{target.Name}。";
            case ActionKind.None:
                var station = territory.Facilities.Find(f => f.Id == worker.FacilityId);
                return station == null
                    ? $"{character.Name}在{place}干活。"
                    : $"{character.Name}在{place}的{station.Name}干活。";
            default:
                return $"{character.Name}在{place}发呆。";
        }
    }

    /// <summary>闲时活动的一句话描述。女仆在房里没坐上设施时写成打扫卫生，其余人写"待着"。</summary>
    private static string DescribeLoiter(CharacterState character, Worker worker,
        Territory territory, string place)
    {
        switch (worker.Loiter)
        {
            case LoiterKind.Sitting:
                var seat = territory.Facilities.Find(f => f.Id == worker.FacilityId);
                return seat == null
                    ? $"{character.Name}在{place}找了个地方歇着。"
                    : $"{character.Name}在{place}的{seat.Name}上歇着。";
            case LoiterKind.Wandering:
                return $"{character.Name}在{place}转悠。";
            default:
                // 闲时做零活是女仆的自觉；其余人闲下来只是待着，不替她们编活儿。
                if (!character.Has(Trait.Maid))
                    return $"{character.Name}在{place}待着。";
                var fac = territory.Facilities.Find(f => f.Id == worker.FacilityId);
                return fac == null
                    ? $"{character.Name}在{place}打扫卫生。"
                    : $"{character.Name}在{place}打扫{fac.Name}。";
        }
    }

    /// <summary>
    /// 工作时段挑一件活干：按优先级档位从高到低（1 最优先），
    /// 同一档内按工作类型行序；挑第一件"有设施、有空位、这个人也肯干"的。
    /// 挑不出返回 Free，交给后续的娱乐/休息/搬运/闲时。
    /// </summary>
    private static ActionKind PickWork(CharacterState character, Worker worker, Territory territory, Dictionary<int, int> used)
    {
        foreach (var task in territory.TasksByPriority(character.Id))
        {
            if (!character.WillWork(WorkTypeMap.IsHard(ActionKindMap.TypeOf(task)!.Value)) || !character.Affect.AcceptsWork())
                continue;
            // 自己正占着的那件设施不算“被占”——否则从第二格起就看不见自己的活了。
            var facility = Pick(territory, task, used, worker.FacilityId);
            if (facility != null)
                return task;
        }
        return ActionKind.None;
    }

    private void Retarget(Territory territory, Worker worker, ActionKind task, Dictionary<int, int> used)
    {
        Release(worker, used);
        worker.Task = task;
        worker.Progress = 0;
        worker.Path.Clear();
        var facility = Pick(territory, task, used);
        if (facility == null)
        {
            worker.Phase = WorkPhase.Idle;
            return;
        }
        worker.FacilityId = facility.Id;
        if (facility.RoomId == worker.RoomId)
            Sit(territory, worker, used);
        else
        {
            foreach (var step in Route(territory, worker.RoomId, facility.RoomId))
                worker.Path.Enqueue(step);
            worker.Phase = worker.Path.Count > 0 ? WorkPhase.Moving : WorkPhase.Idle;
        }
    }

    private static void Sit(Territory territory, Worker worker, Dictionary<int, int> used)
    {
        var facility = territory.Facilities.Find(f => f.Id == worker.FacilityId);
        if (facility == null || used.GetValueOrDefault(facility.Id) >= facility.Capacity)
        {
            worker.Phase = WorkPhase.Idle;
            return;
        }
        used[facility.Id] = used.GetValueOrDefault(facility.Id) + 1;
        worker.Phase = WorkPhase.Working;
        worker.RoomId = facility.RoomId;
    }

    private static WorkLog? Finish(Territory territory, CharacterState character, Worker worker, Facility facility, Func<ActionKind, int>? yieldFor)
    {
        if (ActionKindMap.IsExtractive(worker.Task))
        {
            var amount = System.Math.Clamp(System.Math.Max(1, character.Life(ActionKindMap.SkillOf(worker.Task)!.Value)) / 40, 1, 4);
            if (yieldFor != null)
                amount = System.Math.Max(1, amount * yieldFor(worker.Task) / 100);
            if (facility.YieldItemId.Length > 0)
                territory.Produce(character, facility.YieldItemId, amount);
            character.GainLifeExp(ActionKindMap.SkillOf(worker.Task)!.Value, Territory.GatherExp);
            character.Condition.Spend(0, 0, Traits.ScaledFatigue(character, 5));
            character.Condition.Apply(character);
            return Log(worker, facility.YieldItemId, amount, ActionKindMap.SkillOf(worker.Task)!.Value);
        }
        // 工作台只用“这个人背包 + 这台子自己的存货”付料：
        // 材料得有人搬过来，不能隔空从别的货架取（RimWorld 的备料口径）。
        var recipe = territory.Recipes.Find(r => r.Station == worker.Task && territory.CanPayAt(facility, character, r.Costs));
        if (recipe == null || !territory.PayAt(facility, character, recipe.Costs))
            return null;
        territory.Produce(character, recipe.ItemId, recipe.OutputCount);
        character.GainLifeExp(recipe.Skill, Territory.CraftExp);
        return Log(worker, recipe.ItemId, recipe.OutputCount, recipe.Skill);
    }

    private static WorkLog Log(Worker worker, string itemId, int count, LifeSkill skill) => new()
    {
        CharacterId = worker.CharacterId,
        Task = worker.Task,
        ItemId = itemId,
        Count = count,
        Skill = skill,
        Exp = Territory.GatherExp,
    };

    private Dictionary<int, int> Seats()
    {
        var used = new Dictionary<int, int>();
        foreach (var worker in _workers)
        {
            if (worker.FacilityId >= 0 && (worker.Phase == WorkPhase.Working || worker.Path.Count == 0))
                used[worker.FacilityId] = used.GetValueOrDefault(worker.FacilityId) + 1;
        }
        return used;
    }

    private static void Release(Worker worker, Dictionary<int, int> used)
    {
        if (worker.Phase != WorkPhase.Working)
            return;
        var left = used.GetValueOrDefault(worker.FacilityId) - 1;
        if (left <= 0)
            used.Remove(worker.FacilityId);
        else
            used[worker.FacilityId] = left;
        worker.Phase = WorkPhase.Idle;
    }

    private static Facility? Pick(Territory territory, ActionKind task, Dictionary<int, int> used, int owned = -1) =>
        territory.Facilities.Find(f => f.Built && f.Supports(task)
            && (f.Id == owned || used.GetValueOrDefault(f.Id) < f.Capacity));

    private static List<int> Route(Territory territory, int fromRoom, int toRoom, Func<Room, bool>? passable = null)
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

    /// <summary>节律决策。睡眠打断一切；工作时段按优先级挑活；闲时按三餐>找人>娱乐>休息选。</summary>
    private void UpdateRoutine(CharacterState character, Worker worker, Territory territory, Roster roster, SlotMode mode, StepContext ctx, Dictionary<int, int> used)
    {
        DriftMood(character, worker, ctx);
        GrowDesire(character, ctx);
        TrackTogether(character, worker, roster, ctx);
        if (ctx.Day != character.Affect.LastBoredDay && ctx.Day - character.Affect.LastPlayDay > 2)
        {
            character.Affect.LastBoredDay = ctx.Day;
            character.Affect.AddMood(-5);
        }
        var minutes = ctx.NowTotal % 1440;
        var night = IsNight(minutes, character);

        if (worker.Goal == ActionKind.Sleep)
        {
            if (!night && !character.Condition.Tired)
            {
                WakeUp(character, worker);
                EndRoutine(worker);
            }
            return;
        }

        // 睡觉打断一切（含闲时活动）。但睡觉必须到床上——
        // 没有床就睡不成，退回下面的日常决策（歇着/串门/零活），不在地上睡。
        if (character.Condition.Tired || night)
        {
            // 已在睡的那一支在上面就返回了，走到这里一定是刚决定要睡。
            if (StartSleep(character, worker, territory, roster, ctx, used))
                return;
        }

        // 正在进行的活动（三餐途中 / 歇脚 / 找人 / 娱乐 / 搬运）不重做决策，
        // 由 ProcessRoutine 自己推进；但三餐与找人这类需求可以打断闲时活动与搬运。
        var interruptible = worker.Goal is ActionKind.Loiter or ActionKind.Haul;
        if (worker.Goal != ActionKind.None && worker.Goal != ActionKind.None && !interruptible)
            return;

        var window = MealWindow(minutes);
        if (window >= 0 && (character.Affect.LastMealDay != ctx.Day || character.Affect.LastMealWindow != window))
        {
            StartMeal(character, worker, territory, roster, ctx, window, used);
            return;
        }
        if (character.Affect.ChatDesire >= SeekThreshold(character))
        {
            StartSeek(character, worker, used);
            return;
        }
        if (interruptible)
            return;

        var work = mode == SlotMode.Work ? PickWork(character, worker, territory, used) : ActionKind.None;
        if (work != ActionKind.None)
        {
            // 工作台缺料就先去搬料，搬齐了再开工（RimWorld 的备料）。
            // 放在“认定委派”之前，否则会一直在空台子前干等。
            if (StartFetchForBench(character, worker, territory, ctx, work))
                return;
            if (worker.Task != work)
                Retarget(territory, worker, work, used);
            // 认定委派：Goal 就是那件活（原 Assigned 语义），主循环据此结算进度。
            worker.Goal = work;
            return;
        }
        if (worker.Goal == ActionKind.None)
            Release(worker, used);
        if (ctx.Day - character.Affect.LastPlayDay >= 1)
        {
            if (StartPlay(character, worker, territory, roster, ctx, used))
                return;
        }
        if (mode == SlotMode.Rest || character.Condition.Spirit < character.Condition.MaxSpirit * 3 / 10)
        {
            StartRest(character, worker, territory, ctx, used);
            return;
        }
        // 背包里有东西、据点又有能收的仓储：先把货送过去（RimWorld 的 haul）。
        // 排在闲时活动之前，免得背着满包东西到处逛。
        if (StartHaul(character, worker, territory, ctx, used))
            return;
        // 无事可做：过自己的日子（坐下歇着 / 在房里忙活 / 串门），不原地发呆。
        StartLoiter(character, worker, territory, roster, ctx, used);
    }

    /// <summary>
    /// 备料：当前委派的工作台缺材料时，去仓储把缺的那一样搬过来。
    /// 搬到位后下次决策就能开工（<see cref="Finish"/> 只认台子上的料）。
    /// </summary>
    private static bool StartFetchForBench(CharacterState character, Worker worker,
        Territory territory, StepContext ctx, ActionKind task)
    {
        var bench = territory.Facilities.Find(f => f.Built && f.Supports(task));
        if (bench == null)
            return false;

        var recipe = territory.Recipes.Find(r => r.Station == task);
        if (recipe == null)
            return false;

        foreach (var cost in recipe.Costs)
        {
            var onBench = bench.Contents.Get(cost.ItemId) + character.Bag.Get(cost.ItemId);
            if (onBench >= cost.Count)
                continue;
            // 该物品在别的仓储里有货：去取。
            var source = territory.FindStockOf(cost.ItemId, worker.RoomId);
            if (source == null || source.Id == bench.Id)
                continue;

            var need = cost.Count - onBench;
            var moved = territory.TakeFrom(character, source, cost.ItemId, need);
            if (moved <= 0)
                continue;

            worker.Goal = ActionKind.Haul;
            worker.Task = ActionKind.None;
            worker.Progress = 0;
            worker.HaulItemId = cost.ItemId;
            worker.HaulCount = moved;
            worker.HaulTargetId = bench.Id;
            worker.FacilityId = -1;
            GotoRoom(worker, territory, bench.RoomId, r => Enterable(r, character, ctx));
            worker.Phase = worker.Path.Count > 0 ? WorkPhase.Moving : WorkPhase.Idle;
            worker.StateMachine.TransitionTo(new StateMachine.States.HaulingState(), new StateMachine.WorkerContext
            {
                Territory = territory,
                Character = character,
                Worker = worker,
                StepContext = ctx,
            });
            return true;
        }
        return false;
    }

    /// <summary>
    /// 挑一件要搬的东西：背包里找一样有仓储收得下的，记下目标设施。
    /// 没得搬返回 false，交给后续决策。
    /// </summary>
    private static bool StartHaul(CharacterState character, Worker worker,
        Territory territory, StepContext ctx, Dictionary<int, int> used)
    {
        foreach (var pair in character.Bag.Items)
        {
            if (pair.Value <= 0)
                continue;
            var storage = territory.FindStorageFor(pair.Key, worker.RoomId);
            if (storage == null)
                continue;

            worker.Goal = ActionKind.Haul;
            worker.Task = ActionKind.None;
            worker.Progress = 0;
            worker.HaulItemId = pair.Key;
            worker.HaulCount = pair.Value;
            worker.HaulTargetId = storage.Id;
            worker.FacilityId = -1;
            GotoRoom(worker, territory, storage.RoomId, r => Enterable(r, character, ctx));
            worker.Phase = worker.Path.Count > 0 ? WorkPhase.Moving : WorkPhase.Idle;
            worker.StateMachine.TransitionTo(new StateMachine.States.HaulingState(), new StateMachine.WorkerContext
            {
                Territory = territory,
                Character = character,
                Worker = worker,
                StepContext = ctx,
            });
            return true;
        }
        return false;
    }

    /// <summary>
    /// 搬运一步：走到目标跟前就把货放下。目标是仓储就走容量/过滤检查；
    /// 目标是工作台则直接卸在台面上（台面放自己的料，不受仓储规则限制）。
    /// </summary>
    private static void ProcessHaul(CharacterState character, Worker worker, Territory territory, StepContext ctx)
    {
        if (worker.Path.Count > 0)
        {
            MoveAlong(worker);
            if (worker.Path.Count > 0)
                return;
        }

        var target = territory.Facilities.Find(f => f.Id == worker.HaulTargetId && f.Built);
        if (target == null)
        {
            EndHaul(worker);
            return;
        }

        var moved = target.CanStore
            ? territory.StoreFrom(character, target, worker.HaulItemId, worker.HaulCount)
            : Deposit(character, target, worker.HaulItemId, worker.HaulCount);

        if (moved > 0)
            ctx.Narrate(character, $"{character.Name}把{worker.HaulItemId}放到了{target.Name}。");
        EndHaul(worker);
    }

    /// <summary>把背包里的东西直接卸到设施台面（工作台备料用，不受仓储容量限制）。</summary>
    private static int Deposit(CharacterState who, Facility target, string itemId, int count)
    {
        if (count <= 0 || itemId.Length == 0)
            return 0;
        var moved = System.Math.Min(count, who.Bag.Get(itemId));
        if (moved <= 0)
            return 0;
        who.Bag.Add(itemId, -moved);
        target.Contents.Add(itemId, moved);
        return moved;
    }

    /// <summary>收尾搬运状态，回决策。</summary>
    private static void EndHaul(Worker worker)
    {
        worker.HaulItemId = "";
        worker.HaulCount = 0;
        worker.HaulTargetId = -1;
        EndRoutine(worker);
    }

    /// <summary>
    /// 闲时活动：挑一件写得出名字的事做。无委派时这就是角色的日常。
    /// 坐着歇脚 / 出门串门 / 在房里忙活三者按素质加权掷骰，
    /// 每次重新挑，所以一天下来是“到处闲逛、或在哪儿歇着”的样子，而不是钉在一处。
    /// </summary>
    private void StartLoiter(CharacterState character, Worker worker, Territory territory,
        Roster roster, StepContext ctx, Dictionary<int, int> used)
    {
        Release(worker, used);
        worker.Goal = ActionKind.Loiter;
        worker.Task = ActionKind.None;
        worker.Progress = 0;
        worker.Path.Clear();
        worker.FacilityId = -1;
        worker.LoiterAnnounced = false;
        worker.LoiterTicks = Traits.LoiterTicksFor(character);

        var seats = ReachableSeats(territory, worker.RoomId, character, ctx, used);
        var rooms = ReachableRooms(territory, worker.RoomId, character, ctx);
        rooms.Remove(worker.RoomId);

        // 玩家在同一间房时不出门：否则玩家想搭话/送礼，人已经溜走了。
        // 这也是“懒散”该有的样子——待在原地歇着或做点零活。
        var playerHere = worker.RoomId == ctx.PlayerRoomId;
        if (playerHere)
            rooms.Clear();

        // 权重：懒散/疲惫偏坐着歇，好奇/精神好偏走动，其余留在房里忙活。
        // 坐着与忙活是常态（“懒散地过一天”），走动是偶尔（“到处闲逛”）。
        var sitWeight = 45;
        sitWeight += Traits.SitWeightDelta(character);
        if (character.Condition.Spirit < character.Condition.MaxSpirit / 2) sitWeight += 15;
        if (character.Condition.Fatigue > 0) sitWeight += 10;
        sitWeight = System.Math.Clamp(sitWeight, 5, 80);

        var wanderWeight = rooms.Count > 0 ? Traits.WanderChance(character) : 0;
        var roll = Rng.Next(100);

        // 优先坐当前房里的座位，找不到才考虑别的房间，避免为了坐而空跑。
        if (seats.Count > 0 && roll < sitWeight)
        {
            var here = seats.FindIndex(s => s.RoomId == worker.RoomId);
            var seat = here >= 0 ? seats[here] : seats[Rng.Next(seats.Count)];
            worker.Loiter = LoiterKind.Sitting;
            worker.FacilityId = seat.Id;
            GotoRoom(worker, territory, seat.RoomId, r => Enterable(r, character, ctx));
            worker.Phase = worker.Path.Count > 0 ? WorkPhase.Moving : WorkPhase.Idle;
            if (worker.Path.Count == 0)
                Sit(territory, worker, used);
            worker.StateMachine.TransitionTo(new StateMachine.States.LoiteringState(), new StateMachine.WorkerContext
            {
                Territory = territory,
                Character = character,
                Worker = worker,
                StepContext = ctx,
                UsedFacilities = new HashSet<int>(used.Keys),
            });
            return;
        }

        if (rooms.Count > 0 && roll < sitWeight + wanderWeight)
        {
            worker.Loiter = LoiterKind.Wandering;
            var target = rooms[Rng.Next(rooms.Count)];
            GotoRoom(worker, territory, target, r => Enterable(r, character, ctx));
            worker.Phase = worker.Path.Count > 0 ? WorkPhase.Moving : WorkPhase.Idle;
            worker.StateMachine.TransitionTo(new StateMachine.States.LoiteringState(), new StateMachine.WorkerContext
            {
                Territory = territory,
                Character = character,
                Worker = worker,
                StepContext = ctx,
                UsedFacilities = new HashSet<int>(used.Keys),
            });
            return;
        }

        // 坐也坐过、走也走过了，或者两样都不成：在房里待着（女仆会顺手做点零活）。
        worker.Loiter = LoiterKind.Chores;
        worker.Phase = WorkPhase.Idle;
        var facilitiesInRoom = territory.Facilities.FindAll(f => f.Built && f.RoomId == worker.RoomId);
        if (facilitiesInRoom.Count > 0)
        {
            var targetFacility = facilitiesInRoom[Rng.Next(facilitiesInRoom.Count)];
            worker.FacilityId = targetFacility.Id;
        }
        worker.StateMachine.TransitionTo(new StateMachine.States.LoiteringState(), new StateMachine.WorkerContext
        {
            Territory = territory,
            Character = character,
            Worker = worker,
            StepContext = ctx,
            UsedFacilities = new HashSet<int>(used.Keys),
        });
    }

    /// <summary>能走到的所有有空位的可坐设施（吃饭用的座位或休息用的座位均可）。</summary>
    private static List<Facility> ReachableSeats(Territory territory, int fromRoom,
        CharacterState character, StepContext ctx, Dictionary<int, int> used)
    {
        var seats = new List<Facility>();
        foreach (var roomId in ReachableRoomsOrdered(territory, fromRoom, character, ctx))
        {
            foreach (var facility in territory.Facilities)
            {
                if (!facility.Built || facility.RoomId != roomId)
                    continue;
                if (!facility.Supports(ActionKind.Meal) && !facility.Supports(ActionKind.Rest))
                    continue;
                if (used.GetValueOrDefault(facility.Id) >= facility.Capacity)
                    continue;
                if (!seats.Contains(facility))
                    seats.Add(facility);
            }
        }
        return seats;
    }

    /// <summary>
    /// 闲时活动一步：先在路上走，到地方了再歇/忙活，时长耗尽后回决策重挑。
    /// 每一步都刷新“此刻在做什么”，因此日志里的这一行始终是当前行为。
    /// </summary>
    private void ProcessLoiter(CharacterState character, Worker worker, Territory territory, StepContext ctx)
    {
        // 还没到地方就先赶路（日志写“正往哪儿去”）。到不了（路被堵）则就地忙活。
        if (worker.Path.Count > 0)
        {
            MoveAlong(worker);
            if (worker.Path.Count > 0)
                return;
        }

        if (worker.LoiterTicks > 0)
            worker.LoiterTicks--;

        if (worker.LoiterTicks <= 0)
        {
            worker.Loiter = LoiterKind.None;
            EndRoutine(worker);
        }
    }

    private void ProcessRoutine(CharacterState character, Worker worker, Territory territory, Roster roster, StepContext ctx)
    {
        if (worker.StateMachine.CurrentState != null)
        {
            var workerCtx = new StateMachine.WorkerContext
            {
                Territory = territory,
                Character = character,
                Worker = worker,
                StepContext = ctx,
                UsedFacilities = new HashSet<int>(Seats().Keys),
                Rng = Rng,
            };
            var finished = worker.StateMachine.Step(workerCtx);
            if (finished)
            {
                EndRoutine(worker);
                return;
            }
        }

        switch (worker.Goal)
        {
            case ActionKind.Meal:
                if (MoveAlong(worker))
                    return;
                Eat(character, worker, territory, ctx);
                EndRoutine(worker);
                break;
            case ActionKind.Sleep:
                if (MoveAlong(worker))
                    return;
                character.Condition.SleepTick();
                break;
            case ActionKind.Rest:
                character.Condition.RestTick();
                character.Condition.Recover(0, Traits.RestSpiritBonus(character), false);
                if (character.Condition.Fatigue == 0)
                    EndRoutine(worker);
                break;
            case ActionKind.Watch:
                if (MoveAlong(worker))
                    return;
                // 到了地方先记“今天玩过了”，再待够时长才回决策（StartPlay 已置时长）。
                character.Affect.LastPlayDay = ctx.Day;
                if (worker.PlayTicks > 0)
                    worker.PlayTicks--;
                if (worker.PlayTicks <= 0)
                    EndRoutine(worker);
                break;
            case ActionKind.SeekChat:
                ProcessSeek(character, worker, territory, roster, ctx);
                break;
            case ActionKind.Loiter:
                ProcessLoiter(character, worker, territory, ctx);
                break;
            case ActionKind.Haul:
                ProcessHaul(character, worker, territory, ctx);
                break;
        }
    }

    private void ProcessSeek(CharacterState character, Worker worker, Territory territory, Roster roster, StepContext ctx)
    {
        if (worker.WaitTicks <= 0)
        {
            character.Affect.AddMood(-5);
            character.Affect.ChatDesire = 50;
            EndRoutine(worker);
            return;
        }
        worker.WaitTicks--;
        if (worker.WantsChat)
        {
            if (worker.RoomId == ctx.PlayerRoomId)
            {
                ShareSeat(worker, territory, ctx);
                return;
            }
            worker.WantsChat = false;
        }
        var target = territory.Rooms.Find(r => r.Id == ctx.PlayerRoomId);
        if (!Enterable(target, character, ctx) || !GotoRoom(worker, territory, target!.Id, r => Enterable(r, character, ctx)))
        {
            worker.SeekWaiting = true;
            return;
        }
        MoveAlong(worker);
        if (worker.RoomId == ctx.PlayerRoomId && worker.Path.Count == 0)
        {
            worker.WantsChat = true;
            worker.Phase = WorkPhase.Idle;
            if (worker.ChatRoom != worker.RoomId)
            {
                worker.ChatRoom = worker.RoomId;
                // 她走到了你面前：这是角色主动找玩家对话，台词弹对话框。
                ctx.SeekDialogue(character, $"{character.Name}似乎想对你说什么。");
            }
            ShareSeat(worker, territory, ctx);
        }
    }

    private static void ShareSeat(Worker worker, Territory territory, StepContext ctx)
    {
        if (worker.FacilityId >= 0 || ctx.PlayerFixtureId < 0)
            return;
        var fixture = territory.Facilities.Find(f => f.Id == ctx.PlayerFixtureId);
        if (fixture != null && fixture.Capacity > 1)
            worker.FacilityId = fixture.Id;
    }

    private static void EndRoutine(Worker worker)
    {
        worker.StateMachine.TransitionTo(null, new StateMachine.WorkerContext { Worker = worker });
        worker.Goal = ActionKind.None;
        worker.Task = ActionKind.None;
        worker.Phase = WorkPhase.Idle;
        worker.Path.Clear();
        worker.FacilityId = -1;
        worker.WaitTicks = 0;
        worker.WantsChat = false;
        worker.SeekWaiting = false;
        worker.ChatRoom = -1;
        worker.Loiter = LoiterKind.None;
        worker.LoiterTicks = 0;
        worker.LoiterAnnounced = false;
        worker.PlayTicks = 0;
        worker.HaulItemId = "";
        worker.HaulCount = 0;
        worker.HaulTargetId = -1;
    }

    /// <summary>起床结算：和人挤一间扣心情；没床再扣。独睡不回。</summary>
    private void WakeUp(CharacterState character, Worker worker)
    {
        var roommates = 0;
        foreach (var other in _workers)
        {
            if (other.CharacterId != worker.CharacterId && other.RoomId == worker.RoomId)
                roommates++;
        }
        if (roommates > 0)
            character.Affect.AddMood(-15);
        if (worker.FacilityId < 0)
            character.Affect.AddMood(-5);
    }

    private static int SeekThreshold(CharacterState character) =>
        character.Condition.Bond == Bond.Lover ? 70 : ChatThreshold;

    private static int SeekBudgetFor(CharacterState character) => System.Math.Max(12,
        (character.Condition.Bond switch
        {
            Bond.Close => 48,
            Bond.Lover => 72,
            _ => SeekBudget,
        }) + Traits.SeekBudgetDelta(character));

    private void TrackTogether(CharacterState character, Worker worker, Roster roster, StepContext ctx)
    {
        foreach (var other in roster.Members)
        {
            if (other.Id == character.Id)
                continue;
            var room = other.Id == ctx.MasterId
                ? ctx.PlayerRoomId
                : _workers.Find(w => w.CharacterId == other.Id)?.RoomId ?? -1;
            if (room < 0 || room != worker.RoomId)
            {
                worker.Together.Remove(other.Id);
                continue;
            }
            var ticks = worker.Together.GetValueOrDefault(other.Id) + 1;
            if (ticks >= 48)
            {
                if (character.Relations.Has(other.Id, RelationFlag.Rival))
                    character.Affect.AddMood(-12);
                ticks = 0;
            }
            worker.Together[other.Id] = ticks;
        }
    }

    /// <summary>
    /// 开始睡觉。睡觉必须到床上：找不到带 Sleep 行动的设施就不睡，
    /// 返回 false 让调用方改做别的事（禁止在房间里凭空睡）。
    /// </summary>
    private static bool StartSleep(CharacterState character, Worker worker, Territory territory, Roster roster, StepContext ctx, Dictionary<int, int> used)
    {
        var bed = NearestRoomWith(territory, worker.RoomId, character, ctx,
            r => HasAction(territory, r.Id, ActionKind.Sleep));
        if (bed < 0)
            return false;

        var mattress = FindFree(territory, bed, ActionKind.Sleep, used);
        if (mattress == null)
            return false;

        Release(worker, used);
        character.Affect.AddMood((Affect.Neutral - character.Affect.Mood) * 20 / 100);
        worker.Goal = ActionKind.Sleep;
        worker.Task = ActionKind.None;
        worker.Progress = 0;
        worker.Path.Clear();
        worker.FacilityId = mattress.Id;
        GotoRoom(worker, territory, bed, r => Enterable(r, character, ctx));
        worker.Phase = worker.Path.Count > 0 ? WorkPhase.Moving : WorkPhase.Idle;
        if (worker.Path.Count == 0)
            Sit(territory, worker, used);
        worker.StateMachine.TransitionTo(new StateMachine.States.SleepingState(), new StateMachine.WorkerContext
        {
            Territory = territory,
            Character = character,
            Worker = worker,
            StepContext = ctx,
            UsedFacilities = new HashSet<int>(used.Keys),
        });
        return true;
    }

    private static void StartMeal(CharacterState character, Worker worker, Territory territory, Roster roster, StepContext ctx, int window, Dictionary<int, int> used)
    {
        Release(worker, used);
        // 记下这一餐已尝试过，免得没吃的时每格都重新算一遍。
        character.Affect.LastMealDay = ctx.Day;
        character.Affect.LastMealWindow = window;

        // 吃到的东西必须在够得着的地方：自己背包里，或某个设施里。
        // 因此要先找到“有食物可取的地方”，再去那儿坐下吃。
        var seat = PickMealSeat(territory, worker.RoomId, character, ctx, used);
        if (seat == null)
        {
            // 背包里有干粮就在原地找座吃；连背包都空、也没设施存货，本餐作废。
            worker.Goal = ActionKind.None;
            return;
        }
        worker.Goal = ActionKind.Meal;
        worker.Task = ActionKind.None;
        worker.Progress = 0;
        worker.FacilityId = seat.Id;
        if (!GotoRoom(worker, territory, seat.RoomId, r => Enterable(r, character, ctx)))
        {
            character.Affect.AddMood(-8);
            worker.Goal = ActionKind.None;
            return;
        }
        worker.Phase = worker.Path.Count > 0 ? WorkPhase.Moving : WorkPhase.Idle;
        if (worker.Path.Count == 0)
            Sit(territory, worker, used);
        worker.StateMachine.TransitionTo(new StateMachine.States.DiningState(), new StateMachine.WorkerContext
        {
            Territory = territory,
            Character = character,
            Worker = worker,
            StepContext = ctx,
            UsedFacilities = new HashSet<int>(used.Keys),
        });
    }

    /// <summary>
    /// 挑一处能坐下吃饭的座位：优先与桌子同房且有空位的，其次任意可坐设施。
    /// 座位所在房间必须有食物可取（背包里有，或该房设施里存着）——
    /// 没有食物的房间不算，免得白跑一趟坐着却没得吃。
    /// </summary>
    private static Facility? PickMealSeat(Territory territory, int fromRoom,
        CharacterState character, StepContext ctx, Dictionary<int, int> used)
    {
        Facility? fallback = null;
        foreach (var roomId in ReachableRoomsOrdered(territory, fromRoom, character, ctx))
        {
            if (territory.FindFoodIn(character, roomId) == null)
                continue;
            var seat = FindFree(territory, roomId, ActionKind.Meal, used);
            if (seat == null)
                continue;
            if (HasTable(territory, roomId))
                return seat;
            fallback ??= seat;
        }
        return fallback;
    }

    /// <summary>这间房有没有桌子。桌子本身无行动，只作为吃饭的体面加成。</summary>
    private static bool HasTable(Territory territory, int roomId) =>
        territory.Facilities.Exists(f => f.Built && f.RoomId == roomId && f.IsTable);

    /// <summary>某房内支持该行动、还有空位的第一件设施。</summary>
    private static Facility? FindFree(Territory territory, int roomId, ActionKind action, Dictionary<int, int> used) =>
        territory.Facilities.Find(f => f.Built && f.RoomId == roomId
            && f.Supports(action)
            && used.GetValueOrDefault(f.Id) < f.Capacity);

    /// <summary>按设施用途找一处空位。用于消遣这类"看设施标签"的去处选择。</summary>
    private static Facility? FindFreeByUsage(Territory territory, int roomId, FacilityUsage usage, Dictionary<int, int> used) =>
        territory.Facilities.Find(f => f.Built && f.RoomId == roomId && f.Usage == usage
            && used.GetValueOrDefault(f.Id) < f.Capacity);

    private bool StartPlay(CharacterState character, Worker worker, Territory territory, Roster roster, StepContext ctx, Dictionary<int, int> used)
    {
        Release(worker, used);
        Facility? playFacility = null;
        foreach (var roomId in ReachableRoomsOrdered(territory, worker.RoomId, character, ctx))
        {
            var freeLeisure = FindFreeByUsage(territory, roomId, FacilityUsage.Leisure, used);
            if (freeLeisure != null)
            {
                playFacility = freeLeisure;
                break;
            }
        }
        if (playFacility == null)
        {
            foreach (var roomId in ReachableRoomsOrdered(territory, worker.RoomId, character, ctx))
            {
                var freeSeat = FindFree(territory, roomId, ActionKind.Rest, used);
                if (freeSeat != null)
                {
                    playFacility = freeSeat;
                    break;
                }
            }
        }
        if (playFacility == null)
        {
            worker.Goal = ActionKind.None;
            return false;
        }

        worker.Goal = ActionKind.Loiter;
        worker.Task = ActionKind.None;
        worker.Progress = 0;
        worker.FacilityId = playFacility.Id;
        worker.PlayTicks = Traits.LoiterTicksFor(character);
        GotoRoom(worker, territory, playFacility.RoomId, r => Enterable(r, character, ctx));
        worker.Phase = worker.Path.Count > 0 ? WorkPhase.Moving : WorkPhase.Idle;
        if (worker.Path.Count == 0)
            Sit(territory, worker, used);
        worker.StateMachine.TransitionTo(new StateMachine.States.PlayState(), new StateMachine.WorkerContext
        {
            Territory = territory,
            Character = character,
            Worker = worker,
            StepContext = ctx,
            UsedFacilities = new HashSet<int>(used.Keys),
        });
        return true;
    }

    private static bool HasAction(Territory territory, int roomId, ActionKind action) =>
        territory.Facilities.Exists(f => f.Built && f.RoomId == roomId && f.Supports(action));

    private static void StartSeek(CharacterState character, Worker worker, Dictionary<int, int> used)
    {
        Release(worker, used);
        worker.Goal = ActionKind.SeekChat;
        worker.Task = ActionKind.None;
        worker.Progress = 0;
        worker.Path.Clear();
        worker.WaitTicks = SeekBudgetFor(character);
        worker.WantsChat = false;
        worker.SeekWaiting = false;
        worker.ChatRoom = -1;
        worker.Phase = WorkPhase.Idle;
        worker.StateMachine.TransitionTo(new StateMachine.States.SeekingChatState(), new StateMachine.WorkerContext
        {
            Character = character,
            Worker = worker,
            UsedFacilities = new HashSet<int>(used.Keys),
        });
    }

    private static void Eat(CharacterState character, Worker worker, Territory territory, StepContext ctx)
    {
        // 从够得着的地方取一份食物（背包优先，其次所在房间设施的存货）。
        // 取不到就不算吃过了，这一餐作废——不允许凭空吃。
        var food = territory.ConsumeFood(character, worker.RoomId);
        if (food == null)
            return;

        character.Condition.Recover(
            Traits.MealStamina(character, 80),
            Traits.MealSpirit(character, 60), false);
        character.Affect.AddMood(Traits.ScaledMood(character,
            territory.FoodTierOf(food) switch
            {
                FoodTier.Delicate => 2,
                FoodTier.Feast => 4,
                FoodTier.Exquisite => 8,
                _ => 0,
            }) * Traits.MealMoodPercent(character) / 100);
        // 坐在没有桌子的房间里吃，等于将就一顿，扣心情。
        if (!HasTable(territory, worker.RoomId))
            character.Affect.AddMood(-3);
    }

    private static bool MoveAlong(Worker worker)
    {
        if (worker.Path.Count == 0)
        {
            if (worker.FacilityId >= 0)
                worker.Phase = WorkPhase.Working;
            return false;
        }
        worker.Phase = WorkPhase.Moving;
        worker.RoomId = worker.Path.Dequeue();
        if (worker.Path.Count == 0 && worker.FacilityId >= 0)
            worker.Phase = WorkPhase.Working;
        return worker.Path.Count > 0;
    }

    private static bool GotoRoom(Worker worker, Territory territory, int roomId, Func<Room, bool>? passable = null)
    {
        worker.Path.Clear();
        if (worker.RoomId == roomId)
            return true;
        foreach (var step in Route(territory, worker.RoomId, roomId, passable))
            worker.Path.Enqueue(step);
        if (worker.Path.Count == 0)
            return false;
        worker.Phase = WorkPhase.Moving;
        return true;
    }

    private void DriftMood(CharacterState character, Worker worker, StepContext ctx)
    {
        if (ctx.NowTotal % Affect.DriftPeriodMinutes != 0)
            return;
        var mood = character.Affect.Mood;
        if (mood > Affect.Neutral)
            character.Affect.AddMood(-1);
        else if (mood < Affect.Neutral)
            character.Affect.AddMood(1);
        if (character.Condition.Stamina * 10 < character.Condition.MaxStamina * 3)
            character.Affect.AddMood(-1);
    }

    private static void GrowDesire(CharacterState character, StepContext ctx)
    {
        if (ctx.MasterId < 0 || character.Id == ctx.MasterId)
            return;
        var rate = 1 + character.Condition.Bond switch
        {
            Bond.Fond => 1,
            Bond.Close => 2,
            Bond.Lover => 3,
            _ => 0,
        };
        rate += Traits.ChatDesireBonus(character);
        if (character.Affect.LastTalkAt >= 0 && ctx.NowTotal - character.Affect.LastTalkAt > 360)
            rate++;
        if (rate > 0)
            character.Affect.AddDesire(rate);
    }

    private static bool IsNight(int minutes, CharacterState character)
    {
        var hour = minutes / 60;
        var wake = Traits.WakeHourFor(character);
        var bed = Traits.BedHourFor(character);
        return hour >= bed || hour < wake;
    }

    private static int MealWindow(int minutes)
    {
        if (minutes >= 360 && minutes < 540)
            return 0;
        if (minutes >= 660 && minutes < 840)
            return 1;
        if (minutes >= 1020 && minutes < 1200)
            return 2;
        return -1;
    }

    private static bool Enterable(Room? room, CharacterState character, StepContext ctx) =>
        room != null && room.Open && (room.Permission == RoomPermission.Public
            || (room.Permission == RoomPermission.Faction && character.FactionId == ctx.MasterFactionId));

    private static int NearestRoomWith(Territory territory, int fromRoom, CharacterState character, StepContext ctx, Func<Room, bool> match)
    {
        var seen = new HashSet<int> { fromRoom };
        var queue = new Queue<int>();
        queue.Enqueue(fromRoom);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            var room = territory.Rooms.Find(r => r.Id == id);
            if (room == null || !room.Open)
                continue;
            if (id != fromRoom && !Enterable(room, character, ctx))
                continue;
            if (match(room))
                return id;
            foreach (var next in room.Links)
            {
                if (seen.Add(next))
                    queue.Enqueue(next);
            }
        }
        return -1;
    }

    /// <summary>
    /// 可去的房间，按从近到远的 BFS 顺序，不做随机。用于“找最近的合适去处”
    /// （找饭座这类），随机化会让每次选点乱跳。
    /// </summary>
    private static List<int> ReachableRoomsOrdered(Territory territory, int fromRoom, CharacterState character, StepContext ctx)
    {
        var rooms = new List<int>();
        var seen = new HashSet<int> { fromRoom };
        var queue = new Queue<int>();
        queue.Enqueue(fromRoom);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            var room = territory.Rooms.Find(r => r.Id == id);
            if (room == null || !room.Open)
                continue;
            if (id != fromRoom && !Enterable(room, character, ctx))
                continue;
            rooms.Add(id);
            foreach (var next in room.Links)
            {
                if (seen.Add(next))
                    queue.Enqueue(next);
            }
        }
        return rooms;
    }

    private List<int> ReachableRooms(Territory territory, int fromRoom, CharacterState character, StepContext ctx)
    {
        var rooms = new List<int>();
        var seen = new HashSet<int> { fromRoom };
        var queue = new Queue<int>();
        queue.Enqueue(fromRoom);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            var room = territory.Rooms.Find(r => r.Id == id);
            if (room == null || !room.Open)
                continue;
            if (id != fromRoom && !Enterable(room, character, ctx))
                continue;
            rooms.Add(id);
            foreach (var next in room.Links)
            {
                if (seen.Add(next))
                    queue.Enqueue(next);
            }
        }
        var index = rooms.Count > 0 ? Rng.Next(rooms.Count) : -1;
        if (index > 0)
        {
            (rooms[0], rooms[index]) = (rooms[index], rooms[0]);
        }
        return rooms;
    }

    private int workerRoom(int characterId) =>
        _workers.Find(w => w.CharacterId == characterId)?.RoomId ?? -1;
}
