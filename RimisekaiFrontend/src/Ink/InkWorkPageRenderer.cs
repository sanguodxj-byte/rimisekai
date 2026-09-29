using Godot;
using Rimisekai.Housing;

namespace Rimisekai.Ink;

/// <summary>
/// 工作页渲染：全员矩阵。行 = 工作类型（含技能标注），列 = 角色。
/// 格子里是优先级（空白 / 1-4），已派的格子加亮；底部是选中格的说明与改档按钮。
/// 画法与命中判定共用 <see cref="InkLayout"/> 的同一套矩形。
/// </summary>
public static class InkWorkPageRenderer
{
    private const int RowFont = 19;
    private const int HeaderFont = 19;
    private const int CellFont = 22;
    private const int NoteFont = 15;

    public static void Draw(CanvasItem ci, InkHubModel model)
    {
        var work = model.Page?.Work;
        if (work == null)
            return;

        if (work.Columns.Count == 0 || work.Rows.Count == 0)
        {
            InkDraw.TextBounded(ci, InkLayout.SubpageBody, "据点里还没有可以派活的人。",
                20, 14, InkStyle.Dim, "cm");
            return;
        }

        var columns = work.Columns.Count;

        // 表头：角色名。
        for (var c = 0; c < columns; c++)
        {
            var rect = InkLayout.WorkColumnHeader(c, columns);
            var name = work.Columns[c].Name;
            var size = InkDraw.FitSize(name, rect.Size.X - 8f, HeaderFont, 12);
            InkDraw.Text(ci, rect.GetCenter(), name, size, InkStyle.Line, "cm");
        }

        // 行：工作类型 + 每列的格子。
        for (var r = 0; r < work.Rows.Count; r++)
        {
            var row = work.Rows[r];
            var header = InkLayout.WorkRowHeader(r, columns);
            InkDraw.Text(ci, new Vector2(header.Position.X, header.GetCenter().Y),
                row.Name, RowFont, InkStyle.Line, "lm");
            // 技能名标在行头右侧，留出与格子之间的空隙，免得看成格子的一部分。
            InkDraw.Text(ci, new Vector2(header.End.X - 26f, header.GetCenter().Y),
                row.SkillName, NoteFont, InkStyle.Dim, "rm");

            for (var c = 0; c < columns; c++)
            {
                var cell = InkLayout.WorkCell(r, c, columns);
                var value = row.Cells[c];
                var selected = r == work.SelectedRow && c == work.SelectedColumn;
                // 已派的格子浅填，选中的格子加框——与列表行的选中画法同源。
                InkFrame.Button(ci, cell, value.Label, selected, true, CellFont, centered: true);
                // 技能值：贴在格子左侧内缘，与居中的档位数字分开，不压框线。
                var shade = value.Skill >= 60 ? InkStyle.Line
                    : value.Skill >= 30 ? InkStyle.Dim
                    : new Color(InkStyle.Dim, 0.55f);
                InkDraw.Text(ci, new Vector2(cell.Position.X + 14f, cell.GetCenter().Y),
                    value.Skill.ToString(), 16, shade, "lm");
            }
        }

        DrawDetail(ci, work);
    }

    /// <summary>底部：选中格的说明与改档按钮。未选中时不画。</summary>
    private static void DrawDetail(CanvasItem ci, InkWorkModel work)
    {
        if (work.DetailTitle.Length == 0 && work.DetailActions.Count == 0)
            return;

        var pane = InkLayout.WorkDetail;
        InkDraw.FoldedBox(ci, pane, InkStyle.Line, fold: 18f, fill: true, seed: 8910);

        var left = pane.Position.X + 28f;
        InkDraw.Text(ci, new Vector2(left, pane.Position.Y + 30f),
            work.DetailTitle, 22, InkStyle.Line, "lm");
        var lines = work.DetailNote.Split('\n');
        for (var i = 0; i < lines.Length; i++)
            InkDraw.Text(ci, new Vector2(left, pane.Position.Y + 62f + i * 28f),
                lines[i], NoteFont, InkStyle.Dim, "lm");

        for (var a = 0; a < work.DetailActions.Count; a++)
        {
            var row = work.DetailActions[a];
            var rect = InkLayout.DetailButton(pane, a, work.DetailActions.Count);
            InkFrame.Button(ci, rect, row.Name, row.Selected, row.Enabled, 20);
        }
    }
}
