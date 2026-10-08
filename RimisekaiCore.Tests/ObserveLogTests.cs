using System.Linq;
using Rimisekai.Defs;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Save;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 观察四周的描述日志与排版顺序：
/// 1. 描述原文接到打量后，打量着xx后面永远是逗号（例如“你打量着庭院，家门口的院子。”）。
/// 2. 日志快照按写入先后（即按时间）排。
/// </summary>
public sealed class ObserveLogTests
{
    private static HubSession NewHub(string roomName)
    {
        DefLoader.EnsureInitialized();
        var state = new GameState();
        state.Roster.Add("领主", master: true);
        state.Territory.AddRoom(new Room { Id = 1, Name = roomName, Open = true });
        var hub = new HubSession(state);
        hub.Enter(1);
        return hub;
    }

    [Fact]
    public void Observe_writes_room_description_appended_after_comma()
    {
        var hub = NewHub("庭院");

        Assert.True(hub.Act(PlaceAction.Observe));
        // 描述接在打量着后面，打量着xx后面永远是逗号，且为高质量具象场景细节
        Assert.Contains(hub.Log, l => l.Text == "你打量着庭院，青石板路的中央立着一口带木篷的水井，回廊尖拱下放着歇脚的躺椅，石墙上的铁壁灯照亮了四周。");
    }

    [Fact]
    public void Observe_without_matching_def_writes_only_the_glance_line_with_comma()
    {
        var hub = NewHub("无名屋");

        Assert.True(hub.Act(PlaceAction.Observe));
        // 查不到定义时只写打量行，且打量着xx后面永远是逗号
        Assert.Contains(hub.Log, l => l.Text == "你打量着无名屋，");
    }

    [Fact]
    public void Log_snapshot_follows_write_order()
    {
        var hub = NewHub("庭院");

        hub.BeginOperation();
        hub.Write("你打量着庭院，家门口的院子。"); // 玩家行动
        hub.WriteActivity(2, "赛琳在厨房做饭。"); // 其他角色行动
        hub.WriteEnvironment("天气转为暴雨。"); // 环境变化

        Assert.Equal(new[] { "你打量着庭院，家门口的院子。", "赛琳在厨房做饭。", "天气转为暴雨。" },
            hub.Log.Select(l => l.Text));
    }
}
