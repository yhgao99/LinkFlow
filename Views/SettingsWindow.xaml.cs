using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LinkFlow.Models;
using LinkFlow.Services;
using Microsoft.Win32;

namespace LinkFlow.Views;

public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow
{
    private class MatchTypeOption
    {
        public RuleMatchType Type { get; set; }
        public string Display { get; set; } = string.Empty;
        public override string ToString() => Display;
    }

    public class ProfileOption
    {
        public string? Id { get; set; }
        public string Display { get; set; } = string.Empty;
        public override string ToString() => Display;
    }

    private class ThemeOption
    {
        public string Key { get; set; } = string.Empty;
        public string Display { get; set; } = string.Empty;
        public override string ToString() => Display;
    }

    private BrowserItem? _lastTestBrowser;
    private string? _lastTestProfileId;
    private string _lastTestUrl = string.Empty;
    private RoutingRule? _editingRule;
    private bool _isLoadingPreferences;

    public SettingsWindow()
    {
        InitializeComponent();

        _autoScrollTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _autoScrollTimer.Tick += AutoScrollTimer_Tick;

        Loaded += SettingsWindow_Loaded;
        Activated += SettingsWindow_Activated;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeMethods.ApplyWindowStylesAndIcon(this);
    }

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        SyncLatestConfigFromDisk(force: true);
        InitRuleFormControls();
        UpdateNavSelection(MainTabControl?.SelectedIndex ?? 0);
    }

    private void SettingsWindow_Activated(object? sender, EventArgs e)
    {
        SyncLatestConfigFromDisk(force: false);
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && int.TryParse(fe.Tag?.ToString(), out int index))
        {
            if (MainTabControl != null)
            {
                MainTabControl.SelectedIndex = index;
            }
            UpdateNavSelection(index);
        }
    }

    private void UpdateNavSelection(int selectedIndex)
    {
        if (NavBtnBrowsers != null) NavBtnBrowsers.IsChecked = (selectedIndex == 0);
        if (NavBtnRules != null) NavBtnRules.IsChecked = (selectedIndex == 1);
        if (NavBtnPreferences != null) NavBtnPreferences.IsChecked = (selectedIndex == 2);
    }

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == MainTabControl)
        {
            UpdateNavSelection(MainTabControl.SelectedIndex);
            SyncLatestConfigFromDisk(force: false);
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            RefreshRules();
            e.Handled = true;
        }
    }

    private void RefreshRulesButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshRules();
    }

    private void RefreshRules()
    {
        SyncLatestConfigFromDisk(force: true);

        if (RulesCountBadge != null)
        {
            var count = ConfigService.Instance.Current.Rules.Count;
            RulesCountBadge.Text = count > 0 ? $"✅ 已刷新 ({count}条)" : "✅ 已刷新 (暂无规则)";
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (RulesCountBadge != null)
                {
                    var c = ConfigService.Instance.Current.Rules.Count;
                    RulesCountBadge.Text = c > 0 ? $"已配置 {c} 条规则" : "暂无规则";
                }
            };
            timer.Start();
        }
    }

    private void SyncLatestConfigFromDisk(bool force = false)
    {
        bool changed = force || ConfigService.Instance.ReloadIfModified();
        if (force && !changed)
        {
            ConfigService.Instance.Reload();
            changed = true;
        }

        if (changed)
        {
            LoadBrowsers();
            LoadRules();
            UpdateRuleTargetBrowserCombo();
            LoadPreferences();
            UpdateDefaultBrowserStatus();
        }
    }

    private void LoadBrowsers()
    {
        var config = ConfigService.Instance.Current;
        ConfigService.SanitizeAndDeduplicate(config.Browsers);

        _browsersObservable.Clear();
        foreach (var b in config.Browsers.OrderBy(b => b.SortOrder))
        {
            _browsersObservable.Add(b);
        }

        if (BrowsersListBox.ItemsSource != _browsersObservable)
        {
            BrowsersListBox.ItemsSource = _browsersObservable;
        }
    }

    private void LoadRules()
    {
        ConfigService.Instance.RefreshRuntimeState();
        var rules = ConfigService.Instance.Current.Rules;
        RulesListBox.ItemsSource = null;
        RulesListBox.ItemsSource = rules.ToList();

        if (EmptyRulesNotice != null)
        {
            EmptyRulesNotice.Visibility = rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        if (RulesCountBadge != null)
        {
            RulesCountBadge.Text = rules.Count > 0 ? $"已配置 {rules.Count} 条规则" : "暂无规则";
        }

        RefreshQuickTestPills();
        RunRuleTester();
    }

    private void LoadPreferences()
    {
        _isLoadingPreferences = true;
        try
        {
            var config = ConfigService.Instance.Current;
            RememberChoiceByDefaultCheck.IsChecked = config.RememberChoiceByDefault;
            AutoCloseOnLostFocusCheck.IsChecked = config.AutoCloseOnLostFocus;
            AlwaysPromptCheck.IsChecked = config.AlwaysPrompt;
            ResolveShortUrlsCheck.IsChecked = config.ResolveShortUrls;
            SuppressDefaultPromptsCheck.IsChecked = config.SuppressDefaultBrowserPrompts;

            EnableFallbackBrowserCheck.IsChecked = config.EnableFallbackBrowser;

            var activeBrowsers = config.Browsers.Where(b => !b.IsDisabled && File.Exists(b.ExePath)).ToList();
            FallbackBrowserCombo.ItemsSource = activeBrowsers;
            if (!string.IsNullOrEmpty(config.FallbackBrowserId))
            {
                FallbackBrowserCombo.SelectedItem = activeBrowsers.FirstOrDefault(b => b.Id == config.FallbackBrowserId);
            }
            if (FallbackBrowserCombo.SelectedItem == null && activeBrowsers.Count > 0)
            {
                FallbackBrowserCombo.SelectedIndex = 0;
            }

            var themeOptions = new List<ThemeOption>
            {
                new() { Key = "System", Display = "🌓 跟随系统主题" },
                new() { Key = "Dark", Display = "🌙 优雅深色模式" },
                new() { Key = "Light", Display = "☀️ 清新浅色模式" }
            };
            ThemeComboBox.ItemsSource = themeOptions;
            ThemeComboBox.SelectedItem = themeOptions.FirstOrDefault(t =>
                string.Equals(t.Key, config.Theme, StringComparison.OrdinalIgnoreCase)) ?? themeOptions[0];

            ShortenersTextBox.Text = config.UrlShorteners != null ? string.Join("; ", config.UrlShorteners) : "";
        }
        finally
        {
            _isLoadingPreferences = false;
        }
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingPreferences) return;

        if (ThemeComboBox.SelectedItem is ThemeOption opt)
        {
            App.ApplyTheme(opt.Key);
        }
    }

    private void InitRuleFormControls()
    {
        RuleMatchTypeCombo.ItemsSource = new List<MatchTypeOption>
        {
            new() { Type = RuleMatchType.Domain, Display = "🌐 域名及子域 (推荐，匹配主站及所有网页)" },
            new() { Type = RuleMatchType.Contains, Display = "🔤 包含关键词 (网址中含有该文本即生效)" },
            new() { Type = RuleMatchType.Prefix, Display = "🔗 网址完整前缀 (以该完整链接开头)" },
            new() { Type = RuleMatchType.Hostname, Display = "🎯 精确主机名 (严格等于主机名)" },
            new() { Type = RuleMatchType.Regex, Display = "⚡ 高级正则表达式 (面向技术人员)" }
        };
        RuleMatchTypeCombo.SelectedIndex = 0;

        UpdateRuleTargetBrowserCombo();
        UpdateRulePlaceholder();
        RunRuleTester();
    }

    private void UpdateRuleTargetBrowserCombo()
    {
        var activeBrowsers = ConfigService.Instance.Current.Browsers
            .Where(b => !b.IsDisabled && File.Exists(b.ExePath))
            .ToList();

        RuleTargetBrowserCombo.ItemsSource = activeBrowsers;
        if (activeBrowsers.Count > 0)
        {
            RuleTargetBrowserCombo.SelectedIndex = 0;
        }
        UpdateRuleTargetProfileCombo();
    }

    private void RuleTargetBrowserCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateRuleTargetProfileCombo();
    }

    private void UpdateRuleTargetProfileCombo()
    {
        if (RuleTargetProfileCombo == null) return;

        if (RuleTargetBrowserCombo.SelectedItem is BrowserItem browser && browser.Profiles.Count > 0)
        {
            var list = new List<ProfileOption>
            {
                new() { Id = null, Display = "默认主环境" }
            };
            foreach (var p in browser.Profiles)
            {
                list.Add(new ProfileOption { Id = p.Id, Display = $"👤 {p.Name}" });
            }
            RuleTargetProfileCombo.ItemsSource = list;
            RuleTargetProfileCombo.SelectedIndex = 0;
            RuleTargetProfileCombo.Visibility = Visibility.Visible;
        }
        else
        {
            RuleTargetProfileCombo.ItemsSource = new List<ProfileOption>
            {
                new() { Id = null, Display = "默认主环境" }
            };
            RuleTargetProfileCombo.SelectedIndex = 0;
            RuleTargetProfileCombo.Visibility = Visibility.Collapsed;
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths != null && paths.Length > 0)
            {
                int added = ImportPaths(paths);
                if (added > 0)
                {
                    ConfigService.Instance.RefreshRuntimeState();
                    LoadBrowsers();
                    UpdateRuleTargetBrowserCombo();
                }

                MessageBox.Show(
                    added > 0 ? $"已成功通过拖拽导入 {added} 个浏览器 / 快捷方式配置！" : "未发现新的浏览器配置（可能已存在于列表中）。",
                    "导入完成",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
        }
    }

    private void AddBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择浏览器程序或快捷方式 (.exe / .lnk)",
            Filter = "所有支持格式 (*.exe;*.lnk)|*.exe;*.lnk|快捷方式 (*.lnk)|*.lnk|可执行程序 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
        {
            int added = ImportPaths(dialog.FileNames);
            if (added > 0)
            {
                ConfigService.Instance.RefreshRuntimeState();
                LoadBrowsers();
                UpdateRuleTargetBrowserCombo();
            }

            MessageBox.Show(
                added > 0 ? $"已成功添加 {added} 个浏览器配置！" : "所选文件已存在于列表中或无法识别。",
                "添加完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
    }

    private void ImportFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择包含浏览器或快捷方式的文件夹",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            int added = ImportPaths([dialog.FolderName]);
            if (added > 0)
            {
                ConfigService.Instance.RefreshRuntimeState();
                LoadBrowsers();
                UpdateRuleTargetBrowserCombo();
            }

            MessageBox.Show(
                added > 0 ? $"已成功从该文件夹扫描并导入 {added} 个浏览器快捷方式配置！" : "该文件夹中未发现新的浏览器快捷方式（可能已存在列表中）。",
                "导入完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
    }

    private int ImportPaths(IEnumerable<string> paths)
    {
        var current = ConfigService.Instance.Current.Browsers;
        int added = 0;

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;

            if (Directory.Exists(path))
            {
                var folderItems = BrowserDetector.ScanFolderForBrowsers(path);
                foreach (var item in folderItems)
                {
                    if (!current.Any(b =>
                        string.Equals(b.ExePath, item.ExePath, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(b.CommandArgs ?? "", item.CommandArgs ?? "", StringComparison.OrdinalIgnoreCase)))
                    {
                        item.SortOrder = current.Count;
                        current.Add(item);
                        added++;
                    }
                }
            }
            else if (File.Exists(path))
            {
                var item = BrowserDetector.CreateItemFromFileOrShortcut(path);
                if (item != null)
                {
                    if (!current.Any(b =>
                        string.Equals(b.ExePath, item.ExePath, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(b.CommandArgs ?? "", item.CommandArgs ?? "", StringComparison.OrdinalIgnoreCase)))
                    {
                        item.SortOrder = current.Count;
                        current.Add(item);
                        added++;
                    }
                }
            }
        }

        ConfigService.SanitizeAndDeduplicate(current);
        return added;
    }

    private void RescanBrowsersButton_Click(object sender, RoutedEventArgs e)
    {
        var current = ConfigService.Instance.Current.Browsers;
        ConfigService.SanitizeAndDeduplicate(current);

        var detected = BrowserDetector.DetectInstalledBrowsers();
        int added = 0;

        foreach (var d in detected)
        {
            if (!current.Any(b =>
                string.Equals(b.ExePath, d.ExePath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(b.CommandArgs ?? "", d.CommandArgs ?? "", StringComparison.OrdinalIgnoreCase)))
            {
                d.SortOrder = current.Count;
                current.Add(d);
                added++;
            }
        }

        ConfigService.SanitizeAndDeduplicate(current);
        ConfigService.Instance.RefreshRuntimeState();
        ConfigService.Instance.Save();
        LoadBrowsers();
        UpdateRuleTargetBrowserCombo();

        MessageBox.Show(
            added > 0 ? $"已成功发现并添加 {added} 个新浏览器！" : "未发现更多新浏览器，所有已知浏览器均已存在（且已自动清理无关条目）。",
            "扫描完成",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }

    private void EditBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is BrowserItem item)
        {
            var dialog = new BrowserEditDialog(item)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                ConfigService.Instance.Save();
                ConfigService.Instance.RefreshRuntimeState();
                LoadBrowsers();
                LoadRules();
                UpdateRuleTargetBrowserCombo();
            }
        }
    }

    private readonly ObservableCollection<BrowserItem> _browsersObservable = [];
    private Point _browserDragStartPoint;
    private BrowserItem? _browserDragItem;
    private DragAdorner? _dragAdorner;
    private AdornerLayer? _adornerLayer;
    private ScrollViewer? _browsersScrollViewer;
    private readonly DispatcherTimer _autoScrollTimer;
    private double _scrollVelocity;
    private int _scrollDirection; // -1: up, 1: down, 0: none
    private Point _lastDragPoint;
    private BrowserItem? _currentSourceDragItem;
    private BrowserItem? _lastTargetItem;
    private bool? _lastIsTopHalf;

    private void AutoScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (_browsersScrollViewer == null)
        {
            _browsersScrollViewer = GetScrollViewer(BrowsersListBox);
        }

        if (_browsersScrollViewer != null && _scrollDirection != 0)
        {
            double newOffset = _browsersScrollViewer.VerticalOffset + (_scrollDirection * _scrollVelocity);
            newOffset = Math.Max(0, Math.Min(_browsersScrollViewer.ScrollableHeight, newOffset));
            _browsersScrollViewer.ScrollToVerticalOffset(newOffset);

            // 滚动后立即刷新插入指示线
            UpdateDropIndicators(_lastDragPoint, _currentSourceDragItem);
        }
    }

    private void DragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is BrowserItem item)
        {
            _browserDragStartPoint = e.GetPosition(null);
            _browserDragItem = item;
            e.Handled = true;
        }
    }

    private void DragHandle_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _browserDragItem != null)
        {
            Point currentPosition = e.GetPosition(null);
            Vector diff = _browserDragStartPoint - currentPosition;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                var dragItem = _browserDragItem;
                _currentSourceDragItem = dragItem;
                dragItem.IsDragging = true;

                // 创建浮动拖拽 Adorner (半透明精致悬浮卡片影子，包含图标、名称与标签)
                var adornerLayer = AdornerLayer.GetAdornerLayer(BrowsersListBox);
                if (adornerLayer != null)
                {
                    var ghostBorder = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x15, 0x1D, 0x2C)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
                        BorderThickness = new Thickness(1.5),
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(12, 8, 14, 8),
                        Effect = new System.Windows.Media.Effects.DropShadowEffect
                        {
                            BlurRadius = 24,
                            Color = Color.FromRgb(0x02, 0x84, 0xC7),
                            ShadowDepth = 6,
                            Opacity = 0.85
                        }
                    };

                    var contentPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    contentPanel.Children.Add(new TextBlock
                    {
                        Text = "⋮⋮",
                        Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
                        FontWeight = FontWeights.Bold,
                        FontSize = 14,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0)
                    });

                    if (dragItem.Icon != null)
                    {
                        var iconImg = new Image
                        {
                            Source = dragItem.Icon,
                            Width = 24,
                            Height = 24,
                            Margin = new Thickness(0, 0, 8, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        RenderOptions.SetBitmapScalingMode(iconImg, BitmapScalingMode.HighQuality);
                        contentPanel.Children.Add(iconImg);
                    }

                    contentPanel.Children.Add(new TextBlock
                    {
                        Text = dragItem.Name,
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 13,
                        VerticalAlignment = VerticalAlignment.Center
                    });

                    if (!string.IsNullOrEmpty(dragItem.Tag))
                    {
                        var tagBorder = new Border
                        {
                            Background = new SolidColorBrush(Color.FromArgb(0x30, 0x38, 0xBD, 0xF8)),
                            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x38, 0xBD, 0xF8)),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(4),
                            Padding = new Thickness(5, 1, 5, 1),
                            Margin = new Thickness(8, 0, 0, 0),
                            VerticalAlignment = VerticalAlignment.Center,
                            Child = new TextBlock
                            {
                                Text = dragItem.Tag,
                                FontSize = 10.5,
                                FontWeight = FontWeights.Medium,
                                Foreground = new SolidColorBrush(Color.FromRgb(0x7D, 0xD3, 0xFC))
                            }
                        };
                        contentPanel.Children.Add(tagBorder);
                    }

                    ghostBorder.Child = contentPanel;

                    _dragAdorner = new DragAdorner(BrowsersListBox, ghostBorder);
                    adornerLayer.Add(_dragAdorner);
                    _adornerLayer = adornerLayer;
                }

                try
                {
                    var data = new DataObject("LinkFlow.BrowserItem", dragItem);
                    DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
                }
                finally
                {
                    CleanupDragState();
                }
            }
        }
    }

    private void BrowsersListBox_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("LinkFlow.BrowserItem"))
        {
            e.Effects = DragDropEffects.Move;

            _lastDragPoint = e.GetPosition(BrowsersListBox);
            _currentSourceDragItem = e.Data.GetData("LinkFlow.BrowserItem") as BrowserItem;

            // 1. 更新跟随鼠标的 Adorner 浮动卡片坐标
            if (_dragAdorner != null)
            {
                _dragAdorner.UpdatePosition(new Point(_lastDragPoint.X + 12, _lastDragPoint.Y + 12));
            }

            // 2. 检测边缘连续自动滚动 (解决滚动条/进度条卡住的痛点)
            if (_browsersScrollViewer == null)
            {
                _browsersScrollViewer = GetScrollViewer(BrowsersListBox);
            }

            if (_browsersScrollViewer != null)
            {
                Point scrollPos = e.GetPosition(_browsersScrollViewer);
                double edgeZone = 70.0; // 边缘触发区 70px

                if (scrollPos.Y < edgeZone && _browsersScrollViewer.VerticalOffset > 0)
                {
                    // 靠近顶部或超出了上方，持续平滑向上滚动，绝不卡住
                    _scrollDirection = -1;
                    double dist = Math.Max(10, edgeZone - scrollPos.Y);
                    double ratio = Math.Min(3.0, dist / edgeZone);
                    _scrollVelocity = Math.Max(6, ratio * 28);
                    if (!_autoScrollTimer.IsEnabled) _autoScrollTimer.Start();
                }
                else if (scrollPos.Y > _browsersScrollViewer.ActualHeight - edgeZone &&
                         _browsersScrollViewer.VerticalOffset < _browsersScrollViewer.ScrollableHeight)
                {
                    // 靠近底部或超出了下方，持续平滑向下滚动，绝不卡住
                    _scrollDirection = 1;
                    double dist = Math.Max(10, scrollPos.Y - (_browsersScrollViewer.ActualHeight - edgeZone));
                    double ratio = Math.Min(3.0, dist / edgeZone);
                    _scrollVelocity = Math.Max(6, ratio * 28);
                    if (!_autoScrollTimer.IsEnabled) _autoScrollTimer.Start();
                }
                else
                {
                    _scrollDirection = 0;
                    _autoScrollTimer.Stop();
                }
            }

            // 3. 动态点亮插入指示线
            UpdateDropIndicators(_lastDragPoint, _currentSourceDragItem);

            e.Handled = true;
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void UpdateDropIndicators(Point mousePos, BrowserItem? sourceItem)
    {
        if (sourceItem == null) return;

        ListBoxItem? targetListBoxItem = null;
        HitTestResult hit = VisualTreeHelper.HitTest(BrowsersListBox, mousePos);
        if (hit?.VisualHit != null)
        {
            targetListBoxItem = FindVisualAncestor<ListBoxItem>(hit.VisualHit);
        }

        // 如果鼠标在滚动条区域（右侧）或边缘空白处，向左采样居中点做采样
        if (targetListBoxItem == null && _browsersObservable.Count > 0)
        {
            double sampleX = Math.Max(10, BrowsersListBox.ActualWidth / 2);
            double sampleY = Math.Max(5, Math.Min(BrowsersListBox.ActualHeight - 5, mousePos.Y));
            Point samplePoint = new Point(sampleX, sampleY);
            HitTestResult centerHit = VisualTreeHelper.HitTest(BrowsersListBox, samplePoint);
            if (centerHit?.VisualHit != null)
            {
                targetListBoxItem = FindVisualAncestor<ListBoxItem>(centerHit.VisualHit);
            }
        }

        if (targetListBoxItem != null && targetListBoxItem.DataContext is BrowserItem targetItem)
        {
            Point pt = ePointRelativeTo(targetListBoxItem, mousePos);
            double height = Math.Max(1.0, targetListBoxItem.ActualHeight);
            double ratio = Math.Max(0.0, Math.Min(1.0, pt.Y / height));

            // 迟滞防抖区间 (Hysteresis Zone)：
            // ratio < 0.40 -> 坚定判定为 Top
            // ratio > 0.60 -> 坚定判定为 Bottom
            // 中间 20% 缓冲区 -> 沿用上次状态，绝不在中线附近微小抖动时翻转闪烁
            bool isTopHalf;
            if (ratio < 0.40)
            {
                isTopHalf = true;
            }
            else if (ratio > 0.60)
            {
                isTopHalf = false;
            }
            else
            {
                isTopHalf = _lastIsTopHalf ?? (ratio < 0.50);
            }

            // 状态无变化时直接短路返回，彻底杜绝无谓的 UI 属性刷新与闪烁
            if (targetItem == _lastTargetItem && isTopHalf == _lastIsTopHalf)
            {
                return;
            }

            _lastTargetItem = targetItem;
            _lastIsTopHalf = isTopHalf;

            // 仅对状态需要变化的项进行精确赋值
            foreach (var b in _browsersObservable)
            {
                bool shouldTop = (b == targetItem && isTopHalf && b != sourceItem);
                bool shouldBottom = (b == targetItem && !isTopHalf && b != sourceItem);

                if (b.IsDropTargetTop != shouldTop) b.IsDropTargetTop = shouldTop;
                if (b.IsDropTargetBottom != shouldBottom) b.IsDropTargetBottom = shouldBottom;
            }
        }
        else
        {
            ClearDropIndicators();
        }
    }

    private Point ePointRelativeTo(UIElement target, Point ptInListBox)
    {
        try
        {
            return BrowsersListBox.TranslatePoint(ptInListBox, target);
        }
        catch
        {
            return new Point(0, 0);
        }
    }

    private void BrowsersListBox_DragLeave(object sender, DragEventArgs e)
    {
        Point pt = e.GetPosition(BrowsersListBox);
        // 关键防抖：如果鼠标依然在列表区域或其上下延伸区，不要清除指示线，避免卡片间切换时狂闪！
        if (pt.X >= -50 && pt.X <= BrowsersListBox.ActualWidth + 50 &&
            pt.Y >= -100 && pt.Y <= BrowsersListBox.ActualHeight + 100)
        {
            return;
        }

        ClearDropIndicators();
        _scrollDirection = 0;
        _autoScrollTimer.Stop();
    }

    private void BrowsersListBox_PreviewDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("LinkFlow.BrowserItem"))
        {
            var sourceItem = e.Data.GetData("LinkFlow.BrowserItem") as BrowserItem;
            if (sourceItem != null)
            {
                Point mousePos = e.GetPosition(BrowsersListBox);
                ListBoxItem? targetListBoxItem = null;
                HitTestResult hit = VisualTreeHelper.HitTest(BrowsersListBox, mousePos);
                if (hit?.VisualHit != null)
                {
                    targetListBoxItem = FindVisualAncestor<ListBoxItem>(hit.VisualHit);
                }

                if (targetListBoxItem == null && _browsersObservable.Count > 0)
                {
                    double sampleX = Math.Max(20, BrowsersListBox.ActualWidth * 0.35);
                    double sampleY = Math.Max(5, Math.Min(BrowsersListBox.ActualHeight - 5, mousePos.Y));
                    HitTestResult centerHit = VisualTreeHelper.HitTest(BrowsersListBox, new Point(sampleX, sampleY));
                    if (centerHit?.VisualHit != null)
                    {
                        targetListBoxItem = FindVisualAncestor<ListBoxItem>(centerHit.VisualHit);
                    }
                }

                var targetItem = targetListBoxItem?.DataContext as BrowserItem;

                if (targetListBoxItem != null && targetItem != null && targetItem != sourceItem)
                {
                    Point pt = ePointRelativeTo(targetListBoxItem, mousePos);
                    bool isTopHalf = _lastIsTopHalf ?? (pt.Y < targetListBoxItem.ActualHeight / 2);

                    int oldIndex = _browsersObservable.IndexOf(sourceItem);
                    int targetIndex = _browsersObservable.IndexOf(targetItem);

                    if (oldIndex >= 0 && targetIndex >= 0)
                    {
                        int newIndex = isTopHalf ? targetIndex : targetIndex + 1;
                        if (newIndex > oldIndex) newIndex--;

                        if (newIndex >= 0 && newIndex < _browsersObservable.Count && newIndex != oldIndex)
                        {
                            MoveBrowserToIndex(sourceItem, newIndex);
                        }
                    }
                }
            }

            CleanupDragState();
            e.Handled = true;
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            CleanupDragState();
            Window_Drop(sender, e);
        }
    }

    private void MoveBrowserToIndex(BrowserItem item, int targetIndex)
    {
        int oldIndex = _browsersObservable.IndexOf(item);
        if (oldIndex >= 0 && targetIndex >= 0 && targetIndex < _browsersObservable.Count && oldIndex != targetIndex)
        {
            _browsersObservable.Move(oldIndex, targetIndex);

            for (int i = 0; i < _browsersObservable.Count; i++)
            {
                _browsersObservable[i].SortOrder = i;
            }

            ConfigService.Instance.Current.Browsers = _browsersObservable.ToList();
            ConfigService.Instance.RefreshRuntimeState();
            ConfigService.Instance.Save();

            foreach (var b in _browsersObservable)
            {
                var found = ConfigService.Instance.Current.Browsers.FirstOrDefault(x => x.Id == b.Id);
                if (found != null)
                {
                    b.HotkeyLabel = found.HotkeyLabel;
                }
            }
        }
    }

    private void MoveToTop_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is BrowserItem item)
        {
            MoveBrowserToIndex(item, 0);
        }
    }

    private void MoveToBottom_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is BrowserItem item)
        {
            MoveBrowserToIndex(item, _browsersObservable.Count - 1);
        }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is BrowserItem item)
        {
            int index = _browsersObservable.IndexOf(item);
            if (index > 0)
            {
                MoveBrowserToIndex(item, index - 1);
            }
        }
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is BrowserItem item)
        {
            int index = _browsersObservable.IndexOf(item);
            if (index >= 0 && index < _browsersObservable.Count - 1)
            {
                MoveBrowserToIndex(item, index + 1);
            }
        }
    }

    private void ToggleBrowserEnabledContext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is BrowserItem item)
        {
            item.IsDisabled = !item.IsDisabled;
            ConfigService.Instance.RefreshRuntimeState();
            ConfigService.Instance.Save();
            UpdateRuleTargetBrowserCombo();
            LoadPreferences();
        }
    }

    private void LaunchTest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is BrowserItem item)
        {
            LaunchBrowserForTest(item);
        }
    }

    private void EditBrowserContext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is BrowserItem item)
        {
            var btn = new Button { Tag = item };
            EditBrowser_Click(btn, e);
        }
    }

    private void DeleteBrowserContext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is BrowserItem item)
        {
            var btn = new Button { Tag = item };
            DeleteBrowser_Click(btn, e);
        }
    }

    private static ScrollViewer? GetScrollViewer(DependencyObject depObj)
    {
        if (depObj is ScrollViewer sv) return sv;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = VisualTreeHelper.GetChild(depObj, i);
            var result = GetScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }

    private void CleanupDragState()
    {
        _scrollDirection = 0;
        _autoScrollTimer.Stop();

        if (_dragAdorner != null && _adornerLayer != null)
        {
            _adornerLayer.Remove(_dragAdorner);
            _dragAdorner = null;
            _adornerLayer = null;
        }

        ClearDropIndicators();

        foreach (var b in _browsersObservable)
        {
            if (b.IsDragging) b.IsDragging = false;
        }

        _browserDragItem = null;
        _currentSourceDragItem = null;
    }

    private void ClearDropIndicators()
    {
        _lastTargetItem = null;
        _lastIsTopHalf = null;

        foreach (var b in _browsersObservable)
        {
            if (b.IsDropTargetTop) b.IsDropTargetTop = false;
            if (b.IsDropTargetBottom) b.IsDropTargetBottom = false;
        }
    }

    private void BrowserCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            var source = e.OriginalSource as DependencyObject;
            if (FindVisualAncestor<Button>(source) != null) return;
            if (FindVisualAncestor<System.Windows.Controls.Primitives.ButtonBase>(source) != null) return;

            if (sender is FrameworkElement elem && elem.DataContext is BrowserItem browser)
            {
                e.Handled = true;
                LaunchBrowserForTest(browser);
            }
        }
    }

    private void LaunchBrowserForTest(BrowserItem browser, string? profileId = null)
    {
        try
        {
            bool success = BrowserLauncher.Launch(browser, "about:blank", profileId);
            if (!success)
            {
                MessageBox.Show($"未能启动浏览器「{browser.Name}」，请检查路径是否正确：\n{browser.ExePath}", "启动提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动浏览器失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void HotkeyBadge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement elem && elem.Tag is BrowserItem item)
        {
            var dialog = new QuickHotkeyDialog(item)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                ConfigService.Instance.RefreshRuntimeState();
                ConfigService.Instance.Save();
                LoadBrowsers();
            }
        }
    }

    private void ProfilesBadge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement elem && elem.Tag is BrowserItem browser && browser.Profiles.Count > 0)
        {
            var menu = new ContextMenu();

            var headerItem = new MenuItem
            {
                Header = $"👥 {browser.Name} 独立 Profile 列表",
                IsEnabled = false,
                FontWeight = FontWeights.Bold
            };
            menu.Items.Add(headerItem);
            menu.Items.Add(new Separator());

            foreach (var profile in browser.Profiles)
            {
                var profileMenu = new MenuItem
                {
                    Header = $"👤 {profile.Name}"
                };

                var launchItem = new MenuItem
                {
                    Header = "▶ 启动此环境测试 (about:blank)"
                };
                var pId = profile.Id;
                launchItem.Click += (_, _) =>
                {
                    LaunchBrowserForTest(browser, pId);
                };
                profileMenu.Items.Add(launchItem);

                var extractItem = new MenuItem
                {
                    Header = "➕ 提取为独立浏览器卡片 (独立快捷键与规则)"
                };
                var currentProfile = profile;
                extractItem.Click += (_, _) =>
                {
                    ExtractProfileAsIndependentBrowser(browser, currentProfile);
                };
                profileMenu.Items.Add(extractItem);

                menu.Items.Add(profileMenu);
            }

            menu.PlacementTarget = elem;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void ExtractProfileAsIndependentBrowser(BrowserItem parentBrowser, BrowserProfileItem profile)
    {
        var current = ConfigService.Instance.Current.Browsers;

        string profileArgs = !string.IsNullOrWhiteSpace(profile.Args) ? profile.Args : $"--profile-directory=\"{profile.Id}\"";
        string mergedArgs = string.IsNullOrWhiteSpace(parentBrowser.CommandArgs)
            ? profileArgs
            : $"{parentBrowser.CommandArgs.Trim()} {profileArgs}".Trim();

        if (current.Any(b => string.Equals(b.ExePath, parentBrowser.ExePath, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(b.CommandArgs ?? "", mergedArgs, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show($"该 Profile (「{profile.Name}」) 已作为独立浏览器存在于列表中！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var newBrowser = new BrowserItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"{parentBrowser.Name} ({profile.Name})",
            OriginalName = parentBrowser.OriginalName,
            Tag = profile.Name,
            ExePath = parentBrowser.ExePath,
            CommandArgs = mergedArgs,
            PrivacyArgs = parentBrowser.PrivacyArgs,
            IconPath = parentBrowser.IconPath,
            Icon = parentBrowser.Icon,
            SortOrder = current.Count
        };

        current.Add(newBrowser);
        ConfigService.SanitizeAndDeduplicate(current);
        ConfigService.Instance.RefreshRuntimeState();
        ConfigService.Instance.Save();
        LoadBrowsers();
        UpdateRuleTargetBrowserCombo();

        MessageBox.Show($"已成功将「{profile.Name}」提取为独立浏览器卡片！\n现在可为其分配专属快捷键及配置分流规则。", "提取成功", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static T? FindVisualAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void DeleteBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is BrowserItem item)
        {
            var res = MessageBox.Show($"确定要从 LinkFlow 中移除「{item.Name}」吗？", "确认移除", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                ConfigService.Instance.Current.Browsers.Remove(item);
                for (int i = 0; i < ConfigService.Instance.Current.Browsers.Count; i++)
                {
                    ConfigService.Instance.Current.Browsers[i].SortOrder = i;
                }
                ConfigService.Instance.RefreshRuntimeState();
                ConfigService.Instance.Save();
                LoadBrowsers();
                UpdateRuleTargetBrowserCombo();
            }
        }
    }

    private void RulePatternBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (RulePatternPlaceholder != null)
            RulePatternPlaceholder.Visibility = Visibility.Collapsed;
    }

    private void RulePatternBox_LostFocus(object sender, RoutedEventArgs e)
    {
        UpdateRulePlaceholder();
    }

    private void RulePatternBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateRulePlaceholder();
    }

    private void UpdateRulePlaceholder()
    {
        if (RulePatternPlaceholder != null)
        {
            RulePatternPlaceholder.Visibility = (string.IsNullOrWhiteSpace(RulePatternBox.Text) && !RulePatternBox.IsFocused)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void PresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string preset)
        {
            switch (preset)
            {
                case "work":
                    RulePatternBox.Text = "feishu.cn;dingtalk.com";
                    RuleMatchTypeCombo.SelectedIndex = 0; // 域名及子域
                    break;
                case "dev":
                    RulePatternBox.Text = "github.com";
                    RuleMatchTypeCombo.SelectedIndex = 0;
                    break;
                case "video":
                    RulePatternBox.Text = "bilibili.com";
                    RuleMatchTypeCombo.SelectedIndex = 0;
                    break;
                case "local":
                    RulePatternBox.Text = "localhost";
                    RuleMatchTypeCombo.SelectedIndex = 1; // 包含关键词
                    break;
            }
            RulePatternBox.Focus();
            RulePatternBox.CaretIndex = RulePatternBox.Text.Length;
        }
    }

    private void RuleToggle_Click(object sender, RoutedEventArgs e)
    {
        ConfigService.Instance.RefreshRuntimeState();
        ConfigService.Instance.Save();
        RunRuleTester();
    }

    private void MoveRuleUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RoutingRule rule)
        {
            var list = ConfigService.Instance.Current.Rules;
            var index = list.IndexOf(rule);
            if (index > 0)
            {
                list.RemoveAt(index);
                list.Insert(index - 1, rule);
                ConfigService.Instance.RefreshRuntimeState();
                ConfigService.Instance.Save();
                LoadRules();
            }
        }
    }

    private void MoveRuleDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RoutingRule rule)
        {
            var list = ConfigService.Instance.Current.Rules;
            var index = list.IndexOf(rule);
            if (index >= 0 && index < list.Count - 1)
            {
                list.RemoveAt(index);
                list.Insert(index + 1, rule);
                ConfigService.Instance.RefreshRuntimeState();
                ConfigService.Instance.Save();
                LoadRules();
            }
        }
    }

    private void AddRuleButton_Click(object sender, RoutedEventArgs e)
    {
        var rawPattern = RulePatternBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(rawPattern))
        {
            MessageBox.Show("请输入匹配的域名或关键词表达式！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (RuleTargetBrowserCombo.SelectedItem is not BrowserItem targetBrowser)
        {
            MessageBox.Show("请选择目标分流浏览器！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var matchType = (RuleMatchTypeCombo.SelectedItem as MatchTypeOption)?.Type ?? RuleMatchType.Domain;
        string? profileId = (RuleTargetProfileCombo.SelectedItem as ProfileOption)?.Id;

        string[] patterns;
        if (matchType == RuleMatchType.Regex)
        {
            try
            {
                _ = new System.Text.RegularExpressions.Regex(rawPattern);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"正则表达式语法无效: {ex.Message}", "语法错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            patterns = [rawPattern];
        }
        else
        {
            patterns = rawPattern.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        if (_editingRule != null)
        {
            var cleanFirst = CleanPattern(patterns[0], matchType);
            _editingRule.Pattern = cleanFirst;
            _editingRule.MatchType = matchType;
            _editingRule.TargetBrowserId = targetBrowser.Id;
            _editingRule.TargetProfileId = profileId;

            for (int i = 1; i < patterns.Length; i++)
            {
                var cleanExtra = CleanPattern(patterns[i], matchType);
                if (string.IsNullOrWhiteSpace(cleanExtra)) continue;

                ConfigService.Instance.Current.Rules.RemoveAll(r =>
                    r.MatchType == matchType &&
                    string.Equals(r.Pattern, cleanExtra, StringComparison.OrdinalIgnoreCase));

                var extraRule = new RoutingRule
                {
                    Pattern = cleanExtra,
                    MatchType = matchType,
                    TargetBrowserId = targetBrowser.Id,
                    TargetProfileId = profileId,
                    IsEnabled = true
                };

                ConfigService.Instance.Current.Rules.Insert(0, extraRule);
            }

            ResetRuleForm();
            ConfigService.Instance.RefreshRuntimeState();
            ConfigService.Instance.Save();
            LoadRules();
            return;
        }

        int added = 0;

        foreach (var p in patterns)
        {
            var cleanPattern = CleanPattern(p, matchType);
            if (string.IsNullOrWhiteSpace(cleanPattern)) continue;

            ConfigService.Instance.Current.Rules.RemoveAll(r =>
                r.MatchType == matchType &&
                string.Equals(r.Pattern, cleanPattern, StringComparison.OrdinalIgnoreCase));

            var newRule = new RoutingRule
            {
                Pattern = cleanPattern,
                MatchType = matchType,
                TargetBrowserId = targetBrowser.Id,
                TargetProfileId = profileId,
                IsEnabled = true
            };

            ConfigService.Instance.Current.Rules.Insert(0, newRule);
            added++;
        }

        RulePatternBox.Clear();
        UpdateRulePlaceholder();
        ConfigService.Instance.RefreshRuntimeState();
        ConfigService.Instance.Save();
        LoadRules();
    }

    private static string CleanPattern(string raw, RuleMatchType matchType)
    {
        var clean = raw.Trim();
        if (matchType == RuleMatchType.Domain)
        {
            if (clean.Contains("://"))
            {
                var pUri = UrlResolver.Parse(clean);
                clean = string.IsNullOrEmpty(pUri.Domain) ? pUri.Host : pUri.Domain;
            }
            else if (clean.Contains('/'))
            {
                clean = clean.Split('/')[0];
            }
            if (clean.StartsWith("*."))
            {
                clean = clean[2..];
            }
        }
        return clean;
    }

    private void EditRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RoutingRule rule)
        {
            _editingRule = rule;
            RuleFormTitleBlock.Text = "✏️ 编辑分流规则";
            CancelEditRuleButton.Visibility = Visibility.Visible;
            SubmitRuleIcon.Text = "💾";
            SubmitRuleText.Text = "保存修改";

            RulePatternBox.Text = rule.Pattern;
            UpdateRulePlaceholder();

            var matchTypeOption = (RuleMatchTypeCombo.ItemsSource as IEnumerable<MatchTypeOption>)
                ?.FirstOrDefault(o => o.Type == rule.MatchType);
            if (matchTypeOption != null)
            {
                RuleMatchTypeCombo.SelectedItem = matchTypeOption;
            }

            var activeBrowsers = RuleTargetBrowserCombo.ItemsSource as IEnumerable<BrowserItem>;
            var targetB = activeBrowsers?.FirstOrDefault(b => b.Id == rule.TargetBrowserId);
            if (targetB != null)
            {
                RuleTargetBrowserCombo.SelectedItem = targetB;
                UpdateRuleTargetProfileCombo();

                if (!string.IsNullOrWhiteSpace(rule.TargetProfileId))
                {
                    var profileOptions = RuleTargetProfileCombo.ItemsSource as IEnumerable<ProfileOption>;
                    var pOption = profileOptions?.FirstOrDefault(p => p.Id == rule.TargetProfileId);
                    if (pOption != null)
                    {
                        RuleTargetProfileCombo.SelectedItem = pOption;
                    }
                }
            }

            RulePatternBox.Focus();
            RulePatternBox.SelectAll();
        }
    }

    private void CancelEditRule_Click(object sender, RoutedEventArgs e)
    {
        ResetRuleForm();
    }

    private void ResetRuleForm()
    {
        _editingRule = null;
        RuleFormTitleBlock.Text = "➕ 新建分流规则";
        CancelEditRuleButton.Visibility = Visibility.Collapsed;
        SubmitRuleIcon.Text = "➕";
        SubmitRuleText.Text = "添加规则";
        RulePatternBox.Clear();
        UpdateRulePlaceholder();
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RoutingRule rule)
        {
            if (_editingRule == rule)
            {
                ResetRuleForm();
            }
            ConfigService.Instance.Current.Rules.Remove(rule);
            ConfigService.Instance.RefreshRuntimeState();
            ConfigService.Instance.Save();
            LoadRules();
        }
    }

    private void TesterUrlBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (TesterPlaceholder != null)
            TesterPlaceholder.Visibility = Visibility.Collapsed;
    }

    private void TesterUrlBox_LostFocus(object sender, RoutedEventArgs e)
    {
        UpdateTesterPlaceholder();
    }

    private void TesterUrlBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateTesterPlaceholder();
        RunRuleTester();
    }

    private void UpdateTesterPlaceholder()
    {
        if (TesterPlaceholder != null)
        {
            TesterPlaceholder.Visibility = (string.IsNullOrWhiteSpace(TesterUrlBox.Text) && !TesterUrlBox.IsFocused)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void RuleTesterToggle_Click(object sender, RoutedEventArgs e)
    {
        bool willBeVisible = RuleTesterBodyGrid.Visibility != Visibility.Visible;
        SetRuleTesterVisibility(willBeVisible);
    }

    private void SetRuleTesterVisibility(bool isVisible)
    {
        if (RuleTesterBodyGrid != null)
        {
            RuleTesterBodyGrid.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }
        if (RuleTesterToggleArrow != null)
        {
            RuleTesterToggleArrow.Text = isVisible ? "▼" : "▶";
        }
    }

    private void TestRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RoutingRule rule)
        {
            SetRuleTesterVisibility(true);
            string sampleUrl;
            if (rule.MatchType == RuleMatchType.Domain || rule.MatchType == RuleMatchType.Hostname)
            {
                sampleUrl = rule.Pattern.Contains("://") ? rule.Pattern : $"https://{rule.Pattern}";
            }
            else if (rule.MatchType == RuleMatchType.Contains)
            {
                sampleUrl = $"https://example.com/search?keyword={rule.Pattern}";
            }
            else if (rule.MatchType == RuleMatchType.Prefix)
            {
                sampleUrl = rule.Pattern.EndsWith("/") ? $"{rule.Pattern}sample" : $"{rule.Pattern}/sample";
            }
            else
            {
                sampleUrl = rule.Pattern.Contains("://") ? rule.Pattern : $"https://{rule.Pattern}";
            }

            if (TesterUrlBox != null)
            {
                TesterUrlBox.Text = sampleUrl;
                TesterUrlBox.Focus();
                TesterUrlBox.SelectAll();
            }
        }
    }

    private void RefreshQuickTestPills()
    {
        if (QuickTestPillsPanel == null) return;
        QuickTestPillsPanel.Children.Clear();

        var text = new TextBlock
        {
            Text = "示例测试:",
            FontSize = 11,
            Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#64748B")),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        QuickTestPillsPanel.Children.Add(text);

        // Add pills for current active rules
        var activeRules = ConfigService.Instance.Current.Rules
            .Where(r => r.IsEnabled && !string.IsNullOrWhiteSpace(r.Pattern))
            .Take(3)
            .ToList();

        foreach (var r in activeRules)
        {
            var targetUrl = r.Pattern.Contains("://") ? r.Pattern : $"https://{r.Pattern}";
            var btn = CreatePillButton($"⚡ {r.Pattern}", targetUrl);
            QuickTestPillsPanel.Children.Add(btn);
        }

        // Add standard sample pills
        QuickTestPillsPanel.Children.Add(CreatePillButton("🐙 GitHub", "https://github.com/microsoft/terminal"));
        QuickTestPillsPanel.Children.Add(CreatePillButton("🎬 B站", "https://www.bilibili.com/video/BV1"));
        QuickTestPillsPanel.Children.Add(CreatePillButton("🏢 飞书", "https://feishu.cn/workbench"));
        QuickTestPillsPanel.Children.Add(CreatePillButton("🌐 百度", "https://www.baidu.com/s?wd=test"));
    }

    private Button CreatePillButton(string label, string targetUrl)
    {
        var btn = new Button
        {
            Style = (Style)FindResource("FluentPresetPillButton"),
            Content = label,
            Tag = targetUrl,
            Margin = new Thickness(0, 0, 5, 0),
            ToolTip = $"点击立即测试: {targetUrl}"
        };
        btn.Click += (s, e) =>
        {
            if (TesterUrlBox != null)
            {
                TesterUrlBox.Text = targetUrl;
            }
        };
        return btn;
    }

    private void PasteTesterClipboardButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText()?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (TesterUrlBox != null)
                    {
                        TesterUrlBox.Text = text;
                    }
                    return;
                }
            }
            MessageBox.Show("剪贴板中未找到文本内容。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"读取剪贴板失败: {ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ClearTesterButton_Click(object sender, RoutedEventArgs e)
    {
        if (TesterUrlBox != null)
        {
            TesterUrlBox.Clear();
            RunRuleTester();
        }
    }

    private void TesterLaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastTestBrowser != null && !string.IsNullOrWhiteSpace(_lastTestUrl))
        {
            BrowserLauncher.Launch(_lastTestBrowser, _lastTestUrl, _lastTestProfileId, false);
        }
    }

    private void TesterTestPickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastTestUrl))
        {
            var picker = new PickerWindow(_lastTestUrl);
            picker.Show();
        }
    }

    private void RunRuleTester()
    {
        if (TesterResultBorder == null || TesterResultText == null || TesterResultIcon == null) return;

        var url = TesterUrlBox?.Text?.Trim();
        _lastTestUrl = url ?? string.Empty;

        if (string.IsNullOrWhiteSpace(url))
        {
            _lastTestBrowser = null;
            _lastTestProfileId = null;
            TesterResultBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            TesterResultBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
            TesterResultIcon.Text = "💡";
            TesterResultText.Text = "输入网址或点击上方测试示例，实时查看分流走向与目标浏览器";
            TesterResultText.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#94A3B8"));

            if (TesterLaunchButton != null) TesterLaunchButton.Visibility = Visibility.Collapsed;
            if (TesterTestPickerButton != null) TesterTestPickerButton.Visibility = Visibility.Collapsed;
            return;
        }

        var result = RuleMatcher.Match(url, ConfigService.Instance.Current, ignoreAlwaysPrompt: true);
        if (result != null)
        {
            _lastTestBrowser = result.Browser;
            _lastTestProfileId = result.ProfileId;

            TesterResultBorder.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1810B981"));
            TesterResultBorder.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4510B981"));
            TesterResultIcon.Text = "🎯";

            string profileInfo = "";
            if (!string.IsNullOrWhiteSpace(result.ProfileId))
            {
                var pr = result.Browser.Profiles.FirstOrDefault(p => p.Id == result.ProfileId);
                profileInfo = $" [分身配置: {(pr != null ? pr.Name : result.ProfileId)}]";
            }

            TesterResultText.Text = $"命中规则 #{result.MatchedRule.PriorityIndex}「{result.MatchedRule.Pattern}」➔ 自动由【{result.Browser.Name}】{profileInfo} 打开";
            TesterResultText.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#34D399"));

            if (TesterLaunchButton != null) TesterLaunchButton.Visibility = Visibility.Visible;
            if (TesterTestPickerButton != null) TesterTestPickerButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            _lastTestBrowser = null;
            _lastTestProfileId = null;

            TesterResultBorder.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1838BDF8"));
            TesterResultBorder.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3538BDF8"));
            TesterResultIcon.Text = "ℹ️";
            TesterResultText.Text = "未命中任何已启用规则 ➔ 点击外部链接时将正常弹出 LinkFlow 选择器供您挑选";
            TesterResultText.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#38BDF8"));

            if (TesterLaunchButton != null) TesterLaunchButton.Visibility = Visibility.Collapsed;
            if (TesterTestPickerButton != null) TesterTestPickerButton.Visibility = Visibility.Visible;
        }
    }

    private void RegisterBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        var ok = SystemRegistration.RegisterAsWebBrowser();
        if (ok)
        {
            UpdateDefaultBrowserStatus();
            var res = MessageBox.Show(
                "LinkFlow 已成功注册到当前用户的系统浏览器列表！\n\n是否立即打开 Windows「默认应用」页面将其设为默认浏览器？",
                "注册成功",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information
            );

            if (res == MessageBoxResult.Yes)
            {
                OpenDefaultAppsButton_Click(sender, e);
            }
        }
        else
        {
            MessageBox.Show("注册失败，请检查文件写入权限。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UnregisterBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "确定要从当前用户的系统注册表中注销 LinkFlow 吗？\n\n注销后，系统默认浏览器列表中将不再出现此应用，已配置的规则和浏览器列表数据仍会保留在本地。",
            "确认注销系统关联",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question
        );

        if (confirm == MessageBoxResult.Yes)
        {
            var ok = SystemRegistration.UnregisterWebBrowser();
            if (ok)
            {
                UpdateDefaultBrowserStatus();
                MessageBox.Show("已成功从系统注册表中移除 LinkFlow 的协议关联与浏览器注册！", "注销成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("注销时发生异常，请检查是否有注册表读写权限。", "注销失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void OpenDefaultAppsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:defaultapps")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开系统设置：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveAndCloseButton_Click(object sender, RoutedEventArgs e)
    {
        // Check if rules or browsers were updated externally
        ConfigService.Instance.ReloadIfModified();

        var config = ConfigService.Instance.Current;
        config.RememberChoiceByDefault = RememberChoiceByDefaultCheck.IsChecked ?? false;
        config.AutoCloseOnLostFocus = AutoCloseOnLostFocusCheck.IsChecked ?? true;
        config.AlwaysPrompt = AlwaysPromptCheck.IsChecked ?? false;
        config.ResolveShortUrls = ResolveShortUrlsCheck.IsChecked ?? true;
        config.SuppressDefaultBrowserPrompts = SuppressDefaultPromptsCheck.IsChecked ?? true;

        config.EnableFallbackBrowser = EnableFallbackBrowserCheck.IsChecked ?? false;
        if (FallbackBrowserCombo.SelectedItem is BrowserItem fb)
        {
            config.FallbackBrowserId = fb.Id;
        }
        else
        {
            config.FallbackBrowserId = null;
        }

        if (ThemeComboBox.SelectedItem is ThemeOption opt)
        {
            config.Theme = opt.Key;
            App.ApplyTheme(config.Theme);
        }

        if (!string.IsNullOrWhiteSpace(ShortenersTextBox.Text))
        {
            config.UrlShorteners = ShortenersTextBox.Text
                .Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        else
        {
            config.UrlShorteners = new List<string>();
        }

        ConfigService.Instance.RefreshRuntimeState();
        ConfigService.Instance.Save();

        Close();
    }

    private void UpdateDefaultBrowserStatus()
    {
        bool isDefault = SystemRegistration.IsDefaultBrowser();

        if (DefaultBrowserBanner != null)
        {
            DefaultBrowserBanner.Visibility = isDefault ? Visibility.Collapsed : Visibility.Visible;
        }

        if (Tab3DefaultStatusBadge != null && Tab3DefaultStatusText != null)
        {
            if (isDefault)
            {
                Tab3DefaultStatusBadge.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2010B981"));
                Tab3DefaultStatusBadge.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4010B981"));
                Tab3DefaultStatusText.Text = "✅ 已是系统默认浏览器 (正常运作中)";
                Tab3DefaultStatusText.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#34D399"));
            }
            else
            {
                Tab3DefaultStatusBadge.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#20F59E0B"));
                Tab3DefaultStatusBadge.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#40F59E0B"));
                Tab3DefaultStatusText.Text = "⚠️ 尚未设为默认浏览器 (无法拦截外链)";
                Tab3DefaultStatusText.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FBBF24"));
            }
        }
    }

    private void OpenOnboardingGuideButton_Click(object sender, RoutedEventArgs e)
    {
        var onboarding = new OnboardingWindow();
        onboarding.Closed += (_, _) => UpdateDefaultBrowserStatus();
        onboarding.Show();
    }

    private void DismissBanner_Click(object sender, RoutedEventArgs e)
    {
        if (DefaultBrowserBanner != null)
        {
            DefaultBrowserBanner.Visibility = Visibility.Collapsed;
        }
    }
}
