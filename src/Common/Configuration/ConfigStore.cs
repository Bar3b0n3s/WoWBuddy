using System.Text.Json;
using System.Text.Json.Serialization;

namespace WoWBuddy.Common.Configuration;

/// <summary>
/// Loads and saves JSON configuration files under a per-user application data folder.
/// </summary>
/// <remarks>
/// <para>
/// Settings are scoped per character (see <see cref="CharacterKey"/>) because the same
/// installation is routinely used for several accounts, and a level 80 raider and a level
/// 12 alt want completely different loot, vendor and combat thresholds.
/// </para>
/// <para>
/// Saves are written to a temporary file and then moved into place. The bot runs unattended
/// for hours and a crash mid-write would otherwise leave a truncated settings file that
/// fails to parse on next start.
/// </para>
/// </remarks>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _rootDirectory;

    /// <summary>
    /// Creates a store rooted at <paramref name="rootDirectory"/>, or at
    /// <c>%APPDATA%/WoWBuddy</c> when null.
    /// </summary>
    public ConfigStore(string? rootDirectory = null)
    {
        _rootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WoWBuddy");
    }

    /// <summary>Root directory that all configuration lives under.</summary>
    public string RootDirectory => _rootDirectory;

    /// <summary>Absolute path of the file backing <paramref name="name"/> for the given character.</summary>
    public string PathFor(string name, CharacterKey? character = null)
    {
        string directory = character is null
            ? _rootDirectory
            : Path.Combine(_rootDirectory, "characters", character.Value.ToFolderName());

        return Path.Combine(directory, name + ".json");
    }

    /// <summary>
    /// Reads <typeparamref name="T"/> from disk, returning a fresh default when the file
    /// is missing. A corrupt file is renamed aside rather than deleted, so the user can
    /// recover hand-edited settings, and a default is returned.
    /// </summary>
    public T Load<T>(string name, CharacterKey? character = null)
        where T : new()
    {
        string path = PathFor(name, character);
        if (!File.Exists(path))
        {
            return new T();
        }

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, SerializerOptions) ?? new T();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            QuarantineCorruptFile(path);
            return new T();
        }
    }

    /// <summary>Writes <paramref name="value"/> atomically.</summary>
    public void Save<T>(string name, T value, CharacterKey? character = null)
    {
        string path = PathFor(name, character);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        string json = JsonSerializer.Serialize(value, SerializerOptions);
        string temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

    private static void QuarantineCorruptFile(string path)
    {
        try
        {
            string backup = path + ".corrupt";
            File.Move(path, backup, overwrite: true);
        }
        catch (IOException)
        {
            // If we cannot even move it aside there is nothing useful left to do; the
            // caller still gets defaults and the bot stays usable.
        }
    }
}
