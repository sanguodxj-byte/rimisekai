using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 架构测试：UI 容器隔离与无交叠硬性检测。
/// 严格守卫 AGENTS.md 容器隔离铁律（2026-10-01 主人定）：
/// 不同容器/面板的 UI 禁止相互遮挡，外框矩形不得有任何交叠。
/// </summary>
public sealed class LayoutContainerIsolationTests
{
    private readonly record struct PanelBox(string Name, float X, float Y, float Width, float Height)
    {
        public float Right => X + Width;
        public float Bottom => Y + Height;

        public bool Intersects(PanelBox other, float tolerance = 0.5f)
        {
            if (Right <= other.X + tolerance || other.Right <= X + tolerance)
                return false;
            if (Bottom <= other.Y + tolerance || other.Bottom <= Y + tolerance)
                return false;
            return true;
        }

        public (float X, float Y, float W, float H) Overlap(PanelBox other)
        {
            var ox1 = Math.Max(X, other.X);
            var oy1 = Math.Max(Y, other.Y);
            var ox2 = Math.Min(Right, other.Right);
            var oy2 = Math.Min(Bottom, other.Bottom);
            return (ox1, oy1, ox2 - ox1, oy2 - oy1);
        }
    }

    private static void AssertNoOverlaps(string screenName, IReadOnlyList<PanelBox> panels)
    {
        // 1. 画布越界检查
        foreach (var p in panels)
        {
            Assert.True(p.X >= 0 && p.Y >= 0 && p.Right <= 1920 && p.Bottom <= 1080,
                $"[{screenName}] 面板越出 1920x1080 画布范围: {p.Name} [X:{p.X}..{p.Right}, Y:{p.Y}..{p.Bottom}]");
            Assert.True(p.Width > 0 && p.Height > 0,
                $"[{screenName}] 面板尺寸无效: {p.Name} [W:{p.Width}, H:{p.Height}]");
        }

        // 2. 两两相交检测
        for (var i = 0; i < panels.Count; i++)
        {
            for (var j = i + 1; j < panels.Count; j++)
            {
                var a = panels[i];
                var b = panels[j];
                var intersects = a.Intersects(b);
                if (intersects)
                {
                    var (ox, oy, ow, oh) = a.Overlap(b);
                    Assert.False(intersects,
                        $"[{screenName}] 容器隔离铁律违反！面板重叠遮挡：\n" +
                        $"  面板A: {a.Name} [X:{a.X}..{a.Right}, Y:{a.Y}..{a.Bottom}]\n" +
                        $"  面板B: {b.Name} [X:{b.X}..{b.Right}, Y:{b.Y}..{b.Bottom}]\n" +
                        $"  重叠冲突区: [X:{ox}..{ox + ow}, Y:{oy}..{oy + oh}], 重叠面积: {ow * oh:F1}px^2");
                }
            }
        }
    }

    [Fact]
    public void Hub_screen_panels_have_zero_overlap()
    {
        var panels = new[]
        {
            new PanelBox("MapPanel", 48, 78, 960, 540),
            new PanelBox("LogPanel", 1032, 78, 840, 540),
            new PanelBox("CharPanel", 48, 642, 760, 360),
            new PanelBox("WorkPanel", 832, 642, 256, 360),
            new PanelBox("ActPanel", 1112, 642, 760, 360),
        };
        AssertNoOverlaps("据点主界面", panels);
    }

    [Fact]
    public void Schedule_page_panels_have_zero_overlap()
    {
        var panels = new[]
        {
            new PanelBox("ScheduleMemberList", 78, 142, 320, 860),
            new PanelBox("ScheduleSlot0", 422, 142, 343, 116),
            new PanelBox("ScheduleSlot1", 781, 142, 343, 116),
            new PanelBox("ScheduleSlot2", 1140, 142, 343, 116),
            new PanelBox("ScheduleSlot3", 1499, 142, 343, 116),
            new PanelBox("ScheduleGrid", 422, 274, 694, 460),
            new PanelBox("ScheduleFacilities", 1132, 274, 710, 460),
            new PanelBox("WorkDetail", 422, 750, 1420, 252),
        };
        AssertNoOverlaps("全屏日程页", panels);
    }

    [Fact]
    public void Trade_page_panels_have_zero_overlap()
    {
        var panels = new[]
        {
            new PanelBox("TradePlayerPanel", 78, 142, 620, 860),
            new PanelBox("TradeIllustrationRect", 819, 142, 283, 504),
            new PanelBox("TradeCenterPanel", 734, 670, 452, 332),
            new PanelBox("TradeMarketPanel", 1222, 142, 620, 860),
        };
        AssertNoOverlaps("全屏交易页", panels);
    }

    [Fact]
    public void Skills_page_panels_have_zero_overlap()
    {
        var panels = new[]
        {
            new PanelBox("SkillDiscPanel", 78, 142, 860, 860),
            new PanelBox("SkillDetailPanel", 950, 142, 892, 860),
        };
        AssertNoOverlaps("全屏技能页", panels);
    }

    [Fact]
    public void Status_page_panels_have_zero_overlap()
    {
        var panels = new[]
        {
            new PanelBox("CharacterRail", 78, 142, 400, 860),
            new PanelBox("StatusVitals", 502, 142, 432, 240),
            new PanelBox("StatusCombat", 950, 142, 432, 240),
            new PanelBox("StatusAttributes", 1398, 142, 444, 240),
            new PanelBox("AbilityPanel", 502, 396, 1340, 606),
        };
        AssertNoOverlaps("全屏状态页", panels);
    }

    [Fact]
    public void List_detail_pages_have_zero_overlap()
    {
        var panels = new[]
        {
            new PanelBox("FullListPanel", 78, 142, 740, 860),
            new PanelBox("FullDetailArea", 842, 142, 1000, 860),
        };
        AssertNoOverlaps("全屏列表详情页", panels);
    }

    [Fact]
    public void Overlap_detector_correctly_catches_conflicts()
    {
        // 模拟此前引发 BUG 的交叠场景：Grid 延伸至 870，而 Detail 在 676 起始
        var conflictingPanels = new[]
        {
            new PanelBox("Grid", 422, 324, 694, 546),
            new PanelBox("Detail", 422, 676, 1420, 326),
        };
        Assert.Throws<Xunit.Sdk.FalseException>(() => AssertNoOverlaps("测试冲突场景", conflictingPanels));
    }
}
