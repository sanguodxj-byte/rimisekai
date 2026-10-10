using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Character;
using Rimisekai.Clock;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Save;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 交易系统：点交易免费开页浏览；**首笔成交结算行程**（固定 6 小时，当天只此一次，
/// 0 点刷新；之后的成交免费，失败的买卖不耗时）；随机行情（每日重掷、供需推价、
/// 60% 收购基准）；集市武器（每日随机锻、卖掉上架买走下架）；行情与结算进存档。
/// </summary>
public sealed class MarketTests
{
    // ---------- 赶集结算 ----------

    [Fact]
    public void OpenTrade_IsFree_ButTradesOnlyWorkInside()
    {
        var (state, hub) = NewHub();

        hub.OpenTrade();
        Assert.True(hub.AtMarket);
        Assert.Equal(0, state.Clock.Minutes);
        Assert.True(hub.TradeAvailable);

        hub.LeaveMarket();
        Assert.False(hub.MarketTrade("干粮", 1, selling: false));
        Assert.Equal(0, state.Clock.Minutes);
    }

    [Fact]
    public void FirstSettlement_CostsSixHours_AndMarksTheDay()
    {
        var (state, hub) = NewHub();
        state.Roster.Master!.Bag.Add("干粮", 1);
        hub.OpenTrade();
        var before = state.Clock.Minutes;

        Assert.True(hub.MarketTrade("干粮", 1, selling: false));
        Assert.Equal(6 * 60, state.Clock.Minutes - before);
        Assert.Equal(state.Clock.Day, hub.MarketSettledDay);
        Assert.False(hub.TradeAvailable);
    }

    [Fact]
    public void Settlement_HappensOnce_PerDay_LaterTradesAreFree()
    {
        var (state, hub) = NewHub();
        state.Roster.Master!.Bag.Add("干粮", 5);
        hub.OpenTrade();

        Assert.True(hub.MarketTrade("干粮", 1, selling: false));
        var afterFirst = state.Clock.Minutes;
        Assert.Equal(6 * 60, afterFirst);

        var money = state.Money;
        Assert.True(hub.MarketTrade("干粮", 1, selling: false));
        Assert.Equal(afterFirst, state.Clock.Minutes);
        Assert.Equal(money - state.Territory.Listing("干粮")!.Value.BuyPrice, state.Money);
    }

    [Fact]
    public void FailedTrades_DoNotSettleOrCostTime()
    {
        var (state, hub) = NewHub();
        hub.OpenTrade();

        // 没有可卖的货：成交失败，不耗时也不结算行程。
        Assert.False(hub.MarketTrade("干粮", 1, selling: true));
        Assert.Equal(0, state.Clock.Minutes);
        Assert.True(hub.TradeAvailable);
    }

    [Fact]
    public void SettlementRefreshesAtMidnight()
    {
        var (state, hub) = NewHub();
        state.Roster.Master!.Bag.Add("干粮", 9);
        state.Money = 100000;
        hub.OpenTrade();

        Assert.True(hub.MarketTrade("干粮", 1, selling: false));
        Assert.False(hub.TradeAvailable);

        // 0 点刷新：次日首笔成交再结算一次 6 小时。
        state.Clock.SetTime(2, 8 * 60);
        Assert.True(hub.TradeAvailable);
        var before = state.Clock.Minutes;
        Assert.True(hub.MarketTrade("干粮", 1, selling: false));
        Assert.Equal(6 * 60, state.Clock.Minutes - before);
    }

    [Fact]
    public void LeaveMarket_IsFree_AndClosesTrading()
    {
        var (state, hub) = NewHub();
        hub.OpenTrade();
        hub.LeaveMarket();

        Assert.False(hub.AtMarket);
        Assert.Equal(0, state.Clock.Minutes);
        Assert.False(hub.MarketTrade("干粮", 1, selling: false));
        // 纯浏览没成交，交易按钮仍亮着。
        Assert.True(hub.TradeAvailable);
    }

    // ---------- 行情：报价、供需、60% 基准 ----------

