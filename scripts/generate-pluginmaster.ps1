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

# Dalamud's plugin master must be a JSON array, even with a single entry. PowerShell 5.1 has no
# `-AsArray` and collapses a one-element array to a bare object, so wrap it manually when needed
# (works on both Windows PowerShell 5.1 and pwsh 7+). Write UTF-8 *without* BOM — Dalamud's parser
# rejects a leading BOM (Set-Content -Encoding utf8 adds one on 5.1).
$json = @($m) | ConvertTo-Json -Depth 10
if ($json.TrimStart().StartsWith('{')) {
    $json = "[$([Environment]::NewLine)$json$([Environment]::NewLine)]"
}
[System.IO.File]::WriteAllText(
    (Join-Path (Get-Location) $Output),
    $json,
    (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Wrote $Output (1 entry, AssemblyVersion=$($m.AssemblyVersion), DalamudApiLevel=$($m.DalamudApiLevel))."
