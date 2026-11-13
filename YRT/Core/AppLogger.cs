using System.IO;
using System.Text;
using YRT.FirstRun;

namespace YRT.Core;

/// <summary>按天保存的应用操作日志；日志写入失败不影响业务。</summary>
public static class AppLogger
{
    private static readonly object Sync = new();
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static string LogDirectory => Path.Combine(ResourceDeployer.DataDir, "logs");
    public static string CurrentLogPath => Path.Combine(LogDirectory, $"YRT-{DateTime.Now:yyyyMMdd}.log");

    public static void Page(string source, string message)
    {
        if (message.StartsWith("[!]", StringComparison.Ordinal))
            Warn(source, message);
        else
            Info(source, message);
    }

    public static void Info(string source, string message) => Write("INFO", source, message, null);
    public static void Warn(string source, string message) => Write("WARN", source, message, null);
    public static void Error(string source, string message, Exception? exception = null) =>
        Write("ERROR", source, message, exception);

    private static void Write(string level, string source, string message, Exception? exception)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{source}] {message}";
                if (exception is not null)
                    line += Environment.NewLine + exception;
                File.AppendAllText(CurrentLogPath, line + Environment.NewLine, Utf8NoBom);
            }
        }
        catch
        {
            // 日志目录不可写等异常不得打断业务，也不能递归记录。
        }
    }
}
