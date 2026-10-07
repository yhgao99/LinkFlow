using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace LinkFlow.Models;

public class BrowserProfileItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Args { get; set; }
    public string? IconColor { get; set; }
}

public class BrowserItem : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string? _tag;
    private string _exePath = string.Empty;
    private string? _commandArgs;
    private int _sortOrder;
    private string? _customHotkey;
    private string _hotkeyLabel = string.Empty;
    private bool _isDragging;
    private bool _isDropTargetTop;
    private bool _isDropTargetBottom;

    public string Id { get; set; } = string.Empty;

    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayNameWithTag)); } }
    }

    public string? OriginalName { get; set; }

    public string? Tag
    {
        get => _tag;
        set { if (_tag != value) { _tag = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasTag)); OnPropertyChanged(nameof(DisplayNameWithTag)); } }
    }

    public string ExePath
    {
        get => _exePath;
        set { if (_exePath != value) { _exePath = value; OnPropertyChanged(); } }
    }

    public string? CommandArgs
    {
        get => _commandArgs;
        set { if (_commandArgs != value) { _commandArgs = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCustomArgs)); } }
    }

    public string? PrivacyArgs { get; set; }
    public string? IconPath { get; set; }

    public int SortOrder
    {
        get => _sortOrder;
        set { if (_sortOrder != value) { _sortOrder = value; OnPropertyChanged(); } }
    }

    private bool _isDisabled;
    public bool IsDisabled
    {
        get => _isDisabled;
        set
        {
            if (_isDisabled != value)
            {
                _isDisabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsEnabled));
                OnPropertyChanged(nameof(StatusOpacity));
                OnPropertyChanged(nameof(StatusDescription));
            }
        }
    }

    [JsonIgnore]
    public bool IsEnabled
    {
        get => !IsDisabled;
        set => IsDisabled = !value;
    }

    [JsonIgnore]
    public double StatusOpacity => IsDisabled ? 0.45 : 1.0;

    [JsonIgnore]
    public string StatusDescription => IsDisabled ? "已停用" : "正常";

    private bool _isSelected;
    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    public string? CustomHotkey
    {
        get => _customHotkey;
        set { if (_customHotkey != value) { _customHotkey = value; OnPropertyChanged(); } }
    }

    public List<BrowserProfileItem> Profiles { get; set; } = [];

    // Runtime state (not saved to JSON)
    [JsonIgnore]
    public ImageSource? Icon { get; set; }

    [JsonIgnore]
    public bool IsRunning { get; set; }

    [JsonIgnore]
    public string HotkeyLabel
    {
        get => _hotkeyLabel;
        set { if (_hotkeyLabel != value) { _hotkeyLabel = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasHotkey)); } }
    }

    [JsonIgnore]
    public bool HasTag => !string.IsNullOrWhiteSpace(Tag);

    [JsonIgnore]
    public string DisplayNameWithTag => HasTag ? $"{Name} [{Tag}]" : Name;

    [JsonIgnore]
    public string EffectiveOriginalName => !string.IsNullOrWhiteSpace(OriginalName) ? OriginalName : Name;

    [JsonIgnore]
    public bool HasProfiles => Profiles.Count > 0;

    [JsonIgnore]
    public bool HasPrivacyMode => !string.IsNullOrWhiteSpace(PrivacyArgs);

    [JsonIgnore]
    public bool HasCustomArgs => !string.IsNullOrWhiteSpace(CommandArgs);

    [JsonIgnore]
    public bool HasHotkey => !string.IsNullOrWhiteSpace(HotkeyLabel);

    // Drag-Drop Animation & Visual State
    [JsonIgnore]
    public bool IsDragging
    {
        get => _isDragging;
        set { if (_isDragging != value) { _isDragging = value; OnPropertyChanged(); } }
    }

    [JsonIgnore]
    public bool IsDropTargetTop
    {
        get => _isDropTargetTop;
        set { if (_isDropTargetTop != value) { _isDropTargetTop = value; OnPropertyChanged(); } }
    }

    [JsonIgnore]
    public bool IsDropTargetBottom
    {
        get => _isDropTargetBottom;
        set { if (_isDropTargetBottom != value) { _isDropTargetBottom = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public static class BrowserExtensions
{
    public static BrowserItem? FindBrowser(this IEnumerable<BrowserItem> browsers, string? targetIdOrName)
    {
        if (string.IsNullOrWhiteSpace(targetIdOrName)) return null;

        var list = browsers as IList<BrowserItem> ?? browsers.ToList();
        return list.FirstOrDefault(b => string.Equals(b.Id, targetIdOrName, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(b => string.Equals(b.Name, targetIdOrName, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(b => !string.IsNullOrEmpty(b.OriginalName) && string.Equals(b.OriginalName, targetIdOrName, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(b => string.Equals(b.ExePath, targetIdOrName, StringComparison.OrdinalIgnoreCase));
    }
}
