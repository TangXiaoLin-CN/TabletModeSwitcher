using System.Windows;
using Microsoft.Win32;

namespace TabletModeSwitcher;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly KeyboardWatcher _keyboardWatcher;
    private readonly TabletModeController _modeController;

    public bool SavedSuccessfully { get; private set; }

    public SettingsWindow(AppSettings settings, KeyboardWatcher keyboardWatcher, TabletModeController modeController)
    {
        _settings = settings;
        _keyboardWatcher = keyboardWatcher;
        _modeController = modeController;

        InitializeComponent();
        LoadSettings();
        RefreshKeyboardList();
    }

    private void LoadSettings()
    {
        chkAutoSwitch.IsChecked = _settings.AutoSwitchEnabled;
        chkShowNotifications.IsChecked = _settings.ShowNotifications;
        chkRunAtStartup.IsChecked = _settings.RunAtStartup;
        chkStartMinimized.IsChecked = _settings.StartMinimized;
        chkUseTaskbarAutoHide.IsChecked = _settings.UseTaskbarAutoHide;
        txtSwitchDelay.Text = _settings.SwitchDelayMs.ToString();

        // 加载排除列表
        lstExcluded.Items.Clear();
        if (_settings.ExcludedDeviceIds != null)
        {
            foreach (var deviceId in _settings.ExcludedDeviceIds)
            {
                lstExcluded.Items.Add(deviceId);
            }
        }

        // 同步排除列表到 KeyboardWatcher
        _keyboardWatcher.UpdateExcludedDevices(_settings.ExcludedDeviceIds ?? new List<string>());

        UpdateStatus();
    }

    private void RefreshKeyboardList()
    {
        _keyboardWatcher.ScanExistingKeyboards();
        lstKeyboards.Items.Clear();

        // 获取当前排除列表中的设备ID
        var excludedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in lstExcluded.Items)
        {
            excludedIds.Add(id);
        }

        // 只显示未被排除的设备
        foreach (var (deviceId, description, filtered, _) in _keyboardWatcher.GetAllKeyboardDevices())
        {
            if (excludedIds.Contains(deviceId))
                continue;

            var item = new KeyboardListItem
            {
                DeviceId = deviceId,
                DisplayText = filtered ? $"[系统] {description}" : description,
                IsFiltered = filtered
            };
            lstKeyboards.Items.Add(item);
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var mode = _modeController.IsTabletMode ? "平板模式" : "桌面模式";
        var allDevices = _keyboardWatcher.GetAllKeyboardDevices();

        var excludedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in lstExcluded.Items)
        {
            excludedIds.Add(id);
        }

        var activeCount = allDevices.Count(d => !d.Filtered && !excludedIds.Contains(d.DeviceId));
        var totalCount = allDevices.Count;
        lblStatus.Text = $"当前模式: {mode}\n检测到 {activeCount} 个有效键盘 (共 {totalCount} 个设备)";
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshKeyboardList();
    }

    private void BtnAddExclude_Click(object sender, RoutedEventArgs e)
    {
        if (lstKeyboards.SelectedItem is KeyboardListItem item)
        {
            if (!lstExcluded.Items.Contains(item.DeviceId))
            {
                lstExcluded.Items.Add(item.DeviceId);
                RefreshKeyboardList();
            }
        }
        else
        {
            System.Windows.MessageBox.Show("请先选择一个键盘设备", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnRemoveExclude_Click(object sender, RoutedEventArgs e)
    {
        if (lstExcluded.SelectedItem != null)
        {
            lstExcluded.Items.Remove(lstExcluded.SelectedItem);
            RefreshKeyboardList();
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        // 验证延迟值
        if (!int.TryParse(txtSwitchDelay.Text, out int delay) || delay < 0 || delay > 10000)
        {
            System.Windows.MessageBox.Show("切换延迟必须是0-10000之间的数字", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // 保存基本设置
        _settings.AutoSwitchEnabled = chkAutoSwitch.IsChecked ?? false;
        _settings.ShowNotifications = chkShowNotifications.IsChecked ?? false;
        _settings.RunAtStartup = chkRunAtStartup.IsChecked ?? false;
        _settings.StartMinimized = chkStartMinimized.IsChecked ?? false;
        _settings.UseTaskbarAutoHide = chkUseTaskbarAutoHide.IsChecked ?? false;
        _settings.SwitchDelayMs = delay;

        // 保存排除列表
        var excludedList = new List<string>();
        foreach (var item in lstExcluded.Items)
        {
            if (item != null)
            {
                excludedList.Add(item.ToString()!);
            }
        }
        _settings.ExcludedDeviceIds = excludedList;

        // 保存到文件
        if (!_settings.Save())
        {
            return;
        }

        // 更新排除列表到监听器
        _keyboardWatcher.UpdateExcludedDevices(_settings.ExcludedDeviceIds);

        // 设置开机自启动
        SetStartupRegistry(_settings.RunAtStartup);

        SavedSuccessfully = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        SavedSuccessfully = false;
        Close();
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
                var exePath = System.Windows.Forms.Application.ExecutablePath;
                key.SetValue(valueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(valueName, false);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"设置开机自启动失败: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private class KeyboardListItem
    {
        public string DeviceId { get; set; } = "";
        public string DisplayText { get; set; } = "";
        public bool IsFiltered { get; set; }

        public override string ToString() => DisplayText;
    }
}
