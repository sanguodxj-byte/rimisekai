using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Housing;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 存储设置抽屉（从存取抽屉的齿轮进来，左上返回退回存取抽屉）：
/// 优先级五段（低 / 普通 / 优先 / 重要 / 关键）、全部允许 / 全部清除、按品类展开的过滤树——
/// 品类行点名字展开 / 收起，右侧钮切换收放（允许 / 部分 / 禁止），物品行同样一钮一切。
/// </summary>
public partial class PortraitHubScreen
{
    private bool _storageSettings;
    private readonly HashSet<string> _storageExpanded = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<Rimisekai.Hub.StorageFilterRow> _storageFilterRows = Array.Empty<Rimisekai.Hub.StorageFilterRow>();

    private static string FilterLabel(FilterState state) => state switch
    {
        FilterState.All => "允许",
        FilterState.Some => "部分",
        _ => "禁止",
    };

    private float DrawStorageSettingsSheet()
    {
        var hub = _vm.Hub;
        var facility = hub.OpenStorageFacility!;
        var top = PortraitLayout.StorageSheetTop;
        PortraitFrame.Sheet(this, top);
        var back = PortraitLayout.SheetBack(top);
        PortraitGlyph.Back(this, back.GetCenter().X, back.GetCenter().Y, 30f, InkStyle.Line);
        _widgets.Add(new PortraitWidget(back, PortraitAction.SheetClose, 0, true, "返回"));
        InkDraw.TextBounded(this, new Rect2(PortraitLayout.Pad + 150f, top + PortraitLayout.SheetTitleOffset - 40f, 700f, 80f),
            $"{facility.Name}的存储设置", PortraitLayout.FontTitle, PortraitLayout.FontBody, InkStyle.Line, "lm");

        var seg = PortraitLayout.StoragePrioritySegment;
        var labels = StoragePriorities.All.Select(StoragePriorities.Label).ToArray();
        PortraitFrame.Segmented(this, seg, labels, Array.IndexOf(StoragePriorities.All, facility.Priority));
        for (var i = 0; i < labels.Length; i++)
            _widgets.Add(new PortraitWidget(PortraitFrame.SegmentRect(seg, labels.Length, i), PortraitAction.StoragePriority, i, true, labels[i]));

        PortraitFrame.Plaque(this, PortraitLayout.StorageAllowAll, "全部允许");
        _widgets.Add(new PortraitWidget(PortraitLayout.StorageAllowAll, PortraitAction.StorageAllowAll, 0, true, "全部允许"));
        PortraitFrame.Plaque(this, PortraitLayout.StorageClearAll, "全部清除");
        _widgets.Add(new PortraitWidget(PortraitLayout.StorageClearAll, PortraitAction.StorageClearAll, 0, true, "全部清除"));

        var rows = hub.StorageFilterRows(_storageExpanded);
        _storageFilterRows = rows;
        var visible = PortraitLayout.StorageFilterRows;
        var first = Math.Clamp(Pan("storage_filter", rows.Count, visible), 0, Math.Max(0, rows.Count - visible));
        for (var i = 0; i < visible && first + i < rows.Count; i++)
        {
            var at = first + i;
            var row = rows[at];
            var rect = PortraitLayout.StorageFilterRow(i);
            var toggle = PortraitLayout.StorageFilterToggle(rect);
            var indent = row.Depth * PortraitLayout.StorageFilterIndent;
            var name = new Rect2(rect.Position.X + indent, rect.Position.Y, toggle.Position.X - 24f - rect.Position.X - indent, rect.Size.Y);
            if (row.IsCategory)
            {
                PortraitFrame.Bevel(this, name, 22f, null, InkStyle.WoodDark, 3f);
                // 展开 / 收起：左端加减号
                var mark = new Vector2(name.Position.X + 56f, name.GetCenter().Y);
                if (_storageExpanded.Contains(row.Entry))
                    PortraitGlyph.Minus(this, mark.X, mark.Y, 18f, InkStyle.Dim);
                else
                    PortraitGlyph.Plus(this, mark.X, mark.Y, 18f, InkStyle.Dim);
                InkDraw.TextBounded(this, new Rect2(name.Position.X + 100f, name.Position.Y, name.Size.X - 120f, name.Size.Y), row.Label,
                    PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "lm");
                _widgets.Add(new PortraitWidget(name, PortraitAction.StorageFold, at, true, row.Label));
            }
            else
            {
                var icon = new Vector2(name.Position.X + 56f, name.GetCenter().Y);
                InkDraw.Jewel(this, icon, 30f, InkStyle.Dim);
                InkDraw.Jewel(this, icon, 26f, InkStyle.Bg);
                InkDraw.Text(this, icon, row.Label[..1], PortraitLayout.FontMeta, InkStyle.Line, "cm");
                InkDraw.TextBounded(this, new Rect2(name.Position.X + 100f, name.Position.Y, name.Size.X - 120f, name.Size.Y), row.Label,
                    PortraitLayout.FontBody, PortraitLayout.FontMeta, row.State == FilterState.All ? InkStyle.Line : InkStyle.Dim, "lm");
            }
            PortraitFrame.Plaque(this, toggle, FilterLabel(row.State), primary: row.State == FilterState.All);
            _widgets.Add(new PortraitWidget(toggle, PortraitAction.StorageToggle, at, true, row.Entry));
        }
        RegisterScroll("storage_filter", new Rect2(0, PortraitLayout.StorageFilterRow(0).Position.Y, PortraitLayout.CanvasWidth,
            visible * PortraitLayout.SheetRowStep), rows.Count, visible, first, v => _pan["storage_filter"] = v, PortraitLayout.SheetRowStep);
        return top;
    }

    private bool ExecuteStorageSettings(PortraitWidget widget)
    {
        var hub = _vm.Hub;
        switch (widget.Action)
        {
            case PortraitAction.StorageSettings:
                _storageSettings = true;
                _storageExpanded.Clear();
                _pan.Remove("storage_filter");
                return true;
            case PortraitAction.StoragePriority:
                hub.SetStoragePriority(StoragePriorities.All[widget.Index]);
                return true;
            case PortraitAction.StorageAllowAll:
                hub.AllowAllStorage();
                return true;
            case PortraitAction.StorageClearAll:
                hub.ClearStorageFilter();
                return true;
            case PortraitAction.StorageFold:
                var entry = _storageFilterRows[widget.Index].Entry;
                if (!_storageExpanded.Remove(entry))
                    _storageExpanded.Add(entry);
                return true;
            case PortraitAction.StorageToggle:
                hub.ToggleStorageFilter(widget.Label);
                return true;
            default:
                return false;
        }
    }
}
