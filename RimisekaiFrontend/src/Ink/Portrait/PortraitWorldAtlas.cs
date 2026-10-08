using System;
using Godot;
using Rimisekai.WorldMap;

namespace Rimisekai.Portrait;

/// <summary>
/// 大世界底图：把生成器的整张地图（默认 128×128）烘成一张灰阶刻版风的位图，每格 <see cref="TilePx"/> 像素。
/// 地形只用骨白—炭黑的灰阶区分（海最暗、平原中灰、雪最亮），再叠每种地貌一眼认得出的刻版图样（见 <see cref="Glyphs"/>）：
/// 水波、树冠、针叶塔、受光背光的山峰与雪顶、丘陵弧、乱石、芦苇、沙点、裂纹等；
/// 海岸描一道骨白线；河流是一道与海同色、带水纹的水槽，两岸各描骨白细线（与海岸同一笔法，一眼可知是水），
/// 河宽随流水进度从源头到入海口渐宽；道路骨白线（按生成器的流向 / 连通掩码从格心连到边）。
/// 聚落与领地标记不烘进图里，运行时按缩放矢量绘制，放大也清晰。同一份地图只烘一次。
/// </summary>
public static class PortraitWorldAtlas
{
    public const int TilePx = 12;

    private static WorldMapData? _for;
    private static ImageTexture? _texture;

    public static ImageTexture Texture(WorldMapData map)
    {
        if (_texture != null && ReferenceEquals(_for, map))
            return _texture;
        _for = map;
        _texture = ImageTexture.CreateFromImage(Bake(map));
        return _texture;
    }

