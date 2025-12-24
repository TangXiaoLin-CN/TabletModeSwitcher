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
    private const uint WM_WININICHANGE = 0x001A;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint Msg,
        IntPtr wParam,
        string? lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    // 注册表路径
    private const string ConvertibleSlatePath = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
    private const string ImmersiveShellPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\ImmersiveShell";
    private const string AutoRotationPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\AutoRotation";
    private const string TabletTipPath = @"SOFTWARE\Microsoft\TabletTip\1.7";
    private const string TouchPath = @"SOFTWARE\Microsoft\Wisp\Touch";
    private const string PenWorkspacePath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\PenWorkspace";

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
                {
                    return Convert.ToInt32(value) == 0;
                }
            }
            catch { }
            return false;
        }
    }

    /// <summary>
    /// 切换到桌面模式（固定任务栏）
    /// </summary>
    public bool SwitchToDesktopMode()
    {
        return SetTabletMode(false);
    }

    /// <summary>
    /// 切换到平板模式（可收起任务栏）
    /// </summary>
    public bool SwitchToTabletMode()
    {
        return SetTabletMode(true);
    }

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

            // 3. 模拟可转换设备/平板设备特征
            SetDevicePosture(enableTabletMode);

            // 4. 设置触控相关设置
            SetTouchSettings(enableTabletMode);

            // 5. 广播设置变更通知
            BroadcastSettingsChange();

            // 6. 备选：任务栏自动隐藏
            if (UseTaskbarAutoHide)
            {
                SetTaskbarAutoHide(enableTabletMode);
            }

            ModeChanged?.Invoke(this, new TabletModeChangedEventArgs(!enableTabletMode));
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"设置平板模式失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 设置 ConvertibleSlateMode (0=平板, 1=桌面)
    /// </summary>
    private void SetConvertibleSlateMode(bool enableTabletMode)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(ConvertibleSlatePath, true);
            if (key != null)
            {
                // ConvertibleSlateMode: 0 = Slate/Tablet, 1 = Laptop/Desktop
                key.SetValue("ConvertibleSlateMode", enableTabletMode ? 0 : 1, RegistryValueKind.DWord);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"设置 ConvertibleSlateMode 失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 设置 ImmersiveShell 相关设置
    /// </summary>
    private void SetImmersiveShellSettings(bool enableTabletMode)
    {
        try
        {
            // HKCU 设置
            using (var key = Registry.CurrentUser.CreateSubKey(ImmersiveShellPath))
            {
                if (key != null)
                {
                    // TabletMode: 当前模式
                    key.SetValue("TabletMode", enableTabletMode ? 1 : 0, RegistryValueKind.DWord);

                    // TabletModePreference: 用户偏好
                    key.SetValue("TabletModePreference", enableTabletMode ? 1 : 0, RegistryValueKind.DWord);

                    // SignInMode: 0=自动, 1=桌面, 2=平板
                    // key.SetValue("SignInMode", 0, RegistryValueKind.DWord);

                    // ConvertibleSlateModePromptPreference: 0=总是询问, 1=不切换, 2=自动切换
                    key.SetValue("ConvertibleSlateModePromptPreference", 2, RegistryValueKind.DWord);

                    // TabletPostureTaskbar: 平板姿态下的任务栏行为
                    key.SetValue("TabletPostureTaskbar", enableTabletMode ? 1 : 0, RegistryValueKind.DWord);
                }
            }

            // HKLM 设置 (如果有权限)
            try
            {
                using var keyLM = Registry.LocalMachine.CreateSubKey(ImmersiveShellPath);
                if (keyLM != null)
                {
                    keyLM.SetValue("TabletMode", enableTabletMode ? 1 : 0, RegistryValueKind.DWord);
                }
            }
            catch { }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"设置 ImmersiveShell 失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 设置设备姿态/形态
    /// </summary>
    private void SetDevicePosture(bool enableTabletMode)
    {
        try
        {
            // 启用自动旋转（平板设备特征）
            using (var key = Registry.LocalMachine.CreateSubKey(AutoRotationPath))
            {
                if (key != null)
                {
                    key.SetValue("Enable", enableTabletMode ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("LastOrientation", 0, RegistryValueKind.DWord);
                    // SlateMode: 表示当前是否为平板形态
                    key.SetValue("SlateMode", enableTabletMode ? 1 : 0, RegistryValueKind.DWord);
                }
            }

            // 设置 PenWorkspace
            using (var key = Registry.CurrentUser.CreateSubKey(PenWorkspacePath))
            {
                if (key != null)
                {
                    key.SetValue("PenWorkspaceButtonDesiredVisibility", enableTabletMode ? 1 : 0, RegistryValueKind.DWord);
                }
            }

            // 设置 TabletTip (触摸键盘)
            using (var key = Registry.CurrentUser.CreateSubKey(TabletTipPath))
            {
                if (key != null)
                {
                    key.SetValue("TipbandDesiredVisibility", enableTabletMode ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("EnableDesktopModeAutoInvoke", enableTabletMode ? 1 : 0, RegistryValueKind.DWord);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"设置设备姿态失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 设置触控相关设置
    /// </summary>
    private void SetTouchSettings(bool enableTabletMode)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(TouchPath);
            if (key != null)
            {
                // TouchMode_hold: 触控模式
                key.SetValue("TouchMode_hold", enableTabletMode ? 1 : 0, RegistryValueKind.DWord);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"设置触控设置失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 广播设置变更通知
    /// </summary>
    private void BroadcastSettingsChange()
    {
        try
        {
            var broadcast = (IntPtr)HWND_BROADCAST;

            // 通知 ConvertibleSlateMode 变化
            SendMessageTimeout(broadcast, WM_SETTINGCHANGE, IntPtr.Zero, "ConvertibleSlateMode",
                0x0002, 5000, out _);

            // 通知 ImmersiveShell 变化
            SendMessageTimeout(broadcast, WM_SETTINGCHANGE, IntPtr.Zero, "ImmersiveShell",
                0x0002, 5000, out _);

            // 通知触控设置变化
            SendMessageTimeout(broadcast, WM_SETTINGCHANGE, IntPtr.Zero, "Touch",
                0x0002, 5000, out _);

            // 通知策略变化
            SendMessageTimeout(broadcast, WM_SETTINGCHANGE, IntPtr.Zero, "Policy",
                0x0002, 5000, out _);

            // 通知 Environment 变化
            SendMessageTimeout(broadcast, WM_SETTINGCHANGE, IntPtr.Zero, null,
                0x0002, 5000, out _);

            // 刷新系统参数
            SendMessageTimeout(broadcast, WM_SETTINGCHANGE, (IntPtr)0x001A, "Environment",
                0x0002, 5000, out _);
        }
        catch { }
    }

    /// <summary>
    /// 设置任务栏自动隐藏（备选方案）
    /// </summary>
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
    }

    /// <summary>
    /// 检查设备是否支持平板模式（有触摸屏）
    /// </summary>
    public static bool IsDeviceTouchCapable()
    {
        try
        {
            // 检查系统是否有触摸输入设备
            var value = GetSystemMetrics(SM_MAXIMUMTOUCHES);
            return value > 0;
        }
        catch
        {
            return false;
        }
    }

    private const int SM_MAXIMUMTOUCHES = 95;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
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
