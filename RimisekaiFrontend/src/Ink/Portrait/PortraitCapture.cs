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
    private (int X, int Y) _travelTarget;
    private int _poiStepDir;
    private string _dump = "";
    private string _blind = "";
    private SubViewport _sub = null!;
    private PortraitRoot _root = null!;
    private readonly StringBuilder _json = new();
    private readonly Queue<Action> _steps = new();
    private int _wait;
    private BattleSession _battleProbe = null!;
    /// <summary>世界种子：默认 42 让画面可复现；<c>--pseed=</c> 换种子看别的世界。</summary>
    private int _seed = 42;
    private int _probeHp;
    private long _probeTime;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--pcap=")) _prefix = arg["--pcap=".Length..];
            if (arg.StartsWith("--pdump=")) _dump = arg["--pdump=".Length..];
            if (arg.StartsWith("--pblind=")) _blind = arg["--pblind=".Length..];
            if (arg.StartsWith("--pseed=")) _seed = int.Parse(arg["--pseed=".Length..]);
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
        InkWorldBootstrap.WorldSeedOverride = _seed;
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
        // 还没有存档：「继续」压暗不可点，按了不进读档页。
        _steps.Enqueue(() =>
        {
            Require(_root.TitleView.DebugWidgets.Any(w => w.Index == 1 && !w.Enabled)
                && _root.TitleView.DebugWidgets.Where(w => w.Index != 1).All(w => w.Enabled), "continue is disabled without saves");
            PressTitle(PortraitLayout.TitleButton(1));
        });
        _steps.Enqueue(() => Require(_root.TitleView.DebugSystemPage == "", "disabled continue does nothing"));
        _steps.Enqueue(() => PressTitle(PortraitLayout.TitleButton(0)));
        _steps.Enqueue(PrepareRosterAndIcons);
        for (var tab = 0; tab < PortraitLayout.TabCount; tab++)
        {
            var index = tab;
            _steps.Enqueue(() => _root.HubScreen.ShowTab(index));
            _steps.Enqueue(() => Shoot($"tab{index}", _root.HubScreen));
        }
        // 图鉴只收录怪物表；装备实例属性运行时生成，物品定义不是图鉴目录。
        // 图鉴入口在系统页「设置」段（2026-10-10 主人定：移出 HUD）。HUD 上不再有图鉴钮。
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.All(widget => widget.Action != PortraitAction.CodexOpen), "HUD carries no codex entry");
            _root.HubScreen.DebugPress(PortraitAction.OpenSystem, 0);
        });
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.SystemSegment, 1));
        _steps.Enqueue(() =>
        {
            var widgets = _root.HubScreen.DebugWidgets;
            var codex = widgets.First(widget => widget.Action == PortraitAction.CodexOpen);
            Require(codex.Rect.Size.X >= PortraitLayout.TouchMin && codex.Rect.Size.Y >= PortraitLayout.TouchMin
                && widgets.Where(widget => widget.Action != PortraitAction.CodexOpen).All(widget => !widget.Rect.Intersects(codex.Rect)),
                "settings codex entry meets the touch minimum and overlaps no other target");
            Shoot("system_settings", _root.HubScreen);
            _root.HubScreen.DebugPress(PortraitAction.CodexOpen, 0);
        });
        _steps.Enqueue(() =>
        {
            var widgets = _root.HubScreen.DebugWidgets;
            Require(widgets.Any(widget => widget.Action == PortraitAction.CodexEntry)
                && widgets.All(widget => widget.Action is PortraitAction.CodexEntry or PortraitAction.Back or PortraitAction.ScrollTrack),
                "codex contains monster entries only, without equipment or item selectors");
            Shoot("codex_monsters", _root.HubScreen);
            _root.HubScreen.DebugPress(PortraitAction.CodexEntry, 0);
        });
        _steps.Enqueue(() =>
        {
            var detail = _root.ModalLayer.Current;
            Require(_root.ModalLayer.IsActive && detail?.Title.Length > 0
                && detail.MonsterCodex?.Attributes.Count >= 9
                && detail.Body.Length == 0,
                $"monster codex entry opens structured details (attributes={detail?.MonsterCodex?.Attributes.Count}, body={detail?.Body.Length})");
            Shoot("codex_monster_detail", _root.ModalLayer);
            _root.ModalLayer.Dismiss();
            _root.HubScreen.DebugPress(PortraitAction.Back, 0);
        });
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Any(widget => widget.Action == PortraitAction.CodexOpen), "codex back returns to system settings");
            _root.HubScreen.DebugPress(PortraitAction.Back, 0);
        });
        // 仓储三段
        _steps.Enqueue(() => _root.HubScreen.ShowTab(3));
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.StoreSegment, 1));
        _steps.Enqueue(() =>
        {
            // 领地里没有远程交易：行情照看，步进与成交都压暗（进城镇商店才能买卖，后面大地图那段核对）。
            var widgets = _root.HubScreen.DebugWidgets;
            Require(!widgets.Any(w => w.Action is PortraitAction.TradePlus or PortraitAction.TradeMinus && w.Enabled)
                && widgets.Any(w => w.Action == PortraitAction.TradeRun && !w.Enabled), "trade steppers are dead at home");
            Shoot("store_trade", _root.HubScreen);
        });
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.StoreSegment, 2));
        _steps.Enqueue(() => Shoot("store_craft", _root.HubScreen));
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.StoreSegment, 0));
        _steps.Enqueue(() => _root.HubScreen.QueueRedraw());
        _steps.Enqueue(() =>
        {
            // 库存一件一条：点条弹结构化物品详情（字段条＋说明），不再是整段字。
            Require(_root.HubScreen.DebugWidgets.Count(w => w.Action == PortraitAction.StockItem) > 0, "stock rows registered");
            Shoot("store_stock", _root.HubScreen);
            _root.HubScreen.DebugPress(PortraitAction.StockItem, 0);
        });
        _steps.Enqueue(() =>
        {
            Require(_root.ModalLayer.Current?.Item is { Lines.Count: > 0 }, "stock row opens structured item detail");
            Shoot("stock_detail", _root.ModalLayer);
            _root.ModalLayer.Dismiss();
        });
        // 角色详情三段＋技能星盘
        _steps.Enqueue(() => _root.HubScreen.ShowTab(1));
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.RosterPick, _root.HubScreen.DebugHub.State.Roster.Master!.Id));
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.CharacterSegment), "character page segments");
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.OpenPortraitPicker), "portrait picker clickable");
            Shoot("char_status", _root.HubScreen);
            _root.HubScreen.DebugPress(PortraitAction.OpenPortraitPicker, _root.HubScreen.DebugHub.State.Roster.Master!.Id);
        });
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.PickPortraitDiff), "portrait diffs displayed in sheet");
            Shoot("char_portrait_picker", _root.HubScreen);
            _root.HubScreen.DebugPress(PortraitAction.PickPortraitDiff, 4);
        });
        _steps.Enqueue(() =>
        {
            var master = _root.HubScreen.DebugHub.State.Roster.Master!;
            Require(master.PortraitDiff == 4, $"portrait diff switched to 4 (actual={master.PortraitDiff})");
            Shoot("char_status_diff4", _root.HubScreen);
        });
        // 入库的身份立绘真能画出来：仓库里只有前 10 职业的 diff1（厨师在内），临时把主角身份换成厨师、差分回 1 拍一张，再换回。
        var identityBefore = "";
        _steps.Enqueue(() =>
        {
            var master = _root.HubScreen.DebugHub.State.Roster.Master!;
            identityBefore = master.Identity;
            master.Identity = "cook";
            master.PortraitDiff = 1;
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() =>
        {
            var master = _root.HubScreen.DebugHub.State.Roster.Master!;
            Require(PortraitAvatars.Resolve(master) != null
                && InkIllustration.LoadTexture(PortraitAvatars.PortraitPath("cook", 1)) != null, "committed identity portrait and avatar load");
            Shoot("char_status_art", _root.HubScreen);
            master.Identity = identityBefore;
            _root.HubScreen.QueueRedraw();
        });
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
            var helmet = Rimisekai.Defs.EquipForge.ForgeArmor(Rimisekai.Defs.EquipSlot.Head, material.DefName,
                quality: Rimisekai.Defs.Quality.Legendary);
            hub.State.Equips.Add(helmet);
            hub.State.Roster.Master!.Bag.Add(helmet.Id, 1);
            // 再塞两顶不同稀有度的，核对候选条的稀有度雾（精致蓝弱风、史诗紫中风）。
            foreach (var (mat, q) in new[] { ("皮", Rimisekai.Defs.Quality.Fine), ("铁", Rimisekai.Defs.Quality.Epic) })
            {
                var extra = Rimisekai.Defs.EquipForge.ForgeArmor(Rimisekai.Defs.EquipSlot.Head, mat, quality: q, enchant: "", blessed: false);
                hub.State.Equips.Add(extra);
                hub.State.Roster.Master!.Bag.Add(extra.Id, 1);
            }
            helmetId = helmet.Id;
            _root.HubScreen.DebugPress(PortraitAction.EquipInfo, 2);
        });
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.EquipSlotPick)
                && _root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.EquipOption && w.Label == helmetId),
                "equipment slot opens the equipment page with bag candidates");
            Shoot("equip_page", _root.HubScreen);
            _root.HubScreen.DebugHold(PortraitAction.EquipOption, HelmetIndex(helmetId));
        });
        _steps.Enqueue(() =>
        {
            Require(_root.ModalLayer.IsActive && _root.ModalLayer.Current?.Item?.Lines.Count > 0, "holding a candidate shows its details");
            Shoot("equip_hold", _root.HubScreen);
            _root.ModalLayer.Dismiss();
            _root.HubScreen.DebugPress(PortraitAction.EquipOption, HelmetIndex(helmetId));
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
        _steps.Enqueue(() =>
        {
            // 熟练分组默认只露最高一项，点开整组。
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.SkillGroup), "skill groups collapse to their top entry");
            _root.HubScreen.DebugPress(PortraitAction.SkillGroup, 0);
        });
        _steps.Enqueue(() => Shoot("char_skills_open", _root.HubScreen));
        _steps.Enqueue(() => { _root.HubScreen.DebugPress(PortraitAction.SkillGroup, 0); _root.HubScreen.DebugPan("character", 0); });
        _steps.Enqueue(() => _root.HubScreen.DebugPan("character", 1500));
        _steps.Enqueue(() => { CheckSkills(); Shoot("skills_chart", _root.HubScreen); });
        _steps.Enqueue(() => _root.HubScreen.DebugPan("character", 2700));
        _steps.Enqueue(() => Shoot("skills_detail", _root.HubScreen));
        _steps.Enqueue(() => { _root.HubScreen.DebugPan("character", 0); _root.HubScreen.DebugPress(PortraitAction.Back, 0); });
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
            // 移动：点一间连通的别房，状态当场改好，王棋沿门一格格走过去；不盖遮罩。
            var hub = _root.HubScreen.DebugHub;
            _veilFrom = hub.PlayerRoomId;
            var target = hub.Map().First(r => r.Open && r.Id != _veilFrom && hub.CanReach(r.Id)).Id;
            _root.HubScreen.DebugPress(PortraitAction.Cell, target);
            Require(hub.PlayerRoomId == target && _root.HubScreen.DebugWalking && !(_root.HubScreen.DebugVeil?.Running ?? false),
                "moving walks the king piece without the veil");
            _root.HubScreen._Process(0.14);
        });
        _steps.Enqueue(() => Shoot("walk_mid", _root.HubScreen));
        _steps.Enqueue(() =>
        {
            _root.HubScreen._Process(2);
            Require(!_root.HubScreen.DebugWalking, "walk settles in the target cell");
            _root.HubScreen.DebugHub.Arrive(_veilFrom);
            PortraitMotion.Instant = true;
            _root.HubScreen.SetProcess(true);
            _root.HubScreen.QueueRedraw();
        });
        // 世界层：整张生成器地图，视口以领地（队伍）为中心；缩小看全图；点最近的聚落弹地点抽屉。
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            _root.HubScreen.DebugPress(PortraitAction.HubWorld, 0);
            var world = hub.State.World;
            Require(hub.Layer == Rimisekai.Hub.MapLayer.World && world.HasHome, "world layer opens with a homesite");
            Require(world.Width >= 128 && world.Height >= 128, "world map is the full generated map, not 5x5");
            var c = _root.HubScreen.DebugWorldCenter;
            Require(Mathf.Abs(c.X - (world.HomeX + 0.5f)) < 20f && Mathf.Abs(c.Y - (world.HomeY + 0.5f)) < 20f, "world view centers on the territory");
        });
        _steps.Enqueue(() => Shoot("world", _root.HubScreen));
        _steps.Enqueue(() =>
        {
            for (var i = 0; i < 6; i++)
                _root.HubScreen.DebugPress(PortraitAction.WorldZoomOut, 0);
        });
        _steps.Enqueue(() => Shoot("world_wide", _root.HubScreen));
        _steps.Enqueue(() =>
        {
            _root.HubScreen.DebugPress(PortraitAction.WorldHome, 0);
            var world = _root.HubScreen.DebugHub.State.World;
            // 最近一座开店的聚落（村、镇、王都）：进去还要走进商店拍交易页。
            var poi = world.Pois.Where(p => p.Type is Rimisekai.WorldMap.WorldPoiType.Village or Rimisekai.WorldMap.WorldPoiType.Town or Rimisekai.WorldMap.WorldPoiType.Capital)
                .OrderBy(p => System.Math.Abs(p.X - world.HomeX) + System.Math.Abs(p.Y - world.HomeY)).First();
            _root.HubScreen.DebugWorldTap(poi.X, poi.Y);
        });
        // 点格逻辑已移除（2026-10-10）：轻点兴趣点格不弹抽屉。
        _steps.Enqueue(() => Require(!_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.WorldGo),
            "tapping a world tile no longer opens a sheet"));
        _steps.Enqueue(() =>
        {
            _root.HubScreen.DebugPress(PortraitAction.HubWorld, 0);
            Require(_root.HubScreen.DebugHub.Layer == Rimisekai.Hub.MapLayer.Territory, "back to territory from world");
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
        _steps.Enqueue(FillCrowdedLog);
        _steps.Enqueue(() => _root.HubScreen.QueueRedraw());
        _steps.Enqueue(() => Shoot("territory_crowded", _root.HubScreen));
        _steps.Enqueue(PressNowPager);
        _steps.Enqueue(() => _root.HubScreen.QueueRedraw());
        _steps.Enqueue(() => Shoot("territory_crowded_page2", _root.HubScreen));
        _steps.Enqueue(CheckNowPageTurned);
        _steps.Enqueue(CheckNowPageWrapped);
        // 自家的店：开一间杂货铺、摊位摆货，访客从外沿走进店里；「此刻」列出访客，点他弹交互抽屉（邀请＝请他入伙）。
        _steps.Enqueue(PrepareShopVisitor);
        _steps.Enqueue(() => _root.HubScreen.QueueRedraw());
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.NowAvatar && w.Index == _shopVisitor),
                "visitor in the shop is listed in the now band");
            Shoot("shop_visitor", _root.HubScreen);
        });
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.NowAvatar, _shopVisitor));
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugHub.SelectedCharacterId == _shopVisitor, "tapping the visitor selects them");
            Shoot("shop_visitor_talk", _root.HubScreen);
        });
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.SheetClose, 0));
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.Cell, _root.HubScreen.DebugHub.PlayerRoomId));
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.RoomGo), "shop room sheet opens");
            Shoot("shop_room_sheet", _root.HubScreen);
        });
        // 仓储设置：摊位的存取抽屉 → 齿轮 → 存储设置（优先级五段、全部允许/清除、过滤树），展开品类、切一件，返回退回存取抽屉。
        _steps.Enqueue(() =>
        {
            _root.HubScreen.DebugPress(PortraitAction.SheetClose, 0);
            Require(_root.HubScreen.DebugHub.OpenStorage(_shopStall), "open the stall storage");
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.StorageSettings), "storage sheet offers settings");
            Shoot("stall_storage", _root.HubScreen);
            _root.HubScreen.DebugPress(PortraitAction.StorageSettings, 0);
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Count(w => w.Action == PortraitAction.StoragePriority) == 5, "five priority levels");
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.StorageToggle), "filter tree rows");
            Shoot("storage_settings", _root.HubScreen);
            PressLabelled(PortraitAction.StorageFold, "加工品");
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() =>
        {
            PressLabelled(PortraitAction.StorageFold, "杂货");
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            Require(hub.StorageEntryState("Goods") == Rimisekai.Housing.FilterState.Some, "only pottery among goods");
            PressLabelled(PortraitAction.StorageToggle, "Goods");
            _root.HubScreen.DebugPress(PortraitAction.StoragePriority, 3);
            Require(hub.OpenStorageFacility!.Priority == Rimisekai.Housing.StoragePriority.Important, "priority picked");
            Require(hub.StorageEntryState("书本") == Rimisekai.Housing.FilterState.All, "the whole goods class is allowed");
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() => Shoot("storage_settings_open", _root.HubScreen));
        _steps.Enqueue(() => DragHubUp(new Rect2(0, PortraitLayout.StorageFilterRow(0).Position.Y, PortraitLayout.CanvasWidth,
            PortraitLayout.StorageFilterRows * PortraitLayout.SheetRowStep)));
        _steps.Enqueue(() => Shoot("storage_settings_scrolled", _root.HubScreen));
        _steps.Enqueue(() =>
        {
            PressLabelled(PortraitAction.SheetClose, "返回");
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.StorageSettings), "back returns to the storage sheet");
            _root.HubScreen.DebugPress(PortraitAction.SheetClose, 0);
        });
        _steps.Enqueue(LeaveShop);
        _steps.Enqueue(() =>
        {
            // 开局前排留空：StartBattle 应收拢阵型——空列后排一路顶到第一排，同列被挡者停在阻挡者身后。
            var probe = new Rimisekai.Combat.Battle();
            probe.Add(Rimisekai.Combat.Deploy.FromEnemy(new Rimisekai.Catalog.EnemyDef
            {
                Id = "front_a", Name = "前甲", ThreatTier = 4, Column = 1,
            }, 900, Rimisekai.Combat.CombatSide.Defender));
            probe.Add(Rimisekai.Combat.Deploy.FromEnemy(new Rimisekai.Catalog.EnemyDef
            {
                Id = "rear_a", Name = "后甲", ThreatTier = 1, Column = 1,
            }, 901, Rimisekai.Combat.CombatSide.Defender));
            probe.Add(Rimisekai.Combat.Deploy.FromEnemy(new Rimisekai.Catalog.EnemyDef
            {
                Id = "rear_b", Name = "后乙", ThreatTier = 2, Column = 2,
            }, 902, Rimisekai.Combat.CombatSide.Defender));
            probe.StartBattle();
            var tiers = probe.Members.Select(m => m.ThreatTier).OrderByDescending(t => t).ToList();
            Require(tiers.SequenceEqual(new[] { 4, 4, 3 }), "empty columns compact at battle start, blocked rear hugs blocker");
        });
        EnqueueProgressChecks();
        // 大地图行进（放在据点各页核对之后：行进会推进时间，免得扰动前面按开局时刻写的核对）：
        // 出行 → 点一格看路程 → 前往（逐格耗时） → 走到最近的聚落进场 → 出来站在聚落格上 → 缩小看走过的路 → 返回领地（走回去）。
        // 野外遭遇按此刻掷骰：出图关掉，免得行进被随机打断；遭遇另行摆出来核对。
        _steps.Enqueue(() => { _root.ModalLayer.Dismiss(); _root.HubScreen.ShowTab(0); _root.HubScreen.DebugHub.EncounterRate = 0; });
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.HubWorld, 0));
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            var world = hub.State.World;
            Require(hub.Layer == Rimisekai.Hub.MapLayer.World && hub.WorldPartyPosition == (world.HomeX, world.HomeY) && hub.PlayerRoomId == -1,
                "travel puts the party on the home tile, out of every territory room");
            // 最近一座开店的聚落（村、镇、王都）：进去还要走进商店拍交易页。
            var poi = world.Pois.Where(p => p.Type is Rimisekai.WorldMap.WorldPoiType.Village or Rimisekai.WorldMap.WorldPoiType.Town or Rimisekai.WorldMap.WorldPoiType.Capital)
                .OrderBy(p => System.Math.Abs(p.X - world.HomeX) + System.Math.Abs(p.Y - world.HomeY)).First();
            // 走到兴趣点旁边一格（逐格耗时），再用方向键踩上去——踩上即自动弹出「进入」。
            var near = new[] { (0, -1), (1, 0), (0, 1), (-1, 0) }
                .Select(d => (X: poi.X - d.Item1, Y: poi.Y - d.Item2, Dir: System.Array.IndexOf(new[] { (0, -1), (1, 0), (0, 1), (-1, 0) }, d)))
                .Where(t => world.IsPassable(t.X, t.Y) && world.PoiAt(t.X, t.Y) == null && hub.WorldTravelMinutes(t.X, t.Y) > 0)
                .OrderBy(t => hub.WorldTravelMinutes(t.X, t.Y)).First();
            _travelTarget = (near.X, near.Y);
            _poiStepDir = near.Dir;
            Require(hub.TravelTo(near.X, near.Y), "walk up next to the nearest settlement");
        });
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            Require(hub.Layer == Rimisekai.Hub.MapLayer.World && hub.WorldPartyPosition == _travelTarget && !hub.MapCovered,
                "going walks the party there with nothing popping over the map");
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() => Shoot("world_walked", _root.HubScreen));
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.WorldStep, _poiStepDir));
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.WorldGo && w.Enabled && w.Label == "进入"),
                "stepping onto a settlement pops its enter option");
            Shoot("world_poi_prompt", _root.HubScreen);
            _root.HubScreen.DebugPress(PortraitAction.WorldGo, 0);
        });
        _steps.Enqueue(() => _root.HubScreen.QueueRedraw());
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugHub.Layer == Rimisekai.Hub.MapLayer.WorldPoi, "inside the settlement");
            Shoot("world_poi_inside", _root.HubScreen);
            // 走进城里的商店：买卖只在这里做。
            var hub = _root.HubScreen.DebugHub;
            var shop = hub.State.Territory.Rooms.First(r => r.RegionId >= Rimisekai.Housing.Territory.MaxTerritoryRegions
                && r.HasTag(Rimisekai.Housing.Territory.CityShopTag));
            Require((hub.PlayerRoomId == shop.Id || hub.Arrive(shop.Id)) && hub.AtCityShop, "walk into the town shop");
            _root.HubScreen.ShowTab(3);
        });
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.StoreSegment, 1));
        _steps.Enqueue(() =>
        {
            var widgets = _root.HubScreen.DebugWidgets;
            Require(widgets.Any(w => w.Action == PortraitAction.TradePlus && w.Enabled), "in the town shop the trade steppers are live");
            Shoot("store_trade_city_shop", _root.HubScreen);
            _root.HubScreen.DebugPress(PortraitAction.TradePlus, widgets.First(w => w.Action == PortraitAction.TradePlus && w.Enabled).Index);
        });
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.TradeRun && w.Enabled), "a stepped line can be dealt in the shop");
            Shoot("store_trade_city_shop_picked", _root.HubScreen);
            var bought = _root.HubScreen.DebugHub.State.Money;
            _root.HubScreen.DebugPress(PortraitAction.TradeRun, 0);
            Require(_root.HubScreen.DebugHub.State.Money < bought, "dealing in the shop pays for the goods");
            _root.HubScreen.ShowTab(0);
        });
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.HubWorld, 0));
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugHub.Layer == Rimisekai.Hub.MapLayer.World, "leaving the settlement stands on its tile");
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() => Shoot("world_poi_out", _root.HubScreen));
        _steps.Enqueue(() =>
        {
            for (var i = 0; i < 3; i++)
                _root.HubScreen.DebugPress(PortraitAction.WorldZoomOut, 0);
        });
        _steps.Enqueue(() => Shoot("world_explored", _root.HubScreen));
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.HubWorld, 0));
        _steps.Enqueue(() =>
        {
            // 返回前会先弹一问写明路程（2026-10-09 起），点「确定」才动身。
            Require(_root.ModalLayer.IsActive && _root.ModalLayer.Current?.Body.Contains("走回") == true, "return asks first with the walk time");
            _root.ModalLayer.Choose("confirm");
        });
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            Require(hub.Layer == Rimisekai.Hub.MapLayer.Territory && hub.State.Party.AtHome && hub.PlayerRoomId >= 0,
                "return walks back home into the room left from");
            Shoot("world_back", _root.HubScreen);
        });
        EnqueueEncounterChecks();
        // 战斗转场（2026-10-10）：定格在领地画面上开演，逐段停帧出图；开门段在战斗画面就位后出。
        _steps.Enqueue(() => _root.BattleWipe.DebugBegin("交战", "斥候 ×2、巫师、石像鬼 等 5 名", 0.17f));
        _steps.Enqueue(() => Shoot("battle_wipe_slash", _root.HubScreen));
        _steps.Enqueue(() => _root.BattleWipe.DebugSeek(0.58f));
        _steps.Enqueue(() => Shoot("battle_wipe_shatter", _root.HubScreen));
        _steps.Enqueue(() => _root.BattleWipe.DebugSeek(0.66f));
        _steps.Enqueue(() => Shoot("battle_wipe_clash", _root.HubScreen));
        _steps.Enqueue(() => _root.BattleWipe.DebugSeek(1.45f));
        _steps.Enqueue(() => Shoot("battle_wipe_title", _root.HubScreen));
        foreach (var size in new[] { 1, 2, 3, 4 })
        {
            var capturedSize = size;
            _steps.Enqueue(() => BeginBattleProbe(capturedSize));
            if (size == 1)
            {
                _steps.Enqueue(() => _root.BattleWipe.DebugSeek(1.78f));
                _steps.Enqueue(() =>
                {
                    Require(_root.BattleWipe.Visible, "battle wipe stays over the combat screen while its doors open");
                    Shoot("battle_wipe_doors", _root.CombatView);
                    _root.BattleWipe.DebugEnd();
                });
            }
            _steps.Enqueue(() =>
            {
                Require(_battleProbe.Battle.Outcome == CombatOutcome.Ongoing && TotalHp(_battleProbe) == _probeHp
                    && _battleProbe.Battle.Time == _probeTime, "combat rendering does not advance battle");
                CheckAvatars();
                Shoot(capturedSize == 1 ? "combat" : $"boss{capturedSize}", _root.CombatView);
            });
            if (size == 1)
                EnqueueFxChecks();
        }
        _steps.Enqueue(CheckCombatButtons);
        _steps.Enqueue(() => Shoot("combat_item_popup", _root.CombatView));
        _steps.Enqueue(CheckSkillPopupFromButton);
        _steps.Enqueue(() => Shoot("combat_skill_popup", _root.CombatView));
        _steps.Enqueue(DispatchAttackViaButton);
        // 逃跑按双方平均躲闪掷骰（20%~95%），首领四人阵＋随机特质下可能只有两成：多给几次，免得出图偶发判败。
        for (var attempt = 0; attempt < 30; attempt++)
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
        // 战斗里存过档：回到标题后「继续」可点，进读档页列出存档。
        _steps.Enqueue(() => _root.TitleView.QueueRedraw());
        _steps.Enqueue(() =>
        {
            Require(_root.TitleView.DebugWidgets.Any(w => w.Index == 1 && w.Enabled), "continue is enabled once a save exists");
            PressTitle(PortraitLayout.TitleButton(1));
        });
        _steps.Enqueue(() => CheckTitlePage("load", InkSystemScreen.PageLoad));
        _steps.Enqueue(() => PressTitle(PortraitLayout.PageBack));
        EnqueueCrossChecks();
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
            && page.Choices.Select(c => c.Label).SequenceEqual(new[] { "保存进度", "读取进度", "放弃战斗并回到标题", "返回" }),
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

    /// <summary>战斗设置链路（下）：读取进度页含返回、返回回设置、关闭、放弃战斗并回到标题。</summary>
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
        Require(hub.Log.Count > 0 && hub.Log[0].Kind == LogKind.Scene && hub.Log[0].Text == $"你来到了{room.Name}。",
            "cell tap writes a scene log entry");
        var log = hub.Log;
        var away = party.Where(c => !c.IsPlayer && c.RoomId != hub.PlayerRoomId).Select(c => c.Name).ToList();
        Require(log.Zip(log.Skip(1)).All(p => LogBook.Rank(p.First.Kind) <= LogBook.Rank(p.Second.Kind))
            && !log.Any(e => e.Kind == LogKind.Activity && away.Any(n => e.Text.StartsWith(n))),
            "log snapshot runs action → environment → characters and only carries what the player's room can perceive");
        Require(screen.DebugWidgets.Any(w => w.Action == PortraitAction.Tab && w.Index == 4 && w.Rect == PortraitLayout.LogPanel),
            "log panel taps through to log tab");
        // 最小字号 36 下至少装得下 6 行（玩家动作 1～2＋环境 1＋「此刻」当前页 3 人）：行高 46、条距 6。
        var minLine = Mathf.Round(PortraitLayout.LogFontMin * 1.28f);
        var minGap = Mathf.Round(PortraitLayout.LogFontMin * 0.16f);
        Require(6 * minLine + 5 * minGap <= PortraitLayout.LogPanelText.Size.Y + 0.5f && PortraitLayout.LogPanel.End.Y < PortraitLayout.MapFrame.Position.Y
            && PortraitLayout.NowStrip.End.Y <= PortraitLayout.TravelButton.Position.Y && PortraitLayout.MapCell >= PortraitLayout.TouchMin,
            "log panel holds six lines at the minimum font and grid plus strip sit below it");
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
        // 设施牌四角棋子：让同房的人都坐到第一件设施上（多于 4 人，右下角应换成「+」）。
        var fixture = hub.FixturesIn(hub.PlayerRoomId)[0];
        foreach (var worker in hub.Day.Workers.Where(w => hub.State.Roster.Find(w.CharacterId)!.Id != hub.State.Roster.Master!.Id).Take(5))
        {
            worker.FacilityId = fixture.Id;
            worker.Phase = Rimisekai.Housing.WorkPhase.Working;
            worker.Path.Clear();
        }
        Require(hub.WorkersAtFixture(fixture.Id).Count >= 4, "fixture corner pieces fixture");
    }

    /// <summary>满屋子人：每人写一行在做什么，看日志角色档是否只跟着「此刻」当前页的 3 人走。</summary>
    private void FillCrowdedLog()
    {
        var hub = _root.HubScreen.DebugHub;
        hub.BeginOperation();
        hub.WriteScene("你来到了" + (hub.State.Territory.Room(hub.PlayerRoomId)?.Name ?? "这里") + "。");
        hub.WriteEnvironment("天气转为雨天。");
        foreach (var c in hub.Party().Where(c => !c.IsPlayer && c.RoomId == hub.PlayerRoomId))
            hub.WriteActivity(c.Id, $"{c.Name}靠在窗边看雨。");
        var others = hub.Party().Count(c => !c.IsPlayer && c.RoomId == hub.PlayerRoomId);
        Require(hub.Log.Count(e => e.Kind == LogKind.Activity) == others, "crowded log has a line per person");
        _root.HubScreen.QueueRedraw();
    }

    private int[] _nowFirstPage = Array.Empty<int>();

    /// <summary>「此刻」当前页上除主角外的人（主角固定在第 1 格，不随翻页换）。</summary>
    private int[] NowIds()
    {
        var player = _root.HubScreen.DebugHub.Party().First(c => c.IsPlayer).Id;
        return _root.HubScreen.DebugWidgets.Where(w => w.Action == PortraitAction.NowAvatar && w.Index != player).Select(w => w.Index).ToArray();
    }

    private int NowPages()
    {
        var hub = _root.HubScreen.DebugHub;
        var others = hub.Party().Count(c => !c.IsPlayer && c.RoomId == hub.PlayerRoomId);
        return (others + PortraitLayout.NowOthersPerPage - 1) / PortraitLayout.NowOthersPerPage;
    }

    /// <summary>翻页（上）：记下首页，按一次三角钮。</summary>
    private void PressNowPager()
    {
        // 有人开口时「此刻」钉在说话人那页，先把气泡说完再翻页。
        for (var i = 0; i < 20 && _root.HubScreen.DebugHub.PendingChatter != null; i++)
            _root.HubScreen.DebugHub.AdvanceChatter();
        _root.HubScreen.QueueRedraw();
        _nowFirstPage = NowIds();
        _root.HubScreen.DebugPress(PortraitAction.NowPage, 0);
    }

    /// <summary>翻页（中）：换成了下 4 人；再按到翻过末页。</summary>
    private void CheckNowPageTurned()
    {
        Require(NowIds().Length > 0 && !NowIds().Intersect(_nowFirstPage).Any(), "now pager turns to the next three");
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
            // 图标按中文名取，背包按物品 Id 记：面包的 Id 是 bread，不能把中文名当 Id 塞进背包。
            var id = Rimisekai.Defs.Items.All().First(def => def.Label == name).DefName;
            hub.State.Roster.Master!.Bag.Add(id, 5);
        }
    }

    private int _shopStall = -1;
    private int _shopVisitor = -1;
    private int _shopReturnRoom = -1;

    /// <summary>开拓庭院南边的空格建一间杂货铺（营业性），摊位摆上货；引一位访客，推到他走进店里，主人在店里等着。</summary>
    private void PrepareShopVisitor()
    {
        var hub = _root.HubScreen.DebugHub;
        var state = hub.State;
        _root.HubScreen.DebugPress(PortraitAction.SheetClose, 0);
        _shopReturnRoom = hub.PlayerRoomId;
        state.Money += 5000;
        state.Roster.Master!.Bag.Add("木材", 40);
        Require(hub.DevelopVacantCell(0, 2, 3), "develop shop cell");
        var cell = state.Territory.RoomAt(0, 2, 3)!;
        Require(hub.BuildRoomDef(146, cell.Id), "build grocery");
        var shop = state.Territory.RoomAt(0, 2, 3)!;
        Require(shop.Commercial, "grocery is commercial");
        var stall = state.Territory.Facilities.First(f => f.RoomId == shop.Id && Rimisekai.Housing.FacilityActions.Supports(f, Rimisekai.Housing.ActionKind.Trade));
        Require(stall.StorageFilter.Rules.Count == 0, "a new stall allows nothing");
        stall.StorageFilter.Only(new[] { "布", "陶罐" });
        stall.Contents.Add("布", 12);
        stall.Contents.Add("陶罐", 6);
        _shopStall = stall.Id;
        Require(hub.Arrive(shop.Id), "walk into the shop");
        var visit = Rimisekai.Housing.Commerce.Spawn(state.Territory, state.Roster, shop, new Random(7));
        Require(visit != null, "a visitor comes");
        _shopVisitor = visit!.CharacterId;
        for (var i = 0; i < 12 && visit.RoomId != shop.Id; i++)
            hub.PassTime(Rimisekai.Housing.TerritoryClock.StepMinutes);
        Require(visit.RoomId == shop.Id, "visitor reaches the shop");
    }

    /// <summary>按动作与标签点一个块（同一动作有多块、序号不定时用）。</summary>
    private void PressLabelled(PortraitAction action, string label)
    {
        var widget = _root.HubScreen.DebugWidgets.FirstOrDefault(w => w.Action == action && w.Label == label);
        Require(widget.Action == action && widget.Label == label, $"widget {action} {label}");
        _root.HubScreen.DebugPress(action, widget.Index);
    }

    private void LeaveShop()
    {
        var hub = _root.HubScreen.DebugHub;
        _root.HubScreen.DebugPress(PortraitAction.SheetClose, 0);
        Require(hub.Arrive(_shopReturnRoom), "walk back from the shop");
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
        // 星盘嵌在技能段里：每式一颗可点的星（含盘心的普通攻击），点星即选中。
        var stars = _root.HubScreen.DebugWidgets.Where(w => w.Action == PortraitAction.SkillNode).ToArray();
        Require(stars.Length >= 10, $"skill chart shows its stars inline, got {stars.Length}");
        Require(stars.All(w => w.Rect.Size.X >= PortraitLayout.TouchMin), "every star is touch-sized");
        foreach (var star in stars)
        {
            _root.HubScreen.DebugPress(PortraitAction.SkillNode, star.Index);
            Require(_root.HubScreen.DebugSelectedSkill == star.Label, "tapping a star selects its skill");
        }
        var armor = stars.First(w => w.Label == "armor_break");
        _root.HubScreen.DebugPress(PortraitAction.SkillNode, armor.Index);
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
    /// <summary>
    /// 世界探索遭遇：出行 → 脚下摆一群狼 → 弹窗（迎战 / 绕开）→ 绕开 →
    /// 走进最近的遗迹 → 朝最深处走，半路撞上的东西弹窗 → 处理掉 → 回领地。
    /// </summary>
    private void EnqueueEncounterChecks()
    {
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.HubWorld, 0));
        _steps.Enqueue(() =>
        {
            _root.HubScreen.DebugHub.ForceWildEncounter("wolves");
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() => _root.HubScreen.QueueRedraw());
        _steps.Enqueue(() =>
        {
            var page = _root.ModalLayer.Current;
            Require(_root.ModalLayer.IsActive && page?.Title == "狼群" && page.Choices.Count == 2
                && page.Choices[0].Label == "迎战" && page.Choices[1].Label == "绕开",
                "a wild encounter pops a modal offering fight or detour");
            Shoot("world_encounter", _root.HubScreen);
            _root.ModalLayer.Choose("avoid");
        });
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            Require(hub.PendingEncounter == null && !_root.ModalLayer.IsActive, "taking the detour settles the encounter");
            var world = hub.State.World;
            var ruin = world.Pois.Where(p => p.Type == Rimisekai.WorldMap.WorldPoiType.Ruin)
                .OrderBy(p => System.Math.Abs(p.X - world.HomeX) + System.Math.Abs(p.Y - world.HomeY)).First();
            Require(hub.TravelToPoiDirect(ruin.Id), "walk to the nearest ruin and go in");
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() => _root.HubScreen.QueueRedraw());
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            Require(hub.Layer == Rimisekai.Hub.MapLayer.WorldPoi, "inside the dungeon");
            // 迷雾：进门只看得见入口与有门相通的几间（写「？」），其余漆黑、去不了。
            var fog = Rimisekai.WorldMap.MapCatalog.Default.Dungeon.FogName;
            var block = hub.State.Territory.Rooms.Count(r => r.RegionId == hub.RegionId && r.Open);
            Require(hub.Map().Count < block && hub.Map().Any(r => r.Name == fog), "dungeon fog hides all but the entrance and its neighbours");
            Require(hub.State.Territory.Rooms.Where(r => r.RegionId == hub.RegionId && r.Open).Any(r => !hub.CanReach(r.Id)),
                "rooms in the dark cannot be walked to");
            Shoot("dungeon_inside", _root.HubScreen);
            // 一间一间往雾里走：第一间有东西的石室会把人截住。
            for (var i = 0; i < 40 && hub.PendingEncounter == null; i++)
            {
                var ahead = hub.Map().FirstOrDefault(r => r.Name == fog && hub.CanReach(r.Id));
                if (ahead == null)
                    break;
                Require(hub.Arrive(ahead.Id), "step into a glimpsed room");
            }
            _root.HubScreen.QueueRedraw();
        });
        _steps.Enqueue(() => _root.HubScreen.QueueRedraw());
        _steps.Enqueue(() => _root.HubScreen.QueueRedraw());
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            if (hub.PendingEncounter != null)
            {
                Require(_root.ModalLayer.IsActive && _root.ModalLayer.Current?.Title == hub.PendingEncounter.Title,
                    "a dungeon room with something in it stops the walk and pops its modal");
                Shoot("dungeon_encounter", _root.HubScreen);
                _root.ModalLayer.Choose(hub.PendingEncounter.IsBattle ? "avoid" : "accept");
            }
        });
        _steps.Enqueue(() =>
        {
            Require(_root.HubScreen.DebugHub.PendingEncounter == null, "the dungeon encounter is settled");
            _root.HubScreen.DebugPress(PortraitAction.HubWorld, 0);
        });
        _steps.Enqueue(() => _root.HubScreen.DebugPress(PortraitAction.HubWorld, 0));
        _steps.Enqueue(() =>
        {
            Require(_root.ModalLayer.IsActive, "return from the dungeon asks first");
            _root.ModalLayer.Choose("confirm");
        });
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            Require(hub.Layer == Rimisekai.Hub.MapLayer.Territory && hub.State.Party.AtHome, "back home from the dungeon");
            _root.HubScreen.ShowTab(2);
        });
        // 地城探索委托＝包接送的地城：接单、点名、出发即被送进地城（有迷雾），撤离即接回领地。
        _steps.Enqueue(() => ClickHub(PortraitAction.QuestTake,
            _root.HubScreen.DebugQuestIndex(d => d.Kind == Rimisekai.Quest.QuestKind.Dungeon)));
        _steps.Enqueue(() =>
        {
            var member = _root.HubScreen.DebugWidgets.First(w => w.Action == PortraitAction.PartyPick && w.Enabled);
            ClickHub(member.Action, member.Index);
        });
        _steps.Enqueue(() => ClickHub(PortraitAction.QuestStart, 0));
        _steps.Enqueue(() => _root.HubScreen.QueueRedraw());
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            Require(_root.DebugPhase == Rimisekai.Flow.FlowPhase.Hub && hub.Layer == Rimisekai.Hub.MapLayer.QuestPlace
                && hub.InQuestDungeon, "a dungeon commission rides the party into its dungeon");
            Require(_root.HubScreen.DebugTab == 0, "the hub shows the dungeon map after setting out");
            Require(_root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.HubWorld && w.Label == hub.TravelLabel)
                && _root.HubScreen.DebugWidgets.Any(w => w.Action == PortraitAction.Build && !w.Enabled),
                "in a commission dungeon the travel button reads leave and building is off");
            Shoot("quest_dungeon", _root.HubScreen);
            _root.HubScreen.DebugPress(PortraitAction.HubWorld, 0);
        });
        _steps.Enqueue(() =>
        {
            Require(_root.ModalLayer.IsActive, "leaving a commission dungeon asks first");
            _root.ModalLayer.Choose("confirm");
        });
        _steps.Enqueue(() =>
        {
            var hub = _root.HubScreen.DebugHub;
            Require(hub.Layer == Rimisekai.Hub.MapLayer.Territory && !hub.InQuestDungeon, "leaving rides the party home");
        });
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
                    Id = $"probe_{i}", Name = $"敌 {i}",
                    ThreatTier = i, Column = i,
                    Portrait = new[] { "monster_acid_slime", "monster_banshee", "monster_basilisk", "monster_bog_leech" }[i - 1],
                });
        else
        {
            foes.Add(new Rimisekai.Catalog.EnemyDef
            {
                Id = "probe_boss", Name = "首领", CorePool = 40, ExpPool = 6000,
                ThreatTier = 4, Column = size == 4 ? 1 : 2, Size = size,
                ActionPoints = size, Portrait = size switch { 2 => "monster_boss_magma_behemoth", 3 => "monster_boss_bone_colossus", _ => "monster_boss_flesh_titan" },
            });
            if (size == 2)
                foreach (var column in new[] { 1, 2, 3, 4 })
                    foes.Add(new Rimisekai.Catalog.EnemyDef
                    {
                        Id = $"probe_flank{column}", Name = $"敌 {column}",
                        ThreatTier = column is 2 or 3 ? 1 : 2, Column = column,
                        Portrait = column is 1 or 4 ? "monster_bone_hound" : "monster_blood_beast",
                    });
        }
        _battleProbe = Encounters.Start(hub.State, foes)!;
        // 夹具：敌人身板由生成器掷（速度 10 起），把敌方出手时点推远，截图前不让敌人先动手。
        foreach (var foe in _battleProbe.Battle.Members.Where(m => m.Side != _battleProbe.Battle.ControlledSide))
            foe.NextActAt += 1_000_000;
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
        // 背包里带两瓶药剂进场：道具页多一枚「饮药剂」，技能页不重复列。
        _root.HubScreen.DebugHub.State.Roster.Master!.Bag.Add("药剂", 2);
        _battleProbe.Battle.Supplies["药剂"] = 2;
        _root.CombatView.DebugPress(PortraitAction.CombatMenu, 2);
        Require(_root.ModalLayer.Current?.Title == "道具"
            && _root.ModalLayer.Current.Choices.Any(c => c.Label == "返回")
            && _root.ModalLayer.Current.Choices.Any(c => c.Id == "potion" && c.Label == "饮药剂 ×2"),
            "item button opens the item popup with the potion to drink");
    }

    /// <summary>道具页返回后点「技能」：技能页含防御架势与返回、不含普攻。</summary>
    private void CheckSkillPopupFromButton()
    {
        _root.ModalLayer.Choose("back");
        _root.CombatView.DebugPress(PortraitAction.CombatMenu, 1);
        Require(_root.ModalLayer.IsActive, "skill button opens the skill popup");
        Require(!_root.ModalLayer.Current!.Choices.Any(c => c.Id == Rimisekai.Combat.BattleSkills.AttackId)
            && _root.ModalLayer.Current.Choices.Any(c => c.Id == Rimisekai.Combat.BattleSkills.GuardId)
            && _root.ModalLayer.Current.Choices.Any(c => c.Label == "返回")
            && !_root.ModalLayer.Current.Choices.Any(c => c.Id == "potion"),
            "skill popup lists guard and back but neither the basic attack nor the potion");
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

    private int HelmetIndex(string itemId) =>
        _root.HubScreen.DebugWidgets.First(w => w.Action == PortraitAction.EquipOption && w.Label == itemId).Index;

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
    /// <summary>
    /// 战斗特效定格：每类招式（近战剑光 / 钝击 / 突刺 / 远射 / 法术 / 治疗 / 防御）以及敌打我方，
    /// 各生成一次光效、快进到中段截图，核对光效落点是否压在目标卡上。
    /// </summary>
    private void EnqueueFxChecks()
    {
        Combatant? deathProbeFoe = null;
        var deathProbeHp = 0;
        var cases = new (string Tag, string Skill, bool EnemyActs, CombatEventKind Kind, float At)[]
        {
            ("fx_attack", BattleSkills.AttackId, false, CombatEventKind.Hit, 0.18f),
            ("fx_cross", "cross_slash", false, CombatEventKind.Hit, 0.18f),
            ("fx_blunt", "palm_strike", false, CombatEventKind.Hit, 0.16f),
            ("fx_stab", "quick_stab", false, CombatEventKind.Hit, 0.14f),
            ("fx_sweep", "sweep", false, CombatEventKind.Hit, 0.18f),
            ("fx_cleave", "heavy_cleave", false, CombatEventKind.Hit, 0.18f),
            ("fx_arrow", "aimed_shot", false, CombatEventKind.Hit, 0.14f),
            ("fx_spell", "flame_burst", false, CombatEventKind.Hit, 0.16f),
            ("fx_enemy", BattleSkills.AttackId, true, CombatEventKind.Hit, 0.18f),
            ("fx_heal", "mend", false, CombatEventKind.Heal, 0.16f),
            ("fx_guard", BattleSkills.GuardId, false, CombatEventKind.Status, 0.16f),
        };
        foreach (var c in cases)
        {
            var fx = c;
            _steps.Enqueue(() =>
            {
                var battle = _battleProbe.Battle;
                var ally = battle.Members.First(m => m.Side == battle.ControlledSide);
                var foe = battle.Members.First(m => m.Side != battle.ControlledSide);
                var actor = fx.EnemyActs ? foe : ally;
                var target = fx.Kind is CombatEventKind.Heal or CombatEventKind.Status ? ally : fx.EnemyActs ? ally : foe;
                _root.CombatView.DebugFx(new BattleEvent
                {
                    Kind = fx.Kind, ActorId = actor.Id, TargetId = target.Id, SkillId = fx.Skill, Amount = 12, HpAfter = 99,
                }, fx.At);
            });
            _steps.Enqueue(() => Shoot(fx.Tag, _root.CombatView));
        }
        _steps.Enqueue(() =>
        {
            var battle = _battleProbe.Battle;
            var ally = battle.Members.First(m => m.Side == battle.ControlledSide && m.Alive);
            var foe = battle.Members.First(m => m.Side != battle.ControlledSide && m.Alive);
            var card = _root.CombatView.DebugUnitCard(foe);
            deathProbeFoe = foe;
            deathProbeHp = foe.Hp;
            foe.Hp = 0;
            Require(_root.CombatView.DebugUnitCenter(foe.Id).DistanceTo(card.GetCenter()) < 0.1f,
                "dead enemy FX stays anchored to its card after leaving the live geometry");
            _root.CombatView.DebugFx(new BattleEvent
            {
                Kind = CombatEventKind.Hit,
                ActorId = ally.Id,
                TargetId = foe.Id,
                SkillId = BattleSkills.AttackId,
                Amount = foe.MaxHp,
                HpAfter = 0,
            }, 0.18f);
        });
        _steps.Enqueue(() => Shoot("fx_death_slice", _root.CombatView));
        _steps.Enqueue(() =>
        {
            if (deathProbeFoe != null)
                deathProbeFoe.Hp = deathProbeHp;
            InkCombatFx.Clear();
        });
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
