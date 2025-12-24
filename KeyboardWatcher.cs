using System.Management;
using System.Text.RegularExpressions;

namespace TabletModeSwitcher;

/// <summary>
/// 监听键盘设备的连接和断开
/// </summary>
public class KeyboardWatcher : IDisposable
{
    private ManagementEventWatcher? _insertWatcher;
    private ManagementEventWatcher? _removeWatcher;
    private readonly HashSet<string> _connectedKeyboards = new();
    private bool _disposed;

    public event EventHandler<KeyboardEventArgs>? KeyboardConnected;
    public event EventHandler<KeyboardEventArgs>? KeyboardDisconnected;
    public event EventHandler<int>? KeyboardCountChanged;

    /// <summary>
    /// 当前连接的键盘数量
    /// </summary>
    public int ConnectedKeyboardCount => _connectedKeyboards.Count;

    /// <summary>
    /// 是否有键盘连接
    /// </summary>
    public bool HasKeyboardConnected => _connectedKeyboards.Count > 0;

    /// <summary>
    /// 开始监听键盘设备
    /// </summary>
    public void Start()
    {
        // 首先扫描当前已连接的键盘
        ScanExistingKeyboards();

        // 设置 USB 设备插入监听
        var insertQuery = new WqlEventQuery(
            "SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_Keyboard'");
        _insertWatcher = new ManagementEventWatcher(insertQuery);
        _insertWatcher.EventArrived += OnDeviceInserted;
        _insertWatcher.Start();

        // 设置 USB 设备移除监听
        var removeQuery = new WqlEventQuery(
            "SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_Keyboard'");
        _removeWatcher = new ManagementEventWatcher(removeQuery);
        _removeWatcher.EventArrived += OnDeviceRemoved;
        _removeWatcher.Start();

        // 同时监听 PnP 设备事件以捕获蓝牙键盘
        StartPnPWatcher();
    }

    private ManagementEventWatcher? _pnpInsertWatcher;
    private ManagementEventWatcher? _pnpRemoveWatcher;

