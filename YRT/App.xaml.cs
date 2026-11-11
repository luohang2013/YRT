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
        ClearLastStartupLog();
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

        // 启动动画：Splash 至少完整播放 2 秒，主窗口在 Splash 淡出时才渐显
        SplashWindow? splash = null;
        var splashStart = DateTime.Now;
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
            main.Opacity = 0; // Splash 结束前主窗口不可见，避免"动画没播完就开了"
            main.ContentRendered += async (_, _) =>
            {
                try
                {
                    var remain = TimeSpan.FromSeconds(2) - (DateTime.Now - splashStart);
                    if (remain > TimeSpan.Zero) await Task.Delay(remain);
                    splash?.CloseWithFade();
                    var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(
                        0, 1, TimeSpan.FromMilliseconds(320))
                    {
                        EasingFunction = new System.Windows.Media.Animation.CubicEase
                        {
                            EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
                        },
                    };
                    main.BeginAnimation(System.Windows.UIElement.OpacityProperty, fadeIn);
                }
                catch
                {
                    splash?.Close();
                    main.Opacity = 1;
                }
            };
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

    /// <summary>每次启动清除上一次的启动日志（日志统一放在数据目录 YRT\startup.log）。</summary>
    private static void ClearLastStartupLog()
    {
        foreach (var path in LogPaths())
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { // 删除失败不影响启动
            }
        }
    }

    /// <summary>启动期日志：落在数据目录 YRT\startup.log（便携目录不可写时回退 %TEMP%）。</summary>
    private static void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}\r\n";
        foreach (var path in LogPaths())
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, line);
                return; // 第一个可写位置即可
            }
            catch
            {
                // 换下一个位置
            }
        }
    }

    private static IEnumerable<string> LogPaths()
    {
        yield return Path.Combine(ResourceDeployer.DataDir, "startup.log");
        yield return Path.Combine(Path.GetTempPath(), "YRT", "startup.log");
    }
}
