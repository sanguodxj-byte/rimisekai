using System;
using System.Collections.Generic;
using Rimisekai.Housing;

namespace Rimisekai.Ink;

/// <summary>
/// 把 Core 的状态摊成页面内容：左栏是列表，右栏是选中项的详情与动作。
/// 只读，不含逻辑；页面上点的每一下都由 InkHubScreen 转成 HubSession 调用。
/// </summary>
public static class InkPageBuilder
{
    public static InkPageModel Build(InkViewModel vm, InkPage page, in InkPageQuery q) => page switch
    {
        InkPage.Stock => Stock(vm, q),
        InkPage.Trade => Trade(vm, q),
        InkPage.Craft => Craft(vm, q),
        InkPage.Develop => Develop(vm, q),
        _ => new InkPageModel { Page = InkPage.None, Title = "" },
    };

    /// <summary>把界面传来的选中下标收进列表范围；空列表返回 -1。</summary>
    private static int ClampSel(int selected, int count) =>
        count == 0 ? -1 : selected < 0 ? 0 : System.Math.Min(selected, count - 1);

    /// <summary>滚动窗口页首：对齐整页并夹在 0..(总数-一页行数) 之间。</summary>
    private static int ClampFirst(int first, int count)
    {
        var visible = InkLayout.ListVisibleRows(InkLayout.FullListArea);
        var max = System.Math.Max(0, count - visible);
        return System.Math.Clamp(first / visible * visible, 0, max);
    }

    /// <summary>搜索匹配：空词全过，否则按名称包含判断。</summary>
    private static bool Matches(string name, string search) =>
        search.Length == 0 || name.Contains(search, StringComparison.OrdinalIgnoreCase);

    /// <summary>库存：背包物品；搜索按名称，筛选"有报价"，按名称/数量排序。</summary>
    private static InkPageModel Stock(InkViewModel vm, in InkPageQuery q)
    {
        var filters = new[] { "全部", "有报价" };
        var sorts = new[] { "名称", "数量" };
        var filter = System.Math.Clamp(q.Filter, 0, filters.Length - 1);
        var sort = System.Math.Clamp(q.Sort, 0, sorts.Length - 1);
        var search = q.Search.Trim();

        var candidates = new List<(InkPageRow Row, string Id, int Count)>();
        foreach (var pair in vm.Hub.Stock())
        {
            if (pair.Value <= 0)
                continue;
            var name = ItemName(vm, pair.Key);
            if (!Matches(name, search))
                continue;
            if (filter == 1 && !HasOffer(vm, pair.Key))
                continue;
            candidates.Add((new InkPageRow { Name = name, Value = $"×{pair.Value}" }, pair.Key, pair.Value));
        }
        candidates.Sort((a, b) => sort == 1
            ? a.Count.CompareTo(b.Count)
            : string.CompareOrdinal(a.Row.Name, b.Row.Name));
        if (q.SortDesc)
            candidates.Reverse();

        var rows = new List<InkPageRow>();
        foreach (var c in candidates)
            rows.Add(c.Row);

        var sel = ClampSel(q.Selected, candidates.Count);
        var title = "";
        var note = "";
        if (sel >= 0)
        {
            title = candidates[sel].Row.Name;
            note = $"持有 ×{candidates[sel].Count}\n{MarketNote(vm, candidates[sel].Id)}";
        }

        return new InkPageModel
        {
            Page = InkPage.Stock,
            Title = "库存",
            Rows = rows,
            SelectedRow = sel,
            ListFirst = ClampFirst(q.PageFirst, candidates.Count),
            DetailTitle = title,
            DetailNote = note,
            Search = q.Search,
            SearchFocused = q.SearchFocused,
            Filters = filters,
            ActiveFilter = filter,
            Sorts = sorts,
            ActiveSort = sort,
            ActiveSortDesc = q.SortDesc,
            HasControls = true,
            EmptyHint = search.Length > 0 || filter > 0 ? "没有匹配的物品。" : "背包是空的。",
        };
    }

    /// <summary>市场对某物品的报价说明；没有登记时如实说明。</summary>
    private static string MarketNote(InkViewModel vm, string itemId)
    {
        foreach (var offer in vm.Hub.State.Territory.Market)
        {
            if (offer.ItemId == itemId)
                return $"市场报价：买 ${offer.BuyPrice} / 卖 ${offer.SellPrice}";
        }
        return "市场暂无报价。";
    }

