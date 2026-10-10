using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Rimisekai.Flow;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 模拟游玩：只经 UI 操作——每一步都是往视口推一次真实的按下/松开（或拖动），落点取当前画面登记的可点块内部。
/// 只读取状态做判断（画面上有哪些钮、阶段、时钟），绝不直接调游戏逻辑。
/// 先跑一串目标（开局、委托战斗、对话、出行、存读档），再随机乱点覆盖其余入口；
/// 记录断点：异常、无钮死局、出不去的画面、目标走不通、战斗不结束、点了没反应的钮。
/// 用法：<c>--pplay=输出目录 [--pseed=42] [--psteps=1500]</c>
/// </summary>
public partial class PortraitPlaytest : Node
{
    private string _out = "";
    private int _seed = 42;
    private int _monkeySteps = 1500;

    /// <summary>只跑这些目标（<c>--pgoals=开局,探索委托</c>）；空＝全跑。</summary>
    private HashSet<string> _only = new();
    private SubViewport _sub = null!;
    private PortraitRoot _root = null!;
    private readonly List<string> _log = new();
    private readonly List<string> _breaks = new();
    private readonly HashSet<string> _breakKeys = new();
    private readonly Dictionary<string, int> _noop = new();
    private readonly HashSet<string> _screens = new();
    private readonly Dictionary<string, int> _covered = new();
    private readonly Dictionary<string, bool> _goals = new();
    private Random _rng = null!;
    private int _taps;
    private int _shots;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--pplay=")) _out = arg["--pplay=".Length..];
            if (arg.StartsWith("--pseed=")) _seed = int.Parse(arg["--pseed=".Length..]);
            if (arg.StartsWith("--psteps=")) _monkeySteps = int.Parse(arg["--psteps=".Length..]);
            if (arg.StartsWith("--pgoals=")) _only = arg["--pgoals=".Length..].Split(',').ToHashSet();
        }
        if (_out.Length == 0)
            return;
        Directory.CreateDirectory(_out);
        _rng = new Random(_seed);
        // 动效直接落终态、读条加速：只缩短等待，不改操作路径。
        PortraitMotion.Instant = true;
        Engine.TimeScale = 3.0;
        InkWorldBootstrap.WorldSeedOverride = _seed;
        InkSaveStore.OverrideDirectory = Path.Combine(Path.GetTempPath(), $"rimisekai-play-{Guid.NewGuid():N}").Replace('\\', '/');
        _root = new PortraitRoot { Name = "PortraitRoot" };
        _sub = new SubViewport
        {
            Size = new Vector2I((int)PortraitLayout.CanvasWidth, (int)PortraitLayout.CanvasHeight),
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(_sub);
        _sub.AddChild(_root);
        _ = Run();
    }

    // ================= 观察（只读） =================

    private FlowPhase Phase => _root.DebugPhase;
    private bool ModalOn => _root.ModalLayer.IsActive;

    private IReadOnlyList<PortraitWidget> Widgets()
    {
        if (ModalOn) return _root.ModalLayer.DebugWidgets;
        if (_root.TitleView.Visible) return _root.TitleView.DebugWidgets;
        if (_root.CombatView.Visible) return _root.CombatView.DebugWidgets;
        return _root.HubScreen.DebugWidgets;
    }

    private string Surface()
    {
        if (ModalOn) return "modal:" + (_root.ModalLayer.Current?.Title ?? "?");
        if (_root.TitleView.Visible) return "title:" + _root.TitleView.DebugSystemPage;
        if (_root.CombatView.Visible) return "combat";
        var acts = string.Join(",", _root.HubScreen.DebugWidgets.Select(w => w.Action).Distinct().OrderBy(a => a).Take(6));
        return $"hub:t{_root.HubScreen.DebugTab}:{acts}";
    }

    private string Signature()
    {
        var sb = new StringBuilder(Surface());
        foreach (var w in Widgets())
            sb.Append('|').Append((int)w.Action).Append(':').Append(w.Index).Append(w.Enabled ? '+' : '-').Append(w.Label);
        var hub = _root.DebugVm?.Hub;
        if (hub != null)
            sb.Append("#t").Append(hub.State.Clock.TotalMinutes)
              .Append("#n").Append(_root.HubScreen.DebugNotice);
        if (_root.DebugCombat?.Battle is { } b)
            sb.Append("#hp").Append(string.Join(",", b.Members.Select(m => m.Hp)));
        return sb.ToString();
    }

    private bool Busy => (_root.HubScreen.Visible && _root.HubScreen.DebugAnimating);

    private bool CombatRunning => _root.CombatView.Visible && !ModalOn
        && _root.CombatView.DebugActorId < 0 && _root.DebugCombat?.Battle is { } b && b.Outcome == Rimisekai.Combat.CombatOutcome.Ongoing;

    // ================= 操作（只经输入事件） =================

    private async Task Frames(int n)
    {
        for (var i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>等动效、行走、战斗读条走完（最多约 40 秒），让画面停在可操作的终态。</summary>
    private async Task Settle(int maxFrames = 2400)
    {
        await Frames(3);
        var f = 0;
        while (f < maxFrames && (Busy || CombatRunning))
        {
            await Frames(4);
            f += 4;
        }
        if (f >= maxFrames)
            Break("hang", Surface(), $"画面 {f} 帧仍在动效/行走/读条，没有回到可操作状态");
        await Frames(2);
    }

    private static Vector2? Inner(PortraitWidget w)
    {
        var c = w.Rect.GetCenter();
        if (w.Contains(c)) return c;
        for (var y = 1; y < 12; y++)
            for (var x = 1; x < 12; x++)
            {
                var p = w.Rect.Position + w.Rect.Size * new Vector2(x / 12f, y / 12f);
                if (w.Contains(p)) return p;
            }
        return null;
    }

    private void Push(InputEvent e) => _sub.PushInput(e, true);

    private async Task TapAt(Vector2 p)
    {
        Push(new InputEventMouseMotion { Position = p, GlobalPosition = p });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = p, GlobalPosition = p });
        // 按下与松开同帧送出：软渲染下一帧可达数百毫秒（再乘 TimeScale），隔帧松开会被当成长按。
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = p, GlobalPosition = p });
        await Frames(2);
        _taps++;
        await Settle();
    }

    private async Task Drag(Vector2 from, Vector2 to)
    {
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from, GlobalPosition = from });
        for (var i = 1; i <= 8; i++)
        {
            var p = from.Lerp(to, i / 8f);
            Push(new InputEventMouseMotion { Position = p, GlobalPosition = p, ButtonMask = MouseButtonMask.Left, Relative = (to - from) / 8f });
            await Frames(1);
        }
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = to, GlobalPosition = to });
        await Settle();
    }

    /// <summary>点一个可点块；点完画面签名没变就记一笔「点了没反应」。</summary>
    private async Task<bool> Tap(PortraitWidget w, string why)
    {
        var p = Inner(w);
        if (p == null)
        {
            Break("hitbox", Surface(), $"{w.Action}#{w.Index}「{w.Label}」登记了命中块但内部取不到点");
            return false;
        }
        var surface = Surface();
        var before = Signature();
        _covered[$"{surface}>{w.Action}"] = _covered.GetValueOrDefault($"{surface}>{w.Action}") + 1;
        try
        {
            await TapAt(p.Value);
        }
        catch (Exception ex)
        {
            Break("exception", surface, $"点 {w.Action}「{w.Label}」抛异常：{ex.GetType().Name}: {ex.Message}");
        }
        var after = Signature();
        _screens.Add(Surface());
        _log.Add($"{_taps,5} [{surface}] {why}: {w.Action}#{w.Index}「{w.Label}」 -> [{Surface()}]");
        if (before == after)
        {
            var key = $"{surface.Split(':')[0]}>{w.Action}「{w.Label}」";
            _noop[key] = _noop.GetValueOrDefault(key) + 1;
        }
        CheckDeadEnd();
        return before != after;
    }

    private PortraitWidget? Find(PortraitAction a, Func<PortraitWidget, bool>? pred = null) =>
        Widgets().Where(w => w.Action == a && w.Enabled && (pred == null || pred(w))).Cast<PortraitWidget?>().FirstOrDefault();

    private async Task<bool> TapAction(PortraitAction a, string why, Func<PortraitWidget, bool>? pred = null)
    {
        var w = Find(a, pred);
        if (w == null) return false;
        await Tap(w.Value, why);
        return true;
    }

    private async Task<bool> TapLabel(string label, string why)
    {
        var w = Widgets().Where(x => x.Enabled && x.Label == label).Cast<PortraitWidget?>().FirstOrDefault();
        if (w == null) return false;
        await Tap(w.Value, why);
        return true;
    }

    // ================= 断点记录 =================

    private void Break(string kind, string where, string what)
    {
        var key = $"{kind}|{where.Split('|')[0]}|{what}";
        if (!_breakKeys.Add(key)) return;
        var shot = Shoot($"break_{_breaks.Count:00}_{kind}");
        _breaks.Add($"[{kind}] @{where}  {what}  (tap {_taps}, {shot})");
        GD.Print($"PLAYTEST BREAK [{kind}] @{where} {what}");
    }

    private void CheckDeadEnd()
    {
        if (Busy || CombatRunning) return;
        if (!Widgets().Any(w => w.Enabled) && !ModalOn)
            Break("deadend", Surface(), "画面静止但没有任何可点的钮");
    }

    private string Shoot(string tag)
    {
        var name = $"{_shots++:000}_{tag}.png";
        try { _sub.GetTexture().GetImage().SavePng(Path.Combine(_out, name)); } catch { }
        return name;
    }

    // ================= 脱身：回到据点根页 =================

    private static readonly PortraitAction[] EscapeOrder =
    {
        PortraitAction.SheetClose, PortraitAction.Back, PortraitAction.InteractionBack,
    };

    private async Task<bool> Escape()
    {
        var before = Signature();
        if (ModalOn)
        {
            var ws = Widgets().Where(w => w.Enabled).ToList();
            if (ws.Count == 0)
            {
                // 纯叙事弹窗：点任意处推进。
                await TapAt(_root.ModalLayer.DebugPanel.GetCenter());
                return Signature() != before;
            }
            var hasInput = ws.Any(w => w.Action == PortraitAction.ModalInput);
            var order = hasInput ? new[] { "取消", "关闭", "确定" } : new[] { "返回领地", "关闭", "确定", "继续", "取消", "绕开" };
            foreach (var l in order)
                if (await TapLabel(l, "escape") && Signature() != before) return true;
            foreach (var w in ws.Where(w => w.Action != PortraitAction.ModalInput))
            {
                await Tap(w, "escape");
                if (Signature() != before) return true;
            }
            return false;
        }
        foreach (var a in EscapeOrder)
            if (await TapAction(a, "escape") && Signature() != before) return true;
        if (await TapAction(PortraitAction.ConversationChoice, "escape") && Signature() != before) return true;
        if (await TapAction(PortraitAction.ConversationAdvance, "escape") && Signature() != before) return true;
        return false;
    }

    private bool AtHubRoot => _root.HubScreen.Visible && !ModalOn
        && Widgets().Count(w => w.Action == PortraitAction.Tab) >= 5
        && !Widgets().Any(w => w.Action is PortraitAction.SheetClose or PortraitAction.Back
            or PortraitAction.ConversationAdvance or PortraitAction.ConversationChoice or PortraitAction.InteractionBack);

    private async Task<bool> GoHubRoot(string goal)
    {
        for (var i = 0; i < 25 && !AtHubRoot; i++)
        {
            if (_root.CombatView.Visible && !ModalOn) { await PlayCombatTurn(); continue; }
            if (!await Escape())
            {
                Break("trap", Surface(), $"[{goal}] 找不到返回/关闭的路回到据点");
                return false;
            }
        }
        if (!AtHubRoot) Break("trap", Surface(), $"[{goal}] 25 次脱身后仍回不到据点根页");
        return AtHubRoot;
    }

    // ================= 战斗：攻击 → 点敌 =================

    private async Task PlayCombatTurn()
    {
        await Settle();
        if (ModalOn || !_root.CombatView.Visible) return;
        if (_root.CombatView.DebugActorId < 0) { await Frames(10); return; }
        if (!await TapAction(PortraitAction.CombatMenu, "combat attack", w => w.Index == 0))
        {
            Break("combat", Surface(), "轮到我方行动但「攻击」不可点");
            await TapAction(PortraitAction.CombatMenu, "combat any");
            return;
        }
        if (!await TapAction(PortraitAction.CombatTarget, "combat target"))
            await TapAction(PortraitAction.CombatColumn, "combat column");
    }

    private async Task<bool> FinishCombat(string goal)
    {
        for (var turn = 0; turn < 120; turn++)
        {
            if (!_root.CombatView.Visible) return true;
            if (ModalOn)
            {
                if (!await Escape()) break;
                continue;
            }
            await PlayCombatTurn();
        }
        if (_root.CombatView.Visible)
            Break("combat", Surface(), $"[{goal}] 120 回合内战斗没有结束或结算后回不到据点");
        return !_root.CombatView.Visible;
    }

    // ================= 目标 =================

    private void Goal(string name, bool ok)
    {
        _goals[name] = ok;
        _log.Add($"===== 目标 {name}: {(ok ? "通过" : "失败")} =====");
        if (!ok) Break("goal", Surface(), $"目标「{name}」走不通");
    }

    private async Task GoalStart()
    {
        await Settle();
        Shoot("title");
        var ok = await TapLabel("新的开始", "start");
        await Settle(600);
        // 开局可能有问卷/叙事弹窗，逐个点过。
        for (var i = 0; i < 40 && ModalOn; i++)
        {
            var input = Find(PortraitAction.ModalInput);
            if (input != null) { Break("input", Surface(), "开局弹窗需要文字输入，模拟器跳过"); }
            if (!await Escape()) break;
        }
        Shoot("start");
        Goal("开局进据点", ok && _root.HubScreen.Visible);
    }

    private async Task GoalTabs()
    {
        var ok = true;
        for (var t = 0; t < 5; t++)
        {
            if (!await TapAction(PortraitAction.Tab, $"tab {t}", w => w.Index == t)) ok = false;
            Shoot($"tab{t}");
        }
        await TapAction(PortraitAction.Tab, "tab home", w => w.Index == 0);
        Goal("五个页签可切换", ok);
    }

    private async Task GoalQuest()
    {
        await GoHubRoot("委托");
        await TapAction(PortraitAction.Tab, "quest tab", w => w.Index == 2);
        // 优先挑普通战斗委托，否则第一个。
        var normal = _root.HubScreen.DebugQuestIndex(d => d.Kind == Rimisekai.Quest.QuestKind.Battle && d.Battle == Rimisekai.Quest.QuestBattle.Normal);
        var took = await TapAction(PortraitAction.QuestTake, "take quest", w => w.Index == normal)
            || await TapAction(PortraitAction.QuestTake, "take quest");
        if (!took) { Goal("委托出发并打完", false); return; }
        Shoot("quest_sheet");
        // 编成：把能选的人都点上，再出发。
        foreach (var w in Widgets().Where(w => w.Action == PortraitAction.PartyPick && w.Enabled).Take(4).ToList())
            await Tap(w, "party pick");
        var started = await TapAction(PortraitAction.QuestStart, "quest start");
        Shoot("quest_started");
        var fought = false;
        var visits = new Dictionary<int, int>();
        // 出发后可能直接进战斗，也可能先进地下城/行走；驱动到战斗结束或回据点。
        for (var i = 0; i < 200 && started; i++)
        {
            if (_root.CombatView.Visible) { fought = true; await FinishCombat("委托"); continue; }
            if (ModalOn) { await Escape(); continue; }
            if (_root.DebugVm?.Hub is { InQuestDungeon: false } && AtHubRoot && fought) break;
            if (await TapAction(PortraitAction.ConversationAdvance, "quest talk")) continue;
            if (_root.DebugVm?.Hub.InQuestDungeon == true)
            {
                // 探索：先去没去过的「？」，再去去得最少的房间；都走过两遍就撤离。
                var cells = Widgets().Where(w => w.Action == PortraitAction.Cell && w.Enabled && w.Index != CurrentCellIndex() && w.Label.Length > 0).ToList();
                var next = cells.OrderBy(w => (w.Label is "？" or "?") ? -1 : visits.GetValueOrDefault(w.Index)).Cast<PortraitWidget?>().FirstOrDefault();
                if (next != null && visits.GetValueOrDefault(next.Value.Index) < 2)
                {
                    visits[next.Value.Index] = visits.GetValueOrDefault(next.Value.Index) + 1;
                    await Tap(next.Value, "dungeon walk");
                    continue;
                }
                if (await TapAction(PortraitAction.HubWorld, "dungeon leave"))
                {
                    if (ModalOn) await TapLabel("确定", "confirm leave");
                    continue;
                }
            }
            if (await TapAction(PortraitAction.CrossGate, "dungeon gate")) continue;
            if (await TapAction(PortraitAction.WorldGo, "quest go")) continue;
            if (AtHubRoot) break;
            await Escape();
        }
        Shoot("quest_done");
        Goal("委托出发并打完", started && fought && await GoHubRoot("委托收尾"));
    }

    /// <summary>
    /// 探索类委托：在委托板上挑一张现生成的地城探索委托，编满人出发，逐间揭雾（先走「？」，再走去得最少的），
    /// 守卫照打，打到正主倒下被接回领地。核对：通关记账、酬金到账、板上撕下。另记走了几间、打了几场、耗时多少。
    /// </summary>
    private async Task GoalDungeonQuest()
    {
        const string name = "探索委托打通";
        await GoHubRoot("探索委托");
        await TapAction(PortraitAction.Tab, "quest tab", w => w.Index == 2);
        var hub = _root.DebugVm!.Hub;
        var idx = _root.HubScreen.DebugQuestIndex(d => d.Generated);
        var def = idx >= 0 ? Rimisekai.Quest.QuestBoard.Open(hub.State)[idx] : null;
        // 生成的委托排在固定委托之后，多半在可见区外：像人一样往上拖卡片流，直到它的「接取」露出来。
        for (var k = 0; k < 8 && def != null && !Widgets().Any(w => w.Action == PortraitAction.QuestTake && w.Index == idx); k++)
            await Drag(new Vector2(540f, 1800f), new Vector2(540f, 900f));
        Shoot("dq_board");
        if (def == null || !await TapAction(PortraitAction.QuestTake, "take dungeon quest", w => w.Index == idx))
        {
            Goal(name, false);
            return;
        }
        foreach (var w in Widgets().Where(w => w.Action == PortraitAction.PartyPick && w.Enabled).Take(4).ToList())
            await Tap(w, "party pick");
        var money = hub.State.Money;
        var minutes = hub.State.Clock.TotalMinutes;
        var started = await TapAction(PortraitAction.QuestStart, "quest start") && hub.InQuestDungeon;
        Shoot("dq_arrive");
        var visits = new Dictionary<int, int>();
        var fights = 0;
        var walks = 0;
        var crosses = 0;
        var regionVisits = new Dictionary<int, int>();
        for (var i = 0; i < 400 && started; i++)
        {
            if (_root.CombatView.Visible)
            {
                fights++;
                if (fights == 1) Shoot("dq_fight");
                await FinishCombat(name);
                continue;
            }
            if (ModalOn)
            {
                // 遭遇弹窗：迎战 / 收下 / 祈祷，不撤退。
                if (!await TapAction(PortraitAction.ModalChoice, "dq encounter", w => w.Label is "fight" or "accept") && !await Escape()) break;
                continue;
            }
            if (!hub.InQuestDungeon) break;
            if (Find(PortraitAction.SheetClose) != null)
            {
                Break("dungeon", Surface(), "[探索委托] 点格弹出了房间抽屉");
                await TapAction(PortraitAction.SheetClose, "dq close sheet");
                continue;
            }
            var cells = Widgets().Where(w => w.Action == PortraitAction.Cell && w.Enabled && w.Index != CurrentCellIndex() && w.Label.Length > 0).ToList();
            // 本块的雾揭完了：去别的块。读画面上各格通向哪一块（只读），挑去得最少的那一块：
            // 人正站在通往它的口子上就过界，否则先走到那个口子的房间。
            if (!cells.Any(w => w.Label is "？" or "?"))
            {
                regionVisits[hub.RegionId] = regionVisits.GetValueOrDefault(hub.RegionId);
                var exits = Widgets().Where(w => w.Action == PortraitAction.Cell && w.Enabled && hub.CrossTargetRegion(w.Index) >= 0)
                    .Select(w => (Cell: w, To: hub.CrossTargetRegion(w.Index))).ToList();
                if (exits.Count > 0)
                {
                    var goal = exits.OrderBy(e => regionVisits.GetValueOrDefault(e.To)).First();
                    if (goal.Cell.Index == CurrentCellIndex())
                    {
                        if (await TapAction(PortraitAction.CrossGate, "dq gate"))
                        {
                            crosses++;
                            regionVisits[hub.RegionId] = regionVisits.GetValueOrDefault(hub.RegionId) + 1;
                        }
                        continue;
                    }
                    walks++;
                    await Tap(goal.Cell, "dq to gate");
                    continue;
                }
            }
            var next = cells.OrderBy(w => (w.Label is "？" or "?") ? -1 : visits.GetValueOrDefault(w.Index)).Cast<PortraitWidget?>().FirstOrDefault();
            if (next == null)
                break;
            visits[next.Value.Index] = visits.GetValueOrDefault(next.Value.Index) + 1;
            walks++;
            if (walks == 12) Shoot("dq_fog");
            await Tap(next.Value, "dq walk");
        }
        await Settle();
        Shoot("dq_done");
        var cleared = hub.State.Quests.ClearCount.GetValueOrDefault(def.Id) > 0;
        var paid = hub.State.Money - money;
        var off = !Rimisekai.Quest.QuestBoard.Open(hub.State).Any(q => q.Id == def.Id);
        _log.Add($"[探索委托] {def.Name} {def.Difficulty}星 正主={string.Join("、", def.Foes.Select(f => f.Name))} 酬金={def.RewardMoney} " +
                 $"走={walks} 过界={crosses} 战={fights} 耗时={hub.State.Clock.TotalMinutes - minutes}分 进账={paid} 通关={cleared} 撕下={off} 在地城={hub.InQuestDungeon}");
        Goal(name, started && cleared && off && paid >= def.RewardMoney && await GoHubRoot("探索委托收尾"));
    }

    /// <summary>
    /// 场景内主动对话：带同伴进一张探索委托，在地城里逐间走，等同伴自己开口——
    /// 气泡要指着「此刻」里的说话人、说的是地城那一组话（台词分场景，不许把家常话带进地城）、点一下推进、说完收起。
    /// </summary>
    private async Task GoalChatter()
    {
        const string name = "同伴主动开口";
        await GoHubRoot(name);
        await TapAction(PortraitAction.Tab, "quest tab", w => w.Index == 2);
        var hub = _root.DebugVm!.Hub;
        var idx = _root.HubScreen.DebugQuestIndex(d => d.Generated);
        for (var k = 0; k < 8 && idx >= 0 && !Widgets().Any(w => w.Action == PortraitAction.QuestTake && w.Index == idx); k++)
            await Drag(new Vector2(540f, 1800f), new Vector2(540f, 900f));
        if (idx < 0 || !await TapAction(PortraitAction.QuestTake, "take", w => w.Index == idx)) { Goal(name, false); return; }
        foreach (var w in Widgets().Where(w => w.Action == PortraitAction.PartyPick && w.Enabled).Take(4).ToList())
            await Tap(w, "party pick");
        if (!await TapAction(PortraitAction.QuestStart, "start") || !hub.InQuestDungeon) { Goal(name, false); return; }
        var spoke = false;
        var wrongScene = false;
        var visits = new Dictionary<int, int>();
        for (var i = 0; i < 160 && hub.InQuestDungeon; i++)
        {
            if (_root.CombatView.Visible) { await FinishCombat(name); continue; }
            if (ModalOn) { if (!await TapAction(PortraitAction.ModalChoice, "enc", w => w.Label is "fight" or "accept") && !await Escape()) break; continue; }
            var bubble = Find(PortraitAction.ChatterAdvance);
            if (bubble != null)
            {
                var chat = hub.PendingChatter!;
                wrongScene |= hub.Setting != Rimisekai.Voice.VoiceSetting.Dungeon || !chat.Line.Settings.Contains(Rimisekai.Voice.VoiceSetting.Dungeon);
                Shoot("chatter_bubble");
                _log.Add($"[主动开口] {chat.Speaker.Name}：{string.Join(" / ", chat.Lines)}（场景 {hub.Setting}，台词 {chat.Line.Id} 属于 {string.Join("/", chat.Line.Settings)}）");
                var count = chat.Lines.Count;
                for (var k = 0; k < count; k++)
                    await Tap(bubble.Value, "chatter advance");
                spoke = hub.PendingChatter == null && Find(PortraitAction.ChatterAdvance) == null;
                Shoot("chatter_closed");
                break;
            }
            var cells = Widgets().Where(w => w.Action == PortraitAction.Cell && w.Enabled && w.Index != CurrentCellIndex() && w.Label.Length > 0).ToList();
            var next = cells.OrderBy(w => (w.Label is "？" or "?") ? -1 : visits.GetValueOrDefault(w.Index)).Cast<PortraitWidget?>().FirstOrDefault();
            if (next == null) break;
            visits[next.Value.Index] = visits.GetValueOrDefault(next.Value.Index) + 1;
            await Tap(next.Value, "walk");
        }
        Goal(name, spoke && !wrongScene);
    }

    private int CurrentCellIndex() => _root.DebugVm?.Hub.PlayerRoomId ?? -1;

    private async Task GoalTalk()
    {
        await GoHubRoot("对话");
        await TapAction(PortraitAction.Tab, "home", w => w.Index == 0);
        // 点「此刻」行里的同伴头像 → 交互抽屉 → 聊天 → 推进到底。
        var avatar = Find(PortraitAction.NowAvatar, w => w.Index != _root.DebugVm!.Hub.State.Roster.Master!.Id);
        if (avatar == null) { Goal("与同伴对话", false); return; }
        await Tap(avatar.Value, "pick companion");
        Shoot("social_sheet");
        var chatted = await TapLabel("交谈", "chat");
        chatted = chatted && (await TapLabel("聊天", "chat sub") || await TapAction(PortraitAction.SocialRun, "social run"));
        var talked = false;
        for (var i = 0; i < 80; i++)
        {
            if (await TapAction(PortraitAction.ConversationChoice, "talk choice")) { talked = true; continue; }
            if (await TapAction(PortraitAction.ConversationAdvance, "talk advance")) { talked = true; continue; }
            if (ModalOn) { await Escape(); continue; }
            break;
        }
        Shoot("talk_done");
        Goal("与同伴对话", chatted && talked && await GoHubRoot("对话收尾"));
    }

    private async Task GoalWorld()
    {
        await GoHubRoot("出行");
        await TapAction(PortraitAction.Tab, "home", w => w.Index == 0);
        var went = await TapAction(PortraitAction.HubWorld, "go out");
        Shoot("world");
        var arrived = false;
        var hub = _root.DebugVm!.Hub;
        var start = hub.WorldPartyPosition;
        // 点格已移除（2026-10-10）：用方向箭头逐格朝最近的兴趣点走，踩上即弹「进入」。
        var poi = hub.State.World.Pois.OrderBy(p => Math.Abs(p.X - start.Item1) + Math.Abs(p.Y - start.Item2)).First();
        for (var step = 0; step < 60 && !arrived; step++)
        {
            for (var i = 0; i < 30 && (ModalOn || _root.CombatView.Visible); i++)
            {
                if (_root.CombatView.Visible) await FinishCombat("出行遭遇");
                else if (!await TapLabel("迎战", "encounter fight")) await Escape();
            }
            if (await TapAction(PortraitAction.WorldGo, $"enter {poi.NameZh}", w => w.Label == "进入"))
            {
                arrived = true;
                break;
            }
            var (x, y) = hub.WorldPartyPosition;
            var dirs = new List<int>();
            if (poi.X != x) dirs.Add(poi.X > x ? 1 : 3);
            if (poi.Y != y) dirs.Add(poi.Y > y ? 2 : 0);
            var moved = false;
            foreach (var d in dirs)
            {
                if (!await TapAction(PortraitAction.WorldStep, "step", w => w.Index == d && w.Enabled)) continue;
                moved = hub.WorldPartyPosition != (x, y);
                if (moved) break;
            }
            if (!moved) break;
        }
        Shoot("world_walked");
        var back = await TapAction(PortraitAction.HubWorld, "return home") || await TapAction(PortraitAction.WorldHome, "home btn");
        for (var i = 0; i < 30 && (ModalOn || _root.CombatView.Visible); i++)
        {
            if (_root.CombatView.Visible) await FinishCombat("回程遭遇");
            else if (!await TapLabel("迎战", "encounter fight")) await Escape();
        }
        Shoot("world_back");
        Goal("出行到地点并返回", went && arrived && back);
    }

    private async Task GoalSaveLoad()
    {
        await GoHubRoot("存档");
        var opened = await TapAction(PortraitAction.OpenSystem, "open system");
        var saved = await TapAction(PortraitAction.SaveNow, "save");
        Shoot("saved");
        var slot = Find(PortraitAction.SavePick);
        var loaded = false;
        if (slot != null)
        {
            await Tap(slot.Value, "load pick");
            for (var i = 0; i < 4 && ModalOn; i++)
                if (!await TapLabel("读取", "confirm load") && !await TapLabel("确定", "confirm")) break;
            loaded = _root.HubScreen.Visible;
        }
        Shoot("loaded");
        Goal("存档并读档", opened && saved && slot != null && loaded && await GoHubRoot("存读档收尾"));
    }

    private async Task GoalBuild()
    {
        await GoHubRoot("建造");
        await TapAction(PortraitAction.Tab, "home", w => w.Index == 0);
        var open = await TapAction(PortraitAction.Build, "build");
        var cell = Find(PortraitAction.DevelopmentCell);
        var acted = false;
        if (cell != null)
        {
            await Tap(cell.Value, "dev cell");
            acted = await TapAction(PortraitAction.DevelopmentRoom, "dev room") || await TapAction(PortraitAction.DevelopmentAction, "dev action");
            for (var i = 0; i < 4 && ModalOn; i++) if (!await TapLabel("确定", "confirm build")) await Escape();
        }
        Shoot("build");
        Goal("建造页可操作", open && cell != null && acted && await GoHubRoot("建造收尾"));
    }

    private async Task GoalTrade()
    {
        await GoHubRoot("交易");
        await TapAction(PortraitAction.Tab, "storage", w => w.Index == 3);
        await TapAction(PortraitAction.StoreSegment, "trade seg", w => w.Index == 1);
        var plus = await TapAction(PortraitAction.TradePlus, "plus");
        var run = await TapAction(PortraitAction.TradeRun, "deal");
        Shoot("trade");
        Goal("交易买入", plus && run);
    }

    // ================= 随机乱点 =================

    private async Task Monkey()
    {
        var lastNew = 0;
        var sameSurface = 0;
        var lastSurface = "";
        for (var i = 0; i < _monkeySteps; i++)
        {
            await Settle();
            if (_root.TitleView.Visible && !ModalOn)
            {
                // 回到标题：从「继续」或「新的开始」再进。
                if (!await TapLabel("继续", "title continue")) await TapLabel("新的开始", "title new");
                continue;
            }
            var ws = Widgets().Where(w => w.Enabled && w.Action is not (PortraitAction.ModalInput or PortraitAction.Rename)).ToList();
            // 不点退出到标题/退出游戏，免得一直重开。
            ws = ws.Where(w => w.Label is not ("退出" or "回到标题")).ToList();
            if (ws.Count == 0)
            {
                if (CombatRunning) { await Frames(20); continue; }
                if (ModalOn && await Escape()) continue;
                Break("deadend", Surface(), "随机游玩时遇到没有可点钮的画面");
                if (!await Escape()) break;
                continue;
            }
            var surface = Surface();
            sameSurface = surface == lastSurface ? sameSurface + 1 : 0;
            lastSurface = surface;
            if (sameSurface > 60)
            {
                Break("trap", surface, "随机点了 60 次仍停在同一画面");
                await GoHubRoot("乱点脱困");
                sameSurface = 0;
                continue;
            }
            // 偶尔拖一下（滚动/平移），其余点没点过最少的动作。
            if (_rng.NextDouble() < 0.06)
            {
                var a = new Vector2(540, 1500);
                await Drag(a, a + new Vector2(_rng.Next(-300, 300), _rng.Next(-600, 600)));
                continue;
            }
            var pick = ws.OrderBy(w => _covered.GetValueOrDefault($"{surface}>{w.Action}") * 10 + _rng.Next(10)).First();
            var before = _screens.Count;
            await Tap(pick, "monkey");
            if (i % 50 == 0) WriteReport();
            if (_screens.Count > before) lastNew = i;
            if (i - lastNew > 400 && i % 200 == 0)
                await GoHubRoot("乱点换区");
        }
    }

    // ================= 主流程 =================

    private async Task Run()
    {
        await Frames(10);
        var steps = new (string, Func<Task>)[]
        {
            ("开局", GoalStart), ("页签", GoalTabs), ("对话", GoalTalk), ("交易", GoalTrade),
            ("建造", GoalBuild), ("委托", GoalQuest), ("探索委托", GoalDungeonQuest), ("主动开口", GoalChatter), ("出行", GoalWorld), ("存读档", GoalSaveLoad),
        };
        foreach (var (name, step) in steps)
        {
            if (_only.Count > 0 && !_only.Contains(name)) continue;
            try { await step(); }
            catch (Exception ex) { Break("exception", Surface(), $"[{name}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"); }
        }
        WriteReport();
        try { await Monkey(); }
        catch (Exception ex) { Break("exception", Surface(), $"[乱点] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"); }
        WriteReport();
        GetTree().Quit();
    }

    private void WriteReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# 模拟游玩报告  seed={_seed} taps={_taps} screens={_screens.Count}");
        sb.AppendLine("## 目标");
        foreach (var (k, v) in _goals) sb.AppendLine($"- {(v ? "PASS" : "FAIL")} {k}");
        sb.AppendLine($"## 断点 ({_breaks.Count})");
        foreach (var b in _breaks) sb.AppendLine("- " + b);
        sb.AppendLine("## 点了没反应（可用钮，画面签名不变）");
        foreach (var (k, v) in _noop.OrderByDescending(x => x.Value).Take(40)) sb.AppendLine($"- {v}× {k}");
        sb.AppendLine("## 覆盖到的画面");
        foreach (var s in _screens.OrderBy(x => x)) sb.AppendLine("- " + s);
        File.WriteAllText(Path.Combine(_out, "report.md"), sb.ToString());
        File.WriteAllLines(Path.Combine(_out, "taps.log"), _log);
        GD.Print($"PLAYTEST DONE goals={_goals.Count(g => g.Value)}/{_goals.Count} breaks={_breaks.Count} taps={_taps}");
    }
}
