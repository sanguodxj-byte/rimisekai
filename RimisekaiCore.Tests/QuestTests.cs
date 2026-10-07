using System;
using System.Linq;
using Rimisekai.Defs;
using Rimisekai.Quest;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 任务：难度星显示（半星镂空、超十星计数）、内容表装载、进度记录的可用性/冷却。
/// </summary>
public sealed class QuestTests
{
    private static QuestDef Def(double difficulty) => new()
    {
        DefName = "Quest_T",
        Id = 1,
        Label = "测试委托",
        Description = "描述",
        Kind = QuestKind.Map,
        Difficulty = difficulty,
        MaxPartySize = 4,
    };

    [Theory]
    [InlineData(0.5, "☆")]
    [InlineData(1, "★")]
    [InlineData(2, "★★")]
    [InlineData(3.5, "★★★☆")]
    [InlineData(10, "★★★★★★★★★★")]
    [InlineData(12, "★x12")]
    [InlineData(10.5, "★x11")]
    public void Difficulty_DisplaysHalfStarsAndCountBeyondTen(double difficulty, string expected)
    {
        Assert.Equal(expected, Def(difficulty).DifficultyText);
    }

    [Fact]
    public void DefLoader_RegistersQuestDefsFromJson()
    {
        DefDatabase<QuestDef>.Clear();
        var count = DefLoader.LoadJson("""
            { "defType": "QuestDef", "defs": [
                { "defName": "Quest_A", "id": 1, "label": "委托A", "kind": "map", "difficulty": 3.5,
                  "maxPartySize": 4, "rewards": ["金币 x10"], "rumor": "传言A" },
                { "defName": "Quest_B", "id": 2, "label": "委托B", "kind": "dialogue", "difficulty": 12,
                  "maxPartySize": 6 }
            ] }
            """);

        Assert.Equal(2, count);
        var a = DefDatabase<QuestDef>.All.Single(d => d.DefName == "Quest_A");
        Assert.Equal("委托A", a.Label);
        Assert.Equal(QuestKind.Map, a.Kind);
        Assert.Equal(3.5, a.Difficulty);
        Assert.Equal("传言A", a.Rumor);
        Assert.Equal(new[] { "金币 x10" }, a.Rewards);
        Assert.Equal(4, a.MaxPartySize);
        Assert.Equal("★★★☆", a.DifficultyText);
        Assert.Equal("★x12", DefDatabase<QuestDef>.All.Single(d => d.DefName == "Quest_B").DifficultyText);
    }

    [Fact]
    public void Record_CompletesOnceThenCoolsDown()
    {
        DefDatabase<QuestDef>.Clear();
        DefLoader.LoadJson("""
            { "defType": "QuestDef", "defs": [
                { "defName": "Quest_C", "id": 7, "label": "委托C", "kind": "dialogue",
                  "cooldownDays": 2, "difficulty": 1, "maxPartySize": 3 }
            ] }
            """);
        var record = new QuestRecord();

        Assert.True(record.IsAvailable(7));
        Assert.Null(record.Start(7, Array.Empty<int>()));

        var run = record.Start(7, new[] { 3, 4 });
        Assert.NotNull(run);
        record.Complete(run!);

        Assert.Equal(1, record.ClearCount[7]);
        Assert.Equal(2, record.CooldownRemaining[7]);
        Assert.False(record.IsAvailable(7));
        Assert.Null(record.Start(7, new[] { 3 }));

        record.TickDay();
        Assert.Equal(1, record.CooldownRemaining[7]);
        record.TickDay();
        Assert.True(record.IsAvailable(7));
    }
}
