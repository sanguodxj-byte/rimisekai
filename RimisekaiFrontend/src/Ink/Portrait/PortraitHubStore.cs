using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Defs;
using Rimisekai.Housing;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 仓储页签：分段 库存 / 制作。
/// 库存＝搜索条（原生输入框）＋品类签＋三列物品卡（点卡看详情抽屉）；
/// 制作＝工种签＋配方行＋详情框（材料与持有、设为/取消该工种的生产目标）。
/// </summary>
public partial class PortraitHubScreen
{
    private static readonly string[] StoreLabels = { "库存", "制作" };

    private int _storeMode;
    private string _stockSearch = "";
    private int _stockCategory;
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

    private void LeaveSearchIfOpen()
    {
        if (_searchEdit is { Visible: true })
            EndSearch();
    }

    private void DrawStore()
    {
        switch (_storeMode)
        {
            case 0: DrawStock(); break;
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
        var step = PortraitLayout.StockRowHeight + PortraitLayout.StockRowGap;
        var total = (int)(shown.Count * step);
        var offset = Pan("stock", total, (int)view.Size.Y);
        for (var i = 0; i < shown.Count; i++)
        {
            var item = shown[i];
            var r = PortraitLayout.StockRow(i, offset);
            if (r.End.Y < view.Position.Y || r.Position.Y > view.End.Y)
                continue;
            DrawStockRow(r, item.Id, item.Name, item.Count, view);
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

    /// <summary>
    /// 库存一条（与战后结算战利品条同一套：缺角细框＋菱形首字）：上行物名，下行题签（品类 ◆ 品质 ◆ 单价），
    /// 右侧件数；精致及以上铺左→右吹的稀有度雾。
    /// </summary>
    private void DrawStockRow(Rect2 r, string itemId, string name, int count, Rect2 view)
    {
        var detail = _vm.Hub.DescribeItem(itemId);
        var info = Items.Info(_vm.Hub.State.Territory, itemId)!.Value;
        PortraitFrame.Bevel(this, r, 22f, PortraitFrame.IsPressed(r) ? PortraitFrame.PressFill : null, InkStyle.WoodDark, 3f);
        if (detail.Quality is { } q)
            _fog.Place(r.Grow(-4f), q, viewport: view);
        var icon = new Vector2(r.Position.X + 66f, r.GetCenter().Y);
        InkDraw.Jewel(this, icon, 38f, InkStyle.Dim);
        InkDraw.Jewel(this, icon, 34f, InkStyle.Bg);
        InkDraw.Text(this, icon, name[..1], PortraitLayout.FontMeta, InkStyle.Line, "cm");
        var countText = $"×{count}";
        var countWidth = InkDraw.Measure(countText, PortraitLayout.FontBody).X;
        var textLeft = r.Position.X + 128f;
        var textRight = r.End.X - 56f - countWidth;
        InkDraw.TextBounded(this, new Rect2(textLeft, r.Position.Y + 12f, textRight - textLeft, 66f), name,
            PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "lm");
        var tags = new List<string> { detail.Category };
        if (detail.Quality is { } quality)
            tags.Add(QualityOf.Label(quality));
        tags.Add($"{info.MarketValue}G");
        PortraitFrame.TagLine(this, textLeft, r.Position.Y + 110f, tags, textRight, InkStyle.Dim);
        InkDraw.Text(this, new Vector2(r.End.X - 32f, r.GetCenter().Y), countText, PortraitLayout.FontBody, InkStyle.Line, "rm");
    }

    /// <summary>配方行右侧的材料：自右向左排「名 ×n」计数签，组间小菱；放不下的前段不画。</summary>
    private void DrawCostTags(IReadOnlyList<RecipeCost> costs, float left, float right, float cy)
    {
        var x = right;
        for (var i = costs.Count - 1; i >= 0; i--)
        {
            var label = ItemName(costs[i].ItemId);
            var value = $"×{costs[i].Count}";
            var w = InkDraw.Measure(label, PortraitLayout.FontMeta).X + 10f + InkDraw.Measure(value, PortraitLayout.FontMeta).X;
            if (x - w < left)
                break;
            PortraitFrame.CountTag(this, x - w, cy, label, value, true);
            x -= w;
            if (i > 0)
            {
                InkDraw.Jewel(this, new Vector2(x - 20f, cy), 6f, new Color(InkStyle.Dim, 0.8f));
                x -= 40f;
            }
        }
    }

    /// <summary>点库存条：弹物品详情（标题、品类副题、逐行字段条、说明），与装备详情同一版式。</summary>
    private void ShowStockDetail(string itemId)
    {
        var d = _vm.Hub.DescribeItem(itemId);
        ModalWanted!(new InkModalPage
        {
            Title = d.Title,
            Item = new InkModalItemData { Subtitle = d.Category, Quality = d.Quality, Lines = d.Lines, Flavor = d.Flavor },
        });
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
            if (recipe.ItemId == target)
                InkDraw.Text(this, new Vector2(r.End.X - 40f, r.GetCenter().Y), "生产目标", PortraitLayout.FontMeta, InkStyle.Line, "rm");
            else
                DrawCostTags(recipe.Costs, r.Position.X + 500f, r.End.X - 40f, r.GetCenter().Y);
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
        var after = PortraitFrame.CountTag(this, x, frame.Position.Y + 136f, "产出", $"×{selRecipe.OutputCount}", true);
        PortraitFrame.TagLine(this, after, frame.Position.Y + 136f, new[] { InkText.LifeSkill(selRecipe.Skill) }, frame.End.X - 50f, InkStyle.Dim, continues: true);
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
                _storeMode = w.Index;
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
                ShowStockDetail(w.Label);
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
