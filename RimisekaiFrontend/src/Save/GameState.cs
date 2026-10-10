using System;
using System.Collections.Generic;
using Rimisekai.Catalog;
using Rimisekai.Character;
using Rimisekai.Clock;
using Rimisekai.Housing;
using Rimisekai.Quest;
using Rimisekai.WorldMap;
using Rimisekai.PoiMap;
using Rimisekai.WorldMap.Generators;
using Rimisekai.PoiMap.Generator;

namespace Rimisekai.Save;

public readonly record struct DaySummary(bool SeasonChanged, Season Season, Weather Weather);

/// <summary>日终事件钩子。返回一行要写入日志的文本，无事返回空。</summary>
public interface IDayEvent
{
    string? Run(GameState state, DaySummary summary);
}

/// <summary>
/// 一份可存档的世界。会话（任务中、战斗中）不进这里，结束时把结果写回。
/// 任务定义是数据，不存档；读档后由数据初始化重新注册。
/// </summary>
public sealed class GameState
{
    public GameClock Clock { get; } = new();
    public Roster Roster { get; } = new();
    public QuestRecord Quests { get; } = new();

    /// <summary>已演过的整局一次事件 Id（Once 语义跨存档生效）。</summary>
    public HashSet<string> FiredEvents { get; } = new();
    public Territory Territory { get; } = new();
    public GameCatalog Catalog { get; } = new();

    /// <summary>
    /// 运行时生成的武器实例。武器不是注册表里的一行——
    /// 材料 × 类型只是基座，每件实例的强化/附魔/祝福/稀有度都是生成时定的。
    /// 登记表挂在 Territory 上（报价要按实例字段算），这里只是同一份的别名。
    /// </summary>
    public Defs.WeaponRegistry Weapons => Territory.Weapons;

    /// <summary>防具与饰品实例（同一份登记表的别名）。</summary>
    public Defs.EquipRegistry Equips => Territory.Equips;
    public long Money { get; set; }
    public int Prestige { get; set; }
    public Weather Weather { get; set; } = Weather.Clear;

    /// <summary>队伍此刻是否刚从战斗胜利归来（睡觉结算后重置）。苛刻剧情使用。</summary>
    public bool ReturnedFromCombat { get; set; }
    public List<IDayEvent> DayEvents { get; } = new();

    /// <summary>当前大世界地图数据。</summary>
    public WorldMapData World { get; set; }

    /// <summary>大地图行进：队伍此刻站的格。随 <see cref="World"/> 一起重建，不进存档。</summary>
    public WorldParty Party { get; set; }

    /// <summary>地城进度：守卫、宝库、神龛与首领的了结记录。随存档走，换世界即清空。</summary>
    public DungeonLedger Dungeons { get; private set; } = new();

    /// <summary>当前探索/驻留的 POI 场景（若有）。</summary>
    public PoiMapData? CurrentPoi { get; set; }

    public int WorldSeed { get; set; } = 42;

    public GameState()
    {
        World = WorldGenerator.Generate(WorldSeed);
        Party = new WorldParty(World);
        // 开局先掷一次首日行情，否则第一天集市全数无货。
        Territory.RollMarketDay(new Random(WorldSeed));
    }

    /// <summary>换一个世界种子重新生成大世界（新开局时掷一次；领地选址随之而定）。</summary>
    public void RegenerateWorld(int seed)
    {
        WorldSeed = seed == 0 ? 42 : seed;
        World = WorldGenerator.Generate(WorldSeed);
        Party = new WorldParty(World);
        Dungeons = new DungeonLedger();
        CurrentPoi = null;
    }

