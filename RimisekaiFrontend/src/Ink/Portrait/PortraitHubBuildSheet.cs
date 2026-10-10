using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Defs;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 全高建造抽屉（建造页点一格、或领地格长按进来）：
/// 分段「可建 / 已有 / 门」→ 两行分类页签（每类「能建/总数」）→ 4 列格子（能建骨白、建不了一律压暗，一项不藏）
/// → 底部常驻详情卡（花费与条件逐条打勾打叉、自带设施、建造 / 开拓并建造 / 拆除，刚建的一笔可撤）。
/// 条件、状态、排序全由 Core <see cref="HubSession.BuildOptions"/> 给出，这里只排版。
/// </summary>
public partial class PortraitHubScreen
{
    private const string PlotName = "空地";
    private static readonly string[] BuildSegmentNames = { "可建", "已有", "门" };

    private int _buildRegion;
    private int _buildX = -1;
    private int _buildY = -1;
    private int _buildSegment;
    private string _buildCategory = "";
    private (BuildTarget Target, int DefId)? _buildPick;
    private int _buildExistingPick;
    private int _buildTileFirst;

    /// <summary>每种房（按房名；空地一类）上次看的分类，下次进来接着看。</summary>
    private readonly Dictionary<string, string> _buildCategoryMemory = new();

    private bool BuildSheetOpen => _buildX >= 0;

    private void OpenBuildSheet(int regionId, int x, int y)
    {
        _buildRegion = regionId;
        _buildX = x;
        _buildY = y;
        _buildSegment = 0;
        _buildCategory = "";
        _buildPick = null;
        _buildExistingPick = 0;
        _buildTileFirst = 0;
    }

    private void CloseBuildSheet() => _buildX = _buildY = -1;

    /// <summary>领地格长按：直接进建造页并选中这一格。</summary>
    private void OpenBuildAt(int roomId)
    {
        var room = _vm.Hub.State.Territory.Room(roomId)!;
        _sheet = SheetKind.None;
        _push = PushPage.Build;
        OpenBuildSheet(room.RegionId, room.X, room.Y);
    }

    private Room? BuildRoom => _vm.Hub.State.Territory.RoomAt(_buildRegion, _buildX, _buildY);

    private string BuildSiteKey(BuildSite site) => site == BuildSite.Plot ? PlotName : BuildRoom!.Name;

    /// <summary>这一格分段的种类（0 可建、1 已有、2 门）：已建的房三段，空房「可建/门」，空地只有「可建」。</summary>
    private static int[] BuildSegmentKinds(BuildSite site) => site switch
    {
        BuildSite.Room => new[] { 0, 1, 2 },
        BuildSite.Vacant => new[] { 0, 2 },
        _ => new[] { 0 },
    };

    private readonly record struct BuildTab(string Key, string Label, int Ready, int Total);

    /// <summary>可建分段的页签与当前页签里的格子（按 Core 的排序）。</summary>
    private (List<BuildTab> Tabs, int Current, List<BuildOption> List) BuildChoices(BuildSite site, IReadOnlyList<BuildOption> options)
    {
        var target = site == BuildSite.Room ? BuildTarget.Facility : BuildTarget.Room;
        var tabs = HubSession.BuildCategories(target)
            .Select(c => new BuildTab(c.DefName, c.Label,
                options.Count(o => o.Category == c.DefName && o.State == BuildState.Ready),
                options.Count(o => o.Category == c.DefName)))
            .ToList();
        tabs.Add(new BuildTab(HubSession.AllCategoryLabel, HubSession.AllCategoryLabel,
            options.Count(o => o.State == BuildState.Ready), options.Count));
        var current = tabs.FindIndex(t => t.Key == _buildCategory);
        if (current < 0)
        {
            var key = BuildSiteKey(site);
            current = _buildCategoryMemory.TryGetValue(key, out var remembered) ? tabs.FindIndex(t => t.Key == remembered) : -1;
            if (current < 0)
                current = tabs.FindIndex(t => t.Ready > 0);
            if (current < 0)
                current = tabs.FindIndex(t => t.Total > 0);
            _buildCategory = tabs[current].Key;
        }
        var list = tabs[current].Key == HubSession.AllCategoryLabel
            ? options.ToList()
            : options.Where(o => o.Category == tabs[current].Key).ToList();
        return (tabs, current, list);
    }

