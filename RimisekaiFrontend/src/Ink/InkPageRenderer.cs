using System.Collections.Generic;
using Godot;
using Rimisekai.Housing;

namespace Rimisekai.Ink;

/// <summary>
/// 子页面：库存 / 交易 / 制作全屏铺开，左列表右详情；
/// 交易是三面板（左领地库存 / 中交易钮 / 右市场库存）；
/// 开发全屏五区域——左上领地网格（含邻近可开拓的未开发房间）、右上选中房间的设施、
/// 左下待安装的房间、中下操作面板、右下详情（纯文字）。见 <see cref="DrawDevPanels"/>。
/// </summary>
public static class InkPageRenderer
{
    private const int RowFont = 26;
    private const int NoteFont = 26;
    private const int DetailFont = 26;

    public static void Draw(CanvasItem ci, InkHubModel model)
    {
        var page = model.Page;
        if (page == null)
            return;

        // 全屏页统一木框；右上角不再显示季节/天气/时刻/金钱状态行（主人 2026-09-30 定）。
        InkFrame.PageShell(ci, page.Title, wood: true, subtitle: page.Subtitle);
        DrawTabs(ci, page.Page);

        if (InkChatPages.Contains(page.Page))
        {
            InkCharacterPageRenderer.Draw(ci, model);
        }
        else if (page.Trade != null)
        {
            InkTradePageRenderer.Draw(ci, page.Trade);
        }
        else
        {
            // 面板定高（列表界面不做自动缩放）：左列表 + 右详情各占满整幅。
            var listPanel = InkLayout.FullListPanel;
            var listArea = listPanel.Grow(-InkLayout.Pad);
            var detailPane = InkLayout.FullDetailArea;

            InkFrame.Panel(ci, listPanel, corner: 16f);

            if (page.Rows.Count == 0)
            {
                InkDraw.TextBounded(ci, listArea, page.EmptyHint, 20, 14, InkStyle.Line, "cm");
                DrawDetailPane(ci, detailPane, page);
                DrawScrollbars(ci, model.Widgets);
                return;
            }

            if (page.HasControls)
                DrawControls(ci, listArea, page);

            var visible = InkLayout.ListVisibleRows(listArea);
            var first = page.ListFirst;
            var shown = System.Math.Min(page.Rows.Count - first, visible);
            for (var i = 0; i < shown; i++)
                DrawListRow(ci, InkLayout.ListRow(listArea, i),
                    page.Rows[first + i], first + i == page.SelectedRow);

            DrawDetailPane(ci, detailPane, page);
        }

        DrawScrollbars(ci, model.Widgets);
    }

    /// <summary>
    /// 把元素表里的滑条（轨道+滑块对）画出来。列表几何只在模型侧建一次，
    /// 渲染端从同一份元素表反推比率——画出来的与点得到的永远一致。
    /// </summary>
    public static void DrawScrollbars(CanvasItem ci, IReadOnlyList<InkWidget> widgets)
    {
        for (var i = 0; i < widgets.Count; i++)
        {
            if (widgets[i].Action != InkAction.ScrollThumb)
                continue;
            var thumb = widgets[i].Rect;
            for (var j = 0; j < widgets.Count; j++)
            {
                if (widgets[j].Action != InkAction.ScrollJump || widgets[j].Index != widgets[i].Index)
                    continue;
                var track = widgets[j].Rect;
                var ratio = track.Size.Y - thumb.Size.Y <= 0f
                    ? 0f
                    : Mathf.Clamp((thumb.Position.Y - track.Position.Y) / (track.Size.Y - thumb.Size.Y), 0f, 1f);
                var thumbRatio = track.Size.Y <= 0f ? 1f : Mathf.Clamp(thumb.Size.Y / track.Size.Y, 0f, 1f);
                InkDraw.Scrollbar(ci, track, ratio, thumbRatio);
                break;
            }
        }
    }

    /// <summary>页签带：领地页与角色页彻底隔离，当前页高亮。</summary>
    public static void DrawTabs(CanvasItem ci, InkPage current)
    {
        var tabs = InkPageTabs.For(current);
        for (var i = 0; i < tabs.Length; i++)
            InkFrame.Button(ci, InkLayout.PageTab(i, tabs.Length),
                InkPageModel.Info(tabs[i]).Label, tabs[i] == current, true, 26, centered: true);
    }

    /// <summary>详情行数（按 捕行）。</summary>
    private static int NoteLineCount(InkPageModel page) =>
        page.DetailNote.Length == 0 ? 0 : page.DetailNote.Split('\n').Length;

