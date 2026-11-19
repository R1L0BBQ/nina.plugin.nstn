<#
.SYNOPSIS
    Installiert das N.I.N.A.-Plugin NSTN für den angemeldeten Benutzer (oder entfernt es).

.DESCRIPTION
    Für N.I.N.A. 3.2 und 3.3. Ablauf:
      1. N.I.N.A. muss geschlossen sein (sie hält die Plugin-Dateien offen).
      2. Prüfsummen des Pakets prüfen (SHA256SUMS.txt) - ein beschädigtes Paket wird nicht installiert.
      3. Eine vorhandene NSTN-Version sichern, nach %LOCALAPPDATA%\NINA\NSTN-Sicherung\<Zeitpunkt>.
         Außerhalb des Plugin-Ordners, sonst lädt N.I.N.A. die Sicherung als zweites Plugin.
      4. Den Plugin-Ordner leeren und die neuen Dateien hineinkopieren.
      5. Prüfsummen der installierten Dateien prüfen.
    Keine Administratorrechte nötig. Die NSTN-Einstellungen (SQM, Schwellen, Filter) liegen im
    N.I.N.A.-Profil und bleiben bei Installation und Deinstallation erhalten.

.PARAMETER Uninstall
    Entfernt das Plugin (vorher gesichert wie oben).

.PARAMETER PluginDir
    Anderer Zielordner, etwa für eine portable N.I.N.A.
    Standard: %LOCALAPPDATA%\NINA\Plugins\3.0.0\NSTN

.PARAMETER NoPause
    Am Ende nicht auf Enter warten (für Aufrufe aus Skripten).

.EXAMPLE
    Doppelklick auf Install-NSTN.cmd im entpackten Ordner.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Install-NSTN.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [switch]$Uninstall,
    [string]$PluginDir = '',
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
$MinimumNina = [version]'3.2.0.9001'
$PluginDll = 'NINA.Plugin.Nstn.dll'

function Finish([int]$Code) {
    if (-not $NoPause) {
        Write-Host ''
        Read-Host 'Enter zum Beenden' | Out-Null
    }
    exit $Code
}

function Fail([string]$Message) {
    Write-Host ''
    Write-Host "FEHLER: $Message" -ForegroundColor Red
    Finish 1
}

# Mit .NET statt Get-FileHash: Aus einem PowerShell-7-Fenster gestartet, erbt Windows PowerShell
# dessen Modulpfad und findet Get-FileHash nicht.
function Hash([string]$Path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try {
        return (($sha.ComputeHash($stream) | ForEach-Object { $_.ToString('x2') }) -join '')
    } finally {
        $stream.Dispose()
        $sha.Dispose()
    }
}

function VersionOf([string]$Folder) {
    $dll = Join-Path $Folder $PluginDll
    if (Test-Path -LiteralPath $dll) { (Get-Item -LiteralPath $dll).VersionInfo.FileVersion } else { $null }
}

# Sichert den vorhandenen Plugin-Ordner außerhalb von Plugins\ und gibt den Sicherungsordner zurück.
function Backup([string]$Folder) {
    $version = VersionOf $Folder
    if (-not $version) { $version = 'unbekannt' }
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $target = Join-Path $env:LOCALAPPDATA "NINA\NSTN-Sicherung\$stamp-$version"
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Copy-Item -Path (Join-Path $Folder '*') -Destination $target -Recurse -Force
    return $target
}

