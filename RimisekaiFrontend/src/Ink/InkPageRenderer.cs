using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 子页面：库存 / 交易 / 制作全屏铺开，左列表右详情；
/// 开发全屏四区域——左上房间网格（非空房间右上角白 X 拆除房间）、
/// 右上房间详情、左下选中房间的设施列表、右下设施详情与建造/拆除。
/// </summary>
public static class InkPageRenderer
{
    private const int RowFont = 22;
    private const int NoteFont = 18;
    private const int DetailFont = 20;

    public static void Draw(CanvasItem ci, InkHubModel model)
    {
        var page = model.Page;
        if (page == null)
            return;

        InkFrame.PageShell(ci, page.Title,
            wood: page.Page is InkPage.Stock or InkPage.Trade or InkPage.Craft,
            subtitle: model.HeaderRight);
        DrawTabs(ci, page.Page);

        if (InkChatPages.Contains(page.Page))
        {
            InkCharacterPageRenderer.Draw(ci, model);
        }
        else if (page.Work != null)
        {
            InkWorkPageRenderer.Draw(ci, model);
        }
        else if (page.Rows.Count == 0)
        {
            InkDraw.TextBounded(ci, InkLayout.SubpageBody, page.EmptyHint,
                20, 14, InkStyle.Dim, "cm");
        }
        else
        {
            InkFrame.Panel(ci, InkLayout.FullListPanel, corner: 16f, rails: false);
            if (page.HasControls)
                DrawControls(ci, InkLayout.FullListArea, page);

            var visible = InkLayout.ListVisibleRows(InkLayout.FullListArea);
            var first = page.ListFirst;
            var shown = System.Math.Min(page.Rows.Count - first, visible);
            for (var i = 0; i < shown; i++)
                DrawListRow(ci, InkLayout.ListRow(InkLayout.FullListArea, i),
                    page.Rows[first + i], first + i == page.SelectedRow);

            InkFrame.PageNavigation(ci, InkLayout.FullListArea, first, visible, page.Rows.Count);
            DrawDetailPane(ci, InkLayout.FullDetailArea, page);
        }

        DrawFooter(ci, model);
    }

    /// <summary>页签带：管理三页与角色三页同一条，当前页高亮。</summary>
    public static void DrawTabs(CanvasItem ci, InkPage current)
    {
        var tabs = InkPageTabs.All;
        for (var i = 0; i < tabs.Length; i++)
            InkFrame.Button(ci, InkLayout.PageTab(i, tabs.Length),
                InkPageModel.Info(tabs[i]).Label, tabs[i] == current, true, 18, centered: true);
    }

    /// <summary>底部一行：操作反馈优先，其次列表溢出提示（角色三页无滚动列表，不提示）。</summary>
    private static void DrawFooter(CanvasItem ci, InkHubModel model)
    {
        var text = model.Notice;
        if (text.Length == 0 && model.Page is { Rows.Count: > 0 } page
            && !InkChatPages.Contains(page.Page))
        {
            var visible = InkLayout.ListVisibleRows(InkLayout.FullListArea);
            var hidden = page.Rows.Count - page.ListFirst - visible;
            if (hidden > 0)
                text = $"（另有 {hidden} 项未显示）";
        }
        if (text.Length > 0)
            InkDraw.TextBounded(ci, InkLayout.PageFooter, text, 18, 14, InkStyle.Dim);
    }

    /// <summary>右栏详情：标题、说明行与底部动作按钮。</summary>
    private static void DrawDetailPane(CanvasItem ci, Rect2 pane, InkPageModel page)
    {
        InkFrame.Panel(ci, pane, corner: 16f, rails: false);
        InkDraw.TextBounded(ci, InkLayout.DetailTitle(pane), page.DetailTitle, 24, 16);
        var lines = page.DetailNote.Split('\n');
        for (var i = 0; i < lines.Length; i++)
            InkDraw.TextBounded(ci, InkLayout.DetailNoteRow(pane, i), lines[i], 20, 14, InkStyle.Dim);
        for (var j = 0; j < page.DetailActions.Count; j++)
        {
            var rect = InkLayout.DetailButton(pane, j, page.DetailActions.Count);
            InkFrame.Button(ci, rect, page.DetailActions[j].Name,
                page.DetailActions[j].Selected, page.DetailActions[j].Enabled, 20, centered: true);
        }
    }