    public static Image Bake(WorldMapData map)
    {
        var w = map.Width * TilePx;
        var h = map.Height * TilePx;
        var data = new byte[w * h * 3];

        for (var ty = 0; ty < map.Height; ty++)
        {
            for (var tx = 0; tx < map.Width; tx++)
            {
                var tile = map.Tiles[tx, ty];
                // 河格的底子是两岸的陆地，水槽另画。
                var ground = tile.IsRiver ? Bank(map, tx, ty) : tile.Terrain;
                var tone = Tone(ground);
                for (var py = 0; py < TilePx; py++)
                {
                    for (var px = 0; px < TilePx; px++)
                    {
                        var v = tone + Hatch(ground, tx, ty, px, py);
                        // 海拔明暗：微微的东南向阴影，让地势起伏可读。
                        if (IsLand(ground))
                            v += (tile.Elevation - 0.5f) * 0.10f;
                        Put(data, w, tx * TilePx + px, ty * TilePx + py, v);
                    }
                }
            }
        }

        // 海岸：陆地格贴着水的那一边描一道骨白细线。
        for (var ty = 0; ty < map.Height; ty++)
            for (var tx = 0; tx < map.Width; tx++)
            {
                var tile = map.Tiles[tx, ty];
                if (!IsLand(tile.Terrain))
                    continue;
                for (var d = 0; d < 4; d++)
                {
                    var (dx, dy) = Direction4Extensions.Offsets[d];
                    var n = map.GetTile(tx + dx, ty + dy);
                    if (n == null || !IsSea(n.Terrain))
                        continue;
                    for (var k = 0; k < TilePx; k++)
                    {
                        var (px, py) = d switch
                        {
                            0 => (k, 0),
                            1 => (TilePx - 1, k),
                            2 => (k, TilePx - 1),
                            _ => (0, k),
                        };
                        Put(data, w, tx * TilePx + px, ty * TilePx + py, 0.80f);
                    }
                }
            }

        // 河流：先挖水槽，再在槽外紧贴的一圈陆地像素上描岸线。
        var channel = new bool[w * h];
        for (var ty = 0; ty < map.Height; ty++)
            for (var tx = 0; tx < map.Width; tx++)
            {
                var tile = map.Tiles[tx, ty];
                if (!tile.IsRiver)
                    continue;
                var half = 1 + (int)MathF.Round(Math.Clamp(tile.RiverFlowProgress, 0f, 1f) * 2f);
                Channel(channel, w, tx, ty, tile.RiverDirections, half);
            }
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (!channel[y * w + x])
                    continue;
                var (tx, ty) = (x / TilePx, y / TilePx);
                Put(data, w, x, y, Tone(WorldTerrainType.ShallowWater) + Hatch(WorldTerrainType.River, tx, ty, x % TilePx, y % TilePx));
            }
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (channel[y * w + x] || !IsLand(map.Tiles[x / TilePx, y / TilePx].IsRiver ? WorldTerrainType.Plains : map.Tiles[x / TilePx, y / TilePx].Terrain))
                    continue;
                if ((x > 0 && channel[y * w + x - 1]) || (x < w - 1 && channel[y * w + x + 1])
                    || (y > 0 && channel[(y - 1) * w + x]) || (y < h - 1 && channel[(y + 1) * w + x]))
                    Put(data, w, x, y, 0.80f);
            }

        // 道路：炭黑描边的骨白线（桥＝河上的路，压在河槽之上）。
        for (var ty = 0; ty < map.Height; ty++)
            for (var tx = 0; tx < map.Width; tx++)
            {
                var tile = map.Tiles[tx, ty];
                if (!tile.IsRoad)
                    continue;
                // 先压一道炭黑底边，亮地貌（沙、雪、山）上的白路也看得见。
                Strokes(data, w, tx, ty, tile.RoadDirections, 0.06f, tile.RoadClass >= 2 ? 4 : 3, dashed: false);
                Strokes(data, w, tx, ty, tile.RoadDirections, tile.RoadClass >= 2 ? 0.95f : tile.RoadClass == 1 ? 0.86f : 0.74f,
                        tile.RoadClass >= 2 ? 2 : 1, dashed: tile.RoadClass < 2);
            }

        return Image.CreateFromData(w, h, false, Image.Format.Rgb8, data);
    }

    public static bool IsSea(WorldTerrainType t) => t is WorldTerrainType.DeepWater or WorldTerrainType.ShallowWater;

    public static bool IsLand(WorldTerrainType t) =>
        t is not (WorldTerrainType.DeepWater or WorldTerrainType.ShallowWater or WorldTerrainType.Lake or WorldTerrainType.River);

    /// <summary>河格两岸的地貌：取四邻里第一块陆地（按北东南西），四面都是水就当平原。</summary>
    private static WorldTerrainType Bank(WorldMapData map, int tx, int ty)
    {
        for (var d = 0; d < 4; d++)
        {
            var (dx, dy) = Direction4Extensions.Offsets[d];
            var n = map.GetTile(tx + dx, ty + dy);
            if (n != null && !n.IsRiver && IsLand(n.Terrain))
                return n.Terrain;
        }
        return WorldTerrainType.Plains;
    }

    /// <summary>河槽：格心一块 (2·half) 见方，再沿流向掩码伸到格边，宽同。</summary>
    private static void Channel(bool[] channel, int w, int tx, int ty, byte mask, int half)
    {
        var c = TilePx / 2;
        for (var py = 0; py < TilePx; py++)
            for (var px = 0; px < TilePx; px++)
            {
                var inX = px >= c - half && px < c + half;
                var inY = py >= c - half && py < c + half;
                var hit = (inX && inY)
                    || (inX && py < c && (mask & 1) != 0)
                    || (inY && px >= c && (mask & 2) != 0)
                    || (inX && py >= c && (mask & 4) != 0)
                    || (inY && px < c && (mask & 8) != 0);
                if (hit)
                    channel[(ty * TilePx + py) * w + tx * TilePx + px] = true;
            }
    }

    /// <summary>
    /// 格底灰度（0 黑 → 1 骨白），按大类拉开阶梯，缩到全图、图样看不清时也能靠明暗分区：
    /// 水 &lt;0.11 ＜ 林地与沼泽 0.13–0.27 ＜ 草原平原 0.36–0.50 ＜ 丘陵荒地乱石沙 0.55–0.66 ＜ 山 0.70 ＜ 雪山冰雪 0.80–0.90。
    /// </summary>
    private static float Tone(WorldTerrainType t) => t switch
    {
        WorldTerrainType.DeepWater => 0.03f,
        WorldTerrainType.ShallowWater => 0.08f,
        WorldTerrainType.Lake => 0.10f,
        WorldTerrainType.River => 0.10f,
        WorldTerrainType.DenseForest => 0.13f,
        WorldTerrainType.Jungle => 0.16f,
        WorldTerrainType.Bog => 0.20f,
        WorldTerrainType.Swamp => 0.18f,
        WorldTerrainType.Forest => 0.22f,
        WorldTerrainType.Taiga => 0.27f,
        WorldTerrainType.Grassland => 0.36f,
        WorldTerrainType.Plains => 0.44f,
        WorldTerrainType.Savanna => 0.50f,
        WorldTerrainType.Hills => 0.55f,
        WorldTerrainType.Wasteland => 0.58f,
        WorldTerrainType.Rocky => 0.62f,
        WorldTerrainType.Sand => 0.66f,
        WorldTerrainType.Mountain => 0.70f,
        WorldTerrainType.MountainSnow => 0.80f,
        WorldTerrainType.Ice => 0.84f,
        WorldTerrainType.Snow => 0.90f,
        _ => 0.30f,
    };

    /// <summary>
    /// 刻版图样在格内像素 (px,py) 上的明暗增量。每格按坐标哈希挑一式、再决定是否左右翻，连片时不显得机械重复。
    /// 湖与河槽用浅海的水纹。
    /// </summary>
    private static float Hatch(WorldTerrainType t, int tx, int ty, int px, int py)
    {
        if (t is WorldTerrainType.Lake or WorldTerrainType.River)
            t = WorldTerrainType.ShallowWater;
        if (!Glyphs.TryGetValue(t, out var styles))
            return 0f;
        var h = Hash(tx, ty);
        var rows = styles[(int)((h >> 3) % (uint)styles.Length)];
        var x = ((h >> 7) & 1u) == 1u ? TilePx - 1 - px : px;
        return rows[py][x] switch
        {
            'W' => 0.36f,
            'w' => 0.18f,
            'k' => -0.12f,
            'K' => -0.22f,
            _ => 0f,
        };
    }

    /// <summary>各地貌的 12×12 刻版图样（每种一到两式，按格哈希挑式、左右翻）：「.」不着墨，W 亮墨、w 浅亮、k 浅影、K 深影。</summary>
    private static readonly System.Collections.Generic.Dictionary<WorldTerrainType, string[][]> Glyphs = new()
    {
        [WorldTerrainType.Forest] = new[]
        {
            new[]
            {
                "............",
                "...wWWw.....",
                "..wWWWWw....",
                "..WWWWWk....",
                "..wWWWkk....",
                "...kWkk.....",
                "....K...wWw.",
                "....K..wWWWw",
                ".......WWWWk",
                ".......wWkkk",
                ".........K..",
                ".........K..",
            },
            new[]
            {
                "............",
                ".......wWw..",
                "......wWWWw.",
                ".wWw..WWWWk.",
                "wWWWw.wWWkk.",
                "WWWWk..kKk..",
                "wWWkk....K..",
                ".kKk.....K..",
                "..K.........",
                "..K.........",
                "............",
                "............",
            },
        },
        [WorldTerrainType.DenseForest] = new[]
        {
            new[]
            {
                ".wWw...wWw..",
                "wWWWw.wWWWw.",
                "WWWWkkWWWWk.",
                "wWkkk.wWkkk.",
                ".kKwWw.kKk..",
                "..wWWWw..wWw",
                "..WWWWk.wWWW",
                "..wWkkk.WWWk",
                "...kKk..wWkk",
                ".wWw.K...kK.",
                "wWWWw..wWw..",
                "WWWWk.wWWWw.",
            },
        },
        [WorldTerrainType.Jungle] = new[]
        {
            new[]
            {
                ".w..w.....w.",
                ".Ww.WwwW.Ww.",
                "wWWWWWWWwWWw",
                "WWWkWWWWWWWk",
                ".wkk.WkkkWk.",
                "..K.wwK..K..",
                ".wW.wWWw.w..",
                "wWWwWWWWwWW.",
                "WWWkWWWkWWWk",
                ".Wkk.kk..Wk.",
                "..K...K...K.",
                "............",
            },
        },
        [WorldTerrainType.Taiga] = new[]
        {
            new[]
            {
                "............",
                "...W........",
                "..wWk.......",
                "..WWk....W..",
                ".wWWkk..wWk.",
                "..WWk...WWk.",
                ".wWWWkk.wWWk",
                "wWWWWkkwWWWk",
                "...K..wWWWWk",
                "...K......K.",
                "..........K.",
                "............",
            },
        },
        [WorldTerrainType.Mountain] = new[]
        {
            new[]
            {
                "............",
                ".....W......",
                "....WWk.....",
                "....WWkk....",
                "...WWWkk....",
                "..wWWWkkk...",
                "..WWWWkkkk..",
                ".wWWWWkkkkk.",
                ".WWWWWkkkkkk",
                "wWWWWWkkkkkk",
                "............",
                "............",
            },
        },
        [WorldTerrainType.MountainSnow] = new[]
        {
            new[]
            {
                "............",
                ".....W......",
                "....WWW.....",
                "....WWWW....",
                "...WWWWWW...",
                "..wWWkWkWk..",
                "..WWWWkkkk..",
                ".wWWWWkkkkk.",
                ".WWWWWkkkkkk",
                "wWWWWWkkkkkk",
                "............",
                "............",
            },
        },
        [WorldTerrainType.Hills] = new[]
        {
            new[]
            {
                "............",
                "............",
                "....wWWw....",
                "..wWWkkkk...",
                ".wWkkkkkkk..",
                ".Wkkkkkkkkk.",
                "............",
                ".......wWw..",
                ".....wWkkkk.",
                "....wWkkkkkk",
                "............",
                "............",
            },
        },
        [WorldTerrainType.Rocky] = new[]
        {
            new[]
            {
                "............",
                "..wW........",
                ".wWWk.......",
                ".WWkk...wW..",
                "..kK...wWkk.",
                ".......WkkK.",
                "...wWw..kK..",
                "..wWWkk.....",
                "..WWkkK.....",
                "...kKK...wk.",
                ".........kK.",
                "............",
            },
        },
        [WorldTerrainType.Swamp] = new[]
        {
            new[]
            {
                "..K.........",
                "..K..K......",
                "..K..K......",
                "..W..K..K...",
                "..W..W..K...",
                "..W..W..W...",
                ".kkkkkkkkk..",
                "............",
                ".......K..K.",
                "...K...W..K.",
                "...W...W..W.",
                ".kkkkk.kkkkk",
            },
        },
        [WorldTerrainType.Bog] = new[]
        {
            new[]
            {
                "............",
                "............",
                ".kkkk...kkk.",
                "............",
                ".....kkkkk..",
                "..W.........",
                ".WW.W..kkk..",
                "..W.W.......",
                ".kkkkk..kkkk",
                "............",
                "............",
                "............",
            },
        },
        [WorldTerrainType.Grassland] = new[]
        {
            new[]
            {
                "............",
                "..w.w.......",
                "...w........",
                ".........w.w",
                "..........w.",
                "....w.w.....",
                ".....w......",
                "............",
                ".w.w.....w.w",
                "..w.......w.",
                "............",
                "............",
            },
        },
        [WorldTerrainType.Savanna] = new[]
        {
            new[]
            {
                "............",
                ".......wWWWw",
                ".w.w...kkKkk",
                "..w.......K.",
                "..........K.",
                "............",
                "....w.w.....",
                ".....w......",
                "............",
                ".w.w.....w.w",
                "..w.......w.",
                "............",
            },
        },
        [WorldTerrainType.Plains] = new[]
        {
            new[]
            {
                "............",
                "............",
                "....w.......",
                "............",
                "............",
                "..........w.",
                "............",
                "............",
                ".....w......",
                "............",
                "............",
                "............",
            },
        },
        [WorldTerrainType.Sand] = new[]
        {
            new[]
            {
                ".w....w...w.",
                "...w......k.",
                "w....k..w...",
                "..w.....k..w",
                ".....w......",
                ".k.....w..w.",
                "...w.k......",
                "w......w..k.",
                "...k..w.....",
                ".w........w.",
                ".....w..k...",
                "..k.....w...",
            },
        },
        [WorldTerrainType.Wasteland] = new[]
        {
            new[]
            {
                "............",
                ".k..........",
                "..k.....k...",
                "..kk...k....",
                "....k.k.....",
                ".....k......",
                ".....k...k..",
                "....k...k...",
                "...k...kk...",
                ".......k....",
                ".........k..",
                "............",
            },
        },
        [WorldTerrainType.Snow] = new[]
        {
            new[]
            {
                "............",
                "..k.........",
                "............",
                ".......k....",
                "............",
                "...........k",
                "....k.......",
                "............",
                ".........k..",
                ".k..........",
                "......k.....",
                "............",
            },
        },
        [WorldTerrainType.Ice] = new[]
        {
            new[]
            {
                "............",
                ".k..........",
                "..k.........",
                "...k........",
                "...k.k......",
                "....k.......",
                "....k...k...",
                ".....k.k....",
                ".........k..",
                ".........k..",
                "..........k.",
                "............",
            },
        },
        [WorldTerrainType.ShallowWater] = new[]
        {
            new[]
            {
                "............",
                "............",
                "..wW.wW.....",
                ".W..W..W....",
                "............",
                "............",
                ".......wW.wW",
                "......W..W..",
                "............",
                "............",
                "............",
                "............",
            },
        },
        [WorldTerrainType.DeepWater] = new[]
        {
            new[]
            {
                "............",
                "............",
                "............",
                "..wWw.......",
                ".w...w......",
                "............",
                "............",
                "............",
                ".......wWw..",
                "......w...w.",
                "............",
                "............",
            },
        },
    };

    /// <summary>从格心向掩码里的每个方向画到格边。</summary>
    private static void Strokes(byte[] data, int w, int tx, int ty, byte mask, float v, int width, bool dashed)
    {
        var c = TilePx / 2;
        var any = false;
        for (var d = 0; d < 4; d++)
        {
            if ((mask & (1 << d)) == 0)
                continue;
            any = true;
            var (dx, dy) = Direction4Extensions.Offsets[d];
            for (var k = 0; k <= c; k++)
            {
                if (dashed && ((k + (d % 2) * 2) % 4) == 3)
                    continue;
                for (var s = 0; s < width; s++)
                {
                    var px = c + dx * k + (dy != 0 ? s - width / 2 : 0);
                    var py = c + dy * k + (dx != 0 ? s - width / 2 : 0);
                    if (px >= 0 && px < TilePx && py >= 0 && py < TilePx)
                        Put(data, w, tx * TilePx + px, ty * TilePx + py, v);
                }
            }
        }
        if (!any)
            for (var s = -1; s <= 1; s++)
                for (var u = -1; u <= 1; u++)
                    Put(data, w, tx * TilePx + c + s, ty * TilePx + c + u, v);
    }

    private static void Put(byte[] data, int w, int x, int y, float v)
    {
        var b = (byte)Math.Clamp((int)(v * 255f), 0, 255);
        var i = (y * w + x) * 3;
        data[i] = b;
        data[i + 1] = b;
        data[i + 2] = (byte)Math.Clamp(b + 3, 0, 255);
    }

    private static uint Hash(int x, int y)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
    }
}
