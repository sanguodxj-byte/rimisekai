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
            PortraitFrame.RoundRect(this, pill, 33f, null, InkStyle.Dim, 3f);
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
            PortraitFrame.Avatar(this, head.GetCenter(), 200f, PortraitAvatars.Resolve(who), who.Name);

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

        // 技能段的顶栏右侧给「星盘」入口（完整的战斗技能星盘与解锁门槛详情）。
        DrawPageTop(who.Name, $"{RoleOf(who)} · Lv {who.Level}", _charSeg == 1 ? "星盘" : "",
            _charSeg == 1 ? PortraitAction.OpenDisc : PortraitAction.Back);
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
            float x = PortraitLayout.Pad;
            for (var i = 0; i < traits.Count; i++)
            {
                var trait = traits[i];
                var w = Mathf.Max(PortraitLayout.TouchMin, PortraitFrame.ChipWidth(trait) - 16f);
                if (x + w > PortraitLayout.CanvasWidth - PortraitLayout.Pad)
                {
                    x = PortraitLayout.Pad;
                    y += 128f;
                }
                var r = new Rect2(x, y + 40f - PortraitLayout.TouchMin / 2f, w, PortraitLayout.TouchMin);
                PortraitFrame.Chip(this, r, trait, false, 80f);
                AddClipped(r, view, PortraitAction.TraitInfo, i, true, trait);
                x += w + 20f;
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
            PortraitFrame.RoundRect(this, r, 20f, null, InkStyle.WoodDark, 3f);
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

    /// <summary>
    /// 技能：生活 / 武器 / 流派三段菱形刻度行（每段的熟练等级），再是战斗技能卡（已解锁亮、未解锁暗＋锁）。
    /// 战斗技能卡点开即进技能星盘并选中该式；顶栏右侧「星盘」直接进星盘。
    /// </summary>
    private float DrawSkillsSegment(CharacterState who, float y, Rect2 view)
    {
        var page = InkCharacterPageBuilder.Build(_vm, InkPage.Status, who)!;
        var group = "";
        foreach (var row in page.Rows)
        {
            if (row.IsHeading)
            {
                group = row.Name;
                PortraitFrame.SectionRule(this, PortraitLayout.Pad, PortraitLayout.CanvasWidth - PortraitLayout.Pad, y, group);
                y += 60f;
                continue;
            }
            if (group.Length == 0)
                continue;
            var level = LevelOf(row.Value);
            InkDraw.TextBounded(this, new Rect2(PortraitLayout.Pad + 20f, y, 230f, 100f), row.Name, PortraitLayout.FontBody,
                PortraitLayout.FontMeta, InkStyle.Line, "lm");
            PortraitFrame.Ticks(this, 280f, y + 50f, 10, Mathf.Min(level, 10), 30f, 22f);
            InkDraw.Text(this, new Vector2(PortraitLayout.CanvasWidth - PortraitLayout.Pad - 10f, y + 50f),
                row.Value.StartsWith("Lv") ? row.Value : $"Lv{row.Value}", PortraitLayout.FontMeta, InkStyle.Dim, "rm");
            InkDraw.InkLine(this, new Vector2(PortraitLayout.Pad, y + 104f),
                new Vector2(PortraitLayout.CanvasWidth - PortraitLayout.Pad, y + 104f), InkStyle.Hover, 2f);
            y += 110f;
        }
        y += 30f;

        var disc = BuildSkillPage().Disc!;
        var tiles = disc.Tiles.Where(t => t.Kind == InkSkillNodeKind.Skill && t.Id.Length > 0)
            .OrderByDescending(t => t.Unlocked).ThenBy(t => t.Sector).ToArray();
        if (tiles.Length > 0)
        {
            PortraitFrame.SectionRule(this, PortraitLayout.Pad, PortraitLayout.CanvasWidth - PortraitLayout.Pad, y, "战斗技能");
            y += 50f;
            var cw = (PortraitLayout.FullWidth - 20f) / 2f;
            for (var i = 0; i < tiles.Length; i++)
            {
                var tile = tiles[i];
                var r = new Rect2(PortraitLayout.Pad + i % 2 * (cw + 20f), y + i / 2 * 196f, cw, 176f);
                PortraitFrame.Card(this, r, tile.Id == _skillSelectedId);
                if (tile.Unlocked)
                    PortraitGlyph.Swords(this, r.Position.X + 70f, r.GetCenter().Y, 30f, InkStyle.Line);
                else
                    PortraitGlyph.Lock(this, r.Position.X + 70f, r.GetCenter().Y, 30f, InkStyle.WoodDark);
                InkDraw.TextBounded(this, new Rect2(r.Position.X + 124f, r.Position.Y + 30f, r.Size.X - 150f, 64f), tile.Name,
                    PortraitLayout.FontBody, PortraitLayout.FontMeta, tile.Unlocked ? InkStyle.Line : InkStyle.Dim, "lm");
                var sector = tile.Sector < disc.SectorLabels.Count ? disc.SectorLabels[tile.Sector] : "";
                InkDraw.TextBounded(this, new Rect2(r.Position.X + 124f, r.Position.Y + 100f, r.Size.X - 150f, 52f), sector,
                    PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "lm");
                AddClipped(r, view, PortraitAction.SkillCard, i, true, tile.Id);
            }
            y += (tiles.Length + 1) / 2 * 196f + 20f;
        }

        return y;
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
            case PortraitAction.SkillCard:
                _skillSelectedId = w.Label;
                _push = PushPage.Disc;
                return true;
            case PortraitAction.OpenDisc:
                _push = PushPage.Disc;
                return true;
            case PortraitAction.TraitInfo:
                ShowTraitInfo(w.Index);
                return true;
            case PortraitAction.EquipInfo:
                ShowEquipInfo(w.Index);
                return true;
            default:
                return false;
        }
    }

    /// <summary>特质说明弹窗：标题＝特质名，正文＝光谱分组＋内容表里的基调句（纯展示，点任意处关闭）。</summary>
    private void ShowTraitInfo(int index)
    {
        var def = TraitDefsOf(Who).ElementAtOrDefault(index);
        if (def == null)
            return;
        var lines = new List<string>();
        if (def.Group.Length > 0)
            lines.Add(def.Group);
        if (def.Keynote.Length > 0)
            lines.Add(def.Keynote);
        ModalWanted!(new InkModalPage { Title = def.Name, Body = string.Join("\n", lines) });
    }

    /// <summary>装备详情弹窗：有实例装备列 Core 的逐行详情；武器槽列武器类型；空槽写未装备（纯展示）。</summary>
    private void ShowEquipInfo(int index)
    {
        if (index < 0 || index >= DisplayEquipSlots.Length)
            return;
        var who = Who;
        var slot = DisplayEquipSlots[index];
        var registry = _vm.Hub.State.Equips;
        var id = who.EquippedId(slot);
        var gear = string.IsNullOrEmpty(id) ? null : registry.Get(id);
        var name = EquipmentName(who, slot, registry);
        string body;
        if (gear != null)
            body = string.Join("\n", gear.DescribeDetails());
        else if (name != "空")
            body = $"槽位　{EquipSlots.Label(slot)}";
        else
            body = "未装备";
        ModalWanted!(new InkModalPage { Title = name == "空" ? EquipSlots.Label(slot) : name, Body = body });
    }

    private static string EquipmentName(CharacterState who, EquipSlot slot, EquipRegistry registry)
    {
        var id = who.EquippedId(slot);
        if (!string.IsNullOrEmpty(id))
        {
            var gear = registry.Get(id);
            if (gear != null)
                return EquipForge.NameOf(gear);
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

        if (who.Name == "璐米埃尔")
            return InkIllustration.LoadTexture("res://assets/portraits/identity/maid_diff1.png");
        if (who.Name == "瑞雅莉")
            return InkIllustration.LoadTexture("res://assets/portraits/identity/knight_diff1.png");
        if (who.Name == "瑞茵")
            return InkIllustration.LoadTexture("res://assets/portraits/identity/scholar_diff1.png");
        if (who.IsMaster || who.Name == "你")
            return InkIllustration.LoadTexture("res://assets/portraits/special/portrait_human_paladin.png")
                   ?? InkIllustration.LoadTexture("res://assets/portraits/identity/warrior_diff1.png");

        return null;
    }
}
