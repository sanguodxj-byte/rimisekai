using System.Linq;
using System.Collections.Generic;
using System.IO;
using Rimisekai.PoiMap;
using Rimisekai.PoiMap.Generator;
using Rimisekai.PoiMap.Visualization;
using Xunit;

namespace Rimisekai.Tests;

public sealed class PoiMapTests
{
    [Fact]
    public void PoiBlock_generates_connected_rooms_and_end_on_edge()
    {
        for (var seed = 100; seed < 110; seed++)
        {
            var map = PoiAssemblyGenerator.GenerateSingle(seed: seed);
            Assert.Single(map.Blocks);
            var block = map.Blocks[0];

            // 核心契约 1：5x5 内部必须全部填满（严格 25 间房，0 空置格）
            Assert.Equal(25, block.Rooms.Count);
            for (var x = 0; x < 5; x++)
            {
                for (var y = 0; y < 5; y++)
                {
                    Assert.NotNull(block.Grid[x, y]);
                }
            }

            Assert.NotNull(block.StartRoom);
            Assert.NotNull(block.EndRoom);

            // 核心契约 2a：起点房间也必须严格在 5x5 的边缘格，且朴实命名为“大门”
            var start = block.StartRoom!;
            Assert.True(
                start.LocalX == 0 || start.LocalX == 4 || start.LocalY == 0 || start.LocalY == 4,
                $"Seed {seed}: Start room at ({start.LocalX}, {start.LocalY}) is not on 5x5 edge!");
            Assert.Equal("大门", start.Name);

            // 核心契约 2b：终点房间必须严格在 5x5 的边缘格，且与起点不重合，朴实命名为“出口”
            var end = block.EndRoom!;
            Assert.True(
                end.LocalX == 0 || end.LocalX == 4 || end.LocalY == 0 || end.LocalY == 4,
                $"Seed {seed}: End room at ({end.LocalX}, {end.LocalY}) is not on 5x5 edge!");
            Assert.NotEqual(start.Id, end.Id);
            Assert.Equal("出口", end.Name);

            // 核心契约 3：5x5 内部完全联通，从起点可到达块内全部 25 间房
            var reachable = GetReachableRoomIds(map, block.StartRoom!.Id);
            Assert.Equal(25, reachable.Count);

            // 核心契约 4：根据生成 ID 取索引
            foreach (var r in block.Rooms)
            {
                var expectedIndex = r.LocalY * 5 + r.LocalX;
                Assert.Equal(expectedIndex, r.CellIndex);
                Assert.Equal(expectedIndex, map.GetCellIndex(r.Id));
                Assert.Same(r, map.GetRoom(r.Id));
                Assert.Same(block, map.GetBlockByRoomId(r.Id));
            }
        }
    }

    [Fact]
    public void PoiAssembly_connects_adjacent_blocks_with_boundary_channels()
    {
        // 测试 1x2 水平双块拼合 (共 2 个 5x5 块，全部填满共 50 间房)
        for (var seed = 200; seed < 205; seed++)
        {
            var map = PoiAssemblyGenerator.GenerateGrid(seed, blocksW: 2, blocksH: 1);
            Assert.Equal(2, map.Blocks.Count);
            Assert.Equal(50, map.AllRooms.Count);

            // 契约验证：两个相邻 5x5 块之间至少有一条边界通道互相连通
            Assert.NotEmpty(map.BoundaryLinks);

            // 验证全图连通性：从起点（位于 Block 0）能够到达全图 50 间房（包括 Block 1 的终点）
            var startId = map.Blocks[0].StartRoom!.Id;
            var reachable = GetReachableRoomIds(map, startId);
            Assert.Equal(50, reachable.Count);

            // 最后一个块的终点必然可达且在边缘
            var endRoom = map.Blocks[1].EndRoom;
            Assert.NotNull(endRoom);
            Assert.True(endRoom!.IsAtBlockEdge);
            Assert.Contains(endRoom.Id, reachable);
        }
    }

