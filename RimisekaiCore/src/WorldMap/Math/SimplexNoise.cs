using System;

namespace Rimisekai.WorldMap.Math;

/// <summary>
/// 高性能确定性 2D Simplex 噪声生成器，支持分形布朗运动 (FBM)。
/// 纯 C# 实现，不依赖任何第三方库或游戏引擎。
/// </summary>
public sealed class SimplexNoise
{
    private readonly byte[] _perm = new byte[512];
    private readonly byte[] _permMod12 = new byte[512];

    private static readonly float[] Grad3X = { 1, -1, 1, -1, 1, -1, 1, -1, 0, 0, 0, 0 };
    private static readonly float[] Grad3Y = { 1, 1, -1, -1, 0, 0, 0, 0, 1, -1, 1, -1 };

    private static readonly float F2 = 0.5f * ((float)System.Math.Sqrt(3.0) - 1.0f);
    private static readonly float G2 = (3.0f - (float)System.Math.Sqrt(3.0)) / 6.0f;

    public int Seed { get; }

    public SimplexNoise(int seed)
    {
        Seed = seed;
        var p = new byte[256];
        for (var i = 0; i < 256; i++)
            p[i] = (byte)i;

        // 基于种子打乱置换表 (Fisher-Yates)
        var rng = new SeededRng(seed);
        for (var i = 255; i > 0; i--)
        {
            var j = rng.NextRange(0, i + 1);
            (p[i], p[j]) = (p[j], p[i]);
        }

        for (var i = 0; i < 512; i++)
        {
            _perm[i] = p[i & 255];
            _permMod12[i] = (byte)(_perm[i] % 12);
        }
    }

    /// <summary>
    /// 评估单频 2D Simplex 噪声，返回值范围约为 [-1.0, 1.0]。
    /// </summary>
    public float Sample2D(float x, float y)
    {
        float n0, n1, n2;

        var s = (x + y) * F2;
        var i = FastFloor(x + s);
        var j = FastFloor(y + s);
        var t = (i + j) * G2;
        var x0 = x - (i - t);
        var y0 = y - (j - t);

        int i1, j1;
        if (x0 > y0)
        {
            i1 = 1;
            j1 = 0;
        }
        else
        {
            i1 = 0;
            j1 = 1;
        }

        var x1 = x0 - i1 + G2;
        var y1 = y0 - j1 + G2;
        var x2 = x0 - 1.0f + 2.0f * G2;
        var y2 = y0 - 1.0f + 2.0f * G2;

        var ii = i & 255;
        var jj = j & 255;
        var gi0 = _permMod12[ii + _perm[jj]];
        var gi1 = _permMod12[ii + i1 + _perm[jj + j1]];
        var gi2 = _permMod12[ii + 1 + _perm[jj + 1]];

        var t0 = 0.5f - x0 * x0 - y0 * y0;
        if (t0 < 0)
        {
            n0 = 0.0f;
        }
        else
        {
            t0 *= t0;
            n0 = t0 * t0 * (Grad3X[gi0] * x0 + Grad3Y[gi0] * y0);
        }

        var t1 = 0.5f - x1 * x1 - y1 * y1;
        if (t1 < 0)
        {
            n1 = 0.0f;
        }
        else
        {
            t1 *= t1;
            n1 = t1 * t1 * (Grad3X[gi1] * x1 + Grad3Y[gi1] * y1);
        }

        var t2 = 0.5f - x2 * x2 - y2 * y2;
        if (t2 < 0)
        {
            n2 = 0.0f;
        }
        else
        {
            t2 *= t2;
            n2 = t2 * t2 * (Grad3X[gi2] * x2 + Grad3Y[gi2] * y2);
        }

        // 缩放到 [-1, 1] 范围
        return 70.0f * (n0 + n1 + n2);
    }

    /// <summary>
    /// 多阶分形布朗运动 (FBM) 噪声。
    /// </summary>
    public float SampleFbm(float x, float y, int octaves, float frequency = 1.0f, float lacunarity = 2.0f, float gain = 0.5f)
    {
        var total = 0.0f;
        var amplitude = 1.0f;
        var maxAmplitude = 0.0f;
        var curFreq = frequency;

        for (var i = 0; i < octaves; i++)
        {
            total += Sample2D(x * curFreq, y * curFreq) * amplitude;
            maxAmplitude += amplitude;
            amplitude *= gain;
            curFreq *= lacunarity;
        }

        return maxAmplitude > 0 ? total / maxAmplitude : total;
    }

    private static int FastFloor(float x)
    {
        var xi = (int)x;
        return x < xi ? xi - 1 : xi;
    }
}
