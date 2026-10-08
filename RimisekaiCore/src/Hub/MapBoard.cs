using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    /// 兴趣点房间进领地表时的编号起点：生成器给的房号从 1 起，会撞上领地自己的房号，
    /// 一律加上这个偏移（连线同样平移），离开即整片撤掉。
    /// </summary>
    public const int PoiRoomIdBase = 1_000_000;

    private WorldParty Trek => State.Party;

    /// <summary>队伍在大地图上的格点。人在领地里时站在领地格。</summary>
    public (int X, int Y) WorldPartyPosition => (Trek.X, Trek.Y);

    /// <summary>
    /// 出行：从领地走上大地图（队伍站在领地格），或从兴趣点走出来（队伍站在该兴趣点格）。
    /// 走上大地图即身子离开领地的所有房间。已在大地图上则不动。
    /// </summary>
    public void SwitchToWorld()
    {
        if (Layer == MapLayer.World)
            return;
        if (Layer == MapLayer.QuestPlace)
        {
            AbandonQuestDungeon();
            return;
        }
        if (Layer == MapLayer.WorldPoi)
        {
            var place = LayerPlaceName;
            LeavePoi();
            LeaveTerritoryBody();
            SetLayer(MapLayer.World);
            Write($"你离开了{place}。");
            return;
        }
        if (PlayerRoomId >= 0)
            _territoryHomeRoomId = PlayerRoomId;
        LeaveTerritoryBody();
        Trek.MoveTo(State.World.HomeX, State.World.HomeY);
        _worldRooms = null;
        SetLayer(MapLayer.World);
        Write("你走出了领地。");
    }

    /// <summary>
    /// 返回领地：人在兴趣点或大地图上时，沿最省时的路走回领地格（路上照样耗时），
    /// 再把人送回出发前站的那间本家房间。路上撞上遭遇就停在那一格（仍在大地图上）；
    /// <paramref name="safe"/>＝战败被抬回去，一路不起遭遇。
    /// </summary>
    public void SwitchToTerritory(bool safe = false)
    {
        if (Layer == MapLayer.Territory)
            return;
        if (Layer == MapLayer.QuestPlace)
        {
            EndQuestDungeon("");
            return;
        }
        if (Layer == MapLayer.WorldPoi)
        {
            LeavePoi();
            LeaveTerritoryBody();
            SetLayer(MapLayer.World);
        }
        if (!Trek.AtHome)
        {
            var home = State.World.FindRoute(Trek.X, Trek.Y, State.World.HomeX, State.World.HomeY)
                ?? throw new System.InvalidOperationException("大地图上找不到回领地的路：走得到这里就一定走得回去。");
            if (WalkWorld(home, roll: !safe))
                return;
        }
        SetLayer(MapLayer.Territory);
        if (_territoryHomeRoomId >= 0)
            Enter(_territoryHomeRoomId);
        _territoryHomeRoomId = -1;
        Write("你回到了领地。");
    }

    /// <summary>
    /// 出发前站的那间本家房间。回来时把人送回这里。走出领地时记录；
    /// 存档时人一律记回这间房（读档即人在据点，见 <see cref="Snapshot"/>）。
    /// </summary>
    private int _territoryHomeRoomId = -1;

    /// <summary>在领地与大世界之间来回切换。</summary>
    public void ToggleWorldLayer()
    {
        if (Layer == MapLayer.World)
            SwitchToTerritory();
        else
            SwitchToWorld();
    }

    private List<Room>? _worldRooms;

    /// <summary>横版世界层的 5×5 视口房编号起点（房号 = 起点 + 行×5 + 列）。</summary>
    private const int WorldViewRoomBase = 1000;

    /// <summary>
    /// 横版世界层的 5×5 视口：以队伍为中心（贴边夹住），每走一步重建。
    /// 走不过去的格不可点。
    /// </summary>
    private List<Room> WorldViewRooms()
    {
        if (_worldRooms != null)
            return _worldRooms;
        var (ox, oy) = WorldViewOrigin();
        _worldRooms = State.World.Get5x5ViewportRooms(ox, oy, WorldViewRoomBase);
        for (var i = 0; i < _worldRooms.Count; i++)
        {
            var room = _worldRooms[i];
            if (State.World.IsPassable(ox + room.X, oy + room.Y))
                continue;
            _worldRooms[i] = new Room { Id = room.Id, Name = room.Name, RegionId = room.RegionId, X = room.X, Y = room.Y, Open = false };
            _worldRooms[i].EnsureDefaultTag();
        }
        foreach (var room in _worldRooms)
            room.Links.RemoveAll(id => !_worldRooms.Exists(r => r.Id == id && r.Open));
        return _worldRooms;
    }

    private (int X, int Y) WorldViewOrigin() =>
        (System.Math.Clamp(Trek.X - 2, 0, State.World.Width - 5),
         System.Math.Clamp(Trek.Y - 2, 0, State.World.Height - 5));

    /// <summary>横版世界层视口房对应的大地图格点。</summary>
    public (int X, int Y) WorldTileOfViewRoom(int roomId)
    {
        var (ox, oy) = WorldViewOrigin();
        var local = roomId - WorldViewRoomBase;
        return (ox + local % 5, oy + local / 5);
    }

    /// <summary>横版世界层：队伍脚下那间视口房。</summary>
    public int WorldPartyViewRoomId
    {
        get
        {
            var (ox, oy) = WorldViewOrigin();
            return WorldViewRoomBase + (Trek.Y - oy) * 5 + (Trek.X - ox);
        }
    }

    /// <summary>
    /// 人走上大地图：身子离开领地的任何房间（PlayerRoomId = -1），
    /// 家里的人不会再来找他搭话，主人的自动日程也停摆；回领地时由 <see cref="Enter"/> 落回出发前的房间。
    /// </summary>
    private void LeaveTerritoryBody()
    {
        LeaveFixture();
        PlayerRoomId = -1;
        State.Territory.MasterRoomId = -1;
        var master = State.Roster.Master;
        if (master != null)
            _presence[master.Id] = -1;
    }

    /// <summary>
    /// 从队伍所在格走到这一格要花多少分钟；走不到返回 -1，原地返回 0。
    /// 抽屉每帧都要问，按（起点, 终点）记住上一次的结果。
    /// </summary>
    public int WorldTravelMinutes(int x, int y)
    {
        var key = (Trek.X, Trek.Y, x, y);
        if (_travelQuote.Key == key && _travelQuote.Map == State.World)
            return _travelQuote.Minutes;
        var route = State.World.FindRoute(Trek.X, Trek.Y, x, y);
        var total = route == null ? -1 : 0;
        if (route != null)
            foreach (var (rx, ry) in route)
                total += State.World.TravelMinutes(rx, ry);
        _travelQuote = (key, State.World, total);
        return total;
    }

    private ((int, int, int, int) Key, WorldMapData? Map, int Minutes) _travelQuote = ((-1, -1, -1, -1), null, -1);

    /// <summary>
    /// 大地图上前往一格：沿最省时的路过去（逐格推进时间），
    /// 到了是兴趣点就进场、是领地就回去。不在大地图上、画面被演出盖着、或走不到，返回 false。
    /// </summary>
    public bool TravelTo(int x, int y)
    {
        if (Layer != MapLayer.World || MapCovered || PendingEncounter != null)
            return false;
        var route = State.World.FindRoute(Trek.X, Trek.Y, x, y);
        if (route == null)
            return false;
        if (route.Count > 0 && WalkWorld(route, roll: true))
            return true;
        if (Trek.AtHome)
        {
            SwitchToTerritory();
            return true;
        }
        var poi = State.World.PoiAt(Trek.X, Trek.Y);
        if (poi != null)
            return EnterWorldPoi(poi.Id);
        Write($"你来到了{State.World.TileName(Trek.X, Trek.Y)}。");
        return true;
    }

    /// <summary>
    /// 沿路线逐格走：每进一格按地貌推进时间；<paramref name="roll"/> 时每格掷野外遭遇，
    /// 撞上就停在那一格，返回 true。
    /// </summary>
    private bool WalkWorld(List<(int x, int y)> route, bool roll)
    {
        _worldRooms = null;
        foreach (var (x, y) in route)
        {
            PassTime(State.World.TravelMinutes(x, y));
            Trek.MoveTo(x, y);
            if (roll && RollWildEncounter(x, y))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 进入大地图上的某个兴趣点：按种子现生成场景（同一处每次进来都一样），
    /// 房间以 <see cref="PoiRoomIdBase"/> 起编号装进领地表、区号从 <see cref="Territory.MaxTerritoryRegions"/> 起，
    /// 人落在正门。只有队伍站在该兴趣点格上才进得去（正常流程由 <see cref="TravelTo"/> 走到再进）。
    /// </summary>
    public bool EnterWorldPoi(int poiId)
    {
        var poi = State.World.Pois.Find(p => p.Id == poiId);
        if (poi == null || Layer != MapLayer.World || (Trek.X, Trek.Y) != (poi.X, poi.Y))
            return false;

        _currentPoiId = poiId;
        var start = ImportScene(State.EnterPoi(poiId));
        if (poi.Type == WorldPoiType.Ruin)
            _dungeon = new DungeonRun
            {
                Key = poi.Id,
                Seed = State.WorldSeed,
                Tier = DangerTier(poi.X, poi.Y),
                Ledger = State.Dungeons,
            };

        SetLayer(MapLayer.WorldPoi, poi.NameZh.Length > 0 ? poi.NameZh : poi.NameEn);
        Enter(start);
        if (InDungeon)
            VisitDungeonRoom(start);
        Write($"抵达了{MapTitle()}。");
        return true;
    }

    /// <summary>
    /// 调试 / 测试 / 出图入口：从当前位置把队伍直接走到某个兴趣点并进场。
    /// 不是传送——沿最省时路线走过去，时间照走。
    /// </summary>
    public bool TravelToPoiDirect(int poiId)
    {
        var poi = State.World.Pois.Find(p => p.Id == poiId);
        if (poi == null)
            return false;
        if (Layer != MapLayer.World)
            SwitchToWorld();
        var route = State.World.FindRoute(Trek.X, Trek.Y, poi.X, poi.Y);
        if (route == null)
            return false;
        WalkWorld(route, roll: false);
        return EnterWorldPoi(poiId);
    }

    /// <summary>此刻身在的兴趣点编号；-1 表示不在兴趣点里。</summary>
    private int _currentPoiId = -1;

    /// <summary>进 POI 前的已解锁区域数，离开时还原。-1 表示当前不在 POI。</summary>
    private int _unlockedBeforePoi = -1;

    /// <summary>领地自己的已解锁区域数（人在兴趣点里时不算兴趣点临时借的区号），存档用。</summary>
    public int TerritoryUnlockedRegions => _unlockedBeforePoi >= 0 ? _unlockedBeforePoi : State.Territory.UnlockedRegions;

    /// <summary>
    /// 退出当前兴趣点或委托地城：人（连同跟着的人）先回到本家落脚点，兴趣点房从领地表里撤掉，
    /// 已解锁区域数还原。图层由调用方再定。
    /// </summary>
    private void LeavePoi()
    {
        LeaveFixture();
        if (_territoryHomeRoomId >= 0)
            Enter(_territoryHomeRoomId);
        State.Territory.Rooms.RemoveAll(r => r.RegionId >= Territory.MaxTerritoryRegions);
        foreach (var key in new List<int>(_presence.Keys))
            if (_presence[key] >= PoiRoomIdBase)
                _presence[key] = PlayerRoomId;
        State.Territory.SetUnlockedRegions(_unlockedBeforePoi);
        _unlockedBeforePoi = -1;
        State.CurrentPoi = null;
        _currentPoiId = -1;
        _dungeon = null;
        _fogRooms = null;
        LayerPlaceName = "";
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
    /// 顶栏左上角的地名：兴趣点/地城由多个 5×5 块拼成时写「地名·方位」（罗恩要塞·东），
    /// 方位是玩家所在块在整张拼图里的相对位置（见 <see cref="BlockDirection"/>），跨块即变；
    /// 只有一个块的场景、领地与世界地图只写 <see cref="MapTitle"/>。
    /// </summary>
    public string HudTitle()
    {
        var direction = BlockDirection();
        return direction.Length > 0 ? $"{MapTitle()}·{direction}" : MapTitle();
    }

    /// <summary>
    /// 玩家所在 5×5 块在整张拼图中的方位（规则见 <see cref="DirectionOf"/>）。
    /// 不在兴趣点/地城里、或拼图只有一个块时为空串。
    /// </summary>
    public string BlockDirection()
    {
        if (Layer is not (MapLayer.WorldPoi or MapLayer.QuestPlace) || State.CurrentPoi!.Blocks.Count == 1)
            return "";
        return DirectionOf(State.CurrentPoi.Blocks, State.CurrentPoi.GetBlockByRoomId(PlayerRoomId - PoiRoomIdBase)!);
    }

    /// <summary>
    /// 某块在拼图中的方位：按全部块的外接矩形，东西、南北各分三段（块心落在前三分之一＝西/北、
    /// 后三分之一＝东/南、中段不写），两轴合成东北/西北/东南/西南，两轴都不写＝中央；
    /// 某轴只有一块宽则该轴不写。2×1＝西/东，1×2＝北/南，3×1＝西/中央/东，3×3 正中＝中央，
    /// L 形三块＝西北/东北/东南。
    /// </summary>
    public static string DirectionOf(IReadOnlyList<PoiMap.PoiBlock> blocks, PoiMap.PoiBlock here)
    {
        var minX = blocks.Min(b => b.BlockX);
        var minY = blocks.Min(b => b.BlockY);
        var direction = Third(here.BlockX - minX, blocks.Max(b => b.BlockX) - minX + 1, "西", "东")
            + Third(here.BlockY - minY, blocks.Max(b => b.BlockY) - minY + 1, "北", "南");
        return direction.Length > 0 ? direction : "中央";

        // 第 k 块（共 n 块）的块心在 (2k+1)/(2n)：前三分之一取 low、后三分之一取 high、中段不写。
        static string Third(int k, int n, string low, string high) =>
            3 * (2 * k + 1) < 2 * n ? low : 3 * (2 * k + 1) > 4 * n ? high : "";
    }

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
            return WorldViewRooms();
        if (InDungeon)
            return FogRooms();
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
        if (MapCovered || PendingEncounter != null || here == null || next == null || !next.Open || !here.Links.Contains(roomId))
            return false;
        Walk(CostMove * TerritoryClock.StepMinutes);
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
        CheckDungeonRoom(here.Id, roomId);
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
