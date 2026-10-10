namespace Rimisekai.Hub;

public enum SocialAction
{
    Talk,
    Observe,
    Gift,
    Touch,
    PatHead,
    BodyContact,
    Hug,
    Kiss,
    SoftKiss,
    DeepKiss,

    /// <summary>邀请同行。女仆无视好感答应，其余人要好感到“好感”档。</summary>
    Invite,

    /// <summary>分开。请正在跟随的人回去，与邀请是两个动作。</summary>
    Part,
}

public readonly record struct SocialSpec(
    SocialAction Action,
    string Label,
    int CostTicks,
    int FavorMin,
    int FavorGain,
    int MoodGain,
    bool NeedsBond = false);
