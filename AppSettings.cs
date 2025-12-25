using System.Text.Json;

namespace TabletModeSwitcher;

/// <summary>
/// 应用程序配置
/// </summary>
public class AppSettings
{
    public bool RunAtStartup { get; set; } = false;
    public bool AutoSwitchEnabled { get; set; } = true;
    public int SwitchDelayMs { get; set; } = 500;
    public bool StartMinimized { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public bool UseTaskbarAutoHide { get; set; } = false;
    public List<string> ExcludedDeviceIds { get; set; } = new();

    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TabletModeSwitcher");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    // 确保列表不为 null
                    settings.ExcludedDeviceIds ??= new List<string>();
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加载配置失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        return new AppSettings();
    }

    public bool Save()
    {
        try
        {
            // 确保目录存在
            if (!Directory.Exists(SettingsDir))
            {
                Directory.CreateDirectory(SettingsDir);
            }

            // 序列化并保存
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(SettingsPath, json);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存配置失败: {ex.Message}\n路径: {SettingsPath}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }
}
