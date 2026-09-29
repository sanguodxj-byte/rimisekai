using Godot;
using Rimisekai.Flow;

namespace Rimisekai.Ink;

/// <summary>
/// 主场景根节点。只负责读内容包并把画面路由挂上，
/// 世界本身等标题画面的“开始游戏”才建。
///
/// 缩放交给 project.godot 的 stretch（canvas_items + keep）：
/// 画布固定 1920×1080，窗口变化时引擎统一缩放并补黑边，
/// 界面自己不再做第二次缩放。
/// </summary>
public partial class InkRoot : Node
{
    public override void _Ready()
    {
        // 内容包只读一次：开局与后续进出据点共用同一份。
        var pack = ContentPack.Load(InkWorldBootstrap.ContentPath, InkWorldBootstrap.SeedPath, InkWorldBootstrap.BuildingsPath);

        var flow = new GameFlow { Name = "GameFlow" };
        var router = new InkScreenRouter { Name = "ScreenRouter" };

        AddChild(flow);
        AddChild(router);

        // 先接信号再挂画面，否则首次 PhaseChanged 会漏掉。
        router.Setup(flow, pack);

        // 开局停在标题画面；世界等玩家点“开始游戏”才建（见路由的 Title 分支）。
        router.Attach();
    }
}
