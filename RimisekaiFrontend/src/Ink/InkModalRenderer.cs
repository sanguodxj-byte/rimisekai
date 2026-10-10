using System;
using System.Collections.Generic;
using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 通用居中自适应弹窗渲染器。
/// 严格遵循单色铜版线稿风格、无冒号、全实心箭头铁律。
/// </summary>
public static class InkModalRenderer
{
    public static void Draw(CanvasItem ci, InkModalPage page, float animTime)
    {
        var layout = InkLayout.CalculateModalLayout(page);

        // 1. 全屏半透明遮罩
        ci.DrawRect(new Rect2(0, 0, InkLayout.CanvasWidth, InkLayout.CanvasHeight),
            new Color(InkStyle.Bg, 0.55f));

        // 2. 居中自适应面板底色与双线角花边框
        ci.DrawRect(layout.PanelRect, InkStyle.Panel);
        InkFrame.Panel(ci, layout.PanelRect);

        // 3. 标题与渐隐分割线（可选：标题文字居中，分割线居中渐隐）
        if (!string.IsNullOrEmpty(page.Title))
        {
            var titleSize = page.Settlement != null ? 28 : 26;
            InkDraw.TextBounded(ci, layout.TitleRect, page.Title, titleSize, 18, InkStyle.Line, "cm");
            InkFrame.FadingRule(ci, layout.TitleRect.Position.X, layout.TitleRect.End.X, layout.RuleY);

            // 战后结算副标题：轮数概览（22号大字）
            if (page.Settlement != null)
            {
                var centerX = layout.PanelRect.GetCenter().X;
                InkDraw.Text(ci, new Vector2(centerX, layout.RuleY + 20f),
                    $"历经 {page.Settlement.Rounds} 回合战斗", 22, InkStyle.Dim, "cm");
            }
        }

        // 4. 战后结算结构化展示 或 普通多行折行正文
        if (page.Settlement != null)
        {
            DrawSettlement(ci, page.Settlement, layout);
        }
        else if (layout.BodyLines.Count > 0)
        {
            for (var i = 0; i < layout.BodyLines.Count; i++)
            {
                var lineRect = new Rect2(
                    layout.BodyRect.Position.X,
                    layout.BodyRect.Position.Y + i * 32f,
                    layout.BodyRect.Size.X,
                    30f);
                var text = layout.BodyLines[i].Trim();
                InkDraw.TextBounded(ci, lineRect, text, 22, 16, InkStyle.Line, "cm");
            }
        }

        // 5. 输入框（可选）
        if (page.Input != null)
        {
            DrawInputField(ci, page.Input, layout.InputRect);
        }

        // 6. 选项按钮列表（可选：按钮文字一律完全居中）
        if (page.Choices.Count > 0 && layout.ChoiceRects.Count == page.Choices.Count)
        {
            for (var i = 0; i < page.Choices.Count; i++)
            {
                var choice = page.Choices[i];
                var rect = layout.ChoiceRects[i];
                InkFrame.Button(ci, rect, choice.Label, false, choice.Enabled, 22, centered: true, beads: false);
            }
        }

        // 7. 底部永远存在的向下的三角实心箭头（明暗交替呼吸动效，完全居中）
        // 铁律（主人定）：实心填充，全项目严禁出现空心图像！
        DrawSolidBlinkingArrow(ci, layout.ArrowTip, animTime);
    }

