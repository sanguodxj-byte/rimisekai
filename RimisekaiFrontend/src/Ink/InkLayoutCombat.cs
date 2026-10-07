using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 战斗界面版式：三面板下沉量、左侧竖向跑条、中上方 Boss 头、右侧战况日志窗。
/// </summary>
public static partial class InkLayout
{
    /// <summary>战斗形态下三面板的下沉量：底边收进画面内，全程可见不被裁。</summary>
    public static readonly Vector2 CombatPanelSink = new(0f, 70f);

    /// <summary>
    /// 战斗左上角：竖向跑条区域（严格收敛在最左侧边缘，位于地图名称下方）。
    /// 主人定跑条改用头像，故宽度需容纳左右两条头像车道；
    /// 右缘收于 178，与最左侧敌人卡（x≈188）仍留 10px 净空，不侵占敌阵舞台。
    /// </summary>
    public static readonly Rect2 CombatVerticalTimeline = new(48f, 88f, 130f, 540f);

    /// <summary>战斗中上方：Boss/精英名称 + 大血条 + 行动点条（类似碧蓝幻想风格，居中 960 中轴）。</summary>
    public static readonly Rect2 CombatBossHeader = new(580f, 32f, 760f, 96f);

    /// <summary>战斗中上方：Boss 行动点下方的当前行动名称容器（攻击、防御、技能名、道具名等，居中 960 中轴）。</summary>
    public static readonly Rect2 CombatActionNameBox = new(830f, 150f, 260f, 36f);

    /// <summary>战斗右侧：战况日志窗口（收窄贴右缘，底边收于 460，不侵占下方前排怪物的纵深空间）。</summary>
    public static readonly Rect2 CombatLogWindow = new(1512f, 60f, 360f, 400f);
}
