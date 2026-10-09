using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Rimisekai.WorldMap;
using Xunit;

namespace Rimisekai.Tests;

public sealed class MapCatalogTableTests
{
    [Fact]
    public void World_map_room_facility_and_poi_tables_are_separate_and_referenced_by_id()
    {
        var content = FindContentDirectory();
        using var map = JsonDocument.Parse(File.ReadAllText(Path.Combine(content, "map_defs.json")));
        using var rooms = JsonDocument.Parse(File.ReadAllText(Path.Combine(content, "room_defs.json")));
        using var facilities = JsonDocument.Parse(File.ReadAllText(Path.Combine(content, "facility_defs.json")));
        using var pois = JsonDocument.Parse(File.ReadAllText(Path.Combine(content, "poi_defs.json")));

        Assert.Equal(
            new[] { "dungeon", "terrains", "wilds" },
            map.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));

        var roomIds = rooms.RootElement.GetProperty("rooms").EnumerateArray()
            .Select(row => row.GetProperty("id").GetInt32()).ToHashSet();
        var facilityIds = facilities.RootElement.GetProperty("facilities").EnumerateArray()
            .Select(row => row.GetProperty("id").GetInt32()).ToHashSet();
        var districts = pois.RootElement.GetProperty("districts").EnumerateArray().ToArray();
        var districtIds = districts.Select(district => district.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(rooms.RootElement.GetProperty("rooms").GetArrayLength(), roomIds.Count);
        Assert.Equal(facilities.RootElement.GetProperty("facilities").GetArrayLength(), facilityIds.Count);
        foreach (var district in districts)
        {
            Assert.All(district.GetProperty("roomIds").EnumerateArray(), id => Assert.Contains(id.GetInt32(), roomIds));
            Assert.All(district.GetProperty("facilityIds").EnumerateArray(), id => Assert.Contains(id.GetInt32(), facilityIds));
        }
        foreach (var poi in pois.RootElement.GetProperty("pois").EnumerateArray())
            Assert.All(poi.GetProperty("districts").EnumerateArray(), id => Assert.Contains(id.GetString()!, districtIds));

        var villageDistrictId = pois.RootElement.GetProperty("pois").EnumerateArray()
            .First(poi => poi.GetProperty("type").GetString() == "Village")
            .GetProperty("districts")[0].GetString()!;
        var villageDistrict = districts.Single(district => district.GetProperty("id").GetString() == villageDistrictId);
        var roomCount = villageDistrict.GetProperty("roomIds").GetArrayLength();
        Assert.True(roomCount > 0);
        Assert.True(villageDistrict.GetProperty("facilityIds").GetArrayLength() > 0);

        var catalog = MapCatalog.Default;
        Assert.Equal("Nature", catalog.GetDistrictRoomTemplate(villageDistrictId, 0).Category);
        Assert.Equal("Facility", catalog.GetDistrictRoomTemplate(villageDistrictId, roomCount).Category);
        Assert.Equal(2, catalog.GetPoiScale(WorldPoiType.Capital).blocksW);
    }

    private static string FindContentDirectory()
    {
        var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (current != null)
        {
            var path = Path.Combine(current.FullName, "content", "map_defs.json");
            if (File.Exists(path))
                return Path.GetDirectoryName(path)!;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("content/map_defs.json not found from the test output directory");
    }
}
