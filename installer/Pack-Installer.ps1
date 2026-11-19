#Requires -Version 7.0
<#
.SYNOPSIS
    Packt den NSTN-Installer aus dist\NSTN: dist\NSTN-Installer-<Version>.zip.

.DESCRIPTION
    Inhalt: die Plugin-Dateien (NSTN\), ihre Prüfsummen (SHA256SUMS.txt), Install-NSTN.ps1,
    Install-NSTN.cmd, Uninstall-NSTN.cmd und LIESMICH.txt. Der Installer läuft mit dem Windows
    PowerShell 5.1 jedes Windows 10/11: das Skript bekommt deshalb eine BOM (ohne liest 5.1 es als
    ANSI und zerstört die Umlaute), alle Textdateien CRLF, die .cmd-Dateien nur ASCII.
    Wird von build.ps1 aufgerufen; packt nur, baut nichts.
#>
param(
    [string]$Root = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = 'Stop'

$assemblyInfo = Get-Content -LiteralPath (Join-Path $Root 'src\NINA.Plugin.Nstn\Properties\AssemblyInfo.cs') -Raw -Encoding UTF8
if ($assemblyInfo -notmatch 'AssemblyFileVersion\("([0-9.]+)"\)') { throw 'AssemblyFileVersion nicht gefunden' }
$version = $Matches[1]

$stage = Join-Path $Root 'dist\NSTN'
$dlls = @(Get-ChildItem -LiteralPath $stage -Filter '*.dll' -File)
if ($dlls.Count -ne 3) { throw "dist\NSTN enthält $($dlls.Count) statt 3 DLLs - erst build.ps1" }
$built = (Get-Item -LiteralPath (Join-Path $stage 'NINA.Plugin.Nstn.dll')).VersionInfo.FileVersion
if ($built -ne $version) { throw "dist\NSTN ist $built, AssemblyInfo sagt $version - erst build.ps1" }

$name = "NSTN-Installer-$version"
$work = Join-Path $Root 'dist\installer'
if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
$package = Join-Path $work $name
New-Item -ItemType Directory -Path (Join-Path $package 'NSTN') -Force | Out-Null
$dlls | Copy-Item -Destination (Join-Path $package 'NSTN')

foreach ($file in 'Install-NSTN.ps1', 'Install-NSTN.cmd', 'Uninstall-NSTN.cmd', 'LIESMICH.txt') {
    $text = [IO.File]::ReadAllText((Join-Path $PSScriptRoot $file)) -replace "`r?`n", "`r`n"
    if ($file -like '*.cmd') {
        # cmd.exe liest in der OEM-Codepage: nur ASCII.
        if ($text -match '[^\x00-\x7F]') { throw "installer\$file enthält Zeichen außerhalb von ASCII" }
        [IO.File]::WriteAllText((Join-Path $package $file), $text, (New-Object System.Text.UTF8Encoding($false)))
    } else {
        [IO.File]::WriteAllText((Join-Path $package $file), $text, (New-Object System.Text.UTF8Encoding($true)))
    }
}

$sums = Get-ChildItem -LiteralPath (Join-Path $package 'NSTN') -File | Sort-Object Name |
    ForEach-Object { '{0}  NSTN/{1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
[IO.File]::WriteAllText((Join-Path $package 'SHA256SUMS.txt'), (($sums -join "`r`n") + "`r`n"), (New-Object System.Text.UTF8Encoding($false)))

$zip = Join-Path $Root "dist\$name.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
Compress-Archive -Path $package -DestinationPath $zip
Write-Host "Installer: $zip"
Write-Host "SHA-256: $((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant())"
