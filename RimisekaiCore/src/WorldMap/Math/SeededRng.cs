using System;

namespace Rimisekai.WorldMap.Math;

/// <summary>
/// 跨平台高确定性 XorShift32 随机数发生器。
/// 杜绝运行时与平台差异，保证相同种子产生字节级相同的序列。
/// </summary>
public sealed class SeededRng
{
    private uint _state;

    public SeededRng(int seed)
    {
        _state = seed == 0 ? 0x6D2B79F5u : (uint)seed;
    }

    public uint NextUInt()
    {
        var x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x;
        return x;
    }

    public int NextRange(int minInclusive, int maxExclusive)
    {
        if (minInclusive >= maxExclusive)
            return minInclusive;
        var range = (uint)(maxExclusive - minInclusive);
        return minInclusive + (int)(NextUInt() % range);
    }

    public float NextFloat() => (NextUInt() >> 8) / (float)(1 << 24);

    public bool NextBool(float chance = 0.5f) => NextFloat() < chance;
}

/// <summary>
/// 格点无状态雪崩哈希工具，用于装饰物挑选、局部变体以及破除对称性。
/// </summary>
public static class VariantHasher
{
    public static uint HashCoord(int x, int y, uint salt = 0)
    {
        var h = (uint)x * 0x9E3779B1u + (uint)y * 0x85EBCA6Bu ^ salt;
        h ^= h >> 16;
        h *= 0x7FEB352Du;
        h ^= h >> 15;
        h *= 0x846CA68Bu;
        h ^= h >> 16;
        return h;
    }

    public static int Pick(int x, int y, int modulo, uint salt = 0)
    {
        if (modulo <= 1)
            return 0;
        return (int)(HashCoord(x, y, salt) % (uint)modulo);
    }

    public static float Value(int x, int y, uint salt = 0) =>
        (HashCoord(x, y, salt) & 0xFFFFFF) / (float)0x1000000;
}
