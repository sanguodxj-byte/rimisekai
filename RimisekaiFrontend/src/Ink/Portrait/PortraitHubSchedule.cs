using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Housing;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

public partial class PortraitHubScreen
{
    private int _scheduleMemberId = -1;
    private int _scheduleSlot;
    private int _scheduleRoomId = -1;
    private int _scheduleMemberFirst;
    private int _scheduleFacilityFirst;
    private int _scheduleOutputFirst;

    private void OpenSchedule()
    {
        _scheduleMemberId = _vm.Hub.State.Roster.Master!.Id;
        _scheduleRoomId = _vm.Hub.PlayerRoomId;
        _scheduleSlot = 0;
        _scheduleMemberFirst = _scheduleFacilityFirst = _scheduleOutputFirst = 0;
    }

    private InkPageModel ScheduleModel() => InkCharacterPageBuilder.Build(_vm, InkPage.Schedule,
        _vm.Hub.State.Roster.Find(_scheduleMemberId)!, selected: _scheduleSlot,
        roomId: _scheduleRoomId, facilityFirst: _scheduleFacilityFirst, memberFirst: _scheduleMemberFirst)!;

    private void DrawSchedule()
    {
        var model = ScheduleModel();
        var work = model.Work!;
        DrawPageTop(model.Title);
        DrawBack();
        var memberRowHeight = Math.Max(PortraitLayout.RowHeight, work.Members.Max(row =>
            InkDraw.WrapLines(_vm.Hub.State.Roster.Find(row.TargetNumber)!.Name,
                PortraitLayout.ScheduleMembers.Size.X - PortraitLayout.TouchMin - 44f, PortraitLayout.FontMeta).Count)
            * PortraitLayout.FontMeta + 24f);
        var memberVisible = (int)(PortraitLayout.ScheduleMembers.Size.Y / memberRowHeight);
        _scheduleMemberFirst = Math.Clamp(_scheduleMemberFirst, 0,
            Math.Max(0, work.Members.Count - memberVisible));
        for (var i = 0; i < memberVisible && i + _scheduleMemberFirst < work.Members.Count; i++)
        {
            var row = work.Members[i + _scheduleMemberFirst];
            var name = _vm.Hub.State.Roster.Find(row.TargetNumber)!.Name;
            var rect = PortraitLayout.ScrolledRow(PortraitLayout.ScheduleMembers, i, memberRowHeight, work.Members.Count > memberVisible);
            PortraitFrame.Button(this, rect, name, selected: row.TargetNumber == _scheduleMemberId);
            _widgets.Add(new PortraitWidget(rect, PortraitAction.ScheduleMember, row.TargetNumber, row.Enabled, name));
        }
        RegisterScroll("schedule_members", PortraitLayout.ScheduleMembers, work.Members.Count,
            memberVisible, _scheduleMemberFirst, first => _scheduleMemberFirst = first, memberRowHeight);

        for (var slot = 0; slot < WorkSlot.Count; slot++)
        {
            var row = model.Rows[slot];
            var rect = PortraitLayout.ScheduleSlot(slot);
            var top = PortraitLayout.ScheduleSlotTop(slot);
            var bottom = PortraitLayout.ScheduleSlotBottom(slot);
            var assignment = _vm.Hub.AssignmentOf(_scheduleMemberId, slot);
            PortraitFrame.Button(this, rect, "", selected: slot == _scheduleSlot);
            InkDraw.TextBounded(this, top.Grow(-12f), row.Name, PortraitLayout.FontMeta,
                PortraitLayout.FontMeta, InkStyle.Line, "cm");
            var activity = assignment.FacilityId >= 0 ? _vm.Hub.FacilityName(assignment.FacilityId) : row.Value;
            InkDraw.TextBounded(this, bottom.Grow(-12f), activity, PortraitLayout.FontBody,
                PortraitLayout.FontMeta, InkStyle.Line, "cm");
            _widgets.Add(new PortraitWidget(top, PortraitAction.ScheduleSlot, slot, row.Enabled, row.Name));
            _widgets.Add(new PortraitWidget(bottom, PortraitAction.ScheduleSlot, slot, row.Enabled, row.Name));
            if (assignment.FacilityId >= 0)
            {
                var cancel = PortraitLayout.ScheduleCancel(slot);
                PortraitFrame.Button(this, cancel, "取消");
                _widgets.Add(new PortraitWidget(cancel, PortraitAction.ScheduleCancel, slot, true, "取消"));
            }
        }

        foreach (var room in work.Rooms)
        {
            var rect = PortraitLayout.ScheduleCell(room.X, room.Y);
            PortraitFrame.Button(this, rect, room.Name, selected: room.Id == _scheduleRoomId);
            _widgets.Add(new PortraitWidget(rect, PortraitAction.ScheduleRoom, room.Id, true, room.Name));
        }

        var facilities = work.Facilities.Where(row => _vm.Hub.FacilityIsWorkbench(row.TargetNumber)).ToArray();
        _scheduleFacilityFirst = Math.Clamp(_scheduleFacilityFirst, 0, Math.Max(0, facilities.Length - 2));
        for (var i = 0; i < 2 && i + _scheduleFacilityFirst < facilities.Length; i++)
        {
            var row = facilities[i + _scheduleFacilityFirst];
            var rect = PortraitLayout.ScrolledRow(PortraitLayout.ScheduleFacilities, i, hasScroll: facilities.Length > 2);
            PortraitFrame.Row(this, rect, row.Name, row.Value, row.TargetNumber == work.AssignedFacilityId);
            _widgets.Add(new PortraitWidget(rect, PortraitAction.ScheduleFacility, row.TargetNumber, row.Enabled, row.Name));
        }
        RegisterScroll("schedule_facilities", PortraitLayout.ScheduleFacilities, facilities.Length, 2,
            _scheduleFacilityFirst, first => _scheduleFacilityFirst = first);

        _scheduleOutputFirst = Math.Clamp(_scheduleOutputFirst, 0, Math.Max(0, work.Outputs.Count - 2));
        for (var i = 0; i < 2 && i + _scheduleOutputFirst < work.Outputs.Count; i++)
        {
            var row = work.Outputs[i + _scheduleOutputFirst];
            PortraitFrame.Row(this, PortraitLayout.ScrolledRow(PortraitLayout.ScheduleOutputs, i, hasScroll: work.Outputs.Count > 2), row.Name, row.Value, false);
        }
        RegisterScroll("schedule_outputs", PortraitLayout.ScheduleOutputs, work.Outputs.Count, 2,
            _scheduleOutputFirst, first => _scheduleOutputFirst = first);
    }

