using System.Text.Json;
using System.Text.Json.Serialization;
using VideoMergeTool.Core.Interfaces;
using VideoMergeTool.Core.Models;

namespace VideoMergeTool.Infrastructure;

public sealed class UserSettingsService : IUserSettingsService
{
    /// <summary>
    /// Enums are written as names so the file stays readable and survives reordering of an enum.
    /// The converter still reads the numeric form written by earlier versions.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _settingsFilePath;

    public UserSettingsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VideoMergeTool",
            "settings.json"))
    {
    }

    public UserSettingsService(string settingsFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        _settingsFilePath = settingsFilePath;
    }

    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return new UserSettings();
            }

            var json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<UserSettings>(json, SerializerOptions) ?? new UserSettings();
        }
        catch (Exception exception) when (exception is IOException or JsonException or NotSupportedException or UnauthorizedAccessException)
        {
            return new UserSettings();
        }
    }

    /// <summary>
    /// Best effort: this runs while the main window is closing, and an unwritable settings
    /// location must not turn into a crash on exit.
    /// </summary>
    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, SerializerOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or UnauthorizedAccessException)
        {
        }
    }
}
