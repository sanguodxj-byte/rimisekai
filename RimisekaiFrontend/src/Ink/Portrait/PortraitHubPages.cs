using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Defs;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Ink;
using Rimisekai.Quest;
using Rimisekai.Save;

namespace Rimisekai.Portrait;

/// <summary>
/// 竖屏据点的推入式页面：通用列表页（数据一律取 InkPageBuilder，不另起一套口径）、
/// 设施交互、交易、开发、任务编成、系统页（设置 / 存档 / 读档）。
/// </summary>
public partial class PortraitHubScreen
{
    private readonly ContentPack _content = new();
    private InkPage _page = InkPage.None;
    private string _systemPage = "";
    private bool _questMode;
    private int _questSel = -1;
    private int _tradeHeld = -1;
    private int _tradeMarket = -1;
    private int _tradeHeldFirst;
    private int _tradeMarketFirst;
    private readonly List<int> _party = new();
    private int _rowSel = -1;

    /// <summary>操作页的入口清单；最后一个下标是「设置」。</summary>
    private static readonly InkPage[] AllEntries =
    {
        InkPage.Stock, InkPage.Trade, InkPage.Develop, InkPage.Schedule, InkPage.Status,
        InkPage.Skills, InkPage.Quest,
    };

    public event Action<GameState, HubSession>? LoadRequested;

    /// <summary>核对工具用：取真实会话（与点击核对同一手法，不改任何状态）。</summary>
    public HubSession DebugHub => _vm!.Hub;

    /// <summary>需要玩家点头的操作（开拓 / 拆除）走弹窗确认，由根画面接走。</summary>
    public Action<InkModalPage>? ModalWanted;

    /// <summary>接下委托即遭遇：起好的战斗会话交根画面切入战斗页。</summary>
    public Action<Rimisekai.Session.BattleSession>? BattleWanted;

    private void Confirm(string title, string body, Action onYes) =>
        ModalWanted!(InkModalFactory.CreateConfirmation(title, body, onYes));

    /// <summary>战斗接管整块画面时由根画面调用；回来时清掉推入的页面。</summary>
    public void RefreshAfterCombat()
    {
        _page = InkPage.None;
        _questMode = false;
        _systemPage = "";
        QueueRedraw();
    }

    /// <summary>从页签带外的入口打开系统页。</summary>
    public void OpenSystemPage(string page)
    {
        _systemPage = page;
        _page = InkPage.None;
        _questMode = false;
        _listFirst = 0;
        QueueRedraw();
    }

    private InkPageQuery Query() =>
        new(_rowSel, _rowSel, -1, "", false, 0, 0, false, -1, -1, _listFirst);

    private bool OverlayActive =>
        _systemPage.Length > 0 || _questMode || _vm!.StorageOpen || _page != InkPage.None;

    /// <summary>操作页入口：最后一个下标是「设置」，任务走编成页，其余是列表页。</summary>
    private void OpenEntry(int index)
    {
        _listFirst = 0;
        _rowSel = -1;
        if (index == AllEntries.Length)
            _systemPage = InkSystemScreen.PageSettings;
        else if (AllEntries[index] == InkPage.Quest)
            _questMode = true;
        else
        {
            _page = AllEntries[index];
            if (_page == InkPage.Schedule)
                OpenSchedule();
            // 交易页进出即开集/散集：页内才能买卖（Core 的 AtMarket 闸门）。
            if (_page == InkPage.Trade)
                _vm!.Hub.OpenTrade();
        }
        QueueRedraw();
    }

    /// <summary>有覆盖页时它接管整个内容区。</summary>
    private bool DrawOverlay()
    {
        if (_systemPage.Length > 0)
            DrawSystem();
        else if (_questMode)
            DrawQuest();
        else if (_vm!.StorageOpen)
            DrawStorage();
        else if (_page == InkPage.Trade)
            DrawTrade();
        else if (_page == InkPage.Develop)
            DrawDevelopment();
        else if (_page == InkPage.Skills)
            DrawSkillPage();
        else if (_page == InkPage.Status)
            DrawStatusPage();
        else if (_page == InkPage.Schedule)
            DrawSchedule();
        else if (_page != InkPage.None)
            DrawModelPage();
        else
            return false;
        DrawTabBar();
        DrawNotice();
        return true;
    }

