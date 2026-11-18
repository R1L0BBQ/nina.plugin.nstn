<#
.SYNOPSIS
    Startet die ASCOM Alpaca Omni-Simulatoren in einem Docker-Container.
.DESCRIPTION
    Montierung, Filterrad, Fokussierer und weitere Geräte zum Testen, ohne ASCOM-Plattform und
    ohne Installation. N.I.N.A. findet sie über die eigene Alpaca-Suche (UDP 32227) und spricht
    sie über HTTP 32323 an.
    Vorher einmal das offizielle Linux-Build herunterladen und auspacken:
    https://github.com/ASCOMInitiative/ASCOM.Alpaca.Simulators/releases (getestet: v0.4.0,
    ascom.alpaca.simulators.linux-x64.tar.xz).
    Die Simulatoren sind auch über die LAN-Adresse des PCs erreichbar. Nach dem Test mit -Stop beenden.
    Ein neuer Container vergibt neue Geräte-IDs; New-NinaTestRoot.ps1 liest sie danach ein.
.PARAMETER SimulatorDir
    Ausgepackter Ordner ascom.alpaca.simulators.linux-x64.
.PARAMETER Stop
    Container anhalten und entfernen.
#>
param(
    [string]$SimulatorDir = '',
    [switch]$Stop
)
$ErrorActionPreference = 'Stop'
$name = 'nstn-omnisim'

if ($Stop) {
    docker stop $name | Out-Null
    docker container rm $name | Out-Null
    "Container $name entfernt"
    return
}
if (-not $SimulatorDir -or -not (Test-Path -LiteralPath (Join-Path $SimulatorDir 'ascom.alpaca.simulators'))) {
    throw 'SimulatorDir: ausgepackter Ordner ascom.alpaca.simulators.linux-x64 (siehe Beschreibung)'
}
docker run -d --name $name -p 32323:32323 -p 32227:32227/udp -v "${SimulatorDir}:/opt/omnisim:ro" mcr.microsoft.com/dotnet/sdk:10.0 `
    sh -c 'cp -r /opt/omnisim /tmp/sim && chmod +x /tmp/sim/ascom.alpaca.simulators && cd /tmp/sim && exec ./ascom.alpaca.simulators --urls=http://0.0.0.0:32323' | Out-Null

for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    try {
        $devices = (Invoke-RestMethod http://localhost:32323/management/v1/configureddevices -TimeoutSec 2).Value | ForEach-Object { $_ }
        if ($devices) { break }
    } catch { }
}
if (-not $devices) { throw 'Simulatoren antworten nicht' }
$devices | Select-Object DeviceType, DeviceName, UniqueID | Format-Table -AutoSize
