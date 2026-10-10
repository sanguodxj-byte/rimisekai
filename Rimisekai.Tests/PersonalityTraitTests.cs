using System.Collections.Generic;
using System.Linq;
using Rimisekai.Character;
using Rimisekai.Housing;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 性格特质的游戏性通道：每条特质都要在某个系统挂点上产生可观测的修正。
/// 数值权威在 PersonalityTraits，这里只验证方向与挂点真的接通。
/// </summary>
public sealed class PersonalityTraitTests
{
    private static CharacterState With(params Trait[] traits)
    {
        var c = new CharacterState(1) { Name = "测试" };
        foreach (var t in traits)
            c.Grant(t);
        return c;
    }

    [Fact]
    public void Catalog_covers_every_trait_with_a_chinese_name()
    {
        // 57 条（40 光谱 + 17 机制）逐条都要有中文名与效果说明。
        var byTrait = Traits.Catalog.ToDictionary(d => d.Trait);
        Assert.Equal(76, byTrait.Count);
        foreach (var value in System.Enum.GetValues<Trait>())
        {
            Assert.True(byTrait.ContainsKey(value), $"{value} 缺目录条目");
            Assert.False(string.IsNullOrWhiteSpace(byTrait[value].Name));
        }
    }

    [Fact]
    public void Trait_effects_are_only_reached_through_the_gate()
    {
        // 统一入口守卫：核心系统代码不允许直接读 Trait 枚举做判定。
        var allowed = new HashSet<string> { "Traits.cs", "PersonalityTraits.cs", "CharacterGenerator.cs", "VoiceGenerationHub.cs" };
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "RimisekaiFrontend", "src")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var src = System.IO.Path.Combine(dir!.FullName, "RimisekaiFrontend", "src");
        var pattern = new System.Text.RegularExpressions.Regex(@"\bTrait\.[A-Z]");
        foreach (var file in System.IO.Directory.EnumerateFiles(src, "*.cs", System.IO.SearchOption.AllDirectories))
        {
            if (allowed.Contains(System.IO.Path.GetFileName(file)))
                continue;
            var text = System.IO.File.ReadAllText(file);
            Assert.True(!pattern.IsMatch(text), $"素质判定必须走 Traits 门面：{file}");
        }
    }

    [Fact]
    public void Work_traits_move_progress_both_ways()
    {
        var baseLine = Traits.WorkProgressPercent(With(), ActionKind.Mine, 12);
        Assert.Equal(100, baseLine);

        var dutiful = Traits.WorkProgressPercent(With(Trait.Dutiful), ActionKind.Mine, 12);
        Assert.True(dutiful > baseLine);

        var lazy = Traits.WorkProgressPercent(With(Trait.Procrastinator), ActionKind.Mine, 12);
        Assert.True(lazy < baseLine);

        // 夜猫子夜里快、白天慢；怕鬼只在夜里拖后腿。
        var owlNight = Traits.WorkProgressPercent(With(Trait.NightOwl), ActionKind.Mine, 23);
        var owlDay = Traits.WorkProgressPercent(With(Trait.NightOwl), ActionKind.Mine, 12);
        Assert.True(owlNight > owlDay);
        Assert.True(Traits.WorkProgressPercent(With(Trait.NightFearful), ActionKind.Mine, 23) < baseLine);
    }

    [Fact]
    public void Mood_scaling_respects_signs()
    {
        // 心情缩放只对正数生效，惩罚不放大。
        Assert.True(Traits.ScaledMood(With(Trait.Expressive), 5) > 5);
        Assert.Equal(-5, Traits.ScaledMood(With(Trait.Expressive), -5));
    }

    [Fact]
    public void Social_favor_kinds_reach_their_traits()
    {
        Assert.True(
            Traits.ScaledFavor(With(Trait.Talkative), SocialKind.Talk, 10) >
            Traits.ScaledFavor(With(Trait.Quiet), SocialKind.Talk, 10));
        Assert.True(
            Traits.ScaledFavor(With(Trait.Frugal), SocialKind.Gift, 10) > 10);
        Assert.True(
            Traits.ScaledFavor(With(Trait.Attentive), SocialKind.Care, 10) > 10);
        Assert.True(
            Traits.ScaledFavor(With(Trait.Arrogant), SocialKind.Intimate, 10) < 10);
    }

    [Fact]
    public void Protective_bonuses_only_in_a_happy_home()
    {
        var sad = With(Trait.Protective);
        var happy = With(Trait.Protective);
        happy.Affect.AddMood(20); // 心情过 60 才算“日子过得好”。
        Assert.Equal(10, Traits.ScaledFavor(sad, SocialKind.Talk, 10));
        Assert.True(Traits.ScaledFavor(happy, SocialKind.Talk, 10) > 10);
    }

    [Fact]
    public void Meal_and_learn_and_talk_hooks_fire()
    {
        Assert.True(Traits.MealMoodPercent(With(Trait.Foodie)) > 100);
        Assert.True(Traits.MealSpirit(With(Trait.Foodie), 60) > 60);

        Assert.True(With(Trait.Inquisitive).LearnPercent() > With().LearnPercent());
        Assert.True(With(Trait.Nervous).TalkDifficulty() > With().TalkDifficulty());
        Assert.True(With(Trait.Forthright).TalkDifficulty() < With().TalkDifficulty());
    }

    [Fact]
    public void Folded_legacy_traits_carry_real_effects()
    {
        // 桀骜：进度 -5、交谈难度 +1。
        Assert.True(Traits.WorkProgressPercent(With(Trait.Defiant), ActionKind.Mine, 12) < 100);
        Assert.True(With(Trait.Defiant).TalkDifficulty() > With().TalkDifficulty());

        // 怕痛拒重活。
        Assert.False(With(Trait.FearPain).WillWork(hardLabor: true));
        Assert.True(With(Trait.FearPain).WillWork(hardLabor: false));

        // 工匠手艺 +10、炼金 +15。
        Assert.True(Traits.WorkProgressPercent(With(Trait.Artisan), ActionKind.Forge, 12) > 100);
        Assert.True(Traits.WorkProgressPercent(With(Trait.Alchemist), ActionKind.Brew, 12) > 100);
    }

    [Fact]
    public void Three_layer_chain_attribute_type_action_holds()
    {
        // 力量 - 采掘 - 挖矿 / 力量 - 采掘 - 伐木（主人给的范例）。
        Assert.Equal(WorkType.Excavate, ActionKindMap.TypeOf(ActionKind.Mine));
        Assert.Equal(WorkType.Excavate, ActionKindMap.TypeOf(ActionKind.Fell));
        Assert.Equal(CoreStat.Strength, WorkTypeMap.CoreOf(WorkType.Excavate));

        // 社交类行动：表演、交易归社交（魅力）。
        Assert.Equal(WorkType.Social, ActionKindMap.TypeOf(ActionKind.Perform));
        Assert.Equal(WorkType.Social, ActionKindMap.TypeOf(ActionKind.Trade));
        Assert.Equal(LifeSkill.Social, ActionKindMap.SkillOf(ActionKind.Trade));
        Assert.Equal(CoreStat.Charm, WorkTypeMap.CoreOf(WorkType.Social));

        // 研究类行动：炼金归研究（智力）。
        Assert.Equal(WorkType.Research, ActionKindMap.TypeOf(ActionKind.Brew));
        Assert.Equal(CoreStat.Intellect, WorkTypeMap.CoreOf(WorkType.Research));

        // 每个行动都能追到工种与属性，没有悬空的行动。
        foreach (var action in ActionKindMap.WorkOrdered)
        {
            var type = ActionKindMap.TypeOf(action)!.Value;
            Assert.True(System.Enum.IsDefined(typeof(WorkType), type));
            Assert.True(System.Enum.IsDefined(typeof(CoreStat), WorkTypeMap.CoreOf(type)));
            Assert.True(System.Enum.IsDefined(typeof(LifeSkill), ActionKindMap.SkillOf(action)!.Value));
        }
    }

    [Fact]
    public void Facility_supports_only_declared_actions()
    {
        // 设施支持什么，只看它自己声明的 Actions；标签（usage）不参与判定。
        var forge = new Facility { Id = 1, Name = "铁砧", Usage = FacilityUsage.Plain };
        Assert.False(forge.Supports(ActionKind.Forge));
        forge.Actions.Add(ActionKind.Forge);
        Assert.True(forge.Supports(ActionKind.Forge));
        Assert.False(forge.Supports(ActionKind.Sleep));

        // 能存货的设施天然支持存取，不必逐件声明。
        var shelf = new Facility { Id = 2, Name = "货架", CanStore = true };
        Assert.True(shelf.Supports(ActionKind.Store));

        // 标签只剩三种纯角色：起居、消遣、摆设。
        Assert.Equal(3, System.Enum.GetValues<FacilityUsage>().Length);
    }

    [Fact]
    public void Alchemy_runs_on_research_so_intellect_drives_it()
    {
        // 炼金挂在研究技能上，而研究的主属性是智力。
        Assert.Equal(LifeSkill.Research, ActionKindMap.SkillOf(ActionKind.Brew));
        Assert.Equal(CoreStat.Intellect, AttributeMap.CoreOf(LifeSkill.Research));

        // 智力高的角色炼金更快，智力低的更慢。
        var smart = With();
        var dull = With();
        smart[CoreStat.Intellect] = 60;
        dull[CoreStat.Intellect] = 10;
        Assert.True(
            ActionKindMap.SpeedPercent(smart, ActionKind.Brew) >
            ActionKindMap.SpeedPercent(dull, ActionKind.Brew));

        // 其他任务不受智力影响（对照：采矿看力量）。
        Assert.Equal(
            ActionKindMap.SpeedPercent(smart, ActionKind.Mine),
            ActionKindMap.SpeedPercent(dull, ActionKind.Mine));
    }

    [Fact]
    public void Merged_aliases_behave_like_their_twins()
    {
        // 胆小≈易紧张、诚实≈直率、自负≈傲慢：并入后效果完全一致。
        Assert.Equal(
            With(Trait.Nervous).TalkDifficulty(),
            With(Trait.Timid).TalkDifficulty());
        Assert.Equal(
            Traits.ScaledMood(With(Trait.Forthright), 5),
            Traits.ScaledMood(With(Trait.Honest), 5));
        Assert.Equal(
            Traits.ScaledFavor(With(Trait.Arrogant), SocialKind.Intimate, 10),
            Traits.ScaledFavor(With(Trait.Prideful), SocialKind.Intimate, 10));
    }

    [Fact]
    public void Player_authored_mechanic_traits_are_untouched()
    {
        // 原有机制素质的数值不动：FastLearner 依旧是 +50，Cold/Prideful 依旧加交谈难度。
        Assert.Equal(150, With(Trait.FastLearner).LearnPercent());
        Assert.Equal(1, With(Trait.Cold).TalkDifficulty());
    }
}
