using System;
using System.Collections.Generic;
using Godot;
using Rimisekai.Hub;
using Rimisekai.Quest;

namespace Rimisekai.Ink;

/// <summary>
/// 任务页：委托与任务的列表和详情。
/// 世界浏览复用据点地图网格（世界层），战斗是独立页面，都不在此处。
/// </summary>
public partial class InkQuestScreen : Control
{
    public event Action? CloseRequested;

    private HubSession? _hub;

    private int _selectedQuestIndex = -1;
    private int _questListPage;
    private const int QuestListRowsPerPage = 8;

    private int _hoveredWidgetIndex = -1;
    private readonly List<QuestWidget> _widgets = new();

    private enum WidgetAction
    {
        Close,
        QuestRow,
        QuestListPrev,
        QuestListNext,
    }

    private readonly record struct QuestWidget(
        Rect2 Rect,
        WidgetAction Action,
        int Index,
        bool Enabled,
        string Label);

    public void Bind(HubSession hub)
    {
        _hub = hub;
        _selectedQuestIndex = -1;
        _questListPage = 0;
        QueueRedraw();
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            CloseRequested?.Invoke();
            GetViewport()?.SetInputAsHandled();
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            var hit = Hit(motion.Position);
            if (hit != _hoveredWidgetIndex)
            {
                _hoveredWidgetIndex = hit;
                QueueRedraw();
            }
            return;
        }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            var hit = Hit(click.Position);
            if (hit >= 0 && hit < _widgets.Count)
            {
                Dispatch(_widgets[hit]);
                AcceptEvent();
            }
        }
    }

    private int Hit(Vector2 point)
    {
        for (var i = _widgets.Count - 1; i >= 0; i--)
        {
            if (_widgets[i].Enabled && _widgets[i].Rect.HasPoint(point))
                return i;
        }
        return -1;
    }

    private void Dispatch(QuestWidget widget)
    {
        switch (widget.Action)
        {
            case WidgetAction.Close:
                CloseRequested?.Invoke();
                break;

            case WidgetAction.QuestRow:
                _selectedQuestIndex = widget.Index;
                QueueRedraw();
                break;

            case WidgetAction.QuestListPrev:
                if (_questListPage > 0)
                {
                    _questListPage--;
                    QueueRedraw();
                }
                break;

            case WidgetAction.QuestListNext:
                var quests = GetQuests();
                if ((_questListPage + 1) * QuestListRowsPerPage < quests.Count)
                {
                    _questListPage++;
                    QueueRedraw();
                }
                break;
        }
    }

    public override void _Draw()
    {
        _widgets.Clear();

        var subtitle = _hub != null
            ? $"{_hub.State.Clock.Year}年 {_hub.State.Clock.Day}日"
            : "";
        InkFrame.PageShell(this, "任务", wood: false, subtitle: subtitle);

        var leftWidth = InkLayout.SubpageBody.Size.X * 0.45f;
        DrawQuestListPanel(new Rect2(InkLayout.SubpageBody.Position,
            new Vector2(leftWidth, InkLayout.SubpageBody.Size.Y)));
        DrawQuestDetailPanel(new Rect2(
            InkLayout.SubpageBody.Position.X + leftWidth + 24f,
            InkLayout.SubpageBody.Position.Y,
            InkLayout.SubpageBody.Size.X - leftWidth - 24f,
            InkLayout.SubpageBody.Size.Y));

        AddWidget(InkLayout.FullPageClose, WidgetAction.Close, 0, true, "关闭");
    }

    private List<QuestDef> GetQuests()
    {
        // 生产内容尚未注册任务定义；界面按真实记录读取，没有就是空态。
        return new List<QuestDef>();
    }

    private void DrawQuestListPanel(Rect2 panel)
    {
        InkFrame.Panel(this, panel, corner: 20f, rails: false);
        InkDraw.TextBounded(this, InkLayout.PanelHeading(panel), "委托与任务", 24, 18);

        var quests = GetQuests();
        if (quests.Count == 0)
        {
            InkDraw.TextBounded(this, InkLayout.PanelText(panel), "暂无已注册任务记录",
                20, 16, InkStyle.Dim, "cm");
            return;
        }

        var startIndex = _questListPage * QuestListRowsPerPage;
        var count = Math.Min(QuestListRowsPerPage, quests.Count - startIndex);

        for (var i = 0; i < count; i++)
        {
            var qIndex = startIndex + i;
            var quest = quests[qIndex];
            var rowRect = new Rect2(panel.Position.X + 20f,
                panel.Position.Y + 76f + i * 56f, panel.Size.X - 40f, 48f);
            var isSelected = qIndex == _selectedQuestIndex;

            if (isSelected)
                InkFrame.Selection(this, rowRect);
            InkFrame.RowRule(this, rowRect);

            InkDraw.TextBounded(this,
                new Rect2(rowRect.Position.X + 16f, rowRect.Position.Y, rowRect.Size.X - 32f, rowRect.Size.Y),
                quest.Name, 20, 14, isSelected ? InkStyle.Line : InkStyle.Dim, "lm");

            AddWidget(rowRect, WidgetAction.QuestRow, qIndex, true, quest.Name);
        }

        if (quests.Count > QuestListRowsPerPage)
        {
            var pagerArea = new Rect2(panel.Position.X + 30f, panel.End.Y - 56f,
                panel.Size.X - 60f, 44f);
            InkFrame.PageNavigation(this, pagerArea, startIndex, QuestListRowsPerPage, quests.Count);
            AddWidget(InkLayout.PagePager(pagerArea, -1), WidgetAction.QuestListPrev, 0,
                startIndex > 0, "‹");
            AddWidget(InkLayout.PagePager(pagerArea, 1), WidgetAction.QuestListNext, 0,
                startIndex + QuestListRowsPerPage < quests.Count, "›");
        }
    }

    private void DrawQuestDetailPanel(Rect2 panel)
    {
        InkFrame.Panel(this, panel, corner: 20f, rails: false);
        InkDraw.TextBounded(this, InkLayout.PanelHeading(panel), "任务详情", 24, 18);

        var quests = GetQuests();
        if (_selectedQuestIndex < 0 || _selectedQuestIndex >= quests.Count)
        {
            InkDraw.TextBounded(this, InkLayout.PanelText(panel), "请在左侧选择一项任务查看详情",
                20, 16, InkStyle.Dim, "cm");
            return;
        }

        var quest = quests[_selectedQuestIndex];
        var titleRect = new Rect2(panel.Position.X + 30f, panel.Position.Y + 76f,
            panel.Size.X - 60f, 44f);
        var noteRect = new Rect2(panel.Position.X + 30f, panel.Position.Y + 136f,
            panel.Size.X - 60f, panel.Size.Y - 176f);

        InkDraw.TextBounded(this, titleRect, quest.Name, 24, 18);
        InkDraw.Wrapped(this, noteRect,
            $"类别：{quest.Kind}\n冷却天数：{quest.CooldownDays}", 18, InkStyle.Dim);
    }

    private void AddWidget(Rect2 rect, WidgetAction action, int index, bool enabled, string label)
    {
        _widgets.Add(new QuestWidget(rect, action, index, enabled, label));
    }
}
