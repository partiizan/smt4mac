namespace Smt.Core;

/// <summary>Resolve the current user's EVE logs without a username or installation-specific connector.</summary>
public static class LogFolderLocator
{
    public static string Resolve(string? folderOverride = null, string? home = null, string? documents = null)
    {
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(folderOverride))
            return folderOverride.StartsWith("~/") ? Path.Combine(home, folderOverride[2..]) : folderOverride;
        documents ??= Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var roots = new[] { Path.Combine(home, "Documents"), documents }
            .Where(p => !string.IsNullOrWhiteSpace(p)).Distinct();
        foreach (var root in roots)
            foreach (var leaf in new[] { "chatlogs", "Chatlogs", "ChatLogs" })
            {
                var candidate = Path.Combine(root, "EVE", "logs", leaf);
                if (Directory.Exists(candidate)) return candidate;
            }
        // Return the expected path even before EVE creates it; the polling loop keeps retrying.
        return Path.Combine(home, "Documents", "EVE", "logs", "chatlogs");
    }
}
