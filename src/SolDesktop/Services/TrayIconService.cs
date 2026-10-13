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
    private readonly Forms.ToolStripMenuItem _notchItem;
    private readonly Forms.ToolStripMenuItem _dockItem;
    private readonly Icon _icon;
    private bool _syncing;

    public event EventHandler? SettingsRequested;
    public event EventHandler<bool>? NotchToggled;
    public event EventHandler<bool>? DockToggled;
    public event EventHandler? QuitRequested;

    public TrayIconService()
    {
        _icon = CreateSunIcon();

        var settingsItem = new Forms.ToolStripMenuItem("Sol settings…", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);

        _notchItem = new Forms.ToolStripMenuItem("Show notch") { CheckOnClick = true };
        _notchItem.CheckedChanged += (_, _) =>
        {
            if (!_syncing) NotchToggled?.Invoke(this, _notchItem.Checked);
        };

        _dockItem = new Forms.ToolStripMenuItem("Show dock") { CheckOnClick = true };
        _dockItem.CheckedChanged += (_, _) =>
        {
            if (!_syncing) DockToggled?.Invoke(this, _dockItem.Checked);
        };

        var quitItem = new Forms.ToolStripMenuItem("Quit Sol Desktop", null, (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty));

        _menu = new Forms.ContextMenuStrip();
        _menu.Items.AddRange(new Forms.ToolStripItem[]
        {
            settingsItem,
            new Forms.ToolStripSeparator(),
            _notchItem,
            _dockItem,
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
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) SettingsRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    public void Sync(bool notchEnabled, bool dockEnabled)
    {
        _syncing = true;
        _notchItem.Checked = notchEnabled;
        _dockItem.Checked = dockEnabled;
        _syncing = false;
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
