using System.Collections.Generic;

namespace Rimisekai.WorldMap;

/// <summary>
/// 地城进度：哪些石室的守卫打过了、宝库开过了、神龛拜过了（按兴趣点编号＋生成器房号记），
/// 哪些地城的首领已倒下。随存档走；地城本身按种子现生成，不存。
/// </summary>
public sealed class DungeonLedger
{
    public HashSet<long> SpentRooms { get; } = new();
    public HashSet<int> Cleared { get; } = new();

    public static long Key(int poiId, int roomId) => ((long)poiId << 32) | (uint)roomId;

    public bool IsSpent(int poiId, int roomId) => SpentRooms.Contains(Key(poiId, roomId));

    public void Spend(int poiId, int roomId) => SpentRooms.Add(Key(poiId, roomId));
}