    /// <summary>已有分段：这间房本身（拆房）＋房里每件设施。</summary>
    private List<(string Name, string Category, IReadOnlyList<string> Tags, Facility? Facility)> BuildExisting(Room room)
    {
        var items = new List<(string, string, IReadOnlyList<string>, Facility?)>
        {
            (room.Name, "", room.Tags.ToList(), null),
        };
        foreach (var f in _vm.Hub.State.Territory.Facilities.Where(f => f.RoomId == room.Id))
        {
            var def = DefDatabase<FacilityDef>.All.First(d => d.Name == f.Name);
            items.Add((f.Name, def.BuildCategory, f.RoomTag.Length > 0 ? new[] { f.RoomTag } : Array.Empty<string>(), f));
        }
        return items;
    }

    private void DrawBuildSheet()
    {
        var hub = _vm.Hub;
        var site = hub.SiteAt(_buildRegion, _buildX, _buildY);
        var sheet = PortraitLayout.BuildSheet;
        PortraitFrame.Dock(this, new Rect2(sheet.Position, sheet.Size + new Vector2(0, 80f)));
        var kinds = BuildSegmentKinds(site);
        _buildSegment = Math.Clamp(_buildSegment, 0, kinds.Length - 1);
        var kind = kinds[_buildSegment];
        var room = BuildRoom;
        var options = hub.BuildOptions(_buildRegion, _buildX, _buildY);

        var labels = kinds.Select(k => k switch
        {
            0 => $"{BuildSegmentNames[0]} {options.Count(o => o.State == BuildState.Ready)}",
            1 => $"{BuildSegmentNames[1]} {hub.State.Territory.FacilityCount(room!.Id)}/{Room.MaxFacilities}",
            _ => BuildSegmentNames[2],
        }).ToArray();
        var seg = PortraitLayout.BuildSegment;
        PortraitFrame.Segmented(this, seg, labels, _buildSegment);
        for (var i = 0; i < labels.Length; i++)
            _widgets.Add(new PortraitWidget(PortraitFrame.SegmentRect(seg, labels.Length, i), PortraitAction.BuildSegment, i, true,
                BuildSegmentNames[kinds[i]]));

        if (kind == 0)
            DrawBuildChoices(site, options);
        else if (kind == 1)
            DrawBuildExisting(room!);
        else
            DrawBuildDoors(room!);
        DrawPageTop("建造", site == BuildSite.Plot ? PlotName : room!.Name, "完成", PortraitAction.BuildDone, tall: true);
    }

    private void DrawBuildChoices(BuildSite site, IReadOnlyList<BuildOption> options)
    {
        var (tabs, current, list) = BuildChoices(site, options);
        for (var i = 0; i < tabs.Count; i++)
            DrawBuildTab(PortraitLayout.BuildTab(i), i, tabs[i], i == current);

        var top = PortraitLayout.BuildBodyTop(true);
        var pick = list.FindIndex(o => (o.Target, o.DefId) == _buildPick);
        if (pick < 0 && list.Count > 0)
        {
            pick = 0;
            _buildPick = (list[0].Target, list[0].DefId);
        }
        DrawBuildTiles(top, list.Count, pick, i =>
        {
            var o = list[i];
            return (o.Name, o.Category, o.State);
        });
        if (pick >= 0)
            DrawBuildCard(list[pick], site);
    }

