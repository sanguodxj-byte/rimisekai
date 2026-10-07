using System.Collections.Generic;
using System.Linq;
using Rimisekai.Character;
using Rimisekai.Clock;

namespace Rimisekai.Voice;

/// <summary>
/// 一句台词的门槛。所有字段都是可选的，留空即不限制。
/// 未填的字段不参与判定，因此门槛写多了不会互相干扰。
/// </summary>
public sealed class VoiceGate
{
    // ---------- 关系 ----------

    /// <summary>好感下限（含）。</summary>
    public int? FavorMin { get; set; }

    /// <summary>好感上限（含）。</summary>
    public int? FavorMax { get; set; }

    /// <summary>好感至少到这一档。</summary>
    public Bond? BondMin { get; set; }

    /// <summary>好感至多到这一档。用来写“还只是熟人时”的台词。</summary>
    public Bond? BondMax { get; set; }

    /// <summary>与主角之间存在这些关系。</summary>
    public List<RelationFlag> RequireRelations { get; set; } = new();

    /// <summary>与主角之间不存在这些关系。</summary>
    public List<RelationFlag> ForbidRelations { get; set; } = new();

    /// <summary>距离上次交谈至少这么多天。从没交谈过视为满足。</summary>
    public int? DaysSinceTalkMin { get; set; }

    // ---------- 状态 ----------

    public int? MoodMin { get; set; }
    public int? MoodMax { get; set; }

    /// <summary>说话人是否湿透。true=只对湿透的人生效，false=只对没湿透的人生效。</summary>
    public bool? Soaked { get; set; }

    /// <summary>说话人等级下限（含）。</summary>
    public int? LevelMin { get; set; }

    /// <summary>是否要求全队角色均已满级（100级）。苛刻剧情使用。</summary>
    public bool? AllMembersMaxLevel { get; set; }

    /// <summary>是否要求刚从战斗/副本胜利归来。同床共寝或归来剧情使用。</summary>
    public bool? ReturnedFromCombat { get; set; }

    /// <summary>所在房间包含这些标签之一（如"卧室"、"室外"等）。</summary>
    public List<string> RoomTags { get; set; } = new();

    /// <summary>身上有这些素质。</summary>
    public List<Trait> RequireTraits { get; set; } = new();

    /// <summary>身上没有这些素质。</summary>
    public List<Trait> ForbidTraits { get; set; } = new();

    // ---------- 世界 ----------

    /// <summary>时刻下限（0-23，含）。</summary>
    public int? HourMin { get; set; }

    /// <summary>时刻上限（0-23，含）。下限大于上限时表示跨夜，如 22 到 5。</summary>
    public int? HourMax { get; set; }

    public Season? Season { get; set; }
    public Weather? Weather { get; set; }

    /// <summary>第几天起可用（含）。</summary>
    public int? DayMin { get; set; }

    /// <summary>第几天后失效（含）。</summary>
    public int? DayMax { get; set; }

    /// <summary>赠物触发时，只对这件物品生效。留空表示任意礼物。</summary>
    public string GiftItemId { get; set; } = "";

    // ---------- 出现频率 ----------

    /// <summary>百分比。掷不过就跳过这一句，用来让同一批台词有稀有度。</summary>
    public int? Chance { get; set; }

    /// <summary>说过一次就不再出现。</summary>
    public bool Once { get; set; }

    /// <summary>距上次说这句至少这么多分钟。</summary>
    public int CooldownMinutes { get; set; }

    /// <summary>必须先说过这些台词。</summary>
    public List<string> RequireSaid { get; set; } = new();

    /// <summary>说过这些台词后本句失效。</summary>
    public List<string> ForbidSaid { get; set; } = new();

    // ---------- 场景 ----------

    /// <summary>说话人必须处于这些活动之一。留空不限。</summary>
    public List<VoiceActivity> Activities { get; set; } = new();

    /// <summary>说话人必须不处于这些活动。留空不限。</summary>
    public List<VoiceActivity> ForbidActivities { get; set; } = new();

    /// <summary>说话人必须扮演这些角色之一。留空不限。</summary>
    public List<VoiceRole> Roles { get; set; } = new();

    /// <summary>动作必须处在这几个阶段之一。留空不限。</summary>
    public List<VoicePlace> Places { get; set; } = new();

    /// <summary>情绪必须是其中之一。留空不限。</summary>
    public List<VoiceEmotion> Emotions { get; set; } = new();

    /// <summary>说话人必须在场于玩家同一房间。</summary>
    public bool? SameRoomAsPlayer { get; set; }

    /// <summary>说话人正在使用某设施。</summary>
    public bool? AtFacility { get; set; }

    /// <summary>说话人所在的房间 Id 之一。</summary>
    public List<int> RoomIds { get; set; } = new();

