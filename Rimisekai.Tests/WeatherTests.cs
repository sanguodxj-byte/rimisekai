using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Character;
using Rimisekai.Clock;
using Rimisekai.Defs;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Save;
using Rimisekai.Voice;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 天气系统：马尔可夫演化（季节状态隔离、持续时长）、恶劣天气判定、
/// 衣着干湿、赶路体力与露天作业减速。
/// </summary>
public sealed class WeatherTests
{
    // ---------- 马尔可夫演化 ----------

    [Fact]
    public void Advance_StaysWithinSeasonStateSpace()
    {
        var rng = new Random(7);
        foreach (var season in new[] { Season.Spring, Season.Summer, Season.Autumn, Season.Winter })
        {
            var weather = Weather.Clear;
            for (var i = 0; i < 5000; i++)
            {
                weather = WorldEffects.Advance(weather, season, rng);
                Assert.True(WorldEffects.Belongs(weather, season),
                    $"{season} 出现了不属于该季的天气 {weather}");
            }
        }
    }

    [Fact]
    public void Advance_WinterNeverRains_SummerNeverSnows()
    {
        var rng = new Random(11);
        var winter = Weather.Cloud;
        var summer = Weather.Clear;
        for (var i = 0; i < 20000; i++)
        {
            winter = WorldEffects.Advance(winter, Season.Winter, rng);
            summer = WorldEffects.Advance(summer, Season.Summer, rng);
            Assert.False(winter is Weather.Rain or Weather.HeavyRain or Weather.Thunder);
            Assert.False(summer is Weather.Snow or Weather.HeavySnow or Weather.Blizzard);
        }
    }

    [Fact]
    public void Advance_ClearSpellsLastAroundTwoDays()
    {
        // 晴的持续概率 0.97/小时：均值约 33 小时。统计拼写长度必须在半期内。
        var rng = new Random(5);
        var spell = 0;
        var total = 0;
        var count = 0;
        var weather = Weather.Clear;
        for (var i = 0; i < 100000; i++)
        {
            var next = WorldEffects.Advance(weather, Season.Autumn, rng);
            if (next == weather)
            {
                spell++;
                continue;
            }
            if (weather == Weather.Clear && spell > 0)
            {
                total += spell;
                count++;
            }
            weather = next;
            spell = 0;
        }
        var mean = (double)total / count;
        Assert.InRange(mean, 16, 66);
    }

    [Fact]
    public void Advance_SummerRainFamilyOutweighsAutumn()
    {
        // 现实标定：夏季雨族占近半，秋季秋晴偏多、雨族约两成。
        Assert.True(SteadyRainShare(Season.Summer, new Random(3)) > 0.30);
        Assert.True(SteadyRainShare(Season.Autumn, new Random(3)) < 0.30);
    }

    [Fact]
    public void Advance_OutOfSeasonWeatherResetsToCloud()
    {
        var rng = new Random(9);
        Assert.Equal(Weather.Cloud, WorldEffects.Advance(Weather.Rain, Season.Winter, rng));
        Assert.Equal(Weather.Cloud, WorldEffects.Advance(Weather.Blizzard, Season.Summer, rng));
    }

    private static double SteadyRainShare(Season season, Random rng)
    {
        var weather = Weather.Clear;
        var rain = 0;
        const int steps = 60000;
        for (var i = 0; i < steps; i++)
        {
            weather = WorldEffects.Advance(weather, season, rng);
            if (weather is Weather.Rain or Weather.HeavyRain or Weather.Thunder)
                rain++;
        }
        return (double)rain / steps;
    }

    // ---------- 判定表 ----------

    [Theory]
    [InlineData(Weather.Clear, false)]
    [InlineData(Weather.Cloud, false)]
    [InlineData(Weather.Rain, false)]
    [InlineData(Weather.Snow, false)]
    [InlineData(Weather.HeavyRain, true)]
    [InlineData(Weather.Thunder, true)]
    [InlineData(Weather.Wind, true)]
    [InlineData(Weather.HeavySnow, true)]
    [InlineData(Weather.Blizzard, true)]
    public void IsSevere_MatchesTheFiveSevereKinds(Weather weather, bool severe) =>
        Assert.Equal(severe, WorldEffects.IsSevere(weather));

