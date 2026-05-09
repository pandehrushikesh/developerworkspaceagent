using System.Text.Json;

namespace ProjectLens.Infrastructure;

public static class ProjectLensSettingsLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static ProjectLensSettings Load(string settingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            return new ProjectLensSettings();
        }

        var json = File.ReadAllText(settingsPath);
        return JsonSerializer.Deserialize<ProjectLensSettings>(json, SerializerOptions)
            ?? new ProjectLensSettings();
    }
}
