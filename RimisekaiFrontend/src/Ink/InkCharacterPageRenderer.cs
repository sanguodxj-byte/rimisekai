using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Rimisekai.Ink;

public static class InkCharacterPageRenderer
{
    /// <summary>「状态」栏的行数（体力 / 气力 / 好感 / 心情）。</summary>
    private const int StatusVitalRows = 5;

    /// <summary>「属性」栏的行数（体质 / 灵巧 / 智力 / 魅力 / 感知 / 力量）。</summary>
    private const int StatusAttributeRows = 6;

    private const int TitleFontSize = 26;
    private const int BodyFontSize = 26;
    private const int SecondaryFontSize = 26;
    private const int SmallFontSize = 26;

    public static void Draw(CanvasItem ci, InkHubModel model)
    {
        var page = model.Page;
        if (page == null)
            return;

        if (page.Rows.Count == 0 && !string.IsNullOrEmpty(page.EmptyHint))
        {
            InkDraw.Text(ci, InkLayout.StatusCenter, page.EmptyHint,
                SecondaryFontSize, InkStyle.Dim, "cm");
            return;
        }

        // 日程页是全宽操作页（时段卡/房间网格都从左缘铺起），不画中列；
        // 技能页是一具铺满正文区的大星盘，同样不画中列（角色名落在盘心毂里）；
        // 状态页画中列（名牌＋居中立绘）。
        if (page.Page == InkPage.Status)
            DrawPortraitColumn(ci, page);

        switch (page.Page)
        {
            case InkPage.Status:
                DrawStatus(ci, page);
                break;
            case InkPage.Skills:
                if (page.Disc != null)
                    InkSkillDisc.Draw(ci, page, page.Disc);
                break;
            case InkPage.Schedule:
                DrawSchedule(ci, model);
                break;
        }

        // 日程页设施列表的滑条。
        if (page.Page == InkPage.Schedule)
            InkPageRenderer.DrawScrollbars(ci, model.Widgets);
    }

    /// <summary>
    /// 状态页中列：立绘以内容区中心为心上下左右居中，装在边框资产里；
    /// 角色名立在立绘正下方、水平居中。
    /// 好感/心情由左列「状态」栏报，这里不再另设一栏重复。
    /// </summary>
    private static void DrawPortraitColumn(CanvasItem ci, InkPageModel page)
    {
        var portraitBox = InkLayout.StatusPortrait;

        if (!string.IsNullOrEmpty(page.PortraitPath) && ResourceLoader.Exists(page.PortraitPath))
        {
            var tex = ResourceLoader.Load<Texture2D>(page.PortraitPath);
            if (tex != null)
            {
                var scale = Mathf.Min(portraitBox.Size.X / tex.GetWidth(), portraitBox.Size.Y / tex.GetHeight());
                var size = new Vector2(tex.GetWidth(), tex.GetHeight()) * scale;
                ci.DrawTextureRect(tex, new Rect2(portraitBox.GetCenter() - size / 2f, size), false);
            }
        }

        // 立绘装进边框资产（细双线 + 四角角花），不再是裸矩形。
        InkFrame.Card(ci, portraitBox, InkStyle.Line, false, InkStyle.Bg);

        var charName = string.IsNullOrEmpty(page.CharacterName) ? "（未知）" : page.CharacterName;
        InkDraw.TextBounded(ci, InkLayout.StatusName,
            charName, TitleFontSize, SecondaryFontSize, InkStyle.Line, "cm");
    }

