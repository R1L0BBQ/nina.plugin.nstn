#Requires -Version 7.0
<#
.SYNOPSIS
    Testet und baut das NSTN-Plugin im .NET-SDK-Container und legt das Installations-ZIP an.

.DESCRIPTION
    Kein lokales .NET-SDK nötig, nur Docker. Ablauf:
      1. Goldtests des Nachtmodells (Nstn.Core.Tests)
      2. Release-Build des Plugins -> dist\NSTN\ (genau drei DLLs und THIRD-PARTY-NOTICES.txt)
      3. dist\NINA.Plugin.Nstn-<Version>.zip mit den Dateien aus NSTN\ (ohne Ordner): das Archiv
         für den Plugin-Manager; von Hand nach %LOCALAPPDATA%\NINA\Plugins\3.0.0\NSTN\ entpacken
      4. dist\NSTN-Installer-<Version>.zip zum Weitergeben (installer\Pack-Installer.ps1)
      5. dist\manifest.json für das Plugin-Repository von N.I.N.A. (installer\New-Manifest.ps1)
    Danach tests\Run-NinaChecks.ps1: es prüft genau die Dateien aus dist\NSTN.

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
# Nur dieser Build legt dist\NSTN an; tests\Run-NinaChecks.ps1 prüft genau diese Dateien.
Invoke-Sdk 'dotnet build src/NINA.Plugin.Nstn/NINA.Plugin.Nstn.csproj -c Release --nologo -v q -p:NstnStage=true'

# Version aus der Assembly-Metadatenzeile lesen, nicht doppelt pflegen.
$assemblyInfo = Get-Content -LiteralPath (Join-Path $root 'src\NINA.Plugin.Nstn\Properties\AssemblyInfo.cs') -Raw -Encoding UTF8
if ($assemblyInfo -notmatch 'AssemblyFileVersion\("([0-9.]+)"\)') { throw 'AssemblyFileVersion nicht gefunden' }
$version = $Matches[1]

$stage = Join-Path $root 'dist\NSTN'
$files = @(Get-ChildItem -LiteralPath $stage -File)
$dlls = @($files | Where-Object Extension -eq '.dll')
if ($dlls.Count -ne 3 -or $files.Count -ne 4 -or -not ($files.Name -contains 'THIRD-PARTY-NOTICES.txt')) {
    throw "dist\NSTN soll drei DLLs und THIRD-PARTY-NOTICES.txt enthalten, enthält: $($files.Name -join ', ')"
}

$zip = Join-Path $root "dist\NINA.Plugin.Nstn-$version.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
# Die Dateien liegen direkt im ZIP: Der Plugin-Manager von N.I.N.A. entpackt ein ARCHIVE in
# Plugins\3.0.0\<Name>, von Hand kommt der Inhalt nach Plugins\3.0.0\NSTN.
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip

$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Plugin: $zip"
Write-Host "SHA-256: $hash"
$files | ForEach-Object { Write-Host ("  {0,-28} {1,8:N0} Bytes" -f $_.Name, $_.Length) }

# Der Installer zum Weitergeben (installer\): dist\NSTN-Installer-<Version>.zip.
& (Join-Path $root 'installer\Pack-Installer.ps1') -Root $root

# Das Manifest zum ZIP oben (Prüfsumme); gilt nur, solange genau dieses ZIP hochgeladen wird.
& (Join-Path $root 'installer\New-Manifest.ps1') -Root $root
