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

/// <summary>
/// 怪物掉装备：掉率、材质窗口、品质按挑战等级（CR）分段；正主（遭遇首领）走首领那套，委托首领保底；
/// 站位层（threatTier）不影响掉落；覆盖行、落袋可装备、结算显示、只从倒下者掉。
/// </summary>
[Collection("Quest definition state")]
public sealed class GearDropTests
{
    private const int Seeds = 400;

    private static ChallengeRating Cr(string text) => ChallengeRating.Parse(text);

    private static EnemyDef Foe(string cr, bool boss = false, WeaponType? weapon = null, EnemyGear? gear = null, int threatTier = 1) => new()
    {
        Id = $"gear_test_{cr}", Name = "试刀怪", Cr = Cr(cr), Boss = boss, ThreatTier = threatTier, Weapon = weapon, Gear = gear,
    };

    private static IEnumerable<LootGear> Drops(EnemyDef def, bool questBoss = false) =>
        Enumerable.Range(0, Seeds).SelectMany(seed => GearLoot.Roll(def, questBoss, new Random(seed)));

    [Fact]
    public void Drop_rules_table_is_complete_and_names_real_materials()
    {
        ContentDefs.EnsureInitialized();
        var rules = GearDropDef.Standard;
        Assert.Equal(Cr("0"), rules.Bands[0].CrMin);
        Assert.All(rules.Bands.Zip(rules.Bands.Skip(1)), p =>
        {
            Assert.True(p.Second.CrMin > p.First.CrMin, "CR 段按 crMin 升序");
            Assert.True(p.Second.DropPercent >= p.First.DropPercent, "CR 越高掉率不降");
        });
        foreach (var band in rules.Bands)
        {
            Assert.All(band.Weapon, m => Assert.True(DefDatabase<MaterialDef>.Get(m)!.WeaponUsable, m));
            Assert.All(band.Armor, m => Assert.True(DefDatabase<MaterialDef>.Get(m)!.ArmorUsable, m));
            Assert.DoesNotContain(band.Quality, q => q.Quality == Quality.Unique);
        }
        Assert.DoesNotContain(rules.Boss.Quality, q => q.Quality == Quality.Unique);
        // 掉落的传说概率明显高于锻造（锻造 4%）。
        Assert.True(rules.Boss.Quality.Single(q => q.Quality == Quality.Legendary).Weight * 100 / rules.Boss.Quality.Sum(q => q.Weight) > 4);
        Assert.True(rules.Boss.EnhanceMax > 3, "掉落强化可越过锻造上限 +3");
        Assert.Equal(Cr("2"), rules.Band(Cr("4")).CrMin);
        Assert.Equal(Cr("14"), rules.Band(Cr("30")).CrMin);
    }

    [Fact]
    public void Leaders_always_drop_and_sometimes_a_second_piece_minions_by_cr()
    {
        ContentDefs.EnsureInitialized();
        var counts = Enumerable.Range(0, Seeds)
            .Select(seed => GearLoot.Roll(Foe("4", boss: true), false, new Random(seed)).Count).ToList();
        Assert.All(counts, c => Assert.InRange(c, 1, 2));
        Assert.Contains(2, counts);
        // 低 CR 的小兵只是偶尔掉，CR 越高越常掉；小兵从不掉第二件。
        int Dropped(string cr) => Enumerable.Range(0, Seeds).Count(seed => GearLoot.Roll(Foe(cr), false, new Random(seed)).Count > 0);
        Assert.InRange(Dropped("1/4"), 1, Seeds / 4);
        Assert.True(Dropped("9") > Dropped("2") && Dropped("2") > Dropped("1/4"));
        Assert.All(Enumerable.Range(0, Seeds), seed => Assert.InRange(GearLoot.Roll(Foe("20"), false, new Random(seed)).Count, 0, 1));
    }

