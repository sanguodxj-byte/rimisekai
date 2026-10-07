using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Character;
using Rimisekai.Defs;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

public partial class PortraitHubScreen
{
    private bool _statusAbilityOpen;
    private int _statusAbilityFirst;
    private int _statusAbilityTotal;

    private static readonly EquipSlot[] DisplayEquipSlots =
    {
        EquipSlot.MainHand,
        EquipSlot.OffHand,
        EquipSlot.Head,
        EquipSlot.Torso,
        EquipSlot.Legs,
        EquipSlot.Hands,
        EquipSlot.Feet,
        EquipSlot.Neck,
        EquipSlot.Ring1,
        EquipSlot.Ring2,
    };

    private InkPageModel StatusModel() => InkCharacterPageBuilder.Build(_vm, InkPage.Status,
        _vm.ChatPartner() ?? _vm.Hub.State.Roster.Master,
        abilityOpen: new[] { _statusAbilityOpen, _statusAbilityOpen, _statusAbilityOpen },
        abilityFirst: _statusAbilityFirst)!;

    private void DrawStatusPage()
    {
        var page = StatusModel();
        DrawPageTop(page.Title);
        DrawBack();
        var character = _vm.ChatPartner() ?? _vm.Hub.State.Roster.Master;

        var vitals = new List<InkPageRow>();
        var combat = new List<InkPageRow>();
        var attributes = new List<InkPageRow>();
        var groups = new List<List<InkPageRow>> { new(), new(), new() };
        var group = -1;
        foreach (var row in page.Rows)
        {
            if (row.IsHeading)
            {
                group = row.Name switch { "生活" => 0, "武器" => 1, "流派" => 2, _ => -1 };
                continue;
            }
            if (row.Name is "体力" or "气力" or "好感" or "心情") vitals.Add(row);
            else if (row.Name is "攻击" or "血量" or "防御" or "闪避" or "法强" or "速度") combat.Add(row);
            else if (row.Name is "体质" or "灵巧" or "智力" or "魅力" or "感知" or "力量") attributes.Add(row);
            else if (group >= 0) groups[group].Add(row);
        }

        DrawPortraitCard(character);
        DrawEquipmentRack(character);
        DrawVitalsSheet(vitals);
        DrawCombatSheet(combat);
        DrawAttributesSheet(attributes);
        DrawAbilitiesSection(groups);
    }

    private void DrawPortraitCard(CharacterState? character)
    {
        var rect = PortraitLayout.StatusPortraitCard;
        // 下沿是名牌带，只长上角两只角花。
        PortraitFrame.Panel(this, rect, InkStyle.Bg, flourish: 0f);
        PortraitFrame.TopFlourishes(this, rect, PortraitLayout.Flourish);

        var tex = LoadCharacterPortrait(character);
        if (tex != null)
        {
            var inner = rect.Grow(-16f);
            var availH = rect.Size.Y - 112f;
            var scale = Mathf.Min(inner.Size.X / tex.GetWidth(), availH / tex.GetHeight());
            var size = new Vector2(tex.GetWidth(), tex.GetHeight()) * scale;
            var imgRect = new Rect2(new Vector2(inner.GetCenter().X - size.X / 2f, inner.Position.Y + 8f), size);
            DrawTextureRect(tex, imgRect, false);
        }

        // 底部名牌区：上界渐隐线，下距内框线留足呼吸，文字在中央完全居中，绝不压边框
        var ruleY = rect.End.Y - 84f;
        PortraitFrame.FadingRule(this, rect.Position.X + 24f, rect.End.X - 24f, ruleY);

        var charName = character?.Name ?? "（未知）";
        var idLabel = character?.IsMaster == true ? "领主" : "同伴";
        var textCenterY = rect.End.Y - 48f;
        InkDraw.Text(this, new Vector2(rect.Position.X + 28f, textCenterY), charName,
            PortraitLayout.FontBody, InkStyle.Line, "lm");
        InkDraw.Text(this, new Vector2(rect.End.X - 28f, textCenterY), idLabel,
            PortraitLayout.FontMeta, InkStyle.Dim, "rm");
    }

    /// <summary>小节标题：一行字＋一条渐隐线。标题带固定 60 高，行距据此往下排。</summary>
    private void DrawSectionHeader(float x, float y, float width, string title)
    {
        InkDraw.Text(this, new Vector2(x + 4f, y + 24f), title, PortraitLayout.FontMeta, InkStyle.Line, "lm");
        PortraitFrame.FadingRule(this, x + 4f, x + width - 4f, y + 56f);
    }

