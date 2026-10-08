using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;
using Rimisekai.Combat;
using Rimisekai.Hub;
using Rimisekai.Ink;
using Rimisekai.Session;

namespace Rimisekai.Portrait;

/// <summary>从真实竖屏根场景导出 PNG、命中多边形与容器几何，并运行导航/绘制回归。</summary>
public partial class PortraitCapture : Node
{
    private string _prefix = "";
    private string _dump = "";
    private string _blind = "";
    private SubViewport _sub = null!;
    private PortraitRoot _root = null!;
    private readonly StringBuilder _json = new();
    private readonly Queue<Action> _steps = new();
    private int _wait;
    private BattleSession _battleProbe = null!;
    private int _probeHp;
    private long _probeTime;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--pcap=")) _prefix = arg["--pcap=".Length..];
            if (arg.StartsWith("--pdump=")) _dump = arg["--pdump=".Length..];
            if (arg.StartsWith("--pblind=")) _blind = arg["--pblind=".Length..];
        }
        if (_blind.Length > 0)
        {
            ExportBlindIcons(_blind);
            Quit();
            return;
        }
        if (_prefix.Length == 0 && _dump.Length == 0)
            return;
        // 截图与命中块核对一律取终态：动效直接跳完（CheckMotion 里临时关掉，专门核对过渡本身）。
        PortraitMotion.Instant = true;
        if (_prefix.Length > 0)
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_prefix))!);
        _root = new PortraitRoot { Name = "PortraitRoot" };
        InkSaveStore.OverrideDirectory = Path.Combine(Path.GetTempPath(), $"rimisekai-portrait-{Guid.NewGuid():N}").Replace('\\', '/');
        _sub = new SubViewport
        {
            Size = new Vector2I((int)PortraitLayout.CanvasWidth, (int)PortraitLayout.CanvasHeight),
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(_sub);
        _sub.AddChild(_root);
        _steps.Enqueue(() => Shoot("title", _root.TitleView));
        _steps.Enqueue(() => PressTitle(PortraitLayout.TitleButton(2)));
        _steps.Enqueue(() => CheckTitlePage("settings", InkSystemScreen.PageSettings));
        _steps.Enqueue(() => PressTitle(PortraitLayout.PageBack));
        _steps.Enqueue(() => PressTitle(PortraitLayout.TitleButton(1)));
        _steps.Enqueue(() => CheckTitlePage("load", InkSystemScreen.PageLoad));
        _steps.Enqueue(() => PressTitle(PortraitLayout.PageBack));
        _steps.Enqueue(() => PressTitle(PortraitLayout.TitleButton(0)));
        _steps.Enqueue(PrepareRosterAndIcons);
        for (var tab = 0; tab < PortraitLayout.TabCount; tab++)
        {
            var index = tab;
            _steps.Enqueue(() => _root.HubScreen.ShowTab(index));
            _steps.Enqueue(() => Shoot($"tab{index}", _root.HubScreen));
        }
        // 仓储三段
        _steps.Enqueue(() => _root.HubScreen.ShowTab(3));
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.StoreSegment, 1));
        _steps.Enqueue(() => Shoot("store_trade", _root.HubScreen));
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.StoreSegment, 2));
        _steps.Enqueue(() => Shoot("store_craft", _root.HubScreen));
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.StoreSegment, 0));
        // 角色详情三段＋技能星盘
        _steps.Enqueue(() => _root.HubScreen.ShowTab(1));
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.RosterPick, _root.HubScreen.DebugHub.State.Roster.Master!.Id));
        _steps.Enqueue(() => { Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.CharacterSegment), "character page segments"); Shoot("char_status", _root.HubScreen); });
        // 特质签、装备格可点：各弹一枚纯展示弹窗（标题＝特质名 / 装备名或槽名）。
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.TraitInfo, 0));
        _steps.Enqueue(() =>
        {
            var trait = _root.HubScreen.DebugWidgets.FirstOrDefault(w => w.Action == PortraitAction.TraitInfo && w.Index == 0);
            Require(trait.Label != null && _root.ModalLayer.IsActive && _root.ModalLayer.Current?.Title == trait.Label,
                "trait tag opens its detail popup");
            Shoot("char_trait_popup", _root.HubScreen);
            _root.ModalLayer.Dismiss();
            _root.HubScreen.DebugPan("character", 4000);
        });
        _steps.Enqueue(() => _root.HubScreen.QueueRedraw());
        _steps.Enqueue(() => { Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.EquipInfo), "equipment slots clickable"); Shoot("char_equip", _root.HubScreen); });
        // 装备格点进装备页：背包塞一顶测试头盔（真实锻造入口），换上、长按看详情、卸下回背包。
        var helmetId = "";
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            var material = Rimisekai.Defs.DefDatabase<Rimisekai.Defs.MaterialDef>.All.First(m => m.ArmorUsable);
            var helmet = Rimisekai.Defs.EquipForge.ForgeArmor(Rimisekai.Defs.EquipSlot.Head, material.DefName);
            hub.State.Equips.Add(helmet);
            hub.State.Roster.Master!.Bag.Add(helmet.Id, 1);
            helmetId = helmet.Id;
            _root.HubScreen.DebugPress(PortraitAction.EquipInfo, 2);
        });
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.EquipSlotPick)
                && _root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.EquipOption && w.Label == helmetId),
                "equipment slot opens the equipment page with bag candidates");
            Shoot("equip_page", _root.HubScreen);
            _root.HubScreen.DebugHold(PortraitAction.EquipOption, 0);
        });
        _steps.Enqueue(() =>
        {
            Require(_root.ModalLayer.IsActive && _root.ModalLayer.Current?.Body.Length > 0, "holding a candidate shows its details");
            Shoot("equip_hold", _root.HubScreen);
            _root.ModalLayer.Dismiss();
            _root.HubScreen.DebugPress(PortraitAction.EquipOption, 0);
        });
        _steps.Enqueue(() =>
        {
            var master = _root.HubScreen.DebugHub.State.Roster.Master!;
            Require(master.EquippedId(Rimisekai.Defs.EquipSlot.Head) == helmetId && master.Bag.Get(helmetId) == 0,
                "tapping a candidate equips it");
            Shoot("equip_swapped", _root.HubScreen);
            _root.HubScreen.DebugPress(PortraitAction.EquipRemove, 2);
        });
        _steps.Enqueue(() =>
        {
            var master = _root.HubScreen.DebugHub.State.Roster.Master!;
            Require(master.EquippedId(Rimisekai.Defs.EquipSlot.Head) == "" && master.Bag.Get(helmetId) == 1,
                "unequip returns the piece to the bag");
            _root.HubScreen.DebugPress(PortraitAction.Back, 0);
            _root.HubScreen.DebugPan("character", 0);
        });
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.CharacterSegment, 2));
        _steps.Enqueue(() => { Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.ScheduleSlot), "schedule timeline blocks"); Shoot("char_schedule", _root.HubScreen); });
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.CharacterSegment, 1));
        _steps.Enqueue(() => Shoot("char_skills", _root.HubScreen));
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.OpenDisc, 0));
        _steps.Enqueue(() => ClickSkillPolygon(PortraitAction.SkillSector, 0));
        _steps.Enqueue(() => { CheckSkills(); Shoot("skills_focus", _root.HubScreen); });
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.SkillReset, 0));
        _steps.Enqueue(() => Require(_root.HubScreen.DebugSkillSector == -1, "skill reset"));
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.Back, 0));
        // 设施抽屉、建造、系统
        _steps.Enqueue(() => _root.HubScreen.ShowTab(0));
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.Cell, _root.HubScreen.DebugHub.PlayerRoomId));
        _steps.Enqueue(() => { Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.RoomGo), "room sheet opens"); Shoot("room_sheet", _root.HubScreen); });
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.SheetClose, 0));
        // 点别的房间格＝当场前往（不弹抽屉）；长按房间格才弹设施抽屉；「此刻」只列同区的人。
        _steps.Enqueue(TapOtherRoom);
        _steps.Enqueue(CheckMovedWithoutSheet);
        _steps.Enqueue(() => _root.HubScreen.ShowTab(4));
        _steps.Enqueue(() => Shoot("log_timeline", _root.HubScreen));
        _steps.Enqueue(() => _root.HubScreen.ShowTab(0));
        _steps.Enqueue(() => Hold(_root.HubScreen, _holdAt = CellCenter(_homeRoom)));
        _steps.Enqueue(() => _root.HubScreen._Process(PortraitMotion.LongPress + 0.05));
        _steps.Enqueue(CheckLongPressSheet);
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.RoomGo, _homeRoom));
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugHub.PlayerRoomId == _homeRoom, "room sheet go returns home");
            _root.HubScreen.DebugPress(PortraitAction.SheetClose, 0);
        });
        _steps.Enqueue(CheckMotion);
        _steps.Enqueue(() => _root.HubScreen._Process(0.06));
        _steps.Enqueue(() => Shoot("motion_sheet_mid", _root.HubScreen));
        _steps.Enqueue(FinishMotion);
        _steps.Enqueue(CheckSheetClosed);
        _steps.Enqueue(() => { Require(_root.HubScreen.DebugAnimating, "push page slide-in runs"); _root.HubScreen._Process(0.06); });
        _steps.Enqueue(() => Shoot("motion_push_mid", _root.HubScreen));
        _steps.Enqueue(() => { _root.HubScreen._Process(1); _root.HubScreen.DebugPress(PortraitAction.Back, 0); });
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugAnimating && _root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.Back),
                "push page slide-out plays before back");
            _root.HubScreen._Process(1);
        });
        _steps.Enqueue(() =>
        {
            Require(!_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.Back), "push page gone after slide-out");
            _root.HubScreen.ShowTab(2);
        });
        _steps.Enqueue(() => { Require(_root.HubScreen.DebugAnimating, "tab arch slides"); _root.HubScreen._Process(0.07); });
        _steps.Enqueue(() => Shoot("motion_tab_mid", _root.HubScreen));
        _steps.Enqueue(() => { _root.HubScreen._Process(1); _root.HubScreen.ShowTab(0); });
        _steps.Enqueue(() =>
        {
            _root.HubScreen._Process(1);
            Require(!_root.HubScreen.DebugAnimating && _root.HubScreen.DebugTab == 0, "motion settles back on territory");
            // 移动过渡：点一间连通的别房，状态当场改好，磨砂遮罩＋脚印图标盖住画面。
            var hub = _root.HubScreen.DebugHub;
            _veilFrom = hub.PlayerRoomId;
            var target = hub.Map().First(r => r.Open && r.Id != _veilFrom && hub.CanReach(r.Id)).Id;
            _root.HubScreen.DebugPress(PortraitAction.Cell, target);
            Require(hub.PlayerRoomId == target && _root.HubScreen.DebugVeil is { Running: true, Icon: VeilIcon.Move },
                "moving plays the frosted veil with the move icon");
            _root.HubScreen.DebugVeil!.Step(0.4f);
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() => Shoot("veil_move_mid", _root.HubScreen));
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugAnimating, "veil locks input while playing");
            _root.HubScreen._Process(2);
            Require(!(_root.HubScreen.DebugVeil?.Running ?? false), "veil clears after its run");
            _root.HubScreen.DebugHub.Arrive(_veilFrom);
            PortraitMotion.Instant = true;
            _root.HubScreen.SetProcess(true);
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.Build, 0));
        _steps.Enqueue(() => Shoot("build", _root.HubScreen));
        _steps.Enqueue(() =>
        {
            // 建造页「操作」分段首几行是选中房间四面的门：点一下封墙、再点开门，Links 随之变化。
            var screen = _root.HubScreen;
            var door = screen.DebugWidgets.FirstOrDefault(w => w.Action == PortraitAction.DevelopmentDoor && w.Enabled);
            Require(door.Rect.Size.Y >= PortraitLayout.TouchMin, "build page lists the selected room's doors");
            _doorIndex = door.Index;
            _linkCount = LinkCount();
            screen.DebugPress(PortraitAction.DevelopmentDoor, _doorIndex);
            Require(LinkCount() != _linkCount, "door row toggles the connection");
            screen.QueueRedraw();
        });
        _steps.Enqueue(() => Shoot("build_door", _root.HubScreen));
        _steps.Enqueue(() =>
        {
            _root.HubScreen.DebugPress(PortraitAction.DevelopmentDoor, _doorIndex);
            Require(LinkCount() == _linkCount, "door row toggles back");
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.Back, 0));
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.OpenSystem, 0));
        _steps.Enqueue(() => Shoot("system", _root.HubScreen));
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.Back, 0));
        _steps.Enqueue(OpenStorage);
        _steps.Enqueue(() => Shoot("storage", _root.HubScreen));
        _steps.Enqueue(PrepareOverflowCases);
        _steps.Enqueue(() => Shoot("storage_long", _root.HubScreen));
        _steps.Enqueue(() => DragHubUp(new Rect2(0, PortraitLayout.StorageRow(0).Position.Y, PortraitLayout.CanvasWidth, PortraitLayout.StorageRows * PortraitLayout.SheetRowStep)));
        _steps.Enqueue(() =>
        {
            var rows = _root.HubScreen.DebugHub.StorageRows();
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.StoreOut && w.Index == rows.Count - 1),
                "storage scrollbar reaches last item without covering transfer buttons");
            Shoot("storage_scrolled", _root.HubScreen);
        });
        _steps.Enqueue(() => { _root.HubScreen.DebugPress(PortraitAction.SheetClose, 0); _root.HubScreen.ShowTab(0); });
        _steps.Enqueue(CheckCrowdedTerritory);
        _steps.Enqueue(() => Shoot("territory_crowded", _root.HubScreen));
        _steps.Enqueue(PressNowPager);
        _steps.Enqueue(CheckNowPageTurned);
        _steps.Enqueue(CheckNowPageWrapped);
        _steps.Enqueue(() =>
        {
            // 开局前排留空：StartBattle 应收拢阵型——空列后排一路顶到第一排，同列被挡者停在阻挡者身后。
            var probe = new Rimisekai.Combat.Battle();
            probe.Add(Rimisekai.Combat.Deploy.FromEnemy(new Rimisekai.Catalog.EnemyDef
            {
                Id = "front_a", Name = "前甲", MaxHp = 9, ThreatTier = 4, Column = 1,
            }, 900, Rimisekai.Combat.CombatSide.Defender));
            probe.Add(Rimisekai.Combat.Deploy.FromEnemy(new Rimisekai.Catalog.EnemyDef
            {
                Id = "rear_a", Name = "后甲", MaxHp = 9, ThreatTier = 1, Column = 1,
            }, 901, Rimisekai.Combat.CombatSide.Defender));
            probe.Add(Rimisekai.Combat.Deploy.FromEnemy(new Rimisekai.Catalog.EnemyDef
            {
                Id = "rear_b", Name = "后乙", MaxHp = 9, ThreatTier = 2, Column = 2,
            }, 902, Rimisekai.Combat.CombatSide.Defender));
            probe.StartBattle();
            var tiers = probe.Members.Select(m => m.ThreatTier).OrderByDescending(t => t).ToList();
            Require(tiers.SequenceEqual(new[] { 4, 4, 3 }), "empty columns compact at battle start, blocked rear hugs blocker");
        });
        EnqueueProgressChecks();
        foreach (var size in new[] { 1, 2, 3, 4 })
        {
            var capturedSize = size;
            _steps.Enqueue(() => BeginBattleProbe(capturedSize));
            _steps.Enqueue(() =>
            {
                Require(_battleProbe.Battle.Outcome == CombatOutcome.Ongoing && TotalHp(_battleProbe) == _probeHp
                    && _battleProbe.Battle.Time == _probeTime, "combat rendering does not advance battle");
                CheckAvatars();
                Shoot(capturedSize == 1 ? "combat" : $"boss{capturedSize}", _root.CombatView);
            });
        }
        _steps.Enqueue(CheckCombatButtons);
        _steps.Enqueue(() => Shoot("combat_item_popup", _root.CombatView));
        _steps.Enqueue(CheckSkillPopupFromButton);
        _steps.Enqueue(() => Shoot("combat_skill_popup", _root.CombatView));
        _steps.Enqueue(DispatchAttackViaButton);
        for (var attempt = 0; attempt < 12; attempt++)
            _steps.Enqueue(CheckFleeButtonAttempt);
        _steps.Enqueue(() =>
            Require(_root.DebugPhase == Rimisekai.Flow.FlowPhase.Hub && !_root.ModalLayer.IsActive,
                "flee button ends the battle back to hub"));
        _steps.Enqueue(PrepareColumnProbe);
        _steps.Enqueue(() =>
        {
            var rear = _battleProbe.Battle.Members.Where(m => m.Side != _battleProbe.Battle.ControlledSide && m.ThreatTier == 1);
            Require(rear.All(m => _root.CombatView.DebugWidgets.Any(w => w.Action == PortraitAction.CombatTarget && w.Index == m.Id && !w.Enabled)),
                "melee cannot target behind a multi-cell boss");
        });
        _steps.Enqueue(ArmRangedProbe);
        _steps.Enqueue(() =>
        {
            Shoot("combat_ranged", _root.CombatView);
            var boss = _battleProbe.Battle.Members.First(m => m.Side != _battleProbe.Battle.ControlledSide && m.Size == 2);
            var card = PortraitLayout.EnemyCard(boss);
            Press(_root.CombatView, new Vector2(card.Position.X + card.Size.X * 0.75f, card.GetCenter().Y));
        });
        _steps.Enqueue(() => Require(_root.CombatView.DebugSelectedColumn == 3,
            "right half of a two-cell boss targets the clicked column"));
        _steps.Enqueue(() => Shoot("combat_settled", _root.CombatView));
        _steps.Enqueue(CheckCombatSettings);
        _steps.Enqueue(() => _root.ModalLayer.Choose("advance"));
        _steps.Enqueue(ReopenCombatSettings);
        _steps.Enqueue(() => Shoot("combat_settings", _root.CombatView));
        _steps.Enqueue(CheckCombatLoadAndTitle);
        _steps.Enqueue(Quit);
    }

    /// <summary>
    /// 逃跑键单次尝试：战斗已结束则跳过；行动者未就绪先推进时钟；
    /// 按下后逃跑是概率制（基准 60%），失败由后续尝试步骤重试（步骤间隔有重绘，命中表新鲜）。
    /// </summary>
    private void CheckFleeButtonAttempt()
    {
        if (_root.DebugPhase != Rimisekai.Flow.FlowPhase.Combat)
            return;
        for (var tick = 0; tick < 24 && _root.CombatView.DebugActorId < 0; tick++)
            _root.CombatView._Process(0.4);
        if (_root.CombatView.DebugActorId < 0)
            return;
        _root.CombatView.DebugPress(PortraitAction.CombatMenu, 3);
    }

    /// <summary>战斗设置链路（上）：齿轮打开设置弹窗并确认四项、保存成功。</summary>
    private void CheckCombatSettings()
    {
        _root.CombatView.DebugPress(PortraitAction.CombatSettings, 0);
        var page = _root.ModalLayer.Current;
        Require(page?.Title == "设置"
            && page.Choices.Select(c => c.Label).SequenceEqual(new[] { "保存进度", "读取进度", "回到主界面", "返回" }),
            "combat settings popup offers save load title back");
        _root.ModalLayer.Choose("save");
        Require(_root.ModalLayer.Current?.Body == "已保存。", "combat settings saves progress");
    }

    /// <summary>战斗设置链路（中）：重开弹窗（供截图）。</summary>
    private void ReopenCombatSettings()
    {
        _root.CombatView.DebugPress(PortraitAction.CombatSettings, 0);
        Require(_root.ModalLayer.Current?.Title == "设置", "combat settings popup reopens");
    }

    /// <summary>战斗设置链路（下）：读取进度页含返回、返回回设置、关闭、回到主界面。</summary>
    private void CheckCombatLoadAndTitle()
    {
        _root.ModalLayer.Choose("load");
        var load = _root.ModalLayer.Current;
        Require(load?.Title == "读取进度" && load.Choices.Any(c => c.Label == "返回") && load.Choices.Count > 1,
            "combat load page lists saves with back");
        _root.ModalLayer.Choose("back");
        Require(_root.ModalLayer.Current?.Title == "设置", "combat load page back returns to settings");
        _root.ModalLayer.Choose("back");
        Require(!_root.ModalLayer.IsActive, "combat settings back closes");
        _root.CombatView.DebugPress(PortraitAction.CombatSettings, 0);
        _root.ModalLayer.Choose("title");
        Require(_root.DebugPhase == Rimisekai.Flow.FlowPhase.Title, "combat settings returns to title");
    }

    private int _veilFrom;
    private int _doorIndex;
    private int _linkCount;

    private int LinkCount() => _root.HubScreen.DebugHub.State.Territory.Rooms.Sum(r => r.Links.Count);

    private int _homeRoom = -1;
    private int _otherRoom = -1;
    private Vector2 _holdAt;

    private Vector2 CellCenter(int roomId) =>
        _root.HubScreen.DebugWidgets.First(w => w.Action == PortraitAction.Cell && w.Index == roomId).Rect.GetCenter();

    private void TapOtherRoom()
    {
        var hub = _root.HubScreen.DebugHub;
        _homeRoom = hub.PlayerRoomId;
        _otherRoom = hub.Map().First(r => r.Open && r.Id != _homeRoom && hub.CanReach(r.Id)).Id;
        Tap(_root.HubScreen, CellCenter(_otherRoom));
    }

    private void CheckMovedWithoutSheet()
    {
        var screen = _root.HubScreen;
        var hub = screen.DebugHub;
        Require(hub.PlayerRoomId == _otherRoom, "cell tap moves player there");
        Require(!screen.DebugWidgets.Any(w => w.Action == PortraitAction.RoomGo), "cell tap does not open sheet");
        var rooms = hub.State.Territory.Rooms;
        var region = rooms.First(r => r.Id == hub.PlayerRoomId).RegionId;
        Require(region == hub.RegionId, "player region tracks player room");
        var party = hub.Party();
        Require(screen.DebugWidgets.Where(w => w.Action == PortraitAction.NowAvatar).All(w =>
            party.First(c => c.Id == w.Index) is var card
            && (card.IsPlayer || card.RoomId == hub.PlayerRoomId)), "now strip lists same-room only");
        Require(party.Where(c => c.IsPlayer || c.RoomId == hub.PlayerRoomId).Count() >= screen.DebugWidgets.Count(w => w.Action == PortraitAction.NowAvatar)
            && !screen.DebugWidgets.Any(w => w.Action == PortraitAction.NowAvatar && party.First(c => c.Id == w.Index).RoomId != hub.PlayerRoomId
                && !party.First(c => c.Id == w.Index).IsPlayer), "characters in other rooms are hidden, not dimmed");
        var sealedRoom = rooms.FirstOrDefault(r => r.Open && r.RegionId == region && r.Id != hub.PlayerRoomId && !hub.CanReach(r.Id));
        if (sealedRoom != null)
        {
            var before = hub.PlayerRoomId;
            Require(!hub.Arrive(sealedRoom.Id) && hub.PlayerRoomId == before, "unconnected room cannot be entered");
        }
        var room = rooms.First(r => r.Id == hub.PlayerRoomId);
        Require(hub.History.Count > 0 && hub.History[^1].Kind == LogKind.Scene && hub.History[^1].Text.StartsWith($"你来到了{room.Name}"),
            "cell tap writes a scene log entry");
        Require(screen.DebugWidgets.Any(w => w.Action == PortraitAction.Tab && w.Index == 4 && w.Rect == PortraitLayout.LogPanel),
            "log panel taps through to log tab");
        Require(PortraitLayout.LogPanel.Size.Y >= 400f && PortraitLayout.LogPanel.End.Y < PortraitLayout.MapFrame.Position.Y
            && PortraitLayout.NowStrip.End.Y <= PortraitLayout.TravelButton.Position.Y && PortraitLayout.MapCell >= PortraitLayout.TouchMin,
            "log panel is tall and grid plus strip sit below it");
        Require(LogEntry.Compose("细雨落在菜垄上。", "你闻到了泥土的气味。") == "细雨落在菜垄上，你闻到了泥土的气味。"
            && LogEntry.Compose("天气转为雨天。", "") == "天气转为雨天。", "log entry joins a and b with a comma");
        Shoot("territory_moved", screen);
    }

    /// <summary>满员：格内至多 4 枚（3 枚＋「+」），「此刻」每页 4 人并出翻页三角钮。</summary>
    private void CheckCrowdedTerritory()
    {
        var screen = _root.HubScreen;
        var hub = screen.DebugHub;
        Require(PortraitHubScreen.PieceSlots(4) == (4, false) && PortraitHubScreen.PieceSlots(5) == (3, true)
            && PortraitHubScreen.PieceSlots(18) == (3, true), "cell pieces cap at four with a plus mark");
        Require(PortraitLayout.PieceStep * (PortraitLayout.PieceCap - 1) + PortraitLayout.PieceHeight * 0.5f
            <= PortraitLayout.CellPieces(PortraitLayout.Cell(0, 0)).Size.X, "four pieces fit inside a cell");
        var crowd = hub.Party().Count(c => c.RoomId == hub.PlayerRoomId);
        Require(crowd > PortraitLayout.PieceCap, "crowded room fixture");
        var avatars = screen.DebugWidgets.Where(w => w.Action == PortraitAction.NowAvatar).ToArray();
        Require(avatars.Length == PortraitLayout.NowPageSize, "now strip shows four per page");
        var pager = screen.DebugWidgets.FirstOrDefault(w => w.Action == PortraitAction.NowPage);
        Require(pager.Rect.Size.X >= PortraitLayout.TouchMin && pager.Rect.Size.Y >= PortraitLayout.TouchMin
            && avatars.All(a => a.Rect.End.X <= pager.Rect.Position.X), "now strip pager sits right of the fourth avatar");
    }

    private int[] _nowFirstPage = Array.Empty<int>();

    private int[] NowIds() =>
        _root.HubScreen.DebugWidgets.Where(w => w.Action == PortraitAction.NowAvatar).Select(w => w.Index).ToArray();

    private int NowPages()
    {
        var hub = _root.HubScreen.DebugHub;
        var total = hub.Party().Count(c => c.IsPlayer || c.RoomId == hub.PlayerRoomId);
        return (total + PortraitLayout.NowPageSize - 1) / PortraitLayout.NowPageSize;
    }

    /// <summary>翻页（上）：记下首页，按一次三角钮。</summary>
    private void PressNowPager()
    {
        _nowFirstPage = NowIds();
        _root.HubScreen.DebugPress(PortraitAction.NowPage, 0);
    }

    /// <summary>翻页（中）：换成了下 4 人；再按到翻过末页。</summary>
    private void CheckNowPageTurned()
    {
        Require(NowIds().Length > 0 && !NowIds().Intersect(_nowFirstPage).Any(), "now pager turns to the next four");
        for (var i = 1; i < NowPages(); i++)
            _root.HubScreen.DebugPress(PortraitAction.NowPage, 0);
    }

    /// <summary>翻页（下）：翻过末页回到首页。</summary>
    private void CheckNowPageWrapped() =>
        Require(NowIds().SequenceEqual(_nowFirstPage), "now pager wraps to the first page");

    private void CheckLongPressSheet()
    {
        var screen = _root.HubScreen;
        Require(screen.DebugWidgets.Any(w => w.Action == PortraitAction.RoomGo && w.Index == _homeRoom && w.Enabled),
            "long press opens room sheet");
        Release(screen, _holdAt);
    }

    /// <summary>
    /// 过渡本身：关掉直跳，开抽屉 / 推入页 / 切页签，确认过渡在走、期间不收输入，
    /// 推进 1 秒后状态落定、命中块是终态布局。
    /// </summary>
    private void CheckMotion()
    {
        var screen = _root.HubScreen;
        var hub = screen.DebugHub;
        PortraitMotion.Instant = false;
        // 帧时长随渲染负载漂移：停掉自走的 _Process，由核对步骤手动推进，结果不随机器快慢变。
        screen.SetProcess(false);
        screen.DebugPress(PortraitAction.Cell, hub.PlayerRoomId);
        screen.QueueRedraw();
    }

    private void FinishMotion()
    {
        var screen = _root.HubScreen;
        Require(screen.DebugAnimating, "sheet slide-in runs");
        Require(screen.DebugWidgets.Any(w => w.Action == PortraitAction.RoomGo), "sheet hit blocks registered at final layout");
        // 过渡中点压暗区不应收起（输入锁住）。
        Tap(screen, new Vector2(PortraitLayout.CanvasWidth / 2f, 200f));
        screen._Process(1);
        Require(!screen.DebugAnimating, "sheet slide-in settles");
        Require(screen.DebugWidgets.Any(w => w.Action == PortraitAction.RoomGo), "input ignored during sheet transition");
        screen.DebugPress(PortraitAction.SheetClose, 0);
        Require(screen.DebugWidgets.Any(w => w.Action == PortraitAction.RoomGo) && screen.DebugAnimating,
            "sheet close plays before state changes");
        screen._Process(1);
    }

    private void CheckSheetClosed()
    {
        var screen = _root.HubScreen;
        Require(!screen.DebugWidgets.Any(w => w.Action == PortraitAction.RoomGo), "sheet closed after slide-out");
        screen.DebugPress(PortraitAction.Build, 0);
    }

    private static void Tap(Control control, Vector2 position)
    {
        Hold(control, position);
        Release(control, position);
    }

    private static void Hold(Control control, Vector2 position)
    {
        var viewport = control.GetViewport();
        viewport.PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position });
        viewport.PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = true, Position = position, GlobalPosition = position,
        });
    }

    private static void Release(Control control, Vector2 position) =>
        control.GetViewport().PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = false, Position = position, GlobalPosition = position,
        });

    private void PrepareRosterAndIcons()
    {
        var hub = _root.HubScreen.DebugHub;
        var generator = new Rimisekai.Character.CharacterGenerator(new Random(20261003));
        while (hub.State.Roster.Members.Count < 4)
        {
            var member = generator.Generate(hub.State.Roster).State;
            hub.Place(member.Id, hub.PlayerRoomId);
        }
        foreach (var name in new[] { "木材", "铁矿", "药草", "书本", "面包" })
        {
            Require(InkIcon.Get(name) != null, $"icon resource {name}");
            hub.State.Roster.Master!.Bag.Add(name, 5);
        }
    }

    private void PrepareOverflowCases()
    {
        var hub = _root.HubScreen.DebugHub;
        foreach (var definition in Rimisekai.Defs.DefDatabase<Rimisekai.Defs.ThingDef>.All.Take(20))
            hub.State.Roster.Master!.Bag.Add(definition.DefName, 1);
        var generator = new Rimisekai.Character.CharacterGenerator(new Random(20261004));
        while (hub.State.Roster.Members.Count < 18)
        {
            var member = generator.Generate(hub.State.Roster).State;
            hub.Place(member.Id, hub.PlayerRoomId);
        }
        Require(hub.StorageRows().Count > PortraitLayout.StorageRows, "long storage fixture");
        _root.HubScreen.QueueRedraw();
    }

    private void PressTitle(Rect2 rect)
    {
        foreach (var pressed in new[] { true, false })
            _root.TitleView._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left, Pressed = pressed, Position = rect.GetCenter(),
            });
    }
    private void CheckTitlePage(string tag, string page)
    {
        Require(_root.TitleView.DebugSystemPage == page, $"title navigation {page}");
        Shoot(tag, _root.TitleView);
    }
    private void ClickSkillPolygon(PortraitAction action, int index)
    {
        var widget = _root.HubScreen.DebugWidgets.First(w => w.Action == action && w.Index == index && w.Enabled);
        for (var y = 1; y < 20; y++)
            for (var x = 1; x < 20; x++)
            {
                var point = widget.Rect.Position + widget.Rect.Size * new Vector2(x / 20f, y / 20f);
                if (!widget.Contains(point))
                    continue;
                Press(_root.HubScreen, point);
                return;
            }
        Require(false, "skill polygon has an actual clickable interior");
    }

    private void PrepareColumnProbe()
    {
        BeginBattleProbe(2);
        _root.EnterCombat(_battleProbe);
        _root.CombatView._Process(0.4);
        _root.CombatView.SetProcess(false);
    }

    /// <summary>等一名我方行动者就绪，再经技能钮装备该远程式。</summary>
    private void ArmRangedProbe()
    {
        for (var i = 0; i < 24 && _root.CombatView.DebugActorId < 0; i++)
            _root.CombatView._Process(0.4);
        var actor = _battleProbe.Battle.Members.First(m => m.Id == _root.CombatView.DebugActorId);
        var skill = SkillTable.All.First(s => s.Range == Rimisekai.Catalog.SkillRange.Ranged && s.Target == Rimisekai.Catalog.SkillTarget.Enemy);
        actor.Skills.Remove(skill.Id);
        actor.Skills.Insert(0, skill.Id);
        _root.EnterCombat(_battleProbe);
        for (var i = 0; i < 24 && _root.CombatView.DebugActorId < 0; i++)
            _root.CombatView._Process(0.4);
        _root.CombatView.SetProcess(false);
        // 经技能钮装备该远程式：2×2 面板 → 技能弹窗 → 首个选项即此式。
        _root.CombatView.DebugPress(PortraitAction.CombatMenu, 1);
        Require(_root.ModalLayer.IsActive, "combat skill popup opens for skill page");
        var first = _root.ModalLayer.Current!.Choices.First(c => c.Enabled);
        Require(first.Label == skill.Name, $"skill page lists the ranged style first, got [{first.Label}]");
        _root.ModalLayer.Choose(first.Id);
        Require(!_root.ModalLayer.IsActive, "picking a skill closes popup for targeting");
    }

    private void CheckSkills()
    {
        Require(_root.HubScreen.DebugSkillSector == 0, "skill sector focus");
        var expected = _root.HubScreen.DebugSkillIds;
        Require(expected.Length > 0, "focused sector has real skill definitions");
        var seen = new HashSet<string>();
        for (var i = 0; i < expected.Length; i++)
        {
            seen.Add(_root.HubScreen.DebugSelectedSkill);
            _root.HubScreen.DebugPress(PortraitAction.SkillNext, 0);
        }
        Require(expected.All(seen.Contains), "navigation reaches every focused skill");
        var selected = _root.HubScreen.DebugSelectedSkill;
        _root.HubScreen.DebugPress(PortraitAction.SkillPrevious, 0);
        _root.HubScreen.DebugPress(PortraitAction.SkillNext, 0);
        Require(_root.HubScreen.DebugSelectedSkill == selected, "previous-next roundtrip");
    }
    private void OpenStorage()
    {
        _root.HubScreen.ShowTab(0);
        var hub = _root.HubScreen.DebugHub;
        foreach (var fixture in hub.State.Territory.Facilities)
            if (fixture.Built && fixture.CanStore)
            {
                hub.OpenStorage(fixture.Id);
                return;
            }
    }
    private void BeginBattleProbe(int size)
    {
        var hub = _root.HubScreen.DebugHub;
        foreach (var member in hub.State.Roster.Members)
            member.Condition.RecoverFull();
        var foes = new List<Rimisekai.Catalog.EnemyDef>();
        if (size == 1)
            for (var i = 1; i <= 4; i++)
                foes.Add(new Rimisekai.Catalog.EnemyDef
                {
                    Id = $"probe_{i}", Name = $"敌 {i}", MaxHp = 20, Attack = 3,
                    ThreatTier = i, Column = i, Speed = 1,
                });
        else
        {
            foes.Add(new Rimisekai.Catalog.EnemyDef
            {
                Id = "probe_boss", Name = "首领", MaxHp = size * 120, Attack = 3,
                ThreatTier = 4, Column = size == 4 ? 1 : 2, Size = size, Speed = 1,
                ActionPoints = size, Portrait = "monster_boss_hobgoblin",
            });
            if (size == 2)
                foreach (var column in new[] { 1, 2, 3, 4 })
                    foes.Add(new Rimisekai.Catalog.EnemyDef
                    {
                        Id = $"probe_flank{column}", Name = $"敌 {column}", MaxHp = 30,
                        Attack = 2, ThreatTier = column is 2 or 3 ? 1 : 2, Column = column, Speed = 1,
                        Portrait = "monster_goblin",
                    });
        }
        _battleProbe = Encounters.Start(hub.State, foes)!;
        var tiers = new[] { 0, 2, 3, 5 };
        var allies = _battleProbe.Battle.Members.Where(m => m.Side == _battleProbe.Battle.ControlledSide).ToArray();
        for (var i = 0; i < allies.Length; i++)
        {
            allies[i].ThreatTier = tiers[i];
            // 夹具 buff：让卡上方图标条真实显示（铁壁人人一枚，瑞菌之外再挂迅捷）。
            allies[i].Statuses.Add(new StatusEffect
            {
                Token = "probe_iron", Name = "铁壁", Category = StatusCategory.Buff,
                Kind = StatusKind.StatMod, Stat = Rimisekai.Catalog.StatusStat.Defence, Percent = 50,
            });
            if (i > 0)
                allies[i].Statuses.Add(new StatusEffect
                {
                    Token = "probe_swift", Name = "迅捷", Category = StatusCategory.Buff,
                    Kind = StatusKind.StatMod, Stat = Rimisekai.Catalog.StatusStat.Speed, Percent = 30,
                });
            // 末位再挂 4 枚，凑满两排（6 枚），验证双排与两位/三位数角标。
            if (i == allies.Length - 1)
                foreach (var pct in new[] { 100, 15, 200, 5 })
                    allies[i].Statuses.Add(new StatusEffect
                    {
                        Token = $"probe_extra_{pct}", Name = pct % 2 == 0 ? "铁壁" : "迅捷", Category = StatusCategory.Buff,
                        Kind = StatusKind.StatMod, Stat = Rimisekai.Catalog.StatusStat.Defence, Percent = pct,
                    });
        }
        _root.EnterCombat(_battleProbe);
        _root.CombatView._Process(0.4);
        _root.CombatView.SetProcess(false);
        _probeHp = TotalHp(_battleProbe);
        _probeTime = _battleProbe.Battle.Time;
    }
    private void CheckAvatars()
    {
        var cards = _root.CombatView.DebugWidgets.Where(w => w.Action == PortraitAction.CombatAct).ToArray();
        Require(cards.Length == 4, "four bottom avatar cards");
        // 2026-10-07 重设计：我方卡一排等高（不再按威胁档错层）。
        Require(cards.Select(w => w.Rect.Position.Y).Distinct().Count() == 1, "ally cards share one row");
        Require(cards.All(w => PortraitLayout.CombatAvatars.Encloses(w.Rect)), "avatars contained in bottom region");
    }
    /// <summary>操作面板 2×2 四钮：核对标签，点「道具」开道具页（含返回）。</summary>
    private void CheckCombatButtons()
    {
        Require(_root.CombatView.DebugActorId >= 0, "controlled actor ready for input");
        var labels = Enumerable.Range(0, 4)
            .Select(i => _root.CombatView.DebugWidgets.First(w => w.Action == PortraitAction.CombatMenu && w.Index == i).Label)
            .ToList();
        Require(labels.SequenceEqual(new[] { "攻击", "技能", "道具", "逃跑" }),
            "action panel lists attack skill item flee");
        _root.CombatView.DebugPress(PortraitAction.CombatMenu, 2);
        Require(_root.ModalLayer.Current?.Title == "道具"
            && _root.ModalLayer.Current.Choices.Any(c => c.Label == "返回"),
            "item button opens the item popup");
    }

    /// <summary>道具页返回后点「技能」：技能页含防御架势与返回、不含普攻。</summary>
    private void CheckSkillPopupFromButton()
    {
        _root.ModalLayer.Choose("back");
        _root.CombatView.DebugPress(PortraitAction.CombatMenu, 1);
        Require(_root.ModalLayer.IsActive, "skill button opens the skill popup");
        Require(!_root.ModalLayer.Current!.Choices.Any(c => c.Id == Rimisekai.Combat.BattleSkills.AttackId)
            && _root.ModalLayer.Current.Choices.Any(c => c.Id == Rimisekai.Combat.BattleSkills.GuardId)
            && _root.ModalLayer.Current.Choices.Any(c => c.Label == "返回"),
            "skill popup lists guard and back but not the basic attack");
    }

    /// <summary>技能页返回后点「攻击」，点选敌卡派发战斗动作。</summary>
    private void DispatchAttackViaButton()
    {
        _root.ModalLayer.Choose("back");
        _root.CombatView.DebugPress(PortraitAction.CombatMenu, 0);
        var target = _root.CombatView.DebugWidgets.First(w => w.Action == PortraitAction.CombatTarget && w.Enabled);
        var before = _battleProbe.Battle.Events.Count;
        _root.CombatView.DebugPress(target.Action, target.Index);
        Require(_battleProbe.Battle.Events.Count > before, "enemy card dispatches battle action");
    }
    private static int TotalHp(BattleSession session) => session.Battle.Members.Sum(member => member.Hp);

    /// <summary>
    /// 按实机 40×40 尺寸导出全部标准图标，供匿名盲审（`--pblind=&lt;目录&gt;`）。
    /// 走 InkIcon 的真实处理链：SVG → 120×120 Lanczos + 3×3 高斯羽化 → Lanczos 降到 40×40 →
    /// 把羽化 alpha 烘进 RGB 合成到纯黑底（等价实机「骨白墨色 + 黑底」的观感）。
    /// 产物：raw/NN.png（40×40 原样）、view/NN.png（最近邻放大 6 倍 240×240，不加细节）、key.txt（序号 → 图标名）。
    /// </summary>
    private static void ExportBlindIcons(string dir)
    {
        var rawDir = Path.Combine(dir, "raw");
        var viewDir = Path.Combine(dir, "view");
        Directory.CreateDirectory(rawDir);
        Directory.CreateDirectory(viewDir);

        var names = InkIcon.StandardNames;
        var key = new StringBuilder();
        var exported = 0;
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            var num = (i + 1).ToString("D2");
            var src = InkIcon.GetProcessedImage(name);
            if (src == null)
            {
                GD.PushError($"Rimisekai: 盲审导出缺少图标 {name}");
                continue;
            }

            // 1) 120×120 处理图 → Lanczos 降到 40×40（等价实机 GPU 三线性降采样）
            var small = (Image)src.Duplicate();
            small.Resize(40, 40, Image.Interpolation.Lanczos);
            small.Convert(Image.Format.Rgba8);

            // 2) 羽化 alpha 烘进 RGB，合成到纯黑底
            for (var y = 0; y < 40; y++)
            {
                for (var x = 0; x < 40; x++)
                {
                    var p = small.GetPixel(x, y);
                    small.SetPixel(x, y, new Color(p.R * p.A, p.G * p.A, p.B * p.A, 1f));
                }
            }
            small.Convert(Image.Format.Rgb8);
            if (small.SavePng(Path.Combine(rawDir, $"{num}.png")) != Error.Ok)
            {
                GD.PushError($"Rimisekai: 盲审导出写 raw 失败 {name}");
                continue;
            }

            // 3) 40×40 最近邻放大 6 倍到 240×240（不加细节，供目视判读）
            var view = (Image)small.Duplicate();
            view.Resize(240, 240, Image.Interpolation.Nearest);
            if (view.SavePng(Path.Combine(viewDir, $"{num}.png")) != Error.Ok)
            {
                GD.PushError($"Rimisekai: 盲审导出写 view 失败 {name}");
                continue;
            }

            key.Append(num).Append('\t').Append(name).Append('\n');
            exported++;
        }
        File.WriteAllText(Path.Combine(dir, "key.txt"), key.ToString(), new UTF8Encoding(false));
        GD.Print($"blind icons exported: {exported}/{names.Count} -> {dir}");
    }

    private void Require(bool condition, string name)
    {
        if (!condition)
        {
            _steps.Clear();
            GD.PushError($"portrait check failed: {name}");
            GetTree().Quit(1);
            throw new InvalidOperationException(name);
        }
        GD.Print($"portrait check passed: {name}");
    }
    public override void _Process(double delta)
    {
        if (_steps.Count == 0 || ++_wait < 3)
            return;
        _wait = 0;
        _steps.Dequeue()();
    }
    private void Shoot(string tag, Node source)
    {
        if (_prefix.Length > 0)
        {
            var path = $"{_prefix}_{tag}.png";
            Require(_sub.GetTexture().GetImage().SavePng(path) == Error.Ok, $"capture {tag}");
            GD.Print($"portrait ok: {path}");
        }
        if (_dump.Length == 0)
            return;
        var regions = source is PortraitCombatView combat ? combat.DebugRegions
            : source is PortraitHubScreen hub ? hub.DebugVisualRegions
            : source is PortraitModalLayer modal ? new[] { new PortraitRegion("modal", modal.DebugPanel) }
            : new[] { new PortraitRegion("title", new Rect2(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight)) };
        foreach (var region in regions)
            _json.AppendLine(JsonSerializer.Serialize(new
            {
                page = tag, kind = "region", action = region.Name, index = 0, label = region.Name,
                enabled = false, x = region.Rect.Position.X, y = region.Rect.Position.Y,
                w = region.Rect.Size.X, h = region.Rect.Size.Y,
            }));
        var widgets = source is PortraitTitleView title ? title.DebugWidgets
            : source is PortraitHubScreen screen ? screen.DebugWidgets
            : source is PortraitModalLayer modalLayer ? modalLayer.DebugWidgets
            : ((PortraitCombatView)source).DebugWidgets;
        foreach (var widget in widgets)
        {
            var owner = regions.FirstOrDefault(region => region.Rect.Encloses(widget.Rect)).Name;
            _json.AppendLine(JsonSerializer.Serialize(new
            {
                page = tag, kind = "widget", action = widget.Action.ToString(), index = widget.Index,
                enabled = widget.Enabled, label = widget.Label, owner,
                x = widget.Rect.Position.X, y = widget.Rect.Position.Y, w = widget.Rect.Size.X, h = widget.Rect.Size.Y,
                polygon = widget.Polygon?.Select(point => new[] { point.X, point.Y }).ToArray(),
            }));
        }
    }
    private void Quit()
    {
        if (_dump.Length > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_dump)!);
            File.WriteAllText(_dump, _json.ToString());
            GD.Print($"portrait widgets ok: {_dump}");
        }
        GetTree().Quit();
    }
}
