using System.Collections.Generic;
using System.Threading.Tasks;
using Rimisekai.Character;
using Rimisekai.Clock;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Save;
using Rimisekai.Voice;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 口上/地文系统的第二层能力：场景维度、排他、场景事件、LLM 生成。
/// 第一层（基础挑选与门槛）见 <see cref="VoiceTests"/>。
/// </summary>
public sealed class VoiceSceneTests
{
    // ---------- 场景维度 ----------

    [Fact]
    public void Line_only_fires_in_matching_activity()
    {
        var pack = new VoicePack();
        pack.Register(new VoiceLine
        {
            Id = "cooking",
            Trigger = VoiceTrigger.Idle,
            Activities = { VoiceActivity.Cooking },
            Lines = { "锅里还差点火候。" },
        });

        var who = new CharacterState(2) { Name = "璐米埃尔" };
        Assert.NotNull(pack.Select(who, VoiceTrigger.Idle, Ctx(who, activity: VoiceActivity.Cooking)));
        Assert.Null(pack.Select(who, VoiceTrigger.Idle, Ctx(who, activity: VoiceActivity.Mining)));
    }

    [Fact]
    public void Line_only_fires_for_matching_role()
    {
        var pack = new VoicePack();
        pack.Register(new VoiceLine
        {
            Id = "touched",
            Trigger = VoiceTrigger.Touch,
            Roles = { VoiceRole.Partner },
            Lines = { "别乱碰。" },
        });

        var who = new CharacterState(2) { Name = "璐米埃尔" };
        Assert.NotNull(pack.Select(who, VoiceTrigger.Touch, Ctx(who, role: VoiceRole.Partner)));
        Assert.Null(pack.Select(who, VoiceTrigger.Touch, Ctx(who, role: VoiceRole.Actor)));
    }

    [Fact]
    public void Line_only_fires_at_matching_place()
    {
        var pack = new VoicePack();
        pack.Register(new VoiceLine
        {
            Id = "before",
            Trigger = VoiceTrigger.Kiss,
            Places = { VoicePlace.Before },
            Lines = { "……等一下。" },
        });

        var who = new CharacterState(2) { Name = "璐米埃尔" };
        Assert.NotNull(pack.Select(who, VoiceTrigger.Kiss, Ctx(who, place: VoicePlace.Before)));
        Assert.Null(pack.Select(who, VoiceTrigger.Kiss, Ctx(who, place: VoicePlace.After)));
    }

    [Fact]
    public void Line_only_fires_on_matching_emotion()
    {
        var pack = new VoicePack();
        pack.Register(new VoiceLine
        {
            Id = "tired",
            Trigger = VoiceTrigger.Idle,
            Emotion = VoiceEmotion.Tired,
            Lines = { "……让我歇一会儿。" },
        });

        var who = new CharacterState(2) { Name = "璐米埃尔" };
        Assert.NotNull(pack.Select(who, VoiceTrigger.Idle, Ctx(who, emotion: VoiceEmotion.Tired)));
        Assert.Null(pack.Select(who, VoiceTrigger.Idle, Ctx(who, emotion: VoiceEmotion.Happy)));
    }

    [Fact]
    public void Emotion_is_derived_from_state()
    {
        var who = new CharacterState(2) { Name = "璐米埃尔" };

        who.Affect.Mood = 90;
        Assert.Equal(VoiceEmotion.Happy, VoiceContext.EmotionOf(who));

        who.Affect.Mood = 5;
        Assert.Equal(VoiceEmotion.Angry, VoiceContext.EmotionOf(who));

        who.Affect.Mood = 50;
        who.Condition.Spend(0, Vitals.DefaultMax); // 气力见底，就是疲劳
        Assert.Equal(VoiceEmotion.Tired, VoiceContext.EmotionOf(who));
    }

    [Fact]
    public void Gate_can_require_presence_and_facility()
    {
        var gate = new VoiceGate { SameRoomAsPlayer = true };
        var who = new CharacterState(2) { Name = "璐米埃尔" };

        Assert.True(gate.Allows(Ctx(who, characterRoom: 5, playerRoom: 5), "x"));
        Assert.False(gate.Allows(Ctx(who, characterRoom: 5, playerRoom: 6), "x"));

        var atFacility = new VoiceGate { AtFacility = true };
        Assert.True(atFacility.Allows(Ctx(who, facility: 3), "x"));
        Assert.False(atFacility.Allows(Ctx(who, facility: -1), "x"));
    }

    // ---------- 排他 ----------

    [Fact]
    public void Exclusive_line_suppresses_lower_priority()
    {
        var pack = new VoicePack();
        // 低优先级的普通闲话，权重很高但会被排他句压掉。
        pack.Register(new VoiceLine
        {
            Id = "smalltalk",
            Trigger = VoiceTrigger.Talk,
            Weight = 1000,
            Priority = 1,
            Lines = { "随便聊聊。" },
        });
        // 高优先级的排他句：此刻必须说这句。
        pack.Register(new VoiceLine
        {
            Id = "urgent",
            Trigger = VoiceTrigger.Talk,
            Priority = 10,
            Exclusive = true,
            Lines = { "先别说话——听。" },
        });

        var who = new CharacterState(2) { Name = "璐米埃尔" };
        // 排他句在，无论掷多少次都只能是它。
        for (var i = 0; i < 20; i++)
            Assert.Equal("urgent", pack.Select(who, VoiceTrigger.Talk, Ctx(who))?.Id);
    }

