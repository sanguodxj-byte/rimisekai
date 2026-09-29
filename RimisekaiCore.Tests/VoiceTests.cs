using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Clock;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Save;
using Rimisekai.Voice;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 口上/地文系统：挑选、门槛、记账、与主界面的挂点。
/// </summary>
public sealed class VoiceTests
{
    // ---------- 挑选与门槛 ----------

    [Fact]
    public void Selects_by_trigger_and_respects_weight()
    {
        var pack = new VoicePack();
        pack.Register(new VoiceLine
        {
            Id = "a",
            Trigger = VoiceTrigger.Talk,
            Lines = { "甲" },
        });
        pack.Register(new VoiceLine
        {
            Id = "b",
            Trigger = VoiceTrigger.Observe,
            Lines = { "乙" },
        });

        var who = new CharacterState(2) { Name = "谁" };
        var ctx = ContextFor(who);

        Assert.Equal("a", pack.Select(who, VoiceTrigger.Talk, ctx)?.Id);
        Assert.Equal("b", pack.Select(who, VoiceTrigger.Observe, ctx)?.Id);
        // 没有注册的时机挑不出句子，宿主据此走默认文案。
        Assert.Null(pack.Select(who, VoiceTrigger.Gift, ctx));
    }

    [Fact]
    public void Gate_filters_by_favor_and_bond()
    {
        var pack = new VoicePack();
        pack.Register(new VoiceLine
        {
            Id = "familiar",
            Trigger = VoiceTrigger.Talk,
            Gate = new VoiceGate { FavorMax = 99 },
            Lines = { "只是熟人" },
        });
        pack.Register(new VoiceLine
        {
            Id = "fond",
            Trigger = VoiceTrigger.Talk,
            Gate = new VoiceGate { BondMin = Bond.Fond },
            Lines = { "亲近了" },
        });

        var who = new CharacterState(2) { Name = "谁" };
        who.Condition.AddFavor(50);
        Assert.Equal("familiar", pack.Select(who, VoiceTrigger.Talk, ContextFor(who))?.Id);

        who.Condition.AddFavor(100);
        Assert.Equal("fond", pack.Select(who, VoiceTrigger.Talk, ContextFor(who))?.Id);
    }

    [Fact]
    public void Gate_filters_by_hour_including_overnight_range()
    {
        var pack = new VoicePack();
        pack.Register(new VoiceLine
        {
            Id = "night",
            Trigger = VoiceTrigger.Meet,
            Gate = new VoiceGate { HourMin = 22, HourMax = 5 },
            Lines = { "夜里" },
        });

        var who = new CharacterState(2) { Name = "谁" };
        Assert.NotNull(pack.Select(who, VoiceTrigger.Meet, ContextAtHour(who, 23)));
        Assert.NotNull(pack.Select(who, VoiceTrigger.Meet, ContextAtHour(who, 3)));
        Assert.Null(pack.Select(who, VoiceTrigger.Meet, ContextAtHour(who, 12)));
    }

    [Fact]
    public void Gate_filters_by_trait_and_weather()
    {
        var pack = new VoicePack();
        pack.Register(new VoiceLine
        {
            Id = "maid",
            Trigger = VoiceTrigger.Invited,
            Gate = new VoiceGate { RequireTraits = { Trait.Maid } },
            Lines = { "女仆应了" },
        });
        pack.Register(new VoiceLine
        {
            Id = "rain",
            Trigger = VoiceTrigger.Meet,
            Gate = new VoiceGate { Weather = Weather.Rain },
            Lines = { "下雨了" },
        });

        var plain = new CharacterState(2) { Name = "普通" };
        Assert.Null(pack.Select(plain, VoiceTrigger.Invited, ContextFor(plain)));
        Assert.Null(pack.Select(plain, VoiceTrigger.Meet, ContextFor(plain)));

        var maid = new CharacterState(3) { Name = "女仆" };
        maid.Grant(Trait.Maid);
        Assert.NotNull(pack.Select(maid, VoiceTrigger.Invited, ContextFor(maid)));
        Assert.NotNull(pack.Select(maid, VoiceTrigger.Meet, ContextFor(maid, weather: Weather.Rain)));
    }

