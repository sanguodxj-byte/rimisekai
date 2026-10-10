using System.Collections.Generic;
using Rimisekai.Catalog;
using Rimisekai.Housing;
using Rimisekai.Session;
using Rimisekai.WorldMap;

namespace Rimisekai.Hub;

/// <summary>遭遇的来路：野外行进中撞上的，或地城里石室自带的。</summary>
public enum EncounterSource
{
    Wild,
    DungeonGuard,
    DungeonBoss,
    DungeonTreasure,
    DungeonShrine,
}

/// <summary>
/// 一次摆在眼前、等玩家拿主意的遭遇。有敌人＝战斗（迎战 / 绕开或退回），没有＝事件（一枚钮收下结果）。
/// 钱数在撞上时就掷定，弹窗里写的和落账的是同一个数。
/// </summary>
public sealed class Encounter
{
    public EncounterDef Def { get; init; } = null!;
    public EncounterSource Source { get; init; }
    public string Place { get; init; } = "";

    /// <summary>地城遭遇所在的地城编号（遗迹＝兴趣点编号）与生成器房号；野外为 -1。</summary>
    public int PoiId { get; init; } = -1;
    public int RoomId { get; init; } = -1;

    /// <summary>落账的钱（可负）。</summary>
    public int Money { get; init; }

    public bool IsBattle => Def.Foes.Count > 0;

    public string Title => Def.Title;

    /// <summary>弹窗正文：遭遇描写，事件再补一行结果。</summary>
    public string Body
    {
        get
        {
            var parts = new List<string> { Def.Text };
            if (Money > 0)
                parts.Add($"获得 {Money} 金币。");
            if (Money < 0)
                parts.Add($"失去 {-Money} 金币。");
            if (!IsBattle && Def.Minutes > 0)
                parts.Add($"耽搁了 {Def.Minutes} 分钟。");
            if (Def.Mood > 0)
                parts.Add($"心情 +{Def.Mood}。");
            if (IsBattle && Source == EncounterSource.Wild)
                parts.Add($"绕开要多走 {Def.DetourMinutes} 分钟。");
            return string.Join("\n", parts);
        }
    }

    /// <summary>迎战钮。</summary>
    public string FightLabel => "迎战";

    /// <summary>避战钮：野外是绕路（多花的时间写在正文里），地城是退回上一间石室。</summary>
    public string AvoidLabel => Source == EncounterSource.Wild ? "绕开" : "退回";

    /// <summary>事件钮。</summary>
    public string AcceptLabel => Def.AcceptLabel;
}

/// <summary>
/// 世界探索的遭遇：大地图上逐格行进时按地貌掷野外遭遇（离领地越远，越凶的东西越多），
/// 地城（遗迹）里踏进石室时按房型摆出守卫、首领、宝库与神龛。
/// 遭遇一出现行进就停下，等前端拿 <see cref="PendingEncounter"/> 弹窗、玩家选完再走。
/// </summary>
public sealed partial class HubSession
{
    /// <summary>眼前这桩还没了结的遭遇。</summary>
    public Encounter? PendingEncounter { get; private set; }

    /// <summary>野外遭遇率（百分比，默认 100）。出图与测试置 0 以免行进被随机遭遇打断。</summary>
    public int EncounterRate { get; set; } = 100;

    /// <summary>正在打的那场遭遇战（迎战后到结算前）。</summary>
    private Encounter? _fighting;

    /// <summary>地城里踏进当前石室之前站的那间（「退回」回这里）。</summary>
    private int _dungeonRetreatRoomId = -1;

    /// <summary>
    /// 危险等级 1–4：离领地每 20 格高一级。数据表里每条遭遇的 minTier 据此筛。
    /// </summary>
    public int DangerTier(int x, int y)
    {
        var dx = x - State.World.HomeX;
        var dy = y - State.World.HomeY;
        var distance = (int)System.Math.Sqrt(dx * dx + dy * dy);
        return System.Math.Clamp(1 + distance / 20, 1, 4);
    }

    /// <summary>
    /// 地城里走远路：一间一间地走，每进一间都看有没有东西，撞上就停在那一间（返回 true，人已挪动）。
    /// </summary>
    private bool ArriveThroughDungeon(int roomId)
    {
        LeaveFixture();
        var from = PlayerRoomId;
        foreach (var step in State.Territory.Route(PlayerRoomId, roomId, r => RoomShown(r.Id), ignoreLocks: true))
        {
            Walk(CostMove * TerritoryClock.StepMinutes);
            Enter(step);
            CheckDungeonRoom(from, step);
            from = step;
            if (PendingEncounter != null)
                break;
        }
        WriteArrival(PlayerRoomId);
        return true;
    }

    /// <summary>首领已倒下的地城。</summary>
    public bool IsDungeonCleared(int poiId) => State.Dungeons.Cleared.Contains(poiId);

