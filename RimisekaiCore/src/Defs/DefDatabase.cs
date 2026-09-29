using System;
using System.Collections.Generic;

namespace Rimisekai.Defs;

/// <summary>
/// 全局泛型 Def 数据库。负责 Def 的注册、按 defName 查询与枚举遍历。
/// </summary>
public static class DefDatabase<T> where T : Def
{
    private static readonly Dictionary<string, T> _defs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<T> _all = new();

    public static IReadOnlyList<T> All => _all;

    public static void Clear()
    {
        _defs.Clear();
        _all.Clear();
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
            return;
        }

        _defs[def.DefName] = def;
        _all.Add(def);
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
}
