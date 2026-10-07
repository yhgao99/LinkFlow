using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LinkFlow.Models;
using LinkFlow.Services;

namespace LinkFlow.Views;

public partial class BrowserEditDialog : Window
{
    private readonly BrowserItem _browser;

    public BrowserEditDialog(BrowserItem browser)
    {
        InitializeComponent();
        _browser = browser;

        BrowserIconImage.Source = _browser.Icon;
        PreviewIconImage.Source = _browser.Icon;
        OriginalPathBlock.Text = _browser.ExePath;

        NameTextBox.Text = _browser.Name;
        TagTextBox.Text = _browser.Tag ?? string.Empty;
        IsEnabledSwitch.IsChecked = _browser.IsEnabled;
        HotkeyTextBox.Text = _browser.CustomHotkey ?? string.Empty;
        PrivacyArgsTextBox.Text = _browser.PrivacyArgs ?? string.Empty;
        ArgsTextBox.Text = _browser.CommandArgs ?? string.Empty;

        // Auto-expand advanced section if user has existing custom hotkey or custom args
        bool hasAdvancedCustomization = !string.IsNullOrWhiteSpace(_browser.CustomHotkey) ||
                                       !string.IsNullOrWhiteSpace(_browser.CommandArgs);
        SetAdvancedSectionVisibility(hasAdvancedCustomization);

        UpdatePreview();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeMethods.ApplyWindowStylesAndIcon(this);
    }

    private void Input_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (PreviewNameText == null || PreviewTagBadge == null || PreviewTagText == null) return;

        var name = NameTextBox.Text?.Trim();
        PreviewNameText.Text = !string.IsNullOrWhiteSpace(name) ? name : _browser.EffectiveOriginalName;

        var tag = TagTextBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(tag))
        {
            PreviewTagBadge.Visibility = Visibility.Visible;
            PreviewTagText.Text = tag;
        }
        else
        {
            PreviewTagBadge.Visibility = Visibility.Collapsed;
        }

        var hotkey = HotkeyTextBox?.Text?.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(hotkey) && PreviewHotkeyBadge != null && PreviewHotkeyText != null)
        {
            PreviewHotkeyBadge.Visibility = Visibility.Visible;
            PreviewHotkeyText.Text = hotkey;
        }
        else if (PreviewHotkeyBadge != null)
        {
            PreviewHotkeyBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void PresetTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            TagTextBox.Text = tag;
            TagTextBox.Focus();
        }
    }

    private void ResetNameButton_Click(object sender, RoutedEventArgs e)
    {
        NameTextBox.Text = _browser.EffectiveOriginalName;
        NameTextBox.Focus();
        NameTextBox.SelectAll();
        UpdatePreview();
    }

    private void AdvancedSectionToggle_Click(object sender, RoutedEventArgs e)
    {
        bool willBeVisible = AdvancedSectionPanel.Visibility != Visibility.Visible;
        SetAdvancedSectionVisibility(willBeVisible);
    }

    private void SetAdvancedSectionVisibility(bool isVisible)
    {
        AdvancedSectionPanel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        AdvancedToggleArrow.Text = isVisible ? "▼" : "▶";
    }

    private void HotkeyTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Allow tab to switch focus
        if (key == Key.Tab)
        {
            return;
        }

        // Backspace, Delete or Escape to clear
        if (key is Key.Back or Key.Delete or Key.Escape)
        {
            HotkeyTextBox.Text = string.Empty;
            e.Handled = true;
            return;
        }

        // Numbers 0-9
        if (key >= Key.D0 && key <= Key.D9)
        {
            HotkeyTextBox.Text = ((int)key - (int)Key.D0).ToString();
            e.Handled = true;
            return;
        }
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            HotkeyTextBox.Text = ((int)key - (int)Key.NumPad0).ToString();
            e.Handled = true;
            return;
        }

        // Letters A-Z
        if (key >= Key.A && key <= Key.Z)
        {
            HotkeyTextBox.Text = key.ToString();
            e.Handled = true;
            return;
        }

        // Suppress any other random keys
        e.Handled = true;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var customName = NameTextBox.Text?.Trim();
        _browser.Name = !string.IsNullOrWhiteSpace(customName) ? customName : _browser.EffectiveOriginalName;

        var tag = TagTextBox.Text?.Trim();
        _browser.Tag = !string.IsNullOrWhiteSpace(tag) ? tag : null;

        _browser.IsDisabled = !(IsEnabledSwitch.IsChecked ?? true);

        var hotkey = HotkeyTextBox.Text?.Trim().ToUpperInvariant();
        _browser.CustomHotkey = !string.IsNullOrWhiteSpace(hotkey) ? hotkey : null;

        var privArgs = PrivacyArgsTextBox.Text?.Trim();
        _browser.PrivacyArgs = !string.IsNullOrWhiteSpace(privArgs) ? privArgs : null;

        var args = ArgsTextBox.Text?.Trim();
        _browser.CommandArgs = !string.IsNullOrWhiteSpace(args) ? args : null;

        DialogResult = true;
        Close();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
