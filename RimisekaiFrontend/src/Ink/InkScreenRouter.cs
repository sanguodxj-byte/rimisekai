using System;
using System.Collections.Generic;
using Godot;
using Rimisekai.Flow;
using Rimisekai.Hub;

namespace Rimisekai.Ink;

/// <summary>
/// 画面路由。听 GameFlow 的 PhaseChanged，按阶段挂对应界面。
///
/// 标题与据点界面已就位，任务/战斗尚未实现；
/// 支持打开系统界面（设置、保存进度、读取进度）并在关闭时返回原界面。
/// </summary>
public partial class InkScreenRouter : Node
{
    private GameFlow? _flow;

    /// <summary>
    /// 据点会话跨阶段保留：从任务回来时据点现场不能丢。
    /// 由路由持有，而不是每次进 Hub 都新建。
    /// 在开新局或读档时必须失效置 null。
    /// </summary>
    private HubSession? _hub;

    /// <summary>内容包只读一次，后续进出据点复用。</summary>
    private ContentPack? _pack;

    private Control? _screen;

    /// <summary>系统页面打开前的来源（Title 或 Hub），用于关闭时无缝返回。</summary>
    private FlowPhase _previousPhase = FlowPhase.Title;

    /// <summary>系统页面实例，打开时挂在当前树上。</summary>
    private InkSystemScreen? _systemScreen;

    /// <summary>任务页实例，打开时盖在当前画面上。</summary>
    private InkQuestScreen? _questScreen;

    /// <summary>战斗页实例（FlowPhase.Combat 时挂载）。</summary>
    private InkCombatScreen? _combatScreen;

    /// <summary>复古转场盖层（换屏时抓旧画面→播放 shader 混合）。</summary>
    private InkTransition? _transition;

    /// <summary>接上阶段信号。必须在 GameFlow 进入任何阶段之前调用。</summary>
    public void Setup(GameFlow flow, ContentPack pack)
    {
        _flow = flow;
        _pack = pack;
        _flow.PhaseChanged += OnPhaseChanged;
    }

    /// <summary>
    /// 开局后调用一次：确保首帧有画面。
    /// 通常 GameFlow.Start 已经触发过 PhaseChanged，这里只在还没挂过界面时兜底。
    /// </summary>
    public void Attach()
    {
        if (_flow == null || _screen != null)
            return;

        Show((FlowPhase)_flow.Phase);
    }

    private void OnPhaseChanged(int phase)
    {
        if (_flow == null)
            return;
        CloseSystem();
        CloseQuest();
        Show((FlowPhase)_flow.Phase);
    }

    private void Show(FlowPhase phase)
    {
        if (_flow == null || _pack == null)
            return;

        switch (phase)
        {
            case FlowPhase.Title:
                ReplaceScreen(BuildTitleScreen(_flow, _pack));
                break;

            case FlowPhase.Hub:
                _hub ??= InkWorldBootstrap.OpenHub(_flow.State, _pack);
                // 标题 → 据点：走复古转场（先抓旧画面，换屏后再播放）。
                ReplaceScreen(BuildHubScreen(_hub, _pack), transition: true);
                break;

            case FlowPhase.Combat:
                _hub ??= InkWorldBootstrap.OpenHub(_flow.State, _pack);
                ReplaceScreen(BuildCombatScreen());
                break;

            default:
                // 任务 / 战斗 / 标题尚未实现，先如实占位。
                ReplaceScreen(BuildPlaceholder(phase));
                break;
        }
    }

    /// <summary>
    /// 标题画面。世界等到点“开始游戏”才建；
    /// “继续”打开读档页，“设置”打开设置页，“退出”直接关游戏。
    /// </summary>
    private InkTitleScreen BuildTitleScreen(GameFlow flow, ContentPack pack)
    {
        var screen = new InkTitleScreen { Name = "TitleScreen" };
        screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        screen.StartRequested += () =>
        {
            _hub = null; // 新游戏使旧据点失效
            flow.Start(InkWorldBootstrap.Create(pack));
        };
        screen.ContinueRequested += () => OpenSystem(InkSystemScreen.PageLoad);
        screen.SettingsRequested += () => OpenSystem(InkSystemScreen.PageSettings);
        screen.QuitRequested += () => screen.GetTree().Quit();
        return screen;
    }

