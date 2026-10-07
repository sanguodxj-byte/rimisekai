using System.Collections.Generic;
using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 交易页三面板：左栏领地、中栏交易键、右栏市场。
/// 中栏面板整体下移，顶部让位给 9:16 插画位；中间面板所有内容严格水平居中。
/// 左侧点击准备卖出，右侧点击准备购买，支持分别选择或一次性同时买卖，点击成交按钮后成交。
/// 彻底无任何箭头。
/// </summary>
public static class InkTradePageRenderer
{
    private const int RowNameSize = 26;
    private const int RowValueSize = 26;
    private const int CenterNameSize = 26;
    private const int TradeButtonSize = 26;

    /// <summary>左栏标题带右侧的金钱：与面板标题同档同基线。</summary>
    private const int MoneySize = 26;

    /// <summary>画三面板。数据由 <see cref="InkPageBuilder"/> 摊好，渲染端只画不判断。</summary>
    public static void Draw(CanvasItem ci, InkTradeModel trade)
    {
        InkFrame.Panel(ci, InkLayout.TradePlayerPanel, corner: 16f);
        InkFrame.Panel(ci, InkLayout.TradeCenterPanel, corner: 16f);
        InkFrame.Panel(ci, InkLayout.TradeMarketPanel, corner: 16f);

        InkFrame.Title(ci, InkLayout.TradePlayerPanel, "领地");
        InkFrame.Title(ci, InkLayout.TradeMarketPanel, "市场");

        // 金钱在领地标题右侧同基线、右对齐显示
        InkDraw.Text(ci,
            new Vector2(InkLayout.TradeMoneyRect.End.X, InkLayout.TradeMoneyRect.Position.Y),
            InkText.Money(trade.Money), MoneySize, InkStyle.Line, "rt");

        // 顶部 9:16 竖幅大插画位
        DrawIllustrationSlot(ci, InkLayout.TradeIllustrationRect);

        DrawList(ci, InkLayout.TradePlayerPanel, trade.Held, trade.SelectedHeld,
            trade.HeldFirst, "背包中暂无可出售的物品。");
        DrawList(ci, InkLayout.TradeMarketPanel, trade.Market, trade.SelectedMarket,
            trade.MarketFirst, "今日集市暂无商品。");

        DrawCenter(ci, trade);
    }

    /// <summary>一栏清单：自顶向下规整排列，单项时不垂直居中悬空。</summary>
    private static void DrawList(CanvasItem ci, Rect2 panel, IReadOnlyList<InkTradeRow> rows,
        int selected, int first, string emptyHint)
    {
        var rowsTop = InkLayout.TradeRow(panel, 0).Position.Y;
        var area = new Rect2(panel.Position.X + 15f, rowsTop,
            panel.Size.X - 30f, panel.End.Y - 50f - rowsTop);

        if (rows.Count == 0)
        {
            InkDraw.TextBounded(ci, area, emptyHint, 20, 14, InkStyle.Dim, "cm");
            return;
        }

        var visible = InkLayout.TradeVisibleRows(panel);
        var shown = System.Math.Min(rows.Count - first, visible);

        for (var i = 0; i < shown; i++)
        {
            var row = rows[first + i];
            var rect = InkLayout.TradeRow(panel, i);
            if (first + i == selected)
                InkFrame.Selection(ci, rect);

            var cy = rect.GetCenter().Y;
            InkDraw.TextFitted(ci, new Vector2(rect.Position.X + 10f, cy),
                row.Name, InkLayout.TradeNameWidth(rect), RowNameSize, 14, InkStyle.Line, "lm");
            if (row.Count.Length > 0)
                InkDraw.Text(ci, new Vector2(InkLayout.TradeCountRight(rect), cy),
                    row.Count, RowValueSize, InkStyle.Line, "rm");
            InkDraw.Text(ci, new Vector2(InkLayout.TradePriceRight(rect), cy),
                InkText.Money(row.Price), RowValueSize, InkStyle.Line, "rm");
            InkFrame.RowDivider(ci, rect);
        }
    }

