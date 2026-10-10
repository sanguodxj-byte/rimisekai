using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Rimisekai.Defs;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// defName 与 label 的唯一性。
///
/// 覆盖面三处：每张表内部不许有同名（撞名的行查不到，等于死行）、
/// 物品表（ThingDef + MaterialDef）之间也不许撞（铁既是一件东西也是一种材料，
/// 全项目只能有一份）。
/// 行动与工种同名的（Sleep/Haul 这类）是**有意配对**：工种驱动行动，
/// 不在此列。没有 label 的行按 defName 索引（作物这类），也不按撞名算。
/// </summary>
public class DefUniquenessTests
{
    public DefUniquenessTests() => DefaultDefs.EnsureInitialized();

    [Fact]
    public void No_duplicate_defName_within_one_table()
    {
        foreach (var (type, names) in Duplicates(d => d.DefName))
            Assert.True(names.Count == 0,
                $"{type.Name} 表内有重复 defName：{string.Join("、", names)}");
    }

    [Fact]
    public void No_duplicate_label_within_one_table()
    {
        foreach (var (type, labels) in Duplicates(d => d.Label))
            Assert.True(labels.Count == 0,
                $"{type.Name} 表内有重复 label：{string.Join("、", labels)}");
    }

    [Fact]
    public void Items_never_repeat_a_defName_or_a_label()
    {
        var items = DefsOf(typeof(ThingDef)).Concat(DefsOf(typeof(MaterialDef))).ToList();
        AssertNoDuplicate(items, d => d.DefName, "物品");
        AssertNoDuplicate(items, d => d.Label, "物品");
    }

    private static void AssertNoDuplicate(
        IEnumerable<Def> defs, Func<Def, string> key, string what)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dup = new List<string>();
        foreach (var def in defs)
        {
            if (key(def).Length == 0)
                continue;
            if (!seen.Add(key(def)))
                dup.Add(key(def));
        }
        Assert.True(dup.Count == 0,
            $"{what}有重名：{string.Join("、", dup.Distinct())}");
    }

    private static IEnumerable<(Type Type, List<string> Names)> Duplicates(
        Func<Def, string> key) =>
        typeof(Def).Assembly.GetTypes()
            .Where(t => typeof(Def).IsAssignableFrom(t) && !t.IsAbstract)
            .Select(t => (Type: t, Defs: DefsOf(t).ToList()))
            .Where(pair => pair.Defs.Count > 0)
            .Select(pair => (pair.Type, Dups(pair.Defs, key)))
            .ToList();

    private static List<string> Dups(List<Def> defs, Func<Def, string> key) =>
        defs.Where(d => key(d).Length > 0)
            .GroupBy(key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

    private static IEnumerable<Def> DefsOf(Type defType)
    {
        var database = typeof(DefDatabase<>).MakeGenericType(defType);
        var all = database.GetProperty(nameof(DefDatabase<Def>.All), BindingFlags.Public | BindingFlags.Static);
        if (all?.GetValue(null) is not System.Collections.IEnumerable items)
            return Enumerable.Empty<Def>();
        return items.Cast<Def>();
    }
}
