using System.Linq;
using Godot;
using Rimisekai.Housing;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 过界出图（排在全部既有核对之后、回到标题再开新局，不扰动前面各页的状态与几何）：
/// 中心区东连接点开一间空房、东区解锁并在其西连接点也开一间（两边各有房才通），主角点格走到东连接点 →
/// 边框缺口箭头（cross_arrow）→ 点箭头，镜头平移中间帧（cross_pan_mid）→ 落到东区西门，回头箭头朝西（cross_back）；
/// 再进最近的多块兴趣点，站到块间通道房（cross_poi）；最后回领地，走进北连接点（cross_north）。
/// </summary>
public partial class PortraitCapture
{
    private int _crossGate;
    private int _crossLanding;

    private void EnqueueCrossChecks()
    {
        _steps.Enqueue(() => PressTitle(PortraitLayout.TitleButton(0)));
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            var t = hub.State.Territory;
            var (ex, ey) = Territory.RegionGate(Territory.RegionDir.East);
            var (wx, wy) = Territory.RegionGate(Territory.RegionDir.West);
            Require(t.RoomAt(0, ex, ey) == null && t.RoomAt(0, ex - 1, ey) is { Open: true }, "east gate cell is vacant beside an open room");
            t.SetUnlockedRegionMask(t.UnlockedRegionMask | (1 << 2));
            _crossGate = AddVacant(hub, 0, ex, ey);
            _crossLanding = AddVacant(hub, 2, wx, wy);
            Require(hub.CrossTargetRegion(_crossGate) == 2 && hub.CrossDir(_crossGate) == Territory.RegionDir.East,
                "east gate crosses to the east region");
            _root.HubScreen.ShowTab(0);
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() =>
        {
            Require(!_root.HubScreen.DebugCrossArrow && !_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.CrossGate),
                "no border arrow away from a gate room");
            _root.HubScreen.DebugPress(PortraitAction.Cell, _crossGate);
        });
        _steps.Enqueue(() =>
        {
            var screen = _root.HubScreen;
            Require(screen.DebugHub.PlayerRoomId == _crossGate, "walked onto the east gate");
            CheckCrossArrow(2);
            Shoot("cross_arrow", screen);
            // 点箭头：过界当场生效，镜头平移在走、不收输入；推进到一小半截中间帧。
            PortraitMotion.Instant = false;
            screen.SetProcess(false);
            screen.DebugPress(PortraitAction.CrossGate, 2);
            var hub = screen.DebugHub;
            Require(hub.RegionId == 2 && hub.PlayerRoomId == _crossLanding && screen.DebugAnimating,
                "tapping the arrow crosses and starts the camera pan");
            screen._Process(0.1);
        });
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugAnimating && !_root.HubScreen.DebugCrossArrow, "no arrow while the camera pans");
            Shoot("cross_pan_mid", _root.HubScreen);
        });
        _steps.Enqueue(() =>
        {
            var screen = _root.HubScreen;
            Tap(screen, PortraitLayout.Cell(1, 1).GetCenter());
            screen._Process(1);
            Require(!screen.DebugAnimating && screen.DebugHub.PlayerRoomId == _crossLanding, "input ignored during the pan; pan settles");
            PortraitMotion.Instant = true;
            screen.SetProcess(true);
            screen.QueueRedraw();
        });
        _steps.Enqueue(() =>
        {
            CheckCrossArrow(0);
            Require(_root.HubScreen.DebugHub.CrossDir(_crossLanding) == Territory.RegionDir.West, "landing gate points back west");
            Shoot("cross_back", _root.HubScreen);
        });
        // 兴趣点：块与块之间走生成器的边界通道，箭头朝通道所在的边。
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            hub.EncounterRate = 0;
            var w = hub.State.World;
            var poi = w.Pois.Where(p => p.Type != Rimisekai.WorldMap.WorldPoiType.Village && p.Type != Rimisekai.WorldMap.WorldPoiType.Ruin
                    && p.Type != Rimisekai.WorldMap.WorldPoiType.Monastery)
                .OrderBy(p => System.Math.Abs(p.X - w.HomeX) + System.Math.Abs(p.Y - w.HomeY)).First();
            Require(hub.TravelToPoiDirect(poi.Id), "enter a multi-block settlement");
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            var gate = hub.Map().First(r => r.Open && hub.CrossTargetRegion(r.Id) >= 0 && (r.Id == hub.PlayerRoomId || hub.CanReach(r.Id)));
            if (gate.Id != hub.PlayerRoomId)
                _root.HubScreen.DebugPress(PortraitAction.Cell, gate.Id);
            _crossGate = gate.Id;
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            Require(hub.PlayerRoomId == _crossGate, "walked onto a block passage");
            CheckCrossArrow(hub.CrossTargetRegion(_crossGate));
            Shoot("cross_poi", _root.HubScreen);
        });
        // 北向：走出兴趣点、回到领地，北区解锁并在其南连接点开一间，主角走进中心区北连接点那间（开局的卧室）。
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.HubWorld, 0));
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugHub.Layer == Rimisekai.Hub.MapLayer.World, "left the settlement onto the world map");
            _root.HubScreen.DebugPress(PortraitAction.HubWorld, 0);
        });
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            Require(hub.Layer == Rimisekai.Hub.MapLayer.Territory && hub.RegionId == 0, "back home in the center region");
            var t = hub.State.Territory;
            var (nx, ny) = Territory.RegionGate(Territory.RegionDir.North);
            var (sx, sy) = Territory.RegionGate(Territory.RegionDir.South);
            var gate = t.RoomAt(0, nx, ny);
            Require(gate is { Open: true }, "north gate cell holds an open room");
            t.SetUnlockedRegionMask(t.UnlockedRegionMask | (1 << 1));
            AddVacant(hub, 1, sx, sy);
            _crossGate = gate!.Id;
            Require(hub.CrossTargetRegion(_crossGate) == 1 && hub.CrossDir(_crossGate) == Territory.RegionDir.North,
                "north gate crosses to the north region");
            if (hub.PlayerRoomId != _crossGate)
                _root.HubScreen.DebugPress(PortraitAction.Cell, _crossGate);
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugHub.PlayerRoomId == _crossGate, "walked onto the north gate");
            CheckCrossArrow(1);
            Shoot("cross_north", _root.HubScreen);
        });
    }

    /// <summary>领地里开一间空房（与 Core 开拓空格同一造法，连上已开放的四邻）。</summary>
    private static int AddVacant(Rimisekai.Hub.HubSession hub, int region, int x, int y)
    {
        var t = hub.State.Territory;
        var room = new Room
        {
            Id = t.Rooms.Max(r => r.Id) + 1, Name = "空房", RegionId = region, X = x, Y = y, Open = true, Vacant = true,
        };
        room.EnsureDefaultTag();
        if (!t.AddRoom(room))
            throw new System.InvalidOperationException($"空房装不进 {region}:{x},{y}");
        t.LinkNeighbors(room);
        return room.Id;
    }

    /// <summary>
    /// 箭头在、命中块凑足 118：外沿落在边框带及其外侧空白，往格内只伸进门房格（主人已接受与门房格重叠），
    /// 不碰别的房间格、日志面板与「此刻」带，指向 target 区。
    /// </summary>
    private void CheckCrossArrow(int target)
    {
        var screen = _root.HubScreen;
        var arrow = screen.DebugWidgets.Where(w => w.Action == PortraitAction.CrossGate).ToList();
        Require(screen.DebugCrossArrow && arrow.Count == 1 && arrow[0].Index == target && arrow[0].Enabled, $"border arrow to region {target}");
        var hit = arrow[0].Rect;
        var hub = screen.DebugHub;
        var room = hub.State.Territory.Room(hub.PlayerRoomId)!;
        var gateCell = PortraitLayout.Cell(room.X, room.Y);
        Require(Mathf.Min(hit.Size.X, hit.Size.Y) >= PortraitLayout.TouchMin - 0.01f, "border arrow hit reaches the 118 touch minimum");
        Require((!hit.Intersects(PortraitLayout.MapGrid) || gateCell.Encloses(hit.Intersection(PortraitLayout.MapGrid)))
            && !hit.Intersects(PortraitLayout.LogPanel)
            && hit.End.Y <= PortraitLayout.NowStrip.Position.Y + 0.01f && hit.Position.X >= 0f && hit.End.X <= PortraitLayout.CanvasWidth,
            "border arrow hit stays in the border band and the gate cell");
        Require(!screen.DebugWidgets.Any(w => w.Action != PortraitAction.CrossGate && w.Rect.Intersects(hit)
                && !(w.Action == PortraitAction.Cell && w.Index == room.Id)),
            "border arrow hit overlaps no target but the gate cell");
    }
}
