using System.Collections.Generic;
using System.Linq;
using Rimisekai.Character;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Save;
using Rimisekai.Voice;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 定时事件与后台预生成：事件管"什么时候演、谁来演"，场景管"演什么"。
/// 开局排队生成 → 到点且内容就绪才开演 → 现掷演员到点才入册 → 成品进存档。
/// </summary>
public sealed class EventSchedulingTests
{
    private const string EventId = "visitor_arrival";

    /// <summary>访客场景：正文全是生成槽，骨架（步骤/分支/效果）写在内容里。</summary>
    private static SceneEvent VisitorScene() => new()
    {
        Id = "visitor_arrival",
        Title = "登门的访客",
        Characters = { "访客" },
        Rate = 1000,
        Spawn = true,
        DismissChoice = "decline",
        Steps =
        {
            new SceneStep
            {
                Id = "0",
                Lines =
                {
                    new SceneText(VoiceKind.Narration, "", "", Gen("写一句叩门的地文。")),
                    new SceneText(VoiceKind.Speech, "访客", "", Gen("旅人开口问候。")),
                },
                Choices =
                {
                    new SceneChoice { Id = "accept", Label = "「留下吧」", GotoStep = 1,
                        Effects = { new SceneEffect { Kind = SceneEffectKind.Favor, Amount = 20 } } },
                    new SceneChoice { Id = "decline", Label = "「这里容不下你」", GotoStep = 2 },
                },
            },
            new SceneStep
            {
                Id = "1",
                End = true,
                Lines = { new SceneText(VoiceKind.Speech, "访客", "", Gen("道谢。")) },
            },
            new SceneStep
            {
                Id = "2",
                End = true,
                Lines = { new SceneText(VoiceKind.Speech, "访客", "", Gen("告辞。")) },
            },
        },
    };

    /// <summary>定时事件行：第三天十一点，指向访客场景。</summary>
    private static HubEventDef VisitorEvent() => new()
    {
        Id = EventId,
        Title = "登门的访客",
        Trigger = HubEventTrigger.Scheduled,
        SceneId = "visitor_arrival",
        Day = 3,
        Hour = 11,
        Once = true,
        Priority = 10,
    };

    private static VoiceGeneration Gen(string instruction) => new()
    {
        Instruction = instruction,
        Style = "沉静克制。",
        LineCount = 1,
        MaxCharsPerLine = 26,
        CacheMinutes = 0,
    };

