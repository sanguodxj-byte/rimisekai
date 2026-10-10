using System;
using System.IO;

namespace Rimisekai.PoiMap.Visualization;

/// <summary>
/// POI 地图像素级色块与连通通道可视化导出器。
/// 纯 C# 无依赖输出标准 24-bit BMP 图像，直观验证：
/// 1. 5x5 块内房间分布与连通通路（门洞）；
/// 2. 终点房间是否位于边缘格；
/// 3. 跨 5x5 块的【边界互通通道】是否打通。
/// </summary>
public static class PoiMapImageExporter
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

    public static readonly Rgb ColorBackground = new(24, 26, 32);       // 深灰底色
    public static readonly Rgb ColorRoomNormal = new(195, 205, 215);    // 浅灰蓝普通房
    public static readonly Rgb ColorRoomStart = new(70, 200, 110);      // 翠绿起点
    public static readonly Rgb ColorRoomEnd = new(235, 75, 75);         // 亮红终点（必须在边缘）
    public static readonly Rgb ColorRoomTreasure = new(255, 215, 0);    // 金色宝藏
    public static readonly Rgb ColorRoomShrine = new(130, 100, 240);    // 秘紫神龛
    public static readonly Rgb ColorRoomCorridor = new(160, 175, 190);  // 过道

    public static readonly Rgb ColorWall = new(60, 65, 75);             // 房间墙线
    public static readonly Rgb ColorDoor = new(230, 235, 240);          // 门洞通道
    public static readonly Rgb ColorBoundaryLink = new(255, 140, 20);   // 醒目橙色跨块边界通道
    public static readonly Rgb ColorBlockBorder = new(80, 95, 120);     // 5x5 块外边界框

    public static void ExportToBmp(PoiMapData map, string filePath, int cellSize = 48)
    {
        if (map.Blocks.Count == 0)
            return;

        // 确定全局视口跨度
        int minBx = int.MaxValue, maxBx = int.MinValue;
        int minBy = int.MaxValue, maxBy = int.MinValue;

        foreach (var b in map.Blocks)
        {
            minBx = System.Math.Min(minBx, b.BlockX);
            maxBx = System.Math.Max(maxBx, b.BlockX);
            minBy = System.Math.Min(minBy, b.BlockY);
            maxBy = System.Math.Max(maxBy, b.BlockY);
        }

        var totalBlocksW = maxBx - minBx + 1;
        var totalBlocksH = maxBy - minBy + 1;

        const int blockPadding = 8; // 块与块之间的可视缝隙留白
        var blockPixelSize = 5 * cellSize;
        var imgWidth = totalBlocksW * (blockPixelSize + blockPadding) + blockPadding;
        var imgHeight = totalBlocksH * (blockPixelSize + blockPadding) + blockPadding;

        var pixels = new Rgb[imgWidth, imgHeight];

        // 填充底色
        for (var x = 0; x < imgWidth; x++)
        {
            for (var y = 0; y < imgHeight; y++)
            {
                pixels[x, y] = ColorBackground;
            }
        }

        // 1. 绘制各 5x5 块背景框与内部房间
        foreach (var block in map.Blocks)
        {
            var originX = blockPadding + (block.BlockX - minBx) * (blockPixelSize + blockPadding);
            var originY = blockPadding + (block.BlockY - minBy) * (blockPixelSize + blockPadding);

            // 绘制 5x5 块边框外框
            DrawRectOutline(pixels, originX - 1, originY - 1, blockPixelSize + 2, blockPixelSize + 2, ColorBlockBorder, imgWidth, imgHeight);

            // 绘制网格中的房间
            for (var lx = 0; lx < 5; lx++)
            {
                for (var ly = 0; ly < 5; ly++)
                {
                    var room = block.Grid[lx, ly];
                    var rx = originX + lx * cellSize;
                    var ry = originY + ly * cellSize;

                    if (room != null)
                    {
                        var fill = room.RoomType switch
                        {
                            PoiRoomType.Start => ColorRoomStart,
                            PoiRoomType.End => ColorRoomEnd,
                            PoiRoomType.Treasure => ColorRoomTreasure,
                            PoiRoomType.Shrine => ColorRoomShrine,
                            PoiRoomType.Corridor => ColorRoomCorridor,
                            _ => ColorRoomNormal,
                        };

                        // 填充房间中心区域（内缩留出墙厚）
                        FillRect(pixels, rx + 4, ry + 4, cellSize - 8, cellSize - 8, fill, imgWidth, imgHeight);

                        // 墙壁边框
                        DrawRectOutline(pixels, rx + 3, ry + 3, cellSize - 6, cellSize - 6, ColorWall, imgWidth, imgHeight);
                    }
                }
            }

            // 绘制块内部相连房间之间的门洞通道
            for (var lx = 0; lx < 5; lx++)
            {
                for (var ly = 0; ly < 5; ly++)
                {
                    var room = block.Grid[lx, ly];
                    if (room == null)
                        continue;

                    var rx = originX + lx * cellSize;
                    var ry = originY + ly * cellSize;

                    // 检查东边房间是否相连
                    var east = block.RoomAt(lx + 1, ly);
                    if (east != null && room.Links.Contains(east.Id))
                    {
                        // 绘制水平连通门洞通道
                        FillRect(pixels, rx + cellSize - 6, ry + cellSize / 2 - 4, 12, 8, ColorDoor, imgWidth, imgHeight);
                    }

                    // 检查南边房间是否相连
                    var south = block.RoomAt(lx, ly + 1);
                    if (south != null && room.Links.Contains(south.Id))
                    {
                        // 绘制垂直连通门洞通道
                        FillRect(pixels, rx + cellSize / 2 - 4, ry + cellSize - 6, 8, 12, ColorDoor, imgWidth, imgHeight);
                    }
                }
            }
        }

        // 2. 绘制跨 5x5 块的高亮【边界通道】(Boundary Links)
        foreach (var link in map.BoundaryLinks)
        {
            var rA = map.GetRoom(link.RoomAId);
            var rB = map.GetRoom(link.RoomBId);
            if (rA == null || rB == null)
                continue;

            var originAX = blockPadding + (rA.BlockX - minBx) * (blockPixelSize + blockPadding);
            var originAY = blockPadding + (rA.BlockY - minBy) * (blockPixelSize + blockPadding);
            var originBX = blockPadding + (rB.BlockX - minBx) * (blockPixelSize + blockPadding);
            var originBY = blockPadding + (rB.BlockY - minBy) * (blockPixelSize + blockPadding);

            var centerAX = originAX + rA.LocalX * cellSize + cellSize / 2;
            var centerAY = originAY + rA.LocalY * cellSize + cellSize / 2;
            var centerBX = originBX + rB.LocalX * cellSize + cellSize / 2;
            var centerBY = originBY + rB.LocalY * cellSize + cellSize / 2;

            // 跨缝隙绘制加粗的高亮边界门道
            var midX = (centerAX + centerBX) / 2;
            var midY = (centerAY + centerBY) / 2;

            if (System.Math.Abs(centerAX - centerBX) > System.Math.Abs(centerAY - centerBY))
            {
                // 水平跨缝
                var x1 = System.Math.Min(centerAX, centerBX);
                var x2 = System.Math.Max(centerAX, centerBX);
                FillRect(pixels, x1 + 10, midY - 6, x2 - x1 - 20, 12, ColorBoundaryLink, imgWidth, imgHeight);
            }
            else
            {
                // 垂直跨缝
                var y1 = System.Math.Min(centerAY, centerBY);
                var y2 = System.Math.Max(centerAY, centerBY);
                FillRect(pixels, midX - 6, y1 + 10, 12, y2 - y1 - 20, ColorBoundaryLink, imgWidth, imgHeight);
            }
        }

        SaveBmpFile(filePath, imgWidth, imgHeight, pixels);
    }

    private static void FillRect(Rgb[,] pixels, int x, int y, int w, int h, Rgb color, int maxW, int maxH)
    {
        for (var px = x; px < x + w; px++)
        {
            for (var py = y; py < y + h; py++)
            {
                if (px >= 0 && px < maxW && py >= 0 && py < maxH)
                {
                    pixels[px, py] = color;
                }
            }
        }
    }

    private static void DrawRectOutline(Rgb[,] pixels, int x, int y, int w, int h, Rgb color, int maxW, int maxH)
    {
        for (var px = x; px < x + w; px++)
        {
            if (px >= 0 && px < maxW)
            {
                if (y >= 0 && y < maxH) pixels[px, y] = color;
                if (y + h - 1 >= 0 && y + h - 1 < maxH) pixels[px, y + h - 1] = color;
            }
        }
        for (var py = y; py < y + h; py++)
        {
            if (py >= 0 && py < maxH)
            {
                if (x >= 0 && x < maxW) pixels[x, py] = color;
                if (x + w - 1 >= 0 && x + w - 1 < maxW) pixels[x + w - 1, py] = color;
            }
        }
    }

    private static void SaveBmpFile(string filePath, int width, int height, Rgb[,] pixels)
    {
        var rowSize = (width * 3 + 3) & ~3;
        var imageSize = rowSize * height;
        const int headerSize = 54;
        var fileSize = headerSize + imageSize;

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // BITMAPFILEHEADER
        bw.Write((byte)'B');
        bw.Write((byte)'M');
        bw.Write(fileSize);
        bw.Write((short)0);
        bw.Write((short)0);
        bw.Write(headerSize);

        // BITMAPINFOHEADER
        bw.Write(40);
        bw.Write(width);
        bw.Write(height);
        bw.Write((short)1);
        bw.Write((short)24);
        bw.Write(0);
        bw.Write(imageSize);
        bw.Write(2835);
        bw.Write(2835);
        bw.Write(0);
        bw.Write(0);

        var rowPadding = new byte[rowSize - width * 3];

        for (var y = height - 1; y >= 0; y--)
        {
            for (var x = 0; x < width; x++)
            {
                var p = pixels[x, y];
                bw.Write(p.B);
                bw.Write(p.G);
                bw.Write(p.R);
            }
            if (rowPadding.Length > 0)
                bw.Write(rowPadding);
        }
    }
}