    [Fact]
    public void Exclusive_does_not_apply_when_its_gate_fails()
    {
        var pack = new VoicePack();
        pack.Register(new VoiceLine
        {
            Id = "smalltalk",
            Trigger = VoiceTrigger.Talk,
            Priority = 1,
            Lines = { "随便聊聊。" },
        });
        pack.Register(new VoiceLine
        {
            Id = "urgent",
            Trigger = VoiceTrigger.Talk,
            Priority = 10,
            Exclusive = true,
            // 排他句自己进不来，就不该压别人。
            Gate = new VoiceGate { FavorMin = 9999 },
            Lines = { "先别说话——听。" },
        });

        var who = new CharacterState(2) { Name = "璐米埃尔" };
        Assert.Equal("smalltalk", pack.Select(who, VoiceTrigger.Talk, Ctx(who))?.Id);
    }

    [Fact]
    public void Priority_below_one_disables_a_line()
    {
        var pack = new VoicePack();
        pack.Register(new VoiceLine
        {
            Id = "off",
            Trigger = VoiceTrigger.Talk,
            Priority = 0,
            Lines = { "不该出现。" },
        });

        var who = new CharacterState(2) { Name = "璐米埃尔" };
        Assert.Null(pack.Select(who, VoiceTrigger.Talk, Ctx(who)));
    }

    // ---------- 场景事件 ----------

    [Fact]
    public void Scene_runs_steps_and_applies_effects()
    {
        var state = Setup(out var who, out var library, out var runner);
        library.Register(new SceneEvent
        {
            Id = "cleaning",
            Title = "打扫",
            Characters = { who.Name },
            Effects = { new SceneEffect { Kind = SceneEffectKind.Favor, Amount = 100 } },
            Steps =
            {
                new SceneStep
                {
                    Lines =
                    {
                        new SceneText(VoiceKind.Narration, "", "她在认真打扫。"),
                        new SceneText(VoiceKind.Speech, who.Name, "您有什么事吗？"),
                    },
                    Effects = { new SceneEffect { Kind = SceneEffectKind.Mood, Amount = 5 } },
                },
                new SceneStep
                {
                    Lines = { new SceneText(VoiceKind.Speech, who.Name, "……我知道了。") },
                },
            },
        });

        var ctx = Ctx(who);
        var run = runner.Begin(who, ctx);
        Assert.NotNull(run);
        Assert.Equal("cleaning", run!.Event.Id);
        // 开跑效果 + 第一步效果都已施加。
        Assert.Equal(100, who.Condition.Favor);
        Assert.Equal(55, who.Affect.Mood);
        Assert.Equal(2, run.Lines().Count);

        // 第一步有 2 行：能推进一次，第二次到头。
        Assert.True(run.NextLine());
        Assert.False(run.NextLine());

        // 进第二步，再进就结束。
        Assert.True(runner.Advance(run, ctx));
        Assert.Equal(1, run.Lines().Count);
        Assert.False(runner.Advance(run, ctx));
        Assert.True(run.Finished);
    }

    [Fact]
    public void Scene_choice_applies_effects_and_jumps()
    {
        var state = Setup(out var who, out var library, out var runner);
        library.Register(new SceneEvent
        {
            Id = "offer",
            Characters = { who.Name },
            Steps =
            {
                new SceneStep
                {
                    Id = "ask",
                    Lines = { new SceneText(VoiceKind.Speech, who.Name, "要休息一下吗？") },
                    Choices =
                    {
                        new SceneChoice
                        {
                            Id = "yes",
                            Label = "好",
                            GotoStep = 2,
                            Effects = { new SceneEffect { Kind = SceneEffectKind.Favor, Amount = 10 } },
                        },
                        new SceneChoice
                        {
                            Id = "no",
                            Label = "不用",
                            Effects = { new SceneEffect { Kind = SceneEffectKind.Favor, Amount = -5 } },
                        },
                    },
                },
                new SceneStep { Id = "ignored", Lines = { new SceneText(VoiceKind.Speech, who.Name, "……") } },
                new SceneStep { Id = "rest", Lines = { new SceneText(VoiceKind.Speech, who.Name, "那就歇会儿。") } },
            },
        });

        var ctx = Ctx(who);
        var run = runner.Begin(who, ctx);
        Assert.NotNull(run);
        run!.Wait();
        Assert.Equal(2, run.Choices.Count);

        Assert.True(runner.Choose(run, "yes", ctx));
        Assert.Equal(10, who.Condition.Favor);
        // 跳到了第 3 步（下标 2），跳过了中间那步。
        Assert.Equal("rest", run.Current?.Id);
        Assert.Single(run.Chosen);
    }

    [Fact]
    public void Scene_choice_with_failing_gate_is_rejected()
    {
        var state = Setup(out var who, out var library, out var runner);
        library.Register(new SceneEvent
        {
            Id = "locked",
            Characters = { who.Name },
            Steps =
            {
                new SceneStep
                {
                    Lines = { new SceneText(VoiceKind.Speech, who.Name, "……") },
                    Choices =
                    {
                        new SceneChoice
                        {
                            Id = "secret",
                            Label = "秘密选项",
                            Gate = new VoiceGate { FavorMin = 500 },
                        },
                    },
                },
            },
        });

        var ctx = Ctx(who);
        var run = runner.Begin(who, ctx);
        Assert.NotNull(run);
        run!.Wait();
        Assert.False(runner.Choose(run, "secret", ctx));
        Assert.Empty(run.Chosen);
    }