    /// <summary>分类页签＝铭牌（选中走主铭牌：银白实心深字），上行类名、下行「能建/总数」。</summary>
    private void DrawBuildTab(Rect2 r, int index, BuildTab tab, bool on)
    {
        PortraitFrame.PlaqueBody(this, r, on, true, PortraitFrame.IsPressed(r));
        var name = on ? InkStyle.Bg : tab.Ready > 0 ? InkStyle.Line : InkStyle.Dim;
        InkDraw.TextBounded(this, new Rect2(r.Position.X + 8f, r.Position.Y + 14f, r.Size.X - 16f, 56f), tab.Label,
            PortraitLayout.FontBody, PortraitLayout.FontMeta, name, "cm");
        InkDraw.Text(this, new Vector2(r.GetCenter().X, r.Position.Y + 110f), $"{tab.Ready}/{tab.Total}", PortraitLayout.FontMeta,
            on ? InkStyle.WoodDark : InkStyle.Dim, "cm");
        _widgets.Add(new PortraitWidget(r, PortraitAction.BuildCategory, index, true, tab.Label));
    }

    /// <summary>格子区：一屏 4 列 × 若干行（见方 238），多出的按行拖。</summary>
    private void DrawBuildTiles(float top, int count, int pick, Func<int, (string Name, string Category, BuildState State)> item)
    {
        var rows = PortraitLayout.BuildTileRows(top);
        var totalRows = (count + PortraitLayout.BuildCols - 1) / PortraitLayout.BuildCols;
        _buildTileFirst = Math.Clamp(_buildTileFirst, 0, Math.Max(0, totalRows - rows));
        var first = _buildTileFirst * PortraitLayout.BuildCols;
        for (var i = first; i < count && i < first + rows * PortraitLayout.BuildCols; i++)
        {
            var (name, category, state) = item(i);
            var r = PortraitLayout.BuildTile(top, i - first);
            DrawBuildTile(r, name, category, state, i == pick);
            _widgets.Add(new PortraitWidget(r, PortraitAction.BuildTile, i, true, name));
        }
        RegisterScroll("build_tiles", PortraitLayout.BuildTileArea(top), totalRows, rows, _buildTileFirst,
            row => _buildTileFirst = row, PortraitLayout.BuildTileStep);
    }

    /// <summary>一格：卡片（选中走卡片选中态）＋类别线描图标＋名。能建骨白；建不了（缺钱料、标签不合、设施位满、等级不够）一律压成银灰，原因看详情卡。</summary>
    private void DrawBuildTile(Rect2 r, string name, string category, BuildState state, bool selected)
    {
        PortraitFrame.Card(this, r, selected, 26f);
        var color = state == BuildState.Ready ? InkStyle.Line : InkStyle.Dim;
        BuildGlyph(category)(this, r.GetCenter().X, r.Position.Y + 98f, 50f, color);
        InkDraw.TextBounded(this, new Rect2(r.Position.X + 10f, r.End.Y - 82f, r.Size.X - 20f, 60f), name,
            PortraitLayout.FontBody, PortraitLayout.FontMeta, color, "cm");
    }

    private void DrawBuildCard(BuildOption option, BuildSite site)
    {
        DrawBuildCardHead(option.Name, option.Tags);
        var i = 0;
        if (option.Bundled.Length > 0)
        {
            var r = PortraitLayout.BuildCond(i++);
            var cy = r.GetCenter().Y;
            PortraitGlyph.Diamond(this, r.Position.X + 22f, cy, 14f, InkStyle.Dim);
            PortraitFrame.CountTag(this, r.Position.X + 56f, cy, HubSession.BundledLabel, option.Bundled, true);
        }
        foreach (var c in option.Conditions.Take(6 - i))
            DrawBuildCond(PortraitLayout.BuildCond(i++), c);
        DrawBuildButtons(site == BuildSite.Plot ? "开拓并建造" : "建造", option.State == BuildState.Ready);
    }

