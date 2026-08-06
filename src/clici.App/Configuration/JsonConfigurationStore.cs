using System.Text.Json;
using Clici.Core.Configuration;

namespace Clici.App.Configuration;

internal sealed class JsonConfigurationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public JsonConfigurationStore()
    {
        DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "clici");
        FilePath = Path.Combine(DirectoryPath, "config.json");
    }

    public string DirectoryPath { get; }

    public string FilePath { get; }

    public ConfigurationLoadResult Load()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);

            if (!File.Exists(FilePath))
            {
                var defaults = new CliciConfiguration();
                return new ConfigurationLoadResult(
                    defaults,
                    !TrySave(defaults),
                    null,
                    true);
            }

            var json = File.ReadAllText(FilePath);
            var candidate = JsonSerializer.Deserialize<CliciConfiguration>(
                json,
                SerializerOptions);
            var validation = ConfigurationValidator.Validate(candidate);

            return new ConfigurationLoadResult(
                validation.Configuration,
                validation.UsedFallback,
                null,
                true);
        }
        catch (Exception exception)
        {
            return new ConfigurationLoadResult(
                new CliciConfiguration(),
                true,
                exception.GetType().Name,
                false);
        }
    }

    public bool TrySave(CliciConfiguration configuration)
    {
        var temporaryPath = $"{FilePath}.tmp";

        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var validated = ConfigurationValidator.Validate(configuration).Configuration;
            var json = JsonSerializer.Serialize(validated, SerializerOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, FilePath, true);
            return true;
        }
        catch
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch
            {
                // Configuration failures must never terminate the tray application.
            }

            return false;
        }
    }
}

internal sealed record ConfigurationLoadResult(
    CliciConfiguration Configuration,
    bool UsedFallback,
    string? ExceptionType,
    bool PersistenceAllowed);
