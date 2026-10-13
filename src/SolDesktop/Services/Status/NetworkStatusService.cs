using System;
using System.Windows.Threading;
using Windows.Networking.Connectivity;

namespace SolDesktop.Services.Status;

public enum NetworkKind
{
    None,
    WiFi,
    Ethernet,
    Cellular,
    Other,
}

public sealed class NetworkStatusService : IDisposable
{
    private const uint IanaEthernet = 6;

    private readonly Dispatcher _dispatcher;

    public NetworkKind Kind { get; private set; }
    public string? Name { get; private set; }

    /// <summary>Signal strength 0-5 as reported by Windows (Wi-Fi/cellular only).</summary>
    public int SignalBars { get; private set; }

    public bool HasInternet { get; private set; }

    public event EventHandler? Changed;

    public NetworkStatusService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
        Refresh();
    }

    private void OnNetworkStatusChanged(object sender) => _dispatcher.InvokeAsync(Refresh);

    public void Refresh()
    {
        try
        {
            ConnectionProfile? profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is null)
            {
                Kind = NetworkKind.None;
                Name = null;
                SignalBars = 0;
                HasInternet = false;
            }
            else
            {
                HasInternet = profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
                SignalBars = profile.GetSignalBars() ?? 0;

                if (profile.IsWlanConnectionProfile)
                {
                    Kind = NetworkKind.WiFi;
                    Name = profile.WlanConnectionProfileDetails.GetConnectedSsid();
                }
                else if (profile.IsWwanConnectionProfile)
                {
                    Kind = NetworkKind.Cellular;
                    Name = profile.ProfileName;
                }
                else if (profile.NetworkAdapter?.IanaInterfaceType == IanaEthernet)
                {
                    Kind = NetworkKind.Ethernet;
                    Name = profile.ProfileName;
                }
                else
                {
                    Kind = NetworkKind.Other;
                    Name = profile.ProfileName;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
}
