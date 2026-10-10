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

    /// <summary>调试：委托板上第一个满足条件的委托排第几（找不到为 -1）。</summary>
    public int DebugQuestIndex(Func<QuestDef, bool> pick) => AvailableQuests().FindIndex(d => pick(d));

    private List<QuestDef> AvailableQuests() => QuestBoard.Open(_vm.Hub.State);

    /// <summary>委托的敌人：同名合并计数；连战把各波都算上。一项一段，画成题签行。</summary>
    private static List<string> FoesOf(QuestDef def) => def.Foes.Concat(def.Waves.SelectMany(w => w))
        .GroupBy(f => f.Name).Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key).ToList();

    /// <summary>
    /// 委托卡的版式（自上而下）：名字行、难度行（敌方放得下就跟在菱形后，放不下就自下一行起按字宽换行）、
    /// 整段描述（按字宽换行，不截断）、分隔线、报酬行（放不下就换行）＋「接取」。
    /// 内容放得下时与原版一致（560 高）；放不下就往下长，不截「…」。
    /// </summary>
    private readonly record struct QuestCardLayout(bool FoesInline, IReadOnlyList<IReadOnlyList<string>> Foes, IReadOnlyList<string> Body,
        float DividerY, IReadOnlyList<IReadOnlyList<string>> Rewards, float Height);

    private const float QuestLineStep = 66f;
    private const float QuestRewardStep = 54f;

    private static float QuestFoesX(QuestDef def, float x) =>
        x + 130f + Math.Min(10, Math.Max(5, (int)Math.Ceiling(def.Difficulty))) * 44f;

    /// <summary>题签分项折行：整项放得下就整项挪到下一行，不把「不明矿块」拆成「不 / 明矿块」；单项一行都放不下才按字宽断。</summary>
    private static List<IReadOnlyList<string>> WrapTags(IReadOnlyList<string> items, float width)
    {
        var lines = new List<IReadOnlyList<string>>();
        var line = new List<string>();
        foreach (var item in items)
        {
            line.Add(item);
            if (PortraitFrame.TagWidth(line) <= width)
                continue;
            line.RemoveAt(line.Count - 1);
            if (line.Count > 0)
                lines.Add(line);
            var parts = InkDraw.WrapLines(item, width, PortraitLayout.FontMeta);
            lines.AddRange(parts.Take(parts.Count - 1).Select(p => (IReadOnlyList<string>)new[] { p }));
            line = new List<string> { parts[^1] };
        }
        lines.Add(line);
        return lines;
    }

    private static QuestCardLayout LayoutQuest(QuestDef def, Rect2 r)
    {
        var x = r.Position.X + 50f;
        var foes = FoesOf(def);
        var foesInline = PortraitFrame.TagWidth(foes) <= r.End.X - 50f - QuestFoesX(def, x);
        var foeLines = foesInline ? new List<IReadOnlyList<string>>() : WrapTags(foes, r.Size.X - 100f);
        var body = def.Description.Length > 0 ? def.Description : def.Rumor.Length > 0 ? $"「{def.Rumor}」" : "";
        var bodyLines = InkDraw.WrapLines(body, r.Size.X - 100f, PortraitLayout.FontMeta);
        var lastLine = 150f + (foeLines.Count + bodyLines.Count) * QuestLineStep;
        var divider = Math.Max(420f, lastLine + 68f);
        var take = QuestTakeRect(r, divider, 0f);
        var rewards = WrapTags(def.Rewards, take.Position.X - x - 74f);
        // 折行时报酬带上下各多留一截，末行不贴到卡底内框线上。
        var band = Math.Max(PortraitLayout.TouchMin, rewards.Count * QuestRewardStep + 40f);
        return new QuestCardLayout(foesInline, foeLines, bodyLines, divider, rewards, divider + 12f + band + 10f);
    }

    /// <summary>「接取」：报酬带右端，在报酬带里上下居中。</summary>
    private static Rect2 QuestTakeRect(Rect2 r, float divider, float band) =>
        new(r.End.X - 40f - 240f, r.Position.Y + divider + 12f + Math.Max(0f, band - PortraitLayout.TouchMin) / 2f, 240f, PortraitLayout.TouchMin);

    private void DrawQuestBoard()
    {
        var view = PortraitLayout.QuestView;
        var defs = AvailableQuests();
        var probe = new Rect2(PortraitLayout.Pad, 0f, PortraitLayout.FullWidth, PortraitLayout.QuestCardHeight);
        var layouts = defs.Select(d => LayoutQuest(d, probe)).ToList();
        var total = (int)layouts.Sum(l => l.Height + PortraitLayout.QuestCardGap);
        var offset = Pan("quests", total, (int)view.Size.Y);
        var top = view.Position.Y + 16f - offset;
        for (var i = 0; i < defs.Count; i++)
        {
            var def = defs[i];
            var lay = layouts[i];
            var r = new Rect2(PortraitLayout.Pad, top, PortraitLayout.FullWidth, lay.Height);
            top += lay.Height + PortraitLayout.QuestCardGap;
            if (r.End.Y < view.Position.Y || r.Position.Y > view.End.Y)
                continue;
            PortraitFrame.GothicFrame(this, r);
            var x = r.Position.X + 50f;
            var size = def.MaxPartySize > 0 ? $"{def.KindText} · {def.MaxPartySize} 人" : def.KindText;
            var pillW = size.Length > 0 ? InkDraw.Measure(size, PortraitLayout.FontMeta).X + 56f : 0f;
            InkDraw.TextBounded(this, new Rect2(x, r.Position.Y + 36f, r.Size.X - 140f - pillW, 80f), def.Name,
                PortraitLayout.FontTitle, PortraitLayout.FontBody, InkStyle.Line, "lm");
            if (size.Length > 0)
            {
                var pill = new Rect2(r.End.X - 50f - pillW, r.Position.Y + 44f, pillW, 64f);
                PortraitFrame.Brackets(this, pill, InkStyle.Dim);
                InkDraw.Text(this, pill.GetCenter(), size, PortraitLayout.FontMeta, InkStyle.Dim, "cm");
            }
            InkDraw.Text(this, new Vector2(x, r.Position.Y + 150f), "难度", PortraitLayout.FontMeta, InkStyle.Dim, "lm");
            var stars = Math.Min(10, Math.Max(5, (int)Math.Ceiling(def.Difficulty)));
            PortraitFrame.Ticks(this, x + 110f, r.Position.Y + 150f, stars, (float)def.Difficulty, 30f, 14f);
            if (lay.FoesInline)
                PortraitFrame.TagLine(this, QuestFoesX(def, x), r.Position.Y + 150f, FoesOf(def), r.End.X - 40f, InkStyle.Dim);
            var lineY = r.Position.Y + 150f;
            foreach (var line in lay.Foes)
                PortraitFrame.TagLine(this, x, lineY += QuestLineStep, line, r.End.X - 40f, InkStyle.Dim);
            foreach (var line in lay.Body)
                InkDraw.Text(this, new Vector2(x, lineY += QuestLineStep), line,
                    PortraitLayout.FontMeta, def.Description.Length > 0 ? InkStyle.Line : InkStyle.Dim, "lm");
            var dividerY = r.Position.Y + lay.DividerY;
            InkDraw.InkLine(this, new Vector2(r.Position.X + 30f, dividerY), new Vector2(r.End.X - 30f, dividerY), InkStyle.WoodDark, 2f);
            var band = lay.Height - lay.DividerY - 22f;
            var take = QuestTakeRect(r, lay.DividerY, band);
            var rewardTop = dividerY + 12f + band / 2f - (lay.Rewards.Count - 1) * QuestRewardStep / 2f;
            PortraitGlyph.Coin(this, x + 18f, rewardTop, 18f, InkStyle.Dim);
            for (var k = 0; k < lay.Rewards.Count; k++)
                PortraitFrame.TagLine(this, x + 54f, rewardTop + k * QuestRewardStep, lay.Rewards[k], take.Position.X - 12f, InkStyle.Line);
            PortraitFrame.Plaque(this, take, "接取");
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
        // 队位只画到人数上限：上限 3 就只有三格，不留一格压暗的空框。
        // 队位整排居中（2026-10-10 主人定）。
        var slotsLeft = PortraitLayout.CanvasWidth / 2f - (cap * w + (cap - 1) * 20f) / 2f;
        for (var i = 0; i < cap; i++)
        {
            var r = new Rect2(slotsLeft + i * (w + 20f), slotY, w, 320f);
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
                PortraitFrame.Bevel(this, r, 18f, null, InkStyle.WoodDark, 3f);
                PortraitGlyph.Plus(this, r.GetCenter().X, r.Position.Y + 130f, 36f, InkStyle.WoodDark);
                InkDraw.Text(this, new Vector2(r.GetCenter().X, r.Position.Y + 236f), "空位", PortraitLayout.FontMeta, InkStyle.WoodDark, "cm");
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
                InkDraw.Jewel(this, dot, 44f, InkStyle.Line);
                InkDraw.Jewel(this, dot, 38f, InkStyle.Line);
                PortraitGlyph.Check(this, dot.X, dot.Y, 22f, InkStyle.Bg);
            }
            else
            {
                InkDraw.Jewel(this, dot, 44f, can ? InkStyle.Line : InkStyle.WoodDark);
                InkDraw.Jewel(this, dot, 40f, InkStyle.Panel);
                PortraitGlyph.Plus(this, dot.X, dot.Y, 22f, can ? InkStyle.Line : InkStyle.WoodDark);
            }
            _widgets.Add(new PortraitWidget(r, PortraitAction.PartyPick, who.Id, can, who.Name));
        }
        RegisterScroll("party", new Rect2(0, listTop, PortraitLayout.CanvasWidth, visible * 150f), candidates.Count, visible, first,
            v => _pan["party"] = v, 150f);

        DrawRect(footer, InkStyle.Hover);
        PortraitFrame.FadingRule(this, 0f, PortraitLayout.CanvasWidth, footer.Position.Y);
        // 来敌：题签行，至多两行，在出发钮左侧上下居中。
        var foeLines = WrapTags(FoesOf(def), 480f).Take(2).ToList();
        var foeY = footer.Position.Y + 90f - (foeLines.Count - 1) * 30f;
        foreach (var line in foeLines)
        {
            PortraitFrame.TagLine(this, PortraitLayout.Pad + 20f, foeY, line, PortraitLayout.Pad + 500f, InkStyle.Dim);
            foeY += 60f;
        }
        var go = new Rect2(PortraitLayout.CanvasWidth - PortraitLayout.Pad - 440f, footer.Position.Y + 26f, 440f, 140f);
        PortraitFrame.Plaque(this, go, "出发", primary: true, glyph: PortraitGlyph.Swords);
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

        var def = defs[_questSel];
        // 地城探索委托＝包接送的地城：马车送进地城，正主倒下即了结接回（Core 收尾）。
        if (def.Kind == QuestKind.Dungeon)
        {
            if (!hub.StartQuestDungeon(run))
            {
                SetNotice("回到领地才能接这单。");
                return;
            }
            _sheet = SheetKind.None;
            ShowTab(0);
            return;
        }

        // 战斗委托：接下即开打（普通战斗 / 首领战一波打完；连战首波之后依次补上各波，清一波一个补给回合）。
        // 结算侧按 QuestRun 记通关与冷却。
        var session = Encounters.Start(hub.State, def.Foes, placeName: def.Name, partyIds: deploy,
            waves: def.Battle == QuestBattle.Waves ? def.Waves : null);
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
        PortraitFrame.Bevel(this, add, 18f, PortraitFrame.IsPressed(add) ? PortraitFrame.PressFill : null, InkStyle.WoodDark, 3f);
        PortraitGlyph.Plus(this, add.GetCenter().X, add.Position.Y + 76f, 34f, InkStyle.Dim);
        InkDraw.Text(this, new Vector2(add.GetCenter().X, add.Position.Y + 150f), "保存当前进度", PortraitLayout.FontMeta, InkStyle.Dim, "cm");
        AddClipped(add, view, PortraitAction.SaveNow, 0, true, "保存");
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var r = new Rect2(PortraitLayout.Pad, add.End.Y + 20f + i * step, PortraitLayout.FullWidth, 230f);
            if (r.End.Y < view.Position.Y || r.Position.Y > view.End.Y)
                continue;
            DrawSaveCard(r, slot, i == 0);
            AddClipped(r, view, PortraitAction.SavePick, i, true, slot.FilePath);
        }
        RegisterScroll("system", view, total, (int)view.Size.Y, offset, v => _pan["system"] = v, 1f);
    }

    private void DrawSaveCard(Rect2 r, SaveSlotInfo slot, bool latest) =>
        PortraitSystemArt.SaveCard(this, r, slot, latest, PortraitFrame.IsPressed(r));

    /// <summary>快速设置：主音量滑条（五档命中，画面连续）。手机上没有窗口模式与垂直同步可调。</summary>
    private void DrawSettings(Rect2 view)
    {
        InkSettings.EnsureLoaded();
        var y = view.Position.Y + 20f;
        var volume = InkSettings.CurrentMasterVolume;
        PortraitSystemArt.Volume(this, y, volume);
        var track = new Rect2(380f, y + 51f, PortraitLayout.CanvasWidth - 380f - PortraitLayout.Pad - 40f, 16f);
        for (var i = 0; i <= 4; i++)
        {
            var cx = track.Position.X + track.Size.X * i / 4f;
            var hit = new Rect2(cx - track.Size.X / 8f, y, track.Size.X / 4f, PortraitLayout.TouchMin);
            _widgets.Add(new PortraitWidget(hit.Intersection(new Rect2(track.Position.X - 60f, y, track.Size.X + 120f, PortraitLayout.TouchMin)),
                PortraitAction.VolumeSet, i, true, $"{i * 25}"));
        }
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
