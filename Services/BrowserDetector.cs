using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using LinkFlow.Models;
using Microsoft.Win32;

namespace LinkFlow.Services;

public static class BrowserDetector
{
    // 精确已知的浏览器主执行文件名（必须全名匹配，杜绝模糊包含导致误判）
    private static readonly HashSet<string> KnownBrowserExeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // 主流国际浏览器
        "chrome.exe",
        "msedge.exe",
        "firefox.exe",
        "brave.exe",
        "opera.exe",
        "vivaldi.exe",
        "chromium.exe",

        // 国内主流浏览器 (精确主程序文件名)
        "360se.exe",        // 360安全浏览器
        "360chrome.exe",    // 360极速浏览器
        "360chromex.exe",   // 360极速浏览器X
        "qqbrowser.exe",    // QQ浏览器
        "sogouexplorer.exe",// 搜狗浏览器
        "liebao.exe",       // 猎豹浏览器
        "2345explorer.exe", // 2345加速浏览器
        "quark.exe",        // 夸克浏览器
        "uc.exe",           // UC浏览器
        "doubao.exe",       // 豆包
        "tabbit browser.exe", // Tabbit浏览器
        "tabbit.exe",
        "maxthon.exe",      // 傲游浏览器
        "theworld.exe",     // 世界之窗
        "catsxp.exe",       // 猫眼浏览器
        "centbrowser.exe",  // 百分浏览器 (CentBrowser)
        "cent.exe",

