using System.Diagnostics;

namespace Frosty;

public static class AppConfig
{
    public const string CurrentVersion = "1.2.0";
    public static string GitHubUrl = "https://github.com/mls4ekui/Frosty";
    public static string TelegramUrl = "https://t.me/M1ELS1";
    public static string GitHubRepo = "mls4ekui/Frosty";
    public const string PreferredAssetName = "Frosty.exe";

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
        }
    }
}
