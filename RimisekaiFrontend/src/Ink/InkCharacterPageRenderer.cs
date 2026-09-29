using System;
using System.Collections.Generic;
using Godot;

namespace Rimisekai.Ink;

public static class InkCharacterPageRenderer
{
    private const int TitleFontSize = 26;
    private const int BodyFontSize = 22;
    private const int SecondaryFontSize = 20;
    private const int SmallFontSize = 18;

    public static void Draw(CanvasItem ci, InkHubModel model)
    {
        var page = model.Page;
        if (page == null)
            return;

        if (page.Rows.Count == 0 && !string.IsNullOrEmpty(page.EmptyHint))
        {
            InkDraw.Text(ci, InkLayout.CharacterBody.GetCenter(), page.EmptyHint,
                SecondaryFontSize, InkStyle.Dim, "cm");
            return;
        }

        DrawRail(ci, page);

        switch (page.Page)
        {
            case InkPage.Status:
                DrawStatus(ci, page);
                break;
            case InkPage.Skills:
                DrawSkills(ci, page);
                break;
            case InkPage.Schedule:
                DrawSchedule(ci, page);
                break;
        }
    }

    private static void DrawRail(CanvasItem ci, InkPageModel page)
    {
        var rail = InkLayout.CharacterRail;
        InkFrame.Panel(ci, rail, corner: 16f, rails: false);

        var idBox = InkLayout.CharacterIdentity;
        InkFrame.Card(ci, idBox, InkStyle.Line, false);

        var charName = string.IsNullOrEmpty(page.CharacterName) ? "（未知）" : page.CharacterName;
        var pageKind = page.Page switch
        {
            InkPage.Status => "状态",
            InkPage.Skills => "技能",
            InkPage.Schedule => "日程",
            _ => "角色",
        };

        InkDraw.TextBounded(ci, InkLayout.CharacterIdentityName,
            charName, BodyFontSize, SmallFontSize, InkStyle.Line, "lm");

        InkDraw.TextBounded(ci, InkLayout.CharacterIdentityKind,
            $"［{pageKind}］", SmallFontSize, 14, InkStyle.Dim, "lm");

        var portraitBox = InkLayout.CharacterPortrait;
        ci.DrawRect(portraitBox, InkStyle.Bg);

        var loaded = false;
        if (!string.IsNullOrEmpty(page.PortraitPath) && ResourceLoader.Exists(page.PortraitPath))
        {
            var tex = ResourceLoader.Load<Texture2D>(page.PortraitPath);
            if (tex != null)
            {
                var scale = Mathf.Min(portraitBox.Size.X / tex.GetWidth(), portraitBox.Size.Y / tex.GetHeight());
                var size = new Vector2(tex.GetWidth(), tex.GetHeight()) * scale;
                ci.DrawTextureRect(tex, new Rect2(portraitBox.GetCenter() - size / 2f, size), false);
                loaded = true;
            }
        }

        if (!loaded)
        {
            InkDraw.Figure(ci, portraitBox.GetCenter().X, portraitBox.End.Y - 24f, portraitBox.Size.Y * 0.70f,
                InkStyle.Line, 107);
            InkDraw.Text(ci, new Vector2(portraitBox.GetCenter().X, portraitBox.Position.Y + 24f),
                "（无立绘）", SmallFontSize, InkStyle.Dim, "cm");
        }

        InkFrame.CardOutline(ci, portraitBox, false);

        var captionBox = InkLayout.CharacterCaption;
        InkFrame.Card(ci, captionBox, InkStyle.Dim, false);

        var captionIndex = 0;
        for (var i = 0; i < page.Rows.Count && captionIndex < 2; i++)
        {
            var row = page.Rows[i];
            if (row.Name is "好感" or "心情")
            {
                var rowRect = InkLayout.CharacterCaptionRow(captionIndex);
                var rowLabel = InkLayout.StatusRowLabel(rowRect);
                var rowVal = InkLayout.StatusRowValue(rowRect);
                InkDraw.TextBounded(ci, rowLabel, row.Name, SmallFontSize, 14, InkStyle.Dim, "lm");
                var displayVal = row.Value;
                if (!string.IsNullOrEmpty(row.Note))
                    displayVal += $" {row.Note}";
                InkDraw.TextBounded(ci, rowVal, displayVal, SmallFontSize, 14, InkStyle.Line, "rm");
                captionIndex++;
            }
        }
    }

