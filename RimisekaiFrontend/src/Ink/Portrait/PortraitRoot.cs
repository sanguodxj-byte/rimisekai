using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Defs;
using Rimisekai.Flow;
using Rimisekai.Save;
using Rimisekai.Hub;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 手机版根画面：竖屏 1080×2340，自己走一遍启动配方（内容 provider → GameFlow → 画面），
/// 但版面、命中、输入全按触摸重做。
/// </summary>
public partial class PortraitRoot : Control
{
    private ContentPack _pack = null!;
    private GameFlow _flow = null!;
    private HubSession? _hub;
    private InkViewModel? _vm;
    private PortraitTitleView _title = null!;
    private PortraitHubScreen _hubScreen = null!;
    private PortraitCombatView _combat = null!;
    private PortraitModalLayer _modal = null!;
    private PortraitGenerationPump _pump = null!;

    /// <summary>刘海/圆角安全区（竖屏 notch 在上方，顶栏要整体下移）。</summary>
    public static Vector2 SafeInset()
    {
        var safe = DisplayServer.GetDisplaySafeArea();
        var screen = DisplayServer.ScreenGetSize();
        return new Vector2(
            Math.Max(0f, safe.Position.X) / Math.Max(1, screen.X) * PortraitLayout.CanvasWidth,
            Math.Max(0f, safe.Position.Y) / Math.Max(1, screen.Y) * PortraitLayout.CanvasHeight);
    }

    public PortraitHubScreen HubScreen => _hubScreen;
    public PortraitTitleView TitleView => _title;
    public PortraitCombatView CombatView => _combat;
    public PortraitModalLayer ModalLayer => _modal;
    public Rimisekai.Flow.FlowPhase DebugPhase => _flow.Phase;
    public Rimisekai.Session.BattleSession? DebugCombat => _vm?.Combat;
    public Rimisekai.Ink.InkViewModel? DebugVm => _vm;

    /// <summary>核对用：跳过标题画面直接进据点。</summary>
    public void DebugStart() => OnStartNew();

    /// <summary>桌面预览窗让给标题条与边框的高度（4K/150% 屏实测窗口外框比内容高 56px）。</summary>
    private const float PreviewChrome = 56f;

    public override void _Ready()
    {
        InkContentProvider.Install();

        if (GetViewport() is Window window)
        {
            window.ContentScaleSize = new Vector2I((int)PortraitLayout.CanvasWidth, (int)PortraitLayout.CanvasHeight);
            if (OS.GetName() is not ("Android" or "iOS") && !window.IsEmbedded())
            {
                // 预览窗吃满可用高：2340 的画布落到桌面上必然缩放，占多满就读得动多少。
                // 只管独立窗口——F5 嵌进编辑器面板时几何归面板，硬设尺寸会把画面撑出面板外，
                // 位置也会被引擎拒掉（日志里那句 Embedded window can't be moved）。
                // Position 是内容原点，所以上下各让一次边框——上让给标题条（否则出屏抓不到窗口），
                // 下让给底边框（否则压进任务栏吃掉页签带）。
                var work = DisplayServer.ScreenGetUsableRect();
                var h = (int)(work.Size.Y - PreviewChrome * 2);
                var w = (int)Math.Round(h * PortraitLayout.CanvasWidth / PortraitLayout.CanvasHeight);
                window.Size = new Vector2I(w, h);
                window.Position = new Vector2I(work.Position.X + (work.Size.X - w) / 2,
                    work.Position.Y + (int)PreviewChrome);
            }
        }
        SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        // 顶栏与页签都从安全区之下起排：刘海/状态栏占去的高度写进版式。
        PortraitLayout.SafeTop = SafeInset().Y;

        _pack = new ContentPack();
        _flow = new GameFlow { Name = "PortraitFlow" };
        AddChild(_flow);

        _title = new PortraitTitleView { Name = "PortraitTitle" };
        _hubScreen = new PortraitHubScreen { Name = "PortraitHub" };
        _combat = new PortraitCombatView { Name = "PortraitCombat" };
        _modal = new PortraitModalLayer { Name = "PortraitModal" };
        _pump = new PortraitGenerationPump { Name = "PortraitPump" };
        AddChild(_pump);
        _title.StartRequested += OnStartNew;
        _title.ContinueRequested += OnContinue;
        _title.SettingsRequested += () => OpenSystem(InkSystemScreen.PageSettings);
        _title.QuitRequested += () => GetTree().Quit();
        _title.LoadRequested += path =>
        {
            if (InkSaveStore.TryLoad(path, _pack, out var state, out var hub, out _)
                && state != null && hub != null)
                OnLoaded(state, hub);
        };
        _hubScreen.LoadRequested += OnLoaded;
        _hubScreen.ModalWanted += page => _modal.Show(page);
        _hubScreen.BattleWanted += EnterCombat;
        _combat.LoadRequested += path =>
        {
            _modal.Dismiss();
            if (_vm != null)
                _vm.Combat = null;
            if (InkSaveStore.TryLoad(path, _pack, out var state, out var hub, out _)
                && state != null && hub != null)
                OnLoaded(state, hub);
        };
        _combat.TitleRequested += () =>
        {
            _modal.Dismiss();
            if (_vm != null)
                _vm.Combat = null;
            _flow.Enter(FlowPhase.Title);
        };
        _combat.UnitImageProvider = unit =>
        {
            var member = _vm?.Hub.State.Roster.Find(unit.Name);
            if (member != null)
                return PortraitAvatars.Resolve(member);
            // 敌方：按内容声明的立绘资产名取 assets/portraits/monster/ 下的实机立绘（黑底转透明）。
            return unit.Portrait.Length == 0 ? null : LoadMonsterArt(unit.Portrait);
        };
        _combat.TrackImageProvider = unit =>
        {
            var member = _vm?.Hub.State.Roster.Find(unit.Name);
            if (member != null)
                return PortraitAvatars.Resolve(member);
            // 敌方小图位：assets/avatars/monster/ 下的头像裁切（保留黑底，不去背）。
            return unit.Portrait.Length == 0 ? null : LoadMonsterAvatar(unit.Portrait);
        };
        _combat.Finished += OnCombatFinished;
        AddChild(_title);
        AddChild(_hubScreen);
        AddChild(_combat);
        AddChild(_modal);

        _flow.PhaseChanged += OnPhase;
        OnPhase((int)FlowPhase.Title);
    }

