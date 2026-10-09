using System.Collections.Generic;
using System.Linq;
using Rimisekai.Character;
using Rimisekai.Clock;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Save;
using Rimisekai.Voice;
using Xunit;

namespace Rimisekai.Tests;

public class SocialNarrationTests
{
    private static (HubSession hub, CharacterState who) SetupSocial()
    {
        var state = new GameState();
        state.Roster.Add("领主", master: true);
        var who = state.Roster.Add("赛琳");
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(who.Id, 1);
        hub.Select(who.Id);
        return (hub, who);
    }

    [Fact]
    public void Social_first_time_displays_dim_check_header_and_subsequent_does_not()
    {
        var (hub, who) = SetupSocial();

        var pack = new VoicePack();
        pack.Register(new VoiceLine
        {
            Settings = { VoiceSetting.Territory },
            Id = "pat_act",
            Trigger = VoiceTrigger.PatHead,
            Kind = VoiceKind.Narration,
            Places = { VoicePlace.Before },
            Lines = { "你伸出手，轻轻抚摸着赛琳柔顺的头发。" },
        });
        pack.Register(new VoiceLine
        {
            Settings = { VoiceSetting.Territory },
            Id = "pat_react",
            Trigger = VoiceTrigger.PatHead,
            Kind = VoiceKind.Narration,
            Places = { VoicePlace.After },
            Lines = { "少女脸颊微红，顺从地垂下眼帘。" },
        });
        pack.Register(new VoiceLine
        {
            Settings = { VoiceSetting.Territory },
            Id = "pat_speech",
            Trigger = VoiceTrigger.PatHead,
            Kind = VoiceKind.Speech,
            Lines = { "大、大人……这样摸头，稍微有点害羞呢……" },
        });

        hub.State.Voice.Register(who.Name, pack);
        who.Condition.AddFavor(150); // 确保通过好感门槛

        // 第一次执行：未成功过，第一行为暗色判定行
        Assert.False(hub.HasEverSucceeded(who, SocialAction.PatHead));
        var ok = hub.Social(SocialAction.PatHead);
        Assert.True(ok);
        Assert.NotNull(hub.Overlay);

        // 首句呈现暗色判定过程与结果
        Assert.True(hub.Overlay!.CurrentLineDim);
        Assert.Contains("判定 好感 150/100", hub.Overlay!.Text);
        Assert.Contains("判定成功", hub.Overlay!.Text);

        // 推进一次呈现玩家动作 A
        Assert.True(hub.AdvanceOverlay());
        Assert.False(hub.Overlay!.CurrentLineDim);
        Assert.Equal("你伸出手，轻轻抚摸着赛琳柔顺的头发。", hub.Overlay!.Text);

        // 推进二次呈现角色反应 B
        Assert.True(hub.AdvanceOverlay());
        Assert.Equal("少女脸颊微红，顺从地垂下眼帘。", hub.Overlay!.Text);

        // 推进三次呈现角色原声台词
        Assert.True(hub.AdvanceOverlay());
        Assert.Equal("大、大人……这样摸头，稍微有点害羞呢……", hub.Overlay!.Text);

        // 推进结束
        Assert.False(hub.AdvanceOverlay());

        // 动作已成功标记入档
        Assert.True(hub.HasEverSucceeded(who, SocialAction.PatHead));

        // 第二次执行：已经成功过，首行不再显示判定行，直接呈现动作 A！
        ok = hub.Social(SocialAction.PatHead);
        Assert.True(ok);
        Assert.NotNull(hub.Overlay);
        Assert.False(hub.Overlay!.CurrentLineDim);
        Assert.Equal("你伸出手，轻轻抚摸着赛琳柔顺的头发。", hub.Overlay!.Text);
    }

    [Fact]
    public void Social_when_refused_presents_check_and_dodge_in_overlay()
    {
        var (hub, who) = SetupSocial();
        who.Condition.AddFavor(20); // 好感不足 100

        var ok = hub.Social(SocialAction.PatHead);
        Assert.True(ok);
        Assert.NotNull(hub.Overlay);

        // 判定失败：首行为暗色判定行（判定失败）
        Assert.True(hub.Overlay!.CurrentLineDim);
        Assert.Contains("判定失败", hub.Overlay!.Text);

        // 推进呈现躲开反应地文
        Assert.True(hub.AdvanceOverlay());
        Assert.False(hub.Overlay!.CurrentLineDim);
        Assert.Contains("躲开了", hub.Overlay!.Text);

        // 推进结束
        Assert.False(hub.AdvanceOverlay());
    }