    [Fact]
    public void Once_line_never_repeats()
    {
        var pack = new VoicePack();
        pack.Register(new VoiceLine
        {
            Id = "hello",
            Trigger = VoiceTrigger.Talk,
            Gate = new VoiceGate { Once = true },
            Lines = { "只此一次" },
        });

        var who = new CharacterState(2) { Name = "谁" };
        var ctx = ContextFor(who);
        Assert.NotNull(pack.Select(who, VoiceTrigger.Talk, ctx));

        who.Voice.MarkSaid("hello", ctx.NowTotal);
        Assert.Null(pack.Select(who, VoiceTrigger.Talk, ContextFor(who)));
    }

    [Fact]
    public void Require_and_forbid_said_gate_later_lines()
    {
        var pack = new VoicePack();
        pack.Register(new VoiceLine
        {
            Id = "after",
            Trigger = VoiceTrigger.Talk,
            Gate = new VoiceGate { RequireSaid = { "before" } },
            Lines = { "接在前一句之后" },
        });
        pack.Register(new VoiceLine
        {
            Id = "before",
            Trigger = VoiceTrigger.Talk,
            Gate = new VoiceGate { ForbidSaid = { "before" } },
            Lines = { "第一次" },
        });

        var who = new CharacterState(2) { Name = "谁" };
        var ctx = ContextFor(who);
        Assert.Equal("before", pack.Select(who, VoiceTrigger.Talk, ctx)?.Id);

        who.Voice.MarkSaid("before", ctx.NowTotal);
        Assert.Equal("after", pack.Select(who, VoiceTrigger.Talk, ContextFor(who))?.Id);
    }

    [Fact]
    public void Cooldown_blocks_until_enough_time_passes()
    {
        var pack = new VoicePack();
        pack.Register(new VoiceLine
        {
            Id = "chatter",
            Trigger = VoiceTrigger.Idle,
            Gate = new VoiceGate { CooldownMinutes = 60 },
            Lines = { "闲话" },
        });

        var who = new CharacterState(2) { Name = "谁" };
        who.Voice.MarkSaid("chatter", 0);

        Assert.Null(pack.Select(who, VoiceTrigger.Idle, ContextFor(who, nowTotal: 30)));
        Assert.NotNull(pack.Select(who, VoiceTrigger.Idle, ContextFor(who, nowTotal: 60)));
    }

    [Fact]
    public void Days_since_talk_holds_back_lonely_lines()
    {
        var pack = new VoicePack();
        pack.Register(new VoiceLine
        {
            Id = "lonely",
            Trigger = VoiceTrigger.Talk,
            Gate = new VoiceGate { DaysSinceTalkMin = 2 },
            Lines = { "好久没说话" },
        });

        var who = new CharacterState(2) { Name = "谁" };
        // 从没交谈过按“很久”算，所以一开始就能说。
        Assert.NotNull(pack.Select(who, VoiceTrigger.Talk, ContextFor(who)));

        who.Affect.LastTalkAt = 0;
        Assert.Null(pack.Select(who, VoiceTrigger.Talk, ContextFor(who, nowTotal: 1440)));
        Assert.NotNull(pack.Select(who, VoiceTrigger.Talk, ContextFor(who, nowTotal: 1440 * 2)));
    }

    // ---------- 记账与调度 ----------

    [Fact]
    public void Director_records_memory_when_it_speaks()
    {
        var director = new VoiceDirector();
        var pack = new VoicePack();
        pack.Register(new VoiceLine
        {
            Id = "once",
            Trigger = VoiceTrigger.Talk,
            Gate = new VoiceGate { Once = true },
            Lines = { "你好" },
        });
        director.Register("璐米埃尔", pack);

        var who = new CharacterState(2) { Name = "璐米埃尔" };
        var ctx = ContextFor(who);

        Assert.True(director.Enabled);
        var first = director.Speak(who, VoiceTrigger.Talk, ctx);
        Assert.NotNull(first);
        Assert.Equal("璐米埃尔", first!.Value.Speaker);
        Assert.True(who.Voice.HasSaid("once"));
        Assert.Equal(0, who.Voice.LastSpoke(VoiceTrigger.Talk));

        // 说过一次之后不再重复。
        Assert.Null(director.Speak(who, VoiceTrigger.Talk, ContextFor(who)));
    }

