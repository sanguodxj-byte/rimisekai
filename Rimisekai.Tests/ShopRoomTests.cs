using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Character;
using Rimisekai.Defs;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Save;
using Xunit;
using Xunit.Abstractions;

namespace Rimisekai.Tests;

/// <summary>
/// 领地里自己开的店（营业性房间）与上门的访客：开店才来人、从最外沿的露天房进门、走进店、
/// 有人当班才买（主人站柜台也算）、买完就走、来过的人记在访客名册里会再来、好感够了能请他入伙。
/// </summary>
[Collection("Quest definition state")]
public sealed class ShopRoomTests
{
    private const int GroceryDef = 146; // 杂货铺：室内、营业性，建成自带一个摊位
    private const int MarketDef = 123;  // 市场：室外、营业性，建成自带一个摊位

    private readonly ITestOutputHelper _out;

    public ShopRoomTests(ITestOutputHelper output) => _out = output;

    /// <summary>开拓 (x, y) 的空格、装一间杂货铺，摊位里摆上货。返回店。</summary>
    private static Room OpenShop(HubSession hub, GameState state, int x = 2, int y = 3)
    {
        state.Money += 5000;
        state.Roster.Master!.Bag.Add("木材", 40);
        Assert.True(hub.DevelopVacantCell(0, x, y));
        Assert.True(hub.BuildRoomDef(GroceryDef, state.Territory.RoomAt(0, x, y)!.Id));
        var shop = state.Territory.RoomAt(0, x, y)!;
        Stall(state, shop).Contents.Add("布", 40);
        Stall(state, shop).Contents.Add("陶罐", 40);
        return shop;
    }

    private static Facility Stall(GameState state, Room shop) =>
        state.Territory.Facilities.Single(f => f.RoomId == shop.Id && f.Supports(ActionKind.Trade));

    /// <summary>女仆白天守摊（6–18 点两段；有床睡、包里揣着干粮，心情不至于垮到撂挑子）。</summary>
    private static void StaffWithMaid(HubSession hub, GameState state, Room shop)
    {
        TerritoryLoopTests.GiveMaidABed(hub, state);
        var maid = TerritoryLoopTests.Maid(state);
        maid.Bag.Add("干粮", 20);
        foreach (var slot in new[] { 1, 2 })
            Assert.True(hub.Assign(maid.Id, slot, SlotMode.Work, Stall(state, shop).Id));
    }

    [Fact]
    public void Commercial_rooms_are_tagged_and_carry_levels_from_the_table()
    {
        ContentDefs.EnsureInitialized();
        foreach (var id in new[] { GroceryDef, MarketDef })
        {
            var def = DefDatabase<RoomDef>.GetById(id) as RoomDef;
            Assert.NotNull(def);
            Assert.Contains(Territory.CommercialTag, def!.Tags);
            Assert.NotEmpty(def.SalesLevels);
            Assert.Equal(0, def.SalesLevels[0]);
            Assert.Equal(def.SalesLevels.Count, def.VisitorChance.Count);
            Assert.True(def.SalesLevels.SequenceEqual(def.SalesLevels.OrderBy(n => n)), "门槛递增");
            Assert.True(def.VisitorChance.SequenceEqual(def.VisitorChance.OrderBy(n => n)), "等级越高来人越多");
            var bundled = DefDatabase<FacilityDef>.GetNamed(def.BundledFacility).ToRuntime();
            Assert.True(bundled.Supports(ActionKind.Trade) && bundled.CanStore, "自带的摊位能招呼生意、能摆货");
        }
        // 只有这两间是营业性的
        Assert.Equal(new[] { MarketDef, GroceryDef }.OrderBy(i => i),
            ContentDefs.BuildingRooms.Where(r => r.Tags.Contains(Territory.CommercialTag)).Select(r => r.Id).OrderBy(i => i));

        var hub = TerritoryLoopTests.NewGame(out var state, 3);
        var shop = OpenShop(hub, state);
        Assert.True(shop.Commercial && shop.HasTag(Territory.IndoorTag));
        Assert.Equal(1, shop.ShopLevel);
        Assert.Equal(25, shop.VisitorPercent);
        // 读档后等级、累计照旧
        shop.Sales = 25;
        var loaded = SaveSystem.Load(SaveSystem.Save(state, hub));
        var again = loaded.Territory.Room(shop.Id)!;
        Assert.Equal(25, again.Sales);
        Assert.Equal(2, again.ShopLevel);
        Assert.Equal(35, again.VisitorPercent);
    }

