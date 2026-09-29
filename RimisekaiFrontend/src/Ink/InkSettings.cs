using System;
using System.Text.Json;
using Godot;

namespace Rimisekai.Ink;

public enum WindowModeOption
{
    Windowed = 0,
    Borderless = 1,
    ExclusiveFullscreen = 2,
}

public sealed class SettingsData
{
    public int WindowMode { get; set; } = (int)WindowModeOption.Windowed;
    public float MasterVolume { get; set; } = 1.0f;
    public bool VSync { get; set; } = true;
}

/// <summary>
/// 纯净设置持久化与应用。
/// 只提供窗口模式、主音量（Master Bus）、垂直同步，无设置文件时不自动修改。
/// </summary>
public static class InkSettings
{
    public const string SettingsFilePath = "user://settings.json";

    public static WindowModeOption CurrentWindowMode { get; private set; } = WindowModeOption.Windowed;
    public static float CurrentMasterVolume { get; private set; } = 1.0f;
    public static bool CurrentVSync { get; private set; } = true;

    private static bool _initialized;

    public static void EnsureLoaded()
    {
        if (_initialized)
            return;
        _initialized = true;

        ReadFromEngine();

        if (FileAccess.FileExists(SettingsFilePath))
        {
            try
            {
                using var file = FileAccess.Open(SettingsFilePath, FileAccess.ModeFlags.Read);
                if (file != null)
                {
                    var text = file.GetAsText();
                    var data = JsonSerializer.Deserialize<SettingsData>(text);
                    if (data != null)
                    {
                        ApplyWindowMode((WindowModeOption)Math.Clamp(data.WindowMode, 0, 2), persist: false);
                        ApplyMasterVolume(Math.Clamp(data.MasterVolume, 0f, 1f), persist: false);
                        ApplyVSync(data.VSync, persist: false);
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PushWarning($"Rimisekai: 加载设置文件失败：{ex.Message}");
            }
        }
    }

    public static void ReadFromEngine()
    {
        try
        {
            var mode = DisplayServer.WindowGetMode();
            CurrentWindowMode = mode switch
            {
                DisplayServer.WindowMode.ExclusiveFullscreen => WindowModeOption.ExclusiveFullscreen,
                DisplayServer.WindowMode.Fullscreen => WindowModeOption.Borderless,
                _ => WindowModeOption.Windowed,
            };

            var vsync = DisplayServer.WindowGetVsyncMode();
            CurrentVSync = vsync != DisplayServer.VSyncMode.Disabled;

            var busIdx = AudioServer.GetBusIndex("Master");
            if (busIdx >= 0)
            {
                if (AudioServer.IsBusMute(busIdx))
                {
                    CurrentMasterVolume = 0f;
                }
                else
                {
                    var db = AudioServer.GetBusVolumeDb(busIdx);
                    CurrentMasterVolume = Mathf.Clamp(Mathf.DbToLinear(db), 0f, 1f);
                }
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Rimisekai: 读取引擎当前设置异常：{ex.Message}");
        }
    }

    public static void ApplyWindowMode(WindowModeOption option, bool persist = true)
    {
        CurrentWindowMode = option;
        try
        {
            switch (option)
            {
                case WindowModeOption.Windowed:
                    DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                    DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
                    break;
                case WindowModeOption.Borderless:
                    DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                    DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
                    DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
                    break;
                case WindowModeOption.ExclusiveFullscreen:
                    DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
                    DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
                    break;
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Rimisekai: 应用窗口模式失败：{ex.Message}");
        }

        if (persist)
            Save();
    }

    public static void ApplyMasterVolume(float volume, bool persist = true)
    {
        CurrentMasterVolume = Mathf.Clamp(volume, 0f, 1f);
        try
        {
            var busIdx = AudioServer.GetBusIndex("Master");
            if (busIdx >= 0)
            {
                if (CurrentMasterVolume <= 0.001f)
                {
                    AudioServer.SetBusMute(busIdx, true);
                }
                else
                {
                    AudioServer.SetBusMute(busIdx, false);
                    AudioServer.SetBusVolumeDb(busIdx, Mathf.LinearToDb(CurrentMasterVolume));
                }
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Rimisekai: 应用主音量失败：{ex.Message}");
        }

        if (persist)
            Save();
    }

    public static void ApplyVSync(bool enabled, bool persist = true)
    {
        CurrentVSync = enabled;
        try
        {
            DisplayServer.WindowSetVsyncMode(enabled
                ? DisplayServer.VSyncMode.Enabled
                : DisplayServer.VSyncMode.Disabled);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Rimisekai: 应用垂直同步失败：{ex.Message}");
        }

        if (persist)
            Save();
    }

    public static void Save()
    {
        try
        {
            var data = new SettingsData
            {
                WindowMode = (int)CurrentWindowMode,
                MasterVolume = CurrentMasterVolume,
                VSync = CurrentVSync,
            };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            using var file = FileAccess.Open(SettingsFilePath, FileAccess.ModeFlags.Write);
            if (file != null)
                file.StoreString(json);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Rimisekai: 保存设置失败：{ex.Message}");
        }
    }
}
