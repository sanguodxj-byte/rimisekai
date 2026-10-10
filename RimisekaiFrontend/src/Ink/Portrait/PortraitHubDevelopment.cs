using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 建造推入页的网格态：上＝缺角双线框里的 5×5 开发网格（已开放实底、可开拓虚线＋加号、空房暗），
/// 下＝底座（建造常用的钱料计数；刚建的一笔可撤时多一枚撤销钮）。点一格进全高建造抽屉（见 PortraitHubBuildSheet）。
/// </summary>
public partial class PortraitHubScreen
{
    private InkDevModel DevelopmentModel(int confirmCell = -1)
    {
        var query = new InkPageQuery(-1, -1, -1, "", false, 0, 0, false, -1, -1, ConfirmCell: confirmCell);
        return InkPageBuilder.Build(_vm, InkPage.Develop, in query).Dev!;
    }

    private void DrawDevelopment()
    {
        if (BuildSheetOpen)
        {
            DrawBuildSheet();
            return;
        }
        var model = DevelopmentModel();
        PortraitFrame.GothicFrame(this, PortraitLayout.DevelopmentFrame, InkStyle.Bg);
        for (var y = 0; y < PortraitLayout.GridRows; y++)
            for (var x = 0; x < PortraitLayout.GridCols; x++)
                InkDraw.Ink(this, RectLoop(PortraitLayout.DevelopmentCell(x, y).Grow(-6f)), new Color(InkStyle.WoodDark, 0.5f), 2f);
        for (var i = 0; i < model.Rooms.Count; i++)
        {
            var cell = model.Rooms[i];
            var rect = PortraitLayout.DevelopmentCell(cell.X, cell.Y);
            var inner = rect.Grow(-6f);
            var plot = cell.Id < 0;
            var enabled = cell.Open || plot || cell.CanDevelop;
            var pressed = PortraitFrame.IsPressed(rect);
            if (cell.Open)
            {
                DrawRect(inner, pressed ? PortraitFrame.PressFill : InkStyle.Panel);
                InkDraw.Ink(this, RectLoop(inner), cell.Vacant ? InkStyle.WoodDark : InkStyle.Dim, 3f);
                InkDraw.TextStacked(this, inner.Grow(-10f), inner.Grow(-10f), cell.Name, PortraitLayout.FontMeta,
                    cell.Vacant ? InkStyle.Dim : InkStyle.Line);
            }
            else if (plot || cell.CanDevelop)
            {
                if (pressed)
                    DrawRect(inner, PortraitFrame.PressFill);
                DashedLoop(inner, InkStyle.Dim);
                PortraitGlyph.Plus(this, inner.GetCenter().X, inner.GetCenter().Y, 30f, InkStyle.Dim);
            }
            else if (cell.Name.Length > 0)
                InkDraw.TextStacked(this, inner.Grow(-10f), inner.Grow(-10f), cell.Name, PortraitLayout.FontMeta, InkStyle.WoodDark);
            _widgets.Add(new PortraitWidget(rect, PortraitAction.DevelopmentCell, i, enabled, cell.Name));
        }
        DrawDoors(PortraitLayout.DevelopmentCell, model.RegionId);

        var panel = PortraitLayout.DevelopmentPanel;
        PortraitFrame.Dock(this, new Rect2(panel.Position, panel.Size + new Vector2(0, 80f)));
        DrawBuildStock(PortraitLayout.DevelopmentStockY);
        if (_vm.Hub.CanUndoBuild)
        {
            var undo = PortraitLayout.DevelopmentUndo;
            PortraitFrame.Pill(this, undo, $"撤销 {_vm.Hub.LastBuildName}", glyph: PortraitGlyph.Back);
            _widgets.Add(new PortraitWidget(undo, PortraitAction.BuildUndo, 0, true, "撤销"));
        }
        DrawPageTop("建造", "", "完成", PortraitAction.BuildDone);
    }

    /// <summary>钱与建造常用料的计数签（钱、木材、石材：现有多少）。</summary>
    private void DrawBuildStock(float cy)
    {
        var hub = _vm.Hub;
        var master = hub.State.Roster.Master;
        var pairs = new List<(string, string)> { (HubSession.MoneyLabel, hub.State.Money.ToString()) };
        foreach (var item in new[] { HubSession.VacantCostItemId, HubSession.VacantCostStoneItemId })
            pairs.Add((item, hub.State.Territory.CountWith(master, item).ToString()));
        PortraitFrame.CountTags(this, PortraitLayout.Pad + 20f, cy, pairs, PortraitLayout.CanvasWidth - PortraitLayout.Pad);
    }

    private readonly record struct DoorRow(RoomDir Dir, string Neighbor, bool Open);

    /// <summary>选中房间四个朝向上、两边都已开放的邻房各一行门（北东南西序）。没选房间或房间不在网格上则为空。</summary>
    private List<DoorRow> DoorRows(int roomId)
    {
        var rows = new List<DoorRow>();
        var territory = _vm.Hub.State.Territory;
        if (territory.Room(roomId) is not { Open: true } room)
            return rows;
        foreach (var dir in Territory.RoomDirs)
            if (territory.NeighborAt(room, dir) is { Open: true } other)
                rows.Add(new DoorRow(dir, other.Name, room.Links.Contains(other.Id)));
        return rows;
    }

    private bool ExecuteDevelopment(PortraitWidget widget)
    {
        var hub = _vm.Hub;
        if (widget.Action == PortraitAction.DevelopmentCell)
        {
            var cell = DevelopmentModel().Rooms[widget.Index];
            var regionId = hub.RegionId;
            if (hub.SiteAt(regionId, cell.X, cell.Y) != BuildSite.None)
                OpenBuildSheet(regionId, cell.X, cell.Y);
            else if (cell.CanDevelop)
            {
                // 已有实体但没开放的房（开局表里预置的格）：照旧花它自己的钱料开放。
                var confirmation = DevelopmentModel(cell.Y * PortraitLayout.GridCols + cell.X);
                Confirm(confirmation.ConfirmTitle, confirmation.ConfirmBody, () =>
                {
                    hub.BeginOperation();
                    hub.DevelopEmptyRoom(cell.Id);
                    QueueRedraw();
                });
            }
            return true;
        }
        if (widget.Action == PortraitAction.DevelopmentDoor)
        {
            var room = hub.State.Territory.RoomAt(_buildRegion, _buildX, _buildY)!;
            var dir = (RoomDir)widget.Index;
            hub.SetDoor(room.Id, dir, !hub.State.Territory.DoorOpen(room, dir));
            return true;
        }
        return ExecuteBuildSheet(widget);
    }

    private IReadOnlyList<PortraitRegion> DevelopmentRegions() => BuildSheetOpen ? BuildSheetRegions() : new[]
    {
        new PortraitRegion("page_top", PortraitLayout.PageTop),
        new PortraitRegion("development_grid", PortraitLayout.DevelopmentFrame),
        new PortraitRegion("development_panel", PortraitLayout.DevelopmentPanel),
    };
}
