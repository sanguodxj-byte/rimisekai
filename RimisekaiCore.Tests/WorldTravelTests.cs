using System.Linq;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Save;
using Rimisekai.WorldMap;
using Rimisekai.WorldMap.Generators;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 大地图行进：领地落位、迷雾、按地貌耗时的逐格行进、
/// 兴趣点进出（编号不撞领地房、反复进出不叠房）、回领地要走回去、存档不留兴趣点房。
/// </summary>
public sealed class WorldTravelTests
{
    private static (HubSession Hub, GameState State) Setup()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "卧室", Open = true });
        state.Territory.Link(1, 2);
        state.Clock.SetTime(1, 9 * 60);
        var hub = new HubSession(state);
        hub.Enter(1);
        return (hub, state);
    }

    private static int Now(GameState s) => (s.Clock.Day - 1) * 24 * 60 + s.Clock.Minutes;

    /// <summary>队伍四邻里一格已探明、走得过去、又不是兴趣点或领地的格。</summary>
    private static (int X, int Y) PlainNeighbour(HubSession hub)
    {
        var w = hub.State.World;
        var (px, py) = hub.WorldPartyPosition;
        return Enumerable.Range(-2, 5).SelectMany(dx => Enumerable.Range(-2, 5).Select(dy => (X: px + dx, Y: py + dy)))
            .Where(t => (t.X, t.Y) != (px, py) && w.InBounds(t.X, t.Y) && hub.IsWorldDiscovered(t.X, t.Y)
                && w.IsPassable(t.X, t.Y) && w.PoiAt(t.X, t.Y) == null && (t.X, t.Y) != (w.HomeX, w.HomeY)
                && hub.WorldTravelMinutes(t.X, t.Y) > 0)
            .OrderByDescending(t => System.Math.Abs(t.X - px) + System.Math.Abs(t.Y - py))
            .First();
    }

    [Fact]
    public void Every_terrain_has_a_travel_cost_in_the_table()
    {
        foreach (var terrain in System.Enum.GetValues<WorldTerrainType>())
        {
            var minutes = MapCatalog.Default.GetTravelMinutes(terrain);
            Assert.True(minutes >= 0 && minutes % 5 == 0, $"{terrain}={minutes}");
        }
        Assert.Equal(0, MapCatalog.Default.GetTravelMinutes(WorldTerrainType.DeepWater));
        Assert.True(MapCatalog.Default.GetTravelMinutes(WorldTerrainType.Road)
            < MapCatalog.Default.GetTravelMinutes(WorldTerrainType.Mountain));
    }

    [Fact]
    public void Going_out_puts_the_party_on_the_home_tile_with_fog_around()
    {
        var (hub, state) = Setup();
        hub.SwitchToWorld();
        var w = state.World;
        Assert.Equal((w.HomeX, w.HomeY), hub.WorldPartyPosition);
        Assert.Equal(-1, hub.PlayerRoomId);
        Assert.True(hub.IsWorldDiscovered(w.HomeX + 2, w.HomeY + 2));
        Assert.False(hub.IsWorldDiscovered(w.HomeX + 4, w.HomeY));
        // 横版视口以队伍为中心，队伍脚下那间房在里面。
        Assert.Contains(hub.Map(), r => r.Id == hub.WorldPartyViewRoomId);
        Assert.Equal((w.HomeX, w.HomeY), hub.WorldTileOfViewRoom(hub.WorldPartyViewRoomId));
    }

    [Fact]
    public void Walking_spends_time_by_terrain_and_reveals_fog()
    {
        var (hub, state) = Setup();
        hub.SwitchToWorld();
        var (tx, ty) = PlainNeighbour(hub);
        var minutes = hub.WorldTravelMinutes(tx, ty);
        var before = Now(state);
        var seenBefore = state.Exploration.DiscoveredCount;
        Assert.True(hub.TravelTo(tx, ty));
        Assert.Equal(before + minutes, Now(state));
        Assert.Equal((tx, ty), hub.WorldPartyPosition);
        Assert.True(state.Exploration.DiscoveredCount >= seenBefore);
        Assert.Contains("你来到了", string.Join("\n", hub.History.Select(l => l.Fact)));
    }

    [Fact]
    public void Fogged_tiles_cannot_be_targeted()
    {
        var (hub, state) = Setup();
        hub.SwitchToWorld();
        var w = state.World;
        var far = Enumerable.Range(0, w.Width).SelectMany(x => Enumerable.Range(0, w.Height).Select(y => (x, y)))
            .First(t => w.IsPassable(t.x, t.y) && !hub.IsWorldDiscovered(t.x, t.y));
        Assert.Equal(-1, hub.WorldTravelMinutes(far.x, far.y));
        Assert.False(hub.TravelTo(far.x, far.y));
    }

    [Fact]
    public void Returning_home_walks_back_and_costs_time()
    {
        var (hub, state) = Setup();
        hub.SwitchToWorld();
        var (tx, ty) = PlainNeighbour(hub);
        Assert.True(hub.TravelTo(tx, ty));
        var before = Now(state);
        hub.SwitchToTerritory();
        Assert.True(Now(state) > before);
        Assert.Equal(MapLayer.Territory, hub.Layer);
        Assert.True(state.Exploration.AtHome);
        Assert.Equal(1, hub.PlayerRoomId);
    }

    [Fact]
    public void Entering_a_poi_needs_the_party_on_its_tile()
    {
        var (hub, state) = Setup();
        var w = state.World;
        var poi = w.Pois.OrderBy(p => System.Math.Abs(p.X - w.HomeX) + System.Math.Abs(p.Y - w.HomeY)).First();
        hub.SwitchToWorld();
        Assert.False(hub.EnterWorldPoi(poi.Id));
        Assert.True(hub.TravelToPoiDirect(poi.Id));
        Assert.Equal(MapLayer.WorldPoi, hub.Layer);
        hub.SwitchToWorld();
        Assert.Equal((poi.X, poi.Y), hub.WorldPartyPosition);
        // 走过的路已探明：从这里点回领地格就是走回去。
        Assert.True(hub.TravelTo(w.HomeX, w.HomeY));
        Assert.Equal(MapLayer.Territory, hub.Layer);
    }

    [Fact]
    public void Poi_rooms_never_collide_with_territory_rooms_and_do_not_pile_up()
    {
        var (hub, state) = Setup();
        var w = state.World;
        var poi = w.Pois.OrderBy(p => System.Math.Abs(p.X - w.HomeX) + System.Math.Abs(p.Y - w.HomeY)).First();
        var territoryRooms = state.Territory.Rooms.Count;

        for (var i = 0; i < 3; i++)
        {
            Assert.True(hub.TravelToPoiDirect(poi.Id));
            Assert.Equal(MapLayer.WorldPoi, hub.Layer);
            Assert.True(hub.PlayerRoomId >= HubSession.PoiRoomIdBase);
            Assert.Equal("大门", state.Territory.Rooms.Single(r => r.Id == hub.PlayerRoomId).Name);
            // 本家的两间房原样还在，名字没被兴趣点顶掉。
            Assert.Equal("庭院", state.Territory.Rooms.Single(r => r.Id == 1).Name);
            hub.SwitchToWorld();
            Assert.Equal(MapLayer.World, hub.Layer);
            Assert.Equal((poi.X, poi.Y), hub.WorldPartyPosition);
            Assert.Equal(territoryRooms, state.Territory.Rooms.Count);
        }
    }

    [Fact]
    public void Multi_block_poi_shows_every_block_on_the_5x5_grid_and_can_cross_between_blocks()
    {
        var (hub, state) = Setup();
        var w = state.World;
        var capital = w.Pois.Where(p => p.Type != WorldPoiType.Village && p.Type != WorldPoiType.Ruin && p.Type != WorldPoiType.Monastery)
            .OrderBy(p => System.Math.Abs(p.X - w.HomeX) + System.Math.Abs(p.Y - w.HomeY)).First();
        Assert.True(hub.TravelToPoiDirect(capital.Id));
        var poiRooms = state.Territory.Rooms.Where(r => r.RegionId >= Territory.MaxTerritoryRegions).ToList();
        Assert.True(poiRooms.Select(r => r.RegionId).Distinct().Count() > 1);
        Assert.All(poiRooms, r => Assert.True(r.X is >= 0 and < 5 && r.Y is >= 0 and < 5));
        Assert.Equal(25, hub.Map().Count);

        // 找一间通往别的块的房：站上去就能过界，过去后网格换成那一块。
        var gate = poiRooms.First(r => hub.CrossTargetRegion(r.Id) >= 0);
        hub.Arrive(gate.Id);
        var target = hub.CrossTargetRegion(gate.Id);
        Assert.True(hub.CrossTo(target));
        Assert.Equal(target, hub.RegionId);
        Assert.All(hub.Map(), r => Assert.Equal(target, r.RegionId));
    }

    [Fact]
    public void Saving_inside_a_poi_keeps_no_poi_rooms_and_restores_home_and_fog()
    {
        var (hub, state) = Setup();
        hub.SwitchToWorld();
        var (tx, ty) = PlainNeighbour(hub);
        Assert.True(hub.TravelTo(tx, ty));
        var w = state.World;
        Assert.True(hub.TravelToPoiDirect(w.Pois.OrderBy(p => System.Math.Abs(p.X - w.HomeX) + System.Math.Abs(p.Y - w.HomeY)).First().Id));
        var discovered = state.Exploration.DiscoveredCount;
        var unlocked = hub.TerritoryUnlockedRegions;

        var data = SaveSystem.Capture(state, hub);
        Assert.DoesNotContain(data.Territory.Rooms, r => r.Id >= HubSession.PoiRoomIdBase);
        Assert.Equal(1, data.Hub!.PlayerRoom);
        Assert.Equal(unlocked, data.Territory.UnlockedRegions);

        var loaded = SaveSystem.Restore(data);
        Assert.True(loaded.Exploration.DiscoveredCount >= discovered);
        Assert.True(loaded.Exploration.AtHome);
    }

    [Fact]
    public void Leaving_the_territory_takes_the_body_out_and_homefolk_do_not_seek_you()
    {
        var (hub, state) = Setup();
        var maid = state.Roster.Add("女仆");
        maid.Affect.ChatDesire = 100;
        hub.SwitchToWorld();
        Assert.Equal(-1, hub.PlayerRoomId);
        var logBefore = hub.History.Count;
        hub.PassTime(120);
        Assert.DoesNotContain(hub.History.Skip(logBefore), l => l.Text.Contains("女仆"));
        Assert.NotEqual(ActionKind.SeekChat, hub.Day.Workers.First(w => w.CharacterId == maid.Id).Goal);
        Assert.Equal(-1, hub.PlayerRoomId);
        Assert.Equal(1, hub.Snapshot().PlayerRoom);
        hub.SwitchToTerritory();
        Assert.Equal(1, hub.PlayerRoomId);
    }
}
