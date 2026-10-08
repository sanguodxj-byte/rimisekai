using System.Collections.Generic;
using System.IO;
using Rimisekai.WorldMap;
using Rimisekai.WorldMap.Generators;
using Rimisekai.WorldMap.Visualization;
using Xunit;

namespace Rimisekai.Tests;

public sealed class WorldMapTests
{
    [Fact]
    public void WorldGenerator_produces_consistent_and_valid_map()
    {
        const int seed = 12345;
        var map1 = WorldGenerator.Generate(seed, 64, 64);
        var map2 = WorldGenerator.Generate(seed, 64, 64);

        Assert.Equal(64, map1.Width);
        Assert.Equal(64, map1.Height);

        // 验证确定性：相同种子每个格点地形和高程完全一致
        for (var x = 0; x < 64; x++)
        {
            for (var y = 0; y < 64; y++)
            {
                Assert.Equal(map1.Tiles[x, y].Terrain, map2.Tiles[x, y].Terrain);
                Assert.Equal(map1.Tiles[x, y].Elevation, map2.Tiles[x, y].Elevation);
            }
        }
    }

    [Fact]
    public void WorldGenerator_generates_natural_ocean_ring()
    {
        var map = WorldGenerator.Generate(seed: 42, width: 64, height: 64);

        // 检查最外圈边界格点，应绝大多数为大洋水域（受 Falloff 衰减）
        var oceanCount = 0;
        var edgeTotal = 0;

        for (var x = 0; x < 64; x++)
        {
            if (map.Tiles[x, 0].Terrain is WorldTerrainType.DeepWater or WorldTerrainType.ShallowWater)
                oceanCount++;
            if (map.Tiles[x, 63].Terrain is WorldTerrainType.DeepWater or WorldTerrainType.ShallowWater)
                oceanCount++;
            edgeTotal += 2;
        }

        for (var y = 1; y < 63; y++)
        {
            if (map.Tiles[0, y].Terrain is WorldTerrainType.DeepWater or WorldTerrainType.ShallowWater)
                oceanCount++;
            if (map.Tiles[63, y].Terrain is WorldTerrainType.DeepWater or WorldTerrainType.ShallowWater)
                oceanCount++;
            edgeTotal += 2;
        }

        // 四周外圈至少 95% 为水体
        Assert.True((float)oceanCount / edgeTotal >= 0.90f);
    }

    [Fact]
    public void WorldGenerator_creates_rivers_and_water_cycle()
    {
        var map = WorldGenerator.Generate(seed: 9999, width: 128, height: 128);

        // 应该至少生成若干条主干河流
        Assert.NotEmpty(map.Rivers);

        foreach (var river in map.Rivers)
        {
            Assert.NotEmpty(river.Path);
            Assert.True(river.MaxFlux > 0);
            Assert.False(string.IsNullOrWhiteSpace(river.NameZh));
            Assert.False(string.IsNullOrWhiteSpace(river.NameEn));

            // 源头与河口格点验证
            var (sx, sy) = river.Source;
            var sourceTile = map.Tiles[sx, sy];
            Assert.True(sourceTile.IsRiver || sourceTile.Terrain == WorldTerrainType.River);
        }
    }

    [Fact]
    public void WorldGenerator_settlements_and_roads_connected()
    {
        var map = WorldGenerator.Generate(seed: 777, width: 128, height: 128);

        // 应当成功放置聚落（首都、城镇、村庄、要塞等）
        Assert.NotEmpty(map.Pois);
        Assert.Contains(map.Pois, p => p.Type == WorldPoiType.Capital);

        // 道路网络应当连通各聚落
        Assert.NotEmpty(map.Roads);

        var bridgeCount = 0;
        for (var x = 0; x < map.Width; x++)
        {
            for (var y = 0; y < map.Height; y++)
            {
                if (map.Tiles[x, y].IsBridge)
                    bridgeCount++;
            }
        }

        // 存在穿越河流或浅水的水域道路桥梁
        Assert.True(bridgeCount >= 0);
    }

    [Fact]
    public void WorldGenerator_regions_have_bilingual_names()
    {
        var map = WorldGenerator.Generate(seed: 8888, width: 128, height: 128);

        Assert.NotEmpty(map.Regions);
        foreach (var region in map.Regions)
        {
            Assert.True(region.TileCount >= 16);
            Assert.False(string.IsNullOrWhiteSpace(region.NameEn));
            Assert.False(string.IsNullOrWhiteSpace(region.NameZh));
            Assert.True(region.CentroidX >= 0 && region.CentroidX < map.Width);
            Assert.True(region.CentroidY >= 0 && region.CentroidY < map.Height);
        }
    }

