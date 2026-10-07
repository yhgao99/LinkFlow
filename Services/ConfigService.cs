using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LinkFlow.Models;

namespace LinkFlow.Services;

public class ConfigService
{
    private static readonly Lazy<ConfigService> _instance = new(() => new ConfigService());
    public static ConfigService Instance => _instance.Value;

    private readonly string _configDirectory;
    private readonly string _configFilePath;
    private readonly string _legacyFluentPickerFilePath;
    private readonly string _legacyBrowserPickerFilePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private DateTime _lastFileWriteTimeUtc = DateTime.MinValue;

    public AppConfig Current { get; private set; } = new();

    public ConfigService()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _configDirectory = Path.Combine(localAppData, "LinkFlow");
        _configFilePath = Path.Combine(_configDirectory, "settings.json");
        _legacyFluentPickerFilePath = Path.Combine(localAppData, "FluentPicker", "settings.json");
        _legacyBrowserPickerFilePath = Path.Combine(localAppData, "BrowserPicker", "settings.json");
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = File.ReadAllText(_configFilePath);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions);
                if (loaded != null)
                {
                    Current = loaded;
                }
                _lastFileWriteTimeUtc = File.GetLastWriteTimeUtc(_configFilePath);
            }
            else if (File.Exists(_legacyFluentPickerFilePath))
            {
                var json = File.ReadAllText(_legacyFluentPickerFilePath);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions);
                if (loaded != null)
                {
                    Current = loaded;
                    Save();
                }
            }
            else if (File.Exists(_legacyBrowserPickerFilePath))
            {
                ImportFromLegacy();
            }
            else
            {
                InitializeFromSystem();
            }

            // Ensure non-null collections
            Current.Browsers ??= [];
            Current.Rules ??= [];
            Current.UrlShorteners ??= [];

            // If browser list is completely empty, auto-populate from system
            if (Current.Browsers.Count == 0)
            {
                var systemBrowsers = BrowserDetector.DetectInstalledBrowsers();
                foreach (var sb in systemBrowsers)
                {
                    sb.SortOrder = Current.Browsers.Count;
                    Current.Browsers.Add(sb);
                }
                SanitizeAndDeduplicate(Current.Browsers);
                Save();
            }

            RefreshRuntimeState();
            BrowserPolicyService.SyncPolicies(Current.Browsers, Current.SuppressDefaultBrowserPrompts);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConfigService ERROR] Failed to load config: {ex}");
            InitializeFromSystem();
            SanitizeAndDeduplicate(Current.Browsers);
            RefreshRuntimeState();
            Save();
        }
    }

    public static void SanitizeAndDeduplicate(List<BrowserItem> browsers)
    {
        // 0. 强力清除无效路径、非 exe、或属于系统工具/安全卫士等误扫的非浏览器垃圾项
        browsers.RemoveAll(b =>
        {
            if (string.IsNullOrWhiteSpace(b.ExePath) || !File.Exists(b.ExePath))
                return true;
            return !BrowserDetector.IsValidBrowserExecutable(b.ExePath, b.Name);
        });

        // 1. Remove bare Doubao AI assistant if Doubao browser entry exists
        bool hasDoubaoBrowser = browsers.Any(b => b.Name.Contains("豆包") && b.CommandArgs?.Contains("browser") == true);
        if (hasDoubaoBrowser)
        {
            browsers.RemoveAll(b => b.Name == "豆包" && (string.IsNullOrWhiteSpace(b.CommandArgs) || !b.CommandArgs.Contains("browser")));
        }

        // 2. Remove duplicate English Quark if Chinese 夸克 exists
        bool hasChineseQuark = browsers.Any(b => b.Name == "夸克");
        if (hasChineseQuark)
        {
            browsers.RemoveAll(b => b.Name == "Quark" && b.ExePath.Contains("quark", StringComparison.OrdinalIgnoreCase));
        }

        // 3. Disambiguate Microsoft Edge Beta
        foreach (var b in browsers)
        {
            if (b.ExePath.Contains("Edge Beta", StringComparison.OrdinalIgnoreCase))
            {
                b.Name = "Microsoft Edge Beta";
                if (b.Id == "Microsoft Edge")
                {
                    b.Id = "Microsoft Edge Beta";
                }
            }
        }

        // 4. Strict deduplication by normalized ExePath + CommandArgs
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = browsers.Count - 1; i >= 0; i--)
        {
            var key = $"{browsers[i].ExePath}::{browsers[i].CommandArgs?.Trim()}";
            if (!seen.Add(key))
            {
                browsers.RemoveAt(i);
            }
        }

        // 5. Re-index sort orders
        for (int i = 0; i < browsers.Count; i++)
        {
            browsers[i].SortOrder = i;
        }
    }

    private static readonly string[] DefaultHotkeys = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P"];

    public void RefreshRuntimeState()
    {
        // Sort active browsers
        var activeBrowsers = Current.Browsers
            .Where(b => !b.IsDisabled && File.Exists(b.ExePath))
            .OrderBy(b => b.SortOrder)
            .ToList();

        var index = 0;
        foreach (var b in activeBrowsers)
        {
            if (!string.IsNullOrWhiteSpace(b.CustomHotkey))
            {
                b.HotkeyLabel = b.CustomHotkey;
            }
            else if (index < DefaultHotkeys.Length)
            {
                b.HotkeyLabel = DefaultHotkeys[index];
            }
            else
            {
                b.HotkeyLabel = string.Empty;
            }

            b.Icon = IconExtractor.GetIcon(b.IconPath ?? b.ExePath);
            b.IsRunning = ProcessMonitor.IsRunning(b.ExePath);
            index++;
        }

        // Refresh and resolve rules runtime metadata
        for (int i = 0; i < Current.Rules.Count; i++)
        {
            var rule = Current.Rules[i];
            rule.PriorityIndex = i + 1;

            var target = Current.Browsers.FindBrowser(rule.TargetBrowserId);
            if (target != null)
            {
                rule.TargetBrowserName = target.Name;
                rule.TargetBrowserTag = target.Tag;
                rule.TargetBrowserIcon = target.Icon ?? IconExtractor.GetIcon(target.IconPath ?? target.ExePath);
                rule.TargetBrowserId = target.Id;

                if (!string.IsNullOrWhiteSpace(rule.TargetProfileId))
                {
                    var p = target.Profiles.FirstOrDefault(pr => pr.Id == rule.TargetProfileId);
                    rule.TargetProfileName = p != null ? p.Name : rule.TargetProfileId;
                }
                else
                {
                    rule.TargetProfileName = null;
                }
            }
            else
            {
                rule.TargetBrowserName = "未找到对应浏览器";
                rule.TargetBrowserTag = null;
                rule.TargetBrowserIcon = null;
                rule.TargetProfileName = null;
            }

            rule.OnPropertyChanged(nameof(rule.PriorityDisplay));
            rule.OnPropertyChanged(nameof(rule.PriorityIndex));
            rule.OnPropertyChanged(nameof(rule.TargetBrowserName));
            rule.OnPropertyChanged(nameof(rule.TargetBrowserTag));
            rule.OnPropertyChanged(nameof(rule.HasTargetBrowserTag));
            rule.OnPropertyChanged(nameof(rule.TargetBrowserIcon));
            rule.OnPropertyChanged(nameof(rule.TargetProfileName));
            rule.OnPropertyChanged(nameof(rule.HasTargetProfile));
            rule.OnPropertyChanged(nameof(rule.StatusOpacity));
            rule.OnPropertyChanged(nameof(rule.StatusDescription));
            rule.OnPropertyChanged(nameof(rule.IsEnabled));
        }
    }

    public void Save()
    {
        try
        {
            if (!Directory.Exists(_configDirectory))
            {
                Directory.CreateDirectory(_configDirectory);
            }

            var json = JsonSerializer.Serialize(Current, _jsonOptions);
            var tempPath = Path.Combine(_configDirectory, $"settings.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _configFilePath, overwrite: true);

            if (File.Exists(_configFilePath))
            {
                _lastFileWriteTimeUtc = File.GetLastWriteTimeUtc(_configFilePath);
            }

            BrowserPolicyService.SyncPolicies(Current.Browsers, Current.SuppressDefaultBrowserPrompts);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConfigService ERROR] Failed to save config: {ex}");
        }
    }

    public bool ReloadIfModified(bool force = false)
    {
        try
        {
            if (!File.Exists(_configFilePath)) return false;
            var currentWrite = File.GetLastWriteTimeUtc(_configFilePath);
            if (force || currentWrite > _lastFileWriteTimeUtc)
            {
                Load();
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConfigService] Reload check failed: {ex.Message}");
        }
        return false;
    }

    public void Reload()
    {
        Load();
    }

    private void InitializeFromSystem()
    {
        Current = new AppConfig
        {
            Browsers = BrowserDetector.DetectInstalledBrowsers(),
            AlwaysPrompt = false,
            RememberChoiceByDefault = false,
            AutoCloseOnLostFocus = true,
            ResolveShortUrls = true
        };
    }

    private void ImportFromLegacy()
    {
        try
        {
            var json = File.ReadAllText(_legacyBrowserPickerFilePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Current = new AppConfig();

            if (root.TryGetProperty("AlwaysPrompt", out var ap))
                Current.AlwaysPrompt = ap.GetBoolean();
            if (root.TryGetProperty("AutoCloseOnFocusLost", out var ac))
                Current.AutoCloseOnLostFocus = ac.GetBoolean();

            if (root.TryGetProperty("BrowserList", out var browserList) && browserList.ValueKind == JsonValueKind.Array)
            {
                int order = 0;
                foreach (var bElem in browserList.EnumerateArray())
                {
                    var id = bElem.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                    var name = bElem.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                    var iconPath = bElem.TryGetProperty("IconPath", out var iconProp) ? iconProp.GetString() : null;
                    var command = bElem.TryGetProperty("Command", out var cmdProp) ? cmdProp.GetString() : null;
                    var privacyArgs = bElem.TryGetProperty("PrivacyArgs", out var privProp) ? privProp.GetString() : null;
                    var disabled = bElem.TryGetProperty("Disabled", out var disProp) && disProp.GetBoolean();

                    var exePath = CleanPath(command) ?? CleanPath(iconPath);
                    if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                    {
                        continue;
                    }

                    var fileName = Path.GetFileName(exePath).ToLowerInvariant();
                    if (fileName is "browserpicker.exe" or "fluentpicker.exe" or "linkflow.exe" or "iexplore.exe")
                    {
                        continue;
                    }

                    var item = new BrowserItem
                    {
                        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
                        Name = name ?? Path.GetFileNameWithoutExtension(exePath),
                        ExePath = exePath,
                        IconPath = iconPath ?? exePath,
                        PrivacyArgs = privacyArgs?.Trim(),
                        IsDisabled = disabled,
                        SortOrder = order++
                    };

                    if (bElem.TryGetProperty("Profiles", out var profiles) && profiles.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var pElem in profiles.EnumerateArray())
                        {
                            var pId = pElem.TryGetProperty("Id", out var pid) ? pid.GetString() : null;
                            var pName = pElem.TryGetProperty("Name", out var pname) ? pname.GetString() : null;
                            var pArgs = pElem.TryGetProperty("CommandArgs", out var pargs) ? pargs.GetString() : null;

                            if (!string.IsNullOrWhiteSpace(pName))
                            {
                                item.Profiles.Add(new BrowserProfileItem
                                {
                                    Id = pId ?? pName,
                                    Name = pName,
                                    Args = pArgs
                                });
                            }
                        }
                    }

                    Current.Browsers.Add(item);
                }
            }

            // Also merge any browsers detected from the system that weren't in the legacy list
            var systemBrowsers = BrowserDetector.DetectInstalledBrowsers();
            foreach (var sb in systemBrowsers)
            {
                if (!Current.Browsers.Any(b =>
                    string.Equals(b.ExePath, sb.ExePath, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(b.CommandArgs ?? "", sb.CommandArgs ?? "", StringComparison.OrdinalIgnoreCase)))
                {
                    sb.SortOrder = Current.Browsers.Count;
                    Current.Browsers.Add(sb);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to import legacy settings: {ex.Message}");
            InitializeFromSystem();
        }
    }

    private static string? CleanPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("\""))
        {
            var next = trimmed.IndexOf('\"', 1);
            if (next > 1)
            {
                return trimmed.Substring(1, next - 1);
            }
        }
        return trimmed.Trim('\"', '\'');
    }
}
