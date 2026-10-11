using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Catalog;
using Rimisekai.Character;
using Rimisekai.Combat;
using Rimisekai.Defs;
using Rimisekai.Hub;
using Rimisekai.Ink;
using Rimisekai.WorldMap;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>怪物掉装备：威胁等级掉率、委托首领保底、材质随危险等级（首领升档）、覆盖行、落袋可装备、结算显示、只从倒下者掉。</summary>
[Collection("Quest definition state")]
public sealed class GearDropTests
{
    private const int Seeds = 400;

    private static EnemyDef Foe(int threat, WeaponType? weapon = null, EnemyGear? gear = null) => new()
    {
        Id = $"gear_test_{threat}", Name = "试刀怪", ThreatTier = threat, Weapon = weapon, Gear = gear,
    };

    private static IEnumerable<LootGear> Drops(EnemyDef def, int tier, bool questBoss = false) =>
        Enumerable.Range(0, Seeds).SelectMany(seed => GearLoot.Roll(def, tier, questBoss, new Random(seed)));

    [Fact]
    public void Drop_rules_table_is_complete_and_names_real_materials()
    {
        ContentDefs.EnsureInitialized();
        var rules = GearDropDef.Standard;
        Assert.Equal(new[] { 1, 2, 3, 4 }, rules.Threats.Select(t => t.Threat).OrderBy(t => t));
        Assert.Equal(new[] { 1, 2, 3, 4 }, rules.Tiers.Select(t => t.Tier).OrderBy(t => t));
        foreach (var tier in rules.Tiers)
        {
            Assert.All(tier.Weapon, m => Assert.True(DefDatabase<MaterialDef>.Get(m)!.WeaponUsable, m));
            Assert.All(tier.Armor, m => Assert.True(DefDatabase<MaterialDef>.Get(m)!.ArmorUsable, m));
        }
        Assert.DoesNotContain(rules.Boss.Quality, q => q.Quality == Quality.Unique);
        Assert.DoesNotContain(rules.Minion.Quality, q => q.Quality == Quality.Unique);
        // 掉落的传说概率明显高于锻造（锻造 4%）。
        Assert.True(rules.Boss.Quality.Single(q => q.Quality == Quality.Legendary).Weight * 100 / rules.Boss.Quality.Sum(q => q.Weight) > 4);
        Assert.True(rules.Boss.EnhanceMax > 3, "掉落强化可越过锻造上限 +3");
    }

    [Fact]
    public void Threat_four_always_drops_and_sometimes_a_second_piece()
    {
        ContentDefs.EnsureInitialized();
        var counts = Enumerable.Range(0, Seeds)
            .Select(seed => GearLoot.Roll(Foe(4), 4, false, new Random(seed)).Count).ToList();
        Assert.All(counts, c => Assert.InRange(c, 1, 2));
        Assert.Contains(2, counts);
        // 威胁 1 的小兵只是偶尔掉。
        var minion = Enumerable.Range(0, Seeds).Count(seed => GearLoot.Roll(Foe(1), 1, false, new Random(seed)).Count > 0);
        Assert.InRange(minion, 1, Seeds / 4);
    }

    [Fact]
    public void Quest_boss_always_drops_at_least_one()
    {
        ContentDefs.EnsureInitialized();
        foreach (var threat in new[] { 1, 2, 3 })
            for (var seed = 0; seed < Seeds; seed++)
                Assert.NotEmpty(GearLoot.Roll(Foe(threat), 2, true, new Random(seed)));
    }

