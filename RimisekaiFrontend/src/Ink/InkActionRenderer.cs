using System.Linq;
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
        if (model.InCombat)
        {
            // 战斗形态：4x3 布局，只有技能和道具有二级页面，攻击和防御点击直接生效或选目标
            // 第 0 列：四大行动/类别按钮
            for (var r = 0; r < 4; r++)
            {
                var widget = model.Find(InkAction.CombatCategory, r);
                if (widget != null)
                {
                    // 攻击选中：当前已装配普通攻击；技能/道具选中：当前已展开对应二级页面
                    var isSelected = r switch
                    {
                        0 => model.ArmedSkillId == Rimisekai.Combat.BattleSkills.AttackId,
                        1 => model.CombatCategory == 1,
                        2 => model.CombatCategory == 2,
                        _ => false
                    };
                    // 主人定：行动面板按钮左右布局，效果和威力写在右侧右对齐，名称换到左侧左对齐
                    var catFill = isSelected ? new Color(InkStyle.Line, 0.12f) : (Color?)Colors.Transparent;
                    InkFrame.Button(ci, InkLayout.SocialGridButton(0, r),
                        widget.Value.Label, isSelected, widget.Value.Enabled, 22,
                        value: widget.Value.Value, fill: catFill);
                }
            }

            // 只有技能(1)与道具(2)在第 1 列与第 2 列展开二级子项；攻击与防御没有二级页面
            if (model.CombatCategory is 1 or 2)
            {
                var subWidgets = model.Widgets
                    .Where(w => w.Action is InkAction.CombatSkill or InkAction.CombatItem)
                    .ToList();

                for (var i = 0; i < subWidgets.Count && i < 8; i++)
                {
                    var col = 1 + i / 4;
                    var row = i % 4;
                    var rect = InkLayout.SocialGridButton(col, row);
                    var w = subWidgets[i];
                    // 若当前技能正是已选定装配的技能，高亮为选中态
                    var isSkillArmed = w.Action == InkAction.CombatSkill && !string.IsNullOrEmpty(model.ArmedSkillId);
                    // 战斗场景：右侧显示预计伤害；无预估量的技能不分区。
                    var subFill = isSkillArmed ? new Color(InkStyle.Line, 0.12f) : (Color?)Colors.Transparent;
                    InkFrame.Button(ci, rect, w.Label, isSkillArmed, w.Enabled, 22,
                        value: w.Value, fill: subFill);
                }
            }
            return;
        }

        if (model.SceneMode || model.Overlay != null)
        {
            var title = model.SceneMode
                ? (model.SceneTitle.Length > 0 ? model.SceneTitle : "演出")
                : (model.Overlay?.Speaker.Length > 0 ? $"交流·{model.Overlay.Speaker}" : "对话");
            InkFrame.TitleFitted(ci, InkLayout.ActPanel, title,
                InkLayout.ActPanel.Size.X - (InkFrame.Pad + 12f) * 2f, 26, 18);

            // 演出态：点击面板任意处推进；几何符号无操作教学文字（AGENTS.md 铁律）。
            InkDraw.Text(ci, new Vector2(InkLayout.ActPanel.GetCenter().X, InkLayout.ActPanel.End.Y - 28f),
                "▽", 18, InkStyle.Line, "cm");
            return;
        }

        if (model.ObservingRoom)
        {
            InkFrame.Title(ci, InkLayout.ActPanel, "观察", 26);
            DrawActionPanelSettings(ci, model);
            // 观察态：点击面板任意处退出（几何符号，无操作教学文字）
            InkDraw.Text(ci, new Vector2(InkLayout.ActPanel.GetCenter().X, InkLayout.ActPanel.End.Y - 28f),
                "▽", 26, InkStyle.Line, "cm");
            return;
        }

        if (model.ShowSocial)
        {
            // 标题与其余面板同一套几何（起点、字号、下饰线），只把宽度让给动态标题。
            InkFrame.TitleFitted(ci, InkLayout.ActPanel, model.SocialTitle,
                InkLayout.ActPanel.Size.X - (InkFrame.Pad + 12f) * 2f - 110f, 26, 26);
            DrawActionPanelSettings(ci, model);

            // 左列起始钮：点开的类别底色变暗（selected），子项从第 1 列竖排。
            for (var i = 0; i < InkViewModel.SocialCategories.Length; i++)
            {
                var widget = model.Find(InkAction.SocialCategory, i);
                var enabled = widget?.Enabled ?? false;
                InkFrame.Button(ci, InkLayout.SocialGridButton(0, i),
                    InkViewModel.SocialCategories[i].Label,
                    model.SocialCategory == i, enabled, 26, centered: true);
            }

            if (model.SocialCategory >= 0
                && InkViewModel.SocialCategories[model.SocialCategory].Children is { } children)
            {
                // 接触启发式：只画存档已解锁的步数，其余类别全量。
                var visible = model.SocialCategory == 1
                    ? Mathf.Min(children.Length, model.SocialTouchVisible)
                    : children.Length;
                for (var i = 0; i < visible; i++)
                {
                    var child = children[i];
                    var widget = model.Find(InkAction.Social, child.Index);
                    // 领地场景：右侧显示这回社交动作的耗时。
                    InkFrame.Button(ci, InkLayout.SocialGridButton(1 + i / 4, i % 4),
                        child.Label, false, widget?.Enabled ?? false, 26,
                        value: widget?.Value ?? "");
                }
            }

            return;
        }

        if (model.ShowFixtureActions)
        {
            // 坐在设施上：标题写设施名，按钮是它支持的行动。
            InkFrame.TitleFitted(ci, InkLayout.ActPanel, model.FixtureTitle,
                InkLayout.ActPanel.Size.X - (InkFrame.Pad + 12f) * 2f - 110f, 26, 26);
            DrawActionPanelSettings(ci, model);
            var count = CountWidgets(model, InkAction.FixtureAction);
            DrawButtons(ci, model, InkAction.FixtureAction, count,
                i => InkLayout.PlaceButton(i, count));
            DrawActionPanelExtras(ci, model);
            return;
        }

        InkFrame.Title(ci, InkLayout.ActPanel, "行动", 26);
        DrawActionPanelSettings(ci, model);

        foreach (var widget in model.Widgets)
        {
            if (widget.Action is InkAction.Place or InkAction.HubWorld or InkAction.RoomLock or InkAction.CrossRegion)
            {
                InkFrame.Button(ci, widget.Rect, widget.Label, false, widget.Enabled, 26,
                    value: widget.Value);
            }
        }
    }

    /// <summary>行动面板右上角设置按钮（字号与行动等大=26）。</summary>
    private static void DrawActionPanelSettings(CanvasItem ci, InkHubModel model)
    {
        var widget = model.Find(InkAction.HubSystem, 0);
        if (widget is { } w)
        {
            InkFrame.Button(ci, w.Rect, w.Label, false, w.Enabled, 26, centered: true);
        }
    }

    /// <summary>
    /// 行动面板里不挂在固定清单上的按钮——目前只有「去往&lt;地区&gt;」这一个。
    /// 它随状态出现/消失，位置也随同屏按钮数变化，
    /// 所以矩形直接用元素表里算好的那份，不再按 PlaceActions 的固定下标重推——
    /// 否则元素表里加了、屏幕上却画不出来。
    /// 只画 CrossRegion：别的动作（如 RoomLock）不归这里管，不要顺手加进来。
    /// </summary>
    private static void DrawActionPanelExtras(CanvasItem ci, InkHubModel model)
    {
        foreach (var widget in model.Widgets)
        {
            if (widget.Action != InkAction.CrossRegion)
                continue;
            InkFrame.Button(ci, widget.Rect, widget.Label, false, widget.Enabled, 26,
                value: widget.Value);
        }
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
            if (widget is not { } w)
                continue;
            InkFrame.Button(ci, rectOf(i), w.Label, false, w.Enabled, 26,
                value: w.Value);
        }
    }
}