    [Fact]
    public void Evaluate_check_factors_affect_target_gate()
    {
        var (hub, who) = SetupSocial();
        who.Condition.AddFavor(200); // 达到熟悉（Fond）

        var check1 = hub.EvaluateSocialCheck(who, SocialAction.Hug);
        Assert.Equal(500, check1.Gate);

        // 心情提升到 80，门槛降低：(80-50)*0.4 = 12
        who.Affect.AddMood(30);
        var check2 = hub.EvaluateSocialCheck(who, SocialAction.Hug);
        Assert.Equal(500 - 12, check2.Gate);
    }

    [Fact]
    public void Meal_has_satiety_window_refuses_continuous_eating()
    {
        var (hub, _) = SetupSocial();
        var master = hub.State.Roster.Master!;
        master.Bag.Add("bread", 5);

        // 放置带有餐桌的房间设施
        hub.State.Territory.AddFacility(new Facility { Id = 10, RoomId = 1, Name = "餐桌", Built = true, Actions = { ActionKind.Meal }, IsTable = true });
        hub.Use(10);

        // 第一次正常就餐
        Assert.True(hub.ActAtFixture(ActionKind.Meal));
        Assert.Contains(hub.Log, l => l.Text.Contains("吃了bread"));

        // 5分钟内立即强行连吃：饱腹拒绝！
        Assert.False(hub.ActAtFixture(ActionKind.Meal));
        Assert.Contains(hub.Log, l => l.Text.Contains("肚子还饱着呢，现在吃不下了"));

        // 推进时间 125 分钟（超过 2 小时饱腹窗口）
        hub.PassTime(125);
        // 恢复食欲，可以再次进食
        Assert.True(hub.ActAtFixture(ActionKind.Meal));
    }

    [Fact]
    public void Gift_favor_varies_by_preference_and_refuses_when_hostile()
    {
        var (hub, who) = SetupSocial();
        var master = hub.State.Roster.Master!;
        master.Bag.Add("药水", 2);
        master.Bag.Add("铁矿石", 2);

        // 送心仪礼物（法师喜欢药水）：好感加成显著（+30）
        var favorBefore = who.Condition.Favor;
        Assert.True(hub.Social(SocialAction.Gift, "药水"));
        var gainFav = who.Condition.Favor - favorBefore;
        Assert.True(gainFav >= 30);

        // 送粗陋废料（铁矿石）：仅礼貌收下，好感微弱（+2）
        favorBefore = who.Condition.Favor;
        Assert.True(hub.Social(SocialAction.Gift, "铁矿石"));
        var gainWaste = who.Condition.Favor - favorBefore;
        Assert.True(gainWaste <= 5);

        // 敌对关系（Hostile）：警惕拒绝收礼，不消耗物品
        who.Condition.AddFavor(-700); // 降至 Hostile 档
        var bagCount = master.Bag.Get("药水");
        Assert.True(hub.Social(SocialAction.Gift, "药水"));
        Assert.Equal(bagCount, master.Bag.Get("药水")); // 物品未消耗
        Assert.NotNull(hub.Overlay);
        Assert.Contains("没有收下", hub.Overlay!.Text);
    }

    [Fact]
    public void Worker_exits_bench_when_no_materials_and_resumes_instantly_on_arrival()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var smith = state.Roster.Add("铁匠");
        state.Territory.AddRoom(new Room { Id = 1, Name = "工坊", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "库房", Open = true });
        state.Territory.Link(1, 2);

        // 货架位于库房，当前空无一物（零原材料）
        var shelf = new Facility { Id = 1, Name = "货架", RoomId = 2, CanStore = true, Built = true };
        state.Territory.AddFacility(shelf);
        // 铁砧位于工坊，支持锻造
        var forge = new Facility { Id = 2, Name = "铁砧", RoomId = 1, Usage = FacilityUsage.Plain, Actions = { ActionKind.Forge }, Built = true };
        state.Territory.AddFacility(forge);

        state.Territory.AddRecipe(new Recipe
        {
            ItemId = "铁", Station = ActionKind.Forge, OutputCount = 1,
            Costs = { new RecipeCost("铁矿", 2) },
        });

        // 安排铁匠全天在铁砧上工作
        for (var slot = 0; slot < WorkSlot.Count; slot++)
            state.Territory.Assign(smith.Id, slot, SlotMode.Work, 2);

        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(smith.Id, 1);

        // 1. 无材料推进时间：铁匠绝不空占铁砧打空气，而是自然退出工作，转入闲时生活（Loiter）
        hub.PassTime(30);
        var worker = hub.Day.Workers.First(w => w.CharacterId == smith.Id);
        Assert.NotEqual(ActionKind.Forge, worker.Goal);
        Assert.Equal(ActionKind.None, worker.Task);
        Assert.True(worker.Goal == ActionKind.Loiter || worker.Goal == ActionKind.Rest);

        // 2. 原材料到货：向库房货架存入 2 个铁矿
        shelf.Contents.Add("铁矿", 2);

