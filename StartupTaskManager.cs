using System.Diagnostics;

namespace TabletModeSwitcher;

/// <summary>
/// 使用任务计划程序管理开机自启动
/// 任务计划程序可以在用户登录时以管理员权限启动程序，无需 UAC 提示
/// </summary>
public static class StartupTaskManager
{
    private const string TaskName = "TabletModeSwitcher";

    /// <summary>
    /// 检查是否已设置开机自启动
    /// </summary>
    public static bool IsStartupEnabled()
    {
        try
        {
            var result = RunSchtasks($"/Query /TN \"{TaskName}\" /FO LIST", out _);
            return result;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 设置开机自启动
    /// </summary>
    public static bool EnableStartup()
    {
        try
        {
            var exePath = Application.ExecutablePath;

            // 先删除可能存在的旧任务
            RunSchtasks($"/Delete /TN \"{TaskName}\" /F", out _);

            // 创建新任务：在用户登录时以最高权限运行
            // /SC ONLOGON - 在用户登录时触发
            // /RL HIGHEST - 以最高权限运行（管理员）
            // /F - 强制创建，覆盖已存在的任务
            var args = $"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL HIGHEST /F";

            if (!RunSchtasks(args, out string error))
            {
                Debug.WriteLine($"创建任务计划失败: {error}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"设置开机自启动失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 取消开机自启动
    /// </summary>
    public static bool DisableStartup()
    {
        try
        {
            return RunSchtasks($"/Delete /TN \"{TaskName}\" /F", out _);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"取消开机自启动失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 设置开机自启动状态
    /// </summary>
    public static bool SetStartup(bool enable)
    {
        return enable ? EnableStartup() : DisableStartup();
    }

    private static bool RunSchtasks(string arguments, out string error)
    {
        error = string.Empty;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                error = "无法启动 schtasks.exe";
                return false;
            }

            process.WaitForExit(5000);

            if (process.ExitCode != 0)
            {
                error = process.StandardError.ReadToEnd();
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