    [Fact]
    public void SellPrice_IsSixtyPercentOfBase_WithNoStockAndFlatMarket()
    {
        var territory = new Territory();
        territory.RollMarketDay(new Random(1));
        // 雨伞基准 30：无库存、当日系数 100 时的情形单独构造。
        territory.MarketDay["雨伞"] = new Territory.MarketEntry(0, 100);

        Assert.Equal(18, territory.Listing("雨伞")!.Value.SellPrice);
        Assert.Equal(30, territory.Listing("雨伞")!.Value.BuyPrice);
    }

    [Fact]
    public void SellingRaisesStock_AndDropsThePrice()
    {
        var territory = new Territory();
        territory.MarketDay["雨伞"] = new Territory.MarketEntry(0, 100);
        var before = territory.Listing("雨伞")!.Value.SellPrice;

        territory.MarketSold("雨伞", 2);
        var after = territory.Listing("雨伞")!.Value.SellPrice;

        Assert.Equal(18, before);
        Assert.True(after < before, $"存货变多后卖价应下跌：{before} -> {after}");
    }

    [Fact]
    public void SellPriceFloorsAtSeventyPercentOfTheRatios()
    {
        var territory = new Territory();
        // 库存堆满：卖价系数压到下限 70（=基准 60% 的七成）。
        territory.MarketDay["雨伞"] = new Territory.MarketEntry(99, 100);
        Assert.Equal(12, territory.Listing("雨伞")!.Value.SellPrice);
    }

    [Fact]
    public void BuyingDepletesStock_AndRaisesThePrice()
    {
        var territory = new Territory();
        territory.MarketDay["雨伞"] = new Territory.MarketEntry(3, 100);
        var before = territory.Listing("雨伞")!.Value.BuyPrice;

        territory.MarketBought("雨伞", 1);
        var after = territory.Listing("雨伞")!.Value.BuyPrice;

        Assert.Equal(26, before);
        Assert.True(after > before, $"库存变少后买价应上涨：{before} -> {after}");
    }

    [Fact]
    public void NoStock_BuyRefused_SellAllowed()
    {
        var state = new GameState { Money = 100 };
        state.Roster.Add("你", master: true);
        state.Roster.Master!.Bag.Add("雨伞", 1);
        var hub = new HubSession(state);
        hub.OpenTrade();
        state.Territory.MarketDay["雨伞"] = new Territory.MarketEntry(0, 100);

        Assert.False(hub.MarketTrade("雨伞", 1, selling: false));
        Assert.True(hub.MarketTrade("雨伞", 1, selling: true));
        Assert.Equal(0, state.Roster.Master.Bag.Get("雨伞"));
    }

    [Fact]
    public void MarketDay_Stock_And_Percent_Drive_Price()
    {
        var territory = new Territory();
        territory.MarketDay["雨伞"] = new Territory.MarketEntry(5, 100);

        var row = territory.Listing("雨伞")!.Value;
        // 雨伞基准 30：买 = 30×100%×80/100 = 24；卖 = 30×60%×100%×75/100 = 13（截断）。
        Assert.Equal(24, row.BuyPrice);
        Assert.Equal(13, row.SellPrice);
        Assert.Equal(5, row.Stock);
    }

    [Fact]
    public void Listing_Reports_SoldOut_And_NoBuy()
    {
        var territory = new Territory();
        territory.MarketDay["雨伞"] = new Territory.MarketEntry(0, 100);
        var soldOut = territory.Listing("雨伞")!.Value;
        Assert.True(soldOut.SoldOut);
        Assert.False(soldOut.NoBuy);
    }

    // ---------- 行情重掷 ----------

    [Fact]
    public void RollMarketDay_StaysInBounds_AndCoversEveryThing()
    {
        var territory = new Territory();
        territory.RollMarketDay(new Random(1));

        foreach (var def in Defs.DefDatabase<Defs.ThingDef>.All)
        {
            var entry = territory.MarketDay[def.DefName];
            var cap = def.MarketValue switch
            {
                <= 5 => 12,
                <= 20 => 6,
                <= 60 => 4,
                _ => 2,
            };
            var floor = def.MarketValue switch
            {
                <= 5 => 4,
                <= 20 => 2,
                _ => 0,
            };
            Assert.InRange(entry.Stock, floor, cap);
            Assert.InRange(entry.PricePercent, 70, 130);
        }
    }

