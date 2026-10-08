using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Character;
using Rimisekai.Housing;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 日程：竖向 24 小时时间轴，四个 6 小时时段块（工作＝骨白实心、娱乐＝浅填描边、空闲＝暗描边），
/// 一条「此刻」横线。点时段块弹排班抽屉：空闲钮＋房间签＋该房设施行（点设施即把这一段排过去，
/// 设施有工作行动算工作，否则算娱乐；再点一次取消）。数据走 InkCharacterPageBuilder 的日程模型。
/// </summary>
public partial class PortraitHubScreen
{
    private int _scheduleSlot;
    private int _scheduleRoomId = -1;

    private const float HourPitch = 46f;

    private void OpenSchedule()
    {
        _scheduleRoomId = _vm.Hub.PlayerRoomId;
        _scheduleSlot = CurrentSlot;
    }

    private InkPageModel ScheduleModel() => InkCharacterPageBuilder.Build(_vm, InkPage.Schedule, Who,
        selected: _scheduleSlot, roomId: _scheduleRoomId)!;

    private static string SlotRange(int slot) => $"{slot * 6}时至{slot * 6 + 6}时";

    private float DrawScheduleSegment(CharacterState who, float y, Rect2 view)
    {
        var top = y + 30f;
        for (var h = 0; h <= 24; h += 3)
        {
            var hy = top + h * HourPitch;
            InkDraw.Text(this, new Vector2(124f, hy), $"{h}时", PortraitLayout.FontMeta, InkStyle.Dim, "rm");
            InkDraw.InkLine(this, new Vector2(136f, hy), new Vector2(170f, hy), InkStyle.WoodDark, 2f);
        }
        InkDraw.InkLine(this, new Vector2(152f, top), new Vector2(152f, top + 24 * HourPitch), InkStyle.WoodDark, 3f);
        // 时段块里两行字所占的横带（x0, x1, y0, y1）：「此刻」线落在带里就在字两侧断开，不穿字。
        var textBands = new List<(float X0, float X1, float Y0, float Y1)>();

        for (var slot = 0; slot < WorkSlot.Count; slot++)
        {
            var a = _vm.Hub.AssignmentOf(who.Id, slot);
            var r = new Rect2(190f, top + slot * 6 * HourPitch + 5f, PortraitLayout.CanvasWidth - 60f - 190f, 6 * HourPitch - 10f);
            var pressed = PortraitFrame.IsPressed(r);
            var work = a.Mode == SlotMode.Work && a.FacilityId >= 0;
            var fun = a.Mode == SlotMode.Entertainment && a.FacilityId >= 0;
            if (work)
                PortraitFrame.Bevel(this, r, 20f, pressed ? new Color(InkStyle.Line, 0.78f) : InkStyle.Line);
            else
                PortraitFrame.Bevel(this, r, 20f, pressed ? PortraitFrame.PressFill : fun ? InkStyle.Hover : InkStyle.Bg,
                    fun ? InkStyle.Dim : InkStyle.WoodDark, 3f);
            var label = work ? $"工作 · {_vm.Hub.FacilityName(a.FacilityId)}"
                : fun ? $"娱乐 · {_vm.Hub.FacilityName(a.FacilityId)}" : "空闲";
            var ink = work ? InkStyle.Bg : fun ? InkStyle.Line : InkStyle.Dim;
            var labelSize = InkDraw.TextBounded(this, new Rect2(r.Position.X + 40f, r.Position.Y + 40f, r.Size.X - 80f, 70f), label,
                PortraitLayout.FontBody, PortraitLayout.FontMeta, ink, "lm");
            InkDraw.Text(this, new Vector2(r.Position.X + 40f, r.End.Y - 50f), SlotRange(slot), PortraitLayout.FontMeta,
                work ? InkStyle.WoodDark : InkStyle.Dim, "lm");
            var labelW = Mathf.Min(InkDraw.Measure(label, labelSize).X, r.Size.X - 80f);
            textBands.Add((r.Position.X + 40f, r.Position.X + 40f + labelW, r.Position.Y + 75f - labelSize * 0.7f, r.Position.Y + 75f + labelSize * 0.7f));
            var rangeW = InkDraw.Measure(SlotRange(slot), PortraitLayout.FontMeta).X;
            textBands.Add((r.Position.X + 40f, r.Position.X + 40f + rangeW, r.End.Y - 50f - PortraitLayout.FontMeta * 0.7f, r.End.Y - 50f + PortraitLayout.FontMeta * 0.7f));
            AddClipped(r, view, PortraitAction.ScheduleSlot, slot, true, SlotRange(slot));
        }

        var header = _vm.Hub.Header();
        var ny = top + (header.Hour + header.Minute / 60f) * HourPitch;
        var lineEnd = PortraitLayout.CanvasWidth - PortraitLayout.Pad;
        var cut = textBands.FirstOrDefault(b => ny >= b.Y0 && ny <= b.Y1);
        if (cut.X1 > cut.X0)
        {
            InkDraw.InkLine(this, new Vector2(152f, ny), new Vector2(cut.X0 - 16f, ny), InkStyle.Line, 4f);
            InkDraw.InkLine(this, new Vector2(cut.X1 + 16f, ny), new Vector2(lineEnd, ny), InkStyle.Line, 4f);
        }
        else
            InkDraw.InkLine(this, new Vector2(152f, ny), new Vector2(lineEnd, ny), InkStyle.Line, 4f);
        InkDraw.Jewel(this, new Vector2(152f, ny), 14f, InkStyle.Line);
        return top + 24 * HourPitch + 40f;
    }

