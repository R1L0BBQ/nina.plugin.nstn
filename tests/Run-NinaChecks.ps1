<#
.SYNOPSIS
    Prüft das NSTN-Plugin gegen die Klassen des installierten N.I.N.A. 3.2.
.DESCRIPTION
    Ergänzt build.ps1: Die Kerntests laufen im Linux-Container, diese Prüfungen brauchen
    N.I.N.A.s eigene DLLs (Fabrik, JSON-Konverter, Drop-Befehl, Sequencer.Validate,
    SequentialStrategy mit den echten Triggern) und laufen deshalb unter Windows.
      1. Prüfprogramm im SDK-Container bauen (self-contained, win-x64)
      2. Die NINA.*.dll des installierten N.I.N.A. darüberkopieren (das NuGet-Paket enthält
         andere Builds), dazu External\ (NOVAS, SOFA, JPL-Ephemeriden)
      3. Prüfprogramm starten. N.I.N.A.s Logger, die Datenbank (%LOCALAPPDATA%) und alle
         Ausgaben liegen in einem Wegwerfordner, nie im echten N.I.N.A.-Profil.
    Exit-Code 0 = alle Prüfungen grün. Dauer etwa 2 Minuten, mit -NoRuns etwa 20 Sekunden.
.PARAMETER NinaDir
    Installationsordner von N.I.N.A. 3.2.0.9001.
.PARAMETER NoRuns
    Ohne die Läufe mit Stub-Geräten.
.PARAMETER OutDir
    Wegwerfordner für Ausgaben, Laufprotokolle und N.I.N.A.s Daten (Standard: neuer Ordner unter %TEMP%).
.PARAMETER WriteGolden
    Schreibt tests\NINA.Plugin.Nstn.Checks\golden\filterplan-3.2.json neu und prüft nichts.
    Nur nach einer gewollten Änderung am Sequenzformat; die Änderung im Diff ansehen.
.PARAMETER WriteSequence
    Schreibt die Beispielsequenz zum Teilen (samples\NSTN-LRGB-Ha.json) neu und prüft nur,
    dass N.I.N.A. sie ohne unbekannte Typen lädt und unverändert wieder speichert.
#>
param(
    [string]$NinaDir = "C:\Program Files\N.I.N.A. - Nighttime Imaging 'N' Astronomy",
    [switch]$NoRuns,
    [string]$OutDir = '',
    [switch]$WriteGolden,
    [switch]$WriteSequence
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = 'tests/NINA.Plugin.Nstn.Checks/NINA.Plugin.Nstn.Checks.csproj'
$publish = Join-Path $root 'tests\NINA.Plugin.Nstn.Checks\bin\checks'
$golden = Join-Path $root 'tests\NINA.Plugin.Nstn.Checks\golden\filterplan-3.2.json'

$ninaExe = Join-Path $NinaDir 'NINA.exe'
if (-not (Test-Path -LiteralPath $ninaExe)) { throw "N.I.N.A. nicht gefunden: $ninaExe" }
$version = (Get-Item -LiteralPath $ninaExe).VersionInfo.FileVersion
if ($version -ne '3.2.0.9001') { throw "Die Prüfungen sind für N.I.N.A. 3.2.0.9001 gebaut, installiert ist $version." }

docker run --rm -v "${root}:/src" -v nstn-nuget:/root/.nuget/packages -w /src mcr.microsoft.com/dotnet/sdk:10.0 `
    sh -c "dotnet publish $project -c Release --nologo -v q -o tests/NINA.Plugin.Nstn.Checks/bin/checks"
if ($LASTEXITCODE -ne 0) { throw 'Build des Prüfprogramms fehlgeschlagen' }

# Die DLLs des installierten N.I.N.A. statt der Paket-Builds.
foreach ($dll in Get-ChildItem -LiteralPath $publish -Filter 'NINA.*.dll' -File) {
    $installed = Join-Path $NinaDir $dll.Name
    if (Test-Path -LiteralPath $installed) { Copy-Item -LiteralPath $installed -Destination $dll.FullName -Force }
}
Copy-Item -LiteralPath (Join-Path $NinaDir 'External') -Destination $publish -Recurse -Force

# Wegwerfordner für alles, was N.I.N.A. und das Prüfprogramm schreiben.
$scratch = if ($OutDir) { $OutDir } else { Join-Path ([IO.Path]::GetTempPath()) ("nstn-checks-" + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
New-Item -ItemType Directory -Path (Join-Path $scratch 'LocalAppData') -Force | Out-Null
$savedLocalAppData = $env:LOCALAPPDATA
$env:LOCALAPPDATA = Join-Path $scratch 'LocalAppData'
try {
    $exe = Join-Path $publish 'NINA.Plugin.Nstn.Checks.exe'
    if ($WriteGolden) {
        & $exe (Join-Path $scratch 'out') write-golden $golden
    } elseif ($WriteSequence) {
        & $exe (Join-Path $scratch 'out') write-sequence (Join-Path $root 'samples\NSTN-LRGB-Ha.json')
    } elseif ($NoRuns) {
        & $exe (Join-Path $scratch 'out') --no-runs
    } else {
        & $exe (Join-Path $scratch 'out')
    }
    $code = $LASTEXITCODE
} finally {
    $env:LOCALAPPDATA = $savedLocalAppData
}
Write-Host "Ausgaben und Laufprotokolle: $scratch"
exit $code
