using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Character;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 运行时角色生成器：槽位与互斥规则、可复现性、身份素质、persona 完整性。
/// </summary>
public sealed class CharacterGeneratorTests
{
    [Fact]
    public void Same_seed_produces_identical_characters()
    {
        var (a, b) = (Roll(42), Roll(42));
        Assert.Equal(a.Persona, b.Persona);
        Assert.Equal(a.Summary, b.Summary);
        Assert.Equal(a.Identity, b.Identity);
        Assert.Equal(a.Traits, b.Traits);
        Assert.Equal(a.State.Name, b.State.Name);
        Assert.Equal(
            a.State.Talents.OrderBy(x => x),
            b.State.Talents.OrderBy(x => x));
    }

    [Fact]
    public void Rolls_respect_slot_and_exclusion_rules()
    {
        for (var seed = 0; seed < 400; seed++)
        {
            var gen = Roll(seed);
            Assert.InRange(gen.Traits.Count, 2, 4);

            // 同组至多 2 条
            foreach (var group in gen.Traits.GroupBy(t => TraitGroups[t]))
                Assert.True(group.Count() <= 2, $"{seed}: {group.Key}组超限");

            // 互斥对不同选
            Assert.False(
                (gen.Traits.Contains("健谈") && gen.Traits.Contains("寡言")) ||
                (gen.Traits.Contains("情绪外露") && gen.Traits.Contains("静水深流")) ||
                (gen.Traits.Contains("乐观") && gen.Traits.Contains("忧郁")) ||
                (gen.Traits.Contains("讨好") && gen.Traits.Contains("傲慢")) ||
                (gen.Traits.Contains("尽责") && gen.Traits.Contains("拖沓")) ||
                (gen.Traits.Contains("完美主义") && gen.Traits.Contains("随性")) ||
                (gen.Traits.Contains("务实") && gen.Traits.Contains("爱幻想")) ||
                (gen.Traits.Contains("懒散") && gen.Traits.Contains("尽责")) ||
                (gen.Traits.Contains("迟钝") && gen.Traits.Contains("聪慧")) ||
                (gen.Traits.Contains("怕痛") && gen.Traits.Contains("不觉痛")) ||
                (gen.Traits.Contains("恢复快") && gen.Traits.Contains("恢复慢")),
                $"{seed}: 互斥对同选");
        }
    }

    [Fact]
    public void Maid_identity_grants_maid_trait_with_address_in_persona()
    {
        var hit = RollManyUntil(500, g => g.Identity == "女仆");
        Assert.NotNull(hit);
        Assert.True(hit!.State.Has(Trait.Maid));
        Assert.Contains("请吩咐", hit.Persona);
    }

    [Fact]
    public void Mage_identity_grants_mage_trait()
    {
        var hit = RollManyUntil(500, g => g.Identity == "魔法师");
        Assert.NotNull(hit);
        Assert.True(hit!.State.Has(Trait.Mage));
    }

    [Fact]
    public void Persona_baseline_matches_trait_mix()
    {
        var fLeaning = new[] { "体贴入微", "讨好", "爱操心", "情绪外露", "静水深流", "爱幻想" };
        for (var seed = 0; seed < 200; seed++)
        {
            var gen = Roll(seed);
            var hasF = gen.Traits.Any(fLeaning.Contains);
            Assert.Equal(hasF, gen.Persona.Contains("F——"));
        }
    }

    [Fact]
    public void Core_attributes_land_in_rolled_range_with_identity_bonus()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var gen = Roll(seed);
            foreach (var stat in gen.State.Core)
                Assert.InRange(stat, 3, 10);
        }

        // 身份加成可见：魔法师的智力应不低于其力量。
        var mage = RollManyUntil(500, g => g.Identity == "魔法师");
        Assert.NotNull(mage);
        Assert.True(mage!.State.Core[(int)CoreStat.Intellect] >= mage.State.Core[(int)CoreStat.Strength]);
    }

    [Fact]
    public void Pool_excludes_identity_and_merged_entries()
    {
        // 52 条池：女仆/魔法师是身份标识，胆小/诚实/自负已并入，都不能被掷出。
        var never = new HashSet<string> { "女仆", "魔法师", "胆小", "诚实", "自负" };
        Assert.Equal(52, Traits.Pool.Count);
        for (var seed = 0; seed < 300; seed++)
        {
            var gen = Roll(seed);
            Assert.Empty(gen.Traits.Where(never.Contains));
        }
    }

    [Fact]
    public void Starting_favor_stays_in_none_tier()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var gen = Roll(seed);
            Assert.True(gen.State.Condition.Favor < 100);
        }
    }

    [Fact]
    public void Names_never_collide_with_existing_members()
    {
        var roster = new Roster();
        roster.Add("璐米埃尔", master: true);
        var gen = new CharacterGenerator(new System.Random(7));
        var names = new HashSet<string> { "璐米埃尔" };
        for (var i = 0; i < 100; i++)
        {
            var g = gen.Generate(roster);
            Assert.DoesNotContain(g.State.Name, names);
            names.Add(g.State.Name);
        }
        Assert.Equal(101, roster.Members.Count);
    }

    // ---------- 辅助 ----------

    private static GeneratedCharacter Roll(int seed)
    {
        var gen = new CharacterGenerator(new System.Random(seed));
        return gen.Generate(new Roster());
    }

    private static GeneratedCharacter? RollManyUntil(int maxSeeds, Func<GeneratedCharacter, bool> predicate)
    {
        for (var seed = 0; seed < maxSeeds; seed++)
        {
            var g = Roll(seed);
            if (predicate(g))
                return g;
        }
        return null;
    }

    private static string group_of(GeneratedCharacter gen, string trait) => TraitGroups[trait];

    /// <summary>与生成器特质池同源的 名字→组 映射（校验用）。</summary>
    private static readonly Dictionary<string, string> TraitGroups = new()
    {
        ["健谈"] = "外向", ["寡言"] = "外向", ["慢热"] = "外向", ["场面应答"] = "外向",
        ["易紧张"] = "情绪", ["情绪外露"] = "情绪", ["静水深流"] = "情绪", ["乐观"] = "情绪",
        ["忧郁"] = "情绪", ["脾气急"] = "情绪", ["泰然"] = "情绪",
        ["体贴入微"] = "待人", ["毒舌"] = "待人", ["爱操心"] = "待人", ["讨好"] = "待人",
        ["傲慢"] = "待人", ["直率"] = "待人", ["护短"] = "待人",
        ["尽责"] = "做事", ["完美主义"] = "做事", ["拖沓"] = "做事", ["有条理"] = "做事", ["随性"] = "做事",
        ["好学"] = "认知", ["书卷气"] = "认知", ["务实"] = "认知", ["爱幻想"] = "认知",
        ["较真"] = "认知", ["迷信"] = "认知",
        ["吃货"] = "独特", ["怕鬼"] = "独特", ["起床气"] = "独特", ["守财"] = "独特",
        ["大方"] = "独特", ["手巧"] = "独特", ["路痴"] = "独特", ["爱干净"] = "独特",
        ["旧物情结"] = "独特", ["夜猫子"] = "独特", ["口癖尾"] = "独特",
        ["懒散"] = "做事", ["工匠"] = "做事",
        ["好奇"] = "认知", ["聪慧"] = "认知", ["迟钝"] = "认知",
        ["冷淡"] = "待人", ["桀骜"] = "待人",
        ["怕痛"] = "独特", ["不觉痛"] = "独特", ["恢复快"] = "独特",
        ["恢复慢"] = "独特", ["炼金"] = "独特",
    };
}