        // 3. 下一心跳立即检测到原材料：铁匠打断闲逛，立即自动发起备料搬运去库房取矿
        hub.PassTime(5);
        Assert.Equal(ActionKind.Haul, worker.Goal);
        Assert.Equal("铁矿", worker.HaulItemId);

        // 4. 继续推进时间：完成备料搬运并开工锻造，最终产出成品铁锭
        for (var i = 0; i < 20; i++)
            hub.PassTime(5);

        Assert.True(state.Territory.CountWith(smith, "铁") > 0);
        // 铁矿被扣减消耗
        Assert.Equal(0, shelf.Contents.Get("铁矿"));
    }

    [Fact]
    public void Master_participates_in_schedule_and_works_automatically_during_work_slot()
    {
        var state = new GameState();
        state.Clock.SetTime(1, 0);
        var master = state.Roster.Add("你", master: true);
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "工坊", Open = true });
        state.Territory.Link(1, 2);

        var anvil = new Facility { Id = 10, RoomId = 2, Usage = FacilityUsage.Plain, Actions = { ActionKind.Forge }, Built = true };
        anvil.Contents.Add("铁矿", 4); // 原料已就位在铁砧
        state.Territory.AddFacility(anvil);

        state.Territory.AddRecipe(new Recipe
        {
            ItemId = "铁", Station = ActionKind.Forge, OutputCount = 1,
            Costs = { new RecipeCost("铁矿", 2) },
        });

        var hub = new HubSession(state);
        hub.Enter(1); // 玩家初始在 1 号庭院

        // 1. 验证玩家自身可以成功排班（不被拦截拒绝）
        Assert.True(hub.Assign(master.Id, 0, SlotMode.Work, 10));
        Assert.Equal(SlotMode.Work, hub.ScheduleOf(master.Id).Slots[0].Mode);
        Assert.Equal(10, hub.ScheduleOf(master.Id).Slots[0].FacilityId);

        // 2. 在工作时段推进时间：玩家自身自动动起来，走到 2 号工坊，入座铁砧并积累进度
        hub.PassTime(30);
        Assert.Equal(2, hub.PlayerRoomId); // 自动走向工坊
        Assert.Equal(10, hub.UsingFixtureId); // 自动入座铁砧

        // 3. 继续推进时间完成工作：成功锻造出铁，放入背包，并真实扣减铁砧材料
        for (var i = 0; i < 20; i++)
            hub.PassTime(5);

        Assert.True(master.Bag.Get("铁") > 0);
        Assert.Equal(2, anvil.Contents.Get("铁矿")); // 4 - 2 = 2
    }

    [Fact]
    public void Craft_feature_sets_target_instead_of_instant_conversion()
    {
        var state = new GameState();
        var master = state.Roster.Add("你", master: true);
        var cook = state.Roster.Add("厨师");
        state.Territory.AddRoom(new Room { Id = 1, Name = "厨房", Open = true });
        var stove = new Facility { Id = 20, RoomId = 1, Usage = FacilityUsage.Plain, Actions = { ActionKind.Cook }, Built = true };
        stove.Contents.Add("肉", 5);
        stove.Contents.Add("菜", 5);
        state.Territory.AddFacility(stove);

        state.Territory.AddRecipe(new Recipe
        {
            ItemId = "大餐", Station = ActionKind.Cook, OutputCount = 1,
            Costs = { new RecipeCost("肉", 1), new RecipeCost("菜", 1) },
        });
        state.Territory.AddRecipe(new Recipe
        {
            ItemId = "小食", Station = ActionKind.Cook, OutputCount = 1,
            Costs = { new RecipeCost("菜", 1) },
        });

        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(cook.Id, 1);

        // 1. 制作功能不直接转化资源（玩家背包不变，材料未扣减，仅将配方登记为领地目标）
        Assert.True(hub.Craft("大餐"));
        Assert.Equal("大餐", state.Territory.GetTargetCraftItem(ActionKind.Cook));
        Assert.Equal(0, master.Bag.Get("大餐")); // 绝不凭空塞进背包！
        Assert.Equal(5, stove.Contents.Get("肉")); // 绝不即时扣减材料！

        // 2. 安排厨师在灶台工作：厨师自动针对设定目标“大餐”进行备料与烹饪
        for (var slot = 0; slot < WorkSlot.Count; slot++)
            state.Territory.Assign(cook.Id, slot, SlotMode.Work, 20);

        for (var i = 0; i < 30; i++)
            hub.PassTime(5);

        // 3. 由厨师在工作日程中通过真实工作产出“大餐”
        Assert.True(state.Territory.CountWith(cook, "大餐") > 0);
        Assert.True(stove.Contents.Get("肉") < 5);
    }
}
