using System;
using System.IO;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChromiumBuildUpdater.Core.Models;

namespace ChromiumBuildUpdater.Core.Services;

/// <summary>
/// Handles reading, migration, and atomic writing of per-user configuration.
/// </summary>
public class ConfigService
{
    private readonly string _configDirectory;
    private readonly string _configFilePath;
    private readonly string? _legacyConfigFilePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppConfig Current { get; private set; }
    public string? LastError { get; private set; }

    public ConfigService()
        : this(GetDefaultConfigDirectory(), GetLegacyConfigFilePath())
    {
    }

    public ConfigService(string configDirectory)
        : this(configDirectory, null)
    {
    }

    internal ConfigService(string configDirectory, string? legacyConfigFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        _configDirectory = configDirectory;
        _configFilePath = Path.Combine(configDirectory, "config.json");
        _legacyConfigFilePath = legacyConfigFilePath;
        Current = Load();
        if (!File.Exists(_configFilePath))
            Save();
    }

    public AppConfig Load()
    {
        LastError = null;
        try
        {
            string? sourcePath = File.Exists(_configFilePath)
                ? _configFilePath
                : _legacyConfigFilePath is not null && File.Exists(_legacyConfigFilePath)
                    ? _legacyConfigFilePath
                    : null;

            if (sourcePath is not null)
            {
                string json = File.ReadAllText(sourcePath);
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

    internal static string GetDefaultConfigDirectory()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return PlatformSupport.Current switch
        {
            HostPlatform.Windows => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ChromiumBuildUpdater"),
            HostPlatform.MacOS => Path.Combine(home, "Library", "Application Support", "ChromiumBuildUpdater"),
            HostPlatform.Linux => Path.Combine(
                GetLinuxConfigRoot(home),
                "ChromiumBuildUpdater"),
            _ => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ChromiumBuildUpdater")
        };
    }

    private static string? GetLegacyConfigFilePath()
    {
        if (PlatformSupport.Current != HostPlatform.Windows)
            return null;

        string legacyDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HibbikiUpdater");
        return Path.Combine(legacyDirectory, "config.json");
    }

    private static string GetLinuxConfigRoot(string home)
    {
        string? configuredRoot = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return !string.IsNullOrWhiteSpace(configuredRoot) && Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.Combine(home, ".config");
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