    /// <summary>
    /// 右栏详情：标题、说明行与动作钮合成一组，在栏内垂直居中——
    /// 不再标题钉顶、按钮钉底，中间留一大块黑。仅纯文本排版，不出现放大版图标。
    /// </summary>
    /// <summary>
    /// 右栏详情：标题居顶加渐隐线，详细说明按规范行距排列，动作按钮固定在底端。
    /// 排版紧凑清晰，不搞夸张拉伸。
    /// </summary>
    private static void DrawDetailPane(CanvasItem ci, Rect2 pane, InkPageModel page)
    {
        InkFrame.Panel(ci, pane, corner: 16f);
        if (string.IsNullOrEmpty(page.DetailTitle) && string.IsNullOrEmpty(page.DetailNote) && page.DetailActions.Count == 0)
            return;

        if (!string.IsNullOrEmpty(page.DetailTitle))
        {
            var titleRect = new Rect2(pane.Position.X + InkLayout.Pad, pane.Position.Y + InkLayout.Pad,
                pane.Size.X - InkLayout.Pad * 2f, 44f);
            InkDraw.TextBounded(ci, titleRect, page.DetailTitle, 24, 16);
            var ruleY = titleRect.End.Y + 6f;
            InkFrame.FadingRule(ci, pane.Position.X + InkLayout.Pad, pane.End.X - InkLayout.Pad, ruleY);
        }

        var lines = page.DetailNote.Split('\n');
        const float lineStep = 34f;
        var noteStartY = pane.Position.Y + InkLayout.Pad + 60f;
        for (var idx = 0; idx < lines.Length; idx++)
        {
            var line = lines[idx];
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var y = noteStartY + idx * lineStep;
            if (y > pane.End.Y - InkLayout.Pad - 64f)
                break;
            var rowRect = new Rect2(pane.Position.X + InkLayout.Pad, y,
                pane.Size.X - InkLayout.Pad * 2f, 28f);
            var isHeading = line.StartsWith("【") || line.StartsWith("所需材料");
            var color = isHeading ? InkStyle.Line : InkStyle.Dim;
            var size = isHeading ? 18 : 17;
            InkDraw.TextBounded(ci, rowRect, line, size, 14, color);
        }

        for (var j = 0; j < page.DetailActions.Count; j++)
        {
            var rect = InkLayout.DetailButton(pane, j, page.DetailActions.Count);
            InkFrame.Button(ci, rect, page.DetailActions[j].Name,
                page.DetailActions[j].Selected, page.DetailActions[j].Enabled, 20, centered: true);
        }
    }
    /// <summary>
    /// 开发页（2026-10-01 主人定）：
    /// 左上领地网格（已开发房间实线格；挨着已开发地方的未开发房间虚线格，点了弹确认窗）、
    /// 右上当前选中房间的设施、左下已建未安装的房间、
    /// 中下操作面板（所有操作按钮都在这里）、右下详情（只有文字，**不画内部边框**）。
    /// </summary>
    public static void DrawDevPanels(CanvasItem ci, InkHubModel model)
    {
        var dev = model.Dev;
        if (dev == null)
            return;

        DrawDevGrid(ci, dev);

        // 右上：**只列当前选中房间的设施**，标题带房间名。
        InkFrame.TitleFitted(ci, InkLayout.LogPanel, dev.FacilityTitle,
            InkLayout.LogPanel.Size.X - 200f, 26, 18);
        var facVisible = System.Math.Min(dev.FacilityRows.Count,
            InkLayout.DevFacilityVisibleRows);
        for (var j = 0; j < facVisible; j++)
            DrawListRow(ci, InkLayout.DevFacilityRow(j), dev.FacilityRows[j],
                dev.FacilityRows[j].Selected);
        if (dev.FacilityRows.Count == 0)
            InkDraw.TextBounded(ci, InkLayout.DevFacilityRow(0),
                dev.RoomId >= 0 ? "这间屋里还没有设施。" : "先在上面网格里选一间房。",
                20, 15, InkStyle.Dim, "lm");

        // 左下：**只列已建但还没安装的房间**。
        InkFrame.Title(ci, InkLayout.CharPanel, dev.RoomListTitle);
        var roomVisible = System.Math.Min(dev.RoomRows.Count,
            InkLayout.DevRoomVisibleRows);
        for (var j = 0; j < roomVisible; j++)
            DrawListRow(ci, InkLayout.DevRoomRow(j), dev.RoomRows[j],
                dev.RoomRows[j].Selected);

        // 中下：操作面板——与主界面共用同一套落位（大按钮 ＋ 下面接行），只换文字。
        // 大按钮位放「退出开发」（主界面那位放的是「工作安排」），不再单独画标题带。
        InkFrame.Button(ci, InkLayout.DevExitButton, "退出开发", false, true, 22, centered: true);
        var actVisible = System.Math.Min(dev.ActionRows.Count,
            InkLayout.DevActionVisibleRows);
        var actFirst = System.Math.Clamp(dev.ActionFirst, 0,
            System.Math.Max(0, dev.ActionRows.Count - actVisible));
        for (var j = 0; j < actVisible; j++)
            DrawDevActionButton(ci, InkLayout.DevActionRow(j), dev.ActionRows[actFirst + j],
                8700 + j * 13);
        if (dev.ActionRows.Count == 0)
            InkDraw.TextBounded(ci, InkLayout.DevActionRow(0), "没有可做的事。", 18, 14, InkStyle.Dim, "lm");

        // 右下：详情——只有文字，不画内部边框，也不放按钮。
        if (!string.IsNullOrEmpty(dev.DetailTitle))
        {
            InkFrame.TitleFitted(ci, InkLayout.ActPanel, dev.DetailTitle,
                InkLayout.ActPanel.Size.X - 200f, 26, 18);
        }
        var startY = string.IsNullOrEmpty(dev.DetailTitle)
            ? InkLayout.ActPanel.Position.Y + InkLayout.Pad + 8f
            : InkLayout.ActContent.Position.Y;
        var lines = dev.DetailNote.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var rect = new Rect2(InkLayout.ActContent.Position.X,
                startY + i * 36f,
                InkLayout.ActContent.Size.X, 32f);
            InkDraw.TextBounded(ci, rect, lines[i], 20, 14, InkStyle.Line, "lm");
        }

