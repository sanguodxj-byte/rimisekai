namespace Rimisekai.WorldMap;

/// <summary>
/// 队伍在大地图上站在哪一格。不进存档——读档即人在据点，出行时从领地格出发。
/// </summary>
public sealed class WorldParty
{
    private readonly WorldMapData _map;

    public WorldParty(WorldMapData map)
    {
        _map = map;
        X = map.HomeX;
        Y = map.HomeY;
    }

    public int X { get; private set; }
    public int Y { get; private set; }

    public bool AtHome => X == _map.HomeX && Y == _map.HomeY;

    public void MoveTo(int x, int y)
    {
        X = x;
        Y = y;
    }
}