    private bool ExecuteSchedule(PortraitWidget widget)
    {
        switch (widget.Action)
        {
            case PortraitAction.ScheduleMember:
                _scheduleMemberId = widget.Index;
                _scheduleOutputFirst = 0;
                return true;
            case PortraitAction.ScheduleSlot:
                _scheduleSlot = widget.Index;
                _scheduleOutputFirst = 0;
                return true;
            case PortraitAction.ScheduleRoom:
                _scheduleRoomId = widget.Index;
                _scheduleFacilityFirst = 0;
                return true;
            case PortraitAction.ScheduleCancel:
                _vm.Hub.Assign(_scheduleMemberId, widget.Index, SlotMode.Free, -1);
                _scheduleOutputFirst = 0;
                return true;
            case PortraitAction.ScheduleFacility:
                var current = _vm.Hub.AssignmentOf(_scheduleMemberId, _scheduleSlot);
                _vm.Hub.Assign(_scheduleMemberId, _scheduleSlot,
                    current.FacilityId == widget.Index ? SlotMode.Free : SlotMode.Work,
                    current.FacilityId == widget.Index ? -1 : widget.Index);
                _scheduleOutputFirst = 0;
                return true;
            default:
                return false;
        }
    }

    private IReadOnlyList<PortraitRegion> ScheduleRegions() => new[]
    {
        new PortraitRegion("back", PortraitLayout.OverlayBack),
        new PortraitRegion("schedule_members", PortraitLayout.ScheduleMembers),
        new PortraitRegion("schedule_slots", PortraitLayout.ScheduleSlots),
        new PortraitRegion("schedule_grid", PortraitLayout.ScheduleGrid),
        new PortraitRegion("schedule_facilities", PortraitLayout.ScheduleFacilities),
        new PortraitRegion("schedule_outputs", PortraitLayout.ScheduleOutputs),
    };
}
