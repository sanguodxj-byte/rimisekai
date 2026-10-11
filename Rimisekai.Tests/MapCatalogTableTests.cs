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
    public void World_map_room_facility_poi_and_dungeon_tables_are_separate_and_referenced_by_id()
    {
        var content = FindContentDirectory();
        using var map = JsonDocument.Parse(File.ReadAllText(Path.Combine(content, "map_defs.json")));
        using var rooms = JsonDocument.Parse(File.ReadAllText(Path.Combine(content, "room_defs.json")));
        using var facilities = JsonDocument.Parse(File.ReadAllText(Path.Combine(content, "facility_defs.json")));
        using var pois = JsonDocument.Parse(File.ReadAllText(Path.Combine(content, "poi_defs.json")));
        using var dungeonDefs = JsonDocument.Parse(File.ReadAllText(Path.Combine(content, "dungeon_defs.json")));

        Assert.Equal(
            new[] { "terrains", "wilds" },
            map.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));

        var roomIds = rooms.RootElement.GetProperty("rooms").EnumerateArray()
            .Select(row => row.GetProperty("id").GetInt32()).ToHashSet();
        var facilityRows = facilities.RootElement.GetProperty("facilities").EnumerateArray()
            .ToDictionary(row => row.GetProperty("id").GetInt32());
        var facilityIds = facilityRows.Keys.ToHashSet();
        var districts = pois.RootElement.GetProperty("districts").EnumerateArray().ToArray();
        var districtIds = districts.Select(district => district.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);
        var dungeonRows = dungeonDefs.RootElement.GetProperty("dungeons").EnumerateArray().ToArray();
        var dungeonRowsById = dungeonRows.ToDictionary(row => row.GetProperty("id").GetString()!, StringComparer.Ordinal);
        var dungeonIds = dungeonRowsById.Keys.ToHashSet(StringComparer.Ordinal);

        Assert.Equal(rooms.RootElement.GetProperty("rooms").GetArrayLength(), roomIds.Count);
        Assert.Equal(facilities.RootElement.GetProperty("facilities").GetArrayLength(), facilityIds.Count);
        Assert.Equal(dungeonRows.Length, dungeonIds.Count);
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
        foreach (var dungeon in dungeonRows)
        {
            var instanceRooms = dungeon.GetProperty("roomIds").EnumerateArray().Select(id => id.GetInt32()).ToHashSet();
            Assert.NotEmpty(instanceRooms);
            Assert.All(instanceRooms, id => Assert.Contains(id, roomIds));
            foreach (var facilityId in dungeon.GetProperty("facilityIds").EnumerateArray().Select(id => id.GetInt32()))
            {
                Assert.Contains(facilityId, facilityIds);
                var facilityRoomIds = facilityRows[facilityId].GetProperty("roomIds").EnumerateArray().Select(id => id.GetInt32());
                Assert.Contains(facilityRoomIds, instanceRooms.Contains);
            }
        }
        foreach (var poi in pois.RootElement.GetProperty("pois").EnumerateArray())
        {
            if (poi.TryGetProperty("districts", out var poiDistricts))
                Assert.All(poiDistricts.EnumerateArray(), id => Assert.Contains(id.GetString()!, districtIds));
            if (poi.TryGetProperty("dungeonId", out var dungeonId))
                Assert.Contains(dungeonId.GetString()!, dungeonIds);
        }
        var sites = dungeonDefs.RootElement.GetProperty("rules").GetProperty("quest")
            .GetProperty("board").GetProperty("sites").EnumerateArray();
        Assert.All(sites, site => Assert.Contains(site.GetProperty("dungeonId").GetString()!, dungeonIds));
        using var questDefs = JsonDocument.Parse(File.ReadAllText(Path.Combine(content, "defs", "quests.json")));
        foreach (var quest in questDefs.RootElement.GetProperty("defs").EnumerateArray())
            if (quest.TryGetProperty("kind", out var kind) && kind.GetString() == "dungeon")
                Assert.Contains(quest.GetProperty("dungeonId").GetString()!, dungeonIds);
        Assert.Contains("forest", dungeonIds);
        Assert.Contains("ancient_battlefield", dungeonIds);
        Assert.Contains("mine", dungeonIds);

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

    [Fact]
    public void Roguelike_archetypes_generate_from_their_own_room_id_indexes()
    {
        var catalog = MapCatalog.Default;
        foreach (var dungeonId in new[] { "ruins", "forest", "ancient_battlefield", "mine" })
        {
            var archetype = catalog.GetDungeonTemplate(dungeonId);
            Assert.Equal(archetype.RoomIds[0], catalog.GetDungeonRoomTemplate(dungeonId, 0).Id);
            Assert.Equal(archetype.RoomIds[0], catalog.GetDungeonRoomTemplate(dungeonId, archetype.RoomIds.Count).Id);

            var map = PoiAssemblyGenerator.GenerateDungeon(dungeonId, seed: 0);
            Assert.Equal(25, map.AllRooms.Count);
            var themedRooms = map.AllRooms.Where(room => room.RoomTemplateId.HasValue).ToArray();
            Assert.Equal(23, themedRooms.Length);
            Assert.All(themedRooms, room => Assert.Contains(room.RoomTemplateId!.Value, archetype.RoomIds));
        }

        Assert.Equal("ruins", catalog.GetPoiDungeonId(WorldPoiType.Ruin));
    }

    [Fact]
    public void Dungeon_encounter_pools_use_the_highest_reachable_tier_and_exact_weights()
    {
        var dungeon = MapCatalog.Default.Dungeon;
        for (var tier = 1; tier <= 4; tier++)
        {
            Assert.True(dungeon.Guards.Count(guard => guard.MinTier == tier) >= 2,
                $"危险级 {tier} 至少要有两组不同守卫");
            CheckPool(dungeon.Guards, dungeon.GuardAtTier, tier);
            CheckPool(dungeon.Bosses, dungeon.BossAtTier, tier);
        }
    }

    [Fact]
    public void Dungeon_noncombat_events_are_bound_to_facilities_and_chests_have_loot_rows()
    {
        var catalog = MapCatalog.Default;
        var chest = catalog.GetFacilityById(1045);
        Assert.Equal("封存宝箱", chest.Name);
        Assert.NotNull(chest.DungeonEvent);
        Assert.NotEmpty(chest.DungeonEvent!.Loot);
        Assert.Empty(chest.DungeonEvent.Foes);

        var ruin = PoiAssemblyGenerator.GenerateForPoi(WorldPoiType.Ruin, seed: 999);
        Assert.Contains(ruin.AllRooms, room => room.Name == "储藏间"
            && room.FacilityIds.Select(catalog.GetFacilityById).Any(facility => facility.DungeonEvent != null));
        Assert.All(catalog.Dungeon.Guards.Concat(catalog.Dungeon.Bosses), encounter => Assert.NotEmpty(encounter.Foes));
    }

    private static void CheckPool(
        System.Collections.Generic.List<EncounterDef> entries,
        Func<int, uint, EncounterDef> pick,
        int tier)
    {
        var eligible = entries.Where(entry => entry.MinTier <= tier).ToArray();
        var highestTier = eligible.Max(entry => entry.MinTier);
        var expected = eligible.Where(entry => entry.MinTier == highestTier).ToArray();
        Assert.All(expected, entry => Assert.True(entry.Weight > 0, $"{entry.Id} 的权重必须为正"));
        var totalWeight = expected.Sum(entry => entry.Weight);
        var selected = Enumerable.Range(0, totalWeight).Select(roll => pick(tier, (uint)roll)).ToArray();
        Assert.All(selected, entry => Assert.Equal(highestTier, entry.MinTier));
        foreach (var entry in expected)
            Assert.Equal(entry.Weight, selected.Count(selectedEntry => ReferenceEquals(selectedEntry, entry)));
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
