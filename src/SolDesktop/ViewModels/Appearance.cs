using SolDesktop.Services;

namespace SolDesktop.ViewModels;

/// <summary>Live size/shape values for the bars, bound from XAML via x:Static.</summary>
public sealed class Appearance : ObservableObject
{
    public static Appearance Current { get; } = new();

    private double _notchScale = 1;
    private double _controlCentreScale = 1;
    private double _dockIconSize = 44;
    private double _dockMagnification = 1.6;
    private double _cornerRadius = 18;
    private double _flareRadius = 10;
    private bool _isGlass;

    public double NotchScale { get => _notchScale; private set => SetProperty(ref _notchScale, value); }
    public double ControlCentreScale { get => _controlCentreScale; private set => SetProperty(ref _controlCentreScale, value); }
    public double DockMagnification { get => _dockMagnification; private set => SetProperty(ref _dockMagnification, value); }
    public double CornerRadius { get => _cornerRadius; private set => SetProperty(ref _cornerRadius, value); }
    public double FlareRadius { get => _flareRadius; private set => SetProperty(ref _flareRadius, value); }
    public bool IsGlass { get => _isGlass; private set => SetProperty(ref _isGlass, value); }

    public double DockIconSize
    {
        get => _dockIconSize;
        private set
        {
            if (!SetProperty(ref _dockIconSize, value)) return;
            OnPropertyChanged(nameof(DockItemHeight));
            OnPropertyChanged(nameof(DockTileCornerRadius));
        }
    }

    /// <summary>Icon plus room for the running indicator underneath.</summary>
    public double DockItemHeight => DockIconSize + 10;

    public double DockTileCornerRadius => DockIconSize * 0.24;

    public void Update(AppSettings s)
    {
        NotchScale = s.NotchScale;
        ControlCentreScale = s.ControlCentreScale;
        DockIconSize = s.DockIconSize;
        DockMagnification = s.DockMagnification;
        CornerRadius = s.CornerRadius;
        FlareRadius = s.FlareRadius;
        IsGlass = s.Theme == ThemeKind.Glass;
    }
}
