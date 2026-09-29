using System.Collections.Generic;
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
        t.Assign(a.Id, 0, SlotMode.Work);
        t.SetPriority(a.Id, ActionKind.Till, 1);
        t.Assign(b.Id, 0, SlotMode.Work);
        t.SetPriority(b.Id, ActionKind.Till, 1);

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
        t.Assign(a.Id, 2, SlotMode.Work);
        t.SetPriority(a.Id, ActionKind.Forge, 1);

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
        c.Threat = 3;

        var sheet = c.Combat;
        Assert.Equal(16, sheet.Attack);
        Assert.Equal(125, sheet.MaxHp);
        Assert.Equal(16, sheet.Defence);
        Assert.Equal(10, sheet.Dodge);
        Assert.Equal(8, sheet.SpellPower);
        Assert.Equal(3, sheet.Threat);
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
        Assert.Equal(10 + 9 * 5 + 10 * 3, c.MaxMana);
        Assert.Equal(c.MaxMana, c.Condition.MaxMana);

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
        Assert.Equal(1, hub.State.Roster.Master!.Bag.Get("布"));
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
        t.Assign(a.Id, 0, SlotMode.Work);
        t.SetPriority(a.Id, ActionKind.Mine, 1);

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
        t.Assign(lazy.Id, 0, SlotMode.Work);
        t.SetPriority(lazy.Id, ActionKind.Mine, 1);
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
        Assert.Equal(Vitals.DefaultMax, c.Condition.Stamina);
        Assert.Equal(Bond.None, c.Condition.Bond);

        c.Condition.Spend(200, 100, 5);
        Assert.Equal(800, c.Condition.Stamina);
        Assert.Equal(900, c.Condition.Spirit);
        Assert.Equal(5, c.Condition.Fatigue);
        Assert.False(c.Condition.Tired);

        c.Condition.Spend(0, 0, Vitals.TiredAt);
        Assert.True(c.Condition.Tired);

        c.Condition.Recover(50, 50, clearFatigue: true);
        Assert.Equal(850, c.Condition.Stamina);
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
        worker.Condition.Spend(0, 0, Vitals.TiredAt);
        var t = new Territory();
        t.AddRoom(new Room { Id = 1, Open = true });
        t.AddFacility(new Facility { Id = 1, RoomId = 1, Usage = FacilityUsage.Plain, Actions = { ActionKind.Till }, Built = true, YieldItemId = "herb" });
        t.Assign(worker.Id, 0, SlotMode.Work);
        t.SetPriority(worker.Id, ActionKind.Till, 1);
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
            state.Territory.Assign(worker.Id, slot, SlotMode.Work);
        state.Territory.SetPriority(worker.Id, ActionKind.Mine, 1);
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(worker.Id, 1);

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
        state.Roster.Add("你", master: true);
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        state.Territory.AddOffer(new MarketOffer { ItemId = "布", BuyPrice = 10, SellPrice = 6 });
        state.Territory.AddGuest(new Guest { Id = 1, Name = "行商", RoomId = 1, Purpose = "卖货" });
        var hub = new HubSession(state);
        hub.Enter(1);

        Assert.True(hub.MarketTrade("布", 2, selling: false));
        Assert.Equal(80, state.Money);
        Assert.Equal(2, state.Roster.Master!.Bag.Get("布"));
        Assert.True(hub.MarketTrade("布", 1, selling: true));
        Assert.Equal(86, state.Money);
        Assert.Single(hub.GuestsHere());
    }

    [Fact]
    public void BuildFacility_costs_money_and_station_time()
    {
        var state = new GameState { Money = 100 };
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
        state.Clock.Advance(7 * 60);
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
        state.Roster.Master!.Bag.Add("花", 3);
        state.Territory.Assign(friend.Id, 2, SlotMode.Work);
        state.Territory.SetPriority(friend.Id, ActionKind.Till, 2);
        state.Territory.AddOffer(new MarketOffer { ItemId = "布", BuyPrice = 10, SellPrice = 6 });
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
        Assert.Equal(SlotMode.Work, loaded.Territory.ScheduleOf(friend.Id).Slots[2]);
        Assert.Equal(2, loaded.Territory.PriorityOf(friend.Id, ActionKind.Till));
        Assert.Single(loaded.Territory.Market);
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
        Assert.Contains(loadedHub.Log, l => l.Text == "测试日志");
    }

    [Fact]
    public void EndDay_recovers_rolls_weather_and_reports_season()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var worker = state.Roster.Add("工");
        worker.Condition.Spend(300, 200, 100);
        var summary = state.EndDay(new System.Random(1));
        Assert.Equal(2, state.Clock.Day);
        Assert.Equal(Vitals.DefaultMax, worker.Condition.Stamina);
        Assert.Equal(0, worker.Condition.Fatigue);
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
        Assert.Equal(9, AttributeMap.LifeCount);
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
        Assert.Contains(hub.Log, l => l.Text.StartsWith("今日天气："));
    }

    [Fact]
    public void Assign_sets_a_slot_and_rejects_master_and_bad_slot()
    {
        var state = new GameState();
        var master = state.Roster.Add("你", master: true);
        var worker = state.Roster.Add("工");
        var hub = new HubSession(state);

        Assert.True(hub.Assign(worker.Id, 1, SlotMode.Work));
        Assert.Equal(SlotMode.Work, state.Territory.ScheduleOf(worker.Id).Slots[1]);
        // 别的时段不受影响，改的只是点中的那一段。
        Assert.Equal(SlotMode.Free, state.Territory.ScheduleOf(worker.Id).Slots[0]);

        // 主角不排时段；越界时段与不存在的人也拒绝。
        Assert.False(hub.Assign(master.Id, 0, SlotMode.Work));
        Assert.False(hub.Assign(worker.Id, WorkSlot.Count, SlotMode.Work));
        Assert.False(hub.Assign(worker.Id, -1, SlotMode.Work));
        Assert.False(hub.Assign(9999, 0, SlotMode.Work));
    }

    [Fact]
    public void SetPriority_orders_work_and_rejects_master_and_non_productive()
    {
        var state = new GameState();
        var master = state.Roster.Add("你", master: true);
        var worker = state.Roster.Add("工");
        var hub = new HubSession(state);

        Assert.True(hub.SetPriority(worker.Id, ActionKind.Mine, 3));
        Assert.Equal(3, hub.PriorityOf(worker.Id, ActionKind.Mine));
        // 收进 1-4 档：0 与负数表示不做。
        Assert.True(hub.SetPriority(worker.Id, ActionKind.Till, 9));
        Assert.Equal(ActionKindMap.MaxPriority, hub.PriorityOf(worker.Id, ActionKind.Till));
        Assert.True(hub.SetPriority(worker.Id, ActionKind.Till, 0));
        Assert.Equal(0, hub.PriorityOf(worker.Id, ActionKind.Till));

        // 主角不设优先级；None（不干活）不是真实行动，不能进优先级表。
        Assert.False(hub.SetPriority(master.Id, ActionKind.Mine, 1));
        Assert.False(hub.SetPriority(worker.Id, ActionKind.None, 1));
        Assert.False(hub.SetPriority(9999, ActionKind.Mine, 1));
    }

    [Fact]
    public void TasksByPriority_sorts_by_priority_then_column_order()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var worker = state.Roster.Add("工");
        var t = state.Territory;

        // 锻造=1、耕作=1（同档）、采矿=2。
        t.SetPriority(worker.Id, ActionKind.Forge, 1);
        t.SetPriority(worker.Id, ActionKind.Till, 1);
        t.SetPriority(worker.Id, ActionKind.Mine, 2);

        var order = t.TasksByPriority(worker.Id);
        // 同档按行动行序（Forge 在 Till 之前），档位低的排最后。
        Assert.Equal(new[] { ActionKind.Forge, ActionKind.Till, ActionKind.Mine }, order);
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
        state.Territory.AddFood("bread");
        state.Clock.Advance(12 * 60);
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
        state.Clock.Advance(23 * 60);
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
                state.Territory.Assign(worker.Id, slot, SlotMode.Work);
            state.Territory.SetPriority(worker.Id, ActionKind.Mine, 1);
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
        Assert.Empty(refuseLogs);
        Assert.NotEqual(WorkPhase.Working, refuseTracked.Phase);
        Assert.Equal(-1, refuseTracked.FacilityId);
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
        state.Clock.Advance(23 * 60);
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
        state.Territory.AddFood("bread");
        state.Clock.Advance(14 * 60);
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
        state.Clock.Advance(14 * 60);
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
        w.Bag.Add("矿石", 5);   // 背包里有货，库房有货架

        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(w.Id, 1);
        for (var i = 0; i < 20; i++)
            hub.PassTime(5);

        // 角色自己把货搬进了货架，背包清空。
        Assert.Equal(5, shelf.Contents.Get("矿石"));
        Assert.Equal(0, w.Bag.Get("矿石"));
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
        shelf.Contents.Add("矿石", 4);   // 料在库房
        state.Territory.AddFacility(shelf);
        state.Territory.AddFacility(new Facility { Id = 2, Name = "铁砧", RoomId = 2, Usage = FacilityUsage.Plain, Actions = { ActionKind.Forge }, Built = true });
        state.Territory.AddRecipe(new Recipe
        {
            ItemId = "铁锭", Station = ActionKind.Forge, OutputCount = 1,
            Costs = { new RecipeCost("矿石", 2) },
        });
        for (var slot = 0; slot < WorkSlot.Count; slot++)
            state.Territory.Assign(w.Id, slot, SlotMode.Work);
        state.Territory.SetPriority(w.Id, ActionKind.Forge, 1);

        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(w.Id, 1);
        for (var i = 0; i < 60; i++)
            hub.PassTime(5);

        // 角色自己去库房把料搬到台子上，才开得了工。
        Assert.True(w.Bag.Get("铁锭") > 0);
        // 备料按需搬（每次只搬够做一份的量），不搬空库房。
        Assert.True(shelf.Contents.Get("矿石") < 4);
    }

    [Fact]
    public void Storage_filter_and_capacity_are_enforced()
    {
        var state = new GameState();
        var w = state.Roster.Add("工");
        var shelf = new Facility { Id = 1, Name = "货架", RoomId = 1, CanStore = true, Built = true };
        shelf.StorageFilter.Add("矿石");          // 只收矿石
        shelf.StorageCapacity = 2;                 // 最多两件
        state.Territory.AddRoom(new Room { Id = 1, Name = "库房", Open = true });
        state.Territory.AddFacility(shelf);
        w.Bag.Add("矿石", 10);
        w.Bag.Add("木材", 10);

        // 过滤：木材不收。
        Assert.Equal(0, state.Territory.StoreFrom(w, shelf, "木材", 5));
        // 容量：矿石最多进 2 件。
        Assert.Equal(2, state.Territory.StoreFrom(w, shelf, "矿石", 5));
        Assert.Equal(2, shelf.Contents.Get("矿石"));
        Assert.Equal(8, w.Bag.Get("矿石"));
        // 满了就再也进不去。
        Assert.Equal(0, state.Territory.StoreFrom(w, shelf, "矿石", 1));
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
            state.Territory.Assign(worker.Id, slot, SlotMode.Work);
        state.Territory.SetPriority(worker.Id, ActionKind.Mine, 1);

        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(worker.Id, 1);
        hub.PassTime(60);

        // 产出先进产出者背包（不是某个“领地库存”）。
        Assert.True(worker.Bag.Get("ore") > 0);
        // 据点总数 = 背包 + 各设施存货，二者之外没有别处。
        Assert.Equal(worker.Bag.Get("ore"), state.Territory.CountWith(worker, "ore"));
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
            state.Clock.Advance(23 * 60);   // 深夜，本该睡
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
        state.Clock.Advance(12 * 60);
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
        state.Clock.Advance(12 * 60);
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
        state.Clock.Advance(14 * 60);
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
        state.Clock.Advance(14 * 60);
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
        Assert.Equal(10 + 5 * 5 + 2 * 3, c.MaxMana);
        Assert.Equal(c.MaxMana, c.Condition.MaxMana);
        Assert.Equal(20 + 11 * 10 + 2 * 5, c.Combat.MaxHp);

        c.GainLifeExp(LifeSkill.Craft, 100000);
        Assert.Equal(100, c.Level);
        Assert.Equal(10 + 99, c[CoreStat.Strength]);

        c.Condition.RecoverFull();
        var full = c.Condition.Mana;
        c.Condition.SpendMana(full + 10);
        Assert.Equal(0, c.Condition.Mana);
        c.Condition.RecoverMana(7);
        Assert.Equal(7, c.Condition.Mana);
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
        state.Catalog.Rooms[roomDef.Id] = roomDef;
        state.Catalog.Facilities[facilityDef.Id] = facilityDef;
        Assert.True(state.Territory.AddRoom(roomDef.ToRuntime()));
        Assert.True(state.Territory.AddFacility(facilityDef.ToRuntime()));
        Assert.Equal("做饭", state.Catalog.Rooms[1].Description);
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
        state.Territory.AddFood("bread");
        state.Clock.Advance(12 * 60);
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
        
        state.Clock.Advance(14 * 60);
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
        var roomDef = new RoomDef { Id = 101, Name = "菜园", Buildable = true };
        roomDef.MaterialCost.Add(new RecipeCost("木材", 3));
        var facilityDef = new FacilityDef
        {
            Id = 1001, Name = "菜地", Usage = FacilityUsage.Plain, Actions = { ActionKind.Till },
            Capacity = 2, YieldItemId = "小麦", Buildable = true,
        };
        facilityDef.MaterialCost.Add(new RecipeCost("木材", 1));
        state.Catalog.Rooms[101] = roomDef;
        state.Catalog.Facilities[1001] = facilityDef;
        state.Roster.Master!.Bag.Add("木材", 4);
        var hub = new HubSession(state);
        hub.Enter(1);

        Assert.False(hub.BuildRoomDef(999, 3, 3));
        Assert.True(hub.BuildRoomDef(101, 3, 3));
        Assert.Equal(1, state.Roster.Master!.Bag.Get("木材"));
        var added = state.Territory.RoomAt(3, 3);
        Assert.NotNull(added);
        Assert.True(added.Open);
        Assert.True(added.Buildable);

        Assert.True(hub.BuildFacilityDef(1001, added.Id));
        Assert.Equal(0, state.Roster.Master!.Bag.Get("木材"));
        Assert.True(state.Territory.Facilities[0].Supports(ActionKind.Till));
        Assert.Equal("小麦", state.Territory.Facilities[0].YieldItemId);
        Assert.False(hub.BuildFacilityDef(1001, added.Id));
    }

    [Fact]
    public void Selection_and_rest_bind_character_and_fixture()
    {
        var state = new GameState();
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
            state.Territory.Assign(friend.Id, slot, SlotMode.Work);
            state.Territory.Assign(stranger.Id, slot, SlotMode.Work);
        }
        state.Territory.SetPriority(friend.Id, ActionKind.Mine, 1);
        state.Territory.SetPriority(stranger.Id, ActionKind.Mine, 1);
        state.Clock.Advance(14 * 60);
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

        state.Clock.Advance(23 * 60);
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

        var wood = DefDatabase<ThingDef>.Get("木材");
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
        // 验证 RoomDef 与 FacilityDef 作为统一 Def 注册进 DefDatabase，且具备标签与设施解耦引用
        var roomDef = new RoomDef
        {
            DefName = "Room_Bakery",
            Id = 143,
            Label = "面包房",
            Tags = new List<string> { "室内", "工作间" },
            FacilityDefs = new List<string> { "Facility_Oven_1043" },
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
