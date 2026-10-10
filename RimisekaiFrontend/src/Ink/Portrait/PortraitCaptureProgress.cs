using System;
using System.Linq;
using Godot;
using Rimisekai.Character;
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
            // 摆满到房间上限，第 MaxFacilities+1 件必须被拒。
            while (hub.State.Territory.HasFacilitySlot(hub.PlayerRoomId))
            {
                var fixture = template.ToRuntime();
                fixture.Id = ++id;
                fixture.RoomId = hub.PlayerRoomId;
                fixture.Built = true;
                Require(hub.State.Territory.AddFacility(fixture), "fixture list probe setup");
            }
            _facilityProbeLast = id;
            var extra = template.ToRuntime();
            extra.Id = id + 1;
            extra.RoomId = hub.PlayerRoomId;
            extra.Built = true;
            Require(!hub.State.Territory.AddFacility(extra), "room refuses facility past Room.MaxFacilities");
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() => ClickHub(PortraitAction.Cell, _root.HubScreen.DebugHub.PlayerRoomId));
        _steps.Enqueue(() => DragHubUp(new Rect2(0, PortraitLayout.RoomSheetRow(0).Position.Y, PortraitLayout.CanvasWidth,
            PortraitLayout.RoomSheetRows * 140f)));
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.Fixture && w.Index == _facilityProbeLast),
                "room sheet reaches last facility");
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
        EnqueueBedKickout();
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

        // 接下单战委托即战斗：任务页选单、点名、开打，核对真切入战斗页且 QuestRun 随行。
        _steps.Enqueue(() =>
        {
            _root.HubScreen.ShowTab(2);
        });
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.QuestTake),
                "quest board lists available commissions");
            // 普通战斗委托：地城探索委托走包接送的地城，另在大地图段核对。
            ClickHub(PortraitAction.QuestTake, _root.HubScreen.DebugQuestIndex(
                d => d.Kind == Rimisekai.Quest.QuestKind.Battle && d.Battle == Rimisekai.Quest.QuestBattle.Normal));
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
            // 点空地＝建造抽屉列房间（一项不藏），页签带「能建/总数」；看一眼不扣钱。
            var screen = _root.HubScreen;
            Require(screen.DebugWidgets.Count(w => w.Action == PortraitAction.BuildCategory) == 6
                && screen.DebugHub.State.Territory.VacantDevelopCount == _developmentProbeCount,
                "tapping a plot opens the room choices without charging");
            ClickLabel(PortraitAction.BuildCategory, "居室");
        });
        _steps.Enqueue(() => ClickLabel(PortraitAction.BuildTile, "客厅"));
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.BuildMain && w.Label == "开拓并建造" && w.Enabled),
                "plot card offers open-and-build");
            ClickHub(PortraitAction.BuildMain, 0);
        });
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            var room = hub.State.Territory.RoomAt(hub.RegionId, _developmentProbeX, _developmentProbeY);
            Require(room is { Name: "客厅", Vacant: false } && hub.State.Territory.VacantDevelopCount == _developmentProbeCount + 1,
                "open-and-build develops the plot and builds the room in one step");
            Require(hub.CanUndoBuild, "the build just made can be undone");
            Shoot("development_installed", _root.HubScreen);
            _root.HubScreen.DebugPress(PortraitAction.Back, 0);
        });
        EnqueueIndoorOnlyRow();
        _steps.Enqueue(() =>
        {
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
                {
                var n = result.Rows.Count;
                // 首人武器本场 1→2 级（挂上升箭头），其余停在本级中段，核对进度条。
                result.Rows.Add(new BattleResult.Row { CharacterId = member.Id, Name = member.Name, DamageDealt = 20, WeaponExp = 24, StyleExp = 12,
                    WeaponLevel = 1, StyleLevel = 1, WeaponTotalExp = n == 0 ? 212 : 30 + n * 20, StyleTotalExp = n == 1 ? 105 : 64,
                    NewSkill = n == 0 ? "slash" : "" });
            }
            var loot = new LootResult { Money = 10 };
            loot.Items.Add(("木材", 1));
            _root.ModalLayer.Show(InkModalFactory.CreateCombatSettlement(result, loot, _root.HubScreen.DebugHub.State.Territory, () => _modalChoices = 3));
        });
        _steps.Enqueue(() =>
        {
            Require(_root.ModalLayer.DebugPanel.Size.Y > 480f, "settlement data produces visible content layout");
            Shoot("settlement", _root.ModalLayer);
            Press(_root.ModalLayer, _root.ModalLayer.DebugPanel.GetCenter());
        });
        _steps.Enqueue(() => Require(_modalChoices == 3 && !_root.ModalLayer.IsActive,
            "display-only settlement advances by clicking inside panel"));
        // 战利品多到排不下：按价值排序（传说精金甲顶到最上）、超出容量转滚动（右下 ▼＋滑条）。
        _steps.Enqueue(() =>
        {
            for (var party = 1; party <= 4; party++)
                GD.Print($"settlement loot capacity: party {party} -> {PortraitModalLayer.SettlementLootCapacity(party)} strips");
            var territory = _root.HubScreen.DebugHub.State.Territory;
            var armor = EquipForge.ForgeArmor(EquipSlot.Torso, "精金", quality: Quality.Legendary, enchant: "", blessed: false, enhance: 0);
            territory.Equips.Add(armor);
            var result = new BattleResult { Outcome = CombatOutcome.AttackerWin, Rounds = 5 };
            foreach (var member in _root.HubScreen.DebugHub.State.Roster.Members.Take(4))
                {
                var n = result.Rows.Count;
                // 首人武器本场 1→2 级（挂上升箭头），其余停在本级中段，核对进度条。
                result.Rows.Add(new BattleResult.Row { CharacterId = member.Id, Name = member.Name, DamageDealt = 20, WeaponExp = 24, StyleExp = 12,
                    WeaponLevel = 1, StyleLevel = 1, WeaponTotalExp = n == 0 ? 212 : 30 + n * 20, StyleTotalExp = n == 1 ? 105 : 64,
                    NewSkill = n == 0 ? "slash" : "" });
            }
            var loot = new LootResult { Money = 120 };
            foreach (var m in new[] { "木材", "布", "皮", "珊瑚", "青铜", "铁", "钢", "秘银", "以太" })
                loot.Items.Add((m, 2));
            loot.Items.Add((armor.Id, 1));
            _root.ModalLayer.Show(InkModalFactory.CreateCombatSettlement(result, loot, territory, () => _modalChoices = 4));
            _sortedTop = armor.Name;
        });
        _steps.Enqueue(() =>
        {
            var data = _root.ModalLayer.Current!.Settlement!;
            Require(data.Items[0].Label == _sortedTop && data.Items[^1].ItemId == "木材", "settlement loot sorts by market value, rare gear first");
            Shoot("settlement_scroll", _root.ModalLayer);
            var at = new Vector2(_root.ModalLayer.DebugPanel.GetCenter().X, _root.ModalLayer.DebugPanel.Position.Y + 700f);
            for (var i = 0; i < 20; i++)
                _root.ModalLayer._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Position = at });
        });
        _steps.Enqueue(() =>
        {
            Shoot("settlement_scroll_end", _root.ModalLayer);
            var panel = _root.ModalLayer.DebugPanel;
            Press(_root.ModalLayer, new Vector2(panel.GetCenter().X, panel.End.Y - 134f));
        });
        _steps.Enqueue(() => Require(_modalChoices == 4 && !_root.ModalLayer.IsActive, "scrolling settlement still closes from its button"));
    }

    private string _sortedTop = "";
    private int _kickBed = -1;
    private int _kickFrom = -1;

    /// <summary>
    /// 被踢下床：女仆睡在卧室她自己那张床上（好感不够同床），玩家点那张床——不上床，底部弹提示签。
    /// </summary>
    private void EnqueueBedKickout()
    {
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            var territory = hub.State.Territory;
            var bedroom = territory.Facilities.First(f => f.Id == territory.MasterBedId).RoomId;
            var maid = hub.State.Roster.Members.First(m => !m.IsMaster && m.IsMaid());
            Require(!Intimacy.SharesBed(maid), "kick-out fixture: maid below the share-bed tier");
            hub.State.Roster.Master!.Bag.Add("木材", 10);
            Require(hub.BuildFacilityDef(DefDatabase<FacilityDef>.GetNamed("床").Id, bedroom), "kick-out fixture: maid bed built");
            _kickBed = territory.Facilities[^1].Id;
            var worker = hub.Day.Track(maid.Id, bedroom);
            hub.Day.EndRoutineOf(maid.Id);
            worker.RoomId = bedroom;
            worker.FacilityId = _kickBed;
            worker.Goal = ActionKind.Sleep;
            worker.Phase = WorkPhase.Working;
            _kickFrom = hub.PlayerRoomId;
            hub.Enter(bedroom);
            _root.HubScreen.ShowTab(0);
        });
        _steps.Enqueue(() => ClickHub(PortraitAction.Cell, _root.HubScreen.DebugHub.PlayerRoomId));
        _steps.Enqueue(() => ClickHub(PortraitAction.Fixture, _kickBed));
        _steps.Enqueue(() =>
        {
            var screen = _root.HubScreen;
            var hub = screen.DebugHub;
            Require(hub.UsingFixtureId != _kickBed, "kicked-out player is not placed in the bed");
            Require(hub.UseRefusal.Contains("踢") && screen.DebugNotice == hub.UseRefusal, "kick-out shows as a toast");
            Require(!hub.Log.Any(e => e.Text == hub.UseRefusal), "kick-out toast is not also logged");
            Shoot("bed_kickout", screen);
            hub.Day.EndRoutineOf(hub.State.Roster.Members.First(m => !m.IsMaster && m.IsMaid()).Id);
            screen.ShowTab(0);
        });
        EnqueueRoomLock();
        _steps.Enqueue(() =>
        {
            _root.HubScreen.DebugHub.Enter(_kickFrom); // 回到摆满设施的那间，后面的开发探针按它来
            _root.HubScreen.ShowTab(0);
        });
    }

    /// <summary>建造抽屉选中庭院（室外）：起居类里的「床」暗着加锁，详情卡「室内」打叉。</summary>
    private void EnqueueIndoorOnlyRow()
    {
        _steps.Enqueue(() =>
        {
            var cell = _root.HubScreen.DebugWidgets.First(w => w.Action == PortraitAction.DevelopmentCell && w.Label == "庭院");
            ClickHub(cell.Action, cell.Index);
        });
        _steps.Enqueue(() => ClickLabel(PortraitAction.BuildCategory, "起居"));
        _steps.Enqueue(() => ClickLabel(PortraitAction.BuildTile, "床"));
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.BuildMain && !w.Enabled),
                "bed cannot be built in the courtyard");
            Shoot("development_indoor_only", _root.HubScreen);
        });
    }

    /// <summary>按标签点一枚可用的命中块（建造抽屉里的页签、格子按名字找）。</summary>
    private void ClickLabel(PortraitAction action, string label)
    {
        var widget = _root.HubScreen.DebugWidgets.First(w => w.Action == action && w.Label == label && w.Enabled);
        Press(_root.HubScreen, widget.Rect.GetCenter());
    }

    /// <summary>自己的房间（主人的床那间）的抽屉：左钮是门锁，点一下换一档，标签与提示签跟着变。</summary>
    private void EnqueueRoomLock()
    {
        _steps.Enqueue(() => ClickHub(PortraitAction.Cell, _root.HubScreen.DebugHub.PlayerRoomId));
        _steps.Enqueue(() =>
        {
            var screen = _root.HubScreen;
            var hub = screen.DebugHub;
            Require(hub.CurrentRoomLockable(), "room lock fixture: player stands in own bedroom");
            Require(screen.DebugWidgets.Any(w => w.Action == PortraitAction.RoomLock && w.Label == "门·自动" && w.Enabled),
                "own room sheet shows the door lock in place of demolish");
            Require(!screen.DebugWidgets.Any(w => w.Action == PortraitAction.RoomDemolish), "lock replaces demolish");
            ClickHub(PortraitAction.RoomLock, hub.PlayerRoomId);
        });
        _steps.Enqueue(() =>
        {
            var screen = _root.HubScreen;
            var hub = screen.DebugHub;
            Require(hub.State.Territory.Rooms.First(r => r.Id == hub.PlayerRoomId).Lock == RoomLock.Locked, "lock click locks the door");
            Require(screen.DebugWidgets.Any(w => w.Action == PortraitAction.RoomLock && w.Label == "门·锁着"), "lock label follows state");
            Require(screen.DebugNotice.StartsWith("门锁上了"), "lock shows a toast");
            Shoot("room_lock", screen);
            ClickHub(PortraitAction.RoomLock, hub.PlayerRoomId);
        });
        _steps.Enqueue(() => ClickHub(PortraitAction.RoomLock, _root.HubScreen.DebugHub.PlayerRoomId));
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            Require(hub.State.Territory.Rooms.First(r => r.Id == hub.PlayerRoomId).Lock == RoomLock.Auto, "lock cycles back to auto");
            _root.HubScreen.DebugPress(PortraitAction.SheetClose, 0);
        });
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