    /// <summary>
    /// 中间面板：所有内容完全水平居中。
    /// 支持同时准备卖出与购买，下方为成交按钮，点击后成交。
    /// </summary>
    private static void DrawCenter(CanvasItem ci, InkTradeModel trade)
    {
        var centerX = InkLayout.TradeCenterPanel.GetCenter().X; // 960f 绝对居中

        if (trade.HasSell && trade.HasBuy)
        {
            // 同时准备卖出与准备买入（两行都完全水平居中）
            var sellLine = $"准备卖出　{trade.SellName}　{trade.SellPrice}G";
            InkDraw.TextBounded(ci, InkLayout.TradeSelectRect, sellLine, 16, 13, InkStyle.Line, "cm");

            var buyLine = $"准备购买　{trade.BuyName}　{trade.BuyPrice}G";
            InkDraw.TextBounded(ci, InkLayout.TradeDescRect, buyLine, 16, 13, InkStyle.Line, "cm");
        }
        else if (trade.HasSell)
        {
            // 仅准备卖出（两行完全水平居中）
            InkDraw.TextBounded(ci, InkLayout.TradeSelectRect, trade.SellName, CenterNameSize, 16, InkStyle.Line, "cm");
            var sellLine = $"准备卖出　{trade.SellPrice}G";
            InkDraw.TextBounded(ci, InkLayout.TradeDescRect, sellLine, 16, 13, InkStyle.Dim, "cm");
        }
        else if (trade.HasBuy)
        {
            // 仅准备购买（两行完全水平居中）
            InkDraw.TextBounded(ci, InkLayout.TradeSelectRect, trade.BuyName, CenterNameSize, 16, InkStyle.Line, "cm");
            var buyLine = $"准备购买　{trade.BuyPrice}G";
            InkDraw.TextBounded(ci, InkLayout.TradeDescRect, buyLine, 16, 13, InkStyle.Dim, "cm");
        }
        else
        {
            // 未选中任何货品时严格留白
        }

        // 成交按钮（完全居中）：单一按钮，无任何箭头
        InkFrame.Button(ci, InkLayout.TradeTradeButton, "成交",
            selected: false, enabled: trade.CanRun, fontSize: TradeButtonSize, centered: true);

        // 成交结果反馈（完全居中）
        if (!string.IsNullOrEmpty(trade.Notice))
        {
            InkDraw.TextBounded(ci, InkLayout.TradeNoticeRect, trade.Notice, 15, 12, InkStyle.Dim, "cm");
        }
    }

    /// <summary>9:16 竖幅大插画位：虚线外框配合四角手绘角标，美观端正。</summary>
    private static void DrawIllustrationSlot(CanvasItem ci, Rect2 rect)
    {
        var c = new Color(InkStyle.Dim, 0.75f);
        InkDraw.Dashed(ci, rect.Position, new Vector2(rect.End.X, rect.Position.Y), c);
        InkDraw.Dashed(ci, new Vector2(rect.End.X, rect.Position.Y), rect.End, c);
        InkDraw.Dashed(ci, rect.End, new Vector2(rect.Position.X, rect.End.Y), c);
        InkDraw.Dashed(ci, new Vector2(rect.Position.X, rect.End.Y), rect.Position, c);

        const float tick = 16f;
        InkDraw.InkLine(ci, rect.Position, rect.Position + new Vector2(tick, 0), c, 1.4f, 0.2f, 9201);
        InkDraw.InkLine(ci, rect.Position, rect.Position + new Vector2(0, tick), c, 1.4f, 0.2f, 9202);
        InkDraw.InkLine(ci, new Vector2(rect.End.X, rect.Position.Y), new Vector2(rect.End.X - tick, rect.Position.Y), c, 1.4f, 0.2f, 9203);
        InkDraw.InkLine(ci, new Vector2(rect.End.X, rect.Position.Y), new Vector2(rect.End.X, rect.Position.Y + tick), c, 1.4f, 0.2f, 9204);
        InkDraw.InkLine(ci, rect.End, rect.End - new Vector2(tick, 0), c, 1.4f, 0.2f, 9205);
        InkDraw.InkLine(ci, rect.End, rect.End - new Vector2(0, tick), c, 1.4f, 0.2f, 9206);
        InkDraw.InkLine(ci, new Vector2(rect.Position.X, rect.End.Y), new Vector2(rect.Position.X + tick, rect.End.Y), c, 1.4f, 0.2f, 9207);
        InkDraw.InkLine(ci, new Vector2(rect.Position.X, rect.End.Y), new Vector2(rect.Position.X, rect.End.Y - tick), c, 1.4f, 0.2f, 9208);
    }
}
