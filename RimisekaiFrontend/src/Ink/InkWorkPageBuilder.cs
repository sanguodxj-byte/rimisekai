using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Housing;

namespace Rimisekai.Ink;

/// <summary>
/// 工作页：全员矩阵。行 = 工作类型，列 = 角色，格子里是优先级（空白 / 1-4）。
/// 与日程页分工：日程决定"这会儿上不上工"，工作页决定"上工时先干哪样"。
/// 只读构建，点的每一下都由 InkHubScreen 转成 HubSession 调用。
/// </summary>
public static class InkWorkPageBuilder
{
    /// <summary>点击格子时循环的档位：空白 → 1 → 2 → 3 → 4 → 空白。</summary>
    public static int NextPriority(int current) =>
        current >= ActionKindMap.MaxPriority ? 0 : current + 1;

    public static InkPageModel Build(InkViewModel vm, int selectedRow, int selectedColumn)
    {
        var hub = vm.Hub;
        var columns = new List<InkWorkColumn>(vm.WorkColumns());

        var rows = new List<InkWorkRow>();
        foreach (var task in ActionKindMap.WorkOrdered)
        {
            var cells = new List<InkWorkCell>();
            foreach (var column in columns)
            {
                var who = hub.State.Roster.Find(column.CharacterId);
                cells.Add(new InkWorkCell(
                    hub.PriorityOf(column.CharacterId, task),
                    who == null ? 0 : who.Life(ActionKindMap.SkillOf(task)!.Value)));
            }
            rows.Add(new InkWorkRow
            {
                Task = task,
                Name = InkText.ActionKind(task),
                SkillName = InkText.LifeSkill(ActionKindMap.SkillOf(task)!.Value),
                Cells = cells,
            });
        }

        var row = selectedRow >= 0 && selectedRow < rows.Count ? selectedRow : -1;
        var col = row >= 0 && selectedColumn >= 0 && selectedColumn < columns.Count ? selectedColumn : -1;
        var actions = new List<InkPageRow>();
        var title = "";
        var note = "";
        if (row >= 0 && col >= 0)
        {
            var workRow = rows[row];
            var column = columns[col];
            var cell = workRow.Cells[col];
            title = $"{column.Name}　{workRow.Name}";
            note = $"技能　{workRow.SkillName} {cell.Skill}\n" +
                   $"优先级　{(cell.Assigned ? InkText.Priority(cell.Priority) : "空白（不做）")}";
            // 点一下循环到下一档：空白 → 1 → 2 → 3 → 4 → 空白。
            var next = NextPriority(cell.Priority);
            actions.Add(new InkPageRow
            {
                Name = next <= 0 ? "设为不做" : $"设为 {next}",
                Action = InkPageAction.SetWorkPriority,
                TargetNumber = row,
                TargetId = column.CharacterId.ToString(),
                Enabled = true,
            });
        }

        return new InkPageModel
        {
            Page = InkPage.Work,
            Title = "工作",
            DetailTitle = title,
            DetailNote = note,
            DetailActions = actions,
            EmptyHint = columns.Count == 0 ? "据点里还没有可以派活的人。" : "",
            Filters = System.Array.Empty<string>(),
            Sorts = System.Array.Empty<string>(),
            Work = new InkWorkModel
            {
                Columns = columns,
                Rows = rows,
                SelectedRow = row,
                SelectedColumn = col,
                DetailTitle = title,
                DetailNote = note,
                DetailActions = actions,
            },
        };
    }
}