    /// <summary>
    /// 状态页：左列「状态」+「攻击」，右列「属性」+「能力」，中列是上下左右居中的立绘。
    /// 属性每项一个正方形框，底部是经验进度条。
    /// 能力列收敛态下每段只报最高一项，展开后才逐行列全。
    /// </summary>
    private static void DrawStatus(CanvasItem ci, InkPageModel page)
    {
        var vitalsRect = InkLayout.StatusVitals;
        InkFrame.Panel(ci, vitalsRect, corner: 16f);

        var combatRect = InkLayout.StatusCombat;
        InkFrame.Panel(ci, combatRect, corner: 16f);

        var vitalIndex = 0;
        var combatIndex = 0;
        var attrIndex = 0;
        // 下排一列：按段头把能力行分进 生活 / 武器 / 流派 三段。
        var groups = new List<List<InkPageRow>> { new(), new(), new() };
        var group = -1;

        for (var i = 0; i < page.Rows.Count; i++)
        {
            var row = page.Rows[i];

            if (row.IsHeading)
            {
                if (row.Name == "生活") group = 0;
                else if (row.Name == "武器") group = 1;
                else if (row.Name == "流派") group = 2;
                continue;
            }

            if (row.Name is "体力" or "气力" or "好感" or "心情")
            {
                var r = InkLayout.StatusColumnRow(vitalsRect, vitalIndex, StatusVitalRows);
                DrawStatusRow(ci, r, row);
                vitalIndex++;
            }
            else if (row.Name is "攻击" or "血量" or "防御" or "闪避" or "法强" or "速度")
            {
                if (combatIndex < 6)
                {
                    var cell = InkLayout.CombatStatCell(combatIndex);
                    DrawCombatStatCard(ci, cell, row);
                    combatIndex++;
                }
            }
            else if (row.Name is "体质" or "灵巧" or "智力" or "魅力" or "感知" or "力量")
            {
                if (attrIndex < StatusAttributeRows)
                {
                    var cell = InkLayout.AttributeCell(attrIndex);
                    DrawAttributeCell(ci, cell, row);
                    attrIndex++;
                }
            }
            else if (group >= 0 && group < 3)
            {
                groups[group].Add(row);
            }
        }

        // 能力：每条一个独立方框，格式「类别　技能名　Lv.等级」。
        // 常显三条段头条（每段最高那一项）；点段头条把本级以下摊开成一条条方框。
        // 摊平后超出可见条数走滑条，不静默裁掉。
        var colTitles = new[] { "生活", "武器", "流派" };
        var bars = new List<(int Group, bool IsHead, string Text)>();
        for (var g = 0; g < 3; g++)
        {
            if (groups[g].Count == 0)
                continue;
            var top = groups[g].FirstOrDefault(r => r.IsTop) ?? groups[g][0];
            bars.Add((g, true, $"{colTitles[g]}　{top.Name}　Lv.{LeadingLevel(top.Value)}"));
            if (!page.AbilityOpen[g])
                continue;
            foreach (var r in groups[g])
            {
                if (r == top)
                    continue;
                bars.Add((g, false, $"{colTitles[g]}　{r.Name}　Lv.{LeadingLevel(r.Value)}"));
            }
        }

        var first = Mathf.Clamp(page.StatusAbilityFirst, 0,
            Mathf.Max(0, bars.Count - InkLayout.AbilityVisibleBars));
        var visible = InkLayout.AbilityVisibleBars;

        for (var i = first; i < bars.Count && i < first + visible; i++)
        {
            var bar = bars[i];
            var rect = InkLayout.AbilityBar(i - first);
            if (rect.End.Y > InkLayout.StatusAbilityArea.End.Y + 4f)
                break;

            // 段头条是实心亮框，摊开的下级条是次级色框——层级靠线重表达，不靠文字。
            InkFrame.Card(ci, rect, bar.IsHead ? InkStyle.Line : InkStyle.Dim, bar.IsHead);

            InkDraw.TextBounded(ci, InkLayout.AbilityBarGroup(rect),
                colTitles[bar.Group], SmallFontSize, 14, InkStyle.Dim, "lm");
            var words = bar.Text.Split('　');
            var skillName = words.Length > 1 ? words[1] : bar.Text;
            var level = words.Length > 2 ? words[2] : "";
            InkDraw.TextBounded(ci, InkLayout.AbilityBarName(rect),
                skillName, SecondaryFontSize, 14, InkStyle.Line, "lm");
            InkDraw.TextBounded(ci, InkLayout.AbilityBarLevel(rect),
                level, SecondaryFontSize, 14, InkStyle.Line, "rm");
        }

        if (bars.Count > visible)
            InkDraw.Scrollbar(ci, InkLayout.ScrollBarRect(InkLayout.StatusAbilityArea, InkLayout.StatusAbilityArea),
                first / (float)(bars.Count - visible),
                visible / (float)bars.Count);
    }

