using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Hub;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 交互：设施行动 / 交流走交互抽屉；打开仓库类设施走存取抽屉；对话与场景演出铺满整屏
/// （说话人立绘作底、名牌、缺角双线框正文、药丸选项）。
/// </summary>
public partial class PortraitHubScreen
{
    private bool _interactionOpen;
    private bool _giftOpen;
    private bool _observing;
    private int _socialCategory = -1;
    private string _conversationText = "";
    private float _conversationReveal;

    private bool ConversationActive => _vm.Hub.Overlay != null || _vm.Hub.ScenePlaying || _observing;

    private void OpenInteraction()
    {
        _interactionOpen = true;
        _giftOpen = false;
        _socialCategory = -1;
        _pan.Remove("interaction");
        QueueRedraw();
    }

    private void InteractionBack()
    {
        if (_giftOpen)
            _giftOpen = false;
        else if (_socialCategory >= 0)
            _socialCategory = -1;
        else
            _interactionOpen = false;
        _pan.Remove("interaction");
    }

    private List<PortraitWidget> InteractionActions()
    {
        var rows = new List<PortraitWidget>();
        void Add(PortraitAction action, int index, string label, bool enabled = true) =>
            rows.Add(new PortraitWidget(default, action, index, enabled, label));
        if (_giftOpen)
        {
            foreach (var pair in _vm.Hub.State.Roster.Master!.Bag.Items)
                if (pair.Value > 0)
                    Add(PortraitAction.GiftItem, rows.Count, pair.Key);
            return rows;
        }
        if (_vm.ShowSocial)
        {
            if (_socialCategory >= 0)
            {
                foreach (var child in InkViewModel.SocialCategories[_socialCategory].Children!)
                {
                    var label = child.Index == InkViewModel.InviteActionIndex ? _vm.InviteEntry().Label : child.Label;
                    var step = child.Index - 4;
                    var enabled = step < 0 || step > 3 || _vm.Hub.TouchStepUnlocked(step);
                    Add(PortraitAction.SocialRun, child.Index, label, enabled);
                }
            }
            else
                for (var i = 0; i < InkViewModel.SocialCategories.Length; i++)
                    Add(PortraitAction.SocialCategory, i, InkViewModel.SocialCategories[i].Label);
        }
        else
        {
            var actions = _vm.FixtureActions();
            for (var i = 0; i < actions.Count; i++)
                Add(PortraitAction.FixtureRun, i, InkText.ActionKind(actions[i]));
            for (var i = 0; i < InkViewModel.PlaceActions.Length; i++)
                Add(PortraitAction.ObserveRoom, i, InkViewModel.PlaceActions[i].Label, InkViewModel.PlaceActions[i].Enabled);
        }
        return rows;
    }

    private float DrawInteractionSheet()
    {
        var rows = InteractionActions();
        var top = PortraitLayout.InteractionSheetTop(rows.Count);
        PortraitFrame.Sheet(this, top);
        var title = _giftOpen ? "赠礼" : _vm.ShowSocial ? _vm.SocialTitle() : _vm.CurrentFixtureName();
        if (_vm.ShowSocial && !_giftOpen)
        {
            var who = _vm.Selected();
            if (who != null)
                PortraitFrame.Avatar(this, new Vector2(PortraitLayout.Pad + 70f, top + PortraitLayout.SheetTitleOffset), 48f,
                    PortraitAvatars.Resolve(_vm.FindById(who.Value.Id)), who.Value.Name);
        }
        var titleX = _vm.ShowSocial && !_giftOpen ? PortraitLayout.Pad + 150f : PortraitLayout.Pad + 20f;
        InkDraw.TextBounded(this, new Rect2(titleX, top + PortraitLayout.SheetTitleOffset - 40f, 700f, 80f), title,
            PortraitLayout.FontTitle, PortraitLayout.FontBody, InkStyle.Line, "lm");
        var close = PortraitLayout.SheetClose(top);
        if (_giftOpen || _socialCategory >= 0)
            PortraitGlyph.Back(this, close.GetCenter().X, close.GetCenter().Y, 26f, InkStyle.Dim);
        else
            PortraitGlyph.Close(this, close.GetCenter().X, close.GetCenter().Y, 26f, InkStyle.Dim);
        _widgets.Add(new PortraitWidget(close, PortraitAction.InteractionBack, 0, true, "返回"));

        var visible = PortraitLayout.InteractionRows(top);
        var first = Math.Clamp(Pan("interaction", rows.Count, visible), 0, Math.Max(0, rows.Count - visible));
        for (var i = 0; i < visible && first + i < rows.Count; i++)
        {
            var row = rows[first + i];
            var rect = PortraitLayout.InteractionRow(top, i);
            PortraitFrame.Plaque(this, rect, row.Action == PortraitAction.GiftItem ? ItemName(row.Label) : row.Label,
                enabled: row.Enabled);
            _widgets.Add(row with { Rect = rect });
        }
        RegisterScroll("interaction", new Rect2(0, PortraitLayout.InteractionRow(top, 0).Position.Y, PortraitLayout.CanvasWidth,
            visible * PortraitLayout.SheetRowStep), rows.Count, visible, first, v => _pan["interaction"] = v, PortraitLayout.SheetRowStep);
        return top;
    }

