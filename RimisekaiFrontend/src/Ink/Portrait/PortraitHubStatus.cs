using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Character;
using Rimisekai.Defs;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 角色页签（名册卡片流）与角色详情推入页：立绘头＋分段（状态 / 技能 / 日程）。
/// 数据一律取 InkCharacterPageBuilder（与旧状态页同一口径），模型没有的东西不画。
/// </summary>
public partial class PortraitHubScreen
{
    private int _charId = -1;
    private int _charSeg;

    private static readonly InkPage[] CharacterSegments = { InkPage.Status, InkPage.Skills, InkPage.Schedule };
    private static readonly string[] CharacterSegmentLabels = { "状态", "技能", "日程" };

    private static readonly EquipSlot[] DisplayEquipSlots =
    {
        EquipSlot.MainHand, EquipSlot.OffHand, EquipSlot.Head, EquipSlot.Torso, EquipSlot.Legs,
        EquipSlot.Hands, EquipSlot.Feet, EquipSlot.Neck, EquipSlot.Ring1, EquipSlot.Ring2,
    };

    private CharacterState Who => _vm.Hub.State.Roster.Find(_charId)!;

    private void OpenCharacter(int id)
    {
        _charId = id;
        _charSeg = 0;
        _push = PushPage.Character;
        _sheet = SheetKind.None;
        _interactionOpen = false;
        _pan.Remove("character");
        ResetSkillView();
    }

    private static string RoleOf(CharacterState who) => who.IsMaster ? "领主" : "同伴";

    private IEnumerable<string> TraitsOf(CharacterState who) =>
        TraitDefsOf(who).Select(d => d.Name);

    private IEnumerable<PersonalityTraits.Def> TraitDefsOf(CharacterState who) =>
        Rimisekai.Character.Traits.Catalog.Where(d => who.Has(d.Trait));

    // ---------- 名册 ----------

    private void DrawRoster()
    {
        var view = PortraitLayout.RosterView;
        var members = _vm.Hub.State.Roster.Members;
        var step = PortraitLayout.RosterCardHeight + PortraitLayout.RosterCardGap;
        var total = (int)(members.Count * step);
        var offset = Pan("roster", total, (int)view.Size.Y);
        for (var i = 0; i < members.Count; i++)
        {
            var r = new Rect2(PortraitLayout.Pad, view.Position.Y + i * step - offset, PortraitLayout.FullWidth,
                PortraitLayout.RosterCardHeight);
            if (r.End.Y < view.Position.Y || r.Position.Y > view.End.Y)
                continue;
            DrawRosterCard(r, members[i]);
            AddClipped(r, view, PortraitAction.RosterPick, members[i].Id, true, members[i].Name);
        }
        RegisterScroll("roster", view, total, (int)view.Size.Y, offset, v => _pan["roster"] = v, 1f);
        MaskAbove(view);
    }

