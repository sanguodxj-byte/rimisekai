using Godot;
using Rimisekai.Housing;
using Rimisekai.Hub;

namespace Rimisekai.Ink;

/// <summary>地图面板：5×5 格、房间墙线、门洞、各房间的在场角色标识。</summary>
public static class InkMapRenderer
{
    public static void Draw(CanvasItem ci, InkHubModel model)
    {
        if (model.StorageOpen)
        {
            DrawStoragePage(ci, model);
            return;
        }

        // 场景演出进行中：插画直接遮住网格。只遮领地层——世界层的兴趣点还得看得见。
        // 平时（没在演出）网格常驻，插画不能常驻盖死。
        // 闸门链（任一环成立即插画盖网格，纹理在盘上为末环）：
        // 长链——场景演出进行中且 Core 链全过；
        // 最短链——玩家在本房间点了观察四周（优先取房间声明插画，缺省退占位）。
        Texture2D? illustration = null;
        if (model.SceneIllustrationCover)
            illustration = InkIllustration.Get();
        else if (model.ObservingRoom)
        {
            var path = model.ObservingRoomIllustrationPath;
            if (path.Length > 0)
                illustration = InkIllustration.LoadTexture(path);
            illustration ??= InkIllustration.ForRoom(model.ObservedRoomName, model.Hour);
        }

        if (illustration != null)
        {
            DrawIllustration(ci, model, illustration);
            return;
        }

        for (var row = 0; row < InkLayout.GridRows; row++)
        {
            for (var col = 0; col < InkLayout.GridCols; col++)
            {
                var rect = InkLayout.Cell(col, row);
                var room = FindRoom(model, col, row);
                if (room == null)
                    DrawUndeveloped(ci, rect);
                else
                    DrawRoom(ci, model, room, col, row, rect);
            }
        }
    }

    /// <summary>
    /// 场景插画：等比放大到铺满整块面板，居中裁掉出界的部分，
    /// 再把框线压回插画上——画与框体同大，不留衬边。
    /// 不拉伸——直接填会变形。
    /// 演出中的对话走聊天层（InkOverlayRenderer），半透明叠在这层插画上。
    /// </summary>
    private static void DrawIllustration(CanvasItem ci, InkHubModel model, Texture2D texture)
    {
        var target = InkLayout.MapPanel;
        var size = texture.GetSize();

        var scale = Mathf.Max(target.Size.X / size.X, target.Size.Y / size.Y);
        var srcW = target.Size.X / scale;
        var srcH = target.Size.Y / scale;
        var src = new Rect2((size.X - srcW) / 2f, (size.Y - srcH) / 2f, srcW, srcH);

        ci.DrawTextureRectRegion(texture, target, src);
        InkFrame.PanelFrame(ci, target, corner: 24f);
    }

    /// <summary>
    /// 房间按格子坐标查。地图数据随模型一起传入，
    /// 不再回头去读 Hub，避免绘制期状态漂移。
    /// </summary>
    private static Room? FindRoom(InkHubModel model, int col, int row) =>
        model.MapAt(col, row);

    /// <summary>
    /// 还没开拓的空格子：只留一圈虚线，**不写任何字**。
    /// 主人定（2026-10-01）：开拓入口一律在开发页，主地图不出现「未开拓」字样、也不报开拓价。
    /// </summary>
    private static void DrawUndeveloped(CanvasItem ci, Rect2 rect)
    {
        InkDraw.Dashed(ci, rect.Position, new Vector2(rect.End.X, rect.Position.Y), InkStyle.Dim);
        InkDraw.Dashed(ci, new Vector2(rect.End.X, rect.Position.Y), rect.End, InkStyle.Dim);
        InkDraw.Dashed(ci, rect.End, new Vector2(rect.Position.X, rect.End.Y), InkStyle.Dim);
        InkDraw.Dashed(ci, new Vector2(rect.Position.X, rect.End.Y), rect.Position, InkStyle.Dim);
    }

