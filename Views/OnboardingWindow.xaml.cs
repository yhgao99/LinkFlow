using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LinkFlow.Services;

namespace LinkFlow.Views;

public partial class OnboardingWindow : Window
{
    public OnboardingWindow()
    {
        InitializeComponent();
        Loaded += OnboardingWindow_Loaded;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeMethods.ApplyWindowStylesAndIcon(this);
    }

    private void OnboardingWindow_Loaded(object sender, RoutedEventArgs e)
    {
        CheckDefaultStatus();
        DoNotShowAgainCheck.IsChecked = ConfigService.Instance.Current.HasCompletedOnboarding;
    }

    private void CheckDefaultStatus()
    {
        bool isDefault = SystemRegistration.IsDefaultBrowser();

        if (isDefault)
        {
            DefaultStatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2010B981"));
            DefaultStatusBadge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4010B981"));
            DefaultStatusText.Text = "✅ 已成功设为系统默认浏览器 (准备就绪)";
            DefaultStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));
            DoNotShowAgainCheck.IsChecked = true;
        }
        else
        {
            DefaultStatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20F59E0B"));
            DefaultStatusBadge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#40F59E0B"));
            DefaultStatusText.Text = "⚠️ 尚未设为默认浏览器 (点击下方按钮设置)";
            DefaultStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FBBF24"));
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void SetDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        var regOk = SystemRegistration.RegisterAsWebBrowser();
        SystemRegistration.OpenDefaultAppsSettings();

        MessageBox.Show(
            "系统注册指令已下发，并为您打开了 Windows「默认应用」页面！\n\n【傻瓜式操作提示】：\n1. 请在打开的 Windows 设置中，找到「Web 浏览器」\n2. 点击并将其更改为「LinkFlow」\n\n设置完成后，回到此窗口点击「刷新检测状态」即可！",
            "已开启 Windows 设置",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );

        CheckDefaultStatus();
    }

    private void CheckStatusButton_Click(object sender, RoutedEventArgs e)
    {
        CheckDefaultStatus();
        if (SystemRegistration.IsDefaultBrowser())
        {
            MessageBox.Show("恭喜！LinkFlow 已经是系统默认浏览器！\n现在在任意第三方软件中点击链接，都能享受智能分流体验。", "检测结果", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("当前仍未检测到设为默认浏览器，请在弹出的 Windows「默认应用」页面中将 Web 浏览器切换为 LinkFlow。", "检测结果", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void TestOpenGeneralLink_Click(object sender, RoutedEventArgs e)
    {
        var picker = new PickerWindow("https://www.bing.com/search?q=LinkFlow");
        picker.Show();
    }

    private void TestOpenRuleLink_Click(object sender, RoutedEventArgs e)
    {
        const string testUrl = "https://github.com/microsoft/terminal";
        var match = RuleMatcher.Match(testUrl, ConfigService.Instance.Current);
        if (match != null)
        {
            var res = MessageBox.Show(
                $"命中已启用的规则「{match.MatchedRule.Pattern}」！\n是否立即调用【{match.Browser.Name}】打开此链接验证？",
                "规则分流测试",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information
            );

            if (res == MessageBoxResult.Yes)
            {
                BrowserLauncher.Launch(match.Browser, testUrl, match.ProfileId, false);
            }
        }
        else
        {
            var picker = new PickerWindow(testUrl);
            picker.Show();
        }
    }

    private void OpenSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SaveOnboardingPreference();
        var settings = new SettingsWindow();
        settings.Show();
        Close();
    }

    private void FinishButton_Click(object sender, RoutedEventArgs e)
    {
        SaveOnboardingPreference();
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        SaveOnboardingPreference();
        Close();
    }

    private void SaveOnboardingPreference()
    {
        ConfigService.Instance.Current.HasCompletedOnboarding = DoNotShowAgainCheck.IsChecked ?? true;
        ConfigService.Instance.Save();
    }
}
