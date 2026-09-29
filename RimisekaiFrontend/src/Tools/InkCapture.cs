using Godot;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Ink;

namespace Rimisekai.Tools;

/// <summary>
/// 开发期核对工具，不参与正式启动。
/// --capture=&lt;路径&gt;：把主界面渲染几帧后存成 PNG。
/// --wait=&lt;帧数&gt;：--click 之后再等多少帧才截图（默认 3），截悬停动画中间态用。
/// --main：跑真正的生产路径（Main.tscn → InkRoot → 路由 → 据点界面）。
/// --overlay：带着对话层截图。
/// --page=&lt;stock|trade|craft|develop&gt;：打开某个子页面后截图。
/// --click=&lt;x,y&gt;：截图前按坐标合成一次点击（核对标题“开始游戏”这类转场）。
/// --rename：打开改名弹窗后截图。
/// --smoke：按坐标合成鼠标点击，检查交互是否落到 HubSession 上。
/// </summary>
public partial class InkCapture : Node
{
    private string _outPath = "";
    private int _frames;
    private HubSession? _hub;
    private InkHubScreen? _screen;
    private bool _smoke;
    private bool _main;
    private string _page = "";
    private int _select = -1;
    private string _search = "";
    private int _sort;
    private int _filter;
    private bool _desc;
    private bool _devtest;
    private Vector2? _click;
    private bool _rename;
    private bool _overlay;
    private bool _demo;
    private bool _world;
    private bool _poi;
    private bool _storage;
    private string _journey = "";
    private string _system = "";
    private bool _combat;
    private int _wait = 3;

