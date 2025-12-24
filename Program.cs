namespace TabletModeSwitcher;

internal static class Program
{
    private static Mutex? _mutex;

    /// <summary>
    /// 应用程序的主入口点
    /// </summary>
    [STAThread]
    static void Main()
    {
        // 确保只有一个实例运行
        const string mutexName = "TabletModeSwitcher_SingleInstance";
        _mutex = new Mutex(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            MessageBox.Show(
                "平板模式切换器已经在运行中。\n请检查系统托盘。",
                "提示",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            // 配置应用程序
            ApplicationConfiguration.Initialize();

            // 设置未处理异常处理
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            // 运行托盘应用
            Application.Run(new TrayApplicationContext());
        }
        finally
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
    }

    private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
    {
        LogError(e.Exception);
        MessageBox.Show(
            $"发生错误: {e.Exception.Message}\n\n详细信息已记录到日志。",
            "错误",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogError(ex);
        }
    }

    private static void LogError(Exception ex)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TabletModeSwitcher",
                "logs");

            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            var logFile = Path.Combine(logDir, $"error_{DateTime.Now:yyyyMMdd}.log");
            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n";

            File.AppendAllText(logFile, logEntry);
        }
        catch
        {
            // 忽略日志写入失败
        }
    }
}
