using System.IO;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;

namespace Frosty;

public enum SnowPreset
{
    Cozy,
    Classic,
    Heavy,
    Cyber
}

public static class SnowPresets
{
    private readonly record struct Values(int Count, float Speed, float Wind, float Height);

    private static Values Get(SnowPreset p) => p switch
    {
        SnowPreset.Cozy => new Values(450, 0.7f, 0.4f, 26f),
        SnowPreset.Heavy => new Values(1400, 1.1f, 1.1f, 42f),
        _ => new Values(900, 0.9f, 0.8f, 34f)
    };

    public static void Apply(AppSettings s, SnowPreset preset)
    {
        Values v = Get(preset);
        s.ParticleCount = v.Count;
        s.SpeedMultiplier = v.Speed;
        s.WindMultiplier = v.Wind;
        s.MaxSnowHeight = v.Height;
        s.IsBlizzardMode = false;

        if (preset == SnowPreset.Cyber)
            s.ColorTheme = SnowColorTheme.CyberpunkNeon;
        else if (s.ColorTheme == SnowColorTheme.CyberpunkNeon)
            s.ColorTheme = SnowColorTheme.ClassicWhite;
    }

    public static SnowPreset? Detect(AppSettings s)
    {
        if (s.IsBlizzardMode) return null;

        bool neon = s.ColorTheme == SnowColorTheme.CyberpunkNeon;
        foreach (SnowPreset p in Enum.GetValues<SnowPreset>())
        {
            if ((p == SnowPreset.Cyber) != neon) continue;
            Values v = Get(p);
            if (s.ParticleCount == v.Count &&
                Math.Abs(s.SpeedMultiplier - v.Speed) < 0.01f &&
                Math.Abs(s.WindMultiplier - v.Wind) < 0.01f &&
                Math.Abs(s.MaxSnowHeight - v.Height) < 0.5f)
            {
                return p;
            }
        }
        return null;
    }
}

public sealed class AppSettings
{
    private const string RegistryRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegistryName = "Frosty";

    public bool IsFirstRunCompleted { get; set; } = false;
    public bool IsEnglishLanguage { get; set; } = false;
    public bool EnableAutoUpdate { get; set; } = true;

    public int ParticleCount { get; set; } = 900;
    public float SizeMultiplier { get; set; } = 1.0f;
    public float SpeedMultiplier { get; set; } = 0.9f;
    public float WindMultiplier { get; set; } = 0.8f;
    public float WindChangeIntervalSeconds { get; set; } = 5.0f;
    public float SwayAmplitudeMultiplier { get; set; } = 1.0f;
    public float MaxSnowHeight { get; set; } = 34.0f;
    public float AccumulationRateMultiplier { get; set; } = 1.0f;
    public bool IsBlizzardMode { get; set; } = false;
    public SnowColorTheme ColorTheme { get; set; } = SnowColorTheme.ClassicWhite;

    public bool EnableSnowballCrafting { get; set; } = true;
    public bool EnableCursorScatter { get; set; } = true;
    public float CursorScatterPower { get; set; } = 0.5f;
    public float ObjectGravityMultiplier { get; set; } = 1.0f;
    public float SnowballToughnessMultiplier { get; set; } = 1.0f;
    public bool EnableWindowOcclusion { get; set; } = true;
    public bool KeepSnowOnHiddenWindows { get; set; } = true;
    public bool AccumulateOnTaskbar { get; set; } = true;
    public bool PauseInFullscreenGames { get; set; } = true;

    public bool EnableLivingSnowmen { get; set; } = true;
    public bool EnableWindowShakeShed { get; set; } = true;
    public float WindowShakeThreshold { get; set; } = 1.0f;
    public bool EnableScreenSplatters { get; set; } = true;

    public bool EnablePowerSaving { get; set; } = true;
    public float PowerSaveTimeoutSeconds { get; set; } = 60.0f;
    public bool EnableGlobalHotkeys { get; set; } = true;

