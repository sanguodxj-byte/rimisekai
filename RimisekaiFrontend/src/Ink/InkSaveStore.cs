using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using Rimisekai.Flow;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Save;

namespace Rimisekai.Ink;

/// <summary>
/// 前端用于补全 Core SaveSystem 缺失 Facility 字段的扩展数据。
/// </summary>
public sealed class FacilityExtraData
{
    public int Id { get; set; }
    public int StorageCapacity { get; set; }
    public List<string> StorageFilter { get; set; } = new();
    public List<ActionKind> Actions { get; set; } = new();
    public bool DeclaredActions { get; set; }
    public bool IsTable { get; set; }
}

/// <summary>
/// 前端存档外层信封（Envelope）。
/// 包含标准 SaveData（含 HubSnapshot）、版本、时间戳及 Facility 前端扩展数据。
/// </summary>
public sealed class SaveEnvelope
{
    public int Version { get; set; } = 1;
    public string Timestamp { get; set; } = "";
    public string TerritoryName { get; set; } = "";
    public int Day { get; set; } = 1;
    public int Minutes { get; set; }
    public string Summary { get; set; } = "";
    public SaveData CoreData { get; set; } = new();
    public List<FacilityExtraData> FacilitiesExtra { get; set; } = new();
}

public sealed class SaveSlotInfo
{
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Timestamp { get; set; } = "";
    public string TerritoryName { get; set; } = "";
    public int Day { get; set; } = 1;
    public int Minutes { get; set; }
    public string Summary { get; set; } = "";
    public bool IsCorrupt { get; set; }
    public string ErrorMessage { get; set; } = "";
}

/// <summary>
/// 负责快照构建、落盘保存与读档恢复。
/// 默认保存目录为 user://saves/，可通过 OverrideDirectory 注入用于测试探针。
/// 每次保存采用全新唯一文件名，不覆盖旧文件，不自动存档。
/// </summary>
public static class InkSaveStore
{
    public const string DefaultSaveDir = "user://saves";

    public static string? OverrideDirectory { get; set; }

    public static string EffectiveSaveDir => OverrideDirectory ?? DefaultSaveDir;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static void EnsureDirectoryExists()
    {
        var dirPath = EffectiveSaveDir;
        if (!DirAccess.DirExistsAbsolute(dirPath))
        {
            DirAccess.MakeDirRecursiveAbsolute(dirPath);
        }
    }

    /// <summary>
    /// 从当前 GameState 和 HubSession 构建存档信封。
    /// </summary>
    public static SaveEnvelope CreateEnvelope(GameState state, HubSession? hub)
    {
        var coreData = SaveSystem.Capture(state, hub);
        var envelope = new SaveEnvelope
        {
            Version = 1,
            Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            TerritoryName = state.Territory.Name,
            Day = state.Clock.Day,
            Minutes = state.Clock.Minutes,
            Summary = $"第 {state.Clock.Day} 天 · {state.Territory.Name}",
            CoreData = coreData,
        };

        foreach (var f in state.Territory.Facilities)
        {
            envelope.FacilitiesExtra.Add(new FacilityExtraData
            {
                Id = f.Id,
                StorageCapacity = f.StorageCapacity,
                StorageFilter = new List<string>(f.StorageFilter),
                Actions = new List<ActionKind>(f.Actions),
                IsTable = f.IsTable,
            });
        }

        return envelope;
    }

