# Builds a standalone SolDesktop.exe into .\dist (no .NET install needed on the target PC).
# Single-file compression is left off on purpose: it roughly doubles RAM use because
# every assembly has to be inflated into memory instead of being mapped from disk.
$ErrorActionPreference = 'Stop'

dotnet publish "$PSScriptRoot\src\SolDesktop\SolDesktop.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o "$PSScriptRoot\dist"

Write-Host "`nBuilt: $PSScriptRoot\dist\SolDesktop.exe" -ForegroundColor Green
