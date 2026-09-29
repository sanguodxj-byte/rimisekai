using Godot;

namespace Rimisekai.Ink;

/// <summary>角色栏：头像卡、翻页三角、下方页面入口。</summary>
public static class InkCharRenderer
{
    private const int NameFontMax = 22;
    private const int NameFontMin = 14;

    /// <summary>头像占卡片的高度比例。剩下的空间给名字。</summary>
    private const float FigureHeightRatio = 0.58f;

    public static void Draw(CanvasItem ci, InkHubModel model)
    {
        InkFrame.Title(ci, InkLayout.CharPanel, "角色", 26);

        for (var i = 0; i < model.Cards.Count; i++)
            DrawCard(ci, model.Cards[i], InkLayout.Card(i));

        DrawPageNext(ci, model);

        var entries = InkViewModel.PageEntries;
        for (var i = 0; i < entries.Length; i++)
        {
            InkFrame.Button(ci, InkLayout.PageEntry(i, entries.Length),
                InkPageModel.Info(entries[i]).Label, false, true, 22);
        }
    }

    private static void DrawCard(CanvasItem ci, CardView view, Rect2 rect)
    {
        // 位置未知（RoomId == -1）且不是玩家本人的，画成暗色。
        var present = view.Card.RoomId >= 0 || view.Card.IsPlayer;
        var color = present ? InkStyle.Line : InkStyle.Dim;

        InkFrame.Card(ci, rect, color, view.Selected,
            view.Selected ? InkStyle.Bg.Lightened(0.07f) : null);

        // 名字先占好底部一条，头像在剩余区域里垂直居中，避免头像压到名字。
        var nameBand = 34f;
        var figureArea = new Rect2(
            rect.Position.X,
            rect.Position.Y + 8f,
            rect.Size.X,
            rect.Size.Y - nameBand - 12f);

        var figureH = figureArea.Size.Y * FigureHeightRatio * 1.6f;
        var figureFootY = figureArea.GetCenter().Y + figureH * 0.5f;

        InkDraw.Figure(ci, rect.GetCenter().X, figureFootY, figureH, color, 17);

        var nameSize = InkDraw.FitSize(view.Card.Name, rect.Size.X - 16f,
            NameFontMax, NameFontMin);
        InkDraw.Text(ci, new Vector2(rect.GetCenter().X, rect.End.Y - nameBand / 2f - 6f),
            view.Card.Name, nameSize, color, "cm");
    }

    /// <summary>
    /// 翻页三角。画在预留竖条的正中，用等边三角形而不是锐角，
    /// 并带页数标记，避免“不知道在第几页”。
    /// </summary>
    private static void DrawPageNext(CanvasItem ci, InkHubModel model)
    {
        if (model.CardPageCount <= 1)
            return;

        var area = InkLayout.PageNext;
        var cx = area.GetCenter().X;
        var cy = area.GetCenter().Y - 12f;

        // 等边三角形：半高 = 半宽 × √3，视觉上比细长锐角稳。
        const float halfW = 11f;
        var halfH = halfW * 1.732f;

        InkDraw.Ink(ci, new[]
        {
            new Vector2(cx - halfW, cy - halfH),
            new Vector2(cx + halfW, cy),
            new Vector2(cx - halfW, cy + halfH),
            new Vector2(cx - halfW, cy - halfH),
        }, InkStyle.Line, 2f, 0.5f, 9911);

        InkDraw.Text(ci, new Vector2(cx, cy + halfH + 22f),
            $"{model.CardPage + 1}/{model.CardPageCount}", 18, InkStyle.Dim, "cm");
    }
}