    /// <summary>一条条件：打勾（满足，银灰）/打叉（不满足，骨白）→ 料与钱带物品图标 → 「名 现有/需要」。</summary>
    private void DrawBuildCond(Rect2 r, BuildCondition c)
    {
        var cy = r.GetCenter().Y;
        if (c.Met)
            PortraitGlyph.Check(this, r.Position.X + 22f, cy, 17f, InkStyle.Dim);
        else
            PortraitGlyph.Close(this, r.Position.X + 22f, cy, 17f, InkStyle.Line);
        var x = r.Position.X + 56f;
        if (c.Check == BuildCheck.Material && InkIcon.Has(c.Label))
        {
            InkIcon.Draw(this, c.Label, new Rect2(x, cy - 24f, 48f, 48f));
            x += 60f;
        }
        else if (c.Check == BuildCheck.Money)
        {
            PortraitGlyph.Coin(this, x + 22f, cy, 20f, InkStyle.Dim);
            x += 60f;
        }
        if (c.Check == BuildCheck.Tag)
            InkDraw.Text(this, new Vector2(x, cy), c.Label, PortraitLayout.FontMeta, c.Met ? InkStyle.Dim : InkStyle.Line, "lm");
        else
            PortraitFrame.CountTag(this, x, cy, c.Label, c.Value, !c.Met);
    }

    /// <summary>详情卡底板（哥特框）与标题带：正中名（标题字号）压窗花底纹，名下一行题签。</summary>
    private void DrawBuildCardHead(string name, IReadOnlyList<string> tags)
    {
        var card = PortraitLayout.BuildCard;
        PortraitFrame.GothicFrame(this, card, InkStyle.Panel);
        PortraitTracery.TitleBackdrop(this, PortraitLayout.BuildCardBand);
        var band = PortraitLayout.BuildCardBand;
        InkDraw.TextBounded(this, new Rect2(band.Position.X, PortraitLayout.BuildCardNameY - 40f, band.Size.X, 80f), name,
            PortraitLayout.FontTitle, PortraitLayout.FontMeta, InkStyle.Line, "cm");
        if (tags.Count > 0)
        {
            var w = PortraitFrame.TagWidth(tags);
            var x = card.GetCenter().X - w / 2f;
            PortraitFrame.TagLine(this, x, PortraitLayout.BuildCardTagY, tags, x + w, InkStyle.Dim);
        }
    }

    private void DrawBuildButtons(string label, bool enabled)
    {
        var hub = _vm.Hub;
        var undo = hub.CanUndoBuild;
        if (undo)
        {
            // 两行：「撤销」＋刚建的名（铭牌自带的次行），四字名在 340 宽的钮里也不挤。
            PortraitFrame.Plaque(this, PortraitLayout.BuildUndo, "撤销", sub: hub.LastBuildName);
            _widgets.Add(new PortraitWidget(PortraitLayout.BuildUndo, PortraitAction.BuildUndo, 0, true, "撤销"));
        }
        var main = PortraitLayout.BuildMain(undo);
        PortraitFrame.Plaque(this, main, label, primary: true, enabled: enabled, size: PortraitLayout.FontTitle);
        _widgets.Add(new PortraitWidget(main, PortraitAction.BuildMain, 0, enabled, label));
    }

