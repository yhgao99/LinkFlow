using System;
using System.Windows;
using System.Windows.Input;
using LinkFlow.Models;

namespace LinkFlow.Views;

public partial class QuickHotkeyDialog : Window
{
    private readonly BrowserItem _browser;
    private string? _selectedHotkey;

    public QuickHotkeyDialog(BrowserItem browser)
    {
        InitializeComponent();
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));

        BrowserIconImage.Source = browser.Icon;
        BrowserNameText.Text = browser.DisplayNameWithTag;

        _selectedHotkey = browser.CustomHotkey ?? browser.HotkeyLabel;
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        if (string.IsNullOrWhiteSpace(_selectedHotkey))
        {
            HotkeyDisplayText.Text = "无";
            HotkeyDisplayText.Foreground = System.Windows.Media.Brushes.Gray;
        }
        else
        {
            HotkeyDisplayText.Text = _selectedHotkey;
            HotkeyDisplayText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x38, 0xBD, 0xF8)
            );
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
            return;
        }

        if (key == Key.Enter)
        {
            SaveAndClose();
            e.Handled = true;
            return;
        }

        if (key is Key.Back or Key.Delete)
        {
            _selectedHotkey = null;
            UpdateDisplay();
            e.Handled = true;
            return;
        }

        // Numbers 0-9
        if (key >= Key.D0 && key <= Key.D9)
        {
            _selectedHotkey = ((int)key - (int)Key.D0).ToString();
            UpdateDisplay();
            e.Handled = true;
            return;
        }
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            _selectedHotkey = ((int)key - (int)Key.NumPad0).ToString();
            UpdateDisplay();
            e.Handled = true;
            return;
        }

        // Letters A-Z
        if (key >= Key.A && key <= Key.Z)
        {
            _selectedHotkey = key.ToString().ToUpperInvariant();
            UpdateDisplay();
            e.Handled = true;
            return;
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedHotkey = null;
        UpdateDisplay();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        SaveAndClose();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SaveAndClose()
    {
        _browser.CustomHotkey = string.IsNullOrWhiteSpace(_selectedHotkey) ? null : _selectedHotkey.Trim().ToUpperInvariant();
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
}