    private void OnPhase(int phase)
    {
        _title.Visible = (FlowPhase)phase == FlowPhase.Title;
        _hubScreen.Visible = (FlowPhase)phase == FlowPhase.Hub;
        _combat.Visible = (FlowPhase)phase == FlowPhase.Combat;
    }

    private void OnStartNew()
    {
        _hub = null;
        var state = InkWorldBootstrap.Create(_pack);
        PrepareHub(state);
        _hub!.WriteOpening();
        _flow.Start(state);
    }

    private void OnContinue() => OpenSystem(InkSystemScreen.PageLoad);

    /// <summary>先把会话绑上，再切相位——否则据点画面会在 _vm 还是空的时候落笔。</summary>
    private void PrepareHub(GameState state)
    {
        _hub = InkWorldBootstrap.OpenHub(state, _pack);
        // 定时事件的排班与后台预生成：开局就把该掷的演员掷好、
        // 该生成的台词排进队列，到点即可直接开演。
        _hub.InitializeScheduledEvents();
        _pump.Bind(_hub);
        _pump.SetRunning(true);
        _vm = new InkViewModel(_hub, _pack);
        _hubScreen.Bind(_vm);
    }

    private void OnLoaded(GameState state, HubSession hub)
    {
        _hub = hub;
        // 读档后接着把还没生成完的定时事件台词补齐。
        _pump.Bind(_hub);
        _pump.SetRunning(true);
        _vm = new InkViewModel(hub, _pack);
        _hubScreen.Bind(_vm);
        _flow.Start(state);
    }

    private void OpenSystem(string page)
    {
        if (_title.Visible)
            _title.ShowSystem(page);
        else
            _hubScreen.OpenSystemPage(page);
    }

    /// <summary>战斗画面由外部（ encounter 来源 / 核对工具）交进来。</summary>
    public void EnterCombat(Rimisekai.Session.BattleSession session)
    {
        if (_vm == null)
            return;
        _vm.Combat = session;
        _combat.Bind(_vm, _modal);
        _flow.Enter(FlowPhase.Combat);
    }

    private static readonly System.Collections.Generic.Dictionary<string, Texture2D?> MonsterArtCache = new();

    private static readonly System.Collections.Generic.Dictionary<string, Texture2D?> MonsterAvatarCache = new();