        // 特色/开源/隐私浏览器
        "zen.exe",          // Zen Browser
        "floorp.exe",       // Floorp
        "thorium.exe",      // Thorium
        "waterfox.exe",     // Waterfox
        "librewolf.exe",    // LibreWolf
        "torbrowser.exe",   // Tor Browser
        "arc.exe",          // Arc Browser
        "yandex.exe",       // Yandex Browser
        "duckduckgo.exe",   // DuckDuckGo
        "duckduckgo_browser.exe",
        "min.exe"           // Min Browser
    };

    // 绝对黑名单：系统工具、辅助进程、安装包、杀毒卫士、调试监控等绝非浏览器的程序
    private static readonly HashSet<string> ExcludedExeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // 自身与遗留老旧组件
        "browserpicker.exe",
        "fluentpicker.exe",
        "linkflow.exe",
        "iexplore.exe",

        // 常见杀软、安全卫士、管家等（极易因包含"360"等关键词被误扫）
        "360safe.exe",
        "360tray.exe",
        "360sd.exe",
        "safemon.exe",
        "360realpro.exe",
        "softmgr.exe",
        "360leakfixer.exe",
        "360speed.exe",
        "zhudongfangyu.exe",
        "qax.exe",
        "huorong.exe",
        "wsctrl.exe",
        "hrdaemon.exe",
        "usysdiag.exe",
        "qqpcmgr.exe",
        "kws.exe",
        "kingsoft.exe",

        // 显卡、系统工具、IDE与开发调试监控工具（避免包含"tor"或"cent"误扫）
        "monitor.exe",
        "nsight.exe",
        "nvidiashare.exe",
        "nvcontainer.exe",
        "devenv.exe",
        "code.exe",
        "windbg.exe",
        "appcertui.exe",
        "gpuview.exe",
        "wpa.exe",
        "wprui.exe",

        // 系统标准工具
        "cmd.exe",
        "powershell.exe",
        "pwsh.exe",
        "conhost.exe",
        "explorer.exe",
        "notepad.exe",
        "regedit.exe",
        "taskmgr.exe",
        "mmc.exe",
        "control.exe"
    };

    public static List<BrowserItem> DetectInstalledBrowsers()
    {
        var result = new Dictionary<string, BrowserItem>(StringComparer.OrdinalIgnoreCase);

        // 1. Scan Registry StartMenuInternet (官方浏览器标准注册表，极高权威性)
        ScanRegistry(Registry.CurrentUser, result);
        ScanRegistry(Registry.LocalMachine, result);

        // 2. Scan Well-Known Paths (主流知名浏览器标准路径)
        ScanWellKnownPaths(result);

        // 3. Scan Standard Desktop and StartMenu Shortcuts (桌面与开始菜单快捷方式，强过滤)
        ScanStandardShortcuts(result);

        var list = new List<BrowserItem>(result.Values);

        // 最终严格过滤：排除无效路径及非浏览器项
        list.RemoveAll(b => !IsValidBrowserExecutable(b.ExePath, b.Name));

        // 自动探测 Chrome / Edge 的多用户 Profile（仅对无自定义参数的条目生效）
        foreach (var b in list)
        {
            if (b.Profiles.Count == 0 && string.IsNullOrWhiteSpace(b.CommandArgs))
            {
                DetectProfiles(b);
            }
        }

        return list;
    }

    /// <summary>
    /// 严谨判定指定文件是否为真正的浏览器主程序，杜绝误扫
    /// </summary>
    public static bool IsValidBrowserExecutable(string? targetExe, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(targetExe) || !File.Exists(targetExe))
            return false;

        // 1. 扩展名必须严格是 .exe（彻底杜绝 .url、.lnk 快捷方式文件本身）
        if (!string.Equals(Path.GetExtension(targetExe), ".exe", StringComparison.OrdinalIgnoreCase))
            return false;

        var fileName = Path.GetFileName(targetExe).ToLowerInvariant();

        // 2. 命中绝对黑名单则直接排除
        if (ExcludedExeNames.Contains(fileName))
            return false;

        // 3. 排除安装、卸载、崩溃收集、后台守护、托盘助手等附属子进程
        if (fileName.Contains("uninstall") ||
            fileName.Contains("installer") ||
            fileName.Contains("setup") ||
            fileName.Contains("update") ||
            fileName.Contains("crashpad") ||
            fileName.Contains("pingsender") ||
            fileName.Contains("helper") ||
            fileName.Contains("daemon") ||
            fileName.Contains("safemon") ||
            fileName.Contains("360safe") ||
            fileName.Contains("softmgr") ||
            fileName.Contains("chrome_proxy"))
        {
            return false;
        }

        // 4. 命中已知浏览器主程序白名单（精确全名匹配）
        if (KnownBrowserExeNames.Contains(fileName))
            return true;

        // 5. 命中通用浏览器规范命名模式 (以 browser.exe 结尾，或包含 -browser.exe 等)
        if (fileName.EndsWith("browser.exe", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("-browser.exe", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("_browser.exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 6. 特殊情况：Opera 启动器 launcher.exe (仅当所在目录包含 Opera 时算作浏览器)
        if (fileName == "launcher.exe" && targetExe.Contains("Opera", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 7. 若上述均未直接命中，但快捷方式/显示名称中明确带有“浏览器”或“Browser”
        //    通过读取可执行文件的 FileVersionInfo PE 元数据进行权威双重校验
        var dn = (displayName ?? "").ToLowerInvariant();
        if (dn.Contains("浏览器") || dn.Contains("browser"))
        {
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(targetExe);
                var meta = $"{vi.ProductName} {vi.FileDescription} {vi.CompanyName}".ToLowerInvariant();

                // 元数据中包含浏览器相关描述
                bool metaIsBrowser = meta.Contains("browser") ||
                                     meta.Contains("浏览器") ||
                                     meta.Contains("web") ||
                                     meta.Contains("chromium") ||
                                     meta.Contains("firefox") ||
                                     meta.Contains("navigator");

                // 且元数据中不属于安全软件、杀毒、监控工具
                bool metaIsSafeOrTool = meta.Contains("安全卫士") ||
                                        meta.Contains("防护") ||
                                        meta.Contains("antivirus") ||
                                        meta.Contains("monitor") ||
                                        meta.Contains("visual studio");

                if (metaIsBrowser && !metaIsSafeOrTool)
                {
                    return true;
                }
            }
            catch
            {
                // 读取失败则保守拒绝
            }
        }

        return false;
    }

    private static void ScanRegistry(RegistryKey rootKey, Dictionary<string, BrowserItem> result)
    {
        try
        {
            using var internetKey = rootKey.OpenSubKey(@"SOFTWARE\Clients\StartMenuInternet");
            if (internetKey == null) return;

            foreach (var browserKeyName in internetKey.GetSubKeyNames())
            {
                using var browserKey = internetKey.OpenSubKey(browserKeyName);
                if (browserKey == null) continue;

                var name = browserKey.GetValue(null) as string ?? browserKeyName;
                using var cmdKey = browserKey.OpenSubKey(@"shell\open\command");
                if (cmdKey == null) continue;

                var cmd = cmdKey.GetValue(null) as string;
                if (string.IsNullOrWhiteSpace(cmd)) continue;

                var exePath = CleanExePath(cmd);
                if (!IsValidBrowserExecutable(exePath, name)) continue;

                var key = $"{exePath}::";
                if (!result.ContainsKey(key))
                {
                    result[key] = CreateBrowserItem(name, exePath);
                }
            }
        }
        catch
        {
            // Ignore registry permissions / missing keys
        }
    }

    private static void ScanWellKnownPaths(Dictionary<string, BrowserItem> result)
    {
        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var candidates = new (string Name, string Path)[]
        {
            ("Google Chrome", Path.Combine(programFiles, @"Google\Chrome\Application\chrome.exe")),
            ("Google Chrome", Path.Combine(programFilesX86, @"Google\Chrome\Application\chrome.exe")),
            ("Microsoft Edge", Path.Combine(programFilesX86, @"Microsoft\Edge\Application\msedge.exe")),
            ("Microsoft Edge", Path.Combine(programFiles, @"Microsoft\Edge\Application\msedge.exe")),
            ("Microsoft Edge Beta", Path.Combine(programFilesX86, @"Microsoft\Edge Beta\Application\msedge.exe")),
            ("Mozilla Firefox", Path.Combine(programFiles, @"Mozilla Firefox\firefox.exe")),
            ("Brave", Path.Combine(programFiles, @"BraveSoftware\Brave-Browser\Application\brave.exe")),
            ("Zen Browser", Path.Combine(programFiles, @"Zen Browser\zen.exe")),
            ("Zen Browser", Path.Combine(localApp, @"Zen Browser\zen.exe")),
            ("Arc", Path.Combine(localApp, @"Arc\Arc.exe")),
            ("Floorp", Path.Combine(programFiles, @"Floorp\floorp.exe")),
            ("Floorp", Path.Combine(localApp, @"Floorp\floorp.exe")),
            ("Thorium", Path.Combine(localApp, @"Thorium\Application\thorium.exe")),
            ("Vivaldi", Path.Combine(localApp, @"Vivaldi\Application\vivaldi.exe")),
            ("Opera", Path.Combine(localApp, @"Programs\Opera\launcher.exe")),
            ("Opera GX", Path.Combine(localApp, @"Programs\Opera GX\launcher.exe")),
            ("豆包", @"D:\Programs\Doubao\app\Doubao.exe"),
            ("豆包", @"D:\Programs\Doubao\Doubao.exe"),
            ("夸克", @"D:\Programs\Quark\quark.exe"),
            ("360安全浏览器", Path.Combine(appData, @"secoresdk\360se6\Application\360se.exe")),
            ("360极速浏览器", Path.Combine(localApp, @"360Chrome\Chrome\Application\360chrome.exe")),
            ("360极速浏览器X", Path.Combine(localApp, @"360ChromeX\Chrome\Application\360chromex.exe")),
            ("Tabbit 浏览器", Path.Combine(localApp, @"Tabbit Browser\Application\Tabbit Browser.exe")),
            ("UC浏览器", Path.Combine(programFiles, @"UC浏览器\uc.exe")),
            ("UC浏览器", Path.Combine(programFilesX86, @"UC浏览器\uc.exe"))
        };

        foreach (var (name, path) in candidates)
        {
            var key = $"{path}::";
            if (File.Exists(path) && IsValidBrowserExecutable(path, name) && !result.ContainsKey(key))
            {
                result[key] = CreateBrowserItem(name, path);
            }
        }
    }

    private static void ScanStandardShortcuts(Dictionary<string, BrowserItem> result)
    {
        var standardFolders = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        };

        foreach (var folder in standardFolders.Where(Directory.Exists))
        {
            var items = ScanFolderForBrowsers(folder);
            foreach (var item in items)
            {
                var key = $"{item.ExePath}::{item.CommandArgs ?? ""}";
                if (!result.ContainsKey(key))
                {
                    result[key] = item;
                }
            }
        }
    }

    public static List<BrowserItem> ScanFolderForBrowsers(string folderPath)
    {
        var result = new List<BrowserItem>();
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return result;

        try
        {
            var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            foreach (var file in files)
            {
                var item = CreateItemFromFileOrShortcut(file);
                if (item != null)
                {
                    result.Add(item);
                }
            }
        }
        catch
        {
            // Ignore access errors on restricted subdirectories
        }

        return result;
    }

    public static BrowserItem? CreateItemFromFileOrShortcut(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        string targetExe = filePath;
        string? commandArgs = null;
        string name = Path.GetFileNameWithoutExtension(filePath);
        string iconPath = filePath;

        if (filePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var shortcut = ShortcutHelper.ResolveShortcut(filePath);
            if (shortcut == null || string.IsNullOrWhiteSpace(shortcut.TargetPath) || !File.Exists(shortcut.TargetPath))
                return null;

            targetExe = shortcut.TargetPath;
            commandArgs = string.IsNullOrWhiteSpace(shortcut.Arguments) ? null : shortcut.Arguments.Trim();
            name = shortcut.Name;
            iconPath = !string.IsNullOrWhiteSpace(shortcut.IconLocation) && File.Exists(shortcut.IconLocation.Split(',')[0])
                ? shortcut.IconLocation.Split(',')[0]
                : targetExe;
        }

        // 严格检验目标必须是真实的浏览器主程序
        if (!IsValidBrowserExecutable(targetExe, name))
        {
            return null;
        }

        var fileName = Path.GetFileName(targetExe).ToLowerInvariant();

        // 优化显示名称
        string displayName;
        if (fileName.Contains("chrome") && !name.Equals("Google Chrome", StringComparison.OrdinalIgnoreCase))
        {
            displayName = name.StartsWith("chrome", StringComparison.OrdinalIgnoreCase)
                ? name
                : $"Chrome ({name})";
        }
        else if (fileName.Contains("doubao") && commandArgs?.Contains("browser") == true)
        {
            displayName = "豆包浏览器";
        }
        else
        {
            displayName = CleanBrowserName(name);
        }

        var item = new BrowserItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = displayName,
            ExePath = targetExe,
            CommandArgs = commandArgs,
            IconPath = iconPath,
            PrivacyArgs = GetDefaultPrivacyArgs(targetExe, displayName)
        };

        return item;
    }

    private static string? GetDefaultPrivacyArgs(string exePath, string name)
    {
        var fileName = Path.GetFileName(exePath).ToLowerInvariant();
        var lowerName = name.ToLowerInvariant();

        if (fileName.Contains("msedge") || lowerName.Contains("edge"))
            return "-inprivate";
        if (fileName.Contains("firefox") || fileName.Contains("zen") || fileName.Contains("floorp") ||
            fileName.Contains("waterfox") || fileName.Contains("librewolf") || lowerName.Contains("firefox"))
            return "-private-window";
        if (fileName.Contains("opera") || lowerName.Contains("opera"))
            return "--private";
        if (fileName.Contains("tor"))
            return null;

        return "--incognito";
    }

    private static BrowserItem CreateBrowserItem(string name, string exePath)
    {
        var cleaned = CleanBrowserName(name);
        var item = new BrowserItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = cleaned,
            OriginalName = cleaned,
            ExePath = exePath,
            IconPath = exePath,
            PrivacyArgs = GetDefaultPrivacyArgs(exePath, cleaned)
        };

        return item;
    }

    private static void DetectProfiles(BrowserItem browser)
    {
        var fileName = Path.GetFileName(browser.ExePath).ToLowerInvariant();
        string? userDataDir = null;

        if (fileName.Contains("chrome"))
        {
            userDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\User Data");
        }
        else if (fileName.Contains("msedge"))
        {
            userDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\User Data");
        }

        if (userDataDir == null || !Directory.Exists(userDataDir))
            return;

        var localStatePath = Path.Combine(userDataDir, "Local State");
        if (!File.Exists(localStatePath))
            return;

        try
        {
            var json = File.ReadAllText(localStatePath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("profile", out var profileElem) &&
                profileElem.TryGetProperty("info_cache", out var infoCache))
            {
                foreach (var prop in infoCache.EnumerateObject())
                {
                    var profileDir = prop.Name;
                    var profileName = profileDir;
                    if (prop.Value.TryGetProperty("name", out var nameVal))
                    {
                        profileName = nameVal.GetString() ?? profileDir;
                    }

                    browser.Profiles.Add(new BrowserProfileItem
                    {
                        Id = profileDir,
                        Name = profileName,
                        Args = $"--profile-directory=\"{profileDir}\""
                    });
                }
            }
        }
        catch
        {
            // Ignore profile parse error
        }
    }

    private static string CleanExePath(string raw)
    {
        var cleaned = raw.Trim();
        if (cleaned.StartsWith("\""))
        {
            var nextQuote = cleaned.IndexOf('\"', 1);
            if (nextQuote > 1)
            {
                return cleaned.Substring(1, nextQuote - 1);
            }
        }

        // Try finding .exe location to avoid cutting at "Program Files" spaces
        var exeIdx = cleaned.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIdx > 0)
        {
            var candidate = cleaned.Substring(0, exeIdx + 4).Trim('\"', '\'');
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var spaceIdx = cleaned.IndexOf(' ');
        if (spaceIdx > 0 && File.Exists(cleaned.Substring(0, spaceIdx)))
        {
            return cleaned.Substring(0, spaceIdx);
        }

        if (exeIdx > 0)
        {
            return cleaned.Substring(0, exeIdx + 4).Trim('\"', '\'');
        }

        return cleaned.Trim('\"', '\'');
    }

    private static string CleanBrowserName(string raw)
    {
        return raw.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();
    }
}
