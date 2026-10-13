using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SolDesktop.Interop;

namespace SolDesktop.Services;

/// <summary>
/// Loads high-resolution shell icons on a dedicated STA thread so the dock never stalls the UI.
/// Each request lists parsing names to try in order (file path, shortcut, shell:AppsFolder\..., CLSID).
/// </summary>
public sealed class IconService : IDisposable
{
    private const int IconPixelSize = 128;

    private readonly BlockingCollection<(string[] Names, TaskCompletionSource<ImageSource?> Result)> _queue = new();
    private readonly ConcurrentDictionary<string, Task<ImageSource?>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Thread _worker;

    public IconService()
    {
        _worker = new Thread(Work) { IsBackground = true, Name = "Sol icon loader" };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    public Task<ImageSource?> GetAsync(params string?[] parsingNames)
    {
        string[] names = parsingNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).ToArray();
        if (names.Length == 0) return Task.FromResult<ImageSource?>(null);

        return _cache.GetOrAdd(string.Join("|", names), _ =>
        {
            var tcs = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add((names, tcs));
            return tcs.Task;
        });
    }

    public void Invalidate(params string[] parsingNames) => _cache.TryRemove(string.Join("|", parsingNames), out _);

    private void Work()
    {
        foreach (var (names, result) in _queue.GetConsumingEnumerable())
        {
            ImageSource? image = null;
            foreach (string name in names)
            {
                image = TryLoad(name);
                if (image is not null) break;
            }

            result.TrySetResult(image);
        }
    }

    private static ImageSource? TryLoad(string parsingName)
    {
        Guid iid = ShellInterop.IID_IShellItemImageFactory;
        if (ShellInterop.SHCreateImageFactoryFromParsingName(parsingName, IntPtr.Zero, ref iid, out IShellItemImageFactory factory) != 0)
        {
            return null;
        }

        IntPtr hbitmap = IntPtr.Zero;
        try
        {
            var size = new NativeMethods.SIZE { cx = IconPixelSize, cy = IconPixelSize };
            if (factory.GetImage(size, ShellInterop.SIIGBF_ICONONLY | ShellInterop.SIIGBF_BIGGERSIZEOK, out hbitmap) != 0) return null;
            return ToBitmapSource(hbitmap);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return null;
        }
        finally
        {
            if (hbitmap != IntPtr.Zero) NativeMethods.DeleteObject(hbitmap);
            Marshal.ReleaseComObject(factory);
        }
    }

    private static BitmapSource? ToBitmapSource(IntPtr hbitmap)
    {
        if (NativeMethods.GetObject(hbitmap, Marshal.SizeOf<NativeMethods.BITMAP>(), out NativeMethods.BITMAP bmp) == 0) return null;

        int width = bmp.bmWidth;
        int height = Math.Abs(bmp.bmHeight);
        var header = new NativeMethods.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
        };

        byte[] pixels = new byte[width * height * 4];
        IntPtr dc = NativeMethods.GetDC(IntPtr.Zero);
        try
        {
            if (NativeMethods.GetDIBits(dc, hbitmap, 0, (uint)height, pixels, ref header, 0) == 0) return null;
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, dc);
        }

        // Legacy icons can come back with an all-zero alpha channel; treat those as opaque.
        bool hasAlpha = false;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                hasAlpha = true;
                break;
            }
        }

        if (!hasAlpha)
        {
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        }

        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        source.Freeze();
        return source;
    }

    public void Dispose() => _queue.CompleteAdding();
}
