namespace Rimisekai.Housing;

/// <summary>
/// 设施标签。这件设施在**日常起居**里扮演什么角色——它是标签，不是行动。
/// 床能睡、灶能做饭，那是行动（ActionKind），写在设施自己的 Actions 里；
/// 本枚举只回答"这地方属于哪类去处"，供 AI 选点与界面分组用。
///
/// 工作设施没有专属标签：它是矿点还是工作台，由它支持的行动说清楚。
/// </summary>
public enum FacilityUsage
{
    /// <summary>起居。床、躺椅、浴池这类过夜与歇脚的地方。</summary>
    Rest = 0,

    /// <summary>消遣。戏台、吧台、书架这类闲时找乐子的去处。</summary>
    Leisure = 1,

    /// <summary>摆设。没有专属角色的物件（货架、摊位、城门……）。</summary>
    Plain = 2,
}
