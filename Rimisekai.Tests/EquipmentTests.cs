using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Rimisekai.Catalog;
using Rimisekai.Character;
using Rimisekai.Combat;
using Rimisekai.Defs;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Quest;
using Rimisekai.Save;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>装备从哪来、怎么进战斗：材料等级表、下单锻造、甲折护甲、怪物掉落、委托实发物品、战斗里喝药、操练。</summary>
[Collection("Quest definition state")]
public sealed class EquipmentTests
{
    private const int Anvil = 1013, ArcheryTarget = 1049, SewingBench = 1016;
    private const int Courtyard = 1, Parlor = 2;

    [Fact]
    public void Material_tiers_raise_weapon_panel_and_armour_defence()
    {
        ContentDefs.EnsureInitialized();
        var materials = DefDatabase<MaterialDef>.All.OrderBy(m => m.Tier).ToList();
        var panels = materials.Where(m => m.WeaponUsable)
            .Select(m => WeaponForge.Forge(m.DefName, WeaponType.Sword, Quality.Common, "", false, 0).Panel).ToList();
        var defences = materials.Where(m => m.ArmorUsable)
            .Select(m => EquipForge.ForgeArmor(EquipSlot.Torso, m.DefName, Quality.Common, "", false, 0).Defence).ToList();
        Assert.True(panels.Zip(panels.Skip(1)).All(p => p.First < p.Second), string.Join(",", panels));
        Assert.True(defences.Zip(defences.Skip(1)).All(p => p.First < p.Second), string.Join(",", defences));
        // 品质照样乘：同材料精良比粗糙强
        Assert.True(WeaponForge.Forge("铁", WeaponType.Sword, Quality.Fine, "", false, 0).Panel
            > WeaponForge.Forge("铁", WeaponType.Sword, Quality.Crude, "", false, 0).Panel);
    }

