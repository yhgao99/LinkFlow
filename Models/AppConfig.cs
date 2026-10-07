using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace LinkFlow.Models;

public enum RuleMatchType
{
    Domain,     // 包含该主域名 (如 github.com)
    Hostname,   // 精确主机名 (如 api.github.com)
    Prefix,     // 网址前缀 (如 https://gitlab.com/org/)
    Regex,      // 正则表达式
    Contains    // 包含特定关键词
}

public class RoutingRule : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Pattern { get; set; } = string.Empty;
    public RuleMatchType MatchType { get; set; } = RuleMatchType.Domain;
    public string TargetBrowserId { get; set; } = string.Empty;
    public string? TargetProfileId { get; set; }

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                OnPropertyChanged(nameof(IsEnabled));
                OnPropertyChanged(nameof(StatusOpacity));
                OnPropertyChanged(nameof(StatusDescription));
            }
        }
    }

    [JsonIgnore]
    public int PriorityIndex { get; set; } = 1;

    [JsonIgnore]
    public string PriorityDisplay => $"#{PriorityIndex}";

    [JsonIgnore]
    public double StatusOpacity => IsEnabled ? 1.0 : 0.45;

    [JsonIgnore]
    public string StatusDescription => IsEnabled ? "已启用" : "已停用";

    [JsonIgnore]
    public string MatchTypeDisplay => MatchType switch
    {
        RuleMatchType.Domain => "🌐 域名及子域",
        RuleMatchType.Hostname => "🎯 精确主机",
        RuleMatchType.Prefix => "🔗 网址前缀",
        RuleMatchType.Regex => "⚡ 正则匹配",
        RuleMatchType.Contains => "🔤 包含关键词",
        _ => MatchType.ToString()
    };

    [JsonIgnore]
    public string TargetBrowserName { get; set; } = "选择浏览器";

    [JsonIgnore]
    public ImageSource? TargetBrowserIcon { get; set; }

    [JsonIgnore]
    public string? TargetBrowserTag { get; set; }

    [JsonIgnore]
    public bool HasTargetBrowserTag => !string.IsNullOrWhiteSpace(TargetBrowserTag);

    [JsonIgnore]
    public string? TargetProfileName { get; set; }

    [JsonIgnore]
    public bool HasTargetProfile => !string.IsNullOrWhiteSpace(TargetProfileName);

    public event PropertyChangedEventHandler? PropertyChanged;
    public void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class AppConfig
{
    public bool AlwaysPrompt { get; set; } = false;
    public bool RememberChoiceByDefault { get; set; } = false;
    public bool AutoCloseOnLostFocus { get; set; } = true;
    public bool ResolveShortUrls { get; set; } = true;
    public bool SuppressDefaultBrowserPrompts { get; set; } = true;
    public string Theme { get; set; } = "System"; // "System", "Dark", "Light"
    public bool HasCompletedOnboarding { get; set; } = false;
    public bool EnableFallbackBrowser { get; set; } = false;
    public string? FallbackBrowserId { get; set; }

    public List<string> UrlShorteners { get; set; } =
    [
        "t.co",
        "bit.ly",
        "aka.ms",
        "goo.gl",
        "tinyurl.com",
        "ow.ly",
        "is.gd",
        "safelinks.protection.outlook.com",
        "fwd.olsvc.com",
        "go.microsoft.com"
    ];

    public List<BrowserItem> Browsers { get; set; } = [];
    public List<RoutingRule> Rules { get; set; } = [];
}