try {
    $package = $PSScriptRoot
    $source = Join-Path $package 'NSTN'
    if (-not $PluginDir) {
        $PluginDir = Join-Path $env:LOCALAPPDATA 'NINA\Plugins\3.0.0\NSTN'
    }

    Write-Host 'NSTN - Narrowband Saves The Night: Plugin für N.I.N.A.' -ForegroundColor Cyan
    Write-Host "Zielordner: $PluginDir"

    # 1. N.I.N.A. muss geschlossen sein.
    $running = @(Get-Process -Name 'NINA' -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        Fail "N.I.N.A. läuft (Prozess $(($running | ForEach-Object { $_.Id }) -join ', ')). Bitte N.I.N.A. schließen und den Installer erneut starten."
    }

    # N.I.N.A.-Version, nur zur Information: eine portable oder anders installierte N.I.N.A. findet er nicht.
    $ninaExe = Join-Path $env:ProgramFiles "N.I.N.A. - Nighttime Imaging 'N' Astronomy\NINA.exe"
    if (Test-Path -LiteralPath $ninaExe) {
        $ninaVersion = [version](Get-Item -LiteralPath $ninaExe).VersionInfo.FileVersion
        Write-Host "N.I.N.A. gefunden: $ninaVersion"
        if ($ninaVersion -lt $MinimumNina) {
            Write-Host "Achtung: NSTN braucht N.I.N.A. $MinimumNina oder neuer; N.I.N.A. lädt das Plugin sonst nicht." -ForegroundColor Yellow
        }
    } else {
        Write-Host 'N.I.N.A. nicht unter Programme gefunden (portable oder anderer Ort?) - die Installation geht trotzdem.' -ForegroundColor Yellow
    }

    if ($Uninstall) {
        if (-not (Test-Path -LiteralPath $PluginDir)) {
            Write-Host 'NSTN ist nicht installiert - nichts zu tun.'
            Finish 0
        }
        $old = VersionOf $PluginDir
        $saved = Backup $PluginDir
        Remove-Item -LiteralPath $PluginDir -Recurse -Force
        Write-Host ''
        Write-Host "NSTN $old entfernt." -ForegroundColor Green
        Write-Host "Gesichert in: $saved"
        Write-Host 'Die NSTN-Einstellungen im N.I.N.A.-Profil bleiben erhalten.'
        Finish 0
    }

    # 2. Paket prüfen.
    $sumsFile = Join-Path $package 'SHA256SUMS.txt'
    if (-not (Test-Path -LiteralPath $sumsFile) -or -not (Test-Path -LiteralPath $source)) {
        Fail 'Paket unvollständig (NSTN-Ordner oder SHA256SUMS.txt fehlt). Bitte das ZIP vollständig entpacken und Install-NSTN.cmd aus dem entpackten Ordner starten.'
    }
    $expected = @{}
    foreach ($line in Get-Content -LiteralPath $sumsFile) {
        if ($line -match '^([0-9a-f]{64})\s+NSTN/(\S+)$') {
            $expected[$Matches[2]] = $Matches[1]
        }
    }
    if ($expected.Count -eq 0 -or -not $expected.ContainsKey($PluginDll)) {
        Fail 'SHA256SUMS.txt ist unlesbar.'
    }
    foreach ($name in $expected.Keys) {
        $file = Join-Path $source $name
        if (-not (Test-Path -LiteralPath $file)) {
            Fail "Im Paket fehlt NSTN\$name."
        }
        if ((Hash $file) -ne $expected[$name]) {
            Fail "NSTN\$name im Paket ist beschädigt (Prüfsumme stimmt nicht). Bitte das ZIP neu kopieren."
        }
    }
    $new = VersionOf $source
    Write-Host "Paket geprüft: NSTN $new, $($expected.Count) Dateien."

    # 3. Vorhandene Version sichern.
    if (Test-Path -LiteralPath $PluginDir) {
        $old = VersionOf $PluginDir
        $saved = Backup $PluginDir
        Write-Host "Vorhandene Version $old gesichert in: $saved"
        Remove-Item -Path (Join-Path $PluginDir '*') -Recurse -Force
    } else {
        New-Item -ItemType Directory -Path $PluginDir -Force | Out-Null
    }

    # 4. Kopieren. Unblock-File nimmt die Internet-Markierung eines heruntergeladenen ZIPs weg;
    # N.I.N.A. (.NET 8) lädt die Dateien auch mit Markierung, deshalb ist ein Fehlschlag kein Abbruch.
    foreach ($name in $expected.Keys) {
        $target = Join-Path $PluginDir $name
        Copy-Item -LiteralPath (Join-Path $source $name) -Destination $target -Force
        try { Unblock-File -LiteralPath $target } catch { }
    }

    # 5. Installierte Dateien prüfen.
    foreach ($name in $expected.Keys) {
        if ((Hash (Join-Path $PluginDir $name)) -ne $expected[$name]) {
            Fail "Nach dem Kopieren stimmt die Prüfsumme von $name nicht. Die gesicherte Version liegt unter %LOCALAPPDATA%\NINA\NSTN-Sicherung."
        }
    }

    Write-Host ''
    Write-Host "NSTN $new installiert." -ForegroundColor Green
    Write-Host 'N.I.N.A. starten: Unter Plugins > Installiert steht dann „NSTN – Narrowband Saves The Night“.'
    Write-Host 'Beim ersten Mal unter Optionen > Plugins > NSTN die Himmelsqualität des Standorts (SQM) eintragen.'
    Finish 0
} catch {
    Fail $_.Exception.Message
}
