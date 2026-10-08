using System.Linq;
using Rimisekai.Combat;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.PoiMap;
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
        for (var guard = 0; guard < 200 && hub.PlayerRoomId != targetRoomId; guard++)
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
}