    private void DrawPageTop(string title) =>
        PortraitFrame.Title(this, new Rect2(0, PortraitLayout.Content.Position.Y,
            PortraitLayout.CanvasWidth, PortraitLayout.TitleBand), title);

    private static Rect2 RowAt(float y, int i, bool hasScroll = false) =>
        new(PortraitLayout.Pad, y + i * PortraitLayout.RowHeight,
            hasScroll ? PortraitLayout.ListWidth : PortraitLayout.FullWidth, PortraitLayout.RowHeight);

    private const float RowsTop = PortraitLayout.OverlayRowsTop;

    // ---------- 通用列表页 ----------

    /// <summary>角色三页走 InkCharacterPageBuilder，其余列表页走 InkPageBuilder——两边同一模型类型。</summary>
    private InkPageModel PageModel()
    {
        if (_page is InkPage.Status or InkPage.Skills or InkPage.Schedule)
            return InkCharacterPageBuilder.Build(_vm!, _page,
                _vm.ChatPartner() ?? _vm.Hub.State.Roster.Master, selected: _rowSel)!;
        var q = Query();
        return InkPageBuilder.Build(_vm!, _page, in q);
    }

    private void DrawModelPage()
    {
        var model = PageModel();
        DrawPageTop(model.Title);
        DrawBack();
        var rowCap = PortraitLayout.OverlayRows - model.DetailActions.Count;
        var hasScroll = model.Rows.Count > rowCap;
        _listFirst = Math.Clamp(_listFirst, 0, Math.Max(0, model.Rows.Count - rowCap));
        for (var i = 0; i < rowCap; i++)
        {
            var at = _listFirst + i;
            if (at >= model.Rows.Count)
                break;
            var row = model.Rows[at];
            var r = RowAt(RowsTop, i, hasScroll);
            PortraitFrame.Row(this, r, row.Name, row.Value, row.Selected || model.SelectedRow == at);
            _widgets.Add(new PortraitWidget(r, PortraitAction.PageRow, at, true, row.Name));
        }
        for (var i = 0; i < model.DetailActions.Count; i++)
        {
            var r = RowAt(RowsTop + (rowCap + i) * PortraitLayout.RowHeight, 0, false);
            PortraitFrame.Button(this, r, model.DetailActions[i].Name, enabled: model.DetailActions[i].Enabled);
            _widgets.Add(new PortraitWidget(r, PortraitAction.PageAction, i,
                model.DetailActions[i].Enabled, model.DetailActions[i].Name));
        }
        if (model.Rows.Count == 0 && model.EmptyHint.Length > 0)
            InkDraw.Text(this, new Vector2(PortraitLayout.Pad, RowsTop + PortraitLayout.RowHeight),
                model.EmptyHint, PortraitLayout.FontBody, InkStyle.Dim, "lt");
        DrawScroll(model.Rows.Count, rowCap);
    }

    // ---------- 设施交互页 ----------

    private void DrawStorage()
    {
        DrawPageTop(_vm!.StorageName());
        DrawBack();
        var rows = _vm.StorageRows();
        _listFirst = Math.Clamp(_listFirst, 0, Math.Max(0, rows.Count - PortraitLayout.StorageItemRows));
        for (var i = 0; i < PortraitLayout.StorageItemRows; i++)
        {
            var at = _listFirst + i;
            if (at >= rows.Count)
                break;
            var row = rows[at];
            var y = RowsTop + i * PortraitLayout.StorageItemHeight;
            var name = PortraitLayout.StorageItemName(y, rows.Count > PortraitLayout.StorageItemRows);
            PortraitFrame.Row(this, name, "", "", false);
            InkDraw.TextFitted(this, new Vector2(name.Position.X + 24f, name.Position.Y + 44f), ItemName(row.ItemId),
                name.Size.X - 48f, PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "lm");
            InkDraw.TextFitted(this, new Vector2(name.Position.X + 24f, name.Position.Y + 104f),
                $"设施 {row.InStorage} 背包 {row.InBag}", name.Size.X - 48f,
                PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "lm");
            for (var b = 0; b < 2; b++)
            {
                var br = PortraitLayout.StorageItemButton(y, b, rows.Count > PortraitLayout.StorageItemRows);
                PortraitFrame.Button(this, br, b == 0 ? "放入" : "取出");
                _widgets.Add(new PortraitWidget(br, b == 0 ? PortraitAction.StoreIn : PortraitAction.StoreOut,
                    at, true, row.ItemId));
            }
        }
        RegisterScroll("storage", PortraitLayout.StorageList, rows.Count, PortraitLayout.StorageItemRows,
            _listFirst, first => _listFirst = first, PortraitLayout.StorageItemHeight);
    }

