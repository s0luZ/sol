using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SolDesktop.Interop;
using SolDesktop.Services;

namespace SolDesktop.Views;

/// <summary>
/// Month calendar owned by Sol. Windows notification history is not read; the buttons open the system pages.
/// </summary>
public partial class CalendarWindow : Window
{
    private DateTime _month;

    public CalendarWindow()
    {
        InitializeComponent();
        _month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        Deactivated += (_, _) => HideFlyout();
    }

    public event EventHandler? ClosedFlyout;

    public void ShowBelow(double centerX, double top)
    {
        _month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        RenderMonth();
        if (!IsVisible) Show();
        UpdateLayout();

        double width = ActualWidth > 0 ? ActualWidth : 292;
        double maxLeft = SystemParameters.PrimaryScreenWidth - width - 8;
        Left = Math.Max(8, Math.Min(centerX - width / 2, maxLeft));
        Top = top + 8;
        Activate();
    }

    public void HideFlyout()
    {
        if (!IsVisible) return;
        Hide();
        ClosedFlyout?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeMethods.AddExtendedStyle(new WindowInteropHelper(this).Handle, NativeMethods.WS_EX_TOOLWINDOW);
    }

    private void OnPrevious(object sender, RoutedEventArgs e)
    {
        _month = _month.AddMonths(-1);
        RenderMonth();
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        _month = _month.AddMonths(1);
        RenderMonth();
    }

    private void OnNotifications(object sender, RoutedEventArgs e) => OpenSettings("ms-settings:notifications");

    private void OnQuietHours(object sender, RoutedEventArgs e) => OpenSettings("ms-settings:quiethours");

    private void OpenSettings(string uri)
    {
        HideFlyout();
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    private void RenderMonth()
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        MonthTitle.Text = _month.ToString("MMMM yyyy", culture);

        Weekdays.Children.Clear();
        string[] names = culture.DateTimeFormat.AbbreviatedDayNames;
        int firstDay = (int)culture.DateTimeFormat.FirstDayOfWeek;
        for (int i = 0; i < 7; i++)
        {
            Weekdays.Children.Add(new TextBlock
            {
                Text = names[(firstDay + i) % 7],
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (Brush)FindResource("Sol.ForegroundDimBrush"),
                Margin = new Thickness(0, 2, 0, 2),
            });
        }

        Days.Children.Clear();
        int lead = ((int)_month.DayOfWeek - firstDay + 7) % 7;
        int count = DateTime.DaysInMonth(_month.Year, _month.Month);
        DateTime today = DateTime.Today;
        for (int i = 0; i < lead; i++) Days.Children.Add(new Border { Height = 32 });

        for (int day = 1; day <= count; day++)
        {
            bool isToday = _month.Year == today.Year && _month.Month == today.Month && day == today.Day;
            var label = new TextBlock
            {
                Text = day.ToString(culture),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = (Brush)FindResource("Sol.ForegroundBrush"),
            };
            var cell = new Border
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(14),
                BorderThickness = isToday ? new Thickness(1.5) : new Thickness(0),
                BorderBrush = isToday ? (Brush)FindResource("Sol.AccentBrush") : Brushes.Transparent,
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = label,
            };
            Days.Children.Add(cell);
        }
    }
}
