#Requires -Version 7.0
<#
.SYNOPSIS
    Schreibt dist\manifest.json für das Plugin-Repository von N.I.N.A. (isbeorn/nina.plugin.manifests).

.DESCRIPTION
    Alle Angaben kommen aus den Attributen der gebauten dist\NSTN\NINA.Plugin.Nstn.dll (wie
    tools\CreateManifest.ps1 im Manifest-Repo), dazu die Prüfsumme von
    dist\NINA.Plugin.Nstn-<Version>.zip und dessen Download-Adresse im GitHub-Release v<Major.Minor.Patch>.
    Prüft die Grenzen aus manifest.schema.json. Das Manifest gilt nur für genau dieses ZIP: nach
    jedem neuen build.ps1 hat das ZIP eine andere Prüfsumme.
    Wird von build.ps1 aufgerufen.
#>
param(
    [string]$Root = (Split-Path $PSScriptRoot -Parent),
    [string]$ReleaseBase = 'https://github.com/R1L0BBQ/nina.plugin.nstn/releases/download'
)

$ErrorActionPreference = 'Stop'

$dll = Join-Path $Root 'dist\NSTN\NINA.Plugin.Nstn.dll'
# Aus dem Speicher laden: die DLL in dist bleibt frei für den nächsten Build.
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($dll))
$attributes = $assembly.GetCustomAttributesData()
function Get-Attribute([string]$type) {
    ($attributes | Where-Object { $_.AttributeType.Name -eq $type } | Select-Object -First 1).ConstructorArguments[0].Value
}
$metadata = @{}
foreach ($a in $attributes | Where-Object { $_.AttributeType.Name -eq 'AssemblyMetadataAttribute' }) {
    $metadata[$a.ConstructorArguments[0].Value] = $a.ConstructorArguments[1].Value
}
function ConvertTo-VersionObject([string]$text) {
    $parts = $text.Split('.')
    if ($parts.Count -ne 4) { throw "Version '$text' hat nicht vier Teile" }
    [ordered]@{ Major = $parts[0]; Minor = $parts[1]; Patch = $parts[2]; Build = $parts[3] }
}

$version = Get-Attribute 'AssemblyFileVersionAttribute'
$tag = 'v' + (($version.Split('.') | Select-Object -First 3) -join '.')
$zipName = "NINA.Plugin.Nstn-$version.zip"
$zip = Join-Path $Root "dist\$zipName"
if (-not (Test-Path -LiteralPath $zip)) { throw "$zipName fehlt - erst build.ps1" }

$manifest = [ordered]@{
    Name                      = Get-Attribute 'AssemblyTitleAttribute'
    Identifier                = Get-Attribute 'GuidAttribute'
    Version                   = ConvertTo-VersionObject $version
    Author                    = Get-Attribute 'AssemblyCompanyAttribute'
    Homepage                  = $metadata['Homepage']
    Repository                = $metadata['Repository']
    License                   = $metadata['License']
    LicenseURL                = $metadata['LicenseURL']
    ChangelogURL              = $metadata['ChangelogURL']
    Tags                      = @($metadata['Tags'].Split(','))
    MinimumApplicationVersion = ConvertTo-VersionObject $metadata['MinimumApplicationVersion']
    Descriptions              = [ordered]@{
        ShortDescription = Get-Attribute 'AssemblyDescriptionAttribute'
        LongDescription  = $metadata['LongDescription']
        FeaturedImageURL = $metadata['FeaturedImageURL']
        ScreenshotURL    = $metadata['ScreenshotURL']
        AltScreenshotURL = $metadata['AltScreenshotURL']
    }
    Installer                 = [ordered]@{
        URL          = "$ReleaseBase/$tag/$zipName"
        Type         = 'ARCHIVE'
        Checksum     = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        ChecksumType = 'SHA256'
    }
}

# Grenzen und Pflichtfelder aus manifest.schema.json.
$problems = @()
foreach ($key in 'Name', 'Identifier', 'Author', 'Repository', 'License', 'LicenseURL') {
    if ([string]::IsNullOrWhiteSpace($manifest[$key])) { $problems += "$key fehlt" }
}
if ($manifest.Name.Length -gt 50) { $problems += 'Name länger als 50' }
if ($manifest.Author.Length -gt 256) { $problems += 'Author länger als 256' }
if ($manifest.License.Length -gt 512) { $problems += 'License länger als 512' }
if ($manifest.Identifier -notmatch '^[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$') { $problems += 'Identifier ist keine GUID' }
if ($manifest.Tags.Count -gt 20) { $problems += 'mehr als 20 Tags' }
foreach ($t in $manifest.Tags) { if ($t.Length -lt 1 -or $t.Length -gt 30) { $problems += "Tag '$t' nicht 1-30 Zeichen" } }
if ([string]::IsNullOrWhiteSpace($manifest.Descriptions.ShortDescription) -or $manifest.Descriptions.ShortDescription.Length -gt 256) { $problems += 'ShortDescription fehlt oder länger als 256' }
if ($manifest.Descriptions.LongDescription.Length -gt 10000) { $problems += "LongDescription hat $($manifest.Descriptions.LongDescription.Length) statt höchstens 10000 Zeichen" }
if ($problems) { throw "Manifest verletzt das Schema: $($problems -join '; ')" }

$out = Join-Path $Root 'dist\manifest.json'
[IO.File]::WriteAllText($out, ($manifest | ConvertTo-Json -Depth 5) + "`n", (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Manifest: $out ($($manifest.Name) $version, LongDescription $($manifest.Descriptions.LongDescription.Length) Zeichen)"
Write-Host "Release:  $tag mit $zipName (SHA-256 $($manifest.Installer.Checksum))"