    // ---------- 交易页 ----------

    private List<(string Id, string Name, string Value)> HeldRows()
    {
        var list = new List<(string Id, string Name, string Value)>();
        foreach (var pair in _vm!.Hub.Stock())
        {
            if (pair.Value <= 0)
                continue;
            list.Add((pair.Key, ItemName(pair.Key), $"×{pair.Value}"));
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return list;
    }

    private List<(string Id, string Name, string Value)> MarketRows()
    {
        var list = new List<(string Id, string Name, string Value)>();
        var hub = _vm!.Hub;
        foreach (var l in hub.State.Territory.Listings())
        {
            if (l.SoldOut)
                continue;
            list.Add((l.ItemId, l.Label, $"{hub.TradePrices(l, false)}G"));
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return list;
    }

    private void DrawTrade()
    {
        DrawPageTop("交易");
        var held = HeldRows();
        var market = MarketRows();
        var per = PortraitLayout.TradeVisibleRows;
        _tradeHeldFirst = Math.Clamp(_tradeHeldFirst, 0, Math.Max(0, held.Count - per));
        _tradeMarketFirst = Math.Clamp(_tradeMarketFirst, 0, Math.Max(0, market.Count - per));
        InkDraw.Text(this, new Vector2(PortraitLayout.Pad, RowsTop), "卖出 持有",
            PortraitLayout.FontMeta, InkStyle.Dim, "lt");
        for (var i = 0; i < per && i + _tradeHeldFirst < held.Count; i++)
        {
            var at = i + _tradeHeldFirst;
            var rect = PortraitLayout.ScrolledRow(PortraitLayout.TradeHeldArea, i, hasScroll: held.Count > per);
            PortraitFrame.Row(this, rect, held[at].Name, held[at].Value, _tradeHeld == at);
            _widgets.Add(new PortraitWidget(rect, PortraitAction.TradeHeld, at, true, held[at].Name));
        }
        RegisterScroll("trade_held", PortraitLayout.TradeHeldArea, held.Count, per,
            _tradeHeldFirst, first => _tradeHeldFirst = first);
        InkDraw.Text(this, new Vector2(PortraitLayout.Pad, PortraitLayout.TradeMarketArea.Position.Y - 50f),
            "买入 市场", PortraitLayout.FontMeta, InkStyle.Dim, "lt");
        for (var i = 0; i < per && i + _tradeMarketFirst < market.Count; i++)
        {
            var at = i + _tradeMarketFirst;
            var rect = PortraitLayout.ScrolledRow(PortraitLayout.TradeMarketArea, i, hasScroll: market.Count > per);
            PortraitFrame.Row(this, rect, market[at].Name, market[at].Value, _tradeMarket == at);
            _widgets.Add(new PortraitWidget(rect, PortraitAction.TradeMarket, at, true, market[at].Name));
        }
        RegisterScroll("trade_market", PortraitLayout.TradeMarketArea, market.Count, per,
            _tradeMarketFirst, first => _tradeMarketFirst = first);
        var canRun = (_tradeHeld >= 0 || _tradeMarket >= 0) && _vm.Hub.TradeAvailable;
        PortraitFrame.Button(this, PortraitLayout.TradeRunRow, "交易", enabled: canRun);
        _widgets.Add(new PortraitWidget(PortraitLayout.TradeRunRow, PortraitAction.TradeRun, 0, canRun, "交易"));
        DrawBack();
    }

    private void RunTrade()
    {
        var hub = _vm!.Hub;
        hub.BeginOperation();
        var held = HeldRows();
        var market = MarketRows();
        var sellId = _tradeHeld >= 0 && _tradeHeld < held.Count ? held[_tradeHeld].Id : null;
        var buyId = _tradeMarket >= 0 && _tradeMarket < market.Count ? market[_tradeMarket].Id : null;
        hub.MarketTradeCombined(sellId, 1, buyId, 1);
        _notice = hub.Log.Count > 0 ? hub.Log[^1].Text : "";
    }

    // ---------- 任务页 ----------

    private void DrawQuest()
    {
        DrawPageTop("任务");
        var defs = AvailableQuests();
        var questRows = PortraitLayout.OverlayRows - 3;
        var hasScroll = defs.Count > questRows;
        _listFirst = ClampFirst(_listFirst, defs.Count);
        for (var i = 0; i < questRows; i++)
        {
            var at = _listFirst + i;
            if (at >= defs.Count)
                break;
            var r = RowAt(RowsTop, i, hasScroll);
            PortraitFrame.Row(this, r, defs[at].Name, $"难度 {defs[at].DifficultyText}", _questSel == at);
            _widgets.Add(new PortraitWidget(r, PortraitAction.QuestPick, at, true, defs[at].Id.ToString()));
        }
        var cards = _vm!.Cards();
        var partyY = RowsTop + (PortraitLayout.OverlayRows - 3) * PortraitLayout.RowHeight;
        for (var i = 0; i < PortraitLayout.PartySlots && i < cards.Count; i++)
        {
            var on = _party.Contains(cards[i].Id);
            var r = PortraitLayout.PartySlot(i, partyY);
            PortraitFrame.Button(this, r, on ? cards[i].Name : "空位", selected: on);
            _widgets.Add(new PortraitWidget(r, PortraitAction.PartyPick, i, true, cards[i].Id.ToString()));
        }
        var start = PortraitLayout.QuestStartRow(partyY);
        var ready = _questSel >= 0 && _party.Count > 0;
        PortraitFrame.Button(this, start, "开始委托", enabled: ready);
        _widgets.Add(new PortraitWidget(start, PortraitAction.QuestStart, 0, ready, "开始委托"));
        DrawBack();
    }

    private List<QuestDef> AvailableQuests()
    {
        var list = new List<QuestDef>();
        foreach (var def in DefDatabase<QuestDef>.All)
            if (_vm!.Hub.State.Quests.IsAvailable(def.Id))
                list.Add(def);
        list.Sort((a, b) => a.Id.CompareTo(b.Id));
        return list;
    }

    private void StartQuest()
    {
        var defs = AvailableQuests();
        if (_questSel < 0 || _questSel >= defs.Count)
            return;
        var hub = _vm!.Hub;
        hub.BeginOperation();
        var deploy = new List<int>(_party);
        var master = hub.State.Roster.Master;
        if (master != null && !deploy.Contains(master.Id))
            deploy.Add(master.Id);
        var run = hub.State.Quests.Start(defs[_questSel], deploy);
        if (run == null)
        {
            _notice = "这单现在接不了。";
            return;
        }

        // 接下委托即遭遇：战斗本身就是委托入口（Encounters 起战，结算侧按 QuestRun 记通关与冷却）。
        var def = defs[_questSel];
        var session = Rimisekai.Hub.Encounters.Start(
            hub.State, def.Foes, placeName: def.Name, partyIds: deploy);
        if (session == null)
        {
            _notice = "这单现在接不了。";
            return;
        }
        session.QuestRun = run;
        BattleWanted?.Invoke(session);
    }

    // ---------- 系统页 ----------

    private void DrawSystem()
    {
        var load = _systemPage == InkSystemScreen.PageLoad;
        var save = _systemPage == InkSystemScreen.PageSave;
        DrawPageTop(load ? "读取进度" : save ? "保存进度" : "设置");
        if (_systemPage == InkSystemScreen.PageSettings)
        {
            for (var i = 0; i < 2; i++)
            {
                var rect = RowAt(RowsTop, i);
                PortraitFrame.Row(this, rect, i == 0 ? "主音量" : "垂直同步",
                    i == 0 ? $"{(int)(InkSettings.CurrentMasterVolume * 100)}" : InkSettings.CurrentVSync ? "开" : "关", false);
                _widgets.Add(new PortraitWidget(rect, PortraitAction.SystemToggle, i, true, ""));
            }
            var labels = new[] { "保存进度", "读取进度" };
            for (var i = 0; i < labels.Length; i++)
            {
                var rect = PortraitLayout.SystemPageButton(i);
                PortraitFrame.Button(this, rect, labels[i]);
                _widgets.Add(new PortraitWidget(rect, PortraitAction.SystemPage, i, true, labels[i]));
            }
        }
        else
        {
            var slots = InkSaveStore.ListSaves();
            var visible = PortraitLayout.OverlayRows - 1;
            var hasScroll = slots.Count > visible;
            _listFirst = Math.Clamp(_listFirst, 0, Math.Max(0, slots.Count - visible));
            for (var i = 0; i < visible && i + _listFirst < slots.Count; i++)
            {
                var slot = slots[i + _listFirst];
                var rect = RowAt(RowsTop, i, hasScroll);
                DrawSaveRow(rect, slot);
                if (load)
                    _widgets.Add(new PortraitWidget(rect, PortraitAction.SavePick, i + _listFirst, true, slot.FilePath));
            }
            DrawScroll(slots.Count, visible);
            if (save)
            {
                PortraitFrame.Button(this, PortraitLayout.SystemSaveRow, "保存当前进度");
                _widgets.Add(new PortraitWidget(PortraitLayout.SystemSaveRow, PortraitAction.SaveNow, 0, true, "保存"));
            }
        }
        DrawBack();
    }

    private void DrawSaveRow(Rect2 rect, SaveSlotInfo slot)
    {
        PortraitFrame.Row(this, rect, "", "", false);
        var name = new Rect2(rect.Position.X + 24f, rect.Position.Y + 14f, rect.Size.X - 48f, 44f);
        InkDraw.TextBounded(this, name, slot.TerritoryName, PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Line, "lm");
        InkDraw.Text(this, new Vector2(name.End.X, name.GetCenter().Y), $"{slot.Day} 日",
            PortraitLayout.FontMeta, InkStyle.Dim, "rm");
        var time = DateTime.ParseExact(slot.Timestamp, "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        var stamp = time.ToString("yyyy-MM-dd HH时mm分ss秒", System.Globalization.CultureInfo.InvariantCulture);
        var detail = new Rect2(name.Position.X, rect.Position.Y + 60f, name.Size.X, 44f);
        InkDraw.TextFitted(this, new Vector2(detail.Position.X, detail.GetCenter().Y), stamp,
            detail.Size.X, PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "lm");
    }

    private void DrawBack()
    {
        var back = PortraitLayout.OverlayBack;
        PortraitFrame.Button(this, back, "返回");
        _widgets.Add(new PortraitWidget(back, PortraitAction.Back, 0, true, "返回"));
    }

    // ---------- 派发 ----------

    private void ExecuteOverlay(PortraitWidget w)
    {
        var hub = _vm!.Hub;
        if (ExecuteStatus(w) || ExecuteSchedule(w) || ExecuteInteraction(w) || ExecuteDevelopment(w))
            return;
        switch (w.Action)
        {
            case PortraitAction.SkillSector:
            case PortraitAction.SkillNode:
            case PortraitAction.SkillReset:
            case PortraitAction.SkillPrevious:
            case PortraitAction.SkillNext:
                ExecuteSkillWidget(w);
                break;
            case PortraitAction.PageRow:
                _rowSel = w.Index;
                break;
            case PortraitAction.PageAction:
                RunDetailAction(w.Index);
                break;
            case PortraitAction.StoreIn:
                hub.BeginOperation();
                hub.StoreOne(w.Label);
                break;
            case PortraitAction.StoreOut:
                hub.BeginOperation();
                hub.TakeOne(w.Label);
                break;
            case PortraitAction.TradeHeld:
                _tradeHeld = _tradeHeld == w.Index ? -1 : w.Index;
                break;
            case PortraitAction.TradeMarket:
                _tradeMarket = _tradeMarket == w.Index ? -1 : w.Index;
                break;
            case PortraitAction.TradeRun:
                RunTrade();
                break;
            case PortraitAction.QuestPick:
                _questSel = w.Index;
                break;
            case PortraitAction.PartyPick:
                ToggleParty(w.Index);
                break;
            case PortraitAction.QuestStart:
                StartQuest();
                break;
            case PortraitAction.SystemPage:
                OpenSystemPage(w.Index == 0 ? InkSystemScreen.PageSave : InkSystemScreen.PageLoad);
                break;
            case PortraitAction.SystemToggle:
                ToggleSetting(w.Index);
                break;
            case PortraitAction.SaveNow:
                _notice = InkSaveStore.SaveNew(hub.State, hub, out _) != null ? "已保存。" : "保存失败。";
                break;
            case PortraitAction.SavePick:
                LoadSave(w.Label);
                break;
            case PortraitAction.Back:
                CloseOverlay();
                break;
        }
        _notice = hub.Log.Count > 0 ? hub.Log[^1].Text : _notice;
    }

    private void RunDetailAction(int index)
    {
        var actions = PageModel().DetailActions;
        if (index >= actions.Count)
            return;
        var row = actions[index];
        var hub = _vm!.Hub;
        hub.BeginOperation();
        switch (row.Action)
        {
            case InkPageAction.Craft:
                hub.Craft(row.TargetId);
                break;
            case InkPageAction.AssignTask:
            case InkPageAction.CancelTask:
                Assign(row);
                break;
        }
    }

    private void Assign(InkPageRow row)
    {
        var hub = _vm!.Hub;
        var who = _vm.ChatPartner() ?? hub.State.Roster.Master;
        if (who == null || !Enum.TryParse<SlotMode>(row.TargetId, out var mode))
            return;
        var existing = hub.AssignmentOf(who.Id, row.TargetNumber);
        hub.Assign(who.Id, row.TargetNumber, mode, mode == SlotMode.Free ? -1 : existing.FacilityId);
    }

    private void ToggleParty(int cardIndex)
    {
        var cards = _vm!.Cards();
        if (cardIndex >= cards.Count)
            return;
        var id = cards[cardIndex].Id;
        if (_party.Contains(id))
            _party.Remove(id);
        else if (_party.Count < PortraitLayout.PartySlots)
            _party.Add(id);
    }

    private void ToggleSetting(int i)
    {
        InkSettings.EnsureLoaded();
        if (i == 0)
            InkSettings.ApplyMasterVolume(InkSettings.CurrentMasterVolume >= 0.99f ? 0f : 1f);
        else
            InkSettings.ApplyVSync(!InkSettings.CurrentVSync);
    }

    private void LoadSave(string path)
    {
        if (_systemPage != InkSystemScreen.PageLoad)
            return;
        if (InkSaveStore.TryLoad(path, _content, out var state, out var hub, out _) && state != null && hub != null)
            LoadRequested?.Invoke(state, hub);
    }

    private void CloseOverlay()
    {
        ResetSkillView();
        if (_systemPage.Length > 0)
            _systemPage = "";
        else if (_questMode)
        {
            _questMode = false;
            _party.Clear();
            _questSel = -1;
        }
        else if (_vm!.StorageOpen)
            _vm.Hub.CloseStorage();
        else
        {
            // 离开交易页即散集（Core 的 AtMarket 闸门）。
            if (_page == InkPage.Trade)
                _vm!.Hub.LeaveMarket();
            _page = InkPage.None;
        }
        _rowSel = -1;
        _tradeHeld = _tradeMarket = -1;
        _listFirst = 0;
    }

    private string ItemName(string itemId)
    {
        var info = Items.Info(_vm!.Hub.State.Territory.Weapons, itemId);
        return info != null && info.Value.Label.Length > 0 ? info.Value.Label : itemId;
    }
}