    /// <summary>开发页列表放不下的行数提示，贴在列表区底部。</summary>
    private static void DrawOverflow(CanvasItem ci, Rect2 listArea, int lastIndex, int hidden) =>
        InkDraw.TextBounded(ci,
            new Rect2(listArea.Position.X, listArea.End.Y - 26f, listArea.Size.X, 24f),
            $"（另有 {hidden} 项未显示）", 16, 12, InkStyle.Dim);

    /// <summary>
    /// 开发模式：四面板容器零改动，只有内容切换——
    /// 左上房间网格（非空房间右上白 X 拆除房间）、右上选中房间详情、
    /// 左下该房间的设施列表、右下选中设施的详情与建造/拆除按钮。
    /// </summary>
    public static void DrawDevPanels(CanvasItem ci, InkHubModel model)
    {
        var dev = model.Dev;
        if (dev == null)
            return;

        for (var x = 0; x < InkLayout.GridCols; x++)
        {
            for (var y = 0; y < InkLayout.GridRows; y++)
            {
                var rect = InkLayout.Cell(x, y);
                var cell = CellAt(dev, x, y);
                if (cell == null)
                {
                    // 还没有房间的格子：虚线空框。
                    InkDraw.Dashed(ci, rect.Position, new Vector2(rect.End.X, rect.Position.Y), InkStyle.Dim);
                    InkDraw.Dashed(ci, new Vector2(rect.End.X, rect.Position.Y), rect.End, InkStyle.Dim);
                    InkDraw.Dashed(ci, rect.End, new Vector2(rect.Position.X, rect.End.Y), InkStyle.Dim);
                    InkDraw.Dashed(ci, new Vector2(rect.Position.X, rect.End.Y), rect.Position, InkStyle.Dim);
                    continue;
                }

                if (cell.Selected)
                    ci.DrawRect(rect, new Color(InkStyle.Line, 0.10f));

                InkDraw.Ink(ci, new[]
                {
                    rect.Position,
                    new Vector2(rect.End.X, rect.Position.Y),
                    rect.End,
                    new Vector2(rect.Position.X, rect.End.Y),
                    rect.Position,
                }, InkStyle.Line, 1.4f, 0.4f, 8300 + cell.Id);

                InkDraw.TextFitted(ci, rect.GetCenter(), cell.Name,
                    rect.Size.X - 30f, 22, 14, InkStyle.Line, "cm");

                // 非空房间右上角白 X：拆除房间。
                if (cell.NonEmpty)
                    DrawX(ci, InkLayout.DevRoomX(rect), cell.Removable);
            }
        }

        // 右上：设施列表（全部设施，含未放置）+ 底部可建造设施目录。
        InkFrame.Title(ci, InkLayout.LogPanel, "设施");
        var facVisible = System.Math.Min(dev.FacilityRows.Count,
            InkLayout.DevFacilityVisibleRows);
        for (var j = 0; j < facVisible; j++)
            DrawListRow(ci, InkLayout.DevFacilityRow(j),
                dev.FacilityRows[j], j == dev.SelectedFacility);
        var facCatalog = System.Math.Min(dev.FacilityCatalog.Count,
            InkLayout.DevFacilityVisibleRows - facVisible);
        for (var c = 0; c < facCatalog; c++)
            DrawListRow(ci, InkLayout.DevFacilityRow(facVisible + c),
                dev.FacilityCatalog[c], false);
        if (dev.FacilityRows.Count > facVisible)
            DrawOverflow(ci, InkLayout.DevFacilityArea, facVisible - 1,
                dev.FacilityRows.Count - facVisible);

        // 左下：房间列表（含未放置的房间）+ 底部可建造房间目录。
        InkFrame.Title(ci, InkLayout.CharPanel, "房间");
        var roomVisible = System.Math.Min(dev.RoomRows.Count,
            InkLayout.DevRoomVisibleRows);
        for (var j = 0; j < roomVisible; j++)
            DrawListRow(ci, InkLayout.DevRoomRow(j),
                dev.RoomRows[j], j == dev.SelectedRoom);
        var roomCatalog = System.Math.Min(dev.RoomCatalog.Count,
            InkLayout.DevRoomVisibleRows - roomVisible);
        for (var c = 0; c < roomCatalog; c++)
            DrawListRow(ci, InkLayout.DevRoomRow(roomVisible + c),
                dev.RoomCatalog[c], false);
        if (dev.RoomRows.Count > roomVisible)
            DrawOverflow(ci, InkLayout.DevRoomArea, roomVisible - 1,
                dev.RoomRows.Count - roomVisible);

        // 右下：共用详情——点哪个显示哪个，附建造/拆除按钮。
        DrawDetailBox(ci, InkLayout.ActContent,
            dev.DetailTitle, dev.DetailNote, dev.DetailActions);

        // 退出开发，回到普通主界面。
        InkFrame.Button(ci, InkLayout.DevExitButton(InkLayout.ActContent),
            "退出开发", false, true, fontSize: 20);
    }

