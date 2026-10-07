namespace Rimisekai.Defs;

/// <summary>
/// 素质定义。纯数据驱动，所有性格特质与机制素质均配置于数据表。
/// </summary>
public sealed class TraitDef : Def
{
    /// <summary>性格光谱分组（外向/情绪/待人/做事/认知/独特/身份/已并入）。</summary>
    public string Group { get; init; } = "";

    /// <summary>对话基调：给 LLM 生成层与 persona 用的一句话提示。</summary>
    public string Keynote { get; init; } = "";

    /// <summary>感叹号档：0 禁用 / 1 偶用 / 2 常用。</summary>
    public int Exclaim { get; init; }

    /// <summary>是否走 F 档亲密基线（Traits 门面据此选 baseline 文案）。</summary>
    public bool IntimateBaseline { get; init; }

    /// <summary>身份标识或已并入别名：不进生成器抽取池。</summary>
    public bool NotInPool { get; init; }
}

/// <summary>互斥对（特质名）。同选视为性格自相矛盾，生成器据此排重。</summary>
public sealed class TraitExclusionDef : Def
{
    public string A
    {
        get => _a;
        init
        {
            _a = value;
            if (string.IsNullOrEmpty(DefName) && !string.IsNullOrEmpty(_a) && !string.IsNullOrEmpty(_b))
                DefName = $"Exclusion_{_a}_{_b}";
        }
    }
    private string _a = "";

    public string B
    {
        get => _b;
        init
        {
            _b = value;
            if (string.IsNullOrEmpty(DefName) && !string.IsNullOrEmpty(_a) && !string.IsNullOrEmpty(value))
                DefName = $"Exclusion_{_a}_{value}";
        }
    }
    private string _b = "";
}