    public override void _Ready()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--capture="))
                _outPath = a["--capture=".Length..];
            else if (a == "--smoke")
                _smoke = true;
            else if (a == "--overlay")
                _overlay = true;
            else if (a == "--rename")
                _rename = true;
            else if (a == "--main")
                _main = true;
            else if (a == "--demo")
                _demo = true;
            else if (a == "--world")
                _world = true;
            else if (a == "--poi")
                _poi = true;
            else if (a == "--storage")
                _storage = true;
            else if (a.StartsWith("--journey="))
                _journey = a["--journey=".Length..];
            else if (a.StartsWith("--system="))
                _system = a["--system=".Length..];
            else if (a == "--combat")
                _combat = true;
            else if (a.StartsWith("--wait="))
            {
                if (int.TryParse(a["--wait=".Length..], out var w) && w >= 3)
                    _wait = w;
                else
                    GD.PushError($"Rimisekai: --wait 需要 >=3 的整数，收到 {a}");
            }
            else if (a.StartsWith("--transition="))
            {
                // 预览转场变体：filmburn / crt / page / ink。
                InkTransition.Current = a["--transition=".Length..].ToLowerInvariant() switch
                {
                    "filmburn" => InkTransition.Variant.FilmBurn,
                    "crt" => InkTransition.Variant.Crt,
                    "page" => InkTransition.Variant.PageTurn,
                    "ink" => InkTransition.Variant.InkWash,
                    _ => InkTransition.Current,
                };
            }
            else if (a.StartsWith("--page="))
                _page = a["--page=".Length..];
            else if (a.StartsWith("--select="))
                _select = int.Parse(a["--select=".Length..]);
            else if (a.StartsWith("--search="))
                _search = a["--search=".Length..];
            else if (a.StartsWith("--sort="))
                _sort = int.Parse(a["--sort=".Length..]);
            else if (a.StartsWith("--filter="))
                _filter = int.Parse(a["--filter=".Length..]);
            else if (a == "--desc")
                _desc = true;
            else if (a == "--devtest")
                _devtest = true;
            else if (a.StartsWith("--click="))
            {
                var parts = a["--click=".Length..].Split(',');
                if (parts.Length == 2
                    && float.TryParse(parts[0], out var x)
                    && float.TryParse(parts[1], out var y))
                    _click = new Vector2(x, y);
                else
                    GD.PushError($"Rimisekai: --click 需要 x,y 两个数，收到 {a}");
            }
        }
        if (_outPath.Length == 0 && !_smoke)
            return;

        // --main：跑真正的生产路径，确认路由把界面挂上了。
        if (_main)
        {
            AddChild(new InkRoot { Name = "MainScene" });
            return;
        }

        // 内容包显式加载一份：立绘等界面配置要从这里取，与生产路径一致。
        var pack = ContentPack.Load(
            InkWorldBootstrap.ContentPath, InkWorldBootstrap.SeedPath, InkWorldBootstrap.BuildingsPath);
        _hub = InkWorldBootstrap.OpenHub(pack, out _);
        if (_world)
            _hub.SwitchToWorld();
        else if (_poi && _hub.State.World.Pois.Count > 0)
            _hub.EnterWorldPoi(_hub.State.World.Pois[0].Id);

        if (_overlay)
            _hub.Social(SocialAction.Talk);
        if (_demo)
            SeedDemoContent(_hub);

        if (_storage)
        {
            var storable = StorableHere(_hub);
            if (storable == null)
            {
                // 默认内容里没有可存储设施时补一件临时货架，只活在本次进程里。
                storable = new Rimisekai.Housing.Facility
                {
                    Id = 998,
                    Name = "货架",
                    RoomId = _hub.PlayerRoomId,
                    Capacity = 1,
                    Built = true,
                    CanStore = true,
                };
                _hub.State.Territory.AddFacility(storable);
            }
            _hub.Use(storable.Id);
            _hub.ActAtFixture(ActionKind.Store);
        }

        _screen = new InkHubScreen();
        _screen.Size = new Vector2(InkLayout.CanvasWidth, InkLayout.CanvasHeight);
        _screen.Bind(new InkViewModel(_hub, pack));
        AddChild(_screen);

        if (_journey.Length > 0)
        {
            var quest = new InkQuestScreen { Name = "QuestScreen" };
            quest.Size = new Vector2(InkLayout.CanvasWidth, InkLayout.CanvasHeight);
            quest.Bind(_hub);
            AddChild(quest);
        }
        else if (_combat)
        {
            // 战斗核对走生产同款场景加载，顺带验证 tscn 可实例化。
            var scene = ResourceLoader.Load<PackedScene>(
                "res://RimisekaiFrontend/src/Ink/InkCombatScreen.tscn");
            var combat = scene.Instantiate<InkCombatScreen>();
            combat.Name = "CombatScreen";
            combat.Size = new Vector2(InkLayout.CanvasWidth, InkLayout.CanvasHeight);
            combat.Bind(_hub);
            AddChild(combat);
        }
        else if (_system.Length > 0)
        {
            // 存档页指向仓库外的临时目录，核对时不碰真实存档。
            InkSaveStore.OverrideDirectory = "D:/123/_ui_preview_saves";
            var system = new InkSystemScreen(_system, _hub.State, _hub, pack);
            system.Size = new Vector2(InkLayout.CanvasWidth, InkLayout.CanvasHeight);
            AddChild(system);
        }
    }

    private static Rimisekai.Housing.Facility? StorableHere(HubSession hub)
    {
        foreach (var f in hub.State.Territory.Facilities)
        {
            if (f.RoomId == hub.PlayerRoomId && f.Built && f.CanStore)
                return f;
        }
        return null;
    }

    public override void _Process(double delta)
    {
        if (++_frames < 3)
            return;

        if (_smoke)
        {
            // 每帧只发一次点击：同一帧内推送多个事件会被视口合并。
            RunSmokeStep();
            return;
        }

        if (_devtest)
        {
            // 开发页核对必须先进入请求的状态，否则点的是普通界面。
            if (_page.Length > 0 && _screen != null)
            {
                if (_page == "develop")
                {
                    _screen.DebugDevMode();
                    _devStep = 1;
                }
                else
                {
                    _screen.DebugOpenPage(_page, _select, _search);
                    _screen.DebugQuery(_filter, _sort, _desc);
                }
                _page = "";
                return;
            }
            RunDevTestStep();
            return;
        }

        // --click：第 3 帧合成点击，之后按 --wait 再等若干帧才截图
        //（悬停动画逐条长线约一秒，靠拉大 --wait 截中间态；默认 3 与原行为一致）。
        if (_click.HasValue)
        {
            if (_frames == 3)
                Click(_click.Value);
            if (_frames < 3 + _wait)
                return;
            _click = null;
        }

        // 先把请求的界面状态摆好（子页面 / 改名弹窗），
        // 再等一帧让 QueueRedraw 真正落笔，否则抓到的是改动前的画面。
        if (_screen != null && (_page.Length > 0 || _rename))
        {
            if (_page.Length > 0)
            {
                if (_page == "develop")
                {
                    _screen.DebugDevMode();
                    if (_devtest)
                        _devStep = 1;
                }
                else
                {
                    _screen.DebugOpenPage(_page, _select, _search);
                    _screen.DebugQuery(_filter, _sort, _desc);
                }
            }
            if (_rename)
                _screen.DebugOpenRename();
            _page = "";
            _rename = false;
            return;
        }

        if (_outPath.Length == 0)
            return;

        var path = _outPath;
        _outPath = "";
        var img = GetViewport().GetTexture().GetImage();
        var err = img.SavePng(path);
        GD.Print(err == Error.Ok ? $"capture ok: {path}" : $"capture failed: {err}");
        GetTree().Quit();
    }

    private int _step;
    private int _devStep;
    private int _logBefore;
    private int _selectedBefore;
    private int _closeTries;

    private void RunSmokeStep()
    {
        var hub = _hub!;

        // 对话层要多行才能读完：每帧点一次，直到关掉再继续下一步。
        if (_step == 2 || _step == 9)
        {
            if (hub.MapCovered && _closeTries++ < 12)
            {
                Click(new Vector2(600, 300));
                return;
            }
            GD.Print($"关闭对话层: covered={hub.MapCovered}（点了 {_closeTries} 次）");
            _step++;
            _closeTries = 0;
            return;
        }

        switch (_step++)
        {
            case 0:
                GD.Print($"start: room={hub.PlayerRoomId} money={hub.State.Money} " +
                         $"log={hub.Log.Count} selected={hub.SelectedCharacterId}");
                break;

            // 开局已选中角色 → 右下角应显示“交流”，点第一项“交谈”。
            case 1:
                GD.Print($"选中态: 显示交流={hub.HasSelection}");
                Click(TalkButton);
                GD.Print($"点交谈: log={hub.Log.Count} last={Last(hub)} covered={hub.MapCovered}");
                break;

            // 点卡片取消选中 → 右下角应切回“行动”。坐标取第 2 张卡中心。
            case 3:
                _selectedBefore = hub.SelectedCharacterId;
                Click(Card1Center);
                GD.Print($"点已选卡片取消选中: {_selectedBefore} -> {hub.SelectedCharacterId} " +
                         $"显示交流={hub.HasSelection}");
                break;

            // 未选中时点右下角第一项应是第一项房间级行动，推进时间。
            case 4:
                var before = hub.State.Clock.Minutes;
                GD.Print($"点前: 显示交流={hub.HasSelection} 命中={_screen!.DebugHitAt(FirstAction)}");
                Click(FirstAction);
                GD.Print($"未选中点行动: minutes {before} -> {hub.State.Clock.Minutes} " +
                         $"last={Last(hub)} notice={_screen!.DebugNotice}");
                break;

            // 重新选中角色。
            case 5:
                Click(Card1Center);
                GD.Print($"重新选中: selected={hub.SelectedCharacterId} 显示交流={hub.HasSelection}");
                break;

            // 再开一次对话，点右上角“状态”入口——角色三页的往返。
            case 6:
                Click(TalkButton);
                GD.Print($"再点交谈: covered={hub.MapCovered}");
                break;

            case 7:
                GD.Print($"点状态前: 命中={_screen!.DebugHitAt(ChatEntryStatus)}");
                Click(ChatEntryStatus);
                GD.Print($"点状态入口: 页面={_screen!.DebugPage} " +
                         $"对话还在={_screen!.DebugOverlayOpen}");
                break;

            case 8:
                GD.Print($"关页面前: 页面={_screen!.DebugPage}");
                Click(PageClose);
                GD.Print($"关页面后: 页面={_screen!.DebugPage} 对话回来了={_screen!.DebugOverlayOpen}");
                break;

            // 点左上角标题打开改名弹窗。
            case 10:
                Click(new Vector2(120, 48));
                GD.Print($"点左上角标题: 弹窗={_screen!.DebugRenaming}");
                break;

            // 关闭改名弹窗
            case 11:
                Click(InkLayout.RenameButton(1).GetCenter());
                GD.Print($"关改名弹窗: 弹窗={_screen!.DebugRenaming}");
                hub.ClearSelection();
                break;

            // 点第 3 个设施（篝火，有存储功能）；默认内容 4 件 → 两列布局，第 3 件在第二列首行。
            case 12:
                Click(InkLayout.FixtureCell(2, 4).GetCenter());
                GD.Print($"点篝火设施: using={hub.UsingFixtureId}");
                break;

            // 行动面板点击“打开篝火”
            case 13:
                Click(InkLayout.PlaceButton(0, 1).GetCenter());
                GD.Print($"点打开篝火: storageOpen={hub.OpenStorageFacility != null} title={hub.OpenStorageFacility?.Name}");
                break;

            // 存储页点击“取出”干粮（第 0 行，按钮 1）
            case 14:
                var foodBefore = hub.State.Roster.Master?.Bag.Get("干粮") ?? 0;
                Click(InkLayout.StorageRowButton(InkLayout.StorageRow(0), 1).GetCenter());
                var foodAfter = hub.State.Roster.Master?.Bag.Get("干粮") ?? 0;
                GD.Print($"点取出干粮: bag={foodBefore} -> {foodAfter}");
                break;

            // 点击右上角关闭按钮，回到地图
            case 15:
                Click(InkLayout.FixtureClose.GetCenter());
                GD.Print($"点关闭存储页: storageOpen={hub.OpenStorageFacility != null}");
                break;

            case 16:
                GD.Print("smoke done");
                GetTree().Quit();
                break;
        }
    }

    /// <summary>聊天层右上角第一个入口（状态）的中心。</summary>
    private static readonly Vector2 ChatEntryStatus =
        InkLayout.ChatEntry(0, InkHubModel.ChatEntries.Length).GetCenter();

    /// <summary>全屏子页面右上角关闭按钮的中心。</summary>
    private static readonly Vector2 PageClose = InkLayout.FullPageClose.GetCenter();

    /// <summary>交流首项（交谈）与房间级行动首项的中心，随版式自动更新。</summary>
    private static readonly Vector2 TalkButton =
        InkLayout.SocialButton(0, InkViewModel.SocialActions.Length).GetCenter();

    private static readonly Vector2 FirstAction =
        InkLayout.PlaceButton(0, InkViewModel.PlaceActions.Length).GetCenter();

    /// <summary>角色栏首张卡的中心（点选/取消选中探针，房间内至少一名同伴）。</summary>
    private static readonly Vector2 Card1Center = InkLayout.Card(0).GetCenter();

    // 开发页探针坐标，中心点取自 InkLayout：左下房间行首、右上设施行首、
    // 右下详情动作按钮、右下目录行（设施列表最末一个可见槽位）。
    private static readonly Vector2 RoomRow0 = new(408f, 704f);
    private static readonly Vector2 FacilityRow0 = new(1522f, 230f);
    private static readonly Vector2 DetailButton = new(1332f, 928f);
    private static readonly Vector2 FacilityCatalog = new(1522f, 470f);

    /// <summary>
    /// 开发页交互全流程自动核对：选房间 → 选设施 → 建造 → 放置，
    /// 每步点击后打印命中与反馈，验证点击管线端到端。
    /// </summary>
    private void RunDevTestStep()
    {
        // --main 路径下真实 hub 屏在 InkRoot/ScreenRouter/HubScreen，不在 _screen。
        var screen = FindHubScreen(GetTree().Root);
        if (screen == null)
        {
            GD.Print("[devtest] 找不到 HubScreen, 场景树:");
            DumpNode(GetTree().Root, "  ");
            GetTree().Quit();
            return;
        }
        var notice = screen.DebugNotice;
        switch (_devStep)
        {
            case 0:
                GD.Print($"[devtest] 进入开发模式: 命中房间行0={screen.DebugHitAt(RoomRow0)}");
                break;

            case 1: // 点左下房间行0：选中它作为放置目标。
                Click(RoomRow0);
                GD.Print($"[devtest] 点房间行0: 命中={screen.DebugHitAt(RoomRow0)} 反馈={screen.DebugNotice}");
                break;

            case 2: // 点右上行首：设施列表第一行。
                Click(FacilityRow0);
                GD.Print($"[devtest] 点设施行0: 命中={screen.DebugHitAt(FacilityRow0)} 反馈={screen.DebugNotice}");
                break;

            case 3: // 点右下详情动作按钮（拆除/放置）。
                Click(DetailButton);
                GD.Print($"[devtest] 点详情按钮: 命中={screen.DebugHitAt(DetailButton)} 反馈={screen.DebugNotice}");
                break;

            case 4: // 点右上下探一行：应是可建造设施目录。
                GD.Print($"[devtest] 目录行探测: 命中={screen.DebugHitAt(FacilityCatalog)}");
                Click(FacilityCatalog);
                GD.Print($"[devtest] 点目录行: 反馈={screen.DebugNotice}");
                break;

            case 5:
                GD.Print("[devtest] done");
                GetTree().Quit();
                break;
        }
        _devStep++;
    }

    private void DumpNode(Node node, string indent)
    {
        GD.Print($"{indent}{node.Name} ({node.GetType().Name})");
        foreach (var child in node.GetChildren())
            DumpNode(child, indent + "  ");
    }

    private InkHubScreen? FindHubScreen(Node node)
    {
        if (node is InkHubScreen found)
            return found;
        foreach (var child in node.GetChildren())
        {
            if (child is Node n && FindHubScreen(n) is InkHubScreen hit)
                return hit;
        }
        return null;
    }

    private static string Last(HubSession hub) =>
        hub.Log.Count > 0 ? hub.Log[^1].Text : "(empty)";

    /// <summary>
    /// --demo：往会话里塞一份**临时**数据，好让库存/交易/制作/开发四个页面
    /// 有内容可核对。只存在于本次进程内，绝不写回 content/world.json。
    /// 正式内容应由内容包提供，这里纯粹是出图用的占位。
    /// </summary>
    private static void SeedDemoContent(HubSession hub)
    {
        var catalog = hub.State.Catalog;
        foreach (var id in new[] { "木材", "石料", "铁矿", "药草", "布" })
            catalog.Items[id] = new Rimisekai.Catalog.ItemDef { Id = id, Name = id };

        var territory = hub.State.Territory;
        // 演示物资进玩家背包（物品只在背包或设施里）。
        var bag = hub.State.Roster.Master?.Bag;
        bag?.Add("木材", 12);
        bag?.Add("石料", 6);
        bag?.Add("药草", 3);

        territory.AddOffer(new Rimisekai.Housing.MarketOffer { ItemId = "木材", BuyPrice = 8, SellPrice = 4 });
        territory.AddOffer(new Rimisekai.Housing.MarketOffer { ItemId = "石料", BuyPrice = 12, SellPrice = 6 });
        territory.AddOffer(new Rimisekai.Housing.MarketOffer { ItemId = "药草", BuyPrice = 20, SellPrice = 11 });

        territory.AddRecipe(new Rimisekai.Housing.Recipe
        {
            ItemId = "布",
            Station = Rimisekai.Housing.ActionKind.Sew,
            OutputCount = 1,
            Costs = { new Rimisekai.Housing.RecipeCost("药草", 2) },
        });

        // 一个未建成的设施，让“开发”页有东西可点。
        territory.AddFacility(new Rimisekai.Housing.Facility
        {
            Id = 90,
            Name = "工坊",
            RoomId = hub.PlayerRoomId,
            Capacity = 1,
            Built = false,
            BuildCost = 300,
        });
    }

    private void Click(Vector2 at)
    {
        var motion = new InputEventMouseMotion { Position = at, GlobalPosition = at };
        GetViewport().PushInput(motion);

        var down = new InputEventMouseButton
        {
            Position = at,
            GlobalPosition = at,
            ButtonIndex = MouseButton.Left,
            Pressed = true,
        };
        GetViewport().PushInput(down);
    }
}
