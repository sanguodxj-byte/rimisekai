using System;
using System.Collections.Generic;
using Godot;
using Rimisekai.Combat;
using Rimisekai.Hub;
using Rimisekai.Session;

namespace Rimisekai.Ink;

/// <summary>
/// 战斗页：独立的整页战斗界面。
/// 左侧参战人员、右侧战况日志、下方行动指令栏；无战斗实例时呈规范空态。
/// </summary>
public partial class InkCombatScreen : Control
{
    public event Action? CloseRequested;

    private HubSession? _hub;
    private BattleSession? _battleSession;

    private int _selectedTargetId = -1;
    private int _selectedSkillIndex;

    private int _hoveredWidgetIndex = -1;
    private readonly List<CombatWidget> _widgets = new();

    private enum WidgetAction
    {
        Close,
        Target,
        Skill,
        Act,
        Flee,
    }

    private readonly record struct CombatWidget(
        Rect2 Rect,
        WidgetAction Action,
        int Index,
        bool Enabled,
        string Label);

    /// <summary>绑定据点会话（无战斗时据此显示空态与日期）。</summary>
    public void Bind(HubSession? hub)
    {
        _hub = hub;
        QueueRedraw();
    }

    /// <summary>接入一场真实战斗；之后本页就是这场的指挥台。</summary>
    public void BindBattle(BattleSession battleSession)
    {
        _battleSession = battleSession;
        _selectedTargetId = -1;
        _selectedSkillIndex = 0;
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

    private void Dispatch(CombatWidget widget)
    {
        switch (widget.Action)
        {
            case WidgetAction.Close:
                CloseRequested?.Invoke();
                break;

            case WidgetAction.Target:
                _selectedTargetId = widget.Index;
                QueueRedraw();
                break;

            case WidgetAction.Skill:
                _selectedSkillIndex = widget.Index;
                QueueRedraw();
                break;

            case WidgetAction.Act:
                ExecuteAction();
                break;

            case WidgetAction.Flee:
                _battleSession?.Flee();
                QueueRedraw();
                break;
        }
    }

    private void ExecuteAction()
    {
        var battle = _battleSession?.Battle;
        var pending = battle?.PendingActor;
        if (battle == null || pending == null)
            return;

        var menu = battle.Menu();
        if (_selectedSkillIndex < 0 || _selectedSkillIndex >= menu.Count)
            return;

        var skill = menu[_selectedSkillIndex];
        var targetId = _selectedTargetId;
        if (targetId < 0)
        {
            var targets = battle.ResolveTargets(pending, skill, -1);
            if (targets.Count > 0)
                targetId = targets[0].Id;
        }

        var action = new CombatAction
        {
            ActorId = pending.Id,
            SkillId = skill.Id,
            TargetId = targetId,
        };

        if (battle.CanAct(action))
        {
            battle.Act(action);
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        _widgets.Clear();

        var subtitle = _hub != null
            ? $"{_hub.State.Clock.Year}年 {_hub.State.Clock.Day}日"
            : "";
        InkFrame.PageShell(this, "战斗", wood: false, subtitle: subtitle);

        if (_battleSession == null)
            DrawEmpty();
        else
            DrawBattle(_battleSession.Battle);

        AddWidget(InkLayout.FullPageClose, WidgetAction.Close, 0, true, "关闭");
    }

    private void DrawEmpty()
    {
        var panel = InkLayout.SubpageBody;
        InkFrame.Panel(this, panel, corner: 20f, rails: false);

        var center = panel.GetCenter();
        InkDraw.TextBounded(this, new Rect2(center.X - 300f, center.Y - 60f, 600f, 40f),
            "当前未处在战斗中", 24, 18, InkStyle.Dim, "cm");
        InkDraw.TextBounded(this, new Rect2(center.X - 300f, center.Y - 10f, 600f, 30f),
            "没有活跃的战斗实例，可直接返回据点", 18, 14, InkStyle.Dim, "cm");

        var backRect = new Rect2(center.X - 100f, center.Y + 50f, 200f, 48f);
        InkFrame.Button(this, backRect, "返回据点", fontSize: 20, centered: true);
        AddWidget(backRect, WidgetAction.Close, 0, true, "返回据点");
    }

    private void DrawBattle(Battle battle)
    {
        DrawMembers(InkLayout.JourneyMap, battle);
        DrawLog(InkLayout.JourneyList, battle);
        DrawActions(InkLayout.JourneyDetail, battle);
    }

    private void DrawMembers(Rect2 panel, Battle battle)
    {
        InkFrame.Panel(this, panel, corner: 20f, rails: false);
        InkDraw.TextBounded(this, InkLayout.PanelHeading(panel), $"回合 {battle.Round} · 参战人员", 24, 18);

        var members = battle.Members;
        var startY = panel.Position.Y + 76f;
        const float rowH = 64f;

        for (var i = 0; i < members.Count && i < 7; i++)
        {
            var m = members[i];
            var rowRect = new Rect2(panel.Position.X + 24f, startY + i * (rowH + 8f),
                panel.Size.X - 48f, rowH);
            var isControlled = m.Side == battle.ControlledSide;
            var isPending = battle.PendingActor?.Id == m.Id;
            var isSelected = m.Id == _selectedTargetId;

            if (isSelected || isPending)
                InkFrame.Selection(this, rowRect);
            else
                InkFrame.CardOutline(this, rowRect);

            var sideTag = isControlled ? "[本方]" : "[敌方]";
            var status = m.Alive ? $"HP {m.Hp}/{m.MaxHp}  MP {m.Mp}/{m.MaxMp}" : "倒下";
            var label = $"{sideTag} {m.Name}";

            InkDraw.TextBounded(this,
                new Rect2(rowRect.Position.X + 16f, rowRect.Position.Y + 10f, 300f, 24f),
                label, 20, 16, m.Alive ? InkStyle.Line : InkStyle.Dim, "lm");
            InkDraw.TextBounded(this,
                new Rect2(rowRect.Position.X + 16f, rowRect.Position.Y + 36f, 300f, 20f),
                status, 16, 14, InkStyle.Dim, "lm");

            if (m.MaxHp > 0)
            {
                var meter = new Rect2(rowRect.End.X - 220f, rowRect.GetCenter().Y - 6f, 200f, 12f);
                InkDraw.Meter(this, meter, (float)m.Hp / m.MaxHp);
            }

            if (m.Side != battle.ControlledSide && m.Alive)
                AddWidget(rowRect, WidgetAction.Target, m.Id, true, m.Name);
        }
    }

    private void DrawLog(Rect2 panel, Battle battle)
    {
        InkFrame.Panel(this, panel, corner: 20f, rails: false);
        InkDraw.TextBounded(this, InkLayout.PanelHeading(panel), "战况日志", 24, 18);

        var events = battle.Events;
        var startY = panel.Position.Y + 76f;
        const int maxLines = 10;
        var startIdx = Math.Max(0, events.Count - maxLines);

        for (var i = 0; i < maxLines && startIdx + i < events.Count; i++)
        {
            var lineRect = new Rect2(panel.Position.X + 20f, startY + i * 44f,
                panel.Size.X - 40f, 36f);
            InkDraw.TextBounded(this, lineRect, FormatEvent(events[startIdx + i], battle),
                18, 14, InkStyle.Dim, "lm");
        }
    }

    private static string FormatEvent(BattleEvent ev, Battle battle)
    {
        var actor = battle.Members.Find(m => m.Id == ev.ActorId)?.Name ?? "未知";
        var target = battle.Members.Find(m => m.Id == ev.TargetId)?.Name ?? "未知";

        return ev.Kind switch
        {
            CombatEventKind.Hit => $"{actor} 对 {target} 造成了 {ev.Amount} 点伤害",
            CombatEventKind.Miss => $"{actor} 的攻击未命中 {target}",
            CombatEventKind.Heal => $"{actor} 为 {target} 恢复了 {ev.Amount} 点生命",
            CombatEventKind.Flee => ev.Amount == 1
                ? $"{actor} 成功带领队伍脱离了战斗"
                : $"{actor} 尝试撤退但未能成功",
            CombatEventKind.Round => $"--- 第 {ev.Round} 回合 ---",
            CombatEventKind.End => "战斗结束",
            _ => $"{actor} 采取了行动",
        };
    }

    private void DrawActions(Rect2 panel, Battle battle)
    {
        InkFrame.Panel(this, panel, corner: 18f, rails: false);

        var pending = battle.PendingActor;
        if (pending == null || battle.Outcome != CombatOutcome.Ongoing)
        {
            var outcome = battle.Outcome switch
            {
                CombatOutcome.AttackerWin => "战斗胜利",
                CombatOutcome.DefenderWin => "战斗结束",
                CombatOutcome.Fled => "已撤退",
                CombatOutcome.Draw => "平局收场",
                _ => "等待指令",
            };
            InkDraw.TextBounded(this, InkLayout.PanelText(panel), outcome, 22, 16, InkStyle.Dim, "lm");

            var leaveRect = InkLayout.JourneyAction(0, 1);
            InkFrame.Button(this, leaveRect, "离开", fontSize: 20, centered: true);
            AddWidget(leaveRect, WidgetAction.Close, 0, true, "离开");
            return;
        }

        InkDraw.TextBounded(this,
            new Rect2(panel.Position.X + 30f, panel.Position.Y + 24f, 400f, 36f),
            $"行动者：{pending.Name} (MP {pending.Mp}/{pending.MaxMp})", 22, 16, InkStyle.Line, "lm");

        var menu = battle.Menu();
        var skillStartX = panel.Position.X + 440f;
        for (var i = 0; i < menu.Count && i < 4; i++)
        {
            var skill = menu[i];
            var btnRect = new Rect2(skillStartX + i * 160f, panel.Position.Y + 20f, 144f, 44f);
            var label = $"{skill.Name} ({skill.MpCost})";

            InkFrame.Button(this, btnRect, label, selected: i == _selectedSkillIndex,
                fontSize: 18, centered: true);
            AddWidget(btnRect, WidgetAction.Skill, i, true, label);
        }

        var targetMember = battle.Members.Find(m => m.Id == _selectedTargetId);
        var targetText = targetMember != null
            ? $"当前目标：{targetMember.Name} (HP {targetMember.Hp})"
            : "目标：未选定 (将默认索敌)";
        InkDraw.TextBounded(this,
            new Rect2(panel.Position.X + 30f, panel.Position.Y + 80f, 600f, 36f),
            targetText, 18, 14, InkStyle.Dim, "lm");

        var actBtn = InkLayout.JourneyAction(0, 2);
        var fleeBtn = InkLayout.JourneyAction(1, 2);
        InkFrame.Button(this, actBtn, "行动", fontSize: 22, centered: true);
        AddWidget(actBtn, WidgetAction.Act, 0, true, "行动");
        InkFrame.Button(this, fleeBtn, "撤退", fontSize: 22, centered: true);
        AddWidget(fleeBtn, WidgetAction.Flee, 0, true, "撤退");
    }

    private void AddWidget(Rect2 rect, WidgetAction action, int index, bool enabled, string label)
    {
        _widgets.Add(new CombatWidget(rect, action, index, enabled, label));
    }
}