    // ---------- 存取抽屉 ----------

    private float DrawStorageSheet()
    {
        var top = PortraitLayout.StorageSheetTop;
        PortraitFrame.Sheet(this, top);
        InkDraw.TextBounded(this, new Rect2(PortraitLayout.Pad + 20f, top + PortraitLayout.SheetTitleOffset - 40f, 700f, 80f),
            _vm.StorageName(), PortraitLayout.FontTitle, PortraitLayout.FontBody, InkStyle.Line, "lm");
        InkDraw.Text(this, new Vector2(PortraitLayout.Pad + 20f, top + PortraitLayout.SheetContentOffset), $"容量 {_vm.StorageCapacityText()}",
            PortraitLayout.FontMeta, InkStyle.Dim, "lm");
        var close = PortraitLayout.SheetClose(top);
        PortraitGlyph.Close(this, close.GetCenter().X, close.GetCenter().Y, 26f, InkStyle.Dim);
        _widgets.Add(new PortraitWidget(close, PortraitAction.SheetClose, 0, true, "收起"));

        var rows = _vm.StorageRows();
        var visible = PortraitLayout.StorageRows;
        var first = Math.Clamp(Pan("storage", rows.Count, visible), 0, Math.Max(0, rows.Count - visible));
        for (var i = 0; i < visible && first + i < rows.Count; i++)
        {
            var at = first + i;
            var row = rows[at];
            var rect = PortraitLayout.StorageRow(i);
            PortraitFrame.Bevel(this, rect, 22f, null, InkStyle.WoodDark, 3f);
            var name = ItemName(row.ItemId);
            var icon = new Vector2(rect.Position.X + 66f, rect.GetCenter().Y);
            InkDraw.Jewel(this, icon, 34f, InkStyle.Dim);
            InkDraw.Jewel(this, icon, 30f, InkStyle.Bg);
            InkDraw.Text(this, icon, name[..1], PortraitLayout.FontMeta, InkStyle.Line, "cm");
            InkDraw.TextBounded(this, new Rect2(rect.Position.X + 120f, rect.Position.Y + 8f, 330f, 60f), name,
                PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "lm");
            InkDraw.TextBounded(this, new Rect2(rect.Position.X + 120f, rect.Position.Y + 64f, 330f, 52f),
                $"仓 {row.InStorage} · 包 {row.InBag}", PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "lm");
            for (var b = 0; b < 2; b++)
            {
                var br = PortraitLayout.StorageButton(rect, b);
                var enabled = b == 0 ? row.InBag > 0 : row.InStorage > 0;
                PortraitFrame.Plaque(this, br, b == 0 ? "放入" : "取出", primary: b == 1, enabled: enabled);
                _widgets.Add(new PortraitWidget(br, b == 0 ? PortraitAction.StoreIn : PortraitAction.StoreOut, at, enabled, row.ItemId));
            }
        }
        RegisterScroll("storage", new Rect2(0, PortraitLayout.StorageRow(0).Position.Y, PortraitLayout.CanvasWidth,
            visible * PortraitLayout.SheetRowStep), rows.Count, visible, first, v => _pan["storage"] = v, PortraitLayout.SheetRowStep);
        return top;
    }

    // ---------- 对话整屏 ----------

