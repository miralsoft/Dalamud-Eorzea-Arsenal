<#
.SYNOPSIS
  Generates the custom Dalamud repository index (pluginmaster.json) for Eorzea Arsenal.
.DESCRIPTION
  Reads the built plugin manifest and emits a one-entry pluginmaster array whose download links
  point at the stable `releases/latest/download/latest.zip` redirect (always the newest release).
  Both this file and the zip are uploaded as release assets, so the workflow never has to push to
  `main` and the public custom-repo URL stays constant. Run by the release workflow on a `v*` tag.
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Tag,
    [string]$Repository = $env:GITHUB_REPOSITORY,
    [string]$Manifest = "src/EorzeaArsenalPlugin/EorzeaArsenalPlugin.json",
    [string]$Output = "pluginmaster.json"
)

$ErrorActionPreference = "Stop"

$m = Get-Content $Manifest -Raw | ConvertFrom-Json
# Stable redirect to the newest published release's asset (independent of the tag name), so older
# manifests never point at a stale zip and the workflow needs no write access to the repo.
$download = "https://github.com/$Repository/releases/latest/download/latest.zip"

$entry = [ordered]@{
    Author              = $m.Author
    Name                = $m.Name
    InternalName        = "EorzeaArsenalPlugin"
    AssemblyVersion     = $Version
    Description         = $m.Description
    Punchline           = $m.Punchline
    ApplicableVersion   = $m.ApplicableVersion
    RepoUrl             = $m.RepoUrl
    Tags                = $m.Tags
    CategoryTags        = $m.CategoryTags
    AcceptsFeedback     = $true
    DownloadLinkInstall = $download
    DownloadLinkUpdate  = $download
    DownloadLinkTesting = $download
}

@($entry) | ConvertTo-Json -Depth 8 | Set-Content -Path $Output -Encoding utf8
Write-Host "Wrote $Output for $Version ($Tag)."
