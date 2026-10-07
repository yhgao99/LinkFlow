using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LinkFlow.Models;
using Microsoft.Win32;

namespace LinkFlow.Services;

public static class BrowserPolicyService
{
    private static readonly (string Keyword, string RegistryPath, string ValueName, object ValueData)[] PolicyDefinitions =
    [
        ("chrome", @"Software\Policies\Google\Chrome", "DefaultBrowserSettingEnabled", 0),
        ("edge", @"Software\Policies\Microsoft\Edge", "DefaultBrowserSettingEnabled", 0),
        ("msedge", @"Software\Policies\Microsoft\Edge", "DefaultBrowserSettingEnabled", 0),
        ("brave", @"Software\Policies\BraveSoftware\Brave", "DefaultBrowserSettingEnabled", 0),
        ("firefox", @"Software\Policies\Mozilla\Firefox", "DisableDefaultBrowserAgent", 1),
        ("chromium", @"Software\Policies\Chromium", "DefaultBrowserSettingEnabled", 0)
    ];

    public static void SyncPolicies(IEnumerable<BrowserItem> browsers, bool enable)
    {
        try
        {
            if (enable)
            {
                var exeNames = browsers
                    .Select(b => Path.GetFileNameWithoutExtension(b.ExePath)?.ToLowerInvariant() ?? "")
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToHashSet();

                foreach (var def in PolicyDefinitions)
                {
                    if (exeNames.Any(name => name.Contains(def.Keyword)))
                    {
                        ApplyPolicy(def.RegistryPath, def.ValueName, def.ValueData);
                    }
                }

                // If any non-Firefox browser is present, also apply generic Chromium policy
                if (exeNames.Any(n => !n.Contains("firefox")))
                {
                    ApplyPolicy(@"Software\Policies\Chromium", "DefaultBrowserSettingEnabled", 0);
                }
            }
            else
            {
                ClearAllPolicies();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to sync browser policies: {ex.Message}");
        }
    }

    private static void ApplyPolicy(string subKeyPath, string valueName, object valueData)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(subKeyPath, writable: true);
            if (key != null)
            {
                var kind = valueData is int ? RegistryValueKind.DWord : RegistryValueKind.String;
                key.SetValue(valueName, valueData, kind);
            }
        }
        catch { }
    }

    public static void ClearAllPolicies()
    {
        try
        {
            foreach (var def in PolicyDefinitions)
            {
                using var key = Registry.CurrentUser.OpenSubKey(def.RegistryPath, writable: true);
                key?.DeleteValue(def.ValueName, throwOnMissingValue: false);
            }

            using var chromKey = Registry.CurrentUser.OpenSubKey(@"Software\Policies\Chromium", writable: true);
            chromKey?.DeleteValue("DefaultBrowserSettingEnabled", throwOnMissingValue: false);
        }
        catch { }
    }
}