    private static void DrawSettlement(CanvasItem ci, InkModalSettlementData data, InkModalLayout layout)
    {
        var centerX = layout.PanelRect.GetCenter().X;
        var curY = layout.SettlementStartY;

        // 四列排布：击坠移除，角色名就位并带等级标识（如“玩家 Lv.1”）；武器和流派改用 [图标 Lv.N 进度条 +增量]
        // Col 1: 角色姓名 Lv.N
        // Col 2: 造成伤害
        // Col 3: 武器 (图标 Lv.N 进度条)
        // Col 4: 流派 (图标 Lv.N 进度条)
        var col1 = centerX - 255f;
        var col2 = centerX - 90f;
        var col3 = centerX + 85f;
        var col4 = centerX + 255f;

        // 1. 参战人员列表（每名角色单行）
        for (var i = 0; i < data.Rows.Count; i++)
        {
            var row = data.Rows[i];
            var rowY = curY + i * 46f;

            // 成员间分割细线（第二位及以后）：横贯内容列区，居中
            if (i > 0)
            {
                InkDraw.InkLine(ci, new Vector2(centerX - 300f, rowY - 5f),
                    new Vector2(centerX + 300f, rowY - 5f),
                    new Color(InkStyle.Dim, 0.25f), 0.8f, 0.2f, 9500 + i);
            }

            var itemY = rowY + 16f;

            // Col 1 (原击坠位): 角色姓名 + 等级标识（如“玩家 Lv.1”）
            DrawNameLevel(ci, col1, itemY, row.Name, row.Level);

            // Col 2: 造成伤害（暗标签 20 + 亮数 22）
            DrawLabelValue(ci, col2, itemY, "造成伤害", row.DamageDealt.ToString(), 20, 22);

            // Col 3: 武器（图标 Lv.N 进度条 +增量）
            DrawIconMeter(ci, col3, itemY, row.WeaponName, row.WeaponLevel, row.WeaponRatio, row.WeaponExp);

            // Col 4: 流派（图标 Lv.N 进度条 +增量）
            DrawIconMeter(ci, col4, itemY, row.StyleName, row.StyleLevel, row.StyleRatio, row.StyleExp);
        }

        // 2. 战利品区：居中渐隐细线 + 小标题 + 金钱文字（不画框）+ 每件物品各一个带边框格子
        if (layout.SettlementLootStartY > 0f)
        {
            var lootTop = layout.SettlementLootStartY;

            // 战利品区顶部渐隐细线，居中
            InkFrame.FadingRule(ci, centerX - 280f, centerX + 280f, lootTop);

            // 小标题
            InkDraw.Text(ci, new Vector2(centerX, lootTop + 18f), "缴获战利品", 22, InkStyle.Dim, "cm");

            var curLootY = lootTop + InkLayout.SettlementLootHeadingH;

            // 金钱：单列居中文字，不画框
            if (data.Money > 0)
            {
                DrawLabelValue(ci, centerX, curLootY + InkLayout.SettlementLootGoldH / 2f,
                    "金钱", $"+{data.Money}G", 22, 24);
                curLootY += InkLayout.SettlementLootGoldH + 12f;
            }

            // 物品：每件一个带边框格子，整排居中，格间等距
            var itemCount = data.Items.Count;
            if (itemCount > 0)
            {
                for (var i = 0; i < itemCount; i++)
                {
                    var box = InkLayout.SettlementLootItemBox(centerX, curLootY, i);
                    InkFrame.Card(ci, box);

                    var item = data.Items[i];
                    // 格内单行（每个物品一列）：左侧名称（亮白 20），右侧数量（亮白 22）
                    var cy = box.GetCenter().Y;
                    InkDraw.Text(ci, new Vector2(box.Position.X + 20f, cy),
                        item.Label, 20, InkStyle.Line, "lm");
                    InkDraw.Text(ci, new Vector2(box.End.X - 20f, cy),
                        $"×{item.Count}", 22, InkStyle.Line, "rm");
                }
            }
        }
    }

    /// <summary>角色名字 + 等级标识（如“玩家 Lv.1”），以 columnCenterX 为锚点整组居中。</summary>
    private static void DrawNameLevel(CanvasItem ci, float columnCenterX, float y, string name, int level)
    {
        var levelText = $"Lv.{level}";
        const float gap = 8f;
        var nameW = InkDraw.Measure(name, 22).X;
        var lvlW = InkDraw.Measure(levelText, 18).X;
        var total = nameW + gap + lvlW;
        var startX = columnCenterX - total / 2f;
        InkDraw.Text(ci, new Vector2(startX, y), name, 22, InkStyle.Line, "lm");
        InkDraw.Text(ci, new Vector2(startX + nameW + gap, y), levelText, 18, InkStyle.Dim, "lm");
    }

