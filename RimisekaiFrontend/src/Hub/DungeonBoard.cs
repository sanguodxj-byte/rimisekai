using System.Collections.Generic;
using Rimisekai.Housing;
using Rimisekai.Quest;
using Rimisekai.WorldMap;

namespace Rimisekai.Hub;

/// <summary>
/// 地城的会话侧：迷雾（走过的石室全亮，与之有门相通的只望见个轮廓，其余漆黑）
/// 与委托地城（接下地图类委托，马车送进一座现生成的地城，正主倒下即了结接回）。
/// 遗迹与委托共用同一套石室遭遇（<see cref="CheckDungeonRoom"/>）。
/// </summary>
public sealed partial class HubSession
{
    /// <summary>此刻身在的这座地城。</summary>
    private sealed class DungeonRun
    {
        /// <summary>地城编号：遗迹是兴趣点编号，委托地城是 <see cref="QuestDungeonKey"/>。</summary>
        public int Key { get; init; }
        public int Seed { get; init; }
        public int Tier { get; init; }
        public DungeonLedger Ledger { get; init; } = null!;

        /// <summary>委托地城：委托运行时与最深处的正主；遗迹为空。</summary>
        public QuestRun? Quest { get; init; }
        public EncounterDef? Boss { get; init; }

        /// <summary>为委托临时拉进队伍的人（原本没跟着你），接回领地时各回各的日子。</summary>
        public List<int> Recruited { get; } = new();
    }

    /// <summary>委托地城一趟一本账，用不到兴趣点编号段。</summary>
    private const int QuestDungeonKey = -1;

    private DungeonRun? _dungeon;

    /// <summary>此刻是否身在地城（遗迹或委托地城）里。</summary>
    private bool InDungeon => _dungeon != null;

    /// <summary>此刻是否在跑委托地城。</summary>
    public bool InQuestDungeon => _dungeon?.Quest != null;

    private bool Visited(int roomId) =>
        _dungeon!.Ledger.IsVisited(_dungeon.Key, roomId - PoiRoomIdBase);

    /// <summary>迷雾里看得见：走过的，或与走过的有门相通的。</summary>
    private bool Known(int roomId)
    {
        if (Visited(roomId))
            return true;
        var room = Room(roomId);
        return room != null && room.Links.Exists(Visited);
    }

    /// <summary>格上画不画这间房：地城里只画看得见的，别处全画。</summary>
    public bool RoomShown(int roomId) => !InDungeon || Known(roomId);

    /// <summary>两间房之间的门画不画：地城里至少一边走过、两边都看得见才画。</summary>
    public bool DoorShown(int a, int b) =>
        !InDungeon || (Known(a) && Known(b) && (Visited(a) || Visited(b)));

    private List<Room>? _fogRooms;
    private (int Region, int Visited) _fogKey = (-1, -1);

    /// <summary>
    /// 地城里交给地图格的房间：走过的照原样；望见的换成只写「？」、只连着走过的那几道门的影子；漆黑的不给。
    /// </summary>
    private List<Room> FogRooms()
    {
        var key = (RegionId, _dungeon!.Ledger.VisitedRooms.Count);
        if (_fogRooms != null && _fogKey == key)
            return _fogRooms;
        var list = new List<Room>();
        foreach (var room in State.Territory.Rooms)
        {
            if (room.RegionId != RegionId || !Known(room.Id))
                continue;
            if (Visited(room.Id))
            {
                list.Add(room);
                continue;
            }
            var veiled = new Room
            {
                Id = room.Id,
                Name = MapCatalog.Default.Dungeon.FogName,
                RegionId = room.RegionId,
                X = room.X,
                Y = room.Y,
                Open = room.Open,
            };
            veiled.Links.AddRange(room.Links.FindAll(Visited));
            veiled.EnsureDefaultTag();
            list.Add(veiled);
        }
        _fogRooms = list;
        _fogKey = key;
        return list;
    }

    /// <summary>踏进地城里的一间：记作走过，迷雾随之揭开。</summary>
    private void VisitDungeonRoom(int roomId) =>
        _dungeon!.Ledger.Visit(_dungeon.Key, roomId - PoiRoomIdBase);