    /// <summary>交易：市场报价；搜索按名称，筛选"买得起/可卖出"，按名称/买价排序。</summary>
    private static InkPageModel Trade(InkViewModel vm, in InkPageQuery q)
    {
        var money = vm.Hub.State.Money;
        var offers = new List<MarketOffer>(vm.Hub.State.Territory.Market);

        var filters = new[] { "全部", "买得起", "可卖出" };
        var sorts = new[] { "名称", "买价" };
        var filter = System.Math.Clamp(q.Filter, 0, filters.Length - 1);
        var sort = System.Math.Clamp(q.Sort, 0, sorts.Length - 1);
        var search = q.Search.Trim();

        var candidates = new List<(InkPageRow Row, MarketOffer Offer)>();
        foreach (var offer in offers)
        {
            var name = ItemName(vm, offer.ItemId);
            if (!Matches(name, search))
                continue;
            var held = vm.Hub.State.Roster.Master?.Bag.Get(offer.ItemId) ?? 0;
            if (filter == 1 && money < offer.BuyPrice)
                continue;
            if (filter == 2 && held <= 0)
                continue;
            candidates.Add((new InkPageRow
            {
                Name = name,
                Value = $"持有 {held}",
                Note = $"买 ${offer.BuyPrice} / 卖 ${offer.SellPrice}",
            }, offer));
        }
        candidates.Sort((a, b) => sort == 1
            ? a.Offer.BuyPrice.CompareTo(b.Offer.BuyPrice)
            : string.CompareOrdinal(a.Row.Name, b.Row.Name));
        if (q.SortDesc)
            candidates.Reverse();

        var rows = new List<InkPageRow>();
        foreach (var c in candidates)
            rows.Add(c.Row);

        var sel = ClampSel(q.Selected, candidates.Count);
        var title = "";
        var note = "";
        var actions = new List<InkPageRow>();
        if (sel >= 0)
        {
            var offer = candidates[sel].Offer;
            var held = vm.Hub.State.Roster.Master?.Bag.Get(offer.ItemId) ?? 0;
            title = ItemName(vm, offer.ItemId);
            note = $"买入 ${offer.BuyPrice}\n卖出 ${offer.SellPrice}\n持有 {held}";
            actions.Add(new InkPageRow
            {
                Name = "买入一份",
                Action = InkPageAction.Buy,
                TargetId = offer.ItemId,
                Enabled = money >= offer.BuyPrice,
            });
            actions.Add(new InkPageRow
            {
                Name = "卖出一份",
                Action = InkPageAction.Sell,
                TargetId = offer.ItemId,
                Enabled = held > 0,
            });
        }

        return new InkPageModel
        {
            Page = InkPage.Trade,
            Title = "交易",
            Rows = rows,
            SelectedRow = sel,
            ListFirst = ClampFirst(q.PageFirst, candidates.Count),
            DetailTitle = title,
            DetailNote = note,
            DetailActions = actions,
            Search = q.Search,
            SearchFocused = q.SearchFocused,
            Filters = filters,
            ActiveFilter = filter,
            Sorts = sorts,
            ActiveSort = sort,
            ActiveSortDesc = q.SortDesc,
            HasControls = true,
            EmptyHint = search.Length > 0 || filter > 0 ? "没有匹配的报价。" : "这里没有可交易的东西。",
        };
    }

    /// <summary>制作：配方列表；搜索按名称，筛选"可制作/缺材料"，按名称/材料数排序。</summary>
    private static InkPageModel Craft(InkViewModel vm, in InkPageQuery q)
    {
        var territory = vm.Hub.State.Territory;
        var master = vm.Hub.State.Roster.Master;
        var recipes = new List<Recipe>(territory.Recipes);

        var filters = new[] { "全部", "可制作", "缺材料" };
        var sorts = new[] { "名称", "材料数" };
        var filter = System.Math.Clamp(q.Filter, 0, filters.Length - 1);
        var sort = System.Math.Clamp(q.Sort, 0, sorts.Length - 1);
        var search = q.Search.Trim();

        var candidates = new List<(InkPageRow Row, Recipe Recipe)>();
        foreach (var recipe in recipes)
        {
            var name = ItemName(vm, recipe.ItemId);
            if (!Matches(name, search))
                continue;
            var canPay = territory.CanPayWith(master, recipe.Costs);
            if (filter == 1 && !canPay)
                continue;
            if (filter == 2 && canPay)
                continue;
            candidates.Add((new InkPageRow
            {
                Name = name,
                Value = $"×{recipe.OutputCount}",
                Note = CostText(vm, recipe),
            }, recipe));
        }
        candidates.Sort((a, b) => sort == 1
            ? a.Recipe.Costs.Count.CompareTo(b.Recipe.Costs.Count)
            : string.CompareOrdinal(a.Row.Name, b.Row.Name));
        if (q.SortDesc)
            candidates.Reverse();

        var rows = new List<InkPageRow>();
        foreach (var c in candidates)
            rows.Add(c.Row);

        var sel = ClampSel(q.Selected, candidates.Count);
        var title = "";
        var note = "";
        var actions = new List<InkPageRow>();
        if (sel >= 0)
        {
            var recipe = candidates[sel].Recipe;
            title = $"{ItemName(vm, recipe.ItemId)} ×{recipe.OutputCount}";
            var lines = new List<string> { "所需材料：" };
            if (recipe.Costs.Count == 0)
                lines.Add("　无需材料");
            foreach (var cost in recipe.Costs)
                lines.Add($"　{ItemName(vm, cost.ItemId)}×{cost.Count}（持有 {territory.CountWith(master, cost.ItemId)}）");
            note = string.Join("\n", lines);
            actions.Add(new InkPageRow
            {
                Name = "制作一份",
                Action = InkPageAction.Craft,
                TargetId = recipe.ItemId,
                Enabled = territory.CanPayWith(master, recipe.Costs),
            });
        }

        return new InkPageModel
        {
            Page = InkPage.Craft,
            Title = "制作",
            Rows = rows,
            SelectedRow = sel,
            ListFirst = ClampFirst(q.PageFirst, candidates.Count),
            DetailTitle = title,
            DetailNote = note,
            DetailActions = actions,
            Search = q.Search,
            SearchFocused = q.SearchFocused,
            Filters = filters,
            ActiveFilter = filter,
            Sorts = sorts,
            ActiveSort = sort,
            ActiveSortDesc = q.SortDesc,
            HasControls = true,
            EmptyHint = search.Length > 0 || filter > 0 ? "没有匹配的配方。" : "还没有可用的配方。",
        };
    }

