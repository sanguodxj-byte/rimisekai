using System.Collections.Generic;
using System.Linq;
using Rimisekai.Clock;
using Rimisekai.Housing;
using Rimisekai.Save;
using Rimisekai.Voice;

namespace Rimisekai.Hub;

public sealed partial class HubSession
{
    /// <summary>
    /// 有人在主人所在的房间锁门睡下、主人醒着：主人跟别人一样被请到隔壁进得去的房间（没有就留在原地，不把人关死）。
    /// </summary>
    private void EvictMasterFromSleepersRoom()
    {
        var master = State.Roster.Master;
        var room = Room(PlayerRoomId);
        if (master == null || room == null || State.Territory.MasterAsleep
            || !State.Territory.SleeperLocks.TryGetValue(room.Id, out var sleeper) || !State.Territory.BarsEntry(room, master))
            return;
        var outside = State.Territory.DoorOut(room, master);
        if (outside == null)
            return;
        LeaveFixture();
        Write($"{NameOf(sleeper.SleeperId)}锁门睡下，你被请出了{room.Name}。");
        Enter(outside.Id);
    }

    /// <summary>推进时间。委派角色按当前槽位跑分钟级结算，闲时跑自主节律。</summary>
    public List<WorkLog> PassTime(int minutes)
    {
        var logs = new List<WorkLog>();
        var master = State.Roster.Master;
        var ctx = new StepContext
        {
            PlayerRoomId = PlayerRoomId,
            PlayerFixtureId = UsingFixtureId ?? -1,
            MasterId = master?.Id ?? -1,
            MasterFactionId = master?.FactionId ?? -1,
            Voice = this,
        };
        var ticks = minutes / TerritoryClock.StepMinutes;
        var seasonBefore = State.Clock.Season;
        var weatherBefore = State.Weather;
        var hour = State.Clock.Minutes / 60;
        for (var i = 0; i < ticks; i++)
        {
            var dayBefore = State.Clock.Day;
            State.Clock.Advance(TerritoryClock.StepMinutes);
            // 跨过午夜：结算刚结束的一天（恢复、任务、作物生长）。
            if (State.Clock.Day != dayBefore)
                State.SettleDay(seasonBefore, Day.Rng);
            // 每跨过一个整点：天气按马尔可夫演化一步；变天写一行日志。
            if (State.Clock.Minutes / 60 != hour)
            {
                hour = State.Clock.Minutes / 60;
                var before = State.Weather;
                State.Weather = WorldEffects.Advance(before, State.Clock.Season, Day.Rng);
                if (State.Weather != before)
                    WriteEnvironment($"天气转为{WeatherName(State.Weather)}。");
            }
            ctx.Day = State.Clock.Day;
            ctx.NowTotal = (State.Clock.Day - 1) * GameClock.MinutesPerDay + State.Clock.Minutes;
            ctx.Weather = State.Weather;
            ctx.Season = State.Clock.Season;
            logs.AddRange(Day.Step(State.Territory, State.Roster, State.Clock.Slot, YieldFor, ctx));
            // 自己开的店：访客进出、当班的人招呼生意（见 Commerce）。
            SettleVisits(Commerce.Step(State.Territory, State.Roster, Day.Workers, ctx.NowTotal,
                ctx.MasterId, ctx.PlayerRoomId, ctx.PlayerFixtureId, Day.Rng));
            // 主角的衣着干湿与 NPC 同口径：按此刻所在房间结算一格。
            if (master != null)
                WorldEffects.SettleWetness(master, State.Territory, PlayerRoomId, State.Weather);
        }
        foreach (var w in Day.Workers)
            _presence[w.CharacterId] = w.RoomId;
        PlaceVisitors();
        EvictMasterFromSleepersRoom();
        if (master != null)
        {
            Worker? masterWorker = null;
            foreach (var w in Day.Workers)
            {
                if (w.CharacterId == master.Id)
                {
                    masterWorker = w;
                    break;
                }
            }
            if (masterWorker != null && masterWorker.RoomId >= 0 && masterWorker.Goal != ActionKind.None)
            {
                PlayerRoomId = masterWorker.RoomId;
                UsingFixtureId = masterWorker.FacilityId >= 0 ? masterWorker.FacilityId : null;
            }
        }
        // 跟随的持续资格随时间复查：好感跌出“好感”档就不再跟着。
        SweepBrokenFollows();
        // 选中的角色走开了就取消选中：右下角随之从“交流”退回“行动”，
        // 而不是留着一个够不着的人在“交流 · XX（不在场）”。
        DropSelectionIfGone();
        FlushActivities(ctx);
        // 换季/变天/剧情到点的重大事件进队；随后逐段演出——
        // 演出期间插画盖网格、右下锁成继续与选项，走完才交还据点。
        CollectEvents(seasonBefore != State.Clock.Season, weatherBefore != State.Weather, false);
        PlayNextEvent();
        // 走在路上不开口，等落脚（SeeAround）再问。
        if (!_walking)
            RollChatter();
        return logs;
    }

