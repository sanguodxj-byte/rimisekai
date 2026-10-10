using System.Collections.Generic;

namespace Rimisekai.Defs;

/// <summary>
/// 运行时武器实例登记表。一件实例一行，按 Id 记。
/// 实例不可堆叠——两把"铁长剑+5"如果附魔不同，就是两件不同的东西。
/// </summary>
public sealed class WeaponRegistry
{
    private readonly Dictionary<string, WeaponInstance> _all = new();
    private int _nextId = 1;

    public IReadOnlyCollection<WeaponInstance> All => _all.Values;

    public int Count => _all.Count;

    public WeaponInstance? Get(string id) =>
        _all.TryGetValue(id, out var w) ? w : null;

    public bool Exists(string id) => _all.ContainsKey(id);

    public void Add(WeaponInstance weapon)
    {
        _all[weapon.Id] = weapon;
        // 读档进来的实例可能带高位 Id，计数要跟上，免得新锻的撞号。
        if (weapon.Id.StartsWith("wpn_") && int.TryParse(weapon.Id[4..], out var n) && n >= _nextId)
            _nextId = n + 1;
    }

    public bool Remove(string id) => _all.Remove(id);

    /// <summary>登记一件新实例并分配 Id（Id 由调用方经 WeaponForge 分配）。</summary>
    public WeaponInstance Register(WeaponInstance weapon)
    {
        Add(weapon);
        return weapon;
    }

    /// <summary>下一个可用 Id（供 forge 使用）。</summary>
    public string NextId() => $"wpn_{_nextId++}";
}
