param([ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet run --project tests/ThunderbirdAccounts.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Regressionstesterna misslyckades.' }
    dotnet publish src/ThunderbirdAccounts.App -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o "artifacts/$Runtime"
    if ($LASTEXITCODE -ne 0) { throw 'Publiceringen misslyckades.' }
    if ($IsWindows -or $env:OS -eq 'Windows_NT') {
        dotnet run --project tests/ThunderbirdAccounts.UiTests -c Release -- artifacts/ui-preview
        if ($LASTEXITCODE -ne 0) { throw 'WPF-gränssnittstestet misslyckades.' }
    }
    Write-Host "Klart: artifacts/$Runtime"
} finally { Pop-Location }