    private void DrawBuildExisting(Room room)
    {
        var items = BuildExisting(room);
        _buildExistingPick = Math.Clamp(_buildExistingPick, 0, items.Count - 1);
        var top = PortraitLayout.BuildBodyTop(false);
        DrawBuildTiles(top, items.Count, _buildExistingPick, i => (items[i].Name, items[i].Category, BuildState.Ready));
        var picked = items[_buildExistingPick];
        DrawBuildCardHead(picked.Name, picked.Tags);
        var territory = _vm.Hub.State.Territory;
        var cell = 0;
        List<RecipeCost> refund;
        if (picked.Facility == null)
        {
            var r = PortraitLayout.BuildCond(cell++);
            var count = territory.FacilityCount(room.Id);
            PortraitGlyph.Chest(this, r.Position.X + 22f, r.GetCenter().Y, 18f, InkStyle.Dim);
            PortraitFrame.CountTag(this, r.Position.X + 56f, r.GetCenter().Y, HubSession.SlotLabel, $"{count}/{Room.MaxFacilities}", true);
            var costs = new List<RecipeCost>(room.MaterialCost);
            foreach (var f in territory.Facilities.Where(f => f.RoomId == room.Id))
                costs.AddRange(f.MaterialCost);
            refund = HubSession.DemolishRefund(HubSession.MergeCosts(costs, Array.Empty<RecipeCost>()));
        }
        else
            refund = HubSession.DemolishRefund(picked.Facility.MaterialCost);
        // 拆了返还什么（Core 的六成返还）：每样一格，物品图标＋「名 +件数」。
        foreach (var back in refund.Take(6 - cell))
        {
            var r = PortraitLayout.BuildCond(cell++);
            var cy = r.GetCenter().Y;
            PortraitGlyph.Plus(this, r.Position.X + 22f, cy, 14f, InkStyle.Dim);
            var x = r.Position.X + 56f;
            if (InkIcon.Draw(this, back.ItemId, new Rect2(x, cy - 24f, 48f, 48f)))
                x += 60f;
            PortraitFrame.CountTag(this, x, cy, back.ItemId, $"+{back.Count}", true);
        }
        DrawBuildButtons("拆除", picked.Facility != null || _vm.Hub.PlayerRoomId != room.Id);
    }

    private void DrawBuildDoors(Room room)
    {
        var top = PortraitLayout.BuildBodyTop(false);
        var doors = DoorRows(room.Id);
        for (var i = 0; i < doors.Count; i++)
        {
            var rect = PortraitLayout.BuildRow(top, i);
            var door = doors[i];
            var cy = rect.GetCenter().Y;
            PortraitFrame.Card(this, rect, door.Open, 24f);
            PortraitFrame.Tag(this, new Vector2(rect.Position.X + 32f, cy - 36f), "门", 72f, door.Open);
            PortraitFrame.TagLine(this, rect.Position.X + 160f, cy,
                new[] { Territory.DirName(door.Dir), door.Neighbor }, rect.Position.X + rect.Size.X * 0.62f, InkStyle.Line, PortraitLayout.FontBody);
            InkDraw.TextBounded(this, new Rect2(rect.Position.X + rect.Size.X * 0.62f, rect.Position.Y, rect.Size.X * 0.38f - 44f, rect.Size.Y),
                door.Open ? "连通" : "墙", PortraitLayout.FontBody, PortraitLayout.FontMeta, door.Open ? InkStyle.Line : InkStyle.Dim, "rm");
            _widgets.Add(new PortraitWidget(rect, PortraitAction.DevelopmentDoor, (int)door.Dir, true, $"{Territory.DirName(door.Dir)}门"));
        }
    }

    /// <summary>分类图标：设施与房间各类一枚线描字形（数据里没有单件图标）。房间本身用城堡。</summary>
    private static Action<CanvasItem, float, float, float, Color> BuildGlyph(string category) => category switch
    {
        "Build_Furniture" or "BuildRoom_Living" => PortraitGlyph.Person,
        "Build_Production" or "BuildRoom_Production" => PortraitGlyph.Hammer,
        "Build_Farm" or "BuildRoom_Farm" => PortraitGlyph.Leaf,
        "Build_Gather" => PortraitGlyph.Sun,
        "Build_Storage" => PortraitGlyph.Chest,
        "Build_Leisure" or "BuildRoom_Leisure" => PortraitGlyph.Book,
        "Build_Faith" => PortraitGlyph.Bell,
        "Build_Military" or "BuildRoom_Military" => PortraitGlyph.Swords,
        "BuildRoom_Shop" => PortraitGlyph.Coin,
        _ => PortraitGlyph.Castle,
    };

