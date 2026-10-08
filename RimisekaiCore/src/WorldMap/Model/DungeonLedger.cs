using System.Collections.Generic;

namespace Rimisekai.WorldMap;

/// <summary>
/// 地城进度：走过哪些石室（迷雾按此揭开）、哪些石室的守卫打过了、宝库开过了、神龛拜过了
/// （按地城编号＋生成器房号记），哪些地城的首领已倒下。
/// 遗迹的进度随存档走；委托地城一趟一本、不存。地城本身按种子现生成，不存。
/// </summary>
public sealed class DungeonLedger
{
    public HashSet<long> SpentRooms { get; } = new();
    public HashSet<long> VisitedRooms { get; } = new();
    public HashSet<int> Cleared { get; } = new();

    public static long Key(int dungeonId, int roomId) => ((long)dungeonId << 32) | (uint)roomId;

    public bool IsSpent(int dungeonId, int roomId) => SpentRooms.Contains(Key(dungeonId, roomId));

    public void Spend(int dungeonId, int roomId) => SpentRooms.Add(Key(dungeonId, roomId));

    public bool IsVisited(int dungeonId, int roomId) => VisitedRooms.Contains(Key(dungeonId, roomId));

    public void Visit(int dungeonId, int roomId) => VisitedRooms.Add(Key(dungeonId, roomId));
}
