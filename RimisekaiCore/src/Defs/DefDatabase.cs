using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimisekai.Defs;

/// <summary>带数值 Id 的 Def（房间、设施这类内容包用数字键排序的定义）。</summary>
public interface IIdentifiedDef
{
    int Id { get; }
}

/// <summary>
/// 全局泛型 Def 数据库。负责 Def 的注册、按 defName / 数值 Id 查询与枚举遍历。
/// 这是内容定义的唯一权威索引：房间、设施、物品、行为都从这里查，不再有第二份目录。
/// </summary>
public static class DefDatabase<T> where T : Def
{
    private static readonly Dictionary<string, T> _defs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<T> _all = new();
    private static Dictionary<int, T>? _byId;

    public static IReadOnlyList<T> All => _all;

    public static void Clear()
    {
        _defs.Clear();
        _all.Clear();
        _byId = null;
    }

    public static void Register(T def)
    {
        if (string.IsNullOrEmpty(def.DefName))
            throw new ArgumentException("DefName cannot be null or empty.");

        if (_defs.TryGetValue(def.DefName, out var existing))
        {
            var idx = _all.IndexOf(existing);
            if (idx >= 0)
                _all[idx] = def;
            _defs[def.DefName] = def;
        }
        else
        {
            _defs[def.DefName] = def;
            _all.Add(def);
        }
        _byId = null; // 有增改就作废 Id 索引，下次按需重建
    }

    public static T? Get(string defName) =>
        _defs.TryGetValue(defName, out var def) ? def : null;

    public static bool TryGet(string defName, out T def) =>
        _defs.TryGetValue(defName, out def!);

    public static T GetNamed(string defName)
    {
        if (_defs.TryGetValue(defName, out var def))
            return def;
        throw new KeyNotFoundException($"Def of type {typeof(T).Name} with name '{defName}' was not found.");
    }

    /// <summary>
    /// 按数值 Id 查定义。给旧逻辑（界面/建造按 int Id 传参）用的兼容入口，
    /// 权威仍是 defName；Id 只是内容包里的排序键，不应当作主键使用。
    /// </summary>
    public static T? GetById(int id)
    {
        if (_byId == null)
        {
            var dict = new Dictionary<int, T>();
            foreach (var item in _all.OfType<IIdentifiedDef>())
                dict[item.Id] = (T)item;
            _byId = dict;
        }
        return _byId.TryGetValue(id, out var def) ? def : null;
    }
}