    [Fact]
    public void Empty_director_is_disabled_and_returns_nothing()
    {
        var director = new VoiceDirector();
        var who = new CharacterState(2) { Name = "谁" };
        Assert.False(director.Enabled);
        Assert.Null(director.Speak(who, VoiceTrigger.Talk, ContextFor(who)));
    }

    [Fact]
    public void World_pack_answers_when_character_has_no_line()
    {
        var director = new VoiceDirector();
        director.World.Register(new VoiceLine
        {
            Id = "dawn",
            Kind = VoiceKind.Narration,
            Trigger = VoiceTrigger.DayEnd,
            Lines = { "天亮了。" },
        });

        var who = new CharacterState(2) { Name = "谁" };
        var said = director.Speak(who, VoiceTrigger.DayEnd, ContextFor(who));
        Assert.NotNull(said);
        Assert.Equal(VoiceKind.Narration, said!.Value.Kind);
        Assert.Equal("天亮了。", VoiceDirector.ToLog(said.Value));
    }

    // ---------- 主界面挂点 ----------

    [Fact]
    public void Talk_prefers_authored_line_over_placeholder()
    {
        var (hub, who) = TalkSetup();
        hub.State.Voice.Register(who.Name, PackFor(who.Name, new VoiceLine
        {
            Id = "hi",
            Trigger = VoiceTrigger.Talk,
            Lines = { "早。", "院子有露水。" },
        }));

        Assert.True(hub.Social(SocialAction.Talk));
        Assert.True(hub.MapCovered);
        Assert.Equal("早。", hub.Overlay!.Text);
        Assert.True(hub.AdvanceOverlay());
        Assert.Equal("院子有露水。", hub.Overlay!.Text);
        Assert.False(hub.AdvanceOverlay());
    }

    [Fact]
    public void Talk_falls_back_to_placeholder_without_content()
    {
        var (hub, _) = TalkSetup();
        Assert.True(hub.Social(SocialAction.Talk));
        Assert.True(hub.MapCovered);
        Assert.Equal("……", hub.Overlay!.Text);
    }

    [Fact]
    public void Gift_line_can_target_a_specific_item()
    {
        var (hub, who) = TalkSetup();
        hub.State.Roster.Master!.Bag.Add("花", 1);
        hub.State.Roster.Master!.Bag.Add("铁", 1);
        // 只注册针对“花”的那一句：送别的东西时挑不出台词，
        // 于是两条路径互不重叠，断言不受随机挑选影响。
        hub.State.Voice.Register(who.Name, PackFor(who.Name, new VoiceLine
        {
            Id = "flower",
            Trigger = VoiceTrigger.Gift,
            Gate = new VoiceGate { GiftItemId = "花" },
            Lines = { "谢谢你的花。" },
        }));

        Assert.True(hub.Social(SocialAction.Gift, "铁"));
        Assert.Contains(hub.Log, l => l.Text.Contains("你把铁送给了璐米埃尔。"));

        // 送对了东西有台词：回应进对话框，日志不写台词。
        Assert.True(hub.Social(SocialAction.Gift, "花"));
        Assert.NotNull(hub.Overlay);
        Assert.Contains("谢谢你的花。", hub.Overlay!.Text);
        Assert.DoesNotContain(hub.Log, l => l.Text.Contains("谢谢你的花。"));
    }

    [Fact]
    public void Moving_into_a_room_never_spawns_dialogue()
    {
        var (hub, who) = TalkSetup();
        hub.State.Voice.Register(who.Name, PackFor(who.Name, new VoiceLine
        {
            Id = "greet",
            Trigger = VoiceTrigger.Meet,
            Lines = { "回来了。" },
        }));

        // 进房不是角色交互：对话框不弹，台词也不落日志。
        hub.Place(who.Id, 2);
        Assert.True(hub.Move(2));
        Assert.Null(hub.Overlay);
        Assert.DoesNotContain(hub.Log, l => l.Text.Contains("回来了。"));

        // 折返再进也一样，移动永远是安静的。
        Assert.True(hub.Move(1));
        Assert.True(hub.Move(2));
        Assert.Null(hub.Overlay);
        Assert.DoesNotContain(hub.Log, l => l.Text.Contains("回来了。"));
    }

