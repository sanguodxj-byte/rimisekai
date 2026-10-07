using Godot;

namespace Rimisekai.Portrait;

public static partial class PortraitLayout
{
    // ---------- 角色状态页版式（三段式：立绘+装备 / 状态+战斗+属性 / 能力抽屉） ----------

    // 顶部段（Y: 370 .. 1050）：立绘（左 460px）＋ 装备栏（右 500px）
    public static readonly Rect2 StatusPortraitCard = new(Pad, 370f, 460f, 680f);
    public static readonly Rect2 StatusSidebar = new(540f, 370f, 500f, 680f);

    // 装备栏：双列 × 五行（10 槽位），每槽单格 240 宽 × 110 高，行距 118
    public const float EquipColumnGap = 20f;
    public const float EquipTop = 436f;
    public const float EquipRowPitch = 118f;
    public const float EquipRowHeight = 110f;
    public static float EquipColumnWidth => (StatusSidebar.Size.X - EquipColumnGap) / 2f; // 240

    public static Rect2 EquipSlotCell(int slotIndex)
    {
        var col = slotIndex % 2;
        var row = slotIndex / 2;
        return new Rect2(
            StatusSidebar.Position.X + col * (EquipColumnWidth + EquipColumnGap),
            EquipTop + row * EquipRowPitch,
            EquipColumnWidth,
            EquipRowHeight);
    }

    // 中部段 A：状态（Y: 1074 .. 1300，高 226px，宽 1000px 全宽双列）
    public const float StatusVitalsTop = 1074f;
    public const float StatusVitalsHeight = 226f;
    public static readonly Rect2 StatusVitals = new(Pad, StatusVitalsTop, CanvasWidth - Pad * 2f, StatusVitalsHeight);

    // 状态 4 格（双列 × 两行）：每列 480 宽，行高 80（给经验条与渐隐线留出独立分区）
    public const float VitalColGap = 40f;
    public static float VitalColWidth => (StatusVitals.Size.X - VitalColGap) / 2f; // 480

    public static Rect2 StatusVitalCell(int index)
    {
        var col = index % 2;
        var row = index / 2;
        return new Rect2(
            StatusVitals.Position.X + col * (VitalColWidth + VitalColGap),
            StatusVitals.Position.Y + 68f + row * 80f,
            VitalColWidth,
            80f);
    }

    // 中部段 B：战斗（左 480px）与 属性（右 480px）（Y: 1324 .. 1632，高 308px）
    public const float StatusMetricsTop = 1324f;
    public const float StatusMetricsHeight = 308f;
    public const float StatusMetricsWidth = 480f;
    public static readonly Rect2 StatusCombat = new(Pad, StatusMetricsTop, StatusMetricsWidth, StatusMetricsHeight);
    public static readonly Rect2 StatusAttributes = new(560f, StatusMetricsTop, StatusMetricsWidth, StatusMetricsHeight);

    // 战斗与属性每栏双列 × 3 行，每格 230 宽，行高 80（给经验条与渐隐线留出独立分区）
    public const float MetricColGap = 20f;
    public static float MetricColWidth => (StatusMetricsWidth - MetricColGap) / 2f; // 230

    public static Rect2 StatusMetricCell(Rect2 area, int index)
    {
        var col = index % 2;
        var row = index / 2;
        return new Rect2(
            area.Position.X + col * (MetricColWidth + MetricColGap),
            area.Position.Y + 68f + row * 80f,
            MetricColWidth,
            80f);
    }

    // 底部段（Y: 1650 .. 2098，高 448px）：能力三抽屉（生活 / 武器 / 流派）
    public const float StatusAbilityTop = 1650f;
    public const float StatusAbilityHeight = 448f;
    public static readonly Rect2 StatusAbilities = new(Pad, StatusAbilityTop, CanvasWidth - Pad * 2f, StatusAbilityHeight);

    public const float StatusAbilityRowGap = 8f;
    public const float StatusAbilityRowHeight = TouchMin + StatusAbilityRowGap; // 126f
    public static int StatusAbilityVisibleRows => 3;

    public static Rect2 StatusAbilityRow(int index, bool hasScroll = false) => new(
        StatusAbilities.Position.X + Pad,
        StatusAbilities.Position.Y + 70f + index * StatusAbilityRowHeight,
        hasScroll ? StatusAbilities.Size.X - Pad * 2f - TouchMin - 16f : StatusAbilities.Size.X - Pad * 2f,
        TouchMin);

    public static Rect2 StatusAbilityScroll => new(
        StatusAbilities.End.X - Pad - TouchMin,
        StatusAbilities.Position.Y + 70f,
        TouchMin,
        StatusAbilityVisibleRows * StatusAbilityRowHeight - StatusAbilityRowGap);
}

