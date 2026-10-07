using Godot;

namespace Rimisekai.Ink;

public static class InkRenameRenderer
{
    public static void Draw(CanvasItem ci, InkHubModel model)
    {
        ci.DrawRect(new Rect2(0, 0, InkLayout.CanvasWidth, InkLayout.CanvasHeight),
            new Color(InkStyle.Bg, 0.55f));

        var panel = InkLayout.RenamePanel;
        ci.DrawRect(panel, InkStyle.Panel);
        InkFrame.Panel(ci, panel);

        InkDraw.TextBounded(ci, InkLayout.RenameTitle,
            "给领地取名", 26, 18, InkStyle.Line, "lm");

        InkDraw.TextBounded(ci, InkLayout.RenameHint,
            $"回车确定　Esc 取消　最多 {InkLayout.RenameMaxChars} 字", 18, 14, InkStyle.Dim, "lm");

        DrawField(ci, model);

        InkFrame.Button(ci, InkLayout.RenameButton(0), "确定", false, true, 22, centered: true);
        InkFrame.Button(ci, InkLayout.RenameButton(1), "取消", false, true, 22, centered: true);
    }

    private static void DrawField(CanvasItem ci, InkHubModel model)
    {
        var rect = InkLayout.RenameField;
        ci.DrawRect(rect, InkStyle.Inset);
        InkDraw.Ink(ci, new[]
        {
            rect.Position,
            new Vector2(rect.End.X, rect.Position.Y),
            rect.End,
            new Vector2(rect.Position.X, rect.End.Y),
            rect.Position,
        }, InkStyle.Line, 1.4f, 0.4f, 9101);

        var text = model.RenameText;
        if (text.Length == 0)
        {
            InkDraw.TextBounded(ci, new Rect2(rect.Position.X + 14f, rect.Position.Y, rect.Size.X - 28f, rect.Size.Y),
                "（输入名字）", 22, 16, InkStyle.Dim, "lm");
            return;
        }

        var maxW = rect.Size.X - 28f;
        var size = InkDraw.FitSize(text, maxW, 24, 16);
        while (text.Length > 1 && InkDraw.Measure(text, size).X > maxW)
            text = text[1..];

        InkDraw.Text(ci, new Vector2(rect.Position.X + 14f, rect.GetCenter().Y),
            text, size, InkStyle.Line, "lm");

        var caretX = rect.Position.X + 14f + InkDraw.Measure(text, size).X + 3f;
        InkDraw.InkLine(ci, new Vector2(caretX, rect.GetCenter().Y - 13f),
            new Vector2(caretX, rect.GetCenter().Y + 13f), InkStyle.Line, 1.6f, 0.2f, 9102);
    }
}