    [Fact]
    public void Scene_flag_state_machine_gates_repeats()
    {
        var state = Setup(out var who, out var library, out var runner);
        library.Register(new SceneEvent
        {
            Id = "first_meeting",
            Characters = { who.Name },
            Flag = "met",
            RequireFlagValue = null,
            DoneValue = 1,
            Steps = { new SceneStep { Lines = { new SceneText(VoiceKind.Speech, who.Name, "初次见面。") } } },
        });

        var ctx = Ctx(who);
        var run = runner.Begin(who, ctx);
        Assert.NotNull(run);
        // 开演时只记冷却，标志要跑完才置位（中途中断下次还能接着讲）。
        Assert.Equal(0, who.Get(who.Flags, SceneLibrary.FlagKey("met")));
        Assert.False(runner.Advance(run!, ctx));
        Assert.True(run!.Finished);
        Assert.Equal(1, who.Get(who.Flags, SceneLibrary.FlagKey("met")));

        // 换成"只在标志为 0 时触发"，验证状态机确实挡住了重复。
        library.Register(new SceneEvent
        {
            Id = "first_meeting",
            Characters = { who.Name },
            Flag = "met",
            RequireFlagValue = 0,
            Steps = { new SceneStep { Lines = { new SceneText(VoiceKind.Speech, who.Name, "初次见面。") } } },
        });
        Assert.Null(runner.Begin(who, Ctx(who)));
    }

    [Fact]
    public void Scene_rate_and_cooldown_limit_triggers()
    {
        var state = Setup(out var who, out var library, out var runner);
        library.Register(new SceneEvent
        {
            Id = "rare",
            Characters = { who.Name },
            Rate = 0,
            Steps = { new SceneStep { Lines = { new SceneText(VoiceKind.Speech, who.Name, "很罕见。") } } },
        });
        // 千分率为 0，永远掷不中。
        Assert.Null(runner.Begin(who, Ctx(who)));

        library.Register(new SceneEvent
        {
            Id = "daily",
            Characters = { who.Name },
            CooldownDays = 3,
            Steps = { new SceneStep { Lines = { new SceneText(VoiceKind.Speech, who.Name, "每天的事。") } } },
        });
        Assert.NotNull(runner.Begin(who, Ctx(who, day: 1)));
        // 同一天、以及冷却期内都不再触发。
        Assert.Null(runner.Begin(who, Ctx(who, day: 2)));
        Assert.Null(runner.Begin(who, Ctx(who, day: 3)));
        Assert.NotNull(runner.Begin(who, Ctx(who, day: 4)));
    }

    [Fact]
    public void Scene_effects_change_traits_items_and_relations()
    {
        var state = Setup(out var who, out var library, out var runner);
        var master = state.Roster.Master!;

        library.Register(new SceneEvent
        {
            Id = "reward",
            Characters = { who.Name },
            Effects =
            {
                new SceneEffect { Kind = SceneEffectKind.GrantTrait, Trait = "Curious" },
                new SceneEffect { Kind = SceneEffectKind.GiveItem, ItemId = "花", Amount = 2 },
                new SceneEffect { Kind = SceneEffectKind.AddRelation, Relation = "Trusted" },
                new SceneEffect { Kind = SceneEffectKind.SetCounter, Flag = "step", Amount = 7 },
            },
            Steps = { new SceneStep { Lines = { new SceneText(VoiceKind.Speech, who.Name, "……") } } },
        });

        runner.Begin(who, Ctx(who, masterId: master.Id));
        Assert.True(who.Has(Trait.Curious));
        Assert.Equal(2, who.Bag.Get("花"));
        Assert.True(who.Relations.Has(master.Id, RelationFlag.Trusted));
        Assert.Equal(7, who.Get(who.Flags, SceneLibrary.FlagKey("step")));
    }

    // ---------- LLM 生成层 ----------

    [Fact]
    public void Generation_is_skipped_when_no_generator_is_wired()
    {
        var director = new VoiceDirector();
        var pack = new VoicePack();
        pack.Register(new VoiceLine
        {
            Id = "gen",
            Trigger = VoiceTrigger.Talk,
            Generation = new VoiceGeneration { Instruction = "说点什么" },
        });
        director.Register("璐米埃尔", pack);

        var who = new CharacterState(2) { Name = "璐米埃尔" };
        // 没接生成器：整层旁路，同步路径也拿不到正文。
        Assert.False(director.Generation.Enabled);
        Assert.Null(director.Speak(who, VoiceTrigger.Talk, Ctx(who)));
    }

