using System;
using System.IO;

namespace SolDesktop.Services;

public static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Sync = new();

    public static void Info(string message) => Write("info", message);

    public static void Error(Exception ex) => Write("error", ex.ToString());

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(SettingsService.DataFolder);
                string path = Path.Combine(SettingsService.DataFolder, "sol.log");
                Rotate(path);
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {level}: {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void Rotate(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= MaxBytes) return;

        string previous = path + ".1";
        if (File.Exists(previous)) File.Delete(previous);
        File.Move(path, previous);
    }
}
