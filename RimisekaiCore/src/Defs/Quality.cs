namespace Rimisekai.Defs;

/// <summary>
/// 品质。既是价值倍率，也是**面板乘数**——同一种材料同一把型，
/// 品质差的那一把就是实打实地弱。
/// </summary>
public enum Quality
{
    /// <summary>粗劣。作坊里剔出来的次品。</summary>
    Crude = 0,

    /// <summary>寻常。街上随手买得的那一档。</summary>
    Common = 1,

    /// <summary>精致。匠人留手的作品。</summary>
    Fine = 2,

    /// <summary>史诗。值得写进歌谣。</summary>
    Epic = 3,

    /// <summary>传说。一柄一个名号。</summary>
    Legendary = 4,

    /// <summary>
    /// 独特。**名称与属性固定**——不参与随机生成，由内容包逐件登记
    /// （"村长的旧猎弓"这类）。这是 Def 表存在的理由。
    /// </summary>
    Unique = 5,
}

/// <summary>品质的面板乘数、价值倍率与显示名。</summary>
public static class QualityOf
{
    /// <summary>面板乘数（百分比）。品质直接决定强弱。</summary>
    public static int PanelFactor(Quality quality) => quality switch
    {
        Quality.Crude => 70,
        Quality.Common => 100,
        Quality.Fine => 130,
        Quality.Epic => 170,
        Quality.Legendary => 220,
        Quality.Unique => 200,
        _ => 100,
    };

    /// <summary>价值倍率（百分比）。</summary>
    public static int ValueFactor(Quality quality) => quality switch
    {
        Quality.Crude => 50,
        Quality.Common => 100,
        Quality.Fine => 180,
        Quality.Epic => 320,
        Quality.Legendary => 600,
        Quality.Unique => 500,
        _ => 100,
    };

    public static string Label(Quality quality) => quality switch
    {
        Quality.Crude => "粗劣",
        Quality.Common => "寻常",
        Quality.Fine => "精致",
        Quality.Epic => "史诗",
        Quality.Legendary => "传说",
        Quality.Unique => "独特",
        _ => "寻常",
    };

    /// <summary>是否参与随机生成。独特品质只来自登记表。</summary>
    public static bool IsRollable(Quality quality) => quality != Quality.Unique;
}
