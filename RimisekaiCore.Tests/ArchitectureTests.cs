using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Catalog;
using Rimisekai.Character;
using Rimisekai.Clock;
using Rimisekai.Defs;
using Rimisekai.Hub;
using Rimisekai.Combat;
using Rimisekai.Command;
using Rimisekai.Housing;
using Rimisekai.Housing.StateMachine;
using Rimisekai.Housing.StateMachine.States;
using Rimisekai.Quest;
using Rimisekai.Save;
using Rimisekai.Session;
using Session = Rimisekai.Session.Session;
using Xunit;

namespace Rimisekai.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Day_closes_hub_then_opens_next()
    {
        var state = new GameState();
        var master = state.Roster.Add("m", master: true);
        master.FactionId = GameState.PlayerFaction;
        master.MaxBase[0] = 100;
        master.Base[0] = 10;

        state.Clock.SetTime(1, 0);
        state.Clock.Advance(GameClock.MinutesPerDay - 1);
        Assert.Equal(3, state.Clock.Slot);
        state.Clock.Advance(1);
        Assert.Equal(2, state.Clock.Day);
        Assert.Equal(0, state.Clock.Minutes);

        state.EndDay();
        Assert.Equal(100, master.Base[0]);
        Assert.Equal(1, master.EmploymentDays);
        Assert.Equal(3, state.Clock.Day);
    }

    [Fact]
    public void Quest_move_requires_a_link()
    {
        var record = new QuestRecord();
        record.Register(new QuestDef { Id = 1, Name = "q", Kind = QuestKind.Map, CooldownDays = 1 });
        var run = record.Start(1, new[] { 1 });
        Assert.NotNull(run);

        var map = new QuestMap
        {
            QuestId = 1,
            Nodes =
            {
                new MapNode { Id = 0, Links = { 1 } },
                new MapNode { Id = 1, Links = { 0 } },
                new MapNode { Id = 2 },
            },
        };

        Assert.True(run!.MoveTo(map, 0));
        Assert.True(run.MoveTo(map, 1));
        Assert.False(run.MoveTo(map, 2));

        record.Complete(run);
        Assert.False(record.IsAvailable(1));
        record.TickDay();
        Assert.True(record.IsAvailable(1));
    }

    [Fact]
    public void Battle_ends_when_a_side_is_gone()
    {
        var battle = new Battle(d100: () => 0);
        battle.Add(new Combatant
        {
            Id = 1, Side = CombatSide.Attacker, Hp = 10, MaxHp = 10,
            Dodge = 9, StrikePower = 3,
        });
        battle.Add(new Combatant
        {
            Id = 2, Side = CombatSide.Defender, Hp = 3, MaxHp = 3, Dodge = 5,
        });

        Assert.True(battle.Act(new CombatAction { ActorId = 1, TargetId = 2 }));
        Assert.Equal(CombatOutcome.AttackerWin, battle.Outcome);
    }

    [Fact]
    public void Session_rejects_unknown_and_runs_registered()
    {
        var roster = new Roster();
        roster.Add("m", master: true);
        var session = new ProbeSession(roster);
        session.Register(new CommandDef
        {
            Id = 7,
            Context = CommandContext.Daily,
            EffectId = "noop",
            CostMinutes = 5,
        });
        session.Register(new NoopEffect());

        Assert.Equal(CommandResult.Rejected, session.Submit(new CommandRequest { CommandId = 1 }));
        Assert.Equal(CommandResult.Executed, session.Submit(new CommandRequest { CommandId = 7 }));
        Assert.Equal(5, session.Spent);
    }

    [Fact]
    public void Territory_blocks_rooms_in_locked_regions()
    {
        var t = new Territory();
        Assert.False(t.AddRoom(new Room { Id = 1, RegionId = 1 }));
        Assert.True(t.UnlockRegion());
        Assert.True(t.AddRoom(new Room { Id = 1, RegionId = 1 }));
    }

    [Fact]
    public void Delegation_gathers_by_slot_and_respects_capacity()
    {
        var roster = new Roster();
        roster.Add("master", master: true);
        var a = roster.Add("a");
        var b = roster.Add("b");
        a[CoreStat.Perception] = 80;

        var t = new Territory();
        t.AddRoom(new Room { Id = 0 });
        t.AddFacility(new Facility { Id = 1, RoomId = 0, Usage = FacilityUsage.Plain, Actions = { ActionKind.Till }, YieldItemId = "herb", Capacity = 1 });
        t.Assign(a.Id, 0, SlotMode.Work, 1);
        t.Assign(b.Id, 0, SlotMode.Work, 1);

        var logs = t.ResolveSlot(0, roster);
        Assert.Single(logs);
        Assert.Equal(a.Id, logs[0].CharacterId);
        // 产出直接进采集者背包；入库由搬运逻辑另行完成。
        Assert.Equal(2, a.Bag.Get("herb"));
        Assert.Equal(3, a.LifeExp[(int)LifeSkill.Farming]);
    }

    [Fact]
    public void Workbench_pays_costs_then_outputs()
    {
        var roster = new Roster();
        var a = roster.Add("a");
        var t = new Territory();
        t.AddRoom(new Room { Id = 0 });
        t.AddFacility(new Facility { Id = 1, RoomId = 0, Usage = FacilityUsage.Plain, Actions = { ActionKind.Forge }, Capacity = 1 });
        t.AddRecipe(new Recipe
        {
            ItemId = "ingot",
            Station = ActionKind.Forge,
            OutputCount = 1,
            Costs = { new RecipeCost("ore", 2) },
        });
        t.Assign(a.Id, 2, SlotMode.Work, 1);

        Assert.Empty(t.ResolveSlot(2, roster));
        a.Bag.Add("ore", 2);
        var logs = t.ResolveSlot(2, roster);
        Assert.Equal("ingot", logs[0].ItemId);
        Assert.Equal(0, a.Bag.Get("ore"));
        Assert.Equal(1, a.Bag.Get("ingot"));
    }

    [Fact]
    public void Clock_slot_is_six_hours()
    {
        var clock = new GameClock();
        clock.SetTime(1, 0);
        clock.Advance(6 * 60);
        Assert.Equal(1, clock.Slot);
        clock.Advance(6 * 60);
        Assert.Equal(2, clock.Slot);
        Assert.Equal(4, GameClock.SlotsPerDay);
    }

    [Fact]
    public void Combat_sheet_folds_core_stats()
    {
        var c = new CharacterState(1);
        c[CoreStat.Constitution] = 10;
        c[CoreStat.Dexterity] = 8;
        c[CoreStat.Intellect] = 6;
        c[CoreStat.Perception] = 4;
        c[CoreStat.Strength] = 12;

        var sheet = c.Combat;
        Assert.Equal(16, sheet.Attack);
        Assert.Equal(125, sheet.MaxHp);
        Assert.Equal(16, sheet.Defence);
        Assert.Equal(10, sheet.Dodge);
        Assert.Equal(8, sheet.SpellPower);
        Assert.Equal(CoreStat.Strength, AttributeMap.CoreOf(LifeSkill.Mining));
        Assert.Equal(CoreStat.Charm, AttributeMap.CoreOf(LifeSkill.Husbandry));

        c.GainLifeExp(LifeSkill.Mining, 100);
        Assert.Equal(5, c.Level);
        Assert.Equal(17, c.Life(LifeSkill.Mining));
    }

    [Fact]
    public void Exp_shares_into_core_level_weapon_and_style()
    {
        var c = new CharacterState(1);
        c.GainLifeExp(LifeSkill.Mining, 100);
        Assert.Equal(50, c.CoreExp[(int)CoreStat.Strength]);
        Assert.Equal(200, c.LevelExp);

        c.GainWeaponExp(WeaponType.Sword, 100);
        Assert.Equal(200, c.Weapons[(int)WeaponType.Sword].Exp);
        Assert.Equal(400, c.LevelExp);
        Assert.Equal(102, c.WeaponHit(WeaponType.Sword, 100));
        Assert.Equal(204, c.WeaponDamage(WeaponType.Sword, 200));

        c.GainStyleExp(StyleType.TwoHand, 100);
        Assert.Equal(500, c.Styles[(int)StyleType.TwoHand].Exp);
        Assert.Equal(50, c.CoreExp[(int)CoreStat.Strength]);
        Assert.Equal(10, c[CoreStat.Strength]);
        Assert.Equal(900, c.LevelExp);
        Assert.Equal(10, c.Level);

        Assert.True(c.Equip(WeaponType.Sword, WeaponType.Sword));
        Assert.Equal(StyleType.TwoHand, c.EquippedStyle);
        Assert.Equal(102, c.EquippedHit(100));
        Assert.Equal(204, c.EquippedDamage(200));
        Assert.True(c.Equip(null, null));
        Assert.Null(c.EquippedStyle);
        Assert.Equal(100, c.EquippedHit(100));
    }

    [Fact]
    public void Style_derives_from_loadout()
    {
        Assert.Equal(StyleType.OneHand, CharacterState.DeriveStyle(WeaponType.Sword, null, false));
        Assert.Equal(StyleType.TwoHand, CharacterState.DeriveStyle(WeaponType.Sword, WeaponType.Sword, false));
        Assert.Equal(StyleType.DualWield, CharacterState.DeriveStyle(WeaponType.Sword, WeaponType.Dagger, false));
        Assert.Equal(StyleType.Shield, CharacterState.DeriveStyle(WeaponType.Sword, null, true));
        Assert.Equal(StyleType.Ranged, CharacterState.DeriveStyle(WeaponType.Bow, null, false));
        Assert.Equal(StyleType.Ranged, CharacterState.DeriveStyle(WeaponType.Crossbow, null, false));
        Assert.Equal(StyleType.Spell, CharacterState.DeriveStyle(WeaponType.Staff, null, false));
        Assert.Equal(StyleType.Unarmed, CharacterState.DeriveStyle(WeaponType.Unarmed, null, false));
        Assert.Null(CharacterState.DeriveStyle(null, null, false));

        var c = new CharacterState(1);
        Assert.False(c.Equip(null, WeaponType.Dagger));
        Assert.False(c.Equip(null, null, true));
        Assert.True(c.Equip(WeaponType.Dagger));
        Assert.Equal(StyleType.OneHand, c.EquippedStyle);
    }

    [Fact]
    public void Hub_moves_uses_trades_and_develops()
    {
        var state = new GameState { Money = 100 };
        state.Roster.Add("你", master: true);
        var lily = state.Roster.Add("莉莉");
        var yard = new Room { Id = 1, Name = "庭院", Open = true, X = 2, Y = 2 };
        var well = new Room { Id = 2, Name = "水井", Open = true, X = 3, Y = 2 };
        var locked = new Room { Id = 3, Name = "库房", X = 2, Y = 4, OpenCost = 40 };
        state.Territory.AddRoom(yard);
        state.Territory.AddRoom(well);
        state.Territory.AddRoom(locked);
        state.Territory.Link(1, 2);
        state.Territory.AddFacility(new Facility { Id = 9, Name = "躺椅", RoomId = 1, Capacity = 1, Actions = { ActionKind.Rest } });
        state.Territory.AddRecipe(new Recipe
        {
            ItemId = "布",
            Station = ActionKind.Sew,
            Costs = { new RecipeCost("纤维", 1) },
        });
        var hub = new HubSession(state);
        // 物资进玩家背包（物品只在背包或设施里，没有领地虚空库存）。
        hub.State.Roster.Master!.Bag.Add("纤维", 1);
        hub.State.Roster.Master!.Bag.Add("花", 1);
        hub.Enter(1);
        hub.Place(lily.Id, 1);
        Assert.True(hub.Move(2));
        Assert.False(hub.Move(3));
        Assert.True(hub.Use(9) == false);
        hub.Move(1);
        Assert.True(hub.Use(9));
        Assert.Contains(hub.Here(), f => f.PlayerHere);

        // 上面几次移动会推进时间，NPC 会自行走动；要验证“同房才能交流”，
        // 就得在选人之前把她放回玩家所在的房间。
        hub.Place(lily.Id, 1);
        Assert.True(hub.Select(lily.Id));
        Assert.True(hub.Social(SocialAction.Gift, "花"));
        Assert.Equal(0, hub.State.Roster.Master!.Bag.Get("花"));
        Assert.True(hub.Craft("布"));
        Assert.Equal("布", hub.State.Territory.GetTargetCraftItem(ActionKind.Sew));
        hub.State.Roster.Master!.Bag.Add("布", 1);
        Assert.True(hub.Trade("布", 1, 15, selling: true));
        Assert.Equal(115, state.Money);
        Assert.True(hub.Develop(3));
        Assert.Equal(75, state.Money);
        Assert.True(locked.Open);
    }

    [Fact]
    public void Overlay_covers_map_until_lines_end()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var lily = state.Roster.Add("莉莉");
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "水井", Open = true });
        state.Territory.Link(1, 2);
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(lily.Id, 1);
        hub.Select(lily.Id);
        hub.Show(MapOverlay.Dialogue("莉莉", new[] { "早。", "院子有露水。" }));

        Assert.True(hub.MapCovered);
        Assert.False(hub.Move(2));
        Assert.Equal("早。", hub.Overlay!.Text);
        Assert.True(hub.AdvanceOverlay());
        Assert.Equal("院子有露水。", hub.Overlay.Text);
        Assert.False(hub.AdvanceOverlay());
        Assert.False(hub.MapCovered);
        Assert.True(hub.Move(2));

        hub.Show(MapOverlay.Illustration("dawn"));
        Assert.Equal(OverlayKind.Illustration, hub.Overlay!.Kind);
        hub.CloseOverlay();
        Assert.False(hub.MapCovered);
    }

    [Fact]
    public void Worker_walks_then_builds_progress()
    {
        var roster = new Roster();
        var a = roster.Add("a");
        a[CoreStat.Strength] = 80;
        var t = new Territory();
        t.AddRoom(new Room { Id = 1, Open = true });
        t.AddRoom(new Room { Id = 2, Open = true });
        t.Link(1, 2);
        var money = 30L;
        t.AddFacility(new Facility
        {
            Id = 5, RoomId = 2, Usage = FacilityUsage.Plain, Actions = { ActionKind.Mine }, YieldItemId = "ore", Built = false, BuildCost = 30, EffectId = "shaft",
        });
        Assert.True(t.Build(5, ref money));
        Assert.Equal(0, money);
        Assert.Equal(1, t.Effect("shaft"));
        t.Assign(a.Id, 0, SlotMode.Work, 5);

        var day = new TerritoryClock();
        day.Track(a.Id, 1);
        var logs = day.Step(t, roster, 0);
        Assert.Empty(logs);
        Assert.Equal(WorkPhase.Working, day.Workers[0].Phase);
        Assert.Equal(2, day.Workers[0].RoomId);
        for (var i = 0; i < 10; i++)
            logs = day.Step(t, roster, 0);
        Assert.True(a.Bag.Get("ore") > 0);
    }

    [Fact]
    public void Traits_change_learning_work_and_talk()
    {
        var fast = new CharacterState(1);
        var slow = new CharacterState(2);
        fast.Grant(Trait.FastLearner);
        slow.Grant(Trait.SlowLearner);
        fast.GainLifeExp(LifeSkill.Craft, 100);
        slow.GainLifeExp(LifeSkill.Craft, 100);
        Assert.Equal(150, fast.LifeExp[(int)LifeSkill.Craft]);
        Assert.Equal(50, slow.LifeExp[(int)LifeSkill.Craft]);

        var roster = new Roster();
        var lazy = roster.Add("懒");
        lazy.Grant(Trait.Lazy);
        var t = new Territory();
        t.AddRoom(new Room { Id = 1, Open = true });
        t.AddFacility(new Facility { Id = 1, RoomId = 1, Usage = FacilityUsage.Plain, Actions = { ActionKind.Mine }, Built = true, YieldItemId = "ore" });
        t.Assign(lazy.Id, 0, SlotMode.Work, 1);
        var day = new TerritoryClock();
        day.Track(lazy.Id, 1);
        day.Step(t, roster, 0);
        Assert.Equal(WorkPhase.Idle, day.Workers[0].Phase);

        var state = new GameState();
        state.Roster.Add("你", master: true);
        var cold = state.Roster.Add("冷");
        cold.Grant(Trait.Cold);
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(cold.Id, 1);
        hub.Select(cold.Id);
        Assert.True(hub.Social(SocialAction.Talk));
        Assert.False(hub.MapCovered);
        Assert.True(hub.Social(SocialAction.Talk));
        Assert.True(hub.MapCovered);
    }

    [Fact]
    public void Vitals_tire_recover_and_bond()
    {
        var c = new CharacterState(1);
        var cap = c.Condition.MaxStamina;
        Assert.Equal(c.Combat.MaxHp, cap);
        Assert.Equal(cap, c.Condition.Stamina);
        Assert.Equal(Bond.None, c.Condition.Bond);

        c.Condition.Spend(cap, 100);
        Assert.Equal(0, c.Condition.Stamina);
        Assert.Equal(Vitals.DefaultMax - 100, c.Condition.Spirit);
        Assert.False(c.Condition.Tired);

        // 气力低于 30% 即进入疲劳；这是唯一的判定。
        c.Condition.Spend(0, Vitals.DefaultMax);
        Assert.True(c.Condition.Tired);

        c.Condition.Recover(cap, Vitals.DefaultMax);
        Assert.Equal(cap, c.Condition.Stamina);
        Assert.False(c.Condition.Tired);

        c.Condition.AddFavor(120);
        Assert.Equal(Bond.Fond, c.Condition.Bond);
        c.Condition.AddFavor(200);
        Assert.Equal(Bond.Close, c.Condition.Bond);
        c.Condition.AddFavor(300);
        Assert.Equal(Bond.Lover, c.Condition.Bond);
    }

    [Fact]
    public void Tired_worker_stops_and_talk_raises_favor()
    {
        var roster = new Roster();
        var worker = roster.Add("工");
        worker.Condition.Spend(0, Vitals.DefaultMax);
        var t = new Territory();
        t.AddRoom(new Room { Id = 1, Open = true });
        t.AddFacility(new Facility { Id = 1, RoomId = 1, Usage = FacilityUsage.Plain, Actions = { ActionKind.Till }, Built = true, YieldItemId = "herb" });
        t.Assign(worker.Id, 0, SlotMode.Work, 1);
        var day = new TerritoryClock();
        day.Track(worker.Id, 1);
        day.Step(t, roster, 0);
        Assert.Equal(WorkPhase.Idle, day.Workers[0].Phase);

        var state = new GameState();
        state.Roster.Add("你", master: true);
        var friend = state.Roster.Add("友");
        state.Roster.Master!.Bag.Add("花", 1);
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(friend.Id, 1);
        hub.Select(friend.Id);
        Assert.True(hub.Social(SocialAction.Talk));
        Assert.True(friend.Condition.Favor > 0);
        Assert.True(hub.Social(SocialAction.Gift, "花"));
        Assert.True(friend.Condition.Favor >= 25);
    }

    [Fact]
    public void PassTime_drives_workers_with_climate()
    {
        var state = new GameState { Money = 0 };
        state.Roster.Add("你", master: true);
        var worker = state.Roster.Add("工");
        worker[CoreStat.Strength] = 80;
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        state.Territory.AddFacility(new Facility
        {
            Id = 1, Name = "矿", RoomId = 1, Usage = FacilityUsage.Plain, Actions = { ActionKind.Mine },
            YieldItemId = "ore", Built = true,
        });
        for (var slot = 0; slot < WorkSlot.Count; slot++)
            state.Territory.Assign(worker.Id, slot, SlotMode.Work, 1);
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(worker.Id, 1);

        state.Clock.SetTime(1, 0);
        state.Clock.Advance(6 * 60);
        var logs = hub.PassTime(6 * 60);
        // 矿脉不是仓储设施，同房也没有货架：产出退回采集者背包。
        Assert.True(worker.Bag.Get("ore") > 0);
        Assert.True(logs.Count > 0);
        Assert.Equal(12 * 60, state.Clock.Minutes);
    }

    [Fact]
    public void Market_and_guests_serve_main_screen()
    {
        var state = new GameState { Money = 100 };
        state.Clock.SetTime(1, 0);
        state.Roster.Add("你", master: true);
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        state.Territory.MarketDay["木材"] = new Territory.MarketEntry(5, 100);
        state.Territory.AddGuest(new Guest { Id = 1, Name = "行商", RoomId = 1, Purpose = "卖货" });
        var hub = new HubSession(state);
        hub.Enter(1);

        // 不在交易页买卖不了，且不耗时。
        Assert.False(hub.MarketTrade("木材", 1, selling: false));
        Assert.Equal(0, state.Clock.Minutes);
        // 点交易开页免费；首笔成交结算行程 6 小时，之后的成交免费。
        hub.OpenTrade();
        Assert.True(hub.MarketTrade("木材", 2, selling: false));
        Assert.Equal(6 * 60, state.Clock.Minutes);
        Assert.Equal(2, state.Roster.Master!.Bag.Get("木材"));
        Assert.True(hub.MarketTrade("木材", 1, selling: true));
        Assert.Single(hub.GuestsHere());
    }

    [Fact]
    public void BuildFacility_costs_money_and_station_time()
    {
        var state = new GameState { Money = 100 };
        state.Clock.SetTime(1, 0);
        state.Roster.Add("你", master: true);
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        state.Territory.AddFacility(new Facility
        {
            Id = 5, Name = "铁砧", RoomId = 1, Usage = FacilityUsage.Plain, Actions = { ActionKind.Forge },
            Built = false, BuildCost = 30,
        });
        var hub = new HubSession(state);
        hub.Enter(1);
        Assert.True(hub.BuildFacility(5));
        Assert.Equal(70, state.Money);
        Assert.Equal(24 * TerritoryClock.StepMinutes, state.Clock.Minutes);
        Assert.True(state.Territory.Facilities[0].Built);
        Assert.False(hub.BuildFacility(5));
    }

    [Fact]
    public void Save_roundtrip_keeps_world_and_hub()
    {
        var state = new GameState { Money = 500, Prestige = 7 };
        state.Clock.SetTime(1, 7 * 60);
        state.Weather = Weather.Rain;
        var master = state.Roster.Add("你", master: true);
        master.FactionId = GameState.PlayerFaction;
        master[CoreStat.Strength] = 12;
        master.Grant(Trait.FastLearner);
        master.Equip(WeaponType.Sword);
        var friend = state.Roster.Add("友");
        friend.Condition.AddFavor(150);
        state.Territory.Name = "领地";
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true, X = 2, Y = 2 });
        state.Territory.AddRoom(new Room { Id = 2, Name = "水井", Open = true, X = 3, Y = 2 });
        state.Territory.Link(1, 2);
        state.Territory.AddFacility(new Facility
        {
            Id = 9, Name = "躺椅", RoomId = 1, Capacity = 2,
            Usage = FacilityUsage.Rest, Built = true, EffectId = "rest",
            Actions = { ActionKind.Rest },
        });
        state.Territory.AddFacility(new Facility
        {
            Id = 8, Name = "矿", RoomId = 2, Usage = FacilityUsage.Plain, Built = true,
            Actions = { ActionKind.Till }, YieldItemId = "herb",
        });
        state.Roster.Master!.Bag.Add("花", 3);
        state.Territory.Assign(friend.Id, 2, SlotMode.Work, 8);
        state.Territory.MarketDay["布"] = new Territory.MarketEntry(5, 100);
        state.Territory.AddGuest(new Guest { Id = 1, Name = "行商", RoomId = 1, Purpose = "卖货" });
        state.Quests.ClearCount[3] = 2;
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(friend.Id, 1);
        hub.Select(friend.Id);
        hub.Write("测试日志");

        var json = SaveSystem.Save(state, hub);
        var loaded = SaveSystem.Load(json);

        Assert.Equal(1, loaded.Clock.Day);
        Assert.Equal(7 * 60, loaded.Clock.Minutes);
        Assert.Equal(500, loaded.Money);
        Assert.Equal(Weather.Rain, loaded.Weather);
        Assert.Equal("领地", loaded.Territory.Name);
        Assert.Equal(2, loaded.Territory.Rooms.Count);
        Assert.Contains(2, loaded.Territory.Rooms[0].Links);
        Assert.Equal(1, loaded.Territory.Effect("rest"));
        Assert.Equal(3, loaded.Roster.Master!.Bag.Get("花"));
        Assert.Equal(SlotMode.Work, loaded.Territory.ScheduleOf(friend.Id).Slots[2].Mode);
        Assert.Equal(8, loaded.Territory.ScheduleOf(friend.Id).Slots[2].FacilityId);
        Assert.Equal(5, loaded.Territory.MarketDay["布"].Stock);
        Assert.Single(loaded.Territory.Guests);
        Assert.Equal(2, loaded.Quests.ClearCount[3]);

        var loadedMaster = loaded.Roster.Master;
        Assert.NotNull(loadedMaster);
        Assert.Equal(12, loadedMaster[CoreStat.Strength]);
        Assert.True(loadedMaster.Has(Trait.FastLearner));
        Assert.Equal(WeaponType.Sword, loadedMaster.MainWeapon);
        Assert.Equal(StyleType.OneHand, loadedMaster.EquippedStyle);
        var loadedFriend = loaded.Roster.Find(friend.Id);
        Assert.NotNull(loadedFriend);
        Assert.Equal(Bond.Fond, loadedFriend.Condition.Bond);

        var loadedHub = new HubSession(loaded);
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<SaveData>(json);
        Assert.NotNull(snapshot?.Hub);
        loadedHub.Restore(snapshot.Hub);
        Assert.Equal(1, loadedHub.PlayerRoomId);
        Assert.Equal(friend.Id, loadedHub.SelectedCharacterId);
        Assert.Contains(loadedHub.History, l => l.Text == "测试日志");
    }

    [Fact]
    public void EndDay_recovers_rolls_weather_and_reports_season()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var worker = state.Roster.Add("工");
        worker.Condition.Spend(300, 200);
        var summary = state.EndDay(new System.Random(1));
        Assert.Equal(2, state.Clock.Day);
        Assert.Equal(worker.Combat.MaxHp, worker.Condition.Stamina);
        Assert.Equal(Season.Spring, summary.Season);
        Assert.False(summary.SeasonChanged);
    }

    [Fact]
    public void Strike_splits_addend_and_multiplier()
    {
        var c = new CharacterState(1);
        c[CoreStat.Strength] = 12;
        c[CoreStat.Dexterity] = 8;
        c[CoreStat.Constitution] = 10;
        c[CoreStat.Perception] = 4;
        c[CoreStat.Intellect] = 6;
        c.GainWeaponExp(WeaponType.Sword, 50);
        Assert.True(c.Equip(WeaponType.Sword));

        var one = c.ResolveStrike(20);
        Assert.Equal(WeaponType.Sword, one.Weapon);
        Assert.Equal(StyleType.OneHand, one.Style);
        Assert.Equal(1, one.WeaponLevel);
        Assert.Equal(62, one.Addend);
        Assert.Equal(2.75, one.Multiplier, 6);
        Assert.Equal(170, one.Rounded);

        Assert.True(c.Equip(WeaponType.Sword, WeaponType.Sword));
        var two = c.ResolveStrike(20);
        Assert.Equal(StyleType.TwoHand, two.Style);
        Assert.Equal(68, two.Addend);
        Assert.Equal(3.45, two.Multiplier, 6);
        Assert.Equal(235, two.Rounded);

        Assert.True(c.Equip(WeaponType.Unarmed));
        var fist = c.ResolveStrike(0);
        Assert.Equal(StyleType.Unarmed, fist.Style);
        Assert.Equal(43, fist.Addend);
        Assert.Equal(4.2, fist.Multiplier, 6);
        Assert.Equal(181, fist.Rounded);

        Assert.True(c.Equip(WeaponType.Sword, null, true));
        var shield = c.ResolveStrike(10);
        Assert.Equal(StyleType.Shield, shield.Style);
        Assert.Equal(56, shield.Addend);
        Assert.Equal(3.75, shield.Multiplier, 6);
        Assert.Equal(210, shield.Rounded);

        Assert.True(c.Equip(null, null));
        var none = c.ResolveStrike(0);
        Assert.Equal(StyleType.Unarmed, none.Style);
        Assert.Equal(43, none.Addend);
        Assert.Equal(181, none.Rounded);
    }

    [Fact]
    public void New_life_skills_map_to_core()
    {
        Assert.Equal(8, AttributeMap.LifeCount);
        Assert.Equal(CoreStat.Intellect, AttributeMap.CoreOf(LifeSkill.Research));
        Assert.Equal(CoreStat.Charm, AttributeMap.CoreOf(LifeSkill.Social));
    }

    [Fact]
    public void Talk_acquaints_and_marks_survive_save()
    {
        var state = new GameState();
        var master = state.Roster.Add("你", master: true);
        var friend = state.Roster.Add("友");
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(friend.Id, 1);
        hub.Select(friend.Id);
        Assert.True(hub.Social(SocialAction.Talk));
        Assert.True(master.Relations.Has(friend.Id, RelationFlag.Acquainted));
        Assert.True(friend.Relations.Has(master.Id, RelationFlag.Acquainted));

        friend.Relations.Add(master.Id, RelationFlag.Marked);
        var loaded = SaveSystem.Load(SaveSystem.Save(state, hub));
        var loadedFriend = loaded.Roster.Find(friend.Id);
        Assert.NotNull(loadedFriend);
        Assert.True(loadedFriend.Relations.Has(master.Id, RelationFlag.Acquainted));
        Assert.True(loadedFriend.Relations.Has(master.Id, RelationFlag.Marked));
    }

    [Fact]
    public void Weapon_def_strikes_with_its_own_proficiency()
    {
        var catalog = new GameCatalog();
        catalog.Weapons["axe"] = new WeaponDef { Id = "axe", Name = "斧", Type = WeaponType.Axe, Panel = 30 };
        var c = new CharacterState(1);
        c[CoreStat.Strength] = 12;
        c[CoreStat.Dexterity] = 8;
        c[CoreStat.Constitution] = 10;
        c.GainWeaponExp(WeaponType.Axe, 50);
        Assert.True(c.Equip(WeaponType.Sword));

        var strike = c.ResolveStrike(catalog.Weapons["axe"]);
        Assert.Equal(WeaponType.Axe, strike.Weapon);
        Assert.Equal(StyleType.OneHand, strike.Style);
        Assert.Equal(30 + 5 + 22 + 15, strike.Addend);
        Assert.Equal(198, strike.Rounded);
    }

    [Fact]
    public void CloseDay_writes_weather_and_runs_events()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        state.DayEvents.Add(new FixedDayEvent("夜里有动静。"));
        var hub = new HubSession(state);
        hub.Enter(1);
        var summary = hub.CloseDay(new System.Random(1));
        Assert.Equal(2, state.Clock.Day);
        Assert.Equal(summary.Weather, state.Weather);
        Assert.Contains(hub.Log, l => l.Text == "夜里有动静。");
        Assert.Contains(hub.Log, l => l.Text.StartsWith("今日天气"));
    }

    [Fact]
    public void Assign_sets_a_slot_and_rejects_master_and_bad_slot()
    {
        var state = new GameState();
        var master = state.Roster.Add("你", master: true);
        var worker = state.Roster.Add("工");
        var hub = new HubSession(state);

        Assert.True(hub.Assign(worker.Id, 1, SlotMode.Work));
        Assert.Equal(SlotMode.Work, state.Territory.ScheduleOf(worker.Id).Slots[1].Mode);
        // 别的时段不受影响，改的只是点中的那一段。
        Assert.Equal(SlotMode.Free, state.Territory.ScheduleOf(worker.Id).Slots[0].Mode);

        // 主角也可排班；越界时段与不存在的人拒绝。
        Assert.True(hub.Assign(master.Id, 0, SlotMode.Work));
        Assert.False(hub.Assign(worker.Id, WorkSlot.Count, SlotMode.Work));
        Assert.False(hub.Assign(worker.Id, -1, SlotMode.Work));
        Assert.False(hub.Assign(9999, 0, SlotMode.Work));
    }

    [Fact]
    public void Assign_carries_facility_and_rejects_unknown_facility()
    {
        var state = new GameState();
        var master = state.Roster.Add("你", master: true);
        var worker = state.Roster.Add("工");
        var hub = new HubSession(state);
        state.Territory.AddRoom(new Room { Id = 1, Open = true });
        state.Territory.AddFacility(new Facility
        {
            Id = 3, RoomId = 1, Usage = FacilityUsage.Plain, Built = true,
            Actions = { ActionKind.Mine }, YieldItemId = "ore",
        });

        // 工作 + 点名设施：存下开关与设施。
        Assert.True(hub.Assign(worker.Id, 1, SlotMode.Work, 3));
        var assignment = state.Territory.ScheduleOf(worker.Id).Slots[1];
        Assert.Equal(SlotMode.Work, assignment.Mode);
        Assert.Equal(3, assignment.FacilityId);

        // 空闲不点名设施。
        Assert.True(hub.Assign(worker.Id, 1, SlotMode.Free, 3));
        Assert.Equal(-1, state.Territory.ScheduleOf(worker.Id).Slots[1].FacilityId);

        // 点名不存在的设施拒绝；主角支持排班。
        Assert.False(hub.Assign(worker.Id, 1, SlotMode.Work, 999));
        Assert.True(hub.Assign(master.Id, 0, SlotMode.Work));
        Assert.False(hub.Assign(worker.Id, WorkSlot.Count, SlotMode.Work));
    }

    [Fact]
    public void ResolveSlot_works_the_assigned_facility_only()
    {
        var roster = new Roster();
        roster.Add("你", master: true);
        var a = roster.Add("a");
        var t = new Territory();
        t.AddRoom(new Room { Id = 0 });
        t.AddFacility(new Facility { Id = 1, RoomId = 0, Built = true, Actions = { ActionKind.Mine }, YieldItemId = "ore" });
        t.AddFacility(new Facility { Id = 2, RoomId = 0, Built = true, Actions = { ActionKind.Till }, YieldItemId = "herb" });

        // 排到矿上：只出矿。
        t.Assign(a.Id, 0, SlotMode.Work, 1);
        var logs = t.ResolveSlot(0, roster);
        Assert.Single(logs);
        Assert.Equal(ActionKind.Mine, logs[0].Task);
        Assert.True(a.Bag.Get("ore") > 0);
        Assert.Equal(0, a.Bag.Get("herb"));

        // 没点名设施的段不出产。
        t.Assign(a.Id, 1, SlotMode.Work);
        Assert.Empty(t.ResolveSlot(1, roster));
    }

    [Fact]
    public void Skill_speed_scales_work_progress()
    {
        var c = new CharacterState(1);
        // 采集对口技能是 Farming（吃 Perception）。
        c[CoreStat.Perception] = ActionKindMap.SkillBaseline;
        Assert.Equal(100, ActionKindMap.SpeedPercent(c, ActionKind.Till));

        c[CoreStat.Perception] = ActionKindMap.SkillBaseline + 40;
        Assert.Equal(140, ActionKindMap.SpeedPercent(c, ActionKind.Till));

        c[CoreStat.Perception] = ActionKindMap.SkillBaseline - 20;
        Assert.Equal(80, ActionKindMap.SpeedPercent(c, ActionKind.Till));

        // 上下限夹住。技能最低为 0（核心属性 + 经验都不能为负），
        // 所以实际能到的最低速是 60%；50% 那道下限是防呆，正常玩不到。
        c[CoreStat.Perception] = 500;
        Assert.Equal(ActionKindMap.SpeedMaxPercent, ActionKindMap.SpeedPercent(c, ActionKind.Till));
        c[CoreStat.Perception] = 0;
        Assert.Equal(60, ActionKindMap.SpeedPercent(c, ActionKind.Till));
    }

    [Fact]
    public void Routine_eats_meals_in_window()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var worker = state.Roster.Add("工");
        state.Territory.AddRoom(new Room { Id = 1, Name = "房", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "厨", Open = true });
        state.Territory.Link(1, 2);
        // 灶能存东西，食物就放在它里面（没有虚空库存）。
        var stove = new Facility { Id = 1, Name = "灶", RoomId = 2, Usage = FacilityUsage.Plain, Actions = { ActionKind.Cook, ActionKind.Meal }, Built = true, CanStore = true };
        stove.Contents.Add("bread", 2);
        state.Territory.AddFacility(stove);
        state.Clock.SetTime(1, 12 * 60);
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(worker.Id, 1);
        hub.PassTime(30);
        Assert.Equal(1, stove.Contents.Get("bread"));
        Assert.Equal(state.Clock.Day, worker.Affect.LastMealDay);
        Assert.Equal(1, worker.Affect.LastMealWindow);
    }

    [Fact]
    public void Routine_sleeps_at_night_and_wakes()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var worker = state.Roster.Add("工");
        state.Territory.AddRoom(new Room { Id = 1, Name = "房", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "寝", Open = true });
        state.Territory.Link(1, 2);
        state.Territory.AddFacility(new Facility { Id = 1, Name = "床", RoomId = 2, Usage = FacilityUsage.Rest, Built = true, Actions = { ActionKind.Sleep, ActionKind.Rest } });
        state.Clock.SetTime(1, 23 * 60);
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(worker.Id, 1);
        hub.PassTime(30);
        var w = hub.Day.Workers[0];
        Assert.Equal(ActionKind.Sleep, w.Goal);
        state.Clock.Advance(8 * 60 + 30);
        hub.PassTime(30);
        Assert.NotEqual(ActionKind.Sleep, w.Goal);
    }

    [Fact]
    public void Seeker_comes_shares_seat_and_talk_clears()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var npc = state.Roster.Add("友");
        state.Territory.AddRoom(new Room { Id = 1, Name = "甲", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "乙", Open = true });
        state.Territory.Link(1, 2);
        state.Territory.AddFacility(new Facility { Id = 1, Name = "长椅", RoomId = 2, Capacity = 2, Usage = FacilityUsage.Rest, Built = true, Actions = { ActionKind.Rest, ActionKind.Meal } });
        state.Clock.Advance(9 * 60);
        var hub = new HubSession(state);
        hub.Enter(2);
        hub.Place(npc.Id, 1);
        npc.Affect.ChatDesire = 100;
        Assert.True(hub.Use(1));
        hub.PassTime(30);
        var w = hub.Day.Workers[0];
        Assert.Equal(2, w.RoomId);
        Assert.True(w.WantsChat);
        Assert.Equal(1, w.FacilityId);
        Assert.Contains(hub.Log, l => l.Text == "友似乎想对你说什么。");
        Assert.True(hub.Select(npc.Id));
        Assert.True(hub.Social(SocialAction.Talk));
        Assert.Equal(0, npc.Affect.ChatDesire);
        Assert.False(w.WantsChat);
    }

    [Fact]
    public void Seeker_blocked_by_master_only_room_waits_then_leaves()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var npc = state.Roster.Add("友");
        state.Territory.AddRoom(new Room { Id = 1, Name = "甲", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "私室", Open = true, Permission = RoomPermission.MasterOnly });
        state.Territory.Link(1, 2);
        state.Clock.Advance(9 * 60);
        var hub = new HubSession(state);
        hub.Enter(2);
        hub.Place(npc.Id, 1);
        npc.Affect.ChatDesire = 100;
        npc.Affect.LastPlayDay = 1;
        hub.PassTime(30);
        var w = hub.Day.Workers[0];
        Assert.Equal(1, w.RoomId);
        Assert.False(w.WantsChat);
        Assert.Contains(hub.Log, l => l.Text == "友似乎想对你说什么。");
        hub.PassTime(TerritoryClock.SeekBudget * TerritoryClock.StepMinutes + 30);
        // 放弃找人后不再纠缠，转去过自己的日子（歇着/串门/零活），而不是原地发呆。
        Assert.NotEqual(ActionKind.SeekChat, w.Goal);
        Assert.False(w.WantsChat);
    }

    [Fact]
    public void Mood_scales_work_and_refuses_when_low()
    {
        Assert.Equal(1.25, new Affect { Mood = 90 }.Efficiency());
        Assert.Equal(1.1, new Affect { Mood = 70 }.Efficiency());
        Assert.Equal(1.0, new Affect { Mood = 50 }.Efficiency());
        Assert.Equal(0.8, new Affect { Mood = 30 }.Efficiency());
        Assert.Equal(0.6, new Affect { Mood = 15 }.Efficiency());
        Assert.False(new Affect { Mood = 10 }.AcceptsWork());
        Assert.True(new Affect { Mood = 11 }.AcceptsWork());

        GameState Setup(int mood)
        {
            var state = new GameState();
            state.Roster.Add("你", master: true);
            var worker = state.Roster.Add("工");
            worker[CoreStat.Strength] = 80;
            worker.Affect.Mood = mood;
            state.Territory.AddRoom(new Room { Id = 1, Name = "矿", Open = true });
            state.Territory.AddRoom(new Room { Id = 2, Name = "房", Open = true });
            state.Territory.Link(1, 2);
            state.Territory.AddFacility(new Facility
            {
                Id = 1, RoomId = 1, Usage = FacilityUsage.Plain, Actions = { ActionKind.Mine },
                YieldItemId = "ore", Built = true,
            });
            for (var slot = 0; slot < WorkSlot.Count; slot++)
                state.Territory.Assign(worker.Id, slot, SlotMode.Work, 1);
            state.Clock.Advance(9 * 60);
            var hub = new HubSession(state);
            hub.Enter(2);
            hub.Place(worker.Id, 1);
            return state;
        }

        int TicksToFinish(GameState state)
        {
            var hub = new HubSession(state);
            hub.Enter(2);
            var worker = state.Roster.Members.Find(m => !m.IsMaster);
            if (worker != null)
                hub.Place(worker.Id, 1);
            for (var i = 1; i <= 40; i++)
            {
                if (hub.PassTime(5).Count > 0)
                    return i;
            }
            return 41;
        }

        var high = TicksToFinish(Setup(90));
        var low = TicksToFinish(Setup(30));
        Assert.True(high <= 40);
        Assert.True(low <= 40);
        Assert.True(high < low);

        var refuseState = Setup(5);
        var refuseChar = refuseState.Roster.Members.Find(m => !m.IsMaster);
        if (refuseChar != null)
            refuseChar.Affect.LastPlayDay = 1;
        var refuseHub = new HubSession(refuseState);
        refuseHub.Enter(2);
        var refuseWorker = refuseState.Roster.Members.Find(m => !m.IsMaster);
        if (refuseWorker != null)
            refuseHub.Place(refuseWorker.Id, 1);
        var refuseLogs = refuseHub.PassTime(5);
        var refuseTracked = refuseHub.Day.Workers[0];
        // 心情过低拒绝上工：没有产出，且目标不是工作行动。
        // 不能用 Phase/FacilityId 判——闲时打扫、歇着同样会占设施，也会落 Working。
        Assert.Empty(refuseLogs);
        Assert.False(ActionKindMap.IsWork(refuseTracked.Goal));
        Assert.NotEqual(ActionKind.Mine, refuseTracked.Task);
    }

    [Fact]
    public void Crowded_bedroom_spoils_sleep()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var a = state.Roster.Add("甲");
        var b = state.Roster.Add("乙");
        state.Territory.AddRoom(new Room { Id = 1, Name = "房", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "寝", Open = true });
        state.Territory.Link(1, 2);
        state.Territory.AddFacility(new Facility { Id = 1, Name = "床", RoomId = 2, Usage = FacilityUsage.Rest, Built = true, Actions = { ActionKind.Sleep, ActionKind.Rest } });
        state.Clock.SetTime(1, 23 * 60);
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(a.Id, 1);
        hub.Place(b.Id, 1);
        a.Affect.Mood = 80;
        b.Affect.Mood = 80;
        a.Affect.LastBoredDay = 1;
        a.Affect.LastPlayDay = 1;
        b.Affect.LastBoredDay = 1;
        b.Affect.LastPlayDay = 1;
        hub.PassTime(30);
        state.Clock.Advance(9 * 60);
        hub.PassTime(5);
        Assert.Equal(59, a.Affect.Mood);
        Assert.Equal(59, b.Affect.Mood);
    }

    [Fact]
    public void Together_hours_move_mood()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var friend = state.Roster.Add("友");
        var rival = state.Roster.Add("敌");
        var mate = state.Roster.Add("伴");
        var foe = state.Roster.Add("仇");
        friend.Relations.Add(mate.Id, RelationFlag.Trusted);
        mate.Relations.Add(friend.Id, RelationFlag.Trusted);
        rival.Relations.Add(foe.Id, RelationFlag.Rival);
        foe.Relations.Add(rival.Id, RelationFlag.Rival);
        state.Territory.AddRoom(new Room { Id = 1, Name = "甲", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "乙", Open = true });
        state.Territory.AddRoom(new Room { Id = 3, Name = "丙", Open = true });
        var stoveA = new Facility { Id = 1, Name = "灶甲", RoomId = 1, Usage = FacilityUsage.Plain, Actions = { ActionKind.Cook, ActionKind.Meal }, Built = true, CanStore = true };
        stoveA.Contents.Add("bread", 10);
        state.Territory.AddFacility(stoveA);
        var stoveB = new Facility { Id = 2, Name = "灶乙", RoomId = 2, Usage = FacilityUsage.Plain, Actions = { ActionKind.Cook, ActionKind.Meal }, Built = true, CanStore = true };
        stoveB.Contents.Add("bread", 10);
        state.Territory.AddFacility(stoveB);
        state.Clock.SetTime(1, 14 * 60);
        var hub = new HubSession(state);
        hub.Enter(3);
        hub.Place(friend.Id, 1);
        hub.Place(mate.Id, 1);
        hub.Place(rival.Id, 2);
        hub.Place(foe.Id, 2);
        foreach (var id in new[] { friend.Id, mate.Id, rival.Id, foe.Id })
        {
            var c = state.Roster.Find(id);
            if (c != null)
            {
                c.Affect.LastPlayDay = 1;
                c.Affect.LastBoredDay = 1;
            }
        }
        hub.PassTime(250);
        Assert.True(friend.Affect.Mood > rival.Affect.Mood);
        Worker? friendWorker = null;
        foreach (var w in hub.Day.Workers)
        {
            if (w.CharacterId == friend.Id)
                friendWorker = w;
        }
        Assert.NotNull(friendWorker);
        Assert.Equal(2, friendWorker.Together[mate.Id]);
    }

    [Fact]
    public void No_play_for_days_bores()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var worker = state.Roster.Add("工");
        state.Territory.AddRoom(new Room { Id = 1, Name = "房", Open = true });
        state.Clock.Advance(24 * 60 + 14 * 60);
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(worker.Id, 1);
        hub.PassTime(60);
        Assert.Equal(2, worker.Affect.LastBoredDay);
    }

    [Fact]
    public void Chat_line_shows_each_time_sharing_room()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var npc = state.Roster.Add("友");
        state.Territory.AddRoom(new Room { Id = 1, Name = "甲", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "乙", Open = true });
        state.Territory.Link(1, 2);
        state.Clock.SetTime(1, 14 * 60);
        var hub = new HubSession(state);
        hub.Enter(2);
        hub.Place(npc.Id, 1);
        npc.Affect.ChatDesire = 100;
        hub.PassTime(10);
        // 她追到玩家所在的房间 → 快照里出现这一句。
        Assert.Contains(hub.Log, l => l.Text == "友似乎想对你说什么。");

        Assert.True(hub.Move(1));
        hub.PassTime(10);
        // 换房后两人仍同房 → 这一句照旧出现（快照里始终有一行，且只有一行）。
        var count = 0;
        foreach (var line in hub.Log)
        {
            if (line.Text == "友似乎想对你说什么。")
                count++;
        }
        Assert.Equal(1, count);
    }

    [Fact]
    public void Character_hauls_bag_surplus_into_storage()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var w = state.Roster.Add("工");
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "库房", Open = true });
        state.Territory.Link(1, 2);
        var shelf = new Facility { Id = 1, Name = "货架", RoomId = 2, CanStore = true, Built = true };
        state.Territory.AddFacility(shelf);
        w.Bag.Add("铁矿", 5);   // 背包里有货，库房有货架

        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(w.Id, 1);
        for (var i = 0; i < 20; i++)
            hub.PassTime(5);

        // 角色自己把货搬进了货架，背包清空。
        Assert.Equal(5, shelf.Contents.Get("铁矿"));
        Assert.Equal(0, w.Bag.Get("铁矿"));
    }

    [Fact]
    public void Bench_crafting_needs_materials_hauled_to_the_bench()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var w = state.Roster.Add("工");
        state.Territory.AddRoom(new Room { Id = 1, Name = "库房", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "工坊", Open = true });
        state.Territory.Link(1, 2);
        var shelf = new Facility { Id = 1, Name = "货架", RoomId = 1, CanStore = true, Built = true };
        shelf.Contents.Add("铁矿", 4);   // 料在库房
        state.Territory.AddFacility(shelf);
        state.Territory.AddFacility(new Facility { Id = 2, Name = "铁砧", RoomId = 2, Usage = FacilityUsage.Plain, Actions = { ActionKind.Forge }, Built = true });
        state.Territory.AddRecipe(new Recipe
        {
            ItemId = "铁", Station = ActionKind.Forge, OutputCount = 1,
            Costs = { new RecipeCost("铁矿", 2) },
        });
        for (var slot = 0; slot < WorkSlot.Count; slot++)
            state.Territory.Assign(w.Id, slot, SlotMode.Work, 2);

        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(w.Id, 1);
        for (var i = 0; i < 60; i++)
            hub.PassTime(5);

        // 角色自己去库房把料搬到台子上，才开得了工。
        Assert.True(w.Bag.Get("铁") > 0 || shelf.Contents.Get("铁") > 0);
        // 备料按需搬（每次只搬够做一份的量），不搬空库房。
        Assert.True(shelf.Contents.Get("铁矿") < 4);
    }

    [Fact]
    public void Storage_filter_and_capacity_are_enforced()
    {
        var state = new GameState();
        var w = state.Roster.Add("工");
        var shelf = new Facility { Id = 1, Name = "货架", RoomId = 1, CanStore = true, Built = true };
        shelf.StorageFilter.Add("铁矿");          // 只收矿石
        shelf.StorageCapacity = 2;                 // 最多两件
        state.Territory.AddRoom(new Room { Id = 1, Name = "库房", Open = true });
        state.Territory.AddFacility(shelf);
        w.Bag.Add("铁矿", 10);
        w.Bag.Add("木材", 10);

        // 过滤：木材不收。
        Assert.Equal(0, state.Territory.StoreFrom(w, shelf, "木材", 5));
        // 容量：矿石最多进 2 件。
        Assert.Equal(2, state.Territory.StoreFrom(w, shelf, "铁矿", 5));
        Assert.Equal(2, shelf.Contents.Get("铁矿"));
        Assert.Equal(8, w.Bag.Get("铁矿"));
        // 满了就再也进不去。
        Assert.Equal(0, state.Territory.StoreFrom(w, shelf, "铁矿", 1));
    }

    [Fact]
    public void Items_live_in_bags_or_facility_storages_never_in_the_void()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var worker = state.Roster.Add("工");
        // 采掘吃力量：给到基准值，速度系数才是 100%（技能会影响干活快慢）。
        worker[CoreStat.Strength] = ActionKindMap.SkillBaseline;
        state.Territory.AddRoom(new Room { Id = 1, Name = "房", Open = true });
        // 采集点产矿；同房放一个货架（能存货）。
        state.Territory.AddFacility(new Facility
        {
            Id = 1, Name = "矿", RoomId = 1, Usage = FacilityUsage.Plain, Actions = { ActionKind.Mine },
            YieldItemId = "ore", Built = true,
        });
        var shelf = new Facility { Id = 9, Name = "货架", RoomId = 1, CanStore = true, Built = true };
        state.Territory.AddFacility(shelf);
        for (var slot = 0; slot < WorkSlot.Count; slot++)
            state.Territory.Assign(worker.Id, slot, SlotMode.Work, 1);

        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(worker.Id, 1);
        hub.PassTime(60);

        // 产出先进产出者背包（不是某个“领地库存”），随后被归集搬运进仓储也行——
        // 断言的是物理存在于领地（背包+仓储），绝无虚空。
        Assert.True(state.Territory.CountWith(worker, "ore") > 0);
        // 据点总数 = 背包 + 各设施存货，二者之外没有别处；归集搬运后存量只在两者之间流转。
        Assert.Equal(worker.Bag.Get("ore") + shelf.Contents.Get("ore"),
            state.Territory.CountWith(worker, "ore"));
    }

    [Fact]
    public void Sleep_needs_a_bed_and_never_happens_on_the_floor()
    {
        GameState Setup(bool withBed)
        {
            var state = new GameState();
            state.Roster.Add("你", master: true);
            var worker = state.Roster.Add("工");
            state.Territory.AddRoom(new Room { Id = 1, Name = "房", Open = true });
            // 躺椅只能休息，不能睡。
            var chair = new Facility { Id = 1, Name = "躺椅", RoomId = 1, Usage = FacilityUsage.Rest, Built = true, Actions = { ActionKind.Rest } };
            chair.Actions.Add(ActionKind.Rest);
            state.Territory.AddFacility(chair);
            if (withBed)
            {
                var bed = new Facility { Id = 2, Name = "床", RoomId = 1, Usage = FacilityUsage.Rest, Built = true, Actions = { ActionKind.Sleep, ActionKind.Rest } };
                bed.Actions.Add(ActionKind.Sleep);
                state.Territory.AddFacility(bed);
            }
            state.Clock.SetTime(1, 23 * 60);   // 深夜，本该睡
            return state;
        }

        // 没有床：不许在房间里凭空睡。
        var noBed = Setup(withBed: false);
        var hub1 = new HubSession(noBed);
        hub1.Enter(1);
        var w1 = noBed.Roster.Members.Find(c => !c.IsMaster)!;
        hub1.Place(w1.Id, 1);
        hub1.PassTime(30);
        Assert.NotEqual(ActionKind.Sleep, hub1.Day.Workers[0].Goal);

        // 有床：到床上睡，并占用那张床。
        var withBed = Setup(withBed: true);
        var hub2 = new HubSession(withBed);
        hub2.Enter(1);
        var w2 = withBed.Roster.Members.Find(c => !c.IsMaster)!;
        hub2.Place(w2.Id, 1);
        hub2.PassTime(30);
        Assert.Equal(ActionKind.Sleep, hub2.Day.Workers[0].Goal);
        Assert.Equal(2, hub2.Day.Workers[0].FacilityId);
    }

    [Fact]
    public void Selection_drops_when_character_leaves_the_room()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var npc = state.Roster.Add("友");
        state.Territory.AddRoom(new Room { Id = 1, Name = "甲", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "乙", Open = true });
        state.Territory.Link(1, 2);
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(npc.Id, 1);
        Assert.True(hub.Select(npc.Id));
        Assert.True(hub.HasSelection);

        // 角色走开（或玩家走开）后，选中自动取消，右下角回到“行动”。
        hub.Place(npc.Id, 2);
        hub.PassTime(0);
        Assert.False(hub.HasSelection);
        Assert.Empty(hub.CardsHere());
    }

    [Fact]
    public void Log_rewrites_per_operation()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var hub = new HubSession(state);

        // 没有操作边界时写入同属一批，互相累加。
        hub.Write("开局提示。");
        hub.Write("同批第二行。");
        Assert.Equal(2, hub.Log.Count);

        // 新操作的首笔写入清空旧内容：只留本操作输出。
        hub.BeginOperation();
        hub.Write("新操作的输出。");
        Assert.Single(hub.Log);
        Assert.Equal("新操作的输出。", hub.Log[0].Text);

        // 没写出任何内容的操作不动旧日志。
        hub.BeginOperation();
        Assert.Single(hub.Log);

        // 新操作内的多行输出完整保留。
        hub.BeginOperation();
        hub.Write("第一行。");
        hub.Write("第二行。");
        Assert.Equal(2, hub.Log.Count);
    }

    [Fact]
    public void Feast_food_lifts_mood()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var worker = state.Roster.Add("工");
        state.Territory.AddRoom(new Room { Id = 1, Name = "房", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "厨", Open = true });
        state.Territory.Link(1, 2);
        var stove = new Facility { Id = 1, Name = "灶", RoomId = 2, Usage = FacilityUsage.Plain, Actions = { ActionKind.Cook, ActionKind.Meal }, Built = true, CanStore = true };
        stove.Contents.Add("stew", 1);
        state.Territory.AddFacility(stove);
        // 有桌子才不算“将就一顿”，否则要扣心情（见 Workday.Eat 的无桌惩罚）。
        state.Territory.AddFacility(new Facility { Id = 2, Name = "长桌", RoomId = 2, Usage = FacilityUsage.Plain, Actions = { ActionKind.Meal }, IsTable = true, Built = true });
        state.Territory.SetFoodTier("stew", FoodTier.Feast);
        worker.Affect.Mood = 0;
        state.Clock.SetTime(1, 12 * 60);
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(worker.Id, 1);
        hub.PassTime(5);
        Assert.Equal(0, stove.Contents.Get("stew"));
        Assert.Equal(4, worker.Affect.Mood);
    }

    [Fact]
    public void Eating_without_a_table_costs_mood()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var worker = state.Roster.Add("工");
        state.Territory.AddRoom(new Room { Id = 1, Name = "房", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "厨", Open = true });
        state.Territory.Link(1, 2);
        var stove = new Facility { Id = 1, Name = "灶", RoomId = 2, Usage = FacilityUsage.Plain, Actions = { ActionKind.Cook, ActionKind.Meal }, Built = true, CanStore = true };
        stove.Contents.Add("stew", 1);
        state.Territory.AddFacility(stove);
        state.Territory.SetFoodTier("stew", FoodTier.Feast);
        worker.Affect.Mood = 50;
        state.Clock.SetTime(1, 12 * 60);
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(worker.Id, 1);
        hub.PassTime(5);
        // 丰盛 +4，没有桌子 -3，净 +1。
        Assert.Equal(51, worker.Affect.Mood);
    }

    [Fact]
    public void Triumph_lifts_party_mood()
    {
        var roster = new Roster();
        var a = roster.Add("甲");
        a.Affect.Mood = 20;
        var record = new QuestRecord();
        record.Register(new QuestDef { Id = 1, Name = "讨伐", Kind = QuestKind.Map });
        var run = record.Start(1, new[] { a.Id });
        Assert.NotNull(run);
        record.Complete(run, roster);
        Assert.Equal(28, a.Affect.Mood);
    }

    [Fact]
    public void Favor_scale_has_negative_tiers_and_clamps()
    {
        var c = new CharacterState(1);
        Assert.Equal(Bond.None, c.Condition.Bond);
        c.Condition.AddFavor(99);
        Assert.Equal(Bond.None, c.Condition.Bond);
        c.Condition.AddFavor(1);
        Assert.Equal(Bond.Fond, c.Condition.Bond);

        var d = new CharacterState(2);
        d.Condition.AddFavor(-99);
        Assert.Equal(Bond.None, d.Condition.Bond);
        d.Condition.AddFavor(-1);
        Assert.Equal(Bond.Dislike, d.Condition.Bond);
        d.Condition.AddFavor(-200);
        Assert.Equal(Bond.Hostile, d.Condition.Bond);
        d.Condition.AddFavor(-300);
        Assert.Equal(Bond.Hatred, d.Condition.Bond);
        d.Condition.AddFavor(-1000);
        Assert.Equal(-1000, d.Condition.Favor);
        d.Condition.AddFavor(3000);
        Assert.Equal(1000, d.Condition.Favor);

        var e = new CharacterState(3);
        e.Condition.AddFavor(-150);
        Assert.Equal(Bond.Dislike, e.Condition.Bond);
        Assert.Equal(1, e.TalkDifficulty());
    }

    [Fact]
    public void Intimacy_unlocks_by_favor()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var npc = state.Roster.Add("友");
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        state.Clock.SetTime(1, 14 * 60);
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(npc.Id, 1);
        hub.Select(npc.Id);

        Assert.True(hub.Social(SocialAction.PatHead));
        Assert.Equal(0, npc.Condition.Favor);
        Assert.Contains(hub.Log, l => l.Text == "友躲开了。");

        npc.Condition.AddFavor(100);
        Assert.True(hub.Social(SocialAction.PatHead));
        Assert.Equal(105, npc.Condition.Favor);
        Assert.Equal(54, npc.Affect.Mood);

        Assert.True(hub.Social(SocialAction.BodyContact));
        Assert.Equal(105, npc.Condition.Favor);
        npc.Condition.AddFavor(200);
        Assert.True(hub.Social(SocialAction.BodyContact));
        Assert.Equal(313, npc.Condition.Favor);
        Assert.Equal(60, npc.Affect.Mood);

        Assert.True(hub.Social(SocialAction.Hug));
        Assert.Equal(313, npc.Condition.Favor);
        npc.Condition.AddFavor(200);
        Assert.True(hub.Social(SocialAction.Hug));
        Assert.Equal(525, npc.Condition.Favor);
        Assert.Equal(68, npc.Affect.Mood);

        Assert.True(hub.Social(SocialAction.Kiss));
        Assert.Equal(525, npc.Condition.Favor);
        npc.Condition.AddFavor(300);
        Assert.True(hub.Social(SocialAction.Kiss));
        Assert.Equal(840, npc.Condition.Favor);
        Assert.Equal(77, npc.Affect.Mood);

        Assert.True(hub.Social(SocialAction.PatHead));
        Assert.True(hub.Social(SocialAction.PatHead));
        Assert.True(hub.Social(SocialAction.PatHead));
        Assert.Equal(850, npc.Condition.Favor);
        var loaded = SaveSystem.Load(SaveSystem.Save(state));
        var loadedNpc = loaded.Roster.Find(npc.Id);
        Assert.NotNull(loadedNpc);
        Assert.Equal(3, loadedNpc.Affect.IntimateRewards[0]);
    }

    [Fact]
    public void Hostile_stance_escalates_and_only_hatred_kills()
    {
        var c = new CharacterState(1);
        Assert.Equal(Stance.Ignore, Hostility.StanceTowardPlayer(c));
        Assert.False(Hostility.AttacksPlayer(c));
        c.Condition.AddFavor(-150);
        Assert.Equal(Stance.Threaten, Hostility.StanceTowardPlayer(c));
        Assert.False(Hostility.AttacksPlayer(c));
        c.Condition.AddFavor(-200);
        Assert.Equal(Stance.Attack, Hostility.StanceTowardPlayer(c));
        Assert.True(Hostility.AttacksPlayer(c));
        Assert.False(Hostility.MayKillPlayerSide(c));
        c.Condition.AddFavor(-300);
        Assert.Equal(Stance.Kill, Hostility.StanceTowardPlayer(c));
        Assert.True(Hostility.MayKillPlayerSide(c));
    }

    [Fact]
    public void Hostile_talk_is_refused()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var npc = state.Roster.Add("敌");
        npc.Condition.AddFavor(-350);
        Assert.Equal(Bond.Hostile, npc.Condition.Bond);
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        state.Clock.SetTime(1, 14 * 60);
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(npc.Id, 1);
        hub.Select(npc.Id);
        Assert.True(hub.Social(SocialAction.Talk));
        Assert.Equal(-350, npc.Condition.Favor);
        Assert.Contains(hub.Log, l => l.Text == "敌不想理你。");
        Assert.False(hub.MapCovered);
    }

    [Fact]
    public void Xp_table_maps_levels_1_to_100()
    {
        Assert.Equal(0, XpTable.TotalFor(1));
        Assert.Equal(10, XpTable.TotalFor(2));
        Assert.Equal(810, XpTable.TotalFor(10));
        Assert.Equal(98010, XpTable.TotalFor(100));
        Assert.Equal(1, XpTable.LevelFor(0));
        Assert.Equal(1, XpTable.LevelFor(9));
        Assert.Equal(2, XpTable.LevelFor(10));
        Assert.Equal(99, XpTable.LevelFor(98009));
        Assert.Equal(100, XpTable.LevelFor(98010));
        Assert.Equal(100, XpTable.LevelFor(9999999));
    }

    [Fact]
    public void Level_up_raises_all_cores_hp_and_mana()
    {
        var c = new CharacterState(1);
        c[CoreStat.Strength] = 10;
        c[CoreStat.Intellect] = 4;
        c[CoreStat.Constitution] = 10;
        Assert.Equal(1, c.Level);
        Assert.Equal(20 + 10 * 10 + 1 * 5, c.Combat.MaxHp);

        c.GainLifeExp(LifeSkill.Craft, 5);
        Assert.Equal(2, c.Level);
        Assert.Equal(11, c[CoreStat.Strength]);
        Assert.Equal(5, c[CoreStat.Intellect]);
        Assert.Equal(11, c[CoreStat.Constitution]);
        Assert.Equal(20 + 11 * 10 + 2 * 5, c.Combat.MaxHp);

        c.GainLifeExp(LifeSkill.Craft, 100000);
        Assert.Equal(100, c.Level);
        Assert.Equal(10 + 99, c[CoreStat.Strength]);
    }

    [Fact]
    public void Defs_build_runtime_rooms_and_facilities()
    {
        var state = new GameState();
        var roomDef = new RoomDef
        {
            Id = 1, Name = "厨", RegionId = 0, X = 2, Y = 2,
            Permission = RoomPermission.Public, StartOpen = true, Description = "做饭",
        };
        var facilityDef = new FacilityDef
        {
            Id = 9, Name = "灶", RoomId = 1, Usage = FacilityUsage.Plain, Actions = { ActionKind.Cook, ActionKind.Meal },
            Capacity = 2, Description = "炒菜",
        };
        DefDatabase<RoomDef>.Register(roomDef);
        DefDatabase<FacilityDef>.Register(facilityDef);
        Assert.True(state.Territory.AddRoom(roomDef.ToRuntime()));
        Assert.True(state.Territory.AddFacility(facilityDef.ToRuntime()));
        Assert.Equal("做饭", DefDatabase<RoomDef>.GetById(1)!.Description);
        Assert.Equal(2, state.Territory.Facilities[0].Capacity);
        Assert.Equal(LifeSkill.Cooking, ActionKindMap.SkillOf(ActionKind.Cook));
    }

    [Fact]
    public void Meal_and_sleep_follow_action_table()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var worker = state.Roster.Add("工");
        state.Territory.AddRoom(new Room { Id = 1, Name = "房", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "锻", Open = true });
        state.Territory.Link(1, 2);
        var forge = new Facility { Id = 1, Name = "炉", RoomId = 2, Usage = FacilityUsage.Plain, Actions = { ActionKind.Forge }, Built = true, CanStore = true };
        forge.Contents.Add("bread", 1);
        state.Territory.AddFacility(forge);
        forge.Actions.Add(ActionKind.Meal);
        state.Clock.SetTime(1, 12 * 60);
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(worker.Id, 1);
        hub.PassTime(30);
        Assert.Equal(0, forge.Contents.Get("bread"));
        Assert.Equal(1, worker.Affect.LastMealWindow);
    }

    [Fact]
    public void Play_prefers_leisure_table()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var worker = state.Roster.Add("闲");
        state.Territory.AddRoom(new Room { Id = 1, Name = "房", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "台", Open = true });
        state.Territory.Link(1, 2);
        var stage = new Facility { Id = 1, Name = "戏台", RoomId = 2, Usage = FacilityUsage.Leisure, Built = true, Actions = { ActionKind.Watch } };
        stage.Actions.Add(ActionKind.Watch);
        state.Territory.AddFacility(stage);
        
        state.Clock.SetTime(1, 14 * 60);
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(worker.Id, 1);
        hub.PassTime(10);
        Assert.Equal(2, hub.Day.Workers[0].RoomId);
        Assert.Equal(state.Clock.Day, worker.Affect.LastPlayDay);
    }

    [Fact]
    public void Room_actions_union_facility_table()
    {
        var territory = new Territory();
        territory.AddRoom(new Room { Id = 1, Name = "卧室", Open = true });
        territory.AddRoom(new Room { Id = 2, Name = "厨房", Open = true });
        territory.AddRoom(new Room { Id = 3, Name = "空房", Open = true });
        territory.AddFacility(new Facility { Id = 1, Name = "床", RoomId = 1, Usage = FacilityUsage.Rest, Built = true, Actions = { ActionKind.Sleep, ActionKind.Rest } });
        territory.AddFacility(new Facility { Id = 2, Name = "灶", RoomId = 2, Usage = FacilityUsage.Plain, Actions = { ActionKind.Cook, ActionKind.Meal }, Built = true });
        territory.AddFacility(new Facility { Id = 3, Name = "未建", RoomId = 2, Usage = FacilityUsage.Plain, Actions = { ActionKind.Cook, ActionKind.Meal }, Built = false });

        var bedroom = territory.RoomActions(1);
        Assert.Contains(ActionKind.Sleep, bedroom);
        Assert.Contains(ActionKind.Rest, bedroom);
        Assert.DoesNotContain(ActionKind.Meal, bedroom);

        var kitchen = territory.RoomActions(2);
        Assert.Contains(ActionKind.Meal, kitchen);
        Assert.DoesNotContain(ActionKind.Sleep, kitchen);

        // 空房没有设施，但房间本身仍可观察。
        var empty = territory.RoomActions(3);
        Assert.Contains(ActionKind.Observe, empty);
        Assert.DoesNotContain(ActionKind.Sleep, empty);
        Assert.DoesNotContain(ActionKind.Sleep, empty);
    }

    [Fact]
    public void Development_add_remove_refund_and_relocate()
    {
        var state = new GameState { Money = 100 };
        state.Roster.Add("你", master: true);
        var npc = state.Roster.Add("工");
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        var bedroom = new Room { Id = 2, Name = "卧室", Open = true, Buildable = true };
        bedroom.MaterialCost.Add(new RecipeCost("木材", 10));
        state.Territory.AddRoom(bedroom);
        state.Territory.Link(1, 2);
        var bed = new Facility { Id = 1, Name = "床", RoomId = 2, Usage = FacilityUsage.Rest, Built = true, Buildable = true, Actions = { ActionKind.Sleep, ActionKind.Rest } };
        bed.MaterialCost.Add(new RecipeCost("木材", 5));
        state.Territory.AddFacility(bed);
        state.Roster.Master!.Bag.Add("木材", 10);
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(npc.Id, 2);

        Assert.True(hub.AddRoomCopy(2, 5, 5));
        Assert.Equal(0, state.Roster.Master!.Bag.Get("木材"));
        Assert.Equal(3, state.Territory.Rooms.Count);
        Assert.False(hub.AddRoomCopy(2, 6, 6));

        Assert.True(hub.RemoveRoom(2));
        Assert.Equal(9, state.Roster.Master!.Bag.Get("木材"));
        Assert.Equal(2, state.Territory.Rooms.Count);
        Assert.Empty(state.Territory.Facilities);
        Assert.DoesNotContain(2, state.Territory.Rooms[0].Links);
        Assert.Equal(1, hub.Day.Workers[0].RoomId);

        hub.Enter(3);
        Assert.False(hub.RemoveRoom(3));
    }

    [Fact]
    public void Development_move_links_open_and_facility()
    {
        var state = new GameState { Money = 100 };
        state.Roster.Add("你", master: true);
        var npc = state.Roster.Add("工");
        state.Territory.AddRoom(new Room { Id = 1, Name = "甲", X = 0, Y = 0, Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "乙", X = 1, Y = 0, Open = true });
        state.Territory.AddRoom(new Room { Id = 3, Name = "丙", Open = true, OpenCost = 50 });
        state.Territory.Link(1, 2);
        var bed = new Facility { Id = 1, Name = "床", RoomId = 2, Usage = FacilityUsage.Rest, Built = true, Actions = { ActionKind.Sleep, ActionKind.Rest } };
        bed.MaterialCost.Add(new RecipeCost("木材", 5));
        state.Territory.AddFacility(bed);
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(npc.Id, 2);

        Assert.False(hub.MoveRoom(2, 0, 0));
        Assert.True(hub.MoveRoom(2, 2, 2));
        Assert.Equal(2, state.Territory.Rooms[1].X);

        Assert.True(hub.SetLink(1, 2, false));
        Assert.Empty(state.Territory.Rooms[0].Links);
        Assert.False(hub.SetLink(1, 2, false));
        Assert.True(hub.SetLink(1, 2, true));

        state.Territory.Rooms[2].Open = false;
        Assert.True(hub.SetRoomOpen(3, true));
        Assert.Equal(50, state.Money);
        Assert.False(hub.SetRoomOpen(3, true));
        Assert.True(hub.SetRoomOpen(3, false));
        Assert.False(state.Territory.Rooms[2].Open);

        var w = hub.Day.Track(npc.Id, 2);
        w.FacilityId = 1;
        w.Phase = WorkPhase.Working;
        w.Task = ActionKind.Mine;
        state.Clock.SetTime(1, 22 * 60); // 夜里干活的人跟着被搬走的床走
        Assert.True(hub.MoveFacility(1, 1));
        Assert.Equal(1, state.Territory.Facilities[0].RoomId);
        Assert.Equal(ActionKind.None, w.Task);
        Assert.Equal(1, w.RoomId);
        Assert.True(hub.RemoveFacility(1));
        Assert.Empty(state.Territory.Facilities);
        Assert.Equal(3, state.Roster.Master!.Bag.Get("木材"));
        Assert.False(hub.RemoveFacility(1));
    }

    [Fact]
    public void Buildable_gates_construction_and_lists()
    {
        var state = new GameState { Money = 100 };
        state.Roster.Add("你", master: true);
        var wild = new Room { Id = 1, Name = "森林", Open = true };
        var hall = new Room { Id = 2, Name = "客厅", Open = true, Buildable = true };
        hall.MaterialCost.Add(new RecipeCost("木材", 10));
        state.Territory.AddRoom(wild);
        state.Territory.AddRoom(hall);
        var herb = new Facility { Id = 1, Name = "草药丛", RoomId = 1, Usage = FacilityUsage.Plain, Actions = { ActionKind.Till }, Built = true };
        var bed = new Facility { Id = 2, Name = "床", RoomId = 2, Usage = FacilityUsage.Rest, Built = true, Buildable = true, Actions = { ActionKind.Sleep, ActionKind.Rest } };
        bed.MaterialCost.Add(new RecipeCost("木材", 5));
        state.Territory.AddFacility(herb);
        state.Territory.AddFacility(bed);
        state.Roster.Master!.Bag.Add("木材", 10);
        var hub = new HubSession(state);
        hub.Enter(1);

        Assert.Single(state.Territory.BuildableRooms());
        Assert.Equal("客厅", state.Territory.BuildableRooms()[0].Name);
        Assert.Single(state.Territory.BuildableFacilities());
        Assert.False(hub.AddRoomCopy(1, 3, 3));
        Assert.False(hub.AddFacilityCopy(1, 2));
        Assert.True(hub.AddRoomCopy(2, 3, 3));
        Assert.Equal(0, state.Roster.Master!.Bag.Get("木材"));
    }

    [Fact]
    public void Build_from_defs_uses_catalog()
    {
        var state = new GameState { Money = 100 };
        state.Roster.Add("你", master: true);
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        var vacant = new Room { Id = 2, Name = "空房", X = 3, Y = 3, Open = true, Vacant = true };
        state.Territory.AddRoom(vacant);
        var roomDef = new RoomDef { Id = 101, Name = "菜园", Buildable = true };
        roomDef.MaterialCost.Add(new RecipeCost("木材", 3));
        var facilityDef = new FacilityDef
        {
            Id = 1001, Name = "菜地", Usage = FacilityUsage.Plain, Actions = { ActionKind.Till },
            Capacity = 2, YieldItemId = "小麦", Buildable = true,
        };
        facilityDef.MaterialCost.Add(new RecipeCost("木材", 1));
        DefDatabase<RoomDef>.Register(roomDef);
        DefDatabase<FacilityDef>.Register(facilityDef);
        state.Roster.Master!.Bag.Add("木材", 4);
        var hub = new HubSession(state);
        hub.Enter(1);

        // 房间只能装进空房：不是空房、或空房不存在，都放不进去，且不该白扣材料。
        Assert.False(hub.BuildRoomDef(101, 1));
        Assert.False(hub.BuildRoomDef(999, vacant.Id));
        Assert.Equal(4, state.Roster.Master!.Bag.Get("木材"));
        Assert.True(hub.BuildRoomDef(101, vacant.Id));
        Assert.Equal(1, state.Roster.Master!.Bag.Get("木材"));
        var added = state.Territory.RoomAt(3, 3);
        Assert.NotNull(added);
        Assert.True(added.Open);
        Assert.True(added.Buildable);
        Assert.Null(state.Territory.Room(vacant.Id)); // 空房被顶替掉

        Assert.True(hub.BuildFacilityDef(1001, added.Id));
        Assert.Equal(0, state.Roster.Master!.Bag.Get("木材"));
        Assert.True(state.Territory.Facilities[0].Supports(ActionKind.Till));
        Assert.Equal("小麦", state.Territory.Facilities[0].YieldItemId);
        Assert.False(hub.BuildFacilityDef(1001, added.Id));
    }

    [Fact]
    public void Develop_vacant_cell_creates_vacant_room_and_scales_cost()
    {
        var state = new GameState { Money = 100000 };
        state.Roster.Add("你", master: true);
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", X = 2, Y = 2, Open = true });
        state.Roster.Master!.Bag.Add("木材", 200);
        state.Roster.Master!.Bag.Add("石材", 200);
        var hub = new HubSession(state);
        hub.Enter(1);

        // 起步价：钱 1000 ＋ 木材 5；还没到第 3 个，所以不要石材。
        Assert.Equal(1000, hub.VacantCostMoney);
        Assert.Equal(5, hub.VacantCostWood);
        Assert.Equal(0, hub.VacantCostStone);

        // 不挨着已开发地方的空格：开不了。
        Assert.False(hub.CanDevelopVacantCell(0, 0, 0));
        Assert.False(hub.DevelopVacantCell(0, 0, 0));

        // 挨着的空格：能开，开出来是一间「空房」。
        Assert.True(hub.CanDevelopVacantCell(0, 2, 3));
        Assert.True(hub.DevelopVacantCell(0, 2, 3));
        var vacant = state.Territory.RoomAt(0, 2, 3);
        Assert.NotNull(vacant);
        Assert.True(vacant.Vacant);
        Assert.True(vacant.Open);
        Assert.Equal("空房", vacant.Name);

        // 每开一格贵 20%：第 2 个 1200/6，第 3 个 1440/7 且开始要石材 7。
        Assert.Equal(1, state.Territory.VacantDevelopCount);
        Assert.Equal(1200, hub.VacantCostMoney);
        Assert.Equal(6, hub.VacantCostWood);
        Assert.Equal(0, hub.VacantCostStone);

        Assert.True(hub.DevelopVacantCell(0, 1, 2));
        Assert.Equal(2, state.Territory.VacantDevelopCount);
        Assert.Equal(1440, hub.VacantCostMoney);
        Assert.Equal(7, hub.VacantCostWood);
        Assert.Equal(7, hub.VacantCostStone);

        // 已经有房间的格子不能再开拓。
        Assert.False(hub.DevelopVacantCell(0, 2, 2));
    }

    [Fact]
    public void Region_unlocks_in_two_rings_when_filled()
    {
        var t = new Territory();
        Assert.True(t.IsRegionUnlocked(0));
        Assert.False(t.IsRegionUnlocked(1));
        Assert.False(t.IsRegionUnlocked(5));

        // 中心区还没铺满 → 什么都不开。
        t.AddRoom(new Room { Id = 1, RegionId = 0, X = 0, Y = 0 });
        Assert.Empty(t.TryUnlockByFill());
        Assert.False(t.IsRegionUnlocked(1));

        // 铺满中心 25 格 → 开四正（1/2/3/4），四角仍关着。
        var id = 1;
        for (var x = 0; x < Territory.RegionSize; x++)
        {
            for (var y = 0; y < Territory.RegionSize; y++)
            {
                if (t.RoomAt(0, x, y) != null)
                    continue;
                t.AddRoom(new Room { Id = ++id, RegionId = 0, X = x, Y = y });
            }
        }
        Assert.True(t.IsRegionFull(0));
        var ring1 = t.TryUnlockByFill();
        Assert.Equal(4, ring1.Count);
        Assert.True(t.IsRegionUnlocked(1));
        Assert.True(t.IsRegionUnlocked(4));
        Assert.False(t.IsRegionUnlocked(5));

        // 四正里任意一块铺满 → 开四角。
        for (var x = 0; x < Territory.RegionSize; x++)
        {
            for (var y = 0; y < Territory.RegionSize; y++)
                t.AddRoom(new Room { Id = ++id, RegionId = 2, X = x, Y = y });
        }
        var ring2 = t.TryUnlockByFill();
        Assert.Equal(4, ring2.Count);
        Assert.True(t.IsRegionUnlocked(5));
        Assert.True(t.IsRegionUnlocked(8));
    }

    [Fact]
    public void Region_gates_face_each_other()
    {
        // 中心区的北邻是北区，北区的南邻回到中心区——连接点是互指的。
        Assert.Equal(1, Territory.RegionNeighbor(0, Territory.RegionDir.North));
        Assert.Equal(0, Territory.RegionNeighbor(1, Territory.RegionDir.South));
        Assert.Equal(2, Territory.RegionNeighbor(0, Territory.RegionDir.East));
        Assert.Equal(4, Territory.RegionNeighbor(0, Territory.RegionDir.West));

        var (nx, ny) = Territory.RegionGate(Territory.RegionDir.North);
        Assert.Equal(2, nx);
        Assert.Equal(0, ny);
        var (sx, sy) = Territory.RegionGate(Territory.RegionDir.South);
        Assert.Equal(2, sx);
        Assert.Equal(4, sy);

        Assert.Equal(Territory.RegionDir.South, Territory.Opposite(Territory.RegionDir.North));
        Assert.Equal(Territory.RegionDir.West, Territory.Opposite(Territory.RegionDir.East));

        // 四角没有正方向邻居。
        Assert.Equal(-1, Territory.RegionNeighbor(5, Territory.RegionDir.North));
    }

    [Fact]
    public void Cross_region_needs_rooms_on_both_gates()
    {
        var state = new GameState();
        var hub = new HubSession(state);
        var t = state.Territory;

        // 东区解锁，中心区东连接点与东区西连接点各放一间房——两边各有房才通。
        t.SetUnlockedRegionMask(t.UnlockedRegionMask | (1 << 2));
        var (ex, ey) = Territory.RegionGate(Territory.RegionDir.East);
        var (wx, wy) = Territory.RegionGate(Territory.RegionDir.West);
        t.AddRoom(new Room { Id = 1, Name = "东门", RegionId = 0, X = ex, Y = ey, Open = true });
        t.AddRoom(new Room { Id = 2, Name = "西门", RegionId = 2, X = wx, Y = wy, Open = true });

        hub.Enter(1);
        Assert.Equal(2, hub.CrossTargetRegion(hub.PlayerRoomId));
        Assert.True(hub.CrossTo(2));
        Assert.Equal(2, hub.RegionId);
        Assert.Equal(2, hub.PlayerRoomId);

        // 北区虽然解锁了，但北区那一侧的连接点上没有房 → 过不去。
        t.SetUnlockedRegionMask(t.UnlockedRegionMask | (1 << 1));
        var (nx, ny) = Territory.RegionGate(Territory.RegionDir.North);
        t.AddRoom(new Room { Id = 3, Name = "北门", RegionId = 0, X = nx, Y = ny, Open = true });
        hub.Enter(3);
        Assert.Equal(-1, hub.CrossTargetRegion(hub.PlayerRoomId));
        Assert.False(hub.CrossTo(1));

        // 站在中间的空房里（不在任何连接点上）也不通。
        t.AddRoom(new Room { Id = 4, Name = "中庭", RegionId = 0, X = 2, Y = 2, Open = true });
        hub.Enter(4);
        Assert.Equal(-1, hub.CrossTargetRegion(hub.PlayerRoomId));
    }

    [Fact]
    public void Selection_and_rest_bind_character_and_fixture()
    {
        var state = new GameState();
        state.Clock.SetTime(1, 22 * 60);
        var master = state.Roster.Add("你", master: true);
        var friend = state.Roster.Add("友");
        var stranger = state.Roster.Add("客");
        state.Territory.AddRoom(new Room { Id = 1, Name = "甲", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "乙", Open = true });
        state.Territory.Link(1, 2);
        state.Territory.AddFacility(new Facility { Id = 1, Name = "床", RoomId = 1, Usage = FacilityUsage.Rest, Built = true, Actions = { ActionKind.Sleep, ActionKind.Rest } });
        state.Territory.AddFacility(new Facility { Id = 2, Name = "矿镐", RoomId = 1, Usage = FacilityUsage.Plain, Actions = { ActionKind.Mine }, Built = true });
        state.Territory.AddFacility(new Facility { Id = 3, Name = "矿镐", RoomId = 2, Usage = FacilityUsage.Plain, Actions = { ActionKind.Mine }, Built = true });
        for (var slot = 0; slot < WorkSlot.Count; slot++)
        {
            state.Territory.Assign(friend.Id, slot, SlotMode.Work, 2);
            state.Territory.Assign(stranger.Id, slot, SlotMode.Work, 3);
        }
        state.Clock.SetTime(1, 14 * 60);
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(friend.Id, 1);
        hub.Place(stranger.Id, 2);

        Assert.False(hub.Select(master.Id));
        Assert.False(hub.Select(stranger.Id));
        Assert.True(hub.Select(friend.Id));
        Assert.Single(hub.CardsHere());
        Assert.Equal(friend.Id, hub.CardsHere()[0].Id);

        // 房间级只剩“观察”；日常行动要坐到设施上（见 ActAtFixture）。
        Assert.True(hub.Act(PlaceAction.Observe));
        Assert.True(hub.Use(2));
        // 2 号是“矿镐”（Mine），不在行动表里，什么日常都做不了。
        Assert.False(hub.ActAtFixture(ActionKind.Rest));
        Assert.False(hub.ActAtFixture(ActionKind.Sleep));
        Assert.True(hub.Use(1));
        // 1 号是“床”（Rest），支持睡觉；不支持吃饭。
        state.Clock.SetTime(1, 22 * 60);
        Assert.True(hub.ActAtFixture(ActionKind.Sleep));
        Assert.False(hub.ActAtFixture(ActionKind.Meal));

        Assert.True(hub.Move(2));
        Assert.Equal(-1, hub.SelectedCharacterId);
        Assert.True(hub.Select(stranger.Id));
        Assert.True(hub.Social(SocialAction.Talk));
        hub.CloseOverlay();
        Assert.True(hub.Move(1));
        Assert.False(hub.Social(SocialAction.Talk));
    }

    [Fact]
    public void Sleep_and_meal_need_their_own_facility()
    {
        // 睡觉必须到床上：床只声明 Sleep，因此睡得了、吃不了。
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var worker = state.Roster.Add("工");
        state.Territory.AddRoom(new Room { Id = 1, Name = "甲", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "乙", Open = true });
        state.Territory.Link(1, 2);
        var bed = new Facility { Id = 1, Name = "床", RoomId = 2, Usage = FacilityUsage.Rest, Built = true, Actions = { ActionKind.Sleep, ActionKind.Rest } };
        bed.Actions.Add(ActionKind.Sleep);
        state.Territory.AddFacility(bed);

        state.Clock.SetTime(1, 23 * 60);
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(worker.Id, 1);
        hub.PassTime(30);
        var w = hub.Day.Workers[0];
        Assert.Equal(ActionKind.Sleep, w.Goal);
        Assert.Equal(2, w.RoomId);
        Assert.Equal(1, w.FacilityId);
    }

    [Fact]
    public void Facility_storage_interaction_and_configuration_flow()
    {
        var state = new GameState();
        var master = state.Roster.Add("你", master: true);
        master.Bag.Add("石料", 3);
        master.Bag.Add("木材", 2);

        state.Territory.AddRoom(new Room { Id = 1, Name = "仓库", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "庭院", Open = true });
        state.Territory.Link(1, 2);

        var shelf = new Facility
        {
            Id = 10, Name = "货架", RoomId = 1, Capacity = 2,
            CanStore = true, StorageCapacity = 5, Built = true,
        };
        var bed = new Facility
        {
            Id = 11, Name = "床", RoomId = 1, Capacity = 1,
            CanStore = false, Built = true,
        };
        bed.Actions.Add(ActionKind.Sleep);

        state.Territory.AddFacility(shelf);
        state.Territory.AddFacility(bed);

        var hub = new HubSession(state);
        hub.Enter(1);

        // 没坐设施前：存储页未开。
        Assert.Null(hub.OpenStorageFacility);

        // 坐到床上：床不支持存储，只有睡觉。
        Assert.True(hub.Use(bed.Id));
        var bedActions = hub.ActionsAtCurrentFixture();
        Assert.Contains(ActionKind.Sleep, bedActions);
        Assert.DoesNotContain(ActionKind.Store, bedActions);

        // 坐到货架上：支持打开货架（Store 行动）。
        Assert.True(hub.Use(shelf.Id));
        var shelfActions = hub.ActionsAtCurrentFixture();
        Assert.Contains(ActionKind.Store, shelfActions);

        // 执行打开货架行动：打开交互页，不推进游戏时间。
        var clockBefore = state.Clock.Minutes;
        Assert.True(hub.ActAtFixture(ActionKind.Store));
        Assert.Equal(clockBefore, state.Clock.Minutes);
        Assert.Equal(shelf, hub.OpenStorageFacility);

        // 存储一览显示背包内的物品。
        var rows = hub.StorageRows();
        Assert.Equal(2, rows.Count);
        Assert.Equal("木材", rows[0].ItemId);
        Assert.Equal(2, rows[0].InBag);
        Assert.Equal(0, rows[0].InStorage);

        // 存入 1 份木材：货架内 +1，背包 -1，时间推进 5 分钟。
        Assert.True(hub.StoreOne("木材"));
        Assert.Equal(1, shelf.Contents.Get("木材"));
        Assert.Equal(1, master.Bag.Get("木材"));
        Assert.Equal(clockBefore + 5, state.Clock.Minutes);

        // 设置白名单过滤：排除木材后，再存入木材被拒绝。
        Assert.True(hub.ToggleStorageFilter("木材"));
        Assert.DoesNotContain("木材", shelf.StorageFilter);
        Assert.False(hub.StoreOne("木材"));

        // 恢复允许后即可存入。
        Assert.True(hub.ToggleStorageFilter("木材"));
        Assert.Contains("木材", shelf.StorageFilter);
        Assert.True(hub.StoreOne("木材"));
        Assert.Equal(2, shelf.Contents.Get("木材"));
        Assert.Equal(0, master.Bag.Get("木材"));

        // 从货架取出 1 份木材。
        Assert.True(hub.TakeOne("木材"));
        Assert.Equal(1, shelf.Contents.Get("木材"));
        Assert.Equal(1, master.Bag.Get("木材"));

        // 离开设施或换房：交互页自动关闭。
        hub.Move(2);
        Assert.Null(hub.OpenStorageFacility);
        Assert.Null(hub.UsingFixtureId);
    }

    [Fact]
    public void DefDatabase_registers_retrieves_and_categorizes()
    {
        DefaultDefs.EnsureInitialized();

        var food = DefDatabase<ThingDef>.Get("干粮");
        Assert.NotNull(food);
        Assert.True(food.IsFood);
        Assert.Equal("Food", food.Category);
        Assert.True(food.IsInCategory("Root"));
        Assert.True(food.IsInCategory("Food"));
        Assert.False(food.IsInCategory("RawMaterial"));

        // 木材现在住材料表（MaterialDef 继承 ThingDef，本身就是物品）。
        var wood = Items.Get("木材");
        Assert.NotNull(wood);
        Assert.False(wood.IsFood);
        Assert.Equal("RawMaterial", wood.Category);

        var sleepJob = DefDatabase<JobDef>.Get("Sleep");
        Assert.NotNull(sleepJob);
        Assert.Equal(ActionKind.Sleep, sleepJob.LegacyGoal);
        Assert.True(sleepJob.IsVitalNeed);
    }

    [Fact]
    public void Worker_state_machine_manages_state_transitions_and_snapshots()
    {
        var territory = new Territory();
        territory.AddRoom(new Room { Id = 1, Name = "工坊", Open = true });
        territory.AddRoom(new Room { Id = 2, Name = "寝室", Open = true });
        territory.Link(1, 2);

        var bed = new Facility { Id = 1, Name = "石床", RoomId = 2, Usage = FacilityUsage.Rest, Built = true, Actions = { ActionKind.Sleep, ActionKind.Rest } };
        territory.AddFacility(bed);

        var character = new CharacterState(10) { Name = "工匠" };
        var worker = new Worker { CharacterId = 10, RoomId = 1, FacilityId = 1 };

        var ctx = new WorkerContext
        {
            Territory = territory,
            Character = character,
            Worker = worker,
            UsedFacilities = new HashSet<int>(),
            Rng = new System.Random(),
        };

        // 1. 切入睡眠状态
        var sleepState = new SleepingState();
        worker.StateMachine.TransitionTo(sleepState, ctx);
        Assert.Equal(ActionKind.Sleep, worker.Goal);
        Assert.Same(sleepState, worker.StateMachine.CurrentState);

        // 描述快照输出（准确反映所在设施）
        var desc = worker.StateMachine.Describe(ctx);
        Assert.Equal("工匠在工坊的石床上睡觉。", desc);

        // 2. 切入闲暇状态（无绑定设施时做零活）
        worker.FacilityId = -1;
        worker.Loiter = LoiterKind.Chores;
        var loiterState = new LoiteringState();
        worker.StateMachine.TransitionTo(loiterState, ctx);
        Assert.Equal(ActionKind.Loiter, worker.Goal);
        Assert.Same(loiterState, worker.StateMachine.CurrentState);

        var choreDesc = worker.StateMachine.Describe(ctx);
        Assert.Equal("工匠在工坊收拾东西。", choreDesc);

        // 3. 结束任务
        worker.StateMachine.TransitionTo(null, ctx);
        Assert.Equal(ActionKind.None, worker.Goal);
        Assert.Null(worker.StateMachine.CurrentState);
    }

    [Fact]
    public void Room_tags_enforce_at_least_one_and_support_unlimited_categories()
    {
        var territory = new Territory();

        // 1. 手动添加多个标签（支持多标签）
        var room1 = new Room { Id = 1, Name = "锻造工坊", Open = true };
        room1.AddTag("室内");
        room1.AddTag("工作间");
        Assert.True(territory.AddRoom(room1));
        Assert.Equal(2, room1.Tags.Count);
        Assert.True(room1.HasTag("室内"));
        Assert.True(room1.HasTag("工作间"));

        // 2. 没有任何显式标签时，EnsureDefaultTag 保证至少有 1 个标签
        var room2 = new Room { Id = 2, Name = "后院菜园", Open = true };
        Assert.Empty(room2.Tags);
        Assert.True(territory.AddRoom(room2));
        Assert.True(room2.Tags.Count >= 1);
        Assert.True(room2.HasTag("室外"));

        // 3. 从 RoomDef 生成时保持标签并保障至少 1 个标签
        var def = new RoomDef
        {
            Id = 3,
            Name = "主卧室",
            Tags = new List<string> { "室内", "卧室" },
        };
        var room3 = def.ToRuntime();
        Assert.True(territory.AddRoom(room3));
        Assert.Equal(2, room3.Tags.Count);
        Assert.True(room3.HasTag("室内"));
        Assert.True(room3.HasTag("卧室"));

        // 4. 存读档保真
        var state = new GameState();
        state.Territory.AddRoom(room1);
        state.Territory.AddRoom(room2);
        state.Territory.AddRoom(room3);
        var json = SaveSystem.Save(state);
        var loaded = SaveSystem.Load(json);

        var loaded1 = loaded.Territory.Rooms.Find(r => r.Id == 1);
        Assert.NotNull(loaded1);
        Assert.Equal(2, loaded1.Tags.Count);
        Assert.True(loaded1.HasTag("室内"));
        Assert.True(loaded1.HasTag("工作间"));

        var loaded2 = loaded.Territory.Rooms.Find(r => r.Id == 2);
        Assert.NotNull(loaded2);
        Assert.True(loaded2.Tags.Count >= 1);

        var loaded3 = loaded.Territory.Rooms.Find(r => r.Id == 3);
        Assert.NotNull(loaded3);
        Assert.Equal(2, loaded3.Tags.Count);
        Assert.True(loaded3.HasTag("卧室"));
    }

    [Fact]
    public void Building_defs_register_in_DefDatabase_and_instantiate_consistently()
    {
        // 验证 RoomDef 与 FacilityDef 作为统一 Def 注册进 DefDatabase，且房间与设施互不引用
        var roomDef = new RoomDef
        {
            DefName = "Room_Bakery",
            Id = 143,
            Label = "面包房",
            Tags = new List<string> { "室内", "工作间" },
            Buildable = true,
        };
        roomDef.MaterialCost.Add(new RecipeCost("木材", 6));

        var facDef = new FacilityDef
        {
            DefName = "Facility_Oven_1043",
            Id = 1043,
            Label = "烤炉",
            Usage = FacilityUsage.Plain, Actions = { ActionKind.Cook, ActionKind.Meal },
            Capacity = 2,
            Storage = true,
            Buildable = true,
        };

        DefDatabase<RoomDef>.Register(roomDef);
        DefDatabase<FacilityDef>.Register(facDef);

        Assert.Equal(roomDef, DefDatabase<RoomDef>.Get("Room_Bakery"));
        Assert.Equal(facDef, DefDatabase<FacilityDef>.Get("Facility_Oven_1043"));

        // 实例化验证
        var runtimeRoom = roomDef.ToRuntime();
        Assert.Equal("面包房", runtimeRoom.Name);
        Assert.True(runtimeRoom.HasTag("室内"));
        Assert.True(runtimeRoom.HasTag("工作间"));

        var runtimeFac = facDef.ToRuntime();
        Assert.Equal("烤炉", runtimeFac.Name);
        Assert.True(runtimeFac.CanStore);
        Assert.True(runtimeFac.Supports(ActionKind.Cook));
    }

    [Fact]
    public void Cook_fetches_ingredients_from_storage_cooks_delivers_to_table_and_eats()
    {
        DefLoader.Reset();
        DefaultDefs.EnsureInitialized();
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var cook = state.Roster.Add("厨师");
        var eater = state.Roster.Add("食客");
        cook[CoreStat.Constitution] = 80;

        // 连通四个房间：库房(1) <-> 庭院(2) <-> 厨房(3) <-> 餐厅(4)
        state.Territory.AddRoom(new Room { Id = 1, Name = "库房", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "庭院", Open = true });
        state.Territory.AddRoom(new Room { Id = 3, Name = "厨房", Open = true });
        state.Territory.AddRoom(new Room { Id = 4, Name = "餐厅", Open = true });
        state.Territory.Link(1, 2);
        state.Territory.Link(2, 3);
        state.Territory.Link(3, 4);

        // 库房摆货架存料：小麦 2 份，水 2 份
        var shelf = new Facility { Id = 1, Name = "货架", RoomId = 1, CanStore = true, Built = true };
        shelf.Contents.Add("小麦", 2);
        shelf.Contents.Add("水", 2);
        state.Territory.AddFacility(shelf);

        // 厨房摆烹饪灶台（支持 Cook）
        var stove = new Facility { Id = 2, Name = "灶台", RoomId = 3, Capacity = 2, Built = true, Actions = { ActionKind.Cook } };
        state.Territory.AddFacility(stove);

        // 餐厅摆餐桌（存熟食，isTable = true）与餐椅（支持 Meal）
        var table = new Facility
        {
            Id = 3, Name = "餐桌", RoomId = 4, Capacity = 4, Built = true,
            IsTable = true, CanStore = true, StorageCapacity = 12, StorageFilter = { "Meal" }
        };
        var chair = new Facility
        {
            Id = 4, Name = "餐椅", RoomId = 4, Capacity = 2, Built = true,
            Actions = { ActionKind.Meal }
        };
        state.Territory.AddFacility(table);
        state.Territory.AddFacility(chair);

        // 领地指定烹饪料理目标：炖菜 (stew)
        state.Territory.TargetCookItem = "stew";

        // 排班：厨师在工作时段专心烹饪
        for (var s = 0; s < WorkSlot.Count; s++)
            state.Territory.Assign(cook.Id, s, SlotMode.Work, 2);

        // 开局厨师在厨房(3)，食客在餐厅(4)
        var hub = new HubSession(state);
        hub.Enter(3);
        hub.Place(cook.Id, 3);
        hub.Place(eater.Id, 4);

        // 1. 推进时间：厨师发现灶台缺料，物理走去库房取料（禁止瞬移！途经庭院2，尚未抵达库房1，货架存料完好！）
        hub.PassTime(5);
        var worker = hub.Day.Workers.First(w => w.CharacterId == cook.Id);
        Assert.Equal(ActionKind.Haul, worker.Goal);
        Assert.Equal(HaulPhase.Fetching, worker.HaulPhase);
        Assert.Equal(shelf.Id, worker.HaulSourceId);
        Assert.Equal(stove.Id, worker.HaulTargetId);
        Assert.Equal(2, worker.RoomId); // 此时刚走到中途庭院(2)
        Assert.Equal(2, shelf.Contents.Get("小麦")); // 尚未抵达库房，货架原料绝无隔空被扣！

        // 推进到厨师走回厨房、完成炖菜烹饪并端到餐厅餐桌
        for (var i = 0; i < 40; i++)
        {
            hub.PassTime(5);
            if (table.Contents.Get("stew") > 0)
                break;
        }

        // 炖菜已被成功端到餐桌上储存，库房材料被消耗，整个过程物理流转无瞬移！
        Assert.True(table.Contents.Get("stew") > 0);
        Assert.True(shelf.Contents.Get("小麦") < 2);

        // 2. 推进到午餐时间（12:00 = 720 分钟），食客坐在餐桌旁优雅享用炖菜
        state.Clock.Advance(12 * 60 - state.Clock.Minutes);
        eater.Affect.Mood = 50;
        hub.PassTime(15);

        // 食客从餐桌成功取食炖菜并吃下，获得丰盛料理心情加成（Feast +4），有桌不扣心情
        Assert.True(eater.Affect.LastMealWindow >= 0);
        Assert.Equal(54, eater.Affect.Mood);
    }

    [Fact]
    public void Weapons_are_runtime_instances_forged_from_material_times_type()
    {
        DefaultDefs.EnsureInitialized();

        // 全项目只有一张材料表：10 种，其中 7 种能缝甲。
        Assert.Equal(10, DefDatabase<MaterialDef>.All.Count);
        Assert.Equal(8, DefDatabase<MaterialDef>.All.Count(m => m.WeaponUsable));
        Assert.Equal(7, DefDatabase<MaterialDef>.All.Count(m => m.ArmorUsable));
        Assert.Equal(7, DefDatabase<WeaponTypeDef>.All.Count);
        // 轴表只是基座：8 材料 × 7 类型 = 56 个配方，不是 56 件货。
        Assert.Equal(56, Weapons.BaseCount());
        Assert.NotEmpty(DefDatabase<EnchantDef>.All);

        // 材料按 Tier 由劣到优：木材 → 珊瑚 → 青铜 → 铁 → … → 以太。
        var tiers = Weapons.Materials.Select(m => m.Label).ToArray();
        Assert.Equal(new[] { "木材", "珊瑚", "青铜", "铁", "钢", "秘银", "精金", "以太" }, tiers);
        // 珊瑚夹在木材与铁之间，青铜在铁之下。
        Assert.True(TierOf("珊瑚") > TierOf("木材"));
        Assert.True(TierOf("珊瑚") < TierOf("青铜"));
        Assert.True(TierOf("青铜") < TierOf("铁"));
        Assert.True(TierOf("铁") < TierOf("钢"));
        // 黑曜石已移除；石制武器没有。
        Assert.Null(DefDatabase<MaterialDef>.Get("黑曜石"));
        Assert.Null(DefDatabase<MaterialDef>.Get("石材"));

        int TierOf(string defName) => DefDatabase<MaterialDef>.Get(defName)!.Tier;

        var state = new GameState();
        var registry = state.Territory.Weapons;

        // 同一基座锻两件，品质差得远。
        var plain = WeaponForge.Forge("木材", WeaponType.Sword,
            quality: Quality.Common, enchant: "", blessed: false, enhance: 0);
        var grand = WeaponForge.Forge("精金", WeaponType.Sword,
            quality: Quality.Legendary, enchant: "Blazing", blessed: true, enhance: 5);
        registry.Add(plain);
        registry.Add(grand);

        Assert.NotEqual(plain.Id, grand.Id);
        Assert.Equal(Quality.Common, plain.Quality);
        Assert.Equal(Quality.Legendary, grand.Quality);
        Assert.Equal(0, plain.Enhance);
        Assert.Equal(5, grand.Enhance);
        Assert.Empty(plain.Enchant);
        Assert.Equal("Blazing", grand.Enchant);
        Assert.False(plain.Blessed);
        Assert.True(grand.Blessed);

        // 名字只带祝福与附魔；品质/强化不进名字，进详情。
        Assert.Equal("木材剑", plain.Name);
        Assert.Equal("受祝福的炽热的精金剑", grand.Name);

        // 品质是面板乘数：同基座同强化，传说远强于寻常。
        var commonSword = WeaponForge.Forge("铁", WeaponType.Sword, quality: Quality.Common, enchant: "", blessed: false, enhance: 0);
        var legendSword = WeaponForge.Forge("铁", WeaponType.Sword, quality: Quality.Legendary, enchant: "", blessed: false, enhance: 0);
        Assert.True(legendSword.Panel > commonSword.Panel * 2, "品质应显著抬面板");
        var crudeSword = WeaponForge.Forge("铁", WeaponType.Sword, quality: Quality.Crude, enchant: "", blessed: false, enhance: 0);
        Assert.True(crudeSword.Panel < commonSword.Panel, "粗劣应弱于寻常");

        // 材料等级也进面板与价值。
        // 材料比较必须钉死品质——否则粗劣以太撞上传说木剑，材料差会被品质差盖过去。
        Assert.True(WeaponForge.Forge("以太", WeaponType.Sword, quality: Quality.Common, enchant: "", blessed: false, enhance: 0).Panel
                    > WeaponForge.Forge("木材", WeaponType.Sword, quality: Quality.Common, enchant: "", blessed: false, enhance: 0).Panel);
        Assert.True(WeaponForge.Forge("以太", WeaponType.Sword, quality: Quality.Common, enchant: "", blessed: false, enhance: 0).Value
                    > WeaponForge.Forge("木材", WeaponType.Sword, quality: Quality.Common, enchant: "", blessed: false, enhance: 0).Value);

        // 详情列明其余字段（品质/材料/类型/强化/附魔/祝福），不与名字重复。
        var details = grand.DescribeDetails();
        Assert.Contains("品质　传说", details);
        Assert.Contains("材料　精金", details);
        Assert.Contains("类型　剑", details);
        Assert.Contains("强化　+5", details);
        Assert.Contains("附魔　炽热的（力道炽盛）", details);
        Assert.Contains("祝福　受祝福", details);

        // 随机生成确实在变。
        var rng = new Random(11);
        var names = Enumerable.Range(0, 40)
            .Select(_ => WeaponForge.Forge("铁", WeaponType.Axe, rng: rng).Name)
            .Distinct().Count();
        Assert.True(names > 3, "同基座应能衍生出多件不同实例");
        var qualities = Enumerable.Range(0, 300)
            .Select(_ => WeaponForge.Forge("木材", WeaponType.Bow, rng: rng).Quality)
            .Distinct().Count();
        Assert.True(qualities >= 4, "应能掷出多档品质");

        // 独特品质不能随机生成——它固定名与属性，须逐件登记。
        Assert.Throws<ArgumentException>(() =>
            WeaponForge.Forge("木材", WeaponType.Sword, quality: Quality.Unique));

        // 子类型轴已预留（剑→刺剑/太刀 后续扩展）。
        var subtyped = WeaponForge.Forge("钢", WeaponType.Sword, subtype: "太刀",
            quality: Quality.Fine, enchant: "", blessed: false, enhance: 0);
        Assert.Equal("太刀", subtyped.Subtype);
        Assert.Equal("钢太刀", subtyped.Name);
        Assert.Contains("类型　剑·太刀", subtyped.DescribeDetails());

        // 实例进背包，按 Id 记 1 件。
        var master = state.Roster.Add("你", master: true);
        master.Bag.Add(grand.Id, 1);
        Assert.Equal(1, master.Bag.Get(grand.Id));

        // 解析器认实例：名字、品类、价值都按实例来。
        var info = Items.Info(registry, grand.Id);
        Assert.NotNull(info);
        Assert.True(info!.Value.IsWeaponInstance);
        Assert.Equal(grand.Name, info.Value.Label);
        Assert.Equal("Weapon", info.Value.Category);
        Assert.Equal(grand.Value, info.Value.MarketValue);
        Assert.NotNull(Items.Info(registry, "木材"));   // 普通物品仍走原路

        // 行情按实例字段算：同基座两实例价不同。
        Assert.NotNull(state.Territory.Listing(grand.Id));
        Assert.NotEqual(state.Territory.Listing(plain.Id)!.Value.SellPrice,
                        state.Territory.Listing(grand.Id)!.Value.SellPrice);

        // 存档往返不丢字段。
        var loaded = SaveSystem.Load(SaveSystem.Save(state));
        var w = loaded.Territory.Weapons.Get(grand.Id);
        Assert.NotNull(w);
        Assert.Equal(Quality.Legendary, w!.Quality);
        Assert.Equal(5, w.Enhance);
        Assert.Equal("Blazing", w.Enchant);
        Assert.True(w.Blessed);
        Assert.Equal(grand.Name, w.Name);
        Assert.Equal(grand.Value, w.Value);
        Assert.Equal(grand.Panel, w.Panel);
        Assert.Equal(1, loaded.Roster.Master!.Bag.Get(grand.Id));
    }

    [Fact]
    public void Weapon_instances_land_in_weapon_storage_by_category()
    {
        DefaultDefs.EnsureInitialized();
        var state = new GameState();
        state.Roster.Add("你", master: true);
        state.Territory.AddRoom(new Room { Id = 1, Name = "库房", Open = true });

        var rack = new Facility { Id = 1, Name = "武器架", RoomId = 1, Capacity = 10, CanStore = true, Built = true };
        rack.StorageFilter.Add("Weapon");
        state.Territory.AddFacility(rack);

        var weapon = WeaponForge.Forge("秘银", WeaponType.Spear, enchant: "", blessed: false, enhance: 0);
        state.Territory.Weapons.Add(weapon);

        // 实例归属武器大类，按品类收；材料不是兵器，仍被拒。
        Assert.True(rack.FilterAccepts(weapon.Id, state.Territory.Weapons));
        Assert.False(rack.FilterAccepts("木材", state.Territory.Weapons));
    }

    [Fact]
    public void Stamina_is_combat_hp_spirit_is_spent_by_work_and_tired_gets_half_stats_debuff()
    {
        DefaultDefs.EnsureInitialized();
        var state = new GameState();
        var c = state.Roster.Add("战士");
        c[CoreStat.Strength] = 20;
        c[CoreStat.Constitution] = 20;
        c[CoreStat.Dexterity] = 20;
        c[CoreStat.Intellect] = 20;
        c[CoreStat.Perception] = 20;

        // 1. 体力就是生命值，不是比例换算：生命值高了体力一样也多
        var initialMaxHp = c.Combat.MaxHp;
        Assert.Equal(initialMaxHp, c.Condition.MaxStamina);
        Assert.Equal(initialMaxHp, c.Condition.Stamina);

        // 进战斗生命值直接等于体力（1:1 无比例换算）
        var fullDeploy = Deploy.FromCharacter(c, CombatSide.Attacker);
        Assert.Equal(initialMaxHp, fullDeploy.Hp);
        Assert.Equal(initialMaxHp, fullDeploy.MaxHp);

        // 体力受损掉 25 点体力（剩余 200 点体力），进战斗直接就是 200 点血，绝无比例换算
        c.Condition.Spend(25, 0);
        Assert.Equal(initialMaxHp - 25, c.Condition.Stamina);
        var hurtDeploy = Deploy.FromCharacter(c, CombatSide.Attacker);
        Assert.Equal(initialMaxHp - 25, hurtDeploy.Hp);
        Assert.Equal(initialMaxHp, hurtDeploy.MaxHp);

        // 生命值高了体力一样也多：体质提升 10 点，生命值涨 100，体力上限随之一同涨 100
        c.Condition.Recover(25, 0);
        c[CoreStat.Constitution] += 10;
        Assert.Equal(initialMaxHp + 100, c.Combat.MaxHp);
        Assert.Equal(initialMaxHp + 100, c.Condition.MaxStamina);
        Assert.Equal(initialMaxHp, c.Condition.Stamina);

        // 2. 气力是被工作消耗的，两者没有强关联：工作消耗气力，体力（生命值）完全不掉
        Assert.Equal(1000, c.Condition.Spirit);
        var staminaBeforeWork = c.Condition.Stamina;

        // 采矿消耗气力（采掘重活消耗25点气力）
        state.Territory.AddRoom(new Room { Id = 1, Name = "矿场", Open = true });
        var mine = new Facility { Id = 1, Name = "矿脉", RoomId = 1, YieldItemId = "铁矿", Built = true, Actions = { ActionKind.Mine } };
        state.Territory.AddFacility(mine);
        state.Territory.Assign(c.Id, 0, SlotMode.Work, 1);

        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(c.Id, 1);
        state.Clock.SetTime(1, 0);

        // 干活推进，气力下降，体力（血量）依旧完好
        for (var i = 0; i < 20; i++)
            hub.PassTime(5);
        Assert.True(c.Condition.Spirit < 1000);
        Assert.Equal(staminaBeforeWork, c.Condition.Stamina); // 体力完全不受工作影响！

        // 3. 气力低于 30% 会进入疲劳状态
        c.Condition.Spend(0, 800);   // 气力降至 200 以下（< 30%）
        Assert.True(c.Condition.Spirit < c.Condition.MaxSpirit * 3 / 10);
        Assert.True(c.Condition.Tired);

        // 4. 疲劳的角色在战斗时受到全属性 -50% 的 debuff
        var normalCombat = c.Combat;
        var tiredDeploy = Deploy.FromCharacter(c, CombatSide.Attacker);
        Assert.Equal(normalCombat.Attack / 2, tiredDeploy.Attack);
        Assert.Equal(normalCombat.Defence / 2, tiredDeploy.Defence);
        Assert.Equal(normalCombat.Dodge / 2, tiredDeploy.Dodge);
        Assert.Equal(normalCombat.SpellPower / 2, tiredDeploy.SpellPower);

        // 5. 恢复气力回升到 30% 以上后疲劳解除，战斗全属性恢复正常
        c.Condition.Recover(0, 500);
        Assert.False(c.Condition.Tired);
        var recoveredDeploy = Deploy.FromCharacter(c, CombatSide.Attacker);
        Assert.Equal(normalCombat.Attack, recoveredDeploy.Attack);
        Assert.Equal(normalCombat.Defence, recoveredDeploy.Defence);
    }

    [Fact]
    public void Equip_slots_cover_weapons_armor_and_accessories()
    {
        DefaultDefs.EnsureInitialized();

        // 十槽：主副手 / 五件甲 / 两戒指一项链。
        Assert.Equal(10, EquipSlots.Count);
        Assert.True(EquipSlots.Accepts(EquipSlot.MainHand, EquipKind.Weapon));
        Assert.True(EquipSlots.Accepts(EquipSlot.OffHand, EquipKind.Weapon));
        Assert.False(EquipSlots.Accepts(EquipSlot.Head, EquipKind.Weapon));
        foreach (var slot in new[] { EquipSlot.Head, EquipSlot.Torso, EquipSlot.Legs, EquipSlot.Hands, EquipSlot.Feet })
            Assert.True(EquipSlots.Accepts(slot, EquipKind.Armor));
        Assert.False(EquipSlots.Accepts(EquipSlot.Neck, EquipKind.Armor));
        foreach (var slot in new[] { EquipSlot.Ring1, EquipSlot.Ring2, EquipSlot.Neck })
            Assert.True(EquipSlots.Accepts(slot, EquipKind.Accessory));
        Assert.False(EquipSlots.Accepts(EquipSlot.Torso, EquipKind.Accessory));

        // 防具：槽位定基座，材料与品质缩放。7 种能缝甲的材料 × 5 槽 = 35 基座。
        Assert.Equal(35, EquipForge.ArmorBaseCount());
        var steelTorso = EquipForge.ForgeArmor(EquipSlot.Torso, "钢",
            quality: Quality.Common, enchant: "", blessed: false, enhance: 0);
        var steelHead = EquipForge.ForgeArmor(EquipSlot.Head, "钢",
            quality: Quality.Common, enchant: "", blessed: false, enhance: 0);
        Assert.True(steelTorso.Defence > steelHead.Defence, "上装应厚于帽子");
        Assert.True(EquipForge.ForgeArmor(EquipSlot.Torso, "以太", quality: Quality.Common, enchant: "").Defence
                    > steelTorso.Defence, "材料应抬防御");
        Assert.True(EquipForge.ForgeArmor(EquipSlot.Torso, "钢", quality: Quality.Legendary, enchant: "").Defence
                    > steelTorso.Defence, "品质应抬防御");

        // 饰品：类型决定加哪项战斗属性。
        var ruby = EquipForge.ForgeAccessory(EquipSlot.Ring1, "Strength", "秘银",
            quality: Quality.Fine, enchant: "", blessed: false, enhance: 0);
        var gale = EquipForge.ForgeAccessory(EquipSlot.Ring1, "Dexterity", "秘银",
            quality: Quality.Fine, enchant: "", blessed: false, enhance: 0);
        var sage = EquipForge.ForgeAccessory(EquipSlot.Neck, "Intellect", "以太",
            quality: Quality.Epic, enchant: "", blessed: false, enhance: 0);
        Assert.Equal(CoreStat.Strength, ruby.BonusStat);
        Assert.Equal(CoreStat.Dexterity, gale.BonusStat);
        Assert.Equal(CoreStat.Intellect, sage.BonusStat);
        Assert.NotEqual(ruby.BonusStat, gale.BonusStat);
        Assert.True(EquipForge.ForgeAccessory(EquipSlot.Ring1, "Strength", "以太",
            quality: Quality.Common, enchant: "").BonusAmount > ruby.BonusAmount);

        // 名字只带祝福与附魔，其余进详情。
        // 防具用物品名词（甲/腿/靴），不是槽位标签（上装/下装/鞋子）。
        var plain = EquipForge.ForgeArmor(EquipSlot.Torso, "铁",
            quality: Quality.Legendary, enchant: "", blessed: false, enhance: 5);
        Assert.Equal("铁甲", plain.Name);
        Assert.Contains("强化　+5", plain.DescribeDetails());
        Assert.Equal("钢腿", EquipForge.ForgeArmor(EquipSlot.Legs, "钢",
            quality: Quality.Common, enchant: "", blessed: false, enhance: 0).Name);
        Assert.Equal("秘银靴", EquipForge.ForgeArmor(EquipSlot.Feet, "秘银",
            quality: Quality.Common, enchant: "", blessed: false, enhance: 0).Name);
        // 铁以下只有布甲皮甲：木头缝不了甲。
        Assert.Equal("布帽", EquipForge.ForgeArmor(EquipSlot.Head, "布",
            quality: Quality.Common, enchant: "", blessed: false, enhance: 0).Name);
        Assert.Throws<ArgumentException>(() =>
            EquipForge.ForgeArmor(EquipSlot.Head, "木材", quality: Quality.Common, enchant: ""));
        var blessedNeck = EquipForge.ForgeAccessory(EquipSlot.Neck, "Strength", "精金",
            quality: Quality.Common, enchant: "", blessed: true, enhance: 0);
        Assert.Equal("受祝福的精金项链", blessedNeck.Name);
        var keenRing = EquipForge.ForgeAccessory(EquipSlot.Ring1, "Perception", "钢",
            quality: Quality.Common, enchant: "Keen", blessed: false, enhance: 0);
        Assert.Equal("锐利的钢戒指", keenRing.Name);
        Assert.Contains("加成　感知 +15", keenRing.DescribeDetails());

        // 饰品名 = 材料 + 戒指/项链；加什么属性是类型的事，写在详情里。
        Assert.Equal("秘银戒指", EquipForge.ForgeAccessory(EquipSlot.Ring1, "Strength", "秘银",
            quality: Quality.Common, enchant: "", blessed: false, enhance: 0).Name);
        Assert.Equal("精金戒指", EquipForge.ForgeAccessory(EquipSlot.Ring2, "Dexterity", "精金",
            quality: Quality.Common, enchant: "", blessed: false, enhance: 0).Name);
        Assert.Equal("以太项链", EquipForge.ForgeAccessory(EquipSlot.Neck, "Constitution", "以太",
            quality: Quality.Common, enchant: "", blessed: false, enhance: 0).Name);
        Assert.Equal("铁项链", EquipForge.ForgeAccessory(EquipSlot.Neck, "Intellect", "铁",
            quality: Quality.Common, enchant: "", blessed: false, enhance: 0).Name);
        Assert.Equal("戒指", EquipSlots.AccessoryNoun(EquipSlot.Ring1));
        Assert.Equal("戒指", EquipSlots.AccessoryNoun(EquipSlot.Ring2));
        Assert.Equal("项链", EquipSlots.AccessoryNoun(EquipSlot.Neck));
    }

    [Fact]
    public void Equipping_gear_aggregates_defence_and_stat_bonuses()
    {
        DefaultDefs.EnsureInitialized();
        var state = new GameState();
        var registry = state.Territory.Equips;
        var hero = state.Roster.Add("勇者");

        var helmet = EquipForge.ForgeArmor(EquipSlot.Head, "钢",
            quality: Quality.Common, enchant: "", blessed: false, enhance: 0);
        var torso = EquipForge.ForgeArmor(EquipSlot.Torso, "钢",
            quality: Quality.Common, enchant: "", blessed: false, enhance: 0);
        var ringA = EquipForge.ForgeAccessory(EquipSlot.Ring1, "Strength", "秘银",
            quality: Quality.Fine, enchant: "", blessed: false, enhance: 0);
        var ringB = EquipForge.ForgeAccessory(EquipSlot.Ring2, "Strength", "精金",
            quality: Quality.Epic, enchant: "", blessed: false, enhance: 0);
        foreach (var g in new[] { helmet, torso, ringA, ringB })
            registry.Add(g);

        Assert.Equal(0, hero.TotalDefence(registry));
        Assert.True(hero.EquipGear(helmet));
        Assert.True(hero.EquipGear(torso));
        Assert.True(hero.EquipGear(ringA));
        Assert.True(hero.EquipGear(ringB));

        // 防御 = 各甲之和；两枚戒指的同属性加成累加。
        Assert.Equal(helmet.Defence + torso.Defence, hero.TotalDefence(registry));
        Assert.Equal(ringA.BonusAmount + ringB.BonusAmount,
            hero.StatBonus(registry, CoreStat.Strength));
        Assert.Equal(0, hero.StatBonus(registry, CoreStat.Dexterity));

        // 槽位占用与错配都被拒。
        Assert.False(hero.EquipGear(EquipForge.ForgeArmor(EquipSlot.Head, "铁",
            quality: Quality.Common, enchant: "")));
        Assert.Throws<ArgumentException>(() =>
            EquipForge.ForgeArmor(EquipSlot.Neck, "铁", quality: Quality.Common, enchant: ""));

        // 卸下后防御下降，腾出的槽可再装。
        Assert.Equal(helmet.Id, hero.UnequipGear(EquipSlot.Head));
        Assert.Equal(torso.Defence, hero.TotalDefence(registry));
        Assert.True(hero.EquipGear(EquipForge.ForgeArmor(EquipSlot.Head, "铁",
            quality: Quality.Common, enchant: "")));

        // 存档往返：实例与槽位都不丢。
        var loaded = SaveSystem.Load(SaveSystem.Save(state));
        var h2 = loaded.Roster.Find(hero.Id)!;
        Assert.Equal(hero.TotalDefence(registry), h2.TotalDefence(loaded.Territory.Equips));
        Assert.Equal(hero.StatBonus(registry, CoreStat.Strength),
            h2.StatBonus(loaded.Territory.Equips, CoreStat.Strength));
    }

    private sealed class FixedDayEvent : IDayEvent
    {
        private readonly string _line;
        public FixedDayEvent(string line) => _line = line;
        public string? Run(GameState state, DaySummary summary) => _line;
    }

    private sealed class ProbeSession : Rimisekai.Session.Session
    {
        public int Spent;
        public ProbeSession(Roster roster) : base(roster) { }
        public override SessionKind Kind => SessionKind.Hub;
        protected override CommandContext Context => CommandContext.Daily;
        protected override void OnSpend(int minutes) => Spent += minutes;
    }

    private sealed class NoopEffect : ICommandEffect
    {
        public string EffectId => "noop";
        public CommandResult Apply(CommandRequest request, ICommandHost host) => CommandResult.Executed;
    }
}
