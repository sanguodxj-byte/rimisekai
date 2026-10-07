using System;
using System.Linq;
using Godot;
using Rimisekai.Combat;
using Rimisekai.Defs;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

public partial class PortraitCapture
{
    private int _scheduleProbeMember;
    private int _scheduleProbeFacility;
    private int _developmentProbeX;
    private int _developmentProbeY;
    private int _developmentProbeRoom;
    private int _developmentProbeCount;
    private int _modalChoices;
    private string _inputResult = "";
    private long _savedMoney;
    private int _facilityProbeLast;

    private void EnqueueProgressChecks()
    {
        _steps.Enqueue(() => _root.HubScreen.ShowTab(1));
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            _scheduleProbeMember = hub.State.Roster.Members.First(member => !member.IsMaster).Id;
            var fixture = hub.State.Territory.Facilities.First(f => f.Built && hub.FacilityIsWorkbench(f.Id));
            _scheduleProbeFacility = fixture.Id;
            _root.HubScreen.DebugPress(PortraitAction.RosterPick, _scheduleProbeMember);
        });
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.CharacterSegment, 2));
        _steps.Enqueue(() => ClickHub(PortraitAction.ScheduleSlot, 1));
        _steps.Enqueue(() =>
        {
            var fixture = _root.HubScreen.DebugHub.State.Territory.Facilities.First(f => f.Id == _scheduleProbeFacility);
            ClickHub(PortraitAction.ScheduleRoom, fixture.RoomId);
        });
        _steps.Enqueue(() => ClickHub(PortraitAction.ScheduleFacility, _scheduleProbeFacility));
        _steps.Enqueue(() =>
        {
            var assignment = _root.HubScreen.DebugHub.AssignmentOf(_scheduleProbeMember, 1);
            Require(assignment.Mode == SlotMode.Work && assignment.FacilityId == _scheduleProbeFacility,
                "schedule actual clicks assign selected member and slot");
            Shoot("schedule_assigned", _root.HubScreen);
            ClickHub(PortraitAction.ScheduleCancel, 1);
        });
        _steps.Enqueue(() =>
        {
            var assignment = _root.HubScreen.DebugHub.AssignmentOf(_scheduleProbeMember, 1);
            Require(assignment.Mode == SlotMode.Free && assignment.FacilityId == -1, "schedule cancel clears assignment");
            ClickHub(PortraitAction.ScheduleFacility, _scheduleProbeFacility);
        });
        _steps.Enqueue(() => ClickHub(PortraitAction.ScheduleFacility, _scheduleProbeFacility));
        _steps.Enqueue(() => Require(_root.HubScreen.DebugHub.AssignmentOf(_scheduleProbeMember, 1).Mode == SlotMode.Free,
            "schedule selecting assigned facility toggles it off"));

        _steps.Enqueue(() => _root.HubScreen.ShowTab(0));
        _steps.Enqueue(() => ClickHub(PortraitAction.OpenSystem, 0));
        _steps.Enqueue(() =>
        {
            _savedMoney = _root.HubScreen.DebugHub.State.Money;
            ClickHub(PortraitAction.SaveNow, 0);
        });
        _steps.Enqueue(() =>
        {
            Require(InkSaveStore.ListSaves().Count > 0, "in-game save entry writes a new save");
            Shoot("save", _root.HubScreen);
            _root.HubScreen.DebugHub.State.Money = _savedMoney + 100;
            _root.HubScreen.OpenSystemPage(InkSystemScreen.PageLoad);
        });
        _steps.Enqueue(() => ClickHub(PortraitAction.SavePick, 0));
        _steps.Enqueue(() => ClickModal("confirm"));
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugHub.State.Money == _savedMoney, "in-game load restores saved state");
            _root.HubScreen.ShowTab(0);
        });

        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            var template = DefDatabase<FacilityDef>.All.First(def => def.Actions.Count > 0);
            var id = hub.State.Territory.Facilities.Max(f => f.Id);
            for (var i = 0; i < 5; i++)
            {
                var fixture = template.ToRuntime();
                fixture.Id = ++id;
                fixture.RoomId = hub.PlayerRoomId;
                fixture.Built = true;
                Require(hub.State.Territory.AddFacility(fixture), "fixture list probe setup");
            }
            _facilityProbeLast = id;
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() => ClickHub(PortraitAction.Cell, _root.HubScreen.DebugHub.PlayerRoomId));
        _steps.Enqueue(() => DragHubUp(new Rect2(0, PortraitLayout.RoomSheetRow(0).Position.Y, PortraitLayout.CanvasWidth,
            PortraitLayout.RoomSheetRows * 140f)));
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.Fixture && w.Index == _facilityProbeLast),
                "fixture scrollbar reaches last facility");
            Shoot("fixtures_scrolled", _root.HubScreen);
            ClickHub(PortraitAction.Fixture, _facilityProbeLast);
        });
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.FixtureRun),
                "fixture click exposes declared facility actions");
            Shoot("fixture_actions", _root.HubScreen);
            _root.HubScreen.ShowTab(0);
        });
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            var member = hub.State.Roster.Members.First(m => !m.IsMaster);
            hub.Place(member.Id, hub.PlayerRoomId);
            hub.ClearSelection();
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            var member = hub.State.Roster.Members.First(m => !m.IsMaster);
            ClickHub(PortraitAction.NowAvatar, member.Id);
        });
        _steps.Enqueue(() => ClickHub(PortraitAction.SocialCategory, 0));
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.SocialRun && w.Index == 0),
                "roster selection exposes social actions");
            Shoot("social", _root.HubScreen);
            var hub = _root.HubScreen.DebugHub;
            var speaker = hub.State.Roster.Find(hub.SelectedCharacterId)!.Name;
            hub.Show(MapOverlay.Dialogue(speaker, new[] { "文本一", "文本二" }));
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() =>
        {
            _root.HubScreen._Process(5);
            Shoot("dialogue", _root.HubScreen);
            ClickHub(PortraitAction.ConversationAdvance, 0);
        });
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugHub.Overlay!.Text == "文本二", "dialogue actual click advances core line");
            _root.HubScreen.DebugHub.Overlay.Offer(new[] { new OverlayChoice(7, "选项一"), new OverlayChoice(9, "选项二") });
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() =>
        {
            Shoot("dialogue_choices", _root.HubScreen);
            ClickHub(PortraitAction.ConversationChoice, 9);
        });
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugHub.Overlay == null, "dialogue second choice dispatches correct core id");
            _root.HubScreen.ShowTab(3);
        });

        // 接下委托即战斗：任务页选单、点名、开打，核对真切入战斗页且 QuestRun 随行。
        _steps.Enqueue(() =>
        {
            _root.HubScreen.ShowTab(2);
        });
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.QuestTake),
                "quest board lists available commissions");
            ClickHub(PortraitAction.QuestTake, 0);
        });
        _steps.Enqueue(() =>
        {
            Shoot("party_sheet", _root.HubScreen);
            var member = _root.HubScreen.DebugWidgets.First(w => w.Action == PortraitAction.PartyPick && w.Enabled);
            ClickHub(member.Action, member.Index);
        });
        _steps.Enqueue(() => ClickHub(PortraitAction.QuestStart, 0));
        _steps.Enqueue(() =>
        {
            Require(_root.DebugPhase == Rimisekai.Flow.FlowPhase.Combat, "quest start enters combat");
            var combat = _root.DebugCombat!;
            Require(combat.QuestRun != null, "quest battle carries its quest run");
            Require(_root.CombatView.Visible, "combat view visible after quest start");
            Shoot("quest_battle", _root.CombatView);
            // 直接斩杀敌方全体并触发胜负判定，走真实结算路径收战回据点。
            foreach (var foe in combat.Battle.Members.Where(m => m.Side != combat.Battle.ControlledSide))
                foe.Hp = 0;
            combat.Battle.JudgeOutcome();
            _root.CombatView._Process(0.4);
        });
        _steps.Enqueue(() =>
        {
            Require(_root.ModalLayer.IsActive, "quest victory shows settlement modal");
            Shoot("quest_settlement", _root.ModalLayer);
            Press(_root.ModalLayer, _root.ModalLayer.DebugPanel.GetCenter());
        });
        _steps.Enqueue(() =>
        {
            Require(!_root.ModalLayer.IsActive, "quest settlement advances");
            Require(_root.DebugCombat == null, "finished quest battle returns to hub");
            Require(_root.DebugPhase == Rimisekai.Flow.FlowPhase.Hub, "hub phase restored after quest battle");
        });

        _steps.Enqueue(() => _root.HubScreen.ShowTab(0));
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.Build, 0));
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            hub.State.Money = 100000;
            foreach (var def in DefDatabase<RoomDef>.All)
                foreach (var cost in def.MaterialCost)
                    hub.State.Roster.Master!.Bag.Add(cost.ItemId, 100);
            hub.State.Roster.Master!.Bag.Add("木材", 100);
            hub.State.Roster.Master!.Bag.Add("石材", 100);
            var widget = _root.HubScreen.DebugWidgets.First(w => w.Action == PortraitAction.DevelopmentCell && w.Label == "空地");
            _developmentProbeX = (int)((widget.Rect.Position.X - PortraitLayout.DevelopmentGrid.Position.X) / PortraitLayout.DevCell);
            _developmentProbeY = (int)((widget.Rect.Position.Y - PortraitLayout.DevelopmentGrid.Position.Y) / PortraitLayout.DevCell);
            _developmentProbeCount = hub.State.Territory.VacantDevelopCount;
            ClickHub(widget.Action, widget.Index);
        });
        _steps.Enqueue(() =>
        {
            Shoot("develop_confirm", _root.ModalLayer);
            ClickModal("cancel");
        });
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugHub.State.Territory.VacantDevelopCount == _developmentProbeCount,
                "development cancellation does not charge or develop");
            var widget = _root.HubScreen.DebugWidgets.First(w => w.Action == PortraitAction.DevelopmentCell &&
                w.Rect.Position == PortraitLayout.DevelopmentCell(_developmentProbeX, _developmentProbeY).Position);
            ClickHub(widget.Action, widget.Index);
        });
        _steps.Enqueue(() => ClickModal("confirm"));
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            var room = hub.State.Territory.RoomAt(hub.RegionId, _developmentProbeX, _developmentProbeY);
            Require(room is { Vacant: true } && hub.State.Territory.VacantDevelopCount == _developmentProbeCount + 1,
                "development confirmation creates vacant room through core");
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() =>
        {
            var row = _root.HubScreen.DebugWidgets.First(w => w.Action == PortraitAction.DevelopmentAction && w.Enabled);
            ClickHub(row.Action, row.Index);
        });
        _steps.Enqueue(() => ClickHub(PortraitAction.DevelopmentTab, 2));
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            var room = hub.State.Territory.Rooms.Last(r => r.X < 0);
            _developmentProbeRoom = room.Id;
            var widget = _root.HubScreen.DebugWidgets.First(w => w.Action == PortraitAction.DevelopmentRoom && w.Label == room.Name);
            ClickHub(widget.Action, widget.Index);
        });
        _steps.Enqueue(() =>
        {
            var widget = _root.HubScreen.DebugWidgets.First(w => w.Action == PortraitAction.DevelopmentCell &&
                w.Rect.Position == PortraitLayout.DevelopmentCell(_developmentProbeX, _developmentProbeY).Position);
            ClickHub(widget.Action, widget.Index);
        });
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            Require(hub.State.Territory.RoomAt(hub.RegionId, _developmentProbeX, _developmentProbeY)?.Id == _developmentProbeRoom,
                "development installs selected built room into vacant cell");
            Shoot("development_installed", _root.HubScreen);
            _root.ModalLayer.Show(InkModalFactory.CreateQuestion("标题", "正文",
                new[] { ("first", "选项一"), ("second", "选项二") }, id => _modalChoices = id == "second" ? 2 : 1));
        });
        _steps.Enqueue(() =>
        {
            Shoot("question", _root.ModalLayer);
            ClickModal("second");
        });
        _steps.Enqueue(() =>
        {
            Require(_modalChoices == 2 && !_root.ModalLayer.IsActive, "modal second option is reachable");
            var page = InkModalFactory.CreateInputQuestion("标题", "正文", "", 24, text => _inputResult = text);
            page.Input!.Text = "输入值";
            _root.ModalLayer.Show(page);
        });
        _steps.Enqueue(() => ClickModal("confirm"));
        _steps.Enqueue(() =>
        {
            Require(_inputResult == "输入值" && !_root.ModalLayer.IsActive, "modal input confirmation dispatches submitted text");
            var result = new BattleResult { Outcome = CombatOutcome.AttackerWin, Rounds = 3 };
            foreach (var member in _root.HubScreen.DebugHub.State.Roster.Members.Take(4))
                result.Rows.Add(new BattleResult.Row { CharacterId = member.Id, Name = member.Name, DamageDealt = 20, WeaponExp = 4, StyleExp = 2 });
            var loot = new LootResult { Money = 10 };
            loot.Items.Add(("木材", 1));
            _root.ModalLayer.Show(InkModalFactory.CreateCombatSettlement(result, loot, () => _modalChoices = 3));
        });
        _steps.Enqueue(() =>
        {
            Require(_root.ModalLayer.DebugPanel.Size.Y > 480f, "settlement data produces visible content layout");
            Shoot("settlement", _root.ModalLayer);
            Press(_root.ModalLayer, _root.ModalLayer.DebugPanel.GetCenter());
        });
        _steps.Enqueue(() => Require(_modalChoices == 3 && !_root.ModalLayer.IsActive,
            "display-only settlement advances by clicking inside panel"));
    }

    private void ClickHub(PortraitAction action, int index)
    {
        var widget = _root.HubScreen.DebugWidgets.First(w => w.Action == action && w.Index == index && w.Enabled);
        Press(_root.HubScreen, widget.Rect.GetCenter());
    }

    private void ClickModal(string id)
    {
        var widget = _root.ModalLayer.DebugWidgets.First(w => w.Action == PortraitAction.ModalChoice && w.Label == id && w.Enabled);
        Press(_root.ModalLayer, widget.Rect.GetCenter());
    }

    private static void Press(Control control, Vector2 position)
    {
        var viewport = control.GetViewport();
        viewport.PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position });
        foreach (var pressed in new[] { true, false })
            viewport.PushInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left, Pressed = pressed, Position = position, GlobalPosition = position,
            });
    }

    /// <summary>在滚动区里自下而上拖内容（反复几次，确保拖到底）。</summary>
    private void DragHubUp(Rect2 area)
    {
        for (var i = 0; i < 6; i++)
        {
            var bottom = new Vector2(area.GetCenter().X, area.End.Y - 8f);
            var top = new Vector2(area.GetCenter().X, area.Position.Y + 8f);
            _root.HubScreen._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = bottom });
            _root.HubScreen._GuiInput(new InputEventMouseMotion { Position = top, ButtonMask = MouseButtonMask.Left });
            _root.HubScreen._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = top });
            _root.HubScreen.QueueRedraw();
        }
    }
}
