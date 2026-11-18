<#
.SYNOPSIS
    Startet eine abgeschottete N.I.N.A.-Testinstanz.
.DESCRIPTION
    Profile, Plugins, Logs, Datenbank und Caches liegen in <TestRoot>\LocalAppData\NINA. Die
    Anwendungseinstellungen bleiben im Speicher (NinaTestHook). Das installierte N.I.N.A. und
    %LOCALAPPDATA%\NINA werden nur gelesen (Programmdateien), nie geschrieben. Die Testinstanz
    läuft deshalb auch neben einem geöffneten N.I.N.A.
    Der Prozess wird per WMI gestartet und überlebt so die aufrufende Shell.
    Den Testordner legt New-NinaTestRoot.ps1 an.
.PARAMETER NinaExe
    NINA.exe – die installierte 3.2 oder ein ausgepacktes 3.3.
.PARAMETER TestRoot
    Testordner (mit LocalAppData\NINA darin).
.PARAMETER SequenceFile
    Sequenz, die beim Start geladen wird (--sequencefile).
.PARAMETER Run
    Die geladene Sequenz sofort starten (--runsequence, ohne Rückfrage).
.PARAMETER NstnClockUtc
    NSTN-Uhr ab diesem Zeitpunkt laufen lassen (UTC, z. B. 2026-11-19T04:40:00Z), damit tagsüber
    Fenster offen sind. Nur NSTN rechnet damit; N.I.N.A., Montierung und Meridian-Flip nutzen die echte Zeit.
.PARAMETER RebuildHook
    NinaTestHook neu bauen (nach einer Änderung an StartupHook.cs).
.EXAMPLE
    .\Start-NinaTest.ps1 -NinaExe "C:\Program Files\N.I.N.A. - Nighttime Imaging 'N' Astronomy\NINA.exe" `
        -TestRoot C:\Temp\nina-test\3.2 -SequenceFile C:\Temp\nina-test\flip.json -Run
#>
param(
    [Parameter(Mandatory)] [string]$NinaExe,
    [Parameter(Mandatory)] [string]$TestRoot,
    [string]$SequenceFile = '',
    [switch]$Run,
    [string]$NstnClockUtc = '',
    [switch]$RebuildHook
)
$ErrorActionPreference = 'Stop'

$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$hook = Join-Path $PSScriptRoot 'NinaTestHook\bin\hook\NinaTestHook.dll'
if ($RebuildHook -or -not (Test-Path -LiteralPath $hook)) {
    docker run --rm -v "${repo}:/src" -v nstn-nuget:/root/.nuget/packages -w /src mcr.microsoft.com/dotnet/sdk:10.0 `
        sh -c 'dotnet build tests/NinaTestInstance/NinaTestHook/NinaTestHook.csproj -c Release --nologo -v q -o tests/NinaTestInstance/NinaTestHook/bin/hook'
    if ($LASTEXITCODE -ne 0) { throw 'Build des NinaTestHook fehlgeschlagen' }
}
$local = Join-Path $TestRoot 'LocalAppData'
$data = Join-Path $local 'NINA'
foreach ($path in $NinaExe, $hook, $data) {
    if (-not (Test-Path -LiteralPath $path)) { throw "fehlt: $path" }
}

# cmd setzt die Umgebung nur für N.I.N.A. und übergibt mit "start".
$arguments = if ($SequenceFile) { " --sequencefile ""$SequenceFile""" } else { '' }
if ($Run) { $arguments += ' --runsequence' }
$clock = if ($NstnClockUtc) { " && set ""NINA_TEST_NSTN_CLOCK=$NstnClockUtc""" } else { '' }
$cmd = "cmd.exe /c set ""DOTNET_STARTUP_HOOKS=$hook"" && set ""NINA_TEST_DATA=$data"" && set ""LOCALAPPDATA=$local""" +
       "$clock && start """" ""$NinaExe""$arguments"
$before = @(Get-Process NINA -ErrorAction SilentlyContinue | ForEach-Object Id)
$result = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
    CommandLine = $cmd; CurrentDirectory = (Split-Path $NinaExe) }
if ($result.ReturnValue -ne 0) { throw "Win32_Process.Create meldet $($result.ReturnValue)" }

# Der neue N.I.N.A.-Prozess, nicht das cmd, das ihn gestartet hat.
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 500
    $new = Get-Process NINA -ErrorAction SilentlyContinue | Where-Object { $before -notcontains $_.Id }
    if ($new) { break }
}
if (-not $new) { throw 'kein neuer NINA-Prozess' }
"Testinstanz PID $($new.Id), Daten in $data (Protokoll des Hooks: $data\nina-test-hook.log)"