        // 滑条画在内容之后、弹窗之前（开发页不走全屏页那条路径，得自己叫一次）。
        DrawScrollbars(ci, model.Widgets);

        // 开拓确认弹窗盖在最上面。
        if (dev.ConfirmCell >= 0)
            DrawDevConfirm(ci, dev);
    }

    /// <summary>开发页左上：领地网格。已开发房间画实线格，邻近可开拓的未开发房间画虚线格。</summary>
    private static void DrawDevGrid(CanvasItem ci, InkDevModel dev)
    {
        for (var x = 0; x < InkLayout.GridCols; x++)
        {
            for (var y = 0; y < InkLayout.GridRows; y++)
            {
                var rect = InkLayout.Cell(x, y);
                var cell = CellAt(dev, x, y);
                if (cell == null)
                {
                    // 够不着的空格子：极淡的虚线空框，点不动。
                    DashedBox(ci, rect, new Color(InkStyle.Dim, 0.5f));
                    continue;
                }

                if (cell.Selected)
                    ci.DrawRect(rect, new Color(InkStyle.Line, 0.10f));

                if (cell.Open)
                {
                    // 「空房」（开拓出来的毛坯）画得比正式房间淡，一眼看出还没装东西；
                    // 正在选落点时反而点亮它——这几格就是能装的地方。
                    var placingHere = dev.PlacingRoom >= 0 && cell.Vacant;
                    var ink = cell.Vacant && !placingHere ? InkStyle.Dim : InkStyle.Line;
                    InkDraw.Ink(ci, Loop(rect), ink,
                        placingHere ? 1.8f : (cell.Vacant ? 1.1f : 1.4f), 0.4f, 8300 + cell.Id);
                    InkDraw.TextFitted(ci, rect.GetCenter(), cell.Name,
                        rect.Size.X - 30f, 22, 14, ink, "cm");
                    // 非空房间右上角白 X：拆除房间。
                    if (cell.NonEmpty)
                        DrawX(ci, InkLayout.DevRoomX(rect), cell.Removable);
                    continue;
                }

                // 邻近的未开发格（含没有房间实体的「空地」）：虚线格 ＋ 暗字 ＋ 右下角花费。
                DashedBox(ci, rect, cell.CanDevelop ? InkStyle.Line : InkStyle.Dim);
                InkDraw.TextFitted(ci,
                    new Vector2(rect.GetCenter().X, rect.Position.Y + rect.Size.Y * 0.42f),
                    cell.Name, rect.Size.X - 24f, 20, 13, InkStyle.Dim, "cm");
                if (cell.CanDevelop && cell.CostText.Length > 0)
                    InkDraw.TextFitted(ci,
                        new Vector2(rect.GetCenter().X, rect.End.Y - 16f),
                        cell.CostText, rect.Size.X - 16f, 14, 10, InkStyle.Dim, "cm");
            }
        }

        // 四个朝向各画一个「去隔壁区域」的指示箭头：极矮极宽，压在对应边的正中格上。
        // 已解锁的朝向画亮，未解锁画暗（主人定：都画，未解锁的变暗）。
        foreach (var dir in new[]
        {
            Territory.RegionDir.North, Territory.RegionDir.East,
            Territory.RegionDir.South, Territory.RegionDir.West,
        })
            DrawRegionArrow(ci, dir, dev.RegionId, dev.UnlockedRegionMask);
    }

    /// <summary>
    /// 网格边上的区域指示箭头：一条贴边的长线 ＋ 一个很浅的箭头尖，
    /// 合起来就是「极矮极宽」的箭头。已解锁画亮，未解锁画暗。
    /// </summary>
    private static void DrawRegionArrow(CanvasItem ci, Territory.RegionDir dir,
        int regionId, int unlockedMask)
    {
        var neighbor = Territory.RegionNeighbor(regionId, dir);
        var unlocked = neighbor >= 0 && neighbor < Territory.MaxTerritoryRegions
            && (unlockedMask & (1 << neighbor)) != 0;
        var color = unlocked ? InkStyle.Line : new Color(InkStyle.Dim, 0.40f);

        var (gx, gy) = Territory.RegionGate(dir);
        var cell = InkLayout.Cell(gx, gy);
        const float flat = 13f;   // 箭头极矮：这点厚度
        const float pad = 8f;

        if (dir == Territory.RegionDir.North || dir == Territory.RegionDir.South)
        {
            var north = dir == Territory.RegionDir.North;
            var y = north ? cell.Position.Y + flat : cell.End.Y - flat;
            var tipY = north ? cell.Position.Y + 1f : cell.End.Y - 1f;
            var left = cell.Position.X + pad;
            var right = cell.End.X - pad;
            var mid = (left + right) * 0.5f;
            InkDraw.InkLine(ci, new Vector2(left, y), new Vector2(right, y), color, 2.0f);
            InkDraw.InkLine(ci, new Vector2(left, y), new Vector2(mid, tipY), color, 2.0f);
            InkDraw.InkLine(ci, new Vector2(right, y), new Vector2(mid, tipY), color, 2.0f);
        }
        else
        {
            var west = dir == Territory.RegionDir.West;
            var x = west ? cell.Position.X + flat : cell.End.X - flat;
            var tipX = west ? cell.Position.X + 1f : cell.End.X - 1f;
            var top = cell.Position.Y + pad;
            var bottom = cell.End.Y - pad;
            var midY = (top + bottom) * 0.5f;
            InkDraw.InkLine(ci, new Vector2(x, top), new Vector2(x, bottom), color, 2.0f);
            InkDraw.InkLine(ci, new Vector2(x, top), new Vector2(tipX, midY), color, 2.0f);
            InkDraw.InkLine(ci, new Vector2(x, bottom), new Vector2(tipX, midY), color, 2.0f);
        }
    }

    /// <summary>操作面板的一行：细双线按钮 ＋ 左侧短前缀（设／房／拆／安）＋ 名称。</summary>
    private static void DrawDevActionButton(CanvasItem ci, Rect2 rect, InkDevActionRow row, int seed)
    {
        var color = row.Enabled ? InkStyle.Line : InkStyle.Dim;
        ci.DrawRect(rect, row.Selected ? InkStyle.Hover : InkStyle.Inset);
        InkDraw.Ink(ci, Loop(rect), color, row.Selected ? 1.8f : 1.2f, 0.3f, seed);
        InkDraw.Ink(ci, Loop(rect.Grow(-3f)), InkStyle.Dim, 0.9f, 0.22f, seed + 1);

        var cy = rect.GetCenter().Y;
        var x = rect.Position.X + 10f;
        if (row.Prefix.Length > 0)
        {
            InkDraw.Text(ci, new Vector2(x, cy), row.Prefix, 15, InkStyle.Dim, "lm");
            x += 20f;
            InkDraw.InkLine(ci, new Vector2(x - 6f, rect.Position.Y + 9f),
                new Vector2(x - 6f, rect.End.Y - 9f), new Color(InkStyle.Dim, 0.5f), 1f);
            x += 4f;
        }
        InkDraw.TextFitted(ci, new Vector2(x, cy), row.Name,
            rect.End.X - 10f - x, 18, 13, color, "lm");
    }

    /// <summary>开拓确认弹窗：遮罩 ＋ 居中一块小面板（标题／正文／开拓·取消）。</summary>
    private static void DrawDevConfirm(CanvasItem ci, InkDevModel dev)
    {
        ci.DrawRect(new Rect2(0, 0, InkLayout.CanvasWidth, InkLayout.CanvasHeight),
            new Color(InkStyle.Bg, 0.55f));

        var panel = InkLayout.DevConfirmPanel;
        ci.DrawRect(panel, InkStyle.Panel);
        InkFrame.Panel(ci, panel);

        InkDraw.TextBounded(ci, InkLayout.DevConfirmTitle, dev.ConfirmTitle,
            26, 18, InkStyle.Line, "lm");
        InkFrame.FadingRule(ci, InkLayout.DevConfirmTitle.Position.X,
            InkLayout.DevConfirmTitle.End.X, InkLayout.DevConfirmTitle.End.Y + 4f);

        var lines = dev.ConfirmBody.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var rect = new Rect2(InkLayout.DevConfirmBody.Position.X,
                InkLayout.DevConfirmBody.Position.Y + i * 34f,
                InkLayout.DevConfirmBody.Size.X, 30f);
            InkDraw.TextBounded(ci, rect, lines[i], 20, 15, InkStyle.Line, "lm");
        }

        InkFrame.Button(ci, InkLayout.DevConfirmButton(0), "开拓", false, true, 22, centered: true);
        InkFrame.Button(ci, InkLayout.DevConfirmButton(1), "取消", false, true, 22, centered: true);
    }

    private static Vector2[] Loop(Rect2 r) => new[]
    {
        r.Position,
        new Vector2(r.End.X, r.Position.Y),
        r.End,
        new Vector2(r.Position.X, r.End.Y),
        r.Position,
    };

    /// <summary>四边虚线框（空位与未开发格的画法）。</summary>
    private static void DashedBox(CanvasItem ci, Rect2 rect, Color color)
    {
        InkDraw.Dashed(ci, rect.Position, new Vector2(rect.End.X, rect.Position.Y), color);
        InkDraw.Dashed(ci, new Vector2(rect.End.X, rect.Position.Y), rect.End, color);
        InkDraw.Dashed(ci, rect.End, new Vector2(rect.Position.X, rect.End.Y), color);
        InkDraw.Dashed(ci, new Vector2(rect.Position.X, rect.End.Y), rect.Position, color);
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
    /// 选中的行加亮框。带有图标时在行首呈现精致小图标（仅作为小图标存在，无放大版本）。
    /// </summary>
    private static void DrawListRow(CanvasItem ci, Rect2 rect, InkPageRow row, bool selected)
    {
        if (selected)
            InkFrame.Selection(ci, rect);

        var hasIcon = InkIcon.Has(row.Name);
        var iconOffset = hasIcon ? 52f : 0f;
        if (hasIcon)
        {
            var iconBox = new Rect2(rect.Position.X + 16f, rect.GetCenter().Y - 20f, 40f, 40f);
            InkIcon.Draw(ci, row.Name, iconBox, showBorder: false);
        }

        var hasNote = row.Note.Length > 0;
        var nameRect = InkLayout.ListRowName(rect, hasNote);
        nameRect = new Rect2(nameRect.Position.X + iconOffset, nameRect.Position.Y, nameRect.Size.X - iconOffset, nameRect.Size.Y);
        InkDraw.TextBounded(ci, nameRect, row.Name, RowFont, 15);
        InkDraw.TextBounded(ci, InkLayout.ListRowValue(rect), row.Value, NoteFont, 14,
            InkStyle.Line, "rm");
        if (hasNote)
        {
            var noteRect = InkLayout.ListRowNote(rect);
            noteRect = new Rect2(noteRect.Position.X + iconOffset, noteRect.Position.Y, noteRect.Size.X - iconOffset, noteRect.Size.Y);
            InkDraw.TextBounded(ci, noteRect, row.Note, 14, 12, InkStyle.Line);
        }
        InkFrame.RowDivider(ci, rect);
    }

    // 🚨 `DrawDetailBox`（折角框＋居中标题＋底部按钮排）已于 2026-10-01 删除，禁止再引入。
    // 主人原话：「右下角详情页重置，删除内部边框」。那个折角框就是「内部边框」，
    // 按钮则全部归拢到中下操作面板。详情现在只有「标题字 ＋ 渐隐线 ＋ 若干行文字」，
    // 与其余面板标题同一套画法，见 `DrawDevPanels` 末尾。
}
