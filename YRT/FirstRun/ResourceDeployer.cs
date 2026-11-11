using System.IO;
using System.Reflection;

namespace YRT.FirstRun;

/// <summary>
/// 首次运行引导：确定数据目录（PCL 风格：exe 同级 YRT\，只读则回退 %APPDATA%\YRT），
/// 并把内嵌资源解压进去（已存在则跳过）。
/// 旧版数据目录 data\ 会自动整体迁移为 YRT\（含 settings.json 等全部数据）。
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
        // 首选：exe 同级 YRT\（便携式定位，PCL 风格——数据都收在同名文件夹里）
        try
        {
            var exeDir = AppContext.BaseDirectory;
            var probe = Path.Combine(exeDir, ".yrt_write_test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            var dir = Path.Combine(exeDir, "YRT");
            MigrateLegacyDataDir(Path.Combine(exeDir, "data"), dir);
            return dir;
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

    /// <summary>旧版 data\ → 新版 YRT\ 一次性迁移：YRT 不存在且 data 存在时整体改名。</summary>
    private static void MigrateLegacyDataDir(string legacyDir, string newDir)
    {
        try
        {
            if (!Directory.Exists(legacyDir) || Directory.Exists(newDir)) return;
            Directory.Move(legacyDir, newDir);
        }
        catch
        {
            // 迁移失败不阻塞启动（新版会按默认值重建数据）
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