    /// <summary>
    /// 画一间房。每格只画自己的上边与左边，右边和下边仅在邻格没有房间时补，
    /// 这样相邻房间的共享墙只会出现一条线，不会重成“梯子”。
    /// </summary>
    private static void DrawRoom(CanvasItem ci, InkHubModel model, Room room,
        int col, int row, Rect2 rect)
    {
        var color = room.Open ? InkStyle.Line : InkStyle.Dim;

        var tl = rect.Position;
        var tr = new Vector2(rect.End.X, rect.Position.Y);
        var bl = new Vector2(rect.Position.X, rect.End.Y);

        DrawEdge(ci, model, room, col, row, 0, tl, tr, color);
        DrawEdge(ci, model, room, col, row, 3, bl, tl, color);
        if (model.MapAt(col + 1, row) == null)
            DrawEdge(ci, model, room, col, row, 1, tr, rect.End, color);
        if (model.MapAt(col, row + 1) == null)
            DrawEdge(ci, model, room, col, row, 2, rect.End, bl, color);

        // 有人时房名让到上方，把下半格留给角色标识；空房仍居中，版面不空旷。
        var occupants = model.OccupantsIn(room.Id);
        var nameY = occupants.Count > 0
            ? rect.Position.Y + 22f
            : rect.GetCenter().Y;

        if (model.PlayerRoomId == room.Id)
        {
            InkDraw.TextFitted(ci, new Vector2(rect.GetCenter().X, nameY),
                $"[{room.Name}]", rect.Size.X - 12f, 20, 13, InkStyle.Line, "cm");
        }
        else
        {
            // 未开放的房间只报房名（压暗），**不报开拓价**——开拓入口在开发页。
            // 名字长了就缩字号，有下限。
            InkDraw.TextFitted(ci, new Vector2(rect.GetCenter().X, nameY),
                $"[{room.Name}]", rect.Size.X - 12f,
                room.Open ? 20 : 18, 12,
                room.Open ? InkStyle.Line : InkStyle.Dim, "cm");
        }

        DrawOccupants(ci, occupants, rect);
    }

    /// <summary>
    /// 房间里的角色标识：房名下方一排国际象棋棋子。
    /// 玩家为国王，NPC 依好感分王后/中阶/小兵；人数超出上限时在右下角标出余数。
    /// </summary>
    private static void DrawOccupants(CanvasItem ci,
        System.Collections.Generic.IReadOnlyList<CharacterCard> who, Rect2 rect)
    {
        if (who.Count == 0)
            return;

        var shown = System.Math.Min(who.Count, InkLayout.OccupantMaxShown);
        var h = InkLayout.OccupantFigureHeight;
        var footY = rect.End.Y - 10f;

        // 宽度不够时收缩间距，但标识本身尺寸不变（地图与设施等大）。
        var step = System.Math.Min(InkLayout.OccupantStep, (rect.Size.X - 24f) / shown);
        var startX = rect.GetCenter().X - step * (shown - 1) / 2f;

        for (var i = 0; i < shown; i++)
        {
            var cx = startX + step * i;
            var card = who[i];
            var piece = InkDraw.PieceFor(card);
            InkDraw.Chess(ci, new Vector2(cx, footY), h, piece, isLimitCap: false);
        }

        if (who.Count > shown)
        {
            InkDraw.Text(ci, new Vector2(rect.End.X - 8f, footY),
                $"+{who.Count - shown}", 14, InkStyle.Dim, "rb");
        }
    }

    /// <summary>画一条墙线；若该侧邻房与本房相通，则在中间留出门洞。</summary>
    private static void DrawEdge(CanvasItem ci, InkHubModel model, Room room,
        int col, int row, int side, Vector2 a, Vector2 b, Color color)
    {
        var (dx, dy) = side switch
        {
            0 => (0, -1),
            1 => (1, 0),
            2 => (0, 1),
            _ => (-1, 0),
        };

        var neighbor = model.MapAt(col + dx, row + dy);
        var seed = (int)(a.X + a.Y) + side * 101;

        if (neighbor == null || !room.Links.Contains(neighbor.Id))
        {
            InkDraw.InkLine(ci, a, b, color, 2f, 1.1f, seed);
            return;
        }

        // 门洞：两段墙 + 两侧门柱。
        var mid1 = a.Lerp(b, 0.38f);
        var mid2 = a.Lerp(b, 0.62f);
        InkDraw.InkLine(ci, a, mid1, color, 2f, 1.1f, seed);
        InkDraw.InkLine(ci, mid2, b, color, 2f, 1.1f, seed + 1);

        var n = (b - a).Normalized().Orthogonal() * 7f;
        InkDraw.InkLine(ci, mid1, mid1 + n, color, 1f, 0.4f, seed + 2);
        InkDraw.InkLine(ci, mid2, mid2 + n, color, 1f, 0.4f, seed + 3);
    }

