using System.Collections.Generic;
using Rimisekai.WorldMap.Generators;

namespace Rimisekai.WorldMap;

/// <summary>
/// 宏观地质地理区域（由同生态连通分量泛洪聚类生成）。
/// </summary>
public sealed class WorldRegion
{
    public int Id { get; init; }
    public string NameEn { get; set; } = "";
    public string NameZh { get; set; } = "";
    public WorldBiomeType Biome { get; init; }
    public WorldTerrainType DominantTerrain { get; init; }

    /// <summary>区域质心 X</summary>
    public int CentroidX { get; set; }

    /// <summary>区域质心 Y</summary>
    public int CentroidY { get; set; }

    /// <summary>格点数</summary>
    public int TileCount { get; set; }

    /// <summary>平均海拔</summary>
    public float AverageElevation { get; set; }

    /// <summary>平均湿度</summary>
    public float AverageMoisture { get; set; }

    /// <summary>平均温度</summary>
    public float AverageTemperature { get; set; }
}

/// <summary>
/// 河流水系实体。
/// </summary>
public sealed class WorldRiver
{
    public int Id { get; init; }
    public string NameEn { get; set; } = "";
    public string NameZh { get; set; } = "";
    public (int x, int y) Source { get; set; }
    public (int x, int y) Mouth { get; set; }
    public List<(int x, int y)> Path { get; } = new();
    public float MaxFlux { get; set; }
    public bool FlowsIntoWater { get; set; }
    public bool IsConfluence { get; set; }
}

/// <summary>
/// 道路连接段。
/// </summary>
public sealed class WorldRoad
{
    public int Id { get; init; }
    public string NameEn { get; set; } = "";
    public string NameZh { get; set; } = "";
    public int FromPoiId { get; init; }
    public int ToPoiId { get; init; }
    public int RoadClass { get; init; } = 1;
    public List<(int x, int y)> Path { get; } = new();
}

/// <summary>
/// 聚落与兴趣点（POI）类型。
/// </summary>
public enum WorldPoiType
{
    Capital = 0,
    Town = 1,
    Castle = 2,
    Village = 3,
    Fortress = 4,
    Monastery = 5,
    Ruin = 6,
}

/// <summary>
/// 聚落与兴趣点实体。
/// </summary>
public sealed class WorldPoi
{
    public int Id { get; init; }
    public string NameEn { get; set; } = "";
    public string NameZh { get; set; } = "";
    public WorldPoiType Type { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int RegionId { get; set; } = -1;
}

/// <summary>
/// 整个世界地图的聚合根模型。
/// </summary>
public sealed class WorldMapData
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int Seed { get; init; }

    public WorldTile[,] Tiles { get; }
    public List<WorldRegion> Regions { get; } = new();
    public List<WorldRiver> Rivers { get; } = new();
    public List<WorldRoad> Roads { get; } = new();
    public List<WorldPoi> Pois { get; } = new();

    /// <summary>玩家领地在大世界上的格点（由 <see cref="Generators.HomesiteStage"/> 按种子选定）。</summary>
    public int HomeX { get; private set; } = -1;

    public int HomeY { get; private set; } = -1;

    public bool HasHome => HomeX >= 0 && HomeY >= 0;

    /// <summary>定下领地格点。</summary>
    public void SetHome(int x, int y)
    {
        HomeX = x;
        HomeY = y;
    }

    /// <summary>格点所在的地理区域（无则 null）。</summary>
    public WorldRegion? RegionAt(int x, int y)
    {
        var tile = GetTile(x, y);
        return tile == null || tile.RegionId < 0 ? null : Regions.Find(r => r.Id == tile.RegionId);
    }

    /// <summary>格点上的聚落（无则 null）。</summary>
    public WorldPoi? PoiAt(int x, int y)
    {
        var tile = GetTile(x, y);
        return tile == null || tile.PoiId <= 0 ? null : Pois.Find(p => p.Id == tile.PoiId);
    }

    /// <summary>格点的朴素地貌名：聚落名 &gt; 道路 &gt; 河流 &gt; 地形名。</summary>
    public string TileName(int x, int y)
    {
        var tile = GetTile(x, y);
        return tile == null ? "" : ResolveTilePhysicalName(tile, x, y);
    }