    [Fact]
    public void PoiAssembly_2x2_grid_connects_all_four_blocks()
    {
        // 测试 2x2 拼合 4 个 5x5 块 (4 * 25 = 100 间房完全填满)
        var map = PoiAssemblyGenerator.GenerateGrid(seed: 42, blocksW: 2, blocksH: 2);
        Assert.Equal(4, map.Blocks.Count);
        Assert.Equal(100, map.AllRooms.Count);

        // 至少有 4 条边界连通线
        Assert.True(map.BoundaryLinks.Count >= 4);

        // 全图 100 间房完全连通
        var startId = map.Blocks[0].StartRoom!.Id;
        var reachable = GetReachableRoomIds(map, startId);
        Assert.Equal(100, reachable.Count);

        // 验证导出为据点系统 Room 结构完全兼容
        var housingRooms = map.ExportToHousingRooms();
        Assert.Equal(100, housingRooms.Count);
        foreach (var hr in housingRooms)
        {
            Assert.NotEmpty(hr.Links);
            Assert.True(hr.X >= 0 && hr.Y >= 0);
        }
    }

    [Fact]
    public void PoiAssembly_generates_by_scale_with_districts_and_no_stuffing()
    {
        // 1. 王都：按数据表规模自动拼合为 2x2 四个 5x5 块 (100格)
        var capital = PoiAssemblyGenerator.GenerateForPoi(Rimisekai.WorldMap.WorldPoiType.Capital, seed: 123);
        Assert.Equal(4, capital.Blocks.Count);
        Assert.Equal(100, capital.AllRooms.Count);
        Assert.True(capital.BoundaryLinks.Count >= 4);

        // 验证起点为大门且在首块边缘；终点为出口且在末块边缘
        Assert.Equal("大门", capital.Blocks[0].StartRoom!.Name);
        Assert.True(capital.Blocks[0].StartRoom!.IsAtBlockEdge);
        Assert.Equal("出口", capital.Blocks[^1].EndRoom!.Name);
        Assert.True(capital.Blocks[^1].EndRoom!.IsAtBlockEdge);

        // 验证全图 100 间房完全联通
        var reachableCap = GetReachableRoomIds(capital, capital.Blocks[0].StartRoom!.Id);
        Assert.Equal(100, reachableCap.Count);

        // 验证自然地貌舒展：道路与草地占有相当比例，绝非强塞满单一功能设施
        var natureOrRoadCount = 0;
        foreach (var r in capital.AllRooms)
        {
            if (r.Terrain == Rimisekai.WorldMap.WorldTerrainType.Road ||
                r.Terrain == Rimisekai.WorldMap.WorldTerrainType.Grassland ||
                r.Terrain == Rimisekai.WorldMap.WorldTerrainType.Forest ||
                r.Name is "道路" or "街道" or "草地" or "庭院" or "中央广场" or "大门" or "出口")
            {
                natureOrRoadCount++;
            }
        }
        Assert.True(natureOrRoadCount >= 40, "各 5x5 块应有足够的道路与开阔绿地留白，避免强塞满设施");

        // 2. 城镇：自动拼合为 2x1 两个 5x5 块 (50格)
        var town = PoiAssemblyGenerator.GenerateForPoi(Rimisekai.WorldMap.WorldPoiType.Town, seed: 456);
        Assert.Equal(2, town.Blocks.Count);
        Assert.Equal(50, town.AllRooms.Count);
        Assert.NotEmpty(town.BoundaryLinks);
        Assert.Equal(50, GetReachableRoomIds(town, town.Blocks[0].StartRoom!.Id).Count);

        // 3. 村庄：自动匹配为 1x1 单个 5x5 块 (25格)
        var village = PoiAssemblyGenerator.GenerateForPoi(Rimisekai.WorldMap.WorldPoiType.Village, seed: 789);
        Assert.Single(village.Blocks);
        Assert.Equal(25, village.AllRooms.Count);
        Assert.Equal(25, GetReachableRoomIds(village, village.Blocks[0].StartRoom!.Id).Count);
    }