    /// <summary>载入怪物头像（assets/avatars/monster/，已按头位截取、保留黑底不去背），结果缓存。</summary>
    private static Texture2D? LoadMonsterAvatar(string name)
    {
        if (MonsterAvatarCache.TryGetValue(name, out var cached))
            return cached;
        var tex = InkIllustration.LoadTexture($"res://assets/avatars/monster/{name}.png");
        MonsterAvatarCache[name] = tex;
        return tex;
    }

    /// <summary>
    /// 载入怪物立绘并去背：先把亮部（画笔）膨胀 seal 像素封住腕缝一类的细长开口，
    /// 再从四边对未封区泛洪——扩散到的置全透；其余（含内部黑）保持不透明。结果缓存。
    /// </summary>
    private static Texture2D? LoadMonsterArt(string name)
    {
        if (MonsterArtCache.TryGetValue(name, out var cached))
            return cached;
        Texture2D? tex = null;
        var image = InkIllustration.LoadTexture($"res://assets/portraits/monster/{name}.png")?.GetImage();
        if (image != null)
        {
            image.Convert(Image.Format.Rgba8);
            var width = image.GetWidth();
            var height = image.GetHeight();
            const float bright = 40f / 255f;
            const int seal = 24;
            var sealedPx = new bool[width * height];
            var frontier = new Queue<int>();
            for (var i = 0; i < sealedPx.Length; i++)
            {
                var p = image.GetPixel(i % width, i / width);
                if (p.R >= bright || p.G >= bright || p.B >= bright)
                {
                    sealedPx[i] = true;
                    frontier.Enqueue(i);
                }
            }
            // 亮部逐层膨胀 seal 层：细缝、腕缝全部封口。
            for (var layer = 0; layer < seal && frontier.Count > 0; layer++)
            {
                var count = frontier.Count;
                while (count-- > 0)
                {
                    var i = frontier.Dequeue();
                    var x = i % width;
                    var y = i / width;
                    if (x > 0 && !sealedPx[i - 1]) { sealedPx[i - 1] = true; frontier.Enqueue(i - 1); }
                    if (x < width - 1 && !sealedPx[i + 1]) { sealedPx[i + 1] = true; frontier.Enqueue(i + 1); }
                    if (y > 0 && !sealedPx[i - width]) { sealedPx[i - width] = true; frontier.Enqueue(i - width); }
                    if (y < height - 1 && !sealedPx[i + width]) { sealedPx[i + width] = true; frontier.Enqueue(i + width); }
                }
            }
            // 从四边对未封区泛洪：可达者即外部底色，置全透。
            var exterior = new bool[width * height];
            frontier.Clear();
            void Seed(int x, int y)
            {
                var i = y * width + x;
                if (!sealedPx[i] && !exterior[i])
                {
                    exterior[i] = true;
                    frontier.Enqueue(i);
                }
            }
            for (var x = 0; x < width; x++)
            {
                Seed(x, 0);
                Seed(x, height - 1);
            }
            for (var y = 0; y < height; y++)
            {
                Seed(0, y);
                Seed(width - 1, y);
            }
            while (frontier.Count > 0)
            {
                var i = frontier.Dequeue();
                var x = i % width;
                var y = i / width;
                if (x > 0) Seed(x - 1, y);
                if (x < width - 1) Seed(x + 1, y);
                if (y > 0) Seed(x, y - 1);
                if (y < height - 1) Seed(x, y + 1);
            }
            // 边缘羽化：由外向内的距离场（上限 feather），smoothstep 过渡——轮廓自然淡入背景。
            const int feather = 28;
            var dist = new byte[width * height];
            frontier.Clear();
            for (var i = 0; i < exterior.Length; i++)
                if (exterior[i])
                    frontier.Enqueue(i);
            while (frontier.Count > 0)
            {
                var i = frontier.Dequeue();
                var x = i % width;
                var y = i / width;
                void Relax(int j)
                {
                    if (exterior[j] || dist[j] > 0)
                        return;
                    dist[j] = (byte)Math.Min(dist[i] + 1, feather);
                    if (dist[j] < feather)
                        frontier.Enqueue(j);
                }
                if (x > 0) Relax(i - 1);
                if (x < width - 1) Relax(i + 1);
                if (y > 0) Relax(i - width);
                if (y < height - 1) Relax(i + width);
            }
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    var p = image.GetPixel(x, y);
                    if (exterior[i])
                        image.SetPixel(x, y, new Color(p.R, p.G, p.B, 0f));
                    else
                    {
                        // dist 0 = 深于羽化带的内部，全不透明。
                        var t = (dist[i] == 0 ? feather : dist[i]) / (float)feather;
                        image.SetPixel(x, y, new Color(p.R, p.G, p.B, t * t * (3f - 2f * t)));
                    }
                }
            tex = ImageTexture.CreateFromImage(image);
        }
        MonsterArtCache[name] = tex;
        return tex;
    }

    private void OnCombatFinished()
    {
        if (_vm?.Combat != null)
            _vm.Hub.SettleEncounterBattle(_vm.Combat.Battle);
        if (_vm != null)
            _vm.Combat = null;
        _hubScreen.RefreshAfterCombat();
        _flow.Enter(FlowPhase.Hub);
    }
}