    [Fact]
    public void Moving_into_an_empty_room_says_nothing()
    {
        var (hub, who) = TalkSetup();
        hub.State.Voice.Register(who.Name, PackFor(who.Name, new VoiceLine
        {
            Id = "greet",
            Trigger = VoiceTrigger.Meet,
            Lines = { "回来了。" },
        }));

        // 她留在庭院，玩家去水井——那间房没人，不该有人开口。
        Assert.True(hub.Move(2));
        Assert.False(hub.MapCovered);
    }

    [Fact]
    public void Seek_lines_pop_the_dialogue_box_on_arrival()
    {
        var (hub, who) = TalkSetup();
        hub.State.Voice.Register(who.Name, PackFor(who.Name, new VoiceLine
        {
            Id = "seek",
            Kind = VoiceKind.Narration,
            Trigger = VoiceTrigger.Seek,
            Lines = { "璐米埃尔在等你回头。" },
        }));

        who.Affect.ChatDesire = 100;
        hub.PassTime(10);
        // 她走到你面前是角色主动找玩家对话：台词弹对话框，日志不写台词。
        Assert.NotNull(hub.Overlay);
        Assert.Contains("璐米埃尔在等你回头。", hub.Overlay!.Text);
        Assert.DoesNotContain(hub.Log, l => l.Text == "璐米埃尔在等你回头。");
        Assert.DoesNotContain(hub.Log, l => l.Text == "璐米埃尔似乎想对你说什么。");
    }

    [Fact]
    public void Seek_falls_back_to_builtin_nudge_without_content()
    {
        var (hub, who) = TalkSetup();
        who.Affect.ChatDesire = 100;
        hub.PassTime(10);
        // 没台词时退回行为叙述；对话框保持安静。
        Assert.Contains(hub.Log, l => l.Text == "璐米埃尔似乎想对你说什么。");
        Assert.Null(hub.Overlay);
    }

    // ---------- 女仆素质 ----------

    [Fact]
    public void Maid_accepts_invite_regardless_of_favor()
    {
        var maid = new CharacterState(1) { Name = "璐米埃尔" };
        maid.Grant(Trait.Maid);
        maid.Condition.AddFavor(50);
        Assert.Equal(Bond.None, maid.Condition.Bond);
        Assert.True(maid.AcceptsInvite());
        Assert.False(maid.RequiresWage());
    }

    [Fact]
    public void Others_need_fond_bond_to_accept_invite_and_do_want_wages()
    {
        var other = new CharacterState(1) { Name = "汉斯" };
        other.Condition.AddFavor(50);
        Assert.False(other.AcceptsInvite());
        Assert.True(other.RequiresWage());

        other.Condition.AddFavor(100);
        Assert.Equal(Bond.Fond, other.Condition.Bond);
        Assert.True(other.AcceptsInvite());
    }

    [Fact]
    public void Invite_reports_acceptance_and_refusal()
    {
        var (hub, who) = TalkSetup();
        hub.State.Voice.Register(who.Name, PackFor(who.Name,
            new VoiceLine
            {
                Id = "yes",
                Trigger = VoiceTrigger.Invited,
                Lines = { "好啊。" },
            },
            new VoiceLine
            {
                Id = "no",
                Trigger = VoiceTrigger.InviteRefused,
                Lines = { "现在不行。" },
            }));

        Assert.True(hub.Social(SocialAction.Invite));
        Assert.Contains("现在不行。", hub.Overlay!.Text);
        Assert.DoesNotContain(hub.Log, l => l.Text.Contains("现在不行。"));

        who.Grant(Trait.Maid);
        Assert.True(hub.Social(SocialAction.Invite));
        Assert.Contains("好啊。", hub.Overlay!.Text);
        Assert.DoesNotContain(hub.Log, l => l.Text.Contains("好啊。"));
    }

    // ---------- 存档 ----------

    [Fact]
    public void Voice_memory_survives_save_and_load()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var who = state.Roster.Add("璐米埃尔");
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(who.Id, 1);
        hub.Select(who.Id);
        state.Voice.Register(who.Name, PackFor(who.Name, new VoiceLine
        {
            Id = "once",
            Trigger = VoiceTrigger.Talk,
            Gate = new VoiceGate { Once = true },
            Lines = { "只此一次" },
        }));
        Assert.True(hub.Social(SocialAction.Talk));
        who.Voice.MarkSpoke(VoiceTrigger.Idle, 123);

