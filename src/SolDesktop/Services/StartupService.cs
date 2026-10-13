using System;
using Microsoft.Win32;

namespace SolDesktop.Services;

/// <summary>Registers Sol Desktop to launch at sign-in for the current user (no admin rights needed).</summary>
public static class StartupService
{
    public const string BackgroundArgument = "--background";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SolDesktop";

    public static bool IsEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            string exePath = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot resolve executable path.");
            key.SetValue(ValueName, $"\"{exePath}\" {BackgroundArgument}");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
