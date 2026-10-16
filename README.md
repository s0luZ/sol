# Sol Desktop

A mac-style notch, Control Centre, and dock for Windows 10 and 11.

## Download

1. Open **[Releases](https://github.com/s0luZ/sol/releases)**.
2. Download `SolDesktop.exe`.
3. Double-click it.

No .NET install is required. Use **64-bit Windows 10 or 11**. The latest notes are in [CHANGELOG.md](CHANGELOG.md).

Windows may show a SmartScreen warning because the app is not code-signed. Choose **More info → Run anyway**.

The first launch opens **Sol settings**. The notch sits at the top of the screen; the dock at the bottom. Right-click either, or the sun icon in the tray, for options. Turn on **Start with Windows** in General if you want it at sign-in.

Quit from the tray icon or from Settings so the Windows taskbar comes back.

## Build from source

```powershell
git clone https://github.com/s0luZ/sol.git
cd sol
.\publish.ps1
```

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). The exe is written to `dist\SolDesktop.exe`.
