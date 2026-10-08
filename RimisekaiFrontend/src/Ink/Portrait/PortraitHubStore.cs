using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Defs;
using Rimisekai.Housing;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 仓储页签：分段 库存 / 交易 / 制作。
/// 库存＝搜索条（原生输入框）＋品类签＋三列物品卡（点卡看详情抽屉）；
/// 交易＝买入/卖出段＋带步进的行＋底部结算条（一次成交整单，与 Core 的「每日一趟」闸门一致）；
/// 制作＝工种签＋配方行＋详情框（材料与持有、设为/取消该工种的生产目标）。
/// </summary>
public partial class PortraitHubScreen
{
    private static readonly string[] StoreLabels = { "库存", "交易", "制作" };

    private int _storeMode;
    private string _stockSearch = "";
    private int _stockCategory;
    private string _stockSel = "";
    private bool _tradeSell;
    private readonly Dictionary<string, int> _tradeQty = new();
    private int _craftStation;
    private string _craftSel = "";
    private LineEdit? _searchEdit;

    public override void _Ready()
    {
        // 帧循环只在会话绑定后跑（Bind 里打开）：标题画面阶段没有会话，遭遇、动效、提示签都无从谈起。
        SetProcess(false);
        // 搜索走原生 LineEdit：软键盘的中文输入法只认它。画面上的字由我们自己画，输入框本身透明。
        _searchEdit = new LineEdit { Modulate = new Color(1, 1, 1, 0f), Visible = false, MaxLength = 16 };
        AddChild(_searchEdit);
        _searchEdit.TextChanged += text =>
        {
            _stockSearch = text;
            _pan.Remove("stock");
            QueueRedraw();
        };
        _searchEdit.TextSubmitted += _ => EndSearch();
    }

    private void EndSearch()
    {
        _searchEdit!.ReleaseFocus();
        _searchEdit.Hide();
        QueueRedraw();
    }

    private string ItemName(string itemId)
    {
        var info = Items.Info(_vm.Hub.State.Territory, itemId);
        return info != null && info.Value.Label.Length > 0 ? info.Value.Label : itemId;
    }

    private void LeaveTradeIfOpen()
    {
        if (_tab == 3 && _storeMode == 1 && _push == PushPage.None)
            _vm.Hub.LeaveMarket();
        _tradeQty.Clear();
        if (_searchEdit is { Visible: true })
            EndSearch();
    }

    private void DrawStore()
    {
        switch (_storeMode)
        {
            case 0: DrawStock(); break;
            case 1: DrawTrade(); break;
            default: DrawCraft(); break;
        }
        var seg = PortraitLayout.BodySegment;
        PortraitFrame.Segmented(this, seg, StoreLabels, _storeMode);
        for (var i = 0; i < StoreLabels.Length; i++)
            _widgets.Add(new PortraitWidget(PortraitFrame.SegmentRect(seg, StoreLabels.Length, i),
                PortraitAction.StoreSegment, i, true, StoreLabels[i]));
    }

    // ---------- 库存 ----------