    [Fact]
    public void SettleDay_RerollsTheMarket()
    {
        var state = new GameState();
        state.Territory.MarketDay["雨伞"] = new Territory.MarketEntry(-5, 999);

        state.SettleDay(Season.Spring);

        // 雨伞 45 元，落在 0-4 件档；-5 与 999 都被重掷洗掉。
        var entry = state.Territory.MarketDay["雨伞"];
        Assert.InRange(entry.Stock, 0, 4);
        Assert.InRange(entry.PricePercent, 70, 130);
    }

    // ---------- 集市武器 ----------

    [Fact]
    public void RollMarketDay_ForgesCheapWeapons_WithinBounds()
    {
        var territory = new Territory();
        territory.RollMarketDay(new Random(1));

        // 3-6 件；材料只取能打兵器里最便宜的三种（布与皮不在其列），
        // 品质只取前三档，不强化不祝福，系数 70-130。
        Assert.InRange(territory.MarketWeapons.Count, 3, 6);
        var cheapTiers = Defs.WeaponForge.WeaponMaterials().Take(3)
            .Select(m => m.DefName).ToHashSet();
        foreach (var listing in territory.MarketWeapons)
        {
            var weapon = territory.Weapons.Get(listing.WeaponId);
            Assert.NotNull(weapon);
            Assert.Contains(weapon!.MaterialDefName, cheapTiers);
            Assert.InRange((int)weapon.Quality, 0, 2);
            Assert.Equal(0, weapon.Enhance);
            Assert.False(weapon.Blessed);
            Assert.InRange(listing.PricePercent, 70, 130);
            Assert.InRange(territory.WeaponPricePercent, 70, 130);
        }
    }

    [Fact]
    public void ListedWeapon_IsBuyable_AndDelistedWhenBought()
    {
        var state = new GameState { Money = 100000 };
        state.Roster.Add("你", master: true);
        var weapon = Defs.WeaponForge.Forge("铁", WeaponType.Sword,
            quality: Defs.Quality.Common, enchant: "", blessed: false, enhance: 0);
        state.Territory.Weapons.Add(weapon);
        state.Territory.MarketWeapons.Add(new Territory.MarketWeaponListing(weapon.Id, 100));
        var hub = new HubSession(state);
        hub.OpenTrade();

        var row = state.Territory.Listing(weapon.Id)!.Value;
        Assert.Equal(1, row.Stock);
        Assert.False(row.Stock < 0);

        Assert.True(hub.MarketTrade(weapon.Id, 1, selling: false));
        Assert.Equal(1, state.Roster.Master!.Bag.Get(weapon.Id));
        // 买走即下架：这一件不在架上了（开局随机锻的其他武器不受影响）。
        Assert.DoesNotContain(state.Territory.MarketWeapons, l => l.WeaponId == weapon.Id);
        Assert.True(state.Territory.Listing(weapon.Id)!.Value.Stock < 0);
    }

    [Fact]
    public void SellingWeapon_ListsIt_AtSixtyPercentTimesWeaponFactor()
    {
        var (state, hub) = NewHub();
        var weapon = Defs.WeaponForge.Forge("铁", WeaponType.Sword,
            quality: Defs.Quality.Common, enchant: "", blessed: false, enhance: 0);
        state.Territory.Weapons.Add(weapon);
        state.Roster.Master!.Bag.Add(weapon.Id, 1);
        hub.OpenTrade();

        // 未上架的武器只收不卖，价 = 价值 × 60% × 当日武器系数。
        var row = state.Territory.Listing(weapon.Id)!.Value;
        Assert.True(row.Stock < 0);
        Assert.Equal(Math.Max(1, weapon.Value * 60 * state.Territory.WeaponPricePercent / 10000), row.SellPrice);

        var money = state.Money;
        Assert.True(hub.MarketTrade(weapon.Id, 1, selling: true));
        // 首笔成交含 6 小时行程结算，钱数本身只看卖价。
        Assert.Equal(money + row.SellPrice, state.Money);
        Assert.Equal(0, state.Roster.Master.Bag.Get(weapon.Id));
        // 卖掉的武器上架，可被买回（开局随机锻的其他武器不受影响）。
        Assert.Contains(state.Territory.MarketWeapons, l => l.WeaponId == weapon.Id);
        Assert.False(state.Territory.Listing(weapon.Id)!.Value.Stock < 0);
    }

