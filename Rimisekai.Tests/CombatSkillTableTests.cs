using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Catalog;
using Rimisekai.Character;
using Rimisekai.Combat;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 能力表：技能按「流派 × 熟练等级」解锁；部署时只带上已解锁的能力。
/// </summary>
public sealed class CombatSkillTableTests
{
    private static CharacterState swordsman(int styleLevel)
    {
        var c = new CharacterState(1) { Name = "剑士" };
        c.Equip(WeaponType.Sword);
        c.Styles[(int)StyleType.OneHand].AddExp(styleLevel * Proficiency.ExpPerLevel);
        return c;
    }

    [Fact]
    public void Skill_table_has_three_tiers_per_style_plus_universal()
    {
        // 通用三式（攻击、防御、饮药剂）+ 七流派技能（持盾系只定了铁壁一项）逐条都有名字与流派归属。
        Assert.Equal(24, SkillTable.All.Count);
        foreach (var skill in SkillTable.All)
            Assert.False(string.IsNullOrWhiteSpace(skill.Name));
        // 通用能力（attack/guard）无流派门槛，其余全部归属一个流派。
        Assert.Null(SkillTable.Get(BattleSkills.AttackId)!.Gate.Style);
        Assert.Null(SkillTable.Get(BattleSkills.GuardId)!.Gate.Style);
        Assert.NotNull(SkillTable.Get("slash")!.Gate.Style);
    }

    /// <summary>门槛是一组可空条件：只写自己在意的项，其余为空即不设条件。</summary>
    [Fact]
    public void Gate_conditions_are_optional_and_all_must_hold()
    {
        var c = new CharacterState(1) { Name = "试炼者" };
        c.Equip(WeaponType.Sword);
        c.Styles[(int)StyleType.OneHand].AddExp(6 * Proficiency.ExpPerLevel);
        c[CoreStat.Strength] = 12;
        c.LifeExp[(int)LifeSkill.Smithing] = 3 * 100;

        var unlocked = new HashSet<string> { "slash" };
        var empty = new SkillGate();
        Assert.True(empty.Meets(c, unlocked));

        // 四条条件都满足即解锁。
        var full = new SkillGate
        {
            Style = StyleType.OneHand,
            StyleLevel = 6,
            Core = new[] { new CoreRequirement(CoreStat.Strength, 12) },
            Life = new[] { new LifeRequirement(LifeSkill.Smithing, 3) },
            Prerequisites = new[] { "slash" },
        };
        Assert.True(full.Meets(c, unlocked));

        // 任一条不满足即锁住，并逐条列在 Unmet 里。
        var tooStrong = new SkillGate
        {
            Style = StyleType.OneHand,
            StyleLevel = 6,
            Core = new[] { new CoreRequirement(CoreStat.Strength, 13) },
        };
        Assert.False(tooStrong.Meets(c, unlocked));
        var misses = tooStrong.Unmet(c, unlocked);
        Assert.Single(misses);
        Assert.Equal(SkillGateKind.CoreStat, misses[0].Kind);
        Assert.Equal(13, misses[0].Required);
        Assert.Equal(12, misses[0].Actual);

        // 前置未会：同样锁住，且 Unmet 指名是哪一项。
        var needsPrereq = new SkillGate { Prerequisites = new[] { "armor_break" } };
        Assert.False(needsPrereq.Meets(c, unlocked));
        var prereqMiss = needsPrereq.Unmet(c, unlocked);
        Assert.Single(prereqMiss);
        Assert.Equal("armor_break", prereqMiss[0].SubjectId);
    }

    /// <summary>素质门槛走 Traits 门面，且要求"须具备"。</summary>
    [Fact]
    public void Gate_can_require_traits()
    {
        var c = new CharacterState(1) { Name = "试炼者" };
        var gate = new SkillGate { Traits = new[] { Trait.FastLearner } };

        Assert.False(gate.Meets(c, new HashSet<string>()));
        c.Grant(Trait.FastLearner);
        Assert.True(gate.Meets(c, new HashSet<string>()));
    }

