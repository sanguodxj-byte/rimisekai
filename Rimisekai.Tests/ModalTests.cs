using System.Collections.Generic;
using Rimisekai.Combat;
using Rimisekai.Ink;
using Xunit;

namespace Rimisekai.Tests;

public sealed class ModalTests
{
    [Fact]
    public void ModalSession_enqueues_and_advances_pages()
    {
        var session = new InkModalSession();
        Assert.False(session.IsActive);

        var p1Advances = 0;
        var p2Advances = 0;

        var p1 = new InkModalPage { Body = "第一页", OnAdvance = () => p1Advances++ };
        var p2 = new InkModalPage { Body = "第二页", OnAdvance = () => p2Advances++ };

        session.Enqueue(p1);
        session.Enqueue(p2);

        Assert.True(session.IsActive);
        Assert.Same(p1, session.Current);

        var next1 = session.Advance();
        Assert.True(next1);
        Assert.Equal(1, p1Advances);
        Assert.Same(p2, session.Current);

        var next2 = session.Advance();
        Assert.False(next2);
        Assert.Equal(1, p2Advances);
        Assert.Null(session.Current);
        Assert.False(session.IsActive);
    }

    [Fact]
    public void Scenario1_OpeningQuestion_constructs_properly()
    {
        var chosen = "";
        var page = InkModalFactory.CreateQuestion(
            "测试问题",
            "测试问题描述内容",
            new[] { ("1", "选项一"), ("2", "选项二") },
            id => chosen = id);

        Assert.Equal("测试问题", page.Title);
        Assert.Equal("测试问题描述内容", page.Body);
        Assert.True(page.HasInteractiveControls);
        Assert.Equal(2, page.Choices.Count);

        page.Choices[0].OnSelected?.Invoke();
        Assert.Equal("1", chosen);
    }

    [Fact]
    public void Scenario1_InputQuestion_constructs_properly()
    {
        var submitted = "";
        var page = InkModalFactory.CreateInputQuestion(
            "测试命名",
            "测试输入提示内容",
            "（输入内容）",
            12,
            name => submitted = name);

        Assert.NotNull(page.Input);
        Assert.True(page.HasInteractiveControls);
        Assert.Single(page.Choices);

        page.Input!.Text = "测试文本";
        page.Choices[0].OnSelected?.Invoke();
        Assert.Equal("测试文本", submitted);
    }

    [Fact]
    public void Scenario2_CombatSettlement_constructs_properly()
    {
        var battle = new BattleResult
        {
            Outcome = CombatOutcome.AttackerWin,
            Rounds = 3,
        };
        battle.Rows.Add(new BattleResult.Row
        {
            CharacterId = 1,
            Name = "艾莉丝",
            Survived = true,
            DamageDealt = 80,
            Kills = 1,
            WeaponExp = 30,
            StyleExp = 15,
            Mood = 8,
        });

        var loot = new LootResult { Money = 100 };
        loot.Items.Add(("Herb", 3));

        var finished = false;
        var page = InkModalFactory.CreateCombatSettlement(battle, loot, () => finished = true);

        Assert.Equal("战斗胜利", page.Title);
        Assert.False(page.HasInteractiveControls); // 纯展示，允许点击任意处推进
        Assert.NotNull(page.Settlement);
        Assert.Equal(3, page.Settlement!.Rounds);
        Assert.Equal(100, page.Settlement.Money);
        Assert.Single(page.Settlement.Items);
        Assert.Equal("Herb", page.Settlement.Items[0].ItemId);

        page.OnAdvance?.Invoke();
        Assert.True(finished);
    }

    [Fact]
    public void Scenario3_Confirmation_constructs_properly()
    {
        var confirmed = false;
        var cancelled = false;

        var page = InkModalFactory.CreateConfirmation(
            "测试确认",
            "测试确认提示内容",
            () => confirmed = true,
            () => cancelled = true);

        Assert.Equal("测试确认", page.Title);
        Assert.True(page.HasInteractiveControls);
        Assert.Equal(2, page.Choices.Count);
        Assert.Equal("确定", page.Choices[0].Label);
        Assert.Equal("取消", page.Choices[1].Label);

        page.Choices[0].OnSelected?.Invoke();
        Assert.True(confirmed);
        Assert.False(cancelled);
    }

    [Fact]
    public void Scenario4_NarrativeSequence_constructs_properly()
    {
        var finished = false;
        var sequence = new List<InkModalPage>(InkModalFactory.CreateNarrativeSequence(new[]
        {
            "测试剧情段落第一行",
            "测试剧情段落第二行",
            "测试剧情段落第三行",
        }, "测试标题", () => finished = true));

        Assert.Equal(3, sequence.Count);
        foreach (var page in sequence)
        {
            Assert.Equal("测试标题", page.Title);
            Assert.False(page.HasInteractiveControls);
        }

        var session = new InkModalSession();
        session.EnqueueRange(sequence);

        Assert.True(session.Advance());
        Assert.True(session.Advance());
        Assert.False(session.Advance()); // 最后一页推进
        Assert.True(finished);
        Assert.False(session.IsActive);
    }
}
