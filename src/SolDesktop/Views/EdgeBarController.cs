using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SolDesktop.Services;

namespace SolDesktop.Views;

/// <summary>
/// Slides a screen-edge bar between shown, a thin peeking sliver and fully hidden (fullscreen apps),
/// based on the user's mode, window overlap and hover.
/// </summary>
public sealed class EdgeBarController
{
    private const double PeekSize = 5;
    private const double HiddenExtra = 40;

    private readonly FrameworkElement _bar;
    private readonly TranslateTransform _slide;
    private readonly ScreenEdge _edge;
    private readonly DispatcherTimer _leaveTimer;
    private readonly IEasingFunction _easing = new CubicEase { EasingMode = EasingMode.EaseOut };

    private EdgeBarMode _mode = EdgeBarMode.SmartHide;
    private bool _hovered;
    private bool _overlapped;
    private bool _fullscreen;
    private bool _keepOpen;
    private double _target = double.NaN;

    public EdgeBarController(FrameworkElement bar, TranslateTransform slide, ScreenEdge edge)
    {
        _bar = bar;
        _slide = slide;
        _edge = edge;

        _leaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _leaveTimer.Tick += (_, _) =>
        {
            _leaveTimer.Stop();
            _hovered = _bar.IsMouseOver;
            Update();
        };

        _bar.MouseEnter += (_, _) =>
        {
            _leaveTimer.Stop();
            _hovered = true;
            Update();
        };
        _bar.MouseLeave += (_, _) => _leaveTimer.Start();
        _bar.SizeChanged += (_, _) => Update(animate: false);
    }

    public EdgeBarMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            Update();
        }
    }

    /// <summary>Holds the bar open while one of its popups (menu, window preview) is showing.</summary>
    public bool KeepOpen
    {
        get => _keepOpen;
        set
        {
            _keepOpen = value;
            if (value) Update();
            else _leaveTimer.Start();
        }
    }

    public void SetEnvironment(bool overlapped, bool fullscreen)
    {
        if (overlapped == _overlapped && fullscreen == _fullscreen) return;
        _overlapped = overlapped;
        _fullscreen = fullscreen;
        Update();
    }

    private void Update(bool animate = true)
    {
        bool open = _hovered || _keepOpen;
        double height = _bar.ActualHeight;
        double direction = _edge == ScreenEdge.Top ? -1 : 1;

        double target;
        if (_fullscreen && !open) target = direction * (height + HiddenExtra);
        else if (open || _mode == EdgeBarMode.AlwaysVisible || (_mode == EdgeBarMode.SmartHide && !_overlapped)) target = 0;
        else target = direction * Math.Max(0, height - PeekSize);

        if (target.Equals(_target)) return;
        _target = target;

        if (!animate)
        {
            _slide.BeginAnimation(TranslateTransform.YProperty, null);
            _slide.Y = target;
            return;
        }

        var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(260)) { EasingFunction = _easing };
        _slide.BeginAnimation(TranslateTransform.YProperty, animation);
    }
}