    /// <summary>从「Lv3」「12」这类串里取出等级数字；没有则 0。</summary>
    private static int LeadingLevel(string text)
    {
        var n = 0;
        foreach (var ch in text)
        {
            if (ch < '0' || ch > '9')
            {
                if (n > 0) break;
                continue;
            }
            n = n * 10 + (ch - '0');
        }
        return n;
    }

    /// <summary>属性方块：正方形框，名在上、值居中、底部是经验进度条。</summary>
    private static void DrawAttributeCell(CanvasItem ci, Rect2 rect, InkPageRow row)
    {
        InkFrame.Card(ci, rect, InkStyle.Dim, false);

        InkDraw.TextBounded(ci, InkLayout.AttributeCellName(rect),
            row.Name, SmallFontSize, 13, InkStyle.Dim, "cm");
        InkDraw.TextBounded(ci, InkLayout.AttributeCellValue(rect),
            row.Value, TitleFontSize, 16, InkStyle.Line, "cm");

        var bar = InkLayout.AttributeCellBar(rect);
        var ratio = row.MeterMax > 0f ? Mathf.Clamp((row.MeterValue ?? 0f) / row.MeterMax, 0f, 1f) : 0f;
        InkDynamicMeter.Draw(ci, $"attr_{row.Name}_{bar.GetHashCode()}", bar, ratio, isHp: false);
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
            var isHp = row.Name == "体力";
            InkDynamicMeter.Draw(ci, $"status_{row.Name}", meterRect, ratio, isHp: isHp);

            InkDraw.TextBounded(ci, valRect, row.Value, SecondaryFontSize, 14, InkStyle.Line, "rm");
        }
        else
        {
            var valStr = row.Value;
            if (!string.IsNullOrEmpty(row.Note))
                valStr += $" ({row.Note})";

            InkDraw.TextBounded(ci, valRect, valStr, SecondaryFontSize, 14, InkStyle.Line, "rm");
        }