/// <summary>
/// 标题画面：标志＋入口钮（继续＝主钮带最近存档副行；开始 / 设置并排描边钮；退出窄钮）。
/// 设置与读档是标题画面自带的推入页（顶栏返回＋卡片列表）。
/// </summary>
public partial class PortraitTitleView : Control
{
    public Action? StartRequested;
    public Action? ContinueRequested;
    public Action? SettingsRequested;
    public Action? QuitRequested;
    public Action<string>? LoadRequested;

    private readonly List<PortraitWidget> _hits = new();
    private Texture2D? _art;
    private Texture2D? _frame;
    private string _systemPage = "";
    private int _first;
    private Vector2 _press;
    private int _pressFirst;
    private bool _dragging;
    private bool _pressed;
    private Rect2? _pressRect;

    public IReadOnlyList<PortraitWidget> DebugWidgets => _hits;
    public string DebugSystemPage => _systemPage;

    private const float SlotStep = 250f;

    public override void _Ready()
    {
        // 只挪锚点不修偏移 = 控件停在 0×0：画得出来但 _GuiInput 永远收不到点击。
        SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        _art = InkIllustration.LoadTexture("res://assets/title_reference.png");
        _frame = InkIllustration.GetOverlayFrame();
    }

    public override void _Draw()
    {
        PortraitFrame.Backdrop(this);
        PortraitFrame.SetPress(_pressed && !_dragging ? _pressRect : null);
        _hits.Clear();
        if (_systemPage.Length > 0)
        {
            DrawSystem();
            return;
        }
        if (_art != null)
            DrawTextureRectRegion(_art, PortraitLayout.TitleArt, PortraitLayout.TitleArtSource);
        PortraitFrame.SectionRule(this, 160f, PortraitLayout.CanvasWidth - 160f, PortraitLayout.TitleArt.End.Y + 80f);

        var saves = InkSaveStore.ListSaves();
        var latest = saves.Count > 0 ? $"{saves[0].TerritoryName} · 第 {saves[0].Day} 日" : "";
        var labels = new[] { "新的开始", "继续", "设置", "退出" };
        for (var i = 0; i < labels.Length; i++)
        {
            var r = PortraitLayout.TitleButton(i);
            // 没有存档：「继续」压暗不可点，实心主钮让给「新的开始」（一屏只留一个实心主钮）。
            var enabled = i != 1 || saves.Count > 0;
            var primary = i == (saves.Count > 0 ? 1 : 0);
            PortraitFrame.Plaque(this, r, labels[i], primary: primary, enabled: enabled, sub: i == 1 ? latest : "",
                size: i == 1 ? 56 : PortraitLayout.FontBody);
            _hits.Add(new PortraitWidget(r, PortraitAction.Tab, i, enabled, labels[i]));
        }

        if (_frame != null)
            PortraitFrame.Mount(this, _frame,
                new Rect2(0f, 0f, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight));
    }

    public void ShowSystem(string page)
    {
        InkSettings.EnsureLoaded();
        _systemPage = page;
        _first = 0;
        QueueRedraw();
    }

    private Rect2 ListView => new(0, PortraitLayout.PageBody.Position.Y + 30f, PortraitLayout.CanvasWidth,
        PortraitLayout.CanvasHeight - PortraitLayout.PageBody.Position.Y - 30f);