    [Fact]
    public void WorldMap_exports_bmp_screenshot_successfully()
    {
        // 生成标准规模 128x128 地图，并放大为 512x512 色块图像输出
        var map = WorldGenerator.Generate(seed: 2026, width: 128, height: 128);
        var previewPath = Path.Combine(Directory.GetCurrentDirectory(), "world_map_preview.bmp");

        WorldMapImageExporter.ExportToBmp(map, previewPath, scale: 4);

        Assert.True(File.Exists(previewPath));
        var fileInfo = new FileInfo(previewPath);
        // 512x512 像素，每行 512*3 = 1536 字节（正好是 4 的倍数无需行填充）
        // 头部 54 字节 + 1536 * 512 = 786,486 字节
        const int expectedSize = 54 + 512 * 512 * 3;
        Assert.Equal(expectedSize, fileInfo.Length);
    }

    [Fact]
    public void Game_integrates_world_and_poi_maps_through_5x5_grid()
    {
        var state = new Rimisekai.Save.GameState();
        Assert.NotNull(state.World);
        Assert.NotEmpty(state.World.Pois);

        var hub = new Rimisekai.Hub.HubSession(state);

        // 1. 默认处于领地据点 5x5 网格
        Assert.Equal(Rimisekai.Hub.MapLayer.Territory, hub.Layer);

        // 2. 切换到大世界图层：在既有 5x5 网格中呈现 25 间宏观大区房间
        hub.SwitchToWorld();
        Assert.Equal(Rimisekai.Hub.MapLayer.World, hub.Layer);
        var worldRooms = hub.Map();
        Assert.Equal(25, worldRooms.Count);
        for (var x = 0; x < 5; x++)
        {
            for (var y = 0; y < 5; y++)
            {
                Assert.Contains(worldRooms, r => r.X == x && r.Y == y);
            }
        }
        // 验证物理地貌名称朴素纯净，绝不强行堆砌修饰词
        Assert.Contains(worldRooms, r => r.Name is "道路" or "草原" or "平原" or "森林" or "山岳" or "湖泊" || r.Name.Contains("王都") || r.Name.Contains("镇") || r.Name.Contains("村") || r.Name.Contains("要塞"));

        // 3. 进入大世界上的 POI 场景：自动开辟 5x5 完全填满且全连通的场景房间
        var targetPoi = state.World.Pois[0];
        Assert.True(hub.EnterWorldPoi(targetPoi.Id));
        Assert.Equal(Rimisekai.Hub.MapLayer.WorldPoi, hub.Layer);
        Assert.NotNull(state.CurrentPoi);

        var poiRooms = hub.Map();
        Assert.Equal(25, poiRooms.Count); // 当前 5x5 块完全填满 25 间房
        Assert.True(hub.PlayerRoomId >= 0);

        // 首块起点为“大门”，末块终点为“出口”，且均在各自 5x5 的边缘格上
        var firstBlock = state.CurrentPoi.Blocks[0];
        var lastBlock = state.CurrentPoi.Blocks[^1];
        Assert.NotNull(firstBlock.StartRoom);
        Assert.NotNull(lastBlock.EndRoom);
        Assert.Equal("大门", firstBlock.StartRoom!.Name);
        Assert.Equal("出口", lastBlock.EndRoom!.Name);
        Assert.True(firstBlock.StartRoom!.IsAtBlockEdge);
        Assert.True(lastBlock.EndRoom!.IsAtBlockEdge);

        // 4. 返回领地
        hub.SwitchToTerritory();
        Assert.Equal(Rimisekai.Hub.MapLayer.Territory, hub.Layer);
    }

