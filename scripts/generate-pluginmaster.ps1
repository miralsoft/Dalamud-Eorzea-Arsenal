<#
.SYNOPSIS
  Generates the custom Dalamud repository index (pluginmaster.json) for Eorzea Arsenal.
.DESCRIPTION
  Builds a one-entry pluginmaster **array** from the *built* plugin manifest (the one DalamudPackager
  emits into bin/Release, which already carries DalamudApiLevel, AssemblyVersion, Punchline, Tags …)
  and adds the download links pointing at the stable `releases/latest/download/latest.zip` redirect.
  Emitting from the built manifest guarantees the entry carries `DalamudApiLevel` (else Dalamud hides
  the plugin as "outdated"); `-AsArray` guarantees a JSON array (Dalamud rejects a bare object).
  Both this file and the zip are uploaded as release assets, so the workflow never pushes to `main`.
#>
param(
    [string]$Version = "",
    [string]$Tag = "",
    [string]$Repository = $env:GITHUB_REPOSITORY,
    [string]$Manifest = "src/EorzeaArsenalPlugin/bin/Release/EorzeaArsenalPlugin/EorzeaArsenalPlugin.json",
    [string]$Output = "pluginmaster.json"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Repository)) {
    throw "Repository is not set (pass -Repository or set GITHUB_REPOSITORY, e.g. owner/repo)."
}

if (-not (Test-Path $Manifest)) {
    throw "Built manifest not found at '$Manifest'. Run 'dotnet build -c Release' first."
}

# Start from the built manifest so every field DalamudPackager produced (DalamudApiLevel,
# AssemblyVersion, Punchline, Tags, CategoryTags, …) carries through unchanged.
$m = Get-Content $Manifest -Raw | ConvertFrom-Json

# Stable redirect to the newest published release's asset (independent of the tag name).
$download = "https://github.com/$Repository/releases/latest/download/latest.zip"
$m | Add-Member -NotePropertyName DownloadLinkInstall -NotePropertyValue $download -Force
$m | Add-Member -NotePropertyName DownloadLinkUpdate  -NotePropertyValue $download -Force
$m | Add-Member -NotePropertyName DownloadLinkTesting -NotePropertyValue $download -Force
$m | Add-Member -NotePropertyName AcceptsFeedback     -NotePropertyValue $true     -Force

# -AsArray: Dalamud's plugin master must be a JSON array, even with a single entry.
@($m) | ConvertTo-Json -Depth 10 -AsArray | Set-Content -Path $Output -Encoding utf8
Write-Host "Wrote $Output (1 entry, AssemblyVersion=$($m.AssemblyVersion), DalamudApiLevel=$($m.DalamudApiLevel))."
