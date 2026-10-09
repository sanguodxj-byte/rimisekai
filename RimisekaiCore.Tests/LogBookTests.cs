using System.Linq;
using Rimisekai.Hub;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 两段式日志模型：a＝系统性信息、b＝以玩家为中心的感受或角色对玩家的反应，显示时合成一句「a，b」；
/// 快照按操作清空、历史跨操作保留；同一角色的同一句活动不重复入史。
/// 只碰 <see cref="LogBook"/>，不依赖内容表。
/// </summary>
public sealed class LogBookTests
{
    private static LogEntry E(LogKind kind, string fact, string feel = "") => new(kind, fact, feel, 1, 0);

    [Fact]
    public void Text_joins_fact_and_feel_with_a_chinese_comma()
    {
        Assert.Equal("细雨落在菜垄上，你闻到了泥土的气味。", E(LogKind.Weather, "细雨落在菜垄上。", "你闻到了泥土的气味。").Text);
        Assert.Equal("天气转为雨天。", E(LogKind.Weather, "天气转为雨天。").Text);
    }

    [Fact]
    public void Operation_snapshot_clears_per_operation_but_history_keeps_order()
    {
        var book = new LogBook();
        book.Write(E(LogKind.Action, "第一句。"));
        book.BeginOperation();
        book.Write(E(LogKind.Action, "第二句。"));
        Assert.Single(book.Operation);
        Assert.Equal(new[] { "第一句。", "第二句。" }, book.History.Select(l => l.Text));
    }

    [Fact]
    public void Snapshot_keeps_time_order_and_moves_a_newer_activity_to_its_place()
    {
        var book = new LogBook();
        book.Write(E(LogKind.Action, "你走出了领地。"));
        book.WriteActivity(2, E(LogKind.Activity, "赛琳在桌前计算符文配比。"));
        book.Write(E(LogKind.Scene, "你来到了工坊。"));
        book.Write(E(LogKind.Weather, "天气转为雨天。"));
        book.WriteActivity(2, E(LogKind.Activity, "赛琳放下了羽毛笔。"));
        Assert.Equal(new[] { "你走出了领地。", "你来到了工坊。", "天气转为雨天。", "赛琳放下了羽毛笔。" },
            book.Operation.Select(l => l.Text));
    }

    [Fact]
    public void Arrival_snapshot_and_history_rank_action_environment_characters()
    {
        var book = new LogBook();
        book.Write(E(LogKind.Action, "上一次操作。"));
        book.BeginOperation();
        book.Write(E(LogKind.Weather, "天气转为雨天。"));
        book.WriteActivity(2, E(LogKind.Activity, "赛琳在桌前计算符文配比。"));
        book.Write(E(LogKind.Scene, "你来到了工坊。"));
        book.WriteActivity(2, E(LogKind.Activity, "赛琳说「欢迎回来。」"));
        Assert.Equal(new[] { "你来到了工坊。", "天气转为雨天。", "赛琳说「欢迎回来。」" }, book.Operation.Select(l => l.Text));
        Assert.Equal(new[] { "上一次操作。", "你来到了工坊。", "天气转为雨天。", "赛琳在桌前计算符文配比。", "赛琳说「欢迎回来。」" },
            book.History.Select(l => l.Text));
    }

    [Fact]
    public void Repeated_activity_is_not_appended_to_history_twice()
    {
        var book = new LogBook();
        book.WriteActivity(2, E(LogKind.Activity, "赛琳在木椅上闭目歇息。"));
        book.BeginOperation();
        book.WriteActivity(2, E(LogKind.Activity, "赛琳在木椅上闭目歇息。"));
        book.BeginOperation();
        book.WriteActivity(2, E(LogKind.Activity, "赛琳在木床上睡着。"));
        Assert.Equal(2, book.History.Count);
        Assert.Single(book.Operation);
    }

    [Fact]
    public void History_is_capped_and_restore_replaces_it()
    {
        var book = new LogBook();
        for (var i = 0; i < LogBook.HistoryLimit + 5; i++)
            book.Write(E(LogKind.Action, $"第{i}句。"));
        Assert.Equal(LogBook.HistoryLimit, book.History.Count);
        Assert.Equal("第5句。", book.History[0].Text);

        book.Restore(new[] { E(LogKind.Scene, "你来到了庭院。", "璐米埃尔说「大人，请问有什么吩咐吗？」") });
        Assert.Single(book.History);
        Assert.Empty(book.Operation);
        Assert.Equal("你来到了庭院，璐米埃尔说「大人，请问有什么吩咐吗？」", book.History[0].Text);
    }
}