    /// <summary>前置技能可以成链：A→B→C 一次求解全解锁，不靠调用方排序。</summary>
    [Fact]
    public void Known_solves_prerequisite_chains()
    {
        var c = new CharacterState(1) { Name = "试炼者" };
        c.Equip(WeaponType.Sword);
        c.Styles[(int)StyleType.OneHand].AddExp(6 * Proficiency.ExpPerLevel);

        // 故意把带前置的技能排在它前置之前，逼求解器靠不动点补上顺序。
        var chain = new[]
        {
            new SkillDef
            {
                Id = "chain_c", Name = "收式",
                Gate = new SkillGate { Prerequisites = new[] { "chain_b" } },
                Kind = SkillKind.Strike, Target = SkillTarget.Enemy,
            },
            new SkillDef
            {
                Id = "chain_b", Name = "接续",
                Gate = new SkillGate { Prerequisites = new[] { "chain_a" } },
                Kind = SkillKind.Strike, Target = SkillTarget.Enemy,
            },
            new SkillDef
            {
                Id = "chain_a", Name = "起手", Gate = SkillGate.Open,
                Kind = SkillKind.Strike, Target = SkillTarget.Enemy,
            },
        };

        var known = SkillTable.Known(c, chain).Select(s => s.Id).ToList();
        Assert.Equal(new[] { "chain_c", "chain_b", "chain_a" }, known);

        // 断链时（缺起手）整条链都锁着。
        var broken = new[]
        {
            chain[0],
            chain[1],
        };
        Assert.Empty(SkillTable.Known(c, broken));
    }

    [Fact]
    public void Unlocks_by_style_proficiency_level()
    {
        // 熟练 1 级：只会初级（疾斩）与通用两式。
        var novice = SkillTable.Known(swordsman(1))
            .Select(s => s.Id).ToList();
        Assert.Contains("slash", novice);
        Assert.Contains(BattleSkills.AttackId, novice);
        Assert.DoesNotContain("armor_break", novice);
        Assert.DoesNotContain("cross_slash", novice);

        // 熟练 3 级：进阶解锁。
        var journeyman = SkillTable.Known(swordsman(3))
            .Select(s => s.Id).ToList();
        Assert.Contains("armor_break", journeyman);
        Assert.DoesNotContain("cross_slash", journeyman);

        // 熟练 6 级：高级解锁。
        var master = SkillTable.Known(swordsman(6))
            .Select(s => s.Id).ToList();
        Assert.Contains("cross_slash", master);

        // 换流派就不带单手的能力：法术流派的角色没有疾斩。
        var mage = new CharacterState(2) { Name = "法师" };
        mage.Equip(WeaponType.Staff);
        mage.Styles[(int)StyleType.Spell].AddExp(9 * Proficiency.ExpPerLevel);
        Assert.DoesNotContain("slash", SkillTable.Known(mage)
            .Select(s => s.Id).ToList());
    }

    [Fact]
    public void Deploy_carries_known_skills_of_the_equipped_style()
    {
        var c = swordsman(3);   // 单手熟练 3：会疾斩与破甲
        var unit = Deploy.FromCharacter(c, CombatSide.Attacker);

        Assert.Contains(BattleSkills.AttackId, unit.Skills);
        Assert.Contains(BattleSkills.GuardId, unit.Skills);
        Assert.Contains("slash", unit.Skills);
        Assert.Contains("armor_break", unit.Skills);
        // 没到 6 级，高级不该出现。
        Assert.DoesNotContain("cross_slash", unit.Skills);
        // 别流派的能力不该出现。
        Assert.DoesNotContain("magic_missile", unit.Skills);
    }

    [Fact]
    public void Universal_skills_come_with_any_style()
    {
        // 法术流派的角色也自带普通攻击与防御架势。
        var mage = new CharacterState(2) { Name = "法师" };
        mage.Equip(WeaponType.Staff);
        mage.Styles[(int)StyleType.Spell].AddExp(Proficiency.ExpPerLevel);
        var unit = Deploy.FromCharacter(mage, CombatSide.Attacker);

        Assert.Contains(BattleSkills.AttackId, unit.Skills);
        Assert.Contains(BattleSkills.GuardId, unit.Skills);
        Assert.Contains("magic_missile", unit.Skills);
    }
}

/// <summary>
/// 法术咏唱：出招入咏唱、走完倒计时施放、被控制命中即打断、速咏减回合。
/// 触碰 PendingActor 会推进回合——非控制方由 AI 代打。
/// </summary>
public sealed class ChantTests
{
    private static SkillDef Spell(int chantRounds, bool control = false) => new()
    {
        Id = "test_spell", Name = "试验法术",
        Gate = new SkillGate { Style = StyleType.Spell, StyleLevel = 1 },
        Kind = SkillKind.Spell, Target = SkillTarget.Enemy, Power = 100,
        ChantRounds = chantRounds, Control = control,
    };