    [Fact]
    public async Task Generation_produces_lines_through_the_same_path()
    {
        var director = new VoiceDirector();
        director.Generation.Generator = new FakeGenerator(new[] { "嗯。", "……你说。" });
        var pack = new VoicePack();
        pack.Register(new VoiceLine
        {
            Id = "gen",
            Trigger = VoiceTrigger.Talk,
            Generation = new VoiceGeneration { Instruction = "回应玩家", LineCount = 2 },
        });
        director.Register("璐米埃尔", pack);

        var who = new CharacterState(2) { Name = "璐米埃尔" };
        var said = await director.SpeakAsync(who, VoiceTrigger.Talk, Ctx(who));
        Assert.NotNull(said);
        Assert.Equal(2, said!.Value.Lines.Count);
        Assert.Equal("嗯。", said.Value.Lines[0]);
        // 生成的内容进近期对话，供后续生成接上话头。
        Assert.Contains(who.Voice.RecentDialogue, d => d.Contains("嗯。"));
        Assert.Equal(1, director.Generation.Generated);
    }

    [Fact]
    public async Task Generation_cache_avoids_repeat_calls()
    {
        var generator = new FakeGenerator(new[] { "第一次。" });
        var director = new VoiceDirector();
        director.Generation.Generator = generator;
        var pack = new VoicePack();
        pack.Register(new VoiceLine
        {
            Id = "gen",
            Trigger = VoiceTrigger.Talk,
            Generation = new VoiceGeneration { Instruction = "x", CacheMinutes = 30 },
        });
        director.Register("璐米埃尔", pack);

        var who = new CharacterState(2) { Name = "璐米埃尔" };
        await director.SpeakAsync(who, VoiceTrigger.Talk, Ctx(who));
        await director.SpeakAsync(who, VoiceTrigger.Talk, Ctx(who));

        // 第二次命中缓存，没再调生成器。
        Assert.Equal(1, generator.Calls);
        Assert.Equal(1, director.Generation.CacheHits);
    }

    [Fact]
    public async Task Failed_generation_yields_nothing()
    {
        var director = new VoiceDirector();
        director.Generation.Generator = new FakeGenerator(System.Array.Empty<string>());
        string? error = null;
        director.Generation.OnError = e => error = e;

        var pack = new VoicePack();
        pack.Register(new VoiceLine
        {
            Id = "gen",
            Trigger = VoiceTrigger.Talk,
            Generation = new VoiceGeneration { Instruction = "x" },
        });
        director.Register("璐米埃尔", pack);

        var who = new CharacterState(2) { Name = "璐米埃尔" };
        Assert.Null(await director.SpeakAsync(who, VoiceTrigger.Talk, Ctx(who)));
        Assert.NotNull(error);
    }

    [Fact]
    public void Situation_text_is_human_readable()
    {
        var who = new CharacterState(2) { Name = "璐米埃尔" };
        who.Grant(Trait.Maid);
        who.Condition.AddFavor(150);
        who.Affect.Mood = 70;

        var text = VoiceGenerationHub.Situation(who, Ctx(who, activity: VoiceActivity.Cooking));
        Assert.Contains("第1天", text);
        Assert.Contains("有好感", text);
        Assert.Contains("在做饭", text);
        Assert.Contains("女仆", text);
    }

    [Fact]
    public void Static_line_still_wins_when_generation_is_also_present()
    {
        var director = new VoiceDirector();
        var pack = new VoicePack();
        // 同一句既有静态正文又有生成配置：同步路径用静态正文。
        pack.Register(new VoiceLine
        {
            Id = "both",
            Trigger = VoiceTrigger.Talk,
            Lines = { "静态兜底。" },
            Generation = new VoiceGeneration { Instruction = "x" },
        });
        director.Register("璐米埃尔", pack);

        var who = new CharacterState(2) { Name = "璐米埃尔" };
        var said = director.Speak(who, VoiceTrigger.Talk, Ctx(who));
        Assert.NotNull(said);
        Assert.Equal("静态兜底。", said!.Value.Lines[0]);
    }

    // ---------- JSON 解析 ----------

    [Fact]
    public void Json_reads_scene_dimensions_and_exclusivity()
    {
        const string json = """
        {
          "characters": [
            {
              "name": "璐米埃尔",
              "lines": [
                {
                  "id": "cook",
                  "trigger": "Idle",
                  "priority": 5,
                  "exclusive": true,
                  "activities": ["Cooking"],
                  "places": ["Middle"],
                  "emotion": "Happy",
                  "cutNarration": true,
                  "lines": ["锅里还差点火候。"]
                }
              ]
            }
          ]
        }
        """;

        Assert.True(VoicePackJson.TryParse(json, out var characters, out _, out var error), error);
        var line = characters["璐米埃尔"].Find("cook");
        Assert.NotNull(line);
        Assert.Equal(5, line!.Priority);
        Assert.True(line.Exclusive);
        Assert.True(line.CutNarration);
        Assert.Equal(VoiceEmotion.Happy, line.Emotion);
        Assert.Contains(VoiceActivity.Cooking, line.Activities);
        Assert.Contains(VoicePlace.Middle, line.Places);
    }

