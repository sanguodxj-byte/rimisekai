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
    public Territory Territory { get; } = new();
    public GameCatalog Catalog { get; } = new();
    public long Money { get; set; }
    public int Prestige { get; set; }
    public Weather Weather { get; set; } = Weather.Clear;
    public List<IDayEvent> DayEvents { get; } = new();

    /// <summary>当前大世界地图数据。</summary>
    public WorldMapData World { get; set; }

    /// <summary>当前探索/驻留的 POI 场景（若有）。</summary>
    public PoiMapData? CurrentPoi { get; set; }

    public int WorldSeed { get; set; } = 42;

    public GameState()
    {
        World = WorldGenerator.Generate(WorldSeed, 128, 128);
    }

    /// <summary>
    /// 进入某个 POI 场景（根据 POI 规模从数据表匹配 5x5 拼合房间）。
    /// </summary>
    public PoiMapData EnterPoi(int poiId, int blocksW = 0, int blocksH = 0)
    {
        var poiSeed = WorldSeed ^ (poiId * 31337);
        var poi = World.Pois.Find(p => p.Id == poiId);
        var poiMap = (poi != null && blocksW == 0 && blocksH == 0)
            ? PoiAssemblyGenerator.GenerateForPoi(poi.Type, poiSeed)
            : PoiAssemblyGenerator.GenerateGrid(poiSeed, blocksW > 0 ? blocksW : 1, blocksH > 0 ? blocksH : 1);
        CurrentPoi = poiMap;
        return poiMap;
    }

    /// <summary>
    /// 口上/地文的调度。台词是数据，读档后由内容包重新灌入，
    /// 与任务定义一样不进存档；进存档的只有各角色说过什么（CharacterState.Voice）。
    /// </summary>
    public Voice.VoiceDirector Voice { get; } = new();

    public DaySummary EndDay(Random? random = null)
    {
        var before = Clock.Season;
        Clock.SkipToNextDay();
        foreach (var c in Roster.Members)
        {
            c.RestoreBase();
            c.Condition.RecoverFull();
            if (c.FactionId == PlayerFaction)
                c.EmploymentDays++;
        }
        Quests.TickDay();
        Weather = WorldEffects.RollWeather(Clock.Season, random);
        return new DaySummary(Clock.Season != before, Clock.Season, Weather);
    }

    public const int PlayerFaction = 1;
}
