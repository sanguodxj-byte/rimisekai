using System;
using System.Linq;
using Rimisekai.Character;
using Rimisekai.Clock;
using Rimisekai.Defs;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Save;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 作物系统：耕地识别、播种扣种、逐日生长与非季暂停、成熟收获、
/// NPC 选活过滤与玩家语义、地块存档。
/// </summary>
public sealed class CropTests
{
    private const int TillTicks = 8;

    [Fact]
    public void CropOf_MatchesByProduceItemId()
    {
        DefLoader.EnsureInitialized();
        Assert.NotNull(Plot().CropOf(Field()));
        Assert.Null(Plot().CropOf(new Facility { Id = 2, Name = "矿脉", YieldItemId = "铁矿" }));
    }

    [Fact]
    public void Sow_ConsumesSeedAndStartsGrowth()
    {
        var territory = Plot();
        var field = Field();
        var who = new CharacterState(1);
        who.Bag.Add("小麦种子", 2);

        Assert.Equal(Territory.FarmState.ReadySow, territory.PlotState(field, Season.Spring, who));

        var log = territory.FarmWork(who, field, ActionKind.Till, Season.Spring, null, out var handled);
        Assert.True(handled);
        Assert.NotNull(log);
        Assert.Equal(0, log!.Count);
        Assert.Equal(1, who.Bag.Get("小麦种子"));
        Assert.Equal("小麦", field.CropDefName);
        Assert.Equal(0, field.Growth);
        Assert.Equal(Territory.FarmState.Growing, territory.PlotState(field, Season.Spring, who));
    }

    [Fact]
    public void Sow_WithoutSeedAnywhere_IsRefused()
    {
        var territory = Plot();
        var field = Field();
        var who = new CharacterState(1);

        Assert.Equal(Territory.FarmState.NoSeed, territory.PlotState(field, Season.Spring, who));
        var log = territory.FarmWork(who, field, ActionKind.Till, Season.Spring, null, out var handled);
        Assert.True(handled);
        Assert.Null(log);
        Assert.Equal("", field.CropDefName);
    }

    [Fact]
    public void Sow_TakesSeedFromStorageWhenBagIsEmpty()
    {
        var territory = Plot();
        var field = Field();
        var who = new CharacterState(1);
        var barn = new Facility
        {
            Id = 3, Name = "库房", RoomId = 1, Built = true, CanStore = true,
            StorageCapacity = 0,
        };
        barn.Contents.Add("小麦种子", 1);
        territory.AddFacility(barn);

        Assert.Equal(Territory.FarmState.ReadySow, territory.PlotState(field, Season.Spring, who));
        territory.FarmWork(who, field, ActionKind.Till, Season.Spring, null, out _);
        Assert.Equal(0, barn.Contents.Get("小麦种子"));
        Assert.Equal("小麦", field.CropDefName);
    }

    [Fact]
    public void Sow_OutOfSeason_IsRefused()
    {
        var territory = Plot();
        var field = Field();
        var who = new CharacterState(1);
        who.Bag.Add("药草种子", 1);
        var herb = new Facility { Id = 4, Name = "药圃", RoomId = 1, YieldItemId = "药草", Built = true };

        Assert.Equal(Territory.FarmState.OutOfSeason, territory.PlotState(herb, Season.Winter, who));
        territory.FarmWork(who, herb, ActionKind.Till, Season.Winter, null, out var handled);
        Assert.True(handled);
        Assert.Equal("", herb.CropDefName);
        Assert.Equal(1, who.Bag.Get("药草种子"));
    }

    [Fact]
    public void Growth_AdvancesDailyInSeason_PausesOutOfSeason()
    {
        var state = new GameState();
        state.Territory.AddRoom(new Room { Id = 1, Name = "农田", Open = true });
        var field = Field();
        field.CropDefName = "小麦";
        field.Growth = 0;
        state.Territory.AddFacility(field);
        state.Clock.SetTime(1, 0); // 春

        state.SettleDay(Season.Spring);
        Assert.Equal(1, field.Growth);

        // 冬天停长：把时钟切到冬天再结算。
        state.Clock.SetTime(46, 0); // 冬
        state.SettleDay(Season.Winter);
        Assert.Equal(1, field.Growth);
    }