    /// <summary>
    /// 踏进大地图上一格后掷野外遭遇。领地周围、聚落格上不起遭遇；走在路上按 roadPercent 打折。
    /// 掷骰按（世界种子, 格点, 此刻）哈希，同一局同一时刻走同一格结果相同。撞上返回 true。
    /// </summary>
    private bool RollWildEncounter(int x, int y)
    {
        var wilds = MapCatalog.Default.Wilds;
        var tile = State.World.Tiles[x, y];
        var dx = x - State.World.HomeX;
        var dy = y - State.World.HomeY;
        if (EncounterRate <= 0 || tile.PoiId > 0 || dx * dx + dy * dy <= wilds.SafeRadius * wilds.SafeRadius)
            return false;
        var terrain = tile.IsRiver ? WorldTerrainType.River : tile.Terrain;
        var permille = MapCatalog.Default.GetEncounterPermille(terrain) * EncounterRate / 100;
        if (tile.IsRoad)
            permille = permille * wilds.RoadPercent / 100;
        var roll = Roll(x + State.WorldSeed * 31, y, State.Clock.TotalMinutes);
        if (roll % 1000 >= (uint)permille)
            return false;
        var tier = DangerTier(x, y);
        var pool = wilds.Events.FindAll(e => e.MinTier <= tier
            && (e.Terrains.Count == 0 || e.Terrains.Contains(terrain.ToString())));
        if (pool.Count == 0)
            return false;
        var def = PickWeighted(pool, roll >> 10);
        PendingEncounter = new Encounter
        {
            Def = def,
            Source = EncounterSource.Wild,
            Place = State.World.TileName(x, y),
            Money = RollMoney(def, roll >> 20),
        };
        Write($"行至{PendingEncounter.Place}，遇上了{def.Title}。");
        return true;
    }

    /// <summary>
    /// 地城里踏进一间石室：首领房（全图出口）、宝库、神龛各有其事；普通石室按 guardPercent 有守卫。
    /// 已了结的石室不再起事；首领倒下的地城只剩宝库神龛还没动过的那些。
    /// </summary>
    private void CheckDungeonRoom(int fromRoomId, int roomId)
    {
        if (!InDungeon)
            return;
        var run = _dungeon!;
        var genId = roomId - PoiRoomIdBase;
        var room = State.CurrentPoi.GetRoom(genId)!;
        VisitDungeonRoom(roomId);
        if (room.IsStart || run.Ledger.IsSpent(run.Key, genId))
            return;
        var dungeon = MapCatalog.Default.Dungeon;
        var cleared = run.Ledger.Cleared.Contains(run.Key);
        var roll = Roll(run.Key, genId, run.Seed);
        EncounterDef? def = null;
        var source = EncounterSource.DungeonGuard;
        if (room.IsEnd)
        {
            if (cleared)
                return;
            def = run.Boss ?? PickByTier(dungeon.Bosses, run.Tier);
            source = EncounterSource.DungeonBoss;
        }
        else if (room.RoomType == PoiMap.PoiRoomType.Treasure)
        {
            def = dungeon.Treasure;
            source = EncounterSource.DungeonTreasure;
        }
        else if (room.RoomType == PoiMap.PoiRoomType.Shrine)
        {
            def = dungeon.Shrine;
            source = EncounterSource.DungeonShrine;
        }
        else if (!cleared && roll % 100 < (uint)dungeon.GuardPercent)
        {
            def = PickByTier(dungeon.Guards, run.Tier);
        }
        if (def == null)
            return;
        _dungeonRetreatRoomId = fromRoomId;
        PendingEncounter = new Encounter
        {
            Def = def,
            Source = source,
            Place = room.Name,
            PoiId = run.Key,
            RoomId = genId,
            Money = RollMoney(def, roll >> 8),
        };
        if (PendingEncounter.IsBattle)
            Write($"{room.Name}里，{def.Title}挡住了去路。");
    }

    /// <summary>事件遭遇：照单落账（钱、耗时、同行者心情），了结。</summary>
    public void AcceptEncounter()
    {
        var e = PendingEncounter;
        if (e == null || e.IsBattle)
            return;
        PendingEncounter = null;
        State.Money = System.Math.Max(0, State.Money + e.Money);
        if (e.Def.Minutes > 0)
            PassTime(e.Def.Minutes);
        if (e.Def.Mood != 0)
            foreach (var id in WorldPartyIds())
                State.Roster.Find(id)?.Affect.AddMood(e.Def.Mood);
        if (e.Source != EncounterSource.Wild)
            _dungeon!.Ledger.Spend(e.PoiId, e.RoomId);
        Write(e.Money > 0 ? $"{e.Title}：得了 {e.Money} 金币。"
            : e.Money < 0 ? $"{e.Title}：破了 {-e.Money} 金币。"
            : $"{e.Title}。");
    }

