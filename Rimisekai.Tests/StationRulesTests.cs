using System.Linq;
using Rimisekai.Defs;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Quest;
using Xunit;
using Xunit.Abstractions;

namespace Rimisekai.Tests;

/// <summary>
/// 台子分门类（铁砧只锻、工坊只做杂项手艺）、接委托耗 8 小时、皮甲卖得比料值钱。
/// </summary>
[Collection("Quest definition state")]
public sealed class StationRulesTests
{
    private readonly ITestOutputHelper _out;

    public StationRulesTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Anvil_only_smiths_and_kiln_only_fires_pottery()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 1);
        var territory = state.Territory;
        var anvil = new Facility { Craft = "锻" };
        var kiln = new Facility { Craft = "工" };
        var pottery = territory.Recipes.Single(r => r.ItemId == "陶罐");
        var dust = territory.Recipes.Single(r => r.ItemId == "炼金尘");
        var iron = territory.Recipes.Single(r => r.ItemId == "铁");
        var steel = territory.Recipes.Single(r => r.ItemId == "钢");
        Assert.True(territory.Makes(pottery, ActionKind.Forge, kiln));
        Assert.True(territory.Makes(dust, ActionKind.Forge, kiln));
        Assert.False(territory.Makes(iron, ActionKind.Forge, kiln));
        Assert.False(territory.Makes(steel, ActionKind.Forge, kiln));
        Assert.True(territory.Makes(iron, ActionKind.Forge, anvil));
        Assert.True(territory.Makes(steel, ActionKind.Forge, anvil));
        Assert.False(territory.Makes(pottery, ActionKind.Forge, anvil));
        Assert.False(territory.Makes(dust, ActionKind.Forge, anvil));
        // 每条 Forge 配方都归了门类，建出来的铁砧、工坊带着门类
        Assert.All(territory.Recipes.Where(r => r.Station == ActionKind.Forge), r => Assert.NotEqual("", r.Craft));
        ContentDefs.EnsureInitialized();
        Assert.Equal("锻", DefDatabase<FacilityDef>.Get("Facility_1013")!.ToRuntime().Craft);
        Assert.Equal("工", DefDatabase<FacilityDef>.Get("Facility_1072")!.ToRuntime().Craft);
        Assert.Equal(15, DefDatabase<FacilityDef>.Get("Facility_1013")!.MaterialCost.Single(c => c.ItemId == "铁矿").Count);
    }

    [Fact]
    public void Untargeted_anvil_with_stone_and_ore_smelts_iron_not_pots()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 1);
        var master = state.Roster.Master!;
        master.Bag.Add("石材", 40);
        master.Bag.Add("铁矿", 20);
        Assert.True(hub.BuildFacilityDef(1013, 1));
        var anvil = state.Territory.Facilities[^1];
        Assert.Equal("锻", anvil.Craft);
        master.Bag.Add("铁矿", 4);
        TerritoryLoopTests.MoveTo(hub, 1);
        Assert.True(hub.Use(anvil.Id));
        var pots = master.Bag.Get("陶罐");
        Assert.True(hub.ActAtFixture(ActionKind.Forge));
        Assert.Equal(pots, master.Bag.Get("陶罐"));
        Assert.True(master.Bag.Get("铁") > 0, "铁砧冶出了铁");
    }

    [Fact]
    public void Taking_a_commission_skips_eight_hours_whatever_the_party_size()
    {
        foreach (var size in new[] { 1, 2 })
        {
            var hub = TerritoryLoopTests.NewGame(out var state, 1);
            var maid = TerritoryLoopTests.Maid(state);
            maid.Bag.Add("干粮", 10);
            for (var slot = 0; slot < 4; slot++)
                Assert.True(hub.Assign(maid.Id, slot, Housing.SlotMode.Work, 11));
            var wood = TerritoryLoopTests.Total(state, "木材");
            var room = hub.PlayerRoomId;
            var def = QuestBoard.Open(state).First(q => !q.Generated && q.Kind == QuestKind.Battle);
            var party = state.Roster.Members.Select(c => c.Id).Take(size).ToList();
            var before = (state.Clock.Day - 1) * 1440 + state.Clock.Minutes;
            var money = state.Money;
            var run = hub.AcceptCommission(def, party);
            Assert.NotNull(run);
            Assert.Equal(before + HubSession.CommissionMinutes, (state.Clock.Day - 1) * 1440 + state.Clock.Minutes);
            Assert.Equal(480, HubSession.CommissionMinutes);
            Assert.Equal(money, state.Money);
            // 同去的女仆这 8 小时不在家伐木；留在家的照常干活。主人回到出门时的房间。
            if (size == 1)
                Assert.True(TerritoryLoopTests.Total(state, "木材") > wood, "留家的女仆照常伐木");
            else
                Assert.Equal(wood, TerritoryLoopTests.Total(state, "木材"));
            Assert.Equal(room, hub.PlayerRoomId);
            Assert.Empty(hub.Day.Away);
        }
    }

    [Fact]
    public void Leather_armour_sells_for_more_than_its_leather()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 1);
        var master = state.Roster.Master!;
        foreach (var recipe in state.Territory.Recipes.Where(r => r.Gear?.Material == "皮"))
        {
            var id = state.Territory.ForgeGear(master, recipe.Gear!, Quality.Common);
            var sell = state.Territory.Listing(id)!.Value.SellPrice;
            var inputs = recipe.Costs.Sum(c => Items.Get(c.ItemId)!.MarketValue * c.Count);
            var hides = recipe.Costs.Sum(c => c.Count) * 2 * Items.Get("兽皮")!.MarketValue;
            _out.WriteLine($"{recipe.ItemId}: 卖 {sell}G；料 皮×{recipe.Costs.Sum(c => c.Count)} 值 {inputs}G（兽皮 {hides}G）");
            Assert.True(sell > inputs, $"{recipe.ItemId} 卖 {sell}G 不如料值 {inputs}G");
            // 行情最差那天（武器甲胄系数 70%）也不亏料钱
            var equip = state.Equips.Get(id)!;
            Assert.True(equip.Value * Territory.SellRatioPercent * 70 / 10000 > inputs, $"{recipe.ItemId} 行情最差卖不回料钱");
        }
    }
}
