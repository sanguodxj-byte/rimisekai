using System.Collections.Generic;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

public partial class PortraitHubScreen
{
    public IReadOnlyList<PortraitRegion> DebugVisualRegions
    {
        get
        {
            var regions = new List<PortraitRegion> { new("header", PortraitLayout.Header) };
            if (OverlayActive)
            {
                if (_page == InkPage.Schedule && _systemPage.Length == 0 && !_questMode && !_vm.StorageOpen)
                    regions.AddRange(ScheduleRegions());
                else if (_page == InkPage.Develop && _systemPage.Length == 0 && !_questMode && !_vm.StorageOpen)
                    regions.AddRange(DevelopmentRegions());
                else if (_page == InkPage.Status && _systemPage.Length == 0 && !_questMode && !_vm.StorageOpen)
                    regions.AddRange(StatusRegions());
                else if (_page == InkPage.Skills && _systemPage.Length == 0 && !_questMode && !_vm.StorageOpen)
                    regions.AddRange(SkillRegions());
                else
                {
                    regions.Add(new PortraitRegion("back", PortraitLayout.OverlayBack));
                    foreach (var area in _scrollAreas)
                        regions.Add(new PortraitRegion(area.Id, area.Area));
                    if (_page == InkPage.Trade)
                        regions.Add(new PortraitRegion("trade_action", PortraitLayout.TradeRunRow));
                    else if (_systemPage == InkSystemScreen.PageSave)
                        regions.Add(new PortraitRegion("save_action", PortraitLayout.SystemSaveRow));
                    else if (_systemPage == InkSystemScreen.PageSettings || _questMode)
                    {
                        regions.RemoveRange(1, regions.Count - 1);
                        regions.Add(new PortraitRegion("content", PortraitLayout.Content));
                    }
                    else if (_page != InkPage.None && !_vm.StorageOpen)
                    {
                        var count = PageModel().DetailActions.Count;
                        for (var i = 0; i < count; i++)
                            regions.Add(new PortraitRegion($"page_action_{i}", RowAt(RowsTop +
                                (PortraitLayout.OverlayRows - count + i) * PortraitLayout.RowHeight, 0)));
                    }
                }
            }
            else if (ConversationActive)
            {
                // 对话只把地图网格区换成对话框，下方头像带与设施栏仍常显。
                regions.Add(new PortraitRegion("conversation_box", PortraitLayout.ConversationBox));
                regions.Add(new PortraitRegion("avatars", PortraitLayout.AvatarArea));
                regions.Add(new PortraitRegion("fixtures", PortraitLayout.FixtureArea));
            }
            else if (_interactionOpen)
            {
                regions.Add(new PortraitRegion("back", PortraitLayout.OverlayBack));
                regions.Add(new PortraitRegion("interaction", PortraitLayout.InteractionList));
            }
            else if (_tab == 0)
            {
                regions.Add(new PortraitRegion("map_grid", PortraitLayout.GridArea));
                regions.Add(new PortraitRegion("avatars", PortraitLayout.AvatarArea));
                regions.Add(new PortraitRegion("fixtures", PortraitLayout.FixtureArea));
            }
            else
                regions.Add(new PortraitRegion("content", PortraitLayout.Content));
            regions.Add(new PortraitRegion("tabs", PortraitLayout.TabBar));
            return regions;
        }
    }
}
