using System.Collections.Generic;

namespace Rimisekai.Character;

/// <summary>人物之间的关系与刻印。存的是谁和谁，不存数值。</summary>
public enum RelationFlag
{
    Acquainted,
    Trusted,
    Sworn,
    Rival,
    Marked,
}

public sealed class Relations : IEnumerable<KeyValuePair<int, HashSet<RelationFlag>>>
{
    private readonly Dictionary<int, HashSet<RelationFlag>> _map = new();

    public bool Has(int otherId, RelationFlag flag) =>
        _map.TryGetValue(otherId, out var set) && set.Contains(flag);

    public void Add(int otherId, RelationFlag flag)
    {
        if (!_map.TryGetValue(otherId, out var set))
        {
            set = new HashSet<RelationFlag>();
            _map[otherId] = set;
        }
        set.Add(flag);
    }

    public bool Remove(int otherId, RelationFlag flag) =>
        _map.TryGetValue(otherId, out var set) && set.Remove(flag);

    public int Count => _map.Count;

    public IEnumerator<KeyValuePair<int, HashSet<RelationFlag>>> GetEnumerator() => _map.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _map.GetEnumerator();

    public IReadOnlyDictionary<int, IReadOnlyCollection<RelationFlag>> All()
    {
        var copy = new Dictionary<int, IReadOnlyCollection<RelationFlag>>();
        foreach (var pair in _map)
            copy[pair.Key] = new List<RelationFlag>(pair.Value);
        return copy;
    }

    public void Set(int otherId, IEnumerable<RelationFlag> flags) =>
        _map[otherId] = new HashSet<RelationFlag>(flags);
}