    private static void DrawStatus(CanvasItem ci, InkPageModel page)
    {
        var vitalsRect = InkLayout.StatusVitals;
        InkFrame.Panel(ci, vitalsRect, corner: 16f, rails: false);
        InkDraw.TextBounded(ci, InkLayout.PanelHeading(vitalsRect),
            "状态", TitleFontSize, SecondaryFontSize, InkStyle.Line, "lm");
        InkFrame.HeaderRule(ci, vitalsRect.Position.X + 20f, vitalsRect.End.X - 20f, vitalsRect.Position.Y + 54f);

        var combatRect = InkLayout.StatusCombat;
        InkFrame.Panel(ci, combatRect, corner: 16f, rails: false);
        InkDraw.TextBounded(ci, InkLayout.PanelHeading(combatRect),
            "攻击", TitleFontSize, SecondaryFontSize, InkStyle.Line, "lm");
        InkFrame.HeaderRule(ci, combatRect.Position.X + 20f, combatRect.End.X - 20f, combatRect.Position.Y + 54f);

        var relationsRect = InkLayout.StatusRelations;
        InkFrame.Panel(ci, relationsRect, corner: 16f, rails: false);
        InkDraw.TextBounded(ci, InkLayout.PanelHeading(relationsRect),
            "关系", TitleFontSize, SecondaryFontSize, InkStyle.Line, "lm");
        InkFrame.HeaderRule(ci, relationsRect.Position.X + 20f, relationsRect.End.X - 20f, relationsRect.Position.Y + 54f);

        var vitalIndex = 0;
        var combatIndex = 0;

        for (var i = 0; i < page.Rows.Count; i++)
        {
            var row = page.Rows[i];

            if (row.Name is "体力" or "气力" or "疲劳" or "好感" or "心情")
            {
                var r = InkLayout.SectionRow(vitalsRect, vitalIndex, step: 54f, height: 44f);
                DrawStatusRow(ci, r, row);
                vitalIndex++;
            }
            else if (row.Name is "攻击" or "血量" or "防御" or "闪避" or "法术" or "威胁")
            {
                if (combatIndex < 6)
                {
                    var cell = InkLayout.CombatStatCell(combatIndex);
                    DrawCombatStatCard(ci, cell, row);
                    combatIndex++;
                }
            }
            else if (row.Name == "关系")
            {
                var textBox = InkLayout.PanelText(relationsRect);
                var relText = string.IsNullOrEmpty(row.Value) ? "无" : row.Value;
                InkDraw.Wrapped(ci, textBox, relText, SecondaryFontSize, InkStyle.Line, 32f);
            }
        }
    }

    private static void DrawCombatStatCard(CanvasItem ci, Rect2 rect, InkPageRow row)
    {
        InkFrame.Card(ci, rect, InkStyle.Dim, false);
        var labelRect = new Rect2(rect.Position.X + 12f, rect.Position.Y + 8f, rect.Size.X - 24f, 22f);
        var valRect = new Rect2(rect.Position.X + 12f, rect.Position.Y + 30f, rect.Size.X - 24f, 26f);
        InkDraw.TextBounded(ci, labelRect, row.Name, SmallFontSize, 14, InkStyle.Dim, "lm");
        InkDraw.TextBounded(ci, valRect, row.Value, SecondaryFontSize, 16, InkStyle.Line, "lm");
    }

    private static void DrawStatusRow(CanvasItem ci, Rect2 rect, InkPageRow row)
    {
        var labelRect = InkLayout.StatusRowLabel(rect);
        var valRect = InkLayout.StatusRowValue(rect);

        InkDraw.TextBounded(ci, labelRect, row.Name, SecondaryFontSize, 14, InkStyle.Dim, "lm");

        if (row.MeterValue.HasValue && row.MeterMax > 0f)
        {
            var ratio = Mathf.Clamp(row.MeterValue.Value / row.MeterMax, 0f, 1f);
            var meterRect = InkLayout.StatusRowMeter(rect);
            InkDraw.Meter(ci, meterRect, ratio);

            InkDraw.TextBounded(ci, valRect, row.Value, SecondaryFontSize, 14, InkStyle.Line, "rm");
        }
        else
        {
            var valStr = row.Value;
            if (!string.IsNullOrEmpty(row.Note))
                valStr += $" ({row.Note})";

            InkDraw.TextBounded(ci, valRect, valStr, SecondaryFontSize, 14, InkStyle.Line, "rm");
        }

        InkFrame.RowRule(ci, rect);
    }

