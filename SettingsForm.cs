namespace TabletModeSwitcher;

/// <summary>
/// 设置窗口
/// </summary>
public partial class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly KeyboardWatcher _keyboardWatcher;
    private readonly TabletModeController _modeController;

    // 控件
    private CheckBox _chkAutoSwitch = null!;
    private CheckBox _chkShowNotifications = null!;
    private CheckBox _chkRunAtStartup = null!;
    private CheckBox _chkStartMinimized = null!;
    private CheckBox _chkUseTaskbarAutoHide = null!;
    private NumericUpDown _numSwitchDelay = null!;
    private ListBox _lstKeyboards = null!;
    private ListBox _lstExcluded = null!;
    private Button _btnAddExclude = null!;
    private Button _btnRemoveExclude = null!;
    private Button _btnRefresh = null!;
    private Label _lblStatus = null!;
    private Button _btnSave = null!;
    private Button _btnCancel = null!;

    public SettingsForm(AppSettings settings, KeyboardWatcher keyboardWatcher, TabletModeController modeController)
    {
        _settings = settings;
        _keyboardWatcher = keyboardWatcher;
        _modeController = modeController;

        InitializeComponent();
        LoadSettings();
        RefreshKeyboardList();
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        // 窗口设置
        Text = "平板模式切换器 - 设置";
        Size = new Size(500, 520);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);

        // 创建选项卡
        var tabControl = new TabControl
        {
            Location = new Point(12, 12),
            Size = new Size(460, 420),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };

        // ========== 常规设置选项卡 ==========
        var tabGeneral = new TabPage("常规设置");
        tabControl.TabPages.Add(tabGeneral);

        int y = 20;

        // 自动切换
        _chkAutoSwitch = new CheckBox
        {
            Text = "启用自动模式切换",
            Location = new Point(20, y),
            Size = new Size(400, 24),
            AutoSize = true
        };
        tabGeneral.Controls.Add(_chkAutoSwitch);
        y += 35;

        // 显示通知
        _chkShowNotifications = new CheckBox
        {
            Text = "显示通知提示",
            Location = new Point(20, y),
            Size = new Size(400, 24),
            AutoSize = true
        };
        tabGeneral.Controls.Add(_chkShowNotifications);
        y += 35;

        // 开机自启
        _chkRunAtStartup = new CheckBox
        {
            Text = "开机自动启动",
            Location = new Point(20, y),
            Size = new Size(400, 24),
            AutoSize = true
        };
        tabGeneral.Controls.Add(_chkRunAtStartup);
        y += 35;

        // 启动时最小化
        _chkStartMinimized = new CheckBox
        {
            Text = "启动时最小化到系统托盘",
            Location = new Point(20, y),
            Size = new Size(400, 24),
            AutoSize = true
        };
        tabGeneral.Controls.Add(_chkStartMinimized);
        y += 35;

        // 使用任务栏自动隐藏（用于非平板设备测试）
        _chkUseTaskbarAutoHide = new CheckBox
        {
            Text = "同时控制任务栏自动隐藏 (用于台式机测试)",
            Location = new Point(20, y),
            Size = new Size(400, 24),
            AutoSize = true
        };
        tabGeneral.Controls.Add(_chkUseTaskbarAutoHide);
        y += 45;

        // 切换延迟
        var lblDelay = new Label
        {
            Text = "模式切换延迟 (毫秒):",
            Location = new Point(20, y + 3),
            AutoSize = true
        };
        tabGeneral.Controls.Add(lblDelay);

        _numSwitchDelay = new NumericUpDown
        {
            Location = new Point(180, y),
            Size = new Size(100, 25),
            Minimum = 0,
            Maximum = 5000,
            Increment = 100,
            Value = 500
        };
        tabGeneral.Controls.Add(_numSwitchDelay);

        var lblDelayHint = new Label
        {
            Text = "(防止频繁切换)",
            Location = new Point(290, y + 3),
            AutoSize = true,
            ForeColor = Color.Gray
        };
        tabGeneral.Controls.Add(lblDelayHint);
        y += 50;

        // 当前状态
        var grpStatus = new GroupBox
        {
            Text = "当前状态",
            Location = new Point(20, y),
            Size = new Size(400, 80)
        };
        tabGeneral.Controls.Add(grpStatus);

        _lblStatus = new Label
        {
            Location = new Point(15, 25),
            Size = new Size(370, 40),
            Text = "正在获取状态..."
        };
        grpStatus.Controls.Add(_lblStatus);

        // ========== 设备管理选项卡 ==========
        var tabDevices = new TabPage("设备管理");
        tabControl.TabPages.Add(tabDevices);

        // 已连接的键盘
        var lblKeyboards = new Label
        {
            Text = "已检测到的键盘设备:",
            Location = new Point(20, 15),
            AutoSize = true
        };
        tabDevices.Controls.Add(lblKeyboards);

        _lstKeyboards = new ListBox
        {
            Location = new Point(20, 40),
            Size = new Size(300, 120),
            SelectionMode = SelectionMode.One
        };
        tabDevices.Controls.Add(_lstKeyboards);

        _btnRefresh = new Button
        {
            Text = "刷新列表",
            Location = new Point(330, 40),
            Size = new Size(90, 30)
        };
        _btnRefresh.Click += BtnRefresh_Click;
        tabDevices.Controls.Add(_btnRefresh);

        _btnAddExclude = new Button
        {
            Text = "添加到排除 ↓",
            Location = new Point(330, 80),
            Size = new Size(90, 30)
        };
        _btnAddExclude.Click += BtnAddExclude_Click;
        tabDevices.Controls.Add(_btnAddExclude);

        // 排除的设备
        var lblExcluded = new Label
        {
            Text = "排除的设备 (这些设备不会触发模式切换):",
            Location = new Point(20, 170),
            AutoSize = true
        };
        tabDevices.Controls.Add(lblExcluded);

        _lstExcluded = new ListBox
        {
            Location = new Point(20, 195),
            Size = new Size(300, 120),
            SelectionMode = SelectionMode.One
        };
        tabDevices.Controls.Add(_lstExcluded);

        _btnRemoveExclude = new Button
        {
            Text = "移除排除",
            Location = new Point(330, 195),
            Size = new Size(90, 30)
        };
        _btnRemoveExclude.Click += BtnRemoveExclude_Click;
        tabDevices.Controls.Add(_btnRemoveExclude);

        // 提示
        var lblHint = new Label
        {
            Text = "提示: 如果您有某些键盘设备不想触发模式切换（如蓝牙遥控器等），\n可以将其添加到排除列表。",
            Location = new Point(20, 330),
            Size = new Size(400, 40),
            ForeColor = Color.Gray
        };
        tabDevices.Controls.Add(lblHint);

        // ========== 关于选项卡 ==========
        var tabAbout = new TabPage("关于");
        tabControl.TabPages.Add(tabAbout);

        var lblAboutTitle = new Label
        {
            Text = "平板模式切换器",
            Location = new Point(20, 30),
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            AutoSize = true
        };
        tabAbout.Controls.Add(lblAboutTitle);

        var lblVersion = new Label
        {
            Text = "版本 1.0.0",
            Location = new Point(20, 65),
            AutoSize = true
        };
        tabAbout.Controls.Add(lblVersion);

        var lblDescription = new Label
        {
            Text = "自动检测键盘连接并切换 Windows 11 平板模式任务栏。\n\n" +
                   "功能说明:\n" +
                   "• 监听 USB 和蓝牙键盘的连接/断开\n" +
                   "• 键盘连接时自动切换到桌面模式（固定任务栏）\n" +
                   "• 键盘断开时自动切换到平板模式（可收起任务栏）\n\n" +
                   "工作原理:\n" +
                   "通过修改系统注册表 ConvertibleSlateMode 值来模拟\n" +
                   "Surface 键盘盖发送的硬件信号。",
            Location = new Point(20, 100),
            Size = new Size(400, 200)
        };
        tabAbout.Controls.Add(lblDescription);

        Controls.Add(tabControl);

        // ========== 底部按钮 ==========
        _btnSave = new Button
        {
            Text = "保存",
            Location = new Point(290, 445),
            Size = new Size(85, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _btnSave.Click += BtnSave_Click;
        Controls.Add(_btnSave);

        _btnCancel = new Button
        {
            Text = "取消",
            Location = new Point(385, 445),
            Size = new Size(85, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _btnCancel.Click += BtnCancel_Click;
        Controls.Add(_btnCancel);

        ResumeLayout(false);
    }

    private void LoadSettings()
    {
        _chkAutoSwitch.Checked = _settings.AutoSwitchEnabled;
        _chkShowNotifications.Checked = _settings.ShowNotifications;
        _chkRunAtStartup.Checked = _settings.RunAtStartup;
        _chkStartMinimized.Checked = _settings.StartMinimized;
        _chkUseTaskbarAutoHide.Checked = _settings.UseTaskbarAutoHide;
        _numSwitchDelay.Value = Math.Clamp(_settings.SwitchDelayMs, 0, 5000);

        // 加载排除列表
        _lstExcluded.Items.Clear();
        foreach (var deviceId in _settings.ExcludedDeviceIds)
        {
            _lstExcluded.Items.Add(deviceId);
        }

        UpdateStatus();
    }

    private void RefreshKeyboardList()
    {
        _keyboardWatcher.ScanExistingKeyboards();
        _lstKeyboards.Items.Clear();

        // 显示所有设备（包括被过滤的），方便调试
        foreach (var (deviceId, description, filtered) in _keyboardWatcher.GetAllKeyboardDevices())
        {
            var displayText = filtered
                ? $"[Filtered] {description}"
                : description;
            _lstKeyboards.Items.Add(new KeyboardItem(deviceId, displayText, filtered));
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var mode = _modeController.IsTabletMode ? "平板模式" : "桌面模式";
        var allDevices = _keyboardWatcher.GetAllKeyboardDevices();
        var activeCount = allDevices.Count(d => !d.Filtered);
        var totalCount = allDevices.Count;
        _lblStatus.Text = $"当前模式: {mode}\n检测到 {activeCount} 个键盘 (共 {totalCount} 个设备)";
    }

    private void BtnRefresh_Click(object? sender, EventArgs e)
    {
        RefreshKeyboardList();
    }

    private void BtnAddExclude_Click(object? sender, EventArgs e)
    {
        if (_lstKeyboards.SelectedItem is KeyboardItem item)
        {
            if (!_lstExcluded.Items.Contains(item.DeviceId))
            {
                _lstExcluded.Items.Add(item.DeviceId);
            }
        }
        else
        {
            MessageBox.Show("请先选择一个键盘设备", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void BtnRemoveExclude_Click(object? sender, EventArgs e)
    {
        if (_lstExcluded.SelectedItem != null)
        {
            _lstExcluded.Items.Remove(_lstExcluded.SelectedItem);
        }
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        // 保存设置
        _settings.AutoSwitchEnabled = _chkAutoSwitch.Checked;
        _settings.ShowNotifications = _chkShowNotifications.Checked;
        _settings.RunAtStartup = _chkRunAtStartup.Checked;
        _settings.StartMinimized = _chkStartMinimized.Checked;
        _settings.UseTaskbarAutoHide = _chkUseTaskbarAutoHide.Checked;
        _settings.SwitchDelayMs = (int)_numSwitchDelay.Value;

        _settings.ExcludedDeviceIds.Clear();
        foreach (string deviceId in _lstExcluded.Items)
        {
            _settings.ExcludedDeviceIds.Add(deviceId);
        }

        _settings.Save();

        // 设置开机自启动
        SetStartupRegistry(_settings.RunAtStartup);

        DialogResult = DialogResult.OK;
        Close();
    }

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void SetStartupRegistry(bool enable)
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "TabletModeSwitcher";

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath, true);
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
            MessageBox.Show($"设置开机自启动失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 键盘列表项
    /// </summary>
    private class KeyboardItem
    {
        public string DeviceId { get; }
        public string Description { get; }
        public bool IsFiltered { get; }

        public KeyboardItem(string deviceId, string description, bool isFiltered)
        {
            DeviceId = deviceId;
            Description = description;
            IsFiltered = isFiltered;
        }

        public override string ToString() => Description;
    }
}
