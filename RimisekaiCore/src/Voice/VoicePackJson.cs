using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rimisekai.Character;
using Rimisekai.Clock;

namespace Rimisekai.Voice;

/// <summary>
/// content/voice.json 的结构与读取。放在 Core 而不是前端，
/// 这样台词能不能被正确解析可以脱离 Godot 直接测。
///
/// 文件形如：
/// <code>
/// {
///   "characters": [
///     { "name": "璐米埃尔", "lines": [ { "id": "...", "trigger": "Meet", "lines": ["..."] } ] }
///   ],
///   "world": [ { "id": "...", "kind": "Narration", "trigger": "DayEnd", "lines": ["..."] } ]
/// }
/// </code>
/// </summary>
public static class VoicePackJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>文件顶层：每个角色一包，外加一包世界通用台词，以及场景事件与角色设定。</summary>
    public sealed class File
    {
        public List<CharacterEntry> Characters { get; set; } = new();
        public List<LineEntry> World { get; set; } = new();

        /// <summary>场景事件（多步剧情）。</summary>
        public List<SceneEntry> Scenes { get; set; } = new();

        /// <summary>据点事件（什么时候演、谁来演，指向一个场景 Id）。</summary>
        public List<EventEntry> Events { get; set; } = new();

        /// <summary>角色的固定设定，供 LLM 生成时当人设用。</summary>
        public Dictionary<string, string> Personas { get; set; } = new();
    }

    /// <summary>
    /// 一条据点事件。管"什么时候演、谁来演"；演什么由 <see cref="SceneId"/> 指向的场景决定。
    /// </summary>
    public sealed class EventEntry
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";

        /// <summary>触发时机：Season / Weather / Join / PersonalStory / WorldStory / Scheduled。</summary>
        public string Trigger { get; set; } = "";

        /// <summary>演出哪段场景（场景库 Id）。</summary>
        public string SceneId { get; set; } = "";

        /// <summary>能演这段的角色名；空 = 任意在场角色。</summary>
        public List<string> Characters { get; set; } = new();

        public GateEntry? Gate { get; set; }

        /// <summary>整局只发生一次。</summary>
        public bool Once { get; set; }

        /// <summary>同一天多事件同时到点时的先后，小的先演。</summary>
        public int Priority { get; set; }

        /// <summary>定时事件：不早于第几天。</summary>
        public int? Day { get; set; }

        /// <summary>定时事件：不早于当天的第几小时。</summary>
        public int? Hour { get; set; }
    }

    public sealed class CharacterEntry
    {
        public string Name { get; set; } = "";
        public List<LineEntry> Lines { get; set; } = new();
    }

    public sealed class LineEntry
    {
        public string Id { get; set; } = "";

        /// <summary>说话人。角色包内留空即用该角色名；世界包里留空表示旁白。</summary>
        public string Speaker { get; set; } = "";

        public VoiceKind Kind { get; set; } = VoiceKind.Speech;
        public VoiceTrigger Trigger { get; set; }
        public List<string> Lines { get; set; } = new();
        public int Weight { get; set; } = 1;

        /// <summary>优先级。默认 1；低于 1 视为关掉。</summary>
        public int Priority { get; set; } = 1;

        /// <summary>排他。为真时压掉所有更低优先级的候选。</summary>
        public bool Exclusive { get; set; }

        public string IllustrationId { get; set; } = "";
        public GateEntry? Gate { get; set; }

        // ---------- 场景维度 ----------

        /// <summary>只在说话人处于这些活动时出现。留空不限。</summary>
        public List<VoiceActivity> Activities { get; set; } = new();

        /// <summary>只在说话人扮演这些角色时出现。留空不限。</summary>
        public List<VoiceRole> Roles { get; set; } = new();

        /// <summary>只在动作的这些阶段出现。留空不限。</summary>
        public List<VoicePlace> Places { get; set; } = new();

        /// <summary>要求的情绪。留空或 Any 表示不限。</summary>
        public VoiceEmotion Emotion { get; set; } = VoiceEmotion.Any;

        /// <summary>说完是否切掉同动作的通用地文。</summary>
        public bool CutNarration { get; set; }

        /// <summary>本句由 LLM 现场生成。与 Lines 可以并存，Lines 当兜底。</summary>
        public GenerationEntry? Generation { get; set; }
    }

    /// <summary>LLM 生成的配置。</summary>
    public sealed class GenerationEntry
    {
        public string Instruction { get; set; } = "";
        public string Style { get; set; } = "";
        public int LineCount { get; set; } = 1;
        public int MaxCharsPerLine { get; set; }
        public bool UseMemory { get; set; } = true;
        public bool UseRecentDialogue { get; set; } = true;
        public int CacheMinutes { get; set; } = 30;
        public Dictionary<string, string> Options { get; set; } = new();
    }

    // ---------- 场景事件 ----------

    public sealed class SceneEntry
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Genre { get; set; } = "";

        /// <summary>基础发生率，千分率。1000 为必发。</summary>
        public int Rate { get; set; } = 1000;

        /// <summary>只对这些角色触发。留空表示任何角色。</summary>
        public List<string> Characters { get; set; } = new();

        public GateEntry? Gate { get; set; }

        /// <summary>状态机标志名。</summary>
        public string Flag { get; set; } = "";

        /// <summary>触发要求标志等于此值。</summary>
        public int? RequireFlagValue { get; set; }

        /// <summary>跑完后把标志设为此值。</summary>
        public int DoneValue { get; set; } = 1;

        /// <summary>两次触发之间至少隔几天。</summary>
        public int CooldownDays { get; set; }

        public List<EffectEntry> Effects { get; set; } = new();
        public List<StepEntry> Steps { get; set; } = new();
        public List<string> Summary { get; set; } = new();

        /// <summary>到点时现掷一名新角色当演员（访客这类"人从外面来"的事件）。</summary>
        public bool Spawn { get; set; }

        /// <summary>选了这一项就把现掷的演员送走。留空表示怎么选都留人。</summary>
        public string DismissChoice { get; set; } = "";
    }

    public sealed class StepEntry
    {
        public string Id { get; set; } = "";

        /// <summary>
        /// 步骤正文。每一项可以是：
        /// 字符串（说话人默认取本事件的主角角色，写成台词）；
        /// 或对象 { "kind": "Narration", "text": "..." } 写成旁白；
        /// 或对象 { "kind": "Speech", "speaker": "某人", "text": "..." }。
        /// </summary>
        public List<TextEntry> Lines { get; set; } = new();

        public List<ChoiceEntry> Choices { get; set; } = new();
        public List<EffectEntry> Effects { get; set; } = new();
        public GateEntry? Gate { get; set; }

        /// <summary>收束步：本步行完即收演，不顺流到下一步。</summary>
        public bool End { get; set; }
    }

    /// <summary>
    /// 一行文本。允许 JSON 里直接写字符串（简写）或写完整对象。
    /// 简写时由解析器按"角色台词"处理。
    /// </summary>
    [JsonConverter(typeof(TextEntryConverter))]
    public sealed class TextEntry
    {
        public VoiceKind Kind { get; set; } = VoiceKind.Speech;
        public string Speaker { get; set; } = "";
        public string Text { get; set; } = "";

        /// <summary>本行由 LLM 生成。给了它，text 可以留空（纯生成行）。</summary>
        public GenerationEntry? Generation { get; set; }
    }

    /// <summary>
    /// 把 <c>"一句话"</c> 与 <c>{ "kind": ..., "text": ... }</c> 两种写法
    /// 统一读成 <see cref="TextEntry"/>。作者写简单文本时不必套对象。
    /// </summary>
    public sealed class TextEntryConverter : JsonConverter<TextEntry>
    {
        public override TextEntry Read(ref Utf8JsonReader reader, System.Type type,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
                return new TextEntry { Text = reader.GetString() ?? "" };

            // 对象形式：手工读，避免递归调用本转换器。
            var entry = new TextEntry();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;
                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                var name = reader.GetString();
                reader.Read();
                switch (name?.ToLowerInvariant())
                {
                    case "kind":
                        if (System.Enum.TryParse<VoiceKind>(reader.GetString(), ignoreCase: true, out var kind))
                            entry.Kind = kind;
                        break;
                    case "speaker":
                        entry.Speaker = reader.GetString() ?? "";
                        break;
                    case "text":
                        entry.Text = reader.GetString() ?? "";
                        break;
                    case "generation":
                        entry.Generation = JsonSerializer.Deserialize<GenerationEntry>(ref reader, options);
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }
            return entry;
        }

        public override void Write(Utf8JsonWriter writer, TextEntry value,
            JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", value.Kind.ToString());
            writer.WriteString("speaker", value.Speaker);
            writer.WriteString("text", value.Text);
            if (value.Generation != null)
            {
                writer.WritePropertyName("generation");
                JsonSerializer.Serialize(writer, value.Generation, options);
            }
            writer.WriteEndObject();
        }
    }

    public sealed class ChoiceEntry
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public int GotoStep { get; set; } = -1;
        public List<EffectEntry> Effects { get; set; } = new();
        public GateEntry? Gate { get; set; }
    }

    public sealed class EffectEntry
    {
        public SceneEffectKind Kind { get; set; }

        /// <summary>数值：好感/心情/体力等的增量，或标志/计数器的目标值。</summary>
        public int Amount { get; set; }

        public string ItemId { get; set; } = "";
        public string Flag { get; set; } = "";
        public string Trait { get; set; } = "";
        public string Relation { get; set; } = "";

        /// <summary>要写进日志的一行。</summary>
        public string Log { get; set; } = "";
    }

    /// <summary>
    /// 门槛。字段与 VoiceGate 一一对应，全部可选。
    ///
    /// 可空的枚举（好感档、季节、天气）刻意收成字符串再手工解析：
    /// JsonStringEnumConverter 只认非空枚举，直接写 Bond? 会在读档时炸。
    /// </summary>
    public sealed class GateEntry
    {
        public int? FavorMin { get; set; }
        public int? FavorMax { get; set; }
        public string? BondMin { get; set; }
        public string? BondMax { get; set; }
        public List<RelationFlag> RequireRelations { get; set; } = new();
        public List<RelationFlag> ForbidRelations { get; set; } = new();
        public int? DaysSinceTalkMin { get; set; }
        public int? MoodMin { get; set; }
        public int? MoodMax { get; set; }
        public bool? Soaked { get; set; }
        public int? LevelMin { get; set; }
        public bool? AllMembersMaxLevel { get; set; }
        public bool? ReturnedFromCombat { get; set; }
        public List<string> RoomTags { get; set; } = new();
        public List<Trait> RequireTraits { get; set; } = new();
        public List<Trait> ForbidTraits { get; set; } = new();
        public int? HourMin { get; set; }
        public int? HourMax { get; set; }
        public string? Season { get; set; }
        public string? Weather { get; set; }
        public int? DayMin { get; set; }
        public int? DayMax { get; set; }
        public string GiftItemId { get; set; } = "";
        public int? Chance { get; set; }
        public bool Once { get; set; }
        public int CooldownMinutes { get; set; }
        public List<string> RequireSaid { get; set; } = new();
        public List<string> ForbidSaid { get; set; } = new();

        // ---------- 场景 ----------

        public List<VoiceActivity> Activities { get; set; } = new();
        public List<VoiceActivity> ForbidActivities { get; set; } = new();
        public List<VoiceRole> Roles { get; set; } = new();
        public List<VoicePlace> Places { get; set; } = new();
        public List<VoiceEmotion> Emotions { get; set; } = new();
        public bool? SameRoomAsPlayer { get; set; }
        public bool? AtFacility { get; set; }
        public List<int> RoomIds { get; set; } = new();
    }

    /// <summary>
    /// 解析成一张名字 → 台词库的表，外加一包世界通用台词、场景事件表与角色设定。
    /// 返回 false 表示 JSON 读不动，调用方应保持无台词状态（整条链路旁路）。
    /// </summary>
    public static bool TryParse(
        string json,
        out Dictionary<string, VoicePack> characters,
        out VoicePack world,
        out string error)
        => TryParse(json, out characters, out world, out _, out _, out _, out error);

    /// <summary>
    /// 完整版解析。除台词库外还给出场景事件表与角色设定。
    /// </summary>
    public static bool TryParse(
        string json,
        out Dictionary<string, VoicePack> characters,
        out VoicePack world,
        out List<SceneEvent> scenes,
        out Dictionary<string, string> personas,
        out string error)
        => TryParse(json, out characters, out world, out scenes, out _, out personas, out error);

    /// <summary>
    /// 完整版解析。除台词库、场景表与角色设定外，还给出据点事件表。
    /// </summary>
    public static bool TryParse(
        string json,
        out Dictionary<string, VoicePack> characters,
        out VoicePack world,
        out List<SceneEvent> scenes,
        out List<Hub.HubEventDef> events,
        out Dictionary<string, string> personas,
        out string error)
    {
        characters = new Dictionary<string, VoicePack>();
        world = new VoicePack();
        scenes = new List<SceneEvent>();
        events = new List<Hub.HubEventDef>();
        personas = new Dictionary<string, string>();
        error = "";
        LastError = "";

        File? file;
        try
        {
            file = JsonSerializer.Deserialize<File>(json, Options);
        }
        catch (JsonException e)
        {
            error = e.Message;
            return false;
        }
        if (file == null)
        {
            error = "内容为空";
            return false;
        }

        foreach (var entry in file.Characters)
        {
            if (entry.Name.Length == 0)
                continue;
            var pack = new VoicePack();
            foreach (var line in entry.Lines)
                pack.Register(ToLine(line, entry.Name));
            characters[entry.Name] = pack;
        }

        foreach (var line in file.World)
            world.Register(ToLine(line, ""));

        foreach (var entry in file.Scenes)
        {
            var scene = ToScene(entry);
            if (scene != null)
                scenes.Add(scene);
        }

        foreach (var entry in file.Events)
        {
            var def = ToEvent(entry);
            if (def != null)
                events.Add(def);
        }

        foreach (var pair in file.Personas)
            personas[pair.Key] = pair.Value;

        return true;
    }

    /// <summary>把一条事件读成运行时对象。Id 或场景 Id 为空的丢弃。</summary>
    private static Hub.HubEventDef? ToEvent(EventEntry entry)
    {
        if (entry.Id.Length == 0 || entry.SceneId.Length == 0)
            return null;

        var trigger = Hub.HubEventTrigger.WorldStory;
        if (!System.Enum.TryParse<Hub.HubEventTrigger>(entry.Trigger, ignoreCase: true, out trigger))
        {
            // 认不出的时机名不静默变成别的时机，直接丢掉并在 LastError 留痕。
            LastError = $"事件 {entry.Id} 的触发时机 {entry.Trigger} 不认识。";
            return null;
        }

        return new Hub.HubEventDef
        {
            Id = entry.Id,
            Title = entry.Title,
            Trigger = trigger,
            SceneId = entry.SceneId,
            Characters = entry.Characters,
            Gate = entry.Gate == null ? null : ToGate(entry.Gate),
            Once = entry.Once,
            Priority = entry.Priority,
            Day = entry.Day,
            Hour = entry.Hour,
        };
    }

    /// <summary>角色包内没写说话人就补角色名；世界包留空即旁白。</summary>
    private static VoiceLine ToLine(LineEntry entry, string defaultSpeaker) => new()
    {
        Id = entry.Id,
        Speaker = entry.Speaker.Length > 0 ? entry.Speaker : defaultSpeaker,
        Kind = entry.Kind,
        Trigger = entry.Trigger,
        Lines = entry.Lines,
        Weight = entry.Weight,
        Priority = entry.Priority,
        Exclusive = entry.Exclusive,
        IllustrationId = entry.IllustrationId,
        Gate = ToGate(entry.Gate),
        Activities = entry.Activities,
        Roles = entry.Roles,
        Places = entry.Places,
        Emotion = entry.Emotion,
        CutNarration = entry.CutNarration,
        Generation = ToGeneration(entry.Generation),
    };

    private static VoiceGeneration? ToGeneration(GenerationEntry? entry)
    {
        if (entry == null)
            return null;
        return new VoiceGeneration
        {
            Instruction = entry.Instruction,
            Style = entry.Style,
            LineCount = entry.LineCount,
            MaxCharsPerLine = entry.MaxCharsPerLine,
            UseMemory = entry.UseMemory,
            UseRecentDialogue = entry.UseRecentDialogue,
            CacheMinutes = entry.CacheMinutes,
            Options = entry.Options,
        };
    }

    /// <summary>把一条场景事件读成运行时对象。步骤为空的事件会被丢弃。</summary>
    private static SceneEvent? ToScene(SceneEntry entry)
    {
        if (entry.Id.Length == 0 || entry.Steps.Count == 0)
            return null;

        var steps = new List<SceneStep>();
        foreach (var step in entry.Steps)
        {
            var lines = new List<SceneText>();
            foreach (var text in step.Lines)
            {
                var generation = ToGeneration(text.Generation);
                // 没有静态正文又没有生成槽的空行才丢弃；纯生成行（text 空、有 generation）要留。
                if (text.Text.Length == 0 && generation == null)
                    continue;
                // 步骤里的简写文本默认按"本事件主角在说话"处理；
                // 写旁白请在 JSON 里显式给 kind。
                var speaker = text.Speaker;
                if (speaker.Length == 0 && text.Kind == VoiceKind.Speech)
                    speaker = entry.Characters.Count > 0 ? entry.Characters[0] : "";
                lines.Add(new SceneText(text.Kind, speaker, text.Text, generation));
            }

            var choices = new List<SceneChoice>();
            foreach (var choice in step.Choices)
            {
                choices.Add(new SceneChoice
                {
                    Id = choice.Id.Length > 0 ? choice.Id : $"choice{choices.Count}",
                    Label = choice.Label,
                    GotoStep = choice.GotoStep,
                    Effects = ToEffects(choice.Effects),
                    Gate = choice.Gate == null ? null : ToGate(choice.Gate),
                });
            }

            steps.Add(new SceneStep
            {
                Id = step.Id,
                Lines = lines,
                Choices = choices,
                Effects = ToEffects(step.Effects),
                Gate = step.Gate == null ? null : ToGate(step.Gate),
                End = step.End,
            });
        }

        return new SceneEvent
        {
            Id = entry.Id,
            Title = entry.Title,
            Genre = entry.Genre,
            Rate = entry.Rate,
            Characters = entry.Characters,
            Gate = entry.Gate == null ? null : ToGate(entry.Gate),
            Flag = entry.Flag,
            RequireFlagValue = entry.RequireFlagValue,
            DoneValue = entry.DoneValue,
            CooldownDays = entry.CooldownDays,
            Spawn = entry.Spawn,
            DismissChoice = entry.DismissChoice,
            Effects = ToEffects(entry.Effects),
            Steps = steps,
            Summary = entry.Summary,
        };
    }

    private static List<SceneEffect> ToEffects(List<EffectEntry> entries)
    {
        var list = new List<SceneEffect>();
        foreach (var entry in entries)
        {
            if (entry.Kind == SceneEffectKind.None)
                continue;
            list.Add(new SceneEffect
            {
                Kind = entry.Kind,
                Amount = entry.Amount,
                ItemId = entry.ItemId,
                Flag = entry.Flag,
                Trait = entry.Trait,
                Relation = entry.Relation,
                Log = entry.Log,
            });
        }
        return list;
    }

    private static VoiceGate ToGate(GateEntry? entry)
    {
        if (entry == null)
            return new VoiceGate();
        return new VoiceGate
        {
            FavorMin = entry.FavorMin,
            FavorMax = entry.FavorMax,
            BondMin = ParseEnum<Bond>(entry.BondMin),
            BondMax = ParseEnum<Bond>(entry.BondMax),
            RequireRelations = entry.RequireRelations,
            ForbidRelations = entry.ForbidRelations,
            DaysSinceTalkMin = entry.DaysSinceTalkMin,
            MoodMin = entry.MoodMin,
            MoodMax = entry.MoodMax,
            Soaked = entry.Soaked,
            LevelMin = entry.LevelMin,
            AllMembersMaxLevel = entry.AllMembersMaxLevel,
            ReturnedFromCombat = entry.ReturnedFromCombat,
            RoomTags = entry.RoomTags,
            RequireTraits = entry.RequireTraits,
            ForbidTraits = entry.ForbidTraits,
            HourMin = entry.HourMin,
            HourMax = entry.HourMax,
            Season = ParseEnum<Season>(entry.Season),
            Weather = ParseEnum<Weather>(entry.Weather),
            DayMin = entry.DayMin,
            DayMax = entry.DayMax,
            GiftItemId = entry.GiftItemId,
            Chance = entry.Chance,
            Once = entry.Once,
            CooldownMinutes = entry.CooldownMinutes,
            RequireSaid = entry.RequireSaid,
            ForbidSaid = entry.ForbidSaid,
            Activities = entry.Activities,
            ForbidActivities = entry.ForbidActivities,
            Roles = entry.Roles,
            Places = entry.Places,
            Emotions = entry.Emotions,
            SameRoomAsPlayer = entry.SameRoomAsPlayer,
            AtFacility = entry.AtFacility,
            RoomIds = entry.RoomIds,
        };
    }

    /// <summary>
    /// 名字转枚举，大小写不敏感。名字不认识时返回 null 并记一条错，
    /// 免得内容里打错字却静默变成“无门槛”。
    /// </summary>
    private static T? ParseEnum<T>(string? name) where T : struct, System.Enum
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        if (System.Enum.TryParse<T>(name, ignoreCase: true, out var value))
            return value;
        LastError = $"认不出的 {typeof(T).Name}：{name}";
        return null;
    }

    /// <summary>最近一次解析里遇到的、不致命但需要提醒的问题。</summary>
    public static string LastError { get; private set; } = "";
}
