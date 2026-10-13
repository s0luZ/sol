using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using SolDesktop.Interop;
using Forms = System.Windows.Forms;

namespace SolDesktop.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Icon _icon;

    public event EventHandler<bool>? NotchToggled;
    public event EventHandler<bool>? StartWithWindowsToggled;
    public event EventHandler? QuitRequested;

    public TrayIconService(bool notchEnabled, bool startWithWindows)
    {
        _icon = CreateSunIcon();

        var title = new Forms.ToolStripMenuItem("Sol Desktop") { Enabled = false };

        var notchItem = new Forms.ToolStripMenuItem("Show notch") { Checked = notchEnabled, CheckOnClick = true };
        notchItem.CheckedChanged += (_, _) => NotchToggled?.Invoke(this, notchItem.Checked);

        var dockItem = new Forms.ToolStripMenuItem("Dock (coming soon)") { Enabled = false };

        var startupItem = new Forms.ToolStripMenuItem("Start with Windows") { Checked = startWithWindows, CheckOnClick = true };
        startupItem.CheckedChanged += (_, _) => StartWithWindowsToggled?.Invoke(this, startupItem.Checked);

        var quitItem = new Forms.ToolStripMenuItem("Quit Sol Desktop", null, (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty));

        _menu = new Forms.ContextMenuStrip();
        _menu.Items.AddRange(new Forms.ToolStripItem[]
        {
            title,
            new Forms.ToolStripSeparator(),
            notchItem,
            dockItem,
            startupItem,
            new Forms.ToolStripSeparator(),
            quitItem,
        });

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "Sol Desktop",
            ContextMenuStrip = _menu,
            Visible = true,
        };
    }

    private static Icon CreateSunIcon()
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            Color sol = Color.FromArgb(255, 255, 181, 71);
            using var brush = new SolidBrush(sol);
            using var pen = new Pen(sol, 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };

            g.FillEllipse(brush, 9, 9, 14, 14);
            for (int i = 0; i < 8; i++)
            {
                double angle = i * Math.PI / 4;
                float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
                g.DrawLine(pen, 16 + cos * 11, 16 + sin * 11, 16 + cos * 14.5f, 16 + sin * 14.5f);
            }
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }
}
