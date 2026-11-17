#Requires -Version 7.0
<#
.SYNOPSIS
    Testet und baut das NSTN-Plugin im .NET-SDK-Container und legt das Installations-ZIP an.

.DESCRIPTION
    Kein lokales .NET-SDK nötig, nur Docker. Ablauf:
      1. Goldtests des Nachtmodells (Nstn.Core.Tests)
      2. Release-Build des Plugins -> dist\NSTN\ (genau drei DLLs)
      3. dist\NINA.Plugin.Nstn-<Version>.zip mit dem Ordner NSTN\
    Das ZIP wird nach %LOCALAPPDATA%\NINA\Plugins\3.0.0\ entpackt.

.PARAMETER SkipTests
    Nur bauen. Für schnelle Oberflächen-Iterationen; vor jeder Weitergabe nicht verwenden.
#>
param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$image = 'mcr.microsoft.com/dotnet/sdk:10.0'

function Invoke-Sdk([string]$command) {
    # Benanntes Volume als NuGet-Cache, damit nicht jeder Lauf alles neu lädt.
    docker run --rm -v "${root}:/src" -v nstn-nuget:/root/.nuget/packages -w /src $image sh -c $command
    if ($LASTEXITCODE -ne 0) { throw "Container-Lauf fehlgeschlagen: $command" }
}

if (-not $SkipTests) {
    Invoke-Sdk 'dotnet test tests/Nstn.Core.Tests/Nstn.Core.Tests.csproj -c Release --nologo -v q'
}
Invoke-Sdk 'dotnet build src/NINA.Plugin.Nstn/NINA.Plugin.Nstn.csproj -c Release --nologo -v q'

# Version aus der Assembly-Metadatenzeile lesen, nicht doppelt pflegen.
$assemblyInfo = Get-Content -LiteralPath (Join-Path $root 'src\NINA.Plugin.Nstn\Properties\AssemblyInfo.cs') -Raw -Encoding UTF8
if ($assemblyInfo -notmatch 'AssemblyFileVersion\("([0-9.]+)"\)') { throw 'AssemblyFileVersion nicht gefunden' }
$version = $Matches[1]

$stage = Join-Path $root 'dist\NSTN'
$files = Get-ChildItem -LiteralPath $stage -File
if ($files.Count -ne 3) { throw "dist\NSTN enthält $($files.Count) statt 3 Dateien" }

$zip = Join-Path $root "dist\NINA.Plugin.Nstn-$version.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
Compress-Archive -Path $stage -DestinationPath $zip

$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Plugin: $zip"
Write-Host "SHA-256: $hash"
$files | ForEach-Object { Write-Host ("  {0,-28} {1,8:N0} Bytes" -f $_.Name, $_.Length) }