    [Fact]
    public void Materials_stay_in_the_danger_tier_window_and_bosses_may_step_up_to_ether()
    {
        ContentDefs.EnsureInitialized();
        var rules = GearDropDef.Standard;
        foreach (var tier in rules.Tiers)
        {
            // 小兵：只在本档窗口里。武器看武器池，防具饰品看防具池。
            foreach (var drop in Drops(Foe(2), tier.Tier, questBoss: true))
                Assert.Contains(drop.MaterialDefName, drop.Weapon != null ? tier.Weapon : tier.Armor);

            // 首领：窗口里的或沿材料表升一档的。
            var bossDrops = Drops(Foe(4), tier.Tier).ToList();
            foreach (var drop in bossDrops)
            {
                var weapon = drop.Weapon != null;
                var pool = weapon ? tier.Weapon : tier.Armor;
                var ladder = DefDatabase<MaterialDef>.All.Where(m => weapon ? m.WeaponUsable : m.ArmorUsable).OrderBy(m => m.Tier).ToList();
                var allowed = pool.Concat(pool.Select(p => ladder.FirstOrDefault(m => m.Tier > DefDatabase<MaterialDef>.Get(p)!.Tier)?.DefName ?? p)).ToHashSet();
                Assert.Contains(drop.MaterialDefName, allowed);
            }
            Assert.Contains(bossDrops, d => !(d.Weapon != null ? tier.Weapon : tier.Armor).Contains(d.MaterialDefName));
        }
        // 最高危险等级的首领能掉到以太。
        Assert.Contains(Drops(Foe(4), 4), d => d.MaterialDefName == "以太");
    }

    [Fact]
    public void Kind_follows_the_enemy_weapon_and_bosses_roll_better_quality()
    {
        ContentDefs.EnsureInitialized();
        var drops = Drops(Foe(4, WeaponType.Axe), 3).ToList();
        Assert.All(drops.Where(d => d.Weapon != null), d => Assert.Equal(WeaponType.Axe, d.Weapon!.Type));
        Assert.Contains(drops, d => d.Equip?.Kind == EquipKind.Armor);
        Assert.Contains(drops, d => d.Equip?.Kind == EquipKind.Accessory);
        Assert.DoesNotContain(drops, d => d.Quality is Quality.Crude or Quality.Unique);
        Assert.Contains(drops, d => d.Quality == Quality.Legendary);
        Assert.Contains(drops, d => (d.Weapon?.Enhance ?? d.Equip!.Enhance) > 3);
        // 饰品只进戒指或项链位。
        Assert.All(drops.Where(d => d.Equip?.Kind == EquipKind.Accessory),
            d => Assert.True(EquipSlots.Accepts(d.Equip!.Slot, EquipKind.Accessory)));
    }

    [Fact]
    public void Gear_override_row_fixes_kind_and_floors_material_and_quality()
    {
        ContentDefs.EnsureInitialized();
        // 骸骨王的示例覆盖：必掉钢或更好材质的剑——危险等级 3 的窗口里有铁，也照样不低于钢。
        var king = DefDatabase<MonsterDef>.GetNamed("skeleton_king");
        var lich = EnemyDef.Place(king, 1, king.ThreatTier, true, king.Cr);
        Assert.NotNull(lich.Gear);
        var steel = DefDatabase<MaterialDef>.Get("钢")!.Tier;
        for (var seed = 0; seed < Seeds; seed++)
        {
            var first = GearLoot.Roll(lich, 3, false, new Random(seed))[0];
            Assert.Equal(WeaponType.Sword, first.Weapon!.Type);
            Assert.True(first.Weapon.Material!.Tier >= steel, first.MaterialDefName);
        }

        var gear = new EnemyGear { Kind = EquipKind.Armor, Slot = EquipSlot.Head, MinMaterial = "秘银", MinQuality = Quality.Epic, RatePercent = 100 };
        for (var seed = 0; seed < Seeds; seed++)
        {
            var first = GearLoot.Roll(Foe(1, gear: gear), 1, false, new Random(seed))[0];
            Assert.Equal(EquipSlot.Head, first.Equip!.Slot);
            Assert.Equal("秘银", first.MaterialDefName);
            Assert.True(first.Quality >= Quality.Epic);
        }
        // 掉率 0 的覆盖行：这只怪的第一件不掉（威胁 1 不会再掷第二件）。
        var never = new EnemyGear { Kind = EquipKind.Weapon, RatePercent = 0 };
        Assert.Empty(Drops(Foe(1, gear: never), 1));
    }

    /// <summary>手搭一场：主角一刀砍翻一只威胁 4、覆盖行定死掉头盔的怪。</summary>
    private static Battle WonBattle(Rimisekai.Save.GameState state, EnemyDef def)
    {
        var master = state.Roster.Master!;
        master[CoreStat.Strength] = 30;
        master.Condition.RecoverFull();
        var battle = new Battle(d100: () => 0, controlled: CombatSide.Attacker) { DangerTier = 4 };
        var hero = Deploy.FromCharacter(master, CombatSide.Attacker, state.Weapons, state.Equips);
        battle.Add(hero);
        battle.Add(new Combatant { Id = 99, Side = CombatSide.Defender, Hp = 1, MaxHp = 10, Enemy = def });
        battle.StartBattle();
        while (battle.Outcome == CombatOutcome.Ongoing)
            Assert.True(battle.Act(new CombatAction { ActorId = battle.PendingActor!.Id, TargetId = 99 }));
        Assert.Equal(CombatOutcome.AttackerWin, battle.Outcome);
        return battle;
    }