    private List<(string Id, string Name, int Count, string Category)> StockItems()
    {
        var list = new List<(string Id, string Name, int Count, string Category)>();
        var search = _stockSearch.Trim();
        foreach (var pair in _vm.Hub.Stock())
        {
            if (pair.Value <= 0)
                continue;
            var name = ItemName(pair.Key);
            if (search.Length > 0 && !name.Contains(search, StringComparison.OrdinalIgnoreCase))
                continue;
            list.Add((pair.Key, name, pair.Value, _vm.ItemCategoryLabel(pair.Key)));
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return list;
    }

    private void DrawStock()
    {
        var all = StockItems();
        var categories = new List<string> { "全部" };
        categories.AddRange(all.Select(i => i.Category).Where(c => c.Length > 0).Distinct());
        _stockCategory = Math.Clamp(_stockCategory, 0, categories.Count - 1);
        var shown = _stockCategory == 0 ? all : all.Where(i => i.Category == categories[_stockCategory]).ToList();

        var view = PortraitLayout.StockView;
        var cw = PortraitLayout.StockCardWidth;
        var step = PortraitLayout.StockCardHeight + 20f;
        var rows = (shown.Count + 2) / 3;
        var total = (int)(rows * step);
        var offset = Pan("stock", total, (int)view.Size.Y);
        for (var i = 0; i < shown.Count; i++)
        {
            var item = shown[i];
            var r = new Rect2(PortraitLayout.Pad + i % 3 * (cw + 20f), view.Position.Y + i / 3 * step - offset,
                cw, PortraitLayout.StockCardHeight);
            if (r.End.Y < view.Position.Y || r.Position.Y > view.End.Y)
                continue;
            PortraitFrame.Card(this, r, item.Id == _stockSel && _sheet == SheetKind.None);
            var c = new Vector2(r.GetCenter().X, r.Position.Y + 92f);
            InkDraw.Jewel(this, c, 54f, InkStyle.Dim);
            InkDraw.Jewel(this, c, 50f, InkStyle.Bg);
            InkDraw.Text(this, c, item.Name[..1], PortraitLayout.FontBody, InkStyle.Line, "cm");
            InkDraw.TextBounded(this, new Rect2(r.Position.X + 24f, r.Position.Y + 176f, r.Size.X - 48f, 60f), item.Name,
                PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Line, "lm");
            InkDraw.Text(this, new Vector2(r.End.X - 24f, r.Position.Y + 140f), $"×{item.Count}", PortraitLayout.FontMeta, InkStyle.Dim, "rm");
            AddClipped(r, view, PortraitAction.StockItem, all.IndexOf(item), true, item.Id);
        }
        if (shown.Count == 0)
            InkDraw.Text(this, new Vector2(PortraitLayout.CanvasWidth / 2f, view.Position.Y + 120f),
                _stockSearch.Length > 0 ? "没有匹配的物品。" : "背包是空的。", PortraitLayout.FontBody, InkStyle.Dim, "cm");
        RegisterScroll("stock", view, total, (int)view.Size.Y, offset, v => _pan["stock"] = v, 1f);
        MaskAbove(view);

        var search = PortraitLayout.StockSearch;
        var editing = _searchEdit is { Visible: true };
        PortraitFrame.Bevel(this, search, search.Size.Y / 2f, InkStyle.Panel, editing ? InkStyle.Line : InkStyle.WoodDark, 3f);
        PortraitGlyph.Search(this, search.Position.X + 70f, search.GetCenter().Y, 24f, InkStyle.Dim);
        InkDraw.TextBounded(this, new Rect2(search.Position.X + 120f, search.Position.Y, search.Size.X - 160f, search.Size.Y),
            _stockSearch.Length > 0 ? _stockSearch : "搜索物品", PortraitLayout.FontBody, PortraitLayout.FontMeta,
            _stockSearch.Length > 0 ? InkStyle.Line : InkStyle.Dim, "lm");
        _widgets.Add(new PortraitWidget(search, PortraitAction.StockSearch, 0, true, "搜索"));

        var chips = new Rect2(0, PortraitLayout.StockChipsY, PortraitLayout.CanvasWidth, PortraitLayout.TouchMin);
        DrawChipRow("stock_chips", chips, categories, _stockCategory, PortraitAction.StockCategory);
    }

    /// <summary>一行可横拖的标签签。</summary>
    private void DrawChipRow(string id, Rect2 row, IReadOnlyList<string> labels, int selected, PortraitAction action)
    {
        var widths = labels.Select(l => PortraitFrame.ChipWidth(l)).ToArray();
        var total = (int)(PortraitLayout.Pad * 2f + widths.Sum() + 20f * Math.Max(0, widths.Length - 1));
        var offset = Pan(id, total, (int)row.Size.X);
        float x = PortraitLayout.Pad - offset;
        for (var i = 0; i < labels.Count; i++)
        {
            var r = new Rect2(x, row.Position.Y, widths[i], row.Size.Y);
            PortraitFrame.Chip(this, r, labels[i], i == selected);
            AddClipped(r, row, action, i, true, labels[i]);
            x += widths[i] + 20f;
        }
        PortraitFrame.ScrollEdges(this, row, offset, total);
        RegisterScroll(id, row, total, (int)row.Size.X, offset, v => _pan[id] = v, 1f, horizontal: true);
    }

    /// <summary>物品详情抽屉：取库存模型的详情（名称 / 数量 / 品类 / 价 / 说明），只读。</summary>
    private float DrawItemSheet()
    {
        var all = StockItems();
        var index = all.FindIndex(i => i.Id == _stockSel);
        var q = new InkPageQuery(index, index, -1, _stockSearch, false, 0, 0, false, -1, -1);
        var model = InkPageBuilder.Build(_vm, InkPage.Stock, in q);
        var lines = InkDraw.WrapLines(model.DetailNote, PortraitLayout.FullWidth - 40f, PortraitLayout.FontMeta);
        var top = Mathf.Max(900f, PortraitLayout.CanvasHeight - 120f - PortraitLayout.SheetContentOffset - lines.Count * 64f);
        PortraitFrame.Sheet(this, top);
        InkDraw.TextBounded(this, new Rect2(PortraitLayout.Pad + 20f, top + PortraitLayout.SheetTitleOffset - 40f, 760f, 80f),
            model.DetailTitle, PortraitLayout.FontTitle, PortraitLayout.FontBody, InkStyle.Line, "lm");
        var close = PortraitLayout.SheetClose(top);
        PortraitGlyph.Close(this, close.GetCenter().X, close.GetCenter().Y, 26f, InkStyle.Dim);
        _widgets.Add(new PortraitWidget(close, PortraitAction.SheetClose, 0, true, "收起"));
        var y = top + PortraitLayout.SheetContentOffset + 10f;
        foreach (var line in lines)
        {
            if (y > PortraitLayout.CanvasHeight - 80f)
                break;
            InkDraw.Text(this, new Vector2(PortraitLayout.Pad + 20f, y), line, PortraitLayout.FontMeta, InkStyle.Line, "lm");
            y += 64f;
        }
        return top;
    }

    // ---------- 交易 ----------

    private readonly record struct TradeLine(string Id, string Name, string Sub, int Price, int Max);

    private List<TradeLine> TradeLines()
    {
        var hub = _vm.Hub;
        var list = new List<TradeLine>();
        if (_tradeSell)
        {
            foreach (var pair in hub.Stock())
            {
                if (pair.Value <= 0)
                    continue;
                var listing = hub.State.Territory.Listing(pair.Key);
                if (listing == null)
                    continue;
                var price = hub.TradePrices(listing.Value, selling: true);
                if (price > 0)
                    list.Add(new TradeLine(pair.Key, ItemName(pair.Key), $"持有 {pair.Value}", price, pair.Value));
            }
        }
        else
        {
            foreach (var l in hub.State.Territory.Listings())
            {
                if (l.Stock <= 0)
                    continue;
                var price = hub.TradePrices(l, selling: false);
                if (price > 0)
                    list.Add(new TradeLine(l.ItemId, ItemName(l.ItemId), $"市场 {l.Stock}", price, l.Stock));
            }
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return list;
    }

    private (int Count, long Delta) TradeTotals()
    {
        var count = 0;
        var delta = 0L;
        foreach (var line in TradeLines())
        {
            var q = _tradeQty.GetValueOrDefault(line.Id);
            count += q;
            delta += (_tradeSell ? 1 : -1) * (long)line.Price * q;
        }
        return (count, delta);
    }

    private void DrawTrade()
    {
        var lines = TradeLines();
        var view = PortraitLayout.TradeView;
        var step = PortraitLayout.TradeRowHeight;
        var total = (int)(lines.Count * step);
        var offset = Pan("trade", total, (int)view.Size.Y);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var r = new Rect2(PortraitLayout.Pad, view.Position.Y + i * step - offset, PortraitLayout.FullWidth, step - 20f);
            if (r.End.Y < view.Position.Y || r.Position.Y > view.End.Y)
                continue;
            var q = _tradeQty.GetValueOrDefault(line.Id);
            PortraitFrame.Bevel(this, r, 22f, q > 0 ? InkStyle.Hover : InkStyle.Bg, q > 0 ? InkStyle.Line : InkStyle.WoodDark,
                q > 0 ? 4f : 3f);
            InkDraw.TextBounded(this, new Rect2(r.Position.X + 40f, r.Position.Y + 14f, 360f, 64f), line.Name,
                PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "lm");
            InkDraw.Text(this, new Vector2(r.Position.X + 40f, r.Position.Y + 102f), line.Sub, PortraitLayout.FontMeta, InkStyle.Dim, "lm");
            PortraitGlyph.Coin(this, r.Position.X + 440f, r.GetCenter().Y, 16f, InkStyle.Dim);
            InkDraw.Text(this, new Vector2(r.Position.X + 470f, r.GetCenter().Y), $"{line.Price}", PortraitLayout.FontBody, InkStyle.Line, "lm");

            var stepper = new Rect2(r.End.X - 30f - 354f, r.GetCenter().Y - 59f, 354f, PortraitLayout.TouchMin);
            PortraitFrame.Bevel(this, stepper, 59f, null, InkStyle.Dim, 3f);
            var minus = new Rect2(stepper.Position.X, stepper.Position.Y, PortraitLayout.TouchMin, PortraitLayout.TouchMin);
            var plus = new Rect2(stepper.End.X - PortraitLayout.TouchMin, stepper.Position.Y, PortraitLayout.TouchMin, PortraitLayout.TouchMin);
            if (PortraitFrame.IsPressed(minus))
                PortraitFrame.PressMark(this, minus.Grow(-8f));
            if (PortraitFrame.IsPressed(plus))
                PortraitFrame.PressMark(this, plus.Grow(-8f));
            PortraitGlyph.Minus(this, minus.GetCenter().X, minus.GetCenter().Y, 22f, q > 0 ? InkStyle.Line : InkStyle.WoodDark);
            PortraitGlyph.Plus(this, plus.GetCenter().X, plus.GetCenter().Y, 22f, q < line.Max ? InkStyle.Line : InkStyle.WoodDark);
            InkDraw.Text(this, stepper.GetCenter(), $"{q}", PortraitLayout.FontBody, InkStyle.Line, "cm");
            AddClipped(minus, view, PortraitAction.TradeMinus, i, q > 0, line.Id);
            AddClipped(plus, view, PortraitAction.TradePlus, i, q < line.Max, line.Id);
        }
        RegisterScroll("trade", view, total, (int)view.Size.Y, offset, v => _pan["trade"] = v, 1f);
        MaskAbove(view);

        var seg = PortraitLayout.TradeSegment;
        var labels = new[] { "买入", "卖出" };
        PortraitFrame.Segmented(this, seg, labels, _tradeSell ? 1 : 0);
        for (var i = 0; i < 2; i++)
            _widgets.Add(new PortraitWidget(PortraitFrame.SegmentRect(seg, 2, i), PortraitAction.TradeSegment, i, true, labels[i]));

        var bar = PortraitLayout.TradeCheckout;
        DrawRect(new Rect2(0, view.End.Y, PortraitLayout.CanvasWidth, PortraitLayout.TabTop - view.End.Y), InkStyle.Bg);
        var (count, delta) = TradeTotals();
        var money = _vm.Hub.State.Money;
        PortraitFrame.Bevel(this, bar, 40f, InkStyle.Line);
        InkDraw.Text(this, new Vector2(bar.Position.X + 50f, bar.Position.Y + 56f), $"共 {count} 件", PortraitLayout.FontMeta, InkStyle.WoodDark, "lm");
        InkDraw.TextBounded(this, new Rect2(bar.Position.X + 50f, bar.Position.Y + 84f, bar.Size.X - 420f, 66f),
            $"{(delta >= 0 ? "+" : "−")} {Math.Abs(delta)}  →  余 {InkText.Money(money + delta)}",
            PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Bg, "lm");
        var commit = new Rect2(bar.End.X - 30f - 290f, bar.Position.Y + 26f, 290f, PortraitLayout.TouchMin);
        var canCommit = count > 0 && money + delta >= 0 && _vm.Hub.TradeAvailable;
        PortraitFrame.Bevel(this, commit, 59f, PortraitFrame.IsPressed(commit) ? InkStyle.Hover : InkStyle.Bg);
        InkDraw.Text(this, commit.GetCenter(), "成交", PortraitLayout.FontBody, canCommit ? InkStyle.Line : InkStyle.Dim, "cm");
        _widgets.Add(new PortraitWidget(commit, PortraitAction.TradeRun, 0, canCommit, "成交"));
    }

    private void CommitTrade()
    {
        var hub = _vm.Hub;
        foreach (var line in TradeLines())
        {
            var q = _tradeQty.GetValueOrDefault(line.Id);
            if (q > 0)
                hub.MarketTrade(line.Id, q, _tradeSell);
        }
        _tradeQty.Clear();
    }

    // ---------- 制作 ----------

    private List<ActionKind> CraftStations() =>
        _vm.Hub.State.Territory.Recipes.Select(r => r.Station).Distinct().ToList();

    private void DrawCraft()
    {
        var stations = CraftStations();
        var territory = _vm.Hub.State.Territory;
        if (stations.Count == 0)
        {
            InkDraw.Text(this, new Vector2(PortraitLayout.CanvasWidth / 2f, PortraitLayout.CraftChipsY + 200f), "没有配方。",
                PortraitLayout.FontBody, InkStyle.Dim, "cm");
            return;
        }
        _craftStation = Math.Clamp(_craftStation, 0, stations.Count - 1);
        var station = stations[_craftStation];
        var recipes = territory.Recipes.Where(r => r.Station == station).ToList();
        if (recipes.All(r => r.ItemId != _craftSel))
            _craftSel = recipes[0].ItemId;
        var target = territory.GetTargetCraftItem(station);
        var stock = _vm.Hub.Stock();

        var view = PortraitLayout.CraftView;
        var step = PortraitLayout.CraftRowHeight;
        var total = (int)(recipes.Count * step);
        var offset = Pan("craft", total, (int)view.Size.Y);
        for (var i = 0; i < recipes.Count; i++)
        {
            var recipe = recipes[i];
            var r = new Rect2(PortraitLayout.Pad, view.Position.Y + i * step - offset, PortraitLayout.FullWidth, step - 20f);
            if (r.End.Y < view.Position.Y || r.Position.Y > view.End.Y)
                continue;
            var sel = recipe.ItemId == _craftSel;
            var payable = recipe.Costs.All(c => stock.GetValueOrDefault(c.ItemId) >= c.Count);
            PortraitFrame.Card(this, r, sel, 22f);
            var name = ItemName(recipe.ItemId);
            var icon = new Vector2(r.Position.X + 70f, r.GetCenter().Y);
            InkDraw.Jewel(this, icon, 38f, payable ? InkStyle.Line : InkStyle.WoodDark);
            InkDraw.Jewel(this, icon, 34f, sel ? InkStyle.Hover : InkStyle.Panel);
            InkDraw.Text(this, icon, name[..1], PortraitLayout.FontMeta, payable ? InkStyle.Line : InkStyle.Dim, "cm");
            InkDraw.TextBounded(this, new Rect2(r.Position.X + 130f, r.Position.Y, 360f, r.Size.Y), name,
                PortraitLayout.FontBody, PortraitLayout.FontMeta, payable ? InkStyle.Line : InkStyle.Dim, "lm");
            var costs = string.Join(" · ", recipe.Costs.Select(c => $"{ItemName(c.ItemId)} ×{c.Count}"));
            InkDraw.TextBounded(this, new Rect2(r.Position.X + 500f, r.Position.Y, r.Size.X - 540f, r.Size.Y),
                recipe.ItemId == target ? "生产目标" : costs, PortraitLayout.FontMeta, PortraitLayout.FontMeta,
                recipe.ItemId == target ? InkStyle.Line : InkStyle.Dim, "rm");
            AddClipped(r, view, PortraitAction.CraftRecipe, i, true, recipe.ItemId);
        }
        RegisterScroll("craft", view, total, (int)view.Size.Y, offset, v => _pan["craft"] = v, 1f);
        MaskAbove(view);
        var chips = new Rect2(0, PortraitLayout.CraftChipsY, PortraitLayout.CanvasWidth, PortraitLayout.TouchMin);
        DrawChipRow("craft_chips", chips, stations.Select(ActionKindMap.LabelOf).ToList(), _craftStation, PortraitAction.CraftStation);

        var selRecipe = recipes.First(r => r.ItemId == _craftSel);
        var frame = PortraitLayout.CraftDetail;
        DrawRect(new Rect2(0, view.End.Y, PortraitLayout.CanvasWidth, PortraitLayout.TabTop - view.End.Y), InkStyle.Bg);
        PortraitFrame.GothicFrame(this, frame);
        var x = frame.Position.X + 50f;
        InkDraw.TextBounded(this, new Rect2(x, frame.Position.Y + 30f, frame.Size.X - 100f, 80f), ItemName(selRecipe.ItemId),
            PortraitLayout.FontTitle, PortraitLayout.FontBody, InkStyle.Line, "lm");
        InkDraw.Text(this, new Vector2(x, frame.Position.Y + 136f),
            $"产出 ×{selRecipe.OutputCount} · {InkText.LifeSkill(selRecipe.Skill)}", PortraitLayout.FontMeta, InkStyle.Dim, "lm");
        var y = frame.Position.Y + 210f;
        foreach (var cost in selRecipe.Costs.Take(3))
        {
            InkDraw.Text(this, new Vector2(x, y), ItemName(cost.ItemId), PortraitLayout.FontBody, InkStyle.Line, "lm");
            InkDraw.Text(this, new Vector2(frame.End.X - 50f, y), $"{stock.GetValueOrDefault(cost.ItemId)} / {cost.Count}",
                PortraitLayout.FontBody, InkStyle.Line, "rm");
            InkDraw.InkLine(this, new Vector2(x, y + 44f), new Vector2(frame.End.X - 50f, y + 44f), InkStyle.Hover, 2f);
            y += 90f;
        }
        var button = new Rect2(x, frame.End.Y - 50f - 128f, frame.Size.X - 100f, 128f);
        var isTarget = selRecipe.ItemId == target;
        PortraitFrame.Plaque(this, button, isTarget ? "取消生产目标" : "设为生产目标", primary: !isTarget);
        _widgets.Add(new PortraitWidget(button, PortraitAction.CraftToggle, 0, true, selRecipe.ItemId));
    }

    private bool ExecuteStore(PortraitWidget w)
    {
        switch (w.Action)
        {
            case PortraitAction.StoreSegment:
                if (_storeMode == 1 && w.Index != 1)
                    _vm.Hub.LeaveMarket();
                if (w.Index == 1 && _storeMode != 1)
                    _vm.Hub.OpenTrade();
                _storeMode = w.Index;
                _tradeQty.Clear();
                return true;
            case PortraitAction.StockSearch:
                _searchEdit!.Position = PortraitLayout.StockSearch.Position;
                _searchEdit.Size = PortraitLayout.StockSearch.Size;
                _searchEdit.Text = _stockSearch;
                _searchEdit.Show();
                _searchEdit.GrabFocus();
                return true;
            case PortraitAction.StockCategory:
                _stockCategory = w.Index;
                _pan.Remove("stock");
                return true;
            case PortraitAction.StockItem:
                _stockSel = w.Label;
                _sheet = SheetKind.Item;
                return true;
            case PortraitAction.TradeSegment:
                _tradeSell = w.Index == 1;
                _tradeQty.Clear();
                _pan.Remove("trade");
                return true;
            case PortraitAction.TradeMinus:
                _tradeQty[w.Label] = Math.Max(0, _tradeQty.GetValueOrDefault(w.Label) - 1);
                return true;
            case PortraitAction.TradePlus:
                _tradeQty[w.Label] = _tradeQty.GetValueOrDefault(w.Label) + 1;
                return true;
            case PortraitAction.TradeRun:
                CommitTrade();
                return true;
            case PortraitAction.CraftStation:
                _craftStation = w.Index;
                _pan.Remove("craft");
                return true;
            case PortraitAction.CraftRecipe:
                _craftSel = w.Label;
                return true;
            case PortraitAction.CraftToggle:
                _vm.Hub.Craft(w.Label);
                return true;
            default:
                return false;
        }
    }
}
