using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace TabletModeSwitcher;

/// <summary>
/// 高性能键盘设备监听器
/// 使用 SetupAPI 和设备通知代替 WMI
/// </summary>
public class KeyboardWatcher : IDisposable
{
    // SetupAPI 常量
    private static readonly Guid GUID_DEVINTERFACE_KEYBOARD = new("884b96c3-56ef-11d1-bc8c-00a0c91405dd");
    private const int DIGCF_PRESENT = 0x02;
    private const int DIGCF_DEVICEINTERFACE = 0x10;
    private const int SPDRP_DEVICEDESC = 0x00;
    private const int SPDRP_HARDWAREID = 0x01;
    private const int SPDRP_CLASS = 0x07;

    // SetupAPI 函数
    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet, int memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, int property,
        out int propertyRegDataType, StringBuilder propertyBuffer, int propertyBufferSize, out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceId(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        StringBuilder deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public int DevInst;
        public IntPtr Reserved;
    }

    // 设备通知
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr RegisterDeviceNotification(
        IntPtr hRecipient, IntPtr notificationFilter, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterDeviceNotification(IntPtr handle);

    private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00;
    private const int DBT_DEVTYP_DEVICEINTERFACE = 0x05;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const int WM_DEVICECHANGE = 0x0219;

    [StructLayout(LayoutKind.Sequential)]
    private struct DEV_BROADCAST_DEVICEINTERFACE
    {
        public int dbcc_size;
        public int dbcc_devicetype;
        public int dbcc_reserved;
        public Guid dbcc_classguid;
        public char dbcc_name;
    }

    private readonly HashSet<string> _connectedKeyboards = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private DeviceNotificationForm? _notificationForm;
    private bool _disposed;
    private System.Threading.Timer? _debounceTimer;
    private volatile bool _pendingRefresh;
    private HashSet<string> _excludedDeviceIds = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<KeyboardEventArgs>? KeyboardConnected;
    public event EventHandler<KeyboardEventArgs>? KeyboardDisconnected;
    public event EventHandler<int>? KeyboardCountChanged;

    /// <summary>
    /// 当前连接的键盘数量
    /// </summary>
    public int ConnectedKeyboardCount
    {
        get { lock (_lock) return _connectedKeyboards.Count; }
    }

    /// <summary>
    /// 是否有键盘连接
    /// </summary>
    public bool HasKeyboardConnected
    {
        get { lock (_lock) return _connectedKeyboards.Count > 0; }
    }

    /// <summary>
    /// 更新排除的设备ID列表
    /// </summary>
    public void UpdateExcludedDevices(IEnumerable<string> excludedIds)
    {
        lock (_lock)
        {
            _excludedDeviceIds.Clear();
            foreach (var id in excludedIds)
            {
                _excludedDeviceIds.Add(id);
            }
        }
        // 重新扫描以应用新的排除列表
        ScanExistingKeyboards();
    }

    /// <summary>
    /// 获取已连接键盘列表的副本
    /// </summary>
    public List<string> GetConnectedKeyboards()
    {
        lock (_lock) return _connectedKeyboards.ToList();
    }

    /// <summary>
    /// 开始监听键盘设备
    /// </summary>
    public void Start()
    {
        // 首先扫描当前已连接的键盘
        ScanExistingKeyboards();

        // 在后台线程创建通知窗口
        var thread = new Thread(CreateNotificationWindow)
        {
            IsBackground = true,
            Name = "DeviceNotificationThread"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private void CreateNotificationWindow()
    {
        _notificationForm = new DeviceNotificationForm(this);
        Application.Run(_notificationForm);
    }

    /// <summary>
    /// 扫描当前已连接的键盘设备（使用 SetupAPI，比 WMI 快得多）
    /// </summary>
    public void ScanExistingKeyboards()
    {
        var newKeyboards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var guid = GUID_DEVINTERFACE_KEYBOARD;
            var deviceInfoSet = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero,
                DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);

            if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
                return;

            try
            {
                var deviceInfoData = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };

                for (int i = 0; SetupDiEnumDeviceInfo(deviceInfoSet, i, ref deviceInfoData); i++)
                {
                    var deviceId = GetDeviceInstanceId(deviceInfoSet, ref deviceInfoData);
                    var description = GetDeviceProperty(deviceInfoSet, ref deviceInfoData, SPDRP_DEVICEDESC);

                    if (string.IsNullOrEmpty(deviceId))
                        continue;

                    // 记录所有设备用于调试
                    System.Diagnostics.Debug.WriteLine($"[Keyboard] {description} | {deviceId}");

                    // 过滤虚拟设备
                    if (IsVirtualKeyboard(deviceId, description))
                    {
                        System.Diagnostics.Debug.WriteLine($"  -> Filtered out (virtual)");
                        continue;
                    }

                    // 检查是否在排除列表中
                    bool isExcluded;
                    lock (_lock)
                    {
                        isExcluded = _excludedDeviceIds.Contains(deviceId);
                    }
                    if (isExcluded)
                    {
                        System.Diagnostics.Debug.WriteLine($"  -> Excluded by user");
                        continue;
                    }

                    newKeyboards.Add(deviceId);
                    System.Diagnostics.Debug.WriteLine($"  -> Accepted");
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Scan failed: {ex.Message}");
        }

        // 更新键盘列表
        int oldCount, newCount;
        lock (_lock)
        {
            oldCount = _connectedKeyboards.Count;
            _connectedKeyboards.Clear();
            foreach (var kb in newKeyboards)
                _connectedKeyboards.Add(kb);
            newCount = _connectedKeyboards.Count;
        }

        if (oldCount != newCount)
        {
            KeyboardCountChanged?.Invoke(this, newCount);
        }
    }

    /// <summary>
    /// 获取所有键盘设备信息（用于调试）
    /// </summary>
    public List<(string DeviceId, string Description, bool Filtered, bool Excluded)> GetAllKeyboardDevices()
    {
        var result = new List<(string, string, bool, bool)>();

        try
        {
            var guid = GUID_DEVINTERFACE_KEYBOARD;
            var deviceInfoSet = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero,
                DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);

            if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
                return result;

            try
            {
                var deviceInfoData = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };

                for (int i = 0; SetupDiEnumDeviceInfo(deviceInfoSet, i, ref deviceInfoData); i++)
                {
                    var deviceId = GetDeviceInstanceId(deviceInfoSet, ref deviceInfoData);
                    var description = GetDeviceProperty(deviceInfoSet, ref deviceInfoData, SPDRP_DEVICEDESC);

                    if (string.IsNullOrEmpty(deviceId))
                        continue;

                    var filtered = IsVirtualKeyboard(deviceId, description);
                    bool excluded;
                    lock (_lock)
                    {
                        excluded = _excludedDeviceIds.Contains(deviceId);
                    }
                    result.Add((deviceId, description, filtered, excluded));
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }
        }
        catch { }

        return result;
    }

    private string GetDeviceInstanceId(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData)
    {
        var buffer = new StringBuilder(256);
        if (SetupDiGetDeviceInstanceId(deviceInfoSet, ref deviceInfoData, buffer, buffer.Capacity, out _))
            return buffer.ToString();
        return string.Empty;
    }

    private string GetDeviceProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, int property)
    {
        var buffer = new StringBuilder(256);
        if (SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref deviceInfoData, property,
            out _, buffer, buffer.Capacity, out _))
            return buffer.ToString();
        return string.Empty;
    }

    /// <summary>
    /// 判断是否是虚拟键盘或内置系统键盘（需要排除）
    /// </summary>
    private bool IsVirtualKeyboard(string deviceId, string? description)
    {
        if (string.IsNullOrEmpty(deviceId))
            return true;

        var upperDeviceId = deviceId.ToUpperInvariant();
        var lowerDesc = description?.ToLowerInvariant() ?? "";

        // 排除 Root 枚举的设备（虚拟设备）
        if (upperDeviceId.StartsWith("ROOT\\"))
            return true;

        // 排除 SW 开头的软件设备
        if (upperDeviceId.StartsWith("SW\\"))
            return true;

        // 排除 SWD 软件设备
        if (upperDeviceId.StartsWith("SWD\\"))
            return true;

        // 排除 HID 兼容键盘驱动（这是驱动层，不是实际设备）
        if (upperDeviceId.Contains("HID_DEVICE_SYSTEM_KEYBOARD"))
            return true;

        // 排除远程桌面键盘
        if (upperDeviceId.Contains("RDP_") || upperDeviceId.Contains("YOURREMOTE") ||
            lowerDesc.Contains("remote desktop") || lowerDesc.Contains("terminal server"))
            return true;

        // 排除虚拟键盘关键词
        if (lowerDesc.Contains("virtual"))
            return true;

        // 不再过滤 ACPI 和 HID 设备，因为有些真实键盘使用这些前缀
        return false;
    }

    /// <summary>
    /// 处理设备变化（防抖动）
    /// </summary>
    internal void OnDeviceChanged()
    {
        _pendingRefresh = true;

        // 使用防抖动定时器，避免频繁刷新
        _debounceTimer?.Dispose();
        _debounceTimer = new System.Threading.Timer(_ =>
        {
            if (_pendingRefresh)
            {
                _pendingRefresh = false;

                var oldKeyboards = GetConnectedKeyboards();
                ScanExistingKeyboards();
                var newKeyboards = GetConnectedKeyboards();

                // 检测新增的键盘
                foreach (var kb in newKeyboards.Except(oldKeyboards))
                {
                    KeyboardConnected?.Invoke(this, new KeyboardEventArgs(kb, "Keyboard"));
                }

                // 检测移除的键盘
                foreach (var kb in oldKeyboards.Except(newKeyboards))
                {
                    KeyboardDisconnected?.Invoke(this, new KeyboardEventArgs(kb, "Keyboard"));
                }
            }
        }, null, 300, Timeout.Infinite);
    }

    /// <summary>
    /// 停止监听
    /// </summary>
    public void Stop()
    {
        _notificationForm?.Invoke(() => _notificationForm?.Close());
        _debounceTimer?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    /// <summary>
    /// 用于接收设备通知的隐藏窗口
    /// </summary>
    private class DeviceNotificationForm : Form
    {
        private readonly KeyboardWatcher _watcher;
        private IntPtr _notificationHandle;

        public DeviceNotificationForm(KeyboardWatcher watcher)
        {
            _watcher = watcher;
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            Visible = false;
            RegisterForDeviceNotifications();
        }

        private void RegisterForDeviceNotifications()
        {
            var dbi = new DEV_BROADCAST_DEVICEINTERFACE
            {
                dbcc_size = Marshal.SizeOf<DEV_BROADCAST_DEVICEINTERFACE>(),
                dbcc_devicetype = DBT_DEVTYP_DEVICEINTERFACE,
                dbcc_classguid = GUID_DEVINTERFACE_KEYBOARD
            };

            var buffer = Marshal.AllocHGlobal(dbi.dbcc_size);
            try
            {
                Marshal.StructureToPtr(dbi, buffer, false);
                _notificationHandle = RegisterDeviceNotification(Handle, buffer, DEVICE_NOTIFY_WINDOW_HANDLE);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DEVICECHANGE)
            {
                var eventType = m.WParam.ToInt32();
                if (eventType == DBT_DEVICEARRIVAL || eventType == DBT_DEVICEREMOVECOMPLETE)
                {
                    _watcher.OnDeviceChanged();
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_notificationHandle != IntPtr.Zero)
            {
                UnregisterDeviceNotification(_notificationHandle);
                _notificationHandle = IntPtr.Zero;
            }
            base.OnFormClosing(e);
        }
    }
}

public class KeyboardEventArgs : EventArgs
{
    public string DeviceId { get; }
    public string Description { get; }
    public DateTime Timestamp { get; }

    public KeyboardEventArgs(string deviceId, string description)
    {
        DeviceId = deviceId;
        Description = description;
        Timestamp = DateTime.Now;
    }
}