    [Fact]
    public void Threat_tier_is_only_formation_and_never_changes_drops()
    {
        ContentDefs.EnsureInitialized();
        for (var seed = 0; seed < 100; seed++)
        {
            var back = GearLoot.Roll(Foe("5", threatTier: 1), false, new Random(seed));
            var front = GearLoot.Roll(Foe("5", threatTier: 4), false, new Random(seed));
            Assert.Equal(back.Select(d => (d.Name, d.Quality, d.MaterialDefName)), front.Select(d => (d.Name, d.Quality, d.MaterialDefName)));
        }
    }

    [Fact]
    public void Quest_boss_always_drops_at_least_one()
    {
        ContentDefs.EnsureInitialized();
        foreach (var cr in new[] { "1/8", "2", "6" })
            for (var seed = 0; seed < Seeds; seed++)
                Assert.NotEmpty(GearLoot.Roll(Foe(cr), true, new Random(seed)));
    }

    [Fact]
    public void Materials_stay_in_the_cr_band_window_and_leaders_may_step_up_to_ether()
    {
        ContentDefs.EnsureInitialized();
        var rules = GearDropDef.Standard;
        foreach (var band in rules.Bands)
        {
            var cr = band.CrMin.ToString();
            // 小兵：只在本段窗口里。武器看武器池，防具饰品看防具池。
            foreach (var drop in Drops(Foe(cr), questBoss: true))
                Assert.Contains(drop.MaterialDefName, drop.Weapon != null ? band.Weapon : band.Armor);

            // 正主：窗口里的或沿材料表升一档的。
            var bossDrops = Drops(Foe(cr, boss: true)).ToList();
            foreach (var drop in bossDrops)
            {
                var weapon = drop.Weapon != null;
                var pool = weapon ? band.Weapon : band.Armor;
                var ladder = DefDatabase<MaterialDef>.All.Where(m => weapon ? m.WeaponUsable : m.ArmorUsable).OrderBy(m => m.Tier).ToList();
                var allowed = pool.Concat(pool.Select(p => ladder.FirstOrDefault(m => m.Tier > DefDatabase<MaterialDef>.Get(p)!.Tier)?.DefName ?? p)).ToHashSet();
                Assert.Contains(drop.MaterialDefName, allowed);
            }
        }
        // 高 CR 正主能掉到以太，低 CR 的掉不到。
        Assert.Contains(Drops(Foe("9", boss: true)), d => d.MaterialDefName == "以太");
        Assert.DoesNotContain(Drops(Foe("1", boss: true)), d => d.MaterialDefName == "以太");
    }

    [Fact]
    public void Kind_follows_the_enemy_weapon_and_leaders_roll_better_quality()
    {
        ContentDefs.EnsureInitialized();
        var drops = Drops(Foe("6", boss: true, weapon: WeaponType.Axe)).ToList();
        Assert.All(drops.Where(d => d.Weapon != null), d => Assert.Equal(WeaponType.Axe, d.Weapon!.Type));
        Assert.Contains(drops, d => d.Equip?.Kind == EquipKind.Armor);
        Assert.Contains(drops, d => d.Equip?.Kind == EquipKind.Accessory);
        Assert.DoesNotContain(drops, d => d.Quality is Quality.Crude or Quality.Unique);
        Assert.Contains(drops, d => d.Quality == Quality.Legendary);
        Assert.Contains(drops, d => (d.Weapon?.Enhance ?? d.Equip!.Enhance) > 3);
        // 饰品只进戒指或项链位。
        Assert.All(drops.Where(d => d.Equip?.Kind == EquipKind.Accessory),
            d => Assert.True(EquipSlots.Accepts(d.Equip!.Slot, EquipKind.Accessory)));
        // 同 CR 的小兵品质更低：会出粗劣。
        Assert.Contains(Drops(Foe("6"), questBoss: true), d => d.Quality == Quality.Crude);
    }