    [Fact]
    public void Sales_raise_the_shop_level_and_level_raises_the_visitor_rate()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 3);
        var shop = OpenShop(hub, state);
        var seller = TerritoryLoopTests.Maid(state);
        var rates = new List<int>();
        var levels = new List<int>();
        var rng = new Random(5);
        var ups = 0;
        while (shop.Sales < shop.SalesLevels[^1])
        {
            Stall(state, shop).Contents.Add("布", 5);
            var sale = Commerce.Sell(state.Territory, shop, seller, 999, rng)!.Value;
            if (sale.LevelUp)
            {
                ups++;
                levels.Add(shop.ShopLevel);
                rates.Add(shop.VisitorPercent);
            }
        }
        Assert.Equal(shop.SalesLevels.Count - 1, ups);
        Assert.Equal(new[] { 2, 3, 4, 5 }, levels);
        Assert.Equal(new[] { 35, 45, 55, 70 }, rates);
    }

    [Fact]
    public void No_commercial_room_no_visitors()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 3);
        Assert.False(Commerce.HasShop(state.Territory));
        hub.PassTime(3 * 24 * 60);
        Assert.Empty(state.Territory.Visits);
        Assert.Empty(state.Roster.Visitors);
    }

    [Fact]
    public void Visitors_enter_from_the_edge_walk_to_the_shop_buy_and_leave_the_same_way()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 3);
        var shop = OpenShop(hub, state);
        StaffWithMaid(hub, state, shop);
        var entry = Commerce.EntryFor(state.Territory, shop, TerritoryLoopTests.Maid(state))!;
        Assert.False(entry.HasTag(Territory.IndoorTag));
        _out.WriteLine($"入口：{entry.Name}（{entry.X},{entry.Y}），店：{shop.Name}（{shop.X},{shop.Y}）");

        var seen = new Dictionary<int, List<int>>();
        var finished = new List<List<int>>();
        var members = state.Roster.Members.Count;
        var money = state.Money;
        // 一格一格推（人一格走一间房），每间都看得见。
        for (var t = 0; t < 5 * 24 * 60 / TerritoryClock.StepMinutes; t++)
        {
            hub.PassTime(TerritoryClock.StepMinutes);
            foreach (var v in state.Territory.Visits)
            {
                if (!seen.TryGetValue(v.CharacterId, out var trail))
                    seen[v.CharacterId] = trail = new List<int>();
                if (trail.Count == 0 || trail[^1] != v.RoomId)
                    trail.Add(v.RoomId);
                Assert.Equal(entry.Id, v.EntryRoomId);
                Assert.DoesNotContain(hub.Party(), c => c.Id == v.CharacterId);
            }
            foreach (var id in seen.Keys.ToList())
            {
                if (!state.Territory.Visits.Exists(v => v.CharacterId == id))
                {
                    finished.Add(seen[id]);
                    seen.Remove(id);
                }
            }
        }
        Assert.NotEmpty(finished);
        _out.WriteLine(string.Join("  ", finished.Select(trail => string.Join("→", trail))));
        foreach (var trail in finished)
        {
            // 进门那间进、店里买、原路出
            Assert.Equal(entry.Id, trail[0]);
            Assert.Contains(shop.Id, trail);
            Assert.Equal(entry.Id, trail[^1]);
        }
        Assert.True(shop.Sales > 0, "女仆守摊卖出了货");
        Assert.True(state.Money > money, "卖货的钱进了账");
        // 访客不是住户：名册人数不变，没有自主行动的身子
        Assert.Equal(members, state.Roster.Members.Count);
        Assert.NotEmpty(state.Roster.Visitors);
        Assert.All(state.Roster.Visitors, v => Assert.DoesNotContain(hub.Day.Workers, w => w.CharacterId == v.Id));
        _out.WriteLine($"5 天来客 {finished.Count} 趟，认识 {state.Roster.Visitors.Count} 人，卖出 {shop.Sales} 件，进账 {state.Money - money}G");
        // 来过的人会再来：趟数多于认识的人数
        Assert.True(finished.Count > state.Roster.Visitors.Count, "有回头客");
    }

    /// <summary>看店是一轮一轮的：干满一轮回决策，饭点照样去吃饭，不会从早到晚钉在摊位上。</summary>
    [Fact]
    public void The_stall_keeper_still_goes_to_meals()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 3);
        var shop = OpenShop(hub, state);
        StaffWithMaid(hub, state, shop);
        var maid = TerritoryLoopTests.Maid(state);
        hub.PassTime(24 * 60);
        var day = state.Clock.Day;
        var goals = new HashSet<ActionKind>();
        while (state.Clock.Day == day && state.Clock.Minutes < 18 * 60)
        {
            hub.PassTime(TerritoryClock.StepMinutes);
            goals.Add(hub.Day.Workers.First(w => w.CharacterId == maid.Id).Goal);
        }
        Assert.Contains(ActionKind.Trade, goals);
        Assert.Contains(ActionKind.Meal, goals);
        Assert.Equal(day, maid.Affect.LastMealDay);
    }

    [Fact]
    public void Nobody_on_duty_nobody_buys()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 3);
        var shop = OpenShop(hub, state);
        var money = state.Money;
        hub.PassTime(2 * 24 * 60);
        Assert.NotEmpty(state.Roster.Visitors);
        Assert.Equal(0, shop.Sales);
        Assert.Equal(money, state.Money);
    }

    [Fact]
    public void Visitors_respect_door_locks()
    {
        // 店开在主人卧室西边，唯一的路穿过卧室：主人把卧室锁上，访客就进不来。
        var hub = TerritoryLoopTests.NewGame(out var state, 3);
        var shop = OpenShop(hub, state, 1, 0);
        var bedroom = state.Territory.Room(state.Territory.MasterBedroomId)!;
        Assert.Contains(bedroom.Id, shop.Links);
        Assert.True(shop.Links.All(id => id == bedroom.Id), "店只通卧室");
        var stranger = new CharacterGenerator(new Random(1)).Roll(state.Roster).State;
        bedroom.Lock = RoomLock.Locked;
        Assert.Null(Commerce.EntryFor(state.Territory, shop, stranger));
        Assert.Null(Commerce.Spawn(state.Territory, state.Roster, shop, new Random(1)));
        bedroom.Lock = RoomLock.Unlocked;
        var visit = Commerce.Spawn(state.Territory, state.Roster, shop, new Random(1));
        Assert.NotNull(visit);
        Assert.Contains(bedroom.Id, visit!.Path);
        // 半路上卧室又锁了：走不通就作罢离开，不卡在门口
        bedroom.Lock = RoomLock.Locked;
        for (var i = 0; i < 6 && state.Territory.Visits.Count > 0; i++)
            hub.PassTime(10);
        Assert.Empty(state.Territory.Visits);
        Assert.NotEqual(shop.Id, visit.RoomId);
    }

    [Fact]
    public void The_player_can_take_a_shift_at_the_stall()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 3);
        var shop = OpenShop(hub, state);
        var master = state.Roster.Master!;
        var rng = new Random(2);
        var money = state.Money;
        var served = 0;
        for (var round = 0; round < 10 && shop.Sales == 0; round++)
        {
            var visit = Commerce.Spawn(state.Territory, state.Roster, shop, rng)!;
            TerritoryLoopTests.MoveTo(hub, shop.Id);
            Assert.True(hub.Use(Stall(state, shop).Id));
            Assert.True(hub.ActAtFixture(ActionKind.Trade));
            Assert.NotEqual(VisitPhase.Shopping, visit.Phase);
            served++;
        }
        Assert.True(shop.Sales > 0, "主人站柜台卖出了货");
        Assert.True(state.Money > money);
        Assert.True(master.Life(LifeSkill.Social) >= 0);
        _out.WriteLine($"主人招呼 {served} 位客人卖出 {shop.Sales} 件");
    }

    [Fact]
    public void Charm_and_social_skill_scale_the_deal()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 3);
        var shop = OpenShop(hub, state);
        var plain = new CharacterState(901) { Name = "木讷" };
        var smooth = new CharacterState(902) { Name = "能说" };
        plain[CoreStat.Charm] = 4;
        smooth[CoreStat.Charm] = 16;
        smooth.GainLifeExp(LifeSkill.Social, 5000);
        var low = Commerce.SellerPercent(plain);
        var high = Commerce.SellerPercent(smooth);
        Assert.Equal(100 + 2 * (4 + plain.Life(LifeSkill.Social)), low);
        Assert.Equal(Commerce.MaxSellerPercent, high);
        Assert.True(Commerce.SuccessPercent(high) > Commerce.SuccessPercent(low));
        Assert.True(Commerce.UnitPrice(state.Territory, "布", high) > Commerce.UnitPrice(state.Territory, "布", low));
        Assert.Equal(Items.Get("布")!.MarketValue * low / 100, Commerce.UnitPrice(state.Territory, "布", low));

        long Earn(CharacterState seller)
        {
            var rng = new Random(9);
            long sum = 0;
            for (var i = 0; i < 300; i++)
            {
                Stall(state, shop).Contents.Add("布", 10);
                sum += Commerce.Sell(state.Territory, shop, seller, 999, rng)!.Value.Income;
            }
            return sum;
        }
        var lowIncome = Earn(plain);
        var highIncome = Earn(smooth);
        _out.WriteLine($"本领 {low}% → 300 位客人 {lowIncome}G；本领 {high}% → {highIncome}G");
        Assert.True(highIncome > lowIncome * 3 / 2);
    }

    [Fact]
    public void Empty_shelves_sell_nothing()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 3);
        var shop = OpenShop(hub, state);
        Stall(state, shop).Contents.Clear();
        Assert.Null(Commerce.Sell(state.Territory, shop, TerritoryLoopTests.Maid(state), 999, new Random(1)));
    }

    [Fact]
    public void Haulers_never_stock_the_shop_shelves()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 3);
        var shop = OpenShop(hub, state);
        Stall(state, shop).Contents.Clear();
        var maid = TerritoryLoopTests.Maid(state);
        maid.Bag.Add("干粮", 10);
        for (var slot = 0; slot < 4; slot++)
            Assert.True(hub.Assign(maid.Id, slot, SlotMode.Work, 11));
        hub.PassTime(2 * 24 * 60);
        Assert.True(TerritoryLoopTests.Total(state, "木材") > 0);
        Assert.Empty(Stall(state, shop).Contents.Items.Where(kv => kv.Value > 0));
        Assert.NotEqual(shop.Id, state.Territory.FindStorageFor("木材", shop.Id)?.RoomId ?? -1);
    }

    [Fact]
    public void Visitors_and_their_trips_survive_save_and_load()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 3);
        var shop = OpenShop(hub, state);
        var visit = Commerce.Spawn(state.Territory, state.Roster, shop, new Random(4))!;
        hub.PassTime(10);
        var who = state.Roster.Visitor(visit.CharacterId)!;
        var loaded = SaveSystem.Load(SaveSystem.Save(state, hub));
        Assert.Equal(state.Roster.Visitors.Select(v => (v.Id, v.Name, v.Identity)), loaded.Roster.Visitors.Select(v => (v.Id, v.Name, v.Identity)));
        var back = loaded.Territory.Visits.Single();
        Assert.Equal((visit.CharacterId, visit.RoomId, visit.ShopRoomId, visit.EntryRoomId, visit.Phase), (back.CharacterId, back.RoomId, back.ShopRoomId, back.EntryRoomId, back.Phase));
        Assert.Equal(visit.Path, back.Path);
        Assert.Null(loaded.Roster.Find(who.Id));
        // 读档后新来的人不会和访客撞号
        Assert.True(loaded.Roster.ReserveId() > who.Id);
    }

    [Fact]
    public void A_fond_visitor_accepts_an_invitation_to_join()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 3);
        var shop = OpenShop(hub, state);
        var visit = Commerce.Spawn(state.Territory, state.Roster, shop, new Random(4))!;
        var who = state.Roster.Visitor(visit.CharacterId)!;
        // 访客走进店里，主人在店里等他
        TerritoryLoopTests.MoveTo(hub, shop.Id);
        for (var i = 0; i < 6 && visit.RoomId != shop.Id; i++)
            hub.PassTime(10);
        Assert.Equal(shop.Id, visit.RoomId);
        Assert.Contains(hub.CardsHere(), c => c.Id == who.Id);
        Assert.True(hub.Select(who.Id));
        // 好感不够：婉拒，仍是访客
        while (who.Condition.Favor >= 100)
            who.Condition.AddFavor(-10);
        Assert.True(hub.Social(SocialAction.Invite));
        Assert.NotNull(state.Roster.Visitor(who.Id));
        Assert.Null(state.Roster.Find(who.Id));
        // 聊得来（好感档）：答应入伙，成了住户，能排班
        if (state.Territory.Visits.All(v => v.CharacterId != who.Id))
        {
            visit = Commerce.Spawn(state.Territory, state.Roster, shop, new Random(4))!;
            Assert.Equal(who.Id, visit.CharacterId);
            for (var i = 0; i < 6 && visit.RoomId != shop.Id; i++)
                hub.PassTime(10);
        }
        who.Condition.AddFavor(150);
        Assert.Equal(Bond.Fond, who.Condition.Bond);
        Assert.True(hub.Select(who.Id));
        Assert.True(hub.Social(SocialAction.Invite));
        Assert.Null(state.Roster.Visitor(who.Id));
        Assert.Same(who, state.Roster.Find(who.Id));
        Assert.DoesNotContain(state.Territory.Visits, v => v.CharacterId == who.Id);
        Assert.True(hub.Assign(who.Id, 0, SlotMode.Work, Stall(state, shop).Id));
        hub.PassTime(60);
        Assert.Contains(hub.Day.Workers, w => w.CharacterId == who.Id);
        Assert.Contains(hub.Party(), c => c.Id == who.Id);
    }

    [Fact]
    public void Talking_to_a_visitor_builds_favor()
    {
        var hub = TerritoryLoopTests.NewGame(out var state, 3);
        var shop = OpenShop(hub, state);
        var visit = Commerce.Spawn(state.Territory, state.Roster, shop, new Random(4))!;
        var who = state.Roster.Visitor(visit.CharacterId)!;
        TerritoryLoopTests.MoveTo(hub, shop.Id);
        for (var i = 0; i < 6 && visit.RoomId != shop.Id; i++)
            hub.PassTime(10);
        var favor = who.Condition.Favor;
        Assert.True(hub.Select(who.Id));
        Assert.True(hub.Social(SocialAction.Talk));
        Assert.True(who.Condition.Favor > favor);
    }
}