    /// <summary>逐项判定。任一不过即返回 false。lineId 用于查说过没有。</summary>
    public bool Allows(VoiceContext ctx, string lineId)
    {
        var character = ctx.Character;

        if (FavorMin.HasValue && character.Condition.Favor < FavorMin.Value)
            return false;
        if (FavorMax.HasValue && character.Condition.Favor > FavorMax.Value)
            return false;
        if (BondMin.HasValue && character.Condition.Bond < BondMin.Value)
            return false;
        if (BondMax.HasValue && character.Condition.Bond > BondMax.Value)
            return false;

        var master = ctx.MasterId;
        foreach (var flag in RequireRelations)
        {
            if (master < 0 || !character.Relations.Has(master, flag))
                return false;
        }
        foreach (var flag in ForbidRelations)
        {
            if (master >= 0 && character.Relations.Has(master, flag))
                return false;
        }

        if (DaysSinceTalkMin.HasValue && DaysSinceTalk(character, ctx.NowTotal) < DaysSinceTalkMin.Value)
            return false;

        if (MoodMin.HasValue && character.Affect.Mood < MoodMin.Value)
            return false;
        if (MoodMax.HasValue && character.Affect.Mood > MoodMax.Value)
            return false;
        if (Soaked.HasValue && character.Condition.Soaked != Soaked.Value)
            return false;
        if (LevelMin.HasValue && character.Level < LevelMin.Value)
            return false;
        if (AllMembersMaxLevel.HasValue && ctx.AllMembersMaxLevel != AllMembersMaxLevel.Value)
            return false;
        if (ReturnedFromCombat.HasValue && ctx.ReturnedFromCombat != ReturnedFromCombat.Value)
            return false;

        foreach (var trait in RequireTraits)
        {
            if (!character.Has(trait))
                return false;
        }
        foreach (var trait in ForbidTraits)
        {
            if (character.Has(trait))
                return false;
        }

        if (HourMin.HasValue && HourMax.HasValue)
        {
            if (!InHourRange(ctx.Hour, HourMin.Value, HourMax.Value))
                return false;
        }
        else if (HourMin.HasValue && ctx.Hour < HourMin.Value)
        {
            return false;
        }
        else if (HourMax.HasValue && ctx.Hour > HourMax.Value)
        {
            return false;
        }

        if (Season.HasValue && ctx.Season != Season.Value)
            return false;
        if (Weather.HasValue && ctx.Weather != Weather.Value)
            return false;
        if (DayMin.HasValue && ctx.Day < DayMin.Value)
            return false;
        if (DayMax.HasValue && ctx.Day > DayMax.Value)
            return false;

        if (GiftItemId.Length > 0 && ctx.GiftItemId != GiftItemId)
            return false;

        return AllowsScene(ctx) && AllowsMemory(ctx, lineId);
    }

    /// <summary>场景维度判定：活动、角色、阶段、情绪、所在位置。</summary>
    private bool AllowsScene(VoiceContext ctx)
    {
        if (Activities.Count > 0 && !Activities.Contains(ctx.Activity))
            return false;
        if (ForbidActivities.Count > 0 && ForbidActivities.Contains(ctx.Activity))
            return false;
        if (Roles.Count > 0 && !Roles.Contains(ctx.Role))
            return false;
        if (Places.Count > 0 && !Places.Contains(ctx.Place))
            return false;
        if (Emotions.Count > 0 && !Emotions.Contains(ctx.Emotion))
            return false;

        if (SameRoomAsPlayer.HasValue)
        {
            var same = ctx.CharacterRoomId >= 0 && ctx.CharacterRoomId == ctx.PlayerRoomId;
            if (same != SameRoomAsPlayer.Value)
                return false;
        }

        if (AtFacility.HasValue)
        {
            var at = ctx.FacilityId >= 0;
            if (at != AtFacility.Value)
                return false;
        }

        if (RoomIds.Count > 0 && !RoomIds.Contains(ctx.CharacterRoomId))
            return false;

        if (RoomTags.Count > 0)
        {
            var match = false;
            foreach (var tag in RoomTags)
            {
                if (ctx.RoomTags.Contains(tag))
                {
                    match = true;
                    break;
                }
            }
            if (!match)
                return false;
        }

        return true;
    }

    /// <summary>说过的记账相关判定，与数值门槛分开，便于单独读懂。</summary>
    private bool AllowsMemory(VoiceContext ctx, string lineId)
    {
        var memory = ctx.Memory;

        if (Once && (memory.HasSaid(lineId) || ctx.FiredEvents.Contains(lineId)))
            return false;

        foreach (var required in RequireSaid)
        {
            if (!memory.HasSaid(required))
                return false;
        }
        foreach (var forbidden in ForbidSaid)
        {
            if (memory.HasSaid(forbidden))
                return false;
        }

        if (CooldownMinutes > 0)
        {
            var last = memory.LastSaidAt(lineId);
            if (last >= 0 && ctx.NowTotal - last < CooldownMinutes)
                return false;
        }

        return true;
    }

    /// <summary>从没交谈过按“很久”算，避免早期台词被时间门槛挡掉。</summary>
    private static int DaysSinceTalk(CharacterState character, int nowTotal)
    {
        var last = character.Affect.LastTalkAt;
        if (last < 0)
            return int.MaxValue;
        var elapsed = nowTotal - last;
        return elapsed <= 0 ? 0 : elapsed / GameClock.MinutesPerDay;
    }

    private static bool InHourRange(int hour, int min, int max) =>
        min <= max ? hour >= min && hour <= max : hour >= min || hour <= max;
}
