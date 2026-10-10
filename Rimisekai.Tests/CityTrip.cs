using System.Linq;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.WorldMap;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>进城买卖：沿大地图走到最近一座有商店的聚落（村、镇、王都），进场后走进商店那一间。</summary>
internal static class CityTrip
{
    internal static readonly WorldPoiType[] ShopTowns = { WorldPoiType.Village, WorldPoiType.Town, WorldPoiType.Capital };

    /// <summary>最近一座有商店的聚落（按大地图路程）。</summary>
    internal static WorldPoi NearestTown(HubSession hub)
    {
        var world = hub.State.World;
        return world.Pois.Where(p => ShopTowns.Contains(p.Type))
            .Select(p => (Poi: p, Route: world.FindRoute(world.HomeX, world.HomeY, p.X, p.Y)))
            .Where(p => p.Route != null)
            .OrderBy(p => p.Route!.Sum(c => world.TravelMinutes(c.x, c.y)))
            .First().Poi;
    }

    /// <summary>走进城镇商店，返回商店的房号。</summary>
    internal static int ToShop(HubSession hub)
    {
        Assert.True(hub.TravelToPoiDirect(NearestTown(hub).Id), "走到最近的城镇");
        var shop = hub.State.Territory.Rooms.Single(r => r.RegionId >= Territory.MaxTerritoryRegions && r.HasTag(Territory.CityShopTag));
        if (hub.PlayerRoomId != shop.Id)
            Assert.True(hub.Arrive(shop.Id), "走进商店");
        Assert.True(hub.AtCityShop);
        return shop.Id;
    }

    /// <summary>回领地（路上不起遭遇）。</summary>
    internal static void Home(HubSession hub)
    {
        hub.SwitchToTerritory(safe: true);
        Assert.Equal(MapLayer.Territory, hub.Layer);
    }
}
