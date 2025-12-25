using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace TabletModeSwitcher;

/// <summary>
/// 控制 Windows 11 平板模式任务栏的核心类
/// </summary>
public class TabletModeController
{
    // Windows 消息常量
    private const int HWND_BROADCAST = 0xFFFF;
    private const uint WM_SETTINGCHANGE = 0x001A;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint Msg, IntPtr wParam, string? lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    // 注册表路径
    private const string ConvertibleSlatePath = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
    private const string ImmersiveShellPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\ImmersiveShell";

    public event EventHandler<TabletModeChangedEventArgs>? ModeChanged;

    /// <summary>
    /// 是否同时控制任务栏自动隐藏（备选方案）
    /// </summary>
    public bool UseTaskbarAutoHide { get; set; } = false;

    /// <summary>
    /// 获取当前是否处于平板模式
    /// </summary>
    public bool IsTabletMode
    {
        get
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(ConvertibleSlatePath, false);
                var value = key?.GetValue("ConvertibleSlateMode");
                if (value != null)
                    return Convert.ToInt32(value) == 0;
            }
            catch { }
            return false;
        }
    }

    /// <summary>
    /// 切换到桌面模式（固定任务栏）
    /// </summary>
    public bool SwitchToDesktopMode() => SetTabletMode(false);

    /// <summary>
    /// 切换到平板模式（可收起任务栏）
    /// </summary>
    public bool SwitchToTabletMode() => SetTabletMode(true);

    /// <summary>
    /// 设置平板模式
    /// </summary>
    public bool SetTabletMode(bool enableTabletMode)
    {
        try
        {
            // 1. 设置 ConvertibleSlateMode (核心设置)
            SetConvertibleSlateMode(enableTabletMode);

            // 2. 设置 ImmersiveShell 平板模式相关键
            SetImmersiveShellSettings(enableTabletMode);

            // 3. 备选：任务栏自动隐藏
            if (UseTaskbarAutoHide)
            {
                SetTaskbarAutoHide(enableTabletMode);
            }

            // 4. 在后台线程广播设置变更（不阻塞 UI）
            Task.Run(BroadcastSettingsChangeAsync);

            ModeChanged?.Invoke(this, new TabletModeChangedEventArgs(!enableTabletMode));
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"设置平板模式失败: {ex.Message}");
            return false;
        }
    }

    private void SetConvertibleSlateMode(bool enableTabletMode)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(ConvertibleSlatePath, true);
            key?.SetValue("ConvertibleSlateMode", enableTabletMode ? 0 : 1, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"设置 ConvertibleSlateMode 失败: {ex.Message}");
        }
    }

    private void SetImmersiveShellSettings(bool enableTabletMode)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(ImmersiveShellPath);
            if (key != null)
            {
                key.SetValue("TabletMode", enableTabletMode ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("TabletPostureTaskbar", enableTabletMode ? 1 : 0, RegistryValueKind.DWord);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"设置 ImmersiveShell 失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 异步广播设置变更通知（不阻塞）
    /// </summary>
    private void BroadcastSettingsChangeAsync()
    {
        try
        {
            var broadcast = (IntPtr)HWND_BROADCAST;

            // 只发送最关键的通知，超时 500ms
            SendMessageTimeout(broadcast, WM_SETTINGCHANGE, IntPtr.Zero, "ConvertibleSlateMode",
                0x0002, 500, out _);

            SendMessageTimeout(broadcast, WM_SETTINGCHANGE, IntPtr.Zero, "ImmersiveShell",
                0x0002, 500, out _);
        }
        catch { }
    }

    private void SetTaskbarAutoHide(bool autoHide)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3", true);
            if (key != null)
            {
                var settings = key.GetValue("Settings") as byte[];
                if (settings != null && settings.Length > 8)
                {
                    settings[8] = (byte)(autoHide ? 0x03 : 0x02);
                    key.SetValue("Settings", settings, RegistryValueKind.Binary);
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// 重启 Explorer 以应用更改
    /// </summary>
    public void RestartExplorer()
    {
        Task.Run(() =>
        {
            try
            {
                foreach (var process in System.Diagnostics.Process.GetProcessesByName("explorer"))
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
                Thread.Sleep(500);
                System.Diagnostics.Process.Start("explorer.exe");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"重启 Explorer 失败: {ex.Message}");
            }
        });
    }
}

public class TabletModeChangedEventArgs : EventArgs
{
    public bool IsDesktopMode { get; }
    public DateTime Timestamp { get; }

    public TabletModeChangedEventArgs(bool isDesktopMode)
    {
        IsDesktopMode = isDesktopMode;
        Timestamp = DateTime.Now;
    }
}
