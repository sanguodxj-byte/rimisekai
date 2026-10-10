using System.Collections.Generic;

namespace Rimisekai.Housing;

public sealed class Stock
{
    private readonly Dictionary<string, int> _items = new();

    public IReadOnlyDictionary<string, int> Items => _items;

    public int Get(string itemId) => _items.TryGetValue(itemId, out var n) ? n : 0;

    public void Clear() => _items.Clear();

    public void Add(string itemId, int count)
    {
        if (count == 0 || itemId.Length == 0)
            return;
        var next = Get(itemId) + count;
        if (next <= 0)
            _items.Remove(itemId);
        else
            _items[itemId] = next;
    }

    public bool CanPay(IReadOnlyList<RecipeCost> costs)
    {
        foreach (var cost in costs)
        {
            if (Get(cost.ItemId) < cost.Count)
                return false;
        }
        return true;
    }

    public bool Pay(IReadOnlyList<RecipeCost> costs)
    {
        if (!CanPay(costs))
            return false;
        foreach (var cost in costs)
            Add(cost.ItemId, -cost.Count);
        return true;
    }
}
