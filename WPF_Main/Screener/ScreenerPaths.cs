using System.IO;

namespace AlgoTrading.Screener.Storage;

/// <summary>
/// Central place for all file-system locations the screener uses.
/// User-writable data lives under %APPDATA%\AlgoTrading\Screener.
/// Read-only shipped resources live next to the executable in \Resources.
/// </summary>
public static class ScreenerPaths
{
    // ---------- user data (%APPDATA%) ----------
    public static string UserDataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AlgoTrading",
        "Screener");

    public static string SavedQueriesFile => Path.Combine(UserDataFolder, "saved-queries.json");
    public static string SettingsFile     => Path.Combine(UserDataFolder, "settings.json");
    public static string BuildPlansFolder => Path.Combine(UserDataFolder, "BuildPlans");

    // ---------- shipped resources (next to the .exe) ----------
    public static string AppResourcesFolder => Path.Combine(AppContext.BaseDirectory, "Resources");
    public static string TemplatesFile      => Path.Combine(AppResourcesFolder, "screener-templates.json");
    public static string HelpFile           => Path.Combine(AppResourcesFolder, "screener-help.md");

    /// <summary>Creates the user data folder if it doesn't exist. Safe to call repeatedly.</summary>
    public static void EnsureUserDataFolder()
    {
        Directory.CreateDirectory(UserDataFolder);
    }
}
