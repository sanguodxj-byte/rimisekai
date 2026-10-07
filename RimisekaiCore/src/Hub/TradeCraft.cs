using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Character;
using Rimisekai.Housing;

namespace Rimisekai.Hub;

public sealed partial class HubSession
{
    public const int CostTrade = 72;

    /// <summary>此刻是否在交易页里（点交易进入，关页离开）。在页内才能买卖。</summary>
    public bool AtMarket { get; private set; }

    /// <summary>最近一次交易成交的日期（首笔成交即结算行程，0 点刷新）。</summary>
    public int MarketSettledDay { get; private set; } = -1;

    /// <summary>今天还没成交过：交易按钮亮着；成交过即暗，0 点重新点亮。</summary>
    public bool TradeAvailable => MarketSettledDay != State.Clock.Day;

    /// <summary>打开交易页：浏览行情不耗时。</summary>
    public void OpenTrade() => AtMarket = true;

    /// <summary>关闭交易页：不耗时。</summary>
    public void LeaveMarket() => AtMarket = false;

    /// <summary>玩家背包里的物品。买卖、送礼都看这里。</summary>
    public IReadOnlyDictionary<string, int> Stock() =>
        State.Roster.Master?.Bag.Items ?? EmptyItems;

    private static readonly Dictionary<string, int> EmptyItems = new();

    /// <summary>
    /// 交易。物品买进/卖出都过背包；**设施只能卖**——卖掉就把据点里那座设施移除，
    /// 想再有一座只能去建。房间不是货，不在市场清单里，<see cref="CanTrade"/> 拒掉。
    /// </summary>
    public bool Trade(string itemId, int count, int price, bool selling)
    {
        if (!CanTrade(itemId, count, price, selling))
            return false;
        PassTime(CostTrade * TerritoryClock.StepMinutes);
        return TradeCore(itemId, count, price, selling);
    }

    /// <summary>按 Id 取设施定义；不是设施返回 null。</summary>
    private static Defs.FacilityDef? FacilityOf(string itemId) =>
        Defs.DefDatabase<Defs.FacilityDef>.All.FirstOrDefault(f =>
            f.DefName.Equals(itemId, StringComparison.OrdinalIgnoreCase));

    /// <summary>据点里同名设施有多少座（含已摆放与未放置）。</summary>
    private int FacilityCount(Defs.FacilityDef def)
    {
        var n = 0;
        foreach (var facility in State.Territory.Facilities)
        {
            if (facility.Name == def.Name)
                n++;
        }
        return n;
    }

    private bool CanTrade(string itemId, int count, int price, bool selling)
    {
        if (count <= 0 || price < 0)
            return false;
        var cost = (long)count * price;
        if (selling)
        {
            // 设施按座数计（拆一座卖一份），其余看背包。
            var def = FacilityOf(itemId);
            return def != null
                ? FacilityCount(def) >= count
                : (State.Roster.Master?.Bag.Get(itemId) ?? 0) >= count;
        }
        return State.Money >= cost;
    }

    private bool TradeCore(string itemId, int count, int price, bool selling)
    {
        var cost = (long)count * price;
        var bag = State.Roster.Master?.Bag;
        if (bag == null)
            return false;
        if (selling)
        {
            var def = FacilityOf(itemId);
            if (def != null)
            {
                // 卖设施：把据点里这座设施拆掉换钱。先拆未放置的，再拆已摆放的。
                for (var i = 0; i < count; i++)
                {
                    var facility = State.Territory.Facilities.Find(f => f.RoomId < 0 && f.Name == def.Name)
                        ?? State.Territory.Facilities.Find(f => f.Name == def.Name);
                    if (facility == null)
                        return false;
                    State.Territory.Facilities.Remove(facility);
                }
            }
            else
            {
                bag.Add(itemId, -count);
            }
            State.Money += cost;
        }
        else
        {
            State.Money -= cost;
            bag.Add(itemId, count);
        }
        return true;
    }

