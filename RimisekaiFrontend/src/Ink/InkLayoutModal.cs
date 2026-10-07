using System.Collections.Generic;
using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 弹窗版式：通用居中自适应弹窗（含战后结算）的整块测算、改名弹窗。
/// 弹窗几何由内容算出来，不是写死的矩形——自适应高度因此与内容严丝合缝。
/// </summary>
public static partial class InkLayout
{
    // ---------- 改名弹窗 ----------

    /// <summary>改名弹窗：居中一块小面板。高度要容下标题、输入框、提示与按钮。</summary>
    public static readonly Rect2 RenamePanel = new(550, 360, 820, 340);
    public static readonly Rect2 RenameTitle = new(580, 388, 760, 40);
    public static readonly Rect2 RenameHint = new(580, 438, 760, 28);
    public static readonly Rect2 RenameField = new(580, 486, 760, 52);

    /// <summary>改名输入框允许的最大字符数。</summary>
    public const int RenameMaxChars = 12;

    public static Rect2 RenameButton(int slot)
    {
        const float w = 160f;
        const float gap = 20f;
        var total = w * 2f + gap;
        var x = RenamePanel.GetCenter().X - total / 2f + slot * (w + gap);
        return new Rect2(x, RenamePanel.End.Y - Pad - 52f, w, 46f);
    }

    // ---------- 战后结算：战利品区 ----------

    /// <summary>战利品区小标题行高。</summary>
    public const float SettlementLootHeadingH = 32f;
    /// <summary>金钱行高（不画框，单列文字）。</summary>
    public const float SettlementLootGoldH = 32f;
    /// <summary>每个物品各占一个带边框的条框（竖向一列排布，左侧名称，右侧数量）。</summary>
    public const float SettlementLootItemBoxW = 460f;
    public const float SettlementLootItemBoxH = 40f;
    public const float SettlementLootItemGap = 8f;

    /// <summary>战利品区的总高度：小标题 + （有金币则一行文字）+ （每个物品一个框，竖向一列排布）。</summary>
    public static float SettlementLootSectionHeight(long money, int itemCount)
    {
        var hasGold = money > 0;
        if (!hasGold && itemCount <= 0)
            return 0f;
        var h = SettlementLootHeadingH;
        if (hasGold)
            h += SettlementLootGoldH + 8f;
        if (itemCount > 0)
            h += itemCount * SettlementLootItemBoxH + (itemCount - 1) * SettlementLootItemGap;
        return h;
    }

    /// <summary>某件物品的带边框格子：竖向一列排布（第 index 个），水平居中。</summary>
    public static Rect2 SettlementLootItemBox(float centerX, float top, int index)
    {
        var x = centerX - SettlementLootItemBoxW / 2f;
        var y = top + index * (SettlementLootItemBoxH + SettlementLootItemGap);
        return new Rect2(x, y, SettlementLootItemBoxW, SettlementLootItemBoxH);
    }

    // ---------- 通用居中弹窗（InkModal） ----------

    /// <summary>弹窗固定宽度（画面居中）。</summary>
    public const float ModalWidth = 800f;
    public const float ModalPadX = 36f;
    public const float ModalPadTop = 28f;
    public const float ModalPadBottom = 22f;
    public const float ModalArrowBand = 28f;