        InkFrame.RowDivider(ci, rect);
    }

    /// <summary>
    /// 日程：上排四段安排，中间房间网格（选房间）＋该房间的设施列表（选设施），
    /// 下方详情栏放三档开关。点设施即把当前时段排到那件设施上。
    /// </summary>
    private static void DrawSchedule(CanvasItem ci, InkHubModel model)
    {
        var page = model.Page!;
        var slotCount = Math.Min(4, page.Rows.Count);
        for (var s = 0; s < slotCount; s++)
        {
            var slotRect = InkLayout.ScheduleSlot(s);
            var row = page.Rows[s];
            var isSelected = s == page.SelectedRow;

            InkFrame.Card(ci, slotRect, isSelected ? InkStyle.Line : InkStyle.Dim, isSelected);

            // 第一行：时间
            InkDraw.TextBounded(ci, InkLayout.ScheduleSlotName(slotRect), row.Name,
                SecondaryFontSize, 14, isSelected ? InkStyle.Line : InkStyle.Dim, "lm");

            var hasWork = row.Value == "工作";

            // 时段已排工作时，右上角显示取消按钮
            if (hasWork)
            {
                InkFrame.Button(ci, InkLayout.ScheduleSlotCancel(slotRect), "取消",
                    false, true, SmallFontSize, centered: true);
            }

            // 第二行：工作（左）   房间-设施-产出（右，右对齐）
            InkDraw.TextBounded(ci, InkLayout.ScheduleSlotWork(slotRect), row.Value,
                SecondaryFontSize, 14, hasWork ? InkStyle.Line : InkStyle.Dim, "lm");

            if (hasWork && !string.IsNullOrEmpty(row.Note))
            {
                InkDraw.TextBounded(ci, InkLayout.ScheduleSlotChain(slotRect), row.Note,
                    SmallFontSize, 12, InkStyle.Dim, "rm");
            }
        }

        var work = page.Work;
        if (work != null)
        {
            DrawMemberList(ci, work);
            DrawRoomGrid(ci, work);
            DrawFacilityList(ci, work);
        }

        // 下方详情栏：仅展示当前设施的可选产出，禁止出现其他内容。
        var detailRect = InkLayout.WorkDetail;
        InkDraw.FoldedBox(ci, detailRect, InkStyle.Line, fold: 18f, fill: true, seed: 8910);

        DrawOutputs(ci, work);
    }

    /// <summary>
    /// 详情栏里的可选产出：一排四条、往下折行。
    /// 只画成品本体与材料两段，不写任何说明性文字。
    /// 仅有该设施可选产出，无可选产出时留空，禁止出现其他内容。
    /// </summary>
    private static void DrawOutputs(CanvasItem ci, InkWorkModel? work)
    {
        var outputs = work?.Outputs;
        if (outputs == null || outputs.Count == 0)
            return;

        const int perRow = 4;
        var top = InkLayout.WorkDetail.Position.Y + InkLayout.Pad;
        var bottom = InkLayout.WorkDetail.End.Y - 24f;
        var visibleRows = Mathf.Max(1, (int)((bottom - top) / 56f));
        for (var i = 0; i < outputs.Count; i++)
        {
            if (i / perRow >= visibleRows)
                break;
            var row = outputs[i];
            var rect = InkLayout.WorkOutputRow(i, perRow);
            InkFrame.Card(ci, rect, InkStyle.Dim, false);

            // 只有成品本体与材料两段，不写任何说明性文字。
            if (row.Value.Length == 0)
            {
                // 直接采集物：只有品名，居中。
                InkDraw.TextBounded(ci, rect, row.Name,
                    SecondaryFontSize, 14, InkStyle.Line, "cm");
            }
            else
            {
                InkDraw.TextBounded(ci, new Rect2(rect.Position.X + 12f, rect.Position.Y + 6f,
                        rect.Size.X - 24f, 22f),
                    row.Name, SecondaryFontSize, 14, InkStyle.Line, "lm");
                InkDraw.TextBounded(ci, new Rect2(rect.Position.X + 12f, rect.Position.Y + 26f,
                        rect.Size.X - 24f, 18f),
                    row.Value, 14, 12, InkStyle.Dim, "lm");
            }
        }
    }

    /// <summary>日程页左侧成员列表：包含玩家与所有同伴，点一行切换正在排班的人（无标题，行从顶部铺起）。</summary>
    private static void DrawMemberList(CanvasItem ci, InkWorkModel work)
    {
        var area = InkLayout.ScheduleMemberList;
        InkFrame.Panel(ci, area, corner: 16f);

        var members = work.Members;
        if (members.Count == 0)
            return;

        var visible = InkLayout.ScheduleMemberVisibleRows;
        var first = Mathf.Clamp(work.MemberFirst, 0, Mathf.Max(0, members.Count - visible));
        for (var i = 0; i < visible && first + i < members.Count; i++)
        {
            var member = members[first + i];
            var rect = InkLayout.ScheduleMemberRow(area, i);
            if (rect.End.Y > area.End.Y - 12f)
                break;

            var isSelected = member.TargetNumber == work.SelectedMemberId;
            InkFrame.Card(ci, rect, isSelected ? InkStyle.Line : InkStyle.Dim, isSelected,
                isSelected ? InkStyle.Bg.Lightened(0.08f) : null);

            InkDraw.TextBounded(ci, InkLayout.ScheduleMemberName(rect),
                member.Name, SecondaryFontSize, 14, isSelected ? InkStyle.Line : InkStyle.Dim, "lm");

            // 工种右对齐。
            if (member.Value.Length > 0)
                InkDraw.TextBounded(ci, InkLayout.ScheduleMemberJob(rect),
                    member.Value, SmallFontSize, 13, isSelected ? InkStyle.Line : InkStyle.Dim, "rm");
        }
    }

    /// <summary>房间网格：已开拓的房间按坐标落位，选中的加亮（移除房间标题）。</summary>
    private static void DrawRoomGrid(CanvasItem ci, InkWorkModel work)
    {
        var area = InkLayout.ScheduleGrid;
        InkFrame.Panel(ci, area, corner: 16f);

        var occupied = new HashSet<int>();
        foreach (var room in work.Rooms)
            occupied.Add(InkLayout.CellIndex(room.X, room.Y));

        for (var x = 0; x < InkLayout.GridCols; x++)
        {
            for (var y = 0; y < InkLayout.GridRows; y++)
            {
                var rect = InkLayout.ScheduleCell(area, x, y);
                if (!occupied.Contains(InkLayout.CellIndex(x, y)))
                {
                    // 没有房间的格子：虚线空框。
                    InkDraw.Dashed(ci, rect.Position, new Vector2(rect.End.X, rect.Position.Y), InkStyle.Dim);
                    InkDraw.Dashed(ci, new Vector2(rect.End.X, rect.Position.Y), rect.End, InkStyle.Dim);
                    InkDraw.Dashed(ci, rect.End, new Vector2(rect.Position.X, rect.End.Y), InkStyle.Dim);
                    InkDraw.Dashed(ci, new Vector2(rect.Position.X, rect.End.Y), rect.Position, InkStyle.Dim);
                    continue;
                }

                var room = FindRoom(work, x, y);
                if (room == null)
                    continue;
                if (room.Selected)
                    ci.DrawRect(rect, InkStyle.Hover);

                InkDraw.Ink(ci, new[]
                {
                    rect.Position,
                    new Vector2(rect.End.X, rect.Position.Y),
                    rect.End,
                    new Vector2(rect.Position.X, rect.End.Y),
                    rect.Position,
                }, InkStyle.Line, room.Selected ? 1.8f : 1.3f, 0.4f, 8300 + room.Id);

                InkDraw.TextFitted(ci, rect.GetCenter(), room.Name,
                    rect.Size.X - 20f, 20, 13, InkStyle.Line, "cm");
            }
        }
    }

    private static InkDevRoomCell? FindRoom(InkWorkModel work, int x, int y)
    {
        foreach (var room in work.Rooms)
        {
            if (room.X == x && room.Y == y)
                return room;
        }
        return null;
    }

    /// <summary>选中房间的设施列表：点一行把当前时段排到它上面（移除庭院/设施标题）。</summary>
    private static void DrawFacilityList(CanvasItem ci, InkWorkModel work)
    {
        var area = InkLayout.ScheduleFacilities;
        InkFrame.Panel(ci, area, corner: 16f);

        if (work.Facilities.Count == 0)
        {
            InkDraw.TextBounded(ci, area.Grow(-InkLayout.Pad),
                work.RoomId < 0 ? "未选中房间" : "没有可用的设施",
                SecondaryFontSize, 14, InkStyle.Dim, "cm");
            return;
        }

        var visible = InkLayout.ScheduleFacilityVisibleRows;
        var first = Mathf.Clamp(work.FacilityFirst, 0, Mathf.Max(0, work.Facilities.Count - visible));
        for (var i = 0; i < visible && first + i < work.Facilities.Count; i++)
        {
            var row = work.Facilities[first + i];
            var rect = InkLayout.ScheduleFacilityRow(area, i);
            if (rect.End.Y > area.End.Y - 12f)
                break;

            var selected = row.TargetNumber == work.AssignedFacilityId;
            InkFrame.Button(ci, rect, row.Name, selected, row.Enabled, SecondaryFontSize);
            if (row.Value.Length > 0)
                InkDraw.TextBounded(ci, new Rect2(rect.End.X - 90f, rect.Position.Y, 74f, rect.Size.Y),
                    row.Value, SmallFontSize, 14, InkStyle.Line, "rm");
        }
    }
}
