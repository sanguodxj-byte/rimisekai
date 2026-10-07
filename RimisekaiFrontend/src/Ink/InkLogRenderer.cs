using Godot;

namespace Rimisekai.Ink;

/// <summary>日志与“此处”面板。</summary>
public static class InkLogRenderer
{
    private const int LogFontMax = 26;
    private const int LogFontMin = 26;

    private const int HereFontMin = 26;

    private const int RowFontMax = 26;
    private const int RowFontMin = 26;

    private const float SlotStep = InkLayout.OccupantStep;
    private const float SlotFigureHeight = InkLayout.OccupantFigureHeight;

    public static void Draw(CanvasItem ci, InkHubModel model)
    {
        InkFrame.Title(ci, InkLayout.LogPanel, "日志", 26);
        DrawHeaderStatus(ci, model);

        var content = InkLayout.LogContent;
        var usable = content.Size.X - 12f;

        // 日志行：默认 4 行给设施段让位；展开后铺满内容区（让出箭头行）。
        var logArea = InkLayout.LogLinesArea(model.LogExpanded);
        var capacity = Mathf.Max(1, (int)(logArea.Size.Y / InkLayout.LogLineStep));
        var lines = model.LogLines;
        var start = System.Math.Max(0, lines.Count - capacity);
        for (var i = start; i < lines.Count; i++)
        {
            var y = logArea.Position.Y + (i - start) * InkLayout.LogLineStep;
            InkDraw.TextFitted(ci, new Vector2(content.Position.X + 6f, y),
                lines[i], usable, LogFontMax, LogFontMin, InkStyle.Line);
        }

        if (!model.LogExpanded)
        {
            // 当前位置：直接写标准地名，不再加“此处 · ”前缀。
            var hereWidth = content.Size.X - InkLayout.TitleInset * 2f;
            InkDraw.TextFitted(ci,
                new Vector2(content.Position.X + InkLayout.TitleInset,
                    content.Position.Y + InkLayout.HereTitleOffset),
                model.PlaceName, hereWidth, InkLayout.TitleFontSize, HereFontMin, InkStyle.Line);

            // 地名行下饰：与标题下饰同款（无珠），缩进也与标题一致。
            InkFrame.FadingRule(ci, content.Position.X + InkLayout.TitleInset,
                content.End.X - InkLayout.TitleInset, InkLayout.HereRuleY);

            for (var i = 0; i < model.Fixtures.Count; i++)
                DrawFixtureRow(ci, model.Fixtures[i], InkLayout.FixtureCell(i, model.Fixtures.Count));

            if (model.Fixtures.Count == 0)
            {
                InkDraw.Text(ci,
                    new Vector2(content.Position.X + 16f, content.Position.Y + InkLayout.HereEmptyHintOffset),
                    "（这里没有可用的设施）", 26, InkStyle.Dim);
            }
        }

        // 右下角开关：▼ 收起设施段铺满日志，▲ 还原两段布局。
        InkFrame.Button(ci, InkLayout.LogToggleArrow(model.LogExpanded), model.LogExpanded ? "▲" : "▼",
            fontSize: 26, centered: true);
    }

    /// <summary>
    /// 日志面板标题行右侧同级状态栏：季节、天气、时刻、金钱。
    /// 与“日志”标题同基线同排（Y = LogPanel.Position.Y + 22f），字号 26。
    /// </summary>
    private static void DrawHeaderStatus(CanvasItem ci, InkHubModel model)
    {
        var items = model.HeaderItems;
        if (items == null || items.Count == 0)
            return;

        var rightX = InkLayout.LogPanel.End.X - InkLayout.Pad - InkLayout.TitleInset;
        var y = InkLayout.LogPanel.Position.Y + 22f;
        const int statusFontSize = 26;
        const float gap = 20f;

        var curX = rightX;
        for (var i = items.Count - 1; i >= 0; i--)
        {
            var text = items[i].Value;
            if (string.IsNullOrEmpty(text))
                continue;
            var w = InkDraw.Measure(text, statusFontSize).X;
            InkDraw.Text(ci, new Vector2(curX - w, y), text, statusFontSize, InkStyle.Line, "lt");
            curX -= w + gap;
        }
    }

    private static void DrawFixtureRow(CanvasItem ci, FixtureRow row, Rect2 rect)
    {
        if (row.Fixture.PlayerHere)
            ci.DrawRect(rect, new Color(InkStyle.Line, 0.10f));

        // 槽位与“你在此”先占好右侧，名称在剩余宽度里自适应。
        var slotsWidth = row.Capacity * SlotStep + 8f;
        var youWidth = row.Fixture.PlayerHere ? 92f : 0f;
        var nameWidth = rect.Size.X - 24f - slotsWidth - youWidth;

        var nameSize = InkDraw.FitSize($"◇ {row.Fixture.Name}", nameWidth,
            RowFontMax, RowFontMin);
        InkDraw.Text(ci, new Vector2(rect.Position.X + 16f, rect.GetCenter().Y),
            $"◇ {row.Fixture.Name}", nameSize, InkStyle.Line, "lm");

        // 槽位：从右往左排。已占用的画点亮的角色棋子，未占用的画暗色小兵（上限标识）。
        var baseX = rect.End.X - 16f - SlotStep;
        for (var k = 0; k < row.Capacity; k++)
        {
            var x = baseX - (row.Capacity - 1 - k) * SlotStep;
            var basePt = new Vector2(x, rect.GetCenter().Y + 9f);

            if (k < row.Used)
            {
                // 已占用：点亮的角色棋子（仅单色白）
                InkDraw.ChessPiece piece;
                if (row.Occupants != null && k < row.Occupants.Count)
                {
                    piece = InkDraw.PieceFor(row.Occupants[k]);
                }
                else if (row.Fixture.PlayerHere && k == 0)
                {
                    piece = InkDraw.ChessPiece.King;
                }
                else
                {
                    piece = InkDraw.ChessPiece.Pawn;
                }
                InkDraw.Chess(ci, basePt, SlotFigureHeight, piece, isLimitCap: false);
            }
            else
            {
                // 未占用：上限标识用单色白点虚线小兵棋子
                InkDraw.Chess(ci, basePt, SlotFigureHeight, InkDraw.ChessPiece.Pawn, isLimitCap: true);
            }
        }

        if (row.Fixture.PlayerHere)
        {
            InkDraw.Text(ci,
                new Vector2(baseX - (row.Capacity - 1) * SlotStep - 14f, rect.GetCenter().Y),
                "▶ 你在此", 20, InkStyle.Line, "rm");
        }
    }
}
