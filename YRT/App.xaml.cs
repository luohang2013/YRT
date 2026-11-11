using System.IO;
using System.Windows;
using System.Windows.Threading;
using YRT.FirstRun;
using YRT.Theme;
using YRT.Views;

namespace YRT;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Log("==== startup begin ====");

        DispatcherUnhandledException += (_, args) =>
        {
            Log($"DispatcherUnhandledException: {args.Exception}");
            MessageBox.Show(args.Exception.ToString(), "YRT 错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log($"AppDomain.UnhandledException: {args.ExceptionObject}");

        try
        {
            base.OnStartup(e);
            Log("base.OnStartup done");
            ThemeManager.ApplySaved();
            Log("theme applied");
        }
        catch (Exception ex)
        {
            Log($"OnStartup catch: {ex}");
            MessageBox.Show(ex.ToString(), "YRT 启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        try
        {
            Log("before ResourceDeployer.Run");
            ResourceDeployer.Run();
            Log($"after ResourceDeployer.Run, DataDir={ResourceDeployer.DataDir}");
        }
        catch (Exception ex)
        {
            Log($"OnStartup catch: {ex}");
            MessageBox.Show($"首次运行初始化失败：{ex.InnerException?.Message ?? ex.Message}",
                "YRT", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // 启动动画：先弹 Splash，主窗口渲染完成后淡出
        SplashWindow? splash = null;
        try
        {
            splash = new SplashWindow();
            splash.Show();
        }
        catch (Exception ex)
        {
            Log($"splash failed (ignored): {ex.Message}");
        }

        try
        {
            var main = new MainWindow();
            MainWindow = main;
            if (splash is not null)
                main.ContentRendered += (_, _) => splash.CloseWithFade();
            main.Show();
            Log("main window shown");
        }
        catch (Exception ex)
        {
            Log($"main window failed: {ex}");
            splash?.Close();
            MessageBox.Show(ex.ToString(), "YRT 启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    /// <summary>启动期日志：多路径落盘（%TEMP%、exe 目录），保证至少一份可读。</summary>
    private static void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}\r\n";
        foreach (var path in LogPaths())
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, line);
            }
            catch
            {
                // 换下一个位置
            }
        }
    }

    private static IEnumerable<string> LogPaths()
    {
        yield return Path.Combine(Path.GetTempPath(), "YRT", "startup.log");
        yield return Path.Combine(AppContext.BaseDirectory, "startup.log");
    }
}
