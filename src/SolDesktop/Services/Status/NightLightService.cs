using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace SolDesktop.Services.Status;

/// <summary>
/// Windows Night Light. There is no public API; the Settings app stores the on/off state and the
/// color temperature in CloudStore registry blobs. Sol only rewrites those blobs when they already
/// exist, and leaves the schedule bytes alone.
/// </summary>
public sealed class NightLightService
{
    private const int WarmestKelvin = 1200;
    private const int CoolestKelvin = 6500;

    private const string StateKey = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";
    private const string SettingsKey = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.settings\windows.data.bluelightreduction.settings";

    public readonly record struct Status(bool Supported, bool Enabled, int Strength);

    public Status Read()
    {
        byte[]? state = ReadBlob(StateKey);
        byte[]? settings = ReadBlob(SettingsKey);
        if (state is null || settings is null || !TrySplit(state, out Blob stateBlob))
            return new Status(false, false, 50);

        bool enabled = IsEnabled(stateBlob.Tail);
        int strength = 50;
        if (TrySplit(settings, out Blob settingsBlob) && TryReadKelvin(settingsBlob.Tail, out int kelvin))
            strength = KelvinToStrength(kelvin);

        return new Status(true, enabled, strength);
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            byte[]? state = ReadBlob(StateKey);
            if (state is null || !TrySplit(state, out Blob blob)) return;
            if (IsEnabled(blob.Tail) == enabled) return;

            var tail = new List<byte>(blob.Tail);
            if (enabled) tail.InsertRange(1, new byte[] { 0x10, 0x00 });
            else if (tail.Count >= 3 && tail[1] == 0x10 && tail[2] == 0x00) tail.RemoveRange(1, 2);

            ReplaceVarUInt(tail, new byte[] { 0xC6, 0x14 }, (ulong)DateTime.UtcNow.ToFileTimeUtc());
            WriteBlob(StateKey, Rebuild(blob, tail));
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    public void SetStrength(int percent)
    {
        try
        {
            byte[]? settings = ReadBlob(SettingsKey);
            if (settings is null || !TrySplit(settings, out Blob blob)) return;

            int kelvin = StrengthToKelvin(percent);
            var tail = new List<byte>(blob.Tail);
            if (!ReplaceVarUInt(tail, new byte[] { 0xCF, 0x28 }, EncodeZigZag(kelvin))) return;
            WriteBlob(SettingsKey, Rebuild(blob, tail));
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    private static bool IsEnabled(byte[] tail) =>
        tail.Length >= 3 && tail[0] == 0x00 && tail[1] == 0x10 && tail[2] == 0x00;

    private static int KelvinToStrength(int kelvin)
    {
        double span = CoolestKelvin - WarmestKelvin;
        int strength = (int)Math.Round((CoolestKelvin - kelvin) * 100.0 / span);
        return Math.Clamp(strength, 0, 100);
    }

    private static int StrengthToKelvin(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        return CoolestKelvin - (int)Math.Round(percent / 100.0 * (CoolestKelvin - WarmestKelvin));
    }

    private static bool TryReadKelvin(byte[] tail, out int kelvin)
    {
        kelvin = 0;
        int at = IndexOf(tail, new byte[] { 0xCF, 0x28 });
        if (at < 0) return false;
        int index = at + 2;
        if (index >= tail.Length) return false;
        uint encoded = (uint)ReadVarUInt(tail, ref index);
        kelvin = DecodeZigZag(encoded);
        return kelvin is >= 1000 and <= 10000;
    }

    private readonly record struct Blob(byte[] Prefix, ulong Timestamp, byte[] Tail);

    /// <summary>Prefix ends at the timestamp. Tail is every byte after the inner 43 42 01 marker.</summary>
    private static bool TrySplit(byte[] data, out Blob blob)
    {
        blob = default;
        int marker = IndexOf(data, new byte[] { 0x2A, 0x06 });
        if (marker < 0) return false;

        int index = marker + 2;
        ulong timestamp = ReadVarUInt(data, ref index);
        if (index + 3 > data.Length || data[index] != 0x2A || data[index + 1] != 0x2B || data[index + 2] != 0x0E)
            return false;

        index += 3;
        ReadVarUInt(data, ref index);
        if (index + 3 > data.Length || data[index] != 0x43 || data[index + 1] != 0x42 || data[index + 2] != 0x01)
            return false;

        var prefix = new byte[marker + 2];
        Buffer.BlockCopy(data, 0, prefix, 0, prefix.Length);
        var tail = new byte[data.Length - (index + 3)];
        Buffer.BlockCopy(data, index + 3, tail, 0, tail.Length);
        blob = new Blob(prefix, timestamp, tail);
        return tail.Length > 0;
    }

    private static byte[] Rebuild(Blob blob, List<byte> tail)
    {
        ulong timestamp = Math.Max(blob.Timestamp + 2, (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var output = new List<byte>(blob.Prefix.Length + tail.Count + 16);
        output.AddRange(blob.Prefix);
        WriteVarUInt(output, timestamp);
        output.Add(0x2A);
        output.Add(0x2B);
        output.Add(0x0E);
        WriteVarUInt(output, (ulong)tail.Count);
        output.Add(0x43);
        output.Add(0x42);
        output.Add(0x01);
        output.AddRange(tail);
        return output.ToArray();
    }

    private static bool ReplaceVarUInt(List<byte> tail, byte[] marker, ulong value)
    {
        int at = IndexOf(tail, marker);
        if (at < 0) return false;
        int start = at + marker.Length;
        int end = start;
        var array = tail.ToArray();
        if (end >= array.Length) return false;
        ReadVarUInt(array, ref end);
        var encoded = new List<byte>(8);
        WriteVarUInt(encoded, value);
        tail.RemoveRange(start, end - start);
        tail.InsertRange(start, encoded);
        return true;
    }

    private static int IndexOf(IReadOnlyList<byte> data, byte[] pattern)
    {
        for (int i = 0; i <= data.Count - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] == pattern[j]) continue;
                match = false;
                break;
            }

            if (match) return i;
        }

        return -1;
    }

    private static ulong ReadVarUInt(IReadOnlyList<byte> data, ref int index)
    {
        ulong result = 0;
        int shift = 0;
        while (index < data.Count && shift < 64)
        {
            byte b = data[index++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }

        return result;
    }

    private static void WriteVarUInt(List<byte> destination, ulong value)
    {
        while (value >= 0x80)
        {
            destination.Add((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }

        destination.Add((byte)value);
    }

    private static uint EncodeZigZag(int value) => (uint)((value << 1) ^ (value >> 31));

    private static int DecodeZigZag(uint value) => (int)((value >> 1) ^ -(int)(value & 1));

    private static byte[]? ReadBlob(string path)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValue("Data") as byte[];
    }

    private static void WriteBlob(string path, byte[] data)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(path, writable: true);
        key?.SetValue("Data", data, RegistryValueKind.Binary);
    }
}