    /// <summary>图标 + 等级标识 + 经验进度条组件：[图标] Lv.N [进度条] +N，以 columnCenterX 为锚点居中。</summary>
    private static void DrawIconMeter(CanvasItem ci, float columnCenterX, float y,
        string iconName, int level, float ratio, int gainedExp)
    {
        const float iconSize = 22f;
        const float meterW = 50f;
        const float meterH = 10f;
        const float gap = 6f;
        var lvlText = $"Lv.{level}";
        var lvlW = InkDraw.Measure(lvlText, 18).X;
        var expText = gainedExp > 0 ? $"+{gainedExp}" : "";
        var expW = expText.Length > 0 ? InkDraw.Measure(expText, 18).X : 0f;

        var totalW = iconSize + gap + lvlW + gap + meterW + (expW > 0 ? gap + expW : 0f);
        var curX = columnCenterX - totalW / 2f;

        // 1. 图标 (22x22)
        var iconRect = new Rect2(curX, y - iconSize / 2f, iconSize, iconSize);
        InkIcon.Draw(ci, iconName, iconRect, showBorder: false);
        curX += iconSize + gap;

        // 2. 等级标识 (Lv.1)
        InkDraw.Text(ci, new Vector2(curX, y), lvlText, 18, InkStyle.Dim, "lm");
        curX += lvlW + gap;

        // 3. 经验进度条 (50x10，平滑动态推进)
        var meterRect = new Rect2(curX, y - meterH / 2f, meterW, meterH);
        InkDynamicMeter.Draw(ci, $"modal_{iconName}_{meterRect.GetHashCode()}", meterRect, ratio, isHp: false);
        curX += meterW + gap;

        // 4. 经验增量文字 (+30)
        if (expW > 0)
        {
            InkDraw.Text(ci, new Vector2(curX, y), expText, 18, InkStyle.Line, "lm");
        }
    }

    /// <summary>
    /// 一列「暗标签 + 亮数值」：以列心为锚点整组居中，标签与数值间留固定间隙。
    /// 同一列在所有行共用同一个列心，因此各行严格竖直对齐。
    /// </summary>
    private static void DrawLabelValue(CanvasItem ci, float columnCenterX, float y,
        string label, string value, int labelSize = 20, int valueSize = 24)
    {
        const float gap = 8f;
        var labelW = InkDraw.Measure(label, labelSize).X;
        var valueW = InkDraw.Measure(value, valueSize).X;
        var total = labelW + gap + valueW;
        var startX = columnCenterX - total / 2f;
        InkDraw.Text(ci, new Vector2(startX, y), label, labelSize, InkStyle.Dim, "lm");
        InkDraw.Text(ci, new Vector2(startX + labelW + gap, y), value, valueSize, InkStyle.Line, "lm");
    }

    private static void DrawInputField(CanvasItem ci, InkModalInput input, Rect2 rect)
    {
        ci.DrawRect(rect, InkStyle.Inset);
        InkDraw.Ink(ci, new[]
        {
            rect.Position,
            new Vector2(rect.End.X, rect.Position.Y),
            rect.End,
            new Vector2(rect.Position.X, rect.End.Y),
            rect.Position,
        }, InkStyle.Line, 1.4f, 0.4f, 9301);

        var text = input.Text;
        if (text.Length == 0 && !string.IsNullOrEmpty(input.Placeholder))
        {
            InkDraw.TextBounded(ci, rect, input.Placeholder, 22, 16, InkStyle.Dim, "cm");
        }
        else
        {
            var maxW = rect.Size.X - 28f;
            var size = InkDraw.FitSize(text, maxW, 24, 16);
            while (text.Length > 1 && InkDraw.Measure(text, size).X > maxW)
                text = text[1..];

            InkDraw.Text(ci, rect.GetCenter(), text, size, InkStyle.Line, "cm");

            if (input.Focused)
            {
                var textHalfW = InkDraw.Measure(text, size).X / 2f;
                var caretX = rect.GetCenter().X + textHalfW + 3f;
                InkDraw.InkLine(ci,
                    new Vector2(caretX, rect.GetCenter().Y - 13f),
                    new Vector2(caretX, rect.GetCenter().Y + 13f),
                    InkStyle.Line, 1.6f, 0.2f, 9302);
            }
        }
    }

    private static void DrawSolidBlinkingArrow(CanvasItem ci, Vector2 tip, float animTime)
    {
        const float halfW = 10f;
        const float h = 9f;
        var p1 = new Vector2(tip.X - halfW, tip.Y - h);
        var p2 = new Vector2(tip.X + halfW, tip.Y - h);

        // 呼吸周期约 1.8 秒，亮度在 0.28 与 1.0 之间舒缓往复
        var pulse = 0.5f + 0.5f * Mathf.Sin(animTime * 3.14159f);
        var alpha = 0.28f + 0.72f * pulse;
        var col = new Color(InkStyle.Line, alpha);

        var poly = new[] { p1, p2, tip };

        // 铁律（主人定）：实心填充，全项目严禁出现空心图像！
        ci.DrawColoredPolygon(poly, col);
    }
}