    /// <summary>避战：野外绕路多走一段（照样耗时），地城退回上一间石室。</summary>
    public void AvoidEncounter()
    {
        var e = PendingEncounter;
        if (e == null || !e.IsBattle)
            return;
        PendingEncounter = null;
        Retreat(e);
    }

    private void Retreat(Encounter e)
    {
        if (e.Source == EncounterSource.Wild)
        {
            PassTime(e.Def.DetourMinutes);
            Write($"绕开了{e.Title}。");
            return;
        }
        Enter(_dungeonRetreatRoomId);
        Write($"退出了{e.Place}。");
    }

    /// <summary>
    /// 迎战：同行者（玩家本人＋跟着的人）上阵，起一场战斗交给前端。
    /// 打完由 <see cref="SettleEncounterBattle"/> 收尾。
    /// </summary>
    public BattleSession? FightEncounter(GameCatalog? catalog = null)
    {
        var e = PendingEncounter;
        if (e == null || !e.IsBattle)
            return null;
        var session = Encounters.Start(State, e.Def.Foes, catalog, e.Place, WorldPartyIds());
        if (session == null)
            return null;
        if (e.Source == EncounterSource.DungeonBoss)
            session.QuestRun = _dungeon!.Quest;
        PendingEncounter = null;
        _fighting = e;
        return session;
    }

    /// <summary>
    /// 遭遇战收尾（结算之后调）：胜——地城守卫房了结、首领倒下则地城肃清；
    /// 撤离或僵持——野外原地不动，地城退回上一间；败——众人狼狈退回领地。不是遭遇战返回 false。
    /// </summary>
    public bool SettleEncounterBattle(Combat.Battle battle)
    {
        var e = _fighting;
        if (e == null)
            return false;
        _fighting = null;
        switch (battle.Outcome)
        {
            case Combat.CombatOutcome.AttackerWin:
                if (e.Source == EncounterSource.Wild)
                    break;
                _dungeon!.Ledger.Spend(e.PoiId, e.RoomId);
                if (e.Source != EncounterSource.DungeonBoss)
                    break;
                if (InQuestDungeon)
                {
                    EndQuestDungeon(string.Format(MapCatalog.Default.Dungeon.Quest.DoneText, LayerPlaceName));
                    break;
                }
                _dungeon.Ledger.Cleared.Add(e.PoiId);
                Write(string.Format(MapCatalog.Default.Dungeon.ClearedText, LayerPlaceName));
                break;
            case Combat.CombatOutcome.DefenderWin:
                Write($"败给了{e.Title}，众人狼狈地退回领地。");
                SwitchToTerritory(safe: true);
                break;
            default:
                // 撤离或僵持：野外原地不动，地城退回上一间。
                if (e.Source != EncounterSource.Wild)
                    Retreat(e);
                break;
        }
        return true;
    }

    /// <summary>此刻在外的队伍：玩家本人＋跟着的人。</summary>
    public List<int> WorldPartyIds()
    {
        var ids = new List<int>();
        var master = State.Roster.Master;
        if (master != null)
            ids.Add(master.Id);
        foreach (var worker in Day.Workers)
            if (worker.FollowsPlayer && !ids.Contains(worker.CharacterId))
                ids.Add(worker.CharacterId);
        return ids;
    }

    /// <summary>调试 / 出图入口：按编号在队伍脚下摆一桩野外遭遇。</summary>
    public void ForceWildEncounter(string id)
    {
        var def = MapCatalog.Default.Wilds.Events.Find(e => e.Id == id)!;
        PendingEncounter = new Encounter
        {
            Def = def,
            Source = EncounterSource.Wild,
            Place = State.World.TileName(Trek.X, Trek.Y),
            Money = RollMoney(def, Roll(Trek.X, Trek.Y, State.Clock.TotalMinutes)),
        };
    }

    private static EncounterDef PickWeighted(List<EncounterDef> pool, uint roll)
    {
        var total = 0;
        foreach (var e in pool)
            total += e.Weight;
        var pick = (int)(roll % (uint)total);
        foreach (var e in pool)
        {
            pick -= e.Weight;
            if (pick < 0)
                return e;
        }
        return pool[^1];
    }

    /// <summary>挑够得着这一等级里门槛最高的那一档。</summary>
    private static EncounterDef PickByTier(List<EncounterDef> defs, int tier)
    {
        EncounterDef? best = null;
        foreach (var d in defs)
            if (d.MinTier <= tier && (best == null || d.MinTier > best.MinTier))
                best = d;
        return best!;
    }

    private static int RollMoney(EncounterDef def, uint roll) =>
        def.MoneyMax > def.MoneyMin
            ? def.MoneyMin + (int)(roll % (uint)(def.MoneyMax - def.MoneyMin + 1))
            : def.Money;

    private static uint Roll(int a, int b, int c)
    {
        unchecked
        {
            var h = (uint)(a * 374761393 + b * 668265263 + c * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            h *= 2246822519u;
            return h ^ (h >> 13);
        }
    }
}
