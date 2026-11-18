<#
.SYNOPSIS
    Legt einen Testordner für eine abgeschottete N.I.N.A.-Instanz an.
.DESCRIPTION
    Kopiert ein Profil (nur lesend) und stellt die Kopie auf die laufenden Alpaca-Simulatoren um
    (Start-Simulators.ps1). Montierung, Filterrad und Fokussierer kommen von dort, Guider ist
    „Mount Dither“, die Kamera bleibt die des Profils (für Tests: N.I.N.A.-Simulatorkamera).
    Für kurze Testläufe: Brennweite (sonst kann Mount Dither keine Pulsdauer rechnen), Autofokus
    mit 1 s und wenigen Punkten, Meridian-Flip ohne Zentrieren, Bilder und Sequenzen im Testordner.
    Das Plugin kommt aus dist\NSTN (build.ps1).
    Mit -UpdateDevices werden in einem bestehenden Testordner nur die Geräte-IDs erneuert
    (ein neuer Simulator-Container vergibt neue).
.PARAMETER TestRoot
    Neuer Testordner (darf nicht existieren, außer mit -UpdateDevices).
.PARAMETER SourceProfile
    Profildatei, die kopiert wird. Standard: das zuletzt geänderte Profil in %LOCALAPPDATA%\NINA\Profiles.
.PARAMETER FocalLength
    Brennweite in mm, falls das Profil keine hat.
#>
param(
    [Parameter(Mandatory)] [string]$TestRoot,
    [string]$SourceProfile = '',
    [double]$FocalLength = 749,
    [switch]$UpdateDevices
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$data = Join-Path $TestRoot 'LocalAppData\NINA'

$devices = (Invoke-RestMethod http://localhost:32323/management/v1/configureddevices -TimeoutSec 5).Value | ForEach-Object { $_ }
function DeviceOf([string]$type) {
    $device = $devices | Where-Object DeviceType -eq $type | Select-Object -First 1
    if (-not $device) { throw "Simulator ohne $type – läuft Start-Simulators.ps1?" }
    $device
}
$telescope = DeviceOf 'Telescope'
$wheel = DeviceOf 'FilterWheel'
$focuser = DeviceOf 'Focuser'

function Set-ProfileValue([xml]$xml, $ns, [string]$path, [string]$value) {
    $node = $xml.DocumentElement.SelectSingleNode($path, $ns)
    if (-not $node) { throw "Profil ohne $path" }
    $node.InnerText = $value
}

function Set-Devices([string]$profilePath, [switch]$All) {
    [xml]$xml = Get-Content -LiteralPath $profilePath -Raw
    $ns = New-Object Xml.XmlNamespaceManager $xml.NameTable
    $ns.AddNamespace('a', $xml.DocumentElement.NamespaceURI)
    Set-ProfileValue $xml $ns 'a:TelescopeSettings/a:Id' $telescope.UniqueID
    Set-ProfileValue $xml $ns 'a:TelescopeSettings/a:LastDeviceName' $telescope.DeviceName
    Set-ProfileValue $xml $ns 'a:FilterWheelSettings/a:Id' $wheel.UniqueID
    Set-ProfileValue $xml $ns 'a:FilterWheelSettings/a:LastDeviceName' $wheel.DeviceName
    Set-ProfileValue $xml $ns 'a:FocuserSettings/a:Id' $focuser.UniqueID
    Set-ProfileValue $xml $ns 'a:FocuserSettings/a:LastDeviceName' $focuser.DeviceName
    if ($All) {
        $name = $xml.DocumentElement.SelectSingleNode('a:Name', $ns)
        $name.InnerText = "$($name.InnerText) (Test)"
        Set-ProfileValue $xml $ns 'a:TelescopeSettings/a:TelescopeLocationSyncDirection' 'TOTELESCOPE'
        $focal = $xml.DocumentElement.SelectSingleNode('a:TelescopeSettings/a:FocalLength', $ns)
        if ($focal.InnerText -eq 'NaN' -or -not $focal.InnerText) {
            $focal.InnerText = $FocalLength.ToString([Globalization.CultureInfo]::InvariantCulture)
        }
        Set-ProfileValue $xml $ns 'a:GuiderSettings/a:GuiderName' 'Direct_Guider'
        Set-ProfileValue $xml $ns 'a:GuiderSettings/a:LastDeviceName' 'Mount Dither'
        Set-ProfileValue $xml $ns 'a:GuiderSettings/a:SettleTime' '2'
        Set-ProfileValue $xml $ns 'a:FocuserSettings/a:AutoFocusExposureTime' '1'
        Set-ProfileValue $xml $ns 'a:FocuserSettings/a:AutoFocusInitialOffsetSteps' '3'
        Set-ProfileValue $xml $ns 'a:MeridianFlipSettings/a:Recenter' 'false'
        Set-ProfileValue $xml $ns 'a:MeridianFlipSettings/a:SettleTime' '5'
        Set-ProfileValue $xml $ns 'a:ImageFileSettings/a:FilePath' (Join-Path $TestRoot 'Images')
        Set-ProfileValue $xml $ns 'a:SequenceSettings/a:DefaultSequenceFolder' (Join-Path $TestRoot 'Sequences')
        Set-ProfileValue $xml $ns 'a:SequenceSettings/a:SequencerTargetsFolder' (Join-Path $TestRoot 'Sequences\Targets')
        Set-ProfileValue $xml $ns 'a:SequenceSettings/a:SequencerTemplatesFolder' (Join-Path $TestRoot 'Sequences\Templates')
    }
    $xml.Save($profilePath)
}

if ($UpdateDevices) {
    $profiles = Get-ChildItem -LiteralPath (Join-Path $data 'Profiles') -Filter '*.profile'
    foreach ($profile in $profiles) { Set-Devices $profile.FullName }
    "Geräte-IDs erneuert in: $($profiles.Name -join ', ')"
    return
}

if (Test-Path -LiteralPath $TestRoot) { throw "$TestRoot gibt es schon" }
if (-not $SourceProfile) {
    $SourceProfile = (Get-ChildItem -LiteralPath (Join-Path $env:LOCALAPPDATA 'NINA\Profiles') -Filter '*.profile' |
        Sort-Object LastWriteTime | Select-Object -Last 1).FullName
}
$plugin = Join-Path $repo 'dist\NSTN'
if ((Get-ChildItem -LiteralPath $plugin -Filter '*.dll' -ErrorAction SilentlyContinue).Count -ne 3) { throw "Erst build.ps1 laufen lassen ($plugin)" }

foreach ($dir in 'Profiles', 'Plugins\3.0.0\NSTN') { New-Item -ItemType Directory -Force -Path (Join-Path $data $dir) | Out-Null }
foreach ($dir in 'Images', 'Sequences') { New-Item -ItemType Directory -Force -Path (Join-Path $TestRoot $dir) | Out-Null }
Copy-Item -Path (Join-Path $plugin '*.dll') -Destination (Join-Path $data 'Plugins\3.0.0\NSTN')
$copy = Join-Path $data "Profiles\$(Split-Path $SourceProfile -Leaf)"
Copy-Item -LiteralPath $SourceProfile -Destination $copy
Set-Devices $copy -All
"Testordner $TestRoot angelegt; Profil kopiert aus $SourceProfile (Original unverändert)"