    /// <summary>
    /// 将当前游戏状态存盘。生成唯一文件名保存，成功返回文件名，失败抛异常或返回 null。
    /// </summary>
    public static string? SaveNew(GameState state, HubSession? hub, out string? error)
    {
        error = null;
        try
        {
            EnsureDirectoryExists();
            var envelope = CreateEnvelope(state, hub);
            var json = JsonSerializer.Serialize(envelope, JsonOptions);

            var fileName = $"save_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}.json";
            var fullPath = $"{EffectiveSaveDir.TrimEnd('/')}/{fileName}";

            using var file = FileAccess.Open(fullPath, FileAccess.ModeFlags.Write);
            if (file == null)
            {
                error = $"无法写入文件：{FileAccess.GetOpenError()}";
                return null;
            }

            file.StoreString(json);
            return fileName;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// 列出所有存档文件元数据，按最后修改时间倒序排列。
    /// </summary>
    public static List<SaveSlotInfo> ListSaves()
    {
        var list = new List<SaveSlotInfo>();
        var dirPath = EffectiveSaveDir;
        if (!DirAccess.DirExistsAbsolute(dirPath))
            return list;

        using var dir = DirAccess.Open(dirPath);
        if (dir == null)
            return list;

        dir.ListDirBegin();
        var fileName = dir.GetNext();
        var files = new List<string>();
        while (!string.IsNullOrEmpty(fileName))
        {
            if (!dir.CurrentIsDir() && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                files.Add(fileName);
            }
            fileName = dir.GetNext();
        }
        dir.ListDirEnd();

        foreach (var file in files)
        {
            var fullPath = $"{dirPath.TrimEnd('/')}/{file}";
            var info = ReadSlotInfo(fullPath, file);
            list.Add(info);
        }

        // 按文件名或时间倒序
        list.Sort((a, b) => string.Compare(b.FileName, a.FileName, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    public static SaveSlotInfo ReadSlotInfo(string fullPath, string fileName)
    {
        var info = new SaveSlotInfo
        {
            FileName = fileName,
            FilePath = fullPath,
        };

        try
        {
            using var file = FileAccess.Open(fullPath, FileAccess.ModeFlags.Read);
            if (file == null)
            {
                info.IsCorrupt = true;
                info.ErrorMessage = $"无法读取文件（{FileAccess.GetOpenError()}）";
                return info;
            }

            var json = file.GetAsText();
            var envelope = JsonSerializer.Deserialize<SaveEnvelope>(json);
            if (envelope == null || envelope.CoreData == null)
            {
                info.IsCorrupt = true;
                info.ErrorMessage = "存档数据格式不正确";
                return info;
            }

            info.Timestamp = envelope.Timestamp;
            info.TerritoryName = envelope.TerritoryName;
            info.Day = envelope.Day;
            info.Minutes = envelope.Minutes;
            info.Summary = envelope.Summary;
            if (string.IsNullOrEmpty(info.Summary))
                info.Summary = $"第 {envelope.Day} 天 · {envelope.TerritoryName}";
        }
        catch (Exception ex)
        {
            info.IsCorrupt = true;
            info.ErrorMessage = ex.Message;
        }

        return info;
    }

    /// <summary>
    /// 从文件加载并还原 GameState 与 HubSession。
    /// 必须调用 InkWorldBootstrap.OpenHub 补齐 voice，再恢复 Facility 扩展数据和 Hub 快照。
    /// </summary>
    public static bool TryLoad(string fullPath, ContentPack pack, out GameState? state, out HubSession? hub, out string? error)
    {
        state = null;
        hub = null;
        error = null;

        try
        {
            using var file = FileAccess.Open(fullPath, FileAccess.ModeFlags.Read);
            if (file == null)
            {
                error = $"无法打开存档文件（{FileAccess.GetOpenError()}）";
                return false;
            }

            var json = file.GetAsText();
            var envelope = JsonSerializer.Deserialize<SaveEnvelope>(json);
            if (envelope == null || envelope.CoreData == null)
            {
                error = "存档数据损坏或为空";
                return false;
            }

            return RestoreFromEnvelope(envelope, pack, out state, out hub, out error);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool RestoreFromEnvelope(SaveEnvelope envelope, ContentPack pack, out GameState state, out HubSession hub, out string? error)
    {
        error = null;
        state = SaveSystem.Restore(envelope.CoreData);

        // 补齐 Core 未保存的 Facility 扩展字段
        if (envelope.FacilitiesExtra != null && envelope.FacilitiesExtra.Count > 0)
        {
            var extraMap = new Dictionary<int, FacilityExtraData>();
            foreach (var extra in envelope.FacilitiesExtra)
                extraMap[extra.Id] = extra;

            foreach (var facility in state.Territory.Facilities)
            {
                if (extraMap.TryGetValue(facility.Id, out var extra))
                {
                    facility.StorageCapacity = extra.StorageCapacity;
                    facility.StorageFilter.Clear();
                    foreach (var filterItem in extra.StorageFilter)
                        facility.StorageFilter.Add(filterItem);

                    facility.IsTable = extra.IsTable;
                    facility.Actions.Clear();
                    foreach (var action in extra.Actions)
                        facility.Actions.Add(action);
                }
            }
        }

        // 调用 OpenHub 补齐台词 (voice)
        hub = InkWorldBootstrap.OpenHub(state, pack);

        // 如果信封或 CoreData 中有 Hub 快照，使用它恢复准确的在场与位置
        var hubSnapshot = envelope.CoreData.Hub;
        if (hubSnapshot != null)
        {
            hub.Restore(hubSnapshot);
        }

        return true;
    }
}