    private void DrawScene()
    {
        var hub = _vm.Hub;
        var scene = hub.ScenePlaying;
        var overlay = hub.Overlay;
        string speaker;
        string text;
        if (_observing)
        {
            // 观察四周：用 Core 写进日志的那句描述作正文。
            speaker = hub.PlaceName();
            text = hub.Log.Count > 0 ? hub.Log[^1].Text : "";
        }
        else if (scene)
        {
            speaker = hub.SceneActor!.Name;
            text = "";
            if (hub.SceneLines.Count > 0)
            {
                speaker = hub.SceneLines[^1].Speaker;
                text = hub.SceneLines[^1].Text;
            }
        }
        else
        {
            speaker = overlay!.Speaker;
            text = overlay.Text;
        }
        if (_conversationText != text)
        {
            _conversationText = text;
            _conversationReveal = 0f;
            _pan.Remove("scene_text");
            _pan.Remove("scene_choices");
        }

        var actor = _vm.FindByName(speaker);
        var art = actor != null ? LoadCharacterPortrait(actor) : null;
        if (art != null)
        {
            PortraitFrame.Cover(this, art, new Rect2(0, 0, PortraitLayout.CanvasWidth, 1750f), 0f);
            PortraitFrame.Fade(this, new Rect2(0, 1000f, PortraitLayout.CanvasWidth, 750f), 0f, 1f);
            PortraitFrame.Fade(this, new Rect2(0, 0, PortraitLayout.CanvasWidth, 300f), 0.85f, 0f);
        }

        InkDraw.TextBounded(this, new Rect2(PortraitLayout.Pad, PortraitLayout.SafeTop + 20f, 380f, PortraitLayout.TouchMin),
            hub.PlaceName(), PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "lm");
        for (var i = 0; i < InkHubModel.ChatEntries.Length; i++)
        {
            var page = InkHubModel.ChatEntries[i];
            var rect = PortraitLayout.SceneEntry(i);
            var label = InkPageModel.Info(page).Label;
            PortraitFrame.Chip(this, rect, label, false, 90f);
            _widgets.Add(new PortraitWidget(rect, PortraitAction.CharacterPage, i, actor != null, label));
        }

        var choices = scene ? hub.SceneChoices.Select((choice, index) => new OverlayChoice(index, choice.Label)).ToArray()
            : _observing ? Array.Empty<OverlayChoice>() : overlay!.Choices.ToArray();
        var visibleChoices = Math.Min(choices.Length, 3);
        var dialog = PortraitLayout.SceneDialog(visibleChoices);

        if (actor != null)
        {
            var nameY = dialog.Position.Y - 70f;
            InkDraw.Text(this, new Vector2(60f, nameY), actor.Name, 72, InkStyle.Line, "lm");
            InkDraw.Text(this, new Vector2(60f + InkDraw.Measure(actor.Name, 72).X + 30f, nameY + 6f),
                InkText.Bond(actor.Condition.Bond), PortraitLayout.FontMeta, InkStyle.Dim, "lm");
        }
        else
            InkDraw.Text(this, new Vector2(60f, dialog.Position.Y - 70f), speaker, PortraitLayout.FontTitle, InkStyle.Line, "lm");

        PortraitFrame.GothicFrame(this, dialog, new Color(InkStyle.Panel, 0.92f));
        var textArea = PortraitLayout.SceneText(dialog);
        var shown = text[..Math.Min(text.Length, (int)_conversationReveal)];
        var lines = InkDraw.WrapLines(shown, textArea.Size.X - 20f, PortraitLayout.FontBody);
        var visibleLines = PortraitLayout.SceneVisibleLines;
        var firstLine = Math.Clamp(Pan("scene_text", lines.Count, visibleLines), 0, Math.Max(0, lines.Count - visibleLines));
        for (var i = 0; i < visibleLines && firstLine + i < lines.Count; i++)
            InkDraw.Text(this, new Vector2(textArea.Position.X, textArea.Position.Y + i * PortraitLayout.SceneLineHeight + 40f),
                lines[firstLine + i], PortraitLayout.FontBody, overlay?.CurrentLineDim == true ? InkStyle.Dim : InkStyle.Line, "lm");

        if (choices.Length == 0)
        {
            // 纯展示：整块正文框（连同其上的立绘区）都是「点击继续」，右下一枚呼吸的实心菱。
            var advance = new Rect2(0, PortraitLayout.SafeTop + 20f + PortraitLayout.TouchMin + 20f,
                PortraitLayout.CanvasWidth, dialog.End.Y - (PortraitLayout.SafeTop + 20f + PortraitLayout.TouchMin + 20f));
            _widgets.Add(new PortraitWidget(advance, PortraitAction.ConversationAdvance, 0, true, ""));
            InkDraw.Jewel(this, new Vector2(dialog.End.X - 80f, dialog.End.Y - 50f), 12f, InkStyle.Line);
        }
        RegisterScroll("scene_text", textArea, lines.Count, visibleLines, firstLine, v => _pan["scene_text"] = v,
            PortraitLayout.SceneLineHeight);

        var firstChoice = Math.Clamp(Pan("scene_choices", choices.Length, visibleChoices), 0,
            Math.Max(0, choices.Length - visibleChoices));
        for (var i = 0; i < visibleChoices && firstChoice + i < choices.Length; i++)
        {
            var choice = choices[firstChoice + i];
            var rect = PortraitLayout.SceneChoice(visibleChoices, i);
            PortraitFrame.Plaque(this, rect, choice.Label, primary: i == 0);
            _widgets.Add(new PortraitWidget(rect, PortraitAction.ConversationChoice, choice.Id, true, choice.Label));
        }
        if (choices.Length > visibleChoices)
            RegisterScroll("scene_choices", new Rect2(0, PortraitLayout.SceneChoice(visibleChoices, 0).Position.Y,
                PortraitLayout.CanvasWidth, visibleChoices * PortraitLayout.SceneChoiceStep), choices.Length, visibleChoices,
                firstChoice, v => _pan["scene_choices"] = v, PortraitLayout.SceneChoiceStep);
    }