    private static InkDevRoomCell? CellAt(InkDevModel dev, int x, int y)
    {
        foreach (var c in dev.Rooms)
        {
            if (c.X == x && c.Y == y)
                return c;
        }
        return null;
    }

    /// <summary>设施列表行：名称左、状态/材料右，选中的行加亮。</summary>
    private static void DrawListRow(CanvasItem ci, Rect2 rect, InkDevRow row, bool selected)
    {
        DrawListRow(ci, rect, new InkPageRow
        {
            Name = row.Name,
            Value = row.Kind == InkDevRowKind.FacilityUnplaced ? "未放置" : "",
            Note = row.Note,
            Enabled = row.Enabled,
        }, selected);
    }

    /// <summary>非空房间右上角的白色拆除 X（叉为两笔交叉墨线）。</summary>
    private static void DrawX(CanvasItem ci, Rect2 rect, bool enabled)
    {
        var color = enabled ? InkStyle.Line : InkStyle.Dim;
        ci.DrawRect(rect, InkStyle.Inset);
        var pad = 4f;
        InkDraw.InkLine(ci, rect.Position + new Vector2(pad, pad),
            rect.End - new Vector2(pad, pad), color, 1.8f, 0.3f, 8420);
        InkDraw.InkLine(ci, new Vector2(rect.End.X - pad, rect.Position.Y + pad),
            new Vector2(rect.Position.X + pad, rect.End.Y - pad), color, 1.8f, 0.3f, 8421);
    }

    /// <summary>
    /// 列表控制行：搜索框（输入即过滤）、筛选档位、排序按钮（点击换字段，再点换方向）。
    /// </summary>
    private static void DrawControls(CanvasItem ci, Rect2 listArea, InkPageModel page)
    {
        var box = InkLayout.SearchBox(listArea);
        ci.DrawRect(box, InkStyle.Inset);
        InkDraw.Ink(ci, new[]
        {
            box.Position,
            new Vector2(box.End.X, box.Position.Y),
            box.End,
            new Vector2(box.Position.X, box.End.Y),
            box.Position,
        }, page.SearchFocused ? InkStyle.Line : InkStyle.Dim, 1.2f, 0.4f, 8701);

        var text = page.Search;
        if (text.Length == 0)
        {
            InkDraw.Text(ci, new Vector2(box.Position.X + 12f, box.GetCenter().Y),
                "搜索", 20, InkStyle.Dim, "lm");
        }
        else
        {
            InkDraw.Text(ci, new Vector2(box.Position.X + 12f, box.GetCenter().Y),
                text, 20, InkStyle.Line, "lm");
        }

        if (page.SearchFocused)
        {
            var w = text.Length == 0 ? 0f : InkDraw.Measure(text, 20).X;
            var caretX = box.Position.X + 12f + w + 3f;
            InkDraw.InkLine(ci, new Vector2(caretX, box.GetCenter().Y - 12f),
                new Vector2(caretX, box.GetCenter().Y + 12f), InkStyle.Line, 1.6f, 0.2f, 8702);
        }

        for (var i = 0; i < page.Filters.Count; i++)
        {
            InkFrame.Button(ci, InkLayout.FilterChip(listArea, i, page.Filters.Count),
                page.Filters[i], selected: i == page.ActiveFilter, enabled: true, fontSize: 20);
        }

        var arrow = page.ActiveSortDesc ? "↓" : "↑";
        InkFrame.Button(ci, InkLayout.SortButton(listArea),
            page.Sorts[page.ActiveSort] + arrow, false, true, fontSize: 20);
    }

