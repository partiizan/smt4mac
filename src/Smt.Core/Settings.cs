using System.Text.Json;
namespace Smt.Core;

public sealed record Settings(string Region = "Delve", string LogFolder = "", string ChannelFilter = "", bool HighSecOnly = false, double? AdmThreshold = null);
public static class SettingsStore
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SMT-Mac-Beta");
    public static string PathName => Path.Combine(Folder, "settings.json");
    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(PathName)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public static void Save(Settings settings)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(PathName + ".tmp", JsonSerializer.Serialize(settings));
        File.Move(PathName + ".tmp", PathName, true);
    }
}
