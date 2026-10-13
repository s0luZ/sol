using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SolDesktop.Services;

namespace SolDesktop.Controls;

/// <summary>
/// A bar that grows out of a screen edge: concave "flares" where it meets the edge
/// and rounded corners on the side facing the desktop, like the macOS notch.
/// </summary>
public sealed class FlaredPanel : Decorator
{
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background), typeof(Brush), typeof(FlaredPanel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush), typeof(Brush), typeof(FlaredPanel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
        nameof(BorderThickness), typeof(double), typeof(FlaredPanel),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(FlaredPanel),
        new FrameworkPropertyMetadata(18.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FlareRadiusProperty = DependencyProperty.Register(
        nameof(FlareRadius), typeof(double), typeof(FlaredPanel),
        new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EdgeProperty = DependencyProperty.Register(
        nameof(Edge), typeof(ScreenEdge), typeof(FlaredPanel),
        new FrameworkPropertyMetadata(ScreenEdge.Top, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding), typeof(Thickness), typeof(FlaredPanel),
        new FrameworkPropertyMetadata(default(Thickness), FrameworkPropertyMetadataOptions.AffectsMeasure));

    public Brush? Background { get => (Brush?)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public Brush? BorderBrush { get => (Brush?)GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }
    public double BorderThickness { get => (double)GetValue(BorderThicknessProperty); set => SetValue(BorderThicknessProperty, value); }
    public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public double FlareRadius { get => (double)GetValue(FlareRadiusProperty); set => SetValue(FlareRadiusProperty, value); }
    public ScreenEdge Edge { get => (ScreenEdge)GetValue(EdgeProperty); set => SetValue(EdgeProperty, value); }
    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }

    protected override Size MeasureOverride(Size constraint)
    {
        Thickness p = Padding;
        double horizontal = 2 * FlareRadius + p.Left + p.Right;
        double vertical = p.Top + p.Bottom;
        if (Child is null) return new Size(horizontal, vertical);

        Child.Measure(new Size(Math.Max(0, constraint.Width - horizontal), Math.Max(0, constraint.Height - vertical)));
        return new Size(Child.DesiredSize.Width + horizontal, Child.DesiredSize.Height + vertical);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Thickness p = Padding;
        double f = FlareRadius;
        Child?.Arrange(new Rect(
            f + p.Left,
            p.Top,
            Math.Max(0, arrangeSize.Width - 2 * f - p.Left - p.Right),
            Math.Max(0, arrangeSize.Height - p.Top - p.Bottom)));
        return arrangeSize;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = RenderSize.Width;
        double h = RenderSize.Height;
        if (w <= 0 || h <= 0) return;

        double f = Math.Max(0, Math.Min(FlareRadius, h / 2));
        double c = Math.Max(0, Math.Min(CornerRadius, Math.Min((w - 2 * f) / 2, h - f)));
        bool top = Edge == ScreenEdge.Top;

        // Geometry is authored for a top-edge bar and mirrored vertically for the bottom edge.
        Point P(double x, double y) => new(x, top ? y : h - y);
        SweepDirection sweep = top ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
        var corner = new Size(c, c);

        void Outline(StreamGeometryContext g)
        {
            g.QuadraticBezierTo(P(w - f, 0), P(w - f, f), true, true);
            g.LineTo(P(w - f, h - c), true, false);
            g.ArcTo(P(w - f - c, h), corner, 0, false, sweep, true, true);
            g.LineTo(P(f + c, h), true, false);
            g.ArcTo(P(f, h - c), corner, 0, false, sweep, true, true);
            g.LineTo(P(f, f), true, false);
            g.QuadraticBezierTo(P(f, 0), P(0, 0), true, true);
        }

        var fill = new StreamGeometry();
        using (StreamGeometryContext g = fill.Open())
        {
            g.BeginFigure(P(0, 0), true, true);
            g.LineTo(P(w, 0), false, false);
            Outline(g);
        }
        fill.Freeze();
        dc.DrawGeometry(Background, null, fill);

        if (BorderBrush is null || BorderThickness <= 0) return;

        // The edge touching the screen border is left open so it reads as part of the bezel.
        var stroke = new StreamGeometry();
        using (StreamGeometryContext g = stroke.Open())
        {
            g.BeginFigure(P(w, 0), false, false);
            Outline(g);
        }
        stroke.Freeze();
        dc.DrawGeometry(null, new Pen(BorderBrush, BorderThickness), stroke);
    }
}
