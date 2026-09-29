using System;
using System.IO;

namespace Rimisekai.WorldMap.Visualization;

/// <summary>
/// 纯 C# 无依赖的像素级地图图像导出工具：
/// 输出标准 24-bit 无压缩位图文件 (.bmp)，可在任何操作系统或浏览器中直接查看。
/// 支持缩放放大渲染，以及突出显示河流、道路桥梁与不同阶级的聚落标记。
/// </summary>
public static class WorldMapImageExporter
{
    public struct Rgb
    {
        public byte R;
        public byte G;
        public byte B;

        public Rgb(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }
    }

    public static readonly Rgb ColorDeepWater = new(18, 48, 120);
    public static readonly Rgb ColorShallowWater = new(65, 135, 215);
    public static readonly Rgb ColorLake = new(45, 115, 195);
    public static readonly Rgb ColorRiver = new(50, 195, 245);
    public static readonly Rgb ColorSand = new(235, 215, 145);
    public static readonly Rgb ColorPlains = new(155, 205, 110);
    public static readonly Rgb ColorGrassland = new(115, 185, 80);
    public static readonly Rgb ColorForest = new(45, 135, 55);
    public static readonly Rgb ColorDenseForest = new(20, 85, 35);
    public static readonly Rgb ColorJungle = new(15, 130, 70);
    public static readonly Rgb ColorTaiga = new(65, 115, 95);
    public static readonly Rgb ColorSavanna = new(190, 185, 85);
    public static readonly Rgb ColorWasteland = new(170, 140, 105);
    public static readonly Rgb ColorRocky = new(145, 145, 150);
    public static readonly Rgb ColorHills = new(160, 130, 80);
    public static readonly Rgb ColorMountain = new(110, 105, 110);
    public static readonly Rgb ColorMountainSnow = new(245, 248, 255);
    public static readonly Rgb ColorSnow = new(225, 235, 245);
    public static readonly Rgb ColorIce = new(190, 225, 240);
    public static readonly Rgb ColorSwamp = new(75, 80, 50);
    public static readonly Rgb ColorBog = new(85, 70, 60);

    public static readonly Rgb ColorRoad = new(220, 160, 60);
    public static readonly Rgb ColorBridge = new(180, 100, 40);

    public static readonly Rgb ColorPoiCapital = new(255, 215, 0);   // 金色
    public static readonly Rgb ColorPoiTown = new(240, 60, 60);      // 亮红
    public static readonly Rgb ColorPoiCastle = new(180, 30, 90);    // 深绯红
    public static readonly Rgb ColorPoiVillage = new(245, 140, 40);  // 橙黄
    public static readonly Rgb ColorPoiRuin = new(180, 80, 230);     // 亮紫

    public static Rgb GetTerrainColor(WorldTerrainType terrain) => terrain switch
    {
        WorldTerrainType.DeepWater => ColorDeepWater,
        WorldTerrainType.ShallowWater => ColorShallowWater,
        WorldTerrainType.Lake => ColorLake,
        WorldTerrainType.River => ColorRiver,
        WorldTerrainType.Sand => ColorSand,
        WorldTerrainType.Plains => ColorPlains,
        WorldTerrainType.Grassland => ColorGrassland,
        WorldTerrainType.Forest => ColorForest,
        WorldTerrainType.DenseForest => ColorDenseForest,
        WorldTerrainType.Jungle => ColorJungle,
        WorldTerrainType.Taiga => ColorTaiga,
        WorldTerrainType.Savanna => ColorSavanna,
        WorldTerrainType.Wasteland => ColorWasteland,
        WorldTerrainType.Rocky => ColorRocky,
        WorldTerrainType.Hills => ColorHills,
        WorldTerrainType.Mountain => ColorMountain,
        WorldTerrainType.MountainSnow => ColorMountainSnow,
        WorldTerrainType.Snow => ColorSnow,
        WorldTerrainType.Ice => ColorIce,
        WorldTerrainType.Swamp => ColorSwamp,
        WorldTerrainType.Bog => ColorBog,
        _ => ColorPlains,
    };

