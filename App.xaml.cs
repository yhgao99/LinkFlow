using System;
using System.IO;
using System.Windows;
using LinkFlow.Services;
using LinkFlow.Views;

namespace LinkFlow;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            NativeMethods.SetCurrentProcessExplicitAppUserModelID("LinkFlow.App.1.0");

            var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinkFlow");
            Directory.CreateDirectory(appDataDir);
            var localIco = Path.Combine(appDataDir, "app_icon.ico");
            if (!File.Exists(localIco))
            {
                var resourceStream = Application.GetResourceStream(new Uri("pack://application:,,,/app_icon.ico"));
                if (resourceStream != null)
                {
                    using var fs = File.Create(localIco);
                    resourceStream.Stream.CopyTo(fs);
                }
            }
        }
        catch { }

        try
        {
            // 1. Load config and runtime state
            ConfigService.Instance.Load();

            // Apply user configured theme
            ApplyTheme(ConfigService.Instance.Current.Theme);

            // 2. Parse arguments
            var targetArg = ExtractUrlFromArgs(e.Args);
            if (string.IsNullOrEmpty(targetArg))
            {
                if (!ConfigService.Instance.Current.HasCompletedOnboarding && !SystemRegistration.IsDefaultBrowser())
                {
                    var onboarding = new OnboardingWindow();
                    onboarding.Show();
                    return;
                }

                var settings = new SettingsWindow();
                settings.Show();
                return;
            }

            if (targetArg is "-onboarding")
            {
                var onboarding = new OnboardingWindow();
                onboarding.Show();
                return;
            }

            if (targetArg is "-settings")
            {
                var settings = new SettingsWindow();
                settings.Show();
                return;
            }

            var rawUrl = targetArg;
            var config = ConfigService.Instance.Current;

            // 3. Fast-path: Quick resolution for short URLs (up to 800ms)
            if (config.ResolveShortUrls && UrlResolver.IsShortUrl(rawUrl, config.UrlShorteners))
            {
                try
                {
                    using var cts = new System.Threading.CancellationTokenSource(800);
                    var resolved = UrlResolver.ResolveRedirectAsync(rawUrl, config.UrlShorteners, cts.Token).GetAwaiter().GetResult();
                    if (!string.IsNullOrEmpty(resolved) && !string.Equals(resolved, rawUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        rawUrl = resolved;
                    }
                }
                catch { }
            }

            // 4. Fast-path: Check if an automated rule matches
            var match = RuleMatcher.Match(rawUrl, config);
            if (match != null)
            {
                BrowserLauncher.Launch(match.Browser, rawUrl, match.ProfileId, false);
                Shutdown(0);
                return;
            }

            // 5. Fallback Browser: Check if user enabled auto-fallback when no rules match
            if (config.EnableFallbackBrowser && !config.AlwaysPrompt && !string.IsNullOrWhiteSpace(config.FallbackBrowserId))
            {
                var fallbackBrowser = config.Browsers.FirstOrDefault(b => b.Id == config.FallbackBrowserId);
                if (fallbackBrowser != null && !fallbackBrowser.IsDisabled && File.Exists(fallbackBrowser.ExePath))
                {
                    BrowserLauncher.Launch(fallbackBrowser, rawUrl, null, false);
                    Shutdown(0);
                    return;
                }
            }

            // 6. Show Fluent Picker UI
            var picker = new PickerWindow(rawUrl);
            picker.Show();
        }
        catch (Exception ex)
        {
            try
            {
                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinkFlow", "crash.log");
                File.WriteAllText(logPath, ex.ToString());
            }
            catch { }

            MessageBox.Show($"启动失败: {ex}", "LinkFlow 错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static string? ExtractUrlFromArgs(string[] args)
    {
        if (args == null || args.Length == 0) return null;

        var first = args[0].Trim();
        if (first is "-onboarding" or "--onboarding" or "/onboarding") return "-onboarding";
        if (first is "-settings" or "--settings" or "/settings") return "-settings";

        int startIndex = 0;
        if (first is "--" or "-url" or "--url" or "/url")
        {
            startIndex = 1;
            if (startIndex >= args.Length) return null;
        }

        if (startIndex == args.Length - 1)
        {
            var raw = args[startIndex].Trim().Trim('\"', '\'');
            return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }

        var joined = string.Join(" ", args.Skip(startIndex)).Trim().Trim('\"', '\'');
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }

    public static void ApplyTheme(string? theme)
    {
        try
        {
            switch (theme?.ToLowerInvariant())
            {
                case "light":
                    Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
                    break;
                case "dark":
                    Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
                    break;
                default:
                    Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();
                    break;
            }
        }
        catch { }
    }
}
