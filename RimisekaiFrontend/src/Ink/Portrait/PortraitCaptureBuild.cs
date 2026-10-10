using System.Linq;
using Rimisekai.Hub;

namespace Rimisekai.Portrait;

/// <summary>
/// 建造抽屉出图（build v3）：卧室起居、缺料、标签挡住、全部、刚建可撤、已有拆除、门、设施位满、山岳采集、
/// 空地选房与开拓并建造的详情卡、网格态撤销钮。放在整轮出图最后，前面各页的状态不受影响。
/// </summary>
public partial class PortraitCapture
{
    private const int BuildShotBedroom = 3;
    private const int BuildShotBedDef = 5;
    private int _buildShotFacilities;

    private void EnqueueBuildSheetShots()
    {
        _steps.Enqueue(() =>
        {
            var screen = _root.HubScreen;
            var hub = screen.DebugHub;
            hub.Enter(1);
            var master = hub.State.Roster.Master!;
            foreach (var item in new[] { "木材", "石材", "布" })
            {
                master.Bag.Add(item, -master.Bag.Get(item));
                foreach (var storage in hub.State.Territory.Storages)
                    storage.Contents.Add(item, -storage.Contents.Get(item));
            }
            master.Bag.Add("木材", 60);
            screen.ShowTab(0);
            screen.QueueRedraw();
        });
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.Build, 0));
        _steps.Enqueue(() => ClickLabel(PortraitAction.DevelopmentCell, "卧室"));
        _steps.Enqueue(() =>
        {
            var screen = _root.HubScreen;
            Require(screen.DebugWidgets.Count(w => w.Action == PortraitAction.BuildCategory) == 8
                && screen.DebugWidgets.Count(w => w.Action == PortraitAction.BuildSegment) == 3,
                "a built room opens three segments and eight facility tabs");
            Shoot("build_sheet_bedroom_living", screen);
            ClickLabel(PortraitAction.BuildTile, "长椅");
        });
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            var bedroom = hub.State.Territory.Room(BuildShotBedroom)!;
            var bench = hub.BuildOptions(0, bedroom.X, bedroom.Y).Single(o => o.Name == "长椅");
            Require(bench.State == BuildState.Short && _root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.BuildMain && !w.Enabled),
                "a bench without cloth is short and cannot be built");
            Shoot("build_sheet_short", _root.HubScreen);
            ClickLabel(PortraitAction.BuildCategory, "农牧");
        });
        _steps.Enqueue(() => ClickLabel(PortraitAction.BuildTile, "猪圈"));
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.BuildMain && !w.Enabled), "pigsty is locked indoors");
            Shoot("build_sheet_tag_locked", _root.HubScreen);
            ClickLabel(PortraitAction.BuildCategory, "生产");
        });
        _steps.Enqueue(() =>
        {
            Shoot("build_sheet_production", _root.HubScreen);
            ClickLabel(PortraitAction.BuildCategory, HubSession.AllCategoryLabel);
        });
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Count(w => w.Action == PortraitAction.BuildTile) == 16, "all tab fills a 4x4 screen");
            Shoot("build_sheet_all", _root.HubScreen);
            ClickLabel(PortraitAction.BuildCategory, "家具");
        });
        _steps.Enqueue(() => ClickLabel(PortraitAction.BuildTile, "床"));
        _steps.Enqueue(() =>
        {
            _buildShotFacilities = _root.HubScreen.DebugHub.State.Territory.FacilityCount(BuildShotBedroom);
            ClickHub(PortraitAction.BuildMain, 0);
        });
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            Require(hub.State.Territory.FacilityCount(BuildShotBedroom) == _buildShotFacilities + 1 && hub.CanUndoBuild
                && _root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.BuildUndo), "bed built with undo offered");
            Shoot("build_sheet_undo", _root.HubScreen);
            ClickHub(PortraitAction.BuildUndo, 0);
        });
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            Require(hub.State.Territory.FacilityCount(BuildShotBedroom) == _buildShotFacilities && !hub.CanUndoBuild
                && hub.State.Roster.Master!.Bag.Get("木材") == 60, "undo removes the bed and refunds the wood");
            ClickHub(PortraitAction.BuildSegment, 1);
        });
        _steps.Enqueue(() => ClickHub(PortraitAction.BuildTile, 1));
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.BuildMain && w.Label == "拆除" && w.Enabled),
                "existing facility offers demolish");
            Shoot("build_sheet_existing", _root.HubScreen);
            ClickHub(PortraitAction.BuildSegment, 2);
        });
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.DevelopmentDoor), "door segment lists doors");
            Shoot("build_sheet_doors", _root.HubScreen);
            var hub = _root.HubScreen.DebugHub;
            hub.State.Roster.Master!.Bag.Add("木材", 200);
            while (hub.State.Territory.HasFacilitySlot(BuildShotBedroom))
                Require(hub.BuildFacilityDef(BuildShotBedDef, BuildShotBedroom), "fill the bedroom");
            ClickHub(PortraitAction.BuildSegment, 0);
        });
        _steps.Enqueue(() =>
        {
            var screen = _root.HubScreen;
            var hub = screen.DebugHub;
            var bedroom = hub.State.Territory.Room(BuildShotBedroom)!;
            Require(hub.BuildOptions(0, bedroom.X, bedroom.Y).All(o => o.State == BuildState.Locked)
                && screen.DebugWidgets.Any(w => w.Action == PortraitAction.BuildMain && !w.Enabled), "a full room locks every facility");
            Shoot("build_sheet_full", screen);
            screen.DebugPress(PortraitAction.Back, 0);
        });
        _steps.Enqueue(() => ClickLabel(PortraitAction.DevelopmentCell, "山岳"));
        _steps.Enqueue(() => ClickLabel(PortraitAction.BuildCategory, "采集"));
        _steps.Enqueue(() =>
        {
            Shoot("build_sheet_mountain_gather", _root.HubScreen);
            _root.HubScreen.DebugPress(PortraitAction.Back, 0);
        });
        _steps.Enqueue(() => ClickLabel(PortraitAction.DevelopmentCell, "庭院"));
        _steps.Enqueue(() => ClickLabel(PortraitAction.BuildCategory, "农牧"));
        _steps.Enqueue(() =>
        {
            Shoot("build_sheet_courtyard_farm", _root.HubScreen);
            _root.HubScreen.DebugPress(PortraitAction.Back, 0);
        });
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            hub.State.Money = 100000;
            hub.State.Roster.Master!.Bag.Add("石材", 50);
            hub.State.Roster.Master!.Bag.Add("布", 20);
            ClickLabel(PortraitAction.DevelopmentCell, "空地");
        });
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Count(w => w.Action == PortraitAction.BuildCategory) == 6
                && _root.HubScreen.DebugWidgets.Count(w => w.Action == PortraitAction.BuildSegment) == 1, "a plot lists room tabs only");
            Shoot("build_sheet_plot_rooms", _root.HubScreen);
            ClickLabel(PortraitAction.BuildCategory, "商业");
        });
        _steps.Enqueue(() => ClickLabel(PortraitAction.BuildTile, "杂货铺"));
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.BuildMain && w.Label == "开拓并建造" && w.Enabled),
                "grocery can be opened and built in one step");
            Shoot("build_sheet_plot_card", _root.HubScreen);
            ClickHub(PortraitAction.BuildMain, 0);
        });
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugHub.CanUndoBuild, "open-and-build can be undone");
            _root.HubScreen.DebugPress(PortraitAction.Back, 0);
        });
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.BuildUndo), "grid dock offers undo");
            Shoot("build_sheet_grid_undo", _root.HubScreen);
        });
    }
}
