using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Housing;
using Rimisekai.WorldMap;

namespace Rimisekai.Hub;

/// <summary>
/// 左上角地图所处的图层。决定标题写什么。
/// 世界地图与任务地点尚未实现，但枚举先立在这里，避免以后改动界面结构。
/// </summary>
public enum MapLayer
{
    /// <summary>领地内，标题写“领地”。</summary>
    Territory = 0,

    /// <summary>世界地图，标题写“世界”。</summary>
    World = 1,

    /// <summary>任务/委托地点，标题写该地点名。</summary>
    QuestPlace = 2,

    /// <summary>世界上的兴趣点，标题写该 POI 名。</summary>
    WorldPoi = 3,
}

public sealed partial class HubSession
{
    /// <summary>玩家行动消耗（格，1 格=5 分钟）。移动是 baseline。</summary>
    public const int CostMove = 1;

    public int RegionId { get; private set; }

    /// <summary>
    /// 左上角当前所处的图层。世界地图与任务地点尚未实现，
    /// 目前只会是领地；枚举先立在这里，避免以后改动界面结构。
    /// </summary>
    public MapLayer Layer { get; private set; } = MapLayer.Territory;

    /// <summary>
    /// 进入某个图层。任务地点与 POI 的名称由调用方在进入前写入
    /// <see cref="LayerPlaceName"/>。
    /// </summary>
    public void SetLayer(MapLayer layer, string placeName = "")
    {
        Layer = layer;
        LayerPlaceName = placeName;
    }

    /// <summary>切到大世界全局地图视口。</summary>
    public void SwitchToWorld()
    {
        SetLayer(MapLayer.World);
        Write("展开了世界大地图。");
    }

    /// <summary>返回领地据点视口。</summary>
    public void SwitchToTerritory()
    {
        SetLayer(MapLayer.Territory);
        Write("返回了领地据点。");
    }

    /// <summary>在领地与大世界之间来回切换。</summary>
    public void ToggleWorldLayer()
    {
        if (Layer == MapLayer.World)
            SwitchToTerritory();
        else
            SwitchToWorld();
    }

    private List<Room>? _worldRooms;

    /// <summary>进入世界上的某个 POI 场景（按数据表规模与分区自然开辟）。</summary>
    public bool EnterWorldPoi(int poiId)
    {
        var poi = State.World.Pois.Find(p => p.Id == poiId);
        if (poi == null)
            return false;

        var poiMap = State.EnterPoi(poiId);

        // 将 POI 房间导入领地系统，分配新 RegionId
        var baseRegion = State.Territory.UnlockedRegions;
        State.Territory.SetUnlockedRegions(baseRegion + poiMap.Blocks.Count);

        var housingRooms = poiMap.ExportToHousingRooms();
        foreach (var r in housingRooms)
        {
            r.RegionId = baseRegion + (poiMap.GetBlockByRoomId(r.Id)?.RegionId ?? 0);
            State.Territory.AddRoom(r);
        }

        SetLayer(MapLayer.WorldPoi, poi.NameZh.Length > 0 ? poi.NameZh : poi.NameEn);
        if (poiMap.Blocks.Count > 0 && poiMap.Blocks[0].StartRoom != null)
        {
            Enter(poiMap.Blocks[0].StartRoom!.Id);
        }
        Write($"抵达了{MapTitle()}。");
        return true;
    }

    /// <summary>非领地图层的地点名（任务地点名 / POI 名）。</summary>
    public string LayerPlaceName { get; private set; } = "";

    /// <summary>
    /// 左上角标题：领地内写“领地”，世界地图写“世界”，
    /// 任务/委托地点与 POI 写对应地点名。
    /// </summary>
    public string MapTitle() => Layer switch
    {
        MapLayer.World => "世界",
        MapLayer.QuestPlace or MapLayer.WorldPoi =>
            LayerPlaceName.Length > 0 ? LayerPlaceName : "未知地点",
        _ => "领地",
    };

    /// <summary>
    /// 当前位置的标准名，形如“中央·庭院”（区域·房间）。
    /// 图内的 POI 头与日志都以此为准。
    /// </summary>
    public string PlaceName()
    {
        var room = Room(PlayerRoomId);
        var region = RegionName(RegionId);
        if (room == null)
            return region;
        return $"{region}·{room.Name}";
    }

    private static string RegionName(int regionId) => regionId switch
    {
        0 => "中央",
        1 => "东部",
        2 => "西部",
        _ => $"{regionId}区",
    };

    public IReadOnlyList<Room> Map()
    {
        if (Layer == MapLayer.World)
            return _worldRooms ??= State.World.ExportTo5x5WorldRooms();
        return State.Territory.Rooms.FindAll(r => r.RegionId == RegionId);
    }

    public bool SelectRegion(int regionId)
    {
        if (regionId < 0 || regionId >= State.Territory.UnlockedRegions)
            return false;
        RegionId = regionId;
        return true;
    }

    public void Enter(int roomId)
    {
        var room = Room(roomId);
        if (room == null || !room.Open)
            return;
        PlayerRoomId = roomId;
        RegionId = room.RegionId;
        var master = State.Roster.Master;
        if (master != null)
            _presence[master.Id] = roomId;
    }

    public bool Move(int roomId)
    {
        var here = Room(PlayerRoomId);
        var next = Room(roomId);
        if (MapCovered || here == null || next == null || !next.Open || !here.Links.Contains(roomId))
            return false;
        PassTime(CostMove * TerritoryClock.StepMinutes);
        LeaveFixture();
        PlayerRoomId = roomId;
        RegionId = next.RegionId;
        var master = State.Roster.Master;
        if (master != null)
            _presence[master.Id] = roomId;
        Write($"你走进了{next.Name}。");
        DropSelectionIfGone();
        return true;
    }

    public bool Develop(int roomId)
    {
        var room = Room(roomId);
        var money = State.Money;
        if (room == null || !State.Territory.OpenRoom(roomId, ref money))
            return false;
        PassTime(CostDevelop * TerritoryClock.StepMinutes);
        State.Money = money;
        Write($"开拓了{room.Name}。");
        return true;
    }

    private CharacterState? PickPresent()
    {
        var selected = State.Roster.Find(SelectedCharacterId);
        if (selected != null && !selected.IsMaster
            && _presence.GetValueOrDefault(selected.Id, -1) == PlayerRoomId)
        {
            return selected;
        }
        foreach (var character in State.Roster.Members)
        {
            if (!character.IsMaster && _presence.GetValueOrDefault(character.Id, -1) == PlayerRoomId)
                return character;
        }
        return null;
    }
}
