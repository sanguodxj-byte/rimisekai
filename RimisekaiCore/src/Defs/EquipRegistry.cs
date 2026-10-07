using System.Collections.Generic;

namespace Rimisekai.Defs;

/// <summary>
/// 防具与饰品的实例登记表。一件实例一行，按 Id 记。
/// 与 <see cref="WeaponRegistry"/> 并列——武器另有类型轴与子类型，形状不同。
/// </summary>
public sealed class EquipRegistry
{
    private readonly Dictionary<string, EquipInstance> _all = new();
    private int _nextId = 1;

    public IReadOnlyCollection<EquipInstance> All => _all.Values;

    public int Count => _all.Count;

    public EquipInstance? Get(string id) =>
        _all.TryGetValue(id, out var e) ? e : null;

    public bool Exists(string id) => _all.ContainsKey(id);

    public void Add(EquipInstance equip)
    {
        _all[equip.Id] = equip;
        if (equip.Id.StartsWith("eqp_") && int.TryParse(equip.Id[4..], out var n) && n >= _nextId)
            _nextId = n + 1;
    }

    public bool Remove(string id) => _all.Remove(id);
}