    /// <summary>
    /// 开发：对领地的编辑，全屏四区域。
    /// 左上：房间网格（已放置房间按坐标；非空房间右上白 X 拆除房间）；
    /// 右上：设施列表（全部设施实例，含未放置；底部为可建造设施目录，点击即建造）；
    /// 左下：房间列表（含未放置房间）；右下：共用详情——点哪个显示哪个。
    /// 开拓只在据点地图上进行。
    /// </summary>
    private static InkPageModel Develop(InkViewModel vm, in InkPageQuery q)
    {
        var territory = vm.Hub.State.Territory;
        var openRooms = new List<Room>();
        var unplacedRooms = new List<Room>();
        foreach (var room in vm.Rooms())
        {
            if (room.X < 0 || room.Y < 0)
                unplacedRooms.Add(room);
            else if (room.Open)
                openRooms.Add(room);
        }
        var sel = ClampSel(q.Selected, openRooms.Count);

        // 左上房间格：已放置的房间按坐标落位。
        var cells = new List<InkDevRoomCell>();
        for (var i = 0; i < openRooms.Count; i++)
        {
            var room = openRooms[i];
            var facs = territory.Facilities.FindAll(f => f.RoomId == room.Id);
            cells.Add(new InkDevRoomCell
            {
                Id = room.Id,
                X = room.X,
                Y = room.Y,
                Name = room.Name,
                NonEmpty = facs.Count > 0,
                Removable = vm.Hub.PlayerRoomId != room.Id,
                Selected = i == sel,
            });
        }

        // 右上：设施列表——已放置的标所在房间，未放置的标“未放置”。
        var facilityRows = new List<InkDevRow>();
        foreach (var f in territory.Facilities)
        {
            facilityRows.Add(new InkDevRow
            {
                Kind = f.RoomId >= 0 ? InkDevRowKind.Facility : InkDevRowKind.FacilityUnplaced,
                Id = f.Id,
                Name = f.Name,
                Note = f.RoomId >= 0 ? RoomName(vm, f.RoomId) : "未放置",
            });
        }
        var facSel = ClampSel(q.SelectedFacility, facilityRows.Count);
        for (var i = 0; i < facilityRows.Count; i++)
            facilityRows[i] = facilityRows[i] with { Selected = i == facSel };

        // 左下：房间行——已开拓在前，未开拓（建好未放置）在后。
        var roomRows = new List<InkDevRow>();
        foreach (var room in openRooms)
            roomRows.Add(new InkDevRow { Kind = InkDevRowKind.Room, Id = room.Id, Name = room.Name });
        foreach (var room in unplacedRooms)
            roomRows.Add(new InkDevRow { Kind = InkDevRowKind.RoomUnplaced, Id = room.Id, Name = room.Name });
        var roomSel = ClampSel(q.Selected, roomRows.Count);
        for (var i = 0; i < roomRows.Count; i++)
            roomRows[i] = roomRows[i] with { Selected = i == roomSel };

        // 右上底部：可建造设施目录（点击行即花材料建造，建成后进入未放置）。
        var facilityCatalog = new List<InkPageRow>();
        foreach (var def in vm.Hub.State.Catalog.Facilities.Values)
        {
            if (!def.Buildable)
                continue;
            facilityCatalog.Add(new InkPageRow
            {
                Name = def.Name,
                Note = MaterialsText(vm, def.MaterialCost),
                Action = InkPageAction.BuildDef,
                TargetNumber = def.Id,
                Enabled = territory.CanPayWith(vm.Hub.State.Roster.Master, def.MaterialCost),
            });
        }

        // 左下底部：可建造房间目录（点击行即花材料建造，建成后未放置）。
        var roomCatalog = new List<InkPageRow>();
        foreach (var def in vm.Hub.State.Catalog.Rooms.Values)
        {
            if (!def.Buildable)
                continue;
            roomCatalog.Add(new InkPageRow
            {
                Name = def.Name,
                Note = MaterialsText(vm, def.MaterialCost),
                Action = InkPageAction.BuildRoom,
                TargetNumber = def.Id,
                Enabled = territory.CanPayWith(vm.Hub.State.Roster.Master, def.MaterialCost),
            });
        }

        // 右上：共用详情——选中设施时显示设施详情（拆除/放置按钮），
        // 否则显示选中房间的说明。
        var detailTitle = "";
        var detailNote = "";
        var detailActions = new List<InkPageRow>();
        if (facSel >= 0 && facilityRows.Count > 0)
        {
            var row = facilityRows[facSel];
            detailTitle = row.Name;
            if (row.Kind == InkDevRowKind.Facility)
            {
                var roomName = RoomName(vm, GetFacilityRoom(vm, row.Id));
                detailNote = $"已放置在{roomName}。\n拆除返还 60% 材料。";
                detailActions.Add(new InkPageRow
                {
                    Name = $"拆除{row.Name}",
                    Action = InkPageAction.RemoveFacility,
                    TargetNumber = row.Id,
                    Note = row.Name,
                });
            }
            else
            {
                detailNote = "未放置。\n点击下方按钮放进选中的房间。";
                detailActions.Add(new InkPageRow
                {
                    Name = $"放置{row.Name}",
                    Action = InkPageAction.PlaceFacility,
                    TargetNumber = row.Id,
                    Note = row.Name,
                });
            }
        }

        return new InkPageModel
        {
            Page = InkPage.Develop,
            Title = "开发",
            Dev = new InkDevModel
            {
                Rooms = cells,
                RoomId = roomSel >= 0 ? roomRows[roomSel].Id : -1,
                FacilityRows = facilityRows,
                SelectedFacility = facSel,
                FacilityCatalog = facilityCatalog,
                RoomRows = roomRows,
                SelectedRoom = roomSel,
                RoomCatalog = roomCatalog,
                DetailTitle = detailTitle,
                DetailNote = detailNote,
                DetailActions = detailActions,
                PlacingFacility = q.PlacingFacility,
                PlacingRoom = q.PlacingRoom,
            },
            EmptyHint = "当前区域没有房间。",
        };
    }

