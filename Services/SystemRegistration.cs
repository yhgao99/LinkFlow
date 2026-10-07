using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace LinkFlow.Services;

public static class SystemRegistration
{
    private const string ProgId = "LinkFlowURL";
    private const string AppName = "LinkFlow";
    private const string AppKey = "LinkFlow";

    public static bool IsRegistered()
    {
        try
        {
            using var regApps = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications");
            return regApps?.GetValue(AppKey) != null;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsDefaultBrowser()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice");
            var progId = key?.GetValue("ProgId") as string;
            return string.Equals(progId, ProgId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(progId, "FluentPickerURL", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void OpenDefaultAppsSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:defaultapps")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore
        }
    }

    public static bool RegisterAsWebBrowser()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                return false;

            var clientPath = $@"Software\Clients\StartMenuInternet\{AppKey}";

            // 1. HKCU\Software\Clients\StartMenuInternet\FluentPicker
            using (var key = Registry.CurrentUser.CreateSubKey(clientPath))
            {
                key.SetValue(null, AppName);
                using (var iconKey = key.CreateSubKey("DefaultIcon"))
                {
                    iconKey.SetValue(null, $"\"{exePath}\",0");
                }
                using (var cmdKey = key.CreateSubKey(@"shell\open\command"))
                {
                    cmdKey.SetValue(null, $"\"{exePath}\" \"%1\"");
                }

                // Capabilities
                using var capKey = key.CreateSubKey("Capabilities");
                capKey.SetValue("ApplicationDescription", "现代 Windows 11 Fluent 风格的智能浏览器路由与分流工具");
                capKey.SetValue("ApplicationIcon", $"\"{exePath}\",0");
                capKey.SetValue("ApplicationName", AppName);

                using (var urlAssoc = capKey.CreateSubKey("URLAssociations"))
                {
                    urlAssoc.SetValue("http", ProgId);
                    urlAssoc.SetValue("https", ProgId);
                    urlAssoc.SetValue("ftp", ProgId);
                }

                using (var fileAssoc = capKey.CreateSubKey("FileAssociations"))
                {
                    fileAssoc.SetValue(".htm", ProgId);
                    fileAssoc.SetValue(".html", ProgId);
                    fileAssoc.SetValue(".xhtml", ProgId);
                }
            }

            // 2. HKCU\Software\RegisteredApplications
            using (var regApps = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            {
                regApps.SetValue(AppKey, $@"{clientPath}\Capabilities");
            }

            // 3. HKCU\Software\Classes\LinkFlowURL (Must include "URL Protocol" = "")
            using (var progKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
            {
                progKey.SetValue(null, "LinkFlow HTML Document / URL");
                progKey.SetValue("URL Protocol", "");
                progKey.SetValue("FriendlyTypeName", AppName);
                using (var iconKey = progKey.CreateSubKey("DefaultIcon"))
                {
                    iconKey.SetValue(null, $"\"{exePath}\",0");
                }
                using (var cmdKey = progKey.CreateSubKey(@"shell\open\command"))
                {
                    cmdKey.SetValue(null, $"\"{exePath}\" \"%1\"");
                }
            }

            // 4. OpenWithProgids hints for modern Windows 11 shell
            using (var httpOpenWith = Registry.CurrentUser.CreateSubKey(@"Software\Classes\http\OpenWithProgids"))
            {
                httpOpenWith.SetValue(ProgId, string.Empty);
            }
            using (var httpsOpenWith = Registry.CurrentUser.CreateSubKey(@"Software\Classes\https\OpenWithProgids"))
            {
                httpsOpenWith.SetValue(ProgId, string.Empty);
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to register as web browser: {ex.Message}");
            return false;
        }
    }

    public static bool UnregisterWebBrowser()
    {
        try
        {
            // 1. Delete HKCU\Software\RegisteredApplications value
            using (var regApps = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true))
            {
                regApps?.DeleteValue(AppKey, throwOnMissingValue: false);
                regApps?.DeleteValue("FluentPicker", throwOnMissingValue: false);
            }

            // 2. Delete HKCU\Software\Clients\StartMenuInternet\LinkFlow tree
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Clients\StartMenuInternet\{AppKey}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Clients\StartMenuInternet\FluentPicker", throwOnMissingSubKey: false);

            // 3. Delete HKCU\Software\Classes\LinkFlowURL tree
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\FluentPickerURL", throwOnMissingSubKey: false);

            // 4. Clean OpenWithProgids
            using (var httpOpenWith = Registry.CurrentUser.OpenSubKey(@"Software\Classes\http\OpenWithProgids", writable: true))
            {
                httpOpenWith?.DeleteValue(ProgId, throwOnMissingValue: false);
                httpOpenWith?.DeleteValue("FluentPickerURL", throwOnMissingValue: false);
            }
            using (var httpsOpenWith = Registry.CurrentUser.OpenSubKey(@"Software\Classes\https\OpenWithProgids", writable: true))
            {
                httpsOpenWith?.DeleteValue(ProgId, throwOnMissingValue: false);
                httpsOpenWith?.DeleteValue("FluentPickerURL", throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to unregister web browser: {ex.Message}");
            return false;
        }
    }
}