    private void DrawRosterCard(Rect2 r, CharacterState who)
    {
        PortraitFrame.Card(this, r, who.IsMaster);
        var x = r.Position.X;
        var y = r.Position.Y;
        PortraitFrame.Avatar(this, new Vector2(x + 110f, y + 110f), 74f, PortraitAvatars.Resolve(who), who.Name);
        InkDraw.Text(this, new Vector2(x + 220f, y + 62f), who.Name, PortraitLayout.FontPlace, InkStyle.Line, "lm");
        InkDraw.Text(this, new Vector2(x + 240f + InkDraw.Measure(who.Name, PortraitLayout.FontPlace).X, y + 66f),
            $"{RoleOf(who)} · Lv {who.Level}", PortraitLayout.FontMeta, InkStyle.Dim, "lm");

        var activity = ActivityOf(who.Id);
        var pillW = Mathf.Min(360f, InkDraw.Measure(activity, PortraitLayout.FontMeta).X + 56f);
        var pill = new Rect2(r.End.X - 30f - pillW, y + 30f, pillW, 66f);
        if (r.Size.X - 220f - pillW > InkDraw.Measure(who.Name, PortraitLayout.FontPlace).X + 260f)
        {
            PortraitFrame.Brackets(this, pill, InkStyle.Dim);
            InkDraw.TextBounded(this, pill.Grow(-16f), activity, PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Line, "cm");
        }

        var tx = x + 220f;
        foreach (var trait in TraitsOf(who).Take(3))
        {
            if (tx + PortraitFrame.ChipWidth(trait) > r.End.X - 30f)
                break;
            tx += PortraitFrame.Tag(this, new Vector2(tx, y + 116f), trait, 66f) + 16f;
        }

        var c = who.Condition;
        var bars = new (string Label, float Frac)[]
        {
            ("体力", c.MaxStamina > 0 ? (float)c.Stamina / c.MaxStamina : 0f),
            ("气力", c.MaxSpirit > 0 ? (float)c.Spirit / c.MaxSpirit : 0f),
            ("心情", who.Affect.Mood / 100f),
        };
        var bw = (r.End.X - 30f - (x + 220f) - 40f) / 3f;
        for (var k = 0; k < bars.Length; k++)
        {
            var bx = x + 220f + k * (bw + 20f);
            InkDraw.Text(this, new Vector2(bx, y + 228f), bars[k].Label, PortraitLayout.FontMeta, InkStyle.Dim, "lm");
            PortraitFrame.Bar(this, new Rect2(bx, y + 266f, bw, 16f), bars[k].Frac);
        }
    }

    // ---------- 角色详情 ----------

    private void DrawCharacterPage()
    {
        var who = Who;
        var view = PortraitLayout.PageBody;
        var offset = _pan.GetValueOrDefault("character");
        var y0 = view.Position.Y - offset;

        // 立绘头：覆盖铺满，下缘渐隐进黑场。
        var head = new Rect2(0, y0, PortraitLayout.CanvasWidth, 620f);
        var art = LoadCharacterPortrait(who);
        if (art != null)
        {
            PortraitFrame.Cover(this, art, head, 0.04f);
            PortraitFrame.Fade(this, new Rect2(0, head.Position.Y + 250f, head.Size.X, 370f), 0f, 1f);
        }
        else
            PortraitSystemArt.PortraitNiche(this, new Rect2(head.GetCenter().X - 230f, head.Position.Y + 50f, 460f, 570f),
                PortraitAvatars.Resolve(who), who.Name);

        var hitHead = head.Intersection(view);
        if (hitHead.Size.Y >= PortraitLayout.TouchMin)
            _widgets.Add(new PortraitWidget(hitHead, PortraitAction.OpenPortraitPicker, who.Id, true, "立绘"));

        var segAt = y0 + 640f;
        var y = segAt + PortraitLayout.TouchMin + 48f;
        // 内容命中块只登记在吸顶分段控件之下，免得与分段控件抢命中。
        var contentView = new Rect2(0, view.Position.Y + PortraitLayout.TouchMin + 40f, view.Size.X,
            view.Size.Y - PortraitLayout.TouchMin - 40f);
        var contentEnd = _charSeg switch
        {
            0 => DrawStatusSegment(who, y, contentView),
            1 => DrawSkillsSegment(who, y, contentView),
            _ => DrawScheduleSegment(who, y, contentView),
        };
        var total = (int)(contentEnd + offset - view.Position.Y + 60f);
        offset = Pan("character", total, (int)view.Size.Y);
        RegisterScroll("character", view, total, (int)view.Size.Y, offset, v => _pan["character"] = v, 1f);

        // 分段控件吸顶：滚过头时停在顶栏下方。
        var seg = new Rect2(PortraitLayout.Pad, Mathf.Max(view.Position.Y + 20f, segAt), PortraitLayout.FullWidth, PortraitLayout.TouchMin);
        if (seg.Position.Y <= view.Position.Y + 20f)
            DrawRect(new Rect2(0, view.Position.Y, PortraitLayout.CanvasWidth, seg.End.Y + 20f - view.Position.Y), InkStyle.Bg);
        PortraitFrame.Segmented(this, seg, CharacterSegmentLabels, _charSeg);
        for (var i = 0; i < CharacterSegmentLabels.Length; i++)
            _widgets.Add(new PortraitWidget(PortraitFrame.SegmentRect(seg, CharacterSegmentLabels.Length, i),
                PortraitAction.CharacterSegment, i, true, CharacterSegmentLabels[i]));

        DrawPageTop(who.Name, $"{RoleOf(who)} · Lv {who.Level}");
    }