    /// <summary>
    /// 把角色此刻在做什么写进日志：一个角色一行，只写当前行为。
    /// 日志是快照不是流水——同一次推进里角色做过的事只留最后一件，
    /// 因此不会把过程累积成一长串。
    /// </summary>
    private void FlushActivities(StepContext ctx)
    {
        foreach (var (id, text) in ctx.Activity)
            _knownActivity[id] = text;
        // 走在路上：这段时间里人还没落脚，等进了门再按新房间看（见 Walk / SeeAround）。
        if (_walking)
        {
            foreach (var (id, text) in ctx.Activity)
                _walkSeen[id] = text;
            return;
        }
        WriteActivities(ctx.Activity);
    }

    private bool _walking;
    private readonly Dictionary<int, string> _walkSeen = new();

    /// <summary>
    /// 每个角色最近一次被叙述的行为（不论玩家在不在场都记）。
    /// 落脚时写在场的人「在做什么」用它——哪怕这一步没推进时间（开局、零耗时的过界），也不会漏掉。
    /// </summary>
    private readonly Dictionary<int, string> _knownActivity = new();

    /// <summary>
    /// 走过去花的时间：推进时不记活动，留到落脚后由 <see cref="SeeAround"/> 按落脚那间房写，
    /// 不会把出发那间房里的人写进来到新房间的这次日志。
    /// </summary>
    private void Walk(int minutes)
    {
        _walkSeen.Clear();
        _walking = true;
        PassTime(minutes);
        _walking = false;
    }

    /// <summary>落脚后：把每人最近的行为按此刻所在房间筛一遍写入（写在场景描述之后）。</summary>
    private void SeeAround()
    {
        WriteActivities(_knownActivity);
        _walkSeen.Clear();
        RollChatter();
    }

    private void WriteActivities(IReadOnlyDictionary<int, string> activity)
    {
        // 人在大地图上（身子不在任何领地房间）：家里谁在做什么一概看不见。
        if (activity.Count == 0 || PlayerRoomId < 0)
            return;
        // 按名册顺序输出，行序稳定，不随内部字典的插入顺序跳。
        foreach (var character in State.Roster.Members)
        {
            if (character.IsMaster)
                continue;
            // 铁律：日志显示是以房间为单元的，该房间看不到的信息禁止显示在日志！
            // 只有与玩家同处当前房间（PlayerRoomId）的角色活动才允许写入日志。
            // 唯一例外：角色在门外找玩家搭话（SeekWaiting），声音能被门内的主角听见。
            var isHere = _presence.GetValueOrDefault(character.Id, -1) == PlayerRoomId;
            var worker = Day.Track(character.Id, PlayerRoomId);
            var isSeekingAtDoor = worker != null && (worker.SeekWaiting || worker.ChatRoom == PlayerRoomId);
            if (!isHere && !isSeekingAtDoor)
                continue;

            if (activity.TryGetValue(character.Id, out var text))
                WriteActivity(character.Id, text);
        }
    }

    private int YieldFor(ActionKind action) =>
        WorldEffects.YieldPercent(action, State.Clock.Season, State.Weather);

    public Schedule ScheduleOf(int characterId) => State.Territory.ScheduleOf(characterId);

    /// <summary>
    /// 设某个角色某一段的安排（空闲 / 工作 / 娱乐，后两者要点名一件设施）。
    /// 玩家自身也可排班，在有工作安排的日程时间内执行自动工作。
    /// </summary>
    public bool Assign(int characterId, int slot, SlotMode mode, int facilityId = -1)
    {
        var character = State.Roster.Find(characterId);
        if (character == null)
            return false;
        if (slot < 0 || slot >= WorkSlot.Count)
            return false;
        return State.Territory.Assign(characterId, slot, mode, facilityId);
    }

    /// <summary>某角色某段的安排（开关 + 点名的设施）。</summary>
    public SlotAssignment AssignmentOf(int characterId, int slot) =>
        State.Territory.AssignmentOf(characterId, slot);

    /// <summary>日终：结算、天气与季节写入日志，再跑注册的日终事件。</summary>
    public DaySummary CloseDay(System.Random? random = null)
    {
        var summary = State.EndDay(random);
        if (summary.SeasonChanged)
            WriteEnvironment($"季节变为{SeasonName(summary.Season)}。");
        WriteEnvironment($"今日天气{WeatherName(summary.Weather)}。");
        foreach (var dayEvent in State.DayEvents)
        {
            var line = dayEvent.Run(State, summary);
            if (!string.IsNullOrEmpty(line))
                WriteEnvironment(line);
        }
        return summary;
    }
}
