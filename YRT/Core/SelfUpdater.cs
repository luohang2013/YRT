using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace YRT.Core;

/// <summary>
/// 自动更新落地：把下载好的 Release zip 预解压到数据目录 update\new\，
/// 再生成并启动一个批处理（等主程序退出 → 覆盖 YRT.exe → 重启 → 清理），完成自替换。
/// </summary>
public static class SelfUpdater
{
    /// <summary>更新工作目录（数据目录 YRT\update\）。</summary>
    public static string UpdateDir =>
        Path.Combine(FirstRun.ResourceDeployer.DataDir, "update");

    /// <summary>
    /// 预解压安装包并准备替换脚本。成功后已启动替换批处理，
    /// 调用方应尽快退出主程序（返回 false 表示失败，不退出）。
    /// </summary>
    public static bool PrepareInstall(string zipPath, out string error)
    {
        error = "";
        try
        {
            var exeDir = AppContext.BaseDirectory;
            var exePath = Path.Combine(exeDir, "YRT.exe");
            var newDir = Path.Combine(UpdateDir, "new");
            if (Directory.Exists(newDir)) Directory.Delete(newDir, recursive: true);
            Directory.CreateDirectory(newDir);

            // 解压（zip 里可能整体多包一层目录，解完自动找 YRT.exe）
            ZipFile.ExtractToDirectory(zipPath, newDir, overwriteFiles: true);
            var newExe = Directory.EnumerateFiles(newDir, "YRT.exe", SearchOption.AllDirectories)
                .FirstOrDefault()
                ?? throw new FileNotFoundException("安装包里没有找到 YRT.exe");

            // 生成替换脚本：等主程序退出 → 覆盖 → 重启 → 清理
            var bat = Path.Combine(UpdateDir, "update.bat");
            var lines = new[]
            {
                "@echo off",
                "chcp 65001 >nul",
                "timeout /t 2 /nobreak >nul",
                $":wait",
                $"tasklist /FI \"IMAGENAME eq YRT.exe\" 2>nul | find /I \"YRT.exe\" >nul",
                "if not errorlevel 1 ( timeout /t 1 /nobreak >nul & goto wait )",
                $"copy /y \"{newExe}\" \"{exePath}\" >nul",
                $"start \"\" \"{exePath}\"",
                $"rmdir /s /q \"{newDir}\"",
                "del \"%~f0\"",
            };
            File.WriteAllLines(bat, lines);

            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