    private InkHubScreen BuildHubScreen(HubSession hub, ContentPack pack)
    {
        var screen = new InkHubScreen { Name = "HubScreen" };
        screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        screen.Bind(new InkViewModel(hub, pack));

        screen.SystemRequested += OpenSystem;
        screen.QuestRequested += OpenQuest;

        return screen;
    }

    /// <summary>战斗是单独场景：进入战斗阶段时实例化这份 tscn。</summary>
    private static readonly PackedScene CombatScene = ResourceLoader.Load<PackedScene>(
        "res://RimisekaiFrontend/src/Ink/InkCombatScreen.tscn");

    /// <summary>战斗场景：独立挂载，关闭（离开/撤退/Esc）即回据点。</summary>
    private Control BuildCombatScreen()
    {
        var screen = CombatScene.Instantiate<InkCombatScreen>();
        screen.Name = "CombatScreen";
        screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        screen.Bind(_hub);
        screen.CloseRequested += () =>
        {
            _combatScreen = null;
            _flow?.Enter(FlowPhase.Hub);
        };
        _combatScreen = screen;
        return screen;
    }

    /// <summary>打开任务页；世界浏览走据点地图网格，战斗走独立页面，都不在这里。</summary>
    public void OpenQuest()
    {
        if (_hub == null || _questScreen != null)
            return;
        CloseSystem();

        _questScreen = new InkQuestScreen { Name = "QuestScreen" };
        _questScreen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _questScreen.Bind(_hub);
        _questScreen.CloseRequested += CloseQuest;
        AddChild(_questScreen);
    }

    /// <summary>关掉任务页，回到据点现场。</summary>
    public void CloseQuest()
    {
        if (_questScreen == null)
            return;
        RemoveChild(_questScreen);
        _questScreen.QueueFree();
        _questScreen = null;
    }

    /// <summary>
    /// 打开系统页面（设置、保存进度、读取进度）。
    /// 供路由自身回调以及主会话调试直接调用。
    /// </summary>
    public void OpenSystem(string page)
    {
        if (_pack == null)
            return;

        if (_systemScreen != null)
        {
            _systemScreen.ShowPage(page);
            return;
        }

        _previousPhase = _flow != null ? (FlowPhase)_flow.Phase : FlowPhase.Title;

        var state = _flow?.State;
        _systemScreen = new InkSystemScreen(page, state, _hub, _pack);
        _systemScreen.CloseRequested += CloseSystem;
        _systemScreen.LoadRequested += OnLoadRequested;

        AddChild(_systemScreen);
    }

    /// <summary>
    /// 关闭系统界面并返回先前的画面。
    /// </summary>
    public void CloseSystem()
    {
        if (_systemScreen != null)
        {
            RemoveChild(_systemScreen);
            _systemScreen.QueueFree();
            _systemScreen = null;
        }
    }

    private void OnLoadRequested(Rimisekai.Save.GameState state, HubSession hub)
    {
        CloseSystem();
        if (_flow == null || _pack == null)
            return;

        _hub = hub; // 换上加载好的据点
        _flow.Start(state); // 切入 Hub 阶段
    }

    private static Label BuildPlaceholder(FlowPhase phase)
    {
        var label = new Label
        {
            Name = "Placeholder",
            Text = $"{phase} 画面尚未实现",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return label;
    }

    private void ReplaceScreen(Control next, bool transition = false)
    {
        if (transition && _screen != null)
            Transition().Capture();

        if (_screen != null)
        {
            RemoveChild(_screen);
            _screen.QueueFree();
        }

        _screen = next;
        AddChild(next);

        if (transition)
            Transition().Play(InkTransition.Current, 1.0f);
    }

    /// <summary>转场盖层懒挂载：常驻路由下，只在大切换时出现。</summary>
    private InkTransition Transition()
    {
        if (_transition == null)
        {
            _transition = new InkTransition { Name = "Transition" };
            AddChild(_transition);
        }
        return _transition;
    }
}