    /// <summary>
    /// 把生成的场景装进领地表：房号加 <see cref="PoiRoomIdBase"/>、区号从 <see cref="Territory.MaxTerritoryRegions"/> 起，
    /// 借用区号（离开时由 <see cref="LeavePoi"/> 整片撤掉并还原）。返回正门的房号。
    /// </summary>
    private int ImportScene(PoiMap.PoiMapData scene)
    {
        _unlockedBeforePoi = State.Territory.UnlockedRegions;
        State.Territory.SetUnlockedRegions(System.Math.Max(State.Territory.UnlockedRegions,
            Territory.MaxTerritoryRegions + scene.Blocks.Count));
        foreach (var r in scene.ExportToHousingRooms())
        {
            var block = scene.GetBlockByRoomId(r.Id)!;
            var room = new Room
            {
                Id = PoiRoomIdBase + r.Id,
                Name = r.Name,
                RegionId = Territory.MaxTerritoryRegions + block.RegionId,
                X = r.X,
                Y = r.Y,
                Open = r.Open,
                OpenCost = r.OpenCost,
            };
            foreach (var link in r.Links)
                room.Links.Add(PoiRoomIdBase + link);
            room.EnsureDefaultTag();
            if (!State.Territory.AddRoom(room))
                throw new System.InvalidOperationException($"场景房 {room.Id} 装不进领地表。");
        }
        return PoiRoomIdBase + scene.Blocks[0].StartRoom!.Id;
    }

    /// <summary>
    /// 接下地图类委托：人须在领地里。编成的人（玩家本人之外）一并跟上，马车把众人送进一座现生成的地城，
    /// 落在入口；地城危险等级按委托难度定，最深处是委托的正主。种子随通关次数变，每趟地城不一样。
    /// </summary>
    public bool StartQuestDungeon(QuestRun run)
    {
        if (Layer != MapLayer.Territory || PendingEncounter != null || PlayerRoomId < 0)
            return false;
        var def = run.Def;
        var quest = MapCatalog.Default.Dungeon.Quest;
        var seed = State.WorldSeed ^ (def.Id * 7919) ^ (State.Quests.ClearCount.GetValueOrDefault(def.Id) * 104729);
        var tier = QuestBoard.TierOf(def.Difficulty);
        _dungeon = new DungeonRun
        {
            Key = QuestDungeonKey,
            Seed = seed,
            Tier = tier,
            Ledger = new DungeonLedger(),
            Quest = run,
            Boss = new EncounterDef { Title = def.Name, Text = quest.BossText, Foes = def.Foes },
        };
        _territoryHomeRoomId = PlayerRoomId;
        var master = State.Roster.Master;
        foreach (var id in run.PartyIds)
        {
            if (master != null && id == master.Id)
                continue;
            var worker = Day.Track(id, PlayerRoomId);
            if (worker.FollowsPlayer)
                continue;
            worker.FollowsPlayer = true;
            _dungeon.Recruited.Add(id);
        }
        var start = ImportScene(State.EnterQuestDungeon(seed));
        LeaveTerritoryBody();
        SetLayer(MapLayer.QuestPlace, def.Name);
        Enter(start);
        VisitDungeonRoom(start);
        Write(string.Format(quest.ArriveText, def.Name));
        return true;
    }

    /// <summary>
    /// 委托地城收场（了结、战败或撤离）：马车把众人接回领地，落回出发前那间房；
    /// 为委托拉来的人各回各的日子。<paramref name="line"/> 为收场日志，空则不写。
    /// </summary>
    private void EndQuestDungeon(string line)
    {
        var run = _dungeon!;
        LeavePoi();
        SetLayer(MapLayer.Territory);
        _territoryHomeRoomId = -1;
        foreach (var worker in Day.Workers)
            if (run.Recruited.Contains(worker.CharacterId))
                EndFollow(worker);
        if (line.Length > 0)
            Write(line);
    }

    /// <summary>撤离委托地城：委托不算数，接回领地。</summary>
    public void AbandonQuestDungeon()
    {
        if (!InQuestDungeon || PendingEncounter != null)
            return;
        EndQuestDungeon(string.Format(MapCatalog.Default.Dungeon.Quest.AbandonText, LayerPlaceName));
    }

    /// <summary>为委托编进队伍的人：好感掉档也不中途离队。</summary>
    private bool OnQuestParty(int characterId) =>
        _dungeon?.Quest != null && _dungeon.Quest.PartyIds.Contains(characterId);

    /// <summary>地图下方出行钮的字：大地图上「返回领地」，委托地城里「撤离」，别处「出行」。</summary>
    public string TravelLabel => Layer switch
    {
        MapLayer.World => "返回领地",
        MapLayer.QuestPlace => MapCatalog.Default.Dungeon.Quest.LeaveLabel,
        _ => "出行",
    };
}
