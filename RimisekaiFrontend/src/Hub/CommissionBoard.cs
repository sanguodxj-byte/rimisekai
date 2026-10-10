using System.Collections.Generic;
using Rimisekai.Quest;

namespace Rimisekai.Hub;

public sealed partial class HubSession
{
    /// <summary>接一单委托要花的时间：赶路、办事，一律 8 小时，与去几个人无关。</summary>
    public const int CommissionMinutes = 8 * 60;

    /// <summary>
    /// 接下委托：开一份委托记录，直接推过 <see cref="CommissionMinutes"/>，之后才开打（或坐马车进地城）。
    /// 地城委托要人在领地里才接得了；接不了返回 null，不耗时。报酬不变，仍在结算时按委托表发。
    /// </summary>
    public QuestRun? AcceptCommission(QuestDef def, IReadOnlyList<int> party)
    {
        if (def.Kind == QuestKind.Dungeon && (Layer != MapLayer.Territory || PendingEncounter != null || PlayerRoomId < 0))
            return null;
        var run = State.Quests.Start(def, party);
        if (run == null)
            return null;
        // 一行人出门这 8 小时：主人的身子离开领地（家里的人不来找他搭话），同去的人也不在家干活。
        LeaveFixture();
        var home = Layer == MapLayer.Territory ? PlayerRoomId : -1;
        if (home >= 0)
            LeaveTerritoryBody();
        foreach (var id in party)
        {
            if (id == State.Roster.Master?.Id)
                continue;
            Day.EndRoutineOf(id);
            Day.Away.Add(id);
        }
        Write($"你们接下「{def.Name}」，赶路办事花了 {CommissionMinutes / 60} 个小时。");
        PassTime(CommissionMinutes);
        Day.Away.Clear();
        if (home >= 0)
            Enter(home);
        return run;
    }
}