    private void DrawSystem()
    {
        var load = _systemPage == InkSystemScreen.PageLoad;
        var view = ListView;
        if (load)
        {
            var slots = InkSaveStore.ListSaves();
            var max = Math.Max(0, (int)(slots.Count * SlotStep - view.Size.Y));
            _first = Math.Clamp(_first, 0, max);
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var r = new Rect2(PortraitLayout.Pad, view.Position.Y + i * SlotStep - _first, PortraitLayout.FullWidth, 230f);
                if (r.End.Y < view.Position.Y || r.Position.Y > view.End.Y)
                    continue;
                PortraitSystemArt.SaveCard(this, r, slot, i == 0, PortraitFrame.IsPressed(r.Intersection(view)));
                var shown = r.Intersection(view);
                if (shown.Size.Y >= PortraitLayout.TouchMin)
                    _hits.Add(new PortraitWidget(shown, PortraitAction.SavePick, i, true, slot.FilePath));
            }
            if (slots.Count == 0)
                PortraitSystemArt.EmptySaves(this, view);
        }
        else
        {
            var y = view.Position.Y + 20f;
            var volume = InkSettings.CurrentMasterVolume;
            PortraitSystemArt.Volume(this, y, volume);
            var track = new Rect2(380f, y + 51f, PortraitLayout.CanvasWidth - 380f - PortraitLayout.Pad - 40f, 16f);
            var band = new Rect2(track.Position.X - 60f, y, track.Size.X + 120f, PortraitLayout.TouchMin);
            for (var i = 0; i <= 4; i++)
            {
                var cx = track.Position.X + track.Size.X * i / 4f;
                _hits.Add(new PortraitWidget(new Rect2(cx - track.Size.X / 8f, y, track.Size.X / 4f, PortraitLayout.TouchMin).Intersection(band),
                    PortraitAction.SystemToggle, i, true, $"{i * 25}"));
            }
        }

        var top = PortraitLayout.PageTop;
        DrawRect(new Rect2(0, 0, PortraitLayout.CanvasWidth, top.End.Y), InkStyle.Bg);
        GothicArt.Tile(this, new Rect2(0, 0, PortraitLayout.CanvasWidth, top.End.Y), 0.7f);
        var back = PortraitLayout.PageBack;
        if (PortraitFrame.IsPressed(back))
            PortraitFrame.PressMark(this, back.Grow(-8f));
        PortraitGlyph.Back(this, back.Position.X + 64f, back.GetCenter().Y, 30f, InkStyle.Line);
        _hits.Add(new PortraitWidget(back, PortraitAction.Back, 0, true, "返回"));
        InkDraw.Text(this, top.GetCenter(), load ? "读取进度" : "设置", PortraitLayout.FontPlace, InkStyle.Line, "cm");
        DrawRect(new Rect2(0, top.End.Y - 4f, PortraitLayout.CanvasWidth, 3f), InkStyle.Dim);
        PortraitFrame.FadingRule(this, 0f, PortraitLayout.CanvasWidth, top.End.Y - 2f);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion motion && _pressed)
        {
            var dy = motion.Position.Y - _press.Y;
            if (Math.Abs(dy) >= PortraitLayout.ListDragThreshold)
                _dragging = true;
            if (_dragging && _systemPage == InkSystemScreen.PageLoad)
            {
                _first = Math.Max(0, _pressFirst - (int)dy);
                QueueRedraw();
            }
            return;
        }
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel
            && _systemPage == InkSystemScreen.PageLoad)
        {
            _first = Math.Max(0, _first + (wheel.ButtonIndex == MouseButton.WheelUp ? -120 : 120));
            QueueRedraw();
            return;
        }
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
            return;
        if (mb.Pressed)
        {
            _pressed = true;
            _press = mb.Position;
            _pressFirst = _first;
            _dragging = false;
            _pressRect = Hit(mb.Position);
            QueueRedraw();
            return;
        }
        if (!_pressed)
            return;
        _pressed = false;
        _pressRect = null;
        if (_dragging)
        {
            QueueRedraw();
            return;
        }
        for (var i = _hits.Count - 1; i >= 0; i--)
        {
            var hit = _hits[i];
            if (!hit.Rect.HasPoint(mb.Position) || !hit.Rect.HasPoint(_press))
                continue;
            if (!hit.Enabled)
                break;
            if (hit.Action == PortraitAction.Back)
                _systemPage = "";
            else if (hit.Action == PortraitAction.SavePick)
                LoadRequested?.Invoke(hit.Label);
            else if (hit.Action == PortraitAction.SystemToggle)
                InkSettings.ApplyMasterVolume(hit.Index / 4f);
            else
                switch (hit.Index)
                {
                    case 0: StartRequested?.Invoke(); break;
                    case 1: ContinueRequested?.Invoke(); break;
                    case 2: SettingsRequested?.Invoke(); break;
                    default: QuitRequested?.Invoke(); break;
                }
            QueueRedraw();
            return;
        }
        QueueRedraw();
    }

    /// <summary>按下点到的块矩形（按下反馈用）；没点中任何块返回 null。</summary>
    private Rect2? Hit(Vector2 at)
    {
        for (var i = _hits.Count - 1; i >= 0; i--)
            if (_hits[i].Rect.HasPoint(at))
                return _hits[i].Rect;
        return null;
    }
}