    /// <summary>
    /// 交易：必须在交易页内（点交易进入，浏览不耗时）。
    /// **首笔成交即结算行程**：固定 6 小时，当天只此一次；之后的成交免费。
    /// 成交即推动行情：买入压库存、卖出抬库存——库存即价格。
    /// </summary>
    public bool MarketTrade(string itemId, int count, bool selling)
    {
        if (!AtMarket)
            return false;
        var listing = State.Territory.Listing(itemId);
        if (listing == null)
            return false;
        var row = listing.Value;
        // 今日无货：买不了，卖不受影响。设施与玩家自己的武器市场根本不卖（Stock < 0）。
        if (!selling && (row.SoldOut || row.Stock < 0))
            return false;
        // 成交价跟随加成后取：买入往下磨，卖出往上抬（与界面显示同一口径）。
        var price = TradePrices(row, selling);
        if (price <= 0)
            return false;
        if (!CanTrade(itemId, count, price, selling))
            return false;
        // 校验全过、这笔必然成交，才结算行程——失败的买卖不耗时。
        if (MarketSettledDay != State.Clock.Day)
        {
            PassTime(CostTrade * TerritoryClock.StepMinutes);
            MarketSettledDay = State.Clock.Day;
            Write("你出门交易了一趟。");
        }
        if (!TradeCore(itemId, count, price, selling))
            return false;
        if (row.Stock >= 0)
        {
            if (selling)
                State.Territory.MarketSold(itemId, count);
            else
                State.Territory.MarketBought(itemId, count);
        }
        // 武器不进物品库存：集市在售的被买走即下架；玩家卖掉的武器上架可被买回。
        if (State.Territory.Weapons.Get(itemId) != null)
        {
            if (selling)
                State.Territory.MarketListWeapon(itemId);
            else
                State.Territory.MarketDelistWeapon(itemId);
        }
        return true;
    }

    /// <summary>
    /// 一次性完成买入和卖出（以物易物，差价结算）。
    /// 支持仅卖出、仅买入、或同时完成买入与卖出。
    /// </summary>
    public bool MarketTradeCombined(string? sellItemId, int sellCount, string? buyItemId, int buyCount)
    {
        if (!AtMarket)
            return false;
        var hasSell = !string.IsNullOrEmpty(sellItemId) && sellCount > 0;
        var hasBuy = !string.IsNullOrEmpty(buyItemId) && buyCount > 0;
        if (!hasSell && !hasBuy)
            return false;
        if (hasSell && !hasBuy)
            return MarketTrade(sellItemId!, sellCount, selling: true);
        if (!hasSell && hasBuy)
            return MarketTrade(buyItemId!, buyCount, selling: false);

        var sellListing = State.Territory.Listing(sellItemId!);
        var buyListing = State.Territory.Listing(buyItemId!);
        if (sellListing == null || buyListing == null)
            return false;

        var sellRow = sellListing.Value;
        var buyRow = buyListing.Value;
        if (buyRow.SoldOut || buyRow.Stock < 0)
            return false;

        var sellPrice = TradePrices(sellRow, selling: true);
        var buyPrice = TradePrices(buyRow, selling: false);
        if (sellPrice <= 0 || buyPrice <= 0)
            return false;

        var bag = State.Roster.Master?.Bag;
        if (bag == null || bag.Get(sellItemId!) < sellCount)
            return false;

        var sellIncome = (long)sellCount * sellPrice;
        var buyExpense = (long)buyCount * buyPrice;
        if (State.Money + sellIncome < buyExpense)
            return false;

        if (MarketSettledDay != State.Clock.Day)
        {
            PassTime(CostTrade * TerritoryClock.StepMinutes);
            MarketSettledDay = State.Clock.Day;
            Write("你出门交易了一趟。");
        }

        bag.Add(sellItemId!, -sellCount);
        bag.Add(buyItemId!, buyCount);
        State.Money += (sellIncome - buyExpense);

        if (sellRow.Stock >= 0)
            State.Territory.MarketSold(sellItemId!, sellCount);
        if (buyRow.Stock >= 0)
            State.Territory.MarketBought(buyItemId!, buyCount);

        if (State.Territory.Weapons.Get(sellItemId!) != null)
            State.Territory.MarketListWeapon(sellItemId!);
        if (State.Territory.Weapons.Get(buyItemId!) != null)
            State.Territory.MarketDelistWeapon(buyItemId!);

        return true;
    }

    /// <summary>
    /// 制作功能：指定领地对应工种的生产目标，由分配了该工种的人员（含玩家自身）
    /// 在日常工作日程中自动前往工作台备料加工，不再直接消耗资源凭空转化。
    /// </summary>
    public bool Craft(string itemId)
    {
        var recipe = State.Territory.Recipes.Find(r => r.ItemId == itemId);
        if (recipe == null)
            return false;

        var currentTarget = State.Territory.GetTargetCraftItem(recipe.Station);
        if (currentTarget == itemId)
        {
            State.Territory.SetTargetCraftItem(recipe.Station, "");
            Write($"取消了{ActionKindMap.LabelOf(recipe.Station)}的指定目标。");
        }
        else
        {
            State.Territory.SetTargetCraftItem(recipe.Station, itemId);
            Write($"已将{itemId}设为{ActionKindMap.LabelOf(recipe.Station)}的生产目标。");
        }
        return true;
    }
}
