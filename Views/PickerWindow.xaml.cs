using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LinkFlow.Models;
using LinkFlow.Services;

namespace LinkFlow.Views;

public partial class PickerWindow : Window
{
    private string _currentUrl;
    private ParsedUrlInfo _parsedInfo;
    private List<BrowserItem> _browsers;
    private bool _isOpeningSettings = false;
    private CancellationTokenSource? _resolveCts;

    public PickerWindow(string targetUrl)
    {
        InitializeComponent();

        _currentUrl = string.IsNullOrWhiteSpace(targetUrl) ? "about:blank" : targetUrl.Trim();
        _parsedInfo = UrlResolver.Parse(_currentUrl);

        _browsers = ConfigService.Instance.Current.Browsers
            .Where(b => !b.IsDisabled && File.Exists(b.ExePath))
            .OrderBy(b => b.SortOrder)
            .ToList();

        BrowsersItemsControl.ItemsSource = _browsers;
        if (_browsers.Count > 0)
        {
            _browsers[0].IsSelected = true;
        }

        if (EmptyBrowsersNotice != null)
        {
            EmptyBrowsersNotice.Visibility = _browsers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        UpdateUrlDisplay();

        RememberDomainCheckBox.IsChecked = ConfigService.Instance.Current.RememberChoiceByDefault;

        Loaded += PickerWindow_Loaded;
        Deactivated += PickerWindow_Deactivated;
        KeyDown += PickerWindow_KeyDown;

        ResolveShortUrlIfNeeded();
    }

    private void UpdateUrlDisplay()
    {
        UrlHostRun.Text = _parsedInfo.DisplayHost;
        UrlPathRun.Text = _parsedInfo.DisplayPath;
        UrlDisplayBlock.ToolTip = _currentUrl;

        if (!string.IsNullOrEmpty(_parsedInfo.Domain) && _parsedInfo.Domain != "blank" && _parsedInfo.Domain != "about:blank")
        {
            RememberDomainCheckBox.ToolTip = $"为「{_parsedInfo.Domain}」记住此选择，以后打开将自动分流";
        }
    }

    private void ResolveShortUrlIfNeeded()
    {
        var config = ConfigService.Instance.Current;
        if (!config.ResolveShortUrls || config.UrlShorteners == null || config.UrlShorteners.Count == 0)
            return;

        if (!UrlResolver.IsShortUrl(_currentUrl, config.UrlShorteners))
            return;

        _resolveCts = new CancellationTokenSource();
        var token = _resolveCts.Token;

        Task.Run(async () =>
        {
            var resolved = await UrlResolver.ResolveRedirectAsync(_currentUrl, config.UrlShorteners, token);
            if (!token.IsCancellationRequested && !string.Equals(resolved, _currentUrl, StringComparison.OrdinalIgnoreCase))
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    _currentUrl = resolved;
                    _parsedInfo = UrlResolver.Parse(_currentUrl);
                    UpdateUrlDisplay();

                    // Check if newly resolved URL matches an automated rule
                    if (!ConfigService.Instance.Current.AlwaysPrompt)
                    {
                        var match = RuleMatcher.Match(_currentUrl, ConfigService.Instance.Current);
                        if (match != null)
                        {
                            LaunchAndClose(match.Browser, match.ProfileId, false);
                        }
                    }
                });
            }
        }, token);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeMethods.ApplyWindowStylesAndIcon(this);
    }

    private void PickerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateLayout();
        PositionNearCursor();
        Activate();
        Focus();
    }

    private void PositionNearCursor()
    {
        try
        {
            NativeMethods.GetCursorPos(out var pt);

            var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
            var dpiX = transform?.M11 ?? 1.0;
            var dpiY = transform?.M22 ?? 1.0;

            var cursorDipX = pt.X * dpiX;
            var cursorDipY = pt.Y * dpiY;

            // Target window dimensions after layout pass
            var windowWidth = ActualWidth > 0 ? ActualWidth : Width;
            var windowHeight = ActualHeight > 0 ? ActualHeight : 420;

            var targetLeft = cursorDipX - windowWidth / 2.0;
            var targetTop = cursorDipY - 80.0; // Place slightly above cursor for natural reading

            // Ensure window stays within current monitor working area
            double workLeft, workTop, workRight, workBottom;

            var hMon = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };

            if (hMon != IntPtr.Zero && NativeMethods.GetMonitorInfo(hMon, ref mi))
            {
                workLeft = mi.rcWork.Left * dpiX;
                workTop = mi.rcWork.Top * dpiY;
                workRight = mi.rcWork.Right * dpiX;
                workBottom = mi.rcWork.Bottom * dpiY;
            }
            else
            {
                workLeft = SystemParameters.WorkArea.Left;
                workTop = SystemParameters.WorkArea.Top;
                workRight = workLeft + SystemParameters.WorkArea.Width;
                workBottom = workTop + SystemParameters.WorkArea.Height;
            }

            const double margin = 12.0;
            if (targetLeft < workLeft + margin) targetLeft = workLeft + margin;
            if (targetLeft + windowWidth > workRight - margin) targetLeft = workRight - windowWidth - margin;
            if (targetTop + windowHeight > workBottom - margin) targetTop = workBottom - windowHeight - margin;
            if (targetTop < workTop + margin) targetTop = workTop + margin;

            Left = targetLeft;
            Top = targetTop;
        }
        catch
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void PickerWindow_KeyDown(object sender, KeyEventArgs e)
    {
        // When Alt or other system keys are pressed, WPF sets e.Key to Key.System and stores the actual key in e.SystemKey
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (key == Key.Space && !(Keyboard.FocusedElement is TextBox))
        {
            RememberDomainCheckBox.IsChecked = !(RememberDomainCheckBox.IsChecked ?? false);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && key == Key.C)
        {
            PerformCopy();
            e.Handled = true;
            return;
        }

        // Arrow Key navigation
        if (key == Key.Down)
        {
            SelectNextBrowser();
            e.Handled = true;
            return;
        }

        if (key == Key.Up)
        {
            SelectPrevBrowser();
            e.Handled = true;
            return;
        }

        // Enter key to launch selected browser
        if (key == Key.Enter)
        {
            var selected = _browsers.FirstOrDefault(b => b.IsSelected) ?? _browsers.FirstOrDefault();
            if (selected != null)
            {
                bool isPrivacy = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
                LaunchAndClose(selected, null, isPrivacy);
                e.Handled = true;
                return;
            }
        }

        // Check number & letter hotkeys
        string? pressedKey = null;
        if (key >= Key.D1 && key <= Key.D9)
        {
            pressedKey = ((int)key - (int)Key.D0).ToString();
        }
        else if (key >= Key.NumPad1 && key <= Key.NumPad9)
        {
            pressedKey = ((int)key - (int)Key.NumPad0).ToString();
        }
        else if (key == Key.D0 || key == Key.NumPad0)
        {
            pressedKey = "0";
        }
        else if (key >= Key.A && key <= Key.Z && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            pressedKey = key.ToString().ToUpperInvariant();
        }

        if (pressedKey != null)
        {
            var match = _browsers.FirstOrDefault(b => string.Equals(b.HotkeyLabel, pressedKey, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                bool isPrivacy = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
                LaunchAndClose(match, null, isPrivacy);
                e.Handled = true;
            }
        }
    }

    private void SelectNextBrowser()
    {
        if (_browsers.Count == 0) return;
        int currentIndex = _browsers.FindIndex(b => b.IsSelected);
        int nextIndex = (currentIndex + 1) % _browsers.Count;
        for (int i = 0; i < _browsers.Count; i++)
        {
            _browsers[i].IsSelected = (i == nextIndex);
        }
    }

    private void SelectPrevBrowser()
    {
        if (_browsers.Count == 0) return;
        int currentIndex = _browsers.FindIndex(b => b.IsSelected);
        int prevIndex = currentIndex <= 0 ? _browsers.Count - 1 : currentIndex - 1;
        for (int i = 0; i < _browsers.Count; i++)
        {
            _browsers[i].IsSelected = (i == prevIndex);
        }
    }

    private bool _isModalDialogOpen;

    private void PickerWindow_Deactivated(object? sender, EventArgs e)
    {
        if (_isOpeningSettings || _isModalDialogOpen) return;

        if (ConfigService.Instance.Current.AutoCloseOnLostFocus)
        {
            Close();
        }
    }

    private void BrowserCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is BrowserItem browser)
        {
            LaunchAndClose(browser, null, false);
        }
    }

    private void ProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is BrowserProfileItem profile)
        {
            // Find parent browser
            var browser = _browsers.FirstOrDefault(b => b.Profiles.Contains(profile));
            if (browser != null)
            {
                LaunchAndClose(browser, profile.Id, false);
            }
        }
        e.Handled = true;
    }

    private void PrivacyModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is BrowserItem browser)
        {
            LaunchAndClose(browser, null, true);
        }
        e.Handled = true;
    }

    private void LaunchAndClose(BrowserItem browser, string? profileId, bool isPrivacy)
    {
        if (RememberDomainCheckBox.IsChecked == true &&
            !string.IsNullOrEmpty(_parsedInfo.Domain) &&
            _parsedInfo.Domain != "blank" &&
            _parsedInfo.Domain != "about:blank" &&
            !UrlResolver.IsShortUrl(_currentUrl, ConfigService.Instance.Current.UrlShorteners))
        {
            BrowserLauncher.SaveRuleForDomain(_parsedInfo.Domain, browser, profileId);
        }

        BrowserLauncher.Launch(browser, _currentUrl, profileId, isPrivacy);
        Close();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        PerformCopy();
    }

    private void PerformCopy()
    {
        try
        {
            Clipboard.SetText(_currentUrl);
            CopyLabelText.Text = "已复制 ✓";
            CopyLabelText.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));

            Task.Delay(1500).ContinueWith(_ =>
            {
                try
                {
                    if (!Dispatcher.HasShutdownStarted)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            CopyLabelText.Text = "复制";
                            CopyLabelText.ClearValue(TextBlock.ForegroundProperty);
                        });
                    }
                }
                catch { }
            });
        }
        catch
        {
            // Ignore clipboard errors
        }
    }

    private void EditBrowserContextMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is BrowserItem browser)
        {
            _isModalDialogOpen = true;
            try
            {
                var dialog = new BrowserEditDialog(browser)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() == true)
                {
                    ConfigService.Instance.Save();
                    ReloadBrowsersList();
                }
            }
            finally
            {
                _isModalDialogOpen = false;
            }
        }
    }

    private void OpenBrowserLocationContextMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is BrowserItem browser)
        {
            if (!string.IsNullOrEmpty(browser.ExePath) && System.IO.File.Exists(browser.ExePath))
            {
                try
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{browser.ExePath}\"");
                }
                catch { }
            }
        }
    }

    private void ReloadBrowsersList()
    {
        ConfigService.Instance.RefreshRuntimeState();
        _browsers = ConfigService.Instance.Current.Browsers
            .Where(b => !b.IsDisabled && System.IO.File.Exists(b.ExePath))
            .OrderBy(b => b.SortOrder)
            .ToList();

        BrowsersItemsControl.ItemsSource = null;
        BrowsersItemsControl.ItemsSource = _browsers;

        if (EmptyBrowsersNotice != null)
        {
            EmptyBrowsersNotice.Visibility = _browsers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _isOpeningSettings = true;
        var settingsWindow = new SettingsWindow();
        settingsWindow.Owner = this;
        settingsWindow.ShowDialog();
        _isOpeningSettings = false;

        ReloadBrowsersList();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _resolveCts?.Cancel();
    }
}
