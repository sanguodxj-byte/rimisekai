using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Clock;

namespace Rimisekai.Voice;

/// <summary>
/// 判定与挑选台词时用的世界快照。刻意只放平铺的值，不持有 GameState，
/// 这样自动节律（Housing）也能组一份，不必反向依赖存档层。
/// 由 VoiceContext.For 统一组装，避免各挂点各填一套。
/// </summary>
public sealed class VoiceContext
{
    /// <summary>说话的角色。</summary>
    public required CharacterState Character { get; init; }

    /// <summary>主角 Id，-1 表示没有主角。</summary>
    public int MasterId { get; init; } = -1;

    /// <summary>触发的时机。</summary>
    public VoiceTrigger Trigger { get; init; }

    /// <summary>从开局起的累计分钟数，与 Affect.LastTalkAt 同一把尺子。</summary>
    public int NowTotal { get; init; }

    /// <summary>当前小时（0-23）。</summary>
    public int Hour { get; init; }

    public Season Season { get; init; }
    public Weather Weather { get; init; }
    public int Day { get; init; } = 1;

    /// <summary>赠物触发时的物品 Id。</summary>
    public string GiftItemId { get; init; } = "";

    /// <summary>玩家当前所在房间，-1 表示未知。</summary>
    public int PlayerRoomId { get; init; } = -1;

    /// <summary>该角色说过什么。Once / 冷却 / RequireSaid 都查这一份。</summary>
    public VoiceMemory Memory { get; init; } = new();

    /// <summary>供门槛里做一次性掷骰。同一句话在一次挑选里只掷一次。</summary>
    public System.Random Rng { get; init; } = new();

    // ---------- 场景维度 ----------

    /// <summary>说话人此刻在做什么。</summary>
    public VoiceActivity Activity { get; init; } = VoiceActivity.Idle;

    /// <summary>说话人在这次互动里的角色（动手/承受/旁观）。</summary>
    public VoiceRole Role { get; init; } = VoiceRole.Actor;

    /// <summary>动作处在哪个阶段（事前/事中/事后）。</summary>
    public VoicePlace Place { get; init; } = VoicePlace.Before;

    /// <summary>说话人此刻的情绪。由宿主按状态推导。</summary>
    public VoiceEmotion Emotion { get; init; } = VoiceEmotion.Any;

    /// <summary>说话人当前所在的房间，-1 表示未知。</summary>
    public int CharacterRoomId { get; init; } = -1;

    /// <summary>说话人正在使用的设施 Id，-1 表示没有。</summary>
    public int FacilityId { get; init; } = -1;

    /// <summary>
    /// 组装上下文。minutes 是当天分钟数，这里换算成累计分钟数，
    /// 与 Affect.LastTalkAt 的口径对齐。
    /// </summary>
    public static VoiceContext For(
        CharacterState character,
        VoiceTrigger trigger,
        int masterId,
        Season season,
        Weather weather,
        int day,
        int minutes,
        int playerRoomId = -1,
        string giftItemId = "",
        System.Random? rng = null,
        VoiceActivity activity = VoiceActivity.Idle,
        VoiceRole role = VoiceRole.Actor,
        VoicePlace place = VoicePlace.Before,
        VoiceEmotion emotion = VoiceEmotion.Any,
        int characterRoomId = -1,
        int facilityId = -1)
    {
        var normalizedDay = day < 1 ? 1 : day;
        return new VoiceContext
        {
            Character = character,
            MasterId = masterId,
            Trigger = trigger,
            NowTotal = (normalizedDay - 1) * GameClock.MinutesPerDay + minutes,
            Hour = minutes / 60,
            Season = season,
            Weather = weather,
            Day = normalizedDay,
            PlayerRoomId = playerRoomId,
            GiftItemId = giftItemId,
            Memory = character.Voice,
            Rng = rng ?? new System.Random(),
            Activity = activity,
            Role = role,
            Place = place,
            Emotion = emotion,
            CharacterRoomId = characterRoomId,
            FacilityId = facilityId,
        };
    }

    /// <summary>按当前上下文推导情绪。内容没指定情绪时用它填。</summary>
    public static VoiceEmotion EmotionOf(CharacterState character)
    {
        var mood = character.Affect.Mood;
        if (mood >= 80)
            return VoiceEmotion.Happy;
        if (mood <= 10)
            return VoiceEmotion.Angry;
        if (mood <= 25)
            return VoiceEmotion.Sad;
        if (character.Condition.Fatigue >= Vitals.TiredAt / 2)
            return VoiceEmotion.Tired;
        if (character.Condition.Bond >= Bond.Lover)
            return VoiceEmotion.Excited;
        return VoiceEmotion.Neutral;
    }
}