    [Theory]
    [InlineData(Weather.Rain, 2)]
    [InlineData(Weather.HeavyRain, 4)]
    [InlineData(Weather.Thunder, 4)]
    [InlineData(Weather.Clear, 0)]
    [InlineData(Weather.Cloud, 0)]
    [InlineData(Weather.Snow, 0)]
    [InlineData(Weather.Blizzard, 0)]
    public void WetRatePerTick_FollowsTable(Weather weather, int rate) =>
        Assert.Equal(rate, WorldEffects.WetRatePerTick(weather));

    // ---------- 衣着干湿与赶路 ----------

    [Fact]
    public void SettleWetness_OutdoorRainSoaksWithoutGear()
    {
        var territory = OutdoorRoom();
        var who = new CharacterState(1);
        for (var i = 0; i < Vitals.WetMax / WorldEffects.WetRatePerTick(Weather.Rain); i++)
            WorldEffects.SettleWetness(who, territory, 1, Weather.Rain);
        Assert.True(who.Condition.Soaked);
    }

    [Fact]
    public void SettleWetness_RainGearKeepsDry()
    {
        var territory = OutdoorRoom();
        var umbrella = new CharacterState(1);
        umbrella.Bag.Add(WorldEffects.UmbrellaItemId, 1);
        var coat = new CharacterState(2);
        coat.Bag.Add(WorldEffects.RaincoatItemId, 1);
        for (var i = 0; i < 60; i++)
        {
            WorldEffects.SettleWetness(umbrella, territory, 1, Weather.Thunder);
            WorldEffects.SettleWetness(coat, territory, 1, Weather.HeavyRain);
        }
        Assert.Equal(0, umbrella.Condition.Wetness);
        Assert.Equal(0, coat.Condition.Wetness);
    }

    [Fact]
    public void SettleWetness_IndoorDries()
    {
        var territory = new Territory();
        territory.AddRoom(new Room { Id = 1, Name = "客厅", Open = true });
        territory.Rooms[0].AddTag("室内");
        var who = new CharacterState(1);
        who.Condition.Soak(Vitals.WetMax);
        for (var i = 0; i < 20; i++)
            WorldEffects.SettleWetness(who, territory, 1, Weather.Rain);
        Assert.Equal(Vitals.WetMax - 20, who.Condition.Wetness);
    }

    [Fact]
    public void SpendMoveStamina_OnlySevereWeatherOutdoors()
    {
        var territory = OutdoorRoom();
        territory.AddRoom(new Room { Id = 2, Name = "堂屋", Open = true });
        territory.Rooms[1].AddTag("室内");
        var who = new CharacterState(1);

        WorldEffects.SpendMoveStamina(who, territory, 1, Weather.Rain);
        Assert.Equal(who.Combat.MaxHp, who.Condition.Stamina);
        WorldEffects.SpendMoveStamina(who, territory, 2, Weather.Blizzard);
        Assert.Equal(who.Combat.MaxHp, who.Condition.Stamina);
        WorldEffects.SpendMoveStamina(who, territory, 1, Weather.Blizzard);
        Assert.Equal(who.Combat.MaxHp - WorldEffects.SevereMoveStamina, who.Condition.Stamina);
        WorldEffects.SpendMoveStamina(null!, territory, 1, Weather.Blizzard);
    }

    [Fact]
    public void Player_MoveIntoOutdoorUnderBlizzard_SpendsStamina()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        LinkOutdoor(state.Territory);
        state.Weather = Weather.Blizzard;
        var hub = new HubSession(state);
        hub.Enter(2);