    private static void DrawSkills(CanvasItem ci, InkPageModel page)
    {
        var columns = new List<List<InkPageRow>> { new(), new(), new() };
        var colTitles = new[] { "生活", "武器", "流派" };
        var currentCol = -1;

        for (var i = 0; i < page.Rows.Count; i++)
        {
            var row = page.Rows[i];
            if (row.IsHeading)
            {
                if (row.Name == "生活") currentCol = 0;
                else if (row.Name == "武器") currentCol = 1;
                else if (row.Name == "流派") currentCol = 2;
                continue;
            }

            if (currentCol >= 0 && currentCol < 3)
                columns[currentCol].Add(row);
        }

        for (var c = 0; c < 3; c++)
        {
            var panel = InkLayout.SkillColumn(c);
            InkFrame.Panel(ci, panel, corner: 16f, rails: false);

            InkDraw.TextBounded(ci, InkLayout.PanelHeading(panel),
                colTitles[c], TitleFontSize, SecondaryFontSize, InkStyle.Line, "lm");
            InkFrame.HeaderRule(ci, panel.Position.X + 20f, panel.End.X - 20f, panel.Position.Y + 54f);

            var rows = columns[c];
            for (var r = 0; r < rows.Count; r++)
            {
                var rowRect = InkLayout.SectionRow(panel, r, step: 72f, height: 62f);
                if (rowRect.End.Y > panel.End.Y - 10f)
                    break;

                var item = rows[r];
                var nameRect = InkLayout.SkillRowName(rowRect);
                var valRect = InkLayout.SkillRowValue(rowRect);
                var noteRect = InkLayout.SkillRowNote(rowRect);

                InkDraw.TextBounded(ci, nameRect, item.Name, SecondaryFontSize, 14, InkStyle.Line, "lm");
                InkDraw.TextBounded(ci, valRect, item.Value, SecondaryFontSize, 14, InkStyle.Line, "rm");
                if (!string.IsNullOrEmpty(item.Note))
                    InkDraw.TextBounded(ci, noteRect, item.Note, SmallFontSize, 14, InkStyle.Dim, "lm");

                InkFrame.RowRule(ci, rowRect);
            }
        }
    }

    private static void DrawSchedule(CanvasItem ci, InkPageModel page)
    {
        var slotCount = Math.Min(4, page.Rows.Count);
        for (var s = 0; s < slotCount; s++)
        {
            var slotRect = InkLayout.ScheduleSlot(s);
            var row = page.Rows[s];
            var isSelected = s == page.SelectedRow;

            InkFrame.Card(ci, slotRect, isSelected ? InkStyle.Line : InkStyle.Dim, isSelected);

            var nameRect = InkLayout.ScheduleSlotName(slotRect);
            var valRect = InkLayout.ScheduleSlotValue(slotRect);
            var noteRect = InkLayout.ScheduleSlotNote(slotRect);

            InkDraw.TextBounded(ci, nameRect, row.Name, SecondaryFontSize, 14,
                isSelected ? InkStyle.Line : InkStyle.Dim, "lm");

            InkDraw.TextBounded(ci, valRect, row.Value, TitleFontSize, SecondaryFontSize,
                InkStyle.Line, "lm");

            if (!string.IsNullOrEmpty(row.Note))
            {
                InkDraw.TextBounded(ci, noteRect, row.Note, SmallFontSize, 14,
                    InkStyle.Dim, "lm");
            }
        }

        var detailRect = InkLayout.ScheduleDetail;
        InkFrame.Panel(ci, detailRect, corner: 18f, rails: false);

        var title = string.IsNullOrEmpty(page.DetailTitle) ? "日程" : page.DetailTitle;
        InkDraw.TextBounded(ci, InkLayout.PanelHeading(detailRect),
            title, TitleFontSize, SecondaryFontSize, InkStyle.Line, "lm");
        InkFrame.HeaderRule(ci, detailRect.Position.X + 24f, detailRect.End.X - 24f, detailRect.Position.Y + 54f);

        if (!string.IsNullOrEmpty(page.DetailNote))
        {
            InkDraw.Wrapped(ci, InkLayout.ScheduleNote, page.DetailNote, SecondaryFontSize, InkStyle.Dim, 32f);
        }

        var actionCount = page.DetailActions.Count;
        for (var a = 0; a < actionCount; a++)
        {
            var actionRow = page.DetailActions[a];
            var btnRect = InkLayout.CharacterDetailButton(a, actionCount);
            InkFrame.Button(ci, btnRect, actionRow.Name, actionRow.Selected, actionRow.Enabled, SecondaryFontSize, centered: true);
        }
    }
}