    /// <summary>状态：2×2 体征卡 / 属性 3×2 / 战斗 3×2 / 特质签 / 装备双列。返回内容下沿。</summary>
    private float DrawStatusSegment(CharacterState who, float y, Rect2 view)
    {
        var page = InkCharacterPageBuilder.Build(_vm, InkPage.Status, who)!;
        var vitals = new List<InkPageRow>();
        var combat = new List<InkPageRow>();
        var attributes = new List<InkPageRow>();
        var inGroups = false;
        foreach (var row in page.Rows)
        {
            if (row.IsHeading)
            {
                inGroups = true;
                continue;
            }
            if (inGroups)
                continue;
            if (row.Name is "体力" or "气力" or "好感" or "心情") vitals.Add(row);
            else if (row.Name is "攻击" or "血量" or "防御" or "闪避" or "法强" or "速度") combat.Add(row);
            else attributes.Add(row);
        }

        var cw = (PortraitLayout.FullWidth - 20f) / 2f;
        for (var i = 0; i < vitals.Count; i++)
        {
            var row = vitals[i];
            var r = new Rect2(PortraitLayout.Pad + i % 2 * (cw + 20f), y + i / 2 * 170f, cw, 150f);
            PortraitFrame.Card(this, r);
            InkDraw.Text(this, new Vector2(r.Position.X + 36f, r.Position.Y + 50f), row.Name, PortraitLayout.FontMeta, InkStyle.Dim, "lm");
            InkDraw.Text(this, new Vector2(r.End.X - 36f, r.Position.Y + 50f), row.Value, PortraitLayout.FontBody, InkStyle.Line, "rm");
            if (row.MeterValue.HasValue && row.MeterMax > 0f)
                PortraitFrame.Bar(this, new Rect2(r.Position.X + 36f, r.Position.Y + 102f, r.Size.X - 72f, 16f),
                    row.MeterValue.Value / row.MeterMax);
            else if (row.Note.Length > 0)
                InkDraw.Text(this, new Vector2(r.Position.X + 36f, r.Position.Y + 110f), row.Note, PortraitLayout.FontMeta, InkStyle.Line, "lm");
        }
        y += (vitals.Count + 1) / 2 * 170f + 40f;

        y = DrawMetricGrid("属性", attributes, y, withMeter: true);
        y = DrawMetricGrid("战斗", combat, y, withMeter: false);

        var traits = TraitsOf(who).ToList();
        if (traits.Count > 0)
        {
            PortraitFrame.SectionRule(this, PortraitLayout.Pad, PortraitLayout.CanvasWidth - PortraitLayout.Pad, y, "特质");
            y += 50f;
            // 特质签可点（弹窗看说明）：命中块 118 高、画出来的签 80 高居中，行距 128 免得上下命中块相叠。
            // 先按宽度分好行，每行整体居中（2026-10-10 主人定：能对称居中的尽量居中）。
            const float chipGap = 20f;
            var span = PortraitLayout.CanvasWidth - PortraitLayout.Pad * 2f;
            var rows = new List<List<int>> { new() };
            var rowW = 0f;
            for (var i = 0; i < traits.Count; i++)
            {
                var w = Mathf.Max(PortraitLayout.TouchMin, PortraitFrame.ChipWidth(traits[i]) - 16f);
                if (rows[^1].Count > 0 && rowW + chipGap + w > span)
                {
                    rows.Add(new List<int>());
                    rowW = 0f;
                }
                rowW += (rows[^1].Count > 0 ? chipGap : 0f) + w;
                rows[^1].Add(i);
            }
            for (var row = 0; row < rows.Count; row++)
            {
                if (row > 0)
                    y += 128f;
                var widths = rows[row].Select(i => Mathf.Max(PortraitLayout.TouchMin, PortraitFrame.ChipWidth(traits[i]) - 16f)).ToList();
                var total = widths.Sum() + chipGap * (widths.Count - 1);
                var x = PortraitLayout.CanvasWidth / 2f - total / 2f;
                for (var k = 0; k < rows[row].Count; k++)
                {
                    var i = rows[row][k];
                    var r = new Rect2(x, y + 40f - PortraitLayout.TouchMin / 2f, widths[k], PortraitLayout.TouchMin);
                    PortraitFrame.Chip(this, r, traits[i], false, 80f);
                    AddClipped(r, view, PortraitAction.TraitInfo, i, true, traits[i]);
                    x += widths[k] + chipGap;
                }
            }
            y += 140f;
        }

        PortraitFrame.SectionRule(this, PortraitLayout.Pad, PortraitLayout.CanvasWidth - PortraitLayout.Pad, y, "装备");
        y += 50f;
        var registry = _vm.Hub.State.Equips;
        for (var i = 0; i < DisplayEquipSlots.Length; i++)
        {
            var slot = DisplayEquipSlots[i];
            var r = new Rect2(PortraitLayout.Pad + i % 2 * (cw + 20f), y + i / 2 * 166f, cw, 150f);
            var equip = EquipmentName(who, slot, registry);
            PortraitFrame.Card(this, r);
            InkDraw.Text(this, new Vector2(r.GetCenter().X, r.Position.Y + 46f), EquipSlots.Label(slot), PortraitLayout.FontMeta, InkStyle.Dim, "cm");
            InkDraw.TextBounded(this, new Rect2(r.Position.X + 20f, r.Position.Y + 76f, r.Size.X - 40f, 60f), equip,
                PortraitLayout.FontBody, PortraitLayout.FontMeta, equip == "空" ? InkStyle.WoodDark : InkStyle.Line, "cm");
            AddClipped(r, view, PortraitAction.EquipInfo, i, true, EquipSlots.Label(slot));
        }
        y += (DisplayEquipSlots.Length + 1) / 2 * 166f;
        return y;
    }

