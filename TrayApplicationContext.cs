using Microsoft.Win32;

namespace TabletModeSwitcher;

/// <summary>
/// 系统托盘应用程序的主窗口
/// </summary>
public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly KeyboardWatcher _keyboardWatcher;
    private readonly TabletModeController _modeController;
    private AppSettings _settings;
    private readonly System.Windows.Forms.Timer _switchTimer;
    private readonly System.Windows.Forms.Timer _pollTimer;  // 定时轮询键盘状态

    private int _lastKeyboardCount = -1;  // 上次检测到的键盘数量

    public TrayApplicationContext()
    {
        _settings = AppSettings.Load();
        _modeController = new TabletModeController();
        _modeController.UseTaskbarAutoHide = _settings.UseTaskbarAutoHide;
        _keyboardWatcher = new KeyboardWatcher();

        // 初始化排除列表
        _keyboardWatcher.UpdateExcludedDevices(_settings.ExcludedDeviceIds);

        // 防抖动定时器
        _switchTimer = new System.Windows.Forms.Timer { Interval = _settings.SwitchDelayMs };
        _switchTimer.Tick += OnSwitchTimerTick;

        // 定时轮询键盘状态（每2秒检查一次）
        _pollTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _pollTimer.Tick += OnPollTimerTick;

        // 创建系统托盘图标
        _trayIcon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Visible = true,
            Text = "平板模式切换器"
        };

        _trayIcon.ContextMenuStrip = CreateContextMenu();
        _trayIcon.DoubleClick += OnTrayIconDoubleClick;

        // 设置事件处理
        _keyboardWatcher.KeyboardConnected += OnKeyboardConnected;
        _keyboardWatcher.KeyboardDisconnected += OnKeyboardDisconnected;
        _keyboardWatcher.KeyboardCountChanged += OnKeyboardCountChanged;
        _modeController.ModeChanged += OnModeChanged;

        // 启动键盘监听
        _keyboardWatcher.Start();

        // 记录初始键盘数量
        _lastKeyboardCount = _keyboardWatcher.ConnectedKeyboardCount;

        // 根据当前键盘状态初始化模式
        if (_settings.AutoSwitchEnabled)
        {
            if (_keyboardWatcher.HasKeyboardConnected)
            {
                _modeController.SwitchToDesktopMode();
            }
            else
            {
                _modeController.SwitchToTabletMode();
            }
        }

        // 启动定时轮询
        _pollTimer.Start();

        UpdateTrayIconText();
        ShowNotification("平板模式切换器", "程序已启动，正在后台运行。");
    }

    private Icon CreateIcon()
    {
        // 创建一个简单的图标（16x16 蓝色方块）
        using var bitmap = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bitmap);

        // 绘制一个键盘样式的图标
        g.Clear(Color.Transparent);
        g.FillRectangle(Brushes.DodgerBlue, 1, 4, 14, 10);
        g.DrawRectangle(Pens.White, 1, 4, 13, 9);

        // 绘制键盘按键
        for (int i = 0; i < 4; i++)
        {
            g.FillRectangle(Brushes.White, 3 + i * 3, 6, 2, 2);
            g.FillRectangle(Brushes.White, 3 + i * 3, 10, 2, 2);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        // 状态显示
        var statusItem = new ToolStripMenuItem("状态: 检测中...")
        {
            Name = "statusItem",
            Enabled = false
        };
        menu.Items.Add(statusItem);

        menu.Items.Add(new ToolStripSeparator());

        // 手动切换选项
        var desktopModeItem = new ToolStripMenuItem("切换到桌面模式", null, (s, e) =>
        {
            _modeController.SwitchToDesktopMode();
        });
        menu.Items.Add(desktopModeItem);

        var tabletModeItem = new ToolStripMenuItem("切换到平板模式", null, (s, e) =>
        {
            _modeController.SwitchToTabletMode();
        });
        menu.Items.Add(tabletModeItem);

        menu.Items.Add(new ToolStripSeparator());

        // 自动切换开关
        var autoSwitchItem = new ToolStripMenuItem("启用自动切换")
        {
            Name = "autoSwitchItem",
            Checked = _settings.AutoSwitchEnabled,
            CheckOnClick = true
        };
        autoSwitchItem.CheckedChanged += (s, e) =>
        {
            _settings.AutoSwitchEnabled = autoSwitchItem.Checked;
            _settings.Save();
        };
        menu.Items.Add(autoSwitchItem);

        // 显示通知开关
        var notifyItem = new ToolStripMenuItem("显示通知")
        {
            Name = "notifyItem",
            Checked = _settings.ShowNotifications,
            CheckOnClick = true
        };
        notifyItem.CheckedChanged += (s, e) =>
        {
            _settings.ShowNotifications = notifyItem.Checked;
            _settings.Save();
        };
        menu.Items.Add(notifyItem);

        // 开机自启
        var startupItem = new ToolStripMenuItem("开机自启动")
        {
            Name = "startupItem",
            Checked = _settings.RunAtStartup,
            CheckOnClick = true
        };
        startupItem.CheckedChanged += (s, e) =>
        {
            _settings.RunAtStartup = startupItem.Checked;
            _settings.Save();
            SetStartupRegistry(startupItem.Checked);
        };
        menu.Items.Add(startupItem);

        menu.Items.Add(new ToolStripSeparator());

        // 重新扫描
        var rescanItem = new ToolStripMenuItem("重新扫描键盘", null, (s, e) =>
        {
            _keyboardWatcher.ScanExistingKeyboards();
            UpdateTrayIconText();

            // 根据当前键盘状态切换模式
            if (_settings.AutoSwitchEnabled)
            {
                SwitchModeBasedOnKeyboardState();
            }

            ShowNotification("扫描完成", $"检测到 {_keyboardWatcher.ConnectedKeyboardCount} 个键盘设备");
        });
        menu.Items.Add(rescanItem);

        // 重启 Explorer
        var restartExplorerItem = new ToolStripMenuItem("重启 Explorer (应用更改)", null, (s, e) =>
        {
            var result = MessageBox.Show(
                "重启 Explorer 将暂时关闭任务栏和桌面图标，确定要继续吗？",
                "确认",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                _modeController.RestartExplorer();
            }
        });
        menu.Items.Add(restartExplorerItem);

        menu.Items.Add(new ToolStripSeparator());

        // 设置
        var settingsItem = new ToolStripMenuItem("设置...", null, (s, e) =>
        {
            OpenSettingsWindow();
        });
        menu.Items.Add(settingsItem);

        menu.Items.Add(new ToolStripSeparator());

        // 退出
        var exitItem = new ToolStripMenuItem("退出", null, (s, e) =>
        {
            _trayIcon.Visible = false;
            Application.Exit();
        });
        menu.Items.Add(exitItem);

        return menu;
    }

    private void UpdateTrayIconText()
    {
        var mode = _modeController.IsTabletMode ? "平板模式" : "桌面模式";
        var keyboards = _keyboardWatcher.ConnectedKeyboardCount;
        _trayIcon.Text = $"平板模式切换器\n当前: {mode}\n键盘: {keyboards} 个";

        // 更新菜单状态项
        if (_trayIcon.ContextMenuStrip?.Items["statusItem"] is ToolStripMenuItem statusItem)
        {
            statusItem.Text = $"当前: {mode} | 键盘: {keyboards} 个";
        }
    }

    private void OnKeyboardConnected(object? sender, KeyboardEventArgs e)
    {
        if (!_settings.AutoSwitchEnabled) return;

        System.Diagnostics.Debug.WriteLine($"事件: 键盘连接 {e.DeviceId}");
        _lastKeyboardCount = _keyboardWatcher.ConnectedKeyboardCount;
        UpdateTrayIconText();
        SwitchModeBasedOnKeyboardState();
    }

    private void OnKeyboardDisconnected(object? sender, KeyboardEventArgs e)
    {
        if (!_settings.AutoSwitchEnabled) return;

        System.Diagnostics.Debug.WriteLine($"事件: 键盘断开 {e.DeviceId}, 剩余: {_keyboardWatcher.ConnectedKeyboardCount}");
        _lastKeyboardCount = _keyboardWatcher.ConnectedKeyboardCount;
        UpdateTrayIconText();

        // 只有当所有键盘都断开时才切换到平板模式
        if (_keyboardWatcher.ConnectedKeyboardCount == 0)
        {
            SwitchModeBasedOnKeyboardState();
        }
    }

    private void OnSwitchTimerTick(object? sender, EventArgs e)
    {
        _switchTimer.Stop();
        SwitchModeBasedOnKeyboardState();
    }

    /// <summary>
    /// 定时轮询键盘状态
    /// </summary>
    private void OnPollTimerTick(object? sender, EventArgs e)
    {
        if (!_settings.AutoSwitchEnabled) return;

        // 重新扫描键盘
        _keyboardWatcher.ScanExistingKeyboards();
        var currentCount = _keyboardWatcher.ConnectedKeyboardCount;

        // 如果键盘数量发生变化，切换模式
        if (currentCount != _lastKeyboardCount)
        {
            System.Diagnostics.Debug.WriteLine($"轮询检测到键盘变化: {_lastKeyboardCount} -> {currentCount}");
            _lastKeyboardCount = currentCount;
            UpdateTrayIconText();
            SwitchModeBasedOnKeyboardState();
        }
    }

    /// <summary>
    /// 根据当前键盘连接状态切换模式
    /// </summary>
    private void SwitchModeBasedOnKeyboardState()
    {
        if (_keyboardWatcher.ConnectedKeyboardCount > 0)
        {
            // 有键盘连接，切换到桌面模式
            if (_modeController.IsTabletMode)
            {
                _modeController.SwitchToDesktopMode();
            }
        }
        else
        {
            // 无键盘连接，切换到平板模式
            if (!_modeController.IsTabletMode)
            {
                _modeController.SwitchToTabletMode();
            }
        }
    }

    private void OnKeyboardCountChanged(object? sender, int count)
    {
        var menu = _trayIcon.ContextMenuStrip;
        if (menu == null) return;

        // 检查是否需要跨线程调用
        if (menu.IsHandleCreated && menu.InvokeRequired)
        {
            menu.Invoke(() => UpdateTrayIconText());
        }
        else
        {
            UpdateTrayIconText();
        }
    }

    private void OnModeChanged(object? sender, TabletModeChangedEventArgs e)
    {
        var mode = e.IsDesktopMode ? "桌面模式" : "平板模式";
        ShowNotification("模式已切换", $"已切换到{mode}\n(注册表已更新，在平板设备上生效)");
        UpdateTrayIconText();
    }

    private void ShowNotification(string title, string message)
    {
        if (!_settings.ShowNotifications) return;

        _trayIcon.BalloonTipTitle = title;
        _trayIcon.BalloonTipText = message;
        _trayIcon.BalloonTipIcon = ToolTipIcon.Info;
        _trayIcon.ShowBalloonTip(2000);
    }

    private void OnTrayIconDoubleClick(object? sender, EventArgs e)
    {
        // 双击打开设置窗口
        OpenSettingsWindow();
    }

    private SettingsWindow? _settingsWindow;

    private void OpenSettingsWindow()
    {
        // 如果设置窗口已打开，则激活它
        if (_settingsWindow != null && _settingsWindow.IsLoaded)
        {
            _settingsWindow.Activate();
            return;
        }

        // 每次打开设置窗口时从文件重新加载配置
        _settings = AppSettings.Load();

        _settingsWindow = new SettingsWindow(_settings, _keyboardWatcher, _modeController);
        _settingsWindow.ShowDialog();

        if (_settingsWindow.SavedSuccessfully)
        {
            // 重新加载设置
            _switchTimer.Interval = _settings.SwitchDelayMs;
            _modeController.UseTaskbarAutoHide = _settings.UseTaskbarAutoHide;

            // 更新菜单项状态
            if (_trayIcon.ContextMenuStrip?.Items["autoSwitchItem"] is ToolStripMenuItem autoItem)
            {
                autoItem.Checked = _settings.AutoSwitchEnabled;
            }
            if (_trayIcon.ContextMenuStrip?.Items["notifyItem"] is ToolStripMenuItem notifyItem)
            {
                notifyItem.Checked = _settings.ShowNotifications;
            }
            if (_trayIcon.ContextMenuStrip?.Items["startupItem"] is ToolStripMenuItem startupItem)
            {
                startupItem.Checked = _settings.RunAtStartup;
            }

            UpdateTrayIconText();
        }
        _settingsWindow = null;
    }

    private void SetStartupRegistry(bool enable)
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "TabletModeSwitcher";

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Application.ExecutablePath;
                key.SetValue(valueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(valueName, false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"设置开机自启动失败: {ex.Message}");
            ShowNotification("错误", "设置开机自启动失败");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pollTimer.Dispose();
            _switchTimer.Dispose();
            _keyboardWatcher.Dispose();
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
