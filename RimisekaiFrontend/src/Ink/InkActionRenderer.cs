using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 交流 / 行动面板。三态互斥：选中角色时“交流”，坐在设施上时这件设施的日常行动，
/// 都没占时房间级行动（观察 + 导航）。
/// </summary>
public static class InkActionRenderer
{
    public static void Draw(CanvasItem ci, InkHubModel model)
    {
        if (model.ShowSocial)
        {
            // 标题与其余面板同一套几何（起点、字号、下饰线），只把宽度让给动态标题。
            InkFrame.TitleFitted(ci, InkLayout.ActPanel, model.SocialTitle,
                InkLayout.ActPanel.Size.X - (InkFrame.Pad + 12f) * 2f, 26, 18);

            DrawButtons(ci, model, InkAction.Social, InkViewModel.SocialActions.Length,
                i => InkLayout.SocialButton(i, InkViewModel.SocialActions.Length));
            DrawNotice(ci, model);
            return;
        }

        if (model.ShowFixtureActions)
        {
            // 坐在设施上：标题写设施名，按钮是它支持的行动。
            InkFrame.TitleFitted(ci, InkLayout.ActPanel, model.FixtureTitle,
                InkLayout.ActPanel.Size.X - (InkFrame.Pad + 12f) * 2f, 26, 18);
            var count = CountWidgets(model, InkAction.FixtureAction);
            DrawButtons(ci, model, InkAction.FixtureAction, count,
                i => InkLayout.PlaceButton(i, count));
            DrawNotice(ci, model);
            return;
        }

        InkFrame.Title(ci, InkLayout.ActPanel, "行动", 26);

        DrawButtons(ci, model, InkAction.Place, InkViewModel.PlaceActions.Length,
            i => InkLayout.PlaceButton(i, InkViewModel.PlaceActions.Length));
        DrawNotice(ci, model);
    }

    /// <summary>元素表里某类元素的个数。画按钮时按它循环，避免与构建处各算一套。</summary>
    private static int CountWidgets(InkHubModel model, InkAction action)
    {
        var n = 0;
        foreach (var widget in model.Widgets)
        {
            if (widget.Action == action)
                n++;
        }
        return n;
    }

    /// <summary>
    /// 按元素表画按钮。可点状态直接取自元素表，
    /// 因此灰掉的按钮一定点不动，不会出现“看着禁用却能点”。
    /// </summary>
    private static void DrawButtons(CanvasItem ci, InkHubModel model,
        InkAction action, int count, System.Func<int, Rect2> rectOf)
    {
        for (var i = 0; i < count; i++)
        {
            var widget = model.Find(action, i);
            var enabled = widget?.Enabled ?? false;
            var label = widget?.Label ?? "";
            InkFrame.Button(ci, rectOf(i), label, false, enabled, 22);
        }
    }

    private static void DrawNotice(CanvasItem ci, InkHubModel model)
    {
        if (model.Notice.Length == 0)
            return;

        InkDraw.TextFitted(ci,
            new Vector2(InkLayout.ActContent.Position.X + 10f,
                InkLayout.ActContent.End.Y - 18f),
            model.Notice, InkLayout.ActContent.Size.X - 20f, 18, 14, InkStyle.Dim);
    }
}
