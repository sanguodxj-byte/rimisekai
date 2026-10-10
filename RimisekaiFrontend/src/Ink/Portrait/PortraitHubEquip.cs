using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Defs;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 装备页（推入页，从角色状态段的装备格进入）：上半十格槽位（双列，点选＝切换当前槽），
/// 下半「可换」列表＝主角背包里能进当前槽的件（点＝换上；当前槽有东西时首行是「卸下」）。
/// 按住槽位或候选 0.5 秒弹详情（纯展示），松手不再触发点按。
/// </summary>
public partial class PortraitHubScreen
{
    private int _equipSlot;

    private const float HoldSeconds = 0.5f;
    private float _holdAge;
    private bool _holdFired;

    private void OpenEquipment(int slotIndex)
    {
        _equipSlot = slotIndex;
        _pan.Remove("equip");
        _push = PushPage.Equip;
    }

    private void DrawEquipmentPage()
    {
        var who = Who;
        var view = PortraitLayout.PageBody;
        var offset = _pan.GetValueOrDefault("equip");
        var y = view.Position.Y + 30f - offset;
        var registry = _vm.Hub.State.Equips;
        var cw = (PortraitLayout.FullWidth - 20f) / 2f;

        PortraitFrame.SectionRule(this, PortraitLayout.Pad, PortraitLayout.CanvasWidth - PortraitLayout.Pad, y + 20f, "槽位");
        y += 70f;
        for (var i = 0; i < DisplayEquipSlots.Length; i++)
        {
            var slot = DisplayEquipSlots[i];
            var r = new Rect2(PortraitLayout.Pad + i % 2 * (cw + 20f), y + i / 2 * 150f, cw, 134f);
            var name = EquipmentName(who, slot, registry);
            PortraitFrame.Card(this, r, i == _equipSlot);
            InkDraw.Text(this, new Vector2(r.GetCenter().X, r.Position.Y + 40f), EquipSlots.Label(slot),
                PortraitLayout.FontMeta, InkStyle.Dim, "cm");
            InkDraw.TextBounded(this, new Rect2(r.Position.X + 20f, r.Position.Y + 66f, r.Size.X - 40f, 56f), name,
                PortraitLayout.FontBody, PortraitLayout.FontMeta, name == "空" ? InkStyle.WoodDark : InkStyle.Line, "cm");
            AddClipped(r, view, PortraitAction.EquipSlotPick, i, true, EquipSlots.Label(slot));
        }
        y += (DisplayEquipSlots.Length + 1) / 2 * 150f + 40f;

        var current = DisplayEquipSlots[_equipSlot];
        PortraitFrame.SectionRule(this, PortraitLayout.Pad, PortraitLayout.CanvasWidth - PortraitLayout.Pad, y,
            $"{EquipSlots.Label(current)} 可换");
        y += 50f;
        var rowStep = 150f;
        var occupied = EquipmentName(who, current, registry) != "空";
        if (occupied)
        {
            var r = new Rect2(PortraitLayout.Pad, y, PortraitLayout.FullWidth, 134f);
            PortraitFrame.Card(this, r);
            PortraitGlyph.Bag(this, r.Position.X + 70f, r.GetCenter().Y, 28f, InkStyle.Dim);
            InkDraw.Text(this, new Vector2(r.Position.X + 130f, r.GetCenter().Y), "卸下",
                PortraitLayout.FontBody, InkStyle.Line, "lm");
            AddClipped(r, view, PortraitAction.EquipRemove, _equipSlot, true, "卸下");
            y += rowStep;
        }
        var options = _vm.Hub.GearOptions(current);
        for (var i = 0; i < options.Count; i++)
        {
            var o = options[i];
            var r = new Rect2(PortraitLayout.Pad, y, PortraitLayout.FullWidth, 134f);
            PortraitFrame.Card(this, r);
            _fog.Place(r.Grow(-6f), o.Quality, viewport: view);
            InkDraw.TextBounded(this, new Rect2(r.Position.X + 40f, r.Position.Y + 14f, r.Size.X - 220f, 64f), o.Name,
                PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "lm");
            var brief = string.Join(" · ", o.Details.Take(3).Select(d => d.Value));
            InkDraw.TextBounded(this, new Rect2(r.Position.X + 40f, r.Position.Y + 76f, r.Size.X - 220f, 48f), brief,
                PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "lm");
            InkDraw.Text(this, new Vector2(r.End.X - 40f, r.GetCenter().Y), $"×{o.Count}",
                PortraitLayout.FontMeta, InkStyle.Dim, "rm");
            AddClipped(r, view, PortraitAction.EquipOption, i, true, o.ItemId);
            y += rowStep;
        }
        if (options.Count == 0)
        {
            InkDraw.Text(this, new Vector2(PortraitLayout.CanvasWidth / 2f, y + 34f), "背包里没有能换的",
                PortraitLayout.FontMeta, InkStyle.Dim, "cm");
            y += 68f;
        }

        var total = (int)(y + offset - view.Position.Y);
        offset = Pan("equip", total, (int)view.Size.Y);
        RegisterScroll("equip", view, total, (int)view.Size.Y, offset, v => _pan["equip"] = v, 1f);
        MaskAbove(view);
        DrawPageTop("装备", who.Name);
    }

