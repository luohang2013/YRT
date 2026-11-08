using System.IO;
using System.Reflection;

namespace YRT.FirstRun;

/// <summary>
/// 首次运行引导：确定数据目录（exe 同级 data\，只读则回退 %APPDATA%\YRT），
/// 并把内嵌资源解压进去（已存在则跳过）。
/// 任何单步失败都不应让应用无法启动。
/// </summary>
public static class ResourceDeployer
{
    public static string DataDir { get; } = ResolveDataDir();

    public static void Run()
    {
        Directory.CreateDirectory(DataDir);
        ExtractEmbedded("YRT.Resources.welcome.txt", Path.Combine(DataDir, "welcome.txt"));
    }

    private static string ResolveDataDir()
    {
        // 首选：exe 同级 data\（便携式定位）
        try
        {
            var exeDir = AppContext.BaseDirectory;
            var probe = Path.Combine(exeDir, ".yrt_write_test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return Path.Combine(exeDir, "data");
        }
        catch
        {
            // 回退：%APPDATA%\YRT
            try
            {
                var appData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YRT");
                Directory.CreateDirectory(appData);
                return appData;
            }
            catch
            {
                // 最终回退：当前目录（保证应用可启动）
                return Directory.GetCurrentDirectory();
            }
        }
    }

    private static void ExtractEmbedded(string resourceName, string targetPath)
    {
        if (File.Exists(targetPath)) return;

        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"内嵌资源缺失: {resourceName}");
        using var file = File.Create(targetPath);
        stream.CopyTo(file);
    }
}