    [Fact]
    public void Harvest_YieldsBySkillAndClearsPlot()
    {
        var territory = Plot();
        var field = Field();
        var who = new CharacterState(1);
        who[CoreStat.Perception] = ActionKindMap.SkillBaseline; // 生活技能 40 → 收获 1 份
        field.CropDefName = "小麦";
        field.Growth = 6;

        Assert.Equal(Territory.FarmState.ReadyHarvest, territory.PlotState(field, Season.Spring, who));
        var log = territory.FarmWork(who, field, ActionKind.Till, Season.Spring, null, out var handled);
        Assert.True(handled);
        Assert.NotNull(log);
        Assert.Equal("小麦", log!.ItemId);
        Assert.Equal(1, log.Count);
        Assert.Equal(1, who.Bag.Get("小麦"));
        Assert.Equal("", field.CropDefName);
        Assert.Equal(0, field.Growth);
    }

    [Fact]
    public void Player_TillOnGrowingPlot_WritesAndCostsNoTime()
    {
        var (state, hub, field) = SownField(Season.Spring);
        state.Clock.SetTime(1, 14 * 60);
        hub.Enter(1);
        Assert.True(hub.Use(field.Id));
        var before = state.Clock.Minutes;

        Assert.False(hub.ActAtFixture(ActionKind.Till));
        Assert.Equal(before, state.Clock.Minutes);
        Assert.Contains("还需要", hub.Log.Last().Text);
    }

    [Fact]
    public void Player_TillOnEmptyPlotWithoutSeed_WritesAndCostsNoTime()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        state.Territory.AddRoom(new Room { Id = 1, Name = "农田", Open = true });
        var field = Field();
        state.Territory.AddFacility(field);
        state.Clock.SetTime(1, 14 * 60);
        var hub = new HubSession(state);
        hub.Enter(1);
        Assert.True(hub.Use(field.Id));

