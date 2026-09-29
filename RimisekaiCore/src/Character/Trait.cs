namespace Rimisekai.Character;

/// <summary>
/// 素质。有或没有，不升级。每条同时挂游戏性（PersonalityTraits 实现）
/// 与对话侧（Keynote/语气词/感叹号档），统一从 Traits 门面查询。
/// 枚举值只追加不重排，保住旧存档的素质编号。
/// </summary>
public enum Trait
{
    Timid,
    Defiant,
    Honest,
    Prideful,
    Lazy,
    FastLearner,
    SlowLearner,
    Cold,
    Curious,
    FearPain,
    IgnorePain,
    QuickRecovery,
    SlowRecovery,
    Mage,
    Artisan,
    Alchemist,

    /// <summary>女仆。不要工资，也不按好感决定接不接受邀请。</summary>
    Maid,

    // ---------- 性格光谱（docs/voice-writing-guide.md 第十一节） ----------

    // 外向组
    Talkative,
    Quiet,
    SlowWarmer,
    SocialChameleon,
    // 情绪组
    Nervous,
    Expressive,
    StillWaters,
    Optimist,
    Melancholy,
    HotTempered,
    Unflappable,
    // 待人组
    Attentive,
    SharpTongued,
    Worrier,
    PeoplePleaser,
    Arrogant,
    Forthright,
    Protective,
    // 做事组
    Dutiful,
    Perfectionist,
    Procrastinator,
    Methodical,
    FreeSpirit,
    // 认知组
    Inquisitive,
    Bookish,
    DownToEarth,
    Dreamer,
    Meticulous,
    Superstitious,
    // 独特组
    Foodie,
    NightFearful,
    GrouchyMornings,
    Frugal,
    Generous,
    DeftHands,
    Directionless,
    NeatFreak,
    Sentimental,
    NightOwl,
    VerbalTic,
}
