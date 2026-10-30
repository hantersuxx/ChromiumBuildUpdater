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
    private readonly string _configDirectory;
    private readonly string _configFilePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppConfig Current { get; private set; }
    public string? LastError { get; private set; }

    public ConfigService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HibbikiUpdater"))
    {
    }

    public ConfigService(string configDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        _configDirectory = configDirectory;
        _configFilePath = Path.Combine(configDirectory, "config.json");
        Current = Load();
        if (!File.Exists(_configFilePath))
            Save();
    }

    public AppConfig Load()
    {
        LastError = null;
        try
        {
            if (File.Exists(_configFilePath))
            {
                string json = File.ReadAllText(_configFilePath);
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
            Directory.CreateDirectory(_configDirectory);
            string json = JsonSerializer.Serialize(Current, JsonOptions);
            tempFilePath = Path.Combine(_configDirectory, $"config-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tempFilePath, json);
            File.Move(tempFilePath, _configFilePath, overwrite: true);
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
