using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Character;
using Rimisekai.Clock;
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

    /// <summary>当前天气。露天劳作、赶路与衣着干湿都看它。</summary>
    public Weather Weather { get; set; } = Weather.Clear;

    /// <summary>当前季节。耕地播种与作物生长看它。</summary>
    public Season Season { get; set; } = Season.Spring;

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
    /// 是否在跟随玩家。邀请同意后置起，再邀一次解除；
    /// 跟随期间日程与自主节律全部让位，人始终跟着玩家走。
    /// 随 Worker 存续，不进存档——位置本来就是会话状态。
    /// </summary>
    public bool FollowsPlayer { get; set; }

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

    /// <summary>这一趟取料的源设施 Id（-1 表示货已在身上，直接走送货阶段）。</summary>
    public int HaulSourceId { get; set; } = -1;

    /// <summary>搬运阶段：前往取料（Fetching）或送往目标（Delivering）。</summary>
    public HaulPhase HaulPhase { get; set; } = HaulPhase.Delivering;

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
        // 跟随没有专门的口上活动，按闲时算——她此刻就是闲着陪在人身边。
        ActionKind.Follow => VoiceActivity.Idle,
        _ => worker.Phase == WorkPhase.Moving ? VoiceActivity.Moving : ActionActivity(worker.Task),
    };

    private static VoiceActivity ActionActivity(ActionKind action) => action switch
    {
        ActionKind.Cook => VoiceActivity.Cooking,
        ActionKind.Mine or ActionKind.Fell => VoiceActivity.Mining,
        ActionKind.Till or ActionKind.Tend => VoiceActivity.Farming,
        ActionKind.Sew => VoiceActivity.Crafting,
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
            var assignment = territory.ScheduleOf(character.Id).Slots[slot];
            if (character.IsMaster)
            {
                // 主人出了领地（人在大地图上）：领地里没有他的身子，不跑任何自动行为。
                if (ctx != null && ctx.PlayerRoomId < 0)
                {
                    var awayWorker = _workers.Find(w => w.CharacterId == character.Id);
                    if (awayWorker != null && awayWorker.Goal != ActionKind.None)
                    {
                        EndRoutine(awayWorker);
                        Release(awayWorker, used);
                    }
                    continue;
                }
                // 玩家在非工作时段（空闲或娱乐）不走自动工作，保持手动自由控制
                if (assignment.Mode != SlotMode.Work)
                {
                    var existingMasterWorker = _workers.Find(w => w.CharacterId == character.Id);
                    if (existingMasterWorker != null && ActionKindMap.IsWork(existingMasterWorker.Goal))
                    {
                        EndRoutine(existingMasterWorker);
                        Release(existingMasterWorker, used);
                    }
                    continue;
                }
            }

            var currentRoom = workerRoom(character.Id);
            if (currentRoom < 0 && character.IsMaster && ctx != null)
                currentRoom = ctx.PlayerRoomId;
            var worker = Track(character.Id, currentRoom);
            if (ctx != null)
            {
                // 衣着干湿按这一格开始时所在的房间算——过去 5 分钟人一直待在这里。
                WorldEffects.SettleWetness(character, territory, worker.RoomId, ctx.Weather);
                UpdateRoutine(character, worker, territory, roster, assignment, ctx, used);
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
                // 无上下文（旧路径 / 测试）：直接按这一段的安排挑活，累了或不肯干就歇着。
                var task = PickWork(character, territory, assignment);
                if (task == ActionKind.None || character.Condition.Tired)
                {
                    Release(worker, used);
                    worker.Task = ActionKind.None;
                    worker.Phase = WorkPhase.Idle;
                    continue;
                }
                if (worker.Task != task || worker.FacilityId != assignment.FacilityId)
                    Retarget(territory, worker, task, assignment.FacilityId, used);
            }
            // 干活时 Goal 就是那件具体的活（原 Assigned 语义）。
            worker.Goal = worker.Task;
            if (worker.Phase == WorkPhase.Moving && worker.Path.Count > 0)
            {
                worker.RoomId = worker.Path.Dequeue();
                if (ctx != null)
                    WorldEffects.SpendMoveStamina(character, territory, worker.RoomId, ctx.Weather);
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
                // 恶劣天气露天作业：耗时加倍，效率减半（产量不变）。
                if (ctx != null && WorldEffects.IsSevere(ctx.Weather)
                    && WorldEffects.OutdoorRoom(territory, facility.RoomId))
                    tick = System.Math.Max(1, tick / 2);
                worker.Progress += tick;
                if (worker.Progress >= Territory.FinishAt)
                {
                    var log = Finish(territory, character, worker, facility, yieldFor, ctx?.Season ?? Season.Spring);
                    if (log != null)
                        logs.Add(log);
                    else if (!ActionKindMap.IsExtractive(worker.Task) && worker.Task != ActionKind.Perform && worker.Task != ActionKind.Trade)
                    {
                        // 制作中途原料耗尽（未能产出）：自然退出工作，回退决策
                        EndRoutine(worker);
                        Release(worker, used);
                    }
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

        // 日常行为的叙述归状态自己（每个状态知道自己此刻在做什么）。
        // 没有状态就是没在做事——不替角色编造一句“发呆”。
        return worker.StateMachine.CurrentState?.Describe(workerCtx) ?? "";
    }

    /// <summary>
    /// 寻找该工作台当前可做的配方：
    /// 1. 优先找工作台台面与身上原料已满足的配方（可直接开工）；
    /// 2. 其次找全领地（含仓储）原料充足、可执行备料的配方。
    /// 找不到说明全领地原料匮乏，返回 null。
    /// </summary>
    public static Recipe? FindAvailableRecipe(Territory territory, Facility bench, CharacterState character, ActionKind task)
    {
        var candidates = territory.Recipes.Where(r => r.Station == task);
        var target = territory.GetTargetCraftItem(task);
        if (!string.IsNullOrEmpty(target))
            candidates = candidates.Where(r => r.ItemId == target);

        Recipe? fetchable = null;
        foreach (var r in candidates)
        {
            if (territory.CanPayAt(bench, character, r.Costs))
                return r;

            if (fetchable == null && CanFetchAllCosts(territory, bench, character, r.Costs))
                fetchable = r;
        }
        return fetchable;
    }

    /// <summary>全据点是否有足够材料为该配方完成备料（各仓储 + 角色身上 + 台面存量满足需求）。</summary>
    private static bool CanFetchAllCosts(Territory territory, Facility bench, CharacterState character, IReadOnlyList<RecipeCost> costs)
    {
        if (costs.Count == 0)
            return true;
        foreach (var cost in costs)
        {
            var onBench = bench.Contents.Get(cost.ItemId);
            var inBag = character.Bag.Get(cost.ItemId);
            var inStorages = 0;
            foreach (var s in territory.Storages)
                inStorages += s.Contents.Get(cost.ItemId);

            if (onBench + inBag + inStorages < cost.Count)
                return false;
        }
        return true;
    }

    /// <summary>
    /// 按这一段的安排挑活：工作时段到点名的那件设施干活。没点名就不出活。
    /// </summary>
    private static ActionKind PickWork(CharacterState character, Territory territory,
        SlotAssignment assignment, Season season = Season.Spring)
    {
        if (assignment.Mode != SlotMode.Work)
            return ActionKind.None;

        if (assignment.FacilityId >= 0)
        {
            var facility = territory.Facilities.Find(f => f.Id == assignment.FacilityId && f.Built);
            if (facility == null)
                return ActionKind.None;

            foreach (var task in ActionKindMap.WorkOrdered)
            {
                if (!facility.Supports(task))
                    continue;
                if (!character.Affect.AcceptsWork())
                    return ActionKind.None;
                if (task == ActionKind.Till
                    && territory.PlotState(facility, season, character)
                        is Territory.FarmState.Growing
                        or Territory.FarmState.OutOfSeason
                        or Territory.FarmState.NoSeed)
                    continue;

                // 制作类（锻造、烹饪、木工、缝纫、炼金）：全领地缺料则无法工作，跳过
                if (!ActionKindMap.IsExtractive(task) && task != ActionKind.Perform && task != ActionKind.Trade)
                {
                    if (FindAvailableRecipe(territory, facility, character, task) == null)
                        continue;
                }

                return task;
            }
            return ActionKind.None;
        }

        return ActionKind.None;
    }


    /// <summary>这一段点名的那件设施支不支持这件工作行动。厨师备餐的门槛用它。</summary>
    private static bool AssignedTo(Territory territory, SlotAssignment assignment, ActionKind task)
    {
        if (assignment.Mode != SlotMode.Work || assignment.FacilityId < 0)
            return false;
        var facility = territory.Facilities.Find(f => f.Id == assignment.FacilityId && f.Built);
        return facility != null && facility.Supports(task);
    }

    /// <summary>
    /// 换活：到排班点名的那件设施去（排班是「某时段到某件设施去」，不是「找任意一件同类设施」）。
    /// 那件设施坐满了就原地待命，下一格再试。
    /// </summary>
    private void Retarget(Territory territory, Worker worker, ActionKind task, int facilityId, Dictionary<int, int> used)
    {
        Release(worker, used);
        worker.Task = task;
        worker.Progress = 0;
        worker.Path.Clear();
        var facility = territory.Facilities.Find(f => f.Id == facilityId && f.Built && f.Supports(task)
            && used.GetValueOrDefault(f.Id) < f.Capacity);
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

    private static WorkLog? Finish(Territory territory, CharacterState character, Worker worker,
        Facility facility, Func<ActionKind, int>? yieldFor, Season season)
    {
        if (ActionKindMap.IsExtractive(worker.Task))
        {
            // 耕地按播种/收获结算，不是无中生有的抽取；生长中这一格空过（无产出也不清地）。
            var farm = territory.FarmWork(character, facility, worker.Task, season, yieldFor, out var handled);
            if (handled)
            {
                character.Condition.Spend(0, GetSpiritCost(worker.Task));
                // 耕完这一下若地里已无事可做（刚播种/没种/非季），放下锄头回决策层重挑。
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
            var amount = System.Math.Clamp(System.Math.Max(1, character.Life(ActionKindMap.SkillOf(worker.Task)!.Value)) / 40, 1, 4);
            if (yieldFor != null)
                amount = System.Math.Max(1, amount * yieldFor(worker.Task) / 100);
            if (facility.YieldItemId.Length > 0)
                territory.Produce(character, facility.YieldItemId, amount);
            character.GainLifeExp(ActionKindMap.SkillOf(worker.Task)!.Value, Territory.GatherExp);
            character.Condition.Spend(0, GetSpiritCost(worker.Task));
            return Log(worker, facility.YieldItemId, amount, ActionKindMap.SkillOf(worker.Task)!.Value);
        }
        // 工作台只用“这个人背包 + 这台子自己的存货”付料：
        // 材料得有人搬过来，不能隔空从别的货架取。
        var targetCraft = territory.GetTargetCraftItem(worker.Task);
        var recipe = (!string.IsNullOrEmpty(targetCraft))
            ? territory.Recipes.Find(r => r.Station == worker.Task && r.ItemId == targetCraft && territory.CanPayAt(facility, character, r.Costs))
            : territory.Recipes.Find(r => r.Station == worker.Task && territory.CanPayAt(facility, character, r.Costs));
        if (recipe == null || !territory.PayAt(facility, character, recipe.Costs))
            return null;
        territory.Produce(character, recipe.ItemId, recipe.OutputCount);
        character.GainLifeExp(recipe.Skill, Territory.CraftExp);
        character.Condition.Spend(0, GetSpiritCost(worker.Task));
        worker.Phase = WorkPhase.Idle;
        worker.Goal = ActionKind.None;
        worker.Progress = 0;
        return Log(worker, recipe.ItemId, recipe.OutputCount, recipe.Skill);
    }

    private static int GetSpiritCost(ActionKind task)
    {
        var def = Defs.DefDatabase<Defs.ActionDef>.Get(task.ToString());
        if (def != null && def.SpiritCost > 0)
            return def.SpiritCost;
        return ActionKindMap.TypeOf(task) is WorkType.Excavate or WorkType.Smithing ? 25 : 15;
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

    private static List<int> Route(Territory territory, int fromRoom, int toRoom, Func<Room, bool>? passable = null) =>
        territory.Route(fromRoom, toRoom, passable);

    private void UpdateRoutine(CharacterState character, Worker worker, Territory territory, Roster roster, SlotAssignment assignment, StepContext ctx, Dictionary<int, int> used)
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

        // 检查三餐是否超过饭点 2 小时未吃：逾期扣除心情
        CheckMissedMeals(character, ctx, minutes);

        // 跟随者的一切日程让位：先结算睡眠，其余时候跟着玩家走。
        if (worker.FollowsPlayer)
        {
            FollowTick(character, worker, territory, roster, ctx, used, night);
            return;
        }

        if (worker.Goal == ActionKind.Sleep)
        {
            if (!night && !character.Condition.Tired)
            {
                WakeUp(character, worker, roster, ctx);
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

        // 采集者的归集环节：包里攒了产出，先送回仓储再继续手头的活——
        // 只认采集劳作本身（备料/搬运途中不打断），排班与闲时节律都会经过这里。
        if (worker.Goal is ActionKind.Fell or ActionKind.Mine or ActionKind.Till or ActionKind.Tend
            && character.Bag.Items.Count > 0
            && StartHaul(character, worker, territory, ctx, used))
            return;

        // 正在进行的活动不重做决策，由 ProcessRoutine 自己推进；
        // 但三餐与找人这类生理/社交需求可以打断闲时活动与搬运。
        // 工作行动不在此列——它由主循环内联结算（见 Step）。
        var interruptible = worker.Goal is ActionKind.Loiter or ActionKind.Haul;
        if (worker.Goal != ActionKind.None && !interruptible)
            return;

        // 食客（包括厨师本人）在开饭前半小时至饭后两小时内赶往存放有熟食的餐桌；无熟食不盲动；饭后两小时内有熟食仍补餐
        var diningWindow = DiningWindow(minutes);
        if (diningWindow >= 0 && (character.Affect.LastMealDay != ctx.Day || character.Affect.LastMealWindow != diningWindow))
        {
            if (HasAvailableMeal(character, territory, worker.RoomId))
            {
                if (StartMeal(character, worker, territory, roster, ctx, diningWindow, used))
                    return;
            }
        }
        // 主人出了领地（人在大地图上，PlayerRoomId < 0）：家里的人找不到他，不起意去搭话。
        if (ctx.PlayerRoomId >= 0 && character.Affect.ChatDesire >= SeekThreshold(character))
        {
            StartSeek(character, worker, used);
            return;
        }

        // 刚做好的热饭热菜优先送到餐桌储存，供全领地享用
        if (HasDeliverableMeal(character, territory))
        {
            if (StartHaul(character, worker, territory, ctx, used))
                return;
        }

        // 厨师在开饭前两小时进入做饭状态（开始去库房寻找食材、回厨房做饭并送到餐桌）。
        // 门槛是「这一段被排到做饭设施上」，不再有优先级表；领地必须有食材才触发备餐。
        var prepWindow = CookPrepWindow(minutes);
        if (prepWindow >= 0 && AssignedTo(territory, assignment, ActionKind.Cook) && !HasTableWithMeal(territory))
        {
            var stove = territory.Facilities.Find(f => f.Id == assignment.FacilityId && f.Built);
            if (stove != null && FindAvailableRecipe(territory, stove, character, ActionKind.Cook) != null)
            {
                if (StartFetchForBench(character, worker, territory, ctx, ActionKind.Cook, stove))
                    return;
                if (worker.Task != ActionKind.Cook || worker.FacilityId != stove.Id)
                    Retarget(territory, worker, ActionKind.Cook, stove.Id, used);
                worker.Goal = ActionKind.Cook;
                return;
            }
        }

        var work = PickWork(character, territory, assignment, ctx.Season);
        if (work != ActionKind.None)
        {
            // 工作台缺料就先去搬料，搬齐了再开工（RimWorld 的备料）。
            // 放在“认定委派”之前，否则会一直在空台子前干等。
            var bench = territory.Facilities.Find(f => f.Id == assignment.FacilityId)!;
            if (StartFetchForBench(character, worker, territory, ctx, work, bench))
                return;
            if (worker.Task != work || worker.FacilityId != bench.Id)
                Retarget(territory, worker, work, bench.Id, used);
            // 认定委派：Goal 就是那件活（原 Assigned 语义），主循环据此结算进度。
            worker.Goal = work;
            return;
        }

        // 无工作可做（无委派，或委派设施缺料/停摆）：自然退出工作并释放设施占用
        if (worker.Goal != ActionKind.None && !interruptible)
        {
            EndRoutine(worker);
        }
        Release(worker, used);

        // 正在进行的搬运或闲时活动（在无工可开时）继续推进，不反复重做决策
        if (interruptible)
            return;

        // 背包里有东西、据点又有能收的仓储：先把货送过去（RimWorld 的 haul）。
        // 搬运优先于娱乐——否则「每日一娱」无限重入，劳动产出永远躺在背包里。
        if (StartHaul(character, worker, territory, ctx, used))
            return;
        // 娱乐时段：到点名的那件消遣设施去消遣。
        if (assignment.Mode == SlotMode.Entertainment)
        {
            if (StartPlayAt(character, worker, territory, assignment.FacilityId, ctx, used))
                return;
        }
        else if (ctx.Day - character.Affect.LastPlayDay >= 1)
        {
            if (StartPlay(character, worker, territory, roster, ctx, used))
                return;
        }
        if (character.Condition.Spirit < character.Condition.MaxSpirit * 3 / 10)
        {
            if (StartRest(character, worker, territory, ctx, used))
                return;
        }
        // 无事可做：过自己的日子（坐下歇着 / 在房里忙活 / 串门），不原地发呆。
        StartLoiter(character, worker, territory, roster, ctx, used);
    }

    /// <summary>
    /// 备料：当前委派的工作台缺材料时，去仓储把缺的那一样搬过来。
    /// 搬到位后下次决策就能开工（<see cref="Finish"/> 只认台子上的料）。
    /// </summary>
    private static bool StartFetchForBench(CharacterState character, Worker worker,
        Territory territory, StepContext ctx, ActionKind task, Facility bench)
    {
        var recipe = FindAvailableRecipe(territory, bench, character, task);
        if (recipe == null)
            return false;

        // 台面上原料充足，无需备料，可直接开工
        if (territory.CanPayAt(bench, character, recipe.Costs))
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
            worker.Goal = ActionKind.Haul;
            worker.Task = ActionKind.None;
            worker.Progress = 0;
            worker.HaulItemId = cost.ItemId;
            worker.HaulCount = need;
            worker.HaulSourceId = source.Id;
            worker.HaulTargetId = bench.Id;
            worker.FacilityId = -1;

            if (source.RoomId == worker.RoomId)
            {
                // 人已经在源设施所在的房间：当面取货，进入送往工作台阶段
                var moved = territory.TakeFrom(character, source, cost.ItemId, need);
                if (moved <= 0)
                    continue;
                worker.HaulCount = moved;
                worker.HaulPhase = HaulPhase.Delivering;
                GotoRoom(worker, territory, bench.RoomId, r => Enterable(r, character, ctx));
            }
            else
            {
                // 物理走去源设施所在房间取料，全局禁止隔空取物
                worker.HaulPhase = HaulPhase.Fetching;
                GotoRoom(worker, territory, source.RoomId, r => Enterable(r, character, ctx));
            }

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

    private static bool HasDeliverableMeal(CharacterState character, Territory territory)
    {
        foreach (var pair in character.Bag.Items)
        {
            if (pair.Value > 0 && territory.IsFood(pair.Key))
            {
                var storage = territory.FindStorageFor(pair.Key);
                if (storage != null)
                    return true;
            }
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
            worker.HaulSourceId = -1;
            worker.HaulPhase = HaulPhase.Delivering;
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
        worker.HaulSourceId = -1;
        worker.HaulPhase = HaulPhase.Delivering;
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
    /// 推进角色的自主行为一格。所有日常行为的生命周期（进入/推进/结束/快照）
    /// 都在 <see cref="StateMachine.WorkerStateMachine"/> 里，本方法只负责驱动一步：
    /// 状态做完了就回决策层（Goal 归 None），否则保留当前状态。
    /// </summary>
    private void ProcessRoutine(CharacterState character, Worker worker, Territory territory, Roster roster, StepContext ctx)
    {
        if (worker.StateMachine.CurrentState == null)
            return;

        var workerCtx = new StateMachine.WorkerContext
        {
            Territory = territory,
            Character = character,
            Worker = worker,
            StepContext = ctx,
            UsedFacilities = new HashSet<int>(Seats().Keys),
            Rng = Rng,
        };
        if (worker.StateMachine.Step(workerCtx))
            EndRoutine(worker);
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
        worker.HaulSourceId = -1;
        worker.HaulPhase = HaulPhase.Delivering;
    }

    /// <summary>让某人的当前活动立刻收尾回决策层（解除跟随时用）。不动跟随标记本身。</summary>
    public void EndRoutineOf(int characterId)
    {
        var worker = _workers.Find(w => w.CharacterId == characterId);
        if (worker != null)
            EndRoutine(worker);
    }

    /// <summary>
    /// 跟随者的一步。睡在玩家床上时玩家不起就不醒；
    /// 夜里或累了先去自己的床睡（跟随标记保留，醒来接着跟）；
    /// 其余时候交给人跟着玩家走。
    /// </summary>
    private void FollowTick(CharacterState character, Worker worker, Territory territory,
        Roster roster, StepContext ctx, Dictionary<int, int> used, bool night)
    {
        if (worker.Goal == ActionKind.Sleep)
        {
            // 还陪在玩家睡的那张床上：玩家不起，就不醒。
            if (ctx.PlayerFixtureId >= 0 && worker.FacilityId == ctx.PlayerFixtureId)
                return;
            if (!night && !character.Condition.Tired)
            {
                WakeUp(character, worker, roster, ctx);
                EndRoutine(worker);
            }
            return;
        }
        if (character.Condition.Tired || night)
        {
            if (StartSleep(character, worker, territory, roster, ctx, used))
                return;
        }
        if (worker.Goal != ActionKind.Follow)
        {
            Release(worker, used);
            worker.Goal = ActionKind.Follow;
            worker.Task = ActionKind.None;
            worker.Progress = 0;
            worker.StateMachine.TransitionTo(new StateMachine.States.FollowingState(),
                new StateMachine.WorkerContext
                {
                    Territory = territory,
                    Character = character,
                    Worker = worker,
                    StepContext = ctx,
                    UsedFacilities = new HashSet<int>(used.Keys),
                    Rng = Rng,
                });
        }
    }

    /// <summary>起床结算：与心仪同伴同室同寝醒来温馨安宁（加心情）；与外人挤房扣心情；没床再扣。起床即换了干衣服。</summary>
    private void WakeUp(CharacterState character, Worker worker, Roster roster, StepContext ctx)
    {
        character.Condition.ChangeIntoDryClothes();
        var roommates = 0;
        var loverWithMe = false;
        foreach (var other in _workers)
        {
            if (other.CharacterId != worker.CharacterId && other.RoomId == worker.RoomId)
            {
                roommates++;
                var otherChar = roster.Find(other.CharacterId);
                if (otherChar != null && (character.Relations.Has(other.CharacterId, RelationFlag.Sworn) || otherChar.Condition.Bond == Bond.Lover))
                    loverWithMe = true;
            }
        }
        if (ctx.PlayerRoomId == worker.RoomId && (character.Relations.Has(ctx.MasterId, RelationFlag.Sworn) || character.Condition.Bond == Bond.Lover))
        {
            loverWithMe = true;
        }

        if (loverWithMe && roommates <= 1)
            character.Affect.AddMood(8);
        else if (roommates > 0)
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
    /// <summary>
    /// 休息：找一把能坐的椅子/沙发，坐下歇着（RestingState 负责回复与结束）。
    /// 找不到座位就返回假——上层会退回闲转。
    /// </summary>
    private static bool StartRest(CharacterState character, Worker worker, Territory territory,
        StepContext ctx, Dictionary<int, int> used)
    {
        var room = NearestRoomWith(territory, worker.RoomId, character, ctx,
            r => HasAction(territory, r.Id, ActionKind.Rest));
        if (room < 0)
            return false;

        var seat = FindFree(territory, room, ActionKind.Rest, used);
        if (seat == null)
            return false;

        Release(worker, used);
        worker.Goal = ActionKind.Rest;
        worker.Task = ActionKind.None;
        worker.Progress = 0;
        worker.Path.Clear();
        worker.FacilityId = seat.Id;
        GotoRoom(worker, territory, room, r => Enterable(r, character, ctx));
        worker.Phase = worker.Path.Count > 0 ? WorkPhase.Moving : WorkPhase.Idle;
        if (worker.Path.Count == 0)
            Sit(territory, worker, used);
        worker.StateMachine.TransitionTo(new StateMachine.States.RestingState(), new StateMachine.WorkerContext
        {
            Territory = territory,
            Character = character,
            Worker = worker,
            StepContext = ctx,
            UsedFacilities = new HashSet<int>(used.Keys),
        });
        return true;
    }

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

    private static bool StartMeal(CharacterState character, Worker worker, Territory territory, Roster roster, StepContext ctx, int window, Dictionary<int, int> used)
    {
        var seat = PickMealSeat(territory, worker.RoomId, character, ctx, used);
        if (seat == null)
            return false;

        Release(worker, used);
        character.Affect.LastMealDay = ctx.Day;
        character.Affect.LastMealWindow = window;

        worker.Goal = ActionKind.Meal;
        worker.Task = ActionKind.None;
        worker.Progress = 0;
        worker.FacilityId = seat.Id;
        if (!GotoRoom(worker, territory, seat.RoomId, r => Enterable(r, character, ctx)))
        {
            character.Affect.AddMood(-8);
            worker.Goal = ActionKind.None;
            return false;
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
        return true;
    }

    /// <summary>
    /// 挑一处能坐下吃饭的座位：优先直接赶往存放有熟食的餐桌，其次与餐桌同房有空位的餐座，最后任意可坐设施。
    /// 座位所在房间必须有食物可取（背包里有，或该房设施里存着）——
    /// 没有食物的房间不算，免得白跑一趟坐着却没得吃。
    /// </summary>
    private static Facility? PickMealSeat(Territory territory, int fromRoom,
        CharacterState character, StepContext ctx, Dictionary<int, int> used)
    {
        // 优先 1：直接赶往存放有熟食且支持就座的餐桌
        var tableWithMeal = territory.Facilities.Find(f => f.Built && f.IsTable && f.CanStore
            && f.Contents.Items.Any(p => p.Value > 0 && territory.IsFood(p.Key))
            && f.Supports(ActionKind.Meal)
            && used.GetValueOrDefault(f.Id) < f.Capacity);
        if (tableWithMeal != null)
            return tableWithMeal;

        // 优先 2：与餐桌同房且该房有食物的餐座
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

    /// <summary>
    /// 娱乐时段：到点名的那件消遣设施去消遣。设施没了或没空位就返回假，
    /// 上层退回自选的闲时活动。
    /// </summary>
    private static bool StartPlayAt(CharacterState character, Worker worker, Territory territory,
        int facilityId, StepContext ctx, Dictionary<int, int> used)
    {
        if (facilityId < 0)
            return false;
        var playFacility = territory.Facilities.Find(f => f.Id == facilityId && f.Built);
        if (playFacility == null || used.GetValueOrDefault(playFacility.Id) >= playFacility.Capacity)
            return false;

        Release(worker, used);
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
        if (worker.Phase == WorkPhase.Working)
            character.Affect.AddMood(-character.HardLaborMoodPenalty(worker.Task));
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

    /// <summary>三餐标准时刻：早 7:00 (420)、午 12:00 (720)、晚 18:00 (1080)。</summary>
    public static readonly int[] MealTimes = { 420, 720, 1080 };

    /// <summary>备餐窗口：开饭前 2 小时内，厨师开始寻找食材进入做饭状态。</summary>
    public static int CookPrepWindow(int minutes)
    {
        for (var i = 0; i < MealTimes.Length; i++)
        {
            var mealTime = MealTimes[i];
            if (minutes >= mealTime - 120 && minutes < mealTime)
                return i;
        }
        return -1;
    }

    /// <summary>就餐窗口：开饭前半小时起至饭点后 2 小时以内。</summary>
    public static int DiningWindow(int minutes)
    {
        for (var i = 0; i < MealTimes.Length; i++)
        {
            var mealTime = MealTimes[i];
            if (minutes >= mealTime - 30 && minutes < mealTime + 120)
                return i;
        }
        return -1;
    }

    private static int MealWindow(int minutes) => DiningWindow(minutes);

    /// <summary>领地内是否存在存放有熟食（Meal）的餐桌。</summary>
    public static bool HasTableWithMeal(Territory territory) =>
        territory.Facilities.Exists(f =>
            f.Built && f.IsTable && f.CanStore &&
            f.Contents.Items.Any(p => p.Value > 0 && territory.IsFood(p.Key)));

    /// <summary>
    /// 食客就餐检查：食客在开饭前半小时起往存放有熟食的餐桌或存有食物的餐室赶，若完全不存在食物则不盲目寻找。
    /// </summary>
    public static bool HasAvailableMeal(CharacterState character, Territory territory, int roomId)
    {
        if (HasTableWithMeal(territory))
            return true;
        if (character.Bag.Items.Any(p => p.Value > 0 && territory.IsFood(p.Key)))
            return true;
        return territory.Facilities.Exists(f => f.Built && f.CanStore &&
            f.Contents.Items.Any(p => p.Value > 0 && territory.IsFood(p.Key)));
    }

    /// <summary>超过饭点 2 小时未就餐扣除心情。仅在领地正常运转期间刚刚跨过 2 小时宽限期该时间步时结算一次。</summary>
    private static void CheckMissedMeals(CharacterState character, StepContext ctx, int minutes)
    {
        // 初始新加入或初次参与节律的角色，同步此前已完全结束的旧饭点，不追溯惩罚此前未经历的餐点
        if (character.Affect.LastMealDay < 0)
        {
            character.Affect.LastMealDay = ctx.Day;
            character.Affect.LastMealWindow = LastPassedMealIndex(minutes);
            return;
        }

        for (var i = 0; i < MealTimes.Length; i++)
        {
            var deadline = MealTimes[i] + 120;
            if (minutes >= deadline && minutes <= deadline + TerritoryClock.StepMinutes
                && (character.Affect.LastMealDay != ctx.Day || character.Affect.LastMealWindow < i))
            {
                character.Affect.LastMealDay = ctx.Day;
                character.Affect.LastMealWindow = i;
                character.Affect.AddMood(-8);
            }
        }
    }

    private static int LastPassedMealIndex(int minutes)
    {
        for (var i = MealTimes.Length - 1; i >= 0; i--)
        {
            if (minutes >= MealTimes[i] + 120)
                return i;
        }
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