    /// <summary>
    /// 左列表一行：名称与说明在左，数值靠右；有说明时拆成两行小字。
    /// 选中的行加亮框。
    /// </summary>
    private static void DrawListRow(CanvasItem ci, Rect2 rect, InkPageRow row, bool selected)
    {
        if (selected)
            InkFrame.Selection(ci, rect);

        var hasNote = row.Note.Length > 0;
        InkDraw.TextBounded(ci, InkLayout.ListRowName(rect, hasNote), row.Name, RowFont, 15);
        InkDraw.TextBounded(ci, InkLayout.ListRowValue(rect), row.Value, NoteFont, 14,
            InkStyle.Line, "rm");
        if (hasNote)
            InkDraw.TextBounded(ci, InkLayout.ListRowNote(rect), row.Note, 14, 12, InkStyle.Dim);
        InkFrame.RowRule(ci, rect);
    }

    /// <summary>
    /// 详情框：折角框内全部居中——标题水平居中贴顶，说明行整块垂直居中，
    /// 动作按钮底部横排居中。不放装饰线，大框里内容少也不显得空旷歪斜。
    /// </summary>
    private static void DrawDetailBox(CanvasItem ci, Rect2 pane, string title, string note,
        System.Collections.Generic.IReadOnlyList<InkPageRow> actions, bool grid = false)
    {
        if (title.Length == 0 && note.Length == 0 && actions.Count == 0)
            return;

        InkDraw.FoldedBox(ci, pane, InkStyle.Line, fold: 22f, fill: true, seed: 8600);

        var center = pane.GetCenter().X;

        // 标题：水平居中，贴顶。
        if (title.Length > 0)
            InkDraw.Text(ci, new Vector2(center, pane.Position.Y + 34f), title, 24,
                InkStyle.Line, "cm");

        // 说明行：去掉底部按钮带后，整块在剩余空间里垂直居中。
        var lines = note.Split('\n');
        const float lineH = 34f;
        var blockH = lines.Length * lineH;
        var blockTop = pane.Position.Y + 70f;
        var blockBottom = pane.End.Y - (count(actions) > 0 ? 100f : 30f);
        var y = blockTop + System.Math.Max(0f, (blockBottom - blockTop - blockH) / 2f);
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                y += 18f;
                continue;
            }
            InkDraw.Text(ci, new Vector2(center, y), line, DetailFont, InkStyle.Dim, "cm");
            y += lineH;
        }

        // 动作按钮：底部排开。网格页（日程派活）折行，其余页面保持单排横列。
        for (var slot = 0; slot < count(actions); slot++)
        {
            var row = actions[slot];
            var rect = grid
                ? InkLayout.DetailButtonGrid(pane, slot, count(actions))
                : InkLayout.DetailButton(pane, slot, count(actions));
            InkFrame.Button(ci, rect, row.Name, row.Selected, row.Enabled, 22);
        }

        static int count(System.Collections.Generic.IReadOnlyList<InkPageRow> list) =>
            list?.Count ?? 0;
    }

    private static void DrawClose(CanvasItem ci, Rect2 rect)
    {
        InkFrame.Button(ci, rect, "关闭", fontSize: 20, centered: true);
    }
}
