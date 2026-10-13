using System;
using System.IO;

namespace SolDesktop.Services;

public static class Log
{
    private static readonly object Sync = new();

    public static void Error(Exception ex)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(SettingsService.DataFolder);
                File.AppendAllText(
                    Path.Combine(SettingsService.DataFolder, "sol.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
        }
    }
}
