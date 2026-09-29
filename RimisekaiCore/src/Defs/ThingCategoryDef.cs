using System;

namespace Rimisekai.Defs;

/// <summary>
/// 物品品类定义（如食物、原材料、药品、武器）。支持树形父子层级，便于仓储一键分类过滤。
/// </summary>
public sealed class ThingCategoryDef : Def
{
    /// <summary>父品类的 DefName。根品类此项为空串。</summary>
    public string ParentCategory { get; init; } = "";

    /// <summary>判断当前品类是否是目标品类或其子品类。</summary>
    public bool IsOrChildOf(string parentName)
    {
        if (string.Equals(DefName, parentName, StringComparison.OrdinalIgnoreCase))
            return true;

        var cur = this;
        while (!string.IsNullOrEmpty(cur.ParentCategory))
        {
            if (string.Equals(cur.ParentCategory, parentName, StringComparison.OrdinalIgnoreCase))
                return true;
            cur = DefDatabase<ThingCategoryDef>.Get(cur.ParentCategory);
            if (cur == null)
                break;
        }
        return false;
    }
}
