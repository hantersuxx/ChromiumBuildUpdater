using System;
using System.IO;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using HibbikiUpdater.Models;

namespace HibbikiUpdater.Services;

/// <summary>
/// Handles reading and writing user configuration to %LocalAppData%\HibbikiUpdater\config.json.
/// </summary>
public class ConfigService
{
    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HibbikiUpdater");

    private static readonly string ConfigFilePath = Path.Combine(ConfigDirectory, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppConfig Current { get; private set; }
    public string? LastError { get; private set; }

    public ConfigService()
    {
        Current = Load();
    }

    public AppConfig Load()
    {
        LastError = null;
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                string json = File.ReadAllText(ConfigFilePath);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (loaded != null)
                {
                    Current = loaded;
                    return loaded;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or SecurityException)
        {
            LastError = $"Settings could not be loaded; defaults are in use. {ex.Message}";
        }

        Current = new AppConfig();
        return Current;
    }

    public void Save()
    {
        LastError = null;
        string? tempFilePath = null;
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            string json = JsonSerializer.Serialize(Current, JsonOptions);
            tempFilePath = Path.Combine(ConfigDirectory, $"config-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tempFilePath, json);
            File.Move(tempFilePath, ConfigFilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            LastError = $"Settings could not be saved. {ex.Message}";
        }
        finally
        {
            if (tempFilePath is not null && File.Exists(tempFilePath))
            {
                try
                {
                    File.Delete(tempFilePath);
                }
                catch (IOException)
                {
                    LastError ??= "A temporary settings file could not be removed.";
                }
                catch (UnauthorizedAccessException)
                {
                    LastError ??= "A temporary settings file could not be removed.";
                }
            }
        }
    }
}