    private float DrawMetricGrid(string title, IReadOnlyList<InkPageRow> rows, float y, bool withMeter)
    {
        if (rows.Count == 0)
            return y;
        PortraitFrame.SectionRule(this, PortraitLayout.Pad, PortraitLayout.CanvasWidth - PortraitLayout.Pad, y, title);
        y += 50f;
        var w = (PortraitLayout.FullWidth - 40f) / 3f;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var r = new Rect2(PortraitLayout.Pad + i % 3 * (w + 20f), y + i / 3 * 130f, w, 110f);
            PortraitFrame.Bevel(this, r, 20f, null, InkStyle.WoodDark, 3f);
            InkDraw.Text(this, new Vector2(r.Position.X + 28f, r.GetCenter().Y - (withMeter ? 6f : 0f)), row.Name,
                PortraitLayout.FontMeta, InkStyle.Dim, "lm");
            InkDraw.Text(this, new Vector2(r.End.X - 28f, r.GetCenter().Y - (withMeter ? 6f : 0f)), row.Value,
                PortraitLayout.FontPlace, InkStyle.Line, "rm");
            if (withMeter && row.MeterMax > 0f)
                PortraitFrame.Bar(this, new Rect2(r.Position.X + 28f, r.End.Y - 22f, r.Size.X - 56f, 8f),
                    (row.MeterValue ?? 0f) / row.MeterMax);
        }
        return y + (rows.Count + 2) / 3 * 130f + 40f;
    }

    /// <summary>展开着的熟练分组（生活 / 武器 / 流派）；不在此集里的分组只露最高那一项。</summary>
    private readonly HashSet<string> _skillGroupsOpen = new();

    /// <summary>
    /// 技能：生活 / 武器 / 流派三组熟练，每组默认只露最高的一项（组底「全部 n ▾」），点该组展开整组（按等级从高到低），再点收起。
    /// 其下直接是战斗技能星盘与所选技能的详情（不另开页面）。
    /// </summary>
    private float DrawSkillsSegment(CharacterState who, float y, Rect2 view)
    {
        var page = InkCharacterPageBuilder.Build(_vm, InkPage.Status, who)!;
        var groups = new List<(string Name, List<InkPageRow> Rows)>();
        foreach (var row in page.Rows)
        {
            if (row.IsHeading)
                groups.Add((row.Name, new List<InkPageRow>()));
            else if (groups.Count > 0)
                groups[^1].Rows.Add(row);
        }
        foreach (var (name, rows) in groups)
        {
            if (rows.Count == 0)
                continue;
            PortraitFrame.SectionRule(this, PortraitLayout.Pad, PortraitLayout.CanvasWidth - PortraitLayout.Pad, y, name);
            y += 40f;
            var open = _skillGroupsOpen.Contains(name);
            var sorted = rows.OrderByDescending(r => LevelOf(r.Value)).ToList();
            var shown = open ? sorted : sorted.Take(1).ToList();
            var groupTop = y;
            for (var i = 0; i < shown.Count; i++)
            {
                var row = shown[i];
                var level = LevelOf(row.Value);
                InkDraw.TextBounded(this, new Rect2(PortraitLayout.Pad + 20f, y, 230f, 100f), row.Name, PortraitLayout.FontBody,
                    PortraitLayout.FontMeta, InkStyle.Line, "lm");
                PortraitFrame.Ticks(this, 280f, y + 50f, 10, Mathf.Min(level, 10), 30f, 22f);
                InkDraw.Text(this, new Vector2(PortraitLayout.CanvasWidth - PortraitLayout.Pad - 10f, y + 50f),
                    $"Lv{level}", PortraitLayout.FontMeta, i == 0 ? InkStyle.Line : InkStyle.Dim, "rm");
                if (i < shown.Count - 1)
                    InkDraw.InkLine(this, new Vector2(PortraitLayout.Pad, y + 104f),
                        new Vector2(PortraitLayout.CanvasWidth - PortraitLayout.Pad, y + 104f), InkStyle.Hover, 2f);
                y += 110f;
            }
            // 展开 / 收起钮：组底一枚居中的「全部 n ▾」或「收起 ▴」，与整组同一命中块。
            if (sorted.Count > 1)
            {
                var label = open ? "收起" : $"全部 {sorted.Count}";
                var cx = PortraitLayout.CanvasWidth / 2f;
                var w = InkDraw.Measure(label, PortraitLayout.FontMeta).X;
                InkDraw.Text(this, new Vector2(cx - 14f, y + 26f), label, PortraitLayout.FontMeta, InkStyle.Dim, "cm");
                var tx = cx - 14f + w / 2f + 26f;
                var ty = y + 28f;
                DrawColoredPolygon(open
                    ? new[] { new Vector2(tx - 12f, ty + 7f), new Vector2(tx + 12f, ty + 7f), new Vector2(tx, ty - 9f) }
                    : new[] { new Vector2(tx - 12f, ty - 7f), new Vector2(tx + 12f, ty - 7f), new Vector2(tx, ty + 9f) }, InkStyle.Dim);
                y += 60f;
                AddClipped(new Rect2(PortraitLayout.Pad, groupTop, PortraitLayout.FullWidth, y - groupTop), view,
                    PortraitAction.SkillGroup, groups.FindIndex(g => g.Name == name), true, name);
            }
            y += 40f;
        }

        PortraitFrame.SectionRule(this, PortraitLayout.Pad, PortraitLayout.CanvasWidth - PortraitLayout.Pad, y, "战斗技能");
        y += 40f;
        return DrawSkillChart(who, y, view);
    }

    private static int LevelOf(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? 0 : int.Parse(digits);
    }

    private bool ExecuteCharacter(PortraitWidget w)
    {
        switch (w.Action)
        {
            case PortraitAction.RosterPick:
                OpenCharacter(w.Index);
                return true;
            case PortraitAction.CharacterSegment:
                _charSeg = w.Index;
                _pan.Remove("character");
                if (_charSeg == 2)
                    OpenSchedule();
                return true;
            case PortraitAction.SkillNode:
                _skillSelectedId = w.Label;
                return true;
            case PortraitAction.SkillGroup:
                if (!_skillGroupsOpen.Remove(w.Label))
                    _skillGroupsOpen.Add(w.Label);
                return true;
            case PortraitAction.TraitInfo:
                ShowTraitInfo(w.Index);
                return true;
            case PortraitAction.OpenPortraitPicker:
                _sheet = SheetKind.PortraitPicker;
                return true;
            case PortraitAction.PickPortraitDiff:
                Who.PortraitDiff = w.Index;
                _sheet = SheetKind.None;
                return true;
            default:
                return false;
        }
    }

    /// <summary>特质说明弹窗：标题＝特质名，正文＝Core 求出的具体数值影响，末行是分组与内容表基调句（纯展示，点任意处关闭）。</summary>
    private void ShowTraitInfo(int index)
    {
        var def = TraitDefsOf(Who).ElementAtOrDefault(index);
        if (def == null)
            return;
        var lines = new List<string>(Rimisekai.Character.Traits.EffectLines(def.Trait));
        if (lines.Count == 0)
            lines.Add("暂无数值影响");
        if (def.Keynote.Length > 0)
        {
            lines.Add("");
            lines.Add($"{def.Group}　{def.Keynote}");
        }
        ModalWanted!(new InkModalPage { Title = def.Name, Body = string.Join("\n", lines) });
    }

    private string EquipmentName(CharacterState who, EquipSlot slot, EquipRegistry registry)
    {
        var id = who.EquippedId(slot);
        if (!string.IsNullOrEmpty(id))
        {
            var gear = registry.Get(id);
            if (gear != null)
                return EquipForge.NameOf(gear);
            if (_vm.Hub.State.Weapons.Get(id) is { } weapon)
                return WeaponForge.NameOf(weapon);
        }
        if (slot == EquipSlot.MainHand)
            return who.MainWeapon.HasValue ? InkText.Weapon(who.MainWeapon.Value) : "空";
        if (slot == EquipSlot.OffHand)
        {
            if (who.OffHandShield) return "盾牌";
            if (who.OffWeapon.HasValue) return InkText.Weapon(who.OffWeapon.Value);
            return "空";
        }
        return "空";
    }

    private Texture2D? LoadCharacterPortrait(CharacterState? who)
    {
        if (who == null) return null;
        var direct = _vm.PortraitPath(who.Name);
        var tex = InkIllustration.LoadTexture(direct);
        if (tex != null) return tex;

        var nameFile = $"res://立绘_{who.Name}.png";
        tex = InkIllustration.LoadTexture(nameFile);
        if (tex != null) return tex;

        var key = PortraitAvatars.ResolveIdentityKey(who);
        var diff = who.PortraitDiff > 0 ? who.PortraitDiff : 1;
        var path = PortraitAvatars.PortraitPath(key, diff);
        tex = InkIllustration.LoadTexture(path);
        if (tex != null) return tex;
        return InkIllustration.LoadTexture(PortraitAvatars.PortraitPath(key, 1));
    }

    /// <summary>同身份立绘选择抽屉：展示当前角色同身份下的全部差分立绘，点选即刻换上。</summary>
    private float DrawPortraitPickerSheet()
    {
        var who = Who;
        var top = 760f;
        PortraitFrame.Sheet(this, top);
        var idKey = PortraitAvatars.ResolveIdentityKey(who);
        var idLabel = PortraitAvatars.ResolveIdentityLabel(idKey);

        InkDraw.TextBounded(this, new Rect2(PortraitLayout.Pad + 20f, top + PortraitLayout.SheetTitleOffset - 40f, 700f, 80f),
            $"立绘 · {idLabel}", PortraitLayout.FontTitle, PortraitLayout.FontBody, InkStyle.Line, "lm");

        var close = PortraitLayout.SheetClose(top);
        PortraitGlyph.Close(this, close.GetCenter().X, close.GetCenter().Y, 26f, InkStyle.Dim);
        _widgets.Add(new PortraitWidget(close, PortraitAction.SheetClose, 0, true, "收起"));

        var contentY = top + PortraitLayout.SheetContentOffset;
        var diffCount = PortraitAvatars.DiffCountFor(idKey);

        const int cols = 3;
        const float gap = 24f;
        var cardW = (PortraitLayout.FullWidth - (cols - 1) * gap) / cols;
        var cardH = 500f;

        var curDiff = who.PortraitDiff > 0 ? who.PortraitDiff : 1;

        for (var i = 0; i < diffCount; i++)
        {
            var diffIndex = i + 1;
            var col = i % cols;
            var row = i / cols;

            var itemsInRow = (row == diffCount / cols) ? (diffCount % cols == 0 ? cols : diffCount % cols) : cols;
            var rowStartX = (itemsInRow < cols)
                ? PortraitLayout.CanvasWidth / 2f - (itemsInRow * cardW + (itemsInRow - 1) * gap) / 2f
                : PortraitLayout.Pad;

            var r = new Rect2(rowStartX + col * (cardW + gap), contentY + row * (cardH + gap), cardW, cardH);
            var isCurrent = curDiff == diffIndex;

            PortraitFrame.Card(this, r, isCurrent, 16f);

            var tex = InkIllustration.LoadTexture(PortraitAvatars.PortraitPath(idKey, diffIndex));
            if (tex != null)
            {
                const float innerPad = 10f;
                var innerRect = new Rect2(r.Position.X + innerPad, r.Position.Y + innerPad,
                    r.Size.X - innerPad * 2f, r.Size.Y - innerPad * 2f - 40f);
                PortraitFrame.Cover(this, tex, innerRect, 0.02f);
                PortraitFrame.Fade(this, new Rect2(innerRect.Position.X, innerRect.End.Y - 70f, innerRect.Size.X, 70f), 0f, 0.75f);
            }
            else
            {
                var av = InkIllustration.LoadTexture(PortraitAvatars.AvatarPath(idKey, diffIndex));
                if (av != null)
                    PortraitFrame.Avatar(this, new Vector2(r.GetCenter().X, r.Position.Y + 180f), 90f, av, $"#{diffIndex}", ring: false);
            }

            var label = RomanNumber(diffIndex);
            InkDraw.Text(this, new Vector2(r.GetCenter().X, r.End.Y - 26f), label,
                PortraitLayout.FontBody, isCurrent ? InkStyle.Line : InkStyle.Dim, "cm");

            if (isCurrent)
            {
                PortraitGlyph.Diamond(this, r.End.X - 24f, r.Position.Y + 24f, 10f, InkStyle.Line);
            }

            _widgets.Add(new PortraitWidget(r, PortraitAction.PickPortraitDiff, diffIndex, true, $"差分{diffIndex}"));
        }

        return top;
    }

    private static string RomanNumber(int n) => n switch
    {
        1 => "I",
        2 => "II",
        3 => "III",
        4 => "IV",
        5 => "V",
        6 => "VI",
        _ => n.ToString(),
    };
}
