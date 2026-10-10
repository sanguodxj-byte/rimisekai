using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Character;
using Rimisekai.Housing;

namespace Rimisekai.Hub;

public sealed partial class HubSession
{
    /// <summary>
    /// 此刻是否站在城镇的商店里：买卖只在这里做——货得人亲手背进城、背回家，
    /// 不再有坐在领地里点一下就成交的远程交易。商店是聚落场景里带 <see cref="Territory.CityShopTag"/> 的那一间。
    /// </summary>
    public bool AtCityShop =>
        Layer == MapLayer.WorldPoi && Room(PlayerRoomId)?.HasTag(Territory.CityShopTag) == true;

    /// <summary>玩家背包里的物品。买卖、送礼都看这里。</summary>
    public IReadOnlyDictionary<string, int> Stock() =>
        State.Roster.Master?.Bag.Items ?? EmptyItems;

    private static readonly Dictionary<string, int> EmptyItems = new();

    /// <summary>买卖都过玩家背包：卖的得在包里，买的落进包里。设施、房间不是随身货，不在城里卖。</summary>
    private bool CanTrade(string itemId, int count, int price, bool selling)
    {
        if (count <= 0 || price < 0)
            return false;
        return selling
            ? (State.Roster.Master?.Bag.Get(itemId) ?? 0) >= count
            : State.Money >= (long)count * price;
    }

    private bool TradeCore(string itemId, int count, int price, bool selling)
    {
        var cost = (long)count * price;
        var bag = State.Roster.Master?.Bag;
        if (bag == null)
            return false;
        if (selling)
        {
            bag.Add(itemId, -count);
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
    /// 在城镇商店里买卖一笔（人得在 <see cref="AtCityShop"/>）。路已经是走来的，成交本身不再耗时。
    /// 成交即推动行情：买入压库存、卖出抬库存——库存即价格。
    /// </summary>
    public bool MarketTrade(string itemId, int count, bool selling)
    {
        if (!AtCityShop)
            return false;
        var listing = State.Territory.Listing(itemId);
        if (listing == null)
            return false;
        var row = listing.Value;
        // 今日无货：买不了，卖不受影响。玩家自己的武器与甲根本不卖（Stock < 0）。
        if (!selling && (row.SoldOut || row.Stock < 0))
            return false;
        // 成交价跟随加成后取：买入往下磨，卖出往上抬（与界面显示同一口径）。
        var price = TradePrices(row, selling);
        if (price <= 0)
            return false;
        if (!CanTrade(itemId, count, price, selling))
            return false;
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
        if (!AtCityShop)
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
