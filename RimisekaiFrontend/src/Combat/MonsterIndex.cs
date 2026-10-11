using System.Collections.Generic;
using System.Linq;
using Rimisekai.Catalog;
using Rimisekai.Defs;

namespace Rimisekai.Combat;

/// <summary>抽怪的地点：野外按地形，地城按地城类型＋房间地形。</summary>
public readonly record struct Habitat(string Dungeon, string Room, string Terrain)
{
    public static Habitat Wild(string terrain) => new("", "", terrain);

    public static Habitat InDungeon(string dungeon, string room) => new(dungeon, room, "");

    public bool IsWild => Dungeon.Length == 0;
}

/// <summary>
/// 抽怪的两张查找表：索引 A 按 CR 分桶，索引 B 按栖息地（地城类型 / 野外地形）；抽怪＝A∩B。
/// 结果按模板表顺序排，同一份表同一组参数永远得到同一串候选。
/// </summary>
public sealed class MonsterIndex
{
    private readonly List<MonsterDef> _order;
    private readonly SortedDictionary<ChallengeRating, List<MonsterDef>> _byCr = new();
    private readonly Dictionary<string, List<MonsterDef>> _byDungeon = new();
    private readonly Dictionary<string, List<MonsterDef>> _byTerrain = new();

    public MonsterIndex(IReadOnlyList<MonsterDef> monsters)
    {
        _order = monsters.ToList();
        foreach (var m in monsters)
        {
            Bucket(_byCr, m.Cr).Add(m);
            foreach (var dungeon in m.Dungeons)
                Bucket(_byDungeon, dungeon).Add(m);
            foreach (var terrain in m.Terrains)
                Bucket(_byTerrain, terrain).Add(m);
        }
    }

    /// <summary>按当前模板表建索引。</summary>
    public static MonsterIndex Build() => new(DefDatabase<MonsterDef>.All);

    /// <summary>索引 A：CR 落在 [min, max] 的模板。</summary>
    public IEnumerable<MonsterDef> ByCr(ChallengeRating min, ChallengeRating max) =>
        _byCr.Where(pair => pair.Key >= min && pair.Key <= max).SelectMany(pair => pair.Value);

    /// <summary>索引 B：野外按地形；地城按类型，再筛房间地形（模板不写 rooms＝任何房间）。</summary>
    public IEnumerable<MonsterDef> ByHabitat(Habitat habitat)
    {
        if (habitat.IsWild)
            return _byTerrain.GetValueOrDefault(habitat.Terrain) ?? Enumerable.Empty<MonsterDef>();
        return (_byDungeon.GetValueOrDefault(habitat.Dungeon) ?? Enumerable.Empty<MonsterDef>())
            .Where(m => m.Rooms.Count == 0 || m.Rooms.Contains(habitat.Room));
    }

    /// <summary>A∩B，按模板表顺序。</summary>
    public List<MonsterDef> Draw(ChallengeRating min, ChallengeRating max, Habitat habitat)
    {
        var a = ByCr(min, max).ToHashSet();
        var b = ByHabitat(habitat).ToHashSet();
        return _order.Where(m => a.Contains(m) && b.Contains(m)).ToList();
    }

    private static List<MonsterDef> Bucket<TKey>(IDictionary<TKey, List<MonsterDef>> table, TKey key)
    {
        if (!table.TryGetValue(key, out var list))
            table[key] = list = new List<MonsterDef>();
        return list;
    }
}
