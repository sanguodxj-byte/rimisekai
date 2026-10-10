using Rimisekai.Combat;
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
            Assert.InRange(gen.Traits.Count, 2, 6);

            // 同组至多 2 条
            foreach (var group in gen.Traits.GroupBy(t => TraitGroups[t]))
                Assert.True(group.Count() <= 2, $"{seed}: {group.Key}组超限");

            // 互斥对不同选
            Assert.False(
                (gen.Traits.Contains("健谈") && gen.Traits.Contains("寡言")) ||
                (gen.Traits.Contains("情绪外露") && gen.Traits.Contains("内敛")) ||
                (gen.Traits.Contains("乐观") && gen.Traits.Contains("忧郁")) ||
                (gen.Traits.Contains("讨好") && gen.Traits.Contains("傲慢")) ||
                (gen.Traits.Contains("尽责") && gen.Traits.Contains("拖沓")) ||
                (gen.Traits.Contains("严谨") && gen.Traits.Contains("随性")) ||
                (gen.Traits.Contains("务实") && gen.Traits.Contains("空想")) ||
                (gen.Traits.Contains("懒散") && gen.Traits.Contains("尽责")) ||
                (gen.Traits.Contains("迟钝") && gen.Traits.Contains("聪慧")) ||
                (gen.Traits.Contains("怕痛") && gen.Traits.Contains("耐痛")) ||
                (gen.Traits.Contains("强韧") && gen.Traits.Contains("体弱")),
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
        var fLeaning = new[] { "体贴", "迎合", "多虑", "情绪外露", "内敛", "空想家", "敏感", "慈悲" };
        for (var seed = 0; seed < 200; seed++)
        {
            var gen = Roll(seed);
            var hasF = gen.Traits.Any(fLeaning.Contains);
            Assert.Equal(hasF, gen.Persona.Contains("F——"));
        }
    }

    [Fact]
    public void Core_attributes_are_forcibly_allocated_and_independent_from_identity()
    {
        var identitiesSeen = new HashSet<string>();
        for (var seed = 0; seed < 200; seed++)
        {
            var gen = Roll(seed);
            identitiesSeen.Add(gen.Identity);

            // 每个核心属性均获得保底基础（>=6）且有分配点，无 0 属性
            foreach (var stat in gen.State.Core)
                Assert.InRange(stat, 6, 18);

            // 速度强制分配在合理作战区间 [10, 16]
            Assert.InRange(gen.State.Core[(int)CoreStat.Speed], 10, 16);
            Assert.True(gen.State.Condition.Stamina >= 50);
        }

        // 验证：身份不限制属性高低（例如女仆也可以是高力量者，骑士也可以是高智力者）
        var strongMaid = RollManyUntil(1000, g => g.Identity == "女仆" && g.State.Core[(int)CoreStat.Strength] >= 11);
        Assert.NotNull(strongMaid);
    }

    [Fact]
    public void Life_skills_are_forcibly_allocated_and_independent_from_identity()
    {
        for (var seed = 0; seed < 100; seed++)
        {
            var gen = Roll(seed);
            // 生活 + 武器 + 流派经验合计一整池（一半按逻辑、一半随机）
            var totalExp = gen.State.LifeExp.Sum()
                + gen.State.Weapons.Sum(w => w.Exp)
                + gen.State.Styles.Sum(st => st.Exp);
            Assert.Equal(CharacterGenerator.ExpPool, totalExp);

            // 生活侧一定有经验落点（特质对口 + 随机散发，非战斗身份还有余量注入）
            Assert.True(gen.State.LifeExp.Sum() > 0);

            // 若自带武器，主武器至少分得逻辑战斗经验
            if (gen.State.MainWeapon != null)
            {
                var mainWp = gen.State.MainWeapon.Value;
                Assert.True(gen.State.Weapons[(int)mainWp].Exp >= CharacterGenerator.ExpPool / 10);
            }
            else
            {
                // 非战斗身份：逻辑余量注入生活技能
                Assert.True(gen.State.LifeExp.Sum() >= CharacterGenerator.ExpPool / 2);
            }
        }

        // 验证：生活技能与身份完全解耦（女仆可以精通锻造，学者可以精通烹饪）
        var smithMaid = RollManyUntil(1000, g => g.Identity == "女仆" && g.State.LifeExp[(int)LifeSkill.Smithing] >= 100);
        Assert.NotNull(smithMaid);
    }

    [Fact]
    public void Style_and_proficiencies_are_forcibly_allocated()
    {
        // 1. 验证战斗身份自带武器（如骑士带剑盾，无论特质与熟练度流派如何）
        var knight = RollManyUntil(1000, g => g.Identity == "骑士");
        Assert.NotNull(knight);
        Assert.Equal(WeaponType.Sword, knight!.State.MainWeapon);
        Assert.True(knight.State.OffHandShield);
        Assert.Equal(StyleType.Shield, knight.State.EquippedStyle);
        Assert.Equal(3, knight.State.ThreatTier);
        Assert.True(knight.State.Weapons[(int)WeaponType.Sword].Exp >= CharacterGenerator.ExpPool / 10);
        Assert.True(knight.State.Styles[(int)StyleType.Shield].Exp >= CharacterGenerator.ExpPool / 10);
        Assert.NotEmpty(SkillTable.Known(knight.State));

        // 2. 验证非战斗身份初始武器可空（如女仆出场无武器）
        var maid = RollManyUntil(1000, g => g.Identity == "女仆");
        Assert.NotNull(maid);
        Assert.Null(maid!.State.MainWeapon);
        Assert.Null(maid.State.OffWeapon);
        Assert.False(maid.State.OffHandShield);
        Assert.Null(maid.State.EquippedStyle);
        Assert.Equal(1, maid.State.ThreatTier);

        // 3. 普查 100 个随机种子：威胁等级与流派完全绑定，不存角色数据
        for (var seed = 0; seed < 100; seed++)
        {
            var gen = Roll(seed);
            if (gen.State.MainWeapon != null)
            {
                var style = gen.State.EquippedStyle!.Value;
                Assert.Equal(Rimisekai.Character.CharacterState.ThreatOf(style), gen.State.ThreatTier);
                Assert.InRange(gen.State.ThreatTier, 1, 3);
            }
            else
            {
                Assert.Null(gen.State.EquippedStyle);
                Assert.Equal(1, gen.State.ThreatTier);
            }
        }
    }

    [Fact]
    public void Pool_excludes_identity_and_merged_entries()
    {
        // 52 条池：女仆/魔法师是身份标识，胆小/诚实/自负已并入，都不能被掷出。
        var never = new HashSet<string> { "女仆", "魔法师", "胆小", "诚实", "自负" };
        Assert.Equal(71, Traits.Pool.Count);
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
        ["健谈"] = "外向", ["寡言"] = "外向", ["慢热"] = "外向", ["圆滑"] = "待人",
        ["易紧张"] = "情绪", ["情绪外露"] = "情绪", ["内敛"] = "情绪", ["乐观"] = "情绪",
        ["忧郁"] = "情绪", ["急躁"] = "情绪", ["沉稳"] = "情绪",
        ["体贴"] = "待人", ["毒舌"] = "待人", ["多虑"] = "情绪", ["迎合"] = "待人",
        ["傲慢"] = "待人", ["直率"] = "待人", ["护短"] = "待人",
        ["尽责"] = "做事", ["严谨"] = "做事", ["拖沓"] = "做事", ["有条理"] = "做事", ["随性"] = "做事",
        ["好学"] = "认知", ["书痴"] = "认知", ["务实"] = "认知", ["空想家"] = "认知",
        ["较真"] = "认知", ["迷信"] = "认知",
        ["吃货"] = "独特", ["畏暗"] = "独特", ["起床气"] = "独特", ["吝啬"] = "独特",
        ["大方"] = "独特", ["手巧"] = "独特", ["路痴"] = "独特", ["洁癖"] = "独特",
        ["念旧"] = "独特", ["夜猫子"] = "独特", ["口头禅"] = "独特", ["速咏"] = "独特",
        ["懒散"] = "做事", ["工匠"] = "做事",
        ["好奇"] = "认知", ["聪慧"] = "认知", ["迟钝"] = "认知",
        ["冷淡"] = "待人", ["叛逆"] = "待人",
        ["怕痛"] = "独特", ["耐痛"] = "独特", ["强韧"] = "独特",
        ["体弱"] = "独特", ["炼金"] = "独特",
        ["警觉"] = "认知", ["直觉"] = "认知", ["理性"] = "认知",
        ["淡漠"] = "情绪", ["敏感"] = "情绪", ["钝感"] = "情绪",
        ["合群"] = "外向", ["独狼"] = "外向",
        ["慈悲"] = "待人", ["记仇"] = "待人", ["忠心"] = "待人",
        ["果决"] = "做事", ["犹豫"] = "做事", ["执着"] = "做事", ["靠得住"] = "做事",
        ["爱冒险"] = "独特", ["苦行"] = "独特", ["好动"] = "独特",
    };
}
