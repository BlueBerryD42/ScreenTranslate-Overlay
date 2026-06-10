using System.IO;
using System.Text;

namespace GameTranslateOverlay.Services;

public sealed class LogService
{
    private static readonly Lazy<LogService> LazyInstance = new(() => new LogService());
    private readonly object _lock = new();

    public static LogService Instance => LazyInstance.Value;

    private LogService()
    {
        TrimOldLogs();
    }

    public string LogDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GameTranslateOverlay",
            "logs");

    public string DebugDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GameTranslateOverlay",
            "debug");

    public void EnsureDebugDirectory()
    {
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(DebugDirectory);
            }
            catch
            {
                // ignore
            }
        }
    }

    public string GetTodayLogPath()
    {
        var fileName = $"gto-{DateTime.Now:yyyy-MM-dd}.log";
        return Path.Combine(LogDirectory, fileName);
    }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    public void Error(string message, Exception ex) =>
        Write("ERROR", $"{message}{Environment.NewLine}{ex}");

    private void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(GetTodayLogPath(), line, Encoding.UTF8);
            }
            catch
            {
                // ignore logging failures
            }
        }
    }

    private void TrimOldLogs()
    {
        try
        {
            if (!Directory.Exists(LogDirectory))
                return;

            var cutoff = DateTime.Now.AddDays(-7);
            foreach (var file in Directory.GetFiles(LogDirectory, "gto-*.log"))
            {
                var info = new FileInfo(file);
                if (info.LastWriteTime < cutoff)
                    File.Delete(file);
            }
        }
        catch
        {
            // ignore
        }
    }
}
