using System;
using System.Collections;

namespace Rimisekai.WorldMap;

/// <summary>
/// 大地图上的行进状态：队伍此刻站在哪一格、哪些格已经探明（迷雾）。
/// 探明记录随存档走（<see cref="Serialize"/> / <see cref="Restore"/>）；
/// 队伍坐标不进存档——读档即人在据点，出行时从领地格出发。
/// </summary>
public sealed class WorldExploration
{
    /// <summary>每走到一格，四周这么多格（切比雪夫距离）被探明。</summary>
    public const int SightRadius = 1;

    /// <summary>领地四周开局就探明的范围。</summary>
    public const int HomeSightRadius = 2;

    private readonly WorldMapData _map;
    private readonly BitArray _discovered;

    public WorldExploration(WorldMapData map)
    {
        _map = map;
        _discovered = new BitArray(map.Width * map.Height);
        PartyX = map.HomeX;
        PartyY = map.HomeY;
        Reveal(map.HomeX, map.HomeY, HomeSightRadius);
    }

    public int PartyX { get; private set; }
    public int PartyY { get; private set; }

    public bool AtHome => PartyX == _map.HomeX && PartyY == _map.HomeY;

    public bool IsDiscovered(int x, int y) => _map.InBounds(x, y) && _discovered[_map.IndexOf(x, y)];

    public int DiscoveredCount
    {
        get
        {
            var n = 0;
            for (var i = 0; i < _discovered.Length; i++)
                if (_discovered[i])
                    n++;
            return n;
        }
    }

    /// <summary>把队伍放到某格并探明四周（落脚、走一步都走这里）。</summary>
    public void PlaceParty(int x, int y)
    {
        PartyX = x;
        PartyY = y;
        Reveal(x, y, SightRadius);
    }

    public void Reveal(int cx, int cy, int radius)
    {
        for (var dx = -radius; dx <= radius; dx++)
            for (var dy = -radius; dy <= radius; dy++)
                if (_map.InBounds(cx + dx, cy + dy))
                    _discovered[_map.IndexOf(cx + dx, cy + dy)] = true;
    }

    /// <summary>探明记录落成存档串（位图 Base64）。</summary>
    public string Serialize()
    {
        var bytes = new byte[(_discovered.Length + 7) / 8];
        _discovered.CopyTo(bytes, 0);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>读回探明记录，队伍回到领地格。</summary>
    public void Restore(string data)
    {
        var restored = new BitArray(Convert.FromBase64String(data)) { Length = _discovered.Length };
        _discovered.SetAll(false);
        _discovered.Or(restored);
        PartyX = _map.HomeX;
        PartyY = _map.HomeY;
        Reveal(_map.HomeX, _map.HomeY, HomeSightRadius);
    }
}
