# TabletModeSwitcher 平板模式切换器

一个 Windows 11 系统托盘应用程序，可以根据键盘的连接和断开自动切换平板模式任务栏。

## 背景

Windows 11 提供了一个很好的平板模式优化功能：在设置中勾选「当此设备用作平板电脑时，优化任务栏以进行触控交互」后，当设备处于平板模式时，任务栏会自动收起成为一个小白条，通过手指从底部向上滑动可以打开开始菜单。

然而，这个功能只对 Surface 官方键盘盖有效。当连接蓝牙键盘或 USB 有线键盘时，系统不会自动切换到桌面模式的固定任务栏。

**本工具解决了这个问题**，通过监听键盘设备的连接和断开事件，自动修改系统的 `ConvertibleSlateMode` 注册表值，从而触发平板模式的切换。

## 功能特性

- 🎹 **自动检测键盘连接** - 支持 USB 有线键盘和蓝牙键盘
- 🔄 **自动模式切换** - 键盘连接时切换到桌面模式，断开时切换到平板模式
- ⚙️ **可视化设置界面** - 双击托盘图标打开设置窗口
- 🚀 **开机自启动** - 可选开机自动运行
- 📋 **设备排除列表** - 可排除特定设备（如蓝牙遥控器）不触发切换
- ⏱️ **防抖动延迟** - 可配置切换延迟，避免频繁切换

## 系统要求

- Windows 11
- .NET 8.0 Runtime
- 管理员权限（用于修改系统注册表）

## 安装

### 方式一：使用安装程序

1. 下载 [Releases](../../releases) 中的安装程序
2. 运行安装程序，按提示完成安装
3. 程序会自动以管理员权限运行

### 方式二：便携版

1. 下载 [Releases](../../releases) 中的便携版压缩包
2. 解压到任意目录
3. 以管理员身份运行 `TabletModeSwitcher.exe`

### 方式三：从源码编译

```bash
# 克隆仓库
git clone https://github.com/yourusername/TabletModeSwitcher.git
cd TabletModeSwitcher

# 编译
dotnet build -c Release

# 或使用打包脚本
.\build.bat
```

## 使用方法

1. 运行程序后，会在系统托盘显示图标
2. **右键菜单**：
   - 切换到桌面模式 / 平板模式
   - 启用/禁用自动切换
   - 显示/隐藏通知
   - 开机自启动
   - 重新扫描键盘
   - 重启 Explorer（强制应用更改）
   - 设置
3. **双击图标**：打开设置窗口

## 设置选项

| 选项 | 说明 |
|------|------|
| 启用自动模式切换 | 是否根据键盘连接状态自动切换模式 |
| 显示通知提示 | 切换模式时是否显示气泡通知 |
| 开机自动启动 | 是否在系统启动时自动运行 |
| 启动时最小化到系统托盘 | 启动时是否隐藏窗口 |
| 同时控制任务栏自动隐藏 | 备选方案，用于非平板设备测试 |
| 模式切换延迟 | 检测到键盘变化后等待的毫秒数 |
| 排除的设备 | 这些设备不会触发模式切换 |

## 工作原理

程序通过以下方式实现模式切换：

1. **监听设备事件** - 使用 WMI 监听 `Win32_Keyboard` 和 `Win32_PnPEntity` 的创建和删除事件
2. **修改注册表** - 设置 `HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl\ConvertibleSlateMode`
   - `0` = 平板模式（Slate Mode）
   - `1` = 桌面模式（Laptop Mode）
3. **广播设置变更** - 通过 `WM_SETTINGCHANGE` 消息通知系统

## 注意事项

⚠️ **重要**：此工具在 **Surface 等具有触摸屏的可转换设备** 上效果最佳。

在没有触摸屏的普通台式机上，即使修改了注册表值，Windows 11 也不会显示平板模式的任务栏，因为系统会检测硬件能力。如果您只是想在台式机上测试功能，可以启用「同时控制任务栏自动隐藏」选项作为替代方案。

## 项目结构

```
TabletModeSwitcher/
├── TabletModeSwitcher.sln      # Visual Studio 解决方案
├── TabletModeSwitcher.csproj   # 项目文件
├── Program.cs                  # 程序入口
├── TrayApplicationContext.cs   # 系统托盘主程序
├── SettingsForm.cs             # 设置窗口
├── TabletModeController.cs     # 平板模式控制核心
├── KeyboardWatcher.cs          # 键盘设备监听
├── AppSettings.cs              # 配置管理
├── app.manifest                # 应用程序清单
├── build.bat                   # 打包脚本
├── build.ps1                   # PowerShell 打包脚本
└── installer.iss               # Inno Setup 安装脚本
```

## 编译打包

### 编译发布版本

```powershell
# 使用 dotnet CLI
dotnet publish -c Release -r win-x64 -o publish

# 或使用打包脚本
.\build.ps1
```

### 创建安装程序

需要安装 [Inno Setup 6](https://jrsoftware.org/isdl.php)，然后运行：

```powershell
.\build.ps1
```

安装程序将生成在 `installer/` 目录中。

## 许可证

MIT License

## 贡献

欢迎提交 Issue 和 Pull Request！