    // ---------- 排班抽屉 ----------

    private float DrawSlotSheet()
    {
        var top = 900f;
        PortraitFrame.Sheet(this, top);
        var model = ScheduleModel();
        var work = model.Work!;
        var current = _vm.Hub.AssignmentOf(_charId, _scheduleSlot);
        InkDraw.TextBounded(this, new Rect2(PortraitLayout.Pad + 20f, top + PortraitLayout.SheetTitleOffset - 40f, 760f, 80f),
            $"{Who.Name} · {SlotRange(_scheduleSlot)}", PortraitLayout.FontTitle, PortraitLayout.FontBody, InkStyle.Line, "lm");
        var close = PortraitLayout.SheetClose(top);
        PortraitGlyph.Close(this, close.GetCenter().X, close.GetCenter().Y, 26f, InkStyle.Dim);
        _widgets.Add(new PortraitWidget(close, PortraitAction.SheetClose, 0, true, "收起"));

        var free = new Rect2(PortraitLayout.Pad, top + PortraitLayout.SheetContentOffset, PortraitLayout.FullWidth, PortraitLayout.TouchMin);
        var isFree = current.Mode == SlotMode.Free || current.FacilityId < 0;
        PortraitFrame.Plaque(this, free, "空闲", primary: isFree);
        _widgets.Add(new PortraitWidget(free, PortraitAction.ScheduleCancel, _scheduleSlot, true, "空闲"));

        PortraitFrame.SectionRule(this, PortraitLayout.Pad, PortraitLayout.CanvasWidth - PortraitLayout.Pad, free.End.Y + 50f, "房间");
        var chips = new Rect2(0, free.End.Y + 90f, PortraitLayout.CanvasWidth, PortraitLayout.TouchMin);
        var widths = work.Rooms.Select(room => PortraitFrame.ChipWidth(room.Name)).ToArray();
        var total = (int)(PortraitLayout.Pad * 2f + widths.Sum() + 16f * Math.Max(0, widths.Length - 1));
        var offset = Pan("slot_rooms", total, (int)chips.Size.X);
        float x = PortraitLayout.Pad - offset;
        for (var i = 0; i < work.Rooms.Count; i++)
        {
            var room = work.Rooms[i];
            var r = new Rect2(x, chips.Position.Y, widths[i], chips.Size.Y);
            PortraitFrame.Chip(this, r, room.Name, room.Id == _scheduleRoomId);
            AddClipped(r, chips, PortraitAction.ScheduleRoom, room.Id, true, room.Name);
            x += widths[i] + 16f;
        }
        PortraitFrame.ScrollEdges(this, chips, offset, total);
        RegisterScroll("slot_rooms", chips, total, (int)chips.Size.X, offset, v => _pan["slot_rooms"] = v, 1f, horizontal: true);

        PortraitFrame.SectionRule(this, PortraitLayout.Pad, PortraitLayout.CanvasWidth - PortraitLayout.Pad, chips.End.Y + 50f, "设施");
        var listTop = chips.End.Y + 90f;
        var visible = (int)((PortraitLayout.CanvasHeight - 60f - listTop) / PortraitLayout.SheetRowStep);
        var facilities = work.Facilities;
        var first = Math.Clamp(Pan("slot_facilities", facilities.Count, visible), 0, Math.Max(0, facilities.Count - visible));
        for (var i = 0; i < visible && first + i < facilities.Count; i++)
        {
            var row = facilities[first + i];
            var r = new Rect2(PortraitLayout.Pad, listTop + i * PortraitLayout.SheetRowStep, PortraitLayout.FullWidth, 124f);
            var on = row.TargetNumber == current.FacilityId && !isFree;
            PortraitFrame.Card(this, r, on, 22f);
            InkDraw.TextBounded(this, new Rect2(r.Position.X + 40f, r.Position.Y, r.Size.X - 300f, r.Size.Y), row.Name,
                PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "lm");
            var kind = _vm.Hub.FacilityIsWorkbench(row.TargetNumber) ? "工作" : "娱乐";
            PortraitFrame.Tag(this, new Vector2(r.End.X - 40f - PortraitFrame.ChipWidth(kind) + 16f, r.GetCenter().Y - 33f), kind, 66f, on);
            _widgets.Add(new PortraitWidget(r, PortraitAction.ScheduleFacility, row.TargetNumber, row.Enabled, row.Name));
        }
        RegisterScroll("slot_facilities", new Rect2(0, listTop, PortraitLayout.CanvasWidth, visible * PortraitLayout.SheetRowStep),
            facilities.Count, visible, first, v => _pan["slot_facilities"] = v, PortraitLayout.SheetRowStep);
        return top;
    }