    /// <summary>一个据点：一间房、一个主角，注册好访客场景与它的定时事件。</summary>
    private static (GameState state, HubSession hub) Setup()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        state.Voice.Scenes.Register(VisitorScene());
        state.Voice.Events.Register(VisitorEvent());
        var hub = new HubSession(state);
        hub.Enter(1);
        hub.InitializeScheduledEvents();
        return (state, hub);
    }

    /// <summary>把队列里的任务全部跑掉，用给定的成品文本。</summary>
    private static void Drain(HubSession hub, string text = "生成的正文。")
    {
        while (true)
        {
            var task = hub.TakeGenerationTask();
            if (task == null)
                break;
            hub.CompleteGeneration(task, new[] { text });
        }
    }

    /// <summary>推进一次时间，触发事件巡检与演出（走生产的 PassTime 管线）。</summary>
    private static void Tick(HubSession hub) => hub.PassTime(TerritoryClock.StepMinutes);

    /// <summary>
    /// 把当前演出走完：遇到分支就选 <paramref name="choiceIndex"/>，其余一路继续。
    /// 有步数上限，卡住就抛——免得测试挂死。
    /// </summary>
    private static void PlayToEnd(HubSession hub, int choiceIndex)
    {
        for (var guard = 0; guard < 100 && hub.ScenePlaying; guard++)
        {
            if (hub.SceneChoices.Count > 0)
            {
                hub.SceneChoose(choiceIndex);
                continue;
            }
            hub.SceneContinue();
        }
        Assert.False(hub.ScenePlaying, "演出没能在限定步数内走完");
    }

    [Fact]
    public void 开局即掷演员并排队_但演员不进名册()
    {
        var (state, hub) = Setup();

        // 后台任务已排好：4 个生成槽全排上。
        Assert.Equal(4, hub.PendingGenerationCount);

        // 演员掷好了但不在名册里：队伍面板看不到幽灵角色。
        Assert.Single(state.Roster.Members);
        Assert.Equal("你", state.Roster.Members[0].Name);
        Assert.True(hub.StagedActors.ContainsKey(EventId));
    }

    [Fact]
    public void 内容未就绪时到点也不开演_就绪后开演()
    {
        var (state, hub) = Setup();

        // 时间推到第三天 11 点：内容还没生成完，事件不该开演。
        state.Clock.SetTime(3, 11 * 60);
        Tick(hub);
        Assert.False(hub.ScenePlaying);
        Assert.False(state.FiredEvents.Contains(EventId));

        // 把后台任务跑完，内容就绪；再推一格时间即可开演。
        Drain(hub, "门外有人叩了三下。");
        Tick(hub);
        Assert.True(hub.ScenePlaying);
        Assert.True(state.FiredEvents.Contains(EventId));
    }

    [Fact]
    public void 到点前不触发()
    {
        var (state, hub) = Setup();
        Drain(hub);

        // 第三天 10 点：还没到 11 点。
        state.Clock.SetTime(3, 10 * 60);
        Tick(hub);
        Assert.False(hub.ScenePlaying);

        // 第二天 12 点：日期没到。
        state.Clock.SetTime(2, 12 * 60);
        Tick(hub);
        Assert.False(hub.ScenePlaying);
    }

    [Fact]
    public void 演过之后不再触发()
    {
        var (state, hub) = Setup();
        Drain(hub);

        state.Clock.SetTime(3, 11 * 60);
        Tick(hub);
        Assert.True(hub.ScenePlaying);

        // 走完演出（选"留下吧"）。
        PlayToEnd(hub, 0);

        // 再推时间：FiredEvents 终身去重，不再开演。
        Tick(hub);
        Assert.False(hub.ScenePlaying);
    }

    [Fact]
    public void 开演时演员才入册并站在玩家跟前()
    {
        var (state, hub) = Setup();
        Drain(hub);

        state.Clock.SetTime(3, 11 * 60);
        Tick(hub);
        Assert.True(hub.ScenePlaying);

        // 演员此刻已入册：主角 + 来客。
        Assert.Equal(2, state.Roster.Members.Count);
        Assert.Empty(hub.StagedActors);
        var visitor = hub.SceneActor!;
        Assert.False(visitor.IsMaster);
        Assert.True(visitor.Id > 0);
        Assert.Contains(hub.Log, l => l.Text.Contains("登门求见"));
    }

    [Fact]
    public void 播放取到的是生成文本而不是静态文本()
    {
        var (state, hub) = Setup();
        Drain(hub, "生成出来的叩门声。");

        state.Clock.SetTime(3, 11 * 60);
        Tick(hub);
        Assert.True(hub.ScenePlaying);

        // 第一行是旁白，正文应取生成成品。
        Assert.Contains(hub.SceneLines, l => l.Text.Contains("生成出来的叩门声"));

        // 推进到台词那一行：占位说话人已换成演员真名，正文取生成成品。
        hub.SceneContinue();
        var spoken = hub.SceneLines.First(l => l.Kind == VoiceKind.Speech);
        Assert.NotEqual("访客", spoken.Speaker);
        Assert.Equal("生成出来的叩门声。", spoken.Text);
    }

    [Fact]
    public void 送客把演员移出名册_留人则留下()
    {
        var (state, hub) = Setup();
        Drain(hub);
        state.Clock.SetTime(3, 11 * 60);
        Tick(hub);
        var visitorId = hub.SceneActor!.Id;

        hub.SceneContinue();      // 走完 step0 的行
        hub.SceneChoose(1);       // 选 decline（送客）
        PlayToEnd(hub, 1);

        Assert.Null(state.Roster.Find(visitorId));
        Assert.Contains(hub.Log, l => l.Text.Contains("告辞"));
    }

    [Fact]
    public void 成品与暂存演员随存档往返()
    {
        var (state, hub) = Setup();
        Drain(hub, "存档前的成品。");

        // 存一份：成品表 + 暂存演员都要进存档。
        var json = SaveSystem.Save(state, hub);
        var loaded = SaveSystem.Load(json);

        Assert.True(loaded.SceneTexts.Has(EventId, 0, 0));
        Assert.Equal("存档前的成品。", loaded.SceneTexts.Get(EventId, 0, 0));

        // 暂存演员经 HubSnapshot 往返：读档后仍在排班表里，且不在名册。
        var snapshot = hub.Snapshot();
        // 真实读档的顺序：先由内容包把场景与事件灌回世界，再建会话并恢复快照。
        loaded.Voice.Scenes.Register(VisitorScene());
        loaded.Voice.Events.Register(VisitorEvent());
        var hub2 = new HubSession(loaded);
        hub2.Enter(1);
        hub2.Restore(snapshot);
        Assert.True(hub2.StagedActors.ContainsKey(EventId));
        Assert.Single(loaded.Roster.Members);

        // 读档后不重掷演员：内容已就绪，到点直接开演。
        loaded.Clock.SetTime(3, 11 * 60);
        Tick(hub2);
        Assert.True(hub2.ScenePlaying);
    }

    [Fact]
    public void 未就绪的行不会被重复排()
    {
        var (_, hub) = Setup();

        var first = hub.TakeGenerationTask();
        Assert.NotNull(first);
        var count = hub.PendingGenerationCount;

        // 同一个槽不会因为再次初始化而重复排。
        hub.InitializeScheduledEvents();
        Assert.Equal(count, hub.PendingGenerationCount);
    }

    [Fact]
    public void 生成失败到上限后放弃该行()
    {
        var (state, hub) = Setup();

        var task = hub.TakeGenerationTask()!;
        // 反复失败：到上限后该行被放弃，不再留在队列里。
        for (var i = 0; i < GenerationQueue.MaxAttempts; i++)
        {
            hub.FailGeneration(task, "网络不通");
            var again = hub.TakeGenerationTask();
            if (again == null)
                break;
            task = again;
        }
        Assert.False(state.SceneTexts.Has(task.Key, task.Step, task.Line));
    }

    [Fact]
    public void 存档落在生成途中_读档后剩下的行会继续排()
    {
        var (state, hub) = Setup();

        // 只完成第一个槽，其余还没生成。
        var first = hub.TakeGenerationTask()!;
        hub.CompleteGeneration(first, new[] { "先好的那一行。" });
        Assert.True(hub.PendingGenerationCount > 0);

        var json = SaveSystem.Save(state, hub);
        var snapshot = hub.Snapshot();
        var loaded = SaveSystem.Load(json);
        loaded.Voice.Scenes.Register(VisitorScene());
        loaded.Voice.Events.Register(VisitorEvent());
        var hub2 = new HubSession(loaded);
        hub2.Enter(1);
        hub2.Restore(snapshot);

        // 已生成的那一行不重排；没生成的继续排进队列。
        Assert.True(loaded.SceneTexts.Has(EventId, first.Step, first.Line));
        Assert.True(hub2.PendingGenerationCount > 0);

        // 补齐剩下的行，到点即可开演。
        Drain(hub2);
        loaded.Clock.SetTime(3, 11 * 60);
        Tick(hub2);
        Assert.True(hub2.ScenePlaying);
    }

    [Fact]
    public void 读档不重掷演员_人设随快照回来()
    {
        var (state, hub) = Setup();
        var stagedName = hub.StagedActors[EventId].Name;
        var stagedId = hub.StagedActors[EventId].Id;

        var json = SaveSystem.Save(state, hub);
        var snapshot = hub.Snapshot();
        var loaded = SaveSystem.Load(json);
        loaded.Voice.Scenes.Register(VisitorScene());
        loaded.Voice.Events.Register(VisitorEvent());
        var hub2 = new HubSession(loaded);
        hub2.Enter(1);
        hub2.Restore(snapshot);

        // 同一个人：名字与 Id 都不变，说明没重掷。
        Assert.Equal(stagedName, hub2.StagedActors[EventId].Name);
        Assert.Equal(stagedId, hub2.StagedActors[EventId].Id);
        // 人设补回了生成层，续生成才有身份语气可依。
        Assert.True(loaded.Voice.Generation.Personas.ContainsKey(stagedName));
    }

    [Fact]
    public void 内容表声明的事件与场景能解析出来()
    {
        var json = """
        {
          "scenes": [
            {
              "id": "visitor_arrival",
              "characters": ["访客"],
              "spawn": true,
              "dismissChoice": "decline",
              "steps": [
                { "id": "0", "lines": [
                  { "kind": "Speech", "generation": { "instruction": "说一句。", "lineCount": 1 } }
                ] }
              ]
            }
          ],
          "events": [
            { "id": "visitor_arrival", "title": "登门的访客", "trigger": "Scheduled",
              "sceneId": "visitor_arrival", "day": 3, "hour": 11, "once": true, "priority": 10 }
          ]
        }
        """;
        Assert.True(VoicePackJson.TryParse(json, out _, out _, out var scenes, out var events,
            out _, out var error), error);

        var scene = Assert.Single(scenes);
        Assert.True(scene.Spawn);
        Assert.Equal("decline", scene.DismissChoice);
        var line = Assert.Single(scene.Steps[0].Lines);
        Assert.True(line.NeedsGeneration);
        Assert.Equal("", line.Text);

        var ev = Assert.Single(events);
        Assert.Equal(HubEventTrigger.Scheduled, ev.Trigger);
        Assert.Equal("visitor_arrival", ev.SceneId);
        Assert.Equal(3, ev.Day);
        Assert.Equal(11, ev.Hour);
        Assert.True(ev.Once);
    }

    [Fact]
    public void 认不出的触发时机不静默变成别的时机()
    {
        var json = """
        {
          "events": [
            { "id": "bogus", "trigger": "NotARealTrigger", "sceneId": "whatever" }
          ]
        }
        """;
        Assert.True(VoicePackJson.TryParse(json, out _, out _, out _, out var events,
            out _, out var error), error);
        Assert.Empty(events);
    }

    [Fact]
    public void 内容就绪判定只看生成槽()
    {
        var (_, hub) = Setup();
        var scene = VisitorScene();

        Assert.False(hub.SceneContentReady(scene, EventId));
        Drain(hub);
        Assert.True(hub.SceneContentReady(scene, EventId));
    }
}
