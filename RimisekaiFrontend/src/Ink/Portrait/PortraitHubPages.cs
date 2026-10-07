using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Defs;
using Rimisekai.Hub;
using Rimisekai.Ink;
using Rimisekai.Quest;
using Rimisekai.Save;

namespace Rimisekai.Portrait;

/// <summary>
/// 委托页签（委托板卡片＋编成抽屉）、日志页签（时间线）、系统推入页（存档卡片＋快速设置）。
/// </summary>
public partial class PortraitHubScreen
{
    private readonly ContentPack _content = new();
    private int _questSel = -1;
    private readonly List<int> _party = new();
    private int _systemSeg;

    public event Action<GameState, HubSession>? LoadRequested;

    /// <summary>核对工具用：取真实会话（不改任何状态）。</summary>
    public HubSession DebugHub => _vm.Hub;

    /// <summary>需要玩家点头的操作（拆除 / 读档 / 改名）走弹窗，由根画面接走。</summary>
    public Action<InkModalPage>? ModalWanted;

    /// <summary>接下委托即遭遇：起好的战斗会话交根画面切入战斗页。</summary>
    public Action<Rimisekai.Session.BattleSession>? BattleWanted;

    private void Confirm(string title, string body, Action onYes) =>
        ModalWanted!(InkModalFactory.CreateConfirmation(title, body, onYes));

    /// <summary>战斗接管整块画面时由根画面调用；回来时收起抽屉与推入页。</summary>
    public void RefreshAfterCombat()
    {
        _push = PushPage.None;
        _sheet = SheetKind.None;
        _party.Clear();
        _questSel = -1;
        QueueRedraw();
    }

    /// <summary>打开系统推入页：读档入口落在「存档」段，设置入口落在「设置」段。</summary>
    public void OpenSystemPage(string page)
    {
        LeaveTradeIfOpen();
        CloseTransient();
        _push = PushPage.System;
        _systemSeg = page == InkSystemScreen.PageSettings ? 1 : 0;
        _pan.Remove("system");
        QueueRedraw();
    }

    // ---------- 委托板 ----------

    private List<QuestDef> AvailableQuests()
    {
        var list = new List<QuestDef>();
        foreach (var def in DefDatabase<QuestDef>.All)
            if (_vm.Hub.State.Quests.IsAvailable(def.Id))
                list.Add(def);
        list.Sort((a, b) => a.Id.CompareTo(b.Id));
        return list;
    }