    [Fact]
    public void Dropped_gear_lands_in_the_bag_is_equippable_and_shows_in_settlement()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 1);
        var master = state.Roster.Master!;
        var def = Foe(4, gear: new EnemyGear { Kind = EquipKind.Armor, Slot = EquipSlot.Head, RatePercent = 100 });
        var battle = WonBattle(state, def);

        var outcome = CombatSettlement.Settle(state, battle)!;
        Assert.NotEmpty(outcome.Loot.Gear);
        foreach (var gear in outcome.Loot.Gear)
        {
            Assert.Equal(1, master.Bag.Get(gear.Id));
            Assert.True(gear.Weapon != null ? state.Weapons.Exists(gear.Id) : state.Equips.Get(gear.Id) != null);
        }
        var helm = outcome.Loot.Gear[0].Equip!;
        Assert.Equal(EquipSlot.Head, helm.Slot);
        Assert.Contains(hub.GearOptions(EquipSlot.Head), o => o.ItemId == helm.Id);
        Assert.True(hub.EquipFromBag(master.Id, EquipSlot.Head, helm.Id));
        Assert.Equal(helm.Id, master.EquippedId(EquipSlot.Head));
        Assert.Equal(0, master.Bag.Get(helm.Id));

        // 结算弹窗的战利品条列出真实掉落：名字、品质跟着实例走。
        var page = InkModalFactory.CreateCombatSettlement(outcome.Result, outcome.Loot, state.Territory);
        foreach (var gear in outcome.Loot.Gear)
        {
            var strip = Assert.Single(page.Settlement!.Items, i => i.ItemId == gear.Id);
            Assert.Equal(gear.Name, strip.Label);
            Assert.Equal(gear.Quality, strip.Quality);
            Assert.Equal(1, strip.Count);
        }
    }

    [Fact]
    public void Only_fallen_enemies_of_a_won_battle_drop_gear()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 1);
        var battle = WonBattle(state, Foe(4));
        // 没倒下的（如逃散的）不掉：一只活着的威胁 4 混进名单也掷不出东西。
        var standing = new Combatant { Id = 100, Side = CombatSide.Defender, Hp = 10, MaxHp = 10, Enemy = Foe(4) };
        battle.Members.Add(standing);
        for (var seed = 0; seed < 50; seed++)
        {
            var loot = BattleLoot.Roll(battle, CombatSide.Defender, gearRng: new Random(seed));
            Assert.InRange(loot.Gear.Count, 1, 2);
        }

        // 没打赢：倒下的威胁 4 也不掉装备。
        var lost = new Battle(controlled: CombatSide.Attacker);
        lost.Add(new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 20, MaxHp = 20 });
        lost.Add(new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 0, MaxHp = 20, Enemy = Foe(4) });
        lost.Add(new Combatant { Id = 3, Side = CombatSide.Defender, Hp = 20, MaxHp = 20, Enemy = Foe(4) });
        Assert.Empty(BattleLoot.Roll(lost, CombatSide.Defender, gearRng: new Random(1)).Gear);
    }

    [Fact]
    public void Encounter_start_carries_danger_tier_and_marks_the_quest_boss()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 1);
        var foes = new List<EnemyDef> { new() { Id = "gear_test_boss", Name = "试刀首领", Boss = true, ThreatTier = 3, Column = 2 }, Foe(1) };
        var session = Encounters.Start(state, foes, dangerTier: 3, questBoss: true)!;
        Assert.Equal(3, session.Battle.DangerTier);
        var enemies = session.Battle.Members.Where(m => m.Side == CombatSide.Defender).ToList();
        Assert.Equal(new[] { true, false }, enemies.Select(m => m.QuestBoss));
        Assert.All(Encounters.Start(state, foes)!.Battle.Members, m => Assert.False(m.QuestBoss));
    }
}