    [Fact]
    public void Gear_override_row_fixes_kind_and_floors_material_and_quality()
    {
        ContentDefs.EnsureInitialized();
        // 骸骨王的示例覆盖：必掉钢或更好材质的剑——它那段 CR 的窗口里有铁，也照样不低于钢。
        var king = DefDatabase<MonsterDef>.GetNamed("skeleton_king");
        var lich = EnemyDef.Place(king, 1, king.ThreatTier, true, king.Cr);
        Assert.NotNull(lich.Gear);
        var steel = DefDatabase<MaterialDef>.Get("钢")!.Tier;
        for (var seed = 0; seed < Seeds; seed++)
        {
            var first = GearLoot.Roll(lich, false, new Random(seed))[0];
            Assert.Equal(WeaponType.Sword, first.Weapon!.Type);
            Assert.True(first.Weapon.Material!.Tier >= steel, first.MaterialDefName);
        }

        var gear = new EnemyGear { Kind = EquipKind.Armor, Slot = EquipSlot.Head, MinMaterial = "秘银", MinQuality = Quality.Epic, RatePercent = 100 };
        for (var seed = 0; seed < Seeds; seed++)
        {
            var first = GearLoot.Roll(Foe("1/4", gear: gear), false, new Random(seed))[0];
            Assert.Equal(EquipSlot.Head, first.Equip!.Slot);
            Assert.Equal("秘银", first.MaterialDefName);
            Assert.True(first.Quality >= Quality.Epic);
        }
        // 掉率 0 的覆盖行：这只怪的第一件不掉（小兵不会再掷第二件）。
        var never = new EnemyGear { Kind = EquipKind.Weapon, RatePercent = 0 };
        Assert.Empty(Drops(Foe("1/4", gear: never)));
    }

    /// <summary>手搭一场：主角一刀砍翻一只怪（正主、覆盖行定死掉头盔之类由调用方给）。</summary>
    private static Battle WonBattle(Rimisekai.Save.GameState state, EnemyDef def)
    {
        var master = state.Roster.Master!;
        master[CoreStat.Strength] = 30;
        master.Condition.RecoverFull();
        var battle = new Battle(d100: () => 0, controlled: CombatSide.Attacker);
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
        var def = Foe("9", boss: true, gear: new EnemyGear { Kind = EquipKind.Armor, Slot = EquipSlot.Head, RatePercent = 100 });
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
        var battle = WonBattle(state, Foe("9", boss: true));
        // 没倒下的（如逃散的）不掉：一只活着的正主混进名单也掷不出东西。
        var standing = new Combatant { Id = 100, Side = CombatSide.Defender, Hp = 10, MaxHp = 10, Enemy = Foe("9", boss: true) };
        battle.Members.Add(standing);
        for (var seed = 0; seed < 50; seed++)
        {
            var loot = BattleLoot.Roll(battle, CombatSide.Defender, gearRng: new Random(seed));
            Assert.InRange(loot.Gear.Count, 1, 2);
        }

        // 没打赢：倒下的正主也不掉装备。
        var lost = new Battle(controlled: CombatSide.Attacker);
        lost.Add(new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 20, MaxHp = 20 });
        lost.Add(new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 0, MaxHp = 20, Enemy = Foe("9", boss: true) });
        lost.Add(new Combatant { Id = 3, Side = CombatSide.Defender, Hp = 20, MaxHp = 20, Enemy = Foe("9", boss: true) });
        Assert.Empty(BattleLoot.Roll(lost, CombatSide.Defender, gearRng: new Random(1)).Gear);
    }

    [Fact]
    public void Encounter_start_marks_only_the_flagged_leader_as_quest_boss()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 1);
        // 正主看标记，不看站位层：后排的正主照样是委托首领，前排的小兵不是。
        var foes = new List<EnemyDef>
        {
            new() { Id = "gear_test_boss", Name = "试刀首领", Boss = true, ThreatTier = 1, Column = 1 },
            Foe("1", threatTier: 4),
        };
        var session = Encounters.Start(state, foes, questBoss: true)!;
        var enemies = session.Battle.Members.Where(m => m.Side == CombatSide.Defender).ToList();
        Assert.Equal(new[] { true, false }, enemies.Select(m => m.QuestBoss));
        Assert.All(Encounters.Start(state, foes)!.Battle.Members, m => Assert.False(m.QuestBoss));
    }
}