    [Fact]
    public void Dungeon_generates_labyrinth_structure_with_treasures()
    {
        // 遗迹/洞窟地下城：应采用封闭式地牢迷宫生成策略，包含藏宝库或神龛
        var ruin = PoiAssemblyGenerator.GenerateForPoi(Rimisekai.WorldMap.WorldPoiType.Ruin, seed: 999);
        Assert.Single(ruin.Blocks);
        Assert.Equal(25, ruin.AllRooms.Count);

        // 验证起终点在边缘
        Assert.Equal("大门", ruin.Blocks[0].StartRoom!.Name);
        Assert.True(ruin.Blocks[0].StartRoom!.IsAtBlockEdge);
        Assert.Equal("出口", ruin.Blocks[0].EndRoom!.Name);
        Assert.True(ruin.Blocks[0].EndRoom!.IsAtBlockEdge);

        // 验证全连通
        Assert.Equal(25, GetReachableRoomIds(ruin, ruin.Blocks[0].StartRoom!.Id).Count);

        // 验证地牢具备分支末梢宝藏或神龛特性
        var hasTreasureOrShrine = false;
        foreach (var r in ruin.AllRooms)
        {
            if (r.RoomType is PoiRoomType.Treasure or PoiRoomType.Shrine || r.Name is "藏宝库" or "神龛" or "破败祭坛")
            {
                hasTreasureOrShrine = true;
                break;
            }
        }
        Assert.True(hasTreasureOrShrine, "地牢应包含藏宝库、神龛或破败祭坛等探索要素");
    }

    [Fact]
    public void PoiMap_exports_preview_screenshots_successfully()
    {
        // 1. 导出开放式村庄聚落 (Village 1x1：主道贯穿，草地田园自然开阔，非迷宫)
        var village = PoiAssemblyGenerator.GenerateForPoi(Rimisekai.WorldMap.WorldPoiType.Village, seed: 111);
        var villageBmp = Path.Combine(Directory.GetCurrentDirectory(), "poi_village_settlement.bmp");
        PoiMapImageExporter.ExportToBmp(village, villageBmp, cellSize: 48);
        Assert.True(File.Exists(villageBmp));

        // 2. 导出开放式王都大聚落 (Capital 2x2：十字大街，中央广场，分区自然舒展)
        var capital = PoiAssemblyGenerator.GenerateForPoi(Rimisekai.WorldMap.WorldPoiType.Capital, seed: 222);
        var capitalBmp = Path.Combine(Directory.GetCurrentDirectory(), "poi_capital_settlement.bmp");
        PoiMapImageExporter.ExportToBmp(capital, capitalBmp, cellSize: 48);
        Assert.True(File.Exists(capitalBmp));

        // 3. 导出封闭式地下遗迹 (Ruin 1x1：密室走廊，分支藏宝库，探索地下城)
        var ruin = PoiAssemblyGenerator.GenerateForPoi(Rimisekai.WorldMap.WorldPoiType.Ruin, seed: 333);
        var ruinBmp = Path.Combine(Directory.GetCurrentDirectory(), "poi_ruin_dungeon.bmp");
        PoiMapImageExporter.ExportToBmp(ruin, ruinBmp, cellSize: 48);
        Assert.True(File.Exists(ruinBmp));
    }

    private static HashSet<int> GetReachableRoomIds(PoiMapData map, int startRoomId)
    {
        var visited = new HashSet<int>();
        var queue = new Queue<int>();

        visited.Add(startRoomId);
        queue.Enqueue(startRoomId);

        while (queue.Count > 0)
        {
            var curId = queue.Dequeue();
            var room = map.GetRoom(curId);
            if (room == null)
                continue;

            foreach (var nextId in room.Links)
            {
                if (visited.Add(nextId))
                {
                    queue.Enqueue(nextId);
                }
            }
        }

        return visited;
    }
}