    private bool ExecuteEquipment(PortraitWidget w)
    {
        switch (w.Action)
        {
            case PortraitAction.EquipInfo:
                OpenEquipment(w.Index);
                return true;
            case PortraitAction.EquipSlotPick:
                _equipSlot = w.Index;
                return true;
            case PortraitAction.EquipRemove:
                if (!_vm.Hub.UnequipToBag(Who.Id, DisplayEquipSlots[w.Index]))
                    SetNotice("先卸下副手。");
                return true;
            case PortraitAction.EquipOption:
                if (!_vm.Hub.EquipFromBag(Who.Id, DisplayEquipSlots[_equipSlot], w.Label))
                    SetNotice(DisplayEquipSlots[_equipSlot] == EquipSlot.OffHand ? "先装上主手。" : "换不上。");
                return true;
            default:
                return false;
        }
    }

    /// <summary>长按支持：槽位格看当前那件，候选行看候选那件。</summary>
    private static bool Holdable(PortraitAction action) =>
        action is PortraitAction.EquipSlotPick or PortraitAction.EquipOption;

    private void HoldTick(double delta)
    {
        if (!_pressed || _dragging || _holdFired || _push != PushPage.Equip)
        {
            _holdAge = 0f;
            return;
        }
        var hit = Hit(_pressPos);
        if (hit == null || !Holdable(hit.Value.Widget.Action))
            return;
        _holdAge += (float)delta;
        if (_holdAge < HoldSeconds)
            return;
        _holdFired = true;
        ShowHeldDetails(hit.Value.Widget);
    }

    /// <summary>核对用：直接走一次长按。</summary>
    public void DebugHold(PortraitAction action, int index)
    {
        foreach (var w in _widgets)
            if (w.Action == action && w.Index == index)
            {
                ShowHeldDetails(w);
                return;
            }
    }

    private void ShowHeldDetails(PortraitWidget w)
    {
        string title;
        string subtitle;
        Quality? quality;
        IReadOnlyList<DetailLine> details;
        if (w.Action == PortraitAction.EquipSlotPick)
        {
            var slot = DisplayEquipSlots[w.Index];
            var name = EquipmentName(Who, slot, _vm.Hub.State.Equips);
            title = name == "空" ? EquipSlots.Label(slot) : name;
            subtitle = name == "空" ? "未装备" : $"{Who.Name} · {EquipSlots.Label(slot)}";
            details = _vm.Hub.EquippedDetails(Who.Id, slot);
            quality = _vm.Hub.EquippedQuality(Who.Id, slot);
        }
        else
        {
            var option = _vm.Hub.GearOptions(DisplayEquipSlots[_equipSlot]).FirstOrDefault(o => o.ItemId == w.Label);
            if (option.ItemId == null)
                return;
            title = option.Name;
            subtitle = "";
            quality = option.Quality;
            details = option.Details;
        }
        ModalWanted!(new InkModalPage { Title = title, Item = new InkModalItemData { Subtitle = subtitle, Quality = quality, Lines = details } });
    }
}