    private static (Battle Battle, Combatant Caster, Combatant Enemy) Setup(
        SkillDef spell, Func<int>? d100 = null, SkillDef[]? extraCatalog = null, params string[] enemySkills)
    {
        // 自定义技能走目录注册：目录优先于内置能力表（既有约定）。
        var catalog = new GameCatalog();
        catalog.Skills[spell.Id] = spell;
        if (extraCatalog != null)
            foreach (var extra in extraCatalog)
                catalog.Skills[extra.Id] = extra;
        var battle = new Battle(catalog, d100: d100);
        battle.Add(new Combatant
        {
            Id = 1, Name = "施法者", Side = CombatSide.Attacker,
            Hp = 100, MaxHp = 100, SpellPower = 50,
        });
        battle.Add(new Combatant { Id = 2, Name = "敌人", Side = CombatSide.Defender, Hp = 500, MaxHp = 500 });
        battle.Find(1)!.Skills.Add(spell.Id);
        foreach (var id in enemySkills)
            battle.Find(2)!.Skills.Add(id);
        return (battle, battle.Find(1)!, battle.Find(2)!);
    }

    [Fact]
    public void Spell_enters_chant_and_fires_after_countdown()
    {
        // 两回合咏唱：出招当轮入咏唱不掉血；敌人 AI 代打一轮，倒计时 2→1；
        // 第二轮咏唱者被跳过，轮末倒计时 1→0，法术施放。
        var (battle, caster, enemy) = Setup(Spell(2), d100: () => 99);

        Assert.True(battle.Act(new CombatAction { ActorId = 1, SkillId = "test_spell", TargetId = 2 }));
        Assert.Equal("test_spell", caster.Chanting);
        // 两回合咏唱：完成时点 = 2 × 轮长。
        Assert.Equal(2000, caster.ChantFireAt - battle.Time);
        Assert.Equal(500, enemy.Hp);

        // 咏唱期间不行动；回合推进走完两轮咏唱即施放（开始咏唱 + 敌我各一动）。
        _ = battle.PendingActor;
        Assert.Null(caster.Chanting);
        Assert.True(enemy.Hp < 500);
        Assert.Contains(battle.Events, e => e.Kind == CombatEventKind.Chant);
        Assert.Contains(battle.Events, e => e.Kind == CombatEventKind.SpellFire);
    }

    [Fact]
    public void Control_hit_interrupts_the_chant()
    {
        // 控制骑在打击上（击晕打）：命中即打断咏唱。
        var controlStrike = new SkillDef
        {
            Id = "test_control", Name = "击晕打", Gate = SkillGate.Open,
            Kind = SkillKind.Strike, Target = SkillTarget.Enemy, Power = 30,
            Control = true,
        };
        var (battle, caster, enemy) = Setup(Spell(2), d100: () => 0, new[] { controlStrike }, "test_control");
        enemy.Skills.Clear();
        enemy.Skills.Add("test_control");   // 敌人只会控制打击：AI 必用它

        Assert.True(battle.Act(new CombatAction { ActorId = 1, SkillId = "test_spell", TargetId = 2 }));
        Assert.Equal("test_spell", caster.Chanting);

        // 敌人 AI 用控制打击命中咏唱者：咏唱被打断，法术作废。
        _ = battle.PendingActor;
        Assert.Null(caster.Chanting);
        Assert.Contains(battle.Events, e => e.Kind == CombatEventKind.Interrupt);
        Assert.DoesNotContain(battle.Events, e => e.Kind == CombatEventKind.SpellFire);
    }

    [Fact]
    public void Quick_chant_passive_reduces_rounds_with_floor_one()
    {
        // 一回合咏唱的法术：速咏也只能保持一回合（下限）。
        var caster = new Combatant
        {
            Id = 1, Name = "速咏者", Side = CombatSide.Attacker,
            Hp = 100, MaxHp = 100, SpellPower = 50, QuickChant = true,
        };
        caster.Skills.Add("magic_missile");
        var battle = new Battle(d100: () => 99);
        battle.Add(caster);
        battle.Add(new Combatant { Id = 2, Name = "敌人", Side = CombatSide.Defender, Hp = 500, MaxHp = 500 });

        Assert.True(battle.Act(new CombatAction { ActorId = 1, SkillId = "magic_missile", TargetId = 2, TargetColumn = 1 }));
        Assert.Equal("magic_missile", caster.Chanting);
        // 速咏 + 一回合咏唱：完成时点压到下一轮界。
        Assert.Equal(1000, caster.ChantFireAt - battle.Time);
    }
}
