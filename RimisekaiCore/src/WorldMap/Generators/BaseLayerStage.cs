using System;
using Rimisekai.WorldMap.Math;

namespace Rimisekai.WorldMap.Generators;

/// <summary>
/// 基础连续场生成阶段：
/// 计算每个格点的海拔高程 (Elevation)、湿度 (Moisture)、温度 (Temperature) 以及养分 (Nutrient)。
/// 包含大陆边缘自然衰减（Falloff Mask），确保地图被自然汪洋环抱。
/// </summary>
public static class BaseLayerStage
{
    public static void Execute(WorldMapData map)
    {
        var seed = map.Seed;
        var noiseElev = new SimplexNoise(seed);
        var noiseMoist = new SimplexNoise(seed + 1000);
        var noiseTemp = new SimplexNoise(seed + 2000);
        var noiseNutrient = new SimplexNoise(seed + 5000);
        var noiseCoastWarp = new SimplexNoise(seed + 7000);

        var w = map.Width;
        var h = map.Height;

        // 基础空间频率自适应（以 128 为基准）
        var freqScale = (float)System.Math.Sqrt(128.0 / System.Math.Max(w, 32));
        var elevFreq = 0.018f * freqScale;
        var moistFreq = 0.022f * freqScale;
        var tempFreq = 0.014f * freqScale;
        var warpFreq = 0.035f * freqScale;

        for (var x = 0; x < w; x++)
        {
            var nx = (float)x / (w - 1); // 归一化 [0, 1]

            for (var y = 0; y < h; y++)
            {
                var ny = (float)y / (h - 1);
                var tile = map.Tiles[x, y];

                // 1. 大陆边缘自然衰减（加入边缘扰动，呈现锯齿与峡湾形态）
                var warp = noiseCoastWarp.Sample2D(x * warpFreq, y * warpFreq) * 0.08f;
                var distToEdgeX = System.Math.Min(nx, 1.0f - nx);
                var distToEdgeY = System.Math.Min(ny, 1.0f - ny);
                var edgeDist = System.Math.Min(distToEdgeX, distToEdgeY) + warp;

                // 边缘 12% 范围内线性衰减到 0
                var falloff = edgeDist < 0.12f ? System.Math.Clamp(edgeDist / 0.12f, 0.0f, 1.0f) : 1.0f;
                // 平滑阶跃
                falloff = falloff * falloff * (3.0f - 2.0f * falloff);

                // 2. 海拔高程 (4阶 FBM)
                var rawElev = noiseElev.SampleFbm(x, y, octaves: 4, frequency: elevFreq, lacunarity: 2.1f, gain: 0.45f);
                var elev01 = (rawElev + 1.0f) * 0.5f;
                tile.Elevation = System.Math.Clamp(elev01 * falloff, 0.0f, 1.0f);

                // 3. 湿度 (2阶 FBM，受海洋与高山降水调节)
                var rawMoist = noiseMoist.SampleFbm(x, y, octaves: 2, frequency: moistFreq, lacunarity: 2.0f, gain: 0.4f);
                var moist01 = (rawMoist + 1.0f) * 0.5f;
                // 适度向近海区域补充水汽
                if (tile.Elevation < 0.35f)
                    moist01 = System.Math.Clamp(moist01 + 0.15f * (0.35f - tile.Elevation) / 0.35f, 0.0f, 1.0f);
                tile.Moisture = moist01;

                // 4. 温度 (纬度递变 + 局部微扰 - 高度衰减)
                // 假设 y=0 为北极寒带，y=h/2 为赤道热带，y=h-1 为南半球温寒带；或从北到南平缓过渡
                var latitudeFactor = (float)System.Math.Sin(ny * System.Math.PI); // 中部最热，两极最冷
                var tempNoiseVal = noiseTemp.SampleFbm(x, y, octaves: 2, frequency: tempFreq, lacunarity: 2.0f, gain: 0.5f) * 0.22f;
                var altitudePenalty = System.Math.Max(0.0f, tile.Elevation - 0.50f) * 0.75f; // 高山寒冷效应

                var temp = latitudeFactor * 0.85f + 0.15f + tempNoiseVal - altitudePenalty;
                tile.Temperature = System.Math.Clamp(temp, 0.0f, 1.0f);

                // 5. 养分/肥沃度
                var rawNutrient = (noiseNutrient.Sample2D(x * 0.03f, y * 0.03f) + 1.0f) * 0.5f;
                var nutrientBoost = System.Math.Clamp(tile.Moisture * 0.6f + tile.Temperature * 0.4f, 0.0f, 1.0f);
                tile.Nutrient = System.Math.Clamp(rawNutrient * nutrientBoost, 0.0f, 1.0f);
            }
        }
    }
}
