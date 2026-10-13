using System;
using System.Globalization;
using System.Windows.Data;

namespace SolDesktop.Converters;

/// <summary>Lights up a signal arc when the bar count reaches the threshold given as ConverterParameter.</summary>
public sealed class SignalBarOpacityConverter : IValueConverter
{
    public double ActiveOpacity { get; set; } = 1.0;
    public double InactiveOpacity { get; set; } = 0.28;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int bars = value is int i ? i : 0;
        int threshold = parameter is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int t) ? t : 1;
        return bars >= threshold ? ActiveOpacity : InactiveOpacity;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