    /// <summary>按设施 Id 找它所在的房间 Id。</summary>
    private static int GetFacilityRoom(InkViewModel vm, int facilityId)
    {
        var f = vm.Hub.State.Territory.Facilities.Find(x => x.Id == facilityId);
        return f?.RoomId ?? -1;
    }

    /// <summary>按房间 Id 找房间名。</summary>
    private static string RoomName(InkViewModel vm, int roomId)
    {
        var room = vm.Hub.State.Territory.Rooms.Find(r => r.Id == roomId);
        return room?.Name ?? "未知房间";
    }

    /// <summary>材料清单文本：物品名×数量，空格分隔。</summary>
    private static string MaterialsText(InkViewModel vm,
        System.Collections.Generic.IReadOnlyList<RecipeCost> costs)
    {
        if (costs.Count == 0)
            return "无需材料";
        var parts = new List<string>(costs.Count);
        foreach (var cost in costs)
            parts.Add($"{ItemName(vm, cost.ItemId)}×{cost.Count}");
        return string.Join("　", parts);
    }

    /// <summary>物品显示名。目录里没有登记时退回 Id，避免显示空白。</summary>
    private static string ItemName(InkViewModel vm, string itemId) =>
        vm.Hub.State.Catalog.Items.TryGetValue(itemId, out var def) && def.Name.Length > 0
            ? def.Name
            : itemId;

    /// <summary>市场是否登记了某物品的报价。</summary>
    private static bool HasOffer(InkViewModel vm, string itemId)
    {
        foreach (var offer in vm.Hub.State.Territory.Market)
        {
            if (offer.ItemId == itemId)
                return true;
        }
        return false;
    }

    private static string CostText(InkViewModel vm, Recipe recipe)
    {
        if (recipe.Costs.Count == 0)
            return "无材料";
        var parts = new List<string>(recipe.Costs.Count);
        foreach (var cost in recipe.Costs)
            parts.Add($"{ItemName(vm, cost.ItemId)}×{cost.Count}");
        return string.Join("　", parts);
    }
}