    [Fact]
    public void Ordered_sword_is_forged_at_the_anvil_into_the_bag_and_raises_strike()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 1);
        var master = state.Roster.Master!;
        master.Bag.Add("石材", 30);
        master.Bag.Add("铁矿", 20);
        var anvil = TerritoryLoopTests.Build(hub, state, Anvil, Courtyard);
        master.Bag.Add("铁", 4);
        master.Bag.Add("木材", 1);

        Assert.True(hub.Craft("铁剑"), "下单：锻造台的目标定为铁剑");
        TerritoryLoopTests.MoveTo(hub, Courtyard);
        Assert.True(hub.Use(anvil.Id));
        Assert.True(hub.ActAtFixture(ActionKind.Forge));

        var sword = state.Weapons.All.Single(w => master.Bag.Get(w.Id) == 1);
        Assert.Equal("铁", sword.MaterialDefName);
        Assert.Equal(WeaponType.Sword, sword.Type);
        Assert.Equal(Territory.CraftQuality(master.Life(LifeSkill.Smithing)), sword.Quality);
        Assert.Equal(0, master.Bag.Get("铁"));
        Assert.Equal("", state.Territory.GetTargetCraftItem(ActionKind.Forge));

        Assert.True(hub.EquipFromBag(master.Id, EquipSlot.MainHand, sword.Id));
        // 进战斗的出手吃这把剑的面板：比同一身手空着面板（原配、无实例）要高
        var unit = Deploy.FromCharacter(master, CombatSide.Attacker, state.Weapons, state.Equips);
        Assert.Equal((int)System.Math.Round(master.ResolveStrike(sword.Panel).Rounded * BattleRules.PowerPercent / 100.0), unit.StrikePower);
        Assert.True(unit.StrikePower > Deploy.FromCharacter(master, CombatSide.Attacker).StrikePower);
    }

    [Fact]
    public void Starting_weapons_are_real_instances_and_accessories_and_off_hand_count_in_battle()
    {
        DefLoader.EnsureInitialized();
        var state = new GameState();
        var c = state.Roster.Add("战士");
        c.Identity = "战士";
        Assert.True(c.Equip(WeaponType.Sword, WeaponType.Sword));

        // 原配武器入伙即落成实例：主副手各一把普通品质的木剑，面板真实。
        var armory = state.Weapons.All.Count;
        state.Outfit(c);
        var main = state.Weapons.Get(c.EquippedId(EquipSlot.MainHand))!;
        var off = state.Weapons.Get(c.EquippedId(EquipSlot.OffHand))!;
        Assert.Equal("木材", main.MaterialDefName);
        Assert.Equal(Quality.Common, main.Quality);
        Assert.True(main.Panel > 0);

        // 再调不重复锻。
        state.Outfit(c);
        Assert.Equal(armory + 2, state.Weapons.All.Count);

        // 副手面板按 OffHandPanelPercent 折进出手。
        var unit = Deploy.FromCharacter(c, CombatSide.Attacker, state.Weapons, state.Equips);
        var expected = c.ResolveStrike(main.Panel + off.Panel * BattleRules.OffHandPanelPercent / 100).Rounded;
        Assert.Equal((int)System.Math.Round(expected * BattleRules.PowerPercent / 100.0), unit.StrikePower);

        // 饰品加成进战斗属性。
        var before = Deploy.FromCharacter(c, CombatSide.Attacker, state.Weapons, state.Equips);
        var ring = EquipForge.ForgeAccessory(EquipSlot.Ring1, "Strength", "铁", Quality.Common, "", false, 0);
        state.Equips.Add(ring);
        c.SetEquippedId(EquipSlot.Ring1, ring.Id);
        var after = Deploy.FromCharacter(c, CombatSide.Attacker, state.Weapons, state.Equips);
        Assert.True(ring.BonusAmount > 0);
        Assert.Equal(c.Stat(CoreStat.Strength, state.Equips), c[CoreStat.Strength] + ring.BonusAmount);
        Assert.True(after.Attack > before.Attack);
        Assert.True(after.StrikePower >= before.StrikePower);
    }

    [Fact]
    public void Gear_is_only_forged_on_order()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 1);
        var territory = state.Territory;
        var sword = territory.Recipes.Single(r => r.ItemId == "铁剑");
        var ingot = territory.Recipes.Single(r => r.ItemId == "铁");
        var smithy = new Facility { Craft = "锻" };
        Assert.False(territory.Makes(sword, ActionKind.Forge, smithy));
        Assert.True(territory.Makes(ingot, ActionKind.Forge, smithy));
        Assert.True(hub.Craft("铁剑"));
        Assert.True(territory.Makes(sword, ActionKind.Forge, smithy));
        Assert.False(territory.Makes(ingot, ActionKind.Forge, smithy));

        // 没下单时站上铁砧的人只冶铁锭，不会把铁锭打成剑
        Assert.True(hub.Craft("铁剑"));
        var master = state.Roster.Master!;
        master.Bag.Add("石材", 30);
        master.Bag.Add("铁矿", 24);
        var anvil = TerritoryLoopTests.Build(hub, state, Anvil, Courtyard);
        master.Bag.Add("铁", 4);
        master.Bag.Add("木材", 1);
        TerritoryLoopTests.MoveTo(hub, Courtyard);
        Assert.True(hub.Use(anvil.Id));
        Assert.True(hub.ActAtFixture(ActionKind.Forge));
        Assert.Empty(state.Weapons.All.Where(w => master.Bag.Get(w.Id) > 0));
        Assert.Equal(5, master.Bag.Get("铁"));
    }

    [Fact]
    public void Leather_armour_sewn_at_the_bench_becomes_combat_armour_and_cuts_damage()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 1);
        var master = state.Roster.Master!;
        master.Bag.Add("木材", 30);
        var bench = TerritoryLoopTests.Build(hub, state, SewingBench, Courtyard);
        master.Bag.Add("皮", 4);
        Assert.True(hub.Craft("皮甲"));
        TerritoryLoopTests.MoveTo(hub, Courtyard);
        Assert.True(hub.Use(bench.Id));
        Assert.True(hub.ActAtFixture(ActionKind.Sew));
        var armour = state.Equips.All.Single(e => master.Bag.Get(e.Id) == 1);
        Assert.Equal(EquipSlot.Torso, armour.Slot);
        Assert.Equal("皮", armour.MaterialDefName);
        Assert.True(hub.EquipFromBag(master.Id, EquipSlot.Torso, armour.Id));

        var bare = Deploy.FromCharacter(master, CombatSide.Attacker, state.Weapons, new EquipRegistry());
        var clad = Deploy.FromCharacter(master, CombatSide.Attacker, state.Weapons, state.Equips);
        Assert.Equal(0, bare.Armour);
        Assert.Equal(armour.Defence * BattleRules.GearArmourPercent / 100, clad.Armour);
        Assert.True(clad.Armour > 0);

        int Taken(Combatant target)
        {
            var battle = new Battle(d100: () => 0);
            battle.Add(new Combatant { Id = 50, Side = CombatSide.Defender, Hp = 100, MaxHp = 100, StrikePower = 60, Speed = 15, BaseHit = 100 });
            battle.Add(target);
            battle.StartBattle();
            _ = battle.PendingActor;
            return battle.Events.First(e => e.Kind == CombatEventKind.Hit && e.TargetId == target.Id).Amount;
        }
        Assert.Equal(Taken(bare) - clad.Armour, Taken(clad));
    }

    [Fact]
    public void Every_monster_in_the_content_has_a_drop_table_of_real_items()
    {
        ContentDefs.EnsureInitialized();
        var foes = new List<JsonObject>();
        void Walk(JsonNode? node)
        {
            if (node is JsonObject o)
            {
                if (o.ContainsKey("corePool"))
                    foes.Add(o);
                foreach (var pair in o)
                    Walk(pair.Value);
            }
            else if (node is JsonArray a)
                foreach (var item in a)
                    Walk(item);
        }
        foreach (var file in new[] { "content/map_defs.json", "content/defs/quests.json" })
            Walk(JsonNode.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(Root, file))));
        Assert.NotEmpty(foes);
        foreach (var foe in foes)
        {
            var loot = foe["loot"]?.AsArray();
            Assert.True(loot is { Count: > 0 }, $"{foe["name"]} 没有掉落表");
            foreach (var row in loot!)
                Assert.NotNull(Items.Get((string)row!["itemId"]!));
        }
    }

    [Fact]
    public void Winning_a_commission_puts_its_reward_items_and_the_loot_in_the_bag()
    {
        CombatBalanceTests.Party(out var state, 3);
        CombatBalanceTests.Kit(state, "钢", "铁");
        var master = state.Roster.Master!;
        var def = DefDatabase<QuestDef>.All.Single(q => q.Name == "北坡头狼");
        Assert.Equal(new[] { "金币 ×200", "兽皮 ×2" }, def.Rewards);
        var hides = master.Bag.Get("兽皮");
        var money = state.Money;
        var run = state.Quests.Start(def, state.Roster.Members.Select(c => c.Id).ToList())!;
        var battle = Enumerable.Range(0, 20).Select(seed => CombatBalanceTests.Brawl(state, def.Foes, def.Waves, seed))
            .First(b => b.Outcome == CombatOutcome.AttackerWin);

        var outcome = CombatSettlement.Settle(state, battle, run)!;

        Assert.True(outcome.Won);
        var looted = outcome.Loot.Items.Where(i => i.ItemId == "兽皮").Sum(i => i.Count);
        Assert.True(looted >= 4, "独眼头狼必掉 4–6 张兽皮");
        Assert.Equal(hides + looted + 2, master.Bag.Get("兽皮"));
        Assert.Equal(money + def.RewardMoney + outcome.Loot.Money, state.Money);
    }

    [Fact]
    public void Potion_heals_a_badly_hurt_ally_once_per_draught_and_is_taken_from_the_bag()
    {
        var state = new GameState();
        var master = state.Roster.Add("主人", master: true);
        master.Bag.Add("药剂", 1);
        var battle = new Battle(d100: () => 0);
        var hero = new Combatant { Id = master.Id, Side = CombatSide.Attacker, Hp = 10, MaxHp = 100, StrikePower = 5, Speed = 50, BaseHit = 100 };
        hero.Skills.Add("药剂");
        battle.Add(hero);
        var foe = new Combatant { Id = 9, Side = CombatSide.Defender, Hp = 1, MaxHp = 1, Speed = 1 };
        battle.Add(foe);
        Encounters.Pack(state, battle);
        Assert.Equal(1, battle.Supplies["药剂"]);
        Assert.Contains(battle.Menu(), s => s.Id == "药剂");

        Assert.True(battle.Act(new CombatAction { ActorId = hero.Id, SkillId = "药剂", TargetId = hero.Id }));
        Assert.Equal(10 + 40, hero.Hp);
        Assert.Equal(0, battle.Supplies["药剂"]);
        Assert.Equal(1, battle.Consumed["药剂"]);
        // 喝光了：菜单里没有，也出不了这一招
        Assert.DoesNotContain(battle.Menu(), s => s.Id == "药剂");
        Assert.False(battle.CanAct(new CombatAction { ActorId = hero.Id, SkillId = "药剂", TargetId = hero.Id }));

        Assert.True(battle.Act(new CombatAction { ActorId = hero.Id, TargetId = foe.Id }));
        Assert.Equal(CombatOutcome.AttackerWin, battle.Outcome);
        CombatSettlement.Settle(state, battle);
        Assert.Equal(0, master.Bag.Get("药剂"));
    }

    [Fact]
    public void Auto_battle_drinks_a_potion_when_someone_drops_below_half()
    {
        var battle = new Battle(d100: () => 99) { AutoBattle = true };
        var hero = new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 30, MaxHp = 100, StrikePower = 1, Speed = 50 };
        hero.Skills.Add("药剂");
        battle.Add(hero);
        battle.Add(new Combatant { Id = 9, Side = CombatSide.Defender, Hp = 500, MaxHp = 500, Speed = 1 });
        battle.Supplies["药剂"] = 2;
        battle.StartBattle();
        battle.StepTurn(out var acted);
        Assert.Equal(hero, acted);
        Assert.Equal(70, hero.Hp);
        Assert.Equal(1, battle.Supplies["药剂"]);
    }

    [Fact]
    public void Enemies_never_drink_potions()
    {
        ContentDefs.EnsureInitialized();
        var wolf = DefDatabase<QuestDef>.All.Single(q => q.Name == "北坡头狼").Foes[1];
        Assert.DoesNotContain("药剂", Deploy.FromEnemy(wolf, 1, CombatSide.Defender).Skills);
    }

    [Fact]
    public void Training_at_the_archery_target_builds_proficiency_up_to_the_cap()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 1);
        var master = state.Roster.Master!;
        master.Bag.Add("木材", 10);
        master.Bag.Add("布", 4);
        var target = TerritoryLoopTests.Build(hub, state, ArcheryTarget, Courtyard);
        var weapon = master.MainWeapon ?? WeaponType.Unarmed;
        var style = master.EquippedStyle ?? StyleType.Unarmed;
        master.Weapons[(int)weapon].Restore(0);
        var exp = master.Weapons[(int)weapon].Exp;
        var styleExp = master.Styles[(int)style].Exp;
        TerritoryLoopTests.MoveTo(hub, Courtyard);
        Assert.True(hub.Use(target.Id));
        Assert.True(hub.ActAtFixture(ActionKind.Train));
        Assert.True(master.Weapons[(int)weapon].Exp > exp);
        Assert.True(master.Styles[(int)style].Exp > styleExp);

        for (var i = 0; i < 200 && master.Weapons[(int)weapon].Level < Territory.TrainLevelCap; i++)
            Assert.True(Territory.Drill(master));
        Assert.Equal(Territory.TrainLevelCap, master.Weapons[(int)weapon].Level);
        Assert.False(Territory.Drill(master), "到顶了，操练练不出长进");
    }

    [Fact]
    public void Npcs_train_at_the_target_in_their_free_time()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 1);
        var master = state.Roster.Master!;
        master.Bag.Add("木材", 10);
        master.Bag.Add("布", 4);
        var target = TerritoryLoopTests.Build(hub, state, ArcheryTarget, Courtyard);
        var maid = TerritoryLoopTests.Maid(state);
        maid.Bag.Add("干粮", 10);
        var weapon = maid.MainWeapon ?? WeaponType.Unarmed;
        maid.Weapons[(int)weapon].Restore(0);
        var exp = maid.Weapons[(int)weapon].Exp;
        var seen = new HashSet<string>();
        for (var i = 0; i < 24 * 6; i++)
        {
            hub.PassTime(10);
            var w = hub.Day.Workers.Single(x => x.CharacterId == maid.Id);
            seen.Add($"{w.Goal}@{w.FacilityId}/{w.PlayTicks}");
        }
        Assert.True(maid.Weapons[(int)weapon].Exp > exp, "女仆闲时去箭靶练了 " + string.Join(" ", seen));
    }

    private static string Root =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", ".."));
}