    private void StartPnPWatcher()
    {
        try
        {
            // 监听 PnP 设备插入（包括蓝牙设备）
            var pnpInsertQuery = new WqlEventQuery(
                "SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_PnPEntity'");
            _pnpInsertWatcher = new ManagementEventWatcher(pnpInsertQuery);
            _pnpInsertWatcher.EventArrived += OnPnPDeviceInserted;
            _pnpInsertWatcher.Start();

            // 监听 PnP 设备移除
            var pnpRemoveQuery = new WqlEventQuery(
                "SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_PnPEntity'");
            _pnpRemoveWatcher = new ManagementEventWatcher(pnpRemoveQuery);
            _pnpRemoveWatcher.EventArrived += OnPnPDeviceRemoved;
            _pnpRemoveWatcher.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PnP watcher 启动失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 扫描当前已连接的键盘设备
    /// </summary>
    public void ScanExistingKeyboards()
    {
        _connectedKeyboards.Clear();

        try
        {
            // 扫描 Win32_Keyboard
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Keyboard");
            foreach (ManagementObject device in searcher.Get())
            {
                var deviceId = device["DeviceID"]?.ToString();
                var description = device["Description"]?.ToString();
                if (!string.IsNullOrEmpty(deviceId))
                {
                    _connectedKeyboards.Add(deviceId);
                    System.Diagnostics.Debug.WriteLine($"发现键盘: {description} ({deviceId})");
                }
            }

            // 扫描 PnP 设备中的键盘（包括蓝牙键盘）
            using var pnpSearcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PnPEntity WHERE PNPClass = 'Keyboard' OR " +
                "Name LIKE '%keyboard%' OR Name LIKE '%键盘%' OR " +
                "Description LIKE '%keyboard%' OR Description LIKE '%键盘%'");

            foreach (ManagementObject device in pnpSearcher.Get())
            {
                var deviceId = device["DeviceID"]?.ToString();
                var name = device["Name"]?.ToString();
                if (!string.IsNullOrEmpty(deviceId) && !_connectedKeyboards.Contains(deviceId))
                {
                    // 过滤掉一些非物理键盘设备
                    if (!IsVirtualKeyboard(deviceId, name))
                    {
                        _connectedKeyboards.Add(deviceId);
                        System.Diagnostics.Debug.WriteLine($"发现PnP键盘: {name} ({deviceId})");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"扫描键盘失败: {ex.Message}");
        }

        KeyboardCountChanged?.Invoke(this, _connectedKeyboards.Count);
    }

    /// <summary>
    /// 判断是否是虚拟键盘（需要排除）
    /// </summary>
    private bool IsVirtualKeyboard(string? deviceId, string? name)
    {
        if (string.IsNullOrEmpty(deviceId)) return true;

        // 排除 Root 枚举的设备（通常是虚拟设备）
        if (deviceId.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase))
            return true;

        // 排除 HID 兼容设备中的一些虚拟设备
        if (name != null)
        {
            var lowerName = name.ToLower();
            if (lowerName.Contains("virtual") || lowerName.Contains("remote"))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 判断设备是否是键盘
    /// </summary>
    private bool IsKeyboardDevice(ManagementBaseObject device)
    {
        var pnpClass = device["PNPClass"]?.ToString();
        var name = device["Name"]?.ToString()?.ToLower() ?? "";
        var description = device["Description"]?.ToString()?.ToLower() ?? "";
        var deviceId = device["DeviceID"]?.ToString() ?? "";

        // 检查 PnP 类
        if (string.Equals(pnpClass, "Keyboard", StringComparison.OrdinalIgnoreCase))
            return true;

        // 检查名称和描述
        if (name.Contains("keyboard") || name.Contains("键盘") ||
            description.Contains("keyboard") || description.Contains("键盘"))
        {
            return !IsVirtualKeyboard(deviceId, name);
        }

        // 检查 HID 键盘设备
        if (deviceId.Contains("HID") && (deviceId.Contains("VID_") || deviceId.Contains("PID_")))
        {
            // HID 使用类代码来标识设备类型，键盘的使用页是 0x01，使用 ID 是 0x06
            // 但这在 WMI 中不容易获取，所以我们依赖名称检查
        }

        return false;
    }

    private void OnDeviceInserted(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var deviceId = targetInstance["DeviceID"]?.ToString();
            var description = targetInstance["Description"]?.ToString();

            if (!string.IsNullOrEmpty(deviceId) && !_connectedKeyboards.Contains(deviceId))
            {
                _connectedKeyboards.Add(deviceId);
                System.Diagnostics.Debug.WriteLine($"键盘已连接: {description} ({deviceId})");

                KeyboardConnected?.Invoke(this, new KeyboardEventArgs(deviceId, description ?? "Unknown"));
                KeyboardCountChanged?.Invoke(this, _connectedKeyboards.Count);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"处理设备插入事件失败: {ex.Message}");
        }
    }

    private void OnDeviceRemoved(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var deviceId = targetInstance["DeviceID"]?.ToString();
            var description = targetInstance["Description"]?.ToString();

            if (!string.IsNullOrEmpty(deviceId) && _connectedKeyboards.Remove(deviceId))
            {
                System.Diagnostics.Debug.WriteLine($"键盘已断开: {description} ({deviceId})");

                KeyboardDisconnected?.Invoke(this, new KeyboardEventArgs(deviceId, description ?? "Unknown"));
                KeyboardCountChanged?.Invoke(this, _connectedKeyboards.Count);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"处理设备移除事件失败: {ex.Message}");
        }
    }

    private void OnPnPDeviceInserted(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];

            if (IsKeyboardDevice(targetInstance))
            {
                var deviceId = targetInstance["DeviceID"]?.ToString();
                var name = targetInstance["Name"]?.ToString();

                if (!string.IsNullOrEmpty(deviceId) && !_connectedKeyboards.Contains(deviceId))
                {
                    _connectedKeyboards.Add(deviceId);
                    System.Diagnostics.Debug.WriteLine($"PnP键盘已连接: {name} ({deviceId})");

                    KeyboardConnected?.Invoke(this, new KeyboardEventArgs(deviceId, name ?? "Unknown"));
                    KeyboardCountChanged?.Invoke(this, _connectedKeyboards.Count);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"处理PnP设备插入事件失败: {ex.Message}");
        }
    }

    private void OnPnPDeviceRemoved(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var deviceId = targetInstance["DeviceID"]?.ToString();

            if (!string.IsNullOrEmpty(deviceId) && _connectedKeyboards.Remove(deviceId))
            {
                var name = targetInstance["Name"]?.ToString();
                System.Diagnostics.Debug.WriteLine($"PnP键盘已断开: {name} ({deviceId})");

                KeyboardDisconnected?.Invoke(this, new KeyboardEventArgs(deviceId, name ?? "Unknown"));
                KeyboardCountChanged?.Invoke(this, _connectedKeyboards.Count);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"处理PnP设备移除事件失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 停止监听
    /// </summary>
    public void Stop()
    {
        _insertWatcher?.Stop();
        _removeWatcher?.Stop();
        _pnpInsertWatcher?.Stop();
        _pnpRemoveWatcher?.Stop();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();

        _insertWatcher?.Dispose();
        _removeWatcher?.Dispose();
        _pnpInsertWatcher?.Dispose();
        _pnpRemoveWatcher?.Dispose();
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