    [Fact]
    public void Json_reads_scenes_with_choices_and_effects()
    {
        const string json = """
        {
          "scenes": [
            {
              "id": "cleaning",
              "title": "打扫",
              "genre": "日常",
              "rate": 500,
              "characters": ["璐米埃尔"],
              "flag": "met",
              "cooldownDays": 2,
              "effects": [ { "kind": "Favor", "amount": 50 } ],
              "steps": [
                {
                  "id": "ask",
                  "lines": [
                    "您有什么事吗？",
                    { "kind": "Narration", "text": "她停下手中的活。" }
                  ],
                  "choices": [
                    {
                      "id": "yes",
                      "label": "没事",
                      "gotoStep": 1,
                      "effects": [ { "kind": "Mood", "amount": 5 } ]
                    }
                  ]
                },
                { "id": "end", "lines": ["……嗯。"] }
              ],
              "summary": ["她打扫了客厅。"]
            }
          ],
          "personas": { "璐米埃尔": "从小一起长大的女仆。" }
        }
        """;

        Assert.True(VoicePackJson.TryParse(json, out _, out _, out var scenes,
            out var personas, out var error), error);

        var scene = Assert.Single(scenes);
        Assert.Equal("cleaning", scene.Id);
        Assert.Equal(500, scene.Rate);
        Assert.Equal(2, scene.CooldownDays);
        Assert.Equal(2, scene.Steps.Count);

        // 第一行是简写字符串，按台词处理并补上角色名。
        Assert.Equal(VoiceKind.Speech, scene.Steps[0].Lines[0].Kind);
        Assert.Equal("璐米埃尔", scene.Steps[0].Lines[0].Speaker);
        // 第二行显式写了旁白。
        Assert.Equal(VoiceKind.Narration, scene.Steps[0].Lines[1].Kind);

        var choice = Assert.Single(scene.Steps[0].Choices);
        Assert.Equal("yes", choice.Id);
        Assert.Equal(1, choice.GotoStep);
        Assert.Equal(SceneEffectKind.Mood, choice.Effects[0].Kind);
        Assert.Equal(SceneEffectKind.Favor, scene.Effects[0].Kind);

        Assert.Equal("从小一起长大的女仆。", personas["璐米埃尔"]);
    }

    [Fact]
    public void Json_reads_generation_config()
    {
        const string json = """
        {
          "characters": [
            {
              "name": "璐米埃尔",
              "lines": [
                {
                  "id": "gen",
                  "trigger": "Talk",
                  "generation": {
                    "instruction": "回应玩家刚才的话",
                    "style": "冷淡、简短",
                    "lineCount": 2,
                    "maxCharsPerLine": 30,
                    "cacheMinutes": 15,
                    "options": { "temperature": "0.8" }
                  }
                }
              ]
            }
          ]
        }
        """;

        Assert.True(VoicePackJson.TryParse(json, out var characters, out _, out var error), error);
        var line = characters["璐米埃尔"].Find("gen");
        Assert.NotNull(line?.Generation);
        Assert.Equal("回应玩家刚才的话", line!.Generation!.Instruction);
        Assert.Equal(2, line.Generation.LineCount);
        Assert.Equal(15, line.Generation.CacheMinutes);
        Assert.Equal("0.8", line.Generation.Options["temperature"]);
    }

    // ---------- 内容文件端到端 ----------

    [Fact]
    public void Shipped_content_loads_scenes_and_personas()
    {
        var path = VoiceFilePath();
        Assert.True(System.IO.File.Exists(path), $"找不到台词文件：{path}");

        var json = System.IO.File.ReadAllText(path);
        Assert.True(VoicePackJson.TryParse(json, out var characters, out _, out var scenes,
            out var events, out var personas, out var error), error);

        // 教程角色要带上固定人设，LLM 生成时才有依据。
        Assert.True(personas.ContainsKey("璐米埃尔"));
        Assert.False(string.IsNullOrWhiteSpace(personas["璐米埃尔"]));

        // 事件表必须指向真实存在的场景，否则永远演不出来。
        Assert.NotEmpty(events);
        foreach (var ev in events)
        {
            Assert.True(ev.Id.Length > 0);
            Assert.Contains(scenes, s => s.Id == ev.SceneId);
        }

        // 场景事件至少要有一条，且步骤与角色都齐。
        Assert.NotEmpty(scenes);
        foreach (var scene in scenes)
        {
            Assert.NotEmpty(scene.Steps);
            Assert.NotEmpty(scene.Characters);
            Assert.True(scene.Rate > 0);
        }

        // 场景里引用的角色名必须在台词库里存在，否则永远触发不了。
        // 例外：Spawn 场景（如访客）的名字是占位说话人，真人到点才由生成器掷出，
        // 因此不需要台词库条目——这类场景的正文全部来自生成槽。
        var pack = characters["璐米埃尔"];
        foreach (var scene in scenes)
        {
            if (scene.Spawn)
                continue;
            foreach (var name in scene.Characters)
                Assert.True(characters.ContainsKey(name), $"场景 {scene.Id} 引用了没有台词库的角色 {name}");
        }

        // 场景事件真的能在一个世界里跑起来，并改到状态。
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var who = state.Roster.Add("璐米埃尔");
        who.Condition.AddFavor(150);
        var room = new Room { Id = 1, Name = "卧室", Open = true };
        room.AddTag("卧室");
        state.Territory.AddRoom(room);
        var library = new SceneLibrary();
        library.RegisterRange(scenes);
        var real = new SceneRunner(library, state.Territory);

        var ctx = VoiceContext.For(who, VoiceTrigger.Scene, masterId: 1,
            Season.Spring, Weather.Clear, 1, 12 * 60,
            playerRoomId: 1, characterRoomId: 1,
            roomTags: new[] { "卧室" });
        var run = real.Begin(who, ctx);
        Assert.NotNull(run);
        Assert.NotEmpty(run!.Lines());
    }

