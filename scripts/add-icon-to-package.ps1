<#
.SYNOPSIS
    Puts images/icon.png into the packaged plugin archive.

.DESCRIPTION
    DalamudPackager enumerates the output folder without descending into it, so a subfolder never
    reaches the archive: it copies the icon next to latest.zip and leaves it out of it. The developer
    build then has an icon and every installed copy shows Dalamud's default instead.

    Written with the archive API rather than Compress-Archive because the entry name matters. Windows
    PowerShell writes 'images\icon.png' with a backslash, which is not a zip path separator, and an
    extractor is then free to produce one file with a backslash in its name rather than a folder.

.PARAMETER Archive
    The packaged latest.zip.

.PARAMETER Icon
    The icon file to add.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $Archive,
    [Parameter(Mandatory = $true)][string] $Icon
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Archive)) { throw "No archive at '$Archive'." }
if (-not (Test-Path -LiteralPath $Icon)) { throw "No icon at '$Icon'." }

Add-Type -AssemblyName System.IO.Compression.FileSystem

$entryName = 'images/icon.png'
$zip = [System.IO.Compression.ZipFile]::Open((Resolve-Path -LiteralPath $Archive).Path, 'Update')
try {
    # A rebuild recreates the archive, but never assume it: two entries of the same name is a valid
    # zip and an unpredictable extraction.
    $existing = $zip.Entries | Where-Object { $_.FullName -eq $entryName }
    foreach ($entry in @($existing)) { $entry.Delete() }

    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $zip, (Resolve-Path -LiteralPath $Icon).Path, $entryName) | Out-Null
}
finally {
    $zip.Dispose()
}

Write-Output "added $entryName to $Archive"
