using System.Linq;
using Rimisekai.Character;
using Rimisekai.Clock;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Save;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 领地跟随系统：邀请同行、跟着移动、单人设施等待、
/// 跟随者的交易还价、同床的好感判定。
/// </summary>
public sealed class FollowTests
{
    private static (HubSession Hub, GameState State, CharacterState Who) Setup()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var who = state.Roster.Add("璐米埃尔");
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "卧室", Open = true });
        state.Territory.Link(1, 2);
        // 时钟推到上午 9 点：夜里角色会直接去睡，走不到跟随那套节律。
        state.Clock.SetTime(1, 9 * 60);
        who.Affect.LastPlayDay = 1;
        who.Affect.LastBoredDay = 1;
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(who.Id, 1);
        hub.Select(who.Id);
        return (hub, state, who);
    }

    private static Worker WorkerOf(HubSession hub, CharacterState who) =>
        hub.Day.Workers.First(w => w.CharacterId == who.Id);

    private static Facility AddFixture(GameState state, int id, string name, int roomId,
        int capacity, ActionKind action)
    {
        var facility = new Facility { Id = id, Name = name, RoomId = roomId, Capacity = capacity };
        facility.Actions.Add(action);
        state.Territory.Facilities.Add(facility);
        return facility;
    }

    // ---------- 邀请与跟随 ----------

    [Fact]
    public void Invited_maid_follows_the_player_around()
    {
        var (hub, _, who) = Setup();
        who.Grant(Trait.Maid);

        Assert.True(hub.Social(SocialAction.Invite));
        Assert.True(WorkerOf(hub, who).FollowsPlayer);
        Assert.Contains(hub.Log, l => l.Text.Contains("跟了上来"));

        // 玩家走，跟着走。移动是先推时间后换房，跟随者天然慢一拍，
        // 补一格推进让她跟上。
        Assert.True(hub.Move(2));
        hub.PassTime(5);
        Assert.Equal(2, WorkerOf(hub, who).RoomId);
        Assert.True(hub.Move(1));
        hub.PassTime(5);
        Assert.Equal(1, WorkerOf(hub, who).RoomId);
    }

    [Fact]
    public void Invite_without_fond_bond_is_refused_and_nobody_follows()
    {
        var (hub, _, who) = Setup();
        Assert.Equal(Bond.None, who.Condition.Bond);

        Assert.True(hub.Social(SocialAction.Invite));
        Assert.False(WorkerOf(hub, who).FollowsPlayer);
        Assert.Contains(hub.Log, l => l.Text.Contains("没有答应"));
    }

    [Fact]
    public void Invite_on_follower_is_a_noop_and_parting_is_a_separate_action()
    {
        var (hub, _, who) = Setup();
        who.Grant(Trait.Maid);

        Assert.True(hub.Social(SocialAction.Invite));
        Assert.True(WorkerOf(hub, who).FollowsPlayer);

        // 跟随中再点邀请：不动状态，请回走“分开”。
        Assert.True(hub.Social(SocialAction.Invite));
        Assert.True(WorkerOf(hub, who).FollowsPlayer);
        Assert.Contains(hub.Log, l => l.Text.Contains("已经跟着你了"));

        // 分开是单独的动作，不用同房也能请回。
        Assert.True(hub.Social(SocialAction.Part));
        Assert.False(WorkerOf(hub, who).FollowsPlayer);
        Assert.Contains(hub.Log, l => l.Text.Contains("不再跟着你了"));
    }

    [Fact]
    public void Favor_drop_below_fond_ends_the_follow()
    {
        var (hub, _, who) = Setup();
        who.Condition.AddFavor(100); // 好感档：够接受邀请
        Assert.True(hub.Social(SocialAction.Invite));
        Assert.True(WorkerOf(hub, who).FollowsPlayer);

        // 好感跌出“好感”档：继续跟随的资格没了，时间一推就退出。
        who.Condition.AddFavor(-100);
        hub.PassTime(5);
        Assert.False(WorkerOf(hub, who).FollowsPlayer);
        Assert.Contains(hub.Log, l => l.Text.Contains("不再跟着你了"));
    }

    [Fact]
    public void Cosleep_refusal_ends_the_follow()
    {
        var (hub, state, who) = Setup();
        who.Grant(Trait.Maid);
        hub.Social(SocialAction.Invite);
        AddFixture(state, 204, "双人床", 1, capacity: 2, ActionKind.Sleep);

        // 好感不够同床：不上床，在床边守候，队伍不解散。
        state.Clock.SetTime(1, 20 * 60); // 恰逢就寝门槛：跟随者还没来得及自己睡下
        hub.Use(204);
        Assert.True(hub.ActAtFixture(ActionKind.Sleep));
        var worker = WorkerOf(hub, who);
        Assert.True(worker.FollowsPlayer);
        Assert.Contains(hub.Log, l => l.Text.Contains("在床边守候着你"));
        Assert.DoesNotContain(hub.Log, l => l.Text.Contains("和你一起睡下了"));
    }

    // ---------- 单人设施与共坐 ----------

    [Fact]
    public void Follower_waits_in_the_room_while_player_uses_single_seat()
    {
        var (hub, state, who) = Setup();
        who.Grant(Trait.Maid);
        hub.Social(SocialAction.Invite);
        AddFixture(state, 101, "单人椅", 1, capacity: 1, ActionKind.Rest);

        Assert.True(hub.Use(101));
        hub.PassTime(5);
        var worker = WorkerOf(hub, who);
        Assert.Equal(hub.PlayerRoomId, worker.RoomId);
        Assert.Equal(-1, worker.FacilityId);
    }

    [Fact]
    public void Follower_shares_a_multi_seat_facility()
    {
        var (hub, state, who) = Setup();
        who.Grant(Trait.Maid);
        hub.Social(SocialAction.Invite);
        AddFixture(state, 102, "长凳", 1, capacity: 2, ActionKind.Rest);

        Assert.True(hub.Use(102));
        hub.PassTime(5);
        Assert.Equal(102, WorkerOf(hub, who).FacilityId);
    }

    // ---------- 交易还价 ----------

    [Fact]
    public void Follower_charm_and_social_skill_improve_prices()
    {
        var (hub, state, who) = Setup();
        who.Grant(Trait.Maid);
        hub.Social(SocialAction.Invite);
        who[CoreStat.Charm] = 10;
        who.LifeExp[(int)LifeSkill.Social] = 1000; // 本领 = 10 + 10 = 20，加成 (10+20)/2 = 15，正好顶格
        state.Territory.MarketDay["药草"] = new Territory.MarketEntry(5, 100);

        // 药草基准 3、存货 5、系数 100：买 = 3×80/100 = 2，卖 = 3×60%×75/100 = 1。
        // 跟随加成 15：买 = max(1, 2×85/100) = 1，卖 = max(1,(1×115+50)/100) = 1。
        var herbRow = state.Territory.Listing("药草")!.Value;
        Assert.Equal(2, herbRow.BuyPrice);
        Assert.Equal(1, herbRow.SellPrice);
        Assert.Equal(1, hub.TradePrices(herbRow, selling: false));
        Assert.Equal(1, hub.TradePrices(herbRow, selling: true));

        state.Money = 1000;
        hub.OpenTrade();
        Assert.True(hub.MarketTrade("药草", 1, selling: false));
        Assert.Equal(999, state.Money);
        Assert.Equal(1, state.Roster.Master!.Bag.Get("药草"));

        // 分开之后报价回到原样。
        Assert.True(hub.Social(SocialAction.Part));
        Assert.Equal(herbRow.BuyPrice, hub.TradePrices(herbRow, selling: false));
    }

    // ---------- 同床 ----------

    [Fact]
    public void Cosleep_needs_high_favor_on_a_double_bed()
    {
        var (hub, state, who) = Setup();
        who.Grant(Trait.Maid);
        hub.Social(SocialAction.Invite);
        AddFixture(state, 201, "双人床", 1, capacity: 2, ActionKind.Sleep);

        // 好感顶格：一起睡下，睡在玩家躺的那张床上。
        // （好感不够的情形见 Cosleep_refusal_ends_the_follow：躲开即退出跟随，没有第二次。）
        who.Condition.AddFavor(Vitals.FavorMax);
        state.Clock.SetTime(1, 20 * 60); // 恰逢就寝门槛：跟随者还没来得及自己睡下
        hub.Use(201);
        Assert.True(hub.ActAtFixture(ActionKind.Sleep));
        var worker = WorkerOf(hub, who);
        Assert.Equal(ActionKind.Sleep, worker.Goal);
        Assert.Equal(201, worker.FacilityId);
    }

    [Fact]
    public void Cosleep_is_refused_on_a_single_bed()
    {
        var (hub, state, who) = Setup();
        who.Grant(Trait.Maid);
        hub.Social(SocialAction.Invite);
        AddFixture(state, 202, "窄床", 1, capacity: 1, ActionKind.Sleep);
        who.Condition.AddFavor(Vitals.FavorMax);
        state.Clock.SetTime(1, 20 * 60); // 恰逢就寝门槛：跟随者还没来得及自己睡下
        hub.Use(202);
        Assert.True(hub.ActAtFixture(ActionKind.Sleep));
        var worker = WorkerOf(hub, who);
        // 单人床没有位置：跟随者自己过夜，晨起照旧跟随；绝无同床痕迹。
        Assert.True(worker.FollowsPlayer);
        Assert.DoesNotContain(hub.Log, l => l.Text.Contains("和你一起睡下了"));
    }

    // ---------- 夜晚与清晨 ----------

    [Fact]
    public void Follower_sleeps_own_bed_at_night_and_resumes_in_the_morning()
    {
        var (hub, state, who) = Setup();
        who.Grant(Trait.Maid);
        hub.Social(SocialAction.Invite);
        AddFixture(state, 203, "木床", 2, capacity: 1, ActionKind.Sleep);
        var worker = WorkerOf(hub, who);

        // 推到深夜 23:00：过了就寝钟点，去自己的床睡（跟随标记保留）。
        hub.PassTime(14 * 60);
        Assert.Equal(ActionKind.Sleep, worker.Goal);
        Assert.Equal(203, worker.FacilityId);
        Assert.True(worker.FollowsPlayer);

        // 推到次日早晨 6 点后：醒来接着跟。
        hub.PassTime(7 * 60 + 30);
        Assert.NotEqual(ActionKind.Sleep, worker.Goal);
        Assert.True(worker.FollowsPlayer);
        Assert.Equal(hub.PlayerRoomId, worker.RoomId);
    }

    [Fact]
    public void Follower_is_brought_along_when_player_enters_a_scene()
    {
        var (hub, _, who) = Setup();
        who.Grant(Trait.Maid);
        hub.Social(SocialAction.Invite);

        hub.Enter(2);
        var worker = WorkerOf(hub, who);
        Assert.Equal(2, worker.RoomId);
        Assert.True(worker.FollowsPlayer);
    }
}