    /// <summary>根据弹窗内容自适应测算面板矩形及内部控件几何布局。全要素完全居中，内部【文字+按钮】在标题与底部之间完全上下垂直居中！</summary>
    public static InkModalLayout CalculateModalLayout(InkModalPage page)
    {
        const float maxContentWidth = 680f;

        var hasTitle = !string.IsNullOrEmpty(page.Title);
        var titleH = hasTitle ? 48f : 0f;

        var isSettlement = page.Settlement != null;

        // 1. 测算中间内容块各组件的高度与部件间距
        var bodyLines = string.IsNullOrEmpty(page.Body)
            ? System.Array.Empty<string>()
            : InkDraw.WrapLines(page.Body, maxContentWidth, 22);
        var bodyH = (!isSettlement && bodyLines.Count > 0) ? bodyLines.Count * 32f : 0f;

        var hasInput = page.Input != null;
        var inputH = hasInput ? 48f : 0f;

        var choiceCount = page.Choices.Count;
        var choicesH = 0f;
        if (choiceCount is 1 or 2)
            choicesH = 46f;
        else if (choiceCount > 2)
            choicesH = choiceCount * 44f + (choiceCount - 1) * 10f;

        // 计算相邻部件间的纵向自然间隔（文字到输入框、文字到按钮）：
        var contentItemCount = (bodyH > 0 ? 1 : 0) + (hasInput ? 1 : 0) + (choicesH > 0 ? 1 : 0);
        var innerGaps = contentItemCount > 1 ? (contentItemCount - 1) * 20f : 0f;
        var generalContentH = bodyH + inputH + choicesH + innerGaps;

        // 战后结算界面的精确内容高度测算（墨水与盒子尺寸严密一致）：
        var settlementRowsH = 0f;
        var lootH = 0f;
        var settlementContentH = 0f;
        if (isSettlement)
        {
            var s = page.Settlement!;
            titleH = 76f; // 结算界面：标题 + 渐隐分割线 + 轮数概览行
            // 每名角色单行：姓名（原击坠位）＋ 造成伤害 ＋ 武器（图标+条） ＋ 流派（图标+条），单行高 40，行距 14。
            settlementRowsH = s.Rows.Count > 0 ? s.Rows.Count * 40f + (s.Rows.Count - 1) * 14f : 0f;
            lootH = SettlementLootSectionHeight(s.Money, s.Items.Count);
            settlementContentH = settlementRowsH + (lootH > 0 ? 20f + lootH : 0f);
        }

        var activeContentH = isSettlement ? settlementContentH : generalContentH;

        // 弹窗内部上下对称留白（顶部留白 + 底部留白 + 底部三角 24px）：
        const float symMargin = 22f;
        var totalH = ModalPadTop + titleH + symMargin + activeContentH + symMargin + 24f + ModalPadBottom;
        if (totalH < 200f)
            totalH = 200f;

        var panelX = (CanvasWidth - ModalWidth) / 2f;
        var panelY = (CanvasHeight - totalH) / 2f;
        var panelRect = new Rect2(panelX, panelY, ModalWidth, totalH);
        var centerX = panelRect.GetCenter().X;

        // 标题区固定在面板顶部
        var titleRect = default(Rect2);
        var ruleY = 0f;
        if (hasTitle)
        {
            var titleX = centerX - maxContentWidth / 2f;
            titleRect = new Rect2(titleX, panelY + ModalPadTop, maxContentWidth, 34f);
            ruleY = panelY + ModalPadTop + 38f;
        }

        // 核心：计算【文字 + 按钮】内容块在标题下方至底部三角区域之间的可用纵向空间
        // 确保顶部边缘与底部箭头边缘的对称基准线严密平衡
        var arrowTopY = panelRect.End.Y - 24f;
        var availableTop = isSettlement ? ruleY + 36f : (hasTitle ? ruleY + 6f : panelY + ModalPadTop);
        var availableBottom = arrowTopY - 4f;
        var availableH = availableBottom - availableTop;

        // 🚨 终极垂直居中：整坨内容块在可用空间内绝对垂直居中！上边距严格等于下边距！
        var contentStartY = availableTop + (availableH - activeContentH) / 2f;
        var curY = contentStartY;

        // 战后结算结构化几何分段（垂直居中）
        var settlementStartY = 0f;
        var settlementLootStartY = 0f;
        if (isSettlement)
        {
            settlementStartY = curY;
            curY += settlementRowsH;
            if (lootH > 0f)
            {
                curY += 20f;
                settlementLootStartY = curY;
                curY += lootH;
            }
        }

        // 2. 正文容器（文字上下垂直居中）
        var bodyRect = default(Rect2);
        if (!isSettlement && bodyLines.Count > 0)
        {
            var bodyX = centerX - maxContentWidth / 2f;
            bodyRect = new Rect2(bodyX, curY, maxContentWidth, bodyH);
            curY += bodyH + 20f;
        }

        // 3. 输入框容器（输入框上下垂直居中）
        var inputRect = default(Rect2);
        if (hasInput)
        {
            const float inputW = 600f;
            var inputX = centerX - inputW / 2f;
            inputRect = new Rect2(inputX, curY, inputW, 48f);
            curY += 48f + 20f;
        }

        // 4. 选项容器（按钮上下垂直居中）
        var choiceRects = new List<Rect2>(choiceCount);
        if (choiceCount is 1 or 2)
        {
            const float btnW = 180f;
            const float btnGap = 24f;
            var rowTotal = choiceCount * btnW + (choiceCount - 1) * btnGap;
            var startX = centerX - rowTotal / 2f;
            for (var i = 0; i < choiceCount; i++)
            {
                choiceRects.Add(new Rect2(startX + i * (btnW + btnGap), curY, btnW, 46f));
            }
        }
        else if (choiceCount > 2)
        {
            const float choiceListW = 600f;
            var choiceX = centerX - choiceListW / 2f;
            for (var i = 0; i < choiceCount; i++)
            {
                choiceRects.Add(new Rect2(choiceX, curY + i * 54f, choiceListW, 44f));
            }
        }

        var arrowTip = new Vector2(centerX, panelRect.End.Y - ModalPadBottom / 2f - 4f);

        return new InkModalLayout
        {
            PanelRect = panelRect,
            TitleRect = titleRect,
            RuleY = ruleY,
            BodyRect = bodyRect,
            BodyLines = bodyLines,
            InputRect = inputRect,
            ChoiceRects = choiceRects,
            ArrowTip = arrowTip,
            SettlementStartY = settlementStartY,
            SettlementLootStartY = settlementLootStartY,
        };
    }
}

/// <summary>自适应弹窗测算后的几何布局结构。</summary>
public sealed class InkModalLayout
{
    public Rect2 PanelRect { get; init; }
    public Rect2 TitleRect { get; init; }
    public float RuleY { get; init; }
    public Rect2 BodyRect { get; init; }
    public IReadOnlyList<string> BodyLines { get; init; } = System.Array.Empty<string>();
    public Rect2 InputRect { get; init; }
    public IReadOnlyList<Rect2> ChoiceRects { get; init; } = System.Array.Empty<Rect2>();
    public Vector2 ArrowTip { get; init; }
    public float SettlementStartY { get; init; }
    public float SettlementLootStartY { get; init; }
}