    /// <summary>
    /// 进入某个 POI 场景（根据 POI 规模从数据表匹配 5x5 拼合房间）。
    /// </summary>
    public PoiMapData EnterPoi(int poiId, int blocksW = 0, int blocksH = 0)
    {
        var poiSeed = WorldSeed ^ (poiId * 31337);
        var poi = World.Pois.Find(p => p.Id == poiId);
        var poiMap = poi is { Type: WorldPoiType.Ruin } && blocksW == 0 && blocksH == 0
            ? GenerateDungeon(poiSeed)
            : (poi != null && blocksW == 0 && blocksH == 0)
                ? PoiAssemblyGenerator.GenerateForPoi(poi.Type, poiSeed)
                : PoiAssemblyGenerator.GenerateGrid(poiSeed, blocksW > 0 ? blocksW : 1, blocksH > 0 ? blocksH : 1);
        CurrentPoi = poiMap;
        return poiMap;
    }

    /// <summary>委托地城：按种子现生成一座地城，作为当前场景。</summary>
    public PoiMapData EnterQuestDungeon(int seed)
    {
        CurrentPoi = GenerateDungeon(seed);
        return CurrentPoi;
    }

    /// <summary>地城：按种子从数据表的块拼法里挑一式，每块都是遗迹地牢。</summary>
    private static PoiMapData GenerateDungeon(int poiSeed)
    {
        var (_, _, districts) = MapCatalog.Default.GetPoiScale(WorldPoiType.Ruin);
        var layouts = MapCatalog.Default.Dungeon.Layouts;
        var layout = layouts[(int)((uint)poiSeed % (uint)layouts.Count)];
        var blocks = new List<(int bx, int by)>();
        var names = new List<string>();
        foreach (var cell in layout)
        {
            blocks.Add((cell[0], cell[1]));
            names.Add(districts[0]);
        }
        return PoiAssemblyGenerator.GenerateCustom(poiSeed, blocks, districts: names);
    }

    /// <summary>
    /// 口上/地文的调度。台词是数据，读档后由内容包重新灌入，
    /// 与任务定义一样不进存档；进存档的只有各角色说过什么（CharacterState.Voice）。
    /// </summary>
    public Voice.VoiceDirector Voice { get; } = new();

    /// <summary>
    /// 定时场景各行生成出来的成品正文。内容表只给骨架与指令，
    /// 正文由后台生成后落在这里，随存档走——读档后不必重烧一遍 token。
    /// </summary>
    public Voice.SceneTextStore SceneTexts { get; } = new();

    public DaySummary EndDay(Random? random = null)
    {
        var before = Clock.Season;
        Clock.SkipToNextDay();
        return SettleDay(before, random);
    }

    /// <summary>
    /// 结算刚结束的一天：全员恢复、任务推进、作物生长。
    /// 天气不在此定时重掷——它随时间按小时马尔可夫演化（见 WorldEffects.Advance）。
    /// 不动时钟——时钟自行跨过午夜时由推进方调用
    /// （<see cref="Hub.HubSession.PassTime"/>）；显式跳天走 <see cref="EndDay"/>。
    /// </summary>
    public DaySummary SettleDay(Season seasonBefore, Random? random = null)
    {
        foreach (var c in Roster.Members)
        {
            c.RestoreBase();
            c.Condition.RecoverFull();
            if (c.FactionId == PlayerFaction)
                c.EmploymentDays++;
        }
        Quests.TickDay();
        // 作物逐日生长：当季 +1，非当季暂停不枯。
        foreach (var f in Territory.Facilities)
        {
            if (!f.Built || f.CropDefName.Length == 0)
                continue;
            var crop = Defs.DefDatabase<Defs.CropDef>.Get(f.CropDefName);
            if (crop != null && crop.GrowsIn(Clock.Season))
                f.Growth++;
        }
        // 0 点集市重掷：今日货品与价格系数。
        Territory.RollMarketDay(random ?? new Random());
        // 井水每日回满：取水与移动等价，不是工作，井就是现成的水源。
        Territory.TopUpWells();
        return new DaySummary(Clock.Season != seasonBefore, Clock.Season, Weather);
    }

    public const int PlayerFaction = 1;
}
