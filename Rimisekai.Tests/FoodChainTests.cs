using System.Linq;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>家里有肉就不用买口粮：肉进客厅（有餐桌）的箱子，排到灶上的人开饭前把肉烤成烤肉摆上桌，大家吃掉。</summary>
[Collection("Quest definition state")]
public sealed class FoodChainTests
{
    private const int Courtyard = 1, Parlor = 2, Chest = 8, Campfire = 1002;

    [Fact]
    public void Meat_in_the_pantry_gets_cooked_and_eaten_without_buying_rations()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 5);
        var t = state.Territory;
        var chest = t.Facilities.Single(f => f.Id == Chest);
        chest.Contents.Add("干粮", -chest.Contents.Get("干粮"));
        var maid = TerritoryLoopTests.Maid(state);
        TerritoryLoopTests.GiveMaidABed(hub, state);
        foreach (var id in maid.Bag.Items.Keys.ToList())
            maid.Bag.Add(id, -maid.Bag.Get(id));
        // 生肉自己会被送进有餐桌那间房的箱子
        Assert.Equal(chest.Id, t.FindStorageFor("肉", Courtyard)!.Id);
        chest.Contents.Add("肉", 12);
        Assert.True(hub.BuildFacilityDef(Campfire, Courtyard));
        var fire = t.Facilities[^1];
        Assert.True(fire.Supports(ActionKind.Cook));
        Assert.True(hub.Assign(maid.Id, 1, SlotMode.Work, fire.Id));
        Assert.True(hub.Assign(maid.Id, 2, SlotMode.Work, fire.Id));
        var money = state.Money;
        var rations = TerritoryLoopTests.Total(state, "干粮");   // 主人包里开局带的那几份不动
        var roasted = false;
        for (var step = 0; step < 2 * 24 * 60 / TerritoryClock.StepMinutes; step++)
        {
            hub.PassTime(TerritoryClock.StepMinutes);
            roasted |= t.Facilities.Any(f => f.IsTable && f.Contents.Get("烤肉") > 0);
        }
        Assert.True(roasted, "开饭前烤肉摆上了桌");
        Assert.True(TerritoryLoopTests.Total(state, "肉") < 12, "肉被用掉了");
        Assert.Equal(state.Clock.Day - 1, maid.Affect.LastMealDay);  // 第二天也吃上了
        Assert.Equal(money, state.Money);
        Assert.Equal(rations, TerritoryLoopTests.Total(state, "干粮"));
    }
}