    /// <summary>
    /// 将地图导出为色块位图文件（每个格点放大为 scale×scale 像素）。
    /// </summary>
    public static void ExportToBmp(WorldMapData map, string filePath, int scale = 4)
    {
        if (scale < 1)
            scale = 1;

        var imgWidth = map.Width * scale;
        var imgHeight = map.Height * scale;

        // 预填充像素色彩缓冲
        var pixels = new Rgb[imgWidth, imgHeight];

        // 1. 底层地形渲染
        for (var x = 0; x < map.Width; x++)
        {
            for (var y = 0; y < map.Height; y++)
            {
                var t = map.Tiles[x, y];
                var baseColor = GetTerrainColor(t.Terrain);

                // 道路叠加
                if (t.IsBridge)
                    baseColor = ColorBridge;
                else if (t.IsRoad)
                    baseColor = ColorRoad;
                else if (t.IsRiver)
                    baseColor = ColorRiver;

                for (var sx = 0; sx < scale; sx++)
                {
                    for (var sy = 0; sy < scale; sy++)
                    {
                        pixels[x * scale + sx, y * scale + sy] = baseColor;
                    }
                }
            }
        }

        // 2. 聚落 POI 覆盖层（放大尺寸并在中心标出显眼标记）
        foreach (var poi in map.Pois)
        {
            var poiColor = poi.Type switch
            {
                WorldPoiType.Capital => ColorPoiCapital,
                WorldPoiType.Town => ColorPoiTown,
                WorldPoiType.Castle or WorldPoiType.Fortress => ColorPoiCastle,
                WorldPoiType.Village => ColorPoiVillage,
                WorldPoiType.Ruin => ColorPoiRuin,
                _ => ColorPoiTown,
            };

            var cx = poi.X * scale + scale / 2;
            var cy = poi.Y * scale + scale / 2;
            var markerRadius = poi.Type == WorldPoiType.Capital ? scale + 1 : scale;

            for (var dx = -markerRadius; dx <= markerRadius; dx++)
            {
                for (var dy = -markerRadius; dy <= markerRadius; dy++)
                {
                    var px = cx + dx;
                    var py = cy + dy;
                    if (px >= 0 && px < imgWidth && py >= 0 && py < imgHeight)
                    {
                        // 边框黑线，内部填充
                        if (System.Math.Abs(dx) == markerRadius || System.Math.Abs(dy) == markerRadius)
                            pixels[px, py] = new Rgb(20, 20, 20);
                        else
                            pixels[px, py] = poiColor;
                    }
                }
            }
        }

        // 3. 写入 24-bit BMP 格式字节流
        SaveBmpFile(filePath, imgWidth, imgHeight, pixels);
    }

    private static void SaveBmpFile(string filePath, int width, int height, Rgb[,] pixels)
    {
        // BMP 每行像素字节必须对齐到 4 字节边界
        var rowSize = (width * 3 + 3) & ~3;
        var imageSize = rowSize * height;
        const int headerSize = 54;
        var fileSize = headerSize + imageSize;

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // BITMAPFILEHEADER (14 bytes)
        bw.Write((byte)'B');
        bw.Write((byte)'M');
        bw.Write(fileSize);
        bw.Write((short)0); // reserved
        bw.Write((short)0); // reserved
        bw.Write(headerSize); // offset to pixel data

        // BITMAPINFOHEADER (40 bytes)
        bw.Write(40); // header size
        bw.Write(width);
        bw.Write(height); // 正数表示自底向上存储
        bw.Write((short)1); // color planes
        bw.Write((short)24); // 24 bits per pixel
        bw.Write(0); // BI_RGB (uncompressed)
        bw.Write(imageSize);
        bw.Write(2835); // 水平分辨率 (72 DPI)
        bw.Write(2835); // 垂直分辨率 (72 DPI)
        bw.Write(0); // colors in palette
        bw.Write(0); // important colors

        // 像素阵列：自底向上存储 (y 从 height-1 递减到 0)
        var rowPadding = new byte[rowSize - width * 3];

        for (var y = height - 1; y >= 0; y--)
        {
            for (var x = 0; x < width; x++)
            {
                var p = pixels[x, y];
                // BMP 像素通道顺序为 BGR
                bw.Write(p.B);
                bw.Write(p.G);
                bw.Write(p.R);
            }
            if (rowPadding.Length > 0)
            {
                bw.Write(rowPadding);
            }
        }
    }
}
