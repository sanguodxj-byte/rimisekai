using System;
using System.Linq;
using Rimisekai.Catalog;
using Rimisekai.Combat;
using Rimisekai.Hub;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 身份技能池：内容表的形状（每身份 20 基础＋3 核心、法术至少咏唱一回合）、抽池必中核心、
/// 核心四类机制（姿态 / 机制点 / 反应 / 光环）在战斗里的结算、咏唱中挨弱化即打断、玩家只在领地里重抽。
/// </summary>
public class IdentitySkillPoolTests
{
    [Fact]
    public void Every_pool_has_twenty_basics_and_three_cores()
    {
        Assert.NotEmpty(SkillPool.Pools);
        foreach (var pool in SkillPool.Pools)
        {
            Assert.Equal(20, pool.Skills.Count(s => s.Core == CoreKind.None));
            Assert.Equal(3, pool.Skills.Count(s => s.Core != CoreKind.None));
            Assert.True(Defs.DefDatabase<Defs.IdentityDef>.Get(pool.DefName) != null, $"{pool.DefName} 不是身份");
        }
    }

    [Fact]
    public void Pool_skill_ids_are_unique_and_do_not_shadow_the_skill_chart()
    {
        var ids = SkillPool.Pools.SelectMany(p => p.Skills).Select(s => s.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.DoesNotContain(ids, id => Defs.DefDatabase<SkillDef>.Get(id) != null);
    }

    [Fact]
    public void Spells_chant_at_least_one_round_and_cores_are_complete()
    {
        foreach (var skill in SkillPool.Pools.SelectMany(p => p.Skills))
        {
            Assert.False(string.IsNullOrEmpty(skill.Name));
            if (skill.Kind == SkillKind.Spell)
                Assert.True(skill.ChantRounds >= 1, $"{skill.Id} 是法术却没有咏唱");
            switch (skill.Core)
            {
                case CoreKind.Stance or CoreKind.Aura:
                    Assert.NotEmpty(skill.Effects);
                    break;
                case CoreKind.Charge:
                    Assert.NotEqual(SkillTrigger.None, skill.Trigger);
                    Assert.NotEmpty(skill.Effects);
                    Assert.True(skill.MaxStacks >= 1 && skill.StatusRounds > 0, skill.Id);
                    break;
                case CoreKind.Reaction:
                    Assert.NotEqual(SkillTrigger.None, skill.Trigger);
                    Assert.True(skill.ReactPower > 0 && skill.ReactUses >= 1 && skill.StatusRounds > 0, skill.Id);
                    break;
            }
            if (skill.Core != CoreKind.None)
                Assert.False(string.IsNullOrEmpty(skill.Description), $"{skill.Id} 核心技能缺说明");
        }
    }

    [Fact]
    public void A_draw_holds_all_three_cores_and_distinct_basics_of_that_identity()
    {
        foreach (var pool in SkillPool.Pools)
            for (var seed = 0; seed < 50; seed++)
            {
                var drawn = SkillPool.Draw(pool, new Random(seed));
                Assert.Equal(3 + SkillPool.BasicDraw, drawn.Distinct().Count());
                Assert.All(drawn, id => Assert.True(pool.Skills.Any(s => s.Id == id) || pool.SharedSkills.Contains(id), id));
                Assert.Equal(3, drawn.Count(id => SkillTable.Get(id)!.Core != CoreKind.None));
            }
    }

    [Fact]
    public void Shared_skills_are_existing_general_skills()
    {
        foreach (var pool in SkillPool.Pools)
            Assert.All(pool.SharedSkills, id => Assert.Contains(SkillTable.All, s => s.Id == id && !SkillLearning.Innate(s)));
    }

    [Fact]
    public void Tree_sorts_by_tier_downwards_and_keeps_the_cores_in_the_middle_row()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 7);
        var master = state.Roster.Master!;
        foreach (var pool in SkillPool.Pools)
        {
            Assert.True(hub.RerollSkillPool(pool.DefName, new Random(3)));
            var slots = SkillTree.Layout(master);
            Assert.Equal(slots.Count, slots.Select(s => s.Def.Id).Distinct().Count());
            Assert.Equal(slots.Count, slots.Select(s => (s.Row, s.Column)).Distinct().Count());
            Assert.All(slots, s => Assert.InRange(s.Column, 0f, SkillTree.Columns - 1));
            var basics = slots.Where(s => s.Def.Core == CoreKind.None).OrderBy(s => s.Row).ToList();
            for (var i = 1; i < basics.Count; i++)
                Assert.True(SkillTier.Of(basics[i - 1].Def) <= SkillTier.Of(basics[i].Def), $"{pool.DefName}: {basics[i - 1].Def.Id} → {basics[i].Def.Id}");
            var cores = slots.Where(s => s.Def.Core != CoreKind.None).ToList();
            Assert.Equal(3, cores.Count);
            Assert.Single(cores.Select(s => s.Row).Distinct());
            var rows = slots.Max(s => s.Row) + 1;
            Assert.Equal((rows - 1) / 2, cores[0].Row);
            Assert.All(pool.SharedSkills, id => Assert.Contains(slots, s => s.Def.Id == id));
        }
    }

    private static (Battle Battle, Combatant Hero, Combatant Foe) Duel(Func<int> d100, int foeHp = 5000)
    {
        var battle = new Battle(d100: d100);
        var hero = new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 500, MaxHp = 500, StrikePower = 40, SpellPower = 20, Dodge = 20, Speed = 40 };
        var foe = new Combatant { Id = 2, Side = CombatSide.Defender, Hp = foeHp, MaxHp = foeHp, StrikePower = 5, Speed = 10 };
        battle.Add(hero);
        battle.Add(foe);
        battle.StartBattle();
        return (battle, hero, foe);
    }

    [Fact]
    public void Charge_stacks_on_hit_and_raises_skill_damage()
    {
        var (battle, hero, foe) = Duel(() => 0);
        hero.Passives.Add("spellblade_c1");
        Assert.True(battle.Act(new CombatAction { ActorId = 1, TargetId = 2 }));
        var first = battle.Events.Last(e => e.Kind == CombatEventKind.Hit && e.ActorId == 1).Amount;
        Assert.Equal(1, hero.Statuses.Single(s => s.Token == "spellblade_c1").Stacks);
        Assert.Equal(130, hero.DamageScale);
        Assert.True(battle.Act(new CombatAction { ActorId = 1, TargetId = 2 }));
        var second = battle.Events.Last(e => e.Kind == CombatEventKind.Hit && e.ActorId == 1).Amount;
        Assert.True(second > first);
        Assert.Equal(2, hero.Statuses.Single(s => s.Token == "spellblade_c1").Stacks);
    }

    [Fact]
    public void Stance_stays_for_the_battle_and_replaces_the_previous_stance()
    {
        var (battle, hero, _) = Duel(() => 0);
        hero.Skills.Add("spellblade_c2");
        hero.Skills.Add("knight_c1");
        Assert.True(battle.Act(new CombatAction { ActorId = 1, SkillId = "spellblade_c2", TargetId = 1 }));
        Assert.Equal(26, hero.EffDodge);
        Assert.Equal(45, hero.EffSpeed);
        for (var i = 0; i < 5; i++)
            battle.AdvanceRound();
        Assert.Contains(hero.Statuses, s => s.Kind == StatusKind.Stance);
        battle.Find(1)!.NextActAt = battle.Time;
        Assert.True(battle.Act(new CombatAction { ActorId = 1, SkillId = "knight_c1", TargetId = 1 }));
        Assert.Equal("knight_c1", hero.Statuses.Single(s => s.Kind == StatusKind.Stance).Token);
    }

    [Fact]
    public void Reaction_on_dodge_strikes_every_enemy()
    {
        var battle = new Battle(d100: () => 99);
        var hero = new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 500, MaxHp = 500, StrikePower = 40, Dodge = 20, Speed = 40 };
        var a = new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 5000, MaxHp = 5000, StrikePower = 5, Speed = 10, Column = 1 };
        var b = new Combatant { Id = 3, Side = CombatSide.Defender, Hp = 5000, MaxHp = 5000, StrikePower = 5, Speed = 10, Column = 2 };
        battle.Add(hero);
        battle.Add(a);
        battle.Add(b);
        battle.StartBattle();
        hero.Skills.Add("spellblade_c3");
        Assert.True(battle.Act(new CombatAction { ActorId = 1, SkillId = "spellblade_c3", TargetId = 1 }));
        hero.NextActAt = battle.Time + 2000; // 让敌人先出手：落空，反应发动
        _ = battle.PendingActor;
        Assert.Contains(battle.Events, e => e.Kind == CombatEventKind.React && e.ActorId == 1);
        Assert.True(a.Hp < 5000 && b.Hp < 5000);
        Assert.DoesNotContain(hero.Statuses, s => s.Token == "spellblade_c3");
    }

    [Fact]
    public void Any_debuff_breaks_a_chant()
    {
        var (battle, hero, foe) = Duel(() => 0);
        foe.Chanting = "mage_01";
        foe.ChantFireAt = battle.Time + 5000;
        hero.Skills.Add("knight_14");
        Assert.True(battle.Act(new CombatAction { ActorId = 1, SkillId = "knight_14", TargetId = 2 }));
        Assert.Null(foe.Chanting);
        Assert.Contains(battle.Events, e => e.Kind == CombatEventKind.Interrupt && e.ActorId == 2);
    }

    [Fact]
    public void Generated_characters_draw_from_their_identity_and_the_player_rerolls_only_at_home()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 7);
        var master = state.Roster.Master!;
        Assert.Equal(master.Identity, master.PoolIdentity);
        Assert.Equal(SkillPool.PoolOf(master.Identity) == null ? 0 : 3 + SkillPool.BasicDraw, master.SkillPool.Count);
        Assert.True(hub.RerollSkillPool("魔剑士", new Random(1)));
        Assert.Equal("魔剑士", master.PoolIdentity);
        Assert.All(master.SkillPool, id => Assert.True(id.StartsWith("spellblade_") || SkillPool.PoolOf("魔剑士")!.SharedSkills.Contains(id)));
        Assert.Contains(SkillTable.Known(master), s => s.Id == master.SkillPool[0]);
        hub.SetLayer(MapLayer.World);
        Assert.False(hub.RerollSkillPool("魔法师"));
        Assert.Equal("魔剑士", master.PoolIdentity);
    }
}
