using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Defs;
using Rimisekai.Housing;
using Rimisekai.Hub;

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
        InkPage.Develop => Develop(vm, q),
        InkPage.CombatLog => CombatLog(vm, q),
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

    /// <summary>库存：背包物品；搜索按名称，筛选"可交易"，按名称/数量排序。</summary>
    private static InkPageModel Stock(InkViewModel vm, in InkPageQuery q)
    {
        var filters = new[] { "全部", "可交易" };
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
            if (filter == 1 && vm.Hub.State.Territory.Listing(pair.Key) == null)
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
        if (sel >= 0 && sel < candidates.Count)
        {
            var itemId = candidates[sel].Id;
            var count = candidates[sel].Count;
            title = candidates[sel].Row.Name;

            var thing = Defs.Items.Get(itemId);
            var weapon = vm.Hub.State.Territory.Weapons.Get(itemId);

            var lines = new List<string>();
            lines.Add($"×{count}");

            var catName = "";
            if (thing != null && !string.IsNullOrEmpty(thing.Category))
            {
                var cat = DefDatabase<ThingCategoryDef>.Get(thing.Category);
                catName = cat != null && cat.Label.Length > 0 ? cat.Label : thing.Category;
            }
            else if (weapon != null)
                catName = $"武器·{InkText.Weapon(weapon.Type)}";

            if (!string.IsNullOrEmpty(catName))
                lines.Add(catName);

            if (thing != null && thing.MarketValue > 0)
                lines.Add($"{thing.MarketValue}G");
            else if (weapon != null && weapon.Value > 0)
                lines.Add($"{weapon.Value}G");

            if (thing != null && thing.IsFood)
            {
                lines.Add(InkText.FoodTier(thing.FoodTier));
                lines.Add($"营养+{thing.Nutrition}　心情{thing.MoodBonus:+0;-0}");
            }
            if (weapon != null)
            {
                lines.Add(Defs.QualityOf.Label(weapon.Quality));
                if (weapon.Material != null)
                    lines.Add(weapon.Material.Label.Length > 0 ? weapon.Material.Label : weapon.Material.DefName);
            }

            if (thing != null && !string.IsNullOrEmpty(thing.Description))
            {
                lines.Add("");
                lines.Add(thing.Description);
            }

            note = string.Join("\n", lines);
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

    /// <summary>交易页不做自动选中：没点过就是没选中（与列表页取第一行不同）。</summary>
    private static int Pick(int selected, int count) =>
        selected < 0 || selected >= count ? -1 : selected;

    /// <summary>
    /// 交易：三面板——左栏领地库存（玩家持有、市场肯收的），
    /// 右栏市场库存（今日有货、市场肯卖的）。没有报价单：
    /// 价格只由基准价 × 当日系数决定，卖出再打商人抽成，与成交结算同一口径。
    /// 房间不是货，两栏都不会出现。
    /// </summary>
    private static InkPageModel Trade(InkViewModel vm, in InkPageQuery q)
    {
        var hub = vm.Hub;
        var territory = hub.State.Territory;

        // 左栏：领地库存——只列玩家背包里的东西（含买回来的武器实例）。
        // 据点里的设施一律不上交易页：它们归开发页管（建/拆/摆放），不是货。
        var held = new List<InkTradeRow>();
        foreach (var pair in hub.Stock())
        {
            if (pair.Value <= 0)
                continue;
            var listing = territory.Listing(pair.Key);
            if (listing == null)
                continue;
            var row = listing.Value;
            var sell = hub.TradePrices(row, selling: true);
            if (sell <= 0)
                continue;
            held.Add(new InkTradeRow(row.ItemId, ItemName(vm, row.ItemId),
                $"{pair.Value}", sell));
        }
        held.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        // 右栏：市场库存——只列今日有货的。无货即不在架上，从左栏照样卖得掉。
        var market = new List<InkTradeRow>();
        foreach (var row in territory.Listings())
        {
            if (row.Stock <= 0)
                continue;
            var buy = hub.TradePrices(row, selling: false);
            if (buy <= 0)
                continue;
            market.Add(new InkTradeRow(row.ItemId, ItemName(vm, row.ItemId), "", buy));
        }
        market.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        var selHeld = Pick(q.SelectedHeld, held.Count);
        var selMarket = Pick(q.SelectedMarket, market.Count);

        var sellName = "";
        var sellPrice = 0L;
        if (selHeld >= 0 && selHeld < held.Count)
        {
            sellName = held[selHeld].Name;
            sellPrice = held[selHeld].Price;
        }

        var buyName = "";
        var buyPrice = 0L;
        if (selMarket >= 0 && selMarket < market.Count)
        {
            buyName = market[selMarket].Name;
            buyPrice = market[selMarket].Price;
        }

        return new InkPageModel
        {
            Page = InkPage.Trade,
            Title = "交易",
            Trade = new InkTradeModel
            {
                Money = hub.State.Money,
                Held = held,
                Market = market,
                SelectedHeld = selHeld,
                SelectedMarket = selMarket,
                HeldFirst = System.Math.Clamp(q.HeldFirst, 0,
                    System.Math.Max(0, held.Count - InkLayout.TradeVisibleRows(InkLayout.TradePlayerPanel))),
                MarketFirst = System.Math.Clamp(q.MarketFirst, 0,
                    System.Math.Max(0, market.Count - InkLayout.TradeVisibleRows(InkLayout.TradeMarketPanel))),
                TradeAvailable = hub.TradeAvailable,
                SellName = sellName,
                SellPrice = sellPrice,
                BuyName = buyName,
                BuyPrice = buyPrice,
                Notice = q.Notice,
            },
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
            var costNote = CostText(vm, recipe);
            candidates.Add((new InkPageRow
            {
                Name = name,
                Value = $"×{recipe.OutputCount}",
                Note = costNote,
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
        if (sel >= 0 && sel < candidates.Count)
        {
            var recipe = candidates[sel].Recipe;
            title = $"{ItemName(vm, recipe.ItemId)} ×{recipe.OutputCount}";
            var lines = new List<string>();

            var productThing = Defs.Items.Get(recipe.ItemId);
            if (productThing != null && !string.IsNullOrEmpty(productThing.Category))
            {
                var cat = DefDatabase<ThingCategoryDef>.Get(productThing.Category);
                var catName = cat != null && cat.Label.Length > 0 ? cat.Label : productThing.Category;
                lines.Add(catName);
            }
            lines.Add(InkText.LifeSkill(recipe.Skill));
            lines.Add(InkText.ActionKind(recipe.Station));

            if (productThing != null && !string.IsNullOrEmpty(productThing.Description))
            {
                lines.Add("");
                lines.Add(productThing.Description);
            }

            lines.Add("");
            lines.Add("所需材料");
            if (recipe.Costs.Count == 0)
                lines.Add("　无需材料");
            foreach (var cost in recipe.Costs)
            {
                lines.Add($"　◇ {ItemName(vm, cost.ItemId)} ×{cost.Count}");
            }
            note = string.Join(Environment.NewLine, lines);

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
    /// 开发：对领地的编辑，全屏五区域（2026-10-01 主人定）。
    /// 左上：领地网格——已开发房间按坐标落位；**挨着已开发地方的未开发房间**也画出来，
    ///       点它弹确认窗（花材料和钱开拓成一间空房）；非空房间右上白 X 拆房间。
    /// 右上：**只列当前选中房间的设施**。
    /// 左下：**只列已建但还没安装的房间**（选中后点网格空格放上去）。
    /// 中下：操作面板——建造设施／建造房间／拆除／安置／退出，所有操作按钮都在这里。
    /// 右下：详情，只有文字，不画内部边框。
    /// </summary>
    private static InkPageModel Develop(InkViewModel vm, InkPageQuery q)
    {
        var territory = vm.Hub.State.Territory;
        var map = vm.Rooms();
        var openRooms = new List<Room>();
        var unplacedRooms = new List<Room>();
        foreach (var room in map)
        {
            if (room.X < 0 || room.Y < 0)
                unplacedRooms.Add(room);
            else if (room.Open)
                openRooms.Add(room);
        }

        // 左上：已开发房间先落位；再把「挨着已开发地方的未开发房间」补成可点的虚线格。
        var cells = new List<InkDevRoomCell>();
        var placed = new HashSet<(int, int)>();
        foreach (var room in openRooms)
        {
            if (!placed.Add((room.X, room.Y)))
                continue;
            var facs = territory.Facilities.FindAll(f => f.RoomId == room.Id);
            cells.Add(new InkDevRoomCell
            {
                Id = room.Id,
                X = room.X,
                Y = room.Y,
                Name = room.Name,
                Open = true,
                Vacant = room.Vacant,
                NonEmpty = facs.Count > 0,
                Removable = vm.Hub.PlayerRoomId != room.Id,
            });
        }
        foreach (var room in map)
        {
            if (room.Open || room.X < 0 || room.Y < 0)
                continue;
            if (!placed.Add((room.X, room.Y)) || !territory.NearOpenRoom(room))
                continue;
            cells.Add(new InkDevRoomCell
            {
                Id = room.Id,
                X = room.X,
                Y = room.Y,
                Name = room.Name,
                Open = false,
                CanDevelop = vm.Hub.CanDevelopEmptyRoom(room.Id),
                CostText = DevelopCostText(vm, room),
            });
        }
        // 再补「空格子」——还没有房间实体、但挨着已开发地方的那几格。
        // 开拓它们会现场生成一间「空房」。这才是「未开拓」的主体：
        // 起始区域 5×5 里只有 5 间房，剩下 20 格全是这种空格子。
        for (var gx = 0; gx < InkLayout.GridCols; gx++)
        {
            for (var gy = 0; gy < InkLayout.GridRows; gy++)
            {
                if (placed.Contains((gx, gy)) || !territory.NearOpenAt(vm.Hub.RegionId, gx, gy))
                    continue;
                placed.Add((gx, gy));
                cells.Add(new InkDevRoomCell
                {
                    Id = -1,
                    X = gx,
                    Y = gy,
                    Name = "空地",
                    Open = false,
                    CanDevelop = vm.Hub.CanDevelopVacantCell(vm.Hub.RegionId, gx, gy),
                    CostText = VacantCostText(vm),
                });
            }
        }
        var cellSel = ClampSel(q.Selected, cells.Count);
        for (var i = 0; i < cells.Count; i++)
            cells[i] = cells[i] with { Selected = i == cellSel };

        var picked = cellSel >= 0 ? cells[cellSel] : null;
        var pickedRoom = picked is { Open: true }
            ? openRooms.Find(r => r.Id == picked.Id)
            : null;

        // 右上：**只列当前选中房间的设施**（没选房间就空着）。
        var facilityRows = new List<InkDevRow>();
        if (pickedRoom != null)
        {
            foreach (var f in territory.Facilities)
            {
                if (f.RoomId != pickedRoom.Id)
                    continue;
                facilityRows.Add(new InkDevRow
                {
                    Kind = InkDevRowKind.Facility,
                    Id = f.Id,
                    Name = f.Name,
                });
            }
        }
        var facSel = facilityRows.FindIndex(r => r.Id == q.SelectedFacility);
        for (var i = 0; i < facilityRows.Count; i++)
            facilityRows[i] = facilityRows[i] with { Selected = i == facSel };

        // 左下：**只列已建但还没安装的房间**。
        var roomRows = new List<InkDevRow>();
        foreach (var room in unplacedRooms)
            roomRows.Add(new InkDevRow
            {
                Kind = InkDevRowKind.RoomUnplaced, Id = room.Id, Name = room.Name,
            });
        var roomSel = ClampSel(q.SelectedRoom, roomRows.Count);
        for (var i = 0; i < roomRows.Count; i++)
            roomRows[i] = roomRows[i] with { Selected = i == roomSel };
        var pickedUnplaced = roomSel >= 0 ? roomRows[roomSel] : null;

        // 中下：操作面板——所有操作按钮都归拢到这一列里。
        // 每行自己带 InkAction 与分派下标，界面侧照抄注册（见 InkHubModel.BuildDevPage）。
        // 排序按「先拆后建」：拆（上下文）→ 建房间 → 建设施 → 安置。
        // 建房间放在建设施前面，是因为没选房间时建设施整组是暗的，
        // 首屏若被暗行占满，就找不到能点的东西了。
        var actions = new List<InkDevActionRow>();
        var payer = vm.Hub.State.Roster.Master;

        if (pickedRoom != null && vm.Hub.PlayerRoomId != pickedRoom.Id)
        {
            actions.Add(new InkDevActionRow
            {
                Name = "拆除本房间", Prefix = "拆",
                Action = InkAction.DevDemolishRoom, Index = pickedRoom.Id,
            });
        }
        if (pickedRoom != null && facSel >= 0)
        {
            actions.Add(new InkDevActionRow
            {
                Name = facilityRows[facSel].Name, Prefix = "拆",
                Action = InkAction.DevRemoveFacility, Index = facilityRows[facSel].Id,
                Selected = true,
            });
        }
        // 建造房间：花材料建出来，进左下「待安装的房间」。
        foreach (var def in DefDatabase<RoomDef>.All)
        {
            if (!def.Buildable)
                continue;
            actions.Add(new InkDevActionRow
            {
                Name = def.Name, Prefix = "房",
                Action = InkAction.DevBuildRoom, Index = def.Id,
                Enabled = territory.CanPayWith(payer, def.MaterialCost),
            });
        }
        // 建造设施：直接建进当前选中的房间（没选房间、或房里已摆满 Room.MaxFacilities 件就点不动）。
        var roomHasSlot = pickedRoom != null && territory.HasFacilitySlot(pickedRoom.Id);
        foreach (var def in DefDatabase<FacilityDef>.All)
        {
            if (!def.Buildable)
                continue;
            actions.Add(new InkDevActionRow
            {
                Name = def.Name, Prefix = "设",
                Action = InkAction.DevBuildFacility, Index = def.Id,
                Enabled = roomHasSlot && territory.CanPayWith(payer, def.MaterialCost),
            });
        }
        // 建成却还没安装的设施（老存档可能留下）：选中房间后可以直接安置。
        foreach (var f in territory.Facilities)
        {
            if (f.RoomId >= 0)
                continue;
            actions.Add(new InkDevActionRow
            {
                Name = f.Name, Prefix = "安",
                Action = InkAction.DevPlaceFacility, Index = f.Id,
                Enabled = roomHasSlot,
            });
        }

        // 右下：详情——纯文字，不画任何内部边框，也不放按钮（按钮全在中下）。
        var detailTitle = "详情";
        var detailNote = "点左上网格里的房间，或右上设施列表里的一件设施。";
        if (pickedRoom != null)
        {
            var count = territory.Facilities.FindAll(f => f.RoomId == pickedRoom.Id).Count;
            detailTitle = "";
            detailNote = $"已开发。\n房内设施 {count} 件。";
            if (vm.Hub.PlayerRoomId == pickedRoom.Id)
                detailNote += "\n你正待在这儿，拆不得。";
        }
        else if (picked is { CanDevelop: true })
        {
            detailTitle = picked.Name;
            detailNote = $"未开发，挨着已开发的地方。\n开拓花费{picked.CostText}\n开拓后是一间空房。";
        }
        else if (pickedUnplaced != null)
        {
            detailTitle = pickedUnplaced.Name;
            detailNote = "已建好，还没安装。\n在上面网格里点一间空房把它装进去。";
        }
        if (facSel >= 0)
        {
            detailTitle = facilityRows[facSel].Name;
            detailNote = $"在{pickedRoom?.Name ?? "屋里"}。\n拆除返还 60% 材料。\n拆除按钮在中下操作面板。";
        }

        // 开拓确认弹窗：点了邻近的未开发格（含空格子）才开。
        // 目标用「格子线性下标」记——空格子没有房间 Id，用坐标最稳。
        var confirmCell = -1;
        var confirmTitle = "";
        var confirmBody = "";
        if (q.ConfirmCell >= 0)
        {
            var cx = q.ConfirmCell % InkLayout.GridCols;
            var cy = q.ConfirmCell / InkLayout.GridCols;
            var target = cells.Find(c => c.X == cx && c.Y == cy);
            if (target is { CanDevelop: true })
            {
                confirmCell = q.ConfirmCell;
                confirmTitle = $"开拓 · {target.Name}";
                confirmBody = $"是否消耗材料和钱，把这里开发成一间空房间？\n\n{target.CostText}";
            }
        }

        return new InkPageModel
        {
            Page = InkPage.Develop,
            Title = "开发",
            Dev = new InkDevModel
            {
                Rooms = cells,
                SelectedCell = cellSel,
                RegionId = vm.Hub.RegionId,
                UnlockedRegionMask = territory.UnlockedRegionMask,
                RoomId = pickedRoom?.Id ?? -1,
                FacilityTitle = pickedRoom != null ? $"{pickedRoom.Name} · 设施" : "设施",
                FacilityRows = facilityRows,
                SelectedFacility = facSel >= 0 ? facilityRows[facSel].Id : -1,
                RoomRows = roomRows,
                SelectedRoomRow = roomSel,
                ActionRows = actions,
                ActionFirst = q.ActionFirst,
                DetailTitle = detailTitle,
                DetailNote = detailNote,
                ConfirmCell = confirmCell,
                ConfirmTitle = confirmTitle,
                ConfirmBody = confirmBody,
                PlacingRoom = q.PlacingRoom,
            },
            EmptyHint = "当前区域没有房间。",
        };
    }

    /// <summary>开拓一间未开发房间的花费文本：材料 ＋ 钱（都没有时给个「无花费」）。</summary>
    private static string DevelopCostText(InkViewModel vm, Room room)
    {
        var parts = new List<string>();
        if (room.MaterialCost.Count > 0)
            parts.Add($"材料 {MaterialsText(vm, room.MaterialCost)}");
        if (room.OpenCost > 0)
            parts.Add($"钱 {room.OpenCost}");
        return parts.Count > 0 ? string.Join("　", parts) : "无花费";
    }

    /// <summary>
    /// 开拓一格「空地」（空格子）的花费文本。定价随已开发房间数递增——越开越贵，
    /// 数值取自 <see cref="HubSession.VacantCostMoney"/> / <see cref="HubSession.VacantCostMaterial"/>。
    /// </summary>
    private static string VacantCostText(InkViewModel vm)
    {
        var parts = new List<string>
        {
            $"材料 {MaterialsText(vm, vm.Hub.VacantCostMaterial())}",
            $"钱 {vm.Hub.VacantCostMoney}",
        };
        return string.Join("　", parts);
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
    private static string ItemName(InkViewModel vm, string itemId)
    {
        var info = Rimisekai.Defs.Items.Info(vm.Hub.State.Territory, itemId);
        if (info != null && info.Value.Label.Length > 0)
            return info.Value.Label;
        var facility = Rimisekai.Defs.DefDatabase<Rimisekai.Defs.FacilityDef>.All
            .FirstOrDefault(f => f.DefName.Equals(itemId, StringComparison.OrdinalIgnoreCase));
        return facility != null && facility.Label.Length > 0 ? facility.Label : itemId;
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

    /// <summary>战斗日志页：展示本场战斗的完整战况流水清单。</summary>
    private static InkPageModel CombatLog(InkViewModel vm, in InkPageQuery q)
    {
        var battle = vm.Combat?.Battle;
        var lines = battle != null ? InkCombatRenderer.FormatBattleEvents(battle) : new List<string>();

        var rows = new List<InkPageRow>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            rows.Add(new InkPageRow
            {
                Name = lines[i],
            });
        }

        var sel = ClampSel(q.Selected, rows.Count);
        var detail = sel >= 0 && sel < lines.Count ? lines[sel] : "";

        return new InkPageModel
        {
            Page = InkPage.CombatLog,
            Title = "战斗日志",
            Rows = rows,
            SelectedRow = sel,
            ListFirst = ClampFirst(q.PageFirst, rows.Count),
            DetailTitle = "战况详情",
            DetailNote = detail,
            HasControls = false,
            EmptyHint = "暂无战斗记录。",
        };
    }
}