    /// <summary>设施交互/存储配置页：替代地图网格在左上面板展示。</summary>
    private static void DrawStoragePage(CanvasItem ci, InkHubModel model)
    {
        InkFrame.Panel(ci, InkLayout.FixturePanel, fill: InkStyle.Panel);
        InkFrame.TitleFitted(ci, InkLayout.FixturePanel, model.StorageTitle,
            InkLayout.FixturePanel.Size.X - (InkFrame.Pad + 50f) * 2f, 26, 18);

        DrawClose(ci, InkLayout.FixtureClose);

        DrawCategoryBand(ci, model);

        if (model.StorageRows.Count == 0)
        {
            InkDraw.Text(ci, InkLayout.FixtureContent.GetCenter(), "设施与背包均无可用物品", 20, InkStyle.Dim, "cm");
            return;
        }

        var visible = InkLayout.FixtureVisibleRows;
        var first = Mathf.Clamp(model.StorageFirst, 0, Mathf.Max(0, model.StorageRows.Count - visible));
        for (var i = 0; i < visible && first + i < model.StorageRows.Count; i++)
        {
            var entry = model.StorageRows[first + i];
            var row = InkLayout.StorageRow(i);

            ci.DrawRect(row, InkStyle.Inset);
            InkDraw.Ink(ci, RectLoop(row), InkStyle.Dim, 0.8f, 0.2f, 7000 + i);

            var filterText = entry.Excluded ? "【禁止存入】" : "【允许存入】";
            var filterColor = entry.Excluded ? InkStyle.Dim : InkStyle.Line;
            var textY = row.GetCenter().Y;
            var label = string.IsNullOrEmpty(entry.CategoryLabel)
                ? entry.ItemId
                : $"{entry.ItemId}·{entry.CategoryLabel}";
            InkDraw.TextFitted(ci, new Vector2(row.Position.X + 12f, textY), label, 150f, 18, 13, InkStyle.Line, "lm");
            // 标签与数值分两段画（暗标签 + 亮数值），不用冒号分隔。
            DrawCountPair(ci, row.Position.X + 172f, textY, "设施内", $"{entry.InStorage}");
            DrawCountPair(ci, row.Position.X + 310f, textY, "背包", $"{entry.InBag}");
            InkDraw.Text(ci, new Vector2(row.Position.X + 460f, textY), filterText, 18, filterColor, "lm");

            var btn0 = InkLayout.StorageRowButton(row, 0);
            var btn1 = InkLayout.StorageRowButton(row, 1);
            var btn2 = InkLayout.StorageRowButton(row, 2);

            InkFrame.Button(ci, btn0, "放入", selected: false, enabled: entry.InBag > 0, fontSize: 18, centered: true);
            InkFrame.Button(ci, btn1, "取出", selected: false, enabled: entry.InStorage > 0, fontSize: 18, centered: true);
            InkFrame.Button(ci, btn2, entry.Excluded ? "允许" : "禁止", selected: false, enabled: true, fontSize: 18, centered: true);
        }

        InkPageRenderer.DrawScrollbars(ci, model.Widgets);
    }

    /// <summary>存储行里的一处「标签 数值」：暗色标签后紧跟亮色数值，无冒号。</summary>
    private static void DrawCountPair(CanvasItem ci, float x, float y, string label, string value)
    {
        const float gap = 8f;
        const int size = 18;
        var width = InkDraw.Measure(label, size).X;
        InkDraw.Text(ci, new Vector2(x, y), label, size, InkStyle.Dim, "lm");
        InkDraw.Text(ci, new Vector2(x + width + gap, y), value, size, InkStyle.Line, "lm");
    }

    /// <summary>品类开关带：顶部一排按钮，按大类一键收放整个品类。</summary>
    private static void DrawCategoryBand(CanvasItem ci, InkHubModel model)
    {
        for (var i = 0; i < model.StorageCategories.Count; i++)
        {
            var cat = model.StorageCategories[i];
            var rect = InkLayout.StorageCategoryRow(i);
            var widget = model.Find(InkAction.StorageCategoryToggle, i);
            var enabled = widget?.Enabled ?? true;
            InkFrame.Button(ci, rect, widget?.Label ?? cat.Label,
                selected: cat.Accepted, enabled: enabled, fontSize: 17, centered: true);
        }
    }

    private static void DrawClose(CanvasItem ci, Rect2 rect)
    {
        ci.DrawRect(rect, InkStyle.Inset);
        InkDraw.Ink(ci, RectLoop(rect), InkStyle.Line, 1.2f, 0.4f, 8801);

        var pad = 9f;
        InkDraw.InkLine(ci, rect.Position + new Vector2(pad, pad),
            rect.End - new Vector2(pad, pad), InkStyle.Line, 1.6f, 0.3f, 8802);
        InkDraw.InkLine(ci, new Vector2(rect.End.X - pad, rect.Position.Y + pad),
            new Vector2(rect.Position.X + pad, rect.End.Y - pad), InkStyle.Line, 1.6f, 0.3f, 8803);
    }

    private static Vector2[] RectLoop(Rect2 r) => new[]
    {
        r.Position,
        new Vector2(r.End.X, r.Position.Y),
        r.End,
        new Vector2(r.Position.X, r.End.Y),
        r.Position,
    };
}
