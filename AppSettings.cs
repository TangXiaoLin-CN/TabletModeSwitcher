using System.Text.Json;

namespace TabletModeSwitcher;

/// <summary>
/// 应用程序配置
/// </summary>
public class AppSettings
{
    /// <summary>
    /// 是否在系统启动时自动运行
    /// </summary>
    public bool RunAtStartup { get; set; } = false;

    /// <summary>
    /// 是否启用自动模式切换
    /// </summary>
    public bool AutoSwitchEnabled { get; set; } = true;

    /// <summary>
    /// 检测到键盘后延迟切换的毫秒数（避免频繁切换）
    /// </summary>
    public int SwitchDelayMs { get; set; } = 500;

    /// <summary>
    /// 是否在启动时最小化到托盘
    /// </summary>
    public bool StartMinimized { get; set; } = true;

    /// <summary>
    /// 是否显示通知
    /// </summary>
    public bool ShowNotifications { get; set; } = true;

    /// <summary>
    /// 是否同时控制任务栏自动隐藏（用于在非平板设备上测试）
    /// </summary>
    public bool UseTaskbarAutoHide { get; set; } = false;

    /// <summary>
    /// 排除的设备ID列表（这些设备不会触发模式切换）
    /// </summary>
    public List<string> ExcludedDeviceIds { get; set; } = new();

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TabletModeSwitcher",
        "settings.json");

    /// <summary>
    /// 加载配置
    /// </summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载配置失败: {ex.Message}");
        }
        return new AppSettings();
    }

    /// <summary>
    /// 保存配置
    /// </summary>
    public bool Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
            System.Diagnostics.Debug.WriteLine($"配置已保存到: {SettingsPath}");
            System.Diagnostics.Debug.WriteLine($"排除设备数量: {ExcludedDeviceIds.Count}");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存配置失败: {ex.Message}");
            return false;
        }
    }
}