    public WorldMapData(int width, int height, int seed)
    {
        Width = width;
        Height = height;
        Seed = seed;
        Tiles = new WorldTile[width, height];
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                Tiles[x, y] = new WorldTile(x, y);
            }
        }
    }

    public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

    public WorldTile? GetTile(int x, int y) => InBounds(x, y) ? Tiles[x, y] : null;

    public int IndexOf(int x, int y) => y * Width + x;

    public (int x, int y) CoordOf(int index) => (index % Width, index / Width);

    /// <summary>
    /// 踏进这一格要花的分钟数：有路走路（桥也算路），否则按地貌查 map_defs.json 的 travel；0 = 不可通行。
    /// </summary>
    public int TravelMinutes(int x, int y)
    {
        var tile = Tiles[x, y];
        var catalog = MapCatalog.Default;
        if (tile.IsRoad)
            return catalog.GetTravelMinutes(WorldTerrainType.Road);
        if (tile.PoiId > 0 || (x == HomeX && y == HomeY))
            return catalog.GetTravelMinutes(WorldTerrainType.Plains);
        if (tile.IsRiver)
            return catalog.GetTravelMinutes(WorldTerrainType.River);
        return catalog.GetTravelMinutes(tile.Terrain);
    }

    public bool IsPassable(int x, int y) => InBounds(x, y) && TravelMinutes(x, y) > 0;

    /// <summary>
    /// 两格之间的最省时路线（Dijkstra，四向，代价＝踏进下一格的分钟数）。
    /// 返回不含起点、含终点的格序列；走不到返回 null，原地返回空表。
    /// </summary>
    public List<(int x, int y)>? FindRoute(int fromX, int fromY, int toX, int toY)
    {
        if (!InBounds(toX, toY) || !IsPassable(toX, toY))
            return null;
        var route = new List<(int x, int y)>();
        if (fromX == toX && fromY == toY)
            return route;

        var size = Width * Height;
        var cost = new int[size];
        var prev = new int[size];
        System.Array.Fill(cost, int.MaxValue);
        System.Array.Fill(prev, -1);
        var start = IndexOf(fromX, fromY);
        var goal = IndexOf(toX, toY);
        cost[start] = 0;
        var queue = new PriorityQueue<int, int>();
        queue.Enqueue(start, 0);
        while (queue.TryDequeue(out var at, out var spent))
        {
            if (spent > cost[at])
                continue;
            if (at == goal)
                break;
            var (ax, ay) = CoordOf(at);
            foreach (var (dx, dy) in Direction4Extensions.Offsets)
            {
                var nx = ax + dx;
                var ny = ay + dy;
                if (!InBounds(nx, ny))
                    continue;
                var step = TravelMinutes(nx, ny);
                if (step <= 0)
                    continue;
                var next = IndexOf(nx, ny);
                var total = spent + step;
                if (total >= cost[next])
                    continue;
                cost[next] = total;
                prev[next] = at;
                queue.Enqueue(next, total);
            }
        }
        if (prev[goal] < 0)
            return null;
        for (var at = goal; at != start; at = prev[at])
            route.Add(CoordOf(at));
        route.Reverse();
        return route;
    }

    /// <summary>
    /// 将大世界中以 (originX, originY) 为左上角锚点的 5x5 物理区域提取为玩家当前的 5x5 视觉视口。
    /// 视口内每个格点都对应真实的物理地貌（道路、草原、平原、森林、山脉、水系或坐落的聚落），
    /// 完全使用已有的 5x5 网格进行墨线连通呈现。
    /// </summary>
    public List<Rimisekai.Housing.Room> Get5x5ViewportRooms(int originX, int originY, int baseRoomId = 1000, int regionId = 99)
    {
        var rooms = new List<Rimisekai.Housing.Room>();
        var grid = new Rimisekai.Housing.Room[5, 5];

        originX = System.Math.Clamp(originX, 0, System.Math.Max(0, Width - 5));
        originY = System.Math.Clamp(originY, 0, System.Math.Max(0, Height - 5));

        for (var ly = 0; ly < 5; ly++)
        {
            for (var lx = 0; lx < 5; lx++)
            {
                var wx = originX + lx;
                var wy = originY + ly;
                var tile = Tiles[wx, wy];
                var roomId = baseRoomId + ly * 5 + lx;

                var name = ResolveTilePhysicalName(tile, wx, wy);

                var room = new Rimisekai.Housing.Room
                {
                    Id = roomId,
                    Name = name,
                    RegionId = regionId,
                    X = lx,
                    Y = ly,
                    Open = true,
                };
                // 房间至少要有一个标签；按名字归出室外/室内，也供天气特效取室外格。
                room.EnsureDefaultTag();

                grid[lx, ly] = room;
                rooms.Add(room);
            }
        }

        // 四方向物理连通性：道路相连建立门洞；陆地相邻建立通行通道
        for (var ly = 0; ly < 5; ly++)
        {
            for (var lx = 0; lx < 5; lx++)
            {
                var curTile = Tiles[originX + lx, originY + ly];
                var curRoom = grid[lx, ly];

                // 水平向东
                if (lx + 1 < 5)
                {
                    var eastTile = Tiles[originX + lx + 1, originY + ly];
                    var eastRoom = grid[lx + 1, ly];
                    if (CanConnect(curTile, eastTile))
                    {
                        curRoom.Links.Add(eastRoom.Id);
                        eastRoom.Links.Add(curRoom.Id);
                    }
                }

                // 垂直向南
                if (ly + 1 < 5)
                {
                    var southTile = Tiles[originX + lx, originY + ly + 1];
                    var southRoom = grid[lx, ly + 1];
                    if (CanConnect(curTile, southTile))
                    {
                        curRoom.Links.Add(southRoom.Id);
                        southRoom.Links.Add(curRoom.Id);
                    }
                }
            }
        }

        return rooms;
    }

    /// <summary>
    /// 默认居中或按大世界主要聚落获取宏观 5x5 视口。
    /// </summary>
    public List<Rimisekai.Housing.Room> ExportTo5x5WorldRooms(int baseRoomId = 1000, int regionId = 99)
    {
        // 视口定格在领地周围；没有领地才退到第一个重要 POI（如首都或城镇）
        var defaultOx = Width / 2 - 2;
        var defaultOy = Height / 2 - 2;

        if (HasHome)
        {
            defaultOx = System.Math.Clamp(HomeX - 2, 0, Width - 5);
            defaultOy = System.Math.Clamp(HomeY - 2, 0, Height - 5);
        }
        else if (Pois.Count > 0)
        {
            defaultOx = System.Math.Clamp(Pois[0].X - 2, 0, Width - 5);
            defaultOy = System.Math.Clamp(Pois[0].Y - 2, 0, Height - 5);
        }

        return Get5x5ViewportRooms(defaultOx, defaultOy, baseRoomId, regionId);
    }

    private string ResolveTilePhysicalName(WorldTile tile, int wx, int wy)
    {
        // 1. 若格点上坐落着 POI，显示纯正日式西幻轻小说聚落名称（如王都、边境要塞、艾尔姆村）
        if (tile.PoiId > 0)
        {
            var poi = Pois.Find(p => p.Id == tile.PoiId);
            if (poi != null && poi.NameZh.Length > 0)
                return poi.NameZh;
        }

        // 2. 真实道路：道路就是道路，不强行堆砌
        if (tile.IsRoad)
            return "道路";

        // 3. 真实河流：河流就是河流
        if (tile.IsRiver || tile.Terrain == WorldTerrainType.River)
            return "河流";

        // 4. 自然物理地貌（草原、森林、山岳、平原等朴素名称）
        var (_, terrainName) = NameGenerator.GenerateTerrainName(tile.Terrain, Seed, wy * Width + wx);
        return terrainName;
    }

    private static bool CanConnect(WorldTile a, WorldTile b)
    {
        // 深海不可直接跨越步行通行
        if (a.Terrain == WorldTerrainType.DeepWater && b.Terrain == WorldTerrainType.DeepWater)
            return false;

        // 道路与道路/桥梁相连，必定完全打通
        if (a.IsRoad && b.IsRoad)
            return true;

        // 陆地与陆地之间相互连通
        var aPassable = a.Terrain != WorldTerrainType.DeepWater && a.Terrain != WorldTerrainType.MountainSnow;
        var bPassable = b.Terrain != WorldTerrainType.DeepWater && b.Terrain != WorldTerrainType.MountainSnow;
        return aPassable && bPassable;
    }
}
