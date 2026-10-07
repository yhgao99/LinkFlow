<div align="center">

<img src="app_icon.png" width="96" height="96" alt="LinkFlow Logo" />

# LinkFlow 🌐

**专为 Windows 11 设计的现代化、闪电响应式智能浏览器路由与外链分流利器**

*Fluent Browser Router & Smart URL Dispatcher for Windows 11*

[![Release](https://img.shields.io/github/v/release/yhgao99/LinkFlow?color=blue)](https://github.com/yhgao99/LinkFlow/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET 10.0](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2011%20%7C%2010-0078D6.svg)](https://microsoft.com)
[![WPF-UI](https://img.shields.io/badge/UI-WPF--UI%20Fluent%202-005fb8.svg)](https://github.com/lepoco/wpfui)

</div>

---

## 💡 为什么需要 LinkFlow？

在日常开发与办公中，你是否经常遇到以下痛点：
- 微信/钉钉里点击链接，总想用轻量级浏览器或特定工作浏览器打开；
- 开发调试 `localhost:3000` 希望默认唤起 Chrome，而阅读技术文档希望用 Edge；
- 多账号隔离：公司的 Google/GitHub 账号在一个浏览器 Profile，个人账号在另一个 Profile；
- 系统默认的打开方式弹窗卡顿、丑陋且无法配置自动化规则。

**LinkFlow** 专为解决此痛点而生：
以 Windows 默认浏览器身份无缝拦截外链，**命中有规则的网址后台静默秒级直开**（零窗口闪烁）；未命中规则时瞬间在光标旁弹窗，**支持单键直达、全键盘导航与一键隐私模式**。让所有外链像流水一样自然流转至最佳目的地。

---

## ✨ 核心特性

### 1. 🎨 Windows 11 Fluent 2.0 现代美学
- 原生 DWM 16px 优雅圆角、深度毛玻璃亚克力与平滑悬浮阴影。
- 自动聚焦于鼠标光标位置弹出，并带有多显示器工作区防边缘溢出保护。
- **多主题支持**：内置「🌓 跟随系统」、「🌙 优雅深色」、「☀️ 清新浅色」三种模式，即时预览切换。

### 2. ⚡ 极致性能（闪电启动）
- 基于 原生 **.NET 10 + WPF** 深度优化，支持 ReadyToRun 预编译启动。
- **Fast-Path 秒开机制**：命中有规则的域名时耗时 **< 20ms**，直接在后台静默直调目标浏览器，**零窗口闪烁**。
- 单文件便携版体积仅约 1MB，内存占用微乎其微。

### 3. 🎯 智能分流规则引擎
- 支持 **主域名 (Domain)**：自动包含主域名及其所有子域名（如 `github.com`、`*.google.com`）。
- 支持 **精确主机 (Hostname)**：区分开发端口（如 `localhost:3000` vs `localhost:8080`）。
- 支持 **网址前缀 (Prefix)**：智能兼容带或不带 `https://`（如 `gitlab.company.com/frontend/`）。
- 支持 **关键词包含 (Contains)** 与 **高级正则表达式 (Regex)**。
- **规则批量导入**：支持分号 `;` 或逗号 `,` 一次性输入多个域名批量添加规则。

### 4. 🔗 短网址智能重定向联动
- 内置针对 `t.co`、`bit.ly`、`aka.ms` 等短链服务商的极速预解析。
- **两阶段解析**：启动瞬间 800ms 内静默展开；若网络慢弹出窗口后，后台异步继续展开并在获取真实目标后**自动二次比对规则并秒级关闭跳转**。
- 支持在设置中心自定义自建短链服务商域名。

### 5. 🛡 兜底默认浏览器与防弹窗干扰
- **兜底分流**：可配置“未命中任何规则时直接由指定的兜底默认浏览器打开”，无需频繁弹窗打扰。
- **Chromium 防打扰**：调用 Chrome、Edge 时自动注入 `--no-default-browser-check`，防止浏览器弹窗询问设为默认。

### 6. 👥 浏览器多配置文件 (Profiles)
- 自动扫描识别 Chrome、Edge 等的多用户 Profile（工作号、个人号、分身环境）。
- 支持卡片直接唤起特定 Profile 或单独抽出为独立卡片。
- 支持浏览器卡片一键「⏸ 启用 / 停用」，临时禁用无需删除配置。

### 7. ⌨️ 全键盘流极速操作
| 按键 | 功能 |
| :--- | :--- |
| `1` ~ `9`, `0` | 一键秒开对应排名的浏览器 |
| `Alt + 1` ~ `Alt + 9` | 对应浏览器以 **无痕 / 隐私模式 (Incognito / InPrivate)** 打开 |
| `↑` / `↓` | 光标循环切换选中的浏览器卡片 |
| `Enter` | 启动当前高亮选中的浏览器 |
| `Alt + Enter` | 以隐私模式启动当前高亮选中的浏览器 |
| `Space (空格)` | 快速勾选/取消勾选「记住此域名的选择」 |
| `Ctrl + C` | 一键复制当前访问的完整网址 |
| `Esc` | 退出并关闭弹窗 |

---

## 🚀 安装与使用

### 1. 下载程序
前往 [GitHub Releases 页面](https://github.com/yhgao99/LinkFlow/releases) 下载最新发行版：
- **`LinkFlow-portable-x64.zip`（轻量便携版，约 1MB）**：适合电脑已安装 .NET 10 运行时的用户。
- **`LinkFlow-standalone-x64.zip`（独立绿色版，约 60MB）**：自带完整运行环境，无需安装任何前置依赖，解压即用。

### 2. 设为系统默认浏览器
1. 打开 `LinkFlow.exe`（不带任何外链参数启动时，将直接进入**控制中心**）。
2. 在「常规偏好与系统」页面中，点击 **“重新注册系统协议”**。
3. 点击 **“打开 Windows「默认应用」设置”**，在系统列表中将默认 Web 浏览器选择为 **LinkFlow** 即可！

> 💡 **提示**：注册仅写入当前用户的注册表键值（`HKEY_CURRENT_USER`），**无需管理员权限**，绿色且安全。想要卸载时随时在设置页面点击“注销系统协议关联”即可一键干净清理。

---

## 🛠 本地编译与构建

### 前置环境
- Windows 10 (1809+) 或 Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或更高版本

### 常用命令
```powershell
# 1. 克隆仓库
git clone https://github.com/yhgao99/LinkFlow.git
cd LinkFlow

# 2. 还原与编译
dotnet build -c Release

# 3. 运行调试
dotnet run

# 4. 一键打包发布 (产出便携版与独立单文件版)
pwsh ./build_release.ps1
```

---

## 📁 项目架构

```
LinkFlow/
├── Models/              # 数据实体定义 (BrowserItem, AppConfig, RoutingRule)
├── Services/            # 核心业务服务
│   ├── BrowserDetector.cs     # 系统已安装浏览器与 Profiles 扫描
│   ├── BrowserLauncher.cs     # 浏览器安全进程拉起与参数注入
│   ├── ConfigService.cs       # 配置原子写入与多进程同步
│   ├── RuleMatcher.cs         # 规则匹配核心引擎 (Domain, Regex, Prefix)
│   ├── SystemRegistration.cs  # Windows Default Programs 协议注册
│   └── UrlResolver.cs         # 网址拆解、域名提取与短链展开
├── Views/               # Fluent 界面视图
│   ├── PickerWindow.xaml      # 现代化外链选择器悬浮窗
│   ├── SettingsWindow.xaml    # 规则与偏好设置中心
│   ├── BrowserEditDialog.xaml # 浏览器属性与参数编辑对话框
│   └── OnboardingWindow.xaml  # 初次启动引导设置向导
├── App.xaml / App.xaml.cs     # 应用启动生命周期与外链 Fast-Path
└── build_release.ps1          # 本地一键打包发布脚本
```

---

## 📄 开源许可证

本项目基于 [MIT License](LICENSE) 开源。欢迎提交 Issue 与 Pull Request！