    private static string FoesOf(QuestDef def) => string.Join(" · ", def.Foes.GroupBy(f => f.Name)
        .Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key));

    private void DrawQuestBoard()
    {
        var view = PortraitLayout.QuestView;
        var defs = AvailableQuests();
        var step = PortraitLayout.QuestCardHeight + PortraitLayout.QuestCardGap;
        var total = (int)(defs.Count * step);
        var offset = Pan("quests", total, (int)view.Size.Y);
        for (var i = 0; i < defs.Count; i++)
        {
            var def = defs[i];
            var r = new Rect2(PortraitLayout.Pad, view.Position.Y + 16f + i * step - offset, PortraitLayout.FullWidth,
                PortraitLayout.QuestCardHeight);
            if (r.End.Y < view.Position.Y || r.Position.Y > view.End.Y)
                continue;
            PortraitFrame.NotchedFrame(this, r);
            var x = r.Position.X + 50f;
            var size = def.MaxPartySize > 0 ? $"{def.MaxPartySize} 人" : "";
            var pillW = size.Length > 0 ? InkDraw.Measure(size, PortraitLayout.FontMeta).X + 56f : 0f;
            InkDraw.TextBounded(this, new Rect2(x, r.Position.Y + 36f, r.Size.X - 140f - pillW, 80f), def.Name,
                PortraitLayout.FontTitle, PortraitLayout.FontBody, InkStyle.Line, "lm");
            if (size.Length > 0)
            {
                var pill = new Rect2(r.End.X - 50f - pillW, r.Position.Y + 44f, pillW, 64f);
                PortraitFrame.RoundRect(this, pill, 32f, null, InkStyle.Dim, 3f);
                InkDraw.Text(this, pill.GetCenter(), size, PortraitLayout.FontMeta, InkStyle.Dim, "cm");
            }
            InkDraw.Text(this, new Vector2(x, r.Position.Y + 150f), "难度", PortraitLayout.FontMeta, InkStyle.Dim, "lm");
            var stars = Math.Min(10, Math.Max(5, (int)Math.Ceiling(def.Difficulty)));
            PortraitFrame.Ticks(this, x + 110f, r.Position.Y + 150f, stars, (float)def.Difficulty, 30f, 14f);
            var foesX = x + 130f + stars * 44f;
            InkDraw.TextBounded(this, new Rect2(foesX, r.Position.Y + 120f, r.End.X - 50f - foesX, 60f), FoesOf(def),
                PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "lm");
            var body = def.Description.Length > 0 ? def.Description : def.Rumor.Length > 0 ? $"「{def.Rumor}」" : "";
            var lines = InkDraw.WrapLines(body, r.Size.X - 100f, PortraitLayout.FontMeta);
            for (var k = 0; k < lines.Count && k < 3; k++)
                InkDraw.Text(this, new Vector2(x, r.Position.Y + 220f + k * 66f),
                    k == 2 && lines.Count > 3 ? InkDraw.Ellipsize(lines[k] + "…", r.Size.X - 100f, PortraitLayout.FontMeta) : lines[k],
                    PortraitLayout.FontMeta, def.Description.Length > 0 ? InkStyle.Line : InkStyle.Dim, "lm");
            InkDraw.InkLine(this, new Vector2(r.Position.X + 30f, r.Position.Y + 420f), new Vector2(r.End.X - 30f, r.Position.Y + 420f),
                InkStyle.WoodDark, 2f);
            var take = new Rect2(r.End.X - 40f - 240f, r.Position.Y + 432f, 240f, PortraitLayout.TouchMin);
            PortraitGlyph.Coin(this, x + 18f, take.GetCenter().Y, 18f, InkStyle.Dim);
            InkDraw.TextBounded(this, new Rect2(x + 54f, take.Position.Y, take.Position.X - x - 74f, take.Size.Y),
                string.Join(" · ", def.Rewards), PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Line, "lm");
            PortraitFrame.Pill(this, take, "接取", primary: true);
            AddClipped(take, view, PortraitAction.QuestTake, i, true, def.Id.ToString());
        }
        if (defs.Count == 0)
            InkDraw.Text(this, new Vector2(PortraitLayout.CanvasWidth / 2f, view.Position.Y + 160f), "暂无委托。",
                PortraitLayout.FontBody, InkStyle.Dim, "cm");
        RegisterScroll("quests", view, total + 32, (int)view.Size.Y, offset, v => _pan["quests"] = v, 1f);
        MaskAbove(view);
    }

    private int PartyCap()
    {
        var defs = AvailableQuests();
        var def = _questSel >= 0 && _questSel < defs.Count ? defs[_questSel] : null;
        return def != null && def.MaxPartySize > 0 ? Math.Min(4, def.MaxPartySize) : 4;
    }

    /// <summary>编成抽屉：四个队位（领主固定第一位）＋可出战名单＋底部出发条。</summary>
    private float DrawPartySheet()
    {
        var defs = AvailableQuests();
        if (_questSel < 0 || _questSel >= defs.Count)
            return 820f;
        var def = defs[_questSel];
        var top = 820f;
        PortraitFrame.Sheet(this, top);
        var cap = PartyCap();
        var master = _vm.Hub.State.Roster.Master!;
        var members = new List<Rimisekai.Character.CharacterState> { master };
        members.AddRange(_party.Select(id => _vm.Hub.State.Roster.Find(id)!));
        InkDraw.TextBounded(this, new Rect2(PortraitLayout.Pad + 20f, top + PortraitLayout.SheetTitleOffset - 40f, 700f, 80f),
            $"编成 · {def.Name}", PortraitLayout.FontTitle, PortraitLayout.FontBody, InkStyle.Line, "lm");
        InkDraw.Text(this, new Vector2(PortraitLayout.CanvasWidth - PortraitLayout.Pad - 140f, top + PortraitLayout.SheetTitleOffset),
            $"{members.Count} / {cap}", PortraitLayout.FontBody, InkStyle.Dim, "rm");
        var close = PortraitLayout.SheetClose(top);
        PortraitGlyph.Close(this, close.GetCenter().X, close.GetCenter().Y, 26f, InkStyle.Dim);
        _widgets.Add(new PortraitWidget(close, PortraitAction.SheetClose, 0, true, "收起"));

        var slotY = top + PortraitLayout.SheetContentOffset;
        var w = (PortraitLayout.FullWidth - 60f) / 4f;
        for (var i = 0; i < 4; i++)
        {
            var r = new Rect2(PortraitLayout.Pad + i * (w + 20f), slotY, w, 320f);
            if (i < members.Count)
            {
                var who = members[i];
                PortraitFrame.Card(this, r, true, 18f);
                PortraitFrame.Avatar(this, new Vector2(r.GetCenter().X, r.Position.Y + 110f), 68f, PortraitAvatars.Resolve(who), who.Name, ring: false);
                InkDraw.TextBounded(this, new Rect2(r.Position.X + 10f, r.Position.Y + 200f, r.Size.X - 20f, 60f), who.Name,
                    PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "cm");
                InkDraw.Text(this, new Vector2(r.GetCenter().X, r.Position.Y + 276f), $"Lv {who.Level}", PortraitLayout.FontMeta, InkStyle.Dim, "cm");
                if (i > 0)
                    _widgets.Add(new PortraitWidget(r, PortraitAction.PartyPick, who.Id, true, who.Name));
            }
            else
            {
                PortraitFrame.RoundRect(this, r, 18f, null, i < cap ? InkStyle.WoodDark : new Color(InkStyle.WoodDark, 0.4f), 3f);
                if (i < cap)
                {
                    PortraitGlyph.Plus(this, r.GetCenter().X, r.Position.Y + 130f, 36f, InkStyle.WoodDark);
                    InkDraw.Text(this, new Vector2(r.GetCenter().X, r.Position.Y + 236f), "空位", PortraitLayout.FontMeta, InkStyle.WoodDark, "cm");
                }
            }
        }

        var ruleY = slotY + 380f;
        PortraitFrame.SectionRule(this, PortraitLayout.Pad, PortraitLayout.CanvasWidth - PortraitLayout.Pad, ruleY, "可出战");
        var listTop = ruleY + 46f;
        var footer = new Rect2(0, PortraitLayout.CanvasHeight - 220f, PortraitLayout.CanvasWidth, 220f);
        var visible = (int)((footer.Position.Y - 16f - listTop) / 150f);
        var candidates = _vm.Hub.State.Roster.Members.Where(m => !m.IsMaster).ToList();
        var first = Math.Clamp(Pan("party", candidates.Count, visible), 0, Math.Max(0, candidates.Count - visible));
        for (var i = 0; i < visible && first + i < candidates.Count; i++)
        {
            var who = candidates[first + i];
            var on = _party.Contains(who.Id);
            var can = on || members.Count < cap;
            var r = new Rect2(PortraitLayout.Pad, listTop + i * 150f, PortraitLayout.FullWidth, 130f);
            PortraitFrame.Card(this, r, on, 22f);
            PortraitFrame.Avatar(this, new Vector2(r.Position.X + 80f, r.GetCenter().Y), 40f, PortraitAvatars.Resolve(who), who.Name);
            InkDraw.TextBounded(this, new Rect2(r.Position.X + 150f, r.Position.Y, 300f, r.Size.Y), who.Name,
                PortraitLayout.FontBody, PortraitLayout.FontMeta, can ? InkStyle.Line : InkStyle.Dim, "lm");
            InkDraw.Text(this, new Vector2(r.Position.X + 470f, r.GetCenter().Y), $"Lv {who.Level}", PortraitLayout.FontMeta, InkStyle.Dim, "lm");
            var dot = new Vector2(r.End.X - 70f, r.GetCenter().Y);
            if (on)
            {
                DrawCircle(dot, 40f, InkStyle.Line);
                PortraitGlyph.Check(this, dot.X, dot.Y, 22f, InkStyle.Bg);
            }
            else
            {
                DrawArc(dot, 40f, 0f, Mathf.Tau, 40, can ? InkStyle.Line : InkStyle.WoodDark, 3f, true);
                PortraitGlyph.Plus(this, dot.X, dot.Y, 22f, can ? InkStyle.Line : InkStyle.WoodDark);
            }
            _widgets.Add(new PortraitWidget(r, PortraitAction.PartyPick, who.Id, can, who.Name));
        }
        RegisterScroll("party", new Rect2(0, listTop, PortraitLayout.CanvasWidth, visible * 150f), candidates.Count, visible, first,
            v => _pan["party"] = v, 150f);

        DrawRect(footer, InkStyle.Hover);
        PortraitFrame.FadingRule(this, 0f, PortraitLayout.CanvasWidth, footer.Position.Y);
        InkDraw.TextBounded(this, new Rect2(PortraitLayout.Pad + 20f, footer.Position.Y + 20f, 480f, 140f), FoesOf(def),
            PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "lm");
        var go = new Rect2(PortraitLayout.CanvasWidth - PortraitLayout.Pad - 440f, footer.Position.Y + 26f, 440f, 140f);
        PortraitFrame.Pill(this, go, "出发", primary: true, glyph: PortraitGlyph.Swords);
        _widgets.Add(new PortraitWidget(go, PortraitAction.QuestStart, 0, true, "出发"));
        return top;
    }

    private void StartQuest()
    {
        var defs = AvailableQuests();
        if (_questSel < 0 || _questSel >= defs.Count)
            return;
        var hub = _vm.Hub;
        var deploy = new List<int>(_party);
        var master = hub.State.Roster.Master;
        if (master != null && !deploy.Contains(master.Id))
            deploy.Add(master.Id);
        var run = hub.State.Quests.Start(defs[_questSel], deploy);
        if (run == null)
        {
            SetNotice("这单现在接不了。");
            return;
        }

        // 接下委托即遭遇：战斗本身就是委托入口（Encounters 起战，结算侧按 QuestRun 记通关与冷却）。
        var def = defs[_questSel];
        var session = Encounters.Start(hub.State, def.Foes, placeName: def.Name, partyIds: deploy);
        if (session == null)
        {
            SetNotice("这单现在接不了。");
            return;
        }
        session.QuestRun = run;
        _sheet = SheetKind.None;
        BattleWanted?.Invoke(session);
    }

    // ---------- 日志 ----------

    /// <summary>
    /// 日志时间线：Core 的近期日志（<c>HubSession.History</c>，最旧在前），每条一句「a，b」。
    /// 按游戏日分组（日序标题＋一条渐隐线），最新的日子与条目在最上；每条前一枚菱，竖线串起来。
    /// </summary>
    private void DrawLog()
    {
        var view = PortraitLayout.LogView;
        var history = _vm.Hub.History;
        var offset = _pan.GetValueOrDefault("log");
        var y = view.Position.Y + 30f - offset;
        const float rail = 80f;
        var textX = rail + 50f;
        var width = PortraitLayout.CanvasWidth - PortraitLayout.Pad - textX - 20f;
        var day = -1;
        for (var i = history.Count - 1; i >= 0; i--)
        {
            var entry = history[i];
            if (entry.Day != day)
            {
                day = entry.Day;
                var label = $"第 {day} 日";
                InkDraw.Text(this, new Vector2(PortraitLayout.Pad, y + 30f), label, PortraitLayout.FontBody, InkStyle.Line, "lm");
                InkDraw.FadeRule(this, PortraitLayout.Pad + 30f + InkDraw.Measure(label, PortraitLayout.FontBody).X,
                    PortraitLayout.CanvasWidth - PortraitLayout.Pad, y + 30f, PortraitLayout.LineHair, InkStyle.Dim, InkDraw.FadeTaper.Right);
                y += 110f;
            }
            var lines = InkDraw.WrapLines(entry.Text, width, PortraitLayout.FontMeta);
            var height = Mathf.Max(1, lines.Count) * PortraitLayout.LogLineHeight;
            var last = i == 0 || history[i - 1].Day != day;
            if (!last)
                InkDraw.InkLine(this, new Vector2(rail, y + 50f), new Vector2(rail, y + height + 40f), InkStyle.Hover, 3f);
            InkDraw.Jewel(this, new Vector2(rail, y + 30f), 12f, InkStyle.Line);
            for (var k = 0; k < lines.Count; k++)
                InkDraw.Text(this, new Vector2(textX, y + 30f + k * PortraitLayout.LogLineHeight), lines[k],
                    PortraitLayout.FontMeta, InkStyle.Line, "lm");
            y += height + 40f;
        }
        var total = (int)(y + offset - view.Position.Y);
        offset = Pan("log", total, (int)view.Size.Y);
        RegisterScroll("log", view, total, (int)view.Size.Y, offset, v => _pan["log"] = v, 1f);
        MaskAbove(view);
    }

    // ---------- 系统 ----------

    private static readonly string[] SystemLabels = { "存档", "设置" };

    private void DrawSystem()
    {
        var body = PortraitLayout.PageBody;
        var seg = new Rect2(PortraitLayout.Pad, body.Position.Y + 40f, PortraitLayout.FullWidth, PortraitLayout.TouchMin);
        var view = new Rect2(0, seg.End.Y + 30f, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - seg.End.Y - 30f);
        if (_systemSeg == 0)
            DrawSaveSlots(view);
        else
            DrawSettings(view);
        MaskAbove(view);
        PortraitFrame.Segmented(this, seg, SystemLabels, _systemSeg);
        for (var i = 0; i < SystemLabels.Length; i++)
            _widgets.Add(new PortraitWidget(PortraitFrame.SegmentRect(seg, 2, i), PortraitAction.SystemSegment, i, true, SystemLabels[i]));
        DrawPageTop("系统");
    }

    private void DrawSaveSlots(Rect2 view)
    {
        var slots = InkSaveStore.ListSaves();
        const float step = 250f;
        var total = (int)(slots.Count * step + 220f);
        var offset = Pan("system", total, (int)view.Size.Y);
        var add = new Rect2(PortraitLayout.Pad, view.Position.Y - offset, PortraitLayout.FullWidth, 200f);
        PortraitFrame.RoundRect(this, add, 18f, PortraitFrame.IsPressed(add) ? PortraitFrame.PressFill : null, InkStyle.WoodDark, 3f);
        PortraitGlyph.Plus(this, add.GetCenter().X, add.Position.Y + 76f, 34f, InkStyle.Dim);
        InkDraw.Text(this, new Vector2(add.GetCenter().X, add.Position.Y + 150f), "保存当前进度", PortraitLayout.FontMeta, InkStyle.Dim, "cm");
        AddClipped(add, view, PortraitAction.SaveNow, 0, true, "保存");
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var r = new Rect2(PortraitLayout.Pad, add.End.Y + 20f + i * step, PortraitLayout.FullWidth, 230f);
            if (r.End.Y < view.Position.Y || r.Position.Y > view.End.Y)
                continue;
            DrawSaveCard(r, slot);
            AddClipped(r, view, PortraitAction.SavePick, i, true, slot.FilePath);
        }
        RegisterScroll("system", view, total, (int)view.Size.Y, offset, v => _pan["system"] = v, 1f);
    }

    private void DrawSaveCard(Rect2 r, SaveSlotInfo slot)
    {
        PortraitFrame.Card(this, r);
        var thumb = new Rect2(r.Position.X + 40f, r.Position.Y + 35f, 220f, 160f);
        DrawRect(thumb, InkStyle.Bg);
        InkDraw.Ink(this, RectLoop(thumb), InkStyle.Dim, 3f);
        PortraitGlyph.Castle(this, thumb.GetCenter().X, thumb.GetCenter().Y, 44f, InkStyle.Dim);
        var x = thumb.End.X + 40f;
        InkDraw.TextBounded(this, new Rect2(x, r.Position.Y + 36f, r.End.X - x - 200f, 70f), slot.TerritoryName,
            PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "lm");
        InkDraw.Text(this, new Vector2(r.End.X - 40f, r.Position.Y + 72f), $"第 {slot.Day} 日", PortraitLayout.FontMeta, InkStyle.Dim, "rm");
        var time = DateTime.ParseExact(slot.Timestamp, "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        InkDraw.TextBounded(this, new Rect2(x, r.Position.Y + 130f, r.End.X - x - 40f, 60f),
            time.ToString("yyyy-MM-dd HH时mm分", System.Globalization.CultureInfo.InvariantCulture),
            PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "lm");
    }

    /// <summary>快速设置：主音量滑条（五档命中，画面连续）。手机上没有窗口模式与垂直同步可调。</summary>
    private void DrawSettings(Rect2 view)
    {
        InkSettings.EnsureLoaded();
        var y = view.Position.Y + 20f;
        InkDraw.Text(this, new Vector2(PortraitLayout.Pad + 20f, y + 59f), "主音量", PortraitLayout.FontBody, InkStyle.Line, "lm");
        var track = new Rect2(380f, y + 51f, PortraitLayout.CanvasWidth - 380f - PortraitLayout.Pad - 40f, 16f);
        var volume = InkSettings.CurrentMasterVolume;
        PortraitFrame.Bar(this, track, volume);
        DrawCircle(new Vector2(track.Position.X + track.Size.X * volume, track.GetCenter().Y), 28f, InkStyle.Line);
        for (var i = 0; i <= 4; i++)
        {
            var cx = track.Position.X + track.Size.X * i / 4f;
            var hit = new Rect2(cx - track.Size.X / 8f, y, track.Size.X / 4f, PortraitLayout.TouchMin);
            _widgets.Add(new PortraitWidget(hit.Intersection(new Rect2(track.Position.X - 60f, y, track.Size.X + 120f, PortraitLayout.TouchMin)),
                PortraitAction.VolumeSet, i, true, $"{i * 25}"));
        }
        InkDraw.Text(this, new Vector2(PortraitLayout.CanvasWidth - PortraitLayout.Pad, y + 150f), $"{(int)(volume * 100)}",
            PortraitLayout.FontMeta, InkStyle.Dim, "rm");
        InkDraw.InkLine(this, new Vector2(PortraitLayout.Pad, y + 200f), new Vector2(PortraitLayout.CanvasWidth - PortraitLayout.Pad, y + 200f),
            InkStyle.Hover, 2f);
    }

    private void LoadSave(string path)
    {
        Confirm("读取进度", "", () =>
        {
            if (InkSaveStore.TryLoad(path, _content, out var state, out var hub, out _) && state != null && hub != null)
                LoadRequested?.Invoke(state, hub);
        });
    }

    private bool ExecutePages(PortraitWidget w)
    {
        var hub = _vm.Hub;
        switch (w.Action)
        {
            case PortraitAction.QuestTake:
                _questSel = w.Index;
                _party.Clear();
                _sheet = SheetKind.Party;
                _pan.Remove("party");
                return true;
            case PortraitAction.PartyPick:
                if (_party.Contains(w.Index))
                    _party.Remove(w.Index);
                else if (_party.Count + 1 < PartyCap())
                    _party.Add(w.Index);
                return true;
            case PortraitAction.QuestStart:
                StartQuest();
                return true;
            case PortraitAction.SystemSegment:
                _systemSeg = w.Index;
                _pan.Remove("system");
                return true;
            case PortraitAction.SaveNow:
                SetNotice(InkSaveStore.SaveNew(hub.State, hub, out _) != null ? "已保存。" : "保存失败。");
                return true;
            case PortraitAction.SavePick:
                LoadSave(w.Label);
                return true;
            case PortraitAction.VolumeSet:
                InkSettings.ApplyMasterVolume(w.Index / 4f);
                return true;
            default:
                return false;
        }
    }
}