    // ---------- 存档 ----------

    [Fact]
    public void SaveRoundtrip_KeepsMarketAndSettlement()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        state.Territory.MarketDay["雨伞"] = new Territory.MarketEntry(0, 88);
        var hub = new HubSession(state);
        hub.OpenTrade();
        state.Clock.SetTime(1, 9 * 60);
        // 成交一笔：结算行程（+6 小时，记当天），卖出抬库存 0→1。
        state.Roster.Master!.Bag.Add("雨伞", 1);
        Assert.True(hub.MarketTrade("雨伞", 1, selling: true));
        hub.LeaveMarket();

        var json = SaveSystem.Save(state, hub);
        var loaded = SaveSystem.Load(json);
        var entry = loaded.Territory.MarketDay["雨伞"];
        Assert.Equal(1, entry.Stock);
        Assert.Equal(88, entry.PricePercent);

        var restored = new HubSession(loaded);
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<SaveData>(json);
        Assert.NotNull(snapshot?.Hub);
        restored.Restore(snapshot.Hub);
        Assert.False(restored.AtMarket);
        Assert.Equal(1, restored.MarketSettledDay);
        // 结算后当天按钮是暗的。
        Assert.False(restored.TradeAvailable);
    }

    [Fact]
    public void MarketTradeCombined_CanExecuteSimultaneousSellAndBuy()
    {
        var (state, hub) = NewHub();
        state.Money = 10; // 现金不足以单独买入高价物品
        state.Territory.MarketDay["木材"] = new Territory.MarketEntry(10, 100);
        state.Territory.MarketDay["雨伞"] = new Territory.MarketEntry(5, 100);

        hub.OpenTrade();
        var master = state.Roster.Master!;
        master.Bag.Add("雨伞", 1);

        var sellListing = state.Territory.Listing("雨伞")!.Value;
        var buyListing = state.Territory.Listing("木材")!.Value;
        var sellPrice = hub.TradePrices(sellListing, selling: true);
        var buyPrice = hub.TradePrices(buyListing, selling: false);

        // 卖出雨伞并买入木材一次性完成
        var ok = hub.MarketTradeCombined("雨伞", 1, "木材", 2);
        Assert.True(ok);

        Assert.Equal(0, master.Bag.Get("雨伞"));
        Assert.Equal(2, master.Bag.Get("木材"));
        var expectedMoney = 10 + sellPrice * 1 - buyPrice * 2;
        Assert.Equal(expectedMoney, state.Money);
    }

    [Fact]
    public void MarketTradeCombined_FailsWhenTotalDeficitExceedsMoney()
    {
        var (state, hub) = NewHub();
        state.Money = 0; // 零现金
        state.Territory.MarketDay["木材"] = new Territory.MarketEntry(10, 100);
        state.Territory.MarketDay["雨伞"] = new Territory.MarketEntry(5, 100);

        hub.OpenTrade();
        var master = state.Roster.Master!;
        master.Bag.Add("木材", 1); // 木材价值很低，远不足以抵扣高价值雨伞

        var ok = hub.MarketTradeCombined("木材", 1, "雨伞", 1);
        Assert.False(ok); // 现金不足以补齐差额，拒绝交易
        Assert.Equal(1, master.Bag.Get("木材"));
        Assert.Equal(0, master.Bag.Get("雨伞"));
    }

    // ---------- 搭建 ----------

    private static (GameState State, HubSession Hub) NewHub()
    {
        var state = new GameState { Money = 500 };
        state.Clock.SetTime(1, 0);
        state.Roster.Add("你", master: true);
        var hub = new HubSession(state);
        return (state, hub);
    }
}