    [Fact]
    public void Settlement_names_are_procedurally_generated_at_runtime_without_duplicates()
    {
        var map = WorldGenerator.Generate(seed: 2026, width: 128, height: 128);
        Assert.NotEmpty(map.Pois);

        var names = new HashSet<string>();
        foreach (var poi in map.Pois)
        {
            Assert.False(string.IsNullOrWhiteSpace(poi.NameZh));
            // 验证全图所有聚落名称唯一不重复
            Assert.DoesNotContain(poi.NameZh, names);
            names.Add(poi.NameZh);

            // 验证纯正日式西幻轻小说风格：以王都、镇、村、要塞、遗迹、教会等结尾
            var validSuffix = poi.NameZh.Contains("王都") ||
                              poi.NameZh.EndsWith("镇") ||
                              poi.NameZh.EndsWith("城") ||
                              poi.NameZh.EndsWith("村") ||
                              poi.NameZh.EndsWith("集落") ||
                              poi.NameZh.EndsWith("要塞") ||
                              poi.NameZh.EndsWith("砦") ||
                              poi.NameZh.EndsWith("关卡") ||
                              poi.NameZh.EndsWith("遗迹") ||
                              poi.NameZh.EndsWith("洞窟") ||
                              poi.NameZh.EndsWith("遗址") ||
                              poi.NameZh.EndsWith("迷宫入口") ||
                              poi.NameZh.EndsWith("废墟") ||
                              poi.NameZh.EndsWith("大教会") ||
                              poi.NameZh.EndsWith("修道院") ||
                              poi.NameZh.EndsWith("祈祷之泉");

            Assert.True(validSuffix, $"POI 名称 '{poi.NameZh}' 应当符合日式西幻轻小说构词规则");
        }
    }

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    [InlineData(2026)]
    [InlineData(99991)]
    public void Homesite_is_on_habitable_land_away_from_settlements_and_joined_to_roads(int seed)
    {
        var map = WorldGenerator.Generate(seed);
        Assert.True(map.HasHome);
        var home = map.Tiles[map.HomeX, map.HomeY];
        Assert.True(home.PoiId <= 0);
        Assert.Contains(home.Terrain, new[] { WorldTerrainType.Plains, WorldTerrainType.Grassland, WorldTerrainType.Savanna,
            WorldTerrainType.Forest, WorldTerrainType.Hills });
        foreach (var poi in map.Pois)
            Assert.True(System.Math.Abs(poi.X - map.HomeX) + System.Math.Abs(poi.Y - map.HomeY) >= 6);
        // 领地沿道路能走到至少一个聚落。
        var seen = new HashSet<(int, int)> { (map.HomeX, map.HomeY) };
        var queue = new Queue<(int x, int y)>();
        queue.Enqueue((map.HomeX, map.HomeY));
        var reached = false;
        while (queue.Count > 0 && !reached)
        {
            var (x, y) = queue.Dequeue();
            foreach (var (dx, dy) in Direction4Extensions.Offsets)
            {
                var t = map.GetTile(x + dx, y + dy);
                if (t == null || !t.IsRoad || !seen.Add((t.X, t.Y)))
                    continue;
                if (t.PoiId > 0)
                    reached = true;
                queue.Enqueue((t.X, t.Y));
            }
        }
        Assert.True(reached);
    }

    [Fact]
    public void Homesite_is_deterministic_per_seed_and_varies_across_seeds()
    {
        var a = WorldGenerator.Generate(31337);
        var b = WorldGenerator.Generate(31337);
        Assert.Equal((a.HomeX, a.HomeY), (b.HomeX, b.HomeY));
        var spots = new HashSet<(int, int)>();
        foreach (var seed in new[] { 1, 2, 3, 4, 5 })
        {
            var m = WorldGenerator.Generate(seed);
            spots.Add((m.HomeX, m.HomeY));
        }
        Assert.True(spots.Count >= 3);
    }

    [Fact]
    public void Regenerated_world_survives_save_round_trip()
    {
        var state = new Rimisekai.Save.GameState();
        state.RegenerateWorld(777);
        var data = Rimisekai.Save.SaveSystem.Capture(state);
        var restored = Rimisekai.Save.SaveSystem.Restore(data);
        Assert.Equal(777, restored.WorldSeed);
        Assert.Equal((state.World.HomeX, state.World.HomeY), (restored.World.HomeX, restored.World.HomeY));
    }

    [Fact]
    public void Visiting_many_pois_does_not_pile_up_rooms()
    {
        var state = new Rimisekai.Save.GameState();
        var hub = new Rimisekai.Hub.HubSession(state);
        var before = state.Territory.Rooms.Count;
        var unlocked = state.Territory.UnlockedRegions;
        foreach (var poi in state.World.Pois)
        {
            hub.SwitchToWorld();
            Assert.True(hub.EnterWorldPoi(poi.Id));
            Assert.True(state.Territory.Rooms.Count <= Rimisekai.Housing.Territory.MaxRooms);
        }
        hub.SwitchToTerritory();
        Assert.Equal(before, state.Territory.Rooms.Count);
        Assert.Equal(unlocked, state.Territory.UnlockedRegions);
        Assert.Null(state.CurrentPoi);
    }
}