        var loaded = SaveSystem.Load(SaveSystem.Save(state, hub));
        var loadedWho = loaded.Roster.Find(who.Id);
        Assert.NotNull(loadedWho);
        Assert.True(loadedWho!.Voice.HasSaid("once"));
        Assert.Equal(123, loadedWho.Voice.LastSpoke(VoiceTrigger.Idle));
    }

    // ---------- 内容文件 ----------

    [Fact]
    public void Shipped_voice_file_parses_and_covers_the_tutorial_character()
    {
        var path = VoiceFilePath();
        Assert.True(System.IO.File.Exists(path), $"找不到台词文件：{path}");

        var json = System.IO.File.ReadAllText(path);
        Assert.True(VoicePackJson.TryParse(json, out var characters, out var world, out var error), error);

        Assert.True(characters.ContainsKey("璐米埃尔"));
        var pack = characters["璐米埃尔"];
        Assert.True(pack.Count > 0);

        // 每个注册的句子都要能对上号，否则内容里打错了时机名。
        foreach (var line in pack.Lines)
            Assert.Equal(line.Id, pack.Find(line.Id)?.Id);

        // 世界旁白层已废除：世界包不再承载内容，日志只写系统叙述。
        Assert.True(world.Count == 0);

        // 教程角色要覆盖开场与日常两个最常用的时机。
        var who = new CharacterState(2) { Name = "璐米埃尔" };
        who.Grant(Trait.Maid);
        who.Condition.AddFavor(50);
        Assert.NotNull(pack.Select(who, VoiceTrigger.Meet, ContextFor(who)));
        Assert.NotNull(pack.Select(who, VoiceTrigger.Talk, ContextFor(who)));
        Assert.NotNull(pack.Select(who, VoiceTrigger.Invited, ContextFor(who)));
    }

    [Fact]
    public void Shipped_voice_file_parses_as_valid_json()
    {
        var path = VoiceFilePath();        using var document = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal(System.Text.Json.JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.True(document.RootElement.TryGetProperty("characters", out _));
    }

    [Fact]
    public void Broken_voice_json_reports_error_instead_of_throwing()
    {
        Assert.False(VoicePackJson.TryParse("{ not json", out _, out _, out var error));
        Assert.NotEqual("", error);
    }

    // ---------- 辅助 ----------

    private static VoiceContext ContextFor(
        CharacterState who,
        int nowTotal = 0,
        Weather weather = Weather.Clear)
    {
        var day = nowTotal / GameClock.MinutesPerDay + 1;
        var minutes = nowTotal % GameClock.MinutesPerDay;
        return VoiceContext.For(who, VoiceTrigger.Talk, -1, Season.Spring, weather, day, minutes);
    }

    /// <summary>按小时定点组上下文，用来测时段门槛。</summary>
    private static VoiceContext ContextAtHour(
        CharacterState who,
        int hour,
        Weather weather = Weather.Clear) =>
        VoiceContext.For(who, VoiceTrigger.Talk, -1, Season.Spring, weather, 1, hour * 60);

    private static VoicePack PackFor(string name, params VoiceLine[] lines)
    {
        var pack = new VoicePack();
        foreach (var line in lines)
            pack.Register(line);
        return pack;
    }

    /// <summary>
    /// 一间房、一个在场角色、已选中——交谈与移动挂点的最小场景。
    /// 时钟推到上午 9 点：深夜会让角色直接去睡，走不到找人那套节律。
    /// 标成今天已娱乐过，免得闲时节律把她逛到别的房间，打乱位置断言。
    /// </summary>
    private static (HubSession Hub, CharacterState Who) TalkSetup()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var who = state.Roster.Add("璐米埃尔");
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "水井", Open = true });
        state.Territory.Link(1, 2);
        state.Clock.Advance(9 * 60);
        who.Affect.LastPlayDay = 1;
        who.Affect.LastBoredDay = 1;
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(who.Id, 1);
        hub.Select(who.Id);
        return (hub, who);
    }

    /// <summary>台词文件在仓库里的真实路径。测试从 bin 目录往上找仓库根。</summary>
    private static string VoiceFilePath()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "content", "voice.json");
            if (System.IO.File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return System.IO.Path.Combine("content", "voice.json");
    }
}
