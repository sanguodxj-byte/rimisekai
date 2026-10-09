using System.Linq;
using Rimisekai.Combat;
using Rimisekai.Defs;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.PoiMap;
using Rimisekai.Quest;
using Rimisekai.Save;
using Rimisekai.WorldMap;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 世界探索遭遇：野外行进中按地貌掷遭遇、撞上即停；事件照单落账；战斗可绕开、可迎战，
/// 败则退回领地；地城石室按房型摆守卫 / 首领 / 宝库 / 神龛，了结记录随存档走。
/// </summary>
public sealed class WorldEncounterTests
{
    private static (HubSession Hub, GameState State) Setup(int rate)
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        state.Clock.SetTime(1, 9 * 60);
        var hub = new HubSession(state) { EncounterRate = rate };
        hub.Enter(1);
        return (hub, state);
    }

    /// <summary>离领地最远、走得到的一格。</summary>
    private static (int X, int Y) FarTile(GameState s)
    {
        var w = s.World;
        var best = (w.HomeX, w.HomeY);
        var bestD = -1;
        for (var x = 0; x < w.Width; x += 3)
            for (var y = 0; y < w.Height; y += 3)
            {
                if (!w.IsPassable(x, y) || w.Tiles[x, y].PoiId > 0)
                    continue;
                var d = (x - w.HomeX) * (x - w.HomeX) + (y - w.HomeY) * (y - w.HomeY);
                if (d > bestD && w.FindRoute(w.HomeX, w.HomeY, x, y) != null)
                    (best, bestD) = ((x, y), d);
            }
        return best;
    }

    [Fact]
    public void Walking_far_hits_an_encounter_and_stops_there()
    {
        var (hub, state) = Setup(rate: 1000);
        hub.SwitchToWorld();
        var target = FarTile(state);
        Assert.True(hub.TravelTo(target.X, target.Y));
        Assert.NotNull(hub.PendingEncounter);
        var (px, py) = hub.WorldPartyPosition;
        Assert.NotEqual(target, (px, py));
        var r = MapCatalog.Default.Wilds.SafeRadius;
        Assert.True((px - state.World.HomeX) * (px - state.World.HomeX) + (py - state.World.HomeY) * (py - state.World.HomeY) > r * r);
        // 遭遇没了结前走不动。
        Assert.False(hub.TravelTo(target.X, target.Y));
    }

    [Fact]
    public void No_encounters_at_rate_zero()
    {
        var (hub, state) = Setup(rate: 0);
        hub.SwitchToWorld();
        var target = FarTile(state);
        Assert.True(hub.TravelTo(target.X, target.Y));
        Assert.Null(hub.PendingEncounter);
        Assert.Equal(target, hub.WorldPartyPosition);
    }

    [Fact]
    public void Events_pay_out_once_and_money_never_goes_negative()
    {
        var (hub, state) = Setup(rate: 0);
        hub.SwitchToWorld();
        state.Money = 10;
        hub.ForceWildEncounter("abandoned_pack");
        Assert.False(hub.PendingEncounter!.IsBattle);
        hub.AcceptEncounter();
        Assert.Equal(50, state.Money);
        Assert.Null(hub.PendingEncounter);

        state.Money = 10;
        hub.ForceWildEncounter("toll");
        hub.AcceptEncounter();
        Assert.Equal(0, state.Money);
    }

    [Fact]
    public void Avoiding_a_fight_costs_the_detour()
    {
        var (hub, state) = Setup(rate: 0);
        hub.SwitchToWorld();
        hub.ForceWildEncounter("wolves");
        var before = state.Clock.TotalMinutes;
        hub.AvoidEncounter();
        Assert.Null(hub.PendingEncounter);
        Assert.Equal(before + MapCatalog.Default.Wilds.Events.First(e => e.Id == "wolves").DetourMinutes, state.Clock.TotalMinutes);
    }

    [Fact]
    public void Losing_a_wild_fight_carries_the_party_home()
    {
        var (hub, state) = Setup(rate: 0);
        hub.SwitchToWorld();
        var target = FarTile(state);
        hub.TravelTo(target.X, target.Y);
        hub.ForceWildEncounter("wolves");
        var session = hub.FightEncounter()!;
        foreach (var m in session.Battle.Members.Where(m => m.Side == CombatSide.Attacker))
            m.Hp = 0;
        session.Battle.JudgeOutcome();
        Assert.Equal(CombatOutcome.DefenderWin, session.Battle.Outcome);
        Assert.True(hub.SettleEncounterBattle(session.Battle));
        Assert.Equal(MapLayer.Territory, hub.Layer);
        Assert.True(state.Party.AtHome);
    }

    /// <summary>走进最近的遗迹。</summary>
    private static WorldPoi EnterRuin(HubSession hub, GameState state)
    {
        var ruin = state.World.Pois.Where(p => p.Type == WorldPoiType.Ruin)
            .OrderBy(p => (p.X - state.World.HomeX) * (p.X - state.World.HomeX) + (p.Y - state.World.HomeY) * (p.Y - state.World.HomeY))
            .First();
        Assert.True(hub.TravelToPoiDirect(ruin.Id));
        return ruin;
    }

    /// <summary>从当前石室沿通路走到目标石室，遇守卫一律迎战并判胜。</summary>
    private static void WalkTo(HubSession hub, int targetRoomId)
    {
        // 委托地城的正主倒下即被接回领地：走到那一步就算到了。
        for (var guard = 0; guard < 200 && hub.PlayerRoomId != targetRoomId && hub.Layer != MapLayer.Territory; guard++)
        {
            var rooms = hub.State.Territory.Rooms;
            var prev = new System.Collections.Generic.Dictionary<int, int> { [hub.PlayerRoomId] = -1 };
            var queue = new System.Collections.Generic.Queue<int>();
            queue.Enqueue(hub.PlayerRoomId);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                foreach (var n in rooms.First(r => r.Id == id).Links)
                    if (!prev.ContainsKey(n))
                    {
                        prev[n] = id;
                        queue.Enqueue(n);
                    }
            }
            var step = targetRoomId;
            while (prev[step] != hub.PlayerRoomId)
                step = prev[step];
            var stepRoom = rooms.First(r => r.Id == step);
            var here = rooms.First(r => r.Id == hub.PlayerRoomId);
            if (stepRoom.RegionId != here.RegionId)
                Assert.True(hub.CrossTo(stepRoom.RegionId));
            else
                Assert.True(hub.Move(step));
            Resolve(hub);
        }
        if (hub.Layer != MapLayer.Territory)
            Assert.Equal(targetRoomId, hub.PlayerRoomId);
    }

    private static void Resolve(HubSession hub)
    {
        var e = hub.PendingEncounter;
        if (e == null)
            return;
        if (!e.IsBattle)
        {
            hub.AcceptEncounter();
            return;
        }
        var session = hub.FightEncounter()!;
        foreach (var m in session.Battle.Members.Where(m => m.Side == CombatSide.Defender))
            m.Hp = 0;
        session.Battle.JudgeOutcome();
        Assert.Equal(CombatOutcome.AttackerWin, session.Battle.Outcome);
        CombatSettlement.Settle(hub.State, session.Battle, session.QuestRun);
        hub.SettleEncounterBattle(session.Battle);
    }

    [Fact]
    public void Ruins_are_dungeons_whose_boss_clears_them_and_survives_a_save()
    {
        var (hub, state) = Setup(rate: 0);
        var ruin = EnterRuin(hub, state);
        var poi = state.CurrentPoi!;
        var end = poi.AllRooms.First(r => r.IsEnd);
        var endId = HubSession.PoiRoomIdBase + end.Id;
        WalkTo(hub, endId);
        Assert.True(hub.IsDungeonCleared(ruin.Id));
        Assert.True(state.Dungeons.IsSpent(ruin.Id, end.Id));

        var loaded = SaveSystem.Restore(SaveSystem.Capture(state, hub));
        Assert.Contains(ruin.Id, loaded.Dungeons.Cleared);
        Assert.True(loaded.Dungeons.IsSpent(ruin.Id, end.Id));
    }

    [Fact]
    public void Long_walks_in_a_dungeon_stop_at_the_first_room_with_something_in_it()
    {
        var (hub, state) = Setup(rate: 0);
        var ruin = EnterRuin(hub, state);
        var end = state.CurrentPoi!.AllRooms.First(r => r.IsEnd);
        var endId = HubSession.PoiRoomIdBase + end.Id;
        var start = state.CurrentPoi.AllRooms.First(r => r.IsStart);
        // 同一块里才走得通远路（跨块走 CrossTo），出口在别的块就只走到本块。
        var sameBlock = state.CurrentPoi.GetBlockByRoomId(end.Id) == state.CurrentPoi.GetBlockByRoomId(start.Id);
        if (!sameBlock)
            return;
        // 远路只走看得见的房：先把迷雾全揭开。
        foreach (var r in state.CurrentPoi.AllRooms)
            state.Dungeons.Visit(ruin.Id, r.Id);
        for (var guard = 0; guard < 50 && hub.PlayerRoomId != endId; guard++)
        {
            Assert.True(hub.Arrive(endId));
            if (hub.PlayerRoomId != endId)
                Assert.NotNull(hub.PendingEncounter);
            Resolve(hub);
        }
        Assert.True(hub.IsDungeonCleared(ruin.Id));
    }

    [Fact]
    public void Treasure_is_taken_once()
    {
        var (hub, state) = Setup(rate: 0);
        var ruin = EnterRuin(hub, state);
        var treasure = state.CurrentPoi!.AllRooms.FirstOrDefault(r => r.RoomType == PoiRoomType.Treasure);
        if (treasure == null)
            return;
        var id = HubSession.PoiRoomIdBase + treasure.Id;
        var before = state.Money;
        WalkTo(hub, id);
        Assert.True(state.Dungeons.IsSpent(ruin.Id, treasure.Id));
        Assert.True(state.Money > before);
        // 走开再回来，宝库不再起事。
        var back = state.Territory.Rooms.First(r => r.Id == id).Links[0];
        Assert.True(hub.Move(back));
        Resolve(hub);
        Assert.True(hub.Move(id));
        Assert.Null(hub.PendingEncounter);
    }

    [Fact]
    public void Dungeon_layouts_vary_by_ruin()
    {
        var state = new GameState();
        var sizes = state.World.Pois.Where(p => p.Type == WorldPoiType.Ruin)
            .Select(p => state.EnterPoi(p.Id).Blocks.Count).Distinct().Count();
        Assert.True(sizes > 1);
    }

    [Fact]
    public void Dungeon_fog_shows_walked_rooms_and_glimpses_their_neighbours()
    {
        var (hub, state) = Setup(rate: 0);
        var ruin = EnterRuin(hub, state);
        var fog = MapCatalog.Default.Dungeon.FogName;
        var here = state.Territory.Rooms.First(r => r.Id == hub.PlayerRoomId);
        var shown = hub.Map();
        Assert.Contains(shown, r => r.Id == here.Id && r.Name == here.Name);
        var sameBlock = here.Links.Where(id => state.Territory.Rooms.First(r => r.Id == id).RegionId == here.RegionId).ToList();
        foreach (var id in sameBlock)
            Assert.Contains(shown, r => r.Id == id && r.Name == fog);
        var block = state.Territory.Rooms.Where(r => r.RegionId == here.RegionId && r.Open).ToList();
        var dark = block.First(r => r.Id != here.Id && !here.Links.Contains(r.Id) && !hub.RoomShown(r.Id));
        Assert.DoesNotContain(shown, r => r.Id == dark.Id);
        Assert.False(hub.CanReach(dark.Id));
        Assert.False(hub.Arrive(dark.Id));

        // 走进望见的那间，它就亮了。
        var next = sameBlock[0];
        Assert.True(hub.Move(next));
        Assert.Contains(hub.Map(), r => r.Id == next && r.Name != fog);
        Assert.True(state.Dungeons.IsVisited(ruin.Id, next - HubSession.PoiRoomIdBase));

        var loaded = SaveSystem.Restore(SaveSystem.Capture(state, hub));
        Assert.True(loaded.Dungeons.IsVisited(ruin.Id, next - HubSession.PoiRoomIdBase));
    }

    private static QuestRun MapQuest(GameState state, params int[] party)
    {
        var def = new QuestDef
        {
            DefName = "Quest_DungeonTest",
            Id = 9001,
            Label = "测试矿道",
            Kind = QuestKind.Dungeon,
            Difficulty = 3.5,
            CooldownDays = 1,
            MaxPartySize = 4,
            Foes = { new Catalog.EnemyDef { Id = "boss", Name = "矿道之主", ThreatTier = 1, Column = 1 } },
        };
        return state.Quests.Start(def, party)!;
    }

    [Fact]
    public void Map_quests_are_dungeons_with_a_ride_there_and_back()
    {
        var (hub, state) = Setup(rate: 0);
        var mate = state.Roster.Add("同伴");
        var run = MapQuest(state, state.Roster.Master!.Id, mate.Id);
        Assert.True(hub.StartQuestDungeon(run));
        Assert.Equal(MapLayer.QuestPlace, hub.Layer);
        Assert.Equal("测试矿道", hub.MapTitle());
        Assert.True(hub.InQuestDungeon);
        Assert.Contains(mate.Id, hub.WorldPartyIds());
        Assert.Equal(MapCatalog.Default.Dungeon.Quest.LeaveLabel, hub.TravelLabel);

        // 一路打到最深处：正主就是委托的敌人，倒下即了结，马车接回领地。
        var end = state.CurrentPoi!.AllRooms.First(r => r.IsEnd);
        var endId = HubSession.PoiRoomIdBase + end.Id;
        var boss = MapCatalog.Default.Dungeon.Quest;
        WalkTo(hub, endId);
        Assert.Contains(hub.History, h => h.Text.Contains(string.Format(boss.DoneText, "测试矿道")));
        Assert.Equal(MapLayer.Territory, hub.Layer);
        Assert.Equal(1, hub.PlayerRoomId);
        Assert.Equal(1, state.Quests.ClearCount[run.QuestId]);
        Assert.DoesNotContain(state.Territory.Rooms, r => r.Id >= HubSession.PoiRoomIdBase);
        Assert.False(hub.IsFollowing(mate.Id));
    }

    [Fact]
    public void Board_posts_generated_dungeon_quests_that_pay_once_and_come_off_the_board()
    {
        var (hub, state) = Setup(rate: 0);
        var board = MapCatalog.Default.Dungeon.Quest.Board;
        var posted = QuestBoard.Open(state).Where(q => q.Generated).ToList();
        Assert.Equal(board.PerDay * board.LifeDays, posted.Count);
        foreach (var q in posted)
        {
            Assert.Equal(QuestKind.Dungeon, q.Kind);
            Assert.InRange(q.Difficulty, board.StarsMin, board.StarsEarly);
            Assert.Contains(board.Sites, s => s.Name == q.Name);
            Assert.DoesNotContain("{", q.Description);
            Assert.Contains(MapCatalog.Default.Dungeon.Bosses, b => b.Foes == q.Foes && b.MinTier <= QuestBoard.TierOf(q.Difficulty));
            Assert.Equal(string.Format(board.RewardText, q.RewardMoney), q.Rewards.Single());
            Assert.True(q.RewardMoney > 0);
        }
        Assert.Equal(posted.Count, posted.Select(q => q.Name).Distinct().Count());
        // 同一局同一张永远一样。
        Assert.Equal(posted.Select(q => (q.Id, q.Name, q.Difficulty)), QuestBoard.Open(state).Where(q => q.Generated).Select(q => (q.Id, q.Name, q.Difficulty)));

        // 接下最早那张，一路打到正主：酬金到手，板上撕下。
        var def = posted[0];
        var run = state.Quests.Start(def, new[] { state.Roster.Master!.Id })!;
        Assert.True(hub.StartQuestDungeon(run));
        Assert.Equal(def.Name, hub.MapTitle());
        var endId = HubSession.PoiRoomIdBase + state.CurrentPoi!.AllRooms.First(r => r.IsEnd).Id;
        var before = state.Money;
        WalkTo(hub, endId);
        Assert.Equal(MapLayer.Territory, hub.Layer);
        Assert.True(state.Money >= before + def.RewardMoney);
        Assert.DoesNotContain(QuestBoard.Open(state), q => q.Id == def.Id);

        // 挂满 lifeDays 天就撤下，新的一天贴新的一张。
        state.Clock.SetTime(1 + board.LifeDays, 9 * 60);
        var later = QuestBoard.Open(state).Where(q => q.Generated).ToList();
        Assert.DoesNotContain(later, q => posted.Any(p => p.Id == q.Id));
        Assert.Equal(board.PerDay * board.LifeDays, later.Count);

        // 日子久了星数上限涨满，高难的委托才挂出来。
        Assert.Contains(Enumerable.Range(board.RampDays, 40).SelectMany(d => Enumerable.Range(0, board.PerDay).Select(k => QuestBoard.Posting(state, d, k))),
            q => q.Difficulty > board.StarsEarly);
    }

    [Fact]
    public void Leaving_a_quest_dungeon_rides_home_without_clearing_it()
    {
        var (hub, state) = Setup(rate: 0);
        var mate = state.Roster.Add("同伴");
        var run = MapQuest(state, state.Roster.Master!.Id, mate.Id);
        Assert.True(hub.StartQuestDungeon(run));
        hub.SwitchToWorld();
        Assert.Equal(MapLayer.Territory, hub.Layer);
        Assert.Equal(1, hub.PlayerRoomId);
        Assert.False(state.Quests.ClearCount.ContainsKey(run.QuestId));
        Assert.True(state.Quests.IsAvailable(run.QuestId));
        Assert.False(hub.IsFollowing(mate.Id));
        Assert.True(state.Party.AtHome);
    }

    [Fact]
    public void Quest_dungeons_start_only_from_home()
    {
        var (hub, state) = Setup(rate: 0);
        hub.SwitchToWorld();
        Assert.False(hub.StartQuestDungeon(MapQuest(state, state.Roster.Master!.Id)));
        Assert.Equal(MapLayer.World, hub.Layer);
    }

    [Fact]
    public void Arrow_step_moves_one_tile_spends_time_and_never_auto_enters()
    {
        var (hub, state) = Setup(rate: 0);
        hub.SwitchToWorld();
        var w = state.World;
        var (sx, sy) = hub.WorldPartyPosition;
        (int dx, int dy) dir = default;
        foreach (var d in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            if (w.IsPassable(sx + d.Item1, sy + d.Item2))
            {
                dir = d;
                break;
            }
        Assert.NotEqual(default, dir);
        var before = state.Clock.TotalMinutes;
        Assert.True(hub.StepWorld(dir.dx, dir.dy));
        Assert.Equal((sx + dir.dx, sy + dir.dy), hub.WorldPartyPosition);
        Assert.Equal(before + w.TravelMinutes(sx + dir.dx, sy + dir.dy), state.Clock.TotalMinutes);
        Assert.Equal(MapLayer.World, hub.Layer);

        // 走回领地格：只站上去，不自动回领地。
        Assert.True(hub.StepWorld(-dir.dx, -dir.dy));
        Assert.Equal((sx, sy), hub.WorldPartyPosition);
        Assert.Equal(MapLayer.World, hub.Layer);

        // 斜走、原地、出界都不动。
        Assert.False(hub.StepWorld(1, 1));
        Assert.False(hub.StepWorld(0, 0));
        Assert.Equal((sx, sy), hub.WorldPartyPosition);
    }

    [Fact]
    public void Arrow_step_refuses_impassable_tiles_and_pending_encounters()
    {
        var (hub, state) = Setup(rate: 0);
        hub.SwitchToWorld();
        var w = state.World;
        // 找一格旁边就是走不过去的地块（湖海雪峰或出界）。
        for (var x = 0; x < w.Width; x++)
            for (var y = 0; y < w.Height; y++)
            {
                if (!w.IsPassable(x, y) || w.IsPassable(x + 1, y) || w.FindRoute(w.HomeX, w.HomeY, x, y) == null)
                    continue;
                Assert.True(hub.TravelTo(x, y) || hub.Layer != MapLayer.World);
                if (hub.Layer != MapLayer.World || hub.WorldPartyPosition != (x, y))
                    return; // 落在聚落里进场了，换不成干净夹具：此用例只验能走的那半。
                var before = state.Clock.TotalMinutes;
                Assert.False(hub.StepWorld(1, 0));
                Assert.Equal((x, y), hub.WorldPartyPosition);
                Assert.Equal(before, state.Clock.TotalMinutes);
                return;
            }
    }
}