    /// <summary>装备栏：双列五行，每槽上下分排「暗槽名」与「亮装备名」，不画厚重白框。</summary>
    private void DrawEquipmentRack(CharacterState? character)
    {
        var sidebar = PortraitLayout.StatusSidebar;
        DrawSectionHeader(sidebar.Position.X, sidebar.Position.Y, sidebar.Size.X, "装备");
        var registry = _vm.Hub.State.Equips;
        for (var i = 0; i < DisplayEquipSlots.Length; i++)
        {
            var slot = DisplayEquipSlots[i];
            var rect = PortraitLayout.EquipSlotCell(i);
            var equipName = character != null ? EquipmentName(character, slot, registry) : "空";
            var empty = equipName == "空";

            // 槽位名称（暗小字，靠左上）
            InkDraw.Text(this, new Vector2(rect.Position.X + 4f, rect.Position.Y + 22f),
                EquipSlots.Label(slot), 38, InkStyle.Dim, "lm");

            // 装备名称（亮大字，靠左下；空则显示暗「空」）
            InkDraw.TextBounded(this, new Rect2(rect.Position.X + 4f, rect.Position.Y + 50f, rect.Size.X - 8f, 48f),
                equipName, 46, 34, empty ? InkStyle.Dim : InkStyle.Line, "lm");

            // 槽底极细渐隐线做横向分隔
            PortraitFrame.FadingRule(this, rect.Position.X, rect.End.X, rect.Position.Y + 108f);
        }
    }

    private void DrawVitalsSheet(IReadOnlyList<InkPageRow> vitals)
    {
        var area = PortraitLayout.StatusVitals;
        DrawSectionHeader(area.Position.X, area.Position.Y, area.Size.X, "状态");
        for (var i = 0; i < vitals.Count && i < 4; i++)
        {
            var row = vitals[i];
            var line = PortraitLayout.StatusVitalCell(i);
            var centerY = line.Position.Y + line.Size.Y / 2f;

            // 标签贴左（暗色，FontMeta 44px）
            InkDraw.Text(this, new Vector2(line.Position.X + 4f, centerY - 4f),
                row.Name, PortraitLayout.FontMeta, InkStyle.Dim, "lm");

            // 数值贴右（亮色，FontBody 50px）
            InkDraw.Text(this, new Vector2(line.End.X - 4f, centerY - 4f),
                row.Value, PortraitLayout.FontBody, InkStyle.Line, "rm");

            // 体力/气力/心情进度量表线（0.45mm 高的槽线，低于此在手机上只是一条灰影）
            if (row.MeterValue.HasValue && row.MeterMax > 0f)
                InkDynamicMeter.Draw(this, $"portrait_status_{row.Name}",
                    new Rect2(line.Position.X + 4f, line.Position.Y + 66f, line.Size.X - 8f, 5f),
                    Mathf.Clamp(row.MeterValue.Value / row.MeterMax, 0f, 1f), row.Name == "体力");

            PortraitFrame.FadingRule(this, line.Position.X + 4f, line.End.X - 4f, line.Position.Y + 76f);
        }
    }

    private void DrawCombatSheet(IReadOnlyList<InkPageRow> combat)
    {
        var area = PortraitLayout.StatusCombat;
        DrawSectionHeader(area.Position.X, area.Position.Y, area.Size.X, "战斗");
        for (var i = 0; i < combat.Count && i < 6; i++)
        {
            var row = combat[i];
            var line = PortraitLayout.StatusMetricCell(area, i);
            var centerY = line.Position.Y + line.Size.Y / 2f;

            // 标签贴左
            InkDraw.Text(this, new Vector2(line.Position.X + 4f, centerY),
                row.Name, PortraitLayout.FontMeta, InkStyle.Dim, "lm");

            // 数值贴右
            InkDraw.Text(this, new Vector2(line.End.X - 4f, centerY),
                row.Value, PortraitLayout.FontBody, InkStyle.Line, "rm");

            PortraitFrame.FadingRule(this, line.Position.X + 4f, line.End.X - 4f, line.Position.Y + 76f);
        }
    }

