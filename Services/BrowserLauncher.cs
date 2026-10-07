using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using LinkFlow.Models;

namespace LinkFlow.Services;

public static class BrowserLauncher
{
    public static bool Launch(BrowserItem browser, string url, string? profileId = null, bool isPrivacyMode = false)
    {
        try
        {
            var parsed = UrlResolver.Parse(url);
            var targetUrl = parsed.NormalizedUrl;

            var sb = new StringBuilder();

            // 1. Browser custom command args (e.g. --user-data-dir=...)
            if (!string.IsNullOrWhiteSpace(browser.CommandArgs))
            {
                sb.Append(browser.CommandArgs.Trim()).Append(' ');
            }

            // 2. Profile args
            if (!string.IsNullOrWhiteSpace(profileId))
            {
                var profile = browser.Profiles.FirstOrDefault(p => p.Id == profileId);
                if (profile != null && !string.IsNullOrWhiteSpace(profile.Args))
                {
                    sb.Append(profile.Args.Trim()).Append(' ');
                }
            }

            // 3. Privacy args
            if (isPrivacyMode && !string.IsNullOrWhiteSpace(browser.PrivacyArgs))
            {
                sb.Append(browser.PrivacyArgs.Trim()).Append(' ');
            }

            // 4. Suppress default browser checks for Chromium-based browsers
            if (ConfigService.Instance.Current.SuppressDefaultBrowserPrompts)
            {
                var exeName = Path.GetFileName(browser.ExePath).ToLowerInvariant();
                if (!exeName.Contains("firefox"))
                {
                    var currentStr = sb.ToString();
                    if (!currentStr.Contains("--no-default-browser-check"))
                    {
                        sb.Append("--no-default-browser-check ");
                    }
                    if (!currentStr.Contains("DefaultBrowserPromptRefresh"))
                    {
                        sb.Append("--disable-features=DefaultBrowserPromptRefresh ");
                    }
                    if (!currentStr.Contains("--no-first-run"))
                    {
                        sb.Append("--no-first-run ");
                    }
                }
            }

            // 3. Append URL safely encoded
            var safeUrl = targetUrl.Replace("\"", "%22");
            sb.Append('"').Append(safeUrl).Append('"');

            var args = sb.ToString().Trim();

            var psi = new ProcessStartInfo
            {
                FileName = browser.ExePath,
                Arguments = args,
                UseShellExecute = true
            };

            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error launching browser: {ex.Message}");
            try
            {
                // Fallback: ShellExecute
                NativeMethods.ShellExecute(IntPtr.Zero, "open", browser.ExePath, $"\"{url}\"", "", 1);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public static void SaveRuleForDomain(string domain, BrowserItem browser, string? profileId)
    {
        if (string.IsNullOrWhiteSpace(domain) || domain == "about:blank")
            return;

        // Ensure latest config is loaded from disk
        ConfigService.Instance.Reload();
        var config = ConfigService.Instance.Current;

        // Remove any existing domain rule for this exact domain to avoid duplicates
        config.Rules.RemoveAll(r => r.MatchType == RuleMatchType.Domain && r.Pattern.Equals(domain, StringComparison.OrdinalIgnoreCase));

        config.Rules.Insert(0, new RoutingRule
        {
            Pattern = domain,
            MatchType = RuleMatchType.Domain,
            TargetBrowserId = browser.Id,
            TargetProfileId = profileId,
            IsEnabled = true
        });

        ConfigService.Instance.RefreshRuntimeState();
        ConfigService.Instance.Save();
    }
}
