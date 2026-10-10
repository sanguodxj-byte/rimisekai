using System.Collections.Generic;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 运行时隔离检查读的分区：每个画面态一组互不交叠的矩形，命中块必须落在某一区内。
/// 抽屉打开时只剩「压暗区（点了收起）＋抽屉面板」两区——下层命中块此时已整体移除。
/// </summary>
public partial class PortraitHubScreen
{
    public IReadOnlyList<PortraitRegion> DebugVisualRegions
    {
        get
        {
            if (_sheetTop >= 0f)
                return new[]
                {
                    new PortraitRegion("sheet_backdrop", new Godot.Rect2(0, 0, PortraitLayout.CanvasWidth, _sheetTop)),
                    new PortraitRegion("sheet", new Godot.Rect2(0, _sheetTop, PortraitLayout.CanvasWidth,
                        PortraitLayout.CanvasHeight - _sheetTop)),
                };
            if (_push == PushPage.Build)
                return DevelopmentRegions();
            if (_push != PushPage.None)
                return new[]
                {
                    new PortraitRegion("page_top", PortraitLayout.PageTop),
                    new PortraitRegion("page_body", PortraitLayout.PageBody),
                };
            if (ConversationActive)
                return new[] { new PortraitRegion("scene", new Godot.Rect2(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight)) };
            return new[]
            {
                new PortraitRegion("hud", PortraitLayout.Hud),
                new PortraitRegion("body", PortraitLayout.Body),
                new PortraitRegion("tabs", PortraitLayout.TabBar),
            };
        }
    }
}
