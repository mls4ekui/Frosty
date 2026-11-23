using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Frosty;

public sealed record UpdateInfo(string TagVersion, string DownloadUrl, string AssetName, string HtmlUrl);

public static class AutoUpdater
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FrostyApp", AppConfig.CurrentVersion));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        if (string.IsNullOrWhiteSpace(AppConfig.GitHubRepo) || AppConfig.GitHubRepo.Contains("YOUR_USERNAME"))
            return null;

        try
        {
            string apiUrl = $"https://api.github.com/repos/{AppConfig.GitHubRepo}/releases/latest";
            using var response = await Http.GetAsync(apiUrl);
            if (!response.IsSuccessStatusCode) return null;

            string json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagProp)) return null;
            string tag = tagProp.GetString() ?? "";
            string cleanRemote = tag.TrimStart('v', 'V').Trim();

            if (!Version.TryParse(cleanRemote, out Version? remoteVer)) return null;
            if (!Version.TryParse(AppConfig.CurrentVersion.TrimStart('v', 'V'), out Version? localVer)) return null;

            if (remoteVer <= localVer) return null;

            string htmlUrl = root.TryGetProperty("html_url", out var htmlProp) ? (htmlProp.GetString() ?? AppConfig.GitHubUrl) : AppConfig.GitHubUrl;
            string downloadUrl = "";
            string assetName = "";

            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsProp.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                    string url = asset.TryGetProperty("browser_download_url", out var u) ? (u.GetString() ?? "") : "";
                    if (string.IsNullOrEmpty(url)) continue;

                    if (!string.IsNullOrEmpty(AppConfig.PreferredAssetName) &&
                        string.Equals(name, AppConfig.PreferredAssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = url;
                        assetName = name;
                        break;
                    }

                    if (string.IsNullOrEmpty(downloadUrl) &&
                        (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                         name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                    {
                        downloadUrl = url;
                        assetName = name;
                    }
                }
            }

            return new UpdateInfo(tag, downloadUrl, assetName, htmlUrl);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<bool> DownloadAndApplyUpdateAsync(UpdateInfo info)
    {
        if (string.IsNullOrWhiteSpace(info.DownloadUrl))
        {
            AppConfig.OpenUrl(info.HtmlUrl);
            return false;
        }

        try
        {
            string? currentExe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
            {
                AppConfig.OpenUrl(info.HtmlUrl);
                return false;
            }

            string appDir = Path.GetDirectoryName(currentExe)!;
            string exeName = Path.GetFileName(currentExe);
            string tempDir = Path.Combine(Path.GetTempPath(), "Frosty_Update_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);

            string downloadedFile = Path.Combine(tempDir, info.AssetName);
            using (var resp = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = new FileStream(downloadedFile, FileMode.Create, FileAccess.Write, FileShare.None);
                await resp.Content.CopyToAsync(fs);
            }

            string sourceFolderForCopy = tempDir;

            if (downloadedFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                string extractDir = Path.Combine(tempDir, "extracted");
                ZipFile.ExtractToDirectory(downloadedFile, extractDir, overwriteFiles: true);
                File.Delete(downloadedFile);
                sourceFolderForCopy = extractDir;
            }
            else if (downloadedFile.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                string targetExeInTemp = Path.Combine(tempDir, exeName);
                if (!string.Equals(downloadedFile, targetExeInTemp, StringComparison.OrdinalIgnoreCase))
                {
                    File.Move(downloadedFile, targetExeInTemp, overwrite: true);
                }
            }

            string batPath = Path.Combine(tempDir, "apply_update.bat");
            int pid = Environment.ProcessId;

            string copyStep = downloadedFile.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? $@"copy /Y ""{Path.Combine(tempDir, exeName)}"" ""{currentExe}"" >nul"
                : $@"xcopy /E /Y /I ""{sourceFolderForCopy}\*"" ""{appDir}\"" >nul";

            string batContent = $@"@echo off
chcp 65001 >nul
:waitloop
tasklist /FI ""PID eq {pid}"" 2>NUL | find /I ""{pid}"" >NUL
if ""%ERRORLEVEL%""==""0"" (
    timeout /t 1 /nobreak >nul
    goto waitloop
)
{copyStep}
start """" ""{currentExe}""
rmdir /S /Q ""{tempDir}""
";
            await File.WriteAllTextAsync(batPath, batContent);

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{batPath}\"\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                System.Windows.Application.Current.Shutdown();
            });

            return true;
        }
        catch
        {
            AppConfig.OpenUrl(info.HtmlUrl);
            return false;
        }
    }
}