    private void DrawAttributesSheet(IReadOnlyList<InkPageRow> attributes)
    {
        var area = PortraitLayout.StatusAttributes;
        DrawSectionHeader(area.Position.X, area.Position.Y, area.Size.X, "属性");
        for (var i = 0; i < attributes.Count && i < 6; i++)
        {
            var row = attributes[i];
            var line = PortraitLayout.StatusMetricCell(area, i);
            var centerY = line.Position.Y + line.Size.Y / 2f;

            // 标签贴左
            InkDraw.Text(this, new Vector2(line.Position.X + 4f, centerY - 4f),
                row.Name, PortraitLayout.FontMeta, InkStyle.Dim, "lm");

            // 数值贴右
            InkDraw.Text(this, new Vector2(line.End.X - 4f, centerY - 4f),
                row.Value, PortraitLayout.FontBody, InkStyle.Line, "rm");

            // 属性升级经验细线
            var ratio = row.MeterMax > 0f ? Mathf.Clamp((row.MeterValue ?? 0f) / row.MeterMax, 0f, 1f) : 0f;
            InkDynamicMeter.Draw(this, $"portrait_attribute_{row.Name}",
                new Rect2(line.Position.X + 4f, line.Position.Y + 66f, line.Size.X - 8f, 5f), ratio, false);

            PortraitFrame.FadingRule(this, line.Position.X + 4f, line.End.X - 4f, line.Position.Y + 76f);
        }
    }

    private void DrawAbilitiesSection(IReadOnlyList<List<InkPageRow>> groups)
    {
        var area = PortraitLayout.StatusAbilities;
        DrawSectionHeader(area.Position.X, area.Position.Y, area.Size.X, "能力");
        var rows = new List<(int Group, InkPageRow Row, bool Heading)>();
        var labels = new[] { "生活", "武器", "流派" };
        for (var g = 0; g < groups.Count; g++)
        {
            if (groups[g].Count == 0) continue;
            rows.Add((g, groups[g][0], true));
            if (_statusAbilityOpen)
                for (var i = 1; i < groups[g].Count; i++)
                    rows.Add((g, groups[g][i], false));
        }
        _statusAbilityTotal = rows.Count;
        var visible = PortraitLayout.StatusAbilityVisibleRows;
        _statusAbilityFirst = Math.Clamp(_statusAbilityFirst, 0, Math.Max(0, rows.Count - visible));
        var hasScroll = rows.Count > visible;
        for (var i = 0; i < visible && i + _statusAbilityFirst < rows.Count; i++)
        {
            var entry = rows[i + _statusAbilityFirst];
            var rect = PortraitLayout.StatusAbilityRow(i, hasScroll);
            var isHighlight = _statusAbilityOpen && entry.Heading;
            PortraitFrame.SubtleButton(this, rect, isHighlight);
            InkDraw.TextBounded(this, new Rect2(rect.Position.X + 24f, rect.Position.Y,
                180f, rect.Size.Y), labels[entry.Group], PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "lm");
            InkDraw.TextBounded(this, new Rect2(rect.Position.X + 220f, rect.Position.Y,
                rect.Size.X - 380f, rect.Size.Y), entry.Row.Name, PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "lm");
            InkDraw.TextBounded(this, new Rect2(rect.End.X - 160f, rect.Position.Y,
                136f, rect.Size.Y), entry.Row.Value, PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Line, "rm");
            _widgets.Add(new PortraitWidget(rect, PortraitAction.StatusAbilityToggle, entry.Group, true, labels[entry.Group]));
        }
        if (hasScroll)
        {
            var track = PortraitLayout.StatusAbilityScroll;
            InkDraw.InkLine(this, new Vector2(track.GetCenter().X, track.Position.Y),
                new Vector2(track.GetCenter().X, track.End.Y), InkStyle.Dim, PortraitLayout.LineHair);
            DrawRect(PortraitLayout.ListThumb(track, rows.Count,
                visible, _statusAbilityFirst), InkStyle.Line);
            RegisterScroll("status_abilities", area, rows.Count, visible, _statusAbilityFirst,
                first => _statusAbilityFirst = first, PortraitLayout.StatusAbilityRowHeight, track);
        }
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

    private bool ExecuteStatus(PortraitWidget widget)
    {
        if (widget.Action != PortraitAction.StatusAbilityToggle)
            return false;
        _statusAbilityOpen = !_statusAbilityOpen;
        _statusAbilityFirst = 0;
        QueueRedraw();
        return true;
    }

    private IReadOnlyList<PortraitRegion> StatusRegions() => new[]
    {
        new PortraitRegion("back", PortraitLayout.OverlayBack),
        new PortraitRegion("status_portrait", PortraitLayout.StatusPortraitCard),
        new PortraitRegion("status_sidebar", PortraitLayout.StatusSidebar),
        new PortraitRegion("status_vitals", PortraitLayout.StatusVitals),
        new PortraitRegion("status_combat", PortraitLayout.StatusCombat),
        new PortraitRegion("status_attributes", PortraitLayout.StatusAttributes),
        new PortraitRegion("status_abilities", PortraitLayout.StatusAbilities),
    };
}
