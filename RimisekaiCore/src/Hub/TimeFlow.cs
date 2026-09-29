using System.Collections.Generic;
using Rimisekai.Clock;
using Rimisekai.Housing;
using Rimisekai.Save;
using Rimisekai.Voice;

namespace Rimisekai.Hub;

public sealed partial class HubSession
{
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
        for (var i = 0; i < ticks; i++)
        {
            State.Clock.Advance(TerritoryClock.StepMinutes);
            ctx.Day = State.Clock.Day;
            ctx.NowTotal = (State.Clock.Day - 1) * GameClock.MinutesPerDay + State.Clock.Minutes;
            logs.AddRange(Day.Step(State.Territory, State.Roster, State.Clock.Slot, YieldFor, ctx));
        }
        foreach (var w in Day.Workers)
            _presence[w.CharacterId] = w.RoomId;
        // 选中的角色走开了就取消选中：右下角随之从“交流”退回“行动”，
        // 而不是留着一个够不着的人在“交流 · XX（不在场）”。
        DropSelectionIfGone();
        FlushActivities(ctx);
        return logs;
    }

    /// <summary>
    /// 把角色此刻在做什么写进日志：一个角色一行，只写当前行为。
    /// 日志是快照不是流水——同一次推进里角色做过的事只留最后一件，
    /// 因此不会把过程累积成一长串。
    /// </summary>
    private void FlushActivities(StepContext ctx)
    {
        if (ctx.Activity.Count == 0)
            return;
        // 按名册顺序输出，行序稳定，不随内部字典的插入顺序跳。
        foreach (var character in State.Roster.Members)
        {
            if (character.IsMaster)
                continue;
            if (ctx.Activity.TryGetValue(character.Id, out var text))
                WriteActivity(character.Id, text);
        }
    }

    private int YieldFor(ActionKind action) =>
        WorldEffects.YieldPercent(action, State.Clock.Season, State.Weather);

    public Schedule ScheduleOf(int characterId) => State.Territory.ScheduleOf(characterId);

    /// <summary>
    /// 设某个角色某一段的开关（空闲 / 工作 / 不干活）。主角与越界时段都拒绝——
    /// 时段是给据点里的人安排的，玩家自己的时段没有意义。
    /// </summary>
    public bool Assign(int characterId, int slot, SlotMode mode)
    {
        var character = State.Roster.Find(characterId);
        if (character == null || character.IsMaster)
            return false;
        if (slot < 0 || slot >= WorkSlot.Count)
            return false;
        State.Territory.Assign(characterId, slot, mode);
        return true;
    }

    /// <summary>
    /// 设某个角色对某类工作的优先级（0 = 不做，1-4 = 档位，1 最高）。
    /// 与时段开关是两件事：时段决定"这会儿上不上工"，优先级决定"上工时先干哪样"。
    /// </summary>
    public bool SetPriority(int characterId, ActionKind task, int priority)
    {
        var character = State.Roster.Find(characterId);
        if (character == null || character.IsMaster)
            return false;
        // None 是"不干活"的哨兵，不是可派行动，进不了优先级表。
        if (!ActionKindMap.IsWork(task))
            return false;
        State.Territory.SetPriority(characterId, task, priority);
        return true;
    }

    /// <summary>读某角色对某类工作的优先级。0 = 不做。</summary>
    public int PriorityOf(int characterId, ActionKind task) =>
        State.Territory.PriorityOf(characterId, task);

    /// <summary>日终：结算、天气与季节写入日志，再跑注册的日终事件。</summary>
    public DaySummary CloseDay(System.Random? random = null)
    {
        var summary = State.EndDay(random);
        if (summary.SeasonChanged)
            Write($"季节变为{SeasonName(summary.Season)}。");
        Write($"今日天气：{WeatherName(summary.Weather)}。");
        foreach (var dayEvent in State.DayEvents)
        {
            var line = dayEvent.Run(State, summary);
            if (!string.IsNullOrEmpty(line))
                Write(line);
        }
        return summary;
    }
}
