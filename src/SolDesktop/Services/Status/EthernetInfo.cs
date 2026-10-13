using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SolDesktop.Services.Status;

public sealed record LabelValue(string Label, string Value);

public sealed record EthernetAdapter(string Name, bool IsUp, IReadOnlyList<LabelValue> Details);

public static class EthernetInfo
{
    private static readonly string[] VirtualMarkers = { "virtual", "hyper-v", "vmware", "virtualbox", "vpn", "tap-", "wan miniport", "loopback", "bluetooth" };

    /// <summary>Physical wired adapters, connected ones first.</summary>
    public static IReadOnlyList<EthernetAdapter> GetAdapters()
    {
        var result = new List<EthernetAdapter>();
        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
                    or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit))
                {
                    continue;
                }

                string description = nic.Description.ToLowerInvariant();
                if (VirtualMarkers.Any(description.Contains)) continue;

                bool up = nic.OperationalStatus == OperationalStatus.Up;
                var details = new List<LabelValue>
                {
                    new("Status", up ? "Connected" : "Cable unplugged"),
                    new("Adapter", nic.Description),
                };

                if (up)
                {
                    IPInterfaceProperties ip = nic.GetIPProperties();
                    details.Add(new("Speed", FormatSpeed(nic.Speed)));
                    AddIfAny(details, "IPv4 address", ip.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString()));
                    AddIfAny(details, "Gateway", ip.GatewayAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString()));
                    AddIfAny(details, "DNS", ip.DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()));
                    AddIfAny(details, "IPv6 address", ip.UnicastAddresses
                        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6 && !a.Address.IsIPv6LinkLocal)
                        .Select(a => a.Address.ToString()).Take(1));
                }

                byte[] mac = nic.GetPhysicalAddress().GetAddressBytes();
                if (mac.Length > 0) details.Add(new("MAC address", string.Join(":", mac.Select(b => b.ToString("X2")))));

                result.Add(new EthernetAdapter(nic.Name, up, details));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }

        return result.OrderByDescending(a => a.IsUp).ToList();
    }

    private static void AddIfAny(List<LabelValue> details, string label, IEnumerable<string> values)
    {
        string joined = string.Join(", ", values);
        if (joined.Length > 0) details.Add(new LabelValue(label, joined));
    }

    private static string FormatSpeed(long bitsPerSecond) => bitsPerSecond switch
    {
        <= 0 => "Unknown",
        >= 1_000_000_000 => $"{bitsPerSecond / 1_000_000_000.0:0.#} Gbps",
        _ => $"{bitsPerSecond / 1_000_000.0:0} Mbps",
    };
}