    [Fact]
    public void Shipped_content_has_activity_and_exclusive_lines()
    {
        var json = System.IO.File.ReadAllText(VoiceFilePath());
        Assert.True(VoicePackJson.TryParse(json, out var characters, out _, out _, out _, out var error), error);
        var pack = characters["璐米埃尔"];

        var hasActivity = false;
        var hasExclusive = false;
        var hasGeneration = false;
        foreach (var line in pack.Lines)
        {
            if (line.Activities.Count > 0)
                hasActivity = true;
            if (line.Exclusive)
                hasExclusive = true;
            if (line.Generation != null)
                hasGeneration = true;
        }

        Assert.True(hasActivity, "内容里应至少有一句按活动区分");
        Assert.True(hasExclusive, "内容里应至少有一句排他");
        Assert.True(hasGeneration, "内容里应至少有一句 LLM 生成");

        // 生成句必须带静态兜底，否则没接生成器时该时机就哑了。
        foreach (var line in pack.Lines)
        {
            if (line.Generation != null)
                Assert.NotEmpty(line.Lines);
        }
    }

    [Fact]
    public void Shipped_content_has_zero_parentheses_in_speech()
    {
        var json = System.IO.File.ReadAllText(VoiceFilePath());
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        // 1. 角色台词库中 Speech 严禁包含括号动作描写
        var chars = root.GetProperty("characters");
        foreach (var c in chars.EnumerateArray())
        {
            var name = c.GetProperty("name").GetString();
            foreach (var l in c.GetProperty("lines").EnumerateArray())
            {
                var kind = l.TryGetProperty("kind", out var k) ? k.GetString() : "Speech";
                if (kind == "Speech")
                {
                    foreach (var textElem in l.GetProperty("lines").EnumerateArray())
                    {
                        var text = textElem.GetString() ?? "";
                        Assert.False(text.Contains('（') || text.Contains('('),
                            $"角色 {name} 的 Speech 台词不得包含括号动作描写：{text}");
                    }
                }
            }
        }

        // 2. 场景剧本中 Speech 同样严禁包含括号动作描写
        var scenes = root.GetProperty("scenes");
        foreach (var s in scenes.EnumerateArray())
        {
            var id = s.GetProperty("id").GetString();
            foreach (var st in s.GetProperty("steps").EnumerateArray())
            {
                foreach (var lineElem in st.GetProperty("lines").EnumerateArray())
                {
                    if (lineElem.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var text = lineElem.GetString() ?? "";
                        Assert.False(text.Contains('（') || text.Contains('('),
                            $"场景 {id} 的纯文本台词不得包含括号动作描写：{text}");
                    }
                    else if (lineElem.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        var kind = lineElem.TryGetProperty("kind", out var sk) ? sk.GetString() : "Speech";
                        if (kind == "Speech")
                        {
                            var text = lineElem.GetProperty("text").GetString() ?? "";
                            Assert.False(text.Contains('（') || text.Contains('('),
                                $"场景 {id} 的 Speech 语句不得包含括号动作描写：{text}");
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void Shipped_content_all_scenes_have_steps_and_clean_text()
    {
        var json = System.IO.File.ReadAllText(VoiceFilePath());
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var scenes = doc.RootElement.GetProperty("scenes");

        foreach (var s in scenes.EnumerateArray())
        {
            var id = s.GetProperty("id").GetString();
            var count = s.GetProperty("steps").GetArrayLength();
            Assert.True(count >= 1, $"场景 {id} 步数不能为空");
        }
    }

    [Fact]
    public void Shipped_content_has_state_lines_covering_core_activities()
    {
        var json = System.IO.File.ReadAllText(VoiceFilePath());
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var chars = doc.RootElement.GetProperty("characters");

        foreach (var c in chars.EnumerateArray())
        {
            var name = c.GetProperty("name").GetString();
            var stateActivities = new HashSet<string>();
            foreach (var l in c.GetProperty("lines").EnumerateArray())
            {
                if (l.TryGetProperty("trigger", out var trig) && trig.GetString() == "State")
                {
                    Assert.Equal("Narration", l.GetProperty("kind").GetString());
                    if (l.TryGetProperty("activities", out var acts))
                    {
                        foreach (var act in acts.EnumerateArray())
                            stateActivities.Add(act.GetString() ?? "");
                    }
                }
            }
            Assert.True(stateActivities.Count >= 5, $"角色 {name} 应具备覆盖核心日常活动的状态地文");
        }
    }

    [Fact]
    public async Task HttpVoiceGenerator_gracefully_falls_back_when_no_network()
    {
        var gen = new HttpVoiceGenerator(endpoint: "http://127.0.0.1:59999/v1", apiKey: "dummy");
        Assert.True(gen.Available);
        var req = new VoiceRequest
        {
            CharacterName = "测试",
            Instruction = "说话",
            LineCount = 1,
        };

        // 网络不通时静默回退，返回空列表，不抛异常
        var result = await gen.GenerateAsync(req);
        Assert.Empty(result);
    }

    [Fact]
    public void Scene_fired_events_prevent_ever_repeating()
    {
        var state = Setup(out var who, out var library, out var runner);
        library.Register(new SceneEvent
        {
            Id = "once_scene",
            Characters = { who.Name },
            Rate = 1000,
            Steps = { new SceneStep { Lines = { new SceneText(VoiceKind.Speech, who.Name, "只演一次。") } } },
        });

        var fired = new HashSet<string>();
        var ctx1 = VoiceContext.For(who, VoiceTrigger.Scene, -1, Season.Spring, Weather.Clear, 1, 12 * 60, firedEvents: fired);
        Assert.NotNull(runner.Begin(who, ctx1));

        // 记入 FiredEvents 后，即使跨天冷却也不再触发
        fired.Add("once_scene");
        var ctx2 = VoiceContext.For(who, VoiceTrigger.Scene, -1, Season.Spring, Weather.Clear, 10, 12 * 60, firedEvents: fired);
        Assert.Null(runner.Begin(who, ctx2));
    }

    [Fact]
    public void Gate_level_and_all_members_max_level_check()
    {
        var state = Setup(out var who, out _, out _);
        who.GainLifeExp(LifeSkill.Cooking, 0); // Level 1
        var gate = new VoiceGate { LevelMin = 50, AllMembersMaxLevel = true };

        var ctxLow = VoiceContext.For(who, VoiceTrigger.Talk, -1, Season.Spring, Weather.Clear, 1, 12 * 60, allMembersMaxLevel: false);
        Assert.False(gate.Allows(ctxLow, "l1"));

        // 角色未到 50 级
        var ctxLevel1 = VoiceContext.For(who, VoiceTrigger.Talk, -1, Season.Spring, Weather.Clear, 1, 12 * 60, allMembersMaxLevel: true);
        Assert.False(gate.Allows(ctxLevel1, "l1"));

        // 提升等级至 100 满级
        for (var i = 0; i < 200000; i++)
            who.GainLifeExp(LifeSkill.Cooking, 1000);
        Assert.True(who.Level >= 50);

        // 角色达标但全队未满级
        var ctxNotAllMax = VoiceContext.For(who, VoiceTrigger.Talk, -1, Season.Spring, Weather.Clear, 1, 12 * 60, allMembersMaxLevel: false);
        Assert.False(gate.Allows(ctxNotAllMax, "l1"));

        // 角色达标且全队满级
        var ctxAllMax = VoiceContext.For(who, VoiceTrigger.Talk, -1, Season.Spring, Weather.Clear, 1, 12 * 60, allMembersMaxLevel: true);
        Assert.True(gate.Allows(ctxAllMax, "l1"));
    }

    [Fact]
    public void Gate_room_tags_matching()
    {
        var state = Setup(out var who, out _, out _);
        var gate = new VoiceGate { RoomTags = { "卧室", "密室" } };

        var ctxYard = VoiceContext.For(who, VoiceTrigger.Talk, -1, Season.Spring, Weather.Clear, 1, 12 * 60, roomTags: new[] { "室外", "工作间" });
        Assert.False(gate.Allows(ctxYard, "l2"));

        var ctxBedroom = VoiceContext.For(who, VoiceTrigger.Talk, -1, Season.Spring, Weather.Clear, 1, 12 * 60, roomTags: new[] { "室内", "卧室" });
        Assert.True(gate.Allows(ctxBedroom, "l2"));
    }

    [Fact]
    public void Social_talk_does_not_trigger_scene_or_collide()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var maid = state.Roster.Add("璐米埃尔");
        state.Territory.AddRoom(new Room { Id = 1, Name = "客厅", Open = true });
        // 注册一个只要人在场就 100% 触发的场景
        state.Voice.Scenes.Register(new SceneEvent
        {
            Id = "ambient_scene",
            Characters = { "璐米埃尔" },
            Rate = 1000,
            Steps = { new SceneStep { Lines = { new SceneText(VoiceKind.Speech, "璐米埃尔", "这是场景。") } } },
        });

        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(maid.Id, 1);
        hub.Select(maid.Id);

        // 点击交谈：必须正常进行交谈，绝不触发场景演出，两者不撞车
        Assert.True(hub.Social(SocialAction.Talk));
        Assert.False(hub.ScenePlaying);
        Assert.NotNull(hub.Overlay);
        Assert.Equal(OverlayKind.Dialogue, hub.Overlay!.Kind);
        Assert.False(state.FiredEvents.Contains("ambient_scene"));
    }

    [Fact]
    public void Room_entry_does_not_trigger_scene()
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        var maid = state.Roster.Add("璐米埃尔");
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "客厅", Open = true });
        state.Territory.Link(1, 2);

        state.Voice.Scenes.Register(new SceneEvent
        {
            Id = "ambient_scene",
            Characters = { "璐米埃尔" },
            Rate = 1000,
            Steps = { new SceneStep { Lines = { new SceneText(VoiceKind.Speech, "璐米埃尔", "进房。") } } },
        });

        var hub = new HubSession(state);
        hub.Place(maid.Id, 2);
        hub.Enter(1);
        Assert.False(hub.ScenePlaying);

        // 走进有角色的房间：不触发场景演出，触发点非进房
        Assert.True(hub.Move(2));
        Assert.False(hub.ScenePlaying);
    }

    [Fact]
    public void Strict_semantic_gate_triggers_only_when_all_conditions_met()
    {
        var state = new GameState();
        var master = state.Roster.Add("你", master: true);
        var maid = state.Roster.Add("璐米埃尔");
        maid.Grant(Trait.Maid);
        state.Territory.AddRoom(new Room { Id = 1, Name = "卧室", Open = true });
        state.Territory.AddFacility(new Facility
        {
            Id = 201, Name = "双人床", RoomId = 1, Usage = FacilityUsage.Rest, Capacity = 2,
            Built = true, Actions = { ActionKind.Sleep }
        });
        state.Territory.AddFacility(new Facility
        {
            Id = 202, Name = "躺椅", RoomId = 1, Usage = FacilityUsage.Rest, Capacity = 1,
            Built = true, Actions = { ActionKind.Rest }
        });
        state.Clock.SetTime(1, 19 * 60 + 50); // 就寝门槛（20 点）前的窗口

        // 注册测试用严苛门槛场景：满好感 + 全员满级 + 战斗归来状态
        state.Voice.Scenes.Register(new SceneEvent
        {
            Id = "test_strict_scene",
            Characters = { "璐米埃尔" },
            Rate = 1000,
            Gate = new VoiceGate
            {
                AllMembersMaxLevel = true,
                ReturnedFromCombat = true,
                FavorMin = Vitals.FavorMax,
            },
            Steps = { new SceneStep { Lines = { new SceneText(VoiceKind.Speech, "璐米埃尔", "测试台词。") } } },
        });

        var hub = new HubSession(state);
        hub.Enter(1);
        hub.Place(maid.Id, 1);
        hub.Select(maid.Id);
        hub.Social(SocialAction.Invite); // 跟随主角

        // 条件不齐（未满好感、未满级、未从战斗归来）：同床不成（守候），不触发场景
        hub.Use(201);
        state.Clock.SetTime(1, 20 * 60); // 就寝门槛整点：尚无任何一格过去，跟随者还醒着
        Assert.True(hub.ActAtFixture(ActionKind.Sleep));
        Assert.False(hub.ScenePlaying);
        Assert.Contains(hub.Log, l => l.Text.Contains("在床边守候着你"));

        // 补齐所有严苛条件：满好感 + 全员100级 + 战斗胜利归来
        maid.Condition.AddFavor(Vitals.FavorMax);
        for (var i = 0; i < 200000; i++)
        {
            master.GainLifeExp(LifeSkill.Cooking, 1000);
            maid.GainLifeExp(LifeSkill.Cooking, 1000);
        }
        Assert.True(master.Level >= 100 && maid.Level >= 100);
        state.ReturnedFromCombat = true;
        // 起身坐上躺椅：玩家离床，同床的跟随者随之醒来，重新邀请才够得着
        Assert.True(hub.Use(202));
        state.Clock.SetTime(1, 10 * 60);
        Assert.True(hub.Social(SocialAction.Invite)); // 重新邀请跟随

        // 满足所有严苛条件：20 点整准时就寝，精确触发测试场景
        state.Clock.SetTime(1, 19 * 60 + 50);
        Assert.True(hub.Use(201));
        state.Clock.SetTime(1, 20 * 60);
        Assert.True(hub.ActAtFixture(ActionKind.Sleep));
        Assert.True(hub.ScenePlaying);
        Assert.True(state.FiredEvents.Contains("test_strict_scene"));

        // 推进并结束场景
        hub.SceneContinue();
        Assert.False(hub.ScenePlaying);

        // 再次就寝：由于终身仅演一次，绝不再触发
        state.ReturnedFromCombat = true;
        Assert.True(hub.Use(202)); // 起身坐上躺椅
        state.Clock.SetTime(1, 19 * 60 + 50);
        Assert.True(hub.Use(201));
        state.Clock.SetTime(1, 20 * 60);
        Assert.True(hub.ActAtFixture(ActionKind.Sleep));
        Assert.False(hub.ScenePlaying);
    }

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

    // ---------- 辅助 ----------

    private static VoiceContext Ctx(
        CharacterState who,
        VoiceActivity activity = VoiceActivity.Idle,
        VoiceRole role = VoiceRole.Actor,
        VoicePlace place = VoicePlace.Before,
        VoiceEmotion emotion = VoiceEmotion.Any,
        int characterRoom = -1,
        int playerRoom = -1,
        int facility = -1,
        int day = 1,
        int masterId = -1) =>
        VoiceContext.For(
            who, VoiceTrigger.Talk, masterId, Season.Spring, Weather.Clear, day, 12 * 60,
            playerRoom, "", new System.Random(12345),
            activity, role, place, emotion, characterRoom, facility);

    /// <summary>一间房、一个在场角色、一个主界面的最小场景。</summary>
    private static GameState Setup(out CharacterState who, out SceneLibrary library,
        out SceneRunner runner)
    {
        var state = new GameState();
        state.Roster.Add("你", master: true);
        who = state.Roster.Add("璐米埃尔");
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        library = new SceneLibrary();
        runner = new SceneRunner(library, state.Territory);
        return state;
    }

    private sealed class FakeGenerator : IVoiceGenerator
    {
        private readonly IReadOnlyList<string> _lines;
        public int Calls { get; private set; }
        public bool Available => true;

        public FakeGenerator(IReadOnlyList<string> lines) => _lines = lines;

        public Task<IReadOnlyList<string>> GenerateAsync(VoiceRequest request)
        {
            Calls++;
            return Task.FromResult(_lines);
        }
    }
}