        Assert.False(hub.ActAtFixture(ActionKind.Till));
        Assert.Contains("种子", hub.Log.Last().Text);
    }

    [Fact]
    public void Player_TillSowsWithSeed()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        state.Roster.Master!.Bag.Add("小麦种子", 1);
        state.Territory.AddRoom(new Room { Id = 1, Name = "农田", Open = true });
        var field = Field();
        state.Territory.AddFacility(field);
        state.Clock.SetTime(1, 14 * 60);
        var hub = new HubSession(state);
        hub.Enter(1);
        Assert.True(hub.Use(field.Id));

        Assert.True(hub.ActAtFixture(ActionKind.Till));
        Assert.Equal(0, state.Roster.Master.Bag.Get("小麦种子"));
        Assert.Equal("小麦", field.CropDefName);
        Assert.Contains("耕作", hub.Log.Last().Text);
    }

    [Fact]
    public void Worker_SkipsGrowingPlotButSowsAndHarvestsWhenWorkable()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var worker = state.Roster.Add("工");
        worker[CoreStat.Perception] = ActionKindMap.SkillBaseline;
        worker.Affect.LastMealDay = 1;
        worker.Affect.LastMealWindow = 1;
        worker.Bag.Add("小麦种子", 1);
        var territory = state.Territory;
        territory.AddRoom(new Room { Id = 1, Name = "农田", Open = true });
        var field = Field();
        territory.AddFacility(field);
        territory.Assign(worker.Id, 2, SlotMode.Work, field.Id);
        state.Clock.SetTime(1, 14 * 60);

        var hub = new HubSession(state);
        hub.Place(worker.Id, 1);
        hub.Day.Rng = new Random(5);

        // 播种：一格活干完（8 tick）后地块入种。
        hub.PassTime(TillTicks * 5 + 15);
        Assert.Equal("小麦", field.CropDefName);
        Assert.Equal(0, worker.Bag.Get("小麦种子"));

        // 生长中：工人不再挑这块地的活，进度归零不再累积。
        hub.PassTime(30);
        var done = hub.Day.Workers.First(w => w.CharacterId == worker.Id);
        Assert.Equal(0, done.Progress);

        // 6 天后成熟，工人自己来收（结算时钟放在 13 点：委派槽 2 内；
        // 窗口放宽到两小时——工人从闲逛节律里回头挑活需要几格）。
        for (var day = 0; day < 6; day++)
        {
            state.Clock.SetTime(2 + day, 13 * 60);
            state.SettleDay(Season.Spring);
        }
        Assert.True(field.Growth >= 6);
        hub.PassTime(120);
        Assert.Equal("", field.CropDefName);
        Assert.Equal(1, worker.Bag.Get("小麦"));
    }

    [Fact]
    public void Worker_keeps_working_the_field_while_master_walks_the_world()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var worker = state.Roster.Add("工");
        worker[CoreStat.Perception] = ActionKindMap.SkillBaseline;
        worker.Affect.LastMealDay = 1;
        worker.Affect.LastMealWindow = 1;
        worker.Bag.Add("小麦种子", 1);
        var territory = state.Territory;
        territory.AddRoom(new Room { Id = 1, Name = "农田", Open = true });
        territory.AddRoom(new Room { Id = 2, Name = "卧室", Open = true });
        territory.Link(1, 2);
        var field = Field();
        territory.AddFacility(field);
        territory.Assign(worker.Id, 2, SlotMode.Work, field.Id);
        state.Clock.SetTime(1, 14 * 60);

        var hub = new HubSession(state) { EncounterRate = 0 };
        hub.Enter(2);
        hub.Place(worker.Id, 1);
        hub.Day.Rng = new Random(5);

        // 主人出门，在大地图上来回踱步（每步按地貌推进时间），家里的工人照常干活。
        hub.SwitchToWorld();
        var w = state.World;
        var start = state.Clock.TotalMinutes;
        var (hx, hy) = hub.WorldPartyPosition;
        var dir = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.First(d => w.IsPassable(hx + d.Item1, hy + d.Item2));
        var back = false;
        while (state.Clock.TotalMinutes - start < TillTicks * 5 + 15)
        {
            Assert.True(back ? hub.StepWorld(-dir.Item1, -dir.Item2) : hub.StepWorld(dir.Item1, dir.Item2));
            back = !back;
        }
        Assert.Equal(MapLayer.World, hub.Layer);
        Assert.Equal(-1, hub.PlayerRoomId);
        Assert.Equal("小麦", field.CropDefName);
        Assert.Equal(0, worker.Bag.Get("小麦种子"));
    }

    [Fact]
    public void SaveRoundtrip_KeepsPlotState()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        state.Territory.AddRoom(new Room { Id = 1, Name = "农田", Open = true });
        var field = Field();
        field.CropDefName = "小麦";
        field.Growth = 3;
        state.Territory.AddFacility(field);
        var hub = new HubSession(state);

        var loaded = SaveSystem.Load(SaveSystem.Save(state, hub));
        var saved = loaded.Territory.Facilities.Single(f => f.Id == field.Id);
        Assert.Equal("小麦", saved.CropDefName);
        Assert.Equal(3, saved.Growth);
    }

    // ---------- 搭建 ----------

    private static Territory Plot()
    {
        var territory = new Territory();
        territory.AddRoom(new Room { Id = 1, Name = "农田", Open = true });
        return territory;
    }

    private static Facility Field() => new()
    {
        Id = 1, Name = "麦田", RoomId = 1, Usage = FacilityUsage.Plain, Capacity = 2,
        YieldItemId = "小麦", Built = true, Actions = { ActionKind.Till },
    };

    /// <summary>一块已播种的小麦田，玩家已坐在上面。</summary>
    private static (GameState State, HubSession Hub, Facility Field) SownField(Season season)
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        state.Territory.AddRoom(new Room { Id = 1, Name = "农田", Open = true });
        var field = Field();
        field.CropDefName = "小麦";
        state.Territory.AddFacility(field);
        state.Clock.SetTime(1, 14 * 60);
        var hub = new HubSession(state);
        hub.Enter(1);
        return (state, hub, field);
    }
}