    private bool ExecuteInteraction(PortraitWidget widget)
    {
        var hub = _vm.Hub;
        switch (widget.Action)
        {
            case PortraitAction.InteractionBack:
                InteractionBack();
                return true;
            case PortraitAction.SocialCategory:
                if (widget.Index == 2)
                    hub.Social(SocialAction.Observe);
                else if (widget.Index == 3)
                {
                    hub.ClearSelection();
                    _interactionOpen = false;
                }
                else
                    _socialCategory = widget.Index;
                return true;
            case PortraitAction.SocialRun:
                if (widget.Index == 2)
                    _giftOpen = true;
                else
                    hub.Social(widget.Index == InkViewModel.InviteActionIndex
                        ? _vm.InviteEntry().Action : InkViewModel.SocialActions[widget.Index].Action);
                return true;
            case PortraitAction.GiftItem:
                hub.Social(SocialAction.Gift, widget.Label);
                _giftOpen = false;
                return true;
            case PortraitAction.FixtureRun:
                var act = _vm.FixtureActions()[widget.Index];
                if (hub.ActAtFixture(act))
                    PlayVeil(PortraitVeil.IconFor(act), Rimisekai.Housing.ActionKindMap.LabelOf(act));
                return true;
            case PortraitAction.ObserveRoom:
                if (hub.Act(InkViewModel.PlaceActions[widget.Index].Action))
                    _observing = true;
                return true;
            case PortraitAction.ConversationAdvance:
                if (_conversationReveal < _conversationText.Length)
                    _conversationReveal = _conversationText.Length;
                else if (_observing)
                    _observing = false;
                else if (hub.ScenePlaying)
                    hub.SceneContinue();
                else
                    hub.AdvanceOverlay();
                return true;
            case PortraitAction.ConversationChoice:
                if (hub.ScenePlaying)
                    hub.SceneChoose(widget.Index);
                else
                    hub.Choose(widget.Index);
                return true;
            case PortraitAction.CharacterPage:
                var who = _vm.FindByName(SceneSpeaker());
                if (who != null)
                {
                    OpenCharacter(who.Id);
                    _charSeg = Math.Clamp(Array.IndexOf(CharacterSegments, InkHubModel.ChatEntries[widget.Index]), 0, 2);
                }
                return true;
            case PortraitAction.StoreIn:
                hub.StoreOne(widget.Label);
                return true;
            case PortraitAction.StoreOut:
                hub.TakeOne(widget.Label);
                return true;
            default:
                return false;
        }
    }

    /// <summary>当前对话/演出的说话人名。</summary>
    private string SceneSpeaker()
    {
        var hub = _vm.Hub;
        if (_observing)
            return "";
        if (hub.ScenePlaying)
            return hub.SceneLines.Count > 0 ? hub.SceneLines[^1].Speaker : hub.SceneActor!.Name;
        return hub.Overlay!.Speaker;
    }
}