        Assert.True(hub.Move(1));
        Assert.Equal(state.Roster.Master!.Combat.MaxHp - WorldEffects.SevereMoveStamina,
            state.Roster.Master.Condition.Stamina);
    }

    // ---------- 露天作业减速（用矿脉，与耕地无关） ----------

    [Fact]
    public void OutdoorWork_ProgressesAtHalfRateUnderSevereWeather()
    {
        var clear = MineWorkRun(Weather.Clear, placeIndoor: false);
        var severe = MineWorkRun(Weather.Blizzard, placeIndoor: false);
        Assert.Equal(90, clear.Progress);
        Assert.Equal(45, severe.Progress);
    }

    [Fact]
    public void OutdoorWorker_SoaksWithoutGear_UmbrellaBlocks()
    {
        var rain = MineWorkRun(Weather.Rain, placeIndoor: false);
        Assert.Equal(90, rain.Progress);
        Assert.Equal(WorldEffects.WetRatePerTick(Weather.Rain) * 9, rain.Worker.Condition.Wetness);

        var withGear = MineWorkRun(Weather.Rain, placeIndoor: false, gear: true);
        Assert.Equal(0, withGear.Worker.Condition.Wetness);
    }

    [Fact]
    public void Worker_WalksIntoOutdoorUnderBlizzard_SpendsStamina()
    {
        var run = MineWorkRun(Weather.Blizzard, placeIndoor: true);
        // 第一格走进室外房间扣体力，其余 8 格露天干活进度减半。
        Assert.Equal(run.Worker.Combat.MaxHp - WorldEffects.SevereMoveStamina, run.Worker.Condition.Stamina);
        Assert.Equal(40, run.Progress);
    }

    [Fact]
    public void IndoorWork_KeepsFullRateUnderSevereWeather()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var worker = state.Roster.Add("工");
        worker[CoreStat.Strength] = ActionKindMap.SpeedBaseline;
        worker.Affect.LastMealDay = 1;
        worker.Affect.LastMealWindow = 1;
        var territory = state.Territory;
        territory.AddRoom(new Room { Id = 1, Name = "工房", Open = true });
        territory.Rooms[0].AddTag("室内");
        territory.AddFacility(new Facility
        {
            Id = 1, Name = "矿脉", RoomId = 1, Usage = FacilityUsage.Plain, Capacity = 2,
            YieldItemId = "铁矿", Built = true, Actions = { ActionKind.Mine },
        });
        territory.Assign(worker.Id, 2, SlotMode.Work, 1);
        state.Weather = Weather.Blizzard;
        state.Clock.SetTime(1, 14 * 60);

        var hub = new HubSession(state);
        hub.Place(worker.Id, 1);
        hub.PassTime(45);

        Assert.Equal(90, hub.Day.Workers.First(w => w.CharacterId == worker.Id).Progress);
    }

    [Fact]
    public void Player_OutdoorWorkTakesDoubleTimeUnderSevereWeather()
    {
        var clear = PlayerMineMinutes(Weather.Clear);
        var severe = PlayerMineMinutes(Weather.Blizzard);
        Assert.Equal(2 * clear, severe);
    }

    [Fact]
    public void SaveRoundtrip_KeepsWetness()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        state.Roster.Master!.Condition.Soak(40);
        var hub = new HubSession(state);

        var loaded = SaveSystem.Load(SaveSystem.Save(state, hub));
        Assert.Equal(40, loaded.Roster.Master!.Condition.Wetness);
    }

    [Fact]
    public void VoiceGate_SoakedFiltersByCondition()
    {
        var soaked = new CharacterState(1);
        soaked.Condition.Soak(Vitals.WetMax);
        var dry = new CharacterState(2);
        var wetOnly = new VoiceGate { Soaked = true };
        var dryOnly = new VoiceGate { Soaked = false };

        Assert.True(wetOnly.Allows(Ctx(soaked), "l"));
        Assert.False(wetOnly.Allows(Ctx(dry), "l"));
        Assert.False(dryOnly.Allows(Ctx(soaked), "l"));
        Assert.True(dryOnly.Allows(Ctx(dry), "l"));
    }

    [Fact]
    public void RainGear_IsRegisteredAsTradables()
    {
        DefLoader.EnsureInitialized();
        var territory = new Territory();
        Assert.NotNull(territory.Listing(WorldEffects.UmbrellaItemId));
        Assert.NotNull(territory.Listing(WorldEffects.RaincoatItemId));
    }

    [Fact]
    public void Season_LengthIsFifteenDays()
    {
        var clock = new GameClock();
        clock.SetTime(15, 100);
        Assert.Equal(Season.Spring, clock.Season);
        clock.SetTime(16, 100);
        Assert.Equal(Season.Summer, clock.Season);
        clock.SetTime(46, 100);
        Assert.Equal(Season.Winter, clock.Season);
        clock.SetTime(61, 100);
        Assert.Equal(Season.Spring, clock.Season);
        Assert.Equal(2, clock.Year);
    }

    // ---------- 搭建 ----------

    private static Territory OutdoorRoom()
    {
        var territory = new Territory();
        territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        territory.Rooms[0].AddTag("室外");
        return territory;
    }

    private static void LinkOutdoor(Territory territory)
    {
        territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true, X = 0, Y = 0 });
        territory.Rooms[0].AddTag("室外");
        territory.AddRoom(new Room { Id = 2, Name = "客厅", Open = true, X = 1, Y = 0 });
        territory.Rooms[1].AddTag("室内");
        territory.Link(1, 2);
    }

    /// <summary>
    /// 让一名矿工干挖矿 45 分钟（9 格，进度不跨完成线，不触及配方与产量）。
    /// placeIndoor 为真时工人从相邻室内房间出发（第一格用于赶路）；
    /// gear 为真时给他一把伞。返回结束时的工作进度与工人状态。
    /// </summary>
    private static (int Progress, CharacterState Worker) MineWorkRun(
        Weather weather, bool placeIndoor, bool gear = false)
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var worker = state.Roster.Add("工");
        // 技能补到基线：速度系数回到 100%，进度断言不掺技能修正。
        worker[CoreStat.Strength] = ActionKindMap.SpeedBaseline;
        // 标记午餐已用过：不背三餐逾期的心情惩罚，效率保持 100%。
        worker.Affect.LastMealDay = 1;
        worker.Affect.LastMealWindow = 1;
        if (gear)
            worker.Bag.Add(WorldEffects.UmbrellaItemId, 1);
        var territory = state.Territory;
        LinkOutdoor(territory);
        territory.AddFacility(new Facility
        {
            Id = 1, Name = "矿脉", RoomId = 1, Usage = FacilityUsage.Plain, Capacity = 2,
            YieldItemId = "铁矿", Built = true, Actions = { ActionKind.Mine },
        });
        territory.Assign(worker.Id, 2, SlotMode.Work, 1);
        state.Weather = weather;
        state.Clock.SetTime(1, 14 * 60);

        var hub = new HubSession(state);
        hub.Place(worker.Id, placeIndoor ? 2 : 1);
        hub.Day.Rng = new Random(5);
        hub.PassTime(45);
        var done = hub.Day.Workers.First(w => w.CharacterId == worker.Id);
        return (done.Progress, worker);
    }

    /// <summary>玩家坐在室外矿脉上挖一次矿花掉的分钟数。</summary>
    private static int PlayerMineMinutes(Weather weather)
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        LinkOutdoor(state.Territory);
        state.Territory.AddFacility(new Facility
        {
            Id = 1, Name = "矿脉", RoomId = 1, Usage = FacilityUsage.Plain, Capacity = 2,
            YieldItemId = "铁矿", Built = true, Actions = { ActionKind.Mine },
        });
        state.Weather = weather;
        state.Clock.SetTime(1, 14 * 60);

        var hub = new HubSession(state);
        hub.Enter(1);
        Assert.True(hub.Use(1));
        var before = state.Clock.Minutes;
        Assert.True(hub.ActAtFixture(ActionKind.Mine));
        return state.Clock.Minutes - before;
    }

    private static VoiceContext Ctx(CharacterState who) =>
        VoiceContext.For(who, VoiceTrigger.Meet, -1, Season.Spring, Weather.Rain, 1, 10 * 60);
}
