namespace Rimisekai.Housing;

/// <summary>
/// 设施可交互性的唯一判据：这件设施支不支持某个行动。
/// 行动就是 <see cref="ActionKind"/>——设施直接声明自己支持哪些行动，
/// 不再有"用途表兜底"这一层间接（旧 FacilityActionTable 已删）。
///
/// 设施能消遣（舞台）、有专属外观——这些是**设施标签**
/// （<see cref="FacilityUsage"/>），不是行动，不参与本判据。
/// </summary>
public static class FacilityActions
{
    /// <summary>
    /// 这件设施支不支持这个行动。
    /// 存取是例外：能存货的设施天然支持存取，不必逐件声明。
    /// </summary>
    public static bool Supports(this Facility facility, ActionKind action)
    {
        if (action == ActionKind.Store && facility.CanStore)
            return true;
        return facility.Actions.Contains(action);
    }
}
