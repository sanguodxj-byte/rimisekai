using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Clock;
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

    /// <summary>
    /// 返回领地据点视口。人在兴趣点时要把人带回本家——
    /// POI 房间挂在领地表里、编号从 <see cref="Territory.MaxTerritoryRegions"/> 起，
    /// 只切图层不挪人，人还站在 POI 里，网格照旧画 POI 的房间。
    /// </summary>
    public void SwitchToTerritory()
    {
        SetLayer(MapLayer.Territory);
        if (RegionId >= Territory.MaxTerritoryRegions && _territoryHomeRoomId >= 0)
            Enter(_territoryHomeRoomId);
        Write("返回了领地据点。");
    }

    /// <summary>
    /// 出发去兴趣点之前站的那间本家房间。回来时把人送回这里，
    /// 而不是丢在 POI 区里。进 POI 时记录，随存档走。
    /// </summary>
    private int _territoryHomeRoomId = -1;

    /// <summary>出发去 POI 前的落脚点，供存档回填。</summary>
    public int TerritoryHomeRoomId => _territoryHomeRoomId;

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

        // 将 POI 房间导入领地系统，分配新 RegionId。
        // 领地内区域占 0..8，POI 区域从 MaxTerritoryRegions 起顺延，免得撞号。
        var baseRegion = System.Math.Max(State.Territory.UnlockedRegions,
            Territory.MaxTerritoryRegions);
        State.Territory.SetUnlockedRegions(baseRegion + poiMap.Blocks.Count);

        var housingRooms = poiMap.ExportToHousingRooms();
        foreach (var r in housingRooms)
        {
            r.RegionId = baseRegion + (poiMap.GetBlockByRoomId(r.Id)?.RegionId ?? 0);
            State.Territory.AddRoom(r);
        }

        SetLayer(MapLayer.WorldPoi, poi.NameZh.Length > 0 ? poi.NameZh : poi.NameEn);
        // 记下出发前站的本家房间：返回领地时要把人送回这里，而不是丢在 POI 区。
        if (_territoryHomeRoomId < 0 && PlayerRoomId >= 0 && RegionId < Territory.MaxTerritoryRegions)
            _territoryHomeRoomId = PlayerRoomId;
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
    /// 地区名称语义是世界地图下的地区、地城、POI、任务地点与领地同级，严禁子一级地区概念。
    /// </summary>
    public string MapTitle() => Layer switch
    {
        MapLayer.World => "世界",
        MapLayer.QuestPlace or MapLayer.WorldPoi =>
            LayerPlaceName.Length > 0 ? LayerPlaceName : "未知地点",
        _ => State.Territory.Name.Length > 0 ? State.Territory.Name : "领地",
    };

    /// <summary>
    /// 当前所在的房间名。严禁拼接子一级地区概念。
    /// </summary>
    public string PlaceName()
    {
        var room = Room(PlayerRoomId);
        return room?.Name ?? MapTitle();
    }

    public IReadOnlyList<Room> Map()
    {
        if (Layer == MapLayer.World)
            return _worldRooms ??= State.World.ExportTo5x5WorldRooms();
        return State.Territory.Rooms.FindAll(r => r.RegionId == RegionId);
    }

    public bool SelectRegion(int regionId)
    {
        if (!State.Territory.IsRegionUnlocked(regionId))
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
        State.Territory.MasterRoomId = roomId;
        var master = State.Roster.Master;
        if (master != null)
            _presence[master.Id] = roomId;
        // 跨场景的落位（如进入 POI）不是走路：跟着的人直接被带到身边。
        // 门锁着的地方不带人——主人进了自家卧室，跟班的留在外头。
        foreach (var worker in Day.Workers)
        {
            if (!worker.FollowsPlayer)
                continue;
            if (State.Territory.IsLocked(room))
            {
                Write($"{NameOf(worker.CharacterId)}被关在{room.Name}门外。");
                continue;
            }
            worker.RoomId = roomId;
            worker.Path.Clear();
            worker.FacilityId = -1;
            worker.Phase = WorkPhase.Idle;
            _presence[worker.CharacterId] = roomId;
        }
    }

    private string NameOf(int characterId) =>
        State.Roster.Find(characterId)?.Name ?? "";

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
        State.Territory.MasterRoomId = roomId;
        var master = State.Roster.Master;
        if (master != null)
            _presence[master.Id] = roomId;
        // 恶劣天气进室外房间：赶路更费劲。
        WorldEffects.SpendMoveStamina(master, State.Territory, roomId, State.Weather);
        WriteArrival(roomId);
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