    public bool EnableUiAnimations { get; set; } = true;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public void CopyFrom(AppSettings other)
    {
        foreach (PropertyInfo p in typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.CanRead && p.CanWrite)
            {
                p.SetValue(this, p.GetValue(other));
            }
        }
    }

    private static string GetSettingsFilePath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dir = Path.Combine(appData, "Frosty");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    public static AppSettings Load()
    {
        string path = GetSettingsFilePath();
        if (!File.Exists(path))
            return new AppSettings();

        try
        {
            string json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json);
            if (loaded == null) return new AppSettings();
            loaded.ClampValues();
            return loaded;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            ClampValues();
            string path = GetSettingsFilePath();
            string tempPath = path + ".tmp";
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
        }
    }

    public void ClampValues()
    {
        ParticleCount = Math.Clamp(ParticleCount, 100, SnowSimulationEngine.MaxParticlesPool);
        SizeMultiplier = Math.Clamp(SizeMultiplier, 0.5f, 2.5f);
        SpeedMultiplier = Math.Clamp(SpeedMultiplier, 0.3f, 3.0f);
        WindMultiplier = Math.Clamp(WindMultiplier, 0.0f, 4.0f);
        WindChangeIntervalSeconds = Math.Clamp(WindChangeIntervalSeconds, 1.0f, 15.0f);
        SwayAmplitudeMultiplier = Math.Clamp(SwayAmplitudeMultiplier, 0.0f, 2.5f);
        MaxSnowHeight = Math.Clamp(MaxSnowHeight, 10.0f, 95.0f);
        AccumulationRateMultiplier = Math.Clamp(AccumulationRateMultiplier, 0.2f, 3.0f);
        CursorScatterPower = Math.Clamp(CursorScatterPower, 0.5f, 2.5f);
        ObjectGravityMultiplier = Math.Clamp(ObjectGravityMultiplier, 0.5f, 2.0f);
        SnowballToughnessMultiplier = Math.Clamp(SnowballToughnessMultiplier, 0.4f, 3.0f);
        WindowShakeThreshold = Math.Clamp(WindowShakeThreshold, 0.3f, 3.0f);
        PowerSaveTimeoutSeconds = Math.Clamp(PowerSaveTimeoutSeconds, 15.0f, 180.0f);
    }

    public void ApplyToEngine(SnowSimulationEngine engine)
    {
        ClampValues();
        engine.SetActiveParticleCount(ParticleCount);
        engine.SetSizeMultiplier(SizeMultiplier);
        engine.SetSpeedMultiplier(SpeedMultiplier);
        engine.SetWindMultiplier(WindMultiplier);
        engine.SetWindChangeInterval(WindChangeIntervalSeconds);
        engine.SetSwayAmplitudeMultiplier(SwayAmplitudeMultiplier);
        engine.SetMaxSnowHeight(MaxSnowHeight);
        engine.SetAccumulationRateMultiplier(AccumulationRateMultiplier);
        engine.SetBlizzardMode(IsBlizzardMode);
        engine.SetColorTheme(ColorTheme);
        engine.SetSnowballCrafting(EnableSnowballCrafting);
        engine.SetCursorScatter(EnableCursorScatter);
        engine.SetCursorScatterPower(CursorScatterPower);
        engine.SetObjectGravityMultiplier(ObjectGravityMultiplier);
        engine.SetSnowballToughnessMultiplier(SnowballToughnessMultiplier);
        engine.SetWindowOcclusion(EnableWindowOcclusion);
        engine.SetKeepSnowOnHiddenWindows(KeepSnowOnHiddenWindows);
        engine.SetAccumulateOnTaskbar(AccumulateOnTaskbar);
        engine.SetLivingSnowmen(EnableLivingSnowmen);
        engine.SetWindowShakeShed(EnableWindowShakeShed);
        engine.SetWindowShakeThreshold(WindowShakeThreshold);
        engine.SetScreenSplatters(EnableScreenSplatters);
        engine.SetPowerSaving(EnablePowerSaving);
        engine.SetPowerSaveTimeoutSeconds(PowerSaveTimeoutSeconds);
    }

    public static bool IsAutoStartEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryRunKeyPath, writable: false);
            return key?.GetValue(AppRegistryName) is string val && !string.IsNullOrWhiteSpace(val);
        }
        catch
        {
            return false;
        }
    }

    public static void SetAutoStart(bool enable)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryRunKeyPath, writable: true);
            if (key == null) return;

            if (enable)
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    key.SetValue(AppRegistryName, $"\"{exePath}\"");
                }
            }
            else
            {
                key.DeleteValue(AppRegistryName, throwOnMissingValue: false);
            }
        }
        catch
        {
        }
    }
}