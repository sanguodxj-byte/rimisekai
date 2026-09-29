using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Housing;

namespace Rimisekai.Hub;

public sealed partial class HubSession
{
    public const int CostTrade = 72;

    /// <summary>玩家背包里的物品。买卖、送礼都看这里。</summary>
    public IReadOnlyDictionary<string, int> Stock() =>
        State.Roster.Master?.Bag.Items ?? EmptyItems;

    private static readonly Dictionary<string, int> EmptyItems = new();

    public bool Trade(string itemId, int count, int unitPrice, bool selling)
    {
        if (!CanTrade(itemId, count, unitPrice, selling))
            return false;
        PassTime(CostTrade * TerritoryClock.StepMinutes);
        return TradeCore(itemId, count, unitPrice, selling);
    }

    private bool CanTrade(string itemId, int count, int unitPrice, bool selling)
    {
        if (count <= 0 || unitPrice < 0)
            return false;
        var cost = (long)count * unitPrice;
        if (selling)
            return (State.Roster.Master?.Bag.Get(itemId) ?? 0) >= count;
        return State.Money >= cost;
    }

    private bool TradeCore(string itemId, int count, int unitPrice, bool selling)
    {
        var cost = (long)count * unitPrice;
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

    public bool MarketTrade(string itemId, int count, bool selling)
    {
        var offer = State.Territory.Market.Find(o => o.ItemId == itemId);
        if (offer == null)
            return false;
        var price = selling ? offer.SellPrice : offer.BuyPrice;
        if (!CanTrade(itemId, count, price, selling))
            return false;
        PassTime(CostTrade * TerritoryClock.StepMinutes);
        return TradeCore(itemId, count, price, selling);
    }

    /// <summary>
    /// 手工制作。材料从玩家背包与据点各设施存货里扣（<see cref="Territory.PayWith"/>），
    /// 成品进玩家背包。
    /// </summary>
    public bool Craft(string itemId)
    {
        var recipe = State.Territory.Recipes.Find(r => r.ItemId == itemId);
        var master = State.Roster.Master;
        if (recipe == null || !State.Territory.CanPayWith(master, recipe.Costs))
            return false;
        PassTime(ActionKindMap.Ticks(recipe.Station) * TerritoryClock.StepMinutes);
        if (!State.Territory.PayWith(master, recipe.Costs))
            return false;
        master?.Bag.Add(recipe.ItemId, recipe.OutputCount);
        master?.GainLifeExp(recipe.Skill, Territory.CraftExp);
        Write($"你制作了{itemId}。");
        return true;
    }
}