    private bool ExecuteBuildSheet(PortraitWidget widget)
    {
        var hub = _vm.Hub;
        switch (widget.Action)
        {
            case PortraitAction.BuildDone:
                CloseBuildSheet();
                RequestBack();
                return true;
            case PortraitAction.BuildUndo:
                hub.UndoLastBuild();
                return true;
            case PortraitAction.BuildSegment:
                _buildSegment = widget.Index;
                _buildTileFirst = 0;
                _buildExistingPick = 0;
                return true;
        }
        if (!BuildSheetOpen)
            return false;
        var site = hub.SiteAt(_buildRegion, _buildX, _buildY);
        var kind = BuildSegmentKinds(site)[_buildSegment];
        switch (widget.Action)
        {
            case PortraitAction.BuildCategory:
            {
                var (tabs, _, _) = BuildChoices(site, hub.BuildOptions(_buildRegion, _buildX, _buildY));
                _buildCategory = tabs[widget.Index].Key;
                _buildCategoryMemory[BuildSiteKey(site)] = _buildCategory;
                _buildTileFirst = 0;
                _buildPick = null;
                return true;
            }
            case PortraitAction.BuildTile when kind == 0:
            {
                var (_, _, list) = BuildChoices(site, hub.BuildOptions(_buildRegion, _buildX, _buildY));
                _buildPick = (list[widget.Index].Target, list[widget.Index].DefId);
                return true;
            }
            case PortraitAction.BuildTile:
                _buildExistingPick = widget.Index;
                return true;
            case PortraitAction.BuildMain when kind == 0:
            {
                var (_, _, list) = BuildChoices(site, hub.BuildOptions(_buildRegion, _buildX, _buildY));
                var option = list.First(o => (o.Target, o.DefId) == _buildPick);
                hub.BuildAt(_buildRegion, _buildX, _buildY, option.Target, option.DefId);
                return true;
            }
            case PortraitAction.BuildMain:
            {
                var room = BuildRoom!;
                var picked = BuildExisting(room)[_buildExistingPick];
                Confirm("拆除", picked.Name, () =>
                {
                    hub.BeginOperation();
                    if (picked.Facility != null)
                        hub.RemoveFacility(picked.Facility.Id);
                    else if (hub.RemoveRoom(room.Id))
                        CloseBuildSheet();
                    _buildExistingPick = 0;
                    QueueRedraw();
                });
                return true;
            }
        }
        return false;
    }

    private IReadOnlyList<PortraitRegion> BuildSheetRegions()
    {
        var site = _vm.Hub.SiteAt(_buildRegion, _buildX, _buildY);
        var kind = BuildSegmentKinds(site)[Math.Clamp(_buildSegment, 0, BuildSegmentKinds(site).Length - 1)];
        var segEnd = PortraitLayout.BuildSegment.End.Y + 10f;
        var regions = new List<PortraitRegion>
        {
            new("page_top", PortraitLayout.PageTop),
            new("build_segment", new Rect2(0, PortraitLayout.BuildTop, PortraitLayout.CanvasWidth, segEnd - PortraitLayout.BuildTop)),
        };
        var bodyTop = segEnd;
        if (kind == 0)
        {
            var tabs = PortraitLayout.BuildTabs;
            regions.Add(new PortraitRegion("build_tabs", new Rect2(0, segEnd, PortraitLayout.CanvasWidth, tabs.End.Y + 12f - segEnd)));
            bodyTop = tabs.End.Y + 12f;
        }
        if (kind == 2)
        {
            regions.Add(new PortraitRegion("build_body", new Rect2(0, bodyTop, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - bodyTop)));
            return regions;
        }
        var cardTop = PortraitLayout.BuildCard.Position.Y - 12f;
        regions.Add(new PortraitRegion("build_body", new Rect2(0, bodyTop, PortraitLayout.CanvasWidth, cardTop - bodyTop)));
        regions.Add(new PortraitRegion("build_card", new Rect2(0, cardTop, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - cardTop)));
        return regions;
    }
}
