using System.Linq;
using System.Text.Json;
using Rimisekai.Character;
using Rimisekai.Housing;
using Rimisekai.Save;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 没床打地铺：只在进得去的室内打；醒来心情 -10，这一夜压心情 3 天，连睡叠加至多 -30，
/// 3 天没再睡地铺就退干净（心情的回归值跟着压低再回升）。
/// </summary>
public sealed class FloorSleepTests
{
    private const int Bedroom = 3, StartBed = 5;

    [Fact]
    public void Each_floor_night_weighs_ten_for_three_days_capped_at_thirty()
    {
        var a = new Affect();
        a.SleptOnFloor(1);
        Assert.Equal(40, a.Mood);
        Assert.Equal(10, a.FloorWeight(1));
        a.SleptOnFloor(2);
        a.SleptOnFloor(3);
        Assert.Equal(30, a.FloorWeight(3));
        Assert.Equal(20, a.Mood);
        // 叠满了：第四夜不再多掉，作用期顺延。
        a.SleptOnFloor(4);
        Assert.Equal(20, a.Mood);
        Assert.Equal(30, a.FloorWeight(4));
        Assert.Equal(Affect.Neutral - 30, a.Baseline(4));
        // 之后不再睡地铺：一夜一夜退掉，第 7 天退干净。
        Assert.Equal(20, a.FloorWeight(5));
        Assert.Equal(10, a.FloorWeight(6));
        Assert.Equal(0, a.FloorWeight(7));
        Assert.Equal(Affect.Neutral, a.Baseline(7));
    }

    [Fact]
    public void Maid_with_no_free_bed_sleeps_on_the_floor_of_the_masters_room_and_pays_for_it()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 31);
        var maid = state.Roster.Members.Single(c => !c.IsMaster);
        hub.Enter(Bedroom);
        Assert.True(hub.Use(StartBed)); // 主人占着唯一那张床，女仆好感不够同床
        Worker W() => hub.Day.Workers.Single(w => w.CharacterId == maid.Id);
        var slept = false;
        for (var i = 0; i < 24 * 6 && !slept; i++)
        {
            hub.PassTime(10);
            slept = W() is { Goal: ActionKind.Sleep, Path.Count: 0 };
        }
        Assert.True(slept, "女仆睡下了");
        Assert.Equal(-1, W().FacilityId);
        Assert.Equal(Bedroom, W().RoomId);
        while (W().Goal == ActionKind.Sleep)
            hub.PassTime(10);
        Assert.Equal(new[] { state.Clock.Day }, maid.Affect.FloorNights);
        Assert.Equal(Affect.FloorPenalty, maid.Affect.FloorWeight(state.Clock.Day));

        var data = JsonSerializer.Deserialize<SaveData>(SaveSystem.Save(state, hub))!;
        var loaded = SaveSystem.Restore(data);
        Assert.Equal(maid.Affect.FloorNights, loaded.Roster.Find(maid.Id)!.Affect.FloorNights);
    }
}
