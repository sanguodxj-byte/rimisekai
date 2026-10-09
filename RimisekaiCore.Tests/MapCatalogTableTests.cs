using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Rimisekai.PoiMap.Generator;
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
        var facilityRows = facilities.RootElement.GetProperty("facilities").EnumerateArray()
            .ToDictionary(row => row.GetProperty("id").GetInt32());
        var facilityIds = facilityRows.Keys.ToHashSet();
        var districts = pois.RootElement.GetProperty("districts").EnumerateArray().ToArray();
        var districtIds = districts.Select(district => district.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(rooms.RootElement.GetProperty("rooms").GetArrayLength(), roomIds.Count);
        Assert.Equal(facilities.RootElement.GetProperty("facilities").GetArrayLength(), facilityIds.Count);
        foreach (var facility in facilityRows.Values)
        {
            var boundRoomIds = facility.GetProperty("roomIds").EnumerateArray().Select(id => id.GetInt32()).ToArray();
            Assert.NotEmpty(boundRoomIds);
            Assert.All(boundRoomIds, id => Assert.Contains(id, roomIds));
        }
        foreach (var district in districts)
        {
            Assert.All(district.GetProperty("roomIds").EnumerateArray(), id => Assert.Contains(id.GetInt32(), roomIds));
            Assert.All(district.GetProperty("facilityIds").EnumerateArray(), id => Assert.Contains(id.GetInt32(), facilityIds));
            var districtRoomIds = district.GetProperty("roomIds").EnumerateArray().Select(id => id.GetInt32()).ToHashSet();
            foreach (var id in district.GetProperty("facilityIds").EnumerateArray().Select(id => id.GetInt32()))
            {
                var facilityRoomIds = facilityRows[id].GetProperty("roomIds").EnumerateArray().Select(roomId => roomId.GetInt32());
                Assert.Contains(facilityRoomIds, districtRoomIds.Contains);
            }
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
        var villageRoom = catalog.GetDistrictRoomTemplate(villageDistrictId, 0);
        Assert.Equal("Nature", villageRoom.Category);
        Assert.Equal(villageRoom.Id, catalog.GetDistrictRoomTemplate(villageDistrictId, roomCount).Id);
        var boundFacility = catalog.GetDistrictFacilityTemplate(villageDistrictId, villageRoom.Id, 0);
        Assert.NotNull(boundFacility);
        Assert.Contains(villageRoom.Id, boundFacility!.RoomIds);

        var generatedVillage = PoiAssemblyGenerator.GenerateForPoi(WorldPoiType.Village, seed: 789);
        var roomsWithFacilities = generatedVillage.AllRooms.Where(room => room.FacilityIds.Count > 0).ToArray();
        Assert.NotEmpty(roomsWithFacilities);
        foreach (var room in roomsWithFacilities)
        {
            Assert.NotNull(room.RoomTemplateId);
            Assert.All(room.FacilityIds, id =>
                Assert.Contains(room.RoomTemplateId!.Value, catalog.GetFacilityById(id).RoomIds));
        }
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