/// <summary>
/// 弹窗层：通用模态（问卷 / 输入 / 确认 / 叙事 / 战后结算）。
/// 输入一律交给原生 LineEdit——软键盘的中文输入法只认它，逐键 Unicode 那套在手机上收不到字。
/// </summary>
public partial class PortraitModalLayer : Control
{
    private readonly InkModalSession _session = new();
    private readonly List<PortraitWidget> _hits = new();
    private readonly LineEdit _ime = new() { Modulate = new Color(1, 1, 1, 0f) };

    public bool IsActive => _session.IsActive;
    public InkModalPage? Current => _session.Current;
    public IReadOnlyList<PortraitWidget> DebugWidgets => _hits;
    public Rect2 DebugPanel => _modalPanel;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(_ime);
        _ime.TextSubmitted += _ => Submit();
        _ime.TextChanged += _ =>
        {
            if (_session.Current?.Input != null)
                _session.Current.Input.Text = _ime.Text;
            QueueRedraw();
        };
    }

    public void Show(InkModalPage page)
    {
        _session.Enqueue(page);
        Visible = true;
        QueueRedraw();
    }

    public void ShowRange(IEnumerable<InkModalPage> pages)
    {
        _session.EnqueueRange(pages);
        Visible = true;
        QueueRedraw();
    }

    public void Dismiss()
    {
        _session.DismissAll();
        _ime.Hide();
        Visible = false;
    }

    public override void _Draw()
    {
        _hits.Clear();
        // 弹窗自己不做按下反馈（点任意处推进），这里清掉据点层留下的按下矩形，
        // 免得坐标撞上时把弹窗的钮画成按下态。
        PortraitFrame.SetPress(null);
        if (_session.Current != null)
            DrawModalPage(_session.Current);
    }

    public override void _Process(double delta)
    {
        if (!IsActive)
            return;
        _modalTime += (float)delta;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent e)
    {
        if (HandleModalScroll(e))
            return;
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } mb)
            return;
        foreach (var h in _hits)
        {
            if (!h.Rect.HasPoint(mb.Position) || !h.Enabled)
                continue;
            if (h.Action == PortraitAction.ModalInput)
                FocusIme();
            else
                Choose(h.Label);
            return;
        }
        if (_session.Current is { HasInteractiveControls: false })
            Advance();
    }

    /// <summary>核对工具用：按弹窗选项 Id 直选（与点按同一回调路径）。</summary>
    public void Choose(string id)
    {
        var page = _session.Current;
        if (page == null)
            return;
        foreach (var c in page.Choices)
        {
            if (c.Id != id || !c.Enabled)
                continue;
            c.OnSelected?.Invoke();
            break;
        }
        Advance();
    }

    private void Submit()
    {
        var input = _session.Current?.Input;
        if (input == null)
            return;
        input.OnSubmit?.Invoke(input.Text);
        _ime.ReleaseFocus();
        Advance();
    }

    private void FocusIme()
    {
        var input = _session.Current?.Input;
        if (input == null)
            return;
        _ime.Position = _modalInput.Position;
        _ime.Size = _modalInput.Size;
        _ime.MaxLength = input.MaxChars;
        _ime.Text = input.Text;
        _ime.Show();
        _ime.GrabFocus();
    }

    private void Advance()
    {
        _modalFirst = 0;
        _ime.ReleaseFocus();
        _ime.Hide();
        if (!_session.Advance())
        {
            _ime.Hide();
            Visible = false;
        }
        QueueRedraw();
    }

    /// <summary>与全局同一套换行（含中文避头尾）。</summary>
    private static List<string> Wrap(string text, float width, int size) => InkDraw.WrapLines(text, width, size).ToList();
}
