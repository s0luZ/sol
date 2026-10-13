using System;
using System.Runtime.InteropServices;
using SolDesktop.Interop;

namespace SolDesktop.Services;

/// <summary>Reads shell metadata: app IDs of windows, shortcut targets and display names.</summary>
internal static class ShellProperties
{
    private static readonly PROPERTYKEY AppUserModelId = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
    private static readonly PROPERTYKEY LinkTargetParsingPath = new(new Guid("B9B4B3FC-2B51-4A42-B5D8-324146AFCF25"), 2);

    public static string? GetWindowAppId(IntPtr hwnd)
    {
        Guid iid = ShellInterop.IID_IPropertyStore;
        if (ShellInterop.SHGetPropertyStoreForWindow(hwnd, ref iid, out IPropertyStore store) != 0) return null;
        try
        {
            return GetString(store, AppUserModelId);
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    public static (string? TargetPath, string? AppId) GetShortcutInfo(string shortcutPath)
    {
        Guid iid = ShellInterop.IID_IPropertyStore;
        if (ShellInterop.SHGetPropertyStoreFromParsingName(shortcutPath, IntPtr.Zero, 0, ref iid, out IPropertyStore store) != 0)
        {
            return (null, null);
        }

        try
        {
            return (GetString(store, LinkTargetParsingPath), GetString(store, AppUserModelId));
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    public static string? GetDisplayName(string parsingName)
    {
        Guid iid = ShellInterop.IID_IShellItem;
        if (ShellInterop.SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out IShellItem item) != 0) return null;
        try
        {
            if (item.GetDisplayName(ShellInterop.SIGDN_NORMALDISPLAY, out IntPtr name) != 0) return null;
            try
            {
                return Marshal.PtrToStringUni(name);
            }
            finally
            {
                Marshal.FreeCoTaskMem(name);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(item);
        }
    }

    private static string? GetString(IPropertyStore store, PROPERTYKEY key)
    {
        if (store.GetValue(ref key, out PROPVARIANT value) != 0) return null;
        try
        {
            if (value.vt != ShellInterop.VT_LPWSTR) return null;
            string? text = Marshal.PtrToStringUni(value.pointerValue);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        finally
        {
            ShellInterop.PropVariantClear(ref value);
        }
    }
}
