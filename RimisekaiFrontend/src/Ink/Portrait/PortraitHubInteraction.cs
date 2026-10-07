using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Hub;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

public partial class PortraitHubScreen
{
    private bool _interactionOpen;
    private bool _giftOpen;
    private bool _observing;
    private int _socialCategory = -1;
    private int _interactionFirst;
    private int _conversationFirst;
    private int _conversationChoiceFirst;
    private string _conversationText = "";
    private float _conversationReveal;

    private bool ConversationActive => _vm.Hub.Overlay != null || _vm.Hub.ScenePlaying || _observing;
    private bool InteractionActive => _interactionOpen || ConversationActive;

    private bool DrawInteraction()
    {
        if (!InteractionActive)
            return false;
        if (ConversationActive)
        {
            DrawConversation();
            DrawAvatars();
            DrawFixtures();
        }
        else
            DrawInteractionActions();
        DrawTabBar();
        return true;
    }

    private void OpenInteraction()
    {
        _interactionOpen = true;
        _giftOpen = false;
        _socialCategory = -1;
        _interactionFirst = 0;
        QueueRedraw();
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

    private void DrawInteractionActions()
    {
        DrawPageTop(_vm.ShowSocial ? _vm.SocialTitle() : _vm.CurrentFixtureName());
        var back = PortraitLayout.OverlayBack;
        PortraitFrame.Button(this, back, "返回");
        _widgets.Add(new PortraitWidget(back, PortraitAction.InteractionBack, 0, true, "返回"));
        var rows = InteractionActions();
        var visible = PortraitLayout.OverlayRows;
        var hasScroll = rows.Count > visible;
        _interactionFirst = Math.Clamp(_interactionFirst, 0, Math.Max(0, rows.Count - visible));
        for (var i = 0; i < visible && i + _interactionFirst < rows.Count; i++)
        {
            var row = rows[i + _interactionFirst];
            var rect = PortraitLayout.ScrolledRow(PortraitLayout.InteractionList, i, PortraitLayout.RowHeight, hasScroll);
            PortraitFrame.Button(this, rect, row.Action == PortraitAction.GiftItem ? ItemName(row.Label) : row.Label,
                enabled: row.Enabled);
            _widgets.Add(row with { Rect = rect });
        }
        RegisterScroll("interaction", PortraitLayout.InteractionList, rows.Count, visible,
            _interactionFirst, first => _interactionFirst = first);
    }

    private void DrawConversation()
    {
        var hub = _vm.Hub;
        var scene = hub.ScenePlaying;
        var overlay = hub.Overlay;
        string speaker;
        string text;
        if (_observing)
        {
            // 观察四周没有插画了（主人 2026-10-05 删地点插图），改用 Core 写进日志的那句描述作正文。
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
            _conversationFirst = _conversationChoiceFirst = 0;
        }

        // 对话框＝地图网格区：只换这一块，下方头像带与设施栏仍常显。
        PortraitFrame.Panel(this, PortraitLayout.ConversationBox, InkStyle.Bg, flourish: 0f);

        for (var i = 0; i < InkHubModel.ChatEntries.Length; i++)
        {
            var page = InkHubModel.ChatEntries[i];
            var rect = PortraitLayout.ConversationEntry(i);
            var label = InkPageModel.Info(page).Label;
            PortraitFrame.Button(this, rect, label);
            _widgets.Add(new PortraitWidget(rect, PortraitAction.CharacterPage, i, true, label));
        }

        var actor = _vm.FindByName(speaker);
        if (actor != null)
        {
            var name = $"{actor.Name}　{InkText.Bond(actor.Condition.Bond)}";
            InkDraw.TextBounded(this, PortraitLayout.ConversationName, name, PortraitLayout.FontBody,
                PortraitLayout.FontMeta, InkStyle.Line, "cm");
        }

        var shown = text[..Math.Min(text.Length, (int)_conversationReveal)];
        var lines = InkDraw.WrapLines(shown, PortraitLayout.ConversationText.Size.X - PortraitLayout.TouchMin - 16f,
            PortraitLayout.FontBody);
        var visible = PortraitLayout.ConversationVisibleLines;
        var linesHasScroll = lines.Count > visible;
        _conversationFirst = Math.Clamp(_conversationFirst, 0, Math.Max(0, lines.Count - visible));
        for (var i = 0; i < visible && i + _conversationFirst < lines.Count; i++)
        {
            var rect = PortraitLayout.ScrolledRow(PortraitLayout.ConversationText, i, PortraitLayout.ModalLineHeight, linesHasScroll);
            InkDraw.Text(this, new Vector2(rect.Position.X, rect.GetCenter().Y), lines[i + _conversationFirst],
                PortraitLayout.FontBody, overlay?.CurrentLineDim == true ? InkStyle.Dim : InkStyle.Line, "lm");
        }
        RegisterScroll("conversation_text", PortraitLayout.ConversationText, lines.Count, visible,
            _conversationFirst, first => _conversationFirst = first, PortraitLayout.ModalLineHeight);

        var choices = scene ? hub.SceneChoices.Select((choice, index) => new OverlayChoice(index, choice.Label)).ToArray()
            : _observing ? Array.Empty<OverlayChoice>() : overlay!.Choices.ToArray();
        var choicesVisible = PortraitLayout.ConversationVisibleChoices;
        var choicesHasScroll = choices.Length > choicesVisible;
        _conversationChoiceFirst = Math.Clamp(_conversationChoiceFirst, 0, Math.Max(0, choices.Length - choicesVisible));
        for (var i = 0; i < choicesVisible && i + _conversationChoiceFirst < choices.Length; i++)
        {
            var choice = choices[i + _conversationChoiceFirst];
            var rect = PortraitLayout.ScrolledRow(PortraitLayout.ConversationChoices, i, PortraitLayout.RowHeight, choicesHasScroll);
            PortraitFrame.Button(this, rect, choice.Label);
            _widgets.Add(new PortraitWidget(rect, PortraitAction.ConversationChoice, choice.Id, true, choice.Label));
        }
        if (choices.Length > 0)
            RegisterScroll("conversation_choices", PortraitLayout.ConversationChoices, choices.Length, choicesVisible,
                _conversationChoiceFirst, first => _conversationChoiceFirst = first);
        else
        {
            // 纯展示：说话人以下的整块都是「点击继续」，箭头落在对话框底部中央。
            _widgets.Add(new PortraitWidget(PortraitLayout.ConversationAdvance, PortraitAction.ConversationAdvance, 0, true, ""));
            DrawColoredPolygon(PortraitLayout.ConversationArrow, InkStyle.Line);
        }
    }

    public override void _Process(double delta)
    {
        // 对话打字机逐字显现。
        if (Visible && ConversationActive && _conversationReveal < _conversationText.Length)
        {
            _conversationReveal = Math.Min(_conversationText.Length, _conversationReveal + (float)delta * 30f);
            QueueRedraw();
        }
        // 技能星盘视角动效：动画进行中逐帧重绘，静止时零开销。
        if (_page == InkPage.Skills && AdvanceSkillView(delta))
            QueueRedraw();
    }

    private bool ExecuteInteraction(PortraitWidget widget)
    {
        var hub = _vm.Hub;
        switch (widget.Action)
        {
            case PortraitAction.RosterPick:
                hub.Select(widget.Index);
                OpenInteraction();
                return true;
            case PortraitAction.InteractionBack:
                if (_giftOpen)
                    _giftOpen = false;
                else if (_socialCategory >= 0)
                    _socialCategory = -1;
                else
                    _interactionOpen = false;
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
                hub.ActAtFixture(_vm.FixtureActions()[widget.Index]);
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
                _page = InkHubModel.ChatEntries[widget.Index];
                if (_page == InkPage.Schedule)
                    OpenSchedule();
                return true;
            default:
                return false;
        }
    }
}
