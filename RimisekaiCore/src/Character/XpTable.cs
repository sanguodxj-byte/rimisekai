using System;

namespace Rimisekai.Character;

/// <summary>
/// 角色等级 1-100。累计经验 TotalFor(level)，反查 LevelFor(exp)。
/// 曲线 10×(n-1)²：前期快、后期靠战斗与任务等高收入冲。
/// </summary>
public static class XpTable
{
    public const int MaxLevel = 100;
    public const int Factor = 10;

    public static int TotalFor(int level)
    {
        var n = Math.Clamp(level, 1, MaxLevel);
        return Factor * (n - 1) * (n - 1);
    }

    public static int LevelFor(int exp)
    {
        if (exp < 0)
            exp = 0;
        return Math.Clamp((int)Math.Sqrt(exp / (double)Factor) + 1, 1, MaxLevel);
    }
}
