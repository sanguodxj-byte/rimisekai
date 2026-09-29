using Godot;

namespace Rimisekai.Ink;

/// <summary>日志与“此处”面板。</summary>
public static class InkLogRenderer
{
    private const int MaxLogLines = 4;

    private const int LogFontMax = 21;
    private const int LogFontMin = 14;

    private const int HereFontMax = 24;
    private const int HereFontMin = 16;

    private const int RowFontMax = 22;
    private const int RowFontMin = 14;

    private const float SlotStep = 30f;
    private const float SlotFigureHeight = 18f;

    public static void Draw(CanvasItem ci, InkHubModel model)
    {
        InkFrame.Title(ci, InkLayout.LogPanel, "日志", 26);

        var content = InkLayout.LogContent;
        var usable = content.Size.X - 12f;

        var lines = model.LogLines;
        var start = System.Math.Max(0, lines.Count - MaxLogLines);
        for (var i = start; i < lines.Count; i++)
        {
            var y = content.Position.Y + (i - start) * InkLayout.LogLineStep;
            InkDraw.TextFitted(ci, new Vector2(content.Position.X + 6f, y),
                lines[i], usable, LogFontMax, LogFontMin, InkStyle.Line);
        }

        // 当前位置：直接写标准地名，不再加“此处 · ”前缀。
        InkDraw.TextFitted(ci,
            new Vector2(content.Position.X + 6f, content.Position.Y + InkLayout.HereTitleOffset),
            model.PlaceName, usable, HereFontMax, HereFontMin, InkStyle.Line);

        for (var i = 0; i < model.Fixtures.Count; i++)
            DrawFixtureRow(ci, model.Fixtures[i], InkLayout.FixtureCell(i, model.Fixtures.Count));

        if (model.Fixtures.Count == 0)
        {
            InkDraw.Text(ci,
                new Vector2(content.Position.X + 16f, content.Position.Y + 200f),
                "（这里没有可用的设施）", 20, InkStyle.Dim);
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

        // 槽位：从右往左排，点亮的表示已占用。
        var baseX = rect.End.X - 16f - SlotStep;
        for (var k = 0; k < row.Capacity; k++)
        {
            var x = baseX - (row.Capacity - 1 - k) * SlotStep;
            InkDraw.MiniFigure(ci, new Vector2(x, rect.GetCenter().Y + 9f),
                SlotFigureHeight, k < row.Used ? InkStyle.Line : InkStyle.Dim);
        }

        if (row.Fixture.PlayerHere)
        {
            InkDraw.Text(ci,
                new Vector2(baseX - (row.Capacity - 1) * SlotStep - 14f, rect.GetCenter().Y),
                "▶ 你在此", 20, InkStyle.Line, "rm");
        }
    }
}