    private bool ExecuteSchedule(PortraitWidget widget)
    {
        switch (widget.Action)
        {
            case PortraitAction.ScheduleSlot:
                _scheduleSlot = widget.Index;
                var assigned = _vm.Hub.AssignmentOf(_charId, _scheduleSlot);
                if (assigned.FacilityId >= 0)
                {
                    var facility = _vm.Hub.State.Territory.Facilities.Find(f => f.Id == assigned.FacilityId);
                    if (facility != null)
                        _scheduleRoomId = facility.RoomId;
                }
                _sheet = SheetKind.Slot;
                _pan.Remove("slot_facilities");
                return true;
            case PortraitAction.ScheduleRoom:
                _scheduleRoomId = widget.Index;
                _pan.Remove("slot_facilities");
                return true;
            case PortraitAction.ScheduleCancel:
                _vm.Hub.Assign(_charId, widget.Index, SlotMode.Free, -1);
                return true;
            case PortraitAction.ScheduleFacility:
                var current = _vm.Hub.AssignmentOf(_charId, _scheduleSlot);
                if (current.FacilityId == widget.Index && current.Mode != SlotMode.Free)
                    _vm.Hub.Assign(_charId, _scheduleSlot, SlotMode.Free, -1);
                else
                    _vm.Hub.Assign(_charId, _scheduleSlot,
                        _vm.Hub.FacilityIsWorkbench(widget.Index) ? SlotMode.Work : SlotMode.Entertainment, widget.Index);
                return true;
            default:
                return false;
        }
    }
}
